using System;
using System.Linq;
using TPW.Sim;
using Xunit;
using static TPW.Sim.Tests.VisitorRestFixture;

namespace TPW.Sim.Tests
{
    public class VisitorWanderTests
    {
        // REJECTS POP/PUSH or doing state 5's random walk on state 1's tick.
        [Fact]
        public void StateOneOnlySetsFiveAndClearsTheStack()
        {
            var g = Guest(); g.PushState(VisitorState.Wander); var w = new World(); var dice = new Dice();
            Assert.Equal(WanderOutcome.ChangedToRandomWander, VisitorWander.Tick(g, w, dice));
            Assert.Equal(VisitorState.RandomWander, g.State); Assert.Equal(0, g.StackDepth);
            Assert.Empty(w.PathReads); Assert.Empty(dice.Bounds);
        }

        // REJECTS a forced minimum of one step, retaining an old chain, or returning directly to Idle on an empty walk.
        [Theory]
        [InlineData(0)] [InlineData(9)]
        public void ZeroStepsOrAnIsolatedTileStillEntersThreeWithAnEmptyChain(int steps)
        {
            var g = Guest(); g.PushState(VisitorState.RandomWander);
            var w = new World { Links = (x, y) => 0 }; w.Head = w.AddPoint(3000, 3000);
            var dice = new Dice(steps);
            Assert.Equal(WanderOutcome.WaypointsReady, VisitorWander.Tick(g, w, dice));
            Assert.Equal(1000, w.Waypoints.FreeCount); Assert.Equal(-1, w.Head);
            Assert.Equal(Purpose.Finished, g.Purpose); Assert.Equal(VisitorState.WalkToWaypoint, g.State);
            Assert.Equal(0, g.StackDepth); Assert.Equal(new[] { 10 }, dice.Bounds); dice.Exhausted();
        }

        // REJECTS wrong table width/order, uniform turns, and the conventional cumulative > roll boundary.
        // These partitions are read independently from the 16 words and the sltu at 0x80093038.
        [Theory]
        [InlineData(0, 100, 140, 150)] [InlineData(1, 40, 140, 180)]
        [InlineData(2, 10, 50, 150)] [InlineData(3, 40, 50, 90)]
        public void EveryTurnTableRowHasItsOriginalRollPartitions(int row, int end0, int end1, int end2)
        {
            var destinations = new[] { (2944, 2688), (2688, 2944), (2432, 2688), (2688, 2432) };
            for (int roll = 0; roll < 190; roll++)
            {
                var g = Guest(); var w = new World(); var dice = new Dice(1, row, roll);
                VisitorWander.Tick(g, w, dice);
                int direction = roll <= end0 ? 0 : roll <= end1 ? 1 : roll <= end2 ? 2 : 3;
                Assert.Equal(new[] { destinations[direction] }, w.Waypoints.Chain(w.Head));
                Assert.Equal(new[] { 10, 4, 190 }, dice.Bounds); dice.Exhausted();
            }
        }

        // REJECTS 1<<direction, checking the neighbour's links, or treating an unconnected path as a candidate.
        [Theory]
        [InlineData(4, 2944, 2688)] [InlineData(16, 2688, 2944)]
        [InlineData(64, 2432, 2688)] [InlineData(1, 2688, 2432)]
        public void LinksComeFromTheCurrentTileInTheDecodedDirectionOrder(int bit, int x, int y)
        {
            var g = Guest(); var w = new World { Links = (tx, ty) => tx == 10 && ty == 10 ? bit : 0 };
            var dice = new Dice(1, 0, 0);
            VisitorWander.Tick(g, w, dice);
            Assert.Equal(new[] { (x, y) }, w.Waypoints.Chain(w.Head)); dice.Exhausted();
        }

        // REJECTS disconnected terrain, accepting path links alone, and querying tiles outside any of the four edges.
        [Theory]
        [InlineData(0, 0, 64)] [InlineData(0, 0, 1)]
        [InlineData(43, 73, 4)] [InlineData(43, 73, 16)] [InlineData(10, 10, 4)]
        public void AnEdgeOrNonPathNeighbourCannotBecomeAWaypoint(int x, int y, int link)
        {
            var w = new World { GuestPosition = (x * 256 + 128, y * 256 + 128), Links = (_, _) => link };
            w.Path = (tx, ty) => tx == x && ty == y;
            VisitorWander.Tick(Guest(), w, new Dice(1));
            Assert.Equal(-1, w.Head);
            Assert.All(w.PathReads, tile => { Assert.InRange(tile.X, 0, 43); Assert.InRange(tile.Y, 0, 73); });
        }

        // REJECTS rerolling at 20, drawing a weighted die on the keep branch, and choosing one direction for the whole walk.
        [Theory]
        [InlineData(19, 2944, 2944)] [InlineData(20, 3200, 2688)] [InlineData(99, 3200, 2688)]
        public void EightyPercentKeepsTheLastDirectionWhenItIsStillAvailable(int keepRoll, int x, int y)
        {
            var g = Guest(); var w = new World();
            var dice = keepRoll < 20 ? new Dice(2, 0, 0, keepRoll, 101) : new Dice(2, 0, 0, keepRoll);
            VisitorWander.Tick(g, w, dice);
            Assert.Equal(new[] { (2944, 2688), (x, y) }, w.Waypoints.Chain(w.Head));
            Assert.Equal(keepRoll < 20 ? new[] { 10, 4, 190, 100, 190 } : new[] { 10, 4, 190, 100 }, dice.Bounds);
            dice.Exhausted();
        }

        // REJECTS retaining a blocked direction or drawing rand(100)/a fresh row when it is unavailable.
        [Fact]
        public void ABlockedForwardDirectionImmediatelyUsesThePreviousRowsWeights()
        {
            var w = new World { Links = (x, y) => x == 10 ? 4 : 16 };
            var dice = new Dice(2, 0, 0, 39);
            VisitorWander.Tick(Guest(), w, dice);
            Assert.Equal(new[] { (2944, 2688), (2944, 2944) }, w.Waypoints.Chain(w.Head));
            Assert.Equal(new[] { 10, 4, 100, 40 }, dice.Bounds); dice.Exhausted();
        }

        // REJECTS compressing a straight wander to corners or capping the rand(10) walk below nine steps.
        [Fact]
        public void TheNineStepWalkKeepsEveryTileAndConsumesEightKeepDice()
        {
            var w = new World(); var dice = new Dice(9, 0, 0, 20, 20, 20, 20, 20, 20, 20, 20);
            VisitorWander.Tick(Guest(), w, dice);
            Assert.Equal(Enumerable.Range(11, 9).Select(x => (x * 256 + 128, 2688)), w.Waypoints.Chain(w.Head));
            Assert.Equal(new[] { 10, 4, 190, 100, 100, 100, 100, 100, 100, 100, 100 }, dice.Bounds); dice.Exhausted();
        }

        // REJECTS allocating before freeing the old chain, forward allocation, rollback on exhaustion, or leaking the suffix.
        [Fact]
        public void ExhaustionKeepsTheFarEndOfTheWalkAndReusesTheOldChain()
        {
            var w = new World(); w.Head = w.AddPoint(0, 0);
            while (w.Waypoints.FreeCount > 0) w.Waypoints.Alloc();
            var g = Guest(); VisitorWander.Tick(g, w, new Dice(3, 0, 0, 20, 20));
            Assert.Equal(new[] { (3456, 2688) }, w.Waypoints.Chain(w.Head));
            Assert.Equal(0, w.Waypoints.FreeCount); Assert.Equal(VisitorState.WalkToWaypoint, g.State);
        }

        // REJECTS staying in 5 when no pool entry is available; state 3's empty-chain arm drives arrival.
        [Fact]
        public void CompletePoolExhaustionStillInstallsAnEmptyPathWalk()
        {
            var w = new World(); while (w.Waypoints.FreeCount > 0) w.Waypoints.Alloc();
            var g = Guest(); VisitorWander.Tick(g, w, new Dice(1, 0, 0));
            Assert.Equal(-1, w.Head); Assert.Equal(Purpose.Finished, g.Purpose);
            Assert.Equal(VisitorState.WalkToWaypoint, g.State);
        }

        // REJECTS fixed ring radius, using the pathfinder for a nearby tile, or forgetting its tile-centre waypoint/purpose.
        [Theory]
        [InlineData(0, 4)] [InlineData(4, 8)]
        public void AnOffPathGuestFirstTriesTheReportedRing(int roll, int radius)
        {
            var g = Guest(); var w = new World { Path = (_, _) => false, RingFound = true };
            var dice = new Dice(roll);
            Assert.Equal(WanderOutcome.WaypointsReady, VisitorWander.Tick(g, w, dice));
            Assert.Equal(radius, w.RingRadius); Assert.Empty(w.Requests);
            Assert.Equal(new[] { (3200, 3456) }, w.Waypoints.Chain(w.Head));
            Assert.Equal(Purpose.Finished, g.Purpose); Assert.Equal(VisitorState.WalkToWaypoint, g.State);
            Assert.Equal(new[] { 5 }, dice.Bounds); dice.Exhausted();
        }

        // REJECTS proceeding to centre attempts or state 3 when a found ring tile cannot allocate its one waypoint.
        [Fact]
        public void RingAllocationFailureReturnsToIdle()
        {
            var g = Guest(); var w = new World { Path = (_, _) => false, RingFound = true };
            while (w.Waypoints.FreeCount > 0) w.Waypoints.Alloc();
            Assert.Equal(WanderOutcome.BackToIdle, VisitorWander.Tick(g, w, new Dice(0)));
            Assert.Equal(VisitorState.Idle, g.State); Assert.Equal(Purpose.AtBin, g.Purpose); Assert.Empty(w.Requests);
        }

        // REJECTS using width for the y centre, ±10 inclusive, wrong flags, SET 11, or failing to save the request tick.
        [Fact]
        public void CentreFallbackUsesTheReportsMapCentreAndPushesElevenOnAcceptance()
        {
            var g = Guest(); var w = new World { Path = (x, y) => x != 10 || y != 10, AcceptPath = true, NowTick = 91 };
            var dice = new Dice(0, 0, 19);
            Assert.Equal(WanderOutcome.PathRequested, VisitorWander.Tick(g, w, dice));
            Assert.Equal(new[] { (3200, 11904, 3, 0) }, w.Requests);
            Assert.Equal(new[] { 5, 20, 20 }, dice.Bounds); dice.Exhausted();
            Assert.True(g.ExitPathRetried); Assert.Equal(91, g.WaitUntil); Assert.Equal(Purpose.Finished, g.Purpose);
            Assert.Equal(VisitorState.WalkToBin, g.State); Assert.Equal(1, g.StackDepth);
            g.PopState(); Assert.Equal(VisitorState.RandomWander, g.State);
        }

        // REJECTS stopping at the first refused request or adopting the binary's fifth attempt/grass-wander tail.
        [Fact]
        public void ExactlyFourRefusalsReturnToIdleUnderTheRetainedReport()
        {
            var g = Guest(); g.PushState(VisitorState.RandomWander);
            var w = new World { Path = (x, y) => x != 10 || y != 10 };
            var dice = new Dice(4, 0, 0, 1, 1, 2, 2, 19, 19);
            Assert.Equal(WanderOutcome.BackToIdle, VisitorWander.Tick(g, w, dice));
            Assert.Equal(new[] { (3200, 7040, 3, 0), (3456, 7296, 3, 0), (3712, 7552, 3, 0), (8064, 11904, 3, 0) }, w.Requests);
            Assert.Equal(VisitorState.Idle, g.State); Assert.Equal(0, g.StackDepth);
            Assert.False(g.ExitPathRetried); Assert.Equal(Purpose.AtBin, g.Purpose); dice.Exhausted();
        }

        // REJECTS requesting paths to non-path or off-map random targets, while preserving both dice for each attempt.
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void InvalidCentreCandidatesUseAnAttemptWithoutARequest(bool offMap)
        {
            var w = new World { Path = (_, _) => false };
            if (offMap) { w.MapWidth = 4; w.MapHeight = 4; w.GuestPosition = (128, 128); }
            var dice = new Dice(0, 0, 0, 0, 0, 0, 0, 0, 0);
            VisitorWander.Tick(Guest(), w, dice);
            Assert.Empty(w.Requests); dice.Exhausted();
            Assert.Equal(9, dice.Bounds.Count);
            if (offMap) Assert.Single(w.PathReads);
        }
    }
}
