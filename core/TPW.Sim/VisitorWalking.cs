using System;

namespace TPW.Sim
{
    /// <summary>READ: state 3 and the Person step called by state 2 (§2.7, 0x800932C8 / 0x8009322C).
    /// A host's IArrivalWorld.StepToNextWaypoint can delegate to Advance; arrival keeps its own purpose
    /// switch. The world owns positions and the shared pool.</summary>
    public static class VisitorWalking
    {
        public const int StepShift = 14;

        public static void Tick(Visitor guest, IVisitorWalkWorld world)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            int head = world.WaypointHead(guest);
            if (head < 0)
            {
                guest.SetState(VisitorState.WalkToDestination);
                return;
            }
            var target = world.Waypoints.Decode(head);
            var position = world.Position(guest);
            int dx = target.X - position.X, dy = target.Y - position.Y;
            guest.Facing = VisitorActivity.Facing(dx, dy);
            int step = (int)(unchecked((uint)(guest.WalkSpeed * world.Timescale)) >> StepShift);
            // READ: independent axis clamps and signed halfword stores (0x800933B8..400).
            // ⚠ DO NOT FIX: diagonal motion gets a full step on BOTH axes; do not normalise it.
            int x = unchecked((short)(position.X + Math.Clamp(dx, -step, step)));
            int y = unchecked((short)(position.Y + Math.Clamp(dy, -step, step)));
            if (!OnMap(world, x >> 8, y >> 8))
            {
                FreeWaypoints(guest, world);
                guest.SetState(VisitorState.Idle);
                return; // READ: an invalid new position is never written (0x80093454..78).
            }
            world.SetPosition(guest, x, y);
            if (x == target.X && y == target.Y) guest.SetState(VisitorState.WalkToDestination);
        }

        /// <summary>READ: called by the existing arrival handler while a head remains. Frees the
        /// reached head, selects its successor, writes animation 13 and SET 3, even at chain end.</summary>
        public static void Advance(Visitor guest, IVisitorWalkWorld world)
        {
            int head = world.WaypointHead(guest);
            int next = world.Waypoints.Next(head);
            world.Waypoints.Free(head);
            world.SetWaypointHead(guest, next);
            guest.Animation = VisitorQueue.AnimationWander;
            guest.SetState(VisitorState.WalkToWaypoint);
        }

        internal static bool OnMap(IVisitorWalkWorld world, int x, int y)
            => x >= 0 && y >= 0 && x < world.MapWidth && y < world.MapHeight;

        internal static void FreeWaypoints(Visitor guest, IVisitorWalkWorld world)
        {
            world.Waypoints.FreeChain(world.WaypointHead(guest));
            world.SetWaypointHead(guest, WaypointPool.NoChain);
        }
    }
}
