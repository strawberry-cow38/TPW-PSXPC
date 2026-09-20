namespace TPW.Sim
{
    /// <summary>The park owns pools, records, positions and money (findings/staff.md §1).</summary>
    public interface IStaffHireWorld
    {
        long TotalDays { get; }
        /// <summary>READ: move a free node into the kind's employed list, capacity five, and select
        /// its record variant (0x80051640..900). Return null if no node is available; the original
        /// caller assumes the hire UI prevented exhaustion. Cancel must reverse this allocation.</summary>
        StaffMember AllocateAndEmploy(StaffKind kind, int variant);
        /// <summary>READ: record+0x14, not a random skill and not the displayed pay grade.</summary>
        int RecruitLevel(StaffKind kind, int variant);
        void SetHireDay(StaffMember staff, long day);
        /// <summary>READ: unassigned rectangle (0,0,-1,-1), 0x800950EC.</summary>
        void ClearPatrolArea(StaffMember staff);
        /// <summary>READ: animation 13 and packed speed 15 are different fields, 0x800941C0.</summary>
        void InitializeAppearance(StaffMember staff, int animation, int packedBaseSpeed);
        /// <summary>READ: P+0x2B bit 0x40. The world moves held staff with its placement cursor.</summary>
        void SetHeld(StaffMember staff, bool held);
        /// <summary>Retains wages.md §3.4's path-placement policy at the held position.
        /// ⚠ DISPUTED: 0x8004D800 also accepts unobstructed ground; see staff.md disagreements.
        /// Cursor snapping is not established by that report; this interface supplies no interval.</summary>
        bool IsPlacementPath(StaffMember staff);
        bool TrySpend(Money amount);
        void FinishPlacement(StaffMember staff);
        /// <summary>READ: unlink employed node, return to free list, release recruit record.</summary>
        void Release(StaffMember staff);
    }

    /// <summary>Hiring from the recruit card through dropping/cancelling (staff.md §1).
    /// ⚠ DISPUTED: wages.md's initial 100 morale / 0 tiredness is retained. The binary constructor
    /// rolls 70+rand(30), then rand(30), at 0x800941E0..0x80094224. No RNG consumption is invented
    /// for the older findings' fixed initialization. Morale/tiredness names are GUESS-high.</summary>
    public static class StaffHiring
    {
        public const int InitialMorale = 100; // wages.md §3.2, disputed above.
        public const int InitialTiredness = 0;
        public const int InitialAnimation = 13; // READ: 0x800941D8..F8.
        public const StaffPurpose InitialPurpose = (StaffPurpose)2; // READ: 0x8009280C..10; meaning unknown.

        /// <summary>READ: allocation/employment precedes placement; skill comes from the selected
        /// record. The world must not defer wage-list membership until TryPlace.</summary>
        public static StaffMember Begin(StaffKind kind, int variant, IStaffHireWorld world)
        {
            var staff = world.AllocateAndEmploy(kind, variant);
            if (staff == null) return null;
            staff.Morale = InitialMorale;
            staff.Tiredness = InitialTiredness;
            staff.Skill = world.RecruitLevel(kind, variant) & 7;
            staff.Purpose = InitialPurpose;
            staff.HasTarget = false;
            staff.SetState(StaffState.Idle);
            world.ClearPatrolArea(staff);
            world.InitializeAppearance(staff, InitialAnimation, StaffMotion.InitialBaseSpeed);
            world.SetHireDay(staff, world.TotalDays);
            world.SetHeld(staff, true);
            return staff;
        }

        /// <summary>READ: confirm 0x8001E720 → 0x8001E6BC. Position is already the held position;
        /// confirming neither chooses the gate nor teleports to another tile.</summary>
        public static bool TryPlace(StaffMember staff, IStaffHireWorld world)
        {
            if (!world.IsPlacementPath(staff)) return false;
            world.SetHeld(staff, false);
            staff.SetState(StaffState.Idle);
            // ⚠ DO NOT FIX: the placer charges zero and ignores TrySpend's return (0x8001E6EC).
            world.TrySpend(Money.Zero);
            world.FinishPlacement(staff);
            return true;
        }

        /// <summary>READ: 0x8001E870; cancellation releases the employee, without a hire refund.</summary>
        public static void Cancel(StaffMember staff, IStaffHireWorld world) => world.Release(staff);
    }
}
