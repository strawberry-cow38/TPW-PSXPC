using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>An attraction's definition record, from its archive entry (a 0x96 container whose header +0x14
    /// points at it; the same entry carries the model). Layout in findings/rides.md §1.3.
    ///
    /// ⭐ FROM THE GAME'S CODE: footprint width +0x08 (0x8006A798) and depth +0x0A (0x8006A7A4); the entrance tile
    /// offset +0x0C and the exit tile offset +0x10 (s16 x, z inside the footprint; -1 for none: 0x80066004 /
    /// 0x80065FD8), the way each faces +0x14 / +0x15 (0x80065FF8 / 0x80065FCC); the record body's length +0x1C, and
    /// right after the body the ground pad (0x8006A7C8): per footprint tile, row by row, s16 sprite in the world's
    /// ground sheet (negative: no ground drawn there) and u16 flags (turns in bits 0-1, mirror V bit 2, mirror U
    /// bit 3: 0x80066024 / 0x80066030 / 0x80066044).</summary>
    public sealed class AttractionDefinition
    {
        public int Entry, Type, NameId, Width, Depth, EntranceFacing, ExitFacing, Price;
        public (int X, int Z)? Entrance, Exit;
        public (short Sprite, ushort Flags)[] Pad = Array.Empty<(short, ushort)>();

        /// <summary>Types (findings/rides.md §1.1): 1 coaster, 2 feature, 3 flat ride, 4 shop, 5 sideshow, 6 track
        /// ride, 7 tour ride.</summary>
        public bool IsRide => Type is 1 or 3 or 6 or 7;

        public static AttractionDefinition Read(int entry, byte[] d)
        {
            if (!MeshContainer.TryParse(d, out var c, out _)) return null;
            int r = (int)c.RecordOffset;
            if (r <= 0 || r + 0x24 > d.Length) return null;
            int type = BitConverter.ToInt32(d, r);
            if (type < 1 || type > 8) return null;
            var a = new AttractionDefinition
            {
                Entry = entry, Type = type, NameId = BitConverter.ToInt32(d, r + 4),
                Width = d[r + 8], Depth = d[r + 0x0A],
                EntranceFacing = d[r + 0x14] & 3, ExitFacing = d[r + 0x15] & 3,
            };
            short ex = BitConverter.ToInt16(d, r + 0x0C), ez = BitConverter.ToInt16(d, r + 0x0E);
            short xx = BitConverter.ToInt16(d, r + 0x10), xz = BitConverter.ToInt16(d, r + 0x12);
            if (ex >= 0 && ez >= 0) a.Entrance = (ex, ez);
            if (xx >= 0 && xz >= 0) a.Exit = (xx, xz);
            // Build price: level 0's block for the rides (+0x24 + 0x2C), +0x20 for the rest (rides.md §1.3).
            int priceAt = a.IsRide ? r + 0x24 + 0x2C : r + 0x20;
            if (priceAt + 4 <= d.Length) a.Price = BitConverter.ToInt32(d, priceAt);
            int body = BitConverter.ToInt32(d, r + 0x1C);
            int pad = r + 0x20 + body, n = a.Width * a.Depth;
            if (body >= 0 && n > 0 && pad + n * 4 <= d.Length)
            {
                a.Pad = new (short, ushort)[n];
                for (int i = 0; i < n; i++) a.Pad[i] = (BitConverter.ToInt16(d, pad + i * 4), BitConverter.ToUInt16(d, pad + i * 4 + 2));
            }
            return a;
        }

        /// <summary>The footprint on the map at a quarter-turn rotation: width and depth swap for 1 and 3 (slots 12 / 14).</summary>
        public (int W, int D) Footprint(int rot) => (rot & 1) == 0 ? (Width, Depth) : (Depth, Width);

        /// <summary>A tile offset inside the footprint, turned (0x800634B8).</summary>
        public (int X, int Z) Rotate(int x, int z, int rot) => (rot & 3) switch
        {
            1 => (z, Width - 1 - x),
            2 => (Width - 1 - x, Depth - 1 - z),
            3 => (Depth - 1 - z, x),
            _ => (x, z),
        };

        /// <summary>The tile just outside the entrance (0x800635E8) and the exit (0x800636F0): the door's own tile, then
        /// one step out of the footprint the way it faces (facing + rotation: 0 north, z - 1; 1 west, x - 1; 2 south,
        /// z + 1; 3 east, x + 1). The placement ghost's door markers stand here, placing lays the queue piece or path
        /// here, and a ride's queue starts here.</summary>
        public (int X, int Z)? EntranceTile(int ox, int oz, int rot) => Door(Entrance, EntranceFacing, ox, oz, rot);
        public (int X, int Z)? ExitTile(int ox, int oz, int rot) => Door(Exit, ExitFacing, ox, oz, rot);

        /// <summary>The entrance's and exit's own tiles (0x80062E48 / 0x80062FF8): the record's offset turned, which is
        /// always on the footprint's edge (every flat ride, shop and sideshow record READ: the offsets are inside
        /// the footprint). Placing makes them 7 and 8.</summary>
        public (int X, int Z)? EntranceDoor(int ox, int oz, int rot) => Inside(Entrance, ox, oz, rot);
        public (int X, int Z)? ExitDoor(int ox, int oz, int rot) => Inside(Exit, ox, oz, rot);

        (int X, int Z)? Inside((int X, int Z)? offset, int ox, int oz, int rot)
        {
            if (offset is not { } o) return null;
            var (x, z) = Rotate(o.X, o.Z, rot);
            return (x + ox, z + oz);
        }

        /// <summary>Which way the entrance or exit faces on the map (0 north … 3 east), for its marker's turn.</summary>
        public int EntranceTurn(int rot) => (EntranceFacing + rot) & 3;
        public int ExitTurn(int rot) => (ExitFacing + rot) & 3;

        (int X, int Z)? Door((int X, int Z)? offset, int facing, int ox, int oz, int rot)
        {
            if (offset is not { } o) return null;
            var (x, z) = Rotate(o.X, o.Z, rot);
            x += ox; z += oz;
            switch ((facing + rot) & 3)
            {
                case 0: z -= 1; break;
                case 1: x -= 1; break;
                case 2: z += 1; break;
                default: x += 1; break;
            }
            return (x, z);
        }
    }

    /// <summary>Each world's attractions for the port's picker: flat rides (both sets), shops and sideshows, as
    /// archive entries. From the theme tables 0x8002ED50 builds (findings/rides_themes.json); coasters, track and
    /// tour rides come from scenario data the port has not read yet.</summary>
    public static class AttractionCatalog
    {
        static readonly int[][] Rides =
        {
            new[] { 220, 217, 216, 218, 227, 224, 222, 211, 210, 209, 221, 225, 226, 214 },
            new[] { 133, 129, 128, 131, 132, 140, 134, 122, 139, 137, 124, 121, 138, 136 },
            new[] { 40, 53, 39, 54, 49, 51, 52, 42, 55, 43, 45, 48, 50 },
            new[] { 377, 362, 369, 379, 371, 365, 376, 360, 364, 374, 375, 366, 370 },
        };
        static readonly int[][] Shops =
        {
            new[] { 237, 234, 236, 239, 235, 238, 232, 247 },
            new[] { 149, 146, 152, 151, 160, 150, 158, 147 },
            new[] { 65, 71, 62, 68, 60, 69, 64, 67 },
            new[] { 385, 381, 383, 387, 382, 386, 384, 390 },
        };
        static readonly int[][] Sideshows =
        {
            new[] { 248, 229, 244, 243, 245, 246 },
            new[] { 144, 159, 153, 154, 145, 155 },
            new[] { 66, 72, 70, 74, 58, 73 },
            new[] { 388, 393, 391, 380, 389, 394 },
        };

        public static IEnumerable<int> ForWorld(int world)
        {
            if (world < 0 || world >= Rides.Length) yield break;
            foreach (var e in Rides[world]) yield return e;
            foreach (var e in Shops[world]) yield return e;
            foreach (var e in Sideshows[world]) yield return e;
        }
    }

    /// <summary>Putting an attraction on the map, as the game's placement check (0x80064558) and placement
    /// (0x80063B98) do it.</summary>
    public static class AttractionPlacement
    {
        /// <summary>The ghost's markers, common-sheet sprites (0x80064558): 0xA5 a footprint tile that is fine, 0xAA
        /// one on the attraction's front edge (turned to face out), 0xAF one that refuses; the entrance 0xA8 for
        /// rides and 0xAC for the rest, the exit 0xA9, both 0xAF when refused.</summary>
        public const int Body = 0xA5, Front = 0xAA, Refused = 0xAF, RideEntrance = 0xA8, OtherEntrance = 0xAC, ExitMarker = 0xA9;

        public readonly struct Marker
        {
            public readonly int X, Z, Sprite, Turns;
            public Marker(int x, int z, int sprite, int turns) { X = x; Z = z; Sprite = sprite; Turns = turns; }
        }

        static bool InMap(ParkMap m, int x, int z) => x >= 0 && x < m.Width - 1 && z >= 0 && z < m.Height - 1;

        /// <summary>Whether the tile outside a door may take the attraction's own path piece (every exit, and the
        /// entrance of anything that is not a ride). The game tests it as it tests the ride's queue piece and the
        /// footprint (0x80064558 → 0x8004D718), so path already there refuses the whole placement.
        ///
        /// ⚠ THE PORT'S DEPARTURE, master's call (the PC version does it): the path piece may land on path already
        /// there (path, or path and queue), and placing then joins that path to the door (<see cref="PathTool.LayDoors"/>
        /// lays path over path as the path tool does, linking it) instead of the blueprint going red. The no-build
        /// flags (0x01 no ground, 0x02 nothing may be built) still refuse; a ride's queue piece keeps the game's rule.</summary>
        static bool PathPieceAllows(MapTile t) =>
            ParkBuild.TileAllows(t) || (t.Raw0 is 2 or 13 && (t.Flags & (0x01 | 0x02)) == 0);

        /// <summary>The markers for an attraction with its footprint's corner at (ox, oz), and whether it may be placed.</summary>
        public static List<Marker> Ghost(ParkMap map, AttractionDefinition a, int ox, int oz, int rot, out bool placeable)
        {
            var list = new List<Marker>();
            placeable = true;
            var (w, d) = a.Footprint(rot);
            for (int z = 0; z < d; z++)
                for (int x = 0; x < w; x++)
                {
                    int tx = ox + x, tz = oz + z;
                    if (!InMap(map, tx, tz) || !InMap(map, tx + 1, tz) || !InMap(map, tx, tz + 1)) { placeable = false; continue; }
                    if (!ParkBuild.CanBuild(map, tx, tz)) { placeable = false; list.Add(new Marker(tx, tz, Refused, 0)); continue; }
                    bool front = (rot & 3) switch { 1 => x == 0, 2 => z == d - 1, 3 => x == w - 1, _ => z == 0 };
                    list.Add(new Marker(tx, tz, front ? Front : Body, front ? rot & 3 : 0));
                }
            if (a.EntranceTile(ox, oz, rot) is { } e && InMap(map, e.X, e.Z))
            {
                bool ok = a.IsRide ? ParkBuild.TileAllows(map[e.X, e.Z]) : PathPieceAllows(map[e.X, e.Z]);
                placeable &= ok;
                list.Add(new Marker(e.X, e.Z, ok ? (a.IsRide ? RideEntrance : OtherEntrance) : Refused, a.EntranceTurn(rot)));
            }
            if (a.ExitTile(ox, oz, rot) is { } x2 && InMap(map, x2.X, x2.Z))
            {
                bool ok = PathPieceAllows(map[x2.X, x2.Z]);
                placeable &= ok;
                list.Add(new Marker(x2.X, x2.Z, ok ? ExitMarker : Refused, a.ExitTurn(rot)));
            }
            return list;
        }

        /// <summary>Place it (0x80063B98), in place on the map: each footprint tile becomes a footprint (type 5) and
        /// wears the record's ground pad, turned with it (the pad's own turns plus the rotation, where the game swaps
        /// 1 and 3); a pad tile with no sprite draws no ground (flags = 1). Then the entrance's own tile, on the
        /// footprint's edge, becomes 7 and the exit's 8, each facing out. What goes on the tiles outside them (a
        /// queue piece or path) is <see cref="PathTool.LayDoors"/>.</summary>
        public static void Place(ParkMap map, AttractionDefinition a, int ox, int oz, int rot)
        {
            rot &= 3;
            int padTurn = rot is 1 or 3 ? (rot + 2) & 3 : rot;
            for (int j = 0; j < a.Depth; j++)
                for (int i = 0; i < a.Width; i++)
                {
                    var (rx, rz) = a.Rotate(i, j, rot);
                    int tx = ox + rx, tz = oz + rz;
                    if (!InMap(map, tx, tz)) continue;
                    var t = map[tx, tz];
                    byte flags = t.Flags;
                    ushort ground = t.Ground;
                    int k = j * a.Width + i;
                    if (k < a.Pad.Length)
                    {
                        var (sprite, pf) = a.Pad[k];
                        if (sprite < 0) flags = 1;
                        else ground = (ushort)((ushort)sprite | (((pf & 3) + padTurn) & 3) << 12 | ((pf >> 3) & 1) << 15 | ((pf >> 2) & 1) << 14);
                    }
                    map.Tiles[tz * map.Width + tx] = new MapTile(5, t.Raw1, t.Links, t.Facing, ground, t.Shade, flags);
                }
            void Door((int X, int Z)? tile, byte type, int turn)
            {
                if (tile is not { } p || !InMap(map, p.X, p.Z)) return;
                var t = map[p.X, p.Z];
                // Facing, in the link bits' encoding (0x01 north, 0x04 east, 0x10 south, 0x40 west): the way out, away
                // from the attraction, toward the path a guest arrives on. The path code joins a path tile to an
                // entrance only when the entrance's facing bit points at it (0x8004E20C).
                byte facing = turn switch { 0 => 0x01, 1 => 0x40, 2 => 0x10, _ => 0x04 };
                map.Tiles[p.Z * map.Width + p.X] = new MapTile(type, t.Raw1, t.Links, facing, t.Ground, t.Shade, t.Flags);
            }
            Door(a.EntranceDoor(ox, oz, rot), 7, a.EntranceTurn(rot));
            Door(a.ExitDoor(ox, oz, rot), 8, a.ExitTurn(rot));
        }
    }
}
