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

        /// <summary>A keyframe from a bare 16-byte {vector, pad, quaternion} block, with the timing
        /// supplied by the caller. The untimed types (1 and 7) store exactly this block and nothing
        /// else -- their evaluator indexes the record array by the clock, so key k IS tick k, and
        /// giving it Time=k, Duration=1 makes the shared <see cref="Sample"/> path reproduce that
        /// indexing without a second code path.</summary>
        public static AnimKey FromBlock(ReadOnlySpan<byte> d, short time, short dur)
        {
            const float S = 1f / 4096f;
            static short H(ReadOnlySpan<byte> b, int i) => BitConverter.ToInt16(b.Slice(i * 2, 2));
            return new AnimKey(time, dur, H(d, 0), H(d, 1), H(d, 2),
                               H(d, 4) * S, H(d, 5) * S, H(d, 6) * S, H(d, 7) * S);
        }

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
        /// <summary>This bone's slice of the skin table: how many entries, and where they start.
        ///
        /// ⚠ Catalogued as "unidentified" until the code was read. The game loads this bone's matrix into
        /// the GTE and walks exactly this range, transforming each entry's bone-space position by the
        /// matrix and accumulating it into the entry's vertex. It is the link from bones to geometry, and
        /// it was sitting in the first four bytes of a record whose other fields I had already named.</summary>
        public readonly ushort SkinCount, SkinStart;

        public readonly short Parent;               // -1 at the root; always < own index
        public readonly float Qx, Qy, Qz, Qw;       // rest rotation
        public readonly short Tx, Ty, Tz;           // rest translation, file units
        public readonly short Sx, Sy, Sz;           // scale, 4096 = 1.0

        public Bone(ReadOnlySpan<byte> d)
        {
            const float S = 1f / 4096f;
            static short H(ReadOnlySpan<byte> b, int i) => BitConverter.ToInt16(b.Slice(i * 2, 2));
            SkinCount = (ushort)H(d, 0);
            SkinStart = (ushort)H(d, 1);
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

    /// <summary>A bone's SCALE and the rotation that orients the scale axes -- the second 16-byte block
    /// that types 0 and 1 carry beyond types 6 and 7.
    ///
    /// ⭐ READ OFF THE CODE, NOT THE VALUES. The evaluator hands the whole block to 0x8002be58 with
    /// flags 7, and that routine switches on the bits:
    ///   bit 2 -> load the GTE identity from 0x800DDD58 (4096 down the diagonal), then overwrite the
    ///            DIAGONAL -- stores at +0x00, +0x08, +0x10 of a 3x3 of s16 -- with the three halfwords
    ///            at this block's +0x00. That is what makes them a SCALE: they are written onto a
    ///            diagonal, not multiplied as a vector.
    ///   bit 1 -> this block's quaternion becomes a rotation matrix (0x80011400), multiplied with it.
    ///   and the FIRST block's quaternion becomes a rotation matrix the same way.
    /// The bone's matrix is then R(quatA) * R(quatB) * diag(scale), composed through the parent's matrix
    /// at bone+6 and stored to the bone's own 32-byte slot.
    ///
    /// So the second block exists to express what ONE quaternion cannot: a scale with its own
    /// orientation, i.e. a general transform with shear. A single rotation plus a diagonal can only
    /// scale along the bone's own axes.
    ///
    /// The file agrees, independently of the disassembly. Across the 696 type-0 records:
    ///   - the three components are EXACTLY EQUAL on 26.4%, against 0.6% for a column-shuffled control.
    ///     A uniform scale is the commonest thing anyone animates; a translation with x==y==z is a
    ///     coincidence, and the first block is uniform on 0.0%.
    ///   - no component is ever exactly 0 -- a zero scale collapses the bone -- while the first block's
    ///     vector is 0 on 9.3% of components, which is an ordinary translation.
    ///   - magnitudes cluster on 4096 (=1.0) and never come near the first block's range: median |v| is
    ///     23673 here and 36 there.
    /// Whole tracks read as scale animations once seen this way: one sub-entry runs (3576,3576,3576),
    /// (3371,3371,3371), (2841,2841,2841) -- something shrinking.
    ///
    /// ⚠ I NEARLY TALKED MYSELF OUT OF THIS WITH A REAL NUMBER. The first record I read held
    /// (4096,4096,4096), exactly 1.0, which reads beautifully as a scale -- and I then measured that
    /// value on only 15.7% of records with "the commonest" being (24945,-23673,24945), wrote that one
    /// sample had told a tidy story the population did not support, and left the field unnamed. Both
    /// numbers were correct and the conclusion was wrong: (24945,-23673,24945) occurs in exactly ONE
    /// sub-entry of the 32 that carry type-0 tracks, and dominates the RECORD count only because that
    /// one mesh has hundreds of keyframes. 23 of the 32 carry the unit scale. **Counting records
    /// weighted my statistic by keyframe count, so one long animation outvoted twenty-two meshes.**
    /// Count the population you are actually generalising over.
    ///
    /// The degenerate cases now read as what they are: 37 records carry unit scale AND an identity
    /// quaternion here, making the block a no-op and the keyframe equivalent to a type-6 one; 191 more
    /// carry the identity quaternion alone, i.e. scale along the bone's own axes.</summary>
    public readonly struct BoneScale
    {
        /// <summary>The scale, in the GTE's 1.12 fixed point: 4096 is 1.0. Written onto a matrix
        /// diagonal by the evaluator, which is what identifies it.</summary>
        public readonly short Sx, Sy, Sz;

        /// <summary>The 4th slot, which in the FIRST block of every type is a zero pad -- 20,350
        /// records, no exceptions -- and in this second block is not. Note the evaluator reads only
        /// THREE halfwords for the diagonal, so whatever this is, it is not part of the scale.
        ///
        /// ⚠ THIS SHIPPED DOCUMENTED AS "the pad, zero as in every other 8-byte vector on the disc".
        /// It is zero on 689 of 696 type-0 records, so every sample I looked at agreed with that, but
        /// the other 7 hold 30707 and all 108 type-1 records hold 30706 -- 0x77F3 and 0x77F2, two
        /// consecutive values, which is the shape of an id or a handle rather than padding. Kept
        /// rather than dropped, because a field discarded as padding cannot later be found to matter.</summary>
        public readonly short Slot3;
        public readonly float Qx, Qy, Qz, Qw;
        public BoneScale(ReadOnlySpan<byte> d)
        {
            const float S = 1f / 4096f;
            static short H(ReadOnlySpan<byte> b, int i) => BitConverter.ToInt16(b.Slice(i * 2, 2));
            Sx = H(d, 0); Sy = H(d, 1); Sz = H(d, 2); Slot3 = H(d, 3);
            Qx = H(d, 4) * S; Qy = H(d, 5) * S; Qz = H(d, 6) * S; Qw = H(d, 7) * S;
        }
        public float QuatLength() => MathF.Sqrt(Qx * Qx + Qy * Qy + Qz * Qz + Qw * Qw);
    }

    /// <summary>A position sample. Types 2/3/4/5 all carry the same 8-byte payload -- the PSX's own
    /// {s16 x, s16 y, s16 z, s16 pad} vertex, with the pad zero on all 3,973 records on the disc.
    /// Timed types put a time and duration in front of it; untimed types do not.</summary>
    public readonly struct PositionKey
    {
        public readonly short Time, Duration;    // both 0 on an untimed type
        public readonly short X, Y, Z;
        public PositionKey(short time, short dur, short x, short y, short z)
        { Time = time; Duration = dur; X = x; Y = y; Z = z; }
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
    /// One animation track. All nine types are now decoded, and they form one small family rather
    /// than nine unrelated formats. The unit of the format is a 16-byte {vector, pad, unit quaternion}
    /// BLOCK; a type is then just how many blocks it carries and whether a 4-byte {time, duration}
    /// header sits in front:
    ///
    ///              | 1 block                  | 2 blocks
    ///     timed    | type 6  (20B, 1,091 trk) | type 0  (36B, 48 trk)
    ///     untimed  | type 7  (16B, 35 trk)    | type 1  (32B, 1 trk)
    ///
    /// with the position types the same idea over an 8-byte {x,y,z,pad} payload -- timed 3 and 5,
    /// untimed 2 and 4 -- and type 8 the odd one out: a bone REST POSE as a 3x3 GTE matrix, 2,295
    /// tracks and always zero keyframes.
    ///
    /// ⚠ THE TABLE ABOVE IS A SHAPE, NOT A MEANING. It says where the bytes are, and for the first
    /// block of each type that reading is earned (see <see cref="BoneScale"/> for the test). What the
    /// SECOND block is FOR is still unknown, and type 1's second block cannot be tested at all: the
    /// disc holds one type-1 track, and in it that block is near-constant.
    /// </summary>
    public sealed class AnimTrack
    {
        public byte Type;
        public int KeyCount;

        /// <summary>Where this track starts in the (expanded) entry, so a caller can reach the bytes
        /// this parser does not turn into keys -- notably the key-sized slot AFTER the last key. See
        /// <see cref="TrailingSlotOffset"/>.</summary>
        public int FileOffset;

        /// <summary>Offset of the key-sized record that follows the last key, or -1 for types without
        /// keys. The game's handlers read key[COUNT] there (type 0 at 0x8002cdac, type 6 at
        /// 0x8002d95c), and the track's own size leaves exactly one key's worth of room for it: type 6
        /// is stride 20 with header 0x1C and keys starting at +8, so 20*count + 28 - (8 + 20*count) =
        /// 20 bytes spare; type 0 is 36 with header 0x2C, leaving 36. That is not slack, it is a
        /// record.</summary>
        /// <summary>Offset of the LAST slot (index KeyCount). Before the +8 fix this was the last slot
        /// the parse read while slot 0 went unread; now every slot 0..KeyCount is a key, so nothing is
        /// actually "trailing" and this is just the final key. Kept for diagnostics.</summary>
        public int TrailingSlotOffset => KeyCount < 0 ? -1 : FileOffset + 8 + KeySizes(Type) * KeyCount;

        /// <summary>Bytes per key for the types that have them, 0 otherwise.</summary>
        public static int KeySizes(byte type) => type switch { 0 => 36, 1 => 32, 6 => 20, 7 => 16, _ => 0 };

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
        public PositionKey[] Positions = Array.Empty<PositionKey>();   // Types 2,3,4,5
        /// <summary>Per-keyframe scale + scale-axis rotation. Types 0 and 1 only.</summary>
        public BoneScale[] Scales = Array.Empty<BoneScale>();
        public byte[] Raw = Array.Empty<byte>();           // everything else

        /// <summary>Whether the records carry their own timing.
        ///
        /// Measured by the same chain that named the duration: types 0, 3, 5 and 6 satisfy
        /// `Time + Duration == next.Time` on 100% of consecutive pairs (646, 2379, 1079 and 12334 of
        /// them), while types 1, 2 and 4 score 0.0% and type 7 scores 6.4%, which is chance.
        ///
        /// An untimed type is one sample per tick: the evaluator indexes the record array by the clock
        /// directly, so there is nothing to interpolate and nothing to look up.</summary>
        public bool IsTimed => Type is 0 or 3 or 5 or 6;
        public bool IsDecoded => Type is 0 or 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8;

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
        /// non-orthonormal records -- it is two 16-byte blocks, where type 8 is a 3x3 matrix.
        /// A shared row in a size table is not a shared layout. (Types 2/4 and 3/5 are the same
        /// trap again, and there the shared size hides a different TARGET rather than a different
        /// layout -- see <see cref="AnimTrack.Target"/>.)</summary>
        /// <summary>Where a track's records start: 8 bytes in, ALWAYS.
        ///
        /// ⭐ Every type's Header below is exactly 8 + Stride, all nine of them, because the 8-byte
        /// header is followed by count + 1 records -- not count. The game's handlers read from track+8
        /// and bound the search at key[count] (type 0 at 0x8002cdac, type 6 at 0x8002d95c), so the
        /// extra record is the LAST one, and reading from +Header silently dropped the FIRST.
        ///
        /// ⭐ That dropped record is the loop closer: in every track of the language advisor it carries
        /// time 0 and the same rotation as the key at t=126, so the clip begins and ends on one pose and
        /// loops seamlessly. Without it the clip appeared to start at t=25 with nothing covering the
        /// head of the timeline, which is why 200 of the disc's 251 animated sub-meshes snapped at their
        /// loop and the console -- measured across 22 consecutive frames -- does not.</summary>
        /// ⭐ CONFIRMED FOR ALL NINE TYPES, not just the two I had measured. Every handler in the jump
        /// table at 0x800DDDA0 computes its record base as $s4 + 8:
        ///   type 0 0x8002ccfc, 1 0x8002d248, 2 0x8002d4a8, 3 0x8002d4e8, 4 0x8002d6f8,
        ///   5 0x8002d738, 6 0x8002d8ec, 7 0x8002dd24, 8 0x8002df5c.
        /// And the first halfword each one reads splits exactly along the timed/untimed line this
        /// project derived independently: types 0, 3, 5 and 6 read the record's TIME at 8($s4); types
        /// 2, 4, 7 and 8 read 4($s4) instead, having no time field to read. That agreement was not
        /// designed for -- it is two separate decodes landing on the same partition.
        const int KeyBase = 8;   // ADOPTED by the parse (2026-09-19): records start here, not at Header.

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

                // Slots run from +8, not from the type's header size: every one of the nine handlers
                // computes its record base as $s4 + 8, and Header == 8 + Stride for all nine types, so a
                // track holds count+1 slots; reading `count` of them from p+hdr skipped the first.
                int n = count + 1;
                int kbase = p + KeyBase;

                var tr = new AnimTrack
                {
                    Type = type,
                    Header4 = BitConverter.ToUInt16(d.Slice(p + 4, 2)),
                    KeyCount = count,
                    FileOffset = p,
                };
                if (type == 8) tr.Rest = new BoneRest(d.Slice(p + 8, 18));
                else if (type == 2 || type == 4)
                {
                    // Untimed: one 8-byte position per tick, the evaluator indexing by the clock itself.
                    var ps = new PositionKey[n];
                    for (int k = 0; k < n; k++)
                    {
                        int o = kbase + k * stride;
                        ps[k] = new PositionKey(0, 0, BitConverter.ToInt16(d.Slice(o, 2)),
                                                BitConverter.ToInt16(d.Slice(o + 2, 2)),
                                                BitConverter.ToInt16(d.Slice(o + 4, 2)));
                    }
                    tr.Positions = ps;
                }
                else if (type == 3 || type == 5)
                {
                    // Timed: 2+2 of timing in front of the same payload.
                    var ps = new PositionKey[n];
                    for (int k = 0; k < n; k++)
                    {
                        int o = kbase + k * stride;
                        ps[k] = new PositionKey(BitConverter.ToInt16(d.Slice(o, 2)),
                                                BitConverter.ToInt16(d.Slice(o + 2, 2)),
                                                BitConverter.ToInt16(d.Slice(o + 4, 2)),
                                                BitConverter.ToInt16(d.Slice(o + 6, 2)),
                                                BitConverter.ToInt16(d.Slice(o + 8, 2)));
                    }
                    tr.Positions = ps;
                }
                else if (type == 0)
                {
                    // {time, dur, vecA, pad, quatA, vecB, pad, quatB} -- the first 20 bytes are exactly a
                    // type-6 keyframe, so AnimKey reads them unchanged.
                    var keys = new AnimKey[n];
                    var pb = new BoneScale[n];
                    for (int k = 0; k < n; k++)
                    {
                        int o = kbase + k * stride;
                        keys[k] = new AnimKey(d.Slice(o, 20));
                        pb[k] = new BoneScale(d.Slice(o + 20, 16));
                    }
                    tr.Keys = keys;
                    tr.Scales = pb;
                }
                else if (type == 7)
                {
                    // Untimed, one 16-byte {vector, pad, quaternion} block per tick -- a type-6
                    // keyframe with the 4-byte timing header removed.
                    var keys = new AnimKey[n];
                    for (int k = 0; k < n; k++)
                        keys[k] = AnimKey.FromBlock(d.Slice(kbase + k * stride, 16), (short)k, 1);
                    tr.Keys = keys;
                }
                else if (type == 1)
                {
                    // Untimed, TWO blocks per tick: type 0 is to type 6 as type 1 is to type 7.
                    var keys = new AnimKey[n];
                    var pb = new BoneScale[n];
                    for (int k = 0; k < n; k++)
                    {
                        int o = kbase + k * stride;
                        keys[k] = AnimKey.FromBlock(d.Slice(o, 16), (short)k, 1);
                        pb[k] = new BoneScale(d.Slice(o + 16, 16));
                    }
                    tr.Keys = keys;
                    tr.Scales = pb;
                }
                else if (type == 6)
                {
                    var keys = new AnimKey[n];
                    for (int k = 0; k < n; k++) keys[k] = new AnimKey(d.Slice(kbase + k * stride, stride));
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
