using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>A bone's rest orientation. Row-major 3x3, already divided by the GTE 4096 scale.</summary>
    public readonly struct RestPose
    {
        public readonly float M00, M01, M02, M10, M11, M12, M20, M21, M22;
        public RestPose(ReadOnlySpan<short> m)
        {
            const float S = 1f / 4096f;
            M00 = m[0] * S; M01 = m[1] * S; M02 = m[2] * S;
            M10 = m[3] * S; M11 = m[4] * S; M12 = m[5] * S;
            M20 = m[6] * S; M21 = m[7] * S; M22 = m[8] * S;
        }
        /// <summary>True when every row is unit length. 2295 of 2295 rest poses on the disc pass at tol 0.045.</summary>
        public bool IsOrthonormal(float tol = 0.045f)
        {
            static float L(float a, float b, float c) => MathF.Sqrt(a * a + b * b + c * c);
            return MathF.Abs(L(M00, M01, M02) - 1f) < tol
                && MathF.Abs(L(M10, M11, M12) - 1f) < tol
                && MathF.Abs(L(M20, M21, M22) - 1f) < tol;
        }
    }

    /// <summary>One keyframe of a type-6 track: a time, a translation and a unit quaternion.</summary>
    public readonly struct AnimKeyframe
    {
        public readonly short Time;       // non-decreasing within a track (1023/1023 tracks on the disc)
        public readonly short Field1;     // 0..380. Bounded but NOT identified - do not rely on it.
        public readonly short Tx, Ty, Tz; // model-space translation, raw units
        public readonly float Qx, Qy, Qz, Qw;

        public AnimKeyframe(ReadOnlySpan<short> v)
        {
            const float S = 1f / 4096f;
            Time = v[0]; Field1 = v[1];
            Tx = v[2]; Ty = v[3]; Tz = v[4];
            // v[5] is zero in every record on the disc.
            Qx = v[6] * S; Qy = v[7] * S; Qz = v[8] * S; Qw = v[9] * S;
        }
        public float QuatLength() => MathF.Sqrt(Qx * Qx + Qy * Qy + Qz * Qz + Qw * Qw);
    }

    /// <summary>
    /// One animation track. Type 8 carries a rest pose and no keyframes; type 6 carries keyframes.
    /// The other six types are sized correctly but their contents are not decoded.
    /// </summary>
    public sealed class AnimTrack
    {
        public byte Type;
        public int BoneIndex;
        public RestPose Rest;                 // valid when Type == 8
        public AnimKeyframe[] Keyframes = Array.Empty<AnimKeyframe>();   // valid when Type == 6
        public byte[] RawPayload = Array.Empty<byte>();                  // everything else, undecoded
        public bool IsDecoded => Type == 8 || Type == 6;
    }

    public static class AnimationReader
    {
        /// <summary>
        /// Per-track size = Stride*count + Header. From the game's jump table at 0x800DDD78.
        /// NOTE types 1 and 8 share a size but NOT a format: the archive's single type-1 track
        /// has non-orthonormal records. Size table and content format are different questions.
        /// </summary>
        static readonly (int Stride, int Header)[] Sizes = new (int, int)[9]
        {
            (36, 0x2C), // 0
            (32, 0x28), // 1  - same size as 8, different contents
            ( 8, 0x10), // 2
            (12, 0x14), // 3  - 360 tracks, the largest undecoded type
            ( 8, 0x10), // 4
            (12, 0x14), // 5
            (20, 0x1C), // 6  - keyframes
            (16, 0x18), // 7
            (32, 0x28), // 8  - rest pose
        };

        public static int TrackSize(byte type, int count)
        {
            if (type >= Sizes.Length) throw new InvalidOperationException($"unknown track type {type}");
            var (stride, header) = Sizes[type];
            return stride * count + header;
        }

        /// <summary>
        /// Reads <paramref name="trackCount"/> tracks starting at <paramref name="offset"/>.
        /// Advances <paramref name="offset"/> past the last track so the caller can check it
        /// lands exactly on the trailing index list - that check is the parser's oracle.
        /// </summary>
        public static List<AnimTrack> Read(ReadOnlySpan<byte> data, ref int offset, int trackCount)
        {
            var tracks = new List<AnimTrack>(trackCount);
            for (int i = 0; i < trackCount; i++)
            {
                byte type = data[offset];
                int bone = BitConverter.ToUInt16(data.Slice(offset + 4, 2));
                int count = BitConverter.ToUInt16(data.Slice(offset + 6, 2));
                int size = TrackSize(type, count);
                var track = new AnimTrack { Type = type, BoneIndex = bone };

                if (type == 8)
                {
                    // 8-byte header, then a 32-byte record whose first 18 bytes are the matrix.
                    track.Rest = new RestPose(AsShorts(data.Slice(offset + 8, 18)));
                }
                else if (type == 6)
                {
                    // 8-byte header, a 20-byte preamble record, then count x 20-byte keyframes.
                    var kf = new AnimKeyframe[count];
                    for (int k = 0; k < count; k++)
                        kf[k] = new AnimKeyframe(AsShorts(data.Slice(offset + 0x1C + k * 20, 20)));
                    track.Keyframes = kf;
                }
                else
                {
                    track.RawPayload = data.Slice(offset, size).ToArray();
                }

                tracks.Add(track);
                offset += size;
            }
            return tracks;
        }

        static short[] AsShorts(ReadOnlySpan<byte> b)
        {
            var s = new short[b.Length / 2];
            for (int i = 0; i < s.Length; i++) s[i] = BitConverter.ToInt16(b.Slice(i * 2, 2));
            return s;
        }
    }
}
