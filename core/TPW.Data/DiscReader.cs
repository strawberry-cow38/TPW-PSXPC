using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TPW.Data
{
    public sealed class DiscFile
    {
        public string Name;          // full path within the disc, e.g. "FOLIO.GAZ"
        public int Lba;              // first logical sector
        public int Length;           // bytes
        public bool IsDirectory;
        public override string ToString() => $"{Name} ({Length:n0} bytes @ lba {Lba})";
    }

    /// <summary>Reads an ISO9660 filesystem out of a PlayStation disc image.
    ///
    /// ⚠ THE SECTOR IS 2352 BYTES, NOT 2048, AND THE PAYLOAD IS NOT AT THE START OF IT. A PSX rip keeps the
    /// raw CD sector: 12 sync bytes, a 4-byte header, and on a Mode 2 disc an 8-byte subheader, before the
    /// 2048 bytes anyone cares about. Reading such an image as a flat 2048-per-sector ISO does not fail --
    /// it silently returns data from the wrong place, shifted 24 bytes and drifting a further 304 every
    /// sector. Both layouts work here and which one is in use is DETECTED, never assumed.
    ///
    /// ⚠ AND THE FORM MATTERS. Mode 2 Form 2 sectors carry 2324 bytes of streaming media with weaker error
    /// correction, and a disc mixes them freely with Form 1 data. On this title ADVISOR.TPW is 346 MB of
    /// Form 2 AUDIO -- two thirds of the whole disc. Anything that walks sectors blindly reads it as files.</summary>
    public sealed class DiscReader : IDisposable
    {
        public const int RawSectorSize = 2352;
        public const int UserDataSize = 2048;

        readonly Stream _s;
        readonly int _sectorSize;    // 2352 raw, or 2048 for an already-cooked iso
        readonly int _dataOffset;    // 24 on Mode 2, 16 on Mode 1, 0 on a cooked iso
        readonly List<DiscFile> _files = new();

        public IReadOnlyList<DiscFile> Files => _files;
        public string VolumeId { get; private set; } = "";
        public bool IsRawSectors => _sectorSize == RawSectorSize;

        DiscReader(Stream s, int sectorSize, int dataOffset)
        {
            _s = s; _sectorSize = sectorSize; _dataOffset = dataOffset;
        }

        public static DiscReader Open(string path) => Open(File.OpenRead(path));

        public static DiscReader Open(Stream s)
        {
            var head = new byte[RawSectorSize];
            s.Position = 0;
            int got = s.Read(head, 0, head.Length);
            if (got < 16) throw new InvalidDataException("File is too small to be a disc image.");

            // The sync pattern 00 FF*10 00 opens every raw CD sector and appears nowhere else at offset 0.
            bool raw = head[0] == 0x00 && head[11] == 0x00;
            for (int i = 1; i <= 10 && raw; i++) if (head[i] != 0xFF) raw = false;

            var r = raw
                ? new DiscReader(s, RawSectorSize, head[15] == 2 ? 24 : 16)
                : new DiscReader(s, UserDataSize, 0);
            r.ReadVolume();
            return r;
        }

        public byte[] ReadSector(int lba)
        {
            var b = new byte[UserDataSize];
            _s.Position = (long)lba * _sectorSize + _dataOffset;
            Fill(b);
            return b;
        }

        /// <summary>The whole 2352-byte sector, or null on a cooked image. The only honest way to tell
        /// streaming media from file data is the Mode 2 subheader, which lives in here.</summary>
        public byte[] ReadRawSector(int lba)
        {
            if (_sectorSize != RawSectorSize) return null;
            var b = new byte[RawSectorSize];
            _s.Position = (long)lba * _sectorSize;
            Fill(b);
            return b;
        }

        /// <summary>True if this sector is Mode 2 Form 2 -- streaming audio or video, not file data. Always
        /// false on a cooked image, where the subheader no longer exists to ask.</summary>
        public bool IsStreamingSector(int lba)
        {
            var raw = ReadRawSector(lba);
            return raw != null && (raw[18] & 0x20) != 0;
        }

        public byte[] ReadFile(DiscFile f)
        {
            var outBuf = new byte[f.Length];
            int sectors = (f.Length + UserDataSize - 1) / UserDataSize;
            for (int i = 0; i < sectors; i++)
            {
                var sec = ReadSector(f.Lba + i);
                int copy = Math.Min(UserDataSize, f.Length - i * UserDataSize);
                if (copy > 0) Buffer.BlockCopy(sec, 0, outBuf, i * UserDataSize, copy);
            }
            return outBuf;
        }

        public DiscFile Find(string name)
        {
            foreach (var f in _files)
                if (!f.IsDirectory && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                    return f;
            return null;
        }

        void Fill(byte[] b)
        {
            int off = 0;
            while (off < b.Length)
            {
                int n = _s.Read(b, off, b.Length - off);
                if (n <= 0) break;
                off += n;
            }
        }

        void ReadVolume()
        {
            var pvd = ReadSector(16);
            if (pvd[0] != 1 || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001")
                throw new InvalidDataException(
                    "No ISO9660 volume descriptor at sector 16 — not a disc image, or the sector layout was misdetected.");
            VolumeId = Encoding.ASCII.GetString(pvd, 40, 32).Trim();
            int rootLba = BitConverter.ToInt32(pvd, 156 + 2);
            int rootLen = BitConverter.ToInt32(pvd, 156 + 10);
            Walk(rootLba, rootLen, "", 0);
        }

        void Walk(int lba, int length, string prefix, int depth)
        {
            if (depth > 8 || length <= 0) return;   // a malformed image must not recurse forever
            int sectors = (length + UserDataSize - 1) / UserDataSize;
            var data = new byte[sectors * UserDataSize];
            for (int i = 0; i < sectors; i++)
                Buffer.BlockCopy(ReadSector(lba + i), 0, data, i * UserDataSize, UserDataSize);

            int off = 0;
            while (off < length)
            {
                int recLen = data[off];
                if (recLen == 0)
                {
                    // A record never straddles a sector boundary, so a zero length means "skip to the next".
                    off = (off / UserDataSize + 1) * UserDataSize;
                    continue;
                }
                if (off + recLen > data.Length || recLen < 33) break;

                int exLba = BitConverter.ToInt32(data, off + 2);
                int exLen = BitConverter.ToInt32(data, off + 10);
                byte flags = data[off + 25];
                int nameLen = data[off + 32];
                if (off + 33 + nameLen > data.Length) break;

                // The . and .. entries are a single byte, 0x00 and 0x01. Compared as bytes rather than as a
                // decoded string: those two code points do not survive a round trip through every encoding.
                bool isDot = nameLen == 1 && data[off + 33] <= 1;
                if (!isDot)
                {
                    string name = Encoding.ASCII.GetString(data, off + 33, nameLen);
                    int sc = name.IndexOf(';');          // strip the ISO version suffix ";1"
                    if (sc >= 0) name = name.Substring(0, sc);
                    bool dir = (flags & 2) != 0;
                    string full = prefix + name;
                    _files.Add(new DiscFile
                    {
                        Name = dir ? full + "/" : full,
                        Lba = exLba,
                        Length = exLen,
                        IsDirectory = dir,
                    });
                    if (dir) Walk(exLba, exLen, full + "/", depth + 1);
                }
                off += recLen;
            }
        }

        public void Dispose() => _s?.Dispose();
    }
}
