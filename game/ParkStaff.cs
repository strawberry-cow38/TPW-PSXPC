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

        /// <summary>READ (findings/staff.md §2), and it is NOT a field. There is no staff P+0x60: a
        /// uniformed staff node is 0x4C bytes and P+0x60/P+0x62 belong to the larger Visitor object.
        /// The walker asks virtual slot 44 instead, and for a mechanic that is a per-SKILL row whose
        /// other column is its repair time — (240,9), (180,12), (120,14), (60,16), (60,18).
        ///
        /// ⭐ SO SPEED IS A CONSEQUENCE OF TRAINING, READ FRESH EVERY CALL. A new mechanic walks at 9,
        /// barely over half the 15 this port had borrowed from the visitor while it was unread, and it
        /// gets faster the moment it is trained rather than at its next hire.</summary>
        public override int WalkSpeed => StaffMotion.Speed(S);
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
        readonly Func<System.Collections.Generic.IReadOnlyList<GuestTarget>> _targets;
        readonly Func<Staffer, int, int, bool> _pathTo;

        public ParkStaffWorld(Func<long> now,
                              Func<System.Collections.Generic.IReadOnlyList<GuestTarget>> targets,
                              Func<Staffer, int, int, bool> pathToTile)
        { _now = now; _targets = targets; _pathTo = pathToTile; }

        /// <summary>The member the next world call is about. Set before each one, the same way
        /// GuestBrain is set per guest, because "nearest" is measured from where THIS one stands.</summary>
        public Staffer Current;

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

        /// <summary>State 49's search (0x800947CC): the NEAREST placed feature whose record +0x2E bit 1
        /// is set (0x80024110 -> 0x8002433C) and whose status byte A+0x6E is non-zero (0x800660DC), then
        /// a path to it.
        ///
        /// ⭐ THE FLAG IS READ AND THE OBJECT NAMES ITSELF. behaviour.md §3.1 marks the target a GUESS
        /// ("a bench/staff room") because it read the flag and not what carries it. Reading the bit out
        /// of all 197 records settles it: bit 1 is on the STAFF ROOM in every one of the four worlds
        /// (entries 32, 109, 197, 353) and on nothing else that appears in more than one. So there is no
        /// bench - the staff room is the thing - and a park with none has staff who can never recover.
        ///
        /// ⚠ DISTANCE IS MEASURED IN TILES, NOT BY THE GAME'S METRIC. 0x800947CC compares a distance it
        /// computes through a vtable call this does not follow; Manhattan on tile centres is the port's
        /// choice, and it can pick a different staff room when two are close to equal.</summary>
        public bool TryPathToRest(StaffMember staff)
        {
            if (Current == null || _targets == null) return false;
            GuestTarget best = null;
            int bestD = int.MaxValue;
            foreach (var t in _targets())
            {
                if (!t.StaffMayRest || !t.Built) continue;
                int d = Math.Abs(t.CentreX - (Current.X >> 8)) + Math.Abs(t.CentreZ - (Current.Z >> 8));
                if (d >= bestD) continue;
                bestD = d; best = t;
            }
            return best != null && _pathTo(Current, best.DoorX, best.DoorZ);
        }
    }
}
