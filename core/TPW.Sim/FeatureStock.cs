using System;
using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>A type-2 Feature's "stock": the capacity byte the visitor's unloading arm draws down
    /// (VisitorQueue.UseFeature) and the handyman fills back up. On the object it is a SIGNED byte at
    /// outer+0x80 (A+0x78) beside a day-stamp word at outer+0x78 (A+0x70), kept by four small accessors:
    /// get 0x800241BC, subtract 0x800241E8, refill 0x80024210 and the stamp's getter 0x800241DC
    /// (findings/shop-stock.md).
    ///
    /// ⭐ THIS IS THE ONLY STOCK IN THE GAME, AND IT BELONGS TO TOILETS AND BINS, NOT TO SHOPS. A type-4
    /// Shop's object is 0x94 bytes and every one of them past the base is a price, a slider, a counter or
    /// a day: there is no stock byte, nothing in its sell routine (0x800B69E0) or its status hooks reads
    /// one, and no TrySpend anywhere restocks anything (economy.md §4.7). The game's own word for this
    /// byte is "Cleanliness" (text 0x7E, the bar on the Toilet Information card); "Stock" in its UI
    /// (text 0x363) is the purchase screen's inventory of things to build. So a shop never runs out, and
    /// a port that gives it a stock level to run out of has invented a mechanic.
    ///
    /// ⚠ OFFSETS: the accessors take the OUTER pool pointer (every caller subtracts 8 first, e.g.
    /// 0x8008F254, 0x8007BF78) so their `+0x80` is A+0x78 in rides.md's A-relative convention. rides.md
    /// §2 writes "feature A+0x80"; that is outer-relative. Same byte, either way.
    ///
    /// ⚠ NOTHING DECAYS IT. By exhaustion of the callers: only the visitor subtracts (0x8008F2C4) and
    /// only placement (0x80023C80), the save-loader (0x80023E30) and the handyman's refill (0x8002422C)
    /// set it. A toilet nobody uses stays clean forever; a port that lets cleanliness drift with time is
    /// inventing that too.</summary>
    public sealed class FeatureStock
    {
        /// <summary>What placement (0x80023C78) and a refill (0x80024220) set the byte to: 100.</summary>
        public const int Full = 100;

        /// <summary>The signed byte itself. `lb` everywhere it is read (0x800242DC, 0x800242B0, 0x80024304).</summary>
        sbyte _level;

        /// <summary>0x800241BC → 0x800242DC: the byte, sign-extended. 0..100 after any setter; see
        /// <see cref="Subtract"/> for how it can leave that range.</summary>
        public int Level => _level;

        /// <summary>outer+0x78: the total-day count (McAi+0x10, 0x80066E78) of the last refill, shown as
        /// "Last Cleaned" (text 0x400) on the Toilet Information card (0x8007C0AC). ZERO at placement
        /// (0x80023C94), not the build day -- a shop stamps its build day into the same slot
        /// (0x800B6980), a feature does not.</summary>
        public int LastServicedDay { get; private set; }

        /// <summary>Placement, 0x80023C70..0x80023C94: full, stamp zero.</summary>
        public FeatureStock()
        {
            Set(Full);
            LastServicedDay = 0;
        }

        /// <summary>The save-loader's version (0x80023DB8): the saved byte goes through the clamping
        /// setter (0x80023E30), the stamp word is copied raw (0x80023E48).</summary>
        public static FeatureStock FromSave(int capacityByte, int stampWord)
        {
            var s = new FeatureStock();
            s.Set(capacityByte);
            s.LastServicedDay = stampWord;
            return s;
        }

        /// <summary>The setter 0x800242E8: store the argument's low byte, then clamp it AS A SIGNED
        /// BYTE -- negative to 0 (0x800242F4..0x80024300 tests the sign bit), then anything over 100 to
        /// 100 (0x80024304..0x80024318, `slti 0x65`).
        ///
        /// ⚠ DO NOT FIX: 128..255 land on ZERO, not 100, because the byte is read back signed before the
        /// upper clamp runs. An int clamp would put Set(200) at 100; the console puts it at 0.</summary>
        public void Set(int value)
        {
            _level = unchecked((sbyte)value);
            if (_level < 0) _level = 0;
            if (_level > Full) _level = (sbyte)Full;
        }

        /// <summary>0x800241E8 → 0x800242B0: `level − units`, floored at zero. The visitor passes
        /// `(need − 60) × 2 / 3` (0x8008F2A4..0x8008F2C8), so 0..26 in practice.
        ///
        /// ⚠ DO NOT FIX: the units are parked in a stack BYTE (`sb a1, 16(sp)` at 0x800241F0) and read
        /// back with `lb`, so 128..255 subtract a NEGATIVE number, and the difference is stored with `sb`
        /// with no upper clamp -- Subtract(255) on a full feature leaves 101, Subtract(128) leaves the
        /// byte 0xE4, which reads as −28. Unreachable from the one caller, reproduced because the
        /// alternative is a tidier rule than the console has.</summary>
        public void Subtract(int units)
        {
            int d = _level - unchecked((sbyte)units);
            _level = d >= 0 ? unchecked((sbyte)d) : (sbyte)0;
        }

        /// <summary>The handyman's refill, 0x80024210, at the end of state 51 (0x80099318): back to
        /// <see cref="Full"/> through the setter, and the stamp := today (McAi+0x10).
        ///
        /// ⭐ INSTANT, FREE, AND THE ONLY WAY BACK UP. The refill has no rate: the emptying TIME is the
        /// handyman's (Handyman.EmptyTicks, 180..15 by skill) and the byte jumps to 100 when it ends. It
        /// calls nothing in the bank. And no other code path raises the byte, so a park with no cleaner
        /// has toilets that only ever get dirtier.</summary>
        /// <param name="today">The park's total-day count.</param>
        public void Refill(int today)
        {
            Set(Full);
            LastServicedDay = today;
        }

        /// <summary>Park statistic 30 (0x800153B4, case 0x1E of the 72-way switch at 0x80016AB4): over
        /// every feature whose slot 33 is set, `100 − mean(level)`, or 0 when there is none.
        ///
        /// ⚠ ZERO, NOT 100, FOR A PARK WITH NO TOILETS: the register is preloaded with 100 and the
        /// no-features branch overwrites it (0x80015440..0x8001544C). A park that has nothing to be
        /// dirty reads as spotless.
        ///
        /// ⚠ THE RUNNING SUM IS 16-BIT (`sll 16; sra 16` after every add, 0x80015424): with the pool
        /// capped at 45 features (4,500 at most) it cannot overflow, and it is reproduced so that the
        /// port reads the same register the console does rather than a wider one. The result is masked
        /// to 16 bits (`andi 0xFFFF`) on its way into an s16 statistic slot.</summary>
        /// <param name="features">(slot 33, level) for every placed feature; the ones with slot 33
        /// clear are skipped from both the sum and the count.</param>
        public static int ParkDirtiness(IEnumerable<(bool Usable, int Level)> features)
        {
            if (features == null) throw new ArgumentNullException(nameof(features));
            int count = 0;
            short sum = 0;
            foreach (var (usable, level) in features)
            {
                if (!usable) continue;
                count++;
                sum = unchecked((short)(sum + level));
            }
            if (count == 0) return 0;
            return (Full - sum / count) & 0xFFFF;
        }

        /// <summary>The "Overall Cleanliness" bar (text 0xFB) on the all-toilets panel: 0x80078010 sums
        /// the level of every entry in the panel's list, then `(sum << 16) / count >> 16`, and 0 for an
        /// empty list or a zero sum. Which features the panel lists is not traced; the arithmetic is.</summary>
        public static int PanelAverage(IReadOnlyList<int> levels)
        {
            if (levels == null) throw new ArgumentNullException(nameof(levels));
            int sum = 0;
            for (int i = 0; i < levels.Count; i++) sum += levels[i];
            int scaled = unchecked(sum << 16);
            if (scaled == 0) return 0;
            return (scaled / levels.Count) >> 16;
        }
    }
}
