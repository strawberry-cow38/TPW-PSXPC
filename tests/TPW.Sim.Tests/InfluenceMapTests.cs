using System;
using System.Collections.Generic;
using System.Linq;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class InfluenceMapTests
    {
        sealed class World : GuardWorld, IInfluenceWorld, INeedsWorld, IEntertainerWorld
        {
            public readonly InfluenceMap Map = new();
            public (int X, int Y) GuestPosition = (2688, 2688), StaffPosition = (2688, 2688);
            public uint TimeStep { get; set; } = 4096;
            public int PositionReads;
            public bool Held;
            public int? GuestDistance = 0;
            public InfluenceArea StaffArea;
            public StaffMember Performer = new(StaffKind.Entertainer);
            public (int X, int Y) Position(Visitor guest) { PositionReads++; return GuestPosition; }
            public (int X, int Y) Position(StaffMember staff) { PositionReads++; return StaffPosition; }
            public bool IdleNeedsSuppressed => false;
            public TileInfluence InfluenceAt(Visitor guest) => Map.InfluenceAt(guest, this);
            public (int Litter, int Vomit) LitterNearby(Visitor guest) => (0, 0);
            public bool InQueue(Visitor guest) => false;
            public IEnumerable<(StaffMember Entertainer, int Distance)> EntertainersWithDistances(Visitor guest)
            { yield return (Performer, 0); }
            public bool IsHeld(StaffMember staff) => Held;
            public int? NearestGuestDistanceSquared(StaffMember staff) => GuestDistance;
            public void ReleaseInfluence(StaffMember staff) { Map.Release(StaffArea); StaffArea = null; }
            public void PlaceInfluence(StaffMember staff, int flag)
            { Assert.Equal(2, flag); StaffArea = Map.TryCreateEntertainer(staff, this); }
            public IEnumerable<(Guard guard, int distanceTiles)> GuardsWithDistances(StaffMember staff)
                => Array.Empty<(Guard, int)>();
        }

        static Visitor Guest(int state = 0)
        {
            var guest = GuardTests.Guest(state);
            guest.Happiness = 50; guest.Nausea = 20; guest.DecisionStagger = 7;
            guest.NeedA = guest.NeedB = guest.Boredom = guest.RideDesire = 0;
            return guest;
        }

        // REJECTS an invented default pleasant aura, stale read accumulation, or a nonempty new pool.
        [Fact]
        public void EmptyMapIsTheNegativeControl()
        {
            var map = new InfluenceMap();
            Assert.Empty(map.Areas); Assert.Equal(0, map.Count);
            Assert.Equal(TileInfluence.None, map.AtPosition(2688, 2688));
            var area = map.TryCreateTiles(10, 10, 1, TileInfluence.Pleasant);
            Assert.Equal(TileInfluence.Pleasant, map.AtTiles(10, 10));
            Assert.Equal(TileInfluence.None, map.AtTiles(20, 20));
            map.Release(area);
            Assert.Equal(TileInfluence.None, map.AtTiles(10, 10));
        }

        // REJECTS 19/21 slots, separate producer pools, eviction, position reads on refusal,
        // append instead of head insertion, and failure to return the exact freed slot.
        [Fact]
        public void TwentySharedSlotsExhaustAndReuseInHeadOrder()
        {
            var w = new World();
            var first = w.Map.TryCreateEntertainer(w.Performer, w);
            for (int i = 1; i < 20; i++) w.Map.CreateUnpleasantParticle(2688, 2688);
            Assert.Equal(20, w.Map.Count); Assert.Same(first, w.Map.Areas[19]);
            Assert.Equal(1, w.PositionReads);
            Assert.Null(w.Map.TryCreateEntertainer(w.Performer, w));
            Assert.Null(w.Map.TryCreateTiles(0, 0, 1, TileInfluence.Pleasant));
            Assert.Equal(1, w.PositionReads);
            Assert.Equal((TileInfluence)6, w.Map.AtTiles(10, 10));
            w.Map.Release(first);
            var replacement = w.Map.TryCreateTiles(3, 7, 5, TileInfluence.Pleasant);
            Assert.Same(first, replacement); Assert.Same(replacement, w.Map.Areas[0]);
            Assert.Equal(20, w.Map.Count);
            Assert.Equal((3, 7, 25u, TileInfluence.Pleasant),
                         ((int)replacement.X, (int)replacement.Y, replacement.RadiusSquared, replacement.Flags));
            Assert.Equal(TileInfluence.Unpleasant, w.Map.AtTiles(10, 10));
            Assert.Null(w.Map.TryCreateTiles(0, 0, 1, TileInfluence.Pleasant));
        }

        // REJECTS Chebyshev/Manhattan distance, exclusive boundary, treating radius as its square,
        // comparing dx/dy without squaring, or dropping one axis. Literal fixtures are independent.
        [Theory]
        [InlineData(0, 0, 0, true)] [InlineData(1, 0, 0, false)]
        [InlineData(0, 1, 0, false)] [InlineData(1, 0, 1, true)]
        [InlineData(0, -1, 1, true)] [InlineData(1, 1, 1, false)]
        [InlineData(-1, -1, 2, true)] [InlineData(2, 1, 2, false)]
        [InlineData(3, 4, 5, true)] [InlineData(-3, -4, 5, true)]
        [InlineData(4, 4, 5, false)] [InlineData(0, 6, 5, false)]
        public void CircleUsesSquaredEuclideanDistanceAndIncludesItsBoundary(int dx, int dy, int radius, bool covered)
        {
            var map = new InfluenceMap(); map.TryCreateTiles(10, 20, radius, TileInfluence.Pleasant);
            Assert.Equal(covered ? TileInfluence.Pleasant : TileInfluence.None, map.AtTiles(10 + dx, 20 + dy));
        }

        // REJECTS unsigned coordinates, rounding-to-nearest, subtract-before-shift, raw-distance
        // circles, losing either axis, or omitting halfword conversion on host inputs.
        [Theory]
        [InlineData(255, 511, 0, 1)] [InlineData(-1, -257, -1, -2)]
        [InlineData(65535, 65536, -1, 0)] [InlineData(32768, 32767, -128, 127)]
        public void HostPositionsBecomeSignedWholeTilesBeforeTheQuery(int x, int y, int tileX, int tileY)
        {
            var w = new World { GuestPosition = (x, y), StaffPosition = (x, y) };
            var area = w.Map.TryCreateEntertainer(w.Performer, w);
            Assert.Equal((tileX, tileY), ((int)area.X, (int)area.Y));
            area.PlaceTiles(tileX, tileY, 0);
            Assert.Equal(TileInfluence.Entertainer, w.Map.InfluenceAt(Guest(), w));
            Assert.Equal(2, w.PositionReads);
            Assert.Equal(TileInfluence.None, w.Map.AtPosition((tileX + 1) * 256, tileY * 256));
        }

        // REJECTS using fractional separation (255 to 256 is only 1 raw unit, but crosses tiles).
        [Fact]
        public void TileBoundaryHasAControlOnEachSide()
        {
            var map = new InfluenceMap(); map.TryCreateTiles(0, 0, 0, TileInfluence.Unpleasant);
            Assert.Equal(TileInfluence.Unpleasant, map.AtPosition(255, 255));
            Assert.Equal(TileInfluence.None, map.AtPosition(256, 255));
            Assert.Equal(TileInfluence.None, map.AtPosition(255, 256));
            Assert.Equal(TileInfluence.None, map.AtPosition(-1, 0));
        }

        // REJECTS widened deltas, widened radius multiplication, signed distance comparison,
        // or clamping negative radii; these out-of-park cases pin the actual integer operations.
        [Fact]
        public void HalfwordDifferencesAndUnsignedSquaredWordsArePreserved()
        {
            var map = new InfluenceMap();
            var area = map.TryCreateTiles(32767, -32768, 2, TileInfluence.Pleasant);
            Assert.Equal(TileInfluence.Pleasant, map.AtTiles(-32768, 32767));
            Assert.Equal(TileInfluence.None, map.AtTiles(-32767, 32767));
            area.PlaceTiles(0, 0, 0);
            Assert.Equal(TileInfluence.None, map.AtTiles(-32768, -32768));
            area.PlaceTiles(0, 0, 50000);
            Assert.Equal(2500000000u, area.RadiusSquared);
            Assert.Equal(TileInfluence.Pleasant, map.AtTiles(-32768, -32768));
            area.PlaceTiles(65535, 65534, int.MaxValue);
            Assert.Equal((-1, -2, 1u), ((int)area.X, (int)area.Y, area.RadiusSquared));
            area.PlaceTiles(0, 0, -5);
            Assert.Equal(25u, area.RadiusSquared);
            Assert.Equal(TileInfluence.Pleasant, map.AtTiles(3, 4));
        }

        // REJECTS returning just the first covering object, adding/XORing flags, clearing a shared
        // bit when one owner disappears, or masking unknown bits from the stored word.
        [Fact]
        public void OverlapsOrFlagsAndDeletionOnlyRemovesOneSource()
        {
            var map = new InfluenceMap();
            var a = map.TryCreateTiles(5, 5, 2, TileInfluence.Entertainer);
            var b = map.TryCreateTiles(5, 5, 2, TileInfluence.Entertainer);
            var c = map.TryCreateTiles(6, 5, 1, TileInfluence.Unpleasant);
            var d = map.TryCreateTiles(5, 5, 0, (TileInfluence)0x101);
            Assert.Equal((TileInfluence)0x107, map.AtTiles(5, 5));
            Assert.Equal((TileInfluence)6, map.AtTiles(6, 5));
            map.Release(a); Assert.Equal((TileInfluence)0x107, map.AtTiles(5, 5));
            map.Release(b); Assert.Equal((TileInfluence)0x105, map.AtTiles(5, 5));
            map.Release(c); Assert.Equal((TileInfluence)0x101, map.AtTiles(5, 5));
            map.Release(d); Assert.Equal(TileInfluence.None, map.AtTiles(5, 5));
        }

        // REJECTS clearing payload on release, ORing a new flag assignment, and mutating geometry
        // when only flags change. Freed payload is retained but excluded from every query.
        [Fact]
        public void FlagsReplaceAndReleaseLeavesUnreadPayloadIntact()
        {
            var map = new InfluenceMap();
            var area = map.TryCreateTiles(3, 4, 5, TileInfluence.Entertainer);
            area.SetFlags(TileInfluence.Unpleasant);
            Assert.Equal(TileInfluence.Unpleasant, map.AtTiles(3, 4));
            area.SetFlags(TileInfluence.None);
            Assert.Equal(TileInfluence.None, map.AtTiles(3, 4));
            area.SetFlags(TileInfluence.Pleasant); map.Release(area);
            Assert.Equal((3, 4, 25u, TileInfluence.Pleasant),
                         ((int)area.X, (int)area.Y, area.RadiusSquared, area.Flags));
            Assert.Equal(TileInfluence.None, map.AtTiles(3, 4));
        }

        // REJECTS accepting a foreign or already released handle and corrupting free/live counts.
        [Fact]
        public void ReleaseValidatesOwnershipAndAcceptsAMissingOwnerHandle()
        {
            var map = new InfluenceMap(); var other = new InfluenceMap();
            var area = other.TryCreateTiles(0, 0, 1, TileInfluence.Pleasant);
            map.Release(null); Assert.Equal(0, map.Count);
            Assert.Throws<InvalidOperationException>(() => map.Release(area));
            Assert.Equal(1, other.Count); Assert.Equal(0, map.Count);
            other.Release(area);
            Assert.Throws<InvalidOperationException>(() => other.Release(area));
            Assert.Equal(0, other.Count);
        }

        // REJECTS an entertainer aura following movement, timing out inside the map, releasing
        // on Set Idle instead of its next handler, or replacing the findings' AND with binary OR.
        [Fact]
        public void RealEntertainerKeepsItsSnapshotUntilTheIdleReleaseHook()
        {
            var w = new World { NowTick = 7 }; var e = new Entertainer(w.Performer);
            e.Tick(w, new StaffDice(0));
            Assert.Equal(EntertainerStates.Entertaining, w.Performer.State);
            Assert.Equal(1, w.Map.Count); Assert.Equal(1u, w.StaffArea.RadiusSquared);
            Assert.Equal(TileInfluence.Entertainer, w.Map.AtTiles(11, 10));
            Assert.Equal(TileInfluence.None, w.Map.AtTiles(11, 11));
            w.StaffPosition = (20 * 256, 20 * 256); w.NowTick = 10000;
            e.Tick(w, new StaffDice()); // findings' retained rule: audience keeps state 12.
            Assert.Equal(EntertainerStates.Entertaining, w.Performer.State);
            Assert.Equal(TileInfluence.Entertainer, w.Map.AtTiles(10, 10));
            Assert.Equal(TileInfluence.None, w.Map.AtTiles(20, 20));
            w.Held = true; w.GuestDistance = null;
            Assert.False(e.Tick(w, new StaffDice())); Assert.Equal(1, w.Map.Count);
            w.Held = false; e.Tick(w, new StaffDice());
            Assert.Equal(StaffState.Idle, w.Performer.State); Assert.Equal(1, w.Map.Count);
            e.Tick(w, new StaffDice(1));
            Assert.Equal(0, w.Map.Count); Assert.Null(w.StaffArea);
            Assert.Equal(TileInfluence.None, w.Map.AtTiles(10, 10));
        }

        // REJECTS preventing state 12 on pool exhaustion or manufacturing its mark later.
        [Fact]
        public void PerformanceStartsEvenWhenItsAllocationFails()
        {
            var w = new World(); var e = new Entertainer(w.Performer);
            for (int i = 0; i < 20; i++) w.Map.TryCreateTiles(50, 50, 1, TileInfluence.Pleasant);
            e.Tick(w, new StaffDice(0));
            Assert.Equal(EntertainerStates.Entertaining, w.Performer.State); Assert.Null(w.StaffArea);
            Assert.Equal(0, w.PositionReads);
            w.Map.Release(w.Map.Areas[0]); e.Tick(w, new StaffDice());
            Assert.Equal(19, w.Map.Count); Assert.Null(w.StaffArea);
            Assert.Equal(TileInfluence.None, w.Map.AtTiles(10, 10));
        }

        // REJECTS expiration at equality, passive disappearance of unrelated sources, moving the
        // aura to the guest, missing immediate release, or a second release through a reused slot.
        [Fact]
        public void ParticleSurvivesZeroAndExpiresOnUpdate361WithAControlBesideIt()
        {
            var w = new World();
            var particle = w.Map.CreateUnpleasantParticle(2688, 2688);
            var persistent = w.Map.TryCreateEntertainer(w.Performer, w);
            Assert.Equal(360, particle.Remaining); Assert.False(particle.Destroyed);
            Assert.Equal((10, 10, 1u, TileInfluence.Unpleasant),
                ((int)particle.Area.X, (int)particle.Area.Y, particle.Area.RadiusSquared, particle.Area.Flags));
            w.GuestPosition = (0, 0);
            for (int i = 0; i < 360; i++) particle.Tick(w);
            Assert.Equal(0, particle.Remaining); Assert.False(particle.Destroyed);
            Assert.Equal((TileInfluence)6, w.Map.AtTiles(10, 10));
            Assert.Equal(TileInfluence.None, w.Map.InfluenceAt(Guest(), w));
            var old = particle.Area; particle.Tick(w);
            Assert.Equal(-1, particle.Remaining); Assert.True(particle.Destroyed); Assert.Null(particle.Area);
            Assert.Equal(TileInfluence.Entertainer, w.Map.AtTiles(10, 10));
            Assert.Same(persistent, Assert.Single(w.Map.Areas));
            Assert.Same(old, w.Map.TryCreateTiles(0, 0, 1, TileInfluence.Pleasant));
            particle.Tick(w); particle.Destroy();
            Assert.Equal(-1, particle.Remaining); Assert.Equal(2, w.Map.Count);
            Assert.Equal(TileInfluence.Pleasant, w.Map.AtTiles(0, 0));
        }

        // REJECTS accumulating fractional time, the wrong timestep shift, saturating rather than
        // wrapping the halfword, or treating NowTick as elapsed emitter time.
        [Theory]
        [InlineData(0u, 360, false)] [InlineData(4095u, 360, false)]
        [InlineData(4096u, 359, false)] [InlineData(8192u, 358, false)]
        [InlineData(1474560u, 0, false)] [InlineData(1478656u, -1, true)]
        [InlineData(134217728u, -32408, true)] [InlineData(268435456u, 360, false)]
        [InlineData(4294967295u, 361, false)]
        public void ParticleCountdownUsesTheUnsignedTimestepAndSignedHalfword(uint step, int remaining, bool dead)
        {
            var w = new World { TimeStep = step, NowTick = 1000000 };
            var particle = w.Map.CreateUnpleasantParticle(-1, -257);
            Assert.Equal(TileInfluence.Unpleasant, w.Map.AtTiles(-1, -2));
            particle.Tick(w);
            Assert.Equal(remaining, particle.Remaining); Assert.Equal(dead, particle.Destroyed);
            Assert.Equal(dead ? 0 : 1, w.Map.Count);
            if (step < 4096)
            {
                for (int i = 0; i < 100; i++) particle.Tick(w);
                Assert.Equal(360, particle.Remaining); Assert.Equal(1, w.Map.Count);
            }
        }

        // REJECTS coupling emitter existence to influence allocation, retrying an exhausted aura,
        // or allowing a failed emitter to delete another owner's slot.
        [Fact]
        public void ExhaustedParticleNeverRetriesAndEarlyDestroyIsIdempotent()
        {
            var w = new World();
            var first = w.Map.CreateUnpleasantParticle(0, 0);
            for (int i = 1; i < 20; i++) w.Map.CreateUnpleasantParticle(0, 0);
            var failed = w.Map.CreateUnpleasantParticle(2688, 2688);
            Assert.Null(failed.Area); Assert.False(failed.Destroyed);
            first.Destroy(); Assert.True(first.Destroyed); Assert.Null(first.Area);
            Assert.Equal(19, w.Map.Count); first.Destroy(); Assert.Equal(19, w.Map.Count);
            failed.Tick(w); Assert.Equal(359, failed.Remaining); Assert.Null(failed.Area);
            Assert.Equal(19, w.Map.Count); Assert.Equal(TileInfluence.None, w.Map.AtTiles(10, 10));
            failed.Destroy(); failed.Destroy(); Assert.Equal(19, w.Map.Count);
            Assert.NotNull(w.Map.TryCreateEntertainer(w.Performer, w));
        }

        // REJECTS a convincing map-only test while INeedsWorld remains zero: compare real needs
        // deltas inside, outside, after deletion, and on the wrong stagger. Pleasant is SYNTHETIC:
        // this pins its established consumer, not an unestablished scenery producer.
        [Theory]
        [InlineData(1, 0, 7, 10, 56, 20)] [InlineData(1, 0, 7, 12, 50, 20)]
        [InlineData(4, 0, 7, 10, 49, 22)] [InlineData(4, 2, 7, 10, 47, 25)]
        [InlineData(4, 0, 6, 10, 50, 20)] [InlineData(5, 3, 7, 10, 53, 25)]
        public void NeedsMeasurementHasOutsideRemovedAndWrongTickControls(int flags, int state, int tick,
            int queryTile, int happy, int nausea)
        {
            var w = new World { NowTick = tick, GuestPosition = (queryTile * 256 + 128, 2688) };
            var guest = Guest(state);
            var a = w.Map.TryCreateTiles(10, 10, 1, (TileInfluence)flags);
            var b = w.Map.TryCreateTiles(10, 10, 1, (TileInfluence)flags);
            VisitorNeeds.Tick(guest, w, new StaffDice());
            Assert.Equal(happy, guest.Happiness); Assert.Equal(nausea, guest.Nausea);
            w.Map.Release(a); w.Map.Release(b);
            var control = Guest(state); VisitorNeeds.Tick(control, w, new StaffDice());
            Assert.Equal(50, control.Happiness); Assert.Equal(20, control.Nausea);
        }

        // REJECTS using a stored entertainer flag without actually reaching the watch consumer,
        // or leaking a watch into an otherwise identical world after the aura has been released.
        [Fact]
        public void EntertainerAreaReachesTheRealNeedsWatchGate()
        {
            var w = new World { NowTick = 7 };
            var area = w.Map.TryCreateEntertainer(w.Performer, w);
            var guest = Guest(); guest.EntertainerNotBefore = 0;
            VisitorNeeds.Tick(guest, w, new StaffDice());
            Assert.Equal(VisitorState.WatchEntertainer, guest.State);
            Assert.Same(w.Performer, guest.WatchedEntertainer);
            w.Map.Release(area);
            var control = Guest(); control.EntertainerNotBefore = 0;
            VisitorNeeds.Tick(control, w, new StaffDice());
            Assert.Equal(VisitorState.Idle, control.State); Assert.Null(control.WatchedEntertainer);
        }
    }
}
