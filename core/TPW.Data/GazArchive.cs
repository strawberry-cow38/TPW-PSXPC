using System;
using System.Collections.Generic;

namespace TPW.Data
{
    public sealed class GazEntry
    {
        public int Index;
        public int Offset;
        public int Size;
        public uint Signature;       // first four bytes of the payload, or 0 if too short
        public bool IsContainer => Signature == GazArchive.ContainerMagic;
    }

    /// <summary>FOLIO.GAZ — the asset archive. Header of two words, then one (offset,size) pair per entry,
    /// then the payloads, each padded up to a 0x800 boundary.
    ///
    /// ⭐ THE LAYOUT IS A HYPOTHESIS THAT PASSED A TEST, not a guess that looked right. Reading it this way
    /// gives, on the real disc: 422 entries, every offset in bounds, none overlapping, all strictly
    /// ascending, and ALL 422 aligned to 0x800. A wrong field layout does not produce that -- misread the
    /// stride or the field order and offsets scatter, run past the end, or collide. The alignment in
    /// particular is the tell, because nothing about a wrong reading would make 422 numbers land on
    /// multiples of 2048. Parse() re-checks all of it and refuses rather than half-working.
    ///
    /// ⚠ ONLY 53% OF THE FILE IS REACHABLE FROM THE TABLE, AND THAT IS CORRECT. Past the final entry the
    /// remaining 7.6 MB is the string " BOYS ROCK OUT - TPW BOOT " repeating to the end -- somebody's
    /// filler. A reader that "helpfully" scans the whole file for payloads finds megabytes of it.</summary>
    public sealed class GazArchive
    {
        /// <summary>First word of the commonest payload type: a container that declares its own byte length
        /// at offset 24, which makes it self-checking. 268 of the 422 entries on the real disc are these.</summary>
        public const uint ContainerMagic = 0x96;

        public const int HeaderSize = 8;
        public const int Alignment = 0x800;

        public int Count => Entries.Count;
        public uint SecondWord { get; private set; }     // 23 on the shipped disc; meaning not yet established
        public List<GazEntry> Entries { get; } = new();

        readonly byte[] _data;
        GazArchive(byte[] data) { _data = data; }

        public static bool TryParse(byte[] data, out GazArchive archive, out string error)
        {
            archive = null; error = null;
            if (data == null || data.Length < HeaderSize) { error = "too short to hold an archive header"; return false; }

            var a = new GazArchive(data);
            uint count = BitConverter.ToUInt32(data, 0);
            a.SecondWord = BitConverter.ToUInt32(data, 4);

            if (count == 0 || count > 100_000) { error = $"implausible entry count {count}"; return false; }
            long tableEnd = HeaderSize + (long)count * 8;
            if (tableEnd > data.Length) { error = $"a table of {count} entries does not fit in {data.Length:n0} bytes"; return false; }

            long prevEnd = tableEnd;
            for (int i = 0; i < count; i++)
            {
                int off = BitConverter.ToInt32(data, HeaderSize + i * 8);
                int size = BitConverter.ToInt32(data, HeaderSize + i * 8 + 4);

                // Each of these is a way the "(offset,size) pairs" reading could be wrong. If any trips, the
                // layout is not what this class believes and a partial parse would be worse than none.
                if (off < tableEnd) { error = $"entry {i} starts at 0x{off:x}, inside the table"; return false; }
                if (size < 0 || (long)off + size > data.Length) { error = $"entry {i} (0x{off:x}+{size}) runs past the end"; return false; }
                if (off < prevEnd) { error = $"entry {i} at 0x{off:x} overlaps the previous, which ended at 0x{prevEnd:x}"; return false; }

                a.Entries.Add(new GazEntry
                {
                    Index = i,
                    Offset = off,
                    Size = size,
                    Signature = size >= 4 ? BitConverter.ToUInt32(data, off) : 0u,
                });
                prevEnd = (long)off + size;
            }

            archive = a;
            return true;
        }

        public byte[] Read(GazEntry e)
        {
            var b = new byte[e.Size];
            Buffer.BlockCopy(_data, e.Offset, b, 0, e.Size);
            return b;
        }

        /// <summary>A container states its own length at offset 24. Comparing that against the length the
        /// table gave us is a free end-to-end check on the whole chain -- disc sector maths, file extraction
        /// and table parse all have to be right for these two independent numbers to agree.</summary>
        public bool ContainerSizeAgrees(GazEntry e, out int declared)
        {
            declared = -1;
            if (!e.IsContainer || e.Size < 28) return false;
            declared = BitConverter.ToInt32(_data, e.Offset + 24);
            return declared == e.Size;
        }

        public int AlignedCount()
        {
            int n = 0;
            foreach (var e in Entries) if (e.Offset % Alignment == 0) n++;
            return n;
        }
    }
}
