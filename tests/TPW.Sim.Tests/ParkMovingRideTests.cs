using System;
using System.Collections.Generic;
using System.Linq;
using TPW.Data;
using TPWGodot;
using Xunit;

namespace TPW.Sim.Tests
{
    public class ParkMovingRideTests
    {
        sealed class Dice : IRandomSource { public int Next(int n) => 0; }
        // Synthetic map, sliders and guests; production host files are linked verbatim.
        sealed class Fixture
        {
            public readonly ParkMap Map;
            public readonly AttractionDefinition Ride;
            public readonly TrackRun Run;
            public readonly ParkMovingRideWorld Host;
            public ParkTrackRideWorld Track => (ParkTrackRideWorld)Host;
            public ParkTourRideWorld Tour => (ParkTourRideWorld)Host;
            public readonly Queue<Visitor> Queue = new();
            public readonly List<Visitor> Boarded = new(), Exited = new();
            public uint Now;
            public AttractionStatus Status = AttractionStatus.Loading;
            public int Capacity = 3, Speed = 100, Duration = 2;
            public Fixture(bool tour = false, int seats = 1, byte baseSpeed = 35, int rot = 0)
            {
                byte[] data = new byte[12 + 24 * 24 * 8];
                BitConverter.GetBytes(24).CopyTo(data, 4); BitConverter.GetBytes(24).CopyTo(data, 8);
                Assert.True(ParkMap.TryParse(data, out Map, out _));
                Ride = new() { Entry = tour ? 208 : 215, Type = tour ? 7 : 6, Width = 4, Depth = 3,
                    TrackPassengersPerVehicle = seats, TourBaseSpeed = baseSpeed };
                Run = new TrackRun(Ride, 10, 10, rot, 0);
                Host = tour ? new ParkTourRideWorld(Map, Ride, 10, 10, rot, n => 0)
                    : new ParkTrackRideWorld(Map, Ride, 10, 10, rot);
                Host.Clock = () => Now; Host.RideStatus = () => Status; Host.ChangeStatus = s => Status = s;
                Host.LiveCapacity = () => Capacity; Host.LiveSpeed = () => Speed; Host.LiveDuration = () => Duration;
                Host.Head = () => Queue.Count == 0 ? null : Queue.Peek();
                Host.Board = v => { Assert.Same(Queue.Dequeue(), v); Boarded.Add(v); v.SetState(VisitorState.Loading); };
                Host.Exit = v => { Assert.DoesNotContain(v, Exited); Exited.Add(v); v.SetState(VisitorState.Unloading); };
            }
            public void Close()
            {
                foreach (var p in new[] { (4,11), (4,15), (8,15) }) Assert.Equal(TrackRun.Step.Placed, Run.Lay(Map,p.Item1,p.Item2));
                Assert.Equal(TrackRun.Step.Closed, Run.Lay(Map,8,11)); Track.Synchronize(Run);
            }
            public void Enqueue(int n)
            {
                for (int i=0;i<n;i++) { var v=Visitor.Spawn(new Dice(),0); v.SetState(VisitorState.WaitingInQueue); Queue.Enqueue(v); }
            }
            public void Tick(int n=1) { for(int i=0;i<n;i++) { Now++; Host.Tick(4096); } }
            public void Until(Func<bool> done, int limit = 5000)
            { for(int i=0;i<limit && !done();i++) Tick(); Assert.True(done(),Host.Report()); }
        }

        // REJECTS treating type-6 D0 as speed, type-7 D0 as seats/a word, or selecting a track piece as a car.
        [Fact]
        public void DefinitionDecodesClassSpecificFields()
        {
            byte[] d=new byte[0x200]; d[0]=0x96; BitConverter.GetBytes(0x40).CopyTo(d,0x14);
            BitConverter.GetBytes(6).CopyTo(d,0x40); BitConverter.GetBytes(5).CopyTo(d,0x40+0xD0);
            BitConverter.GetBytes(7).CopyTo(d,0x40+0xEC);
            var track=AttractionDefinition.Read(215,d);
            Assert.Equal(5,track.TrackPassengersPerVehicle); Assert.Equal(7,track.TrackCarSub); Assert.Equal(0,track.TourBaseSpeed);
            BitConverter.GetBytes(7).CopyTo(d,0x40); BitConverter.GetBytes(0x01020323).CopyTo(d,0x40+0xD0);
            var tour=AttractionDefinition.Read(208,d);
            Assert.Equal(35,tour.TourBaseSpeed); Assert.Equal(0,tour.TrackPassengersPerVehicle); Assert.Equal(0,tour.TrackCarSub);
        }

        // REJECTS boarding on missing/open track, accepting refused points, and resetting a live route on every update.
        [Fact]
        public void BuilderAcceptanceConnectivityAndReadoutAgree()
        {
            var f=new Fixture(); f.Enqueue(3); f.Tick(100);
            Assert.False(f.Host.OpenToGuests); Assert.Empty(f.Boarded); Assert.Contains("no-track",f.Host.Report());
            Assert.Equal(TrackRun.Step.Refused,f.Run.Lay(f.Map,-10,11));
            Assert.Equal(TrackRun.Step.Placed,f.Run.Lay(f.Map,4,11)); f.Track.Synchronize(f.Run); f.Tick(100);
            Assert.Empty(f.Boarded); Assert.Contains("open-track",f.Host.Report()); Assert.Equal(512,f.Track.RouteLength);
            Assert.Equal(TrackRun.Step.Closed,f.Run.Lay(f.Map,8,11)); f.Track.Synchronize(f.Run);
            Assert.True(f.Host.OpenToGuests); Assert.Contains("connected",f.Host.Report()); Assert.Equal(1024,f.Track.RouteLength);
            Assert.False(f.Track.Synchronize(f.Run));
            f.Status=AttractionStatus.ClosedByPlayer; Assert.False(f.Host.OpenToGuests);
        }

        // REJECTS partial-load dispatch, 10-tick boarding, invalid queue heads, or leaving on the final boarding tick.
        [Fact]
        public void TrackWaitsForWholeLiveCapacityAndNextCadence()
        {
            var f=new Fixture(); f.Close(); f.Enqueue(2); f.Tick(10); Assert.Empty(f.Boarded);
            f.Tick(10); Assert.Single(f.Boarded); f.Queue.Peek().SetState(VisitorState.ShuffleForward);
            f.Tick(20); Assert.Single(f.Boarded); f.Queue.Peek().SetState(VisitorState.WaitingInQueue);
            f.Tick(20); Assert.Equal(2,f.Boarded.Count); f.Tick(100);
            Assert.Equal(AttractionStatus.Loading,f.Status); Assert.Equal(0,f.Host.PositionChanges);
            f.Enqueue(1); f.Tick(20); Assert.Equal(3,f.Boarded.Count); Assert.Equal(AttractionStatus.Loading,f.Status);
            f.Tick(19); Assert.Equal(0,f.Host.Dispatches); f.Tick();
            Assert.Equal(AttractionStatus.Running,f.Status); Assert.Equal(3,f.Host.Dispatches);
        }

        // REJECTS a flat-ride timer trip, freezing after 2*duration, omitting CompleteLap, or reading duration only at launch.
        [Fact]
        public void TrackTickGateAndLiveLapLimitAreIndependent()
        {
            var f=new Fixture(); f.Capacity=1; f.Close(); f.Enqueue(1); f.Tick(40);
            var car=Assert.Single(f.Host.Cars); var start=car.Position;
            f.Tick(); Assert.Equal(new CoasterVector(start.X-10,start.Y,start.Z),car.Position);
            f.Tick(2); Assert.Equal(AttractionStatus.Running,f.Status); f.Tick();
            Assert.Equal(4,f.Track.Controller.RunTicks); Assert.Equal(AttractionStatus.Unloading,f.Status);
            Assert.Empty(f.Exited); Assert.Equal(0,car.Track.Laps);
            f.Until(()=>car.Track.Laps==1); Assert.False(car.Track.ReadyToUnload); Assert.Empty(f.Exited);
            f.Duration=3; f.Until(()=>car.Track.Laps==2); Assert.False(car.Track.ReadyToUnload);
            f.Until(()=>f.Exited.Count==1); Assert.Equal(3,car.Track.Laps);
            Assert.Equal(3,f.Host.Arrivals); Assert.Equal(1,f.Host.Completed); Assert.Equal(1,f.Host.Unloaded);
            Assert.Contains("completed 1 (ready-now 0)",f.Host.Report());
            Assert.Equal(VisitorState.Unloading,f.Exited[0].State);
        }

        // REJECTS using eight ride seats per car; covers the eight disc track definitions' distinct seat inputs.
        [Theory]
        [InlineData(41,1)] [InlineData(57,1)] [InlineData(130,2)] [InlineData(143,4)]
        [InlineData(215,1)] [InlineData(228,5)] [InlineData(363,1)] [InlineData(378,5)]
        public void TrackDefinitionsRunAndUnloadVehicleBatches(int entry,int seats)
        {
            var f=new Fixture(seats:seats); f.Ride.Entry=entry; f.Capacity=8; f.Duration=1;
            f.Close(); f.Enqueue(8); f.Tick(180);
            Assert.Equal((8+seats-1)/seats,f.Host.Cars.Count);
            var expected=f.Host.Cars.Select(c=>c.Passengers.AsEnumerable().Reverse().ToArray()).ToArray();
            f.Until(()=>f.Exited.Count==8);
            Assert.Equal(expected.Length,f.Host.Completed); Assert.Equal(8,f.Host.Unloaded);
            foreach(var batch in expected) Assert.Equal(batch,f.Exited.Where(batch.Contains));
        }

        // REJECTS unloading adjacent compacted entries in one scan, or reopening on the scan that removes the last car.
        [Fact]
        public void TrackRetainsCompactedArraySkipAndLaterEmptyScan()
        {
            var f=new Fixture(); f.Close(); f.Enqueue(3); f.Tick(80);
            foreach(var c in f.Host.Cars) c.Track.ReadyToUnload=true;
            f.Tick(10); Assert.Single(f.Host.Cars); Assert.Equal(1,f.Host.Cars[0].Id); Assert.Equal(2,f.Exited.Count);
            f.Tick(10); Assert.Empty(f.Host.Cars); Assert.Equal(AttractionStatus.Unloading,f.Status);
            f.Tick(9); Assert.Equal(AttractionStatus.Unloading,f.Status); f.Tick(); Assert.Equal(AttractionStatus.Loading,f.Status);
        }

        // REJECTS moving a speed-zero car, re-reading initialized speed, or resetting stable identities on refused clicks.
        [Fact]
        public void TrackSpeedIsSampledOnBoardingAndFrozenIsVisible()
        {
            var f=new Fixture(); f.Capacity=1; f.Speed=0; f.Close(); f.Enqueue(1); f.Tick(40);
            var car=Assert.Single(f.Host.Cars); var p=car.Position; f.Speed=100; f.Tick(500);
            Assert.Equal(p,car.Position); Assert.Equal(0,f.Host.PositionChanges); Assert.Equal(0,f.Host.Completed);
            Assert.Contains("connected",f.Host.Report()); Assert.Contains("dispatches 1, position-changes 0",f.Host.Report());
            Assert.Equal(TrackRun.Step.Refused,f.Run.Lay(f.Map,1,1)); Assert.False(f.Track.Synchronize(f.Run));
            Assert.Same(car,Assert.Single(f.Host.Cars));
        }

        // REJECTS orphaning hidden passengers on undo, keeping stale connectivity, or clearing completion history with vehicles.
        [Fact]
        public void RouteEditReturnsPassengersAndRebuilds()
        {
            var f=new Fixture(); f.Close(); f.Enqueue(3); f.Tick(80);
            Assert.True(f.Run.Undo(f.Map)); Assert.True(f.Track.Synchronize(f.Run));
            Assert.Equal(3,f.Exited.Count); Assert.Empty(f.Host.Cars); Assert.Equal(0,f.Host.Riders);
            Assert.False(f.Track.TrackConnected); Assert.Equal(0,f.Track.Controller.RunTicks);
            Assert.Equal(AttractionStatus.Loading,f.Status); f.Tick(500); Assert.Equal(3,f.Exited.Count);
        }

        // REJECTS requiring TrackRun for tours or the whole ride capacity instead of the three-seat transport.
        [Theory]
        [InlineData(56,30)] [InlineData(142,35)] [InlineData(208,35)] [InlineData(372,50)]
        public void TourDefinitionsMoveReturnAndUnloadAtCadence(int entry,byte speed)
        {
            var f=new Fixture(tour:true,baseSpeed:speed); f.Ride.Entry=entry; f.Capacity=8; f.Enqueue(3);
            f.Tick(60); var car=Assert.Single(f.Host.Cars); Assert.Equal(3,car.Passengers.Count);
            Assert.Equal(AttractionStatus.Loading,f.Status); f.Tick();
            Assert.Equal(TourTransportState.Departing,car.Tour.State); Assert.Null(f.Tour.Docked);
            Assert.Equal(AttractionStatus.Running,f.Status); Assert.Equal(1,f.Host.Dispatches);
            var p=car.Position; f.Tick(); Assert.NotEqual(p,car.Position);
            f.Until(()=>car.Tour.State==TourTransportState.Unloading);
            Assert.Equal(AttractionStatus.Unloading,f.Status); Assert.Equal(2,car.Tour.Laps);
            Assert.Equal(2,f.Host.Arrivals); Assert.Equal(1,f.Host.Completed);
            for(int i=0;i<5000 && f.Exited.Count<3;i++)
            {
                int before=f.Exited.Count; f.Tick();
                Assert.Equal(before+(f.Now%20==0?1:0),f.Exited.Count);
            }
            Assert.Equal(f.Boarded.AsEnumerable().Reverse(),f.Exited); f.Tick();
            Assert.Contains("completed 1 (ready-now 0)",f.Host.Report()); Assert.Contains("air-route",f.Host.Report());
            Assert.Contains("track not-required",f.Host.Report()); Assert.True(f.Host.Transitions>0);
        }

        // REJECTS boarding off cadence or silently adopting the binary's disputed empty-vehicle requirement.
        [Fact]
        public void TourRetainsPartialQueueEmptyDispatch()
        {
            var f=new Fixture(tour:true); f.Enqueue(1); f.Tick(19); Assert.Empty(f.Boarded);
            f.Tick(); Assert.Single(f.Boarded); Assert.Equal(0,f.Host.Dispatches); f.Tick();
            Assert.Equal(1,f.Host.Dispatches); Assert.Equal(TourTransportState.Departing,Assert.Single(f.Host.Cars).Tour.State);
            f.Until(()=>f.Exited.Count==1); Assert.Equal(1,f.Host.Completed);
        }

        // REJECTS losing passengers on retained partial-load retirement or allocating a fourth transport.
        [Fact]
        public void TourRetirementAndAllocationKeepGuestOwnership()
        {
            var f=new Fixture(tour:true); f.Duration=255; f.Enqueue(12); f.Tick(300);
            Assert.Equal(3,f.Host.Cars.Count); Assert.Equal(9,f.Boarded.Count);
            Assert.Equal(3,f.Queue.Count); Assert.Null(f.Tour.Docked);
            f.Host.EjectPassengers(); Assert.Equal(9,f.Exited.Count); Assert.Empty(f.Host.Cars); Assert.Null(f.Tour.Docked);
            var partial=new Fixture(tour:true); partial.Enqueue(4); partial.Tick(82);
            Assert.Contains(partial.Host.Cars,c=>c.Retirement && c.Passengers.Count==1);
            partial.Until(()=>partial.Exited.Count==4);
            Assert.Equal(0,partial.Host.Riders); Assert.True(partial.Host.Completed<2);
        }

        // REJECTS construction overwritten by tour status derivation, and re-opening protected broken/repair states.
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
        public void TourPreservesConstructionAndProtectedStatuses(int status)
        {
            var f=new Fixture(tour:true); f.Status=(AttractionStatus)status; f.Enqueue(1); f.Tick(100);
            Assert.Equal((AttractionStatus)status,f.Status); Assert.Empty(f.Boarded); Assert.False(f.Host.OpenToGuests);
        }

        // REJECTS deriving status before movement (one tick late) or clamping the busy-dock sentinel to duration.
        [Fact]
        public void TourBusyDockKeepsMovingAndStatusUsesDock()
        {
            var f=new Fixture(tour:true); f.Duration=1; f.Enqueue(4); f.Tick(61);
            var flying=f.Host.Cars[0]; var waiting=f.Tour.Docked; Assert.NotNull(waiting);
            f.Queue.Peek().SetState(VisitorState.ShuffleForward);
            f.Until(()=>flying.Tour.Laps==TourTransport.BusyDockLaps);
            Assert.Equal(AttractionStatus.Loading,f.Status); Assert.Equal(TourTransportState.Touring,flying.Tour.State);
            Assert.Same(waiting,f.Tour.Docked); Assert.Equal(0,f.Host.Completed);
        }

        // REJECTS double guest transfers on shared lifecycle ejection, retaining a dock/car, or reusing an old vehicle identity.
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void LifecycleEjectionClearsOnlyHostOwnership(bool tour)
        {
            var f=new Fixture(tour); if(!tour) f.Close(); f.Enqueue(3); f.Tick(60);
            int previous=f.Host.Cars.Max(c=>c.Id); f.Host.EjectPassengers(transfer:false);
            Assert.Empty(f.Exited); Assert.Empty(f.Host.Cars); Assert.Equal(0,f.Host.Riders);
            if(tour) Assert.Null(f.Tour.Docked);
            f.Status=AttractionStatus.Loading; f.Enqueue(3); f.Tick(20);
            Assert.All(f.Host.Cars,c=>Assert.True(c.Id>previous)); Assert.Single(f.Host.Cars);
        }
    }
}
