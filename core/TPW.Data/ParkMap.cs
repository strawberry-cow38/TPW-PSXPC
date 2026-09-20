using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>Tile kinds, from every SetType site in the game. Names are inferred from use.</summary>
    public enum TileType : byte
    {
        Grass = 0,
        Path = 2,
        QueuePath = 4,
        BuildingFootprint = 5,
        AttractionEntrance = 7,
        AttractionExit = 8,
        TrackPiece = 10,
        ParkGate = 12,
        /// <summary>A path and a queue crossing: the tool turns 2/4 collisions into this, and both the
        /// walk-in and the wander behaviour treat it as path.</summary>
        PathQueueOverlap = 13,
        GateSide = 14,
    }

    /// <summary>One 8-byte map tile.
    ///
    /// ⭐⭐ BYTE +1 IS THE HEIGHT, AND THIS COMMENT USED TO SAY THE OPPOSITE: "there is no height field; byte +1
    /// has no reader anywhere in the game". It has one. 0x800620B8 returns `tile[+1] << 2`, and 0x800590A0 uses
    /// it as the Y of a tile's world position, (column << 8, tile[+1] << 2, row << 8). So a tile is 256 world
    /// units across and the height is byte +1 × 4, 0..1020. On the maps it is anything but flat: map #203 has
    /// a plateau at 64, a dip at 60 and hills above 94, in smooth regions, which noise would not make. The
    /// "no reader" claim came from a search that did not find this accessor, and was repeated here as fact;
    /// a negative is only as strong as the search behind it.
    ///
    /// tinyclaw measured the terrain as GENERATED (~185 triangles a frame, not a mesh in the archive): generated
    /// FROM this field.</summary>
    public readonly struct MapTile
    {
        public readonly byte Raw0, Raw1, Links, Facing;
        /// <summary>+4: the ground texture word. See <see cref="GroundSprite"/>.</summary>
        public readonly ushort Ground;
        /// <summary>+6: the corner shade. See <see cref="ShadeIndex"/>.</summary>
        public readonly byte Shade;
        public readonly byte Flags;

        public MapTile(byte t, byte r1, byte links, byte facing, ushort ground, byte shade, byte flags)
        { Raw0 = t; Raw1 = r1; Links = links; Facing = facing; Ground = ground; Shade = shade; Flags = flags; }

        // ⭐ +4, +6 AND +7 ARE THE GROUND, FROM THE GAME'S TERRAIN ROUTINE (0x80012110). They were read as "a model
        // index" and "an appearance variant"; the routine that draws the ground says otherwise.

        /// <summary>The sprite this tile's ground wears, in the world's ground sheet: bits 0-11 of +4.</summary>
        public int GroundSprite => Ground & 0x0FFF;
        /// <summary>Quarter turns of the texture on the tile: bits 12-13 of +4.</summary>
        public int GroundTurns => (Ground >> 12) & 3;
        /// <summary>Bit 14 of +4: the texture mirrored top to bottom.</summary>
        public bool GroundFlipV => (Ground & 0x4000) != 0;
        /// <summary>Bit 15 of +4: the texture mirrored left to right.</summary>
        public bool GroundFlipU => (Ground & 0x8000) != 0;
        /// <summary>The shade at this tile's corner: an index into the map's <see cref="ParkMap.ShadeTable"/>,
        /// bits 0-5 of +6. Like the height, it belongs to the corner, so a quad blends four of them.</summary>
        public int ShadeIndex => Shade & 0x3F;
        /// <summary>⭐ BITS 6-7 OF +6 ARE A HEIGHT, NOT PART OF THE SHADE, and they are what stops the camera
        /// sinking. The ground sampler takes a third argument (0x80050938, called with 1 from 0x80050918, which
        /// is the camera's own reader) and when it is set the sampler ADDS this per corner: 0x8004D208 returns
        /// (byte +6 >> 6) &lt;&lt; 10, so 0, 1024, 2048 or 3072 world units — four, eight or twelve tiles. Everything
        /// else reads the ground without it.</summary>
        public int CameraLift => (Shade >> 6) << 10;
        /// <summary>Flags bit 0: the terrain routine draws no ground here at all. On map #203 that is one patch
        /// of 63 grass tiles (x 5-20, z 48-68), drawn by something else.</summary>
        public bool NoGround => (Flags & 1) != 0;
        /// <summary>Flags bit 1: nothing may be built here. The can-build tests (0x8004D718 and its siblings) refuse
        /// a tile with it; on map #203 it marks the border rows and columns, the river banks and the entrance road.</summary>
        public bool Unbuildable => (Flags & 2) != 0;
        /// <summary>Flags bit 3: the entrance path the park starts with. The map loader (0x800541B8) lays path on
        /// every tile that has it (12 on map #203, x 18-23, z 18-19). Bits 4 and 6 are not map data: the game sets
        /// them at run time for occupancy and clears them in 0x800A4C70.</summary>
        public bool EntrancePath => (Flags & 8) != 0;

        public TileType Type => (TileType)Raw0;

        /// <summary>The raw height byte (+1).</summary>
        public byte Height => Raw1;

        /// <summary>Height in the game's world units, as 0x800620B8 computes it: byte +1 × 4. A tile is 256 units across.</summary>
        public int HeightUnits => Raw1 << 2;

        /// <summary>True for anything a guest walks on. ⚠ PathQueueOverlap counts: the game's own walk-in
        /// and wander code treat 13 as path, so excluding it silently breaks connectivity at crossings.</summary>
        public bool IsWalkable => Type is TileType.Path or TileType.QueuePath or TileType.PathQueueOverlap
                                       or TileType.AttractionEntrance or TileType.ParkGate;
    }

    /// <summary>A park's tile map, as stored in the archive.
    ///
    /// ⭐ LAYOUT: `u32 N; u32 shade[N]; u32 w; u32 h; tile[w*h] (8 bytes each); u32 nbuild; …` — a FOLIO
    /// resource, so the maps are ordinary archive entries rather than separate files. The game's map loader
    /// (0x800544E0) keeps a pointer to each part: the shade table at gp+0x12D4, w and h at gp+0x1258/0x125C,
    /// the tiles at gp+0x1254.
    ///
    /// ✅ On the shipped disc this finds **exactly eight**, which matches the eight maps tinyclaw counted
    /// independently while checking something else. All eight are 44x74 and every one decodes to **100%
    /// known tile types** — 3,218 grass and 38 path, summing to exactly w*h. A wrong reading of `w` or `h`
    /// does not produce a grid whose every byte is a documented enum value.
    ///
    /// The eight share a histogram, so they are the same starting layout dressed for different worlds: an
    /// entrance strip with a path leading in.</summary>
    public sealed class ParkMap
    {
        /// <summary>The height the most tiles stand at: the park's own ground level.
        ///
        /// ⭐ WHAT IT IS FOR. The sea is not a separate surface in this game — it is terrain, a tile or more BELOW
        /// the park, wearing a water texture, and the game's camera sinks into it because its own step samples the
        /// ground under the eye wherever the eye is (0x80055104). Master asked for a camera that stays at the
        /// shore's height out over the sea, and this is the floor it holds to. On map #203 it is 256, where 1,763
        /// of 3,256 tiles stand, against 176 at zero for the sea; a map whose park sits at zero gets a floor of
        /// zero and nothing changes.</summary>
        public int ModalHeight
        {
            get
            {
                if (_modalHeight >= 0) return _modalHeight;
                var count = new System.Collections.Generic.Dictionary<int, int>();
                int best = 0, bestN = -1;
                for (int z = 0; z < Height; z++)
                    for (int x = 0; x < Width; x++)
                    {
                        int h = this[x, z].HeightUnits;
                        int n = count[h] = count.TryGetValue(h, out int c) ? c + 1 : 1;
                        if (n > bestN) { bestN = n; best = h; }
                    }
                return _modalHeight = best;
            }
        }
        int _modalHeight = -1;

        public const int TileBytes = 8;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public MapTile[] Tiles { get; private set; } = Array.Empty<MapTile>();
        public int TableCount { get; private set; }
        /// <summary>The map's shade colours, 0x00BBGGRR with 128 as neutral. A tile's <see cref="MapTile.ShadeIndex"/>
        /// picks one per corner and the GPU multiplies the ground texture by it, so this is the terrain's baked light:
        /// map #203 fills 36 of its 64 with greys from 0x44 to 0xF0, and flat ground sits at 0x84-0x93, a touch
        /// brighter than the texture itself.</summary>
        public uint[] ShadeTable { get; private set; } = Array.Empty<uint>();
        /// <summary>Bytes after the tile grid: the build list. Small on a real map.</summary>
        public int TrailingBytes { get; private set; }
        /// <summary>The build list: static scenery models placed on the map, drawn every frame by 0x80057AF0 from
        /// the world's <see cref="SceneryPack"/>. `u32 count; count × 12 bytes: u32 model | flags &lt;&lt; 24, u16 x,
        /// u16 y, u16 z, u16 quarter turns`. The loader (0x800544E0) keeps the count at gp+0x12D8 and the records
        /// at gp+0x12DC, and loads the scenery pack only when the count is not zero.</summary>
        public List<SceneryPlacement> Scenery { get; private set; } = new();
        /// <summary>After the build list: `u32 n; n × (u8 x, u8 z)`, the tiles guests arrive on (0x800540B8 puts a
        /// guest at x·256 + 128, z·256 + 128). Every shipped map has two, beside the entrance road: (18, 5) and
        /// (23, 5) on the jungle's, (17, 5) and (24, 5) on the rest. The loader keeps the count at gp+0x12E4 and the
        /// pairs at gp+0x12E8. ✅ All eight maps end exactly after them: every byte of the format is accounted for.</summary>
        public List<(int X, int Z)> SpawnTiles { get; private set; } = new();

        public MapTile this[int x, int y] => Tiles[y * Width + x];

        /// <summary>A copy with its own tiles, for the changes the game makes to a map once it has loaded it
        /// (<see cref="ParkPaths"/>). The rest is shared.</summary>
        public ParkMap Clone()
        {
            var m = (ParkMap)MemberwiseClone();
            m.Tiles = (MapTile[])Tiles.Clone();
            return m;
        }

        public static bool TryParse(byte[] d, out ParkMap map, out string error)
        {
            map = null; error = null;
            if (d == null || d.Length < 16) { error = "too short for a map resource"; return false; }

            int n = BitConverter.ToInt32(d, 0);
            if (n < 0 || n > 4096) { error = $"implausible table count {n}"; return false; }
            int at = 4 + n * 4;
            if (at + 8 > d.Length) { error = "header runs past the end"; return false; }

            int w = BitConverter.ToInt32(d, at);
            int h = BitConverter.ToInt32(d, at + 4);
            if (w < 4 || w > 512 || h < 4 || h > 512) { error = $"implausible grid {w}x{h}"; return false; }

            long need = (long)at + 8 + (long)w * h * TileBytes;
            if (need > d.Length) { error = $"a {w}x{h} grid needs {need:n0} bytes, entry has {d.Length:n0}"; return false; }

            var m = new ParkMap { Width = w, Height = h, TableCount = n, TrailingBytes = (int)(d.Length - need) };
            m.ShadeTable = new uint[n];
            for (int i = 0; i < n; i++) m.ShadeTable[i] = BitConverter.ToUInt32(d, 4 + i * 4);
            m.Tiles = new MapTile[w * h];
            int p = at + 8;
            for (int i = 0; i < w * h; i++, p += TileBytes)
                m.Tiles[i] = new MapTile(d[p], d[p + 1], d[p + 2], d[p + 3],
                                         BitConverter.ToUInt16(d, p + 4), d[p + 6], d[p + 7]);

            // The build list. A map that ends at the grid simply has none.
            if (p + 4 <= d.Length)
            {
                int count = BitConverter.ToInt32(d, p);
                if (count < 0 || p + 4 + (long)count * 12 > d.Length) { error = $"build list of {count} runs past the end"; return false; }
                int r = p + 4;
                for (int i = 0; i < count; i++, r += 12)
                {
                    uint w0 = BitConverter.ToUInt32(d, r);
                    m.Scenery.Add(new SceneryPlacement((int)(w0 & 0xFFFFFF), (byte)(w0 >> 24), BitConverter.ToUInt16(d, r + 4),
                        BitConverter.ToUInt16(d, r + 6), BitConverter.ToUInt16(d, r + 8), BitConverter.ToUInt16(d, r + 10)));
                }
                if (r + 4 <= d.Length)
                {
                    int spawns = BitConverter.ToInt32(d, r);
                    if (spawns >= 0 && r + 4 + (long)spawns * 2 <= d.Length)
                        for (int i = 0; i < spawns; i++) m.SpawnTiles.Add((d[r + 4 + i * 2], d[r + 5 + i * 2]));
                }
            }

            map = m;
            return true;
        }

        /// <summary>⭐ THE CONTENT CHECK, and the one that separates a real map from a lucky size fit. A
        /// misread grid is not made of documented enum values: on the real maps this returns 1.0 exactly.</summary>
        public double KnownTypeFraction()
        {
            if (Tiles.Length == 0) return 0;
            int known = 0;
            foreach (var t in Tiles) if (Enum.IsDefined(typeof(TileType), t.Raw0)) known++;
            return (double)known / Tiles.Length;
        }

        /// <summary>Every map in the archive, in entry order.</summary>
        public static List<(GazEntry Entry, ParkMap Map)> FindAll(GazArchive gaz)
        {
            var outp = new List<(GazEntry, ParkMap)>();
            if (gaz == null) return outp;
            foreach (var e in gaz.Entries)
            {
                if (e.Size < 1024) continue;
                if (!TryParse(gaz.Read(e), out var m, out _)) continue;
                // ⚠ A size fit alone is not a map. Requiring every tile to be a documented type is what
                // keeps mesh containers and texture pages out, and it costs one pass.
                if (m.KnownTypeFraction() < 1.0) continue;
                if (m.TrailingBytes > 4096) continue;
                outp.Add((e, m));
            }
            return outp;
        }
    }
}
