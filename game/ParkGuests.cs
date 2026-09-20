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

        /// <summary>The last step this walker took, in WORLD units — which way it is actually going.
        ///
        /// ⚠ THIS, NOT THE FACING INDEX, IS WHAT A WALKER OWNS. Which of the eight drawn facings it
        /// shows depends on where the CAMERA is, so it is not a property of the walker at all and must
        /// be worked out again every time the view moves. Storing the index instead froze every guest's
        /// sprite at whatever the camera happened to be doing on its last step.</summary>
        public int DirX, DirZ;

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
        public override int WalkSpeed => V.WalkSpeed;
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
    sealed class ParkGuests
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

        /// <summary>Every path request in the park goes through here, so the tile asked for is always
        /// recorded against the walker and a failure can say which KIND of failure it was.</summary>
        bool Ask(Walker w, int tx, int tz, PathFlags flags)
        {
            if (!_finder.Request(w, w.X, w.Z, Centre(tx), Centre(tz), flags, 0)) return false;
            w.TargetTileX = tx; w.TargetTileZ = tz;
            return true;
        }

        /// <summary>Build the queue world. Separate from the brain because the rides need it too.</summary>
        public void SetRideWorld(Func<IReadOnlyList<GuestTarget>> targets)
        {
            _rides = new ParkRideWorld(_map, () => _now,
                (g, tx, tz) => _finder.Request(g, g.X, g.Z, Centre(tx), Centre(tz), WalkFlags, 0),
                SetSingleWaypoint, FreeChain);
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
        public void Populate(int target)
        {
            if (_guests.Count < target) Spawn();
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
                RemoveGuest);
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
            var (sx, sz) = _map.SpawnTiles[_rng.Next(_map.SpawnTiles.Count)];
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

        ParkStaffWorld StaffWorld() => _staffWorld ??= new ParkStaffWorld(
            () => _now,
            () => _rideTargets?.Invoke() ?? (IReadOnlyList<GuestTarget>)System.Array.Empty<GuestTarget>(),
            (st, tx, tz) =>
            {
                if (!_finder.Request(st, st.X, st.Z, Centre(tx), Centre(tz), WalkFlags, 0)) return false;
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
            foreach (var st in _staff)
            {
                // "Nearest" is measured from where THIS member stands, so the world is pointed at it
                // before every call, exactly as GuestBrain is set per guest.
                world.Current = st;
                var wasState = st.S.State;
                var mech = st.S.Kind == StaffKind.Mechanic ? MechanicWorld() : null;
                if (mech != null) mech.Current = st;

                if (st.Answer is { } m)
                {
                    st.Answer = null;
                    bool found = m == PathMessage.Found;
                    if (mech != null) Mechanic.OnPathMessage(st.S, mech, found);
                    else StaffBase.OnPathMessage(st.S, found);
                }
                if (st.WaypointHead != WaypointPool.NoChain)
                {
                    // ⭐ THE MECHANIC'S WALK COSTS ARE ITS OWN, and they run BEFORE the base's. Walking
                    // to a breakdown RESTS a mechanic and demoralises it, which is backwards from every
                    // other class and is what the code says (Mechanic's class note).
                    if (mech != null) Mechanic.Arrive(st.S, mech, true);
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
                if (st.S.State == StaffState.Idle) StaffBase.IdleCheck(st.S, world);
                if (mech != null && RunMechanic(st, mech))
                {
                    if (LogStaff && st.S.State != wasState)
                        Godot.GD.Print($"[tpw] staff {st.S.Kind}: {wasState} -> {st.S.State}, purpose {st.S.Purpose}, "
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
                    Godot.GD.Print($"[tpw] staff {st.S.Kind}: {wasState} -> {st.S.State}, purpose {st.S.Purpose}, "
                                 + $"jobs {(_rideJobs?.Invoke().Count ?? -1)}, tired {st.S.Tiredness}");
            }
        }

        /// <summary>Print each staff member's state changes (--park-log-rides).</summary>
        public bool LogStaff { get; set; }

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
                if (!_finder.Request(st, st.X, st.Z, Centre(tx), Centre(tz), WalkFlags, 0)) return false;
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
            if (_finder.Request(st, st.X, st.Z, Centre(tx), Centre(tz), WalkFlags, 0))
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
                Turnstile.Admit(_entrance);
                Turnstile.LaneTick(_entrance, 0);
                Turnstile.LaneTick(_entrance, 1);
            }
            _ticking = true;
            foreach (var g in _guests)
            {
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
                    if (SameArea(g)) RouteFailedSameArea++;
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
        }

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
            if (g.V.Purpose == Purpose.QueueWalk || g.V.Purpose == Purpose.QueueShuffle)
            {
                g.V.SetState(VisitorState.WaitingInQueue);
                return;
            }

            if (!VisitorQueue.HasQueue(_rides.TargetType(g.V))) { g.V.HasTarget = false; return; }
            g.V.SetState(VisitorState.JoiningQueue);
        }

        /// <summary>Drive the guest's queue-and-ride states. True when it handled the tick.</summary>
        bool RunQueueState(Guest g)
        {
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
                (w, x, y, flags, _) => _finder.Request(w, w.X, w.Z, x, y, (PathFlags)flags, 0) && (w.Waiting = true));
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
            g.Facing = GuestSprites.FacingFor(g.DirX, -g.DirZ, CameraForward);
            _sprites.Draw(g.Inst, g.Block, g.Facing, g.Frame, feet, CameraForward);
        }

        /// <summary>The people sheet to draw guests from, and where the camera is looking, which is what
        /// decides which of the eight drawn facings each guest shows (GuestSprites).</summary>
        public void SetSprites(GuestSprites sprites) => _sprites = sprites;
        public Vector3 CameraForward { get; set; } = new Vector3(0, 0, -1);

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
        GuestSprites _sprites;

        /// <summary>A step of the walk: the facing the camera sees and how far through the stride, from
        /// how far the guest has actually moved. ⚠ The port's own timing — the game advances its frames
        /// on the animation clock, which this has not traced.</summary>
        void Stride(Walker g, int fromX, int fromZ)
        {
            int dx = g.X - fromX, dz = g.Z - fromZ;
            if (dx == 0 && dz == 0) return;
            g.DirX = dx; g.DirZ = dz;
            g.Walked += Math.Abs(dx) + Math.Abs(dz);
            g.Frame = g.Walked / 48 % TPW.Data.PeopleSheet.WalkFrames;
        }

        /// <summary>Put a guest off a ride at its exit tile, or at its door when it has no exit.
        ///
        /// ⚠ THE EXIT IS NOT THE ENTRANCE. A ride with a separate exit puts guests out the other side,
        /// which is what stops the queue and the people leaving from walking through each other. A
        /// shop or a feature has no exit tile at all and the door is right.</summary>
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
            _walkable.Clear();
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
                    if (_map[x, z].IsWalkable) hasWalkable.Add(n);
                }
            // Only pieces somebody can actually stand in. Every lone grass tile is its own piece and
            // counting those makes the number a map statistic instead of a park one.
            Areas = hasWalkable.Count;
        }
        int[,] _area;

        /// <summary>How many connected pieces the walkable map is in. One is a healthy park.</summary>
        public int Areas { get; private set; }

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

        /// <summary>The three connectivity questions an attraction has, separately, because they have
        /// different answers: guests reach it through the DOOR, leave it through the EXIT, and queue on
        /// tiles that have to meet a path. Any of them can be false while the others are true.</summary>
        public (bool Door, bool Exit) AttractionJoined(GuestTarget t)
            => (JoinedToGate(t.DoorX, t.DoorZ), t.ExitX < 0 || JoinedToGate(t.ExitX, t.ExitZ));

        /// <summary>The tile the reachability check measures from: where a guest stands after walking in.</summary>
        public (int X, int Z) GateTile { get; private set; } = (-1, -1);

        int GateArea()
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
