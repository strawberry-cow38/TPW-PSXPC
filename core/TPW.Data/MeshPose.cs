using System;

namespace TPW.Data
{
    /// <summary>
    /// A mesh evaluated at a point in time: bone poses, and vertices moved by the animation.
    ///
    /// ⚠ WHAT THIS CAN AND CANNOT DO, because the difference is not obvious from the outside.
    /// Vertex animation is complete: the position tracks drive scatter sources, the sources reach
    /// vertices through <see cref="VertexBinding"/>, and other tracks write vertices directly.
    /// **Nothing established connects BONES to vertices.** The scatter sources are driven by position
    /// tracks, not by the skeleton, so a model animated purely through its bones will pose its skeleton
    /// here and not move a single vertex. That is a gap in what has been decoded, not a bug to hunt in
    /// a renderer, and it is why <see cref="Bones"/> and <see cref="Vertices"/> are separate outputs
    /// rather than one pipeline.
    ///
    /// Everything is in the FILE's left-handed frame. Convert with <see cref="ToGodot(int,int,int)"/>
    /// after posing, never before -- the reflection preserves lengths and commutes with the blend, so
    /// converting late keeps the integer arithmetic identical to the console's.
    /// </summary>
    public sealed class MeshPose
    {
        /// <summary>Bone transforms in model space at this time, rest pose where a bone has no track.</summary>
        public (BoneRest R, float X, float Y, float Z)[] Bones = Array.Empty<(BoneRest, float, float, float)>();

        /// <summary>Vertex positions after scatter and direct writes, in the file's frame.</summary>
        public (int X, int Y, int Z)[] Vertices = Array.Empty<(int, int, int)>();

        /// <summary>Vertex positions from the bone skin, before the scatter path is applied.</summary>
        public (int X, int Y, int Z)[] Skinned = Array.Empty<(int, int, int)>();

        /// <summary>Which vertices any bone reached.</summary>
        public bool[] SkinnedMask = Array.Empty<bool>();

        /// <summary>The scatter sources this time produced, before they reach vertices.</summary>
        public (short X, short Y, short Z)[] Sources = Array.Empty<(short, short, short)>();

        /// <summary>Evaluate <paramref name="mesh"/> at <paramref name="time"/>.</summary>
        public static MeshPose Evaluate(Mesh mesh, int time)
        {
            var pose = new MeshPose();
            if (mesh == null) return pose;

            // --- bones: rest pose, then any bone track that names them -------------------------
            // ⚠ A TRACK GIVES A BONE'S **LOCAL** TRANSFORM, NOT ITS WORLD ONE. Overwriting the composed
            // world transform with the keyframe's values detaches that bone from its parents: its
            // translation becomes a small local offset and everything it skins collapses toward the
            // origin. So pose locally first, then compose the whole hierarchy in one forward pass --
            // which is exactly what the rest pose already does, and for the same reason.
            var skel = mesh.Skeleton;
            if (skel != null && skel.Count > 0)
            {
                int n = skel.Count;
                var localR = new BoneRest[n];
                var localT = new (float X, float Y, float Z)[n];
                for (int b = 0; b < n; b++)
                {
                    localR[b] = skel.Bones[b].ToMatrix();
                    localT[b] = (skel.Bones[b].Tx, skel.Bones[b].Ty, skel.Bones[b].Tz);
                }
                if (mesh.Tracks != null)
                    foreach (var t in mesh.Tracks)
                    {
                        if (t.Type != 6 || t.Keys.Length == 0) continue;
                        int b = t.BoneIndex;
                        if (b < 0 || b >= n) continue;
                        var k = t.Sample(time);
                        localR[b] = QuatToMatrix(k.Qx, k.Qy, k.Qz, k.Qw);
                        localT[b] = (k.Tx, k.Ty, k.Tz);
                    }

                var world = new (BoneRest R, float X, float Y, float Z)[n];
                for (int b = 0; b < n; b++)
                {
                    int par = skel.Bones[b].Parent;
                    if (par < 0 || par >= b) { world[b] = (localR[b], localT[b].X, localT[b].Y, localT[b].Z); continue; }
                    var (pr, px, py, pz) = world[par];
                    var (lx, ly, lz) = localT[b];
                    world[b] = (BoneRest.Multiply(pr, localR[b]),
                                px + pr.M00 * lx + pr.M01 * ly + pr.M02 * lz,
                                py + pr.M10 * lx + pr.M11 * ly + pr.M12 * lz,
                                pz + pr.M20 * lx + pr.M21 * ly + pr.M22 * lz);
                }
                pose.Bones = world;
            }

            // --- bone skinning ------------------------------------------------------------------
            // ⭐ THE LINK FROM BONES TO GEOMETRY, from 0x8002e1a4. Each bone names a slice of the skin
            // table; the game loads that bone's matrix into the GTE, and for every entry in its slice
            // transforms the entry's BONE-SPACE position by the matrix and accumulates it into the
            // entry's vertex, scaled by the weight.
            //
            // What makes this the binding rather than a plausible reading: every vertex's weights across
            // all its bones sum to EXACTLY 16384 -- all 14,531 of them, no exceptions, not one off by a
            // single unit. The bone ranges also tile the skin table on 209 of 232 meshes.
            var skinned = new bool[mesh.VertexCount];
            if (skel != null && skel.Count > 0 && mesh.Binding != null && mesh.Binding.Records.Length > 0)
            {
                var recs = mesh.Binding.Records;
                var acc = new (int X, int Y, int Z)[mesh.VertexCount];
                for (int b = 0; b < skel.Count && b < pose.Bones.Length; b++)
                {
                    var bone = skel.Bones[b];
                    var (R, tx, ty, tz) = pose.Bones[b];
                    for (int k = 0; k < bone.SkinCount; k++)
                    {
                        int at = bone.SkinStart + k;
                        if (at < 0 || at >= recs.Length) continue;
                        var r = recs[at];
                        if (r.Vertex >= acc.Length) continue;
                        // bone-space position through the bone's world transform
                        float px = tx + R.M00 * r.X + R.M01 * r.Y + R.M02 * r.Z;
                        float py = ty + R.M10 * r.X + R.M11 * r.Y + R.M12 * r.Z;
                        float pz = tz + R.M20 * r.X + R.M21 * r.Y + R.M22 * r.Z;
                        acc[r.Vertex].X += (int)(px * r.Weight) >> 14;
                        acc[r.Vertex].Y += (int)(py * r.Weight) >> 14;
                        acc[r.Vertex].Z += (int)(pz * r.Weight) >> 14;
                        skinned[r.Vertex] = true;
                    }
                }
                pose.Skinned = acc;
                pose.SkinnedMask = skinned;
            }

            // --- scatter sources, then the vertices they reach ---------------------------------
            var bind = mesh.Binding;
            int nSrc = bind?.SourceCount ?? 0;
            // ⚠ SEED FROM THE RECORD'S OWN DEFAULT, NOT ZERO. Tracks drive only the odd source slots
            // (every animated mesh on the disc drives exactly half), and the console seeds the whole
            // region from the run records. Zeroing the undriven half pulls the vertices they reach to
            // the origin -- reported from the browser as "the animated parts are stretching from 0,0".
            var sources = new (short X, short Y, short Z)[nSrc];
            for (int i = 0; i < nSrc; i++) sources[i] = (bind.Runs[i].X, bind.Runs[i].Y, bind.Runs[i].Z);
            if (mesh.Tracks != null)
                foreach (var t in mesh.Tracks)
                {
                    if (t.Target != TrackTarget.ScatterSource || t.Positions.Length == 0) continue;
                    int i = t.Header4;
                    if (i >= 0 && i < nSrc) sources[i] = SampleAt(t, time);
                }
            pose.Sources = sources;

            // ⚠ A VERTEX NO SOURCE REACHES KEEPS ITS REST POSITION -- it does not go to the origin.
            // The scatter is a weighted AVERAGE of sources, not a delta on top of the rest pose: the
            // source coordinates occupy the same range as the vertices themselves (median 383 against
            // 262, maxima 1448 against 1443), and the weights reaching a vertex sum to exactly 1. So a
            // reached vertex is replaced outright, and an unreached one was never written at all.
            // Zeroing those instead would collapse them onto the origin and read as a shattered model.
            var verts = new (int X, int Y, int Z)[mesh.VertexCount];
            for (int v = 0; v < verts.Length && v * 3 + 2 < mesh.Vertices.Length; v++)
                verts[v] = ((int)mesh.Vertices[v * 3], (int)mesh.Vertices[v * 3 + 1], (int)mesh.Vertices[v * 3 + 2]);

            if (bind != null && bind.Records.Length > 0 && bind.SourceCount > 0)
            {
                var scattered = new (int X, int Y, int Z)[mesh.VertexCount];
                bind.Scatter(sources, scattered);

                // ⚠ ONLY THE RECORDS A RUN ACTUALLY COVERS. The runs do not start at record 0 -- on 16 of
                // the 30 scattering meshes the first run begins partway in, leaving a leading block of
                // records that no run reads. Overwriting their vertices from the scatter buffer sets them
                // to an uncomputed zero and collapses them onto the origin. (The earlier "runs cover the
                // table, 1.000" measured where the chain ENDS and never checked where it starts.)
                foreach (var run in bind.Runs)
                    for (int k = 0; k < run.Count; k++)
                    {
                        int at = run.Start + k;
                        if (at < 0 || at >= bind.Records.Length) continue;
                        int vtx = bind.Records[at].Vertex;
                        if (vtx < verts.Length) verts[vtx] = scattered[vtx];
                    }
            }

            // Direct vertex writes land after the scatter, as they do on the console.
            if (mesh.Tracks != null)
                foreach (var t in mesh.Tracks)
                {
                    if (t.Target != TrackTarget.Vertex || t.Positions.Length == 0) continue;
                    int v = t.Header4;
                    if (v < 0 || v >= verts.Length) continue;
                    var p = SampleAt(t, time);
                    verts[v] = (p.X, p.Y, p.Z);
                }
            // Skinned vertices take the bone result. Weights summing to 1 make this a replacement,
            // not an addition on top of the rest pose.
            for (int v = 0; v < verts.Length; v++)
                if (v < skinned.Length && skinned[v]) verts[v] = pose.Skinned[v];

            pose.Vertices = verts;
            return pose;
        }

        /// <summary>One position track's value at a time. A timed track interpolates between the keys
        /// whose span contains it; an untimed track is one sample per tick, so the time IS the index and
        /// there is nothing to interpolate. Both hold their ends.</summary>
        public static (short X, short Y, short Z) SampleAt(AnimTrack t, int time)
        {
            var p = t.Positions;
            if (p.Length == 0) return default;
            if (!t.IsTimed)
            {
                int i = time < 0 ? 0 : time >= p.Length ? p.Length - 1 : time;
                return (p[i].X, p[i].Y, p[i].Z);
            }
            if (time <= p[0].Time) return (p[0].X, p[0].Y, p[0].Z);
            for (int i = 0; i + 1 < p.Length; i++)
            {
                int start = p[i].Time, dur = p[i].Duration;
                if (dur <= 0 || time < start || time >= start + dur) continue;
                int w = ((time - start) << 12) / dur;           // the game's own weight, integer division
                return ((short)(p[i].X + (((p[i + 1].X - p[i].X) * w) >> 12)),
                        (short)(p[i].Y + (((p[i + 1].Y - p[i].Y) * w) >> 12)),
                        (short)(p[i].Z + (((p[i + 1].Z - p[i].Z) * w) >> 12)));
            }
            var last = p[^1];
            return (last.X, last.Y, last.Z);
        }

        /// <summary>File frame to Godot's: the file is left-handed (z into the screen), Godot is
        /// right-handed, and negating z converts. Being a reflection it also reverses triangle winding,
        /// which the mesh builder handles separately.</summary>
        public static (float X, float Y, float Z) ToGodot(int x, int y, int z) => (x, y, -z);

        /// <summary>The same reflection applied to a rotation. Conjugating a rotation by the mirror
        /// diag(1,1,-1) sends its axis to (x, y, -z) AND negates the angle, and the two together land on
        /// (-x, -y, z, w) -- so it is not simply "negate z" the way a position is.</summary>
        public static (float X, float Y, float Z, float W) QuatToGodot(float x, float y, float z, float w)
            => (-x, -y, z, w);

        static BoneRest QuatToMatrix(float x, float y, float z, float w) => BoneRest.FromRows(
            1 - 2 * (y * y + z * z), 2 * (x * y - z * w),     2 * (x * z + y * w),
            2 * (x * y + z * w),     1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
            2 * (x * z - y * w),     2 * (y * z + x * w),     1 - 2 * (x * x + y * y));
    }
}
