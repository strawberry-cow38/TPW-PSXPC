using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>Plays one of the game's tracker modules (see TrackerModule) with the waveforms of its VAB bank, the way
    /// FastTracker 2 would play the equivalent standard .xm. Pure C#, no engine references; Render is called from
    /// the game's audio thread with whatever the mixer has room for.
    ///
    /// ⭐ THE RULES ARE FT2's, TAKEN FROM ITS REPLAYER, NOT TUNED BY EAR. The row/tick loop (getNewNote, triggerNote,
    /// keyOff, the effect jump tables, envelopes with their 8.8 delta interpolation, fadeout, auto-vibrato, position
    /// jump / pattern break / speed) is a literal port of the FT2 replayer as documented by the 8bitbubsy FT2 clone,
    /// including its byte and word wraps (the vibrato position is a byte, periods are words, the envelope tick starts
    /// at 65535 so the first update lands on 0, the arpeggio table is indexed by the DOWN-counting tick so the order
    /// per row is base,y,x,base,y,x, the pan-envelope key-off adjustment fires only when the pan envelope is OFF).
    /// The modules on this disc use: linear frequencies (flags bit 0 on all 9); effects 0 1 2 3 4 6 9 A B C D E1 E9
    /// EB ED F; volume-column set volume and set panning; volume envelopes of types 1, 3 and 5; one panning envelope;
    /// fadeout; key-off; relative note, finetune, per-sample volume and panning; forward loops; auto-vibrato on four
    /// instruments (293's 1 and 3, 299's 11 and 13). The rest of the FT2 effect set that shares code paths (5, 8, E2,
    /// EA, EC, EE, G, H, K, the volume-column slides) is here too, untested against this disc.
    ///
    /// Mixing: 32.32 fixed-point positions with linear interpolation (the game's samples are 8..44 kHz VAG rips;
    /// the difference to a sinc interpolator is above the material's bandwidth), FT2's square-root panning law,
    /// FT2's volume ramping (a changed volume ramps across the tick; a triggered note ramps in over 5 ms while the
    /// voice it replaces ramps out over the same 5 ms), tick length = rate * 2.5 / BPM carried with a fractional
    /// accumulator so long-term timing is exact (libopenmpt does the same).
    ///
    /// ⚖ The replayer rules follow ft2-clone by Olav Sørensen (BSD 3-Clause); see THIRD_PARTY_NOTICES.md.
    ///
    /// Song end: with Loop the order list wraps to the header's restart position as in FT2; without it playback
    /// stops the first time a row that has already been played is reached again, which is libopenmpt's definition
    /// of "the song ends" (it covers both the order list wrapping and a Bxx jump back into the song).</summary>
    public sealed class TrackerPlayer
    {
        public TrackerPlayer(TrackerModule module, IReadOnlyList<PcmSample> waves, int outputRate = 44100)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (outputRate < 4000) throw new ArgumentOutOfRangeException(nameof(outputRate));
            _module = module;
            OutputRate = outputRate;
            _quickRampSamples = Math.Max(1, outputRate / 200);
            _instruments = new Instrument[module.Instruments.Count];
            for (int k = 0; k < _instruments.Length; k++)
                _instruments[k] = Instrument.From(module.Instruments[k], waves != null && k < waves.Count ? waves[k] : null);
            _channels = new Channel[Math.Max(1, module.Channels)];
            for (int i = 0; i < _channels.Length; i++) _channels[i] = new Channel(_empty);
            _speed = module.Speed;
            SetBpm(module.Tempo);
            _tick = 1;                               // the first tick reads row 0
            _songPos = 0; _row = 0;
            _visited = new bool[Math.Max(1, module.SongLength) * 256];
            if (module.SongLength == 0 || module.Patterns.Count == 0) Finished = true;
            else { _pattNum = module.Order[0]; _numRows = RowsOf(_pattNum); }
        }

        /// <summary>Restart at the module's restart position when the order list ends, instead of stopping.</summary>
        public bool Loop { get; set; }
        public bool Finished { get; private set; }
        public int OutputRate { get; }
        /// <summary>Output scale: 1.0 makes a single full-volume centred voice peak at 0.707 of full scale. FT2's
        /// own default is 0.125 (amp 4 of 32); 0.375 is libopenmpt's level, measured on this disc's modules (the
        /// RMS envelopes of the two renders agree to 0.02 dB at this value), so the game sounds as the reference
        /// renders master approved did.</summary>
        public float MasterVolume { get; set; } = 0.375f;
        public int SongPosition => _songPos;
        public int Row => _row;
        public int Speed => _speed;
        public int Bpm => _bpm;

        /// <summary>Fill `interleavedStereo` (L,R pairs) with `frames` frames; returns the frames written, fewer only
        /// at the song's end when not looping.</summary>
        public int Render(short[] interleavedStereo, int frames)
        {
            if (interleavedStereo == null || frames <= 0) return 0;
            if (frames * 2 > interleavedStereo.Length) frames = interleavedStereo.Length / 2;
            if (_mix == null || _mix.Length < frames * 2) _mix = new float[frames * 2];
            int done = 0;
            while (done < frames)
            {
                if (_tickLeft == 0)
                {
                    if (Finished || !Tick()) break;
                    double t = _samplesPerTick + _tickFrac;
                    _tickLeft = (int)t;
                    _tickFrac = t - _tickLeft;
                    if (_tickLeft < 1) _tickLeft = 1;
                    UpdateVoices(_tickLeft);
                }
                int n = Math.Min(_tickLeft, frames - done);
                MixAll(_mix, done * 2, n);
                done += n;
                _tickLeft -= n;
            }
            int total = done * 2;
            for (int i = 0; i < total; i++)
            {
                float v = _mix[i];
                _mix[i] = 0f;
                int s = (int)(v + (v >= 0f ? 0.5f : -0.5f));
                interleavedStereo[i] = s > 32767 ? (short)32767 : s < -32768 ? (short)-32768 : (short)s;
            }
            return done;
        }

        // ------------------------------------------------------------------------------------------------ data

        sealed class Sample
        {
            public short[] Data = new short[1];      // padded by one: Data[Length] is the loop's first sample, or 0
            public int Length, LoopStart, LoopLength;
            public bool Loops;
            public int Volume, Panning = 128;
            public int Finetune, RelativeNote;      // signed bytes
        }

        sealed class Instrument
        {
            public readonly byte[] NoteToSample = new byte[96];
            public readonly Sample[] Samples = new Sample[16];
            public readonly int[,] VolPoints = new int[12, 2], PanPoints = new int[12, 2];
            public int VolLength, PanLength, VolSustain, VolLoopStart, VolLoopEnd, PanSustain, PanLoopStart, PanLoopEnd;
            public int VolFlags, PanFlags;          // bit 0 on, bit 1 sustain, bit 2 loop
            public int VibType, VibSweep, VibDepth, VibRate, Fadeout;

            public Instrument()
            {
                for (int i = 0; i < Samples.Length; i++) Samples[i] = new Sample();
            }

            /// <summary>The instrument's header and sample headers as TrackerModule keeps them; sample 0 gets the
            /// bank's waveform (16-bit, forward loop from the VAG loop point to the end, exactly as ToStandardXm
            /// writes it), the others stay empty.</summary>
            public static Instrument From(TrackerInstrument src, PcmSample wave)
            {
                var ins = new Instrument();
                var h = src.Header;
                if (src.SampleCount > 0 && h.Length >= 241)
                {
                    Buffer.BlockCopy(h, 33, ins.NoteToSample, 0, 96);
                    for (int i = 0; i < 12; i++)
                    {
                        ins.VolPoints[i, 0] = BitConverter.ToUInt16(h, 129 + i * 4);
                        ins.VolPoints[i, 1] = BitConverter.ToUInt16(h, 131 + i * 4);
                        ins.PanPoints[i, 0] = BitConverter.ToUInt16(h, 177 + i * 4);
                        ins.PanPoints[i, 1] = BitConverter.ToUInt16(h, 179 + i * 4);
                    }
                    ins.VolLength = Math.Min(12, (int)h[225]);
                    ins.PanLength = Math.Min(12, (int)h[226]);
                    ins.VolSustain = Math.Min(11, (int)h[227]); ins.VolLoopStart = Math.Min(11, (int)h[228]); ins.VolLoopEnd = Math.Min(11, (int)h[229]);
                    ins.PanSustain = Math.Min(11, (int)h[230]); ins.PanLoopStart = Math.Min(11, (int)h[231]); ins.PanLoopEnd = Math.Min(11, (int)h[232]);
                    ins.VolFlags = h[233]; ins.PanFlags = h[234];
                    ins.VibType = h[235]; ins.VibSweep = h[236]; ins.VibDepth = h[237]; ins.VibRate = h[238];
                    ins.Fadeout = BitConverter.ToUInt16(h, 239);
                }
                for (int sIdx = 0; sIdx < src.SampleCount && sIdx < 16; sIdx++)
                {
                    var sh = src.SampleHeaders[sIdx];
                    var s = ins.Samples[sIdx];
                    s.Volume = sh[12]; s.Finetune = (sbyte)sh[13]; s.Panning = sh[15]; s.RelativeNote = (sbyte)sh[16];
                    if (sIdx != 0 || wave == null || wave.SampleCount == 0) continue;
                    int n = wave.SampleCount;
                    int loop = wave.LoopStart;
                    s.Length = n;
                    s.Loops = loop >= 0 && loop < n;
                    s.LoopStart = s.Loops ? loop : 0;
                    s.LoopLength = s.Loops ? n - loop : 0;
                    s.Data = new short[n + 1];
                    Array.Copy(wave.Samples, s.Data, n);
                    s.Data[n] = s.Loops ? wave.Samples[loop] : (short)0;
                }
                return ins;
            }
        }

        sealed class Voice
        {
            public short[] Data; public int End, LoopLength; public bool Loops, Active;
            public long Pos, Step;                  // 32.32 sample position and increment
            public float VolL, VolR, TargetL, TargetR, DeltaL, DeltaR; public int RampLeft;

            public void CopyFrom(Voice o)
            {
                Data = o.Data; End = o.End; LoopLength = o.LoopLength; Loops = o.Loops; Active = o.Active;
                Pos = o.Pos; Step = o.Step; VolL = o.VolL; VolR = o.VolR; TargetL = o.TargetL; TargetR = o.TargetR;
                DeltaL = o.DeltaL; DeltaR = o.DeltaR; RampLeft = o.RampLeft;
            }

            public void SetTarget(float l, float r, int ramp)
            {
                TargetL = l; TargetR = r;
                if (ramp <= 0 || (l == VolL && r == VolR)) { VolL = l; VolR = r; RampLeft = 0; return; }
                RampLeft = ramp;
                DeltaL = (l - VolL) / ramp; DeltaR = (r - VolR) / ramp;
            }
        }

        sealed class Channel
        {
            public Instrument Ins; public Sample Smp;
            public int InstrNum, NoteNum, SmpNum;
            public int Efx, EfxData, VolCol, CopyOfInstrAndNote;
            public int RealPeriod, OutPeriod, FinalPeriod;
            public int RealVol, OutVol, OldVol, OutPan = 128, OldPan = 128, FinalPan = 128;
            public int Finetune, RelativeNote;
            public int PortaSpeed, PortaTarget, PortaDir; public bool SemitonePorta;
            public int PitchUpSpeed, PitchDownSpeed, FPitchUpSpeed, FPitchDownSpeed, VolSlideSpeed, FVolUpSpeed, FVolDownSpeed;
            public int VibPos, VibSpeed, VibDepth, VibTremCtrl, TremoloPos, TremoloSpeed, TremoloDepth;
            public int SampleOffset, SmpStartPos;
            public bool KeyOff;
            public int FadeoutVol, FadeoutSpeed;
            public int VolEnvTick, VolEnvPos, VolEnvValue, VolEnvDelta;
            public int PanEnvTick, PanEnvPos, PanEnvValue, PanEnvDelta;
            public int AutoVibPos, AutoVibAmp, AutoVibSweep;
            public float FinalVol;
            public bool TriggerVoice, QuickRamp;
            public readonly Voice V = new Voice(), F = new Voice();
            public Channel(Instrument empty) { Ins = empty; Smp = empty.Samples[0]; }
        }

        readonly TrackerModule _module;
        readonly Instrument[] _instruments;
        readonly Instrument _empty = new Instrument();
        readonly Channel[] _channels;
        readonly bool[] _visited;
        readonly int _quickRampSamples;
        float[] _mix;

        // song state, FT2 names
        int _speed, _bpm, _tick, _songPos, _row, _pattNum, _numRows, _pBreakPos, _pattDelTime, _pattDelTime2, _globalVolume = 64;
        bool _posJumpFlag, _bxxOverflow;
        double _samplesPerTick, _tickFrac;
        int _tickLeft;

        static readonly byte[] VibratoTab =
        {
            0, 24, 49, 74, 97, 120, 141, 161, 180, 197, 212, 224, 235, 244, 250, 253,
            255, 253, 250, 244, 235, 224, 212, 197, 180, 161, 141, 120, 97, 74, 49, 24
        };
        /// <summary>FT2's arpeggio table is 16 entries in a 32-entry read; the second half is what follows it in
        /// the FT2 binary (only reachable with speed above 16).</summary>
        static readonly byte[] ArpeggioTab =
        {
            0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0,
            0x00, 0x18, 0x31, 0x4A, 0x61, 0x78, 0x8D, 0xA1, 0xB4, 0xC5, 0xD4, 0xE0, 0xEB, 0xF4, 0xFA, 0xFD
        };
        static readonly sbyte[] AutoVibSineTab = BuildAutoVibSine();
        static readonly float[] SqrtPanTab = BuildSqrtPan();

        static sbyte[] BuildAutoVibSine()
        {
            var t = new sbyte[256];
            for (int i = 0; i < 256; i++) t[i] = (sbyte)Math.Round(64.0 * Math.Sin(-i * 2.0 * Math.PI / 256.0), MidpointRounding.AwayFromZero);
            return t;
        }

        static float[] BuildSqrtPan()
        {
            var t = new float[257];
            for (int i = 0; i <= 256; i++) t[i] = (float)(Math.Round(65536.0 * Math.Sqrt(i / 256.0), MidpointRounding.AwayFromZero) / 65536.0);
            return t;
        }

        /// <summary>FT2's linear period table: entry i is (1936 - i) * 4; a note's index is (note-1)*16 + finetune/8 + 16.</summary>
        static int PeriodLut(int i)
        {
            if (i < 0) i = 0; else if (i > 1935) i = 1935;
            return (1936 - i) * 4;
        }

        int RowsOf(int pattern) => pattern >= 0 && pattern < _module.Patterns.Count ? _module.Patterns[pattern].GetLength(0) : 64;

        Instrument InstrumentAt(int num) => num >= 1 && num <= _instruments.Length ? _instruments[num - 1] : _empty;

        void SetBpm(int bpm)
        {
            if (bpm < 1) bpm = 1;
            _bpm = bpm;
            _samplesPerTick = OutputRate * 2.5 / bpm;
        }

        // ------------------------------------------------------------------------------------------- the tick

        /// <summary>One replayer tick: FT2's tickReplayer. Returns false when the song has ended (nothing to mix).</summary>
        bool Tick()
        {
            _tick = (_tick - 1) & 0xFF;
            bool tickZero = false;
            if (_tick == 0) { _tick = _speed; tickZero = true; }
            bool readNewNote = tickZero && _pattDelTime2 == 0;
            if (readNewNote)
            {
                if (!Loop)
                {
                    int key = (_songPos & 0xFF) * 256 + (_row & 0xFF);
                    if (key < _visited.Length)
                    {
                        if (_visited[key]) { Finished = true; return false; }
                        _visited[key] = true;
                    }
                }
                var grid = _pattNum >= 0 && _pattNum < _module.Patterns.Count ? _module.Patterns[_pattNum] : null;
                int chans = grid?.GetLength(1) ?? 0;
                for (int i = 0; i < _channels.Length; i++)
                {
                    var cell = grid != null && i < chans && _row < grid.GetLength(0) ? grid[_row, i] : default;
                    GetNewNote(_channels[i], cell);
                    UpdateVolPanAutoVib(_channels[i]);
                }
            }
            else
            {
                for (int i = 0; i < _channels.Length; i++)
                {
                    HandleEffectsTickNonZero(_channels[i]);
                    UpdateVolPanAutoVib(_channels[i]);
                }
            }
            GetNextPos();
            return true;
        }

        void GetNextPos()
        {
            if (_tick != 1) return;
            _row++;
            if (_pattDelTime > 0) { _pattDelTime2 = _pattDelTime; _pattDelTime = 0; }
            if (_pattDelTime2 > 0)
            {
                _pattDelTime2--;
                if (_pattDelTime2 > 0) _row--;
            }
            if (_row >= _numRows || _posJumpFlag)
            {
                _row = _pBreakPos;
                _pBreakPos = 0;
                _posJumpFlag = false;
                if (_bxxOverflow) { _songPos = 0; _bxxOverflow = false; }
                else if (++_songPos >= _module.SongLength)
                    _songPos = _module.Restart < _module.SongLength ? _module.Restart : 0;
                _pattNum = _module.Order[_songPos];
                _numRows = RowsOf(_pattNum);
                if (_row >= _numRows) _row = 0;
            }
        }

        // ----------------------------------------------------------------------------------------- tick zero

        void GetNewNote(Channel ch, TrackerCell p)
        {
            ch.VolCol = p.Volume;
            if (ch.Efx == 0)
            {
                if (ch.EfxData > 0) ch.OutPeriod = ch.RealPeriod;          // an arpeggio was running: period back
            }
            else if ((ch.Efx == 4 || ch.Efx == 6) && p.Effect != 4 && p.Effect != 6)
                ch.OutPeriod = ch.RealPeriod;                             // a vibrato ended on this row: period back

            ch.Efx = p.Effect;
            ch.EfxData = p.Param;
            ch.CopyOfInstrAndNote = (p.Instrument << 8) | p.Note;

            int inst = p.Instrument;
            if (inst > 0)
            {
                if (inst <= 128) ch.InstrNum = inst; else inst = 0;
            }

            if (p.Effect == 0x0E && p.Param >= 0xD1 && p.Param <= 0xDF) return;   // note delay: nothing until the tick

            if (p.Effect != 0x0E || p.Param != 0x90)                               // everything but E90 (retrig now)
            {
                if ((ch.VolCol & 0xF0) == 0xF0)                                    // volume-column tone portamento
                {
                    int param = ch.VolCol & 0x0F;
                    if (param > 0) ch.PortaSpeed = (param << 4) * 4;
                    PreparePortamento(ch, p, inst);
                    HandleEffectsTickZero(ch);
                    return;
                }
                if (p.Effect == 3 || p.Effect == 5)
                {
                    if (p.Effect != 5 && p.Param != 0) ch.PortaSpeed = p.Param * 4;
                    PreparePortamento(ch, p, inst);
                    HandleEffectsTickZero(ch);
                    return;
                }
                if (p.Effect == 0x14 && p.Param == 0)                              // K00
                {
                    KeyOff(ch);
                    if (inst > 0) ResetVolumes(ch);
                    HandleEffectsTickZero(ch);
                    return;
                }
                if (p.Note == 0)
                {
                    if (inst > 0) { ResetVolumes(ch); TriggerInstrument(ch); }
                    HandleEffectsTickZero(ch);
                    return;
                }
            }

            if (p.Note == 97) KeyOff(ch);
            else TriggerNote(p.Note, p.Effect, p.Param, ch);

            if (inst > 0)
            {
                ResetVolumes(ch);
                if (p.Note != 97) TriggerInstrument(ch);
            }
            HandleEffectsTickZero(ch);
        }

        void PreparePortamento(Channel ch, TrackerCell p, int inst)
        {
            if (p.Note > 0)
            {
                if (p.Note == 97) KeyOff(ch);
                else
                {
                    // FT2 uses the channel's CURRENT relative note and finetune here, not the new instrument's
                    int note = (((p.Note - 1) + ch.RelativeNote) * 16) + ((ch.Finetune >> 3) + 16);
                    if (note >= 0 && note < 10 * 12 * 16)
                    {
                        ch.PortaTarget = PeriodLut(note);
                        if (ch.PortaTarget == ch.RealPeriod) ch.PortaDir = 0;
                        else if (ch.PortaTarget > ch.RealPeriod) ch.PortaDir = 1;
                        else ch.PortaDir = 2;
                    }
                }
            }
            if (inst > 0)
            {
                ResetVolumes(ch);
                if (p.Note != 97) TriggerInstrument(ch);
            }
        }

        void TriggerNote(int note, int efx, int efxData, Channel ch)
        {
            if (note == 97) { KeyOff(ch); return; }
            if (note == 0)
            {
                note = ch.NoteNum;                     // retrigger (E9x / ED x): the channel's note
                if (note == 0) return;
            }
            ch.NoteNum = note;
            var ins = InstrumentAt(ch.InstrNum);
            ch.Ins = ins;
            if (note > 96) note = 96;
            ch.SmpNum = ins.NoteToSample[note - 1] & 0xF;
            var s = ins.Samples[ch.SmpNum];
            ch.Smp = s;
            ch.RelativeNote = s.RelativeNote;
            note = (note + ch.RelativeNote) & 0xFF;    // FT2: a byte; a negative result reads as >= 120
            if (note >= 10 * 12) return;
            ch.OldVol = s.Volume;
            ch.OldPan = s.Panning;
            if (efx == 0xE && (efxData & 0xF0) == 0x50) ch.Finetune = ((efxData & 0x0F) * 16) - 128;   // E5x
            else ch.Finetune = s.Finetune;
            if (note != 0)
            {
                int noteIndex = ((note - 1) * 16) + ((ch.Finetune >> 3) + 16);
                ch.OutPeriod = ch.RealPeriod = PeriodLut(noteIndex);
            }
            ch.TriggerVoice = true;
            ch.QuickRamp = true;
            if (efx == 9)
            {
                if (efxData > 0) ch.SampleOffset = ch.EfxData;
                ch.SmpStartPos = ch.SampleOffset << 8;
            }
            else ch.SmpStartPos = 0;
        }

        void TriggerInstrument(Channel ch)
        {
            if ((ch.VibTremCtrl & 0x04) == 0) ch.VibPos = 0;
            if ((ch.VibTremCtrl & 0x40) == 0) ch.TremoloPos = 0;
            ch.KeyOff = false;
            var ins = ch.Ins;
            if ((ins.VolFlags & 1) != 0) { ch.VolEnvTick = 65535; ch.VolEnvPos = 0; }
            if ((ins.PanFlags & 1) != 0) { ch.PanEnvTick = 65535; ch.PanEnvPos = 0; }
            ch.FadeoutSpeed = ins.Fadeout;
            ch.FadeoutVol = 32768;                     // FT2's real range, not the 65536 of the format doc
            if (ins.VibDepth > 0)
            {
                ch.AutoVibPos = 0;
                if (ins.VibSweep > 0) { ch.AutoVibAmp = 0; ch.AutoVibSweep = ((ins.VibDepth << 8) / ins.VibSweep) & 0xFFFF; }
                else { ch.AutoVibAmp = ins.VibDepth << 8; ch.AutoVibSweep = 0; }
            }
        }

        void KeyOff(Channel ch)
        {
            ch.KeyOff = true;
            var ins = ch.Ins;
            if ((ins.VolFlags & 1) != 0)
            {
                if (ch.VolEnvTick >= ins.VolPoints[ch.VolEnvPos, 0]) ch.VolEnvTick = (ins.VolPoints[ch.VolEnvPos, 0] - 1) & 0xFFFF;
            }
            else
            {
                ch.RealVol = 0; ch.OutVol = 0; ch.QuickRamp = true;
            }
            if ((ins.PanFlags & 1) == 0)               // FT2 logic bug: adjusts the pan envelope only when it is OFF
            {
                if (ch.PanEnvTick >= ins.PanPoints[ch.PanEnvPos, 0]) ch.PanEnvTick = (ins.PanPoints[ch.PanEnvPos, 0] - 1) & 0xFFFF;
            }
        }

        static void ResetVolumes(Channel ch)
        {
            ch.RealVol = ch.OldVol;
            ch.OutVol = ch.OldVol;
            ch.OutPan = ch.OldPan;
            ch.QuickRamp = true;
        }

        void HandleEffectsTickZero(Channel ch)
        {
            // volume column
            int vc = ch.VolCol;
            switch (vc >> 4)
            {
                case 1: case 2: case 3: case 4: case 5:
                {
                    int v = vc - 16;
                    if (v > 64) v = 64;
                    ch.OutVol = ch.RealVol = v; ch.QuickRamp = true;
                    break;
                }
                case 8:
                {
                    int v = ch.RealVol - (vc & 0x0F);
                    if (v < 0) v = 0;
                    ch.OutVol = ch.RealVol = v;
                    break;
                }
                case 9:
                {
                    int v = ch.RealVol + (vc & 0x0F);
                    if (v > 64) v = 64;
                    ch.OutVol = ch.RealVol = v;
                    break;
                }
                case 0xA:
                {
                    int v = (vc & 0x0F) * 4;
                    if (v != 0) ch.VibSpeed = v;
                    break;
                }
                case 0xC:
                    ch.OutPan = (vc & 0x0F) << 4;
                    break;
            }

            int param = ch.EfxData;
            if (ch.Efx == 0 && param == 0) return;
            if (ch.Efx == 8) ch.OutPan = param;
            else if (ch.Efx == 0xC)
            {
                if (param > 64) param = 64;
                ch.OutVol = ch.RealVol = param; ch.QuickRamp = true;
            }
            HandleMoreEffectsTickZero(ch);
        }

        void HandleMoreEffectsTickZero(Channel ch)
        {
            int param = ch.EfxData;
            switch (ch.Efx)
            {
                case 0xB:                                                   // position jump
                {
                    int pos = param - 1;
                    if (pos < 0 || pos >= _module.SongLength) _bxxOverflow = true;
                    else _songPos = pos;
                    _pBreakPos = 0;
                    _posJumpFlag = true;
                    break;
                }
                case 0xD:                                                   // pattern break, decimal parameter
                {
                    int p = ((param >> 4) * 10) + (param & 0x0F);
                    _pBreakPos = p <= 63 ? p : 0;
                    _posJumpFlag = true;
                    break;
                }
                case 0xE:
                    EEffectsTickZero(ch, param);
                    break;
                case 0xF:                                                   // set speed / BPM
                    if (param >= 32) SetBpm(param);
                    else _tick = _speed = param;
                    break;
                case 0x10:                                                  // G: set global volume
                    _globalVolume = param > 64 ? 64 : param;
                    break;
            }
        }

        void EEffectsTickZero(Channel ch, int param)
        {
            int efx = param >> 4;
            param &= 0x0F;
            switch (efx)
            {
                case 0x1:                                                   // fine porta up
                    if (param == 0) param = ch.FPitchUpSpeed;
                    ch.FPitchUpSpeed = param;
                    ch.RealPeriod = (ch.RealPeriod - param * 4) & 0xFFFF;
                    if ((short)ch.RealPeriod < 1) ch.RealPeriod = 1;
                    ch.OutPeriod = ch.RealPeriod;
                    break;
                case 0x2:                                                   // fine porta down
                    if (param == 0) param = ch.FPitchDownSpeed;
                    ch.FPitchDownSpeed = param;
                    ch.RealPeriod = (ch.RealPeriod + param * 4) & 0xFFFF;
                    if ((short)ch.RealPeriod >= 32000) ch.RealPeriod = 32000 - 1;
                    ch.OutPeriod = ch.RealPeriod;
                    break;
                case 0x3: ch.SemitonePorta = param != 0; break;
                case 0x4: ch.VibTremCtrl = (ch.VibTremCtrl & 0xF0) | param; break;
                case 0x7: ch.VibTremCtrl = (param << 4) | (ch.VibTremCtrl & 0x0F); break;
                case 0xA:                                                   // fine volume slide up
                    if (param == 0) param = ch.FVolUpSpeed;
                    ch.FVolUpSpeed = param;
                    ch.RealVol += param;
                    if (ch.RealVol > 64) ch.RealVol = 64;
                    ch.OutVol = ch.RealVol;
                    break;
                case 0xB:                                                   // fine volume slide down
                    if (param == 0) param = ch.FVolDownSpeed;
                    ch.FVolDownSpeed = param;
                    ch.RealVol -= param;
                    if (ch.RealVol < 0) ch.RealVol = 0;
                    ch.OutVol = ch.RealVol;
                    break;
                case 0xC:                                                   // note cut with parameter 0
                    if (param == 0) { ch.RealVol = 0; ch.OutVol = 0; ch.QuickRamp = true; }
                    break;
                case 0xE:                                                   // pattern delay
                    if (_pattDelTime2 == 0) _pattDelTime = param + 1;
                    break;
            }
        }

        // ------------------------------------------------------------------------------------- other ticks

        void HandleEffectsTickNonZero(Channel ch)
        {
            int vc = ch.VolCol;
            switch (vc >> 4)
            {
                case 6:
                {
                    int v = ch.RealVol - (vc & 0x0F);
                    if (v < 0) v = 0;
                    ch.OutVol = ch.RealVol = v;
                    break;
                }
                case 7:
                {
                    int v = ch.RealVol + (vc & 0x0F);
                    if (v > 64) v = 64;
                    ch.OutVol = ch.RealVol = v;
                    break;
                }
                case 0xB:
                {
                    int p = vc & 0x0F;
                    if (p > 0) ch.VibDepth = p;
                    DoVibrato(ch);
                    break;
                }
                case 0xD:
                {
                    int p = ch.OutPan - (vc & 0x0F);
                    if (p < 0) p = 0;
                    ch.OutPan = p;
                    break;
                }
                case 0xE:
                {
                    int p = ch.OutPan + (vc & 0x0F);
                    if (p > 255) p = 255;
                    ch.OutPan = p;
                    break;
                }
                case 0xF:
                    Portamento(ch);
                    break;
            }

            int param = ch.EfxData;
            if ((ch.Efx == 0 && param == 0) || ch.Efx > 35) return;
            switch (ch.Efx)
            {
                case 0x0: Arpeggio(ch, param); break;
                case 0x1: PitchSlideUp(ch, param); break;
                case 0x2: PitchSlideDown(ch, param); break;
                case 0x3: Portamento(ch); break;
                case 0x4: Vibrato(ch, param); break;
                case 0x5: Portamento(ch); VolSlide(ch, param); break;
                case 0x6: DoVibrato(ch); VolSlide(ch, param); break;
                case 0x7: Tremolo(ch, param); break;
                case 0xA: VolSlide(ch, param); break;
                case 0xE: EEffectsTickNonZero(ch, param); break;
                case 0x11:                                                  // H: global volume slide
                {
                    if (param == 0) param = ch.VolSlideSpeed;
                    if ((param & 0xF0) == 0) { _globalVolume -= param; if (_globalVolume < 0) _globalVolume = 0; }
                    else { _globalVolume += param >> 4; if (_globalVolume > 64) _globalVolume = 64; }
                    break;
                }
                case 0x14:                                                  // K: key off at tick
                    if (((_speed - _tick) & 0xFF) == param) KeyOff(ch);
                    break;
            }
        }

        void EEffectsTickNonZero(Channel ch, int param)
        {
            int efx = param >> 4;
            param &= 0x0F;
            switch (efx)
            {
                case 0x9:                                                   // retrigger note
                    if (param == 0) return;
                    if ((_speed - _tick) % param == 0)
                    {
                        TriggerNote(0, 0, 0, ch);
                        TriggerInstrument(ch);
                    }
                    break;
                case 0xC:                                                   // note cut
                    if (((_speed - _tick) & 0xFF) == param) { ch.OutVol = ch.RealVol = 0; ch.QuickRamp = true; }
                    break;
                case 0xD:                                                   // note delay
                    if (((_speed - _tick) & 0xFF) == param)
                    {
                        int note = ch.CopyOfInstrAndNote & 0xFF;
                        TriggerNote(note, 0, 0, ch);
                        int instrument = ch.CopyOfInstrAndNote >> 8;
                        if (instrument > 0) ResetVolumes(ch);
                        TriggerInstrument(ch);
                        if (ch.VolCol >= 0x10 && ch.VolCol <= 0x50) { ch.OutVol = ch.VolCol - 16; ch.RealVol = ch.OutVol; }
                        else if (ch.VolCol >= 0xC0 && ch.VolCol <= 0xCF) ch.OutPan = (ch.VolCol & 0x0F) << 4;
                    }
                    break;
            }
        }

        /// <summary>FT2's period2NotePeriod: the period snapped to the nearest note of the channel's finetune, plus
        /// an offset in semitones (arpeggio, semitone-mode portamento), with FT2's 8-octave clamp bug.</summary>
        int PeriodToNotePeriod(int period, int noteOffset, Channel ch)
        {
            int fineTune = (ch.Finetune >> 3) + 16;
            int hiPeriod = 8 * 12 * 16, loPeriod = 0, tmpPeriod;
            for (int i = 0; i < 8; i++)
            {
                tmpPeriod = (((loPeriod + hiPeriod) >> 1) & ~15) + fineTune;
                int lookUp = tmpPeriod - 8;
                if (lookUp < 0) lookUp = 0;
                if (period >= PeriodLut(lookUp)) hiPeriod = (tmpPeriod - fineTune) & ~15;
                else loPeriod = (tmpPeriod - fineTune) & ~15;
            }
            tmpPeriod = loPeriod + fineTune + (noteOffset << 4);
            if (tmpPeriod >= (8 * 12 * 16 + 15) - 1) tmpPeriod = (8 * 12 * 16 + 16) - 1;
            return PeriodLut(tmpPeriod);
        }

        void DoVibrato(Channel ch)
        {
            int tmpVib = (ch.VibPos >> 2) & 0x1F;
            switch (ch.VibTremCtrl & 3)
            {
                case 0: tmpVib = VibratoTab[tmpVib]; break;
                case 1:
                    tmpVib <<= 3;
                    if ((sbyte)ch.VibPos < 0) tmpVib = (~tmpVib) & 0xFF;
                    break;
                default: tmpVib = 255; break;
            }
            tmpVib = (tmpVib * ch.VibDepth) >> 5;
            if ((sbyte)ch.VibPos < 0) ch.OutPeriod = (ch.RealPeriod - tmpVib) & 0xFFFF;
            else ch.OutPeriod = (ch.RealPeriod + tmpVib) & 0xFFFF;
            ch.VibPos = (ch.VibPos + ch.VibSpeed) & 0xFF;
        }

        void Arpeggio(Channel ch, int param)
        {
            int tick = ArpeggioTab[_tick & 31];
            if (tick == 0) ch.OutPeriod = ch.RealPeriod;
            else ch.OutPeriod = PeriodToNotePeriod(ch.RealPeriod, tick == 1 ? param >> 4 : param & 0x0F, ch);
        }

        static void PitchSlideUp(Channel ch, int param)
        {
            if (param == 0) param = ch.PitchUpSpeed;
            ch.PitchUpSpeed = param;
            ch.RealPeriod = (ch.RealPeriod - param * 4) & 0xFFFF;
            if ((short)ch.RealPeriod < 1) ch.RealPeriod = 1;
            ch.OutPeriod = ch.RealPeriod;
        }

        static void PitchSlideDown(Channel ch, int param)
        {
            if (param == 0) param = ch.PitchDownSpeed;
            ch.PitchDownSpeed = param;
            ch.RealPeriod = (ch.RealPeriod + param * 4) & 0xFFFF;
            if ((short)ch.RealPeriod >= 32000) ch.RealPeriod = 32000 - 1;    // FT2 bug: signed compare
            ch.OutPeriod = ch.RealPeriod;
        }

        void Portamento(Channel ch)
        {
            if (ch.PortaDir == 0) return;
            if (ch.PortaDir > 1)
            {
                ch.RealPeriod = (ch.RealPeriod - ch.PortaSpeed) & 0xFFFF;
                if ((short)ch.RealPeriod <= (short)ch.PortaTarget) { ch.PortaDir = 1; ch.RealPeriod = ch.PortaTarget; }
            }
            else
            {
                ch.RealPeriod = (ch.RealPeriod + ch.PortaSpeed) & 0xFFFF;
                if (ch.RealPeriod >= ch.PortaTarget) { ch.PortaDir = 1; ch.RealPeriod = ch.PortaTarget; }
            }
            ch.OutPeriod = ch.SemitonePorta ? PeriodToNotePeriod(ch.RealPeriod, 0, ch) : ch.RealPeriod;
        }

        void Vibrato(Channel ch, int param)
        {
            if (param > 0)
            {
                int depth = param & 0x0F;
                if (depth > 0) ch.VibDepth = depth;
                int speed = (param & 0xF0) >> 2;
                if (speed > 0) ch.VibSpeed = speed;
            }
            DoVibrato(ch);
        }

        void Tremolo(Channel ch, int param)
        {
            if (param > 0)
            {
                int depth = param & 0x0F;
                if (depth > 0) ch.TremoloDepth = depth;
                int speed = (param & 0xF0) >> 2;
                if (speed > 0) ch.TremoloSpeed = speed;
            }
            int tmpTrem = (ch.TremoloPos >> 2) & 0x1F;
            switch ((ch.VibTremCtrl >> 4) & 3)
            {
                case 0: tmpTrem = VibratoTab[tmpTrem]; break;
                case 1:
                    tmpTrem <<= 3;
                    if ((sbyte)ch.VibPos < 0) tmpTrem = (~tmpTrem) & 0xFF;     // FT2 bug: tests the VIBRATO position
                    break;
                default: tmpTrem = 255; break;
            }
            tmpTrem = (tmpTrem * ch.TremoloDepth) >> 6;
            int vol;
            if ((sbyte)ch.TremoloPos < 0) { vol = ch.RealVol - tmpTrem; if (vol < 0) vol = 0; }
            else { vol = ch.RealVol + tmpTrem; if (vol > 64) vol = 64; }
            ch.OutVol = vol;
            ch.TremoloPos = (ch.TremoloPos + ch.TremoloSpeed) & 0xFF;
        }

        static void VolSlide(Channel ch, int param)
        {
            if (param == 0) param = ch.VolSlideSpeed;
            ch.VolSlideSpeed = param;
            int v = ch.RealVol;
            if ((param & 0xF0) == 0) { v -= param; if (v < 0) v = 0; }
            else { v += param >> 4; if (v > 64) v = 64; }
            ch.OutVol = ch.RealVol = v;
        }

        // ------------------------------------------------------------------- envelopes, fadeout, auto-vibrato

        /// <summary>FT2's updateVolPanAutoVib, every tick: fadeout, the volume envelope (8.8 fixed point, the
        /// value stepping by (dy*256)/dx per tick between points, sustain holding at the sustain point until key
        /// off, loop jumping back at the loop end), the panning envelope scaled by the room the pan has to move,
        /// and auto-vibrato; leaves FinalVol (0..1), FinalPan (0..255) and FinalPeriod for the mixer.</summary>
        void UpdateVolPanAutoVib(Channel ch)
        {
            var ins = ch.Ins;
            if (ch.KeyOff)
            {
                if (ch.FadeoutSpeed > ch.FadeoutVol) { ch.FadeoutVol = 0; ch.FadeoutSpeed = 0; }
                else ch.FadeoutVol -= ch.FadeoutSpeed;
            }

            float fVol;
            if ((ins.VolFlags & 1) != 0)
            {
                int envVal = RunEnvelope(ins.VolPoints, ins.VolLength, ins.VolFlags, ins.VolSustain, ins.VolLoopStart, ins.VolLoopEnd, ch.KeyOff,
                                         ref ch.VolEnvTick, ref ch.VolEnvPos, ref ch.VolEnvValue, ref ch.VolEnvDelta);
                float fEnvVal = (envVal & 0xFFFF) * (1.0f / (64.0f * 256.0f));
                if (fEnvVal > 1.0f) fEnvVal = 1.0f;
                fVol = (_globalVolume * ch.OutVol * ch.FadeoutVol) * (1.0f / (64.0f * 64.0f * 32768.0f)) * fEnvVal;
            }
            else
                fVol = (_globalVolume * ch.OutVol * ch.FadeoutVol) * (1.0f / (64.0f * 64.0f * 32768.0f));
            if (fVol > 1.0f) fVol = 1.0f;
            ch.FinalVol = fVol;

            if ((ins.PanFlags & 1) != 0)
            {
                int envVal = RunEnvelope(ins.PanPoints, ins.PanLength, ins.PanFlags, ins.PanSustain, ins.PanLoopStart, ins.PanLoopEnd, ch.KeyOff,
                                         ref ch.PanEnvTick, ref ch.PanEnvPos, ref ch.PanEnvValue, ref ch.PanEnvDelta);
                int panMul = ch.OutPan - 128;
                if (panMul >= 0) panMul = -panMul;
                panMul += 128;
                panMul <<= 3;
                envVal -= 32 * 256;
                int panAdd = (sbyte)((envVal * panMul) >> 16);
                ch.FinalPan = (ch.OutPan + panAdd) & 0xFF;
            }
            else ch.FinalPan = ch.OutPan;

            if (ins.VibDepth > 0)
            {
                int autoVibAmp;
                if (ch.AutoVibSweep > 0)
                {
                    autoVibAmp = ch.AutoVibSweep;
                    if (!ch.KeyOff)
                    {
                        autoVibAmp = (autoVibAmp + ch.AutoVibAmp) & 0xFFFF;
                        if ((autoVibAmp >> 8) > ins.VibDepth) { autoVibAmp = ins.VibDepth << 8; ch.AutoVibSweep = 0; }
                        ch.AutoVibAmp = autoVibAmp;
                    }
                }
                else autoVibAmp = ch.AutoVibAmp;
                ch.AutoVibPos = (ch.AutoVibPos + ins.VibRate) & 0xFF;
                int autoVibVal;
                if (ins.VibType == 1) autoVibVal = ch.AutoVibPos > 127 ? 64 : -64;
                else if (ins.VibType == 2) autoVibVal = (((ch.AutoVibPos >> 1) + 64) & 127) - 64;
                else if (ins.VibType == 3) autoVibVal = ((-(ch.AutoVibPos >> 1) + 64) & 127) - 64;
                else autoVibVal = AutoVibSineTab[ch.AutoVibPos];
                autoVibVal = (autoVibVal * (short)autoVibAmp) >> (6 + 8);
                int tmpPeriod = (ch.OutPeriod + autoVibVal) & 0xFFFF;
                if (tmpPeriod >= 32000) tmpPeriod = 0;
                ch.FinalPeriod = tmpPeriod;
            }
            else ch.FinalPeriod = ch.OutPeriod;
        }

        /// <summary>One tick of an FT2 envelope; returns the 8.8 value (0..64*256).</summary>
        static int RunEnvelope(int[,] pts, int length, int flags, int sustain, int loopStart, int loopEnd, bool keyOff,
                               ref int envTick, ref int envPosRef, ref int envValue, ref int envDelta)
        {
            bool didInterpolate = false;
            int envVal = 0;
            int envPos = envPosRef;
            envTick = (envTick + 1) & 0xFFFF;
            if (envTick == pts[envPos, 0])
            {
                envValue = (short)((sbyte)pts[envPos, 1] << 8);
                envPos++;
                if ((flags & 4) != 0)
                {
                    envPos--;
                    if (envPos == loopEnd)
                    {
                        if ((flags & 2) == 0 || envPos != sustain || !keyOff)
                        {
                            envPos = loopStart;
                            envTick = pts[envPos, 0];
                            envValue = (short)((sbyte)pts[envPos, 1] << 8);
                        }
                    }
                    envPos++;
                }
                if (envPos < length)
                {
                    bool interpolate = true;
                    if ((flags & 2) != 0 && !keyOff)
                    {
                        if (envPos - 1 == sustain) { envPos--; envDelta = 0; interpolate = false; }
                    }
                    if (interpolate)
                    {
                        envPosRef = envPos;
                        int x0 = pts[envPos - 1, 0], x1 = pts[envPos, 0];
                        int xDiff = (short)(x1 - x0);
                        if (xDiff > 0)
                        {
                            int yDiff = (sbyte)(pts[envPos, 1] - pts[envPos - 1, 1]);
                            envDelta = (short)((yDiff << 8) / xDiff);
                            envVal = envValue;
                            didInterpolate = true;
                        }
                        else envDelta = 0;
                    }
                }
                else envDelta = 0;
            }
            if (!didInterpolate)
            {
                envValue = (short)(envValue + envDelta);
                envVal = envValue;
                int hi = (envVal >> 8) & 0xFF;                 // FT2 tests the high byte unsigned
                if (hi > 64)
                {
                    envVal = hi <= 160 ? 64 * 256 : 0;
                    envDelta = 0;
                }
            }
            return envVal;
        }

        // ---------------------------------------------------------------------------------------- the mixer

        /// <summary>FT2's updateVoices after a tick: start triggered voices (the old one ramps out over 5 ms while
        /// the new one ramps in), apply the period, and set this tick's volume targets, ramped over 5 ms after a
        /// note/volume command or across the whole tick otherwise.</summary>
        void UpdateVoices(int samplesThisTick)
        {
            foreach (var ch in _channels)
            {
                var v = ch.V;
                if (ch.TriggerVoice)
                {
                    ch.TriggerVoice = false;
                    if (v.Active)
                    {
                        ch.F.CopyFrom(v);
                        ch.F.SetTarget(0f, 0f, _quickRampSamples);
                    }
                    var s = ch.Smp;
                    v.Active = false;
                    if (s != null && s.Length > 0 && ch.SmpStartPos < s.Length)
                    {
                        v.Data = s.Data; v.End = s.Length; v.Loops = s.Loops; v.LoopLength = s.LoopLength;
                        v.Pos = (long)ch.SmpStartPos << 32;
                        v.VolL = v.VolR = 0f;
                        v.Active = true;
                    }
                }
                if (!v.Active) { ch.QuickRamp = false; continue; }

                double freq = ch.FinalPeriod == 0 ? 0.0 : 8363.0 * Math.Pow(2.0, (4608 - ch.FinalPeriod) / 768.0);
                v.Step = (long)(freq / OutputRate * 4294967296.0 + 0.5);

                int pan = ch.FinalPan;
                float vol = ch.FinalVol * MasterVolume;
                v.SetTarget(vol * SqrtPanTab[256 - pan], vol * SqrtPanTab[pan], ch.QuickRamp ? _quickRampSamples : samplesThisTick);
                ch.QuickRamp = false;
            }
        }

        void MixAll(float[] buf, int offset, int n)
        {
            foreach (var ch in _channels)
            {
                if (ch.F.Active)
                {
                    MixVoice(ch.F, buf, offset, n);
                    if (ch.F.RampLeft == 0) ch.F.Active = false;   // the replaced voice is silent once ramped out
                }
                if (ch.V.Active) MixVoice(ch.V, buf, offset, n);
            }
        }

        static void MixVoice(Voice v, float[] buf, int offset, int n)
        {
            var data = v.Data;
            long pos = v.Pos, step = v.Step, end = (long)v.End << 32, loopLen = (long)v.LoopLength << 32;
            float volL = v.VolL, volR = v.VolR, dL = v.DeltaL, dR = v.DeltaR;
            int ramp = v.RampLeft;
            int o = offset;
            for (int i = 0; i < n; i++)
            {
                if (pos >= end)
                {
                    if (!v.Loops || loopLen <= 0) { v.Active = false; break; }
                    do pos -= loopLen; while (pos >= end);
                }
                int idx = (int)(pos >> 32);
                float frac = (uint)pos * (1f / 4294967296f);
                float s0 = data[idx];
                float s = s0 + (data[idx + 1] - s0) * frac;
                buf[o] += s * volL;
                buf[o + 1] += s * volR;
                o += 2;
                pos += step;
                if (ramp > 0)
                {
                    volL += dL; volR += dR;
                    if (--ramp == 0) { volL = v.TargetL; volR = v.TargetR; }
                }
            }
            v.Pos = pos; v.VolL = volL; v.VolR = volR; v.RampLeft = ramp;
        }
    }
}
