using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>The queue-and-ride chain: states 41, 19, 18, 21, 22, 23 and 58 and messages 6, 7 and 10
    /// (behaviour.md §2.4 and §2.10, as re-read in §0 item 6), plus the arrival arms they lean on.</summary>
    public class VisitorQueueTests
    {
        /// <summary>Scripted dice that RECORD the bound of every roll, so dice order and the exact
        /// argument of a roll can be asserted as well as the outcome. Exhausted rolls return 1, which
        /// reads as "missed" for every threshold in this chain.</summary>
        sealed class Dice : IRandomSource
        {
            readonly Queue<int> _v;
            public Dice(params int[] v) => _v = new Queue<int>(v);
            public List<int> Bounds { get; } = new();
            public int Next(int n) { Bounds.Add(n); return _v.Count > 0 ? _v.Dequeue() : 1; }
        }

        sealed class World : IQueueWorld
        {
            public long NowTick { get; set; }
            public int Type { get; set; } = 3;
            public AttractionStatus Status { get; set; } = AttractionStatus.Running;
            public int Count { get; set; }
            public int Level { get; set; }
            public int IntensityValue { get; set; } = 50;
            public QueueTile Origin { get; set; } = new QueueTile(10, 10);
            public List<QueueTile> Path { get; set; } = new() { new(10, 11), new(10, 12), new(10, 13), new(10, 14) };
            public int Index { get; set; } = -1;
            public int Appended { get; private set; }
            public int LeftList { get; private set; }
            public bool SlotPathAccepted { get; set; } = true;
            public bool SlotPathAsked { get; private set; }
            public (int X, int Y) SlotPathDest { get; private set; }
            public bool WaypointAvailable { get; set; } = true;
            public bool WaypointSet { get; private set; }
            public (int X, int Y) Waypoint { get; private set; }
            public int Freed { get; private set; }
            public bool Entrance { get; set; } = true;
            public bool EntrancePathAccepted { get; set; } = true;
            public bool LeavePathAccepted { get; set; } = true;
            public int Served { get; private set; }
            public bool Stock { get; set; } = true;
            public int Consumed { get; private set; } = -1;
            public int StockLevelValue { get; set; } = 100;
            public int ShopBuys { get; private set; }
            public int SideShowPlays { get; private set; }
            // The shop half (IShopWorld): a Fries stall at £30, which the default Guest() buys, and an
            // Arcade, which it plays. VisitorPurchaseTests covers the routines; here they only need to run.
            public ShopProduct ProductValue { get; set; } = new(40, 7, 20, 0, 5, 10);
            public int Price { get; set; } = 30;
            public SideShowGame GameValue { get; set; } = new(10, 30, 25);

            public int TargetType(Visitor g) => Type;
            public AttractionStatus RideStatus(Visitor g) => Status;
            public int QueueCount(Visitor g) => Count;
            public int UpgradeLevel(Visitor g) => Level;
            public int Intensity(Visitor g) => IntensityValue;
            public QueueTile QueueOrigin(Visitor g) => Origin;
            public IReadOnlyList<QueueTile> QueuePath(Visitor g) => Path;
            public int QueueIndexOf(Visitor g) => Index;
            public void AppendToQueue(Visitor g) { Appended++; Index = Count; Count++; }
            public void LeaveQueueList(Visitor g) { LeftList++; if (Index >= 0) { Count--; Index = -1; } }
            public readonly System.Collections.Generic.List<(int, int)> Events = new();
            public void AdvisorEvent(int index, int amount) => Events.Add((index, amount));
            public bool TryPathToSlot(Visitor g, int x, int y) { SlotPathAsked = true; SlotPathDest = (x, y); return SlotPathAccepted; }
            public bool TrySetSingleWaypoint(Visitor g, int x, int y)
            {
                if (!WaypointAvailable) return false;
                WaypointSet = true; Waypoint = (x, y); return true;
            }
            public void FreeWaypoints(Visitor g) => Freed++;
            public bool TargetHasEntrance(Visitor g) => Entrance;
            public bool TryPathToEntrance(Visitor g) => EntrancePathAccepted;
            public bool TryPathToLeavePoint(Visitor g) => LeavePathAccepted;
            public void CountGuestServed(Visitor g) => Served++;
            public bool TargetHasStock(Visitor g) => Stock;
            public void ConsumeStock(Visitor g, int units) => Consumed = units;
            public int StockLevel(Visitor g) => StockLevelValue;
            public ShopProduct Product(Visitor g) => ProductValue;
            public int SalePrice(Visitor g) => Price;
            public int QualitySlider(Visitor g) => 50;
            public int SecondSlider(Visitor g) => 50;
            public void BookSale(Visitor g, ShopSale s) => ShopBuys++;
            public SideShowGame Game(Visitor g) => GameValue;
            public void BookPlay(Visitor g, SideShowPlay p) => SideShowPlays++;
            public void RecordSatisfaction(Visitor g, int amount) { }
            public void PostEvent(int id, int value) { }
            public bool TrySpawnProp(Visitor g) => true;
            public void ReleaseModel(Visitor g) { }
        }

        /// <summary>A guest with a target, in the given state, with every stat well clear of a threshold.
        /// Visitor type 0 has preference 90.</summary>
        static Visitor Guest(VisitorState state = VisitorState.JoiningQueue)
        {
            var v = Visitor.Spawn(new Dice(), 0);
            v.Happiness = 50; v.Tiredness = 40; v.Nausea = 10; v.Boredom = 20; v.RideDesire = 0;
            v.VisitorType = 0;
            v.WalkSpeed = 22; v.NormalWalkSpeed = 22;
            v.HasTarget = true;
            v.SetState(state);
            return v;
        }

        // ⚠ QUEUE SPEED IS EXACTLY 15, AND IT IS SLOWER THAN WALKING. A guest shuffles up a queue at
        // 15 whatever its own pace was, and gets its own pace back when the ride lets it off. Pinning
        // the NUMBER and not merely "it changed" - a test that only checks it differs from 22 passes
        // for any wrong value. REJECTS reusing the normal speed, and REJECTS 16.
        [Fact]
        public void JoiningAQueueDropsTheGuestToExactlyFifteen()
        {
            Assert.Equal(15, VisitorQueue.QueueWalkSpeed);

            var g = Guest(); var w = new World();
            Assert.Equal(JoinOutcome.Walking, VisitorQueue.JoinQueue(g, w));
            Assert.Equal(15, g.WalkSpeed);
            Assert.Equal(22, g.NormalWalkSpeed);      // the guest's own pace is remembered, not lost
        }

        // ⭐⭐ BOTH PERIODIC PASSES ARE STAGGERED PER GUEST, so a queue of forty does not all tick its
        // boredom on the same frame. REJECTS `(now & 3) == 0`, which is what §2.4's "every 4 ticks"
        // reads like and which makes a whole queue lose patience in lockstep. Two guests, same tick,
        // different staggers, and exactly one of them may act.
        [Fact]
        public void TheBrokenRidePassIsStaggeredPerGuestNotGlobal()
        {
            var w = new World { Status = AttractionStatus.BrokenDown, NowTick = 8 };

            var onBeat = Guest(VisitorState.WaitingInQueue);
            onBeat.DecisionStagger = 0; onBeat.Boredom = 20; onBeat.WaitUntil = long.MaxValue;
            VisitorQueue.Wait(onBeat, w, new Dice(99, 99));

            var offBeat = Guest(VisitorState.WaitingInQueue);
            offBeat.DecisionStagger = 1; offBeat.Boredom = 20; offBeat.WaitUntil = long.MaxValue;
            VisitorQueue.Wait(offBeat, w, new Dice(99, 99));

            Assert.Equal(21, onBeat.Boredom);          // 8 & 3 == 0 & 3: his tick
            Assert.Equal(20, offBeat.Boredom);         // 8 & 3 != 1 & 3: not his
        }

        // ⚠ THE BOREDOM ADDED THIS TICK COUNTS THIS TICK. A guest sitting on the threshold is pushed
        // over and leaves in the same pass, rather than standing there for one more. REJECTS testing
        // the threshold before the increment, which costs a tick and is invisible in any test that
        // does not start exactly ON the boundary.
        [Fact]
        public void TheGuestThatTipsOverTheThresholdLeavesOnThatSameTick()
        {
            var w = new World { Status = AttractionStatus.BrokenDown, NowTick = 8 };
            var g = Guest(VisitorState.WaitingInQueue);
            g.DecisionStagger = 0;
            g.Boredom = VisitorQueue.BoredomLeaveThreshold;      // exactly 80: not yet over
            g.WaitUntil = long.MaxValue;

            Assert.Equal(WaitOutcome.LeftBored, VisitorQueue.Wait(g, w, new Dice(99, 99)));
            Assert.Equal(81, g.Boredom);
            Assert.Equal(1, w.LeftList);

            // The control: one below, and the same tick leaves him standing at exactly the threshold.
            var patient = Guest(VisitorState.WaitingInQueue);
            patient.DecisionStagger = 0;
            patient.Boredom = VisitorQueue.BoredomLeaveThreshold - 1;
            patient.WaitUntil = long.MaxValue;
            Assert.Equal(WaitOutcome.Standing, VisitorQueue.Wait(patient, new World
            { Status = AttractionStatus.BrokenDown, NowTick = 8 }, new Dice(99, 99)));
            Assert.Equal(80, patient.Boredom);
        }

        static QueueTile T(int x, int y) => new(x, y);

        static List<QueueTile> Straight(int x, int y0, int dy, int n)
        {
            var l = new List<QueueTile>();
            for (int i = 0; i < n; i++) l.Add(T(x, y0 + dy * i));
            return l;
        }

        // ───────────────────────── the can-join test (0x8008DA14) ─────────────────────────

        // ⭐ ONLY FOUR TYPES HAVE A QUEUE. The walk-in types pass whatever the building's status; 1, 3, 6
        // and 7 must be open and under the cap. REJECTS gating every type, and REJECTS gating "everything
        // but type 2" -- 4 and 5 walk in too.
        [Theory]
        [InlineData(1, true)] [InlineData(3, true)] [InlineData(6, true)] [InlineData(7, true)]
        [InlineData(0, false)] [InlineData(2, false)] [InlineData(4, false)] [InlineData(5, false)] [InlineData(8, false)]
        public void OnlyTheFourQueuedTypesAreGated(int type, bool gated)
        {
            Assert.Equal(gated, VisitorQueue.HasQueue(type));
            var closed = new World { Type = type, Status = AttractionStatus.ClosedByPlayer };
            Assert.Equal(!gated, VisitorQueue.CanJoin(Guest(), closed));
        }

        // The open rule IS AttractionLifecycle.OpenToGuests, checked against every status both ways so a
        // second copy cannot drift. REJECTS admitting 7 (reopen) or refusing 11 (unloading).
        [Theory]
        [InlineData(AttractionStatus.JustPlaced, false)]
        [InlineData(AttractionStatus.UnderConstruction, false)]
        [InlineData(AttractionStatus.Running, true)]
        [InlineData(AttractionStatus.ClosedByPlayer, false)]
        [InlineData(AttractionStatus.AboutToBreakDown, false)]
        [InlineData(AttractionStatus.BrokenDown, false)]
        [InlineData(AttractionStatus.UnderRepair, false)]
        [InlineData(AttractionStatus.Reopen, false)]
        [InlineData(AttractionStatus.Dead8, false)]
        [InlineData(AttractionStatus.Dead9, false)]
        [InlineData(AttractionStatus.Loading, true)]
        [InlineData(AttractionStatus.Unloading, true)]
        public void OpenMeansExactlyRunningLoadingOrUnloading(AttractionStatus s, bool open)
        {
            Assert.Equal(open, VisitorQueue.CanJoin(Guest(), new World { Status = s }));
            Assert.Equal(open, AttractionLifecycle.OpenToGuests(s));
        }

        // ⭐ THE CAP IS 4 x LEVEL + 7 AND STRICT: 7 / 11 / 15, so the 8th guest at level 0 is refused.
        // REJECTS <=, REJECTS a flat 7, REJECTS 4 x (level + 1) + 3 (same at level 0, wrong after).
        [Theory]
        [InlineData(0, 6, true)] [InlineData(0, 7, false)]
        [InlineData(1, 10, true)] [InlineData(1, 11, false)]
        [InlineData(2, 14, true)] [InlineData(2, 15, false)]
        [InlineData(3, 18, true)] [InlineData(3, 19, false)]
        public void TheQueueCapIsFourPerLevelPlusSevenAndStrict(int level, int count, bool joins)
        {
            Assert.Equal(joins, VisitorQueue.CanJoin(Guest(), new World { Level = level, Count = count }));
        }

        // ⭐ BEING QUEUED BEATS THE CAP BUT NOT A CLOSED RIDE. REJECTS "in-queue passes everything" and
        // REJECTS ignoring the bit.
        [Fact]
        public void AQueuedGuestPassesTheCapButNotAClosedRide()
        {
            var queued = Guest(); queued.InQueue = true;
            Assert.True(VisitorQueue.CanJoin(queued, new World { Count = 50 }));
            Assert.False(VisitorQueue.CanJoin(queued, new World { Count = 0, Status = AttractionStatus.BrokenDown }));

            var stranger = Guest();
            Assert.False(VisitorQueue.CanJoin(stranger, new World { Count = 50 }));
        }

        // No target, no join -- even for a walk-in type. REJECTS testing the type before the pointer.
        [Fact]
        public void NoTargetCannotJoin()
        {
            var g = Guest(); g.HasTarget = false;
            Assert.False(VisitorQueue.CanJoin(g, new World { Type = 2 }));
        }

        // ───────────────────────── the slot (0x8009D57C) ─────────────────────────

        // ⭐ THE HEAD STANDS ON THE ORIGIN'S CENTRE AND EACH MEMBER AHEAD IS ONE QUARTER TILE further along
        // the path. REJECTS starting from path tile 0, REJECTS half or whole tiles, REJECTS counting
        // from the far end.
        [Theory]
        [InlineData(0, 2688, 2688)]
        [InlineData(1, 2688, 2752)]
        [InlineData(4, 2688, 2944)]     // exactly on path tile 0's centre
        [InlineData(13, 2688, 3520)]
        public void EachMemberAheadPushesTheSlotAQuarterTileAlongThePath(int ahead, int x, int y)
        {
            Assert.True(VisitorQueue.WalkSlot(T(10, 10), Straight(10, 11, +1, 4), ahead, out int sx, out int sy));
            Assert.Equal((x, y), (sx, sy));
        }

        // The direction turns at a tile's centre, toward the next tile. REJECTS carrying straight on.
        [Fact]
        public void TheSlotTurnsTowardTheNextTileAtATileCentre()
        {
            var bent = new List<QueueTile> { T(10, 11), T(11, 11), T(12, 11) };
            Assert.True(VisitorQueue.WalkSlot(T(10, 10), bent, 5, out int x, out int y));
            Assert.Equal((2752, 2944), (x, y));    // one quarter in +x from (10,11)'s centre
        }

        // ⚠ DO NOT FIX: two ways off the end, both READ, and together they are asymmetric. On a path laid
        // in +y the 14th member ahead lands on the last tile and fails; laid in -y the 15th does.
        // REJECTS any symmetric bound (the last tile's centre, or >= on the coordinate).
        [Theory]
        [InlineData(+1, 13, true)] [InlineData(+1, 14, false)]
        [InlineData(-1, 14, true)] [InlineData(-1, 15, false)]
        public void TheLastTileIsOutOfBoundsAndTheBoundIsAsymmetric(int dir, int ahead, bool fits)
        {
            var path = Straight(10, 10 + dir, dir, 4);
            Assert.Equal(fits, VisitorQueue.WalkSlot(T(10, 10), path, ahead, out _, out _));
        }

        // A one-tile path whose tile IS the origin holds only the head: the first step would advance past
        // the end. REJECTS clamping to the last tile and REJECTS walking on forever.
        [Fact]
        public void AdvancingPastTheEndOfThePathFails()
        {
            var one = new List<QueueTile> { T(10, 10) };
            Assert.True(VisitorQueue.WalkSlot(T(10, 10), one, 0, out _, out _));
            Assert.False(VisitorQueue.WalkSlot(T(10, 10), one, 1, out _, out _));
        }

        // ⭐ APPEND ONLY ON SUCCESS AND ONLY IF NOT ALREADY LISTED, and a member's slot counts the members
        // ahead of IT, not the whole list. REJECTS appending before the walk, REJECTS appending twice,
        // REJECTS using the count for a member, REJECTS a slot on an empty path.
        [Fact]
        public void TheSlotListsTheGuestOnceAndOnlyWhenASlotWasFound()
        {
            var g = Guest();

            var newcomer = new World { Count = 3, Index = -1 };
            Assert.True(VisitorQueue.TryQueueSlot(g, newcomer, out _, out int y));
            Assert.Equal(2688 + 3 * 64, y);
            Assert.Equal(1, newcomer.Appended);

            var member = new World { Count = 3, Index = 1 };
            Assert.True(VisitorQueue.TryQueueSlot(g, member, out _, out y));
            Assert.Equal(2688 + 64, y);
            Assert.Equal(0, member.Appended);

            var full = new World { Count = 14, Index = -1 };
            Assert.False(VisitorQueue.TryQueueSlot(g, full, out _, out _));
            Assert.Equal(0, full.Appended);

            var noPath = new World { Path = new List<QueueTile>() };
            Assert.False(VisitorQueue.TryQueueSlot(g, noPath, out _, out _));
            Assert.Equal(0, noPath.Appended);
        }

        // ───────────────────────── 41 join queue (0x8008F688) ─────────────────────────

        // ⭐ SET 11, NOT PUSH; and the guest is listed, flagged, given purpose 3, slowed and stamped.
        // REJECTS PushState (a stack depth of 1), REJECTS leaving the speed at its spawn value.
        [Fact]
        public void JoiningListsFlagsSlowsAndSetsElevenWithAnEmptyStack()
        {
            var g = Guest(); g.PushState(VisitorState.JoiningQueue);   // give it a stack to lose
            var world = new World { NowTick = 700 };
            Assert.Equal(JoinOutcome.Walking, VisitorQueue.JoinQueue(g, world));
            Assert.True(g.InQueue);
            Assert.Equal(1, world.Appended);
            Assert.Equal(Purpose.QueueWalk, g.Purpose);
            Assert.Equal((2688, 2688), world.SlotPathDest);
            Assert.Equal(VisitorQueue.QueueWalkSpeed, g.WalkSpeed);
            Assert.Equal(700, g.WaitUntil);
            Assert.Equal(VisitorState.WalkToBin, g.State);            // 11, waiting for the pathfinder
            Assert.Equal(0, g.StackDepth);
        }

        // ⚠ A REFUSED PATH IS NOT A FAILURE TO JOIN: the guest is already listed and flagged and stays in
        // 41 to ask again, not yet slowed. REJECTS treating refusal as "cannot join".
        [Fact]
        public void ARefusedPathLeavesTheGuestListedAndRetrying()
        {
            var g = Guest();
            var world = new World { SlotPathAccepted = false };
            Assert.Equal(JoinOutcome.PathRefused, VisitorQueue.JoinQueue(g, world));
            Assert.Equal(VisitorState.JoiningQueue, g.State);
            Assert.True(g.InQueue);
            Assert.Equal(1, world.Appended);
            Assert.True(g.HasTarget);
            Assert.Equal(Purpose.QueueWalk, g.Purpose);
            Assert.Equal(22, g.WalkSpeed);
        }

        // ⭐ TWO FAILURES, TWO DESTINATIONS. Not queued: drop the target, Idle. Already queued: out via
        // message 7 -- 58, target KEPT, in-queue cleared, bit 0x01 set, waypoints freed, and no SetState(0)
        // after it. REJECTS sending both to Idle, which is how §2.4's table reads.
        [Fact]
        public void CannotJoinGoesIdleUnlessAlreadyQueuedWhichGoesToFiftyEight()
        {
            var stranger = Guest();
            var closed = new World { Status = AttractionStatus.ClosedByPlayer };
            Assert.Equal(JoinOutcome.GaveUp, VisitorQueue.JoinQueue(stranger, closed));
            Assert.Equal(VisitorState.Idle, stranger.State);
            Assert.False(stranger.HasTarget);
            Assert.Equal(0, closed.LeftList);

            var queued = Guest(); queued.InQueue = true;
            var closed2 = new World { Status = AttractionStatus.ClosedByPlayer, Count = 2, Index = 1 };
            Assert.Equal(JoinOutcome.LeftQueue, VisitorQueue.JoinQueue(queued, closed2));
            Assert.Equal(VisitorState.RemovedFromQueue, queued.State);
            Assert.True(queued.HasTarget);
            Assert.False(queued.InQueue);
            Assert.True(queued.Flag1);
            Assert.Equal(1, closed2.LeftList);
            Assert.Equal(1, closed2.Freed);
        }

        // A slot failure after a passed can-join is the same fork, and the guest was never listed.
        // REJECTS "can-join passed, so append".
        [Fact]
        public void ASlotFailureIsTheSameForkAsACanJoinFailure()
        {
            var g = Guest();
            var full = new World { Count = 14, Index = -1, Level = 3 };   // cap 19; the path holds 13
            Assert.Equal(JoinOutcome.GaveUp, VisitorQueue.JoinQueue(g, full));
            Assert.Equal(VisitorState.Idle, g.State);
            Assert.False(g.InQueue);
            Assert.Equal(0, full.Appended);
        }

        // ───────────────────────── 19 shuffle forward (0x8008F508) ─────────────────────────

        // ⭐ STRICTLY AFTER THE DEADLINE, and then ONE waypoint without the pathfinder and state 3.
        // REJECTS >=, REJECTS asking the pathfinder, REJECTS Set 11.
        [Fact]
        public void ShufflingWaitsOutTheDeadlineThenSetsOneWaypointAndStepsToIt()
        {
            var g = Guest(VisitorState.ShuffleForward); g.InQueue = true; g.WaitUntil = 1000;
            var world = new World { NowTick = 1000, Count = 3, Index = 2 };
            Assert.Equal(ShuffleOutcome.Waiting, VisitorQueue.Shuffle(g, world));
            Assert.Equal(VisitorState.ShuffleForward, g.State);
            Assert.False(world.WaypointSet);

            world.NowTick = 1001;
            Assert.Equal(ShuffleOutcome.Stepping, VisitorQueue.Shuffle(g, world));
            Assert.Equal(1, world.Freed);
            Assert.Equal(Purpose.QueueShuffle, g.Purpose);
            Assert.Equal((2688, 2688 + 2 * 64), world.Waypoint);
            Assert.False(world.SlotPathAsked);
            Assert.Equal(VisitorState.WalkToWaypoint, g.State);
            Assert.Equal(0, world.Appended);
        }

        // An empty waypoint pool leaves the guest in 19 with purpose 10 and its waypoints already freed.
        // REJECTS going Idle, REJECTS freeing only after the check.
        [Fact]
        public void AnEmptyWaypointPoolKeepsTheGuestShuffling()
        {
            var g = Guest(VisitorState.ShuffleForward); g.InQueue = true; g.WaitUntil = 0;
            var world = new World { NowTick = 1, Count = 1, Index = 0, WaypointAvailable = false };
            Assert.Equal(ShuffleOutcome.NoWaypoint, VisitorQueue.Shuffle(g, world));
            Assert.Equal(VisitorState.ShuffleForward, g.State);
            Assert.Equal(Purpose.QueueShuffle, g.Purpose);
            Assert.Equal(1, world.Freed);
        }

        // Shuffle re-runs the can-join test, so a ride that broke since throws its queue out one by one as
        // each guest's turn to move comes. REJECTS skipping the test on a shuffle.
        [Fact]
        public void ShufflingAtARideThatBrokeLeavesTheQueue()
        {
            var g = Guest(VisitorState.ShuffleForward); g.InQueue = true; g.WaitUntil = 0;
            var world = new World { NowTick = 1, Count = 2, Index = 1, Status = AttractionStatus.BrokenDown };
            Assert.Equal(ShuffleOutcome.LeftQueue, VisitorQueue.Shuffle(g, world));
            Assert.Equal(VisitorState.RemovedFromQueue, g.State);
        }

        // ───────────────────────── 18 waiting in queue (0x800906EC) ─────────────────────────

        // ⭐ THE DIFFERENCE IS ABSOLUTE, THEN FLOORED AT 50, THEN HALVED, and the boredom die is rolled
        // against 100 - that. Asserted on the BOUND. REJECTS the one-sided `max(pref - intensity, 50)`
        // (75 for a ride far wilder than the guest likes), REJECTS dropping the floor (95 for a match).
        [Theory]
        [InlineData(0, 80, 75)]     // pref 90, 10 away: floored to 50, halved: rand(75)
        [InlineData(0, 90, 75)]     // a perfect match rolls the same die -- the floor
        [InlineData(1, 100, 65)]    // pref 30, 70 WILDER: |-70| -> 35 -> rand(65)
        [InlineData(0, 0, 55)]      // pref 90, 90 tamer -> 45 -> rand(55)
        [InlineData(4, 0, 50)]      // pref 100, the worst case -> rand(50)
        public void QueueBoredomRollsAgainstOneHundredMinusTheFlooredHalfDifference(int type, int intensity, int bound)
        {
            var g = Guest(VisitorState.WaitingInQueue); g.VisitorType = type; g.DecisionStagger = 0;
            g.WaitUntil = long.MaxValue;
            var dice = new Dice(99, 99);
            VisitorQueue.Wait(g, new World { NowTick = 8, IntensityValue = intensity }, dice);   // 8 & 7 == 0
            Assert.Equal(new[] { bound, 100 }, dice.Bounds);
            Assert.Equal(bound, 100 - VisitorQueue.BoredomDifference(VisitorTables.TypePreference[type], intensity));
        }

        // ⭐ BOTH PERIODIC PASSES ARE STAGGERED BY V+0x10. Off the guest's tick: no dice, no change.
        // REJECTS the bare `now % 8 == 0` and REJECTS running every tick.
        [Fact]
        public void TheEightTickPassOnlyRunsOnTheGuestsOwnStaggerTick()
        {
            var g = Guest(VisitorState.WaitingInQueue); g.DecisionStagger = 3; g.WaitUntil = long.MaxValue;
            var off = new Dice(0, 0);
            VisitorQueue.Wait(g, new World { NowTick = 8 }, off);          // 8 & 7 = 0, not 3
            Assert.Empty(off.Bounds);
            Assert.Equal(20, g.Boredom);
            Assert.Equal(40, g.Tiredness);

            var on = new Dice(0, 0);                                        // both rolls land
            VisitorQueue.Wait(g, new World { NowTick = 11 }, on);          // 11 & 7 = 3
            Assert.Equal(2, on.Bounds.Count);
            Assert.Equal(21, g.Boredom);
            Assert.Equal(39, g.Tiredness);
        }

        // The rolls are strict: rand < 2 bores, rand < 10 rests. REJECTS <= and REJECTS == 0.
        [Theory]
        [InlineData(1, 9, 21, 39)]
        [InlineData(2, 10, 20, 40)]
        public void TheBoredomAndTirednessRollsAreStrictlyBelow(int boredRoll, int restRoll, int boredom, int tiredness)
        {
            var g = Guest(VisitorState.WaitingInQueue); g.DecisionStagger = 0; g.WaitUntil = long.MaxValue;
            VisitorQueue.Wait(g, new World { NowTick = 16 }, new Dice(boredRoll, restRoll));
            Assert.Equal(boredom, g.Boredom);
            Assert.Equal(tiredness, g.Tiredness);
        }

        // A warned or broken ride (slot 20: status 4 or 5) bores its queue a point every 4 ticks, on the
        // guest's stagger. REJECTS "not open" (3 would count), REJECTS 5 alone, REJECTS no stagger.
        [Theory]
        [InlineData(AttractionStatus.AboutToBreakDown, 4, 21)]
        [InlineData(AttractionStatus.BrokenDown, 4, 21)]
        [InlineData(AttractionStatus.BrokenDown, 5, 20)]      // 5 & 3 = 1, not the guest's 0
        [InlineData(AttractionStatus.ClosedByPlayer, 4, 20)]
        [InlineData(AttractionStatus.Running, 4, 20)]
        public void ABrokenRideBoresItsQueueEveryFourStaggeredTicks(AttractionStatus s, long now, int boredom)
        {
            var g = Guest(VisitorState.WaitingInQueue); g.DecisionStagger = 0; g.WaitUntil = long.MaxValue;
            VisitorQueue.Wait(g, new World { NowTick = now, Status = s }, new Dice(99, 99));
            Assert.Equal(boredom, g.Boredom);
        }

        // ⭐ BOREDOM STRICTLY OVER 80 LEAVES, and the leaving IS message 7: bubble 0x30, off the list, 58,
        // in-queue cleared, bit 0x01 set, waypoints freed, target kept. REJECTS >= 80, REJECTS Idle.
        [Fact]
        public void BoredomOverEightyLeavesTheQueueThroughMessageSeven()
        {
            var stays = Guest(VisitorState.WaitingInQueue); stays.Boredom = 80; stays.WaitUntil = long.MaxValue;
            Assert.Equal(WaitOutcome.Standing, VisitorQueue.Wait(stays, new World { NowTick = 1 }, new Dice()));
            Assert.Equal(VisitorState.WaitingInQueue, stays.State);

            var goes = Guest(VisitorState.WaitingInQueue); goes.Boredom = 81; goes.InQueue = true;
            var world = new World { NowTick = 1, Count = 2, Index = 0 };
            Assert.Equal(WaitOutcome.LeftBored, VisitorQueue.Wait(goes, world, new Dice()));
            Assert.Equal(VisitorQueue.BubbleBoredInQueue, goes.Bubble);
            Assert.Equal(1, world.LeftList);
            Assert.Equal(VisitorState.RemovedFromQueue, goes.State);
            Assert.False(goes.InQueue);
            Assert.True(goes.Flag1);
            Assert.Equal(1, world.Freed);
            Assert.True(goes.HasTarget);
        }

        // ⭐⭐ LEAVING BORED RAISES ADVISOR EVENT 3, AND NOTHING ELSE DOES. The original raises it at
        // 0x800908A4 inside the boredom arm -- a call the port's own note had read as a sound, though
        // its arguments are the counter's: index 3, amount EIGHT.
        //
        // ⚠ THE CONTROLS ARE THE POINT. Three paths call LeaveQueue: this one, GiveUp (a closed or
        // broken ride turning the queue away) and a PathFailed message. I first hooked the counter onto
        // the list removal they share, which counts all three -- a broken ride's ejected queue would
        // have registered as guests losing patience, inflating the one number a rule tests against.
        // REJECTS that placement: the two arms below leave the queue and must raise NOTHING.
        [Fact]
        public void OnlyTheBoredGuestRaisesTheAbandonmentCounter()
        {
            var goes = Guest(VisitorState.WaitingInQueue); goes.Boredom = 81; goes.InQueue = true;
            var bored = new World { NowTick = 1, Count = 2, Index = 0 };
            Assert.Equal(WaitOutcome.LeftBored, VisitorQueue.Wait(goes, bored, new Dice()));
            Assert.Equal(new[] { (VisitorQueue.AdvisorEventQueueAbandoned,
                                  VisitorQueue.AdvisorQueueAbandonPoints) }, bored.Events);
            Assert.Equal((3, 8), bored.Events[0]);          // the literals, so a renamed constant cannot drift

            var stays = Guest(VisitorState.WaitingInQueue); stays.Boredom = 80; stays.WaitUntil = long.MaxValue;
            var patient = new World { NowTick = 1 };
            Assert.Equal(WaitOutcome.Standing, VisitorQueue.Wait(stays, patient, new Dice()));
            Assert.Empty(patient.Events);

            var turned = Guest(); turned.InQueue = true;
            var closed = new World { Status = AttractionStatus.ClosedByPlayer, Count = 2, Index = 1 };
            Assert.Equal(JoinOutcome.LeftQueue, VisitorQueue.JoinQueue(turned, closed));
            Assert.Equal(1, closed.LeftList);               // it really did leave the list...
            Assert.Empty(closed.Events);                    // ...and that is still not abandonment

            var thrown = Guest(VisitorState.ShuffleForward); thrown.InQueue = true; thrown.WaitUntil = 0;
            var broke = new World { NowTick = 1, Count = 2, Index = 1, Status = AttractionStatus.BrokenDown };
            Assert.Equal(ShuffleOutcome.LeftQueue, VisitorQueue.Shuffle(thrown, broke));
            Assert.Equal(1, broke.LeftList);
            Assert.Empty(broke.Events);
        }

        // The periodic increment lands BEFORE the check, so a guest at 80 on its stagger tick can tip over
        // and leave the same tick. REJECTS checking first.
        [Fact]
        public void TheIncrementLandsBeforeTheCheck()
        {
            var g = Guest(VisitorState.WaitingInQueue); g.Boredom = 80; g.DecisionStagger = 0; g.InQueue = true;
            Assert.Equal(WaitOutcome.LeftBored, VisitorQueue.Wait(g, new World { NowTick = 8 }, new Dice(0, 99)));
        }

        // ⭐ FIDGET DICE ORDER: rand(300) for the next deadline, rand(4) x 2 for the facing, rand(10) for
        // the sound -- and only once the deadline has STRICTLY passed. REJECTS >= and any other order.
        [Fact]
        public void FidgetingRollsTheDeadlineThenTheFacingThenTheSound()
        {
            var g = Guest(VisitorState.WaitingInQueue); g.WaitUntil = 500; g.DecisionStagger = 1;
            var early = new Dice();
            Assert.Equal(WaitOutcome.Standing, VisitorQueue.Wait(g, new World { NowTick = 500 }, early));
            Assert.Empty(early.Bounds);

            var dice = new Dice(120, 3, 5);
            Assert.Equal(WaitOutcome.Fidgeted, VisitorQueue.Wait(g, new World { NowTick = 501 }, dice));
            Assert.Equal(new[] { 300, 4, 10 }, dice.Bounds);
            Assert.Equal(621, g.WaitUntil);
            Assert.Equal(6, g.Facing);
        }

        // ───────────────────────── 21 loading (0x8008E538) ─────────────────────────

        // Three bits and nothing else, and it does not end itself. REJECTS moving to 22 from here.
        [Fact]
        public void LoadingKeepsThreeBitsStraightAndNothingElse()
        {
            var g = Guest(VisitorState.Loading); g.Flag1 = true; g.InQueue = false; g.Bubble = 0x30; g.Boredom = 60;
            VisitorQueue.Loading(g);
            Assert.False(g.Flag1);
            Assert.True(g.InQueue);
            Assert.Equal(0, g.Bubble);
            Assert.Equal(VisitorState.Loading, g.State);
            Assert.Equal(60, g.Boredom);
        }

        // ───────────────────────── 22 unloading (0x8008F110) ─────────────────────────

        // ⭐ THE COMMON EFFECTS AND THEIR DICE, before the type is looked at: rand(60), rand(300), rand(20).
        // The speed comes back from V+0x62, not a constant. REJECTS reordered dice, REJECTS speed := 15
        // or any fixed number, REJECTS Idle when there is no arm to run.
        [Fact]
        public void UnloadingRestoresTheGuestAndRollsThreeDiceInOrder()
        {
            var g = Guest(VisitorState.Unloading);
            g.InQueue = true; g.WalkSpeed = 15; g.NormalWalkSpeed = 27; g.Tiredness = 40;
            var world = new World { NowTick = 1000, Type = 0 };
            var dice = new Dice(30, 150, 7);
            Assert.Equal(UnloadOutcome.NothingToUnload, VisitorQueue.Unload(g, world, dice));
            Assert.Equal(new[] { 60, 300, 20 }, dice.Bounds);
            Assert.True(g.Flag1);
            Assert.False(g.InQueue);
            Assert.Equal(1090, g.EntertainerNotBefore);
            Assert.Equal(1450, g.WaitUntil);
            Assert.Equal(27, g.WalkSpeed);
            Assert.Equal(VisitorQueue.AnimationWander, g.Animation);
            Assert.Equal(33, g.Tiredness);
            Assert.Equal(VisitorState.GotoEntrance, g.State);
        }

        // ⭐ THREE HAPPINESS BANDS ON |pref - intensity|, boundaries 21 and 51, in BOTH directions.
        // REJECTS <= boundaries and REJECTS a one-sided difference.
        [Theory]
        [InlineData(0, 90, 15)]     // pref 90, a match
        [InlineData(0, 70, 15)]     // 20 away
        [InlineData(0, 69, 10)]     // 21 away
        [InlineData(0, 40, 10)]     // 50 away
        [InlineData(0, 39, 5)]      // 51 away
        [InlineData(1, 100, 5)]     // pref 30, 70 WILDER: still measured
        [InlineData(1, 45, 15)]     // pref 30, 15 wilder
        public void ARidePaysHappinessByHowCloseItsIntensityIsEitherWay(int type, int intensity, int gain)
        {
            var g = Guest(VisitorState.Unloading); g.VisitorType = type; g.Happiness = 50;
            var world = new World { Type = 3, IntensityValue = intensity };
            Assert.Equal(UnloadOutcome.RodeIt, VisitorQueue.Unload(g, world, new Dice(0, 0, 0)));
            Assert.Equal(50 + gain, g.Happiness);
        }

        // ⭐ NAUSEA FROM 56 UP, as 1212 x (I - 30) >> 12. REJECTS > 56, and REJECTS 0.3 x (I - 30) in
        // floating point, which agrees at 56 (7) and disagrees at 100 (21 against the shifted 20).
        [Theory]
        [InlineData(55, 0)] [InlineData(56, 7)] [InlineData(80, 14)] [InlineData(100, 20)]
        public void IntenseRidesAddNauseaInTwentyTwelveFixedPoint(int intensity, int gain)
        {
            var g = Guest(VisitorState.Unloading); g.Nausea = 10;
            VisitorQueue.Unload(g, new World { Type = 1, IntensityValue = intensity }, new Dice(0, 0, 0));
            Assert.Equal(10 + gain, g.Nausea);
        }

        // ⚠ THE SCALE IS PINNED BY VALUE, NOT BY EFFECT. Over the reachable intensities 56..100 no
        // shifted product distinguishes 1212 from 1211 or 1213 (checked exhaustively), so the theory above
        // cannot see a nudge to it -- but 0x801031FC is a lever the debug menu moved (debug.md §3, verified
        // live), and the WORD is what a debug port would read. REJECTS any other value of the word; the
        // theory above REJECTS a scale that is wrong by enough to move a byte (1100 gives 18 at 100).
        [Fact]
        public void TheNauseaScaleIsTheDebugMenusWord()
        {
            Assert.Equal(1212, VisitorQueue.NauseaScale);
            Assert.Equal(30, VisitorQueue.NauseaBase);
            Assert.Equal(56, VisitorQueue.NauseaFromIntensity);
        }

        // Boredom drops by the intensity (4096/4096 of it), the ride counts a guest, the target is KEPT
        // and the guest is in 23. REJECTS clearing the target for rides, REJECTS Idle.
        [Fact]
        public void ARideRelievesBoredomCountsTheGuestAndKeepsTheTargetForTheWalkOut()
        {
            var g = Guest(VisitorState.Unloading); g.Boredom = 70;
            var world = new World { Type = 6, IntensityValue = 45 };
            VisitorQueue.Unload(g, world, new Dice(0, 0, 0));
            Assert.Equal(25, g.Boredom);
            Assert.Equal(1, world.Served);
            Assert.True(g.HasTarget);
            Assert.Equal(VisitorState.GotoEntrance, g.State);
        }

        // The four ride types share one arm and the three walk-in types have one each (table 0x800E3B54).
        // REJECTS treating 7 as a walk-in or 5 as a ride.
        [Theory]
        [InlineData(1, UnloadOutcome.RodeIt)] [InlineData(3, UnloadOutcome.RodeIt)]
        [InlineData(6, UnloadOutcome.RodeIt)] [InlineData(7, UnloadOutcome.RodeIt)]
        [InlineData(2, UnloadOutcome.UsedFeature)]
        [InlineData(4, UnloadOutcome.Bought)] [InlineData(5, UnloadOutcome.Bought)]
        [InlineData(8, UnloadOutcome.NothingToUnload)]
        public void TheUnloadTableDispatchesByType(int type, UnloadOutcome expected)
        {
            var g = Guest(VisitorState.Unloading);
            Assert.Equal(expected, VisitorQueue.Unload(g, new World { Type = type }, new Dice(0, 0, 0)));
        }

        // ⭐ TYPE 2 WITH STOCK: only the need's excess over 60 is spent (x 2/3), the need is zeroed,
        // nausea -40, a guest counted, and a stock reading under 50 upsets the guest. REJECTS >= 60,
        // REJECTS spending the whole need, REJECTS the penalty at exactly 50.
        [Theory]
        [InlineData(90, 100, 20, 30, 0, 0)]      // (90 - 60) x 2 / 3 = 20; nausea 30 - 40 clamps to 0
        [InlineData(61, 100, 0, 30, 0, 0)]       // (61 - 60) x 2 / 3 = 0, and the call is still made
        [InlineData(60, 100, -1, 30, 0, 0)]      // not above 60: no call at all
        [InlineData(90, 49, 20, 50, -10, 10)]    // low stock: -10 happiness, +10 nausea, bubble
        [InlineData(90, 50, 20, 30, 0, 0)]       // exactly 50 is not low
        public void UsingAFeatureSpendsTheNeedAndMayUpsetTheGuest(int need, int stock, int consumed, int nausea0, int dHappy, int dNausea)
        {
            var g = Guest(VisitorState.Unloading); g.RideDesire = need; g.Nausea = nausea0; g.Happiness = 50;
            var world = new World { Type = 2, StockLevelValue = stock };
            Assert.Equal(UnloadOutcome.UsedFeature, VisitorQueue.Unload(g, world, new Dice(0, 0, 0)));
            Assert.Equal(consumed, world.Consumed);
            Assert.Equal(0, g.RideDesire);
            Assert.Equal(Stat.Clamp(nausea0 - 40) + dNausea, g.Nausea);
            Assert.Equal(50 + dHappy, g.Happiness);
            Assert.Equal(dHappy != 0 ? VisitorQueue.BubbleFeatureLow : 0, g.Bubble);
            Assert.Equal(1, world.Served);
            Assert.False(g.HasTarget);
            Assert.Equal(VisitorState.Idle, g.State);
        }

        // An empty feature does nothing but drop the target. REJECTS applying the effects anyway.
        [Fact]
        public void AnEmptyFeatureDoesNothing()
        {
            var g = Guest(VisitorState.Unloading); g.RideDesire = 90; g.Nausea = 50;
            var world = new World { Type = 2, Stock = false };
            Assert.Equal(UnloadOutcome.FeatureEmpty, VisitorQueue.Unload(g, world, new Dice(0, 0, 0)));
            Assert.Equal(90, g.RideDesire);
            Assert.Equal(50, g.Nausea);
            Assert.Equal(-1, world.Consumed);
            Assert.Equal(0, world.Served);
            Assert.False(g.HasTarget);
            Assert.Equal(VisitorState.Idle, g.State);
        }

        // Types 4 and 5 hand off to two DIFFERENT purchase routines (§2.5, VisitorPurchase) and then
        // stand around without a target. REJECTS one routine for both, REJECTS keeping the target.
        // ⚠ DICE: the purchase rolls AFTER the unload's three -- rand(25) for a sale, rand(100) for a
        // play. REJECTS rolling the purchase die first.
        [Fact]
        public void ShopsAndSideShowsHandOffToTheirOwnPurchaseRoutines()
        {
            var shop = new World { Type = 4 };
            var a = Guest(VisitorState.Unloading); var d1 = new Dice(0, 0, 0, 4);
            Assert.Equal(UnloadOutcome.Bought, VisitorQueue.Unload(a, shop, d1));
            Assert.Equal(new[] { 60, 300, 20, 25 }, d1.Bounds);
            Assert.Equal((1, 0), (shop.ShopBuys, shop.SideShowPlays));
            Assert.False(a.HasTarget);
            Assert.Equal(VisitorState.Idle, a.State);

            var stall = new World { Type = 5 };
            var b = Guest(VisitorState.Unloading); var d2 = new Dice(0, 0, 0, 50);
            Assert.Equal(UnloadOutcome.Bought, VisitorQueue.Unload(b, stall, d2));
            Assert.Equal(new[] { 60, 300, 20, 100 }, d2.Bounds);
            Assert.Equal((0, 1), (stall.ShopBuys, stall.SideShowPlays));
            Assert.False(b.HasTarget);
            Assert.Equal(VisitorState.Idle, b.State);
        }

        // A refused purchase is DidNotBuy: the guest still drops its target and goes Idle (the handler
        // ignores the routine's return), and no purchase die is rolled. REJECTS Bought on a refusal.
        [Fact]
        public void ARefusedPurchaseStillEndsInIdleWithoutATarget()
        {
            var shop = new World { Type = 4, Price = 500 };
            var a = Guest(VisitorState.Unloading); var d = new Dice(0, 0, 0);
            Assert.Equal(UnloadOutcome.DidNotBuy, VisitorQueue.Unload(a, shop, d));
            Assert.Equal(new[] { 60, 300, 20 }, d.Bounds);
            Assert.Equal(0, shop.ShopBuys);
            Assert.False(a.HasTarget);
            Assert.Equal(VisitorState.Idle, a.State);
        }

        // ───────────────────────── 23 goto entrance (0x8008F7BC) ─────────────────────────

        // Three exits: no entrance point -> Idle with the target KEPT; refused -> stay, purpose already 4;
        // accepted -> SET 11 at queue speed. REJECTS clearing the target, REJECTS Idle on refusal,
        // REJECTS Push.
        [Fact]
        public void GoingToTheEntranceHasThreeExits()
        {
            var none = Guest(VisitorState.GotoEntrance);
            Assert.Equal(EntranceOutcome.NoEntrance, VisitorQueue.GotoEntrance(none, new World { Entrance = false }));
            Assert.Equal(VisitorState.Idle, none.State);
            Assert.True(none.HasTarget);
            Assert.Equal(Purpose.Spent, none.Purpose);

            var refused = Guest(VisitorState.GotoEntrance);
            Assert.Equal(EntranceOutcome.PathRefused, VisitorQueue.GotoEntrance(refused, new World { EntrancePathAccepted = false }));
            Assert.Equal(VisitorState.GotoEntrance, refused.State);
            Assert.Equal(Purpose.Abandon, refused.Purpose);

            var ok = Guest(VisitorState.GotoEntrance); ok.PushState(VisitorState.GotoEntrance);
            Assert.Equal(EntranceOutcome.Walking, VisitorQueue.GotoEntrance(ok, new World { NowTick = 42 }));
            Assert.Equal(VisitorState.WalkToBin, ok.State);
            Assert.Equal(0, ok.StackDepth);
            Assert.Equal(VisitorQueue.QueueWalkSpeed, ok.WalkSpeed);
            Assert.Equal(42, ok.WaitUntil);
            Assert.Equal(Purpose.Abandon, ok.Purpose);
        }

        // ───────────────────────── 58 removed from queue (0x800915F4) ─────────────────────────

        // ⭐ PUSH 11, so the walk pops back to 58 and arrival 22 ends it; the purpose is only written once
        // the request is accepted. REJECTS Set, REJECTS writing the purpose on refusal.
        [Fact]
        public void WalkingToTheLeavePointPushesElevenAndOnlyCommitsOnAcceptance()
        {
            var refused = Guest(VisitorState.RemovedFromQueue);
            Assert.False(VisitorQueue.WalkToLeavePoint(refused, new World { LeavePathAccepted = false }));
            Assert.Equal(VisitorState.RemovedFromQueue, refused.State);
            Assert.Equal(Purpose.Spent, refused.Purpose);

            var ok = Guest(VisitorState.RemovedFromQueue);
            Assert.True(VisitorQueue.WalkToLeavePoint(ok, new World { NowTick = 9 }));
            Assert.Equal(VisitorState.WalkToBin, ok.State);
            Assert.Equal(1, ok.StackDepth);
            Assert.Equal(Purpose.ClearTarget, ok.Purpose);
            Assert.Equal(9, ok.WaitUntil);
            ok.PopState();
            Assert.Equal(VisitorState.RemovedFromQueue, ok.State);
        }

        // ───────────────────────── messages 6, 7, 10 (0x8008F880) ─────────────────────────

        // ⭐ SHUFFLE IS GATED ON STATE 18 (OR 44) AND DELAYS BY 3 x THE STAGGER into whatever state param2
        // names. REJECTS acting in other states, REJECTS 1 x stagger, REJECTS a hard-wired 19.
        [Fact]
        public void AShuffleMessageOnlyMovesAWaitingGuestAndDelaysThreeTicksPerStagger()
        {
            var waiting = Guest(VisitorState.WaitingInQueue);
            var world = new World { NowTick = 100 };
            Assert.True(VisitorQueue.OnMessage(waiting, world, QueueMessage.Shuffle, param1: 4, param2: 19));
            Assert.Equal(VisitorState.ShuffleForward, waiting.State);
            Assert.Equal(112, waiting.WaitUntil);
            Assert.Equal(1, world.Freed);

            var turnstile = Guest(VisitorQueue.TurnstileFront);
            Assert.True(VisitorQueue.OnMessage(turnstile, world, QueueMessage.Shuffle, 0, 43));
            Assert.Equal((VisitorState)43, turnstile.State);

            var loading = Guest(VisitorState.Loading);
            Assert.False(VisitorQueue.OnMessage(loading, world, QueueMessage.Shuffle, 4, 19));
            Assert.Equal(VisitorState.Loading, loading.State);
            Assert.Equal(2, world.Freed);                                 // untouched by the third
        }

        // ⭐ REMOVED KEEPS THE TARGET, EJECTED DROPS IT; both clear the queue bit, free the waypoints and
        // act in any state. REJECTS one arm for both, REJECTS gating them on 18.
        [Fact]
        public void RemovedKeepsTheTargetAndEjectedDropsIt()
        {
            var removed = Guest(VisitorState.Loading); removed.InQueue = true;
            var w1 = new World();
            Assert.True(VisitorQueue.OnMessage(removed, w1, QueueMessage.RemovedFromQueue));
            Assert.Equal(VisitorState.RemovedFromQueue, removed.State);
            Assert.True(removed.HasTarget);
            Assert.False(removed.InQueue);
            Assert.Equal(1, w1.Freed);

            var ejected = Guest(VisitorState.ShuffleForward); ejected.InQueue = true;
            var w2 = new World();
            Assert.True(VisitorQueue.OnMessage(ejected, w2, QueueMessage.Ejected));
            Assert.Equal(VisitorState.Idle, ejected.State);
            Assert.False(ejected.HasTarget);
            Assert.False(ejected.InQueue);
            Assert.Equal(1, w2.Freed);
        }

        // ───────────────────────── the arrival's share (§2.3 purposes 0, 3, 10, 4, 22) ─────────────────────────

        sealed class ArrivalWorld : IArrivalWorld
        {
            public long NowTick { get; set; }
            public int Type { get; set; } = 3;
            public bool AtSlot { get; set; }
            public bool SlotFound { get; set; } = true;
            public bool HasWaypoint(Visitor g) => false;
            public void StepToNextWaypoint(Visitor g) { }
            public int TargetType(Visitor g) => Type;
            public bool TargetHasStock(Visitor g) => true;
            public bool TargetHasEntrance(Visitor g) => true;
            public bool TryJoinQueue(Visitor g) => true;
            public bool TryQueueSlot(Visitor g, out bool atSlot) { atSlot = AtSlot; return SlotFound; }
            public void RemoveFromPark(Visitor g) { }
            // the turnstile view, unused by the queue chain
            public int Counter80103950 { get; set; }
            public int Counter80103954 { get; set; }
            public (int X, int Y) Position(Visitor g) => (0, 0);
            public MapTile EntranceTile(int lane) => new MapTile(0, 0);
            public IReadOnlyList<Visitor> Lane(int lane) => new List<Visitor>();
            public void AppendToLane(int lane, Visitor g) { }
            public int LaneCount(int lane) => 0;
            public void SetLaneCount(int lane, int value) { }
        }

        // ⭐ ARRIVING AT THE QUEUE SETS THE IN-QUEUE BIT (whether or not 41 did), and reaching the slot
        // resets the animation; a lost slot sets neither. REJECTS leaving the bit to 41 alone.
        [Theory]
        [InlineData(Purpose.QueueWalk, true, VisitorState.WaitingInQueue, 11)]
        [InlineData(Purpose.QueueWalk, false, VisitorState.ShuffleForward, 0)]
        [InlineData(Purpose.QueueShuffle, true, VisitorState.WaitingInQueue, 11)]
        [InlineData(Purpose.QueueShuffle, false, VisitorState.JoiningQueue, 0)]
        public void ArrivingAtTheQueueFlagsTheGuestAndAtTheSlotResetsTheAnimation(Purpose p, bool atSlot, VisitorState state, int animation)
        {
            var g = Guest(VisitorState.WalkToWaypoint); g.Purpose = p; g.Animation = 0;
            VisitorArrival.Tick(g, new ArrivalWorld { AtSlot = atSlot }, new Dice());
            Assert.True(g.InQueue);
            Assert.Equal(state, g.State);
            Assert.Equal(animation, g.Animation);

            var lost = Guest(VisitorState.WalkToWaypoint); lost.Purpose = p;
            VisitorArrival.Tick(lost, new ArrivalWorld { SlotFound = false }, new Dice());
            Assert.False(lost.InQueue);
        }

        // Purpose 4 resets the animation and drops the target; purpose 22 drops the target and leaves the
        // animation alone; purpose 1 resets it; a type-2 or type-4/5 arrival clears bit 0x01 and a
        // ride's does not. REJECTS sharing one arm between 4 and 22, REJECTS clearing the bit for rides.
        [Fact]
        public void TheOtherArrivalArmsTouchTheNewFieldsAsRead()
        {
            var back = Guest(VisitorState.WalkToWaypoint); back.Purpose = Purpose.Abandon; back.Animation = 13;
            VisitorArrival.Tick(back, new ArrivalWorld(), new Dice());
            Assert.Equal(VisitorQueue.AnimationIdle, back.Animation);
            Assert.False(back.HasTarget);

            var away = Guest(VisitorState.WalkToWaypoint); away.Purpose = Purpose.ClearTarget; away.Animation = 13;
            VisitorArrival.Tick(away, new ArrivalWorld(), new Dice());
            Assert.Equal(13, away.Animation);
            Assert.False(away.HasTarget);

            var done = Guest(VisitorState.WalkToWaypoint); done.Purpose = Purpose.Finished; done.Animation = 13;
            VisitorArrival.Tick(done, new ArrivalWorld(), new Dice());
            Assert.Equal(VisitorQueue.AnimationIdle, done.Animation);

            var shopper = Guest(VisitorState.WalkToWaypoint); shopper.Purpose = Purpose.AtAttraction; shopper.Flag1 = true;
            VisitorArrival.Tick(shopper, new ArrivalWorld { Type = 2 }, new Dice());
            Assert.False(shopper.Flag1);

            var player = Guest(VisitorState.WalkToWaypoint); player.Purpose = Purpose.AtAttraction; player.Flag1 = true;
            VisitorArrival.Tick(player, new ArrivalWorld { Type = 5 }, new Dice());
            Assert.False(player.Flag1);                                // stalls clear it as well (0x8008DCC8)

            var rider = Guest(VisitorState.WalkToWaypoint); rider.Purpose = Purpose.AtAttraction; rider.Flag1 = true;
            VisitorArrival.Tick(rider, new ArrivalWorld { Type = 3 }, new Dice());
            Assert.True(rider.Flag1);
        }

        // The spawn copies ONE speed roll to both fields. REJECTS a second rand(15), which would shift
        // every later die.
        [Fact]
        public void SpawnRollsTheWalkSpeedOnceForBothFields()
        {
            var dice = new Dice(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 9, 0);
            var v = Visitor.Spawn(dice, 0);
            Assert.Equal(24, v.WalkSpeed);
            Assert.Equal(24, v.NormalWalkSpeed);
            Assert.Equal(12, dice.Bounds.Count);
        }

        // ───────────────────────── the whole chain ─────────────────────────

        // ⭐ FROM CHOSEN TO WALKING AWAY WITHOUT EVER ENTERING 4. The ride's two hand-offs (18 -> 21 and
        // 21 -> 22) are done by hand, as the ride classes do them, and every state seen is pinned.
        // REJECTS any implementation that wires OnRide, and pins the order of the chain.
        [Fact]
        public void TheChainRunsFromJoinToWalkingAwayAndNeverEntersOnRide()
        {
            var g = Guest(); g.VisitorType = 0; g.Boredom = 20;
            var world = new World { NowTick = 100, Type = 3, IntensityValue = 80 };
            var seen = new List<VisitorState> { g.State };
            void Note() => seen.Add(g.State);

            Assert.Equal(JoinOutcome.Walking, VisitorQueue.JoinQueue(g, world)); Note();               // 41 -> 11
            g.SetState(VisitorState.WalkToWaypoint); Note();                                          // path ready
            VisitorArrival.Tick(g, new ArrivalWorld { AtSlot = true }, new Dice()); Note();            // arrival 3 -> 18
            world.NowTick = 101;
            Assert.Equal(WaitOutcome.Fidgeted, VisitorQueue.Wait(g, world, new Dice(0, 0, 1))); Note();
            g.SetState(VisitorState.Loading); Note();                                                 // the ride loads
            VisitorQueue.Loading(g); Note();
            g.SetState(VisitorState.Unloading); Note();                                               // the ride unloads
            Assert.Equal(UnloadOutcome.RodeIt, VisitorQueue.Unload(g, world, new Dice(0, 0, 0))); Note(); // -> 23
            Assert.Equal(EntranceOutcome.Walking, VisitorQueue.GotoEntrance(g, world)); Note();          // -> 11
            g.SetState(VisitorState.WalkToWaypoint);
            VisitorArrival.Tick(g, new ArrivalWorld(), new Dice()); Note();                            // arrival 4 -> 0

            Assert.Equal(new[]
            {
                VisitorState.JoiningQueue, VisitorState.WalkToBin, VisitorState.WalkToWaypoint,
                VisitorState.WaitingInQueue, VisitorState.WaitingInQueue, VisitorState.Loading,
                VisitorState.Loading, VisitorState.Unloading, VisitorState.GotoEntrance,
                VisitorState.WalkToBin, VisitorState.Idle,
            }, seen);
            Assert.DoesNotContain(VisitorState.OnRide, seen);
            Assert.Equal(4, (int)VisitorState.OnRide);
            Assert.Equal(65, g.Happiness);                    // pref 90 against 80: 10 away, the +15 band
            Assert.False(g.InQueue);
            Assert.False(g.HasTarget);
        }
    }
}
