using System.Linq;
using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    sealed class StaffDice : IRandomSource
    {
        readonly Queue<int> values;
        public StaffDice(params int[] values) => this.values = new Queue<int>(values);
        public List<int> Bounds { get; } = new();
        public int Next(int n) { Bounds.Add(n); return values.Count == 0 ? 0 : values.Dequeue(); }
    }

    // Shared park fixture lets the entertainer dispatch a REAL guard through the same interface.
    class GuardWorld : IGuardWorld
    {
        public long NowTick { get; set; }
        public bool OnStrike, Exists = true, SameTile, PostPath = true;
        public bool HasExits { get; set; } = true;
        public int Counter80103950 { get; set; }
        public int Counter80103954 { get; set; }
        public readonly List<string> Calls = new();
        public StaffMember LastStaff;
        public Visitor LastGuest;
        public int LastMessage, Animation;
        public (int flags, int secondary) PathFlags;
        public (int attempts, int radius, int minY, int maxY) PostSearch;
        public bool IsTypeOnStrike(StaffKind kind) => OnStrike;
        public bool HasPatrolRect(StaffMember s) => true;
        public bool TryPathIntoPatrolArea(StaffMember s) => true;
        public bool StrikeMusterExists => true;
        public bool TryPathToStrikeMuster(StaffMember s) => true;
        public bool TryPathToRest(StaffMember s) => true;
        public bool GuestExists(Visitor v) { Calls.Add("exists"); LastGuest = v; return Exists; }
        public bool OnGuestTile(StaffMember s, Visitor v)
        { Calls.Add("tile"); LastStaff = s; LastGuest = v; return SameTile; }
        public void SendGuestMessage(Visitor v, int message)
        { Calls.Add("message"); LastGuest = v; LastMessage = message; }
        public void FreeWaypoints(StaffMember s) { Calls.Add("free"); LastStaff = s; }
        public void SetAnimation(StaffMember s, int animation)
        { Calls.Add("animation"); LastStaff = s; Animation = animation; }
        public void PathToGuest(StaffMember s, Visitor v, int flags, int secondaryFlags)
        { Calls.Add("guest-path"); LastStaff = s; LastGuest = v; PathFlags = (flags, secondaryFlags); }
        public void PathToParkPoint(StaffMember s, int pointIndex, int flags, int secondaryFlags)
        { Calls.Add($"point-{pointIndex}"); LastStaff = s; PathFlags = (flags, secondaryFlags); }
        public void PathToRandomExit(StaffMember s, int flags, int secondaryFlags)
        { Calls.Add("random-exit"); LastStaff = s; PathFlags = (flags, secondaryFlags); }
        public void SetGateWaypoint(StaffMember s) { Calls.Add("gate-waypoint"); LastStaff = s; }
        public bool TryPathToPost(StaffMember s, int attempts, int xRadius, int minY, int maxY,
                                  int flags, int secondaryFlags)
        {
            Calls.Add("post"); LastStaff = s; PostSearch = (attempts, xRadius, minY, maxY);
            PathFlags = (flags, secondaryFlags); return PostPath;
        }
    }

    public class GuardTests
    {
        static Guard NewGuard() => new(new StaffMember(StaffKind.Guard) { Morale = 50, Tiredness = 20 });
        internal static Visitor Guest(int state = 0)
        {
            var v = Visitor.Spawn(new StaffDice(), 0);
            v.SetState((VisitorState)state);
            return v;
        }
        static void State(Guard g, int expected, int depth = 0)
        { Assert.Equal(expected, (int)g.Staff.State); Assert.Equal(depth, g.Staff.StackDepth); }
        static void HasPath(Guard g, GuardWorld w, string call, int purpose, int flags, int depth = 0)
        {
            State(g, 11, depth);
            Assert.Equal(purpose, (int)g.Staff.Purpose);
            Assert.Contains(call, w.Calls);
            Assert.Equal((flags, 0), w.PathFlags);
            Assert.Same(g.Staff, w.LastStaff);
        }

        // REJECTS renumbering raw jump-table keys or mistaking state 11 for state 2.
        [Fact]
        public void StateAndPurposeNumbersAreTheOriginalInterface()
        {
            Assert.Equal(new[] { 2, 33, 39, 46, 47, 48, 55, 59 }, new[] {
                (int)GuardStates.WalkToDestination, (int)GuardStates.Chase, (int)GuardStates.ToExitPoint,
                (int)GuardStates.AtGate, (int)GuardStates.CrossGate, (int)GuardStates.LeavePark,
                (int)GuardStates.TakePost, (int)GuardStates.ToSpawnPoint });
            Assert.Equal(new[] { 8, 9, 14, 15, 16, 21 }, new[] {
                (int)GuardStates.ToCulprit, (int)GuardStates.ToRandomExit, (int)GuardStates.ExitPoint,
                (int)GuardStates.SpawnPoint, (int)GuardStates.Gate, (int)GuardStates.Post });
        }

        // REJECTS dropping any busy state, treating all purposes as busy, or letting a stale idle
        // purpose block dispatch. The full product isolates each clause of the shared availability rule.
        [Fact]
        public void AvailabilityDistinguishesStateFromWalkingPurpose()
        {
            foreach (int state in new[] { 0, 2, 3, 5, 11, 13, 15, 33, 39, 46, 47, 48, 55, 59 })
            foreach (int purpose in new[] { 0, 1, 5, 8, 9, 14, 15, 16, 17, 21 })
            {
                var s = NewGuard().Staff;
                s.SetState((StaffState)state); s.Purpose = (StaffPurpose)purpose;
                bool busyState = state == 15 || state == 33 || state == 39;
                bool busyWalk = state == 3
                    && new[] { 5, 8, 9 }.Contains(purpose);
                Assert.Equal(!busyState && !busyWalk, Guard.NotBusy(s));
            }
        }

        // REJECTS reusing the old path/deadline, pushing Chase, omitting the target or chase animation.
        [Fact]
        public void DispatchReplacesThePreviousJobAndStartsTheFullDeadline()
        {
            var g = NewGuard(); var w = new GuardWorld { NowTick = 123 };
            var v = Guest(); g.Staff.PushState(StaffState.Resting); g.Staff.BusyUntil = 9;
            g.Dispatch(v, w);
            State(g, 33); Assert.Equal(3723, g.Staff.BusyUntil);
            Assert.Same(v, g.Culprit); Assert.True(g.Staff.HasTarget);
            Assert.Equal(new[] { "free", "animation" }, w.Calls);
            Assert.Same(g.Staff, w.LastStaff); Assert.Equal(30, w.Animation);
            Assert.Equal(50, g.Staff.Morale); Assert.Equal(20, g.Staff.Tiredness);
        }

        // REJECTS patrol during a strike and omitting animation 15 on the strike branch.
        [Theory]
        [InlineData(false, 13)] [InlineData(true, 0)]
        public void IdleUsesTheBaseStrikeHandling(bool strike, int state)
        {
            var g = NewGuard(); var w = new GuardWorld { OnStrike = strike };
            g.Tick(w); State(g, state); Assert.Equal(15, w.Animation);
            Assert.Equal(new[] { "animation" }, w.Calls);
            Assert.Equal(50, g.Staff.Morale); Assert.Equal(20, g.Staff.Tiredness);
        }

        // REJECTS && between queue and timeout, omitting queue state 20, broadening to joining-queue
        // state 41, or catching first when a queuing culprit shares the guard's tile.
        [Theory]
        [InlineData(18, true)] [InlineData(19, true)] [InlineData(20, true)]
        [InlineData(17, false)] [InlineData(21, false)] [InlineData(41, false)]
        public void OnlyTheThreeQueueStatesAbortBeforeACatch(int guestState, bool abort)
        {
            var g = NewGuard(); var w = new GuardWorld { SameTile = true };
            g.Dispatch(Guest(guestState), w); w.Calls.Clear();
            g.Tick(w);
            State(g, abort ? 0 : 39);
            Assert.Equal(abort ? 45 : 60, g.Staff.Morale);
            Assert.Equal(abort ? 20 : 23, g.Staff.Tiredness);
            if (abort) { Assert.Null(g.Culprit); Assert.False(g.Staff.HasTarget); Assert.Equal(new[] { "exists" }, w.Calls); }
            else Assert.Equal(4, w.LastMessage);
        }

        // REJECTS >= at the chase deadline, resetting the clock each chase tick, and using -2 for
        // giving up. Exactly at the deadline he can still catch; one tick later he cannot.
        [Theory]
        [InlineData(3699, 39, 60)] [InlineData(3700, 39, 60)] [InlineData(3701, 0, 45)]
        public void ChaseDeadlinePassesStrictlyAfter3600Ticks(long tick, int state, int morale)
        {
            var g = NewGuard(); var w = new GuardWorld { NowTick = 100, SameTile = true };
            g.Dispatch(Guest(), w); w.NowTick = tick;
            g.Tick(w); State(g, state); Assert.Equal(morale, g.Staff.Morale);
            Assert.Equal(3700, g.Staff.BusyUntil);
        }

        // REJECTS Set instead of Push for chasing, wrong request flags, and arriving at Idle instead
        // of resuming Chase. Arrival must not grant the catch reward without a same-tile check.
        [Fact]
        public void ChasePathsToTheActualCulpritAndReturnsTo33()
        {
            var g = NewGuard(); var w = new GuardWorld(); var v = Guest();
            g.Dispatch(v, w); w.Calls.Clear(); g.Tick(w);
            HasPath(g, w, "guest-path", 8, 0x11, 1); Assert.Same(v, w.LastGuest);
            Assert.Equal(new[] { "exists", "tile", "guest-path" }, w.Calls);
            g.Arrive(w); State(g, 33); Assert.Same(v, g.Culprit);
            Assert.Equal(50, g.Staff.Morale); Assert.Equal(20, g.Staff.Tiredness);
        }

        // REJECTS checking for a catch only at path arrival, running it for another purpose, or
        // charging a catch reward while still on a different tile.
        [Theory]
        [InlineData(8, true, true)] [InlineData(8, false, false)] [InlineData(9, true, false)]
        public void EveryPurposeEightStepCanCatch(int purpose, bool sameTile, bool caught)
        {
            var g = NewGuard(); var w = new GuardWorld { SameTile = sameTile }; var v = Guest();
            g.Dispatch(v, w); g.Staff.PushState(StaffState.PathReady);
            g.Staff.Purpose = (StaffPurpose)purpose; w.Calls.Clear();
            Assert.Equal(caught, g.WalkStep(w));
            State(g, caught ? 39 : 3, caught ? 0 : 1);
            // ⭐⭐ NOTHING IS PAID FOR A CATCH MADE MID-STEP. 0x80097B44..0x80097B84 sends message 4 and
            // sets 39 with no stat call between; state 33's catch pays +10 and +3. Same act, same
            // result for the guest, and the guard is rewarded only if he was standing still.
            // REJECTS sharing one Catch() between the two handlers, which is the natural way to write
            // it and quietly pays the reward twice as often as the game does.
            Assert.Equal(50, g.Staff.Morale);
            Assert.Equal(20, g.Staff.Tiredness);
            if (caught) { Assert.Same(v, w.LastGuest); Assert.Equal(4, w.LastMessage); }
            if (purpose == 9) Assert.Empty(w.Calls);
        }

        // REJECTS one price for both handlers, chasing a removed object, and not clearing the target.
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void GoneCulpritCostsTwoMidStepAndFiveWhileChasing(bool step)
        {
            var g = NewGuard(); var w = new GuardWorld { Exists = false, SameTile = true };
            g.Dispatch(Guest(), w); g.Staff.Purpose = (StaffPurpose)8;
            g.Staff.PushState(step ? StaffState.PathReady : GuardStates.Chase); w.Calls.Clear();
            if (step) Assert.True(g.WalkStep(w)); else g.Tick(w);
            // ⚠ THE SAME EVENT COSTS DIFFERENT AMOUNTS depending on which handler notices it: state 33
            // puts a vanished culprit through the same -5 block as a timeout (0x80097D9C -> 0x80097DC8),
            // while the state-3 override charges -2 (0x80097A94). REJECTS one shared abort price.
            State(g, 0); Assert.Equal(step ? 48 : 45, g.Staff.Morale); Assert.Equal(20, g.Staff.Tiredness);
            Assert.Null(g.Culprit); Assert.False(g.Staff.HasTarget);
            Assert.Equal(new[] { "exists" }, w.Calls);
        }

        // REJECTS dereferencing a cleared culprit, or letting an absent culprit be caught at a stale tile.
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void NullCulpritIsGoneWithoutConsultingThePark(bool step)
        {
            var g = NewGuard(); var w = new GuardWorld { SameTile = true };
            g.Staff.SetState(GuardStates.Chase); g.Staff.Purpose = (StaffPurpose)8;
            if (step) Assert.True(g.WalkStep(w)); else g.Tick(w);
            // -2 mid-step, -5 standing: the two aborts are not the same price. See the Chase note.
            State(g, 0); Assert.Equal(step ? 48 : 45, g.Staff.Morale); Assert.Empty(w.Calls);
        }

        // ⭐ REJECTS ending the ejection when the guest disappears, collapsing the two gate visits,
        // reversing gate direction, "repairing" the asymmetric counters, and skipping the final post.
        [Fact]
        public void ACatchMakesTheWholeExitAndReturnLoopBeforePatrol()
        {
            var g = NewGuard(); var v = Guest();
            var w = new GuardWorld { SameTile = true, Counter80103950 = 10, Counter80103954 = 20 };
            g.Dispatch(v, w); g.Tick(w); State(g, 39);
            Assert.Equal(4, w.LastMessage); Assert.Same(v, w.LastGuest);
            w.Calls.Clear(); g.Staff.PushState(GuardStates.ToExitPoint); g.Tick(w);
            HasPath(g, w, "point-1", 14, 0x21);
            g.Arrive(w); State(g, 46); Assert.Equal(1, g.GateDirection);
            Assert.Null(g.Culprit); Assert.Equal(11, w.Counter80103950); Assert.Equal(20, w.Counter80103954);
            w.Calls.Clear(); g.Tick(w); State(g, 46); Assert.Empty(w.Calls);
            g.OnMessage(w, 9); State(g, 47); g.Tick(w); State(g, 3);
            Assert.Equal(16, (int)g.Staff.Purpose); Assert.Equal(new[] { "gate-waypoint" }, w.Calls);
            Assert.Same(g.Staff, w.LastStaff);
            g.Arrive(w); State(g, 48); Assert.Equal(10, w.Counter80103950); Assert.Equal(21, w.Counter80103954);
            w.Calls.Clear(); g.Staff.PushState(GuardStates.LeavePark); g.Tick(w);
            HasPath(g, w, "random-exit", 9, 1);
            g.Arrive(w); State(g, 59); Assert.Null(g.Culprit); Assert.False(g.Staff.HasTarget);
            w.Calls.Clear(); g.Staff.PushState(GuardStates.ToSpawnPoint); g.Tick(w);
            HasPath(g, w, "point-0", 15, 1);
            g.Arrive(w); State(g, 46); Assert.Equal(0, g.GateDirection);
            // ⭐ THE COUNTER BALANCES, AND THAT IS THE EVIDENCE. Both arrivals AT the gate add one and
            // both crossings take one away, so a guard that ejects someone and comes back leaves the
            // count exactly where it found it - 10 in, 10 out. behaviour.md §3.4 omits the increment on
            // arrival 15, and with that reading every ejection would quietly lose one, for ever.
            Assert.Equal(11, w.Counter80103950); Assert.Equal(21, w.Counter80103954);
            g.OnMessage(w, 9); State(g, 47); g.Tick(w); State(g, 3);
            g.Arrive(w); State(g, 55); Assert.Equal(10, w.Counter80103950); Assert.Equal(22, w.Counter80103954);
            w.Calls.Clear(); g.Tick(w); HasPath(g, w, "post", 21, 0x21, 1);
            Assert.Equal((5, 3, 1, 5), w.PostSearch);
            g.Arrive(w); State(g, 0); g.Tick(w); State(g, 13);
            // The +10/+3 IS paid here: this catch came from state 33, not from a walking step.
            Assert.Equal(60, g.Staff.Morale); Assert.Equal(23, g.Staff.Tiredness);
        }

        // REJECTS assuming only literal 1 means outbound, clamping the unnamed counter, and Push at
        // arrival. These are arithmetic words with unknown purpose, not validated guest counts.
        [Theory]
        [InlineData(0, 55)] [InlineData(1, 48)] [InlineData(2, 48)]
        public void GateArrivalBranchesOnZeroAndMovesBothRawCounters(int direction, int state)
        {
            var g = NewGuard(); var w = new GuardWorld();
            g.GateDirection = direction; g.Staff.Purpose = (StaffPurpose)16;
            g.Staff.PushState(StaffState.PathReady); g.Arrive(w);
            State(g, state); Assert.Equal(-1, w.Counter80103950); Assert.Equal(1, w.Counter80103954);
        }

        // REJECTS leaving a stale guest pointer or nonzero +0x28 scratch on either arrival that
        // writes target := 0. Starting with a live target prevents the full loop hiding a missing clear.
        [Theory]
        [InlineData(9, 59)] [InlineData(15, 46)]
        public void ExitAndSpawnArrivalsClearBothUsesOfTheTargetField(int purpose, int state)
        {
            var g = NewGuard(); var w = new GuardWorld();
            g.Dispatch(Guest(), w); g.GateDirection = 1; g.Staff.Purpose = (StaffPurpose)purpose;
            g.Staff.PushState(GuardStates.WalkToDestination); g.Arrive(w);
            State(g, state); Assert.Null(g.Culprit); Assert.False(g.Staff.HasTarget);
            Assert.Equal(0, g.GateDirection);
            // Arrival 15 increments the first counter (0x80097C7C -> 0x8005996C); arrival 9 does not.
            Assert.Equal(purpose == 15 ? 1 : 0, w.Counter80103950);
            Assert.Equal(0, w.Counter80103954);
        }

        // REJECTS despawning a guard or requesting a nonexistent exit when state 39 has nowhere to go.
        [Fact]
        public void NoExitsReturnsToIdleWithoutAPathOrPenalty()
        {
            var g = NewGuard(); var w = new GuardWorld { HasExits = false };
            g.Staff.PushState(GuardStates.ToExitPoint); g.Tick(w);
            State(g, 0); Assert.Empty(w.Calls); Assert.Equal(50, g.Staff.Morale);
        }

        // REJECTS pushing a walk after all five post candidates fail or inventing a fallback state.
        [Fact]
        public void PostSearchWithoutAPathTileStaysAt55()
        {
            var g = NewGuard(); var w = new GuardWorld { PostPath = false };
            g.Staff.SetState(GuardStates.TakePost); g.Tick(w);
            State(g, 55); Assert.Equal(StaffPurpose.None, g.Staff.Purpose);
            Assert.Equal((5, 3, 1, 5), w.PostSearch); Assert.Equal((0x21, 0), w.PathFlags);
        }

        // REJECTS using a message id as a purpose, infinite retry, wrong retry flags, or pushing
        // path-ready/gate-admission states. A failed retry goes Idle with no new path request.
        [Fact]
        public void RandomExitFailureRetriesExactlyOnce()
        {
            var g = NewGuard(); var w = new GuardWorld();
            g.Staff.Purpose = (StaffPurpose)9; g.Staff.PushState(StaffState.Walking);
            g.OnMessage(w, 2); HasPath(g, w, "random-exit", 9, 0x23);
            Assert.True(g.ExitPathRetried);
            w.Calls.Clear(); g.OnMessage(w, 2); State(g, 0); Assert.Empty(w.Calls);
            Assert.Equal(50, g.Staff.Morale);
            g.Staff.PushState(StaffState.Walking); g.OnMessage(w, 1); State(g, 3);
            Assert.False(g.ExitPathRetried);
            g.Staff.PushState(StaffState.Walking); g.OnMessage(w, 9); State(g, 47);
        }

        // REJECTS blanket failure handling: post retries state 55, chase gives up without the timeout
        // charge, and shared purposes retain the staff base's distinct answers.
        //
        // ⚠ PURPOSE 8 ENDS IN 13, NOT 0. §3.4 says "8 -> 0" and stops one call short: 0x800979B8 clears
        // the culprit and sets 0, then falls into the person base at 0x800942D8, which reads the purpose
        // again and sets 13 for anything that is not 1 or 5. The 0 is real and is immediately overwritten.
        [Theory]
        [InlineData(21, 55)] [InlineData(8, 13)] [InlineData(5, 0)]
        [InlineData(1, 5)] [InlineData(17, 13)] [InlineData(0, 13)]
        public void PathFailureRoutesByPurpose(int purpose, int state)
        {
            var g = NewGuard(); var w = new GuardWorld();
            g.Staff.Purpose = (StaffPurpose)purpose; g.Staff.PushState(StaffState.Walking);
            g.OnMessage(w, 2); State(g, state); Assert.Empty(w.Calls);
            Assert.Equal(50, g.Staff.Morale); Assert.False(g.ExitPathRetried);
        }

        // REJECTS treating an unknown message (including visitor-only message 10) as a path failure.
        [Theory]
        [InlineData(0)] [InlineData(4)] [InlineData(10)]
        public void UnhandledMessagesDoNotChangeTheGuard(int message)
        {
            var g = NewGuard(); var w = new GuardWorld();
            g.Staff.Purpose = (StaffPurpose)9; g.Staff.PushState(GuardStates.AtGate);
            g.OnMessage(w, message); State(g, 46, 1); Assert.Empty(w.Calls);
        }

        // REJECTS routing an unfinished walk as an arrival or losing the base's four-tick fatigue.
        [Theory]
        [InlineData(4, 21)] [InlineData(5, 20)]
        public void UnfinishedWalkUsesSharedTirednessOnly(int tick, int tiredness)
        {
            var g = NewGuard(); var w = new GuardWorld { NowTick = tick };
            g.Staff.Purpose = (StaffPurpose)14; g.Staff.SetState(GuardStates.WalkToDestination);
            g.Arrive(w, stillWalking: true); State(g, 2);
            Assert.Equal(tiredness, g.Staff.Tiredness); Assert.Equal(0, w.Counter80103950);
            Assert.Equal(0, g.GateDirection); Assert.Empty(w.Calls);
        }

        // REJECTS dropping shared arrival purposes when the guard overrides slot 35.
        [Theory]
        [InlineData(1, 0)] [InlineData(5, 15)] [InlineData(17, 50)]
        public void SharedArrivalsStillReachTheirBaseStates(int purpose, int state)
        {
            var g = NewGuard(); g.Staff.Purpose = (StaffPurpose)purpose;
            g.Staff.PushState(GuardStates.WalkToDestination); g.Arrive(new GuardWorld()); State(g, state);
        }

        // REJECTS bypassing stat clamps for either rewards or the two different abort charges.
        [Fact]
        public void CatchAndAbortStatsClampAtBothEnds()
        {
            var g = NewGuard(); var w = new GuardWorld { SameTile = true };
            g.Staff.Morale = 98; g.Staff.Tiredness = 99;
            g.Dispatch(Guest(), w); g.Tick(w);
            Assert.Equal(100, g.Staff.Morale); Assert.Equal(100, g.Staff.Tiredness);
            g.Staff.Morale = 1; g.Dispatch(Guest(18), w); g.Tick(w); Assert.Equal(0, g.Staff.Morale);
            g.Staff.Morale = 1; w.Exists = false; g.Dispatch(Guest(), w); g.Tick(w); Assert.Equal(0, g.Staff.Morale);
        }
    }
}
