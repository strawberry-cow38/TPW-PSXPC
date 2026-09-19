using System;
using System.Collections.Generic;
using System.Text;

namespace TPW.Data
{
    /// <summary>The game's text, one table per language: 1,031 strings each.
    ///
    /// Format: u32 count, count × u32 offset (from the entry's start), then NUL-terminated strings.
    ///
    /// ⭐ WHICH TABLE IS WHICH LANGUAGE COMES FROM THE GAME'S OWN MAP, a u16 array in TPW.BIN at 0x800E1BEC:
    /// {407, 408, 409, 411, 414, 406, 415, 412}, indexed by language. It was found by searching for an array
    /// whose entries 0, 1, 4 and 7 are the English, French, Spanish and Japanese tables, the four languages
    /// master had already identified BY EAR from the advisor's voice channels. Four fixed values matching by
    /// chance is out of the question, so the array also names the other four: 2 German, 3 Italian, 5 Dutch,
    /// 6 Swedish. The self-test re-reads it from the executable, so a disc where it differs says so.
    ///
    /// Entry 410 is not a language: it holds the string IDs themselves ("STR_LISTBOX_BUILD_TRACK"), which is
    /// what makes the other tables searchable by meaning.
    ///
    /// ⚠ ENCODINGS DIFFER. The European tables are DOS code page 850 ("Cr\x82er" is Créer). Japanese is
    /// Shift-JIS. Reading them all as Latin-1 garbles every accent and all of the Japanese.</summary>
    public sealed class StringTable
    {
        /// <summary>Language index → FOLIO entry, the game's own array (TPW.BIN 0x800E1BEC).</summary>
        public static readonly ushort[] EntryByLanguage = { 407, 408, 409, 411, 414, 406, 415, 412 };
        public static readonly string[] LanguageNames =
            { "English", "French", "German", "Italian", "Spanish", "Dutch", "Swedish", "Japanese" };
        public const uint EntryMapAddress = 0x800E1BEC;
        public const int IdEntry = 410;
        public const int Japanese = 7;

        public int Entry;
        public string[] Strings = Array.Empty<string>();

        public string this[int id] => id >= 0 && id < Strings.Length ? Strings[id] : null;

        static bool _registered;
        static Encoding EncodingFor(int entry)
        {
            if (!_registered) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); _registered = true; }
            if (entry == IdEntry) return Encoding.ASCII;
            return Encoding.GetEncoding(entry == EntryByLanguage[Japanese] ? 932 : 850);
        }

        public static bool TryParse(byte[] d, int entry, out StringTable table, out string error)
        {
            table = null; error = null;
            if (d == null || d.Length < 8) { error = "too short for a string table"; return false; }
            int n = BitConverter.ToInt32(d, 0);
            if (n <= 0 || n > 10000 || 4 + 4L * n > d.Length) { error = $"implausible string count {n}"; return false; }
            // The first string starts right after the offset table; anything else is not this format.
            if (BitConverter.ToInt32(d, 4) != 4 + 4 * n) { error = "the first offset does not follow the table"; return false; }
            var enc = EncodingFor(entry);
            var strings = new string[n];
            for (int i = 0; i < n; i++)
            {
                int p = BitConverter.ToInt32(d, 4 + 4 * i);
                if (p < 0 || p >= d.Length) { error = $"string {i} starts outside the entry"; return false; }
                int q = p;
                while (q < d.Length && d[q] != 0) q++;
                strings[i] = enc.GetString(d, p, q - p).TrimEnd();
            }
            table = new StringTable { Entry = entry, Strings = strings };
            return true;
        }

        public static bool TryRead(GazArchive gaz, int language, out StringTable table, out string error)
        {
            table = null;
            if (language < 0 || language >= EntryByLanguage.Length) { error = $"no language {language}"; return false; }
            int entry = EntryByLanguage[language];
            if (gaz == null || entry >= gaz.Entries.Count) { error = $"no archive entry {entry}"; return false; }
            return TryParse(gaz.Read(gaz.Entries[entry]), entry, out table, out error);
        }

        /// <summary>The language map as the executable holds it, for checking the constant against the disc.</summary>
        public static ushort[] ReadEntryMap(byte[] tpwBin, uint loadAddress)
        {
            int at = (int)(EntryMapAddress - loadAddress);
            if (tpwBin == null || at < 0 || at + 16 > tpwBin.Length) return null;
            var m = new ushort[8];
            for (int i = 0; i < 8; i++) m[i] = BitConverter.ToUInt16(tpwBin, at + 2 * i);
            return m;
        }
    }

    /// <summary>An attraction's definition record: the part of a 0x96 entry that says what the model IS.
    /// Layout READ by fable (rides.md §1.3): +0 u32 type (the AttractionType enum, 1..8), +4 u32 name text id,
    /// +8 u8 footprint width, +0x0A u8 footprint depth.</summary>
    public readonly struct AttractionRecord
    {
        public readonly int Type, TextId, Width, Depth;
        public AttractionRecord(int type, int textId, int width, int depth) { Type = type; TextId = textId; Width = width; Depth = depth; }

        public static readonly string[] TypeNames =
            { "void", "rollercoaster", "feature", "ride", "shop", "sideshow", "track ride", "tour ride", "track upgrade" };

        public string TypeName => Type >= 0 && Type < TypeNames.Length ? TypeNames[Type] : $"type {Type}";

        public static bool TryRead(byte[] entry, MeshContainer c, out AttractionRecord r)
        {
            r = default;
            if (entry == null || c == null || c.RecordOffset == 0 || c.RecordOffset + 0x20 > entry.Length) return false;
            int at = (int)c.RecordOffset;
            int type = BitConverter.ToInt32(entry, at), text = BitConverter.ToInt32(entry, at + 4);
            // A real record's type is one of the enum's values and its text id is a string in the table.
            if (type < 1 || type > 8 || text < 0 || text > 1030) return false;
            r = new AttractionRecord(type, text, entry[at + 8], entry[at + 0x0A]);
            return true;
        }
    }
}
