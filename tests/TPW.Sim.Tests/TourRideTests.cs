using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class TourRideTests
    {
        sealed class ZeroRandom : IRandomSource { public int Next(int n) => 0; }

        // REJECTS renumbering vehicle states while leaving tests that compare only enum names green.
        [Fact]
        public void TransportStatesKeepTheJumpTableNumbers()
        {
            Assert.Equal(0, (int)TourTransportState.Loading);
            Assert.Equal(1, (int)TourTransportState.Departing);
            Assert.Equal(2, (int)TourTransportState.Touring);
            Assert.Equal(3, (int)TourTransportState.Returning);
            Assert.Equal(4, (int)TourTransportState.Approaching);
            Assert.Equal(5, (int)TourTransportState.Docking);
            Assert.Equal(6, (int)TourTransportState.Settling);
            Assert.Equal(7, (int)TourTransportState.Unloading);
            Assert.Equal(8, (int)TourTransportState.Retiring);
        }
        static Visitor Guest(VisitorState state = VisitorState.WaitingInQueue)
        {
            var guest = Visitor.Spawn(new ZeroRandom(), 0); guest.SetState(state); return guest;
        }

        sealed class Dock : ITourRideWorld
        {
            public uint NowTick { get; set; }
            public Visitor QueueHead { get; set; }
            public int TransportCount { get; set; } = 1;
            public int DockedPassengers { get; set; }
            public List<string> Calls { get; } = new();
            public void BoardHeadGuest() { Calls.Add("board"); DockedPassengers++; QueueHead = null; }
            public void UnloadLastGuest() => Calls.Add("unload");
            public void RequestRetirement() => Calls.Add("retire");
            public void Depart() => Calls.Add("depart");
        }

        sealed class Flight : ITourTransportWorld
        {
            public int Passengers { get; set; } = 3;
            public int Duration { get; set; } = 5;
            public bool DepartureBlocked { get; set; }
            public bool RetirementRequested { get; set; }
            public bool DockOccupied { get; set; }
            public AttractionStatus RideStatus { get; set; } = AttractionStatus.Running;
            public bool Arrived { get; set; }
            public bool Settled { get; set; }
            public int Moves, Settles, Removed;
            public List<TourTransportState> Entries { get; } = new();
            public bool MoveToDestination() { Moves++; return Arrived; }
            public bool SettleAtDock() { Settles++; return Settled; }
            public void EnterState(TourTransportState state) => Entries.Add(state);
            public void RemoveTransport() => Removed++;
        }

        // REJECTS local countdowns, taking shuffling guests, and confusing eight ride seats with three vehicle seats.
        [Theory]
        [InlineData(19u, 0, VisitorState.WaitingInQueue, false)]
        [InlineData(20u, 0, VisitorState.ShuffleForward, false)]
        [InlineData(20u, 2, VisitorState.WaitingInQueue, true)]
        [InlineData(20u, 3, VisitorState.WaitingInQueue, false)]
        [InlineData(40u, 0, VisitorState.WaitingInQueue, true)]
        [InlineData(0xFFFFFFF0u, 0, VisitorState.WaitingInQueue, true)]
        public void BoardingUsesTheGlobalCadenceAndTheTransportLimit(uint tick, int aboard, VisitorState state, bool boards)
        {
            var world = new Dock { NowTick = tick, DockedPassengers = aboard, QueueHead = Guest(state) };
            TourRide.LoadTick(world);
            Assert.Equal(boards ? new[] { "board" } : new string[0], world.Calls);
            Assert.Equal(aboard + (boards ? 1 : 0), world.DockedPassengers);
        }

        // REJECTS silently adopting the binary's newly found empty-transport restriction over
        // rides.md's retained queue-empty dispatch, or running that branch on a loading tick.
        [Theory]
        [InlineData(20u, 0, 1, false, "")]
        [InlineData(21u, 1, 1, false, "depart")]
        [InlineData(21u, 0, 1, true, "")]
        [InlineData(21u, 0, 1, false, "depart")]
        [InlineData(21u, 0, 2, false, "retire")]
        [InlineData(21u, 0, 3, false, "retire")]
        public void EmptySpareTransportsRetire(uint tick, int aboard, int count, bool queue, string call)
        {
            var world = new Dock { NowTick = tick, DockedPassengers = aboard, TransportCount = count,
                                   QueueHead = queue ? Guest() : null };
            TourRide.LoadTick(world);
            Assert.Equal(call == "" ? new string[0] : new[] { call }, world.Calls);
        }

        // REJECTS dispatching on the boarding call; the retained report rule permits it on the following non-cadence call.
        [Fact]
        public void BoardingAndEmptyQueueHandlingAreSeparateBranches()
        {
            var world = new Dock { NowTick = 20, QueueHead = Guest() };
            TourRide.LoadTick(world);
            Assert.Equal(new[] { "board" }, world.Calls);
            world.NowTick = 21;
            TourRide.LoadTick(world);
            Assert.Equal(new[] { "board", "depart" }, world.Calls);
        }

        // REJECTS borrowing the flat ride's ten-tick unload cadence or unloading on every call.
        [Fact]
        public void TourUnloadingUsesTwentyTicks()
        {
            var world = new Dock();
            foreach (uint tick in new uint[] { 0, 10, 19, 20, 21, 40 })
            { world.NowTick = tick; TourRide.UnloadTick(world); }
            Assert.Equal(new[] { "unload", "unload", "unload" }, world.Calls);
        }

        // REJECTS changing a repair status from vehicle motion or keeping closed/loading status without a docked vehicle.
        [Fact]
        public void VehicleStateDrivesTheRideExceptDuringStatusesFourThroughNine()
        {
            for (int status = 0; status <= 11; status++)
            {
                foreach (TourTransportState? docked in new TourTransportState?[]
                    { null, TourTransportState.Loading, TourTransportState.Touring, TourTransportState.Unloading })
                {
                    var expected = status >= 4 && status <= 9 ? (AttractionStatus)status
                        : docked == TourTransportState.Loading ? AttractionStatus.Loading
                        : docked == TourTransportState.Unloading ? AttractionStatus.Unloading : AttractionStatus.Running;
                    Assert.Equal(expected, TourRide.StatusAfterMovement((AttractionStatus)status, docked));
                }
            }
        }

        // REJECTS needing both full and retiring, departing through the blocker, or failing to reset the lap byte.
        [Theory]
        [InlineData(2, false, false, false)]
        [InlineData(3, false, false, true)]
        [InlineData(4, false, false, true)]
        [InlineData(0, true, false, true)]
        [InlineData(3, false, true, false)]
        [InlineData(0, true, true, false)]
        public void FullOrRetiringVehiclesDepartWhenUnblocked(int passengers, bool retire, bool blocked, bool departs)
        {
            var world = new Flight { Passengers = passengers, RetirementRequested = retire, DepartureBlocked = blocked };
            var vehicle = new TourTransport { State = TourTransportState.Loading, Laps = 9 };
            vehicle.Tick(world);
            Assert.Equal(departs ? TourTransportState.Departing : TourTransportState.Loading, vehicle.State);
            Assert.Equal(departs ? 0 : 9, vehicle.Laps);
            Assert.Equal(departs ? new[] { TourTransportState.Departing } : new TourTransportState[0], world.Entries);
        }

        // REJECTS using an animation completion, counting every tick, testing before incrementing, or waiting in place at a busy dock.
        [Fact]
        public void TourDurationCountsArrivalsAndABusyDockSendsItRoundAgain()
        {
            var world = new Flight();
            var vehicle = new TourTransport { State = TourTransportState.Touring, Laps = 3 };
            vehicle.Tick(world);
            Assert.Equal(3, vehicle.Laps); Assert.Empty(world.Entries);
            world.Arrived = true;
            vehicle.Tick(world);
            Assert.Equal(4, vehicle.Laps); Assert.Equal(TourTransportState.Touring, vehicle.State);
            world.DockOccupied = true;
            vehicle.Tick(world);
            Assert.Equal(200, vehicle.Laps); Assert.Equal(TourTransportState.Touring, vehicle.State);
            world.DockOccupied = false;
            vehicle.Tick(world);
            Assert.Equal(201, vehicle.Laps); Assert.Equal(TourTransportState.Returning, vehicle.State);
            Assert.Equal(new[] { TourTransportState.Touring, TourTransportState.Touring, TourTransportState.Returning }, world.Entries);
        }

        // REJECTS hard-coding five laps, a signed lap byte, or widening away the byte wrap.
        [Theory]
        [InlineData(0, 1, TourTransportState.Returning, 1)]
        [InlineData(4, 6, TourTransportState.Touring, 5)]
        [InlineData(199, 5, TourTransportState.Returning, 200)]
        [InlineData(255, 5, TourTransportState.Touring, 0)]
        public void TourLapLimitIsLiveAndTheCounterIsAnUnsignedByte(byte laps, int duration, TourTransportState expected, int count)
        {
            var vehicle = new TourTransport { State = TourTransportState.Touring, Laps = laps };
            vehicle.Tick(new Flight { Arrived = true, Duration = duration });
            Assert.Equal(expected, vehicle.State); Assert.Equal(count, vehicle.Laps);
        }

        // REJECTS skipping return stages, ignoring arrival, retiring as a normal tour, or using movement instead of settling at the dock.
        [Theory]
        [InlineData(TourTransportState.Departing, false, TourTransportState.Touring)]
        [InlineData(TourTransportState.Departing, true, TourTransportState.Retiring)]
        [InlineData(TourTransportState.Returning, false, TourTransportState.Approaching)]
        [InlineData(TourTransportState.Approaching, false, TourTransportState.Docking)]
        [InlineData(TourTransportState.Docking, false, TourTransportState.Settling)]
        [InlineData(TourTransportState.Settling, false, TourTransportState.Unloading)]
        public void MovementMustFinishBeforeTheNextStage(TourTransportState state, bool retiring, TourTransportState next)
        {
            var world = new Flight { RetirementRequested = retiring, Arrived = false, Settled = false };
            var vehicle = new TourTransport { State = state };
            vehicle.Tick(world); Assert.Empty(world.Entries); Assert.Equal(state, vehicle.State);
            world.Arrived = world.Settled = true;
            vehicle.Tick(world);
            Assert.Equal(next, vehicle.State); Assert.Equal(new[] { next }, world.Entries);
            Assert.Equal(state == TourTransportState.Settling ? 0 : 2, world.Moves);
            Assert.Equal(state == TourTransportState.Settling ? 2 : 0, world.Settles);
        }

        // REJECTS reloading while passengers remain, or treating statuses 3 and 10 as part of the 4..9 hold.
        [Fact]
        public void AnEmptyDockedVehicleWaitsOutRepair()
        {
            for (int status = 0; status <= 11; status++)
            {
                var world = new Flight { RideStatus = (AttractionStatus)status, Passengers = 1 };
                var vehicle = new TourTransport { State = TourTransportState.Unloading };
                vehicle.Tick(world); Assert.Empty(world.Entries);
                world.Passengers = 0; vehicle.Tick(world);
                bool held = status >= 4 && status <= 9;
                Assert.Equal(held ? TourTransportState.Unloading : TourTransportState.Loading, vehicle.State);
                Assert.Equal(held ? 0 : 1, world.Entries.Count);
            }
        }

        // REJECTS deleting a retiring vehicle before it reaches its destination, or merely changing its state on arrival.
        [Fact]
        public void RetirementRemovesOnlyOnArrival()
        {
            var vehicle = new TourTransport { State = TourTransportState.Retiring };
            var world = new Flight(); vehicle.Tick(world); Assert.Equal(0, world.Removed);
            world.Arrived = true; vehicle.Tick(world); Assert.Equal(1, world.Removed); Assert.Empty(world.Entries);
        }
    }
}
