using System;
using System.Collections.Generic;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    /// <summary>TrackerPlayer against hand-worked FT2 expectations on synthetic modules: a pure sine of 32 samples
    /// per cycle at C-6 plays at 8363*4/32 = 1045.4 Hz, so pitch is read off zero crossings per tick (882 frames
    /// at 125 BPM) and level off RMS. Timing expectations are exact frame counts.</summary>
    public class TrackerPlayerTests
    {
        const int Rate = 44100, Tick = 882;      // 44100 * 2.5 / 125
        const byte C6 = 73, Off = 97;

        // ------------------------------------------------------------------------------------------ helpers

        static PcmSample Sine(int cycles = 100, double amp = 20000, int perCycle = 32)
        {
            var s = new short[cycles * perCycle];
            for (int i = 0; i < s.Length; i++) s[i] = (short)Math.Round(amp * Math.Sin(2 * Math.PI * i / perCycle));
            return new PcmSample { Samples = s, LoopStart = 0 };
        }

        static TrackerInstrument Instr(int vol = 64, int finetune = 0, int relNote = 0, int pan = 128, int fade = 0,
            (int, int)[] volEnv = null, int volType = 0, int volSustain = 0)
        {
            var h = new byte[263];
            h[0] = 263 & 0xFF; h[1] = 263 >> 8; h[27] = 1; h[29] = 40;
            if (volEnv != null)
                for (int i = 0; i < volEnv.Length; i++)
                {
                    h[129 + i * 4] = (byte)volEnv[i].Item1; h[130 + i * 4] = (byte)(volEnv[i].Item1 >> 8);
                    h[131 + i * 4] = (byte)volEnv[i].Item2;
                }
            h[225] = (byte)(volEnv?.Length ?? 0); h[227] = (byte)volSustain; h[233] = (byte)volType;
            h[239] = (byte)fade; h[240] = (byte)(fade >> 8);
            var sh = new byte[40];
            sh[12] = (byte)vol; sh[13] = (byte)(sbyte)finetune; sh[14] = 0x10; sh[15] = (byte)pan; sh[16] = (byte)(sbyte)relNote;
            return new TrackerInstrument { SampleCount = 1, Header = h, SampleHeaders = new[] { sh } };
        }

        static TrackerModule Module(TrackerCell[,] pattern, int speed = 6, int tempo = 125, int restart = 0, params TrackerInstrument[] instruments)
        {
            var m = new TrackerModule
            {
                Version = 0x0104, SongLength = 1, Restart = restart, Channels = pattern.GetLength(1), PatternCount = 1,
                InstrumentCount = instruments.Length, Flags = 1, Speed = speed, Tempo = tempo, Order = new byte[] { 0 },
                HeaderBytes = new byte[336],
            };
            m.Patterns.Add(pattern);
            m.Instruments.AddRange(instruments);
            return m;
        }

        static TrackerCell Cell(byte note, byte ins = 0, byte vol = 0, byte fx = 0, byte param = 0) => new TrackerCell(note, ins, vol, fx, param);

        /// <summary>Everything the player produces before it finishes, or `maxFrames`.</summary>
        static short[] RenderAll(TrackerPlayer p, int maxFrames = Rate * 60)
        {
            var all = new List<short>();
            var buf = new short[1024 * 2];
            while (!p.Finished && all.Count / 2 < maxFrames)
            {
                int got = p.Render(buf, 1024);
                if (got == 0) break;
                for (int i = 0; i < got * 2; i++) all.Add(buf[i]);
            }
            return all.ToArray();
        }

        static int ZeroCrossings(short[] pcm, int fromFrame, int toFrame)
        {
            int n = 0;
            for (int f = fromFrame + 1; f < toFrame; f++)
                if ((pcm[f * 2] >= 0) != (pcm[(f - 1) * 2] >= 0)) n++;
            return n;
        }

        static double Rms(short[] pcm, int fromFrame, int toFrame, int channel = 0)
        {
            double acc = 0;
            for (int f = fromFrame; f < toFrame; f++) { double v = pcm[f * 2 + channel]; acc += v * v; }
            return Math.Sqrt(acc / Math.Max(1, toFrame - fromFrame));
        }

        static TrackerCell[,] Rows(int rows, params (int row, TrackerCell cell)[] cells)
        {
            var p = new TrackerCell[rows, 1];
            foreach (var (row, cell) in cells) p[row, 0] = cell;
            return p;
        }

        // ------------------------------------------------------------------------------------------- timing

        [Fact]
        public void SixtyFourRowsAtSpeedSixAre64x6TicksOf882FramesThenTheSongEnds()
        {
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1))), 6, 125, 0, Instr()), new[] { Sine() });
            var pcm = RenderAll(p);
            Assert.Equal(64 * 6 * Tick, pcm.Length / 2);
            Assert.True(p.Finished);
        }

        [Fact]
        public void FractionalTickLengthsAccumulateExactly()
        {
            // 100 BPM: 1102.5 frames per tick; 64 rows * 6 ticks = 384 ticks = 423,360 frames, not 384 * 1102
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1))), 6, 100, 0, Instr()), new[] { Sine() });
            Assert.Equal(423360, RenderAll(p).Length / 2);
        }

        [Fact]
        public void FxxBelow32SetsSpeedFromThatRowOn()
        {
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1, 0, 0xF, 0x03))), 6, 125, 0, Instr()), new[] { Sine() });
            Assert.Equal(64 * 3 * Tick, RenderAll(p).Length / 2);
        }

        [Fact]
        public void Fxx32OrMoreSetsTempo()
        {
            // F 0x7D = 125 -> F 0xFA = 250 BPM halves the tick to 441 frames from row 0
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1, 0, 0xF, 0xFA))), 6, 125, 0, Instr()), new[] { Sine() });
            Assert.Equal(64 * 6 * 441, RenderAll(p).Length / 2);
        }

        [Fact]
        public void PositionJumpBackIntoThePlayedSongEndsItWhenNotLooping()
        {
            // rows 0 and 1 play, B00 jumps to order 0 row 0, already played -> end after exactly 2 rows
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1)), (1, Cell(0, 0, 0, 0xB, 0x00))), 6, 125, 0, Instr()), new[] { Sine() });
            Assert.Equal(2 * 6 * Tick, RenderAll(p).Length / 2);
            Assert.True(p.Finished);
        }

        [Fact]
        public void PatternBreakGoesToTheGivenRowOfTheNextOrder()
        {
            // D 0x10 = row 10 (decimal parameter); only one order, so the "next" order is the restart: row 10 of
            // the same pattern, unvisited -> keeps playing rows 10..63, then row 0 is visited -> end.
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1)), (3, Cell(0, 0, 0, 0xD, 0x10))), 6, 125, 0, Instr()), new[] { Sine() });
            Assert.Equal((4 + 54) * 6 * Tick, RenderAll(p).Length / 2);
        }

        [Fact]
        public void LoopingRestartsInsteadOfFinishing()
        {
            var p = new TrackerPlayer(Module(Rows(4, (0, Cell(C6, 1))), 6, 125, 0, Instr()), new[] { Sine() }) { Loop = true };
            var buf = new short[Tick * 2];
            for (int i = 0; i < 4 * 6 * 5; i++) Assert.Equal(Tick, p.Render(buf, Tick));   // five times round
            Assert.False(p.Finished);
        }

        // -------------------------------------------------------------------------------------------- pitch

        [Fact]
        public void C6PlaysTheSampleAt4x8363Hz()
        {
            // 32 samples per cycle at 33452 Hz = 1045.4 Hz -> 2090.8 zero crossings per second
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1))), 6, 125, 0, Instr()), new[] { Sine() });
            var pcm = RenderAll(p);
            int zc = ZeroCrossings(pcm, Rate, 2 * Rate);
            Assert.InRange(zc, 2085, 2097);
        }

        [Fact]
        public void RelativeNoteMinus12HalvesThePitch()
        {
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1))), 6, 125, 0, Instr(relNote: -12)), new[] { Sine() });
            Assert.InRange(ZeroCrossings(RenderAll(p), Rate, 2 * Rate), 1040, 1051);
        }

        [Fact]
        public void FinetuneIgnoresItsLowerThreeBits()
        {
            // FT2 quantises finetune to steps of 8 (= 4 period units = 6.25 cents): +42 acts as +40 = 31.25 cents,
            // 1045.4 Hz * 2^(31.25/1200) = 1064.5 Hz -> 2129 crossings/s; the full 42/128 semitone would give 2131.
            // The song is 64 rows = 7.68 s, shorter than the ten seconds measured, so it loops (the note retriggers
            // at the restart, which moves the count by one at most).
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1))), 6, 125, 0, Instr(finetune: 42)), new[] { Sine() }) { Loop = true };
            var pcm = RenderAll(p, 11 * Rate);
            int zc = ZeroCrossings(pcm, Rate, 11 * Rate);           // ten seconds: 21,290 expected, 21,309 unquantised
            Assert.InRange(zc, 21280, 21300);
        }

        [Fact]
        public void ArpeggioAtSpeedSixPlaysBaseThenYThenX()
        {
            // FT2 indexes its arpeggio table with the DOWN-counting tick: at speed 6 the ticks give 0,y,x,0,y,x.
            // 0x37: tick 1 = +7 semitones (1566 Hz, ~62 crossings per 882 frames), tick 2 = +3 (1243 Hz, ~50).
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1, 0, 0x0, 0x37)), (1, Cell(0, 0, 0, 0x0, 0x37))), 6, 125, 0, Instr()), new[] { Sine() });
            var pcm = RenderAll(p);
            int base0 = ZeroCrossings(pcm, 0, Tick), t1 = ZeroCrossings(pcm, Tick, 2 * Tick), t2 = ZeroCrossings(pcm, 2 * Tick, 3 * Tick), t3 = ZeroCrossings(pcm, 3 * Tick, 4 * Tick);
            Assert.InRange(base0, 40, 44);
            Assert.InRange(t1, 60, 65);
            Assert.InRange(t2, 48, 52);
            Assert.InRange(t3, 40, 44);
        }

        [Fact]
        public void PortaUpSlidesFourPeriodUnitsPerTickPerParameterUnit()
        {
            // 1 04 for one row (5 non-zero ticks): 5 * 16 units = 80 units = 1.25 semitones up = 1124.4 Hz
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1)), (1, Cell(0, 0, 0, 0x1, 0x04))), 6, 125, 0, Instr()), new[] { Sine() });
            var pcm = RenderAll(p);
            Assert.InRange(ZeroCrossings(pcm, 3 * Rate, 4 * Rate), 2243, 2255);
        }

        [Fact]
        public void VibratoIsNotAppliedOnTickZeroOfARow()
        {
            // 4 8F: speed 8 (32 per tick over a 256 cycle), depth 15. Tick 1 reads table position 0 (no offset:
            // 41-42 crossings), tick 3 position 64 (table 255 -> (255*15)>>5 = 119 units = 1.86 semitones DOWN,
            // 939 Hz -> 37-38), tick 5 position 128 (none again). Tick 0 of the next row keeps tick 5's period
            // (41-42): FT2 evaluates no vibrato on tick 0. A player that did would already be at position 160
            // there (84 units UP, 1128 Hz -> 45), which is where tick 7 lands.
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1, 0, 0x4, 0x8F)), (1, Cell(0, 0, 0, 0x4, 0x00))), 6, 125, 0, Instr()), new[] { Sine() });
            var pcm = RenderAll(p);
            Assert.InRange(ZeroCrossings(pcm, 1 * Tick, 2 * Tick), 40, 43);
            Assert.InRange(ZeroCrossings(pcm, 3 * Tick, 4 * Tick), 36, 39);
            Assert.InRange(ZeroCrossings(pcm, 6 * Tick, 7 * Tick), 40, 43);
            Assert.InRange(ZeroCrossings(pcm, 7 * Tick, 8 * Tick), 44, 47);
        }

        // ------------------------------------------------------------------------------------- volume, pan

        [Fact]
        public void SetVolumeIsLinearInSixtyFourths()
        {
            var full = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1, 0x50))), 6, 125, 0, Instr()), new[] { Sine() });
            var half = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1, 0x30))), 6, 125, 0, Instr()), new[] { Sine() });
            double a = Rms(RenderAll(full), Rate, 2 * Rate), b = Rms(RenderAll(half), Rate, 2 * Rate);
            Assert.InRange(b / a, 0.499, 0.501);
        }

        [Fact]
        public void PanningFollowsTheSquareRootLaw()
        {
            // pan 0xC4 -> 64: left = sqrt(192/256) = 0.866, right = sqrt(64/256) = 0.5
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1, 0xC4))), 6, 125, 0, Instr()), new[] { Sine() });
            var pcm = RenderAll(p);
            double l = Rms(pcm, Rate, 2 * Rate, 0), r = Rms(pcm, Rate, 2 * Rate, 1);
            Assert.InRange(r / l, 0.576, 0.579);
        }

        [Fact]
        public void KeyOffWithoutAnEnvelopeCutsTheNoteWithinTheQuickRamp()
        {
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1)), (8, Cell(Off))), 6, 125, 0, Instr()), new[] { Sine() });
            var pcm = RenderAll(p);
            int off = 8 * 6 * Tick;
            Assert.True(Rms(pcm, off - Tick, off) > 1000);
            Assert.Equal(0, Rms(pcm, off + 221, off + 2 * Tick));       // 5 ms ramp = 220 frames, then silence
        }

        [Fact]
        public void FadeoutAfterKeyOffReachesSilenceAfter32768OverFadeoutTicks()
        {
            // envelope on (sustain at 64), fadeout 512: 32768/512 = 64 ticks after the key-off tick
            var ins = Instr(fade: 512, volEnv: new[] { (0, 64), (100, 64) }, volType: 3, volSustain: 0);
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1)), (8, Cell(Off))), 6, 125, 0, ins), new[] { Sine() });
            var pcm = RenderAll(p);
            int off = 8 * 6 * Tick;
            Assert.True(Rms(pcm, off + 30 * Tick, off + 31 * Tick) > 100);            // half-way: still audible
            Assert.Equal(0, Rms(pcm, off + 65 * Tick, off + 70 * Tick));             // gone
        }

        [Fact]
        public void VolumeEnvelopeRampsInAtItsPointRate()
        {
            // (0,0) -> (10,64): tick 0 silent, full from tick 10; each tick's volume is reached at the tick's end
            var ins = Instr(volEnv: new[] { (0, 0), (10, 64), (200, 64) }, volType: 1);
            var env = RenderAll(new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1))), 6, 125, 0, ins), new[] { Sine() }));
            var flat = RenderAll(new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1))), 6, 125, 0, Instr()), new[] { Sine() }));
            Assert.Equal(0, Rms(env, 0, Tick / 2));
            Assert.InRange(Rms(env, 12 * Tick, 20 * Tick) / Rms(flat, 12 * Tick, 20 * Tick), 0.999, 1.001);
            Assert.InRange(Rms(env, 5 * Tick, 6 * Tick) / Rms(flat, 5 * Tick, 6 * Tick), 0.40, 0.55);   // tick 5 ends at 32/64
        }

        [Fact]
        public void SampleOffsetPastTheEndPlaysNothing()
        {
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1, 0, 0x9, 0xFF))), 6, 125, 0, Instr()), new[] { Sine(cycles: 10) });   // 320 samples, offset 0xFF00
            Assert.Equal(0, Rms(RenderAll(p), 0, Rate));
        }

        [Fact]
        public void NoteDelayAtOrPastTheSpeedNeverPlays()
        {
            var p = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1, 0, 0xE, 0xD6))), 6, 125, 0, Instr()), new[] { Sine() });
            Assert.Equal(0, Rms(RenderAll(p), 0, Rate));
            var q = new TrackerPlayer(Module(Rows(64, (0, Cell(C6, 1, 0, 0xE, 0xD3))), 6, 125, 0, Instr()), new[] { Sine() });
            var pcm = RenderAll(q);
            Assert.Equal(0, Rms(pcm, 0, 3 * Tick));
            Assert.True(Rms(pcm, 4 * Tick, 6 * Tick) > 1000);
        }

        [Fact]
        public void RenderReturnsFewerFramesOnlyAtTheEnd()
        {
            var p = new TrackerPlayer(Module(Rows(4, (0, Cell(C6, 1))), 6, 125, 0, Instr()), new[] { Sine() });
            // 4 rows x 6 ticks x 882 = 21,168 frames: two full buffers of 10,000, then the last 1,168.
            // (It asserted 11,168 from the second call, which a 10,000-frame buffer cannot hold: tinyclaw's catch.)
            var buf = new short[10000 * 2];
            Assert.Equal(10000, p.Render(buf, 10000));
            Assert.Equal(10000, p.Render(buf, 10000));
            Assert.False(p.Finished);
            Assert.Equal(4 * 6 * Tick - 20000, p.Render(buf, 10000));
            Assert.True(p.Finished);
            Assert.Equal(0, p.Render(buf, 10000));
        }
    }
}
