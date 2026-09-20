using System;

namespace TPW.Sim;

/// <summary>READ: 0x800715F0 / 0x80072B54; preserve the existing ResearchSystem and research.md §4.
/// Catalogue bytes are percent FIRST, completed count SECOND. Saving queries may unlock free tiers,
/// just as the original getters do; the count is sampled before querying the next-level percent.</summary>
public static class ResearchSave
{
    public static byte[] CaptureCatalogue(ParkSaveLayout layout, ResearchSystem research)
    {
        if (layout.Restricted) return Array.Empty<byte>();
        var result = new byte[layout.CatalogueBytes]; int at = 0;
        for (int group = 0; group < ParkSaveLayout.CatalogueTypes.Count; group++)
            for (int index = 0; index < layout.CatalogueCounts[group]; index++)
            {
                var definition = new ResearchDefinition(ParkSaveLayout.CatalogueTypes[group], index);
                byte levels = unchecked((byte)research.LevelCount(definition));
                result[at++] = unchecked((byte)research.ProgressPercent(definition, levels));
                result[at++] = levels;
            }
        return result;
    }

    public static void RestoreCatalogue(ParkSaveLayout layout, ReadOnlySpan<byte> bytes, ResearchSystem freshResearch)
    {
        int expected = layout.Restricted ? 0 : layout.CatalogueBytes;
        if (bytes.Length != expected) throw new ArgumentException("Incorrect saved catalogue size.");
        if (layout.Restricted) return;
        int at = 0;
        for (int group = 0; group < ParkSaveLayout.CatalogueTypes.Count; group++)
            for (int index = 0; index < layout.CatalogueCounts[group]; index++)
            {
                var definition = new ResearchDefinition(ParkSaveLayout.CatalogueTypes[group], index);
                int percent = bytes[at++], levels = bytes[at++];
                freshResearch.StoreProgress(definition, levels, percent);
            }
    }
}
