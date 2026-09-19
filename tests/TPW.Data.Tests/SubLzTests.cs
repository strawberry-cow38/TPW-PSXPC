using System;
using System.Collections.Generic;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    public class SubLzTests
    {
        static (char op, int a, int b) L(int v) => ('L', v, 0);
        static (char op, int a, int b) M(int distance, int length) => ('M', distance, length);
        static readonly (char op, int a, int b) End = ('E', 0, 0);

        /// <summary>Assemble a stream from ops, choosing the encoding the format defines for each: a two-byte
        /// match at distance 1..160 is the short form, everything else the long form.
        ///
        /// ⚠ WRITTEN FROM THE SAME READING OF THE ROUTINE AS THE DECODER, so the two could share a
        /// misconception and agree with each other. That is why the first test uses bytes copied off the
        /// disc and an expansion the game's own loader reads, not anything this helper produced.</summary>
        static byte[] Assemble(params (char op, int a, int b)[] ops)
        {
            var o = new List<byte>();
            int flagAt = -1, bit = 8;
            void Flag(int v)
            {
                if (bit == 8) { flagAt = o.Count; o.Add(0); bit = 0; }
                if (v != 0) o[flagAt] = (byte)(o[flagAt] | (1 << bit));
                bit++;
            }
            foreach (var (op, a, b) in ops)
            {
                switch (op)
                {
                    case 'L': Flag(0); o.Add((byte)a); break;
                    case 'M':
                        Flag(1);
                        if (b == 2 && a >= 1 && a <= 160) o.Add((byte)(0x100 - a));
                        else if (b >= 3 && b <= 7) { o.Add((byte)(((b - 3) << 4) | (a >> 8))); o.Add((byte)a); }
                        else { o.Add((byte)(0x50 | (a >> 8))); o.Add((byte)a); o.Add((byte)(b - 8)); }
                        break;
                    case 'E': Flag(1); o.Add(0); o.Add(0); break;
                }
            }
            return o.ToArray();
        }

        // ⭐ THE DISC IS THE REFERENCE. Entry 0 sub-entry 6 begins `f4 01 00 30 01 24 40 08` on the disc and
        // the loader reads its expansion as the mesh header `01 00 00 00 | 00 00 00 00 | 24 00 00 00 | ...`.
        // Only the flag byte differs here (0x34 for 0xf4): bits 0-4 are as on the disc and bit 5 is turned
        // into a terminator, because the disc's stream runs on for 900 more bytes. A wrong bit order reads
        // 0x30 as a literal; a wrong length bias puts 0x24 in the wrong column; a wrong 0x60 split makes
        // 0x30 a short match. Every one of those fails this.
        [Fact]
        public void TheDiscStreamPrefixExpandsToTheMeshHeader()
        {
            var src = new byte[] { 0x34, 0x01, 0x00, 0x30, 0x01, 0x24, 0x40, 0x08, 0x00, 0x00 };
            Assert.True(SubLz.TryDecompress(src, 0, 16, out var dst, out int used, out string err), err);
            Assert.Equal(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0x24, 0, 0, 0, 0, 0, 0, 0 }, dst);
            Assert.Equal(src.Length, used);
        }

        [Fact]
        public void FlagBitsAreConsumedLsbFirstEightPerByte()
        {
            // Eight literals use up the first flag byte (all zero bits); the terminator needs a fresh one.
            var src = new byte[] { 0x00, 1, 2, 3, 4, 5, 6, 7, 8, 0x01, 0x00, 0x00 };
            Assert.True(SubLz.TryDecompress(src, 0, 8, out var dst, out string err), err);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, dst);

            // Bit 0 literal, bit 1 terminator: MSB-first reading would take 0x00 0x00 as two more literals.
            Assert.True(SubLz.TryDecompress(new byte[] { 0x02, 0x41, 0x00, 0x00 }, 0, 1, out dst, out err), err);
            Assert.Equal(new byte[] { 0x41 }, dst);
        }

        [Fact]
        public void ShortMatchIsTwoBytesAtDistanceTwoFiftySixMinusCode()
        {
            var src = Assemble(L('A'), L('B'), M(2, 2), End);
            Assert.Equal(0xFE, src[3]);                                  // 0x100 - 2, the hand-checked encoding
            Assert.True(SubLz.TryDecompress(src, 0, 4, out var dst, out string err), err);
            Assert.Equal("ABAB", System.Text.Encoding.ASCII.GetString(dst));

            // 0x60 is the smallest short code and the longest short distance: 160 back.
            var ops = new List<(char, int, int)>();
            for (int i = 0; i < 160; i++) ops.Add(L(i));
            ops.Add(M(160, 2)); ops.Add(End);
            src = Assemble(ops.ToArray());
            Assert.True(SubLz.TryDecompress(src, 0, 162, out dst, out err), err);
            Assert.Equal(0, dst[160]); Assert.Equal(1, dst[161]);
        }

        [Fact]
        public void LongMatchLengthIsNibblePlusThreeAndNibbleFiveTakesAByte()
        {
            var src = Assemble(L(7), M(1, 7), End);                       // nibble 4: 7 copies of the last byte
            Assert.Equal(0x40, src[2]); Assert.Equal(0x01, src[3]);
            Assert.True(SubLz.TryDecompress(src, 0, 8, out var dst, out string err), err);
            foreach (var b in dst) Assert.Equal(7, b);

            src = Assemble(L(9), M(1, SubLz.MaxMatch), End);              // nibble 5, byte 0xFF: 263 copies
            Assert.Equal(0x50, src[2]); Assert.Equal(0x01, src[3]); Assert.Equal(0xFF, src[4]);
            Assert.True(SubLz.TryDecompress(src, 0, 264, out dst, out err), err);
            foreach (var b in dst) Assert.Equal(9, b);
        }

        // ⭐ REJECTS A BLOCK COPY. Distance 2, length 7 has to read bytes written by the same match.
        [Fact]
        public void OverlappingMatchRepeatsThePattern()
        {
            var src = Assemble(L('A'), L('B'), M(2, 7), End);
            Assert.True(SubLz.TryDecompress(src, 0, 9, out var dst, out string err), err);
            Assert.Equal("ABABABABA", System.Text.Encoding.ASCII.GetString(dst));
        }

        [Fact]
        public void TwelveBitDistanceReachesBackFourThousandNinetyFive()
        {
            var ops = new List<(char, int, int)> { L(0xAA) };
            ops.Add(M(1, SubLz.MaxMatch));                                // 264 bytes of 0xAA
            for (int n = 264; n < 4095; n += 263) ops.Add(M(1, Math.Min(SubLz.MaxMatch, 4095 - n)));
            ops.Add(L(0xBB));                                             // byte 4095
            ops.Add(M(4095, 3));                                          // reaches the first byte
            ops.Add(End);
            var src = Assemble(ops.ToArray());
            Assert.True(SubLz.TryDecompress(src, 0, 4099, out var dst, out string err), err);
            Assert.Equal(0xBB, dst[4095]);
            Assert.Equal(0xAA, dst[4096]); Assert.Equal(0xAA, dst[4097]); Assert.Equal(0xAA, dst[4098]);
        }

        // ⭐ THE ORACLE IS BUILT INTO THE API. A stream that stops short of, or would run past, the size the
        // container table declares is refused, because a mesh parsed from a half-filled buffer reports a
        // wrong shape rather than an error.
        [Fact]
        public void TerminatorMustLandExactlyOnTheDeclaredSize()
        {
            var src = Assemble(L(1), L(2), L(3), L(4), End);
            Assert.True(SubLz.TryDecompress(src, 0, 4, out _, out string err), err);
            Assert.False(SubLz.TryDecompress(src, 0, 5, out var dst, out err));
            Assert.Null(dst);
            Assert.Contains("4 of the declared 5", err);
            Assert.False(SubLz.TryDecompress(src, 0, 3, out _, out err));
            Assert.Contains("pass the declared size", err);
        }

        [Fact]
        public void MatchBeforeTheStartIsRefused()
        {
            Assert.False(SubLz.TryDecompress(new byte[] { 0x01, 0x30, 0x05 }, 0, 6, out _, out string err));
            Assert.Contains("5 bytes back", err);
        }

        [Fact]
        public void TruncatedSourceIsRefusedNotThrown()
        {
            var full = Assemble(L(1), M(1, 300), End);
            for (int cut = 0; cut < full.Length; cut++)
            {
                var src = new byte[cut];
                Buffer.BlockCopy(full, 0, src, 0, cut);
                Assert.False(SubLz.TryDecompress(src, 0, 301, out _, out string err));
                Assert.Contains("source ended", err);
            }
        }

        [Fact]
        public void OffsetIntoALargerBufferIsHonoured()
        {
            var stream = Assemble(L('x'), L('y'), End);
            var src = new byte[16];
            Buffer.BlockCopy(stream, 0, src, 5, stream.Length);
            Assert.True(SubLz.TryDecompress(src, 5, 2, out var dst, out int used, out string err), err);
            Assert.Equal("xy", System.Text.Encoding.ASCII.GetString(dst));
            Assert.Equal(stream.Length, used);
        }

        [Fact]
        public void MeshContainerExpandsACompressedSubEntryBeforeWalking()
        {
            // A container with one compressed sub-entry whose expansion is a 0-vertex, 0-block mesh header.
            var header = new byte[MeshContainer.MeshHeaderBytes];        // every count zero
            var ops = new List<(char, int, int)> { L(0) };
            ops.Add(M(1, header.Length - 1)); ops.Add(End);
            var stream = Assemble(ops.ToArray());

            var entry = new List<byte>();
            void U32(uint v) { entry.Add((byte)v); entry.Add((byte)(v >> 8)); entry.Add((byte)(v >> 16)); entry.Add((byte)(v >> 24)); }
            U32(MeshContainer.Magic); U32(1); U32(0); U32(0); U32(0); U32(0); U32(0); U32(0);   // 0x20 header, ntab 0
            U32((uint)(MeshContainer.HeaderBytes + 8)); U32((uint)header.Length);              // sub 0: off, unpacked
            entry.AddRange(stream);
            var d = entry.ToArray();

            Assert.True(MeshContainer.TryParse(d, out var c, out string err), err);
            Assert.True(c.IsCompressed(0));
            Assert.True(c.TryParseMesh(d, 0, out var mesh, out err), err);
            Assert.True(mesh.WasCompressed);
            Assert.Equal(0, mesh.VertexCount);
            Assert.Equal(MeshContainer.MeshHeaderBytes, mesh.FaceBytesEnd);   // relative to the expanded buffer

            // The same container with the declared size off by one is refused by name, not walked.
            d[MeshContainer.HeaderBytes + 4] = (byte)(header.Length + 1);
            Assert.True(MeshContainer.TryParse(d, out c, out err), err);
            Assert.False(c.TryParseMesh(d, 0, out _, out err));
            Assert.Contains("did not expand", err);
        }
    }
}
