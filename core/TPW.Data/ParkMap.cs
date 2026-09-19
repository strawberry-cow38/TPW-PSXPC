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
    /// ⚠ THERE IS NO HEIGHT FIELD. Every byte is accounted for — type, link bits, facing, a model index, an
    /// appearance variant and flags — and none of them is elevation. Byte +1 has no reader anywhere in the
    /// game. So a port must not expect the map to carry a heightmap; whatever shapes the landscape is not
    /// in here.</summary>
    public readonly struct MapTile
    {
        public readonly byte Raw0, Raw1, Links, Facing;
        public readonly ushort ModelIndex;
        public readonly byte Appearance, Flags;

        public MapTile(byte t, byte r1, byte links, byte facing, ushort model, byte appearance, byte flags)
        { Raw0 = t; Raw1 = r1; Links = links; Facing = facing; ModelIndex = model; Appearance = appearance; Flags = flags; }

        public TileType Type => (TileType)Raw0;

        /// <summary>True for anything a guest walks on. ⚠ PathQueueOverlap counts: the game's own walk-in
        /// and wander code treat 13 as path, so excluding it silently breaks connectivity at crossings.</summary>
        public bool IsWalkable => Type is TileType.Path or TileType.QueuePath or TileType.PathQueueOverlap
                                       or TileType.AttractionEntrance or TileType.ParkGate;
    }

    /// <summary>A park's tile map, as stored in the archive.
    ///
    /// ⭐ LAYOUT: `u32 N; u32 tab[N]; u32 w; u32 h; tile[w*h] (8 bytes each); u32 nbuild; …` — a FOLIO
    /// resource, so the maps are ordinary archive entries rather than separate files.
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
        public const int TileBytes = 8;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public MapTile[] Tiles { get; private set; } = Array.Empty<MapTile>();
        public int TableCount { get; private set; }
        /// <summary>Bytes after the tile grid: the build list. Small on a real map.</summary>
        public int TrailingBytes { get; private set; }

        public MapTile this[int x, int y] => Tiles[y * Width + x];

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
            m.Tiles = new MapTile[w * h];
            int p = at + 8;
            for (int i = 0; i < w * h; i++, p += TileBytes)
                m.Tiles[i] = new MapTile(d[p], d[p + 1], d[p + 2], d[p + 3],
                                         BitConverter.ToUInt16(d, p + 4), d[p + 6], d[p + 7]);

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
