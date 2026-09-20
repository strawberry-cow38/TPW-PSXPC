using System;
using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>The park services used by the litter allocator (0x800514E0 / 0x80051A94).
    /// The host owns this pool, the shared object-id sequence and the drawable-object list.</summary>
    public interface ILitterWorld
    {
        /// <summary>READ: 0x80059A9C != 0 refuses allocation before touching the pool or dice.</summary>
        bool LitterSuppressed { get; }
        /// <summary>READ: 0x80099D58 takes and increments the shared word at 0x80103284.</summary>
        int TakeObjectId();
        void RegisterLitter(Litter piece);
        void UnregisterLitter(Litter piece);
    }

    /// <summary>READ: one 0x24-byte pool object, type 13 (0x800669B4). The outer pointer's +0/+4
    /// are pool links, +8/+12 drawable-list links, +16 the shared id, +20 the vtable, +24/+26 signed
    /// 8.8 coordinates, +28 the sprite id and +32 the claimant. No tile, age or dirt counter.
    /// See findings/litter.md. "Vomit" is the established state-29 kind; the six other pictures'
    /// individual identities are NOT ESTABLISHED, so they keep their sprite ids.</summary>
    public sealed class Litter
    {
        internal Litter() { }
        public int Id { get; internal set; }
        public short X { get; private set; }
        public short Y { get; private set; }
        public int SpriteId { get; private set; }
        public StaffMember ClaimedBy { get; internal set; }
        /// <summary>READ: 0x80066958 compares the sprite word, not a separate sickness flag.</summary>
        public bool IsVomit => SpriteId == VisitorActivity.VomitKind;

        internal void Initialise(int id, int sprite)
        {
            Id = id;
            SpriteId = sprite;
            ClaimedBy = null;
            // READ: 0x8006659C does not reset the position. The producer places the piece next.
        }

        /// <summary>READ: halfword stores at 0x80066638/654; state 29 then overwrites +28 with 0x9E.
        /// Use this for IVisitorActivityWorld.PlaceLitter: its coordinates are already scattered.</summary>
        public void Place(int x, int y, int sprite)
        {
            X = unchecked((short)x);
            Y = unchecked((short)y);
            SpriteId = sprite;
        }

        /// <summary>READ: 0x800665FC, x draw then y draw, offsets -100..99 in 8.8 units.</summary>
        public void Scatter(int x, int y, IRandomSource rng)
            => Place(x + rng.Next(VisitorActivity.LitterOffsetBound) - VisitorActivity.LitterOffsetSubtract,
                     y + rng.Next(VisitorActivity.LitterOffsetBound) - VisitorActivity.LitterOffsetSubtract,
                     SpriteId);
    }

    /// <summary>READ: PoolOfLitter at 0x80103878, constructed by 0x8005F5F8. Allocation and return
    /// both insert at the head (0x8005DD20 / 0x8005D284). Newest live piece wins an equal-distance
    /// claim. The host supplies world services; this component owns only its forty pieces.</summary>
    public sealed class LitterPool
    {
        /// <summary>READ: 0x8005F640, forty objects of stride 0x24 in a 0x5B0-byte pool.</summary>
        public const int Capacity = 40;
        /// <summary>READ: statistic switch entry 12 at 0x800DBA28 → 0x80016CFC stores the live count.
        /// This is separate from FeatureStock.ParkDirtiness, statistic 30.</summary>
        public const int CountStatistic = 12;
        /// <summary>The immediate in the binary's own test: `slti v0, v0, 0x2` at 0x800901F4, applied
        /// STRICTLY — `|dx| + |dy| &lt; 2`, which is the guest's own tile and its four edge neighbours.
        /// FIVE tiles, not a 5x5 block and not a nine-tile square.
        ///
        /// ⭐ THREE INDEPENDENT READS, AGAINST THE PROSE. behaviour.md §2.9 says "within 2 tiles" and
        /// this constant used to be inclusive on that basis. Two agents traced 0x800901B8..0x800901F4
        /// separately on 2026-09-20 without sight of each other and both read it strictly; I then read
        /// the same window myself — the two `bgez`/`subu` absolute values, the `addu`, and the `slti`
        /// against 2. The prose is a summary; the instruction is the rule.
        ///
        /// ⚠ THE CONSTANT STAYS 2 AND THE COMPARISON IS `&lt;`, deliberately, so the number here is the
        /// one you can find in the disassembly. Writing it as `&lt;= 1` would be the same behaviour and
        /// would hide where it came from.</summary>
        public const int NearbyRadius = 2;

        // READ: 0x800E14B8, in stored order. In particular A0 precedes 9F.
        static readonly int[] OrdinarySprites = { 0x9A, 0x9B, 0x9C, 0x9D, 0xA0, 0x9F };
        readonly Stack<Litter> free = new();
        readonly List<Litter> live = new();
        public IReadOnlyList<Litter> Pieces { get; }
        public int Count => live.Count;

        public LitterPool()
        {
            Pieces = live.AsReadOnly();
            for (int i = 0; i < Capacity; i++) free.Push(new Litter());
        }

        /// <summary>READ: 0x800514E0. Return null on suppression or exhaustion. Even a future vomit
        /// piece draws its ordinary sprite first (0x800665CC), before the producer overwrites it.</summary>
        public Litter TryAllocate(ILitterWorld world, IRandomSource rng)
        {
            if (world.LitterSuppressed || free.Count == 0) return null;
            var piece = free.Pop();
            int id = world.TakeObjectId();
            piece.Initialise(id, OrdinarySprites[rng.Next(OrdinarySprites.Length)]);
            live.Insert(0, piece);
            world.RegisterLitter(piece);
            return piece;
        }

        /// <summary>Wire IVisitorWorld.DropLitter to this method using the guest's world position.
        /// READ for bin failure (0x8008D9B0); ⚠ DISPUTED for misery: the existing Idle port retains
        /// behaviour.md's litter interpretation, but the binary creates a particle, not this object.
        /// Allocation failure consumes neither the sprite draw nor the two position draws.</summary>
        public Litter Drop(int x, int y, ILitterWorld world, IRandomSource rng)
        {
            var piece = TryAllocate(world, rng);
            if (piece != null) piece.Scatter(x, y, rng);
            return piece;
        }

        /// <summary>READ: 0x80051A94 unregisters and returns the slot. No bank, tile or stat write.
        /// The caller releases the claim first, as the handyman does at 0x80099224..234.</summary>
        public void Delete(Litter piece, ILitterWorld world)
        {
            if (!live.Contains(piece)) throw new InvalidOperationException("Litter is not in this pool.");
            world.UnregisterLitter(piece);
            live.Remove(piece);
            free.Push(piece);
        }

        /// <summary>READ: vtable slot 3 = 0x80066664, just jr ra/nop. ⚠ DO NOT FIX: nothing ages,
        /// fades or disappears by itself. A neglected park can keep all forty slots forever.</summary>
        public void Tick() { }

        /// <summary>READ: 0x80098D44, Manhattan distance after EACH coordinate is reduced to tiles;
        /// skip every claimed piece, even one claimed by this same handyman. No maximum distance.</summary>
        public Litter NearestUnclaimed(int x, int y)
        {
            Litter nearest = null;
            int distance = int.MaxValue;
            foreach (var piece in live)
            {
                int candidate = TileDistance(piece, x, y);
                if (piece.ClaimedBy == null && candidate < distance)
                {
                    nearest = piece;
                    distance = candidate;
                }
            }
            return nearest;
        }

        /// <summary>Wire INeedsWorld.LitterNearby to this. READ: all pieces count, including claimed
        /// ones, and vomit also counts as litter (0x80090198..244), within the strict radius above.</summary>
        public (int Litter, int Vomit) Nearby(int x, int y)
        {
            int litter = 0, vomit = 0;
            foreach (var piece in live)
                if (TileDistance(piece, x, y) < NearbyRadius)
                {
                    litter++;
                    if (piece.IsVomit) vomit++;
                }
            return (litter, vomit);
        }

        static int TileDistance(Litter piece, int x, int y)
            => Math.Abs((piece.X >> 8) - (unchecked((short)x) >> 8))
             + Math.Abs((piece.Y >> 8) - (unchecked((short)y) >> 8));

        /// <summary>READ: 0x80053A10 saves only two bytes: ordinary count, then vomit count.
        /// 0x80053A98 recreates positions on random path/queue tiles when loading; positions and
        /// claims are not saved. This exposes the counts; the host owns the save format.</summary>
        public (byte Rubbish, byte Vomit) SaveCounts()
        {
            byte rubbish = 0, vomit = 0;
            foreach (var piece in live)
                if (piece.IsVomit) vomit++; else rubbish++;
            return (rubbish, vomit);
        }
    }

    /// <summary>The position and path services for 0x80098D44. Implement alongside IHandymanWorld;
    /// its claim/delete/unclaim methods can delegate to HandymanLitter.</summary>
    public interface IHandymanLitterWorld : ILitterWorld
    {
        long NowTick { get; }
        (int X, int Y) Position(StaffMember staff);
        /// <summary>READ: request to the raw litter position, flags (0x11, 0), 0x80098E94..EB4.</summary>
        bool TryPathToLitter(StaffMember staff, int x, int y, int flags, int argument);
    }

    /// <summary>READ: concrete litter side of the existing handyman machine. State changes and job
    /// rewards remain in Handyman; the target here is the real object, not a separate Boolean claim.</summary>
    public static class HandymanLitter
    {
        public static bool TryClaimNearest(StaffMember staff, LitterPool pool, IHandymanLitterWorld world)
        {
            var position = world.Position(staff);
            var piece = pool.NearestUnclaimed(position.X, position.Y);
            if (piece == null) return false;
            staff.Purpose = StaffClassStates.ToLitter;
            staff.TargetLitter = piece;
            staff.HasTarget = true;
            piece.ClaimedBy = staff;
            if (!world.TryPathToLitter(staff, piece.X, piece.Y, 0x11, 0))
            {
                Unclaim(staff);
                return false;
            }
            staff.BusyUntil = world.NowTick;
            return true; // Handyman.Idle pushes 11 after this succeeds.
        }

        /// <summary>READ: failure message 2, 0x80098B80..B88, clears both sides without deletion.</summary>
        public static void Unclaim(StaffMember staff)
        {
            if (staff.TargetLitter != null) staff.TargetLitter.ClaimedBy = null;
            staff.TargetLitter = null;
            staff.HasTarget = false;
        }

        /// <summary>READ: 0x80099224..240, release, delete, then clear the staff target. Deleting a
        /// piece removes its future nearby penalties; it gives no happiness refund to guests.</summary>
        public static void DeleteClaimed(StaffMember staff, LitterPool pool, ILitterWorld world)
        {
            var piece = staff.TargetLitter;
            piece.ClaimedBy = null;
            pool.Delete(piece, world);
            staff.TargetLitter = null;
            staff.HasTarget = false;
        }
    }
}
