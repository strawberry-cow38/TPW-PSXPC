using System;

namespace TPW.Sim
{
    /// <summary>The park's objects and positions for states 28/29 (behaviour.md §2.8).
    /// Allocation and lifetime belong to the park; guest decisions and random draws belong to the sim.</summary>
    public interface IVisitorActivityWorld
    {
        long NowTick { get; }
        /// <summary>READ: signed 8.8 position, P+0x18/P+0x1A (0x80093D44).</summary>
        (int X, int Y) Position(Visitor guest);
        /// <summary>The staff member's signed 8.8 position. Watching uses the whole-tile part.</summary>
        (int X, int Y) Position(StaffMember entertainer);
        /// <summary>READ: 0x800514E0, null on allocation failure. The mode suppression check belongs
        /// to this allocator (0x800514F8..508), not to the guest's recovery.</summary>
        object TryAllocateLitter();
        /// <summary>Place the allocated object and set its obj+0x1C kind (0x800665FC, 0x8006694C).</summary>
        void PlaceLitter(object litter, int x, int y, int kind);
    }

    /// <summary>READ: vomiting and watching, 0x80090C00 / 0x80090A50. Animation meanings are
    /// GUESS-medium (§1); the numeric writes, deadlines and stat changes are READ.</summary>
    public static class VisitorActivity
    {
        /// <summary>READ: Idle's state-29 entry write, §2.1 / 0x8008D620..630.</summary>
        public const int VomitAnimation = 12;
        public const int WatchAnimation = 2;
        public const int WatchBaseTicks = 300;
        public const int WatchSkillTicks = 60;
        public const int WatchSkillMask = 7;
        public const int WatchHappiness = 5;
        public const int WatchCooldownTicks = 900;
        /// <summary>READ refinement: rand(200)-100 per axis, 0x80066608..54. The report's ±0.4
        /// tile is rounded prose: the actual 8.8 offsets are -100..99, not symmetric endpoints.</summary>
        public const int LitterOffsetBound = 200;
        public const int LitterOffsetSubtract = 100;
        public const int VomitKind = 0x9E;

        /// <summary>READ: true when state 29 finishes. Equality still waits (0x80090C20).</summary>
        public static bool Vomit(Visitor guest, IVisitorActivityWorld world, IRandomSource rng)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (world.NowTick <= guest.WaitUntil) return false;

            var litter = world.TryAllocateLitter();
            if (litter != null)
            {
                var position = world.Position(guest);
                int x = unchecked((short)(position.X + rng.Next(LitterOffsetBound) - LitterOffsetSubtract));
                int y = unchecked((short)(position.Y + rng.Next(LitterOffsetBound) - LitterOffsetSubtract));
                world.PlaceLitter(litter, x, y, VomitKind);
            }
            // ⚠ DO NOT FIX: allocation failure skips the two dice, but still cures the guest.
            // 0x80090C38 branches directly to the stat reset at 0x80090C6C.
            guest.Nausea = 0;
            guest.Animation = VisitorQueue.AnimationIdle;
            guest.SetState(VisitorState.Idle);
            return true;
        }

        /// <summary>READ: true when state 28 finishes. The selected staff member must still exist,
        /// as the original dereferences V+0x4C before checking its state (0x80090AC8..E8).</summary>
        public static bool Watch(Visitor guest, IVisitorActivityWorld world)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            var entertainer = guest.WatchedEntertainer
                ?? throw new InvalidOperationException("State 28 requires its selected entertainer.");
            guest.Animation = WatchAnimation;
            var position = world.Position(guest);
            var target = world.Position(entertainer);
            // READ: slot 10 returns tiles (0x800939D4..EC), not the raw position used for litter.
            guest.Facing = Facing((target.X >> 8) - (position.X >> 8), (target.Y >> 8) - (position.Y >> 8));
            if (entertainer.State == EntertainerStates.Entertaining && world.NowTick <= guest.WaitUntil)
                return false;

            // ⭐ POP RESUMES THE INTERRUPTED JOURNEY. SET would erase it and strand a watching guest.
            guest.PopState();
            guest.Happiness = Stat.Add(guest.Happiness, WatchHappiness);
            guest.Animation = VisitorQueue.AnimationWander;
            guest.WatchedEntertainer = null;
            guest.EntertainerNotBefore = world.NowTick + WatchCooldownTicks;
            return true;
        }

        /// <summary>READ: x takes priority over y, even on a diagonal; coincident positions face 4
        /// (0x80090AFC..B58). State 3 uses the same branches (0x80093320..7C).</summary>
        internal static int Facing(int dx, int dy) => dx < 0 ? 2 : dx > 0 ? 6 : dy > 0 ? 0 : 4;
    }
}
