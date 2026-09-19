using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>A sound-effect group: one blob of SPU ADPCM and a table of 8-byte records over it, each a separate
    /// archive entry.
    ///
    /// ⭐ FROM THE GAME'S CODE. 0x800B8B80 loads group g from the pair of archive entries at 0x800F91D0 + g × 8
    /// (`u32 samples, u32 table`) through 0x800B804C: the samples go to SPU memory whole, the table stays in RAM
    /// with count = its size / 8. 0x800B8E08(g, n) plays sound n of group g (0x800B84AC): record n is `s16 flag
    /// (a per-voice switch, 0x801033D0), u16 pitch (0x1000 = 44,100 Hz), u32 offset` into the group's samples, at
    /// volume 0x3FFF of the master effects volume and centre pan. Group 7 is entries 320/321, the park's build
    /// tools: the path tool plays 0 when a run is started, 4 when it is laid, 3 as well when the run ends on path
    /// already there (connected; master confirmed it by ear) and 2 when it refuses (0x8001D5C0).</summary>
    public sealed class SoundGroup
    {
        public readonly struct Sound
        {
            public readonly short Flag;
            public readonly ushort Pitch;
            public readonly int Offset;
            public Sound(short flag, ushort pitch, int offset) { Flag = flag; Pitch = pitch; Offset = offset; }
            /// <summary>The rate the SPU plays it at: pitch / 0x1000 × 44,100.</summary>
            public int SampleRate => Math.Max(1, (int)((long)Pitch * 44100 / 0x1000));
        }

        const uint GroupTable = 0x800F91D0;
        public const int Groups = 12;

        public readonly List<Sound> Sounds = new();
        byte[] _samples = Array.Empty<byte>();
        readonly Dictionary<int, PcmSample> _decoded = new();

        /// <summary>Group g's (samples, table) archive entries, from the executable.</summary>
        public static (int Samples, int Table)? Entries(byte[] exe, uint baseAddress, int group)
        {
            int o = (int)(GroupTable - baseAddress) + group * 8;
            if (exe == null || group < 0 || group >= Groups || o < 0 || o + 8 > exe.Length) return null;
            return (BitConverter.ToInt32(exe, o), BitConverter.ToInt32(exe, o + 4));
        }

        public static SoundGroup Parse(byte[] samples, byte[] table)
        {
            if (samples == null || table == null) return null;
            var g = new SoundGroup { _samples = samples };
            for (int p = 0; p + 8 <= table.Length; p += 8)
                g.Sounds.Add(new Sound(BitConverter.ToInt16(table, p), BitConverter.ToUInt16(table, p + 2), BitConverter.ToInt32(table, p + 4)));
            return g;
        }

        /// <summary>Load group g off the archive, as 0x800B8B80 does. Null when the executable or the entries are missing.</summary>
        public static SoundGroup Load(GazArchive gz, byte[] exe, uint baseAddress, int group)
        {
            var e = Entries(exe, baseAddress, group);
            if (gz == null || e == null) return null;
            var (s, t) = e.Value;
            if (s < 0 || t < 0 || s >= gz.Entries.Count || t >= gz.Entries.Count) return null;
            return Parse(gz.Read(gz.Entries[s]), gz.Read(gz.Entries[t]));
        }

        /// <summary>Sound n as 16-bit PCM: the ADPCM from its offset to the block that ends it (Vag.Decode stops there).</summary>
        public PcmSample Decode(int n)
        {
            if (n < 0 || n >= Sounds.Count) return null;
            if (_decoded.TryGetValue(n, out var pcm)) return pcm;
            int off = Sounds[n].Offset;
            if (off < 0 || off >= _samples.Length) return null;
            var slice = new byte[_samples.Length - off];
            Array.Copy(_samples, off, slice, 0, slice.Length);
            pcm = Vag.Decode(slice, $"sound group sound {n}");
            _decoded[n] = pcm;
            return pcm;
        }
    }
}
