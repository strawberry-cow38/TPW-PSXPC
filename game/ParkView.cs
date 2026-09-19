using System;
using System.Collections.Generic;
using Godot;
using TPW.Data;

namespace TPWGodot
{
    /// <summary>A park's ground, drawn the way the game draws it.
    ///
    /// ⭐ THE GROUND IS THE GAME'S: every quad comes from <see cref="ParkTerrain"/>, which is the game's own terrain
    /// routine (0x80012110) — per tile a POLY_GT4 whose corners sit at the corner tiles' heights, textured with the
    /// sprite that tile's +4 names in its world's ground sheet (turned and mirrored as +4 says), and shaded per
    /// corner from the map's shade table. What the routine skips (flags bit 0: on map #203, a river that falls
    /// down a waterfall) is left open, because the game draws it with something else that is not read yet.
    ///
    /// T toggles the old inspection view, every tile coloured by its type, drawn just above the ground.
    ///
    /// ⚠ MIRRORED UNTIL Z WAS NEGATED, like the models. The game's world is left-handed and this one is not, so
    /// row z sits at Godot -z; the first version laid rows along +z and master saw the park mirrored.
    ///
    /// ✅ The map itself is right: tinyclaw found map #203 in the jungle park's RAM, 27,431 of 27,552 bytes
    /// identical to the disc copy, at 0x8017A5A8.
    ///
    /// Controls: WASD / arrows pan, Q/E turn, mouse wheel or +/- zoom, R/F tilt, T tile types.</summary>
    public partial class ParkView : Node3D
    {
        MeshInstance3D _ground, _types;
        Camera3D _camera;
        ParkMap _map;
        Vector3 _focus;
        float _yaw = 0f, _pitch = -0.85f, _distance = 30f;
        Label _info;
        string _infoText = "";

        public bool HasMap => _map != null;

        public void SetInfoLabel(Label l) => _info = l;

        public override void _Ready()
        {
            _ground = new MeshInstance3D();
            AddChild(_ground);
            _types = new MeshInstance3D { Visible = false };
            AddChild(_types);
            _camera = new Camera3D { Fov = 50, Current = false };
            AddChild(_camera);
            Visible = false;
        }

        /// <summary>Tile (x, z) at a height in world units, in Godot's frame: one tile per unit, z negated.</summary>
        static Vector3 At(float x, float z, int heightUnits) =>
            new Vector3(x, heightUnits / (float)ParkTerrain.TileUnits, -z);

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

        /// <param name="ground">The world's ground sheet, or null to draw the tile types alone.</param>
        public void Load(ParkMap map, string name, TextureSheet ground, ParkWorld world)
        {
            _map = map;
            int drawn = 0, open = 0;
            _ground.Mesh = null;
            if (ground != null)
            {
                var quads = ParkTerrain.Build(map, ground.Sprites);
                drawn = quads.Count;
                _ground.Mesh = GroundMesh(quads, ground);
            }
            foreach (var t in map.Tiles) if (t.NoGround) open++;
            _types.Mesh = TypeMesh(map);
            _types.Visible = ground == null;

            _infoText = $"{name}, {ParkWorlds.Describe(world)}: {map.Width}x{map.Height} tiles, " +
                        (ground != null ? $"ground from sheet #{world?.GroundSheet}, {drawn:n0} quads, {open} tiles left open (drawn by something not read yet)"
                                        : "no ground sheet, tile types only") +
                        "\nWASD/arrows pan, Q/E turn, wheel zoom, R/F tilt, T tile types";

            // Start over the path strip if there is one (the park's entrance), else the middle.
            _focus = At(map.Width / 2f, map.Height / 2f, 256);
            for (int i = 0; i < map.Tiles.Length; i++)
                if (map.Tiles[i].IsWalkable) { _focus = At(i % map.Width + 0.5f, i / map.Width + 0.5f, map.Tiles[i].HeightUnits); break; }
            UpdateCamera();
        }

        /// <summary>The ground: one atlas tile per (page, palette) the quads use, rendered as VRAM addresses it,
        /// so each corner's texel is the game's (u, v) plus the tile's origin.</summary>
        static ArrayMesh GroundMesh(List<GroundQuad> quads, TextureSheet sheet)
        {
            var tiles = new Dictionary<(ushort, ushort), int>();
            foreach (var q in quads)
            {
                var key = ((ushort)(q.TPage & 0x1FF), q.Clut);
                if (!tiles.ContainsKey(key)) tiles[key] = tiles.Count;
            }
            int tx = (int)Math.Ceiling(Math.Sqrt(Math.Max(1, tiles.Count))), ty = (tiles.Count + tx - 1) / tx;
            int aw = tx * TextureSheet.PageTexels, ah = Math.Max(1, ty) * TextureSheet.PageTexels;
            var rgba = new byte[aw * ah * 4];
            foreach (var ((tpage, clut), t) in tiles)
                sheet.RenderPage(tpage, clut, rgba, aw, (t % tx) * TextureSheet.PageTexels, (t / tx) * TextureSheet.PageTexels);

            var verts = new List<Vector3>(quads.Count * 6);
            var cols = new List<Color>(quads.Count * 6);
            var uvs = new List<Vector2>(quads.Count * 6);
            // The GPU draws a quad as triangles 0-1-2 and 1-2-3: the fold runs from (x+1, z) to (x, z+1), which is
            // what a bent tile shows. Winding does not matter here: the ground is drawn from both sides.
            int[] order = { 0, 1, 2, 2, 1, 3 };
            foreach (var q in quads)
            {
                int t = tiles[((ushort)(q.TPage & 0x1FF), q.Clut)];
                float ox = (t % tx) * TextureSheet.PageTexels, oy = (t / tx) * TextureSheet.PageTexels;
                foreach (int k in order)
                {
                    var c = q[k];
                    verts.Add(At(q.X + (k & 1), q.Z + (k >> 1), c.Height));
                    cols.Add(new Color((c.Shade & 0xFF) / 255f, ((c.Shade >> 8) & 0xFF) / 255f, ((c.Shade >> 16) & 0xFF) / 255f));
                    uvs.Add(new Vector2((ox + c.U) / aw, (oy + c.V) / ah));
                }
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = verts.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Color] = cols.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = uvs.ToArray();
            var mesh = new ArrayMesh();
            if (verts.Count == 0) return mesh;
            mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
            var mat = new ShaderMaterial { Shader = PsxShading.Shader(false) };
            mat.SetShaderParameter("atlas", ImageTexture.CreateFromImage(Image.CreateFromData(aw, ah, false, Image.Format.Rgba8, rgba)));
            mesh.SurfaceSetMaterial(0, mat);
            return mesh;
        }

        /// <summary>The inspection view: every tile coloured by its type, a hair above the ground.</summary>
        static ArrayMesh TypeMesh(ParkMap map)
        {
            var verts = new List<Vector3>(map.Width * map.Height * 6);
            var cols = new List<Color>(map.Width * map.Height * 6);
            const int lift = 8;   // world units (a tile is 256): enough to win the depth test, too little to see
            for (int z = 0; z < map.Height - 1; z++)
                for (int x = 0; x < map.Width - 1; x++)
                {
                    var c = TypeColour(map[x, z].Type);
                    var a = At(x, z, map[x, z].HeightUnits + lift);
                    var b = At(x + 1, z, map[x + 1, z].HeightUnits + lift);
                    var d = At(x, z + 1, map[x, z + 1].HeightUnits + lift);
                    var e = At(x + 1, z + 1, map[x + 1, z + 1].HeightUnits + lift);
                    verts.AddRange(new[] { a, b, d, d, b, e });
                    for (int k = 0; k < 3; k++) cols.Add(c);
                    // Shade the second triangle a touch so the grid still reads on slopes.
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
            return mesh;
        }

        /// <summary>Set the camera outright: focus tile (x, z), yaw and pitch in radians, distance in tiles. For
        /// captures; the keys do the same thing interactively.</summary>
        public void SetView(float x, float z, float yaw, float pitch, float distance)
        {
            if (_map != null)
            {
                int ix = Math.Clamp((int)x, 0, _map.Width - 1), iz = Math.Clamp((int)z, 0, _map.Height - 1);
                _focus = At(x, z, _map[ix, iz].HeightUnits);
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
            if (_info != null && _map != null) _info.Text = _infoText;
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
            else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.T } && _ground.Mesh != null)
                _types.Visible = !_types.Visible;
        }
    }
}
