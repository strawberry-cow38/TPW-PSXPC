using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TPW.Data
{
    public sealed class AssetCheck
    {
        public string Name;
        public bool Ok;
        public string Detail;
        public override string ToString() => $"{(Ok ? "ok  " : "FAIL")} {Name}: {Detail}";
    }

    public sealed class SelfTestReport
    {
        public List<AssetCheck> Checks = new();
        public long ElapsedMs;
        public bool AllOk { get { foreach (var c in Checks) if (!c.Ok) return false; return true; } }
        public int Failures { get { int n = 0; foreach (var c in Checks) if (!c.Ok) n++; return n; } }

        public AssetCheck Add(string name, bool ok, string detail)
        {
            var c = new AssetCheck { Name = name, Ok = ok, Detail = detail };
            Checks.Add(c);
            return c;
        }

        public string Summary() => AllOk
            ? $"all {Checks.Count} asset checks passed in {ElapsedMs} ms"
            : $"{Failures} of {Checks.Count} asset checks FAILED in {ElapsedMs} ms";
    }

    /// <summary>Runs at startup and proves the user's own disc can actually be read, before anything tries to
    /// draw from it.
    ///
    /// ⭐ EVERY CHECK HERE MUST BE ABLE TO FAIL ON A REAL DEFECT. "The file is present" is not a check -- it
    /// passes on a disc whose sector layout was misdetected, because the directory table would still parse
    /// while every byte of content came out shifted. So each check names a quantity the data has to produce
    /// and compares it against something derived INDEPENDENTLY:
    ///   - a TGA's declared width and height against the byte count the file actually has
    ///   - each container's self-declared length against the length the archive table gave it, two numbers
    ///     that only agree if sector maths, file extraction and table parse are ALL correct
    ///   - the archive offsets against each other for overlap and alignment
    /// A self-test that only confirms things exist is the failure it is supposed to catch, wearing a tick.</summary>
    public static class AssetSelfTest
    {
        public const string BootExe = "SLES_026.88";
        public const string LegalScreen = "LEGAL.GFX";
        public const string AssetArchive = "FOLIO.GAZ";
        public const string SpeechStream = "ADVISOR.TPW";
        public const string GameExecutable = "TPW.BIN";
        /// <summary>Where the game image loads. Established two ways, and a 100.00% byte match against live RAM;
        /// see findings/psx-assets.md.</summary>
        public const uint GameExecutableBase = 0x80010000;

        public static SelfTestReport Run(DiscReader disc, bool decodeEveryImage = true)
        {
            var sw = Stopwatch.StartNew();
            var r = new SelfTestReport();
            if (disc == null)
            {
                r.Add("disc", false, "no disc image was opened");
                r.ElapsedMs = sw.ElapsedMilliseconds;
                return r;
            }

            r.Add("disc layout", disc.IsRawSectors,
                disc.IsRawSectors
                    ? "raw 2352-byte sectors, payload found at the Mode 2 offset"
                    : "cooked 2048-byte sectors — a PSX rip should be raw; content offsets may be wrong");

            r.Add("volume", disc.Files.Count > 0, $"{disc.Files.Count} directory entries");

            var boot = disc.Find(BootExe);
            r.Add("boot executable", boot != null,
                boot != null ? $"{BootExe}, {boot.Length:n0} bytes" : $"{BootExe} not present — not a PAL TPW disc");

            CheckLegalScreen(disc, r);
            CheckArchive(disc, r, decodeEveryImage);
            CheckSpeechIsStreaming(disc, r);
            CheckAdvisor(disc, r);
            CheckMovies(disc, r);

            r.ElapsedMs = sw.ElapsedMilliseconds;
            return r;
        }

        static void CheckLegalScreen(DiscReader disc, SelfTestReport r)
        {
            var f = disc.Find(LegalScreen);
            if (f == null) { r.Add("legal screen", false, $"{LegalScreen} not present"); return; }

            byte[] bytes;
            try { bytes = disc.ReadFile(f); }
            catch (Exception e) { r.Add("legal screen", false, "could not read: " + e.Message); return; }

            if (!Tga.TryDecode(bytes, out var img, out string err))
            { r.Add("legal screen", false, $"{LegalScreen} did not decode: {err}"); return; }

            // Independent corroboration: the TGA footer must sit after the pixel data the header implies.
            // If the sector layout were misdetected both of these would still be *present* but would no
            // longer line up, which is exactly the failure a presence check cannot see.
            int footerAt = IndexOf(bytes, Tga.Footer);
            long pixelEnd = Tga.HeaderSize + (long)img.Width * img.Height * 2;
            bool consistent = footerAt < 0 || footerAt >= pixelEnd;

            r.Add("legal screen", consistent,
                $"{img.Width}x{img.Height} RGBA, {img.Rgba.Length:n0} bytes" +
                (footerAt >= 0
                    ? $"; TGA footer at {footerAt:n0}, after the {pixelEnd:n0} bytes the header declares"
                    : "; no TGA footer (not required)"));
        }

        static void CheckArchive(DiscReader disc, SelfTestReport r, bool deep)
        {
            var f = disc.Find(AssetArchive);
            if (f == null) { r.Add("asset archive", false, $"{AssetArchive} not present"); return; }

            byte[] bytes;
            try { bytes = disc.ReadFile(f); }
            catch (Exception e) { r.Add("asset archive", false, "could not read: " + e.Message); return; }

            if (!GazArchive.TryParse(bytes, out var gaz, out string err))
            { r.Add("asset archive", false, $"{AssetArchive} did not parse: {err}"); return; }

            // TryParse already refuses on overlap and out-of-bounds, so reaching here proves those. Alignment
            // is reported separately because it is the strongest single signal that the field layout is right.
            int aligned = gaz.AlignedCount();
            r.Add("asset archive", aligned == gaz.Count,
                $"{gaz.Count} entries, {aligned} aligned to 0x{GazArchive.Alignment:x}, none overlapping or out of bounds");

            if (!deep) return;

            int containers = 0, agree = 0;
            var mismatches = new List<string>();
            foreach (var e in gaz.Entries)
            {
                if (!e.IsContainer) continue;
                containers++;
                if (gaz.ContainerSizeAgrees(e, out int declared)) agree++;
                else if (mismatches.Count < 5) mismatches.Add($"#{e.Index} says {declared} but the table says {e.Size}");
            }

            r.Add("container sizes", containers > 0 && agree == containers,
                containers == 0
                    ? "no containers found — the archive parsed but its contents are not the expected type"
                    : $"{agree}/{containers} containers agree with the table on their own size" +
                      (mismatches.Count > 0 ? "; " + string.Join(", ", mismatches) : ""));

            // ⭐ THE LZSS PORT IS CHECKED HERE BECAUSE THE CHECK CAN FAIL. A compressed sub-entry states its
            // unpacked size in the container table and its stream carries its own terminator; the two meet
            // only if the decoder is right to the last byte, and the buffer then has to walk as a mesh. A
            // wrong bit order or length bias does not fail one of the 25, it fails nearly all of them.
            int packed = 0, expanded = 0, walked = 0;
            string firstLz = null;
            foreach (var e in gaz.Entries)
            {
                if (!e.IsContainer) continue;
                var cb = gaz.Read(e);
                if (!MeshContainer.TryParse(cb, out var c, out _)) continue;
                for (int i = 0; i < c.SubCount; i++)
                {
                    if (!c.IsCompressed(i)) continue;
                    packed++;
                    if (!c.TryExpand(cb, i, out var data, out int start, out string lz))
                    { firstLz ??= $"#{e.Index} sub {i}: {lz}"; continue; }
                    expanded++;
                    if (MeshContainer.TryParseMeshAt(data, start, out _, out string me)) walked++;
                    else firstLz ??= $"#{e.Index} sub {i}: expanded but did not walk as a mesh: {me}";
                }
            }
            r.Add("compressed meshes", packed > 0 && expanded == packed && walked == packed,
                packed == 0
                    ? "no LZSS sub-entries found — the containers parsed but every sub-entry table reads as plain"
                    : $"{expanded}/{packed} LZSS sub-entries expand to exactly their declared size, {walked} walk as meshes" +
                      (firstLz != null ? "; first failure " + firstLz : ""));

            // ⚠ THIS IS A COUNT, NOT A VALIDATION, AND IT SAYS SO. An earlier version read "12/12 texture
            // pages decoded" while the output was coloured noise, because decoding only fails on a short
            // buffer -- so it restated "12 entries are 131,156 bytes" and dressed it as a decode. Whether the
            // pixels are right was settled by rendering one and reading the text in it, which nothing here
            // can do. Word a check for what it actually tests.
            int pageEntries = 0;
            foreach (var e in gaz.Entries) if (VramTexture.LooksLikeTexturePage(e)) pageEntries++;
            if (pageEntries > 0)
                r.Add("texture pages", true,
                    $"{pageEntries} entries sized for a {VramTexture.Width}x{VramTexture.Height} " +
                    $"{VramTexture.BitsPerPixel}bpp page (size only — pixel correctness is not checked here)");

            // ⚠ NO PALETTE CHECK. An earlier version reported "2 strips, 65 of 8,192 well-formed
            // 16-colour tables" and was measuring the wrong entries with a signature the real palettes do not
            // carry. Palettes are 32 bytes of arbitrary colour and cannot be told from any other 32 bytes, so
            // there is nothing here that could fail on a wrong answer. See Clut.
            // ⭐ THIS ONE CAN FAIL, unlike the sheet count: it asserts WHICH entries are banks, not just how
            // many. The two indices were established by content-hash binding against live hardware, so a disc
            // that disagrees is telling us something rather than passing quietly.
            var banks = new List<int>();
            foreach (var e in gaz.Entries) if (SpriteBank.LooksLikeBank(e)) banks.Add(e.Index);
            bool expected = banks.Count == 2 && banks[0] == 0x104 && banks[1] == 0x10C;
            int decoded = 0;
            foreach (int bi in banks) if (SpriteBank.TryDecode(gaz.Read(gaz.Entries[bi]), out _, out _)) decoded++;
            r.Add("sprite banks", expected && decoded == banks.Count,
                expected
                    ? $"{decoded}/{banks.Count} decoded at 0x104 and 0x10C, {SpriteBank.Width}x{SpriteBank.Height} 4bpp"
                    : $"expected banks at 0x104 and 0x10C, found [{string.Join(", ", banks.ConvertAll(b => "0x" + b.ToString("x")))}]");

            CheckAudio(gaz, r);

            // Decode anything that is actually an image. Today that is TGA only; as formats are cracked they
            // join in here and the self-test widens with them rather than needing to be rewritten.
            int images = 0, failed = 0;
            var firstFailure = "";
            foreach (var e in gaz.Entries)
            {
                var payload = gaz.Read(e);
                if (!LooksLikeTga(payload)) continue;
                images++;
                if (!Tga.TryDecode(payload, out _, out string derr))
                {
                    failed++;
                    if (firstFailure.Length == 0) firstFailure = $"entry #{e.Index}: {derr}";
                }
            }
            if (images > 0)
                r.Add("archive images", failed == 0,
                    $"{images - failed}/{images} decoded" + (failed > 0 ? $"; first failure {firstFailure}" : ""));
        }

        /// <summary>Sound banks and music. The strong check here is not "the files parse" -- it is that two
        /// separately-authored files agree about a number neither of them controls.</summary>
        static void CheckAudio(GazArchive gaz, SelfTestReport r)
        {
            var headers = new List<(GazEntry Entry, VabHeader Vab)>();
            foreach (var e in gaz.Entries)
            {
                if (e.Size != VabHeader.SplitHeaderSize) continue;
                if (VabHeader.TryParse(gaz.Read(e), out var v, out _)) headers.Add((e, v));
            }
            if (headers.Count == 0) { r.Add("sound banks", false, "no VAB headers found"); return; }

            // ⭐ THE BODY IS THE ENTRY BEFORE THE HEADER, WHICH IS THE OPPOSITE OF THE NATURAL GUESS. Verified
            // rather than assumed: the VAG size table's sum must equal that entry's size EXACTLY. If the
            // ordering were the other way, or the size units wrong, this is 0 for 9 rather than slightly off.
            int paired = 0, decoded = 0, emptyWaves = 0;
            string firstProblem = "";
            foreach (var (entry, vab) in headers)
            {
                int idx = entry.Index - 1;
                if (idx < 0 || gaz.Entries[idx].Size != vab.BodyBytes)
                {
                    if (firstProblem.Length == 0)
                        firstProblem = $"bank #{entry.Index} wants a {vab.BodyBytes:n0}-byte body; entry #{idx} is " +
                                       (idx < 0 ? "absent" : $"{gaz.Entries[idx].Size:n0}");
                    continue;
                }
                paired++;
                foreach (var wave in vab.SliceBody(gaz.Read(gaz.Entries[idx])))
                {
                    if (wave.Length == 0) { emptyWaves++; continue; }
                    var pcm = Vag.Decode(wave);
                    if (pcm.SampleCount > 0) decoded++;
                    else if (firstProblem.Length == 0) firstProblem = $"a waveform in bank #{entry.Index} decoded to nothing";
                }
            }
            r.Add("sound banks", paired == headers.Count,
                $"{paired}/{headers.Count} banks paired with their waveform body, {decoded:n0} waveforms decoded" +
                (emptyWaves > 0 ? $", {emptyWaves} empty slots" : "") +
                (firstProblem.Length > 0 ? "; " + firstProblem : ""));

            // Music. The XM header declares how many instruments it uses, and the VAB declares how many
            // waveforms it holds. Nothing makes those agree except being the matching pair -- so agreement
            // across every bank is evidence the grouping is right, from a direction the parser cannot fake.
            int modules = 0, agree = 0;
            foreach (var e in gaz.Entries)
            {
                if (!XmModule.TryReadCounts(gaz.Read(e), out _, out _, out int instruments)) continue;
                modules++;
                foreach (var (entry, vab) in headers)
                    if (entry.Index == e.Index - 1 && vab.WaveCount == instruments) { agree++; break; }
            }
            if (modules > 0)
                r.Add("music modules", agree == modules,
                    $"{modules} XM modules, {agree} whose instrument count matches the paired bank's waveform count");
        }

        static void CheckSpeechIsStreaming(DiscReader disc, SelfTestReport r)
        {
            var f = disc.Find(SpeechStream);
            if (f == null) return;   // absent on other regions; not a failure

            // ⚠ THIS FILE IS 346 MB, TWO THIRDS OF THE DISC, AND IT IS NOT DATA. Its sectors are Mode 2 Form
            // 2 audio. Asserting that here stops a future reader from treating it as an archive and spending
            // a long time decoding speech as textures. On a cooked image the subheader is gone and the
            // question cannot be asked, so that is reported rather than failed.
            if (!disc.IsRawSectors) { r.Add("speech stream", true, "cooked image — sector form cannot be checked"); return; }
            bool streaming = disc.IsStreamingSector(f.Lba);
            r.Add("speech stream", streaming,
                streaming
                    ? $"{SpeechStream} is Form 2 streaming media, {f.Length:n0} bytes — correctly not an archive"
                    : $"{SpeechStream} is Form 1 — expected streaming audio; the sector layout may be misdetected");
        }

        /// <summary>The advisor's speech: the first block, all 32 lines of it, found and decoded.
        ///
        /// ⭐ THREE THINGS THAT CAN EACH FAIL. Every audio sector must sit on channel (sector mod 32), or the
        /// interleave is not what the reader assumes and it refuses. Every line must end on the zero-filled
        /// marker sector. And every XA sound group's duplicated parameters must agree when decoded. Only the
        /// first block is checked at startup: the whole file is 346 MB of reads, and the first block is 6,000
        /// sectors that exercise all 32 channels.</summary>
        static void CheckAdvisor(DiscReader disc, SelfTestReport r)
        {
            var f = disc.Find(AdvisorSpeech.File);
            if (f == null || !disc.IsRawSectors) return;   // absent, or unreadable as XA; the speech check says which
            if (!AdvisorSpeech.TryScan(disc, f, out var clips, out int unmarked, out string err, maxSectors: 6000))
            { r.Add("advisor speech", false, err); return; }

            var first = clips.FindAll(c => c.Block == 0);
            int decoded = 0;
            double seconds = 0;
            string problem = null;
            foreach (var c in first)
            {
                if (!AdvisorSpeech.TryDecode(disc, f, c, out var pcm, out var coding, out string derr))
                { problem ??= $"{c}: {derr}"; continue; }
                decoded++;
                seconds += pcm.SampleCount / (double)coding.SampleRate;
            }
            bool channelsOk = first.Count == AdvisorSpeech.Channels;
            bool ok = channelsOk && unmarked == 0 && problem == null;
            r.Add("advisor speech", ok, ok
                ? $"first block: {first.Count} lines, one per channel, each ending on its marker sector; " +
                  $"all decoded ({seconds:0} s), every sound group's parameter copies agree"
                : !channelsOk ? $"first block has {first.Count} lines, expected one per channel ({AdvisorSpeech.Channels})"
                : unmarked > 0 ? $"{unmarked} lines did not end on the marker sector"
                : problem);
        }

        /// <summary>Every movie, demuxed in full.
        ///
        /// ⭐ TWO CHECKS THE DATA CAN FAIL. Every chunk of a frame repeats that frame's total byte count, so a
        /// frame whose chunks do not add up to it was misassembled: a skipped sector, a misread chunk index,
        /// an audio sector taken for video. And every XA sound group carries its parameters twice, so a
        /// soundtrack read from the wrong offset, or a sector that is not audio at all, fails a comparison
        /// random bytes pass with probability 2^-64 per group. A bad rip is caught here rather than as a movie
        /// that stops halfway.
        ///
        /// ✅ The soundtrack decode was checked outside this test against ffmpeg's XA decoder, a separate
        /// implementation sharing no code with this one. Every sample was identical: 721,728 of 721,728 on
        /// BF.STR and 2,987,712 of 2,987,712 on GRAV.STR. The same comparison shifted by one stereo frame
        /// matches 1-4%, so the identity is a result, not a quirk of silent audio.</summary>
        static void CheckMovies(DiscReader disc, SelfTestReport r)
        {
            int files = 0, frames = 0, bad = 0;
            long groups = 0;
            double seconds = 0;
            string firstProblem = "";
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int pictures = 0, badPictures = 0;
            string firstPicture = "";
            foreach (var f in disc.Files)
            {
                if (f.IsDirectory || !f.Name.EndsWith(".STR", StringComparison.OrdinalIgnoreCase)) continue;
                files++;
                if (!StrMovie.TryLoad(disc, f, out var m, out string err))
                {
                    bad++;
                    if (firstProblem.Length == 0) firstProblem = $"{f.Name}: {err}";
                    continue;
                }
                frames += m.Frames.Count;
                groups += m.XaGroups;
                seconds += m.DurationSeconds;
                counts[f.Name] = m.Frames.Count;

                string problem =
                    m.Misassembled > 0 ? $"{m.Misassembled} frames did not add up to their declared size" :
                    m.XaBadCopies > 0 ? $"{m.XaBadCopies} sound groups whose two parameter copies disagree" :
                    m.AudioError != null ? $"soundtrack: {m.AudioError}" :
                    m.AudioSectors == 0 ? "no soundtrack" : null;
                if (problem == null)
                    foreach (var fr in m.Frames)
                        if (fr.Width != m.Width || fr.Height != m.Height)
                        { problem = $"frame {fr.Number} is {fr.Width}x{fr.Height}, the first is {m.Width}x{m.Height}"; break; }
                if (problem != null)
                {
                    bad++;
                    if (firstProblem.Length == 0) firstProblem = $"{f.Name}: {problem}";
                }

                // The picture: first, middle and last frame of each movie through the real decoder. Decoding every
                // frame takes seconds; three per file is enough to catch a disc that is not what the decoder
                // expects, and the two structural checks are what make a decode worth counting.
                foreach (int i in new[] { 0, m.Frames.Count / 2, m.Frames.Count - 1 })
                {
                    var fr = m.Frames[i];
                    pictures++;
                    string why = !Mdec.TryDecodeFrame(fr.Data, fr.Width, fr.Height, null, out _, out string derr, out var info) ? derr
                               : !info.TailPaddingOk ? "did not end on the encoder's end-of-frame padding"
                               : !info.HeaderWordsOk ? $"decoded {info.CodeCount} codes, which the header's size ({info.HeaderWords}) disagrees with"
                               : null;
                    if (why == null) continue;
                    badPictures++;
                    if (firstPicture.Length == 0) firstPicture = $"{f.Name} frame {fr.Number}: {why}";
                }
            }
            if (files == 0) return;   // a disc without movies is not a broken one

            r.Add("movies", bad == 0, bad == 0
                ? $"{files} files, {frames:n0} frames over {seconds:0} s, every frame assembled to its declared size; " +
                  $"{groups:n0} XA sound groups, every one's duplicated parameters agree"
                : $"{bad} of {files} movies have a problem; first: {firstProblem}");

            // ⭐ A DECODE IS ONLY COUNTED IF THE BITSTREAM CAME OUT EXACTLY. Every frame's variable-length codes
            // must end precisely on the encoder's 0111111111 padding, and the number of codes decoded must give
            // back the size the frame's header states. One wrong entry in the code table desynchronises the
            // reader, and it then fails both, whatever the picture looks like. (The pictures themselves were
            // checked against ffmpeg outside this test: every frame of five movies within 8 levels, mean 0.3-0.4.)
            if (pictures > 0)
                r.Add("movie pictures", badPictures == 0, badPictures == 0
                    ? $"{pictures} frames decoded (first, middle and last of each movie), every bitstream consumed exactly " +
                      "to its end-of-frame padding with the code count its header states"
                    : $"{badPictures} of {pictures} frames failed; first: {firstPicture}");

            // The frame counts again, from a source the demuxer never touches: the game's own table.
            var exe = disc.Find(GameExecutable);
            if (exe == null || counts.Count == 0) return;
            var table = StrMovie.FrameCountsFromExecutable(disc.ReadFile(exe), GameExecutableBase, counts.Keys);
            int agree = 0;
            string mismatch = "";
            foreach (var (name, n) in counts)
            {
                if (table.TryGetValue(name, out int want) && want == n) { agree++; continue; }
                if (mismatch.Length == 0)
                    mismatch = table.ContainsKey(name) ? $"{name}: the game says {want} frames, the disc gave {n}"
                                                       : $"{name} is not in the game's table";
            }
            r.Add("movie lengths", agree == counts.Count,
                $"{agree}/{counts.Count} movies have exactly the frame count {GameExecutable}'s own table gives" +
                (mismatch.Length > 0 ? "; " + mismatch : ""));
        }

        static bool LooksLikeTga(byte[] d)
        {
            if (d == null || d.Length < Tga.HeaderSize) return false;
            if (d[1] > 1) return false;
            if (d[2] is not (1 or 2 or 3 or 9 or 10 or 11)) return false;
            int bpp = d[16];
            if (bpp != 8 && bpp != 15 && bpp != 16 && bpp != 24 && bpp != 32) return false;
            int w = BitConverter.ToUInt16(d, 12), h = BitConverter.ToUInt16(d, 14);
            return w > 0 && h > 0 && w <= 2048 && h <= 2048;
        }

        static int IndexOf(byte[] hay, string needle)
        {
            for (int i = 0; i + needle.Length <= hay.Length; i++)
            {
                int k = 0;
                while (k < needle.Length && hay[i + k] == (byte)needle[k]) k++;
                if (k == needle.Length) return i;
            }
            return -1;
        }
    }
}
