using System;
using System.Linq;
using Godot;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot
{
    public partial class ParkView
    {
        void CreateMovingRide(PlacedAttraction a)
        {
            a.MovingRide = a.Rec.Type == 6
                ? new ParkTrackRideWorld(_map, a.Rec, a.Ox, a.Oz, a.Rot)
                : new ParkTourRideWorld(_map, a.Rec, a.Ox, a.Oz, a.Rot, n => _guests.Dice.Next(n));
            var host = a.MovingRide;
            host.Clock = () => (uint)_clockTicks;
            host.RideStatus = () => a.Status;
            host.ChangeStatus = status => a.Status = AttractionLifecycle.Enter(status, a);
            host.LiveCapacity = () => a.Capacity;
            host.LiveSpeed = () => a.SpeedSlider;
            host.LiveDuration = () => a.CyclesPerLoad;
            host.Head = () => _guests?.Rides?.RuntimeFor(a.Rec.Entry)?.Queue.FirstOrDefault()?.V;
            host.Board = guest =>
            {
                var load = CoasterLoading(a); // Same real queue/list/visibility transaction.
                if (!ReferenceEquals(load.QueueHead, guest)) throw new InvalidOperationException("Moving ride queue head changed.");
                load.BoardQueueHead(); load.ShuffleTheRestForward();
            };
            host.Exit = guest => CoasterLoading(a).UnloadRider(guest);
            var eject = a.Eject;
            a.Eject = () => { host.EjectPassengers(transfer: false); eject(); };
            // READ: type-6 car class 4 from record+DC (0x800A6088). GUESS-medium: use
            // its first variant for every vehicle; PSX colour/variant selection remains pending.
            // GUESS-high: the tour's sole non-station model is sub 1 in all four disc entries.
            // Trace the transport render selector to replace that display policy.
            a.MovingCarSub = a.Rec.Type == 6 ? a.Rec.TrackCarSub : 1;
        }
        void TickMovingRide(PlacedAttraction a, int frameTime)
        {
            if (a.MovingRide is ParkTrackRideWorld track) track.Synchronize(a.Track);
            a.MovingRide.Tick(frameTime);
            DrawMovingRide(a);
            // Instrumentation only: reuse loading cadence, not a new trip duration.
            if (_logRides && _clockTicks % PathedRide.LoadCadence == 0)
                GD.Print($"[tpw] moving-ride-report tick {_clockTicks}, frame {Engine.GetFramesDrawn()}" + MovingRideReport(a));
        }
        static string MovingRideReport(PlacedAttraction a)
        {
            var text = a.MovingRide.Report();
            foreach (var pair in a.MovingCars.Where(p => p.Value.Visible))
            {
                var p = pair.Value.GlobalPosition * ParkTerrain.TileUnits;
                text += $"\n      drawn-vehicle {pair.Key}: position ({p.X:0.0},{p.Y:0.0},{-p.Z:0.0}), model {a.MovingCarSub}, surfaces {pair.Value.Mesh?.GetSurfaceCount() ?? 0}";
            }
            return text;
        }
        void DrawMovingRide(PlacedAttraction a)
        {
            // Track vehicles are destroyed and tour transports retire; release their scene nodes.
            foreach (int id in a.MovingCars.Keys.Where(id => !a.MovingRide.Cars.Any(c => c.Id == id)).ToArray())
            {
                a.MovingCars[id].QueueFree(); a.MovingCars.Remove(id);
            }
            foreach (var c in a.MovingRide.Cars)
            {
                if (!a.MovingCars.TryGetValue(c.Id, out var car))
                {
                    var model = _attractionSub(a.Rec.Entry, a.MovingCarSub);
                    var mesh = ModelMesh.Build(model, null, _modelSheets, true, false, false,
                        CarOrigin(model), 1f / ParkTerrain.TileUnits, out _);
                    car = new MeshInstance3D { Mesh = mesh };
                    a.Inst.AddChild(car); a.MovingCars.Add(c.Id, car);
                }
                var p = c.Position;
                car.GlobalPosition = new Vector3(p.X, p.Y, -p.Z) / ParkTerrain.TileUnits;
                var t = c.Tangent;
                var forward = new Vector3(t.X, t.Y, -t.Z);
                if (forward.LengthSquared() > 0 && forward.Cross(Vector3.Up).LengthSquared() > 0)
                    car.GlobalBasis = Basis.LookingAt(forward.Normalized());
            }
        }
    }
}
