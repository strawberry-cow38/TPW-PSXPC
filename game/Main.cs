using System.Threading.Tasks;
using Godot;
using TPW.Data;
using TPW.Launcher;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>The render half's entry point.
    ///
    /// ⚠ WHAT THIS IS FOR RIGHT NOW: proving the whole chain end to end with nothing faked -- the launcher
    /// identifies the user's disc, hands the path and variant over as environment, Godot starts, the
    /// ENGINE-FREE sim in core ticks, and the asset layer reads real bytes off the user's own copy. Each
    /// piece was built and tested separately; this is the thing that shows they meet.</summary>
    public partial class Main : Node
    {
        ParkClock _clock;
        ParkFinances _finances;
        bool _noGate;
        int _autoBreakAt = -1;
        bool _autoBreakHard;
        int _autoUpgrade = -1;
        string _autoPrice;
        bool _autoSeats;
        string _parkSavePath, _parkLoadPath;
        string _parkSaveProof;
        int _parkSaveAfter, _parkSaveFrames;
        ParkSaveHost _saveHost;
        GameDataResult _data;
        Label _status;
        Label _selfTest;
        TextureRect _preview;
        AudioStreamPlayer _audio;
        Button _playSound;
        PcmSample _sample;
        System.Collections.Generic.List<PcmSample> _sounds = new();
        System.Collections.Generic.List<TpwImage> _views = new();
        int _view;
        Button _next;
        /// <summary>Starts the boot chain (legal screen, intro movies, language, menu). ⭐ THE GAME WAITS FOR IT:
        /// master's call, so the debug menu is what a launch shows and the intro only runs when asked for.
        /// <c>--boot</c> or <c>--boot-from=</c> start it at once, as a launch used to.</summary>
        Button _startGame;
        bool _bootStarted, _bootNow;
        ModelBrowser _models;
        Label _modelInfo;
        readonly System.Random _rng = new();
        OptionButton _rate;
        MoviePlayer _player;
        AdvisorVoice _advisorVoice;
        ParkView _park;
        Button _parkButton;
        OptionButton _parkChoice;
        Label _parkInfo;
        System.Collections.Generic.List<(int Entry, ParkMap Map)> _maps = new();
        /// <summary>Each world's ground sheet by archive entry, for the maps found (see <see cref="ParkWorlds"/>).</summary>
        System.Collections.Generic.Dictionary<int, TextureSheet> _groundSheets = new();
        /// <summary>The game's music: every tracker module with the waveforms it plays.</summary>
        System.Collections.Generic.List<(int Entry, TrackerModule Module, System.Collections.Generic.List<PcmSample> Waves)> _modules = new();
        MusicPlayer _music;
        /// <summary>True while the music playing is the park's own (started by opening the park view).</summary>
        bool _parkStartedMusic;
        Button _musicButton;
        OptionButton _musicChoice;
        /// <summary>The common sheet (#416): guests, the flags by the bus stops, the loading font.</summary>
        TextureSheet _commonSheet;
        /// <summary>The build tools' sound-effect group (SoundGroup 7), for the park view's path tool, and group 8, with
        /// the placement tools' sounds.</summary>
        SoundGroup _toolSounds, _parkSounds;
        /// <summary>Group 5, the UI's own sounds, for the attraction panel and its widgets.</summary>
        SoundGroup _uiSounds;
        /// <summary>Group 1, the positional voices, for a running ride's noise.</summary>
        SoundGroup _guestSounds;
        /// <summary>The language's strings (English), for the park HUD's labels.</summary>
        StringTable _strings;
        /// <summary>Each world's attractions (AttractionCatalog) with their English names, for the park view's picker.</summary>
        readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<(AttractionDefinition, string)>> _attractionsByWorld = new();
        /// <summary>The game's executable (TPW.BIN), for the data tables the port reads out of it (particle templates).</summary>
        byte[] _exe;
        /// <summary>From <c>--park-open</c>: open the park as soon as it shows (captures of the gates opening).</summary>
        bool _autoOpen;
        /// <summary>From <c>--park-buildable</c>: show where a ride or shop may stand (the park view's B key).</summary>
        bool _autoBuildable;
        /// <summary>From <c>--park-gamecam</c>: start the park view on the game's own camera (its G key), seeded from
        /// <c>--park-view</c>'s focus and nearest quarter turn.</summary>
        bool _autoGameCam;
        /// <summary>From <c>--park-lay=x0,z0,x1,z1;...</c>: runs of path laid as the path tool would, and from
        /// <c>--park-pathcursor=x,z[,sx,sz]</c> its cursor pinned there. For captures of path building.</summary>
        string _autoLay, _autoPathCursor;
        /// <summary>From <c>--park-place=entry,x,z,rot;...</c>: attractions placed at load (footprint corner, quarter
        /// turns); from <c>--park-ghost=entry,x,z,rot</c>: one shown as the placement ghost there. For captures.</summary>
        string _autoPlace, _autoGhost, _autoDelete, _autoReplace, _autoEditQueue, _autoTile, _autoSay;
        bool _logRides;
        int _forcedGuests = -1;
        int _autoRequestUpgrade = -1;
        bool _researchAll;
        string _autoHire;
        int _autoBreak = -1;
        /// <summary>From <c>--park-queue=entry,x,z,rot:cx,cz:cx,cz...[:~hx,hz]</c>: a ride placed at load, then its queue
        /// tool pressed at each cursor tile in turn ("u" for the undo), the pointer then held at hx,hz. For captures of
        /// queue building.</summary>
        string _autoQueue;
        /// <summary>From <c>--park-track=entry,x,z,rot:cx,cz:...</c>: a coaster or track ride placed at load and its
        /// track laid by a press at each cursor tile ("u" for the undo). For captures of the track builder.</summary>
        string _autoTrack;
        /// <summary>From <c>--park-hover=x,z[,x,z...]</c>: the cursor held on that tile, and any further pairs held
        /// as hovered too, for captures of one hover box or of several overlapping.</summary>
        string _autoHover;
        /// <summary>From <c>--park-select=x,z</c>: the attraction on that tile selected at load, as a click on
        /// it does, so the panel can be captured.</summary>
        string _autoSelect;
        /// <summary>From <c>--park-slider=speed,75</c>: move a slider on the selected attraction and report
        /// what it actually became, so the clamping is provable without a mouse.</summary>
        string _autoSlider;
        /// <summary>From <c>--park-context=x,z</c>: the right button's command list opened on that tile.</summary>
        string _autoContext;
        /// <summary>From <c>--park-picker[=tab]</c>: the purchase catalogue open on that category, for captures.</summary>
        int _autoPicker = -1;
        /// <summary>Each world's gate pack by archive entry (ParkGate).</summary>
        System.Collections.Generic.Dictionary<int, SceneryPack> _gatePacks = new();
        /// <summary>The bus's model pack (archive entry 90), shared by every world.</summary>
        SceneryPack _busPack;
        /// <summary>Each world's scenery pack by archive entry.</summary>
        System.Collections.Generic.Dictionary<int, SceneryPack> _sceneryPacks = new();
        /// <summary>From <c>--park=203</c>: open the park view on that map once the disc is checked, UI hidden.</summary>
        int _autoPark = -1;
        /// <summary>From <c>--music=293</c>: play that module once the disc is checked (for captures: the movie
        /// writer records the mix, so the port's own output can be compared with a reference render).</summary>
        int _autoMusic = -1;
        /// <summary>From <c>--park-view=x,z,yaw,pitch,distance</c>: where the park camera starts.</summary>
        float[] _parkView;
        OptionButton _movieChoice;
        Button _playMovie;
        System.Collections.Generic.List<string> _movieFiles = new();
        StrMovie _pendingMovie;
        string _pendingMovieError;
        /// <summary>From <c>--movie=GRAV.STR</c> after <c>--</c> on the command line: play it as soon as the
        /// disc has been checked. With <c>--quit-after-movie</c>, exit when it ends, so a capture of it can
        /// be made unattended.</summary>
        string _autoMovie;
        bool _quitAfterMovie;

        /// <summary>--shot=PATH[:FRAME]: render until FRAME, write the window to PATH and quit. Exists so
        /// a change to something DRAWN can be looked at, in a repo whose whole discipline is that a
        /// green build is not a picture. Needs a display; xvfb-run supplies one on a headless box.</summary>
        string _shotPath; int _shotFrame = 150; int _shotClock; bool _shotWhenRunning; int _shotHold;

        /// <summary>--fullscreen: take the whole screen. The default is a window, which is what master
        /// wants to develop against — a fullscreen Godot is a nuisance to alt-tab out of all day.</summary>
        bool _fullscreen;
        /// <summary>--psx-snap: start with the PSX vertex grid on. The F3 option is the normal route.</summary>
        bool _psxSnap;
        CheckBox _snapBox;
        Button _playAdvisor;
        OptionButton _advisorLanguage;
        bool _hasAdvisor;
        System.Collections.Generic.List<AdvisorLine> _advisorLines;
        PcmSample _pendingLine;
        XaAudio.Coding _pendingLineCoding;
        string _pendingLineError;
        /// <summary>From <c>--advisor-line=LINE:LANGUAGE</c>: play that line once the disc is checked, and with
        /// <c>--quit-after-line</c> exit when it ends. For capturing it unattended.</summary>
        int _autoLine = -1, _autoLanguage;
        bool _quitAfterLine;
        /// <summary>From <c>--models=A,B,C</c>: hide the UI and show each model for two seconds, then quit. For
        /// capturing models unattended.</summary>
        int[] _modelTour;
        /// <summary>The tour as given: browser indices, "e133" for the first model of archive entry 133, or "e83.10" for
        /// entry 83's sub-model 10.</summary>
        string[] _modelTourSpec;
        /// <summary>From <c>--cull-on</c>: tour with back-face culling on, the view that shows winding faults.</summary>
        bool _tourCull;
        int _tourPos = -1, _tourFrames;
        /// <summary>--model-play: the tour plays each model's animation from its start. --tour-frames=N: frames per
        /// model (24 by default), long enough to watch a loop come round.</summary>
        bool _tourPlay;
        int _tourHold = 24;

        /// <summary>✅ 22,050 Hz, SETTLED BY LISTENING. Master tried the selector and identified it, and also
        /// worked out what these waveforms are: mostly short chunks of the game's MUSIC, cut up so it can
        /// stream seamlessly off the CD. That explains why the XM modules treat them as instruments.
        ///
        /// ⚠ THE ROUTE TO THIS NUMBER IS THE CAUTIONARY BIT. 22,050 was my first value, picked because "it is
        /// a common PSX rate" — not a reason. I then replaced it with 8363 Hz derived from the XM instrument
        /// headers, which is real evidence, properly extracted, and WRONG: it is the rate the tracker reasons
        /// in, not the rate the hardware plays at. So a lucky guess got overruled by a careful inference, and
        /// the guess had been right.
        ///
        /// The moral is not "trust guesses". It is that a guess and an inference look identical once they are
        /// a constant in a file, and neither of them was a measurement. Only listening was. The selector stays
        /// so the next open rate is settled the same way instead of argued about.</summary>
        static readonly int[] RateChoices = { 8363, 11025, 16726, 22050, 32000, 37800, 44100 };

        /// <summary>⚠⚠ 22,050 Hz IS RIGHT FOR THESE WAVEFORMS AND IS NOT THE GAME'S SAMPLE RATE. Master
        /// identified it by ear, on the 192 waveforms this browser plays — and fable has since established
        /// that those are the MUSIC bank, not the sound effects. The two are different populations with
        /// different rates, and nothing about listening to one says anything about the other.
        ///
        /// The effects have only two rates across all 132 of them: **11,025 Hz and 8,000 Hz**. Playing an
        /// effect at 22,050 would be twice to nearly three times too fast.
        ///
        /// ⚠ It also invalidates the route by which 22,050 was first suggested: tinyclaw read the console's
        /// pitch registers for voices 0-7, which are the MUSIC voices, so that measurement was of a tracker
        /// note's playback speed rather than a sample's base rate. A correct reading of the wrong voices.
        ///
        /// ✅ AND THE TWO FIGURES ARE NOT IN CONFLICT — they describe different things, which is the whole
        /// resolution. **8363 Hz is the tracker BASE**, exactly as the XM instrument headers said, and the
        /// pitches heard on the music voices are NOTES played from it. tinyclaw checked the console's actual
        /// pitch values against the base and every one lands on a semitone within 0.2%:
        ///
        ///     0x0817 = 22,297.6 Hz     8363 * 2^(17/12) = 22,326.5     -0.13%
        ///     0x0610 = 16,709.8 Hz     8363 * 2^(12/12) = 16,726.0     -0.10%
        ///     0x0308 =  8,354.9 Hz     8363 * 2^( 0/12) =  8,363.0     -0.10%
        ///
        /// So 22,050 is roughly a note seventeen semitones above the base, which is why it sounds right for
        /// a preview while 8363 sounds "too slow": these samples are written to be played UP. Both numbers
        /// were correct; the question "what is the sample rate" had two answers and no one had separated
        /// them.
        ///
        /// ⭐ It also dissolved an anomaly filed as noise. tinyclaw had recorded "22050, consistently +1.1%"
        /// and written the 1.1% off as emulator drift. 22050 * 1.011 = 22,292 — a musical interval, not
        /// drift. **A residual that sits at the same value every time is a signal nobody has identified yet.**
        ///
        /// ⚠ One value still does not fit: 0x0400 sits 1.24% off the nearest semitone while everything else
        /// is inside 0.2%. It is exactly 11,025 Hz — an SFX base rate — on a music voice. Unexplained, and
        /// not built on.</summary>
        public const int ConfirmedRateHz = 22050;

        /// <summary>The two rates the 132 sound EFFECTS actually use. Not yet wired: this browser plays the
        /// music bank. Recorded so the next person does not inherit 22,050 for them.</summary>
        public static readonly int[] EffectRatesHz = { 11025, 8000 };
        VBoxContainer _root;
        /// <summary>The grouped tabs inside <see cref="_root"/>. Kept as its own node for layout; it is
        /// <see cref="_root"/>'s visibility that F3 and the captures both act on.</summary>
        VBoxContainer _debugPanel;
        /// <summary>The whole debug overlay. ⭐ ONE SWITCH: F3 flips it, every capture path clears it.
        /// Two switches is what produced a chrome that was hidden AND underneath at the same time.</summary>
        CanvasLayer _debugLayer;
        /// <summary>The way back to a hidden debug menu without knowing F3: a small corner button that shows only
        /// while the mouse moves and fades out after <see cref="MenuTabSeconds"/>, so it never sits over the game or
        /// in a capture (captures do not move the mouse).</summary>
        CanvasLayer _menuTabLayer;
        Button _menuTab;
        double _menuTabShown = -1;
        const double MenuTabSeconds = 2.5;
        BootScreens _boot;
        /// <summary>From <c>--no-boot</c>: go straight to the debug tools. The capture paths set it
        /// too, since a capture of a model must not have 90 seconds of intro in front of it.</summary>
        bool _skipBoot;
        /// <summary>From <c>--boot-from=MainMenu</c>: start the chain partway in.</summary>
        string _bootFrom;
        /// <summary>The language the player picked, as the GAME's index -- never the ring position.</summary>
        int _language = 0;
        /// <summary>Sheet 84: every pixel of the main menu and the language flags (findings/menu-art.md).</summary>
        TextureSheet _menuSheet;
        double _accum;

        public override void _Ready()
        {
            // ⚠ THE GAME NEVER LOOKS FOR THE DISC ITSELF. The launcher already identified it and passes the
            // answer in; re-deriving it here would be a second implementation of the same rule, free to
            // disagree with the first. If the env is absent -- someone ran Godot directly -- we identify it
            // here through the SAME core call rather than a copy of it.
            string dataPath = System.Environment.GetEnvironmentVariable("TPW_DATA");
            string variant = System.Environment.GetEnvironmentVariable("TPW_VARIANT");

            _data = string.IsNullOrEmpty(dataPath)
                ? GameDataLocator.Probe()
                : GameDataLocator.Identify(dataPath);

            _clock = new ParkClock();
            // The opening balance: the park's game-mode init sets the bank to Money(50000, 0) (0x800588D0, at
            // 0x80058A94..0x80058AB8 through 0x800868B0; economy.md §1.2), what the HUD shows at the start. Whether a
            // level record overrides it later is not read.
            // ⭐ THE PARK NOW OWES WAGES. ParkFinances' own comment said the default of nothing was
            // "the honest answer, because the port has no staff yet"; it has staff now, so the honest
            // answer changed. The month just ended is the one to bill for — its length is on the
            // calendar AFTER the rollover, and the day it ended on is the day before today.
            ResetParkFinances();

            // ⚠ INSET FROM THE EDGES. Anchored full-rect with no offsets, the first label sits ON the top
            // edge and is clipped by it -- which looked like a missing widget rather than a margin bug.
            _root = new VBoxContainer
            {
                AnchorRight = 1, AnchorBottom = 1,
                OffsetLeft = 8, OffsetTop = 8, OffsetRight = -8, OffsetBottom = -8,
            };
            _root.AddThemeConstantOverride("separation", 6);
            // ⚠ ON ITS OWN LAYER, ABOVE THE BOOT. A Control parented straight to a Node draws on canvas
            // layer 0, and BootScreens is a CanvasLayer at 10 -- so the debug chrome was rendered
            // UNDERNEATH the boot screens and F3 did nothing visible at all. Measured: the top-left of
            // a frame with the panel open was the menu's own red with zero lit pixels over it.
            _debugLayer = new CanvasLayer { Layer = 20 };
            AddChild(_debugLayer);
            // An opaque-ish backing, or the debug text reads on top of whatever the game is drawing and
            // both become unreadable. It is a child of the LAYER, not of _root, because _root is a
            // VBoxContainer and anything parented to it joins the column.
            _debugLayer.AddChild(new ColorRect { Color = new Color(0.04f, 0.04f, 0.05f, 0.94f),
                                                 AnchorRight = 1, AnchorBottom = 1 });
            _debugLayer.AddChild(_root);

            // ⭐ THE DEBUG MENU. What used to be here was every label, every button and the legal-screen
            // preview stacked in one flat column, always on screen -- the port's whole UI was its debug
            // output. None of it is deleted: this is the same controls, grouped by what they inspect and
            // put behind one key, so the window can show the game instead.
            //
            // ⚠ ONE LEVEL OF VISIBILITY NOW, NOT TWO. This briefly had an outer node the captures hid
            // and an inner panel F3 toggled, plus an always-on "F3 — debug menu" hint. That was right
            // when the window had nothing in it but debug output, and the boot chain made it wrong: the
            // hint would burn over the game, and F3 toggled a panel inside a node the boot had hidden,
            // so it did nothing. The LAYER's visibility is the debug overlay now -- captures clear it,
            // F3 flips it, and it draws above everything with its own backing.
            _debugPanel = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            _debugPanel.AddThemeConstantOverride("separation", 8);
            _root.AddChild(_debugPanel);

            _debugPanel.AddChild(new Label { Text = "Theme Park World — Godot" });
            _startGame = new Button { Text = "▶  Start the game (intro, then the menu)", Disabled = true,
                                      SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            _startGame.Pressed += StartGame;
            var hideMenu = new Button { Text = "Hide this menu  (F3 or the ≡ corner button brings it back)" };
            hideMenu.Pressed += () => SetDebugMenu(false);
            var topRow = new HBoxContainer();
            topRow.AddThemeConstantOverride("separation", 8);
            topRow.AddChild(_startGame);
            topRow.AddChild(hideMenu);
            _debugPanel.AddChild(topRow);

            _menuTabLayer = new CanvasLayer { Layer = 21 };
            AddChild(_menuTabLayer);
            _menuTab = new Button { Text = "≡ menu", Visible = false, Position = new Vector2(8, 8),
                                    TooltipText = "Show the debug menu (F3)" };
            _menuTab.Pressed += () => SetDebugMenu(true);
            _menuTabLayer.AddChild(_menuTab);
            _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _debugPanel.AddChild(_status);
            _selfTest = new Label { Text = "Asset self-test: running…", AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _preview = new TextureRect
            {
                StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                CustomMinimumSize = new Vector2(320, 256),
            };

            // ⭐ A WAY TO LOOK AT THINGS. Every image fault today -- a 180 rotation, a residual mirror, the
            // wrong channel order -- was found by a person looking at the screen while every headless check
            // passed. So the port gets a button that cycles through what it has decoded, rather than making
            // someone rebuild to see the next asset.
            _next = new Button { Text = "Next image", Disabled = true };
            _next.Pressed += ShowNext;

            // ⭐ THE MODEL BROWSER. 531 meshes parsing with zero failures says the layout is
            // self-consistent; it does not say one of them is shaped like a rollercoaster. Only looking
            // says that, and looking is the instrument that has caught every asset fault so far.
            _models = new ModelBrowser();
            AddChild(_models);
            _modelInfo = new Label { Text = "Models: loading…" };
            _models.SetInfoLabel(_modelInfo);

            var prevModel = new Button { Text = "< Prev model" };
            var nextModel = new Button { Text = "Next model >" };
            prevModel.Pressed += () => _models.Prev();
            nextModel.Pressed += () => _models.Next();

            var cullBtn = new Button { Text = "Cull on/off" };
            var windBtn = new Button { Text = "Flip winding" };
            var texBtn = new Button { Text = "Textures on/off" };
            var animBtn = new Button { Text = "Play animation" };
            animBtn.Pressed += () => _models.TogglePlay();
            var nextAnimBtn = new Button { Text = "Next animated >" };
            nextAnimBtn.Pressed += () => _models.NextAnimated();
            cullBtn.Pressed += () => _models.ToggleCull();
            windBtn.Pressed += () => _models.ToggleWinding();
            texBtn.Pressed += () => _models.ToggleTextures();

            // ⭐ THE PARK VIEW. A park's map, drawn; see ParkView for what it does and does not show yet.
            _park = new ParkView();
            // ⚠ AFTER THE PARK EXISTS. _finances is built long before this line, so wiring the gate
            // there fired on a null park and the turnstile was never connected at all.
            _park.SetFinances(_finances);
            AddChild(_park);
            _parkInfo = new Label { Text = "" };
            _park.SetInfoLabel(_parkInfo);
            _parkButton = new Button { Text = "Park view", Disabled = true, ToggleMode = true };
            _parkButton.Toggled += on => ShowPark(on);
            _parkChoice = new OptionButton { Disabled = true };
            _parkChoice.ItemSelected += i => { if (_parkButton.ButtonPressed) ShowPark(true); };

            // ⚠ THE TABS MUST EXPAND, NOT BE GIVEN A FIXED SIZE. A CustomMinimumSize taller than the
            // window pushes the panel off the bottom of the screen and nothing clips it -- the controls
            // are simply gone, with no scrollbar to suggest they exist.
            var tabs = new TabContainer
            {
                CustomMinimumSize = new Vector2(0, 180),
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            };
            _debugPanel.AddChild(tabs);

            // ⚠ A TabContainer TAKES ITS TAB TITLES FROM ITS CHILDREN'S NODE NAMES, so Name is the label
            // here and not decoration -- leave it off and the tab reads "@VBoxContainer@31".
            // ⚠ THE SCROLLER IS THE TAB, AND THE BOX INSIDE IT IS WHAT CALLERS FILL. The self-test alone
            // is 15 lines plus a 256px image, which is taller than the tab on any window -- without this
            // the overflow is invisible rather than scrollable.
            static VBoxContainer Tab(TabContainer into, string name)
            {
                var scroll = new ScrollContainer { Name = name };
                var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                v.AddThemeConstantOverride("separation", 8);
                scroll.AddChild(v);
                into.AddChild(scroll);
                return v;
            }
            static HBoxContainer Row(Node into, params Node[] items)
            {
                var h = new HBoxContainer();
                h.AddThemeConstantOverride("separation", 8);
                foreach (var i in items) h.AddChild(i);
                into.AddChild(h);
                return h;
            }

            // Disc: what the self-test found, and the decoded images -- the legal/copyright screen among
            // them, which is why that text was the first thing on screen before this.
            var discTab = Tab(tabs, "Disc");
            discTab.AddChild(_selfTest);
            Row(discTab, _next);
            discTab.AddChild(_preview);

            var modelTab = Tab(tabs, "Models");
            Row(modelTab, prevModel, nextModel, cullBtn, windBtn, texBtn, animBtn, nextAnimBtn);
            modelTab.AddChild(_modelInfo);

            // ⭐ A GRAPHICS OPTION, OFF BY DEFAULT (master). The PSX snapped every vertex to a whole pixel;
            // this turns that on. Registered before any material exists — see PsxShading.RegisterGlobals.
            PsxShading.RegisterGlobals();
            _snapBox = new CheckBox { Text = "PSX vertex snapping (the wobble)", ButtonPressed = false };
            var snapBox = _snapBox;
            snapBox.Toggled += on =>
            {
                PsxShading.SetSnap(on);
                GD.Print($"[tpw] psx vertex snapping {(on ? "ON, grid 256x120" : "off")}");
            };

            var parkTab = Tab(tabs, "Park");
            parkTab.AddChild(snapBox);
            Row(parkTab, _parkButton, _parkChoice);
            parkTab.AddChild(_parkInfo);

            _audio = new AudioStreamPlayer();
            AddChild(_audio);
            _playSound = new Button { Text = "Play a sound from your disc", Disabled = true };
            _playSound.Pressed += PlaySample;

            _rate = new OptionButton();
            foreach (int hz in RateChoices) _rate.AddItem($"{hz} Hz");
            _rate.Selected = System.Array.IndexOf(RateChoices, ConfirmedRateHz);
            _rate.ItemSelected += _ => GD.Print($"[tpw] sample rate set to {CurrentRate} Hz");

            var soundRow = new HBoxContainer { };
            soundRow.AddThemeConstantOverride("separation", 8);
            soundRow.AddChild(_playSound);
            soundRow.AddChild(new Label { Text = "rate:", VerticalAlignment = VerticalAlignment.Center });
            soundRow.AddChild(_rate);
            // ⭐ THE ADVISOR: 3,616 recorded lines in ADVISOR.TPW, two thirds of the disc. Which line is which is
            // not known yet (the file has no index), so this plays a random one, the same way the sound button
            // does, and for the same reason: a different one each press shows the whole file is read correctly.
            _playAdvisor = new Button { Text = "Play an advisor line", Disabled = true };
            _playAdvisor.Pressed += PlayAdvisorLine;
            soundRow.AddChild(_playAdvisor);
            _advisorLanguage = new OptionButton();
            foreach (var name in AdvisorSpeech.LanguageNames) _advisorLanguage.AddItem(name);
            _advisorLanguage.AddItem("any language");
            _advisorLanguage.Selected = 0;   // English
            soundRow.AddChild(_advisorLanguage);
            // ⭐ THE MUSIC: the game's tracker modules, synthesised live by TrackerPlayer (FT2's own rules; checked
            // against libopenmpt module by module). Which module plays where in the game is not known yet.
            _music = new MusicPlayer();
            AddChild(_music);
            _musicButton = new Button { Text = "Music", ToggleMode = true, Disabled = true };
            _musicButton.Toggled += on => { _parkStartedMusic = false; PlayMusic(on); };
            _musicChoice = new OptionButton { Disabled = true };
            _musicChoice.ItemSelected += _ => { _parkStartedMusic = false; if (_musicButton.ButtonPressed) PlayMusic(true); };
            var audioTab = Tab(tabs, "Audio");
            audioTab.AddChild(soundRow);
            Row(audioTab, new Label { Text = "music:", VerticalAlignment = VerticalAlignment.Center },
                          _musicButton, _musicChoice);

            // ⭐ THE MOVIES. In the game they play on ENTERING A WORLD (see StrMovie.Catalogue), and there is no
            // world to enter yet, so for now they play from here: pick one, press play, any key skips.
            _player = new MoviePlayer();
            AddChild(_player);
            _advisorVoice = new AdvisorVoice();
            AddChild(_advisorVoice);
            _player.Finished += OnMovieFinished;

            // ⭐ THE BOOT CHAIN. Built here but not started until the self-test has run, so the legal
            // screen has its art and the movie list is known.
            _boot = new BootScreens { Visible = false, ProcessMode = ProcessModeEnum.Disabled };
            AddChild(_boot);
            _boot.WantMovie += PlayMovieNamed;
            _boot.WantMenuMusic += PlayFrontEndMusic;
            _boot.LanguageChosen += l =>
            {
                _language = l;
                GD.Print($"[tpw] language chosen: {StringTable.LanguageNames[l]} (game index {l})");
            };
            _boot.StartPracticePark += () =>
            {
                int i = _maps.FindIndex(m => m.Entry == ParkWorlds.PracticeParkMap);
                if (i < 0) { GD.PushWarning($"[tpw] Practice Park map {ParkWorlds.PracticeParkMap} is not on this disc"); return; }
                _parkChoice.Selected = i;
                _debugLayer.Visible = false;
                _boot.Visible = false;
                ShowPark(true);
            };
            // ⚠ SFX is carried but not applied: the port has no sound-effect bus yet, only music and
            // the one-shot sample button. Wiring it to the music player instead would make the slider
            // LOOK like it works, which is worse than a slider that visibly does nothing.
            _boot.VolumeChanged += (music, _) => _music?.SetVolume(music);
            _playMovie = new Button { Text = "Play movie", Disabled = true };
            _playMovie.Pressed += PlayMovie;
            _movieChoice = new OptionButton { Disabled = true };
            var movieRow = new HBoxContainer();
            movieRow.AddThemeConstantOverride("separation", 8);
            movieRow.AddChild(_playMovie);
            movieRow.AddChild(_movieChoice);
            Tab(tabs, "Movies").AddChild(movieRow);

            foreach (var arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--movie=")) _autoMovie = arg.Substring("--movie=".Length).ToUpperInvariant();
                else if (arg == "--quit-after-movie") _quitAfterMovie = true;
                else if (arg.StartsWith("--shot="))
                {
                    // ⚠ A WINDOWS PATH HAS A COLON IN IT ("C:\\..."), so only the LAST colon can be the frame,
                    // and only when what follows it is a number. Splitting on the first wrote a file called "C".
                    //
                    // :running (optionally :running:N) waits for a ride to be mid-cycle instead of counting
                    // frames. A capture is NOT frame-deterministic - the park catches up several sim frames
                    // per drawn one, so the same frame number lands on a different animation tick each run,
                    // which makes "photograph the ride mid-cycle" a lottery when spelled as a number.
                    var spec = arg.Substring("--shot=".Length);
                    int hold = -1, colon = spec.LastIndexOf(':');
                    if (colon > 1 && int.TryParse(spec.Substring(colon + 1), out int f))
                    { hold = f; spec = spec.Substring(0, colon); colon = spec.LastIndexOf(':'); }
                    if (colon > 1 && spec.Substring(colon + 1) == "running")
                    {
                        _shotWhenRunning = true; _shotFrame = int.MaxValue;
                        if (hold >= 0) _shotHold = hold;
                        spec = spec.Substring(0, colon);
                    }
                    else if (hold >= 0) _shotFrame = hold;
                    _shotPath = spec;
                }
                else if (arg.StartsWith("--advisor-line="))
                {
                    var parts = arg.Substring("--advisor-line=".Length).Split(':');
                    _autoLine = int.Parse(parts[0]);
                    _autoLanguage = parts.Length > 1 ? int.Parse(parts[1]) : 0;
                }
                else if (arg == "--quit-after-line") _quitAfterLine = true;
                else if (arg.StartsWith("--models="))
                    _modelTourSpec = arg.Substring("--models=".Length).Split(',');
                else if (arg == "--cull-on") _tourCull = true;
                else if (arg == "--model-play") _tourPlay = true;
                else if (arg.StartsWith("--tour-frames=")) _tourHold = System.Math.Max(1, int.Parse(arg.Substring("--tour-frames=".Length)));
                // Turns the graphics option on from the command line. It is OFF by default (master), and a
                // still capture is the only way to test a shader that has no UI in a headless run.
                else if (arg == "--psx-snap") _psxSnap = true;
                else if (arg == "--fullscreen") _fullscreen = true;
                else if (arg == "--windowed") { }        // still accepted: it is what the capture scripts pass
                else if (arg == "--no-boot") _skipBoot = true;
                else if (arg == "--boot") _bootNow = true;
                else if (arg.StartsWith("--boot-from=")) _bootFrom = arg.Substring("--boot-from=".Length);
                else if (arg.StartsWith("--park=")) _autoPark = int.Parse(arg.Substring("--park=".Length));
                else if (arg.StartsWith("--music=")) _autoMusic = int.Parse(arg.Substring("--music=".Length));
                else if (arg == "--park-open") _autoOpen = true;
                else if (arg == "--park-buildable") _autoBuildable = true;
                else if (arg == "--park-gamecam") _autoGameCam = true;
                else if (arg.StartsWith("--park-lay=")) _autoLay = arg.Substring("--park-lay=".Length);
                else if (arg.StartsWith("--park-place=")) _autoPlace = arg.Substring("--park-place=".Length);
                else if (arg.StartsWith("--park-delete=")) _autoDelete = arg.Substring("--park-delete=".Length);
                else if (arg.StartsWith("--advisor-say=")) _autoSay = arg.Substring("--advisor-say=".Length);
                else if (arg.StartsWith("--park-replace=")) _autoReplace = arg.Substring("--park-replace=".Length);
                else if (arg.StartsWith("--park-editqueue=")) _autoEditQueue = arg.Substring("--park-editqueue=".Length);
                else if (arg.StartsWith("--park-tile=")) _autoTile = arg.Substring("--park-tile=".Length);
                else if (arg == "--park-log-rides") _logRides = true;
                else if (arg.StartsWith("--park-guests=")) _forcedGuests = int.Parse(arg.Substring("--park-guests=".Length));
                else if (arg == "--park-nogate") _noGate = true;
                else if (arg == "--park-nopreflight") _noPreflight = true;
                else if (arg.StartsWith("--park-pelt=")) _autoPeltAt = int.Parse(arg.Substring("--park-pelt=".Length));
                else if (arg.StartsWith("--park-upgrade=")) _autoUpgrade = int.Parse(arg.Substring("--park-upgrade=".Length));
                else if (arg == "--park-research-all") _researchAll = true;
                else if (arg.StartsWith("--park-request-upgrade="))
                    _autoRequestUpgrade = int.Parse(arg.Substring("--park-request-upgrade=".Length));
                else if (arg.StartsWith("--park-price=")) _autoPrice = arg.Substring("--park-price=".Length);
                else if (arg == "--park-seats") _autoSeats = true;
                else if (arg.StartsWith("--park-save=")) _parkSavePath = arg.Substring("--park-save=".Length);
                else if (arg.StartsWith("--park-load=")) _parkLoadPath = arg.Substring("--park-load=".Length);
                else if (arg.StartsWith("--park-save-after=")) _parkSaveAfter = int.Parse(arg.Substring("--park-save-after=".Length));
                else if (arg.StartsWith("--park-save-proof=")) _parkSaveProof = arg.Substring("--park-save-proof=".Length);
                else if (arg.StartsWith("--park-hire=")) _autoHire = arg.Substring("--park-hire=".Length);
                else if (arg.StartsWith("--park-research=")) _autoResearch = arg.Substring("--park-research=".Length);
                else if (arg.StartsWith("--park-break="))
                {
                    // --park-break=ENTRY or ENTRY@FRAME. ⚠ BREAKING AT LOAD TESTS NOTHING ABOUT A LOADED
                    // RIDE: the ride is empty then, so the eject path runs over nobody and the invariant
                    // it is meant to check passes vacuously. @FRAME lets it break with guests aboard.
                    // ENTRY[!][@FRAME]; the ! takes reliability to zero and through BrokenDown, which
                    // is the only setting that makes the ride eject its riders and its queue.
                    var bspec = arg.Substring("--park-break=".Length);
                    _autoBreakHard = bspec.Contains('!');
                    var bp = bspec.Replace("!", "").Split('@');
                    _autoBreak = int.Parse(bp[0]);
                    if (bp.Length > 1) _autoBreakAt = int.Parse(bp[1]);
                }
                else if (arg.StartsWith("--park-ghost=")) _autoGhost = arg.Substring("--park-ghost=".Length);
                else if (arg.StartsWith("--park-queue=")) _autoQueue = arg.Substring("--park-queue=".Length);
                else if (arg.StartsWith("--park-track=")) _autoTrack = arg.Substring("--park-track=".Length);
                else if (arg.StartsWith("--park-hover=")) _autoHover = arg.Substring("--park-hover=".Length);
                else if (arg.StartsWith("--park-select=")) _autoSelect = arg.Substring("--park-select=".Length);
                else if (arg.StartsWith("--park-slider=")) _autoSlider = arg.Substring("--park-slider=".Length);
                else if (arg.StartsWith("--park-context=")) _autoContext = arg.Substring("--park-context=".Length);
                else if (arg == "--park-picker") _autoPicker = 0;
                else if (arg.StartsWith("--park-picker=")) _autoPicker = int.Parse(arg.Substring("--park-picker=".Length));
                else if (arg.StartsWith("--park-pathcursor=")) _autoPathCursor = arg.Substring("--park-pathcursor=".Length);
                else if (arg.StartsWith("--park-view="))
                    _parkView = System.Array.ConvertAll(arg.Substring("--park-view=".Length).Split(','),
                        v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
            }

            if ((_parkSavePath != null || _parkLoadPath != null || _parkSaveProof != null) && _autoPark < 0)
            {
                FailParkSave(new System.ArgumentException("Park file hooks require --park=MAP so the matching map and catalogue are loaded."));
                return;
            }

            // ⭐ A WINDOW BY DEFAULT, FULLSCREEN ON REQUEST. This went fullscreen-by-default earlier today and
            // master asked for it back: developing against it means alt-tabbing all day. --fullscreen still
            // takes the whole screen, and the layout fills whatever it is given either way, because every HUD
            // coordinate here is a FRACTION of the viewport.
            //
            // ⚠ A capture must NEVER take the display whoever starts it. master plays on this same box from
            // their own clone, so --shot and --write-movie refuse fullscreen THEMSELVES rather than trusting
            // whichever script launched them. That guard stays even though the default is now a window.
            bool capturing = _shotPath != null
                             || System.Array.Exists(OS.GetCmdlineArgs(), a => a.StartsWith("--write-movie"));
            if (_fullscreen && !capturing) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);

            // ⚠ AFTER THE ARGUMENT LOOP, because it reads one. Set beside the checkbox where it is BUILT and
            // --psx-snap can never apply: the UI is constructed before the arguments are parsed. That is the
            // fourth hook today ordered before the thing it depends on, so it is called out rather than moved
            // quietly -- the failure is always silent and always looks like the flag doing nothing.
            if (_psxSnap)
            {
                PsxShading.SetSnap(true);
                if (_snapBox != null) _snapBox.SetPressedNoSignal(true);
            }

            GD.Print($"[tpw] data: {_data.Message}");
            GD.Print($"[tpw] launcher said variant={variant}, we identified {_data.Variant?.Id ?? "(none)"}");

            // ⚠ A DISAGREEMENT HERE IS A REAL BUG, SO IT IS CHECKED RATHER THAN ASSUMED. The launcher and the
            // game run the same core rule against the same file; if they reach different answers, one of them
            // is reading something else, and finding that out at startup beats finding it out in the economy
            // six hours later.
            if (!string.IsNullOrEmpty(variant) && _data.Variant != null && variant != _data.Variant.Id)
                GD.PushError($"[tpw] launcher and game disagree on the variant: '{variant}' vs '{_data.Variant.Id}'");

            StartSelfTest();
        }

        /// <summary>⚠ OFF THE MAIN THREAD. The self-test reads and parses a 16 MB archive and hashes its way
        /// through several hundred entries; doing that in _Ready freezes the window for seconds on first run,
        /// which reads as a hang. Results come back through CallDeferred because Godot nodes may only be
        /// touched from the main thread -- a cross-thread node write does not reliably throw, it corrupts.</summary>
        void StartSelfTest()
        {
            string path = _data.SourcePath;
            if (string.IsNullOrEmpty(path))
            {
                _selfTest.Text = "Asset self-test: skipped — no game data identified.";
                return;
            }

            Task.Run(() =>
            {
                SelfTestReport report;
                TpwImage legal = null;
                System.Collections.Generic.List<PcmSample> sounds = new();
                var views = new System.Collections.Generic.List<TpwImage>();
                var movies = new System.Collections.Generic.List<string>();
                try
                {
                    using var disc = DiscReader.Open(path);
                    report = AssetSelfTest.Run(disc);

                    var f = disc.Find(AssetSelfTest.LegalScreen);
                    // ⚠ NOT the spec TGA path. This file is a VRAM block in a TGA wrapper: BGR channel
                    // order, and the descriptor's origin bits do not apply. See Tga.TryDecodeVramBlock.
                    if (f != null && Tga.TryDecodeVramBlock(disc.ReadFile(f), out var img, out _))
                    { legal = img; legal.Source = "LEGAL.GFX"; views.Add(legal); }
                    views.AddRange(SpriteBlocks(disc));
                    views.AddRange(TextureSheets(disc));
                    _models.Load(disc);   // parses 531 meshes; far too slow for the main thread
                    sounds = AllSounds(disc);
                    foreach (var df in disc.Files)
                        if (!df.IsDirectory && df.Name.EndsWith(".STR", System.StringComparison.OrdinalIgnoreCase))
                            movies.Add(df.Name);
                    _hasAdvisor = disc.Find(AdvisorSpeech.File) != null && disc.IsRawSectors;
                    var af = disc.Find(AssetSelfTest.AssetArchive);
                    if (af != null && GazArchive.TryParse(disc.ReadFile(af), out var gz, out _))
                    {
                        _modules = TrackerModule.FindAll(gz);
                        var exeFile = disc.Find(AssetSelfTest.GameExecutable);
                        if (exeFile != null) _exe = disc.ReadFile(exeFile);
                        if (EntranceFlags.CommonSheet < gz.Entries.Count &&
                            TextureSheet.TryParse(gz.Read(gz.Entries[EntranceFlags.CommonSheet]), out var common, out _))
                            _commonSheet = common;
                        // The build tools' sounds: group 7 of the game's effect groups (entries 320/321).
                        _toolSounds = SoundGroup.Load(gz, _exe, AssetSelfTest.GameExecutableBase, 7);
                        _parkSounds = SoundGroup.Load(gz, _exe, AssetSelfTest.GameExecutableBase, 8);
                        _uiSounds = SoundGroup.Load(gz, _exe, AssetSelfTest.GameExecutableBase, 5);
                        _guestSounds = SoundGroup.Load(gz, _exe, AssetSelfTest.GameExecutableBase, 1);
                        // Each world's attractions for the park view's picker, named from the English table.
                        StringTable english = null;
                        int enEntry = StringTable.EntryByLanguage[0];
                        if (enEntry < gz.Entries.Count && StringTable.TryParse(gz.Read(gz.Entries[enEntry]), enEntry, out var en, out _)) english = en;
                        _strings = english;
                        for (int w = 0; w < 4; w++)
                        {
                            var list = new System.Collections.Generic.List<(AttractionDefinition, string)>();
                            foreach (int ae in AttractionCatalog.ForWorld(w))
                            {
                                if (ae >= gz.Entries.Count) continue;
                                var rec = AttractionDefinition.Read(ae, gz.Read(gz.Entries[ae]));
                                if (rec != null) list.Add((rec, english?[rec.NameId] ?? $"entry {ae}"));
                            }
                            _attractionsByWorld[w] = list;
                        }
                        if (MenuLayout.Sheet < gz.Entries.Count &&
                            TextureSheet.TryParse(gz.Read(gz.Entries[MenuLayout.Sheet]), out var ms, out _))
                            _menuSheet = ms;
                        // The bus: one model for all four worlds, on the common sheet (TPW.Sim.BusRoute).
                        if (TPW.Sim.BusRoute.ModelEntry < gz.Entries.Count &&
                            SceneryPack.TryParse(gz.Read(gz.Entries[TPW.Sim.BusRoute.ModelEntry]), out var busPack, out _))
                            _busPack = busPack;
                        foreach (var (entry, map) in ParkMap.FindAll(gz))
                        {
                            _maps.Add((entry.Index, map));
                            var world = ParkWorlds.ForMap(entry.Index);
                            if (world != null && !_groundSheets.ContainsKey(world.GroundSheet) && world.GroundSheet < gz.Entries.Count &&
                                TextureSheet.TryParse(gz.Read(gz.Entries[world.GroundSheet]), out var ground, out _))
                                _groundSheets[world.GroundSheet] = ground;
                            var gateInfo = world != null ? ParkGate.ForWorld(world.Index) : null;
                            if (gateInfo != null && !_gatePacks.ContainsKey(gateInfo.PackEntry) && gateInfo.PackEntry < gz.Entries.Count &&
                                SceneryPack.TryParse(gz.Read(gz.Entries[gateInfo.PackEntry]), out var gatePack, out _))
                                _gatePacks[gateInfo.PackEntry] = gatePack;
                            if (world != null && !_sceneryPacks.ContainsKey(world.SceneryEntry) && world.SceneryEntry < gz.Entries.Count &&
                                SceneryPack.TryParse(gz.Read(gz.Entries[world.SceneryEntry]), out var pack, out _))
                                _sceneryPacks[world.SceneryEntry] = pack;
                        }
                    }
                }
                catch (System.Exception e)
                {
                    report = new SelfTestReport();
                    report.Add("disc", false, e.Message);
                }
                // ⚠ An empty array, never null: these arguments cross into Godot as Variants, and a null
                // byte[] does not round-trip the way a managed null would -- it arrives as an empty
                // PackedByteArray anyway, so say so here rather than relying on that.
                // Published before CallDeferred, so the main thread sees a fully built list when it runs.
                _sounds = sounds;
                _views = views;
                _movieFiles = movies;
                _sample = sounds.Count > 0 ? sounds[0] : null;
                CallDeferred(nameof(ApplySelfTest), ReportToText(report), report.AllOk,
                    legal?.Rgba ?? System.Array.Empty<byte>(), legal?.Width ?? 0, legal?.Height ?? 0);
            });
        }

        static string ReportToText(SelfTestReport r)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Asset self-test — " + r.Summary());
            foreach (var c in r.Checks) sb.AppendLine("  " + c);
            return sb.ToString();
        }

        void ApplySelfTest(string text, bool ok, byte[] rgba, int w, int h)
        {
            _selfTest.Text = text;
            _selfTest.AddThemeColorOverride("font_color", ok ? new Color(0.5f, 0.85f, 0.55f) : new Color(0.9f, 0.5f, 0.45f));

            // Print it too: a headless run (--headless, CI, or a launcher-driven smoke test) has no window to
            // read, and this is exactly the report you want in a log when someone says "it won't start".
            GD.Print(text);
            if (!ok) GD.PushWarning("[tpw] asset self-test reported failures — see the log above.");

            // ⚠ NOT AN EARLY RETURN. This used to `return` when the legal screen had not decoded, which
            // silently skipped everything below it too: models, sounds and movies all stayed disabled because
            // of one unrelated image. Code under an early return only runs on the happy path.
            if (rgba != null && rgba.Length > 0 && w > 0 && h > 0)
            {
                var image = Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);
                _preview.Texture = ImageTexture.CreateFromImage(image);
                GD.Print($"[tpw] legal screen decoded from the user's disc: {w}x{h}");
            }

            // ⚠ EVERY CAPTURE PATH SKIPS THE BOOT. A --models= or --park= capture that sat through 90
            // seconds of legal screen and intro would record the intro, and the failure looks like the
            // capture being broken rather than being early.
            if (_modelTourSpec != null || _autoPark >= 0 || _autoMusic >= 0 || _autoMovie != null || _autoLine >= 0)
                _skipBoot = true;

            if (!_skipBoot)
            {
                if (_preview.Texture != null) _boot.SetLegalArt(_preview.Texture);
                _boot.SetMenuSheet(_menuSheet);
                // The language screen's advisor, with his pole and flag: entry 83 sub 10. Taken from the
                // models the browser already parsed rather than re-read, so there is one answer about
                // what is in the archive instead of two that can disagree.
                if (_models != null && _models.TryGet(AdvisorModel.Entry, AdvisorModel.SubMesh, out var advisor))
                    _boot.SetAdvisor(advisor, _models.Sheets);
                else
                    GD.PushWarning($"[tpw] advisor model (entry {AdvisorModel.Entry} sub {AdvisorModel.SubMesh}) not found among {_models?.MeshCount ?? -1} parsed meshes; the language screen falls back to the flat flag");
                // Ready, but waiting for the Start button unless the command line asked for the boot outright.
                if (_bootNow || _bootFrom != null) StartGame();
                else _startGame.Disabled = false;
            }

            // Show the first model as soon as the parse is done, so the window is never empty.
            if (_models != null && _models.Count > 0) _models.Show(0);
            if (_modelTourSpec != null && _models != null)
                _modelTour = System.Array.ConvertAll(_modelTourSpec,
                    t => !t.StartsWith("e") ? int.Parse(t)
                        : t.Contains('.') ? System.Math.Max(0, _models.IndexOf(int.Parse(t.Substring(1, t.IndexOf('.') - 1)), int.Parse(t.Substring(t.IndexOf('.') + 1))))
                        : System.Math.Max(0, _models.IndexOfEntry(int.Parse(t.Substring(1)))));
            if (_modelTour != null && _models != null && _models.Count > 0)
            {
                if (_tourCull) _models.ToggleCull();
                _debugLayer.Visible = false;
                _tourPos = 0;
                _models.Show(_modelTour[0]);
                if (_tourPlay) _models.PlayFromStart();
            }
            else if (_modelInfo != null) _modelInfo.Text = "Models: none parsed.";

            if (_views.Count > 0)
            {
                _next.Disabled = false;
                _next.Text = $"Next image  (1/{_views.Count}: {_views[0].Source})";
            }

            if (_sample != null && _sample.SampleCount > 0)
            {
                _playSound.Disabled = false;
                _playSound.Text = $"Play a random sound from your disc ({_sounds.Count} available)";
                GD.Print($"[tpw] decoded {_sounds.Count} waveforms; longest {_sample.SampleCount:n0} samples" +
                         $" ({_sample.SampleCount / (double)ConfirmedRateHz:0.00}s at {ConfirmedRateHz} Hz)");
            }
            else _playSound.Text = "No sound decoded";

            _musicChoice.Clear();
            foreach (var (entry, module, _) in _modules)
            {
                string use = entry == ParkWorlds.FrontEndMusic ? ", front end" : "";
                foreach (var pw in ParkWorlds.All) if (pw.Music == entry) use = $", {ParkWorlds.Describe(pw)} parks";
                _musicChoice.AddItem($"module #{entry} ({module.Channels} channels{use})");
            }
            _musicChoice.Disabled = _musicButton.Disabled = _modules.Count == 0;
            if (_autoMusic >= 0)
            {
                int mi = _modules.FindIndex(m => m.Entry == _autoMusic);
                if (mi >= 0) { _musicChoice.Selected = mi; _musicButton.ButtonPressed = true; }
            }

            _parkChoice.Clear();
            foreach (var (entry, _) in _maps)
                _parkChoice.AddItem($"map #{entry}, {ParkWorlds.Describe(ParkWorlds.ForMap(entry))}" + (entry == 203 ? " (tinyclaw's park)" : ""));
            _parkChoice.Disabled = _parkButton.Disabled = _maps.Count == 0;
            if ((_parkSavePath != null || _parkLoadPath != null || _parkSaveProof != null)
                && _maps.FindIndex(m => m.Entry == _autoPark) < 0)
            {
                FailParkSave(new System.ArgumentException($"Map {_autoPark} is not available on this disc."));
                return;
            }
            if (_autoPark >= 0)
            {
                int i = _maps.FindIndex(m => m.Entry == _autoPark);
                if (i >= 0)
                {
                    _parkChoice.Selected = i; _debugLayer.Visible = false; ShowPark(true);
                    // AFTER map/assets/finances exist, BEFORE hooks that edit the restored park.
                    try
                    {
                        _saveHost = CreateSaveHost();
                        if (_parkSaveProof == "read") ParkSaveProof.AssertFresh(_park, _saveHost);
                        if (_parkLoadPath != null)
                        {
                            ParkSaving.Load(System.IO.File.ReadAllBytes(_parkLoadPath), _saveHost.Layout, _saveHost);
                            GD.Print($"[tpw] --park-load {_parkLoadPath}: loaded");
                        }
                    }
                    catch (System.Exception e) { FailParkSave(e); return; }
                    if (_parkView != null && _parkView.Length == 5) _park.SetView(_parkView[0], _parkView[1], _parkView[2], _parkView[3], _parkView[4]);
                    if (_autoGameCam) _park.GameCamera = true;
// ⭐ PLACE BEFORE LAYING. A path run links to whatever is beside it AS IT IS LAID, so a
                    // path laid before the ride cannot link to the ride's door - the guests then
                    // choose the ride and can never reach it. Build, then connect, as a player does.
                    if (_autoPlace != null)
                        foreach (var pl in _autoPlace.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
                        {
                            var v = System.Array.ConvertAll(pl.Split(','), int.Parse);
                            if (v.Length == 4) GD.Print($"[tpw] --park-place {pl}: {(_park.PlaceAt(v[0], v[1], v[2], v[3]) ? "placed" : "refused")}");
                        }
                    if (_autoHire != null)
                        foreach (var hire in _autoHire.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
                        {
                            var v = System.Array.ConvertAll(hire.Split(','), int.Parse);
                            if (v.Length == 3) GD.Print($"[tpw] --park-hire {hire}: {(_park.Hire(v[0], v[1], v[2]) ? "hired" : "refused")}");
                        }
                    // ⚠ AFTER THE HIRES, because a topic is picked against the catalogue and the
                    // catalogue's tier rule counts what is already built and who is already employed.
                    if (_autoResearch != null)
                        foreach (var pick in _autoResearch.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
                        {
                            var v = System.Array.ConvertAll(pick.Split(','), int.Parse);
                            if (v.Length == 3) GD.Print($"[tpw] --park-research {pick}: {_park.StartResearch(v[0], v[1], v[2])}");
                        }
                    if (_autoLay != null)
                        foreach (var run in _autoLay.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
                        {
                            var v = System.Array.ConvertAll(run.Split(','), int.Parse);
                            if (v.Length == 4) GD.Print($"[tpw] --park-lay {run}: {_park.LayRun(v[0], v[1], v[2], v[3])} tiles took path");
                        }
                    if (_autoGhost != null)
                    {
                        var v = System.Array.ConvertAll(_autoGhost.Split(','), int.Parse);
                        if (v.Length == 4) _park.PinGhost(v[0], v[1], v[2], v[3]);
                    }
                    if (_autoPicker >= 0) _park.ShowPicker(_autoPicker);
                    if (_autoHover != null)
                    {
                        var v = System.Array.ConvertAll(_autoHover.Split(','), int.Parse);
                        if (v.Length >= 2) _park.PinHover(v[0], v[1]);
                        for (int hv = 2; hv + 1 < v.Length; hv += 2) _park.PinHoverAlso(v[hv], v[hv + 1]);
                    }
                    if (_autoTrack != null)
                    {
                        var parts = _autoTrack.Split(':');
                        var v = System.Array.ConvertAll(parts[0].Split(','), int.Parse);
                        var clicks = new System.Collections.Generic.List<(int X, int Z)>();
                        (int X, int Z)? hover = null;
                        for (int k = 1; k < parts.Length; k++)
                        {
                            if (parts[k] == "u") { clicks.Add((-1, -1)); continue; }
                            var c = System.Array.ConvertAll(parts[k].TrimStart('~').Split(','), int.Parse);
                            if (c.Length != 2) continue;
                            if (parts[k].StartsWith("~")) hover = (c[0], c[1]); else clicks.Add((c[0], c[1]));
                        }
                        if (v.Length == 4)
                            GD.Print($"[tpw] --park-track {_autoTrack}: {_park.TrackAt(v[0], v[1], v[2], v[3], clicks, hover)?.ToString() ?? "refused"}");
                    }

                    // ⚠ AFTER the placement flags, not before: selecting a tile is only meaningful once
                    // whatever --park-track put there exists. Sitting above them selected empty ground.
                    if (_autoSelect != null)
                    {
                        var v = System.Array.ConvertAll(_autoSelect.Split(','), int.Parse);
                        if (v.Length >= 2) GD.Print($"[panel] select {v[0]},{v[1]}: {(_park.SelectAttraction(v[0], v[1]) ? "selected" : "nothing there")}");
                    }
                    if (_autoContext != null)
                    {
                        var v = System.Array.ConvertAll(_autoContext.Split(','), int.Parse);
                        if (v.Length >= 2)
                            GD.Print($"[panel] context {v[0]},{v[1]}: {(_park.ShowContext(v[0], v[1], new Vector2(430, 250)) ? "open" : "nothing there")}");
                    }
                    if (_autoSlider != null)
                    {
                        var parts = _autoSlider.Split(',');
                        if (parts.Length == 2 && int.TryParse(parts[1], out int want))
                            GD.Print($"[panel] slider {parts[0]} := {want} -> {_park.MoveSlider(parts[0], want)?.ToString() ?? "no selection"}");
                    }

                    if (_autoQueue != null)
                    {
                        var parts = _autoQueue.Split(':');
                        var v = System.Array.ConvertAll(parts[0].Split(','), int.Parse);
                        var clicks = new System.Collections.Generic.List<(int X, int Z)>();
                        (int X, int Z)? hover = null;
                        for (int k = 1; k < parts.Length; k++)
                        {
                            // "~x,z": hold the pointer there without pressing (the last one wins); "u": the undo.
                            if (parts[k] == "u") { clicks.Add((-1, -1)); continue; }
                            bool held = parts[k].StartsWith("~");
                            var c = System.Array.ConvertAll(parts[k].TrimStart('~').Split(','), int.Parse);
                            if (c.Length != 2) continue;
                            if (held) hover = (c[0], c[1]); else clicks.Add((c[0], c[1]));
                        }
                        if (v.Length == 4) GD.Print($"[tpw] --park-queue {_autoQueue}: {_park.QueueAt(v[0], v[1], v[2], v[3], clicks, hover)?.ToString() ?? "not placed"}");
                    }
                    // ⚠⚠ ALL THREE OF THESE RUN AFTER --park-place, --park-lay AND --park-queue, because every
                    // one of them EDITS what those built. Ordered before the queue, --park-delete reported
                    // "nothing there" and --park-editqueue reported -1 — both of which read like a broken
                    // command rather than a command run too early. Twice, on two different hooks.
                    // --advisor-say=ID[;ID...]: hand the advisor a message directly.
                    // ⚠ THIS IS A WAY IN, NOT A SHORTCUT PAST THE MACHINE. It posts to the same queue the
                    // rules post to and nothing else, so what follows -- the dedupe, the 50-tick arrival,
                    // the caption, the take rotation, the statistics freezing while he talks -- is the real
                    // path. It exists because the rules that fire early are rare: proving the port's wiring
                    // by waiting for one means rendering tens of thousands of frames.
                    if (_autoSay != null)
                        foreach (var id in _autoSay.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
                            if (ushort.TryParse(id, out ushort said))
                            {
                                _park.Advisor.Post(said);
                                GD.Print($"[tpw] --advisor-say {said}: queued");
                            }
                    if (_autoDelete != null)
                        foreach (var del in _autoDelete.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
                        {
                            var v = System.Array.ConvertAll(del.Split(','), int.Parse);
                            if (v.Length == 2) GD.Print($"[tpw] --park-delete {del}: {_park.DeleteAt(v[0], v[1])}");
                        }
                    if (_autoReplace != null)
                        foreach (var pl in _autoReplace.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
                        {
                            var v = System.Array.ConvertAll(pl.Split(','), int.Parse);
                            if (v.Length == 4) GD.Print($"[tpw] --park-replace {pl}: {(_park.PlaceAt(v[0], v[1], v[2], v[3]) ? "placed" : "refused")}");
                        }
                    if (_autoEditQueue != null)
                    {
                        foreach (var eq in _autoEditQueue.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
                        {
                            var v = System.Array.ConvertAll(eq.Split(','), int.Parse);
                            if (v.Length == 2) GD.Print($"[tpw] --park-editqueue {eq}: {_park.EditQueueAt(v[0], v[1])}");
                        }
                    }
                    // LAST, so it reports the state everything else left behind.
                    if (_autoTile != null)
                    {
                        foreach (var tl in _autoTile.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
                        {
                            var v = System.Array.ConvertAll(tl.Split(','), int.Parse);
                            if (v.Length == 2) GD.Print($"[tpw] --park-tile {tl}: {_park.TileAt(v[0], v[1])}");
                        }
                    }
                    // Last, because the ride it names may have been placed by --park-queue.
                    if (_autoSeats) GD.Print("[tpw] seat table:" + _park.SeatTable());
                    if (_autoPrice != null)
                        foreach (var one in _autoPrice.Split(';'))
                        {
                            var pp = one.Split(',');
                            bool priced = _park.SetStallPrice(int.Parse(pp[0]), int.Parse(pp[1]));
                            GD.Print($"[tpw] --park-price {one}: {(priced ? "set" : "no such attraction")}");
                        }
                    // --park-request-upgrade=ENTRY: the PANEL's half. Unlike --park-upgrade it buys
                    // nothing; it queues work and a mechanic has to walk over and do it, which is the
                    // chain worth testing because both of its ends used to be stubs.
                    if (_researchAll) _park.ResearchAll = true;
                    if (_autoRequestUpgrade >= 0)
                        GD.Print($"[tpw] --park-request-upgrade {_autoRequestUpgrade}: "
                               + _park.RequestUpgrade(_autoRequestUpgrade));
                    if (_autoUpgrade >= 0)
                    {
                        var before = _finances?.Bank.Balance ?? Money.Zero;
                        int lvl = _park.UpgradeNow(_autoUpgrade);
                        GD.Print($"[tpw] --park-upgrade {_autoUpgrade}: level {lvl}, "
                               + $"bank {before} -> {_finances?.Bank.Balance}");
                    }
                    if (_autoBreak >= 0)
                    {
                        if (_autoBreakAt < 0)
                            GD.Print($"[tpw] --park-break {_autoBreak}: {(_park.Break(_autoBreak, _autoBreakHard) ? "broken" : "refused")}");
                    }
                    if (_autoPathCursor != null)
                    {
                        var v = System.Array.ConvertAll(_autoPathCursor.Split(','), int.Parse);
                        if (v.Length >= 2) _park.PinPathCursor(v[0], v[1], v.Length >= 4 ? v[2] : -1, v.Length >= 4 ? v[3] : -1);
                    }
                    // LAST: save sees placement, paths, queue, hires, prices, upgrades and breaks.
                    try
                    {
                        if (_parkSaveProof == "write") ParkSaveProof.Seed(_park, _saveHost);
                        else if (_parkSaveProof == "read") ParkSaveProof.Check(_park, _saveHost);
                        else if (_parkSaveProof != null) throw new System.ArgumentException("--park-save-proof must be write or read.");
                        if (_parkSavePath != null && _parkSaveAfter <= 0) SaveParkFile();
                        if (_parkSaveProof != null) GetTree().Quit();
                    }
                    catch (System.Exception e) { FailParkSave(e); return; }
                }
            }

            _playAdvisor.Disabled = !_hasAdvisor;
            if (!_hasAdvisor) _playAdvisor.Text = "No advisor speech on this disc";
            else if (_autoLine >= 0) PlayAdvisorLine(_autoLine, _autoLanguage);

            _movieChoice.Clear();
            foreach (var name in _movieFiles) _movieChoice.AddItem($"{name}  —  {StrMovie.Describe(name)}");
            _movieChoice.Disabled = _playMovie.Disabled = _movieFiles.Count == 0;
            if (_movieFiles.Count == 0) _playMovie.Text = "No movies on this disc";
            else
            {
                int auto = _autoMovie == null ? -1 : _movieFiles.IndexOf(_autoMovie);
                _movieChoice.Selected = auto >= 0 ? auto : 0;
                if (auto >= 0) PlayMovie();
                else if (_autoMovie != null) GD.PushWarning($"[tpw] --movie={_autoMovie}: not on this disc");
            }
        }

        void PlayMovie()
        {
            if (_movieFiles.Count == 0) return;
            int sel = _movieChoice.Selected;
            if (sel < 0 || sel >= _movieFiles.Count) return;
            PlayMovieNamed(_movieFiles[sel]);
        }

        /// <summary>Load and play one movie by file name. The boot chain and the debug button go
        /// through the same loader on purpose -- a second copy is a second thing to keep correct.</summary>
        void PlayMovieNamed(string name)
        {
            if (_player.IsPlaying) return;
            string path = _data.SourcePath;
            _playMovie.Disabled = true;
            _playMovie.Text = $"Loading {name}…";
            _audio.Stop();

            // ⚠ OFF THE MAIN THREAD. A movie is up to 16 MB of raw sectors, and its whole soundtrack is decoded
            // before the first frame shows. That takes long enough on a cold disc cache to freeze the window.
            // Its own DiscReader: the reader shares one stream position, so it must not be used from two threads.
            Task.Run(() =>
            {
                StrMovie m = null;
                string err = null;
                try
                {
                    using var disc = DiscReader.Open(path);
                    var f = disc.Find(name);
                    if (f == null) err = $"{name} is not on this disc";
                    else StrMovie.TryLoad(disc, f, out m, out err);
                }
                catch (System.Exception e) { err = e.Message; }
                _pendingMovie = m;
                _pendingMovieError = err;
                CallDeferred(nameof(StartPendingMovie));
            });
        }

        /// <summary>The front-end music: FOLIO entry 299, the module the game plays on its menus.</summary>
        void PlayFrontEndMusic()
        {
            for (int i = 0; i < _modules.Count; i++)
                if (_modules[i].Entry == ParkWorlds.FrontEndMusic)
                {
                    var (entry, module, waves) = _modules[i];
                    _music.Play(module, waves, $"module #{entry}");
                    return;
                }
            GD.PushWarning("[tpw] front-end music (entry 299) is not on this disc");
        }

        void PlayMusic(bool on)
        {
            if (!on || _modules.Count == 0) { _music.Stop(); return; }
            int sel = System.Math.Clamp(_musicChoice.Selected, 0, _modules.Count - 1);
            var (entry, module, waves) = _modules[sel];
            _music.Play(module, waves, $"module #{entry}");
        }

        /// <summary>⭐ THE DEBUG MENU IS NOT PART OF THE GAME, so opening the park closes it (master's rule).
        /// F3 brings it back. It is closed rather than hidden-and-restored: coming out of the park should
        /// leave you where the game leaves you, not put the developer chrome back over it.</summary>
        void ShowPark(bool on)
        {
            if (on) SetDebugMenu(false);
            if (on && _maps.Count > 0)
            {
                int sel = System.Math.Clamp(_parkChoice.Selected, 0, _maps.Count - 1);
                var (entry, map) = _maps[sel];
                var world = ParkWorlds.ForMap(entry);
                TextureSheet ground = null;
                SceneryPack scenery = null;
                if (world != null) { _groundSheets.TryGetValue(world.GroundSheet, out ground); _sceneryPacks.TryGetValue(world.SceneryEntry, out scenery); }
                SceneryPack gateModels = null;
                var gateInfo = world != null ? ParkGate.ForWorld(world.Index) : null;
                if (gateInfo != null) _gatePacks.TryGetValue(gateInfo.PackEntry, out gateModels);
                _park.SetCatalogue(_exe, _strings);
                _park.SetAttractions(world != null && _attractionsByWorld.TryGetValue(world.Index, out var al) ? al : null,
                                     ae => _models != null && _models.TryGet(ae, 0, out var am) ? am : null, _models?.Sheets,
                                     (ae, sub) => _models != null && _models.TryGet(ae, sub, out var sm) ? sm : null);
                // ⚠ BEFORE THE LOAD, NOT AT CONSTRUCTION. The command line is not parsed until long
                // after `new ParkView()`, so setting this there left the control permanently off — and
                // a --park-nogate run reported the gate's own numbers back at me, which is exactly what
                // a working control would have made impossible. The check is that it prints "no gate".
                _park.NoGate = _noGate;
                // ⚠ BEFORE the park loads, like NoGate: ParkGuests is rebuilt by ShowPark, so a flag
                // set afterwards is a control that never arms — which has already cost a whole
                // measurement on this project once.
                _park.Preflight = !_noPreflight;
                _park.Load(map, $"map #{entry}", ground, world, scenery, _commonSheet, gateModels, _exe, _busPack);
                _park.SetToolSounds(_toolSounds, _parkSounds, _uiSounds, _guestSounds);
                // The advisor's voice, in the language the front end picked. ⚠ AFTER Load, like NoGate
                // above: Load builds the advisor's world, and a delegate set before it would be dropped.
                _advisorVoice.DiscPath = _hasAdvisor ? _data.SourcePath : null;
                _advisorVoice.Language = _language;
                _park.AdvisorSpeak = _advisorVoice.Speak;
                _park.AdvisorStop = _advisorVoice.Stop;
                _park.AdvisorVoicePlaying = () => _advisorVoice.Playing;
                _park.SetHud(_commonSheet, _exe, _strings);
                _park.SetBuildRig(v => _models != null && _models.TryGet(3, v, out var rig) ? rig : null);
                _park.LogRides = _logRides;
                _park.ForcedGuests = _forcedGuests;
                _park.SetBank(_finances.Bank);
                if (_autoOpen) _park.ParkOpen = true;
                if (_autoBuildable) _park.ShowBuildable = true;
                // ⭐ Entering a park starts its world's music, looping, as 0x80058694 does in the game.
                int mi = world != null ? _modules.FindIndex(m => m.Entry == world.Music) : -1;
                if (mi >= 0 && _autoMusic < 0)
                {
                    _musicChoice.Selected = mi;
                    _musicButton.SetPressedNoSignal(true);
                    PlayMusic(true);
                    _parkStartedMusic = true;
                }
            }
            _park.Activate(on && _park.HasMap);
            _models.Activate(!(on && _park.HasMap));
            if (!on)
            {
                _parkInfo.Text = "";
                // Leaving takes the park's music with it; music picked by hand is left alone.
                if (_parkStartedMusic) { _music.Stop(); _musicButton.SetPressedNoSignal(false); _parkStartedMusic = false; }
            }
        }

        void ResetParkFinances()
        {
            _finances = new ParkFinances(ParkEconomy.OpeningBalance, () =>
            {
                var cal = _finances.Calendar;
                int len = cal.LengthOfMonthJustEnded > 0 ? cal.LengthOfMonthJustEnded : cal.CurrentMonthLength;
                return _park?.StaffWages(cal.TotalDays - 1, len) ?? Money.Zero;
            });
            _park?.SetBank(_finances.Bank);
            _park?.SetFinances(_finances);
            _clock = new ParkClock(); _accum = 0;
        }

        ParkSaveHost CreateSaveHost() => new(_park,
            _park.SaveLayout(_maps[_parkChoice.Selected].Entry), ResetParkFinances,
            tick => { _clock = ParkClock.At(tick); _accum = 0; });

        void SaveParkFile()
        {
            byte[] bytes = ParkSaving.Save(_saveHost ??= CreateSaveHost());
            // Serialize completely before replacing the file; a refused capture cannot truncate it.
            string path = System.IO.Path.GetFullPath(_parkSavePath);
            string temporary = path + "." + System.Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                System.IO.File.WriteAllBytes(temporary, bytes);
                System.IO.File.Move(temporary, path, overwrite: true);
            }
            finally { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); }
            GD.Print($"[tpw] --park-save {path}: {bytes.Length} bytes");
            _parkSavePath = null;
        }

        void FailParkSave(System.Exception error)
        {
            GD.PrintErr($"[park-save] FAILED: {error}");
            GetTree().Quit(1);
        }

        void PlayAdvisorLine() => PlayAdvisorLine(-1, _advisorLanguage.Selected);

        /// <summary>Play a line in a language, or a random audible line when <paramref name="line"/> is -1.</summary>
        void PlayAdvisorLine(int line, int language)
        {
            if (_player.IsPlaying) return;
            string path = _data.SourcePath;
            _playAdvisor.Disabled = true;
            _playAdvisor.Text = "Loading…";

            // ⚠ OFF THE MAIN THREAD. The first press reads the game's index of lines out of the archive, the way
            // the game finds them; later presses reuse it and read only the one line.
            Task.Run(() =>
            {
                PcmSample pcm = null;
                XaAudio.Coding coding = default;
                string err = null;
                try
                {
                    using var disc = DiscReader.Open(path);
                    var f = disc.Find(AdvisorSpeech.File);
                    var lines = _advisorLines;
                    if (lines == null)
                    {
                        var af = disc.Find(AssetSelfTest.AssetArchive);
                        if (af != null && GazArchive.TryParse(disc.ReadFile(af), out var gaz, out err)
                            && AdvisorSpeech.TryReadIndex(gaz, out var found, out err))
                            lines = _advisorLines = found;
                    }
                    if (lines != null)
                    {
                        int pick = line >= 0 ? line : System.Random.Shared.Next(lines.Count);
                        // The last choice in the list is "any language".
                        int lang = language >= 0 && language < AdvisorSpeech.Languages
                            ? language : System.Random.Shared.Next(AdvisorSpeech.Languages);
                        if (pick >= lines.Count) err = $"there is no line {pick}; the index has {lines.Count}";
                        else AdvisorSpeech.TryDecodeLine(disc, f, lines[pick], lang, out pcm, out coding, out err);
                    }
                }
                catch (System.Exception e) { err = e.Message; }
                _pendingLine = pcm;
                _pendingLineCoding = coding;
                _pendingLineError = err;
                CallDeferred(nameof(PlayPendingLine));
            });
        }

        void PlayPendingLine()
        {
            _playAdvisor.Disabled = false;
            var pcm = _pendingLine;
            _pendingLine = null;
            if (pcm == null || pcm.SampleCount == 0)
            {
                _playAdvisor.Text = "Play an advisor line";
                GD.PushWarning($"[tpw] advisor line failed: {_pendingLineError}");
                if (_quitAfterLine) GetTree().Quit(1);
                return;
            }
            var bytes = new byte[pcm.SampleCount * 2];
            System.Buffer.BlockCopy(pcm.Samples, 0, bytes, 0, bytes.Length);
            _audio.Stream = new AudioStreamWav
            {
                Format = AudioStreamWav.FormatEnum.Format16Bits,
                MixRate = _pendingLineCoding.SampleRate,
                Stereo = _pendingLineCoding.Stereo,
                Data = bytes,
            };
            _audio.Play();
            if (_quitAfterLine) _audio.Finished += () => GetTree().Quit();
            double secs = pcm.SampleCount / (double)_pendingLineCoding.Channels / _pendingLineCoding.SampleRate;
            _playAdvisor.Text = $"Play an advisor line ({_advisorLines?.Count ?? 0} lines × {AdvisorSpeech.Languages} languages)" +
                                $"  —  now: {pcm.Source}, {secs:0.0}s";
            GD.Print($"[tpw] playing {pcm.Source}: {_pendingLineCoding}, {secs:0.00}s");
        }

        void StartPendingMovie()
        {
            var m = _pendingMovie;
            _pendingMovie = null;
            if (m == null)
            {
                _playMovie.Disabled = false;
                _playMovie.Text = "Play movie";
                GD.PushWarning($"[tpw] could not load the movie: {_pendingMovieError}");
                if (_quitAfterMovie) GetTree().Quit(1);
                OnMovieUnavailable();
                return;
            }
            if (m.AudioError != null) GD.PushWarning($"[tpw] {m.Name}: soundtrack stopped decoding: {m.AudioError}");
            _player.Play(m);
        }

        void OnMovieFinished(bool completed)
        {
            _playMovie.Disabled = false;
            _playMovie.Text = "Play movie";
            if (_quitAfterMovie) GetTree().Quit();
            // ⚠ BOTH OUTCOMES. A skipped movie has to advance the boot exactly as a finished one does;
            // waiting only for `completed` leaves the chain stuck on a black screen the moment anyone
            // presses a button, which is the common case rather than the edge case.
            _boot?.MovieEnded();
        }

        /// <summary>A movie that could not be LOADED must not strand the boot either. The disc may be a
        /// cooked rip, in which case every .STR fails to decode and the chain would sit forever on a
        /// screen that never starts.</summary>
        void OnMovieUnavailable() => _boot?.MovieEnded();

        /// <summary>Every waveform on the disc, decoded to PCM, longest first.
        ///
        /// ⚠ IT USED TO TAKE THE FIRST NON-EMPTY ONE, WHICH IS A BUG YOU CANNOT SEE IN THE SOURCE. That reads
        /// as perfectly sensible and picks a 64-byte waveform: 112 samples, **13 milliseconds**, an inaudible
        /// click. The first run of this program is what exposed it — the log said "decoded 112 PCM samples"
        /// and the number was too small to be a sound. Nothing about the code looked wrong, and no unit test
        /// would have failed, because the behaviour was exactly what was written.</summary>
        static System.Collections.Generic.List<PcmSample> AllSounds(DiscReader disc)
        {
            var all = new System.Collections.Generic.List<PcmSample>();
            var f = disc.Find(AssetSelfTest.AssetArchive);
            if (f == null || !GazArchive.TryParse(disc.ReadFile(f), out var gaz, out _)) return all;

            foreach (var e in gaz.Entries)
            {
                if (e.Size != VabHeader.SplitHeaderSize) continue;
                if (!VabHeader.TryParse(gaz.Read(e), out var vab, out _)) continue;
                int bodyIndex = e.Index - 1;   // ⚠ BEFORE, not after — see VabHeader
                if (bodyIndex < 0 || gaz.Entries[bodyIndex].Size != vab.BodyBytes) continue;
                int n = 0;
                foreach (var wave in vab.SliceBody(gaz.Read(gaz.Entries[bodyIndex])))
                {
                    n++;
                    if (wave.Length == 0) continue;
                    var pcm = Vag.Decode(wave, $"bank #{e.Index} waveform {n}");
                    if (pcm.SampleCount > 0) all.Add(pcm);
                }
            }
            // Longest first, so the label and the very first press show something clearly audible rather
            // than whichever 13-millisecond click happens to come first in the archive.
            all.Sort((x, y) => y.SampleCount.CompareTo(x.SampleCount));
            return all;
        }

        /// <summary>Every block of every sprite bank, as separate images. One bank is 1024 pixels tall, which
        /// is unreadable in a preview; a block is 256x64 and is the unit the hardware uploads anyway.</summary>
        static System.Collections.Generic.List<TpwImage> SpriteBlocks(DiscReader disc)
        {
            var outp = new System.Collections.Generic.List<TpwImage>();
            var f = disc.Find(AssetSelfTest.AssetArchive);
            if (f == null || !GazArchive.TryParse(disc.ReadFile(f), out var gaz, out _)) return outp;
            foreach (var (entry, bank) in SpriteBank.DecodeAll(gaz))
                for (int b = 0; b < SpriteBank.Blocks; b++)
                {
                    var blk = SpriteBank.Block(bank, b);
                    if (blk != null) outp.Add(blk);
                }
            return outp;
        }

        /// <summary>Every texture sheet, in colour: each sprite drawn with the palette its own table names.
        ///
        /// ⭐ THE FIRST TEXTURES IN THIS PORT WITH THEIR REAL COLOURS. Everything before was a grey ramp over
        /// palette indices, because nothing said which palette went with which rectangle. The sheets say it
        /// themselves: every sprite record names its CLUT, and the CLUT is inside the sheet's own pixels.</summary>
        static System.Collections.Generic.List<TpwImage> TextureSheets(DiscReader disc)
        {
            var outp = new System.Collections.Generic.List<TpwImage>();
            var f = disc.Find(AssetSelfTest.AssetArchive);
            if (f == null || !GazArchive.TryParse(disc.ReadFile(f), out var gaz, out _)) return outp;
            foreach (var (entry, sheet) in TextureSheet.FindAll(gaz))
                outp.Add(sheet.RenderSprites($"texture sheet #{entry.Index} ({sheet.Columns}x{sheet.Rows} pages, " +
                                             $"{sheet.Sprites.Count} sprites{(sheet.Compressed ? ", compressed" : "")})"));
            return outp;
        }

        void ShowNext()
        {
            if (_views.Count == 0) return;
            _view = (_view + 1) % _views.Count;
            var v = _views[_view];
            var image = Image.CreateFromData(v.Width, v.Height, false, Image.Format.Rgba8, v.Rgba);
            _preview.Texture = ImageTexture.CreateFromImage(image);
            _next.Text = $"Next image  ({_view + 1}/{_views.Count}: {v.Source})";
            GD.Print($"[tpw] showing {v.Source} ({v.Width}x{v.Height})");
        }

        void PlaySample()
        {
            // Master: "make it play a random sound. the one you chose isnt great for figuring out if its
            // right." A single fixed sample tells you it decodes; a different one each press is what tells
            // you the BANK is being read correctly rather than one lucky entry.
            if (_sounds.Count > 0) _sample = _sounds[_rng.Next(_sounds.Count)];
            if (_sample == null || _sample.SampleCount == 0) return;
            int hz = CurrentRate;
            GD.Print($"[tpw] playing {_sample.Source}: {_sample.SampleCount:n0} samples" +
                     $" ({_sample.SampleCount / (double)hz:0.00}s at {hz} Hz)");

            // PCM16 little-endian, which is what AudioStreamWav.Format16Bits expects.
            var bytes = new byte[_sample.SampleCount * 2];
            System.Buffer.BlockCopy(_sample.Samples, 0, bytes, 0, bytes.Length);

            _audio.Stream = new AudioStreamWav
            {
                Format = AudioStreamWav.FormatEnum.Format16Bits,
                MixRate = hz,
                Stereo = false,
                Data = bytes,
            };
            _audio.Play();
        }

        /// <summary>Base playback rate. ⚠ DERIVED from the music, not measured — but it is evidence, and it
        /// replaced a guess that was wrong by a factor of 2.6.
        ///
        /// VAG ADPCM carries no sample rate, so the first version of this was 22,050 Hz "because that is a
        /// common PSX rate". That is not a reason. The answer turned out to be in bytes already in hand: the
        /// XM modules keep their instrument headers, with sample length ZEROED — the waveform data is stripped
        /// because it lives in the VAG bank — while **relative note and finetune survive intact**. Across
        /// module #296's 26 instruments those read 0 or -12 with finetune 0, and the XM convention
        /// `rate = 8363 * 2^((relative + finetune/128) / 12)` turns those into **8363 Hz** and **4182 Hz**.
        ///
        /// ✅ AND THE SECOND WORRY IS CLOSED: the VAB tone attributes CANNOT disagree, because they say
        /// nothing. Parsed across all 9 banks, every one of the 16 tone slots is byte-identical boilerplate —
        /// `centre=60, fine=0, vol=127, pan=64, min=0, max=127` — and **every tone points at waveform index 1**
        /// while the banks hold 6 to 28 distinct waveforms. Each declares `tones=1`. That is a default-filled
        /// template, not an instrument map: the VAB is being used as a bare sample container and the game's
        /// own XM player addresses waveforms directly. So the tracker side is the authority on pitch by
        /// elimination, not by preference — which is worth more than picking it because it was convenient.
        ///
        /// ⚠ ONE REASON IT IS STILL NOT SETTLED. The rate is PER-INSTRUMENT, not global: half are an octave
        /// down, so any single constant is wrong for those by design. This wants to become a per-waveform rate
        /// once the XM-instrument to VAG-index mapping is wired.
        ///
        /// Superseded by the selector above: master listened at 8363 and it plays too slow. Kept as the
        /// documented lower bound of the range worth trying.</summary>
        const int TrackerBaseHz = 8363;

        int CurrentRate => _rate != null && _rate.Selected >= 0 && _rate.Selected < RateChoices.Length
            ? RateChoices[_rate.Selected] : ConfirmedRateHz;

        /// <summary>Run the boot chain from the start (or from <c>--boot-from=</c>'s screen), over the debug menu.</summary>
        void StartGame()
        {
            if (_skipBoot || _bootStarted) return;
            _bootStarted = true;
            _startGame.Disabled = true;
            _startGame.Text = "The game is running (F3 hides this menu)";
            _boot.ProcessMode = ProcessModeEnum.Inherit;
            if (_bootFrom != null && !_boot.StartAt(_bootFrom))
                GD.PushWarning($"[tpw] --boot-from={_bootFrom} is not a boot screen; starting from the beginning");
            _boot.Visible = true;
            _debugLayer.Visible = false;       // the debug chrome stays available on F3
        }

        /// <summary>F3 opens and closes the debug menu.
        ///
        /// ⚠ _UnhandledInput, NOT _Input. MoviePlayer takes _Input and marks the event handled to skip a
        /// playing movie; if this were _Input too, F3 during a movie would both skip it and toggle the
        /// panel, and which happened first would depend on node order. Unhandled input runs only after
        /// nobody claimed the key, which is exactly the rule wanted here.
        ///
        /// Note this toggles the PANEL, not _root: a capture hides _root, so F3 cannot bring the debug
        /// chrome back into a recording that is already running.</summary>
        public override void _UnhandledInput(InputEvent e)
        {
            if (e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 }) return;
            if (_debugLayer == null) return;
            SetDebugMenu(!_debugLayer.Visible);
            GetViewport().SetInputAsHandled();
        }

        /// <summary>Show or hide the debug menu: F3, the menu's own Hide button, or the corner button.</summary>
        void SetDebugMenu(bool on)
        {
            if (_debugLayer == null) return;
            _debugLayer.Visible = on;
            if (on && _menuTab != null) { _menuTab.Visible = false; _menuTabShown = -1; }
        }

        /// <summary>Mouse movement while the menu is hidden brings the corner button up for a moment. _Input, and
        /// never marked handled, so the game and the park view still get every motion.</summary>
        public override void _Input(InputEvent e)
        {
            if (e is InputEventMouseMotion && _menuTab != null && _debugLayer != null && !_debugLayer.Visible)
            {
                _menuTab.Visible = true;
                _menuTab.Modulate = Colors.White;
                _menuTabShown = 0;
            }
        }

        bool _noPreflight;
        /// <summary>--park-pelt=FRAME. ⚠ A FRAME, NOT LOAD TIME: guests are populated per tick, so a
        /// pelt at park-load finds an empty park and reports "no guests" — which is exactly what the
        /// first version did, and it looked like the flag was broken rather than early.</summary>
        int _autoPeltAt = -1;
        string _autoResearch;
        string _shotTarget;
        void TakeShot()
        {
            RenderingServer.ForceDraw();
            var img = GetViewport().GetTexture().GetImage();
            string path = _shotTarget;
            if (path != null && img != null)
            {
                img.SavePng(path);
                GD.Print($"[tpw] wrote {path} ({img.GetWidth()}x{img.GetHeight()})");
                // Say what was in the picture. A shot of a park is evidence about the park, and the
                // counts are the half of it a screenshot cannot show.
                if (_park != null && _park.HasMap) GD.Print("[tpw] " + _park.GuestReport());
                if (_finances != null)
                    GD.Print($"[tpw] money: balance {_finances.Bank.Balance}, {_finances.MonthsRun} month ends run"
                           + (_finances.LastMonthEnd is { } me ? $", last one billed {me.Wages} in wages" : ", none yet"));
            }
            GetTree().Quit();
        }

        public override void _Process(double delta)
        {
            // The corner menu button fades out a moment after the mouse stops, unless the pointer is on it.
            if (_menuTabShown >= 0 && _menuTab != null)
            {
                _menuTabShown = _menuTab.IsHovered() ? 0 : _menuTabShown + delta;
                if (_debugLayer.Visible || _menuTabShown > MenuTabSeconds) { _menuTab.Visible = false; _menuTabShown = -1; }
                else if (_menuTabShown > MenuTabSeconds - 0.5)
                    _menuTab.Modulate = new Color(1, 1, 1, (float)((MenuTabSeconds - _menuTabShown) / 0.5));
            }
            // ⚠ DON'T COUNT FRAMES THE PARK ISN'T UP FOR. The asset self-test runs first and takes as long as
            // the box feels like; counting through it shot the menu twice and looked like the park had failed
            // to draw. With --park, the clock starts when the park is actually on screen.
            bool parkUp = _autoPark < 0 || (_park?.Visible ?? false);
            // ⭐ BREAK IT WITH PEOPLE ON IT. The shot clock is the only frame counter that waits for the
            // park to be up, so the delayed break rides on it rather than on a second one that would
            // count through the asset self-test.
            // ⚠ ON THE SHOT CLOCK, like the delayed break — it is the only counter that waits for the
            // park to be up, so this does not count through the asset self-test.
            if (_autoPeltAt >= 0 && parkUp && _park != null && _shotClock >= _autoPeltAt)
            {
                GD.Print($"[tpw] --park-pelt@{_autoPeltAt}: {_park.Pelt()}");
                _autoPeltAt = -1;
            }
            if (_autoBreakAt >= 0 && parkUp && _park != null && _shotClock >= _autoBreakAt)
            {
                GD.Print($"[tpw] --park-break {_autoBreak}@{_autoBreakAt}: {(_park.Break(_autoBreak, _autoBreakHard) ? "broken" : "refused")}");
                _autoBreakAt = -1;
            }
            // AFTER delayed edits too. Optional delay gives guests time to enter, independently of
            // the screenshot clock; saving and breaking on the same frame captures the broken ride.
            if (_parkSavePath != null && _parkSaveAfter > 0 && _saveHost != null && _park.Visible
                && ++_parkSaveFrames >= _parkSaveAfter)
            {
                try { SaveParkFile(); }
                catch (System.Exception e) { FailParkSave(e); return; }
            }
            if (_shotPath != null && parkUp && (++_shotClock >= _shotFrame
                                                || (_shotWhenRunning && _park != null && _park.AnyRideRunning
                                                    && _shotHold-- <= 0)))
            {
                // ⚠ Wait for the frame to be DRAWN before reading it back. Grabbing the texture inside
                // _Process reads the previous frame at best and an empty one at worst, which looks
                // exactly like the thing you were checking having failed to render.
                _shotTarget = _shotPath; _shotPath = null;
                CallDeferred(nameof(TakeShot));
            }

            // ⚠ NOT frame-delta driven. The sim advances in whole ticks -- see ParkClock -- because a sim that
            // integrates on frame time inherits the frame rate into its economy, which is a bug unturnedGD
            // still carries in its weather. Accumulate real time and spend it in whole ticks.
            _accum += delta;
            double tickSeconds = _data.Variant?.TickSeconds ?? ParkClock.TickSeconds;
            // The calendar advances on the DAY edge, which OnTick tests for -- see ParkFinances. It is
            // inside this loop and not outside it because spending several ticks in one frame must roll
            // several days, and a check after the loop would see only the last one.
            while (_accum >= tickSeconds)
            {
                _accum -= tickSeconds;
                _clock.Advance();
                // ⚠ THE BALANCE IS READ BEFORE THE CHARGES, because OnTick applies them and the
                // month's history row wants what the park had when the month ENDED, not what was left
                // after the wages came out. Reading it after would bank a number a month too late.
                var before = _finances.Bank.Balance;
                var monthEnd = _finances.OnTick(_clock);
                // ⭐⭐ THE DAY BOUNDARY, WHICH NOTHING WAS DRIVING. ParkScore.RecordMonthEnd and
                // ParkObjectives.AfterDay were both merged today with no caller at all, so the park's
                // rating, its yearly figures, its twelve-year history and every goal it could ever
                // meet were computed from a machine nobody started.
                if (_park != null && _park.HasMap && _park.RollDay(monthEnd, before) is { } awarded)
                    GD.Print($"[tpw] objectives: {awarded}");
            }
            // The park HUD's balance and date (0x800390C8 / 0x80039008: the day and month shown 1-based, the year 2000 on).
            _park?.SetHudStatus(_finances.Bank.Balance.Pounds, _finances.Calendar.Day + 1, _finances.Calendar.Month + 1,
                                _finances.Calendar.Year + 2000);

            if (_tourPos >= 0 && ++_tourFrames >= _tourHold)
            {
                _tourFrames = 0;
                if (++_tourPos >= _modelTour.Length) { _tourPos = -1; GetTree().Quit(); }
                else
                {
                    _models.Show(_modelTour[_tourPos]);
                    if (_tourPlay) _models.PlayFromStart();
                }
            }

            _status.Text = _data.CanPlay
                ? $"{_data.Variant.Name} ({_data.Variant.Region})\n{_clock}\ntick = {tickSeconds:0.####}s"
                  + $"\nyear {_finances.Calendar.Year}  month {_finances.Calendar.Month + 1}  day {_finances.Calendar.Day + 1}"
                  + $"   balance {_finances.Bank.Balance}   months run {_finances.MonthsRun}"
                : $"No playable game data.\n{_data.Message}";
        }
    }
}
