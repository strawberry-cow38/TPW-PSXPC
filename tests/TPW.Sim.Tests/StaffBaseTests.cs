using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>The staff base machine that every class falls through to (behaviour.md §3.1).</summary>
    public class StaffBaseTests
    {
        sealed class World : IStaffWorld
        {
            public long NowTick { get; set; }
            public bool OnStrike { get; set; }
            public bool PatrolRect { get; set; } = true;
            public bool PatrolPath { get; set; } = true;
            public bool Muster { get; set; } = true;
            public bool MusterPath { get; set; } = true;
            public bool RestPath { get; set; } = true;

            public bool IsTypeOnStrike(StaffKind k) => OnStrike;
            public bool HasPatrolRect(StaffMember s) => PatrolRect;
            public bool TryPathIntoPatrolArea(StaffMember s) => PatrolPath;
            public bool StrikeMusterExists => Muster;
            public bool TryPathToStrikeMuster(StaffMember s) => MusterPath;
            public bool TryPathToRest(StaffMember s) => RestPath;
        }

        static StaffMember Staff(int tiredness = 0, int morale = 100)
            => new(StaffKind.Mechanic) { Tiredness = tiredness, Morale = morale };

        // ⭐ IT IS AN ELSE-CHAIN. An exhausted member who is IDLE goes to rest; one who is not loses
        // morale instead. REJECTS writing the two conditions as independent ifs, which would have an
        // exhausted idler both rest AND lose morale -- in the one place staff actually recover.
        [Fact]
        public void AnExhaustedIdlerRestsInsteadOfLosingMorale()
        {
            var idler = Staff(tiredness: 95, morale: 50);
            idler.SetState(StaffState.Idle);
            StaffBase.IdleCheck(idler, new World { NowTick = 0 });     // tick 0 is a morale-loss tick
            Assert.Equal(StaffState.GoAndRest, idler.State);
            Assert.Equal(50, idler.Morale);                            // untouched

            var busy = Staff(tiredness: 95, morale: 50);
            busy.SetState(StaffState.Patrolling);
            StaffBase.IdleCheck(busy, new World { NowTick = 0 });
            Assert.Equal(StaffState.Patrolling, busy.State);           // does not go to rest
            Assert.Equal(49, busy.Morale);                             // loses morale instead
        }

        // The two thresholds are different numbers and both are exclusive.
        [Theory]
        [InlineData(80, StaffState.Idle)]        // not ABOVE 80
        [InlineData(81, StaffState.GoAndRest)]
        public void TheRestThresholdIsAboveEighty(int tiredness, StaffState expected)
        {
            var s = Staff(tiredness); s.SetState(StaffState.Idle);
            StaffBase.IdleCheck(s, new World { NowTick = 1 });
            Assert.Equal(expected, s.State);
        }

        [Theory]
        [InlineData(90, 50)]     // not above 90: no loss
        [InlineData(91, 49)]
        public void TheMoraleLossThresholdIsAboveNinety(int tiredness, int morale)
        {
            var s = Staff(tiredness, 50); s.SetState(StaffState.Patrolling);
            StaffBase.IdleCheck(s, new World { NowTick = 0 });
            Assert.Equal(morale, s.Morale);
        }

        // A strike outranks everything, including being on the point of collapse.
        [Fact]
        public void AStrikeOutranksExhaustion()
        {
            var s = Staff(tiredness: 100); s.SetState(StaffState.Idle); s.HasTarget = true;
            StaffBase.IdleCheck(s, new World { OnStrike = true });
            Assert.Equal(StaffState.WalkToStrike, s.State);
            Assert.False(s.HasTarget);                                  // whatever it was doing is dropped
        }

        // ⚠ NO RECTANGLE AND A REFUSED PATH BOTH LEAD TO WANDERING, not to standing still. A staff
        // member with nowhere assigned drifts around the park.
        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void PatrollingWithNowhereToGoWandersRatherThanStops(bool rect, bool path)
        {
            var s = Staff(); s.SetState(StaffState.Patrolling);
            StaffBase.Patrol(s, new World { PatrolRect = rect, PatrolPath = path });
            Assert.Equal(StaffState.RandomWander, s.State);
        }

        [Fact]
        public void APatrollingMemberWalksWithThePatrolPurpose()
        {
            var s = Staff(); s.SetState(StaffState.Patrolling);
            StaffBase.Patrol(s, new World());
            Assert.Equal(StaffState.Walking, s.State);
            Assert.Equal(StaffPurpose.Patrol, s.Purpose);
            Assert.Equal(0, s.StackDepth);                              // SET, not pushed
        }

        // ⚠ PUSH vs SET again: walking to the muster and to a bench PUSH, so the member returns; a
        // patrol leg SETS. Getting this wrong strands staff in the walking state.
        [Fact]
        public void WalkingToStrikeOrRestPushesSoTheyComeBack()
        {
            var striker = Staff(); striker.SetState(StaffState.WalkToStrike);
            StaffBase.WalkToStrike(striker, new World());
            Assert.Equal(StaffState.Walking, striker.State);
            Assert.Equal(1, striker.StackDepth);

            var tired = Staff(); tired.SetState(StaffState.GoAndRest);
            StaffBase.GoAndRest(tired, new World());
            Assert.Equal(StaffState.Walking, tired.State);
            Assert.Equal(1, tired.StackDepth);
            Assert.Equal(StaffPurpose.Rest, tired.Purpose);
        }

        // With nowhere to muster the strike simply starts on the spot.
        [Fact]
        public void WithNoMusterPointTheStrikeStartsWhereTheyStand()
        {
            var s = Staff(); s.SetState(StaffState.WalkToStrike);
            StaffBase.WalkToStrike(s, new World { Muster = false });
            Assert.Equal(StaffState.Striking, s.State);
        }

        // Nothing to sit on: back to work rather than standing about.
        [Fact]
        public void WithNoBenchTheyGoBackToPatrolling()
        {
            var s = Staff(); s.SetState(StaffState.GoAndRest);
            StaffBase.GoAndRest(s, new World { RestPath = false });
            Assert.Equal(StaffState.Patrolling, s.State);
        }

        // ⭐ RESTING IS ASYMMETRIC: tiredness falls twice as fast as morale climbs. A member worked into
        // the ground comes off the bench rested and still resentful, which is what makes strikes bite.
        // REJECTS equal rates, which would quietly defuse the whole strike system.
        [Fact]
        public void RestingShedsTirednessTwiceAsFastAsItRepairsMorale()
        {
            var s = Staff(tiredness: 50, morale: 50); s.SetState(StaffState.Resting);
            var world = new World { NowTick = 0 };
            StaffBase.Rest(s, world);
            Assert.Equal(48, s.Tiredness);          // -2
            Assert.Equal(51, s.Morale);             // +1

            world.NowTick = 1;                       // not a multiple of 4
            StaffBase.Rest(s, world);
            Assert.Equal(48, s.Tiredness);
            Assert.Equal(51, s.Morale);
        }

        [Fact]
        public void RestingEndsWhenTheTirednessIsGone()
        {
            var s = Staff(tiredness: 2, morale: 50); s.SetState(StaffState.Resting); s.HasTarget = true;
            StaffBase.Rest(s, new World { NowTick = 0 });
            Assert.Equal(0, s.Tiredness);
            Assert.Equal(StaffState.Patrolling, s.State);
            Assert.False(s.HasTarget);
        }

        // ⚠ STAFF TIRE ON A FIXED CADENCE, guests tire on a dice roll. Same-looking rule, two different
        // mechanisms; REJECTS reusing the guest's 1-in-10.
        [Fact]
        public void StaffTireEveryFourWalkingTicksNotRandomly()
        {
            var s = Staff();
            for (long t = 0; t < 8; t++) StaffBase.Arrive(s, new World { NowTick = t }, stillWalking: true);
            Assert.Equal(2, s.Tiredness);           // ticks 0 and 4 only
        }

        // Arrival routes by purpose, and the three destinations are all different.
        [Theory]
        [InlineData(StaffPurpose.Strike, StaffState.Striking)]
        [InlineData(StaffPurpose.Patrol, StaffState.Idle)]
        [InlineData(StaffPurpose.Rest, StaffState.Resting)]
        public void ArrivalRoutesByWhyTheyWereWalking(StaffPurpose purpose, StaffState expected)
        {
            var s = Staff(); s.Purpose = purpose;
            StaffBase.Arrive(s, new World(), stillWalking: false);
            Assert.Equal(expected, s.State);
        }

        // ⭐ A FAILED PATH IS HANDLED BY PURPOSE AND THE THREE ANSWERS DIFFER. REJECTS one blanket
        // "go Idle", which would leave striking staff milling about instead of giving up on the muster.
        [Theory]
        [InlineData(StaffPurpose.Strike, StaffState.Idle)]
        [InlineData(StaffPurpose.Patrol, StaffState.RandomWander)]
        [InlineData(StaffPurpose.Rest, StaffState.Patrolling)]
        [InlineData(StaffPurpose.None, StaffState.Patrolling)]
        public void AFailedPathIsAnsweredDifferentlyByPurpose(StaffPurpose purpose, StaffState expected)
        {
            var s = Staff(); s.Purpose = purpose;
            StaffBase.OnPathMessage(s, pathFound: false);
            Assert.Equal(expected, s.State);
        }

        [Fact]
        public void APathThatWasFoundPutsThemInTheReadyState()
        {
            var s = Staff(); s.Purpose = StaffPurpose.Patrol;
            StaffBase.OnPathMessage(s, pathFound: true);
            Assert.Equal(StaffState.PathReady, s.State);
        }

        // Striking ends only when the strike is called off.
        [Fact]
        public void StrikersStandUntilTheStrikeIsOver()
        {
            var s = Staff(); s.SetState(StaffState.Striking);
            StaffBase.Strike(s, new World { OnStrike = true });
            Assert.Equal(StaffState.Striking, s.State);
            StaffBase.Strike(s, new World { OnStrike = false });
            Assert.Equal(StaffState.Idle, s.State);
        }

        // Staff stats clamp like the visitor's.
        [Fact]
        public void StaffStatsClamp()
        {
            var s = Staff();
            s.Tiredness = 500; Assert.Equal(100, s.Tiredness);
            s.Morale = -10; Assert.Equal(0, s.Morale);
        }
    }
}
