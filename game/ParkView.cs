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
        /// <summary>A ride's sub-model by (entry, sub), for the track pieces a coaster is drawn from.</summary>
        Func<int, int, TPW.Data.Mesh> _attractionSub;
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
        /// <summary>Group 5, the UI's own sounds — the panel and its widgets. A park loads eight groups, listed
        /// as u16 at 0x800F23CC: 1, 10, 11, 7, 2, 6, 5, 8.</summary>
        SoundGroup _uiSounds;
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
        /// <summary>The gate's swing at the last two park frames, to draw the angle between them, and its last
        /// few park frames, to tell a swing from the twitch it ends on.</summary>
        int _gateAnglePrev, _gateAngleCur, _gateRecentAt;
        readonly int[] _gateRecent = new int[8];
        /// <summary>How wide the swing's own resting cycle is (ParkGate.State: 1021 to 1024, forever). A gate whose
        /// last few frames all sit inside this is at rest as far as the console could ever show, so it is DRAWN at
        /// rest — the simulation keeps twitching, faithfully, and nobody has to watch it.</summary>
        const int GateAtRest = 4;
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
        bool _cameraDebug;
        int _cameraShout;
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
                                           OffsetLeft = -520, OffsetTop = 8, OffsetRight = -8, OffsetBottom = -8 };
            _pickerLayer.AddChild(_picker);
            _pickerTabs = new TabContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            _picker.AddChild(_pickerTabs);
            _scenery = new MeshInstance3D();
            AddChild(_scenery);
            _flags = new MeshInstance3D();
            AddChild(_flags);
            _gateMesh = new MeshInstance3D();
            AddChild(_gateMesh);
            _busMesh = new MeshInstance3D { Visible = false };
            AddChild(_busMesh);
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
                                   List<(GazEntry Entry, TextureSheet Sheet)> sheets, Func<int, int, TPW.Data.Mesh> subFor = null)
        {
            _attractions = list ?? new();
            _attractionMesh = meshFor;
            _attractionSub = subFor;
            _modelSheets = sheets;
            _attractionMeshes.Clear();
            _attractionPoses.Clear();
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

        void Charge(AttractionDefinition rec) => Charge(rec.Price);

        void Charge(int pounds)
        {
            if (_bank == null || pounds <= 0) return;
            _bank.Spend(Money.FromPounds(pounds));
            RefreshPickerPrices();
        }

        /// <param name="ground">The world's ground sheet, or null to draw the tile types alone.</param>
        /// <param name="scenery">The world's scenery pack, or null for bare ground.</param>
        /// <param name="common">The common sheet (#416), for the entrance flags; null leaves them out.</param>
        /// <param name="gatePack">The world's gate pack (ParkGate), drawn closed at the gate's base; null leaves it out.</param>
        public void Load(ParkMap map, string name, TextureSheet ground, ParkWorld world, SceneryPack scenery, TextureSheet common = null,
                         SceneryPack gatePack = null, byte[] exe = null, SceneryPack busPack = null)
        {
            // The park as the game has it once loaded: road typed, the square inside the gate laid as path (ParkPaths).
            if (exe != null && world != null) map = ParkPaths.LayStartingPaths(map, exe, AssetSelfTest.GameExecutableBase, world.Index);
            _worldIndex = world?.Index ?? -1;
            _map = map;
            _common = common;

            _guests?.Clear();
            _guests = new ParkGuests(map, this);
            _guestSprites ??= GuestSprites.From(_modelSheets);
            var cli = System.Environment.GetCommandLineArgs();
            GuestSprites.Debug = Array.IndexOf(cli, "--guest-debug") >= 0;
            _cameraDebug = Array.IndexOf(cli, "--camera-debug") >= 0;
            _guests.SetSprites(_guestSprites);
            _guests.SetBrain(GuestTargets);
            _guests.SetRideWorld(GuestTargets);
            _guests.SetRideJobs(RideJobs);
            // ⭐ THE GATE, AND IT HAS TO BE RE-WIRED ON EVERY LOAD. ParkGuests is rebuilt above, so an
            // entrance wired once at boot would be silently dropped the first time a park is opened and
            // every guest would go back to appearing inside the fence.
            if (_finances != null && !NoGate) _guests.SetEntrance(_finances, () => _bus);
            _guests.LogStaff = _logRides;
            _guests.MapChanged();
            _gate = null; _gateModel = null; _gateMesh.Mesh = null; _gateAngleDrawn = int.MinValue;
            _gateAnglePrev = _gateAngleCur = _gateRecentAt = 0;
            Array.Clear(_gateRecent);
            _fx = new ParticleSystem();
            _openingFx.Clear();
            _pathMode = false; _runStart = null; _cursorTile = null; _cursorMesh.Mesh = null; _paths = null; _cursorPinned = false;
            _queue = null;
            StopPlacing();
            foreach (var m in _placed) m.QueueFree();
            _placed.Clear();
            _attractionsPlaced.Clear();
            _track = null;
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
            BuildBus(common, busPack);
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
        /// <summary>The bus, built once: archive entry 90 is a scenery-style pack of ONE model (156 vertices,
        /// 146 faces, three tiles long), and its textures are on the COMMON sheet #416 rather than a world's
        /// ground sheet — which is why one bus serves all four worlds.
        ///
        /// ⭐ HOW THE ENTRY WAS IDENTIFIED. The call that builds the bus's model object (0x800351BC at
        /// 0x800508AC) is the same one the four gate packs go through, and those are handed 87, 86, 85 and 88 —
        /// the gate entries this port already loads per world. So the id it takes is the folio entry, and the
        /// bus is 90.</summary>
        void BuildBus(TextureSheet common, SceneryPack pack)
        {
            _busMesh.Mesh = null;
            _busMesh.Visible = false;
            _bus = null;
            if (common == null || pack == null || pack.Models.Count == 0) return;
            var model = pack.Models[0];
            var uses = new List<(ushort, ushort)>();
            foreach (var t in model.Textures) uses.Add((t.TPage, t.Clut));
            var atlas = PageAtlas.Build(common, uses);
            if (atlas?.Image == null) return;
            var mat = new ShaderMaterial { Shader = PsxShading.Shader(false) };
            // ⚠ FRONT, NOT BACK. The port negates z, which reverses winding, so a polygon the game draws
            // from one side is cull_FRONT here (SingleSidedCull). This said Shader(true) = cull_back and the
            // bus rendered INSIDE OUT — master spotted it; the near panels were culled and you saw the far
            // wheels through the body. Every other single-sided surface in the park already used the
            // constant; the bus was the one place that hardcoded the wrong sense.
            var matCull = new ShaderMaterial { Shader = PsxShading.Shader(SingleSidedCull) };
            var tex = ImageTexture.CreateFromImage(Image.CreateFromData(atlas.Image.Width, atlas.Image.Height, false,
                                                                       Image.Format.Rgba8, atlas.Image.Rgba));
            mat.SetShaderParameter("atlas", tex);
            matCull.SetShaderParameter("atlas", tex);
            var both = new Buffers(); var single = new Buffers();
            float aw = atlas.Image.Width, ah = atlas.Image.Height;
            int[] tri = { 0, 1, 2 }, quad = { 0, 1, 2, 2, 1, 3 };
            foreach (var poly in model.Polygons)
            {
                var t = model.Textures[poly.Texture];
                var b = t.DoubleSided ? both : single;
                atlas.TryOrigin(t.TPage, t.Clut, out int ox, out int oy);
                foreach (int k in poly.IsQuad ? quad : tri)
                {
                    int vi = poly.Corner(k);
                    var (vx, vy, vz) = model.Position(vi);
                    b.V.Add(new Vector3(vx / ParkTerrain.TileUnits, vy / ParkTerrain.TileUnits, -vz / ParkTerrain.TileUnits));
                    byte g = model.Vertices[vi].Shade;
                    b.C.Add(new Color(g / 255f, g / 255f, g / 255f));
                    ushort uv = poly.Uv(k);
                    b.UV.Add(new Vector2((ox + (uv & 0xFF)) / aw, (oy + (uv >> 8)) / ah));
                    b.R.Add(0); b.R.Add(0); b.R.Add(0); b.R.Add(0);
                }
            }
            _busMesh.Mesh = Surfaces(both, mat, single, matCull);
            _bus = new TPW.Sim.BusRoute();
            _busXPrev = _busXCur = _bus.WorldX;
        }

        /// <summary>Where the park draw puts the bus: (pos >> 8, 256, 1900) in world units, 0x80057CB0. Only the
        /// X moves — it runs along the road at a fixed depth, one tile up.</summary>
        void PlaceBus()
        {
            if (_bus == null) return;
            _busMesh.Visible = _bus.OnRoad;
            if (!_bus.OnRoad) return;
            float f = Mathf.Clamp((float)(_frameClock * ParticleSystem.FramesPerSecond), 0f, 1f);
            float x = Mathf.Lerp(_busXPrev, _busXCur, f) / ParkTerrain.TileUnits;
            _busMesh.Position = new Vector3(x, BusY / (float)ParkTerrain.TileUnits, -BusZ / (float)ParkTerrain.TileUnits);
        }

        /// <summary>The bus has reached the stop: its load steps off. How many is the park's own business —
        /// TPW.Sim.BusLoad works it out from what is built, and an empty park gets a bus with nobody on it,
        /// which is the point of the whole design.
        ///
        /// ⚠ ONE INPUT IS A STAND-IN AND ONE ONLY LOOKS LIKE ONE. The UPGRADE LEVEL is a stand-in: the port
        /// does not track upgrades, so every ride counts as level 0 — which is what a freshly built one IS
        /// (placement sets 0 and the upgrade path is `if level >= 3 return; level++`), so it is only wrong
        /// once upgrades exist. The LANE COUNT is 0 and that is CORRECT: `0x80059150(i)` reads
        /// `[gp+0x12F4 + i*4]`, a pair of counters zeroed at game start and incremented as a guest enters a
        /// turnstile lane (0x8005929C), so `20 − lanes` caps the bus by how many are already IN the gate.
        /// With no turnstile queue in the port there is nobody in one, and zero is the honest answer rather
        /// than a placeholder.</summary>
        void BusArrived()
        {
            if (_guests == null) return;
            var draws = new List<TPW.Sim.AttractionDraw>();
            var kinds = new HashSet<int>();
            foreach (var a in _attractionsPlaced)
            {
                // The young bonus is REAL now that the build day is recorded. ⚠ The test is the game's own
                // arithmetic, not "built today": `(age << 12) / 2024 < 4` (0x800674F8..0x80067530), which is
                // true while the age is 0 or 1 whole days. Written the long way because the obvious `age <= 1`
                // is only *incidentally* the same answer, and this is the expression that was measured.
                int age = AgeInDays(a);
                draws.Add(new TPW.Sim.AttractionDraw(a.Rec.Type, a.Level, a.Rec.BaseIntensity,
                                                     (age << 12) / 2024 < 4));
                kinds.Add(a.Rec.Entry);
            }
            int catalogue = Math.Max(1, _attractions.Count);
            int capacity = 25 + 75 * kinds.Count / catalogue;
            int score = TPW.Sim.BusLoad.ParkScore(draws, _guests.Dice);
            // ⭐ THE LANE COUNT IS REAL NOW. It was 0 while nothing queued at a turnstile, which was
            // correct then; with the gate wired, `20 - lanes` caps the load by how many are already
            // standing in one, and leaving the 0 there would let a jammed gate keep filling.
            int count = TPW.Sim.BusLoad.HeadCount(score, capacity, _guests.Count, _guests.LanesWaiting, BusDivisor);
            // ⭐ THEY GET OFF OUTSIDE. SpawnAtGate puts each one on the map's own spawn tile in state 36
            // and the turnstile walks it in; with no entrance wired it falls back to appearing INSIDE the
            // park for nothing, which is what this did before.
            for (int i = 0; i < count; i++) _guests.SpawnAtGate();
            GD.Print($"[bus] arrived: score {score} capacity {capacity} lanes {_guests.LanesWaiting} -> {count} guests");
        }

        /// <summary>[0x80102E50], the divisor the draw is scaled by.</summary>
        const int BusDivisor = 0x14000;

        /// <summary>The bus's fixed depth and height, straight out of the draw (0x80057CB0: 256 and 1900).</summary>
        public const int BusY = 256, BusZ = 1900;

        /// <summary>The park's books, handed over by Main so the turnstile can bank an entry fee. Null
        /// until it is, and the gate simply is not wired then.</summary>
        ParkFinances _finances;

        /// <summary>Can a guest standing inside the gate walk to this tile? For the build tool and the
        /// attraction panel, which want to WARN about a ride nobody can reach rather than discover it
        /// as two guests standing very still. Rebuilds the park's connectivity first, so it is safe to
        /// call straight after a placement. ⚠ A DIAGNOSTIC: the game refuses no build over this.</summary>
        public bool TileJoinedToGate(int x, int z)
        {
            if (_guests == null) return true;
            _guests.MapChanged();
            return _guests.JoinedToGate(x, z);
        }

        /// <summary>Whether a placed attraction's door and its exit are each joined to the park. False
        /// on the exit is the one that strands people: they queue and ride happily, then get put down
        /// somewhere they cannot walk out of.</summary>
        public (bool Door, bool Exit) AttractionJoined(int entry)
        {
            if (_guests == null) return (true, true);
            foreach (var t in GuestTargets())
                if (t.Id == entry) { _guests.MapChanged(); return _guests.AttractionJoined(t); }
            return (true, true);
        }

        /// <summary>--park-nogate: guests appear inside the fence and pay nothing, the way they did
        /// before the turnstile existed. A CONTROL, not a rule — it exists so the gate's effect on the
        /// bank can be measured against its own absence in the same binary.</summary>
        public bool NoGate { get; set; }

        /// <summary>Give the park its books and wire the turnstile to them. Safe before or after a load.</summary>
        public void SetFinances(ParkFinances finances)
        {
            _finances = finances;
            if (_guests != null && finances != null && !NoGate) _guests.SetEntrance(finances, () => _bus);
        }

        TPW.Sim.BusRoute _bus;
        MeshInstance3D _busMesh;
        float _busXPrev, _busXCur;

        ArrayMesh GateMesh(int angle)
        {
            var both = new Buffers(); var single = new Buffers();
            float aw = _atlas.Image.Width, ah = _atlas.Image.Height;
            int[] tri = { 0, 1, 2 }, quad = { 0, 1, 2, 2, 1, 3 };
            for (int i = 0; i < _gate.Gate.Parts.Length; i++)
            {
                var part = _gate.Part(i, angle);
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

        /// <summary>What the guests are doing, for a caption on a capture.</summary>
        public string GuestReport()
        {
            if (_guests == null) return "no guests";
            var (q, r, served) = _guests.Rides?.Totals() ?? (0, 0, 0);
            return $"guests {_guests.Count}, {q} queueing, {r} riding, {served} served; "
                 + $"{_guests.WithTarget} heading somewhere now "
            + $"({_guests.ChoseTarget} decisions made, {_guests.RouteFailed} routes failed), "
            + $"{_guests.Outstanding}/{Pathfinder.MaxRequests} searches out, "
            + $"{_guests.FreeNodes}/{Pathfinder.NodePoolSize} nodes and "
            + $"{_guests.FreeWaypoints}/{WaypointPool.Capacity} waypoints free"
            + $"\n{_guests.GateReport()}"
            + $"\n{_guests.Reachability()}"
            + $"\n{_guests.StrandedReport()}"
            + $"; map in {_guests.Areas} connected pieces, failures {_guests.RouteFailedStranded} stranded "
            + $"/ {_guests.RouteFailedSameArea} SAME AREA (this one should be 0)"
            + (_guests.StaffCount > 0 ? $", {_guests.StaffCount} staff" : "")
            + RideReport() + _guests.StaffReport();
        }

        /// <summary>Each placed ride's status, its animation clock and what it is carrying - the half of the park
        /// the guest counters cannot see, since a ride that takes people on and never lets them off looks the same
        /// as a busy one from the guest side.</summary>
        string RideReport()
        {
            var sb = new System.Text.StringBuilder();
            if (_panelFor != null)
            {
                // Until the panel is drawn, its ROWS are at least visible: the same list the drawing will lay out.
                sb.Append($"\nselected {_panelFor.Rec.Entry} ({_panelFor.Type}) level {_panelFor.Level}, seats {_panelFor.MaximumSeats}");
                foreach (var row in PanelRows(_panelFor))
                    sb.Append($"\n  [{row.Label}] {_catalogueNames?[row.Label] ?? "?"}: "
                            + (row.Slider ? $"{row.Now} [{row.Min}..{row.Max}]" : row.Value ?? "(not wired)"));
            }
            if (_contextFor != null)
            {
                sb.Append($"\ncontext {_contextFor.Rec.Entry} ({_contextFor.Type}):");
                foreach (int id in ContextCommands(_contextFor))
                    sb.Append($" [{id}] {_catalogueNames?[id] ?? "?"};");
            }
            foreach (var a in _attractionsPlaced)
            {
                int len = PhaseTicks(a);
                sb.Append($"\n  {a.Rec.Entry}: status {(int)a.Status} {a.Status}, "
                        + $"tick {(len > 0 ? a.Cycle.Accumulator >> RideCycle.FixedShift : 0)}/{len}, "
                        + $"cycle {a.CyclesRun}/{a.CyclesPerLoad}, {a.Riders}/{a.MaxSeats} aboard, "
                        + $"reliability {a.Reliability}"
                        // ⭐ THE QUEUE HEAD'S STATE IS THE WHOLE LOADING STORY. RideLoading.Load boards
                        // only a head in 18, and a head in any other state blocks the ride entirely —
                        // there is no "skip him". So "people in the queue, they just don't get on after
                        // the first group" is answered by this one field and by nothing else in the
                        // report: the rider count, the status and the queue length all look healthy.
                        + QueueHeadReport(a));
            }
            return sb.ToString();
        }

        /// <summary>The queue's length and what state its front guest is in — the two numbers that say
        /// whether a ride CAN load. See RideLoading.Load's state-18 rule.</summary>
        string QueueHeadReport(PlacedAttraction a)
        {
            if (_guests?.Rides is not { } rw || rw.RuntimeFor(a.Rec.Entry) is not { } run) return "";
            if (run.Queue.Count == 0) return ", queue empty";
            var head = run.Queue[0];
            bool boardable = head.V.State == (VisitorState)18;
            return $", queue {run.Queue.Count} head in {head.V.State}"
                 + (boardable ? " (BOARDABLE)" : " ⚠ NOT BOARDABLE — the ride cannot load past it");
        }

        void RefreshInfo()
        {
            if (_info != null && _map != null)
                _info.Text = _infoText + (_gameCam ? "\ncamera: THE GAME'S (fixed height and distance, Q/E quarter turns); G for the free camera"
                                                  : "\ncamera: free; G for the game's own")
                           + (_pathMode ? "\nPATH TOOL: click the start, then click the end; right button cancels the ghost, then closes the tool" : "")
                           + (_queue != null ? $"\nQUEUE TOOL ({_queue.Points.Count}/{QueueRun.MaxPoints - 1} corners): click to lay the queue toward the pointer; it goes on from its end, "
                                              + "and is done when it reaches a path or you click its end again; right button takes the last piece back, then closes; Esc closes" : "")
                           + (_placing >= 0 ? $"\nPLACING {_attractions[_placing].Name}: , . or R to turn, left button to place, right button to stop" : "")
                           + (_guests != null ? $"\nguests {_guests.Count}, {_guests.WithTarget} heading somewhere; "
                                              + $"pathfinder {_guests.Outstanding}/{Pathfinder.MaxRequests} searching, "
                                              + $"{_guests.FreeNodes} nodes and {_guests.FreeWaypoints} waypoints free" : "");
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
            _guests?.OnBuildItemPlaced();
            _guests?.MapChanged();
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

        /// <summary>Place a coaster or track ride and lay its track by pressing at each cursor tile in turn, as the
        /// mouse would; (-1, -1) is the undo. For captures.</summary>
        public TrackRun.Step? TrackAt(int entry, int x, int z, int rot, IReadOnlyList<(int X, int Z)> clicks, (int X, int Z)? hover = null)
        {
            if (!PlaceAt(entry, x, z, rot)) return null;
            var rec = _attractions.Find(a => a.Rec.Entry == entry).Rec;
            StartTrack(rec, x, z, rot & 3);
            TrackRun.Step? last = null;
            foreach (var c in clicks)
            {
                if (_track == null) break;
                if (c.X < 0) { if (_track.Undo(_map)) { RebuildGround(); RebuildTrackPieces(); } continue; }
                _cursorTile = c;
                last = PressTrack(c);
            }
            if (hover is { } h) _cursorTile = h;
            if (_track != null) _cursorPinned = true;
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
            var posed = PoseVertices(mesh, 0);
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

        /// <summary>The model's vertices at one whole animation tick, or null when it does not animate.</summary>
        static (int X, int Y, int Z)[] PoseVertices(TPW.Data.Mesh mesh, int tick)
        {
            if (mesh?.Tracks == null || mesh.Tracks.Count == 0) return null;
            try { return MeshPose.Evaluate(mesh, tick).Vertices; } catch { return null; }
        }

        /// <summary>An attraction's model posed at <paramref name="tick"/> of its own animation, cached per tick.
        ///
        /// ⭐ A RUNNING RIDE IS ITS ANIMATION. The model's first halfword is both the phase length the cycle clock
        /// races (TPW.Sim.RideCycle) and the end of its keys, so the clock's whole ticks index the animation
        /// directly -- the ride is on screen doing exactly what the sim says it is doing. Measured across the
        /// archive: 60 of the 94 animated entries move vertices, the Crazy Ape 98 of its 189 by up to 1054 units
        /// (four tiles), so this is the difference between a ride and a statue.
        ///
        /// The poses are whole ticks and there are at most a model's length of them (41 for the Crazy Ape, 801 for
        /// Zero G), so they are built once each and kept rather than rebuilt per frame.</summary>
        ArrayMesh AttractionMeshAt(int entry, int tick)
        {
            if (tick <= 0) return AttractionMesh(entry);
            if (_attractionPoses.TryGetValue((entry, tick), out var m)) return m;
            var mesh = _attractionMesh?.Invoke(entry);
            var posed = PoseVertices(mesh, tick);
            if (mesh == null || posed == null) return AttractionMesh(entry);
            var def = _attractions.Find(a => a.Rec.Entry == entry).Rec;
            var centre = def == null ? Vector3.Zero : new Vector3(def.Width * ParkTerrain.TileUnits / 2f, 0, -def.Depth * ParkTerrain.TileUnits / 2f);
            m = ModelMesh.Build(mesh, posed, _modelSheets, true, false, false, centre, 1f / ParkTerrain.TileUnits, out _);
            // One park's rides cannot reach this; a browse that walked every model could.
            if (_attractionPoses.Count > 4096) _attractionPoses.Clear();
            _attractionPoses[(entry, tick)] = m;
            return m;
        }
        readonly Dictionary<(int Entry, int Tick), ArrayMesh> _attractionPoses = new();

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
            // 0x800EBBE4: putting a build item down wipes both pathfinder pools and drops every search.
            _guests?.OnBuildItemPlaced();
            _guests?.MapChanged();
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
            // ⭐ STAMPING (master's, not the game's): hold SHIFT and the tool stays on the cursor so the next
            // click places another one. Each stamp is charged and validated exactly like a single placement —
            // the only thing skipped is the tool closing and its hand-over.
            // ⚠ SO A RIDE STAMPED THIS WAY GETS NO QUEUE AND NO TRACK. The game hands a placed ride straight
            // to the queue tool and a coaster to the track builder, and a stamp cannot do that for each copy;
            // the hand-over happens for the LAST one, when shift comes off. Placing rides in bulk and building
            // their queues afterwards is the trade.
            if (Input.IsKeyPressed(Key.Shift)) { RefreshInfo(); return; }
            StopPlacing();
            // A coaster (type 1) or a track ride (6) hands over to a track builder; everything else with a queue
            // hands over to the queue tool (findings/rides.md §7b).
            if (rec.Type is 1 or 6) StartTrack(rec, o.X, o.Z, rot);
            else if (rec.IsRide) StartQueue(rec, o.X, o.Z, rot);
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

        /// <summary>The track builder, open after a coaster or a track ride is placed (the game's tools 12 and 8,
        /// findings/rides.md §7b). Its shape is the queue tool's: press to lay a segment, right button to take the
        /// last one back, and it ends itself when the track closes on the station.</summary>
        void StartTrack(AttractionDefinition rec, int ox, int oz, int rot)
        {
            if (_map == null || _worldIndex < 0) return;
            _track = new TrackRun(rec, ox, oz, rot, _worldIndex);
            _queue = null; _pathMode = false; _runStart = null; _cursorPinned = false;
            _cursorTile = _track.Start;
            SwingGameCamera(_track.Start, rot);
            PlaySfx(ToolSound.Start);
            RefreshInfo();
        }

        TrackRun.Step PressTrack((int X, int Z) c)
        {
            if (_bank != null && _bank.Balance.Pounds < TrackRun.PiecePrice[_track.World])
            { PlaySfx(ToolSound.Refused); return TrackRun.Step.Refused; }
            var step = _track.Lay(_map, c.X, c.Z);
            switch (step)
            {
                case TrackRun.Step.Refused: PlaySfx(ToolSound.Refused); break;
                case TrackRun.Step.Placed:
                    Charge(TrackRun.PiecePrice[_track.World]);
                    RebuildGround(); RebuildTrackPieces(); PlaySfx(ToolSound.Lay); RefreshInfo();
                    break;
                case TrackRun.Step.Closed:
                    Charge(TrackRun.PiecePrice[_track.World]);
                    RebuildGround(); RebuildTrackPieces(); PlaySfx(ToolSound.Lay); PlaySfx(ToolSound.Connected);
                    CloseTrack(false);
                    break;
            }
            return step;
        }

        /// <summary>Put the builder away. The track stays as it is: an unclosed one is a ride nobody can queue for,
        /// which is the game's own rule rather than a reason to refuse the tool.</summary>
        void CloseTrack(bool sound)
        {
            if (_track == null) return;
            if (sound) PlaySfx(ToolSound.Closed);
            _track = null; _cursorPinned = false; _cursorMesh.Mesh = null;
            RefreshInfo();
        }

        TrackRun _track;
        /// <summary>Which world's park is loaded, for the track price (TrackRun.PiecePrice).</summary>
        int _worldIndex = -1;

        /// <summary>The laid track, drawn from the ride's own pieces: a support at every pylon and the flat piece
        /// along every tile between them (TrackRun.PickPieces picks which sub-models those are).
        ///
        /// ⚠ The port arranges them; the game's own arrangement — which piece for a corner, for a slope, and how
        /// it banks — is the table this has not found yet (TrackRun.Pieces).</summary>
        void RebuildTrackPieces()
        {
            _trackPieces ??= AddNode(new Node3D());
            foreach (var c in _trackPieces.GetChildren()) c.QueueFree();
            if (_track == null || _attractionSub == null) return;
            var bounds = new List<(int W, int H, int D, int Verts)>();
            for (int sub = 0; ; sub++)
            {
                var m = _attractionSub(_track.Ride.Entry, sub);
                if (m == null) break;
                int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
                for (int v = 0; v < m.VertexCount; v++)
                {
                    int vx = m.Vertices[v * 3], vy = m.Vertices[v * 3 + 1], vz = m.Vertices[v * 3 + 2];
                    x0 = Math.Min(x0, vx); x1 = Math.Max(x1, vx);
                    y0 = Math.Min(y0, vy); y1 = Math.Max(y1, vy);
                    z0 = Math.Min(z0, vz); z1 = Math.Max(z1, vz);
                }
                bounds.Add(m.VertexCount == 0 ? (0, 0, 0, 0) : (x1 - x0, y1 - y0, z1 - z0, m.VertexCount));
                if (sub > 40) break;
            }
            var pieces = TrackRun.PickPieces(bounds);
            GD.Print($"[track] entry {_track.Ride.Entry} subs {bounds.Count} straight {pieces.Straight} support {pieces.Support} column {pieces.Column} pylons {_track.Pylons.Count}");
            if (!pieces.Any) return;
            int u = ParkTerrain.TileUnits;
            float colH = pieces.Column >= 0 ? bounds[pieces.Column].H / (float)u : 1f;

            // ⭐ THE TRACK IS LEVEL AND THE PYLONS MAKE UP THE DIFFERENCE. Master, on the real game: "since the
            // pylons can vary in height, rotation, etc i think the pylons are a combination of multiple parts."
            // So the deck sits one tile above the HIGHEST ground the run crosses and every tile stacks the ride's
            // own block up to it — which is what its one-tile cube is for. ⚠ The port chooses the deck height;
            // the game's own rule for it is in the piece table it has not finished reading (TrackRun.Pieces).
            var tiles = new List<(int X, int Z, bool Pylon)>();
            var last = _track.Start;
            foreach (var p in _track.Pylons)
            {
                foreach (var (x, z) in TrackRun.Span(last, p)) tiles.Add((x, z, false));
                tiles.Add((p.X, p.Z, true));
                last = p;
            }
            int high = 0;
            foreach (var (x, z, _) in tiles) high = Math.Max(high, ParkCamera.GroundHeight(_map, x * u + u / 2, z * u + u / 2));
            float deck = high / (float)u + 1f;
            foreach (var (x, z, pylon) in tiles)
            {
                // Only a PYLON stands on the ground. The track between two of them is held up by them, which is
                // the whole shape of the tool: a column under every tile would be a wall, not a coaster.
                if (pylon)
                {
                    float ground = ParkCamera.GroundHeight(_map, x * u + u / 2, z * u + u / 2) / (float)u;
                    for (float y = ground; y < deck - 0.01f; y += colH)
                        AddTrackPiece(y + colH >= deck - 0.01f ? pieces.Support : pieces.Column, x, z, Math.Min(y, deck - colH));
                }
                AddTrackPiece(pieces.Straight, x, z, deck);
            }
            GD.Print($"[track] drew {_trackPieces.GetChildCount()} pieces on a deck at {deck:0.00}");
        }

        void AddTrackPiece(int sub, int x, int z, float y)
        {
            if (sub < 0) return;
            int key = _track.Ride.Entry * 64 + sub;
            if (!_trackMeshes.TryGetValue(key, out var mesh))
            {
                var m = _attractionSub(_track.Ride.Entry, sub);
                if (m == null) { _trackMeshes[key] = null; return; }
                int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue, y0 = int.MaxValue;
                for (int v = 0; v < m.VertexCount; v++)
                {
                    int vx = m.Vertices[v * 3], vy = m.Vertices[v * 3 + 1], vz = m.Vertices[v * 3 + 2];
                    x0 = Math.Min(x0, vx); x1 = Math.Max(x1, vx); z0 = Math.Min(z0, vz); z1 = Math.Max(z1, vz);
                    y0 = Math.Min(y0, vy);
                }
                var centre = new Vector3((x0 + x1) / 2f, y0, -(z0 + z1) / 2f);
                mesh = ModelMesh.Build(m, null, _modelSheets, true, false, false, centre, 1f / ParkTerrain.TileUnits, out _);
                _trackMeshes[key] = mesh;
            }
            if (mesh == null) return;
            _trackPieces.AddChild(new MeshInstance3D
            {
                Mesh = mesh,
                Position = new Vector3(x + 0.5f, y, -(z + 0.5f)),
            });
        }

        readonly Dictionary<int, ArrayMesh> _trackMeshes = new();
        Node3D _trackPieces;

        T AddNode<T>(T node) where T : Node { AddChild(node); return node; }

        /// <summary>The builder's ghost: the pylon under the cursor and the track it would assemble from the last
        /// one, in ground markers.</summary>
        ArrayMesh TrackMesh()
        {
            if (_track == null || _common == null || _paths == null) return new ArrayMesh();
            var c = _cursorTile ?? _track.End;
            var marks = new List<AttractionPlacement.Marker>();
            foreach (var (x, z, sprite, _) in _track.Ghost(_paths, _map, c.X, c.Z, out _))
                marks.Add(new AttractionPlacement.Marker(x, z, sprite, 0));
            return MarkerMesh(marks);
        }

        /// <summary>A press of the queue tool at tile c (0x8001DF08): lays the ghost when it may be laid, else refuses.
        /// A finished queue hands over to the path tool at the ride's exit.</summary>
        QueueRun.Step PressQueue((int X, int Z) c)
        {
            // A queue tile costs more than a path one (PathTool.QueueTileCost). The segment about to be laid runs
            // from the queue's end to the snapped cursor, and its new tiles are the ones past that corner.
            int cost = _queue.GhostCost(_map, c.X, c.Z);
            if (_bank != null && cost > _bank.Balance.Pounds) { PlaySfx(ToolSound.Refused); return QueueRun.Step.Refused; }
            var step = _queue.Lay(_map, c.X, c.Z);
            if (step != QueueRun.Step.Refused) Charge(cost);
            switch (step)
            {
                case QueueRun.Step.Refused: PlaySfx(ToolSound.Refused); break;
                // Laying queue changes what the guests can walk on and how long the queue is.
                case QueueRun.Step.Laid: RebuildGround(); _guests?.MapChanged(); PlaySfx(ToolSound.Lay); break;
                case QueueRun.Step.Finished: RebuildGround(); _guests?.MapChanged(); FinishQueue(); break;
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

        /// <summary>Move a slider on the selected attraction, as the panel's widget will. Returns what the
        /// value actually became, which is NOT always what was asked: RidePanel.Apply clamps to the range of
        /// the ride's current level, and a coaster's duration range is 1..1 on the disc, so its slider exists
        /// and cannot move.
        ///
        /// ⚠ THIS IS THE WRITING HALF AND IT REACHES LIVE STATE. Speed and duration are read every tick by the
        /// wear and cycle code, so moving a slider changes how fast a ride wears and how long a cycle takes —
        /// this is the first thing in the port that can make a park worse. Proven headlessly before any pixels
        /// exist, on purpose.</summary>
        public (int Speed, int Duration)? MoveSlider(string which, int value)
        {
            if (_panelFor == null) return null;
            int speed = _panelFor.SpeedSlider, duration = _panelFor.CyclesPerLoad, capacity = _panelFor.Capacity;
            switch (which)
            {
                case "speed": speed = value; break;
                case "duration": duration = value; break;
                case "capacity": capacity = value; break;
                default: return null;
            }
            TPW.Sim.RidePanel.Apply(_panelFor, speed, capacity, duration);
            PlaySfx(UiSound.SliderMoved);
            RefreshInfo();
            return (_panelFor.SpeedSlider, _panelFor.CyclesPerLoad);
        }

        /// <summary>Open the context list on the attraction at a tile, as the right button does, at a fixed
        /// place on screen. For captures — a right-click cannot be simulated headlessly and I am not shipping
        /// a menu I have not seen.</summary>
        public bool ShowContext(int x, int z, Vector2 at)
        {
            _contextFor = AttractionAt((x, z));
            _contextAt = _contextFor != null ? at : null;
            if (_contextFor != null) PlaySfx(UiSound.ContextOpened);
            RefreshInfo();
            return _contextFor != null;
        }

        /// <summary>Select the attraction on a tile, as a click on it does. For captures, so the panel can be
        /// driven headlessly the way every other tool in this port is.</summary>
        public bool SelectAttraction(int x, int z)
        {
            _panelFor = AttractionAt((x, z));
            if (_panelFor != null) PlaySfx(UiSound.PanelOpened);
            RefreshInfo();
            return _panelFor != null;
        }
        (int X, int Z)? _hoverPin;
        /// <summary>Extra tiles held as hovered besides <see cref="PinHover"/>'s, so a capture can put two boxes up
        /// at once (the game keeps three: SelectionFades).</summary>
        public void PinHoverAlso(int x, int z) => _hoverAlso.Add((x, z));
        readonly List<(int X, int Z)> _hoverAlso = new();

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
            var a = new PlacedAttraction
            {
                Rec = rec, Inst = inst, Rest = inst.Transform, Box = box, Ox = ox, Oz = oz, Rot = rot,
                RunLength = () => _attractionMesh?.Invoke(rec.Entry)?.HeaderWord0 ?? 0,
                RiderCount = () => _guests?.Rides?.RuntimeFor(rec.Entry)?.Riders.Count ?? 0,
            };
            a.BuildLength = () => BuildRig(a.Variant)?.HeaderWord0 ?? 0;
            // ⭐ RUN THE GAME'S OWN PLACEMENT INITIALISER. It sets level 0, full reliability, the lifetime from
            // the record, and the three sliders to the record's defaults — capacity to half the seats, speed to
            // the MIDDLE of the level's range, duration to half its maximum. The port had never called it, so a
            // placed ride started on hardcoded values: capacity 0, below its own minimum of 1, and a speed of 50
            // that happened to match only because most ranges are 1..100.
            if (a.IsRide) TPW.Sim.RidePanel.Place(a);
            // Read once, at placement (0x8009F524) - and never again, which is the point of it.
            a.Wear.Lifetime = rec.Levels.Length > 0 ? rec.Level0.Lifetime : 0;
            a.Status = AttractionLifecycle.Enter(AttractionStatus.JustPlaced, a);
            a.Status = AttractionLifecycle.Enter(AttractionStatus.UnderConstruction, a);
            a.BuiltOnDay = ParkDay;
            a.Variant = _buildVariant;
            _buildVariant = (_buildVariant + 1) & 7;
            _attractionsPlaced.Add(a);
        }

        /// <summary>A placed attraction: its model, where it stands, and its status, which it drives itself from
        /// placement to running (TPW.Sim.AttractionLifecycle). Kept in the order placed, the order the game's own
        /// list walks them to find the one under the cursor.</summary>
        sealed class PlacedAttraction : IAttractionWorld, IRideAnimation, IRideWearWorld, IRideJob, IRidePanelWorld
        {
            // --- IRideJob: the same object as a mechanic acts on it ---------------------------------
            int IRideJob.Id => Rec.Entry;
            AttractionStatus IRideJob.Status { get => Status; set => Status = AttractionLifecycle.Enter(value, this); }
            int IRideJob.Lifetime => Wear.Lifetime;
            int IRideJob.CentreX { get { var (w, _) = Rec.Footprint(Rot); return Ox + w / 2; } }
            int IRideJob.CentreZ { get { var (_, d) = Rec.Footprint(Rot); return Oz + d / 2; } }
            /// <summary>Where a mechanic walks. ⚠ NOT THE ENTRANCE TILE, and that is READ rather than a
            /// preference: the mechanic's own request uses flags 0x11 — path and queue — while reaching
            /// an ENTRANCE tile (type 7) needs 0x18 (behaviour.md's state 23 uses exactly that). So the
            /// entrance is not reachable on the mechanic's own flags, and aiming at it made every claim
            /// end in a failed path and a released ride. The port aims at a walkable tile touching the
            /// footprint instead; what the game asks the object for is its slot 42, not followed.</summary>
            public int JobDoorX, JobDoorZ;
            int IRideJob.DoorX => JobDoorX;
            int IRideJob.DoorZ => JobDoorZ;
            int IRideJob.FootprintSpan { get { var (w, d) = Rec.Footprint(Rot); return w + d; } }
            /// <summary>A+0xEC. Its own field because nothing else in the port has one; the ride's own
            /// machine never touches it, only the mechanic closing the ride does.</summary>
            public int Closing;
            int IRideJob.Closing { get => Closing; set => Closing = value; }
            public StaffMember MechanicClaim;
            StaffMember IRideJob.Claim { get => MechanicClaim; set => MechanicClaim = value; }
            /// <summary>⚠ NOT WIRED: the paid level-up (RidePanel.CompleteUpgrade) needs the panel and
            /// the bank, and nothing in the port queues an upgrade, so nothing can reach this.</summary>
            void IRideJob.CompleteUpgrade() { }

            public AttractionDefinition Rec;
            public MeshInstance3D Inst;
            public Transform3D Rest;
            public BoxSite Box;
            public int Ox, Oz, Rot;

            /// <summary>The park day this was built, so the ride knows its own age. The game keeps it at
            /// A+0xF4 and reads it back through 0x8009EDBC as `totalDays - that`, in DAYS; the Details page
            /// then divides by 365 for the years it shows (0x80079520's magic-number divide), and the park
            /// draw score asks the same getter for its young-ride bonus. One field, both readers.</summary>
            public int BuiltOnDay;
            public AttractionStatus Status;
            /// <summary>Which of the eight build rigs this one uses (A+0x6D).</summary>
            public int Variant;
            /// <summary>A+0xF8: the day the queue last had somebody in it. The part-load timeout
            /// measures how long it has been EMPTY, not how long loading has taken.</summary>
            public int QueueEmptySince;
            /// <summary>The running model's own phase length, and the build rig's: both are the mesh's
            /// first halfword, which is what the animation descriptor's +0x38 is copied from
            /// (0x8002C5FC → 0x8002C604, rides.md §0 item 7). Supplied by the view, which owns the
            /// model cache.</summary>
            public Func<int> RunLength, BuildLength;

            /// <summary>The animation clock that decides when a phase ends, and with it the end of both
            /// construction and a run (0x800658D8).</summary>
            public readonly RideCycle Cycle = new();
            /// <summary>Reliability, lifetime and the condemned mechanic (rides.md §6).</summary>
            public readonly RideWear Wear = new();

            /// <summary>The speed slider, A+0xB8. 50 is the DEFAULT rides.md §8 records on a placed
            /// Crazy Ape; the range comes from the record's CURRENT level.</summary>
            public int SpeedSlider { get; set; } = 50;

            /// <summary>The upgrade level, 0 to 2, and it is REAL now rather than assumed. ⭐ Placement sets
            /// ZERO (0x8009C344) and the upgrade path is `if level >= 3 return; level++` (0x8009C56C), so a
            /// freshly built ride is level 0 and everything the record says about it comes from the level-0
            /// block. This class used to read Level0 unconditionally, which was right only until the first
            /// upgrade; the panel is what moves it.</summary>
            public int Level { get; set; }

            /// <summary>The record's block for a level, as the panel wants it. Clamped, because the game's
            /// own upgrade path allows a fourth level the data does not have (rides.md §1.3).</summary>
            public TPW.Sim.RidePanelLevel ReadLevel(int level)
            {
                if (Rec.Levels.Length == 0) return default;
                var l = Rec.Levels[Math.Clamp(level, 0, Rec.Levels.Length - 1)];
                return new TPW.Sim.RidePanelLevel(l.WearMultiplier, l.MaxSeats, l.Lifetime,
                                                  l.SpeedMin, l.SpeedMax, l.CyclesMin, l.CyclesMax, l.Price);
            }

            /// <summary>The record's block for the level this ride is actually at.</summary>
            RideLevel Now => Rec.Levels.Length > 0 ? Rec.Levels[Math.Clamp(Level, 0, Rec.Levels.Length - 1)] : default;

            // --- IRidePanelWorld ------------------------------------------------------------------------
            public AttractionType Type => (AttractionType)Rec.Type;
            /// <summary>⚠ Slot 99, and NOT the same as Capacity: a coaster overrides it with its model's
            /// attachment count (0x800AD728). Until coaster trains exist, the level's seats.</summary>
            public int MaximumSeats => MaxSeats;
            public int BaseIntensity => Rec.BaseIntensity;
            public int Capacity { get; set; }
            public int ReliabilityFixed { get => Wear.Reliability; set => Wear.Reliability = value; }
            public int Lifetime { get => Wear.Lifetime; set => Wear.Lifetime = value; }
            public int ClosingProgress { get; set; }
            /// <summary>⚠ NOT WIRED: the port has no smoke or sparkle handles to release yet, so this is
            /// deliberately empty rather than pretending. When ride effects exist, both handles are freed
            /// and zeroed here (0x8009C428 / 0x8009C434).</summary>
            public void ClearRideEffects() { }

            public bool IsRide => Rec.IsRide;
            public bool BuildAnimationComplete { get; set; }

            // ⚠ IAttractionWorld WANTS POINTS, RideWear KEEPS 20.12. One of the two has to convert and
            // it is cheaper to do it here than to widen the lifecycle's interface, which is about
            // statuses and does not otherwise care about fixed point.
            public int Reliability
            {
                get => Wear.ReliabilityPoints;
                set => Wear.Reliability = value << 12;
            }

            public int CyclesRun { get => Cycle.CyclesRun; set => Cycle.CyclesRun = value; }
            /// <summary>The duration slider. Until the panel moves it, the game's own default: half the
            /// CURRENT level's maximum, so 5 for most rides and 25 for a bouncer (rides.md §4.3). Setting it
            /// is what the panel does; −1 means "still the default", so an upgrade that widens the range
            /// carries the ride with it rather than pinning it to the old level's half.</summary>
            int _cyclesPerLoad = -1;
            public int CyclesPerLoad
            {
                get => _cyclesPerLoad >= 0 ? _cyclesPerLoad
                     : Rec.Levels.Length > 0 ? Now.DefaultCycles : 1;
                set => _cyclesPerLoad = value;
            }
            /// <summary>Riders on board, from the ride's own runtime (ParkRideWorld). ⚠ THIS WAS THE
            /// STUB THAT ATE THE RIDERS: while it answered a constant "empty", status 11 left for 10
            /// on its FIRST tick, so at most one guest ever stepped off per unload and the rest stayed
            /// aboard for good. Unloading ends when the ride is empty, and only the real count knows.</summary>
            public Func<int> RiderCount;
            public bool IsEmpty => (RiderCount?.Invoke() ?? 0) <= 0;
            public bool MechanicAssigned => false;
            public void PostMessage(int id) { }
            public void EjectEveryone() { }
            public void ClearSmoke() { }

            // IRideAnimation: a flat ride has one mesh and therefore one phase (astra's phase table).
            public int CurrentPhase => 0;
            public int PhaseLength(int phase) => RunLength?.Invoke() ?? 0;
            public int BuildAnimationLength => BuildLength?.Invoke() ?? 0;

            // IRideWearWorld
            public int Riders => RiderCount?.Invoke() ?? 0;
            public int MaxSeats => Rec.Levels.Length > 0 ? Math.Max(1, Now.MaxSeats) : 1;
            public int WearMultiplier => Rec.Levels.Length > 0 ? Now.WearMultiplier : 5;
            public bool NoWear => false;
        }
        readonly List<PlacedAttraction> _attractionsPlaced = new();

        /// <summary>The park's guests. Null until a map is loaded.</summary>
        ParkGuests _guests;
        /// <summary>The people sheet the guests are drawn from, baked once (GuestSprites).</summary>
        GuestSprites _guestSprites;
        readonly List<GuestTarget> _guestTargets = new();
        /// <summary>Park ticks since the map loaded: the clock the loading cadences count on.</summary>
        long _clockTicks;

        /// <summary>Days since the park opened — the port's McAi+0x10.</summary>
        int ParkDay => (int)(_clockTicks / TPW.Sim.ParkClock.TicksPerDay);

        /// <summary>A ride's age in DAYS, which is the unit both readers want.</summary>
        int AgeInDays(PlacedAttraction a) => Math.Max(0, ParkDay - a.BuiltOnDay);

        /// <summary>The placed attractions as the guests' decision reads them (see GuestBrain).
        ///
        /// ⚠ SLOT 54 IS ZERO FOR EVERY RIDE and that is READ, not a placeholder (rides.md §0 item 1):
        /// only a FEATURE marked usable in its record ever contributes the desire terms. Getting this
        /// backwards would make every ride score as though guests had a need for it.</summary>
        /// <summary>A walkable tile touching a footprint, for the things that have no entrance tile -
        /// shops, features and sideshows (rides.md §2: their entrance/exit offsets are (-1,-1)).
        ///
        /// ⚠ THE PORT'S CHOICE, NOT THE GAME'S. 0x8009F614 asks the attraction for its entrance point
        /// and the object answers through a vtable slot this has not followed. What is READ is that the
        /// search runs with flags 0x11 (path and queue, NOT footprint), so a target ON the footprint
        /// cannot be reached at all — and that is what the port was asking for: a feature's "door" was
        /// its own centre tile, which no walker can stand on. Guests and staff were both failing to
        /// reach every shop and every feature in the park because of it.
        ///
        /// Walks the footprint's four sides and takes the first walkable tile, in a fixed order so two
        /// runs agree. Nearest-by-distance would be a different arbitrary rule, not a better one.</summary>
        (int X, int Z)? WalkableBeside(int ox, int oz, int w, int d, bool allowEntrance = true)
        {
            if (_map == null) return null;
            // ⚠ AN ENTRANCE TILE IS "WALKABLE" AND STILL NOT REACHABLE. ParkMap.IsWalkable counts type 7
            // because guests stand on one to queue — but they get there with flags 0x18, and anything
            // walking on the plain 0x11 (path and queue) cannot enter it. Handing an entrance tile to a
            // 0x11 search is a route that always fails, which is exactly what it did to every mechanic
            // claim until this was separated.
            bool Ok(int x, int z) => x >= 0 && z >= 0 && x < _map.Width && z < _map.Height
                && _map[x, z].IsWalkable
                && (allowEntrance || _map[x, z].Type != TileType.AttractionEntrance);
            for (int x = ox; x < ox + w; x++)
            {
                if (Ok(x, oz - 1)) return (x, oz - 1);
                if (Ok(x, oz + d)) return (x, oz + d);
            }
            for (int z = oz; z < oz + d; z++)
            {
                if (Ok(ox - 1, z)) return (ox - 1, z);
                if (Ok(ox + w, z)) return (ox + w, z);
            }
            return null;
        }

        /// <summary>The placed rides as a mechanic acts on them. Live handles, not a snapshot: the
        /// claim, the status and the closing progress are all written back through them.</summary>
        IReadOnlyList<IRideJob> RideJobs()
        {
            _rideJobs.Clear();
            foreach (var a in _attractionsPlaced)
            {
                if (!a.IsRide) continue;
                var (w, d) = a.Rec.Footprint(a.Rot);
                // ⭐ THE ENTRANCE TILE IS THE RIGHT TARGET AFTER ALL. Type 7 is enterable as a
                // DESTINATION whatever the flags say (Pathfinder's own case 7) — only the link test
                // applies — so 0x11 reaches it. A tile merely beside the footprint is usually not a
                // path at all: a ride's only pedestrian access is its entrance and its exit.
                var door = a.Rec.EntranceTile(a.Ox, a.Oz, a.Rot)
                        ?? WalkableBeside(a.Ox, a.Oz, w, d, allowEntrance: false)
                        ?? (a.Ox + w / 2, a.Oz + d / 2);
                a.JobDoorX = door.Item1; a.JobDoorZ = door.Item2;
                _rideJobs.Add(a);
            }
            return _rideJobs;
        }
        readonly List<IRideJob> _rideJobs = new();

        IReadOnlyList<GuestTarget> GuestTargets()
        {
            _guestTargets.Clear();
            foreach (var a in _attractionsPlaced)
            {
                var (w, d) = a.Rec.Footprint(a.Rot);
                int cx = a.Ox + w / 2, cz = a.Oz + d / 2;
                var door = a.Rec.EntranceTile(a.Ox, a.Oz, a.Rot) ?? WalkableBeside(a.Ox, a.Oz, w, d) ?? (cx, cz);
                _guestTargets.Add(new GuestTarget
                {
                    Id = a.Rec.Entry,
                    TypeIndex = a.Rec.Type,
                    Intensity = a.Rec.BaseIntensity,
                    Usable = a.Rec.Type == 2 && a.Rec.UsableByGuests ? 1 : 0,
                    Open = AttractionLifecycle.OpenToGuests(a.Status),
                    StaffMayRest = a.Rec.Type == 2 && a.Rec.StaffMayRest,
                    Built = a.Status != AttractionStatus.JustPlaced,
                    DoorX = door.X, DoorZ = door.Z,
                    CentreX = cx, CentreZ = cz,
                    ExitX = a.Rec.ExitTile(a.Ox, a.Oz, a.Rot)?.X ?? -1,
                    ExitZ = a.Rec.ExitTile(a.Ox, a.Oz, a.Rot)?.Z ?? -1,
                });
            }
            return _guestTargets;
        }

        /// <summary>How many guests to put in the park when one loads. ⚠ A STAND-IN for the bus
        /// arrivals (TPW.Sim.BusArrivals), which compute a real arrival rate from what is built and are
        /// not wired to this yet.</summary>
        const int DebugGuestCount = 24;

        /// <summary>--park-guests=N: keep N guests in the park regardless of the bus. A TEST HOOK, not a
        /// rule — the bus is the arrivals and this does not touch it. It exists because the arrival path
        /// currently delivers nobody (every attraction is handed upgrade level 0, so the park draw is the
        /// bare base score of 10 and the head count floors to zero), and a park with no guests in it
        /// cannot be used to test queueing, riding, wear or staff.</summary>
        public int ForcedGuests { get; set; } = -1;

        /// <summary>--park-hire=kind,x,z;... : put staff in the park at load. kind is TPW.Sim.StaffKind
        /// (0 mechanic, 1 entertainer, 2 cleaner, 3 guard, 4 researcher). A test hook: the game hires
        /// from a panel this port does not have, and what a hire COSTS and where it appears are not
        /// read yet (findings/staff.md is being written).</summary>
        /// <summary>--park-break=entry: drop a placed ride's reliability so it breaks down now. A TEST
        /// HOOK. Wear takes several minutes of running to cross the threshold on its own, which is too
        /// long to watch a mechanic with; nothing here changes the wear rule, it only moves the number
        /// the rule already reads.</summary>
        public bool Break(int entry)
        {
            foreach (var a in _attractionsPlaced)
                if (a.Rec.Entry == entry && a.IsRide)
                {
                    // Reliability AND the status, because wear only acts on a ride in status 2 — and a
                    // ride that nobody is queueing for never leaves 10, so lowering the number alone
                    // would leave the hook waiting on the same guests the hook exists to do without.
                    a.Reliability = 1;
                    a.Status = AttractionLifecycle.Enter(AttractionStatus.AboutToBreakDown, a);
                    return true;
                }
            return false;
        }

        /// <summary>What the park owes its staff for the month just ended. Zero with no staff, which is
        /// the right answer rather than a placeholder.</summary>
        public Money StaffWages(int lastDay, int monthLength)
            => _guests?.MonthlyWages(lastDay, monthLength) ?? Money.Zero;

        public bool Hire(int kind, int x, int z)
        {
            if (_guests == null || _map == null) return false;
            if (x < 0 || z < 0 || x >= _map.Width || z >= _map.Height) return false;
            _guests.Hire((TPW.Sim.StaffKind)kind, x, z);
            return true;
        }
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
                var wasStatus = a.Status;
                // ⭐ ONE CLOCK DRIVES BOTH. Construction and a run are the same accumulator against
                // different animation lengths (0x800658D8), which is why the build finishes with the
                // animation rather than on a timer.
                if (a.Status == AttractionStatus.UnderConstruction)
                    a.BuildAnimationComplete = a.Cycle.Advance(a.Status, a, frameTime, halfSpeed: false);
                else if (a.Status == AttractionStatus.Running && a.IsRide)
                    a.Cycle.RunTick(a, a.CyclesPerLoad, frameTime, halfSpeed: false);
                // ⭐ A SHOP ANIMATES TOO, AND THE FINDINGS SAID IT DID NOT. Every class's status-2 tick
                // sits at its vtable's +0x23C: for the rides that is the phase-counting tick, but Shop
                // (0x800E6A8C), Feature (0x800DCA5C) and SideShow (0x800E6D64) all carry 0x80065B78 —
                // a wrapper whose whole body is the same animation clock, with the completion discarded.
                // So they run their model and never count a cycle. See findings/rides.md §0 item 9.
                else if (a.Status == AttractionStatus.Running)
                    a.Cycle.Advance(a.Status, a, frameTime, halfSpeed: false);

                // Wear runs on the ride's own tick and can send it to 4 or 5; the lifecycle's tick then
                // sees the status it left behind.
                if (a.IsRide)
                {
                    var worn = a.Wear.Tick(a.Status, a, isTrackOrCoaster: a.Rec.Type is 1 or 6);
                    if (worn != a.Status) a.Status = AttractionLifecycle.Enter(worn, a);
                }

                // ⭐ THE RIDE PULLS GUESTS OFF THE QUEUE. Statuses 10 and 11 are where a guest gets on
                // and off; nothing in the guest's own machine does it (TPW.Sim.RideLoading).
                if (a.IsRide && _guests?.Rides is { } rw
                    && rw.RuntimeFor(a.Rec.Entry) is { } run
                    && (a.Status == AttractionStatus.Loading || a.Status == AttractionStatus.Unloading))
                {
                    var load = new ParkRideWorld.LoadAdapter(run, Math.Max(1, a.MaxSeats),
                        () => _clockTicks, () => (int)(_clockTicks / TPW.Sim.ParkClock.TicksPerDay),
                        g => _guests.PlaceAtExit(g, a.Rec, a.Ox, a.Oz, a.Rot));
                    var after = a.Status == AttractionStatus.Loading
                        ? TPW.Sim.RideLoading.Load(load, ref a.QueueEmptySince)
                        : TPW.Sim.RideLoading.Unload(load);
                    if (after != a.Status) a.Status = AttractionLifecycle.Enter(after, a);
                }

                var next = AttractionLifecycle.Tick(a.Status, a);
                if (next != a.Status) a.Status = AttractionLifecycle.Enter(next, a);

                // A ride spends most of a capture standing still waiting for a queue, so say when it
                // moves: without this, finding a frame where one is actually RUNNING is guesswork.
                if (_logRides && a.Status != wasStatus)
                    GD.Print($"[tpw] frame {Engine.GetFramesDrawn()}: {a.Rec.Entry} {wasStatus} -> {a.Status}, {a.Riders} aboard");

                // ⭐ THIS READS THE MESH THE RENDERER IS HOLDING, not the clock that is supposed to drive
                // it. A count taken off the simulation cannot see a view that has stopped listening to it
                // (it was exactly that blindness that let "8 riding" print over a jammed-looking park), so
                // the check for "does the ride move on screen" has to touch a.Inst.
                if (_logRides && a.Status == AttractionStatus.Running && a.Inst.Mesh is { } drawn)
                {
                    var box = drawn.GetAabb();
                    GD.Print($"[tpw] frame {Engine.GetFramesDrawn()}: {a.Rec.Entry} tick {a.Cycle.Accumulator >> RideCycle.FixedShift}, "
                           + $"drawing {(drawn == AttractionMesh(a.Rec.Entry) ? "the REST mesh" : "a posed mesh")}, "
                           + $"aabb {box.Size.X:0.000}x{box.Size.Y:0.000}x{box.Size.Z:0.000} at {box.Position.Y:0.000}");
                }
            }
        }

        /// <summary>Whether any placed ride is mid-cycle right now (<c>--shot=PATH:running</c>).</summary>
        public bool AnyRideRunning
        {
            get
            {
                foreach (var a in _attractionsPlaced)
                    if (a.Status == AttractionStatus.Running && a.Cycle.Accumulator > 0) return true;
                return false;
            }
        }

        /// <summary>Print each ride's status changes with the frame they happen on (<c>--park-log-rides</c>).</summary>
        public bool LogRides { set { _logRides = value; if (_guests != null) _guests.LogStaff = value; } }
        bool _logRides;

        /// <summary>Draw each attraction under construction moved by its build rig (0x800659C4): the rig's bone 1,
        /// posed at the build clock's whole ticks, applied to the whole model about a pivot at (width × 128, 0,
        /// height × 128) from the model's origin (its unturned footprint's corner) -- the TURNED footprint's width
        /// (slot 0x64) and, as the game has it, the model's height in tiles (slot 0x6C) where its depth would
        /// centre it -- and then the attraction's own placement. The rigs grow the model from about a tenth of its
        /// size with a springy overshoot, some turning it and hopping it up on the way. The clock is eased between
        /// park frames here. Anything else stands at rest.</summary>
        static int PhaseTicks(PlacedAttraction a) => a.PhaseLength(a.CurrentPhase);

        void PoseAttractions(double sinceFrame)
        {
            int frameTime = (int)(EntranceFlags.TimeUnitsPerSecond / ParticleSystem.FramesPerSecond);
            foreach (var a in _attractionsPlaced)
            {
                if (a.Status != AttractionStatus.UnderConstruction || BuildRig(a.Variant) is not { } rig)
                {
                    if (a.Inst.Transform != a.Rest) a.Inst.Transform = a.Rest;
                    // ⭐ A RUNNING ATTRACTION PLAYS ITS OWN MODEL — a shop as much as a ride. The cycle clock's whole ticks ARE the
                    // animation's frames - the same halfword is the phase length it races and the end of
                    // the model's keys - so the thing on screen is the thing the sim is counting. It
                    // holds its last pose through loading and unloading, which is where guests get on
                    // and off; only status 2 advances the clock (StepAttractions).
                    if (a.Cycle.Accumulator > 0 && PhaseTicks(a) is int len && len > 1)
                    {
                        int t = a.Cycle.Accumulator
                              + (int)(Math.Min(frameTime, RideCycle.MaxDelta) * Math.Clamp(sinceFrame * ParticleSystem.FramesPerSecond, 0, 1));
                        var posed = AttractionMeshAt(a.Rec.Entry, Math.Clamp(t >> RideCycle.FixedShift, 0, len - 1));
                        if (posed != null && a.Inst.Mesh != posed) a.Inst.Mesh = posed;
                    }
                    continue;
                }
                int clock = a.Cycle.Accumulator + (int)(Math.Min(frameTime, 0x4000) * Math.Clamp(sinceFrame * ParticleSystem.FramesPerSecond, 0, 1));
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

        /// <summary>What the open tool is about to charge, for the HUD's "Cost:" line (0x8001B1DC reads the tool's
        /// own +8, which each tool fills as its ghost is worked out: the blueprint's price, or the run's total).</summary>
        int PendingCost()
        {
            if (_map == null) return 0;
            if (_placing >= 0 && _placing < _attractions.Count) return _attractions[_placing].Rec.Price;
            if (_queue != null && _cursorTile is { } q) return _queue.GhostCost(_map, q.X, q.Z);
            if (_track != null && _cursorTile is { } t) return _track.GhostCost(_map, t.X, t.Z);
            if (_pathMode && _paths != null && _cursorTile is { } cur)
            {
                var run = _runStart is { } st ? PathTool.Run(st.X, st.Z, cur.X, cur.Z)
                                              : new List<(int X, int Z)> { cur };
                return _paths.RunCost(_map, run);
            }
            return 0;
        }

        /// <summary>The object the hover box goes round this frame, or null. As 0x80052324 picks it: with no tool
        /// open, the first attraction whose turned footprint holds the tile under the cursor; and while the park is
        /// closed, the park gate when the cursor is in its rectangle (you open the park there).</summary>
        BoxSite HoverTarget()
        {
            if (_map == null || _pathMode || _placing >= 0 || _queue != null || _track != null || (_picker != null && _picker.Visible)) return null;
            if ((_hoverPin ?? TileUnderMouse()) is not { } t) return null;
            return TargetAt(t);
        }

        /// <summary>The placed attraction under a tile, which is what a click on one selects. Same search as
        /// <see cref="TargetAt"/>, which returns only its hover box.</summary>
        PlacedAttraction AttractionAt((int X, int Z) t)
        {
            foreach (var a in _attractionsPlaced)
            {
                var (w, d) = a.Rec.Footprint(a.Rot);
                if (t.X >= a.Ox && t.X < a.Ox + w && t.Z >= a.Oz && t.Z < a.Oz + d) return a;
            }
            return null;
        }

        /// <summary>One row of an attraction's panel: a label from the game's own string table, and either a
        /// slider with its range or a plain value. ⭐ THE ROWS DIFFER BY TYPE and that is the whole point — a
        /// shop has NO sliders, it has a margin (what the stock costs against what it sells for), and a
        /// sideshow prices its game and its prize separately. A panel that drew sliders for all four types
        /// would be wrong for three of them (rides.md, the panel's own words).</summary>
        readonly record struct PanelRow(int Label, string Value, bool Slider, int Min, int Max, int Now);

        /// <summary>What the selected attraction's panel shows, per type — READ off the game's own draw
        /// routines (0x800794C4 ride, 0x8007A9E4 shop, 0x8007B2F4 sideshow), not inferred from label words.
        ///
        /// ⭐ THE RIDE'S DETAILS PAGE IS SIX READINGS AND UP TO THREE SLIDERS, and four of the readings are
        /// BARS rather than numbers: Age and Users are text, then Excitement, Reliability, Repair and Life are
        /// 0..100 bars. "Reliability" and "Repair" are NOT the same number — Reliability is the PROJECTED
        /// value (slot 88, what the current slider settings will wear it down to) and Repair is the live one
        /// (A+0xB4 >> 12). The panel shows both, side by side, which is the whole point of it.
        ///
        /// ⚠ DURATION IS HIDDEN ON A COASTER (0x800798D8) — not shown pinned at 1..1, which is what I had.
        /// And Capacity appears only when max seats > 1. So a coaster shows ONE slider, and a single-seat ride
        /// shows two.
        ///
        /// ⚠ UPGRADES IS A TAB, NOT A ROW. The panel is four pages behind a tab menu (Details, Options,
        /// Upgrades, Addons) and Upgrades only exists while the next level is researched and the ride is not
        /// condemned. Addons is track-rides-only.
        ///
        /// ⚠ A PLAIN FEATURE HAS NO PANEL AT ALL (0x80038900): a bench or a bin opens the root menu instead.
        /// Only toilets and staff rooms get one, and theirs are different panels again.
        ///
        /// ⚠ Values the port does not keep yet say "(not wired)" rather than showing a plausible invention:
        /// the placement day Age counts from, the served-guest count, and every shop and sideshow figure
        /// (those live in the spending work).</summary>
        List<PanelRow> PanelRows(PlacedAttraction a)
        {
            var rows = new List<PanelRow>();
            if (a == null) return rows;
            var r = TPW.Sim.RidePanel.Ranges(a);
            bool coaster = a.Type == AttractionType.RollerCoaster;
            switch (a.Type)
            {
                case AttractionType.Ride:
                case AttractionType.RollerCoaster:
                case AttractionType.TrackRide:
                case AttractionType.TourRide:
                    rows.Add(new PanelRow(PanelLabel.Age, "(not wired)", false, 0, 0, 0));
                    rows.Add(new PanelRow(PanelLabel.Users, "(not wired)", false, 0, 0, 0));
                    rows.Add(new PanelRow(PanelLabel.Excitement, $"{a.BaseIntensity}", false, 0, 100, a.BaseIntensity));
                    rows.Add(new PanelRow(PanelLabel.Reliability, "(not wired)", false, 0, 100, 0));
                    rows.Add(new PanelRow(PanelLabel.Repair, $"{a.Reliability}", false, 0, 100, a.Reliability));
                    rows.Add(new PanelRow(PanelLabel.Life, $"{a.Lifetime}", false, 0, 100, a.Lifetime));
                    rows.Add(new PanelRow(PanelLabel.Speed, null, true, r.Speed.Min, r.Speed.Max, a.SpeedSlider));
                    if (a.MaximumSeats > 1)
                        rows.Add(new PanelRow(PanelLabel.Capacity, null, true, r.Capacity.Min, r.Capacity.Max, a.Capacity));
                    if (!coaster)
                        rows.Add(new PanelRow(PanelLabel.Duration, null, true, r.Duration.Min, r.Duration.Max, a.CyclesPerLoad));
                    break;
                case AttractionType.Shop:
                    rows.Add(new PanelRow(PanelLabel.ShopCustomers, "(not wired)", false, 0, 0, 0));
                    rows.Add(new PanelRow(PanelLabel.CostOfGoods, "(not wired)", false, 0, 0, 0));
                    rows.Add(new PanelRow(PanelLabel.Takings, "(not wired)", false, 0, 0, 0));
                    rows.Add(new PanelRow(PanelLabel.Profit, "(not wired)", false, 0, 0, 0));
                    rows.Add(new PanelRow(PanelLabel.CustomerSatisfaction, "(not wired)", false, 0, 100, 0));
                    rows.Add(new PanelRow(PanelLabel.QualityOfGoods, "(not wired)", true, 50, 100, 0));
                    rows.Add(new PanelRow(PanelLabel.SalePrice, "(not wired)", false, 1, 500, 0));
                    break;
                case AttractionType.SideShow:
                    rows.Add(new PanelRow(PanelLabel.ShowCustomers, "(not wired)", false, 0, 0, 0));
                    rows.Add(new PanelRow(PanelLabel.Winners, "(not wired)", false, 0, 0, 0));
                    rows.Add(new PanelRow(PanelLabel.ShowTakings, "(not wired)", false, 0, 0, 0));
                    rows.Add(new PanelRow(PanelLabel.ShowProfit, "(not wired)", false, 0, 0, 0));
                    rows.Add(new PanelRow(PanelLabel.ShowExcitement, "(not wired)", false, 0, 100, 0));
                    rows.Add(new PanelRow(PanelLabel.Satisfaction, "(not wired)", false, 0, 100, 0));
                    rows.Add(new PanelRow(PanelLabel.ChanceOfWinning, "(not wired)", true, 0, 100, 0));
                    rows.Add(new PanelRow(PanelLabel.PrizeCost, "(not wired)", false, 1, 1000, 0));
                    rows.Add(new PanelRow(PanelLabel.GamePrice, "(not wired)", false, 1, 1000, 0));
                    break;
            }
            return rows;
        }

        /// <summary>The panel's labels, by string id in the game's own table (FOLIO entry 407 in English),
        /// READ off the draw routines rather than matched by word.</summary>
        static class PanelLabel
        {
            // Ride Details (0x800794C4)
            public const int Age = 0x1DC, Users = 0x18C, Excitement = 0x37;
            public const int Reliability = 0x3ED, Repair = 0x25C, Life = 0x3FA;
            public const int Speed = 0x1A1, Capacity = 0x364, Duration = 0x2D5;
            // The context list / Options page (0x8004A0B4)
            public const int BuildQueue = 0x374, EditQueue = 0x37F, BuildTrack = 0x0, EditTrack = 0xD;
            public const int EditPylons = 0x3DF, CallMechanic = 0xAE, Delete = 0x24D, ZoomTo = 0x174;
            // Upgrades page (0x80079E38)
            public const int UpgradeCost = 0x119, Stock = 0x363;
            // Shop Details (0x8007A9E4)
            public const int ShopCustomers = 0x4C, CostOfGoods = 0x11, Takings = 0x63, Profit = 0x3A4;
            public const int CustomerSatisfaction = 0x3FB, QualityOfGoods = 0x127, SalePrice = 0x230;
            // Sideshow Details (0x8007B2F4)
            public const int ShowCustomers = 0x293, Winners = 0x257, ShowTakings = 0x383, ShowProfit = 0xE2;
            public const int ShowExcitement = 0x350, Satisfaction = 0x2B9;
            public const int ChanceOfWinning = 0x116, PrizeCost = 0x313, GamePrice = 0xB6;
        }

        /// <summary>The commands the game offers for an attraction — ⭐ THE SAME LIST TWICE OVER. In the park
        /// CROSS pops it up at the cursor (0x800387AC → 0x800385D0), and inside the panel it IS the Options
        /// page; both are filled by 0x8004A0B4, so they can never drift apart. Master's mapping for the port:
        /// LMB opens the panel, RMB opens this.
        ///
        /// ⚠ Availability is the game's, where the port can answer it. "Call Mechanic" needs the ride broken
        /// (status 4 or 5) and not condemned; Build/Edit Queue and Build/Edit Track change word by whether one
        /// exists. "Zoom To" is deliberately absent: the game only offers it when the panel was opened from a
        /// LIST, not from the park.</summary>
        List<int> ContextCommands(PlacedAttraction a)
        {
            var cmds = new List<int>();
            if (a == null) return cmds;
            bool ride = a.IsRide;
            if (ride) cmds.Add(HasQueue(a) ? PanelLabel.EditQueue : PanelLabel.BuildQueue);
            if (a.Type == AttractionType.TrackRide || a.Type == AttractionType.RollerCoaster)
                cmds.Add(HasTrack(a) ? PanelLabel.EditTrack : PanelLabel.BuildTrack);
            if (a.Type == AttractionType.RollerCoaster && HasTrack(a)) cmds.Add(PanelLabel.EditPylons);
            if (ride && (a.Status == AttractionStatus.AboutToBreakDown || a.Status == AttractionStatus.BrokenDown)
                     && a.Wear.Lifetime != 0)
                cmds.Add(PanelLabel.CallMechanic);
            cmds.Add(PanelLabel.Delete);
            return cmds;
        }

        /// <summary>⚠ STAND-INS, both of them, and they decide which WORD the command shows. The port does not
        /// record which queue or which track belongs to which attraction yet, so these answer "no" and the
        /// list always says Build rather than Edit.</summary>
        static bool HasQueue(PlacedAttraction a) => false;
        static bool HasTrack(PlacedAttraction a) => false;

        /// <summary>The attraction whose context list is open (master: right button), or null, and where on
        /// screen the click was — the list pops up at the cursor, as the game's does.</summary>
        PlacedAttraction _contextFor;
        Vector2? _contextAt;

        /// <summary>The attraction whose panel is open, or null. ⚠ SELECTION ONLY SO FAR: the panel itself is
        /// not drawn yet, and what it shows per attraction type is being read off the disc rather than
        /// invented. This is the hook it will hang from, and it is deliberately separate from the hover box —
        /// hovering is where the cursor is, selecting is what you chose.</summary>
        PlacedAttraction _panelFor;

        BoxSite TargetAt((int X, int Z) t)
        {
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

        /// <summary>The GPU's additive blend (B + F, clamped): the box ADDS its colour to what is behind it, which is
        /// what the console does and what lets everything behind the box still show through it.
        ///
        /// ⚠ IT READ THE SCREEN AND IT MUST NOT. Adding in DISPLAY space is the accurate thing -- Godot's blend_add
        /// adds in the linear buffer, where the same numbers come out duller -- so this used to sample the screen
        /// texture, add, and write the result opaquely. That works until something TRANSPARENT is behind the box:
        /// the screen copy is taken once, before the transparent queue, so a transparent surface drawn before the
        /// box (i.e. one BEHIND it, since that queue draws back to front) is not in the copy, and writing the copy
        /// back paints over it. A hovered fountain lost its own water. Reported by master; reproduced.
        ///
        /// ⭐ SO IT ADDS THE DIFFERENCE. The screen is still read -- to work out how much light the display-space sum
        /// asks for over what is already there -- but that amount is ADDED rather than written, which is what keeps
        /// the colour the game gives and can no longer erase anything. Over an opaque background the two are the
        /// same arithmetic and the box looks exactly as it did; over a transparent one the copy is out of date, so
        /// the added amount is worked out against the wrong base and the glow is a little off there, which is a far
        /// smaller price than the water vanishing. Two boxes overlapping likewise add, each a little bright.</summary>
        const string SelectionShader = @"shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back;
uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
vec3 to_linear(vec3 c) {
    return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, lessThan(c, vec3(0.04045)));
}
vec3 to_display(vec3 c) {
    return mix(1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, c * 12.92, lessThan(c, vec3(0.0031308)));
}
void fragment() {
    vec3 behind = max(texture(screen_tex, SCREEN_UV).rgb, vec3(0.0));
    vec3 want = to_linear(clamp(to_display(behind) + COLOR.rgb, 0.0, 1.0));
    ALBEDO = max(want - behind, vec3(0.0));
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
            // Paid for by the tile (PathTool.PathTileCost): the game checks the running total against the balance as
            // it walks the run and refuses the tile the money does not reach, so the run stops there.
            int cost = 0;
            if (_bank != null)
            {
                run = _paths.Afford(_map, run, _bank.Balance.Pounds, out cost);
                if (run.Count == 0) { PlaySfx(ToolSound.Refused); return; }
            }
            // ⭐ CONNECTED: the run reached its last tile and that tile was already path (or queue or track). The place
            // step flags it (0x8004DE04 sets 0x801026D0 for the last tile of those types), the piece pass then
            // reports the run finished, the tool resets and 0x8001D5C0 plays sound 3 after sound 4. Master: "03 plays
            // when a path is connected to another one successfully".
            var last = run[^1];
            bool endsOnPath = _map[last.X, last.Z].Raw0 is 2 or 4 or 10 or 13;
            int laid = _paths.Lay(_map, run);
            if (laid > 0)
            {
                Charge(cost);
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

        /// <summary>The UI's sounds in group 5. Each was taken from a routine this port had ALREADY identified
        /// for another reason, so the sound and the thing it belongs to were established separately:
        ///
        /// ⭐ <b>PanelOpened</b> — 0x80038900, the CIRCLE/open dispatcher in findings/panel.md §0, tail-calls
        /// 0x80073EF4, which plays (5,2) at 0x800740D4.
        /// ⭐ <b>ContextOpened</b> — 0x800385D0, the routine that fills the context list and shows it at the
        /// cursor (panel.md §0), plays (5,3) at 0x8003877C.
        /// ⭐ <b>SliderMoved</b> — 0x80079300, the ride panel's slider input (panel.md §2), plays (5,6) at BOTH
        /// 0x80079354 and 0x80079378, the decrement and the increment, so either direction sounds the same.
        ///
        /// ⚠ The panel's base input 0x80044D38 is NOT here. It looks like (5,1), and it is not: `a1` is
        /// `addu a1,s2,zero` in the call's delay slot, a per-widget value, and the `addiu a1,zero,1` above it
        /// belongs to the PREVIOUS call. A scan that reads the nearest immediate gets that one wrong.</summary>
        public enum UiSound { PanelOpened = 2, ContextOpened = 3, SliderMoved = 6 }

        /// <summary>Give the park view the build tools' sound group (SoundGroup.Load(…, 7)) and the group with the
        /// placement tools' sounds (SoundGroup.Load(…, 8)).</summary>
        public void SetToolSounds(SoundGroup tools, SoundGroup park = null, SoundGroup ui = null)
        { _toolSounds = tools; _parkSounds = park; _uiSounds = ui; _sfxStreams.Clear(); }

        void PlaySfx(ToolSound which) => PlaySfx(_toolSounds, 7, (int)which);
        void PlaySfx(PlaceSound which) => PlaySfx(_parkSounds, 8, (int)which);
        void PlaySfx(UiSound which) => PlaySfx(_uiSounds, 5, (int)which);

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
            var run = PathTool.Run(x0, z0, x1, z1);
            int cost = _paths.RunCost(_map, run);
            int laid = _paths.Lay(_map, run);
            if (laid > 0) { Charge(cost); RebuildGround(); }
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
                    if (_cameraDebug && (_cameraShout++ % 25) == 0)
                        GD.Print($"[cam] eye tile {_gcam.EyeX >> 8},{_gcam.EyeZ >> 8}  ground {_gcam.Ground} " +
                                 $"(map floor {_map.ModalHeight}) eyeY {_gcam.EyeY}");
                    TakeGameCamera();
                }
                if (_gate != null)
                {
                    _gateAnglePrev = _gateAngleCur;
                    _gate.Update(frameTime, ParkOpen);
                    _gateAngleCur = (short)_gate.Angle;
                    _gateRecent[_gateRecentAt++ % _gateRecent.Length] = _gateAngleCur;
                }
                if (_bus != null)
                {
                    _busXPrev = _busXCur;
                    // ⭐ CARRYING SOMETHING FROM THE CATALOGUE COSTS YOU THE BUS. The game's `held`
                    // ([0x80103940]) is set while an object is on the cursor, and the arrival condition tests
                    // it: the bus still turns up on time, it just turns up EMPTY. _placing is this port's
                    // cursor-carry. ⚠ The gate BATCH hold is a different mechanism and is still not wired —
                    // see BusRoute.Batch.
                    if (_bus.Step(frameTime, ParkOpen, _placing >= 0)) BusArrived();
                    _busXCur = _bus.WorldX;
                }
                if (_gate != null)
                    foreach (int i in _gate.TakeDueEffects())
                        if (i < _openingFx.Count && _openingFx[i].T != null)
                            _fx?.Add(_openingFx[i].T, _openingFx[i].X, _openingFx[i].Y, _openingFx[i].Z);
                _fx?.Step();
                _selection.Step();
                _selection.Hover(hovered);
                foreach (var t in _hoverAlso) _selection.Hover(TargetAt(t));
                StepAttractions(frameTime);
                // ⚠ ONE CALL EACH, AND THEY ARE NOT THE SAME CLOCK IN THE ORIGINAL. The search runs
                // once a VIDEO frame (0x800EC8C4 from the game-mode tick) and the guests move once a
                // SIM tick; this loop is the sim tick, so the search currently gets half the slices it
                // would on hardware. It does not matter while ExpansionsPerSlice is unbounded and every
                // search finishes the frame it starts, and it will matter the moment that is set.
                if (_guests != null)
                {
                    _guests.CameraForward = -_camera.GlobalTransform.Basis.Z;
                    _guests.Redraw();
                }
                _clockTicks++;
                // The stand-in only runs where there is no bus: once the bus is on the road it is the arrivals.
                if (ForcedGuests >= 0) _guests?.Populate(ForcedGuests);
                else if (_bus == null) _guests?.Populate(DebugGuestCount);
                _guests?.RunPathfinder();
                _guests?.Tick();
            }
            // ⭐ THE GATE MOVES BETWEEN PARK FRAMES TOO. Its swing is worked out 25 times a second like everything
            // else in the game, but the port draws far more often than that, and a gate stepping at 25 while the
            // camera and the ride animations run at the window's rate reads as a low frame rate (master reported
            // exactly that). So it is drawn at the angle it is passing through right now.
            if (_gate != null)
            {
                float f = Mathf.Clamp((float)(_frameClock * ParticleSystem.FramesPerSecond), 0f, 1f);
                int angle = Mathf.RoundToInt(Mathf.Lerp(_gateAnglePrev, _gateAngleCur, f));
                int lo = int.MaxValue, hi = int.MinValue;
                foreach (int a in _gateRecent) { lo = Math.Min(lo, a); hi = Math.Max(hi, a); }
                if (_gateRecentAt >= _gateRecent.Length && hi - lo <= GateAtRest) angle = hi;
                if (angle != _gateAngleDrawn) { _gateMesh.Mesh = GateMesh(angle); _gateAngleDrawn = angle; }
            }
            PlaceBus();
            _selectionMesh.Mesh = SelectionMesh();
            PoseAttractions(_frameClock);
            _hud.Cost = PendingCost();
            // The panel, handed to the HUD as numbers; the HUD owns where they go.
            if (_panelFor != null)
            {
                var r = TPW.Sim.RidePanel.Ranges(_panelFor);
                bool coaster = _panelFor.Type == AttractionType.RollerCoaster;
                _hud.Panel = new ParkHud.AttractionPanelView
                {
                    Name = _catalogueNames?[_panelFor.Rec.NameId] ?? $"#{_panelFor.Rec.Entry}",
                    // ⚠ Age and Users stay null - the port records neither the day a ride was built nor how
                    // many it has served - and the panel shows a dash rather than a plausible number.
                    // ⭐ AGE IS IN YEARS, and the ride is not yet a year old for its first 365 days, so a new
                    // ride reads 0 and stays there. That is the game: 0x80079520 divides the DAYS the getter
                    // returns by 365 (magic-number divide, M'=0x16719F361 shift 8). Users stays a dash — the
                    // port has no all-time served counter (slot 22 = A+0x14) to read.
                    Age = (AgeInDays(_panelFor) / 365).ToString(), Users = null,
                    Excitement = _panelFor.BaseIntensity,
                    Reliability = -1,                       // ⚠ the PROJECTED value; not computed yet
                    Repair = _panelFor.Reliability,
                    Life = _panelFor.Lifetime,
                    Speed = _panelFor.SpeedSlider, SpeedMin = r.Speed.Min, SpeedMax = r.Speed.Max,
                    Capacity = _panelFor.Capacity, CapacityMin = r.Capacity.Min, CapacityMax = r.Capacity.Max,
                    ShowCapacity = _panelFor.MaximumSeats > 1,
                    Duration = _panelFor.CyclesPerLoad, DurationMin = r.Duration.Min, DurationMax = r.Duration.Max,
                    ShowDuration = !coaster,
                };
            }
            else _hud.Panel = null;
            // The right button's context list, handed to the HUD as words at a place on screen.
            if (_hud.ContextRows.Count > 0 || _contextFor != null)
            {
                _hud.ContextRows.Clear();
                if (_contextFor != null)
                {
                    foreach (int id in ContextCommands(_contextFor))
                        _hud.ContextRows.Add(_catalogueNames?[id] ?? $"#{id}");
                    if (_contextAt is { } ca) _hud.ContextAt = ca;
                }
            }
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
            else if (_track != null)
            {
                if (!_cursorPinned) _cursorTile = TileUnderMouse() ?? _cursorTile;
                _cursorMesh.Mesh = TrackMesh();
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
            // ⭐ THE TRACK BUILDER. Left lays the segment, right takes the last press back and closes the tool when
            // there is nothing left to take back, Esc closes it (the game's Cancel / Undo / Place, and builder 8's
            // Undo All on Shift+right).
            if (_track != null)
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                {
                    if ((_cursorPinned ? _cursorTile : TileUnderMouse() ?? _cursorTile) is { } tc) { _cursorTile = tc; PressTrack(tc); }
                    else PlaySfx(ToolSound.Refused);
                    return;
                }
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
                {
                    bool all = Input.IsKeyPressed(Key.Shift);
                    if (all) { _track.UndoAll(_map); RebuildGround(); RebuildTrackPieces(); PlaySfx(ToolSound.Undo); RefreshInfo(); }
                    else if (_track.Undo(_map)) { RebuildGround(); RebuildTrackPieces(); PlaySfx(ToolSound.Undo); RefreshInfo(); }
                    else CloseTrack(true);
                    return;
                }
                if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) { CloseTrack(true); return; }
            }
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
                    // The click that opens the tool only opens it -- and it does not open at all while the cursor is
                    // on something the hover box is round (master): a click there belongs to that attraction or to
                    // the gate, not to the path tool. That click now SELECTS the attraction, which is what opens
                    // its panel; clicking bare ground with nothing selected closes it again.
                    if (HoverTarget() != null)
                    {
                        if (tile is { } sel) { _panelFor = AttractionAt(sel); RefreshInfo(); }
                        return;
                    }
                    if (_panelFor != null) { _panelFor = null; RefreshInfo(); }
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
            // ⭐ MASTER'S MAPPING: left button opens the panel (above), right button opens the context list.
            // Only with no tool running — while a tool is open the right button is that tool's cancel.
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }
                && !_pathMode && _placing < 0 && _queue == null && _track == null)
            {
                var hit = TileUnderMouse() is { } ct ? AttractionAt(ct) : null;
                if (hit != null || _contextFor != null)
                {
                    _contextFor = hit;
                    _contextAt = hit != null ? GetViewport().GetMousePosition() : null;
                    RefreshInfo();
                    return;
                }
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
