using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>Building a coaster or a track ride: the game's tools 8 and 12 (findings/rides.md §7b).
    ///
    /// ⭐ YOU PLACE PYLONS, AND THE TRACK ASSEMBLES BETWEEN THEM. Master said so and the code agrees: what a press
    /// stores is a POINT, not a run of tiles. 0x800A7CC8 takes a piece, reads two s16s out of it, and writes them
    /// as a pair of u16s into the ride's own array at +0x106 (4 bytes each, the count in the byte at +0x104), and
    /// it refuses the thirty-third. Nothing about a press touches the map.
    ///
    /// ⭐ AND THE CIRCUIT CLOSES BY LANDING BACK ON THE START. The same routine compares the new point against the
    /// ride's own start and sets its +0x18A when they match — the flag a coaster's "may guests queue" override
    /// tests, so an unfinished track is not merely unattractive, it is unqueueable.
    ///
    /// ⭐ WHERE THE RAILS LEAVE THE STATION IS THE GAME'S, by the blueprint's turn (0x800A6550, off the ride's own
    /// tile at +0x60/+0x64): <see cref="StartFor"/>. This replaces the port's earlier guess at a "front edge".
    ///
    /// ⚠ WHAT A PYLON COSTS is the theme's one track price (<see cref="PiecePrice"/>), charged per pylon here. The
    /// game's confirm works out `unit × (pieces − 4)` in one go (0x800229E4) while its per-press handler charges
    /// through the usual helper (0x800225C0 → 0x8001C2E0); which is the real bill is not settled.</summary>
    public sealed class TrackRun
    {
        /// <summary>The thirty-third pylon is refused (0x800A7CC8).</summary>
        public const int MaxPylons = 32;

        /// <summary>What a pylon costs, by world: the theme's first type-8 record (entries 256, 165, 79 and 399,
        /// whose +0x20 holds 900, 900, 750 and 750). defPrice(8, kind) returns that one record whatever the kind
        /// (0x8006A54C), so a theme has a single track price.</summary>
        public static readonly int[] PiecePrice = { 900, 900, 750, 750 };

        readonly List<(int X, int Z)> _pylons = new();

        public TrackRun(AttractionDefinition ride, int ox, int oz, int rot, int world)
        {
            Ride = ride;
            World = Math.Clamp(world, 0, PiecePrice.Length - 1);
            Start = StartFor(ox, oz, rot);
        }

        /// <summary>Where the rails leave a station whose tile is (ox, oz), turned (0x800A6550).</summary>
        public static (int X, int Z) StartFor(int ox, int oz, int rot) => (rot & 3) switch
        {
            0 => (ox - 2, oz + 1),
            1 => (ox + 1, oz + 4),
            2 => (ox + 4, oz + 1),
            _ => (ox + 1, oz - 2),
        };

        public AttractionDefinition Ride { get; }
        public int World { get; }
        /// <summary>The rails' own end at the station: where the track starts and what closes it.</summary>
        public (int X, int Z) Start { get; }
        public IReadOnlyList<(int X, int Z)> Pylons => _pylons;
        /// <summary>Set once a pylon lands back on <see cref="Start"/> (the ride's +0x18A).</summary>
        public bool Circuit { get; private set; }
        public (int X, int Z) End => _pylons.Count > 0 ? _pylons[^1] : Start;
        public int Cost => _pylons.Count * PiecePrice[World];

        public enum Step { Refused, Placed, Closed }

        /// <summary>Whether a pylon may stand on tile (x, z): on the map, on open ground, and nothing forbidding
        /// building there. The start is always allowed — landing on it is how the circuit closes.</summary>
        public bool Takes(ParkMap map, int x, int z)
        {
            if ((x, z) == Start) return _pylons.Count > 0;
            if (x < 0 || z < 0 || x >= map.Width - 1 || z >= map.Height - 1) return false;
            foreach (var p in _pylons) if (p == (x, z)) return false;
            var t = map[x, z];
            return t.Raw0 == (byte)TileType.Grass && (t.Flags & 0x02) == 0;
        }

        /// <summary>The ghost: the pylon that would go down, and the span of track it would assemble from the last
        /// one. The span is drawn tile by tile so the player can see where the rails will run.</summary>
        public List<(int X, int Z, int Sprite, bool Takes)> Ghost(PathTool tool, ParkMap map, int cx, int cz, out bool valid)
        {
            var ghost = new List<(int, int, int, bool)>();
            valid = !Circuit && _pylons.Count < MaxPylons && Takes(map, cx, cz);
            foreach (var (x, z) in Span(End, (cx, cz)))
                ghost.Add((x, z, tool.Marker(valid ? 0 : 1), valid));
            ghost.Add((cx, cz, tool.Marker(valid ? 6 : 1), valid));
            return ghost;
        }

        /// <summary>The tiles the track runs over between two pylons: straight along whichever axis is longer,
        /// then along the other, the way the rest of the game's runs are laid out. The pylons themselves are not
        /// in it.</summary>
        public static IEnumerable<(int X, int Z)> Span((int X, int Z) from, (int X, int Z) to)
        {
            int x = from.X, z = from.Z;
            while (x != to.X) { x += Math.Sign(to.X - x); if ((x, z) != to) yield return (x, z); }
            while (z != to.Z) { z += Math.Sign(to.Z - z); if ((x, z) != to) yield return (x, z); }
        }

        /// <summary>A press at (cx, cz): one pylon goes down, and the track is what runs between them.</summary>
        public Step Lay(ParkMap map, int cx, int cz)
        {
            if (Circuit || _pylons.Count >= MaxPylons || !Takes(map, cx, cz)) return Step.Refused;
            _pylons.Add((cx, cz));
            Mark(map, cx, cz, (byte)TileType.TrackPiece);
            foreach (var (x, z) in Span(_pylons.Count > 1 ? _pylons[^2] : Start, (cx, cz)))
                Mark(map, x, z, (byte)TileType.TrackPiece);
            if ((cx, cz) == Start) { Circuit = true; return Step.Closed; }
            return Step.Placed;
        }

        /// <summary>Take the last pylon back, and the track that ran to it (the builders' Undo).</summary>
        public bool Undo(ParkMap map)
        {
            if (_pylons.Count == 0) return false;
            var gone = _pylons[^1];
            _pylons.RemoveAt(_pylons.Count - 1);
            foreach (var (x, z) in Span(_pylons.Count > 0 ? _pylons[^1] : Start, gone)) Mark(map, x, z, (byte)TileType.Grass);
            if (gone != Start) Mark(map, gone.X, gone.Z, (byte)TileType.Grass);
            Circuit = false;
            return true;
        }

        /// <summary>Take the whole track back (builder 8's Undo All).</summary>
        public void UndoAll(ParkMap map) { while (Undo(map)) { } }

        static void Mark(ParkMap map, int x, int z, byte type)
        {
            if (x < 0 || z < 0 || x >= map.Width || z >= map.Height) return;
            var t = map[x, z];
            if (type == (byte)TileType.TrackPiece && t.Raw0 != (byte)TileType.Grass) return;
            if (type == (byte)TileType.Grass && t.Raw0 != (byte)TileType.TrackPiece) return;
            map.Tiles[z * map.Width + x] = new MapTile(type, t.Raw1, t.Links, t.Facing, t.Ground, t.Shade, t.Flags);
        }
    }
}
