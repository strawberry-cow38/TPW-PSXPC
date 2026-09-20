using TPW.Sim;
using Xunit;
using static TPW.Sim.Tests.VisitorRestFixture;

namespace TPW.Sim.Tests
{
    public class VisitorWalkingTests
    {
        // REJECTS normalising diagonals, rounding the fixed multiply, using spawn speed, or changing y before x-facing.
        [Theory]
        [InlineData(3200, 3200, 2701, 2701, 6)]
        [InlineData(2400, 2400, 2675, 2675, 2)]
        [InlineData(2688, 3200, 2688, 2701, 0)]
        [InlineData(2688, 2400, 2688, 2675, 4)]
        public void EachAxisGetsTheTruncatedCurrentSpeedStep(int tx, int ty, int x, int y, int facing)
        {
            var g = Guest(VisitorState.WalkToWaypoint); g.NormalWalkSpeed = 29;
            var w = new World { Timescale = 10000 }; w.Head = w.AddPoint(tx, ty);
            VisitorWalking.Tick(g, w);
            Assert.Equal((x, y), w.GuestPosition); Assert.Equal(facing, g.Facing);
            Assert.Equal(VisitorState.WalkToWaypoint, g.State); Assert.Equal(1, w.PositionWrites);
        }

        // REJECTS overflow widening or arithmetic rather than logical shift of the original low multiply word.
        [Fact]
        public void StepUsesTheUnsignedLowWordOfTheMultiply()
        {
            var g = Guest(VisitorState.WalkToWaypoint); g.WalkSpeed = 30;
            var w = new World { Timescale = 143165577, GuestPosition = (2687, 2687) };
            w.Head = w.AddPoint(2688, 2688);
            VisitorWalking.Tick(g, w);
            Assert.Equal((2687, 2687), w.GuestPosition); // low product = 14, step = 0
            w.Timescale = 0x08000000; // low product 0xF0000000, logical shift remains positive
            VisitorWalking.Tick(g, w);
            Assert.Equal((2688, 2688), w.GuestPosition);
            Assert.Equal(VisitorState.WalkToDestination, g.State);
        }

        // REJECTS overshoot, arriving when only one coordinate matches, or freeing the head in state 3.
        [Fact]
        public void ReachingBothCoordinatesEntersTwoAndLeavesTheHeadForArrival()
        {
            var g = Guest(); g.PushState(VisitorState.WalkToWaypoint);
            var w = new World { GuestPosition = (2680, 2640) }; w.Head = w.AddPoint(2688, 2688);
            VisitorWalking.Tick(g, w);
            Assert.Equal((2688, 2662), w.GuestPosition); Assert.Equal(VisitorState.WalkToWaypoint, g.State);
            VisitorWalking.Tick(g, w); VisitorWalking.Tick(g, w);
            Assert.Equal((2688, 2688), w.GuestPosition); Assert.Equal(VisitorState.WalkToDestination, g.State);
            Assert.Equal(0, g.StackDepth); Assert.True(w.Waypoints.IsAllocated(w.Head));
        }

        // REJECTS clamping the guest back inside the map, committing the invalid position, or leaking any of its chain.
        [Theory]
        [InlineData(0, 128, -64, 128)] [InlineData(128, 0, 128, -64)]
        [InlineData(11263, 128, 11328, 128)] [InlineData(128, 18943, 128, 19008)]
        public void AnOffMapStepFreesTheWholeChainWithoutMoving(int x, int y, int tx, int ty)
        {
            var g = Guest(); g.PushState(VisitorState.WalkToWaypoint);
            var w = new World { GuestPosition = (x, y) };
            w.Head = w.AddPoint(tx, ty); w.Waypoints.SetNext(w.Head, w.AddPoint(0, 0));
            VisitorWalking.Tick(g, w);
            Assert.Equal((x, y), w.GuestPosition); Assert.Equal(0, w.PositionWrites);
            Assert.Equal(-1, w.Head); Assert.Equal(1000, w.Waypoints.FreeCount);
            Assert.Equal(VisitorState.Idle, g.State); Assert.Equal(0, g.StackDepth);
        }

        // REJECTS skipping state 3 on the last Advance, keeping the reached entry, or changing purpose in the base walker.
        [Fact]
        public void AdvancingFreesOneHeadAndAnEmptyThreeReturnsToTheArrivalHandler()
        {
            var g = Guest(VisitorState.WalkToDestination); var w = new World();
            int first = w.AddPoint(2688, 2688), second = w.AddPoint(2944, 2688);
            w.Head = first; w.Waypoints.SetNext(first, second);
            VisitorWalking.Advance(g, w);
            Assert.Equal(second, w.Head); Assert.False(w.Waypoints.IsAllocated(first));
            Assert.True(w.Waypoints.IsAllocated(second)); Assert.Equal(13, g.Animation);
            Assert.Equal(VisitorState.WalkToWaypoint, g.State); Assert.Equal(Purpose.AtBin, g.Purpose);
            g.PushState(VisitorState.WalkToDestination);
            VisitorWalking.Advance(g, w);
            Assert.Equal(-1, w.Head); Assert.Equal(1000, w.Waypoints.FreeCount);
            Assert.Equal(0, g.StackDepth); Assert.Equal(VisitorState.WalkToWaypoint, g.State);
            VisitorWalking.Tick(g, w);
            Assert.Equal(VisitorState.WalkToDestination, g.State); Assert.Equal(0, w.PositionWrites);
        }
    }
}
