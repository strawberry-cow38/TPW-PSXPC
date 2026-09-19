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
        public readonly short Field1;           // 0..380. Bounded, NOT identified. Do not rely on it.
        public readonly short Tx, Ty, Tz;       // translation, the file's own s16 units
        public readonly float Qx, Qy, Qz, Qw;   // unit quaternion
        public AnimKey(ReadOnlySpan<byte> d)
        {
            const float S = 1f / 4096f;
            static short H(ReadOnlySpan<byte> b, int i) => BitConverter.ToInt16(b.Slice(i * 2, 2));
            Time = H(d, 0); Field1 = H(d, 1);
            Tx = H(d, 2); Ty = H(d, 3); Tz = H(d, 4);
            // H(d,5) is zero in all 13,425 keyframes on the disc.
            Qx = H(d, 6) * S; Qy = H(d, 7) * S; Qz = H(d, 8) * S; Qw = H(d, 9) * S;
        }
        public float QuatLength() => MathF.Sqrt(Qx * Qx + Qy * Qy + Qz * Qz + Qw * Qw);
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
        public int BoneIndex;
        public int KeyCount;
        public BoneRest Rest;                              // Type == 8
        public AnimKey[] Keys = Array.Empty<AnimKey>();    // Type == 6
        public byte[] Raw = Array.Empty<byte>();           // everything else
        public bool IsDecoded => Type == 8 || Type == 6;

        /// <summary>Pose at time t, holding the ends. Nearest-key; the game interpolates
        /// (0x8002CBC4 composes matrices per frame) but the interpolation is not decoded,
        /// so this deliberately does not pretend to smooth.</summary>
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
                                    out List<AnimTrack> tracks, out int end, out string error)
        {
            tracks = new List<AnimTrack>(trackCount);
            error = null;
            int p = Align4(faceBytesEnd);
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
                    BoneIndex = BitConverter.ToUInt16(d.Slice(p + 4, 2)),
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
