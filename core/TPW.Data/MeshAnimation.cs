using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>A bone's rest orientation: row-major 3x3, already divided by the GTE 4096 scale.</summary>
    public readonly struct BoneRest
    {
        public readonly float M00, M01, M02, M10, M11, M12, M20, M21, M22;
        public BoneRest(ReadOnlySpan<byte> d)
        {
            const float S = 1f / 4096f;
            static short H(ReadOnlySpan<byte> b, int i) => BitConverter.ToInt16(b.Slice(i * 2, 2));
            M00 = H(d, 0) * S; M01 = H(d, 1) * S; M02 = H(d, 2) * S;
            M10 = H(d, 3) * S; M11 = H(d, 4) * S; M12 = H(d, 5) * S;
            M20 = H(d, 6) * S; M21 = H(d, 7) * S; M22 = H(d, 8) * S;
        }
        BoneRest(float a, float b, float c, float d2, float e, float f, float g, float h, float i)
        { M00=a; M01=b; M02=c; M10=d2; M11=e; M12=f; M20=g; M21=h; M22=i; }

        public static BoneRest FromRows(float a, float b, float c, float d, float e, float f,
                                        float g, float h, float i) => new BoneRest(a,b,c,d,e,f,g,h,i);

        /// <summary>Row-major product a*b, for composing a child's rotation through its parent.</summary>
        public static BoneRest Multiply(in BoneRest a, in BoneRest b) => new BoneRest(
            a.M00*b.M00 + a.M01*b.M10 + a.M02*b.M20,  a.M00*b.M01 + a.M01*b.M11 + a.M02*b.M21,  a.M00*b.M02 + a.M01*b.M12 + a.M02*b.M22,
            a.M10*b.M00 + a.M11*b.M10 + a.M12*b.M20,  a.M10*b.M01 + a.M11*b.M11 + a.M12*b.M21,  a.M10*b.M02 + a.M11*b.M12 + a.M12*b.M22,
            a.M20*b.M00 + a.M21*b.M10 + a.M22*b.M20,  a.M20*b.M01 + a.M21*b.M11 + a.M22*b.M21,  a.M20*b.M02 + a.M21*b.M12 + a.M22*b.M22);

        /// <summary>Every row unit length. All 2,295 rest poses on the disc pass at tol 0.045.</summary>
        public bool IsOrthonormal(float tol = 0.045f)
        {
            static float L(float a, float b, float c) => MathF.Sqrt(a * a + b * b + c * c);
            return MathF.Abs(L(M00, M01, M02) - 1f) < tol
                && MathF.Abs(L(M10, M11, M12) - 1f) < tol
                && MathF.Abs(L(M20, M21, M22) - 1f) < tol;
        }
    }

    /// <summary>One keyframe of a type-6 track.</summary>
    public readonly struct AnimKey
    {
        public readonly short Time;             // non-decreasing within a track: 1091/1091 tracks

        /// <summary>How long this key lasts, in the same units as <see cref="Time"/>.
        ///
        /// ⚠ This shipped as an unidentified "Field1". It is the divisor in the game's own blend at
        /// 0x8002da94 -- t = ((now - start) &lt;&lt; 12) / duration -- and the file proves it: across all
        /// 12,334 consecutive keyframe pairs on the disc, Time + Duration equals the next key's Time
        /// exactly, residual zero on every pair.</summary>
        public readonly short Duration;
        public readonly short Tx, Ty, Tz;       // translation, the file's own s16 units
        public readonly float Qx, Qy, Qz, Qw;   // unit quaternion
        public AnimKey(ReadOnlySpan<byte> d)
        {
            const float S = 1f / 4096f;
            static short H(ReadOnlySpan<byte> b, int i) => BitConverter.ToInt16(b.Slice(i * 2, 2));
            Time = H(d, 0); Duration = H(d, 1);
            Tx = H(d, 2); Ty = H(d, 3); Tz = H(d, 4);
            // H(d,5) is zero in all 13,425 keyframes on the disc.
            Qx = H(d, 6) * S; Qy = H(d, 7) * S; Qz = H(d, 8) * S; Qw = H(d, 9) * S;
        }
        public float QuatLength() => MathF.Sqrt(Qx * Qx + Qy * Qy + Qz * Qz + Qw * Qw);

        AnimKey(short time, short dur, short tx, short ty, short tz,
                float qx, float qy, float qz, float qw)
        { Time = time; Duration = dur; Tx = tx; Ty = ty; Tz = tz; Qx = qx; Qy = qy; Qz = qz; Qw = qw; }

        /// <summary>Blend a toward b by w/4096. Translation is linear; rotation is a normalised linear
        /// blend, with b negated when the two quaternions point opposite ways so the blend takes the
        /// short way round -- without that, a pair more than 180 degrees apart sweeps the long arc.</summary>
        public static AnimKey Lerp(in AnimKey a, in AnimKey b, int w)
        {
            float f = w / 4096f, g = 1f - f;
            float bx = b.Qx, by = b.Qy, bz = b.Qz, bw = b.Qw;
            if (a.Qx * bx + a.Qy * by + a.Qz * bz + a.Qw * bw < 0f) { bx = -bx; by = -by; bz = -bz; bw = -bw; }
            float qx = a.Qx * g + bx * f, qy = a.Qy * g + by * f,
                  qz = a.Qz * g + bz * f, qw = a.Qw * g + bw * f;
            float len = MathF.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
            if (len > 1e-6f) { qx /= len; qy /= len; qz /= len; qw /= len; }
            return new AnimKey(a.Time, a.Duration,
                (short)MathF.Round(a.Tx * g + b.Tx * f),
                (short)MathF.Round(a.Ty * g + b.Ty * f),
                (short)MathF.Round(a.Tz * g + b.Tz * f),
                qx, qy, qz, qw);
        }
    }

    /// <summary>
    /// One 40-byte bone record: the skeleton's parent link and the bone's rest transform.
    ///
    /// Fields were identified by property, not by eye:
    ///   +6   parent index, -1 at the root. The ONLY s16 column of the twenty that is both always
    ///        in [-1, nbones) and always acyclic over 323 multi-bone meshes. Two other columns are
    ///        always in range and fail the acyclic test, which is what makes the test worth running.
    ///   +8   rest rotation, unit quaternion x,y,z,w at 4096. Confirmed against an independent
    ///        encoding: for all 2,295 bones that ALSO carry a type-8 matrix track, this quaternion
    ///        reproduces that matrix to a median error of 0.000 and a worst case of 0.001.
    ///   +24  rest translation. Median difference of 0.0 against the mean of the bone's own type-6
    ///        keyframe translations, where the next best 3-wide window scores 181.
    ///   +32  scale, 4096/4096/4096 on every bone on the disc. Read, not assumed.
    ///
    /// ⚠ +16..+23 is NOT identified. It passes a unit-quaternion test at 100%, but so does a
    /// control with every column independently shuffled (81.7%), so that 100% is the column
    /// distributions and not a relationship within the record. Left unnamed on purpose.
    /// </summary>
    public readonly struct Bone
    {
        public readonly short Parent;               // -1 at the root; always < own index
        public readonly float Qx, Qy, Qz, Qw;       // rest rotation
        public readonly short Tx, Ty, Tz;           // rest translation, file units
        public readonly short Sx, Sy, Sz;           // scale, 4096 = 1.0

        public Bone(ReadOnlySpan<byte> d)
        {
            const float S = 1f / 4096f;
            static short H(ReadOnlySpan<byte> b, int i) => BitConverter.ToInt16(b.Slice(i * 2, 2));
            Parent = H(d, 3);
            Qx = H(d, 4) * S; Qy = H(d, 5) * S; Qz = H(d, 6) * S; Qw = H(d, 7) * S;
            Tx = H(d, 12); Ty = H(d, 13); Tz = H(d, 14);
            Sx = H(d, 16); Sy = H(d, 17); Sz = H(d, 18);
        }

        public bool IsRoot => Parent < 0;
        public float QuatLength() => MathF.Sqrt(Qx * Qx + Qy * Qy + Qz * Qz + Qw * Qw);

        /// <summary>Rest rotation as a row-major 3x3, the same convention as <see cref="BoneRest"/>.</summary>
        public BoneRest ToMatrix()
        {
            float x = Qx, y = Qy, z = Qz, w = Qw;
            return BoneRest.FromRows(
                1 - 2 * (y * y + z * z), 2 * (x * y - z * w),     2 * (x * z + y * w),
                2 * (x * y + z * w),     1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
                2 * (x * z - y * w),     2 * (y * z + x * w),     1 - 2 * (x * x + y * y));
        }
    }

    /// <summary>A mesh's bones, in file order.
    ///
    /// ⭐ Parents always precede their children in the file — true of all 331 skeletons — so world
    /// transforms compose in ONE forward pass with no sort and no recursion. <see cref="IsWellFormed"/>
    /// is what entitles a caller to rely on that, and it is checked rather than assumed.</summary>
    public sealed class Skeleton
    {
        public Bone[] Bones = Array.Empty<Bone>();
        public int Count => Bones.Length;

        /// <summary>Exactly one root, every parent in range and ahead of its child. All 331 pass.</summary>
        public bool IsWellFormed()
        {
            int roots = 0;
            for (int i = 0; i < Bones.Length; i++)
            {
                int p = Bones[i].Parent;
                if (p < 0) { roots++; continue; }
                if (p >= i) return false;          // forward reference, or self
            }
            return roots == 1 || Bones.Length == 0;
        }

        /// <summary>Depth of each bone, root = 0. Requires <see cref="IsWellFormed"/>.</summary>
        public int[] Depths()
        {
            var d = new int[Bones.Length];
            for (int i = 0; i < Bones.Length; i++)
            {
                int p = Bones[i].Parent;
                d[i] = p < 0 ? 0 : d[p] + 1;
            }
            return d;
        }

        /// <summary>Rest pose in model space: each bone's transform composed through its parents.
        /// One forward pass, which the parents-precede-children property makes sufficient.</summary>
        public (BoneRest R, float X, float Y, float Z)[] RestWorld()
        {
            var outp = new (BoneRest R, float X, float Y, float Z)[Bones.Length];
            for (int i = 0; i < Bones.Length; i++)
            {
                var b = Bones[i];
                var local = b.ToMatrix();
                if (b.Parent < 0) { outp[i] = (local, b.Tx, b.Ty, b.Tz); continue; }
                var (pr, px, py, pz) = outp[b.Parent];
                outp[i] = (BoneRest.Multiply(pr, local),
                           px + pr.M00 * b.Tx + pr.M01 * b.Ty + pr.M02 * b.Tz,
                           py + pr.M10 * b.Tx + pr.M11 * b.Ty + pr.M12 * b.Tz,
                           pz + pr.M20 * b.Tx + pr.M21 * b.Ty + pr.M22 * b.Tz);
            }
            return outp;
        }
    }

    /// <summary>What a track drives. Read off the evaluators: the types differ far more in their
    /// DESTINATION than in their record shape.</summary>
    public enum TrackTarget
    {
        /// <summary>Not established.</summary>
        Unknown = 0,
        /// <summary>A bone in the skeleton.</summary>
        Bone,
        /// <summary>A scatter source point, which reaches vertices through <see cref="VertexBinding"/>.</summary>
        ScatterSource,
        /// <summary>A vertex in the work buffer, written directly, bypassing the scatter.</summary>
        Vertex,
    }

    /// <summary>
    /// One animation track. Two of the eight types are decoded:
    ///
    ///   type 8 — 2,295 tracks, always zero keyframes: a bone REST POSE, 3x3 GTE matrix.
    ///   type 6 — 1,091 tracks, 13,425 keyframes: time + translation + unit quaternion.
    ///
    /// The other six (836 tracks) are sized correctly from the game's jump table but their
    /// contents are not decoded; they arrive as <see cref="Raw"/> rather than being dropped.
    /// </summary>
    public sealed class AnimTrack
    {
        public byte Type;
        public int KeyCount;

        /// <summary>The raw u16 at +4 in the track header. NOT always a bone index -- see
        /// <see cref="BoneIndex"/>.</summary>
        public int Header4;

        /// <summary>The bone this track drives, or -1 where the header field is known not to
        /// address a bone.
        ///
        /// ⚠ THE FIELD AT +4 IS NOT A BONE INDEX FOR EVERY TYPE, which is easy to miss because it
        /// is a plausible small integer either way. Measured over the disc, how often it lands
        /// inside [0, nbones): types 6 and 8 at 100%, and types 2 and 4 at 0.0%, type 5 at 7.7%,
        /// type 3 at 36.1% -- chance, for a field of that width. So for the undecoded types it is
        /// an index into some other table, and calling it a bone would quietly drive the wrong bone.
        ///
        /// Types 0, 1 and 7 do stay in range on every track, but that is consistency, not proof, and
        /// the sample is 85 tracks; they are treated as unproven. Types 6 and 8 are proven by
        /// content: the type-8 matrix for index i reproduces bone i's own quaternion, 2,295 for
        /// 2,295, which a wrong index could not do.</summary>
        public int BoneIndex => Target == TrackTarget.Bone ? Header4 : -1;

        /// <summary>What <see cref="Header4"/> addresses, from the evaluator each type dispatches to.
        ///
        /// ⚠ A SHARED ROW IN THE SIZE TABLE IS NOT A SHARED MEANING, and this is the second place that
        /// bites. Types 2 and 4 have identical strides AND identical headers; so do 3 and 5. They write
        /// to DIFFERENT arrays -- 2/3 to the scatter sources at ARS+rec[0x30], 4/5 straight into the
        /// vertex buffer at ARS+rec[0x1c]. Treating a shared size as a shared target drives the wrong
        /// buffer and still looks plausible. (The first instance was types 1 and 8.)
        ///
        /// The index ranges corroborate in both directions rather than one loose bound: types 2 and 3
        /// land inside the source count 100% of the time and types 4 and 5 only 11.8% and 0.6%, while
        /// 4 and 5 land inside the vertex count 100%.
        ///
        /// Types 0, 1 and 7 index a bone on every track on the disc, but that is 85 tracks and
        /// consistency rather than proof; 6 and 8 are proven by the quaternion/matrix cross-check.</summary>
        public TrackTarget Target => Type switch
        {
            0 or 1 or 6 or 7 or 8 => TrackTarget.Bone,
            2 or 3                => TrackTarget.ScatterSource,
            4 or 5                => TrackTarget.Vertex,
            _                     => TrackTarget.Unknown,
        };

        /// <summary>The index <see cref="Header4"/> holds, or -1 when the target is unknown. Read it
        /// together with <see cref="Target"/> -- the number alone does not say which array it is in.</summary>
        public int TargetIndex => Target == TrackTarget.Unknown ? -1 : Header4;
        public BoneRest Rest;                              // Type == 8
        public AnimKey[] Keys = Array.Empty<AnimKey>();    // Type == 6
        public byte[] Raw = Array.Empty<byte>();           // everything else
        public bool IsDecoded => Type == 8 || Type == 6;

        /// <summary>The game's own blend weight for time <paramref name="t"/>: 0..4096 across the key
        /// that contains it. Straight from 0x8002da8c, integer division included, so it matches the
        /// hardware's rounding rather than being tidier than it. -1 when no key contains t.</summary>
        public int BlendWeight(int t)
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                int start = Keys[i].Time, dur = Keys[i].Duration;
                if (dur > 0 && t >= start && t < start + dur) return ((t - start) << 12) / dur;
            }
            return -1;
        }

        /// <summary>Interpolated pose at time t, the way the game does it: find the key whose span
        /// contains t, blend it toward the next by <see cref="BlendWeight"/>. The console applies that
        /// weight through the GTE's GPF/GPL opcodes, which is a LINEAR interpolation -- so this is an
        /// nlerp, not a slerp. The hardware has no slerp, and matching it means not being cleverer than
        /// it. Ends are held: before the first key and past the last, the nearest comes back unblended.</summary>
        public AnimKey Sample(int t)
        {
            if (Keys.Length == 0) return default;
            if (t <= Keys[0].Time) return Keys[0];
            for (int i = 0; i < Keys.Length - 1; i++)
            {
                int start = Keys[i].Time, dur = Keys[i].Duration;
                if (dur > 0 && t >= start && t < start + dur)
                    return AnimKey.Lerp(Keys[i], Keys[i + 1], ((t - start) << 12) / dur);
            }
            return Keys[^1];
        }

        /// <summary>Nearest key, no blending, for callers wanting the raw authored pose.</summary>
        public AnimKey SampleNearest(int t)
        {
            if (Keys.Length == 0) return default;
            if (t <= Keys[0].Time) return Keys[0];
            if (t >= Keys[^1].Time) return Keys[^1];
            for (int i = 1; i < Keys.Length; i++)
                if (Keys[i].Time >= t)
                    return (t - Keys[i - 1].Time) <= (Keys[i].Time - t) ? Keys[i - 1] : Keys[i];
            return Keys[^1];
        }
    }

    public static class MeshAnimation
    {
        /// <summary>Per-track size = Stride*count + Header, from the jump table at 0x800DDD78.
        /// ⚠ Types 1 and 8 share a SIZE but not a FORMAT: the archive's single type-1 track has
        /// non-orthonormal records. A shared row in a size table is not a shared layout.</summary>
        static readonly (int Stride, int Header)[] Sizes =
        {
            (36, 0x2C), (32, 0x28), (8, 0x10), (12, 0x14),
            (8, 0x10),  (12, 0x14), (20, 0x1C), (16, 0x18), (32, 0x28),
        };

        public static int TrackSize(byte type, int count) =>
            type < Sizes.Length ? Sizes[type].Stride * count + Sizes[type].Header
                                : throw new InvalidOperationException($"unknown track type {type}");

        static int Align4(int v) => (v + 3) & ~3;

        /// <summary>
        /// Walks bones then tracks, starting from where the face walk stopped.
        /// File order after the faces (folio.md §3.2):
        ///   align 4 | bone[n32] 40B | r12[n8b] 12B | r12b[m12] 12B | align 4 | track[ntracks] | u32 list
        /// </summary>
        public static bool TryParse(ReadOnlySpan<byte> d, int meshBase, int faceBytesEnd,
                                    int boneCount, int trackCount, int m12, int n8b,
                                    out List<AnimTrack> tracks, out Skeleton skeleton,
                                    out VertexBinding binding, out int end, out string error)
        {
            tracks = new List<AnimTrack>(trackCount);
            skeleton = new Skeleton();
            binding = new VertexBinding();
            error = null;
            int p = Align4(faceBytesEnd);

            if (p + boneCount * 40 > d.Length) { end = p; error = "bone table past the end"; return false; }
            var bones = new Bone[boneCount];
            for (int i = 0; i < boneCount; i++) bones[i] = new Bone(d.Slice(p + i * 40, 40));
            skeleton.Bones = bones;

            if (!VertexBinding.TryParse(d, p + boneCount * 40, n8b, m12, out binding, out error))
            { end = p; return false; }

            p += boneCount * 40 + n8b * 12 + m12 * 12;
            p = Align4(p);
            end = p;

            for (int i = 0; i < trackCount; i++)
            {
                if (p + 8 > d.Length) { error = "track header past the end"; return false; }
                byte type = d[p];
                if (type >= Sizes.Length) { error = $"unknown track type {type}"; return false; }
                int count = BitConverter.ToUInt16(d.Slice(p + 6, 2));
                var (stride, hdr) = Sizes[type];        // one source for the layout, not two
                int size = stride * count + hdr;
                if (p + size > d.Length) { error = "track body past the end"; return false; }

                var tr = new AnimTrack
                {
                    Type = type,
                    Header4 = BitConverter.ToUInt16(d.Slice(p + 4, 2)),
                    KeyCount = count,
                };
                if (type == 8) tr.Rest = new BoneRest(d.Slice(p + 8, 18));
                else if (type == 6)
                {
                    var keys = new AnimKey[count];
                    for (int k = 0; k < count; k++) keys[k] = new AnimKey(d.Slice(p + hdr + k * stride, stride));
                    tr.Keys = keys;
                }
                else tr.Raw = d.Slice(p, size).ToArray();

                tracks.Add(tr);
                p += size;
            }
            end = p;
            return true;
        }
    }
}
