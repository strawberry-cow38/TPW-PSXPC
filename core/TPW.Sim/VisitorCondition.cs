using System;
using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>The one-number reading of a guest's stats that 0x80091C28 returns (findings/needs.md §6).
    ///
    /// ⚠ THE NUMBERS ARE THE INTERFACE. The Park Statistics window indexes its icon table by this value
    /// (0x800E3100 + 4 × code) and park statistics 1 and 2 count guests whose code EQUALS 1 and 2, so
    /// the values are the original's, not a tidy enumeration. Named by the byte they test, not by what
    /// the picture is taken to mean: the burger sprite for code 2 is READ, "hungry" is a reading of it.</summary>
    public enum GuestCondition
    {
        None = 0,
        /// <summary>V+0x5E &gt; 75. Icon 0x32, a drink cup.</summary>
        NeedBHigh = 1,
        /// <summary>V+0x5B &gt; 80. Icon 0x34, a burger.</summary>
        NeedAHigh = 2,
        /// <summary>V+0x5D &gt; 75. Icon 0x3B, the toilet pictogram.</summary>
        RideDesireHigh = 3,
        /// <summary>Nausea &gt; 75. Icon 0x3D, the nauseous bubble.</summary>
        NauseaHigh = 4,
        /// <summary>Happiness &gt; 75. Icon 0x3E, the happy bubble.</summary>
        HappinessHigh = 5,
        /// <summary>Tiredness &gt; 75. Icon 0x40, the tired bubble.</summary>
        TirednessHigh = 6,
        /// <summary>V+0x5B &gt; 80 AND V+0x5E &gt; 80. Icon 0x33, cup and burger together.</summary>
        BothNeedsHigh = 7,
        /// <summary>Happiness &lt; 25. Icon 0x35, the miserable bubble.</summary>
        HappinessLow = 8,
    }

    /// <summary>0x80091C28 and its two consumers: park statistics 1 and 2 (0x80016AB4 cases 1 and 2)
    /// and the Park Statistics window's three-icon summary (0x800814AC). All READ, findings/needs.md §6.
    ///
    /// ⭐ THIS IS HOW THE GAME NAMES THE BYTES. Nothing in the visitor code says which of V+0x5B..V+0x5E
    /// is which; this function and its icon table are the one place the original ties each byte to a
    /// picture the player sees. The order and thresholds below are the original's and are not the
    /// bubble pass's (VisitorNeeds.ChooseBubble tests different fields at different levels).</summary>
    public static class VisitorCondition
    {
        /// <summary>Need A alone, and each need inside the pair (0x80091C38: `addiu s2, zero, 0x50`).</summary>
        public const int NeedAAbove = 80;
        /// <summary>⚠ NEED B ALONE IS 75, NOT 80 (0x80091C98: `addiu s1, zero, 0x4b`). A guest at need B
        /// 78 reports thirsty on its own, but with need A over 80 it reports hungry, not both.</summary>
        public const int NeedBAloneAbove = 75;
        /// <summary>V+0x5D, nausea, happiness and tiredness all use the same 75 (s1 stays 0x4B).</summary>
        public const int OthersAbove = 75;
        /// <summary>Happiness below this reports miserable (0x80091D14: `addiu v0, zero, 0x19`).</summary>
        public const int MiserableBelow = 25;

        /// <summary>The window's icon word for each code, 0x800E3100 + 4 × code (READ). They are the bubble
        /// sprite ids the needs pass uses (P+0x2A), not text ids. Index 0 is a real zero.</summary>
        public static readonly int[] IconByCode = { 0x00, 0x32, 0x34, 0x3B, 0x3D, 0x3E, 0x40, 0x33, 0x35 };

        /// <summary>The window shows this many conditions (0x800818E8: `slti v0, t1, 3`).</summary>
        public const int WindowSlots = 3;

        /// <summary>0x80091C28(guest): first match wins, every comparison strict.
        ///
        /// ⚠ DO NOT REORDER. Nausea is tested before happiness, so a delighted but queasy guest reads as
        /// queasy; tiredness before misery, so a worn-out miserable guest reads as tired. Sorting these
        /// by field or by threshold changes the park's statistics.</summary>
        public static GuestCondition Of(Visitor guest)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));

            if (guest.NeedA > NeedAAbove && guest.NeedB > NeedAAbove) return GuestCondition.BothNeedsHigh;
            if (guest.NeedA > NeedAAbove) return GuestCondition.NeedAHigh;
            if (guest.NeedB > NeedBAloneAbove) return GuestCondition.NeedBHigh;
            if (guest.RideDesire > OthersAbove) return GuestCondition.RideDesireHigh;
            if (guest.Nausea > OthersAbove) return GuestCondition.NauseaHigh;
            if (guest.Happiness > OthersAbove) return GuestCondition.HappinessHigh;
            if (guest.Tiredness > OthersAbove) return GuestCondition.TirednessHigh;
            if (guest.Happiness < MiserableBelow) return GuestCondition.HappinessLow;
            return GuestCondition.None;
        }

        /// <summary>The icon the Park Statistics window draws for a code (0x800818CC..0x800818D4).</summary>
        public static int Icon(GuestCondition code) => IconByCode[(int)code];

        /// <summary>Park statistics 1 and 2 (0x80016B1C, 0x80016B74): the percentage of guests whose code is
        /// EXACTLY <paramref name="code"/>, `count × 100 / guests` truncated, 0 when there are no guests
        /// (0x80016B28 stores the zero without dividing).
        ///
        /// ⚠ EXACT MATCH. A guest at code 7 (both needs high) is counted in NEITHER statistic 1 nor 2:
        /// the hungriest guests in the park are the ones the "hungry" statistic does not see.</summary>
        public static int PercentWith(IEnumerable<Visitor> guests, GuestCondition code)
        {
            if (guests == null) throw new ArgumentNullException(nameof(guests));
            int total = 0, matching = 0;
            foreach (var g in guests)
            {
                total++;
                if (Of(g) == code) matching++;
            }
            if (total == 0) return 0;
            return matching * 100 / total;
        }

        /// <summary>The Park Statistics window's summary (0x80081700..0x800818F0): count every guest's code,
        /// then take the three most common of codes 1..8. Codes with no guests are not shown, so the list
        /// is shorter when fewer than three conditions occur. Returned most common first.
        ///
        /// ⚠ TIES GO TO THE LOWER CODE: the scan is `slt` (strict) from code 1 upward, so at equal counts
        /// need B beats need A beats the toilet. Not the order of the enum's meaning, the order of its numbers.</summary>
        public static IReadOnlyList<GuestCondition> TopThree(IEnumerable<Visitor> guests)
        {
            if (guests == null) throw new ArgumentNullException(nameof(guests));
            var counts = new int[IconByCode.Length];
            foreach (var g in guests) counts[(int)Of(g)]++;

            var result = new List<GuestCondition>(WindowSlots);
            for (int slot = 0; slot < WindowSlots; slot++)
            {
                int best = 0, bestCode = 0;
                for (int code = 1; code < counts.Length; code++)
                    if (best < counts[code]) { best = counts[code]; bestCode = code; }
                if (best == 0) continue;
                counts[bestCode] = 0;
                result.Add((GuestCondition)bestCode);
            }
            return result;
        }
    }
}
