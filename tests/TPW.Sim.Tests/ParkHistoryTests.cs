using System.Collections.Generic;
using System.Linq;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>0x800670D4's monthly rings and park statistic 46 (findings/happiness.md §4.3).</summary>
    public class ParkHistoryTests
    {
        sealed class Dice : IRandomSource { public int Next(int n) => 0; }

        static Visitor Guest(int happiness, long arrivedOnDay = 0)
        {
            var v = Visitor.Spawn(new Dice(), 0);
            v.Happiness = happiness;
            v.ArrivedOnDay = arrivedOnDay;
            return v;
        }

        static IEnumerable<Visitor> Guests(params int[] happiness) => happiness.Select(h => Guest(h)).ToList();

        // Σ / count, truncated, 0 with no guests. REJECTS rounding to nearest (33.5 → 34), REJECTS a
        // "no guests" default of 50 or 100, and REJECTS averaging the condition code instead of the byte.
        [Fact]
        public void MeanHappinessTruncatesAndIsZeroForAnEmptyPark()
        {
            Assert.Equal(0, ParkHistory.MeanHappiness(new Visitor[0]));
            Assert.Equal(33, ParkHistory.MeanHappiness(Guests(33, 34)));
            Assert.Equal(66, ParkHistory.MeanHappiness(Guests(100, 100, 0)));
            Assert.Equal(100, ParkHistory.MeanHappiness(Guests(100)));
        }

        // The slot is months BEFORE the increment, modulo 144. REJECTS using the post-increment count
        // (slot 1 for the first month), REJECTS an unbounded list (month 144 must land on slot 0 again),
        // and REJECTS a ring of 12 or 120.
        [Fact]
        public void TheSlotIsThePreIncrementMonthCountModulo144()
        {
            var h = new ParkHistory();
            h.RecordMonth(monthsBefore: 0, month: 1, year: 0, totalDays: 31, Guests(10), rating: 0);
            Assert.Equal(10, h.Read(HistoryRow.Happiness, months: 1, n: 0));   // months is 1 AFTER the tick; the page passes 0
            h.RecordMonth(monthsBefore: 1, month: 2, year: 0, totalDays: 59, Guests(20), rating: 0);
            Assert.Equal(20, h.Read(HistoryRow.Happiness, months: 2, n: 0));

            h.RecordMonth(monthsBefore: 144, month: 1, year: 12, totalDays: 4400, Guests(77), rating: 0);
            Assert.Equal(77, h.Read(HistoryRow.Happiness, months: 145, n: 0));   // slot 0, overwritten
            Assert.Equal(20, h.Read(HistoryRow.Happiness, months: 145, n: 144)); // slot 1, month 1's value
            Assert.Equal(0, h.Read(HistoryRow.Happiness, months: 145, n: 145));  // refused: n is not < months
        }

        // The arrival rate reads last month's head count through the accessor, which returns 0 when
        // n ≥ months. REJECTS reading people[slot − 1] directly at the second month (it would be 3, not
        // 0, and the rate 2, not 5), REJECTS a signed delta (a drop must read 0, not −2), and REJECTS
        // counting arrivals rather than the net change.
        [Fact]
        public void ArrivalRateIsThePopulationDeltaClampedAtZeroAndBlindForTwoMonths()
        {
            var h = new ParkHistory();
            h.RecordMonth(0, 1, 0, 31, Guests(50, 50, 50), 0);
            Assert.Equal(3, h.Read(HistoryRow.ArrivalRate, months: 1, n: 0));

            h.RecordMonth(1, 2, 0, 59, Guests(50, 50, 50, 50, 50), 0);
            Assert.Equal(5, h.Read(HistoryRow.ArrivalRate, months: 2, n: 0));   // NOT 5 − 3

            h.RecordMonth(2, 3, 0, 90, Guests(50, 50, 50, 50, 50, 50, 50), 0);
            Assert.Equal(2, h.Read(HistoryRow.ArrivalRate, months: 3, n: 0));   // 7 − 5

            h.RecordMonth(3, 4, 0, 120, Guests(50, 50, 50, 50, 50), 0);
            Assert.Equal(0, h.Read(HistoryRow.ArrivalRate, months: 4, n: 0));   // 5 − 7 → 0
            Assert.Equal(5, h.Read(HistoryRow.People, months: 4, n: 0));
            Assert.Equal(5, h.Read(HistoryRow.People, months: 4, n: 1));        // raw 1 reads the same slot as raw 0
            Assert.Equal(7, h.Read(HistoryRow.People, months: 4, n: 2));        // two back
        }

        // Happiness and time in park are means over THIS month's guests and read 0 for an empty park.
        // REJECTS carrying the previous slot's value forward, REJECTS summing instead of averaging, and
        // REJECTS time in park measured from 0 rather than from the guest's arrival day.
        [Fact]
        public void HappinessAndTimeInParkAreMeansOfThisMonthsGuests()
        {
            var h = new ParkHistory();
            var guests = new[] { Guest(80, arrivedOnDay: 100), Guest(20, arrivedOnDay: 90) };
            h.RecordMonth(monthsBefore: 5, month: 6, year: 0, totalDays: 120, guests, rating: 9);
            Assert.Equal(50, h.Read(HistoryRow.Happiness, months: 6, n: 0));
            Assert.Equal(25, h.Read(HistoryRow.TimeInPark, months: 6, n: 0));   // (20 + 30) / 2
            Assert.Equal(9, h.Read(HistoryRow.Overall, months: 6, n: 0));

            h.RecordMonth(6, 7, 0, 151, new Visitor[0], rating: 4);
            Assert.Equal(0, h.Read(HistoryRow.Happiness, months: 7, n: 0));
            Assert.Equal(0, h.Read(HistoryRow.TimeInPark, months: 7, n: 0));
            Assert.Equal(0, h.Read(HistoryRow.People, months: 7, n: 0));
            Assert.Equal(4, h.Read(HistoryRow.Overall, months: 7, n: 0));
            Assert.Equal(50, h.Read(HistoryRow.Happiness, months: 7, n: 2));    // last month's still there
        }

        // Every row is stored with `sb`. REJECTS a 16-bit or wider store: 256 guests read as 0, and a
        // mean time in park of 300 days reads as 44.
        [Fact]
        public void RowsAreBytes()
        {
            var h = new ParkHistory();
            var crowd = Enumerable.Range(0, 256).Select(_ => Guest(50, arrivedOnDay: 0)).ToList();
            h.RecordMonth(monthsBefore: 2, month: 3, year: 0, totalDays: 300, crowd, rating: 300);
            Assert.Equal(0, h.Read(HistoryRow.People, months: 3, n: 0));
            Assert.Equal(44, h.Read(HistoryRow.TimeInPark, months: 3, n: 0));
            Assert.Equal(44, h.Read(HistoryRow.Overall, months: 3, n: 0));
        }

        // McAi+0x2F8 takes the rating only at a January that is not the park's first. REJECTS storing it
        // every month, REJECTS storing it in year 0, and REJECTS keying on the year change alone (a
        // February of year 1 must not write it).
        [Fact]
        public void TheNewYearRatingIsTakenOnlyInAJanuaryAfterTheFirstYear()
        {
            var h = new ParkHistory();
            h.RecordMonth(monthsBefore: 0, month: 0, year: 0, totalDays: 0, Guests(), rating: 7);
            Assert.Equal(0, h.RatingAtNewYear);
            h.RecordMonth(11, 11, 0, 334, Guests(), rating: 8);
            Assert.Equal(0, h.RatingAtNewYear);
            h.RecordMonth(12, 0, 1, 365, Guests(), rating: 9);
            Assert.Equal(9, h.RatingAtNewYear);
            h.RecordMonth(13, 1, 1, 396, Guests(), rating: 3);
            Assert.Equal(9, h.RatingAtNewYear);
        }

        // The accessor: the RAW n must be < months (so raw 1 is refused at months 1 while raw 0 is not),
        // then n = 0 reads as n = 1, and the index wraps by adding 144. REJECTS guarding after the bump
        // (that would let raw 1 through at months 1), REJECTS n = 0 reading the unwritten current slot,
        // REJECTS a negative index, and REJECTS `n > months` (at n == months the original also refuses).
        [Theory]
        [InlineData(1, 0, 0)]
        [InlineData(1, 1, -1)]
        [InlineData(2, 0, 1)]
        [InlineData(2, 1, 1)]
        [InlineData(2, 2, -1)]
        [InlineData(145, 1, 0)]
        [InlineData(145, 2, 143)]
        [InlineData(145, 144, 1)]
        [InlineData(145, 145, -1)]
        [InlineData(0, 0, -1)]
        public void TheAccessorRefusesTheFutureAndWrapsThePast(int months, int n, int expected)
        {
            Assert.Equal(expected, ParkHistory.SlotFor(months, n));
        }
    }
}
