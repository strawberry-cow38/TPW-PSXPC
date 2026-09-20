using System;
using TPW.Sim;
using Xunit;
using static TPW.Sim.Tests.VisitorRestFixture;

namespace TPW.Sim.Tests
{
    public class VisitorActivityTests
    {
        // REJECTS >= deadline, early allocation, early nausea reset, and consuming placement dice while waiting.
        [Theory]
        [InlineData(59)] [InlineData(60)]
        public void VomitingWaitsThroughTheDeadline(int now)
        {
            var g = Guest(VisitorState.Vomiting); g.WaitUntil = 60;
            var w = new World { NowTick = now }; var dice = new Dice();
            Assert.False(VisitorActivity.Vomit(g, w, dice));
            Assert.Equal(VisitorState.Vomiting, g.State); Assert.Equal(30, g.Nausea);
            Assert.Equal(12, g.Animation); Assert.Equal(0, w.Allocations); Assert.Empty(dice.Bounds);
        }

        // REJECTS symmetric ±100, whole-tile offsets, swapped dice, wrong kind, POP, and treating vomit as rubbish disposal.
        [Fact]
        public void VomitingPlacesTheAllocatedObjectAndResetsOnlyItsOwnStats()
        {
            var g = Guest(); g.PushState(VisitorState.Vomiting); g.WaitUntil = 60; g.Rubbish = 77;
            var w = new World { NowTick = 61 }; var dice = new Dice(0, 199);
            Assert.True(VisitorActivity.Vomit(g, w, dice));
            Assert.Equal((w.Litter, 2588, 2787, 0x9E), w.Placed);
            Assert.Equal(1, w.Allocations); Assert.Equal(1, w.Placements);
            Assert.Equal(new[] { 200, 200 }, dice.Bounds); dice.Exhausted();
            Assert.Equal(0, g.Nausea); Assert.Equal(11, g.Animation);
            Assert.Equal(VisitorState.Idle, g.State); Assert.Equal(0, g.StackDepth);
            Assert.Equal(77, g.Rubbish); Assert.Equal(50, g.Happiness); Assert.True(g.HasTarget);
        }

        // REJECTS retrying forever on allocation failure, resetting only after a successful spawn, or rolling on failure.
        [Fact]
        public void AllocationFailureStillEndsVomitingWithoutPlacementDice()
        {
            var g = Guest(VisitorState.Vomiting); var w = new World { AllocationSucceeds = false };
            var dice = new Dice();
            Assert.True(VisitorActivity.Vomit(g, w, dice));
            Assert.Equal(1, w.Allocations); Assert.Equal(0, w.Placements); Assert.Empty(dice.Bounds);
            Assert.Equal(0, g.Nausea); Assert.Equal(11, g.Animation); Assert.Equal(VisitorState.Idle, g.State);
        }

        // REJECTS widening the original halfword stores for either litter coordinate.
        [Fact]
        public void VomitPositionWrapsAsSignedHalfwords()
        {
            var w = new World { GuestPosition = (32760, -32760) };
            VisitorActivity.Vomit(Guest(), w, new Dice(199, 0));
            Assert.Equal(-32677, w.Placed.X); Assert.Equal(32676, w.Placed.Y);
        }

        // REJECTS y-first/diagonal facing and preserving facing when guest and entertainer coincide.
        [Theory]
        [InlineData(-1, 9, 2)] [InlineData(1, -9, 6)]
        [InlineData(0, 1, 0)] [InlineData(0, -1, 4)] [InlineData(0, 0, 4)]
        public void WatchingFacesTheEntertainerOnEveryTick(int dx, int dy, int facing)
        {
            var g = Guest(); var e = new StaffMember(StaffKind.Entertainer);
            e.SetState(EntertainerStates.Entertaining); g.WatchedEntertainer = e;
            g.PushState(VisitorState.WatchEntertainer); g.WaitUntil = 8;
            var w = new World { StaffPosition = (2688 + dx * 256, 2688 + dy * 256) };
            Assert.False(VisitorActivity.Watch(g, w));
            Assert.Equal(facing, g.Facing); Assert.Equal(2, g.Animation);
            Assert.Same(e, g.WatchedEntertainer); Assert.Equal(50, g.Happiness);
            Assert.Equal(VisitorState.WatchEntertainer, g.State);
            w.StaffPosition = (2432, 2688);
            VisitorActivity.Watch(g, w); Assert.Equal(2, g.Facing);
        }

        // REJECTS facing from raw sub-tile offsets: slot 10 reduces BOTH positions to tiles first.
        [Fact]
        public void WatchingSomeoneOnTheSameTileFacesFourDespiteTheirRawOffsets()
        {
            var g = Guest(); var e = new StaffMember(StaffKind.Entertainer);
            e.SetState(EntertainerStates.Entertaining); g.WatchedEntertainer = e; g.WaitUntil = 8;
            var w = new World { StaffPosition = (2691, 2692) };
            Assert.False(VisitorActivity.Watch(g, w));
            Assert.Equal(4, g.Facing);
        }

        // REJECTS requiring BOTH timeout and stopped performance, SET instead of POP, fixed absolute cooldown,
        // lost attraction target, and skipping facing on the final tick.
        [Theory]
        [InlineData(true, 9)] [InlineData(false, 7)] [InlineData(false, 8)]
        public void EitherEndConditionResumesTheJourneyAndPaysTheBonus(bool performing, int now)
        {
            var g = Guest(VisitorState.WalkToWaypoint); g.WaitUntil = 8;
            var e = new StaffMember(StaffKind.Entertainer);
            e.SetState(performing ? EntertainerStates.Entertaining : StaffState.Idle);
            g.WatchedEntertainer = e; g.PushState(VisitorState.WatchEntertainer);
            var w = new World { NowTick = now, StaffPosition = (3200, 2600) };
            Assert.True(VisitorActivity.Watch(g, w));
            Assert.Equal(VisitorState.WalkToWaypoint, g.State); Assert.Equal(0, g.StackDepth);
            Assert.Equal(55, g.Happiness); Assert.Equal(13, g.Animation); Assert.Equal(6, g.Facing);
            Assert.Null(g.WatchedEntertainer); Assert.Equal(now + 900, g.EntertainerNotBefore);
            Assert.True(g.HasTarget); Assert.Equal(Purpose.AtBin, g.Purpose); Assert.Equal(8, g.WaitUntil);
        }

        // REJECTS a second watch during the exact cooldown boundary, and failure to connect needs selection to completion.
        [Fact]
        public void NeedsPushAndWatchPopShareTheSelectedEntertainerAndCooldown()
        {
            var g = Guest(VisitorState.WalkToWaypoint); g.Happiness = 98;
            var e = new StaffMember(StaffKind.Entertainer) { Skill = 1 };
            var w = new World { Influence = TileInfluence.Entertainer };
            w.Entertainers.Add((e, 5));
            VisitorNeeds.Tick(g, w, new Dice());
            Assert.Same(e, g.WatchedEntertainer); Assert.Equal(368, g.WaitUntil);
            Assert.True(VisitorActivity.Watch(g, w)); // staff Idle: immediately ends
            Assert.Equal(100, g.Happiness); Assert.Equal(908, g.EntertainerNotBefore);
            w.NowTick = 908; g.DecisionStagger = 4;
            VisitorNeeds.Tick(g, w, new Dice()); Assert.Null(g.WatchedEntertainer);
            w.NowTick = 909; g.DecisionStagger = 5;
            VisitorNeeds.Tick(g, w, new Dice()); Assert.Same(e, g.WatchedEntertainer);
        }

        // REJECTS a hidden default entertainer/position when the state has lost its required target.
        [Fact]
        public void MissingWatchTargetIsAnExplicitHostError()
            => Assert.Throws<InvalidOperationException>(() => VisitorActivity.Watch(Guest(), new World()));

        // REJECTS 1/11 as the moving states, losing combined-bit effects, and applying them after the watch push.
        [Theory]
        [InlineData(1, 55, 32)] [InlineData(2, 53, 35)] [InlineData(3, 53, 35)]
        [InlineData(5, 55, 32)] [InlineData(11, 55, 32)] [InlineData(28, 55, 32)]
        public void InfluenceUsesTheRawMovingStateNumbersBeforeInterrupting(int state, int happy, int nausea)
        {
            var g = Guest((VisitorState)state);
            var w = new World { Influence = (TileInfluence)7 };
            w.Entertainers.Add((new StaffMember(StaffKind.Entertainer), 0));
            VisitorNeeds.Tick(g, w, new Dice());
            Assert.Equal(happy, g.Happiness); Assert.Equal(nausea, g.Nausea);
        }

        // REJECTS last-equal wins, first-entry selection, performance filtering, range limits, and unmasked skill bytes.
        [Theory]
        [InlineData(0, 308)] [InlineData(7, 728)] [InlineData(9, 368)]
        public void TheNearestEntertainerWinsAndTiesKeepListOrder(int skill, int deadline)
        {
            var g = Guest(); g.PushState(VisitorState.WalkToWaypoint);
            var nearest = new StaffMember(StaffKind.Entertainer) { Skill = skill };
            var w = new World { Influence = TileInfluence.Entertainer };
            w.Entertainers.Add((new StaffMember(StaffKind.Entertainer), 10000));
            w.Entertainers.Add((nearest, 9000));
            w.Entertainers.Add((new StaffMember(StaffKind.Entertainer), 9000));
            VisitorNeeds.Tick(g, w, new Dice());
            Assert.Same(nearest, g.WatchedEntertainer); Assert.Equal(deadline, g.WaitUntil);
            Assert.Equal(2, g.StackDepth); Assert.Equal(VisitorState.WatchEntertainer, g.State);
        }

        // REJECTS removing any watch gate or enumerating the staff list before all gates have passed.
        [Theory]
        [InlineData("queue")] [InlineData("depth")] [InlineData("deadline")]
        [InlineData("watching")] [InlineData("flag")] [InlineData("stagger")]
        public void EveryGateBlocksSelectionIndependently(string gate)
        {
            var g = Guest(); var w = new World { Influence = TileInfluence.Entertainer };
            if (gate == "queue") w.Queueing = true;
            if (gate == "depth") { g.PushState(VisitorState.MajorDecision); g.PushState(VisitorState.WalkToWaypoint); }
            if (gate == "deadline") g.EntertainerNotBefore = 8;
            if (gate == "watching") g.SetState(VisitorState.WatchEntertainer);
            if (gate == "flag") w.Influence = TileInfluence.Pleasant;
            if (gate == "stagger") g.DecisionStagger = 1;
            var state = g.State; int depth = g.StackDepth;
            w.Entertainers.Add((new StaffMember(StaffKind.Entertainer), 0));
            VisitorNeeds.Tick(g, w, new Dice());
            Assert.Null(g.WatchedEntertainer); Assert.Equal(0, w.Scans);
            Assert.Equal(state, g.State); Assert.Equal(depth, g.StackDepth);
        }

        // REJECTS entering 28 on a stale influence object when no entertainer remains in the list.
        [Fact]
        public void AnEmptyEntertainerListCannotPushAWatch()
        {
            var g = Guest(); var w = new World { Influence = TileInfluence.Entertainer };
            VisitorNeeds.Tick(g, w, new Dice());
            Assert.Equal(1, w.Scans); Assert.Null(g.WatchedEntertainer);
            Assert.Equal(VisitorState.RandomWander, g.State); Assert.Equal(0, g.StackDepth);
        }
    }
}
