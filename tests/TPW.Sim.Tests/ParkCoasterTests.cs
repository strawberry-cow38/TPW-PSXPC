using System;
using System.Collections.Generic;
using System.Linq;
using TPW.Data;
using TPWGodot;
using Xunit;

namespace TPW.Sim.Tests
{
    public class ParkCoasterTests
    {
        sealed class Random : IRandomSource { public int Next(int n) => 0; }
        sealed class Fixture
        {
            public readonly ParkMap Map;
            public readonly AttractionDefinition Ride = new() { Entry = 212, Type = 1, Width = 2, Depth = 3,
                Coaster = new(0, 1, 1, 1, 140, 140, 16, 1, 3) };
            public readonly TrackRun Run;
            public readonly ParkCoasterWorld Host;
            public readonly Queue<Visitor> Queue = new();
            public readonly List<Visitor> Boarded = new(), Exited = new();
            public uint Now;
            public AttractionStatus Status = AttractionStatus.Loading;
            public int Capacity = 6, Speed = 100, Duration = 1, Animations;
            public Fixture(int seats = 3)
            {
                byte[] data = new byte[12 + 24 * 24 * 8];
                BitConverter.GetBytes(24).CopyTo(data, 4); BitConverter.GetBytes(24).CopyTo(data, 8);
                for (int z = 0; z < 24; z++) for (int x = 0; x < 24; x++) data[12 + (z * 24 + x) * 8 + 1] = (byte)(40 + x + z);
                Assert.True(ParkMap.TryParse(data, out Map, out _));
                Run = new TrackRun(Ride, 10, 10, 0, 0);
                var rail = new Mesh { VertexCount = 10, Vertices = new short[] { 0,0,0, 256,32,256, 0,0,0, 0,0,0, 0,0,0, 0,0,0, 0,0,0, 0,0,0, 0,0,0, 0,0,0 } };
                var models = new[] { new Mesh(), rail, new Mesh { SeatCount = seats } };
                Host = new ParkCoasterWorld(Map, Ride, 10, 10, 0, i => i < models.Length ? models[i] : null)
                {
                    Clock = () => Now, RideStatus = () => Status, LiveCapacity = () => Capacity,
                    LiveSpeed = () => Speed, LiveDuration = () => Duration,
                    Head = () => Queue.Count > 0 ? Queue.Peek() : null,
                    Animate = () => { Animations++; return false; },
                    Board = guest => { Assert.Same(Queue.Dequeue(), guest); Boarded.Add(guest); guest.SetState(VisitorState.Loading); },
                    Exit = guest => { Exited.Add(guest); guest.SetState(VisitorState.Unloading); },
                };
            }
            public void Close()
            {
                foreach (var p in new[] { (6,11), (6,17), (16,17), (16,11) }) Assert.Equal(TrackRun.Step.Placed, Run.Lay(Map, p.Item1, p.Item2));
                Assert.Equal(TrackRun.Step.Closed, Run.Lay(Map, Run.Finish.X, Run.Finish.Z));
                Host.Synchronize(Run);
            }
            public void Enqueue(int n = 1)
            {
                for (int i = 0; i < n; i++) { var v = Visitor.Spawn(new Random(), 0); v.SetState(VisitorState.WaitingInQueue); Queue.Enqueue(v); }
            }
            public void Tick(int count = 1) { for (int i = 0; i < count; i++) { Now++; Host.Tick(4096); } }
        }

        // REJECTS swapping station/guest doors, unsigned heights, using +CA as launch speed, or merging direction fields.
        [Fact]
        public void DefinitionReadsConnectionAndLaunchInputsAtTheirOwnOffsets()
        {
            byte[] b = new byte[0xD4];
            short[] values = { -2,3,4,-5,-128,970,16 };
            for (int i = 0; i < values.Length; i++) BitConverter.GetBytes(values[i]).CopyTo(b, 0xBC + i * 2);
            b[0xCA] = 99; BitConverter.GetBytes(0xD0000000u).CopyTo(b, 0xD0);
            Assert.Equal(new CoasterDefinition(-2,3,4,-5,-128,970,16,1,3), CoasterDefinition.Read(b));
        }

        // REJECTS using TrackRun.StartFor's track-ride offsets or failing to rotate the two independent station connections.
        [Theory]
        [InlineData(0,9,11,12,11)] [InlineData(1,11,12,11,9)]
        [InlineData(2,12,11,9,11)] [InlineData(3,11,9,11,12)]
        public void BuilderUsesCoasterConnections(int rot, int lx, int lz, int ax, int az)
        {
            var f = new Fixture(); var run = new TrackRun(f.Ride,10,10,rot,0);
            Assert.Equal((lx,lz),run.Start); Assert.Equal((ax,az),run.Finish);
        }

        // REJECTS appending a refused click, keeping the closing sentinel as a runtime slot, and accidental open-route dispatch.
        [Fact]
        public void OnlyAcceptedPointsReachRuntimeAndClosureAddsNoSlot()
        {
            var f = new Fixture(); f.Enqueue();
            Assert.Equal(TrackRun.Step.Refused,f.Run.Lay(f.Map,-1,3));
            Assert.Equal(TrackRun.Step.Placed,f.Run.Lay(f.Map,7,11));
            f.Host.Synchronize(f.Run); f.Tick(300);
            Assert.Single(f.Host.Simulation.Track.Pieces); Assert.Empty(f.Boarded);
            Assert.Contains("open-track", f.Host.Report());
            Assert.Equal(TrackRun.Step.Closed,f.Run.Lay(f.Map,12,11));
            f.Host.Synchronize(f.Run);
            Assert.Single(f.Host.Simulation.Track.Pieces); Assert.True(f.Host.Simulation.Track.Connected);
            Assert.Equal((short)7,f.Host.Simulation.Track.Pieces[0].Piece.TileX);
        }

        // REJECTS reading opaque padding as height, zero terrain, omitted model height/correction, and ignoring crossed high ground.
        [Fact]
        public void GeometryUsesRealMapAndMeasuredModelWithExplicitDeckPolicy()
        {
            var f = new Fixture(); f.Close();
            var p = new CoasterPiece(6,0xBEEF,11,1,19,0,0,0);
            var g = f.Host.ResolvePieceGeometry(p);
            Assert.Equal(232,g.BaseHeight); Assert.Equal(32,g.SupportTopOffset); Assert.Equal(new CoasterVector(0,-8,0),g.SplineOffset);
            var node = new CoasterTrackNode(p,g);
            Assert.Equal(283,node.Anchor.Y); Assert.Equal(275,node.SplinePoint.Y);
            Assert.Equal(552,f.Host.DeckHeight);
            Assert.All(f.Host.Simulation.Track.Pieces, n => Assert.Equal(576,n.SplinePoint.Y));
            Assert.Equal(408,f.Host.Simulation.Track.Launch.SplinePoint.Y);
            Assert.Throws<NotSupportedException>(() => f.Host.ResolvePieceGeometry(p with { Kind = 1 }));
        }

        // REJECTS a host that constructs a simulation but never ticks it, resets trains on each tick, or never returns its guest batch.
        [Fact]
        public void PlacedRouteLoadsMovesCompletesAndUnloadsThroughHostTickOnly()
        {
            var f = new Fixture(); f.Close(); f.Enqueue(3); f.Tick(60);
            var s = f.Host.Simulation; var train = Assert.Single(s.Trains); var p = train.Pose.Position;
            Assert.Equal(3,f.Boarded.Count); Assert.Equal(7,s.FreeTrains);
            Assert.False(f.Host.Synchronize(f.Run)); Assert.Same(s,f.Host.Simulation);
            f.Tick(); Assert.NotEqual(p,train.Pose.Position);
            f.Tick(1200);
            Assert.Empty(s.Trains); Assert.Equal(8,s.FreeTrains); Assert.Equal(3,f.Exited.Count);
            Assert.Equal(f.Boarded.AsEnumerable().Reverse(),f.Exited);
            Assert.All(f.Exited,v => Assert.Equal(VisitorState.Unloading,v.State));
            Assert.Equal(1,s.CompletedTrips); Assert.Equal(1,s.CompletedLaps);
            Assert.True(s.PositionChanges > 0); Assert.Equal(0,s.Riders); Assert.True(f.Animations > 0);
            Assert.Contains("completed 1, laps 1",f.Host.Report());
        }

        // REJECTS a readout in which an unticked connected coaster looks the same as a coaster without a route.
        [Fact]
        public void ReportsDistinguishNoTrackUntickedAndMovingWithStableTrainIdentity()
        {
            var f = new Fixture(); string empty = f.Host.Report(); f.Close();
            Assert.Contains("no-track",empty); Assert.Contains("pieces 0/32",empty);
            Assert.Contains("connected",f.Host.Report()); Assert.Contains("updates 0, dispatches 0, position-changes 0",f.Host.Report());
            f.Enqueue(3); f.Tick(60); var t=Assert.Single(f.Host.Simulation.Trains);
            string first=f.Host.Report(); f.Tick(); string second=f.Host.Report();
            Assert.NotEqual(first,second); Assert.Contains($"train {t.PoolId}:",second);
            Assert.Contains("position (",second); Assert.Contains("trains 1/8, free 7",second);
        }

        // REJECTS stale connectivity after undo and discarding active/pending passengers when rebuilding accepted pieces.
        [Fact]
        public void UndoEjectsAndRebuildsButRefusedClicksDoNotResetRuntime()
        {
            var f = new Fixture(); f.Close(); f.Enqueue(4); f.Tick(80);
            Assert.Equal(1,f.Host.Simulation.PendingPassengers); Assert.Single(f.Host.Simulation.Trains);
            Assert.Equal(TrackRun.Step.Refused,f.Run.Lay(f.Map,1,1));
            Assert.False(f.Host.Synchronize(f.Run));
            Assert.True(f.Run.Undo(f.Map)); Assert.True(f.Host.Synchronize(f.Run));
            Assert.False(f.Host.Simulation.Track.Connected); Assert.Equal(4,f.Exited.Count);
            Assert.Equal(0,f.Host.Simulation.Riders); Assert.Equal(8,f.Host.Simulation.FreeTrains);
            f.Tick(300); Assert.Empty(f.Host.Simulation.Trains);
        }

        // REJECTS boarding against record max seats instead of live capacity, and treating the attachment count as eight trains.
        [Fact]
        public void LiveControlsAndModelBatchSizeReachSimulation()
        {
            var f = new Fixture(); f.Close(); f.Capacity=1; f.Speed=37; f.Duration=2; f.Enqueue(4); f.Tick(20);
            Assert.Equal(3,f.Host.BoardingBatchSize); Assert.Equal(16,f.Host.LaunchSpeed);
            Assert.Equal(37,f.Host.SpeedSlider); Assert.Equal(2,f.Host.Duration); Assert.Equal(4096,f.Host.MovementDelta);
            Assert.Single(f.Boarded); Assert.Single(f.Host.Simulation.Trains); Assert.Equal(3,f.Queue.Count);
            var zero = new Fixture(0); Assert.Equal(1,zero.Host.BoardingBatchSize);
        }

        // REJECTS exposing an unfinished coaster to guests just because construction has finished.
        [Fact]
        public void GuestAvailabilityRequiresConnectionAndLiveStatus()
        {
            var f = new Fixture(); Assert.False(f.Host.OpenToGuests);
            f.Close(); Assert.True(f.Host.OpenToGuests);
            f.Status=AttractionStatus.ClosedByPlayer; Assert.False(f.Host.OpenToGuests);
        }
    }
}
