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

        [Fact]
        public void OneAttractionAddsTheBase()
        {
            // a level-0 ride contributes nothing itself, so the score is exactly the base
            var one = new[] { new AttractionDraw(0, 0, builtToday: false) };
            Assert.Equal(BusLoad.BaseScore, BusLoad.ParkScore(one));
        }

        // ⭐ REJECTS THE DELAY-SLOT MISREADING. An earlier report had level*intensity gated on the ride being
        // new. It is not -- that instruction sits in a branch delay slot and runs on both paths. Only the +20
        // is young-only. If the bonus were gated, these two would be equal.
        [Fact]
        public void LevelTimesIntensityCountsWhateverTheAge()
        {
            var old = new[] { new AttractionDraw(3, 4, builtToday: false) };
            var young = new[] { new AttractionDraw(3, 4, builtToday: true) };
            Assert.Equal(BusLoad.BaseScore + 12, BusLoad.ParkScore(old));
            Assert.Equal(BusLoad.BaseScore + 12 + 20, BusLoad.ParkScore(young));
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
