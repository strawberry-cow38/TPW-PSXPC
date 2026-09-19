using System;

namespace TPW.Data
{
    /// <summary>The eight waving flags by the park's bus stops, as 0x800574A4 builds them every frame.
    ///
    /// ⭐ ALL OF IT FROM THE GAME'S CODE. The scenery pass (0x80057AF0) ends with two calls: one with the four
    /// positions at 0x800F2338 and phase t &gt;&gt; 5, one with the four at 0x800F2358 and phase (t · 0x1405) &gt;&gt; 17,
    /// where t is the park's time accumulator (gp+0x778, advanced by the frame time 0x800BDD0C every frame). Each
    /// flag is a 3x3 grid of vertices: the pole column fixed at x = 0 and y = 0, 90, 180; the other two columns
    /// at x = 150 and x = 300, each vertex pushed in y and z by two sine terms of its own phase, the phase
    /// stepping 0x100 per vertex from the tip back, the tip column swinging twice as far as the middle one.
    /// Four textured Gouraud quads (index table 0x800E0CE8) each take a quarter of sprite 354 of the common
    /// sheet #416, and each vertex's grey is 80 + z / 4, so the ripples catch the light. The whole flag is turned
    /// half a turn about the vertical (angles at 0x80102DC4) and moved to its position.
    ///
    /// Time: the frame time is root counter 2's interrupt count × 128 (0x800BC290); the counter is set to
    /// interrupt every 0x866 ticks of the system clock / 8 (0x800BC2EC), i.e. 33.8688 MHz / 8 / 2150 ≈ 1969 Hz.
    /// That makes one set wave about 1.9 times a second and the other about 2.4.</summary>
    public static class EntranceFlags
    {
        /// <summary>Time units per second: root counter 2's interrupt rate × 128.</summary>
        public const double TimeUnitsPerSecond = 33868800.0 / 8 / 2150 * 128;

        public static readonly (int X, int Y, int Z)[] SetA = { (3580, 635, 1560), (4610, 635, 1240), (6150, 635, 1560), (7170, 635, 1240) };
        public static readonly (int X, int Y, int Z)[] SetB = { (3580, 635, 1240), (4610, 635, 1560), (7170, 635, 1560), (6150, 635, 1240) };

        /// <summary>The flag sprite in the common sheet (#416).</summary>
        public const int Sprite = 354;
        public const int CommonSheet = 416;

        /// <summary>The four quads, as vertex indices of the 3x3 grid (0x800E0CE8), in the GPU's corner order.</summary>
        public static readonly int[] Quads = { 0, 1, 3, 4, 1, 2, 4, 5, 3, 4, 6, 7, 4, 5, 7, 8 };

        /// <summary>The phases the two sets are drawn with, from the time accumulator, with the game's 32-bit
        /// arithmetic (the second one's product wraps, which the sine's period makes harmless).</summary>
        public static int PhaseA(long t) => (int)((uint)t >> 5);
        public static int PhaseB(long t) => (int)((uint)((int)t * 0x1405) >> 17);

        /// <summary>PsyQ's rsin / rcos: 4096 is a full turn and 1.0.</summary>
        public static int Sin(int a) => (int)Math.Round(Math.Sin(a * Math.PI / 2048.0) * 4096);
        public static int Cos(int a) => (int)Math.Round(Math.Cos(a * Math.PI / 2048.0) * 4096);

        /// <summary>The nine vertices of a flag at a phase, in its own frame (x along the flag, y up, z the ripple).</summary>
        public static (int X, int Y, int Z)[] Grid(int phase)
        {
            var v = new (int X, int Y, int Z)[9];
            v[0] = (0, 0, 0); v[1] = (0, 90, 0); v[2] = (0, 180, 0);
            // Middle column: the phase is 0x500, 0x400, 0x300 ahead; y by sin >> 9 + sin2 >> 11, z by cos >> 7 + cos2 >> 9.
            for (int r = 0; r < 3; r++)
            {
                int p = phase + 0x500 - 0x100 * r;
                v[3 + r] = (150, (Sin(p) >> 9) + (Sin(2 * p) >> 11) + 90 * r, (Cos(p) >> 7) + (Cos(2 * p) >> 9));
            }
            // The tip: 0x200, 0x100, 0 ahead, each term shifted one less, so twice the swing.
            for (int r = 0; r < 3; r++)
            {
                int p = phase + 0x200 - 0x100 * r;
                v[6 + r] = (300, (Sin(p) >> 8) + (Sin(2 * p) >> 10) + 90 * r, (Cos(p) >> 6) + (Cos(2 * p) >> 8));
            }
            return v;
        }

        /// <summary>A grid vertex's grey (128 neutral): 80 + z / 4, from the vertex's own ripple.</summary>
        public static int Grey(int z) => 80 + (z >> 2);

        /// <summary>Where a flag-frame point lands in the world: half a turn about the vertical, then the position.</summary>
        public static (int X, int Y, int Z) Place((int X, int Y, int Z) at, (int X, int Y, int Z) v) =>
            (at.X - v.X, at.Y + v.Y, at.Z - v.Z);

        /// <summary>The texel box of quad q (0-3) inside the sprite, relative to its corner: column q &gt;&gt; 1, row q &amp; 1,
        /// split at half of (width - 1) and (height - 1) as the game computes it. Corners in the order the game writes
        /// them: (u0, v0) for grid corner 0 of the quad, then (u0, v1), (u1, v0), (u1, v1).</summary>
        public static (int U0, int V0, int U1, int V1) QuadTexels(int q, int w, int h)
        {
            int col = q >> 1, row = q & 1;
            int u0 = (col * (w - 1)) >> 1, u1 = ((col + 1) * (w - 1)) >> 1;
            int v0 = (row * (h - 1)) >> 1, v1 = ((h - 1) << row) >> 1;
            return (u0, v0, u1, v1);
        }
    }
}
