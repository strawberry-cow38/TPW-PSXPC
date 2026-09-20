using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>A ride taking guests on and putting them off (rides.md §4.2).</summary>
    public class RideLoadingTests
    {
        sealed class World : IRideLoadWorld
        {
            public long NowTick { get; set; }
            public int Riders { get; set; }
            public int Capacity { get; set; } = 4;
            public Visitor Head;
            public int Queued;
            public int TotalDays { get; set; }
            public int Boarded, Shuffles, Unloaded;

            public Visitor QueueHead => Head;
            public int QueueCount => Queued;
            public void BoardQueueHead() { Boarded++; Riders++; Head = null; Queued = System.Math.Max(0, Queued - 1); }
            public void ShuffleTheRestForward() => Shuffles++;
            public void UnloadFirstRider() { Unloaded++; Riders--; }
        }

        static Visitor Waiting()
        {
            var v = Visitor.Spawn(new Dice(), 0);
            v.SetState(VisitorState.WaitingInQueue);
            return v;
        }

        sealed class Dice : IRandomSource { public int Next(int n) => 0; }

        // ⚠ ONE GUEST EVERY TWENTY TICKS, ON THE PARK'S CLOCK. REJECTS boarding every tick, which fills
        // a ride instantly and makes the capacity slider free.
        [Fact]
        public void OneGuestBoardsEveryTwentyTicksAndNotBetween()
        {
            var w = new World { Head = Waiting(), Queued = 3 };
            int since = 0;

            for (long t = 1; t < 20; t++) { w.NowTick = t; RideLoading.Load(w, ref since); }
            Assert.Equal(0, w.Boarded);

            w.NowTick = 20;
            RideLoading.Load(w, ref since);
            Assert.Equal(1, w.Boarded);
        }

        // ⭐⭐ A HEAD THAT IS NOT IN STATE 18 BLOCKS THE WHOLE RIDE. The game has no "skip him", so a
        // guest still shuffling forward stalls loading until it arrives. REJECTS taking whoever is at
        // the front regardless, which quietly removes a real cause of a ride that takes nobody.
        [Theory]
        [InlineData(VisitorState.WaitingInQueue, 1)]
        [InlineData(VisitorState.ShuffleForward, 0)]
        [InlineData(VisitorState.JoiningQueue, 0)]
        [InlineData(VisitorState.Idle, 0)]
        public void OnlyAGuestActuallyWaitingIsTakenAboard(VisitorState state, int boarded)
        {
            var g = Visitor.Spawn(new Dice(), 0);
            g.SetState(state);
            var w = new World { Head = g, Queued = 2, NowTick = 20 };
            int since = 0;
            RideLoading.Load(w, ref since);
            Assert.Equal(boarded, w.Boarded);
        }

        // Everyone still queued is told to shuffle up, once per boarding.
        [Fact]
        public void BoardingShufflesTheRestForward()
        {
            var w = new World { Head = Waiting(), Queued = 3, NowTick = 20 };
            int since = 0;
            RideLoading.Load(w, ref since);
            Assert.Equal(1, w.Shuffles);
        }

        // A full ride sets off.
        [Fact]
        public void AFullRideStartsRunning()
        {
            var w = new World { Riders = 4, Capacity = 4, NowTick = 20, Head = Waiting() };
            int since = 0;
            Assert.Equal(AttractionStatus.Running, RideLoading.Load(w, ref since));
            Assert.Equal(0, w.Boarded);                     // and takes nobody else
        }

        // ⭐ A PART-FULL RIDE WAITS FIVE GAME DAYS OF EMPTY QUEUE, then goes anyway. REJECTS both
        // "always wait for a full load" (a quiet park's ride never runs) and "go as soon as the queue
        // empties" (a busy park's ride runs half empty).
        [Fact]
        public void APartFullRideWaitsFiveDaysOfEmptyQueueAndThenGoes()
        {
            var w = new World { Riders = 2, Capacity = 8, NowTick = 20, TotalDays = 100 };
            int since = 100;

            w.TotalDays = 104;
            Assert.Equal(AttractionStatus.Loading, RideLoading.Load(w, ref since));

            w.TotalDays = 105;
            Assert.Equal(AttractionStatus.Running, RideLoading.Load(w, ref since));
        }

        // ⚠ AND THE CLOCK RESETS WHILE ANYONE IS QUEUEING. The five days measure an EMPTY queue, not
        // slow loading. REJECTS starting the timer when loading begins.
        [Fact]
        public void SomebodyInTheQueueKeepsTheTimerPinnedToToday()
        {
            var w = new World { Riders = 2, Capacity = 8, NowTick = 20, TotalDays = 100, Queued = 1 };
            int since = 90;                                  // long overdue on the face of it

            Assert.Equal(AttractionStatus.Loading, RideLoading.Load(w, ref since));
            Assert.Equal(100, since);                        // pinned to today by the queue
        }

        // An empty ride never sets off on the timeout, however long it waits.
        [Fact]
        public void AnEmptyRideNeverSetsOffOnTheTimeout()
        {
            var w = new World { Riders = 0, Capacity = 8, NowTick = 20, TotalDays = 500 };
            int since = 0;
            Assert.Equal(AttractionStatus.Loading, RideLoading.Load(w, ref since));
        }

        // ⚠ UNLOADING IS TWICE AS FAST AS LOADING: every 10 ticks, not 20.
        [Fact]
        public void OneGuestStepsOffEveryTenTicks()
        {
            var w = new World { Riders = 3 };
            for (long t = 1; t < 10; t++) { w.NowTick = t; RideLoading.Unload(w); }
            Assert.Equal(0, w.Unloaded);

            w.NowTick = 10;
            RideLoading.Unload(w);
            Assert.Equal(1, w.Unloaded);
            Assert.Equal(2, w.Riders);
        }

        [Fact]
        public void AnEmptyRideGoesBackToLoading()
        {
            var w = new World { Riders = 0, NowTick = 10 };
            Assert.Equal(AttractionStatus.Loading, RideLoading.Unload(w));
        }

        // ⭐ THE LOADING DOMINATES. Eight seats cost 240 ticks of shuffling people on and off whatever
        // the run length is, which is why doubling the capacity slider does not double throughput.
        [Fact]
        public void TheCycleIsMostlyLoadingAndUnloading()
        {
            int run = RideLoading.CycleTicks(capacity: 8, cyclesPerLoad: 5, phaseTicks: 8);
            Assert.Equal(8 * 20 + 5 * 8 + 8 * 10, run);
            Assert.True(8 * 30 > 5 * 8);                     // the people cost more than the ride
        }

        [Fact]
        public void TheCadencesAreTheOriginals()
        {
            Assert.Equal(20, RideLoading.LoadEveryTicks);
            Assert.Equal(10, RideLoading.UnloadEveryTicks);
            Assert.Equal(5, RideLoading.PartialLoadDays);
            Assert.Equal(VisitorState.WaitingInQueue, RideLoading.Boardable);
        }
    }
}
