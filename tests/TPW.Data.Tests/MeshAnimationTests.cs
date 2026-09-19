using System;
using System.Collections.Generic;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    /// <summary>Animation tracks. Expected values are worked by hand from the byte layout.</summary>
    public class MeshAnimationTests
    {
        static void U16(List<byte> b, int v) { b.Add((byte)v); b.Add((byte)(v >> 8)); }
        static void S16(List<byte> b, int v) => U16(b, v & 0xFFFF);

        /// <summary>8-byte track header: type, pad, u16, u16 bone, u16 count.</summary>
        static void Header(List<byte> b, int type, int bone, int count)
        { b.Add((byte)type); b.Add(0); U16(b, 0); U16(b, bone); U16(b, count); }

        // ⭐ The GTE scale is 4096, so an identity rest pose is 4096 down the diagonal of a row-major
        // 3x3. All 2,295 type-8 rest poses on the disc are orthonormal; that is the property worth
        // asserting, because a wrong stride still yields nine plausible-looking s16.
        [Fact]
        public void ATypeEightTrackIsABoneRestPose()
        {
            var b = new List<byte>();
            Header(b, 8, bone: 7, count: 0);
            foreach (var v in new[] { 4096, 0, 0, 0, 4096, 0, 0, 0, 4096 }) S16(b, v);
            b.AddRange(new byte[32 - 18]);                     // rest of the 32-byte record

            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0,
                                               out var tracks, out int end, out string err), err);
            var t = Assert.Single(tracks);
            Assert.Equal(8, t.Type);
            Assert.Equal(7, t.BoneIndex);
            Assert.Empty(t.Keys);
            Assert.True(t.Rest.IsOrthonormal());
            Assert.Equal(1f, t.Rest.M00, 4);
            Assert.Equal(1f, t.Rest.M11, 4);
            Assert.Equal(1f, t.Rest.M22, 4);
            Assert.Equal(0f, t.Rest.M01, 4);
            Assert.Equal(40, end);                             // 32*0 + 0x28
        }

        // REJECTS a matrix read at the wrong offset: a pose whose rows are not unit length is not a
        // rotation, and IsOrthonormal must say so rather than waving it through.
        [Fact]
        public void ARestPoseThatIsNotARotationIsNotOrthonormal()
        {
            var b = new List<byte>();
            Header(b, 8, bone: 0, count: 0);
            foreach (var v in new[] { 1000, 0, 0, 0, 4096, 0, 0, 0, 4096 }) S16(b, v);   // first row short
            b.AddRange(new byte[32 - 18]);

            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0, out var tracks, out _, out _));
            Assert.False(tracks[0].Rest.IsOrthonormal());
        }

        // ⭐ Worked by hand. A type-6 record is 20 bytes: time, an unidentified field, three translation
        // components, a zero, then a unit quaternion at 4096 scale. 2048 = 0.5, and
        // 0.5^2 * 4 = 1, so (2048,2048,2048,2048) is a unit quaternion — a cheap exact case.
        [Fact]
        public void ATypeSixTrackCarriesTimeTranslationAndAUnitQuaternion()
        {
            var b = new List<byte>();
            Header(b, 6, bone: 3, count: 2);
            b.AddRange(new byte[20]);                                            // preamble record
            foreach (var v in new[] { 10, 1, -5, 6, 7, 0, 2048, 2048, 2048, 2048 }) S16(b, v);
            foreach (var v in new[] { 25, 2, -5, 6, 7, 0, 0, 0, 0, 4096 }) S16(b, v);

            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0,
                                               out var tracks, out int end, out string err), err);
            var t = Assert.Single(tracks);
            Assert.Equal(6, t.Type);
            Assert.Equal(3, t.BoneIndex);
            Assert.Equal(2, t.Keys.Length);

            Assert.Equal(10, t.Keys[0].Time);
            Assert.Equal(-5, t.Keys[0].Tx);
            Assert.Equal(6, t.Keys[0].Ty);
            Assert.Equal(7, t.Keys[0].Tz);
            Assert.Equal(0.5f, t.Keys[0].Qx, 4);
            Assert.Equal(1f, t.Keys[0].QuatLength(), 3);
            Assert.Equal(1f, t.Keys[1].QuatLength(), 3);
            Assert.Equal(20 * 2 + 0x1C, end);
        }

        // Sampling holds at both ends rather than extrapolating, and picks the nearer key between.
        [Fact]
        public void SamplingHoldsTheEndsAndPicksTheNearerKey()
        {
            var b = new List<byte>();
            Header(b, 6, bone: 0, count: 2);
            b.AddRange(new byte[20]);
            foreach (var v in new[] { 10, 0, 100, 0, 0, 0, 0, 0, 0, 4096 }) S16(b, v);
            foreach (var v in new[] { 30, 0, 200, 0, 0, 0, 0, 0, 0, 4096 }) S16(b, v);

            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0, out var tracks, out _, out _));
            var t = tracks[0];
            Assert.Equal(100, t.SampleNearest(0).Tx);     // before the first key
            Assert.Equal(100, t.SampleNearest(14).Tx);    // nearer the first
            Assert.Equal(200, t.SampleNearest(26).Tx);    // nearer the second
            Assert.Equal(200, t.SampleNearest(999).Tx);   // after the last
        }

        // ⚠ Types 1 and 8 share a SIZE row in the game's jump table but not a format: the archive's
        // single type-1 track has non-orthonormal records. A reader that treats them alike will read
        // nine arbitrary s16 as a rotation and produce a silently wrong pose.
        // REJECTS merging them: a type-1 track must not come back claiming a decoded rest pose.
        [Fact]
        public void TypeOneIsNotTreatedAsTypeEightDespiteTheSharedSize()
        {
            Assert.Equal(MeshAnimation.TrackSize(8, 3), MeshAnimation.TrackSize(1, 3));

            var b = new List<byte>();
            Header(b, 1, bone: 0, count: 0);
            b.AddRange(new byte[32]);

            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0, out var tracks, out _, out _));
            Assert.Equal(1, tracks[0].Type);
            Assert.False(tracks[0].IsDecoded);
            Assert.Equal(32 + 8, tracks[0].Raw.Length);     // handed back whole, not dropped
        }

        // Sizes come from the jump table at 0x800DDD78. Checked by hand so a typo in the table fails here
        // rather than as a mis-walk thousands of bytes later.
        [Theory]
        [InlineData(0, 2, 36 * 2 + 0x2C)]
        [InlineData(2, 5, 8 * 5 + 0x10)]
        [InlineData(3, 4, 12 * 4 + 0x14)]
        [InlineData(6, 7, 20 * 7 + 0x1C)]
        [InlineData(7, 1, 16 * 1 + 0x18)]
        public void TrackSizesMatchTheGamesJumpTable(int type, int count, int expected)
            => Assert.Equal(expected, MeshAnimation.TrackSize((byte)type, count));

        // REJECTS a truncated buffer: a track whose body runs past the end must fail, not read garbage.
        [Fact]
        public void ATrackBodyPastTheEndIsRefused()
        {
            var b = new List<byte>();
            Header(b, 6, bone: 0, count: 50);      // claims 50 keys, supplies none
            Assert.False(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0, out _, out _, out string err));
            Assert.Contains("past the end", err);
        }

        // REJECTS an unknown type: sizes are unknown past 8, so the walk cannot continue safely.
        [Fact]
        public void AnUnknownTrackTypeIsRefused()
        {
            var b = new List<byte>();
            Header(b, 9, bone: 0, count: 0);
            b.AddRange(new byte[32]);
            Assert.False(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0, out _, out _, out string err));
            Assert.Contains("unknown track type", err);
        }
    }
}
