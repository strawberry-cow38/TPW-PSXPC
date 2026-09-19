using System;
using System.Collections.Generic;
using System.IO;
using TPW.Data;

/// <summary>Synthetic FT2 modules that exercise one replayer rule each, on a pure-sine sample, so TrackerPlayer's
/// output can be compared with libopenmpt's per TICK in cents and dB rather than through the real music, where a
/// dozen rules overlap on harmonic-rich samples and nothing can be attributed. `tpwcheck --music-test DIR` writes
/// DIR/xm/test_NAME.xm (for libopenmpt) and DIR/out/test_NAME.wav (this player) from the SAME module object.
///
/// The sample is 32 samples per cycle, so at C-6 (8363 * 4 Hz) it plays at 1045.4 Hz: a spectral peak whose
/// position can be read to a fraction of a cent from a 17 ms window inside every 20 ms tick.</summary>
static class MusicTests
{
    const int Rate = 44100;
    const byte C6 = 73, E6 = 77, G6 = 80, Off = 97;

    public static int Run(string dir)
    {
        Directory.CreateDirectory(Path.Combine(dir, "xm"));
        Directory.CreateDirectory(Path.Combine(dir, "out"));
        foreach (var (name, m, waves) in All())
        {
            File.WriteAllBytes(Path.Combine(dir, "xm", $"test_{name}.xm"), m.ToStandardXm(waves));
            var pcm = Program.RenderModule(m, waves, Rate, out _, out _);
            Program.WriteWav(Path.Combine(dir, "out", $"test_{name}.wav"), pcm, 2, Rate);
            Console.WriteLine($"test_{name}: {m.Patterns[0].GetLength(0)} rows, {pcm.Length / 2.0 / Rate:F2}s");
        }
        return 0;
    }

    static IEnumerable<(string, TrackerModule, List<PcmSample>)> All()
    {
        // ---- vibrato: 4xy with memory, 6xy (vibrato memory + volume slide), reset when the effect ends
        {
            var b = new Builder();
            b.Add(Instr(64, 0, 0, 128));
            var p = b.Pattern(32);
            p[0, 0] = Cell(C6, 1, 0, 0x4, 0x85);            // speed 8 depth 5
            for (int r = 1; r <= 5; r++) p[r, 0] = Cell(0, 0, 0, 0x4, 0x00);
            p[8, 0] = Cell(C6, 1, 0, 0x4, 0x2F);            // speed 2 depth 15
            for (int r = 9; r <= 15; r++) p[r, 0] = Cell(0, 0, 0, 0x4, 0x00);
            p[16, 0] = Cell(C6, 1, 0, 0x6, 0x01);           // vibrato memory + slide down 1
            for (int r = 17; r <= 20; r++) p[r, 0] = Cell(0, 0, 0, 0x6, 0x00);
            p[24, 0] = Cell(C6, 1, 0, 0x4, 0xF3);           // speed 15 depth 3
            for (int r = 25; r <= 27; r++) p[r, 0] = Cell(0, 0, 0, 0x4, 0x00);
            p[28, 0] = Cell(0, 0, 0, 0x4, 0x10);            // speed change only, depth kept
            p[29, 0] = Cell(0, 0, 0, 0x4, 0x08);            // depth change only, speed kept
            yield return ("vibrato", b.Build(), b.Waves);
        }
        // ---- portamento: 1xx 2xx with memory, 3xx tone portamento, E1x/E2x fine slides
        {
            var b = new Builder();
            b.Add(Instr(64, 0, 0, 128));
            var p = b.Pattern(32);
            p[0, 0] = Cell(C6, 1, 0, 0, 0);
            for (int r = 1; r <= 3; r++) p[r, 0] = Cell(0, 0, 0, 0x1, 0x04);
            p[4, 0] = Cell(0, 0, 0, 0x1, 0x00);            // memory
            for (int r = 5; r <= 6; r++) p[r, 0] = Cell(0, 0, 0, 0x2, 0x08);
            p[7, 0] = Cell(0, 0, 0, 0x2, 0x00);
            p[8, 0] = Cell(E6, 0, 0, 0x3, 0x10);            // towards E-6 at 64 units/tick
            for (int r = 9; r <= 13; r++) p[r, 0] = Cell(0, 0, 0, 0x3, 0x00);
            p[16, 0] = Cell(C6, 1, 0, 0xE, 0x18);           // fine porta up 8 at tick 0
            p[17, 0] = Cell(0, 0, 0, 0xE, 0x10);            // memory
            p[18, 0] = Cell(0, 0, 0, 0xE, 0x24);            // fine porta down 4
            p[24, 0] = Cell(G6, 1, 0, 0, 0);
            p[25, 0] = Cell(C6, 0, 0, 0x3, 0x04);           // slow slide down towards C-6
            for (int r = 26; r <= 31; r++) p[r, 0] = Cell(0, 0, 0, 0x3, 0x00);
            yield return ("porta", b.Build(), b.Waves);
        }
        // ---- arpeggio: 0xy at speed 6 and 3 (FT2's down-counting tick order), stop resets the period
        {
            var b = new Builder();
            b.Add(Instr(64, 0, 0, 128));
            var p = b.Pattern(32);
            p[0, 0] = Cell(C6, 1, 0, 0x0, 0x37);
            for (int r = 1; r <= 3; r++) p[r, 0] = Cell(0, 0, 0, 0x0, 0x37);
            p[8, 0] = Cell(C6, 1, 0, 0x0, 0xC7);
            for (int r = 9; r <= 11; r++) p[r, 0] = Cell(0, 0, 0, 0x0, 0xC7);
            p[16, 0] = Cell(C6, 1, 0, 0xF, 0x03);
            for (int r = 17; r <= 21; r++) p[r, 0] = Cell(0, 0, 0, 0x0, 0x37);
            p[22, 0] = Cell(0, 0, 0, 0xF, 0x06);
            yield return ("arpeggio", b.Build(), b.Waves);
        }
        // ---- volume: envelopes (sustain, loop, none), fadeout, key off, slides, set volume, retrigger, delay, offset
        {
            var b = new Builder();
            b.Add(Instr(64, 0, 0, 128, volEnv: new[] { (0, 64), (10, 20), (30, 60), (60, 0) }, volType: 3, volSus: 2, fade: 512));
            b.Add(Instr(64, 0, 0, 128, volEnv: new[] { (0, 0), (5, 64), (10, 16), (15, 64), (20, 0) }, volType: 5, volLoopStart: 1, volLoopEnd: 3, fade: 1024));
            b.Add(Instr(48, 0, 0, 128, fade: 2048, wave: Decaying()));
            var p = b.Pattern(64);
            p[0, 0] = Cell(C6, 1, 0, 0, 0);
            p[8, 0] = Cell(Off, 0, 0, 0, 0);
            p[16, 0] = Cell(C6, 2, 0, 0, 0);
            p[24, 0] = Cell(Off, 0, 0, 0, 0);
            p[32, 0] = Cell(C6, 3, 0x30, 0, 0);             // set volume 32 from the volume column
            p[36, 0] = Cell(0, 0, 0, 0xA, 0x02);
            p[37, 0] = Cell(0, 0, 0, 0xA, 0x00);
            p[38, 0] = Cell(0, 0, 0, 0xA, 0x40);
            p[39, 0] = Cell(0, 0, 0, 0xA, 0x00);
            p[40, 0] = Cell(0, 0, 0, 0xC, 0x20);
            p[41, 0] = Cell(0, 0, 0, 0xE, 0xB4);
            p[42, 0] = Cell(0, 0, 0, 0xE, 0xB0);
            p[44, 0] = Cell(C6, 3, 0, 0xE, 0x92);           // retrigger every 2 ticks
            p[46, 0] = Cell(C6, 3, 0, 0xE, 0xD3);           // delayed 3 ticks
            p[47, 0] = Cell(C6, 3, 0x40, 0xE, 0xD7);        // delay >= speed: never plays
            p[48, 0] = Cell(Off, 3, 0, 0, 0);               // key off with an instrument: volume reset (FT2 quirk)
            p[52, 0] = Cell(C6, 1, 0x40, 0, 0);
            p[53, 0] = Cell(0, 0, 0x20, 0, 0);
            p[54, 0] = Cell(0, 1, 0, 0, 0);                 // instrument alone: envelope restarts, volume reset
            p[56, 0] = Cell(C6, 3, 0, 0x9, 0x10);           // offset 4096 into the decaying sample
            p[58, 0] = Cell(C6, 3, 0, 0x9, 0x00);           // memory
            p[60, 0] = Cell(C6, 3, 0, 0x9, 0x40);           // offset past the end: silent
            yield return ("volume", b.Build(), b.Waves);
        }
        // ---- instrument: finetune, relative note, auto-vibrato with and without sweep, pan envelope, panning
        {
            var b = new Builder();
            b.Add(Instr(64, 42, -12, 128));
            b.Add(Instr(64, -8, 7, 128));
            b.Add(Instr(64, 0, 0, 128, vib: (0, 38, 6, 16)));
            b.Add(Instr(64, 0, 0, 128, vib: (0, 0, 4, 24)));
            b.Add(Instr(64, 0, 0, 128, panEnv: new[] { (0, 0), (20, 64), (40, 32) }, panType: 1));
            b.Add(Instr(64, 0, 0, 64));
            var p = b.Pattern(64);
            p[0, 0] = Cell(C6, 1, 0, 0, 0);
            p[8, 0] = Cell(C6, 2, 0, 0, 0);
            p[16, 0] = Cell(C6, 3, 0, 0, 0);
            p[32, 0] = Cell(C6, 4, 0, 0, 0);
            p[40, 0] = Cell(C6, 5, 0, 0, 0);
            p[48, 0] = Cell(C6, 6, 0, 0, 0);
            p[52, 0] = Cell(C6, 6, 0xC3, 0, 0);
            p[56, 0] = Cell(C6, 6, 0xCC, 0, 0);
            p[60, 0] = Cell(C6, 6, 0, 0x8, 0xF0);
            yield return ("instrument", b.Build(), b.Waves);
        }
    }

    static TrackerCell Cell(byte note, byte ins, byte vol, byte fx, byte param) => new TrackerCell(note, ins, vol, fx, param);

    static PcmSample Sine()
    {
        var s = new short[3200];
        for (int i = 0; i < s.Length; i++) s[i] = (short)Math.Round(20000 * Math.Sin(2 * Math.PI * i / 32.0));
        return new PcmSample { Samples = s, LoopStart = 0 };
    }

    static PcmSample Decaying()
    {
        var s = new short[8000];
        for (int i = 0; i < s.Length; i++) s[i] = (short)Math.Round(20000 * (1.0 - i / 8000.0) * Math.Sin(2 * Math.PI * i / 32.0));
        return new PcmSample { Samples = s, LoopStart = -1 };
    }

    /// <summary>A standard 263-byte FT2 instrument header plus one 40-byte sample header, as TrackerModule keeps them.</summary>
    static (TrackerInstrument, PcmSample) Instr(int vol, int finetune, int relNote, int pan, (int, int)[] volEnv = null, int volType = 0,
        int volSus = 0, int volLoopStart = 0, int volLoopEnd = 0, (int, int)[] panEnv = null, int panType = 0, int panSus = 0,
        int fade = 0, (int, int, int, int)? vib = null, PcmSample wave = null)
    {
        var h = new byte[263];
        Write32(h, 0, 263);
        h[27] = 1;
        Write32(h, 29, 40);
        if (volEnv != null) for (int i = 0; i < volEnv.Length; i++) { Write16(h, 129 + i * 4, volEnv[i].Item1); Write16(h, 131 + i * 4, volEnv[i].Item2); }
        if (panEnv != null) for (int i = 0; i < panEnv.Length; i++) { Write16(h, 177 + i * 4, panEnv[i].Item1); Write16(h, 179 + i * 4, panEnv[i].Item2); }
        h[225] = (byte)(volEnv?.Length ?? 0); h[226] = (byte)(panEnv?.Length ?? 0);
        h[227] = (byte)volSus; h[228] = (byte)volLoopStart; h[229] = (byte)volLoopEnd; h[230] = (byte)panSus;
        h[233] = (byte)volType; h[234] = (byte)panType;
        if (vib != null) { h[235] = (byte)vib.Value.Item1; h[236] = (byte)vib.Value.Item2; h[237] = (byte)vib.Value.Item3; h[238] = (byte)vib.Value.Item4; }
        Write16(h, 239, fade);
        var sh = new byte[40];
        sh[12] = (byte)vol; sh[13] = (byte)(sbyte)finetune; sh[14] = 0x10; sh[15] = (byte)pan; sh[16] = (byte)(sbyte)relNote;
        // FT2 pads names with spaces (and keeps the sample name's length in the "reserved" byte); libopenmpt's
        // loader uses exactly that to decide a file was made with FT2, which switches on its FT2 quirks and the
        // FT2-compatible mix levels. The disc's modules are padded this way; a NUL-padded name is played as a
        // generic XM: 9.5 dB quieter and with the arpeggio in the generic order.
        for (int i = 4; i < 26; i++) h[i] = (byte)' ';
        for (int i = 18; i < 40; i++) sh[i] = (byte)' ';
        var ins = new TrackerInstrument
        {
            SampleCount = 1, Header = h, SampleHeaders = new[] { sh }, Volume = (byte)vol, FineTune = (sbyte)finetune,
            Panning = (byte)pan, RelativeNote = (sbyte)relNote, VolumeEnvelopeType = (byte)volType, PanningEnvelopeType = (byte)panType,
        };
        return (ins, wave ?? Sine());
    }

    static void Write16(byte[] b, int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); }
    static void Write32(byte[] b, int at, int v) { Write16(b, at, v); Write16(b, at + 2, v >> 16); }

    sealed class Builder
    {
        public int Speed = 6, Tempo = 125;
        /// <summary>Ten channels (nine silent) because libopenmpt's pre-amp depends on the channel count and the
        /// real modules have 4..10; a 1-channel module comes out 6.5 dB quieter there.</summary>
        public const int Channels = 10;
        public readonly List<PcmSample> Waves = new();
        readonly List<TrackerInstrument> _instruments = new();
        TrackerCell[,] _pattern;

        public void Add((TrackerInstrument, PcmSample) i) { _instruments.Add(i.Item1); Waves.Add(i.Item2); }
        public TrackerCell[,] Pattern(int rows) => _pattern = new TrackerCell[rows, Channels];

        public TrackerModule Build()
        {
            var h = new byte[60 + 276];
            System.Text.Encoding.ASCII.GetBytes(TrackerModule.Signature).CopyTo(h, 0);
            for (int i = 17; i < 37; i++) h[i] = (byte)' ';        // FT2-style space padding (see Instr)
            h[37] = 0x1A;
            System.Text.Encoding.ASCII.GetBytes("FastTracker v2.00   ").CopyTo(h, 38);
            Write16(h, 58, 0x0104);
            Write32(h, 60, 276);
            Write16(h, 64, 1); Write16(h, 66, 0); Write16(h, 68, Channels); Write16(h, 70, 1);
            Write16(h, 72, _instruments.Count); Write16(h, 74, 1); Write16(h, 76, Speed); Write16(h, 78, Tempo);
            var m = new TrackerModule
            {
                Version = 0x0104, SongLength = 1, Restart = 0, Channels = Channels, PatternCount = 1, InstrumentCount = _instruments.Count,
                Flags = 1, Speed = Speed, Tempo = Tempo, Order = new byte[] { 0 }, HeaderBytes = h,
            };
            m.Patterns.Add(_pattern);
            m.Instruments.AddRange(_instruments);
            return m;
        }
    }
}
