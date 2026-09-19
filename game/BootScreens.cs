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

        readonly BootSequence _boot = new();
        readonly MainMenu _menu = new();
        int _ring = LanguageRing.RestPosition;

        ColorRect _bg;
        TextureRect _legal;
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

        /// <summary>The movie finished or was skipped. The FILE decides its own length, so this is what
        /// advances the chain, not the frame count in the table.</summary>
        public void MovieEnded()
        {
            if (!_waitingForMovie) return;
            _waitingForMovie = false;
            _boot.Advance();
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
            if (_confirmEdge && _menu.Confirm() == MenuAction.StartPracticePark) StartPracticePark?.Invoke();
        }

        void EnterScreen(BootScreen s)
        {
            string movie = BootSequence.MovieFor(s);
            if (movie != null) { _waitingForMovie = true; WantMovie?.Invoke(movie); }
        }

        void Redraw()
        {
            var s = _boot.Screen;
            _legal.Visible = s == BootScreen.Legal && _legalArt != null;
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
                case BootScreen.MainMenu:
                    _bg.Color = new Color(0.16f, 0.04f, 0.07f);
                    var sb = new System.Text.StringBuilder();
                    foreach (string item in _menu.Items)
                        sb.AppendLine(item == _menu.Current ? $"▸  {item}" : $"    {item}");
                    _big.Text = sb.ToString().TrimEnd();
                    _small.Text = _menuFrame < BootSequence.MenuInputDelay ? "" : "up / down, cross to choose";
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
