using System;
using Godot;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>The cold boot, on screen: legal screen, the two movies, the language ring, the loading
    /// screen and the main menu, in the order and at the durations measured off the console
    /// (findings/boot.md, encoded in <see cref="BootSequence"/>).
    ///
    /// ⚠ THREE OF THESE SCREENS HAVE NO ART YET and are drawn as labelled placeholders: the language
    /// ring, the "now loading" jester, and the menu's backdrop. They are marked ON SCREEN as
    /// placeholders rather than styled to look finished, because a port that draws its own idea of a
    /// menu is indistinguishable from one that found the real one until somebody compares screenshots.
    /// The menu's BEHAVIOUR is real -- navigation, wrapping, what each item opens -- only its art is
    /// missing.</summary>
    public partial class BootScreens : CanvasLayer
    {
        /// <summary>Play this movie file off the disc. The caller answers with <see cref="MovieEnded"/>.</summary>
        public event Action<string> WantMovie;
        /// <summary>Start the front-end music (FOLIO entry 299).</summary>
        public event Action WantMenuMusic;
        /// <summary>The player chose a language; the argument is the GAME's language index, not the
        /// ring position. See <see cref="LanguageRing"/> for why that distinction has its own type.</summary>
        public event Action<int> LanguageChosen;
        /// <summary>The player asked to start the Practice Park.</summary>
        public event Action StartPracticePark;
        /// <summary>Music and SFX volume, 0..1, from the Options sliders.</summary>
        public event Action<float, float> VolumeChanged;

        readonly BootSequence _boot = new();
        readonly MainMenu _menu = new();
        int _ring = LanguageRing.RestPosition;

        ColorRect _bg;
        TextureRect _legal;
        /// <summary>The real menu, drawn from sheet 84 into a 320x256 buffer and scaled up whole. Kept
        /// at the PSX framebuffer size deliberately -- the art was authored for 320x256, and letting
        /// Godot scale the finished frame keeps the pixels square instead of stretching each sprite
        /// independently.</summary>
        TextureRect _menuView;
        MenuRenderer.Prepared _menuArt;
        string _menuDrawn;
        Label _big, _small, _note;
        Texture2D _legalArt;

        // The language screen's advisor: a real 3D model in a viewport stacked over the 2D frame.
        SubViewport _advisorView;
        MeshInstance3D _advisorMesh;
        TextureRect _advisorRect;
        TPW.Data.Mesh _advisor;
        System.Collections.Generic.List<(GazEntry Entry, TextureSheet Sheet)> _advisorSheets;
        Vector3 _advisorCentre; float _advisorScale = 1f;
        int _advisorSheetIndex = -1, _advisorFlagDrawn = -1;
        float _advisorClock; int _advisorLength;

        bool _waitingForMovie;
        int _menuFrame = -1;
        bool _musicStarted;

        // Input edges, set by _UnhandledInput and consumed by the next frame.
        bool _anyEdge, _confirmEdge, _upEdge, _downEdge, _leftEdge, _rightEdge;

        /// <summary>Start partway in, for working on a later screen without sitting through 90 seconds
        /// of intro. Names are the <see cref="BootScreen"/> values, case-insensitive.</summary>
        public bool StartAt(string screenName)
        {
            if (!Enum.TryParse<BootScreen>(screenName, ignoreCase: true, out var s)) return false;
            _boot.SkipTo(s);
            // ⚠ A JUMP LANDS ON A LIVE SCREEN, NOT A FROZEN ONE. Starting at the menu with its input
            // delay and its 305-frame music delay still to run gives six seconds of a menu that
            // ignores you in silence -- which is exactly what "the menu is broken" looks like to
            // whoever used this flag to go and work on the menu. Both delays are already spent.
            if (s == BootScreen.MainMenu) _menuFrame = BootSequence.MenuMusicDelay - 1;
            EnterScreen(s);
            Redraw();
            return true;
        }

        public BootScreen Screen => _boot.Screen;
        public bool AtMenu => _boot.Screen == BootScreen.MainMenu;

        public override void _Ready()
        {
            Layer = 10;                       // above the model browser and park view
            _bg = new ColorRect { Color = Colors.Black, AnchorRight = 1, AnchorBottom = 1 };
            AddChild(_bg);

            _legal = new TextureRect
            {
                AnchorRight = 1, AnchorBottom = 1,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Visible = false,
            };
            AddChild(_legal);

            _menuView = new TextureRect
            {
                AnchorRight = 1, AnchorBottom = 1,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                Visible = false,
            };
            AddChild(_menuView);

            BuildAdvisorViewport();

            var box = new VBoxContainer { AnchorRight = 1, AnchorBottom = 1, Alignment = BoxContainer.AlignmentMode.Center };
            box.AddThemeConstantOverride("separation", 10);
            AddChild(box);
            _big = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _big.AddThemeFontSizeOverride("font_size", 28);
            _small = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _note = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 1, 1, 0.4f) };
            box.AddChild(_big); box.AddChild(_small); box.AddChild(_note);
        }

        /// <summary>The advisor is a MODEL, so he gets a real 3D viewport stacked over the 2D frame
        /// rather than being faked with sprites.
        ///
        /// ⭐ The viewport is exactly the frame's own size and carries the SAME anchors and stretch mode
        /// as <see cref="_menuView"/>, so the two scale together. Aligning a separately-sized overlay
        /// against a KeepAspectCentered image means re-deriving its letterboxing, and getting that
        /// subtly wrong looks exactly like a wrong model position.</summary>
        void BuildAdvisorViewport()
        {
            _advisorView = new SubViewport
            {
                Size = new Vector2I(MenuRenderer.W, MenuLayout.VisibleHeight),
                TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                Disable3D = false,
            };
            AddChild(_advisorView);

            // ⚠ ORTHOGONAL, and sized to the frame in PIXELS. A perspective camera would need the game's
            // own field of view and eye distance, neither of which has been read; orthogonal with
            // Size = the frame height makes one world unit one framebuffer pixel, so every number below
            // is a measurement off the console rather than a value tuned until it looked right.
            var cam = new Camera3D
            {
                Projection = Camera3D.ProjectionType.Orthogonal,
                Size = MenuLayout.VisibleHeight,
                Position = new Vector3(0, 0, 600),
                Near = 1, Far = 2000,
            };
            _advisorView.AddChild(cam);

            _advisorMesh = new MeshInstance3D();
            _advisorView.AddChild(_advisorMesh);

            _advisorRect = new TextureRect
            {
                AnchorRight = 1, AnchorBottom = 1,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                Texture = _advisorView.GetTexture(),
                Visible = false,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            AddChild(_advisorRect);
        }

        /// <summary>Give the boot screens the language-screen advisor: FOLIO entry 83 sub-mesh 10.
        ///
        /// ⭐ WHICH MESH IS MEASURED, NOT PICKED. Six of the eight palettes that sub-mesh uses are the
        /// ones the console's display list draws on this screen, and 192 of its 319 faces carry CLUT
        /// 0x40e0 over u 1..149, v 88..173 -- exactly the flag geometry measured from the same dump. So
        /// the waving flag is part of HIM, posed by his 29 bones, not a separate cloth.</summary>
        public void SetAdvisor(TPW.Data.Mesh mesh, System.Collections.Generic.List<(GazEntry Entry, TextureSheet Sheet)> sheets)
        {
            _advisor = mesh;
            _advisorSheets = sheets;
            if (_advisor == null) return;
            // Each language's flag is copied into the slot from the DISC's sheet, not from whatever the
            // last language left there -- copying over a copy would be fine for the texels but would
            // quietly become a chain nobody can reason about the first time one of the copies refuses.
            _advisorSheetIndex = -1;
            for (int i = 0; i < sheets.Count; i++)
                if (sheets[i].Entry.Index == MenuLayout.Sheet) { _advisorSheetIndex = i; break; }
            _advisorFlagDrawn = -1;
            // ⚠ Fit on the REST pose and hold it, or a waving flag rescales the whole advisor each frame.
            ModelMesh.Fit(_advisor, out _advisorCentre, out _advisorScale, AdvisorScreenHeight);
            _advisorLength = MeshPose.AnimationLength(_advisor);
            _advisorClock = 0;
            RebuildAdvisor();
        }

        /// <summary>Where the console puts him, measured off the display list: the advisor's own
        /// triangles span x 229..320 and y 132..187, and his flag spans x 172..283, y 26..118, so the
        /// whole model occupies x 172..320, y 26..187 of the 512x240 frame.
        ///
        /// ⚠ THIS IS A FIT TO THE MODEL'S ON-SCREEN BOUNDING BOX, NOT THE GAME'S CAMERA. The console
        /// projects him with its own transform, which has not been read; this places and scales the
        /// model so its silhouette lands where the console's does. It will be right in position and
        /// size and can still be wrong in ROTATION, and that difference is not visible in a bounding
        /// box -- so it stays labelled rather than quietly presented as the real placement.</summary>
        public const int AdvisorLeft = 172, AdvisorRight = 320, AdvisorTop = 26, AdvisorBottom = 187;
        public const int AdvisorScreenHeight = AdvisorBottom - AdvisorTop;      // 161 px

        /// <summary>Put the ring's current flag into the slot sprite the mesh samples, the way the
        /// console does. Works on a COPY of the sheet, so the model browser and the park keep seeing
        /// the disc's own texels.</summary>
        void PutFlagInSlot()
        {
            if (_advisorSheets == null || _advisorSheetIndex < 0) return;
            int want = LanguageRing.FlagSprite[((_ring % LanguageRing.Count) + LanguageRing.Count) % LanguageRing.Count];
            if (want == _advisorFlagDrawn) return;
            var fresh = _menuSheetPristine?.CloneTexels();
            if (fresh == null) return;
            if (!fresh.CopySpriteInto(want, LanguageRing.FlagSlotSprite))
            { GD.PushWarning($"[tpw] flag sprite {want} would not copy into slot {LanguageRing.FlagSlotSprite}"); return; }
            var e = _advisorSheets[_advisorSheetIndex].Entry;
            _advisorSheets[_advisorSheetIndex] = (e, fresh);
            _advisorFlagDrawn = want;
        }

        void RebuildAdvisor()
        {
            if (_advisor == null || _advisorMesh == null) return;
            PutFlagInSlot();
            var posed = MeshPose.Evaluate(_advisor, (int)_advisorClock).Vertices;
            if (posed != null && posed.Length != _advisor.VertexCount) posed = null;
            _advisorMesh.Mesh = ModelMesh.Build(_advisor, posed, _advisorSheets,
                                                // ⚠ cull: false IS the game's rule, not "no culling".
                                                // ModelMesh drops every single-sided face that turns
                                                // away and draws double-sided groups from both sides,
                                                // which is what the console does. true means cull
                                                // EVERYTHING -- the winding instrument, not fidelity,
                                                // and it would show the flag from one side only.
                                                textured: true, reverse: false, cull: false,
                                                _advisorCentre, _advisorScale, out _);
            // Frame pixels -> camera units: x right from the centre, y UP from the centre.
            float cx = (AdvisorLeft + AdvisorRight) / 2f - MenuRenderer.W / 2f;
            float cy = MenuLayout.VisibleHeight / 2f - (AdvisorTop + AdvisorBottom) / 2f;
            _advisorMesh.Position = new Vector3(cx, cy, 0);
        }

        /// <summary>Hand over the decoded LEGAL.GFX. Optional: without it the legal screen still runs
        /// for its measured 313 frames, as black, rather than being skipped -- a missing asset must not
        /// silently change the timing.</summary>
        public void SetLegalArt(Texture2D t) => _legalArt = t;

        /// <summary>Hand over FOLIO entry 84. Without it the menu falls back to the labelled
        /// placeholder rather than drawing nothing -- a disc we cannot read the sheet from must still
        /// reach a usable menu.</summary>
        public void SetMenuSheet(TextureSheet sheet)
        {
            _menuArt = MenuRenderer.Prepare(sheet);
            _menuSheetPristine = sheet;      // kept unmodified; every flag copy starts from the disc's own texels
        }

        TextureSheet _menuSheetPristine;

        /// <summary>The movie finished or was skipped. The FILE decides its own length, so this is what
        /// advances the chain, not the frame count in the table.</summary>
        public void MovieEnded()
        {
            if (!_waitingForMovie) return;
            _waitingForMovie = false;
            _boot.Advance();
            // ⚠ EnterScreen, NOT just Redraw. Advancing out of a movie has to go through the same
            // arrival path as any other transition, or the screen it lands on never gets STARTED --
            // only drawn. Today every movie is followed by a black gap so nothing visible breaks, and
            // the only symptom was a missing log line; put two movies back to back and the second one
            // would silently never play. Found because the boot log skipped BlackAfterBullfrog.
            EnterScreen(_boot.Screen);
            Redraw();
        }

        /// <summary>⚠ THE TABLE IS IN PAL FRAMES AND _Process IS NOT. Every duration in
        /// <see cref="BootSequence"/> was measured at 50 Hz; ticking it once per rendered frame runs
        /// the whole boot 20% fast on a 60 Hz display and at some other speed on a 144 Hz one, which
        /// looks like nothing at all going wrong. The accumulator makes the chain's clock the console's
        /// clock regardless of what the window is doing.</summary>
        double _accum;

        public override void _Process(double delta)
        {
            if (_waitingForMovie) return;

            _accum += delta;
            double step = 1.0 / BootSequence.Hz;
            if (_accum < step) return;
            // Never run away: a long stall (a movie decode, a window drag) must not fast-forward the
            // boot by a hundred frames when it comes back.
            int ticks = Math.Min(4, (int)(_accum / step));
            _accum -= ticks * step;

            var before = _boot.Screen;
            for (int i = 0; i < ticks && _boot.Screen == before; i++)
                _boot.Tick(_anyEdge && i == 0, _confirmEdge && i == 0);

            // The advisor animates on the SAME console tick as the rest of the chain, not on the
            // renderer's frame rate -- his flag would wave at a different speed on a 144 Hz monitor
            // otherwise, and that is the sort of wrong that only shows up on somebody else's machine.
            if (_boot.Screen == BootScreen.LanguageSelect && _advisor != null && _advisorLength > 0)
            {
                _advisorClock += ticks;
                if (_advisorClock >= _advisorLength) _advisorClock %= _advisorLength;
                RebuildAdvisor();
            }

            if (_boot.Screen == BootScreen.MainMenu)
            {
                if (before != BootScreen.MainMenu) { _menuFrame = 0; _musicStarted = false; }
                else _menuFrame++;
                HandleMenuInput();
                if (!_musicStarted && _menuFrame >= BootSequence.MenuMusicDelay)
                { _musicStarted = true; WantMenuMusic?.Invoke(); }
            }
            else if (_boot.Screen == BootScreen.LanguageSelect)
            {
                // The ring turns while the screen waits. LEFT decrements, measured.
                if (_leftEdge)  _ring = LanguageRing.Turn(_ring, -1);
                if (_rightEdge) _ring = LanguageRing.Turn(_ring, +1);
            }
            else if (before == BootScreen.LanguageSelect)
            {
                // Just left the ring: report the GAME's index, never the ring position.
                LanguageChosen?.Invoke(LanguageRing.ToLanguage(_ring));
            }

            if (_boot.Screen != before) EnterScreen(_boot.Screen);
            _anyEdge = _confirmEdge = _upEdge = _downEdge = _leftEdge = _rightEdge = false;
            Redraw();
        }

        void HandleMenuInput()
        {
            // ⚠ The menu ignores input for its first 152 frames. Not a loading delay: it stops the menu
            // eating the press the player is still holding from the screen before.
            if (_menuFrame < BootSequence.MenuInputDelay) return;
            if (_upEdge)   _menu.Move(-1);
            if (_downEdge) _menu.Move(+1);
            // LEFT/RIGHT only do anything on a slider row. On the root menu they were byte-identical to
            // no press on the console, and Adjust() enforces that rather than this call site.
            bool moved = false;
            if (_leftEdge)  moved |= _menu.Adjust(-1);
            if (_rightEdge) moved |= _menu.Adjust(+1);
            if (moved) VolumeChanged?.Invoke(_menu.MusicVolume, _menu.SfxVolume);
            if (_confirmEdge && _menu.Confirm() == MenuAction.StartPracticePark) StartPracticePark?.Invoke();
        }

        void EnterScreen(BootScreen s)
        {
            // One line per screen. A boot that stalls is the failure mode here -- a movie that never
            // reports back, a screen waiting on input nobody sends -- and it looks identical to a long
            // black gap from outside. This is what says which screen it stopped on.
            GD.Print($"[tpw] boot: {s} at frame {_boot.TotalFrames}");
            string movie = BootSequence.MovieFor(s);
            if (movie != null) { _waitingForMovie = true; WantMovie?.Invoke(movie); }
        }

        /// <summary>The menu as the console drew it: the captured backdrop, then the item text in the
        /// game's own font.
        ///
        /// ⚠ REDRAWN ONLY WHEN THE TEXT CHANGES. Rasterising 320x256 and uploading a texture every
        /// frame is affordable here and would still be the wrong habit -- the backdrop is static and
        /// the items change on a keypress. The key is the state that can alter the picture; get that
        /// wrong and the menu stops responding while looking perfectly fine.</summary>
        void DrawRealMenu()
        {
            _legal.Visible = false;
            _bg.Color = Colors.Black;
            _menuView.Visible = true;

            var sb = new System.Text.StringBuilder(_menu.Page.ToString());
            foreach (string it in _menu.Items) sb.Append('|').Append(it);
            sb.Append('#').Append(_menu.Index);
            if (_menu.Page == MenuPage.Options) sb.Append('v').Append(_menu.MusicLevel).Append(',').Append(_menu.SfxLevel);
            string key = sb.ToString();
            if (key == _menuDrawn) return;
            _menuDrawn = key;

            var frame = MenuRenderer.NewFrame();
            MenuRenderer.DrawBackdrop(_menuArt, frame);
            // The glow goes under the selected row, BEFORE the text -- it is the selection indicator,
            // and the text sits inside it.
            // ⭐ MEASURED OFF THE CONSOLE, not chosen. In the emulator capture of the real menu
            // (fable/b/png/12_main_menu_f5100.png, 512x240) "Play Game" occupies rows 113-133, and the
            // three rows are 31 apart. Row0 is the BASELINE of the first item, so it sits just under
            // that band. My earlier 138/44 was invented and both numbers were wrong.
            // ⭐ TAKEN FROM THE CONSOLE'S OWN DISPLAY LIST, not from measuring pixels in a screenshot.
            // The captured frame's font quads span y 139..224 in three rows 31 apart, and its highlight
            // glow sits at y 130..174 -- bracketing the first row exactly. A cap is 21 tall and its
            // OffsetY is -21, so the first BASELINE is 139 + 21 = 160.
            //
            // ⚠ I ARRIVED HERE AFTER TWO WRONG ANSWERS FROM PIXEL-BAND MEASUREMENTS of the emulator
            // screenshot: the first caught the WORLD logo instead of the menu text, the second was
            // tuned against bands that turned out to be different features in the two images. Both
            // produced real numbers and both were ~28 rows out. The display list was in the repo the
            // whole time and is the console's own answer -- measure the ARTEFACT, not a picture of it.
            // Row baselines 160, 190, 220: the captured rows start at y 140, 170 and 200, and a cap is
            // 20 tall with OffsetY -20.
            const int Row0 = 160, RowStep = 30;
            // The captured glow already sits on the first row, so row 0 needs no shift at all.
            MenuRenderer.DrawHighlight(_menuArt, frame, _menu.Index * RowStep);

            // The item list, centred, below the logo. ⚠ The Y positions are MINE, not measured: the
            // captured frame's own text primitives were left out on purpose (they spell one menu with
            // the highlight on one item), so where the rows sit is the one part of this screen not
            // taken from the console. It is a placeholder with real art on top of it, and should be
            // replaced by the real row geometry if anyone measures it.
            int y = Row0;
            for (int i = 0; i < _menu.Items.Length; i++)
            {
                string label = _menu.Items[i];
                if (_menu.Page == MenuPage.Options && i < MainMenu.OptionKinds.Length
                    && MainMenu.OptionKinds[i] == MainMenu.OptionKind.Slider)
                    label += "  " + Bar(i == 0 ? _menu.MusicLevel : _menu.SfxLevel);
                int w = MenuRenderer.MeasureText(_menuArt, label);
                // ⚠ OPAQUE, NOT ADDITIVE. Drawing the selected item additively over its own bright
                // yellow glow erased it -- white on yellow. The console draws the items opaque and
                // lets the glow behind them mark the selection; additive is the GLOW's blend, not the
                // text's, and I had borrowed it for the wrong thing.
                MenuRenderer.DrawText(_menuArt, frame, (MenuRenderer.W - w) / 2, y, label,   // y is the baseline
                    shade: _menu.IsDisabled(i) ? MenuRenderer.DisabledShade : MenuRenderer.NormalShade);
                y += RowStep;
            }

            var img = Image.CreateFromData(MenuRenderer.W, MenuLayout.VisibleHeight, false, Image.Format.Rgba8, MenuRenderer.Visible(frame));
            _menuView.Texture = ImageTexture.CreateFromImage(img);
            _big.Text = _small.Text = _note.Text = "";
        }

        /// <summary>The language screen: its measured background gradient and the language name in the
        /// game's own font.
        ///
        /// ⚠ STILL MISSING THE FLAGS AND THE ADVISOR. The flags are sheet 84 sprites #119-125 and the
        /// advisor is a SKINNED MESH (entry 83 sub-mesh 10), which is a different pipeline from the 2D
        /// quads here -- so this screen is part real and part not, and says so on it rather than
        /// looking finished.</summary>
        void DrawLanguage()
        {
            _legal.Visible = false;
            _menuView.Visible = true;
            string key = "lang" + _ring + (_advisor != null ? "m" : "");
            if (key == _menuDrawn) return;
            _menuDrawn = key;

            var frame = MenuRenderer.NewFrame();
            MenuRenderer.DrawLanguageScreen(_menuArt, frame, _ring, flatFlag: _advisor == null);

            var img = Image.CreateFromData(MenuRenderer.W, MenuLayout.VisibleHeight, false, Image.Format.Rgba8, MenuRenderer.Visible(frame));
            _menuView.Texture = ImageTexture.CreateFromImage(img);
            _big.Text = _small.Text = "";
            _note.Text = _advisor != null
                ? "advisor + flag: the real model, waved by its own bones; placement fitted, not the game's camera"
                : "advisor and flag not loaded — the 2D flag below is the right art in the wrong form";
        }

        static string Bar(int level)
            => new string('|', level) + new string('.', MainMenu.SliderSteps - level);

        void Redraw()
        {
            var s = _boot.Screen;
            _legal.Visible = s == BootScreen.Legal && _legalArt != null;
            if (s != BootScreen.MainMenu && s != BootScreen.LanguageSelect)
            { _menuView.Visible = false; _menuDrawn = null; }
            // He belongs to the language screen only. The viewport keeps rendering either way, so the
            // flag is mid-wave rather than snapped to frame 0 when the screen comes back.
            if (_advisorRect != null)
                _advisorRect.Visible = s == BootScreen.LanguageSelect && _advisor != null;
            _bg.Color = Colors.Black;
            _big.Text = _small.Text = _note.Text = "";

            if (s == BootScreen.Legal && _legalArt != null)
            {
                _legal.Texture = _legalArt;
                // Fade in, hold, fade out -- 61 / 189 / 63 of its 313 frames.
                int f = _boot.FrameInScreen;
                float a = f < BootSequence.LegalFadeIn ? f / (float)BootSequence.LegalFadeIn
                        : f < BootSequence.LegalFadeIn + BootSequence.LegalHold ? 1f
                        : Math.Max(0f, 1f - (f - BootSequence.LegalFadeIn - BootSequence.LegalHold) / (float)BootSequence.LegalFadeOut);
                _legal.Modulate = new Color(1, 1, 1, a);
                return;
            }
            if (BootSequence.IsBlack(s) || _waitingForMovie) return;

            switch (s)
            {
                case BootScreen.Legal:
                    _big.Text = "LEGAL.GFX";
                    _note.Text = "not decoded from this disc — running its measured 313 frames anyway";
                    break;
                case BootScreen.LanguageSelect when _menuArt != null:
                    DrawLanguage();
                    return;
                case BootScreen.LanguageSelect:
                    _bg.Color = new Color(0.05f, 0.08f, 0.16f);
                    _big.Text = $"‹  {LanguageRing.NameAt(_ring)}  ›";
                    _small.Text = "left / right to change, cross to confirm";
                    _note.Text = "placeholder — the language screen's art is not identified yet";
                    break;
                case BootScreen.LanguageFadeOut:
                    break;
                case BootScreen.NowLoading:
                    _bg.Color = new Color(0.06f, 0.10f, 0.28f);
                    _big.Text = "Now Loading…";
                    _note.Text = "placeholder — the jester animation is not identified yet";
                    break;
                case BootScreen.MainMenu when _menuArt != null:
                    DrawRealMenu();
                    return;
                case BootScreen.MainMenu:
                    _bg.Color = new Color(0.16f, 0.04f, 0.07f);
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < _menu.Items.Length; i++)
                    {
                        string item = _menu.Items[i];
                        string row = _menu.Page == MenuPage.Options && i < MainMenu.OptionKinds.Length
                                  && MainMenu.OptionKinds[i] == MainMenu.OptionKind.Slider
                            ? $"{item}  {Bar(i == 0 ? _menu.MusicLevel : _menu.SfxLevel)}"
                            : item;
                        sb.AppendLine(i == _menu.Index ? $"▸  {row}" : $"    {row}");
                    }
                    _big.Text = sb.ToString().TrimEnd();
                    _small.Text = _menuFrame < BootSequence.MenuInputDelay ? ""
                        : _menu.CurrentKind == MainMenu.OptionKind.Slider ? "left / right to set"
                        : "up / down, cross to choose";
                    _note.Text = "placeholder — the curtains, logo and advisor are not identified yet";
                    break;
            }
        }

        /// <summary>⚠ EDGES, NOT LEVELS, and the console is why. Its FMV skip is tested against a
        /// snapshot taken when the video player starts, so a button held from before a movie skips
        /// nothing, and a held CROSS never advances the language screen either. Godot gives us that for
        /// free as long as we only ever look at Pressed && !Echo and clear the flag each frame.</summary>
        public override void _UnhandledInput(InputEvent e)
        {
            bool down = e switch
            {
                InputEventKey k          => k.Pressed && !k.Echo,
                InputEventJoypadButton j => j.Pressed,
                _                        => false,
            };
            if (!down) return;

            // F3 belongs to the debug menu, and must not also be a boot button.
            if (e is InputEventKey { Keycode: Key.F3 }) return;

            _anyEdge = true;
            switch (e)
            {
                case InputEventKey k:
                    if (k.Keycode is Key.Enter or Key.Space or Key.X) _confirmEdge = true;
                    if (k.Keycode == Key.Up) _upEdge = true;
                    if (k.Keycode == Key.Down) _downEdge = true;
                    if (k.Keycode == Key.Left) _leftEdge = true;
                    if (k.Keycode == Key.Right) _rightEdge = true;
                    break;
                case InputEventJoypadButton j:
                    // ⚠ JoyButton.A is CROSS on a PlayStation pad. The libretro core this was measured
                    // on calls the same button "B", which is an SNES-era naming and cost a previous
                    // session 13 minutes of pressing the wrong one.
                    if (j.ButtonIndex == JoyButton.A) _confirmEdge = true;
                    if (j.ButtonIndex == JoyButton.DpadUp) _upEdge = true;
                    if (j.ButtonIndex == JoyButton.DpadDown) _downEdge = true;
                    if (j.ButtonIndex == JoyButton.DpadLeft) _leftEdge = true;
                    if (j.ButtonIndex == JoyButton.DpadRight) _rightEdge = true;
                    break;
            }
            GetViewport().SetInputAsHandled();
        }
    }
}
