using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>One cell of a pattern, as in FastTracker 2: note (1..96, 97 = key off), instrument (1-based),
    /// volume-column byte, effect and parameter. Zero means empty in every field.</summary>
    public readonly struct TrackerCell
    {
        public readonly byte Note, Instrument, Volume, Effect, Param;
        public TrackerCell(byte note, byte ins, byte vol, byte eff, byte par) { Note = note; Instrument = ins; Volume = vol; Effect = eff; Param = par; }
        public bool IsEmpty => Note == 0 && Instrument == 0 && Volume == 0 && Effect == 0 && Param == 0;
    }

    /// <summary>What survives of an instrument: everything but the sample data and its length.</summary>
    public sealed class TrackerInstrument
    {
        public int SampleCount;
        public sbyte RelativeNote;
        public sbyte FineTune;
        public byte Volume;
        public byte Panning;
        public byte VolumeEnvelopeType, PanningEnvelopeType;
        /// <summary>The raw instrument header as stored, so a rebuilt standard .xm keeps the envelopes intact.</summary>
        public byte[] Header = Array.Empty<byte>();
        /// <summary>The raw 40-byte sample headers, lengths zeroed as stored.</summary>
        public byte[][] SampleHeaders = Array.Empty<byte[]>();
    }

    /// <summary>The game's music: FastTracker 2 modules in the game's OWN variant.
    ///
    /// ⚠⚠ IT IS NOT STANDARD XM, AND A STANDARD READER PRODUCES MUSIC-SHAPED NONSENSE. The header, instrument and
    /// sample headers are FT2's, but the PATTERNS are packed the game's own way: each row is a list of
    /// [channel byte, one FT2 cell] pairs, only for the channels that play, ended by 0xFF. FT2's own packing
    /// has a byte for every cell of every channel. The files even say so: the version field, 0x0104 in FT2,
    /// reads 0xDDBA ("BADD"). libopenmpt read them as FT2 and played garbage notes; master heard "chunks of all
    /// different music and sounds mashed together".
    ///
    /// ✅ THE ROW FORMAT IS CHECKED, NOT ASSUMED: every pattern of all 9 modules, 187 of them, decodes to exactly
    /// its declared row count using exactly its declared byte count. A wrong reading overruns or stops short.
    ///
    /// ⚠ THE SAMPLES ARE ELSEWHERE. Each module's sample headers survive with their lengths ZEROED; the
    /// waveforms are the VAB bank in the entry before it (see VabHeader), one waveform per instrument, and
    /// the instrument count always equals the bank's waveform count (9 of 9).
    ///
    /// ✅ WAVEFORM N IS INSTRUMENT N, CONFIRMED BY EAR. Rebuilt that way and played through libopenmpt, master's
    /// verdict on the set was "the rest of the music sounds 1:1" (module 296 has a rhythmic tick that may be
    /// genuine). The first render, before the pattern packing was understood, had sounded like "chunks of all
    /// different music mashed together", which is why the pairing was never really tested until the patterns
    /// were right.</summary>
    public sealed class TrackerModule
    {
        public const string Signature = "Extended Module: ";
        public const ushort GameVersion = 0xDDBA;

        public ushort Version;
        public int SongLength, Restart, Channels, PatternCount, InstrumentCount, Flags, Speed, Tempo;
        public byte[] Order = Array.Empty<byte>();
        public readonly List<TrackerCell[,]> Patterns = new();   // [row, channel]
        public readonly List<TrackerInstrument> Instruments = new();
        /// <summary>The raw header, signature to the end of the order table.</summary>
        public byte[] HeaderBytes = Array.Empty<byte>();

        public bool LinearFrequencies => (Flags & 1) != 0;

        /// <summary>Every module entry in the archive with the waveforms it plays. Each module's VAB bank is split
        /// around its header entry (exactly <see cref="VabHeader.SplitHeaderSize"/> bytes): the body is the entry
        /// before the header and the module the entry after it. Waveform N is instrument N.</summary>
        public static IEnumerable<(int ModuleEntry, List<PcmSample> Waves)> WaveBanks(GazArchive g)
        {
            foreach (var e in g.Entries)
            {
                if (e.Size != VabHeader.SplitHeaderSize || !VabHeader.TryParse(g.Read(e), out var vab, out _)) continue;
                int bodyIndex = e.Index - 1, modIndex = e.Index + 1;
                if (bodyIndex < 0 || modIndex >= g.Entries.Count) continue;
                var waves = new List<PcmSample>();
                foreach (var w in vab.SliceBody(g.Read(g.Entries[bodyIndex]))) waves.Add(Vag.Decode(w));
                yield return (modIndex, waves);
            }
        }

        /// <summary>Every module that parses, with its waveforms: the game's music, ready for TrackerPlayer.</summary>
        public static List<(int Entry, TrackerModule Module, List<PcmSample> Waves)> FindAll(GazArchive g)
        {
            var outp = new List<(int, TrackerModule, List<PcmSample>)>();
            foreach (var (entry, waves) in WaveBanks(g))
                if (TryParse(g.Read(g.Entries[entry]), out var m, out _)) outp.Add((entry, m, waves));
            return outp;
        }

        public static bool TryParse(byte[] d, out TrackerModule m, out string error)
        {
            m = null; error = null;
            if (d == null || d.Length < 80) { error = "too short for a module"; return false; }
            for (int i = 0; i < Signature.Length; i++) if (d[i] != (byte)Signature[i]) { error = "no module signature"; return false; }
            int hsize = BitConverter.ToInt32(d, 60);
            if (hsize < 20 || 60 + hsize > d.Length) { error = "header runs past the entry"; return false; }
            var t = new TrackerModule
            {
                Version = BitConverter.ToUInt16(d, 58),
                SongLength = BitConverter.ToUInt16(d, 64), Restart = BitConverter.ToUInt16(d, 66),
                Channels = BitConverter.ToUInt16(d, 68), PatternCount = BitConverter.ToUInt16(d, 70),
                InstrumentCount = BitConverter.ToUInt16(d, 72), Flags = BitConverter.ToUInt16(d, 74),
                Speed = BitConverter.ToUInt16(d, 76), Tempo = BitConverter.ToUInt16(d, 78),
            };
            if (t.Channels < 1 || t.Channels > 32 || t.SongLength > 256) { error = $"{t.Channels} channels, {t.SongLength} orders"; return false; }
            t.Order = new byte[t.SongLength];
            Buffer.BlockCopy(d, 80, t.Order, 0, t.SongLength);
            t.HeaderBytes = new byte[60 + hsize];
            Buffer.BlockCopy(d, 0, t.HeaderBytes, 0, t.HeaderBytes.Length);

            int at = 60 + hsize;
            for (int p = 0; p < t.PatternCount; p++)
            {
                if (at + 9 > d.Length) { error = $"pattern {p} header past the end"; return false; }
                int plen = BitConverter.ToInt32(d, at);
                int rows = BitConverter.ToUInt16(d, at + 5), psize = BitConverter.ToUInt16(d, at + 7);
                if (at + plen + psize > d.Length) { error = $"pattern {p} data past the end"; return false; }
                if (!TryUnpackSparse(d, at + plen, psize, rows, t.Channels, out var grid, out string perr))
                { error = $"pattern {p}: {perr}"; return false; }
                t.Patterns.Add(grid);
                at += plen + psize;
            }
            for (int k = 0; k < t.InstrumentCount; k++)
            {
                if (at + 29 > d.Length) { error = $"instrument {k + 1} past the end"; return false; }
                int isize = BitConverter.ToInt32(d, at);
                int nsmp = BitConverter.ToUInt16(d, at + 27);
                if (isize < 29 || at + isize > d.Length) { error = $"instrument {k + 1} header size {isize}"; return false; }
                var ins = new TrackerInstrument { SampleCount = nsmp, Header = new byte[isize] };
                Buffer.BlockCopy(d, at, ins.Header, 0, isize);
                if (nsmp > 0 && isize >= 241)
                {
                    ins.VolumeEnvelopeType = d[at + 233];
                    ins.PanningEnvelopeType = d[at + 234];
                }
                at += isize;
                ins.SampleHeaders = new byte[nsmp][];
                long stored = 0;
                for (int sIdx = 0; sIdx < nsmp; sIdx++)
                {
                    if (at + 40 > d.Length) { error = $"instrument {k + 1} sample header past the end"; return false; }
                    var sh = new byte[40];
                    Buffer.BlockCopy(d, at, sh, 0, 40);
                    ins.SampleHeaders[sIdx] = sh;
                    stored += BitConverter.ToUInt32(sh, 0);
                    at += 40;
                }
                if (nsmp > 0)
                {
                    var s0 = ins.SampleHeaders[0];
                    ins.Volume = s0[12]; ins.FineTune = (sbyte)s0[13]; ins.Panning = s0[15]; ins.RelativeNote = (sbyte)s0[16];
                }
                at += (int)stored;   // zero on every module on the disc: the data lives in the VAB bank
                t.Instruments.Add(ins);
            }
            m = t;
            return true;
        }

        /// <summary>A standard FT2 module with the given waveforms as the instruments' samples (instrument i gets
        /// waves[i], 16-bit, looping from the VAG loop point when it has one), so any tracker or libopenmpt can
        /// play it. Patterns are re-packed FT2's way; the envelopes and every other header byte are kept.</summary>
        public byte[] ToStandardXm(IReadOnlyList<PcmSample> waves)
        {
            var o = new List<byte>(HeaderBytes);
            o[58] = 0x04; o[59] = 0x01;   // FT2's version 0x0104 in place of the game's 0xDDBA
            foreach (var grid in Patterns)
            {
                var packed = new List<byte>();
                int rows = grid.GetLength(0), chans = grid.GetLength(1);
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < chans; c++)
                    {
                        var cell = grid[r, c];
                        int f = 0x80 | (cell.Note != 0 ? 1 : 0) | (cell.Instrument != 0 ? 2 : 0) | (cell.Volume != 0 ? 4 : 0)
                                     | (cell.Effect != 0 ? 8 : 0) | (cell.Param != 0 ? 16 : 0);
                        packed.Add((byte)f);
                        if (cell.Note != 0) packed.Add(cell.Note);
                        if (cell.Instrument != 0) packed.Add(cell.Instrument);
                        if (cell.Volume != 0) packed.Add(cell.Volume);
                        if (cell.Effect != 0) packed.Add(cell.Effect);
                        if (cell.Param != 0) packed.Add(cell.Param);
                    }
                o.AddRange(BitConverter.GetBytes(9));
                o.Add(0);
                o.AddRange(BitConverter.GetBytes((ushort)rows));
                o.AddRange(BitConverter.GetBytes((ushort)packed.Count));
                o.AddRange(packed);
            }
            for (int k = 0; k < Instruments.Count; k++)
            {
                var ins = Instruments[k];
                o.AddRange(ins.Header);
                if (ins.SampleCount == 0) continue;
                var pcm = k < waves.Count ? waves[k] : null;
                int n = pcm?.SampleCount ?? 0;
                for (int sIdx = 0; sIdx < ins.SampleCount; sIdx++)
                {
                    var sh = (byte[])ins.SampleHeaders[sIdx].Clone();
                    bool mine = sIdx == 0 && n > 0;
                    int loop = mine ? pcm.LoopStart : -1;
                    WriteU32(sh, 0, mine ? n * 2 : 0);
                    WriteU32(sh, 4, mine && loop >= 0 && loop < n ? loop * 2 : 0);
                    WriteU32(sh, 8, mine && loop >= 0 && loop < n ? (n - loop) * 2 : 0);
                    sh[14] = (byte)((sh[14] & ~3) | 0x10 | (mine && loop >= 0 && loop < n ? 1 : 0));
                    o.AddRange(sh);
                }
                if (n > 0)
                {
                    short prev = 0;
                    foreach (var v in pcm.Samples)
                    {
                        short delta = (short)(v - prev);
                        o.Add((byte)delta); o.Add((byte)(delta >> 8));
                        prev = v;
                    }
                }
            }
            return o.ToArray();
        }

        static void WriteU32(byte[] b, int at, int v)
        { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24); }

        /// <summary>The game's pattern packing: per row, [channel, FT2 cell]... then 0xFF. The cell is FT2's: a
        /// flags byte with bit 7 set naming which fields follow, or five raw bytes. The row count and the byte
        /// count must both come out exact.</summary>
        static bool TryUnpackSparse(byte[] d, int start, int size, int rows, int channels, out TrackerCell[,] grid, out string error)
        {
            grid = new TrackerCell[rows, channels];
            error = null;
            int i = start, end = start + size, r = 0;
            while (r < rows)
            {
                if (i >= end) { error = $"data ended after {r} of {rows} rows"; return false; }
                int b = d[i++];
                if (b == 0xFF) { r++; continue; }
                if (b >= channels) { error = $"channel {b} of {channels} in row {r}"; return false; }
                if (i >= end) { error = "cell past the end"; return false; }
                byte note = 0, ins = 0, vol = 0, eff = 0, par = 0;
                int f = d[i];
                if ((f & 0x80) != 0)
                {
                    i++;
                    if ((f & 1) != 0) note = d[i++];
                    if ((f & 2) != 0) ins = d[i++];
                    if ((f & 4) != 0) vol = d[i++];
                    if ((f & 8) != 0) eff = d[i++];
                    if ((f & 16) != 0) par = d[i++];
                }
                else
                {
                    if (i + 5 > end) { error = "full cell past the end"; return false; }
                    note = d[i]; ins = d[i + 1]; vol = d[i + 2]; eff = d[i + 3]; par = d[i + 4];
                    i += 5;
                }
                if (i > end) { error = "cell past the end"; return false; }
                grid[r, b] = new TrackerCell(note, ins, vol, eff, par);
            }
            if (i != end) { error = $"{rows} rows used {i - start} of {size} bytes"; return false; }
            return true;
        }
    }
}
