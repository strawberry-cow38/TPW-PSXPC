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
        /// <summary>The queue's pieces (0x8004D9DC for type 4): 11 records, straight, end, corner and lone, all list 2.</summary>
        const uint QueuePieceTable = 0x800EFF48, QueuePieceCount = 0x801026DC;
        /// <summary>Each world's sixteen path sprites, s16 indices into its ground sheet (the world record's +0x90).</summary>
        static readonly uint[] PathSpriteLists = { 0x800E14D4, 0x800E1534, 0x800E14F4, 0x800E1514 };
        /// <summary>Each world's four queue sprites (+0x98: lone, end, straight, corner) and two grass sprites (+0x88),
        /// the lists 0x8002ED50 puts in the world records beside the path sprites.</summary>
        static readonly uint[] QueueSpriteLists = { 0x80102DF4, 0x80102E3C, 0x80102E0C, 0x80102E24 };
        static readonly uint[] GrassSpriteLists = { 0x80102DE8, 0x80102E30, 0x80102E00, 0x80102E18 };
        /// <summary>The path sprite the road's ends wear (0x800541B8 reads the list at +0x16).</summary>
        public const int RoadEndSprite = 11;

        /// <summary>A record of the piece table: which list and sprite (the high and low halves of the first word;
        /// list 1 is the path sprites, 2 the queue sprites, 0 the grass), the angle in degrees, the link mask.</summary>
        public readonly struct Piece
        {
            public readonly int List, Sprite, Angle, Mask;
            public Piece(int list, int sprite, int angle, int mask) { List = list; Sprite = sprite; Angle = angle; Mask = mask; }
        }

        public static Piece[] ReadPieces(byte[] exe, uint baseAddress) => ReadPieces(exe, baseAddress, PieceTable, PieceCount);
        public static Piece[] ReadQueuePieces(byte[] exe, uint baseAddress) => ReadPieces(exe, baseAddress, QueuePieceTable, QueuePieceCount);

        static Piece[] ReadPieces(byte[] exe, uint baseAddress, uint table, uint count)
        {
            int c = (int)(count - baseAddress), t = (int)(table - baseAddress);
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

        public static int[] ReadPathSprites(byte[] exe, uint baseAddress, int world) => ReadSprites(exe, baseAddress, PathSpriteLists, world, 16);
        public static int[] ReadQueueSprites(byte[] exe, uint baseAddress, int world) => ReadSprites(exe, baseAddress, QueueSpriteLists, world, 4);
        public static int[] ReadGrassSprites(byte[] exe, uint baseAddress, int world) => ReadSprites(exe, baseAddress, GrassSpriteLists, world, 2);

        static int[] ReadSprites(byte[] exe, uint baseAddress, uint[] lists, int world, int n)
        {
            if (exe == null || world < 0 || world >= lists.Length) return null;
            int o = (int)(lists[world] - baseAddress);
            if (o < 0 || o + n * 2 > exe.Length) return null;
            var s = new int[n];
            for (int i = 0; i < n; i++) s[i] = BitConverter.ToUInt16(exe, o + i * 2);
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

        /// <summary>What the queue's linking needs to know about the run it belongs to (0x8004E20C's queue branch): the
        /// ride's entrance tile, the way the last tile laid went (0x80102D0C / 0x80102D10, 0x80 while no run is going)
        /// and whether a tile lies on the run so far (0x8001B76C).</summary>
        internal sealed class QueueLink
        {
            public int DoorX = -1, DoorZ = -1;
            public int PrevDx = NoWay, PrevDz = NoWay;
            public Func<int, int, bool> OnRun = (_, _) => false;
            public const int NoWay = 0x80;
        }

        /// <summary>The tile fields the path code changes, worked on in place.</summary>
        internal sealed class Layer
        {
            readonly ParkMap _map;
            readonly Piece[] _pieces, _queuePieces;
            readonly int[] _sprites, _queueSprites, _grassSprites;
            readonly Random _rng;
            readonly int _w, _h;
            readonly byte[] _type, _links, _facing;
            readonly ushort[] _ground;

            public Layer(ParkMap map, Piece[] pieces, int[] sprites, Piece[] queuePieces = null, int[] queueSprites = null,
                         int[] grassSprites = null, Random rng = null)
            {
                _map = map; _pieces = pieces; _sprites = sprites; _w = map.Width; _h = map.Height;
                _queuePieces = queuePieces; _queueSprites = queueSprites; _grassSprites = grassSprites; _rng = rng;
                _type = new byte[_w * _h]; _links = new byte[_w * _h]; _facing = new byte[_w * _h]; _ground = new ushort[_w * _h];
                for (int i = 0; i < map.Tiles.Length; i++)
                {
                    _type[i] = map.Tiles[i].Raw0; _links[i] = map.Tiles[i].Links;
                    _facing[i] = map.Tiles[i].Facing; _ground[i] = map.Tiles[i].Ground;
                }
            }

            public ParkMap Result()
            {
                var m = _map.Clone();
                for (int i = 0; i < m.Tiles.Length; i++)
                {
                    var t = m.Tiles[i];
                    m.Tiles[i] = new MapTile(_type[i], t.Raw1, _links[i], _facing[i], _ground[i], t.Shade, t.Flags);
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
            static int Popcount(byte b) { int n = 0; for (; b != 0; b &= (byte)(b - 1)) n++; return n; }
            static int Opposite(int bit) => bit < 0x10 ? bit << 4 : bit >> 4;

            /// <summary>0x8004D9DC: a path, path-and-queue or queue tile picks its piece by its links (the last record
            /// whose mask equals them, else the first whose mask they contain), from the path table or, for a queue,
            /// the queue table; the piece's list says which of the world's lists its sprite comes from (0 grass,
            /// 1 path, 2 queue). A grass tile gets one of the world's two grass sprites, turned, both at random: what
            /// the queue's undo leaves behind.</summary>
            void Wear(int t)
            {
                Piece p;
                switch (_type[t])
                {
                    case 2: case 13: p = Choose(_pieces, _links[t]); break;
                    case 4:
                        if (_queuePieces == null) return;
                        p = Choose(_queuePieces, _links[t]);
                        break;
                    case 0:
                        if (_grassSprites == null || _rng == null) return;
                        int angle = _rng.Next(360), pick = _rng.Next(2);
                        _ground[t] = (ushort)(_grassSprites[pick] | (angle / 90 & 3) << 12);
                        return;
                    default: return;
                }
                var list = p.List switch { 0 => _grassSprites, 1 => _sprites, 2 => _queueSprites, _ => null };
                if (list == null || p.Sprite >= list.Length) return;
                _ground[t] = (ushort)(list[p.Sprite] | ((p.Angle / 90) & 3) << 12);
            }

            /// <summary>0x8004E20C for path (kind 2), neighbour by neighbour in the game's order. After each orthogonal
            /// neighbour's path test comes its door test (<see cref="JoinDoor"/>).</summary>
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
                JoinDoor(t, n, North, South);
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
                JoinDoor(t, e, East, West);
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
                JoinDoor(t, s, South, North);
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
                JoinDoor(t, w, West, East);
                int nw = At(x - 1, z - 1);
                if (IsPath(nw) && IsPath(At(x, z - 1)) && IsPath(At(x - 1, z)))
                { _links[t] |= NorthWest; _links[nw] |= SouthEast; Wear(nw); }
            }

            /// <summary>The door half of 0x8004E20C's path branch for the orthogonal neighbour n (bit: the way from
            /// this tile to n, opp: back): an exit (8) or an entrance (7) whose facing points at this tile is joined
            /// both ways; a queue (4) whose own link already points here only picks its piece again.</summary>
            void JoinDoor(int t, int n, int bit, int opp)
            {
                if (n < 0) return;
                if (_type[n] == 8 && (_facing[n] & opp) != 0) { _links[t] |= (byte)bit; _links[n] |= (byte)opp; Wear(n); }
                if (_type[n] == 7)
                {
                    if ((_facing[n] & opp) != 0) { _links[t] |= (byte)bit; _links[n] |= (byte)opp; Wear(n); }
                }
                else if (_type[n] == 4 && (_links[n] & opp) != 0) Wear(n);
            }

            /// <summary>0x8004E20C's queue branch (kind 4). A queue tile joins only the tile behind it on the run, and
            /// that only while the tile behind is a queue with fewer than two links that lies on the run
            /// (0x8001B76C): so a queue is one line, and never joins a part of itself it passes beside. The first
            /// tile of a segment also joins the ride's entrance when that is beside it and not joined yet, and looks
            /// behind along the way the previous segment went. A path tile the queue ends on (13) drops its
            /// diagonal links on that side, so its path piece opens toward the queue.</summary>
            void LinkQueue(int x, int z, int dx, int dz, bool first, QueueLink q)
            {
                int t = At(x, z);
                if (t < 0) return;
                int door = q.DoorX >= 0 ? At(q.DoorX, q.DoorZ) : -1;
                if (door >= 0 && _links[door] != 0) door = -1;
                int bx = dx, bz = dz;
                if (first)
                {
                    int dn = At(x, z - 1), de = At(x + 1, z), ds = At(x, z + 1), dw = At(x - 1, z);
                    if (dn >= 0 && dn == door) { _links[t] |= North; _links[dn] |= South; }
                    if (de >= 0 && de == door) { _links[t] |= East; _links[de] |= West; }
                    if (ds >= 0 && ds == door) { _links[t] |= South; _links[ds] |= North; }
                    if (dw >= 0 && dw == door) { _links[t] |= West; _links[dw] |= East; }
                    if (q.PrevDx == QueueLink.NoWay || q.PrevDz == QueueLink.NoWay) { bx = 0; bz = 0; }
                    else { bx = q.PrevDx; bz = q.PrevDz; }
                }
                bool pathy = _type[t] is 2 or 13;
                if (bx == -1) Behind(t, x + 1, z, East, West, pathy, NorthEast | SouthEast, q);
                if (bx == 1) Behind(t, x - 1, z, West, East, pathy, NorthWest | SouthWest, q);
                if (bz == 1) Behind(t, x, z - 1, North, South, pathy, NorthEast | NorthWest, q);
                if (bz == -1) Behind(t, x, z + 1, South, North, pathy, SouthEast | SouthWest, q);
                q.PrevDx = dx; q.PrevDz = dz;
            }

            void Behind(int t, int nx, int nz, int bit, int opp, bool pathy, int diagonals, QueueLink q)
            {
                int n = At(nx, nz);
                if (n < 0 || _type[n] != 4) return;
                if (Popcount(_links[n]) < 2 && q.OnRun(nx, nz)) { _links[t] |= (byte)bit; _links[n] |= (byte)opp; }
                if (pathy) _links[t] &= (byte)~diagonals;
            }

            /// <summary>0x8004F200: whether a tile takes the kind (2 path, 4 queue, 13 path and queue, 5 footprint,
            /// 0 grass). Grass and the kind itself always do; flag 0x10 takes path and queue; path takes path and
            /// queue, a queue an entrance; path takes the END of a queue (one link).</summary>
            bool Accepts(int t, int kind)
            {
                byte type = _type[t];
                if (kind == 5 && type == 5) return false;
                if (type == kind || type == 0) return true;
                if ((_map.Tiles[t].Flags & 0x10) != 0 && kind is 2 or 4 or 13) return true;
                if (kind == 5 && type is 2 or 13) return true;
                if (kind == 0) return true;
                if (kind == 2 && type == 13) return true;
                if (kind == 4 && type == 7) return true;
                if (kind is 2 or 13 && type == 4) return Popcount(_links[t]) == 1;
                return false;
            }

            /// <summary>0x8004E034: lay the kind on a tile. A tile that refuses stays as it was, except that a queue
            /// refused by path makes it path and queue (13). Queue over path, path over queue and anything over
            /// path and queue make 13.</summary>
            bool LayTile(int t, int kind)
            {
                byte type = _type[t];
                if (!Accepts(t, kind))
                {
                    if (kind == 4 && type == 2) _type[t] = 13;
                    return false;
                }
                if (kind != 0 && type == kind) return true;
                int k = kind;
                if (k == 4 && type == 2) k = 13;
                if (k == 2 && type == 4) k = 13;
                if (type == 13) k = 13;
                _type[t] = (byte)k;
                return true;
            }

            static byte FacingBit(int dx, int dz) =>
                (byte)((dx == -1 ? East : dx == 1 ? West : 0) | (dz == -1 ? South : dz == 1 ? North : 0));

            /// <summary>0x8001B924's walk: along x when the segment runs further in x than in z, else along z; each tile
            /// with the step it was reached by (a lone tile has none).</summary>
            static System.Collections.Generic.List<(int X, int Z, int Dx, int Dz)> Steps(int x0, int z0, int x1, int z1)
            {
                var list = new System.Collections.Generic.List<(int, int, int, int)>();
                if (Math.Abs(z1 - z0) < Math.Abs(x1 - x0))
                {
                    int s = x1 < x0 ? -1 : 1;
                    for (int x = x0; x != x1; x += s) list.Add((x, z0, s, 0));
                    list.Add((x1, z0, s, 0));
                }
                else
                {
                    int s = z1 < z0 ? -1 : 1;
                    for (int z = z0; z != z1; z += s) list.Add((x0, z, 0, s));
                    list.Add((x0, z1, 0, z0 == z1 ? 0 : s));
                }
                return list;
            }

            /// <summary>One segment of a run as 0x8001BD30 lays it: the tiles from (x0, z0) to (x1, z1) are laid with the
            /// kind in order until one refuses (0x8004E034); then every tile of the segment is linked with the kind
            /// last laid (pass 0x80), gains the way back along the run in its facing (0x82: 0x8004FA68) and picks its
            /// piece (0x81). Returns whether the run is finished: its last tile was path, queue, path and queue or
            /// track before it was laid (0x8004DE04 sets 0x801026D0, which ends the run in pass 0x81).</summary>
            public bool LaySegment(int x0, int z0, int x1, int z1, int kind, QueueLink q)
            {
                var tiles = Steps(x0, z0, x1, z1);
                int laidKind = 0;
                bool ended = false;
                for (int i = 0; i < tiles.Count; i++)
                {
                    int t = At(tiles[i].X, tiles[i].Z);
                    if (t < 0) break;
                    ended = i == tiles.Count - 1 && _type[t] is 4 or 2 or 13 or 10;
                    if (!LayTile(t, kind)) break;
                    laidKind = kind;
                }
                for (int i = 0; i < tiles.Count; i++)
                {
                    var (x, z, dx, dz) = tiles[i];
                    if (At(x, z) < 0) continue;
                    if (laidKind == 2) Link(x, z);
                    else if (laidKind == 4 && q != null) LinkQueue(x, z, dx, dz, i == 0, q);
                }
                foreach (var (x, z, dx, dz) in tiles)
                {
                    int t = At(x, z);
                    if (t >= 0) _facing[t] |= FacingBit(dx, dz);
                }
                foreach (var (x, z, _, _) in tiles)
                {
                    int t = At(x, z);
                    if (t >= 0) Wear(t);
                }
                return ended;
            }

            /// <summary>Drop every link on this tile that points at a tile nothing can link to any more, and
            /// re-pick its sprite if anything went. Returns true when it changed.
            ///
            /// ⚠ A LINK IS HALF OF A PAIR AND THE OTHER HALF OUTLIVES THE TILE. Taking a tile off the map
            /// leaves its neighbours still carrying the bit that pointed AT it, and the sprite is chosen from
            /// that mask (0x8004D9DC) — so the neighbour keeps drawing the stub of a junction to a tile that
            /// is now grass. Nothing notices, because the mask is still a perfectly valid mask.</summary>
            public bool DropDeadLinks(int x, int z)
            {
                int t = At(x, z);
                if (t < 0) return false;
                byte before = _links[t];
                bool linkable = _type[t] == 2 || _type[t] == 4 || _type[t] == 13;
                if (!linkable) _links[t] = 0;
                else
                    foreach (var (bit, dx, dz) in new[] { (North, 0, -1), (East, 1, 0), (South, 0, 1), (West, -1, 0) })
                    {
                        if ((_links[t] & bit) == 0) continue;
                        int n = At(x + dx, z + dz);
                        if (n < 0 || !(_type[n] == 2 || _type[n] == 4 || _type[n] == 13)) _links[t] &= (byte)~bit;
                    }
                if (_links[t] == before) return false;
                Wear(t);
                return true;
            }

            /// <summary>Whether this tile carries a link to something unlinkable — the defect DropDeadLinks
            /// fixes, counted rather than fixed, so a test can assert there are none left.</summary>
            public bool HasDeadLink(int x, int z)
            {
                int t = At(x, z);
                if (t < 0) return false;
                if (!(_type[t] == 2 || _type[t] == 4 || _type[t] == 13)) return _links[t] != 0;
                foreach (var (bit, dx, dz) in new[] { (North, 0, -1), (East, 1, 0), (South, 0, 1), (West, -1, 0) })
                {
                    if ((_links[t] & bit) == 0) continue;
                    int n = At(x + dx, z + dz);
                    if (n < 0 || !(_type[n] == 2 || _type[n] == 4 || _type[n] == 13)) return true;
                }
                return false;
            }

            /// <summary>0x8004FBEC for a queue tile, the queue's undo: each of its four links is dropped, from the
            /// neighbour too, which picks its piece again; then the tile is grass again (0x8004E034 with kind 0),
            /// wearing a random grass sprite.</summary>
            public void RemoveQueueTile(int x, int z)
            {
                int t = At(x, z);
                if (t < 0 || _type[t] != 4) return;
                foreach (var (bit, dx, dz) in new[] { (North, 0, -1), (East, 1, 0), (South, 0, 1), (West, -1, 0) })
                {
                    if ((_links[t] & bit) == 0) continue;
                    _links[t] &= (byte)~bit;
                    int n = At(x + dx, z + dz);
                    if (n < 0) continue;
                    _links[n] &= (byte)~Opposite(bit);
                    Wear(n);
                }
                _links[t] = 0;
                LayTile(t, 0);
                Wear(t);
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
                    if (t.Raw0 != _type[i] || t.Links != _links[i] || t.Ground != _ground[i] || t.Facing != _facing[i])
                        map.Tiles[i] = new MapTile(_type[i], t.Raw1, _links[i], _facing[i], _ground[i], t.Shade, t.Flags);
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
        readonly ParkPaths.Piece[] _pieces, _queuePieces;
        readonly int[] _sprites, _queueSprites, _grassSprites;
        readonly int[] _markers;
        /// <summary>For the grass the queue's undo leaves (0x8004D9DC draws its sprite and turn with rand()).</summary>
        readonly Random _rng = new(1);

        /// <summary>The world's sixteen path sprites, for the atlas.</summary>
        public System.Collections.Generic.IReadOnlyList<int> Sprites => _sprites;

        /// <summary>Every ground sprite the tools can put down: path, queue, and the grass the queue's undo leaves.</summary>
        public System.Collections.Generic.IEnumerable<int> AllSprites
        {
            get
            {
                foreach (int s in _sprites) yield return s;
                if (_queueSprites != null) foreach (int s in _queueSprites) yield return s;
                if (_grassSprites != null) foreach (int s in _grassSprites) yield return s;
            }
        }

        PathTool(ParkPaths.Piece[] pieces, int[] sprites, int[] markers, ParkPaths.Piece[] queuePieces, int[] queueSprites, int[] grassSprites)
        {
            _pieces = pieces; _sprites = sprites; _markers = markers;
            _queuePieces = queuePieces; _queueSprites = queueSprites; _grassSprites = grassSprites;
        }

        internal ParkPaths.Layer NewLayer(ParkMap map) => new(map, _pieces, _sprites, _queuePieces, _queueSprites, _grassSprites, _rng);

        /// <summary>The marker sprite for a validator result code (0x800DBEFC).</summary>
        internal int Marker(int code) => _markers[code];

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
            return new PathTool(pieces, sprites, markers, ParkPaths.ReadQueuePieces(exe, baseAddress),
                                ParkPaths.ReadQueueSprites(exe, baseAddress, world), ParkPaths.ReadGrassSprites(exe, baseAddress, world));
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

        /// <summary>What laying <paramref name="run"/> would cost, in pounds, the way the validator adds it up
        /// (0x8004F360 → the accumulator at 0x80102724): a tile the ghost refuses costs nothing and ends the run,
        /// and a tile that is ALREADY the type being laid costs nothing either — 0x8004F494 compares the tile's own
        /// type against the one being laid and skips the add when they match, so running path back over path is
        /// free.</summary>
        public int RunCost(ParkMap map, System.Collections.Generic.IReadOnlyList<(int X, int Z)> run)
        {
            int cost = 0;
            foreach (var (x, z, _, takes) in Ghost(map, run))
            {
                if (!takes) break;
                if (map[x, z].Raw0 != 2) cost += PathTileCost;
            }
            return cost;
        }

        /// <summary>The head of <paramref name="run"/> the bank can pay for, and what it costs: the game checks the
        /// running total against the balance tile by tile and refuses the one the money does not reach, so a run
        /// stops there rather than failing whole.</summary>
        public System.Collections.Generic.List<(int X, int Z)> Afford(ParkMap map,
            System.Collections.Generic.IReadOnlyList<(int X, int Z)> run, long pounds, out int cost)
        {
            var kept = new System.Collections.Generic.List<(int X, int Z)>();
            cost = 0;
            foreach (var (x, z, _, takes) in Ghost(map, run))
            {
                int tile = takes && map[x, z].Raw0 != 2 ? PathTileCost : 0;
                if (cost + tile > pounds) break;
                cost += tile;
                kept.Add((x, z));
                if (!takes) break;
            }
            return kept;
        }

        /// <summary>Whether a click on tile (x, z) opens the path tool (the port's control, master's call): any tile on
        /// the map that nothing is built on -- grass, path, the entrance road, buildable or not. Attraction footprints,
        /// entrances and exits, queues and track do not.</summary>
        /// <summary>What a tile of path and a tile of queue cost, in pounds.
        ///
        /// ⭐ FROM THE GAME'S CODE, and it took a correction to find: 0x8001B580 sets the path's per-tile value to
        /// **10** and the queue's to **25** (the two gp words at 0x80102714 and 0x80102718, read back by 0x8001B60C
        /// and 0x8001B624). While a run is being built, 0x8004F360 reads the value for the tile's own type, checks
        /// the balance against the run's total so far (the accumulator at 0x80102724) and REFUSES the tile when the
        /// money would run out -- so a run stops where the bank stops, it does not fail as a whole. The tool then
        /// hands the total to the same charge the placement tools use (0x8001C2E0, total x 10, the money unit).
        ///
        /// ⚠ The path tool's own vtable never touches the bank, which is exactly how this was missed: the charge is
        /// two calls deep, through a helper shared with the placement tools. "No bank call in the tool" is not the
        /// same as "no charge", and master knew the price before the disassembly did.</summary>
        public const int PathTileCost = 10, QueueTileCost = 25;

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
            var layer = NewLayer(map);
            int n = layer.LayRun(run);
            layer.WriteInto(map);
            return n;
        }

        /// <summary>What placing an attraction lays at its doors (0x80063B98, after the footprint and the door tiles):
        /// on the tile outside the entrance, a one-tile run (0x8001BD30 twice on the same tile) of queue for a ride
        /// and of path for anything else; on the tile outside the exit, one of path. Each joins its door.</summary>
        public void LayDoors(ParkMap map, AttractionDefinition a, int ox, int oz, int rot)
        {
            var layer = NewLayer(map);
            if (a.EntranceTile(ox, oz, rot) is { } e && a.EntranceDoor(ox, oz, rot) is { } d)
            {
                var link = new ParkPaths.QueueLink { DoorX = d.X, DoorZ = d.Z, OnRun = (x, z) => x == e.X && z == e.Z };
                layer.LaySegment(e.X, e.Z, e.X, e.Z, a.IsRide ? 4 : 2, link);
            }
            if (a.ExitTile(ox, oz, rot) is { } x2) layer.LaySegment(x2.X, x2.Z, x2.X, x2.Z, 2, null);
            layer.WriteInto(map);
        }

        /// <summary>Put the path sprites right around something that has just been taken off the map: any tile
        /// in the rect still linking to a tile nothing can link to loses that link and picks its sprite again.
        /// Returns how many tiles changed. See <see cref="Layer.DropDeadLinks"/> for why they are left behind.</summary>
        public int RefreshLinks(ParkMap map, int x0, int z0, int x1, int z1)
        {
            var layer = NewLayer(map);
            int fixedUp = 0;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (layer.DropDeadLinks(x, z)) fixedUp++;
            layer.WriteInto(map);
            return fixedUp;
        }

        /// <summary>How many tiles in the rect still link to something unlinkable. For tests: the count that
        /// has to be zero once <see cref="RefreshLinks"/> has run.</summary>
        public int DanglingLinks(ParkMap map, int x0, int z0, int x1, int z1)
        {
            var layer = NewLayer(map);
            int n = 0;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (layer.HasDeadLink(x, z)) n++;
            return n;
        }

        /// <summary>Take away the queue a ride already has: from the tile outside its entrance, every QUEUE tile
        /// (type 4) reachable across the four sides, each dropped the way the tool's own undo drops one
        /// (<see cref="Layer.RemoveQueueTile"/>, 0x8004FBEC — the links go from the neighbour too, and the tile is
        /// grass again). Returns how many went. ⭐ The tile outside the entrance itself is KEPT — see below.
        ///
        /// ⚠ **THE JOIN TILE IS NOT A QUEUE TILE AND IS LEFT ALONE.** Where a run ended ON a path, that tile became
        /// **13** — path AND queue, the only thing that ever bridges the two (see <see cref="QueueRun"/>). It is a
        /// PATH tile, the path is not the queue's to delete, and a fresh run can join it again. So the walk stops at
        /// 13 rather than treating it as part of the queue, and the park keeps its path.
        ///
        /// ⚠ It walks the MAP, not a remembered run: the tool's QueueRun is gone the moment the tool closes, and a
        /// queue can also be a shape the current run never laid.</summary>
        public int RemoveQueue(ParkMap map, AttractionDefinition a, int ox, int oz, int rot)
        {
            if (a.EntranceTile(ox, oz, rot) is not { } e) return 0;
            var layer = NewLayer(map);
            var seen = new System.Collections.Generic.HashSet<(int, int)>();
            var stack = new System.Collections.Generic.Stack<(int X, int Z)>();
            // ⭐⭐ THE DOOR'S OWN QUEUE TILE STAYS. The tile outside the entrance is laid by LayDoors when the
            // RIDE is placed, not by the player laying a queue, and StartQueue begins its run ON it
            // (0x8001DDD8). Take it away and the ride has no queue piece at its door and the tool has nothing
            // to start from. So it is marked seen without being removed, and the walk sets off from its
            // neighbours — everything the player actually laid.
            seen.Add((e.X, e.Z));
            stack.Push((e.X + 1, e.Z)); stack.Push((e.X - 1, e.Z));
            stack.Push((e.X, e.Z + 1)); stack.Push((e.X, e.Z - 1));
            int gone = 0;
            while (stack.Count > 0)
            {
                var (x, z) = stack.Pop();
                if (!seen.Add((x, z))) continue;
                if (x < 0 || z < 0 || x >= map.Width || z >= map.Height) continue;
                // PathQueueOverlap (13), the join, and everything else stop the walk.
                if (map[x, z].Type != TileType.QueuePath) continue;
                layer.RemoveQueueTile(x, z);
                gone++;
                stack.Push((x + 1, z)); stack.Push((x - 1, z));
                stack.Push((x, z + 1)); stack.Push((x, z - 1));
            }
            layer.WriteInto(map);
            return gone;
        }

        /// <summary>The queue tool for a ride just placed with its footprint's corner at (ox, oz): its run starts on
        /// the queue piece outside the entrance (0x8001DDD8, which asks the ride for that tile, 0x8009D240).</summary>
        public QueueRun StartQueue(AttractionDefinition a, int ox, int oz, int rot)
        {
            if (a.EntranceTile(ox, oz, rot) is not { } e || a.EntranceDoor(ox, oz, rot) is not { } d) return null;
            return new QueueRun(this, e, d);
        }
    }

    /// <summary>The queue tool's run (tool 3, vtable 0x800DC324), which placing a ride hands over to (0x8001C92C
    /// switches to tool 3). ⭐ FROM THE GAME'S CODE.
    ///
    /// It starts on the queue piece placing laid outside the entrance, and keeps the run's corners (0x80104AD8); it
    /// lays only while there are fewer than <see cref="MaxPoints"/> (0x8001DF08). The ghost runs from the last corner
    /// toward the cursor along one axis (0x8001D9D0 with kind 4), and a press lays it when no tile of it refuses
    /// (0x8001DF08 → 0x8001BD30): the queue then carries on from its new end. The run is FINISHED when the
    /// segment's last tile was path (it becomes path and queue, 13: the queue has joined the paths) or queue, or when
    /// the press lands on the run's own end. The undo (0x8001E114 → 0x8001BB08) takes the last segment back, up to
    /// the corner before it.
    ///
    /// The ghost's verdict per tile (0x8004F360, kind 4): flag 0x02 refuses; grass takes it; a queue tile takes it
    /// only if it is one of the run's corners and not a middle piece (two links); path or path and queue refuses
    /// except as the LAST tile, where it shows the join (code 5, marker #173); everything else refuses, and so does
    /// every tile after a refusal. Not ported: the bank check, and the attraction lookup for tiles with flag 0x10,
    /// which nothing sets yet.
    ///
    /// ⚠⚠ A QUEUE THAT STOPS ONE TILE SHORT OF THE PATH LOOKS CONNECTED AND IS NOT. This is the game's
    /// behaviour, not a port artefact, and it costs an afternoon every time. A path NEVER links straight to a
    /// queue: 0x8004E20C's neighbour test takes a type equal to the kind being laid, plus `type == 13 &amp;&amp;
    /// kind == 2`, and never type 4. The only bridge is the tile where the run ENDS ON a path, which becomes
    /// **13** — a path for linking and the queue's own neighbour-behind, so the route runs path → 13 → queue.
    /// Two tiles that merely touch are two structures that can never link, in any order.
    ///
    /// ✅ MEASURED both ways (tinyclaw, in the port): a queue ending at (21,34) with path at (21,33) — one tile
    /// short — refuses every route, including from the adjacent tile. The same park with the run extended onto
    /// (21,33) walks a mechanic the whole way and back. **The tell is the meeting tile's type: 13 bridged, 2 or
    /// 4 did not.**</summary>
    public sealed class QueueRun
    {
        readonly PathTool _tool;
        readonly System.Collections.Generic.List<(int X, int Z)> _points = new();
        readonly ParkPaths.QueueLink _link;

        /// <summary>0x8001DF08 lays a segment only while the run has fewer corners than this.</summary>
        public const int MaxPoints = 0x20;

        public System.Collections.Generic.IReadOnlyList<(int X, int Z)> Points => _points;
        public (int X, int Z) End => _points[^1];
        public bool Finished { get; private set; }

        public enum Step { Refused, Laid, Finished }

        internal QueueRun(PathTool tool, (int X, int Z) start, (int X, int Z) door)
        {
            _tool = tool;
            _points.Add(start);
            _link = new ParkPaths.QueueLink { DoorX = door.X, DoorZ = door.Z, OnRun = OnRun };
        }

        /// <summary>The ghost's far end for the cursor at (cx, cz): along whichever axis it has moved further on from
        /// the run's end (x only if strictly further).</summary>
        public (int X, int Z) Snap(int cx, int cz)
        {
            var (x0, z0) = End;
            return Math.Abs(cz - z0) < Math.Abs(cx - x0) ? (cx, z0) : (x0, cz);
        }

        /// <summary>The ghost from the run's end toward the cursor: per tile its marker sprite and verdict code, and
        /// whether a press would lay it (no tile refuses).</summary>
        public System.Collections.Generic.List<(int X, int Z, int Sprite, int Code)> Ghost(ParkMap map, int cx, int cz, out bool valid)
        {
            var (ex, ez) = Snap(cx, cz);
            var run = PathTool.Run(End.X, End.Z, ex, ez);
            var ghost = new System.Collections.Generic.List<(int, int, int, int)>();
            valid = true;
            for (int i = 0; i < run.Count; i++)
            {
                var (x, z) = run[i];
                int code = valid ? Verdict(map, x, z, i == run.Count - 1) : 1;
                if (code == 1) valid = false;
                ghost.Add((x, z, _tool.Marker(code), code));
            }
            return ghost;
        }

        int Verdict(ParkMap map, int x, int z, bool last)
        {
            if (x < 0 || x >= map.Width - 1 || z < 0 || z >= map.Height - 1) return 1;
            var t = map[x, z];
            if ((t.Flags & 0x02) != 0) return 1;
            if (t.Raw0 == 4) return Popcount(t.Links) == 2 || !IsPoint(x, z) ? 1 : 0;
            if ((t.Flags & 0x10) != 0) return 0;
            return t.Raw0 switch { 0 => 0, 2 or 13 => last ? 5 : 1, _ => 1 };
        }

        /// <summary>A press with the cursor at (cx, cz), as 0x8001DF08 takes it.</summary>
        public Step Lay(ParkMap map, int cx, int cz)
        {
            if (Finished) return Step.Refused;
            Ghost(map, cx, cz, out bool valid);
            if (!valid || _points.Count >= MaxPoints) return Step.Refused;
            var start = End;
            var end = Snap(cx, cz);
            bool ended = false;
            if (end != start || _points.Count == 1)
            {
                _points.Add(end);
                var layer = _tool.NewLayer(map);
                ended = layer.LaySegment(start.X, start.Z, end.X, end.Z, 4, _link);
                layer.WriteInto(map);
            }
            if (end != start && !ended) return Step.Laid;
            Finished = true;
            _link.PrevDx = _link.PrevDz = ParkPaths.QueueLink.NoWay;
            return Step.Finished;
        }

        /// <summary>What the segment to the cursor would cost, in pounds: its tiles that are not already queue
        /// (PathTool.QueueTileCost each), up to the first the ghost refuses.</summary>
        public int GhostCost(ParkMap map, int cx, int cz)
        {
            int cost = 0;
            foreach (var (x, z, _, code) in Ghost(map, cx, cz, out _))
            {
                if (code == 1) break;
                if (map[x, z].Raw0 != 4) cost += PathTool.QueueTileCost;
            }
            return cost;
        }

        /// <summary>Take the last segment back (0x8001BB08): its tiles, all but the corner it started from, are grass
        /// again, and that corner is the queue's end. False when there is nothing to take back.</summary>
        public bool Undo(ParkMap map)
        {
            if (Finished || _points.Count <= 1) return false;
            var gone = _points[^1];
            _points.RemoveAt(_points.Count - 1);
            var back = End;
            if (gone == back) return true;
            int sx = Math.Sign(back.X - gone.X), sz = Math.Sign(back.Z - gone.Z);
            var layer = _tool.NewLayer(map);
            foreach (var (x, z) in PathTool.Run(gone.X, gone.Z, back.X - sx, back.Z - sz)) layer.RemoveQueueTile(x, z);
            layer.WriteInto(map);
            return true;
        }

        /// <summary>0x8001B76C: whether (x, z) lies on the run, any tile of any segment between its corners.</summary>
        bool OnRun(int x, int z)
        {
            for (int i = 0; i + 1 < _points.Count; i++)
                foreach (var p in PathTool.Run(_points[i].X, _points[i].Z, _points[i + 1].X, _points[i + 1].Z))
                    if (p.X == x && p.Z == z) return true;
            return false;
        }

        /// <summary>0x8004D298: whether (x, z) is one of the run's corners.</summary>
        bool IsPoint(int x, int z)
        {
            foreach (var p in _points) if (p.X == x && p.Z == z) return true;
            return false;
        }

        static int Popcount(int b) { int n = 0; for (; b != 0; b &= b - 1) n++; return n; }
    }
}
