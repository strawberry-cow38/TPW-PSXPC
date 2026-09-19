using System;

namespace TPW.Data
{
    /// <summary>One moving part of a park gate: model 0 of the gate's pack, at an offset from the gate's base,
    /// turned by angles about x, y and z (4096 = a full turn) when the park is closed.</summary>
    public readonly struct GatePart
    {
        public readonly int X, Y, Z;
        public readonly int RotX, RotY, RotZ;
        public GatePart(int x, int y, int z, int rx, int ry, int rz) { X = x; Y = y; Z = z; RotX = rx; RotY = ry; RotZ = rz; }
    }

    /// <summary>The park entrance's gate: the part that opens (doors, a drawbridge, a hatch), drawn by a per-world
    /// object separate from the scenery. The stone frame, torches' housings and statues around it are scenery.
    ///
    /// ⭐ FROM THE GAME: the park loader (0x800588D0) builds a per-world object (constructors 0x80061324 jungle,
    /// 0x800612F0 halloween, 0x800612BC fantasy, 0x80061288 space) and initialises it at the gate's base: the tile in
    /// the 5-byte record for the world at 0x800F2398, x·256 and z·256, with the ground's height there (0x800611A0 →
    /// 0x80050938). Its init loads the gate's own one-model pack (the same format as the scenery packs) and its
    /// per-frame method draws model 0 once per part at the base plus an offset, turned by the part's angles
    /// (0x8006E3F8). Offsets and packs are the constants each world's methods use:
    /// <list type="bullet">
    /// <item>jungle (0x8006E4A0 / 0x8006E66C): pack #87; two doors at (512, 0, 350) and (1024, 0, 350), the second
    /// a half turn round. Opening (0x8006E5D0): door 1's y angle runs to 0x400 at a speed starting 0x400 and gaining
    /// 0x100 a frame, bouncing back at half speed; door 2 is always 0x800 minus it. The torches' flames are effects
    /// at (440, 440, 200) and (1130, 440, 200).</item>
    /// <item>halloween (0x8006E7A8 / 0x8006E8C4 / 0x8006E828): pack #86; doors at (260, 0, 280) and (750, 0, 280),
    /// opening the same way.</item>
    /// <item>fantasy (0x8006EA00 / 0x8006EB0C / 0x8006EA80): pack #85; one part at (1100, 600, 190) whose z angle
    /// runs to -800 (speed -0x1000, gaining -0x400 a frame, bouncing at half): a drawbridge.</item>
    /// <item>space (0x8006EBB0 / 0x8006EDE4 / 0x8006EC8C): pack #88; one part at (800, 570, 300) whose x angle runs
    /// to 0x708 (speed 0x100, gaining 0x40, bouncing at a quarter), setting off effects at (500, 0, 240) and
    /// (1024, 0, 240) as it opens.</item>
    /// </list>
    /// The gates open only when the park is open (0x800541AC); closed, every angle is 0 but the second door's.
    /// Each pack's textures are in its world's ground sheet.</summary>
    public sealed class ParkGate
    {
        public int World { get; init; }
        public int PackEntry { get; init; }
        public int TileX { get; init; }
        public int TileZ { get; init; }
        public GatePart[] Parts { get; init; } = Array.Empty<GatePart>();
        /// <summary>Effect emitters the gate starts with: offset from the base and the emitter template's address in
        /// the executable (EmitterTemplate). The jungle's torch flames (0x8006E4A0: 0x80089E38 with 0x800F7918).</summary>
        public (int X, int Y, int Z, uint Template)[] Effects { get; init; } = Array.Empty<(int, int, int, uint)>();
        /// <summary>Effects the gate sets off as it opens, each once, in the frame its angle first passes the mark:
        /// offset from the base, emitter template, mark. The space hatch's (0x8006EC8C, offsets at 0x80102F10 and
        /// 0x80102F18): 0x800F7958 at (500, 0, 240) once the angle is above 0 and 0x800F7998 at (1024, 0, 240) once
        /// it is above 0x40, two short bursts (32 ticks) of grey additive puffs rising and drifting apart.</summary>
        public (int X, int Y, int Z, uint Template, int AfterAngle)[] OpeningEffects { get; init; } = Array.Empty<(int, int, int, uint, int)>();

        public static readonly ParkGate[] All =
        {
            new ParkGate { World = 0, PackEntry = 87, TileX = 18, TileZ = 16, Parts = new[] { new GatePart(512, 0, 350, 0, 0, 0), new GatePart(1024, 0, 350, 0, 0x800, 0) },
                           Effects = new[] { (440, 440, 200, 0x800F7918u), (1130, 440, 200, 0x800F7918u) } },
            new ParkGate { World = 1, PackEntry = 86, TileX = 19, TileZ = 17, Parts = new[] { new GatePart(260, 0, 280, 0, 0, 0), new GatePart(750, 0, 280, 0, 0x800, 0) } },
            new ParkGate { World = 2, PackEntry = 85, TileX = 18, TileZ = 16, Parts = new[] { new GatePart(1100, 600, 190, 0, 0, 0) } },
            new ParkGate { World = 3, PackEntry = 88, TileX = 18, TileZ = 16, Parts = new[] { new GatePart(800, 570, 300, 0, 0, 0) },
                           OpeningEffects = new[] { (500, 0, 240, 0x800F7958u, 0), (1024, 0, 240, 0x800F7998u, 0x40) } },
        };

        public static ParkGate ForWorld(int world) => world >= 0 && world < All.Length ? All[world] : null;

        /// <summary>A gate's moving state, updated as each world's per-frame method does once the park is open
        /// (0x8006E5D0 jungle, 0x8006E828 halloween, 0x8006EA80 fantasy, 0x8006EC8C space).</summary>
        public sealed class State
        {
            public readonly ParkGate Gate;
            /// <summary>The swinging angle (y for the doors, z for the drawbridge, x for the hatch) and its speed.</summary>
            public int Angle, Speed;
            readonly bool[] _fired;
            public State(ParkGate gate)
            {
                Gate = gate;
                Speed = gate.World switch { 2 => unchecked((int)0xFFFFF000), 3 => 0x100, _ => 0x400 };
                _fired = new bool[gate.OpeningEffects.Length];
            }

            /// <summary>The opening effects due now: those whose mark the angle has passed and that have not gone off
            /// yet. Each is returned once.</summary>
            public System.Collections.Generic.List<int> TakeDueEffects()
            {
                var due = new System.Collections.Generic.List<int>();
                for (int i = 0; i < _fired.Length; i++)
                    if (!_fired[i] && (short)Angle > Gate.OpeningEffects[i].AfterAngle) { _fired[i] = true; due.Add(i); }
                return due;
            }

            /// <summary>One frame, frameTime in the game's time units (EntranceFlags.TimeUnitsPerSecond; the game
            /// caps a frame at 0x4000).</summary>
            public void Update(int frameTime, bool parkOpen)
            {
                if (!parkOpen) return;
                frameTime = Math.Min(frameTime, 0x4000);
                int step = (int)((uint)((Speed >> 8) * frameTime) >> 12);
                switch (Gate.World)
                {
                    case 0: case 1:       // doors: to 0x400, bounce back at half speed, pulled open by 0x100 a frame
                        Angle = (ushort)(Angle + step);
                        if ((short)Angle > 0x3FF) { Angle = 0x400; Speed = -(Speed >> 1); }
                        Speed += 0x100;
                        break;
                    case 2:               // drawbridge: to -800, bounce at half, pulled by -0x400 a frame
                        Angle = (short)(Angle + step);
                        if (Angle < -799) { Angle = -800; Speed = -(Speed >> 1); }
                        Speed -= 0x400;
                        break;
                    case 3:               // hatch: to 0x708, bounce at a quarter, pulled by 0x40 a frame
                        Angle = (ushort)(Angle + step);
                        if ((short)Angle > 0x6E7) { Angle = 0x708; Speed = -(Speed >> 2); }
                        Speed += 0x40;
                        break;
                }
            }

            /// <summary>Part i with the current angle applied, as the frame method passes it to 0x8006E3F8.</summary>
            public GatePart Part(int i)
            {
                var p = Gate.Parts[i];
                return Gate.World switch
                {
                    0 or 1 => new GatePart(p.X, p.Y, p.Z, 0, i == 0 ? (short)Angle : 0x800 - (short)Angle, 0),
                    2 => new GatePart(p.X, p.Y, p.Z, 0, 0, (short)Angle),
                    _ => new GatePart(p.X, p.Y, p.Z, (short)Angle, 0, 0),
                };
            }
        }

        /// <summary>A model point placed as the game places a gate part: turned by PsyQ's RotMatrix for the part's
        /// angles (for one non-zero angle: rows (c 0 s / 0 1 0 / -s 0 c) about y, (1 0 0 / 0 c -s / 0 s c) about x,
        /// (c -s 0 / s c 0 / 0 0 1) about z), then moved to base + offset. Closed gates only use 0 and 0x800 about y,
        /// where every convention agrees.</summary>
        public static (float X, float Y, float Z) Place(GatePart p, (int X, int Y, int Z) gateBase, (float X, float Y, float Z) v)
        {
            double a;
            float x = v.X, y = v.Y, z = v.Z;
            if (p.RotY != 0) { a = p.RotY * Math.PI / 2048; float c = (float)Math.Cos(a), s = (float)Math.Sin(a); (x, z) = (c * x + s * z, -s * x + c * z); }
            if (p.RotX != 0) { a = p.RotX * Math.PI / 2048; float c = (float)Math.Cos(a), s = (float)Math.Sin(a); (y, z) = (c * y - s * z, s * y + c * z); }
            if (p.RotZ != 0) { a = p.RotZ * Math.PI / 2048; float c = (float)Math.Cos(a), s = (float)Math.Sin(a); (x, y) = (c * x - s * y, s * x + c * y); }
            return (x + gateBase.X + p.X, y + gateBase.Y + p.Y, z + gateBase.Z + p.Z);
        }
    }
}
