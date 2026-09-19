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

            var box = new VBoxContainer { AnchorRight = 1, AnchorBottom = 1, Alignment = BoxContainer.AlignmentMode.Center };
            box.AddThemeConstantOverride("separation", 10);
            AddChild(box);
            _big = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _big.AddThemeFontSizeOverride("font_size", 28);
            _small = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _note = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 1, 1, 0.4f) };
            box.AddChild(_big); box.AddChild(_small); box.AddChild(_note);
        }

        /// <summary>Hand over the decoded LEGAL.GFX. Optional: without it the legal screen still runs
        /// for its measured 313 frames, as black, rather than being skipped -- a missing asset must not
        /// silently change the timing.</summary>
        public void SetLegalArt(Texture2D t) => _legalArt = t;

        /// <summary>Hand over FOLIO entry 84. Without it the menu falls back to the labelled
        /// placeholder rather than drawing nothing -- a disc we cannot read the sheet from must still
        /// reach a usable menu.</summary>
        public void SetMenuSheet(TextureSheet sheet) => _menuArt = MenuRenderer.Prepare(sheet);

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
            // ⚠ THE ROW SPACING IS SET BY THE GLOW, NOT PICKED. The highlight cap is 44 pixels tall
            // (findings/menu-art.md), so rows closer together than that put one item's highlight over
            // its neighbour -- which is exactly what 22 did. These positions are still mine rather
            // than measured, but the spacing is now derived from the art instead of guessed.
            const int Row0 = 138, RowStep = 44;
            MenuRenderer.DrawHighlight(_menuArt, frame, Row0 + _menu.Index * RowStep - MenuRenderer.GlowY - 4);

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
                MenuRenderer.DrawText(_menuArt, frame, (MenuRenderer.W - w) / 2, y, label);
                y += RowStep;
            }

            var img = Image.CreateFromData(MenuRenderer.W, MenuRenderer.H, false, Image.Format.Rgba8, frame);
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
            string key = "lang" + _ring;
            if (key == _menuDrawn) return;
            _menuDrawn = key;

            var frame = MenuRenderer.NewFrame();
            // ⭐ MEASURED, not picked: one Gouraud quad running RGB(153,163,254) at the top to
            // RGB(42,32,87) at the bottom (findings/menu-art.md).
            for (int yy = 0; yy < MenuRenderer.H; yy++)
            {
                float t = yy / (float)(MenuRenderer.H - 1);
                byte r = (byte)(153 + (42 - 153) * t), g = (byte)(163 + (32 - 163) * t), b = (byte)(254 + (87 - 254) * t);
                for (int xx = 0; xx < MenuRenderer.W; xx++)
                {
                    int o = (yy * MenuRenderer.W + xx) * 4;
                    frame[o] = r; frame[o + 1] = g; frame[o + 2] = b; frame[o + 3] = 255;
                }
            }
            string name = LanguageRing.NameAt(_ring);
            int w = MenuRenderer.MeasureText(_menuArt, name);
            MenuRenderer.DrawText(_menuArt, frame, (MenuRenderer.W - w) / 2, 110, name);

            var img = Image.CreateFromData(MenuRenderer.W, MenuRenderer.H, false, Image.Format.Rgba8, frame);
            _menuView.Texture = ImageTexture.CreateFromImage(img);
            _big.Text = _small.Text = "";
            _note.Text = "flags and advisor not drawn yet — they are sheet 84 sprites and a skinned mesh";
        }

        static string Bar(int level)
            => new string('|', level) + new string('.', MainMenu.SliderSteps - level);

        void Redraw()
        {
            var s = _boot.Screen;
            _legal.Visible = s == BootScreen.Legal && _legalArt != null;
            if (s != BootScreen.MainMenu && s != BootScreen.LanguageSelect)
            { _menuView.Visible = false; _menuDrawn = null; }
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
