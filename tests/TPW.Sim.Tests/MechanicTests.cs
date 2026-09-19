using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>The mechanic (behaviour.md §3.5).</summary>
    public class MechanicTests
    {
        sealed class Dice : IRandomSource
        {
            readonly Queue<int> _v;
            public Dice(params int[] v) => _v = new Queue<int>(v);
            public int Next(int n) => _v.Count > 0 ? _v.Dequeue() : 0;
        }

        sealed class World : IMechanicWorld
        {
            public long NowTick { get; set; }
            public bool OnStrike { get; set; }
            public bool Broken { get; set; }
            public bool ServiceDue { get; set; }
            public bool Clear { get; set; } = true;
            public bool LeavePath { get; set; } = true;
            public int Unloads { get; private set; }
            public int Reloads { get; private set; }
            public int Released { get; private set; }
            public int Serviced { get; private set; }
            public int Reopened { get; private set; }

            public bool IsTypeOnStrike(StaffKind k) => OnStrike;
            public bool HasPatrolRect(StaffMember s) => true;
            public bool TryPathIntoPatrolArea(StaffMember s) => true;
            public bool StrikeMusterExists => true;
            public bool TryPathToStrikeMuster(StaffMember s) => true;
            public bool TryPathToRest(StaffMember s) => true;

            public bool TryClaimBrokenRide(StaffMember s) => Broken;
            public bool TryClaimServiceRide(StaffMember s) => ServiceDue;
            public bool RideIsClear(StaffMember s) => Clear;
            public void UnloadRide(StaffMember s) => Unloads++;
            public void ReloadRide(StaffMember s) => Reloads++;
            public void MarkRideUnderRepair(StaffMember s) { }
            public void MarkRideOpenAndRelease(StaffMember s) => Reopened++;
            public void CompleteService(StaffMember s) => Serviced++;
            public void ReleaseClaim(StaffMember s) => Released++;
            public bool TryPathToLeavePoint(StaffMember s) => LeavePath;
        }

        static StaffMember Mech(int skill = 0, int tiredness = 50, int morale = 50)
            => new(StaffKind.Mechanic) { Skill = skill, Tiredness = tiredness, Morale = morale };

        // ⭐ THE TABLE IS PAIRS OF s16, NOT A FLAT ARRAY, and getting that wrong is quiet. Read as flat
        // s32 it is 590064/786612/917624 - obvious nonsense. Read as flat s16 it is 240, 9, 180, 12:
        // the FIRST value is right and the second is plausible, which is the reading that would have
        // survived. The second column is real and unidentified.
        [Fact]
        public void TheRepairTableIsTwoColumnsAndBothAreRecorded()
        {
            Assert.Equal(new[] { 240, 180, 120, 60, 60 }, Mechanic.RepairTicks);
            Assert.Equal(new[] { 9, 12, 14, 16, 18 }, Mechanic.UnknownSecondColumn);

            // The two columns move in opposite directions, which is why the second is presumably a
            // quality rather than another duration.
            for (int i = 1; i < 5; i++)
            {
                Assert.True(Mechanic.RepairTicks[i] <= Mechanic.RepairTicks[i - 1]);
                Assert.True(Mechanic.UnknownSecondColumn[i] > Mechanic.UnknownSecondColumn[i - 1]);
            }
        }

        // ⭐⭐ TIRED BY IDLENESS, RESTED BY WORK. Both signs are READ and they only make sense together.
        // REJECTS "correcting" either one: an idle tick is +6 tiredness, and walking to a repair is -2.
        [Fact]
        public void AMechanicIsWornDownByStandingAboutAndRestedByWalkingToAJob()
        {
            var idle = Mech(tiredness: 50, morale: 50);
            Mechanic.Idle(idle, new World(), new Dice(0));
            Assert.Equal(56, idle.Tiredness);          // +6 for doing nothing
            Assert.Equal(51, idle.Morale);             // +1

            var walking = Mech(tiredness: 50, morale: 50);
            walking.Purpose = MechanicStates.ToRepair;
            Mechanic.WalkTick(walking);
            Assert.Equal(48, walking.Tiredness);       // -2: work is restful
            Assert.Equal(48, walking.Morale);          // -2: and resented
        }

        // The walk cost applies to a REPAIR walk only, not to a service walk.
        [Fact]
        public void OnlyTheRepairWalkHasThatCost()
        {
            var s = Mech(tiredness: 50, morale: 50);
            s.Purpose = MechanicStates.ToService;
            Mechanic.WalkTick(s);
            Assert.Equal(50, s.Tiredness);
            Assert.Equal(50, s.Morale);
        }

        // ⚠ BOTH ORDERS TRY BOTH JOBS, unlike the handyman whose litter branch does not fall back. The
        // coin flip only picks which is looked at first, so a broken ride is never skipped.
        [Theory]
        [InlineData(0)] [InlineData(1)]
        public void EitherCoinFlipStillFindsTheOnlyJobGoing(int flip)
        {
            var onlyBroken = Mech();
            Mechanic.Idle(onlyBroken, new World { Broken = true }, new Dice(flip));
            Assert.Equal(MechanicStates.GoToBrokenRide, onlyBroken.State);

            var onlyService = Mech();
            Mechanic.Idle(onlyService, new World { ServiceDue = true }, new Dice(flip));
            Assert.Equal(MechanicStates.GoToServiceRide, onlyService.State);
        }

        [Fact]
        public void TheCoinFlipDecidesWhichJobWinsWhenBothAreWaiting()
        {
            var a = Mech();
            Mechanic.Idle(a, new World { Broken = true, ServiceDue = true }, new Dice(0));
            Assert.Equal(MechanicStates.GoToBrokenRide, a.State);

            var b = Mech();
            Mechanic.Idle(b, new World { Broken = true, ServiceDue = true }, new Dice(1));
            Assert.Equal(MechanicStates.GoToServiceRide, b.State);
        }

        [Fact]
        public void WithNothingBrokenHePatrols()
        {
            var s = Mech();
            Mechanic.Idle(s, new World(), new Dice(0));
            Assert.Equal(StaffState.Patrolling, s.State);
        }

        // ⭐ THE RIDE MUST EMPTY BEFORE THE CLOCK STARTS, and he keeps unloading it meanwhile. A busy
        // ride delays its own repair. REJECTS starting the timer on arrival.
        [Fact]
        public void TheRepairClockDoesNotStartUntilTheRideIsEmpty()
        {
            var s = Mech(skill: 0);
            var world = new World { NowTick = 1000, Clear = false };

            Assert.False(Mechanic.CloseRide(s, world, forService: false));
            Assert.Equal(1, world.Unloads);
            Assert.Equal(0, s.BusyUntil);

            world.Clear = true;
            Assert.True(Mechanic.CloseRide(s, world, forService: false));
            Assert.Equal(1240, s.BusyUntil);                  // now + 240 at skill 0
            Assert.Equal(MechanicStates.Repairing, s.State);
        }

        [Theory]
        [InlineData(0, 240)]
        [InlineData(4, 60)]
        public void TheJobLengthComesFromTheSkillTable(int skill, int ticks)
        {
            var s = Mech(skill);
            Mechanic.CloseRide(s, new World { NowTick = 0 }, forService: false);
            Assert.Equal(ticks, s.BusyUntil);
        }

        [Fact]
        public void TheWorkTakesItsFullTime()
        {
            var s = Mech(); s.BusyUntil = 500;
            var world = new World { NowTick = 500 };
            Assert.False(Mechanic.Work(s, world, forService: false));
            world.NowTick = 501;
            Assert.True(Mechanic.Work(s, world, forService: false));
            Assert.Equal(MechanicStates.OpeningRide, s.State);
        }

        // Only a SERVICE books the service against the ride; a repair does not.
        [Fact]
        public void OnlyAServiceIsBookedAgainstTheRide()
        {
            var repair = Mech(); repair.BusyUntil = 0;
            var w1 = new World { NowTick = 1 };
            Mechanic.Work(repair, w1, forService: false);
            Assert.Equal(0, w1.Serviced);

            var service = Mech(); service.BusyUntil = 0;
            var w2 = new World { NowTick = 1 };
            Mechanic.Work(service, w2, forService: true);
            Assert.Equal(1, w2.Serviced);
        }

        // ⭐ THE +10 IS PAID AT REOPENING, NOT AT FINISHING THE REPAIR. A mechanic pulled off a job
        // before the ride reopens never gets paid for it. REJECTS paying it in Work().
        [Fact]
        public void TheMoraleRewardIsPaidForReopeningNotForRepairing()
        {
            var s = Mech(morale: 50); s.BusyUntil = 0;
            Mechanic.Work(s, new World { NowTick = 1 }, forService: false);
            Assert.Equal(50, s.Morale);                        // finishing the repair pays nothing

            Assert.True(Mechanic.OpenRide(s, new World { NowTick = 2 }));
            Assert.Equal(60, s.Morale);                        // reopening pays 10
            Assert.Equal(MechanicStates.LeavingRide, s.State);
        }

        [Fact]
        public void ReopeningWaitsForTheRideToClearToo()
        {
            var s = Mech(morale: 50);
            var world = new World { Clear = false };
            Assert.False(Mechanic.OpenRide(s, world));
            Assert.Equal(1, world.Reloads);
            Assert.Equal(50, s.Morale);
            Assert.Equal(0, world.Reopened);
        }

        // ⚠ A FAILED WALK MUST RELEASE THE RIDE, or it stays claimed by a mechanic who never arrives and
        // nobody else is sent to it - a ride that is broken forever.
        [Theory]
        [InlineData(6)]      // ToRepair
        [InlineData(20)]     // ToService
        public void AFailedWalkToAJobReleasesTheRide(int purpose)
        {
            var s = Mech(); s.Purpose = (StaffPurpose)purpose; s.HasTarget = true;
            var world = new World();
            Mechanic.OnPathMessage(s, world, pathFound: false);
            Assert.Equal(1, world.Released);
            Assert.False(s.HasTarget);
            Assert.Equal(StaffState.Idle, s.State);
        }

        [Fact]
        public void AFailedWalkAwayFromAFinishedJobReleasesNothing()
        {
            var s = Mech(); s.Purpose = MechanicStates.LeaveRide;
            var world = new World();
            Mechanic.OnPathMessage(s, world, pathFound: false);
            Assert.Equal(0, world.Released);                   // the claim was already dropped
            Assert.Equal(StaffState.Idle, s.State);
        }

        // Arrival routes by purpose, and leaving clears the target.
        [Fact]
        public void ArrivalRoutesByPurpose()
        {
            var r = Mech(); r.Purpose = MechanicStates.ToRepair;
            Mechanic.Arrive(r);
            Assert.Equal(MechanicStates.ClosingRide, r.State);

            var v = Mech(); v.Purpose = MechanicStates.ToService;
            Mechanic.Arrive(v);
            Assert.Equal(MechanicStates.ClosingForService, v.State);

            var l = Mech(); l.Purpose = MechanicStates.LeaveRide; l.HasTarget = true;
            Mechanic.Arrive(l);
            Assert.Equal(StaffState.Idle, l.State);
            Assert.False(l.HasTarget);
        }
    }
}
