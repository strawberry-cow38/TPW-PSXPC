using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class BusArrivalTests
    {
        // ⭐ THE DIVISOR IS NOT MEASURED, SO NOTHING HERE DEPENDS ON ONE VALUE OF IT. Every case that involves
        // the draw term is asserted at BOTH ends of the derived range. If the measured input/output pairs hold
        // across the whole window, the window is self-consistent and these tests keep their meaning when the
        // true value is finally read out. Pinning a guessed constant instead would produce tests that pass
        // because they agree with the guess.
        public static IEnumerable<object[]> Divisors => new List<object[]>
        {
            new object[] { BusLoad.DerivedDivisorLowerExclusive + 1 },
            new object[] { BusLoad.DerivedDivisorUpperInclusive },
        };

        const int Roomy = 1000;      // capacity well above anything these cases reach
        const int NoLanes = 0;

        // MEASURED: the fixture park scores 20..24 and every bus brings exactly one guest, 11 times out of 11.
        [Theory]
        [MemberData(nameof(Divisors))]
        public void FixtureParkScoreGivesOneGuest(int divisor)
        {
            for (int s = 20; s <= 24; s++)
                Assert.Equal(1, BusLoad.HeadCount(s, Roomy, 0, NoLanes, divisor));
        }

        // MEASURED: forcing the bonus word to 100 gives 7 guests a bus. This is the pair that establishes the
        // +10 base -- without it the score would be 110..114 and this would be 6.
        [Theory]
        [MemberData(nameof(Divisors))]
        public void BonusOfOneHundredGivesSevenGuests(int divisor)
        {
            for (int s = 20; s <= 24; s++)
                Assert.Equal(7, BusLoad.HeadCount(s, Roomy, 0, NoLanes, divisor, bonus: 100));
        }

        // ⭐ REJECTS DROPPING THE +10. A 16-day-old level-0 ride sums to 10..14 on its own; with the base it is
        // 20..24. Without the base the head-count floors to zero and the park receives nobody while looking
        // entirely healthy -- the failure this constant exists to prevent.
        [Theory]
        [MemberData(nameof(Divisors))]
        public void WithoutTheBaseTheParkWouldReceiveNobody(int divisor)
        {
            Assert.Equal(0, BusLoad.HeadCount(14, Roomy, 0, NoLanes, divisor));   // what the sum alone gives
            Assert.Equal(1, BusLoad.HeadCount(24, Roomy, 0, NoLanes, divisor));   // what the game actually does
        }

        [Fact]
        public void EmptyParkScoresZeroNotTheBase()
        {
            Assert.Equal(0, BusLoad.ParkScore(new AttractionDraw[0]));
            Assert.Equal(0, BusLoad.ParkScore(null));
        }

        /// <summary>A die that always shows the same face, so the roll inside the score can be pinned.</summary>
        sealed class Roll : IRandomSource
        {
            readonly int _face;
            public Roll(int face) => _face = face;
            public int Next(int n) => _face;
        }

        // ⭐⭐ REJECTS THE FORMULA THAT MADE EVERY NEW PARK DEAD. A freshly built ride is level 0 (placement
        // sets it; the upgrade path is `if level >= 3 return; level++`), so level*intensity is exactly zero.
        // The old port scored `base + level*intensity` and therefore stayed at the bare 10, the head-count
        // floored, and the bus turned up on time with nobody on it forever while every number looked healthy.
        // What carries a new park is the ROLL: 20..29 halved is 10..14, which is precisely the contribution
        // the live measurement recorded for a 16-day-old level-0 ride.
        [Fact]
        public void ALevelZeroRideContributesTheRollNotNothing()
        {
            var one = new[] { new AttractionDraw(3, 0, 90, builtToday: false) };
            Assert.Equal(BusLoad.BaseScore + 10, BusLoad.ParkScore(one, new Roll(0)));   // (20 + 0) / 2
            Assert.Equal(BusLoad.BaseScore + 14, BusLoad.ParkScore(one, new Roll(9)));   // (29 + 0) / 2
        }

        // ⭐ REJECTS THE DELAY-SLOT MISREADING. An earlier report had level*intensity gated on the ride being
        // new. It is not -- that instruction sits in a branch delay slot and runs on both paths. Only the +20
        // is young-only. If the bonus were gated, these two would be equal.
        [Fact]
        public void LevelTimesIntensityCountsWhateverTheAge()
        {
            var old = new[] { new AttractionDraw(3, 3, 4, builtToday: false) };
            var young = new[] { new AttractionDraw(3, 3, 4, builtToday: true) };
            Assert.Equal(BusLoad.BaseScore + (20 + 12) / 2, BusLoad.ParkScore(old, new Roll(0)));
            Assert.Equal(BusLoad.BaseScore + (20 + 12 + 20) / 2, BusLoad.ParkScore(young, new Roll(0)));
        }

        // ⭐ THE SEVEN KINDS ARE NOT SCORED ALIKE, which one formula for all of them could never express: the
        // score branches through a jump table at 0x800E1564 where the four RIDE kinds share a handler and the
        // other three have their own. A feature divides by ten instead of two, a shop adds nothing but the
        // roll, and a sideshow adds its intensity outright since it has no level to multiply by.
        [Fact]
        public void TheKindsAreScoredDifferently()
        {
            Assert.Equal(BusLoad.BaseScore + 2,
                         BusLoad.ParkScore(new[] { new AttractionDraw(2, 0, 90, false) }, new Roll(0)));   // feature: 20 / 10
            Assert.Equal(BusLoad.BaseScore + 10,
                         BusLoad.ParkScore(new[] { new AttractionDraw(4, 0, 90, false) }, new Roll(0)));   // shop: 20 / 2
            Assert.Equal(BusLoad.BaseScore + 55,
                         BusLoad.ParkScore(new[] { new AttractionDraw(5, 0, 90, false) }, new Roll(0)));   // sideshow: (20 + 90) / 2
        }

        // ⭐ THE SCORE IS RANDOM PER CALL, and that is the mechanism behind a pattern an earlier report
        // recorded and could not explain: batch sizes of 5, 5, 4, 6, 3 and 6 guests with the park unchanged.
        // A roll per attraction per call is enough on its own.
        [Fact]
        public void TheSameParkDoesNotScoreTheSameTwice()
        {
            var park = new[] { new AttractionDraw(3, 0, 90, false), new AttractionDraw(3, 0, 90, false) };
            Assert.NotEqual(BusLoad.ParkScore(park, new Roll(0)), BusLoad.ParkScore(park, new Roll(9)));
        }

        // The headroom term is SOURCED and does cap the bus, so these cases stand. ⚠ But note what they do
        // NOT establish: I claimed this term explains the live batch-size pattern, and measurement said no --
        // batch 2 and batch 1 both occur at 5 and at 6 guests. These assert the expression's arithmetic, which
        // is all they ever could; the mechanism behind the observed pattern is open. Keeping that distinction
        // visible here, because a passing test next to a withdrawn claim is how the claim creeps back.
        [Theory]
        [MemberData(nameof(Divisors))]
        public void RemainingHeadroomCapsTheBus(int divisor)
        {
            const int score = 24;
            Assert.Equal(1, BusLoad.HeadCount(score, capacity: 10, guestsNow: 0, NoLanes, divisor));
            Assert.Equal(1, BusLoad.HeadCount(score, capacity: 10, guestsNow: 9, NoLanes, divisor));
            Assert.Equal(0, BusLoad.HeadCount(score, capacity: 10, guestsNow: 10, NoLanes, divisor));
            Assert.Equal(0, BusLoad.HeadCount(score, capacity: 10, guestsNow: 20, NoLanes, divisor));  // over capacity
        }

        [Theory]
        [MemberData(nameof(Divisors))]
        public void LanesCapTheBus(int divisor)
            => Assert.Equal(0, BusLoad.HeadCount(24, Roomy, 0, lanes: 20, divisor));

        [Theory]
        [MemberData(nameof(Divisors))]
        public void AnEmptyParkGetsNobodyEvenWithRoomAndNoLanes(int divisor)
            => Assert.Equal(0, BusLoad.HeadCount(0, Roomy, 0, NoLanes, divisor));

        // ⭐ THE CADENCE IS FIXED. This is the property that makes it a timetable rather than a rate: a better
        // park must not change the spacing. If someone later "improves" arrivals by shortening the interval,
        // this fails, which is the point.
        [Fact]
        public void BusesArriveOnAFixedInterval()
        {
            var bus = new BusSchedule();
            var arrivals = new List<long>();
            for (long t = 1; t <= 694 * 5; t++) if (bus.IsArrivalTick(t)) arrivals.Add(t);

            Assert.Equal(5, arrivals.Count);
            for (int i = 1; i < arrivals.Count; i++)
                Assert.Equal(BusSchedule.DefaultIntervalTicks, arrivals[i] - arrivals[i - 1]);
        }

        [Fact]
        public void TheMeasuredIntervalIsSixNinetyFour() => Assert.Equal(694, BusSchedule.DefaultIntervalTicks);

        [Fact]
        public void AskingTwiceOnTheSameTickDoesNotProduceTwoBuses()
        {
            var bus = new BusSchedule();
            Assert.True(bus.IsArrivalTick(694));
            Assert.False(bus.IsArrivalTick(694));
            Assert.Equal(1, bus.BusesArrived);
        }
    }
}
