using System;

namespace TPW.Sim
{
    /// <summary>READ: staff virtual slot 44, findings/staff.md §2.
    /// ⭐ Staff do not inherit the visitor's speed roll or its +0x60/+0x62 fields. Two classes
    /// look up speed by current skill; the others read five packed bits initialized to 15.</summary>
    public static class StaffMotion
    {
        public const int InitialBaseSpeed = 15; // READ: 0x800942A8..AC.

        /// <summary>READ: 0x800972BC (mechanic), 0x80098A68 (cleaner), 0x8009571C (others).
        /// packedBaseSpeed is P+0x3C bits 3..7, supplied by the host, not an animation id.
        /// ⚠ Guard.cs retains behaviour.md's disputed animation interpretation of the writes
        /// 15/30. This helper does not silently reinterpret that controller's calls.</summary>
        public static int Speed(StaffMember staff, int packedBaseSpeed = InitialBaseSpeed)
        {
            int[] table = staff.Kind switch
            {
                // The old names remain in the owning files; these consumers settle their meaning.
                StaffKind.Mechanic => Mechanic.UnknownSecondColumn,
                StaffKind.Cleaner => Handyman.UnusedThirdColumn,
                _ => null,
            };
            if (table == null) return packedBaseSpeed & 0x1F;
            int row = staff.Skill & 7;
            // ⚠ NOT ESTABLISHED: speeds for corrupt skill rows 5..7. The binary reads past the
            // table. Refuse to manufacture a speed; this is a managed boundary, not a game clamp.
            if (row >= table.Length) throw new ArgumentOutOfRangeException(nameof(staff.Skill));
            return table[row];
        }

        /// <summary>READ: shared Person step, 0x80093384..B4: low multiply word, logical >>14.
        /// Returns 8.8 displacement for EACH axis; the host keeps the shared walker's independent
        /// clamps. ⚠ DO NOT FIX: diagonals receive the full displacement on both axes.</summary>
        public static int Step(StaffMember staff, int timescale, int packedBaseSpeed = InitialBaseSpeed)
            => (int)(unchecked((uint)(Speed(staff, packedBaseSpeed) * timescale)) >> VisitorWalking.StepShift);
    }
}
