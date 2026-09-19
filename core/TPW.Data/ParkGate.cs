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

        public static readonly ParkGate[] All =
        {
            new ParkGate { World = 0, PackEntry = 87, TileX = 18, TileZ = 16, Parts = new[] { new GatePart(512, 0, 350, 0, 0, 0), new GatePart(1024, 0, 350, 0, 0x800, 0) } },
            new ParkGate { World = 1, PackEntry = 86, TileX = 19, TileZ = 17, Parts = new[] { new GatePart(260, 0, 280, 0, 0, 0), new GatePart(750, 0, 280, 0, 0x800, 0) } },
            new ParkGate { World = 2, PackEntry = 85, TileX = 18, TileZ = 16, Parts = new[] { new GatePart(1100, 600, 190, 0, 0, 0) } },
            new ParkGate { World = 3, PackEntry = 88, TileX = 18, TileZ = 16, Parts = new[] { new GatePart(800, 570, 300, 0, 0, 0) } },
        };

        public static ParkGate ForWorld(int world) => world >= 0 && world < All.Length ? All[world] : null;

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
