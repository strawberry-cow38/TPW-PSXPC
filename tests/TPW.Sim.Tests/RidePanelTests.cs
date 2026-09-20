using System;
using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class RidePanelTests
    {
        // Crazy Ape's three blocks, rides.md §1.4. Tests needing asymmetry explicitly replace
        // individual words with synthetic inputs; those are counterexamples, not game constants.
        static RidePanelLevel[] Ape() => new[]
        {
            new RidePanelLevel(5, 8, 45, 1, 100, 1, 10, 2000),
            new RidePanelLevel(3, 11, 45, 1, 100, 1, 10, 200),
            new RidePanelLevel(2, 14, 45, 1, 100, 1, 10, 200),
        };

        sealed class World : IRideUpgradeWorld, IRideWearWorld, IAttractionWorld, IRideAnimation,
            IRideLoadWorld, IQueueWorld
        {
            public RidePanelLevel[] Levels = Ape();
            public List<int> LevelReads = new();
            public AttractionType Type { get; set; } = AttractionType.Ride;
            public int Level { get; set; }
            public RidePanelLevel ReadLevel(int level) { LevelReads.Add(level); return Levels[level]; }
            public int? VirtualSeats;
            public int MaximumSeats => VirtualSeats ?? Levels[Level].MaxSeats;
            public int BaseIntensity { get; set; } = 60;
            public int SpeedSlider { get; set; }
            public int Capacity { get; set; }
            public int CyclesPerLoad { get; set; }
            public readonly RideWear Wear = new();
            public readonly RideCycle Cycle = new();
            public int ReliabilityFixed { get => Wear.Reliability; set => Wear.Reliability = value; }
            public int Lifetime { get => Wear.Lifetime; set => Wear.Lifetime = value; }
            public int CyclesRun { get => Cycle.CyclesRun; set => Cycle.CyclesRun = value; }
            public int ClosingProgress { get; set; }
            public int Cleared, Sparkles;
            public bool? FinalSparkle;
            public List<string> Events = new();
            public int ResearchedLevelCount { get; set; } = 3;
            public int MechanicCount { get; set; } = 1;
            public bool MechanicsOnStrike { get; set; }
            public bool QueueAccepts = true, SpendAccepts = true;
            public int Requests;
            public List<Money> Charges = new();
            public Action AtCharge;
            public void ClearRideEffects() { Cleared++; Events.Add("clear"); }
            public bool TryEnqueueUpgrade() { Requests++; return QueueAccepts; }
            public bool TrySpend(Money amount)
            { AtCharge?.Invoke(); Charges.Add(amount); Events.Add("spend"); return SpendAccepts; }
            public void ShowUpgradeEffect(bool finalLevel)
            { Sparkles++; FinalSparkle = finalLevel; Events.Add("sparkle"); }
            public int MaxSeats => MaximumSeats;
            public int WearMultiplier => RidePanel.WearMultiplier(this);
            public bool NoWear => false;
            public void PostMessage(int id) { }
            public bool IsRide => true;
            public bool BuildAnimationComplete => false;
            public int Reliability { get => Wear.ReliabilityPoints; set => Wear.Reliability = value << 12; }
            public bool IsEmpty => Riders == 0;
            public bool MechanicAssigned => false;
            public void EjectEveryone() => Riders = 0;
            public void ClearSmoke() { }
            public int CurrentPhase => 0;
            public int PhaseLength(int phase) => 41; // Crazy Ape mesh length, ride-phase-lengths.md.
            public int BuildAnimationLength => throw new InvalidOperationException();
            public long NowTick { get; set; } = 20;
            public int Riders { get; set; }
            public int TotalDays => 0;
            public int QueueCount { get; set; } = 1;
            public Visitor QueueHead { get; set; } = Guest(VisitorState.WaitingInQueue);
            public void BoardQueueHead() => Riders++;
            public void ShuffleTheRestForward() { }
            public void UnloadFirstRider() => Riders--;
            public AttractionStatus Status = AttractionStatus.Running;
            public int Served;
            public int TargetType(Visitor g) => (int)Type;
            public AttractionStatus RideStatus(Visitor g) => Status;
            int IQueueWorld.QueueCount(Visitor g) => QueueCount;
            public int UpgradeLevel(Visitor g) => Level;
            public int Intensity(Visitor g) => RidePanel.Intensity(this);
            public QueueTile QueueOrigin(Visitor g) => new(10, 10);
            public IReadOnlyList<QueueTile> QueuePath(Visitor g) => Array.Empty<QueueTile>();
            public int QueueIndexOf(Visitor g) => -1;
            public void AppendToQueue(Visitor g) { }
            public void LeaveQueueList(Visitor g) { }
            public bool TryPathToSlot(Visitor g, int x, int y) => true;
            public bool TrySetSingleWaypoint(Visitor g, int x, int y) => true;
            public void FreeWaypoints(Visitor g) { }
            public bool TargetHasEntrance(Visitor g) => true;
            public bool TryPathToEntrance(Visitor g) => true;
            public bool TryPathToLeavePoint(Visitor g) => true;
            public void CountGuestServed(Visitor g) => Served++;
            public bool TargetHasStock(Visitor g) => throw new InvalidOperationException("ride stock");
            public void ConsumeStock(Visitor g, int units) => throw new InvalidOperationException();
            public int StockLevel(Visitor g) => throw new InvalidOperationException();
            public ShopProduct Product(Visitor g) => throw new InvalidOperationException();
            public int SalePrice(Visitor g) => throw new InvalidOperationException("ride ticket");
            public int QualitySlider(Visitor g) => throw new InvalidOperationException();
            public int SecondSlider(Visitor g) => throw new InvalidOperationException();
            public void BookSale(Visitor g, ShopSale s) => throw new InvalidOperationException("ride sale");
            public SideShowGame Game(Visitor g) => throw new InvalidOperationException();
            public void BookPlay(Visitor g, SideShowPlay p) => throw new InvalidOperationException();
            public void RecordSatisfaction(Visitor g, int amount) { }
            public void PostEvent(int id, int value) { }
            public bool TrySpawnProp(Visitor g) => true;
            public void ReleaseModel(Visitor g) { }
        }
        sealed class Dice : IRandomSource { public int Next(int n) => 0; }
        static Visitor Guest(VisitorState state = VisitorState.Unloading)
        {
            var g = Visitor.Spawn(new Dice(), 0);
            g.SetState(state); g.HasTarget = true; g.VisitorType = 0; // preference 90
            g.Happiness = 50; g.Nausea = 0; g.Boredom = 100; g.Money = Money.FromPounds(200);
            return g;
        }
        static World Placed()
        {
            var w = new World(); RidePanel.Place(w); w.Events.Clear(); w.Cleared = 0; return w;
        }

        // REJECTS reusing a previous pool occupant's level, lifetime, progress, or sliders, and charging
        // construction again in the object instead of leaving that to the placement tool.
        [Fact]
        public void PlacementDerivesAllDefaultsFromLevelZero()
        {
            var w = new World { Level = 2, Lifetime = 1, ReliabilityFixed = 123,
                CyclesRun = 9, ClosingProgress = 900, SpeedSlider = 99, Capacity = 14, CyclesPerLoad = 9 };
            RidePanel.Place(w);
            Assert.Equal(0, w.Level); Assert.Equal(45, w.Lifetime);
            Assert.Equal(0x64000, w.ReliabilityFixed); Assert.Equal(0, w.CyclesRun);
            Assert.Equal(0, w.ClosingProgress); Assert.Equal(1, w.Cleared);
            Assert.Equal((50, 4, 5), (w.SpeedSlider, w.Capacity, w.CyclesPerLoad));
            Assert.Empty(w.Charges); Assert.Equal(5, RidePanel.WearMultiplier(w));
        }

        // REJECTS fixed speed 50, rounding up an odd interval, treating seats as the slider, and
        // clamping duration to its minimum. The 30..45 case is Bounce on Iggy, not a bad fixture.
        [Theory]
        [InlineData(80, 100, 8, 1, 10, 90, 4, 5)]
        [InlineData(2, 5, 11, 30, 45, 3, 5, 22)]
        [InlineData(1, 100, 1, 1, 1, 50, 1, 1)]
        [InlineData(1, 100, 0, 1, 0, 50, 1, 1)]
        public void DefaultArithmeticIsLiteral(int min, int max, int seats, int cmin, int cmax,
            int speed, int capacity, int duration)
        {
            var w = new World(); w.Levels[0] = w.Levels[0] with
            { SpeedMin = min, SpeedMax = max, MaxSeats = seats, CyclesMin = cmin, CyclesMax = cmax };
            RidePanel.Place(w);
            Assert.Equal((speed, capacity, duration), (w.SpeedSlider, w.Capacity, w.CyclesPerLoad));
        }

        // REJECTS reading only the current/researched level or taking the first/last block as the
        // widest. Synthetic interior extrema make each min/max operation necessary.
        [Fact]
        public void PanelUsesTheUnionOfAllThreeLevelsEvenWhenLocked()
        {
            var w = Placed(); w.ResearchedLevelCount = 1;
            w.Levels[0] = w.Levels[0] with { SpeedMin = 20, SpeedMax = 80, CyclesMin = 10, CyclesMax = 20 };
            w.Levels[1] = w.Levels[1] with { SpeedMin = 1, SpeedMax = 120, CyclesMin = 2, CyclesMax = 30, MaxSeats = 18 };
            w.Levels[2] = w.Levels[2] with { SpeedMin = 5, SpeedMax = 100, CyclesMin = 1, CyclesMax = 60 };
            w.LevelReads.Clear();
            var r = RidePanel.Ranges(w);
            Assert.Equal(new RideSliderRange(1, 120), r.Speed);
            Assert.Equal(new RideSliderRange(1, 18), r.Capacity);
            Assert.Equal(new RideSliderRange(1, 60), r.Duration);
            Assert.Equal(new[] { 0, 1, 2 }, w.LevelReads);
            Assert.True(r.CapacityVisible); Assert.True(r.DurationVisible);
        }

        // REJECTS initializing the union from the current level and thereby dropping level zero.
        // Here ONLY level zero carries the widest bounds; the current ride is already level two.
        [Fact]
        public void AnUpgradedRideStillIncludesLevelZeroInItsPanelBounds()
        {
            var w = Placed(); w.Level = 2;
            w.Levels[0] = w.Levels[0] with { SpeedMin = 0, SpeedMax = 120,
                CyclesMin = 0, CyclesMax = 60, MaxSeats = 18 };
            var r = RidePanel.Ranges(w);
            Assert.Equal(new RideSliderRange(0, 120), r.Speed);
            Assert.Equal(new RideSliderRange(0, 60), r.Duration);
            Assert.Equal(new RideSliderRange(1, 18), r.Capacity);
        }

        // REJECTS a lower-only or upper-only clamp, swapped slider stores, a current-level capacity
        // cap (Crazy Ape permits 14 at level 0), and restarting or repairing a ride during editing.
        [Theory]
        [InlineData(-1, 0, -9, 1, 1, 1)]
        [InlineData(200, 50, 30, 100, 14, 10)]
        [InlineData(82, 9, 7, 82, 9, 7)]
        public void ApplyClampsTheWidgetButDoesNotRestartTheRide(int s, int c, int d, int es, int ec, int ed)
        {
            var w = Placed(); w.ReliabilityFixed = 1234; w.CyclesRun = 3;
            w.Cycle.Accumulator = 789; w.ClosingProgress = 10; w.Lifetime = 12; w.Riders = 4;
            RidePanel.Apply(w, s, c, d);
            Assert.Equal((es, ec, ed), (w.SpeedSlider, w.Capacity, w.CyclesPerLoad));
            Assert.Equal(1234, w.ReliabilityFixed); Assert.Equal(3, w.CyclesRun);
            Assert.Equal(789, w.Cycle.Accumulator); Assert.Equal(10, w.ClosingProgress);
            Assert.Equal(12, w.Lifetime); Assert.Equal(4, w.Riders); Assert.Empty(w.Events);
        }

        // REJECTS treating a placement default as already panel-clamped. The first callback commits
        // the widget's clamp even when its requested values are the unchanged live settings.
        [Fact]
        public void FirstPanelUpdateCanRaiseIggysDuration()
        {
            var w = new World();
            for (int i = 0; i < 3; i++) w.Levels[i] = w.Levels[i] with { CyclesMin = 30, CyclesMax = 45 + 5 * i };
            RidePanel.Place(w); Assert.Equal(22, w.CyclesPerLoad);
            RidePanel.Apply(w, w.SpeedSlider, w.Capacity, w.CyclesPerLoad);
            Assert.Equal(30, w.CyclesPerLoad);
        }

        // REJECTS using record seats for a coaster, halving the record rather than virtual maximum,
        // or treating the hidden duration widget as absent from the writeback.
        [Fact]
        public void CoasterUsesVirtualSeatsAndStillWritesHiddenDuration()
        {
            var w = new World { Type = AttractionType.RollerCoaster, VirtualSeats = 6 };
            for (int i = 0; i < 3; i++) w.Levels[i] = w.Levels[i] with { CyclesMin = 1, CyclesMax = 1 };
            RidePanel.Place(w); Assert.Equal(3, w.Capacity);
            var r = RidePanel.Ranges(w);
            Assert.Equal(new RideSliderRange(1, 6), r.Capacity);
            Assert.False(r.DurationVisible); Assert.True(r.CapacityVisible);
            RidePanel.Apply(w, 80, 20, 50);
            Assert.Equal((80, 6, 1), (w.SpeedSlider, w.Capacity, w.CyclesPerLoad));
            w.VirtualSeats = 1;
            Assert.False(RidePanel.Ranges(w).CapacityVisible);
            w.VirtualSeats = 2;
            Assert.True(RidePanel.Ranges(w).CapacityVisible);
        }

        // REJECTS retaining the full integer instead of the widget's signed high halfword, and
        // reversing the min-then-max order when supplied contradictory bounds.
        [Fact]
        public void WidgetGetterHasSignedHalfwordSemantics()
        {
            Assert.Equal(-32768, new RideSliderRange(0, 65535).Clamp(32768));
            Assert.Equal(10, new RideSliderRange(30, 10).Clamp(20));
        }

        // REJECTS treating saves like live words or signed fields, and routing restoration through
        // the panel clamp. These synthetic over-range values discriminate byte/halfword stores.
        [Fact]
        public void SaveNarrowsButRestoreDoesNotClamp()
        {
            var w = Placed(); w.SpeedSlider = -1; w.Capacity = 300; w.CyclesPerLoad = 511;
            var s = RidePanel.SaveSliders(w);
            Assert.Equal(new RideSliderSave(65535, 44, 255), s);
            w.SpeedSlider = w.Capacity = w.CyclesPerLoad = 0;
            RidePanel.RestoreSliders(w, s);
            Assert.Equal((65535, 44, 255), (w.SpeedSlider, w.Capacity, w.CyclesPerLoad));
            Assert.Empty(w.Events);
        }

        // REJECTS sign-extending a saved capacity byte. The earlier narrowing fixture wraps to 44,
        // which is deliberately complemented here by a byte with bit 7 set.
        [Fact]
        public void RestoreZeroExtendsBothSavedBytes()
        {
            var w = Placed();
            RidePanel.RestoreSliders(w, new RideSliderSave(60000, 200, 201));
            Assert.Equal((60000, 200, 201), (w.SpeedSlider, w.Capacity, w.CyclesPerLoad));
        }

        // REJECTS offering the binary's overread level, accepting equality with the research count,
        // requiring positive lifetime rather than nonzero, or hiding an otherwise available upgrade.
        [Theory]
        [InlineData(0, 45, 1, false)]
        [InlineData(0, 45, 2, true)]
        [InlineData(1, 45, 2, false)]
        [InlineData(1, 45, 3, true)]
        [InlineData(2, 45, 4, false)]
        [InlineData(0, 0, 3, false)]
        [InlineData(0, -1, 3, true)]
        public void UpgradeOfferSeparatesLifetimeResearchAndPanelLimit(int level, int life, int research, bool expected)
        {
            var w = Placed(); w.Level = level; w.Lifetime = life; w.ResearchedLevelCount = research;
            Assert.Equal(expected, RidePanel.CanOfferUpgrade(w));
        }

        // REJECTS ignoring any request gate or claiming queue success when insertion was refused.
        [Theory]
        [InlineData(0, false, true, false, 0)]
        [InlineData(1, true, true, false, 0)]
        [InlineData(1, false, false, false, 1)]
        [InlineData(1, false, true, true, 1)]
        public void RequestRequiresMechanicsAndAcceptsTheQueueResult(int mechanics, bool strike, bool queue,
            bool expected, int requests)
        {
            var w = Placed(); w.MechanicCount = mechanics; w.MechanicsOnStrike = strike; w.QueueAccepts = queue;
            Assert.Equal(expected, RidePanel.RequestUpgrade(w)); Assert.Equal(requests, w.Requests);
            Assert.Equal(0, w.Level); Assert.Empty(w.Charges); Assert.Empty(w.Events);
            w.Lifetime = 0; w.Requests = 0;
            Assert.False(RidePanel.RequestUpgrade(w)); Assert.Equal(0, w.Requests);
        }

        // REJECTS a running/closed/broken gate or minimum reliability at request time, and immediate
        // closing/payment on clicking the button. The mechanic owns the later transition.
        [Theory]
        [InlineData(AttractionStatus.Running)]
        [InlineData(AttractionStatus.ClosedByPlayer)]
        [InlineData(AttractionStatus.BrokenDown)]
        public void ARequestLeavesTheCurrentRideAlone(AttractionStatus status)
        {
            var w = Placed(); w.Status = status; w.ReliabilityFixed = 0; w.CyclesRun = 4; w.Riders = 3;
            Assert.True(RidePanel.RequestUpgrade(w));
            Assert.Equal(status, w.Status); Assert.Equal(0, w.ReliabilityFixed);
            Assert.Equal(4, w.CyclesRun); Assert.Equal(3, w.Riders);
            Assert.Empty(w.Events); Assert.Empty(w.Charges);
        }

        // REJECTS charging a price difference, charging level zero, converting tenths twice, or
        // letting a failed spend roll back the upgrade. Synthetic unequal prices expose all three.
        [Fact]
        public void CompletionResetsFirstAndChargesFullNewLevelEvenIfSpendFails()
        {
            var w = Placed(); w.Levels[1] = new(3, 11, 99, 80, 100, 30, 45, 275);
            w.SpeedSlider = 3; w.Capacity = 8; w.CyclesPerLoad = 1; w.ReliabilityFixed = 99;
            w.Lifetime = 12; w.CyclesRun = 4; w.Cycle.Accumulator = 1234; w.ClosingProgress = 123;
            w.Riders = 4; w.Status = AttractionStatus.Running; w.SpendAccepts = false;
            w.AtCharge = () =>
            {
                Assert.Equal(1, w.Level); Assert.Equal((90, 5, 22), (w.SpeedSlider, w.Capacity, w.CyclesPerLoad));
                Assert.Equal(0x64000, w.ReliabilityFixed); Assert.Equal(0, w.CyclesRun);
                Assert.Equal(0, w.ClosingProgress); Assert.Equal(1, w.Cleared);
            };
            Assert.True(RidePanel.CompleteUpgrade(w));
            Assert.Equal(new[] { Money.FromRaw(2750) }, w.Charges);
            Assert.Equal(new[] { "clear", "spend", "sparkle" }, w.Events);
            Assert.Equal(false, w.FinalSparkle);
            Assert.Equal(12, w.Lifetime); Assert.Equal(1234, w.Cycle.Accumulator);
            Assert.Equal(4, w.Riders); Assert.Equal(AttractionStatus.Running, w.Status);
            Assert.Equal(3, RidePanel.WearMultiplier(w)); Assert.Equal(11, w.MaximumSeats);
            Assert.Equal(30, w.ReadLevel(w.Level).CyclesMin);
        }

        // REJECTS reapplying panel/research/mechanic/condemnation gates inside completion, and
        // interpreting silent as free or as skipping the defaults. The caller already owns the job.
        [Fact]
        public void SilentCompletionStillPaysAndIgnoresRequestGates()
        {
            var w = Placed(); w.Lifetime = 0; w.ResearchedLevelCount = 1; w.MechanicCount = 0;
            w.MechanicsOnStrike = true; w.Status = AttractionStatus.UnderRepair;
            Assert.True(RidePanel.CompleteUpgrade(w, silent: true));
            Assert.Equal(1, w.Level); Assert.Equal(0, w.Lifetime); Assert.Equal(5, w.Capacity);
            Assert.Equal(new[] { Money.FromPounds(200) }, w.Charges);
            Assert.Equal(new[] { "clear", "spend" }, w.Events); Assert.Equal(0, w.Sparkles);
        }

        // REJECTS treating the last legitimate panel level as the binary's final-sound level, or
        // keeping level one's defaults/multiplier after the second normal upgrade.
        [Fact]
        public void SecondNormalUpgradeUsesItsBlockButNotTheFinalSound()
        {
            var w = Placed(); w.Level = 1;
            Assert.True(RidePanel.CompleteUpgrade(w));
            Assert.Equal(2, w.Level); Assert.Equal(2, RidePanel.WearMultiplier(w));
            Assert.Equal(14, w.MaximumSeats); Assert.Equal(7, w.Capacity);
            Assert.Equal(false, w.FinalSparkle); Assert.False(RidePanel.CanOfferUpgrade(w));
        }

        // REJECTS silently reducing the completion guard to the UI's two upgrades, or supplying a
        // fabricated fourth block. The test explicitly supplies synthetic overread words.
        [Fact]
        public void CompletionCanReachThreeButNeverFour()
        {
            var w = Placed(); var list = new List<RidePanelLevel>(w.Levels)
                { new(7, 18, 80, 80, 100, 1, 60, 999) };
            w.Levels = list.ToArray(); w.Level = 2;
            Assert.False(RidePanel.CanOfferUpgrade(w));
            Assert.True(RidePanel.CompleteUpgrade(w));
            Assert.Equal(3, w.Level); Assert.Equal(true, w.FinalSparkle);
            Assert.Equal(7, RidePanel.WearMultiplier(w)); Assert.Equal(9, w.Capacity);
            Assert.Equal(Money.FromPounds(999), Assert.Single(w.Charges));
            w.Events.Clear(); w.LevelReads.Clear();
            Assert.False(RidePanel.CompleteUpgrade(w)); Assert.Empty(w.Events); Assert.Empty(w.LevelReads);
            Assert.Single(w.Charges); Assert.Equal(3, w.Level);
        }

        // REJECTS reusing level 2 or a default zero/guessed block if a direct caller bypasses the UI.
        [Fact]
        public void MissingFourthBlockFailsExplicitly()
        {
            var w = Placed(); w.Level = 2;
            Assert.Throws<IndexOutOfRangeException>(() => RidePanel.CompleteUpgrade(w));
            Assert.Empty(w.Charges);
        }

        // REJECTS a disconnected setter: the real wear consumer must read edited speed and the
        // upgraded multiplier/seat denominator. Empty load isolates speed from the heavy-load guess.
        [Fact]
        public void SlidersAndUpgradeFeedExistingWear()
        {
            var w = Placed(); w.Riders = 0;
            RidePanel.Apply(w, 100, 8, 5);
            for (int i = 0; i < 4; i++) w.Wear.Tick(AttractionStatus.Running, w);
            Assert.Equal(0x64000 - 320, w.ReliabilityFixed);
            RidePanel.CompleteUpgrade(w); w.Riders = 1; // independent nonzero denominator probe
            for (int i = 0; i < 4; i++) w.Wear.Tick(AttractionStatus.Running, w);
            Assert.Equal(0x64000 - 113, w.ReliabilityFixed);
        }

        // REJECTS confusing capacity with maximum seats or ignoring a duration edit until the next
        // run. The loading and lifecycle code consume the same fields the panel writes.
        [Fact]
        public void CapacityControlsLoadingAndDurationCanEndAnExistingRun()
        {
            var w = Placed(); w.Riders = 3; int emptySince = 0;
            RidePanel.Apply(w, 50, 3, 2);
            Assert.Equal(AttractionStatus.Running, RideLoading.Load(w, ref emptySince));
            w.CyclesRun = 3; w.Cycle.Accumulator = 567;
            Assert.Equal(AttractionStatus.Unloading, AttractionLifecycle.Tick(AttractionStatus.Running, w));
            Assert.Equal(567, w.Cycle.Accumulator);
        }

        // REJECTS multiplying flat animation time by speed. The two rides cross the same real phase
        // boundary despite opposite slider extremes; duration then counts those completions.
        [Fact]
        public void SpeedDoesNotChangeFlatPhaseTime()
        {
            var slow = Placed(); var fast = Placed();
            RidePanel.Apply(slow, 1, 4, 1); RidePanel.Apply(fast, 100, 4, 1);
            for (int i = 0; i < 9; i++)
            {
                Assert.False(slow.Cycle.RunTick(slow, slow.CyclesPerLoad, 0x4000, false));
                Assert.False(fast.Cycle.RunTick(fast, fast.CyclesPerLoad, 0x4000, false));
            }
            Assert.True(slow.Cycle.RunTick(slow, slow.CyclesPerLoad, 0x4000, false));
            Assert.True(fast.Cycle.RunTick(fast, fast.CyclesPerLoad, 0x4000, false));
        }

        // REJECTS wiring intensity to slot 54 or a need attribute, losing candidate identity/type/
        // location flags, or caching the record base instead of recomputing live slot 53.
        [Fact]
        public void IntensityReachesOnlySlot53AndTheExistingTasteScore()
        {
            var w = Placed(); w.Type = AttractionType.TourRide;
            var low = RidePanel.Candidate(w, 17, false, 7, false);
            Assert.Equal(17, low.Id); Assert.Equal(7, low.TypeIndex); Assert.Equal(7, low.DistanceTiles);
            Assert.False(low.OpenToGuests); Assert.False(low.CentreTileValid);
            Assert.Equal(45, low.Slot53); Assert.Equal((0, 0, 0), (low.Slot54, low.Slot55, low.Slot56));
            RidePanel.Apply(w, 100, 4, 7);
            var high = RidePanel.Candidate(w, 17, true, 0, true);
            Assert.Equal(75, high.Slot53); Assert.True(high.OpenToGuests); Assert.True(high.CentreTileValid);
            var g = Guest(); g.RideDesire = 100; g.Nausea = 100;
            Assert.Equal(12, RideScore.Score(g, high, 50, 50)); // (100+70)/14
            g.RideDesire = 0; g.Nausea = 0;
            Assert.Equal(12, RideScore.Score(g, high, 50, 50));
            var normal = Placed();
            Assert.Equal(7, RideScore.Score(g, RidePanel.Candidate(normal, 17, true, 0, true), 50, 50));
        }

        // REJECTS treating high intensity as ticket income or leaving reward consumers on the record
        // base. At 75 preference mismatch is 15, nausea gains 13, boredom loses 75; money is intact.
        [Fact]
        public void LiveIntensityReachesGuestRewardWithoutATicket()
        {
            var w = Placed(); RidePanel.Apply(w, 100, 4, 7); var g = Guest();
            var money = g.Money;
            VisitorQueue.Unload(g, w, new Dice());
            Assert.Equal(65, g.Happiness); Assert.Equal(13, g.Nausea); Assert.Equal(25, g.Boredom);
            Assert.Equal(money, g.Money); Assert.Empty(w.Charges); Assert.Equal(1, w.Served);
        }

        sealed class MechanicWorld : IMechanicWorld
        {
            readonly World ride;
            public MechanicWorld(World ride) => this.ride = ride;
            public long NowTick { get; set; } = 100;
            public bool IsTypeOnStrike(StaffKind k) => false;
            public bool HasPatrolRect(StaffMember s) => true;
            public bool TryPathIntoPatrolArea(StaffMember s) => true;
            public bool StrikeMusterExists => true;
            public bool TryPathToStrikeMuster(StaffMember s) => true;
            public bool TryPathToRest(StaffMember s) => true;
            public bool TryClaimBrokenRide(StaffMember s) => false;
            public bool TryClaimQueuedUpgrade(StaffMember s) => ride.Requests != 0;
            public bool TryClaimQueuedUpgradeAsRepair(StaffMember s) => false;
            public void ReleaseClaim(StaffMember s) { }
            public StaffMember NextMechanic(StaffMember s) => null;
            public bool TryClaimRideFor(StaffMember from, StaffMember to, bool repair) => false;
            public bool TryPathToClaimedRide(StaffMember s) => true;
            public bool TryPathToLeavePoint(StaffMember s) => true;
            // The binary's predicate: the ride's closing-progress word A+0xEC, the same field the
            // upgrade purchase zeroes. Riders on board are NOT consulted (ride-panel.md SOURCE DISAGREEMENTS).
            public int ClosingProgress(StaffMember s) => ride.ClosingProgress;
            public void SetClosingProgress(StaffMember s, int v) => ride.ClosingProgress = v;
            public int FootprintSpan(StaffMember s) => 2;                 // a 1x1 ride: 20.0 units
            public int ClosingStep => 10 << Fixed.FracBits;               // 10.0 a tick: two ticks to close
            public void MarkRideUnderRepair(StaffMember s) => ride.Status = AttractionStatus.UnderRepair;
            public void MarkRideOpenAndRelease(StaffMember s)
                => ride.Status = AttractionLifecycle.Enter(AttractionStatus.Reopen, ride);
            public void CompleteUpgrade(StaffMember s) => RidePanel.CompleteUpgrade(ride);
        }

        // REJECTS an upgrade charged at enqueue or at the start/equality edge of the mechanic's
        // work timer, and REJECTS a closing wait keyed to riders: the rider stays on board throughout
        // and the ride still closes once its progress word reaches 20.0.
        [Fact]
        public void MechanicWorkflowCompletesTheQueuedUpgradeThenReopens()
        {
            var w = Placed(); w.Riders = 1; w.Cycle.Accumulator = 321;
            var park = new MechanicWorld(w); var m = new StaffMember(StaffKind.Mechanic) { Skill = 3 };
            Assert.True(RidePanel.RequestUpgrade(w));
            Mechanic.Idle(m, park, new Dice());
            Assert.Equal(MechanicStates.GoToUpgradeRide, m.State);
            Assert.True(Mechanic.SetOff(m, park, forRepair: false));
            Mechanic.Arrive(m, park, stillWalking: false);
            Assert.Equal(MechanicStates.ClosingForUpgrade, m.State);
            Assert.False(Mechanic.CloseRide(m, park, forUpgrade: true));
            Assert.Equal(10 << Fixed.FracBits, w.ClosingProgress);
            Assert.Empty(w.Charges);
            Assert.False(Mechanic.CloseRide(m, park, forUpgrade: true));   // reaches 20.0, clamped
            Assert.True(Mechanic.CloseRide(m, park, forUpgrade: true));
            Assert.Equal(1, w.Riders);
            Assert.Equal(AttractionStatus.UnderRepair, w.Status);
            park.NowTick = 160;
            Assert.False(Mechanic.Work(m, park, forUpgrade: true)); Assert.Empty(w.Charges);
            park.NowTick = 161;
            Assert.True(Mechanic.Work(m, park, forUpgrade: true));
            Assert.Equal(1, w.Level); Assert.Equal(Money.FromPounds(200), Assert.Single(w.Charges));
            Assert.Equal(321, w.Cycle.Accumulator);
            Assert.Equal(0, w.ClosingProgress);                           // the purchase zeroed it
            Assert.True(Mechanic.OpenRide(m, park));
            Assert.Equal(AttractionStatus.Loading, w.Status);
        }
    }
}
