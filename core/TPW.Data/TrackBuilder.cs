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

        /// <summary>The ghost: where the pylon would land once the cursor is projected onto the dominant axis,
        /// and the run of track that would reach it.</summary>
        public List<(int X, int Z, int Sprite, bool Takes)> Ghost(PathTool tool, ParkMap map, int cx, int cz, out bool valid)
        {
            var ghost = new List<(int, int, int, bool)>();
            var at = Project(End, (cx, cz));
            valid = !Circuit && _pylons.Count < MaxPylons && PieceCount(End, (cx, cz)) >= 1 && Takes(map, at.X, at.Z);
            foreach (var (x, z) in Span(End, at))
                ghost.Add((x, z, tool.Marker(valid ? 0 : 1), valid));
            ghost.Add((at.X, at.Z, tool.Marker(valid ? 6 : 1), valid));
            return ghost;
        }

        /// <summary>⭐ WHERE THE NEXT PYLON ACTUALLY LANDS. The builder does not join two points with a corner:
        /// its update (0x800221B0) takes |dx| and |dz| off the last point, picks the DOMINANT AXIS — X when
        /// |dz| &lt; |dx|, Z otherwise, so a tie goes to Z — and walks that axis alone in steps of <b>two tiles</b>
        /// (`addiu s5, zero, 2`), laying `abs(delta) &gt;&gt; 1` pieces. So the cursor's off-axis half is thrown away
        /// and an odd distance is rounded down: the pylon lands on a two-tile lattice along one axis, which is
        /// why the game's track comes out in straight runs and never in an L.</summary>
        public static (int X, int Z) Project((int X, int Z) from, (int X, int Z) to)
        {
            int dx = to.X - from.X, dz = to.Z - from.Z;
            if (Math.Abs(dz) < Math.Abs(dx)) return (from.X + (Math.Abs(dx) >> 1) * 2 * Math.Sign(dx), from.Z);
            return (from.X, from.Z + (Math.Abs(dz) >> 1) * 2 * Math.Sign(dz));
        }

        /// <summary>How many pieces that run is: `abs(delta) &gt;&gt; 1`, and the piece family it lays is the
        /// two-tile one (kinds 40..51, size 4x3x4 half-tiles).</summary>
        public static int PieceCount((int X, int Z) from, (int X, int Z) to)
        {
            int dx = to.X - from.X, dz = to.Z - from.Z;
            return Math.Max(Math.Abs(dx), Math.Abs(dz)) >> 1;
        }

        /// <summary>The tiles the track covers between two pylons, one at a time. The game lays TWO-TILE pieces
        /// (see <see cref="Project"/>); this walks every tile because the port draws the ride's own one-tile
        /// piece on each of them, which is the same ground covered.</summary>
        public static IEnumerable<(int X, int Z)> Span((int X, int Z) from, (int X, int Z) to)
        {
            int x = from.X, z = from.Z;
            while (x != to.X) { x += Math.Sign(to.X - x); if ((x, z) != to) yield return (x, z); }
            while (z != to.Z) { z += Math.Sign(to.Z - z); if ((x, z) != to) yield return (x, z); }
        }

        /// <summary>A press at (cx, cz): the run is projected onto the dominant axis, one pylon goes down at
        /// its end, and the track is what runs between.</summary>
        public Step Lay(ParkMap map, int cx, int cz)
        {
            var at = Project(End, (cx, cz));
            if (Circuit || _pylons.Count >= MaxPylons || PieceCount(End, (cx, cz)) < 1 || !Takes(map, at.X, at.Z))
                return Step.Refused;
            var from = End;
            _pylons.Add(at);
            Mark(map, at.X, at.Z, (byte)TileType.TrackPiece);
            foreach (var (x, z) in Span(from, at)) Mark(map, x, z, (byte)TileType.TrackPiece);
            if (at == Start) { Circuit = true; return Step.Closed; }
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

        /// <summary>Which of a ride's sub-models are its track.
        ///
        /// ⭐ THE GAME'S PIECE TABLE IS READ (rides.md): 80 descriptors of 8 bytes at 0x800F8A30 = 20 piece
        /// types x 4 directions, each carrying a class (+1), the direction (+5) and a model 0..3 (+6). A piece's
        /// kind indexes it directly below 40; kinds 40..51 are the 2-tile family and pick their base through two
        /// gp globals.
        ///
        /// ⚠ +6 TURNED OUT TO BE THE PIECE'S TURN, NOT ITS MODEL (it lands at piece +0x6C, and its reader
        /// takes the record's width on 0/2 and its depth on 1/3). The MESH comes from the class at +1:
        /// `owner[0xDC + class * 4]`, with classes 11 and 99 drawing nothing. What fills that table is still
        /// unread, so until it is, the port picks pieces by MEASURING the sub-models: a car is small and long
        /// (under two thirds of a tile wide) and always in a matching set at the end of the list, the station is
        /// the biggest thing there, and the track is the one-tile piece that is flat and not merely a box.</summary>
        public readonly struct Pieces
        {
            public readonly int Straight, Support, Column;
            public Pieces(int straight, int support, int column) { Straight = straight; Support = support; Column = column; }
            public bool Any => Straight >= 0;
        }

        /// <summary>Pick the track pieces out of a ride's sub-models, given each one's extent in world units
        /// and how many vertices it has.
        ///
        /// The cars are thrown out by being NARROW (Chac Atak's sleds are 0.58 of a tile across, Dino Karts'
        /// 0.34, while no track piece is under three quarters), anything over two and a half tiles is a station
        /// or a set piece, and of what is left the track is the FLATTEST piece that is more than a box: a trough
        /// has ten vertices and twelve faces where a plain support block has eight and a decorative cap has
        /// five. The support is simply the tallest of them, and the column — what a pylon is stacked out of — is the
        /// plain box nearest a one-tile cube.</summary>
        public static Pieces PickPieces(IReadOnlyList<(int W, int H, int D, int Verts)> subs)
        {
            int straight = -1, support = -1, column = -1;
            double straightH = double.MaxValue, supportH = -1, columnSquare = double.MaxValue;
            for (int i = 1; i < subs.Count; i++)      // sub 0 is the station: the attraction itself draws that
            {
                var (w, h, d, verts) = subs[i];
                double tw = w / 256.0, th = h / 256.0, td = d / 256.0;
                if (Math.Min(tw, td) < 0.7 || Math.Max(tw, td) > 2.6) continue;
                if (th > supportH) { supportH = th; support = i; }
                if (verts > 8 && th < straightH) { straightH = th; straight = i; }
                // The column is the plain BOX nearest a one-tile cube: what a pylon is stacked out of.
                double square = Math.Abs(tw - 1) + Math.Abs(td - 1) + Math.Abs(th - 1);
                if (verts <= 8 && square < columnSquare) { columnSquare = square; column = i; }
            }
            return new Pieces(straight, support, column);
        }

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
