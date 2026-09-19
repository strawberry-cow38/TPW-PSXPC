using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>State 0, Idle. Worked from behaviour.md §2.1 and the constants table §2.12.</summary>
    public class VisitorIdleTests
    {
        static Money P(long pounds) => Money.FromPounds(pounds);

        /// <summary>Returns queued numbers in order and RECORDS the bound of every call, so a test can
        /// assert which dice were rolled as well as what they said. The bounds are the evidence for roll
        /// ORDER, which no assertion on the outcome alone can see.</summary>
        sealed class ScriptedRandom : IRandomSource
        {
            readonly Queue<int> _values;
            public ScriptedRandom(params int[] values) => _values = new Queue<int>(values);
            public List<int> Bounds { get; } = new();
            public int Next(int n)
            {
                Bounds.Add(n);
                return _values.Count > 0 ? _values.Dequeue() : 0;
            }
        }

        sealed class FakeWorld : IVisitorWorld
        {
            public long NowTick { get; set; }
            public int SlowClockDay { get; set; }
            public bool IdleNeedsSuppressed { get; set; }
            public BinSearch BinResult { get; set; } = BinSearch.NoBinInRange;
            public bool EntertainerInRange { get; set; }
            public int LitterDropped { get; private set; }

            public BinSearch TryWalkToBin(Visitor g) => BinResult;
            public bool TryPeltEntertainer(Visitor g) => EntertainerInRange;
            public void DropLitter(Visitor g) => LitterDropped++;
        }

        /// <summary>A guest with nothing wrong with it: comfortably above every leave threshold.</summary>
        static Visitor Healthy()
        {
            var v = Visitor.Spawn(new ScriptedRandom(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), nowTick: 0);
            v.Money = P(300); v.Happiness = 50; v.Tiredness = 10; v.Nausea = 10; v.Rubbish = 0;
            return v;
        }

        // ⭐ ANY ONE OF THE FOUR IS ENOUGH, and each is tested with the other three healthy so a single
        // over-broad condition cannot carry the whole test. REJECTS && where the original has ||.
        [Fact]
        public void EachLeaveConditionIsIndependentlySufficient()
        {
            var world = new FakeWorld();

            var tired = Healthy(); tired.Tiredness = 99;
            Assert.Equal(IdleAction.Leave, VisitorIdle.Tick(tired, world, new ScriptedRandom(0)));

            var sad = Healthy(); sad.Happiness = 4;
            Assert.Equal(IdleAction.Leave, VisitorIdle.Tick(sad, world, new ScriptedRandom(0)));

            var broke = Healthy(); broke.Money = P(9);
            Assert.Equal(IdleAction.Leave, VisitorIdle.Tick(broke, world, new ScriptedRandom(0)));

            // The fourth needs both its clauses: long enough in the park AND the 2-in-20 roll.
            var bored = Healthy(); bored.ArrivedOnDay = 0; world.SlowClockDay = 81;
            Assert.Equal(IdleAction.Leave, VisitorIdle.Tick(bored, world, new ScriptedRandom(1)));
        }

        // The boundaries, each side. REJECTS > for >= on tiredness and <= for < on happiness and money.
        [Theory]
        [InlineData(98, 5, 10, false)]
        [InlineData(99, 5, 10, true)]    // tiredness is >= 99
        [InlineData(10, 5, 10, false)]   // happiness 5 is NOT below 5
        [InlineData(10, 4, 10, true)]
        [InlineData(10, 50, 10, false)]  // exactly £10 stays
        [InlineData(10, 50, 9, true)]
        public void TheLeaveThresholdsAreExact(int tired, int happy, long pounds, bool leaves)
        {
            var v = Healthy(); v.Tiredness = tired; v.Happiness = happy; v.Money = P(pounds);
            var action = VisitorIdle.Tick(v, new FakeWorld(), new ScriptedRandom(0));
            Assert.Equal(leaves, action == IdleAction.Leave);
        }

        // ⚠ DICE ORDER. The departure roll is only taken when the time condition already holds; taking
        // it first would consume a number the original never draws and shift every later roll. Asserted
        // on the BOUNDS the source was asked for, because the outcome alone cannot show it.
        [Fact]
        public void TheDepartureDiceAreOnlyRolledWhenTheGuestHasStayedLongEnough()
        {
            var world = new FakeWorld { SlowClockDay = 0 };          // nowhere near 81 days
            var rng = new ScriptedRandom(1);                          // would be a "leave" if consumed
            VisitorIdle.Tick(Healthy(), world, rng);
            Assert.Equal(new[] { 6 }, rng.Bounds);                    // straight to the rand(6) selector

            var world2 = new FakeWorld { SlowClockDay = 81 };
            // roll 5 on a cheerful guest short-circuits before drawing again, so the recorded bounds are
            // exactly the two dice under test. (Roll 0 would legitimately draw a third for its cooldown,
            // which is what an earlier version of this assertion mistook for a bug in the code.)
            var rng2 = new ScriptedRandom(19, 5);                     // 19 is not < 2, so it stays
            VisitorIdle.Tick(Healthy(), world2, rng2);
            Assert.Equal(new[] { 20, 6 }, rng2.Bounds);               // rand(20) first, THEN rand(6)
        }

        // The leave check runs before the roll, so a doomed guest leaves whatever the dice say.
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void AGuestThatShouldLeaveLeavesWhateverTheRollWouldHaveBeen(int roll)
        {
            var v = Healthy(); v.Happiness = 0;
            Assert.Equal(IdleAction.Leave, VisitorIdle.Tick(v, new FakeWorld(), new ScriptedRandom(roll)));
            Assert.Equal(VisitorState.LeavingPark, v.State);
        }

        // The "left in a huff" cue is below 3, not below the leave threshold of 5 -- a guest leaving at
        // happiness 4 goes quietly. REJECTS reusing LeaveHappiness for the bubble.
        [Theory]
        [InlineData(2, VisitorIdle.BubbleLeftUnhappy)]
        [InlineData(4, 0)]
        public void OnlyAThoroughlyMiserableGuestShowsTheLeavingBubble(int happiness, int bubble)
        {
            var v = Healthy(); v.Happiness = happiness;
            VisitorIdle.Tick(v, new FakeWorld(), new ScriptedRandom(0));
            Assert.Equal(bubble, v.Bubble);
        }

        // ⭐ PUSH VERSUS SET, which is the difference between "go do this and come back" and "stop being
        // idle". Rolls 0 and 1 push; the vomit branch and the leave check set, which also CLEARS the
        // stack. REJECTS implementing both as an assignment, which loses the return path.
        [Fact]
        public void WanderAndDecisionPushWhileVomitAndLeavingReplace()
        {
            var world = new FakeWorld { NowTick = 100000 };           // cooldown long expired

            var wanderer = Healthy();
            VisitorIdle.Tick(wanderer, world, new ScriptedRandom(1));
            Assert.Equal(VisitorState.Wander, wanderer.State);
            Assert.Equal(1, wanderer.StackDepth);
            wanderer.PopState();
            Assert.Equal(VisitorState.Idle, wanderer.State);          // it comes back

            var decider = Healthy();
            VisitorIdle.Tick(decider, world, new ScriptedRandom(0, 0));
            Assert.Equal(VisitorState.MajorDecision, decider.State);
            Assert.Equal(1, decider.StackDepth);

            var sick = Healthy(); sick.Nausea = 100;
            VisitorIdle.Tick(sick, world, new ScriptedRandom(4));
            Assert.Equal(VisitorState.Vomiting, sick.State);
            Assert.Equal(0, sick.StackDepth);                          // no way back
        }

        // ⚠ OR, NOT AND. A guest with a settled stomach is still sick one roll in four.
        [Theory]
        [InlineData(100, 3, true)]   // nauseous: the rand(4) is not even reached
        [InlineData(10, 0, true)]    // calm, but the 1-in-4 landed
        [InlineData(10, 1, false)]
        public void SicknessNeedsEitherNauseaOrTheOneInFour(int nausea, int roll, bool vomits)
        {
            var v = Healthy(); v.Nausea = nausea;
            var world = new FakeWorld { NowTick = 500 };
            var action = VisitorIdle.Tick(v, world, new ScriptedRandom(4, roll));
            Assert.Equal(vomits, action == IdleAction.Vomit);
            if (vomits) Assert.Equal(560, v.WaitUntil);                // now + 60
        }

        // ⭐ THREE OUTCOMES, NOT TWO. No bin and a refused path look the same from outside and behave
        // oppositely: one litters and empties the guest's hands, the other keeps the rubbish.
        [Fact]
        public void TheBinSearchHasThreeDistinctOutcomes()
        {
            var full = new Visitor[3];
            for (int i = 0; i < 3; i++) { full[i] = Healthy(); full[i].Rubbish = 95; }

            var walking = new FakeWorld { BinResult = BinSearch.WalkingToBin };
            Assert.Equal(IdleAction.WalkToBin, VisitorIdle.Tick(full[0], walking, new ScriptedRandom(3)));
            Assert.Equal(VisitorState.WalkToBin, full[0].State);
            Assert.Equal(95, full[0].Rubbish);                         // still carrying it
            Assert.Equal(0, walking.LitterDropped);

            var refused = new FakeWorld { BinResult = BinSearch.PathRefused };
            Assert.Equal(IdleAction.Nothing, VisitorIdle.Tick(full[1], refused, new ScriptedRandom(3)));
            Assert.Equal(VisitorState.Idle, full[1].State);
            Assert.Equal(95, full[1].Rubbish);                         // and it keeps it
            Assert.Equal(0, refused.LitterDropped);

            var none = new FakeWorld { BinResult = BinSearch.NoBinInRange };
            Assert.Equal(IdleAction.DropLitterBecauseNoBin, VisitorIdle.Tick(full[2], none, new ScriptedRandom(3)));
            Assert.Equal(0, full[2].Rubbish);                          // hands emptied
            Assert.Equal(1, none.LitterDropped);
        }

        // A guest that is not carrying much does not go looking for a bin at all.
        [Fact]
        public void AGuestBelowTheRubbishThresholdIgnoresBins()
        {
            var v = Healthy(); v.Rubbish = 89;
            var world = new FakeWorld { BinResult = BinSearch.WalkingToBin };
            Assert.Equal(IdleAction.Nothing, VisitorIdle.Tick(v, world, new ScriptedRandom(3)));
            Assert.Equal(VisitorState.Idle, v.State);
        }

        // ⚠ THE TWO LITTER PATHS ARE DIFFERENT ACTS. Dropping it for want of a bin empties the guest's
        // hands; dropping it out of misery does not. REJECTS sharing one code path between them.
        [Fact]
        public void MiseryLitterDoesNotEmptyTheGuestsHands()
        {
            var v = Healthy(); v.Happiness = 10; v.Rubbish = 50;
            var world = new FakeWorld();
            Assert.Equal(IdleAction.DropLitterFromMisery, VisitorIdle.Tick(v, world, new ScriptedRandom(5, 50)));
            Assert.Equal(50, v.Rubbish);
            Assert.Equal(1, world.LitterDropped);
        }

        // Both litter clauses must hold, and the suppression flag stops litter everywhere.
        [Theory]
        [InlineData(10, 50, false, IdleAction.DropLitterFromMisery)]
        [InlineData(30, 50, false, IdleAction.Nothing)]    // too cheerful
        [InlineData(10, 999, false, IdleAction.Nothing)]   // roll missed
        [InlineData(10, 50, true, IdleAction.Nothing)]     // suppressed
        public void MiseryLitterNeedsBothClauses(int happiness, int roll, bool suppressed, IdleAction expected)
        {
            var v = Healthy(); v.Happiness = happiness;
            var world = new FakeWorld { IdleNeedsSuppressed = suppressed };
            Assert.Equal(expected, VisitorIdle.Tick(v, world, new ScriptedRandom(5, roll)));
        }

        // The cooldown keeps an idle guest idle; once it expires the guest goes and decides something.
        [Fact]
        public void TheDecisionCooldownHoldsTheGuestStill()
        {
            var v = Healthy(); v.WaitUntil = 1000; v.HasTarget = false;
            var early = new FakeWorld { NowTick = 1000 };               // 1000 + 60 + 0 > 1000
            Assert.Equal(IdleAction.Nothing, VisitorIdle.Tick(v, early, new ScriptedRandom(0, 0)));
            Assert.Equal(VisitorState.Idle, v.State);

            var late = new FakeWorld { NowTick = 1061 };                // past 1000 + 60 + 0
            Assert.Equal(IdleAction.MakeMajorDecision, VisitorIdle.Tick(v, late, new ScriptedRandom(0, 0)));
        }

        // A guest already heading somewhere does not wait out the cooldown.
        [Fact]
        public void AGuestWithSomethingInMindSkipsTheCooldown()
        {
            var v = Healthy(); v.WaitUntil = 100000; v.HasTarget = true;
            var world = new FakeWorld { NowTick = 0 };
            Assert.Equal(IdleAction.MakeMajorDecision, VisitorIdle.Tick(v, world, new ScriptedRandom(0, 0)));
        }

        // Needs bubbles, first match wins, and ride desire only speaks when neither need does.
        [Theory]
        [InlineData(95, 0, 0, VisitorIdle.BubbleNeed)]
        [InlineData(0, 95, 0, VisitorIdle.BubbleNeed)]
        [InlineData(0, 0, 95, VisitorIdle.BubbleRideDesire)]
        [InlineData(95, 0, 95, VisitorIdle.BubbleNeed)]     // need wins over desire
        [InlineData(0, 0, 0, 0)]
        public void TheNeedsBubbleTakesTheFirstMatch(int needA, int needB, int desire, int bubble)
        {
            var v = Healthy();
            v.NeedA = needA; v.NeedB = needB; v.RideDesire = desire; v.HasTarget = true;
            VisitorIdle.Tick(v, new FakeWorld { NowTick = 0 }, new ScriptedRandom(0, 0));
            Assert.Equal(bubble, v.Bubble);
        }

        // Stats clamp rather than wrap -- three litter piles cannot make a sad guest happy.
        [Fact]
        public void StatsClampAtBothEnds()
        {
            Assert.Equal(0, Stat.Sub(2, 3));
            Assert.Equal(100, Stat.Add(98, 5));
            Assert.Equal(0, Stat.Clamp(-40));
            Assert.Equal(100, Stat.Clamp(240));
        }

        // The two product-of-two-rolls stats are not uniform, and the spawn reads its rolls in the
        // constructor's order. Pinned so a "simplification" to one rand(100) is caught.
        [Fact]
        public void SpawnUsesTheConstructorsOwnRolls()
        {
            //      money  rubbish  nausea  needA  boredom  desireA desireB  needBa needBb  tired  speed
            var rng = new ScriptedRandom(100, 10, 20, 30, 15, 50, 50, 80, 50, 40, 5);
            var v = Visitor.Spawn(rng, nowTick: 0);

            Assert.Equal(P(300), v.Money);          // 200 + 100
            Assert.Equal(10, v.Rubbish);
            Assert.Equal(50, v.Happiness);          // always 50, never rolled
            Assert.Equal(20, v.Nausea);
            Assert.Equal(30, v.NeedA);
            Assert.Equal(15, v.Boredom);
            Assert.Equal(25, v.RideDesire);         // 50*50/100, not 50
            Assert.Equal(40, v.NeedB);              // 80*50/100
            Assert.Equal(40, v.Tiredness);
            Assert.Equal(20, v.WalkSpeed);          // rand(15)+15
        }
    }
}
