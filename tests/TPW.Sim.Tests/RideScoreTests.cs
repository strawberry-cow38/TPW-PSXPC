using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>The ride score and the tables behind it (behaviour.md §2.2, §2.2b).</summary>
    public class RideScoreTests
    {
        sealed class Dice : IRandomSource
        {
            readonly Queue<int> _v;
            public Dice(params int[] v) => _v = new Queue<int>(v);
            public int Next(int n) => _v.Count > 0 ? _v.Dequeue() : 0;
        }

        sealed class World : IDecisionWorld
        {
            public long NowTick { get; set; }
            public int MapWidth { get; set; } = 50;
            public int MapHeight { get; set; } = 50;
            public List<AttractionCandidate> Attractions { get; } = new();
            public IReadOnlyList<AttractionCandidate> OpenAttractions => Attractions;
            public bool PathAccepted { get; set; } = true;
            public bool TryPathToTarget(Visitor guest) => PathAccepted;
        }

        static Visitor Guest()
        {
            var v = Visitor.Spawn(new Dice(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), 0);
            v.Happiness = 50; v.Nausea = 0; v.NeedA = 0; v.NeedB = 0; v.RideDesire = 0;
            v.VisitorType = 0;               // preference 90
            return v;
        }

        static AttractionCandidate Ride(int id = 1, int dist = 0, int slot53 = 0, int slot54 = 0,
                                        int slot55 = 0, int slot56 = 0, int type = 1, int kind = 0,
                                        bool valid = true)
            => new(id, type, slot53, slot54, slot55, slot56, true, dist, valid, kind);

        // ⭐ THE DESIRE CURVE PENALISES, IT DOES NOT MERELY IGNORE. Its first eight entries are -20 and
        // it crosses zero at exactly 35, which means a guest with low ride-desire scores every ride
        // NEGATIVELY. REJECTS a curve that starts at 0, which would make such a guest indifferent to
        // rides instead of pushed away from them.
        [Theory]
        [InlineData(0, -20)]
        [InlineData(30, -20)]
        [InlineData(34, -20)]
        [InlineData(35, 0)]      // the crossover, exactly
        [InlineData(50, 4)]
        [InlineData(70, 26)]
        [InlineData(90, 100)]
        [InlineData(100, 100)]
        public void TheDesireCurveStartsNegativeAndCrossesAtThirtyFive(int stat, int expected)
        {
            Assert.Equal(expected, VisitorTables.Desire(stat));
        }

        // ⭐ THE NEED SURFACE IS A PRODUCT. Zero on either axis gives zero, so "I don't need this" and
        // "this doesn't provide it" both score nothing rather than scoring badly. REJECTS a difference
        // or distance table, which would score a zero-need/zero-attribute pair as a perfect match.
        [Fact]
        public void TheNeedSurfaceScoresNothingWhenEitherSideIsZero()
        {
            for (int a = 0; a <= 100; a += 10) Assert.Equal(0, VisitorTables.Match(0, a));
            for (int b = 0; b <= 100; b += 10) Assert.Equal(0, VisitorTables.Match(b, 0));
            Assert.Equal(100, VisitorTables.Match(100, 100));
            Assert.Equal(17, VisitorTables.Match(50, 50));
            Assert.True(VisitorTables.Match(100, 100) > VisitorTables.Match(50, 50));
        }

        // Both rejections are absolute: the attraction cannot be chosen at any distance or need.
        [Fact]
        public void AnOffMapCentreOrAnEmptyShopIsRejectedOutright()
        {
            var g = Guest();
            Assert.Equal(RideScore.Rejected, RideScore.Score(g, Ride(valid: false), 50, 50));
            // type 2 is a shop; slot 54 of zero is GUESS "no stock".
            Assert.Equal(RideScore.Rejected, RideScore.Score(g, Ride(type: 2, slot54: 0), 50, 50));
            // ...but the same shop with stock scores normally. Asserted with a guest that actually
            // wants something, because an INDIFFERENT guest scores a perfectly good ride at exactly -1
            // too -- the sentinel is a value the formula can produce, which is the point of the note on
            // RideScore.Rejected.
            var keen = Guest(); keen.RideDesire = 90; keen.NeedA = 100; keen.NeedB = 100;
            Assert.True(RideScore.Score(keen, Ride(type: 2, slot54: 1, slot55: 100, slot56: 100), 50, 50) > 0);
        }

        // Closeness is the dominant term for a plain ride with nothing else going for it.
        [Fact]
        public void NearerScoresHigherAllElseEqual()
        {
            var g = Guest();
            int near = RideScore.Score(g, Ride(dist: 0), 50, 50);
            int far = RideScore.Score(g, Ride(dist: 50), 50, 50);
            Assert.True(near > far, $"near {near} should beat far {far}");
        }

        // ⭐ THE REPEAT PENALTY FADES BY POSITION: 5, 4, 3, 2. The thing just done is punished hardest
        // and the fourth-most-recent barely. REJECTS a flat divisor or a plain ban, both of which would
        // make the four history slots interchangeable.
        [Fact]
        public void TheRepeatPenaltyIsHarshestForTheMostRecentRide()
        {
            var g = Guest();
            var ride = Ride(id: 7, dist: 0);
            int plain = RideScore.Score(g, ride, 50, 50);

            int[] divisors = { 5, 4, 3, 2 };
            for (int slot = 0; slot < 4; slot++)
            {
                var history = new[] { -1, -1, -1, -1 };
                history[slot] = 7;
                int penalised = RideScore.Score(g, ride, 50, 50, history);
                Assert.Equal(plain / divisors[slot], penalised);
            }

            // An id that is not in the history is untouched.
            Assert.Equal(plain, RideScore.Score(g, ride, 50, 50, new[] { 1, 2, 3, 4 }));
        }

        // The type-4 happiness bonus only exists for the product kinds that have a threshold, and only
        // above it. REJECTS applying it to every shop or from happiness 0.
        [Theory]
        [InlineData(2, 60, false)]   // kind 2, below the 70 threshold
        [InlineData(2, 90, true)]
        [InlineData(6, 72, false)]   // kind 6's threshold is 75, not 70
        [InlineData(6, 90, true)]
        [InlineData(1, 100, false)]  // a kind with no threshold never gets the bonus
        public void TheShopHappinessBonusNeedsTheRightKindAndEnoughHappiness(int kind, int happiness, bool helps)
        {
            var g = Guest(); g.Happiness = happiness;
            var plainGuest = Guest(); plainGuest.Happiness = happiness;

            int withBonus = RideScore.Score(g, Ride(type: 4, kind: kind, slot54: 1), 50, 50);
            int asRide = RideScore.Score(plainGuest, Ride(type: 1, kind: kind, slot54: 1), 50, 50);
            Assert.Equal(helps, withBonus != asRide);
        }

        // ⚠ Flagged in the code and pinned here so it cannot be "tidied" silently: the second desire
        // term is indexed by NAUSEA, so a queasy guest scores a ride HIGHER. If this is ever shown to be
        // a misreading of the report, this test is the thing that will fail and say so.
        [Fact]
        public void NauseaCurrentlyRaisesARidesScore()
        {
            var well = Guest(); well.Nausea = 0;
            var queasy = Guest(); queasy.Nausea = 90;
            var ride = Ride(slot54: 1);
            Assert.True(RideScore.Score(queasy, ride, 50, 50) > RideScore.Score(well, ride, 50, 50));
        }

        // ⭐ ONCE EVERY 8 TICKS, STAGGERED. REJECTS running every tick, which would multiply the
        // pathfinder's load eightfold, and REJECTS a shared phase, which would make every guest in the
        // park re-plan on the same tick.
        [Fact]
        public void TheDecisionRunsOnceEveryEightTicksOnItsOwnPhase()
        {
            var g = Guest(); g.DecisionStagger = 3;
            var world = new World();
            world.Attractions.Add(Ride(dist: 1, slot54: 1));

            int ran = 0;
            for (long t = 0; t < 16; t++)
            {
                world.NowTick = t;
                g.HasTarget = false;
                if (VisitorDecision.Tick(g, world, new Dice(0)) != DecisionOutcome.NotMyTick) ran++;
            }
            Assert.Equal(2, ran);                       // ticks 3 and 11

            var other = Guest(); other.DecisionStagger = 4;
            world.NowTick = 3; other.HasTarget = false;
            Assert.Equal(DecisionOutcome.NotMyTick, VisitorDecision.Tick(other, world, new Dice(0)));
        }

        // An empty park costs happiness and adds boredom, and puts the guest back to Idle with a long
        // cooldown so it does not retry every tick.
        [Fact]
        public void AnEmptyParkDisappointsTheGuestAndBacksOff()
        {
            var g = Guest(); g.DecisionStagger = 0; g.Happiness = 50; g.Boredom = 0;
            g.PushState(VisitorState.MajorDecision);
            var world = new World { NowTick = 0 };      // no attractions at all

            Assert.Equal(DecisionOutcome.NothingWorthDoing, VisitorDecision.Tick(g, world, new Dice(0)));
            Assert.Equal(40, g.Happiness);              // -10
            Assert.Equal(5, g.Boredom);                 // +5
            Assert.Equal(VisitorDecision.BubbleNothingToDo, g.Bubble);
            Assert.Equal(VisitorState.Idle, g.State);   // popped back
            Assert.Equal(VisitorDecision.FailureCooldown, g.WaitUntil);
        }

        // ⚠ A REFUSED PATH COSTS THE SAME 360 TICKS as an empty park. paths.md §2 records that an
        // unreachable shop is chosen anyway and the request then fails -- this is the loop that produces
        // a park full of wandering guests and no sales, so the cooldown is the thing that keeps it from
        // being a per-tick spin.
        [Fact]
        public void ARefusedPathAlsoBacksOffAndReturnsToIdle()
        {
            var g = Guest(); g.DecisionStagger = 0;
            g.PushState(VisitorState.MajorDecision);
            var world = new World { NowTick = 0, PathAccepted = false };
            world.Attractions.Add(Ride(dist: 1, slot54: 1));

            Assert.Equal(DecisionOutcome.PathRefused, VisitorDecision.Tick(g, world, new Dice(0)));
            Assert.Equal(VisitorState.Idle, g.State);
            Assert.Equal(VisitorDecision.FailureCooldown, g.WaitUntil);
            Assert.False(g.HasTarget);
        }

        // A successful decision hands the guest to the walking state and resets the cooldown to now.
        [Fact]
        public void ASuccessfulDecisionSendsTheGuestWalking()
        {
            var g = Guest(); g.DecisionStagger = 0; g.WaitUntil = 9999;
            g.PushState(VisitorState.MajorDecision);
            var world = new World { NowTick = 800 };
            world.Attractions.Add(Ride(id: 42, dist: 1, slot54: 1));

            Assert.Equal(DecisionOutcome.HeadingThere, VisitorDecision.Tick(g, world, new Dice(0)));
            Assert.True(g.HasTarget);
            Assert.Equal(42, g.ChosenId);
            Assert.Equal(800, g.WaitUntil);
            Assert.Equal(VisitorState.WalkToBin, g.State);
        }

        // ⭐ THE HISTORY ONLY RECORDS WHEN THE GUEST REALLY WANTED IT (desire > 98) and never records a
        // shop. REJECTS remembering every choice, which would apply the repeat penalty to shops a guest
        // must revisit and to rides it picked out of indifference.
        [Fact]
        public void OnlyAKeenlyWantedRideGoesIntoTheRepeatHistory()
        {
            var keen = Guest(); keen.DecisionStagger = 0; keen.RideDesire = 99;
            var world = new World { NowTick = 0 };
            world.Attractions.Add(Ride(id: 5, dist: 1, slot54: 1));
            VisitorDecision.Tick(keen, world, new Dice(0));
            Assert.Equal(5, keen.RideHistory[0]);

            var lukewarm = Guest(); lukewarm.DecisionStagger = 0; lukewarm.RideDesire = 98;
            VisitorDecision.Tick(lukewarm, world, new Dice(0));
            Assert.Equal(-1, lukewarm.RideHistory[0]);   // 98 is not > 98

            var shopper = Guest(); shopper.DecisionStagger = 0; shopper.RideDesire = 100;
            var shops = new World { NowTick = 0 };
            shops.Attractions.Add(Ride(id: 9, type: 2, dist: 1, slot54: 1));
            VisitorDecision.Tick(shopper, shops, new Dice(0));
            Assert.Equal(-1, shopper.RideHistory[0]);    // shops are never remembered
        }

        // The history is a four-slot shift register, newest first.
        [Fact]
        public void TheHistoryKeepsTheLastFourNewestFirst()
        {
            var g = Guest();
            foreach (int id in new[] { 1, 2, 3, 4, 5 }) g.RememberRide(id);
            Assert.Equal(new[] { 5, 4, 3, 2 }, new[] { g.RideHistory[0], g.RideHistory[1], g.RideHistory[2], g.RideHistory[3] });
        }
    }
}
