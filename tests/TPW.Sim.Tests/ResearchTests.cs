using System;
using System.Collections.Generic;
using System.Linq;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class ResearchTests
    {
        // Synthetic catalogue: no game assets or invented production work constants.
        sealed class World : IResearchCatalogueWorld, IResearchWorld, IRideUpgradeWorld, IMechanicWorld
        {
            public readonly Dictionary<ResearchDefinition, ResearchLevel[]> Data = new();
            public readonly Dictionary<ResearchDefinition, int> Built = new();
            public readonly List<(ResearchDefinition Definition, int Message)> Messages = new();
            public readonly List<Money> Charges = new();
            public readonly List<(ResearchDefinition Definition, int Level)> Reads = new();
            public readonly ResearchSystem Research;
            public World()
            {
                // Each group has a locked tier-one topic, so the native five-bin scan stops.
                foreach (int type in new[] { 3, 6, 7, 1, 4, 5, 2, 8 }) Add(type, 0, 1, 200);
                Research = new(this);
            }
            public ResearchDefinition Add(int type, int index, int tier, int work)
            {
                var key = new ResearchDefinition(type, index);
                Data[key] = new[] { new ResearchLevel(tier, work), new(1, 200), new(1, 300) };
                return key;
            }
            public int DefinitionCount(int type) => Data.Keys.Count(k => k.Type == type);
            public ResearchLevel ReadResearchLevel(ResearchDefinition key, int level)
            {
                Reads.Add((key, level));
                // Explicit failure for the unestablished level-three overread.
                return Data[key][level];
            }
            public int BuiltCount(ResearchDefinition key) => Built.GetValueOrDefault(key);
            public bool AllResearchUnlocked { get; set; }
            public bool RestrictedMode { get; set; }
            public int MechanicCount { get; set; } = 1;
            public void AnnounceDiscovery(ResearchDefinition key, int message) => Messages.Add((key, message));
            public int ResearchFunding => Research.Funding;
            public void ContributeResearch(int points) => Research.ContributeResearch(points);
            public long NowTick { get; set; } = 100;
            public bool Strike;
            public bool IsTypeOnStrike(StaffKind kind) => Strike;
            public bool HasPatrolRect(StaffMember staff) => true;
            public bool TryPathIntoPatrolArea(StaffMember staff) => true;
            public bool StrikeMusterExists => true;
            public bool TryPathToStrikeMuster(StaffMember staff) => true;
            public bool TryPathToRest(StaffMember staff) => true;
            public AttractionType Type => AttractionType.Ride;
            public ResearchDefinition RideDefinition => new(3, 0);
            public int Level { get; set; }
            public RidePanelLevel ReadLevel(int level) => level switch
            {
                0 => new(5, 8, 45, 1, 100, 1, 10, 2000),
                1 => new(3, 11, 50, 20, 120, 30, 45, 200),
                2 => new(2, 14, 60, 30, 140, 30, 55, 150),
                _ => throw new InvalidOperationException("No fourth block supplied."),
            };
            public int MaximumSeats => ReadLevel(Level).MaxSeats;
            public int BaseIntensity => 60;
            public int SpeedSlider { get; set; }
            public int Capacity { get; set; }
            public int CyclesPerLoad { get; set; }
            public int ReliabilityFixed { get; set; }
            public int Lifetime { get; set; } = 45;
            public int CyclesRun { get; set; }
            public int ClosingProgress { get; set; }
            public int Clears, Effects, Requests;
            public void ClearRideEffects() => Clears++;
            public int ResearchedLevelCount => Research.LevelCount(RideDefinition);
            public bool MechanicsOnStrike { get; set; }
            public bool TryEnqueueUpgrade() { Requests++; return true; }
            public bool TrySpend(Money amount) { Charges.Add(amount); return false; }
            public void ShowUpgradeEffect(bool finalLevel) => Effects++;
            public AttractionStatus Status = AttractionStatus.Running;
            public bool TryClaimBrokenRide(StaffMember staff) => false;
            public bool TryClaimQueuedUpgrade(StaffMember staff) => Requests != 0;
            public bool TryClaimQueuedUpgradeAsRepair(StaffMember staff) => false;
            public void ReleaseClaim(StaffMember staff) { }
            public StaffMember NextMechanic(StaffMember staff) => null;
            public bool TryClaimRideFor(StaffMember from, StaffMember to, bool repair) => false;
            public bool TryPathToClaimedRide(StaffMember staff) => true;
            public bool TryPathToLeavePoint(StaffMember staff) => true;
            int IMechanicWorld.ClosingProgress(StaffMember staff) => ClosingProgress;
            public void SetClosingProgress(StaffMember staff, int value) => ClosingProgress = value;
            public int FootprintSpan(StaffMember staff) => 2;
            public int ClosingStep => 10 << 12;
            public void MarkRideUnderRepair(StaffMember staff) => Status = AttractionStatus.UnderRepair;
            public void MarkRideOpenAndRelease(StaffMember staff) => Status = AttractionStatus.Loading;
            public void CompleteUpgrade(StaffMember staff) { RidePanel.CompleteUpgrade(this); Requests = 0; }
        }

        sealed class Dice : IRandomSource
        {
            readonly int roll;
            public int Calls;
            public Dice(int roll) => this.roll = roll;
            public int Next(int maxExclusive) { Assert.Equal(10, maxExclusive); Calls++; return roll; }
        }

        static ResearchDefinition Key(int type = 3, int index = 0) => new(type, index);
        static StaffMember Scientist(int skill = 0) => new(StaffKind.Researcher) { Skill = skill };
        static void Start(World w, int slot = 0, int type = 3) => Assert.True(w.Research.Start(slot, Key(type)));

        // REJECTS money-like funding defaults, a hidden sixth topic, or an active initial topic.
        [Fact]
        public void NewParkHasFiveIdleTopicsAndEightyEffort()
        {
            var r = new World().Research;
            Assert.Equal(80, r.Funding);
            Assert.False(r.AnyActive);
            for (int i = 0; i < 5; i++)
            {
                Assert.False(r.Topic(i).Active); Assert.False(r.Topic(i).Finished);
                Assert.Equal(0u, r.Topic(i).ProgressFixed);
            }
            Assert.Throws<IndexOutOfRangeException>(() => r.Topic(5));
        }

        // REJECTS clamping the raw BANK setter, the wrong slider bounds, or a 32-bit funding save.
        [Theory]
        [InlineData(-1, 70)] [InlineData(70, 70)] [InlineData(83, 83)] [InlineData(100, 100)] [InlineData(150, 100)]
        public void FundingSliderAndRawSaveAreDifferent(int input, int expected)
        {
            var r = new World().Research;
            r.ApplyFundingSlider(input); Assert.Equal(expected, r.Funding);
            r.Funding = 337; Assert.Equal(337, r.Funding);
            Assert.Equal(81, r.SaveTopics()[0]);
        }

        // REJECTS a record keyed by index alone, shared static progress, or a fabricated 61st entry.
        [Fact]
        public void CatalogueIsPerParkTypeAndIndexWithSixtyRecords()
        {
            var a = new World().Research; var b = new World().Research;
            a.StoreProgress(Key(), 1, 24);
            Assert.Equal(new ResearchProgress(1, 24), a.Progress(Key()));
            Assert.Equal(default, a.Progress(Key(4)));
            Assert.Equal(default, b.Progress(Key()));
            for (int i = 1; i <= 58; i++) a.Progress(Key(3, i));
            Assert.Throws<InvalidOperationException>(() => a.Progress(Key(3, 59)));
        }

        // REJECTS rolling back a completed level, taking max(percent), looping through overflow
        // completions, saturating byte fields, or losing the >=100 equality edge.
        [Fact]
        public void CatalogueStoresOneCompletionAndAllowsSameLevelRegression()
        {
            var r = new World().Research; var key = Key();
            r.StoreProgress(key, 1, 70); r.StoreProgress(key, 0, 99);
            Assert.Equal(new ResearchProgress(1, 70), r.Progress(key));
            r.StoreProgress(key, 1, 20); Assert.Equal(new ResearchProgress(1, 20), r.Progress(key));
            r.StoreProgress(key, 1, 100); Assert.Equal(new ResearchProgress(2, 0), r.Progress(key));
            r.StoreProgress(key, 2, 399); Assert.Equal(new ResearchProgress(3, 0), r.Progress(key));
            r.StoreProgress(key, 259, -1); Assert.Equal(new ResearchProgress(3, 255), r.Progress(key));
        }

        // REJECTS using work==0 as the free-unlock criterion or treating a getter as read-only.
        [Fact]
        public void ZeroTierUnlocksEvenWithWorkButZeroWorkDoesNotUnlockOnQuery()
        {
            var w = new World(); var key = w.Add(3, 1, 0, 876);
            Assert.True(w.Research.IsAvailable(key));
            Assert.Equal(new ResearchProgress(1, 0), w.Research.Progress(key));
            var paid = w.Add(4, 1, 1, 0);
            Assert.False(w.Research.IsAvailable(paid));
            Assert.Equal(0, w.Research.LevelCount(paid));
        }

        // REJECTS returning zero for future levels, percent>100 as available, or ignoring completed levels.
        [Fact]
        public void ProgressQueryKeepsItsOddFutureLevelAndExactEqualityRules()
        {
            var r = new World().Research; var key = Key();
            r.StoreProgress(key, 1, 47);
            Assert.Equal(100, r.ProgressPercent(key, 0));
            Assert.Equal(47, r.ProgressPercent(key, 2));
            r.StoreProgress(Key(4), 0, -1); Assert.False(r.IsAvailable(Key(4)));
        }

        // REJECTS stopping at one free level, using the restricted-mode availability override for
        // level counts, or mutating progress when the all-unlocked override short-circuits.
        [Fact]
        public void CountChainsFreeLevelsButOnlyDebugOverridesTheCount()
        {
            var w = new World(); var key = Key();
            w.Data[key] = new[] { new ResearchLevel(0, 999), new(0, 888), new(1, 77) };
            Assert.Equal(2, w.Research.LevelCount(key));
            Assert.Equal(new ResearchProgress(2, 0), w.Research.Progress(key));
            w.RestrictedMode = true; Assert.True(w.Research.IsAvailable(Key(4)));
            Assert.Equal(0, w.Research.LevelCount(Key(4)));
            w.AllResearchUnlocked = true; w.Reads.Clear();
            Assert.Equal(3, w.Research.LevelCount(Key(5))); Assert.True(w.Research.IsAvailable(Key(5)));
            Assert.Empty(w.Reads); Assert.Equal(default, w.Research.Progress(Key(5)));
            w.AllResearchUnlocked = false; w.RestrictedMode = false;
            w.Data[key][2] = new(0, 77); Assert.Equal(3, w.Research.LevelCount(key));
        }

        // REJECTS a strict >2/3 threshold, requiring every item, or using current upgrade tiers.
        [Theory]
        [InlineData(1, 1)] [InlineData(2, 2)]
        public void TwoThirdsOfBaseDefinitionsOpensTheNextTier(int completed, int ceiling)
        {
            var w = new World();
            foreach (int type in new[] { 6, 7, 1 }) w.Data.Remove(Key(type));
            w.Add(3, 1, 1, 80); w.Add(3, 2, 1, 80); w.Add(3, 3, 2, 80);
            for (int i = 0; i < completed; i++)
            {
                w.Research.StoreProgress(Key(3, i), 1, 0);
                w.Data[Key(3, i)][1] = new(4, 800); // Rides have distinct tier words per level.
            }
            Assert.Equal(ceiling, w.Research.TierCeiling(0));
            Assert.Equal(ceiling == 2, w.Research.CanSelect(0, Key(3, 3)));
        }

        // REJECTS advancing at one-half: the first sweep's 1/3 and 2/3 cases did not distinguish it.
        [Fact]
        public void HalfOfATierIsNotEnoughToAdvance()
        {
            var w = new World(); w.Add(4, 1, 1, 80); w.Add(4, 2, 2, 80);
            w.Research.StoreProgress(Key(4), 1, 0);
            Assert.Equal(1, w.Research.TierCeiling(1));
            Assert.False(w.Research.CanSelect(1, Key(4, 2)));
        }

        // REJECTS enumerating ride tiers in UI order (the getter's unlock side effects are observable).
        [Fact]
        public void TierScanUsesTrackTourCoasterFlatOrder()
        {
            var w = new World(); w.Research.RefreshTiers();
            Assert.Equal(new[] { 6, 7, 1, 3, 4, 5, 2, 8 }, w.Reads.Select(x => x.Definition.Type).Distinct());
        }

        // REJECTS assuming empty tiers stop the scan or silently clamping the binary's overrun.
        [Fact]
        public void EmptyTiersAdvanceButTheUnestablishedStackOverreadFailsExplicitly()
        {
            var w = new World(); foreach (var key in w.Data.Keys.ToArray()) w.Data[key][0] = new(4, 20);
            Assert.Equal(4, w.Research.TierCeiling(0));
            var empty = new World(); empty.Data.Clear();
            Assert.Throws<InvalidOperationException>(() => empty.Research.RefreshTiers());
        }

        // REJECTS treating catalogue updates as tier invalidations; only active completion dirties it.
        [Fact]
        public void LoadedProgressDoesNotRefreshAnAlreadyCachedTier()
        {
            var w = new World(); w.Add(4, 1, 2, 80);
            Assert.Equal(1, w.Research.TierCeiling(1));
            w.Research.StoreProgress(Key(4), 1, 0);
            Assert.Equal(1, w.Research.TierCeiling(1));
        }

        // REJECTS an automatic predetermined unlock, sorting by numeric type, or returning unlocked bases.
        [Fact]
        public void MenuUsesDefinitionIndexWithinFlatTrackTourCoasterOrder()
        {
            var w = new World(); w.Add(3, 1, 1, 80); w.Add(3, 2, 0, 0);
            Assert.Equal(new[] { Key(3), Key(3, 1), Key(6), Key(7), Key(1) }, w.Research.Candidates(0));
            Assert.Equal(new[] { Key(4) }, w.Research.Candidates(1));
            Assert.Equal(new[] { Key(5) }, w.Research.Candidates(2));
            Assert.Equal(new[] { Key(2) }, w.Research.Candidates(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => w.Research.Candidates(5));
        }

        // REJECTS upgrade research on unbuilt or still-locked rides, offering level three, or applying
        // the ordinary category tier gate to the upgrade list.
        [Fact]
        public void UpgradeTopicsRequireAnUnlockedBuiltRideAndFewerThanThreeLevels()
        {
            var w = new World(); var r = w.Research; var key = Key();
            Assert.False(r.CanSelect(4, key)); w.Built[key] = 1;
            Assert.False(r.CanSelect(4, key)); r.StoreProgress(key, 1, 0);
            w.Data[key][1] = new(4, 200);
            Assert.True(r.CanSelect(4, key)); Assert.True(r.Start(4, key));
            Assert.Equal(new[] { key }, r.Candidates(4));
            r.StoreProgress(key, 3, 0); Assert.False(r.CanSelect(4, key));
            w.Built[key] = 0; r.StoreProgress(Key(6), 1, 0);
            Assert.False(r.CanSelect(4, Key(6)));
        }

        // REJECTS gating track pieces on any built track ride, tier, or variant one instead of zero.
        [Fact]
        public void TrackPiecesRequireAvailableTrackVariantZeroAndAreListedLast()
        {
            var w = new World(); w.Add(6, 1, 0, 0); w.Data[Key(8)][0] = new(4, 12);
            Assert.False(w.Research.CanSelect(4, Key(8)));
            w.Research.StoreProgress(Key(6), 1, 0); w.Built[Key(6)] = 1;
            Assert.Equal(new[] { Key(6), Key(8) }, w.Research.Candidates(4));
            Assert.True(w.Research.Start(4, Key(8)));
            w.Research.StoreProgress(Key(8), 1, 0); Assert.False(w.Research.CanSelect(4, Key(8)));
        }

        // REJECTS overwriting an occupied slot, ignoring tier gates or rechecking the menu's built gate.
        [Fact]
        public void StartHasItsOwnNarrowerGatesAndSelectStopsBeforeFailure()
        {
            var w = new World(); var r = w.Research; Start(w);
            Assert.False(r.Start(0, Key(6))); Assert.Equal(Key(), r.Topic(0).Definition);
            var high = w.Add(3, 1, 4, 100);
            Assert.False(r.Select(0, high)); Assert.False(r.AnyActive);
            Assert.True(r.Start(4, high)); Assert.Equal(high, r.Topic(4).Definition);
            Assert.False(r.Select(4, null)); Assert.False(r.AnyActive);
        }

        // REJECTS a denominator of five, signed division, rounding before shifting, or redistributing remainder.
        [Fact]
        public void ContributionsSplitFixedWorkAcrossOnlyActiveSlots()
        {
            var w = new World(); var r = w.Research;
            r.ContributeResearch(123); Assert.False(r.AnyActive);
            Start(w); Start(w, 1, 4); Start(w, 2, 5);
            Researcher.Research(Scientist(), w);
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(21845u, r.Topic(i).ProgressFixed); Assert.Equal(819200u, r.Topic(i).RequiredFixed);
                Assert.Equal(2u, r.Topic(i).Percent);
            }
            Assert.Equal(0u, r.Topic(3).ProgressFixed); Assert.Equal(0u, r.Topic(4).ProgressFixed);
            Assert.Empty(w.Charges);
        }

        // REJECTS multiplying by employee count or requiring a lab, and applying funding twice.
        [Theory]
        [InlineData(0, 20)] [InlineData(1, 30)] [InlineData(2, 35)] [InlineData(3, 40)] [InlineData(4, 43)]
        [InlineData(8, 20)]
        public void EachResearchStateAddsOneSkillContribution(int skill, int points)
        {
            var w = new World(); Start(w); w.Research.Funding = 100;
            var s = Scientist(skill); s.SetState(StaffClassStates.Researching);
            Researcher.Research(s, w);
            Assert.Equal((uint)(points << 12), w.Research.Topic(0).ProgressFixed);
            Assert.Equal(StaffState.Idle, s.State);
            Researcher.Research(Scientist(skill), w);
            Assert.Equal((uint)(2 * points << 12), w.Research.Topic(0).ProgressFixed);
        }

        // REJECTS inventing skill rows by saturating 5..7 to the last established table entry.
        [Theory]
        [InlineData(5)] [InlineData(6)] [InlineData(7)]
        public void UnknownSkillOverreadsAreExplicit(int skill)
            => Assert.Throws<ArgumentOutOfRangeException>(() => Researcher.Research(Scientist(skill), new World()));

        // REJECTS silently substituting the binary's unsigned tiredness division for the retained
        // behaviour.md signed rule, skipping no-topic fatigue, or removing the existing stat clamp.
        [Theory]
        [InlineData(70, 5, 2)] [InlineData(79, 5, 5)] [InlineData(80, 5, 5)]
        [InlineData(83, 5, 6)] [InlineData(100, 98, 100)] [InlineData(70, 0, 0)]
        public void ResearchFatigueKeepsTheFindingsVersion(int funding, int start, int expected)
        {
            var w = new World(); w.Research.Funding = funding;
            var staff = Scientist(); staff.Tiredness = start; staff.PushState(StaffClassStates.Researching);
            Researcher.Research(staff, w);
            Assert.Equal(expected, staff.Tiredness); Assert.Equal(StaffState.Idle, staff.State);
            Assert.Equal(0, staff.StackDepth); Assert.False(w.Research.AnyActive);
        }

        // REJECTS wrong state numbers, a <=3 probability, work during Idle, or a repeated research state.
        [Theory]
        [InlineData(0, 31)] [InlineData(2, 31)] [InlineData(3, 13)] [InlineData(9, 13)]
        public void IdleOnlyChoosesTheNextState(int roll, int expected)
        {
            var w = new World(); Start(w); var s = Scientist(); var dice = new Dice(roll);
            Researcher.Idle(s, w, dice);
            Assert.Equal(expected, (int)s.State); Assert.Equal(1, dice.Calls);
            Assert.Equal(0u, w.Research.Topic(0).ProgressFixed);
        }

        // REJECTS bypassing inherited rest/strike state numbers or silently changing the retained RNG ordering.
        [Theory]
        [InlineData(true, 0, 26)] [InlineData(false, 81, 49)] [InlineData(false, 80, 31)]
        public void IdleRunsTheSharedStaffCheck(bool strike, int tired, int state)
        {
            var w = new World { Strike = strike }; var s = Scientist(); s.Tiredness = tired; s.HasTarget = true;
            var dice = new Dice(0); Researcher.Idle(s, w, dice);
            Assert.Equal(state, (int)s.State); Assert.Equal(state == 31 ? 1 : 0, dice.Calls);
            if (strike) Assert.False(s.HasTarget);
        }

        // REJECTS floating-point progress, a signed shift/division, or widening the percent multiply.
        [Fact]
        public void WorkUsesLowWordArithmeticAndUnsignedPercentCanGoBackwards()
        {
            var w = new World(); w.Data[Key()][0] = new(1, 524288); Start(w);
            var r = w.Research;
            r.ContributeResearch(524288); Assert.Equal(21474836u, r.Topic(0).ProgressFixed);
            Assert.Equal(0u, r.Topic(0).Percent);
            r.ContributeResearch(524288); Assert.Equal(1u, r.Topic(0).Percent);
            r.ContributeResearch(524288); Assert.Equal(0u, r.Topic(0).Percent);
            Assert.Equal(new ResearchProgress(0, 0), r.Progress(Key()));
            r.ContributeResearch(1048576); Assert.Equal(64424508u, r.Topic(0).ProgressFixed);
        }

        // REJECTS finishing at one point short, requiring >target, spilling surplus into another
        // level, failing to clear the topic, or automatically selecting the next catalogue item.
        [Fact]
        public void ExactCompletionUnlocksOneLevelAndLeavesAnIdleFinishedSlot()
        {
            var w = new World(); w.Data[Key()][0] = new(1, 20); Start(w);
            var r = w.Research; r.ContributeResearch(1999);
            Assert.True(r.AnyActive); Assert.Equal(99, r.Progress(Key()).Percent);
            r.ContributeResearch(1); Assert.True(r.AnyActive); // 81919: independent truncations lose one.
            r.ContributeResearch(1);
            Assert.False(r.AnyActive); Assert.True(r.Topic(0).Finished);
            Assert.Equal(new ResearchDefinition(3, -1), r.Topic(0).Definition);
            Assert.Equal(new ResearchProgress(1, 0), r.Progress(Key()));
            Assert.Equal((Key(), 0x56), Assert.Single(w.Messages));
            Assert.Equal(0, w.Level); Assert.Empty(w.Charges);
            Assert.True(r.Start(0, Key(6))); Assert.False(r.Topic(0).Finished);
            Assert.Equal(0, r.Topic(0).Attention);
        }

        // REJECTS dividing again after an earlier slot finishes or reusing its surplus for the later slot.
        [Fact]
        public void CompletingFirstSlotDoesNotChangeTheCurrentContributionDenominator()
        {
            var w = new World(); w.Data[Key()][0] = new(1, 8); Start(w); Start(w, 1, 4);
            w.Research.ContributeResearch(1600);
            Assert.False(w.Research.Topic(0).Active);
            Assert.Equal(32768u, w.Research.Topic(1).ProgressFixed);
            Assert.Equal(1, w.Research.Progress(Key()).CompletedLevels);
            w.Research.ContributeResearch(1600);
            Assert.Equal(98304u, w.Research.Topic(1).ProgressFixed);
        }

        // REJECTS delaying a zero-work topic until positive input or suppressing discovery messages by type.
        [Theory]
        [InlineData(4, 1, 0x58)] [InlineData(5, 2, 0x59)] [InlineData(2, 3, 0x5A)]
        [InlineData(8, 4, 0x57)] [InlineData(1, 0, 0x56)] [InlineData(6, 0, 0x56)] [InlineData(7, 0, 0x56)]
        public void ZeroWorkCompletesOnAContributionEvenWhenItIsZero(int type, int slot, int message)
        {
            var w = new World(); w.Data[Key(type)][0] = new(1, 0); Start(w, slot, type);
            Assert.True(w.Research.AnyActive); Assert.Equal(100u, w.Research.Topic(slot).Percent);
            w.Research.ContributeResearch(0);
            Assert.Equal((Key(type), message), Assert.Single(w.Messages));
            Assert.Equal(new ResearchProgress(1, 0), w.Research.Progress(Key(type)));
        }

        // REJECTS treating newly researched upgrades as base discoveries or ignoring mechanic availability.
        [Theory]
        [InlineData(0, 0x90)] [InlineData(1, 0x57)]
        public void UpgradeResearchChoosesTheMechanicAdviceMessage(int mechanics, int message)
        {
            var w = new World { MechanicCount = mechanics }; var r = w.Research;
            r.StoreProgress(Key(), 1, 0); Assert.True(r.Start(4, Key())); r.ContributeResearch(30000);
            Assert.Equal((Key(), message), Assert.Single(w.Messages));
            Assert.Equal(new ResearchProgress(2, 0), r.Progress(Key())); // excess 100 work is lost.
            Assert.Equal(150u, r.Topic(4).Percent); // REJECTS the initial sweep's surviving display cap.
        }

        // REJECTS keeping the tier cache clean after completion, even for a topic in a different category.
        [Fact]
        public void ActiveCompletionRefreshesEligibility()
        {
            var w = new World(); w.Add(4, 1, 2, 20); Start(w, 1, 4);
            Assert.Equal(1, w.Research.TierCeiling(1));
            w.Research.ContributeResearch(20000);
            Assert.Equal(2, w.Research.TierCeiling(1)); Assert.True(w.Research.CanSelect(1, Key(4, 1)));
        }

        // REJECTS keeping fractions on stop/reselect, clearing catalogue percent, or applying stopped work.
        [Fact]
        public void SwitchingTopicsKeepsOnlyWholePercentAndDoesNotTransferWork()
        {
            var w = new World(); Start(w); var r = w.Research;
            r.ContributeResearch(1234); Assert.Equal(50544u, r.Topic(0).ProgressFixed);
            Assert.Equal(6, r.Progress(Key()).Percent); r.Stop(0);
            r.ContributeResearch(999); Assert.Equal(50544u, r.Topic(0).ProgressFixed);
            Assert.True(r.Select(0, Key(6))); Assert.Equal(0u, r.Topic(0).ProgressFixed);
            Assert.True(r.Select(0, Key())); Assert.Equal(49152u, r.Topic(0).ProgressFixed);
        }

        // REJECTS a raw accumulator save, wrong triple order, loading topics before catalogue, losing
        // the fifth slot, funding clamp on restore, or resetting inactive topic flags in the loader.
        [Fact]
        public void TopicSaveRebuildsAllActiveSlotsFromCataloguePercent()
        {
            var w = new World(); var r = w.Research; Start(w); Start(w, 4, 8);
            r.ContributeResearch(2468); r.Funding = 69;
            var saved = r.SaveTopics(); Assert.Equal(16, saved.Length);
            Assert.Equal(new byte[] { 69, 1, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 8, 0 }, saved);
            var loaded = new World();
            foreach (var key in new[] { Key(), Key(8) })
            {
                var p = r.Progress(key); loaded.Research.StoreProgress(key, p.CompletedLevels, p.Percent);
            }
            loaded.Research.RestoreTopics(saved);
            Assert.Equal(69, loaded.Research.Funding);
            Assert.Equal(49152u, loaded.Research.Topic(0).ProgressFixed);
            Assert.Equal(49152u, loaded.Research.Topic(4).ProgressFixed);
            Assert.True(loaded.Research.Topic(4).Active); Assert.False(loaded.Research.Topic(3).Active);
            Assert.Throws<ArgumentException>(() => loaded.Research.RestoreTopics(new byte[15]));
        }

        // REJECTS unsigned interpretation of the restored index byte; malformed saves fail explicitly
        // through the host's missing-definition lookup rather than silently becoming variant 255.
        [Fact]
        public void TopicRestoreSignExtendsTheDefinitionIndex()
        {
            var w = new World(); var r = w.Research; var data = new byte[16];
            data[0] = 80; data[1] = 1; data[2] = 3; data[3] = 255;
            Assert.Throws<KeyNotFoundException>(() => r.RestoreTopics(data));
            Assert.Contains(w.Reads, read => read.Definition == new ResearchDefinition(3, -1));
        }

        // REJECTS conflating research with the paid instance upgrade, charging enqueue, bypassing
        // mechanic time, resetting lifetime/status at purchase, or replacing existing panel defaults.
        [Fact]
        public void ResearchThenMechanicUpgradeUsesTheExistingRidePipeline()
        {
            var w = new World(); w.Data[Key()][0] = new(0, 0); w.Built[Key()] = 1;
            RidePanel.Place(w); var r = w.Research;
            Assert.False(RidePanel.RequestUpgrade(w)); Assert.Equal(1, r.LevelCount(Key()));
            Assert.True(r.CanSelect(4, Key())); Start(w, 4);
            for (int i = 0; i < 12; i++) Researcher.Research(Scientist(), w);
            Assert.False(RidePanel.RequestUpgrade(w)); Researcher.Research(Scientist(), w);
            Assert.Equal(2, r.LevelCount(Key())); Assert.Equal(0, w.Level); Assert.Empty(w.Charges);
            Assert.True(RidePanel.RequestUpgrade(w)); Assert.Empty(w.Charges);
            var m = new StaffMember(StaffKind.Mechanic) { Skill = 3 };
            Mechanic.Idle(m, w, new MechanicDice());
            Assert.Equal(MechanicStates.GoToUpgradeRide, m.State);
            Assert.True(Mechanic.SetOff(m, w, false)); Mechanic.Arrive(m, w, false);
            Assert.False(Mechanic.CloseRide(m, w, true)); Assert.False(Mechanic.CloseRide(m, w, true));
            Assert.True(Mechanic.CloseRide(m, w, true)); w.NowTick = 160;
            Assert.False(Mechanic.Work(m, w, true)); w.NowTick = 161;
            Assert.True(Mechanic.Work(m, w, true));
            Assert.Equal(1, w.Level); Assert.Equal(Money.FromPounds(200), Assert.Single(w.Charges));
            Assert.Equal((5, 70, 22), (w.Capacity, w.SpeedSlider, w.CyclesPerLoad));
            Assert.Equal(3, RidePanel.WearMultiplier(w)); Assert.Equal(45, w.Lifetime);
            Assert.Equal(AttractionStatus.UnderRepair, w.Status); Assert.Equal(0x64000, w.ReliabilityFixed);
            Assert.Equal(0, w.Requests); Assert.Equal(2, r.LevelCount(Key()));
            Assert.True(Mechanic.OpenRide(m, w)); Assert.Equal(AttractionStatus.Loading, w.Status);
            Assert.False(RidePanel.CanOfferUpgrade(w));
            Assert.True(r.Start(4, Key())); r.ContributeResearch(30000);
            Assert.Equal(3, r.LevelCount(Key())); Assert.True(RidePanel.CanOfferUpgrade(w));
            Assert.True(RidePanel.CompleteUpgrade(w)); Assert.Equal(2, w.Level);
            Assert.Equal((7, 85, 27), (w.Capacity, w.SpeedSlider, w.CyclesPerLoad));
            Assert.Equal(2, RidePanel.WearMultiplier(w)); Assert.False(RidePanel.CanOfferUpgrade(w));
        }

        sealed class MechanicDice : IRandomSource { public int Next(int maxExclusive) => 0; }
    }
}
