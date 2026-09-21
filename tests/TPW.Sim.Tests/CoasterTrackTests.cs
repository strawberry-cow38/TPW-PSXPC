using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class CoasterTrackTests
    {
        // Synthetic geometry throughout: no fixture is claimed to be a saved PSX park.
        sealed class Geometry : ICoasterTrackWorld
        {
            public CoasterPieceGeometry Value;
            public int Calls;
            public CoasterPieceGeometry ResolvePieceGeometry(CoasterPiece piece) { Calls++; return Value; }
        }
        sealed class Random : IRandomSource { public int Next(int n) => 0; }
        sealed class World : ICoasterSimulationWorld
        {
            public uint NowTick { get; set; } = 20;
            public AttractionStatus Status { get; set; } = AttractionStatus.Loading;
            public int Capacity { get; set; } = 100;
            public int BoardingBatchSize { get; set; } = 1;
            public int LaunchSpeed { get; set; } = 64;
            public int MovementDelta { get; set; } = 4096;
            public int SpeedSlider { get; set; } = 100;
            public int Duration { get; set; } = 1;
            public bool HalfSpeed { get; set; }
            public readonly Queue<Visitor> Queue = new();
            public readonly List<Visitor> Exited = new();
            public int Animations;
            public Visitor QueueHead => Queue.Count == 0 ? null : Queue.Peek();
            public bool AdvanceStationAnimation() { Animations++; return false; }
            public void BoardGuest(Visitor guest) { Assert.Same(guest, Queue.Dequeue()); guest.SetState(VisitorState.Loading); }
            public void ExitGuest(Visitor guest) { guest.SetState(VisitorState.Unloading); Exited.Add(guest); }
            public Visitor Enqueue()
            {
                var v = Visitor.Spawn(new Random(), 0); v.SetState(VisitorState.WaitingInQueue); Queue.Enqueue(v); return v;
            }
        }

        static CoasterPiece Piece(short x, short z, short height = 0, short bank = 0, byte kind = 0, byte phase = 0)
            => new(x, 0, z, 1, height, bank, kind, phase);
        static CoasterTrackNode Node(CoasterPiece p) => new(p, default);
        static CoasterTrack Circuit(short size = 2, byte kind = 0)
        {
            var g = new Geometry();
            var t = new CoasterTrack(Node(Piece(size,0,kind:kind)), Node(Piece(0,0,kind:kind)));
            Assert.Equal(CoasterTrack.Addition.Placed,t.Append(Piece(size,size,kind:kind),g));
            Assert.Equal(CoasterTrack.Addition.Placed,t.Append(Piece(0,size,kind:kind),g));
            Assert.Equal(CoasterTrack.Addition.Closed,t.Append(Piece(0,0),g));
            return t;
        }

        // REJECTS swapping the X/Z and height/roll fields, changing signedness, dropping opaque bytes or writing past 14 bytes.
        [Fact]
        public void FourteenByteEncodingRoundTripsWithoutNamingPadding()
        {
            byte[] b = { 0xFE,0xFF, 0xAD,0xDE, 0x03,0x80, 0x01,0x80, 0x34,0xF2, 0xFE,0xFF, 1,3, 0xCC };
            var p = CoasterPiece.Read(b);
            Assert.Equal(new CoasterPiece(-2,0xDEAD,unchecked((short)0x8003),0x8001,unchecked((short)0xF234),-2,1,3),p);
            var output = Enumerable.Repeat((byte)0xCC,15).ToArray(); p.Write(output);
            Assert.Equal(b,output);
            Assert.Throws<ArgumentException>(() => CoasterPiece.Read(new byte[13]));
            Assert.Throws<ArgumentException>(() => p.Write(new byte[13]));
        }

        // REJECTS treating the 64-slot reservation or connection bits as populated pieces, and touching unknown record tail bytes.
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(31)] [InlineData(63)]
        public void PopulatedCountIsIndependentOfReservation(int count)
        {
            var b = Enumerable.Repeat((byte)0xAB,0x418).ToArray(); b[0x95]=(byte)(0xC0|count);
            for (int i=0;i<count;i++) Piece((short)i,(short)-i).Write(b.AsSpan(0x96+i*14));
            var before = (byte[])b.Clone(); var pieces = CoasterPiece.ReadPopulated(b);
            Assert.Equal(count,pieces.Length); Assert.Equal(before,b);
            for (int i=0;i<count;i++) Assert.Equal(Piece((short)i,(short)-i),pieces[i]);
            Assert.Throws<ArgumentException>(() => CoasterPiece.ReadPopulated(new byte[0x417]));
        }

        // REJECTS interpreting the opaque halfword as elevation, omitting tile centers, height, model offsets or s16 narrowing.
        [Fact]
        public void GeometryBoundaryKeepsTerrainAndAssetInputsExplicit()
        {
            var p = new CoasterPiece(-2,0xBEEF,127,0x8000,-20,-80,1,3);
            var n = new CoasterTrackNode(p,new CoasterPieceGeometry(300,44,new CoasterVector(5,-12,-3)));
            Assert.Equal(new CoasterVector(-384,324,32640),n.Anchor);
            Assert.Equal(new CoasterVector(-379,312,32637),n.SplinePoint);
            Assert.True(n.CheckPassed);
            Assert.Equal(-32768, new CoasterTrackNode(Piece(0,0,1),new CoasterPieceGeometry(32767,0,default)).Anchor.Y);
        }

        // REJECTS a single station node, reverse links, length-to-next instead of previous, and counting a closing sentinel as a piece.
        [Fact]
        public void BuilderLinksTheTwoStationNodesAndClosesWithoutAddingASlot()
        {
            var t = Circuit(); var a=t.Pieces[0]; var b=t.Pieces[1];
            Assert.Equal(2,t.Pieces.Count); Assert.True(t.Connected); Assert.True(t.ChecksPass);
            Assert.Same(t.Launch,a.Previous); Assert.Same(b,a.Next);
            Assert.Same(b,t.Approach.Previous); Assert.Same(t.Launch,t.Approach.Next);
            Assert.Same(t.Approach,t.Launch.Previous);
            Assert.Equal(512,t.Launch.Length); Assert.Equal(512,a.Length);
            var same=Node(Piece(0,0)); Assert.Throws<ArgumentException>(() => new CoasterTrack(same,same));
        }

        // REJECTS closing an empty route, height-based closure veto, capacity 64 and allowing closure AFTER reaching 32.
        [Fact]
        public void ClosureIgnoresHeightButThePoolLimitIsTestedFirst()
        {
            var g=new Geometry(); var t=new CoasterTrack(Node(Piece(1,0)),Node(Piece(0,0)));
            Assert.Equal(CoasterTrack.Addition.Placed,t.Append(Piece(0,0),g));
            Assert.False(t.Connected);
            Assert.Equal(CoasterTrack.Addition.Closed,t.Append(Piece(0,0,999),g));
            Assert.Equal(1,g.Calls);
            t=new CoasterTrack(Node(Piece(1,0)),Node(Piece(0,0)));
            for(short i=1;i<=32;i++) Assert.Equal(CoasterTrack.Addition.Placed,t.Append(Piece(i,1),g));
            Assert.Equal(CoasterTrack.Addition.Refused,t.Append(Piece(0,0),g));
            Assert.Equal(CoasterTrack.Addition.Refused,t.Append(Piece(33,1),g));
            Assert.Equal(32,t.Pieces.Count); Assert.False(t.Connected);
        }

        // REJECTS using validation as connection, skipping either station's validation, and caching a stale aggregate result.
        [Fact]
        public void ValidationIsIndependentOfConnectionAndIncludesEachUsedNode()
        {
            var t=Circuit();
            foreach(var n in new[]{t.Launch,t.Approach}.Concat(t.Pieces))
            { n.CheckPassed=false; Assert.False(t.ChecksPass); Assert.True(t.Connected); n.CheckPassed=true; Assert.True(t.ChecksPass); }
            var open=new CoasterTrack(Node(Piece(1,0)),Node(Piece(0,0)));
            Assert.True(open.ChecksPass); Assert.False(open.Connected);
        }

        // REJECTS treating closure as an immutable circuit and forgetting to clear the connection veto on a later edit.
        [Fact]
        public void AppendingToAClosedRouteBreaksTheClosingConnection()
        {
            var t=Circuit();var old=t.Pieces[^1];
            Assert.Equal(CoasterTrack.Addition.Placed,t.Append(Piece(5,5),new Geometry()));
            Assert.False(t.Connected);Assert.Same(t.Pieces[^1],old.Next);Assert.Null(t.Pieces[^1].Next);
        }

        // REJECTS consuming bit 7 as connection contrary to save.md, reclassifying reservation as runtime capacity, and guessing closure from length.
        [Theory]
        [InlineData(0,false)] [InlineData(0x40,true)] [InlineData(0x80,false)]
        public void RestoreRetainsReportConnectionFlag(int flags,bool connected)
        {
            var bytes=new byte[0x418]; bytes[0x95]=(byte)(flags|2);
            Piece(2,2).Write(bytes.AsSpan(0x96)); Piece(0,2).Write(bytes.AsSpan(0xA4));
            var t=CoasterTrack.Restore(bytes,Node(Piece(2,0)),Node(Piece(0,0)),new Geometry());
            Assert.Equal(connected,t.Connected); Assert.Equal(2,t.Pieces.Count);
            bytes[0x95]=33;
            Assert.Throws<ArgumentException>(() => CoasterTrack.Restore(bytes,Node(Piece(2,0)),Node(Piece(0,0)),new Geometry()));
        }

        // REJECTS Manhattan/horizontal distance, exact floating-point sqrt in place of the PSX lookup, and length-after-spline-offset.
        [Fact]
        public void ChordLengthUsesThreeAxesAndTheOriginalQuantizedRoot()
        {
            var g=new Geometry { Value=new CoasterPieceGeometry(0,0,new CoasterVector(1000,1000,1000)) };
            var t=new CoasterTrack(Node(Piece(0,0)),Node(Piece(-1,0)));
            t.Append(Piece(1,1,256),g);
            Assert.Equal(443,t.Pieces[0].Length); // SquareRoot0(196608), all three deltas are 256.
            Assert.Equal(1021,CoasterMath.SquareRoot(1048575)); // exact floor sqrt would be 1023.
            Assert.Equal(0,CoasterMath.SquareRoot(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CoasterMath.SquareRoot(-1));
        }

        // REJECTS multiplying before reciprocal truncation, unsigned division, or a 12-bit distance accumulator.
        [Theory]
        [InlineData(196608,768,3840)] [InlineData(-1,512,-1)] [InlineData(100,0,0)]
        [InlineData(65536,512,2048)] [InlineData(65536,-512,-2048)]
        public void FractionPreservesReciprocalQuantization(int distance,short length,int expected)
            => Assert.Equal(expected,CoasterMath.Fraction(distance,length));

        // REJECTS linear interpolation, conventional Catmull-Rom tension, omitting derivative normalization and swapping spline endpoints.
        [Fact]
        public void SplineUsesTheReadNegativeTensionAndSuppliesPhysicsTangent()
        {
            var p0=new CoasterVector(0,0,0); var p1=new CoasterVector(256,0,0);
            var p2=new CoasterVector(256,256,0); var p3=new CoasterVector(512,256,0);
            Assert.Equal(p1,CoasterMath.Spline(p0,p1,p2,p3,0).Position);
            Assert.Equal(p2,CoasterMath.Spline(p0,p1,p2,p3,4096).Position);
            var m=CoasterMath.Spline(p0,p1,p2,p3,2048);
            Assert.Equal(new CoasterVector(256,128,0),m.Position);
            Assert.Equal(CoasterMath.Normalize(new CoasterVector(-96,288,0)),m.Tangent);
            Assert.True(m.Tangent.X<0); Assert.True(m.Tangent.Y>3800);
            Assert.Equal(new CoasterVector(0,4096,0),CoasterMath.Normalize(new CoasterVector(0,256,0)));
            Assert.Equal(default,CoasterMath.Normalize(default));
        }

        // REJECTS a chord-only visual position, using next→current, failing to duplicate station endpoints, and omitting bank interpolation.
        [Fact]
        public void RouteSampleUsesFourControlsAndStationEndCaps()
        {
            var g=new Geometry();
            var t=new CoasterTrack(Node(Piece(2,0,bank:200)),Node(Piece(0,0,bank:-100)));
            t.Append(Piece(2,2,bank:600),g); t.Append(Piece(0,2),g); t.Append(Piece(0,0),g);
            var sample=t.Sample(t.Launch,1024);
            var expected=CoasterMath.Spline(t.Approach.SplinePoint,t.Approach.SplinePoint,t.Launch.SplinePoint,t.Launch.SplinePoint,1024);
            Assert.Equal(expected.Position,sample.Position); Assert.Equal(-25,sample.Bank);
            sample=t.Sample(t.Pieces[0],1024);
            expected=CoasterMath.Spline(t.Approach.SplinePoint,t.Launch.SplinePoint,t.Pieces[0].SplinePoint,t.Pieces[1].SplinePoint,1024);
            Assert.Equal(expected.Position,sample.Position); Assert.Equal(300,sample.Bank);
        }

        public static IEnumerable<object[]> BinaryVectors()
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"coaster-source.json")));
            foreach(var vector in doc.RootElement.GetProperty("vectors").EnumerateArray())
                yield return new object[]{vector.GetProperty("input").GetProperty("name").GetString(),vector.GetRawText()};
        }

        // REJECTS 32 alternatives using the executed original MIPS as oracle: wrong forces/clamps, delta/shift order,
        // multi-link wrapping, backwards launch-flag clearing, zero-length completion and post-wrap return fractions.
        [Theory]
        [MemberData(nameof(BinaryVectors))]
        public void MovementMatchesExecutedPalInstructions(string name,string json)
        {
            Assert.False(string.IsNullOrEmpty(name));
            using var doc=JsonDocument.Parse(json); var c=doc.RootElement.GetProperty("input"); var e=doc.RootElement.GetProperty("expected");
            int I(string key)=>c.GetProperty(key).GetInt32(); bool B(string key)=>c.GetProperty(key).GetBoolean();
            byte kind=(byte)I("kind"); var t=Circuit(kind:kind);
            var nodes=new[]{t.Approach,t.Launch,t.Pieces[0],t.Pieces[1]};
            int i=0; foreach(var length in c.GetProperty("lengths").EnumerateArray()) nodes[i++].Length=(short)length.GetInt32();
            var m=new CoasterTrainMotion(); m.Start(t,64);
            m.Segment=nodes[I("segment")]; m.Speed=I("speed"); m.Distance=I("distance");
            m.Tangent=new CoasterVector(0,I("slope"),0);
            m.Control.StillOnLaunchSegment=B("launch"); m.Control.ReadyToUnload=B("ready");
            m.Control.IsPreview=B("preview"); m.Control.Laps=(short)I("laps");
            var w=new World { MovementDelta=I("delta"),HalfSpeed=B("half"),SpeedSlider=I("slider"),Duration=I("duration") };
            m.Advance(t,w,I("elapsed"));
            Assert.Equal(e.GetProperty("speed").GetInt32(),m.Speed);
            Assert.Equal(e.GetProperty("distance").GetInt32(),m.Distance);
            Assert.Same(nodes[e.GetProperty("segment").GetInt32()],m.Segment);
            Assert.Equal(e.GetProperty("launch").GetBoolean(),m.Control.StillOnLaunchSegment);
            Assert.Equal(e.GetProperty("laps").GetInt32(),m.Control.Laps);
            Assert.Equal(e.GetProperty("ready").GetBoolean(),m.Control.ReadyToUnload);
        }

        // REJECTS losing launch initialization, deriving speed from the slider, and leaving reused train completion flags latched.
        [Fact]
        public void LaunchInitializesDistanceSpeedAndController()
        {
            var t=Circuit(); t.Launch.Length=513;
            var m=new CoasterTrainMotion(); m.Control.Laps=40; m.Control.ReadyToUnload=true;
            m.Start(t,77);
            Assert.Equal(77*256,m.Speed); Assert.Equal(98304,m.Distance);
            Assert.Same(t.Launch,m.Segment); Assert.Same(t.Launch,m.LaunchSegment);
            Assert.True(m.Control.StillOnLaunchSegment); Assert.Equal(0,m.Control.Laps); Assert.False(m.Control.ReadyToUnload);
            Assert.Equal(t.Sample(t.Launch,CoasterMath.Fraction(m.Distance,513)),m.Pose);
        }

        // REJECTS freezing the slope cache, advancing distance without a world position, and treating uphill/downhill motion identically.
        [Fact]
        public void MovementRefreshesTheSplinePositionAndNextTicksSlope()
        {
            var g=new Geometry(); var t=new CoasterTrack(Node(Piece(2,0)),Node(Piece(0,0)));
            t.Append(Piece(2,2,256),g); t.Append(Piece(0,2),g); t.Append(Piece(0,0),g);
            var m=new CoasterTrainMotion(); m.Start(t,64); m.Segment=t.Pieces[0];m.Distance=5000;
            var before=m.Pose.Position; var w=new World(); m.Advance(t,w,0);
            Assert.NotEqual(before,m.Pose.Position); Assert.True(m.Tangent.Y>0);
            int speed=m.Speed; m.Advance(t,w,0); Assert.True(m.Speed<speed-CoasterTrainMotion.Friction);
        }

        // REJECTS falling through a zero-length span with an invented zero fraction and refreshing its pose;
        // control: restoring a nonzero length makes the same train sample its new position.
        [Fact]
        public void ZeroLengthAdvancesDistanceButSkipsCompletionAndPose()
        {
            var t=Circuit();var m=new CoasterTrainMotion();m.Start(t,64);
            m.Segment=t.Pieces[0];m.Distance=1234;m.Segment.Length=0;
            var pose=m.Pose;var w=new World {MovementDelta=0};
            Assert.False(m.Advance(t,w,0));Assert.Equal(3282,m.Distance);
            Assert.Equal(pose,m.Pose);Assert.Equal(0,m.Control.Laps);
            m.Segment.Length=512;
            Assert.True(m.Advance(t,w,0));Assert.NotEqual(pose,m.Pose);
        }

        // REJECTS decorative trains, directly setting completion in the fixture, omitting either controller entry point,
        // cadence-gated unload and whole-ride status transitions. Control: disconnected track cannot board.
        [Fact]
        public void AGuestBoardsTravelsReturnsAndExitsUsingOnlyUpdate()
        {
            var w=new World(); var guest=w.Enqueue(); var sim=new CoasterSimulation(Circuit(),w);
            sim.Update(); var train=Assert.Single(sim.Trains); var first=train.Pose.Position;
            Assert.Equal(1,sim.Riders); Assert.Equal(7,sim.FreeTrains); Assert.Empty(w.Exited);
            int updates=0;
            while(w.Exited.Count==0 && updates++<1000) { w.NowTick++;sim.Update(); }
            Assert.InRange(updates,1,999); Assert.NotEqual(first,train.Pose.Position);
            Assert.Same(guest,Assert.Single(w.Exited)); Assert.True(train.Control.ReadyToUnload);
            Assert.Empty(sim.Trains); Assert.Equal(0,sim.Riders); Assert.Equal(8,sim.FreeTrains);
            Assert.Equal(AttractionStatus.Loading,w.Status); Assert.Equal(updates+1,sim.Controller.DispatchTicks);
            var blocked=new World(); blocked.Enqueue();
            var open=new CoasterTrack(Node(Piece(2,0)),Node(Piece(0,0)));
            var control=new CoasterSimulation(open,blocked);
            for(int i=0;i<100;i++) control.Update();
            Assert.Empty(control.Trains); Assert.Single(blocked.Queue); Assert.Empty(blocked.Exited);
        }

        // REJECTS a partial-batch timer starting at boarding, dispatching at 240, moving the new train before dispatch or forgetting elapsed reset.
        [Fact]
        public void UpdateReallyCallsPeriodicDispatchBeforeStatus()
        {
            var w=new World { BoardingBatchSize=3 };w.Enqueue(); var sim=new CoasterSimulation(Circuit(),w);
            sim.Update();w.NowTick=1;
            for(int i=1;i<240;i++)sim.Update();
            Assert.Empty(sim.Trains);Assert.Equal(1,sim.PendingPassengers);Assert.Equal(240*4096,sim.Elapsed);
            sim.Update();var m=Assert.Single(sim.Trains);
            Assert.Equal(0,sim.Controller.DispatchTicks);Assert.Equal(0,sim.Elapsed);Assert.Equal(98304,m.Distance);
            sim.Update();Assert.NotEqual(98304,m.Distance);Assert.Equal(4096,sim.Elapsed);
        }

        // REJECTS allocating a ninth train, dropping a batch when the pool is full, wrong active-list order, and failing to recycle objects.
        [Fact]
        public void EightIndependentBatchesExhaustThenReuseThePool()
        {
            var w=new World {MovementDelta=0,LaunchSpeed=8};var sim=new CoasterSimulation(Circuit(12),w);
            for(int i=0;i<9;i++){w.Enqueue();sim.Update();}
            Assert.Equal(8,sim.Trains.Count);Assert.Equal(0,sim.FreeTrains);Assert.Equal(1,sim.PendingPassengers);Assert.Equal(9,sim.Riders);
            var recycled=sim.Trains[^1];recycled.Control.ReadyToUnload=true;w.NowTick=1;sim.Update();
            Assert.Equal(1,sim.FreeTrains);Assert.Equal(1,sim.PendingPassengers);Assert.Equal(8,sim.Riders);
            sim.Controller.DispatchTicks=240;sim.Update();
            Assert.Equal(0,sim.PendingPassengers);Assert.Equal(8,sim.Trains.Count);Assert.Same(recycled,sim.Trains[0]);
            Assert.False(recycled.Control.ReadyToUnload);Assert.Equal(0,recycled.Control.Laps);
        }

        // REJECTS FIFO train unloading, skipping a train after removal, waiting for all active trains, and ejecting pending guests in reverse order.
        [Fact]
        public void InvalidTrackEjectsActiveBatchesThenThePendingBuffer()
        {
            var w=new World {BoardingBatchSize=3,MovementDelta=0};var sim=new CoasterSimulation(Circuit(12),w);
            var guests=new List<Visitor>();
            for(int i=0;i<5;i++){guests.Add(w.Enqueue());sim.Update();}
            Assert.Single(sim.Trains);Assert.Equal(2,sim.PendingPassengers);
            sim.Track.Pieces[0].CheckPassed=false;w.NowTick=1;sim.Update();
            Assert.Equal(new[]{guests[2],guests[1],guests[0],guests[3],guests[4]},w.Exited);
            Assert.Equal(0,sim.Riders);Assert.Equal(0,sim.PendingPassengers);Assert.Empty(sim.Trains);Assert.Equal(8,sim.FreeTrains);
        }

        // REJECTS a global finish barrier, unloading only one ready batch, and making ReadyToUnload itself stop movement.
        [Fact]
        public void FinishedTrainsUnloadIndependentlyWhileAnotherKeepsMoving()
        {
            var w=new World {MovementDelta=0};var sim=new CoasterSimulation(Circuit(12),w);
            for(int i=0;i<3;i++){w.Enqueue();sim.Update();}
            var moving=sim.Trains[1];int before=moving.Distance;
            sim.Trains[0].Control.ReadyToUnload=true;sim.Trains[2].Control.ReadyToUnload=true;
            w.NowTick=1;sim.Update();
            Assert.Same(moving,Assert.Single(sim.Trains));Assert.True(moving.Distance>before);
            Assert.Equal(2,w.Exited.Count);Assert.Equal(1,sim.Riders);
        }

        // REJECTS omitting a status call, moving only while status 2, or allowing preview trains to unload and ordinary guests to board beside one.
        [Theory]
        [InlineData(2,1)] [InlineData(10,1)] [InlineData(11,0)] [InlineData(3,0)]
        [InlineData(4,1)] [InlineData(5,0)] [InlineData(6,0)]
        public void StatusDispatchKeepsMovementIndependentAndPreviewExempt(int status,int animations)
        {
            var w=new World {MovementDelta=0};var sim=new CoasterSimulation(Circuit(12),w);w.Enqueue();sim.Update();
            var m=Assert.Single(sim.Trains);m.Control.IsPreview=true;m.Control.StillOnLaunchSegment=false;m.Control.ReadyToUnload=true;
            w.Status=(AttractionStatus)status;w.Animations=0;w.Enqueue();int distance=m.Distance;sim.Update();
            Assert.True(m.Distance>distance);Assert.Same(m,Assert.Single(sim.Trains));Assert.Empty(w.Exited);
            Assert.Single(w.Queue);Assert.Equal(animations,w.Animations);
        }
    }
}
