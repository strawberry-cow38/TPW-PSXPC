using System;
using System.Collections.Generic;
using Godot;
using TPW.Data;

namespace TPWGodot
{
    /// <summary>Plays one of the game's music modules through TrackerPlayer, streamed into Godot's mixer.
    ///
    /// ⭐ THE MUSIC IS SYNTHESISED, NOT DECODED. The game ships no recorded soundtrack for its parks: the music
    /// is nine tracker modules (FT2 with the game's own pattern packing, see TrackerModule) playing short
    /// waveforms out of the VAB banks. So it has to be rendered as it plays, a few thousand frames at a time,
    /// which is what an AudioStreamGenerator is for.
    ///
    /// ⚠ RENDER AHEAD, BUT NOT FAR. The generator's buffer is a quarter of a second; each frame tops it up with
    /// whatever it has room for. Rendering more than it can take would drop audio; rendering less leaves gaps.</summary>
    public partial class MusicPlayer : Node
    {
        /// <summary>The rate the tracker renders at: the engine's own mix rate, so the audio is resampled once (by
        /// the tracker, from each waveform) rather than twice. The engine's rate is 48 kHz on the box, not the
        /// 44.1 kHz this first assumed; Godot would have converted, but a second resampler is only more error.</summary>
        int _rate = 44100;

        AudioStreamPlayer _out;
        AudioStreamGeneratorPlayback _playback;
        TrackerPlayer _tracker;
        short[] _pcm = Array.Empty<short>();
        Vector2[] _frames = Array.Empty<Vector2>();

        public bool IsPlaying => _tracker != null;
        public string Now { get; private set; } = "";

        public override void _Ready()
        {
            _rate = (int)AudioServer.GetMixRate();
            _out = new AudioStreamPlayer
            {
                Stream = new AudioStreamGenerator { MixRate = _rate, BufferLength = 0.25f },
            };
            AddChild(_out);
        }

        public void Play(TrackerModule module, IReadOnlyList<PcmSample> waves, string name)
        {
            Stop();
            _tracker = new TrackerPlayer(module, waves, _rate) { Loop = true };
            Now = name;
            _out.Play();
            _playback = (AudioStreamGeneratorPlayback)_out.GetStreamPlayback();
            GD.Print($"[tpw] music: {name}, {module.Channels} channels, {module.Patterns.Count} patterns, speed {module.Speed} tempo {module.Tempo}, rendered at {_rate} Hz");
        }

        public void Stop()
        {
            _tracker = null;
            _playback = null;
            if (_out != null && _out.Playing) _out.Stop();
            Now = "";
        }

        public override void _Process(double delta)
        {
            if (_tracker == null || _playback == null) return;
            int want = _playback.GetFramesAvailable();
            if (want <= 0) return;
            if (_pcm.Length < want * 2) { _pcm = new short[want * 2]; _frames = new Vector2[want]; }
            int got = _tracker.Render(_pcm, want);
            if (_frames.Length != got) _frames = new Vector2[got];
            for (int i = 0; i < got; i++) _frames[i] = new Vector2(_pcm[i * 2] / 32768f, _pcm[i * 2 + 1] / 32768f);
            _playback.PushBuffer(_frames);
            if (_tracker.Finished) Stop();
        }
    }
}
