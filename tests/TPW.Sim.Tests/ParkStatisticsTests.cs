using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class ParkStatisticsTests
    {
        internal sealed class World : IParkStatisticsWorld
        {
            public bool StatisticsEnabled { get; set; } = true;
            public bool AdvisorIdle { get; set; } = true;
            public bool AdvisorQueueEmpty { get; set; }
            public bool ParkOpen { get; set; }
            public uint TotalDays { get; set; }
            public uint Month { get; set; }
            public uint Year { get; set; }
            public int VisitorCount { get; set; }
            public IEnumerable<Visitor> Visitors { get; set; } = Array.Empty<Visitor>();
            public int LiveLitterCount { get; set; }
            public IEnumerable<StatisticAttraction> Attractions { get; set; } = Array.Empty<StatisticAttraction>();
            public IEnumerable<Definition> Catalogue { get; set; } = Array.Empty<Definition>();
            public IEnumerable<StatisticDefinition> Definitions => Catalogue.Select(d => new StatisticDefinition(d.Type,d.Index));
            public readonly List<(string Query,AttractionType Type,int Index)> CatalogueQueries = new();
            public Action<StatisticDefinition> OnAvailabilityQuery;
            public bool IsDefinitionAvailable(StatisticDefinition definition)
            {
                CatalogueQueries.Add(("base",definition.Type,definition.Index));
                OnAvailabilityQuery?.Invoke(definition);
                return Catalogue.First(d => d.Type == definition.Type && d.Index == definition.Index).Available;
            }
            public int AvailableLevelCount(StatisticDefinition definition)
            {
                CatalogueQueries.Add(("levels",definition.Type,definition.Index));
                return Catalogue.First(d => d.Type == definition.Type && d.Index == definition.Index).Levels;
            }
            public IEnumerable<StatisticStaff> Staff { get; set; } = Array.Empty<StatisticStaff>();
            public StaffKind? HeldStaffKind { get; set; }
            public bool MechanicsOnStrike { get; set; }
            public int BalanceRaw { get; set; }
            public int LastMonthWagesRaw { get; set; }
            public int LastMonthIncomeRaw { get; set; }
            public bool AnyResearchActive { get; set; }
            public int MapWidthTiles { get; set; } = 12;
            public int MapHeightTiles { get; set; } = 8;
            public readonly HashSet<(int, int)> Paths = new();
            public readonly List<(int, int)> Samples = new();
            public readonly List<string> Effects = new();
            public bool IsPathTile(int x, int y) { Samples.Add((x, y)); return Paths.Contains((x, y)); }
            public void PostMessage(ushort id) => Effects.Add("post:" + id);
            public void ApplyRuleAction8(short id) => Effects.Add("action8:" + id);
        }

        internal sealed record Definition(AttractionType Type,int Index,bool Available,int Levels = 0);

        sealed class Dice : IRandomSource { public int Next(int bound) => 0; }
        static Visitor Guest(int happiness = 50, int needA = 0, int needB = 0)
        {
            var v = Visitor.Spawn(new Dice(), 0);
            v.Happiness = happiness; v.NeedA = needA; v.NeedB = needB;
            return v;
        }
        static int Compute(int slot, World w) => ParkStatisticCalculator.Compute((ParkStatistic)slot, w);
        internal static StatisticRule Rule(params StatisticInstruction[] instructions) => new(10, instructions);
        internal static void Set(ParkStatistics s, World w, int slot, short value)
            => s.Evaluate(Rule(new StatisticInstruction(StatisticOpcode.Set, (short)slot, value)), w);
        internal static void Ticks(ParkStatistics s, World w, int count)
        { for (int i = 0; i < count; i++) s.Tick(w); }

        // REJECTS renumbering dead entries, omitting 71, or assigning a pleasant name to an unknown flag.
        [Fact]
        public void AllSeventyTwoNumbersRemainAnExternalContract()
        {
            string[] names = ("ParkOpen NeedBConditionPercent NeedAConditionPercent TotalDays CalendarMonths CalendarYears " +
                "Entertainers Mechanics Guards Researchers Cleaners Visitors LiveLitter PlacedRides PlacedShops " +
                "PlacedSideShows PlacedFeatures PlacedUsableFeatures PlacedFeatureFlag1 PlacedFeatureFlag3 " +
                "BuiltRideVariety AvailableRideVariety AppliedRideUpgrades AvailableRideUpgrades BuiltShopVariety " +
                "AvailableShopVariety BuiltSideShowVariety AvailableSideShowVariety BuiltFeatureVariety AvailableFeatureVariety " +
                "FeatureDirtiness RetainedUsableFeatureCount MechanicsWithoutPatrol CleanersWithoutPatrol GuardsWithoutPatrol " +
                "EntertainersWithoutPatrol MechanicPathCoverage CleanerPathCoverage GuardPathCoverage EntertainerPathCoverage " +
                "MechanicGradePercent CleanerGradePercent GuardGradePercent EntertainerGradePercent ResearcherGradePercent " +
                "MechanicsOnStrike MeanHappiness BalanceHundreds WagesExceedIncome WeightedAttractionCount AnyResearchActive " +
                "RideWearEvents RideBreakEvents MisbehaviourEvents QueueAbandonmentPoints DeadCounter4 BalloonEvents " +
                "CostumeEvents SouvenirEvents CatalogueResultEvents ShopValueGroup0 ShopValueGroup2 ShopValueGroup5 ShopValueGroup1 " +
                "SideShowValue ShopSatisfactionGroup0 ShopSatisfactionGroup2 ShopSatisfactionGroup5 ShopSatisfactionGroup1 " +
                "SideShowSatisfaction EntryValue RuleElapsedDays").Split(' ');
            Assert.Equal(72, Enum.GetValues<ParkStatistic>().Length);
            for (int i = 0; i < names.Length; i++) Assert.Equal(names[i], ((ParkStatistic)i).ToString());
        }

        // REJECTS an eager first calculation, global-tick alignment, reversed order, and clearing old values.
        [Fact]
        public void RefreshesInOrderOnCallsOneFiveNineAndKeepsTheOtherSlots()
        {
            var w = new World { ParkOpen = true, Visitors = new[] { Guest(60, needB: 90) }, VisitorCount = 1 };
            var s = new ParkStatistics(0);
            Assert.All(Enum.GetValues<ParkStatistic>(), slot => Assert.Equal(0, s[slot]));
            s.Tick(w);
            Assert.Equal(1, s[ParkStatistic.ParkOpen]);
            w.ParkOpen = false;
            Ticks(s, w, 3);
            Assert.Equal(0, s[ParkStatistic.NeedBConditionPercent]);
            s.Tick(w);
            Assert.Equal(100, s[ParkStatistic.NeedBConditionPercent]);
            Assert.Equal(1, s[ParkStatistic.ParkOpen]);
            Set(s, w, 2, 88);
            Ticks(s, w, 3);
            Assert.Equal(88, s[ParkStatistic.NeedAConditionPercent]);
            s.Tick(w);
            Assert.Equal(0, s[ParkStatistic.NeedAConditionPercent]);
            Ticks(s, w, 278);
            Assert.False(s.FirstSweepComplete);
            Assert.Equal(287, s.RefreshCursor);
            s.Tick(w);
            Assert.True(s.FirstSweepComplete);
            Assert.Equal(0, s.RefreshCursor);
            Assert.Equal(1, s[ParkStatistic.ParkOpen]);
            s.Tick(w);
            Assert.Equal(0, s[ParkStatistic.ParkOpen]);
        }

        // REJECTS a refresh dispatch that skips any slot, copies the wrong counter, or computes scratch 71.
        [Fact]
        public void EachRefreshTouchesExactlyItsOriginalSlot()
        {
            var s = new ParkStatistics(0); var w = new World();
            for (int slot = 0; slot < 72; slot++)
            {
                for (int j = 0; j < 71; j++) Set(s, w, j, (short)(-1000 - j));
                // Add changes scratch without Set's cursor alias.
                s.Evaluate(Rule(new StatisticInstruction(StatisticOpcode.Add, 71, 1)), w);
                var before = Enum.GetValues<ParkStatistic>().Select(p => s[p]).ToArray();
                s.Tick(w);
                for (int j = 0; j < 72; j++)
                    if (j != slot || slot == 71) Assert.Equal(before[j], s[(ParkStatistic)j]);
                if (slot < 51) Assert.NotEqual(before[slot], s[(ParkStatistic)slot]);
                else if (slot < 71) Assert.Equal(-1000 - slot, s[(ParkStatistic)slot]);
                Ticks(s, w, 3);
            }
        }

        // REJECTS refreshing while the advisor is speaking/disabled, stopping native event accumulation
        // while it speaks, or freezing refreshes behind queued messages.
        [Fact]
        public void HostGatesAndMessageQueueHaveDifferentEffects()
        {
            var s = new ParkStatistics(0); var w = new World { StatisticsEnabled = false };
            Ticks(s, w, 300); Assert.Equal(0, s.RefreshCursor);
            w.StatisticsEnabled = true; w.AdvisorIdle = false;
            Ticks(s, w, 300); Assert.Equal(0, s.RefreshCursor);
            s.AddEvent(0,23,w); Assert.Equal(0,s[ParkStatistic.RideWearEvents]);
            w.AdvisorIdle = true;
            Ticks(s, w, 289); Assert.Equal(1, s.RefreshCursor); Assert.Equal(0, s.RuleCursor);
            Assert.Equal(23,s[ParkStatistic.RideWearEvents]);
            w.AdvisorQueueEmpty = true;
            s.Tick(w); Assert.Equal(1, s.RuleCursor);
        }

        // REJECTS inclusive need thresholds, counting the combined condition twice, and using the list as denominator.
        [Fact]
        public void NeedSharesUseExactConditionCodesAndPoolDenominator()
        {
            var w = new World { Visitors = new[] { Guest(50, 81, 81), Guest(50, 81, 0), Guest(50, 0, 76), Guest(50, 80, 75) }, VisitorCount = 7 };
            Assert.Equal(14, Compute(1, w)); Assert.Equal(14, Compute(2, w));
            w.VisitorCount = 0;
            Assert.Equal(0, Compute(1, w)); Assert.Equal(0, Compute(2, w));
        }

        // REJECTS month+year, signed comparisons, uncapped dates, and substituting guests/litter/strike/research for one another.
        [Fact]
        public void SimpleSourcesAndUnsignedCalendarCapsAreExact()
        {
            var w = new World { Month = 11, Year = 3, TotalDays = 30001, ParkOpen = true,
                VisitorCount = 37, LiveLitterCount = 19, MechanicsOnStrike = true, AnyResearchActive = true };
            foreach (var (slot, expected) in new[] { (0,1),(3,30000),(4,47),(5,3),(11,37),(12,19),(45,1),(50,1) })
                Assert.Equal(expected, Compute(slot,w));
            w.TotalDays = 29999; w.Year = 30001;
            Assert.Equal(29999, Compute(3,w)); Assert.Equal(30000, Compute(4,w)); Assert.Equal(30000, Compute(5,w));
            w.TotalDays = uint.MaxValue; w.Year = uint.MaxValue;
            Assert.Equal(30000, Compute(3,w)); Assert.Equal(30000, Compute(5,w));
            w.ParkOpen = w.MechanicsOnStrike = w.AnyResearchActive = false;
            Assert.Equal(0, Compute(0,w)); Assert.Equal(0, Compute(45,w)); Assert.Equal(0, Compute(50,w));
        }

        // REJECTS confusing grade with morale/tiredness, using the hire-screen order, and dropping the held member from means.
        [Fact]
        public void StaffCountsExcludeTheCursorButGradesAndPatrolsDoNot()
        {
            var members = new List<StatisticStaff>();
            for (int kind = 0; kind < 5; kind++)
                for (int n = 0; n <= kind; n++) members.Add(new((StaffKind)kind, kind + 8, n == 0));
            var w = new World { Staff = members, HeldStaffKind = StaffKind.Cleaner };
            Assert.Equal(new[] { 2,1,4,5,2 }, Enumerable.Range(6,5).Select(i => Compute(i,w)));
            Assert.Equal(new[] { 0,67,75,50 }, Enumerable.Range(32,4).Select(i => Compute(i,w)));
            Assert.Equal(new[] { 0,50,75,25,100 }, Enumerable.Range(40,5).Select(i => Compute(i,w)));
            w.Staff = new[] { new StatisticStaff(StaffKind.Mechanic, 7) }; w.HeldStaffKind = StaffKind.Mechanic;
            Assert.Equal(0, Compute(7,w)); Assert.Equal(100, Compute(32,w)); Assert.Equal(175, Compute(40,w));
            w.Staff = Array.Empty<StatisticStaff>(); w.HeldStaffKind = null;
            Assert.All(Enumerable.Range(32,13), i => Assert.Equal(0, Compute(i,w)));
        }

        // REJECTS open-to-guests filtering and treating bits 0/1/3 as independent categories.
        [Fact]
        public void CountsIncludeConstructionAndBreakdownsWithFeatureFlagPriority()
        {
            var w = new World { Attractions = new[] {
                new StatisticAttraction(AttractionType.Ride,0,AttractionStatus.UnderConstruction),
                new StatisticAttraction(AttractionType.TourRide,0,AttractionStatus.BrokenDown),
                new StatisticAttraction(AttractionType.TrackRide,0,AttractionStatus.ClosedByPlayer),
                new StatisticAttraction(AttractionType.RollerCoaster,0,AttractionStatus.UnderRepair),
                new StatisticAttraction(AttractionType.Ride,1,AttractionStatus.JustPlaced),
                new StatisticAttraction(AttractionType.Shop,0,AttractionStatus.Running),
                new StatisticAttraction(AttractionType.SideShow,0,AttractionStatus.Running),
                new StatisticAttraction(AttractionType.SideShow,1,AttractionStatus.Loading),
                new StatisticAttraction(AttractionType.Feature,0,AttractionStatus.Running,11),
                new StatisticAttraction(AttractionType.Feature,1,AttractionStatus.Running,10),
                new StatisticAttraction(AttractionType.Feature,2,AttractionStatus.Running,2),
                new StatisticAttraction(AttractionType.Feature,3,AttractionStatus.Running,0),
                new StatisticAttraction(AttractionType.Feature,4,AttractionStatus.Running,4),
                new StatisticAttraction(AttractionType.Feature,5,AttractionStatus.JustPlaced,1) } };
            Assert.Equal(new[] {4,1,2,5,1,1,1}, Enumerable.Range(13,7).Select(i => Compute(i,w)));
            Assert.Equal(0, ParkStatisticCalculator.Category((AttractionType)99, 0));
            Assert.Equal(72, Compute(49,w)); // five rides×6 + three shops/games×4 + six features×5.
        }

        // REJECTS borrowing the definition type number as its selection mask; the four ride bits are distinct
        // even though the current statistic switch asks for their union.
        [Theory]
        [InlineData(3,0,1)] [InlineData(7,0,2)] [InlineData(6,0,4)] [InlineData(1,0,8)]
        [InlineData(4,0,256)] [InlineData(5,0,512)] [InlineData(2,0,16)]
        [InlineData(2,11,32)] [InlineData(2,10,64)] [InlineData(2,2,128)]
        public void CategoryMasksAreTheBinaryValues(int type,byte flags,int expected)
            => Assert.Equal(expected,ParkStatisticCalculator.Category((AttractionType)type,flags));

        // REJECTS swapping the feature-selection masks in the statistic switch. One feature in each
        // category would conceal that mistake even if the category classifier itself were correct.
        [Fact]
        public void FeatureCountSlotsHaveDifferentCategoryTotals()
        {
            var w = new World { Attractions = new[] { (Flags:0,Count:1),(Flags:1,Count:2),
                (Flags:2,Count:3),(Flags:8,Count:4) }.SelectMany(group => Enumerable.Repeat(
                    new StatisticAttraction(AttractionType.Feature,0,AttractionStatus.Running,(byte)group.Flags),group.Count)) };
            Assert.Equal(new[] {10,2,3,4},Enumerable.Range(16,4).Select(slot => Compute(slot,w)));
            Assert.Equal(2,Compute(31,w));
        }

        // REJECTS counting duplicate instances as variety, combining type-local IDs, and dividing by locked definitions.
        [Fact]
        public void VarietySeparatesBuiltFromAvailableAndTreatsAnEmptyCatalogueAsComplete()
        {
            var w = new World { Catalogue = new[] {
                new Definition(AttractionType.Ride,0,true), new Definition(AttractionType.Ride,1,true),
                new Definition(AttractionType.Ride,2,false), new Definition(AttractionType.TourRide,0,true),
                new Definition(AttractionType.TrackRide,0,false),
                new Definition(AttractionType.Shop,0,true), new Definition(AttractionType.Shop,1,false),
                new Definition(AttractionType.SideShow,0,true), new Definition(AttractionType.SideShow,1,true),
                new Definition(AttractionType.SideShow,2,true),
                new Definition(AttractionType.Feature,0,true), new Definition(AttractionType.Feature,1,true),
                new Definition(AttractionType.Feature,2,false), new Definition(AttractionType.Feature,3,false) },
                Attractions = new[] {
                new StatisticAttraction(AttractionType.Ride,0,AttractionStatus.Running),
                new StatisticAttraction(AttractionType.Ride,0,AttractionStatus.BrokenDown),
                new StatisticAttraction(AttractionType.Ride,1,AttractionStatus.JustPlaced),
                new StatisticAttraction(AttractionType.TourRide,0,AttractionStatus.ClosedByPlayer),
                new StatisticAttraction(AttractionType.TrackRide,0,AttractionStatus.Running),
                new StatisticAttraction(AttractionType.SideShow,0,AttractionStatus.UnderConstruction),
                new StatisticAttraction(AttractionType.Feature,0,AttractionStatus.ClosedByPlayer) } };
            Assert.Equal(new[] {66,60,0,50,33,100,50,50},
                new[] {20,21,24,25,26,27,28,29}.Select(i => Compute(i,w)));
            w.Catalogue = Array.Empty<Definition>();
            Assert.All(new[] {20,21,22,23,24,25,26,27,28,29}, i => Assert.Equal(100,Compute(i,w)));
        }

        // REJECTS averaging upgrades per instance, excluding status zero, or charging unbuilt/locked bases in the denominator.
        [Fact]
        public void UpgradeRatiosUseBestInstanceAndSubtractTheBaseLevel()
        {
            var w = new World { Catalogue = new[] {
                new Definition(AttractionType.Ride,0,true,3), new Definition(AttractionType.Ride,1,true,2),
                new Definition(AttractionType.TourRide,0,true,3), new Definition(AttractionType.TrackRide,0,false,0),
                new Definition(AttractionType.RollerCoaster,0,true,1),
                new Definition(AttractionType.Shop,0,true,3) },
                Attractions = new[] {
                new StatisticAttraction(AttractionType.Ride,0,AttractionStatus.JustPlaced,UpgradeLevel:1),
                new StatisticAttraction(AttractionType.Ride,0,AttractionStatus.Running,UpgradeLevel:0),
                new StatisticAttraction(AttractionType.Ride,1,AttractionStatus.Running,UpgradeLevel:0) } };
            Assert.Equal(33, Compute(22,w)); Assert.Equal(62, Compute(23,w));
            w.Attractions = w.Attractions.Append(new(AttractionType.Ride,0,AttractionStatus.Running,UpgradeLevel:2));
            Assert.Equal(66, Compute(22,w));
        }

        // REJECTS eager/cached availability snapshots: the getter can change research, so it must run
        // only when the owning statistic is refreshed and its returned value must affect that refresh.
        [Fact]
        public void CatalogueAvailabilityRemainsAnEffectfulWorldQuery()
        {
            var w = new World { Catalogue = new[] { new Definition(AttractionType.Ride,0,false) },
                Attractions = new[] { new StatisticAttraction(AttractionType.Ride,0,AttractionStatus.Running) } };
            w.OnAvailabilityQuery = d => w.Catalogue = w.Catalogue.Select(entry =>
                entry.Type == d.Type && entry.Index == d.Index ? entry with { Available = true } : entry).ToArray();
            var s = new ParkStatistics(0);
            Ticks(s,w,80);
            Assert.Empty(w.CatalogueQueries); Assert.False(w.Catalogue.Single().Available);
            s.Tick(w);
            Assert.True(w.Catalogue.Single().Available);
            Assert.Equal(100,s[ParkStatistic.BuiltRideVariety]); Assert.Single(w.CatalogueQueries);
            Ticks(s,w,3); Assert.Single(w.CatalogueQueries);
        }

        // REJECTS pool/enum order, descending definition IDs, querying unbuilt upgrades, or querying
        // unrelated types. Availability getters may finish zero-cost research, making call order matter.
        [Fact]
        public void CatalogueQueriesFollowEachHelpersTypeAndIndexOrder()
        {
            var w = new World { Catalogue = new[] { 1,2,3,4,5,6,7 }.SelectMany(type =>
                new[] { new Definition((AttractionType)type,1,true,3),new Definition((AttractionType)type,0,true,2) }),
                Attractions = new[] { 1,2,3,4,5,6,7 }.Select(type =>
                    new StatisticAttraction((AttractionType)type,0,AttractionStatus.JustPlaced)) };
            foreach (int slot in new[] {20,21,22,23,24,25,26,27,28,29})
            {
                w.CatalogueQueries.Clear(); Compute(slot,w);
                int[] types = slot < 22 ? new[] {3,7,6,1} : slot < 24 ? new[] {3,6,7,1} :
                    slot < 26 ? new[] {4} : slot < 28 ? new[] {5} : new[] {2};
                string query = slot == 22 || slot == 23 ? "levels" : "base";
                int[] indices = slot == 22 ? new[] {0} : new[] {0,1};
                Assert.Equal(types.SelectMany(type => indices.Select(index => (query,(AttractionType)type,index))),
                    w.CatalogueQueries);
            }
        }

        // REJECTS replacing retained slot 31 with the newly traced spatial score, excluding held toilets from dirtiness,
        // and using litter or unweighted feature count for cleanliness.
        [Fact]
        public void LitterDirtinessAndRetainedFeatureCountAreThreeDifferentNumbers()
        {
            var w = new World { LiveLitterCount = 7, Attractions = new[] {
                new StatisticAttraction(AttractionType.Feature,0,AttractionStatus.JustPlaced,1,20),
                new StatisticAttraction(AttractionType.Feature,1,AttractionStatus.Running,1,81),
                new StatisticAttraction(AttractionType.Feature,2,AttractionStatus.Running,0,0),
                new StatisticAttraction(AttractionType.Shop,0,AttractionStatus.Running,1,0) } };
            Assert.Equal(7,Compute(12,w)); Assert.Equal(50,Compute(30,w)); Assert.Equal(1,Compute(31,w));
            w.Attractions = Array.Empty<StatisticAttraction>();
            Assert.Equal(0,Compute(30,w)); Assert.Equal(0,Compute(31,w));
        }

        // REJECTS full-tile scans, exclusive patrol far edges, and overlapping coverage counted twice.
        [Fact]
        public void PatrolCoverageSamplesFourEvenTilesAndUnionsInclusiveRectangles()
        {
            var w = new World { Staff = new[] {
                new StatisticStaff(StaffKind.Guard,0,true,0,0,4,4),
                new StatisticStaff(StaffKind.Guard,0,true,0,0,4,0),
                new StatisticStaff(StaffKind.Guard,0,false,8,4,11,7),
                new StatisticStaff(StaffKind.Mechanic,0,true,8,4,11,7) },
                Attractions = new[] { new StatisticAttraction(AttractionType.Feature,0,AttractionStatus.Running,8) } };
            w.Paths.UnionWith(new[] {(0,2),(6,0),(10,6),(1,5)});
            Assert.Equal(66,Compute(38,w)); Assert.Equal(33,Compute(36,w)); Assert.Equal(0,Compute(37,w));
            Assert.DoesNotContain((1,5),w.Samples);
            Assert.Contains((6,0),w.Samples); Assert.Contains((10,6),w.Samples);
            w.Paths.Clear(); Assert.Equal(0,Compute(38,w));
            w.Paths.UnionWith(new[] {(0,2),(6,0)}); Assert.Equal(100,Compute(38,w));
        }

        // REJECTS rounding, using £ rather than £100, comparing raw tenths, and clamping a wrapped cache to positive.
        [Fact]
        public void MoneyAndHappinessUseTheirOwnScales()
        {
            var w = new World { Visitors = new[] { Guest(97),Guest(100) }, VisitorCount = 3,
                BalanceRaw = -1999, LastMonthWagesRaw = 109, LastMonthIncomeRaw = 101 };
            Assert.Equal(65,Compute(46,w)); Assert.Equal(-1,Compute(47,w)); Assert.Equal(0,Compute(48,w));
            w.VisitorCount = 1; Assert.Equal(100,Compute(46,w));
            w.VisitorCount = 0; Assert.Equal(0,Compute(46,w));
            w.LastMonthWagesRaw = 110; Assert.Equal(1,Compute(48,w));
            w.BalanceRaw = int.MaxValue; Assert.Equal(30000,Compute(47,w));
            w.BalanceRaw = int.MinValue; Assert.Equal(-30000,Compute(47,w));
            w.LiveLitterCount = 32768; var s = new ParkStatistics(0); Ticks(s,w,49);
            Assert.Equal(short.MinValue,s[ParkStatistic.LiveLitter]);
        }

        // REJECTS replacing the helper's u16 accumulator with a wide sum and dropping its final cap.
        // This synthetic list exceeds pool capacity to isolate the arithmetic instructions themselves.
        [Fact]
        public void WeightedCountWrapsBeforeItsSwitchCap()
        {
            var ride = new StatisticAttraction(AttractionType.Ride,0,AttractionStatus.JustPlaced);
            var w = new World { Attractions = Enumerable.Repeat(ride,11000) };
            Assert.Equal(464,Compute(49,w));
            w.Attractions = Enumerable.Repeat(ride,6000);
            Assert.Equal(30000,Compute(49,w));
        }

        // REJECTS a 49-entry scratch set, missing coaster variety, and a ratio above 100 when built IDs exceed availability.
        [Fact]
        public void VarietyHasFiftyPositionsPerTypeAndCapsExcessBuiltDefinitions()
        {
            var w = new World { Catalogue = new[] {
                new Definition(AttractionType.RollerCoaster,49,true),
                new Definition(AttractionType.RollerCoaster,0,false) },
                Attractions = new[] {
                new StatisticAttraction(AttractionType.RollerCoaster,49,AttractionStatus.Running),
                new StatisticAttraction(AttractionType.RollerCoaster,0,AttractionStatus.Running) } };
            Assert.Equal(100,Compute(20,w)); Assert.Equal(50,Compute(21,w));
        }

        // REJECTS undocumented changes to ANY disc rule/threshold/order. Expected data is the separately decoded,
        // addressed finding, not values computed from the C# table; focused behavioral tests exercise its meaning.
        [Fact]
        public void EveryRuleMatchesTheIndependentDiscDecode()
        {
            string root = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(root,"findings/statistics-rules.json"))) root = Directory.GetParent(root)!.FullName;
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"findings/statistics-rules.json")));
            var decoded = doc.RootElement.GetProperty("rules").EnumerateArray().ToArray();
            Assert.Equal(125, ParkStatisticRules.All.Count);
            string[] opcodes = {"End","Equal","NotEqual","Less","Greater","Add","Set","PostMessage","Action8","ElapsedGreater"};
            for (int r = 0; r < decoded.Length; r++)
            {
                var actual = ParkStatisticRules.All[r];
                Assert.Equal(decoded[r].GetProperty("cooldown_days").GetInt32(),actual.CooldownDays);
                var instructions = decoded[r].GetProperty("instructions").EnumerateArray().ToArray();
                Assert.Equal(instructions.Length,actual.Instructions.Count);
                for (int i = 0; i < instructions.Length; i++)
                {
                    Assert.Equal(Array.IndexOf(opcodes,instructions[i].GetProperty("op").GetString()),(int)actual.Instructions[i].Op);
                    var args = instructions[i].GetProperty("args").EnumerateArray().Select(a => a.GetInt16()).ToArray();
                    Assert.Equal(args[0],actual.Instructions[i].A);
                    Assert.Equal(args.Length == 2 ? args[1] : 0,actual.Instructions[i].B);
                }
            }
        }
    }
}
