using System;
using System.Collections.Generic;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    /// <summary>Evaluating a mesh at a time. Expected values worked by hand.</summary>
    public class MeshPoseTests
    {
        static void U16(List<byte> b, int v) { b.Add((byte)v); b.Add((byte)(v >> 8)); }
        static void S16(List<byte> b, int v) => U16(b, v & 0xFFFF);

        static AnimTrack PosTrack(int type, int index, params (int t, int d, int x, int y, int z)[] recs)
        {
            var b = new List<byte>();
            // count is the LAST slot index, and slots start at +8 - so recs.Length records
            // laid straight after the 8-byte header are slots 0..recs.Length-1.
            b.Add((byte)type); b.Add(0); U16(b, 0); U16(b, index); U16(b, recs.Length - 1);
            foreach (var r in recs)
            {
                if (type is 3 or 5) { S16(b, r.t); S16(b, r.d); }
                S16(b, r.x); S16(b, r.y); S16(b, r.z); S16(b, 0);
            }
            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0,
                                               out var tr, out _, out _, out _, out string e), e);
            return tr[0];
        }

        // An untimed track has no timing to look up: the clock IS the index. Ends are held.
        [Theory]
        [InlineData(0, 10)]
        [InlineData(1, 20)]
        [InlineData(2, 30)]
        [InlineData(-5, 10)]     // before the start
        [InlineData(99, 30)]     // past the end
        public void AnUntimedTrackIsIndexedByTheClock(int time, int expectedX)
        {
            var t = PosTrack(2, 0, (0, 0, 10, 0, 0), (0, 0, 20, 0, 0), (0, 0, 30, 0, 0));
            Assert.Equal(expectedX, MeshPose.SampleAt(t, time).X);
        }

        // A timed track interpolates with the game's integer weight. Span [10,30): halfway is 20.
        [Theory]
        [InlineData(5, 100)]     // before the first key, held
        [InlineData(10, 100)]
        [InlineData(20, 200)]    // halfway between 100 and 300
        [InlineData(30, 300)]
        [InlineData(99, 300)]    // past the last, held
        public void ATimedTrackInterpolatesBetweenItsKeys(int time, int expectedX)
        {
            var t = PosTrack(3, 0, (10, 20, 100, 0, 0), (30, 10, 300, 0, 0));
            Assert.Equal(expectedX, MeshPose.SampleAt(t, time).X);
        }

        // ⭐ AN UNTIMED TRACK'S LENGTH IS ITS SAMPLE COUNT, AND TYPES 7 AND 1 KEEP THEIR SAMPLES IN
        // Keys, NOT Positions. AnimationLength only consulted Positions, so a mesh animated solely by a
        // type-7 track measured as length 0 and was treated as not animated at all -- it would never
        // play. REJECTS a reader that looks in one array and calls the absence of the other a zero.
        [Fact]
        public void AnUntimedKeyTrackContributesItsLengthNotZero()
        {
            var b = new List<byte>();
            b.Add(7); b.Add(0); U16(b, 0); U16(b, 0); U16(b, 2);   // type 7, count == last slot index
            for (int k = 0; k < 3; k++)                            // three 16-byte slots from +8
                foreach (var v in new[] { k, 0, 0, 0, 0, 0, 0, 4096 }) S16(b, v);

            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0,
                                               out var tr, out _, out _, out _, out string e), e);
            var mesh = new Mesh { Tracks = tr };
            Assert.Equal(3, tr[0].Keys.Length);
            Assert.Equal(3, MeshPose.AnimationLength(mesh));   // was 0: Positions is empty for type 7
        }

        // ⭐ THE REFLECTION IS NOT THE SAME OPERATION ON A ROTATION AS ON A POSITION, and writing the
        // position rule for both is the obvious mistake. A position mirrors z; a rotation conjugated by
        // the same mirror has its axis reflected AND its angle negated, landing on (-x, -y, z, w).
        // REJECTS applying the position rule to a quaternion.
        [Fact]
        public void TheMirrorTreatsPositionsAndRotationsDifferently()
        {
            Assert.Equal((1f, 2f, -3f), MeshPose.ToGodot(1, 2, 3));

            var q = MeshPose.QuatToGodot(0.1f, 0.2f, 0.3f, 0.9f);
            Assert.Equal(-0.1f, q.X, 5);
            Assert.Equal(-0.2f, q.Y, 5);
            Assert.Equal(0.3f, q.Z, 5);      // NOT negated -- the position rule would have flipped this
            Assert.Equal(0.9f, q.W, 5);

            // and it preserves length, which is what lets the blend happen before the conversion
            float len = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
            Assert.Equal(MathF.Sqrt(0.01f + 0.04f + 0.09f + 0.81f), len, 5);
        }

        // A z-mirror applied twice is the identity, for both kinds.
        [Fact]
        public void MirroringTwiceIsTheIdentity()
        {
            var p = MeshPose.ToGodot(4, -5, 6);
            Assert.Equal((4f, -5f, 6f), MeshPose.ToGodot((int)p.X, (int)p.Y, (int)p.Z));
            var q = MeshPose.QuatToGodot(0.1f, 0.2f, 0.3f, 0.9f);
            var r = MeshPose.QuatToGodot(q.X, q.Y, q.Z, q.W);
            Assert.Equal(0.1f, r.X, 5); Assert.Equal(0.2f, r.Y, 5);
            Assert.Equal(0.3f, r.Z, 5); Assert.Equal(0.9f, r.W, 5);
        }
    }
}
