using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>The calendar. Expected values worked from the month-length table at 0x800E1554.</summary>
    public class CalendarTests
    {
        static Calendar After(int days)
        {
            var c = new Calendar();
            for (int i = 0; i < days; i++) c.AdvanceDay();
            return c;
        }

        // ⭐ The month lengths are what independently confirm TicksPerDay: rollovers measured at 2772
        // and 3069 ticks divide by 99 to give exactly 28 and 31. If this table is wrong, that
        // corroboration silently stops meaning anything.
        [Fact]
        public void TheMonthLengthsAreTheRealOnesAndSumToAYear()
        {
            Assert.Equal(12, Calendar.MonthLengths.Length);
            Assert.Equal(new[] { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 }, Calendar.MonthLengths);
            int sum = 0;
            foreach (int m in Calendar.MonthLengths) sum += m;
            Assert.Equal(Calendar.DaysPerYear, sum);
            Assert.Equal(365, sum);
        }

        // REJECTS an off-by-one in the rollover: day 30 of a 31-day January is still January, day 31 is
        // February the 1st. Both directions fail if the comparison is > instead of >=, or <= instead of <.
        [Theory]
        [InlineData(0, 0, 0)]      // opening day
        [InlineData(30, 0, 30)]    // last day of January (0-based day 30)
        [InlineData(31, 1, 0)]     // first day of February
        [InlineData(58, 1, 27)]    // last day of February -- 28 days, so 0-based 27
        [InlineData(59, 2, 0)]     // first of March: proves February is 28, not 29
        public void DaysRollIntoMonthsAtTheTablesLengths(int days, int month, int dayOfMonth)
        {
            var c = After(days);
            Assert.Equal(month, c.Month);
            Assert.Equal(dayOfMonth, c.Day);
            Assert.Equal(days, c.TotalDays);
        }

        // ⚠ NO LEAP YEAR. 365 days lands exactly on the first day of year 1, and a leap-year rule would
        // put it one short. Worth pinning because adding leap years is the obvious "improvement".
        [Fact]
        public void AYearIsAlwaysThreeHundredAndSixtyFiveDays()
        {
            var c = After(Calendar.DaysPerYear);
            Assert.Equal(1, c.Year);
            Assert.Equal(0, c.Month);
            Assert.Equal(0, c.Day);
            Assert.Equal(12, c.TotalMonths);

            var mid = After(Calendar.DaysPerYear - 1);   // 31 Dec of year 0
            Assert.Equal(0, mid.Year);
            Assert.Equal(11, mid.Month);
            Assert.Equal(30, mid.Day);
        }

        // ⭐ THE LENGTH OF THE MONTH THAT ENDED, NOT THE ONE STARTING. Monthly wages pro-rate against
        // this and the original passes month-1 to get it (0x80069314). REJECTS reading the new month's
        // length: on the Jan->Feb roll those are 31 and 28, so the wrong one misprices every part-month
        // wage by three days.
        [Fact]
        public void TheRolloverReportsTheLengthOfTheMonthThatJustEnded()
        {
            var c = new Calendar();
            for (int i = 0; i < 30; i++) c.AdvanceDay();      // through 31 Jan, no roll yet
            Assert.False(c.MonthRolledOver);
            Assert.Equal(-1, c.LengthOfMonthJustEnded);

            c.AdvanceDay();                                   // into February
            Assert.True(c.MonthRolledOver);
            Assert.Equal(31, c.LengthOfMonthJustEnded);       // January's length, not February's 28
            Assert.Equal(28, c.CurrentMonthLength);

            for (int i = 0; i < 27; i++) c.AdvanceDay();      // through February
            Assert.False(c.MonthRolledOver);
            c.AdvanceDay();                                   // into March
            Assert.Equal(28, c.LengthOfMonthJustEnded);       // now February's
        }

        // The edges are edges, not states: they must be true only on the day they happen, because the
        // month rollover hangs off MonthRolledOver and would otherwise fire every day for a month.
        [Fact]
        public void TheRolloverFlagsAreEdgesAndClearOnTheNextDay()
        {
            var c = After(31);
            Assert.True(c.MonthRolledOver);
            c.AdvanceDay();
            Assert.False(c.MonthRolledOver);
            Assert.Equal(-1, c.LengthOfMonthJustEnded);

            var y = After(Calendar.DaysPerYear);
            Assert.True(y.YearRolledOver);
            Assert.True(y.MonthRolledOver);     // a year ends a month too
            y.AdvanceDay();
            Assert.False(y.YearRolledOver);
        }

        // ⚠ IT IS DAY-OF-MONTH MODULO 7, NOT A REAL WEEK, so it fires on the 28th and again two days
        // later on the 1st of a 30-day month. That looks like a bug and is the original's behaviour.
        // REJECTS "fix" it to a running day count, which would never produce two checks three days apart.
        [Fact]
        public void TheObjectiveCheckRunsOnDayOfMonthMultiplesOfSeven()
        {
            var c = new Calendar();
            Assert.True(c.IsObjectiveDay);                       // day 0
            for (int d = 1; d < 31; d++)
            {
                c.AdvanceDay();
                Assert.Equal(d % 7 == 0, c.IsObjectiveDay);      // 7, 14, 21, 28 within January
            }
            c.AdvanceDay();                                       // 1 Feb, day 0
            Assert.True(c.IsObjectiveDay);                        // fires again three days after the 28th
        }
    }
}
