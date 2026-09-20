using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class PathedRideTests
    {
        sealed class ZeroRandom : IRandomSource { public int Next(int n) => 0; }
        sealed class World : IPathedRideWorld
        {
            public uint NowTick { get; set; } = 20;
            public int Riders { get; set; }
            public int Capacity { get; set; } = 2;
            public int Duration { get; set; } = 5;
            public bool TrackConnected { get; set; } = true;
            public Visitor QueueHead { get; set; }
            public bool PreviewActive { get; set; }
            public List<int> Vehicles = new();
            public List<int> Removed = new();
            public int VehicleCount => Vehicles.Count;
            public int Boardings;
            public void BoardHeadGuest() { Boardings++; Riders++; QueueHead = null; }
            public bool VehicleReadyToUnload(int index) => Vehicles[index] != 0;
            public void UnloadVehicle(int index) { Removed.Add(Vehicles[index]); Vehicles.RemoveAt(index); Riders--; }
        }

        // REJECTS dispatching partial loads, boarding a missing/shuffling head, or borrowing a timeout from flat rides.
        [Theory]
        [InlineData(10u, 18, false)] [InlineData(19u, 18, false)] [InlineData(20u, 19, false)]
        [InlineData(20u, -1, false)] [InlineData(20u, 18, true)]
        [InlineData(40u, 18, true)] [InlineData(0xFFFFFFF0u, 18, true)]
        public void OnlyAReadyQueueHeadBoardsOnTheCadence(uint tick, int state, bool boards)
        {
            var world = new World { NowTick = tick };
            if (state >= 0) { world.QueueHead = Visitor.Spawn(new ZeroRandom(), 0); world.QueueHead.SetState((VisitorState)state); }
            var ride = new PathedRide { RunTicks = 7 };
            Assert.Equal(AttractionStatus.Loading, ride.LoadTick(world));
            Assert.Equal(boards ? 1 : 0, world.Boardings); Assert.Equal(7, ride.RunTicks);
        }

        // REJECTS dispatch on the last boarding call, dispatch off-cadence, and carrying an old run counter into a new load.
        [Fact]
        public void AFullRideStartsOnTheFollowingCadence()
        {
            var world = new World { Riders = 1, QueueHead = Visitor.Spawn(new ZeroRandom(), 0) };
            world.QueueHead.SetState(VisitorState.WaitingInQueue);
            var ride = new PathedRide { RunTicks = 7 };
            Assert.Equal(AttractionStatus.Loading, ride.LoadTick(world));
            Assert.Equal(2, world.Riders);
            world.NowTick = 21;
            Assert.Equal(AttractionStatus.Loading, ride.LoadTick(world)); Assert.Equal(7, ride.RunTicks);
            world.NowTick = 40;
            Assert.Equal(AttractionStatus.Running, ride.LoadTick(world)); Assert.Equal(0, ride.RunTicks);
            Assert.Equal(1, world.Boardings);
        }

        // REJECTS equality-only capacity checks when a slider was reduced below the existing passenger count.
        [Fact]
        public void CapacityReductionStillDispatches()
        {
            var world = new World { Riders = 3 };
            Assert.Equal(AttractionStatus.Running, new PathedRide().LoadTick(world)); Assert.Equal(0, world.Boardings);
        }

        // REJECTS phase counting, duration-as-ticks, testing before increment, and using a fixed default duration.
        [Theory]
        [InlineData(1, 2)] [InlineData(5, 10)] [InlineData(10, 20)]
        public void RunEnablesUnloadingAtTwiceTheDuration(int duration, int ticks)
        {
            var world = new World { Duration = duration }; var ride = new PathedRide();
            for (int i = 1; i < ticks; i++) Assert.Equal(AttractionStatus.Running, ride.RunTick(world));
            Assert.Equal(AttractionStatus.Unloading, ride.RunTick(world)); Assert.Equal(ticks, ride.RunTicks);
        }

        // REJECTS advancing the counter on an incomplete route, or widening the signed halfword to avoid its wrap.
        [Fact]
        public void AnIncompleteRouteClosesBeforeCountingAndRunCountWraps()
        {
            var world = new World { TrackConnected = false }; var ride = new PathedRide { RunTicks = 32767 };
            Assert.Equal(AttractionStatus.ClosedByPlayer, ride.RunTick(world)); Assert.Equal(32767, ride.RunTicks);
            world.TrackConnected = true;
            Assert.Equal(AttractionStatus.Running, ride.RunTick(world)); Assert.Equal(-32768, ride.RunTicks);
        }

        // REJECTS unloading before the timer has enabled it being interpreted as permission to ignore the vehicle's lap flag.
        [Fact]
        public void UnloadRequiresCadenceAndFinishedVehiclesAndPreservesTheCompactionSkip()
        {
            var world = new World { NowTick = 19, Riders = 4, Vehicles = new() { 1, 2, 0, 3 } };
            Assert.Equal(AttractionStatus.Unloading, PathedRide.UnloadTick(AttractionStatus.Unloading, world));
            Assert.Empty(world.Removed);
            world.NowTick = 20;
            PathedRide.UnloadTick(AttractionStatus.Unloading, world);
            Assert.Equal(new[] { 1, 3 }, world.Removed); Assert.Equal(new[] { 2, 0 }, world.Vehicles);
            world.NowTick = 30; PathedRide.UnloadTick(AttractionStatus.Unloading, world);
            Assert.Equal(new[] { 1, 3, 2 }, world.Removed);
        }

        // REJECTS unloading preview passengers, reopening a broken ride, or reopening in the same scan that unloads the last vehicle.
        [Fact]
        public void PreviewHoldsPassengersAndEmptyTransitionsWaitUntilTheNextCadence()
        {
            var world = new World { Riders = 1, Vehicles = new() { 1 }, PreviewActive = true };
            PathedRide.UnloadTick(AttractionStatus.Unloading, world); Assert.Empty(world.Removed);
            world.PreviewActive = false;
            Assert.Equal(AttractionStatus.Unloading, PathedRide.UnloadTick(AttractionStatus.Unloading, world));
            Assert.Equal(0, world.Riders);
            world.NowTick = 21;
            Assert.Equal(AttractionStatus.Unloading, PathedRide.UnloadTick(AttractionStatus.Unloading, world));
            world.NowTick = 30;
            Assert.Equal(AttractionStatus.Loading, PathedRide.UnloadTick(AttractionStatus.Unloading, world));
            Assert.Equal(AttractionStatus.BrokenDown, PathedRide.UnloadTick(AttractionStatus.BrokenDown, world));
        }

        // REJECTS using the ride tick counter as vehicle readiness, equality-only lap limits, signedness loss, and resetting a latched flag.
        [Fact]
        public void VehicleLapsAreSignedBytesAndTheUnloadFlagLatches()
        {
            var v = new PathedRideVehicle();
            v.CompleteLap(2); Assert.False(v.ReadyToUnload); Assert.Equal(1, v.Laps);
            v.CompleteLap(2); Assert.True(v.ReadyToUnload);
            v.CompleteLap(10); Assert.True(v.ReadyToUnload);
            var reduced = new PathedRideVehicle { Laps = 5 };
            reduced.CompleteLap(2); Assert.True(reduced.ReadyToUnload);
            var wrapped = new PathedRideVehicle { Laps = 127 };
            wrapped.CompleteLap(1); Assert.False(wrapped.ReadyToUnload); Assert.Equal(-128, wrapped.Laps);
        }
    }
}
