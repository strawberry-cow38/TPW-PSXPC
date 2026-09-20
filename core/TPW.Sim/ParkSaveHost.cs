using System;

namespace TPW.Sim;

/// <summary>Host integration for 0x80071D0C. No files or engine objects cross this boundary.
/// Capture supplies the established record representations (save.md), including encoded histories;
/// it can use VisitorSaveRanges.Capture, LitterPool.SaveCounts and ResearchSystem.SaveTopics.
/// Restoring always starts from a fresh map/pools. Assets, object IDs, catalogue definitions,
/// RNG initialization and geometry remain host responsibilities. For histories/totals, delegate
/// to ParkScore.CaptureBank/RestoreBank and ParkHistory.Capture/Restore (findings/rating.md).
/// Implementations must retain the existing findings' behaviours where save.md §0 records a dispute.</summary>
public interface IParkSaveHost
{
    ParkSave Capture();
    /// <summary>Load original terrain/build list, clear all pools/claims/transient state; initialize
    /// managers and the closed gate. Restricted mode auto-opens per parkopen.md §2.4.</summary>
    void BeginPark(ParkSaveLayout layout);
    void OpenPark();
    void SetEntryFee(Money fee);
    /// <summary>READ: both placement calls at 0x800728C0 and 0x800728D4 are intentional.</summary>
    void PlacePath(int x, int y);
    /// <summary>Construct from class/definition; restore fields, rebuild queue/track/geometry.
    /// Reset riders, motion and claims. Ride states 4..6 become 4; see RestoredStatus.</summary>
    void RestoreAttraction(AttractionSave saved);
    /// <summary>Spawn/reinitialize at the jittered exit-side entrance (0x800599A4(1)); return the
    /// fresh visitor. The core restores ranges next; do not charge entry a second time.</summary>
    Visitor CreateVisitor();
    IRandomSource Random { get; }
    void SetVisitorUnknown63(Visitor visitor, byte value);
    /// <summary>Block index: guard, researcher, mechanic, handyman, entertainer. Restore position,
    /// hire day, recruit variant, skill/strike and patrol. Reset other states via class initialization.</summary>
    void RestoreStaff(int block, StaffSave saved);
    /// <summary>Use LitterSave.Restore on the fresh pool with the restored map and the same RNG.</summary>
    void RestoreLitter(byte ordinary, byte vomit);
    void RestoreBank(BankSave saved);
    void RestoreCalendar(CalendarSave saved);
    /// <summary>Whole percent THEN completed count in each pair; type/index ordering in Layout.</summary>
    void RestoreCatalogue(byte[] percentAndCompleted);
    /// <summary>Invoke ResearchSystem.RestoreTopics only AFTER restoring catalogue progress.</summary>
    void RestoreResearchTopics(byte[] topics);
    void RestoreMessage(ParkSaveMessage saved);
    /// <summary>READ: 0x800505A8 -> 0x800AF614 per coaster, final geometry/track pass.</summary>
    void FinishPark();
}

public static class ParkSaving
{
    public static byte[] Save(IParkSaveHost host) => ParkSaveCodec.Write(host.Capture());

    /// <summary>Validate the entire buffer before changing the host. READ restoration ordering:
    /// 0x80071D40..DE0. ⚠ DO NOT FIX: opening happens BEFORE the saved calendar is restored.</summary>
    public static void Load(ReadOnlySpan<byte> bytes, ParkSaveLayout layout, IParkSaveHost host)
    {
        var park = ParkSaveCodec.Read(bytes, layout);
        host.BeginPark(layout);
        if (park.Open != 0) host.OpenPark();
        host.SetEntryFee(Money.FromPounds(park.EntryFeePounds));
        for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
            {
                int tile = y * layout.Width + x;
                if ((park.Paths[tile >> 3] & (1 << (tile & 7))) == 0) continue;
                host.PlacePath(x, y);
                host.PlacePath(x, y);
            }
        foreach (var group in park.Attractions) foreach (var saved in group) host.RestoreAttraction(saved);
        for (int i = 0; i < park.VisitorCount; i++)
        {
            var guest = host.CreateVisitor();
            byte unknown = park.Visitors.Restore(guest, host.Random);
            host.SetVisitorUnknown63(guest, unknown);
        }
        for (int i = 0; i < park.Staff.Length; i++) foreach (var saved in park.Staff[i]) host.RestoreStaff(i, saved);
        host.RestoreLitter(park.OrdinaryLitter, park.Vomit);
        host.RestoreBank(park.Bank);
        host.RestoreCalendar(park.Calendar);
        if (!layout.Restricted)
        {
            host.RestoreCatalogue(park.Catalogue);
            host.RestoreResearchTopics(park.ResearchTopics);
        }
        foreach (var message in park.Messages) host.RestoreMessage(message);
        host.FinishPark();
    }
}
