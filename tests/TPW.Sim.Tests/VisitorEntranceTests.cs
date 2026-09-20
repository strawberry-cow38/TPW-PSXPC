using System;
using System.Collections.Generic;
using System.Linq;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>The park entrance and exit: states 36, 42, 43, 44, 46, 37, 45, 38, 47 and 48, the arrival
    /// arms 11-16, the message rows 1, 2, 4 and 9, and the gate's own two routines (behaviour.md §2.6,
    /// §2.3, §2.10; arrivals.md §4).</summary>
    public class VisitorEntranceTests
    {
        /// <summary>Scripted dice that RECORD the bound of every roll. Exhausted rolls return 1, which
        /// reads as "missed" for the 25% greeting and as "lane 1" for the lane roll.</summary>
        sealed class Dice : IRandomSource
        {
            readonly Queue<int> _v;
            public Dice(params int[] v) => _v = new Queue<int>(v);
            public List<int> Bounds { get; } = new();
            public int Next(int n) { Bounds.Add(n); return _v.Count > 0 ? _v.Dequeue() : 1; }
        }

        /// <summary>Only <see cref="FreeWaypoints"/> and <see cref="NowTick"/> are reached: message 6 is
        /// the one queue message the turnstile sends, and that is all it touches.</summary>
        sealed class QueueStub : IQueueWorld
        {
            public World Owner;
            public int Freed;
            public long NowTick => Owner.NowTick;
            public void FreeWaypoints(Visitor g) => Freed++;
            public int TargetType(Visitor g) => throw new NotSupportedException();
            public AttractionStatus RideStatus(Visitor g) => throw new NotSupportedException();
            public int QueueCount(Visitor g) => throw new NotSupportedException();
            public int UpgradeLevel(Visitor g) => throw new NotSupportedException();
            public int Intensity(Visitor g) => throw new NotSupportedException();
            public QueueTile QueueOrigin(Visitor g) => throw new NotSupportedException();
            public IReadOnlyList<QueueTile> QueuePath(Visitor g) => throw new NotSupportedException();
            public int QueueIndexOf(Visitor g) => throw new NotSupportedException();
            public void AppendToQueue(Visitor g) => throw new NotSupportedException();
            public void LeaveQueueList(Visitor g) => throw new NotSupportedException();
            public bool TryPathToSlot(Visitor g, int x, int y) => throw new NotSupportedException();
            public bool TrySetSingleWaypoint(Visitor g, int x, int y) => throw new NotSupportedException();
            public bool TargetHasEntrance(Visitor g) => throw new NotSupportedException();
            public bool TryPathToEntrance(Visitor g) => throw new NotSupportedException();
            public bool TryPathToLeavePoint(Visitor g) => throw new NotSupportedException();
            public void CountGuestServed(Visitor g) => throw new NotSupportedException();
            public bool TargetHasStock(Visitor g) => throw new NotSupportedException();
            public void ConsumeStock(Visitor g, int units) => throw new NotSupportedException();
            public int StockLevel(Visitor g) => throw new NotSupportedException();
            public void BuyAtShop(Visitor g) => throw new NotSupportedException();
            public void PlaySideShow(Visitor g) => throw new NotSupportedException();
        }

        /// <summary>One park: two lanes, an entrance building at tile (20, 5), two exits, a £40 fee.
        /// Messages are ROUTED the way a host would route them, so the turnstile's message 6 really
        /// lands in VisitorQueue.OnMessage and its message 9 in VisitorEntrance.OnMessage.</summary>
        sealed class World : IEntranceWorld, IArrivalWorld
        {
            public World() { Queue = new QueueStub { Owner = this }; }
            public readonly QueueStub Queue;

            public long NowTick { get; set; }
            public (int X, int Y) Pos { get; set; }
            public MapTile Entrance { get; set; } = new MapTile(20, 5);
            public List<Visitor>[] Lanes { get; } = { new(), new() };
            public int[] Counts { get; } = { 0, 0 };
            public int Counter80103950 { get; set; }
            public int Counter80103954 { get; set; }
            public int ExitCount { get; set; } = 2;
            public bool PathAccepted { get; set; } = true;
            public List<string> Calls { get; } = new();
            public (int Point, int Flags, int Secondary) LastPoint { get; private set; } = (-1, -1, -1);
            public (int Exit, int Flags, int Secondary) LastExit { get; private set; } = (-1, -1, -1);
            public (int X, int Y) LastSlotPath { get; private set; }
            public bool WaypointAvailable { get; set; } = true;
            public (int X, int Y) Waypoint { get; private set; }
            public int GateWaypointPoint { get; private set; } = -1;
            public int Freed { get; private set; }
            public HashSet<(int, int)> Flag8 { get; } = new();
            public HashSet<(int, int)> PathTiles { get; } = new();
            public int TileLookups { get; private set; }
            public Money Fee { get; set; } = Money.FromPounds(40);
            public int Booked { get; private set; }
            public int Admissions { get; private set; }
            public List<int> Intensities { get; } = new();
            public int Removed { get; private set; }
            public int GateBatch { get; set; }
            public int BusPhase { get; set; } = 1;
            public List<Visitor> GuestList { get; } = new();
            public List<StaffMember> StaffList { get; } = new();
            public List<(Visitor Guest, int Id, int P1, int P2)> Messages { get; } = new();
            public List<(StaffMember Staff, int Id)> StaffMessages { get; } = new();
            public List<Visitor> RemovedFromLane { get; } = new();

            public (int X, int Y) Position(Visitor g) => Pos;
            public MapTile EntranceTile(int lane) { Calls.Add($"entrance-{lane}"); return Entrance; }
            public IReadOnlyList<Visitor> Lane(int lane) => Lanes[lane];
            public void AppendToLane(int lane, Visitor g) { Calls.Add($"append-{lane}"); Lanes[lane].Add(g); }
            public int LaneCount(int lane) => Counts[lane];
            public void SetLaneCount(int lane, int value) => Counts[lane] = value;
            public bool TryPathToParkPoint(Visitor g, int point, int flags, int secondary)
            { Calls.Add($"point-{point}"); LastPoint = (point, flags, secondary); return PathAccepted; }
            public bool TryPathToExit(Visitor g, int exit, int flags, int secondary)
            { Calls.Add($"exit-{exit}"); LastExit = (exit, flags, secondary); return PathAccepted; }
            public bool TryPathToLaneSlot(Visitor g, int x, int y) { Calls.Add("slot-path"); LastSlotPath = (x, y); return PathAccepted; }
            public bool TrySetSingleWaypoint(Visitor g, int x, int y)
            {
                Calls.Add("waypoint");
                if (!WaypointAvailable) return false;
                Waypoint = (x, y); return true;
            }
            public bool TrySetGateWaypoint(Visitor g, int point)
            {
                Calls.Add($"gate-waypoint-{point}");
                if (!WaypointAvailable) return false;
                GateWaypointPoint = point; return true;
            }
            public void FreeWaypoints(Visitor g) { Calls.Add("free"); Freed++; }
            public bool TileHasFlag8(int x, int y) { TileLookups++; return Flag8.Contains((x, y)); }
            public bool TileIsPath(int x, int y) => PathTiles.Contains((x, y));
            public Money EntryFee => Fee;
            public void BookEntryFee(Visitor g) => Booked++;
            public void CountAdmission() => Admissions++;
            public IEnumerable<int> AttractionIntensities => Intensities;
            public void RemoveFromPark(Visitor g) { Calls.Add("remove"); Removed++; }
            public IEnumerable<Visitor> Guests => GuestList;
            public IEnumerable<StaffMember> Staff => StaffList;
            public void DeliverMessage(Visitor g, int id, int p1, int p2)
            {
                Messages.Add((g, id, p1, p2));
                if (id == 6 || id == 7 || id == 10) VisitorQueue.OnMessage(g, Queue, (QueueMessage)id, p1, p2);
                else VisitorEntrance.OnMessage(g, this, (EntranceMessage)id, new Dice());
            }
            public void DeliverStaffMessage(StaffMember s, int id) => StaffMessages.Add((s, id));
            public void RemoveFromLane(int lane, Visitor g) { RemovedFromLane.Add(g); Lanes[lane].Remove(g); }

            // IArrivalWorld, none of which the turnstile arms touch
            public bool HasWaypoint(Visitor g) => false;
            public void StepToNextWaypoint(Visitor g) { }
            public int TargetType(Visitor g) => 0;
            public bool TargetHasStock(Visitor g) => false;
            public bool TargetHasEntrance(Visitor g) => false;
            public bool TryJoinQueue(Visitor g) => false;
            public bool TryQueueSlot(Visitor g, out bool atSlot) { atSlot = false; return false; }
        }

        /// <summary>A guest in the given state with its own pace 22, no target, and V+0x28's number as given.</summary>
        static Visitor Guest(VisitorState state, int scratch = 0)
        {
            var v = Visitor.Spawn(new Dice(), 0);
            v.Happiness = 50; v.Tiredness = 40; v.Nausea = 10; v.Boredom = 20; v.Rubbish = 0;
            v.WalkSpeed = 22; v.NormalWalkSpeed = 22;
            v.HasTarget = false; v.GateScratch = scratch; v.Purpose = Purpose.Spent;
            v.SetState(state);
            return v;
        }

        static void State(Visitor g, int state) => Assert.Equal((VisitorState)state, g.State);

        // ───────────────────────── 36: spawn to gate ─────────────────────────

        // ⚠ THE PURPOSE IS WRITTEN BEFORE THE REQUEST, and the accepted request SETS 11 rather than
        // pushing it. REJECTS writing purpose 15 only on acceptance, pushing 11, asking for point 1,
        // and asking with the gate-side flags 38 uses.
        [Fact]
        public void SpawnToGateWritesPurpose15BeforeAskingAndSetsElevenOnlyWhenAccepted()
        {
            var g = Guest(VisitorState.SpawnToGate); g.WaitUntil = 7;
            var w = new World { NowTick = 50, PathAccepted = false };

            Assert.Equal(GateWalkOutcome.PathRefused, VisitorEntrance.SpawnToGate(g, w));
            Assert.Equal(Purpose.Turnstile15, g.Purpose);
            State(g, 36); Assert.Equal(7, g.WaitUntil);

            w.PathAccepted = true;
            g.PushState(VisitorState.SpawnToGate);       // a stack entry, to prove SetState clears it
            Assert.Equal(GateWalkOutcome.Walking, VisitorEntrance.SpawnToGate(g, w));
            Assert.Equal((0, 1, 0), w.LastPoint);
            Assert.Equal(50, g.WaitUntil);
            State(g, 11); Assert.Equal(0, g.StackDepth);
        }

        // ───────────────────────── 47: pick a lane ─────────────────────────

        // ⚠ THE WAYPOINT'S Y IS THE FAR SIDE'S: point `1 - V+0x28`, so a guest coming in steps toward
        // the exit point's y and a guest going out toward the spawn point's y. REJECTS a fixed "gate y",
        // and REJECTS writing purpose 16 when the pool was empty.
        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 0)]
        public void PickLaneStepsToTheFarSidesPointAndWritesPurposeOnlyWithAWaypoint(int scratch, int point)
        {
            var g = Guest(VisitorState.PickLane, scratch);
            var w = new World { WaypointAvailable = false };
            Assert.Equal(GateWalkOutcome.NoWaypoint, VisitorEntrance.PickLane(g, w));
            Assert.Equal(Purpose.Spent, g.Purpose); State(g, 47);
            Assert.Equal(new[] { "free", $"gate-waypoint-{point}" }, w.Calls);

            w.WaypointAvailable = true;
            Assert.Equal(GateWalkOutcome.Stepping, VisitorEntrance.PickLane(g, w));
            Assert.Equal(point, w.GateWaypointPoint);
            Assert.Equal(Purpose.Turnstile16, g.Purpose); State(g, 3);
        }

        // ───────────────────────── lane slots ─────────────────────────

        // ⚠ THE TWO LANES ARE ON OPPOSITE CORNERS, NOT MIRRORED: lane 0 is (+0x180, +0x80), lane 1 is
        // (-0x180, -0x80), and BOTH lanes extend toward -y a quarter tile per guest ahead. REJECTS a
        // mirrored y offset, REJECTS +y spacing, and REJECTS spacing that differs by lane.
        [Theory]
        [InlineData(0, 0, 5504, 1408)]
        [InlineData(0, 3, 5504, 1216)]
        [InlineData(1, 0, 4736, 1152)]
        [InlineData(1, 2, 4736, 1024)]
        public void LaneSlotIsTheEntranceCornerByLaneMinusAQuarterTilePerGuestAhead(int lane, int ahead, int x, int y)
        {
            Assert.Equal((x, y), VisitorEntrance.LaneSlot(new MapTile(20, 5), lane, ahead));
        }

        // A newcomer is placed behind everyone, appended, and the lane COUNT WORD goes up by one; a
        // member is placed by its index and nothing is written. REJECTS counting a member as "ahead of
        // itself", REJECTS appending a member twice, and REJECTS deriving the count from the list.
        [Fact]
        public void LaneSlotForAppendsAndCountsANewcomerButNotAMember()
        {
            var w = new World();
            var a = Guest(VisitorState.WalkToLaneSlot, 1); var b = Guest(VisitorState.WalkToLaneSlot, 1);
            w.Lanes[1].Add(a); w.Lanes[1].Add(b); w.Counts[1] = 7;   // the word need not match the list

            var c = Guest(VisitorState.WalkToLaneSlot, 1);
            Assert.Equal((4736, 1152 - 2 * 0x40), VisitorEntrance.LaneSlotFor(c, w));
            Assert.Equal(new[] { a, b, c }, w.Lanes[1]);
            Assert.Equal(8, w.Counts[1]);

            Assert.Equal((4736, 1152 - 0x40), VisitorEntrance.LaneSlotFor(b, w));
            Assert.Equal(3, w.Lanes[1].Count);
            Assert.Equal(8, w.Counts[1]);
            Assert.Empty(w.Lanes[0]);
        }

        // ───────────────────────── 42 and 43 ─────────────────────────

        // The queue bit and purpose 11 go on before the request; the queue speed only after it is
        // accepted. REJECTS slowing a refused guest, REJECTS leaving the bit to the arrival, and
        // REJECTS pushing 11.
        [Fact]
        public void WalkToLaneSlotFlagsAndPurposesBeforeAskingAndSlowsOnlyAfter()
        {
            var g = Guest(VisitorState.WalkToLaneSlot, 0);
            var w = new World { NowTick = 90, PathAccepted = false };

            Assert.Equal(GateWalkOutcome.PathRefused, VisitorEntrance.WalkToLaneSlot(g, w));
            Assert.True(g.InQueue); Assert.Equal(Purpose.Turnstile11, g.Purpose);
            Assert.Equal(22, g.WalkSpeed); State(g, 42);
            Assert.Equal((5504, 1408), w.LastSlotPath);
            Assert.Single(w.Lanes[0]);

            w.PathAccepted = true;
            Assert.Equal(GateWalkOutcome.Walking, VisitorEntrance.WalkToLaneSlot(g, w));
            Assert.Equal(15, g.WalkSpeed); Assert.Equal(90, g.WaitUntil);
            State(g, 11); Assert.Equal(0, g.StackDepth);
            Assert.Single(w.Lanes[0]);                 // still listed once
        }

        // 43 frees, flags, purposes, and walks ONE waypoint to the recomputed slot; an empty pool leaves
        // it in 43 with purpose 12 and the waypoints already freed. REJECTS pathing (42's move),
        // REJECTS keeping the old waypoint, and REJECTS resetting the purpose on an empty pool.
        [Fact]
        public void ShuffleInLaneFreesThenWalksOneWaypointToTheRecomputedSlot()
        {
            var w = new World();
            var ahead = Guest(VisitorState.LaneFront, 0); w.Lanes[0].Add(ahead);
            var g = Guest(VisitorState.ShuffleInLane, 0); w.Lanes[0].Add(g);

            w.WaypointAvailable = false;
            Assert.Equal(GateWalkOutcome.NoWaypoint, VisitorEntrance.ShuffleInLane(g, w));
            State(g, 43); Assert.Equal(Purpose.Turnstile12, g.Purpose); Assert.True(g.InQueue);
            Assert.Equal(1, w.Freed);

            w.WaypointAvailable = true;
            Assert.Equal(GateWalkOutcome.Stepping, VisitorEntrance.ShuffleInLane(g, w));
            Assert.Equal((5504, 1408 - 0x40), w.Waypoint);
            State(g, 3); Assert.Equal(2, w.Freed);
            Assert.DoesNotContain("slot-path", w.Calls);
        }

        // ───────────────────────── arrivals 11 and 12 ─────────────────────────

        // ⚠ "AT THE SLOT" IS EXACT, TO THE 8.8 UNIT, and the only difference between the two arms is
        // where a guest that is NOT on it goes. REJECTS comparing tiles, and REJECTS one fallback for both.
        [Theory]
        [InlineData(Purpose.Turnstile11, 0, 44, Arrival.AtLaneFront)]
        [InlineData(Purpose.Turnstile11, 1, 42, Arrival.WalkingToLaneSlot)]
        [InlineData(Purpose.Turnstile12, 0, 44, Arrival.AtLaneFront)]
        [InlineData(Purpose.Turnstile12, 1, 43, Arrival.ShufflingInLane)]
        public void ArrivalAtTheLaneSlotIsExactAndOnlyTheFallbackDiffers(Purpose p, int shortBy, int state, Arrival result)
        {
            var g = Guest(VisitorState.WalkToWaypoint, 0); g.Purpose = p;
            var w = new World { Pos = (5504, 1408 - shortBy) };   // same tile either way
            Assert.Equal(result, VisitorArrival.Tick(g, w, new Dice()));
            State(g, state);
            Assert.Equal(Purpose.Spent, g.Purpose);
            Assert.Single(w.Lanes[0]);
        }

        // ───────────────────────── arrivals 14 and 15 ─────────────────────────

        // ⭐ BOTH ARRIVALS AT THE GATE COUNT ONE WAITING, and they differ only in the number left in
        // V+0x28 and the facing. §2.6's row for 38 omits the increment on 14; §2.3 and the code have
        // it. REJECTS counting on 15 only, REJECTS the same scratch for both, and REJECTS a target left
        // "set" through the gate.
        [Theory]
        [InlineData(Purpose.Turnstile14, 1, 4)]
        [InlineData(Purpose.Turnstile15, 0, 0)]
        public void ArrivalAtEitherGatePointWaitsCountedWithItsDirectionInTheScratch(Purpose p, int scratch, int facing)
        {
            var g = Guest(VisitorState.WalkToWaypoint, 9); g.Purpose = p; g.HasTarget = true; g.Facing = 6;
            var w = new World { Counter80103950 = 3, Counter80103954 = 8 };
            Assert.Equal(Arrival.AtGate, VisitorArrival.Tick(g, w, new Dice()));
            State(g, 46);
            Assert.Equal(scratch, g.GateScratch); Assert.False(g.HasTarget); Assert.Equal(facing, g.Facing);
            Assert.Equal(4, w.Counter80103950); Assert.Equal(8, w.Counter80103954);
            Assert.Equal(p, g.Purpose);        // §2.3's rule, kept; see the note in VisitorArrival.Tick
        }

        // ───────────────────────── arrival 16 ─────────────────────────

        // A leaver crosses without a die: scratch zeroed, 48, one fewer waiting, one more admitted.
        // REJECTS rolling a lane for a leaver, and REJECTS crossing without moving the counters.
        [Fact]
        public void ArrivalAtTheGateLeavingGoesOutWithoutRollingAndMovesBothCounters()
        {
            var g = Guest(VisitorState.WalkToWaypoint, 1); g.Purpose = Purpose.Turnstile16; g.HasTarget = true;
            var w = new World { Counter80103950 = 10, Counter80103954 = 20 };
            var dice = new Dice();
            Assert.Equal(Arrival.LeavingThroughGate, VisitorArrival.Tick(g, w, dice));
            State(g, 48); Assert.Equal(0, g.GateScratch); Assert.False(g.HasTarget);
            Assert.Equal(9, w.Counter80103950); Assert.Equal(21, w.Counter80103954);
            Assert.Empty(dice.Bounds);
        }

        // ⚠ ONE FLIP, NOT A SEARCH, AND AT 11 NOT 12. A lane whose count word is 11 or more sends the
        // guest to the other lane -- even if that one is full too. REJECTS a strict "> 11", REJECTS
        // waiting for room, and REJECTS testing the list instead of the count word.
        [Theory]
        [InlineData(0, 10, 0, 0)]
        [InlineData(0, 11, 0, 1)]
        [InlineData(1, 0, 11, 0)]
        [InlineData(1, 11, 11, 0)]
        public void ArrivalAtTheGateEnteringRollsALaneAndFlipsAFullOne(int roll, int count0, int count1, int lane)
        {
            var g = Guest(VisitorState.WalkToWaypoint, 0); g.Purpose = Purpose.Turnstile16;
            var w = new World { Counter80103950 = 5, Counter80103954 = 2 };
            w.Counts[0] = count0; w.Counts[1] = count1;
            var dice = new Dice(roll);
            Assert.Equal(Arrival.WalkingToLaneSlot, VisitorArrival.Tick(g, w, dice));
            State(g, 42); Assert.Equal(lane, g.GateScratch);
            Assert.Equal(new[] { 2 }, dice.Bounds);
            Assert.Equal(4, w.Counter80103950); Assert.Equal(3, w.Counter80103954);
            Assert.Empty(w.Lanes[0]); Assert.Empty(w.Lanes[1]);   // listing happens in 42, not here
        }

        // ───────────────────────── the verdict ─────────────────────────

        // With sum 100 and a roll of 0, q = 409600 / 10000 = 40, so the bands sit at 30, 50 and 60.
        // REJECTS an off-by-one at every edge: 30 is still a bargain, 50 is already -1, 60 is refused.
        [Theory]
        [InlineData(30, 1)]
        [InlineData(31, 0)]
        [InlineData(49, 0)]
        [InlineData(50, -1)]
        [InlineData(59, -1)]
        [InlineData(60, -2)]
        public void TheVerdictBandsAreThreeQuartersFiveQuartersAndThreeHalvesOfQ(int feePounds, int verdict)
        {
            Assert.Equal(verdict, VisitorEntrance.FeeVerdict(100, Money.FromPounds(feePounds), new Dice(0)));
        }

        // ⚠ THE DIVISOR IS 10000 + rand(5001), not §2.6's `5001 + rand(5001)`. A roll of 5000 makes it
        // 15000 and q = 27, so a £21 fee is neutral; under §2.6's reading the divisor would be 10001,
        // q = 40, and £21 a bargain. REJECTS §2.6's formula, and REJECTS rand(5000).
        [Fact]
        public void TheVerdictDivisorIsTenThousandPlusTheRoll()
        {
            var dice = new Dice(5000);
            Assert.Equal(1, VisitorEntrance.FeeVerdict(100, Money.FromPounds(20), dice));
            Assert.Equal(new[] { 5001 }, dice.Bounds);
            Assert.Equal(0, VisitorEntrance.FeeVerdict(100, Money.FromPounds(21), new Dice(5000)));
        }

        // ⚠ NO RIDES IS NOT FREE ENTRY. q = 0 makes every band 0, so any fee at all is refused, and only
        // "no rides AND no fee" takes the early 0. Either way the die is spent first. REJECTS treating
        // q == 0 as "pays nothing", and REJECTS skipping the roll on the early exit.
        [Fact]
        public void NoRidesAndNoFeeIsZeroButNoRidesAndAnyFeeRefuses()
        {
            var free = new Dice(0);
            Assert.Equal(0, VisitorEntrance.FeeVerdict(0, Money.Zero, free));
            Assert.Single(free.Bounds);
            Assert.Equal(-2, VisitorEntrance.FeeVerdict(0, Money.FromPounds(1), new Dice(0)));
            Assert.Equal(1, VisitorEntrance.FeeVerdict(100, Money.Zero, new Dice(0)));   // rides, no fee: a bargain
        }

        // ───────────────────────── 37: pay ─────────────────────────

        // ⭐ 45 IS SET FIRST, and the reset of target, lane scratch, QUEUE BIT and speed happens before
        // the fee is even read. The queue bit is READ (0x80090F14) and not in §2.6. REJECTS leaving the
        // guest "queued" through the gate, REJECTS restoring the speed only on payment, and REJECTS
        // charging the bank without charging the guest.
        [Fact]
        public void PayingSetsWalkInFirstAndClearsTargetLaneBitAndSpeed()
        {
            var g = Guest(VisitorState.PayEntryFee, 1);
            g.HasTarget = true; g.InQueue = true; g.WalkSpeed = 15; g.Money = Money.FromPounds(100);
            var w = new World(); w.Intensities.Add(100);
            var dice = new Dice(0);

            Assert.Equal(PayOutcome.Paid, VisitorEntrance.PayEntryFee(g, w, dice));
            State(g, 45);
            Assert.False(g.HasTarget); Assert.Equal(0, g.GateScratch); Assert.False(g.InQueue);
            Assert.Equal(22, g.WalkSpeed);
            Assert.Equal(Money.FromPounds(60), g.Money);
            Assert.Equal(1, w.Booked); Assert.Equal(1, w.Admissions);
            Assert.Equal(new[] { 5001 }, dice.Bounds);
        }

        // ⚠ STRICTLY MORE THAN THE FEE, and a guest that cannot pay rolls no verdict at all -- but has
        // still had its speed and bit reset, because 45 was set first. REJECTS `>=`, REJECTS rolling
        // before the money test, and REJECTS resetting speed only for payers.
        [Fact]
        public void AGuestWithExactlyTheFeeCannotPayAndRollsNothing()
        {
            var g = Guest(VisitorState.PayEntryFee, 0);
            g.InQueue = true; g.WalkSpeed = 15; g.Money = Money.FromPounds(40);
            var w = new World(); w.Intensities.Add(100);
            var dice = new Dice(0);

            Assert.Equal(PayOutcome.CannotAfford, VisitorEntrance.PayEntryFee(g, w, dice));
            State(g, 38);
            Assert.Empty(dice.Bounds);
            Assert.Equal(Money.FromPounds(40), g.Money);
            Assert.Equal(0, w.Booked); Assert.Equal(0, w.Admissions);
            Assert.False(g.InQueue); Assert.Equal(22, g.WalkSpeed);
        }

        // Only -2 refuses: -1 still pays. With q = 40, £59 is -1 and £60 is -2. REJECTS refusing at -1,
        // and REJECTS booking a refused fee.
        [Theory]
        [InlineData(59, PayOutcome.Paid, 45, 41)]
        [InlineData(60, PayOutcome.Refused, 38, 100)]
        public void OnlyAVerdictOfMinusTwoRefuses(int feePounds, PayOutcome outcome, int state, int moneyAfter)
        {
            var g = Guest(VisitorState.PayEntryFee); g.Money = Money.FromPounds(100);
            var w = new World { Fee = Money.FromPounds(feePounds) }; w.Intensities.Add(100);
            Assert.Equal(outcome, VisitorEntrance.PayEntryFee(g, w, new Dice(0)));
            State(g, state);
            Assert.Equal(Money.FromPounds(moneyAfter), g.Money);
            Assert.Equal(outcome == PayOutcome.Paid ? 1 : 0, w.Booked);
        }

        // ───────────────────────── 45: walk in ─────────────────────────

        // ⚠ THE FLAG TEST IS STICKY: a flag-0x08 tile on row 0 lets a path tile on row 2 through even
        // though row 2 has no flag. REJECTS requiring flag and path on the same tile, which would walk
        // nobody in. Also pins the waypoint at the tile CENTRE and the purpose at 13.
        [Fact]
        public void WalkInTakesTheFirstPathTileAfterAFlagTileEvenOnALaterRow()
        {
            var g = Guest(VisitorState.WalkIn);
            var w = new World { Pos = (21 * 256 + 0x80, 6 * 256 + 0x10) };
            w.Flag8.Add((21, 6)); w.PathTiles.Add((21, 8));

            Assert.Equal(GateWalkOutcome.Stepping, VisitorEntrance.WalkIn(g, w));
            Assert.Equal((21 * 256 + 0x80, 8 * 256 + 0x80), w.Waypoint);
            Assert.Equal(Purpose.Pleased, g.Purpose);
            State(g, 3); Assert.Equal(1, w.Freed);
        }

        // A path tile BEFORE the first flag tile is skipped. REJECTS taking the first path tile
        // regardless of the flag.
        [Fact]
        public void WalkInIgnoresPathTilesBeforeTheFirstFlagTile()
        {
            var g = Guest(VisitorState.WalkIn);
            var w = new World { Pos = (21 * 256, 6 * 256) };
            w.PathTiles.Add((21, 6)); w.Flag8.Add((21, 7)); w.PathTiles.Add((21, 9));

            Assert.Equal(GateWalkOutcome.Stepping, VisitorEntrance.WalkIn(g, w));
            Assert.Equal((21 * 256 + 0x80, 9 * 256 + 0x80), w.Waypoint);
        }

        // Fifteen rows, the guest's own first: row 14 (y + 14) is looked at, row 15 is not. A scan
        // that finds nothing leaves state and purpose alone. REJECTS 14 rows, REJECTS 16, and REJECTS
        // starting one row below the guest.
        [Fact]
        public void WalkInScansExactlyFifteenRowsThenStaysPut()
        {
            var g = Guest(VisitorState.WalkIn);
            var w = new World { Pos = (21 * 256, 6 * 256) };
            w.Flag8.Add((21, 6)); w.PathTiles.Add((21, 6 + 15));

            Assert.Equal(GateWalkOutcome.NoPathTile, VisitorEntrance.WalkIn(g, w));
            State(g, 45); Assert.Equal(Purpose.Spent, g.Purpose);
            Assert.Equal(15, w.TileLookups); Assert.Equal(0, w.Freed);

            w.PathTiles.Add((21, 6 + 14));
            Assert.Equal(GateWalkOutcome.Stepping, VisitorEntrance.WalkIn(g, w));
            Assert.Equal((21 * 256 + 0x80, 20 * 256 + 0x80), w.Waypoint);
        }

        // ⚠ AN EMPTY POOL DOES NOT END THE SCAN: the next path tile is tried too, the purpose stays 13
        // and the waypoints are freed each time. REJECTS returning at the first failed allocation.
        [Fact]
        public void WalkInKeepsScanningWhenThePoolIsEmpty()
        {
            var g = Guest(VisitorState.WalkIn);
            var w = new World { Pos = (21 * 256, 6 * 256), WaypointAvailable = false };
            w.Flag8.Add((21, 6)); w.PathTiles.Add((21, 7)); w.PathTiles.Add((21, 8));

            Assert.Equal(GateWalkOutcome.NoPathTile, VisitorEntrance.WalkIn(g, w));
            State(g, 45); Assert.Equal(Purpose.Pleased, g.Purpose);
            Assert.Equal(2, w.Freed);
            Assert.Equal(2, w.Calls.Count(c => c == "waypoint"));
        }

        // Arrival 13 ends 45's walk: Idle, and the greeting is rand(4) == 0 followed by rand(2) for
        // which sound -- the second die only when the first landed. REJECTS one roll, and REJECTS
        // rolling the variant when the greeting missed.
        [Fact]
        public void ArrivalThirteenRollsTheVariantOnlyWhenTheGreetingFires()
        {
            var greeted = Guest(VisitorState.WalkToWaypoint); greeted.Purpose = Purpose.Pleased;
            var dice = new Dice(0, 1);
            Assert.Equal(Arrival.BackToIdle, VisitorArrival.Tick(greeted, new World(), dice));
            Assert.Equal(new[] { 4, 2 }, dice.Bounds); State(greeted, 0);

            var missed = Guest(VisitorState.WalkToWaypoint); missed.Purpose = Purpose.Pleased;
            dice = new Dice(3);
            VisitorArrival.Tick(missed, new World(), dice);
            Assert.Equal(new[] { 4 }, dice.Bounds); State(missed, 0);
        }

        // ───────────────────────── 38: leave ─────────────────────────

        // No exits: gone at once, bubble off, waypoints freed, and no request made. REJECTS pathing
        // to a point that does not exist, and REJECTS waiting for the guest's tick to despawn.
        [Fact]
        public void LeaveDespawnsAtOnceWithoutExits()
        {
            var g = Guest(VisitorState.LeavingPark); g.Bubble = 0x3A; g.DecisionStagger = 5;
            var w = new World { ExitCount = 0, NowTick = 0 };
            Assert.Equal(GateWalkOutcome.Despawned, VisitorEntrance.Leave(g, w));
            Assert.Equal(1, w.Removed); Assert.Equal(0, g.Bubble); Assert.Equal(1, w.Freed);
            Assert.DoesNotContain("point-1", w.Calls);
        }

        // ⚠ THE 64-TICK GATE IS STAGGERED BY V+0x10: tick 64 is not this guest's, tick 69 is. Then the
        // exit point, purpose 14, gate-side flags 0x21, and SET 11. REJECTS `now % 64 == 0`, REJECTS
        // point 0, and REJECTS 36's plain flags.
        [Fact]
        public void LeaveAsksOnlyOnTheGuestsOwnSixtyFourthTick()
        {
            var g = Guest(VisitorState.LeavingPark); g.DecisionStagger = 5;
            var w = new World { NowTick = 64 };
            Assert.Equal(GateWalkOutcome.NotMyTick, VisitorEntrance.Leave(g, w));
            Assert.Equal(Purpose.Spent, g.Purpose); Assert.Empty(w.Calls);

            w.NowTick = 69;
            Assert.Equal(GateWalkOutcome.Walking, VisitorEntrance.Leave(g, w));
            Assert.Equal((1, 0x21, 0), w.LastPoint);
            Assert.Equal(Purpose.Turnstile14, g.Purpose);
            Assert.Equal(69, g.WaitUntil); State(g, 11); Assert.Equal(0, g.StackDepth);
        }

        // A refusal leaves the purpose at 14 and the guest in 38 for its next tick. REJECTS
        // resetting either.
        [Fact]
        public void LeaveRefusedKeepsPurpose14AndStaysIn38()
        {
            var g = Guest(VisitorState.LeavingPark); g.DecisionStagger = 0;
            var w = new World { NowTick = 128, PathAccepted = false };
            Assert.Equal(GateWalkOutcome.PathRefused, VisitorEntrance.Leave(g, w));
            Assert.Equal(Purpose.Turnstile14, g.Purpose); State(g, 38);
        }

        // ───────────────────────── 48: walk out ─────────────────────────

        // rand(exit count) picks the exit, the request carries plain flags, purpose 9 goes on before
        // the answer. REJECTS rand(count - 1), REJECTS always exit 0, and REJECTS the gate-side flags.
        [Fact]
        public void WalkOutRollsAnExitFromTheCountAndAsksWithPlainFlags()
        {
            var g = Guest(VisitorState.WalkOut);
            var w = new World { ExitCount = 3, NowTick = 200, PathAccepted = false };
            var dice = new Dice(2);
            Assert.Equal(GateWalkOutcome.PathRefused, VisitorEntrance.WalkOut(g, w, dice));
            Assert.Equal(new[] { 3 }, dice.Bounds);
            Assert.Equal((2, 1, 0), w.LastExit);
            Assert.Equal(Purpose.LeavePark, g.Purpose); State(g, 48);

            w.PathAccepted = true;
            Assert.Equal(GateWalkOutcome.Walking, VisitorEntrance.WalkOut(g, w, new Dice(0)));
            Assert.Equal(200, g.WaitUntil); State(g, 11);
        }

        // ───────────────────────── messages ─────────────────────────

        // ⚠ MESSAGE 9 IS NOT GATED ON STATE: the sender filters on 46, the handler does not. REJECTS
        // a handler that checks for 46.
        [Theory]
        [InlineData(46)]
        [InlineData(0)]
        public void Message9SendsAnyGuestToPickALaneWhateverItsState(int from)
        {
            var g = Guest((VisitorState)from);
            Assert.True(VisitorEntrance.OnMessage(g, new World(), EntranceMessage.Admit, new Dice()));
            State(g, 47);
        }

        // Message 1 only acts in 11: state 3, animation 13, and the exit-retry flag cleared. REJECTS
        // acting in any other state, and REJECTS leaving the flag set for the next leave.
        [Fact]
        public void Message1StartsWalkingAndClearsTheRetryFlagOnlyFromEleven()
        {
            var g = Guest(VisitorState.WalkToBin); g.ExitPathRetried = true; g.Animation = 11;
            Assert.True(VisitorEntrance.OnMessage(g, new World(), EntranceMessage.PathReady, new Dice()));
            State(g, 3); Assert.Equal(13, g.Animation); Assert.False(g.ExitPathRetried);

            var waiting = Guest(VisitorState.AtGate); waiting.ExitPathRetried = true;
            Assert.False(VisitorEntrance.OnMessage(waiting, new World(), EntranceMessage.PathReady, new Dice()));
            State(waiting, 46); Assert.True(waiting.ExitPathRetried);
        }

        // ⚠ THE RETRY FLAG GOES ON ONLY AFTER THE RETRY IS ACCEPTED, with grass flags 0x23; a refused
        // retry leaves the guest in 11 with the flag clear; a failure WITH the flag set is Idle.
        // REJECTS setting the flag before asking, REJECTS 0x21, and REJECTS a third try.
        [Fact]
        public void TheExitRetryHappensOnceWithGrassFlagsAndOnlyAfterAcceptance()
        {
            var g = Guest(VisitorState.WalkToBin); g.Purpose = Purpose.Turnstile14;
            var w = new World { NowTick = 300, PathAccepted = false };

            Assert.True(VisitorEntrance.OnMessage(g, w, EntranceMessage.PathFailed, new Dice()));
            Assert.Equal((1, 0x23, 0), w.LastPoint);
            Assert.False(g.ExitPathRetried); State(g, 11);

            w.PathAccepted = true;
            Assert.True(VisitorEntrance.OnMessage(g, w, EntranceMessage.PathFailed, new Dice()));
            Assert.True(g.ExitPathRetried); Assert.Equal(300, g.WaitUntil); State(g, 11);
            Assert.Equal(2, w.Calls.Count(c => c == "point-1"));

            Assert.True(VisitorEntrance.OnMessage(g, w, EntranceMessage.PathFailed, new Dice()));
            State(g, 0); Assert.Equal(2, w.Calls.Count(c => c == "point-1"));
        }

        // Purposes 11 and 15 re-enter the state that made the request; 22 drops the target for Idle.
        // REJECTS the wander arm for any of these, and REJECTS keeping 22's target.
        [Theory]
        [InlineData(Purpose.Turnstile11, 42, true)]
        [InlineData(Purpose.Turnstile15, 36, true)]
        [InlineData(Purpose.ClearTarget, 0, false)]
        public void Message2ReentersTheRequestingStateOrDropsTheTarget(Purpose p, int state, bool target)
        {
            var g = Guest(VisitorState.WalkToBin); g.Purpose = p; g.HasTarget = true;
            var dice = new Dice();
            Assert.True(VisitorEntrance.OnMessage(g, new World(), EntranceMessage.PathFailed, dice));
            State(g, state); Assert.Equal(target, g.HasTarget); Assert.Empty(dice.Bounds);
        }

        // Every purpose without an arm -- the walk out's 9 among them -- costs rand(15) happiness,
        // gains rand(2) boredom, drops the target and wanders; only in 11, and purpose 3 is the queue's
        // and is not answered here. REJECTS acting outside 11, REJECTS rand(2) before rand(15), and
        // REJECTS wandering a queued guest.
        [Fact]
        public void Message2WithAnyOtherPurposeCostsHappinessAndWanders()
        {
            var g = Guest(VisitorState.WalkToBin); g.Purpose = Purpose.LeavePark; g.HasTarget = true;
            var dice = new Dice(7, 1);
            Assert.True(VisitorEntrance.OnMessage(g, new World(), EntranceMessage.PathFailed, dice));
            Assert.Equal(new[] { 15, 2 }, dice.Bounds);
            Assert.Equal(43, g.Happiness); Assert.Equal(21, g.Boredom);
            Assert.False(g.HasTarget); State(g, 5);

            var queued = Guest(VisitorState.WalkToBin); queued.Purpose = Purpose.QueueWalk;
            Assert.False(VisitorEntrance.OnMessage(queued, new World(), EntranceMessage.PathFailed, new Dice()));
            State(queued, 11);

            var elsewhere = Guest(VisitorState.WalkOut); elsewhere.Purpose = Purpose.LeavePark;
            Assert.False(VisitorEntrance.OnMessage(elsewhere, new World(), EntranceMessage.PathFailed, new Dice(0, 0)));
            Assert.Equal(50, elsewhere.Happiness); State(elsewhere, 48);
        }

        // Message 4 despawns from any state; the queue's three are not answered here. REJECTS
        // answering 6, 7 or 10 twice (once here and once in VisitorQueue).
        [Fact]
        public void Message4DespawnsAndTheQueuesMessagesAreNotAnsweredHere()
        {
            var g = Guest(VisitorState.WalkToLaneSlot);
            var w = new World();
            Assert.True(VisitorEntrance.OnMessage(g, w, EntranceMessage.ThrownOut, new Dice()));
            Assert.Equal(1, w.Removed);
            foreach (int id in new[] { 3, 5, 6, 7, 8, 10 })
                Assert.False(VisitorEntrance.OnMessage(g, w, (EntranceMessage)id, new Dice()));
            State(g, 42);
        }

        // ───────────────────────── the gate: admit ─────────────────────────

        // A batch opens only when someone is waiting AND no batch is open; opening zeroes the
        // admitted count; a bus-present batch (2) is not reopened. REJECTS opening on nobody,
        // REJECTS keeping the old admitted count, and REJECTS treating 2 as "none open".
        [Fact]
        public void AdmitOpensABatchOnlyWhenSomeoneWaitsAndNoneIsOpen()
        {
            var idle = new World { Counter80103950 = 0, GateBatch = 0, Counter80103954 = 5 };
            Assert.Equal(0, Turnstile.Admit(idle));
            Assert.Equal(0, idle.GateBatch); Assert.Equal(5, idle.Counter80103954);

            var w = new World { Counter80103950 = 1, GateBatch = 0, Counter80103954 = 5 };
            Turnstile.Admit(w);
            Assert.Equal(1, w.GateBatch); Assert.Equal(0, w.Counter80103954);

            var bus = new World { Counter80103950 = 1, GateBatch = 2, Counter80103954 = 5 };
            Assert.Equal(0, Turnstile.Admit(bus));
            Assert.Equal(2, bus.GateBatch); Assert.Equal(5, bus.Counter80103954);
        }

        // ⭐ ADMISSION IS A BROADCAST to everyone in 46, guests and guards alike, on the same tick;
        // nobody in any other state hears it. REJECTS one per tick, REJECTS guests only, and REJECTS
        // sending to a guest still walking up.
        [Fact]
        public void AdmitBroadcastsNineToEveryoneInFortySixGuestsAndGuards()
        {
            var w = new World { Counter80103950 = 2 };
            var waiting = Guest(VisitorState.AtGate); var also = Guest(VisitorState.AtGate);
            var walking = Guest(VisitorState.WalkToLaneSlot);
            w.GuestList.AddRange(new[] { waiting, walking, also });
            var guard = new StaffMember(StaffKind.Guard); guard.SetState(GuardStates.AtGate);
            var mechanic = new StaffMember(StaffKind.Mechanic);
            w.StaffList.AddRange(new[] { guard, mechanic });

            Assert.Equal(3, Turnstile.Admit(w));
            State(waiting, 47); State(also, 47); State(walking, 42);
            Assert.Equal(new[] { (guard, 9) }, w.StaffMessages);
            Assert.All(w.Messages, m => Assert.Equal(9, m.Id));
        }

        // Eleven crossings stop the sending; the batch then closes only with the bus at the gate, or
        // whenever nobody is left waiting. REJECTS closing a full batch in any other phase, REJECTS
        // sending at 11, and REJECTS closing a batch that is 2.
        [Fact]
        public void AdmitStopsAtElevenAndClosesOnlyWithTheBusAtTheGate()
        {
            var w = new World { Counter80103950 = 3, Counter80103954 = 11, GateBatch = 1, BusPhase = 1 };
            w.GuestList.Add(Guest(VisitorState.AtGate));
            Assert.Equal(0, Turnstile.Admit(w));
            Assert.Equal(1, w.GateBatch);

            w.BusPhase = 2;
            Assert.Equal(0, Turnstile.Admit(w));
            Assert.Equal(0, w.GateBatch);

            var drained = new World { Counter80103950 = 0, Counter80103954 = 5, GateBatch = 1, BusPhase = 1 };
            Turnstile.Admit(drained);
            Assert.Equal(0, drained.GateBatch);

            var bus = new World { Counter80103950 = 0, Counter80103954 = 11, GateBatch = 2, BusPhase = 2 };
            Turnstile.Admit(bus);
            Assert.Equal(2, bus.GateBatch);
        }

        // ───────────────────────── the gate: lanes ─────────────────────────

        // ⚠ THE LANES TAKE TURNS: lane 0 on ticks where now & 31 == 0, lane 1 one tick later. And only
        // a head already in 44 goes through; one still walking up blocks the lane, silently. REJECTS
        // both lanes on the same tick, and REJECTS shuffling the others when nobody was admitted.
        [Fact]
        public void TheTurnstileTakesTurnsBetweenLanesAndOnlyAHeadInFortyFour()
        {
            var w = new World { NowTick = 32 };
            var a = Guest(VisitorState.LaneFront, 0); var b = Guest(VisitorState.LaneFront, 1);
            w.Lanes[0].Add(a); w.Lanes[1].Add(b); w.Counts[0] = 1; w.Counts[1] = 1;

            Assert.Equal(LaneTickOutcome.NotMyTick, Turnstile.LaneTick(w, 1));
            Assert.Equal(LaneTickOutcome.Admitted, Turnstile.LaneTick(w, 0));
            State(a, 37); State(b, 44);

            w.NowTick = 33;
            Assert.Equal(LaneTickOutcome.NotMyTick, Turnstile.LaneTick(w, 0));
            Assert.Equal(LaneTickOutcome.Admitted, Turnstile.LaneTick(w, 1));
            State(b, 37);

            w.NowTick = 64;
            Assert.Equal(LaneTickOutcome.LaneEmpty, Turnstile.LaneTick(w, 0));
            var c = Guest(VisitorState.WalkToLaneSlot, 0); var d = Guest(VisitorState.LaneFront, 0);
            w.Lanes[0].Add(c); w.Lanes[0].Add(d);
            Assert.Equal(LaneTickOutcome.HeadNotAtFront, Turnstile.LaneTick(w, 0));
            State(c, 42); State(d, 44); Assert.Empty(w.Messages);
        }

        // The head goes to 37, the count word drops, the head leaves the list, and everyone STILL
        // LISTED gets message 6 with stagger 0 and target 43 -- routed through the queue's handler, so
        // a member in 44 lands in 43 and a member still in 42 ignores it. REJECTS a stagger, REJECTS
        // sending to the head, and REJECTS a target other than 43.
        [Fact]
        public void AdmissionSendsThirtySevenToTheHeadAndShuffleToEveryoneBehind()
        {
            var w = new World { NowTick = 96 };
            var a = Guest(VisitorState.LaneFront, 0); var b = Guest(VisitorState.LaneFront, 0);
            var c = Guest(VisitorState.WalkToLaneSlot, 0);
            w.Lanes[0].AddRange(new[] { a, b, c }); w.Counts[0] = 3;
            b.WaitUntil = 5;

            Assert.Equal(LaneTickOutcome.Admitted, Turnstile.LaneTick(w, 0));
            State(a, 37); State(b, 43); State(c, 42);
            Assert.Equal(2, w.Counts[0]);
            Assert.Equal(new[] { b, c }, w.Lanes[0]);
            Assert.Equal(new[] { a }, w.RemovedFromLane);
            Assert.Equal(new[] { (b, 6, 0, 43), (c, 6, 0, 43) }, w.Messages);
            Assert.Equal(96, b.WaitUntil);                  // now + 3 x 0
            Assert.Equal(1, w.Queue.Freed);                 // b's waypoints; c's shuffle was dropped
        }

        // ───────────────────────── the whole way in, and out ─────────────────────────

        // ⭐ FROM THE BUS TO IDLE, every state pinned, every counter balanced. The pathfinder's answer
        // (message 1) and the state-3 walk are done by hand, as the queue chain's test does them.
        // REJECTS any chain that skips 46, 47, 42, 44 or 37, REJECTS a counter that does not return to
        // where it started, and REJECTS a lane count left behind by the admitted guest.
        [Fact]
        public void TheWholeWayInFromTheBusToIdle()
        {
            var w = new World { NowTick = 100, Pos = (5376, 1605) };
            w.Intensities.Add(100);
            var g = Guest(VisitorState.SpawnToGate); g.Money = Money.FromPounds(100); g.DecisionStagger = 0;
            w.GuestList.Add(g);
            var seen = new List<VisitorState> { g.State };
            void Note() => seen.Add(g.State);

            Assert.Equal(GateWalkOutcome.Walking, VisitorEntrance.SpawnToGate(g, w)); Note();          // 36 -> 11
            Assert.True(VisitorEntrance.OnMessage(g, w, EntranceMessage.PathReady, new Dice())); Note(); // -> 3
            Assert.Equal(Arrival.AtGate, VisitorArrival.Tick(g, w, new Dice())); Note();               // arrival 15 -> 46
            Assert.Equal(1, w.Counter80103950);
            Assert.Equal(1, Turnstile.Admit(w)); Note();                                               // message 9 -> 47
            Assert.Equal(GateWalkOutcome.Stepping, VisitorEntrance.PickLane(g, w)); Note();            // -> 3
            Assert.Equal(1, w.GateWaypointPoint);
            Assert.Equal(Arrival.WalkingToLaneSlot, VisitorArrival.Tick(g, w, new Dice(0))); Note();   // arrival 16 -> 42
            Assert.Equal(0, w.Counter80103950); Assert.Equal(1, w.Counter80103954);
            Assert.Equal(GateWalkOutcome.Walking, VisitorEntrance.WalkToLaneSlot(g, w)); Note();       // -> 11
            Assert.Equal(1, w.Counts[0]); Assert.Equal(15, g.WalkSpeed);
            VisitorEntrance.OnMessage(g, w, EntranceMessage.PathReady, new Dice()); Note();            // -> 3
            w.Pos = (5504, 1407);                                                                       // one unit short
            Assert.Equal(Arrival.WalkingToLaneSlot, VisitorArrival.Tick(g, w, new Dice())); Note();    // arrival 11 -> 42
            Assert.Equal(GateWalkOutcome.Walking, VisitorEntrance.WalkToLaneSlot(g, w)); Note();       // -> 11
            VisitorEntrance.OnMessage(g, w, EntranceMessage.PathReady, new Dice()); Note();            // -> 3
            w.Pos = (5504, 1408);
            Assert.Equal(Arrival.AtLaneFront, VisitorArrival.Tick(g, w, new Dice())); Note();          // arrival 11 -> 44
            w.NowTick = 128;
            Assert.Equal(LaneTickOutcome.Admitted, Turnstile.LaneTick(w, 0)); Note();                  // -> 37
            Assert.Equal(0, w.Counts[0]); Assert.Empty(w.Lanes[0]);
            Assert.Equal(PayOutcome.Paid, VisitorEntrance.PayEntryFee(g, w, new Dice(0))); Note();     // -> 45
            w.Pos = (5504, 1408); w.Flag8.Add((21, 5)); w.PathTiles.Add((21, 7));
            Assert.Equal(GateWalkOutcome.Stepping, VisitorEntrance.WalkIn(g, w)); Note();              // -> 3
            Assert.Equal(Arrival.BackToIdle, VisitorArrival.Tick(g, w, new Dice(1))); Note();          // arrival 13 -> 0

            Assert.Equal(new[]
            {
                VisitorState.SpawnToGate, VisitorState.WalkToBin, VisitorState.WalkToWaypoint,
                VisitorState.AtGate, VisitorState.PickLane, VisitorState.WalkToWaypoint,
                VisitorState.WalkToLaneSlot, VisitorState.WalkToBin, VisitorState.WalkToWaypoint,
                VisitorState.WalkToLaneSlot, VisitorState.WalkToBin, VisitorState.WalkToWaypoint,
                VisitorState.LaneFront, VisitorState.PayEntryFee, VisitorState.WalkIn,
                VisitorState.WalkToWaypoint, VisitorState.Idle,
            }, seen);
            Assert.Equal(Money.FromPounds(60), g.Money);
            Assert.Equal(1, w.Booked); Assert.Equal(1, w.Admissions);
            Assert.Equal(22, g.WalkSpeed); Assert.False(g.InQueue); Assert.False(g.HasTarget);
            Assert.Equal(0, w.Counter80103950); Assert.Equal(1, w.Counter80103954);
            Assert.Equal(1, w.GateBatch);                    // still open: nobody waiting, but Admit was not run again
        }

        // ⭐ FROM IDLE'S DECISION TO GONE. The exit point is asked for with gate-side flags, the gate
        // line is crossed WITHOUT a lane roll, and the random exit is rolled from the exit count.
        // REJECTS a chain that goes through the lanes, and REJECTS one that skips the gate.
        [Fact]
        public void TheWholeWayOutFromLeavingToGone()
        {
            var w = new World { NowTick = 64, ExitCount = 2 };
            var g = Guest(VisitorState.LeavingPark); g.DecisionStagger = 0; g.HasTarget = true;
            w.GuestList.Add(g);
            var seen = new List<VisitorState> { g.State };
            void Note() => seen.Add(g.State);

            Assert.Equal(GateWalkOutcome.Walking, VisitorEntrance.Leave(g, w)); Note();                // 38 -> 11
            Assert.Equal((1, 0x21, 0), w.LastPoint);
            VisitorEntrance.OnMessage(g, w, EntranceMessage.PathReady, new Dice()); Note();            // -> 3
            Assert.Equal(Arrival.AtGate, VisitorArrival.Tick(g, w, new Dice())); Note();               // arrival 14 -> 46
            Assert.Equal(1, g.GateScratch); Assert.Equal(1, w.Counter80103950); Assert.Equal(4, g.Facing);
            Turnstile.Admit(w); Note();                                                                // -> 47
            Assert.Equal(GateWalkOutcome.Stepping, VisitorEntrance.PickLane(g, w)); Note();            // -> 3
            Assert.Equal(0, w.GateWaypointPoint);
            var dice = new Dice();
            Assert.Equal(Arrival.LeavingThroughGate, VisitorArrival.Tick(g, w, dice)); Note();         // arrival 16 -> 48
            Assert.Empty(dice.Bounds);
            Assert.Equal(0, w.Counter80103950); Assert.Equal(1, w.Counter80103954);
            dice = new Dice(1);
            Assert.Equal(GateWalkOutcome.Walking, VisitorEntrance.WalkOut(g, w, dice)); Note();        // -> 11
            Assert.Equal(new[] { 2 }, dice.Bounds); Assert.Equal((1, 1, 0), w.LastExit);
            VisitorEntrance.OnMessage(g, w, EntranceMessage.PathReady, new Dice()); Note();            // -> 3
            Assert.Equal(Arrival.LeftThePark, VisitorArrival.Tick(g, w, new Dice()));                  // arrival 9: gone

            Assert.Equal(new[]
            {
                VisitorState.LeavingPark, VisitorState.WalkToBin, VisitorState.WalkToWaypoint,
                VisitorState.AtGate, VisitorState.PickLane, VisitorState.WalkToWaypoint,
                VisitorState.WalkOut, VisitorState.WalkToBin, VisitorState.WalkToWaypoint,
            }, seen);
            Assert.Equal(1, w.Removed);
            Assert.Empty(w.Lanes[0]); Assert.Empty(w.Lanes[1]);
            Assert.False(g.HasTarget);
        }
    }
}
