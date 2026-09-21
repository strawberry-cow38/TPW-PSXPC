using System;
using System.Linq;
using Godot;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot
{
    public partial class ParkView
    {
        void CreateCoaster(PlacedAttraction a)
        {
            a.Coaster = new ParkCoasterWorld(_map, a.Rec, a.Ox, a.Oz, a.Rot, sub => _attractionSub(a.Rec.Entry, sub))
            {
                Clock = () => (uint)_clockTicks,
                RideStatus = () => a.Status,
                LiveCapacity = () => a.Capacity,
                LiveSpeed = () => a.SpeedSlider,
                LiveDuration = () => a.CyclesPerLoad,
                Head = () => _guests?.Rides?.RuntimeFor(a.Rec.Entry)?.Queue.FirstOrDefault()?.V,
                Animate = () => a.Cycle.Advance(a.Status, a, a.Coaster.MovementDelta, halfSpeed: false),
                Board = guest =>
                {
                    var load = CoasterLoading(a);
                    if (!ReferenceEquals(load.QueueHead, guest)) throw new InvalidOperationException("Coaster queue head changed.");
                    load.BoardQueueHead();
                    load.ShuffleTheRestForward();
                },
                Exit = guest => CoasterLoading(a).UnloadRider(guest),
            };
            var eject = a.Eject;
            a.Eject = () =>
            {
                // Keep both owners synchronized before lifecycle ejection clears the park lists.
                // The shared message-10 transaction handles hidden guests and queue purpose.
                var exit = a.Coaster.Exit;
                a.Coaster.Exit = _ => { }; // EjectAll below owns these guest transactions.
                try { a.Coaster.Simulation.EjectPassengers(); }
                finally { a.Coaster.Exit = exit; }
                eject();
            };
        }

        ParkRideWorld.LoadAdapter CoasterLoading(PlacedAttraction a)
        {
            var rw = _guests.Rides;
            return new ParkRideWorld.LoadAdapter(rw.RuntimeFor(a.Rec.Entry), a.Capacity,
                () => _clockTicks, () => ParkDay,
                g => _guests.PlaceAtExit(g, a.Rec, a.Ox, a.Oz, a.Rot), rw, _guests.Dice)
            { PlaceAtDoor = g => _guests.PlaceAtDoor(g, a.Rec, a.Ox, a.Oz, a.Rot) };
        }

        void TickCoaster(PlacedAttraction a, int frameTime)
        {
            a.Coaster.Synchronize(a.Track); // Accepted points only, including undo/rebuild.
            a.Coaster.Tick(frameTime);
            DrawCoaster(a);
            // Diagnostic cadence reuses the controller's load cadence; it is not a gameplay timer.
            if (_logRides && _clockTicks % RollerCoaster.LoadCadence == 0)
                GD.Print($"[tpw] coaster-report tick {_clockTicks}, frame {Engine.GetFramesDrawn()}" + CoasterReport(a));
        }

        static string CoasterReport(PlacedAttraction a)
        {
            var text = a.Coaster.Report();
            // Observe the actual scene nodes as well as the sim, so a severed drawing call is visible.
            foreach (var pair in a.CoasterCars.Where(p => p.Value.Visible))
            {
                var p = pair.Value.GlobalPosition * ParkTerrain.TileUnits;
                text += $"\n      drawn-train {pair.Key}: position ({p.X:0.0},{p.Y:0.0},{-p.Z:0.0})";
            }
            return text;
        }

        void DrawCoaster(PlacedAttraction a)
        {
            foreach (var car in a.CoasterCars.Values) car.Visible = false;
            foreach (var train in a.Coaster.Simulation.Trains)
            {
                if (!a.CoasterCars.TryGetValue(train.PoolId, out var car))
                {
                    var model = _attractionSub(a.Rec.Entry, a.Coaster.CarSub);
                    // GUESS-medium display adapter: center the real car model at the sampled
                    // centerline. Exact +0xCA/seat transforms from 0x800B2758 remain unported.
                    var mesh = ModelMesh.Build(model, null, _modelSheets, true, false, false,
                        CarOrigin(model), 1f / ParkTerrain.TileUnits, out _);
                    car = new MeshInstance3D { Mesh = mesh };
                    a.Inst.AddChild(car); // Lifetime follows demolition/load of the station.
                    a.CoasterCars.Add(train.PoolId, car);
                }
                var p = train.Pose.Position;
                car.GlobalPosition = new Vector3(p.X, p.Y, -p.Z) / ParkTerrain.TileUnits;
                var t = train.Pose.Tangent;
                // Host rendering only. Movement continues without drawing or a nonzero tangent.
                var forward = new Vector3(t.X, t.Y, -t.Z);
                if (forward.LengthSquared() > 0 && forward.Cross(Vector3.Up).LengthSquared() > 0)
                    car.GlobalBasis = Basis.LookingAt(forward.Normalized());
                car.Visible = true;
            }
        }

        static Vector3 CarOrigin(TPW.Data.Mesh model)
        {
            if (model.VertexCount == 0) return Vector3.Zero;
            int x0 = int.MaxValue, z0 = x0, y0 = x0, x1 = int.MinValue, z1 = x1;
            for (int i = 0; i < model.VertexCount; i++)
            {
                x0 = Math.Min(x0, model.Vertices[i * 3]); x1 = Math.Max(x1, model.Vertices[i * 3]);
                y0 = Math.Min(y0, model.Vertices[i * 3 + 1]);
                z0 = Math.Min(z0, model.Vertices[i * 3 + 2]); z1 = Math.Max(z1, model.Vertices[i * 3 + 2]);
            }
            return new Vector3((x0 + x1) / 2f, y0, -(z0 + z1) / 2f);
        }
    }
}
