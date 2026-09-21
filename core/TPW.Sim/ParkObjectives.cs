using System;
using System.Collections.Generic;
using System.Linq;

namespace TPW.Sim;

/// <summary>Live attraction snapshot. Include held/status-zero objects, all seven pools.
/// READ: type/status/upgrade at 0x80067808, 0x80067860, 0x800595B8.
/// Bounds are signed tile halfwords from slots 10/12/14; Width/Height are extents, not far edges.
/// IsLitterBin is the descriptor+0x2E bit 2 predicate (0x80023FB8 -> 0x80024330).
/// FeaturePricePounds retains economy.md's LEVEL-price contract, like IParkScoreWorld:
/// GUESS-low / SOURCE DISAGREEMENT: 0x800678D8 actually reads definition A+0x6B and
/// 0x800678E8 asks 0x8006AD58 for base price. See findings/goals.md §0.</summary>
public readonly record struct ObjectiveAttraction(StatisticAttraction Item,
    int FeaturePricePounds = 0, short X = 0, short Y = 0, short Width = 0, short Height = 0,
    bool IsLitterBin = false);

/// <summary>Live quantities for the nine hardcoded weekly awards. No cached advisor statistics.
/// No Overall Rating is read by this path; keep rating/history in ParkScore.
/// A future loader selects a ParkObjectiveDefinition, restores both award words, and supplies
/// these values. The definition does not contain the sandbox/research/entry-fee settings.</summary>
public interface IParkObjectiveWorld
{
    /// <summary>READ: 0x80059A9C, sandbox word 0x80102D34. Same flag as research.RestrictedMode.</summary>
    bool RestrictedMode { get; }
    /// <summary>READ: McAi+0x1C, 0x8006797C; total successful admissions, not guests now.</summary>
    uint Admissions { get; }
    bool ParkOpen { get; } // READ: 0x80067A18 -> 0x800541AC.
    /// <summary>READ: 0x80102D24, 0x8005BA80. Preserve the original opening stamp, including
    /// 0xFFFF before opening; save.md documents why this is not saved in the park stream.</summary>
    uint OpeningMonth { get; }
    /// <summary>READ: 0x8008748C, remaining principal in the four occupied loan slots.
    /// Adapt LoanBook.TotalOutstanding; money is in tenths, not pounds.</summary>
    Money OutstandingLoans { get; }
    IEnumerable<ObjectiveAttraction> Attractions { get; }
    /// <summary>READ: the fifth pool counted by 0x80059914 is PoolOfTourTransports,
    /// 0x80103880 / pool+0xC (created at 0x800502F8..304). It is NOT another ride list.
    /// ⚠ DO NOT FIX: transports help meet the 8/10-ride gates but are not upgrade-tested.</summary>
    uint TourTransportCount { get; }
    /// <summary>READ: 0x800598A4 counts tile-type 2 over width*height, including disconnected
    /// paths. Supply a live whole-map count, not path length or distance to a ride.</summary>
    uint PathTileCount { get; }
}

/// <summary>READ: McAi+0x20 per-park bits and +0x24 campaign-wide hidden-award bits.
/// Restore/capture complete words, including bits outside the weekly checks (0x80066B54/0x80066BFC).
/// These words belong in general campaign data, not CalendarSave's packed park record.</summary>
public readonly record struct ObjectiveState(uint ParkBits, uint BonusBits);

/// <summary>Host seam only: each event grants ONE Gold Ticket (0x800677C8 -> 0x8006BFE4),
/// incrementing spendable and lifetime totals 0x80103984/88 and refreshing the HUD. Then post
/// MessageId via advisor 0x80014144 AND the type-2 list route 0x800693C8, in returned order.
/// Neither ParkAdvisor nor ParkMessages is called here. Deliver each returned event once.
/// READ: no all-complete victory transition and no money award in 0x80067928..0x80067CD8.</summary>
public readonly record struct ObjectiveAward(bool Bonus, int Bit, ushort MessageId)
{
    public int GoldTickets => 1; // READ: 0x800677BC.
}

/// <summary>READ: startup descriptions from 0x800676DC, string IDs 0x161/0x34E/0x1EB.
/// Host formats and inserts these as type 4 (0x8006768C); they are not reward events.</summary>
public readonly record struct ObjectiveDescription(int Bit, ushort TextId, uint Threshold);

/// <summary>Owns immutable thresholds and sticky award bits. READ: 0x80067928.
/// An unmet condition does nothing; completed awards never revert when quantities decline.
/// Bankruptcy remains DebtWatch plus the host's message-0x8D completion/dismissal seam:
/// 0x8001347C/0x800136D8 -> 0x800BCEA0 -> events 0x10004, 0x80008. See findings/goals.md.
/// This component deliberately exposes no invented Won/Lost flag.</summary>
public sealed class ParkObjectives
{
    readonly ParkScore score;
    public ParkObjectiveDefinition Definition { get; }
    public ObjectiveState State { get; private set; }

    public ParkObjectives(ParkObjectiveDefinition definition, ParkScore score, ObjectiveState state = default)
    {
        Definition = definition;
        this.score = score ?? throw new ArgumentNullException(nameof(score));
        State = state;
    }

    /// <summary>Port lifecycle API. Loader supplies both complete words; no award is replayed.</summary>
    public void Restore(ObjectiveState state) => State = state;

    public IReadOnlyList<ObjectiveDescription> UnmetDescriptions(bool restrictedMode)
    {
        var result = new List<ObjectiveDescription>();
        if (Definition == null || restrictedMode) return result;
        if (!Has(false, 1)) result.Add(new(1, 0x161, Definition.AdmissionsThreshold));
        if (!Has(false, 2)) result.Add(new(2, 0x34E, unchecked((uint)Definition.ProfitPounds)));
        if (!Has(false, 3)) result.Add(new(3, 0x1EB, Definition.YearsOpen));
        return result;
    }

    /// <summary>Call once AFTER each Calendar.AdvanceDay, after monthly finance/history updates.
    /// READ: 0x80066E10..50 checks a changed day-of-month modulo 7, including day zero.
    /// Do not call every frame merely because Calendar.IsObjectiveDay remains true.</summary>
    public IReadOnlyList<ObjectiveAward> AfterDay(Calendar calendar, IParkObjectiveWorld world)
    {
        var result = new List<ObjectiveAward>();
        if (!calendar.IsObjectiveDay || Definition == null || world.RestrictedMode) return result;

        // READ: strict UNSIGNED admissions comparison, 0x8006797C..98.
        if (!Has(false, 1) && world.Admissions > Definition.AdmissionsThreshold)
            Award(false, 1, 0xAF, result);

        // READ: 0x800873D8 is all-time income - outstanding loans - all-time spending.
        // ⚠ DO NOT FIX: despite the goal's "12 months" caption this reads totals, not histories.
        // 0x80069444 compares signed native Money words strictly; 0x800694C0 converts pounds.
        if (!Has(false, 2) && unchecked((int)(score.Income.Raw - world.OutstandingLoans.Raw - score.Spending.Raw))
            > unchecked(Definition.ProfitPounds * 10))
            Award(false, 2, 0xB0, result);

        // READ: unsigned subtraction and unsigned /12, 0x80067A3C..70. Retain stamp underflow.
        if (!Has(false, 3) && world.ParkOpen &&
            unchecked((uint)(calendar.Month + calendar.Year * 12) - world.OpeningMonth) / 12 >= Definition.YearsOpen)
            Award(false, 3, 0xB1, result);

        var all = world.Attractions.ToArray();
        var rides = all.Where(a => (ParkStatisticCalculator.Category(a.Item.Type, 0) & 0xF) != 0).ToArray();
        var shops = all.Where(a => a.Item.Type == AttractionType.Shop).ToArray();
        var features = all.Where(a => a.Item.Type == AttractionType.Feature).ToArray();
        uint rideCount = unchecked((uint)rides.Length + world.TourTransportCount); // 0x80059914.

        // READ: optional +0x30 byte; raw pool counts, no placed/open filter, 0x80067A90..B2C.
        if (Definition.TutorialAwardEnabled && !Has(false, 4) &&
            all.Any(a => a.Item.Type == AttractionType.SideShow) && shops.Length != 0 &&
            features.Length != 0 && rides.Length >= 2)
            Award(false, 4, 0xBC, result);

        // SOURCE DISAGREEMENT: retain shop-stock.md's feature COUNT reading of 0x80015A68.
        // Native call uses mask 0x40 and >80 (0x80067B44..50), not statistic index 64 or rating.
        if (!Has(true, 0) && features.Count(a => a.Item.Status != AttractionStatus.JustPlaced &&
                (ParkStatisticCalculator.Category(a.Item.Type, a.Item.FeatureFlags) & 0x40) != 0) > 80)
            Award(true, 0, 0xB2, result);

        // READ: all four ride-class lists; any zero upgrade fails, 0x80067808. Empty list is
        // intentionally true here; the separate gate counts tour transports too. ⚠ DO NOT FIX.
        if (!Has(true, 1) && rideCount >= 8 && rides.All(a => a.Item.UpgradeLevel != 0))
            Award(true, 1, 0xB3, result);

        uint featureValue = 0;
        foreach (var feature in features) featureValue = unchecked(featureValue + (uint)feature.FeaturePricePounds);
        if (!Has(true, 2) && rideCount >= 8 && featureValue >= Definition.FeatureValuePounds)
            Award(true, 2, 0xB4, result); // READ: 0x80067BF0..C18; no halving or status filter.

        if (!Has(true, 3) && ShopsHaveBins(shops, features))
            Award(true, 3, 0xB5, result); // READ: 0x80067C30..58, helper arguments 5 and 2.

        if (!Has(true, 4) && rideCount >= 10 && world.PathTileCount <= Definition.MaximumPathTiles)
            Award(true, 4, 0xB6, result); // READ: 0x80067C70..CB4, inclusive UNSIGNED maximum.
        return result;
    }

    bool Has(bool bonus, int bit) => ((bonus ? State.BonusBits : State.ParkBits) & (1u << bit)) != 0;
    void Award(bool bonus, int bit, ushort message, List<ObjectiveAward> result)
    {
        State = bonus ? State with { BonusBits = State.BonusBits | (1u << bit) }
                      : State with { ParkBits = State.ParkBits | (1u << bit) };
        result.Add(new(bonus, bit, message));
    }

    static bool ShopsHaveBins(ObjectiveAttraction[] shops, ObjectiveAttraction[] features)
    {
        if (shops.Length < 5) return false; // READ: 0x800595E0; counts status-zero shops too.
        foreach (var shop in shops)
        {
            if (shop.Item.Status == AttractionStatus.JustPlaced) continue; // 0x80059610..18.
            short left = unchecked((short)(shop.X - 2)), top = unchecked((short)(shop.Y - 2));
            short right = unchecked((short)(shop.X + shop.Width + 2));
            short bottom = unchecked((short)(shop.Y + shop.Height + 2));
            // READ: 0x8005971C..38 / 0x800597F4..84C. Inclusive rectangle overlap; one bin
            // may cover several shops. Width is NOT decremented. Retain signed halfword wrap.
            if (!features.Any(f => f.Item.Status != AttractionStatus.JustPlaced && f.IsLitterBin &&
                unchecked((short)(f.X + f.Width)) >= left && f.X <= right &&
                unchecked((short)(f.Y + f.Height)) >= top && f.Y <= bottom)) return false;
        }
        return true;
    }
}
