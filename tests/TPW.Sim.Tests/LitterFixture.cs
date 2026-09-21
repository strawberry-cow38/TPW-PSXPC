using System;
using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    static class LitterFixture
    {
        internal sealed class Dice : IRandomSource
        {
            readonly Queue<int> values;
            public readonly List<int> Bounds = new();
            public Dice(params int[] values) { this.values = new(values); }
            public int Next(int n)
            {
                Bounds.Add(n);
                Assert.NotEmpty(values);
                int result = values.Dequeue();
                Assert.InRange(result, 0, n - 1);
                return result;
            }
            public void Exhausted() => Assert.Empty(values);
        }

        internal sealed class World : ILitterWorld, IVisitorWorld, IVisitorActivityWorld,
            INeedsWorld, IHandymanWorld, IHandymanLitterWorld, ILitterDrawWorld
        {
            public readonly LitterPool Pool = new();
            public Dice Dice = new();
            public long NowTick { get; set; } = 64;
            public int SlowClockDay => 0;
            public bool LitterSuppressed { get; set; }
            public bool IdleNeedsSuppressed => LitterSuppressed;
            public int NextId = 700;
            public readonly List<Litter> Registered = new(), Removed = new();
            public int TakeObjectId() => NextId++;
            public void RegisterLitter(Litter piece) => Registered.Add(piece);
            public void UnregisterLitter(Litter piece) => Removed.Add(piece);
            public (int X, int Y) GuestPosition = (2688, 2688), StaffPosition = (2688, 2688);
            public (int X, int Y) Position(Visitor guest) => GuestPosition;
            public (int X, int Y) Position(StaffMember staff) => StaffPosition;
            public BinSearch BinResult = BinSearch.NoBinInRange;
            public BinSearch TryWalkToBin(Visitor guest) => BinResult;
            public bool TryPeltEntertainer(Visitor guest) => false;
            public void DropLitter(Visitor guest) => Pool.Drop(GuestPosition.X, GuestPosition.Y, this, Dice);
            public readonly System.Collections.Generic.List<(int, int)> Events = new();
            public void AdvisorEvent(int index, int amount) => Events.Add((index, amount));
            public object TryAllocateLitter() => Pool.TryAllocate(this, Dice);
            public void PlaceLitter(object piece, int x, int y, int kind) => ((Litter)piece).Place(x, y, kind);
            public TileInfluence InfluenceAt(Visitor guest) => TileInfluence.None;
            public (int Litter, int Vomit) LitterNearby(Visitor guest) => Pool.Nearby(GuestPosition.X, GuestPosition.Y);
            public bool InQueue(Visitor guest) => false;
            public IEnumerable<(StaffMember Entertainer, int Distance)> EntertainersWithDistances(Visitor guest)
                => Array.Empty<(StaffMember, int)>();

            public bool PathAccepted = true;
            public Action<StaffMember> DuringPath;
            public readonly List<(int X, int Y, int Flags, int Argument)> Paths = new();
            public bool TryPathToLitter(StaffMember staff, int x, int y, int flags, int argument)
            {
                Paths.Add((x, y, flags, argument));
                DuringPath?.Invoke(staff);
                return PathAccepted;
            }
            public bool TryClaimNearestLitter(StaffMember staff) => HandymanLitter.TryClaimNearest(staff, Pool, this);
            public bool ClaimedLitterIsVomit(StaffMember staff) => staff.TargetLitter.IsVomit;
            public void DeleteClaimedLitter(StaffMember staff) => HandymanLitter.DeleteClaimed(staff, Pool, this);
            public void UnclaimLitter(StaffMember staff) => HandymanLitter.Unclaim(staff);
            public bool TryChooseBin(StaffMember staff) => false;
            public int ChosenBinRemaining(StaffMember staff) => throw new InvalidOperationException();
            public void EmptyChosenBin(StaffMember staff) => throw new InvalidOperationException();
            public bool IsTypeOnStrike(StaffKind kind) => false;
            public bool HasPatrolRect(StaffMember staff) => true;
            public bool TryPathIntoPatrolArea(StaffMember staff) => true;
            public bool StrikeMusterExists => true;
            public bool TryPathToStrikeMuster(StaffMember staff) => true;
            public bool TryPathToRest(StaffMember staff) => true;

            public LitterSprite SpriteData = new(0x1234, 0x5678, 5, 7, 253, 252);
            public readonly List<int> SpriteRequests = new();
            public readonly List<(short X, short Y)> HeightRequests = new();
            public readonly List<LitterQuad> Quads = new();
            public LitterSprite Sprite(int id) { SpriteRequests.Add(id); return SpriteData; }
            public short TerrainHeight(short x, short y)
            {
                HeightRequests.Add((x, y));
                return (short)(HeightRequests.Count * 11);
            }
            public void SubmitLitterQuad(LitterQuad quad) => Quads.Add(quad);

            public Litter Piece(int x = 2688, int y = 2688, bool vomit = false)
            {
                var piece = Pool.TryAllocate(this, new Dice(0));
                piece.Place(x, y, vomit ? 0x9E : 0x9A);
                return piece;
            }
        }

        internal static Visitor Guest()
        {
            var guest = Visitor.Spawn(new Dice(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), 0);
            guest.Happiness = 50; guest.Nausea = 20; guest.Rubbish = 90;
            guest.Money = Money.FromRaw(10000); guest.DecisionStagger = 7;
            guest.SetState(VisitorState.Idle);
            return guest;
        }
        internal static StaffMember Hand() => new(StaffKind.Cleaner) { Skill = 4, Morale = 50, Tiredness = 20 };
    }
}
