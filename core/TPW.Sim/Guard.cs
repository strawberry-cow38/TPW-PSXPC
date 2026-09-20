using System;

namespace TPW.Sim
{
    /// <summary>READ: guard state and purpose numbers (behaviour.md §3.4; walking correction §0).</summary>
    public static class GuardStates
    {
        public const StaffState WalkToDestination = (StaffState)2;
        public const StaffState Chase = (StaffState)33;
        public const StaffState ToExitPoint = (StaffState)39;
        public const StaffState AtGate = (StaffState)46;
        public const StaffState CrossGate = (StaffState)47;
        public const StaffState LeavePark = (StaffState)48;
        /// <summary>GUESS-high label "post near entrance" (states.json 55); the path and state are READ.</summary>
        public const StaffState TakePost = (StaffState)55;
        public const StaffState ToSpawnPoint = (StaffState)59;

        public const StaffPurpose ToCulprit = (StaffPurpose)8;
        public const StaffPurpose ToRandomExit = (StaffPurpose)9;
        public const StaffPurpose ExitPoint = (StaffPurpose)14;
        public const StaffPurpose SpawnPoint = (StaffPurpose)15;
        public const StaffPurpose Gate = (StaffPurpose)16;
        public const StaffPurpose Post = (StaffPurpose)21;
    }

    /// <summary>The guard's view of the park. Requests retain the original flags; the host owns paths.</summary>
    public interface IGuardWorld : IStaffWorld
    {
        bool GuestExists(Visitor guest);
        bool OnGuestTile(StaffMember staff, Visitor guest);
        void SendGuestMessage(Visitor guest, int message);
        void FreeWaypoints(StaffMember staff);
        void SetAnimation(StaffMember staff, int animation);
        void PathToGuest(StaffMember staff, Visitor guest, int flags, int secondaryFlags);
        bool HasExits { get; }
        /// <summary>READ: point index 1 is the exit, 0 is the spawn point (0x800599A4).</summary>
        void PathToParkPoint(StaffMember staff, int pointIndex, int flags, int secondaryFlags);
        void PathToRandomExit(StaffMember staff, int flags, int secondaryFlags);
        /// <summary>READ: replace the path with ONE waypoint at (my x, gate y), 0x80098594.</summary>
        void SetGateWaypoint(StaffMember staff);
        /// <summary>READ: up to the supplied number of random tiles at building 0x8005439C(0,0),
        /// x ± radius, y + min..max; a path tile requests a path. GUESS-high: "post near entrance".
        /// READ refinement, 0x80098090..0x800981D8: sample x then y in 8.8 units, upper bounds exclusive,
        /// clamp to map bounds, stop on an accepted path. False means the search found no accepted path.</summary>
        bool TryPathToPost(StaffMember staff, int attempts, int xRadius, int minY, int maxY,
                           int flags, int secondaryFlags);
        /// <summary>READ: word at 0x80103950, changed by arrivals 14 and 16.
        /// ⚠ Its purpose is NOT ESTABLISHED. Do not relabel it as a guest count or clamp it.</summary>
        int Counter80103950 { get; set; }
        /// <summary>READ: word at 0x80103954, incremented by arrival 16. Its purpose is NOT ESTABLISHED.</summary>
        int Counter80103954 { get; set; }
    }

    /// <summary>READ: guard handlers, behaviour.md §3.4. Call the shared Staff/Person handlers after
    /// the class handlers, as for the mechanic. This object holds the guard's additional fields only.
    /// "Morale" and "tiredness" are GUESS-high field names (§3 introduction); their arithmetic is READ.
    ///
    /// ⭐ THE GATE IS VISITED TWICE: 39 → 46 → 47 → 48 → 59 → 46 → 47 → 55. The guest has already
    /// received message 4 when this starts; the guard still makes the whole trip back to its post.
    ///
    /// ⚠ SOURCE DISAGREEMENTS: §3.4 is the requested specification here. The local reference also
    /// charges -5 for a gone culprit in state 33 (0x80097D94..0x80097DF0), overwrites the chase clock
    /// after an accepted path (0x80097F4C..0x80097F54), and gives no catch reward in the state-3 handler
    /// (0x80097B44..0x80097B84), which also aborts for queues at -2 (0x80097A84..0x80097ABC).
    /// Arrival 15 calls the counter increment too (0x80097C7C → 0x8005996C), and failed purpose 8 clears
    /// its target then delegates to the base (0x800979B8..0x800979E0). The report's stated rules are
    /// retained below rather than silently substituting these conflicting readings.</summary>
    public sealed class Guard
    {
        public Guard(StaffMember staff) => Staff = staff ?? throw new ArgumentNullException(nameof(staff));
        public StaffMember Staff { get; }
        /// <summary>READ: G+0x28 while chasing; cleared when giving up or on arrival 9.</summary>
        public Visitor Culprit { get; private set; }
        /// <summary>READ: the numeric use of +0x28 at the gate: 1 on arrival 14, 0 on arrival 15.</summary>
        public int GateDirection { get; set; }
        /// <summary>READ: retry flag 0x20; path-ready message 1 clears it (0x800978B8..0x800978C0,
        /// a reset omitted from the report's shorthand). The host may supply it when loading state.</summary>
        public bool ExitPathRetried { get; set; }

        public const int ChaseTicks = 3600;
        public const int CatchMessage = 4;
        public const int CatchMorale = 10;
        public const int CatchTiredness = 3;
        public const int GiveUpMorale = -5;
        public const int GoneMorale = -2;
        public const int IdleAnimation = 15;
        public const int ChaseAnimation = 30;
        // READ: pathfinder.md Appendix call-site table, including flags omitted from behaviour.md.
        public const int ChasePathFlags = 0x11;
        public const int GatePathFlags = 0x21;
        public const int OutsidePathFlags = 1;
        public const int RetryPathFlags = 0x23;
        public const int SecondaryPathFlags = 0;
        public const int PostAttempts = 5;
        public const int PostXRadius = 3;
        public const int PostMinY = 1;
        public const int PostMaxY = 5;

        /// <summary>READ: 0x8009843C. The report's "walking" purpose test is ONLY state 3
        /// (0x80098458..0x80098460). An old purpose on an idle guard does not make it busy.
        /// Used by the entertainer, not copied into its search.</summary>
        public static bool NotBusy(StaffMember staff)
        {
            if (staff.State == GuardStates.Chase || staff.State == GuardStates.ToExitPoint
                || staff.State == StaffState.Striking) return false;
            // ⚠ DO NOT FIX: states 2 and 11 remain dispatchable even with purposes 5/8/9.
            bool walking = staff.State == StaffState.PathReady;
            return !walking || (staff.Purpose != StaffPurpose.Strike
                && staff.Purpose != GuardStates.ToCulprit && staff.Purpose != GuardStates.ToRandomExit);
        }

        /// <summary>READ: 0x80098494, called by the entertainer when its shock deadline passes.</summary>
        public void Dispatch(Visitor culprit, IGuardWorld world)
        {
            world.FreeWaypoints(Staff);
            Staff.BusyUntil = world.NowTick + ChaseTicks;
            Culprit = culprit;
            Staff.HasTarget = true;
            world.SetAnimation(Staff, ChaseAnimation);
            Staff.SetState(GuardStates.Chase);
        }

        /// <summary>READ: the class-specific Update switch, 0x80098290. State 46 deliberately waits.
        /// Movement is driven separately: WalkStep before a state-3 step, Arrive in state 2.</summary>
        public void Tick(IGuardWorld world)
        {
            switch (Staff.State)
            {
                case StaffState.Idle: Idle(world); break;
                case GuardStates.Chase: Chase(world); break;
                case GuardStates.ToExitPoint: GoToExitPoint(world); break;
                case GuardStates.CrossGate: CrossGate(world); break;
                case GuardStates.LeavePark: LeavePark(world); break;
                case GuardStates.ToSpawnPoint: GoToSpawnPoint(world); break;
                case GuardStates.TakePost: TakePost(world); break;
            }
        }

        /// <summary>READ: Idle, 0x800981F8. Shared slot 51 is StaffBase.IdleCheck.</summary>
        public void Idle(IGuardWorld world)
        {
            world.SetAnimation(Staff, IdleAnimation);
            if (!world.IsTypeOnStrike(Staff.Kind)) Staff.SetState(StaffState.Patrolling);
        }

        void Abandon(int morale)
        {
            Staff.Morale = Stat.Add(Staff.Morale, morale);
            Culprit = null;
            Staff.HasTarget = false;
            Staff.SetState(StaffState.Idle);
        }

        bool Catch(IGuardWorld world)
        {
            if (!world.OnGuestTile(Staff, Culprit)) return false;
            world.SendGuestMessage(Culprit, CatchMessage);
            Staff.SetState(GuardStates.ToExitPoint);
            Staff.Morale = Stat.Add(Staff.Morale, CatchMorale);
            Staff.Tiredness = Stat.Add(Staff.Tiredness, CatchTiredness);
            return true;
        }

        /// <summary>READ: state 33, 0x80097D5C. Queue states are exactly 18/19/20 (0x800923A8).</summary>
        public void Chase(IGuardWorld world)
        {
            if (Culprit == null || !world.GuestExists(Culprit)) { Abandon(GoneMorale); return; }
            if ((int)Culprit.State == 18 || (int)Culprit.State == 19 || (int)Culprit.State == 20
                || world.NowTick > Staff.BusyUntil)
            {
                Abandon(GiveUpMorale);
                return;
            }
            if (Catch(world)) return;
            Staff.Purpose = GuardStates.ToCulprit;
            world.PathToGuest(Staff, Culprit, ChasePathFlags, SecondaryPathFlags);
            Staff.PushState(StaffState.Walking);
        }

        /// <summary>READ: state-3 override, 0x80097A1C. True means the chase ended; otherwise the host
        /// performs the ordinary waypoint step. Only purpose 8 does this catch/gone check.</summary>
        public bool WalkStep(IGuardWorld world)
        {
            if (Staff.Purpose != GuardStates.ToCulprit) return false;
            if (Culprit == null || !world.GuestExists(Culprit)) { Abandon(GoneMorale); return true; }
            return Catch(world);
        }

        /// <summary>READ: state 39, 0x80097F8C.</summary>
        public void GoToExitPoint(IGuardWorld world)
        {
            if (!world.HasExits) { Staff.SetState(StaffState.Idle); return; }
            Staff.Purpose = GuardStates.ExitPoint;
            world.PathToParkPoint(Staff, 1, GatePathFlags, SecondaryPathFlags);
            Staff.SetState(StaffState.Walking);
        }

        /// <summary>READ: state 47, 0x80098594.</summary>
        public void CrossGate(IGuardWorld world)
        {
            world.SetGateWaypoint(Staff);
            Staff.Purpose = GuardStates.Gate;
            Staff.SetState(StaffState.PathReady);
        }

        /// <summary>READ: state 48, 0x80098680.</summary>
        public void LeavePark(IGuardWorld world)
        {
            Staff.Purpose = GuardStates.ToRandomExit;
            world.PathToRandomExit(Staff, OutsidePathFlags, SecondaryPathFlags);
            Staff.SetState(StaffState.Walking);
        }

        /// <summary>READ: state 59, 0x80098728.</summary>
        public void GoToSpawnPoint(IGuardWorld world)
        {
            Staff.Purpose = GuardStates.SpawnPoint;
            world.PathToParkPoint(Staff, 0, OutsidePathFlags, SecondaryPathFlags);
            Staff.SetState(StaffState.Walking);
        }

        /// <summary>READ: state 55, 0x8009805C. The host owns the random tile search and path acceptance,
        /// as it does for StaffBase.Patrol; the search contract is on IGuardWorld.TryPathToPost.</summary>
        public void TakePost(IGuardWorld world)
        {
            if (!world.TryPathToPost(Staff, PostAttempts, PostXRadius, PostMinY, PostMaxY,
                                     GatePathFlags, SecondaryPathFlags)) return;
            Staff.Purpose = GuardStates.Post;
            Staff.PushState(StaffState.Walking);
        }

        /// <summary>READ: slot 35, arrivals by purpose (§3.4), with shared walking tiredness (§3.1).</summary>
        public void Arrive(IGuardWorld world, bool stillWalking = false)
        {
            if (stillWalking) { StaffBase.Arrive(Staff, world, true); return; }
            switch (Staff.Purpose)
            {
                case GuardStates.ToCulprit: Staff.SetState(GuardStates.Chase); break;
                case GuardStates.ExitPoint:
                    GateDirection = 1;
                    Culprit = null; // +0x28 is now a numeric scratch, not the caught guest pointer.
                    Staff.SetState(GuardStates.AtGate);
                    world.Counter80103950++;
                    break;
                case GuardStates.Gate:
                    Staff.SetState(GateDirection == 0 ? GuardStates.TakePost : GuardStates.LeavePark);
                    world.Counter80103950--;
                    world.Counter80103954++;
                    break;
                case GuardStates.ToRandomExit:
                    Culprit = null;
                    GateDirection = 0;
                    Staff.HasTarget = false;
                    Staff.SetState(GuardStates.ToSpawnPoint);
                    break;
                case GuardStates.SpawnPoint:
                    GateDirection = 0;
                    Culprit = null;
                    Staff.HasTarget = false;
                    Staff.SetState(GuardStates.AtGate);
                    // ⚠ DO NOT FIX: §3.4 gives NO counter increment on arrival 15. Only arrival 14
                    // increments it, although BOTH passes through arrival 16 decrement it.
                    break;
                case GuardStates.Post: Staff.SetState(StaffState.Idle); break;
                default: StaffBase.Arrive(Staff, world, false); break;
            }
        }

        /// <summary>READ: 0x80097828. The id 9 (gate admission) and PURPOSE 9 (walk to a random exit)
        /// are different dispatch keys. Unknown message ids are left to the host.</summary>
        public void OnMessage(IGuardWorld world, int message)
        {
            if (message == 1)
            {
                Staff.SetState(StaffState.PathReady);
                ExitPathRetried = false;
                return;
            }
            if (message == 9) { Staff.SetState(GuardStates.CrossGate); return; }
            if (message != 2) return;
            switch (Staff.Purpose)
            {
                case GuardStates.ToRandomExit:
                    if (ExitPathRetried) { Staff.SetState(StaffState.Idle); return; }
                    ExitPathRetried = true;
                    world.PathToRandomExit(Staff, RetryPathFlags, SecondaryPathFlags);
                    Staff.SetState(StaffState.Walking);
                    break;
                case GuardStates.Post: Staff.SetState(GuardStates.TakePost); break;
                case GuardStates.ToCulprit: Staff.SetState(StaffState.Idle); break;
                default: StaffBase.OnPathMessage(Staff, false); break;
            }
        }
    }
}
