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
        MeshInstance3D _ground, _types, _scenery, _flags, _build, _cursorMesh;
        /// <summary>The path tool (right mouse button): a cursor on the ground under the mouse; press the left button
        /// to start a run, release to lay it (PathTool, the game's rules). The ground sheet and scroll rectangles are
        /// kept to rebuild the ground after laying.</summary>
        PathTool _paths;
        bool _pathMode;
        /// <summary>The build tools' sound group (SoundGroup 7) and a player for it: the path tool's own sounds.</summary>
        SoundGroup _toolSounds;
        AudioStreamPlayer _sfx;
        readonly Dictionary<int, AudioStreamWav> _sfxStreams = new();
        (int X, int Z)? _cursorTile, _runStart;
        TextureSheet _groundSheet;
        Scrolling _scroll;
        /// <summary>The flag sprite (EntranceFlags) as its own texture, and its size in texels.</summary>
        ShaderMaterial _flagMat;
        int _flagW, _flagH;
        double _parkTime;   // the park's time accumulator, in the game's time units (EntranceFlags.TimeUnitsPerSecond)
        // The gate (ParkGate), drawn on its own so it can swing, and the effects (ParticleSystem).
        MeshInstance3D _gateMesh, _fxMesh;
        ParkGate.State _gate;
        SceneryModel _gateModel;
        (int X, int Y, int Z) _gateBase;
        PageAtlas _atlas;
        ParticleSystem _fx;
        /// <summary>The gate's opening effects (ParkGate.OpeningEffects), read at load, placed when they go off.</summary>
        readonly List<(EmitterTemplate T, int X, int Y, int Z)> _openingFx = new();
        readonly Dictionary<int, (ImageTexture Tex, int W, int H)> _fxSprites = new();
        TextureSheet _common;
        double _frameClock;
        int _gateAngleDrawn = int.MinValue;
        /// <summary>Whether the park is open: the gates only open then (0x800541AC).</summary>
        public bool ParkOpen { get; set; }
        /// <summary>The material ground and scenery share; its scroll_rows advances one row per park frame.</summary>
        ShaderMaterial _mat;
        /// <summary>The same for the polygons the game draws from one side only (<see cref="SingleSidedCull"/>).</summary>
        ShaderMaterial _matCull;
        /// <summary>⭐ THE GAME DROPS A SCENERY POLYGON THAT FACES AWAY unless its texture's flags say both sides
        /// (SceneryTexture.DoubleSided; 0x80035548 tests the GTE's NCLIP), so the port does too: Godot's cull mode
        /// for those polygons, in the port's winding (z negated).</summary>
        const string SingleSidedCull = "front";
        double _scrollClock;
        Camera3D _camera;
        ParkMap _map;
        Vector3 _focus;
        float _yaw = 0f, _pitch = -0.85f, _distance = 30f;
        /// <summary>The game's own camera (ParkCamera): fixed height and distance, quarter turns only. Off: the
        /// port's free camera, which can go where the game's never does.</summary>
        bool _gameCam;
        readonly ParkCamera _gcam = new();
        /// <summary>The game camera's eye and look-at point at the last two park frames, in Godot space. ⭐ THE
        /// CAMERA RUNS AT THE GAME'S 25 Hz AND IS DRAWN BETWEEN THEM: its height easing is per frame, not per unit
        /// of frame time, so stepping it at the render rate would change how it moves; planting the view on the
        /// last step made it judder at the port's frame rate (master). Drawing it part-way from the previous
        /// frame to the latest, by how far the clock is into the next, is smooth and moves exactly as the game's.</summary>
        Vector3 _gEyePrev, _gEyeCur, _gLookPrev, _gLookCur;
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
            _build = new MeshInstance3D { Visible = false };
            AddChild(_build);
            _cursorMesh = new MeshInstance3D();
            AddChild(_cursorMesh);
            _sfx = new AudioStreamPlayer();
            AddChild(_sfx);
            _scenery = new MeshInstance3D();
            AddChild(_scenery);
            _flags = new MeshInstance3D();
            AddChild(_flags);
            _gateMesh = new MeshInstance3D();
            AddChild(_gateMesh);
            _fxMesh = new MeshInstance3D();
            AddChild(_fxMesh);
            // The game's own field of view: projection distance H = 256 (SetGeomScreen at 0x80054C44, from
            // gp+0x700) about the centre of its 512x256 PAL screen (0x800BA6A4 → 0x800BB3B4 sets 512x256 and puts
            // the projection centre at half of it), so the vertical view is 2·atan(128 / 256) = 53.13°.
            _camera = new Camera3D { Fov = 53.13f, Current = false };
            // A plain light blue sky, master's call: the game's camera stays in a range of angles that never shows
            // the sky, so there is nothing of the game's to copy up there, and the port's freer camera needs
            // something better than the void.
            _camera.Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.56f, 0.78f, 0.95f),
            };
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
        /// <param name="common">The common sheet (#416), for the entrance flags; null leaves them out.</param>
        /// <param name="gatePack">The world's gate pack (ParkGate), drawn closed at the gate's base; null leaves it out.</param>
        public void Load(ParkMap map, string name, TextureSheet ground, ParkWorld world, SceneryPack scenery, TextureSheet common = null,
                         SceneryPack gatePack = null, byte[] exe = null)
        {
            // The park as the game has it once loaded: road typed, the square inside the gate laid as path (ParkPaths).
            if (exe != null && world != null) map = ParkPaths.LayStartingPaths(map, exe, AssetSelfTest.GameExecutableBase, world.Index);
            _map = map;
            _common = common;
            _gate = null; _gateModel = null; _gateMesh.Mesh = null; _gateAngleDrawn = int.MinValue;
            _fx = new ParticleSystem();
            _openingFx.Clear();
            _pathMode = false; _runStart = null; _cursorTile = null; _cursorMesh.Mesh = null; _paths = null; _cursorPinned = false;
            _fxMesh.Mesh = null;
            ParkOpen = false;
            _flagMat = null;
            _flags.Mesh = null;
            if (common != null && EntranceFlags.Sprite < common.Sprites.Count)
            {
                var img = common.RenderSprite(EntranceFlags.Sprite);
                _flagW = img.Width; _flagH = img.Height;
                _flagMat = new ShaderMaterial { Shader = PsxShading.Shader(false) };
                _flagMat.SetShaderParameter("atlas", ImageTexture.CreateFromImage(Image.CreateFromData(img.Width, img.Height, false, Image.Format.Rgba8, img.Rgba)));
            }
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
                // Every path piece the tool can lay, so a path laid later has its texture in the atlas.
                _paths = exe != null && world != null ? PathTool.Create(exe, AssetSelfTest.GameExecutableBase, world.Index) : null;
                if (_paths != null)
                    foreach (int ps in _paths.Sprites)
                        if (ps < ground.Sprites.Count) uses.Add((ground.Sprites[ps].TPage, ground.Sprites[ps].Clut));
                var gate = world != null ? ParkGate.ForWorld(world.Index) : null;
                if (gate != null && gatePack != null && gatePack.Models.Count > 0)
                    foreach (var t in gatePack.Models[0].Textures) uses.Add((t.TPage, t.Clut));
                else gate = null;
                var atlas = PageAtlas.Build(ground, uses);
                var scroll = new Scrolling(ground.ScrollRects(), atlas);
                var atlasTex = ImageTexture.CreateFromImage(
                    Image.CreateFromData(atlas.Image.Width, atlas.Image.Height, false, Image.Format.Rgba8, atlas.Image.Rgba));
                _mat = new ShaderMaterial { Shader = PsxShading.ScrollingShader() };
                _mat.SetShaderParameter("atlas", atlasTex);
                _matCull = new ShaderMaterial { Shader = PsxShading.ScrollingShader(SingleSidedCull) };
                _matCull.SetShaderParameter("atlas", atlasTex);
                _ground.Mesh = GroundMesh(quads, atlas, scroll, _mat);
                _atlas = atlas;
                _groundSheet = ground;
                _scroll = scroll;
                if (scenery != null) _scenery.Mesh = SceneryMesh(map, scenery, atlas, scroll, _mat, _matCull, out placed, out skipped);
                if (gate != null)
                {
                    _gate = new ParkGate.State(gate);
                    _gateModel = gatePack.Models[0];
                    _gateBase = (gate.TileX * ParkTerrain.TileUnits, map[gate.TileX, gate.TileZ].HeightUnits, gate.TileZ * ParkTerrain.TileUnits);
                    if (exe != null)
                    {
                        foreach (var (ex, ey, ez, addr) in gate.Effects)
                        {
                            var t = EmitterTemplate.Read(exe, AssetSelfTest.GameExecutableBase, addr);
                            if (t != null) _fx.Add(t, _gateBase.X + ex, _gateBase.Y + ey, _gateBase.Z + ez);
                        }
                        foreach (var (ex, ey, ez, addr, _) in gate.OpeningEffects)
                            _openingFx.Add((EmitterTemplate.Read(exe, AssetSelfTest.GameExecutableBase, addr), _gateBase.X + ex, _gateBase.Y + ey, _gateBase.Z + ez));
                    }
                }
            }
            foreach (var t in map.Tiles) if (t.NoGround) open++;
            _types.Mesh = TypeMesh(map);
            _types.Visible = ground == null;
            _build.Mesh = BuildMesh(map, out string buildCounts);

            _infoText = $"{name}, {ParkWorlds.Describe(world)}: {map.Width}x{map.Height} tiles, " +
                        (ground != null ? $"ground from sheet #{world?.GroundSheet}, {quads.Count:n0} quads (with {GroundBorder} tiles past each edge), {open} tiles left to the scenery" +
                                          (scenery != null ? $"; {placed} scenery models from #{world?.SceneryEntry}" + (skipped > 0 ? $" ({skipped} naming no model)" : "") : "; no scenery pack")
                                        : "no ground sheet, tile types only") +
                        "\n" + buildCounts +
                        "\nWASD/arrows pan, Q/E turn, wheel zoom, R/F tilt, T tile types, B where you can build, O scenery, P open the park, G the game's camera, left-click the ground to lay path";

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
        static ArrayMesh SceneryMesh(ParkMap map, SceneryPack pack, PageAtlas atlas, Scrolling scroll, Material mat, Material matCull,
                                     out int placed, out int skipped)
        {
            var both = new Buffers(); var single = new Buffers();
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
                    var b = tex.DoubleSided ? both : single;
                    foreach (int k in poly.IsQuad ? quad : tri)
                    {
                        b.R.Add(r.X); b.R.Add(r.Y); b.R.Add(r.Z); b.R.Add(r.W);
                        int vi = poly.Corner(k);
                        var (x, y, z) = m.Position(vi);
                        var (wx, wy, wz) = pl.Place(x, y, z);
                        b.V.Add(new Vector3(wx / ParkTerrain.TileUnits, wy / ParkTerrain.TileUnits, -wz / ParkTerrain.TileUnits));
                        byte g = m.Vertices[vi].Shade;
                        b.C.Add(new Color(g / 255f, g / 255f, g / 255f));
                        ushort uv = poly.Uv(k);
                        b.UV.Add(new Vector2((ox + (uv & 0xFF)) / aw, (oy + (uv >> 8)) / ah));
                    }
                }
            }
            return Surfaces(both, mat, single, matCull);
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

        /// <summary>The eight entrance flags at time t (EntranceFlags): both sets, each quad as the GPU's two triangles.</summary>
        ArrayMesh FlagMesh(long t)
        {
            var verts = new List<Vector3>(8 * 24);
            var cols = new List<Color>(8 * 24);
            var uvs = new List<Vector2>(8 * 24);
            void Set((int X, int Y, int Z)[] at, int phase)
            {
                var g = EntranceFlags.Grid(phase);
                foreach (var pos in at)
                    for (int q = 0; q < 4; q++)
                    {
                        var (u0, v0, u1, v1) = EntranceFlags.QuadTexels(q, _flagW, _flagH);
                        // Grid corners in the game's order, each with its texel: (u0,v0) (u0,v1) (u1,v0) (u1,v1).
                        int[] corner = { EntranceFlags.Quads[q * 4], EntranceFlags.Quads[q * 4 + 1], EntranceFlags.Quads[q * 4 + 2], EntranceFlags.Quads[q * 4 + 3] };
                        var tex = new[] { new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v0), new Vector2(u1, v1) };
                        foreach (int k in new[] { 0, 1, 2, 2, 1, 3 })
                        {
                            var v = g[corner[k]];
                            var w = EntranceFlags.Place(pos, v);
                            verts.Add(new Vector3(w.X / (float)ParkTerrain.TileUnits, w.Y / (float)ParkTerrain.TileUnits, -w.Z / (float)ParkTerrain.TileUnits));
                            float grey = Math.Clamp(EntranceFlags.Grey(v.Z), 0, 255) / 255f;
                            cols.Add(new Color(grey, grey, grey));
                            uvs.Add(new Vector2((tex[k].X + 0.5f) / _flagW, (tex[k].Y + 0.5f) / _flagH));
                        }
                    }
            }
            Set(EntranceFlags.SetA, EntranceFlags.PhaseA(t));
            Set(EntranceFlags.SetB, EntranceFlags.PhaseB(t));
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = verts.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Color] = cols.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = uvs.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(0, _flagMat);
            return mesh;
        }

        /// <summary>The gate's moving parts at the gate state's angle (ParkGate.State.Part), textured from the park atlas.</summary>
        ArrayMesh GateMesh()
        {
            var both = new Buffers(); var single = new Buffers();
            float aw = _atlas.Image.Width, ah = _atlas.Image.Height;
            int[] tri = { 0, 1, 2 }, quad = { 0, 1, 2, 2, 1, 3 };
            for (int i = 0; i < _gate.Gate.Parts.Length; i++)
            {
                var part = _gate.Part(i);
                foreach (var poly in _gateModel.Polygons)
                {
                    var tex = _gateModel.Textures[poly.Texture];
                    var b = tex.DoubleSided ? both : single;
                    _atlas.TryOrigin(tex.TPage, tex.Clut, out int ox, out int oy);
                    foreach (int k in poly.IsQuad ? quad : tri)
                    {
                        int vi = poly.Corner(k);
                        var (wx, wy, wz) = ParkGate.Place(part, _gateBase, _gateModel.Position(vi));
                        b.V.Add(new Vector3(wx / ParkTerrain.TileUnits, wy / ParkTerrain.TileUnits, -wz / ParkTerrain.TileUnits));
                        byte g = _gateModel.Vertices[vi].Shade;
                        b.C.Add(new Color(g / 255f, g / 255f, g / 255f));
                        ushort uv = poly.Uv(k);
                        b.UV.Add(new Vector2((ox + (uv & 0xFF)) / aw, (oy + (uv >> 8)) / ah));
                        b.R.Add(0); b.R.Add(0); b.R.Add(0); b.R.Add(0);
                    }
                }
            }
            return Surfaces(both, _mat, single, _matCull);
        }

        /// <summary>Vertex streams for one surface: positions, shades, UVs and the scroll rectangle (CUSTOM0).</summary>
        sealed class Buffers
        {
            public readonly List<Vector3> V = new();
            public readonly List<Color> C = new();
            public readonly List<Vector2> UV = new();
            public readonly List<float> R = new();
        }

        /// <summary>A mesh of the polygons drawn from both sides and those drawn from the front only, each with its
        /// material.</summary>
        static ArrayMesh Surfaces(Buffers both, Material bothMat, Buffers single, Material singleMat)
        {
            var mesh = new ArrayMesh();
            foreach (var (b, mat) in new[] { (both, bothMat), (single, singleMat) })
            {
                if (b.V.Count == 0) continue;
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Godot.Mesh.ArrayType.Max);
                arrays[(int)Godot.Mesh.ArrayType.Vertex] = b.V.ToArray();
                arrays[(int)Godot.Mesh.ArrayType.Color] = b.C.ToArray();
                arrays[(int)Godot.Mesh.ArrayType.TexUV] = b.UV.ToArray();
                arrays[(int)Godot.Mesh.ArrayType.Custom0] = b.R.ToArray();
                var format = (Godot.Mesh.ArrayFormat)((long)Godot.Mesh.ArrayCustomFormat.RgbaFloat << (int)Godot.Mesh.ArrayFormat.FormatCustom0Shift);
                mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays, null, null, format);
                mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, mat);
            }
            return mesh;
        }

        /// <summary>The live particles as billboards facing the camera, sized as 0x80034BDC sizes them: half-extents
        /// sprite width × 0.8 × size / 16 and sprite height × size / 16 world units (the 0.8 is the game's own,
        /// for its 512-wide screen), coloured by the particle, blended by its definition (0x8008B014).</summary>
        ArrayMesh FxMesh()
        {
            var mesh = new ArrayMesh();
            if (_fx == null || _fx.Particles.Count == 0 || _common == null) return mesh;
            var right = _camera.GlobalTransform.Basis.X.Normalized();
            var up = _camera.GlobalTransform.Basis.Y.Normalized();
            // One bucket per (sprite, blend): each is a surface pair (solid texels, blended texels) as the GPU draws it.
            var buckets = new Dictionary<(int, int), (List<Vector3> V, List<Color> C, List<Vector2> UV)>();
            foreach (var p in _fx.Particles)
            {
                var d = p.Def;
                if (!_fxSprites.TryGetValue(d.Sprite, out var spr))
                {
                    var img = _common.RenderSprite(d.Sprite);
                    spr = img == null ? (null, 1, 1) : (ImageTexture.CreateFromImage(Image.CreateFromData(img.Width, img.Height, false, Image.Format.Rgba8, img.Rgba)), img.Width, img.Height);
                    _fxSprites[d.Sprite] = spr;
                }
                if (spr.Tex == null) continue;
                var key = (d.Sprite, d.Blend);
                if (!buckets.TryGetValue(key, out var b)) buckets[key] = b = (new List<Vector3>(), new List<Color>(), new List<Vector2>());
                float hw = spr.W * 0.8f * p.SizeUnits / 16f / ParkTerrain.TileUnits, hh = spr.H * p.SizeUnits / 16f / ParkTerrain.TileUnits;
                var c = new Vector3(p.X / (float)ParkTerrain.TileUnits, p.Y / (float)ParkTerrain.TileUnits, -p.Z / (float)ParkTerrain.TileUnits);
                var col = new Color(Math.Clamp(p.R >> 8, 0, 255) / 255f, Math.Clamp(p.G >> 8, 0, 255) / 255f, Math.Clamp(p.B >> 8, 0, 255) / 255f);
                Vector3 tl = c - right * hw + up * hh, tr = c + right * hw + up * hh, bl = c - right * hw - up * hh, br = c + right * hw - up * hh;
                foreach (var (v, uv) in new[] { (tl, new Vector2(0, 0)), (tr, new Vector2(1, 0)), (bl, new Vector2(0, 1)), (bl, new Vector2(0, 1)), (tr, new Vector2(1, 0)), (br, new Vector2(1, 1)) })
                { b.V.Add(v); b.C.Add(col); b.UV.Add(uv); }
            }
            foreach (var ((sprite, blend), b) in buckets)
            {
                var tex = _fxSprites[sprite].Tex;
                // The definition's blend (0x8008B014): 0 solid; 1 the page's own mode; 2 subtract; 3 add; 4 add a quarter.
                int mode = blend switch { 2 => 2, 3 => 1, 4 => 3, 1 => (_common.Sprites[sprite].TPage >> 5) & 3, _ => -1 };
                void Add(Shader sh)
                {
                    var arrays = new Godot.Collections.Array();
                    arrays.Resize((int)Godot.Mesh.ArrayType.Max);
                    arrays[(int)Godot.Mesh.ArrayType.Vertex] = b.V.ToArray();
                    arrays[(int)Godot.Mesh.ArrayType.Color] = b.C.ToArray();
                    arrays[(int)Godot.Mesh.ArrayType.TexUV] = b.UV.ToArray();
                    mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
                    var m = new ShaderMaterial { Shader = sh };
                    m.SetShaderParameter("atlas", tex);
                    mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, m);
                }
                if (mode < 0) Add(PsxShading.Shader(false));
                else { Add(PsxShading.SemiTransparentShader(mode, false, false)); Add(PsxShading.SemiTransparentShader(mode, true, false)); }
            }
            return mesh;
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

        /// <summary>Show or hide where a ride or shop may stand (the B key; for captures).</summary>
        public bool ShowBuildable { get => _build.Visible; set => _build.Visible = value; }

        static Color VerdictColour(ParkBuild.Verdict v) => v switch
        {
            ParkBuild.Verdict.Buildable => new Color(0.20f, 0.85f, 0.30f, 0.55f),
            ParkBuild.Verdict.TooSteep => new Color(1.00f, 0.60f, 0.10f, 0.65f),
            ParkBuild.Verdict.Unbuildable => new Color(0.90f, 0.15f, 0.15f, 0.65f),
            ParkBuild.Verdict.NoGround => new Color(0.60f, 0.25f, 0.85f, 0.65f),
            ParkBuild.Verdict.Entrance => new Color(1.00f, 0.90f, 0.20f, 0.65f),
            ParkBuild.Verdict.Occupied => new Color(0.25f, 0.50f, 1.00f, 0.65f),
            _ => new Color(0.30f, 0.30f, 0.33f, 0.65f),
        };

        /// <summary>Every tile coloured by whether a ride or shop may stand on it (ParkBuild, the game's placement
        /// test): green yes, orange too steep, red ruled out by the map, purple where scenery stands, yellow the
        /// entrance, blue path, grey the edge. Drawn just above the ground and see-through, over the scenery's feet.</summary>
        static ArrayMesh BuildMesh(ParkMap map, out string counts)
        {
            var verts = new List<Vector3>(map.Width * map.Height * 6);
            var cols = new List<Color>(map.Width * map.Height * 6);
            var n = new int[System.Enum.GetValues(typeof(ParkBuild.Verdict)).Length];
            const int lift = 10;
            for (int z = 0; z < map.Height - 1; z++)
                for (int x = 0; x < map.Width - 1; x++)
                {
                    var v = ParkBuild.Classify(map, x, z);
                    n[(int)v]++;
                    var c = VerdictColour(v);
                    var a = At(x, z, map[x, z].HeightUnits + lift);
                    var b = At(x + 1, z, map[x + 1, z].HeightUnits + lift);
                    var d = At(x, z + 1, map[x, z + 1].HeightUnits + lift);
                    var e = At(x + 1, z + 1, map[x + 1, z + 1].HeightUnits + lift);
                    verts.AddRange(new[] { a, b, d, d, b, e });
                    for (int k = 0; k < 6; k++) cols.Add(c);
                }
            counts = $"build: {n[(int)ParkBuild.Verdict.Buildable]} tiles buildable, {n[(int)ParkBuild.Verdict.TooSteep]} too steep, " +
                     $"{n[(int)ParkBuild.Verdict.Unbuildable]} ruled out by the map, {n[(int)ParkBuild.Verdict.NoGround]} under scenery, " +
                     $"{n[(int)ParkBuild.Verdict.Entrance]} entrance, {n[(int)ParkBuild.Verdict.Occupied]} path, {n[(int)ParkBuild.Verdict.Edge]} edge";
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
                VertexColorIsSrgb = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            });
            return mesh;
        }

        /// <summary>Use the game's camera (G) or the free one. Switching carries the view across: the game camera
        /// starts at the free camera's focus and nearest quarter turn, the free camera where the game's one is.</summary>
        public bool GameCamera
        {
            get => _gameCam;
            set
            {
                if (_map == null || value == _gameCam) { _gameCam = value && _map != null; return; }
                _gameCam = value;
                if (value)
                {
                    int yaw = (int)Math.Round(-_yaw * 4096 / (2 * Math.PI) / ParkCamera.QuarterTurn) * ParkCamera.QuarterTurn;
                    _gcam.Reset(_map, ClampX(_focus.X * ParkTerrain.TileUnits), ClampZ(-_focus.Z * ParkTerrain.TileUnits), yaw);
                    TakeGameCamera(); _gEyePrev = _gEyeCur; _gLookPrev = _gLookCur;
                    PlaceGameCamera();
                }
                else
                {
                    _yaw = -(_gcam.Yaw & 0xFFF) * 2 * Mathf.Pi / 4096;
                    _pitch = -Mathf.Atan2(ParkCamera.Above, ParkCamera.Behind);
                    _distance = Mathf.Sqrt((float)ParkCamera.Above * ParkCamera.Above + (float)ParkCamera.Behind * ParkCamera.Behind) / ParkTerrain.TileUnits;
                    _focus = new Vector3((_gcam.FocusX >> 8) / (float)ParkTerrain.TileUnits, _gcam.LookY / (float)ParkTerrain.TileUnits,
                                         -(_gcam.FocusZ >> 8) / (float)ParkTerrain.TileUnits);
                    UpdateCamera();
                }
                RefreshInfo();
            }
        }

        int ClampX(float units) => (int)Math.Clamp(units, 0, (_map.Width - 1) * ParkTerrain.TileUnits);
        int ClampZ(float units) => (int)Math.Clamp(units, 0, (_map.Height - 1) * ParkTerrain.TileUnits);

        /// <summary>The game camera's current eye and look-at point, into <see cref="_gEyeCur"/> / <see cref="_gLookCur"/>.</summary>
        void TakeGameCamera()
        {
            float u = ParkTerrain.TileUnits;
            _gEyeCur = new Vector3(_gcam.EyeX / u, _gcam.EyeY / u, -_gcam.EyeZ / u);
            _gLookCur = new Vector3((_gcam.FocusX >> 8) / u, _gcam.LookY / u, -(_gcam.FocusZ >> 8) / u);
        }

        /// <summary>Put the view between the last two park frames, by how far the park clock is into the next.</summary>
        void PlaceGameCamera()
        {
            float t = Mathf.Clamp((float)(_frameClock * ParticleSystem.FramesPerSecond), 0f, 1f);
            _camera.Position = _gEyePrev.Lerp(_gEyeCur, t);
            _camera.LookAt(_gLookPrev.Lerp(_gLookCur, t), Vector3.Up);
        }

        void RefreshInfo()
        {
            if (_info != null && _map != null)
                _info.Text = _infoText + (_gameCam ? "\ncamera: THE GAME'S (fixed height and distance, Q/E quarter turns); G for the free camera"
                                                  : "\ncamera: free; G for the game's own")
                           + (_pathMode ? "\nPATH TOOL: click the start, then click the end; right button cancels the ghost, then closes the tool" : "");
        }

        /// <summary>The tile under the mouse: march the ray from the camera through the pointer until it drops below
        /// the ground as drawn (ParkCamera.GroundHeight). Null off the map or when the ray never meets the ground.</summary>
        (int X, int Z)? TileUnderMouse()
        {
            var mp = GetViewport().GetMousePosition();
            Vector3 from = _camera.ProjectRayOrigin(mp), dir = _camera.ProjectRayNormal(mp);
            float u = ParkTerrain.TileUnits;
            for (float t = 0; t < 400; t += 0.05f)
            {
                var p = from + dir * t;
                int gx = (int)Math.Floor(p.X * u), gz = (int)Math.Floor(-p.Z * u);
                if (p.Y * u > ParkCamera.GroundHeight(_map, gx, gz)) continue;
                int tx = gx >> 8, tz = gz >> 8;
                return tx >= 0 && tx < _map.Width - 1 && tz >= 0 && tz < _map.Height - 1 ? (tx, tz) : null;
            }
            return null;
        }

        /// <summary>The path cursor: the tile under the mouse, or the run from the pressed tile to it, each tile green
        /// where the tool takes path and red where it refuses.</summary>
        ArrayMesh CursorMesh()
        {
            var mesh = new ArrayMesh();
            if (_cursorTile is not { } cur || _paths == null || _common == null) return mesh;
            var run = _runStart is { } st ? PathTool.Run(st.X, st.Z, cur.X, cur.Z) : new List<(int X, int Z)> { cur };
            // ⭐ THE GAME'S GHOST (0x8001D9D0 → 0x800553B0): each tile of the run wears a marker sprite from the common
            // sheet chosen by the tool's own verdict, drawn semi-transparent (the markers' texels all carry the GPU's
            // blend bit, their page is mode 0: half and half) over the ground, neutral grey. The game also pulses the
            // markers' brightness and lays a dim quad under them; not yet.
            var byMarker = new Dictionary<int, (List<Vector3> V, List<Color> C, List<Vector2> UV)>();
            const int lift = 12;
            var grey = new Color(0.5f, 0.5f, 0.5f);
            foreach (var (x, z, sprite, _) in _paths.Ghost(_map, run))
            {
                if (!byMarker.TryGetValue(sprite, out var b)) byMarker[sprite] = b = (new List<Vector3>(), new List<Color>(), new List<Vector2>());
                var a = At(x, z, _map[x, z].HeightUnits + lift);
                var p1 = At(x + 1, z, _map[x + 1, z].HeightUnits + lift);
                var d = At(x, z + 1, _map[x, z + 1].HeightUnits + lift);
                var e = At(x + 1, z + 1, _map[x + 1, z + 1].HeightUnits + lift);
                b.V.AddRange(new[] { a, p1, d, d, p1, e });
                b.UV.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) });
                for (int k = 0; k < 6; k++) b.C.Add(grey);
            }
            foreach (var (sprite, b) in byMarker)
            {
                if (!_markerTex.TryGetValue(sprite, out var tex))
                {
                    var img = sprite < _common.Sprites.Count ? _common.RenderSprite(sprite) : null;
                    tex = img == null ? null : ImageTexture.CreateFromImage(Image.CreateFromData(img.Width, img.Height, false, Image.Format.Rgba8, img.Rgba));
                    _markerTex[sprite] = tex;
                }
                if (tex == null) continue;
                foreach (bool blended in new[] { false, true })
                {
                    var arrays = new Godot.Collections.Array();
                    arrays.Resize((int)Godot.Mesh.ArrayType.Max);
                    arrays[(int)Godot.Mesh.ArrayType.Vertex] = b.V.ToArray();
                    arrays[(int)Godot.Mesh.ArrayType.Color] = b.C.ToArray();
                    arrays[(int)Godot.Mesh.ArrayType.TexUV] = b.UV.ToArray();
                    mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
                    // The blend is the sprite's own page's (its record's tpage, bits 5-6): mode 0, half and half, for
                    // every marker on the disc, read rather than assumed.
                    int mode = (_common.Sprites[sprite].TPage >> 5) & 3;
                    var m = new ShaderMaterial { Shader = PsxShading.SemiTransparentShader(mode, blended, false) };
                    m.SetShaderParameter("atlas", tex);
                    mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, m);
                }
            }
            return mesh;
        }
        readonly Dictionary<int, ImageTexture> _markerTex = new();

        void LayPath((int X, int Z) start, (int X, int Z) end)
        {
            int laid = _paths.Lay(_map, PathTool.Run(start.X, start.Z, end.X, end.Z));
            if (laid > 0) { RebuildGround(); PlaySfx(ToolSound.Lay); }
            else PlaySfx(ToolSound.Refused);
        }

        /// <summary>The path tool's sounds in group 7, as 0x8001D5C0 plays them.</summary>
        public enum ToolSound { Start = 0, Refused = 2, Finished = 3, Lay = 4 }

        /// <summary>Give the park view the build tools' sound group (SoundGroup.Load(…, 7)).</summary>
        public void SetToolSounds(SoundGroup group) { _toolSounds = group; _sfxStreams.Clear(); }

        /// <summary>Play one of the tools' sounds, at the rate its record's pitch gives, full volume, centred (0x800B84AC).</summary>
        void PlaySfx(ToolSound which)
        {
            int n = (int)which;
            if (_toolSounds == null || _sfx == null) return;
            if (!_sfxStreams.TryGetValue(n, out var stream))
            {
                var pcm = _toolSounds.Decode(n);
                if (pcm == null || pcm.SampleCount == 0) { _sfxStreams[n] = null; return; }
                var bytes = new byte[pcm.SampleCount * 2];
                Buffer.BlockCopy(pcm.Samples, 0, bytes, 0, bytes.Length);
                stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = _toolSounds.Sounds[n].SampleRate, Stereo = false, Data = bytes };
                _sfxStreams[n] = stream;
            }
            if (stream == null) return;
            _sfx.Stream = stream;
            _sfx.Play();
        }

        /// <summary>Lay a run of path as the tool would, from tile (x0, z0) toward (x1, z1). For captures: the mouse
        /// does the same interactively. Returns how many tiles took path.</summary>
        public int LayRun(int x0, int z0, int x1, int z1)
        {
            if (_paths == null || _map == null) return 0;
            int laid = _paths.Lay(_map, PathTool.Run(x0, z0, x1, z1));
            if (laid > 0) RebuildGround();
            return laid;
        }

        /// <summary>Show the path cursor pinned at tile (x, z), with a run from (sx, sz) when given, instead of
        /// following the mouse. For captures.</summary>
        public void PinPathCursor(int x, int z, int sx = -1, int sz = -1)
        {
            if (_paths == null) return;
            _pathMode = true; _cursorPinned = true;
            _cursorTile = (x, z);
            _runStart = sx >= 0 ? (sx, sz) : null;
            RefreshInfo();
        }
        bool _cursorPinned;

        /// <summary>After the map changes: the ground again from the map as it now is, and the overlays that read it.</summary>
        void RebuildGround()
        {
            if (_groundSheet == null || _atlas == null) return;
            _ground.Mesh = GroundMesh(ParkTerrain.Build(_map, _groundSheet.Sprites, GroundBorder), _atlas, _scroll, _mat);
            _types.Mesh = TypeMesh(_map);
            _build.Mesh = BuildMesh(_map, out _);
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
            if (_gameCam) { _gameCam = false; GameCamera = true; }
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
            RefreshInfo();
        }

        public override void _Process(double delta)
        {
            if (!Visible || _map == null) return;
            // One texel row per park frame. A park frame is one clock tick (2 vsyncs), so this is 25 rows a second at
            // PAL's 50 Hz: derived, like ParkClock.TickSeconds, whose assumption it shares.
            _scrollClock += delta;
            float rows = (float)Math.Floor(_scrollClock / TPW.Sim.ParkClock.TickSeconds);
            _mat?.SetShaderParameter("scroll_rows", rows);
            _matCull?.SetShaderParameter("scroll_rows", rows);
            _parkTime += delta * EntranceFlags.TimeUnitsPerSecond;
            if (_flagMat != null) _flags.Mesh = FlagMesh((long)_parkTime);
            // Park frames: 25 a second (a frame per clock tick at PAL's 50 Hz), each worth 1/25 s of the game's time.
            _frameClock += delta;
            int frameTime = (int)(EntranceFlags.TimeUnitsPerSecond / ParticleSystem.FramesPerSecond);
            int frames = 0;
            while (_frameClock >= 1.0 / ParticleSystem.FramesPerSecond && frames < 10)
            {
                _frameClock -= 1.0 / ParticleSystem.FramesPerSecond;
                frames++;
                if (_gameCam)
                {
                    _gEyePrev = _gEyeCur; _gLookPrev = _gLookCur;
                    _gcam.Step(_map, frameTime);
                    TakeGameCamera();
                }
                _gate?.Update(frameTime, ParkOpen);
                if (_gate != null)
                    foreach (int i in _gate.TakeDueEffects())
                        if (i < _openingFx.Count && _openingFx[i].T != null)
                            _fx?.Add(_openingFx[i].T, _openingFx[i].X, _openingFx[i].Y, _openingFx[i].Z);
                _fx?.Step();
            }
            if (_gate != null && _gate.Angle != _gateAngleDrawn) { _gateMesh.Mesh = GateMesh(); _gateAngleDrawn = _gate.Angle; }
            _fxMesh.Mesh = FxMesh();
            if (_pathMode)
            {
                if (!_cursorPinned) _cursorTile = TileUnderMouse();
                _cursorMesh.Mesh = CursorMesh();
            }
            float dt = (float)delta;
            var move = Vector2.Zero;
            if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Y -= 1;
            if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Y += 1;
            if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1;
            if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;
            if (_gameCam)
            {
                // The cursor moves over the ground relative to the way the camera faces; the camera chases it. The
                // speed is the port's (the game's cursor speed is not read), 8 tiles a second.
                if (move != Vector2.Zero)
                {
                    double a = (_gcam.TargetYaw & 0xFFF) * 2 * Math.PI / 4096;
                    double fx = Math.Sin(a), fz = Math.Cos(a);          // forward, game x and z
                    double step = 8 * ParkTerrain.TileUnits * dt;
                    _gcam.CursorX = ClampX((float)(_gcam.CursorX + (fz * move.X - fx * move.Y) * step));
                    _gcam.CursorZ = ClampZ((float)(_gcam.CursorZ + (-fx * move.X - fz * move.Y) * step));
                }
                PlaceGameCamera();
                return;
            }
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
            if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.G })
            { GameCamera = !GameCamera; return; }
            // ⭐ THE PATH TOOL, master's way. A left click on a path or an unoccupied tile (buildable or not) opens the
            // tool -- and only opens it. With the tool open, one click fixes the ghost's start and the next lays the
            // run (no dragging, master's call). The right button cancels the ghost, or closes the tool when there is
            // no ghost.
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && _pathMode)
            {
                if (_runStart != null) _runStart = null;
                else { _pathMode = false; _cursorPinned = false; _cursorMesh.Mesh = null; PlaySfx(ToolSound.Finished); }
                RefreshInfo();
                return;
            }
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && _paths != null)
            {
                var tile = TileUnderMouse();            // the cursor is not tracked while the tool is closed
                if (!_pathMode)
                {
                    // The click that opens the tool only opens it.
                    if (tile is not { } t0 || !PathTool.CanStartOn(_map, t0.X, t0.Z)) return;
                    _cursorTile = tile; _pathMode = true; _cursorPinned = false; RefreshInfo();
                    return;
                }
                // Click the start, click the end: the first press fixes the ghost's start, the second lays the run.
                if (tile is { } t) _cursorTile = t;
                if (_runStart == null)
                {
                    if (_cursorTile is { } c && PathTool.CanStartOn(_map, c.X, c.Z)) { _runStart = c; PlaySfx(ToolSound.Start); }
                    else PlaySfx(ToolSound.Refused);
                }
                else if (_cursorTile is { } end)
                {
                    LayPath(_runStart.Value, end);
                    _runStart = null;
                }
                return;
            }
            if (_pathMode && e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } && _runStart != null)
            {
                _runStart = null;   // drop the ghost, keep the tool
                return;
            }
            if (_gameCam && e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Q }) { _gcam.Turn(-1); return; }
            if (_gameCam && e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.E }) { _gcam.Turn(1); return; }
            if (!_gameCam && e is InputEventMouseButton { Pressed: true } mb)
            {
                if (mb.ButtonIndex == MouseButton.WheelUp) { _distance = Mathf.Max(3, _distance * 0.9f); UpdateCamera(); }
                else if (mb.ButtonIndex == MouseButton.WheelDown) { _distance = Mathf.Min(120, _distance * 1.1f); UpdateCamera(); }
            }
            else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.T } && _ground.Mesh != null)
                _types.Visible = !_types.Visible;
            else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.B })
                _build.Visible = !_build.Visible;
            else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.O })
                _scenery.Visible = !_scenery.Visible;
            else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.P })
                ParkOpen = true;   // the gates swing open; the game never shuts them again
        }
    }
}
