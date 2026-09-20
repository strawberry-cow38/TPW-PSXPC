using System;
using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>Which kinds of tile a request is willing to cross (pathfinder.md §5.2, `flagA`).
    ///
    /// ⚠ THREE BITS ARE DEAD. Only 0x01, 0x02, 0x08, 0x10 and 0x20 are ever tested; 0x04, 0x40 and 0x80
    /// are never read by the search. They are named here so that a call site copied out of the findings
    /// with one of them set does not look like it means something.</summary>
    [Flags]
    public enum PathFlags
    {
        None = 0,
        /// <summary>Ordinary path (2), path-over-queue (13) and the park gate (12).</summary>
        Path = 0x01,
        /// <summary>Grass, at double cost and subject to the grass gate.</summary>
        Grass = 0x02,
        /// <summary>Never tested by the search.</summary>
        Dead04 = 0x04,
        /// <summary>A building's footprint tiles, at double cost.</summary>
        Footprint = 0x08,
        /// <summary>Queue path (4).</summary>
        Queue = 0x10,
        /// <summary>The tiles beside the gate (14).</summary>
        GateSide = 0x20,
        /// <summary>Never tested by the search.</summary>
        Dead40 = 0x40,
        /// <summary>Never tested by the search.</summary>
        Dead80 = 0x80,
    }

    /// <summary>What the search tells the walker when it is done (pathfinder.md §8). These are the
    /// original's own message ids, delivered through the person's vtable slot 40.</summary>
    public enum PathMessage
    {
        /// <summary>A chain of waypoints is waiting on the walker's head index.</summary>
        Found = 1,
        /// <summary>No path, or the search ran out of budget, nodes or waypoints. The walker cannot
        /// tell which from the message alone - and neither could the original.</summary>
        Failed = 2,
    }

    /// <summary>The map, as the search reads it. Three bytes of a tile and one park-wide flag; the
    /// search never writes to the map.</summary>
    public interface IPathMap
    {
        int Width { get; }
        int Height { get; }
        /// <summary>Tile byte +0: the type. See TPW.Data.TileType for the names.</summary>
        int TypeAt(int x, int y);
        /// <summary>Tile byte +2: which sides this tile is joined to. Bits 0x01, 0x04, 0x10, 0x40 are
        /// the four orthogonal directions - see <see cref="Pathfinder"/>'s direction table.</summary>
        int LinkBitsAt(int x, int y);
        /// <summary>Tile byte +7: the flags. 0x02 unbuildable, 0x08 the starting path, 0x10 and 0x40
        /// are run-time bits whose meaning beyond the grass gate is not established.</summary>
        int FlagsAt(int x, int y);
        /// <summary>The park is open to the public (0x800541AC). The grass gate consults it.</summary>
        bool ParkIsOpen { get; }
    }

    /// <summary>Whoever asked for the path: a guest, a member of staff, anything that walks.</summary>
    public interface IPathClient
    {
        /// <summary>The head of this walker's waypoint chain, or <see cref="WaypointPool.NoChain"/>.
        /// The pathfinder frees whatever is here before it builds a replacement, and writes the new
        /// head on success.</summary>
        int WaypointHead { get; set; }
        /// <summary>Delivery of <see cref="PathMessage.Found"/> or <see cref="PathMessage.Failed"/>.
        ///
        /// ⚠ THIS IS CALLED FROM INSIDE THE SEARCH, and what has and has not been cleaned up by then
        /// differs between the four failure cases - see <see cref="Pathfinder"/>. A handler that asks
        /// for another path immediately will sometimes be refused for reasons that have nothing to do
        /// with the map.</summary>
        void OnPathMessage(PathMessage message);
    }

    /// <summary>One node of a search: a tile, what it cost to get there, and where it came from.</summary>
    sealed class PathNode
    {
        public readonly int Index;
        public PathNode(int index) => Index = index;

        public int G, H, X, Y;
        public PathNode Parent;
        public PathNode Next, Prev;

        public int F => G + H;
    }

    /// <summary>The shared node pool (pathfinder.md §6.1, overlay 0x801141D8).
    ///
    /// ⭐⭐ ONE POOL OF 2000 SERVES EVERY SEARCH AT ONCE. The open and closed lists of all ten
    /// in-flight requests draw from it, so a single large search starves the others, and a park that
    /// runs it dry produces path failures that have nothing to do with the routes being asked for.
    /// This, not any per-search limit, is what bounds how big a search can get.</summary>
    sealed class NodePool
    {
        readonly PathNode[] _nodes;
        readonly int[] _stack;
        int _count;

        public NodePool(int capacity)
        {
            _nodes = new PathNode[capacity];
            _stack = new int[capacity];
            for (int i = 0; i < capacity; i++) _nodes[i] = new PathNode(i);
            Reset();
        }

        public int Capacity => _nodes.Length;
        public int FreeCount => _count;

        /// <summary>0x800ED2FC: every node free again. Called at mode init and shutdown, and by the
        /// "build item put down" unpause.</summary>
        public void Reset()
        {
            for (int i = 0; i < _nodes.Length; i++) _stack[i] = i;
            _count = _nodes.Length;
        }

        /// <summary>0x800ED1DC. Null when the pool is empty.</summary>
        public PathNode Alloc()
        {
            if (_count == 0) return null;
            var n = _nodes[_stack[--_count]];
            n.G = n.H = n.X = n.Y = 0;
            n.Parent = n.Next = n.Prev = null;
            return n;
        }

        /// <summary>0x800ECDE8.</summary>
        public void Free(PathNode n) => _stack[_count++] = n.Index;
    }

    /// <summary>The open list: a doubly-linked list kept sorted by f (pathfinder.md §6.2).
    ///
    /// ⚠ BOTH THE MEMBERSHIP SCAN AND THE INSERT WALK ARE LINEAR. The algorithm is not what the frame
    /// budget is protecting - this list is. Replacing it with a heap or a dictionary would change which
    /// of several equally good paths comes out, because the tie rule below is positional.</summary>
    sealed class OpenList
    {
        public PathNode Head, Tail;

        public bool IsEmpty => Head == null;

        public void Clear() => Head = Tail = null;

        /// <summary>0x800ECE70: the cheapest node, unlinked.</summary>
        public PathNode PopHead()
        {
            var n = Head;
            Unlink(n);
            return n;
        }

        public PathNode Find(int x, int y)
        {
            for (var n = Head; n != null; n = n.Next) if (n.X == x && n.Y == y) return n;
            return null;
        }

        public void Unlink(PathNode n)
        {
            if (n.Prev != null) n.Prev.Next = n.Next; else Head = n.Next;
            if (n.Next != null) n.Next.Prev = n.Prev; else Tail = n.Prev;
            n.Next = n.Prev = null;
        }

        /// <summary>0x800ECED8: insert in f order, entering from whichever END is closer in f.
        ///
        /// ⭐ THE TIE RULE DEPENDS ON WHICH END IT WALKED IN FROM. Coming from the front a new node
        /// goes AHEAD of nodes with the same f; coming from the back it goes BEHIND them. So two nodes
        /// with identical f expand in an order that depends on what else is in the list - which, with
        /// the random neighbour order in <see cref="Pathfinder"/>, is why the same map and the same
        /// endpoints do not always give the same route.</summary>
        public void InsertSorted(PathNode n)
        {
            // The original's sentinel makes the empty case fall out of the same code, reading the
            // sentinel's uninitialised f; either walk then stops immediately and the node becomes the
            // only element, so the garbage cannot change the outcome. Handled explicitly here.
            if (Head == null) { Head = Tail = n; return; }

            int f = n.F;
            int d1 = Math.Abs(f - Head.F), d2 = Math.Abs(f - Tail.F);
            if (d1 < d2)
            {
                var cur = Head;
                while (cur != null && cur.F < f) cur = cur.Next;
                InsertBefore(cur, n);
            }
            else
            {
                var cur = Tail;
                while (cur != null && f < cur.F) cur = cur.Prev;
                InsertAfter(cur, n);
            }
        }

        void InsertBefore(PathNode at, PathNode n)
        {
            if (at == null) { n.Prev = Tail; Tail.Next = n; Tail = n; return; }
            n.Next = at; n.Prev = at.Prev;
            if (at.Prev != null) at.Prev.Next = n; else Head = n;
            at.Prev = n;
        }

        void InsertAfter(PathNode at, PathNode n)
        {
            if (at == null) { n.Next = Head; Head.Prev = n; Head = n; return; }
            n.Prev = at; n.Next = at.Next;
            if (at.Next != null) at.Next.Prev = n; else Tail = n;
            at.Next = n;
        }

        public IEnumerable<PathNode> All()
        {
            for (var n = Head; n != null; n = n.Next) yield return n;
        }
    }

    /// <summary>The closed set: four unsorted buckets by `(x + y) &amp; 3` (pathfinder.md §6.3).</summary>
    sealed class ClosedSet
    {
        readonly PathNode[] _heads = new PathNode[4];

        public static int BucketOf(int x, int y) => (x + y) & 3;

        public void Clear() => Array.Clear(_heads, 0, 4);

        public PathNode Find(int x, int y)
        {
            for (var n = _heads[BucketOf(x, y)]; n != null; n = n.Next)
                if (n.X == x && n.Y == y) return n;
            return null;
        }

        /// <summary>0x800ECE40: push-front onto the node's own bucket.</summary>
        public void PushFront(PathNode n)
        {
            int b = BucketOf(n.X, n.Y);
            n.Prev = null;
            n.Next = _heads[b];
            if (_heads[b] != null) _heads[b].Prev = n;
            _heads[b] = n;
        }

        public void Unlink(PathNode n)
        {
            int b = BucketOf(n.X, n.Y);
            if (n.Prev != null) n.Prev.Next = n.Next; else _heads[b] = n.Next;
            if (n.Next != null) n.Next.Prev = n.Prev;
            n.Next = n.Prev = null;
        }

        public IEnumerable<PathNode> All()
        {
            for (int b = 0; b < 4; b++)
                for (var n = _heads[b]; n != null; n = n.Next) yield return n;
        }
    }

    /// <summary>One in-flight search (pathfinder.md §1.1). Ten of these exist and are reused.</summary>
    sealed class PathRequest
    {
        public IPathClient Client;
        public int FromTileX, FromTileY, ToTileX, ToTileY;
        /// <summary>The destination exactly as the caller gave it, in 8.8 units. This, not the tile
        /// centre, becomes the last waypoint.</summary>
        public int RawToX, RawToY;
        public PathFlags FlagA;
        public int FlagB;
        public int Budget;
        public readonly OpenList Open = new();
        public readonly ClosedSet Closed = new();

        public void Reset()
        {
            Client = null;
            Open.Clear();
            Closed.Clear();
            Budget = 0;
        }
    }

    /// <summary>Theme Park World's pathfinder (findings/pathfinder.md), read out of TPW.BIN.
    ///
    /// ⭐ IT IS A\*, AND IT IS ASYNCHRONOUS. `Request` does not return a path - it returns whether the
    /// search was ACCEPTED. The answer arrives later, on a frame that may be two hundred frames away,
    /// as a message to the walker. Everything in the visitor and staff machines that looks like it is
    /// waiting for nothing is waiting for this.
    ///
    /// ⭐ THE TWO REFUSALS ARE SILENT AND MEAN DIFFERENT THINGS FROM A FAILURE. Ten searches already
    /// outstanding, or no nodes left, and `Request` returns false having sent nothing and queued
    /// nothing. A caller that treats that as "unreachable" is wrong; the game's own callers retry
    /// (state 23 next tick) or back off (state 6 for 360 ticks).
    ///
    /// ⚠ THE SEARCH IS NOT DETERMINISTIC given the same map and endpoints. The four neighbours are
    /// visited in one of four fixed orders chosen at random per expansion, and the open list's tie rule
    /// is positional. Costs are optimal; WHICH optimal route comes out is not repeatable. Any test that
    /// pins an exact route must pin the dice too.</summary>
    public sealed class Pathfinder
    {
        /// <summary>Ten outstanding searches; the eleventh request is refused (§2).</summary>
        public const int MaxRequests = 10;
        /// <summary>The shared node pool, 2000 (§6.1).</summary>
        public const int NodePoolSize = 2000;
        /// <summary>Slices a search may consume before it gives up: 200 (§1.1, request +0x30).</summary>
        public const int SliceBudget = 200;

        /// <summary>The largest tile coordinate a search node can hold.
        ///
        /// ⚠ NODES STORE x AND y AS BYTES and a waypoint stores them as SIGNED bytes, so the original
        /// silently corrupts any map wider or taller than this. The constructor refuses such a map
        /// rather than reproducing the corruption, because a wrapped coordinate produces a plausible
        /// route to the wrong place - the worst kind of wrong. The shipped maps are 44x74.</summary>
        public const int MaxMapExtent = 128;

        /// <summary>Which way each direction index goes (overlay 0x8011417C).</summary>
        static readonly int[] Dx = { 1, 0, -1, 0 };
        static readonly int[] Dy = { 0, 1, 0, -1 };

        /// <summary>The link bit a tile must have set to leave it in each direction (0x801036D0).</summary>
        static readonly int[] LinkBit = { 0x04, 0x10, 0x40, 0x01 };

        /// <summary>The four neighbour orders (0x80102510), one chosen at random per expansion.</summary>
        static readonly int[][] NeighbourOrders =
        {
            new[] { 0, 1, 2, 3 },
            new[] { 3, 2, 1, 0 },
            new[] { 2, 1, 3, 0 },
            new[] { 1, 3, 0, 2 },
        };

        readonly IPathMap _map;
        readonly WaypointPool _waypoints;
        readonly IRandomSource _rng;
        readonly NodePool _nodes;
        readonly List<PathRequest> _free = new(MaxRequests);
        readonly List<PathRequest> _active = new(MaxRequests);

        bool _paused;
        int _expansionsThisFrame;

        /// <summary>How many expansions one slice may do.
        ///
        /// ⚠⚠ THE ONE PLACE THIS PORT CANNOT BE THE ORIGINAL. The game does not count expansions - it
        /// watches the PlayStation's horizontal-blank counter and stops when the next expansion would
        /// push it past 100 scanlines, about 6.4 ms of a PAL frame. How many expansions that buys
        /// depends on how long the open list has grown, because the membership scan is linear, so
        /// there is no fixed number to copy and inventing one would be inventing a constant.
        ///
        /// Unbounded by default, which means a search finishes on the frame it is accepted. That is
        /// FASTER than the original, and two consequences follow: a walker never stands waiting for a
        /// route, and the 200-slice budget can never run out. Set this to make the search stagger the
        /// way the original does; the value is the host's to measure, not this file's to guess.</summary>
        public int ExpansionsPerSlice { get; set; } = int.MaxValue;

        /// <summary>The pool size is a parameter ONLY so that the exhaustion paths can be exercised:
        /// draining two thousand nodes to prove what happens at zero costs a second a test and nothing
        /// is learned that twenty nodes do not teach. The default is the game's.</summary>
        public Pathfinder(IPathMap map, WaypointPool waypoints, IRandomSource rng,
                          int nodePoolSize = NodePoolSize)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _waypoints = waypoints ?? throw new ArgumentNullException(nameof(waypoints));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _nodes = new NodePool(nodePoolSize);
            if (map.Width > MaxMapExtent || map.Height > MaxMapExtent)
                throw new ArgumentOutOfRangeException(nameof(map),
                    $"a search node holds x and y in a byte and a waypoint in a signed byte; " +
                    $"{map.Width}x{map.Height} exceeds {MaxMapExtent} and the original would wrap silently");

            for (int i = 0; i < MaxRequests; i++) _free.Add(new PathRequest());
        }

        /// <summary>How many of the ten request slots are in use.</summary>
        public int ActiveRequests => _active.Count;
        /// <summary>Nodes left in the shared pool.</summary>
        public int FreeNodes => _nodes.FreeCount;
        /// <summary>Whether a held build item has stopped the search (§2).</summary>
        public bool IsPaused => _paused;

        /// <summary>0x800EC9F4: ask for a path. Coordinates are 8.8 world units.
        ///
        /// Returns whether the search was ACCEPTED, not whether a route exists. Both refusals are
        /// silent: no message is ever sent for them.</summary>
        public bool Request(IPathClient client, int fromX, int fromY, int toX, int toY,
                            PathFlags flagA, int flagB)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            if (_free.Count == 0) return false;          // 0x800ECBF8: ten already outstanding
            if (_nodes.FreeCount == 0) return false;     // 0x800ECFC0: no node for the seed

            var req = _free[_free.Count - 1];
            _free.RemoveAt(_free.Count - 1);

            req.Reset();
            req.Client = client;
            req.FromTileX = ClampX(fromX >> 8);
            req.FromTileY = ClampY(fromY >> 8);
            req.ToTileX = ClampX(toX >> 8);
            req.ToTileY = ClampY(toY >> 8);
            req.RawToX = toX;
            req.RawToY = toY;
            req.FlagA = flagA;
            req.FlagB = flagB;
            req.Budget = SliceBudget;

            var seed = _nodes.Alloc();
            seed.G = 0;
            seed.H = Math.Abs(req.ToTileX - req.FromTileX) + Math.Abs(req.ToTileY - req.FromTileY);
            seed.X = req.FromTileX;
            seed.Y = req.FromTileY;
            seed.Parent = null;
            req.Open.InsertSorted(seed);

            // Push-front: the active list is newest-first, and the scheduler starts at the head.
            _active.Insert(0, req);
            return true;
        }

        // ⚠ CLAMPED TO w-1, NOT w-2. The pathfinder's world includes the last row and column, which the
        // placement check (0x800508C8, TPW.Data.ParkBuild) excludes. The two disagree deliberately.
        int ClampX(int x) => x < 0 ? 0 : x >= _map.Width ? _map.Width - 1 : x;
        int ClampY(int y) => y < 0 ? 0 : y >= _map.Height ? _map.Height - 1 : y;

        /// <summary>0x800EC8C4: one frame of searching.
        ///
        /// ⭐ THE NEWEST REQUEST ALWAYS GETS A SLICE; everything after it only runs if its flagB is 1
        /// AND the frame has time left. Since the first slice normally uses the frame, an older
        /// flagB == 0 request can wait a long time - but its own 200-slice budget is NOT spent while it
        /// waits, because the budget is decremented inside the worker.</summary>
        public void RunFrame()
        {
            if (_paused) return;                       // [0x801036C8]: a build item is held

            _expansionsThisFrame = 0;
            bool ranOne = false;

            // The original reads each request's `next` before running it, so a request that finishes
            // and is recycled cannot break the walk. A snapshot does the same thing.
            var snapshot = _active.ToArray();
            foreach (var req in snapshot)
            {
                if (ranOne && req.FlagB != 1) continue;
                if (Work(req)) Recycle(req);
                ranOne = true;
                if (_expansionsThisFrame >= ExpansionsPerSlice) return;
            }
        }

        /// <summary>0x800EC490: one slice of one search. True when the request is finished.</summary>
        bool Work(PathRequest req)
        {
            if (req.Budget == 0)
            {
                // Two hundred slices used. Note the ORDER: the nodes go back first, then the message -
                // so a handler that asks for another path here finds the pool replenished.
                FreeLists(req);
                req.Client.OnPathMessage(PathMessage.Failed);
                return true;
            }
            req.Budget--;

            int thisSlice = 0;
            while (true)
            {
                if (req.Open.IsEmpty)
                {
                    // Genuinely unreachable under these flags. Here the message goes FIRST and the
                    // nodes come back after - the opposite of the budget case above, and the opposite
                    // of the pool-empty case below. All three are as read.
                    req.Client.OnPathMessage(PathMessage.Failed);
                    FreeLists(req);
                    return true;
                }

                var n = req.Open.PopHead();

                // ⭐ THE GOAL IS TESTED WHEN A NODE IS POPPED, not when it is discovered. That is what
                // makes the route cost-optimal: a cheaper way to the goal still in the open list wins.
                if (n.X == req.ToTileX && n.Y == req.ToTileY)
                {
                    bool ok = BuildChain(req, n);
                    req.Client.OnPathMessage(ok ? PathMessage.Found : PathMessage.Failed);
                    FreeLists(req);
                    return true;
                }

                if (!Expand(req, n))
                {
                    // ⚠ ONE NODE LEAKS HERE, IN THE ORIGINAL AND SO HERE TOO. `n` has been popped off
                    // the open list and has not been pushed onto the closed list, and FreeLists only
                    // walks the lists - so it is never returned to the pool. It comes back only at the
                    // next pool reset. Reproduced deliberately: a park that keeps exhausting the pool
                    // loses a node each time, which makes the failure progressive rather than steady,
                    // and that is a symptom someone will eventually have to recognise.
                    FreeLists(req);
                    req.Client.OnPathMessage(PathMessage.Failed);
                    return true;
                }

                thisSlice++;
                _expansionsThisFrame++;
                req.Closed.PushFront(n);

                if (thisSlice >= ExpansionsPerSlice || _expansionsThisFrame >= ExpansionsPerSlice)
                    return false;                      // yield; the request keeps its place
            }
        }

        /// <summary>0x800EBFD4: expand one node. False when the node pool ran dry.</summary>
        bool Expand(PathRequest req, PathNode node)
        {
            int x = node.X, y = node.Y;
            int mask = MaskOf(x, y);
            int[] order = NeighbourOrders[_rng.Next(4)];

            for (int i = 0; i < 4; i++)
            {
                int dir = order[i];
                int nx = x + Dx[dir], ny = y + Dy[dir];
                if (nx < 0 || nx >= _map.Width) continue;
                if (ny < 0 || ny >= _map.Height) continue;

                int t = _map.TypeAt(nx, ny);
                if (t >= 15) continue;

                if (!Admits(req, t, nx, ny, mask, LinkBit[dir], out int cost)) continue;
                if (!Admit(req, node, nx, ny, cost)) return false;
            }
            return true;
        }

        /// <summary>The mask the link test is applied to (0x800EC028).
        ///
        /// ⚠ THE TEST IS ON THE TILE BEING LEFT, NEVER THE ONE BEING ENTERED. "Does where I am say it
        /// joins that way", not "does that tile accept me". Plain grass and footprint tiles answer yes
        /// in all four directions, so a route can step off grass onto a path whose own links point
        /// elsewhere.</summary>
        int MaskOf(int x, int y)
        {
            int t = _map.TypeAt(x, y);
            if (t == 0 && (_map.FlagsAt(x, y) & 0x02) == 0) return 0x55;   // 0x8004D60C
            if (t == 5) return 0x55;                                        // 0x8004D5E8
            return _map.LinkBitsAt(x, y);
        }

        /// <summary>Whether a neighbour of this type may be stepped onto, and what the step costs
        /// (pathfinder.md §5.2). Types 1, 3, 6, 9, 10 and 11 are never walkable.</summary>
        bool Admits(PathRequest req, int type, int nx, int ny, int mask, int bit, out int cost)
        {
            bool destination = nx == req.ToTileX && ny == req.ToTileY;
            switch (type)
            {
                case 0:                                        // grass
                    cost = 2;
                    return Has(req, PathFlags.Grass) && GrassGate(nx, ny);
                case 2:
                case 13:                                       // path, path over queue
                    cost = 1;
                    return Has(req, PathFlags.Path) && (mask & bit) != 0;
                case 4:                                        // queue path
                    // ⚠⚠ IF A GUEST WILL NOT ENTER A QUEUE, SUSPECT THE LINKER, NOT THIS. A path tile's bits
                    // never point at a queue tile — the game refuses that join outright (0x8004E20C) — so the
                    // only way in is over the type-13 tile a queue run makes when it ENDS ON a path. A queue
                    // that stops one tile short looks connected on screen and can never be routed into, from
                    // any adjacent tile. Measured both ways; see ParkPaths.QueueRun.
                    cost = 1;
                    return Has(req, PathFlags.Queue) && (mask & bit) != 0;
                case 5:                                        // a building's footprint
                    // ⭐ THE DESTINATION IS ALWAYS ENTERABLE even without the flag. That is how a guest
                    // reaches a shop counter standing on the building's own tiles.
                    cost = 2;
                    return Has(req, PathFlags.Footprint) || destination;
                case 7:                                        // an attraction's entrance
                    // ⚠ ONLY AS A DESTINATION. An entrance tile is never a through-route, whatever the
                    // flags say, so a queue cannot be short-cut across someone else's entrance.
                    cost = 1;
                    return (mask & bit) != 0 && destination;
                case 8:                                        // an attraction's exit
                    // ⚠ NO FLAG AND NO LINK TEST: always walkable. The only tile type like it.
                    cost = 1;
                    return true;
                case 12:                                       // the park gate
                    // ⚠ NO LINK TEST, unlike ordinary path on the same flag.
                    cost = 1;
                    return Has(req, PathFlags.Path);
                case 14:                                       // the tiles beside the gate
                    cost = 1;
                    return Has(req, PathFlags.GateSide);
                default:
                    cost = 0;
                    return false;
            }
        }

        static bool Has(PathRequest req, PathFlags f) => (req.FlagA & f) != 0;

        /// <summary>0x8004D388, as the search reaches it - which is only ever with a type 0 tile, so
        /// the routine's first condition collapses to "flag 0x02 is clear".
        ///
        /// ⚠ THE LAST ARM DEPENDS ON THE PARK BEING OPEN, which means some grass becomes walkable at
        /// the moment the park opens and stops being walkable when it closes. The meanings of flags
        /// 0x08, 0x10 and 0x40 beyond these three tests are NOT ESTABLISHED (paths.md §7 agrees).</summary>
        bool GrassGate(int x, int y)
        {
            int f = _map.FlagsAt(x, y);
            if ((f & 0x02) == 0 && (f & 0x10) == 0) return true;
            if ((f & 0x10) != 0 && (f & 0x40) != 0) return true;
            // The original's `|| type == 12` cannot fire here: this is only reached for type 0.
            return (f & 0x08) != 0 && _map.ParkIsOpen;
        }

        /// <summary>0x800EC28C: score the neighbour and put it on the open list if it is an improvement.
        /// False when a new node was needed and the pool was empty.</summary>
        bool Admit(PathRequest req, PathNode parent, int nx, int ny, int cost)
        {
            int h = Math.Abs(nx - req.ToTileX) + Math.Abs(ny - req.ToTileY);
            int g = parent.G + cost;
            int f = g + h;

            var closed = req.Closed.Find(nx, ny);
            if (closed != null && !(f < closed.F)) return true;

            // ⚠ THE OPEN LIST IS NOT SEARCHED WHEN THE CLOSED LIST ALREADY MATCHED. A tile cannot be in
            // both, so this is an optimisation rather than a rule - but it is the order as read.
            var open = closed != null ? null : req.Open.Find(nx, ny);
            if (open != null && !(f < open.F)) return true;

            PathNode n;
            if (open != null) { req.Open.Unlink(open); n = open; }
            else if (closed != null)
            {
                // ⭐ A CLOSED NODE CAN BE RE-OPENED - but only on a strictly cheaper f, and the
                // heuristic is consistent (Manhattan, every step costs 1 or 2), so this can never
                // actually fire. It is defensive code in the original and it is kept as such.
                req.Closed.Unlink(closed);
                n = closed;
            }
            else
            {
                n = _nodes.Alloc();
                if (n == null) return false;
            }

            n.G = g; n.H = h; n.X = nx; n.Y = ny; n.Parent = parent;
            req.Open.InsertSorted(n);
            return true;
        }

        /// <summary>0x800EC748: turn the parent chain into waypoints. False when the waypoint pool
        /// ran out - in which case the partial chain is given back and the walker keeps nothing.</summary>
        bool BuildChain(PathRequest req, PathNode goal)
        {
            // Whatever the walker was following is dropped first, path found or not.
            if (req.Client.WaypointHead != WaypointPool.NoChain)
            {
                _waypoints.FreeChain(req.Client.WaypointHead);
                req.Client.WaypointHead = WaypointPool.NoChain;
            }

            int prevDir = -1, tail = -1;
            bool first = true;

            for (var n = goal; n != null; n = n.Parent)
            {
                int d = StepDirection(n);
                if (d == prevDir) continue;            // still going the same way: not a corner

                int i = _waypoints.Alloc();
                if (i < 0)
                {
                    _waypoints.FreeChain(tail);
                    _nodes.Free(goal);
                    return false;
                }

                // ⭐ THE LAST WAYPOINT IS THE CALLER'S EXACT DESTINATION, not the centre of its tile.
                // Every other one is a tile centre. That is how a guest ends up at a specific spot in
                // a queue or on a ride rather than in the middle of a square.
                if (first) { _waypoints.Encode(i, req.RawToX, req.RawToY); first = false; }
                else _waypoints.Encode(i, (n.X << 8) | 0x80, (n.Y << 8) | 0x80);

                _waypoints.SetNext(i, tail);
                tail = i;
                prevDir = d;
            }

            // The goal node is never closed and never freed by FreeLists; it goes back here.
            _nodes.Free(goal);
            req.Client.WaypointHead = tail;
            return true;
        }

        /// <summary>0x800ED478: which way the step into this node went, in the game's facing codes.
        ///
        /// ⚠ NO PARENT GIVES 0, AND SO DOES A STEP NORTH. The start node has no parent, so its code
        /// collides with −y - which means the walker's own tile gets a waypoint of its own EXCEPT when
        /// the first leg heads north, where it is silently skipped as a non-corner. That asymmetry is
        /// real and is what the findings read; it is not a transcription slip.</summary>
        static int StepDirection(PathNode n)
        {
            if (n.Parent == null) return 0;
            int dx = n.X - n.Parent.X;
            if (dx != 0) return dx > 0 ? 2 : 6;
            return n.Y - n.Parent.Y > 0 ? 4 : 0;
        }

        /// <summary>0x800EC6C0: every node on the open and closed lists back to the pool.</summary>
        void FreeLists(PathRequest req)
        {
            foreach (var n in req.Open.All()) _nodes.Free(n);
            foreach (var n in req.Closed.All()) _nodes.Free(n);
            req.Open.Clear();
            req.Closed.Clear();
        }

        /// <summary>0x800ECC04: the slot goes back. ⚠ This happens in the SCHEDULER, after the message
        /// has been delivered - so a walker that asks for a new path from inside its own message
        /// handler is still holding this slot.</summary>
        void Recycle(PathRequest req)
        {
            _active.Remove(req);
            req.Reset();
            _free.Add(req);
        }

        /// <summary>0x800EBA2C: a build item has been picked up. Searches stop where they are; their
        /// budgets are not spent while paused.</summary>
        public void Pause() => _paused = true;

        /// <summary>0x800EBBE4: a build item has been put down.
        ///
        /// ⚠⚠ EVERY OUTSTANDING SEARCH IS DROPPED WITHOUT A WORD. No Found, no Failed - the pools are
        /// simply rebuilt underneath them. Ten walkers can be left waiting for a message that is never
        /// coming, and the only reason the game does not deadlock is that their own states time out or
        /// retry. Worse, the waypoint pool is wiped too while nobody's head index is cleared, so a
        /// walker mid-route is left pointing at an entry that is now free (see
        /// <see cref="WaypointPool.Reset"/> for the one deviation this port makes about that).
        ///
        /// The original also clears a per-person flag across all six person lists; that belongs to the
        /// host, not here.</summary>
        public void ResetAfterBuildItemPlaced()
        {
            _waypoints.Reset();
            _nodes.Reset();
            _active.Clear();
            _free.Clear();
            for (int i = 0; i < MaxRequests; i++) _free.Add(new PathRequest());
            _paused = false;
        }
    }
}
