using System;

namespace TPW.Sim
{
    /// <summary>Staff states, by the original's numbers (behaviour.md §3.1).</summary>
    public enum StaffState
    {
        Idle = 0,
        /// <summary>READ: state 2, the per-tick walking state whose handler IS slot 35 (behaviour.md §3.1
        /// row "2 (arrival)", and §0 item 2 for the guest). Walking = 11 is the request; once the path is
        /// ready the member lives here until it arrives.</summary>
        WalkToDestination = 2,
        /// <summary>Walking to a destination; arrival is handled by slot 35.</summary>
        Walking = 11,
        /// <summary>Random wander, shared with the visitor machine.</summary>
        RandomWander = 5,
        /// <summary>The path-ready message puts staff here.</summary>
        PathReady = 3,
        Patrolling = 13,
        Striking = 15,
        WalkToStrike = 26,
        GoAndRest = 49,
        Resting = 50,
    }

    /// <summary>Why a staff member is walking somewhere.
    ///
    /// ⚠ SAME FIELD AS THE VISITOR'S, DIFFERENT MEANINGS. Staff and guests both store this in P+0x2C,
    /// but the arrival table is per class (slot 35 is overridden), so staff purpose 1 is "finished
    /// patrolling" where a guest's purpose 1 is something else entirely. Sharing one enum between them
    /// would compile perfectly and be wrong.</summary>
    public enum StaffPurpose
    {
        None = 0,
        /// <summary>Walking a patrol leg. On arrival: back to Idle.</summary>
        Patrol = 1,
        /// <summary>Walking to the strike muster point. On arrival: start striking.</summary>
        Strike = 5,
        /// <summary>Walking to a bench. On arrival: rest.</summary>
        Rest = 17,
        /// <summary>⚠ NOT IDENTIFIED. 0x800955B4 (<see cref="StaffBase.IsCommittedToStrikeOrRest"/>) tests
        /// the purpose byte against 0x32 in every state it does not otherwise decide, but no write of 50
        /// exists in the staff code: a scan of every `sb ..,44()` store in 0x80090000..0x8009B000 finds
        /// only 1, 2, 5 and 17 stored. Carried so the test is ported as READ, not laundered into "false".</summary>
        Unidentified50 = 50,
    }

    /// <summary>What the staff machine needs of the park.</summary>
    public interface IStaffWorld
    {
        long NowTick { get; }
        /// <summary>Is this staff member's whole type out on strike (IsTypeOnStrike, McAi)?</summary>
        bool IsTypeOnStrike(StaffKind kind);
        /// <summary>The patrol rectangle S+0x38..0x3B is non-empty.</summary>
        bool HasPatrolRect(StaffMember staff);
        /// <summary>Up to 10 random tiles inside the patrol rectangle; path to the first that is a path
        /// tile. False when none of the ten worked or the request was refused.</summary>
        bool TryPathIntoPatrolArea(StaffMember staff);
        /// <summary>The building staff muster at when striking exists (0x8005439C).</summary>
        bool StrikeMusterExists { get; }
        /// <summary>Up to 10 random tiles within +/-2 of the muster building.</summary>
        bool TryPathToStrikeMuster(StaffMember staff);
        /// <summary>Nearest bench or staff room. False when there is none.</summary>
        bool TryPathToRest(StaffMember staff);
    }

    /// <summary>One member of staff.</summary>
    public sealed class StaffMember
    {
        public StaffMember(StaffKind kind) { Kind = kind; }

        public StaffKind Kind { get; }
        public StaffState State { get; private set; } = StaffState.Idle;
        public StaffPurpose Purpose { get; set; }

        int _tiredness, _morale = 100;
        /// <summary>S+0x3F, 0..100. Clamped like the visitor stats.</summary>
        public int Tiredness { get => _tiredness; set => _tiredness = Stat.Clamp(value); }
        /// <summary>S+0x40, 0..100. The strike test averages morale against (100 - tiredness).</summary>
        public int Morale { get => _morale; set => _morale = Stat.Clamp(value); }

        /// <summary>S+0x44. Its low three bits index the per-class duration tables.</summary>
        public int Skill { get; set; }

        /// <summary>H+0x2C and friends: the tick a timed job finishes. Per-class states use it as a
        /// deadline, the same way the visitor uses WaitUntil.</summary>
        public long BusyUntil { get; set; }

        /// <summary>Whether this member has somewhere to be.</summary>
        public bool HasTarget { get; set; }

        /// <summary>READ: the handyman's H+0x28 points to the claimed litter's drawable base.
        /// HandymanLitter keeps this and the piece's +0x20 claim in step (findings/litter.md).</summary>
        public Litter TargetLitter { get; internal set; }

        readonly System.Collections.Generic.Stack<StaffState> _stack = new();
        public int StackDepth => _stack.Count;
        public void SetState(StaffState s) { _stack.Clear(); State = s; }
        public void PushState(StaffState s) { _stack.Push(State); State = s; }
        public void PopState() => State = _stack.Count > 0 ? _stack.Pop() : StaffState.Idle;
    }

    /// <summary>The Staff base state machine, 0x80094AC4 (behaviour.md §3.1).
    ///
    /// ⭐ THIS RUNS UNDER EVERY STAFF CLASS. An entertainer, mechanic, guard, researcher and handyman
    /// each have their own switch, and then fall through to THIS - so patrolling, striking, resting and
    /// the tiredness economy are shared by all five. Reimplementing them per class would duplicate this
    /// five times and let them drift.</summary>
    public static class StaffBase
    {
        /// <summary>Above this a staff member in Idle goes looking for a bench.</summary>
        public const int RestAbove = 80;
        /// <summary>Above this, one NOT in Idle loses morale instead.</summary>
        public const int MoraleLossAbove = 90;
        /// <summary>Resting and walking both act on a 4-tick cadence.</summary>
        public const int RestPeriod = 4;
        /// <summary>Morale gained per rest period.</summary>
        public const int RestMorale = 1;
        /// <summary>Tiredness shed per rest period.</summary>
        public const int RestTiredness = 2;

        /// <summary>Slot 51, called from every class's Idle (0x80094698).
        ///
        /// ⚠ IT IS AN ELSE-CHAIN, AND THAT MATTERS AT HIGH TIREDNESS. A member at 95 who is Idle goes
        /// to REST; one at 95 who is not Idle loses morale instead. Writing the two conditions as
        /// independent ifs would have an exhausted idler both rest and lose morale, which is the
        /// opposite of what the original does for the one case where staff recover.</summary>
        public static void IdleCheck(StaffMember staff, IStaffWorld world)
        {
            if (staff == null) throw new ArgumentNullException(nameof(staff));
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (world.IsTypeOnStrike(staff.Kind))
            {
                staff.HasTarget = false;                 // slot 54 aborts whatever it was doing
                staff.SetState(StaffState.WalkToStrike);
                return;
            }
            if (staff.Tiredness > RestAbove && staff.State == StaffState.Idle)
            {
                staff.SetState(StaffState.GoAndRest);
                return;
            }
            if (staff.Tiredness > MoraleLossAbove && world.NowTick % RestPeriod == 0)
                staff.Morale = Stat.Sub(staff.Morale, 1);
        }

        /// <summary>State 13 -- patrolling (0x80095260).</summary>
        public static void Patrol(StaffMember staff, IStaffWorld world)
        {
            // ⚠ NO RECTANGLE AND A REFUSED PATH GO THE SAME WAY: random wander, not Idle. A staff member
            // with nowhere assigned does not stand still, it drifts.
            if (!world.HasPatrolRect(staff) || !world.TryPathIntoPatrolArea(staff))
            {
                staff.SetState(StaffState.RandomWander);
                return;
            }
            staff.Purpose = StaffPurpose.Patrol;
            staff.SetState(StaffState.Walking);
        }

        /// <summary>State 26 -- walking to the strike muster (slot 50, 0x80094378).</summary>
        public static void WalkToStrike(StaffMember staff, IStaffWorld world)
        {
            // With no muster building there is nowhere to go, so the strike starts where they stand.
            if (!world.StrikeMusterExists || !world.TryPathToStrikeMuster(staff))
            {
                staff.SetState(StaffState.Striking);
                return;
            }
            staff.Purpose = StaffPurpose.Strike;
            staff.PushState(StaffState.Walking);          // PUSH: they come back to 26 on arrival
        }

        /// <summary>State 15 -- striking (slot 49, 0x80094510). Stands until the strike is called off.</summary>
        public static void Strike(StaffMember staff, IStaffWorld world)
        {
            if (!world.IsTypeOnStrike(staff.Kind)) staff.SetState(StaffState.Idle);
        }

        /// <summary>State 49 -- go and find somewhere to sit (0x800947CC).</summary>
        public static void GoAndRest(StaffMember staff, IStaffWorld world)
        {
            if (!world.TryPathToRest(staff))
            {
                staff.SetState(StaffState.Patrolling);    // nothing to sit on: back to work
                return;
            }
            staff.Purpose = StaffPurpose.Rest;
            staff.HasTarget = true;
            staff.PushState(StaffState.Walking);
        }

        /// <summary>State 50 -- resting (0x80094A10).
        ///
        /// ⭐ THE ONLY PLACE STAFF RECOVER, and it is asymmetric: morale climbs 1 per 4 ticks while
        /// tiredness falls 2, so a rest repairs exhaustion twice as fast as it repairs mood. A member
        /// worked into the ground leaves the bench rested and still resentful, which is what makes the
        /// strike system bite.</summary>
        public static void Rest(StaffMember staff, IStaffWorld world)
        {
            if (world.NowTick % RestPeriod == 0)
            {
                staff.Morale = Stat.Add(staff.Morale, RestMorale);
                staff.Tiredness = Stat.Sub(staff.Tiredness, RestTiredness);
            }
            if (staff.Tiredness <= 0)
            {
                staff.HasTarget = false;
                staff.SetState(StaffState.Patrolling);
            }
        }

        /// <summary>Slot 35 -- arrival (0x80094590). Also tires the member while it is still walking.</summary>
        public static void Arrive(StaffMember staff, IStaffWorld world, bool stillWalking)
        {
            if (stillWalking)
            {
                // ⚠ EVERY 4 TICKS, NOT A DICE ROLL. Guests tire on a 1-in-10 per walking tick; staff
                // tire on a fixed cadence. Two different rules for the same-looking thing.
                if (world.NowTick % RestPeriod == 0) staff.Tiredness = Stat.Add(staff.Tiredness, 1);
                return;
            }

            switch (staff.Purpose)
            {
                case StaffPurpose.Strike: staff.SetState(StaffState.Striking); break;
                case StaffPurpose.Patrol: staff.SetState(StaffState.Idle); break;
                case StaffPurpose.Rest: staff.SetState(StaffState.Resting); break;
            }
        }

        /// <summary>0x800955B4 -- is this member spoken for by a strike or a rest? READ: true when striking
        /// (15) or walking to the strike (26); true when in the walking states 2/3 for a strike (5) or a
        /// rest (17); otherwise true only for the unidentified purpose 50.
        ///
        /// ⚠ THE PURPOSE TEST ONLY COUNTS IN STATES 2 AND 3. A member in state 11 (walk requested, path not
        /// yet ready) with a strike purpose is NOT committed by this test, so a mechanic's hand-off
        /// (<see cref="Mechanic.CanTakeHandedJob"/>) can pull a colleague off a strike walk in the one tick
        /// between the request and the path arriving. Port of the instructions, not of the intent.</summary>
        public static bool IsCommittedToStrikeOrRest(StaffMember staff)
        {
            if (staff == null) throw new ArgumentNullException(nameof(staff));
            if (staff.State == StaffState.WalkToStrike || staff.State == StaffState.Striking) return true;
            if ((staff.State == StaffState.WalkToDestination || staff.State == StaffState.PathReady)
                && (staff.Purpose == StaffPurpose.Strike || staff.Purpose == StaffPurpose.Rest)) return true;
            return staff.Purpose == StaffPurpose.Unidentified50;
        }

        /// <summary>Slot 40 -- the pathfinder's answer (0x800942D8).
        ///
        /// ⚠ FAILURE IS HANDLED BY PURPOSE, AND THE THREE ANSWERS DIFFER. A failed walk to a strike
        /// gives up and goes Idle; a failed patrol leg wanders instead; anything else returns to
        /// patrolling. A single "go Idle on failure" would leave striking staff milling about.</summary>
        public static void OnPathMessage(StaffMember staff, bool pathFound)
        {
            if (pathFound) { staff.SetState(StaffState.PathReady); return; }

            staff.SetState(staff.Purpose switch
            {
                StaffPurpose.Strike => StaffState.Idle,
                StaffPurpose.Patrol => StaffState.RandomWander,
                _ => StaffState.Patrolling,
            });
        }
    }
}
