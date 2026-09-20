using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class RollerCoasterTests
    {
        sealed class ZeroRandom : IRandomSource { public int Next(int n) => 0; }
        sealed class World : IRollerCoasterWorld
        {
            public uint NowTick { get; set; } = 20;
            public bool TrackConnected { get; set; } = true;
            public bool PieceChecksPass { get; set; } = true;
            public bool PreviewTrainActive { get; set; }
            public int Riders { get; set; }
            public int Capacity { get; set; } = 8;
            public Visitor QueueHead { get; set; }
            public int PendingPassengers { get; set; }
            public int BoardingBatchSize { get; set; } = 3;
            public bool FreeTrainAvailable { get; set; } = true;
            public bool AnimationComplete;
            public int Scans;
            public List<string> Calls { get; } = new();
            public List<CoasterTrain> Trains { get; } = new();
            public IReadOnlyList<CoasterTrain> ActiveTrains { get { Scans++; return Trains; } }
            public bool AdvanceStationAnimation() { Calls.Add("animate"); return AnimationComplete; }
            public void BoardHeadGuest() { Calls.Add("board"); Riders++; PendingPassengers++; QueueHead = null; }
            public void DispatchPendingPassengers() { Calls.Add("dispatch:" + PendingPassengers); PendingPassengers = 0; }
            public void UnloadTrain(CoasterTrain train) { Calls.Add("unload"); Trains.Remove(train); Riders--; }
            public void Queue(VisitorState state = VisitorState.WaitingInQueue)
            { QueueHead = Visitor.Spawn(new ZeroRandom(), 0); QueueHead.SetState(state); }
        }

        // REJECTS adding another open status or admitting guests to an unconnected coaster; piece validation is not slot 86's field.
        [Fact]
        public void CoasterOpenPredicateIsTheSharedSetWithAConnectionVeto()
        {
            for (int status = 0; status <= 12; status++)
            {
                Assert.False(RollerCoaster.OpenToGuests((AttractionStatus)status, false));
                Assert.Equal(status == 2 || status == 10 || status == 11,
                             RollerCoaster.OpenToGuests((AttractionStatus)status, true));
            }
        }

        // REJECTS omitting any boarding gate, letting a shuffling head board, or treating animation completion as permission.
        [Theory]
        [InlineData(true, true, false, 20u, 0, 18, false, true)]
        [InlineData(true, true, false, 40u, 0, 18, true, true)]
        [InlineData(false, true, false, 20u, 0, 18, true, false)]
        [InlineData(true, false, false, 20u, 0, 18, true, false)]
        [InlineData(true, true, true, 20u, 0, 18, true, false)]
        [InlineData(true, true, false, 10u, 0, 18, true, false)]
        [InlineData(true, true, false, 19u, 0, 18, true, false)]
        [InlineData(true, true, false, 20u, 8, 18, true, false)]
        [InlineData(true, true, false, 20u, 9, 18, true, false)]
        [InlineData(true, true, false, 20u, 0, 19, true, false)]
        [InlineData(true, true, false, 20u, 0, -1, true, false)]
        public void BoardingHasAllItsOwnGates(bool connected, bool piecesPass, bool preview, uint tick,
                                              int riders, int headState, bool animation, bool boards)
        {
            var w = new World { TrackConnected = connected, PieceChecksPass = piecesPass, PreviewTrainActive = preview,
                NowTick = tick, Riders = riders, AnimationComplete = animation };
            if (headState >= 0) w.Queue((VisitorState)headState);
            RollerCoaster.LoadTick(w);
            Assert.Equal(boards ? new[] { "animate", "board" } : new[] { "animate" }, w.Calls);
            Assert.Equal(1, w.Scans);
        }

        // REJECTS dispatch before increment, using maximum ride seats for batch size, max instead of min, or dropping the 16-passenger limit.
        [Theory]
        [InlineData(1, 3, 8, false, 2)]
        [InlineData(2, 3, 8, true, 3)]
        [InlineData(1, 3, 2, true, 2)]
        [InlineData(15, 24, 30, true, 16)]
        [InlineData(3, 2, 8, true, 4)]
        public void ThePendingBatchDispatchesAtTheSmallestApplicableLimit(int pending, int batch, int capacity, bool dispatch, int loaded)
        {
            var w = new World { PendingPassengers = pending, Riders = pending, BoardingBatchSize = batch, Capacity = capacity };
            w.Queue(); RollerCoaster.LoadTick(w);
            Assert.Equal(dispatch ? new[] { "animate", "board", "dispatch:" + loaded } : new[] { "animate", "board" }, w.Calls);
            Assert.Equal(dispatch ? 0 : loaded, w.PendingPassengers);
        }

        // REJECTS a timer starting on first boarding, a 240-update interval, and leaving an unsuccessful attempt overdue.
        [Fact]
        public void PartialDispatchIsARepeating241UpdateAttempt()
        {
            var ride = new RollerCoaster(); var w = new World();
            for (int i = 0; i < 240; i++) ride.UpdateDispatch(w);
            Assert.Equal(240, ride.DispatchTicks); Assert.Empty(w.Calls);
            w.PendingPassengers = 1;
            ride.UpdateDispatch(w);
            Assert.Equal(new[] { "dispatch:1" }, w.Calls); Assert.Equal(0, ride.DispatchTicks);
            ride.DispatchTicks = 240; w.FreeTrainAvailable = false; w.PendingPassengers = 2;
            ride.UpdateDispatch(w);
            Assert.Equal(0, ride.DispatchTicks); Assert.Equal(2, w.PendingPassengers);
            w.FreeTrainAvailable = true; ride.UpdateDispatch(w);
            Assert.Equal(new[] { "dispatch:1" }, w.Calls); Assert.Equal(1, ride.DispatchTicks);
        }

        // REJECTS dispatching empty batches or ignoring the train pool when a batch fills.
        [Fact]
        public void EmptyBatchesAndAnExhaustedPoolCannotDispatch()
        {
            var ride = new RollerCoaster { DispatchTicks = 240 }; var w = new World();
            ride.UpdateDispatch(w); Assert.Empty(w.Calls); Assert.Equal(0, ride.DispatchTicks);
            w.FreeTrainAvailable = false; w.PendingPassengers = 2; w.Queue();
            RollerCoaster.LoadTick(w);
            Assert.Equal(new[] { "animate", "board" }, w.Calls); Assert.Equal(3, w.PendingPassengers);
        }

        // REJECTS a shared unload cadence, stopping after one ready train, losing next during unlink, or unloading the preview train.
        [Fact]
        public void EveryReadyTrainUnloadsEvenOffCadenceAndAlongsideAnUnfinishedTrain()
        {
            var w = new World { NowTick = 19, Riders = 4 };
            var first = new CoasterTrain { ReadyToUnload = true };
            var second = new CoasterTrain { ReadyToUnload = true };
            var travelling = new CoasterTrain();
            var preview = new CoasterTrain { ReadyToUnload = true, IsPreview = true };
            w.Trains.AddRange(new[] { first, second, travelling, preview });
            RollerCoaster.UnloadTick(w);
            Assert.Equal(new[] { "unload", "unload" }, w.Calls);
            Assert.Equal(new[] { travelling, preview }, w.Trains);
        }

        // REJECTS dropping the second running-state scan or moving unloading ahead of boarding (capacity changes).
        [Fact]
        public void RunningLoadsThenScansForUnloadingTwice()
        {
            var w = new World { Riders = 8 }; w.Queue();
            w.Trains.Add(new CoasterTrain { ReadyToUnload = true });
            RollerCoaster.RunTick(w);
            Assert.Equal(new[] { "animate", "unload" }, w.Calls); Assert.Equal(2, w.Scans);
            Assert.NotNull(w.QueueHead);
        }

        // REJECTS treating mesh phase completion or whole-ride emptiness as a train's finish, or admitting preview/launch movement.
        [Theory]
        [InlineData(false, false, true, 0x800, false)]
        [InlineData(false, false, true, 0x801, true)]
        [InlineData(true, false, true, 0x801, false)]
        [InlineData(false, true, true, 0x801, false)]
        [InlineData(false, false, false, 0x801, false)]
        public void FinishingRequiresReturnSegmentPastItsMidpoint(bool preview, bool launch, bool returning, int fraction, bool finishes)
        {
            var train = new CoasterTrain { IsPreview = preview, StillOnLaunchSegment = launch };
            train.AfterMovement(returning, fraction, 1);
            Assert.Equal(finishes, train.ReadyToUnload); Assert.Equal(finishes ? 1 : 0, train.Laps);
        }

        // REJECTS hard-coding the disc's duration=1, an added edge detector, clearing the finished flag, or widening the lap halfword.
        [Fact]
        public void CoasterLapCheckReadsDurationAndDoesNotDebounceRepeatedCalls()
        {
            var train = new CoasterTrain();
            train.AfterMovement(true, 0x900, 2); Assert.False(train.ReadyToUnload); Assert.Equal(1, train.Laps);
            train.AfterMovement(true, 0x900, 2); Assert.True(train.ReadyToUnload); Assert.Equal(2, train.Laps);
            train.AfterMovement(true, 0x900, 10); Assert.True(train.ReadyToUnload); Assert.Equal(3, train.Laps);
            var reduced = new CoasterTrain { Laps = 5 };
            reduced.AfterMovement(true, 0x900, 2); Assert.True(reduced.ReadyToUnload);
            var wrap = new CoasterTrain { Laps = 32767 };
            wrap.AfterMovement(true, 0x900, 1); Assert.False(wrap.ReadyToUnload); Assert.Equal(-32768, wrap.Laps);
        }
    }
}
