using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>A particle definition, read from the game's executable (0x8008A9C8 creates particles from one,
    /// 0x8008AC7C / 0x8008B1A0 / 0x8008B2DC move and colour them, 0x8008B014 draws them). Byte offsets:
    /// <code>
    ///   +0x00 u16 lifetime (ticks)          +0x02 u8 flags (bit 0 special draw, bit 2 wind)   +0x03 u8 ground (bit 0 die, bit 1 bounce)
    ///   +0x04 u32 per-frame callback        +0x08 u32 init callback
    ///   +0x0C u16 sprite in the effects sheet (#416)       +0x0E u8 blend: 0 solid, 1 page mode, 2 sub, 3 add, 4 add a quarter
    ///   +0x0F rgb start, +0x12 rgb middle, +0x15 rgb end    (128 = the texel as is)
    ///   +0x18 s16 size start, +0x1A middle, +0x1C end, stepped every half-life
    ///   +0x1E s16 vertical speed   +0x20 s16 vertical pull (subtracted per tick)   +0x22 s16 pull change per tick
    ///   +0x24 s16 spawn jitter (x and z, +-)   +0x26 s16 heading   +0x28 s16 speed (&lt;&lt; 4)   +0x2A s16 heading jitter
    ///   +0x2C s16 speed change per tick         +0x2E s16 heading change per tick
    /// </code></summary>
    public sealed class ParticleDef
    {
        public int Lifetime, Flags, Ground, Sprite, Blend;
        public (int R, int G, int B) Colour0, Colour1, Colour2;
        public int Size0, Size1, Size2;
        public int VerticalSpeed, Pull, PullChange, Jitter, Heading, Speed, HeadingJitter, SpeedChange, HeadingChange;
        public bool HasCallbacks;

        public static ParticleDef Read(byte[] exe, uint baseAddress, uint address)
        {
            int o = (int)(address - baseAddress);
            if (o < 0 || o + 0x30 > exe.Length) return null;
            short S(int at) => BitConverter.ToInt16(exe, o + at);
            return new ParticleDef
            {
                Lifetime = BitConverter.ToUInt16(exe, o), Flags = exe[o + 2], Ground = exe[o + 3],
                HasCallbacks = BitConverter.ToUInt32(exe, o + 4) != 0 || BitConverter.ToUInt32(exe, o + 8) != 0,
                Sprite = BitConverter.ToUInt16(exe, o + 0x0C), Blend = exe[o + 0x0E],
                Colour0 = (exe[o + 0x0F], exe[o + 0x10], exe[o + 0x11]),
                Colour1 = (exe[o + 0x12], exe[o + 0x13], exe[o + 0x14]),
                Colour2 = (exe[o + 0x15], exe[o + 0x16], exe[o + 0x17]),
                Size0 = S(0x18), Size1 = S(0x1A), Size2 = S(0x1C),
                VerticalSpeed = S(0x1E), Pull = S(0x20), PullChange = S(0x22), Jitter = S(0x24),
                Heading = S(0x26), Speed = S(0x28), HeadingJitter = S(0x2A), SpeedChange = S(0x2C), HeadingChange = S(0x2E),
            };
        }
    }

    /// <summary>An emitter template: `s16 lifetime (ticks, -1 forever), u16 spawn interval (ticks), u32 particle
    /// definition, u32 init callback` (0x8008A6D8, 0x8008A774).</summary>
    public sealed class EmitterTemplate
    {
        public int Lifetime, Interval;
        public ParticleDef Particle;

        public static EmitterTemplate Read(byte[] exe, uint baseAddress, uint address)
        {
            int o = (int)(address - baseAddress);
            if (o < 0 || o + 12 > exe.Length) return null;
            var def = ParticleDef.Read(exe, baseAddress, BitConverter.ToUInt32(exe, o + 4));
            return def == null ? null : new EmitterTemplate { Lifetime = BitConverter.ToInt16(exe, o), Interval = BitConverter.ToUInt16(exe, o + 2), Particle = def };
        }
    }

    /// <summary>One live particle, in the game's own fixed point.</summary>
    public sealed class Particle
    {
        public ParticleDef Def;
        public int X, Y, Z;                 // world units (y up)
        public int VX, VY, VZ;              // 8.8 per tick
        public int Pull, Speed, Heading;
        public int Life, Stage, StageTicks, ColourUpdates;
        public int Size;                    // 8.8
        public int SizeStep;                // 8.8 per tick
        public int R, G, B;                 // 8.8
        public int DR, DG, DB;              // 8.8 per update
        public int ColourStage;
        public int SizeUnits => Size >> 8;
    }

    /// <summary>The game's particle effects: emitters spawning particles that rise, grow, shrink and change colour.
    ///
    /// ⭐ EVERY RULE IS THE GAME'S. Time runs in ticks, frame time &gt;&gt; 12 (a tick is 4096 of the timer's units,
    /// 1/61.5 s); the park runs a frame per clock tick, 25 a second at PAL's 50 Hz, and the shift truncates, so each
    /// frame advances TWO ticks. Per frame an emitter adds its ticks to a timer and, past its interval, resets it and
    /// spawns one particle (0x8008A774). A particle (0x8008AC7C) loses its ticks from its life, moves by its velocity's
    /// high byte per tick, has its vertical velocity reduced by its pull per tick, steps its size every half-life to
    /// the next of three sizes, and (0x8008B1A0, once per FRAME, not per tick) its colour towards the next of three
    /// colours. Its horizontal velocity is its speed along its heading (0x8008B2DC). Spawn position: the emitter's,
    /// plus rand(2j) - j in x and z (0x8008A9C8).</summary>
    public sealed class ParticleSystem
    {
        public const int TicksPerFrame = 2;
        public const double FramesPerSecond = 25;

        public sealed class Emitter
        {
            public EmitterTemplate Template;
            public int X, Y, Z;
            public int Timer, Life;
        }

        public readonly List<Emitter> Emitters = new();
        public readonly List<Particle> Particles = new();
        readonly Random _rand;
        /// <summary>The game's pools: 32 emitters, 128 particles (0x80089AF8).</summary>
        public const int MaxParticles = 128;

        public ParticleSystem(int seed = 1) { _rand = new Random(seed); }

        public Emitter Add(EmitterTemplate t, int x, int y, int z)
        {
            var e = new Emitter { Template = t, X = x, Y = y, Z = z, Life = t.Lifetime };
            Emitters.Add(e);
            return e;
        }

        int Rand(int n) => n <= 0 ? 0 : _rand.Next(n);

        /// <summary>One park frame.</summary>
        public void Step()
        {
            int ticks = TicksPerFrame;
            for (int i = Emitters.Count - 1; i >= 0; i--)
            {
                var e = Emitters[i];
                e.Timer += ticks;
                if (e.Timer > e.Template.Interval)
                {
                    e.Timer = 0;
                    if (Particles.Count < MaxParticles) Particles.Add(Spawn(e));
                }
                if (e.Template.Lifetime >= 0)
                {
                    e.Life -= ticks;
                    if (e.Life < 0) Emitters.RemoveAt(i);
                }
            }
            for (int i = Particles.Count - 1; i >= 0; i--)
            {
                var p = Particles[i];
                Update(p, ticks);
                if (p.Life < 1) Particles.RemoveAt(i);
            }
        }

        Particle Spawn(Emitter e)
        {
            var d = e.Template.Particle;
            int half = Math.Max(1, d.Lifetime >> 1);
            var p = new Particle
            {
                Def = d, Life = d.Lifetime, X = e.X, Y = e.Y, Z = e.Z,
                VY = d.VerticalSpeed, Pull = d.Pull, Speed = d.Speed << 4, Heading = d.Heading,
                Size = d.Size0 << 8, SizeStep = ((d.Size1 - d.Size0) << 8) / half,
                R = d.Colour0.R << 8, G = d.Colour0.G << 8, B = d.Colour0.B << 8,
                DR = ((d.Colour1.R - d.Colour0.R) << 8) / half, DG = ((d.Colour1.G - d.Colour0.G) << 8) / half, DB = ((d.Colour1.B - d.Colour0.B) << 8) / half,
            };
            if (d.HeadingJitter != 0) p.Heading += Rand(d.HeadingJitter * 2) - d.HeadingJitter;
            if (d.Jitter != 0) { p.X += Rand(d.Jitter * 2) - d.Jitter; p.Z += Rand(d.Jitter * 2) - d.Jitter; }
            Steer(p);
            return p;
        }

        static void Steer(Particle p)
        {
            int a = p.Heading & 0xFFF, sp = (short)p.Speed >> 4;
            p.VX = (sp * EntranceFlags.Sin(a)) >> 8;
            p.VZ = (sp * EntranceFlags.Sin((a + 0x400) & 0xFFF)) >> 8;
        }

        void Update(Particle p, int ticks)
        {
            var d = p.Def;
            int half = Math.Max(1, d.Lifetime >> 1);
            p.Life -= ticks;
            p.X += (sbyte)((ushort)p.VX >> 8) * ticks;
            p.Y += (sbyte)((ushort)p.VY >> 8) * ticks;
            p.Z += (sbyte)((ushort)p.VZ >> 8) * ticks;
            p.VY = (short)(p.VY - p.Pull * ticks);
            p.Pull = (short)(p.Pull + d.PullChange * ticks);
            p.Speed = (short)(p.Speed + d.SpeedChange * ticks);
            p.Heading = (short)(p.Heading + d.HeadingChange * ticks);
            p.StageTicks += ticks;
            if (p.StageTicks > half)
            {
                p.StageTicks = 0;
                p.Stage++;
                int from = p.Stage == 1 ? d.Size1 : d.Size2, to = p.Stage == 1 ? d.Size2 : d.Size2;
                p.SizeStep = ((to - from) << 8) / half;
            }
            p.Size += p.SizeStep * ticks;
            // Colour, per frame (0x8008B1A0).
            p.ColourUpdates++;
            if (p.ColourUpdates > half)
            {
                p.ColourUpdates = 0;
                p.DR = ((d.Colour2.R << 8) - p.R) / half; p.DG = ((d.Colour2.G << 8) - p.G) / half; p.DB = ((d.Colour2.B << 8) - p.B) / half;
            }
            p.R += p.DR; p.G += p.DG; p.B += p.DB;
            Steer(p);
        }
    }
}
