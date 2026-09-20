using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>State 2 -- walking, and the 23-entry purpose table (behaviour.md §2.3).</summary>
    public class VisitorArrivalTests
    {
        sealed class Dice : IRandomSource
        {
            readonly Queue<int> _v;
            public Dice(params int[] v) => _v = new Queue<int>(v);
            public int Next(int n) => _v.Count > 0 ? _v.Dequeue() : 1;   // 1 = "the roll missed"
        }

        sealed class World : IArrivalWorld
        {
            public long NowTick { get; set; }
            public bool Waypoint { get; set; }
            public int Steps { get; private set; }
            public int Type { get; set; } = 1;
            public bool Stock { get; set; } = true;
            public bool Entrance { get; set; } = true;
            public bool QueueJoins { get; set; } = true;
            public bool SlotFound { get; set; } = true;
            public bool AtSlot { get; set; }
            public bool Removed { get; private set; }

            public bool HasWaypoint(Visitor g) => Waypoint;
            public void StepToNextWaypoint(Visitor g) => Steps++;
            public int TargetType(Visitor g) => Type;
            public bool TargetHasStock(Visitor g) => Stock;
            public bool TargetHasEntrance(Visitor g) => Entrance;
            public bool TryJoinQueue(Visitor g) => QueueJoins;
            public bool TryQueueSlot(Visitor g, out bool atSlot) { atSlot = AtSlot; return SlotFound; }
            public void RemoveFromPark(Visitor g) => Removed = true;

            // the turnstile view: one lane, empty, entrance at (20, 5)
            public (int X, int Y) Pos { get; set; }
            public List<Visitor> LaneList { get; } = new();
            public int Count0 { get; set; }
            public int Counter80103950 { get; set; }
            public int Counter80103954 { get; set; }
            public (int X, int Y) Position(Visitor g) => Pos;
            public MapTile EntranceTile(int lane) => new MapTile(20, 5);
            public IReadOnlyList<Visitor> Lane(int lane) => LaneList;
            public void AppendToLane(int lane, Visitor g) => LaneList.Add(g);
            public int LaneCount(int lane) => Count0;
            public void SetLaneCount(int lane, int value) => Count0 = value;
        }

        static Visitor Guest(Purpose p)
        {
            var v = Visitor.Spawn(new Dice(), 0);
            v.Happiness = 50; v.Tiredness = 0; v.Rubbish = 50;
            v.Purpose = p; v.HasTarget = true;
            return v;
        }

        // While a waypoint remains this is just walking, and tiredness is a per-tick 1-in-10 roll --
        // not per tile and not per second. REJECTS tiring the guest on arrival instead of en route.
        [Fact]
        public void WalkingStepsAndOccasionallyTires()
        {
            var g = Guest(Purpose.AtAttraction);
            var world = new World { Waypoint = true };

            Assert.Equal(Arrival.StillWalking, VisitorArrival.Tick(g, world, new Dice(0)));
            Assert.Equal(1, g.Tiredness);                 // the 1-in-10 landed
            Assert.Equal(1, world.Steps);

            Assert.Equal(Arrival.StillWalking, VisitorArrival.Tick(g, world, new Dice(5)));
            Assert.Equal(1, g.Tiredness);                 // and missed
            Assert.Equal(2, world.Steps);

            // Nothing has been consumed: the guest is still on its errand.
            Assert.Equal(Purpose.AtAttraction, g.Purpose);
        }

        // ⭐ ARRIVAL SPENDS THE PURPOSE. A second arrival tick must do nothing, and that is what stops a
        // guest emptying its hands twice or joining a queue twice. REJECTS leaving the purpose set.
        [Fact]
        public void ArrivingConsumesThePurposeSoASecondTickIsANoOp()
        {
            var g = Guest(Purpose.AtBin);
            var world = new World();

            Assert.Equal(Arrival.EmptiedRubbish, VisitorArrival.Tick(g, world, new Dice()));
            Assert.Equal(0, g.Rubbish);
            Assert.Equal(Purpose.Spent, g.Purpose);

            g.Rubbish = 40;
            Assert.Equal(Arrival.NoOp, VisitorArrival.Tick(g, world, new Dice()));
            Assert.Equal(40, g.Rubbish);                  // untouched the second time
        }

        // ⚠ THREE ARMS KEEP THEIR PURPOSE: leaving the park and the two turnstile ones. Everything else
        // spends it. REJECTS spending it unconditionally, which would strand a guest mid-exit.
        [Theory]
        [InlineData(Purpose.LeavePark)]
        [InlineData(Purpose.Turnstile14)]
        [InlineData(Purpose.Turnstile15)]
        public void LeavingAndTheTwoTurnstilePurposesSurviveArrival(Purpose p)
        {
            var g = Guest(p);
            VisitorArrival.Tick(g, new World(), new Dice());
            Assert.Equal(p, g.Purpose);
        }

        // ⭐ THE DWELL TIME DIFFERS BY TYPE. A shop is a flat 120 ticks; a stall is 120 plus up to 300
        // more, so stalls hold a guest much longer and for a variable time. REJECTS one dwell for both.
        [Fact]
        public void AShopHoldsAGuestForAFlatTimeAndAStallForAVariableOne()
        {
            var shopper = Guest(Purpose.AtAttraction);
            var shop = new World { NowTick = 1000, Type = 2 };
            Assert.Equal(Arrival.UsingAttraction, VisitorArrival.Tick(shopper, shop, new Dice()));
            Assert.Equal(1120, shopper.WaitUntil);
            Assert.Equal(VisitorState.UsingAttraction, shopper.State);

            var player = Guest(Purpose.AtAttraction);
            var stall = new World { NowTick = 1000, Type = 5 };
            Assert.Equal(Arrival.UsingAttraction, VisitorArrival.Tick(player, stall, new Dice(200)));
            Assert.Equal(1320, player.WaitUntil);         // 120 + the rand(300)
        }

        // Anything that is not a shop or a stall is a ride, and a ride means the queue.
        [Fact]
        public void ARideSendsTheGuestToTheQueue()
        {
            var g = Guest(Purpose.AtAttraction);
            var world = new World { NowTick = 500, Type = 1 };
            Assert.Equal(Arrival.JoinedQueue, VisitorArrival.Tick(g, world, new Dice()));
            Assert.Equal(VisitorState.JoiningQueue, g.State);
            Assert.Equal(500, g.WaitUntil);
        }

        // Two separate ways a ride arrival falls through to Idle, and they differ: no entrance clears
        // the target, a refused queue does not.
        [Fact]
        public void ARideWithNoEntranceClearsTheTargetButARefusedQueueDoesNot()
        {
            var noEntrance = Guest(Purpose.AtAttraction);
            Assert.Equal(Arrival.BackToIdle,
                VisitorArrival.Tick(noEntrance, new World { Type = 1, Entrance = false }, new Dice()));
            Assert.Equal(VisitorState.Idle, noEntrance.State);
            Assert.False(noEntrance.HasTarget);

            var refused = Guest(Purpose.AtAttraction);
            Assert.Equal(Arrival.BackToIdle,
                VisitorArrival.Tick(refused, new World { Type = 1, QueueJoins = false }, new Dice()));
            Assert.Equal(VisitorState.Idle, refused.State);
            Assert.True(refused.HasTarget);               // still wants it
        }

        // A guest that arrives with no target at all just stands around.
        [Fact]
        public void NoTargetMeansStandAround()
        {
            var g = Guest(Purpose.AtAttraction);
            Assert.Equal(Arrival.BackToIdle, VisitorArrival.Tick(g, new World { Type = 0 }, new Dice()));
            Assert.Equal(VisitorState.Idle, g.State);
        }

        // ⭐ PURPOSES 3 AND 10 DIFFER IN ONE BRANCH ONLY: what happens when the guest is not yet standing
        // on its slot. Same lookup, same failure path, opposite answer there. REJECTS treating them as
        // the same purpose.
        [Fact]
        public void TheTwoQueuePurposesDifferOnlyWhenNotYetAtTheSlot()
        {
            var atSlot = new World { SlotFound = true, AtSlot = true };
            var notYet = new World { SlotFound = true, AtSlot = false };

            var a = Guest(Purpose.QueueWalk);
            var b = Guest(Purpose.QueueShuffle);
            Assert.Equal(Arrival.WaitingInQueue, VisitorArrival.Tick(a, atSlot, new Dice()));
            Assert.Equal(Arrival.WaitingInQueue, VisitorArrival.Tick(b, atSlot, new Dice()));
            Assert.Equal(VisitorState.WaitingInQueue, a.State);
            Assert.Equal(VisitorState.WaitingInQueue, b.State);   // identical when on the slot

            var c = Guest(Purpose.QueueWalk);
            var d = Guest(Purpose.QueueShuffle);
            Assert.Equal(Arrival.ShufflingForward, VisitorArrival.Tick(c, notYet, new Dice()));
            Assert.Equal(Arrival.JoinedQueue, VisitorArrival.Tick(d, notYet, new Dice()));
            Assert.Equal(VisitorState.ShuffleForward, c.State);
            Assert.Equal(VisitorState.JoiningQueue, d.State);     // and opposite when not
        }

        // Losing your place in the queue drops the target and puts you back to Idle.
        [Fact]
        public void LosingTheQueueSlotSendsTheGuestBackToIdle()
        {
            var g = Guest(Purpose.QueueWalk);
            Assert.Equal(Arrival.BackToIdle,
                VisitorArrival.Tick(g, new World { SlotFound = false }, new Dice()));
            Assert.Equal(VisitorState.Idle, g.State);
            Assert.False(g.HasTarget);
        }

        // Leaving removes the guest from the park and clears its bubble on the way out.
        [Fact]
        public void LeavingTakesTheGuestOutOfThePark()
        {
            var g = Guest(Purpose.LeavePark); g.Bubble = 0x3A;
            var world = new World();
            Assert.Equal(Arrival.LeftThePark, VisitorArrival.Tick(g, world, new Dice()));
            Assert.True(world.Removed);
            Assert.Equal(0, g.Bubble);
        }

        // ⚠ THE NO-OP PURPOSES ARE DOCUMENTED, NOT MISSING. The report lists 2, 5-8, 17, 18, 20, 21 and
        // anything past 22 as doing nothing, so this is the original's behaviour rather than a gap here.
        [Theory]
        [InlineData(Purpose.Spent)]
        [InlineData((Purpose)5)]
        [InlineData((Purpose)17)]
        [InlineData((Purpose)21)]
        [InlineData((Purpose)30)]
        public void TheDocumentedNoOpPurposesDoNothing(Purpose p)
        {
            var g = Guest(p); g.SetState(VisitorState.Idle);
            Assert.Equal(Arrival.NoOp, VisitorArrival.Tick(g, new World(), new Dice()));
            Assert.Equal(VisitorState.Idle, g.State);
            Assert.Equal(50, g.Rubbish);
        }

        // The six turnstile arms are tested with the rest of the entrance, in VisitorEntranceTests.
    }
}
