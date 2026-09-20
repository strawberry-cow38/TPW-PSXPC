using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>The mechanic (behaviour.md §3.5 and its 2026-09-20 correction).</summary>
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
            public bool Queued { get; set; }
            public bool QueuedAsRepair { get; set; }
            public bool PathToRide { get; set; } = true;
            public bool LeavePath { get; set; } = true;
            public bool HandOffAccepted { get; set; } = true;
            public List<StaffMember> Mechanics { get; } = new();

            // A 2x2 ride: span 4, so the finish line is 40.0 units = 163840.
            public int Span { get; set; } = 4;
            public int Progress { get; set; }
            public int ClosingStep { get; set; } = Fixed.One;

            public int BrokenCalls, QueuedCalls, QueuedAsRepairCalls, Released, Upgraded, Reopened, UnderRepair;
            public List<(StaffMember from, StaffMember to, bool repair)> HandOffs { get; } = new();

            public bool IsTypeOnStrike(StaffKind k) => OnStrike;
            public bool HasPatrolRect(StaffMember s) => true;
            public bool TryPathIntoPatrolArea(StaffMember s) => true;
            public bool StrikeMusterExists => true;
            public bool TryPathToStrikeMuster(StaffMember s) => true;
            public bool TryPathToRest(StaffMember s) => true;

            public bool TryClaimBrokenRide(StaffMember s) { BrokenCalls++; return Broken; }
            public bool TryClaimQueuedUpgrade(StaffMember s) { QueuedCalls++; return Queued; }
            public bool TryClaimQueuedUpgradeAsRepair(StaffMember s) { QueuedAsRepairCalls++; return QueuedAsRepair; }
            public void ReleaseClaim(StaffMember s) => Released++;
            public StaffMember NextMechanic(StaffMember s)
            {
                int i = Mechanics.IndexOf(s);
                return i >= 0 && i + 1 < Mechanics.Count ? Mechanics[i + 1] : null;
            }
            public bool TryClaimRideFor(StaffMember from, StaffMember to, bool repair)
            {
                HandOffs.Add((from, to, repair));
                return HandOffAccepted;
            }
            public bool TryPathToClaimedRide(StaffMember s) => PathToRide;
            public bool TryPathToLeavePoint(StaffMember s) => LeavePath;
            public int ClosingProgress(StaffMember s) => Progress;
            public void SetClosingProgress(StaffMember s, int v) => Progress = v;
            public int FootprintSpan(StaffMember s) => Span;
            public void MarkRideUnderRepair(StaffMember s) => UnderRepair++;
            public void MarkRideOpenAndRelease(StaffMember s) => Reopened++;
            public void CompleteUpgrade(StaffMember s) => Upgraded++;
        }

        static StaffMember Mech(int skill = 0, int tiredness = 50, int morale = 50)
            => new(StaffKind.Mechanic) { Skill = skill, Tiredness = tiredness, Morale = morale };

        static StaffMember InState(StaffState state, StaffPurpose purpose = StaffPurpose.None, bool target = false)
        {
            var s = Mech(); s.SetState(state); s.Purpose = purpose; s.HasTarget = target; return s;
        }

        const int Threshold = 40 << Fixed.FracBits;   // 2x2 ride

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

        // The walk cost applies to a REPAIR walk only, not to an upgrade walk.
        [Fact]
        public void OnlyTheRepairWalkHasThatCost()
        {
            var s = Mech(tiredness: 50, morale: 50);
            s.Purpose = MechanicStates.ToUpgrade;
            Mechanic.WalkTick(s);
            Assert.Equal(50, s.Tiredness);
            Assert.Equal(50, s.Morale);
        }

        // A still-walking tick is the mechanic's -2/-2 AND the base's +1 tiredness every 4 ticks
        // (0x80096BF4..C38 falls through to 0x80094590). REJECTS applying WalkTick alone, which would
        // make a mechanic's walk 25% more restful than the code says.
        [Fact]
        public void AStillWalkingTickPaysTheMechanicsCostAndThenTheBases()
        {
            var repair = Mech(tiredness: 50, morale: 50); repair.Purpose = MechanicStates.ToRepair;
            Mechanic.Arrive(repair, new World { NowTick = 8 }, stillWalking: true);
            Assert.Equal(49, repair.Tiredness);        // -2 + 1
            Assert.Equal(48, repair.Morale);

            var upgrade = Mech(tiredness: 50, morale: 50); upgrade.Purpose = MechanicStates.ToUpgrade;
            Mechanic.Arrive(upgrade, new World { NowTick = 8 }, stillWalking: true);
            Assert.Equal(51, upgrade.Tiredness);       // just the base's +1
            Assert.Equal(50, upgrade.Morale);
        }

        // Repair-first (coin 0) falls back to the upgrade queue when nothing is broken.
        [Fact]
        public void RepairFirstFallsBackToTheUpgradeQueue()
        {
            var s = Mech(); var w = new World { Queued = true };
            Mechanic.Idle(s, w, new Dice(0));
            Assert.Equal(MechanicStates.GoToUpgradeRide, s.State);
            Assert.True(s.HasTarget);
            Assert.Equal(0, s.StackDepth);                     // 0x80093F80 is Set, not Push
            Assert.Equal(1, w.BrokenCalls);
            Assert.Equal(1, w.QueuedCalls);
        }

        // ⚠ UPGRADE-FIRST (coin 1) NEVER LOOKS AT BROKEN RIDES. 0x80096E28..E6C calls 0x8005BE44 twice
        // and 0x8005BCB0 never. REJECTS the report's "or the reverse": a symmetric fallback would send
        // this mechanic to the broken ride, and the binary sends him on patrol.
        [Fact]
        public void UpgradeFirstNeverLooksAtBrokenRides()
        {
            var s = Mech(); var w = new World { Broken = true };
            Mechanic.Idle(s, w, new Dice(1));
            Assert.Equal(StaffState.Patrolling, s.State);
            Assert.False(s.HasTarget);
            Assert.Equal(0, w.BrokenCalls);
        }

        // The second attempt in the upgrade-first order is the SAME selector with the repair flag
        // (0x80096E4C..E64). REJECTS dropping the dead call, and REJECTS replacing it with the broken-ride
        // selector. The fixture answering "yes" to it is not reachable in the park (see the interface
        // note); what the assertion pins is that a flag-1 claim lands in state 56, not 57.
        [Fact]
        public void UpgradeFirstAsksTheQueueTwiceTheSecondTimeAsARepair()
        {
            var s = Mech(); var w = new World();
            Mechanic.Idle(s, w, new Dice(1));
            Assert.Equal(2, w.QueuedCalls + w.QueuedAsRepairCalls);
            Assert.Equal(1, w.QueuedAsRepairCalls);
            Assert.Equal(StaffState.Patrolling, s.State);

            var t = Mech(); var w2 = new World { QueuedAsRepair = true };
            Mechanic.Idle(t, w2, new Dice(1));
            Assert.Equal(MechanicStates.GoToBrokenRide, t.State);
            Assert.True(t.HasTarget);
        }

        [Fact]
        public void TheCoinFlipDecidesWhichJobWinsWhenBothAreWaiting()
        {
            var a = Mech();
            Mechanic.Idle(a, new World { Broken = true, Queued = true }, new Dice(0));
            Assert.Equal(MechanicStates.GoToBrokenRide, a.State);

            var b = Mech();
            Mechanic.Idle(b, new World { Broken = true, Queued = true }, new Dice(1));
            Assert.Equal(MechanicStates.GoToUpgradeRide, b.State);
        }

        [Fact]
        public void WithNothingToDoHePatrolsAndOnStrikeHeDoesNothing()
        {
            var s = Mech();
            Mechanic.Idle(s, new World(), new Dice(0));
            Assert.Equal(StaffState.Patrolling, s.State);

            var striking = Mech(tiredness: 50);
            Mechanic.Idle(striking, new World { OnStrike = true, Broken = true }, new Dice(0));
            Assert.Equal(StaffState.Idle, striking.State);
            Assert.Equal(50, striking.Tiredness);      // the idle cost is not paid either
        }

        // Setting off PUSHES the walk over 56/57 (0x80093F20), stamps M+0x2C with now, and sets the
        // purpose. REJECTS Set (the stack would be empty) and REJECTS leaving the stamp from an old job.
        [Theory]
        [InlineData(true, 6, 56)]
        [InlineData(false, 20, 57)]
        public void SettingOffPushesTheWalkAndStampsTheClock(bool repair, int purpose, int under)
        {
            var s = InState((StaffState)under, target: true); s.BusyUntil = 999;
            Assert.True(Mechanic.SetOff(s, new World { NowTick = 1234 }, repair));
            Assert.Equal(StaffState.Walking, s.State);
            Assert.Equal((StaffPurpose)purpose, s.Purpose);
            Assert.Equal(1, s.StackDepth);
            Assert.Equal(1234, s.BusyUntil);
            s.PopState();
            Assert.Equal((StaffState)under, s.State);
        }

        // ⚠ A REFUSED PATH REQUEST IS NOT A FAILED WALK. 0x80096F2C returns with nothing changed, so the
        // mechanic stays in 56/57 holding the claim and asks again next tick. REJECTS going Idle,
        // REJECTS releasing, REJECTS setting the purpose anyway.
        [Fact]
        public void ARefusedPathRequestKeepsHimInPlaceToRetry()
        {
            var s = InState(MechanicStates.GoToBrokenRide, target: true);
            var w = new World { PathToRide = false };
            Assert.False(Mechanic.SetOff(s, w, forRepair: true));
            Assert.Equal(MechanicStates.GoToBrokenRide, s.State);
            Assert.Equal(StaffPurpose.None, s.Purpose);
            Assert.True(s.HasTarget);
            Assert.Equal(0, w.Released);
        }

        // Arrival routes by purpose and zeroes the deadline (0x80096B84/BB4). REJECTS carrying the
        // set-off stamp into the job.
        [Theory]
        [InlineData(6, 16)]
        [InlineData(20, 52)]
        public void ArrivalZeroesTheDeadlineAndStartsClosingTheRide(int purpose, int closing)
        {
            var s = InState(StaffState.WalkToDestination, (StaffPurpose)purpose, target: true); s.BusyUntil = 777;
            Mechanic.Arrive(s, new World { NowTick = 900 }, stillWalking: false);
            Assert.Equal((StaffState)closing, s.State);
            Assert.Equal(0, s.BusyUntil);
            Assert.True(s.HasTarget);
        }

        // ⚠ ARRIVING WITHOUT A TARGET LEAVES HIM IDLE (0x80096B74 / 0x80096BA4). REJECTS closing a ride
        // he no longer holds.
        [Theory]
        [InlineData(6)]
        [InlineData(20)]
        public void ArrivingWithTheClaimGoneStaysIdle(int purpose)
        {
            var s = InState(StaffState.WalkToDestination, (StaffPurpose)purpose, target: false);
            Mechanic.Arrive(s, new World(), stillWalking: false);
            Assert.Equal(StaffState.Idle, s.State);
            Assert.False(s.HasTarget);
        }

        // Arrival 22 drops the target; any other purpose goes to the base (a rest walk ends resting).
        // REJECTS swallowing the base's purposes.
        [Fact]
        public void ArrivalOffTheRideClearsTheTargetAndOtherPurposesGoToTheBase()
        {
            var l = InState(StaffState.WalkToDestination, MechanicStates.LeaveRide, target: true);
            Mechanic.Arrive(l, new World(), stillWalking: false);
            Assert.Equal(StaffState.Idle, l.State);
            Assert.False(l.HasTarget);

            var r = InState(StaffState.WalkToDestination, StaffPurpose.Rest, target: true);
            Mechanic.Arrive(r, new World(), stillWalking: false);
            Assert.Equal(StaffState.Resting, r.State);
        }

        // ⭐ THE CLOCK DOES NOT START UNTIL THE PROGRESS WORD REACHES ITS THRESHOLD, and he raises it by the
        // tick step meanwhile. REJECTS a rider-count predicate (there are no riders in this world at all),
        // REJECTS starting the timer on arrival, and REJECTS `>` (equality completes).
        [Fact]
        public void TheRepairClockDoesNotStartUntilClosingProgressReachesTheThreshold()
        {
            var s = Mech(skill: 0);
            var world = new World { NowTick = 1000, Progress = 0, ClosingStep = Fixed.One };

            Assert.False(Mechanic.CloseRide(s, world, forUpgrade: false));
            Assert.Equal(Fixed.One, world.Progress);
            Assert.Equal(0, s.BusyUntil);
            Assert.Equal(0, world.UnderRepair);

            world.Progress = Threshold - 1;
            Assert.False(Mechanic.CloseRide(s, world, forUpgrade: false));
            Assert.Equal(Threshold, world.Progress);           // clamped, not overshot

            Assert.True(Mechanic.CloseRide(s, world, forUpgrade: false));
            Assert.Equal(1240, s.BusyUntil);                  // now + 240 at skill 0
            Assert.Equal(MechanicStates.Repairing, s.State);
            Assert.Equal(1, world.UnderRepair);
        }

        [Fact]
        public void ClosingForAnUpgradeIsTheSameWaitIntoADifferentState()
        {
            var s = Mech(skill: 2);
            var world = new World { NowTick = 10, Progress = Threshold };
            Assert.True(Mechanic.CloseRide(s, world, forUpgrade: true));
            Assert.Equal(MechanicStates.Upgrading, s.State);
            Assert.Equal(130, s.BusyUntil);
        }

        // The threshold is 10 whole units per tile of SPAN (w+h), in 20.12. A 1x3 ride has span 4 and
        // area 3: REJECTS area, REJECTS 5 per tile (dropping the <<13 for <<12), REJECTS 20 (reading the
        // ×5 and the <<13 as ×5 twice).
        [Theory]
        [InlineData(4, 40 << 12)]
        [InlineData(2, 20 << 12)]
        [InlineData(8, 80 << 12)]
        public void TheClosingThresholdIsTenUnitsPerTileOfSpan(int span, int threshold)
        {
            Assert.Equal(threshold, RideClosing.Threshold(span));
            Assert.True(RideClosing.IsComplete(threshold, threshold));
            Assert.False(RideClosing.IsComplete(threshold - 1, threshold));
        }

        // Raising clamps at the threshold and lowering clamps at zero (0x8009EF80..8C, 0x8009EFC8..D0).
        // REJECTS overshoot in either direction, which would make reopening take one tick longer than
        // closing for the same ride.
        [Fact]
        public void ProgressIsClampedAtBothEnds()
        {
            Assert.Equal(95, RideClosing.Raise(90, 5, 100));
            Assert.Equal(100, RideClosing.Raise(97, 5, 100));
            Assert.Equal(100, RideClosing.Raise(100, 5, 100));
            Assert.Equal(0, RideClosing.Lower(3, 5));
            Assert.Equal(5, RideClosing.Lower(10, 5));
        }

        // A bigger ride takes longer to close. REJECTS a fixed threshold.
        [Fact]
        public void ABiggerRideTakesLongerToClose()
        {
            int TicksToClose(int span)
            {
                var w = new World { Span = span, ClosingStep = Fixed.One };
                var s = Mech(); int ticks = 0;
                while (!Mechanic.CloseRide(s, w, forUpgrade: false) && ticks < 1000) ticks++;
                return ticks;                                  // 1000 = never closed
            }
            Assert.Equal(40, TicksToClose(4));
            Assert.Equal(80, TicksToClose(8));
        }

        [Theory]
        [InlineData(0, 240)]
        [InlineData(4, 60)]
        public void TheJobLengthComesFromTheSkillTable(int skill, int ticks)
        {
            var s = Mech(skill);
            Mechanic.CloseRide(s, new World { NowTick = 0, Progress = Threshold }, forUpgrade: false);
            Assert.Equal(ticks, s.BusyUntil);
        }

        [Fact]
        public void TheWorkTakesItsFullTime()
        {
            var s = Mech(); s.BusyUntil = 500;
            var world = new World { NowTick = 500 };
            Assert.False(Mechanic.Work(s, world, forUpgrade: false));
            world.NowTick = 501;
            Assert.True(Mechanic.Work(s, world, forUpgrade: false));
            Assert.Equal(MechanicStates.OpeningRide, s.State);
        }

        // ⚠ DO NOT FIX: A ZERO DEADLINE NEVER FIRES (0x800968B4, 0x80096A90). REJECTS the plain
        // `now > deadline`, under which a zeroed deadline is an instant finish.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AZeroDeadlineNeverFires(bool upgrade)
        {
            var s = Mech(); s.BusyUntil = 0;
            var world = new World { NowTick = 1 };
            Assert.False(Mechanic.Work(s, world, upgrade));
            world.NowTick = 1_000_000;
            Assert.False(Mechanic.Work(s, world, upgrade));
            Assert.Equal(0, world.Upgraded);
        }

        // Only an UPGRADE buys anything (0x8009C56C at 0x80096AA4); a repair changes nothing about the
        // ride when the timer runs out. REJECTS a repair that "services" the ride.
        [Fact]
        public void OnlyAnUpgradeIsBought()
        {
            var repair = Mech(); repair.BusyUntil = 1;
            var w1 = new World { NowTick = 2 };
            Assert.True(Mechanic.Work(repair, w1, forUpgrade: false));
            Assert.Equal(0, w1.Upgraded);

            var upgrade = Mech(); upgrade.BusyUntil = 1;
            var w2 = new World { NowTick = 2 };
            Assert.True(Mechanic.Work(upgrade, w2, forUpgrade: true));
            Assert.Equal(1, w2.Upgraded);
            Assert.Equal(MechanicStates.OpeningRide, upgrade.State);
        }

        // ⭐ THE +10 IS PAID AT REOPENING, NOT AT FINISHING THE REPAIR. A mechanic pulled off a job
        // before the ride reopens never gets paid for it. REJECTS paying it in Work().
        [Fact]
        public void TheMoraleRewardIsPaidForReopeningNotForRepairing()
        {
            var s = Mech(morale: 50); s.BusyUntil = 1;
            Mechanic.Work(s, new World { NowTick = 2 }, forUpgrade: false);
            Assert.Equal(50, s.Morale);                        // finishing the repair pays nothing

            var w = new World { NowTick = 3, Progress = 0 };
            Assert.True(Mechanic.OpenRide(s, w));
            Assert.Equal(60, s.Morale);                        // reopening pays 10
            Assert.Equal(MechanicStates.LeavingRide, s.State);
            Assert.Equal(1, w.Reopened);
        }

        // ⚠ REOPENING WAITS FOR PROGRESS == 0, NOT FOR "BELOW THE THRESHOLD" (0x8009C294 vs 0x8009C1E8).
        // REJECTS reusing the closing predicate: progress 1 is below the threshold and must NOT reopen.
        [Fact]
        public void ReopeningWaitsForProgressToFallToExactlyZero()
        {
            var s = Mech(morale: 50);
            var w = new World { Progress = Threshold, ClosingStep = Fixed.One };

            Assert.False(Mechanic.OpenRide(s, w));
            Assert.Equal(Threshold - Fixed.One, w.Progress);
            Assert.Equal(50, s.Morale);
            Assert.Equal(0, w.Reopened);

            w.Progress = 1;
            Assert.False(Mechanic.OpenRide(s, w));             // below threshold is not enough
            Assert.Equal(0, w.Progress);

            Assert.True(Mechanic.OpenRide(s, w));
            Assert.Equal(1, w.Reopened);
        }

        // Reopening does NOT drop the target: state 58 still paths from the ride's leave point.
        // REJECTS clearing HasTarget in OpenRide.
        [Fact]
        public void ReopeningKeepsTheTargetForTheWalkOff()
        {
            var s = Mech(); s.HasTarget = true;
            Assert.True(Mechanic.OpenRide(s, new World { Progress = 0 }));
            Assert.True(s.HasTarget);
        }

        // State 58 pushes the walk with purpose 22 and stamps the clock, like 56/57.
        [Fact]
        public void LeavingPushesTheWalkOffTheRide()
        {
            var s = InState(MechanicStates.LeavingRide, target: true);
            Assert.True(Mechanic.LeaveRide(s, new World { NowTick = 55 }));
            Assert.Equal(StaffState.Walking, s.State);
            Assert.Equal(MechanicStates.LeaveRide, s.Purpose);
            Assert.Equal(1, s.StackDepth);
            Assert.Equal(55, s.BusyUntil);
            Assert.True(s.HasTarget);
        }

        // ⚠ A REFUSED LEAVE REQUEST KEEPS HIM AT THE RIDE (0x800970C0 returns with nothing changed).
        // REJECTS the first port's "go Idle and drop the target", which would strand a mechanic on a
        // ride whose leave point the pathfinder was merely too busy to serve this tick.
        [Fact]
        public void ARefusedLeavePathKeepsHimAtTheRideToRetry()
        {
            var s = InState(MechanicStates.LeavingRide, target: true);
            Assert.False(Mechanic.LeaveRide(s, new World { LeavePath = false }));
            Assert.Equal(MechanicStates.LeavingRide, s.State);
            Assert.True(s.HasTarget);
            Assert.Equal(StaffPurpose.None, s.Purpose);
        }

        // ⚠ A FAILED WALK MUST RELEASE THE RIDE, or it stays claimed by a mechanic who never arrives and
        // nobody else is sent to it - a ride that is broken forever.
        [Theory]
        [InlineData(6)]      // ToRepair
        [InlineData(20)]     // ToUpgrade
        public void AFailedWalkToAJobReleasesTheRide(int purpose)
        {
            var s = Mech(); s.Purpose = (StaffPurpose)purpose; s.HasTarget = true;
            var world = new World();
            Mechanic.OnPathMessage(s, world, pathFound: false);
            Assert.Equal(1, world.Released);
            Assert.False(s.HasTarget);
            Assert.Equal(StaffState.Idle, s.State);
        }

        // ⭐ ...AND OFFERS IT TO THE NEXT FREE MECHANIC (0x80096580..5A0), with the repair flag if it was
        // a repair. The taker gets the target and state 56/57. REJECTS releasing without the hand-off,
        // and REJECTS handing a repair on as an upgrade (the flag decides both the checks and the state).
        [Theory]
        [InlineData(6, true, 56)]
        [InlineData(20, false, 57)]
        public void AFailedWalkHandsTheJobToTheNextFreeMechanic(int purpose, bool repair, int state)
        {
            var me = Mech(); me.Purpose = (StaffPurpose)purpose; me.HasTarget = true;
            var next = Mech();
            var world = new World(); world.Mechanics.AddRange(new[] { me, next });

            Mechanic.OnPathMessage(me, world, pathFound: false);

            var offer = Assert.Single(world.HandOffs);
            Assert.Same(me, offer.from); Assert.Same(next, offer.to); Assert.Equal(repair, offer.repair);
            Assert.True(next.HasTarget);
            Assert.Equal((StaffState)state, next.State);
            Assert.Equal(0, next.StackDepth);
            Assert.Equal(StaffState.Idle, me.State);
            Assert.False(me.HasTarget);
        }

        // The list is walked FORWARD FROM ME (0x80097640(me) first): a free mechanic before me is never
        // asked, busy or committed ones after me are skipped, and only the first free one is offered.
        // REJECTS searching from the head and REJECTS offering to more than one.
        [Fact]
        public void TheOfferGoesToTheFirstFreeMechanicAfterMeOnly()
        {
            var before = Mech();
            var me = Mech(); me.Purpose = MechanicStates.ToRepair; me.HasTarget = true;
            var busy = Mech(); busy.HasTarget = true;
            var striking = InState(StaffState.Striking);
            var free = Mech();
            var alsoFree = Mech();
            var world = new World(); world.Mechanics.AddRange(new[] { before, me, busy, striking, free, alsoFree });

            Mechanic.OnPathMessage(me, world, pathFound: false);

            var offer = Assert.Single(world.HandOffs);
            Assert.Same(free, offer.to);
            Assert.Equal(MechanicStates.GoToBrokenRide, free.State);
            Assert.Equal(StaffState.Idle, before.State);
            Assert.Equal(StaffState.Idle, alsoFree.State);
            Assert.False(busy.State == MechanicStates.GoToBrokenRide);
        }

        // A refused claim ENDS the search (0x8009654C jumps straight to the abort). REJECTS moving on
        // to the next candidate, which is what a "find someone" loop would naturally do.
        [Fact]
        public void ARefusedHandOffEndsTheSearch()
        {
            var me = Mech(); me.Purpose = MechanicStates.ToRepair; me.HasTarget = true;
            var first = Mech(); var second = Mech();
            var world = new World { HandOffAccepted = false }; world.Mechanics.AddRange(new[] { me, first, second });

            Mechanic.OnPathMessage(me, world, pathFound: false);

            Assert.Single(world.HandOffs);
            Assert.False(first.HasTarget); Assert.False(second.HasTarget);
            Assert.Equal(StaffState.Idle, first.State);
            Assert.Equal(StaffState.Idle, me.State);
        }

        // Who can be handed a job (0x80097694 + 0x800955B4). REJECTS "any idle mechanic" (a striking one
        // is idle-ish and refused), REJECTS "must be in state 0" (a patroller is taken), and REJECTS
        // "any strike purpose" (only states 2/3 consult the purpose; state 11 with a strike purpose is
        // fair game, as the instructions have it).
        [Theory]
        [InlineData(0, 0, false, true)]        // Idle, nothing: yes
        [InlineData(13, 1, false, true)]       // patrolling: yes
        [InlineData(0, 0, true, false)]        // has a target: no
        [InlineData(15, 0, false, false)]      // striking: no
        [InlineData(26, 0, false, false)]      // walking to strike: no
        [InlineData(2, 5, false, false)]       // walking (2) for a strike: no
        [InlineData(3, 17, false, false)]      // path ready (3) for a rest: no
        [InlineData(2, 1, false, true)]        // walking (2) for a patrol leg: yes
        [InlineData(11, 5, false, true)]       // walk requested (11) for a strike: YES, states 2/3 only
        [InlineData(0, 50, false, false)]      // the unidentified purpose 50: no
        public void WhoCanBeHandedAJob(int state, int purpose, bool target, bool expected)
        {
            var c = InState((StaffState)state, (StaffPurpose)purpose, target);
            Assert.Equal(expected, Mechanic.CanTakeHandedJob(c));
        }

        [Fact]
        public void AFailedWalkAwayFromAFinishedJobReleasesNothingButDropsTheTarget()
        {
            var s = Mech(); s.Purpose = MechanicStates.LeaveRide; s.HasTarget = true;
            var world = new World();
            Mechanic.OnPathMessage(s, world, pathFound: false);
            Assert.Equal(0, world.Released);                   // the claim was already dropped at 17
            Assert.Empty(world.HandOffs);
            Assert.False(s.HasTarget);                         // 0x800965CC
            Assert.Equal(StaffState.Idle, s.State);
        }

        [Fact]
        public void APathFoundGoesToPathReadyAndOtherPurposesGoToTheBase()
        {
            var s = Mech(); s.Purpose = MechanicStates.ToRepair;
            Mechanic.OnPathMessage(s, new World(), pathFound: true);
            Assert.Equal(StaffState.PathReady, s.State);

            var p = Mech(); p.Purpose = StaffPurpose.Patrol;
            Mechanic.OnPathMessage(p, new World(), pathFound: false);
            Assert.Equal(StaffState.RandomWander, p.State);    // the base's answer for a patrol leg
        }

        // The pick-up sweep (0x8009731C): walking states re-issue the job with the right flag.
        // REJECTS aborting a walking mechanic (he would drop a claimed ride every time the player
        // opened the catalogue), and REJECTS re-claiming a repair without the repair checks.
        [Theory]
        [InlineData(2, 20, false, 57)]
        [InlineData(3, 6, true, 56)]
        [InlineData(11, 6, true, 56)]
        public void ThePickUpSweepReissuesAWalkingMechanicsJob(int state, int purpose, bool repair, int go)
        {
            var s = InState((StaffState)state, (StaffPurpose)purpose, target: true);
            var w = new World();
            Mechanic.OnCataloguePickUp(s, w);
            var claim = Assert.Single(w.HandOffs);
            Assert.Same(s, claim.from); Assert.Same(s, claim.to); Assert.Equal(repair, claim.repair);
            Assert.Equal((StaffState)go, s.State);
            Assert.True(s.HasTarget);
            Assert.Equal(0, w.Released);
        }

        // A refused re-claim (the ride stopped qualifying) leaves him where he was. REJECTS aborting.
        [Fact]
        public void ARefusedReclaimLeavesTheWalkingMechanicWhereHeIs()
        {
            var s = InState(StaffState.WalkToDestination, MechanicStates.ToRepair, target: true);
            var w = new World { HandOffAccepted = false };
            Mechanic.OnCataloguePickUp(s, w);
            Assert.Equal(StaffState.WalkToDestination, s.State);
            Assert.True(s.HasTarget);
        }

        // Walking OFF the ride (22) goes back to 58 to re-path; a walking mechanic on base business
        // (a rest, with a target) releases and aborts. REJECTS treating 22 like the job purposes.
        [Fact]
        public void ThePickUpSweepSendsALeavingMechanicBackTo58AndAbortsTheRest()
        {
            var l = InState(StaffState.WalkToDestination, MechanicStates.LeaveRide, target: true);
            var w = new World();
            Mechanic.OnCataloguePickUp(l, w);
            Assert.Equal(MechanicStates.LeavingRide, l.State);
            Assert.True(l.HasTarget);
            Assert.Equal(0, w.Released);

            var r = InState(StaffState.WalkToDestination, StaffPurpose.Rest, target: true);
            Mechanic.OnCataloguePickUp(r, w);
            Assert.Equal(StaffState.Idle, r.State);
            Assert.False(r.HasTarget);
            Assert.Equal(1, w.Released);
        }

        // At the ride (14, 16, 17, 52, 54) the sweep does nothing at all (0x80097458). REJECTS aborting
        // a job in progress, and REJECTS releasing the claim.
        [Theory]
        [InlineData(14)] [InlineData(16)] [InlineData(17)] [InlineData(52)] [InlineData(54)]
        public void ThePickUpSweepLeavesAMechanicAtTheRideAlone(int state)
        {
            var s = InState((StaffState)state, MechanicStates.ToRepair, target: true); s.BusyUntil = 42;
            var w = new World();
            Mechanic.OnCataloguePickUp(s, w);
            Assert.Equal((StaffState)state, s.State);
            Assert.True(s.HasTarget);
            Assert.Equal(42, s.BusyUntil);
            Assert.Equal(0, w.Released);
            Assert.Empty(w.HandOffs);
        }

        // ⚠ EVERYTHING ELSE ABORTS, INCLUDING 56/57/58: states 55+ fall off the end of the 0x800E467C
        // table into the default arm. A mechanic who has claimed a ride but not yet got a path loses it.
        // The purpose is a job purpose here because that is what a mechanic in 56 carries (SetOff writes
        // it later, so 56 still holds the LAST job's), and the walking arm would re-claim on it.
        // REJECTS grouping 56/57 with the walking states, which is what their names suggest.
        [Theory]
        [InlineData(0, false)] [InlineData(13, false)] [InlineData(50, false)]
        [InlineData(56, true)] [InlineData(57, true)] [InlineData(58, true)] [InlineData(49, true)]
        public void ThePickUpSweepAbortsEveryoneElseReleasingAnyClaim(int state, bool target)
        {
            var s = InState((StaffState)state, MechanicStates.ToRepair, target: target);
            var w = new World();
            Mechanic.OnCataloguePickUp(s, w);
            Assert.Equal(StaffState.Idle, s.State);
            Assert.False(s.HasTarget);
            Assert.Equal(target ? 1 : 0, w.Released);
            Assert.Empty(w.HandOffs);
        }
    }
}
