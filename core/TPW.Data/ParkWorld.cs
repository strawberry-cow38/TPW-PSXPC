using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>One of the game's four worlds, as the game's world table records it.</summary>
    public sealed class ParkWorld
    {
        public int Index { get; init; }
        /// <summary>What it is, where that is known. Only the jungle is confirmed (tinyclaw found map #203 in the
        /// jungle park's RAM); the rest are left unnamed rather than guessed from their colours.</summary>
        public string Name { get; init; } = "";
        /// <summary>The maps a park in this world starts from, archive entries: record +0x00 points at a list
        /// of (map, scenery pack) pairs and +0x04 counts them.</summary>
        public int[] Maps { get; init; } = Array.Empty<int>();
        /// <summary>The entry listed beside every map of the world (#205 for both jungle maps): its
        /// <see cref="SceneryPack"/>, the models the maps' build lists place. The map loader loads it as the
        /// map's second argument (0x800544E0 → 0x800351BC).</summary>
        public int SceneryEntry { get; init; }
        /// <summary>The texture sheet the ground is cut from (record +0xA0): the one whose sprite table the park
        /// loader (0x800588D0) keeps at gp+0x12A8 for the terrain routine.</summary>
        public int GroundSheet { get; init; }
        /// <summary>Records +0xA4 and +0xA8: two more sheets, of which the park loader loads one, picked by an
        /// index it gets from 0x80053FF4.</summary>
        public int[] ExtraSheets { get; init; } = Array.Empty<int>();
        /// <summary>The park's music module (archive entry). Not in the record: 0x80058694 picks it by world index
        /// and hands 0x800B7A90 the bank header, bank body and module (module - 1, module - 2, module).</summary>
        public int Music { get; init; }
    }

    /// <summary>The world table.
    ///
    /// ⭐ THE GAME'S OWN, NOT A FIT. 0x8002ED50 builds four 0xB0-byte records at 0x8010558C, 0x801054DC,
    /// 0x8010542C and 0x8010537C, which the pointer table at 0x800DDDC4 lists in that order as worlds 0-3; the
    /// values below are the immediates it writes, and the map lists are the static pairs at 0x800DDE88,
    /// 0x800DDE98, 0x800DDEA8 and 0x800DDEB8.
    ///
    /// ⚠ A FIT WOULD HAVE BEEN WRONG. "The sheet whose sprites at every index the map uses are ground-sized"
    /// picks #82 for map #116 (all 28x28) where the game uses #168 (a mix of 28x28 and 27x27), and cannot
    /// choose between #82 and #258 for map #34. It happens to agree on the jungle, which is how it would have
    /// slipped through.</summary>
    public static class ParkWorlds
    {
        public static readonly IReadOnlyList<ParkWorld> All = new[]
        {
            new ParkWorld { Index = 0, Name = "jungle", Maps = new[] { 203, 204 }, SceneryEntry = 205, GroundSheet = 258, ExtraSheets = new[] { 169, 170 }, Music = 305 },
            new ParkWorld { Index = 1, Maps = new[] { 116, 117 }, SceneryEntry = 118, GroundSheet = 168, ExtraSheets = new[] { 91, 92 }, Music = 302 },
            new ParkWorld { Index = 2, Maps = new[] { 34, 35 }, SceneryEntry = 36, GroundSheet = 82, ExtraSheets = new[] { 17, 18 }, Music = 296 },
            new ParkWorld { Index = 3, Maps = new[] { 355, 356 }, SceneryEntry = 359, GroundSheet = 400, ExtraSheets = new[] { 332, 333 }, Music = 317 },
        };

        /// <summary>The front end's music: 0x800BCE44, beside the front-end state machine, is the only other place
        /// that starts a module by number. Modules #293, #308, #311 and #314 are started some other way.</summary>
        public const int FrontEndMusic = 299;

        /// <summary>The world a map belongs to, or null for an entry no world lists.</summary>
        public static ParkWorld ForMap(int mapEntry)
        {
            foreach (var w in All)
                if (Array.IndexOf(w.Maps, mapEntry) >= 0) return w;
            return null;
        }

        /// <summary>"world 0 (jungle)", or "world 2" where the name is not known.</summary>
        public static string Describe(ParkWorld w) =>
            w == null ? "no world" : w.Name.Length > 0 ? $"world {w.Index} ({w.Name})" : $"world {w.Index}";
    }
}
