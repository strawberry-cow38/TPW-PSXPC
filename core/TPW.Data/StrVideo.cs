using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>One frame's compressed bitstream, reassembled from its sectors.</summary>
    public sealed class StrFrame
    {
        public int Number;
        public int Width;
        public int Height;
        /// <summary>The MDEC bitstream. Still compressed — see Mdec.</summary>
        public byte[] Data = Array.Empty<byte>();
        public int ChunkCount;
        /// <summary>Index within the file of the sector that COMPLETED this frame. The console cannot decode a
        /// frame before its last chunk arrives, and the disc reads at a fixed rate, so this is the frame's
        /// clock. See <see cref="StrMovie.SectorsPerSecond"/>.</summary>
        public int LastSector;
    }

    /// <summary>The `.STR` movie container: PlayStation streaming video with audio interleaved.
    ///
    /// ⭐ WHAT THESE FILES ARE. Five of them, 12-14 MB each, and everyone including me assumed they were the
    /// music — they are 320x176 VIDEO. The actual music was the XM tracker in the archive all along.
    ///
    /// ⚠ THEY ARE NOT THE TITLE-SCREEN ATTRACT CYCLE, which is what fable's report calls GRAV/MIR/JUG. They
    /// play ON ENTERING A WORLD, one per world. Master remembers it that way, and tinyclaw's failed capture
    /// turned into the control: 9,000 frames idling the title screen with ZERO vram uploads, and a playing
    /// movie uploads a frame at a time. The absence carries information because something was predicted to
    /// be there. It also fits the rest of that same report better than the report's own label does — it
    /// found the music picks its module by WORLD INDEX. Four worlds, four modules, one intro movie each.
    ///
    /// ⭐ SO THE DECODER HAS FOUR POSITIVE CONTROLS, NOT ONE — from master, who has played it:
    ///     BF     the Bullfrog logo        (publicly known)
    ///     GRAV   a gravity bounce-house   (space)
    ///     MIR    a freaky mirror thing    (halloween)
    ///     JUG    jungle                   (jungle)
    /// A logo only proves the output is not noise. "A gravity bounce-house" catches the failure a logo
    /// cannot: a DCT close enough to give plausible shapes in roughly the right colours.
    ///
    /// ⚠ ENGLISH/FRENCH/SPANISH/END ARE A GAME OVER SCREEN, NOT AN ENDING, which is what fable's report and this
    /// comment both called them. tinyclaw decoded the same frame from two of them: a boarded-up park gate under
    /// "GAME OVER", and under **げーむおーばー** in END.STR. So they play on a LOSS, and wiring them to finishing
    /// the game would be a bug nobody notices until they win. The text is burned into the picture; the audio is
    /// byte-identical across all four.
    ///
    /// ⭐ END.STR IS THE JAPANESE ONE, and it settles an indexing question. fable's movie table maps 0 ENGLISH,
    /// 1 FRENCH, 4 SPANISH, 7 END. Master independently identified advisor channel 7 as the Japanese voice
    /// actor, so 7 means Japanese on both sides: the table is indexed by LANGUAGE, and the unhelpful filename is
    /// just a filename. The hiragana, where a loanword would normally take katakana, is a choice for young
    /// readers, and it matches that voice: slow, high, 43% pauses.
    ///
    /// ⚠ THE SECTORS ARE NOT ALL THE SAME KIND, and a reader that treats them uniformly produces corrupt
    /// frames rather than an error. The Mode 2 subheader's submode byte says which: bit 0x04 is audio and
    /// bit 0x08 is data. Measured on BF.STR, the first 64 sectors are 56 video and 8 audio — audio every
    /// eighth sector, exactly as fable reported.
    ///
    /// ⚠ AND VIDEO SECTORS ARE FORM 1, NOT FORM 2. Their submode is 0x48: real-time (0x40) plus data
    /// (0x08), with the Form 2 bit clear. So video carries 2048 bytes and the interleaved audio carries
    /// 2324 — reading one size for both drifts 276 bytes per audio sector.
    ///
    /// Each video sector opens with a 32-byte header: magic `0x80010160`, chunk index and count, frame
    /// number, the frame's total byte count, then width and height.</summary>
    public static class StrVideo
    {
        public const uint ChunkMagic = 0x80010160;
        public const int SectorHeaderBytes = 32;
        /// <summary>Video payload per sector: 2048 of user data less the 32-byte chunk header.</summary>
        public const int ChunkPayloadBytes = DiscReader.UserDataSize - SectorHeaderBytes;

        public static bool IsAudioSector(byte[] rawSector) =>
            rawSector != null && rawSector.Length >= 24 && (rawSector[18] & 0x04) != 0;

        public static bool IsVideoSector(byte[] rawSector) =>
            rawSector != null && rawSector.Length >= 24 && (rawSector[18] & 0x04) == 0 && (rawSector[18] & 0x08) != 0;

        /// <summary>Read frames from a `.STR` on the disc, up to <paramref name="maxFrames"/>.
        ///
        /// ⭐ THE ORACLE IS IN THE CONTAINER: every chunk of a frame repeats that frame's total byte count,
        /// so a frame whose assembled chunks do not add up to the declared size has been misassembled. That
        /// is checked rather than assumed, because a short frame still decodes — into rubbish.</summary>
        public static List<StrFrame> ReadFrames(DiscReader disc, DiscFile file, int maxFrames = 1)
        {
            var frames = new List<StrFrame>();
            if (disc == null || file == null) return frames;

            int sectors = file.Length / DiscReader.UserDataSize;
            var asm = new FrameAssembler();
            for (int i = 0; i < sectors && frames.Count < maxFrames; i++)
            {
                var rawSector = disc.ReadRawSector(file.Lba + i);
                if (rawSector == null) break;
                var f = asm.Feed(rawSector, i);
                if (f != null) frames.Add(f);
            }
            return frames;
        }

        /// <summary>Reassembles frames from video sectors fed in disc order.
        ///
        /// ⚠ THE CHUNK COUNT IS READ, NEVER ASSUMED. It is 10 or 11 on this disc, not always 11 (tinyclaw
        /// counted it independently off the same headers). An assembler that assumed 11 would glue the first
        /// chunk of the next frame onto every 10-chunk frame.</summary>
        public sealed class FrameAssembler
        {
            StrFrame _current;
            readonly List<byte> _buf = new();
            int _declared, _seen;

            /// <summary>Frames whose chunks did not add up to the size they declared. Dropped, and counted.</summary>
            public int Misassembled { get; private set; }

            /// <summary>Feed one raw sector. Returns a frame when this sector completes one, else null.</summary>
            public StrFrame Feed(byte[] rawSector, int sectorIndex)
            {
                if (!IsVideoSector(rawSector)) return null;          // audio: not this reader's business

                int at = 24;                                       // Mode 2 user data starts 24 bytes in
                if (BitConverter.ToUInt32(rawSector, at) != ChunkMagic) return null;

                int chunk = BitConverter.ToUInt16(rawSector, at + 4);
                int chunks = BitConverter.ToUInt16(rawSector, at + 6);
                int frameNo = BitConverter.ToInt32(rawSector, at + 8);
                int frameBytes = BitConverter.ToInt32(rawSector, at + 12);
                int w = BitConverter.ToUInt16(rawSector, at + 16);
                int h = BitConverter.ToUInt16(rawSector, at + 18);

                if (chunk == 0)
                {
                    _current = new StrFrame { Number = frameNo, Width = w, Height = h, ChunkCount = chunks };
                    _buf.Clear(); _declared = frameBytes; _seen = 0;
                }
                if (_current == null) return null;                 // joined mid-frame; wait for the next chunk 0

                int take = Math.Min(ChunkPayloadBytes, _declared - _seen);
                if (take <= 0) take = 0;
                for (int k = 0; k < take; k++) _buf.Add(rawSector[at + SectorHeaderBytes + k]);
                _seen += take;

                if (chunk != chunks - 1) return null;

                var done = _current;
                _current = null;
                if (_seen != _declared) { Misassembled++; return null; }
                done.Data = _buf.ToArray();
                done.LastSector = sectorIndex;
                return done;
            }
        }
    }

    /// <summary>A whole movie, demuxed: every frame's bitstream with its place on the clock, and the soundtrack
    /// decoded to PCM.
    ///
    /// ⭐ THE CLOCK IS THE DISC. Nothing in the format states a frame rate. The drive delivers sectors at a
    /// fixed rate, audio and video are interleaved in the proportions they are consumed, and a frame is
    /// ready when its last chunk has been read. So a frame's time is its sector position divided by the read
    /// rate. That rate is not assumed either; the audio fixes it. See <see cref="SectorsPerSecond"/>.
    ///
    /// ⚠ ffmpeg's demuxer reports these files as 15 fps. That number is its hardcoded default, not a
    /// measurement. The sector clock gives 12, on every file, and tinyclaw got 11.99 independently by
    /// walking the chunk headers without ffmpeg anywhere in the path.</summary>
    public sealed class StrMovie
    {
        /// <summary>✅ 150 SECTORS A SECOND: the double-speed drive, and the only rate at which the soundtrack
        /// plays at its own pitch. Derived, not assumed. XA stereo 4-bit gives 2016 samples per channel per
        /// sector, and the audio sits in exactly 1 sector of every 8. So 37,800 Hz needs 37,800 / 2016 * 8
        /// = 150 sectors/s.
        ///
        /// Measured on the four files whose audio runs their full length (BF, GRAV, JUG, MIR): the interleave
        /// is exactly 8.00. The four game-over files run out of audio at 8.1 s and fill the remaining audio slots
        /// with empty sectors (submode 0), so their audio-sector count alone would imply 242 sectors/s. That
        /// is faster than the drive can read, and it is why the rate is fixed here rather than re-derived per
        /// file.</summary>
        public const int SectorsPerSecond = 150;

        public string Name = "";
        /// <summary>Sectors in the file, all kinds. The movie's length on the clock.</summary>
        public int Sectors;
        public readonly List<StrFrame> Frames = new();

        /// <summary>The soundtrack, interleaved L, R, L, R… when stereo.</summary>
        public short[] Audio = Array.Empty<short>();
        public XaAudio.Coding AudioCoding;
        public int AudioSectors;
        /// <summary>Sectors that are neither video nor audio. The game-over files pad with these.</summary>
        public int EmptySectors;
        /// <summary>Audio for a different file/channel number than the first audio sector. The console plays
        /// one channel, so these are skipped rather than mixed in. None on this disc.</summary>
        public int ForeignAudioSectors;
        public int FirstAudioSector = -1;
        public int Misassembled;
        public int XaGroups, XaBadCopies, XaReservedParams;
        /// <summary>Null when the whole soundtrack decoded.</summary>
        public string AudioError;

        public int Width => Frames.Count > 0 ? Frames[0].Width : 0;
        public int Height => Frames.Count > 0 ? Frames[0].Height : 0;
        public double DurationSeconds => Sectors / (double)SectorsPerSecond;
        public double FramesPerSecond => DurationSeconds > 0 ? Frames.Count / DurationSeconds : 0;
        public double AudioSeconds => AudioCoding.Channels > 0 && Audio.Length > 0
            ? Audio.Length / (double)AudioCoding.Channels / AudioCoding.SampleRate : 0;

        /// <summary>When frame i is ready: the moment its last chunk has been read.</summary>
        public double FrameTime(int i) => (Frames[i].LastSector + 1) / (double)SectorsPerSecond;

        /// <summary>When the first audio sector has been read, which is when its sound can start.</summary>
        public double AudioStartSeconds => FirstAudioSector < 0 ? 0 : (FirstAudioSector + 1) / (double)SectorsPerSecond;

        /// <summary>The frame that should be on screen at time t: the last one complete by then, or -1 before
        /// the first. Every frame is independently coded, so jumping straight to it is always valid.</summary>
        public int FrameAt(double t)
        {
            int lo = 0, hi = Frames.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (FrameTime(mid) <= t) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return best;
        }

        /// <summary>What each file is, and when the game plays it.
        ///
        /// ⭐ FROM MASTER, WHO HAS PLAYED IT. The world movies play on entering that world, not on the title
        /// screen. tinyclaw idled the title screen for 9,000 frames and saw zero VRAM uploads, where a playing
        /// movie uploads one frame at a time. The contents were confirmed against decoded frames: the frog
        /// kite, the kid in the ride car, the dark corridor, the purple dinosaur on a unicycle.
        ///
        /// ⚠ THREE WORLD MOVIES, AND NO FOURTH ON THE DISC. There are exactly eight .STR files and this is
        /// all of them, so if a fourth world has an intro movie, it is not a .STR.</summary>
        public static readonly (string File, string What)[] Catalogue =
        {
            ("BF.STR", "Bullfrog logo"),
            ("GRAV.STR", "space world: gravity bounce-house"),
            ("MIR.STR", "halloween world: the freaky mirror"),
            ("JUG.STR", "jungle world"),
            ("ENGLISH.STR", "game over, English"),
            ("FRENCH.STR", "game over, French"),
            ("SPANISH.STR", "game over, Spanish"),
            ("END.STR", "game over, Japanese (げーむおーばー)"),
        };

        public static string Describe(string file)
        {
            string bare = file?.TrimStart('/').ToUpperInvariant() ?? "";
            foreach (var (f, what) in Catalogue) if (f == bare) return what;
            return "";
        }

        /// <summary>The game's own record of how many frames each movie has, read out of its executable.
        ///
        /// ⭐ AN ORACLE THAT SHARES NOTHING WITH THE DEMUXER. TPW.BIN keeps {pointer to file name, last frame
        /// index} pairs, which its player uses to know when to stop: BF.STR 113, GRAV.STR 473, MIR.STR 547,
        /// JUG.STR 458, and 156 for each of the four game-over screens. Those were written from the files when the disc
        /// was built. If the demuxer drops or invents a frame, the two disagree.
        ///
        /// Found by locating each name as a NUL-terminated, word-aligned string, turning its file offset into
        /// a load address, and looking for that address stored as a word. A wrong load address finds nothing,
        /// so this re-checks the base as a side effect.
        ///
        /// ⭐ IT ALSO SETTLES HOW MANY WORLD MOVIES THERE ARE. The three world entries sit together in one
        /// table, GRAV, MIR, JUG, and the word after JUG's is zero. The game knows of three, not four.</summary>
        public static Dictionary<string, int> FrameCountsFromExecutable(byte[] exe, uint loadAddress, IEnumerable<string> names)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (exe == null || names == null) return result;
            foreach (var name in names)
            {
                var needle = System.Text.Encoding.ASCII.GetBytes(name);
                for (int at = 0; at + needle.Length < exe.Length && !result.ContainsKey(name); at += 4)
                {
                    if (exe[at + needle.Length] != 0) continue;
                    int k = 0;
                    while (k < needle.Length && exe[at + k] == needle[k]) k++;
                    if (k != needle.Length) continue;

                    uint addr = loadAddress + (uint)at;
                    for (int p = 0; p + 8 <= exe.Length; p += 4)
                    {
                        if (BitConverter.ToUInt32(exe, p) != addr) continue;
                        uint last = BitConverter.ToUInt32(exe, p + 4);
                        if (last < 100_000) { result[name] = (int)last + 1; break; }
                    }
                }
            }
            return result;
        }

        /// <summary>Read and demux a whole .STR: frames assembled and checked, audio decoded.
        ///
        /// ⚠ NEEDS A RAW IMAGE. Video and audio are told apart by the Mode 2 subheader, and a cooked 2048-byte
        /// image has thrown the subheaders away. That is reported rather than guessed around.</summary>
        public static bool TryLoad(DiscReader disc, DiscFile file, out StrMovie movie, out string error)
        {
            movie = null; error = null;
            if (disc == null || file == null) { error = "no file"; return false; }
            if (!disc.IsRawSectors)
            { error = "cooked image: without the Mode 2 subheaders, video and audio sectors cannot be told apart"; return false; }

            int sectors = file.Length / DiscReader.UserDataSize;
            var m = new StrMovie { Name = file.Name, Sectors = sectors };
            var asm = new StrVideo.FrameAssembler();
            var xa = new XaAudio.Decoder();
            var pcm = new List<short>(sectors / 8 * 4032 + 4032);
            int fileNo = -1, channelNo = -1;

            for (int i = 0; i < sectors; i++)
            {
                var raw = disc.ReadRawSector(file.Lba + i);
                if (raw == null) { error = $"could not read sector {i}"; return false; }

                if (StrVideo.IsAudioSector(raw))
                {
                    if (m.FirstAudioSector < 0)
                    {
                        m.FirstAudioSector = i;
                        fileNo = raw[16]; channelNo = raw[17];
                        m.AudioCoding = XaAudio.ReadCoding(raw);
                    }
                    if (raw[16] != fileNo || raw[17] != channelNo) { m.ForeignAudioSectors++; continue; }
                    m.AudioSectors++;
                    if (m.AudioError != null) continue;
                    if (raw[XaAudio.CodingInfoOffset] != m.AudioCoding.Raw)
                    { m.AudioError = $"coding changes mid-stream at sector {i}"; continue; }
                    if (!xa.TryDecodeSector(raw, pcm, out string ae)) m.AudioError = $"sector {i}: {ae}";
                    continue;
                }

                if (StrVideo.IsVideoSector(raw))
                {
                    var f = asm.Feed(raw, i);
                    if (f != null) m.Frames.Add(f);
                    continue;
                }
                m.EmptySectors++;
            }

            m.Audio = pcm.ToArray();
            m.Misassembled = asm.Misassembled;
            m.XaGroups = xa.Groups;
            m.XaBadCopies = xa.BadCopies;
            m.XaReservedParams = xa.ReservedParams;
            if (m.Frames.Count == 0) { error = "no complete frames"; return false; }
            movie = m;
            return true;
        }
    }
}
