using System;

namespace TPW.Sim
{
    /// <summary>Everything the park's money does on a schedule, in one place.
    ///
    /// Owns the <see cref="Calendar"/>, the <see cref="Bank"/>, the four loan slots and the
    /// <see cref="DebtWatch"/>, and drives the month rollover off the calendar's own edge. A host
    /// advances <see cref="ParkClock"/> and calls <see cref="OnDayRollover"/> on each day boundary;
    /// nothing here knows about frames, rendering or Godot, so it stays testable on its own.
    ///
    /// ⚠ THE HOST MUST CALL THIS ON THE DAY EDGE, NOT EVERY TICK. <see cref="ParkClock.IsDayRollover"/>
    /// is an edge and is only true on the tick a day begins; calling this every tick would run a month
    /// of calendar in a second. The tick loop spends whole ticks one at a time precisely so that edge
    /// cannot be missed or doubled.</summary>
    public sealed class ParkFinances
    {
        readonly Func<Money> _monthlyWages;

        /// <param name="opening">Starting balance. Use <see cref="ParkEconomy.OpeningBalance"/>, £50,000.
        ///
        /// ⚠ I PREVIOUSLY MARKED THIS "NOT ESTABLISHED" AND THAT WAS WRONG. economy.md §1.2 has it READ
        /// at 0x80058A94 in the game-mode init, and I asserted the absence without looking - the figure
        /// was in the report the whole time. Left as a parameter because the BANK constructor's own
        /// default (£10,000) is a different number for a bank built outside that init, so the caller
        /// has to say which it means.</param>
        /// <param name="monthlyWages">Total staff wages for the month just ended, summed by the caller
        /// over its roster with <see cref="Wages.Monthly"/>. Defaults to nothing, because the port has
        /// no staff yet -- and a park with no staff genuinely owes no wages, so the default is the
        /// correct answer rather than a placeholder.</param>
        public ParkFinances(Money opening, Func<Money> monthlyWages = null)
        {
            Bank = new Bank(opening);
            _monthlyWages = monthlyWages;
        }

        public Calendar Calendar { get; } = new Calendar();
        public Bank Bank { get; }
        public LoanBook Loans { get; } = new LoanBook();
        public DebtWatch Debt { get; } = new DebtWatch();

        /// <summary>The most recent month end, or null before the first one.</summary>
        public MonthEndResult? LastMonthEnd { get; private set; }

        /// <summary>Months rolled over since the park opened. Equals <c>Calendar.TotalMonths</c> unless
        /// a host has driven the calendar directly, which it should not.</summary>
        public int MonthsRun { get; private set; }

        /// <summary>Advance one day and, if that ended a month, run the rollover.
        /// Returns the month end when one happened, otherwise null.</summary>
        public MonthEndResult? OnDayRollover()
        {
            Calendar.AdvanceDay();
            if (!Calendar.MonthRolledOver) return null;

            Money wages = _monthlyWages?.Invoke() ?? Money.Zero;
            var result = MonthRollover.Run(Bank, Loans, wages, Debt);
            LastMonthEnd = result;
            MonthsRun++;
            return result;
        }

        /// <summary>Convenience for a host that holds a clock: advances the calendar only on the tick a
        /// day actually begins. Safe to call every tick.</summary>
        public MonthEndResult? OnTick(ParkClock clock)
            => clock != null && clock.IsDayRollover ? OnDayRollover() : null;
    }
}
