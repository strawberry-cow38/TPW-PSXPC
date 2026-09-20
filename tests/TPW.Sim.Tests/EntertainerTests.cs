using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class EntertainerTests
    {
        sealed class World : GuardWorld, IEntertainerWorld
        {
            public bool Held;
            public int? GuestDistance = 0;
            public int GuestQueries, InfluenceFlag;
            public readonly List<(Guard guard, int distanceTiles)> Guards = new();
            public bool IsHeld(StaffMember s) { LastStaff = s; return Held; }
            public int? NearestGuestDistanceSquared(StaffMember s)
            { GuestQueries++; LastStaff = s; Calls.Add("guest-distance"); return GuestDistance; }
            public void ReleaseInfluence(StaffMember s) { LastStaff = s; Calls.Add("release"); }
            public void PlaceInfluence(StaffMember s, int flag)
            { LastStaff = s; InfluenceFlag = flag; Calls.Add("place"); }
            public IEnumerable<(Guard guard, int distanceTiles)> GuardsWithDistances(StaffMember s)
            { LastStaff = s; Calls.Add("guards"); return Guards; }
        }
        static Entertainer NewEntertainer()
            => new(new StaffMember(StaffKind.Entertainer) { Morale = 70, Tiredness = 20 });
        static Guard NewGuard(int state = 0, int purpose = 0)
        {
            var s = new StaffMember(StaffKind.Guard) { Morale = 50, Tiredness = 20, Purpose = (StaffPurpose)purpose };
            s.SetState((StaffState)state); return new Guard(s);
        }

        // REJECTS the old swapped handler map, renumbering states, and Push instead of Set on Idle.
        [Fact]
        public void IdleReleasesThenPlacesFlagTwoAtTheMembersPositionAndSets12()
        {
            var e = NewEntertainer(); var w = new World { NowTick = 123 }; var dice = new StaffDice(0);
            e.Staff.PushState(StaffState.Idle);
            Assert.True(e.Tick(w, dice));
            Assert.Equal(12, (int)e.Staff.State); Assert.Equal(0, e.Staff.StackDepth);
            Assert.Equal(12, (int)EntertainerStates.Entertaining); Assert.Equal(32, (int)EntertainerStates.Shocked);
            Assert.Equal(123, e.Staff.BusyUntil); Assert.Equal(2, w.InfluenceFlag);
            Assert.Equal(new[] { "release", "guest-distance", "place" }, w.Calls);
            Assert.Same(e.Staff, w.LastStaff); Assert.Equal(new[] { 3 }, dice.Bounds);
        }

        // REJECTS <= 2 (diagonals), rejecting a guest on the same tile, or performing with no guests.
        [Theory]
        [InlineData(0, 12)] [InlineData(1, 12)] [InlineData(2, 13)] [InlineData(4, 13)] [InlineData(null, 13)]
        public void IdleRequiresSquaredTileDistanceStrictlyBelowTwo(int? distance, int state)
        {
            var e = NewEntertainer(); var w = new World { GuestDistance = distance };
            e.Tick(w, new StaffDice(0)); Assert.Equal(state, (int)e.Staff.State);
            Assert.Equal(state == 12 ? 2 : 0, w.InfluenceFlag);
        }

        // REJECTS a two-in-three chance, querying guests before a failed roll, or staying idle on failure.
        [Theory]
        [InlineData(1)] [InlineData(2)]
        public void AFaultlessAudienceDoesNotOverrideAFailedRoll(int roll)
        {
            var e = NewEntertainer(); var w = new World(); var dice = new StaffDice(roll);
            e.Staff.BusyUntil = 777; e.Staff.PushState(StaffState.Idle);
            e.Tick(w, dice); Assert.Equal(13, (int)e.Staff.State); Assert.Equal(0, e.Staff.StackDepth);
            Assert.Equal(new[] { "release" }, w.Calls); Assert.Equal(new[] { 3 }, dice.Bounds);
            Assert.Equal(777, e.Staff.BusyUntil);
        }

        // REJECTS reallocating/restarting an existing performance through the idle selector.
        [Fact]
        public void TheSelectorCannotStartAnotherPerformanceWhileAlreadyIn12()
        {
            var e = NewEntertainer(); var w = new World { NowTick = 123 };
            e.Staff.SetState(EntertainerStates.Entertaining); e.Staff.BusyUntil = 50;
            e.Idle(w, new StaffDice(0));
            Assert.Equal(13, (int)e.Staff.State); Assert.Equal(50, e.Staff.BusyUntil);
            Assert.Equal(new[] { "release", "guest-distance" }, w.Calls);
        }

        // REJECTS rolling or creating an aura during a strike, and releasing the old aura only on success.
        [Fact]
        public void StrikingIdleStillReleasesItsAuraButDoesNotRoll()
        {
            var e = NewEntertainer(); var w = new World { OnStrike = true }; var dice = new StaffDice(0);
            e.Tick(w, dice); Assert.Equal(StaffState.Idle, e.Staff.State);
            Assert.Equal(new[] { "release" }, w.Calls); Assert.Empty(dice.Bounds);
        }

        // REJECTS updating held entertainers in any owned state, including charging shock morale.
        [Theory]
        [InlineData(0)] [InlineData(12)] [InlineData(32)]
        public void HoldingTheEntertainerSuppressesItsWholeUpdate(int state)
        {
            var e = NewEntertainer(); var w = new World { Held = true, NowTick = 1000 };
            var dice = new StaffDice(0); e.Person08 = 3; e.Staff.SetState((StaffState)state);
            Assert.False(e.Tick(w, dice)); Assert.Equal(state, (int)e.Staff.State);
            Assert.Equal(70, e.Staff.Morale); Assert.Equal(20, e.Staff.Tiredness);
            Assert.Empty(w.Calls); Assert.Empty(dice.Bounds); Assert.Same(e.Staff, w.LastStaff);
        }

        // REJECTS the shared four-tick cadence, EVEN ticks, || in the quirk, or replacing raw P+0x08
        // with staff kind/state/skill. Those fields are identical in every row; only P+0x08 changes.
        [Theory]
        [InlineData(3, 101, 22, 71)] [InlineData(3, 102, 20, 70)]
        [InlineData(2, 101, 20, 70)] [InlineData(4, 101, 20, 70)] [InlineData(0, 101, 20, 70)]
        public void PerformanceQuirkNeedsRawThreeAndAnOddTick(int raw, int now, int tired, int morale)
        {
            var e = NewEntertainer(); var w = new World { NowTick = now };
            e.Person08 = raw; e.Staff.SetState(EntertainerStates.Entertaining);
            e.Tick(w, new StaffDice());
            Assert.Equal(tired, e.Staff.Tiredness); Assert.Equal(morale, e.Staff.Morale);
            Assert.Equal(12, (int)e.Staff.State); Assert.Equal(0, w.GuestQueries);
        }

        // REJECTS a fixed 600-tick stop despite an audience, ending at exactly the deadline, early
        // audience queries, or resetting the start time each tick while an audience stays.
        [Theory]
        [InlineData(699, null, 12, 0)] [InlineData(700, null, 12, 0)]
        [InlineData(701, null, 0, 1)] [InlineData(701, 1, 12, 1)] [InlineData(701, 2, 0, 1)]
        public void PerformanceEndsOnlyAfter600TicksAndWithoutAnAdjacentGuest(int now, int? distance, int state, int queries)
        {
            var e = NewEntertainer(); var w = new World { NowTick = now, GuestDistance = distance };
            e.Staff.BusyUntil = 100; e.Staff.PushState(EntertainerStates.Entertaining);
            e.Tick(w, new StaffDice());
            Assert.Equal(state, (int)e.Staff.State); Assert.Equal(queries, w.GuestQueries);
            Assert.Equal(state == 0 ? 0 : 1, e.Staff.StackDepth); Assert.Equal(100, e.Staff.BusyUntil);
            Assert.DoesNotContain("release", w.Calls); // released on the NEXT Idle handler
        }

        // REJECTS a guessed minimum shock duration, wrong random bound/multiplier, Set instead of
        // Push, charging -10 on entry, or changing the pelting guest's own state.
        [Theory]
        [InlineData(0, 100)] [InlineData(1, 160)] [InlineData(4, 340)]
        public void PeltingPushesShockAndRecordsTheCulpritAndExactDeadline(int roll, int deadline)
        {
            var e = NewEntertainer(); var w = new World { NowTick = 100 }; var dice = new StaffDice(roll);
            var v = GuardTests.Guest(5); e.Staff.SetState(StaffState.Patrolling);
            e.Staff.PushState(EntertainerStates.Entertaining);
            e.Pelted(v, w, dice);
            Assert.Equal(32, (int)e.Staff.State); Assert.Equal(2, e.Staff.StackDepth);
            Assert.Equal(65, e.Staff.Morale); Assert.Equal(20, e.Staff.Tiredness);
            Assert.Equal(deadline, e.Staff.BusyUntil); Assert.Same(v, e.Culprit);
            Assert.Equal(VisitorState.RandomWander, v.State); Assert.Equal(0, v.StackDepth);
            Assert.Equal(new[] { 5 }, dice.Bounds); Assert.Empty(w.Calls);
        }

        // ⭐ REJECTS -10 only once, at a cadence, or only before expiry; >= for expiry; and Set Idle
        // instead of Pop. At expiry the extra -5 stacks with that tick's -10, then nested work resumes.
        [Fact]
        public void ShockChargesEveryTickAndPopsOnlyAfterItsDeadline()
        {
            var e = NewEntertainer(); var w = new World { NowTick = 100 };
            e.Staff.SetState(StaffState.Patrolling); e.Staff.PushState(EntertainerStates.Entertaining);
            e.Pelted(GuardTests.Guest(), w, new StaffDice(1));
            w.NowTick = 159; e.Tick(w, new StaffDice()); Assert.Equal(55, e.Staff.Morale);
            Assert.Equal(32, (int)e.Staff.State); Assert.Empty(w.Calls);
            w.NowTick = 160; e.Tick(w, new StaffDice()); Assert.Equal(45, e.Staff.Morale);
            Assert.Equal(32, (int)e.Staff.State); Assert.Empty(w.Calls);
            w.NowTick = 161; e.Tick(w, new StaffDice()); Assert.Equal(30, e.Staff.Morale);
            Assert.Equal(12, (int)e.Staff.State); Assert.Equal(1, e.Staff.StackDepth);
            Assert.Same(e.Staff, w.LastStaff);
            e.Staff.PopState(); Assert.Equal(StaffState.Patrolling, e.Staff.State);
        }

        // REJECTS restoring only performances: shock returns to whatever was interrupted, including
        // a walking state with a purpose. Its timer aliases E+0x2C rather than restoring a saved clock.
        [Theory]
        [InlineData(0)] [InlineData(3)] [InlineData(11)] [InlineData(13)] [InlineData(50)]
        public void ShockResumesArbitraryWorkAndKeepsTheOverwrittenClock(int state)
        {
            var e = NewEntertainer(); var w = new World { NowTick = 100 };
            e.Staff.SetState((StaffState)state); e.Staff.Purpose = StaffPurpose.Rest; e.Staff.BusyUntil = 1;
            e.Pelted(GuardTests.Guest(), w, new StaffDice(4)); w.NowTick = 341; e.Tick(w, new StaffDice());
            Assert.Equal(state, (int)e.Staff.State); Assert.Equal(0, e.Staff.StackDepth);
            Assert.Equal(StaffPurpose.Rest, e.Staff.Purpose); Assert.Equal(340, e.Staff.BusyUntil);
            Assert.Equal(50, e.Staff.Morale);
        }

        // REJECTS dispatching the nearest BUSY guard, dispatching every free guard, a different
        // culprit, or applying the no-guard penalty even when a real guard takes the chase.
        [Fact]
        public void ShockDispatchesOnlyTheNearestAvailableGuardUsingTheSharedPredicate()
        {
            var e = NewEntertainer(); var w = new World { NowTick = 200 }; var v = GuardTests.Guest();
            var busy = NewGuard(33); var busyWalk = NewGuard(3, 8);
            var nearestFree = NewGuard(0, 8); var furtherFree = NewGuard(13);
            w.Guards.AddRange(new[] { (busy, 0), (busyWalk, 1), (furtherFree, 5), (nearestFree, 2) });
            e.Staff.SetState(EntertainerStates.Entertaining); e.Pelted(v, w, new StaffDice(0));
            w.NowTick = 201; e.Tick(w, new StaffDice());
            Assert.Equal(55, e.Staff.Morale); Assert.Equal(12, (int)e.Staff.State);
            Assert.Equal(33, (int)nearestFree.Staff.State); Assert.Same(v, nearestFree.Culprit);
            Assert.Equal(3801, nearestFree.Staff.BusyUntil); Assert.True(nearestFree.Staff.HasTarget);
            Assert.Equal(new[] { "guards", "free", "animation" }, w.Calls);
            Assert.Equal(30, w.Animation); Assert.Same(nearestFree.Staff, w.LastStaff);
            Assert.Null(busy.Culprit); Assert.Equal(3, (int)busyWalk.Staff.State);
            Assert.Equal(13, (int)furtherFree.Staff.State); Assert.Null(furtherFree.Culprit);
        }

        // REJECTS treating a nonempty guard list as successful dispatch when every guard is busy.
        [Fact]
        public void AllBusyGuardsCostTheSameExtraFiveAsNoGuards()
        {
            var e = NewEntertainer(); var w = new World();
            w.Guards.AddRange(new[] { (NewGuard(39), 0), (NewGuard(15), 1), (NewGuard(3, 5), 2), (NewGuard(3, 9), 3) });
            e.Pelted(GuardTests.Guest(), w, new StaffDice(0)); w.NowTick = 1; e.Tick(w, new StaffDice());
            Assert.Equal(50, e.Staff.Morale); Assert.Equal(StaffState.Idle, e.Staff.State);
            Assert.Equal(new[] { "guards" }, w.Calls);
        }

        // REJECTS an inclusive seven-tile radius, ignoring the radius, and replacing the first
        // equally near guard. Unsorted candidates also reject taking the first available guard.
        [Theory]
        [InlineData(6, true)] [InlineData(7, false)] [InlineData(8, false)]
        public void DispatchRangeIsStrictAndTiesKeepListOrder(int distance, bool dispatched)
        {
            var e = NewEntertainer(); var w = new World();
            var first = NewGuard(); var tied = NewGuard(); var farther = NewGuard();
            w.Guards.AddRange(new[] { (farther, distance + 1), (first, distance), (tied, distance) });
            e.Pelted(GuardTests.Guest(), w, new StaffDice(0)); w.NowTick = 1; e.Tick(w, new StaffDice());
            Assert.Equal(dispatched ? 33 : 0, (int)first.Staff.State);
            Assert.Equal(StaffState.Idle, farther.Staff.State); Assert.Equal(StaffState.Idle, tied.Staff.State);
            Assert.Equal(dispatched ? 55 : 50, e.Staff.Morale);
        }

        // REJECTS bypassing clamps in any entertainer stat path; repeated shock never wraps morale.
        [Fact]
        public void PerformanceAndShockClampStats()
        {
            var e = NewEntertainer(); var w = new World { NowTick = 1 }; e.Person08 = 3;
            e.Staff.Morale = 100; e.Staff.Tiredness = 99; e.Perform(w);
            Assert.Equal(100, e.Staff.Morale); Assert.Equal(100, e.Staff.Tiredness);
            e.Staff.Morale = 4; e.Pelted(GuardTests.Guest(), w, new StaffDice(4));
            Assert.Equal(0, e.Staff.Morale); e.Shock(w); Assert.Equal(0, e.Staff.Morale);
            w.NowTick = 1000; e.Shock(w); Assert.Equal(0, e.Staff.Morale);
        }

        // REJECTS accidentally running Idle for shared staff states or withholding their base update.
        [Fact]
        public void SharedStatesAreLeftToTheSharedMachine()
        {
            var e = NewEntertainer(); var w = new World(); var dice = new StaffDice(0);
            e.Staff.SetState(StaffState.Resting);
            Assert.True(e.Tick(w, dice)); Assert.Equal(StaffState.Resting, e.Staff.State);
            Assert.Empty(w.Calls); Assert.Empty(dice.Bounds); Assert.Equal(70, e.Staff.Morale);
        }
    }
}
