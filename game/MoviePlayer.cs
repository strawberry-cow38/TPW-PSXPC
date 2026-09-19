using System;
using Godot;
using TPW.Data;

namespace TPWGodot
{
    /// <summary>Plays a .STR movie over everything else, the way the game shows them: full screen,
    /// letterboxed, skippable.
    ///
    /// ⭐ THE CLOCK IS THE DISC'S, NOT GODOT'S. A frame goes up when the console would have finished reading
    /// it (<see cref="StrMovie.FrameTime"/>), and the soundtrack starts when its first sector has been read.
    /// Nothing is paced to the render frame rate, so a 144 Hz monitor and a 30 Hz one show the same movie at
    /// the same speed.
    ///
    /// ⚠ TIME IS ACCUMULATED DELTA, NOT THE WALL CLOCK. In real time the two are the same thing. Under Godot's
    /// movie writer they are not: frames are produced as fast as they render, each advancing delta by a fixed
    /// step, and the audio is captured in step with them. A wall-clock player would race through the movie
    /// in a capture and drift out of sync with its own soundtrack.
    ///
    /// ⚠ EVERY FRAME IS INDEPENDENTLY CODED, so a late frame is simply skipped: the player jumps to whichever
    /// frame should be on screen now and decodes only that one. Nothing is lost by skipping except the
    /// skipped picture.</summary>
    public partial class MoviePlayer : CanvasLayer
    {
        StrMovie _movie;
        ColorRect _back;
        TextureRect _view;
        Label _caption;
        ImageTexture _tex;
        TpwImage _frame;     // reused by the decoder, so playing a movie allocates nothing per frame
        AudioStreamPlayer _audio;
        double _t;
        int _shown = -1;
        int _decoded;
        bool _audioStarted;
        int _failures;
        string _firstFailure;

        /// <summary>Raised once when the movie ends or is skipped. The argument is true if it ran to the end.</summary>
        public event Action<bool> Finished;

        public bool IsPlaying => _movie != null;

        public override void _Ready()
        {
            // Above the browsers' UI and the 3D view. ⚠ The black backdrop also STOPS mouse input, or a
            // click meant to skip the movie falls through and presses whatever button is underneath.
            //
            // ⚠ 11, NOT 10, AND IT MUST STAY ABOVE BootScreens. Both were on 10, and two CanvasLayers on
            // the same layer fall back to TREE ORDER -- BootScreens is added second, so its full-screen
            // black ColorRect sat on top and both boot movies played to an entirely black window. The
            // audio ran, the decoder reported its frames, every self-test passed: the only symptom was
            // that nothing could be seen, which is why this survived. A movie is the whole screen while
            // it plays, so it belongs above the chain that starts it.
            Layer = 11;
            _back = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Stop };
            _back.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            AddChild(_back);

            // The movies are 320x176 with square pixels, which the console letterboxed inside its 4:3 picture,
            // so keep the aspect and let the backdrop be the bars.
            _view = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _view.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _back.AddChild(_view);

            // A strip along the bottom edge. ⚠ Anchors and offsets, not Position: Position is measured from the
            // parent's top-left whatever the anchors say, so a negative Y puts the label off the top of the screen.
            _caption = new Label
            {
                Modulate = new Color(1, 1, 1, 0.55f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                AnchorLeft = 0, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1,
                OffsetLeft = 12, OffsetRight = -12, OffsetTop = -32, OffsetBottom = -8,
            };
            // An outline, or the caption vanishes over the white frames at the end of every world movie.
            _caption.AddThemeConstantOverride("outline_size", 4);
            _caption.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
            _back.AddChild(_caption);

            _audio = new AudioStreamPlayer();
            AddChild(_audio);
            Visible = false;
        }

        public void Play(StrMovie m)
        {
            if (m == null || m.Frames.Count == 0) return;
            Stop(false, notify: false);

            _movie = m;
            _t = 0; _shown = -1; _decoded = 0; _failures = 0; _firstFailure = null;
            _audioStarted = false;

            // ⚠ An empty soundtrack (the game-over movies) is skipped, not played: see StrMovie.AudioIsEmpty.
            if (m.Audio.Length > 0 && !m.AudioIsEmpty)
            {
                // PCM16 little-endian, interleaved, which is what AudioStreamWav expects when Stereo is set.
                var bytes = new byte[m.Audio.Length * 2];
                Buffer.BlockCopy(m.Audio, 0, bytes, 0, bytes.Length);
                _audio.Stream = new AudioStreamWav
                {
                    Format = AudioStreamWav.FormatEnum.Format16Bits,
                    MixRate = m.AudioCoding.SampleRate,
                    Stereo = m.AudioCoding.Stereo,
                    Data = bytes,
                };
            }
            else _audio.Stream = null;

            Visible = true;
            GD.Print($"[tpw] movie {m.Name}: {m.Frames.Count} frames {m.Width}x{m.Height}, {m.DurationSeconds:0.00}s " +
                     $"at {m.FramesPerSecond:0.00} fps; audio {m.AudioCoding}, {m.AudioSeconds:0.00}s" +
                     (m.AudioIsEmpty ? " (an empty track, skipped)" : ""));
        }

        public override void _Process(double delta)
        {
            if (_movie == null) return;
            _t += delta;

            if (!_audioStarted && _audio.Stream != null && _t >= _movie.AudioStartSeconds)
            {
                _audio.Play((float)(_t - _movie.AudioStartSeconds));
                _audioStarted = true;
            }

            // ⚠ What leaves the speakers is behind the mixer by the output latency. Hold the picture back by
            // the same amount, or it runs ahead of the sound it belongs to.
            double videoT = _t - (_audioStarted ? AudioServer.GetOutputLatency() : 0);
            int want = _movie.FrameAt(videoT);
            if (want > _shown) ShowFrame(want);

            if (_t >= _movie.DurationSeconds) Stop(true);
        }

        void ShowFrame(int i)
        {
            _shown = i;
            var f = _movie.Frames[i];
            if (!Mdec.TryDecodeFrame(f.Data, f.Width, f.Height, _frame, out var img, out string err, out _))
            {
                // Counted and reported at the end rather than logged per frame: one bad table would otherwise
                // print hundreds of identical lines and bury the first one, which is the useful one.
                _failures++;
                _firstFailure ??= $"frame {f.Number}: {err}";
                return;
            }
            _decoded++;
            _frame = img;

            var image = Image.CreateFromData(img.Width, img.Height, false, Image.Format.Rgba8, img.Rgba);
            if (_tex == null || _tex.GetWidth() != img.Width || _tex.GetHeight() != img.Height)
            {
                _tex = ImageTexture.CreateFromImage(image);
                _view.Texture = _tex;
            }
            else _tex.Update(image);

            _caption.Text = $"{_movie.Name}  {StrMovie.Describe(_movie.Name)}  —  frame {f.Number}/{_movie.Frames.Count}" +
                            $"  —  any key or click skips";
        }

        /// <summary>End playback. <paramref name="completed"/> is true when it ran to the end.</summary>
        public void Stop(bool completed, bool notify = true)
        {
            if (_movie == null) return;
            var m = _movie;
            _movie = null;
            _audio.Stop();
            Visible = false;

            GD.Print($"[tpw] movie {m.Name} {(completed ? "finished" : "skipped")} at {_t:0.00}s: " +
                     $"{_decoded} frames shown of {m.Frames.Count}" +
                     (_failures > 0 ? $", {_failures} FAILED to decode, first: {_firstFailure}" : ""));
            if (_failures > 0) GD.PushWarning($"[tpw] {_failures} frames of {m.Name} failed to decode; first: {_firstFailure}");
            if (notify) Finished?.Invoke(completed);
        }

        public override void _Input(InputEvent e)
        {
            if (_movie == null) return;
            bool skip = e is InputEventKey { Pressed: true, Echo: false }
                     || e is InputEventMouseButton { Pressed: true }
                     || e is InputEventJoypadButton { Pressed: true };
            if (!skip) return;
            GetViewport().SetInputAsHandled();
            Stop(false);
        }
    }
}
