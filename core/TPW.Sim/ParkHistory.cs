using System;
using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>The five rows of the "Visitor Information" page, named by their byte offset in the McAi
    /// block (findings/transport.md §4.1, READ): five 144-entry byte rings, one slot per calendar month.
    /// The numbers are the offsets because the accessors are five copies of one routine that differ only
    /// in the `lbu` offset (0x80066FA8 +40, 0x80066FE4 +184, 0x80067020 +328, 0x8006705C +472,
    /// 0x80067098 +616).</summary>
    public enum HistoryRow
    {
        /// <summary>Text 0x1E8 "People In Park".</summary>
        People = 0x28,
        /// <summary>Text 0x243 "Arrival Rate" -- a population delta, see <see cref="ParkHistory.RecordMonth"/>.</summary>
        ArrivalRate = 0xB8,
        /// <summary>Text 0x36B "Happiness": the mean of every guest's V+0x59 at the month change.</summary>
        Happiness = 0x148,
        /// <summary>Text 0x205 "Time In Park": the mean of `McAi+0x10 - V+0x54` (0x80091DFC).</summary>
        TimeInPark = 0x1D8,
        /// <summary>Text 0x2A6 "Overall Rating": 0x8005B830's number at the month change.</summary>
        Overall = 0x268,
    }

    /// <summary>The park's monthly history, written by 0x800670D4 from the McAi tick (READ, findings/happiness.md
    /// §4.3 and transport.md §4.2), and the mean-happiness statistic it shares its formula with.
    ///
    /// ⭐ THIS IS THE ONLY PLACE GUEST HAPPINESS LEAVES THE GUEST. Every other reader of V+0x59 decides
    /// something about that one guest; this writer and park statistic 46 are the two places the park
    /// sees the population's happiness, and both are a plain mean. Neither feeds the park rating
    /// (0x8005B830 reads object-manager counts, never a guest) nor the arrival score.
    ///
    /// ⚠ WRITTEN BEFORE THE MONTH COUNTER MOVES. The tick calls this at 0x80066DEC and increments
    /// McAi+0x18 at 0x80066E04, after it returns, so the slot is `months % 144` with months as it was
    /// BEFORE the increment. The read accessor then subtracts at least one from the incremented count,
    /// which is how "last month" lands on the slot just written. Pass <see cref="Calendar.TotalMonths"/>
    /// minus one after a rollover, or the pre-rollover value.</summary>
    public sealed class ParkHistory
    {
        /// <summary>144 slots per ring: `months % 144` (0x80067154..0x8006717C, the 0x38E38E39 multiply),
        /// twelve years of months. The 145th month overwrites the first.</summary>
        public const int Slots = 144;

        readonly byte[] _people = new byte[Slots];
        readonly byte[] _arrivalRate = new byte[Slots];
        readonly byte[] _happiness = new byte[Slots];
        readonly byte[] _timeInPark = new byte[Slots];
        readonly byte[] _overall = new byte[Slots];

        /// <summary>McAi+0x2F8: the park rating as it stood at the first month change of every year after
        /// the first (0x80067180..0x80067198: skipped when McAi+8 year is 0 or McAi+4 month is not 0).
        /// A byte, like the rings. ⚠ WHAT READS IT IS NOT TRACED: 0x8006892C and 0x80069314 load it and
        /// 0x80069034 also stores it; none of the three was followed. Carried because the writer writes it.</summary>
        public int RatingAtNewYear { get; private set; }

        /// <summary>Park statistic 46 (0x80016E90..0x80016EF8) and the Happiness row's value
        /// (0x800671CC..0x80067200): the sum of every guest's happiness divided by the guest count,
        /// truncated, and 0 when there are no guests (the divide is skipped, not performed).
        ///
        /// ⚠ NOT ROUNDED, and not capped in any way that matters: the statistic tests its result against
        /// 101 (`sltiu 0x65` at 0x80016EF4) but a mean of bytes in 0..100 cannot exceed 100, so the cap is
        /// dead code and is not reproduced. The statistic divides unsigned (`divu`) and the ring signed
        /// (`div`); for a non-negative sum they agree.</summary>
        public static int MeanHappiness(IEnumerable<Visitor> guests)
        {
            if (guests == null) throw new ArgumentNullException(nameof(guests));
            int count = 0, sum = 0;
            foreach (var g in guests) { count++; sum += g.Happiness; }
            return count == 0 ? 0 : sum / count;
        }

        /// <summary>0x800670D4(McAi): fill this month's slot in all five rings (READ throughout).
        ///
        /// <paramref name="monthsBefore"/> is McAi+0x18 before the tick increments it; <paramref name="month"/>
        /// and <paramref name="year"/> are McAi+4 and McAi+8 as already advanced by 0x80066EA8 (the tick
        /// updates the calendar first, then calls this); <paramref name="totalDays"/> is McAi+0x10.
        ///
        /// ⚠ "ARRIVAL RATE" IS `max(0, guests − last month's guests)` (0x800671AC..0x800671C8, max at
        /// 0x800693A4), read through the accessor, so for the first TWO month changes "last month" is 0
        /// and the row is just the head count: the accessor refuses `n ≥ months` (0x80066F4C..0x80066F54)
        /// and months is still 1 at the second change. Departures subtract, and a month that ends in an
        /// exodus reads 0. transport.md §4.2 has the formula; this is the guard it did not mention.
        ///
        /// ⚠ EVERY STORE IS A BYTE (`sb`). A head count of 256 records as 0, and a mean time in park past
        /// 255 days wraps. Reproduced, not fixed: the rows are what the page shows.</summary>
        public void RecordMonth(int monthsBefore, int month, int year, int totalDays, IEnumerable<Visitor> guests, int rating)
        {
            if (monthsBefore < 0) throw new ArgumentOutOfRangeException(nameof(monthsBefore));
            if (guests == null) throw new ArgumentNullException(nameof(guests));

            // The rating is computed before the guest loop (0x80067100), the loop sums happiness and time
            // in park and counts (0x80067114..0x8006714C). Time in park is 0x80091DFC: McAi+0x10 − V+0x54.
            int count = 0, happinessSum = 0; long timeSum = 0;
            foreach (var g in guests)
            {
                count++;
                happinessSum += g.Happiness;
                timeSum += totalDays - g.ArrivedOnDay;
            }

            int slot = monthsBefore % Slots;

            if (year != 0 && month == 0) RatingAtNewYear = rating & 0xFF;

            _people[slot] = (byte)count;
            int previous = Read(HistoryRow.People, monthsBefore, 1);
            _arrivalRate[slot] = (byte)Math.Max(0, count - previous);
            _happiness[slot] = (byte)(count == 0 ? 0 : happinessSum / count);
            _timeInPark[slot] = (byte)(count == 0 ? 0 : timeSum / count);
            _overall[slot] = (byte)rating;
        }

        /// <summary>The slot 0x80066F44(McAi, n) resolves, or -1 for "no such month": `n` months back from
        /// the month counter, with n forced to at least 1, wrapped into 0..143 by adding 144 until it is
        /// not negative (0x80066F84..0x80066F9C). -1 when `n ≥ months` (`sltu` at 0x80066F4C).
        ///
        /// ⚠ THE GUARD TESTS THE RAW n, BEFORE THE BUMP. The Visitor Information page passes 0
        /// (transport.md §4.1), which passes `0 < months` from the first rollover on and then reads as 1;
        /// a raw 1 at months = 1 is refused. So n = 0 is not "this month" -- nothing can read the slot the
        /// writer is about to fill -- and "one month back" has two spellings that differ only in the
        /// first month.</summary>
        public static int SlotFor(int months, int n)
        {
            if (!(unchecked((uint)n) < unchecked((uint)months))) return -1;
            if (n == 0) n = 1;
            int idx = months % Slots - n;
            while (idx < 0) idx += Slots;
            return idx;
        }

        /// <summary>The five row accessors (0x80066FA8 / 0x80066FE4 / 0x80067020 / 0x8006705C / 0x80067098):
        /// the byte at <see cref="SlotFor"/>, or 0 when it is -1.</summary>
        public int Read(HistoryRow row, int months, int n)
        {
            int idx = SlotFor(months, n);
            if (idx < 0) return 0;
            return row switch
            {
                HistoryRow.People => _people[idx],
                HistoryRow.ArrivalRate => _arrivalRate[idx],
                HistoryRow.Happiness => _happiness[idx],
                HistoryRow.TimeInPark => _timeInPark[idx],
                HistoryRow.Overall => _overall[idx],
                _ => throw new ArgumentOutOfRangeException(nameof(row)),
            };
        }
    }
}
