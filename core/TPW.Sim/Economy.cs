using System;

namespace TPW.Sim
{
    /// <summary>Money, in the game's own representation.
    ///
    /// ⚠⚠ THE GAME STORES TEN TIMES THE NUMBER IT SHOWS YOU. £50,000 is held as 500000. tinyclaw measured
    /// this off a live HUD: the screen read $48,040 while memory held 480800. A raw int from a save or a
    /// memory dump is NOT pounds, and treating it as pounds makes everything in the economy come out an order
    /// of magnitude wrong while still looking like plausible money.
    ///
    /// The type exists so the units cannot be mixed up by accident: you cannot add an int to a Money.</summary>
    public readonly struct Money : IEquatable<Money>, IComparable<Money>
    {
        /// <summary>The stored value — ten times the displayed pounds.</summary>
        public readonly long Raw;

        Money(long raw) { Raw = raw; }

        public static Money FromRaw(long raw) => new(raw);
        /// <summary>From whole pounds as the player sees them.</summary>
        public static Money FromPounds(long pounds) => new(pounds * 10);

        /// <summary>Displayed pounds. ⚠ Truncates: the tenth is not shown anywhere in the game either.</summary>
        public long Pounds => Raw / 10;

        public static readonly Money Zero = new(0);

        public static Money operator +(Money a, Money b) => new(a.Raw + b.Raw);
        public static Money operator -(Money a, Money b) => new(a.Raw - b.Raw);
        public static Money operator *(Money a, int n) => new(a.Raw * n);
        public static bool operator <(Money a, Money b) => a.Raw < b.Raw;
        public static bool operator >(Money a, Money b) => a.Raw > b.Raw;
        public static bool operator <=(Money a, Money b) => a.Raw <= b.Raw;
        public static bool operator >=(Money a, Money b) => a.Raw >= b.Raw;
        public static bool operator ==(Money a, Money b) => a.Raw == b.Raw;
        public static bool operator !=(Money a, Money b) => a.Raw != b.Raw;

        public bool Equals(Money o) => Raw == o.Raw;
        public override bool Equals(object o) => o is Money m && Equals(m);
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(Money o) => Raw.CompareTo(o.Raw);
        public override string ToString() => $"£{Pounds:n0}";
    }

    /// <summary>⚠ THE KIND ORDER IS NOT THE ORDER THE HIRE SCREEN LISTS THEM IN. An earlier report had it as
    /// Entertainer/Mechanic/Guard/Researcher/Handyman and that is wrong; fable established this one three
    /// independent ways (the placer's create switch, the "pool full" message switch, and each pool's own init
    /// passing its kind). Getting it wrong pays a mechanic a cleaner's wage and looks entirely reasonable.</summary>
    public enum StaffKind
    {
        Mechanic = 0,
        Entertainer = 1,
        Cleaner = 2,
        Guard = 3,
        Researcher = 4,
    }

    public static class Wages
    {
        /// <summary>Wage base by level. ⚠ LEVEL IS ZERO-BASED; the screen shows "Pay Grade 1" for level 0.
        /// That off-by-one made a measured £100 look like it contradicted a formula predicting 150.</summary>
        public static readonly int[] BaseByLevel = { 50, 55, 65, 80, 100 };

        /// <summary>Wage multiplier by staff kind. Distinct from the TRAINING multiplier, which differs for
        /// Researcher (6 rather than 3) — using the training table for wages overpays researchers double.</summary>
        public static readonly int[] MultiplierByKind = { 3, 1, 1, 2, 3 };

        /// <summary>Training-cost multiplier. Kept beside the wage one precisely because they are nearly the
        /// same table and are not interchangeable.</summary>
        public static readonly int[] TrainingMultiplierByKind = { 3, 1, 1, 2, 6 };

        /// <summary>Training price by level, the table at 0x800E1654 (economy.md §4.4, wages.md §1).
        ///
        /// ⚠⚠ NOT THE WAGE TABLE. It is exactly five times it, {250..500} against {50..100}, and
        /// TrainingCost used to read the wage table, so every training in the port cost a fifth of the
        /// original's. Nothing looked wrong: the numbers were small, round and plausible for a park sim. It
        /// was caught by re-reading fable's report while answering an unrelated question about the training
        /// CARD. Same shape as the multiplier trap above: two tables that look alike, one of them silently used
        /// for the other.</summary>
        public static readonly int[] TrainingBaseByLevel = { 250, 275, 325, 400, 500 };

        /// <summary>The word in memory after the five-entry wage table (0x800E162C[5]). The Training card
        /// reads it for a top-level member; see <see cref="TrainingCardWage"/>.</summary>
        public const int WordAfterWageTable = 3;

        public const int MaxLevel = 4;

        /// <summary>Monthly wage for one staff member.
        ///
        /// ⚠⚠ TWO FLOORS, AND THE ORDER MATTERS. The game computes
        ///     pct  = daysWorked &lt; monthLength ? floor(100 * daysWorked / monthLength) : 100
        ///     wage = floor(pct * base * mult / 100)
        /// Collapsing that into one division gives a different answer for most part-months. tinyclaw measured
        /// a grade-1 guard hired mid-month at **£16**: 5 days of a 31-day month is pct = floor(500/31) = 16,
        /// then floor(16 * 50 * 2 / 100) = 16. A single-step (days/monthLength) * base * mult gives 16.129 →
        /// 16 here but diverges elsewhere, and matching one case is not matching the rule.
        ///
        /// ⚠ A STRIKING MEMBER IS PAID NOTHING, which is a real branch and not an edge case to tidy away.</summary>
        public static Money Monthly(int level, StaffKind kind, int daysWorked, int monthLength, bool striking = false)
        {
            if (striking) return Money.Zero;
            if (monthLength <= 0) return Money.Zero;
            if (level < 0) level = 0;
            if (level > MaxLevel) level = MaxLevel;

            int pct = daysWorked < monthLength ? (int)(100L * daysWorked / monthLength) : 100;
            if (pct < 0) pct = 0;
            int pounds = (int)((long)pct * BaseByLevel[level] * MultiplierByKind[(int)kind] / 100);
            return Money.FromPounds(pounds);
        }

        /// <summary>What the bank is CHARGED to train a member up to <paramref name="newLevel"/>:
        /// TrainingBase[newLevel] × training multiplier.
        ///
        /// ⚠ This is what an earlier report mistook for the cost of HIRING. Hiring is free (the game spends £0),
        /// so a port that charges for it drains the balance for something the original never billed.
        ///
        /// ⚠⚠ ONE ROW DEARER THAN THE CARD SAID. The Training card prints the price at the CURRENT level
        /// (<see cref="TrainingCardCost"/>), and the purchase handler (0x80085728) sets level = L+1 first and
        /// THEN prices it, so the player pays the next row: a grade-1 guard's card says £500 and the bank
        /// loses £550. That is the original's bug, and a port that "fixes" it disagrees with every save.</summary>
        public static Money TrainingCost(int newLevel, StaffKind kind)
        {
            if (newLevel < 0) newLevel = 0;
            if (newLevel > MaxLevel) newLevel = MaxLevel;
            return Money.FromPounds((long)TrainingBaseByLevel[newLevel] * TrainingMultiplierByKind[(int)kind]);
        }

        /// <summary>The "Training Cost" the Training card PRINTS: TrainingBase[level] × training multiplier,
        /// the current row. Not what is charged; see <see cref="TrainingCost"/>. (SOURCED, 0x80094DC0 called
        /// at the current level from the card's draw at 0x80085908.)
        ///
        /// ⚠ THIS CARD HAS NOT BEEN SEEN ON SCREEN. tinyclaw opened an employee's Staff Information card and
        /// found three bars (skill, motivation, overall motivation) and an "All Staff" button: no wage and no
        /// training anywhere on it. So where this card lives in this build, or whether it is reachable, is
        /// open. The draw code exists; the screen that shows it has not been found. The PRICE TABLE is a
        /// separate matter, read off the purchase handler, and does not depend on finding the card.</summary>
        public static Money TrainingCardCost(int level, StaffKind kind)
        {
            if (level < 0) level = 0;
            if (level > MaxLevel) level = MaxLevel;
            return Money.FromPounds((long)TrainingBaseByLevel[level] * TrainingMultiplierByKind[(int)kind]);
        }

        /// <summary>The "Monthly Wage" the Training card prints: the NEXT level's wage,
        /// base[min(level+1, 5)] × wage multiplier.
        ///
        /// ⚠ AT THE TOP LEVEL IT READS PAST THE TABLE. The table has five entries (0..4) and the clamp is to
        /// 5, not 4, so a level-4 member's card indexes one past the end and prints whatever word follows,
        /// which is 3: "Monthly Wage £9" for a top-grade mechanic. SOURCED by fable (wages.md §2.3), not yet
        /// seen on screen, and see the warning on <see cref="TrainingCardCost"/>: nobody has found the card.
        /// Reproduced rather than clamped, because it is what the original's code draws.</summary>
        public static Money TrainingCardWage(int level, StaffKind kind)
        {
            if (level < 0) level = 0;
            int i = Math.Min(level + 1, 5);
            int b = i < BaseByLevel.Length ? BaseByLevel[i] : WordAfterWageTable;
            return Money.FromPounds((long)b * MultiplierByKind[(int)kind]);
        }

        /// <summary>What the hire screen prints. The stored level is zero-based; the display is not.</summary>
        public static int DisplayedPayGrade(int level) => level + 1;
    }

    public static class ParkEconomy
    {
        /// <summary>The bank's constructed default entry fee: Money(40).</summary>
        public static readonly Money DefaultEntryFee = Money.FromPounds(40);

        /// <summary>⚠ HIRING COSTS NOTHING. The game calls TrySpend with £0. See Wages.TrainingCost.</summary>
        public static readonly Money HireCost = Money.Zero;

        /// <summary>⚠ THE BANK MAY GO NEGATIVE: its constructor sets a flag that makes TrySpend never refuse.
        /// A port that blocks a purchase on insufficient funds is stricter than the original and will diverge
        /// from it the first time a player overspends — which the original simply allows.</summary>
        public const bool BalanceMayGoNegative = true;

        /// <summary>Months of history the bank keeps, indexed modulo this. Twelve years.</summary>
        public const int HistoryMonths = 144;
    }
}
