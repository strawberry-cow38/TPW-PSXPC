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

        /// <summary>Ring position -> the sheet-84 sprite holding that language's FLAG.
        ///
        /// ⚠ NOT IN RING ORDER AND NOT IN LANGUAGE ORDER -- a third ordering again (122, 124, 119, 120, 125, 121, 123). Read off
        /// the disc rather than assumed, and two of the seven are verified by consequence: on the
        /// console the flag is drawn from a SLOT sprite (#126) whose texels the game overwrites with
        /// the chosen flag, and at rest that slot's contents equal sprite #120 (English) while after
        /// one LEFT they equal #119 (Dutch). The other five are named by the flag picture itself.
        ///
        /// ⚠ THE CONSOLE DRAWS THIS ON A MESH, not as a flat quad -- the advisor holds a pole and the
        /// cloth waves. Every textured 2D primitive on that screen is font. So drawing this sprite
        /// flat gives the RIGHT ART in the WRONG FORM, which is worth having and worth saying.</summary>
        public static readonly int[] FlagSprite = { 122, 124, 119, 120, 125, 121, 123 };

        /// <summary>Ring position -> the name the console PRINTS for it, which is the language's own
        /// name, not the English one. ⭐ These are the game's own strings, reached through its own
        /// pointer array at <see cref="NameTableAddress"/> -- seven words in ring order, read both out
        /// of a live RAM dump on the language screen and straight out of TPW.BIN at the matching file
        /// offset, so this is static data and not something assembled at runtime.
        ///
        /// ⚠ NOT in the string tables, and that is not an oversight: the language screen runs BEFORE a
        /// language is chosen, so there is no table to read from yet. Searching all eight tables for
        /// them returns nothing, which is the check that sent me to the executable.
        ///
        /// The array is also the third independent witness to <see cref="ToLanguageIndex"/>: its order
        /// is Deutsch, Español, Nederlands, English, Svenska, Français, Italiano, matching both the ring
        /// order derived from input and the flag sprites' own keys.
        ///
        /// ⚠ Four of the seven live in TPW.BIN and three in the title overlay (0x8011476c onward) --
        /// the split is by string LENGTH, the short ones fitting an 8-byte slot. A reader who assumes
        /// one contiguous block finds four names and concludes the other three are generated.</summary>
        public static readonly string[] NativeName =
            { "Deutsch", "Español", "Nederlands", "English", "Svenska", "Français", "Italiano" };

        /// <summary>TPW.BIN address of the pointer array above. File offset is this minus 0x80010000.</summary>
        public const uint NameTableAddress = 0x801024F4;

        /// <summary>The name as the console prints it for a ring position.</summary>
        public static string NativeNameAt(int ringPosition)
        {
            int n = ToLanguageIndex.Length;
            return NativeName[((ringPosition % n) + n) % n];
        }

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
