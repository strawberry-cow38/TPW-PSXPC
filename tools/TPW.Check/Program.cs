using System;
using System.Collections.Generic;
using System.Linq;
using TPW.Data;
using TPW.Sim;
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
        int spans = 0, spansMeet = 0, t0keys = 0, t0a = 0, t0b = 0, t0tracks = 0;
        int blkTot = 0, blkTight = 0, untimedTracks = 0, untimedKeys = 0;
        int scaleRecs = 0, scaleZero = 0, scaleUniform = 0;
        int posTracks = 0, posKeys = 0, posSpans = 0, posMeet = 0, empty = 0;
        int posable = 0, moved = 0; long movedVerts = 0;
        int wildMeshes = 0, originMeshes = 0, unplacedMeshes = 0; long wildVerts = 0, originVerts = 0, unplacedVerts = 0;
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
                // ⚠ COVER EVERYTHING THAT CAN MOVE, not just the scatter path. This gated on
                // SourceCount, which was every animated mesh until bone skinning landed and then was 30
                // of 233 -- the 203 newly-working models went unverified by the one check that poses
                // anything. A check's SCOPE goes stale the same way a capability flag does.
                bool canMove = mesh.Binding != null && mesh.Binding.SourceCount > 0;
                if (!canMove && mesh.Skeleton != null)
                    foreach (var bb in mesh.Skeleton.Bones) if (bb.SkinCount > 0) { canMove = true; break; }

                if (mesh.Tracks != null && canMove && mesh.VertexCount > 0)
                {
                    posable++;
                    var a0 = MeshPose.Evaluate(mesh, 0);
                    var a1 = MeshPose.Evaluate(mesh, 64);
                    int diff = 0;
                    for (int v = 0; v < a0.Vertices.Length && v < a1.Vertices.Length; v++)
                        if (a0.Vertices[v] != a1.Vertices[v]) diff++;
                    if (diff > 0) { moved++; movedVerts += diff; }

                    // ⚠ "IT CHANGED" IS NOT "IT IS RIGHT". An earlier version of this check stopped at the
                    // line above and passed at 80% while half of every model's vertices were being dragged
                    // to the origin -- because those differ between two times too. So bound the change:
                    //   * nothing may move further than the model is wide;
                    //   * nothing may arrive at the origin that did not start near it.
                    // Both are values a broken pipeline produces and a correct one does not.
                    // ⚠ THE REFERENCE IS THE FILE'S REST POSE, NOT ANOTHER POSE. Comparing two poses
                    // against each other cannot see a fault both of them share: the first version of this
                    // bound compared t=0 against t=64, and when the seeding bug was reintroduced it still
                    // reported zero, because the affected vertices sat at the origin in BOTH poses. A
                    // control computed by the same broken code is not a control.
                    // ⚠ The scale is the larger of the rest and posed extents. Some meshes ship with
                    // ALL-ZERO rest vertices -- entry #67 is 58 vertices at the origin driven by 51
                    // sources -- so a rest-only span is 1 there and every real motion trips the bound.
                    // That is the metric being wrong for a legitimate mesh, not the mesh being wrong.
                    int span = 1;
                    for (int v = 0; v * 3 + 2 < mesh.Vertices.Length; v++)
                    {
                        span = Math.Max(span, (int)Math.Abs(mesh.Vertices[v * 3]));
                        span = Math.Max(span, (int)Math.Abs(mesh.Vertices[v * 3 + 1]));
                        span = Math.Max(span, (int)Math.Abs(mesh.Vertices[v * 3 + 2]));
                    }
                    foreach (var pose in new[] { a0, a1 })
                        foreach (var p in pose.Vertices)
                        {
                            span = Math.Max(span, Math.Abs(p.X));
                            span = Math.Max(span, Math.Abs(p.Y));
                            span = Math.Max(span, Math.Abs(p.Z));
                        }
                    int wild = 0, toOrigin = 0;
                    foreach (var pose in new[] { a0, a1 })
                        for (int v = 0; v < pose.Vertices.Length && v * 3 + 2 < mesh.Vertices.Length; v++)
                        {
                            int rx = (int)mesh.Vertices[v * 3], ry = (int)mesh.Vertices[v * 3 + 1], rz = (int)mesh.Vertices[v * 3 + 2];
                            var (x, y, z) = pose.Vertices[v];
                            long d2 = (long)(x - rx) * (x - rx) + (long)(y - ry) * (y - ry) + (long)(z - rz) * (z - rz);
                            if (d2 > (long)span * span * 4) wild++;
    
                            // ⚠ EXACT zero, not "near the origin". A fractional threshold flagged two
                            // perfectly good poses once the check covered 253 meshes instead of 30 --
                            // a vertex legitimately moving toward the model's centre trips it. The bug
                            // this exists to catch writes an UNCOMPUTED zero, so exact zero against a
                            // non-zero rest is the signature, and it cannot false-positive on a pose.
                            if (x == 0 && y == 0 && z == 0 && (rx != 0 || ry != 0 || rz != 0)) toOrigin++;
                        }
                    // ⭐ THE ASSERTION WITH TEETH, and it took three tries to find one that had any.
                    // "Collapsed to the origin" cannot fire here: 104 of entry #43's 364 rest vertices are
                    // exactly (0,0,0) in the FILE, because the animation is what places them. So a wrongly
                    // zeroed vertex is indistinguishable from a legitimately unplaced one by position alone.
                    // What IS distinguishable: a vertex the animation covers must come out PLACED. Any
                    // covered vertex still sitting exactly at (0,0,0) after posing means its source never
                    // got a value -- which is exactly the seeding bug, and it fails nothing else.
                    int unplaced = 0;
                    if (mesh.Binding != null)
                        foreach (var run in mesh.Binding.Runs)
                            for (int k = 0; k < run.Count; k++)
                            {
                                int rec = run.Start + k;
                                if (rec < 0 || rec >= mesh.Binding.Records.Length) continue;
                                int vtx = mesh.Binding.Records[rec].Vertex;
                                if (vtx < a0.Vertices.Length && a0.Vertices[vtx] == (0, 0, 0)) unplaced++;
                            }
                    if (unplaced > 0) { unplacedMeshes++; unplacedVerts += unplaced; }

                    if (wild > 0) { wildMeshes++; wildVerts += wild; }
                    if (toOrigin > 0) { originMeshes++; originVerts += toOrigin; }
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
                    // ⭐ THE TIGHT ONE. Every 16-byte block on the disc, types 0/1/6/7, first and
                    // second. The file normalises to EXACTLY 4096, so the real invariant is +-1 of
                    // 4096, not the +-123 that a 0.03 tolerance allows. That width is not pedantry:
                    // at +-41 a column-shuffled control PASSES this test on 73% of type-0 second
                    // blocks, because most of them are near-identity and a loose band cannot tell a
                    // near-identity quadruple from a shuffle of near-identity columns. At +-1 the
                    // shuffle drops to 27% and the first blocks to 4-6%, while the real records stay
                    // at 100%. A check whose band admits the degenerate case is not a check.
                    const float Tight = 1.5f / 4096f;
                    if (t.Type is 0 or 1 or 6 or 7)
                    {
                        for (int k = 0; k < t.Keys.Length; k++)
                        { blkTot++; if (Math.Abs(t.Keys[k].QuatLength() - 1f) <= Tight) blkTight++; }
                        for (int k = 0; k < t.Scales.Length; k++)
                        {
                            blkTot++; if (Math.Abs(t.Scales[k].QuatLength() - 1f) <= Tight) blkTight++;
                            // ⭐ A SCALE IS NEVER ZERO -- a zero component collapses the bone to a plane.
                            // This is the check that would fail if these three halfwords were something
                            // else read at this offset: the FIRST block's vector, an ordinary
                            // translation, is exactly 0 on 9.3% of its components.
                            var sc = t.Scales[k];
                            scaleRecs++;
                            if (sc.Sx == 0 || sc.Sy == 0 || sc.Sz == 0) scaleZero++;
                            if (sc.Sx == sc.Sy && sc.Sy == sc.Sz) scaleUniform++;
                        }
                    }
                    if (t.Type == 0 && t.Scales.Length > 0)
                    {
                        // Type 0 carries TWO quaternions per key; both must be unit length.
                        for (int k = 0; k < t.Keys.Length; k++)
                        {
                            t0keys++;
                            if (Math.Abs(t.Keys[k].QuatLength() - 1f) < 0.03f) t0a++;
                            if (k < t.Scales.Length && Math.Abs(t.Scales[k].QuatLength() - 1f) < 0.03f) t0b++;
                        }
                    }
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
                    else if (t.Type == 0 && t.Keys.Length > 0) { t0tracks++; }
                    // Types 1 and 7, the untimed bone tracks. ⚠ NOT checked for spans meeting, and
                    // that omission is the point: their Time and Duration are SYNTHESISED by the
                    // parser (k and 1), so "spans meet" would be a property of my own arithmetic and
                    // would score 100% no matter what the file said. A check on a field the checker
                    // wrote is not a check on the file.
                    else if (t.Type is 1 or 7 && t.Keys.Length > 0)
                    { untimedTracks++; untimedKeys += t.Keys.Length; }
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
        Console.WriteLine($"type 0 tracks      : {t0tracks}, {t0keys} keyframes; first quaternion unit {t0a} ({pc(t0a, t0keys)}), second unit {t0b} ({pc(t0b, t0keys)})");
        Console.WriteLine($"untimed bone tracks: {untimedTracks} (types 1/7), {untimedKeys} samples, one per tick -- no timing to verify");
        Console.WriteLine($"bone scales        : {scaleRecs} (types 0/1), none zero on {scaleRecs - scaleZero} ({pc(scaleRecs - scaleZero, scaleRecs)}); uniform x==y==z on {scaleUniform} ({pc(scaleUniform, scaleRecs)}, shuffled control 0.6%)");
        Console.WriteLine($"quaternion blocks  : {blkTot} across types 0/1/6/7, normalised to 4096 within +-1.5 on {blkTight} ({pc(blkTight, blkTot)})");
        Console.WriteLine($"keyframe spans     : {spans} consecutive pairs, key.Time+key.Duration == next.Time on {spansMeet} ({pc(spansMeet, spans)})");
        Console.WriteLine($"type 6 tracks      : {tracks}, time non-decreasing {monotonic} ({pc(monotonic, tracks)})");
        Console.WriteLine($"skeletons          : {skels}, single-rooted and parent-before-child {wellFormed} ({pc(wellFormed, skels)}), deepest chain {maxDepth}");
        Console.WriteLine($"bone rest rotations: {bonesTot}, unit quaternion {boneUnit} ({pc(boneUnit, bonesTot)})");
        Console.WriteLine($"vertex binding     : {binds} meshes scatter, runs tile the table {tiled} ({pc(tiled, binds)}); {noRuns} more carry records but no runs and do not scatter");
        Console.WriteLine($"binding weights    : sum to 1.0 on {sumOne} meshes, {sumBad} not");
        Console.WriteLine($"binding records    : {bindRecs}, destination is a real vertex {destOk} ({pc(destOk, bindRecs)})");
        Console.WriteLine($"quaternion vs type-8 matrix, where a bone states both: {agree}/{bothEncodings} agree ({pc(agree, bothEncodings)}), worst {worstAgree:0.000}");
        Console.WriteLine($"posed end to end   : {posable} meshes evaluated at two times, {moved} ({pc(moved, posable)}) actually move, {movedVerts:n0} vertex differences");
        Console.WriteLine($"                     bounded: {wildVerts} moved further than the model is wide ({wildMeshes} meshes), {originVerts} collapsed to the origin ({originMeshes} meshes), {unplacedVerts} covered-but-unplaced ({unplacedMeshes} meshes)");
        Console.WriteLine($"position tracks    : {posTracks} (types 2/3/4/5), {posKeys} samples; timed spans meet on {posMeet}/{posSpans} ({pc(posMeet, posSpans)}); {empty} more parse to zero records");
        if (undecoded.Count > 0)
        {
            Console.WriteLine("still undecoded (kept as raw bytes, not skipped):");
            foreach (var kv in undecoded) Console.WriteLine($"   type {kv.Key}: {kv.Value} tracks");
        }
        // Exit code is the number of failed checks, so this is usable as a gate. ⚠ Two earlier edits to
        // this line silently did not match and the new bounds were never counted -- the report showed the
        // fault while the exit code stayed 0. Anything added above belongs here too.
        //
        // ⚠ AND THE COUNT IS CLAMPED, because a process exit code is one byte. A mutation test that
        // broke 6,195 records exited 51 -- 6195 mod 256 -- which is still non-zero and still failed the
        // gate, but 6,144 of them would have exited 0 and PASSED. A gate whose failure value can wrap
        // onto its success value is not a gate; the report above carries the real number.
        int failures = animFail + (rests - orthonormal) + (keys - unit) + (tracks - monotonic)
             + (skels - wellFormed) + (bonesTot - boneUnit) + (bothEncodings - agree)
             + (binds - tiled) + sumBad + (bindRecs - destOk) + (spans - spansMeet)
             + (posSpans - posMeet) + wildMeshes + originMeshes + unplacedMeshes
             + (t0keys - t0a) + (t0keys - t0b) + (blkTot - blkTight) + scaleZero;
        return failures == 0 ? 0 : Math.Clamp(failures, 1, 255);
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

    /// <summary>--model-faces ENTRY: every textured face of one model entry with the texture the port gives it --
    /// page, palette, UV box, and which sheets hold a sprite with that page and palette, whether the UV box fits
    /// inside it, and by how much it overshoots when it does not. For chasing a wrong or stretched texture.</summary>
    static int ModelFaces(DiscReader disc, int wantEntry, int onlyClut = -1)
    {
        var g = Archive(disc);
        if (g == null) return 1;
        var sheets = TextureSheet.FindAll(g);
        var e = g.Entries[wantEntry];
        var bytes = g.Read(e);
        if (!MeshContainer.IsContainer(bytes)) { Console.WriteLine($"#{wantEntry}: not a mesh container"); return 1; }
        if (!MeshContainer.TryParse(bytes, out var c, out string ce)) { Console.WriteLine($"#{wantEntry}: {ce}"); return 1; }
        for (int i = 0; i < c.SubCount; i++)
        {
            if (!c.TryParseMesh(bytes, i, out var m, out _) || m.Faces.Count == 0) continue;
            var t = ModelTexturing.Build(m, sheets);
            Console.WriteLine($"#{wantEntry} sub {i}: {m.Faces.Count} faces; {t.Unique} by sprite, {t.Ambiguous} shared, {t.Loose} loose, {t.Unmatched} untextured; main sheet #{t.MainSheetEntry}");
            var groups = new SortedDictionary<(ushort, ushort), (int n, int umin, int vmin, int umax, int vmax)>();
            foreach (var f in m.Faces)
            {
                var k = ((ushort)(f.TPage & 0x1FF), f.Clut);
                int umin = Math.Min(f.U0, Math.Min(f.U1, f.U2)), umax = Math.Max(f.U0, Math.Max(f.U1, f.U2));
                int vmin = Math.Min(f.V0, Math.Min(f.V1, f.V2)), vmax = Math.Max(f.V0, Math.Max(f.V1, f.V2));
                groups[k] = groups.TryGetValue(k, out var gv)
                    ? (gv.n + 1, Math.Min(gv.umin, umin), Math.Min(gv.vmin, vmin), Math.Max(gv.umax, umax), Math.Max(gv.vmax, vmax))
                    : (1, umin, vmin, umax, vmax);
            }
            foreach (var ((tp, cl), gv) in groups)
            {
                var hits = new List<string>();
                foreach (var (se, sh) in sheets)
                    foreach (var sp in sh.Sprites)
                        if (sp.Clut == cl && (sp.TPage & 0x1F) == (tp & 0x1F))
                        {
                            bool fits = gv.umin >= sp.U && gv.umax <= sp.U + sp.W && gv.vmin >= sp.V && gv.vmax <= sp.V + sp.H;
                            hits.Add($"#{se.Index}:{sp.U},{sp.V} {sp.W}x{sp.H}{(fits ? "" : " (UVs overshoot)")}");
                        }
                Console.WriteLine($"   tpage {tp:x3} clut {cl:x4}: {gv.n,3} faces, UVs {gv.umin},{gv.vmin}..{gv.umax},{gv.vmax}  sprites: {(hits.Count > 0 ? string.Join("; ", hits) : "NONE")}");
            }
            if (onlyClut >= 0)
                foreach (var f in m.Faces)
                {
                    if (f.Clut != onlyClut) continue;
                    string V(int vi) => vi < m.VertexCount ? $"({m.Vertices[vi * 3]},{m.Vertices[vi * 3 + 1]},{m.Vertices[vi * 3 + 2]})" : "(?)";
                    Console.WriteLine($"      kind {f.Kind:x2}  uv ({f.U0},{f.V0}) ({f.U1},{f.V1}) ({f.U2},{f.V2})  v {f.I0}{V(f.I0)} {f.I1}{V(f.I1)} {f.I2}{V(f.I2)}");
                }
        }
        return 0;
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
    /// <summary>Dump entry ENTRY, sub-entry SUB, posed at several times, as OBJ.
    ///
    /// ⚠ ADDRESSED BY THE ENTRY NUMBER, NOT BY POSITION AMONG ANIMATED MESHES. It used to take an
    /// ordinal, and widening the "which meshes count as animated" filter silently renumbered everything:
    /// index 0 stopped meaning entry 43 and started meaning entry 0. I then spent a long stretch
    /// comparing one model's file data against another model's posed output and concluding, reasonably,
    /// that the poser was badly broken. An identifier that moves when an unrelated filter changes is not
    /// an identifier.</summary>
    static int PoseDump(GazArchive gaz, int entry, int sub, string dir)
    {
        foreach (var e in gaz.Entries)
        {
            if (e.Index != entry) continue;
            var bytes = gaz.Read(e);
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _))
            { Console.WriteLine($"entry #{entry} is not a mesh container"); return 1; }
            for (int i = 0; i < c.SubCount; i++)
            {
                if (i != sub) continue;
                if (!c.TryParseMesh(bytes, i, out var m, out _) || m.Faces.Count == 0)
                { Console.WriteLine($"entry #{entry} sub {i} has no faces"); return 1; }
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
        Console.WriteLine($"entry #{entry} sub {sub} not found");
        return 1;
    }

    /// <summary>List every attraction by entry, with its English name, so a model can be found by what it
    /// IS rather than by scrolling. The name comes from the game's own string table via the attraction
    /// record in the same entry -- not a guess.</summary>
    static int Attractions(GazArchive gaz, string filter)
    {
        StringTable.TryRead(gaz, 0, out var english, out _);
        if (english == null) { Console.WriteLine("no English string table"); return 1; }
        int n = 0;
        foreach (var e in gaz.Entries)
        {
            var bytes = gaz.Read(e);
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) continue;
            if (!AttractionRecord.TryRead(bytes, c, out var rec)) continue;
            string name = english[rec.TextId];
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            Console.WriteLine($"entry #{e.Index,-4} {name}  ({rec.TypeName}, {rec.Width}x{rec.Depth} tiles, {c.SubCount} sub-entries)");
            n++;
        }
        return n == 0 ? 1 : 0;
    }

    /// <summary>For each textured face group of one mesh, how much of the page region its UVs cover is
    /// actually HELD by the sheet the port picked. A texel the sheet does not hold is skipped by
    /// RenderPage and left transparent, so partial coverage shows up as a smeared band rather than a
    /// missing texture -- which is the shape of the reported streaking.</summary>
    static int Coverage(GazArchive gaz, int entry)
    {
        var sheets = TextureSheet.FindAll(gaz);
        foreach (var e in gaz.Entries)
        {
            if (e.Index != entry) continue;
            var bytes = gaz.Read(e);
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) return 1;
            if (!c.TryParseMesh(bytes, 0, out var m, out _)) return 1;
            var tex = ModelTexturing.Build(m, sheets);
            var seen = new SortedDictionary<int, (int held, int total, int uw, int vh, ushort tp, ushort cl)>();
            for (int fi = 0; fi < m.Faces.Count; fi++)
            {
                int t = tex.FaceTile[fi];
                if (t < 0 || seen.ContainsKey(t)) continue;
                var f = m.Faces[fi];
                int u0 = Math.Min(f.U0, Math.Min(f.U1, f.U2)), u1 = Math.Max(f.U0, Math.Max(f.U1, f.U2));
                int v0 = Math.Min(f.V0, Math.Min(f.V1, f.V2)), v1 = Math.Max(f.V0, Math.Max(f.V1, f.V2));
                // widen to the whole group's UV box
                for (int g = 0; g < m.Faces.Count; g++)
                {
                    if (tex.FaceTile[g] != t) continue;
                    var q = m.Faces[g];
                    u0 = Math.Min(u0, Math.Min(q.U0, Math.Min(q.U1, q.U2))); u1 = Math.Max(u1, Math.Max(q.U0, Math.Max(q.U1, q.U2)));
                    v0 = Math.Min(v0, Math.Min(q.V0, Math.Min(q.V1, q.V2))); v1 = Math.Max(v1, Math.Max(q.V0, Math.Max(q.V1, q.V2)));
                }
                var sh = sheets[0].Sheet;
                foreach (var cand in sheets) { sh = cand.Sheet; break; }
                int px = (f.TPage & 0x0F) * 64, py = ((f.TPage >> 4) & 1) * 256;
                int held = 0, total = 0;
                foreach (var cand in sheets)
                {
                    held = 0; total = 0;
                    for (int v = v0; v <= v1; v++)
                        for (int u = u0; u <= u1; u++)
                        { total++; if (cand.Sheet.Contains(px + (u >> 2), py + v)) held++; }
                    if (held > 0) break;
                }
                seen[t] = (held, total, u1 - u0 + 1, v1 - v0 + 1, f.TPage, f.Clut);
            }
            // ⭐ AMBIGUITY, NOT ABSENCE. Coverage says the texels exist; it cannot say they are the RIGHT
            // texels. Sheets for different worlds are uploaded to the same VRAM and reuse palettes, so a
            // face can sit inside a valid sprite in more than one sheet. If those sheets disagree at that
            // page and palette, picking either gives real texels from the wrong world.
            {
                int amb = 0, differ = 0, tot = 0;
                var byTile = new SortedDictionary<int, int>();
                for (int fi = 0; fi < m.Faces.Count; fi++)
                {
                    var f2 = m.Faces[fi];
                    var hits = new List<int>();
                    for (int si = 0; si < sheets.Count; si++)
                    {
                        int px2 = (f2.TPage & 0x0F) * 64, py2 = ((f2.TPage >> 4) & 1) * 256;
                        bool all = true;
                        foreach (var (uu, vv) in new[] { (f2.U0, f2.V0), (f2.U1, f2.V1), (f2.U2, f2.V2) })
                            if (!sheets[si].Sheet.Contains(px2 + (uu >> 2), py2 + vv)) { all = false; break; }
                        if (all) hits.Add(si);
                    }
                    tot++;
                    if (hits.Count < 2) continue;
                    amb++;
                    // do the candidate sheets actually DISAGREE on those texels?
                    var a0 = new byte[256 * 256 * 4]; var a1 = new byte[256 * 256 * 4];
                    sheets[hits[0]].Sheet.RenderPage(f2.TPage, f2.Clut, a0, 256, 0, 0);
                    sheets[hits[1]].Sheet.RenderPage(f2.TPage, f2.Clut, a1, 256, 0, 0);
                    bool same = true;
                    for (int q2 = 0; q2 < a0.Length && same; q2++) if (a0[q2] != a1[q2]) same = false;
                    if (!same)
                    {
                        differ++;
                        byTile.TryGetValue(tex.FaceTile[fi], out int nn);
                        byTile[tex.FaceTile[fi]] = nn + 1;
                        if (differ <= 3)
                            Console.WriteLine($"      face {fi} tpage {f2.TPage:x4} clut {f2.Clut:x4}: sheets {hits[0]} and {hits[1]} differ; port picked tile {tex.FaceTile[fi]}");
                    }
                }
                Console.WriteLine($"   faces: {tot}, matching 2+ sheets: {amb}, and those sheets DISAGREE on the pixels: {differ}");
                Console.WriteLine($"   disagreeing faces by tile: {string.Join(", ", byTile.Select(kv => $"tile {kv.Key}:{kv.Value}"))}");
            }
            Console.WriteLine($"entry #{entry}: {seen.Count} tiles");
            foreach (var kv in seen)
            {
                var (held, total, uw, vh, tp, cl) = kv.Value;
                Console.WriteLine($"   tile {kv.Key,2}  tpage {tp:x4} clut {cl:x4}  UV box {uw}x{vh}  sheet holds {held}/{total} ({(total == 0 ? 0 : 100.0 * held / total):0.0}%)");
            }
            return 0;
        }
        return 1;
    }

    static int GrepStr(GazArchive g, string needle)
    {
        StringTable ids = null, en = null;
        if (StringTable.IdEntry < g.Entries.Count)
            StringTable.TryParse(g.Read(g.Entries[StringTable.IdEntry]), StringTable.IdEntry, out ids, out _);
        int ee = StringTable.EntryByLanguage[0];
        if (ee < g.Entries.Count) StringTable.TryParse(g.Read(g.Entries[ee]), ee, out en, out _);
        if (ids == null || en == null) { Console.WriteLine("tables missing"); return 1; }
        int n = Math.Min(ids.Strings.Length, en.Strings.Length), hits = 0;
        for (int i = 0; i < n; i++)
        {
            string a = ids[i] ?? "", b = en[i] ?? "";
            if (a.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0 &&
                b.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
            Console.WriteLine($"{i,5}  {a,-44}  {b}");
            hits++;
        }
        Console.WriteLine($"-- {hits} hit(s) for \"{needle}\"");
        return 0;
    }

    /// <summary>Faces of one sub-mesh using one CLUT, with their vertex indices and UVs -- for asking
    /// whether a suspicious group is a FAN (all sharing a vertex) and what it samples.</summary>
    /// <summary>How many texels in a rectangle of a sheet are palette index 0 -- transparent on this
    /// GPU. Answers "is this area see-through in the DATA" without involving any renderer.</summary>
    static int TexelScan(GazArchive g, int sheetEntry, ushort tpage, ushort clut, int u0, int v0, int u1, int v1)
    {
        if (sheetEntry >= g.Entries.Count) { Console.WriteLine("no such sheet entry"); return 1; }
        if (!TextureSheet.TryParse(g.Read(g.Entries[sheetEntry]), out var sh, out string err))
        { Console.WriteLine("sheet: " + err); return 1; }
        int total = 0, clear = 0, oob = 0;
        var hist = new SortedDictionary<int,int>();
        for (int v = v0; v <= v1; v++)
            for (int u = u0; u <= u1; u++)
            {
                total++;
                int c = sh.Texel(tpage, clut, u, v);
                if (c < 0) { oob++; continue; }
                if (c == 0) clear++;
                hist.TryGetValue(c, out int n); hist[c] = n + 1;
            }
        Console.WriteLine($"sheet #{sheetEntry} tpage 0x{tpage:x4} clut 0x{clut:x4}  u {u0}..{u1}  v {v0}..{v1}");
        Console.WriteLine($"  {total} texels: {clear} transparent ({100.0 * clear / Math.Max(1,total):F1}%), " +
                          $"{oob} out of the sheet, {hist.Count} distinct colours");
        return 0;
    }

    /// <summary>How far vertices move between consecutive animation units, across one cycle. A loop
    /// that is smooth in the DATA has a last-to-first step like any other; a big one there means the
    /// clip does not loop, or the cycle length is wrong.</summary>
    /// <summary>Every animated sub-mesh on the disc: does its first key start at 0, and how big is the
    /// step from its last pose back to its first compared with a typical step? Answers whether the
    /// language advisor's loop jump is one clip's quirk or a gap in the evaluator.</summary>
    static int LoopSurvey(GazArchive g)
    {
        int animated = 0, lateStart = 0, jumpy = 0, flat = 0, jumpyCycleDiffers = 0;
        int atCycleMeasured = 0, atCycleJumpy = 0, cleanHdrAgrees = 0, quietButDisagrees = 0, jumpyFewVerts = 0;
        int jumpyTiny = 0, jumpyVisible = 0, jumpyNotRendered = 0;
        var worst = new List<(double Ratio, int Entry, int Sub, int Start, int Len)>();
        foreach (var e in g.Entries)
        {
            byte[] bytes;
            try { bytes = g.Read(e); } catch { continue; }
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) continue;
            for (int sub = 0; sub < c.Subs.Count; sub++)
            {
                if (!c.TryParseMesh(bytes, sub, out var m, out _)) continue;
                int len = MeshPose.AnimationLength(m);
                if (len <= 1 || m.Tracks == null) continue;
                animated++;
                int start = int.MaxValue;
                foreach (var tr in m.Tracks)
                {
                    if (tr.Keys != null && tr.Keys.Length > 0) start = Math.Min(start, tr.Keys[0].Time);
                    if (tr.Positions != null && tr.Positions.Length > 0 && tr.IsTimed) start = Math.Min(start, tr.Positions[0].Time);
                }
                if (start == int.MaxValue) start = 0;
                if (start > 0) lateStart++;
                double sum = 0, last = 0; int n = 0;
                for (int t = 0; t < len; t++)
                {
                    var a = MeshPose.Evaluate(m, t).Vertices;
                    var b = MeshPose.Evaluate(m, (t + 1) % len).Vertices;
                    if (a == null || b == null || a.Length != b.Length || a.Length == 0) break;
                    double d = 0;
                    for (int i = 0; i < a.Length; i++)
                        d = Math.Max(d, Math.Abs(a[i].X - b[i].X) + Math.Abs(a[i].Y - b[i].Y) + Math.Abs(a[i].Z - b[i].Z));
                    if (t == len - 1) last = d; else { sum += d; n++; }
                }
                double mean = n > 0 ? sum / n : 0;
                if (mean <= 0.001) { flat++; continue; }
                double ratio = last / mean;

                // The same measurement wrapped where the GAME wraps, reported alongside rather than
                // instead of: if a clip is smooth at its own cycle, the step above was never played.
                int cyc = m.HeaderWord0 + 1;
                if (cyc > 1)
                {
                    double csum = 0, clast = 0; int cn = 0; bool okc = true;
                    for (int t = 0; t < cyc; t++)
                    {
                        var a = MeshPose.Evaluate(m, t).Vertices;
                        var b = MeshPose.Evaluate(m, (t + 1) % cyc).Vertices;
                        if (a == null || b == null || a.Length != b.Length || a.Length == 0) { okc = false; break; }
                        double dd = 0;
                        for (int i = 0; i < a.Length; i++)
                            dd = Math.Max(dd, Math.Abs(a[i].X - b[i].X) + Math.Abs(a[i].Y - b[i].Y) + Math.Abs(a[i].Z - b[i].Z));
                        if (t == cyc - 1) clast = dd; else { csum += dd; cn++; }
                    }
                    double cmean = cn > 0 ? csum / cn : 0;
                    if (okc && cmean > 0.001) { atCycleMeasured++; if (clast / cmean > 4) atCycleJumpy++; }
                }
                // ⚠ Compare like with like, and this took three goes to get right.
                // --animheaders prints AnimationLength under the label "keys end", and on a healthy
                // mesh that equals the header word exactly (entry 83: 41/41, 32/32, 127/127). The
                // cycle is then header+1, i.e. the last key holds for one unit before wrapping.
                // Two earlier versions of this counter compared header+1 against AnimationLength, and
                // then header against the last key's TIME -- both off by one quantity, both reported
                // near-total "disagreement" that sounded decisive and separated nothing.
                bool hdrDisagrees = m.HeaderWord0 != len;
                if (!hdrDisagrees) cleanHdrAgrees += ratio > 4 ? 0 : 1;
                if (hdrDisagrees && ratio <= 4) quietButDisagrees++;
                if (ratio > 4)
                {
                    jumpy++; worst.Add((ratio, e.Index, sub, start, len));
                    // ⚠ WHERE we wrap is an assumption, and it is not always the game's. This survey
                    // wraps at AnimationLength (derived from the KEYS); the game's cycle is the mesh
                    // header's +0x00 plus one. Entry 75 sub 7 has header 2 against keys ending at 200,
                    // so the step measured there is a wrap the console never performs.
                    //
                        // ⚠ TWO EXPLANATIONS TESTED, BOTH REFUTED, 2026-09-19.
                    //
                    // 1. THE WRAP POINT. This survey wraps at AnimationLength; the console wraps at the
                    //    header word + 1. Entry 75 sub 7 has header 2 against a length of 200, so the
                    //    step measured there is a wrap the console never performs. Re-measuring every
                    //    clip at its OWN cycle leaves 56 of 239 snapping -- essentially unchanged.
                    //
                    // 2. THE HEADER DISAGREEING WITH THE KEYS. It looked like the marker of a broken
                    //    clip because 83 (agrees, smooth) and 75/7 (disagrees wildly, snaps) sit at
                    //    opposite extremes. Counted across the disc it separates nothing: 52 of the 58
                    //    snapping clips have a header that agrees EXACTLY, as do 177 of the 181 smooth
                    //    ones. Only ~10 clips disagree at all, split across both groups.
                    //
                    // So the cause of the 58 is still unknown, and these two are spent. Do not re-run
                    // either expecting the number to fall.
                    //
                    // 3. LOOKED AT SIX OF THEM ON A GPU BOX (cow tools captured, 2026-09-19). The
                    //    flagged set is not one phenomenon. Entry 75 sub 7 is a rigid lava slab that
                    //    sweeps at the camera and is small and far again one frame later -- its two
                    //    ends are nowhere near each other, so it is a ONE-SHOT, not a loop that fails
                    //    to close, and this metric has no business scoring it. 397 sub 4 is the same
                    //    shape of thing, a rocket translating and resetting, and shows no visible jump
                    //    at all. So "ratio > 4" is measuring at least two different things, only some
                    //    of which are faults. Separating one-way clips from looping ones is the next
                    //    move, not hunting a single cause.
                    //
                    // ⚠ And mind the capture window: 389 sub 0 has a 601-unit cycle, which at the
                    //    model browser's 30 units/s is 20.0s against a 16.7s capture. It never reaches
                    //    its own wrap, so a "no jump detected" reading on that clip is a null from a
                    //    window that excludes the event.
                    if (hdrDisagrees) jumpyCycleDiffers++;

                    // Would the model browser even RENDER this animation? Its Playability gate treats a
                    // mesh as playable only if a track writes vertices directly, drives a bound scatter
                    // source, or poses a bone that actually SKINS vertices. A mesh whose motion is all
                    // bone keys with no skinned bone renders as a static rest pose there, while
                    // MeshPose.Evaluate (what this survey calls) poses it regardless. Same data, two
                    // consumers, different answers -- which is how a capture can show a still model
                    // while this survey insists its vertices move.
                    bool skinned = false;
                    if (m.Skeleton != null)
                        foreach (var bn in m.Skeleton.Bones) if (bn.SkinCount > 0) { skinned = true; break; }
                    bool writesVerts = false, boneKeys = false;
                    foreach (var t3 in m.Tracks)
                    {
                        if (t3.Positions.Length > 0 &&
                            (t3.Target == TrackTarget.Vertex ||
                             (t3.Target == TrackTarget.ScatterSource && m.Binding != null && m.Binding.SourceCount > 0)))
                            writesVerts = true;
                        if (t3.Keys.Length > 0) boneKeys = true;
                    }
                    if (!writesVerts && boneKeys && !skinned) jumpyNotRendered++;

                    // HOW MANY VERTICES actually jump at the wrap? `d` above is a MAX over vertices,
                    // so one stray vertex scores the whole clip as snapping while the visible body is
                    // continuous. Count the vertices within 25% of the worst one: a handful means a
                    // stray, most of the mesh means a real discontinuity.
                    var pa = MeshPose.Evaluate(m, len - 1).Vertices;
                    var pb = MeshPose.Evaluate(m, 0).Vertices;
                    if (pa != null && pb != null && pa.Length == pb.Length && pa.Length > 0)
                    {
                        double vWorst = 0;
                        for (int i = 0; i < pa.Length; i++)
                            vWorst = Math.Max(vWorst, Math.Abs(pa[i].X - pb[i].X) + Math.Abs(pa[i].Y - pb[i].Y) + Math.Abs(pa[i].Z - pb[i].Z));
                        int movers = 0;
                        for (int i = 0; i < pa.Length; i++)
                            if (Math.Abs(pa[i].X - pb[i].X) + Math.Abs(pa[i].Y - pb[i].Y) + Math.Abs(pa[i].Z - pb[i].Z) > vWorst * 0.25) movers++;
                        if (movers * 20 <= pa.Length) jumpyFewVerts++;   // 5% or fewer of the mesh

                        // ⚠ THE RATIO HAS NO ABSOLUTE FLOOR. A long slow clip moves very little per
                        // unit, so a physically tiny discontinuity divides into a huge ratio. Measure
                        // the jump against the mesh's OWN size: anything under a percent or so of the
                        // bounding box cannot be seen and is not worth calling a defect.
                        double lo = double.MaxValue, hi = double.MinValue;
                        for (int i = 0; i < pa.Length; i++)
                        {
                            lo = Math.Min(lo, Math.Min(pa[i].X, Math.Min(pa[i].Y, pa[i].Z)));
                            hi = Math.Max(hi, Math.Max(pa[i].X, Math.Max(pa[i].Y, pa[i].Z)));
                        }
                        double span = hi - lo;
                        if (span > 0)
                        {
                            double frac = vWorst / span;
                            if (frac < 0.01) jumpyTiny++; else jumpyVisible++;
                        }
                    }
                }
            }
        }
        Console.WriteLine($"{animated} animated sub-meshes");
        Console.WriteLine($"  {lateStart} whose first key is NOT at time 0");
        Console.WriteLine($"  {jumpy} whose last-to-first step is more than 4x a typical step");
        Console.WriteLine($"    of those, {jumpyCycleDiffers} whose header word does NOT equal the animation length");
        Console.WriteLine($"    of those, {jumpyFewVerts} where 5% or fewer of the vertices jump (a stray, not the body)");
        Console.WriteLine($"    of those, {jumpyTiny} whose jump is under 1% of the mesh's own size (invisible), {jumpyVisible} bigger");
        Console.WriteLine($"    of those, {jumpyNotRendered} the model browser would NOT animate at all (bone keys, no skinned bone)");
        Console.WriteLine($"  {flat} that never move (skipped)");
        Console.WriteLine($"  wrapped at the GAME's cycle instead: {atCycleJumpy} of {atCycleMeasured} still snap");
        Console.WriteLine($"  separation test -- smooth AND header agrees with keys: {cleanHdrAgrees};  smooth BUT header disagrees: {quietButDisagrees}");
        worst.Sort((a, b) => b.Ratio.CompareTo(a.Ratio));
        Console.WriteLine("worst offenders:");
        foreach (var w in worst.Take(12))
            Console.WriteLine($"  entry {w.Entry,4} sub {w.Sub,3}: loop step {w.Ratio:F1}x typical, keys start at {w.Start}, length {w.Len}");
        return 0;
    }

    static int PoseDelta(GazArchive g, int entry, int sub)
    {
        foreach (var e in g.Entries)
        {
            if (e.Index != entry) continue;
            var bytes = g.Read(e);
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) return 1;
            if (!c.TryParseMesh(bytes, sub, out var m, out _)) return 1;
            int len = MeshPose.AnimationLength(m);
            if (len <= 0) { Console.WriteLine("no animation"); return 1; }
            var deltas = new List<(int At, double D)>();
            for (int cycle = len; cycle <= len + 1; cycle++)
            {
                double worst = 0; int worstAt = -1; double sum = 0;
                for (int t = 0; t < cycle; t++)
                {
                    var a = MeshPose.Evaluate(m, t).Vertices;
                    var b = MeshPose.Evaluate(m, (t + 1) % cycle).Vertices;
                    if (a == null || b == null || a.Length != b.Length) continue;
                    double d = 0;
                    for (int i = 0; i < a.Length; i++)
                        d = Math.Max(d, Math.Abs(a[i].X - b[i].X) + Math.Abs(a[i].Y - b[i].Y) + Math.Abs(a[i].Z - b[i].Z));
                    sum += d;
                    if (d > worst) { worst = d; worstAt = t; }
                    if (t == cycle - 1) deltas.Add((cycle, d));
                }
                Console.WriteLine($"cycle {cycle}: mean step {sum / cycle:F1}, worst {worst:F0} at unit {worstAt} -> {(worstAt + 1) % cycle}");
            }
            foreach (var (at, d) in deltas)
                Console.WriteLine($"  wrapping at {at}: the last-to-first step is {d:F0}");
            Console.WriteLine("  track key times (first track with timed keys):");
            foreach (var tr in m.Tracks)
            {
                if (!tr.IsTimed || tr.Keys == null || tr.Keys.Length == 0) continue;
                var ks = tr.Keys;
                Console.WriteLine($"    {ks.Length} keys; first {{t={ks[0].Time},d={ks[0].Duration}}} " +
                                  $"last {{t={ks[^1].Time},d={ks[^1].Duration}}} -> ends {ks[^1].Time + ks[^1].Duration}");
                break;
            }
            Console.WriteLine("  tail profile (step from each unit to the next):");
            for (int t = len - 8; t <= len; t++)
            {
                var a = MeshPose.Evaluate(m, t).Vertices;
                var b = MeshPose.Evaluate(m, t + 1).Vertices;
                double d2 = 0;
                for (int i = 0; i < a.Length; i++)
                    d2 = Math.Max(d2, Math.Abs(a[i].X - b[i].X) + Math.Abs(a[i].Y - b[i].Y) + Math.Abs(a[i].Z - b[i].Z));
                Console.WriteLine($"    {t,3} -> {t + 1,3}: {d2:F0}");
            }
            return 0;
        }
        return 1;
    }

    /// <summary>Header +0x00 and the keys' end time for every sub-mesh of one entry, so a measured
    /// on-screen period can be tested against both readings of the cycle.</summary>
    /// <summary>Does a track's LAST key duplicate its FIRST? The game's handler reads key[count] --
    /// one past the last -- so the array may carry a loop-closing sentinel this parser stops before.</summary>
    /// <summary>Across the whole disc: what TIME does the record at track+8 carry -- the one the
    /// parser skips? If a keyed track's animation really begins there, it should be 0.</summary>
    /// <summary>Sub-meshes whose faces use a given set of palettes, with their cycle field -- for
    /// identifying WHICH model a screen draws from the palettes its display list shows.</summary>
    static int FindByClut(GazArchive g, string clutList)
    {
        var want = new HashSet<ushort>();
        foreach (var t in clutList.Split(',')) want.Add(Convert.ToUInt16(t.Trim(), 16));
        const double UnitsPerFrame = 33868800.0 / 8 / 2150 * 128 / 4096 / 50;
        foreach (var e in g.Entries)
        {
            byte[] bytes;
            try { bytes = g.Read(e); } catch { continue; }
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) continue;
            for (int sub = 0; sub < c.Subs.Count; sub++)
            {
                if (!c.TryParseMesh(bytes, sub, out var m, out _)) continue;
                var have = new HashSet<ushort>();
                foreach (var f in m.Faces) have.Add(f.Clut);
                if (!want.IsSubsetOf(have)) continue;
                int cyc = m.HeaderWord0 + 1;
                Console.WriteLine($"  entry {e.Index,4} sub {sub,3}: {m.VertexCount,4} verts {m.Faces.Count,4} faces  " +
                                  $"+0x00={m.HeaderWord0,4}  cycle {cyc,4} -> {cyc / UnitsPerFrame,6:F1} frames");
            }
        }
        return 0;
    }

    static int FirstRecord(GazArchive g)
    {
        var times = new SortedDictionary<int,int>();
        int tracks = 0, matchesLast = 0, hasLast = 0;
        foreach (var e in g.Entries)
        {
            byte[] bytes;
            try { bytes = g.Read(e); } catch { continue; }
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) continue;
            for (int sub = 0; sub < c.Subs.Count; sub++)
            {
                if (!c.TryParseMesh(bytes, sub, out var m, out _) || m.Tracks == null) continue;
                if (!c.TryExpand(bytes, sub, out var exp, out _, out _)) continue;
                foreach (var tr in m.Tracks)
                {
                    if (tr.Type != 0 && tr.Type != 6) continue;          // the TIMED keyed types
                    if (tr.Keys == null || tr.Keys.Length == 0) continue;
                    int off = tr.FileOffset + 8;
                    if (off + 20 > exp.Length) continue;
                    tracks++;
                    int t = BitConverter.ToInt16(exp, off);
                    times.TryGetValue(t, out int n); times[t] = n + 1;
                    var first = AnimKey.FromBlock(exp.AsSpan(off + 4, 16), 0, 1);
                    var last = tr.Keys[tr.Keys.Length - 1];
                    hasLast++;
                    if (Math.Abs(first.Qx - last.Qx) < 1e-4 && Math.Abs(first.Qy - last.Qy) < 1e-4 &&
                        Math.Abs(first.Qz - last.Qz) < 1e-4 && Math.Abs(first.Qw - last.Qw) < 1e-4)
                        matchesLast++;
                }
            }
        }
        Console.WriteLine($"{tracks} timed keyed tracks examined");
        Console.WriteLine("time carried by the record at track+8:");
        foreach (var kv in times.Take(6)) Console.WriteLine($"   t = {kv.Key}: {kv.Value} tracks");
        if (times.Count > 6) Console.WriteLine($"   ... {times.Count} distinct times in all");
        Console.WriteLine($"{matchesLast} of {hasLast} carry the SAME rotation as the track's last key");
        return 0;
    }

    static int KeyEnds(GazArchive g, int entry, int sub)
    {
        foreach (var e in g.Entries)
        {
            if (e.Index != entry) continue;
            var bytes = g.Read(e);
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) return 1;
            if (!c.TryParseMesh(bytes, sub, out var m, out _)) return 1;
            int shown = 0, dup = 0, timed = 0;
            foreach (var tr in m.Tracks)
            {
                if (tr.Keys == null || tr.Keys.Length < 2) continue;
                timed++;
                var a = tr.Keys[0];
                var z = tr.Keys[tr.Keys.Length - 1];
                bool same = a.Qx == z.Qx && a.Qy == z.Qy && a.Qz == z.Qz && a.Qw == z.Qw &&
                            a.Tx == z.Tx && a.Ty == z.Ty && a.Tz == z.Tz;
                if (same) dup++;
                if (shown++ < 4)
                    Console.WriteLine($"  track type {tr.Type} keys {tr.Keys.Length}: " +
                                      $"first t={a.Time} q=({a.Qx},{a.Qy},{a.Qz},{a.Qw})  " +
                                      $"last t={z.Time} q=({z.Qx},{z.Qy},{z.Qz},{z.Qw})  {(same ? "SAME POSE" : "different")}");
            }
            Console.WriteLine($"{dup} of {timed} tracks end on a duplicate of their first key");
            return 0;
        }
        return 1;
    }

    static int AnimHeaders(GazArchive g, int entry)
    {
        foreach (var e in g.Entries)
        {
            if (e.Index != entry) continue;
            var bytes = g.Read(e);
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) return 1;
            const double UnitsPerFrame = 33868800.0 / 8 / 2150 * 128 / 4096 / 50;
            for (int sub = 0; sub < c.Subs.Count; sub++)
            {
                if (!c.TryParseMesh(bytes, sub, out var m, out _)) continue;
                int len = MeshPose.AnimationLength(m);
                if (len <= 1) continue;
                int n0 = m.HeaderWord0;
                Console.WriteLine($"  sub {sub,2}: +0x00 = {n0,4}   keys end {len,4}   " +
                                  $"period if cycle=+0x00: {n0 / UnitsPerFrame,6:F1} frames   " +
                                  $"if cycle=+0x00+1: {(n0 + 1) / UnitsPerFrame,6:F1}");
            }
            return 0;
        }
        return 1;
    }


    /// <summary>--scaleblocks E: for every type-0 and type-1 track in entry E, the second block's
    /// diagonal (vecB) and axis quaternion (quatB).
    ///
    /// Exists to answer one question before porting the scale: does it actually DO anything on these
    /// rigs? The evaluator builds a diagonal from vecB and conjugates it by quatB, so an identity
    /// diagonal with an identity quaternion is a no-op and the composition ORDER cannot matter.
    /// 4096 is 1.0 in the GTE's 1.12 fixed point.</summary>
    static int ScaleBlocks(GazArchive gaz, int entry)
    {
        foreach (var e in gaz.Entries)
        {
            if (e.Index != entry) continue;
            var bytes = gaz.Read(e);
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _))
            { Console.WriteLine($"entry #{entry} is not a mesh container"); return 1; }

            int identity = 0, scaled = 0;
            for (int sub = 0; sub < c.SubCount; sub++)
            {
                if (!c.TryParseMesh(bytes, sub, out var m, out _) || m.Tracks == null) continue;
                foreach (var t in m.Tracks)
                {
                    if (t.Type != 0 && t.Type != 1) continue;
                    if (t.Scales == null || t.Scales.Length == 0) continue;
                    for (int k = 0; k < t.Scales.Length; k++)
                    {
                        var s = t.Scales[k];
                        bool unit = s.Sx == 4096 && s.Sy == 4096 && s.Sz == 4096;
                        bool noRot = Math.Abs(s.Qx) < 1e-4 && Math.Abs(s.Qy) < 1e-4
                                  && Math.Abs(s.Qz) < 1e-4 && Math.Abs(Math.Abs(s.Qw) - 1f) < 1e-3;
                        if (unit && noRot) { identity++; continue; }
                        scaled++;
                        if (scaled <= 12)
                            Console.WriteLine($"  sub {sub,2} type {t.Type} bone {t.BoneIndex,2} key {k,3}: " +
                                              $"diag ({s.Sx,6},{s.Sy,6},{s.Sz,6})  " +
                                              $"quatB ({s.Qx,6:F3},{s.Qy,6:F3},{s.Qz,6:F3},{s.Qw,6:F3})  slot3 {s.Slot3}");
                    }
                }
            }
            Console.WriteLine($"entry #{entry}: {identity} blocks are a no-op (unit diagonal, identity quat), {scaled} are not");
            return 0;
        }
        Console.WriteLine($"no entry #{entry}");
        return 1;
    }

    static int FaceGroup(GazArchive g, int entry, int sub, ushort clut)
    {
        foreach (var e in g.Entries)
        {
            if (e.Index != entry) continue;
            var bytes = g.Read(e);
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) return 1;
            if (!c.TryParseMesh(bytes, sub, out var m, out _)) return 1;
            var shared = new Dictionary<int,int>();
            int n = 0;
            foreach (var f in m.Faces)
            {
                if (f.Clut != clut) continue;
                n++;
                Console.WriteLine($"  v({f.I0},{f.I1},{f.I2})  uv({f.U0},{f.V0}) ({f.U1},{f.V1}) ({f.U2},{f.V2})  " +
                                  $"tpage=0x{f.TPage:x4} kind=0x{f.Kind:x2} {(f.DoubleSided ? "double" : "single")}-sided");
                foreach (int vi in new[]{ (int)f.I0, f.I1, f.I2 })
                { shared.TryGetValue(vi, out int k); shared[vi] = k + 1; }
            }
            Console.WriteLine($"{n} faces on clut 0x{clut:x4}");
            foreach (var kv in shared.OrderByDescending(k => k.Value).Take(3))
                Console.WriteLine($"  vertex {kv.Key} appears in {kv.Value} of them" + (kv.Value == n ? "  <- a FAN around it" : ""));
            return 0;
        }
        return 1;
    }

    static int FaceClut(GazArchive g, int entry, int sub)
    {
        foreach (var e in g.Entries)
        {
            if (e.Index != entry) continue;
            var bytes = g.Read(e);
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) return 1;
            if (!c.TryParseMesh(bytes, sub, out var m2, out string err)) { Console.WriteLine(err); return 1; }
            Console.WriteLine($"entry {entry} sub {sub}: {m2.VertexCount} verts, {m2.Faces.Count} faces, {m2.BoneCount} bones, "
                            + $"{m2.TrackCount} tracks, animation length {MeshPose.AnimationLength(m2)}");
            var byClut = new SortedDictionary<ushort, int>();
            var uByClut = new Dictionary<ushort, (int u0, int u1, int v0, int v1)>();
            foreach (var f in m2.Faces)
            {
                byClut.TryGetValue(f.Clut, out int n); byClut[f.Clut] = n + 1;
                int lu = Math.Min(f.U0, Math.Min(f.U1, f.U2)), hu = Math.Max(f.U0, Math.Max(f.U1, f.U2));
                int lv = Math.Min(f.V0, Math.Min(f.V1, f.V2)), hv = Math.Max(f.V0, Math.Max(f.V1, f.V2));
                if (uByClut.TryGetValue(f.Clut, out var b))
                    uByClut[f.Clut] = (Math.Min(b.u0, lu), Math.Max(b.u1, hu), Math.Min(b.v0, lv), Math.Max(b.v1, hv));
                else uByClut[f.Clut] = (lu, hu, lv, hv);
            }
            foreach (var kv in byClut)
            {
                var b = uByClut[kv.Key];
                Console.WriteLine($"   clut 0x{kv.Key:x4}  {kv.Value,4} faces   u {b.u0,3}..{b.u1,3}  v {b.v0,3}..{b.v1,3}");
            }
            return 0;
        }
        Console.WriteLine("no such entry");
        return 1;
    }

    /// <summary>⚠ A no-match here means "no mesh with these totals", NOT "not on the disc" -- see the
    /// warning at the call site. Reach for --faceclut before concluding anything is generated.</summary>
    static int FindMesh(GazArchive g, int verts, int faces)
    {
        int hits = 0, scanned = 0;
        foreach (var e in g.Entries)
        {
            byte[] bytes;
            try { bytes = g.Read(e); } catch { continue; }
            if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) continue;
            for (int sub = 0; sub < c.Subs.Count; sub++)
            {
                if (!c.TryParseMesh(bytes, sub, out var m2, out _)) continue;
                scanned++;
                bool vOk = verts < 0 || m2.VertexCount == verts;
                bool fOk = faces < 0 || m2.Faces.Count == faces;
                if (!vOk || !fOk) continue;
                Console.WriteLine($"entry {e.Index,4} sub {sub,3}: {m2.VertexCount} verts, {m2.Faces.Count} faces");
                hits++;
            }
        }
        Console.WriteLine($"-- {hits} match(es) out of {scanned} sub-meshes");
        return 0;
    }

    static int Buildable(DiscReader disc, GazArchive g, int onlyMap)
    {
        var exeFile = disc.Find(AssetSelfTest.GameExecutable);
        if (exeFile == null) { Console.WriteLine("no game executable on the disc"); return 1; }
        var exe = disc.ReadFile(exeFile);
        const string marks = "#^XS=P.";   // buildable, too steep, ruled out, scenery, entrance, path, edge
        foreach (var (entry, raw) in ParkMap.FindAll(g))
        {
            if (onlyMap >= 0 && entry.Index != onlyMap) continue;
            ParkWorld world = null;
            foreach (var w in ParkWorlds.All) if (System.Array.IndexOf(w.Maps, entry.Index) >= 0) world = w;
            var map = world != null ? ParkPaths.LayStartingPaths(raw, exe, AssetSelfTest.GameExecutableBase, world.Index) : raw;
            var n = new int[7];
            var grid = new System.Text.StringBuilder();
            for (int z = 0; z < map.Height; z++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var v = ParkBuild.Classify(map, x, z);
                    n[(int)v]++;
                    grid.Append(marks[(int)v]);
                }
                grid.Append('\n');
            }
            Console.WriteLine($"map {entry.Index,4} ({world?.Name ?? "?"}): {n[0]} buildable, {n[1]} too steep, {n[2]} ruled out by the map, " +
                              $"{n[3]} under scenery, {n[4]} entrance, {n[5]} path, {n[6]} edge");
            if (onlyMap >= 0) Console.Write("# buildable  ^ too steep  X ruled out  S scenery  = entrance  P path  . edge\n" + grid);
        }
        return 0;
    }

    static int MapHeights(GazArchive g)
    {
        foreach (var (entry, map) in ParkMap.FindAll(g))
        {
            int lo = 255, hi = 0;
            var seen = new SortedSet<int>();
            foreach (var t in map.Tiles) { int h = t.Height; if (h < lo) lo = h; if (h > hi) hi = h; seen.Add(h); }
            Console.WriteLine($"map {entry.Index,4}  {map.Width}x{map.Height}  raw height {lo}..{hi}  " +
                              $"({seen.Count} distinct)  world units {lo * 4}..{hi * 4}");
        }
        return 0;
    }

    static int WorldMapDump(GazArchive g)
    {
        int e = StringTable.EntryByLanguage[0];
        if (e >= g.Entries.Count) { Console.WriteLine("English table missing"); return 1; }
        if (!StringTable.TryParse(g.Read(g.Entries[e]), e, out var en, out string err))
        { Console.WriteLine("English table: " + err); return 1; }
        foreach (var i in WorldMap.Islands)
        {
            string name = WorldMap.NameOf(i, en);
            Console.WriteLine($"world {i.World} ({ParkWorlds.All[i.World].Name,-9}) park {i.Park}  " +
                              $"id {i.NameId,4}  maps {string.Join(",", ParkWorlds.All[i.World].Maps)}  {name}");
        }
        Console.WriteLine($"at most {WorldMap.MaxOpenParks} parks open at once (string 318)");
        return 0;
    }

    static int LangNames(GazArchive g)
    {
        string[] want = { "English", "Fran", "Deutsch", "Italiano", "Espa", "Nederlands", "Svenska" };
        int parsed = 0;
        for (int l = 0; l < StringTable.EntryByLanguage.Length; l++)
        {
            int e = StringTable.EntryByLanguage[l];
            if (e >= g.Entries.Count) { Console.WriteLine($"lang {l}: entry {e} missing"); continue; }
            if (!StringTable.TryParse(g.Read(g.Entries[e]), e, out var t, out string err))
            { Console.WriteLine($"lang {l} entry {e}: {err}"); continue; }
            parsed++;
            var found = new List<string>();
            for (int id = 0; id < t.Strings.Length; id++)
            {
                string v = t[id];
                if (v == null || v.Length > 16) continue;
                foreach (var w in want)
                    if (v.StartsWith(w, StringComparison.Ordinal)) { found.Add($"{id}:{v}"); break; }
            }
            Console.WriteLine($"lang {l} ({StringTable.LanguageNames[l]}) entry {e}: {t.Strings.Length} strings, " +
                              (found.Count == 0 ? "NO native-language names" : string.Join(", ", found.Take(12))));
        }
        Console.WriteLine($"{parsed} tables parsed");
        return 0;
    }

    static int MenuRender(GazArchive g, string dir)
    {
        if (MenuLayout.Sheet >= g.Entries.Count) { Console.WriteLine("menu sheet: entry missing"); return 1; }
        if (!TextureSheet.TryParse(g.Read(g.Entries[MenuLayout.Sheet]), out var sheet, out string err))
        { Console.WriteLine("menu sheet: " + err); return 1; }
        var p = MenuRenderer.Prepare(sheet);
        System.IO.Directory.CreateDirectory(dir);
        int n = 0;
        var menu = MenuRenderer.NewFrame();
        MenuRenderer.DrawBackdrop(p, menu);
        for (int i = 0; i < MainMenu.RootItems.Length; i++)
        {
            int y = 160 + i * 30;
            if (i == 0) MenuRenderer.DrawHighlight(p, menu, y);
            int w = MenuRenderer.MeasureText(p, MainMenu.RootItems[i]);
            MenuRenderer.DrawText(p, menu, (MenuRenderer.W - w) / 2, y, MainMenu.RootItems[i]);
        }
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "menu.rgba"), menu); n++;
        for (int r = 0; r < LanguageRing.Count; r++)
        {
            var f = MenuRenderer.NewFrame();
            MenuRenderer.DrawLanguageScreen(p, f, r);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"lang{r}_{LanguageRing.NameAt(r)}.rgba"), f); n++;
        }
        Console.WriteLine($"wrote {n} frames of {MenuRenderer.W}x{MenuRenderer.H} RGBA to {dir}");
        return 0;
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
            // --menurender <dir>: the menu and all seven language screens, through the SAME
            // MenuRenderer the game runs, as raw RGBA. No engine, so a layout error is visible without
            // a window -- and because it is the product's code, a pass here is a pass on the game.
            // --langnames: the language screen shows each language in ITS OWN language. Find where those
            // names live by asking every table for its own name, through the real StringTable decoder
            // (code page 850), and report the string ID that holds it.
            // --findmesh V F: every sub-mesh in the archive with V vertices and F faces.
            //
            // ⚠ IT ANSWERS "IS THERE A MESH SHAPED LIKE THIS", NOT "IS THIS THING STORED". A whole-object
            // fingerprint cannot see an object that is PART of a bigger one, and I read its silence as
            // "the code generates it" -- wrongly. The language screen's flag is 192 faces; no sub-mesh
            // has 192 faces; the flag is nonetheless stored, as 192 of the 319 faces of entry 83 sub 10.
            // Use --faceclut to ask the other question.
            // --faceclut E S: the face CLUT histogram of one sub-mesh. Answers "is this group of faces a
            // PART of a bigger mesh", which a whole-mesh fingerprint search cannot see.
            int tsAt = Array.IndexOf(args, "--texelscan");
            if (tsAt >= 0 && tsAt + 7 < args.Length)
                return TexelScan(g, int.Parse(args[tsAt + 1]), Convert.ToUInt16(args[tsAt + 2], 16),
                                 Convert.ToUInt16(args[tsAt + 3], 16), int.Parse(args[tsAt + 4]),
                                 int.Parse(args[tsAt + 5]), int.Parse(args[tsAt + 6]), int.Parse(args[tsAt + 7]));
            if (Array.IndexOf(args, "--loopsurvey") >= 0) return LoopSurvey(g);
            int pdAt = Array.IndexOf(args, "--posedelta");
            if (pdAt >= 0 && pdAt + 2 < args.Length)
                return PoseDelta(g, int.Parse(args[pdAt + 1]), int.Parse(args[pdAt + 2]));
            int fbAt = Array.IndexOf(args, "--findbyclut");
            if (fbAt >= 0 && fbAt + 1 < args.Length) return FindByClut(g, args[fbAt + 1]);
            if (Array.IndexOf(args, "--firstrecord") >= 0) return FirstRecord(g);
            int keAt = Array.IndexOf(args, "--keyends");
            if (keAt >= 0 && keAt + 2 < args.Length) return KeyEnds(g, int.Parse(args[keAt + 1]), int.Parse(args[keAt + 2]));
            int sbAt = Array.IndexOf(args, "--scaleblocks");
            if (sbAt >= 0 && sbAt + 1 < args.Length) return ScaleBlocks(g, int.Parse(args[sbAt + 1]));
            int ahAt = Array.IndexOf(args, "--animheaders");
            if (ahAt >= 0 && ahAt + 1 < args.Length) return AnimHeaders(g, int.Parse(args[ahAt + 1]));
            int fgAt = Array.IndexOf(args, "--facegroup");
            if (fgAt >= 0 && fgAt + 3 < args.Length)
                return FaceGroup(g, int.Parse(args[fgAt + 1]), int.Parse(args[fgAt + 2]),
                                 Convert.ToUInt16(args[fgAt + 3], 16));
            int fcAt = Array.IndexOf(args, "--faceclut");
            if (fcAt >= 0 && fcAt + 2 < args.Length)
                return FaceClut(g, int.Parse(args[fcAt + 1]), int.Parse(args[fcAt + 2]));
            int fmAt = Array.IndexOf(args, "--findmesh");
            if (fmAt >= 0 && fmAt + 2 < args.Length)
                return FindMesh(g, int.Parse(args[fmAt + 1]), int.Parse(args[fmAt + 2]));
            // --worldmap: the eight parks with their names resolved through the real string table, which
            // is the end-to-end check that the island table was transcribed at the right offsets.
            // --mapheights: how much relief each park map actually has, which decides whether a slope
            // rule can be TESTED on the shipped maps at all.
            if (Array.IndexOf(args, "--mapheights") >= 0) return MapHeights(g);
            if (Array.IndexOf(args, "--worldmap") >= 0) return WorldMapDump(g);
            if (Array.IndexOf(args, "--langnames") >= 0) return LangNames(g);
            // --grepstr <text>: search the ID table and the English table together, so a hit shows both
            // the symbolic name the developers gave a string and what it actually says.
            int gsAt = Array.IndexOf(args, "--grepstr");
            if (gsAt >= 0 && gsAt + 1 < args.Length) return GrepStr(g, args[gsAt + 1]);
            int mrAt = Array.IndexOf(args, "--menurender");
            if (mrAt >= 0 && mrAt + 1 < args.Length) return MenuRender(g, args[mrAt + 1]);
            // --attractions [text]: find a model by WHAT IT IS. ⚠ Not --names, which already exists on the
            // disc route and dumps the string tables by language; two different jobs, and one name for
            // both is how someone runs the wrong one and believes the output.
            int cv = Array.IndexOf(args, "--coverage");
            if (cv >= 0 && cv + 1 < args.Length) return Coverage(g, int.Parse(args[cv + 1]));
            int nm = Array.IndexOf(args, "--attractions");
            if (nm >= 0) return Attractions(g, nm + 1 < args.Length && !args[nm + 1].StartsWith("--") ? args[nm + 1] : null);
            // --posedump <entry> <sub> <dir>
            int pd = Array.IndexOf(args, "--posedump");
            if (pd >= 0 && pd + 3 < args.Length)
                return PoseDump(g, int.Parse(args[pd + 1]), int.Parse(args[pd + 2]), args[pd + 3]);
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
            // --model-bounds E: sub-model 0 of entry E, its vertices' extent at rest and posed at time 0, and its
            // definition record -- where a model's origin sits relative to its footprint.
            // --pack E: a scenery-style model pack (the gate packs, the bus at entry 90), with each model's
            // size and the texture pages its faces name — which is what says whose sheet it is drawn from.
            int pkAt = Array.IndexOf(args, "--pack");
            if (pkAt >= 0 && pkAt + 1 < args.Length)
            {
                var agp = Archive(disc);
                int pe = int.Parse(args[pkAt + 1]);
                var pb = agp.Read(agp.Entries[pe]);
                if (!SceneryPack.TryParse(pb, out var pack, out string pErr)) { Console.WriteLine($"entry {pe}: {pErr}"); return 1; }
                Console.WriteLine($"entry {pe}: {pb.Length} bytes, {pack.Models.Count} models");
                for (int m = 0; m < pack.Models.Count; m++)
                {
                    var md = pack.Models[m];
                    int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
                    for (int v = 0; v < md.Vertices.Count; v++)
                    {
                        var (vx, vy, vz) = md.Position(v);
                        x0 = Math.Min(x0, (int)vx); x1 = Math.Max(x1, (int)vx);
                        y0 = Math.Min(y0, (int)vy); y1 = Math.Max(y1, (int)vy);
                        z0 = Math.Min(z0, (int)vz); z1 = Math.Max(z1, (int)vz);
                    }
                    var pages = new System.Collections.Generic.SortedSet<string>();
                    foreach (var t in md.Textures) pages.Add($"{t.TPage:X4}/{t.Clut:X4}");
                    Console.WriteLine($"  model {m}: scale {md.Scale}, {md.Vertices.Count} verts, {md.Polygons.Count} faces, " +
                                      $"{(x1 - x0) / 256.0:0.00} x {(y1 - y0) / 256.0:0.00} x {(z1 - z0) / 256.0:0.00} tiles, " +
                                      $"pages {string.Join(" ", pages)}");
                }
                return 0;
            }

            // --recdump E [FROM LEN]: an attraction record's raw words, for reading tables the port has not
            // named yet (the piece class table the coaster code indexes at record +0xDC, say).
            int rdAt = Array.IndexOf(args, "--recdump");
            if (rdAt >= 0 && rdAt + 1 < args.Length)
            {
                var agr = Archive(disc);
                int re = int.Parse(args[rdAt + 1]);
                var rb = agr.Read(agr.Entries[re]);
                int rec = BitConverter.ToInt32(rb, 0x14);
                int from = rdAt + 2 < args.Length ? Convert.ToInt32(args[rdAt + 2], 16) : 0;
                int len = rdAt + 3 < args.Length ? Convert.ToInt32(args[rdAt + 3], 16) : 0x180;
                Console.WriteLine($"entry {re}: {rb.Length} bytes, record at +0x{rec:X} (header +0x14)");
                Console.WriteLine("  header " + string.Join(" ", System.Linq.Enumerable.Range(0, Math.Min(32, rb.Length)).Select(k => rb[k].ToString("X2"))));
                for (int o = from; o < len && rec + o < rb.Length; o += 16)
                {
                    int n = Math.Min(16, Math.Min(len - o, rb.Length - rec - o));
                    var words = new System.Collections.Generic.List<string>();
                    for (int k = 0; k + 4 <= n; k++) if (k % 4 == 0) words.Add(BitConverter.ToUInt32(rb, rec + o + k).ToString("X8"));
                    var raw = string.Join(" ", System.Linq.Enumerable.Range(0, n).Select(k => rb[rec + o + k].ToString("X2")));
                    Console.WriteLine($"  +0x{o:X3}  {string.Join(" ", words),-35}   {raw}");
                }
                return 0;
            }

            // --subbounds E: every sub-model of entry E with its extent in tiles, which is how a station, a piece
            // of track and a car tell themselves apart without a table.
            int sbAt = Array.IndexOf(args, "--subbounds");
            if (sbAt >= 0 && sbAt + 1 < args.Length)
            {
                var ag2 = Archive(disc);
                int se2 = int.Parse(args[sbAt + 1]);
                var bytes2 = ag2.Read(ag2.Entries[se2]);
                if (!MeshContainer.TryParse(bytes2, out var c2, out string ce2)) { Console.WriteLine($"entry {se2}: {ce2}"); return 1; }
                Console.WriteLine($"entry {se2}: {c2.Subs.Count} sub-models (256 units = one tile)");
                for (int sub = 0; sub < c2.Subs.Count; sub++)
                {
                    if (!c2.TryParseMesh(bytes2, sub, out var m2, out _)) { Console.WriteLine($"  sub {sub}: unreadable"); continue; }
                    int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
                    for (int v = 0; v < m2.VertexCount; v++)
                    {
                        int vx = m2.Vertices[v * 3], vy = m2.Vertices[v * 3 + 1], vz = m2.Vertices[v * 3 + 2];
                        x0 = Math.Min(x0, vx); x1 = Math.Max(x1, vx);
                        y0 = Math.Min(y0, vy); y1 = Math.Max(y1, vy);
                        z0 = Math.Min(z0, vz); z1 = Math.Max(z1, vz);
                    }
                    if (m2.VertexCount == 0) { Console.WriteLine($"  sub {sub}: empty"); continue; }
                    Console.WriteLine($"  sub {sub,2}: {m2.VertexCount,4} verts {m2.Faces.Count,4} faces   " +
                                      $"x {x0,5}..{x1,-5} y {y0,5}..{y1,-5} z {z0,5}..{z1,-5}   " +
                                      $"{(x1 - x0) / 256.0:F2} x {(y1 - y0) / 256.0:F2} x {(z1 - z0) / 256.0:F2} tiles");
                }
                return 0;
            }
            // --rig E: every sub-model of entry E, its bones and animation length, and each bone's transform at a few
            // times -- for the park's build animation (entry 3, a rig per variant whose bone moves a whole ride).
            // --gatesim W N: run THE PORT'S OWN ParkGate.State for world W over N park frames and print the angle,
            // so "does the swing settle" is answered by the shipped code rather than by a model of it.
            int gsimAt = Array.IndexOf(args, "--gatesim");
            if (gsimAt >= 0 && gsimAt + 2 < args.Length)
            {
                int w = int.Parse(args[gsimAt + 1]), n = int.Parse(args[gsimAt + 2]);
                var gate = ParkGate.ForWorld(w);
                if (gate == null) { Console.WriteLine($"no gate for world {w}"); return 1; }
                var st = new ParkGate.State(gate);
                int frameTime = (int)(EntranceFlags.TimeUnitsPerSecond / ParticleSystem.FramesPerSecond);
                Console.WriteLine($"world {w}: frame time {frameTime}, speed {st.Speed}");
                for (int f = 0; f < n; f++)
                {
                    st.Update(frameTime, true);
                    if (f < 40 || f % 10 == 0 || f >= n - 12)
                        Console.WriteLine($"  f{f,4}  angle {(short)st.Angle,6}  ({(short)st.Angle * 90.0 / 1024:F1} deg)  speed {st.Speed}");
                }
                return 0;
            }
            int rigAt = Array.IndexOf(args, "--rig");
            if (rigAt >= 0 && rigAt + 1 < args.Length)
            {
                var ag = Archive(disc);
                int re = int.Parse(args[rigAt + 1]);
                var bytes = ag.Read(ag.Entries[re]);
                if (!MeshContainer.TryParse(bytes, out var rc, out string rce)) { Console.WriteLine($"entry {re}: {rce}"); return 1; }
                for (int sub = 0; sub < rc.Subs.Count; sub++)
                {
                    if (!rc.TryParseMesh(bytes, sub, out var rm, out rce)) { Console.WriteLine($"sub {sub}: {rce}"); continue; }
                    int len = MeshPose.AnimationLength(rm);
                    Console.WriteLine($"sub {sub}: {rm.VertexCount} vertices, {rm.Faces.Count} faces, {rm.BoneCount} bones, {rm.TrackCount} tracks, header word {rm.HeaderWord0}, keys end {len}");
                    foreach (var tr in rm.Tracks)
                        Console.WriteLine($"  track type {tr.Type} target {tr.Target} index {tr.Header4} keys {tr.KeyCount} scales {tr.Scales.Length} timed {tr.IsTimed}");
                    for (int t = 0; t <= len; t += Math.Max(1, len / 6))
                    {
                        var pose = MeshPose.Evaluate(rm, t);
                        var parts = new List<string>();
                        for (int b = 0; b < pose.Bones.Length; b++)
                        {
                            var (rr, x, y, z) = pose.Bones[b];
                            parts.Add($"b{b} [{rr.M00:F2} {rr.M01:F2} {rr.M02:F2} / {rr.M10:F2} {rr.M11:F2} {rr.M12:F2} / {rr.M20:F2} {rr.M21:F2} {rr.M22:F2}] t({x:F0},{y:F0},{z:F0})");
                        }
                        int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
                        foreach (var (vx, vy, vz) in pose.Vertices)
                        { x0 = Math.Min(x0, vx); x1 = Math.Max(x1, vx); y0 = Math.Min(y0, vy); y1 = Math.Max(y1, vy); z0 = Math.Min(z0, vz); z1 = Math.Max(z1, vz); }
                        Console.WriteLine($"  t={t}: verts x {x0}..{x1} y {y0}..{y1} z {z0}..{z1}   " + string.Join("  ", parts));
                    }
                }
                return 0;
            }
            int mbAt = Array.IndexOf(args, "--model-bounds");
            if (mbAt >= 0 && mbAt + 1 < args.Length)
            {
                var ag = Archive(disc);
                int me = int.Parse(args[mbAt + 1]);
                var bytes = ag.Read(ag.Entries[me]);
                if (!MeshContainer.TryParse(bytes, out var mc, out string mce) || !mc.TryParseMesh(bytes, 0, out var mm, out mce))
                { Console.WriteLine($"entry {me}: {mce}"); return 1; }
                void Report(string what, Func<int, (int X, int Y, int Z)> v, int n)
                {
                    int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
                    for (int i = 0; i < n; i++) { var (x, y, z) = v(i); x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z); }
                    Console.WriteLine($"  {what}: x {x0}..{x1}  y {y0}..{y1}  z {z0}..{z1}");
                }
                Console.WriteLine($"entry {me}: {mm.VertexCount} vertices");
                Report("rest", i => ((int)mm.Vertices[i * 3], (int)mm.Vertices[i * 3 + 1], (int)mm.Vertices[i * 3 + 2]), mm.VertexCount);
                if (mm.Tracks != null) { var pv = MeshPose.Evaluate(mm, 0).Vertices; Report("posed t=0", i => pv[i], pv.Length); }
                var def = AttractionDefinition.Read(me, bytes);
                if (def != null) Console.WriteLine($"  record: type {def.Type} footprint {def.Width}x{def.Depth} entrance {def.Entrance} exit {def.Exit} facing {def.EntranceFacing}/{def.ExitFacing}");
                return 0;
            }

            // --sfx GROUP [DIR]: a sound-effect group (SoundGroup: the pair of entries at 0x800F91D0), every sound's
            // record, length and rate, and with a directory each one as a WAV to listen to.
            int sfxAt = Array.IndexOf(args, "--sfx");
            if (sfxAt >= 0 && sfxAt + 1 < args.Length)
            {
                var sg = Archive(disc);
                var exeF = disc.Find(AssetSelfTest.GameExecutable);
                if (sg == null || exeF == null) { Console.WriteLine("no archive or executable"); return 1; }
                int grp = int.Parse(args[sfxAt + 1]);
                string dir = sfxAt + 2 < args.Length && !args[sfxAt + 2].StartsWith("--") ? args[sfxAt + 2] : null;
                var exeB = disc.ReadFile(exeF);
                var ents = SoundGroup.Entries(exeB, AssetSelfTest.GameExecutableBase, grp);
                var group = SoundGroup.Load(sg, exeB, AssetSelfTest.GameExecutableBase, grp);
                if (group == null) { Console.WriteLine($"group {grp}: not loadable"); return 1; }
                Console.WriteLine($"group {grp}: entries {ents?.Samples}/{ents?.Table}, {group.Sounds.Count} sounds");
                if (dir != null) System.IO.Directory.CreateDirectory(dir);
                for (int i = 0; i < group.Sounds.Count; i++)
                {
                    var snd = group.Sounds[i];
                    var pcm = group.Decode(i);
                    int n = pcm?.SampleCount ?? 0, peak = 0;
                    if (pcm != null) foreach (var v in pcm.Samples) peak = Math.Max(peak, Math.Abs((int)v));
                    Console.WriteLine($"  #{i,2}  flag {snd.Flag}  pitch 0x{snd.Pitch:x4} = {snd.SampleRate,5} Hz  offset {snd.Offset,7}  " +
                                      $"{n,6} samples = {(n / (double)snd.SampleRate):0.000} s  peak {peak}" + (pcm != null && pcm.LoopStart >= 0 ? $"  loops at {pcm.LoopStart}" : ""));
                    if (dir != null && pcm != null && n > 0)
                    {
                        using var fs = System.IO.File.Create(System.IO.Path.Combine(dir, $"g{grp}_{i:00}.wav"));
                        using var bw = new System.IO.BinaryWriter(fs);
                        bw.Write("RIFF"u8.ToArray()); bw.Write(36 + n * 2); bw.Write("WAVEfmt "u8.ToArray()); bw.Write(16);
                        bw.Write((short)1); bw.Write((short)1); bw.Write(snd.SampleRate); bw.Write(snd.SampleRate * 2);
                        bw.Write((short)2); bw.Write((short)16); bw.Write("data"u8.ToArray()); bw.Write(n * 2);
                        foreach (var v in pcm.Samples) bw.Write(v);
                    }
                }
                return 0;
            }

            // --buildable [MAP]: where a ride or shop may stand on each park map (ParkBuild, the game's placement
            // test), on the map as loaded (ParkPaths, which needs the executable's tables); with a map entry, the
            // grid itself.
            int bdAt = Array.IndexOf(args, "--buildable");
            if (bdAt >= 0)
            {
                var bg = Archive(disc);
                if (bg == null) { Console.WriteLine("no archive"); return 1; }
                return Buildable(disc, bg, bdAt + 1 < args.Length && int.TryParse(args[bdAt + 1], out int bm) ? bm : -1);
            }

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
            // --semi-list: models with semi-transparent faces (MeshFace.SemiTransparent), and their blend modes.
            if (Array.IndexOf(args, "--semi-list") >= 0)
            {
                var ga = Archive(disc);
                if (ga == null) return 1;
                int models = 0, withSemi = 0;
                foreach (var e in ga.Entries)
                {
                    var bytes = ga.Read(e);
                    if (!MeshContainer.IsContainer(bytes) || !MeshContainer.TryParse(bytes, out var c, out _)) continue;
                    for (int i = 0; i < c.SubCount; i++)
                    {
                        if (!c.TryParseMesh(bytes, i, out var m, out _) || m.Faces.Count == 0) continue;
                        models++;
                        var modes = new SortedDictionary<int, int>();
                        foreach (var f in m.Faces) if (f.SemiTransparent) modes[f.BlendMode] = modes.GetValueOrDefault(f.BlendMode) + 1;
                        if (modes.Count == 0) continue;
                        withSemi++;
                        Console.WriteLine($"#{e.Index} sub {i}: {string.Join(", ", System.Linq.Enumerable.Select(modes, kv => $"{kv.Value} faces mode {kv.Key}"))} of {m.Faces.Count}");
                    }
                }
                Console.WriteLine($"{withSemi} of {models} models have semi-transparent faces");
                return 0;
            }
            // --sprites SHEET OUT i,j,k: those sprites of one sheet side by side (sheet orientation), as raw RGBA.
            int spritesAt = Array.IndexOf(args, "--sprites");
            if (spritesAt >= 0 && spritesAt + 3 < args.Length)
            {
                var gs = Archive(disc);
                if (gs == null) return 1;
                if (!TextureSheet.TryParse(gs.Read(gs.Entries[int.Parse(args[spritesAt + 1])]), out var sh, out string sherr)) { Console.WriteLine(sherr); return 1; }
                var imgs = new List<TpwImage>();
                foreach (var part in args[spritesAt + 3].Split(',')) { var im = sh.RenderSprite(int.Parse(part)); if (im != null) imgs.Add(im); }
                int tw = 0, th = 0;
                foreach (var im in imgs) { tw += im.Width + 4; th = Math.Max(th, im.Height); }
                var outRgba = new byte[Math.Max(1, tw * th) * 4];
                int ox = 0;
                foreach (var im in imgs)
                {
                    for (int y = 0; y < im.Height; y++)
                        Array.Copy(im.Rgba, y * im.Width * 4, outRgba, (y * tw + ox) * 4, im.Width * 4);
                    ox += im.Width + 4;
                }
                System.IO.File.WriteAllBytes(args[spritesAt + 2], outRgba);
                Console.WriteLine($"sprites -> {args[spritesAt + 2]} {tw}x{th} rgba");
                return 0;
            }
            int facesAt = Array.IndexOf(args, "--model-faces");
            if (facesAt >= 0 && facesAt + 1 < args.Length)
                return ModelFaces(disc, int.Parse(args[facesAt + 1]),
                                  facesAt + 2 < args.Length && !args[facesAt + 2].StartsWith("-") ? Convert.ToInt32(args[facesAt + 2], 16) : -1);
            // --extract NAME OUT: one file off the disc, byte for byte (e.g. TPW.OVL, the code overlays).
            int extractAt = Array.IndexOf(args, "--extract");
            if (extractAt >= 0 && extractAt + 2 < args.Length)
            {
                var xf = disc.Find(args[extractAt + 1]);
                if (xf == null) { Console.WriteLine($"no {args[extractAt + 1]} on the disc"); return 1; }
                var xb = disc.ReadFile(xf);
                System.IO.File.WriteAllBytes(args[extractAt + 2], xb);
                Console.WriteLine($"{xf.Name}: {xb.Length:n0} bytes -> {args[extractAt + 2]}");
                return 0;
            }
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
            // --tilefield E: the histogram of byte +6's top two bits over map entry E — the per-tile height the
            // camera's own ground sampler adds (0x80050938 with its third argument, 0x8004D208: (b6 >> 6) << 10).
            int tfAt = Array.IndexOf(args, "--tilefield");
            if (tfAt >= 0 && tfAt + 1 < args.Length)
            {
                var af5 = disc.Find(AssetSelfTest.AssetArchive);
                if (af5 == null) { Console.WriteLine("no asset archive on this disc"); return 1; }
                if (!GazArchive.TryParse(disc.ReadFile(af5), out var gz5, out string gerr5))
                { Console.WriteLine("archive: " + gerr5); return 1; }
                int me = int.Parse(args[tfAt + 1]);
                ParkMap found = null;
                foreach (var (entry, map) in ParkMap.FindAll(gz5)) if (entry.Index == me) { found = map; break; }
                if (found == null) { Console.WriteLine($"entry {me} is not a park map"); return 1; }
                var hist = new int[4];
                var firstAt = new (int X, int Z)[4];
                for (int z = 0; z < found.Height; z++)
                    for (int x = 0; x < found.Width; x++)
                    {
                        int f = found[x, z].CameraLift >> 10;
                        if (f < 0 || f > 3) continue;
                        if (hist[f]++ == 0) firstAt[f] = (x, z);
                    }
                Console.WriteLine($"map #{me}: {found.Width}x{found.Height} tiles");
                int noGround = 0; int minH = int.MaxValue, maxH = int.MinValue;
                for (int z = 0; z < found.Height; z++)
                    for (int x = 0; x < found.Width; x++)
                    {
                        var t = found[x, z];
                        if (t.NoGround) noGround++;
                        minH = Math.Min(minH, t.HeightUnits); maxH = Math.Max(maxH, t.HeightUnits);
                    }
                Console.WriteLine($"  no-ground tiles {noGround}, height {minH}..{maxH} units");
                int bMin = int.MaxValue, bMax = int.MinValue, bCount = 0;
                var heights = new SortedDictionary<int, int>();
                for (int z = 0; z < found.Height; z++)
                    for (int x = 0; x < found.Width; x++)
                    {
                        var t = found[x, z];
                        heights[t.HeightUnits] = heights.TryGetValue(t.HeightUnits, out int hc) ? hc + 1 : 1;
                        if (t.Unbuildable) continue;
                        bCount++; bMin = Math.Min(bMin, t.HeightUnits); bMax = Math.Max(bMax, t.HeightUnits);
                    }
                Console.WriteLine($"  buildable tiles {bCount}, height {bMin}..{bMax}");
                Console.Write("  height histogram:");
                foreach (var kv in heights) if (kv.Value >= 40) Console.Write($" {kv.Key}x{kv.Value}");
                Console.WriteLine();
                var world5 = ParkWorlds.ForMap(me);
                if (world5 != null && world5.GroundSheet < gz5.Entries.Count &&
                    TextureSheet.TryParse(gz5.Read(gz5.Entries[world5.GroundSheet]), out var gsh5, out _))
                {
                    int wet = 0, wetMin = int.MaxValue, wetMax = int.MinValue;
                    for (int z = 0; z < found.Height; z++)
                        for (int x = 0; x < found.Width; x++)
                        {
                            int sprite = found[x, z].GroundSprite;
                            if (sprite < 0 || sprite >= gsh5.Sprites.Count || !gsh5.Sprites[sprite].Scrolls) continue;
                            wet++; wetMin = Math.Min(wetMin, found[x, z].HeightUnits); wetMax = Math.Max(wetMax, found[x, z].HeightUnits);
                        }
                    Console.WriteLine($"  water tiles (scrolling ground sprite): {wet}" + (wet > 0 ? $", height {wetMin}..{wetMax}" : ""));
                }
                int rgAt = Array.IndexOf(args, "--region");
                if (rgAt >= 0 && rgAt + 4 < args.Length)
                {
                    int rx0 = int.Parse(args[rgAt + 1]), rz0 = int.Parse(args[rgAt + 2]);
                    int rx1 = int.Parse(args[rgAt + 3]), rz1 = int.Parse(args[rgAt + 4]);
                    var gsheet = ParkWorlds.ForMap(me) is { } w6 && w6.GroundSheet < gz5.Entries.Count &&
                                 TextureSheet.TryParse(gz5.Read(gz5.Entries[w6.GroundSheet]), out var gs6, out _) ? gs6 : null;
                    for (int z = rz0; z <= rz1 && z < found.Height; z++)
                    {
                        Console.Write($"  z={z,3}: ");
                        for (int x = rx0; x <= rx1 && x < found.Width; x++)
                        {
                            var t = found[x, z];
                            bool scrolls = gsheet != null && t.GroundSprite < gsheet.Sprites.Count && gsheet.Sprites[t.GroundSprite].Scrolls;
                            Console.Write($"[{x,2}] h{t.HeightUnits,4} s{t.GroundSprite,4}{(scrolls ? "~" : " ")}f{t.Flags:x2} t{t.Raw0}  ");
                        }
                        Console.WriteLine();
                    }
                    return 0;
                }
                int mid = found.Height / 2;
                Console.Write($"  heights along z={mid}: ");
                for (int x = 0; x < found.Width; x += 2) Console.Write(found[x, mid].HeightUnits + " ");
                Console.WriteLine();
                Console.Write($"  heights along x={found.Width / 2}: ");
                for (int z = 0; z < found.Height; z += 3) Console.Write(found[found.Width / 2, z].HeightUnits + " ");
                Console.WriteLine();
                for (int f = 0; f < 4; f++)
                    Console.WriteLine($"  lift {f} ({f * 1024,5} units): {hist[f],6} tiles" +
                                      (hist[f] > 0 ? $"   first at {firstAt[f].X},{firstAt[f].Z}" +
                                       $"  (height {found[firstAt[f].X, firstAt[f].Z].HeightUnits}, sprite {found[firstAt[f].X, firstAt[f].Z].GroundSprite}," +
                                       $" no-ground {found[firstAt[f].X, firstAt[f].Z].NoGround})" : ""));
                return 0;
            }
            // --people OUT.rgba: one row per person the game knows (PeopleSheet's twelve blocks), five frames of
            // one facing each, so who is a guest and who is staff can be settled by looking.
            int ppAt = Array.IndexOf(args, "--people");
            if (ppAt >= 0 && ppAt + 1 < args.Length)
            {
                var af4 = disc.Find(AssetSelfTest.AssetArchive);
                if (af4 == null) { Console.WriteLine("no asset archive on this disc"); return 1; }
                if (!GazArchive.TryParse(disc.ReadFile(af4), out var gz4, out string gerr4))
                { Console.WriteLine("archive: " + gerr4); return 1; }
                var people = PeopleSheet.Read(gz4);
                if (people == null) { Console.WriteLine("no people sheet"); return 1; }
                var psh = people.Sheet269;
                int facing = Array.IndexOf(args, "--facing") is int fa && fa >= 0 && fa + 1 < args.Length ? int.Parse(args[fa + 1]) : 5;
                int cw = 28, ch = 30, cols = PeopleSheet.WalkFrames, rows = PeopleSheet.Blocks.Length;
                int gw = cw * cols, gh = ch * rows;
                var grid = new byte[gw * gh * 4];
                for (int i = 0; i < gw * gh; i++) { grid[i * 4] = 24; grid[i * 4 + 1] = 16; grid[i * 4 + 2] = 28; grid[i * 4 + 3] = 255; }
                for (int b = 0; b < rows; b++)
                    for (int f = 0; f < cols; f++)
                    {
                        int si = people.WalkSprite(b, facing, f);
                        var img = psh.RenderSprite(si);
                        if (img == null) continue;
                        var sp = psh.Sprites[si];
                        int cx = f * cw + cw / 2, cy = b * ch + ch - 4;
                        for (int y = 0; y < img.Height; y++)
                            for (int x = 0; x < img.Width; x++)
                            {
                                int o = (y * img.Width + x) * 4;
                                if (img.Rgba[o + 3] == 0) continue;
                                int X = cx + sp.OffsetX + x, Y = cy + sp.OffsetY + y;
                                if (X < 0 || Y < 0 || X >= gw || Y >= gh) continue;
                                int d = (Y * gw + X) * 4;
                                grid[d] = img.Rgba[o]; grid[d + 1] = img.Rgba[o + 1]; grid[d + 2] = img.Rgba[o + 2]; grid[d + 3] = 255;
                            }
                    }
                System.IO.File.WriteAllBytes(args[ppAt + 1], grid);
                Console.WriteLine($"{rows} people x {cols} frames of facing {facing} -> {gw}x{gh} RGBA at {args[ppAt + 1]}");
                for (int b = 0; b < rows; b++)
                    Console.WriteLine($"  row {b}: entry {PeopleSheet.Blocks[b].Entry}, block base {PeopleSheet.Blocks[b].Base}, walk at {people.WalkSprite(b, 0, 0)}");
                return 0;
            }
            // --spritegrid E FIRST N COLS OUT.rgba: sprites FIRST..FIRST+N-1 of entry E laid out in a grid, each
            // placed by its own offsets inside its cell so the figures line up as the game draws them. For reading
            // an animation's order off the picture instead of guessing it.
            int sgAt = Array.IndexOf(args, "--spritegrid");
            if (sgAt >= 0 && sgAt + 5 < args.Length)
            {
                var af3 = disc.Find(AssetSelfTest.AssetArchive);
                if (af3 == null) { Console.WriteLine("no asset archive on this disc"); return 1; }
                if (!GazArchive.TryParse(disc.ReadFile(af3), out var gz3, out string gerr3))
                { Console.WriteLine("archive: " + gerr3); return 1; }
                int ge = int.Parse(args[sgAt + 1]), first = int.Parse(args[sgAt + 2]);
                int count = int.Parse(args[sgAt + 3]), cols = Math.Max(1, int.Parse(args[sgAt + 4]));
                if (!TextureSheet.TryParse(gz3.Read(gz3.Entries[ge]), out var gsh, out string gherr))
                { Console.WriteLine($"entry {ge}: {gherr}"); return 1; }
                int cw = 0, ch = 0;
                for (int i = first; i < first + count && i < gsh.Sprites.Count; i++)
                { cw = Math.Max(cw, gsh.Sprites[i].W + 8); ch = Math.Max(ch, gsh.Sprites[i].H + 8); }
                int rows = (count + cols - 1) / cols, gw = cw * cols, gh = ch * rows;
                var grid = new byte[gw * gh * 4];
                for (int i = 0; i < gw * gh; i++) { grid[i * 4] = 24; grid[i * 4 + 1] = 16; grid[i * 4 + 2] = 28; grid[i * 4 + 3] = 255; }
                for (int k = 0; k < count && first + k < gsh.Sprites.Count; k++)
                {
                    var img = gsh.RenderSprite(first + k);
                    if (img == null) continue;
                    var sp = gsh.Sprites[first + k];
                    int cx = (k % cols) * cw + cw / 2, cy = (k / cols) * ch + ch - 4;   // feet on the cell's floor
                    for (int y = 0; y < img.Height; y++)
                        for (int x = 0; x < img.Width; x++)
                        {
                            int si = (y * img.Width + x) * 4;
                            if (img.Rgba[si + 3] == 0) continue;
                            int X = cx + sp.OffsetX + x, Y = cy + sp.OffsetY + y;
                            if (X < 0 || Y < 0 || X >= gw || Y >= gh) continue;
                            int di = (Y * gw + X) * 4;
                            grid[di] = img.Rgba[si]; grid[di + 1] = img.Rgba[si + 1];
                            grid[di + 2] = img.Rgba[si + 2]; grid[di + 3] = 255;
                        }
                }
                System.IO.File.WriteAllBytes(args[sgAt + 5], grid);
                Console.WriteLine($"entry {ge}: sprites {first}..{first + count - 1} -> {gw}x{gh} RGBA ({cols} x {rows} cells of {cw}x{ch}) at {args[sgAt + 5]}");
                return 0;
            }
            // --spritesheet E OUT.rgba: entry E's texture sheet with every sprite painted in its own palette,
            // in the VRAM layout the game uploads — the quickest way to SEE what a sheet holds.
            int ssAt = Array.IndexOf(args, "--spritesheet");
            if (ssAt >= 0 && ssAt + 2 < args.Length)
            {
                var af2 = disc.Find(AssetSelfTest.AssetArchive);
                if (af2 == null) { Console.WriteLine("no asset archive on this disc"); return 1; }
                if (!GazArchive.TryParse(disc.ReadFile(af2), out var gz2, out string gerr2))
                { Console.WriteLine("archive: " + gerr2); return 1; }
                int se = int.Parse(args[ssAt + 1]);
                if (se < 0 || se >= gz2.Entries.Count) { Console.WriteLine("no such entry"); return 1; }
                if (!TextureSheet.TryParse(gz2.Read(gz2.Entries[se]), out var ssh, out string sherr))
                { Console.WriteLine($"entry {se}: {sherr}"); return 1; }
                var img = ssh.RenderSprites($"sheet #{se}");
                System.IO.File.WriteAllBytes(args[ssAt + 2], img.Rgba);
                Console.WriteLine($"entry {se}: {ssh.Sprites.Count} sprites, texpage {ssh.TPage:x2}, " +
                                  $"{ssh.Columns}x{ssh.Rows} pages -> {img.Width}x{img.Height} RGBA at {args[ssAt + 2]}");
                var sizes = new SortedDictionary<(int, int), int>();
                foreach (var sp in ssh.Sprites) { var k = (sp.W, sp.H); sizes[k] = sizes.TryGetValue(k, out int c2) ? c2 + 1 : 1; }
                foreach (var kv in sizes) if (kv.Value >= 8) Console.WriteLine($"  {kv.Key.Item1}x{kv.Key.Item2}: {kv.Value} sprites");
                if (Array.IndexOf(args, "--table") >= 0)
                    for (int i = 0; i < ssh.Sprites.Count; i++)
                    {
                        var sp = ssh.Sprites[i];
                        Console.WriteLine($"  [{i,3}] page {sp.TPage:x2} clut {sp.Clut:x4} u {sp.U,3} v {sp.V,3} {sp.W,3}x{sp.H,-3} off {sp.OffsetX,3},{sp.OffsetY,-3} flags {sp.Flags}");
                    }
                return 0;
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
