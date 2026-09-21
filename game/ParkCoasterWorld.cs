using System;
using System.Linq;
using System.Text;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>The placed coaster's host: real map elevation, model dimensions and accepted builder
    /// points. Engine-free so the very same adapter is exercised by the sim tests.
    /// READ: ordinary geometry 0x800B340C/3660; station definitions 0x800AFB60.
    /// GUESS-high: the existing port's level deck (highest crossed terrain + one tile) supplies
    /// the height the 2D builder cannot edit. An original editor-height trace would replace this.
    /// No special-shape/stacked pylons are manufactured by this builder.</summary>
    public sealed class ParkCoasterWorld : ICoasterTrackWorld, ICoasterSimulationWorld
    {
        readonly ParkMap map;
        readonly AttractionDefinition ride;
        readonly int ox, oz, rot;
        readonly int supportHeight;
        TrackRun source;
        (int X, int Z)[] accepted = Array.Empty<(int, int)>();
        bool connected;
        public CoasterSimulation Simulation { get; private set; }
        public int DeckHeight { get; private set; }
        public int CarSub { get; }
        public int BoardingBatchSize { get; }
        public int LaunchSpeed => ride.Coaster.LaunchSpeed;
        public Func<uint> Clock;
        public Func<AttractionStatus> RideStatus;
        public Func<int> LiveCapacity, LiveSpeed, LiveDuration;
        public Func<Visitor> Head;
        public Func<bool> Animate;
        public Action<Visitor> Board, Exit;
        public uint NowTick => Clock();
        public AttractionStatus Status => RideStatus();
        public int Capacity => LiveCapacity();
        public int SpeedSlider => LiveSpeed();
        public int Duration => LiveDuration();
        public int MovementDelta { get; private set; }
        public bool HalfSpeed => false; // Host has no separate half-speed switch; same as other rides.
        public Visitor QueueHead => Head();
        public bool AdvanceStationAnimation() => Animate();
        public void BoardGuest(Visitor guest) => Board(guest);
        public void ExitGuest(Visitor guest) => Exit(guest);

        public ParkCoasterWorld(ParkMap map, AttractionDefinition ride, int ox, int oz, int rot,
            Func<int, TPW.Data.Mesh> model)
        {
            this.map = map; this.ride = ride; this.ox = ox; this.oz = oz; this.rot = rot;
            if (ride.Coaster == null) throw new ArgumentException("Missing coaster definition.", nameof(ride));
            var models = new System.Collections.Generic.List<TPW.Data.Mesh>();
            for (int i = 0; model(i) is { } m; i++) models.Add(m);
            var bounds = models.Select(Bounds).ToArray();
            var pieces = TrackRun.PickPieces(bounds);
            if (!pieces.Any) throw new ArgumentException("Missing coaster track models.", nameof(model));
            // GUESS-high: reuse the existing renderer's measured straight selector. The actual
            // selector-6 model-height cache is maxY-minY (0x800B2F2C..30AC), not a universal height.
            supportHeight = bounds[pieces.Straight].H;
            // GUESS-medium: first passenger-bearing submodel, otherwise the last submodel. This
            // agrees with the 12 disc car families, but world/B/kind selection is not wired here.
            // Resolve that catalogue selection before claiming exact car variants/seat matrices.
            CarSub = models.FindIndex(m => m.SeatCount > 0);
            if (CarSub < 0) CarSub = models.Count - 1;
            // READ: 0x800AD610, selected car's attachment count, zero replaced by one.
            BoardingBatchSize = Math.Max(1, models[CarSub].SeatCount);
            Synchronize(null);
        }

        static (int W, int H, int D, int Verts) Bounds(TPW.Data.Mesh m)
        {
            if (m.VertexCount == 0) return default;
            int minX = int.MaxValue, minY = minX, minZ = minX;
            int maxX = int.MinValue, maxY = maxX, maxZ = maxX;
            for (int i = 0; i < m.VertexCount; i++)
            {
                minX = Math.Min(minX, m.Vertices[i * 3]); maxX = Math.Max(maxX, m.Vertices[i * 3]);
                minY = Math.Min(minY, m.Vertices[i * 3 + 1]); maxY = Math.Max(maxY, m.Vertices[i * 3 + 1]);
                minZ = Math.Min(minZ, m.Vertices[i * 3 + 2]); maxZ = Math.Max(maxZ, m.Vertices[i * 3 + 2]);
            }
            return (maxX - minX, maxY - minY, maxZ - minZ, m.VertexCount);
        }

        public short BaseHeight(int x, int z) => (short)ParkCamera.GroundHeight(map, (x << 8) + 0x80, (z << 8) + 0x80);
        public CoasterPieceGeometry ResolvePieceGeometry(CoasterPiece piece)
        {
            if (piece.Kind != 0 || piece.SpecialPhase != 0)
                throw new NotSupportedException("The park builder only supplies ordinary unstacked points.");
            // READ: selector 6 added to support top, then subtracted and +24 for the spline,
            // 0x800B3424..3438 / 0x800B36B8..36DC. Opaque saved padding is NEVER elevation.
            return new(BaseHeight(piece.TileX, piece.TileY), supportHeight, new(0, 24 - supportHeight, 0));
        }

        public bool Synchronize(TrackRun run)
        {
            if (Simulation != null && ReferenceEquals(source, run) && connected == (run?.Circuit ?? false)
                && accepted.SequenceEqual(run?.Pylons ?? Array.Empty<(int, int)>())) return false;
            Simulation?.EjectPassengers();
            source = run; accepted = run?.Pylons.ToArray() ?? Array.Empty<(int, int)>();
            connected = run?.Circuit ?? false;
            var launch = ride.Coaster.Connection(ride, ox, oz, rot, true);
            var approach = ride.Coaster.Connection(ride, ox, oz, rot, false);
            // GUESS-high: preserve the renderer's one-tile deck assumption; sample the actual map.
            DeckHeight = Math.Max(BaseHeight(launch.X, launch.Z), BaseHeight(approach.X, approach.Z));
            var last = launch;
            foreach (var p in accepted)
            {
                foreach (var t in TrackRun.Span(last, p)) DeckHeight = Math.Max(DeckHeight, BaseHeight(t.X, t.Z));
                DeckHeight = Math.Max(DeckHeight, BaseHeight(p.X, p.Z)); last = p;
            }
            DeckHeight += ParkTerrain.TileUnits;
            var lp = Piece(launch, ride.Coaster.LaunchHeight);
            var ap = Piece(approach, ride.Coaster.ApproachHeight);
            var track = new CoasterTrack(new(lp, ResolvePieceGeometry(lp)), new(ap, ResolvePieceGeometry(ap)));
            for (int i = 0; i < accepted.Length; i++)
            {
                var p = accepted[i];
                var piece = Piece(p, (short)(DeckHeight - BaseHeight(p.X, p.Z)));
                var result = track.Append(piece, this);
                bool closing = connected && i == accepted.Length - 1;
                if (result != (closing ? CoasterTrack.Addition.Closed : CoasterTrack.Addition.Placed))
                    throw new InvalidOperationException($"Builder/runtime disagree at accepted point {i}: {result}");
            }
            Simulation = new CoasterSimulation(track, this);
            return true;
        }

        // READ: accepted append has check=1 (0x800AF880); ordinary bank/kind/phase=0
        // (0x800AFB60 station setup). The current 2D tool has no bank/special-shape fields.
        static CoasterPiece Piece((int X, int Z) p, short height) => new((short)p.X, 0, (short)p.Z, 1, height, 0, 0, 0);

        public void Tick(int frameTime)
        {
            MovementDelta = frameTime; // Same 20.12 delta supplied to the other ride classes.
            Simulation.Update();
        }

        public bool OpenToGuests => RollerCoaster.OpenToGuests(Status, Simulation.Track.Connected);

        public string Report()
        {
            var s = Simulation;
            string track = accepted.Length == 0 ? "no-track" : s.Track.Connected ? "connected" : "open-track";
            var b = new StringBuilder($"\n    coaster {ride.Entry}: {track}, accepted {accepted.Length}, pieces {s.Track.Pieces.Count}/{CoasterTrack.MaximumPieces}, "
                + $"checks {s.Track.ChecksPass}, trains {s.Trains.Count}/{CoasterSimulation.TrainCount}, free {s.FreeTrains}, pending {s.PendingPassengers}, "
                + $"updates {s.Updates}, dispatches {s.Dispatches}, position-changes {s.PositionChanges}, completed {s.CompletedTrips}, laps {s.CompletedLaps}");
            foreach (var t in s.Trains)
            {
                string segment = ReferenceEquals(t.Segment, s.Track.Launch) ? "launch" : ReferenceEquals(t.Segment, s.Track.Approach) ? "approach"
                    : $"piece({t.Segment.Piece.TileX},{t.Segment.Piece.TileY})";
                string state = t.Control.ReadyToUnload ? "ready" : t.Control.StillOnLaunchSegment ? "launching" : "running";
                var p = t.Pose.Position;
                b.Append($"\n      train {t.PoolId}: {state}, segment {segment}, position ({p.X},{p.Y},{p.Z}), distance {t.Distance}, speed {t.Speed}, laps {t.Control.Laps}, position-changes {t.PositionChanges}");
            }
            return b.ToString();
        }
    }
}
