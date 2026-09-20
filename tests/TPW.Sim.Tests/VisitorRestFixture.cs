using System;
using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    internal static class VisitorRestFixture
    {
        internal sealed class Dice : IRandomSource
        {
            readonly Queue<int> values;
            public List<int> Bounds { get; } = new();
            public Dice(params int[] rolls) => values = new(rolls);
            public int Next(int n)
            {
                Bounds.Add(n);
                Assert.NotEmpty(values);
                int value = values.Dequeue();
                Assert.InRange(value, 0, n - 1);
                return value;
            }
            public void Exhausted() => Assert.Empty(values);
        }
        sealed class SpawnDice : IRandomSource { public int Next(int n) => 0; }
        internal static Visitor Guest(VisitorState state = VisitorState.RandomWander)
        {
            var guest = Visitor.Spawn(new SpawnDice(), 0);
            guest.SetState(state);
            guest.Happiness = 50; guest.Nausea = 30; guest.WalkSpeed = 22;
            guest.NeedA = guest.NeedB = guest.RideDesire = guest.Boredom = guest.Tiredness = 0;
            guest.HasTarget = true; guest.Purpose = Purpose.AtBin; guest.Animation = 12;
            return guest;
        }
        internal sealed class World : IWanderWorld, IVisitorActivityWorld, INeedsWorld
        {
            public long NowTick { get; set; } = 8;
            public (int X, int Y) GuestPosition = (10 * 256 + 128, 10 * 256 + 128);
            public (int X, int Y) StaffPosition = (10 * 256 + 128, 11 * 256 + 128);
            public int MapWidth { get; set; } = 44;
            public int MapHeight { get; set; } = 74;
            public int Timescale { get; set; } = 16384;
            public WaypointPool Waypoints { get; } = new();
            public int Head = -1, PositionWrites, RingRadius = -1, Allocations, Placements, Scans;
            public Func<int, int, bool> Path = (x, y) => true;
            public Func<int, int, int> Links = (x, y) => 0x55;
            public List<(int X, int Y)> PathReads = new();
            public bool RingFound, AcceptPath, AllocationSucceeds = true;
            public MapTile RingTile = new(12, 13);
            public List<(int X, int Y, int Flags, int Secondary)> Requests = new();
            public object Litter = new();
            public (object Litter, int X, int Y, int Kind) Placed;
            public bool IdleNeedsSuppressed { get; set; }
            public bool Queueing;
            public TileInfluence Influence;
            public List<(StaffMember Entertainer, int Distance)> Entertainers = new();
            public (int X, int Y) Position(Visitor guest) => GuestPosition;
            public (int X, int Y) Position(StaffMember staff) => StaffPosition;
            public void SetPosition(Visitor guest, int x, int y) { GuestPosition = (x, y); PositionWrites++; }
            public int WaypointHead(Visitor guest) => Head;
            public void SetWaypointHead(Visitor guest, int head) => Head = head;
            public bool IsPath(int x, int y) { PathReads.Add((x, y)); return Path(x, y); }
            public int PathLinks(int x, int y) => Links(x, y);
            public bool TryFindPathInRing(Visitor guest, int radius, out MapTile tile)
            { RingRadius = radius; tile = RingTile; return RingFound; }
            public bool TryRequestPath(Visitor guest, int x, int y, int flags, int secondaryFlags)
            { Requests.Add((x, y, flags, secondaryFlags)); return AcceptPath; }
            public object TryAllocateLitter() { Allocations++; return AllocationSucceeds ? Litter : null; }
            public void PlaceLitter(object litter, int x, int y, int kind)
            { Placements++; Placed = (litter, x, y, kind); }
            public TileInfluence InfluenceAt(Visitor guest) => Influence;
            public (int Litter, int Vomit) LitterNearby(Visitor guest) => (0, 0);
            public bool InQueue(Visitor guest) => Queueing;
            public IEnumerable<(StaffMember Entertainer, int Distance)> EntertainersWithDistances(Visitor guest)
            { Scans++; return Entertainers; }
            public int AddPoint(int x, int y)
            {
                int entry = Waypoints.Alloc(); Waypoints.Encode(entry, x, y); return entry;
            }
        }
    }
}
