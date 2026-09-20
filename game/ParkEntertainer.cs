using System;
using System.Collections.Generic;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>The park as an entertainer reads it (TPW.Sim.IEntertainerWorld).
    ///
    /// ⚠⚠ THE FIFTH PORTED-BUT-NEVER-CALLED SYSTEM IN ONE DAY, and the one that made three other
    /// pieces of work inert. `Entertainer` — Tick, Idle, Perform, Pelted, Shock — had ZERO call sites
    /// in the game project and `IEntertainerWorld` had no implementation anywhere, so an entertainer
    /// you hired and paid drew a wage and wandered like any other staff member. Measured the same way
    /// the cleaner was: five staff classes exist, and before today only the MECHANIC ran.
    ///
    /// ⭐ IT IS THE PRODUCER THE INFLUENCE MAP WAS WAITING FOR. astra's InfluenceMap ports a pool of
    /// twenty circles and has exactly two established producers: this class's performance aura (bit 2)
    /// and the unpleasant particle. With no entertainer running, the map could only ever be empty, so
    /// wiring `INeedsWorld.InfluenceAt` to it would have been plumbing that provably changed nothing.
    /// It is also the other end of state 28 — a guest watching an entertainer — whose handler had no
    /// caller either until this morning.
    ///
    /// ⚠ MOST OF IGuardWorld IS DELIBERATELY NOT WIRED. The interface is inherited whole, but the
    /// entertainer's own five handlers touch only a corner of it. Everything the entertainer never
    /// calls THROWS rather than returning a plausible zero, because a silent default here is how a
    /// system looks wired and does nothing — which is the exact failure this file exists to fix.</summary>
    sealed class ParkEntertainerWorld : IEntertainerWorld
    {
        readonly ParkStaffWorld _base;
        readonly InfluenceMap _influence;
        readonly Func<IEnumerable<Guest>> _guests;
        readonly Func<StaffMember, (int X, int Z)> _staffTile;

        /// <summary>The aura each entertainer is holding, which the binary keeps at E+0x4C. Kept beside
        /// the StaffMember for the same reason the mechanic's ride and the handyman's bin are: the sim's
        /// type is not widened to carry a host's bookkeeping.</summary>
        readonly Dictionary<StaffMember, InfluenceArea> _aura = new();

        public ParkEntertainerWorld(ParkStaffWorld shared, InfluenceMap influence,
                                    Func<IEnumerable<Guest>> guests,
                                    Func<StaffMember, (int X, int Z)> staffTile)
        { _base = shared; _influence = influence; _guests = guests; _staffTile = staffTile; }

        public Staffer Current { get => _base.Current; set => _base.Current = value; }

        public long NowTick => _base.NowTick;
        public bool IsTypeOnStrike(StaffKind kind) => _base.IsTypeOnStrike(kind);
        public bool HasPatrolRect(StaffMember s) => _base.HasPatrolRect(s);
        public bool TryPathIntoPatrolArea(StaffMember s) => _base.TryPathIntoPatrolArea(s);
        public bool StrikeMusterExists => _base.StrikeMusterExists;
        public bool TryPathToStrikeMuster(StaffMember s) => _base.TryPathToStrikeMuster(s);
        public bool TryPathToRest(StaffMember s) => _base.TryPathToRest(s);

        // ---- what the entertainer's own handlers actually ask for ---------------------------------

        /// <summary>⚠ FALSE, AND IT IS A GAP NOT A MEASURED ZERO. The original skips a staff member's
        /// whole Update while the player is HOLDING it (0x80103920) — the build cursor picking a person
        /// up. The port has no such gesture, so nobody is ever held. Answering true would freeze them.</summary>
        public bool IsHeld(StaffMember staff) => false;

        /// <summary>READ: the nearest guest's SQUARED tile distance, or null with no guests. The
        /// performance test is `&lt; 2`, so it is the four edge neighbours and the entertainer's own
        /// tile — a guest diagonally adjacent is 2 and does NOT keep a show going.</summary>
        public int? NearestGuestDistanceSquared(StaffMember staff)
        {
            var (sx, sz) = _staffTile(staff);
            int best = int.MaxValue;
            foreach (var g in _guests())
            {
                int dx = (g.X >> 8) - sx, dz = (g.Z >> 8) - sz;
                int d = dx * dx + dz * dz;
                if (d < best) best = d;
            }
            return best == int.MaxValue ? null : best;
        }

        /// <summary>READ: 0x800959D4..A38 — snapshot the position, radius 1, flag 2. ⭐ A SNAPSHOT,
        /// NOT A FOLLOWING AURA: an entertainer that walks away leaves its mark where the show was
        /// until Idle releases it. Allocation failure is silent and does NOT stop the performance —
        /// with twenty areas in the whole park, the twenty-first entertainer performs to no effect.</summary>
        public void PlaceInfluence(StaffMember staff, int flag)
        {
            ReleaseInfluence(staff);
            if (_influence.TryCreateEntertainer(staff, Positions) is { } area) _aura[staff] = area;
        }

        /// <summary>Release the held aura. ⚠ A MISSING ONE IS HARMLESS AND THAT IS DELIBERATE: Idle
        /// calls this every single tick, including the many where nothing was ever placed.</summary>
        public void ReleaseInfluence(StaffMember staff)
        {
            if (!_aura.Remove(staff, out var area)) return;
            _influence.Release(area);
        }

        /// <summary>⚠ EMPTY, BECAUSE THERE ARE NO GUARDS IN THE PORT. `Guard` is the sixth system with
        /// no caller — `IGuardWorld` has no implementation and nothing constructs one. So a pelted
        /// entertainer always takes the no-guard branch: −5 more morale and nobody comes. That is the
        /// honest consequence of the gap, and it is why this returns empty rather than throwing: the
        /// sim ASKS for the list on every shock, and an exception there would take the park down for a
        /// missing feature rather than reporting it.</summary>
        public IEnumerable<(Guard guard, int distanceTiles)> GuardsWithDistances(StaffMember staff)
            => System.Array.Empty<(Guard, int)>();

        /// <summary>The position service the influence map needs. ⚠ 8.8 WORLD UNITS — InfluenceMap
        /// converts each coordinate itself with an arithmetic shift, so handing it tiles would put
        /// every aura within one tile of the origin.</summary>
        InfluenceWorld Positions => _positions ??= new InfluenceWorld(this);
        InfluenceWorld _positions;

        sealed class InfluenceWorld : IInfluenceWorld
        {
            readonly ParkEntertainerWorld _owner;
            public InfluenceWorld(ParkEntertainerWorld owner) => _owner = owner;
            public (int X, int Y) Position(Visitor guest) => throw ParkEntertainerWorld.NotWired("guest positions for the influence map");
            public (int X, int Y) Position(StaffMember staff)
            {
                var (x, z) = _owner._staffTile(staff);
                return (ParkGuests.Centre(x), ParkGuests.Centre(z));
            }
            /// <summary>⚠ ZERO, AND ZERO FREEZES THE UNPLEASANT PARTICLE'S LIFETIME — which is correct
            /// here only because nothing in the port creates one. The entertainer's aura has no
            /// countdown at all; it lives until Idle releases it. If a particle producer is ever wired,
            /// this must become the real engine timestep (0x80103A90) or the mess never fades.</summary>
            public uint TimeStep => 0;
        }

        // ---- IGuardWorld, which the entertainer never reaches -------------------------------------

        static Exception NotWired(string what)
            => new NotSupportedException($"{what} is not wired: the port has no guard system. "
                                       + "The entertainer's own handlers never call this; if you are "
                                       + "seeing this, something else started using IGuardWorld.");

        public bool GuestExists(Visitor guest) => throw NotWired("GuestExists");
        public bool OnGuestTile(StaffMember staff, Visitor guest) => throw NotWired("OnGuestTile");
        public void SendGuestMessage(Visitor guest, int message) => throw NotWired("SendGuestMessage");
        public void FreeWaypoints(StaffMember staff) => throw NotWired("FreeWaypoints");
        public void SetAnimation(StaffMember staff, int animation) => throw NotWired("SetAnimation");
        public void PathToGuest(StaffMember staff, Visitor guest, int flags, int secondaryFlags)
            => throw NotWired("PathToGuest");
        public bool HasExits => throw NotWired("HasExits");
        public void PathToParkPoint(StaffMember staff, int pointIndex, int flags, int secondaryFlags)
            => throw NotWired("PathToParkPoint");
        public void PathToRandomExit(StaffMember staff, int flags, int secondaryFlags)
            => throw NotWired("PathToRandomExit");
        public void SetGateWaypoint(StaffMember staff) => throw NotWired("SetGateWaypoint");
        public bool TryPathToPost(StaffMember staff, int attempts, int xRadius, int minY, int maxY,
                                  int flags, int secondaryFlags) => throw NotWired("TryPathToPost");
        public int Counter80103950 { get => throw NotWired("Counter80103950"); set => throw NotWired("Counter80103950"); }
        public int Counter80103954 { get => throw NotWired("Counter80103954"); set => throw NotWired("Counter80103954"); }
    }
}
