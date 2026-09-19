namespace TPW.Data
{
    /// <summary>Where a ride or shop may stand: the game's own test, tile by tile.
    ///
    /// ⭐ FROM THE GAME'S CODE. The placement check (0x80064558) walks every tile of the attraction's footprint
    /// and turns the whole placement red if any tile fails one of three things:
    /// <list type="number">
    /// <item>it and its east and south neighbours are on the map (0x800508C8: 0 ≤ x &lt; w − 1, 0 ≤ z &lt; h − 1);</item>
    /// <item>0x8004D718 accepts it: not path (2), queue (4), footprint (5), attraction entrance (7) or exit (8),
    /// track (10), entrance road (12) or path and queue (13), and none of flags 0x01 (no ground: scenery stands
    /// there), 0x02 (the map says nothing may be built), 0x08 (the path the park starts with) or 0x10 (taken, set at
    /// run time);</item>
    /// <item>the ground is flat enough: each of its other three corners (the height bytes of the tiles east, south
    /// and south-east, × 4) is within 64 units of its own corner (0x800664C8 reads the height).</item>
    /// </list>
    /// A fourth condition, what the attraction costs against the bank balance, fails every tile at once and is not
    /// about the tile. The other tile tests (0x8004D800 for the path tools, 0x8004D8B8, 0x8004D690) are looser:
    /// this is the one that says where things can be built.</summary>
    public static class ParkBuild
    {
        /// <summary>Why a tile can or cannot take a building, most telling reason first.</summary>
        public enum Verdict
        {
            Buildable,
            /// <summary>Everything but the slope allows it.</summary>
            TooSteep,
            /// <summary>Flag 0x02: the map rules it out (borders, river banks, the entrance road's sides).</summary>
            Unbuildable,
            /// <summary>Flag 0x01: no ground is drawn; a scenery model (cliff, river) stands there.</summary>
            NoGround,
            /// <summary>Entrance road (12) or the path the park starts with (flag 0x08).</summary>
            Entrance,
            /// <summary>Path, queue, a footprint, an attraction's entrance or exit, or track.</summary>
            Occupied,
            /// <summary>The last row or column, or a tile whose east or south neighbour is: the game never builds there.</summary>
            Edge,
        }

        /// <summary>The largest difference in height, in world units, between a tile's own corner and any of its
        /// other three that the placement check accepts.</summary>
        public const int MaxCornerRise = 0x40;

        /// <summary>0x8004D718: the tile on its own, type and flags.</summary>
        public static bool TileAllows(MapTile t)
        {
            switch (t.Raw0)
            {
                case 2: case 4: case 5: case 7: case 8: case 10: case 12: case 13: return false;
            }
            return (t.Flags & (0x01 | 0x02 | 0x08 | 0x10)) == 0;
        }

        static bool InBounds(ParkMap map, int x, int z) => x >= 0 && x < map.Width - 1 && z >= 0 && z < map.Height - 1;

        /// <summary>The slope rule: the tile's other three corners within <see cref="MaxCornerRise"/> of its own.</summary>
        public static bool FlatEnough(ParkMap map, int x, int z)
        {
            int h = map[x, z].HeightUnits;
            int e = map[x + 1, z].HeightUnits, s = map[x, z + 1].HeightUnits, se = map[x + 1, z + 1].HeightUnits;
            return System.Math.Abs(e - h) <= MaxCornerRise && System.Math.Abs(s - h) <= MaxCornerRise
                && System.Math.Abs(se - h) <= MaxCornerRise;
        }

        /// <summary>Whether a footprint tile at (x, z) passes, as 0x80064558 tests it.</summary>
        public static bool CanBuild(ParkMap map, int x, int z) =>
            InBounds(map, x, z) && InBounds(map, x + 1, z) && InBounds(map, x, z + 1)
            && TileAllows(map[x, z]) && FlatEnough(map, x, z);

        /// <summary>The verdict for one tile, with the reason when it fails. Use it on the map as the game has it
        /// once loaded (<see cref="ParkPaths.LayStartingPaths"/>), when the road is typed and the starting path laid.</summary>
        public static Verdict Classify(ParkMap map, int x, int z)
        {
            if (!InBounds(map, x, z) || !InBounds(map, x + 1, z) || !InBounds(map, x, z + 1)) return Verdict.Edge;
            var t = map[x, z];
            if (t.Raw0 == 12 || (t.Flags & 0x08) != 0) return Verdict.Entrance;
            switch (t.Raw0)
            {
                case 2: case 4: case 5: case 7: case 8: case 10: case 13: return Verdict.Occupied;
            }
            if ((t.Flags & 0x02) != 0) return Verdict.Unbuildable;
            if ((t.Flags & 0x01) != 0) return Verdict.NoGround;
            if ((t.Flags & 0x10) != 0) return Verdict.Occupied;
            return FlatEnough(map, x, z) ? Verdict.Buildable : Verdict.TooSteep;
        }
    }
}
