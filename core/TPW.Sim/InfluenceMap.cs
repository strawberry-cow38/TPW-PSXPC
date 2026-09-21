using System;
using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>Host services for the influence list. The component owns all twenty areas;
    /// the host retains the returned handles alongside their entertainer/particle owners.</summary>
    public interface IInfluenceWorld
    {
        /// <summary>Signed 8.8 world coordinates, before tile conversion. READ: Person slot 10
        /// (0x800939B0..A18) converts each signed halfword separately by arithmetic shift 8.</summary>
        (int X, int Y) Position(Visitor guest);
        /// <summary>The same signed 8.8 coordinates for performance entry (0x80095A08..2C).
        /// Movement after entry does not move the returned area.</summary>
        (int X, int Y) Position(StaffMember staff);
        /// <summary>READ: raw unsigned word 0x80103A90, returned by 0x800BDD0C; the emitter
        /// subtracts this >>12 per update (0x8008A82C..850). Supply the engine timestep, not
        /// NowTick or a guessed frames/seconds conversion. Zero freezes this lifetime.</summary>
        uint TimeStep { get; }
    }

    /// <summary>READ: one 0x1C-byte effector. Outer +0/+4 are next/previous pool links;
    /// +8/+12 contain signed tile-coordinate halfwords, +0x10 an unsigned squared radius,
    /// +0x14 flags, +0x18 an unestablished word, written zero by the unpleasant producer only.
    /// Coordinate padding and +0x18 are not read by InfluenceAt. See findings/influence.md.</summary>
    public sealed class InfluenceArea
    {
        internal InfluenceArea() { }
        public short X { get; private set; }
        public short Y { get; private set; }
        public uint RadiusSquared { get; private set; }
        public TileInfluence Flags { get; private set; }

        /// <summary>READ: geometry copy and low-word radius*radius at 0x800961E0..20C /
        /// 0x8008C3D4..400. Inputs are whole tiles. No map clipping or radius validation.</summary>
        public void PlaceTiles(int x, int y, int radius)
        {
            X = unchecked((short)x);
            Y = unchecked((short)y);
            RadiusSquared = unchecked((uint)(radius * radius));
        }

        /// <summary>READ: 0x800961DC / 0x8008C3D0 replace the entire flag word, not OR it.</summary>
        public void SetFlags(TileInfluence flags) => Flags = flags;

        internal bool Covers(int x, int y)
        {
            // READ: 0x800617F8..818 truncates each difference to a halfword; 0x80061500/510
            // reads it signed. 0x80061530..534 compares the squared sum UNSIGNED and INCLUSIVE.
            // ⚠ DO NOT FIX: retain halfword wrapping even outside normal park coordinates.
            int dx = unchecked((short)(X - x));
            int dy = unchecked((short)(Y - y));
            uint distanceSquared = unchecked((uint)(dx * dx + dy * dy));
            return distanceSquared <= RadiusSquared;
        }
    }

    /// <summary>READ: PoolOfEffectors, heap pointer at 0x80103860, created at 0x80050198..1C4.
    /// A list of circles, not a tile grid. The 0x240-byte pool contains a 0x10-byte header and
    /// twenty 0x1C-byte entries (0x80060228..304). No object id, drawable registration, suppression
    /// gate, owner tracking, automatic movement or per-frame clearing. Newest allocation is first.
    /// Bounded READ: the intact effector ownership routes have two flag writers (0x8008C3D0,
    /// 0x800961DC), supplied only 4 and 2. No bit-1 producer or qualifying scenery record.
    /// See findings/influence-bit1.md for the census, controls and limits of this negative.</summary>
    public sealed class InfluenceMap
    {
        /// <summary>READ: 0x80060270 / 0x80060304, shared by both established producers.</summary>
        public const int Capacity = 20;
        /// <summary>READ: a2=1 at 0x80095A2C and 0x8008C318, squared by each setter.</summary>
        public const int ProducerRadius = 1;
        readonly Stack<InfluenceArea> free = new();
        readonly List<InfluenceArea> live = new();
        public IReadOnlyList<InfluenceArea> Areas { get; }
        public int Count => live.Count;

        public InfluenceMap()
        {
            Areas = live.AsReadOnly();
            for (int i = 0; i < Capacity; i++) free.Push(new InfluenceArea());
        }

        /// <summary>READ: allocation 0x80053554 / 0x8005CDE8 returns null on exhaustion;
        /// no eviction. This combines allocation with the producer's geometry/flag writes so no
        /// uninitialised payload is exposed. Release itself leaves the payload intact, as the binary
        /// does; both known producers overwrite all fields the reader uses before returning.
        /// Pleasant is a synthetic/manual input here: the bounded retail writer census finds no
        /// bit-1 producer. Do not map attraction record flags to it (findings/influence-bit1.md).</summary>
        public InfluenceArea TryCreateTiles(int x, int y, int radius, TileInfluence flags)
        {
            if (free.Count == 0) return null;
            var area = free.Pop();
            area.PlaceTiles(x, y, radius);
            area.SetFlags(flags);
            live.Insert(0, area);
            return area;
        }

        /// <summary>Wire IEntertainerWorld.PlaceInfluence to this and retain the result as E+0x4C.
        /// READ: 0x800959D4..A38 snapshots position, radius 1, flag 2; even a null result does not
        /// prevent entering state 12. Call Release for the matching ReleaseInfluence hook. Binary
        /// removal also occurs on pelting (0x80095E5C), slot 46 (0x800960EC), and slot 57
        /// (0x800962A8). Those host lifecycle hooks are not called automatically by this map.
        /// ⚠ SOURCE DISAGREEMENT: existing Entertainer retains behaviour.md's performance AND
        /// exit rule and pelt summary; this component does not replace those state handlers.</summary>
        public InfluenceArea TryCreateEntertainer(StaffMember staff, IInfluenceWorld world)
        {
            // READ: no position getter on allocation failure, 0x800959DC.
            if (free.Count == 0) return null;
            var position = world.Position(staff);
            return TryCreateTiles(ToTile(position.X), ToTile(position.Y), ProducerRadius,
                                  TileInfluence.Entertainer);
        }

        /// <summary>READ: 0x8005358C / 0x8005CD74 unlinks a live area and returns it to the free
        /// head. ⚠ DO NOT FIX: it does not zero the flags/geometry. Removing one overlapping area
        /// leaves the others effective. Clear the owner's handle after this call; null is harmless
        /// as in the owner guards. Foreign/double releases are rejected as host errors.</summary>
        public void Release(InfluenceArea area)
        {
            if (area == null) return;
            if (!live.Remove(area)) throw new InvalidOperationException("Influence is not in this map.");
            free.Push(area);
        }

        /// <summary>READ: 0x800535B4..668 scans the live list and ORs every covering +0x14.
        /// Inputs are whole tiles; duplicate sources do not multiply their guest effects.</summary>
        public TileInfluence AtTiles(int x, int y)
        {
            TileInfluence result = TileInfluence.None;
            foreach (var area in live)
                if (area.Covers(x, y)) result |= area.Flags;
            return result;
        }

        /// <summary>READ: signed 8.8 position wrapper, 0x800939B0. Convert each coordinate
        /// BEFORE subtracting; the circle test itself uses tile units.</summary>
        public TileInfluence AtPosition(int x, int y) => AtTiles(ToTile(x), ToTile(y));

        /// <summary>Wire INeedsWorld.InfluenceAt to this. READ: 0x8008FED0..EDC calls Person
        /// slot 10 then 0x800535B4. The guest's eight-tick stagger remains in VisitorNeeds.</summary>
        public TileInfluence InfluenceAt(Visitor guest, IInfluenceWorld world)
        {
            var position = world.Position(guest);
            return AtPosition(position.X, position.Y);
        }

        static int ToTile(int coordinate) => unchecked((short)coordinate) >> 8;

        /// <summary>Call ONLY after the host successfully creates descriptor 0x800F79D8's emitter
        /// (0x80089E38..E8C); x/y are its signed 8.8 position. This owns its aura and lifetime,
        /// not a particle renderer/pool. Failed influence allocation is never retried.
        /// ⚠ SOURCE DISAGREEMENT: behaviour.md calls Idle roll 5 litter; VisitorIdle.DropLitter
        /// retains that rule. Do not silently redirect that hook here (findings/influence.md §0).</summary>
        public UnpleasantInfluence CreateUnpleasantParticle(int x, int y)
            => new(this, TryCreateTiles(ToTile(x), ToTile(y), ProducerRadius, TileInfluence.Unpleasant));
    }

    /// <summary>READ: lifetime side of descriptor 0x800F79D8's emitter. Its visual identity is
    /// NOT ESTABLISHED. The area is a fixed snapshot, not a moving cloud or persistent litter.
    /// Host calls Tick once per actual emitter update (0x80089C70), even if offscreen, and Destroy
    /// on an earlier explicit emitter deletion. No seconds conversion or render dependency.</summary>
    public sealed class UnpleasantInfluence
    {
        /// <summary>READ: signed halfword at 0x800F79D8 = 0x168; copied at 0x8008A700..708.</summary>
        public const short InitialLifetime = 360;
        readonly InfluenceMap map;
        public InfluenceArea Area { get; private set; }
        public short Remaining { get; private set; } = InitialLifetime;
        public bool Destroyed { get; private set; }

        internal UnpleasantInfluence(InfluenceMap map, InfluenceArea area)
        { this.map = map; Area = area; }

        public void Tick(IInfluenceWorld world)
        {
            if (Destroyed) return;
            // READ: 0x8008A82C..850. ⚠ DO NOT FIX: short wrap, no fractional accumulation,
            // and zero survives. Visibility gates emission above this block, not this countdown.
            Remaining = unchecked((short)(Remaining - (world.TimeStep >> 12)));
            if (Remaining < 0) Destroy();
        }

        /// <summary>READ: 0x8008A66C calls descriptor+12; 0x8008C348..390 nulls the owner's
        /// +0x30 handle and returns its area immediately, before the next emitter-list removal.</summary>
        public void Destroy()
        {
            if (Destroyed) return;
            Destroyed = true;
            var area = Area;
            Area = null;
            map.Release(area);
        }
    }
}
