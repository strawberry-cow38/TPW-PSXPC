using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>Laying a coaster's or a track ride's track: the game's tools 8 and 12 (findings/rides.md §7b).
    ///
    /// ⭐ THE STATION IS PLACED FIRST AND THE TRACK IS A SECOND TOOL. A coaster or a track ride goes down as an
    /// ordinary blueprint — its tool carries the same Cancel / Rotate / Place a flat ride's does — and the place
    /// handler then hands over to a builder (track ride 7 → builder 8 at 0x80021EDC, coaster 11 → builder 12 at
    /// 0x8001F0D0) which carries Undo and, for builder 8, Undo All. So this is the queue tool's shape: a run that
    /// grows a segment per press, takes the last one back, and knows when it is done.
    ///
    /// ⭐ THIRTY-TWO PIECES. 0x800A7CC8 keeps the count in the ride's own +0x104 and refuses to add the
    /// thirty-third, writing each piece into the array of 4-byte entries at +0x106.
    ///
    /// ⚠ WHERE THE TRACK MEETS THE STATION IS THE PORT'S CHOICE. The record's entrance and exit are the GUESTS'
    /// doors — queue in, walk out — and nothing in it has been identified as the rails' own ends, so this runs the
    /// track out of the middle of the station's front edge and closes it against the middle of the back edge.
    ///
    /// ⚠ AND SO IS CHARGING PER PIECE. The price is the theme's own (<see cref="PiecePrice"/>), but the game's
    /// confirm works out `unit × (pieces − 4)` in one go (0x800229E4, economy.md §4.6) while its per-press handler
    /// charges through the same helper as everything else (0x800225C0 → 0x8001C2E0). Which of the two is the real
    /// bill is not settled, so the port charges the unit per piece and says so.</summary>
    public sealed class TrackRun
    {
        /// <summary>The thirty-third piece is refused (0x800A7CC8).</summary>
        public const int MaxPieces = 32;

        /// <summary>What a piece costs, by world: the theme's first type-8 record (entries 256, 165, 79 and 399,
        /// whose +0x20 holds 900, 900, 750 and 750). defPrice(8, kind) ignores the kind and returns that one
        /// record (0x8006A54C), so a theme has a single track price.</summary>
        public static readonly int[] PiecePrice = { 900, 900, 750, 750 };

        readonly List<(int X, int Z)> _pieces = new();
        readonly List<int> _corners = new();      // how many pieces each press laid, for Undo

        public TrackRun(AttractionDefinition ride, int ox, int oz, int rot, int world)
        {
            Ride = ride; World = Math.Clamp(world, 0, PiecePrice.Length - 1);
            var (w, d) = ride.Footprint(rot);
            // Out of the middle of one edge and back to the middle of the opposite one. Which edge is "front"
            // follows the blueprint's turn, so a turned station runs its track out of the side it faces.
            switch (rot & 3)
            {
                case 0: Start = (ox + w / 2, oz + d); Close = (ox + w / 2, oz - 1); break;
                case 1: Start = (ox - 1, oz + d / 2); Close = (ox + w, oz + d / 2); break;
                case 2: Start = (ox + w / 2, oz - 1); Close = (ox + w / 2, oz + d); break;
                default: Start = (ox + w, oz + d / 2); Close = (ox - 1, oz + d / 2); break;
            }
        }

        public AttractionDefinition Ride { get; }
        public int World { get; }
        public (int X, int Z) Start { get; }
        /// <summary>The tile the track has to come back to for the circuit to close.</summary>
        public (int X, int Z) Close { get; }
        public IReadOnlyList<(int X, int Z)> Pieces => _pieces;
        /// <summary>The track runs from the station and back to it: only then does a coaster open to guests
        /// (astra's reading of the class's own "may guests queue" override).</summary>
        public bool Circuit { get; private set; }
        public (int X, int Z) End => _pieces.Count > 0 ? _pieces[^1] : Start;
        public int Cost => _pieces.Count * PiecePrice[World];

        public enum Step { Refused, Laid, Closed }

        /// <summary>The far end of the ghost for a cursor at (cx, cz): along whichever axis it has moved further,
        /// the way the queue tool snaps (0x8001DF08).</summary>
        public (int X, int Z) Snap(int cx, int cz)
        {
            var (x0, z0) = End;
            return Math.Abs(cz - z0) < Math.Abs(cx - x0) ? (cx, z0) : (x0, cz);
        }

        /// <summary>Whether a tile can take track: on the map, nothing built on it, nothing forbidding building,
        /// and not already track. The tile that closes the circuit is the exception — it is the station's own.</summary>
        public bool Takes(ParkMap map, int x, int z)
        {
            if ((x, z) == Close) return true;
            if (x < 0 || z < 0 || x >= map.Width - 1 || z >= map.Height - 1) return false;
            var t = map[x, z];
            return t.Raw0 == (byte)TileType.Grass && (t.Flags & 0x02) == 0;
        }

        /// <summary>The ghost from the run's end toward the cursor: each tile with the marker sprite for its
        /// verdict, and whether a press would lay the lot.</summary>
        public List<(int X, int Z, int Sprite, bool Takes)> Ghost(PathTool tool, ParkMap map, int cx, int cz, out bool valid)
        {
            var (ex, ez) = Snap(cx, cz);
            var run = PathTool.Run(End.X, End.Z, ex, ez);
            var ghost = new List<(int, int, int, bool)>();
            valid = run.Count > 1;
            for (int i = 1; i < run.Count; i++)
            {
                var (x, z) = run[i];
                bool ok = valid && Takes(map, x, z) && _pieces.Count + i <= MaxPieces;
                if (!ok) valid = false;
                ghost.Add((x, z, tool.Marker(ok ? 0 : 1), ok));
            }
            return ghost;
        }

        /// <summary>A press with the cursor at (cx, cz): the whole segment goes down or none of it does, as the
        /// queue tool's does. The run ends the moment it reaches the closing tile.</summary>
        public Step Lay(ParkMap map, PathTool tool, int cx, int cz)
        {
            if (Circuit) return Step.Refused;
            var ghost = Ghost(tool, map, cx, cz, out bool valid);
            if (!valid || ghost.Count == 0) return Step.Refused;
            int laid = 0;
            foreach (var (x, z, _, _) in ghost)
            {
                if ((x, z) == Close) { Circuit = true; break; }
                map.Tiles[z * map.Width + x] = With(map[x, z], (byte)TileType.TrackPiece);
                _pieces.Add((x, z));
                laid++;
            }
            _corners.Add(laid);
            return Circuit ? Step.Closed : Step.Laid;
        }

        /// <summary>Take the last press back (the builders' Undo).</summary>
        public bool Undo(ParkMap map)
        {
            if (_corners.Count == 0) return false;
            int n = _corners[^1];
            _corners.RemoveAt(_corners.Count - 1);
            for (int i = 0; i < n && _pieces.Count > 0; i++)
            {
                var (x, z) = _pieces[^1];
                _pieces.RemoveAt(_pieces.Count - 1);
                map.Tiles[z * map.Width + x] = With(map[x, z], (byte)TileType.Grass);
            }
            Circuit = false;
            return true;
        }

        /// <summary>Take the whole track back (builder 8's Undo All).</summary>
        public void UndoAll(ParkMap map) { while (Undo(map)) { } }

        static MapTile With(MapTile t, byte type) => new(type, t.Raw1, t.Links, t.Facing, t.Ground, t.Shade, t.Flags);
    }
}
