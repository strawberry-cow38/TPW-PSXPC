using System;
using System.Collections.Generic;
using System.Linq;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>The park as a guest arriving or leaving reads it (TPW.Sim.VisitorEntrance, behaviour.md
    /// §2.6): the two turnstile lanes, the gate's crossing points, the fee and the bank.
    ///
    /// ⭐⭐ THIS IS WHERE THE PARK'S MONEY COMES FROM. A ride in this game charges NOTHING — economy.md
    /// §4.7 is explicit that no ride class ever calls GetBank — so admission and the shops are the whole
    /// income. Until this was wired a guest appeared inside the park for free and the bank only ever
    /// went down.</summary>
    sealed class ParkEntranceWorld : IVisitorMessageWorld
    {
        readonly ParkMap _map;
        readonly Func<long> _now;
        readonly Func<Guest, int, int, int, bool> _pathTo;      // guest, tile x, tile z, flags
        readonly Func<Guest, int, int, bool> _setWaypoint;      // guest, 8.8 x, 8.8 y
        readonly Func<Guest, int> _freeChain;
        readonly Func<IEnumerable<Guest>> _guests;
        readonly Func<IEnumerable<StaffMember>> _staff;
        readonly Func<Visitor, Guest> _byVisitor;
        readonly Func<IReadOnlyList<GuestTarget>> _targets;
        readonly Action<Guest> _remove;
        readonly ParkFinances _finances;
        readonly Func<BusRoute> _bus;
        readonly IRandomSource _dice;

        public ParkEntranceWorld(ParkMap map, Func<long> now, ParkFinances finances, Func<BusRoute> bus,
                                 IRandomSource dice,
                                 Func<Guest, int, int, int, bool> pathToTile,
                                 Func<Guest, int, int, bool> setWaypoint,
                                 Func<Guest, int> freeChain,
                                 Func<IEnumerable<Guest>> guests, Func<IEnumerable<StaffMember>> staff,
                                 Func<Visitor, Guest> byVisitor,
                                 Func<IReadOnlyList<GuestTarget>> targets, Action<Guest> remove)
        {
            _map = map; _now = now; _finances = finances; _bus = bus; _dice = dice;
            _pathTo = pathToTile; _setWaypoint = setWaypoint; _freeChain = freeChain;
            _guests = guests; _staff = staff; _byVisitor = byVisitor; _targets = targets; _remove = remove;
        }

        public long NowTick => _now();

        public (int X, int Y) Position(Visitor guest)
        {
            var g = _byVisitor(guest);
            return g == null ? (0, 0) : (g.X, g.Z);
        }

        // ── the two turnstile lanes ────────────────────────────────────────────────────────────────

        /// <summary>The entrance building for a lane, out of the map's own build list.
        ///
        /// ⭐ THE "BUILDING TABLE" IS THE MAP'S BUILD LIST. 0x8005439C takes its count and records from
        /// gp+0x12D8/0x12DC, which is the pair the map loader fills at 0x80054584 — the same 12-byte
        /// records ParkMap already parses as Scenery. The entry "flagged 0x04000000" is
        /// SceneryPlacement.Flags bit 2, and the tile is its X and Z (the halfwords at +4 and +8).
        ///
        /// ⚠ LANE 0 IS THE SMALLER X. The interface's own comment had the argument mapping the other way
        /// round; see the note there for the call site that settles it. Map 203 gives (19,13) for lane 0
        /// and (23,14) for lane 1, straddling the gate arch's centre line at x 21.0.
        ///
        /// ⚠ THE ORIGINAL DOES NOT NULL-TEST THIS (0x800591B0 branches on the lane, not the result), so a
        /// park with no entrance building reads through address 4. This throws instead of reproducing a
        /// wild read, and says which map is missing one.</summary>
        public TPW.Sim.MapTile EntranceTile(int lane)
        {
            var found = Entrances();
            if (found.Count == 0)
                throw new InvalidOperationException(
                    "this map has no build-list entry flagged 0x04000000, so it has no turnstile. The original "
                  + "does not check and reads through address 4; refusing rather than reproducing that.");
            var p = found[lane == 0 ? 0 : found.Count - 1];
            return new TPW.Sim.MapTile(p.X, p.Z);
        }

        List<SceneryPlacement> Entrances()
            => _entrances ??= _map.Scenery.Where(p => (p.Flags & 0x04) != 0).OrderBy(p => p.X).ToList();
        List<SceneryPlacement> _entrances;

        readonly List<Visitor>[] _lane = { new(), new() };
        readonly int[] _laneCount = new int[2];

        public IReadOnlyList<Visitor> Lane(int lane) => _lane[lane & 1];
        public void AppendToLane(int lane, Visitor guest) => _lane[lane & 1].Add(guest);
        public void RemoveFromLane(int lane, Visitor guest) => _lane[lane & 1].Remove(guest);

        /// <summary>⚠ NOT THE LIST'S LENGTH, and the interface says so: a separate word, +1 on append and
        /// -1 on admission, and it is what arrival 16 tests against 11 when picking a lane. Backing it
        /// with Lane(l).Count would be the same number nearly always and wrong exactly when it matters.</summary>
        public int LaneCount(int lane) => _laneCount[lane & 1];
        public void SetLaneCount(int lane, int value) => _laneCount[lane & 1] = value;

        public int Counter80103950 { get; set; }
        public int Counter80103954 { get; set; }

        public int GateBatch { get => _bus()?.Batch ?? 0; set { if (_bus() is { } b) b.Batch = value; } }
        public int BusPhase => _bus()?.Phase ?? 0;

        // ── walking ───────────────────────────────────────────────────────────────────────────────

        /// <summary>The gate's crossing points (TPW.Sim.ParkPoints), with the original's two dice.
        /// ⚠ THE X ROLL COMES FIRST; a host keeping the original's random stream must draw them in that
        /// order, which is why the aim is taken from ParkPoints rather than rolled here.</summary>
        public bool TryPathToParkPoint(Visitor guest, int pointIndex, int flags, int secondaryFlags)
        {
            var g = _byVisitor(guest);
            if (g == null) return false;
            var (x, z) = ParkPoints.Aim(pointIndex, _dice);
            return _pathTo(g, x >> 8, z >> 8, flags);
        }

        /// <summary>The map's exit points: the table the loader fills after the build list, which ParkMap
        /// parses as <see cref="ParkMap.SpawnTiles"/>. Map 203 has two, (18,5) and (23,5) — the same
        /// tile the bus puts its load down on.</summary>
        public int ExitCount => _map.SpawnTiles.Count;

        public bool TryPathToExit(Visitor guest, int exitIndex, int flags, int secondaryFlags)
        {
            var g = _byVisitor(guest);
            if (g == null || exitIndex < 0 || exitIndex >= _map.SpawnTiles.Count) return false;
            var (x, z) = _map.SpawnTiles[exitIndex];
            return _pathTo(g, x, z, flags);
        }

        public bool TryPathToLaneSlot(Visitor guest, int x, int y)
        {
            var g = _byVisitor(guest);
            return g != null && _pathTo(g, x >> 8, y >> 8, VisitorEntrance.LaneSlotPathFlags);
        }

        public bool TrySetSingleWaypoint(Visitor guest, int x, int y)
        {
            var g = _byVisitor(guest);
            return g != null && _setWaypoint(g, x, y);
        }

        /// <summary>State 47's waypoint: one waypoint at (my x, the y of the park point).
        /// ⚠ THE ALLOCATION HAPPENS BEFORE THE LOOKUP in the original, so a failed one rolls no dice.
        /// Reproduced by asking ParkPoints for the aim only after the waypoint is known to be available —
        /// which here means asking the pool first.</summary>
        public bool TrySetGateWaypoint(Visitor guest, int pointIndex)
        {
            var g = _byVisitor(guest);
            if (g == null) return false;
            var (_, z) = ParkPoints.Aim(pointIndex, _dice);
            return _setWaypoint(g, g.X, z);
        }

        public void FreeWaypoints(Visitor guest)
        {
            var g = _byVisitor(guest);
            if (g != null) _freeChain(g);
        }

        public bool TileHasFlag8(int x, int y)
            => x >= 0 && y >= 0 && x < _map.Width && y < _map.Height && (_map[x, y].Flags & 0x08) != 0;

        public bool TileIsPath(int x, int y)
            => x >= 0 && y >= 0 && x < _map.Width && y < _map.Height
            && (_map[x, y].Type == TileType.Path || _map[x, y].Type == TileType.PathQueueOverlap);

        // ── the money ─────────────────────────────────────────────────────────────────────────────

        /// <summary>BANK+0, the player-set admission price.
        ///
        /// ⚠ THE PANEL THAT SETS IT DOES NOT EXIST, so the park charges the bank's own constructed
        /// default of £40 (ParkEconomy.DefaultEntryFee) for ever. That is the value a park genuinely
        /// starts with rather than a number invented here — but a player who would have raised or
        /// dropped it cannot, so every admission figure this port produces is the £40 one.</summary>
        public Money EntryFee { get; set; } = ParkEconomy.DefaultEntryFee;

        /// <summary>0x80087258: balance += fee, and the takings and the fee history bucket with it.
        /// ⚠ ONLY THE BALANCE IS WIRED. The two history words are economy.md §4.1's and the port has
        /// nowhere to put them yet, so the month-end report cannot break income down by source.</summary>
        /// <summary>⭐ AND REMEMBERED AS ENTRANCE MONEY. The bank only learns the balance went up; the
        /// park's yearly figures separate the gate from the shops from the sideshows, and that split
        /// exists nowhere else.</summary>
        public void BookEntryFee(Visitor guest)
        {
            _finances.Bank.Receive(EntryFee);
            Score?.RecordIncome(EntryFee, ScoreIncome.Entrance);
        }

        /// <summary>The park's books beyond the balance. Set by the view.</summary>
        public ParkScore Score;

        /// <summary>McAi+0x1C += 1. ⚠ READ that it increments; "admissions" is a GUESS, so the port
        /// counts it under the same doubt rather than giving it a confident name.</summary>
        public int Counter_McAi1C { get; internal set; }
        public void CountAdmission() => Counter_McAi1C++;

        /// <summary>What the fee verdict sums. ⭐ NOT EVERY ATTRACTION COUNTS. The game asks each object's
        /// class for a value through a virtual getter at record+0x1A8/0x1AC, and two classes answer with a
        /// literal zero: **Shop (type 4) and Feature (type 2) both point at 0x80066110**, which is
        /// `jr ra; move v0, zero`. Rides (1, 3, 6, 7) and SideShow (5) each run a real computation. So a park
        /// full of shops and benches is worth nothing at the gate, and the port was counting all of them.
        ///
        /// ⚠ STILL WRONG, JUST LESS SO: a ride's real contribution is computed (0x800A0594 → 0x800A0B1C for a
        /// flat ride, and a different routine per class), not the record's base intensity this hands over.
        /// Excluding the two zero classes is measured; the value for the rest is not.</summary>
        public IEnumerable<int> AttractionIntensities
            => _targets().Where(t => t.TypeIndex != 4 && t.TypeIndex != 2).Select(t => t.Intensity);

        // ── the park's lists ──────────────────────────────────────────────────────────────────────

        public IEnumerable<Visitor> Guests => _guests().Select(g => g.V);
        public IEnumerable<StaffMember> Staff => _staff();

        public void RemoveFromPark(Visitor guest)
        {
            var g = _byVisitor(guest);
            if (g != null) _remove(g);
        }

        /// <summary>⚠ SYNCHRONOUS, as it is in the original: the message runs inside the caller's tick
        /// rather than being queued. VisitorMessages.OnMessage is the guest's slot 40.</summary>
        public void DeliverMessage(Visitor guest, int id, int param1, int param2)
        {
            VisitorMessages.OnMessage(guest, this, (VisitorMessage)id, _dice);
        }
        Func<IQueueWorld> _queue = () => null;
        public void SetQueueWorld(Func<IQueueWorld> q) => _queue = q ?? (() => null);

        /// <summary>⚠ NOT WIRED: a guard waiting in state 46 gets message 9 like a guest, and no guard
        /// in the port ever reaches 46 because nothing posts them to the gate. Named rather than
        /// silently empty, because "the guards never came" is a symptom somebody will chase.</summary>
        public void DeliverStaffMessage(StaffMember staff, int id) { }

        // ── the QUEUE half of IVisitorMessageWorld ────────────────────────────────────────────────
        //
        // ⭐ ONE OBJECT, TWO CONTRACTS, BECAUSE THE GAME HAS ONE. IVisitorMessageWorld is
        // IEntranceWorld AND IQueueWorld: the guest's slot 40 is a single vtable entry and a message
        // arriving at a guest may concern either the gate or a ride's queue. Splitting them into two
        // objects would mean a message handler that can only see half of the guest's situation, so the
        // entrance world forwards the queue half to the world that owns it rather than reimplementing
        // it. TrySetSingleWaypoint and FreeWaypoints are already ours and are NOT forwarded — both
        // contracts want the same thing and this side has it.
        public int TargetType(Visitor guest) => _queue() is { } q ? q.TargetType(guest) : default;
        public AttractionStatus RideStatus(Visitor guest) => _queue() is { } q ? q.RideStatus(guest) : default;
        public int QueueCount(Visitor guest) => _queue() is { } q ? q.QueueCount(guest) : default;
        public int UpgradeLevel(Visitor guest) => _queue() is { } q ? q.UpgradeLevel(guest) : default;
        public int Intensity(Visitor guest) => _queue() is { } q ? q.Intensity(guest) : default;
        public QueueTile QueueOrigin(Visitor guest) => _queue() is { } q ? q.QueueOrigin(guest) : default;
        public IReadOnlyList<QueueTile> QueuePath(Visitor guest) => _queue() is { } q ? q.QueuePath(guest) : default;
        public int QueueIndexOf(Visitor guest) => _queue() is { } q ? q.QueueIndexOf(guest) : default;
        public void AppendToQueue(Visitor guest) { _queue()?.AppendToQueue(guest); }
        public void LeaveQueueList(Visitor guest) { _queue()?.LeaveQueueList(guest); }
        public bool TryPathToSlot(Visitor guest, int x, int y) => _queue() is { } q ? q.TryPathToSlot(guest, x, y) : default;
        public bool TargetHasEntrance(Visitor guest) => _queue() is { } q ? q.TargetHasEntrance(guest) : default;
        public bool TryPathToEntrance(Visitor guest) => _queue() is { } q ? q.TryPathToEntrance(guest) : default;
        public bool TryPathToLeavePoint(Visitor guest) => _queue() is { } q ? q.TryPathToLeavePoint(guest) : default;
        public bool TargetHasStock(Visitor guest) => _queue() is { } q ? q.TargetHasStock(guest) : default;
        public void ConsumeStock(Visitor guest, int units) { _queue()?.ConsumeStock(guest, units); }
        public int StockLevel(Visitor guest) => _queue() is { } q ? q.StockLevel(guest) : default;


        // The SHOP half comes along with IQueueWorld, which extends IShopWorld. Forwarded for the same
        // reason: one slot 40, one world behind it. ⚠ Several of these THROW on the other side, on
        // purpose (ParkRideWorld's note): a shop's price and sliders have no plausible neutral value
        // that would not make the purchase code agree with itself and sell things for nothing.
        public void BookPlay(Visitor guest, SideShowPlay play) { _queue()?.BookPlay(guest, play); }
        public void BookSale(Visitor guest, ShopSale sale) { _queue()?.BookSale(guest, sale); }
        public void CountGuestServed(Visitor guest) { _queue()?.CountGuestServed(guest); }
        public SideShowGame Game(Visitor guest) => _queue() is { } q ? q.Game(guest) : default;
        public void PostEvent(int id, int value) { _queue()?.PostEvent(id, value); }
        public ShopProduct Product(Visitor guest) => _queue() is { } q ? q.Product(guest) : default;
        public int QualitySlider(Visitor guest) => _queue() is { } q ? q.QualitySlider(guest) : default;
        public void RecordSatisfaction(Visitor guest, int amount) { _queue()?.RecordSatisfaction(guest, amount); }
        public void ReleaseModel(Visitor guest) { _queue()?.ReleaseModel(guest); }
        public int SalePrice(Visitor guest) => _queue() is { } q ? q.SalePrice(guest) : default;
        public int SecondSlider(Visitor guest) => _queue() is { } q ? q.SecondSlider(guest) : default;
        public bool TrySpawnProp(Visitor guest) => _queue() is { } q ? q.TrySpawnProp(guest) : default;
    }
}
