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
        GameDataResult _data;
        Label _status;
        Label _selfTest;
        TextureRect _preview;
        VBoxContainer _root;
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

            _root = new VBoxContainer { AnchorRight = 1, AnchorBottom = 1 };
            _root.AddThemeConstantOverride("separation", 10);
            AddChild(_root);

            _root.AddChild(new Label { Text = "Theme Park World — Godot" });
            _status = new Label();
            _root.AddChild(_status);
            _selfTest = new Label { Text = "Asset self-test: running…" };
            _root.AddChild(_selfTest);
            _preview = new TextureRect
            {
                StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                CustomMinimumSize = new Vector2(320, 256),
            };
            _root.AddChild(_preview);

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
                try
                {
                    using var disc = DiscReader.Open(path);
                    report = AssetSelfTest.Run(disc);

                    var f = disc.Find(AssetSelfTest.LegalScreen);
                    if (f != null && Tga.TryDecode(disc.ReadFile(f), out var img, out _)) legal = img;
                }
                catch (System.Exception e)
                {
                    report = new SelfTestReport();
                    report.Add("disc", false, e.Message);
                }
                // ⚠ An empty array, never null: these arguments cross into Godot as Variants, and a null
                // byte[] does not round-trip the way a managed null would -- it arrives as an empty
                // PackedByteArray anyway, so say so here rather than relying on that.
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

            if (rgba == null || rgba.Length == 0 || w <= 0 || h <= 0) return;
            var image = Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);
            _preview.Texture = ImageTexture.CreateFromImage(image);
            GD.Print($"[tpw] legal screen decoded from the user's disc: {w}x{h}");
        }

        public override void _Process(double delta)
        {
            // ⚠ NOT frame-delta driven. The sim advances in whole ticks -- see ParkClock -- because a sim that
            // integrates on frame time inherits the frame rate into its economy, which is a bug unturnedGD
            // still carries in its weather. Accumulate real time and spend it in whole ticks.
            _accum += delta;
            double tickSeconds = _data.Variant?.TickSeconds ?? ParkClock.TickSeconds;
            while (_accum >= tickSeconds) { _accum -= tickSeconds; _clock.Advance(); }

            _status.Text = _data.CanPlay
                ? $"{_data.Variant.Name} ({_data.Variant.Region})\n{_clock}\ntick = {tickSeconds:0.####}s"
                : $"No playable game data.\n{_data.Message}";
        }
    }
}
