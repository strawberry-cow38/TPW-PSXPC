using System;
using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>The ride cycle: the animation clock at 0x800658D8 and the status-2 tick that counts
    /// phases off it (rides.md §4.3, re-read from TPW.BIN 2026-09-20).</summary>
    public class RideCycleTests
    {
        // MUTATION RECORD (2026-09-20): 15 breakages, 14 caught. The survivor is equivalent, not a gap:
        // widening the clamp from `d > MaxDelta` to `d >= MaxDelta` only changes what happens at
        // exactly MaxDelta, where both spellings assign MaxDelta and nothing observable differs. Noted
        // rather than chased, and the boundary is still pinned below so a clamp at the WRONG value dies.

        sealed class Anim : IRideAnimation
        {
            public int CurrentPhase { get; set; }
            public int BuildAnimationLength { get; set; } = 30;
            public List<int> Lengths { get; set; } = new() { 40, 60, 80 };
            public List<int> Asked { get; } = new();
            public int PhaseLength(int phase)
            {
                Asked.Add(phase);
                return Lengths[phase % Lengths.Count];   // the index WRAPS (0x800300E4)
            }
        }

        // ⭐⭐ A RIDE'S RUN LENGTH IS ITS ANIMATION. The phase target comes from the model, so a ride
        // with a longer animation runs longer for the same slider. REJECTS a fixed per-tick duration,
        // which is the obvious way to write "run for N cycles" and makes every ride the same length.
        [Fact]
        public void ThePhaseTargetComesFromTheModelAndNotFromAConstant()
        {
            var a = new Anim { CurrentPhase = 0, Lengths = new() { 40 } };
            Assert.Equal((40 - 1) << 12, RideCycle.TargetFor(AttractionStatus.Running, a));

            var b = new Anim { CurrentPhase = 0, Lengths = new() { 100 } };
            Assert.Equal((100 - 1) << 12, RideCycle.TargetFor(AttractionStatus.Running, b));
        }

        // ⚠ THE MINUS ONE IS ON THE RUNNING PATH ONLY. It sits in the delay slot of the branch that
        // skips the construction case, so construction races the raw length. REJECTS applying it to
        // both, which is one instruction's worth of difference and reads as an obvious tidy-up.
        [Fact]
        public void ConstructionRacesTheRawLengthAndARunRacesOneLess()
        {
            var a = new Anim { BuildAnimationLength = 30, CurrentPhase = 0, Lengths = new() { 30 } };

            Assert.Equal(30 << 12, RideCycle.TargetFor(AttractionStatus.UnderConstruction, a));
            Assert.Equal(29 << 12, RideCycle.TargetFor(AttractionStatus.Running, a));
        }

        // ⚠ CONSTRUCTION READS A DIFFERENT RESOURCE ENTIRELY and never touches the phase table.
        // REJECTS reusing the running phases for the build, which would make a ride's construction as
        // long as one cycle of it.
        [Fact]
        public void ConstructionNeverAsksThePhaseTable()
        {
            var a = new Anim { BuildAnimationLength = 12 };
            RideCycle.TargetFor(AttractionStatus.UnderConstruction, a);
            Assert.Empty(a.Asked);

            RideCycle.TargetFor(AttractionStatus.Running, a);
            Assert.Single(a.Asked);
        }

        // ⚠ THE PHASE INDEX WRAPS rather than running off the end of the table.
        [Theory]
        [InlineData(0, 40)]
        [InlineData(1, 60)]
        [InlineData(2, 80)]
        [InlineData(3, 40)]
        [InlineData(7, 60)]
        public void APhasePastTheEndOfTheTableRestarts(int phase, int length)
        {
            var a = new Anim { CurrentPhase = phase };
            Assert.Equal((length - 1) << 12, RideCycle.TargetFor(AttractionStatus.Running, a));
        }

        // A handle with no phase reads as phase 0 rather than indexing backwards.
        [Fact]
        public void ANegativePhaseIsReadAsTheFirst()
        {
            var a = new Anim { CurrentPhase = -1 };
            Assert.Equal((40 - 1) << 12, RideCycle.TargetFor(AttractionStatus.Running, a));
            Assert.Equal(new[] { 0 }, a.Asked.ToArray());
        }

        // The accumulator races the target and resets on arrival, not on overshoot.
        [Fact]
        public void APhaseEndsWhenTheAccumulatorReachesTheTargetAndThenStartsAgain()
        {
            var a = new Anim { CurrentPhase = 0, Lengths = new() { 5 } };   // target (5-1)<<12 = 0x4000
            var c = new RideCycle();

            Assert.False(c.Advance(AttractionStatus.Running, a, 0x2000, false));
            Assert.Equal(0x2000, c.Accumulator);                                   // part way

            Assert.True(c.Advance(AttractionStatus.Running, a, 0x2000, false));    // 0x4000 >= 0x4000
            Assert.Equal(0, c.Accumulator);                                        // and starts over

            // Arrival is enough; it does not have to overshoot.
            var exact = new RideCycle();
            Assert.True(exact.Advance(AttractionStatus.Running, a, 0x4000, false));
        }

        // ⚠ THE CAP IS THE POINT OF A WALL-CLOCK ANIMATION. A stalled frame loses animation time
        // instead of fast-forwarding a whole phase. REJECTS adding the raw delta, which on a long
        // hitch would complete several phases at once and cut the ride short.
        [Fact]
        public void AnEnormousDeltaIsCappedRatherThanSkippingAhead()
        {
            var a = new Anim { CurrentPhase = 0, Lengths = new() { 100 } };
            var c = new RideCycle();

            c.Advance(AttractionStatus.Running, a, 0x7FFFFFF, false);
            Assert.Equal(RideCycle.MaxDelta, c.Accumulator);
            Assert.Equal(0x4000, RideCycle.MaxDelta);
        }

        // Exactly the cap is not capped; one more is.
        [Theory]
        [InlineData(0x4000, 0x4000)]
        [InlineData(0x4001, 0x4000)]
        [InlineData(0x3FFF, 0x3FFF)]
        public void TheCapIsOnTheValueAboveIt(int delta, int gained)
        {
            var a = new Anim { CurrentPhase = 0, Lengths = new() { 1000 } };
            var c = new RideCycle();
            c.Advance(AttractionStatus.Running, a, delta, false);
            Assert.Equal(gained, c.Accumulator);
        }

        // Half speed halves the delta BEFORE the cap, so it can admit a delta the cap would have cut.
        [Fact]
        public void HalfSpeedHalvesBeforeTheCapAndNotAfter()
        {
            var a = new Anim { CurrentPhase = 0, Lengths = new() { 1000 } };

            var slow = new RideCycle();
            slow.Advance(AttractionStatus.Running, a, 0x6000, true);
            Assert.Equal(0x3000, slow.Accumulator);       // halved to 0x3000, under the cap

            var normal = new RideCycle();
            normal.Advance(AttractionStatus.Running, a, 0x6000, false);
            Assert.Equal(0x4000, normal.Accumulator);     // capped
        }

        // ⚠⚠ A NEGATIVE DELTA WINDS THE CLOCK BACKWARDS AND THEN COMPLETES THE PHASE INSTANTLY. The
        // clamp is signed so it never catches one, and the completion test is UNSIGNED so a negative
        // accumulator reads as enormous. Reproduced deliberately: it is what the hardware does when the
        // root counter wraps, and a guard here would hide the wrap rather than fix it.
        // REJECTS "tidying" either comparison to match the other.
        [Fact]
        public void ANegativeDeltaIsNotCaughtAndFinishesThePhaseAtOnce()
        {
            var a = new Anim { CurrentPhase = 0, Lengths = new() { 1000 } };
            var c = new RideCycle();

            Assert.True(c.Advance(AttractionStatus.Running, a, -1, false));
            Assert.Equal(0, c.Accumulator);
        }

        // ⭐ THE RUN ENDS ON PHASES, NOT TICKS, and the count is incremented before it is tested - so a
        // one-cycle ride runs exactly one phase. REJECTS testing before counting, which gives every
        // ride one phase more than the slider says.
        [Fact]
        public void AOneCycleRideRunsExactlyOnePhase()
        {
            var a = new Anim { CurrentPhase = 0, Lengths = new() { 2 } };   // target (2-1)<<12 = 4096
            var c = new RideCycle();
            c.StartRun();

            Assert.True(c.RunTick(a, cyclesPerLoad: 1, rawDelta: 4096, halfSpeed: false));
            Assert.Equal(1, c.CyclesRun);
        }

        [Fact]
        public void AFiveCycleRideTakesFivePhases()
        {
            var a = new Anim { CurrentPhase = 0, Lengths = new() { 2 } };
            var c = new RideCycle();
            c.StartRun();

            for (int i = 1; i <= 4; i++)
            {
                Assert.False(c.RunTick(a, 5, 4096, false));
                Assert.Equal(i, c.CyclesRun);
            }
            Assert.True(c.RunTick(a, 5, 4096, false));
            Assert.Equal(5, c.CyclesRun);
        }

        // A tick that does not complete a phase does not count one.
        [Fact]
        public void TicksInsideAPhaseDoNotCount()
        {
            var a = new Anim { CurrentPhase = 0, Lengths = new() { 100 } };
            var c = new RideCycle();
            c.StartRun();

            for (int i = 0; i < 5; i++) Assert.False(c.RunTick(a, 5, 0x1000, false));
            Assert.Equal(0, c.CyclesRun);
        }

        // Starting a run clears both, or a reopened ride unloads on its first phase.
        [Fact]
        public void StartingARunClearsTheCountAndThePartPhase()
        {
            var c = new RideCycle { CyclesRun = 99, Accumulator = 1234 };
            c.StartRun();
            Assert.Equal(0, c.CyclesRun);
            Assert.Equal(0, c.Accumulator);
        }
    }
}
