using System;
using System.Linq;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>Tour transport host. Tours select airborne destinations, not TrackRun rails.
    /// READ: random map destinations 0x800A3D80, return/approach/depart offsets 0x800A3E90,
    /// 0x800A3FA4/0x800A426C; state speed fractions 0x800A3368. GUESS-medium: straight-line
    /// following instead of the unported steering/avoidance integrator (0x800A382C).</summary>
    public sealed class ParkTourRideWorld : ParkMovingRideWorld, ITourRideWorld
    {
        readonly ParkMap map;
        readonly int rot;
        readonly Func<int, int> random;
        readonly CoasterVector dock;
        Car docked;
        public Car Docked => docked;
        // READ 0x800A22A0: allocator refuses the fourth transport.
        public const int MaximumTransports = 3;
        public int TransportCount => cars.Count;
        public int DockedPassengers => docked?.Passengers.Count ?? 0;
        public override bool RouteAvailable => map.Width > 0 && map.Height > 0;
        protected override string RouteDescription => RouteAvailable ? "air-route" : "no-route";
        protected override string Detail => $", docked {(docked == null ? "none" : docked.Id.ToString())}, track not-required";
        public ParkTourRideWorld(ParkMap map, AttractionDefinition ride, int ox, int oz, int rot,
            Func<int, int> random) : base(ride)
        {
            this.map = map; this.rot = rot & 3; this.random = random;
            var (w, d) = ride.Footprint(rot);
            // GUESS-medium: station center at ground supplies the dock. 0x800A1504 actually
            // transforms a model attachment; tracing that matrix would replace this policy.
            int x = ox * ParkTerrain.TileUnits + w * ParkTerrain.TileUnits / 2;
            int z = oz * ParkTerrain.TileUnits + d * ParkTerrain.TileUnits / 2;
            dock = new(x, ParkCamera.GroundHeight(map, x, z), z);
        }
        public override void EjectPassengers(bool transfer = true)
        {
            base.EjectPassengers(transfer); docked = null;
        }
        public void BoardHeadGuest() => BoardInto(docked);
        public void UnloadLastGuest()
        {
            if (docked?.Passengers.Count > 0) ExitLast(docked);
        }
        public void RequestRetirement() => docked.Retirement = true;
        public void Depart()
        {
            var c = docked;
            c.Tour.State = TourTransportState.Departing;
            c.Tour.Laps = 0;
            Enter(c, TourTransportState.Departing);
        }
        public override void Tick(int frameTime)
        {
            Updates++;
            if (!RouteAvailable || Status is AttractionStatus.JustPlaced or AttractionStatus.UnderConstruction) return;
            foreach (var c in cars.ToArray()) c.Tour.Tick(new TransportWorld(this, c));
            // READ Update allocates only with no dock and a queue head (0x800A0F84).
            if (docked == null && QueueHead != null && cars.Count < MaximumTransports
                && AttractionLifecycle.OpenToGuests(Status)) docked = AddCar(dock);
            SetStatus(TourRide.StatusAfterMovement(Status, docked?.Tour.State));
            if (docked?.Tour.State == TourTransportState.Loading && Status == AttractionStatus.Loading) TourRide.LoadTick(this);
            if (docked?.Tour.State == TourTransportState.Unloading && Status == AttractionStatus.Unloading) TourRide.UnloadTick(this);
        }
        CoasterVector Offset(int forward, int up) => rot switch
        {
            0 => new(dock.X + forward, dock.Y + up, dock.Z),
            1 => new(dock.X, dock.Y + up, dock.Z - forward),
            2 => new(dock.X - forward, dock.Y + up, dock.Z),
            _ => new(dock.X, dock.Y + up, dock.Z + forward),
        };
        void Enter(Car c, TourTransportState state)
        {
            Transitions++;
            int speed = ride.TourBaseSpeed;
            switch (state)
            {
                case TourTransportState.Departing:
                    if (ReferenceEquals(docked, c)) docked = null;
                    Dispatches++; c.Destination = Offset(-900, 0); speed = speed * 2 / 3; break;
                case TourTransportState.Touring:
                    // READ: random(width)<<8, baseY+0x600+rand(0x500), random(height)<<8.
                    int x = random(map.Width) << 8;
                    int y = dock.Y + 0x600 + random(0x500);
                    int z = random(map.Height) << 8;
                    // GUESS-medium: terrain-only clearance. 0x800A4374 also sees object tops.
                    y = Math.Max(y, ParkCamera.GroundHeight(map, x, z) + 0x200);
                    c.Destination = new(x, y, z); break;
                case TourTransportState.Returning:
                    docked = c; c.Destination = Offset(2000, 1500); speed = speed * 2 / 3; break;
                case TourTransportState.Approaching:
                    c.Destination = Offset(800, 600); speed = speed * 2 / 5; break;
                case TourTransportState.Docking:
                    c.Destination = dock; speed /= 5; break;
                case TourTransportState.Unloading:
                    Completed++; break;
                case TourTransportState.Retiring:
                    // READ 0x800A4118: pick one of four map edges, five tiles beyond it.
                    int edge = random(4);
                    c.Destination = edge switch
                    {
                        0 => new(random(map.Width) << 8, dock.Y, -5 << 8),
                        1 => new(random(map.Width) << 8, dock.Y, (map.Height + 5) << 8),
                        2 => new(-5 << 8, dock.Y, random(map.Height) << 8),
                        _ => new((map.Width + 5) << 8, dock.Y, random(map.Height) << 8),
                    };
                    speed = speed * 4 / 3; break;
            }
            c.Speed = RideSliderEffects.TourVehicleSpeed(speed, LiveSpeed());
        }
        bool Advance(Car c)
        {
            var p = c.Position; var d = c.Destination;
            long dx = d.X - p.X, dy = d.Y - p.Y, dz = d.Z - p.Z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length <= c.Speed) Move(c, d);
            else Move(c, new(p.X + (int)Math.Round(dx * c.Speed / length),
                p.Y + (int)Math.Round(dy * c.Speed / length), p.Z + (int)Math.Round(dz * c.Speed / length)));
            bool arrived = c.Position == d;
            if (arrived && c.Tour.State == TourTransportState.Touring) Arrivals++;
            return arrived;
        }
        bool Settle(Car c)
        {
            // READ 0x800A3C70: truncating fifth; snap each axis when it no longer changes.
            static int Axis(int current, int target)
            {
                int next = current - (current - target) / 5;
                return next == current ? target : next;
            }
            var p = c.Position;
            Move(c, new(Axis(p.X, dock.X), Axis(p.Y, dock.Y), Axis(p.Z, dock.Z)));
            return c.Position == dock;
        }
        sealed class TransportWorld : ITourTransportWorld
        {
            readonly ParkTourRideWorld host;
            readonly Car car;
            public TransportWorld(ParkTourRideWorld host, Car car) { this.host = host; this.car = car; }
            public int Passengers => car.Passengers.Count;
            public int Duration => host.Duration;
            public bool RetirementRequested => car.Retirement;
            public bool DockOccupied => host.docked != null;
            public AttractionStatus RideStatus => host.Status;
            // READ loading proximity gate 0x800A36DC..3754. Nonloading avoidance is still pending.
            public bool DepartureBlocked => host.cars.Any(c => c != car && c.Tour.State != TourTransportState.Loading
                && Math.Abs(c.Position.X - car.Position.X) < 2000 && Math.Abs(c.Position.Y - car.Position.Y) < 1500
                && Math.Abs(c.Position.Z - car.Position.Z) < 2000);
            public bool MoveToDestination() => host.Advance(car);
            public bool SettleAtDock() => host.Settle(car);
            public void EnterState(TourTransportState state) => host.Enter(car, state);
            public void RemoveTransport()
            {
                // Retained partial-load retirement can carry passengers. Never orphan the park
                // list when removing it; normal completion still unloads one per 20 ticks at dock.
                while (car.Passengers.Count != 0) host.ExitLast(car);
                host.cars.Remove(car);
                if (ReferenceEquals(host.docked, car)) host.docked = null;
            }
        }
    }
}
