namespace TPW.Sim
{
    /// <summary>The visitor lookup tables, read out of TPW.BIN rather than reconstructed.
    ///
    /// ⭐ THESE ARE THE FILE'S OWN NUMBERS. behaviour.md §2.2b names the tables by address and leaves
    /// their contents unread; they are dumped here from the executable at 0x800E3820 and 0x800E3A04,
    /// both as s32. The s32 reading is not a guess -- read as bytes the same memory is visible nonsense
    /// (a sparse scatter of 0..31), while as words it is a smooth monotonic surface running 0 to 100
    /// with a zero first row. A wrong element width does not accidentally produce that.</summary>
    public static class VisitorTables
    {
        /// <summary>The 11x11 need-versus-attribute surface at 0x800E3820, indexed
        /// <c>[need / 10][attribute / 10]</c>.
        ///
        /// ⭐ IT IS A PRODUCT, NOT A DIFFERENCE. Both a low need and a low attribute give zero, and only
        /// a high need against a high attribute scores: the whole first row and first two columns are 0,
        /// and [10][10] is 100. So a guest with no appetite scores a food stall at nothing rather than
        /// scoring it badly -- there is no "wrong for me" here, only "does nothing for me".</summary>
        public static readonly int[,] NeedMatch =
        {
            { 0, 0, 0, 0,  0,  0,  0,  0,  0,  0,   0 },
            { 0, 0, 1, 2,  5,  7, 11, 15, 20, 25,  31 },
            { 0, 0, 1, 4,  7, 11, 16, 21, 28, 36,  44 },
            { 0, 0, 2, 4,  8, 13, 19, 26, 35, 44,  54 },
            { 0, 0, 2, 5, 10, 15, 22, 30, 40, 51,  63 },
            { 0, 0, 2, 6, 11, 17, 25, 34, 45, 57,  70 },
            { 0, 0, 3, 6, 12, 19, 27, 37, 49, 62,  77 },
            { 0, 0, 3, 7, 13, 20, 30, 40, 53, 67,  83 },
            { 0, 0, 3, 8, 14, 22, 32, 43, 57, 72,  89 },
            { 0, 0, 3, 8, 15, 23, 34, 46, 60, 76,  94 },
            { 0, 1, 4, 9, 16, 25, 36, 49, 64, 81, 100 },
        };

        /// <summary>The 22 stored words of the desire curve at 0x800E3A04, indexed <c>(stat + 5) / 5</c>.
        ///
        /// ⭐ LOW DESIRE IS A PENALTY, NOT INDIFFERENCE. The first eight entries are -20, so a guest
        /// whose ride-desire is under 35 scores every ride NEGATIVELY and is actively pushed toward
        /// shops and the exit rather than merely being unmoved. It crosses zero at exactly 35, then
        /// climbs steeply -- 4 at 50, 26 at 70, and saturated at 100 from 90 up. Index 0 is unreachable
        /// because the +5 means a stat of 0 already indexes 1.</summary>
        public static readonly int[] DesireCurve =
        {
            -20, -20, -20, -20, -20, -20, -20, -20,
            0, 1, 2, 4, 7, 11, 17, 26, 37, 53, 73, 100, 100, 100,
        };

        /// <summary>Per visitor-type data at 0x800F79E8, 8 entries of 8 bytes. The first u16 is the
        /// ride preference the score matches against (behaviour.md §2.2b).
        ///
        /// ⚠ THE SECOND u16 IS NOT IDENTIFIED. It reads 15, 20, 25, 18, 16, 14, 20, 10 and nothing in
        /// the findings says what consumes it, so it is exposed as <see cref="SecondField"/> rather than
        /// given a name. The remaining four bytes of each row are zero in every entry.</summary>
        public static readonly int[] TypePreference = { 90, 30, 50, 75, 100, 45, 60, 70 };

        /// <summary>The unidentified second u16 of each visitor-type row. See <see cref="TypePreference"/>.</summary>
        public static readonly int[] SecondField = { 15, 20, 25, 18, 16, 14, 20, 10 };

        /// <summary>Happiness threshold above which a type-4 shop gains its happiness bonus, for product
        /// kinds 2 and 3 (0x80103258 = 70) and for kind 6 (0x8010325C = 75).</summary>
        public const int ShopBonusThresholdKind23 = 70;
        /// <summary>See <see cref="ShopBonusThresholdKind23"/>.</summary>
        public const int ShopBonusThresholdKind6 = 75;

        /// <summary>Look up the desire curve for a 0..100 stat, indexing as the game does.</summary>
        public static int Desire(int stat)
        {
            int i = (Stat.Clamp(stat) + 5) / 5;
            return DesireCurve[i < 0 ? 0 : i >= DesireCurve.Length ? DesireCurve.Length - 1 : i];
        }

        /// <summary>Look up the need surface. Both axes are divided by 10 as the game divides them.
        ///
        /// ⚠ The attribute axis is clamped defensively: the visitor stats are known to be 0..100, but
        /// the ride slots feeding the other axis have no established range, and an out-of-range slot
        /// would index off the end of a real 11x11 table in the original too.</summary>
        public static int Match(int need, int attribute)
        {
            int a = Stat.Clamp(need) / 10;
            int b = attribute / 10;
            if (b < 0) b = 0; else if (b > 10) b = 10;
            return NeedMatch[a, b];
        }
    }
}
