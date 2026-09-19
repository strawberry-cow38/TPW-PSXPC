using System;

namespace TPW.Data
{
    /// <summary>One island on the world map: a park you can open, enter and close.</summary>
    public readonly struct MapIsland
    {
        /// <summary>Which of the four worlds this park belongs to -- the SAME index as
        /// <see cref="ParkWorlds.All"/>, read from the game's own record rather than matched by theme.</summary>
        public readonly int World;
        /// <summary>Which of that world's two parks this is, 0 or 1.</summary>
        public readonly int Park;
        /// <summary>String id of the park's display name, e.g. 945 = "Lost Kingdom: Prehistoric World".</summary>
        public readonly int NameId;

        public MapIsland(int world, int park, int nameId) { World = world; Park = park; NameId = nameId; }
    }

    /// <summary>The world map -- the screen the Main Game opens onto, where the parks are islands joined by
    /// rope bridges and you spend Gold Tickets to open one.
    ///
    /// ⭐ THE GAME'S OWN TABLE, at 0x801141F4 in the title overlay: eight 28-byte records, read out of a RAM
    /// dump taken with the world map on screen. Each record carries the world index at +6, the park-within-world
    /// at +7 and the name's string id at +10. The remaining fields are NOT decoded and are deliberately absent
    /// from this type rather than guessed at -- two of them look like map coordinates and three look like
    /// bitmasks, which is exactly the sort of resemblance that turns into a wrong constant if written down.
    ///
    /// ⭐ This table INDEPENDENTLY CONFIRMS the world ordering in <see cref="ParkWorlds"/>. That order came
    /// from the world-record pointer table at 0x800DDDC4; this one is a different table, written by different
    /// code, for a different screen, and it assigns the same indices -- Lost Kingdom 0, Halloween 1, Wonder
    /// Land 2, Space Zone 3. Worth having because the port's world 0 is called "jungle" while the map calls
    /// that park "Lost Kingdom", and a reader matching the two by NAME would find no match at all.
    ///
    /// ⚠ REACHING THIS SCREEN AT ALL NEEDED THE COPY PROTECTION DEFEATED. The PAL disc carries a LibCrypt
    /// check; an image without the protection's subchannel data fails it and the game greys out Main Game, so
    /// this screen was believed unreachable and overlay 11 never appeared in RAM. It appears the moment the
    /// flag is cleared. See <see cref="MainMenuIsNotGatedHere"/>.</summary>
    public static class WorldMap
    {
        /// <summary>Overlay that holds the world map. It is absent from RAM until Main Game is entered, which
        /// is why a sweep of a menu-time dump concludes -- wrongly -- that the screen does not exist.</summary>
        public const int Overlay = 11;

        /// <summary>Address of the island table in the title overlay.</summary>
        public const uint TableAddress = 0x801141F4;

        /// <summary>Bytes per record.</summary>
        public const int RecordStride = 0x1C;

        /// <summary>Field offsets within a record, as measured.</summary>
        public const int WorldOffset = 6, ParkOffset = 7, NameIdOffset = 10;

        /// <summary>The eight parks, in the table's own order.</summary>
        public static readonly MapIsland[] Islands =
        {
            new MapIsland(0, 0, 945),   // Lost Kingdom: Prehistoric World
            new MapIsland(0, 1, 946),   // Lost Kingdom: The Park That Time Forgot
            new MapIsland(1, 0, 373),   // Halloween World: Realm of Terror
            new MapIsland(1, 1, 375),   // Halloween: Ghost World
            new MapIsland(2, 0, 752),   // Wonder Land: Land of Dreams
            new MapIsland(2, 1, 753),   // Wonder Land: Enchanted Island
            new MapIsland(3, 0, 406),   // Space Zone: The Final Frontier
            new MapIsland(3, 1, 407),   // Space Zone: Star Park
        };

        /// <summary>How many parks may be open at once.
        ///
        /// ⭐ The game says so in its own words, string 318: "You already have the maximum of three parks
        /// open". Taken from the text rather than from a constant in the code, so it is the DESIGNED rule and
        /// not a buffer size that happens to be three -- but for the same reason, the code that enforces it
        /// has not been read, and a port should confirm the limit before relying on it for anything but a
        /// message.</summary>
        public const int MaxOpenParks = 3;

        /// <summary>Marker for the note above: nothing on the main menu is disabled in this port, because the
        /// only thing that disabled it on the console was the disc check. Kept as a named constant so the
        /// cross-reference in the class comment resolves.</summary>
        public const bool MainMenuIsNotGatedHere = true;

        /// <summary>The island record for a given world and park, or null if there isn't one.</summary>
        public static MapIsland? Find(int world, int park)
        {
            foreach (var i in Islands)
                if (i.World == world && i.Park == park) return i;
            return null;
        }

        /// <summary>The park's display name from a language's string table.</summary>
        public static string NameOf(MapIsland island, StringTable table) => table?[island.NameId];
    }
}
