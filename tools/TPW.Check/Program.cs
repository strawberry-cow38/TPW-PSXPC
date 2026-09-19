using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Animation tracks on the real disc.
    ///
    /// ⭐ THE ORACLE IS A PROPERTY, NOT A COUNT. The unit tests parse nine hand-written s16 and prove the
    /// stride arithmetic; they cannot tell a correct stride from one that is wrong by a multiple of the
    /// record size, because both produce numbers. These can: a type-8 record read at the RIGHT offset is a
    /// rotation matrix and a type-6 record holds a UNIT quaternion, and neither survives a misread. Anything
    /// under 100% here means the walk is landing off the records, however plausible the totals look.
    ///
    /// Undecoded types are listed rather than ignored, so the remaining work is visible and a type that
    /// silently vanished from the walk cannot be mistaken for a type that does not occur.
    /// Exit contribution is the number of records that failed a property.</summary>
    static int Anim(GazArchive gaz)
    {
        int rests = 0, orthonormal = 0, keys = 0, unit = 0, tracks = 0, monotonic = 0, parsed = 0, animFail = 0;
        int skels = 0, wellFormed = 0, maxDepth = 0, bothEncodings = 0, agree = 0, boneUnit = 0, bonesTot = 0;
        int binds = 0, tiled = 0, sumOne = 0, bindRecs = 0, destOk = 0, noRuns = 0, sumBad = 0;
        int spans = 0, spansMeet = 0;
        int posTracks = 0, posKeys = 0, posSpans = 0, posMeet = 0, empty = 0;
        int posable = 0, moved = 0; long movedVerts = 0;
        double worstAgree = 0;
        var undecoded = new SortedDictionary<int, int>();
        string firstFail = "";
        foreach (var e in gaz.Entries)
        {
            var bytes = gaz.Read(e);
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) continue;
            for (int i = 0; i < c.SubCount; i++)
            {
                if (!c.TryExpand(bytes, i, out var data, out int start, out _)) continue;
                if (!MeshContainer.TryParseMeshAt(data, start, out var mesh, out _)) continue;
                if (mesh.AnimationError != null)
                {
                    animFail++;
                    if (firstFail.Length == 0) firstFail = $"entry #{e.Index} sub {i}: {mesh.AnimationError}";
                    continue;
                }
                if (mesh.Tracks == null) continue;
                parsed++;

                // ⭐ THE CROSS-ENCODING CHECK, and the reason to trust the field offsets at all.
                // The file states a bone's rest rotation twice in two unrelated forms: a quaternion in
                // the 40-byte bone record, and -- for some bones -- a 3x3 matrix in a type-8 track.
                // Nothing makes those agree except reading both correctly. A wrong offset, a wrong
                // component order or a wrong bone index all break the agreement, so this is a far
                // stronger statement than either being individually well-formed.
                // ⭐ THE BINDING'S OWN ORACLE. Weights reaching a vertex must sum to 1.0 and the runs must
                // tile the record table exactly. Both are properties a merely plausible table fails.
                var bd = mesh.Binding;
                // ⚠ Runs and records are SEPARATE populations. 209 meshes carry records with no run
                // table, and the game skips the whole blend loop when the run count is zero (beqz on
                // mesh+0x20 at 0x8002e2f4) -- so those meshes do not scatter at all, and scoring them as
                // tiling failures would be measuring the wrong thing. Counted apart.
                if (bd != null && bd.Records.Length > 0)
                {
                    if (bd.SourceCount == 0) { noRuns++; }
                    else
                    {
                        binds++;
                        if (bd.RunsTileTheTable()) tiled++;
                    }
                    if (bd.WeightsSumToOne(mesh.VertexCount)) sumOne++; else sumBad++;
                    foreach (var r in bd.Records)
                    { bindRecs++; if (r.Vertex < mesh.VertexCount) destOk++; }
                }

                // ⭐ THE END-TO-END CHECK, and the one the unit tests structurally cannot make. Every
                // property above can hold while the pipeline as a whole moves nothing -- a pose that runs
                // and outputs the rest position is indistinguishable from a correct one field by field.
                // So: pose each animated mesh at two times and require the vertices to actually DIFFER.
                if (mesh.Tracks != null && mesh.Binding != null && mesh.Binding.SourceCount > 0
                    && mesh.VertexCount > 0)
                {
                    posable++;
                    var a0 = MeshPose.Evaluate(mesh, 0);
                    var a1 = MeshPose.Evaluate(mesh, 64);
                    int diff = 0;
                    for (int v = 0; v < a0.Vertices.Length && v < a1.Vertices.Length; v++)
                        if (a0.Vertices[v] != a1.Vertices[v]) diff++;
                    if (diff > 0) { moved++; movedVerts += diff; }
                }

                var sk = mesh.Skeleton;
                if (sk != null && sk.Count > 0)
                {
                    skels++;
                    if (sk.IsWellFormed())
                    {
                        wellFormed++;
                        foreach (int dep in sk.Depths()) if (dep > maxDepth) maxDepth = dep;
                    }
                    foreach (var bone in sk.Bones)
                    { bonesTot++; if (Math.Abs(bone.QuatLength() - 1f) < 0.01f) boneUnit++; }

                    foreach (var t in mesh.Tracks)
                    {
                        if (t.Type != 8 || t.BoneIndex < 0 || t.BoneIndex >= sk.Count) continue;
                        var q = sk.Bones[t.BoneIndex].ToMatrix();
                        double err2 = Math.Max(Math.Max(Math.Abs(q.M00 - t.Rest.M00), Math.Abs(q.M01 - t.Rest.M01)),
                                   Math.Max(Math.Max(Math.Abs(q.M02 - t.Rest.M02), Math.Abs(q.M10 - t.Rest.M10)),
                                   Math.Max(Math.Max(Math.Abs(q.M11 - t.Rest.M11), Math.Abs(q.M12 - t.Rest.M12)),
                                   Math.Max(Math.Max(Math.Abs(q.M20 - t.Rest.M20), Math.Abs(q.M21 - t.Rest.M21)),
                                                     Math.Abs(q.M22 - t.Rest.M22)))));
                        bothEncodings++;
                        if (err2 < 0.05) agree++;
                        if (err2 > worstAgree) worstAgree = err2;
                    }
                }
                foreach (var t in mesh.Tracks)
                {
                    if (t.Type == 8) { rests++; if (t.Rest.IsOrthonormal()) orthonormal++; }
                    else if (t.Type == 6)
                    {
                        tracks++;
                        bool up = true;
                        for (int k = 0; k < t.Keys.Length; k++)
                        {
                            keys++;
                            // ⭐ What identifies the second field as a DURATION rather than an unnamed
                            // number: each key's span must end exactly where the next one starts.
                            if (k + 1 < t.Keys.Length)
                            {
                                spans++;
                                if (t.Keys[k].Time + t.Keys[k].Duration == t.Keys[k + 1].Time) spansMeet++;
                            }
                            float q = t.Keys[k].QuatLength();
                            if (q > 0.97f && q < 1.03f) unit++;
                            if (k > 0 && t.Keys[k].Time < t.Keys[k - 1].Time) up = false;
                        }
                        if (up) monotonic++;
                    }
                    else if (t.Positions.Length > 0)
                    {
                        posTracks++; posKeys += t.Positions.Length;
                        if (t.IsTimed)
                            for (int k = 0; k + 1 < t.Positions.Length; k++)
                            {
                                posSpans++;
                                if (t.Positions[k].Time + t.Positions[k].Duration == t.Positions[k + 1].Time)
                                    posMeet++;
                            }
                    }
                    // ⚠ EMPTY IS NOT UNDECODED. A type-3 track with zero records parses perfectly
                    // and has nothing in it; filing those as undecoded overstated the remaining work by
                    // 177 tracks and would have sent someone looking for a format that is already known.
                    else if (t.KeyCount == 0 && t.IsDecoded) empty++;
                    else { undecoded.TryGetValue(t.Type, out int n); undecoded[t.Type] = n + 1; }
                }
            }
        }
        string pc(int a, int b) => b == 0 ? "n/a" : $"{100.0 * a / b:0.0}%";
        Console.WriteLine($"meshes with tracks : {parsed}  ({animFail} failed to walk" +
                          (firstFail.Length > 0 ? $", first: {firstFail}" : "") + ")");
        Console.WriteLine($"type 8 rest poses  : {rests}, orthonormal {orthonormal} ({pc(orthonormal, rests)})");
        Console.WriteLine($"type 6 keyframes   : {keys}, unit quaternion {unit} ({pc(unit, keys)})");
        Console.WriteLine($"keyframe spans     : {spans} consecutive pairs, key.Time+key.Duration == next.Time on {spansMeet} ({pc(spansMeet, spans)})");
        Console.WriteLine($"type 6 tracks      : {tracks}, time non-decreasing {monotonic} ({pc(monotonic, tracks)})");
        Console.WriteLine($"skeletons          : {skels}, single-rooted and parent-before-child {wellFormed} ({pc(wellFormed, skels)}), deepest chain {maxDepth}");
        Console.WriteLine($"bone rest rotations: {bonesTot}, unit quaternion {boneUnit} ({pc(boneUnit, bonesTot)})");
        Console.WriteLine($"vertex binding     : {binds} meshes scatter, runs tile the table {tiled} ({pc(tiled, binds)}); {noRuns} more carry records but no runs and do not scatter");
        Console.WriteLine($"binding weights    : sum to 1.0 on {sumOne} meshes, {sumBad} not");
        Console.WriteLine($"binding records    : {bindRecs}, destination is a real vertex {destOk} ({pc(destOk, bindRecs)})");
        Console.WriteLine($"quaternion vs type-8 matrix, where a bone states both: {agree}/{bothEncodings} agree ({pc(agree, bothEncodings)}), worst {worstAgree:0.000}");
        Console.WriteLine($"posed end to end   : {posable} meshes evaluated at two times, {moved} ({pc(moved, posable)}) actually move, {movedVerts:n0} vertex differences");
        Console.WriteLine($"position tracks    : {posTracks} (types 2/3/4/5), {posKeys} samples; timed spans meet on {posMeet}/{posSpans} ({pc(posMeet, posSpans)}); {empty} more parse to zero records");
        if (undecoded.Count > 0)
        {
            Console.WriteLine("still undecoded (kept as raw bytes, not skipped):");
            foreach (var kv in undecoded) Console.WriteLine($"   type {kv.Key}: {kv.Value} tracks");
        }
        return animFail + (rests - orthonormal) + (keys - unit) + (tracks - monotonic)
             + (skels - wellFormed) + (bonesTot - boneUnit) + (bothEncodings - agree)
             + (binds - tiled) + sumBad + (bindRecs - destOk) + (spans - spansMeet);
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
            Console.WriteLine($"             audio {m.AudioCoding}, {m.AudioSeconds:0.00} s{(m.AudioIsEmpty ? " (EMPTY: skipped by the player)" : "")}, {m.AudioSectors} sectors from #{m.FirstAudioSector}" +
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

    static int Music(DiscReader disc, string outDir)
    {
        var g = Archive(disc);
        if (g == null) return 1;
        System.IO.Directory.CreateDirectory(outDir);
        int bad = 0;
        foreach (var e in g.Entries)
        {
            if (e.Size != VabHeader.SplitHeaderSize || !VabHeader.TryParse(g.Read(e), out var vab, out _)) continue;
            int bodyIndex = e.Index - 1, modIndex = e.Index + 1;
            if (bodyIndex < 0 || modIndex >= g.Entries.Count) continue;
            var waves = new List<PcmSample>();
            foreach (var w in vab.SliceBody(g.Read(g.Entries[bodyIndex]))) waves.Add(Vag.Decode(w));
            if (!TrackerModule.TryParse(g.Read(g.Entries[modIndex]), out var m, out string err)) { Console.WriteLine($"#{modIndex}: {err}"); bad++; continue; }
            var xm = m.ToStandardXm(waves);
            string path = System.IO.Path.Combine(outDir, $"module_{modIndex}.xm");
            System.IO.File.WriteAllBytes(path, xm);
            Console.WriteLine($"#{modIndex}: version {m.Version:x4}, {m.Channels} channels, {m.Patterns.Count} patterns, {m.Instruments.Count} instruments, " +
                              $"{waves.Count} waves, speed {m.Speed} tempo {m.Tempo} -> {path} ({xm.Length:n0} bytes)");
        }
        return bad;
    }

    /// <summary>The (module entry, module bytes, bank waveforms) triples, paired as Music() pairs them.</summary>
    static IEnumerable<(int modIndex, byte[] bytes, List<PcmSample> waves)> MusicEntries(GazArchive g)
    {
        // The pairing rule lives in core (TrackerModule.WaveBanks) so the game and this tool cannot drift apart.
        foreach (var (modIndex, waves) in TrackerModule.WaveBanks(g))
            yield return (modIndex, g.Read(g.Entries[modIndex]), waves);
    }

    /// <summary>One module through TrackerPlayer, start to end (not looping), as interleaved stereo.</summary>
    internal static short[] RenderModule(TrackerModule m, List<PcmSample> waves, int rate, out double renderSeconds, out bool capped)
    {
        var player = new TrackerPlayer(m, waves, rate) { Loop = false };
        var chunks = new List<short[]>();
        long frames = 0, cap = (long)rate * 60 * 20;   // 20 minutes: a runaway song is a bug, not a render
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!player.Finished && frames < cap)
        {
            var buf = new short[4096 * 2];
            int got = player.Render(buf, 4096);
            if (got <= 0) break;
            if (got < 4096) Array.Resize(ref buf, got * 2);
            chunks.Add(buf);
            frames += got;
        }
        sw.Stop();
        renderSeconds = sw.Elapsed.TotalSeconds;
        capped = frames >= cap;
        var pcm = new short[frames * 2];
        long at = 0;
        foreach (var c in chunks) { Array.Copy(c, 0, pcm, at, c.Length); at += c.Length; }
        return pcm;
    }

    /// <summary>--music-render DIR: every module played through TrackerPlayer (the port's own player) to a WAV, so
    /// its output can be compared numerically with libopenmpt's render of the rebuilt .xm from --music. Same
    /// bank/wave/module pairing as Music(). Prints the render speed as a multiple of real time.</summary>
    static int MusicRender(DiscReader disc, string outDir, int rate)
    {
        var g = Archive(disc);
        if (g == null) return 1;
        System.IO.Directory.CreateDirectory(outDir);
        int bad = 0;
        double audioSeconds = 0, renderSeconds = 0;
        foreach (var (modIndex, bytes, waves) in MusicEntries(g))
        {
            if (!TrackerModule.TryParse(bytes, out var m, out string err)) { Console.WriteLine($"#{modIndex}: {err}"); bad++; continue; }
            var pcm = RenderModule(m, waves, rate, out double took, out bool capped);
            string path = System.IO.Path.Combine(outDir, $"module_{modIndex}.wav");
            WriteWav(path, pcm, 2, rate);
            double secs = pcm.Length / 2.0 / rate;
            audioSeconds += secs; renderSeconds += took;
            Console.WriteLine($"#{modIndex}: {m.Channels} channels, speed {m.Speed} tempo {m.Tempo}, {secs:F1}s of audio in {took * 1000:F0} ms " +
                              $"({secs / Math.Max(1e-6, took):F0}x real time){(capped ? " CAPPED" : "")} -> {path}");
        }
        Console.WriteLine($"total {audioSeconds:F0}s of audio in {renderSeconds:F2}s = {audioSeconds / Math.Max(1e-6, renderSeconds):F0}x real time");
        return bad;
    }

    /// <summary>--music-solo DIR MOD: module MOD one channel at a time, for attributing a discrepancy to a channel.
    /// The other channels keep only their position jump / pattern break / speed cells (so the song's structure and
    /// timing survive) and are otherwise silent. Writes DIR/xm/module_MOD_cN.xm (for libopenmpt) and
    /// DIR/out/module_MOD_cN.wav (this player) from the SAME stripped module.</summary>
    static int MusicSolo(DiscReader disc, string outDir, int wantMod)
    {
        var g = Archive(disc);
        if (g == null) return 1;
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(outDir, "xm"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(outDir, "out"));
        foreach (var (modIndex, bytes, waves) in MusicEntries(g))
        {
            if (modIndex != wantMod) continue;
            if (!TrackerModule.TryParse(bytes, out var probe, out string err)) { Console.WriteLine($"#{modIndex}: {err}"); return 1; }
            for (int c = 0; c < probe.Channels; c++)
            {
                TrackerModule.TryParse(bytes, out var m, out _);
                foreach (var grid in m.Patterns)
                    for (int r = 0; r < grid.GetLength(0); r++)
                        for (int k = 0; k < grid.GetLength(1); k++)
                        {
                            if (k == c) continue;
                            var cell = grid[r, k];
                            bool structural = cell.Effect == 0xB || cell.Effect == 0xD || cell.Effect == 0xF;
                            grid[r, k] = structural ? new TrackerCell(0, 0, 0, cell.Effect, cell.Param) : default;
                        }
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, "xm", $"module_{modIndex}_c{c}.xm"), m.ToStandardXm(waves));
                var pcm = RenderModule(m, waves, 44100, out _, out _);
                WriteWav(System.IO.Path.Combine(outDir, "out", $"module_{modIndex}_c{c}.wav"), pcm, 2, 44100);
            }
            Console.WriteLine($"#{modIndex}: {probe.Channels} solo renders -> {outDir}");
            return 0;
        }
        Console.WriteLine($"no module #{wantMod}");
        return 1;
    }

    /// <summary>--music-cells MOD CH T0 T1: the non-empty cells channel CH of module MOD plays between T0 and T1
    /// seconds (CH -1 = every channel), each stamped with the time the player reached its row.</summary>
    static int MusicCells(DiscReader disc, int wantMod, int wantChan, double t0, double t1)
    {
        var g = Archive(disc);
        if (g == null) return 1;
        foreach (var (modIndex, bytes, waves) in MusicEntries(g))
        {
            if (modIndex != wantMod) continue;
            if (!TrackerModule.TryParse(bytes, out var m, out string err)) { Console.WriteLine($"#{modIndex}: {err}"); return 1; }
            var player = new TrackerPlayer(m, waves, 44100) { Loop = false };
            var buf = new short[64 * 2];
            long frames = 0;
            int lastPos = -1, lastRow = -1;
            while (!player.Finished)
            {
                int got = player.Render(buf, 64);
                if (got <= 0) break;
                if (player.SongPosition != lastPos || player.Row != lastRow)
                {
                    lastPos = player.SongPosition; lastRow = player.Row;
                    double t = frames / 44100.0;
                    if (t >= t0 && t <= t1 && lastPos < m.Order.Length)
                    {
                        int pat = m.Order[lastPos];
                        var grid = pat < m.Patterns.Count ? m.Patterns[pat] : null;
                        if (grid != null && lastRow < grid.GetLength(0))
                            for (int c = 0; c < grid.GetLength(1); c++)
                            {
                                if (wantChan >= 0 && c != wantChan) continue;
                                var cell = grid[lastRow, c];
                                if (cell.IsEmpty) continue;
                                Console.WriteLine($"t={t,7:F2} pos={lastPos,3} pat={pat,3} row={lastRow,2} ch={c,2} | note {cell.Note,3} ins {cell.Instrument,3} vol {cell.Volume:x2} fx {cell.Effect:x1}{cell.Param:x2}  (speed {player.Speed} bpm {player.Bpm})");
                            }
                    }
                }
                frames += got;
            }
            return 0;
        }
        Console.WriteLine($"no module #{wantMod}");
        return 1;
    }

    static int Names(DiscReader disc)
    {
        var g = Archive(disc);
        if (g == null) return 1;
        for (int lang = 0; lang < StringTable.EntryByLanguage.Length; lang++)
            if (StringTable.TryRead(g, lang, out var t, out string err))
                Console.WriteLine($"{StringTable.LanguageNames[lang],-9} #{t.Entry}: {t.Strings.Length} strings: {t[0]} | {t[2]} | {t[4]}");
            else Console.WriteLine($"{StringTable.LanguageNames[lang]}: {err}");
        StringTable.TryRead(g, 0, out var en, out _);
        StringTable.TryParse(g.Read(g.Entries[StringTable.IdEntry]), StringTable.IdEntry, out var ids, out _);
        int n = 0;
        foreach (var e in g.Entries)
        {
            var bytes = g.Read(e);
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) continue;
            if (!AttractionRecord.TryRead(bytes, c, out var rec)) continue;
            n++;
            Console.WriteLine($"  #{e.Index,-4} {rec.TypeName,-13} {rec.Width}x{rec.Depth}  {en?[rec.TextId],-28} {ids?[rec.TextId]}");
        }
        Console.WriteLine($"{n} attraction records");
        return 0;
    }

    static int ModelAtlas(DiscReader disc, int want, string outPath)
    {
        var g = Archive(disc);
        if (g == null) return 1;
        var sheets = TextureSheet.FindAll(g);
        int index = 0;
        foreach (var e in g.Entries)
        {
            var bytes = g.Read(e);
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) continue;
            for (int i = 0; i < c.SubCount; i++)
            {
                if (!c.TryParseMesh(bytes, i, out var m, out _) || m.Faces.Count == 0) continue;
                if (index++ != want) continue;
                var t = ModelTexturing.Build(m, sheets);
                // Keep only the texels some face actually samples (inside its UV triangle); everything else is
                // greyed out. Transparent texels that faces DO sample stay transparent, so they show as holes.
                var used = new bool[t.Atlas.Width * t.Atlas.Height];
                long sampled = 0, holes = 0;
                for (int fi = 0; fi < m.Faces.Count; fi++)
                {
                    int tile = t.FaceTile[fi];
                    if (tile < 0) continue;
                    var f = m.Faces[fi];
                    int ox = (tile % t.TilesX) * ModelTexturing.Tile, oy = (tile / t.TilesX) * ModelTexturing.Tile;
                    int x0 = ox + f.U0, y0 = oy + f.V0, x1 = ox + f.U1, y1 = oy + f.V1, x2 = ox + f.U2, y2 = oy + f.V2;
                    int minx = Math.Min(x0, Math.Min(x1, x2)), maxx = Math.Max(x0, Math.Max(x1, x2));
                    int miny = Math.Min(y0, Math.Min(y1, y2)), maxy = Math.Max(y0, Math.Max(y1, y2));
                    long area = (long)(x1 - x0) * (y2 - y0) - (long)(x2 - x0) * (y1 - y0);
                    for (int y = miny; y <= maxy; y++)
                        for (int x = minx; x <= maxx; x++)
                        {
                            long w0 = (long)(x1 - x) * (y2 - y) - (long)(x2 - x) * (y1 - y);
                            long w1 = (long)(x2 - x) * (y0 - y) - (long)(x0 - x) * (y2 - y);
                            long w2 = (long)(x0 - x) * (y1 - y) - (long)(x1 - x) * (y0 - y);
                            bool inside = area >= 0 ? (w0 >= 0 && w1 >= 0 && w2 >= 0) : (w0 <= 0 && w1 <= 0 && w2 <= 0);
                            if (!inside || used[y * t.Atlas.Width + x]) continue;
                            used[y * t.Atlas.Width + x] = true;
                            sampled++;
                            if (t.Atlas.Rgba[(y * t.Atlas.Width + x) * 4 + 3] == 0) holes++;
                        }
                }
                var outRgba = (byte[])t.Atlas.Rgba.Clone();
                for (int k = 0; k < used.Length; k++)
                    if (!used[k]) { outRgba[k * 4] = 60; outRgba[k * 4 + 1] = 60; outRgba[k * 4 + 2] = 60; outRgba[k * 4 + 3] = 255; }
                System.IO.File.WriteAllBytes(outPath, outRgba);
                Console.WriteLine($"texels the faces sample: {sampled:n0}, of which transparent (palette colour 0): {holes:n0} ({100.0 * holes / Math.Max(1, sampled):0.0}%)");
                Console.WriteLine($"model {want}: entry #{e.Index} sub {i}; atlas {t.Atlas.Width}x{t.Atlas.Height} ({t.TilesX}x{t.TilesY} tiles, {t.Atlas.Source}), " +
                                  $"main sheet #{t.MainSheetEntry}; faces {t.Unique} by sprite, {t.Ambiguous} shared, {t.Loose} loose, {t.Unmatched} untextured -> {outPath}");
                return 0;
            }
        }
        Console.WriteLine($"no model {want}");
        return 1;
    }

    /// <summary>--ground MAP SHEET OUT: map entry MAP's ground drawn from straight above with ground sheet SHEET,
    /// through the real terrain builder, as raw RGBA (28 pixels a tile, z up the picture).</summary>
    static int Ground(DiscReader disc, int mapEntry, int sheetEntry, string outPath)
    {
        var g = Archive(disc);
        if (g == null) return 1;
        if (!ParkMap.TryParse(g.Read(g.Entries[mapEntry]), out var map, out string me)) { Console.WriteLine($"map #{mapEntry}: {me}"); return 1; }
        if (!TextureSheet.TryParse(g.Read(g.Entries[sheetEntry]), out var sheet, out string se)) { Console.WriteLine($"sheet #{sheetEntry}: {se}"); return 1; }
        var quads = ParkTerrain.Build(map, sheet.Sprites);
        var sprites = new SortedDictionary<int, int>();
        foreach (var q in quads) sprites[q.Sprite] = sprites.GetValueOrDefault(q.Sprite) + 1;
        int skipped = 0, missing = 0;
        foreach (var t in map.Tiles) { if (t.NoGround) skipped++; else if (t.GroundSprite >= sheet.Sprites.Count) missing++; }
        var img = ParkTerrain.RenderTopDown(map, sheet);
        System.IO.File.WriteAllBytes(outPath, img.Rgba);
        Console.WriteLine($"map #{mapEntry} {map.Width}x{map.Height}, sheet #{sheetEntry} ({sheet.Sprites.Count} sprites): {quads.Count} quads, " +
                          $"{skipped} tiles flagged no-ground, {missing} naming a sprite the sheet lacks");
        Console.WriteLine("sprites used (sprite:quads w x h): " + string.Join("  ", System.Linq.Enumerable.Select(sprites,
            kv => $"{kv.Key}:{kv.Value} {sheet.Sprites[kv.Key].W}x{sheet.Sprites[kv.Key].H}")));
        Console.WriteLine($"-> {outPath} {img.Width}x{img.Height} rgba");
        return 0;
    }

    /// <summary>--scenery: every world's scenery pack through the real reader, and every map's build list against
    /// it. Exit code: packs that failed plus placements naming a model their pack does not have.</summary>
    static int Scenery(DiscReader disc)
    {
        var g = Archive(disc);
        if (g == null) return 1;
        int bad = 0;
        foreach (var w in ParkWorlds.All)
        {
            if (!SceneryPack.TryParse(g.Read(g.Entries[w.SceneryEntry]), out var pack, out string err))
            { Console.WriteLine($"{ParkWorlds.Describe(w)}: scenery pack #{w.SceneryEntry} FAILED: {err}"); bad++; continue; }
            int polys = 0, verts = 0, two = 0;
            foreach (var m in pack.Models) { polys += m.Polygons.Count; verts += m.Vertices.Count; foreach (var t in m.Textures) if (t.DoubleSided) two++; }
            Console.WriteLine($"{ParkWorlds.Describe(w)}: scenery pack #{w.SceneryEntry}: {pack.Models.Count} models, {polys:n0} polygons, " +
                              $"{verts:n0} vertices, {two} double-sided textures");
            foreach (int mapEntry in w.Maps)
            {
                if (!ParkMap.TryParse(g.Read(g.Entries[mapEntry]), out var map, out string me)) { Console.WriteLine($"   map #{mapEntry}: {me}"); bad++; continue; }
                int missing = 0;
                var used = new SortedSet<int>();
                foreach (var pl in map.Scenery) { if (pl.Model >= pack.Models.Count) missing++; else used.Add(pl.Model); }
                bad += missing;
                Console.WriteLine($"   map #{mapEntry}: {map.Scenery.Count} placements of {used.Count} models" +
                                  (missing > 0 ? $", {missing} naming a model the pack lacks" : ""));
            }
        }
        return bad;
    }

    static int ModelTextures(DiscReader disc)
    {
        var g = Archive(disc);
        if (g == null) return 1;
        var sheets = TextureSheet.FindAll(g);
        var clutWords = new List<HashSet<ushort>>();
        foreach (var (_, sh) in sheets) { var set = new HashSet<ushort>(); foreach (var sp in sh.Sprites) set.Add(sp.Clut); clutWords.Add(set); }

        long faces = 0, inArea = 0, exact = 0, uniquelyExact = 0, spriteAny = 0, spriteOne = 0;
        var colourHist = new long[16];
        var listing = new List<(int Browser, int Entry, int Sub, int Faces, int Sheet, int Unique, int Unmatched)>();
        int browserIndex = 0;
        int meshes = 0, meshesAllExact = 0;
        var bestSheetHist = new Dictionary<int, int>();
        foreach (var e in g.Entries)
        {
            var bytes = g.Read(e);
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) continue;
            for (int i = 0; i < c.SubCount; i++)
            {
                if (!c.TryExpand(bytes, i, out var data, out int start, out _)) continue;
                if (!MeshContainer.TryParseMeshAt(data, start, out var mesh, out _)) continue;
                meshes++;
                if (mesh.Faces.Count > 0)
                {
                    var mt = ModelTexturing.Build(mesh, sheets);
                    listing.Add((browserIndex, e.Index, i, mesh.Faces.Count, mt.MainSheetEntry, mt.Unique, mt.Unmatched));
                    browserIndex++;
                }
                for (int q = 0; q < mesh.VertexColours.Length; q++) colourHist[mesh.VertexColours[q] >> 4]++;
                var perSheet = new int[sheets.Count];
                long meshExact = 0;
                foreach (var f in mesh.Faces)
                {
                    faces++;
                    var (cx, cy) = f.ClutOrigin; var (px, py) = f.TPageOrigin;
                    bool anyArea = false; int hits = 0;
                    for (int k = 0; k < sheets.Count; k++)
                    {
                        var sh = sheets[k].Sheet;
                        if (sh.Contains(cx, cy)) anyArea = true;
                        if (clutWords[k].Contains(f.Clut) && sh.Contains(px, py)) { hits++; perSheet[k]++; }
                    }
                    if (anyArea) inArea++;
                    if (hits > 0) { exact++; meshExact++; }
                    if (hits == 1) uniquelyExact++;

                    // Stricter: the face's UVs must fall inside ONE sprite of the sheet that has the same page and
                    // the same palette. Sheets for different worlds share VRAM and palette positions, so the looser
                    // test above cannot say which world a face belongs to; this one might.
                    int umin = Math.Min(f.U0, Math.Min(f.U1, f.U2)), umax = Math.Max(f.U0, Math.Max(f.U1, f.U2));
                    int vmin = Math.Min(f.V0, Math.Min(f.V1, f.V2)), vmax = Math.Max(f.V0, Math.Max(f.V1, f.V2));
                    int inSprite = 0;
                    for (int k = 0; k < sheets.Count; k++)
                    {
                        bool found = false;
                        foreach (var sp in sheets[k].Sheet.Sprites)
                            if (sp.Clut == f.Clut && (sp.TPage & 0x1F) == (f.TPage & 0x1F) &&
                                umin >= sp.U && umax <= sp.U + sp.W && vmin >= sp.V && vmax <= sp.V + sp.H) { found = true; break; }
                        if (found) inSprite++;
                    }
                    if (inSprite > 0) spriteAny++;
                    if (inSprite == 1) spriteOne++;
                }
                if (mesh.Faces.Count > 0 && meshExact == mesh.Faces.Count) meshesAllExact++;
                int best = -1; for (int k = 0; k < sheets.Count; k++) if (perSheet[k] > 0 && (best < 0 || perSheet[k] > perSheet[best])) best = k;
                if (best >= 0) { int id = sheets[best].Entry.Index; bestSheetHist[id] = bestSheetHist.GetValueOrDefault(id) + 1; }
            }
        }
        Console.WriteLine($"{meshes} meshes, {faces:n0} faces");
        Console.WriteLine($"  palette inside SOME sheet's area            : {inArea:n0} ({100.0 * inArea / Math.Max(1, faces):0.0}%)");
        Console.WriteLine($"  palette is one a sheet's sprites use, on a page of that sheet: {exact:n0} ({100.0 * exact / Math.Max(1, faces):0.0}%), of which one sheet only: {uniquelyExact:n0}");
        Console.WriteLine($"  meshes whose EVERY face matches that way     : {meshesAllExact} of {meshes}");
        Console.WriteLine($"  UVs inside a sprite with that page and palette: {spriteAny:n0} ({100.0 * spriteAny / Math.Max(1, faces):0.0}%), in exactly one sheet: {spriteOne:n0}");
        Console.WriteLine("  largest meshes (browser index, entry/sub, faces, main sheet, faces placed by sprite, untextured):");
        foreach (var x in listing.OrderByDescending(x => x.Faces).Take(14))
            Console.WriteLine($"    #{x.Browser,-4} entry {x.Entry}/{x.Sub,-3} {x.Faces,5} faces  sheet #{x.Sheet,-4} {x.Unique,5} by sprite  {x.Unmatched} untextured");
        Console.Write("  vertex colour channel values, by 16s: ");
        for (int q = 0; q < 16; q++) Console.Write($"{q * 16}-{q * 16 + 15}:{colourHist[q]} ");
        Console.WriteLine();
        Console.Write("  best sheet per mesh: ");
        foreach (var kv in bestSheetHist.OrderByDescending(kv => kv.Value)) Console.Write($"#{kv.Key}:{kv.Value} ");
        Console.WriteLine();
        return 0;
    }

    static int Sheets(DiscReader disc, string hashFile)
    {
        var g = Archive(disc);
        if (g == null) return 1;
        var sheets = TextureSheet.FindAll(g);
        int sprites = 0, inside = 0;
        var predicted = new Dictionary<string, HashSet<string>>();
        using var sha = System.Security.Cryptography.SHA256.Create();
        foreach (var (entry, sh) in sheets)
        {
            sprites += sh.Sprites.Count;
            inside += sh.SpritesWithPaletteInside();
            Console.WriteLine($"#{entry.Index,-4} {(sh.Compressed ? "compressed" : "raw       ")} texpage {sh.TPage:x2} " +
                              $"{sh.Columns}x{sh.Rows} pages, {sh.Sprites.Count,3} sprites, {sh.FreeRects.Count,2} free rects, " +
                              $"pixels at +{sh.DataOffset:x}");
            for (int i = 0; i < sh.PageCount * 4; i++)
            {
                var b = sh.Block(i, out int vx, out int vy);
                string h = Convert.ToHexString(sha.ComputeHash(b)).ToLowerInvariant();
                string key = $"{vx},{vy}";
                if (!predicted.TryGetValue(key, out var set)) predicted[key] = set = new HashSet<string>();
                set.Add(h);
            }
        }
        Console.WriteLine($"{sheets.Count} sheets, {sprites:n0} sprites, {inside:n0} with their palette inside their own sheet");
        if (hashFile == null) return inside == sprites ? 0 : 1;

        string blank = Convert.ToHexString(sha.ComputeHash(new byte[TextureSheet.BlockBytes])).ToLowerInvariant();
        using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(hashFile));
        foreach (var cap in doc.RootElement.GetProperty("captures").EnumerateObject())
        {
            int blanks = 0, hit = 0, miss = 0;
            var missed = new List<string>();
            foreach (var blk in cap.Value.EnumerateObject())
            {
                string h = blk.Value.GetString();
                if (h == blank) { blanks++; continue; }
                if (predicted.TryGetValue(blk.Name, out var set) && set.Contains(h)) hit++;
                else { miss++; missed.Add(blk.Name); }
            }
            Console.WriteLine($"capture {cap.Name}: {hit} of {hit + miss} non-blank blocks match a decoded block at the " +
                              $"predicted position ({blanks} blank); unmatched: {string.Join(" ", missed)}");
        }
        return inside == sprites ? 0 : 1;
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
    internal static void WriteWav(string path, short[] pcm, int channels, int rate)
    {
        using var w = new System.IO.BinaryWriter(System.IO.File.Create(path));
        int dataBytes = pcm.Length * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataBytes); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)channels);
        w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataBytes);
        foreach (var v in pcm) w.Write(v);
    }

    /// <summary>Dump one animated mesh posed at several times, as OBJ, so the animation can be LOOKED at
    /// without a GPU. A render change verified only numerically has not been verified.</summary>
    static int PoseDump(GazArchive gaz, int want, string dir)
    {
        int seen = 0;
        foreach (var e in gaz.Entries)
        {
            var bytes = gaz.Read(e);
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) continue;
            for (int i = 0; i < c.SubCount; i++)
            {
                if (!c.TryParseMesh(bytes, i, out var m, out _) || m.Faces.Count == 0) continue;
                if (m.Binding == null || m.Binding.SourceCount == 0) continue;
                if (seen++ != want) continue;
                System.IO.Directory.CreateDirectory(dir);
                foreach (int t in new[] { 0, 16, 32, 48, 64, 96 })
                {
                    var pose = MeshPose.Evaluate(m, t);
                    var sb = new System.Text.StringBuilder();
                    foreach (var v in pose.Vertices) sb.AppendLine($"v {v.X} {v.Y} {-v.Z}");
                    foreach (var fc in m.Faces) sb.AppendLine($"f {fc.I0 + 1} {fc.I1 + 1} {fc.I2 + 1}");
                    System.IO.File.WriteAllText($"{dir}/pose_{t:D3}.obj", sb.ToString());
                }
                Console.WriteLine($"entry #{e.Index} sub {i}: {m.VertexCount} verts, {m.Faces.Count} faces, " +
                                  $"{m.Binding.SourceCount} sources -> {dir}/pose_*.obj");
                return 0;
            }
        }
        Console.WriteLine($"no animated mesh at index {want} (saw {seen})");
        return 1;
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
            if (Array.IndexOf(args, "--anim") >= 0) return Anim(g);
            int pd = Array.IndexOf(args, "--posedump");
            if (pd >= 0 && pd + 2 < args.Length) return PoseDump(g, int.Parse(args[pd + 1]), args[pd + 2]);
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
            // --sheets [vram_block_hashes.json]: every texture sheet through the real decoder; with a file of live
            // VRAM block hashes, count how many decoded blocks match the console hash-exactly AT THE POSITION the
            // sheet header predicts. The hashes come from the running game, so this is the one test here whose
            // answer the decoder cannot have shaped.
            int sheetsAt = Array.IndexOf(args, "--sheets");
            if (sheetsAt >= 0)
            {
                string json = sheetsAt + 1 < args.Length && !args[sheetsAt + 1].StartsWith("--") ? args[sheetsAt + 1] : null;
                return Sheets(disc, json);
            }

            // --model-textures: do the meshes draw from the texture sheets? A face names a page and a palette;
            // count the faces whose palette is one a sheet's own sprite table lists, on a page of that same sheet.
            if (Array.IndexOf(args, "--model-textures") >= 0) return ModelTextures(disc);
            if (Array.IndexOf(args, "--names") >= 0) return Names(disc);
            // --music DIR: every module rebuilt as a standard .xm with its bank's waveforms, for any tracker.
            int musicAt = Array.IndexOf(args, "--music");
            if (musicAt >= 0 && musicAt + 1 < args.Length) return Music(disc, args[musicAt + 1]);
            // --music-render DIR [--rate N]: every module through the port's own TrackerPlayer to WAV.
            // --music-test DIR: synthetic one-rule modules through this player and out as .xm for libopenmpt.
            int testAt = Array.IndexOf(args, "--music-test");
            if (testAt >= 0 && testAt + 1 < args.Length) return MusicTests.Run(args[testAt + 1]);
            int soloAt = Array.IndexOf(args, "--music-solo");
            if (soloAt >= 0 && soloAt + 2 < args.Length) return MusicSolo(disc, args[soloAt + 1], int.Parse(args[soloAt + 2]));
            int cellsAt = Array.IndexOf(args, "--music-cells");
            if (cellsAt >= 0 && cellsAt + 4 < args.Length)
                return MusicCells(disc, int.Parse(args[cellsAt + 1]), int.Parse(args[cellsAt + 2]),
                                  double.Parse(args[cellsAt + 3], System.Globalization.CultureInfo.InvariantCulture),
                                  double.Parse(args[cellsAt + 4], System.Globalization.CultureInfo.InvariantCulture));
            int renderAt = Array.IndexOf(args, "--music-render");
            if (renderAt >= 0 && renderAt + 1 < args.Length)
            {
                int rateAt = Array.IndexOf(args, "--rate");
                int rate = rateAt >= 0 && rateAt + 1 < args.Length ? int.Parse(args[rateAt + 1]) : 44100;
                return MusicRender(disc, args[renderAt + 1], rate);
            }

            // --model-atlas N OUT: the atlas the model browser builds for model N (browser order), as raw RGBA, so
            // the texels a face samples can be looked at directly rather than through a render.
            if (Array.IndexOf(args, "--scenery") >= 0) return Scenery(disc);
            int groundAt = Array.IndexOf(args, "--ground");
            if (groundAt >= 0 && groundAt + 3 < args.Length)
                return Ground(disc, int.Parse(args[groundAt + 1]), int.Parse(args[groundAt + 2]), args[groundAt + 3]);
            int atlasAt = Array.IndexOf(args, "--model-atlas");
            if (atlasAt >= 0 && atlasAt + 2 < args.Length) return ModelAtlas(disc, int.Parse(args[atlasAt + 1]), args[atlasAt + 2]);

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
