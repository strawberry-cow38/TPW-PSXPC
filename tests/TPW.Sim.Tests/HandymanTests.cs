using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>The handyman and the researcher (behaviour.md §3.6, §3.3).</summary>
    public class HandymanTests
    {
        sealed class Dice : IRandomSource
        {
            readonly Queue<int> _v;
            public Dice(params int[] v) => _v = new Queue<int>(v);
            public int Next(int n) => _v.Count > 0 ? _v.Dequeue() : 0;
        }

        sealed class World : IHandymanWorld, IResearchWorld
        {
            public long NowTick { get; set; }
            public bool OnStrike { get; set; }
            public bool LitterAvailable { get; set; }
            public bool BinAvailable { get; set; }
            public bool IsVomit { get; set; }
            public int BinRemaining { get; set; } = 100;
            public int Deleted { get; private set; }
            public int Unclaimed { get; private set; }
            public int Emptied { get; private set; }
            public int ResearchPoints { get; private set; }
            public int ResearchFunding { get; set; } = 1;

            public bool IsTypeOnStrike(StaffKind k) => OnStrike;
            public bool HasPatrolRect(StaffMember s) => true;
            public bool TryPathIntoPatrolArea(StaffMember s) => true;
            public bool StrikeMusterExists => true;
            public bool TryPathToStrikeMuster(StaffMember s) => true;
            public bool TryPathToRest(StaffMember s) => true;

            public bool TryClaimNearestLitter(StaffMember s) => LitterAvailable;
            public bool TryChooseBin(StaffMember s) => BinAvailable;
            public bool ClaimedLitterIsVomit(StaffMember s) => IsVomit;
            public void DeleteClaimedLitter(StaffMember s) => Deleted++;
            public void UnclaimLitter(StaffMember s) => Unclaimed++;
            public int ChosenBinRemaining(StaffMember s) => BinRemaining;
            public void EmptyChosenBin(StaffMember s) => Emptied++;
            public void ContributeResearch(int p) => ResearchPoints += p;
        }

        static StaffMember Hand(int skill = 0, int morale = 50)
            => new(StaffKind.Cleaner) { Skill = skill, Morale = morale };

        // ⭐ READ OUT OF THE EXECUTABLE, not copied from prose. A top-skill handyman cleans twelve times
        // faster than a new one and empties bins twelve times faster. REJECTS a flat table or invented durations.
        [Fact]
        public void TheSkillTablesAreTheExecutablesOwnNumbers()
        {
            Assert.Equal(new[] { 120, 60, 30, 20, 10 }, Handyman.CleanTicks);
            Assert.Equal(new[] { 180, 120, 60, 30, 15 }, Handyman.EmptyTicks);
            Assert.Equal(new[] { 20, 30, 35, 40, 43 }, Researcher.PointsBySkill);
            // READ refinement (staff.md/litter.md): slot 44 reads this as speed, not a third job duration.
            Assert.Equal(new[] { 10, 15, 20, 20, 18 }, Handyman.UnusedThirdColumn);
        }

        // ⚠ THE ORIGINAL INDEXES BY skill & 7 AND THE TABLES HAVE FIVE ROWS. This clamps rather than
        // reading past the end, and the test says so, so the divergence is recorded rather than hidden.
        [Fact]
        public void AnOutOfRangeSkillClampsRatherThanReadingPastTheTable()
        {
            Assert.Equal(10, Handyman.BySkill(Handyman.CleanTicks, 4));
            Assert.Equal(10, Handyman.BySkill(Handyman.CleanTicks, 7));   // would be garbage on hardware
            Assert.Equal(120, Handyman.BySkill(Handyman.CleanTicks, 0));
        }

        // ⭐ THE BIN BRANCH FALLS BACK TO LITTER; THE LITTER BRANCH DOES NOT FALL BACK TO BINS. So a
        // park with full bins and no litter is served half as often as one with litter and no bins.
        // REJECTS making the two branches symmetrical, which is the obvious tidy-up.
        [Fact]
        public void TheBinBranchFallsBackToLitterButNotTheOtherWayRound()
        {
            // Bins first, no bin, but litter present: he goes to the litter.
            var a = Hand();
            Handyman.Idle(a, new World { LitterAvailable = true, BinAvailable = false }, new Dice(0));
            Assert.Equal(StaffClassStates.ToLitter, a.Purpose);

            // Litter first, no litter, bin present: he does NOT go to the bin, he patrols.
            var b = Hand();
            Handyman.Idle(b, new World { LitterAvailable = false, BinAvailable = true }, new Dice(1));
            Assert.Equal(StaffState.Patrolling, b.State);
        }

        [Fact]
        public void WithNothingToDoHePatrols()
        {
            var h = Hand();
            Handyman.Idle(h, new World(), new Dice(0));
            Assert.Equal(StaffState.Patrolling, h.State);
        }

        // ⚠ THE LITTER WALK PUSHES AND THE BIN WALK SETS. Different in the report (Push 11 against
        // Set 11), so a bin trip does not return to whatever came before it.
        [Fact]
        public void TheLitterWalkPushesAndTheBinWalkDoesNot()
        {
            var toLitter = Hand();
            Handyman.Idle(toLitter, new World { LitterAvailable = true }, new Dice(1));
            Assert.Equal(StaffState.Walking, toLitter.State);
            Assert.Equal(1, toLitter.StackDepth);

            var toBin = Hand();
            Handyman.Idle(toBin, new World { BinAvailable = true }, new Dice(0));
            Assert.Equal(StaffState.Walking, toBin.State);
            Assert.Equal(0, toBin.StackDepth);
        }

        // The job timer is set on arrival and comes from the skill table.
        [Theory]
        [InlineData(0, 120)]
        [InlineData(4, 10)]
        public void ArrivingAtLitterStartsASkillLengthTimer(int skill, int ticks)
        {
            var h = Hand(skill); h.Purpose = StaffClassStates.ToLitter;
            Handyman.Arrive(h, new World { NowTick = 1000 });
            Assert.Equal(StaffClassStates.CleaningLitter, h.State);
            Assert.Equal(1000 + ticks, h.BusyUntil);
        }

        [Fact]
        public void NothingHappensUntilTheTimerRunsOut()
        {
            var h = Hand(); h.BusyUntil = 500;
            var world = new World { NowTick = 500 };
            Assert.False(Handyman.CleanLitter(h, world));
            Assert.Equal(0, world.Deleted);
            world.NowTick = 501;
            Assert.True(Handyman.CleanLitter(h, world));
            Assert.Equal(1, world.Deleted);
        }

        // ⭐ THE PENALTIES DWARF THE REWARDS. Vomit is -6 against litter's +1; a neglected bin is -10
        // against a tidy one's +5. A park that lets its bins fill grinds handymen down about twice as
        // fast as a clean one builds them up, which is how neglect becomes a strike.
        [Fact]
        public void UnpleasantJobsCostFarMoreMoraleThanPleasantOnesPay()
        {
            var tidy = Hand(morale: 50); tidy.BusyUntil = 0;
            Handyman.CleanLitter(tidy, new World { NowTick = 1, IsVomit = false });
            Assert.Equal(51, tidy.Morale);
            Assert.Equal(5, tidy.Tiredness);

            var grim = Hand(morale: 50); grim.BusyUntil = 0;
            Handyman.CleanLitter(grim, new World { NowTick = 1, IsVomit = true });
            Assert.Equal(44, grim.Morale);

            var goodBin = Hand(morale: 50); goodBin.BusyUntil = 0;
            Handyman.EmptyBin(goodBin, new World { NowTick = 1, BinRemaining = 50 });
            Assert.Equal(55, goodBin.Morale);

            var fullBin = Hand(morale: 50); fullBin.BusyUntil = 0;
            Handyman.EmptyBin(fullBin, new World { NowTick = 1, BinRemaining = 39 });
            Assert.Equal(40, fullBin.Morale);
        }

        // The neglect threshold is on REMAINING capacity, so lower is worse. Both sides of 40.
        [Theory]
        [InlineData(40, 55)]     // exactly 40 remaining is not neglected
        [InlineData(39, 40)]
        public void TheNeglectedBinThresholdIsRemainingBelowForty(int remaining, int morale)
        {
            var h = Hand(morale: 50); h.BusyUntil = 0;
            Handyman.EmptyBin(h, new World { NowTick = 1, BinRemaining = remaining });
            Assert.Equal(morale, h.Morale);
        }

        // ⚠ A FAILED WALK MUST RELEASE THE CLAIM, or that piece of litter stays reserved by a handyman
        // who never arrives and no one else ever collects it.
        [Fact]
        public void AFailedWalkToLitterReleasesTheClaim()
        {
            var h = Hand(); h.Purpose = StaffClassStates.ToLitter;
            var world = new World();
            Handyman.OnPathMessage(h, world, pathFound: false);
            Assert.Equal(1, world.Unclaimed);
            Assert.Equal(StaffState.Patrolling, h.State);

            // A failed bin walk has nothing to release and falls through to the base handling.
            var b = Hand(); b.Purpose = StaffClassStates.ToBin;
            var w2 = new World();
            Handyman.OnPathMessage(b, w2, pathFound: false);
            Assert.Equal(0, w2.Unclaimed);
        }

        // ⭐ A RESEARCHER IS NOT A STATIONARY WORKER: three ticks in ten it researches, the rest it
        // patrols. REJECTS parking it in the research state, which would make its output a steady rate
        // and stop it ever tiring.
        [Theory]
        [InlineData(0, true)] [InlineData(2, true)] [InlineData(3, false)] [InlineData(9, false)]
        public void AResearcherAlternatesResearchWithPatrolling(int roll, bool researches)
        {
            var r = new StaffMember(StaffKind.Researcher);
            Researcher.Idle(r, new World(), new Dice(roll));
            Assert.Equal(researches ? StaffClassStates.Researching : StaffState.Patrolling, r.State);
        }

        // Output is points-by-skill multiplied by what the bank is putting behind research, and the
        // skill curve flattens hard at the top.
        [Fact]
        public void ResearchOutputScalesWithSkillAndFunding()
        {
            var novice = new StaffMember(StaffKind.Researcher) { Skill = 0 };
            var world = new World { ResearchFunding = 2 };
            Researcher.Research(novice, world);
            Assert.Equal(40, world.ResearchPoints);              // 20 * 2
            Assert.Equal(StaffState.Idle, novice.State);         // one tick only

            var expert = new StaffMember(StaffKind.Researcher) { Skill = 4 };
            var w2 = new World { ResearchFunding = 1 };
            Researcher.Research(expert, w2);
            Assert.Equal(43, w2.ResearchPoints);                 // barely more than grade 4's 40
        }
        // The staff-side low-stock line is 60, strictly (0x80098FC4, `slti 0x3C`): a bin reading 60 is
        // left alone, 59 gets a visit. REJECTS `<= 60`, and REJECTS reusing the guest's 50 or the
        // neglect threshold's 40 for the pick.
        [Theory]
        [InlineData(61, false)] [InlineData(60, false)] [InlineData(59, true)] [InlineData(0, true)]
        public void ABinIsWorthATripStrictlyBelowSixtyRemaining(int remaining, bool wanted)
        {
            Assert.Equal(60, Handyman.BinPickBelow);
            Assert.Equal(wanted, Handyman.WantsEmptying(remaining));
        }

        // ⚠ PINS THE FINDINGS' FORMULA, WHICH THE BINARY DISPUTES (see Handyman.BinScore). behaviour.md
        // §3.6: Manhattan distance × (remaining + 1), so (3, 4, 10) is 7 × 11 = 77. The instructions at
        // 0x80099018..0x80099050 weight only the y term, which would give 3 + 4 × 11 = 47. This test
        // REJECTS that reading DELIBERATELY, per the port's disagreement policy; when the dispute is
        // resolved in the binary's favour, change the expected values here and the formula together.
        // It also REJECTS an unweighted distance (7), a signed distance (−1 × 11 = −11), and weighting
        // by remaining alone (70).
        [Theory]
        [InlineData(3, 4, 10, 77)]
        [InlineData(-3, 4, 10, 77)]
        [InlineData(3, -4, 10, 77)]
        [InlineData(0, 0, 59, 0)]
        [InlineData(5, 0, 0, 5)]
        public void BinScoreIsManhattanDistanceTimesRemainingPlusOne(int dx, int dy, int remaining, int expected)
        {
            Assert.Equal(expected, Handyman.BinScore(dx, dy, remaining));
        }

    }
}
