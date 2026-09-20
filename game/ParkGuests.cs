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
            => _brain = new GuestBrain(_map, targets,
                   (g, tx, tz) => _finder.Request(g, g.X, g.Z, Centre(tx), Centre(tz), WalkFlags, 0),
                   () => _now);

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

        /// <summary>How many guests currently have something they are heading for.</summary>
        public int WithTarget { get { int n = 0; foreach (var g in _guests) if (g.V.HasTarget) n++; return n; } }

        public int Count => _guests.Count;
        public int FreeNodes => _finder.FreeNodes;
        public int FreeWaypoints => _waypoints.FreeCount;
        public int Outstanding => _finder.ActiveRequests;
        public bool ParkIsOpen { get => _pathMap.ParkIsOpen; set => _pathMap.ParkIsOpen = value; }

        /// <summary>Put a guest on a walkable tile. Returns null when the park has nowhere to stand -
        /// a park with no paths laid, which is the state every park starts in.</summary>
        public Guest Spawn()
        {
            if (_walkable.Count == 0) return null;
            var (tx, tz) = _walkable[_rng.Next(_walkable.Count)];

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

        /// <summary>One sim tick of every guest.</summary>
        public void Tick()
        {
            _now++;
            // The rides a guest can be standing at change whenever something is built, and the scripted
            // harness does not always say so. Refreshing here means the two can never disagree.
            if (_rides != null && _rideTargets != null) _rides.SetRides(_rideTargets());
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
                if (g.Answer == PathMessage.Failed)
                {
                    g.Answer = null;
                    g.V.HasTarget = false;
                    g.V.Happiness = Stat.Sub(g.V.Happiness, _rng.Next(15));
                    g.V.Boredom = Stat.Add(g.V.Boredom, _rng.Next(2));
                    RouteFailed++;
                    Wander(g);
                    continue;
                }
                if (g.WaypointHead != WaypointPool.NoChain) { Walk(g); continue; }
                if (g.Waiting) continue;                       // the answer has not come back yet
                if (RunQueueState(g)) continue;
                AskForARoute(g);
            }
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

        /// <summary>No target, or the decision gave up: walk somewhere on the paths so the guest is not
        /// simply stood still. ⚠ A STAND-IN for TPW.Sim's real wander (§2.7), which is not wired.</summary>
        void Wander(Guest g)
        {
            if (_walkable.Count == 0) return;
            var (tx, tz) = _walkable[_rng.Next(_walkable.Count)];

            // ⚠ A REFUSAL IS NOT A FAILURE AND GETS NO MESSAGE. Ten searches may be outstanding at
            // once; the eleventh is simply declined, and so is any request made with the node pool
            // empty. The guest must retry rather than wait, or a busy park quietly freezes everyone
            // who happened to ask on a crowded frame - which is exactly what the original's own
            // callers do (state 23 retries next tick).
            if (!_finder.Request(g, g.X, g.Z, Centre(tx), Centre(tz), WalkFlags, 0)) return;

            g.Waiting = true;
            g.Answer = null;
        }

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
            _sprites.Draw(g.Inst, g.Block, g.Facing, g.Frame, feet, CameraForward);
        }

        /// <summary>The people sheet to draw guests from, and where the camera is looking, which is what
        /// decides which of the eight drawn facings each guest shows (GuestSprites).</summary>
        public void SetSprites(GuestSprites sprites) => _sprites = sprites;
        public Vector3 CameraForward { get; set; } = new Vector3(0, 0, -1);
        GuestSprites _sprites;

        /// <summary>A step of the walk: the facing the camera sees and how far through the stride, from
        /// how far the guest has actually moved. ⚠ The port's own timing — the game advances its frames
        /// on the animation clock, which this has not traced.</summary>
        void Stride(Walker g, int fromX, int fromZ)
        {
            int dx = g.X - fromX, dz = g.Z - fromZ;
            if (dx == 0 && dz == 0) return;
            g.Facing = GuestSprites.FacingFor(dx, -dz, CameraForward);
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
        }
    }
}
