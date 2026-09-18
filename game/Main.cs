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
        AudioStreamPlayer _audio;
        Button _playSound;
        PcmSample _sample;
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

            _audio = new AudioStreamPlayer();
            AddChild(_audio);
            _playSound = new Button { Text = "Play a sound from your disc", Disabled = true };
            _playSound.Pressed += PlaySample;
            _root.AddChild(_playSound);

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
                PcmSample sample = null;
                try
                {
                    using var disc = DiscReader.Open(path);
                    report = AssetSelfTest.Run(disc);

                    var f = disc.Find(AssetSelfTest.LegalScreen);
                    if (f != null && Tga.TryDecode(disc.ReadFile(f), out var img, out _)) legal = img;
                    sample = FirstSound(disc);
                }
                catch (System.Exception e)
                {
                    report = new SelfTestReport();
                    report.Add("disc", false, e.Message);
                }
                // ⚠ An empty array, never null: these arguments cross into Godot as Variants, and a null
                // byte[] does not round-trip the way a managed null would -- it arrives as an empty
                // PackedByteArray anyway, so say so here rather than relying on that.
                _sample = sample;   // read back on the main thread once ApplySelfTest runs
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

            if (_sample != null && _sample.SampleCount > 0)
            {
                _playSound.Disabled = false;
                _playSound.Text = $"Play a sound from your disc ({_sample.SampleCount:n0} samples)";
                GD.Print($"[tpw] decoded {_sample.SampleCount:n0} PCM samples from {_sample.Source}");
            }
            else _playSound.Text = "No sound decoded";
        }

        /// <summary>First non-empty waveform out of the first sound bank, decoded to PCM.</summary>
        static PcmSample FirstSound(DiscReader disc)
        {
            var f = disc.Find(AssetSelfTest.AssetArchive);
            if (f == null || !GazArchive.TryParse(disc.ReadFile(f), out var gaz, out _)) return null;

            foreach (var e in gaz.Entries)
            {
                if (e.Size != VabHeader.SplitHeaderSize) continue;
                if (!VabHeader.TryParse(gaz.Read(e), out var vab, out _)) continue;
                int bodyIndex = e.Index - 1;   // ⚠ BEFORE, not after — see VabHeader
                if (bodyIndex < 0 || gaz.Entries[bodyIndex].Size != vab.BodyBytes) continue;
                foreach (var wave in vab.SliceBody(gaz.Read(gaz.Entries[bodyIndex])))
                {
                    if (wave.Length == 0) continue;
                    var pcm = Vag.Decode(wave, $"bank #{e.Index}");
                    if (pcm.SampleCount > 0) return pcm;
                }
            }
            return null;
        }

        void PlaySample()
        {
            if (_sample == null || _sample.SampleCount == 0) return;

            // PCM16 little-endian, which is what AudioStreamWav.Format16Bits expects.
            var bytes = new byte[_sample.SampleCount * 2];
            System.Buffer.BlockCopy(_sample.Samples, 0, bytes, 0, bytes.Length);

            _audio.Stream = new AudioStreamWav
            {
                Format = AudioStreamWav.FormatEnum.Format16Bits,
                MixRate = SampleRateHz,
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
        /// Kept as a constant only because a wrong rate is instantly audible as wrong pitch — the safe kind of
        /// wrong. 8363 is the better default because something measured points at it.</summary>
        const int SampleRateHz = 8363;

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
