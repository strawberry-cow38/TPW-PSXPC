using System;
using System.Collections.Generic;
using Godot;
using TPW.Data;

namespace TPWGodot
{
    /// <summary>A park, from its map: the first step toward drawing a park the way the game does.
    ///
    /// ⚠ WHAT THIS DRAWS TODAY IS THE TILE TYPES, NOT THE GROUND'S TEXTURES. Every tile is coloured by its type
    /// (grass, path, queue, footprint, entrance, exit, track, gate), which is an inspection view of the map
    /// data, deliberately not dressed up as terrain. The real ground is being read out of the game's own
    /// drawing code: the obvious reading of the tile's +4 field ("a sprite number in the world's sheet") was
    /// tested and is wrong (in the jungle sheet, #258, sprite 198 is a water shore, while every grass tile on
    /// the map names 198). tinyclaw measured the console's terrain as GENERATED, ~185 triangles a frame; that
    /// is the target once the real ground goes in.
    ///
    /// ✅ The map itself is right: tinyclaw found map #203 in the jungle park's RAM, 27,431 of 27,552 bytes
    /// identical to the disc copy, at 0x8017A5A8.
    ///
    /// Controls: WASD / arrows pan, Q/E turn, mouse wheel or +/- zoom, R/F tilt.</summary>
    public partial class ParkView : Node3D
    {
        MeshInstance3D _ground;
        Camera3D _camera;
        ParkMap _map;
        Vector3 _focus;
        float _yaw = Mathf.Pi, _pitch = -0.85f, _distance = 30f;
        Label _info;
        string _name = "";

        public bool HasMap => _map != null;

        public void SetInfoLabel(Label l) => _info = l;

        public override void _Ready()
        {
            _ground = new MeshInstance3D();
            AddChild(_ground);
            _camera = new Camera3D { Fov = 50, Current = false };
            AddChild(_camera);
            Visible = false;
        }

        static Color TypeColour(TileType t) => t switch
        {
            TileType.Grass => new Color(0.23f, 0.52f, 0.20f),
            TileType.Path => new Color(0.78f, 0.66f, 0.45f),
            TileType.QueuePath => new Color(0.93f, 0.55f, 0.20f),
            TileType.BuildingFootprint => new Color(0.45f, 0.45f, 0.48f),
            TileType.AttractionEntrance => new Color(0.25f, 0.45f, 0.95f),
            TileType.AttractionExit => new Color(0.90f, 0.25f, 0.25f),
            TileType.TrackPiece => new Color(0.60f, 0.30f, 0.75f),
            TileType.ParkGate => new Color(0.95f, 0.85f, 0.20f),
            TileType.PathQueueOverlap => new Color(0.88f, 0.75f, 0.52f),
            TileType.GateSide => new Color(0.70f, 0.62f, 0.15f),
            _ => new Color(1, 0, 1),
        };

        public void Load(ParkMap map, string name)
        {
            _map = map;
            _name = name;
            var verts = new List<Vector3>(map.Width * map.Height * 6);
            var cols = new List<Color>(map.Width * map.Height * 6);
            // ⭐ THE SHAPE IS REAL: byte +1 × 4 is the height in world units, where a tile is 256 (0x800620B8,
            // 0x800590A0). The height belongs to the tile's CORNER, so quad (x, z) spans the heights of tiles
            // (x, z), (x+1, z), (x, z+1) and (x+1, z+1), and the last row and column are corners only, which is
            // why the game's in-bounds test stops one short of the width and height.
            float H(int x, int y) => map[x, y].HeightUnits / 256f;
            for (int y = 0; y < map.Height - 1; y++)
                for (int x = 0; x < map.Width - 1; x++)
                {
                    var t = map[x, y];
                    var c = TypeColour(t.Type);
                    // The +6 variant (1..5 on the starting maps) nudges the shade, so the map's own variation shows.
                    float v = 1f + (t.Appearance - 3) * 0.04f;
                    c = new Color(c.R * v, c.G * v, c.B * v);
                    var a = new Vector3(x, H(x, y), y); var b = new Vector3(x + 1, H(x + 1, y), y);
                    var d = new Vector3(x, H(x, y + 1), y + 1); var e = new Vector3(x + 1, H(x + 1, y + 1), y + 1);
                    verts.AddRange(new[] { a, b, e, a, e, d });
                    // A darker edge line would need a second pass; shade the second triangle a touch instead so
                    // the grid still reads on slopes.
                    for (int k = 0; k < 3; k++) cols.Add(c);
                    var c2 = new Color(c.R * 0.93f, c.G * 0.93f, c.B * 0.93f);
                    for (int k = 0; k < 3; k++) cols.Add(c2);
                }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = verts.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Color] = cols.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(0, new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                // The colours above are display colours. Without this Godot takes them as linear and the output
                // encode washes every one of them out.
                VertexColorIsSrgb = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            });
            _ground.Mesh = mesh;

            // Start over the path strip if there is one (the park's entrance), else the middle.
            _focus = new Vector3(map.Width / 2f, 1, map.Height / 2f);
            for (int i = 0; i < map.Tiles.Length; i++)
                if (map.Tiles[i].IsWalkable) { _focus = new Vector3(i % map.Width + 0.5f, map.Tiles[i].HeightUnits / 256f, i / map.Width + 0.5f); break; }
            UpdateCamera();
        }

        /// <summary>Set the camera outright: focus tile (x, z), yaw and pitch in radians, distance in tiles. For
        /// captures; the keys do the same thing interactively.</summary>
        public void SetView(float x, float z, float yaw, float pitch, float distance)
        {
            if (_map != null)
            {
                int ix = Math.Clamp((int)x, 0, _map.Width - 1), iz = Math.Clamp((int)z, 0, _map.Height - 1);
                _focus = new Vector3(x, _map[ix, iz].HeightUnits / 256f, z);
            }
            _yaw = yaw; _pitch = pitch; _distance = distance;
            UpdateCamera();
        }

        public void Activate(bool on)
        {
            Visible = on;
            _camera.Current = on;
        }

        void UpdateCamera()
        {
            var dir = new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), Mathf.Sin(-_pitch), Mathf.Cos(_yaw) * Mathf.Cos(_pitch));
            _camera.Position = _focus + dir * _distance;
            _camera.LookAt(_focus, Vector3.Up);
            if (_info != null && _map != null)
            {
                var counts = new SortedDictionary<string, int>();
                foreach (var t in _map.Tiles) { var k = t.Type.ToString(); counts[k] = counts.GetValueOrDefault(k) + 1; }
                var parts = new List<string>();
                foreach (var kv in counts) parts.Add($"{kv.Key} {kv.Value}");
                _info.Text = $"{_name}: {_map.Width}x{_map.Height} tiles ({string.Join(", ", parts)})\n" +
                             "tile TYPES shown, not the real ground yet  ·  WASD/arrows pan, Q/E turn, wheel zoom, R/F tilt";
            }
        }

        public override void _Process(double delta)
        {
            if (!Visible || _map == null) return;
            float dt = (float)delta;
            var move = Vector2.Zero;
            if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Y -= 1;
            if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Y += 1;
            if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1;
            if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;
            bool changed = false;
            if (move != Vector2.Zero)
            {
                // Screen-relative: "up" moves away from the camera along the ground.
                var fwd = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
                var right = new Vector3(Mathf.Cos(_yaw), 0, -Mathf.Sin(_yaw));
                _focus += (right * move.X - fwd * move.Y) * dt * _distance * 0.6f;
                changed = true;
            }
            if (Input.IsKeyPressed(Key.Q)) { _yaw += dt * 1.5f; changed = true; }
            if (Input.IsKeyPressed(Key.E)) { _yaw -= dt * 1.5f; changed = true; }
            if (Input.IsKeyPressed(Key.R)) { _pitch = Mathf.Clamp(_pitch - dt, -1.5f, -0.2f); changed = true; }
            if (Input.IsKeyPressed(Key.F)) { _pitch = Mathf.Clamp(_pitch + dt, -1.5f, -0.2f); changed = true; }
            if (Input.IsKeyPressed(Key.Equal) || Input.IsKeyPressed(Key.KpAdd)) { _distance = Mathf.Max(3, _distance - dt * 20); changed = true; }
            if (Input.IsKeyPressed(Key.Minus) || Input.IsKeyPressed(Key.KpSubtract)) { _distance = Mathf.Min(120, _distance + dt * 20); changed = true; }
            if (changed) UpdateCamera();
        }

        public override void _UnhandledInput(InputEvent e)
        {
            if (!Visible || _map == null) return;
            if (e is InputEventMouseButton { Pressed: true } mb)
            {
                if (mb.ButtonIndex == MouseButton.WheelUp) { _distance = Mathf.Max(3, _distance * 0.9f); UpdateCamera(); }
                else if (mb.ButtonIndex == MouseButton.WheelDown) { _distance = Mathf.Min(120, _distance * 1.1f); UpdateCamera(); }
            }
        }
    }
}
