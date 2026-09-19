namespace TPW.Sim
{
    /// <summary>The park's calendar: days into months into years.
    ///
    /// READ from 0x80066EA8 (economy.md §2). The game keeps this in the McAi block (pointer at gp
    /// 0x80102E48): +0 sub-day accumulator, +4 month 0..11, +8 year, +0xC day-of-month 0..N-1,
    /// +0x10 total days, +0x18 total months.
    ///
    /// ⚠ THIS COUNTS DAYS, NOT TICKS, AND THAT IS DELIBERATE. The original advances a sub-day
    /// accumulator by a per-tick `timescale` and rolls a day over at 0xF0000 (983040), so a day is
    /// ceil(983040 / timescale) ticks and a dropped frame makes a tick worth MORE calendar time. That
    /// coupling is a property of the original's frame pacing, not of the calendar, and reproducing it
    /// here would drag frame rate into the economy -- the exact bug <see cref="ParkClock"/>'s note
    /// exists to avoid. So <see cref="ParkClock"/> owns ticks-to-days (99 ticks, measured four ways)
    /// and this owns days-to-months. Feed it <see cref="AdvanceDay"/> on a day rollover.
    ///
    /// ⭐ The month lengths are the real ones and they are what confirm TicksPerDay independently:
    /// month rollovers measured at 2772 and 3069 ticks divide by 99 to give exactly 28 and 31 days.
    /// An hour-sized unit cannot produce that pair.
    ///
    /// ⚠ NO LEAP YEARS. February is 28 days in the table at 0x800E1554 and nothing adjusts it, so a
    /// year is always 365 days. Do not "fix" this.</summary>
    public sealed class Calendar
    {
        /// <summary>Days in each month, from the table at 0x800E1554 (READ). Index 0 = January.</summary>
        public static readonly int[] MonthLengths = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        /// <summary>Days in a year: the month lengths summed, 365. No leap year exists in the game.</summary>
        public const int DaysPerYear = 365;

        /// <summary>Day of the current month, 0-based (McAi+0xC). The game shows it 1-based.</summary>
        public int Day { get; private set; }

        /// <summary>Month of the current year, 0..11 (McAi+4). 0 = January.</summary>
        public int Month { get; private set; }

        /// <summary>Years since the park opened (McAi+8), starting at 0.</summary>
        public int Year { get; private set; }

        /// <summary>Days since the park opened (McAi+0x10). Wages pro-rate against this.</summary>
        public int TotalDays { get; private set; }

        /// <summary>Months since the park opened (McAi+0x18).
        ///
        /// ⚠ READ WITH A CAVEAT. economy.md §2 places `McAi+0x18++` after the month-changed branch in
        /// 0x80066C50, and the field is described as total months elapsed, so it is incremented here on
        /// a month rollover. The report's wording does not make it unambiguous whether the original
        /// increments it every tick instead; if a live reading ever shows this running at tick rate,
        /// this is the line to change and the field is misnamed rather than miscounted.</summary>
        public int TotalMonths { get; private set; }

        /// <summary>Length of the month now in progress.</summary>
        public int CurrentMonthLength => MonthLengths[Month];

        /// <summary>True when the last <see cref="AdvanceDay"/> ended a month. The edge, not the state --
        /// this is what drives the month rollover, so it must be read on the tick it happens.</summary>
        public bool MonthRolledOver { get; private set; }

        /// <summary>True when the last <see cref="AdvanceDay"/> ended a year.</summary>
        public bool YearRolledOver { get; private set; }

        /// <summary>The length of the month that just ENDED, or -1 if none did.
        ///
        /// ⭐ This is the number monthly wages pro-rate against, not the length of the month now
        /// starting. 0x80069314 is called with month-1 for exactly that reason (economy.md §3.1), and
        /// getting it wrong shifts every part-month wage by up to three days' worth.</summary>
        public int LengthOfMonthJustEnded { get; private set; } = -1;

        /// <summary>True on a day the weekly objectives check runs: day-of-month 0, 7, 14, 21, 28
        /// (0x80067928, READ). It moves no money -- it posts award messages 0xAF-0xB6 and 0xBC.
        ///
        /// ⚠ It is day-of-MONTH modulo 7, not a real week, so it fires twice in three days at every
        /// month boundary (28 then 0). That is the original's behaviour, not an error here.</summary>
        public bool IsObjectiveDay => Day % 7 == 0;

        /// <summary>Advance one day, rolling the month and year as needed. Call once per day rollover.</summary>
        public void AdvanceDay()
        {
            MonthRolledOver = false;
            YearRolledOver = false;
            LengthOfMonthJustEnded = -1;

            Day++;
            TotalDays++;
            if (Day < MonthLengths[Month]) return;

            LengthOfMonthJustEnded = MonthLengths[Month];
            Day = 0;
            Month++;
            MonthRolledOver = true;
            TotalMonths++;
            if (Month < 12) return;

            Month = 0;
            Year++;
            YearRolledOver = true;
        }

        /// <summary>Restore a calendar to a known point, for fixtures that compare against a savestate.</summary>
        public void RestoreTo(int year, int month, int day, int totalDays, int totalMonths)
        {
            Year = year; Month = month; Day = day;
            TotalDays = totalDays; TotalMonths = totalMonths;
            MonthRolledOver = false; YearRolledOver = false; LengthOfMonthJustEnded = -1;
        }
    }
}
