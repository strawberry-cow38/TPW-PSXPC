using System;

namespace TPW.Sim
{
    /// <summary>READ: class resource and sheet-269 location (staff.md §3). A null base/block means
    /// a separate animation resource, not a missing value to replace with a guest body.</summary>
    public readonly record struct StaffArt(int Resource, int? SheetBase, int? PeopleBlock);

    /// <summary>READ: slot-45 selection and animation tables, findings/staff.md §3.
    /// This supplies asset indices only; the host owns assets, rendering and the render clock.</summary>
    public static class StaffAppearance
    {
        public const int WalkFrames = 8; // READ: first animation byte in resources 263/264/266/274.

        /// <summary>READ: manager slots 9..13 (0x800972E0 / 0x8009852C / 0x80099590 /
        /// 0x80099C5C / 0x80096064), theme table 0x800E002C, bases 0x800DFFFC.</summary>
        public static StaffArt Art(StaffKind kind, int theme) => kind switch
        {
            StaffKind.Mechanic => new(263, 264, 8),
            StaffKind.Guard => new(264, 308, 9),
            StaffKind.Cleaner => new(266, 352, 10),
            StaffKind.Researcher => new(274, 396, 11),
            StaffKind.Entertainer => new(theme switch
            {
                0 => 403, 1 => 401, 2 => 402, 3 => 404,
                _ => throw new ArgumentOutOfRangeException(nameof(theme)),
            }, null, null),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        /// <summary>READ: 0x80032488..98, 0x8002BB58..68, 0x8002BDA8. Frame index within the
        /// resource, plus horizontal mirroring. Add Art.SheetBase for a uniformed employee.
        /// ⭐ Five stored directions, eight frames each; PeopleSheet's guest-oriented heuristic
        /// assumes the reverse. Entertainers use this index in their separate frame resource.</summary>
        public static (int Frame, bool Mirror) Walk(int personFacing, int cameraFacing, int frame)
        {
            if ((uint)frame >= WalkFrames) throw new ArgumentOutOfRangeException(nameof(frame));
            int facing = (personFacing + cameraFacing) & 7;
            // READ: 0x800337E4..88 reverses U for relative directions BELOW five.
            // ⚠ DO NOT FIX: folding 5..7 to 3..1 does not mean those are the flipped draws.
            bool mirror = facing < 5;
            int storedFacing = mirror ? facing : 8 - facing;
            return (storedFacing * WalkFrames + frame, mirror);
        }
    }
}
