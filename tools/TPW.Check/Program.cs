using System;
using System.Collections.Generic;
using TPW.Data;
using TPW.Launcher;

// Runs the startup asset self-test against a disc image and prints the report, with no engine involved.
//
// ⭐ THIS EXISTS BECAUSE THE SELF-TEST'S OWN CORRECTNESS NEEDS PROVING ON REAL DATA. Unit tests cover it
// against synthetic bytes, which establishes that the logic is right; they cannot establish that the real
// disc is shaped the way the parser believes. Those are different claims and only this answers the second.
// It is also the thing to run when someone says "it won't start": same checks, same wording, no window.
//
// Exit code is the number of failed checks, so it is usable as a gate.
static class Program
{
    /// <summary>Print candidate hashes of the legal screen as the console would hold it: 320x256, 16bpp
    /// little-endian, row-major, no padding. Several candidates because three conventions are genuinely
    /// unknown, and printing all of them says WHICH one the hardware uses rather than just pass/fail.</summary>
    /// <summary>Parse every plain sub-entry and report. ⭐ THE FALSIFIER: a wrong layout does not fail
    /// gracefully on a few, it fails on most, because every field feeds the walk that finds the next one.</summary>
    /// <summary>Which texture page draws with palettes held in which page, read off the FILE.
    ///
    /// ⭐ THE TEST WITH TEETH. tinyclaw logged the same mapping off the live GPU. Twelve pages each landing
    /// on the right one of three has many ways to be wrong and one to be right, and the two methods share
    /// no code, no file and no assumption.</summary>
    /// <summary>Model-space face-normal handedness per texture page, under the FILE's vertex order.
    ///
    /// ⭐ THE PRE-CULL ANALOGUE OF tinyclaw's MEASUREMENT. Theirs is screen-space signed area AFTER the
    /// game's software cull, which this cannot reproduce. But a cull cannot MAKE a set one-handed that was
    /// not already — it only removes the half pointing away. So if terrain really is 100.0% one way over
    /// 213,000 triangles, the file's own vertex order should say so too, and that IS measurable here.
    ///
    /// A closed object should land near 50/50; a heightmap, overwhelmingly one way.</summary>
    static void Winding(GazArchive gaz)
    {
        var up = new SortedDictionary<string, int>();
        var down = new SortedDictionary<string, int>();
        foreach (var e in gaz.Entries)
        {
            var bytes = gaz.Read(e);
            if (!MeshContainer.IsContainer(bytes)) continue;
            if (!MeshContainer.TryParse(bytes, out var c, out _)) continue;
            for (int i = 0; i < c.SubCount; i++)
            {
                if (!c.TryParseMesh(bytes, i, out var mesh, out _)) continue;
                foreach (var face in mesh.Faces)
                {
                    if (face.I0 >= mesh.VertexCount || face.I1 >= mesh.VertexCount || face.I2 >= mesh.VertexCount) continue;
                    var (px, py) = face.TPageOrigin;
                    string page = $"{px},{py}";
                    long ax = mesh.Vertices[face.I1 * 3] - mesh.Vertices[face.I0 * 3];
                    long az = mesh.Vertices[face.I1 * 3 + 2] - mesh.Vertices[face.I0 * 3 + 2];
                    long bx = mesh.Vertices[face.I2 * 3] - mesh.Vertices[face.I0 * 3];
                    long bz = mesh.Vertices[face.I2 * 3 + 2] - mesh.Vertices[face.I0 * 3 + 2];
                    long ny = az * bx - ax * bz;      // Y component of (v1-v0) x (v2-v0)
                    if (ny == 0) continue;            // edge-on: carries no handedness
                    var t = ny > 0 ? up : down;
                    t.TryGetValue(page, out int n); t[page] = n + 1;
                }
            }
        }
        Console.WriteLine("face-normal Y sign per texture page, under the FILE's own vertex order:");
        var pages = new SortedSet<string>();
        foreach (var k in up.Keys) pages.Add(k);
        foreach (var k in down.Keys) pages.Add(k);
        foreach (var p in pages)
        {
            up.TryGetValue(p, out int u); down.TryGetValue(p, out int d);
            int tot = u + d;
            string pct = tot > 0 ? $"{100.0 * Math.Max(u, d) / tot:0.0}% {(u >= d ? "+Y" : "-Y")}" : "-";
            Console.WriteLine($"   {p,-10} +Y {u,7:n0}   -Y {d,7:n0}   {pct}");
        }
    }

    static void Mapping(GazArchive gaz)
    {
        var map = new SortedDictionary<string, SortedDictionary<string, int>>();
        int parsed = 0;
        foreach (var e in gaz.Entries)
        {
            var bytes = gaz.Read(e);
            if (!MeshContainer.IsContainer(bytes)) continue;
            if (!MeshContainer.TryParse(bytes, out var c, out _)) continue;
            for (int i = 0; i < c.SubCount; i++)
            {
                if (!c.TryParseMesh(bytes, i, out var mesh, out _)) continue;
                parsed++;
                foreach (var face in mesh.Faces)
                {
                    var (px, py) = face.TPageOrigin;
                    var (cx, cy) = face.ClutOrigin;
                    // CLUT and tpage coordinates are both in 16-bit VRAM cells, so they compare directly.
                    string page = $"{px},{py}";
                    string holder = $"{(cx / 64) * 64},{(cy >= 256 ? 256 : 0)}";
                    if (!map.TryGetValue(page, out var inner)) map[page] = inner = new SortedDictionary<string, int>();
                    inner.TryGetValue(holder, out int n); inner[holder] = n + 1;
                }
            }
        }
        Console.WriteLine($"texture page -> palette-holder page (from the FILE, via {parsed} parsed meshes):");
        foreach (var kv in map)
        {
            var parts = new List<string>();
            foreach (var h in kv.Value) parts.Add($"{h.Key} ({h.Value:n0})");
            Console.WriteLine($"   {kv.Key,-10} -> {string.Join("  ", parts)}");
        }
    }

    static int Meshes(GazArchive gaz)
    {
        int containers = 0, subs = 0, compressed = 0, expanded = 0, expandedParsed = 0, ok = 0, failed = 0;
        long faces = 0, verts = 0;
        var pages = new SortedDictionary<int, int>();
        string firstFail = "";
        foreach (var e in gaz.Entries)
        {
            var bytes = gaz.Read(e);
            if (!MeshContainer.IsContainer(bytes)) continue;
            if (!MeshContainer.TryParse(bytes, out var c, out string cerr))
            { if (firstFail.Length == 0) firstFail = $"entry #{e.Index}: {cerr}"; continue; }
            containers++;
            for (int i = 0; i < c.SubCount; i++)
            {
                subs++;
                bool packed = c.IsCompressed(i);
                if (packed) compressed++;
                // ⭐ THE HARD ORACLE FOR THE LZSS PORT. A compressed stream must reach its terminator at
                // exactly the size the container table declares, and the bytes must then walk as a mesh.
                // Expansion and parse are counted apart from the plain meshes so a decoder fault cannot hide
                // inside a healthy-looking total.
                if (!c.TryExpand(bytes, i, out var data, out int start, out string xerr))
                { failed++; if (firstFail.Length == 0) firstFail = $"entry #{e.Index} sub {i}: {xerr}"; continue; }
                if (packed) expanded++;
                if (MeshContainer.TryParseMeshAt(data, start, out var mesh, out string merr))
                {
                    ok++; faces += mesh.Faces.Count; verts += mesh.VertexCount;
                    if (packed) expandedParsed++;
                    foreach (var tp in mesh.TPages)
                    {
                        int key = ((tp & 0x0F) * 64) | (((tp & 0x10) != 0 ? 256 : 0) << 16);
                        pages.TryGetValue(key, out int n); pages[key] = n + 1;
                    }
                }
                else { failed++; if (firstFail.Length == 0) firstFail = $"entry #{e.Index} sub {i}: {merr}"; }
            }
        }
        Console.WriteLine($"containers     : {containers}");
        Console.WriteLine($"sub-entries    : {subs}  ({compressed} LZSS-compressed: {expanded} expanded to their declared size, {expandedParsed} of those parsed as meshes)");
        Console.WriteLine($"meshes parsed  : {ok} ok, {failed} failed" + (firstFail.Length > 0 ? $"  first: {firstFail}" : ""));
        Console.WriteLine($"geometry       : {verts:n0} vertices, {faces:n0} faces");
        Console.WriteLine();
        Console.WriteLine("texture pages referenced by meshes (x,y -> how many meshes):");
        foreach (var kv in pages)
            Console.WriteLine($"   {kv.Key & 0xFFFF},{(kv.Key >> 16) & 0xFFFF}  used by {kv.Value} meshes");
        return failed;
    }

    static GazArchive Archive(DiscReader disc)
    {
        var f = disc.Find(AssetSelfTest.AssetArchive);
        if (f == null) { Console.WriteLine("no archive"); return null; }
        if (!GazArchive.TryParse(disc.ReadFile(f), out var gaz, out string gerr))
        { Console.WriteLine("archive: " + gerr); return null; }
        return gaz;
    }

    static void VramHash(DiscReader disc)
    {
        var f = disc.Find(AssetSelfTest.LegalScreen);
        if (f == null) { Console.WriteLine("no legal screen"); return; }
        if (!Tga.TryDecodeVramBlock(disc.ReadFile(f), out var img, out string err))
        { Console.WriteLine("decode failed: " + err); return; }

        Console.WriteLine($"{img.Width}x{img.Height} decoded");

        // ⭐ POSITION-INDEPENDENT INVARIANTS BISECT A HASH MISMATCH. A bare hash says "no" without saying
        // which assumption is wrong. These two split the problem: the peak channel triple is order-SENSITIVE
        // and position-INDEPENDENT, so it isolates channel order; the non-black count cannot change with row
        // order, so it isolates completeness. Together they say whether a mismatch is colour or layout.
        int nonBlack = 0, peakR = 0, peakG = 0, peakB = 0;
        for (int i = 0; i < img.Rgba.Length; i += 4)
        {
            int r = img.Rgba[i] >> 3, g = img.Rgba[i + 1] >> 3, b = img.Rgba[i + 2] >> 3;
            if ((r | g | b) != 0) nonBlack++;
            if (r > peakR) peakR = r;
            if (g > peakG) peakG = g;
            if (b > peakB) peakB = b;
        }
        Console.WriteLine($"  non-black pixels : {nonBlack:n0} of {img.Width * img.Height:n0}");
        Console.WriteLine($"  peak channel     : R={peakR} G={peakG} B={peakB}  (max 31)");

        // ⭐ THE DISTRIBUTION, NOT THE EXTREMUM. A peak is a max over 81,920 samples, so two stray pixels set
        // it -- which is exactly how a published peak of R=14 sent me hunting a colour transform that does not
        // exist, on an image whose red is zero nearly everywhere. Counting how many pixels hold a channel at
        // zero describes the image; the maximum describes its outliers.
        int zeroR = 0, zeroB = 0;
        for (int i = 0; i < img.Rgba.Length; i += 4)
        {
            int r = img.Rgba[i] >> 3, g = img.Rgba[i + 1] >> 3, b = img.Rgba[i + 2] >> 3;
            if ((r | g | b) == 0) continue;
            if (r == 0) zeroR++;
            if (b == 0) zeroB++;
        }
        Console.WriteLine($"  of those, R=0    : {zeroR:n0}      B=0 : {zeroB:n0}");

        // The words as the hardware holds them: BGR555, top-left origin, no padding.
        var vram = new byte[img.Width * img.Height * 2];
        for (int i = 0, o = 0; i < img.Rgba.Length; i += 4, o += 2)
        {
            int r = img.Rgba[i] >> 3, g = img.Rgba[i + 1] >> 3, b = img.Rgba[i + 2] >> 3;
            int wv = (b << 10) | (g << 5) | r;
            vram[o] = (byte)(wv & 0xFF);
            vram[o + 1] = (byte)(wv >> 8);
        }
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            Console.WriteLine($"  sha256 VRAM form : {Convert.ToHexString(sha.ComputeHash(vram)).ToLowerInvariant()}");
            // Independent check: the file's pixel block untouched. Agreement proves the decode round-trips.
            var raw = disc.ReadFile(f);
            var block = new byte[vram.Length];
            Buffer.BlockCopy(raw, Tga.HeaderSize, block, 0, block.Length);
            Console.WriteLine($"  sha256 raw block : {Convert.ToHexString(sha.ComputeHash(block)).ToLowerInvariant()}");
        }

        // ⚠ THE PSX FRAMEBUFFER IS BGR, NOT RGB. A 16-bit VRAM word is 0bbbbbgggggrrrrr -- blue in the high
        // bits -- while a 15-bit TGA word is 0rrrrrgggggbbbbb. Same size, same layout, channels reversed. That
        // is invisible in every structural check: sizes match, the image decodes, the picture looks like a
        // picture. Only a byte-exact comparison against the hardware can see it.
        foreach (bool flip in new[] { false, true })
            foreach (bool bgr in new[] { false, true })
            foreach (int maskBit in new[] { 0, 1 })
            {
                var buf = new byte[img.Width * img.Height * 2];
                for (int y = 0; y < img.Height; y++)
                {
                    int srcRow = flip ? img.Height - 1 - y : y;
                    for (int x = 0; x < img.Width; x++)
                    {
                        int sp = (srcRow * img.Width + x) * 4;
                        // 8-bit back to 5-bit is exact: the decoder expands with (v<<3)|(v>>2), so >>3 inverts it.
                        int r = img.Rgba[sp] >> 3, g = img.Rgba[sp + 1] >> 3, b = img.Rgba[sp + 2] >> 3;
                        int w = bgr ? (maskBit << 15) | (b << 10) | (g << 5) | r
                                    : (maskBit << 15) | (r << 10) | (g << 5) | b;
                        int dp = (y * img.Width + x) * 2;
                        buf[dp] = (byte)(w & 0xFF);
                        buf[dp + 1] = (byte)(w >> 8);
                    }
                }
                using var sha = System.Security.Cryptography.SHA256.Create();
                string hex = Convert.ToHexString(sha.ComputeHash(buf)).ToLowerInvariant();
                Console.WriteLine($"  rows={(flip ? "bottom-up" : "top-down ")} {(bgr ? "BGR" : "RGB")} maskbit={maskBit}  {hex}");
            }
    }

    /// <summary>Every movie on the disc, demuxed and its soundtrack decoded. Exit code is the number of files
    /// with a problem: a frame that did not assemble to its declared size, an XA group whose duplicated
    /// parameters disagree, or audio that failed to decode.</summary>
    static int Movies(DiscReader disc, string outDir, bool frames, HashSet<string> only)
    {
        int bad = 0;
        foreach (var f in disc.Files)
        {
            if (f.IsDirectory || !f.Name.EndsWith(".STR", StringComparison.OrdinalIgnoreCase)) continue;
            if (only != null && !only.Contains(f.Name.ToUpperInvariant())) continue;
            if (!StrMovie.TryLoad(disc, f, out var m, out string err)) { Console.WriteLine($"{f.Name}: {err}"); bad++; continue; }

            Console.WriteLine($"{f.Name,-12} {m.Frames.Count,4} frames {m.Width}x{m.Height}, {m.DurationSeconds:0.00} s, {m.FramesPerSecond:0.00} fps  [{StrMovie.Describe(f.Name)}]");
            Console.WriteLine($"             audio {m.AudioCoding}, {m.AudioSeconds:0.00} s, {m.AudioSectors} sectors from #{m.FirstAudioSector}" +
                              $"; xa groups {m.XaGroups:n0}, bad copies {m.XaBadCopies}, reserved params {m.XaReservedParams}" +
                              $"; empty {m.EmptySectors}, foreign {m.ForeignAudioSectors}, misassembled {m.Misassembled}" +
                              (m.AudioError != null ? $"; AUDIO ERROR {m.AudioError}" : ""));
            if (m.XaBadCopies > 0 || m.Misassembled > 0 || m.AudioError != null) bad++;

            if (outDir != null && m.Audio.Length > 0)
            {
                System.IO.Directory.CreateDirectory(outDir);
                string wav = System.IO.Path.Combine(outDir, System.IO.Path.GetFileNameWithoutExtension(f.Name) + ".wav");
                WriteWav(wav, m.Audio, m.AudioCoding.Channels, m.AudioCoding.SampleRate);
                Console.WriteLine($"             wrote {wav}");
            }

            // --frames: every frame through the real decoder, as raw rgb24 in one file, frame after frame, so a
            // script can compare it with another decoder's output without either side writing images. A frame
            // that fails is written black and counted, so the file stays aligned frame for frame.
            if (outDir != null && frames)
            {
                string rgb = System.IO.Path.Combine(outDir, System.IO.Path.GetFileNameWithoutExtension(f.Name) + ".rgb");
                int failed = 0, structural = 0;
                string first = null;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (var o = System.IO.File.Create(rgb))
                {
                    var row = new byte[m.Width * m.Height * 3];
                    foreach (var fr in m.Frames)
                    {
                        Array.Clear(row);
                        if (Mdec.TryDecodeFrame(fr.Data, fr.Width, fr.Height, null, out var img, out string ferr, out var info)
                            && img.Width == m.Width && img.Height == m.Height)
                        {
                            if (!info.TailPaddingOk || !info.HeaderWordsOk) structural++;
                            for (int i = 0, j = 0; i < row.Length; i += 3, j += 4)
                            { row[i] = img.Rgba[j]; row[i + 1] = img.Rgba[j + 1]; row[i + 2] = img.Rgba[j + 2]; }
                        }
                        else { failed++; first ??= $"frame {fr.Number}: {ferr ?? "wrong size"}"; }
                        o.Write(row);
                    }
                }
                Console.WriteLine($"             wrote {rgb}: {m.Frames.Count - failed}/{m.Frames.Count} frames decoded" +
                                  $" in {sw.ElapsedMilliseconds} ms ({sw.Elapsed.TotalMilliseconds / Math.Max(1, m.Frames.Count):0.00} ms/frame)" +
                                  $"; {structural} not ending exactly on the end-of-frame padding with the header's code count" +
                                  (failed > 0 ? $"; first failure {first}" : ""));
                if (failed > 0 || structural > 0) bad++;
            }
        }
        return bad;
    }

    static int Advisor(DiscReader disc, string outDir, string want)
    {
        var f = disc.Find(AdvisorSpeech.File);
        if (f == null) { Console.WriteLine("no advisor file on this disc"); return 1; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (!AdvisorSpeech.TryScan(disc, f, out var clips, out string err)) { Console.WriteLine("scan: " + err); return 1; }
        int blocks = clips.Count == 0 ? 0 : clips[^1].Block + 1;
        long sectors = 0; foreach (var c in clips) sectors += c.Sectors;
        Console.WriteLine($"{clips.Count:n0} clips in {blocks} blocks, {sectors:n0} audio sectors, scanned in {sw.ElapsedMilliseconds} ms");
        var perBlock = new Dictionary<int, int>();
        foreach (var c in clips) perBlock[c.Block] = perBlock.GetValueOrDefault(c.Block) + 1;
        int full = 0; foreach (var v in perBlock.Values) if (v == AdvisorSpeech.Channels) full++;
        Console.WriteLine($"blocks with all {AdvisorSpeech.Channels} channels: {full}/{blocks}");

        if (outDir == null || want.Length == 0) return 0;
        System.IO.Directory.CreateDirectory(outDir);
        int bad = 0;
        foreach (var spec in want.Split(','))
        {
            var parts = spec.Split(':');
            int b = int.Parse(parts[0]), ch = int.Parse(parts[1]);
            var clip = clips.Find(c => c.Block == b && c.Channel == ch);
            if (clip.Sectors == 0) { Console.WriteLine($"no clip at {spec}"); bad++; continue; }
            if (!AdvisorSpeech.TryDecode(disc, f, clip, out var pcm, out var coding, out string derr)) { Console.WriteLine($"{spec}: {derr}"); bad++; continue; }
            string wav = System.IO.Path.Combine(outDir, $"advisor_b{b:000}_c{ch:00}.wav");
            WriteWav(wav, pcm.Samples, coding.Channels, coding.SampleRate);
            Console.WriteLine($"{clip}: {coding}, {pcm.SampleCount / (double)coding.SampleRate:0.00} s -> {wav}");
        }
        return bad;
    }

    /// <summary>A plain 16-bit PCM WAV: the 44-byte canonical header and the samples.</summary>
    static void WriteWav(string path, short[] pcm, int channels, int rate)
    {
        using var w = new System.IO.BinaryWriter(System.IO.File.Create(path));
        int dataBytes = pcm.Length * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataBytes); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)channels);
        w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataBytes);
        foreach (var v in pcm) w.Write(v);
    }

    static int Main(string[] args)
    {
        // --gaz <file> [--mapping]: the archive reports on a FOLIO.GAZ already pulled off the disc, for a
        // machine that holds the archive but not the image. Same code from the archive down as the disc
        // route, so a pass here is a pass on the real thing. Exit code is the number of failed meshes.
        int gazAt = Array.IndexOf(args, "--gaz");
        if (gazAt >= 0 && gazAt + 1 < args.Length)
        {
            byte[] gb;
            try { gb = System.IO.File.ReadAllBytes(args[gazAt + 1]); }
            catch (Exception e) { Console.WriteLine("could not read: " + e.Message); return 1; }
            if (!GazArchive.TryParse(gb, out var g, out string ge)) { Console.WriteLine("archive: " + ge); return 1; }
            if (Array.IndexOf(args, "--mapping") >= 0) { Mapping(g); return 0; }
            return Meshes(g);
        }

        string path = args.Length > 0 ? args[0] : null;
        if (path == null)
        {
            var probe = GameDataLocator.Probe();
            path = probe.SourcePath;
            if (string.IsNullOrEmpty(path))
            {
                Console.WriteLine("usage: tpwcheck <disc image>   (nothing found by probing)");
                return 1;
            }
            Console.WriteLine($"probed: {path}");
        }

        // --vramhash: emit the legal screen in the console's own framebuffer format so it can be compared
        // against a VRAM dump by hash alone -- neither side has to send an image anywhere.
        //
        // ⚠ THIS GOES THROUGH THE REAL DECODER ON PURPOSE. Re-implementing the conversion in a script to
        // "check the format" would test the script, which is the mistake that just shipped a broken launcher:
        // a harness that builds its own input is not testing the product.
        bool winding = Array.IndexOf(args, "--winding") >= 0;
        bool mapping = Array.IndexOf(args, "--mapping") >= 0;
        bool meshes = Array.IndexOf(args, "--meshes") >= 0;
        bool vramHash = Array.IndexOf(args, "--vramhash") >= 0;
        int texAt = Array.IndexOf(args, "--tex");
        int texIndex = texAt >= 0 && texAt + 1 < args.Length ? int.Parse(args[texAt + 1]) : -1;
        int rawAt = Array.IndexOf(args, "--rawrgba");
        string rawOut = rawAt >= 0 && rawAt + 1 < args.Length ? args[rawAt + 1] : null;

        var id = GameDataLocator.Identify(path);
        Console.WriteLine($"identify: {id.Message}");
        Console.WriteLine($"variant : {id.Variant?.Id ?? "(unrecognised)"}");
        Console.WriteLine();

        DiscReader disc = null;
        try { disc = DiscReader.Open(path); }
        catch (Exception e) { Console.WriteLine("could not open: " + e.Message); return 1; }

        using (disc)
        {
            // --advisor [dir] [--clip B:C,...]: find every clip in the advisor's speech file, print what was found,
            // and write the named clips (block:channel) as WAVs for a human to listen to.
            int advAt = Array.IndexOf(args, "--advisor");
            if (advAt >= 0)
            {
                string outDir = advAt + 1 < args.Length && !args[advAt + 1].StartsWith("--") ? args[advAt + 1] : null;
                int clipAt = Array.IndexOf(args, "--clip");
                string want = clipAt >= 0 && clipAt + 1 < args.Length ? args[clipAt + 1] : "";
                return Advisor(disc, outDir, want);
            }

            // --movies [dir]: demux every .STR, print what each holds, and with a directory write each
            // soundtrack as a WAV, so it can be compared sample for sample with a decoder that shares no code
            // with this one.
            int moviesAt = Array.IndexOf(args, "--movies");
            if (moviesAt >= 0)
            {
                string outDir = moviesAt + 1 < args.Length && !args[moviesAt + 1].StartsWith("--") ? args[moviesAt + 1] : null;
                int onlyAt = Array.IndexOf(args, "--only");
                var only = onlyAt >= 0 && onlyAt + 1 < args.Length
                    ? new HashSet<string>(args[onlyAt + 1].ToUpperInvariant().Split(','))
                    : null;
                return Movies(disc, outDir, Array.IndexOf(args, "--frames") >= 0, only);
            }
            if (rawOut != null && texIndex >= 0)
            {
                var af = disc.Find(AssetSelfTest.AssetArchive);
                if (af == null) { Console.WriteLine("no asset archive on this disc"); return 1; }
                if (!GazArchive.TryParse(disc.ReadFile(af), out var gz, out string gerr))
                { Console.WriteLine("archive: " + gerr); return 1; }
                var pages = VramTexture.DecodeAll(gz);
                Console.WriteLine($"{pages.Count} texture pages");
                if (texIndex >= pages.Count) { Console.WriteLine("out of range"); return 1; }
                System.IO.File.WriteAllBytes(rawOut, pages[texIndex].Image.Rgba);
                Console.WriteLine($"wrote page {texIndex} ({pages[texIndex].Image.Source}) to {rawOut}");
                return 0;
            }
            if (rawOut != null)
            {
                // Dump the decoded image so a human can look at it. Orientation and colour are invisible to
                // every headless check -- both were wrong here while the buffer stayed the right size, format
                // and content. Looking is the only instrument that sees them.
                var lf = disc.Find(AssetSelfTest.LegalScreen);
                if (lf == null) { Console.WriteLine("no legal screen on this disc"); return 1; }
                if (!Tga.TryDecodeVramBlock(disc.ReadFile(lf), out var limg, out string lerr))
                { Console.WriteLine("could not decode: " + lerr); return 1; }
                System.IO.File.WriteAllBytes(rawOut, limg.Rgba);
                Console.WriteLine($"wrote {limg.Width}x{limg.Height} RGBA to {rawOut}");
                return 0;
            }
            if (mapping || meshes || winding)
            {
                var g = Archive(disc);
                if (g == null) return 1;
                if (winding) { Winding(g); return 0; }
                if (mapping) { Mapping(g); return 0; }
                return Meshes(g);
            }
            if (vramHash) { VramHash(disc); return 0; }

            var r = AssetSelfTest.Run(disc);
            Console.WriteLine(r.Summary());
            foreach (var c in r.Checks) Console.WriteLine("  " + c);
            Console.WriteLine();

            Console.WriteLine($"volume '{disc.VolumeId}', {disc.Files.Count} entries:");
            foreach (var f in disc.Files)
                Console.WriteLine($"  {f.Length,12:n0}  lba {f.Lba,-7} {f.Name}");

            return r.Failures;
        }
    }
}
