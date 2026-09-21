using System;
using System.Collections.Generic;
using TPW.Data;
using TPWGodot;
using Xunit;

namespace TPW.Sim.Tests
{
    // The tour host's destination offsets were ported with addresses cited but with NO test over
    // them: astra's 38-mutation audit pinned cadences, seat counts, record offsets and the guest
    // transactions, and left the whole Offset() rotation table free. A sign flip or a swapped axis
    // survived every one of the 1,879 tests. These cases close that: each expectation is the
    // constant AND the axis AND the sign the binary picks for that station rotation.
    //
    // READ 0x800A426C: a four-way switch on the station rotation applies 0x384 (900) to either the
    //   X word or the Z word -- rot 0 X-900, rot 1 Z+900, rot 2 X+900, rot 3 Z-900.
    // READ 0x800A3E90: the same switch with 0x7d0 (2000) horizontally and 0x5dc (1500) up.
    // READ 0x800A3FA4: the same switch with 0x320 (800) horizontally and 0x258 (600) up.
    public class ParkTourGeometryTests
    {
        sealed class Dice : IRandomSource { public int Next(int n) => 0; }

        static (ParkTourRideWorld Host, Func<int, ParkMovingRideWorld.Car> Drive) Build(int rot)
        {
            byte[] data = new byte[12 + 24 * 24 * 8];
            BitConverter.GetBytes(24).CopyTo(data, 4); BitConverter.GetBytes(24).CopyTo(data, 8);
            Assert.True(ParkMap.TryParse(data, out var map, out _));
            var ride = new AttractionDefinition { Entry = 208, Type = 7, Width = 4, Depth = 3, TourBaseSpeed = 35 };
            var host = new ParkTourRideWorld(map, ride, 10, 10, rot, n => 0);
            var queue = new Queue<Visitor>();
            uint now = 0; var status = AttractionStatus.Loading;
            host.Clock = () => now; host.RideStatus = () => status; host.ChangeStatus = s => status = s;
            host.LiveCapacity = () => 8; host.LiveSpeed = () => 100; host.LiveDuration = () => 2;
            host.Head = () => queue.Count == 0 ? null : queue.Peek();
            host.Board = v => { Assert.Same(queue.Dequeue(), v); v.SetState(VisitorState.Loading); };
            host.Exit = v => v.SetState(VisitorState.Unloading);
            for (int i = 0; i < 3; i++)
            { var v = Visitor.Spawn(new Dice(), 0); v.SetState(VisitorState.WaitingInQueue); queue.Enqueue(v); }
            return (host, n => { for (int i = 0; i < n; i++) { now++; host.Tick(4096); } return host.Cars[0]; });
        }

        // The dock is the car's own position while it is still Loading, so the expectations below
        // are deltas -- they hold whatever the station footprint and terrain put under it.
        static CoasterVector Delta(CoasterVector from, CoasterVector to)
            => new(to.X - from.X, to.Y - from.Y, to.Z - from.Z);

        // REJECTS a flipped sign, a swapped axis, a dropped rotation case and a changed magnitude.
        [Theory]
        [InlineData(0, -900, 0, 0)]
        [InlineData(1, 0, 0, 900)]
        [InlineData(2, 900, 0, 0)]
        [InlineData(3, 0, 0, -900)]
        public void DepartureAimsNineHundredOutwardAlongTheStationAxis(int rot, int dx, int dy, int dz)
        {
            var (host, drive) = Build(rot);
            var car = drive(60);
            Assert.Equal(TourTransportState.Loading, car.Tour.State);
            var dock = car.Position;
            drive(1);
            Assert.Equal(TourTransportState.Departing, car.Tour.State);
            Assert.Equal(new CoasterVector(dx, dy, dz), Delta(dock, car.Destination));
        }

        // REJECTS the returning and approaching tables drifting apart from the departure one:
        // same switch, different magnitudes, and the only two states that also rise.
        [Theory]
        [InlineData(0, 2000, 1500, 800, 600)]
        [InlineData(1, 2000, 1500, 800, 600)]
        [InlineData(2, 2000, 1500, 800, 600)]
        [InlineData(3, 2000, 1500, 800, 600)]
        public void ReturnAndApproachUseTheSameRotationWithTheirOwnOffsets(int rot, int rf, int ru, int af, int au)
        {
            var (host, drive) = Build(rot);
            var car = drive(60);
            var dock = car.Position;
            // The binary's table, read off the branch targets rather than copied from the port:
            // rot 0 adds to X, rot 1 subtracts from Z, rot 2 subtracts from X, rot 3 adds to Z,
            // and every rotation raises Y by the same amount (0x800A3F78, 0x800A408C).
            CoasterVector Out(int forward, int up) => rot switch
            {
                0 => new(forward, up, 0),
                1 => new(0, up, -forward),
                2 => new(-forward, up, 0),
                _ => new(0, up, forward),
            };
            for (int i = 0; i < 5000 && car.Tour.State != TourTransportState.Returning; i++) drive(1);
            Assert.Equal(TourTransportState.Returning, car.Tour.State);
            Assert.Equal(Out(rf, ru), Delta(dock, car.Destination));
            for (int i = 0; i < 5000 && car.Tour.State != TourTransportState.Approaching; i++) drive(1);
            Assert.Equal(TourTransportState.Approaching, car.Tour.State);
            Assert.Equal(Out(af, au), Delta(dock, car.Destination));
        }

        // REJECTS the docking state acquiring an offset: the binary aims at the dock itself.
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
        public void DockingAimsAtTheDockItself(int rot)
        {
            var (host, drive) = Build(rot);
            var car = drive(60);
            var dock = car.Position;
            for (int i = 0; i < 20000 && car.Tour.State != TourTransportState.Docking; i++) drive(1);
            Assert.Equal(TourTransportState.Docking, car.Tour.State);
            Assert.Equal(new CoasterVector(0, 0, 0), Delta(dock, car.Destination));
        }
    }
}
