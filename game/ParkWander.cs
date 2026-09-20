using System;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>The park as the guest's real wander reads it (TPW.Sim.VisitorWander, behaviour.md §2.7).
    ///
    /// ⭐⭐ THE REAL WANDER DOES NOT USE THE PATHFINDER AT ALL when the guest is standing on a path. It
    /// walks rand(10) steps neighbour to neighbour, testing the CURRENT tile's link bits, and chains the
    /// waypoints itself. The port's stand-in picked a random walkable tile anywhere on the map and asked
    /// the pathfinder for a route to it — and most of those tiles are on path islands nothing connects
    /// to, so a park of thirty guests produced FOUR THOUSAND failed searches in one run and held all ten
    /// request slots permanently. Every guest with somewhere real to go was queueing behind a wander.
    ///
    /// ⚠ THE PATHFINDER IS STILL USED OFF-PATH, which is the game's own fallback: a guest standing on
    /// grass looks for a path tile in a ring, and failing that asks for a route to somewhere near the
    /// map centre.</summary>
    sealed class ParkWanderWorld : IWanderWorld
    {
        readonly ParkMap _map;
        readonly WaypointPool _pool;
        readonly Func<long> _now;
        readonly Func<Guest, int, int, int, int, bool> _request;

        public ParkWanderWorld(ParkMap map, WaypointPool pool, Func<long> now,
                               Func<Guest, int, int, int, int, bool> request)
        { _map = map; _pool = pool; _now = now; _request = request; }

        /// <summary>The guest the next call is about. Visitor carries no position in the sim, so the host
        /// holds it; set this before every call, as GuestBrain is set per guest.</summary>
        public Guest Current;

        public long NowTick => _now();
        public int MapWidth => _map.Width;
        public int MapHeight => _map.Height;

        /// <summary>⚠ 0x4000 IS THE IDENTITY, NOT A GUESS AT 0x80103A90. The shared step is
        /// `speed x timescale >> 14`, so at 0x4000 the displacement IS the speed — which is exactly what
        /// this port's walk loop already does with its per-tick budget. Using anything else here would
        /// make the wander's own step disagree with the walk that carries it out.</summary>
        public int Timescale => 0x4000;

        public (int X, int Y) Position(Visitor guest) => (Current.X, Current.Z);
        public void SetPosition(Visitor guest, int x, int y) { Current.X = x; Current.Z = y; }

        public WaypointPool Waypoints => _pool;
        public int WaypointHead(Visitor guest) => Current.WaypointHead;
        public void SetWaypointHead(Visitor guest, int head) => Current.WaypointHead = head;

        /// <summary>Type 2 and type 13 — plain path and path-over-queue — which is what the pathfinder's
        /// own `case 2: case 13:` treats as path. A QUEUE tile is not one: a wandering guest does not
        /// drift down somebody else's queue.</summary>
        public bool IsPath(int tileX, int tileY)
            => tileX >= 0 && tileY >= 0 && tileX < _map.Width && tileY < _map.Height
            && (_map[tileX, tileY].Type == TileType.Path || _map[tileX, tileY].Type == TileType.PathQueueOverlap);

        public int PathLinks(int tileX, int tileY)
            => tileX >= 0 && tileY >= 0 && tileX < _map.Width && tileY < _map.Height
             ? _map[tileX, tileY].Links : 0;

        /// <summary>READ (findings/visitor-rest.md): four rays in +x, +y, -x, -y order, distances 0
        /// through radius-1. ⚠ TIE ORDER IS NOT ESTABLISHED — the first ray to find one wins here, which
        /// is the order the rays are written in and not a measured preference.</summary>
        public bool TryFindPathInRing(Visitor guest, int radius, out TPW.Sim.MapTile tile)
        {
            int x = Current.X >> 8, y = Current.Z >> 8;
            int[] dx = { 1, 0, -1, 0 }, dy = { 0, 1, 0, -1 };
            for (int ray = 0; ray < 4; ray++)
                for (int d = 0; d < radius; d++)
                {
                    int nx = x + dx[ray] * d, ny = y + dy[ray] * d;
                    if (nx < 0 || ny < 0 || nx >= _map.Width || ny >= _map.Height) break;
                    if (IsPath(nx, ny)) { tile = new TPW.Sim.MapTile(nx, ny); return true; }
                }
            tile = default;
            return false;
        }

        public bool TryRequestPath(Visitor guest, int x, int y, int flags, int secondaryFlags)
            => _request(Current, x, y, flags, secondaryFlags);
    }
}
