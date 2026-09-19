using System;

namespace TPW.Data
{
    /// <summary>The path a park starts with, and the piece every path tile wears.
    ///
    /// ⭐ ALL OF IT FROM THE GAME'S CODE. Once a map has loaded, 0x800541B8 walks every tile but the last row and
    /// column, row by row. A path tile (type 2) becomes entrance road (12), and the tile after it in z, if it is not
    /// path, becomes the road's end (14). A tile with flag 8 (<see cref="MapTile.EntrancePath"/>) is given path with
    /// the path tool, as a click would (0x8001BD30 twice with kind 2); a road end among them is first turned back to
    /// grass and remembered. The remembered tiles, two on every shipped map where the road meets the park, end as
    /// road ends again wearing the world's path sprite 11, the second a quarter turn round.
    ///
    /// Laying path on a tile: its type becomes 2 (0x8004E034; grass always takes it, 0x8004F200), it is linked to
    /// its neighbours (0x8004E20C), then it picks its piece (0x8004D9DC). Linking sets one bit of byte +2 per joined
    /// neighbour on both tiles, <see cref="North"/> to <see cref="NorthWest"/>. An orthogonal path neighbour is always
    /// joined, and gains the diagonal bits toward this tile's own path neighbours on either side of it; a diagonal
    /// one is joined only when both tiles between are path. Every neighbour it changes picks its piece again.
    ///
    /// A piece comes from the table at 0x800EFFCC (count at 0x801026E0, 49 records of `u32 piece, u32 degrees,
    /// u32 link mask`): the LAST record whose mask equals the links, or failing that the FIRST whose mask the links
    /// contain (the last record, mask 0, always does). The piece's low half picks one of the world's sixteen path
    /// sprites (the world record's +0x90, filled in by 0x8002ED50), its angle / 90 is the quarter turns, and the
    /// tile's ground word becomes `sprite | turns &lt;&lt; 12` (0x8004D250), which the terrain draws like any other ground.
    ///
    /// On the shipped maps this lays the square just inside the gate: 6 x 2 tiles on the jungle's, 4 x 2 on the
    /// others', corners and edges facing out, the two road ends in the middle of its gate side.</summary>
    public static class ParkPaths
    {
        /// <summary>The link bits of tile byte +2, one per neighbour (z grows southwards).</summary>
        public const int North = 0x01, NorthEast = 0x02, East = 0x04, SouthEast = 0x08,
                         South = 0x10, SouthWest = 0x20, West = 0x40, NorthWest = 0x80;

        const uint PieceTable = 0x800EFFCC, PieceCount = 0x801026E0;
        /// <summary>Each world's sixteen path sprites, s16 indices into its ground sheet.</summary>
        static readonly uint[] PathSpriteLists = { 0x800E14D4, 0x800E1534, 0x800E14F4, 0x800E1514 };
        /// <summary>The path sprite the road's ends wear (0x800541B8 reads the list at +0x16).</summary>
        public const int RoadEndSprite = 11;

        /// <summary>A record of the piece table: which list and sprite (the high and low halves of the first word;
        /// list 1 is the path sprites, 2 the queue sprites, 0 the grass), the angle in degrees, the link mask.</summary>
        public readonly struct Piece
        {
            public readonly int List, Sprite, Angle, Mask;
            public Piece(int list, int sprite, int angle, int mask) { List = list; Sprite = sprite; Angle = angle; Mask = mask; }
        }

        public static Piece[] ReadPieces(byte[] exe, uint baseAddress)
        {
            int c = (int)(PieceCount - baseAddress), t = (int)(PieceTable - baseAddress);
            if (exe == null || c < 0 || c + 4 > exe.Length) return null;
            int n = BitConverter.ToInt32(exe, c);
            if (n <= 0 || n > 256 || t < 0 || t + n * 12 > exe.Length) return null;
            var pieces = new Piece[n];
            for (int i = 0; i < n; i++)
            {
                uint piece = BitConverter.ToUInt32(exe, t + i * 12);
                pieces[i] = new Piece((int)(piece >> 16), (int)(piece & 0xFFFF), BitConverter.ToInt32(exe, t + i * 12 + 4), exe[t + i * 12 + 8]);
            }
            return pieces;
        }

        public static int[] ReadPathSprites(byte[] exe, uint baseAddress, int world)
        {
            if (exe == null || world < 0 || world >= PathSpriteLists.Length) return null;
            int o = (int)(PathSpriteLists[world] - baseAddress);
            if (o < 0 || o + 32 > exe.Length) return null;
            var s = new int[16];
            for (int i = 0; i < 16; i++) s[i] = BitConverter.ToUInt16(exe, o + i * 2);
            return s;
        }

        /// <summary>The piece a path tile with these links wears (0x8004D9DC).</summary>
        public static Piece Choose(Piece[] pieces, int links)
        {
            int found = -1;
            for (int i = 0; i < pieces.Length; i++)
                if (pieces[i].Mask == links) found = i;
            if (found < 0)
                for (int i = 0; i < pieces.Length; i++)
                    if ((pieces[i].Mask & links) == pieces[i].Mask) { found = i; break; }
            return found < 0 ? default : pieces[found];
        }

        /// <summary>A copy of the map as the game has it once loaded: the road typed, the starting path laid and every
        /// path tile wearing its piece. The map itself is left alone. Returns it unchanged when the executable's
        /// tables cannot be read.</summary>
        public static ParkMap LayStartingPaths(ParkMap map, byte[] exe, uint baseAddress, int world)
        {
            var pieces = ReadPieces(exe, baseAddress);
            var sprites = ReadPathSprites(exe, baseAddress, world);
            if (map == null || pieces == null || sprites == null) return map;
            var laid = new Layer(map, pieces, sprites);
            laid.PostLoad();
            return laid.Result();
        }

        /// <summary>The tile fields the path code changes, worked on in place.</summary>
        internal sealed class Layer
        {
            readonly ParkMap _map;
            readonly Piece[] _pieces;
            readonly int[] _sprites;
            readonly int _w, _h;
            readonly byte[] _type, _links;
            readonly ushort[] _ground;

            public Layer(ParkMap map, Piece[] pieces, int[] sprites)
            {
                _map = map; _pieces = pieces; _sprites = sprites; _w = map.Width; _h = map.Height;
                _type = new byte[_w * _h]; _links = new byte[_w * _h]; _ground = new ushort[_w * _h];
                for (int i = 0; i < map.Tiles.Length; i++)
                { _type[i] = map.Tiles[i].Raw0; _links[i] = map.Tiles[i].Links; _ground[i] = map.Tiles[i].Ground; }
            }

            public ParkMap Result()
            {
                var m = _map.Clone();
                for (int i = 0; i < m.Tiles.Length; i++)
                {
                    var t = m.Tiles[i];
                    m.Tiles[i] = new MapTile(_type[i], t.Raw1, _links[i], t.Facing, _ground[i], t.Shade, t.Flags);
                }
                return m;
            }

            /// <summary>0x800541B8.</summary>
            public void PostLoad()
            {
                var ends = new System.Collections.Generic.List<int>();
                for (int z = 0; z < _h - 1; z++)
                    for (int x = 0; x < _w - 1; x++)
                    {
                        int t = z * _w + x, below = t + _w;
                        if (_type[t] == 2)
                        {
                            _type[t] = 12;
                            if (_type[below] != 2) _type[below] = 14;
                        }
                        if ((_map.Tiles[t].Flags & 8) == 0) continue;
                        if (_type[t] == 14) { _type[t] = 0; ends.Add(t); }
                        // The path tool takes any grass tile (0x8004F200); nothing else is flagged on a shipped map.
                        if (_type[t] != 0) continue;
                        _type[t] = 2;
                        Link(x, z);
                        Wear(t);
                    }
                for (int i = 0; i < ends.Count; i++)
                {
                    _type[ends[i]] = 14;
                    _ground[ends[i]] = (ushort)(_sprites[RoadEndSprite] | (i != 0 ? 1 : 0) << 12);
                }
            }

            /// <summary>0x8004DC78: the neighbour's index, or -1 off the map (the last row and column never count).</summary>
            int At(int x, int z) => x >= 0 && x < _w - 1 && z >= 0 && z < _h - 1 ? z * _w + x : -1;
            bool IsPath(int t) => t >= 0 && (_type[t] == 2 || _type[t] == 13);

            /// <summary>0x8004D9DC, for path tiles.</summary>
            void Wear(int t)
            {
                if (_type[t] != 2 && _type[t] != 13) return;
                var p = Choose(_pieces, _links[t]);
                if (p.List != 1 || p.Sprite >= _sprites.Length) return;
                _ground[t] = (ushort)(_sprites[p.Sprite] | ((p.Angle / 90) & 3) << 12);
            }

            /// <summary>0x8004E20C for path (kind 2), neighbour by neighbour in the game's order. It also joins the
            /// entrance and exit tiles of attractions facing the tile, which a freshly loaded map does not have.</summary>
            void Link(int x, int z)
            {
                int t = At(x, z);
                // North, then north-east, east, south-east, south, south-west, west and north-west.
                int n = At(x, z - 1);
                if (IsPath(n))
                {
                    _links[t] |= North; _links[n] |= South;
                    if (IsPath(At(x + 1, z))) _links[n] |= SouthEast;
                    if (IsPath(At(x - 1, z))) _links[n] |= SouthWest;
                    Wear(n);
                }
                int ne = At(x + 1, z - 1);
                if (ne >= 0 && _type[ne] == 2 && IsPath(At(x, z - 1)) && IsPath(At(x + 1, z)))
                { _links[t] |= NorthEast; _links[ne] |= SouthWest; Wear(ne); }
                int e = At(x + 1, z);
                if (IsPath(e))
                {
                    _links[t] |= East; _links[e] |= West;
                    if (IsPath(At(x, z - 1))) _links[e] |= NorthWest;
                    if (IsPath(At(x, z + 1))) _links[e] |= SouthWest;
                    Wear(e);
                }
                int se = At(x + 1, z + 1);
                if (IsPath(se) && IsPath(At(x, z + 1)) && IsPath(At(x + 1, z)))
                { _links[t] |= SouthEast; _links[se] |= NorthWest; Wear(se); }
                int s = At(x, z + 1);
                if (IsPath(s))
                {
                    _links[t] |= South; _links[s] |= North;
                    if (IsPath(At(x + 1, z))) _links[s] |= NorthEast;
                    if (IsPath(At(x - 1, z))) _links[s] |= NorthWest;
                    Wear(s);
                }
                int sw = At(x - 1, z + 1);
                if (IsPath(sw) && IsPath(At(x, z + 1)) && IsPath(At(x - 1, z)))
                { _links[t] |= SouthWest; _links[sw] |= NorthEast; Wear(sw); }
                int w = At(x - 1, z);
                if (IsPath(w))
                {
                    _links[t] |= West; _links[w] |= East;
                    if (IsPath(At(x, z - 1))) _links[w] |= NorthEast;
                    if (IsPath(At(x, z + 1))) _links[w] |= SouthEast;
                    Wear(w);
                }
                int nw = At(x - 1, z - 1);
                if (IsPath(nw) && IsPath(At(x, z - 1)) && IsPath(At(x - 1, z)))
                { _links[t] |= NorthWest; _links[nw] |= SouthEast; Wear(nw); }
            }

            /// <summary>The path tool's run (0x8001BD30 → 0x8001B924, then the 0x80 and 0x81 passes): path on each
            /// tile in order until one refuses, which ends the run; then every tile laid is linked; then each picks
            /// its piece.</summary>
            public int LayRun(System.Collections.Generic.IReadOnlyList<(int X, int Z)> run)
            {
                var laid = new System.Collections.Generic.List<(int X, int Z)>();
                foreach (var (x, z) in run)
                {
                    int t = At(x, z);
                    if (t < 0 || !PathTool.CanLay(_type[t], _map.Tiles[t].Flags)) break;
                    if (_type[t] != 2 && _type[t] != 13) _type[t] = 2;   // 0x8004E034: path over path (or path and queue) stays
                    laid.Add((x, z));
                }
                foreach (var (x, z) in laid) Link(x, z);
                foreach (var (x, z) in laid) Wear(z * _w + x);
                return laid.Count;
            }

            /// <summary>Write the changed tiles back into <paramref name="map"/>'s own tile array.</summary>
            public void WriteInto(ParkMap map)
            {
                for (int i = 0; i < map.Tiles.Length; i++)
                {
                    var t = map.Tiles[i];
                    if (t.Raw0 != _type[i] || t.Links != _links[i] || t.Ground != _ground[i])
                        map.Tiles[i] = new MapTile(_type[i], t.Raw1, _links[i], t.Facing, _ground[i], t.Shade, t.Flags);
                }
            }
        }

    }

    /// <summary>The path tool, as the game's (0x8001BD30): a press starts a run, the run follows the cursor along
    /// whichever axis it has moved further on (x only if strictly further, else z), and the second press lays it.
    ///
    /// ⭐ WHERE PATH MAY GO IS NOT WHERE A BUILDING MAY GO. The tool's own check (0x8004F360) refuses a tile with
    /// flag 0x02, takes grass and path (and path-and-queue) and nothing else, and has NO slope test: path follows
    /// the ground. (It also refuses when the bank cannot pay; the port has no bank yet.)</summary>
    public sealed class PathTool
    {
        readonly ParkPaths.Piece[] _pieces;
        readonly int[] _sprites;
        readonly int[] _markers;

        /// <summary>The world's sixteen path sprites, for the atlas.</summary>
        public System.Collections.Generic.IReadOnlyList<int> Sprites => _sprites;

        PathTool(ParkPaths.Piece[] pieces, int[] sprites, int[] markers) { _pieces = pieces; _sprites = sprites; _markers = markers; }

        /// <summary>The ghost's marker sprites in the common sheet (#416), by the validator's result code (the table at
        /// 0x800DBEFC, eight words: 0 takes path, 1 refused, 6 the run ends on path already there, others for queues).</summary>
        const uint MarkerTable = 0x800DBEFC;

        public static PathTool Create(byte[] exe, uint baseAddress, int world)
        {
            var pieces = ParkPaths.ReadPieces(exe, baseAddress);
            var sprites = ParkPaths.ReadPathSprites(exe, baseAddress, world);
            int m = (int)(MarkerTable - baseAddress);
            if (pieces == null || sprites == null || m < 0 || m + 32 > exe.Length) return null;
            var markers = new int[8];
            for (int i = 0; i < 8; i++) markers[i] = BitConverter.ToInt32(exe, m + i * 4);
            return new PathTool(pieces, sprites, markers);
        }

        /// <summary>The ghost the tool draws over a run (0x8001D9D0): per tile, the common-sheet sprite for the
        /// validator's verdict (0x8004F360, for path): refused (flag 0x02, or not grass or path) is 1, and so is every
        /// tile after the first refusal; the LAST tile on existing path is 6; any other grass or path is 0.</summary>
        public System.Collections.Generic.List<(int X, int Z, int Sprite, bool Takes)> Ghost(ParkMap map,
            System.Collections.Generic.IReadOnlyList<(int X, int Z)> run)
        {
            var ghost = new System.Collections.Generic.List<(int, int, int, bool)>();
            bool refused = false;
            for (int i = 0; i < run.Count; i++)
            {
                var (x, z) = run[i];
                int code;
                if (refused || !CanLay(map, x, z)) { code = 1; refused = true; }
                else if (i == run.Count - 1 && map[x, z].Raw0 == 2) code = 6;
                else code = 0;
                ghost.Add((x, z, _markers[code], code != 1));
            }
            return ghost;
        }

        /// <summary>Whether a click on tile (x, z) opens the path tool (the port's control, master's call): any tile on
        /// the map that nothing is built on -- grass, path, the entrance road, buildable or not. Attraction footprints,
        /// entrances and exits, queues and track do not.</summary>
        public static bool CanStartOn(ParkMap map, int x, int z) =>
            x >= 0 && x < map.Width - 1 && z >= 0 && z < map.Height - 1 && map[x, z].Raw0 is not (4 or 5 or 7 or 8 or 10);

        /// <summary>Whether the tool takes tile (x, z): on the map, not flag 0x02, grass or path.</summary>
        public static bool CanLay(ParkMap map, int x, int z) =>
            x >= 0 && x < map.Width - 1 && z >= 0 && z < map.Height - 1 && CanLay(map[x, z].Raw0, map[x, z].Flags);

        internal static bool CanLay(byte type, byte flags) => (flags & 0x02) == 0 && (type == 0 || type == 2 || type == 13);

        /// <summary>The tiles of the run from the start toward the cursor, start first.</summary>
        public static System.Collections.Generic.List<(int X, int Z)> Run(int x0, int z0, int x1, int z1)
        {
            if (Math.Abs(z1 - z0) < Math.Abs(x1 - x0)) z1 = z0; else x1 = x0;
            var run = new System.Collections.Generic.List<(int, int)>();
            int dx = Math.Sign(x1 - x0), dz = Math.Sign(z1 - z0);
            for (int x = x0, z = z0; ; x += dx, z += dz)
            {
                run.Add((x, z));
                if (x == x1 && z == z1) break;
            }
            return run;
        }

        /// <summary>Lay a run on <paramref name="map"/> in place, as the game does it. Returns how many tiles took
        /// path (the run stops at the first that refuses).</summary>
        public int Lay(ParkMap map, System.Collections.Generic.IReadOnlyList<(int X, int Z)> run)
        {
            var layer = new ParkPaths.Layer(map, _pieces, _sprites);
            int n = layer.LayRun(run);
            layer.WriteInto(map);
            return n;
        }
    }
}
