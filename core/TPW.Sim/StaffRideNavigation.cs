using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>READ: mechanic job/leave path inputs, staff.md §4. The host owns the claimed ride.</summary>
    public interface IStaffRidePathWorld
    {
        /// <summary>Ride slot 42, 0x8009D240: origin plus rotated record entrance offset, then ONE
        /// tile outward along (rotation + record entrance facing) &amp; 3. The existing data-layer
        /// EntranceTile method supplies this; EntranceDoor, ExitTile and centre do not.</summary>
        QueueTile EntranceOutside(StaffMember staff);
        /// <summary>A+0x70 queue path in stored order, nearest the door first.</summary>
        IReadOnlyList<QueueTile> QueuePath(StaffMember staff);
        void FreeWaypoints(StaffMember staff);
        /// <summary>8.8 coordinates; false means the pathfinder refused the request.</summary>
        bool TryPath(StaffMember staff, int x, int y, int flags, int secondaryFlags);
    }

    /// <summary>READ: 0x80096E9C / 0x80096F70 / 0x80097044. Usable by an IMechanicWorld adapter
    /// without changing the existing Mechanic controller; no claim or state is owned here.</summary>
    public static class StaffRideNavigation
    {
        /// <summary>READ: repair AND upgrade request the same outside-entrance point, flags (0x11,0).</summary>
        public static bool TryPathToJob(StaffMember staff, IStaffRidePathWorld world)
        {
            world.FreeWaypoints(staff);
            return Request(staff, world, world.EntranceOutside(staff));
        }

        /// <summary>READ: slot 26 → 0x8009D508 → 0x8009FAA8. No explicit free before this request.</summary>
        public static bool TryPathToLeavePoint(StaffMember staff, IStaffRidePathWorld world)
        {
            var path = world.QueuePath(staff);
            // ⚠ DO NOT FIX: an empty array reads the upper bytes of its zero count word, (0,0).
            // The original still requests that tile's centre. It does not fall back to an exit.
            var tile = path.Count == 0 ? new QueueTile(0, 0) : path[path.Count - 1];
            return Request(staff, world, new QueueTile(unchecked((byte)tile.X), unchecked((byte)tile.Y)));
        }

        static bool Request(StaffMember staff, IStaffRidePathWorld world, QueueTile tile)
            => world.TryPath(staff, (tile.X << 8) | 0x80, (tile.Y << 8) + 0x80, 0x11, 0);
    }
}
