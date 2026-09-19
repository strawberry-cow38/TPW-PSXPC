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
                                               out var tracks, out _, out _, out int end, out string err), err);
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

            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0, out var tracks, out _, out _, out _, out _));
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
                                               out var tracks, out _, out _, out int end, out string err), err);
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

            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0, out var tracks, out _, out _, out _, out _));
            var t = tracks[0];
            Assert.Equal(100, t.SampleNearest(0).Tx);     // before the first key
            Assert.Equal(100, t.SampleNearest(14).Tx);    // nearer the first
            Assert.Equal(200, t.SampleNearest(26).Tx);    // nearer the second
            Assert.Equal(200, t.SampleNearest(999).Tx);   // after the last
        }

        // ⭐ Type 7 is a type-6 keyframe with the 4-byte {time, duration} header removed: one bare
        // 16-byte {vector, pad, quaternion} block per tick, the evaluator indexing by the clock.
        //
        // The vector is deliberately NOT symmetric and the quaternion is deliberately NOT the
        // identity, because both of those are invariant under the mistake this test exists to catch.
        // A half-word slip reads the pad into the quaternion and the quaternion's w out of it, which
        // an identity block (0,0,0,4096) would survive looking plausible and a symmetric vector would
        // hide entirely. Here a slip in either direction breaks the norm and the translation at once.
        [Fact]
        public void ATypeSevenTrackIsAnUntimedBoneBlock()
        {
            var b = new List<byte>();
            Header(b, 7, bone: 4, count: 2);
            b.AddRange(new byte[16]);            // rest of the 0x18 track header
            foreach (var v in new[] { -5, 6, 7, 0, 2048, 2048, 2048, 2048 }) S16(b, v);
            foreach (var v in new[] { 11, 12, 13, 0, 0, 0, -2048, 3547 }) S16(b, v);

            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0,
                                               out var tracks, out _, out _, out int end, out string err), err);
            var t = Assert.Single(tracks);
            Assert.Equal(7, t.Type);
            Assert.Equal(2, t.Keys.Length);
            Assert.False(t.IsTimed);

            Assert.Equal(-5, t.Keys[0].Tx);
            Assert.Equal(6, t.Keys[0].Ty);
            Assert.Equal(7, t.Keys[0].Tz);
            Assert.Equal(0.5f, t.Keys[0].Qx, 4);
            Assert.Equal(11, t.Keys[1].Tx);
            Assert.Equal(-0.5f, t.Keys[1].Qz, 4);

            // The file normalises to exactly 4096, so this is the tight bound, not a friendly one.
            foreach (var k in t.Keys) Assert.Equal(1f, k.QuatLength(), 3);

            // Timing is SYNTHESISED, one sample per tick -- key k is tick k. Asserted because the
            // shared Sample() path depends on it and nothing in the file states it.
            Assert.Equal(0, t.Keys[0].Time);
            Assert.Equal(1, t.Keys[1].Time);
            Assert.Equal(11, t.Sample(1).Tx);
            Assert.Equal(16 * 2 + 0x18, end);
        }

        // ⚠ Types 1 and 8 share a SIZE row in the game's jump table but not a format: type 1 is two
        // 16-byte {vector, pad, quaternion} blocks where type 8 is a 3x3 rotation matrix. A reader
        // that treats them alike will read nine arbitrary s16 as a rotation and produce a silently
        // wrong pose.
        //
        // ⚠ THIS TEST USED TO ASSERT `IsDecoded == false`, which passed for the whole time type 1 was
        // undecoded and stopped meaning anything the moment it was decoded -- it would have failed on
        // a CORRECT change, which is the signature of a test that encodes the current state instead of
        // the requirement. The requirement is that the two types are read DIFFERENTLY, so that is what
        // is asserted now: the same 32 bytes must yield a keyframe and no rest pose.
        [Fact]
        public void TypeOneIsNotTreatedAsTypeEightDespiteTheSharedSize()
        {
            Assert.Equal(MeshAnimation.TrackSize(8, 3), MeshAnimation.TrackSize(1, 3));

            var b = new List<byte>();
            Header(b, 1, bone: 0, count: 1);
            b.AddRange(new byte[32]);            // rest of the 0x28 track header
            // Block A: vector (10,20,30), zero pad, identity quaternion. Block B: zeroed.
            S16(b, 10); S16(b, 20); S16(b, 30); S16(b, 0);
            S16(b, 0); S16(b, 0); S16(b, 0); S16(b, 4096);
            b.AddRange(new byte[16]);

            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0, out var tracks, out _, out _, out _, out _));
            var t = tracks[0];
            Assert.Equal(1, t.Type);

            // Read as type 1: a keyframe at the type-1 offsets.
            Assert.Single(t.Keys);
            Assert.Equal(10, t.Keys[0].Tx);
            Assert.Equal(1f, t.Keys[0].Qw, 3);
            Assert.Single(t.PairB);

            // NOT read as type 8: the rest pose is never populated. Were these bytes run through the
            // type-8 reader, M00 would be the 10 above.
            Assert.Equal(0, t.Rest.M00);
            Assert.False(t.Rest.IsOrthonormal());
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
            Assert.False(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0, out _, out _, out _, out _, out string err));
            Assert.Contains("past the end", err);
        }

        // REJECTS an unknown type: sizes are unknown past 8, so the walk cannot continue safely.
        [Fact]
        public void AnUnknownTrackTypeIsRefused()
        {
            var b = new List<byte>();
            Header(b, 9, bone: 0, count: 0);
            b.AddRange(new byte[32]);
            Assert.False(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0, out _, out _, out _, out _, out string err));
            Assert.Contains("unknown track type", err);
        }
        // --- the skeleton -------------------------------------------------------------------

        static void BoneRec(List<byte> b, int parent, int qx, int qy, int qz, int qw,
                            int tx, int ty, int tz)
        {
            S16(b, 0); S16(b, 0); S16(b, 0);            // +0..+5 unidentified
            S16(b, parent);                              // +6
            S16(b, qx); S16(b, qy); S16(b, qz); S16(b, qw);   // +8  rest rotation
            for (int i = 0; i < 4; i++) S16(b, 0);       // +16 unidentified
            S16(b, tx); S16(b, ty); S16(b, tz);          // +24 rest translation
            S16(b, 0); S16(b, 4096); S16(b, 4096); S16(b, 4096); S16(b, 0);   // +30 scale
        }

        // Worked by hand. Two bones, the second parented to the first.
        [Fact]
        public void ABoneRecordCarriesItsParentRotationAndTranslation()
        {
            var b = new List<byte>();
            BoneRec(b, -1, 0, 0, 0, 4096, 5, 6, 7);      // root, identity rotation
            BoneRec(b, 0, 0, 0, 2048, 3547, 10, 0, 0);   // child, ~60 degrees about Z

            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 2, 0, 0, 0,
                                               out _, out var sk, out _, out _, out string err), err);
            Assert.Equal(2, sk.Count);
            Assert.True(sk.Bones[0].IsRoot);
            Assert.Equal(-1, sk.Bones[0].Parent);
            Assert.Equal(0, sk.Bones[1].Parent);
            Assert.Equal(5, sk.Bones[0].Tx);
            Assert.Equal(10, sk.Bones[1].Tx);
            Assert.Equal(1f, sk.Bones[1].QuatLength(), 2);
            Assert.Equal(4096, sk.Bones[0].Sx);
            Assert.True(sk.IsWellFormed());
            Assert.Equal(new[] { 0, 1 }, sk.Depths());
        }

        // REJECTS a skeleton that cannot be composed in one forward pass. Both shapes below would
        // sail through a parse that only checked the parent was in range.
        [Theory]
        [InlineData(1, 0, false)]    // bone 0 points FORWARD at bone 1 -- one pass would read a stale parent
        [InlineData(-1, -1, false)]  // two roots
        [InlineData(-1, 1, false)]   // bone 1 is its OWN parent: in range, backward-ish, still a cycle
        [InlineData(-1, 0, true)]    // the only well-formed arrangement of two bones
        public void OnlyASingleRootedBackwardReferencingSkeletonIsWellFormed(int p0, int p1, bool ok)
        {
            var b = new List<byte>();
            BoneRec(b, p0, 0, 0, 0, 4096, 0, 0, 0);
            BoneRec(b, p1, 0, 0, 0, 4096, 0, 0, 0);
            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 2, 0, 0, 0, out _, out var sk, out _, out _, out _));
            Assert.Equal(ok, sk.IsWellFormed());
        }

        // ⭐ Composition is the thing animation actually needs, so it gets an exact case rather than a
        // round-trip that would pass on an identity parent. Parent turns 90 degrees about Z; the child
        // sits 10 along ITS OWN x. In model space that child must land on +y, not +x.
        // REJECTS composing in the wrong order, and REJECTS ignoring the parent entirely.
        [Fact]
        public void AChildIsPlacedThroughItsParentsRotation()
        {
            var b = new List<byte>();
            BoneRec(b, -1, 0, 0, 2896, 2896, 0, 0, 0);   // 90 deg about Z: q=(0,0,sin45,cos45)
            BoneRec(b, 0, 0, 0, 0, 4096, 10, 0, 0);      // child offset 10 along x

            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 2, 0, 0, 0, out _, out var sk, out _, out _, out _));
            var w = sk.RestWorld();
            Assert.Equal(0f, w[0].X, 3);
            Assert.Equal(0f, w[1].X, 2);      // NOT 10 -- that would be the un-rotated offset
            Assert.Equal(10f, w[1].Y, 2);     // the parent's turn carried it onto +y
            Assert.Equal(0f, w[1].Z, 3);
            Assert.True(w[1].R.IsOrthonormal());
        }

        // A quaternion and a 3x3 are the two encodings the file uses for the same rotation; on the disc
        // 2,295 bones carry both and they agree. Here is that identity on a case worked by hand.
        [Fact]
        public void TheQuaternionAndTheMatrixAreTheSameRotation()
        {
            var b = new List<byte>();
            BoneRec(b, -1, 0, 0, 2896, 2896, 0, 0, 0);   // 90 deg about Z
            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 1, 0, 0, 0, out _, out var sk, out _, out _, out _));
            var m = sk.Bones[0].ToMatrix();
            Assert.Equal(0f, m.M00, 2); Assert.Equal(-1f, m.M01, 2);
            Assert.Equal(1f, m.M10, 2); Assert.Equal(0f, m.M11, 2);
            Assert.Equal(1f, m.M22, 2);
            Assert.True(m.IsOrthonormal());
        }

        // REJECTS calling the +4 header field a bone index on a type whose field is measured NOT to be
        // one: type 2 lands in range 0.0% of the time on the disc, type 5 7.7%.
        [Theory]
        [InlineData(6, true)]
        [InlineData(8, true)]
        [InlineData(2, false)]
        [InlineData(3, false)]
        [InlineData(5, false)]
        public void OnlyProvenTypesExposeABoneIndex(int type, bool addressesABone)
        {
            var b = new List<byte>();
            Header(b, type, bone: 4, count: 0);
            b.AddRange(new byte[64]);
            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0, out var tr, out _, out _, out _, out _));
            Assert.Equal(4, tr[0].Header4);                         // the raw field is always kept
            Assert.Equal(addressesABone ? 4 : -1, tr[0].BoneIndex);
        }

        // ⚠ THIS EXISTS BECAUSE THE TEST ABOVE COULD NOT FAIL ON ORDER. Its child had an identity
        // rotation, and A*I == I*A, so a reversed matrix product passed all 23 tests. Rotating BOTH
        // bones, about different axes, is what makes the order observable.
        // REJECTS composing child*parent instead of parent*child.
        [Fact]
        public void CompositionOrderIsParentThenChild()
        {
            var b = new List<byte>();
            BoneRec(b, -1, 0, 0, 2896, 2896, 0, 0, 0);   // parent: 90 deg about Z
            BoneRec(b, 0, 2896, 0, 0, 2896, 0, 0, 0);    // child:  90 deg about X

            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 2, 0, 0, 0, out _, out var sk, out _, out _, out _));
            var r = sk.RestWorld()[1].R;
            // Rz(90)*Rx(90) = [[0,0,1],[1,0,0],[0,1,0]]. The reverse product is
            // [[0,-1,0],[0,0,-1],[1,0,0]], which differs in exactly these cells.
            Assert.Equal(0f, r.M01, 2);
            Assert.Equal(1f, r.M02, 2);
            Assert.Equal(1f, r.M10, 2);
            Assert.Equal(0f, r.M12, 2);
            Assert.Equal(1f, r.M21, 2);
            Assert.True(r.IsOrthonormal());
        }

        // ⚠ THE PAIRS THAT SHARE A SIZE ROW MUST NOT SHARE A TARGET. 2 and 4 have the same stride and
        // header, as do 3 and 5, and they write to different arrays. This is the test that stops a
        // future simplification from collapsing them.
        [Theory]
        [InlineData(0, TrackTarget.Bone)]
        [InlineData(1, TrackTarget.Bone)]
        [InlineData(2, TrackTarget.ScatterSource)]
        [InlineData(3, TrackTarget.ScatterSource)]
        [InlineData(4, TrackTarget.Vertex)]
        [InlineData(5, TrackTarget.Vertex)]
        [InlineData(6, TrackTarget.Bone)]
        [InlineData(7, TrackTarget.Bone)]
        [InlineData(8, TrackTarget.Bone)]
        public void EachTypeDrivesItsOwnKindOfThing(int type, TrackTarget expected)
        {
            var b = new List<byte>();
            Header(b, type, bone: 2, count: 0);
            b.AddRange(new byte[64]);
            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0,
                                               out var tr, out _, out _, out _, out _));
            Assert.Equal(expected, tr[0].Target);
            Assert.Equal(2, tr[0].TargetIndex);
            Assert.Equal(expected == TrackTarget.Bone ? 2 : -1, tr[0].BoneIndex);
        }

        [Fact]
        public void TypesThatShareASizeRowDoNotShareATarget()
        {
            Assert.Equal(MeshAnimation.TrackSize(2, 5), MeshAnimation.TrackSize(4, 5));
            Assert.Equal(MeshAnimation.TrackSize(3, 5), MeshAnimation.TrackSize(5, 5));
            var b = new List<byte>(); Header(b, 2, 0, 0); b.AddRange(new byte[32]);
            var c = new List<byte>(); Header(c, 4, 0, 0); c.AddRange(new byte[32]);
            MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0, out var t2, out _, out _, out _, out _);
            MeshAnimation.TryParse(c.ToArray(), 0, 0, 0, 1, 0, 0, out var t4, out _, out _, out _, out _);
            Assert.NotEqual(t2[0].Target, t4[0].Target);
        }

        // --- interpolation ------------------------------------------------------------------

        static List<byte> SixTrack(params (int t, int dur, int tx, int qz, int qw)[] keys)
        {
            var b = new List<byte>();
            Header(b, 6, bone: 0, count: keys.Length);
            b.AddRange(new byte[20]);                      // preamble
            foreach (var k in keys)
                foreach (var v in new[] { k.t, k.dur, k.tx, 0, 0, 0, 0, 0, k.qz, k.qw }) S16(b, v);
            return b;
        }
        static AnimTrack One(List<byte> b)
        {
            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0,
                                               out var tr, out _, out _, out _, out string e), e);
            return tr[0];
        }

        // ⭐ Worked by hand against the game's own arithmetic at 0x8002da8c:
        // ((t - start) << 12) / duration, INTEGER division. 5<<12 == 20480, over 20 is 1024.
        // REJECTS a float divide dressed up as the hardware's, and REJECTS an off-by-one span.
        [Theory]
        [InlineData(10, 0)]        // the key's own start
        [InlineData(15, 1024)]     // a quarter through a span of 20
        [InlineData(20, 2048)]     // halfway
        [InlineData(29, 3891)]     // 19<<12 / 20 -- integer division truncates, 3891.2 is not 3892
        [InlineData(9, -1)]        // before any key
        [InlineData(30, -1)]       // exactly at the end: the span is [start, start+dur)
        public void TheBlendWeightIsTheGamesOwnIntegerArithmetic(int t, int expected)
            => Assert.Equal(expected, One(SixTrack((10, 20, 0, 0, 4096))).BlendWeight(t));

        [Fact]
        public void ADurationIsHowLongTheKeyLastsAndMeetsTheNextKey()
        {
            var tr = One(SixTrack((10, 20, 100, 0, 4096), (30, 15, 200, 0, 4096)));
            Assert.Equal(10, tr.Keys[0].Time);
            Assert.Equal(20, tr.Keys[0].Duration);
            Assert.Equal(30, tr.Keys[1].Time);
            // The property that identified the field: every key meets the next one exactly.
            Assert.Equal(tr.Keys[1].Time, tr.Keys[0].Time + tr.Keys[0].Duration);
        }

        // REJECTS returning a key instead of blending, and REJECTS blending the wrong way round.
        [Fact]
        public void SamplingBlendsBetweenTheBracketingKeys()
        {
            var tr = One(SixTrack((0, 100, 0, 0, 4096), (100, 50, 400, 0, 4096)));
            Assert.Equal(0, tr.Sample(0).Tx);
            Assert.Equal(100, tr.Sample(25).Tx);     // a quarter of the way to 400
            Assert.Equal(200, tr.Sample(50).Tx);     // halfway -- NOT 0 and NOT 400
            Assert.Equal(300, tr.Sample(75).Tx);
            Assert.Equal(400, tr.Sample(100).Tx);    // past the last key's start: held
            Assert.Equal(0, tr.Sample(-5).Tx);       // before the first: held
        }

        // ⭐ REJECTS blending quaternions without the sign fix. q and -q are the SAME rotation, so a
        // naive componentwise blend of a pair that point opposite ways collapses toward zero instead of
        // rotating between them. Here the two keys are the same rotation written with opposite signs:
        // every sample must stay that rotation, and a unit quaternion throughout.
        [Fact]
        public void BlendingTakesTheShortWayRoundBetweenOppositeSignQuaternions()
        {
            var tr = One(SixTrack((0, 100, 0, 2896, 2896), (100, 50, 0, -2896, -2896)));
            for (int t = 0; t <= 100; t += 10)
            {
                var k = tr.Sample(t);
                Assert.Equal(1f, k.QuatLength(), 3);
                Assert.Equal(0.7071f, MathF.Abs(k.Qz), 2);   // still the same rotation, not collapsed
            }
        }

        // ⚠ THE TEST ABOVE CANNOT CATCH A MISSING RENORMALISATION. Its two keys are the same rotation
        // with flipped signs, so after the sign fix the blend is exact and already unit length -- dropping
        // the normalise passed all 45 tests. Two rotations 90 degrees apart is what exposes it: a straight
        // componentwise average of those has length cos(45) = 0.924, which would scale the pose.
        [Fact]
        public void BlendingTwoDifferentRotationsStaysUnitLength()
        {
            // identity -> 90 degrees about Z
            var tr = One(SixTrack((0, 100, 0, 0, 4096), (100, 50, 0, 2896, 2896)));
            for (int t = 0; t <= 100; t += 10)
                Assert.Equal(1f, tr.Sample(t).QuatLength(), 3);
            Assert.InRange(tr.Sample(50).QuatLength(), 0.999f, 1.001f);
        }

        [Fact]
        public void AZeroDurationKeyIsNotDividedBy()
        {
            var tr = One(SixTrack((10, 0, 100, 0, 4096), (10, 0, 200, 0, 4096)));
            Assert.Equal(-1, tr.BlendWeight(10));      // no span contains anything
            Assert.Equal(100, tr.Sample(10).Tx);       // and sampling does not throw
        }

        // --- position tracks ----------------------------------------------------------------

        // ⭐ Types 2/4 and 3/5 carry the SAME 8-byte payload; the timed pair just puts 4 bytes of timing
        // in front. Parsing a timed type as untimed would silently read the time as the x coordinate,
        // which is why these are asserted against one another rather than separately.
        [Fact]
        public void AnUntimedPositionTrackIsOneSamplePerTick()
        {
            var b = new List<byte>();
            Header(b, 2, bone: 0, count: 3);
            b.AddRange(new byte[0x10 - 8]);        // records begin at the TYPE's header size, not at +8
            foreach (var v in new[] { 10, 20, 30, 0, 11, 21, 31, 0, 12, 22, 32, 0 }) S16(b, v);

            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0,
                                               out var tr, out _, out _, out _, out string e), e);
            var t = tr[0];
            Assert.False(t.IsTimed);
            Assert.Equal(3, t.Positions.Length);
            Assert.Equal(10, t.Positions[0].X);   // NOT a time: the record starts at the coordinate
            Assert.Equal(20, t.Positions[0].Y);
            Assert.Equal(30, t.Positions[0].Z);
            Assert.Equal(0, t.Positions[0].Time);
            Assert.Equal(12, t.Positions[2].X);
        }

        [Fact]
        public void ATimedPositionTrackPutsTimingInFrontOfTheSamePayload()
        {
            var b = new List<byte>();
            Header(b, 3, bone: 0, count: 2);
            b.AddRange(new byte[0x14 - 8]);        // ditto: type 3's header is 0x14
            foreach (var v in new[] { 5, 15, 10, 20, 30, 0, 20, 8, 11, 21, 31, 0 }) S16(b, v);

            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0,
                                               out var tr, out _, out _, out _, out string e), e);
            var t = tr[0];
            Assert.True(t.IsTimed);
            Assert.Equal(2, t.Positions.Length);
            Assert.Equal(5, t.Positions[0].Time);
            Assert.Equal(15, t.Positions[0].Duration);
            Assert.Equal(10, t.Positions[0].X);      // the payload begins AFTER the timing
            Assert.Equal(30, t.Positions[0].Z);
            Assert.Equal(20, t.Positions[1].Time);
            // the property that identifies the field as a duration
            Assert.Equal(t.Positions[1].Time, t.Positions[0].Time + t.Positions[0].Duration);
        }

        // REJECTS calling a type timed when the disc says it is not. Types 1, 2, 4 and 7 fail the
        // time+duration chain (0.0%, 0.0%, 0.0% and 6.4%); 0, 3, 5 and 6 satisfy it on every pair.
        [Theory]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(3, true)]
        [InlineData(4, false)]
        [InlineData(5, true)]
        [InlineData(6, true)]
        [InlineData(7, false)]
        public void OnlyTheTypesThatChainAreTimed(int type, bool timed)
        {
            var b = new List<byte>();
            Header(b, type, bone: 0, count: 0);
            b.AddRange(new byte[64]);
            Assert.True(MeshAnimation.TryParse(b.ToArray(), 0, 0, 0, 1, 0, 0,
                                               out var tr, out _, out _, out _, out _));
            Assert.Equal(timed, tr[0].IsTimed);
        }

    }
}
