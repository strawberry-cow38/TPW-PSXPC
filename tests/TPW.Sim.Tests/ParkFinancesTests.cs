using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>The finances driven by a real clock, which is where the tick/day/month units meet.</summary>
    public class ParkFinancesTests
    {
        static Money P(long pounds) => Money.FromPounds(pounds);

        /// <summary>Run a real ParkClock for n ticks, feeding every tick to the finances exactly as the
        /// host's loop does. Deliberately NOT a shortcut that calls OnDayRollover directly -- the unit
        /// conversion is the thing under test.</summary>
        static ParkFinances RunTicks(int ticks, Money opening, System.Func<Money> wages = null)
        {
            var clock = new ParkClock();
            var fin = new ParkFinances(opening, wages);
            for (int i = 0; i < ticks; i++) { clock.Advance(); fin.OnTick(clock); }
            return fin;
        }

        // ⭐ THE UNITS ALL THE WAY THROUGH: 99 ticks to a day, 31 days to January. REJECTS a host that
        // advances the calendar every tick rather than on the day edge -- that would run 3069 days here
        // and roll about a hundred months instead of one.
        [Fact]
        public void AMonthOfTicksRollsExactlyOneMonth()
        {
            int ticksInJanuary = 31 * ParkClock.TicksPerDay;     // 3069

            var justBefore = RunTicks(ticksInJanuary - 1, P(1000));
            Assert.Equal(0, justBefore.MonthsRun);
            Assert.Equal(0, justBefore.Calendar.Month);

            var onTheEdge = RunTicks(ticksInJanuary, P(1000));
            Assert.Equal(1, onTheEdge.MonthsRun);
            Assert.Equal(1, onTheEdge.Calendar.Month);           // February
            Assert.Equal(0, onTheEdge.Calendar.Day);
            Assert.Equal(31, onTheEdge.Calendar.TotalDays);
        }

        // A year of ticks is twelve rollovers and one year, not eleven or thirteen. This is the whole
        // chain -- ticks, days, months, years -- checked against a number nothing in the chain states.
        [Fact]
        public void AYearOfTicksRollsTwelveMonthsAndOneYear()
        {
            var fin = RunTicks(Calendar.DaysPerYear * ParkClock.TicksPerDay, P(1000));
            Assert.Equal(12, fin.MonthsRun);
            Assert.Equal(1, fin.Calendar.Year);
            Assert.Equal(0, fin.Calendar.Month);
            Assert.Equal(365, fin.Calendar.TotalDays);
        }

        // ⚠ NOTHING HAPPENS TO THE MONEY ON A DAY. The day rollover drives the objectives check and
        // guests' timers and moves no money at all. REJECTS adding a daily drip: with no staff and no
        // loans the balance must be untouched after a month of ticks, not merely close to it.
        [Fact]
        public void TicksAndDaysMoveNoMoneyByThemselves()
        {
            var fin = RunTicks(31 * ParkClock.TicksPerDay, P(1234));
            Assert.Equal(1, fin.MonthsRun);                      // a month really did roll
            Assert.Equal(P(1234), fin.Bank.Balance);             // and it cost nothing
            Assert.Equal(Money.Zero, fin.LastMonthEnd.Value.TotalCharged);
        }

        // Wages are asked for once per month end, not once per day and not once per tick. REJECTS
        // wiring the callback to the wrong edge, which is invisible in the balance if the roster is
        // empty and catastrophic the moment it is not.
        [Fact]
        public void WagesAreAskedForOncePerMonthEnd()
        {
            int asked = 0;
            var fin = RunTicks(2 * 31 * ParkClock.TicksPerDay, P(10000), () => { asked++; return P(100); });

            Assert.Equal(2, fin.MonthsRun);                      // January and February...
            Assert.Equal(fin.MonthsRun, asked);                  // ...and exactly that many wage bills
            Assert.Equal(P(10000 - 100 * asked), fin.Bank.Balance);
        }

        // The loan is paid by the rollover the host drives, not by anything the host has to remember.
        [Fact]
        public void ALoanIsRepaidByTheMonthsThemselves()
        {
            var clock = new ParkClock();
            var fin = new ParkFinances(P(1000));
            fin.Loans[0].Grant(P(300), P(100), months: 3);

            for (int i = 0; i < 31 * ParkClock.TicksPerDay; i++) { clock.Advance(); fin.OnTick(clock); }
            Assert.Equal(P(200), fin.Loans.TotalOutstanding);
            Assert.Equal(P(900), fin.Bank.Balance);

            // February (28 days) then March (31): two more payments clear it.
            for (int i = 0; i < 59 * ParkClock.TicksPerDay; i++) { clock.Advance(); fin.OnTick(clock); }
            Assert.Equal(3, fin.MonthsRun);
            Assert.Equal(Money.Zero, fin.Loans.TotalOutstanding);
            Assert.Equal(P(700), fin.Bank.Balance);
            Assert.False(fin.Loans.AnyOutstanding);
        }
    }
}
