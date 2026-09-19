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

        /// <summary>Cost of training a member to <paramref name="newLevel"/>. ⚠ This is what an earlier report
        /// mistook for the cost of HIRING. Hiring is free — the game spends £0 — so a port that charges for it
        /// drains the player's balance for something the original never billed.</summary>
        public static Money TrainingCost(int newLevel, StaffKind kind)
        {
            if (newLevel < 0) newLevel = 0;
            if (newLevel > MaxLevel) newLevel = MaxLevel;
            return Money.FromPounds((long)BaseByLevel[newLevel] * TrainingMultiplierByKind[(int)kind]);
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
