using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>The purchase catalogue's categories -- the tabs the Build menu shows (the park menu's triangle,
    /// findings/measured.md).
    ///
    /// ⭐ FROM THE GAME'S OWN TABLES, not from what a port would guess. 0x8007D290 walks two parallel tables in
    /// step: the attraction types at 0x800E2D4C (u32) and the labels at 0x800E2D30 (s16 string ids, a zero ends
    /// them). In the game's order: Rides (type 3), Track Rides (6), Tour Rides (7), Roller Coasters (1), Shops
    /// (4), Sideshows (5), Features (2). The staff catalogue (the square button) has its own pair right after,
    /// 0x800E2D40 / 0x800E2D68: Guards, Mechanics, Cleaners, Researchers, Entertainers.
    ///
    /// ⭐ A CATEGORY WITH NOTHING IN IT IS NOT SHOWN. Each one has to pass two tests before it becomes a tab: a
    /// per-type availability query (0x80052EA0, a jump table at 0x800E0FEC, asked with 7) and, plainly,
    /// 0x8006A158(manager, type) != 0 -- how many of that type the park's theme offers. So the tabs a park shows
    /// depend on its theme and scenario, and an empty tab never appears.</summary>
    public sealed class BuildCatalogue
    {
        const uint TypeTable = 0x800E2D4C, LabelTable = 0x800E2D30;
        const int MaxCategories = 16;

        public readonly struct Category
        {
            public readonly int Type, LabelId;
            public Category(int type, int labelId) { Type = type; LabelId = labelId; }
        }

        readonly List<Category> _categories = new();
        public IReadOnlyList<Category> Categories => _categories;

        /// <summary>The categories in the game's order, or null when the executable is missing or too short.</summary>
        public static BuildCatalogue Read(byte[] exe, uint baseAddress)
        {
            int t = (int)(TypeTable - baseAddress), l = (int)(LabelTable - baseAddress);
            if (exe == null || t < 0 || l < 0 || t + MaxCategories * 4 > exe.Length || l + MaxCategories * 2 > exe.Length)
                return null;
            var c = new BuildCatalogue();
            for (int i = 0; i < MaxCategories; i++)
            {
                short label = BitConverter.ToInt16(exe, l + i * 2);
                int type = BitConverter.ToInt32(exe, t + i * 4);
                if (label == 0) break;                     // the zero that ends the build list
                c._categories.Add(new Category(type, label));
            }
            return c;
        }
    }
}
