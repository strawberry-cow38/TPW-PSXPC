using System;
using System.Collections.Generic;
using TPW.Sim;
using Xunit;
using static TPW.Sim.Tests.VisitorRestFixture;

namespace TPW.Sim.Tests
{
    public class VisitorMessagesTests
    {
        sealed class World : VisitorEntranceTests.World, IVisitorMessageWorld
        {
            public int LeftList;
            public void LeaveQueueList(Visitor g) { LeftList++; Calls.Add("leave-list"); }
            public AttractionStatus RideStatus(Visitor g) => throw new NotSupportedException();
            public int QueueCount(Visitor g) => throw new NotSupportedException();
            public int UpgradeLevel(Visitor g) => throw new NotSupportedException();
            public int Intensity(Visitor g) => throw new NotSupportedException();
            public QueueTile QueueOrigin(Visitor g) => throw new NotSupportedException();
            public IReadOnlyList<QueueTile> QueuePath(Visitor g) => throw new NotSupportedException();
            public int QueueIndexOf(Visitor g) => throw new NotSupportedException();
            public void AppendToQueue(Visitor g) => throw new NotSupportedException();
            public bool TryPathToSlot(Visitor g, int x, int y) => throw new NotSupportedException();
            public bool TryPathToEntrance(Visitor g) => throw new NotSupportedException();
            public bool TryPathToLeavePoint(Visitor g) => throw new NotSupportedException();
            public void CountGuestServed(Visitor g) => throw new NotSupportedException();
            public void ConsumeStock(Visitor g, int units) => throw new NotSupportedException();
            public int StockLevel(Visitor g) => throw new NotSupportedException();
            // The shop half of IShopWorld arrived with the purchase port after these tests were
            // written; none of them reach it, so it throws like everything else here rather than
            // answering a number a test could quietly come to depend on.
            public ShopProduct Product(Visitor g) => throw new NotSupportedException();
            public int SalePrice(Visitor g) => throw new NotSupportedException();
            public int QualitySlider(Visitor g) => throw new NotSupportedException();
            public int SecondSlider(Visitor g) => throw new NotSupportedException();
            public void BookSale(Visitor g, ShopSale sale) => throw new NotSupportedException();
            public SideShowGame Game(Visitor g) => throw new NotSupportedException();
            public void BookPlay(Visitor g, SideShowPlay play) => throw new NotSupportedException();
            public void RecordSatisfaction(Visitor g, int amount) => throw new NotSupportedException();
            public void PostEvent(int id, int value) => throw new NotSupportedException();
            public bool TrySpawnProp(Visitor g) => throw new NotSupportedException();
            public void ReleaseModel(Visitor g) => throw new NotSupportedException();
        }

        // REJECTS leaving purpose 3 unhandled, using SET 0 for a listed guest, or discarding the target needed by 58.
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void QueuePathFailureEitherDropsTheTargetOrSynchronouslyRemovesTheGuest(bool queued)
        {
            var g = Guest(); g.PushState(VisitorState.WalkToBin); g.Purpose = Purpose.QueueWalk; g.InQueue = queued;
            var w = new World(); var dice = new Dice();
            Assert.True(VisitorMessages.OnMessage(g, w, (VisitorMessage)2, dice));
            Assert.Equal(queued ? VisitorState.RemovedFromQueue : VisitorState.Idle, g.State);
            Assert.Equal(queued, g.HasTarget); Assert.Equal(queued, g.Flag1); Assert.False(g.InQueue);
            Assert.Equal(queued ? 1 : 0, w.LeftList); Assert.Equal(queued ? 1 : 0, w.Freed);
            Assert.Equal(0, g.StackDepth); Assert.Equal(50, g.Happiness); Assert.Empty(dice.Bounds);
            if (queued) Assert.Equal(new[] { "leave-list", "free" }, w.Calls);
        }

        // REJECTS state-independent path failure/ready, including the new queue-purpose branch bypassing the 11 gate.
        [Theory]
        [InlineData(1, 3)] [InlineData(2, 3)] [InlineData(2, 14)] [InlineData(2, 19)]
        public void PathMessagesAreIgnoredOutsideEleven(int id, int purpose)
        {
            var g = Guest(VisitorState.WatchEntertainer); g.Purpose = (Purpose)purpose; g.InQueue = true;
            g.ExitPathRetried = true; var w = new World(); var dice = new Dice();
            Assert.False(VisitorMessages.OnMessage(g, w, (VisitorMessage)id, dice));
            Assert.Equal(VisitorState.WatchEntertainer, g.State); Assert.True(g.HasTarget);
            Assert.True(g.InQueue); Assert.True(g.ExitPathRetried); Assert.Empty(w.Calls); Assert.Empty(dice.Bounds);
        }

        // REJECTS routing raw id 1 to failure, retaining retry/stack, or using the idle animation for a ready path.
        [Fact]
        public void ReadyPathClearsRetryAndSetsThreeWithAnimationThirteen()
        {
            var g = Guest(); g.PushState(VisitorState.WalkToBin); g.ExitPathRetried = true;
            var w = new World();
            Assert.True(VisitorMessages.OnMessage(g, w, (VisitorMessage)1, new Dice()));
            Assert.Equal(VisitorState.WalkToWaypoint, g.State); Assert.Equal(13, g.Animation);
            Assert.False(g.ExitPathRetried); Assert.Equal(0, g.StackDepth); Assert.Empty(w.Calls);
        }

        // REJECTS state-gating unconditional rows, losing shuffle parameters, and conflating ejection with queue removal.
        [Theory]
        [InlineData(4, 28, 28, 0, 1, true, true)]
        [InlineData(6, 18, 19, 1, 0, true, true)]
        [InlineData(6, 44, 43, 1, 0, true, true)]
        [InlineData(7, 28, 58, 1, 0, true, false)]
        [InlineData(9, 28, 47, 0, 0, true, true)]
        [InlineData(10, 28, 0, 1, 0, false, false)]
        public void TheWholeTableDispatchesRawIds(int id, int before, int after, int freed, int removed, bool target, bool queue)
        {
            var g = Guest(); g.PushState((VisitorState)before); g.InQueue = true;
            var w = new World { NowTick = 50 };
            Assert.True(VisitorMessages.OnMessage(g, w, (VisitorMessage)id, new Dice(), 7, after));
            Assert.Equal((VisitorState)after, g.State); Assert.Equal(freed, w.Freed); Assert.Equal(removed, w.Removed);
            Assert.Equal(target, g.HasTarget); Assert.Equal(queue, g.InQueue);
            if (id != 4) Assert.Equal(0, g.StackDepth);
            if (id == 6) Assert.Equal(71, g.WaitUntil);
        }

        // REJECTS routing ignored ids through a default path-failure branch or accepting shuffle outside 18/44.
        [Theory]
        [InlineData(0)] [InlineData(3)] [InlineData(5)] [InlineData(6)]
        [InlineData(8)] [InlineData(11)] [InlineData(-1)] [InlineData(256)]
        public void IgnoredRowsAndOutOfStateShuffleHaveNoEffects(int id)
        {
            var g = Guest(VisitorState.WalkToWaypoint); var w = new World(); var dice = new Dice();
            Assert.False(VisitorMessages.OnMessage(g, w, (VisitorMessage)id, dice, 9, 19));
            Assert.Equal(VisitorState.WalkToWaypoint, g.State); Assert.True(g.HasTarget);
            Assert.Empty(w.Calls); Assert.Empty(dice.Bounds);
        }

        // REJECTS treating all path failures as wander or failing to delegate any of the entrance-specific purposes.
        [Theory]
        [InlineData(11, 42, true)] [InlineData(15, 36, true)] [InlineData(22, 0, false)]
        public void SpecialFailurePurposesKeepTheirOwnRows(int purpose, int state, bool target)
        {
            var g = Guest(VisitorState.WalkToBin); g.Purpose = (Purpose)purpose;
            Assert.True(VisitorMessages.OnMessage(g, new World(), (VisitorMessage)2, new Dice()));
            Assert.Equal((VisitorState)state, g.State); Assert.Equal(target, g.HasTarget);
        }

        // REJECTS routing default purposes to queue removal or swapping/skipping either penalty die.
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(5)]
        [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
        [InlineData(12)] [InlineData(13)] [InlineData(16)] [InlineData(17)] [InlineData(18)]
        [InlineData(19)] [InlineData(20)] [InlineData(21)] [InlineData(23)]
        public void EveryOtherFailurePurposeTakesTheWanderPenalty(int purpose)
        {
            var g = Guest(VisitorState.WalkToBin); g.Purpose = (Purpose)purpose;
            var dice = new Dice(14, 1);
            Assert.True(VisitorMessages.OnMessage(g, new World(), (VisitorMessage)2, dice));
            Assert.Equal(36, g.Happiness); Assert.Equal(1, g.Boredom); Assert.False(g.HasTarget);
            Assert.Equal(VisitorState.RandomWander, g.State); Assert.Equal(new[] { 15, 2 }, dice.Bounds); dice.Exhausted();
        }

        // REJECTS making retries unconditional, flagging refused requests, or routing the second failure back to pathfinding.
        [Fact]
        public void ExitFailureKeepsTheEntrancesOneAcceptedRetryRule()
        {
            var g = Guest(VisitorState.WalkToBin); g.Purpose = Purpose.Turnstile14;
            var w = new World { NowTick = 83, PathAccepted = false };
            VisitorMessages.OnMessage(g, w, (VisitorMessage)2, new Dice());
            Assert.False(g.ExitPathRetried); Assert.Equal(VisitorState.WalkToBin, g.State);
            w.PathAccepted = true;
            VisitorMessages.OnMessage(g, w, (VisitorMessage)2, new Dice());
            Assert.Equal((1, 0x23, 0), w.LastPoint); Assert.True(g.ExitPathRetried); Assert.Equal(83, g.WaitUntil);
            VisitorMessages.OnMessage(g, w, (VisitorMessage)2, new Dice());
            Assert.Equal(VisitorState.Idle, g.State); Assert.Equal(2, w.Calls.Count);
        }
    }
}
