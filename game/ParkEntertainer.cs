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
        readonly Func<IEnumerable<Guard>> _guardsOf;

        /// <summary>The aura each entertainer is holding, which the binary keeps at E+0x4C. Kept beside
        /// the StaffMember for the same reason the mechanic's ride and the handyman's bin are: the sim's
        /// type is not widened to carry a host's bookkeeping.</summary>
        readonly Dictionary<StaffMember, InfluenceArea> _aura = new();

        public ParkEntertainerWorld(ParkStaffWorld shared, InfluenceMap influence,
                                    Func<IEnumerable<Guest>> guests,
                                    Func<StaffMember, (int X, int Z)> staffTile,
                                    Func<IEnumerable<Guard>> guardsOf,
                                    Func<ParkGuardWorld> guardWorld)
        { _base = shared; _influence = influence; _guests = guests; _staffTile = staffTile;
          _guardsOf = guardsOf; _guardWorld = guardWorld; }

        /// <summary>The guard's own world, for the two members <see cref="Guard.Dispatch"/> reaches
        /// through this one. Lazy, because it is built after this is.</summary>
        readonly Func<ParkGuardWorld> _guardWorld;

        public Staffer Current { get => _base.Current; set => _base.Current = value; }

        /// <summary>Why a shocked entertainer got no guard: how many were offered, how many were busy,
        /// how many were out of range. Host bookkeeping — the sim keeps no tally.</summary>
        public int Asked { get; private set; }
        public int Busy { get; private set; }
        public int TooFar { get; private set; }

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

        /// <summary>The park's guards with their Manhattan tile distance from this entertainer, in
        /// HIRING ORDER — READ refinement of behaviour.md §3.2, 0x80095F8C..0x80095FAC. ⭐ THE ORDER IS
        /// PART OF THE BEHAVIOUR: the sim keeps the first entry on a tie, so which guard comes when two
        /// are equally close is decided by who you hired first.
        ///
        /// ⚠ This returned EMPTY until the guard was wired, and the consequence was not cosmetic: a
        /// pelted entertainer always took the no-guard branch, −5 more morale and nobody comes.</summary>
        public IEnumerable<(Guard guard, int distanceTiles)> GuardsWithDistances(StaffMember staff)
        {
            // ⚠ THE CALL AND THE ENUMERATION ARE DIFFERENT EVENTS. This is an iterator, so a body
            // counter only moves when somebody walks it — and "Shock asked and got an empty list" then
            // looks identical to "Shock never asked". One run read `offered 0` and cost an hour for
            // exactly that reason. Calls is incremented eagerly, here, before the deferred part.
            Calls++;
            return Enumerate(staff);
        }

        /// <summary>How many times Shock asked for a guard, whatever the list then held.</summary>
        public int Calls { get; private set; }

        IEnumerable<(Guard guard, int distanceTiles)> Enumerate(StaffMember staff)
        {
            var (sx, sz) = _staffTile(staff);
            foreach (var g in _guardsOf())
            {
                var (gx, gz) = _staffTile(g.Staff);
                int d = Math.Abs(gx - sx) + Math.Abs(gz - sz);
                // ⭐ SAY WHY NOBODY CAME. The sim picks the nearest guard that is NotBusy and strictly
                // under 7 tiles, and silently takes the no-guard branch when none qualifies — so
                // "0 chasing" cannot distinguish a broken dispatch from a park where every guard
                // happened to be asleep four tiles too far away. This records both reasons.
                Asked++;
                if (!Guard.NotBusy(g.Staff)) Busy++;
                else if (d >= 7) TooFar++;
                yield return (g, d);
            }
        }

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
        // ⚠⚠ THESE TWO ARE ON THE ENTERTAINER'S PATH AFTER ALL, and the claim above that they are
        // not was wrong for four hours of running time. Entertainer.Shock ends in
        // `chosen.Dispatch(Culprit, world)` and Dispatch's first two statements are FreeWaypoints and
        // SetAnimation — on the GUARD, through the world the ENTERTAINER was handed. Throwing here
        // raised out of the staff loop before Shock could PopState, so the entertainer stayed in state
        // 32 for the rest of the park's life and re-dispatched every tick: 1769 offers in one run,
        // 0 busy, 0 too far, 0 chasing. The exception was in the log 7,076 times and my own grep
        // filtered it out — the throw did its job, the reader did not.
        //
        // ⭐ THE THROW IS STILL RIGHT FOR EVERYTHING ELSE. What was wrong was the premise "the
        // entertainer's handlers never call this", not the policy. So these two forward to the real
        // guard world and the other fourteen members keep throwing.
        public void FreeWaypoints(StaffMember staff) => _guardWorld().FreeWaypoints(staff);
        public void SetAnimation(StaffMember staff, int animation) => _guardWorld().SetAnimation(staff, animation);
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
