using System;
using System.Collections.Generic;
using System.Linq;

namespace TPW.Sim
{
    /// <summary>READ: indices in 0x800DB9F8. Names describe the operands, not what an advisor sentence
    /// suggests they ought to mean. Full provenance, ranges and readers: findings/statistics.md.</summary>
    public enum ParkStatistic
    {
        ParkOpen = 0, NeedBConditionPercent = 1, NeedAConditionPercent = 2,
        TotalDays = 3, CalendarMonths = 4, CalendarYears = 5,
        Entertainers = 6, Mechanics = 7, Guards = 8, Researchers = 9, Cleaners = 10,
        Visitors = 11, LiveLitter = 12,
        PlacedRides = 13, PlacedShops = 14, PlacedSideShows = 15, PlacedFeatures = 16,
        PlacedUsableFeatures = 17, PlacedFeatureFlag1 = 18, PlacedFeatureFlag3 = 19,
        BuiltRideVariety = 20, AvailableRideVariety = 21, AppliedRideUpgrades = 22,
        AvailableRideUpgrades = 23, BuiltShopVariety = 24, AvailableShopVariety = 25,
        BuiltSideShowVariety = 26, AvailableSideShowVariety = 27,
        BuiltFeatureVariety = 28, AvailableFeatureVariety = 29, FeatureDirtiness = 30,
        /// <summary>GUESS-low: retained shop-stock.md §7.5's feature-count interpretation.
        /// ⚠ SOURCE DISAGREEMENT: 0x80015A68 actually divides occupied spatial bits by byte count.
        /// The count interpretation stays until that finding is explicitly superseded.</summary>
        RetainedUsableFeatureCount = 31,
        MechanicsWithoutPatrol = 32, CleanersWithoutPatrol = 33,
        GuardsWithoutPatrol = 34, EntertainersWithoutPatrol = 35,
        MechanicPathCoverage = 36, CleanerPathCoverage = 37,
        GuardPathCoverage = 38, EntertainerPathCoverage = 39,
        MechanicGradePercent = 40, CleanerGradePercent = 41, GuardGradePercent = 42,
        EntertainerGradePercent = 43, ResearcherGradePercent = 44, MechanicsOnStrike = 45,
        /// <summary>GUESS-high: the byte at V+0x59 is happiness (needs.md §1/§6).</summary>
        MeanHappiness = 46,
        BalanceHundreds = 47, WagesExceedIncome = 48, WeightedAttractionCount = 49,
        AnyResearchActive = 50,
        RideWearEvents = 51, RideBreakEvents = 52, MisbehaviourEvents = 53,
        QueueAbandonmentPoints = 54,
        /// <summary>READ: no native writer or disc-rule reader/writer for counter 4. Still copied.</summary>
        DeadCounter4 = 55,
        BalloonEvents = 56, CostumeEvents = 57, SouvenirEvents = 58,
        /// <summary>READ: increment at 0x8002391C on a nonzero catalogue selection result.
        /// GUESS-medium: rejected build attempts; the result's complete meaning is not established.</summary>
        CatalogueResultEvents = 59,
        ShopValueGroup0 = 60, ShopValueGroup2 = 61, ShopValueGroup5 = 62,
        ShopValueGroup1 = 63, SideShowValue = 64,
        ShopSatisfactionGroup0 = 65, ShopSatisfactionGroup2 = 66,
        ShopSatisfactionGroup5 = 67, ShopSatisfactionGroup1 = 68, SideShowSatisfaction = 69,
        EntryValue = 70,
        /// <summary>READ: refresh case is empty, but 0x80016974 writes elapsed rule days here.
        /// ⚠ DO NOT FIX: this is live interpreter scratch, not a missing statistic to renumber away.</summary>
        RuleElapsedDays = 71,
    }

    /// <summary>READ: node/definition inputs to 0x80014554, 0x8001472C, 0x800154A8 and 0x800153B4.
    /// A snapshot supplied by the world; the statistic service does not own attractions.</summary>
    public readonly record struct StatisticAttraction(AttractionType Type, int DefinitionIndex,
        AttractionStatus Status, byte FeatureFlags = 0, int Cleanliness = 0, int UpgradeLevel = 0);

    /// <summary>READ: type-local catalogue identity. Availability must be queried separately because
    /// 0x8006A92C and 0x8006AC04 can complete zero-cost research as a side effect.</summary>
    public readonly record struct StatisticDefinition(AttractionType Type, int Index);

    /// <summary>READ: staff kind, grade bits and 0x80095098's inclusive patrol bounds in tiles.
    /// HasPatrolArea is 0x8009524C, separate from whether the rectangle is empty.</summary>
    public readonly record struct StatisticStaff(StaffKind Kind, int Grade, bool HasPatrolArea = false,
        short Left = 0, short Top = 0, short Right = 0, short Bottom = 0);

    /// <summary>All outside inputs and effects for the statistics service. READ addresses describe the
    /// original accessors, not a requirement that the host store PSX pointers.</summary>
    public interface IParkStatisticsWorld
    {
        /// <summary>READ: advisor flag bit 3, 0x80013298..B0 and 0x800139B4.</summary>
        bool StatisticsEnabled { get; }
        /// <summary>READ: only advisor state 1 reaches 0x80013298. The host calls on its ticks;
        /// speaking, transition and cooldown states freeze BOTH round robins.</summary>
        bool AdvisorIdle { get; }
        /// <summary>READ: 0x80017234, advisor read/write queue cursors +0xA8/+0xA9 are equal.</summary>
        bool AdvisorQueueEmpty { get; }
        bool ParkOpen { get; }
        uint TotalDays { get; }
        uint Month { get; }
        uint Year { get; }
        /// <summary>READ: live people pool count, 0x800537B0; includes guests at the gate.</summary>
        int VisitorCount { get; }
        IEnumerable<Visitor> Visitors { get; }
        int LiveLitterCount { get; }
        IEnumerable<StatisticAttraction> Attractions { get; }
        IEnumerable<StatisticDefinition> Definitions { get; }
        /// <summary>READ: 0x8006A92C → 0x8006AB54. Returns whether level zero is complete.
        /// ⭐ This query can complete zero-cost base research via 0x8006ACB8; enumerating Definitions
        /// must not eagerly perform it. Debug or restricted mode returns true without that query.</summary>
        bool IsDefinitionAvailable(StatisticDefinition definition);
        /// <summary>READ: 0x8006AC04. Completes successive zero-cost research levels via 0x8006ACB8,
        /// stopping at a nonzero cost or three levels. Returns 0..3 INCLUDING the base level;
        /// debug mode returns three directly. The host owns and changes the research records.</summary>
        int AvailableLevelCount(StatisticDefinition definition);
        IEnumerable<StatisticStaff> Staff { get; }
        /// <summary>READ: 0x80053864; counts subtract a staff member currently on the cursor.
        /// Grade and patrol aggregations traverse the full class list, including that member.</summary>
        StaffKind? HeldStaffKind { get; }
        /// <summary>READ: 0x80068714(calendar, 2), the mechanic strike flag, not its prediction.</summary>
        bool MechanicsOnStrike { get; }
        /// <summary>READ: signed 32-bit bank words. Last-month accessors are 0x8008781C / 0x8008767C
        /// with age 1; each returns zero while BANK+0x12BC &lt;= 1.</summary>
        int BalanceRaw { get; }
        int LastMonthWagesRaw { get; }
        int LastMonthIncomeRaw { get; }
        /// <summary>READ: any of five research-topic +0xC words is nonzero, 0x8009B5B8.</summary>
        bool AnyResearchActive { get; }
        int MapWidthTiles { get; }
        int MapHeightTiles { get; }
        /// <summary>READ: 0x80053FD0 → 0x8004D4EC → tile type == 2.</summary>
        bool IsPathTile(int x, int y);
        /// <summary>READ: opcode 7 posts a message through 0x80014118/12C/144.</summary>
        void PostMessage(ushort messageId);
        /// <summary>⚠ SOURCE DISAGREEMENT: litter.md describes a game-state action, whereas
        /// 0x800139EC → 0x8003A65C removes a message from the UI. Keep the older finding's action
        /// boundary opaque here; see statistics.md §0 before choosing a host implementation.</summary>
        void ApplyRuleAction8(short messageId);
    }

    /// <summary>READ: 0x80016AB4's computations. Arithmetic ranges and empty-set defaults vary by slot;
    /// a universal percentage helper would silently change the original.</summary>
    public static class ParkStatisticCalculator
    {
        public const int Percent = 100;                 // 0x80016F04, 0x80015348
        public const int Limit = 30000;                 // 0x80016C2C / 0x80016F54
        public const int MonthsPerYear = 12;            // 0x80016C50..5C
        public const int MoneyScale = 10;               // 0x80017274..94
        public const int GradeMaximum = 4;              // 0x800165A8
        public const int GradeMask = 7;                 // 0x800956F8
        public const int DefinitionSlots = 50;          // 0x80014788 / 0x80014868
        public const int CoverageCellTiles = 4;         // 0x80015D68 / 0x80015DDC
        public const int CoverageSampleStep = 2;        // 0x80015E38 / 0x80015E54
        public const int RideWeight = 6;                // 0x80014324..30
        public const int ShopWeight = 4;                // 0x800143C0
        public const int FeatureWeight = 5;             // 0x80014444

        static readonly StaffKind[] CountKinds = { StaffKind.Entertainer, StaffKind.Mechanic,
            StaffKind.Guard, StaffKind.Researcher, StaffKind.Cleaner };
        static readonly StaffKind[] MetricKinds = { StaffKind.Mechanic, StaffKind.Cleaner,
            StaffKind.Guard, StaffKind.Entertainer, StaffKind.Researcher };
        // READ: 0x8001472C / 0x800150C0 / 0x80015750 / 0x80015920. Effectful catalogue calls
        // make these traversal orders observable even when the final ratios would be identical.
        static readonly AttractionType[] BuiltOrder = { AttractionType.Ride, AttractionType.TourRide,
            AttractionType.TrackRide, AttractionType.RollerCoaster, AttractionType.Feature,
            AttractionType.Shop, AttractionType.SideShow };
        static readonly AttractionType[] AvailableOrder = { AttractionType.Ride, AttractionType.TourRide,
            AttractionType.TrackRide, AttractionType.RollerCoaster, AttractionType.Shop,
            AttractionType.SideShow, AttractionType.Feature };
        static readonly AttractionType[] UpgradeOrder = { AttractionType.Ride, AttractionType.TrackRide,
            AttractionType.TourRide, AttractionType.RollerCoaster };

        static IEnumerable<StatisticDefinition> OrderedDefinitions(IParkStatisticsWorld world,
            IEnumerable<AttractionType> order, int mask)
            => order.Where(type => (Category(type, 0) & mask) != 0)
                .SelectMany(type => world.Definitions.Where(d => d.Type == type).OrderBy(d => d.Index));

        /// <summary>READ: groups in 0x80014554 / 0x8001472C. Feature flags have PRIORITY: usable,
        /// bit 3, bit 1, otherwise. Flags 0x03 on alien scenery make a literal "staff rooms" name wrong.</summary>
        public static int Category(AttractionType type, byte flags) => type switch
        {
            AttractionType.Ride => 0x01,
            AttractionType.TourRide => 0x02,
            AttractionType.TrackRide => 0x04,
            AttractionType.RollerCoaster => 0x08,
            AttractionType.Shop => 0x100,
            AttractionType.SideShow => 0x200,
            AttractionType.Feature => (flags & 1) != 0 ? 0x20 :
                (flags & 8) != 0 ? 0x40 : (flags & 2) != 0 ? 0x80 : 0x10,
            _ => 0,
        };

        /// <summary>READ: zero denominator gives 100 in variety/upgrade helpers (0x80015044,
        /// 0x80015344, 0x80015858, 0x80015A28). It does not mean 100 percent was researched.</summary>
        static int SaturatingRatio(int numerator, int denominator)
            => numerator >= denominator ? Percent : numerator * Percent / denominator;

        /// <summary>READ: 0x80016AB4. The service handles counter copies 51..70 and scratch 71.</summary>
        public static int Compute(ParkStatistic statistic, IParkStatisticsWorld world)
        {
            int slot = (int)statistic;
            if (slot >= 6 && slot <= 10)
            {
                var kind = CountKinds[slot - 6];
                return world.Staff.Count(s => s.Kind == kind) - (world.HeldStaffKind == kind ? 1 : 0);
            }
            if (slot >= 13 && slot <= 19)
            {
                int mask = slot switch { 13 => 0xF, 14 => 0x100, 15 => 0x200,
                    16 => 0xF0, 17 => 0x20, 18 => 0x80, _ => 0x40 };
                // ⚠ DO NOT FIX: nonzero status counts construction and broken attractions too.
                return world.Attractions.Count(a => a.Status != AttractionStatus.JustPlaced &&
                    (Category(a.Type, a.FeatureFlags) & mask) != 0);
            }
            if (slot >= 32 && slot <= 44)
            {
                int start = slot < 36 ? 32 : slot < 40 ? 36 : 40;
                var staff = world.Staff.Where(s => s.Kind == MetricKinds[slot - start]).ToArray();
                if (slot < 36)
                {
                    int assigned = staff.Count(s => s.HasPatrolArea);
                    return assigned >= staff.Length ? 0 : Percent - assigned * Percent / staff.Length;
                }
                if (slot < 40) return PathCoverage(staff, world);
                return staff.Length == 0 ? 0 :
                    staff.Sum(s => s.Grade & GradeMask) * Percent / (staff.Length * GradeMaximum);
            }
            switch (statistic)
            {
                case ParkStatistic.ParkOpen: return world.ParkOpen ? 1 : 0;
                case ParkStatistic.NeedBConditionPercent:
                case ParkStatistic.NeedAConditionPercent:
                    return world.VisitorCount == 0 ? 0 : world.Visitors.Count(v =>
                        VisitorCondition.Of(v) == (GuestCondition)slot) * Percent / world.VisitorCount;
                case ParkStatistic.TotalDays: return (int)Math.Min(world.TotalDays, Limit);
                case ParkStatistic.CalendarMonths:
                    return (int)Math.Min(unchecked(world.Month + MonthsPerYear * world.Year), Limit);
                case ParkStatistic.CalendarYears: return (int)Math.Min(world.Year, Limit);
                case ParkStatistic.Visitors: return world.VisitorCount;
                case ParkStatistic.LiveLitter: return world.LiveLitterCount;
                case ParkStatistic.BuiltRideVariety: return BuiltVariety(0xF, world);
                case ParkStatistic.AvailableRideVariety: return AvailableVariety(0xF, world);
                case ParkStatistic.AppliedRideUpgrades: return AppliedUpgrades(world);
                case ParkStatistic.AvailableRideUpgrades: return AvailableUpgrades(world);
                case ParkStatistic.BuiltShopVariety: return BuiltVariety(0x100, world);
                case ParkStatistic.AvailableShopVariety: return AvailableVariety(0x100, world);
                case ParkStatistic.BuiltSideShowVariety: return BuiltVariety(0x200, world);
                case ParkStatistic.AvailableSideShowVariety: return AvailableVariety(0x200, world);
                case ParkStatistic.BuiltFeatureVariety: return BuiltVariety(0xF0, world);
                case ParkStatistic.AvailableFeatureVariety: return AvailableVariety(0xF0, world);
                case ParkStatistic.FeatureDirtiness:
                    return FeatureStock.ParkDirtiness(world.Attractions.Where(a => a.Type == AttractionType.Feature)
                        .Select(a => ((a.FeatureFlags & 1) != 0, a.Cleanliness)));
                case ParkStatistic.RetainedUsableFeatureCount:
                    // GUESS: preserve shop-stock.md's count, per the user's source precedence rule.
                    return world.Attractions.Count(a => a.Type == AttractionType.Feature &&
                        a.Status != AttractionStatus.JustPlaced && (a.FeatureFlags & 1) != 0);
                case ParkStatistic.MechanicsOnStrike: return world.MechanicsOnStrike ? 1 : 0;
                case ParkStatistic.MeanHappiness:
                    uint sum = 0;
                    foreach (var v in world.Visitors) sum = unchecked(sum + (uint)v.Happiness);
                    return world.VisitorCount == 0 ? 0 : (int)Math.Min(sum / (uint)world.VisitorCount, Percent);
                case ParkStatistic.BalanceHundreds:
                    return Math.Clamp(world.BalanceRaw / MoneyScale / Percent, -Limit, Limit);
                case ParkStatistic.WagesExceedIncome:
                    return world.LastMonthWagesRaw / MoneyScale > world.LastMonthIncomeRaw / MoneyScale ? 1 : 0;
                case ParkStatistic.WeightedAttractionCount:
                    int weighted = 0;
                    foreach (var a in world.Attractions)
                        weighted = unchecked((ushort)(weighted + (a.Type == AttractionType.Feature ? FeatureWeight :
                            a.Type == AttractionType.Shop || a.Type == AttractionType.SideShow ? ShopWeight : RideWeight)));
                    return Math.Min(weighted, Limit);
                case ParkStatistic.AnyResearchActive: return world.AnyResearchActive ? 1 : 0;
                default: throw new ArgumentOutOfRangeException(nameof(statistic));
            }
        }

        /// <summary>READ: 0x8001472C, distinct nonzero-status definitions / available definitions.
        /// A category with no available definitions contributes neither side; duplicates do not help.</summary>
        static int BuiltVariety(int mask, IParkStatisticsWorld world)
        {
            int built = 0, available = 0;
            foreach (var type in BuiltOrder)
            {
                if ((Category(type, 0) & mask) == 0) continue;
                int count = world.Definitions.Where(d => d.Type == type).OrderBy(d => d.Index)
                    .Count(d => world.IsDefinitionAvailable(d));
                if (count == 0) continue;
                available += count;
                var present = new bool[DefinitionSlots];
                foreach (var a in world.Attractions)
                    if (a.Type == type && a.Status != AttractionStatus.JustPlaced)
                        present[a.DefinitionIndex] = true;
                built += present.Count(p => p);
            }
            return SaturatingRatio(built, available);
        }

        /// <summary>READ: 0x800150C0. The selected masks here are 0xF/0x100/0x200/0xF0.
        /// Its duplicate bit-4 test for coaster and track is harmless for mask 0xF; do not generalise
        /// this helper into an arbitrary-mask public API (0x8001519C..1F4).</summary>
        static int AvailableVariety(int mask, IParkStatisticsWorld world)
        {
            var definitions = OrderedDefinitions(world, AvailableOrder, mask).ToArray();
            return SaturatingRatio(definitions.Count(d => world.IsDefinitionAvailable(d)), definitions.Length);
        }

        /// <summary>READ: 0x800154A8 / 0x80015750. Best upgrade on ANY instance per ride definition,
        /// including status zero; divide by unlocked upgrade count only for definitions present.</summary>
        static int AppliedUpgrades(IParkStatisticsWorld world)
        {
            int applied = 0, available = 0;
            foreach (var d in OrderedDefinitions(world, UpgradeOrder, 0xF))
            {
                int bestPlusOne = 0;
                foreach (var a in world.Attractions)
                    if (a.Type == d.Type && a.DefinitionIndex == d.Index)
                        bestPlusOne = Math.Max(bestPlusOne, a.UpgradeLevel + 1);
                if (bestPlusOne == 0) continue;
                applied += bestPlusOne - 1;
                available += world.AvailableLevelCount(d) - 1;
            }
            return SaturatingRatio(applied, available);
        }

        /// <summary>READ: 0x80015874 / 0x80015920. Each definition with nonzero available level count
        /// contributes two possible upgrades; count-1 have been unlocked. Unavailable bases are omitted.</summary>
        static int AvailableUpgrades(IParkStatisticsWorld world)
        {
            int unlocked = 0, possible = 0;
            foreach (var d in OrderedDefinitions(world, UpgradeOrder, 0xF))
            {
                int levels = world.AvailableLevelCount(d);
                if (levels == 0) continue;
                unlocked += levels - 1;
                possible += 2;                           // 0x800158D4
            }
            return SaturatingRatio(unlocked, possible);
        }

        /// <summary>READ: 0x80015D2C. Fraction of sampled path cells covered by at least one assigned
        /// rectangle. Samples only (0,0), (0,2), (2,0), (2,2) of each 4×4 tile cell; no camera input.
        /// ⚠ DO NOT FIX: odd-coordinate paths can be invisible, and rectangle far edges are inclusive.</summary>
        static int PathCoverage(IEnumerable<StatisticStaff> staff, IParkStatisticsWorld world)
        {
            int width = world.MapWidthTiles / CoverageCellTiles, height = world.MapHeightTiles / CoverageCellTiles;
            var cells = new byte[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    bool path = false;
                    for (int dx = 0; dx < CoverageCellTiles && !path; dx += CoverageSampleStep)
                        for (int dy = 0; dy < CoverageCellTiles && !path; dy += CoverageSampleStep)
                            path = world.IsPathTile(x * CoverageCellTiles + dx, y * CoverageCellTiles + dy);
                    if (path) cells[y * width + x] = 1;
                }
            foreach (var s in staff)
            {
                if (!s.HasPatrolArea) continue;
                for (int x = s.Left >> 2; x <= s.Right >> 2; x++)
                    for (int y = s.Top >> 2; y <= s.Bottom >> 2; y++)
                        cells[y * width + x] |= 2;
            }
            int paths = cells.Count(c => c == 1 || c == 3), covered = cells.Count(c => c == 3);
            return covered == 0 ? 0 : SaturatingRatio(covered, paths);
        }
    }
}
