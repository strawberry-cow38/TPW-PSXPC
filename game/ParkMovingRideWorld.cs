using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>Host ownership and instrumentation shared by the two distinct controllers.
    /// Vehicle identities are never reused, so a replacement cannot masquerade as movement.</summary>
    public abstract class ParkMovingRideWorld
    {
        protected readonly AttractionDefinition ride;
        protected readonly List<Car> cars = new();
        int nextId;
        public IReadOnlyList<Car> Cars => cars;
        public Func<uint> Clock;
        public Func<AttractionStatus> RideStatus;
        public Action<AttractionStatus> ChangeStatus;
        public Func<int> LiveCapacity, LiveSpeed, LiveDuration;
        public Func<Visitor> Head;
        public Action<Visitor> Board, Exit;
        public uint NowTick => Clock();
        public AttractionStatus Status => RideStatus();
        public int Capacity => LiveCapacity();
        public int Duration => LiveDuration();
        public Visitor QueueHead => Head();
        public int Riders => cars.Sum(c => c.Passengers.Count);
        public long Updates { get; protected set; }
        public long Dispatches { get; protected set; }
        public long PositionChanges { get; protected set; }
        public long Completed { get; protected set; }
        public long Arrivals { get; protected set; }
        public long Unloaded { get; private set; }
        public long Transitions { get; protected set; }
        public abstract bool RouteAvailable { get; }
        public bool OpenToGuests => RouteAvailable && AttractionLifecycle.OpenToGuests(Status);
        protected ParkMovingRideWorld(AttractionDefinition ride) { this.ride = ride; }
        public abstract void Tick(int frameTime);
        protected abstract string RouteDescription { get; }
        protected virtual string Detail => "";

        public sealed class Car
        {
            public int Id { get; internal set; }
            public readonly List<Visitor> Passengers = new();
            public CoasterVector Position { get; internal set; }
            public CoasterVector Tangent { get; internal set; }
            public readonly PathedRideVehicle Track = new();
            public readonly TourTransport Tour = new();
            public long PositionChanges { get; internal set; }
            public int Progress, Speed;
            public bool Retirement;
            public CoasterVector Destination;
        }
        protected Car AddCar(CoasterVector position)
        {
            var c = new Car { Id = nextId++, Position = position };
            cars.Add(c); return c;
        }
        protected void Move(Car c, CoasterVector p)
        {
            if (p == c.Position) return;
            c.Tangent = new(p.X - c.Position.X, p.Y - c.Position.Y, p.Z - c.Position.Z);
            c.Position = p; c.PositionChanges++; PositionChanges++;
        }
        protected void BoardInto(Car c)
        {
            var guest = QueueHead;
            Board(guest); c.Passengers.Add(guest);
        }
        protected void ExitLast(Car c)
        {
            var guest = c.Passengers[^1];
            Exit(guest); c.Passengers.RemoveAt(c.Passengers.Count - 1); Unloaded++;
        }
        /// <summary>Editing/removing a route returns real guests, or clears just vehicle ownership
        /// before the shared lifecycle message-10 ejection handles the park lists.</summary>
        public virtual void EjectPassengers(bool transfer = true)
        {
            foreach (var c in cars)
                if (transfer) while (c.Passengers.Count != 0) ExitLast(c);
            cars.Clear();
        }
        protected void SetStatus(AttractionStatus next)
        {
            if (next == Status) return;
            Transitions++; ChangeStatus(next);
        }
        public string Report()
        {
            bool track = ride.Type == 6;
            int ready = cars.Count(c => track ? c.Track.ReadyToUnload : c.Tour.State == TourTransportState.Unloading);
            var b = new StringBuilder($"\n    {(track ? "track-ride" : "tour-ride")} {ride.Entry}: {RouteDescription}, "
                + $"vehicles {cars.Count}, riders {Riders}, updates {Updates}, dispatches {Dispatches}, position-changes {PositionChanges}, "
                + $"completed {Completed} (ready-now {ready}), arrivals {Arrivals}, unloaded {Unloaded}, transitions {Transitions}{Detail}");
            foreach (var c in cars)
            {
                var p = c.Position;
                string state = track ? c.Track.ReadyToUnload ? "ready" : Status == AttractionStatus.Loading ? "loading" : "running" : c.Tour.State.ToString();
                b.Append($"\n      vehicle {c.Id}: {state}, passengers {c.Passengers.Count}, position ({p.X},{p.Y},{p.Z}), laps {(track ? c.Track.Laps : c.Tour.Laps)}, position-changes {c.PositionChanges}");
            }
            return b.ToString();
        }
    }
}
