using System;
using System.Collections.Generic;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>What a placed attraction needs to remember about its guests, kept apart from the
    /// rendering so the ride's queue is not tangled up with its model.</summary>
    sealed class RideRuntime
    {
        /// <summary>The guests in the queue, front first. The game keeps this list at ride+0xC4 and
        /// every guest's slot is worked out from its POSITION in it, so the order is the data.</summary>
        public readonly List<Guest> Queue = new();
        /// <summary>The guests on board.</summary>
        public readonly List<Guest> Riders = new();
        /// <summary>The queue tiles walked out from the entrance, nearest the ride first. Rebuilt when
        /// the map changes, because the player can lay more queue at any time.</summary>
        public List<QueueTile> Path = new();
        /// <summary>How many guests this attraction has served.</summary>
        public int Served;
    }

    /// <summary>The park as the queue-and-ride chain reads it (behaviour.md §2.4).
    ///
    /// ⭐ THE QUEUE IS THE MAP, NOT A LIST THE TOOL SAVED. The game walks the ride's queue path off the
    /// tiles themselves, so laying more queue lengthens it immediately and demolishing some shortens
    /// it. Deriving it the same way means the two can never drift, and it is why nothing here had to be
    /// added to the queue TOOL.
    ///
    /// ⚠ ONE INSTANCE SERVES EVERY GUEST, and almost every member takes the guest whose turn it is.
    /// <see cref="SetGuest"/> picks the ride out of the guest's target before the sim calls anything,
    /// exactly as <see cref="GuestBrain"/> does for the decision.</summary>
    sealed class ParkRideWorld : IQueueWorld
    {
        readonly ParkMap _map;
        readonly Func<long> _now;
        readonly Func<Guest, int, int, bool> _pathToTile;
        readonly Func<Guest, int> _freeWaypoints;
        readonly Func<Guest, int, int, bool> _setSingleWaypoint;
        readonly Dictionary<int, (GuestTarget T, RideRuntime R)> _rides = new();

        Guest _guest;
        GuestTarget _target;
        RideRuntime _runtime;

        public ParkRideWorld(ParkMap map, Func<long> now,
                             Func<Guest, int, int, bool> pathToTile,
                             Func<Guest, int, int, bool> setSingleWaypoint,
                             Func<Guest, int> freeWaypoints)
        {
            _map = map; _now = now; _pathToTile = pathToTile;
            _setSingleWaypoint = setSingleWaypoint; _freeWaypoints = freeWaypoints;
        }

        /// <summary>Refresh the rides this world knows about. Called when something is built.</summary>
        public void SetRides(IReadOnlyList<GuestTarget> targets)
        {
            foreach (var t in targets)
            {
                bool isNew = !_rides.TryGetValue(t.Id, out var existing);
                var runtime = isNew ? new RideRuntime() : existing.R;
                _rides[t.Id] = (t, runtime);
                // ⚠ WALK THE QUEUE ONLY WHEN IT CAN HAVE CHANGED. Re-walking every ride every tick is
                // affordable now and would not be with a park full of them, and a queue only changes
                // when somebody lays or demolishes one.
                if (isNew || _pathsDirty) runtime.Path = WalkQueue(t);
            }
            _pathsDirty = false;
        }

        bool _pathsDirty = true;

        /// <summary>The map changed under the queues; re-walk them on the next refresh.</summary>
        public void QueuesChanged() => _pathsDirty = true;

        /// <summary>Point the world at the guest whose turn it is.</summary>
        public bool SetGuest(Guest g)
        {
            _guest = g;
            if (_rides.TryGetValue(g.V.ChosenId, out var r)) { _target = r.T; _runtime = r.R; return true; }
            _target = null; _runtime = null;
            return false;
        }

        public RideRuntime Runtime => _runtime;
        public GuestTarget Target => _target;

        /// <summary>Totals across every ride, for a caption on a capture.</summary>
        public (int Queued, int Riding, int Served) Totals()
        {
            int q = 0, r = 0, s = 0;
            foreach (var kv in _rides) { q += kv.Value.R.Queue.Count; r += kv.Value.R.Riders.Count; s += kv.Value.R.Served; }
            return (q, r, s);
        }

        /// <summary>The runtime for an attraction by id, or null when it has none yet.</summary>
        public RideRuntime RuntimeFor(int id) => _rides.TryGetValue(id, out var r) ? r.R : null;

        /// <summary>One placed ride as TPW.Sim.RideLoading reads it.
        ///
        /// ⭐ THE SAME TWO LISTS the guests' queue states use. That is the point of putting the runtime
        /// here rather than on the view: the ride pulling a guest off the queue and the guest standing
        /// in it are two views of one list, and a port that kept them apart would have to sync them.</summary>
        public sealed class LoadAdapter : IRideLoadWorld
        {
            readonly RideRuntime _r;
            readonly Func<long> _now;
            readonly Func<int> _days;
            readonly Action<Guest> _place;
            readonly int _capacity;

            public LoadAdapter(RideRuntime r, int capacity, Func<long> now, Func<int> days, Action<Guest> placeAtExit)
            { _r = r; _capacity = capacity; _now = now; _days = days; _place = placeAtExit; }

            public long NowTick => _now();
            public int Riders => _r.Riders.Count;
            public int Capacity => _capacity;
            public int QueueCount => Math.Max(0, _r.Queue.Count - 1);
            public int TotalDays => _days();
            public Visitor QueueHead => _r.Queue.Count > 0 ? _r.Queue[0].V : null;

            public void BoardQueueHead()
            {
                var g = _r.Queue[0];
                _r.Queue.RemoveAt(0);
                _r.Riders.Add(g);
                g.V.SetState((VisitorState)21);              // 21: loading, the ride owns it now
                // ⚠ HIDE IT HERE, NOT ON THE NEXT MOVE. A boarded guest is skipped by the walk loop,
                // so nothing would ever draw it again - it would simply stand frozen at the head of
                // the queue for the whole ride, which is exactly what it looked like.
                g.Hidden = true;
                if (g.Inst != null) g.Inst.Visible = false;
            }

            /// <summary>Message 6 to everyone still queued. ⚠ The real one carries a per-guest
            /// `rand(3)` stagger; here they are simply told to shuffle, and the stagger that matters
            /// most - the per-guest decision phase - is already in the sim.</summary>
            public void ShuffleTheRestForward()
            {
                foreach (var g in _r.Queue)
                    if (g.V.State == VisitorState.WaitingInQueue) g.V.SetState(VisitorState.ShuffleForward);
            }

            public void UnloadFirstRider()
            {
                var g = _r.Riders[0];
                _r.Riders.RemoveAt(0);
                g.Hidden = false;
                _place(g);                                    // puts it at the exit AND shows it again
                g.V.SetState((VisitorState)22);              // 22: unloading, which applies the ride's effect
                _r.Served++;
            }
        }

        /// <summary>The tiles a guest queues on, from the ride's door outward.
        ///
        /// ⚠ IT WALKS, IT DOES NOT FLOOD. A queue is a line, and following the link bits one step at a
        /// time is what keeps it one - a flood fill would swallow the whole path network the moment the
        /// queue touched it, and every guest in the park would think it was in the queue.</summary>
        List<QueueTile> WalkQueue(GuestTarget t)
        {
            var path = new List<QueueTile>();
            int x = t.DoorX, z = t.DoorZ;
            if (x < 0 || z < 0 || x >= _map.Width || z >= _map.Height) return path;

            var seen = new HashSet<(int, int)>();
            while (seen.Add((x, z)))
            {
                int type = _map[x, z].Raw0;
                if (type != (int)TileType.QueuePath && type != (int)TileType.PathQueueOverlap) break;
                path.Add(new QueueTile(x, z));

                int links = _map[x, z].Links;
                bool stepped = false;
                foreach (var (dx, dz, bit) in Steps)
                {
                    if ((links & bit) == 0) continue;
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= _map.Width || nz >= _map.Height) continue;
                    if (seen.Contains((nx, nz))) continue;
                    int nt = _map[nx, nz].Raw0;
                    if (nt != (int)TileType.QueuePath && nt != (int)TileType.PathQueueOverlap) continue;
                    x = nx; z = nz; stepped = true; break;
                }
                if (!stepped) break;
            }
            return path;
        }

        /// <summary>(dx, dz, link bit) for the four directions, in the pathfinder's own order.</summary>
        static readonly (int dx, int dz, int bit)[] Steps =
            { (1, 0, 0x04), (0, 1, 0x10), (-1, 0, 0x40), (0, -1, 0x01) };

        // ---- IQueueWorld -------------------------------------------------------------------------

        public long NowTick => _now();
        public int TargetType(Visitor g) => _target?.TypeIndex ?? 0;
        public AttractionStatus RideStatus(Visitor g) => _target != null && _target.Open
            ? AttractionStatus.Running : AttractionStatus.ClosedByPlayer;
        public int QueueCount(Visitor g) => _runtime?.Queue.Count ?? 0;
        /// <summary>⚠ ALWAYS 0: upgrades are not wired, so every ride is at level 0. The queue cap is
        /// `4 x level + 7`, so this makes every queue hold seven - the level-0 cap, not an invention.</summary>
        public int UpgradeLevel(Visitor g) => 0;
        public int Intensity(Visitor g) => _target?.Intensity ?? 0;
        public QueueTile QueueOrigin(Visitor g) => new(_target?.DoorX ?? 0, _target?.DoorZ ?? 0);
        public IReadOnlyList<QueueTile> QueuePath(Visitor g) => (IReadOnlyList<QueueTile>)_runtime?.Path
                                                             ?? Array.Empty<QueueTile>();

        public int QueueIndexOf(Visitor g) => _runtime?.Queue.IndexOf(_guest) ?? -1;
        public void AppendToQueue(Visitor g) { if (_runtime != null && !_runtime.Queue.Contains(_guest)) _runtime.Queue.Add(_guest); }
        public void LeaveQueueList(Visitor g) => _runtime?.Queue.Remove(_guest);

        public bool TryPathToSlot(Visitor g, int x, int y) => _pathToTile(_guest, x >> 8, y >> 8);
        public bool TrySetSingleWaypoint(Visitor g, int x, int y) => _setSingleWaypoint(_guest, x, y);
        public void FreeWaypoints(Visitor g) => _freeWaypoints(_guest);

        public bool TargetHasEntrance(Visitor g) => _target != null;
        public bool TryPathToEntrance(Visitor g) => _target != null && _pathToTile(_guest, _target.DoorX, _target.DoorZ);
        /// <summary>⚠ THE RIDE'S LEAVE POINT IS ITS EXIT TILE and we do not track one yet, so a guest
        /// thrown out of a queue walks back to the door it came in by. Marked because it is a stand-in,
        /// not because the game does that.</summary>
        public bool TryPathToLeavePoint(Visitor g) => TryPathToEntrance(g);

        public void CountGuestServed(Visitor g) { if (_runtime != null) _runtime.Served++; }

        // ⚠ SHOPS ARE NOT WIRED. fable is porting the purchase routines (§2.5); until that lands a
        // shop has infinite stock, sells nothing and takes no money. Every one of these is a stub and
        // says so, rather than quietly returning a plausible number.
        public bool TargetHasStock(Visitor g) => true;
        public void ConsumeStock(Visitor g, int units) { }
        public int StockLevel(Visitor g) => 100;
        public void BuyAtShop(Visitor g) { }
        public void PlaySideShow(Visitor g) { }
    }
}
