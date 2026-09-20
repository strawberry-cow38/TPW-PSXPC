using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>The per-tick needs update, vtable slot 41 (behaviour.md §2.9).</summary>
    public class VisitorNeedsTests
    {
        sealed class Dice : IRandomSource
        {
            readonly Queue<int> _v;
            public Dice(params int[] v) => _v = new Queue<int>(v);
            public int Next(int n) => _v.Count > 0 ? _v.Dequeue() : 0;
        }

        sealed class World : INeedsWorld
        {
            public long NowTick { get; set; }
            public bool IdleNeedsSuppressed { get; set; }
            public TileInfluence Influence { get; set; }
            public int Litter { get; set; }
            public int Vomit { get; set; }
            public bool Queueing { get; set; }
            public bool EntertainerAvailable { get; set; }
            public int WatchSkill { get; set; }

            public TileInfluence InfluenceAt(Visitor g) => Influence;
            public (int Litter, int Vomit) LitterNearby(Visitor g) => (Litter, Vomit);
            public bool InQueue(Visitor g) => Queueing;
            public IEnumerable<(StaffMember Entertainer, int Distance)> EntertainersWithDistances(Visitor g)
            {
                if (EntertainerAvailable) yield return (new StaffMember(StaffKind.Entertainer) { Skill = WatchSkill }, 0);
            }
        }

        static Visitor Guest()
        {
            var v = Visitor.Spawn(new Dice(), 0);
            v.Happiness = 50; v.Nausea = 0; v.Tiredness = 0;
            v.NeedA = 0; v.NeedB = 0; v.Boredom = 0; v.RideDesire = 0;
            v.DecisionStagger = 0;
            return v;
        }

        // ⭐ IT RUNS FOR EVERY STATE, not just Idle. A guest queueing or walking to the exit is still
        // getting hungrier and still being depressed by litter. REJECTS folding this into Idle, which is
        // the obvious shortcut because Idle is where most guests are.
        [Theory]
        [InlineData(VisitorState.Idle)]
        [InlineData(VisitorState.WalkToBin)]
        [InlineData(VisitorState.Vomiting)]
        [InlineData(VisitorState.LeavingPark)]
        public void TheNeedsUpdateAppliesInEveryState(VisitorState state)
        {
            var g = Guest(); g.SetState(state);
            var world = new World { NowTick = VisitorNeeds.NeedAPeriod };
            VisitorNeeds.Tick(g, world, new Dice(1));
            Assert.Equal(1, g.NeedA);
        }

        // The needs climb on their own periods and only on those ticks.
        [Fact]
        public void TheTwoNeedsGrowOnTheirOwnSchedules()
        {
            var g = Guest();
            var world = new World();

            world.NowTick = 50;                                  // NeedA period
            VisitorNeeds.Tick(g, world, new Dice(1, 1));
            Assert.Equal(1, g.NeedA);
            Assert.Equal(0, g.NeedB);                            // 50 is not a multiple of 40

            var h = Guest();
            world.NowTick = 40;                                  // NeedB period
            VisitorNeeds.Tick(h, world, new Dice(1, 1));
            Assert.Equal(0, h.NeedA);
            Assert.Equal(1, h.NeedB);

            var k = Guest();
            world.NowTick = 51;                                  // neither
            VisitorNeeds.Tick(k, world, new Dice(1, 1));
            Assert.Equal(0, k.NeedA);
            Assert.Equal(0, k.NeedB);
        }

        // ⚠ THE UNPLEASANT INFLUENCE IS WORSE WHILE WALKING: 3 happiness and 5 nausea moving, 1 and 2
        // standing still. REJECTS a single figure for both, which is what you get by not reading the
        // clause about states 2 and 3.
        [Fact]
        public void StandingInSomethingUnpleasantIsCheaperThanWalkingThroughIt()
        {
            var standing = Guest(); standing.SetState(VisitorState.Idle);
            var walking = Guest(); walking.SetState(VisitorState.WalkToWaypoint);
            var world = new World { NowTick = 0, Influence = TileInfluence.Unpleasant };

            VisitorNeeds.Tick(standing, world, new Dice());
            VisitorNeeds.Tick(walking, world, new Dice());

            Assert.Equal(49, standing.Happiness);   // -1
            Assert.Equal(2, standing.Nausea);       // +2
            Assert.Equal(47, walking.Happiness);    // -3
            Assert.Equal(5, walking.Nausea);        // +5
        }

        [Fact]
        public void APleasantTileLiftsHappiness()
        {
            var g = Guest();
            VisitorNeeds.Tick(g, new World { NowTick = 0, Influence = TileInfluence.Pleasant }, new Dice());
            Assert.Equal(56, g.Happiness);
        }

        // ⚠ THE 8-TICK PASS IS STAGGERED AND THE OTHERS ARE NOT. Two guests on different phases take
        // their influence hit on different ticks, which is what stops a park re-evaluating in lockstep.
        [Fact]
        public void TheInfluencePassIsStaggeredPerGuest()
        {
            var a = Guest(); a.DecisionStagger = 0;
            var b = Guest(); b.DecisionStagger = 3;
            var world = new World { NowTick = 0, Influence = TileInfluence.Pleasant };

            VisitorNeeds.Tick(a, world, new Dice());
            VisitorNeeds.Tick(b, world, new Dice());
            Assert.Equal(56, a.Happiness);          // phase 0 fires on tick 0
            Assert.Equal(50, b.Happiness);          // phase 3 does not

            world.NowTick = 3;
            VisitorNeeds.Tick(b, world, new Dice());
            Assert.Equal(56, b.Happiness);
        }

        // ⭐ FIVE INDEPENDENT PENALTIES WITH FIVE DIFFERENT THRESHOLDS. A guest failing three of them
        // loses 3 happiness per pass, not 1. REJECTS one shared threshold and REJECTS an else-if chain.
        [Fact]
        public void EachUnmetNeedDocksHappinessSeparately()
        {
            var one = Guest(); one.Boredom = 95;
            var three = Guest(); three.Boredom = 95; three.Nausea = 85; three.NeedB = 85;
            var world = new World { NowTick = VisitorNeeds.LitterPeriod };

            VisitorNeeds.Tick(one, world, new Dice());
            VisitorNeeds.Tick(three, world, new Dice());

            Assert.Equal(49, one.Happiness);        // -1
            Assert.Equal(47, three.Happiness);      // -3, and nausea 85 is its own threshold
        }

        // The thresholds are not all the same number, which is exactly why they are worth pinning.
        [Theory]
        [InlineData(94, 0)] [InlineData(95, 1)]     // boredom
        public void TheBoredomThresholdIsNinetyFive(int boredom, int lost)
        {
            var g = Guest(); g.Boredom = boredom;
            VisitorNeeds.Tick(g, new World { NowTick = VisitorNeeds.LitterPeriod }, new Dice());
            Assert.Equal(50 - lost, g.Happiness);
        }

        [Theory]
        [InlineData(84, 0)] [InlineData(85, 1)]     // NeedB is 85, NOT 95 like NeedA
        public void TheNeedBThresholdIsEightyFive(int needB, int lost)
        {
            var g = Guest(); g.NeedB = needB;
            VisitorNeeds.Tick(g, new World { NowTick = VisitorNeeds.LitterPeriod }, new Dice());
            Assert.Equal(50 - lost, g.Happiness);
        }

        // Litter scales with how much of it there is, and only vomit adds nausea.
        [Fact]
        public void LitterDepressesAndOnlyVomitSickens()
        {
            var g = Guest();
            var world = new World { NowTick = VisitorNeeds.LitterPeriod, Litter = 3, Vomit = 1 };
            VisitorNeeds.Tick(g, world, new Dice());
            Assert.Equal(41, g.Happiness);          // 3 piles x -3
            Assert.Equal(3, g.Nausea);              // one of them was vomit

            var h = Guest();
            VisitorNeeds.Tick(h, new World { NowTick = VisitorNeeds.LitterPeriod, Litter = 2 }, new Dice());
            Assert.Equal(44, h.Happiness);
            Assert.Equal(0, h.Nausea);              // plain litter is not sickening
        }

        // ⭐ BUBBLE ORDER IS NOT THRESHOLD ORDER. Delighted (>90) is tested before happy (>80), so a
        // guest at 95 never shows the happy bubble. REJECTS sorting these by threshold, which looks like
        // a tidy-up and changes which bubble a delighted guest shows.
        [Theory]
        [InlineData(95, 0, 0, VisitorNeeds.BubbleDelighted)]
        [InlineData(85, 0, 0, VisitorNeeds.BubbleHappy)]
        [InlineData(5, 0, 0, VisitorNeeds.BubbleMiserable)]
        [InlineData(50, 95, 0, VisitorNeeds.BubbleNauseous)]     // nausea beats a merely ok mood
        [InlineData(95, 95, 0, VisitorNeeds.BubbleNauseous)]     // ...and beats delighted too
        [InlineData(50, 0, 95, VisitorNeeds.BubbleTired)]
        [InlineData(20, 0, 0, VisitorNeeds.BubbleNone)]          // too sad for content, not sad enough for miserable
        public void TheBubbleTakesTheFirstMatchInTheOriginalsOrder(int happiness, int nausea, int tired, int bubble)
        {
            var g = Guest(); g.Happiness = happiness; g.Nausea = nausea; g.Tiredness = tired;
            VisitorNeeds.Tick(g, new World { NowTick = VisitorNeeds.BubblePeriod }, new Dice(9));
            Assert.Equal(bubble, g.Bubble);
        }

        // Ride desire is tested before everything, and past 97 it also makes the guest hurry.
        [Fact]
        public void WantingARideBeatsEveryOtherBubbleAndSpeedsTheGuestUp()
        {
            var keen = Guest(); keen.RideDesire = 95; keen.Nausea = 100; keen.Happiness = 100;
            keen.WalkSpeed = 20;
            VisitorNeeds.Tick(keen, new World { NowTick = VisitorNeeds.BubblePeriod }, new Dice());
            Assert.Equal(VisitorNeeds.BubbleRideDesire, keen.Bubble);
            Assert.Equal(20, keen.WalkSpeed);                    // 95 is not past 97

            var desperate = Guest(); desperate.RideDesire = 98; desperate.WalkSpeed = 20;
            VisitorNeeds.Tick(desperate, new World { NowTick = VisitorNeeds.BubblePeriod }, new Dice());
            Assert.Equal(VisitorNeeds.HurryingSpeed, desperate.WalkSpeed);
        }

        [Fact]
        public void TheBubblePassIsSkippedWhileSuppressed()
        {
            var g = Guest(); g.Happiness = 95; g.Bubble = 0;
            var world = new World { NowTick = VisitorNeeds.BubblePeriod, IdleNeedsSuppressed = true };
            VisitorNeeds.Tick(g, world, new Dice());
            Assert.Equal(VisitorNeeds.BubbleNone, g.Bubble);
        }

        // ⭐ FOUR GATES ON STOPPING FOR A SHOW, and the stack-depth one is the interesting one: a guest
        // already two states deep will not stop, so an entertainer cannot interrupt a guest mid-errand.
        [Fact]
        public void AGuestStopsForAnEntertainerOnlyWhenAllFourGatesPass()
        {
            var world = new World { NowTick = 800, Influence = TileInfluence.Entertainer,
                                    EntertainerAvailable = true, WatchSkill = 1 };

            var free = Guest(); free.EntertainerNotBefore = 0;
            VisitorNeeds.Tick(free, world, new Dice());
            Assert.Equal(VisitorState.WatchEntertainer, free.State);
            Assert.Equal(1160, free.WaitUntil);                  // now + 360

            var queueing = Guest(); queueing.EntertainerNotBefore = 0;
            VisitorNeeds.Tick(queueing, new World { NowTick = 800, Influence = TileInfluence.Entertainer,
                                                    EntertainerAvailable = true, Queueing = true }, new Dice());
            Assert.Equal(VisitorState.Idle, queueing.State);

            var busy = Guest(); busy.EntertainerNotBefore = 0;
            busy.PushState(VisitorState.MajorDecision); busy.PushState(VisitorState.WalkToBin);
            VisitorNeeds.Tick(busy, world, new Dice());
            Assert.Equal(VisitorState.WalkToBin, busy.State);    // depth 2: will not stop

            var freshlyArrived = Guest(); freshlyArrived.EntertainerNotBefore = 5000;
            VisitorNeeds.Tick(freshlyArrived, world, new Dice());
            Assert.Equal(VisitorState.Idle, freshlyArrived.State);
        }
    }
}
