using System;
using System.Linq;
using TPW.Sim;
using Xunit;
using static TPW.Sim.Tests.LitterFixture;

namespace TPW.Sim.Tests
{
    public class LitterTests
    {
        // REJECTS a sorted sprite table, selecting vomit randomly, skipping the allocation roll or a private id counter.
        [Theory]
        [InlineData(0, 0x9A)] [InlineData(1, 0x9B)] [InlineData(2, 0x9C)]
        [InlineData(3, 0x9D)] [InlineData(4, 0xA0)] [InlineData(5, 0x9F)]
        public void AllocationUsesTheSixStoredSpritesAndTheSharedId(int roll, int sprite)
        {
            var w = new World(); var dice = new Dice(roll);
            var piece = w.Pool.TryAllocate(w, dice);
            Assert.Equal(sprite, piece.SpriteId); Assert.False(piece.IsVomit);
            Assert.Equal(700, piece.Id); Assert.Equal(701, w.NextId);
            Assert.Null(piece.ClaimedBy); Assert.Same(piece, Assert.Single(w.Registered));
            Assert.Same(piece, Assert.Single(w.Pool.Pieces));
            Assert.Equal(new[] { 6 }, dice.Bounds); dice.Exhausted();
        }

        // REJECTS a 39/41-slot pool, evicting the oldest piece, consuming dice/ids on refusal, or leaking returned slots.
        [Fact]
        public void FortyPiecesFillThePoolAndDeletingOneMakesExactlyOneSlotAvailable()
        {
            var w = new World();
            for (int i = 0; i < 40; i++) w.Piece();
            var first = w.Pool.Pieces[0]; var last = w.Pool.Pieces[39];
            var none = new Dice();
            Assert.Null(w.Pool.Drop(0, 0, w, none)); Assert.Empty(none.Bounds);
            Assert.Equal(740, w.NextId); Assert.Equal(40, w.Pool.Count);
            Assert.Equal(40, w.Registered.Count); Assert.Equal(12, LitterPool.CountStatistic);
            w.Pool.Delete(last, w);
            Assert.Equal(39, w.Pool.Count); Assert.Same(last, Assert.Single(w.Removed));
            Assert.DoesNotContain(last, w.Pool.Pieces);
            var replacement = w.Pool.Drop(17, 29, w, new Dice(5, 100, 100));
            Assert.Same(last, replacement); Assert.Equal(740, replacement.Id);
            Assert.Equal(0x9F, replacement.SpriteId); Assert.Equal(17, replacement.X); Assert.Equal(29, replacement.Y);
            Assert.Same(replacement, w.Pool.Pieces[0]); Assert.Same(first, w.Pool.Pieces[1]);
            Assert.Null(w.Pool.TryAllocate(w, none)); Assert.Empty(none.Bounds);
        }

        // REJECTS checking the mode only in Idle: vomiting and direct allocations share the same suppression gate.
        [Fact]
        public void SuppressionRefusesWithoutTouchingThePoolIdSequenceOrDice()
        {
            var w = new World { LitterSuppressed = true }; var dice = new Dice();
            Assert.Null(w.Pool.Drop(0, 0, w, dice));
            Assert.Equal(0, w.Pool.Count); Assert.Equal(700, w.NextId); Assert.Empty(w.Registered);
            Assert.Empty(dice.Bounds);
            w.LitterSuppressed = false;
            Assert.NotNull(w.Pool.TryAllocate(w, new Dice(0)));
        }

        // REJECTS relying on the deleting caller to initialise a reused slot, or clearing coordinates in the allocator.
        [Fact]
        public void ReinitialisationResetsTheClaimAndKindButLeavesTheOldCoordinatesUntilPlacement()
        {
            var w = new World(); var piece = w.Piece(12, -34, true); var hand = Hand();
            Assert.True(w.TryClaimNearestLitter(hand));
            w.Pool.Delete(piece, w); // low-level delete only unlinks; normal cleaning releases first
            Assert.Same(hand, piece.ClaimedBy);
            var reused = w.Pool.TryAllocate(w, new Dice(4));
            Assert.Same(piece, reused); Assert.Null(reused.ClaimedBy);
            Assert.Equal((12, -34, 0xA0, 701), ((int)reused.X, (int)reused.Y, reused.SpriteId, reused.Id));
        }

        // REJECTS returning a foreign or already-free slot, which would let the forty-slot pool allocate it twice.
        [Fact]
        public void ReturningAnUnownedSlotDoesNotCorruptThePoolOrUnregisterSomeoneElsesObject()
        {
            var w = new World(); var other = new World(); var piece = other.Piece();
            Assert.Throws<InvalidOperationException>(() => w.Pool.Delete(piece, w));
            Assert.Empty(w.Removed); Assert.Equal(0, w.Pool.Count);
            other.Pool.Delete(piece, other);
            Assert.Throws<InvalidOperationException>(() => other.Pool.Delete(piece, other));
            Assert.Single(other.Removed); Assert.Equal(0, other.Pool.Count);
        }

        // REJECTS symmetric offsets, tile-centre snapping, reversed x/y dice, or losing signed-halfword wrapping.
        [Theory]
        [InlineData(2688, 2688, 0, 199, 2588, 2787)]
        [InlineData(32760, -32760, 199, 0, -32677, 32676)]
        public void DropUsesSpriteThenXThenY(int x, int y, int rx, int ry, int px, int py)
        {
            var w = new World(); var dice = new Dice(4, rx, ry);
            var piece = w.Pool.Drop(x, y, w, dice);
            Assert.Equal(px, piece.X); Assert.Equal(py, piece.Y); Assert.Equal(0xA0, piece.SpriteId);
            Assert.Equal(new[] { 6, 200, 200 }, dice.Bounds); dice.Exhausted();
        }

        // REJECTS requiring sickness for ordinary litter, clearing rubbish only after allocation, or filling a failed bin path.
        [Theory]
        [InlineData(false, BinSearch.NoBinInRange, 1, 0)]
        [InlineData(true, BinSearch.NoBinInRange, 0, 0)]
        [InlineData(false, BinSearch.PathRefused, 0, 90)]
        [InlineData(false, BinSearch.WalkingToBin, 0, 90)]
        public void TheGuestBinActionCreatesARealPieceOnlyWhenNoBinExists(bool suppressed, BinSearch result,
            int pieces, int rubbish)
        {
            var w = new World { LitterSuppressed = suppressed, BinResult = result };
            var g = Guest(); g.Nausea = 0;
            w.Dice = pieces == 1 ? new Dice(3, 2, 0, 199) : new Dice(3);
            VisitorIdle.Tick(g, w, w.Dice);
            Assert.Equal(pieces, w.Pool.Count); Assert.Equal(rubbish, g.Rubbish);
            if (pieces != 0)
            {
                var piece = Assert.Single(w.Pool.Pieces);
                Assert.Equal(0x9C, piece.SpriteId); Assert.Equal(2588, piece.X); Assert.Equal(2787, piece.Y);
            }
            w.Dice.Exhausted();
        }

        // REJECTS losing the guest's disposal/recovery on a full pool or consuming placement dice after failure.
        [Fact]
        public void AFullPoolStillLetsGuestsEmptyTheirHandsAndFinishVomiting()
        {
            var w = new World(); for (int i = 0; i < 40; i++) w.Piece();
            var g = Guest(); w.Dice = new Dice(3);
            VisitorIdle.Tick(g, w, w.Dice); Assert.Equal(0, g.Rubbish); w.Dice.Exhausted();
            g.Nausea = 99; g.Rubbish = 71; g.SetState(VisitorState.Vomiting);
            w.Dice = new Dice();
            Assert.True(VisitorActivity.Vomit(g, w, w.Dice));
            Assert.Equal(0, g.Nausea); Assert.Equal(71, g.Rubbish); Assert.Equal(VisitorState.Idle, g.State);
            Assert.Equal(40, w.Pool.Count); Assert.Empty(w.Dice.Bounds);
        }

        // REJECTS silently substituting the binary's AND rule for behaviour.md's OR rule or skipping vomit's sprite draw.
        [Theory]
        [InlineData(0)] [InlineData(93)]
        public void TheRetainedVomitRuleReachesARealVomitPieceAfterSixtyTicks(int nausea)
        {
            var w = new World { NowTick = 10 }; var g = Guest(); g.Nausea = nausea;
            w.Dice = nausea == 0 ? new Dice(4, 0) : new Dice(4);
            Assert.Equal(IdleAction.Vomit, VisitorIdle.Tick(g, w, w.Dice)); w.Dice.Exhausted();
            Assert.Equal(70, g.WaitUntil);
            w.NowTick = 70; w.Dice = new Dice();
            Assert.False(VisitorActivity.Vomit(g, w, w.Dice)); Assert.Empty(w.Pool.Pieces);
            w.NowTick = 71; w.Dice = new Dice(5, 199, 0);
            Assert.True(VisitorActivity.Vomit(g, w, w.Dice));
            var piece = Assert.Single(w.Pool.Pieces);
            Assert.True(piece.IsVomit); Assert.Equal(0x9E, piece.SpriteId);
            Assert.Equal(2787, piece.X); Assert.Equal(2588, piece.Y);
            Assert.Equal(new[] { 6, 200, 200 }, w.Dice.Bounds); w.Dice.Exhausted();
            Assert.Equal(0, g.Nausea); Assert.Equal(90, g.Rubbish);
        }

        // REJECTS laundering the roll-5 particle disagreement into a silent change to the existing findings-based Idle port.
        [Theory]
        [InlineData(24, 99, true)] [InlineData(24, 100, false)] [InlineData(25, 0, false)]
        public void MiseryRetainsTheReportsTenPercentLitterRuleAndDoesNotEmptyHands(int happy, int roll, bool drops)
        {
            var w = new World(); var g = Guest(); g.Happiness = happy; g.Rubbish = 0;
            w.Dice = happy == 25 ? new Dice(5) : drops ? new Dice(5, roll, 0, 100, 100) : new Dice(5, roll);
            VisitorIdle.Tick(g, w, w.Dice);
            Assert.Equal(drops ? 1 : 0, w.Pool.Count); Assert.Equal(0, g.Rubbish);
            Assert.Equal(VisitorState.Idle, g.State); w.Dice.Exhausted();
            if (drops)
            {
                g.Rubbish = 77; w.Dice = new Dice(5, 0, 0, 100, 100);
                VisitorIdle.Tick(g, w, w.Dice); Assert.Equal(77, g.Rubbish);
            }
        }

        // REJECTS decay, treating vomit as transient, or double-counting vomit as ordinary rubbish in the two-byte save.
        [Fact]
        public void PiecesPersistAndSaveOnlyTheirTwoKindCounts()
        {
            var w = new World(); w.Piece(); w.Piece(100, 300); var vomit = w.Piece(19, 31, true);
            Assert.Equal(((byte)2, (byte)1), w.Pool.SaveCounts());
            for (int i = 0; i < 10000; i++) w.Pool.Tick();
            Assert.Equal(3, w.Pool.Count); Assert.Empty(w.Removed);
            Assert.Equal((19, 31, 0x9E), ((int)vomit.X, (int)vomit.Y, vomit.SpriteId));
            w.Pool.Delete(vomit, w);
            Assert.Equal(((byte)2, (byte)0), w.Pool.SaveCounts());
            Assert.False(w.Pool.Nearby(19, 31).Vomit > 0);
        }

        // REJECTS Chebyshev distance, subtract-before-shift, truncating negative tiles toward zero,
        // and changing the report's inclusive two-tile boundary to the binary's strict one without review.
        [Theory]
        [InlineData(255, 255, 256, 256, true)]
        [InlineData(0, 0, -1, -1, true)]
        [InlineData(0, 0, -257, -1, false)]
        [InlineData(0, 0, 512, 0, true)]
        [InlineData(0, 0, 512, 256, false)]
        [InlineData(0, 0, 0, 768, false)]
        [InlineData(65535, 65535, -1, -1, true)]
        public void NearbyUsesWholeTilesAndTheRetainedBoundary(int x, int y, int px, int py, bool near)
        {
            var w = new World(); w.Piece(px, py, true);
            Assert.Equal(near ? (1, 1) : (0, 0), w.Pool.Nearby(x, y));
        }

        // REJECTS penalties only in Idle, only once per pass, only on unclaimed pieces, or staggered timing replacing the report.
        [Fact]
        public void ActualPiecesPenaliseAWalkingGuestUntilCleaningRemovesThem()
        {
            var w = new World { NowTick = 63 }; var g = Guest();
            g.SetState(VisitorState.WalkToDestination); g.DecisionStagger = 63;
            w.Piece(); w.Piece(2700, 2700, true); w.Piece(8000, 8000, true);
            var hand = Hand(); Assert.True(w.TryClaimNearestLitter(hand));
            VisitorNeeds.Tick(g, w, new Dice()); Assert.Equal(50, g.Happiness); Assert.Equal(20, g.Nausea);
            w.NowTick = 64; VisitorNeeds.Tick(g, w, new Dice());
            Assert.Equal(44, g.Happiness); Assert.Equal(23, g.Nausea);
            Assert.Equal(VisitorState.WalkToDestination, g.State);
            Handyman.Arrive(hand, w); w.NowTick = 75;
            Assert.True(Handyman.CleanLitter(hand, w));
            Assert.Equal(44, g.Happiness); Assert.Equal(23, g.Nausea); // no refund
            Assert.Equal(2, w.Pool.Count); Assert.Null(hand.TargetLitter); Assert.False(hand.HasTarget);
            w.NowTick = 192; w.LitterSuppressed = true; // suppression blocks spawning, not damage from existing pieces
            VisitorNeeds.Tick(g, w, new Dice());
            Assert.Equal(41, g.Happiness); Assert.Equal(23, g.Nausea);
        }

        // REJECTS stat wrapping and evaluating the nausea need penalty before the vomit increments.
        [Fact]
        public void NearbyVomitCanCrossTheNeedThresholdAndBothStatsClamp()
        {
            var w = new World(); var g = Guest(); g.Happiness = 5; g.Nausea = 98;
            w.Piece(vomit: true); w.Piece(vomit: true);
            VisitorNeeds.Tick(g, w, new Dice());
            Assert.Equal(0, g.Happiness); Assert.Equal(100, g.Nausea);
            g.Happiness = 50; g.Nausea = 83;
            VisitorNeeds.Tick(g, w, new Dice());
            Assert.Equal(43, g.Happiness); Assert.Equal(89, g.Nausea);
        }

        // REJECTS a maximum search radius, last-wins ties, raw-coordinate distance, and reclaiming one's own reserved piece.
        [Fact]
        public void HandymenChooseNearestUnclaimedInNewestFirstOrder()
        {
            var w = new World { StaffPosition = (255, 255) };
            var old = w.Piece(256, 0); var recent = w.Piece(0, 256);
            var diagonal = w.Piece(256, 256); // closer in raw units, farther in tiles
            var hand = Hand(); Assert.True(w.TryClaimNearestLitter(hand));
            Assert.Same(recent, hand.TargetLitter); Assert.Same(hand, recent.ClaimedBy);
            Assert.True(w.TryClaimNearestLitter(hand)); Assert.Same(old, hand.TargetLitter);
            var other = Hand(); Assert.True(w.TryClaimNearestLitter(other)); Assert.Same(diagonal, other.TargetLitter);
            Assert.False(w.TryClaimNearestLitter(Hand())); Assert.Equal(3, w.Paths.Count);
            var farWorld = new World { StaffPosition = (-30000, -30000) };
            var far = farWorld.Piece(30000, 30000);
            Assert.True(farWorld.TryClaimNearestLitter(Hand())); Assert.NotNull(far.ClaimedBy);
        }

        // REJECTS Euclidean ranking: the diagonal is closer in a circle but ties the axis piece in Manhattan tiles.
        // The inclusive radius-two neighbourhood alone cannot distinguish these metrics on an integer grid.
        [Fact]
        public void ManhattanTieKeepsTheNewerAxisPieceOverTheEuclideanNearestDiagonal()
        {
            var w = new World { StaffPosition = (0, 0) };
            w.Piece(256, 256);
            var axis = w.Piece(512, 0);
            var hand = Hand(); Assert.True(w.TryClaimNearestLitter(hand));
            Assert.Same(axis, hand.TargetLitter);
        }

        // REJECTS claiming after the path callback, pathing to a tile centre, wrong path flags, or forgetting the start tick.
        [Fact]
        public void ClaimIsVisibleBeforeThePathRequestAndIdlePushesTheWalk()
        {
            var w = new World { NowTick = 123 }; var piece = w.Piece(2601, 2733); var hand = Hand();
            w.DuringPath = staff =>
            {
                Assert.Same(piece, staff.TargetLitter); Assert.True(staff.HasTarget);
                Assert.Same(staff, piece.ClaimedBy); Assert.Equal(StaffClassStates.ToLitter, staff.Purpose);
            };
            Handyman.Idle(hand, w, new Dice(1));
            Assert.Equal((2601, 2733, 0x11, 0), Assert.Single(w.Paths));
            Assert.Equal(123, hand.BusyUntil); Assert.Equal(StaffState.Walking, hand.State);
            Assert.Equal(1, hand.StackDepth);
        }

        // REJECTS reserving an unreachable piece forever, deleting it on path failure, or trying the next-nearest piece.
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void BothPathFailuresReleaseBothPointersWithoutDeleting(bool asynchronous)
        {
            var w = new World { PathAccepted = asynchronous }; var piece = w.Piece(); w.Piece(3000, 3000);
            var hand = Hand(); Handyman.Idle(hand, w, new Dice(1));
            if (asynchronous) Handyman.OnPathMessage(hand, w, false);
            Assert.Null(piece.ClaimedBy); Assert.Null(hand.TargetLitter); Assert.False(hand.HasTarget);
            Assert.Equal(2, w.Pool.Count); Assert.Empty(w.Removed); Assert.Single(w.Paths);
            Assert.Equal(StaffState.Patrolling, hand.State);
            w.PathAccepted = true; Assert.True(w.TryClaimNearestLitter(Hand()));
        }

        // REJECTS deleting before the strict deadline, rewarding vomit, leaving stale claims/targets, or hiding an unreturned slot.
        [Theory]
        [InlineData(false, 51)] [InlineData(true, 44)]
        public void CleaningChangesTheRealPoolAndOnlyTheTracedStaffStats(bool vomit, int morale)
        {
            var w = new World { NowTick = 100 }; var piece = w.Piece(vomit: vomit); var hand = Hand();
            Handyman.Idle(hand, w, new Dice(1)); Handyman.Arrive(hand, w);
            w.NowTick = 110; Assert.False(Handyman.CleanLitter(hand, w)); Assert.Single(w.Pool.Pieces);
            w.NowTick = 111; Assert.True(Handyman.CleanLitter(hand, w));
            Assert.Equal(morale, hand.Morale); Assert.Equal(25, hand.Tiredness);
            Assert.Equal(StaffState.Idle, hand.State); Assert.Equal(0, hand.StackDepth);
            Assert.Null(hand.TargetLitter); Assert.False(hand.HasTarget); Assert.Null(piece.ClaimedBy);
            Assert.Equal(0, w.Pool.Count); Assert.Same(piece, Assert.Single(w.Removed));
            Assert.Equal((0, 0), w.Pool.Nearby(2688, 2688));
            var reused = w.Pool.TryAllocate(w, new Dice(1));
            Assert.Same(piece, reused); Assert.False(reused.IsVomit); Assert.Null(reused.ClaimedBy);
        }

        // REJECTS billboard drawing, half-after-scale, one shared terrain height, inclusive UV ends, UV saturation,
        // wrong corner order, dropping page/CLUT, and changing the opaque neutral FT4 or its depth ceiling.
        [Fact]
        public void DrawingBuildsFourIndependentlyGroundedCornersFromTheSelectedSprite()
        {
            var w = new World(); var piece = w.Piece(1000, 2000, true);
            LitterDrawing.Draw(piece, w);
            Assert.Equal(new[] { 0x9E }, w.SpriteRequests);
            Assert.Equal(new[] { ((short)984, (short)1976), ((short)1016, (short)1976),
                ((short)984, (short)2024), ((short)1016, (short)2024) }, w.HeightRequests);
            var q = Assert.Single(w.Quads);
            Assert.Equal(new LitterVertex(984, 11, 1976, 253, 252), q.A);
            Assert.Equal(new LitterVertex(1016, 22, 1976, 1, 252), q.B);
            Assert.Equal(new LitterVertex(984, 33, 2024, 253, 2), q.C);
            Assert.Equal(new LitterVertex(1016, 44, 2024, 1, 2), q.D);
            Assert.Equal(w.SpriteData, q.Sprite); Assert.Equal(0x2C, q.Command);
            Assert.Equal(0x80, q.Colour); Assert.Equal(2000, q.DepthLimit);
            Assert.Equal(1, w.Pool.Count); Assert.True(piece.IsVomit);
        }

        // REJECTS clamping terrain corners at the signed-position edge instead of wrapping each halfword.
        [Theory]
        [InlineData(32760, -32760, 32744, -32760, 32752, -32736)]
        [InlineData(-32760, 32760, 32760, -32744, 32736, -32752)]
        public void DrawingWrapsAllFourTerrainEdges(int x, int y, int left, int right, int top, int bottom)
        {
            var w = new World(); var piece = w.Piece(x, y);
            LitterDrawing.Draw(piece, w); var q = Assert.Single(w.Quads);
            Assert.Equal((left, top), ((int)q.A.X, (int)q.A.Y));
            Assert.Equal((right, top), ((int)q.B.X, (int)q.B.Y));
            Assert.Equal((left, bottom), ((int)q.C.X, (int)q.C.Y));
            Assert.Equal((right, bottom), ((int)q.D.X, (int)q.D.Y));
        }

        // REJECTS aliasing the dead name-table entry to the live cleaning state or renumbering the two real jobs.
        [Fact]
        public void DeadClearLitterKeepsItsNameTableNumberSeparateFromTheRealJobs()
        {
            Assert.Equal(25, (int)StaffClassStates.DeadClearLitter);
            Assert.Equal(27, (int)StaffClassStates.CleaningLitter); Assert.Equal(51, (int)StaffClassStates.EmptyingBin);
        }
    }
}
