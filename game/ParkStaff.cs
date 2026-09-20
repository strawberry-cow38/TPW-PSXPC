using System;
using Godot;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>A member of staff standing in the park: the sim's <see cref="StaffMember"/> on the same
    /// body a guest uses (<see cref="Walker"/>).
    ///
    /// ⭐ THE BODY IS SHARED AND THE MACHINE IS NOT. In the original, staff and guests are both Person:
    /// the same pathfinder, the same waypoint chain, the same eight drawn facings, the same P+0x60 step.
    /// What differs is the state machine hanging off it and, per class, the arrival table - behaviour.md
    /// §3.1 is explicit that staff purpose 1 and guest purpose 1 mean different things even though they
    /// are the same field.</summary>
    sealed class Staffer : Walker
    {
        public StaffMember S;

        /// <summary>⚠ NOT ESTABLISHED, AND BORROWED RATHER THAN INVENTED. behaviour.md reads the shared
        /// step as `speed x timescale >> 14` with speed = P+0x60 in 15..29, and reads the VISITOR's
        /// constructor rolling rand(15)+15 into it (0x8008C6CC). Nothing reads what a STAFF object's
        /// P+0x60 is set to. 15 is the bottom of the game's own range - the speed it pins guests to in a
        /// queue - so a mechanic walks at a real number from the game rather than a made-up one, and it
        /// is the slowest choice in range so nothing downstream is tuned against an optimistic value.
        /// Replace it the moment the staff constructor is read.</summary>
        public const int NotEstablishedWalkSpeed = 15;

        public override int WalkSpeed => NotEstablishedWalkSpeed;
    }

    /// <summary>The park as the shared staff machine reads it (TPW.Sim.StaffBase, behaviour.md §3.1).
    ///
    /// ⚠ EVERY ANSWER HERE IS "NO", AND EACH ONE IS A MISSING FEATURE RATHER THAN A CHOICE. Patrol
    /// rectangles are assigned from a UI the port does not have; the strike system needs wages, which
    /// are not wired; benches and staff rooms are features the park does not yet track. StaffBase is
    /// written so that every one of those falls through to random wander rather than standing still,
    /// which is why a mechanic with nothing to do still moves.</summary>
    sealed class ParkStaffWorld : IStaffWorld
    {
        readonly Func<long> _now;
        public ParkStaffWorld(Func<long> now) => _now = now;

        public long NowTick => _now();

        /// <summary>⚠ NO STRIKES. The strike test averages morale against (100 - tiredness) and is driven
        /// by pay; McAi's wage machinery is not wired, so nothing can ever call one.</summary>
        public bool IsTypeOnStrike(StaffKind kind) => false;

        /// <summary>⚠ NO PATROL AREAS. S+0x38..0x3B is set by dragging a rectangle in the staff panel,
        /// which does not exist yet. StaffBase sends a member with no rectangle to random wander.</summary>
        public bool HasPatrolRect(StaffMember staff) => false;
        public bool TryPathIntoPatrolArea(StaffMember staff) => false;

        /// <summary>⚠ NO MUSTER BUILDING tracked, so a strike would start where they stand. Unreachable
        /// while <see cref="IsTypeOnStrike"/> is false.</summary>
        public bool StrikeMusterExists => false;
        public bool TryPathToStrikeMuster(StaffMember staff) => false;

        /// <summary>⚠ NO BENCHES tracked. A member over the rest threshold therefore goes back to
        /// patrolling instead of recovering - so tiredness only ever climbs, and morale with it. That is
        /// StaffBase's own answer to "nothing to sit on", not a shortcut here, but it does mean staff
        /// fatigue is currently a ratchet.</summary>
        public bool TryPathToRest(StaffMember staff) => false;
    }
}
