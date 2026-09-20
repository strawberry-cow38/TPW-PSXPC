using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using TPW.Data;

namespace TPWGodot
{
    /// <summary>The advisor's voice: one line of ADVISOR.TPW, read off the disc and played.
    ///
    /// ⚠ LOADING COUNTS AS PLAYING. `ParkAdvisor` leaves its speaking state the moment this says it is
    /// quiet, so a read that takes a few frames would otherwise make him mime the whole message and walk
    /// off before a sound came out. The flag is raised when the read starts, not when the audio does.
    ///
    /// Two thirds of the disc is this file: 3,616 recordings, and a message holds up to three of them so
    /// the same advice is never delivered the same way twice running.</summary>
    public partial class AdvisorVoice : Node
    {
        /// <summary>The disc to read from, and which of the eight voices to use.</summary>
        public string DiscPath;
        public int Language;

        AudioStreamPlayer _player;
        List<AdvisorLine> _lines;
        volatile bool _busy;

        public override void _Ready()
        {
            _player = new AudioStreamPlayer();
            AddChild(_player);
        }

        public bool Playing => _busy || (_player?.Playing ?? false);

        public void Stop()
        {
            _busy = false;
            _player?.Stop();
        }

        /// <summary>Play line <paramref name="line"/>. Silently does nothing without a disc, which is what
        /// lets a park run from a dump with no audio.</summary>
        public void Speak(int line)
        {
            if (_busy || string.IsNullOrEmpty(DiscPath) || line < 0) return;
            _busy = true;
            string path = DiscPath;
            int language = Language;
            Task.Run(() =>
            {
                PcmSample pcm = null;
                XaAudio.Coding coding = default;
                string err = null;
                try
                {
                    using var disc = DiscReader.Open(path);
                    var file = disc?.Find(AdvisorSpeech.File);
                    var lines = _lines;
                    if (lines == null && disc != null)
                    {
                        var archive = disc.Find(AssetSelfTest.AssetArchive);
                        if (archive != null && GazArchive.TryParse(disc.ReadFile(archive), out var gaz, out err)
                            && AdvisorSpeech.TryReadIndex(gaz, out var found, out err))
                            lines = _lines = found;
                    }
                    if (file == null) err ??= $"no {AdvisorSpeech.File} on {path}";
                    else if (lines == null) err ??= "no index of advisor lines";
                    else if (line >= lines.Count) err = $"there is no line {line}; the index has {lines.Count}";
                    else AdvisorSpeech.TryDecodeLine(disc, file, lines[line], language, out pcm, out coding, out err);
                }
                catch (System.Exception e) { err = e.Message; }
                _pending = pcm;
                _pendingCoding = coding;
                _pendingError = err;
                CallDeferred(nameof(PlayPending));
            });
        }

        PcmSample _pending;
        XaAudio.Coding _pendingCoding;
        string _pendingError;

        void PlayPending()
        {
            var pcm = _pending;
            _pending = null;
            if (pcm == null || pcm.SampleCount == 0)
            {
                _busy = false;
                GD.PushWarning($"[advisor] voice line failed: {_pendingError}");
                return;
            }
            var bytes = new byte[pcm.SampleCount * 2];
            System.Buffer.BlockCopy(pcm.Samples, 0, bytes, 0, bytes.Length);
            _player.Stream = new AudioStreamWav
            {
                Format = AudioStreamWav.FormatEnum.Format16Bits,
                MixRate = _pendingCoding.SampleRate,
                Stereo = _pendingCoding.Stereo,
                Data = bytes,
            };
            _player.Play();
            // ⚠ The flag drops only once the stream is actually running: `Playing` is the OR of the two, so
            // handing over between them cannot leave a gap the advisor would read as "finished".
            _busy = false;
        }
    }
}
