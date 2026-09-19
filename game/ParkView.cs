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
    /// corner from the map's shade table. What the routine skips (flags bit 0) is where scenery models stand.
    ///
    /// ⭐ THE SCENERY IS THE GAME'S TOO: the map's build list places models from the world's scenery pack, and the
    /// tiles the terrain routine skips are where they stand (world 2's cliff faces, the jungle's river).
    ///
    /// T toggles the old inspection view, every tile coloured by its type, drawn just above the ground; O the scenery.
    ///
    /// ⚠ MIRRORED UNTIL Z WAS NEGATED, like the models. The game's world is left-handed and this one is not, so
    /// row z sits at Godot -z; the first version laid rows along +z and master saw the park mirrored.
    ///
    /// ✅ The map itself is right: tinyclaw found map #203 in the jungle park's RAM, 27,431 of 27,552 bytes
    /// identical to the disc copy, at 0x8017A5A8.
    ///
    /// Controls: WASD / arrows pan, Q/E turn, mouse wheel or +/- zoom, R/F tilt, T tile types, O scenery.</summary>
    public partial class ParkView : Node3D
    {
        MeshInstance3D _ground, _types, _scenery;
        /// <summary>The material ground and scenery share; its scroll_rows advances one row per park frame.</summary>
        ShaderMaterial _mat;
        double _scrollClock;
        Camera3D _camera;
        ParkMap _map;
        Vector3 _focus;
        float _yaw = 0f, _pitch = -0.85f, _distance = 30f;
        Label _info;
        string _infoText = "";

        public bool HasMap => _map != null;

        /// <summary>Tiles of ground drawn past each edge of the map, as the game draws them (ParkTerrain.Build): far
        /// enough that the park view's camera does not see the ground end.</summary>
        const int GroundBorder = 40;

        public void SetInfoLabel(Label l) => _info = l;

        public override void _Ready()
        {
            _ground = new MeshInstance3D();
            AddChild(_ground);
            _types = new MeshInstance3D { Visible = false };
            AddChild(_types);
            _scenery = new MeshInstance3D();
            AddChild(_scenery);
            // The game's own field of view: projection distance H = 256 (SetGeomScreen at 0x80054C44, from
            // gp+0x700) about the centre of its 512x256 PAL screen (0x800BA6A4 → 0x800BB3B4 sets 512x256 and puts
            // the projection centre at half of it), so the vertical view is 2·atan(128 / 256) = 53.13°.
            _camera = new Camera3D { Fov = 53.13f, Current = false };
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
        /// <param name="scenery">The world's scenery pack, or null for bare ground.</param>
        public void Load(ParkMap map, string name, TextureSheet ground, ParkWorld world, SceneryPack scenery)
        {
            _map = map;
            int open = 0, placed = 0, skipped = 0;
            _ground.Mesh = null;
            _scenery.Mesh = null;
            var quads = ground != null ? ParkTerrain.Build(map, ground.Sprites, GroundBorder) : new List<GroundQuad>();
            if (ground != null)
            {
                // ⭐ ONE ATLAS FOR BOTH: the scenery's textures are on the world's ground sheet too (SceneryPack).
                var uses = new List<(ushort, ushort)>();
                foreach (var q in quads) uses.Add((q.TPage, q.Clut));
                if (scenery != null)
                    foreach (var pl in map.Scenery)
                        if (pl.Model < scenery.Models.Count)
                            foreach (var t in scenery.Models[pl.Model].Textures) uses.Add((t.TPage, t.Clut));
                var atlas = PageAtlas.Build(ground, uses);
                var scroll = new Scrolling(ground.ScrollRects(), atlas);
                _mat = new ShaderMaterial { Shader = PsxShading.ScrollingShader() };
                _mat.SetShaderParameter("atlas", ImageTexture.CreateFromImage(
                    Image.CreateFromData(atlas.Image.Width, atlas.Image.Height, false, Image.Format.Rgba8, atlas.Image.Rgba)));
                _ground.Mesh = GroundMesh(quads, atlas, scroll, _mat);
                if (scenery != null) _scenery.Mesh = SceneryMesh(map, scenery, atlas, scroll, _mat, out placed, out skipped);
            }
            foreach (var t in map.Tiles) if (t.NoGround) open++;
            _types.Mesh = TypeMesh(map);
            _types.Visible = ground == null;

            _infoText = $"{name}, {ParkWorlds.Describe(world)}: {map.Width}x{map.Height} tiles, " +
                        (ground != null ? $"ground from sheet #{world?.GroundSheet}, {quads.Count:n0} quads (with {GroundBorder} tiles past each edge), {open} tiles left to the scenery" +
                                          (scenery != null ? $"; {placed} scenery models from #{world?.SceneryEntry}" + (skipped > 0 ? $" ({skipped} naming no model)" : "") : "; no scenery pack")
                                        : "no ground sheet, tile types only") +
                        "\nWASD/arrows pan, Q/E turn, wheel zoom, R/F tilt, T tile types, O scenery";

            // Start over the path strip if there is one (the park's entrance), else the middle.
            _focus = At(map.Width / 2f, map.Height / 2f, 256);
            for (int i = 0; i < map.Tiles.Length; i++)
                if (map.Tiles[i].IsWalkable) { _focus = At(i % map.Width + 0.5f, i / map.Width + 0.5f, map.Tiles[i].HeightUnits); break; }
            UpdateCamera();
        }

        /// <summary>The ground: every quad textured from the atlas at the game's (u, v) on its page.</summary>
        static ArrayMesh GroundMesh(List<GroundQuad> quads, PageAtlas atlas, Scrolling scroll, Material mat)
        {
            var verts = new List<Vector3>(quads.Count * 6);
            var cols = new List<Color>(quads.Count * 6);
            var uvs = new List<Vector2>(quads.Count * 6);
            var rects = new List<float>(quads.Count * 24);
            float aw = atlas.Image.Width, ah = atlas.Image.Height;
            // The GPU draws a quad as triangles 0-1-2 and 1-2-3: the fold runs from (x+1, z) to (x, z+1), which is
            // what a bent tile shows. Winding does not matter here: the ground is drawn from both sides.
            int[] order = { 0, 1, 2, 2, 1, 3 };
            foreach (var q in quads)
            {
                atlas.TryOrigin(q.TPage, q.Clut, out int ox, out int oy);
                var r = scroll.RectFor(q.TPage, q.Clut,
                    Math.Min(Math.Min(q.C0.U, q.C1.U), Math.Min(q.C2.U, q.C3.U)), Math.Min(Math.Min(q.C0.V, q.C1.V), Math.Min(q.C2.V, q.C3.V)),
                    Math.Max(Math.Max(q.C0.U, q.C1.U), Math.Max(q.C2.U, q.C3.U)), Math.Max(Math.Max(q.C0.V, q.C1.V), Math.Max(q.C2.V, q.C3.V)));
                foreach (int k in order)
                {
                    var c = q[k];
                    verts.Add(At(q.X + (k & 1), q.Z + (k >> 1), c.Height));
                    cols.Add(Shade(c.Shade));
                    uvs.Add(new Vector2((ox + c.U) / aw, (oy + c.V) / ah));
                    rects.Add(r.X); rects.Add(r.Y); rects.Add(r.Z); rects.Add(r.W);
                }
            }
            return Surface(verts, cols, uvs, rects, mat);
        }

        /// <summary>The scenery: each placement's model, scaled, turned and moved as 0x80057AF0 does, in the world's
        /// frame and then into Godot's (z negated, like everything else). Faces are drawn from both sides, as master
        /// asked of every model; the game itself drops a single-sided face that looks away.</summary>
        static ArrayMesh SceneryMesh(ParkMap map, SceneryPack pack, PageAtlas atlas, Scrolling scroll, Material mat, out int placed, out int skipped)
        {
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var uvs = new List<Vector2>();
            var rects = new List<float>();
            float aw = atlas.Image.Width, ah = atlas.Image.Height;
            int[] tri = { 0, 1, 2 }, quad = { 0, 1, 2, 2, 1, 3 };
            placed = skipped = 0;
            foreach (var pl in map.Scenery)
            {
                if (pl.Model >= pack.Models.Count) { skipped++; continue; }
                placed++;
                var m = pack.Models[pl.Model];
                foreach (var poly in m.Polygons)
                {
                    var tex = m.Textures[poly.Texture];
                    atlas.TryOrigin(tex.TPage, tex.Clut, out int ox, out int oy);
                    int n = poly.IsQuad ? 4 : 3, u0 = 255, v0 = 255, u1 = 0, v1 = 0;
                    for (int c = 0; c < n; c++)
                    {
                        ushort w = poly.Uv(c);
                        u0 = Math.Min(u0, w & 0xFF); u1 = Math.Max(u1, w & 0xFF); v0 = Math.Min(v0, w >> 8); v1 = Math.Max(v1, w >> 8);
                    }
                    var r = scroll.RectFor(tex.TPage, tex.Clut, u0, v0, u1, v1);
                    foreach (int k in poly.IsQuad ? quad : tri)
                    {
                        rects.Add(r.X); rects.Add(r.Y); rects.Add(r.Z); rects.Add(r.W);
                        int vi = poly.Corner(k);
                        var (x, y, z) = m.Position(vi);
                        var (wx, wy, wz) = pl.Place(x, y, z);
                        verts.Add(new Vector3(wx / ParkTerrain.TileUnits, wy / ParkTerrain.TileUnits, -wz / ParkTerrain.TileUnits));
                        byte g = m.Vertices[vi].Shade;
                        cols.Add(new Color(g / 255f, g / 255f, g / 255f));
                        ushort uv = poly.Uv(k);
                        uvs.Add(new Vector2((ox + (uv & 0xFF)) / aw, (oy + (uv >> 8)) / ah));
                    }
                }
            }
            return Surface(verts, cols, uvs, rects, mat);
        }

        /// <summary>Which scrolling rectangle, if any, a polygon's texels lie in, as an atlas rectangle for CUSTOM0.
        /// The game scrolls TEXELS in VRAM, so every palette's view of that page region scrolls with them: the match
        /// is on the page (and depth), not the palette.</summary>
        sealed class Scrolling
        {
            readonly List<(ushort TPage, int U, int V, int W, int H)> _rects;
            readonly PageAtlas _atlas;
            public Scrolling(List<(ushort, int, int, int, int)> rects, PageAtlas atlas) { _rects = rects; _atlas = atlas; }

            public Vector4 RectFor(ushort tpage, ushort clut, int u0, int v0, int u1, int v1)
            {
                foreach (var r in _rects)
                    if (PageAtlas.PageKey(r.TPage) == PageAtlas.PageKey(tpage) &&
                        u0 >= r.U && u1 <= r.U + r.W && v0 >= r.V && v1 <= r.V + r.H &&
                        _atlas.TryOrigin(tpage, clut, out int ox, out int oy))
                        return new Vector4(ox + r.U, oy + r.V, r.W, r.H);
                return Vector4.Zero;
            }
        }

        static Color Shade(uint bgr) => new Color((bgr & 0xFF) / 255f, ((bgr >> 8) & 0xFF) / 255f, ((bgr >> 16) & 0xFF) / 255f);

        static ArrayMesh Surface(List<Vector3> verts, List<Color> cols, List<Vector2> uvs, List<float> rects, Material mat)
        {
            var mesh = new ArrayMesh();
            if (verts.Count == 0) return mesh;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = verts.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Color] = cols.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = uvs.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Custom0] = rects.ToArray();
            var format = (Godot.Mesh.ArrayFormat)((long)Godot.Mesh.ArrayCustomFormat.RgbaFloat << (int)Godot.Mesh.ArrayFormat.FormatCustom0Shift);
            mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays, null, null, format);
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
            // One texel row per park frame. A park frame is one clock tick (2 vsyncs), so this is 25 rows a second at
            // PAL's 50 Hz: derived, like ParkClock.TickSeconds, whose assumption it shares.
            _scrollClock += delta;
            _mat?.SetShaderParameter("scroll_rows", (float)Math.Floor(_scrollClock / TPW.Sim.ParkClock.TickSeconds));
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
            else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.O })
                _scenery.Visible = !_scenery.Visible;
        }
    }
}
