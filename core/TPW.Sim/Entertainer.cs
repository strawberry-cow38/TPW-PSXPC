using System;
using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>READ: entertainer state numbers (behaviour.md §3.2).</summary>
    public static class EntertainerStates
    {
        public const StaffState Entertaining = (StaffState)12;
        public const StaffState Shocked = (StaffState)32;
    }

    /// <summary>The entertainer's view of the park. Positions and influence objects belong to the host.</summary>
    public interface IEntertainerWorld : IGuardWorld
    {
        /// <summary>READ: this is the held object at 0x80103920; its whole Update is skipped.</summary>
        bool IsHeld(StaffMember staff);
        /// <summary>READ: nearest guest's squared tile distance, or null when there are no guests.</summary>
        int? NearestGuestDistanceSquared(StaffMember staff);
        /// <summary>Release E+0x4C if present; a missing influence object is harmless.</summary>
        void ReleaseInfluence(StaffMember staff);
        /// <summary>Allocate the influence object at this member's current position (0x800961D8).</summary>
        void PlaceInfluence(StaffMember staff, int flag);
        /// <summary>Guards in list 0x800536D8 order, with Manhattan tile distances from this member.
        /// READ refinement of §3.2: abs(dx) + abs(dy), 0x80095F8C..0x80095FAC. The sim selects the
        /// nearest available guard; equal distances keep the first list entry.</summary>
        IEnumerable<(Guard guard, int distanceTiles)> GuardsWithDistances(StaffMember staff);
    }

    /// <summary>READ: entertainer handlers, behaviour.md §3.2. The member supplies the shared staff
    /// state and stats; this object holds only the entertainer's additional fields.
    /// "Pelt" is GUESS-high in §2.1; the morale changes, culprit and pushed state are READ.
    /// "Morale" and "tiredness" are GUESS-high field names (§3 introduction); their arithmetic is READ.</summary>
    public sealed class Entertainer
    {
        public Entertainer(StaffMember staff) => Staff = staff ?? throw new ArgumentNullException(nameof(staff));
        public StaffMember Staff { get; }
        /// <summary>READ: the guest recorded at E+0x28 by 0x80095DCC.</summary>
        public Visitor Culprit { get; private set; }
        /// <summary>READ: raw P+0x08, compared with 3 during performance. Its meaning is NOT ESTABLISHED;
        /// it is deliberately not interpreted as the staff kind, state, skill or animation.</summary>
        public int Person08 { get; set; }

        public const int IdleRollBound = 3;
        public const int GuestDistanceSquaredLimit = 2;
        public const int InfluenceFlag = 2;
        public const int PerformanceTicks = 600;
        public const int PerformancePerson08 = 3;
        public const int PerformanceTiredness = 2;
        public const int PerformanceMorale = 1;
        public const int ShockRollBound = 5;
        public const int ShockTickMultiplier = 60;
        public const int PeltMorale = -5;
        public const int ShockMoralePerTick = -10;
        public const int NoGuardMorale = -5;
        public const int GuardRangeTiles = 7;

        bool GuestAdjacent(IEntertainerWorld world)
            => world.NearestGuestDistanceSquared(Staff) < GuestDistanceSquaredLimit;

        /// <summary>READ: class Update, 0x80095C5C. False means held: the caller must also skip the
        /// shared Staff/Person update. Otherwise the host runs that shared update after this switch,
        /// as it does for the existing mechanic/handyman handlers.</summary>
        public bool Tick(IEntertainerWorld world, IRandomSource rng)
        {
            if (world.IsHeld(Staff)) return false;
            switch (Staff.State)
            {
                case StaffState.Idle: Idle(world, rng); break;
                case EntertainerStates.Entertaining: Perform(world); break;
                case EntertainerStates.Shocked: Shock(world); break;
            }
            return true;
        }

        /// <summary>READ: Idle, 0x80095A78. The shared slot-51 check remains StaffBase.IdleCheck.</summary>
        public void Idle(IEntertainerWorld world, IRandomSource rng)
        {
            world.ReleaseInfluence(Staff);
            if (world.IsTypeOnStrike(Staff.Kind)) return;
            if (rng.Next(IdleRollBound) == 0 && GuestAdjacent(world)
                && Staff.State != EntertainerStates.Entertaining)
            {
                Staff.BusyUntil = world.NowTick; // E+0x2C is a START time in state 12.
                world.PlaceInfluence(Staff, InfluenceFlag);
                Staff.SetState(EntertainerStates.Entertaining);
            }
            else Staff.SetState(StaffState.Patrolling);
        }

        /// <summary>Report READ: state 12, 0x80095AFC. The audience can keep a performance going beyond
        /// 600 ticks under §3.2's rule.
        /// ⚠ SOURCE DISAGREEMENT: the local reference at 0x80095B68..0x80095B94 exits when the clock
        /// expires OR the audience disappears. This handler follows the requested report's AND rule;
        /// the two readings are not equivalent.</summary>
        public void Perform(IEntertainerWorld world)
        {
            // ⚠ DO NOT FIX: the raw P+0x08 test and ODD ticks are both in the traced handler. This is
            // not the shared walking/rest cadence and is not a test of the entertainer's state.
            if (Person08 == PerformancePerson08 && (world.NowTick & 1) != 0)
            {
                Staff.Tiredness = Stat.Add(Staff.Tiredness, PerformanceTiredness);
                Staff.Morale = Stat.Add(Staff.Morale, PerformanceMorale);
            }
            if (world.NowTick > Staff.BusyUntil + PerformanceTicks && !GuestAdjacent(world))
                Staff.SetState(StaffState.Idle);
        }

        /// <summary>READ: 0x80095DCC. The guest's own state does not change.</summary>
        public void Pelted(Visitor culprit, IEntertainerWorld world, IRandomSource rng)
        {
            Staff.Morale = Stat.Add(Staff.Morale, PeltMorale);
            Culprit = culprit;
            // ⭐ THE SAME E+0x2C IS REUSED. Pop restores the state stack, not the old performance clock.
            Staff.BusyUntil = world.NowTick + rng.Next(ShockRollBound) * ShockTickMultiplier;
            Staff.PushState(EntertainerStates.Shocked);
        }

        /// <summary>READ: state 32, 0x80095E7C. Dispatch uses the guard's single availability rule.</summary>
        public void Shock(IEntertainerWorld world)
        {
            // ⚠ DO NOT FIX: -10 EVERY tick, including the tick that dispatches a guard. Moving this
            // to entry or putting it on a slower cadence makes the original's steep penalty disappear.
            Staff.Morale = Stat.Add(Staff.Morale, ShockMoralePerTick);
            if (world.NowTick <= Staff.BusyUntil) return;
            Guard chosen = null;
            int nearestDistance = GuardRangeTiles;
            foreach (var candidate in world.GuardsWithDistances(Staff))
            {
                if (!Guard.NotBusy(candidate.guard.Staff)) continue;
                // READ: strict improvement at 0x80095FB0, strict distance < 7 at 0x80095FDC.
                if (candidate.distanceTiles >= nearestDistance) continue;
                chosen = candidate.guard;
                nearestDistance = candidate.distanceTiles;
            }
            if (chosen != null) chosen.Dispatch(Culprit, world);
            else Staff.Morale = Stat.Add(Staff.Morale, NoGuardMorale);
            Staff.PopState();
        }
    }
}
