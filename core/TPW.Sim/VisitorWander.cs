using System;
using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>Positions and the shared waypoint pool for the Person walking handlers (§2.7).
    /// Positions use signed 8.8 world units; map dimensions use tiles.</summary>
    public interface IVisitorWalkWorld
    {
        (int X, int Y) Position(Visitor guest);
        void SetPosition(Visitor guest, int x, int y);
        int MapWidth { get; }
        int MapHeight { get; }
        /// <summary>READ: 0x80103A90. No default timescale is invented by the walker.</summary>
        int Timescale { get; }
        WaypointPool Waypoints { get; }
        int WaypointHead(Visitor guest);
        void SetWaypointHead(Visitor guest, int head);
    }

    public interface IWanderWorld : IVisitorWalkWorld
    {
        long NowTick { get; }
        /// <summary>READ: the report's path predicate (0x8004D4EC / 0x8004D558).</summary>
        bool IsPath(int tileX, int tileY);
        /// <summary>READ: outgoing links of the CURRENT tile, +2 (0x80094148).</summary>
        int PathLinks(int tileX, int tileY);
        /// <summary>behaviour.md §2.7: path tile in a ring of rand(5)+4 around the guest.
        /// ⚠ Ring traversal/tie order is NOT ESTABLISHED by that report, so this spatial query belongs
        /// to the world. The binary scans four rays instead; see findings/visitor-rest.md. Returning
        /// a tile requests a direct waypoint to its centre, not a pathfinder request.</summary>
        bool TryFindPathInRing(Visitor guest, int radius, out MapTile tile);
        /// <summary>READ: 0x800EC9F4 request, destination in 8.8; true means accepted, not arrived.</summary>
        bool TryRequestPath(Visitor guest, int x, int y, int flags, int secondaryFlags);
    }

    public enum WanderOutcome { ChangedToRandomWander, WaypointsReady, PathRequested, BackToIdle }

    /// <summary>READ: states 1 and 5 (§2.7). Newly decoded tables and allocation details are recorded
    /// in findings/visitor-rest.md. The report's off-path fallback is retained where the binary differs.</summary>
    public static class VisitorWander
    {
        public const int StepRollBound = 10;
        public const int KeepDirectionRollBound = 100;
        public const int RerollBelow = 20;
        public const int RingRollBound = 5;
        public const int RingBaseRadius = 4;
        public const int CentreRollBound = 20;
        public const int CentreOffset = 10;
        public const int CentreAttempts = 4;
        public const int PathFlags = 3;
        public const int SecondaryPathFlags = 0;

        // READ: signed halfwords at 0x800E3EAC/0x800E3EB0, stride 8, order +x,+y,-x,-y.
        static readonly int[] Dx = { 1, 0, -1, 0 };
        static readonly int[] Dy = { 0, 1, 0, -1 };
        // READ: bytes at 0x80103268, copied at 0x80092E4C..60. These are not 1 << direction.
        static readonly int[] LinkBits = { 4, 16, 64, 1 };
        // READ: s32 rows at 0x800E3ECC, previous direction then candidate direction.
        static readonly int[,] TurnWeights =
        {
            { 100, 40, 10, 40 }, { 40, 100, 40, 10 },
            { 10, 40, 100, 40 }, { 40, 10, 40, 100 },
        };

        public static WanderOutcome Tick(Visitor guest, IWanderWorld world, IRandomSource rng)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (guest.State == VisitorState.Wander)
            {
                // ⚠ DO NOT FIX: READ 0x800934D4 is SET, even though Idle pushed state 1.
                // The stack is discarded; purpose 1 eventually sets Idle without popping this push.
                guest.SetState(VisitorState.RandomWander);
                return WanderOutcome.ChangedToRandomWander;
            }

            var position = world.Position(guest);
            int x = position.X >> 8, y = position.Y >> 8;
            if (!world.IsPath(x, y)) return OffPath(guest, world, rng);

            int steps = rng.Next(StepRollBound);
            int previous = -1;
            var points = new List<(int X, int Y)>();
            for (int step = 0; step < steps; step++)
            {
                var candidates = new List<int>();
                int links = world.PathLinks(x, y);
                for (int direction = 0; direction < Dx.Length; direction++)
                {
                    int nx = x + Dx[direction], ny = y + Dy[direction];
                    if (VisitorWalking.OnMap(world, nx, ny) && world.IsPath(nx, ny)
                        && (links & LinkBits[direction]) != 0)
                        candidates.Add(direction);
                }
                if (candidates.Count == 0) break;
                if (!candidates.Contains(previous) || rng.Next(KeepDirectionRollBound) < RerollBelow)
                {
                    // READ 0x80092FBC: first pick a row with rand(4), even with only one candidate.
                    if (previous < 0) previous = rng.Next(Dx.Length);
                    int total = 0;
                    foreach (int candidate in candidates) total += TurnWeights[previous, candidate];
                    int roll = rng.Next(total);
                    int cumulative = 0;
                    foreach (int candidate in candidates)
                    {
                        cumulative += TurnWeights[previous, candidate];
                        // ⚠ DO NOT FIX: the binary advances while cumulative < roll, not <=.
                        // Equality belongs to the earlier candidate (0x80093038..3C / 74..78).
                        if (cumulative >= roll) { previous = candidate; break; }
                    }
                }
                x += Dx[previous]; y += Dy[previous];
                points.Add((VisitorQueue.Centre(x), VisitorQueue.Centre(y)));
            }
            Install(guest, world, points);
            guest.Purpose = Purpose.Finished;
            guest.SetState(VisitorState.WalkToWaypoint);
            return WanderOutcome.WaypointsReady;
        }

        static WanderOutcome OffPath(Visitor guest, IWanderWorld world, IRandomSource rng)
        {
            int radius = rng.Next(RingRollBound) + RingBaseRadius;
            if (world.TryFindPathInRing(guest, radius, out var tile))
            {
                Install(guest, world, new[] { (VisitorQueue.Centre(tile.X), VisitorQueue.Centre(tile.Y)) });
                if (world.WaypointHead(guest) < 0)
                {
                    guest.SetState(VisitorState.Idle);
                    return WanderOutcome.BackToIdle;
                }
                guest.Purpose = Purpose.Finished;
                guest.SetState(VisitorState.WalkToWaypoint);
                return WanderOutcome.WaypointsReady;
            }

            // ⚠ DO NOT FIX: retain §2.7's four tries around the map centre and Idle on failure.
            // 0x80092A70..B10 instead tries five times, centres BOTH axes on width/2, then makes a
            // grass walk. These disagreements are recorded, not silently substituted for the report.
            for (int attempt = 0; attempt < CentreAttempts; attempt++)
            {
                int x = world.MapWidth / 2 + rng.Next(CentreRollBound) - CentreOffset;
                int y = world.MapHeight / 2 + rng.Next(CentreRollBound) - CentreOffset;
                if (!VisitorWalking.OnMap(world, x, y) || !world.IsPath(x, y)) continue;
                if (!world.TryRequestPath(guest, VisitorQueue.Centre(x), VisitorQueue.Centre(y),
                    PathFlags, SecondaryPathFlags)) continue;
                guest.ExitPathRetried = true;
                guest.Purpose = Purpose.Finished;
                guest.WaitUntil = world.NowTick;
                guest.PushState(VisitorState.WalkToBin);
                return WanderOutcome.PathRequested;
            }
            guest.SetState(VisitorState.Idle);
            return WanderOutcome.BackToIdle;
        }

        static void Install(Visitor guest, IVisitorWalkWorld world, IReadOnlyList<(int X, int Y)> points)
        {
            VisitorWalking.FreeWaypoints(guest, world);
            int head = WaypointPool.NoChain;
            // ⚠ DO NOT FIX: allocate from the destination backwards and keep the suffix if the pool
            // fills (0x80093174..DC). This may omit the nearest steps; it is not an all-or-nothing path.
            for (int i = points.Count - 1; i >= 0; i--)
            {
                int entry = world.Waypoints.Alloc();
                if (entry < 0) break;
                world.Waypoints.Encode(entry, points[i].X, points[i].Y);
                world.Waypoints.SetNext(entry, head);
                head = entry;
            }
            world.SetWaypointHead(guest, head);
        }
    }
}
