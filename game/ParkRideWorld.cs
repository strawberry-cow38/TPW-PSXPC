using System;
using Godot;
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
        /// <summary>How many guests this attraction has served (A+0x14, the "Users" line on a toilet's
        /// card).</summary>
        public int Served;
        /// <summary>A feature's capacity byte and last-cleaned stamp, placed full (TPW.Sim.FeatureStock).
        /// Only a type-2 feature reads it; it is allocated for every runtime because the runtime does
        /// not know its type, and an unused full byte costs nothing.</summary>
        public FeatureStock Stock = new();
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

        /// <summary>Run <paramref name="body"/> with the cursor pointing at <paramref name="g"/>, and put
        /// it back afterwards.
        ///
        /// ⚠⚠ EVERY IQueueWorld/IShopWorld METHOD ON THIS CLASS IGNORES ITS `Visitor` PARAMETER and reads
        /// `_guest`, `_target` and `_runtime` instead. That is fine for the one guest being ticked and is
        /// a trap for anything that acts on a guest in a LIST — the world will happily answer about, and
        /// mutate, whoever the cursor last pointed at. Anything iterating guests goes through here.</summary>
        public void WithGuest(Guest g, Action<ParkRideWorld> body)
        {
            var sg = _guest; var st = _target; var sr = _runtime;
            SetGuest(g);
            try { body(this); }
            finally { _guest = sg; _target = st; _runtime = sr; }
        }

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

            readonly ParkRideWorld _queue;
            readonly IRandomSource _dice;

            public LoadAdapter(RideRuntime r, int capacity, Func<long> now, Func<int> days, Action<Guest> placeAtExit,
                               ParkRideWorld queue = null, IRandomSource dice = null)
            { _r = r; _capacity = capacity; _now = now; _days = days; _place = placeAtExit; _queue = queue; _dice = dice; }

            public long NowTick => _now();
            public int Riders => _r.Riders.Count;
            public int Capacity => _capacity;
            public int QueueCount => Math.Max(0, _r.Queue.Count - 1);
            public int TotalDays => _days();
            public Visitor QueueHead => _r.Queue.Count > 0 ? _r.Queue[0].V : null;

            public Action<Guest> PlaceAtDoor;

            public void BoardQueueHead()
            {
                var g = _r.Queue[0];
                _r.Queue.RemoveAt(0);
                _r.Riders.Add(g);
                // ⚠ MOVE IT TO THE DOOR BEFORE HIDING IT. The hide below is immediate and right, but it
                // hid the guest WHERE IT WAS STANDING — at the head of the queue, several tiles short of
                // the ride — so people popped out of existence beside the thing they were boarding.
                PlaceAtDoor?.Invoke(g);
                g.V.SetState((VisitorState)21);              // 21: loading, the ride owns it now
                // ⚠ HIDE IT HERE, NOT ON THE NEXT MOVE. A boarded guest is skipped by the walk loop,
                // so nothing would ever draw it again - it would simply stand frozen at the head of
                // the queue for the whole ride, which is exactly what it looked like.
                g.Hidden = true;
                if (g.Inst != null) g.Inst.Visible = false;
            }

                /// <summary>Message 6 to everyone still queued, through the queue's own handler.
            ///
            /// ⚠⚠ THE STAGGER IS NOT COSMETIC — IT IS WHAT UNSTICKS THE QUEUE. Setting state 19 directly
            /// leaves V+0x2C holding whatever the FIDGET timer last wrote (0..300 ticks, VisitorQueue's
            /// Wait), and Shuffle refuses to move a guest until that deadline passes. Message 6 overwrites
            /// it with 3 x rand(3) — at most six ticks — which is the whole difference between a queue
            /// that shuffles up and one that boards a group and then stops.
            ///
            /// MEASURED on map 203: worst genuine stall 236 ticks with the head sitting in ShuffleForward,
            /// going nowhere, inside the 0..300 the fidget roll can produce.
            ///
            /// ⭐ AND THE MESSAGE IS GATED ON STATE 18, which the hand-rolled version also skipped: a
            /// shuffle that reaches a guest already loading or already walking is dropped (0x8008FB98).
            /// Falls back to the old direct set only when no queue world was supplied.</summary>
            public void ShuffleTheRestForward()
            {
                foreach (var g in _r.Queue)
                {
                    if (_queue != null && _dice != null)
                    {
                        // ⚠⚠ MOVE THE CURSOR FIRST. Every IQueueWorld method on this class IGNORES the
                        // Visitor it is handed and acts on `_guest` — so sending a message on behalf of
                        // a guest that is not the cursor frees SOMEBODY ELSE'S waypoint chain. A guest
                        // whose chain vanishes mid-walk stops where it stands, which is exactly the
                        // "stuck for ever while later joiners get on" this shuffle was meant to cure.
                        _queue.WithGuest(g, w => VisitorQueue.OnMessage(g.V, w, QueueMessage.Shuffle,
                                                                       _dice.Next(3), (int)VisitorState.ShuffleForward));
                    }
                    else if (g.V.State == VisitorState.WaitingInQueue)
                        g.V.SetState(VisitorState.ShuffleForward);
                }
            }


            public void UnloadFirstRider()
            {
                UnloadRider(_r.Riders[0].V);
            }

            /// <summary>A coaster returns one particular train batch, in reverse boarding order.
            /// Reuse the ordinary exit transaction without unloading another train's first rider.</summary>
            public void UnloadRider(Visitor visitor)
            {
                var g = _r.Riders.Find(g => ReferenceEquals(g.V, visitor));
                if (g == null) throw new InvalidOperationException("Coaster passenger is absent from the ride list.");
                _r.Riders.Remove(g);
                g.Hidden = false;
                _place(g);                                    // puts it at the exit AND shows it again
                // ⚠⚠ THE QUEUE PURPOSE OUTLIVES THE QUEUE, AND IT FREEZES THE GUEST. A guest joins a queue
                // with purpose QueueWalk and nothing here cleared it, so it walked out of the ride's exit
                // still wearing it -- and the arrival handler promotes ANY guest whose purpose is QueueWalk
                // or QueueShuffle straight back to WaitingInQueue, a queue it is no longer in, with no
                // target and no route. Master caught it with the hover readout, which said so in one line:
                //   guest #7843: Wander for 40 ticks; purpose QueueWalk; no target ⚠ no route
                // It is off the ride and done with the queue, so it is Finished: go back to standing around.
                g.V.Purpose = Purpose.Finished;
                g.V.InQueue = false;
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
        /// <summary>Throw everyone off a ride and out of its queue — the game's message 10 (0x8009D8C4)
        /// to every rider and every queuer, sent when the ride breaks down or is demolished.
        ///
        /// ⚠⚠ THIS USED TO BE AN EMPTY METHOD. AttractionLifecycle calls EjectEveryone on entering
        /// BrokenDown, and on AboutToBreakDown when there is no life left — and the port did nothing at
        /// all. The riders stayed in the Riders list, HIDDEN, which is how a guest stops being drawn for
        /// good; the queue stayed in the list with its states intact; and the ride's status is then
        /// neither Loading nor Unloading, so RideLoading never runs and none of it can resolve itself.
        /// A broken ride quietly swallowed everybody standing at it.
        ///
        /// ⚠ THE CURSOR IS SHARED. AppendToQueue and LeaveQueueList act on `_guest`, not on the Visitor
        /// they are handed, so every message here has to move the cursor first and put it back after.</summary>
        /// <summary>--park-log-rides: say what the ride machinery does rather than only its result.</summary>
        public bool Log;

        public void EjectAll(int entry, Action<Guest> placeAtExit)
        {
            if (!_rides.TryGetValue(entry, out var r)) return;
            var savedGuest = _guest; var savedTarget = _target; var savedRuntime = _runtime;
            int riders = r.R.Riders.Count, queued = r.R.Queue.Count;
            _target = r.T; _runtime = r.R;
            foreach (var g in r.R.Riders.ToArray())
            {
                g.Hidden = false;
                if (g.Inst != null) g.Inst.Visible = true;
                placeAtExit?.Invoke(g);
                _guest = g;
                VisitorQueue.OnMessage(g.V, this, QueueMessage.Ejected);
            }
            r.R.Riders.Clear();
            foreach (var g in r.R.Queue.ToArray())
            {
                _guest = g;
                VisitorQueue.OnMessage(g.V, this, QueueMessage.Ejected);
            }
            r.R.Queue.Clear();
            _guest = savedGuest; _target = savedTarget; _runtime = savedRuntime;
            // ⭐ SAY WHAT IT ACTUALLY DID. "Queue empty and nobody hidden" after a breakdown is equally
            // true of an eject that ran and of an eject that never needed to, so the counts are the only
            // thing that separates a working path from the empty method this replaced.
            if (Log) GD.Print($"[tpw] eject {entry}: {riders} thrown off, {queued} sent out of the queue");
        }

        public void AppendToQueue(Visitor g) { if (_runtime != null && !_runtime.Queue.Contains(_guest)) _runtime.Queue.Add(_guest); }
        public void LeaveQueueList(Visitor g) => _runtime?.Queue.Remove(_guest);
        /// <summary>Raised by the queue's boredom branch, not by the list removal -- see
        /// <see cref="VisitorQueue.Wait"/> for why the two are not the same guests.</summary>
        public Action<int, int> Advisor;
        public void AdvisorEvent(int index, int amount) => Advisor?.Invoke(index, amount);

        public bool TryPathToSlot(Visitor g, int x, int y) => _pathToTile(_guest, x >> 8, y >> 8);
        public bool TrySetSingleWaypoint(Visitor g, int x, int y) => _setSingleWaypoint(_guest, x, y);
        public void FreeWaypoints(Visitor g) => _freeWaypoints(_guest);

        public bool TargetHasEntrance(Visitor g) => _target != null;
        public bool TryPathToEntrance(Visitor g) => _target != null && _pathToTile(_guest, _target.DoorX, _target.DoorZ);
        /// <summary>⚠ THE RIDE'S LEAVE POINT IS ITS EXIT TILE and we do not track one yet, so a guest
        /// thrown out of a queue walks back to the door it came in by. Marked because it is a stand-in,
        /// not because the game does that.</summary>
        public bool TryPathToLeavePoint(Visitor g) => TryPathToEntrance(g);

        /// <summary>target+0x14 += 1 (0x80063164). ⚠ TWO COUNTERS, ONE CALL: the ride runtime counts
        /// rides given, and a STALL keeps its own tally of who bought — the shop line would read "0
        /// sales" beside real takings without this, which looks like the money came from nowhere.</summary>
        public void CountGuestServed(Visitor g)
        {
            if (_runtime != null) _runtime.Served++;
            _target?.Site?.CountServed();
        }

        // ---- the type-2 feature's stock (findings/shop-stock.md) ------------------------------------
        //
        // ⭐ A SHOP HAS NO STOCK TO WIRE. The three members below are the FEATURE's: the visitor's
        // type-2 arm is their only caller, and the type-4 arm never asks. A shop's object holds prices,
        // sliders and counters and nothing that runs out (shop-stock.md §2), so nothing here fakes a
        // level for one.
        //
        // ⚠ RUNTIMES ARE KEYED BY DEFINITION ENTRY, NOT BY PLACED INSTANCE (SetRides uses t.Id =
        // a.Rec.Entry), so two Small Toilets share one queue and now one capacity byte. That is a
        // pre-existing property of this world's id scheme and it is wrong for stock the same way it
        // was already wrong for queues; it is not widened here because the id reaches into the guest's
        // target and history. Flagged so it is fixed on purpose rather than found by a guest.

        /// <summary>Slot 54: the record's "guests may use it" flag, 0 for anything but a feature
        /// (GuestTargets already folds the type in). Not the byte -- an empty toilet still answers true,
        /// exactly as 0x80023F0C does.</summary>
        public bool TargetHasStock(Visitor g) => _target != null && _target.Usable != 0;
        /// <summary>0x800241E8: off the capacity byte, floored at zero.</summary>
        public void ConsumeStock(Visitor g, int units) => _runtime?.Stock.Subtract(units);
        /// <summary>0x800241BC: the capacity byte. ⚠ 100 for a target this world does not know, which
        /// is the placement value, not a stand-in -- a guest cannot reach the type-2 arm with no
        /// target, and the byte a feature starts with IS 100.</summary>
        public int StockLevel(Visitor g) => _runtime?.Stock.Level ?? FeatureStock.Full;

        // ⚠ SHOPS AND SIDESHOWS ARE NOT WIRED. The purchase routines are ported
        // (TPW.Sim.VisitorPurchase) and this world implements IShopWorld, but these two are the only
        // doors into it and both are empty, so a shop sells nothing and takes no money.
        public void BuyAtShop(Visitor g) { }
        public void PlaySideShow(Visitor g) { }

        // ⚠ THESE THROW ON PURPOSE. IShopWorld's shop half needs real record bytes, real panel sliders
        // and the real bank; every one of them has a plausible-looking neutral value (price 0, quality
        // 0, an empty product) and every one of those would make the purchase code agree with itself
        // and sell things for nothing. Nothing reaches them while the two doors above are empty, so a
        // throw costs nothing today and names the gap loudly the moment someone opens one.
        static Exception NotWired([System.Runtime.CompilerServices.CallerMemberName] string member = null)
            => new NotSupportedException($"IShopWorld.{member}: the park has no shop wiring yet. "
                                       + "Wire it rather than giving this a default - see ParkRideWorld.");

        /// <summary>The stall the current guest is standing at. ⭐ EVERY MEMBER BELOW GOES THROUGH HERE
        /// and throws on a null rather than answering zero: a shop that sells for nothing, or takes
        /// money into no bank, is exactly the kind of wrong that looks like it works.</summary>
        IShopSite Site => _target?.Site ?? throw NotWired();

        public ShopProduct Product(Visitor g) => Site.Product ?? throw NotWired();
        public int SalePrice(Visitor g) => Site.SalePrice;
        public int QualitySlider(Visitor g) => Site.QualitySlider;
        public int SecondSlider(Visitor g) => Site.SecondSlider;
        public void BookSale(Visitor g, ShopSale sale) => Site.BookSale(sale);
        public SideShowGame Game(Visitor g) => Site.Game ?? throw NotWired();
        public void BookPlay(Visitor g, SideShowPlay play) => Site.BookPlay(play);
        public void RecordSatisfaction(Visitor g, int amount) => Site.RecordSatisfaction(amount);

        /// <summary>⚠ DELIBERATELY A NO-OP, NOT A THROW. The event goes to the message-box state
        /// machine (0x800139B4 on 0x8010265C) and what each id means to it is NOT TRACED — so there is
        /// nothing to wire it to yet, and dropping it changes no money and no guest.</summary>
        /// <summary>⚠ THIS WAS `{ }`. It carries thirteen of the advisor's twenty counters -- balloon,
        /// costume and gift (5/6/7), the four shop verdict groups and the sideshow's (9..13) and the
        /// four satisfaction groups and the sideshow's (14..18) -- so an empty body here left every
        /// advisor rule about shops and sideshows unreachable. See GuestSpending's note for why the id
        /// is a counter index and not a message-box id.</summary>
        public void PostEvent(int id, int value) => Advisor?.Invoke(id, value);

        /// <summary>⚠ NO PROP POOL IN THE PORT, so a guest that buys a balloon never carries one. It is
        /// a drawing, not a rule: the purchase, the money and every stat effect have already happened by
        /// the time this is called, and the original treats a full pool the same way.</summary>
        public bool TrySpawnProp(Visitor g) => false;

        /// <summary>⚠ NO GUEST MODEL HANDLE IN THE PORT (V+0x24), so there is nothing to release.</summary>
        public void ReleaseModel(Visitor g) { }
    }
}
