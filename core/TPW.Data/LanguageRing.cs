using System;

namespace TPW.Data
{
    /// <summary>The language selector on the boot screen, and — the part that matters — the fact that
    /// its position is NOT the game's language number.
    ///
    /// ⚠⚠ TWO DIFFERENT ORDERINGS, AND ONE IS A PLAUSIBLE SMALL INTEGER FOR THE OTHER. The ring the
    /// player turns runs German, Spanish, Dutch, English, Swedish, French, Italian. The executable's
    /// language index, the one <see cref="StringTable.EntryByLanguage"/> is indexed by, runs English,
    /// French, German, Italian, Spanish, Dutch, Swedish, Japanese. Feeding the ring position straight
    /// into the string tables loads ENGLISH for a player who picked German and ITALIAN for one who
    /// picked English — wrong, silent, and it looks like it works for whoever tests it in English,
    /// because English is the ring's resting position and index 3 lands on Italian only once someone
    /// moves it. Always go through <see cref="ToLanguage"/>.
    ///
    /// ⚠ THE RING IS 7 LONG AND THE LANGUAGE LIST IS 8. Japanese (index 7) has string tables on the
    /// PAL disc but no position on this ring, so it is unreachable from the boot screen. A port that
    /// assumes the two lists are the same length is wrong in both directions.
    ///
    /// HOW THIS IS KNOWN, and the two halves have different strength:
    ///   - the ORDER is read off the screen: writing the index word (RAM 0x801EF3D0, and a copy at
    ///     0x801EF3D4) and rendering the ring shows each label in turn. 3 at rest, 2 after LEFT, 4
    ///     after RIGHT, so LEFT decrements.
    ///   - position 5 = FRENCH is confirmed END TO END, which is the stronger evidence and the reason
    ///     to trust the rest: RIGHT twice from rest then CROSS brought up a main menu reading
    ///     "Jouer / Options / Charger partie". That is the whole chain — ring position, to language
    ///     number, to string table — verified by its output rather than by an address.
    ///
    /// ⚠ The index word is not the only state: a write held every frame changes nothing, and a one-shot
    /// write re-labels the ring while leaving the flag the Union Jack. So do not treat this as "the
    /// language variable"; it is the ring's position, and something else derives the flag from the
    /// input. See findings/boot.md §C.</summary>
    public static class LanguageRing
    {
        /// <summary>Ring position → the game's language index. English sits at 3, which is where the
        /// ring rests.</summary>
        public static readonly int[] ToLanguageIndex = { 2, 4, 5, 0, 6, 1, 3 };

        /// <summary>Where the ring sits before anyone touches it: English.</summary>
        public const int RestPosition = 3;
        public static int Count => ToLanguageIndex.Length;

        /// <summary>The game's language index for a ring position.</summary>
        public static int ToLanguage(int ringPosition)
        {
            if (ringPosition < 0 || ringPosition >= ToLanguageIndex.Length)
                throw new ArgumentOutOfRangeException(nameof(ringPosition));
            return ToLanguageIndex[ringPosition];
        }

        /// <summary>The name of the language at a ring position, in English.</summary>
        public static string NameAt(int ringPosition) => StringTable.LanguageNames[ToLanguage(ringPosition)];

        /// <summary>Turn the ring. It WRAPS — measured by turning past both ends.</summary>
        public static int Turn(int ringPosition, int delta)
        {
            int n = ToLanguageIndex.Length;
            return ((ringPosition + delta) % n + n) % n;
        }

        /// <summary>Whether a game language can be chosen on this screen at all. Japanese cannot.</summary>
        public static bool IsReachable(int languageIndex) => Array.IndexOf(ToLanguageIndex, languageIndex) >= 0;
    }
}
