using System;
using System.Linq;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>Track builder → route position → PathedRide controller → real guest transactions.
    /// GUESS-medium geometry: axis-aligned centerline at the renderer's level deck. The PSX
    /// piece curves, acceleration, spacing, +0xD4 vehicle variants and finishing offsets remain
    /// unported. A trace of 0x800A519C/0x800AA704 would settle that adapter; no trip timer is used.</summary>
    public sealed class ParkTrackRideWorld : ParkMovingRideWorld, IPathedRideWorld
    {
        readonly ParkMap map;
        readonly (int X, int Z) start;
        TrackRun source;
        (int X, int Z)[] accepted = Array.Empty<(int, int)>();
        CoasterVector[] points = Array.Empty<CoasterVector>();
        int[] lengths = Array.Empty<int>();
        bool connected;
        public readonly PathedRide Controller = new();
        public int RouteLength { get; private set; }
        public int DeckHeight { get; private set; }
        public int VehicleCount => cars.Count;
        public bool TrackConnected => connected;
        public bool PreviewActive => false; // No preview controller exists in the park host.
        public override bool RouteAvailable => connected && RouteLength > 0;
        protected override string RouteDescription => accepted.Length == 0 ? "no-track" : connected ? "connected" : "open-track";
        protected override string Detail => $", accepted {accepted.Length}, route-length {RouteLength}, run-ticks {Controller.RunTicks}";
        public ParkTrackRideWorld(ParkMap map, AttractionDefinition ride, int ox, int oz, int rot) : base(ride)
        {
            if (ride.TrackPassengersPerVehicle <= 0) throw new ArgumentException("Missing track vehicle seat definition.");
            this.map = map; start = TrackRun.StartFor(ox, oz, rot);
        }
        public bool Synchronize(TrackRun run)
        {
            if (ReferenceEquals(source, run) && connected == (run?.Circuit ?? false)
                && accepted.SequenceEqual(run?.Pylons ?? Array.Empty<(int, int)>())) return false;
            EjectPassengers(); Controller.RunTicks = 0;
            source = run; connected = run?.Circuit ?? false;
            accepted = run?.Pylons.ToArray() ?? Array.Empty<(int, int)>();
            var tiles = new[] { start }.Concat(accepted).ToArray();
            // GUESS-high: same level deck as RebuildTrackPieces, including crossed high ground.
            DeckHeight = Ground(start);
            for (int i = 1; i < tiles.Length; i++)
            {
                DeckHeight = Math.Max(DeckHeight, Ground(tiles[i]));
                foreach (var p in TrackRun.Span(tiles[i - 1], tiles[i])) DeckHeight = Math.Max(DeckHeight, Ground(p));
            }
            DeckHeight += ParkTerrain.TileUnits;
            points = tiles.Select(p => new CoasterVector(p.X * ParkTerrain.TileUnits + ParkTerrain.TileUnits / 2,
                DeckHeight, p.Z * ParkTerrain.TileUnits + ParkTerrain.TileUnits / 2)).ToArray();
            // READ 0x800A9360: 256 route units per generated piece. Builder's ordinary piece
            // spans two tiles (0x800221B0). GUESS-medium: linear interpolation within that piece.
            lengths = Enumerable.Range(1, tiles.Length - 1).Select(i => TrackRun.PieceCount(tiles[i-1], tiles[i]) * 256).ToArray();
            RouteLength = lengths.Sum();
            if (Status is AttractionStatus.Running or AttractionStatus.Unloading) SetStatus(AttractionStatus.Loading);
            return true;
        }
        int Ground((int X, int Z) p) => ParkCamera.GroundHeight(map,
            p.X * ParkTerrain.TileUnits + ParkTerrain.TileUnits / 2, p.Z * ParkTerrain.TileUnits + ParkTerrain.TileUnits / 2);
        public void BoardHeadGuest()
        {
            if (cars.Count == 0 || cars[^1].Passengers.Count >= ride.TrackPassengersPerVehicle)
            {
                var car = AddCar(points[0]);
                car.Speed = RideSliderEffects.TrackVehicleSpeed(LiveSpeed()); // READ: sampled at vehicle initialization.
            }
            BoardInto(cars[^1]);
        }
        public bool VehicleReadyToUnload(int index) => cars[index].Track.ReadyToUnload;
        public void UnloadVehicle(int index)
        {
            var c = cars[index];
            while (c.Passengers.Count != 0) ExitLast(c);
            cars.RemoveAt(index); // ⚠ DO NOT FIX caller's compacted-array skip.
        }
        public override void Tick(int frameTime)
        {
            Updates++;
            if (!RouteAvailable) return;
            // READ: motion is independent of the short ride-tick gate, including status 11.
            if (Status != AttractionStatus.Loading)
                foreach (var c in cars) Advance(c);
            var before = Status;
            var next = Status switch
            {
                AttractionStatus.Loading => Controller.LoadTick(this),
                AttractionStatus.Running => Controller.RunTick(this),
                AttractionStatus.Unloading or AttractionStatus.AboutToBreakDown or AttractionStatus.BrokenDown or AttractionStatus.UnderRepair
                    => PathedRide.UnloadTick(Status, this),
                _ => Status,
            };
            if (before == AttractionStatus.Loading && next == AttractionStatus.Running) Dispatches += cars.Count;
            SetStatus(next);
        }
        void Advance(Car c)
        {
            if (c.Track.ReadyToUnload) return;
            // GUESS-medium: use initialized target byte as constant route increment per park tick.
            // 0x800AA974 adds current speed byte; full acceleration/random traffic behavior is pending.
            c.Progress += c.Speed;
            if (c.Progress >= RouteLength)
            {
                c.Progress %= RouteLength;
                c.Track.CompleteLap(Duration);
                Arrivals++;
                if (c.Track.ReadyToUnload) Completed++;
            }
            int progress = c.Progress;
            for (int i = 0; i < lengths.Length; i++)
            {
                if (progress >= lengths[i]) { progress -= lengths[i]; continue; }
                var a = points[i]; var b = points[i + 1];
                Move(c, new(a.X + (b.X - a.X) * progress / lengths[i], DeckHeight,
                    a.Z + (b.Z - a.Z) * progress / lengths[i]));
                break;
            }
        }
    }
}
