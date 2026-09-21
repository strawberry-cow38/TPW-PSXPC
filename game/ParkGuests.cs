using System;
using System.Collections.Generic;
using Godot;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>The park's map as the pathfinder reads it (TPW.Sim.IPathMap).
    ///
    /// ⚠ THE SIM DELIBERATELY CANNOT SEE TPW.Data. TPW.Sim has no reference to it, which is what keeps
    /// the whole simulation testable with no engine and no archive; this adapter is the seam. Three
    /// bytes of a tile and one park-wide flag are all the search wants.</summary>
    sealed class ParkPathMap : IPathMap
    {
        readonly ParkMap _map;
        public ParkPathMap(ParkMap map) => _map = map;

        public int Width => _map.Width;
        public int Height => _map.Height;
        public bool ParkIsOpen { get; set; }
        public int TypeAt(int x, int y) => _map[x, y].Raw0;
        public int LinkBitsAt(int x, int y) => _map[x, y].Links;
        public int FlagsAt(int x, int y) => _map[x, y].Flags;
    }

    /// <summary>One guest in the park: the simulation's <see cref="Visitor"/>, where it is standing, and
    /// the box that stands there.
    ///
    /// ⚠ POSITION IS 8.8 WORLD UNITS, the game's own, where a tile is 256 and a tile centre is
    /// `tile &lt;&lt; 8 | 0x80`. Not Godot metres and not tiles. The pathfinder, the waypoints and the
    /// walk speed are all in these units, and converting early is how they stop agreeing.</summary>
    /// <summary>Somebody the park walks along a waypoint chain and draws out of the people sheet.
    ///
    /// ⭐ STAFF AND GUESTS ARE THE SAME BODY. In the original they share the Person base (P+0x..), the
    /// same pathfinder, the same waypoint pool and the same eight drawn facings; only the state machine
    /// hanging off them differs. Splitting that here would mean two copies of the walk loop, and the
    /// first time one was fixed they would begin to disagree.</summary>
    abstract class Walker : IPathClient
    {
        public int X, Z;
        public MeshInstance3D Inst;

        /// <summary>How this one is drawn (GuestSprites): which person's sprites, which way it is walking
        /// as the camera sees it, and how far through the five-frame stride it is.</summary>
        public int Block, Facing, Frame, Walked;
        /// <summary>Draws since this walker last covered any ground, for the standing pose.</summary>
        public int LastWalked, StillFor;
        /// <summary>Which animation this walker is playing, and the draws since its frame last stepped.
        /// A change of animation resets the frame, which is why the id is kept rather than inferred.</summary>
        public int Anim = -1, AnimTick;

        /// <summary>The last step this walker took, in WORLD units — which way it is actually going.
        ///
        /// ⚠ THIS, NOT THE FACING INDEX, IS WHAT A WALKER OWNS. Which of the eight drawn facings it
        /// shows depends on where the CAMERA is, so it is not a property of the walker at all and must
        /// be worked out again every time the view moves. Storing the index instead froze every guest's
        /// sprite at whatever the camera happened to be doing on its last step.</summary>
        public int DirX, DirZ;

        /// <summary>How many ticks this walker has had its in-queue bit set without boarding. A
        /// DIAGNOSTIC of the port's own — "some guests get straight on and others are stuck for ever"
        /// is a statement about TIME IN THE QUEUE, and nothing else in the readout measures it.</summary>
        public long QueuedTicks;

        /// <summary>Not drawn, and not walked - somebody else owns it. A guest on a ride (state 21).</summary>
        public bool Hidden;

        /// <summary>Where the walk is up to: the waypoint pool index of the next corner, or
        /// <see cref="WaypointPool.NoChain"/>.</summary>
        public int WaypointHead { get; set; } = WaypointPool.NoChain;

        /// <summary>The last thing the pathfinder said, or null while a request is outstanding.
        ///
        /// ⚠ NULL IS A REAL STATE and it is the common one. A request can sit unanswered for up to two
        /// hundred frames, and a REFUSED request is never answered at all - see the note on Step.</summary>
        public PathMessage? Answer;

        /// <summary>True between asking for a route and hearing about it.</summary>
        public bool Waiting;

        /// <summary>The tile the outstanding request was for, so a FAILURE can be classified. A bare
        /// count of failures cannot tell "the player built an island" from "the search is broken", and
        /// those two want opposite responses.</summary>
        public int TargetTileX = -1, TargetTileZ = -1;

        /// <summary>Tiles per park frame in 8.8 units; the walk loop's budget.</summary>
        public abstract int WalkSpeed { get; }

        /// <summary>What to do when the chain runs out. Set once, because the walk loop can reach the
        /// end of a chain in the middle of a frame's budget and carries on afterwards.</summary>
        public Action<Walker> OnArrive;

        public void OnPathMessage(PathMessage message) { Answer = message; Waiting = false; }
    }

    sealed class Guest : Walker
    {
        public Visitor V;
        // Save's V+0x63 has unknown semantics; retain its byte, never invent behaviour for it.
        public byte Unknown63;
        public override int WalkSpeed => V.WalkSpeed;

        /// <summary>The state this guest was in last tick, and the tick it entered the current one.
        ///
        /// ⭐ THE ONLY THING THAT MAKES A FREEZE NAME ITSELF. "Guests get stuck" has been reported four
        /// separate ways from outside and every diagnostic so far has been per-SUBSYSTEM — the queue's
        /// own wait, the gate's own counters — so a guest frozen in a state nobody owns is invisible to
        /// all of them. A state and a duration together say which handler is not running, which is the
        /// actual question every one of those reports was asking.</summary>
        public VisitorState LastState = (VisitorState)(-1);
        public long StateSince;
    }

    /// <summary>The guests in a park: spawning them, asking the pathfinder for routes, and walking them.
    ///
    /// ⭐ THIS IS THE SEAM EVERYTHING ELSE WAS WAITING ON. The visitor state machines, the queue chain
    /// and the staff classes were all written against interfaces with nothing behind them; this is the
    /// first thing that puts a Visitor in an actual park and moves it. It deliberately does the SMALLEST
    /// honest thing - spawn, route, walk, repeat - because the value is in proving the pathfinder and
    /// the movement against a real map, not in wiring every state at once.
    ///
    /// ⚠ WHAT IS NOT WIRED YET, so nobody reads more into this than it does: the guests do not decide
    /// (VisitorDecision), do not have needs (VisitorNeeds), do not queue or ride (VisitorQueue) and do
    /// not enter or leave through the turnstile. They pick a random reachable path tile and walk to it.
    /// Every one of those machines exists and is tested; they are not connected here.</summary>
    sealed partial class ParkGuests
    {
        /// <summary>A tile centre in 8.8 world units.</summary>
        public static int Centre(int tile) => (tile << 8) | 0x80;

        readonly ParkMap _map;
        readonly ParkPathMap _pathMap;
        readonly WaypointPool _waypoints = new();
        readonly Pathfinder _finder;
        readonly List<Guest> _guests = new();
        /// <summary>The sim passes a Visitor where the park needs the body that carries it. One map
        /// rather than a scan: the entrance machine asks about a Visitor several times per guest tick.</summary>
        readonly Dictionary<Visitor, Guest> _byVisitor = new();
        readonly List<Staffer> _staff = new();

        /// <summary>The park's people, for anything outside this class that needs to read them — the
        /// advisor's statistics, which count visitors and staff by kind and grade.</summary>
        public IEnumerable<Visitor> VisitorList { get { foreach (var g in _guests) yield return g.V; } }
        public IEnumerable<StaffMember> StaffList { get { foreach (var s in _staff) yield return s.S; } }
        ParkStaffWorld _staffWorld;
        readonly Node3D _parent;
        readonly Random _rng;
        readonly SimRandom _dice;
        readonly List<(int X, int Z)> _walkable = new();
        GuestBrain _brain;
        ParkRideWorld _rides;

        /// <summary>The flags a guest walks with: paths, queues, the gate and the tiles beside it.
        ///
        /// ⚠ NOT grass. A guest that may cross grass will, at double cost, and the park's paths stop
        /// meaning anything. behaviour.md's call sites decide this per request; this is the plain
        /// "walk about the park" set.</summary>
        public const PathFlags WalkFlags = PathFlags.Path | PathFlags.Queue | PathFlags.GateSide;

        sealed class SimRandom : IRandomSource
        {
            readonly Random _r;
            public SimRandom(Random r) => _r = r;
            public int Next(int n) => _r.Next(n);
        }

        public ParkGuests(ParkMap map, Node3D parent, int seed = 12345)
        {
            _map = map;
            _parent = parent;
            _rng = new Random(seed);
            _dice = new SimRandom(_rng);
            _pathMap = new ParkPathMap(map);
            _finder = new Pathfinder(_pathMap, _waypoints, _dice);

            for (int x = 0; x < map.Width; x++)
                for (int z = 0; z < map.Height; z++)
                    if (map[x, z].IsWalkable) _walkable.Add((x, z));
        }

        /// <summary>Give the guests something to want. Until this is set they walk to random tiles;
        /// with it they run the real decision (TPW.Sim.VisitorDecision) against the placed
        /// attractions.</summary>
        /// <summary>The queue-and-ride world, shared with the park so the rides load from the same
        /// lists the guests stand in.</summary>
        public ParkRideWorld Rides => _rides;

        public void SetBrain(Func<IReadOnlyList<GuestTarget>> targets)
            => _brain = new GuestBrain(_map, targets, (g, tx, tz) => Ask(g, tx, tz, WalkFlags), () => _now);

        /// <summary>⭐ EVERY PATH REQUEST IN THE PARK GOES THROUGH HERE, and it refuses one whose two
        /// ends are in different connected pieces of the map before the pathfinder ever sees it.
        ///
        /// ⚠⚠ THIS IS A DELIBERATE DIVERGENCE FROM THE ORIGINAL, and it is the only one in this file.
        /// The original asks, searches, and fails. So does this, with one exception: a target the
        /// guest provably cannot reach is refused immediately instead of burning a full 200-slice
        /// budget first. Measured reason (findings/pathfinder.md): requests are scheduled newest-first
        /// and only the head runs each frame, so ONE unreachable target holds the head for many frames
        /// while everything behind it waits — and the old requests at the tail then never run at all.
        /// Four to seven of the ten slots ended up permanently held, and 99% of every route request in
        /// the park failed as a result. The scheduling is faithful and must stay; the trigger is what
        /// this removes.
        ///
        /// ⚠ IT IS NOT A PATHFINDING SHORTCUT. Same-piece requests still go to the pathfinder and can
        /// still fail on flags, budget or a full pool. This only declines the ones whose answer is
        /// already known from the connectivity pass the park computes anyway.
        ///
        /// Turn it off with --park-nopreflight to measure against the original's behaviour; the count
        /// is reported so a park refusing a lot of these is visibly a park with a connectivity
        /// problem, which is the thing that should be fixed instead.</summary>
        public bool Preflight { get; set; } = true;
        public int PreflightRefused { get; private set; }

        bool Seek(IPathClient client, int fromX, int fromY, int toX, int toY, PathFlags flagA, int flagB)
        {
            // ⚠ ONLY WHEN THE REQUEST IS NO MORE PERMISSIVE THAN THE MAP I BUILT. The connected pieces
            // are flooded with the pathfinder's OWN step test at WalkFlags, so the two agree by
            // construction — but only for a request at those flags or a subset of them. A caller that
            // added Grass or Footprint could legally cross a boundary this map says is closed, and
            // refusing it would be the preflight inventing a wall. Today every caller passes WalkFlags
            // or 0x11 (Path|Queue), a subset; this makes that a requirement instead of a coincidence.
            if (Preflight && _area != null && (flagA & ~WalkFlags) == 0
                && AreaAt(fromX >> 8, fromY >> 8) != AreaAt(toX >> 8, toY >> 8))
            {
                PreflightRefused++;
                // ⭐⭐ DELIVER THE FAILURE, DO NOT JUST DECLINE. This is the whole difference between
                // a faithful shortcut and a behaviour change, and the first version of this got it
                // wrong. The original ASKS and FAILS, and failing costs the guest happiness and
                // boredom and sends it to wander. Returning a silent false instead skips all of that:
                // measured, average boredom went from 35 to ZERO and targets chosen fell 76-fold,
                // because guests were quietly retrying a refused request forever instead of taking
                // the hit and doing something else. A park that felt fixed and had stopped playing
                // the game.
                //
                // The pathfinder answers on a later frame, so this does too — the message is left for
                // the next tick rather than re-entering the caller mid-decision.
                client.OnPathMessage(PathMessage.Failed);
                return false;
            }
            return _finder.Request(client, fromX, fromY, toX, toY, flagA, flagB);
        }

        /// <summary>Every path request in the park goes through here, so the tile asked for is always
        /// recorded against the walker and a failure can say which KIND of failure it was.</summary>
        bool Ask(Walker w, int tx, int tz, PathFlags flags)
        {
            if (!Seek(w, w.X, w.Z, Centre(tx), Centre(tz), flags, 0)) return false;
            w.TargetTileX = tx; w.TargetTileZ = tz;
            return true;
        }

        /// <summary>Build the queue world. Separate from the brain because the rides need it too.</summary>
        public void SetRideWorld(Func<IReadOnlyList<GuestTarget>> targets)
        {
            _rides = new ParkRideWorld(_map, () => _now,
                (g, tx, tz) => Seek(g, g.X, g.Z, Centre(tx), Centre(tz), WalkFlags, 0),
                SetSingleWaypoint, FreeChain);
            _rides.Advisor = (i, n) => AdvisorEvent?.Invoke(i, n);
            _rideTargets = targets;
        }

        Func<IReadOnlyList<GuestTarget>> _rideTargets;

        /// <summary>One waypoint straight to a point, for the shuffle-forward step, which moves a guest
        /// one place up a queue rather than asking the pathfinder for a route.</summary>
        bool SetSingleWaypoint(Walker g, int x, int z)
        {
            FreeChain(g);
            int i = _waypoints.Alloc();
            if (i < 0) return false;
            _waypoints.Encode(i, x, z);
            _waypoints.SetNext(i, -1);
            g.WaypointHead = i;
            return true;
        }

        int FreeChain(Walker g)
        {
            if (g.WaypointHead != WaypointPool.NoChain) _waypoints.FreeChain(g.WaypointHead);
            g.WaypointHead = WaypointPool.NoChain;
            return 0;
        }

        /// <summary>Running totals since the park loaded. ⚠ THE INSTANTANEOUS COUNT IS A BAD
        /// INSTRUMENT: with an 8-tick decision stagger and a 360-tick cooldown after a failure, how
        /// many guests happen to hold a target at the moment of a screenshot swings between none and
        /// most of them for reasons that have nothing to do with whether the decision works. These
        /// only go up.</summary>
        public int ChoseTarget { get; private set; }
        /// <summary>Searches that came back with no route. A park whose attractions cannot be reached
        /// shows it here rather than by quietly doing nothing.</summary>
        public int RouteFailed { get; private set; }

        /// <summary>Failures where the walker and its destination were in DIFFERENT connected pieces of
        /// the map. Expected and self-inflicted: the player built somewhere nothing joins to. The useful
        /// reading of this one is not "small", it is FLAT — a climbing number means guests keep choosing
        /// things they can never reach, which is the park's shape, not a fault.</summary>
        public int RouteFailedStranded { get; private set; }

        /// <summary>Failures where the two WERE in the same piece and the search still found nothing.
        ///
        /// ⭐ THIS IS THE NUMBER WORTH GATING ON. The other bucket can never be zero in a park with an
        /// island in it, so a single total can only ever climb and says nothing. ⚠ It is a triage
        /// signal, not a proof: the fill is WEAKLY connected (an edge either way joins two tiles) while
        /// the search is directed, so a genuinely one-way link lands here legitimately.</summary>
        public int RouteFailedSameArea { get; private set; }

        /// <summary>How many guests currently have something they are heading for.</summary>
        public int WithTarget { get { int n = 0; foreach (var g in _guests) if (g.V.HasTarget) n++; return n; } }

        public int Count => _guests.Count;
        public int FreeNodes => _finder.FreeNodes;
        public int FreeWaypoints => _waypoints.FreeCount;
        /// <summary>The sim's dice, so a caller that has to roll the game's own rolls (the bus's draw score is
        /// a die roll per attraction) uses the same stream rather than starting a second one.</summary>
        public IRandomSource Dice => _dice;
        public int Outstanding => _finder.ActiveRequests;
        public bool ParkIsOpen { get => _pathMap.ParkIsOpen; set => _pathMap.ParkIsOpen = value; }

        /// <summary>Put a guest on a tile: <paramref name="at"/> when the caller knows where (the bus drops
        /// its whole load on one tile), otherwise a random walkable one. Returns null when the park has
        /// nowhere to stand - a park with no paths laid, which is the state every park starts in.
        ///
        /// ⭐ THE BUS DOES NOT DELIVER GUESTS WHERE IT PARKS. Arrivals (0x80067274) calls 0x800540B8(&amp;pos, 0)
        /// BEFORE its per-guest loop, so every guest of a load appears on the same tile - the CENTRE of the
        /// map's exit point 0 (`x·256 + 128`, `z·256 + 128`, ParkMap.SpawnTiles), beside the entrance road.
        /// The bus's own position is never read by the spawn; the two are only correlated in time.</summary>
        public Guest Spawn((int X, int Z)? at = null)
        {
            if (_walkable.Count == 0) return null;
            var (tx, tz) = at ?? _walkable[_rng.Next(_walkable.Count)];

            var g = new Guest
            {
                V = Visitor.Spawn(_dice, 0),
                X = Centre(tx),
                Z = Centre(tz),
                // ⭐ DRAWN AS THE GAME'S OWN SPRITE when the people sheet is there, and as the box this
                // started as when it is not, so the pathfinder can still be watched without the archive.
                // ⚠ WHICH person a guest is drawn as is the port's choice: the game's own rule for that is
                // not traced (PeopleSheet).
                Inst = _sprites?.NewGuest() ?? new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.42f, 0.18f) },
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = Color.FromHsv((float)_rng.NextDouble(), 0.55f, 0.95f),
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    },
                },
            };
            // The body its type calls for: the game's own eight-slot list, four bodies over eight types
            // (PeopleSheet.GuestBlocks). Staff and the world's costumed character are NOT in it.
            g.V.VisitorType = _rng.Next(TPW.Data.PeopleSheet.GuestBlocks.Length);
            g.Block = TPW.Data.PeopleSheet.GuestBlocks[g.V.VisitorType];
            g.OnArrive = w => Arrived((Guest)w);
            _parent.AddChild(g.Inst);
            _guests.Add(g);
            _byVisitor[g.V] = g;
            Place(g);
            return g;
        }

        /// <summary>Bring the park up to a target headcount, at most one a tick.
        ///
        /// ⚠ A STAND-IN, not the game's rule. Real arrivals come from TPW.Sim.BusArrivals, which works
        /// out a rate from what is BUILT - a park with nothing in it gets nobody, which is the single
        /// most surprising fact in the whole economy. One a tick is a placeholder so there is something
        /// to look at, and it is deliberately not dressed up as the real thing.</summary>
        /// <summary>Path tiles only, for <see cref="Populate"/>. Rebuilt when the map changes.</summary>
        readonly List<(int X, int Z)> _paths = new();

        public void Populate(int target)
        {
            // ⚠⚠ ON A PATH, NOT ANYWHERE WALKABLE. `Spawn()` with no tile drops a guest on a RANDOM
            // walkable tile, and walkable includes every attraction entrance and queue tile in the park.
            // So --park-guests was teleporting guests INSIDE rides' enclosed entrance pockets, where they
            // promptly queued and boarded a ride nothing could walk to. Master spotted it from a
            // screenshot: "there's no way someone could have got on it." There wasn't. The harness put
            // one there, and every conclusion drawn from a --park-guests run about who can reach what was
            // measuring the harness.
            //
            // ⚠ AND SENDING THEM THROUGH THE GATE INSTEAD WAS WORSE. That was the first fix, and on a
            // park that is not open it left every guest standing at the bus stop waiting for a turnstile
            // that never admits them -- which reads exactly like the sim has frozen. This flag exists to
            // put guests IN a park now; it just has to put them somewhere a guest could legitimately be
            // standing, which is a path tile and never a ride's own entrance.
            if (_guests.Count >= target) return;
            if (_paths.Count == 0)
                foreach (var (x, z) in _walkable)
                    if (_map[x, z].Type is TPW.Data.TileType.Path or TPW.Data.TileType.PathQueueOverlap)
                        _paths.Add((x, z));
            if (_paths.Count == 0) { SpawnAtGate(); return; }
            Spawn(_paths[_rng.Next(_paths.Count)]);
        }

        /// <summary>One park frame of the pathfinder (0x800EC8C4). Separate from <see cref="Tick"/>
        /// because the search runs per FRAME and the guests move per TICK, and in the original those
        /// are not the same rate.</summary>
        public void RunPathfinder() => _finder.RunFrame();

        /// <summary>Put a member of staff in the park at a tile, drawn from the block its class actually
        /// uses: mechanic 263, guard 264, cleaner 266, researcher 274 (READ, findings/staff.md §3, the
        /// manager slots 0x800972E0/0x8009852C/0x80099590/0x80099C5C).
        ///
        /// ⚠ AN ENTERTAINER IS NOT UNIFORMED and has no block here — its resource is per THEME
        /// (403/401/402/404), which is why there are four uniformed blocks for five classes. Until the
        /// park knows its theme, hiring one falls back to the mechanic's block and says so.</summary>
        public Staffer Hire(StaffKind kind, int tx, int tz)
        {
            int block = StaffAppearance.Art(kind == StaffKind.Entertainer ? StaffKind.Mechanic : kind, 0)
                                       .PeopleBlock ?? TPW.Data.PeopleSheet.StaffBlocks[0];
            var st = new Staffer
            {
                S = new StaffMember(kind),
                X = Centre(tx),
                Z = Centre(tz),
                Block = block,
                Inst = _sprites?.NewGuest() ?? new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.20f, 0.46f, 0.20f) },
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = new Color(0.95f, 0.85f, 0.15f),
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    },
                },
            };
            // ⚠ ARRIVAL IS PER CLASS, and this cost me an afternoon. Slot 35 is OVERRIDDEN: a
            // mechanic's arrival (0x80096AE0) checks its target and starts closing the ride, where the
            // base's (0x80094590) only knows patrol, strike and rest. Sending a mechanic through the
            // base one made it walk all the way to a broken ride and then stand there with nothing to
            // do, go idle, and claim the same ride again — for ever.
            st.HiredDay = (int)(_now / ParkClock.TicksPerDay);
            st.OnArrive = w =>
            {
                var s2 = (Staffer)w;
                if (s2.S.Kind == StaffKind.Mechanic) { var mw = MechanicWorld(); mw.Current = s2; Mechanic.Arrive(s2.S, mw, false); }
                // ⚠ AND THE CLEANER'S IS ITS OWN TOO. Its arrival starts the job TIMER (0x80099418's
                // slot 35): reaching the litter is not clearing it, and reaching the bin is not
                // emptying it. Through the base's arrival a handyman walked to a piece of rubbish,
                // found no purpose it knew, went Idle, and claimed the same piece again — the exact
                // loop the mechanic's note describes, one class over.
                else if (s2.S.Kind == StaffKind.Cleaner)
                {
                    var hw = HandymanWorld(); hw.Current = s2;
                    // ⚠⚠ THE HANDYMAN'S ARRIVAL ONLY KNOWS ITS OWN TWO PURPOSES, and routing every
                    // arrival through it is why a hired cleaner cleared NOTHING. Handyman.Arrive
                    // starts a job timer for ToLitter or ToBin and does nothing at all otherwise —
                    // unlike Mechanic.Arrive, which sets Idle FIRST and then overrides. So a cleaner
                    // that finished an ordinary patrol leg arrived, was handed to a function with no
                    // case for it, stayed in Walking, and NEVER RETURNED TO IDLE — and Idle is the
                    // only state that looks for work. Measured: one `Idle -> Patrolling` at tick zero
                    // with the park still clean, then PathReady -> Walking for ever at tiredness 100.
                    //
                    // The base's arrival is what maps Patrol -> Idle. Call the handyman's for the
                    // handyman's purposes and the shared one for everything else.
                    if (s2.S.Purpose == StaffClassStates.ToLitter || s2.S.Purpose == StaffClassStates.ToBin)
                        Handyman.Arrive(s2.S, hw);
                    else StaffBase.Arrive(s2.S, StaffWorld(), false);
                }
                // ⚠⚠ AND THE GUARD'S IS ITS OWN TOO — the same bug as the cleaner's above, one class
                // further on, found the same way. Guard.Arrive is the WHOLE gate machine: purpose 8
                // returns to Chase so a chase that reaches its tile carries on instead of wandering
                // off; purpose 14 (ExitPoint) starts the ejection at the gate and bumps the gate
                // counter; purpose Gate then sends the guard to its POST or out of the park; and
                // purpose Post is the only thing that ever returns a posted guard to Idle. Routing
                // guards through the base's arrival left every one of those unreachable, which is why
                // the report has read "0 on post" since the guard was wired and why the ejection walk
                // ended in an ordinary patrol.
                // ⭐ SAFE TO ROUTE EVERYTHING THROUGH IT, unlike Handyman.Arrive: Guard.Arrive's
                // switch ends in `default: StaffBase.Arrive(...)`, so an ordinary patrol leg still
                // gets the shared answer. That fall-through is the difference and it is worth saying
                // out loud, because the cleaner's crash was caused by assuming it was there.
                else if (s2.S.Kind == StaffKind.Guard)
                {
                    var gw = GuardWorld(); gw.Current = s2;
                    GuardFor(s2.S).Arrive(gw, false);
                }
                else StaffBase.Arrive(s2.S, StaffWorld(), false);
            };
            _parent.AddChild(st.Inst);
            _staff.Add(st);
            Place(st);
            return st;
        }

        /// <summary>Take a guest out of the park entirely: the sim's RemoveFromPark (0x800519B0 then
        /// 0x80051D74 — out of the guest manager and every staff list told).
        /// ⚠ NO STAFF LIST IS TOLD, because no staff list holds guests yet.</summary>
        void RemoveGuest(Guest g)
        {
            // ⚠ DEFERRED, BECAUSE THE CALLER IS INSIDE THE FOREACH. A guest leaves from its OWN arrival,
            // which runs while Tick is enumerating _guests, and removing there throws. It never fired
            // before only because nothing reached the leaving arrival at all.
            if (_ticking) { _leaving.Add(g); return; }
            Left++;
            FreeChain(g);
            _byVisitor.Remove(g.V);
            _guests.Remove(g);
            g.Inst?.QueueFree();
        }

        /// <summary>How many guests have actually left the park. ⚠ THE NUMBER THAT SHOWS THE LEAVE PATH
        /// IS REAL — a refused guest that walks back in instead of leaving keeps the headcount looking
        /// healthy and never appears here.</summary>
        public int Left { get; private set; }

        readonly List<Guest> _leaving = new();
        bool _ticking;

        void DrainLeavers()
        {
            if (_leaving.Count == 0) return;
            foreach (var g in _leaving) RemoveGuest(g);
            _leaving.Clear();
        }

        /// <summary>The park as an arriving or leaving guest reads it. Built lazily because it needs the
        /// bank, which the host owns.</summary>
        public ParkEntranceWorld Entrance => _entrance;
        ParkEntranceWorld _entrance;

        /// <summary>Give the guests a turnstile. Until this is called they appear inside the park for
        /// free, which is the stand-in the bus arrivals were built against.</summary>
        public void SetEntrance(ParkFinances finances, Func<BusRoute> bus)
        {
            _entrance = new ParkEntranceWorld(_map, () => _now, finances, bus, _dice,
                (g, tx, tz, flags) => Ask(g, tx, tz, (PathFlags)flags),
                (g, x, y) => SetSingleWaypoint(g, x, y),
                FreeChain,
                () => _guests, () => System.Linq.Enumerable.Select(_staff, s => s.S),
                v => _byVisitor.TryGetValue(v, out var g) ? g : null,
                () => _rideTargets?.Invoke() ?? (IReadOnlyList<GuestTarget>)System.Array.Empty<GuestTarget>(),
                RemoveGuest,
                // ⭐ THE TURNSTILE'S BROADCAST REACHES THE GUARD NOW. Admit sends message 9 to every
                // person in state 46, guests AND staff, and the staff half landed in an empty body —
                // while still counting as sent. A guard's message 9 is what puts it in CrossGate.
                (st, id) =>
                {
                    foreach (var sf in _staff)
                        if (sf.S == st)
                        {
                            var gw = GuardWorld(); gw.Current = sf;
                            GuardFor(st).OnMessage(gw, id);
                            StaffAdmitted++;
                            return;
                        }
                });
            _entrance.SetQueueWorld(() => _rides);
        }

        /// <summary>Whether this guest is in the turnstile machine rather than loose in the park.
        ///
        /// ⚠⚠ THE STATE IS NOT ENOUGH, AND THAT COST A REAL BUG. Both Leave (38) and WalkOut (48) set
        /// state 11 WHILE WALKING (0x80091094, 0x800915D8) — the same state the litter-bin walk uses —
        /// so a guest on its way out stopped looking like a gate guest the moment it started moving. It
        /// then fell through to the ordinary logic, and a failed route dropped its target and WANDERED
        /// it: observed as "they turn round at the bus stop and walk back in without paying".
        /// The PURPOSE is what survives the walk, so it is half of the test.
        ///
        /// ⚠ AND IT MUST BE THE PURPOSE, NOT "state 11", or a guest walking to a bin is caught too.</summary>
        static bool AtTheGate(Guest g) => AtTheGateByPurpose(g) || g.V.State is VisitorState.SpawnToGate or VisitorState.WalkToLaneSlot
            or VisitorState.ShuffleInLane or VisitorState.LaneFront or VisitorState.PayEntryFee
            or VisitorState.WalkIn or VisitorState.AtGate or VisitorState.LeavingPark
            or VisitorState.PickLane or VisitorState.WalkOut;

        /// <summary>The purposes the entrance owns: 9 (gone), 11/12 (a lane slot), 14/15/16 (the gate
        /// line, the exit point, the spawn point). A guest carrying one of these is walking the gate's
        /// errand whatever state it is sitting in.</summary>
        static bool AtTheGateByPurpose(Guest g) => g.V.Purpose is Purpose.LeavePark or Purpose.Turnstile11
            or Purpose.Turnstile12 or Purpose.Turnstile14 or Purpose.Turnstile15 or Purpose.Turnstile16;

        /// <summary>One tick of a guest that is arriving or leaving (TPW.Sim.VisitorEntrance, §2.6).
        ///
        /// ⭐ 44 AND 46 DO NOTHING HERE ON PURPOSE. A guest at the front of a lane waits for the LANE's
        /// tick to send it to pay, and one in 46 waits for the admit routine's message 9. Giving either
        /// a per-tick handler would let a guest admit itself.</summary>
        void RunEntrance(Guest g)
        {
            switch (g.V.State)
            {
                case VisitorState.SpawnToGate: VisitorEntrance.SpawnToGate(g.V, _entrance); break;
                case VisitorState.PickLane: VisitorEntrance.PickLane(g.V, _entrance); break;
                case VisitorState.WalkToLaneSlot: VisitorEntrance.WalkToLaneSlot(g.V, _entrance); break;
                case VisitorState.ShuffleInLane: VisitorEntrance.ShuffleInLane(g.V, _entrance); break;
                case VisitorState.PayEntryFee:
                    // The two ways to be turned away are different facts: one is a guest who cannot
                    // afford £40, the other one who looked at what is built and decided it was not
                    // worth it. A park that charges too much and a park with nothing in it look the
                    // same from the headcount and nothing else here separates them.
                    // ⚠ THE SUM AT THE MOMENT OF THE ROLL, not at the end of the run. The verdict
                    // weighs the fee against what is built RIGHT THEN, so a guest that rolled before a
                    // ride was placed answered a different question from the one the final report shows.
                    // That is the first thing to rule out when the measured refusal rate misses the
                    // closed form, and it cannot be ruled out by reading the total afterwards.
                    int sumNow = System.Linq.Enumerable.Sum(_entrance.AttractionIntensities);
                    if (_verdicts++ == 0) _sumMin = _sumMax = sumNow;
                    else { if (sumNow < _sumMin) _sumMin = sumNow; if (sumNow > _sumMax) _sumMax = sumNow; }
                    switch (VisitorEntrance.PayEntryFee(g.V, _entrance, _dice))
                    {
                        case PayOutcome.CannotAfford: _refusedEntry++; _brokeAtGate++; break;
                        case PayOutcome.Refused: _refusedEntry++; break;
                    }
                    break;
                case VisitorState.WalkIn: VisitorEntrance.WalkIn(g.V, _entrance); break;
                case VisitorState.LeavingPark: VisitorEntrance.Leave(g.V, _entrance); break;
                case VisitorState.WalkOut: VisitorEntrance.WalkOut(g.V, _entrance, _dice); break;
            }
        }

        /// <summary>Put a guest down where the bus does and start it through the turnstile: the map's
        /// own spawn tile, state 36. ⚠ Without an entrance wired this is Spawn(), which puts a guest
        /// INSIDE the park for free — the stand-in the arrivals were built against.</summary>
        public Guest SpawnAtGate()
        {
            if (_entrance == null || _map.SpawnTiles.Count == 0) return Spawn();
            // ⭐⭐ ARRIVALS ALWAYS USE EXIT 0 — ROLLING FOR ONE PUTS THEM AT THE *EXIT* BUS STOP.
            // The bus tick calls Arrivals (0x80067274) at 0x8005273C with `addu a1,zero,zero`, so the index
            // is hard-wired to 0, and `Arrivals`' own rand(2) machinery is reached only by the −1 variant
            // that the game never runs (findings/transport.md §5, which measured it both ways: with entry 0
            // left alone 12 guests stand at (4736,1408) = tile 18, and only a PATCHED entry 0 puts them at
            // (6016,1408) = tile 23). Map 203's two tiles are (18,5), the arrivals' side, and (23,5), the
            // exit's. Rolling between them sent half of every bus to the wrong one.
            // ⚠ LEAVERS ARE THE OPPOSITE and must keep rolling: state 48 picks rand(N) at 0x8009154C.
            var (sx, sz) = _map.SpawnTiles[0];
            var g = Spawn((sx, sz));
            if (g == null) return null;
            g.V.SetState(VisitorState.SpawnToGate);
            return g;
        }

        int _refusedEntry, _brokeAtGate, _verdicts, _sumMin, _sumMax;

        readonly Dictionary<int, int> _strandedBy = new();
        readonly HashSet<Visitor> _strandedGuests = new();
        readonly Dictionary<(VisitorState, Purpose), int> _strandedStates = new();
        readonly Dictionary<string, int> _strandedTiles = new();

        /// <summary>⚠⚠ THE SAME-AREA FAILURES, BROKEN DOWN. The report has carried
        /// "N SAME AREA (this one should be 0)" for as long as it has existed and nobody chased a
        /// non-zero one, because a bare count cannot be acted on — it says a route failed between two
        /// tiles the area map calls connected, and nothing about WHICH.
        ///
        /// ⭐ AND IT IS NOT A CONTRADICTION, WHICH IS WHY IT NEEDS NAMING RATHER THAN ASSERTING. The
        /// area map unions a pair when an edge exists in EITHER direction (RebuildAreas' own note); the
        /// search is DIRECTED. So "same piece" is a strong maybe, and a one-way link — a queue tile
        /// joins only the tile behind it along the run — is exactly a place where the two disagree
        /// legitimately. A guest standing on one can be weakly connected to the whole park and unable
        /// to walk anywhere.</summary>
        readonly Dictionary<string, int> _sameAreaFrom = new();
        readonly Dictionary<string, int> _sameAreaTo = new();
        readonly Dictionary<(VisitorState, Purpose), int> _sameAreaStates = new();
        readonly HashSet<Visitor> _sameAreaGuests = new();

        /// <summary>Where the same-area failures happened, for the report. Empty is the healthy
        /// answer and says so, rather than printing nothing and looking like a missing line.</summary>
        public string SameAreaReport()
        {
            if (_sameAreaFrom.Count == 0) return "no same-area failures";
            var st = new List<string>();
            foreach (var kv in _sameAreaStates) st.Add($"{kv.Key.Item1}/{kv.Key.Item2}x{kv.Value}");
            return $"⚠ SAME-AREA failures — {_sameAreaGuests.Count} distinct guests, in {string.Join(" ", st)}"
                 + $"; standing on {string.Join(" ", _sameAreaFrom.Keys)}"
                 + $"; heading for {string.Join(" ", _sameAreaTo.Keys)}";
        }

        /// <summary>Where the stranded route failures are coming FROM: the piece each failing guest was
        /// standing in, and how many distinct guests are doing it. A big number from two guests in the
        /// wrong piece is a different fault from the same number spread over everyone.</summary>
        public string StrandedReport()
        {
            if (_strandedBy.Count == 0) return "no stranded failures";
            var parts = new List<string>();
            foreach (var kv in _strandedBy) parts.Add($"piece {kv.Key}: {kv.Value}");
            var st = new List<string>();
            foreach (var kv in _strandedStates) st.Add($"{kv.Key.Item1}/{kv.Key.Item2}x{kv.Value}");
            return $"stranded from {string.Join(", ", parts)} — {_strandedGuests.Count} distinct guests, in {string.Join(" ", st)}"
                 + $"; standing on {string.Join(" ", _strandedTiles.Keys)}";
        }

        /// <summary>What the gate is doing: how many guests are in each entrance state, plus the two
        /// lane counters and the fees taken. For a caption on a capture — the turnstile's whole job is
        /// invisible from the guest count, because a guest stuck OUTSIDE still counts as a guest.</summary>
        public string GateReport()
        {
            if (_entrance == null) return "no gate";
            int outside = 0, lanes = 0, paying = 0, inside = 0, leaving = 0;
            foreach (var g in _guests)
                switch (g.V.State)
                {
                    case VisitorState.SpawnToGate: case VisitorState.PickLane: case VisitorState.AtGate: outside++; break;
                    case VisitorState.WalkToLaneSlot: case VisitorState.ShuffleInLane: case VisitorState.LaneFront: lanes++; break;
                    case VisitorState.PayEntryFee: paying++; break;
                    case VisitorState.WalkIn: inside++; break;
                    case VisitorState.LeavingPark: case VisitorState.WalkOut: leaving++; break;
                }
            return $"gate: {outside} outside, {lanes} in a lane, {paying} paying, {inside} walking in, "
                 + $"{leaving} leaving; lanes {_entrance.LaneCount(0)}/{_entrance.LaneCount(1)}, "
                 + $"{_entrance.Counter_McAi1C} paid, {_refusedEntry} turned back ({_brokeAtGate} broke), {Left} actually left; "
                 + $"intensity sum {System.Linq.Enumerable.Sum(_entrance.AttractionIntensities)} "
                 + $"over {System.Linq.Enumerable.Count(_entrance.AttractionIntensities)} attractions, fee {_entrance.EntryFee}"
                 + $" (at roll time the sum ran {_sumMin}..{_sumMax} over {_verdicts} rolls)";
        }

        /// <summary>How many guests are standing in the two turnstile lanes right now — the number the
        /// bus load is capped by (`20 - lanes`, 0x80059150). ⚠ ZERO WHEN NO TURNSTILE IS WIRED, which is
        /// the honest answer then and a bug the moment one is.</summary>
        public int LanesWaiting => _entrance == null ? 0 : _entrance.LaneCount(0) + _entrance.LaneCount(1);

        ParkIdleWorld IdleWorld()
        {
            if (_idle != null) return _idle;
            var w = NewIdleWorld();
            w.Advisor = (i, n) => AdvisorEvent?.Invoke(i, n);
            return _idle = w;
        }

        ParkIdleWorld NewIdleWorld() => new ParkIdleWorld(
            () => _now,
            () => SlowClockDay?.Invoke() ?? 0,
            () => System.Linq.Enumerable.Select(_staff, s => s.S),
            v => _byVisitor.TryGetValue(v, out var gg) ? (gg.X >> 8, gg.Z >> 8) : (0, 0),
            st => { foreach (var sf in _staff) if (sf.S == st) return (sf.X >> 8, sf.Z >> 8); return (0, 0); },
            v =>
            {
                if (NearestBin == null || !_byVisitor.TryGetValue(v, out var gg)) return null;
                return NearestBin(gg.X >> 8, gg.Z >> 8);
            },
            (v, tx, tz) => _byVisitor.TryGetValue(v, out var gg)
                        && Seek(gg, gg.X, gg.Z, Centre(tx), Centre(tz), WalkFlags, 0)
                        && (gg.Waiting = true),
            // ⚠ THE GUEST'S OWN 8.8 POSITION, which Drop then scatters by ±100 (about 0.4 of a tile).
            // Passing a tile centre would stack every piece on the middle of its tile.
            v => { if (_byVisitor.TryGetValue(v, out var gg)) _litter.Pool.Drop(gg.X, gg.Z, _litter, _dice); },
            (s2, v) =>
            {
                var w = EntertainerWorld();
                foreach (var sf in _staff) if (sf.S == s2) w.Current = sf;
                var e = _entertainers.TryGetValue(s2, out var have) ? have : (_entertainers[s2] = new Entertainer(s2));
                e.Pelted(v, w, _dice);
            });

        /// <summary>The park's forty pieces of litter. ⭐ OWNED HERE because the guests who drop it, the
        /// guests who are made miserable by it and the staff who clear it are all in this one list —
        /// the pool is the only thing all three touch.</summary>
        readonly ParkLitterWorld _litter = new();

        /// <summary>The park's twenty influence circles. ⚠ TWENTY IN THE WHOLE PARK, not a tile map —
        /// everything written about this before today, including my own comment in ParkNeeds, called it
        /// a per-tile grid. The only producer wired is the entertainer's performance aura.</summary>
        readonly InfluenceMap _influence = new();

        /// <summary>The park's research tree, and the catalogue it reads. ⚠⚠ NOTHING IN THE PORT
        /// OWNED ONE, so Researcher.Research had nowhere to put its points even if it had been called —
        /// which it was not. Set by the view, which owns the catalogue.</summary>
        ParkResearchWorld _research;

        /// <summary>The park's research tree, for the save host. Null until a park is loaded.</summary>
        public ResearchSystem Research => _research?.System;
        public void SetResearch(Func<IReadOnlyList<TPW.Data.AttractionDefinition>> definitions,
                                Func<IEnumerable<int>> placedEntries, Action<ushort> announce)
        {
            _research = new ParkResearchWorld(StaffWorld(), definitions, placedEntries,
                                              () => System.Linq.Enumerable.Select(_staff, s => s.S), announce);
            _research.Attach(new ResearchSystem(_research));
        }

        /// <summary>One TPW.Sim.Guard per hired guard — the class keeps its culprit and gate direction
        /// on this object rather than on StaffMember, so the host owns one each.</summary>
        readonly Dictionary<StaffMember, Guard> _guards = new();

        /// <summary>One TPW.Sim.Entertainer per hired entertainer. The sim keeps the class's extra
        /// fields on this object rather than on StaffMember, so the host has to own one each.</summary>
        readonly Dictionary<StaffMember, Entertainer> _entertainers = new();

        /// <summary>Litter dropped / cleared, bins emptied, and how many of those were already past
        /// the neglect line. ⭐ CURRENTLY THE ONLY WAY TO SEE ANY OF IT, because nothing draws litter.</summary>
        public (int Live, int Dropped, int Cleaned, int Bins, int Neglected) LitterReport()
            => (_litter.Pool.Count, _litter.Dropped, _litter.Cleaned, _litter.BinsEmptied, _litter.BinsNeglected);

        /// <summary>Being sick, and watching an entertainer. ⚠ BOTH ARE EXITS THAT NOBODY WAS DRIVING
        /// — see ParkActivityWorld. Wiring the needs clock and the idle pass earlier today is what made
        /// states 28 and 29 reachable, and neither had a handler running, so a guest that entered one
        /// never came out of it.</summary>
        ParkActivityWorld ActivityWorld() => _activity ??= new ParkActivityWorld(
            () => _now, _litter, _dice,
            v => _byVisitor.TryGetValue(v, out var gg) ? (gg.X, gg.Z) : (0, 0),
            st => { foreach (var sf in _staff) if (sf.S == st) return (sf.X, sf.Z); return (0, 0); });
        ParkActivityWorld _activity;

        /// <summary>⚠⚠ THE FIFTH NEVER-CALLED SYSTEM. Entertainer.Tick had no caller anywhere in the
        /// game project and IEntertainerWorld had no implementation, so a hired entertainer wandered.
        /// It is also the only producer the influence map has, and the other end of a guest's state 28.</summary>
        ParkEntertainerWorld EntertainerWorld() => _entWorld ??= new ParkEntertainerWorld(
            StaffWorld(), _influence, () => _guests,
            st => { foreach (var sf in _staff) if (sf.S == st) return (sf.X >> 8, sf.Z >> 8); return (0, 0); },
            // ⭐ HIRING ORDER, because the sim keeps the first entry on an equal distance.
            () => { var list = new List<Guard>();
                    foreach (var sf in _staff) if (sf.S.Kind == StaffKind.Guard) list.Add(GuardFor(sf.S));
                    return list; },
            // ⚠⚠ THE ENTERTAINER REACHES INTO THE GUARD'S WORLD, and finding out cost a morning.
            // Guard.Dispatch runs inside Entertainer.Shock and calls FreeWaypoints and SetAnimation on
            // the world it was handed — this one. Both THREW, so every dispatch raised before PopState
            // and the entertainer never left state 32: one run showed the guard offered 1769 times, never
            // busy, never too far, and never once chasing.
            GuardWorld);
        ParkEntertainerWorld _entWorld;

        /// <summary>⚠⚠ THE LAST STAFF CLASS. Guard had no caller and IGuardWorld no implementation, so
        /// a hired guard wandered and every pelted entertainer took the no-guard branch.</summary>
        ParkGuardWorld GuardWorld() => _guardWorld ??= new ParkGuardWorld(
            StaffWorld(), () => _guests,
            v => _byVisitor.TryGetValue(v, out var gg) ? gg : null,
            (st, tx, tz, fl) => Ask(st, tx, tz, fl) && (st.Waiting = true),
            (st, wx, wz) => Seek(st, st.X, st.Z, wx, wz, WalkFlags, 0) && (st.Waiting = true),
            // ⚠⚠ CANCELLING A WALK MUST ALSO DROP THE ANSWER THAT IS ALREADY IN THE POST. The port
            // DEFERS a path message: the pathfinder calls OnPathMessage during its own frame, which only
            // parks it on `Answer`, and the staff loop consumes it at the top of that member's next
            // iteration. The original has no such gap — pathfinder.md's delivery is a direct virtual
            // call, `person->vtable[0x144](person+adjust, &msg)`, acted on where it lands. So a message
            // that the original would have processed BEFORE Guard.Dispatch ran instead arrives after it
            // here, and StaffBase.OnPathMessage sets the state from the purpose byte alone (READ:
            // 0x80094304..0x80094358) — wiping state 33 the tick it was set, purpose Patrol -> 5
            // RandomWander. That is why "dispatch asked 1, offered 1, 0 chasing" was the reading for
            // every run: the chase started and was overwritten before Guard.Chase could tick once.
            // ⭐ Dropping the stale answer here is host bookkeeping, not a change to the sim: the
            // binary's Dispatch opens with FreeWaypoints precisely because it is CANCELLING that walk,
            // and an answer to a cancelled walk is not news.
            st => { st.Answer = null; FreeChain(st); },
            // ⭐ THE SAME DOOR THE GATE USES, and it is SYNCHRONOUS as the original is: the catch
            // runs inside the guard's tick rather than being queued.
            (v, m) => { if (_entrance != null) _entrance.DeliverMessage(v, m, 0, 0); },
            () => _map.SpawnTiles,
            // ⚠ THE GATE'S TILE, NOT ITS AREA ID. GateArea() answers which connected PIECE the gate
            // belongs to, which is a different question and would have posted every guard to tile
            // (1,1) — a compile error caught it, and it would not have been visible at runtime.
            // ⚠ THE EXISTING GateTile PROPERTY — "where a guest stands after walking in" — NOT
            // GateArea(), which answers which connected PIECE the gate is in. Both are small ints and
            // both look like an answer; only the return type told me apart. The post the guard takes
            // is meant to be inside the park by the gate, which is exactly what this already is.
            // ⚠⚠ AND IT HAS TO BE COMPUTED, NOT HOPED FOR. `GateTile` is a side effect of
            // GateArea(), which is a DIAGNOSTIC — the reachability readout. In a park with no
            // attractions nothing calls it, so the tile stayed (-1,-1), HasExits answered false, and
            // Guard.GoToExitPoint dropped the guard it had just caught somebody with straight back to
            // Idle without ever walking them out. The tell in the log was the PURPOSE: still 8
            // (ToCulprit), so GoToExitPoint had returned before it could set its own. Asking GateArea()
            // here costs one cached flood fill and makes the value depend on the map rather than on
            // whether a readout happened to run first.
            () => { GateArea(); return GateTile.X < 0 ? null : GateTile; },
            _dice,
            st => { foreach (var sf in _staff) if (sf.S == st) return sf; return null; },
            _entrance.Counters);
        ParkGuardWorld _guardWorld;


        ParkHandymanWorld HandymanWorld() => _handyWorld ??= new ParkHandymanWorld(
            StaffWorld(), _litter,
            () => _rideTargets?.Invoke() ?? (IReadOnlyList<GuestTarget>)System.Array.Empty<GuestTarget>(),
            id => _rides?.RuntimeFor(id)?.Stock,
            // ⚠ WORLD POINTS, NOT TILES: a claimed piece of litter is walked to at its own scattered
            // coordinate (0x80098E94..EB4), so this one does NOT apply Centre.
            (st, wx, wz) =>
            {
                if (!Seek(st, st.X, st.Z, wx, wz, WalkFlags, 0)) return false;
                st.Waiting = true;
                return true;
            },
            () => SlowClockDay?.Invoke() ?? 0);
        ParkHandymanWorld _handyWorld;

        ParkStaffWorld StaffWorld() => _staffWorld ??= new ParkStaffWorld(
            () => _now,
            () => _rideTargets?.Invoke() ?? (IReadOnlyList<GuestTarget>)System.Array.Empty<GuestTarget>(),
            (st, tx, tz) =>
            {
                if (!Seek(st, st.X, st.Z, Centre(tx), Centre(tz), WalkFlags, 0)) return false;
                st.Waiting = true;
                return true;
            });

        public int StaffCount => _staff.Count;

        /// <summary>What the park owes its staff for a month of <paramref name="monthLength"/> days that
        /// ended on total day <paramref name="lastDay"/> (TPW.Sim.Wages).
        ///
        /// ⭐ PRO-RATED BY THE DAYS ACTUALLY WORKED, and the original floors twice on the way — a
        /// percentage first, then the wage — so a mid-month hire is computed from its DAY rather than
        /// from a fraction taken at the end. Collapsing the two divisions gives a different answer for
        /// most part-months.
        ///
        /// ⚠ NOBODY STRIKES YET, so the striking branch (which pays nothing) is never taken here. That
        /// is ParkStaffWorld's gap, not this one's.</summary>
        public Money MonthlyWages(int lastDay, int monthLength)
        {
            var total = Money.Zero;
            foreach (var st in _staff)
            {
                int worked = Math.Min(monthLength, lastDay - st.HiredDay + 1);
                if (worked <= 0) continue;
                total += Wages.Monthly(st.S.Skill, st.S.Kind, worked, monthLength);
            }
            return total;
        }

        /// <summary>Each member of staff's state, tiredness and where it is standing — the half of the
        /// park a headcount cannot see. A staff member that is "there" but never moves and a staff
        /// member that is working look identical from a count.</summary>
        public string StaffReport()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var st in _staff)
                sb.Append($"\n  {st.S.Kind}: {st.S.State}, purpose {st.S.Purpose}, "
                        + $"tired {st.S.Tiredness} morale {st.S.Morale}, "
                        + $"at ({st.X >> 8},{st.Z >> 8}){(st.WaypointHead != WaypointPool.NoChain ? ", walking" : "")}");
            return sb.ToString();
        }

        /// <summary>One sim tick of every member of staff.
        ///
        /// ⚠ THE CLASS HOOK IS NOT CONNECTED. This runs the SHARED machine (TPW.Sim.StaffBase) only -
        /// tiredness, patrolling, wandering, resting, strikes and the arrival table. Each class's own
        /// switch runs BEFORE that fall-through in the original, so a mechanic here cannot yet claim a
        /// broken ride; it walks about like any other staff member. Deliberately left as a gap rather
        /// than approximated, because "a mechanic that looks like it is working" is worse than one that
        /// visibly is not.</summary>
        void TickStaff()
        {
            var world = StaffWorld();
            // ⚠ NUMBER THEM. Four guards all logging "staff Guard: ..." is one member's story told
            // four ways, and I read a chase ending off a line that belonged to a different guard.
            int who = -1;
            foreach (var st in _staff)
            {
                who++;
                // "Nearest" is measured from where THIS member stands, so the world is pointed at it
                // before every call, exactly as GuestBrain is set per guest.
                world.Current = st;
                var wasState = st.S.State;
                var mech = st.S.Kind == StaffKind.Mechanic ? MechanicWorld() : null;
                if (mech != null) mech.Current = st;
                var handy = st.S.Kind == StaffKind.Cleaner ? HandymanWorld() : null;
                if (handy != null) handy.Current = st;
                var ent = st.S.Kind == StaffKind.Entertainer ? EntertainerWorld() : null;
                if (ent != null) ent.Current = st;
                var res = st.S.Kind == StaffKind.Researcher ? _research : null;
                if (res != null) res.Current = st;
                var grd = st.S.Kind == StaffKind.Guard ? GuardWorld() : null;
                if (grd != null) grd.Current = st;

                if (st.Answer is { } m)
                {
                    st.Answer = null;
                    bool found = m == PathMessage.Found;
                    if (mech != null) Mechanic.OnPathMessage(st.S, mech, found);
                    // ⭐ A FAILED WALK TO LITTER MUST RELEASE THE CLAIM. The base's message handler
                    // does not know about claims, so routing a handyman through it left the piece
                    // reserved by someone who never arrived — and NearestUnclaimed skips claimed
                    // pieces, so one refused path could retire a piece of rubbish permanently.
                    else if (handy != null) Handyman.OnPathMessage(st.S, handy, found);
                    else StaffBase.OnPathMessage(st.S, found);
                }
                // ⚠⚠ A LEFTOVER CHAIN IS NOT A WALK. This used to read only
                // `st.WaypointHead != NoChain`, and a chain OUTLIVES a state change: every walk sets
                // State = Walking (StaffBase 159/172/191, StaffClasses 149/160, Guard 182/217/233/241/
                // 251/313) but nothing clears the chain when a class PUSHES a state on top. So an
                // entertainer pelted mid-stroll sat in state 32 while this branch stepped it along the
                // old chain and `continue`d — Entertainer.Tick never ran, Shock never asked for a
                // guard, and the arrival popped 32 straight off the stack. The pelt was swallowed
                // whenever the entertainer happened to be walking, which is most of the time.
                // ⭐ The state is the authority on what a member is DOING; the chain is only how it
                // gets there. Same family as keying a flow check on a field two flows share.
                // ⚠ AND THE WALKING STATES ARE 2 AND 3, NOT 11. `Walking = 11` is the REQUEST; the
                // path-ready message puts the member in 3 and the per-tick walk lives in 2 (StaffState's
                // own notes, behaviour.md §3.1 row "2 (arrival)"). Gating on 11 froze every staff member
                // the moment its path arrived — the obvious name was the wrong state.
                if ((st.S.State == StaffState.WalkToDestination || st.S.State == StaffState.PathReady)
                    && st.WaypointHead != WaypointPool.NoChain)
                {
                    // ⚠⚠ THE GUARD'S STATE-3 OVERRIDE, 0x80097A1C, HAD NO CALLER. Guard.WalkStep is
                    // the MID-WALK catch: purpose 8 checks every step whether it is standing on the
                    // culprit's tile, rather than only when it arrives where the guest used to be. A
                    // chase with only the arrival check is a walk to a stale tile, repeated — which is
                    // why the chase ran and nobody was ever caught. True means the chase ended, and the
                    // ordinary step must not also run.
                    if (grd != null && GuardFor(st.S).WalkStep(grd))
                    {
                        if (LogStaff && st.S.State != wasState)
                            Godot.GD.Print($"[tpw] staff #{who} Guard walkstep: {wasState} -> {st.S.State}, "
                                         + $"purpose {st.S.Purpose}");
                        continue;
                    }
                    // ⭐ THE MECHANIC'S WALK COSTS ARE ITS OWN, and they run BEFORE the base's. Walking
                    // to a breakdown RESTS a mechanic and demoralises it, which is backwards from every
                    // other class and is what the code says (Mechanic's class note).
                    if (mech != null) Mechanic.Arrive(st.S, mech, true);
                    else if (grd != null) GuardFor(st.S).Arrive(grd, true);
                    else StaffBase.Arrive(st.S, world, true);
                    Walk(st);
                    continue;
                }
                if (st.Waiting) continue;

                // ⚠ IdleCheck IS PART OF THE IDLE STATE, NOT A PER-TICK PRE-PASS. StaffBase's own note
                // has it as "slot 51, called from every class's Idle" (0x80094698). Running it on every
                // state meant a tired mechanic was sent to rest out of the middle of a repair, and —
                // worse — could never reach the branch that looks for work at all, because resting
                // returns to Patrolling rather than to Idle.
                // ⚠⚠ THE DIVERT HAPPENS HERE, NOT IN THE CLASS HANDLER, AND THE FIRST VERSION OF THE
                // COUNTER BELOW COULD NOT SEE IT. This pre-pass runs IdleCheck before any class gets
                // a turn, so a tired researcher is already in GoAndRest by the time RunResearcher is
                // called — and RunResearcher has no case for it, falls through to the shared machine,
                // and GoAndRest with nowhere to rest sets Patrolling. The whole diversion opens and
                // closes inside one tick, so the state log prints `Idle -> Patrolling` and it is
                // IDENTICAL to the researcher's own roll choosing to patrol. Counting it inside the
                // class handler read `0 never chose` while every decision was being taken away.
                if (st.S.State == StaffState.Idle)
                {
                    StaffBase.IdleCheck(st.S, world);
                    if (st.S.Kind == StaffKind.Researcher && st.S.State != StaffState.Idle)
                        ResearchDiverted++;
                }
                // ⭐ Entertainer.Tick OWNS ITS OWN DISPATCH, unlike the mechanic's and the handyman's.
                // It returns false only when the member is HELD, in which case the shared machine must
                // be skipped as well — so the return value decides whether anything runs at all, and
                // treating it as "did you handle it" would run the base pass on a held entertainer.
                if (ent != null)
                {
                    var e = _entertainers.TryGetValue(st.S, out var have) ? have : (_entertainers[st.S] = new Entertainer(st.S));
                    if (!e.Tick(ent, _dice)) continue;
                    if (st.S.State == StaffState.Idle || st.S.State == EntertainerStates.Entertaining
                        || st.S.State == EntertainerStates.Shocked)
                    {
                        if (LogStaff && st.S.State != wasState)
                            Godot.GD.Print($"[tpw] staff #{who} {st.S.Kind}: {wasState} -> {st.S.State}, "
                                         + $"{_influence.Count} influence areas, morale {st.S.Morale} [own]");
                        continue;
                    }
                }
                if (grd != null && RunGuard(st, grd))
                {
                    if (LogStaff && st.S.State != wasState)
                        Godot.GD.Print($"[tpw] staff #{who} {st.S.Kind}: {wasState} -> {st.S.State}, purpose {st.S.Purpose}, "
                                     + $"gate counter {grd.Counter80103950} [own]");
                    continue;
                }
                // ⚠ THE RESEARCHER'S Idle RUNS StaffBase.IdleCheck ITSELF, which the pre-pass above
                // has already done this tick. That double call is what the sim's own note describes —
                // the binary rolls BEFORE slot 51 and behaviour.md §3.3's order is retained — so the
                // handler is given the state it expects rather than being "tidied" into one call.
                if (res != null && RunResearcher(st, res))
                {
                    if (LogStaff && st.S.State != wasState)
                        Godot.GD.Print($"[tpw] staff #{who} {st.S.Kind}: {wasState} -> {st.S.State}, "
                                     + $"funding {res.ResearchFunding} [own]");
                    continue;
                }
                if (handy != null && RunHandyman(st, handy))
                {
                    if (LogStaff && st.S.State != wasState)
                        Godot.GD.Print($"[tpw] staff #{who} {st.S.Kind}: {wasState} -> {st.S.State}, purpose {st.S.Purpose}, "
                                     + $"litter {_litter.Pool.Count} live, tired {st.S.Tiredness} [own]");
                    continue;
                }
                if (mech != null && RunMechanic(st, mech))
                {
                    if (LogStaff && st.S.State != wasState)
                        Godot.GD.Print($"[tpw] staff #{who} {st.S.Kind}: {wasState} -> {st.S.State}, purpose {st.S.Purpose}, "
                                     + $"jobs {(_rideJobs?.Invoke().Count ?? -1)}, tired {st.S.Tiredness} [own]");
                    continue;
                }
                switch (st.S.State)
                {
                    case StaffState.Patrolling: StaffBase.Patrol(st.S, world); break;
                    case StaffState.WalkToStrike: StaffBase.WalkToStrike(st.S, world); break;
                    case StaffState.Striking: StaffBase.Strike(st.S, world); break;
                    case StaffState.GoAndRest: StaffBase.GoAndRest(st.S, world); break;
                    case StaffState.Resting: StaffBase.Rest(st.S, world); break;
                    case StaffState.PathReady: st.S.SetState(StaffState.Walking); break;
                    // ⚠ Idle is where the class switch belongs. With none, fall to the base's own
                    // answer for a member with nothing assigned: patrol, which with no rectangle
                    // becomes a random wander.
                    case StaffState.Idle: st.S.SetState(StaffState.Patrolling); break;
                    default: WanderStaff(st); break;
                }
                if (LogStaff && st.S.State != wasState)
                    // ⚠ WAITING AND THE ANSWER ARE THE TWO FIELDS THAT EXPLAIN A WANDER. State 11 is
                    // "walk requested"; it leaves either because an ANSWER arrived (and then the purpose
                    // table decides) or because it fell through to the base machine with neither — which
                    // is a request that was accepted and never replied to. Those look identical in a
                    // state log and are completely different bugs.
                    Godot.GD.Print($"[tpw] staff #{who} {st.S.Kind}: {wasState} -> {st.S.State}, purpose {st.S.Purpose}, "
                                 + $"waiting {st.Waiting}, answer {(st.Answer?.ToString() ?? "none")}, "
                                 + $"jobs {(_rideJobs?.Invoke().Count ?? -1)}, tired {st.S.Tiredness}");
            }
        }

        /// <summary>Print each staff member's state changes (--park-log-rides).</summary>
        public bool LogStaff { get; set; }

        /// <summary>The guard's own states (TPW.Sim.Guard). ⭐ ITS ARRIVAL FALLS THROUGH TO THE BASE
        /// for purposes it does not own (`default: StaffBase.Arrive`), which is why every guard arrival
        /// can safely be routed to Guard.Arrive — checked before wiring, because the handyman's does
        /// NOT fall through and routing everything through that one cost a day.</summary>
        bool RunGuard(Staffer st, ParkGuardWorld grd)
        {
            var g = _guards.TryGetValue(st.S, out var have) ? have : (_guards[st.S] = new Guard(st.S));
            switch (st.S.State)
            {
                case StaffState.Idle: g.Idle(grd); return true;
                case GuardStates.Chase:
                    // ⭐ WHICH OF CHASE'S THREE IDENTICAL-LOOKING ABORTS FIRED. All three cost the same
                    // −5 and all three land in the same state, so the state log alone cannot tell a
                    // culprit who left the park from one who joined a queue from a deadline — and the
                    // first thing this printed was `culprit NULL`, which is what turned a week-old
                    // "the guard never chases" into a one-line answer.
                    if (LogStaff)
                        Godot.GD.Print($"[tpw] guard chase: culprit {(g.Culprit == null ? "NULL" : "set")}, "
                                     + $"waiting {st.Waiting}, chain {st.WaypointHead != WaypointPool.NoChain}, "
                                     + $"now {_now} busyUntil {st.S.BusyUntil}");
                    g.Chase(grd); return true;
                case GuardStates.ToExitPoint: g.GoToExitPoint(grd); return true;
                // ⚠⚠ 46 IS HANDLED BY DOING NOTHING, AND THAT IS THE POINT. The host used to send
                // AtGate straight into CrossGate, so a guard walked itself through the gate on the tick
                // after it arrived and the turnstile's message 9 never had anything to arrive at —
                // which is why `0 admitted by the turnstile` survived every run. READ 0x80098290: the
                // class Update switch indexes 0x800E4978 by state, and entry 46 is 0x800983D0, the
                // function's OWN EPILOGUE. Not the default entry 0x800983BC (52 of the 60 states),
                // which at least calls the shared base pass 0x80094AC4 — 46 does not even get that.
                // Returning true without running anything is that table row; falling through to the
                // base would be the default row, and they are different.
                case GuardStates.AtGate: return true;
                case GuardStates.CrossGate: g.CrossGate(grd); return true;
                case GuardStates.LeavePark: g.LeavePark(grd); return true;
                case GuardStates.ToSpawnPoint: g.GoToSpawnPoint(grd); return true;
                case GuardStates.TakePost: g.TakePost(grd); return true;
                default: return false;          // the shared states are the base's
            }
        }

        /// <summary>The guard holding this staff member's class object, for the entertainer's shock
        /// dispatch — which is the only thing in the park that sends a guard anywhere.</summary>
        public Guard GuardFor(StaffMember staff)
            => _guards.TryGetValue(staff, out var g) ? g : (_guards[staff] = new Guard(staff));

        /// <summary>The researcher's own two states (TPW.Sim.Researcher). ⚠⚠ NEITHER HAD A CALLER.
        /// Research is a TRICKLE by design — three idle decisions in ten choose work and a patrol leg
        /// is many ticks — so a researcher that never runs and a researcher that is merely slow look
        /// identical for a long time, which is how this stayed hidden.</summary>
        bool RunResearcher(Staffer st, ParkResearchWorld res)
        {
            switch (st.S.State)
            {
                case StaffState.Idle:
                    // ⚠ THREE OUTCOMES, AND THE STATE LOG SHOWS TWO. Researcher.Idle runs IdleCheck
                    // FIRST, so an idle researcher can leave for a rest without ever reaching its own
                    // roll — and that exit looks identical in a before/after line to the roll choosing
                    // to patrol. A measured 2 research ticks against ~100 idle decisions reads as a 2%
                    // roll where the constant says 30%; separating "diverted" from "rolled and lost"
                    // is the only way to tell a broken rate from a busy staff member.
                    Researcher.Idle(st.S, res, _dice);
                    if (st.S.State == StaffClassStates.Researching) ResearchPicked++;
                    else if (st.S.State == StaffState.Patrolling) ResearchPatrolled++;
                    else ResearchDiverted++;   // its OWN IdleCheck, the second of the two
                    return true;
                case StaffClassStates.Researching: Researcher.Research(st.S, res); return true;
                default: return false;          // the shared states are the base's
            }
        }

        /// <summary>Start researching one definition in one topic slot. ⚠ A HARNESS DOOR, NOT THE
        /// GAME'S. The player picks topics in a panel this port does not have, so nothing selects one
        /// on its own — and an auto-start here would be inventing the choice rather than testing the
        /// machine. Returns what ResearchSystem.Start said, so a refused pick is visible instead of
        /// looking like a silent success.</summary>
        /// <summary>⭐ THE GAME'S OWN SHORTLIST, WHICH NOTHING CALLED. `ResearchSystem.Candidates` is
        /// the list a player actually picks from — every definition of the slot's types that passes
        /// `CanSelect` — and the port had it tested and unreachable, so every harness pick was a raw
        /// (type, index) that might not have been offerable at all. Picking BY POSITION in this list
        /// is what the menu does, and it is the only way a test can select something legitimate
        /// without hardcoding what the catalogue happens to contain.</summary>
        public string StartResearch(int slot, int choice)
        {
            if (_research?.System is not { } sys) return "no catalogue";
            try
            {
                var list = sys.Candidates(slot);
                if (list.Count == 0) return $"slot {slot} offers nothing";
                if (choice < 0 || choice >= list.Count)
                    return $"slot {slot} offers {list.Count}, not #{choice}";
                var pick = list[choice];
                return sys.Start(slot, pick)
                     ? $"slot {slot} researching type {pick.Type}#{pick.Index} (#{choice} of {list.Count} offered)"
                     : "start refused";
            }
            catch (InvalidOperationException e) { return $"tier scan refused: {e.Message}"; }
        }

        /// <summary>Funding, 70..100, clamped by the sim (ResearchSystem.ApplyFundingSlider).</summary>
        public string SetResearchFunding(int value)
        {
            if (_research?.System is not { } sys) return "no catalogue";
            sys.ApplyFundingSlider(value);
            return $"funding := {value} -> {sys.Funding}";
        }

        public string StartResearch(int slot, int type, int index)
        {
            if (_research?.System is not { } sys) return "no catalogue";
            var def = new ResearchDefinition(type, index);
            try
            {
                if (!sys.CanSelect(slot, def)) return $"refused: slot {slot} may not take type {type}#{index}";
                return sys.Start(slot, def) ? $"slot {slot} researching type {type}#{index}" : "start refused";
            }
            catch (InvalidOperationException e)
            {
                // ⚠⚠ THE SIM'S OWN GUARD, AND IT IS RIGHT TO FIRE. RefreshTier walks five tier bins
                // with NO BOUND in the original (0x8009BA98..C4) and the port fails explicitly instead
                // of reproducing an overread. An EMPTY bin passes the two-thirds rule vacuously
                // (3×0 >= 2×0), and this disc's rides use tiers 0..3 only — bin 4 is empty in all 498
                // level blocks — so the walk runs off the end the moment the four real tiers pass.
                //
                // Caught HERE, at the harness door, and reported. Not "fixed": the sim's note says DO
                // NOT FIX, and a host that swallows this quietly would turn a documented overread into
                // a silent wrong answer. See findings/research.md.
                return $"tier scan refused: {e.Message}";
            }
        }

        /// <summary>Research progress in one line, for the report. ⭐ TOPIC PERCENTAGES, NOT A POINT
        /// TOTAL: points accumulated says the researcher is running, which is the easy half; whether
        /// any TOPIC moves is whether the system is wired to anything.</summary>
        public string ResearchLine()
        {
            if (_research?.System is not { } sys) return "research: no catalogue";
            int researchers = 0;
            foreach (var st in _staff) if (st.S.Kind == StaffKind.Researcher) researchers++;
            var parts = new List<string>();
            for (int slot = 0; slot < ResearchSystem.TopicCount; slot++)
            {
                var t = sys.Topic(slot);
                if (!t.Active && !t.Finished) continue;
                // ⭐ THE STORED RECORD, NOT JUST THE LIVE TOPIC. ResearchSystem.Progress is what a
                // save keeps and what LevelCount reads; a topic's own Percent is the live fixed-point
                // and they are allowed to differ mid-level. Printing one and calling it the other is
                // how a save-round-trip bug hides.
                var stored = sys.Progress(t.Definition);
                parts.Add($"slot {slot} type {t.Definition.Type}#{t.Definition.Index} {t.Percent}%"
                        + $" (stored {stored.CompletedLevels} levels, {stored.Percent}%)"
                        + (t.Finished ? " done" : ""));
            }
            // ⭐ THE CEILING AND THE SHORTLIST, BESIDE THE TOPIC THEY GATE. "slot 1 offers nothing"
            // and "slot 1 offers 9" are the difference between a catalogue that is exhausted and one
            // the tier rule is holding shut, and a percentage on its own can say neither.
            var offer = new List<string>();
            for (int slot = 0; slot < ResearchSystem.TopicCount; slot++)
            {
                int n; try { n = sys.Candidates(slot).Count; } catch (InvalidOperationException) { n = -1; }
                int ceiling; try { ceiling = sys.TierCeiling(slot); } catch (InvalidOperationException) { ceiling = -1; }
                offer.Add($"{slot}:{n}@{ceiling}");
            }
            return $"research: funding {sys.Funding}, {researchers} researchers, "
                 + (parts.Count == 0 ? "no active topics" : string.Join(", ", parts))
                 + $"; offered per slot (count@ceiling) {string.Join(" ", offer)}"
                 + $"; idle decisions: {ResearchPicked} took the work, {ResearchPatrolled} patrolled, "
                 + $"{ResearchDiverted} never chose"
                 + $"; rest: {_staffWorld?.RestGranted ?? 0} of {_staffWorld?.RestAsked ?? 0} asked "
                 + $"({_staffWorld?.RestNoRoom ?? 0} no staff room, {_staffWorld?.RestUnbuilt ?? 0} still building, "
                 + $"{_staffWorld?.RestPathRefused ?? 0} no route at {_staffWorld?.RestRefusedAt ?? "none"}"
                 + (_staffWorld?.RestRefusedAt is { } at && at != "none" && _map != null
                    && int.TryParse(at.Split('(')[1].Split(',')[0], out var dx)
                    && int.TryParse(at.Split(',')[1].Split(')')[0], out var dz)
                    && dx >= 0 && dz >= 0 && dx < _map.Width && dz < _map.Height
                    ? $", that tile is {_map[dx, dz].Type} in area {AreaOf(dx, dz)}" : "") + ")"
                 + $"; map in {AreaCount} connected pieces, main area {MainArea}";
        }

        /// <summary>The handyman's own states (TPW.Sim.Handyman). Returns true when it handled the
        /// state, so the shared machine does not also run on it.
        ///
        /// ⚠⚠ THIS SWITCH IS WHY THE CLEANER DID NOTHING. Every method it calls was ported and tested
        /// months ago; not one of them had a caller. Without the Idle case a handyman never looks for
        /// work, so it falls to the base's answer — patrol, which with no patrol rectangle is a random
        /// wander. From outside, a hired cleaner walking around the park and a hired cleaner working
        /// look exactly the same, which is how this survived so long.</summary>
        bool RunHandyman(Staffer st, ParkHandymanWorld handy)
        {
            switch (st.S.State)
            {
                case StaffState.Idle: Handyman.Idle(st.S, handy, _dice); return true;
                case StaffClassStates.CleaningLitter: Handyman.CleanLitter(st.S, handy); return true;
                case StaffClassStates.EmptyingBin: Handyman.EmptyBin(st.S, handy); return true;
                default: return false;          // the shared states are the base's
            }
        }

        /// <summary>The mechanic's own states (TPW.Sim.Mechanic). Returns true when it handled the
        /// state, so the shared machine does not also run on it.
        ///
        /// ⭐ TWO JOBS, ONE MACHINE: a repair is 56 -> 16 -> 14 -> 17 -> 58 and an upgrade is
        /// 57 -> 52 -> 54 -> 17 -> 58. Nothing in a repair restores reliability; reopening the ride is
        /// the repair, and that is what unsticks a park whose rides have all worn into status 4.</summary>
        bool RunMechanic(Staffer st, ParkMechanicWorld mech)
        {
            switch (st.S.State)
            {
                case StaffState.Idle: Mechanic.Idle(st.S, mech, _dice); return true;
                case MechanicStates.GoToBrokenRide: Mechanic.SetOff(st.S, mech, true); return true;
                case MechanicStates.GoToUpgradeRide: Mechanic.SetOff(st.S, mech, false); return true;
                case MechanicStates.ClosingRide: Mechanic.CloseRide(st.S, mech, false); return true;
                case MechanicStates.ClosingForUpgrade: Mechanic.CloseRide(st.S, mech, true); return true;
                case MechanicStates.Repairing: Mechanic.Work(st.S, mech, false); return true;
                case MechanicStates.Upgrading: Mechanic.Work(st.S, mech, true); return true;
                case MechanicStates.OpeningRide: Mechanic.OpenRide(st.S, mech); return true;
                case MechanicStates.LeavingRide: Mechanic.LeaveRide(st.S, mech); return true;
                default: return false;          // the shared states are the base's
            }
        }

        ParkMechanicWorld MechanicWorld() => _mechWorld ??= new ParkMechanicWorld(
            StaffWorld(),
            () => _rideJobs?.Invoke() ?? (IReadOnlyList<IRideJob>)System.Array.Empty<IRideJob>(),
            (st, tx, tz) =>
            {
                if (!Seek(st, st.X, st.Z, Centre(tx), Centre(tz), WalkFlags, 0)) return false;
                st.Waiting = true;
                return true;
            },
            _staff) { Log = LogStaff };
        ParkMechanicWorld _mechWorld;
        Func<IReadOnlyList<IRideJob>> _rideJobs;

        /// <summary>The placed rides a mechanic can be sent to. Set by the view, which owns them.</summary>
        public void SetRideJobs(Func<IReadOnlyList<IRideJob>> jobs) => _rideJobs = jobs;

        /// <summary>State 5, random wander. ⚠ A STAND-IN, the same one the guests use: the real wander
        /// (§2.7) picks its tile by a rule this does not implement.</summary>
        void WanderStaff(Staffer st)
        {
            if (_walkable.Count == 0) return;
            var (tx, tz) = _walkable[_rng.Next(_walkable.Count)];
            if (Seek(st, st.X, st.Z, Centre(tx), Centre(tz), WalkFlags, 0))
            {
                st.Waiting = true;
                st.S.SetState(StaffState.Walking);
                st.S.Purpose = StaffPurpose.Patrol;
            }
        }

        /// <summary>One sim tick of every guest.</summary>
        public void Tick()
        {
            _now++;
            // The rides a guest can be standing at change whenever something is built, and the scripted
            // harness does not always say so. Refreshing here means the two can never disagree.
            if (_rides != null && _rideTargets != null) _rides.SetRides(_rideTargets());
            if (_entrance != null)
            {
                // ⚠ BOTH LANES EVERY TICK. LaneTick's own first act is `now & 31 == lane`, so the
                // turn-taking lives in the sim; calling one lane per tick here would halve its rate and
                // look like a tuning choice.
                // ⭐ WHY "0 ADMITTED BY THE TURNSTILE" HAS FOUR CAUSES AND THE COUNT HAS ONE VALUE.
                // Turnstile.Admit only reaches its staff loop when the gate counter is non-zero and a
                // batch is open, and it runs HERE, at the top of the tick, while a guard enters state
                // 46 down in TickStaff. So a delivery of 0 means any of: no guard ever held 46, one
                // held it but only between two Admit calls, the counter was zero when it did, or the
                // delivery itself is broken — and those need four different fixes. Sampling on both
                // sides of the staff loop separates the second from the first.
                int atGate = 0;
                foreach (var sf in _staff) if (sf.S.State == GuardStates.AtGate) atGate++;
                if (atGate > 0)
                {
                    AtGateAtAdmit++;
                    if (_entrance.Counter80103950 != 0) AtGateWithCounter++;
                }
                Turnstile.Admit(_entrance);
                Turnstile.LaneTick(_entrance, 0);
                Turnstile.LaneTick(_entrance, 1);
                // ⚠ ONE SAMPLE OF A PERCENTAGE IS NOT A CURVE. Two runs of different lengths reported
                // 18% and then 9%, which reads as progress going BACKWARDS and is not a thing a single
                // endpoint can distinguish from a topic that finished and was replaced, an overflow, or
                // a figure that never moved at all. Print it periodically and the question answers
                // itself. Same rule as the ride report, which is why it rides on the same flag.
                if (LogStaff && _now % 250 == 0 && _research != null)
                    Godot.GD.Print($"[tpw] research tick {_now}: {ResearchLine()}");
            }
            _ticking = true;
            _needs ??= new ParkNeedsWorld(() => _now, () => System.Linq.Enumerable.Select(_staff, s => s.S),
                                          v => _byVisitor.TryGetValue(v, out var gg) ? (gg.X >> 8, gg.Z >> 8) : (0, 0),
                                          st => { foreach (var sf in _staff) if (sf.S == st) return (sf.X >> 8, sf.Z >> 8); return (0, 0); },
                                          // ⚠ RAW 8.8, NOT TILES. LitterPool.Nearby shifts its own arguments down
                                          // by 8; handing it tiles would put every piece beside the origin.
                                          v => _byVisitor.TryGetValue(v, out var gg)
                                             ? _litter.Pool.Nearby(gg.X, gg.Z) : (0, 0),
                                          // ⭐ THE ENTERTAINER'S MARK, AT LAST. This answered None for
                                          // every guest on every tile because nothing in the park owned
                                          // an InfluenceMap — and it would have gone on answering None
                                          // even with one, because the only producer is the entertainer
                                          // and the entertainer never ran.
                                          v => _byVisitor.TryGetValue(v, out var gg)
                                             ? _influence.AtPosition(gg.X, gg.Z) : TileInfluence.None);
            foreach (var g in _guests)
            {
                if (g.V.State != g.LastState) { g.LastState = g.V.State; g.StateSince = _now; }

                // ⭐⭐ THE NEEDS CLOCK, WHICH HAD NO CALLER AT ALL. VisitorNeeds.Tick is "call before the
                // state handler, every tick" and nothing in the game project called it, so every guest
                // kept its spawn needs for its whole visit and no shop ever sold anything. Before the
                // hidden skip, because a guest on a ride still gets hungry.
                VisitorNeeds.Tick(g.V, _needs, _dice);

                // ⭐⭐ AND THE IDLE PASS, WHICH ALSO HAD NO CALLER. Its first act every tick is the
                // LEAVE check — too tired, too unhappy, out of money, or here long enough — so with it
                // unwired nobody ever decided to go home. Measured on a soak: 29 guests, average
                // happiness 0, needs pinned at 100, and not one of them left.
                // ⚠ STATE 0 ONLY. The pass IS state 0's handler; running it on a guest that is queueing,
                // riding or walking would re-decide something another state owns.
                if (g.V.State == VisitorState.Idle && !g.Hidden)
                    VisitorIdle.Tick(g.V, IdleWorld(), _dice);

                // ⚠ COUNTED BEFORE THE HIDDEN SKIP, so a guest that boards stops counting rather than
                // freezing its total at whatever it had. The bit is the queue's own (V+0x5C bit), so
                // this measures exactly what the queue thinks, not what the port thinks.
                if (g.V.InQueue) { g.QueuedTicks++; if (g.QueuedTicks > QueuedWorst) { QueuedWorst = g.QueuedTicks; QueuedWorstState = g.V.State.ToString(); } }
                else g.QueuedTicks = 0;

                // On a ride: the ride owns it entirely (state 21).
                if (g.Hidden) continue;
                // ⚠ A FAILED SEARCH MUST BE ANSWERED OR THE PARK LOCKS UP. The guest asked, the search
                // was accepted, and it came back with "no route" - message 2. If the guest simply keeps
                // its target and asks again, it does so every tick, holds one of the TEN request slots
                // for ever, and starves every other guest in the park. That is not a hypothetical: it
                // is what this code did until the readout showed 10/10 searches out and not one
                // waypoint ever allocated. behaviour.md §2.10's own answer is the fix - drop the
                // target, take the happiness hit, wander.
                // ⚠ A GUEST AT THE GATE MUST NOT BE WANDERED. The failure path below drops the
                // target and sends the guest off to walk about, which for one queueing at a turnstile
                // loses its place and its state. The entrance machine has its own answer for both
                // messages (VisitorEntrance.OnMessage) and it is the one that must run.
                if (_entrance != null && AtTheGate(g) && g.Answer is { } em)
                {
                    g.Answer = null;
                    // ⭐ THE RETURN VALUE IS THE WHOLE POINT. behaviour.md §2.10's message-2 table is by
                    // PURPOSE: 11 re-enters 42, 15 re-enters 36, 14 gets one retry — and "any other",
                    // which includes 9, the walk out, is `happiness −rand(15), boredom +rand(2), target
                    // := 0, state 5` — the ordinary wander below, exactly.
                    //
                    // ⚠ SWALLOWING IT ORPHANS THE GUEST. Both leaving states walk in state 11, so a
                    // leaver whose route to the exit failed stayed in 11 with nothing owning it: outside
                    // the fence, retrying for ever. MEASURED as 1123 route failures from four guests,
                    // every one of them in WalkToBin, before this line read the answer.
                    if (VisitorEntrance.OnMessage(g.V, _entrance,
                            em == PathMessage.Found ? EntranceMessage.PathReady : EntranceMessage.PathFailed, _dice))
                        continue;
                    if (em == PathMessage.Found) continue;   // handled by the walk below
                    g.Answer = PathMessage.Failed;           // fall into the shared "any other" row
                }
                if (g.Answer == PathMessage.Failed)
                {
                    g.Answer = null;
                    g.V.HasTarget = false;
                    g.V.Happiness = Stat.Sub(g.V.Happiness, _rng.Next(15));
                    g.V.Boredom = Stat.Add(g.V.Boredom, _rng.Next(2));
                    RouteFailed++;
                    if (SameArea(g))
                    {
                        RouteFailedSameArea++;
                        int gx = g.X >> 8, gz = g.Z >> 8;
                        _sameAreaFrom[$"{_map[gx, gz].Type}@({gx},{gz})"] = 1;
                        if (g.TargetTileX >= 0 && g.TargetTileZ >= 0 && g.TargetTileX < _map.Width
                            && g.TargetTileZ < _map.Height)
                            _sameAreaTo[$"{_map[g.TargetTileX, g.TargetTileZ].Type}@({g.TargetTileX},{g.TargetTileZ})"] = 1;
                        _sameAreaGuests.Add(g.V);
                        var sk = (g.V.State, g.V.Purpose);
                        _sameAreaStates[sk] = _sameAreaStates.GetValueOrDefault(sk) + 1;
                    }
                    else
                    {
                        RouteFailedStranded++;
                        // ⚠ "STRANDED" IS A COUNT, NOT A DIAGNOSIS. It says the two ends are in
                        // different pieces; it does not say WHICH guests, and a handful of guests in
                        // the wrong piece retrying for ever produce the same number as a broken park.
                        // Without this the figure cannot be acted on.
                        _strandedBy[AreaAt(g.X >> 8, g.Z >> 8)] = _strandedBy.GetValueOrDefault(AreaAt(g.X >> 8, g.Z >> 8)) + 1;
                        _strandedGuests.Add(g.V);
                        _strandedTiles[$"{_map[g.X >> 8, g.Z >> 8].Type}@({g.X >> 8},{g.Z >> 8})"] = 1;
                        var k = (g.V.State, g.V.Purpose);
                        _strandedStates[k] = _strandedStates.GetValueOrDefault(k) + 1;
                    }
                    g.TargetTileX = g.TargetTileZ = -1;
                    Wander(g);
                    continue;
                }
                if (g.WaypointHead != WaypointPool.NoChain) { Walk(g); continue; }
                if (g.Waiting) continue;                       // the answer has not come back yet
                // ⚠ AFTER THE WALK AND THE WAIT, NOT BEFORE THEM. Every entrance state ends by asking
                // for a route, so running this while the guest still had a chain to walk re-asked every
                // tick: the guest never moved, and ten of them held all ten request slots for ever.
                // Same failure as the unanswered search above, reached from the other direction.
                if (_entrance != null && AtTheGate(g)) { RunEntrance(g); continue; }
                if (RunQueueState(g)) continue;
                AskForARoute(g);
            }
            _ticking = false;
            DrainLeavers();
            TickStaff();
            foreach (var sf in _staff)
                if (sf.S.State == GuardStates.AtGate) { AtGateAfterStaff++; break; }
        }

        /// <summary>Ticks on which a guard was in state 46 when the turnstile ran, how many of those
        /// also had a non-zero gate counter (the batch's own precondition), and ticks on which one was
        /// in 46 after the staff loop. AtGateAfterStaff &gt; 0 with AtGateAtAdmit == 0 is a state that
        /// opens and closes inside one tick and can never be seen by a sampler at the top of it.</summary>
        /// <summary>What an idle researcher's decision actually did: took the work, took a patrol leg,
        /// or never got to choose because IdleCheck sent it to rest or to strike.</summary>
        public int ResearchPicked { get; private set; }
        public int ResearchPatrolled { get; private set; }
        public int ResearchDiverted { get; private set; }

        public int AtGateAtAdmit { get; private set; }
        public int AtGateWithCounter { get; private set; }
        public int AtGateAfterStaff { get; private set; }

        /// <summary>The longest any guest has held its in-queue bit without boarding, and what it was
        /// doing at that point. ⚠ A LONG TIME IS NORMAL ON A BUSY RIDE — what is not normal is one guest
        /// far above the rest, which is what "stuck for ever while later joiners get on" looks like.</summary>
        public long QueuedWorst { get; private set; }
        public string QueuedWorstState { get; private set; } = "";

        /// <summary>The spread of time-in-queue right now, worst first: one outlier against a pack is
        /// the signature of a stuck guest; an evenly long list is just a popular ride.</summary>
        /// <summary>Guests that are hidden. ⚠ AN INVARIANT, NOT A STATISTIC: the only reason to hide a
        /// guest is that a ride has it, so this must equal the riders aboard. Anything more is a guest
        /// that has been swallowed — still in the list, never drawn, never ticked, gone for good — which
        /// is what an unimplemented EjectEveryone did to everyone aboard a ride that broke down.</summary>
        /// <summary>How many levels of an attraction have been researched. READ 0x8006AC04: an EXCLUSIVE
        /// upper bound, which is what RidePanel.CanOfferUpgrade compares the next level against.</summary>
        public int ResearchedLevels(int type, int index)
            => _research?.System?.LevelCount(new TPW.Sim.ResearchDefinition(type, index)) ?? 0;

        /// <summary>Hired staff of one kind, and whether that kind is out. ⚠ The port has no strike
        /// model, so the strike answer is a documented false rather than a measurement.</summary>
        public int CountStaff(StaffKind kind)
        {
            int n = 0;
            foreach (var s in _staff) if (s.S.Kind == kind) n++;
            return n;
        }
        public bool IsOnStrike(StaffKind kind) => StaffWorld().IsTypeOnStrike(kind);

        /// <summary>Hand the park its whole catalogue, for tests that need a researched level without
        /// waiting game months for one. ⚠ Debug only.</summary>
        public bool AllResearchUnlocked { set { if (_research != null) _research.AllUnlocked = value; } }

        /// <summary>Tell the mechanics which rides are waiting for an upgrade, and how to clear one.
        /// Both halves are needed: a queue nothing claims and a claim with no queue are equally dead,
        /// and the port had the second.</summary>
        public void SetUpgradeQueue(Func<IRideJob, bool> queued, Action<IRideJob> dequeue)
        {
            MechanicWorld().QueuedForUpgrade = queued;
            MechanicWorld().DequeueUpgrade = dequeue;
        }

        /// <summary>Report one of the advisor's twenty event counters, set by the park. Null until then,
        /// so a park without an advisor simply does not count.</summary>
        public Action<int, int> AdvisorEvent;

        public int HiddenGuests { get { int n = 0; foreach (var g in _guests) if (g.Hidden) n++; return n; } }

        /// <summary>What the guests actually WANT, averaged. ⭐ THE SHOPS' WHOLE INPUT: a purchase is
        /// the base want times the need factor times (happiness + 100)/100, so a park of contented,
        /// unhungry guests buys nothing at any sensible price — and that is indistinguishable from a
        /// broken shop unless somebody prints the needs.</summary>
        public string NeedReport()
        {
            if (_guests.Count == 0) return "no guests";
            long a = 0, b = 0, bored = 0, happy = 0, tired = 0, nausea = 0;
            foreach (var g in _guests)
            {
                a += g.V.NeedA; b += g.V.NeedB; bored += g.V.Boredom;
                happy += g.V.Happiness; tired += g.V.Tiredness; nausea += g.V.Nausea;
            }
            int n = _guests.Count;
            return $"average guest: need A {a / n}, need B {b / n}, bored {bored / n}, "
                 + $"happy {happy / n}, tired {tired / n}, nausea {nausea / n} (of {n})";
        }

        /// <summary>Every guest standing on one tile, in full. ⭐ THE PER-GUEST HALF OF StateReport:
        /// that one says a state is stuck, this one says WHICH guest and what it wanted. A guest's state
        /// alone never explains it -- the purpose and the target are what say whether it is waiting for
        /// something reasonable or holding a goal nothing will ever satisfy.
        ///
        /// ⚠ ALL of them, not the first. Guests pile up several to a tile at queue heads and doorways,
        /// which is exactly where they get stuck, so reporting one would report the wrong one.</summary>
        /// <summary>Guests carrying a QUEUE purpose while standing around. ⚠⚠ A guest that has left a ride
        /// still wearing QueueWalk is a guest the arrival handler will promote back into WaitingInQueue --
        /// a queue it is not in, with no target and no route -- so it stands still until something else
        /// shakes it loose. This is that state, counted: it must be zero.</summary>
        public int StaleQueuePurpose
        {
            get
            {
                int n = 0;
                foreach (var g in _guests)
                    if ((g.V.Purpose == Purpose.QueueWalk || g.V.Purpose == Purpose.QueueShuffle)
                        && (g.V.State == VisitorState.Wander || g.V.State == VisitorState.Idle)) n++;
                return n;
            }
        }

        public string HoverReport(int tileX, int tileZ)
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var g in _guests)
            {
                if (g.X / ParkTerrain.TileUnits != tileX || g.Z / ParkTerrain.TileUnits != tileZ) continue;
                n++;
                sb.Append($"\n  guest #{g.GetHashCode() & 0xFFFF}: {g.V.State}");
                if (g.LastState != (VisitorState)(-1) && g.LastState != g.V.State) sb.Append($" (was {g.LastState})");
                sb.Append($" for {_now - g.StateSince} ticks");
                sb.Append($"; purpose {g.V.Purpose}");
                sb.Append(g.V.HasTarget ? $"; target ({g.TargetTileX},{g.TargetTileZ})" : "; no target");
                // ⚠⚠ THE WARNING WAS ON EVERY IDLE GUEST, and it sent master hunting a routing bug in a
                // park with 0 route failures. `WaypointHead == NoChain` means "not mid-walk", which is
                // the NORMAL condition for a guest standing about: Wander, purpose Finished, no target,
                // no chain is a guest doing exactly what it should. Printing ⚠ there makes the readout
                // cry wolf on the healthy majority and tells you nothing about the sick one.
                // ⭐ A MISSING ROUTE IS ONLY NEWS WHEN THE GUEST WANTS TO GO SOMEWHERE. Warn when it has
                // a target and no way to it and is not waiting for an answer — that is a stuck guest.
                if (g.WaypointHead == WaypointPool.NoChain)
                    sb.Append(g.V.HasTarget && !g.Waiting ? " ⚠ no route TO ITS TARGET"
                            : g.Waiting ? "; waiting for a route" : "; not walking");
                if (g.V.InQueue) sb.Append("; queued");
                if (g.Hidden) sb.Append("; ABOARD");
                sb.Append($"; happy {g.V.Happiness} nausea {g.V.Nausea} tired {g.V.Tiredness}");
            }
            foreach (var st in _staff)
            {
                if (st.X / ParkTerrain.TileUnits != tileX || st.Z / ParkTerrain.TileUnits != tileZ) continue;
                n++;
                sb.Append($"\n  {st.S.Kind} #{st.GetHashCode() & 0xFFFF}: {st.S.State}"
                        + (st.WaypointHead != WaypointPool.NoChain ? "" 
                         : st.Waiting ? "; waiting for a route" : "; not walking"));
            }
            return n == 0 ? "" : $"\nunder the mouse ({tileX},{tileZ}), {n}:" + sb;
        }

        /// <summary>Every state a guest is currently in, with the longest anyone has held it.
        ///
        /// ⭐ READ THE DURATION, NOT THE COUNT. Twenty guests idle is a park; ONE guest that has been
        /// in the same state for the whole run is a handler nobody is calling, and that is invisible in
        /// a headcount. This is the instrument that found state 29: a guest went to be sick, the sim's
        /// handler for it had no caller anywhere in the game project, and it stood there for ever.</summary>
        public string StateReport()
        {
            if (_guests.Count == 0) return "no guests";
            var count = new Dictionary<VisitorState, int>();
            var oldest = new Dictionary<VisitorState, long>();
            foreach (var g in _guests)
            {
                count.TryGetValue(g.V.State, out int n);
                count[g.V.State] = n + 1;
                long held = _now - g.StateSince;
                if (!oldest.TryGetValue(g.V.State, out long o) || held > o) oldest[g.V.State] = held;
            }
            var parts = new List<string>();
            foreach (var kv in count)
                parts.Add($"{kv.Key} x{kv.Value} (oldest {oldest[kv.Key]})");
            parts.Sort();
            return "states: " + string.Join(", ", parts);
        }

        /// <summary>The dirt loop in one line. ⭐ READ IT AS A RATE, NOT A LEVEL: "live" alone cannot
        /// tell a clean park from one whose forty slots are full and whose guests have given up
        /// littering — dropped-minus-cleared is the number that says which.</summary>
        /// <summary>The influence circles, and who is standing in one. ⭐ THE COUNT ALONE IS NOT THE
        /// TEST: twenty areas with nobody inside them changes nothing a guest feels, and "areas &gt; 0"
        /// would pass on a park where the aura is placed somewhere no one goes.</summary>
        /// <summary>Pelt the first entertainer with the first guest, on command (--park-pelt).
        ///
        /// ⚠⚠ A HARNESS DOOR, AND IT EXISTS BECAUSE THE CHAIN WOULD OTHERWISE BE UNTESTABLE. Pelting
        /// happens on one idle roll in a park where the guest has to be beside a performing
        /// entertainer, and a performance needs a guest adjacent first. Measured over 3000 frames with
        /// two entertainers hired: ZERO performances and zero pelts, so the guard's whole reason to
        /// exist — dispatch, chase, catch — never fired once and "2 guards, 0 caught" proves nothing
        /// either way. A feature whose trigger is that rare needs a way to pull it.
        ///
        /// ⭐ IT FORCES THE TRIGGER, NOT THE OUTCOME. It calls the same Entertainer.Pelted the idle
        /// roll calls and then gets out of the way; everything after — the shock, the guard search,
        /// the chase and the catch — runs on its own or does not.</summary>
        public string Pelt()
        {
            Staffer ent = null;
            foreach (var st in _staff) if (st.S.Kind == StaffKind.Entertainer) { ent = st; break; }
            if (ent == null) return "no entertainer hired";
            if (_guests.Count == 0) return "no guests";
            // ⚠⚠ REFUSE A PELT THAT WOULD BE EATEN, AND SAY SO. `Pelted` PUSHES state 32, and the
            // shared path-message handler (StaffBase.OnPathMessage, 0x800942D8) SETS the state with no
            // regard for what is on top of it -- READ: 0x80094304..0x80094358 branches on the purpose
            // byte alone, purpose 5 -> state 0, purpose 1 -> state 5, else 13. So an entertainer with a
            // walk in flight loses the shock the moment its path answer lands, and the guard is never
            // asked. That is the ORIGINAL's behaviour and is NOT to be fixed; what was wrong was the
            // harness pelting into it and reporting "pelted" for something that then vanished. Three
            // runs in four read `dispatch asked 0x` for exactly this reason and cost a morning.
            if (ent.Answer != null || ent.S.State == StaffState.Walking
                || ent.S.State == StaffState.PathReady || ent.WaypointHead != WaypointPool.NoChain)
                return $"not now: {ent.S.Kind} is mid-walk (state {ent.S.State}), a pelt would be eaten";
            // ⚠ AND A GUARD HAS TO BE IN RANGE WHEN THE SHOCK ENDS, NOT WHEN THE PELT LANDS. Shock
            // waits rand(5)*60 ticks before it looks, and it takes the nearest NotBusy guard STRICTLY
            // under 7 tiles (Entertainer.GuardRangeTiles). A guard four tiles away at pelt time can be
            // eight away by then, so this asks for FOUR as a margin rather than seven. It is a harness
            // precondition for observing the chain, not a rule the game has — the game is perfectly
            // happy to pelt an entertainer nobody can help, which is what the -5 no-guard arm is for.
            int near = int.MaxValue;
            foreach (var sf in _staff)
            {
                if (sf.S.Kind != StaffKind.Guard || !TPW.Sim.Guard.NotBusy(sf.S)) continue;
                int d = Math.Abs((sf.X >> 8) - (ent.X >> 8)) + Math.Abs((sf.Z >> 8) - (ent.Z >> 8));
                if (d < near) near = d;
            }
            if (near > 4)
                return near == int.MaxValue ? "not now: no free guard" : $"not now: nearest free guard is {near} tiles away";
            var w = EntertainerWorld();
            w.Current = ent;
            var e = _entertainers.TryGetValue(ent.S, out var have) ? have : (_entertainers[ent.S] = new Entertainer(ent.S));
            e.Pelted(_guests[0].V, w, _dice);
            return $"{ent.S.Kind} pelted, now {ent.S.State}, morale {ent.S.Morale}";
        }

        /// <summary>The guards, and whether any of them is doing anything. ⭐ "3 guards" is a payroll
        /// line; a guard in Chase or holding a post is the feature. The gate counter is printed raw
        /// because its meaning is NOT ESTABLISHED and a label would be an invention.</summary>
        /// <summary>Staff the turnstile has admitted through the gate. Counted because the broadcast
        /// used to land in an empty body and still increment the turnstile's own `sent`.</summary>
        public int StaffAdmitted { get; private set; }

        public string GuardLine()
        {
            int n = 0, chasing = 0, posted = 0, caught = 0;
            foreach (var st in _staff)
            {
                if (st.S.Kind != StaffKind.Guard) continue;
                n++;
                if (st.S.State == GuardStates.Chase) chasing++;
                if (st.S.State == GuardStates.TakePost || st.S.Purpose == GuardStates.Post) posted++;
            }
            caught = _guardWorld?.Caught ?? 0;
            var w = _entWorld;
            return $"guards: {n}, {_guardWorld?.ChasesStarted ?? 0} chases started ({chasing} in one now), "
                 + $"{_guardWorld?.PostTaken ?? 0} posts taken ({posted} walking to one now), {caught} caught"
                 + $", gate counter {(_guardWorld?.Counter80103950 ?? 0)}"
                 + (w == null ? "" : $"; dispatch asked {w.Calls}x, offered {w.Asked} ({w.Busy} busy, {w.TooFar} too far)")
                 + $"; chase saw culprit gone {_guardWorld?.CulpritGone ?? 0}x, last culprit state "
                 + $"{_guardWorld?.LastCulpritState ?? -1}"
                 + $"; chase paths {_guardWorld?.ChasePathAsked ?? 0} asked, "
                 + $"{_guardWorld?.ChasePathRefused ?? 0} refused at entry, "
                 + $"{_guardWorld?.ChasePathNoGuest ?? 0} with no guest"
                 + $"; {StaffAdmitted} admitted by the turnstile "
                 + $"(46 seen {AtGateAtAdmit}x at admit, {AtGateWithCounter}x of those with the counter up, "
                 + $"{AtGateAfterStaff}x after the staff loop)"
                 + $"; post: {_guardWorld?.PostTaken ?? 0} taken of {_guardWorld?.PostTried ?? 0} tiles tried, "
                 + $"{_guardWorld?.PostGaveUp ?? 0} gave up after five, {_guardWorld?.PostNoGate ?? 0} with no gate"
                 + $"; thrown out: {_entrance?.ThrownOutSent ?? 0} told, "
                 + $"{_entrance?.ThrownOutRemoved ?? 0} taken out of the park "
                 + $"({_entrance?.ThrownOutStale ?? 0} already gone)";
        }

        public string InfluenceLine()
        {
            int watched = 0, ents = 0;
            foreach (var st in _staff) if (st.S.Kind == StaffKind.Entertainer) ents++;
            foreach (var g in _guests)
                if (_influence.AtPosition(g.X, g.Z) != TileInfluence.None) watched++;
            return $"influence: {_influence.Count} areas of {InfluenceMap.Capacity}, "
                 + $"{watched} guests standing in one, {ents} entertainers";
        }

        public string LitterLine()
        {
            var (live, dropped, cleaned, bins, neglected) = LitterReport();
            int cleaners = 0;
            foreach (var st in _staff) if (st.S.Kind == StaffKind.Cleaner) cleaners++;
            // ⭐ THE VOMIT SPLIT IS THE PROOF THAT STATE 29 RUNS AT ALL. VisitorActivity.Vomit is the
            // only thing in the game that places kind 0x9E, so a non-zero vomit count cannot be
            // produced by the ordinary littering path — it says the handler fired, which is exactly
            // the thing that had no caller. Zero here with guests present is a freeze, not a tidy park.
            var (rubbish, vomit) = _litter.Pool.SaveCounts();
            return $"litter: {live} live of {LitterPool.Capacity} ({rubbish} rubbish, {vomit} vomit), "
                 + $"{dropped} dropped, {cleaned} cleared, "
                 + $"{bins} bins emptied ({neglected} of them already neglected), {cleaners} cleaners";
        }

        public string QueueWaitReport()
        {
            var waits = new List<(long T, VisitorState S)>();
            foreach (var g in _guests) if (g.V.InQueue) waits.Add((g.QueuedTicks, g.V.State));
            if (waits.Count == 0) return $"nobody queueing; worst ever {QueuedWorst} ticks in {QueuedWorstState}";
            waits.Sort((a, b) => b.T.CompareTo(a.T));
            var parts = new List<string>();
            for (int i = 0; i < waits.Count && i < 6; i++) parts.Add($"{waits[i].T}/{waits[i].S}");
            return $"queueing now ({waits.Count}): {string.Join(" ", parts)}"
                 + (waits.Count > 6 ? " ..." : "") + $"; worst ever {QueuedWorst} ticks in {QueuedWorstState}";
        }

        ParkNeedsWorld _needs;
        ParkIdleWorld _idle;

        /// <summary>The park's own slow day counter, which the leave check measures a visit against.
        /// Set by the view, which owns the calendar. ⚠ WITHOUT IT the "been here long enough" arm of
        /// the leave check compares against zero for ever.</summary>
        public System.Func<int> SlowClockDay;

        /// <summary>The nearest litter bin to a guest within six tiles: a placed FEATURE whose record
        /// byte +0x2E has bit 2 set. Set by the view, which owns the placed attractions.</summary>
        public System.Func<int, int, (int X, int Z)?> NearestBin;

        long _now;

        void AskForARoute(Guest g)
        {
            // ⭐ THE DECISION FIRST. It scores every open attraction for THIS guest - its taste against
            // the ride's intensity, how far it is, and what it has been on lately - and only falls back
            // to wandering when the park has nothing it wants.
            // ⭐ THROUGH THE WHOLE DECISION, not just its scoring half. VisitorDecision.Tick carries the
            // 8-tick stagger - so a park's guests do not all think on the same frame - and the 360-tick
            // cooldown after a failure, which is the thing that stops a guest with an unreachable
            // target asking for ever. Calling ChooseTarget directly skips both, and skipping the second
            // one is what jammed the pathfinder.
            if (_brain != null)
            {
                _brain.SetGuest(g);
                if (g.V.WaitUntil > _now) return;              // still cooling off from a failure
                switch (VisitorDecision.Tick(g.V, _brain, _dice))
                {
                    case DecisionOutcome.HeadingThere:
                        g.Waiting = true; g.Answer = null; ChoseTarget++; return;
                    case DecisionOutcome.NotMyTick: return;
                    default: break;                            // nothing worth doing, or refused
                }
            }

            Wander(g);
        }

        /// <summary>The guest got where it was going.
        ///
        /// ⭐ ARRIVING AT A RIDE MEANS JOINING ITS QUEUE, not riding it. The guest goes into state 41
        /// and the RIDE decides when to take it - see TPW.Sim.RideLoading. Nothing in the guest's own
        /// machine puts it on a ride.
        ///
        /// ⚠ THIS IS A SHORT CUT THROUGH TPW.Sim.VisitorArrival, which owns the real 23-case purpose
        /// table. Only the two cases that matter here are taken: a queued type joins the queue, and
        /// anything else is simply done. Wiring the full table needs the turnstile and the walking
        /// purposes, which are not connected yet.</summary>
        void Arrived(Guest g)
        {
            // ⭐ THE GATE'S ARRIVALS COME FIRST AND DO NOT TOUCH A RIDE. Slot 35's table (0x800E3A94)
            // is keyed on PURPOSE, and 11..16 belong to the entrance — the ride arm below would clear
            // the target of a guest that was only walking to its place in a turnstile lane.
            switch (g.V.Purpose)
            {
                case Purpose.Turnstile11:
                case Purpose.Turnstile12:
                    VisitorEntrance.ArriveAtLaneSlot(g.V, _entrance, g.V.Purpose == Purpose.Turnstile12);
                    return;
                case Purpose.Turnstile14: VisitorEntrance.ArriveAtExitPoint(g.V, _entrance); return;
                case Purpose.Turnstile15: VisitorEntrance.ArriveAtSpawnPoint(g.V, _entrance); return;
                case Purpose.Turnstile16: VisitorEntrance.ArriveAtGate(g.V, _entrance, _dice); break;
                // Arm 9 (0x8008DF3C), the one that jumps past the shared `purpose := 2`: the guest is
                // gone. ⚠ WITHOUT THIS A REFUSED GUEST NEVER LEAVES — it walks out to the gate line and
                // then stands on it for ever, still counted, still eating a search slot. 22 of 27 in the
                // first measured run were doing exactly that.
                case Purpose.LeavePark: g.V.Bubble = 0; RemoveGuest(g); return;
                default: goto notTheGate;
            }
            // ⭐ ARRIVAL IS SELF-CANCELLING and 14 and 15 are the exceptions (VisitorArrival.Tick). A
            // guest that keeps 11, 12 or 16 arrives again the next time its chain empties and picks a
            // second lane; one that loses 14 or 15 stops being on its way out. This line is the whole
            // guard — there is no other.
            if (g.V.Purpose != Purpose.Turnstile14 && g.V.Purpose != Purpose.Turnstile15)
                g.V.Purpose = Purpose.Spent;
            return;
        notTheGate:

            // ⭐⭐ A QUEUE ARRIVAL OWNS ITSELF, AND IT MUST COME BEFORE THE GUARDS BELOW. Purposes 3
            // and 10 mean the guest was walking to its place IN the queue; what decides where it goes
            // next is its in-queue bit, not whether it still has a target. Both guards below clear the
            // target and RETURN — so one arrival that found no target left the guest sitting in
            // WalkToWaypoint for ever, and RideLoading.Load boards only a head in 18. The ride then
            // takes nobody at all, with a full queue and a healthy-looking status.
            //
            // MEASURED before this line existed: "worst stall 309 ticks with the head in
            // WalkToWaypoint" on a park that still served 48 guests — it recovers only when the stuck
            // guest eventually gives up and someone else reaches the front.
            if (g.V.Purpose == Purpose.QueueWalk || g.V.Purpose == Purpose.QueueShuffle)
            {
                _rides?.SetGuest(g);
                // ⚠ IS IT ACTUALLY AT THE QUEUE? This promotes the guest to the state a ride boards from,
                // so an arrival that happened somewhere else entirely is a guest boarding a ride it never
                // reached. Report the distance rather than trusting the purpose.
                if (_rides != null && _promoLog < 20)   // an invariant, not a trace: see below
                {
                    var slot = _rides.QueuePath(g.V);
                    if (slot is { Count: > 0 })
                    {
                        int tx = slot[0].X, tz = slot[0].Y;
                        int gx = g.X / ParkTerrain.TileUnits, gz = g.Z / ParkTerrain.TileUnits;
                        int d = System.Math.Abs(gx - tx) + System.Math.Abs(gz - tz);
                        // ⚠⚠ A GUEST PROMOTED TO BOARDABLE AWAY FROM THE QUEUE HAS NOT QUEUED. State 18
                        // is the only state a ride boards from, so this is the last place to notice a
                        // guest that got here without walking the queue. MEASURED: on map 203 with an
                        // unreachable queue this fires at distance 0 -- the guest really is on the tile --
                        // which is how the straight-line shuffle was found. Keep it: it is cheap and it is
                        // the difference between "a ride took somebody" and "a ride took somebody who
                        // could not have got there".
                        if (d > 1)
                        {
                            _promoLog++;
                            GD.PushWarning($"[tpw] guest promoted to WaitingInQueue at ({gx},{gz}), {d} tiles "
                                         + $"from the queue head ({tx},{tz}), purpose {g.V.Purpose}");
                        }
                    }
                }
                g.V.SetState(VisitorState.WaitingInQueue);
                return;
            }

            if (!g.V.HasTarget || _rides == null) { g.V.HasTarget = false; return; }
            if (!_rides.SetGuest(g)) { g.V.HasTarget = false; return; }

            // ⭐ ARRIVING WHILE WALKING UP A QUEUE IS A DIFFERENT ARRIVAL. Purposes 3 and 10 mean the
            // guest was walking to its place IN the queue, not to the ride - so it stops and waits
            // rather than joining all over again. Without this it re-joins for ever and never reaches
            // the state the ride will actually take it from.
            //
            // ⚠ SIMPLIFIED: the real arrival (VisitorArrival purpose 3/10) re-queries the slot and
            // chooses 18 or 19 by whether the guest is standing on it. Here it always waits, and the
            // ride's own "shuffle everyone up" is what moves the queue. The difference shows as a
            // queue that closes up in steps rather than continuously.
            // ⭐⭐ A SHOP, A STALL AND A FEATURE ARE VISITED, NOT QUEUED — and this used to DROP THE
            // TARGET for all three, so a guest walked to a shop and then forgot why. Nothing ever
            // bought anything, which is how a park whose only income is the gate looks from outside.
            // VisitorArrival.AtAttraction's own two arms, reproduced: a type-2 building is a flat 120
            // ticks, a stall is 120 plus up to 300 more, and both clear flag 0x01 on arrival
            // (0x8008DC04, 0x8008DCC8).
            int type = _rides.TargetType(g.V);
            if (!VisitorQueue.HasQueue(type))
            {
                if (type != 2 && type != 4 && type != 5) { g.V.HasTarget = false; return; }
                g.V.Flag1 = false;
                g.V.WaitUntil = _now + VisitorArrival.ShopDwellTicks
                              + (type == 2 ? 0 : _dice.Next(VisitorArrival.StallExtraDwellMax));
                g.V.SetState(VisitorState.UsingAttraction);
                return;
            }
            g.V.SetState(VisitorState.JoiningQueue);
        }

        /// <summary>Drive the guest's queue-and-ride states. True when it handled the tick.</summary>
        bool RunQueueState(Guest g)
        {
            // ⚠⚠ THE TWO ACTIVITY STATES COME FIRST AND DO NOT NEED A RIDE. They sit above the
            // `_rides == null` guard deliberately: a guest being sick in a park with no ride manager
            // is still stuck, and putting them below meant the freeze survived in exactly the
            // stripped-down harness most likely to be used to reproduce it.
            switch (g.V.State)
            {
                case VisitorState.Vomiting:
                    VisitorActivity.Vomit(g.V, ActivityWorld(), _dice);
                    return true;
                case VisitorState.WatchEntertainer:
                    // ⚠ THE ENTERTAINER MUST STILL EXIST. The sim throws rather than guessing, because
                    // the original dereferences the pointer before testing it — so a guest watching
                    // someone who has been fired is the original's crash, not a state to invent an
                    // answer for. Sack the watch instead of reproducing an access violation.
                    if (g.V.WatchedEntertainer == null) { g.V.PopState(); return true; }
                    VisitorActivity.Watch(g.V, ActivityWorld());
                    return true;
            }

            if (_rides == null) return false;

            switch (g.V.State)
            {
                case VisitorState.JoiningQueue:
                    if (!_rides.SetGuest(g)) { g.V.HasTarget = false; g.V.SetState(VisitorState.Idle); return true; }
                    switch (VisitorQueue.JoinQueue(g.V, _rides))
                    {
                        case JoinOutcome.Walking: g.Waiting = true; g.Answer = null; break;
                        // ⚠ A REFUSED PATHFIND LEAVES THE STATE ALONE and it tries again next tick -
                        // the guest is already in the list with its in-queue bit set by then.
                        case JoinOutcome.PathRefused: break;
                        default: g.V.SetState(VisitorState.Idle); g.V.HasTarget = false; break;
                    }
                    return true;

                case VisitorState.ShuffleForward:
                    if (!_rides.SetGuest(g)) { g.V.SetState(VisitorState.Idle); return true; }
                    VisitorQueue.Shuffle(g.V, _rides);
                    return true;

                case VisitorState.WaitingInQueue:
                    if (!_rides.SetGuest(g)) { g.V.SetState(VisitorState.Idle); return true; }
                    VisitorQueue.Wait(g.V, _rides, _dice);
                    return true;

                // ⭐ THE DWELL, THEN THE TILL. A guest inside a shop or a stall waits out the time
                // the arrival set and then goes through the SAME unloading arm a ride uses — that is
                // where VisitorQueue.Unload dispatches by type to the two purchase routines. Without
                // this the guest stands in the doorway for ever with its target still set.
                case VisitorState.UsingAttraction:
                    if (!_rides.SetGuest(g)) { g.V.HasTarget = false; g.V.SetState(VisitorState.Idle); return true; }
                    if (g.V.WaitUntil > _now) return true;
                    g.V.SetState((VisitorState)22);
                    return true;

                // 21: the ride owns the guest completely. Nothing here, by design.
                case (VisitorState)21:
                    return true;

                case (VisitorState)22:
                    if (!_rides.SetGuest(g)) { g.V.SetState(VisitorState.Idle); return true; }
                    VisitorQueue.Unload(g.V, _rides, _dice);
                    g.V.HasTarget = false;
                    g.V.SetState(VisitorState.Idle);
                    return true;
            }
            return false;
        }

        /// <summary>No target, or the decision gave up: the game's own wander (TPW.Sim.VisitorWander).
        ///
        /// ⭐ IT DOES NOT ASK THE PATHFINDER when the guest is on a path — it walks rand(10) steps
        /// neighbour to neighbour off the tile's link bits and chains the waypoints itself. That is the
        /// whole point of wiring it: the stand-in this replaces asked for a route to a random walkable
        /// tile anywhere on the map, most of which sit on path islands nothing connects to, and a park
        /// of thirty guests produced four thousand failed searches and held all ten request slots.</summary>
        void Wander(Guest g)
        {
            _wander ??= new ParkWanderWorld(_map, _waypoints, () => _now,
                (w, x, y, flags, _) => Seek(w, w.X, w.Z, x, y, (PathFlags)flags, 0) && (w.Waiting = true));
            _wander.Current = g;
            if (VisitorWander.Tick(g.V, _wander, _dice) != WanderOutcome.PathRequested) return;
        }
        ParkWanderWorld _wander;


        void Walk(Walker g)
        {
            int speed = Math.Max(1, g.WalkSpeed);
            int budget = speed;
            int fromX = g.X, fromZ = g.Z;

            while (budget > 0 && g.WaypointHead != WaypointPool.NoChain)
            {
                var (wx, wz) = _waypoints.Decode(g.WaypointHead);
                int dx = wx - g.X, dz = wz - g.Z;
                int dist = Math.Abs(dx) + Math.Abs(dz);

                if (dist == 0 || dist <= budget)
                {
                    g.X = wx; g.Z = wz;
                    budget -= dist;
                    int next = _waypoints.Next(g.WaypointHead);
                    _waypoints.Free(g.WaypointHead);
                    g.WaypointHead = next < 0 ? WaypointPool.NoChain : next;
                    // Arrived: the guest wants something else now. The real machine does this through
                    // the arrival purposes (TPW.Sim.VisitorArrival), which are not wired here yet.
                    if (g.WaypointHead == WaypointPool.NoChain) g.OnArrive?.Invoke(g);
                    continue;
                }

                // Step along the longer axis first, the way a tile grid is walked: the chain's corners
                // are axis-aligned, so this is a straight line in practice.
                if (Math.Abs(dx) >= Math.Abs(dz)) g.X += Math.Sign(dx) * budget;
                else g.Z += Math.Sign(dz) * budget;
                budget = 0;
            }
            Stride(g, fromX, fromZ);
            Place(g);
        }

        void Place(Walker g)
        {
            if (g.Walked == g.LastWalked) { if (g.StillFor < 1000) g.StillFor++; }
            else { g.StillFor = 0; g.LastWalked = g.Walked; }
            g.Inst.Visible = !g.Hidden;
            if (g.Hidden) return;
            float u = ParkTerrain.TileUnits;
            int gh = ParkCamera.GroundHeight(_map, g.X, g.Z);
            var feet = new Vector3(g.X / u, gh / u, -g.Z / u);
            if (_sprites == null) { g.Inst.Position = feet + new Vector3(0, 0.21f, 0); return; }
            // ⭐ THE FACING IS RE-DERIVED HERE, EVERY DRAW. The quad is aimed at the camera in Draw, and
            // which of the eight sprites it wears is the walk direction measured against that same
            // camera — so both halves move together when the view turns. Deciding it on the STEP left
            // a standing guest wearing a facing from a camera angle that no longer existed.
            // ⭐ CARDINAL FACING PLUS THE CAMERA'S OCTANT. A person stores which of FOUR ways it walks;
            // the eight drawn angles come from adding where the camera is standing. Deriving eight straight
            // from the velocity -- which is what this did -- gives a person who turns as you orbit them and
            // reads correctly from exactly one angle. findings/people-sprites.md.
            g.Facing = PeopleSheet.CardinalFacing(g.DirX, g.DirZ);
            // ⭐ READ 0x800323B0: the frame is a per-drawable counter that steps every FOURTH DRAW and
            // resets to zero whenever the person's animation changes. It is NOT distance walked, which is
            // what this port used -- that ties the stride to the speed, so a guest slowed by a crowd
            // shuffles in slow motion and one hurrying flickers.
            //
            // ⚠ FOUR DRAWS ARE FOUR VIDEO FRAMES, AND THIS RUNS ON THE SIM TICK. A tick is
            // ParkClock.FramesPerTick frames, so the counter steps every 4 / FramesPerTick ticks. Same
            // trap as the advisor's clock, which cost an evening: a cadence in frames is not a cadence in
            // ticks, and here the two differ by exactly the factor that makes a walk look half speed.
            int anim = g is Guest ag && ag.V.State == VisitorState.Vomiting ? 2
                     : g.StillFor >= StillBeforeIdle ? 1 : 0;
            StepFrame(g, anim, anim == 2 ? PeopleSheet.VomitFrames
                             : anim == 1 ? PeopleSheet.IdleFrames : PeopleSheet.WalkFrames);

            // ⭐ A GUEST THAT IS NOT WALKING IS NOT DRAWN WALKING. The 26 sprites before the walk are the
            // poses, and the game shows the IDLE one whenever a person stands (findings/people-sprites.md
            // §4): queueing, waiting at a door, stopped to look at something. Drawing a stride frame
            // instead leaves the park full of people frozen mid-step, which is what it looked like.
            // ⚠ Guests only: Place draws staff through here too, and a member of staff has no visitor
            // state. Their own poses are a separate question (staff.md has different tables).
            // The relative facing goes in for a pose as well: the sprite ignores it, the MIRROR does not.
            int rel = (g.Facing + CameraOctant) & 7;
            if (g is Guest sick && sick.V.State == VisitorState.Vomiting)
                _sprites.Draw(g.Inst, g.Block, rel, g.Frame, feet, CameraForward,
                              PeopleSheet.VomitFirst, PeopleSheet.VomitFrames);
            else if (g.StillFor >= StillBeforeIdle)
                _sprites.Draw(g.Inst, g.Block, rel, g.Frame, feet, CameraForward,
                              PeopleSheet.IdleFirst, PeopleSheet.IdleFrames);
            else
                _sprites.Draw(g.Inst, g.Block, rel, g.Frame, feet, CameraForward);
        }

        /// <summary>How many draws a guest must have gone nowhere before it stands rather than strides.
        /// ⚠ NOT ZERO: a walker is momentarily still between steps, and switching on the first still draw
        /// makes every guest flicker between standing and walking as it goes.</summary>
        const int StillBeforeIdle = 3;

        /// <summary>READ 0x800323B0 / 0x8003236C. One step of a walker's animation frame.</summary>
        static void StepFrame(Walker g, int anim, int frames)
        {
            if (anim != g.Anim) { g.Anim = anim; g.Frame = 0; g.AnimTick = 0; return; }
            const int drawsPerStep = 4;
            int ticksPerStep = Math.Max(1, drawsPerStep / TPW.Sim.ParkClock.FramesPerTick);
            if (++g.AnimTick < ticksPerStep) return;
            g.AnimTick = 0;
            g.Frame = frames <= 0 ? 0 : (g.Frame + 1) % frames;
        }

        /// <summary>Draw a rider in its seat, wherever the ride is holding it this frame.
        ///
        /// ⭐ IT DRAWS WITHOUT MOVING ANYTHING. On 54 of the 59 flat rides the original writes NOTHING to a
        /// rider: the sprite is composed from camera x ride x bone at draw time and the guest's own position
        /// is left where it was when it boarded (findings/rider-positions.md §2.4). So this is a draw, not a
        /// placement -- `g.X`/`g.Z` stay put, and unloading puts the guest out at the exit from where the sim
        /// always thought it was. Ride the sprite, not the guest.</summary>
        public void DrawRider(Guest g, Vector3 feet, Vector3 outward)
        {
            if (g?.Inst == null) return;
            g.Inst.Visible = true;
            if (_sprites == null) { g.Inst.Position = feet + new Vector3(0, 0.21f, 0); return; }
            g.Facing = PeopleSheet.CardinalFacing((int)(outward.X * 256), (int)(-outward.Z * 256));
            _sprites.RestoreWalkTexture(g.Inst);
            _sprites.Draw(g.Inst, g.Block, (g.Facing + CameraOctant) & 7, g.Frame, feet, CameraForward);
        }

        /// <summary>Draw a rider as the head the game draws: sheet 416, picked by the guest's own visitor
        /// type and the seat's orientation. Falls back to the standing sprite when the common sheet has
        /// not been baked, so a park without it still shows somebody in the seat.</summary>
        public void DrawRiderHead(Guest g, Vector3 seat, int facing, int band, float roll, Vector3 outward)
        {
            if (g?.Inst == null) return;
            g.Inst.Visible = true;
            if (_sprites == null || !_sprites.HasHeads) { DrawRider(g, seat, outward); return; }
            int sprite = TPW.Sim.RiderSprites.For(g.V.VisitorType, facing, band, out bool mirror);
            _sprites.DrawHead(g.Inst, sprite, mirror, roll, seat, CameraForward);
        }

        /// <summary>The riders of one ride, in boarding order -- which is the order the seats are handed
        /// out, so rider i belongs in seat i.</summary>
        public IReadOnlyList<Guest> RidersOf(int entry) => Rides?.RuntimeFor(entry)?.Riders;

        /// <summary>The people sheet to draw guests from, and where the camera is looking, which is what
        /// decides which of the eight drawn facings each guest shows (GuestSprites).</summary>
        public void SetSprites(GuestSprites sprites) => _sprites = sprites;
        public void SetCommonSheet(TextureSheet common) => _sprites?.SetCommonSheet(common);
        public Vector3 CameraForward { get; set; } = new Vector3(0, 0, -1);

        /// <summary>Where the camera stands, as an octant (GuestSprites.CameraOctant). Set from the view's
        /// right vector; it is added to every person's own cardinal facing to choose the drawing.</summary>
        public int CameraOctant { get; private set; }
        public Vector3 CameraRight { set => CameraOctant = GuestSprites.CameraOctant(value); }

        /// <summary>Re-aim and re-dress every guest and every member of staff for the camera where it is
        /// NOW. Called once a drawn frame, not once a sim tick.
        ///
        /// ⚠ A STANDING GUEST IS NOT REDRAWN BY THE WALK. Place only ran after a step, so anyone idle,
        /// queueing or waiting at a gate kept the quad angle it was given when it last moved: turn the
        /// camera and they went edge-on and vanished. Everyone gets redrawn, movers included — it is one
        /// transform and two UV numbers each.</summary>
        public void Redraw()
        {
            foreach (var g in _guests) Place(g);
            foreach (var st in _staff) Place(st);
        }
        int _promoLog;
        GuestSprites _sprites;

        /// <summary>A step of the walk: the facing the camera sees and how far through the stride, from
        /// how far the guest has actually moved. ⚠ The port's own timing — the game advances its frames
        /// on the animation clock. ✅ TRACED SINCE: the frame is not driven by distance at all -- see
        /// <see cref="StepFrame"/>.</summary>
        void Stride(Walker g, int fromX, int fromZ)
        {
            int dx = g.X - fromX, dz = g.Z - fromZ;
            if (dx == 0 && dz == 0) return;
            g.DirX = dx; g.DirZ = dz;
            g.Walked += Math.Abs(dx) + Math.Abs(dz);
        }

        /// <summary>Put a guest off a ride at its exit tile, or at its door when it has no exit.
        ///
        /// ⚠ THE EXIT IS NOT THE ENTRANCE. A ride with a separate exit puts guests out the other side,
        /// which is what stops the queue and the people leaving from walking through each other. A
        /// shop or a feature has no exit tile at all and the door is right.</summary>
        /// <summary>Put a guest at the ride's DOOR, which is where it should be standing the moment it
        /// boards. ⚠ WITHOUT THIS A GUEST VANISHED AT THE HEAD OF THE QUEUE — several tiles short of the
        /// thing it was getting on — because boarding hides it where it happened to be standing. The
        /// hide is immediate and correct; the position it was hidden AT was not.</summary>
        public void PlaceAtDoor(Guest g, AttractionDefinition rec, int ox, int oz, int rot)
        {
            var (w, d) = rec.Footprint(rot);
            var door = rec.EntranceTile(ox, oz, rot)
                    ?? rec.ExitTile(ox, oz, rot)
                    ?? (ox + w / 2, oz + d / 2);
            g.X = Centre(door.X);
            g.Z = Centre(door.Z);
            Place(g);
        }

        public void PlaceAtExit(Guest g, AttractionDefinition rec, int ox, int oz, int rot)
        {
            var (w, d) = rec.Footprint(rot);
            var exit = rec.ExitTile(ox, oz, rot)
                    ?? rec.EntranceTile(ox, oz, rot)
                    ?? (ox + w / 2, oz + d / 2);
            g.X = Centre(exit.X);
            g.Z = Centre(exit.Z);
            Place(g);
        }

        /// <summary>Take every guest out of the park, and their routes with them. Called when the map
        /// changes underneath them.</summary>
        public void Clear()
        {
            foreach (var g in _guests)
            {
                if (g.WaypointHead != WaypointPool.NoChain) _waypoints.FreeChain(g.WaypointHead);
                g.Inst?.QueueFree();
            }
            _guests.Clear();
            _byVisitor.Clear();
            _leaving.Clear();
            foreach (var st in _staff)
            {
                // ⚠ RELEASE THE LITTER CLAIM BEFORE THE HANDYMAN GOES. A claimed piece is skipped by
                // NearestUnclaimed for ever, so a staff member deleted mid-walk would retire its target
                // permanently — forty map changes and the park can hold no litter at all while looking
                // perfectly clean. The rubbish itself stays: the original does not sweep the park
                // because somebody put a path down.
                if (st.S.TargetLitter != null) HandymanLitter.Unclaim(st.S);
                FreeChain(st);
                st.Inst?.QueueFree();
            }
            _staff.Clear();
            _finder.ResetAfterBuildItemPlaced();
        }

        /// <summary>A build item has been put down: the original wipes both pools and drops every
        /// search without a word (0x800EBBE4), leaving walkers pointing at freed waypoints. Reproduced,
        /// with the heads cleared HERE rather than in the pool - the sim keeps the original's bug, and
        /// the host is the right place to decline to inherit it.</summary>
        public void OnBuildItemPlaced()
        {
            _finder.ResetAfterBuildItemPlaced();
            foreach (var g in _guests) { g.WaypointHead = WaypointPool.NoChain; g.Waiting = false; }
        }

        /// <summary>Rebuild the walkable list after the map changes.</summary>
        public void MapChanged()
        {
            _rides?.QueuesChanged();
            _walkable.Clear(); _paths.Clear();
            for (int x = 0; x < _map.Width; x++)
                for (int z = 0; z < _map.Height; z++)
                    if (_map[x, z].IsWalkable) _walkable.Add((x, z));
            RebuildAreas();
        }

        /// <summary>Stamp every tile with which connected piece of the map it belongs to, using the
        /// SEARCH'S OWN step test (Pathfinder.CanStepThrough) rather than a second opinion about
        /// walkability. A separate reachability rule beside the pathfinder agrees with it until one of
        /// them changes, and then answers confidently for the other.
        ///
        /// ⚠ WEAKLY CONNECTED: an edge in EITHER direction joins two tiles, where the search is
        /// directed. That makes "different piece" a solid NO and "same piece" only a strong maybe,
        /// which is the right way round for triage.
        ///
        /// ⚠ A FOOTPRINT OR ENTRANCE TILE HAS NO THROUGH-EDGES and so belongs to no piece of its own —
        /// they are enterable only as a destination. <see cref="AreaAt"/> answers for those with the
        /// best piece among their neighbours, which is what "can somebody standing nearby get here"
        /// actually means.</summary>
        void RebuildAreas()
        {
            _gateAreaCache = -1;
            _areaSize.Clear();
            int w = _map.Width, h = _map.Height;
            _area = new int[w, h];
            var parent = new int[w * h];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }

            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    for (int dir = 0; dir < 2; dir++)          // +x and +z only; the reverse is the same pair
                    {
                        int nx = x + (dir == 0 ? 1 : 0), nz = z + (dir == 0 ? 0 : 1);
                        if (nx >= w || nz >= h) continue;
                        if (_finder.CanStepThrough(x, z, dir, WalkFlags)
                         || _finder.CanStepThrough(nx, nz, dir + 2, WalkFlags))
                            Union(x * h + z, nx * h + nz);
                    }

            var id = new Dictionary<int, int>();
            var hasWalkable = new HashSet<int>();
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                {
                    int root = Find(x * h + z);
                    if (!id.TryGetValue(root, out int n)) id[root] = n = id.Count + 1;
                    _area[x, z] = n;
                    if (_map[x, z].IsWalkable)
                    {
                        hasWalkable.Add(n);
                        _areaSize[n] = _areaSize.GetValueOrDefault(n) + 1;
                    }
                }
            // Only pieces somebody can actually stand in. Every lone grass tile is its own piece and
            // counting those makes the number a map statistic instead of a park one.
            Areas = hasWalkable.Count;
        }
        int[,] _area;

        /// <summary>How many connected pieces the walkable map is in. One is a healthy park.</summary>
        public int Areas { get; private set; }

        /// <summary>Walkable tiles in a piece, and the piece a tile is in. ⭐ A RIDE WHOSE DOOR SITS IN A
        /// TWO-TILE PIECE IS UNREACHABLE and nothing else in the park says so: the guests simply never
        /// arrive, the ride reports itself healthy and waiting, and the only trace is a routing failure
        /// counted against the park as a whole. It cost two of us an hour each today.
        ///
        /// ⚠ NOT A REASON TO REFUSE THE PLACEMENT. The game lets you drop a ride anywhere and build its
        /// queue afterwards, so an unreachable entrance is an ordinary half-finished state, not an error.
        /// Naming it is right; forbidding it would break the way the game is played.</summary>
        readonly Dictionary<int, int> _areaSize = new();
        public int AreaOf(int x, int z) => AreaAt(x, z);
        public int AreaTiles(int area) => _areaSize.GetValueOrDefault(area);
        /// <summary>How many separate walkable pieces the park is in. One is healthy.</summary>
        public int AreaCount { get { GateArea(); return _areaSize.Count; } }

        /// <summary>The piece the park's walkable network is, taken as the biggest one.</summary>
        public int MainArea
        {
            get
            {
                int best = 0, n = 0;
                foreach (var kv in _areaSize) if (kv.Value > n) { n = kv.Value; best = kv.Key; }
                return best;
            }
        }

        /// <summary>The piece a tile belongs to, answering for a footprint or entrance tile with the
        /// piece of whichever neighbour has one.</summary>
        int AreaAt(int x, int z)
        {
            if (_area == null || x < 0 || z < 0 || x >= _map.Width || z >= _map.Height) return 0;
            var t = _map[x, z].Type;
            if (t != TileType.BuildingFootprint && t != TileType.AttractionEntrance) return _area[x, z];
            int[] dx = { 1, 0, -1, 0 }, dz = { 0, 1, 0, -1 };
            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i], nz = z + dz[i];
                if (nx < 0 || nz < 0 || nx >= _map.Width || nz >= _map.Height) continue;
                if (_map[nx, nz].IsWalkable) return _area[nx, nz];
            }
            return _area[x, z];
        }

        /// <summary>The piece of the map a guest is standing in once it has walked through the gate:
        /// the first path tile the entrance's own walk-in scan would find, or the biggest piece there is
        /// when no entrance is wired. This is the "here" that everything else is measured against.</summary>
        /// <summary>Is this tile joined to the rest of the park — can a guest standing inside the gate
        /// walk to it? For anything that wants to ASK before the fact: the build tool, a panel row, a
        /// warning when a ride is placed with its exit on a stub.
        ///
        /// ⭐ NOT A RULE, AND IT MUST NOT BECOME ONE. The game validates none of this — the queue tool's
        /// join marker at build time is the only feedback it ever gives, and it happily lets you place a
        /// ride nobody can reach. Warn, colour a row, refuse nothing.
        ///
        /// ⚠ CALL <see cref="MapChanged"/> AFTER A BUILD FIRST. The flood fill is cached, and asking on
        /// a stale one answers about the park as it was before the path was laid — which is precisely
        /// the moment a placer wants to ask.</summary>
        public bool JoinedToGate(int x, int z)
        {
            if (_area == null) RebuildAreas();
            return AreaAt(x, z) == GateArea();
        }

        /// <summary>⚠⚠ THE WEAK ANSWER AND THE REAL ONE, SIDE BY SIDE. `JoinedToGate` compares area
        /// ids, and RebuildAreas unions a pair when an edge exists in EITHER direction while the search
        /// is DIRECTED — so the port can tell a player "all attractions reachable" and then watch every
        /// guest fail to route there. This walks the gate's piece twice with the SEARCH'S own step test,
        /// forwards and backwards, and reports the tiles where the two disagree.
        ///
        /// ⭐ BOTH DIRECTIONS, because a guest has to get back as well as in. A tile you can walk to and
        /// not leave is the shape that strands a guest at a ride exit; one you can leave and not reach
        /// is a ride nobody arrives at. They are different bugs and the same line should not blur them.
        ///
        /// Returns the counts and a short list of example tiles, or an empty string when the weak and
        /// directed answers agree — which is the healthy case and should say so rather than print
        /// nothing and look like a missing line.</summary>
        public string DirectedReachReport()
        {
            if (_map == null) return "";
            if (_area == null) RebuildAreas();
            int gate = GateArea();
            var (gx, gz) = GateTile;
            if (gx < 0) return "directed reach: no gate tile";

            bool[,] outward = Flood(gx, gz, forward: true);
            bool[,] inward = Flood(gx, gz, forward: false);
            var noWayIn = new List<string>();
            var noWayBack = new List<string>();
            int inCount = 0, backCount = 0;
            for (int x = 0; x < _map.Width; x++)
                for (int z = 0; z < _map.Height; z++)
                {
                    if (!_map[x, z].IsWalkable || AreaAt(x, z) != gate) continue;
                    // ⚠ NAME THE TYPE. An attraction's door and exit tiles are DIRECTIONAL BY DESIGN
                    // (paths.md §9: they become type 7/8 with a facing bit), so a one-way door is the
                    // game working, not a bug — and a check that cannot tell those from a one-way PATH
                    // tile cries wolf on every park with a ride in it. The type is the discriminator.
                    if (!outward[x, z]) { inCount++; if (noWayIn.Count < 6) noWayIn.Add($"{_map[x, z].Type}@({x},{z})"); }
                    if (!inward[x, z]) { backCount++; if (noWayBack.Count < 6) noWayBack.Add($"{_map[x, z].Type}@({x},{z})"); }
                }
            if (inCount == 0 && backCount == 0)
                return "directed reach: agrees with the area map on every tile in the gate's piece";
            return $"⚠ DIRECTED REACH DISAGREES with the area map: {inCount} tiles the gate cannot reach"
                 + (noWayIn.Count > 0 ? $" ({string.Join(" ", noWayIn)})" : "")
                 + $", {backCount} that cannot reach the gate"
                 + (noWayBack.Count > 0 ? $" ({string.Join(" ", noWayBack)})" : "")
                 + " — a DOOR or ENTRANCE here is by design; a PATH here is a guest that cannot walk out";
        }

        /// <summary>Flood the map from one tile using the pathfinder's own step test in ONE direction.
        /// <paramref name="forward"/> false walks the edges backwards, answering "who can reach here".</summary>
        bool[,] Flood(int sx, int sz, bool forward)
        {
            int w = _map.Width, h = _map.Height;
            var seen = new bool[w, h];
            var queue = new Queue<(int X, int Z)>();
            seen[sx, sz] = true; queue.Enqueue((sx, sz));
            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();
                for (int dir = 0; dir < 4; dir++)
                {
                    int nx = x + (dir == 0 ? 1 : dir == 2 ? -1 : 0);
                    int nz = z + (dir == 1 ? 1 : dir == 3 ? -1 : 0);
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h || seen[nx, nz]) continue;
                    // Forward: can I step from here to there. Backward: can somebody there step to here.
                    bool ok = forward ? _finder.CanStepThrough(x, z, dir, WalkFlags)
                                      : _finder.CanStepThrough(nx, nz, (dir + 2) & 3, WalkFlags);
                    if (!ok) continue;
                    seen[nx, nz] = true; queue.Enqueue((nx, nz));
                }
            }
            return seen;
        }

        /// <summary>The three connectivity questions an attraction has, separately, because they have
        /// different answers: guests reach it through the DOOR, leave it through the EXIT, and queue on
        /// tiles that have to meet a path. Any of them can be false while the others are true.</summary>
        public (bool Door, bool Exit) AttractionJoined(GuestTarget t)
            => (JoinedToGate(t.DoorX, t.DoorZ), t.ExitX < 0 || JoinedToGate(t.ExitX, t.ExitZ));

        /// <summary>The tile the reachability check measures from: where a guest stands after walking in.</summary>
        public (int X, int Z) GateTile { get; private set; } = (-1, -1);

        int _gateAreaCache = -1;

        /// <summary>⚠ CACHED PER FLOOD FILL. The fallback branch walks the whole map, and this is now
        /// called from the on-screen readout once per attraction per refresh — which turned a diagnostic
        /// into a per-frame map scan the first time it was put in front of a human.</summary>
        int GateArea()
        {
            if (_gateAreaCache >= 0) return _gateAreaCache;
            return _gateAreaCache = GateAreaUncached();
        }

        int GateAreaUncached()
        {
            if (_entrance != null)
            {
                var e = _entrance.EntranceTile(0);
                // ⚠ THE FIRST *PATH* TILE, NOT THE FIRST WALKABLE ONE. Grass outside the fence has a
                // piece of its own, so taking the first non-zero answer put the reference OUTSIDE the
                // park and reported every ride in the place as unreachable. WalkIn's own rule is the
                // first type-2/13 tile scanning +y, and that is the tile a guest actually stands on.
                //
                // ⭐ AND THE GATE LINE IS NOT A LINK. A guest crosses it through the turnstile, not
                // through the pathfinder, so the tiles outside and inside are legitimately different
                // pieces. Anything measuring "reachable" from outside is measuring the wrong park.
                for (int dz = 0; dz <= 15; dz++)
                {
                    var t = _map[e.X, e.Y + dz].Type;
                    if (t == TileType.Path || t == TileType.PathQueueOverlap)
                    {
                        GateTile = (e.X, e.Y + dz);
                        return AreaAt(e.X, e.Y + dz);
                    }
                }
            }
            var size = new Dictionary<int, int>();
            for (int z = 0; z < _map.Height; z++)
                for (int x = 0; x < _map.Width; x++)
                    if (_area[x, z] != 0) size[_area[x, z]] = size.GetValueOrDefault(_area[x, z]) + 1;
            int best = 0, bestN = 0;
            foreach (var kv in size) if (kv.Value > bestN) { best = kv.Key; bestN = kv.Value; }
            return best;
        }

        /// <summary>Which attractions a guest standing inside the gate cannot walk to.
        ///
        /// ⭐ THE GAME HAS NO SUCH CHECK and this is deliberately not one. Nothing in the original ever
        /// validates that a queue reached a path: the only feedback is the queue tool's join marker at
        /// BUILD time, and after that a queue one tile short is silently never routed into, for ever,
        /// while the ride sits there open, staffed and empty. This is a DIAGNOSTIC over the port, not a
        /// rule ported from the disc — it must never refuse a build the game would allow.
        ///
        /// It is the same flood fill the route-failure split already runs, asked a different question.</summary>
        public string Reachability()
        {
            var targets = _rideTargets?.Invoke();
            if (targets == null || targets.Count == 0) return "nothing built";
            if (_area == null) RebuildAreas();
            int gate = GateArea();
            var bad = new List<string>();
            foreach (var t in targets)
            {
                // ⭐ THROUGH THE SAME PUBLIC ANSWER THE BUILD TOOL GETS. If the printed line and the
                // API could disagree, the one nobody runs is the one that would be wrong.
                var joined = AttractionJoined(t);
                if (!joined.Door) bad.Add($"#{t.Id} door ({t.DoorX},{t.DoorZ}) in piece {AreaAt(t.DoorX, t.DoorZ)}");
                // ⭐ THE EXIT IS ITS OWN QUESTION AND IT IS THE ONE THAT STRANDS PEOPLE. A ride whose
                // door is reachable takes guests in happily and then puts them down on the far side; if
                // THAT tile is not joined to the park they stand there failing every route, waiting out
                // the decision cooldown between each one. From outside it reads as "guests freeze at the
                // exit of rides for a while after they get off", which is exactly how it was reported.
                if (!joined.Exit)
                    bad.Add($"#{t.Id} EXIT ({t.ExitX},{t.ExitZ}) in piece {AreaAt(t.ExitX, t.ExitZ)}");
            }
            // ⭐ THE PIECE NUMBERS ARE THE DIAGNOSIS, not decoration. All the strays sharing ONE piece
            // means the paths are fine and it is the GATE that is not joined to them; each in its own
            // means the runs never met. Without them "unreachable" says only that something is wrong.
            return bad.Count == 0
                ? $"all {targets.Count} attractions reachable from the gate at {GateTile} (piece {gate})"
                : $"⚠ {bad.Count}/{targets.Count} UNREACHABLE from the gate at {GateTile} (piece {gate}): {string.Join(", ", bad)}"
                  + " — a queue that stops one tile short of a path looks connected and never is";
        }

        bool SameArea(Walker wk)
            => wk.TargetTileX >= 0 && AreaAt(wk.X >> 8, wk.Z >> 8) == AreaAt(wk.TargetTileX, wk.TargetTileZ);
    }
}
