using System;
using System.Collections.Generic;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>The park as a GUARD reads it (TPW.Sim.IGuardWorld).
    ///
    /// ⚠⚠ THE LAST OF THE FIVE STAFF CLASSES, and the sixth "ported but never called" system found in
    /// two days. `Guard` — Dispatch, Idle, Chase, GoToExitPoint, CrossGate, LeavePark, GoToSpawnPoint,
    /// TakePost, Arrive, OnMessage — had ZERO call sites in the game project and `IGuardWorld` had no
    /// implementation anywhere. A hired guard drew a wage and wandered, and every pelted entertainer
    /// took the no-guard branch: −5 more morale and nobody comes.
    ///
    /// ⭐ THE ARRIVE SHAPE IS THE SAFE ONE, AND I CHECKED BEFORE WIRING because the handyman's was not.
    /// `Guard.Arrive` handles its own six purposes and ends `default: StaffBase.Arrive(...)`, so
    /// routing every guard arrival through it is correct. `Handyman.Arrive` has no such fall-through,
    /// and routing everything through THAT is what left a cleaner walking patrol legs for ever without
    /// ever returning to Idle. Three shapes exist — sets a default then overrides, handles only its own
    /// silently, handles only its own and throws — and which one you have decides the wiring.</summary>
    sealed class ParkGuardWorld : IGuardWorld
    {
        readonly ParkStaffWorld _base;
        readonly Func<IEnumerable<Guest>> _guests;
        readonly Func<Visitor, Guest> _guestOf;
        readonly Func<Staffer, int, int, PathFlags, bool> _ask;      // tile coords
        readonly Func<Staffer, int, int, bool> _askWorld;            // raw 8.8
        readonly Action<Staffer> _freeChain;
        readonly Action<Visitor, int> _message;
        readonly Func<IReadOnlyList<(int X, int Z)>> _spawnTiles;
        readonly Func<(int X, int Z)?> _gateArea;
        readonly IRandomSource _dice;

        public ParkGuardWorld(ParkStaffWorld shared, Func<IEnumerable<Guest>> guests,
                              Func<Visitor, Guest> guestOf,
                              Func<Staffer, int, int, PathFlags, bool> askTile,
                              Func<Staffer, int, int, bool> askWorld,
                              Action<Staffer> freeChain, Action<Visitor, int> message,
                              Func<IReadOnlyList<(int X, int Z)>> spawnTiles,
                              Func<(int X, int Z)?> gateArea, IRandomSource dice,
                              Func<StaffMember, Staffer> stafferOf)
        { _base = shared; _guests = guests; _guestOf = guestOf; _ask = askTile; _askWorld = askWorld;
          _freeChain = freeChain; _message = message; _spawnTiles = spawnTiles; _gateArea = gateArea;
          _dice = dice; _stafferOf = stafferOf; }

        /// <summary>The host's Staffer for a StaffMember the sim names. See FreeWaypoints.</summary>
        readonly Func<StaffMember, Staffer> _stafferOf;

        public Staffer Current { get => _base.Current; set => _base.Current = value; }

        public long NowTick => _base.NowTick;
        public bool IsTypeOnStrike(StaffKind kind) => _base.IsTypeOnStrike(kind);
        public bool HasPatrolRect(StaffMember s) => _base.HasPatrolRect(s);
        public bool TryPathIntoPatrolArea(StaffMember s) => _base.TryPathIntoPatrolArea(s);
        public bool StrikeMusterExists => _base.StrikeMusterExists;
        public bool TryPathToStrikeMuster(StaffMember s) => _base.TryPathToStrikeMuster(s);
        public bool TryPathToRest(StaffMember s) => _base.TryPathToRest(s);

        // ---- the guest the guard is chasing -------------------------------------------------------

        /// <summary>⚠ THE GUEST MAY HAVE LEFT THE PARK MID-CHASE. The sim asks this every tick of a
        /// chase precisely because a culprit can be removed underneath it, and a host that answered
        /// true unconditionally would have guards chasing freed objects for 3600 ticks.</summary>
        /// <summary>⭐ SAY WHICH ABORT ENDED THE CHASE. Guard.Chase has three exits that all cost the
        /// same −5 and all land in the same state, so "0 chasing" cannot tell a culprit who left the
        /// park from one who joined a queue from a deadline. The sim keeps no tally; this does.</summary>
        public int CulpritGone { get; private set; }
        public int LastCulpritState { get; private set; } = -1;

        public bool GuestExists(Visitor guest)
        {
            bool ok = guest != null && _guestOf(guest) != null;
            if (ok) LastCulpritState = (int)guest.State; else CulpritGone++;
            return ok;
        }

        /// <summary>Same TILE, not the same position. A guard that has to reach the guest's exact 8.8
        /// coordinate never catches anybody, because both are still moving.</summary>
        public bool OnGuestTile(StaffMember staff, Visitor guest)
        {
            if (Current == null || !ReferenceEquals(Current.S, staff)) return false;
            var g = _guestOf(guest);
            return g != null && (g.X >> 8) == (Current.X >> 8) && (g.Z >> 8) == (Current.Z >> 8);
        }

        /// <summary>Slot 40 on the guest — the same door `ParkEntrance` already posts through
        /// (VisitorMessages.OnMessage). Message 4 is the catch.</summary>
        public void SendGuestMessage(Visitor guest, int message)
        {
            // ⭐ COUNTED HERE BECAUSE THE SIM KEEPS NO TALLY. A catch is the guard's whole point and
            // nothing else in the park records that one happened; without this the only evidence a
            // guard ever did its job would be a morale number that several other things also move.
            if (message == Guard.CatchMessage) Caught++;
            _message(guest, message);
        }

        /// <summary>How many guests a guard has caught. Host bookkeeping, not the sim's.</summary>
        public int Caught { get; private set; }

        /// <summary>⭐ WHY NOBODY IS STANDING AT THE GATE. "0 on post" has four different causes — the
        /// machine never reached TakePost, there is no gate tile, all five samples were refused, or one
        /// was accepted and the ROUTE later failed — and the state log can only see the first. The
        /// original gives up silently after five tries (Guard.PostAttempts), so a park whose gate has
        /// little walkable ground around it legitimately has no posted guards; that is not a defect,
        /// and without these counts it is indistinguishable from one.</summary>
        public int PostTried { get; private set; }
        public int PostTaken { get; private set; }
        public int PostGaveUp { get; private set; }
        public int PostNoGate { get; private set; }

        /// <summary>⚠⚠ RESOLVE THE STAFFER BY THE MEMBER THE SIM HANDED US, NOT BY `Current`. Both of
        /// these used to no-op unless `Current.S` WAS the staff, on the assumption that the sim only ever
        /// acts on the staffer whose tick is running. `Guard.Dispatch` disproves that: it is called from
        /// inside the ENTERTAINER's Shock handler and acts on a different staff member entirely, so the
        /// Current test failed and the two calls did nothing. Every IGuardWorld member takes its
        /// StaffMember explicitly, and that is the argument to trust.</summary>
        public void FreeWaypoints(StaffMember staff)
        { if (_stafferOf(staff) is { } s) _freeChain(s); }

        /// <summary>The drawn animation. ⚠ THE PORT HAS NO GUARD ART, so this only records which clip
        /// the sim asked for — 15 idle, 30 chase — and nothing draws differently yet. A GAP, and the
        /// reason it is stored rather than discarded is that a discarded value cannot be measured.</summary>
        public void SetAnimation(StaffMember staff, int animation)
        {
            // ⚠⚠ COUNT THE TRANSITION, NOT THE STATE, AND COUNT IT WHERE IT HAPPENS. Chase (33)
            // pushes Walking on its very FIRST tick, so an instantaneous "how many are in state 33"
            // reads 0 on a park whose chase works perfectly — the same mistake as the "on post" count,
            // where TakePost's arrival sets Idle two ticks later.
            // ⭐ And the obvious fix was ALSO wrong: counting `State == Chase && wasState != Chase` in
            // the staff loop observes nothing, because Dispatch runs during the ENTERTAINER's iteration,
            // so by the guard's own turn `wasState` is already 33. The chase animation is set exactly
            // once, inside Dispatch, with the guard named — that is the event, so that is where it is
            // counted.
            if (animation == TPW.Sim.Guard.ChaseAnimation) ChasesStarted++;
            if (_stafferOf(staff) is { } s) s.Anim = animation;
        }

        /// <summary>Chases STARTED since the park loaded, counted at Guard.Dispatch's own animation
        /// call. The running total is the only readable form; see SetAnimation.</summary>
        public int ChasesStarted { get; private set; }

        /// <summary>Re-path to wherever the culprit is NOW. ⚠ Called every tick of a chase, which is
        /// what makes it a chase rather than a walk to where the guest used to be.</summary>
        public void PathToGuest(StaffMember staff, Visitor guest, int flags, int secondaryFlags)
        {
            if (Current == null || !ReferenceEquals(Current.S, staff)) return;
            var g = _guestOf(guest);
            if (g == null) { ChasePathNoGuest++; return; }
            // ⚠⚠ THE RETURN VALUE WAS DROPPED, AND IT IS THE DIFFERENCE BETWEEN TWO BUGS. `_ask`
            // false means the request was REFUSED AT ENTRY — ten searches already outstanding, or no
            // nodes — and produces NO message, so the guard sits in state 11 until the base machine
            // wanders it away. A request that was ACCEPTED and came back with no route is a different
            // animal entirely: it gets an answer and the purpose table handles it. Both end as
            // "1 chase started, 0 caught" and only this counter tells them apart.
            if (_ask(Current, g.X >> 8, g.Z >> 8, (PathFlags)flags)) ChasePathAsked++;
            else ChasePathRefused++;
        }

        /// <summary>The chase's own path requests: accepted, refused at entry, and asked for a guest
        /// the host could not find. See PathToGuest.</summary>
        public int ChasePathAsked { get; private set; }
        public int ChasePathRefused { get; private set; }
        public int ChasePathNoGuest { get; private set; }

        // ---- the gate and the way out -------------------------------------------------------------

        /// <summary>READ: point index 1 is the exit, 0 is the spawn point (0x800599A4). ⭐ THESE ARE A
        /// BINARY CONSTANT, NOT MAP DATA — every map puts its gate in the same place, which is the only
        /// reason a fixed pair of world coordinates works. So "has exits" is really "does this park
        /// have a gate at all", and a no-gate debug park has none.</summary>
        public bool HasExits => _gateArea() != null;

        public void PathToParkPoint(StaffMember staff, int pointIndex, int flags, int secondaryFlags)
        {
            if (Current == null || !ReferenceEquals(Current.S, staff)) return;
            var (x, z) = ParkPoints.Aim(pointIndex, _dice);
            // ⚠ RAW WORLD UNITS. ParkPoints.Aim answers in 8.8 and its x is a TILE BOUNDARY (5376 is
            // exactly 21.0 tiles), not a centre — rounding it to a tile and re-centring would move the
            // guard half a tile off the line the game actually walks people down.
            _askWorld(Current, x, z);
        }

        /// <summary>READ: replace the path with ONE waypoint at (my x, gate y), 0x80098594. ⭐ A
        /// STRAIGHT LINE, NOT A ROUTE — crossing the gate is the one move that does not ask the
        /// pathfinder, which is why a guard can cross a turnstile a guest would have to queue for.</summary>
        public void SetGateWaypoint(StaffMember staff)
        {
            if (Current == null || !ReferenceEquals(Current.S, staff)) return;
            var (_, z) = ParkPoints.Aim(0, _dice);
            _askWorld(Current, Current.X, z);
        }

        /// <summary>Off the map the way guests leave. ⚠ Uses the map's own spawn tiles rather than an
        /// invented "outside", so a guard leaves by the road the buses use.</summary>
        public void PathToRandomExit(StaffMember staff, int flags, int secondaryFlags)
        {
            if (Current == null || !ReferenceEquals(Current.S, staff)) return;
            var tiles = _spawnTiles();
            if (tiles.Count == 0) return;
            var (x, z) = tiles[_dice.Next(tiles.Count)];
            _ask(Current, x, z, (PathFlags)flags);
        }

        /// <summary>READ (0x80098090..0x800981D8): sample x then y in 8.8 units around the entrance,
        /// upper bounds EXCLUSIVE, clamp to the map, stop at the first accepted path. ⭐ FALSE MEANS NO
        /// ACCEPTED PATH IN FIVE TRIES, not "no tile there" — the guard then goes Idle rather than
        /// standing on a post it could not reach.</summary>
        public bool TryPathToPost(StaffMember staff, int attempts, int xRadius, int minY, int maxY,
                                  int flags, int secondaryFlags)
        {
            if (Current == null || !ReferenceEquals(Current.S, staff)) return false;
            if (_gateArea() is not { } gate) { PostNoGate++; return false; }
            for (int i = 0; i < attempts; i++)
            {
                int x = gate.X + _dice.Next(xRadius * 2 + 1) - xRadius;
                int z = gate.Z + minY + _dice.Next(maxY - minY);
                PostTried++;
                if (_ask(Current, x, z, (PathFlags)flags)) { PostTaken++; return true; }
            }
            PostGaveUp++;
            return false;
        }

        /// <summary>READ: the word at 0x80103950, changed by arrivals 14 and 16. ⚠ ITS PURPOSE IS NOT
        /// ESTABLISHED — do not relabel it a guest count or clamp it. Both gate arrivals increment and
        /// both crossings decrement, which is what makes it balance.</summary>
        public int Counter80103950 { get; set; }
        /// <summary>READ: the word at 0x80103954, incremented by arrival 16. NOT ESTABLISHED.</summary>
        public int Counter80103954 { get; set; }
    }
}
