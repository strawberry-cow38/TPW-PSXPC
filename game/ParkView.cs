using System;
using System.Collections.Generic;
using Godot;
using TPW.Data;
using TPW.Sim;

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
        /// <summary>Attraction placement (Tab opens the picker): the world's attractions with their names, where their
        /// models come from, and the one being placed, turned by , . or R. Placed ones stay as model instances.</summary>
        List<(AttractionDefinition Rec, string Name)> _attractions = new();
        Func<int, TPW.Data.Mesh> _attractionMesh;
        List<(GazEntry Entry, TextureSheet Sheet)> _modelSheets;
        readonly Dictionary<int, ArrayMesh> _attractionMeshes = new();
        int _placing = -1, _placeRot;
        MeshInstance3D _ghostModel, _ghostMarks;
        readonly List<MeshInstance3D> _placed = new();
        CanvasLayer _pickerLayer;
        PanelContainer _picker;
        TabContainer _pickerTabs;
        /// <summary>The picker's buttons with the attraction each stands for, so their prices can be greyed when
        /// the bank cannot pay for them.</summary>
        readonly List<(Button Button, int Index)> _pickerButtons = new();
        BuildCatalogue _catalogue;
        StringTable _catalogueNames;
        Bank _bank;
        /// <summary>The build tools' sound group (SoundGroup 7): the path tool's own sounds, and the placement tools'
        /// refusal. Group 8 holds the placement tools' other sounds (placed, cancelled, turned).</summary>
        SoundGroup _toolSounds, _parkSounds;
        /// <summary>A few voices, so two sounds the game starts together (lay, then connected) both play, as the
        /// game's voice allocator (0x800B8300) gives each its own SPU voice.</summary>
        readonly AudioStreamPlayer[] _sfx = new AudioStreamPlayer[4];
        int _sfxNext;
        readonly Dictionary<(int Group, int Sound), AudioStreamWav> _sfxStreams = new();
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
            for (int i = 0; i < _sfx.Length; i++) { _sfx[i] = new AudioStreamPlayer(); AddChild(_sfx[i]); }
            _ghostModel = new MeshInstance3D();
            AddChild(_ghostModel);
            _ghostMarks = new MeshInstance3D();
            AddChild(_ghostMarks);
            _selectionMesh = new MeshInstance3D();
            AddChild(_selectionMesh);
            _hudLayer = new CanvasLayer { Layer = 3 };
            AddChild(_hudLayer);
            _hud = new ParkHud();
            _hudLayer.AddChild(_hud);
            _pickerLayer = new CanvasLayer { Layer = 5 };
            AddChild(_pickerLayer);
            _picker = new PanelContainer { Visible = false, AnchorLeft = 1, AnchorRight = 1, AnchorBottom = 1,
                                           OffsetLeft = -420, OffsetTop = 8, OffsetRight = -8, OffsetBottom = -8 };
            _pickerLayer.AddChild(_picker);
            _pickerTabs = new TabContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            _picker.AddChild(_pickerTabs);
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

        /// <summary>The world's attractions for the picker, their models and the sheets that texture them. Call before
        /// <see cref="Load"/>, which puts their ground pads' textures in the park atlas.</summary>
        public void SetAttractions(List<(AttractionDefinition Rec, string Name)> list, Func<int, TPW.Data.Mesh> meshFor,
                                   List<(GazEntry Entry, TextureSheet Sheet)> sheets)
        {
            _attractions = list ?? new();
            _attractionMesh = meshFor;
            _modelSheets = sheets;
            _attractionMeshes.Clear();
            BuildPicker();
        }

        /// <summary>The purchase catalogue's categories and the names for their labels (<see cref="BuildCatalogue"/>).
        /// Call before <see cref="SetAttractions"/>, which lays the picker out from them.</summary>
        public void SetCatalogue(byte[] exe, StringTable names)
        {
            _catalogue = BuildCatalogue.Read(exe, AssetSelfTest.GameExecutableBase);
            _catalogueNames = names;
        }

        /// <summary>The park's bank: placing charges it (<see cref="PlaceAttraction"/>), and what it cannot pay for
        /// cannot be picked. Null leaves everything free, as the port was before.</summary>
        public void SetBank(Bank bank) { _bank = bank; RefreshPickerPrices(); }

        /// <summary>The picker, a tab per catalogue category in the game's own order, holding that category's
        /// attractions. A category the park has nothing for is left out, as the game leaves it out (0x8007D290).</summary>
        void BuildPicker()
        {
            foreach (var c in _pickerTabs.GetChildren()) c.QueueFree();
            _pickerButtons.Clear();
            var cats = _catalogue?.Categories;
            for (int c = 0; cats != null && c < cats.Count; c++)
            {
                var cat = cats[c];
                var items = new List<int>();
                for (int i = 0; i < _attractions.Count; i++) if (_attractions[i].Rec.Type == cat.Type) items.Add(i);
                if (items.Count == 0) continue;
                var scroll = new ScrollContainer
                {
                    Name = _catalogueNames?[cat.LabelId] ?? $"type {cat.Type}",
                    HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                };
                var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                scroll.AddChild(box);
                _pickerTabs.AddChild(scroll);
                foreach (int i in items)
                {
                    int k = i;
                    var (rec, name) = _attractions[i];
                    var b = new Button { Text = PickerText(rec, name), Alignment = HorizontalAlignment.Left };
                    b.Pressed += () => StartPlacing(k);
                    box.AddChild(b);
                    _pickerButtons.Add((b, k));
                }
            }
            RefreshPickerPrices();
        }

        static string PickerText(AttractionDefinition rec, string name) =>
            $"{name}  ({rec.Width}x{rec.Depth}, {HudText.Money(rec.Price)})";

        /// <summary>What the bank cannot pay for is greyed: the game works out the same thing every frame while a
        /// blueprint is up (0x8001C22C: affordable if balance − price × 10 ≥ 0).</summary>
        void RefreshPickerPrices()
        {
            foreach (var (b, k) in _pickerButtons)
                if (k >= 0 && k < _attractions.Count) b.Disabled = !CanAfford(_attractions[k].Rec);
        }

        bool CanAfford(AttractionDefinition rec) => _bank == null || _bank.Balance >= Money.FromPounds(rec.Price);

        void Charge(AttractionDefinition rec)
        {
            if (_bank == null) return;
            _bank.Spend(Money.FromPounds(rec.Price));
            RefreshPickerPrices();
        }

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
            _queue = null;
            StopPlacing();
            foreach (var m in _placed) m.QueueFree();
            _placed.Clear();
            _attractionsPlaced.Clear();
            _selection.Clear();
            _selectionMesh.Mesh = null;
            _gateBox = null; _gateRect = null;
            // The gate's hover rectangle and height (0x800F2398 + world × 5, read by the gate object's slots
            // 0x4C / 0x64 / 0x6C / 0x74: x, z, width, height in tiles, depth).
            int gr = world != null ? (int)(GateHoverRects - AssetSelfTest.GameExecutableBase) + world.Index * 5 : -1;
            if (exe != null && gr >= 0 && gr + 5 <= exe.Length)
            {
                int u = ParkTerrain.TileUnits, gx = exe[gr], gz = exe[gr + 1], gw = exe[gr + 2], gh = exe[gr + 3], gd = exe[gr + 4];
                _gateRect = (gx, gz, gw, gd);
                _gateBox = new BoxSite
                {
                    X0 = gx * u, Z0 = gz * u, W = gw * u, D = gd * u, Height = gh, Type = GateType,
                    Y0 = ParkCamera.GroundHeight(map, gx * u, gz * u), Cx = gx * u + gw * u / 2, Cz = gz * u + gd * u / 2,
                };
            }
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
                // Every attraction's ground pad, so a ride placed later has its paving in the atlas.
                foreach (var (rec, _) in _attractions)
                    foreach (var (ps, _) in rec.Pad)
                        if (ps >= 0 && ps < ground.Sprites.Count) uses.Add((ground.Sprites[ps].TPage, ground.Sprites[ps].Clut));
                // Every path and queue piece the tools can lay, and the grass the queue's undo leaves, so a tile laid
                // later has its texture in the atlas.
                _paths = exe != null && world != null ? PathTool.Create(exe, AssetSelfTest.GameExecutableBase, world.Index) : null;
                if (_paths != null)
                    foreach (int ps in _paths.AllSprites)
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
                        "\nWASD/arrows pan, Q/E turn, wheel zoom, R/F tilt, T tile types, B where you can build, O scenery, P open the park, G the game's camera, left-click the ground to lay path, Tab to place attractions";

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
                           + (_pathMode ? "\nPATH TOOL: click the start, then click the end; right button cancels the ghost, then closes the tool" : "")
                           + (_queue != null ? $"\nQUEUE TOOL ({_queue.Points.Count}/{QueueRun.MaxPoints - 1} corners): click to lay the queue toward the pointer; it goes on from its end, "
                                              + "and is done when it reaches a path or you click its end again; right button takes the last piece back, then closes; Esc closes" : "")
                           + (_placing >= 0 ? $"\nPLACING {_attractions[_placing].Name}: , . or R to turn, left button to place, right button to stop" : "");
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
            if (_cursorTile is not { } cur || _paths == null || _common == null) return new ArrayMesh();
            var run = _runStart is { } st ? PathTool.Run(st.X, st.Z, cur.X, cur.Z) : new List<(int X, int Z)> { cur };
            // The game's ghost (0x8001D9D0 → 0x800553B0): each tile of the run wears the marker sprite for the tool's
            // verdict, drawn by MarkerMesh as the game draws any marker.
            var marks = new List<AttractionPlacement.Marker>();
            foreach (var (x, z, sprite, _) in _paths.Ghost(_map, run)) marks.Add(new AttractionPlacement.Marker(x, z, sprite, 0));
            return MarkerMesh(marks);
        }
        readonly Dictionary<int, ImageTexture> _markerTex = new();

        /// <summary>Start placing attraction k of the picker's list: the path tool closes, the ghost follows the mouse.</summary>
        void StartPlacing(int k)
        {
            if (k < 0 || k >= _attractions.Count) return;
            _placing = k; _placeRot = 0;
            _pathMode = false; _runStart = null; _cursorMesh.Mesh = null; _queue = null;
            _picker.Visible = false;
            // Master: the preview is the markers alone, as the game shows it; the model appears when it is placed.
            _ghostModel.Mesh = null;
            _ghostModel.Visible = false;
            RefreshInfo();
        }

        /// <summary>Place attraction <paramref name="entry"/> with its footprint's corner at (x, z), turned. For captures.</summary>
        public bool PlaceAt(int entry, int x, int z, int rot)
        {
            int k = _attractions.FindIndex(a => a.Rec.Entry == entry);
            if (k < 0) return false;
            var rec = _attractions[k].Rec;
            AttractionPlacement.Ghost(_map, rec, x, z, rot & 3, out bool ok);
            if (!ok) return false;
            AttractionPlacement.Place(_map, rec, x, z, rot & 3);
            _paths?.LayDoors(_map, rec, x, z, rot & 3);
            Charge(rec);
            var inst = new MeshInstance3D { Mesh = AttractionMesh(entry), Transform = AttractionTransform(rec, x, z, rot & 3) };
            AddChild(inst);
            _placed.Add(inst);
            RegisterPlaced(rec, x, z, rot & 3, inst);
            RebuildGround();
            return true;
        }

        /// <summary>Place ride <paramref name="entry"/> as <see cref="PlaceAt"/> does, open its queue tool, and press at
        /// each of <paramref name="clicks"/> in turn, as the mouse would; then hold the pointer at
        /// <paramref name="hover"/> (the ghost toward it stays pinned). For captures. Returns what the last press did.</summary>
        public QueueRun.Step? QueueAt(int entry, int x, int z, int rot, IReadOnlyList<(int X, int Z)> clicks, (int X, int Z)? hover = null)
        {
            if (!PlaceAt(entry, x, z, rot)) return null;
            var rec = _attractions.Find(a => a.Rec.Entry == entry).Rec;
            StartQueue(rec, x, z, rot & 3);
            QueueRun.Step? last = null;
            foreach (var c in clicks)
            {
                if (_queue == null) break;
                if (c.X < 0) { if (_queue.Undo(_map)) RebuildGround(); continue; }   // (-1, -1): the undo
                _cursorTile = c;
                last = PressQueue(c);
            }
            if (hover is { } h) _cursorTile = h;
            if (_queue != null || _pathMode) _cursorPinned = true;
            return last;
        }

        /// <summary>Show attraction <paramref name="entry"/> as the placement ghost with its footprint's corner at
        /// (x, z), turned, instead of following the mouse. For captures.</summary>
        public void PinGhost(int entry, int x, int z, int rot)
        {
            int k = _attractions.FindIndex(a => a.Rec.Entry == entry);
            if (k < 0) return;
            StartPlacing(k);
            _placeRot = rot & 3;
            var (w, d) = _attractions[k].Rec.Footprint(_placeRot);
            _cursorTile = (x + (w >> 1), z + (d >> 1));
            _ghostPinned = true;
        }
        bool _ghostPinned;

        void StopPlacing()
        {
            _ghostPinned = false;
            _placing = -1;
            if (_ghostModel != null) { _ghostModel.Mesh = null; _ghostModel.Visible = false; }
            if (_ghostMarks != null) _ghostMarks.Mesh = null;
        }

        /// <summary>An attraction's model (sub-model 0 of its entry, posed at time 0), in tiles, z negated, and moved so
        /// its footprint's centre is the origin. ⭐ THE MODELS ARE BUILT FROM THEIR FOOTPRINT'S CORNER: every vertex of
        /// a w x d attraction lies in x 0..w·256, z 0..d·256 (Crazy Ape 57..968 by 60..965 on its 4x4; the Fries shop
        /// within 512 on its 2x2), so turning one about its centre keeps it on its turned footprint.</summary>
        ArrayMesh AttractionMesh(int entry)
        {
            if (_attractionMeshes.TryGetValue(entry, out var m)) return m;
            var def = _attractions.Find(a => a.Rec.Entry == entry).Rec;
            var mesh = _attractionMesh?.Invoke(entry);
            var centre = def == null ? Vector3.Zero : new Vector3(def.Width * ParkTerrain.TileUnits / 2f, 0, -def.Depth * ParkTerrain.TileUnits / 2f);
            // ⚠ POSED AT TIME 0, NOT THE FILE'S REST: an animated model's moving parts sit at (0, 0, 0) in the file and
            // only get their places from the animation, so the rest pose scatters them (MeshPose).
            (int X, int Y, int Z)[] posed = null;
            if (mesh?.Tracks != null) { try { posed = MeshPose.Evaluate(mesh, 0).Vertices; } catch { posed = null; } }
            m = mesh == null ? null : ModelMesh.Build(mesh, posed, _modelSheets, true, false, false, centre, 1f / ParkTerrain.TileUnits, out _);
            _attractionMeshes[entry] = m;
            // Its height for the hover box (0x80062C6C): the model's extent in y at its current frame (time 0 here).
            if (mesh != null && mesh.VertexCount > 0)
            {
                int minY = int.MaxValue, maxY = int.MinValue;
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    int y = posed != null && i < posed.Length ? posed[i].Y : mesh.Vertices[i * 3 + 1];
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
                _attractionHeights[entry] = SelectionBox.HeightTiles(minY, maxY);
            }
            return m;
        }

        /// <summary>The footprint's corner for the attraction under the mouse. 0x8001C454, run every frame with the
        /// cursor's tile, puts the corner at the cursor less half the turned footprint, rounded down (w >> 1, d >> 1):
        /// an even side has the cursor just past its middle.</summary>
        (int X, int Z)? PlacementCorner()
        {
            if (_placing < 0 || _cursorTile is not { } c) return null;
            var (w, d) = _attractions[_placing].Rec.Footprint(_placeRot);
            return (c.X - (w >> 1), c.Z - (d >> 1));
        }

        /// <summary>Where an attraction with its footprint's corner at (ox, oz) stands: the footprint's centre, at its
        /// base height (<see cref="BaseHeight"/>), turned a quarter per rotation step (the game's y turn, negated with z).</summary>
        Transform3D AttractionTransform(AttractionDefinition rec, int ox, int oz, int rot)
        {
            var (w, d) = rec.Footprint(rot);
            int cx = ox * ParkTerrain.TileUnits + w * ParkTerrain.TileUnits / 2, cz = oz * ParkTerrain.TileUnits + d * ParkTerrain.TileUnits / 2;
            float y = BaseHeight(rec, ox, oz, rot) / (float)ParkTerrain.TileUnits;
            var basis = new Basis(Vector3.Up, -rot * Mathf.Pi / 2);
            return new Transform3D(basis, new Vector3(cx / (float)ParkTerrain.TileUnits, y, -cz / (float)ParkTerrain.TileUnits));
        }

        void UpdatePlacementGhost()
        {
            if (!_ghostPinned) _cursorTile = TileUnderMouse();
            if (PlacementCorner() is not { } o) { _ghostMarks.Mesh = null; return; }
            var rec = _attractions[_placing].Rec;
            var marks = AttractionPlacement.Ghost(_map, rec, o.X, o.Z, _placeRot, out _);
            _ghostMarks.Mesh = MarkerMesh(marks);
        }

        void PlaceAttraction()
        {
            if (PlacementCorner() is not { } o) return;
            var rec = _attractions[_placing].Rec;
            AttractionPlacement.Ghost(_map, rec, o.X, o.Z, _placeRot, out bool ok);
            if (!ok || !CanAfford(rec)) { PlaySfx(ToolSound.Refused); return; }
            AttractionPlacement.Place(_map, rec, o.X, o.Z, _placeRot);
            _paths?.LayDoors(_map, rec, o.X, o.Z, _placeRot);
            var inst = new MeshInstance3D { Mesh = AttractionMesh(rec.Entry), Transform = AttractionTransform(rec, o.X, o.Z, _placeRot) };
            AddChild(inst);
            _placed.Add(inst);
            RegisterPlaced(rec, o.X, o.Z, _placeRot, inst);
            RebuildGround();
            // The placed sound. A shop or sideshow's tool then closes (0x8001C5C8 switches to tool 0; only the
            // features' tool, 15, reopens itself). A flat or tour ride's hands over to the queue tool (0x8001C92C /
            // 0x8001CAFC switch to tool 3).
            PlaySfx(PlaceSound.Placed);
            // ⭐ AND IT IS PAID FOR, right after the placed sound: 0x8001C5C8 calls 0x8001C2E0, which spends the
            // price the blueprint carries (+8, from 0x8006AD58) times ten, the game's money unit (economy.md §4.6).
            Charge(rec);
            int rot = _placeRot;
            StopPlacing();
            if (rec.IsRide) StartQueue(rec, o.X, o.Z, rot);
            RefreshInfo();
        }

        /// <summary>The queue tool, open after a ride is placed (null otherwise), and the ride it queues for.</summary>
        QueueRun _queue;
        AttractionDefinition _queueRide;
        int _queueOx, _queueOz, _queueRot;

        /// <summary>Open the queue tool for a ride just placed (0x8001DDD8): its run starts on the queue piece outside
        /// the entrance, the game camera swings round to look at the entrance, and the tool's start sound plays.</summary>
        void StartQueue(AttractionDefinition rec, int ox, int oz, int rot)
        {
            _queue = _paths?.StartQueue(rec, ox, oz, rot);
            if (_queue == null) return;
            _queueRide = rec; _queueOx = ox; _queueOz = oz; _queueRot = rot;
            _pathMode = false; _runStart = null; _cursorPinned = false;
            _cursorTile = _queue.End;
            SwingGameCamera(_queue.End, rec.EntranceTurn(rot));
            PlaySfx(ToolSound.Start);
            RefreshInfo();
        }

        /// <summary>A press of the queue tool at tile c (0x8001DF08): lays the ghost when it may be laid, else refuses.
        /// A finished queue hands over to the path tool at the ride's exit.</summary>
        QueueRun.Step PressQueue((int X, int Z) c)
        {
            var step = _queue.Lay(_map, c.X, c.Z);
            switch (step)
            {
                case QueueRun.Step.Refused: PlaySfx(ToolSound.Refused); break;
                case QueueRun.Step.Laid: RebuildGround(); PlaySfx(ToolSound.Lay); break;
                case QueueRun.Step.Finished: RebuildGround(); FinishQueue(); break;
            }
            RefreshInfo();
            return step;
        }

        /// <summary>The queue is done (0x8001DF08, for a ride just placed): the game camera moves to the tile outside
        /// the exit and turns to face it, and the path tool opens with its run already started there (0x8001D36C sees
        /// it came from the queue tool) -- its start sound, then the queue's connected sound.</summary>
        void FinishQueue()
        {
            var rec = _queueRide;
            var exit = rec?.ExitTile(_queueOx, _queueOz, _queueRot);
            CloseQueue(false);
            if (exit is { } x && _paths != null)
            {
                _pathMode = true; _cursorPinned = false;
                _runStart = x; _cursorTile = x;
                SwingGameCamera(x, rec.ExitTurn(_queueRot));
                PlaySfx(ToolSound.Start);
            }
            PlaySfx(ToolSound.Connected);
            RefreshInfo();
        }

        /// <summary>Close the queue tool, the queue laid so far staying (0x8001E178, with its sound 7 when asked).</summary>
        void CloseQueue(bool sound)
        {
            _queue = null;
            _cursorMesh.Mesh = null;
            if (sound) PlaySfx(ToolSound.Closed);
            RefreshInfo();
        }

        /// <summary>The game camera to tile t, turning to look the way <paramref name="turn"/> faces (0x8001934C moves the
        /// cursor there, 0x80053F04 sets the target yaw to turn × 0x400); it eases after both as it always does. The
        /// free camera is left alone.</summary>
        void SwingGameCamera((int X, int Z) t, int turn)
        {
            if (!_gameCam) return;
            _gcam.CursorX = t.X * ParkTerrain.TileUnits + ParkTerrain.TileUnits / 2;
            _gcam.CursorZ = t.Z * ParkTerrain.TileUnits + ParkTerrain.TileUnits / 2;
            _gcam.TargetYaw = turn * ParkCamera.QuarterTurn;
        }

        /// <summary>What the hover box needs to know of one object (SelectionBox): footprint corner and size and base
        /// height in world units, its height in tiles, its footprint's centre.</summary>
        sealed class BoxSite { public int X0, Z0, W, D, Y0, Height, Cx, Cz, Type; }
        const uint GateHoverRects = 0x800F2398;
        /// <summary>The park gate's type (its slot 0x84, 0x8006238C).</summary>
        const int GateType = 0x12;

        /// <summary>Open the picker on one of its tabs, as Tab does. For captures.</summary>
        public void ShowPicker(int tab = 0)
        {
            if (_attractions.Count == 0) return;
            _picker.Visible = true;
            if (tab >= 0 && tab < _pickerTabs.GetTabCount()) _pickerTabs.CurrentTab = tab;
        }

        /// <summary>Hold the cursor on tile (x, z) for the hover, instead of the mouse. For captures.</summary>
        public void PinHover(int x, int z) => _hoverPin = (x, z);
        (int X, int Z)? _hoverPin;

        readonly Dictionary<int, int> _attractionHeights = new();
        /// <summary>The park gate as the hover sees it: a rectangle and height per world (0x800F2398 + world × 5:
        /// x, z, width, height, depth), its base the ground at the rectangle's corner (0x800611A0).</summary>
        BoxSite _gateBox;
        (int X, int Z, int W, int D)? _gateRect;
        readonly SelectionFades<BoxSite> _selection = new();
        MeshInstance3D _selectionMesh;
        ShaderMaterial _selectionMat;

        /// <summary>An attraction's base height (the object's +0x5A): the ground under the cursor tile it was placed
        /// from, which 0x8001C454 hands it with its corner (the cursor less half the turned footprint). The model
        /// stands at it and the hover box starts from it.</summary>
        int BaseHeight(AttractionDefinition rec, int ox, int oz, int rot)
        {
            var (w, d) = rec.Footprint(rot);
            int u = ParkTerrain.TileUnits;
            return ParkCamera.GroundHeight(_map, (ox + (w >> 1)) * u + u / 2, (oz + (d >> 1)) * u + u / 2);
        }

        void RegisterPlaced(AttractionDefinition rec, int ox, int oz, int rot, MeshInstance3D inst)
        {
            var (w, d) = rec.Footprint(rot);
            int u = ParkTerrain.TileUnits;
            var box = new BoxSite
            {
                X0 = ox * u, Z0 = oz * u, W = w * u, D = d * u, Y0 = BaseHeight(rec, ox, oz, rot),
                Height = _attractionHeights.TryGetValue(rec.Entry, out int h) ? h : 0, Type = rec.Type,
            };
            box.Cx = box.X0 + box.W / 2; box.Cz = box.Z0 + box.D / 2;
            // Placed (status 0), then built: 0x80062894 zeroes the build clock, sets status 1 and takes the next
            // build variant (0x8006268C: a counter that runs 0..7 and round again, one per placement, never reset).
            var a = new PlacedAttraction { Rec = rec, Inst = inst, Rest = inst.Transform, Box = box, Ox = ox, Oz = oz, Rot = rot };
            a.Status = AttractionLifecycle.Enter(AttractionStatus.JustPlaced, a);
            a.Status = AttractionLifecycle.Enter(AttractionStatus.UnderConstruction, a);
            a.Variant = _buildVariant;
            _buildVariant = (_buildVariant + 1) & 7;
            _attractionsPlaced.Add(a);
        }

        /// <summary>A placed attraction: its model, where it stands, and its status, which it drives itself from
        /// placement to running (TPW.Sim.AttractionLifecycle). Kept in the order placed, the order the game's own
        /// list walks them to find the one under the cursor.</summary>
        sealed class PlacedAttraction : IAttractionWorld
        {
            public AttractionDefinition Rec;
            public MeshInstance3D Inst;
            public Transform3D Rest;
            public BoxSite Box;
            public int Ox, Oz, Rot;
            public AttractionStatus Status;
            /// <summary>The build animation: which of the eight rigs (A+0x6D) and its clock, in 1/4096 ticks (A+0x60).</summary>
            public int Variant, Clock;
            public bool IsRide => Rec.IsRide;
            public bool BuildAnimationComplete { get; set; }
            public int Reliability { get; set; } = 100;
            public int CyclesRun { get; set; }
            public int CyclesPerLoad => 1;
            public bool IsEmpty => true;
            public bool MechanicAssigned => false;
            public void PostMessage(int id) { }
            public void EjectEveryone() { }
            public void ClearSmoke() { }
        }
        readonly List<PlacedAttraction> _attractionsPlaced = new();
        int _buildVariant;
        Func<int, TPW.Data.Mesh> _buildRigSource;
        readonly TPW.Data.Mesh[] _buildRigs = new TPW.Data.Mesh[8];

        /// <summary>The build animation's rigs: the eight sub-models of archive entry 3, one per variant (a cube
        /// whose bone 1 carries the whole animation). Looked up when first wanted, since the models load in the
        /// background.</summary>
        public void SetBuildRig(Func<int, TPW.Data.Mesh> variantRig) { _buildRigSource = variantRig; Array.Clear(_buildRigs); }

        TPW.Data.Mesh BuildRig(int variant) => _buildRigs[variant] ??= _buildRigSource?.Invoke(variant);

        /// <summary>One park frame of every placed attraction: the status tick, and under construction the build
        /// clock first (0x800658D8). The clock gains the frame's time, at most four ticks' worth, and the build is
        /// done once it reaches the rig's length (the game reads it from the rig's record, 0x800300B8; the sub-model's
        /// first word, which is also where its keys end: 61 ticks, 81 for variant 4, about a second at 25 frames a
        /// second). The game halves the time in one display mode (0x80053D98, mode 3), which nothing in this build
        /// calls the mode setter with.</summary>
        void StepAttractions(int frameTime)
        {
            foreach (var a in _attractionsPlaced)
            {
                if (a.Status == AttractionStatus.UnderConstruction)
                {
                    a.Clock += Math.Min(frameTime, 0x4000);
                    a.BuildAnimationComplete = a.Clock >= (BuildRig(a.Variant)?.HeaderWord0 ?? 0) << 12;
                    if (a.BuildAnimationComplete) a.Clock = 0;
                }
                var next = AttractionLifecycle.Tick(a.Status, a);
                if (next != a.Status) a.Status = AttractionLifecycle.Enter(next, a);
            }
        }

        /// <summary>Draw each attraction under construction moved by its build rig (0x800659C4): the rig's bone 1,
        /// posed at the build clock's whole ticks, applied to the whole model about a pivot at (width × 128, 0,
        /// height × 128) from the model's origin (its unturned footprint's corner) -- the TURNED footprint's width
        /// (slot 0x64) and, as the game has it, the model's height in tiles (slot 0x6C) where its depth would
        /// centre it -- and then the attraction's own placement. The rigs grow the model from about a tenth of its
        /// size with a springy overshoot, some turning it and hopping it up on the way. The clock is eased between
        /// park frames here. Anything else stands at rest.</summary>
        void PoseAttractions(double sinceFrame)
        {
            int frameTime = (int)(EntranceFlags.TimeUnitsPerSecond / ParticleSystem.FramesPerSecond);
            foreach (var a in _attractionsPlaced)
            {
                if (a.Status != AttractionStatus.UnderConstruction || BuildRig(a.Variant) is not { } rig)
                {
                    if (a.Inst.Transform != a.Rest) a.Inst.Transform = a.Rest;
                    continue;
                }
                int clock = a.Clock + (int)(Math.Min(frameTime, 0x4000) * Math.Clamp(sinceFrame * ParticleSystem.FramesPerSecond, 0, 1));
                var pose = MeshPose.Evaluate(rig, clock >> 12);
                if (pose.Bones.Length < 2) { a.Inst.Transform = a.Rest; continue; }
                var (r, tx, ty, tz) = pose.Bones[1];
                var (w, _) = a.Rec.Footprint(a.Rot);
                float u = ParkTerrain.TileUnits;
                // In the game's model frame (corner at the origin, z down the map): v -> R (v - p) + t + p.
                var p = new Vector3(w * u / 2, 0, a.Box.Height * u / 2);
                var c = new Vector3(a.Rec.Width * u / 2, 0, a.Rec.Depth * u / 2);   // where the port centres the mesh
                var R = new Basis(new Vector3(r.M00, r.M10, r.M20), new Vector3(r.M01, r.M11, r.M21), new Vector3(r.M02, r.M12, r.M22));
                var gameShift = R * (c - p) + new Vector3(tx, ty, tz) + p - c;
                // Into the port's mesh frame, z negated and in tiles.
                var flip = new Basis(new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, -1));
                var local = new Transform3D(flip * R * flip, new Vector3(gameShift.X, gameShift.Y, -gameShift.Z) / u);
                a.Inst.Transform = a.Rest * local;
            }
        }

        /// <summary>The object the hover box goes round this frame, or null. As 0x80052324 picks it: with no tool
        /// open, the first attraction whose turned footprint holds the tile under the cursor; and while the park is
        /// closed, the park gate when the cursor is in its rectangle (you open the park there).</summary>
        BoxSite HoverTarget()
        {
            if (_map == null || _pathMode || _placing >= 0 || _queue != null || (_picker != null && _picker.Visible)) return null;
            if ((_hoverPin ?? TileUnderMouse()) is not { } t) return null;
            BoxSite hit = null;
            foreach (var a in _attractionsPlaced)
            {
                var (w, d) = a.Rec.Footprint(a.Rot);
                if (t.X >= a.Ox && t.X < a.Ox + w && t.Z >= a.Oz && t.Z < a.Oz + d) { hit = a.Box; break; }
            }
            if (!ParkOpen && _gateRect is { } g && _gateBox != null && t.X >= g.X && t.X < g.X + g.W && t.Z >= g.Z && t.Z < g.Z + g.D)
                hit = _gateBox;
            return hit;
        }

        /// <summary>The hover boxes, every one still fading, built as 0x8001A70C builds them and breathing with the
        /// park's time. Each band side is one-sided (the game draws it only when it faces the camera, NCLIP), so its
        /// triangles keep the game's corner order and the material culls back faces; the lid is drawn both ways.</summary>
        ArrayMesh SelectionMesh()
        {
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            int pulse = SelectionBox.Pulse((long)_parkTime);
            float u = ParkTerrain.TileUnits;
            Vector3 P(SelectionBox.Corner c) => new(c.X / u, c.Y / u, -c.Z / u);
            Color C(SelectionBox.Corner c) => new(c.R / 255f, c.G / 255f, c.B / 255f);
            void Tri(SelectionBox.Corner a, SelectionBox.Corner b, SelectionBox.Corner c)
            {
                verts.Add(P(a)); verts.Add(P(b)); verts.Add(P(c));
                cols.Add(C(a)); cols.Add(C(b)); cols.Add(C(c));
            }
            foreach (var (box, fade) in _selection.Active)
                foreach (var q in SelectionBox.Build(box.X0, box.Y0, box.Z0, box.W, box.D, box.Height, box.Cx, box.Cz, fade, pulse))
                {
                    Tri(q.V0, q.V1, q.V2); Tri(q.V1, q.V3, q.V2);
                    if (!q.Culled) { Tri(q.V0, q.V2, q.V1); Tri(q.V1, q.V2, q.V3); }
                }
            if (verts.Count == 0) return null;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = verts.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Color] = cols.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
            _selectionMat ??= new ShaderMaterial { Shader = new Shader { Code = SelectionShader } };
            mesh.SurfaceSetMaterial(0, _selectionMat);
            return mesh;
        }

        /// <summary>The GPU's additive blend (B + F, clamped) done where the GPU does it, in DISPLAY space: the box reads
        /// what is behind it and adds its colour to that. A plain additive blend adds in linear space and turns the
        /// game's pale yellow-green glow into a dull one.</summary>
        const string SelectionShader = @"shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_back;
uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
vec3 to_linear(vec3 c) {
    return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, lessThan(c, vec3(0.04045)));
}
vec3 to_display(vec3 c) {
    return mix(1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, c * 12.92, lessThan(c, vec3(0.0031308)));
}
void fragment() {
    vec3 behind = texture(screen_tex, SCREEN_UV).rgb;
    ALBEDO = to_linear(clamp(to_display(max(behind, vec3(0.0))) + COLOR.rgb, 0.0, 1.0));
    ALPHA = 1.0;
}
";

        CanvasLayer _hudLayer;
        ParkHud _hud;

        /// <summary>Give the HUD the common sheet, the executable's tables and the language's strings.</summary>
        public void SetHud(TextureSheet common, byte[] exe, StringTable strings) => _hud?.Setup(common, exe, strings);

        /// <summary>What the HUD shows of the park: the balance in pounds and the date (day and month 1-based).</summary>
        public void SetHudStatus(long pounds, int day, int month, int year)
        {
            if (_hud == null) return;
            _hud.Pounds = pounds; _hud.Day = day; _hud.Month = month; _hud.Year = year;
        }

        /// <summary>The game's number for the tool open (0x800EFD5C), for its prompts: 2 the path tool, 3 the queue
        /// tool, the placement tools by what they place (5 flat ride, 6 tour ride, 7 track ride, 11 coaster, 14 shop,
        /// 15 feature, 16 sideshow), 0 none.</summary>
        int CurrentTool()
        {
            if (_queue != null) return 3;
            if (_pathMode) return 2;
            if (_placing >= 0)
                return _attractions[_placing].Rec.Type switch { 3 => 5, 7 => 6, 6 => 7, 1 => 11, 4 => 14, 2 => 15, 5 => 16, _ => 0 };
            return 0;
        }

        /// <summary>The prompts with no tool open, set every frame from what the cursor is over (0x800396FC): Build,
        /// Laptop, Path, Hire; over an attraction, Info and OK; over the park gate, OK. (Build and Hire show only while
        /// something can still be built or hired, which in the port is always so far.)</summary>
        static int[] IdlePrompts(BoxSite hovered)
        {
            int top = HudPrompts.Build, right = HudPrompts.Laptop, bottom = HudPrompts.Path, left = HudPrompts.Hire;
            if (hovered != null)
            {
                bottom = HudPrompts.Ok;
                if (hovered.Type != GateType) right = HudPrompts.Info;
            }
            return new[] { top, right, bottom, left };
        }

        /// <summary>The queue tool's ghost: from the queue's end toward the tile under the mouse, each tile wearing the
        /// marker for its verdict (0x8001D9D0 with kind 4, drawn as every marker is).</summary>
        ArrayMesh QueueMesh()
        {
            if (_queue == null || _common == null) return new ArrayMesh();
            var c = _cursorTile ?? _queue.End;
            var marks = new List<AttractionPlacement.Marker>();
            foreach (var (x, z, sprite, _) in _queue.Ghost(_map, c.X, c.Z, out _)) marks.Add(new AttractionPlacement.Marker(x, z, sprite, 0));
            return MarkerMesh(marks);
        }

        /// <summary>The underlay every marker has (0x800553B0: common-sheet sprite 171 at shade 0x40).</summary>
        const int MarkerUnderlay = 171;

        /// <summary>Ground markers from the common sheet (the path ghost, the ride blueprint), drawn as 0x800553B0 draws
        /// them.
        ///
        /// ⭐ THEY RIPPLE (master). Per tile, two semi-transparent quads (each sprite's page decides the blend):
        /// <list type="bullet">
        /// <item>an underlay, sprite 171 at flat shade 0x40 (half brightness), on the ground, each corner pushed along x
        /// AND z by its wave value / 8;</item>
        /// <item>the marker, its corners raised off the ground by the wave value + 0x40 world units, each corner's shade
        /// 0x80 + (sin a >> 8) + (sin b >> 8), so the brightness runs across the tile rather than flashing.</item>
        /// </list>
        /// A corner's wave value is 32·(1 + sin a) + 32·(1 + sin b) (the amplitudes at 0x80102D68 / 0x80102D74), where
        /// a = (x + z)·0x200 + phase A and b = (x + z)·0x200 + phase B, x and z the corner's tile: two waves running
        /// along the diagonal. The phases advance with the park's time (0x80057DE4): A by 128·frame time / 4096, B by
        /// -64·frame time / 4096 (0x80102D6C, 0x80102D78), so they are drawn at the render rate from the park clock.
        /// Markers are turned a quarter per step (0 north, 1 west, 2 south, 3 east: the sprite's top edge on that side).</summary>
        ArrayMesh MarkerMesh(IEnumerable<AttractionPlacement.Marker> marks)
        {
            var mesh = new ArrayMesh();
            if (_common == null) return mesh;
            int phaseA = (int)(((long)(_parkTime * 128 / 4096)) & 0xFFF), phaseB = (int)(((long)(-_parkTime * 64 / 4096)) & 0xFFF);
            (int Value, int Shade) Wave(int cx, int cz)
            {
                int a = ((cx + cz) * 0x200 + phaseA) & 0xFFF, b = ((cx + cz) * 0x200 + phaseB) & 0xFFF;
                int sa = EntranceFlags.Sin(a), sb = EntranceFlags.Sin(b);
                int value = (32 * (4096 + sa) >> 12) + (32 * (4096 + sb) >> 12);
                return (value, 0x80 + (sa >> 8) + (sb >> 8));
            }
            var bySprite = new Dictionary<int, (List<Vector3> V, List<Color> C, List<Vector2> UV)>();
            (List<Vector3> V, List<Color> C, List<Vector2> UV) Bucket(int sprite)
            {
                if (!bySprite.TryGetValue(sprite, out var b)) bySprite[sprite] = b = (new List<Vector3>(), new List<Color>(), new List<Vector2>());
                return b;
            }
            float u = ParkTerrain.TileUnits;
            var uvs = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            var dim = new Color(0x40 / 255f, 0x40 / 255f, 0x40 / 255f);
            foreach (var mk in marks)
            {
                int x = mk.X, z = mk.Z;
                if (x < 0 || z < 0 || x >= _map.Width - 1 || z >= _map.Height - 1) continue;
                (int X, int Z)[] corners = { (x, z), (x + 1, z), (x, z + 1), (x + 1, z + 1) };
                var waves = new (int Value, int Shade)[4];
                var ground = new Vector3[4];
                var raised = new Vector3[4];
                for (int k = 0; k < 4; k++)
                {
                    var (cx, cz) = corners[k];
                    waves[k] = Wave(cx, cz);
                    int h = _map[cx, cz].HeightUnits, shift = waves[k].Value >> 3;
                    ground[k] = new Vector3((cx * u + shift) / u, (h + 4) / u, -(cz * u + shift) / u);
                    raised[k] = new Vector3(cx, (h + waves[k].Value + 0x40) / u, -cz);
                }
                // The underlay: the whole sprite on the tile, dim.
                var ub = Bucket(MarkerUnderlay);
                ub.V.AddRange(new[] { ground[0], ground[1], ground[2], ground[2], ground[1], ground[3] });
                ub.UV.AddRange(new[] { uvs[0], uvs[1], uvs[2], uvs[2], uvs[1], uvs[3] });
                for (int k = 0; k < 6; k++) ub.C.Add(dim);
                // The marker, turned: the UV each corner takes puts the sprite's top edge on the turn's side.
                Vector2 u0, u1, u2, u3;
                switch (mk.Turns & 3)
                {
                    case 1: u2 = uvs[0]; u0 = uvs[1]; u3 = uvs[2]; u1 = uvs[3]; break;
                    case 2: u3 = uvs[0]; u2 = uvs[1]; u1 = uvs[2]; u0 = uvs[3]; break;
                    case 3: u1 = uvs[0]; u3 = uvs[1]; u0 = uvs[2]; u2 = uvs[3]; break;
                    default: u0 = uvs[0]; u1 = uvs[1]; u2 = uvs[2]; u3 = uvs[3]; break;
                }
                var mb = Bucket(mk.Sprite);
                mb.V.AddRange(new[] { raised[0], raised[1], raised[2], raised[2], raised[1], raised[3] });
                mb.UV.AddRange(new[] { u0, u1, u2, u2, u1, u3 });
                foreach (int k in new[] { 0, 1, 2, 2, 1, 3 })
                {
                    float g = Math.Clamp(waves[k].Shade, 0, 255) / 255f;
                    mb.C.Add(new Color(g, g, g));
                }
            }
            // The underlays first, so the raised markers draw over them.
            foreach (int sprite in System.Linq.Enumerable.OrderBy(bySprite.Keys, k => k == MarkerUnderlay ? 0 : 1))
            {
                var b = bySprite[sprite];
                if (!_markerTex.TryGetValue(sprite, out var tex))
                {
                    var img = sprite < _common.Sprites.Count ? _common.RenderSprite(sprite) : null;
                    tex = img == null ? null : ImageTexture.CreateFromImage(Image.CreateFromData(img.Width, img.Height, false, Image.Format.Rgba8, img.Rgba));
                    _markerTex[sprite] = tex;
                }
                if (tex == null) continue;
                int mode = (_common.Sprites[sprite].TPage >> 5) & 3;
                foreach (bool blended in new[] { false, true })
                {
                    var arrays = new Godot.Collections.Array();
                    arrays.Resize((int)Godot.Mesh.ArrayType.Max);
                    arrays[(int)Godot.Mesh.ArrayType.Vertex] = b.V.ToArray();
                    arrays[(int)Godot.Mesh.ArrayType.Color] = b.C.ToArray();
                    arrays[(int)Godot.Mesh.ArrayType.TexUV] = b.UV.ToArray();
                    mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
                    var mat = new ShaderMaterial { Shader = PsxShading.SemiTransparentShader(mode, blended, false) };
                    mat.SetShaderParameter("atlas", tex);
                    mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, mat);
                }
            }
            return mesh;
        }

        void LayPath((int X, int Z) start, (int X, int Z) end)
        {
            var run = PathTool.Run(start.X, start.Z, end.X, end.Z);
            // ⭐ CONNECTED: the run reached its last tile and that tile was already path (or queue or track). The place
            // step flags it (0x8004DE04 sets 0x801026D0 for the last tile of those types), the piece pass then
            // reports the run finished, the tool resets and 0x8001D5C0 plays sound 3 after sound 4. Master: "03 plays
            // when a path is connected to another one successfully".
            var last = run[^1];
            bool endsOnPath = _map[last.X, last.Z].Raw0 is 2 or 4 or 10 or 13;
            int laid = _paths.Lay(_map, run);
            if (laid > 0)
            {
                RebuildGround();
                PlaySfx(ToolSound.Lay);
                if (laid == run.Count && endsOnPath) PlaySfx(ToolSound.Connected);
            }
            else PlaySfx(ToolSound.Refused);
        }

        /// <summary>The path tool's sounds in group 7, as 0x8001D5C0 plays them. The placement tools refuse with the
        /// same sound 2 (0x8001C5C8).</summary>
        public enum ToolSound { Start = 0, Refused = 2, Connected = 3, Lay = 4, Undo = 5, Closed = 7 }

        /// <summary>The placement tools' sounds in group 8, shared by every placement tool (rides, shops, sideshows,
        /// features): placed by 0x8001C5C8, cancelled by 0x8001C7E4, turned by 0x8001C6BC / 0x8001C750.</summary>
        public enum PlaceSound { Placed = 3, Cancelled = 6, Turned = 9 }

        /// <summary>Give the park view the build tools' sound group (SoundGroup.Load(…, 7)) and the group with the
        /// placement tools' sounds (SoundGroup.Load(…, 8)).</summary>
        public void SetToolSounds(SoundGroup tools, SoundGroup park = null) { _toolSounds = tools; _parkSounds = park; _sfxStreams.Clear(); }

        void PlaySfx(ToolSound which) => PlaySfx(_toolSounds, 7, (int)which);
        void PlaySfx(PlaceSound which) => PlaySfx(_parkSounds, 8, (int)which);

        /// <summary>Play sound n of a group, at the rate its record's pitch gives, full volume, centred (0x800B84AC).</summary>
        void PlaySfx(SoundGroup group, int g, int n)
        {
            if (group == null) return;
            if (!_sfxStreams.TryGetValue((g, n), out var stream))
            {
                var pcm = group.Decode(n);
                if (pcm == null || pcm.SampleCount == 0) { _sfxStreams[(g, n)] = null; return; }
                var bytes = new byte[pcm.SampleCount * 2];
                Buffer.BlockCopy(pcm.Samples, 0, bytes, 0, bytes.Length);
                stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = group.Sounds[n].SampleRate, Stereo = false, Data = bytes };
                _sfxStreams[(g, n)] = stream;
            }
            if (stream == null) return;
            var voice = _sfx[_sfxNext];
            _sfxNext = (_sfxNext + 1) % _sfx.Length;
            voice.Stream = stream;
            voice.Play();
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
            var hovered = HoverTarget();
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
                _selection.Step();
                _selection.Hover(hovered);
                StepAttractions(frameTime);
            }
            if (_gate != null && _gate.Angle != _gateAngleDrawn) { _gateMesh.Mesh = GateMesh(); _gateAngleDrawn = _gate.Angle; }
            _selectionMesh.Mesh = SelectionMesh();
            PoseAttractions(_frameClock);
            _hudLayer.Visible = Visible && _hud.CanDraw;
            if (_hud.CanDraw) _hud.SetPrompts(CurrentTool() is int tool && tool != 0 ? _hud.ToolPrompts(tool) : IdlePrompts(hovered));
            _fxMesh.Mesh = FxMesh();
            if (_pathMode)
            {
                if (!_cursorPinned) _cursorTile = TileUnderMouse();
                _cursorMesh.Mesh = CursorMesh();
            }
            else if (_queue != null)
            {
                if (!_cursorPinned) _cursorTile = TileUnderMouse() ?? _cursorTile;
                _cursorMesh.Mesh = QueueMesh();
            }
            if (_placing >= 0) UpdatePlacementGhost();
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
            if (Input.IsKeyPressed(Key.R) && _placing < 0) { _pitch = Mathf.Clamp(_pitch - dt, -1.5f, -0.2f); changed = true; }
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
            // Attraction placement: Tab opens the picker; while placing, , and . turn it a quarter each way and R turns
            // it on (master's keys), the left button places it and the right button puts it away.
            // ⭐ THE GAME'S TURN IS INSTANT: no tween, no wobble. Its turn button (0x8001C6BC / 0x8001C750) adds one to
            // the tool's quarter count (3 wraps to 0), hands it straight to the attraction (0x80063294, one byte
            // store) and plays group 8 sound 9; the blueprint is drawn from that byte the same frame (0x80064558).
            // The game only turns one way, the way . and R do; , is master's.
            if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab } && _attractions.Count > 0)
            {
                _picker.Visible = !_picker.Visible;
                if (_picker.Visible) RefreshPickerPrices();   // the bank may have moved since it was last open
                return;
            }
            if (_placing >= 0)
            {
                if (e is InputEventKey { Pressed: true, Echo: false } key && key.Keycode is Key.Comma or Key.Period or Key.R)
                {
                    _placeRot = (_placeRot + (key.Keycode == Key.Comma ? 3 : 1)) & 3;
                    PlaySfx(PlaceSound.Turned);
                    return;
                }
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) { PlaceAttraction(); return; }
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
                {
                    PlaySfx(PlaceSound.Cancelled);   // 0x8001C7E4: the attraction is dropped, the tool closes
                    StopPlacing(); RefreshInfo();
                    return;
                }
            }
            // ⭐ THE QUEUE TOOL, open after a ride is placed (the game's tool 3). The left button lays the ghost from the
            // queue's end toward the pointer when no tile of it refuses (0x8001DF08); the right button takes the last
            // segment back (the game's undo, 0x8001E114) or, with nothing to take back, closes the tool; Esc closes it
            // (the game's close, 0x8001E178). Closing keeps the queue laid so far.
            if (_queue != null)
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                {
                    if ((_cursorPinned ? _cursorTile : TileUnderMouse() ?? _cursorTile) is { } c) { _cursorTile = c; PressQueue(c); }
                    else PlaySfx(ToolSound.Refused);
                    return;
                }
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
                {
                    if (_queue.Undo(_map)) { RebuildGround(); PlaySfx(ToolSound.Undo); RefreshInfo(); }
                    else CloseQueue(true);
                    return;
                }
                if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) { CloseQueue(true); return; }
            }
            // ⭐ THE PATH TOOL, master's way. A left click on a path or an unoccupied tile (buildable or not) opens the
            // tool -- and only opens it. With the tool open, one click fixes the ghost's start and the next lays the
            // run (no dragging, master's call). The right button cancels a ghost of two tiles or more; with no ghost,
            // or a ghost of just its start tile, it closes the tool (master's call).
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && _pathMode)
            {
                bool longGhost = _runStart is { } st && _cursorTile is { } cur && PathTool.Run(st.X, st.Z, cur.X, cur.Z).Count > 1;
                if (longGhost) _runStart = null;
                else { _pathMode = false; _runStart = null; _cursorPinned = false; _cursorMesh.Mesh = null; }
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
