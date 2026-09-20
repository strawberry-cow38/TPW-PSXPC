using System;

namespace TPW.Sim
{
    /// <summary>The park gate's two crossing points — where a guest walks to get in, and to get out.
    ///
    /// ⭐ THEY ARE A BINARY CONSTANT, NOT MAP DATA. Table 0x800E0D28 holds (5376, 1620) and (5376, 2640) in
    /// world units, read by 0x800599A4. Every map on the disc puts its gate in the same place, which is the
    /// only reason a fixed pair of numbers can work at all.
    ///
    /// ⭐ x = 5376 IS EXACTLY 21.0 TILES — A TILE BOUNDARY, NOT A CENTRE. Everything else in this game sits
    /// at a tile CENTRE (`tile * 256 + 128`), so a corridor drawn on tile centres lands half a tile off from
    /// where the game actually sends people. That is what a port gets wrong here.
    ///
    /// ⭐ AND THERE ARE TWO OF THEM, ABOUT FOUR TILES APART: point 0 at z = 1620 (6.33 tiles) and point 1 at
    /// z = 2640 (10.31 tiles) — the gate's two turnstile lanes, the same two the lane counters count.
    /// **Arrivals walk to point 1, leavers to point 0** (state 47 asks for `1 - V+0x28`), which is what stops
    /// the queue and the leavers walking through each other.</summary>
    /// <remarks>
    /// ⭐ **THEY SIT ON THE GATE'S OWN CENTRE LINE, WHICH IS HOW YOU KNOW THEY BELONG TO IT.** The gate's
    /// rectangle (0x800F2398, five bytes a world) puts world 0's arch at tile x 18, z 16, six wide — so its
    /// centre x is 18 + 6/2 = **21.0**, exactly <see cref="Table"/>'s 5376. That looked like a contradiction
    /// at first (the arch is at z 16 and these are at z 6.33 and 10.31) and it is not: the z values are a
    /// SEQUENCE along the approach, not a second opinion about where the gate is.
    ///
    /// Along z: the spawn/exit tiles at **5**, point 0 at **6.33**, the bus stop at **7.42**, point 1 at
    /// **10.31**, the arch at **16..19**. Which matches the direction rule exactly — leavers take point 0,
    /// nearest the bus on their way out, and arrivals take point 1, further in toward the arch.
    ///
    /// ⚠ NOT THE SAME THING AS THE ENTRANCE BUILDING. The turnstile needs `EntranceTile(lane)`, the
    /// building-table entry flagged 0x04000000, and that table is NOT PARSED ANYWHERE in this port.
    /// </remarks>
    public static class ParkPoints
    {
        /// <summary>Table 0x800E0D28, in world units (256 to the tile).</summary>
        public static readonly (int X, int Z)[] Table = { (5376, 1620), (5376, 2640) };

        /// <summary>Which point a guest heads for: an ARRIVING guest (side 0) crosses to point 1, a LEAVER
        /// (side 1) to point 0. State 47 computes it as `1 - side` (0x800914D0).</summary>
        public static int ForSide(int side) => 1 - (side & 1);

        /// <summary>The spread in x: `rand(426) - 213`, so guests fan out across ±0.83 of a tile
        /// (0x80059A1C..0x80059A68).</summary>
        public const int SpreadX = 426, SpreadXBias = 213;

        /// <summary>Where a guest actually aims, rolled as the game rolls it: x first, then z.
        ///
        /// ⚠⚠ THE Z ROLL IS DISPUTED AND I HAVE NOT SETTLED IT. Two independent reads of 0x80059A4C..8C
        /// disagree: one says `rand(2*index + 1) - 15`, the other `rand(31) - 15`. They differ everywhere
        /// except point 0, where `rand(1)` is always 0 and both give exactly z - 15 = 1605 — which a third
        /// read measured live. So point 0 is safe and point 1 is not: the first reading gives it a 3-unit
        /// spread, the second a 31-unit one. <see cref="DisputedZRoll"/> is the one implemented, and it is
        /// the reading two of the three sources support. ⚠ Do not build a tolerance on point 1's spread
        /// until someone re-reads that instruction.
        ///
        /// ⚠ THE ORDER OF THE ROLLS MATTERS to anything reproducing the original's random stream: x is drawn
        /// before z, and both are drawn even when the result is not used.</summary>
        public static (int X, int Z) Aim(int pointIndex, IRandomSource rng)
        {
            int i = pointIndex & 1;
            var (x, z) = Table[i];
            x += (rng?.Next(SpreadX) ?? SpreadXBias) - SpreadXBias;
            int jitter = (rng?.Next(DisputedZRoll(i)) ?? 15) - 15;
            return (x, i == 0 ? z + jitter : z - jitter);
        }

        /// <summary>`rand(2 * index + 1)`: 1 for point 0 (always zero, so a flat −15) and 3 for point 1.
        /// ⚠ The disputed half — see <see cref="Aim"/>.</summary>
        public static int DisputedZRoll(int pointIndex) => 2 * (pointIndex & 1) + 1;

        /// <summary>The point in TILES, for anything drawing it rather than walking it. Note the .0 on x:
        /// it really is on the boundary.</summary>
        public static (double X, double Z) InTiles(int pointIndex)
        {
            var (x, z) = Table[pointIndex & 1];
            return (x / 256.0, z / 256.0);
        }
    }
}
