using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>One line of the advisor's speech: a run of sectors on one XA channel of ADVISOR.TPW.</summary>
    public readonly struct AdvisorClip
    {
        /// <summary>Which of the file's blocks it is in. Every block starts all 32 channels together.</summary>
        public readonly int Block;
        public readonly int Channel;
        /// <summary>Sector index within the file of the clip's first sector.</summary>
        public readonly int FirstSector;
        /// <summary>How many sectors the clip has on its channel (it occupies every 32nd sector from FirstSector).</summary>
        public readonly int Sectors;

        public AdvisorClip(int block, int channel, int firstSector, int sectors)
        { Block = block; Channel = channel; FirstSector = firstSector; Sectors = sectors; }

        /// <summary>Which of the eight languages. fable, from the game's code: channel = lang + 8·(line &amp; 3).</summary>
        public int Language => Channel & 7;

        /// <summary>Which line (0..451) this is, the same in every language: four lines per block, each in eight
        /// languages side by side.</summary>
        public int Line => Block * 4 + (Channel >> 3);

        public override string ToString() => $"line {Line} language {Language} (block {Block} channel {Channel})";
    }

    /// <summary>ADVISOR.TPW: the advisor's voice. 346 MB, two thirds of the disc, and all of it speech.
    ///
    /// ⭐ LAYOUT (MEASURED, every sector of it): XA-ADPCM, mono, 18,900 Hz, 4-bit, in **32 interleaved
    /// channels**. The channel of sector k is always k mod 32 (0 exceptions in 162,111 audio sectors), so each
    /// channel is its own real-time stream: at double speed, one sector in 32 is 4.6875 sectors/s × 4032
    /// samples = exactly 18,900 samples/s. The console plays one line by seeking to it and filtering on the
    /// channel, and the other 31 streams go past unheard.
    ///
    /// The file is 113 BLOCKS. A block starts all 32 channels in the same rotation, and each channel carries
    /// one clip. A clip ends with a zero-filled DATA sector in its slot (3,616 of 3,616 clips), and the slot
    /// is then padded with null audio (channel 255) until the longest clip in the block has finished. So
    /// 113 × 32 = **3,616 clips**, 0.2 s to 37.8 s long, median 6.4 s, 6.8 hours in all.
    ///
    /// ⭐ AND THE 32 CHANNELS ARE 4 LINES × 8 LANGUAGES. fable read it off the game's code: "452 clips indexed
    /// by FOLIO entry 405; channel = lang + 8·(clip &amp; 3)". This scan, which knows nothing of that, finds
    /// 3,616 = 452 × 8 lines in 113 = 452 / 4 blocks: the two agree to the unit, from opposite directions.
    /// So a block holds four lines, each recorded in eight languages side by side, and the game picks one
    /// language's channel. Which language is which number is not yet known.
    ///
    /// ⚠ WHAT EACH LINE SAYS IS NOT IN HERE. The file has no index; FOLIO entry 405 is where the game keeps
    /// it (per fable), and it has not been read yet. Until then a line is identified only by where it sits.
    ///
    /// ✅ The rate, 18,900 Hz, is not just the coding byte's say-so. One sector in 32 at the drive's 150
    /// sectors/s is 4.6875 sectors/s per channel, × 4032 samples = exactly 18,900 samples/s. The interleave
    /// would have to be 1 in 16 for 37,800. And tinyclaw measured the voice: median f0 140 Hz, an ordinary
    /// adult male pitch, with 9-27% of each line in pauses.</summary>
    public static class AdvisorSpeech
    {
        public const string File = "ADVISOR.TPW";
        public const int Channels = 32;

        /// <summary>Find every clip, from the sector subheaders alone. Reads the whole file's subheaders, so run
        /// it off the main thread: 169,344 sectors.
        ///
        /// <paramref name="unmarkedEnds"/> counts clips that ended on anything other than the zero-filled data
        /// sector every clip on the disc ends with. It is a check, not a rule the scan relies on: a clip closes
        /// on any non-audio sector either way. <paramref name="maxSectors"/> limits the scan to the start of the
        /// file; clips still running when it stops are left out rather than returned cut short.</summary>
        public static bool TryScan(DiscReader disc, DiscFile file, out List<AdvisorClip> clips, out string error)
            => TryScan(disc, file, out clips, out _, out error);

        public static bool TryScan(DiscReader disc, DiscFile file, out List<AdvisorClip> clips, out int unmarkedEnds,
                                   out string error, int maxSectors = int.MaxValue)
        {
            clips = new List<AdvisorClip>();
            error = null;
            unmarkedEnds = 0;
            if (disc == null || file == null) { error = "no file"; return false; }
            if (!disc.IsRawSectors) { error = "cooked image: the XA channel numbers live in the subheaders it has lost"; return false; }

            int sectors = Math.Min(file.Length / DiscReader.UserDataSize, maxSectors);
            // Per channel: where its current clip began, or -1 between clips.
            var start = new int[Channels];
            var count = new int[Channels];
            Array.Fill(start, -1);
            var found = new List<(int Channel, int First, int Sectors)>();

            for (int k = 0; k < sectors; k++)
            {
                var raw = disc.ReadRawSector(file.Lba + k);
                if (raw == null) { error = $"could not read sector {k}"; return false; }
                int slot = k % Channels;
                bool audio = StrVideo.IsAudioSector(raw) && raw[17] != 255;
                if (audio && raw[17] != slot) { error = $"sector {k} is on channel {raw[17]}, not {slot}: the interleave is not what this reader assumes"; return false; }

                if (audio)
                {
                    if (start[slot] < 0) { start[slot] = k; count[slot] = 0; }
                    count[slot]++;
                }
                else if (start[slot] >= 0)
                {
                    found.Add((slot, start[slot], count[slot]));
                    start[slot] = -1;
                    if (!StrVideo.IsVideoSector(raw)) unmarkedEnds++;   // the marker is a plain data sector
                }
            }
            // Clips still open at the end: whole if the scan reached the end of the file, cut short otherwise.
            if (sectors == file.Length / DiscReader.UserDataSize)
                for (int c = 0; c < Channels; c++)
                    if (start[c] >= 0) found.Add((c, start[c], count[c]));

            // Blocks: clips that start in the same rotation belong together.
            found.Sort((a, b) => a.First != b.First ? a.First.CompareTo(b.First) : a.Channel.CompareTo(b.Channel));
            int block = -1, rotation = -1;
            foreach (var (channel, first, n) in found)
            {
                if (first / Channels != rotation) { rotation = first / Channels; block++; }
                clips.Add(new AdvisorClip(block, channel, first, n));
            }
            return true;
        }

        /// <summary>Decode one clip to PCM. Its sectors are every 32nd from the first, fed to one XA decoder in
        /// order so the filter history runs across them.</summary>
        public static bool TryDecode(DiscReader disc, DiscFile file, AdvisorClip clip, out PcmSample pcm, out XaAudio.Coding coding, out string error)
        {
            pcm = null; coding = default; error = null;
            if (disc == null || file == null) { error = "no file"; return false; }
            var xa = new XaAudio.Decoder();
            var samples = new List<short>(clip.Sectors * 4032);
            for (int i = 0; i < clip.Sectors; i++)
            {
                var raw = disc.ReadRawSector(file.Lba + clip.FirstSector + i * Channels);
                if (raw == null) { error = "could not read"; return false; }
                if (i == 0) coding = XaAudio.ReadCoding(raw);
                if (!xa.TryDecodeSector(raw, samples, out error)) return false;
            }
            if (xa.BadCopies > 0) { error = $"{xa.BadCopies} sound groups whose parameter copies disagree"; return false; }
            pcm = new PcmSample { Samples = samples.ToArray(), Source = $"advisor {clip}" };
            return true;
        }
    }
}
