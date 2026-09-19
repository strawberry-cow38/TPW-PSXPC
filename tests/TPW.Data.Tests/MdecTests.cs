using System;
using System.Collections.Generic;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    /// <summary>Synthetic BS v2 bitstreams, built with the bit-writer below, decoded by Mdec.
    ///
    /// ⭐ THE REAL ACCEPTANCE TEST IS NOT HERE: it was every frame of every movie on the disc compared plane by
    /// plane against ffmpeg's independent decoder (max 4 levels apart, 1,752 frames). These tests pin the
    /// CONVENTIONS that a nearly-right decoder gets wrong while still producing a plausible picture — each one
    /// names the wrong implementation it rejects.</summary>
    public class MdecTests
    {
        /// <summary>Writes bits into 16-bit little-endian halfwords, most significant bit first: the BS bit order.</summary>
        sealed class BitWriter
        {
            readonly List<byte> _out = new();
            int _acc, _n;

            public void Write(string bits)
            {
                foreach (char c in bits) WriteBit(c == '1');
            }

            public void Write(int value, int count)
            {
                for (int i = count - 1; i >= 0; i--) WriteBit(((value >> i) & 1) != 0);
            }

            void WriteBit(bool one)
            {
                _acc = (_acc << 1) | (one ? 1 : 0);
                if (++_n == 16)
                {
                    _out.Add((byte)(_acc & 0xFF)); _out.Add((byte)(_acc >> 8));   // little-endian halfword
                    _acc = 0; _n = 0;
                }
            }

            public byte[] ToFrame(int qscale)
            {
                Write("0111111111");                                  // the encoder's end-of-frame padding
                while (_n != 0) WriteBit(false);
                var frame = new byte[8 + _out.Count];
                frame[2] = 0x00; frame[3] = 0x38;                    // magic 3800h
                frame[4] = (byte)qscale;
                frame[6] = 2;                                         // version 2
                _out.CopyTo(frame, 8);
                return frame;
            }
        }

        const string EndOfBlock = "10";

        static void DcOnlyBlock(BitWriter w, int dc)
        {
            w.Write(dc & 0x3FF, 10);
            w.Write(EndOfBlock);
        }

        /// <summary>A macroblock whose six blocks are all DC-only: Cr, Cb, then the four luma blocks.</summary>
        static void FlatMacroblock(BitWriter w, int cr, int cb, int y)
        {
            DcOnlyBlock(w, cr); DcOnlyBlock(w, cb);
            for (int i = 0; i < 4; i++) DcOnlyBlock(w, y);
        }

        static byte[] Pixel(TpwImage img, int x, int y)
        {
            int o = (y * img.Width + x) * 4;
            return new[] { img.Rgba[o], img.Rgba[o + 1], img.Rgba[o + 2], img.Rgba[o + 3] };
        }

        // ⭐ THE DC ARITHMETIC IS EXACT AND SCALE-FREE. A DC of 400 is multiplied by the table's first entry (2)
        // and NOT by the frame's quantisation scale, then the IDCT divides by 8: 400*2/8 + 128 = 228. A decoder
        // that applies the scale to the DC gives 328→255 here at qscale 2; one that uses the MPEG matrix's 8
        // instead of the console's 2 gives 255 too. Both would still show a recognisable picture.
        [Fact]
        public void DcIsScaledByTwoAndNotByTheQuantisationScale()
        {
            var w = new BitWriter();
            FlatMacroblock(w, 0, 0, 400);
            Assert.True(Mdec.TryDecodeFrame(w.ToFrame(2), 16, 16, out var img, out string err), err);
            Assert.Equal(16, img.Width);
            Assert.Equal(16, img.Height);
            foreach (var (x, y) in new[] { (0, 0), (15, 15), (7, 8) })
            {
                var p = Pixel(img, x, y);
                Assert.Equal(228, (int)p[0]); Assert.Equal(228, (int)p[1]); Assert.Equal(228, (int)p[2]); Assert.Equal(255, (int)p[3]);
            }
        }

        [Fact]
        public void ZeroEverythingIsMidGrey()
        {
            var w = new BitWriter();
            FlatMacroblock(w, 0, 0, 0);
            Assert.True(Mdec.TryDecodeFrame(w.ToFrame(1), 16, 16, out var img, out string err), err);
            Assert.Equal(new byte[] { 128, 128, 128, 255 }, Pixel(img, 5, 9));
        }

        // ⚠ MACROBLOCKS RUN DOWN THE COLUMN FIRST. The second macroblock in the stream is the one BELOW the first,
        // not the one to its right (psx-spx: "vertically arranged"; ffmpeg mdec.c loops x outside y). A row-major
        // decoder shows every frame as a scrambled tiling that is still obviously "the picture".
        [Fact]
        public void MacroblocksAreColumnMajor()
        {
            var w = new BitWriter();
            FlatMacroblock(w, 0, 0, -400);   // (0,0)   dark
            FlatMacroblock(w, 0, 0, 400);    // (0,16)  light: second in the stream is BELOW the first
            FlatMacroblock(w, 0, 0, -400);   // (16,0)  dark
            FlatMacroblock(w, 0, 0, -400);   // (16,16) dark
            Assert.True(Mdec.TryDecodeFrame(w.ToFrame(1), 32, 32, out var img, out string err), err);
            Assert.Equal(28, (int)Pixel(img, 0, 0)[0]);
            Assert.Equal(228, (int)Pixel(img, 0, 16)[0]);
            Assert.Equal(28, (int)Pixel(img, 16, 0)[0]);
            Assert.Equal(28, (int)Pixel(img, 16, 16)[0]);
        }

        // Cr comes first and drives red; Cb second and drives blue. Swapping them (the other plausible order)
        // paints every warm scene cold and is not obviously wrong on an unfamiliar movie.
        [Fact]
        public void ChromaOrderIsCrThenCb()
        {
            var w = new BitWriter();
            FlatMacroblock(w, 300, 0, 0);    // Cr = +300*2/8 = 75 → R = 128 + 1.402*75 ≈ 233, B unchanged
            Assert.True(Mdec.TryDecodeFrame(w.ToFrame(1), 16, 16, out var img, out string err), err);
            var p = Pixel(img, 3, 3);
            Assert.InRange((int)p[0], 230, 236);
            Assert.InRange((int)p[2], 126, 130);
            Assert.True((int)p[1] < 128, "green should drop under positive Cr");
        }

        // ⭐ THE FIRST AC COEFFICIENT IS THE HORIZONTAL COSINE. Scan index 1 is raster (row 0, column 1): one
        // half-cycle of cosine across the block, constant down it. A transposed zig-zag (the other diagonal
        // convention) makes the gradient vertical instead; the picture would still look like the movie, just
        // with every edge's ringing rotated 90°.
        [Fact]
        public void FirstAcCoefficientVariesAcrossNotDown()
        {
            var w = new BitWriter();
            DcOnlyBlock(w, 0); DcOnlyBlock(w, 0);                     // Cr, Cb
            w.Write(0, 10); w.Write("11"); w.Write("0"); w.Write(EndOfBlock);   // Y1: DC 0, then (run 0, level +1), EOB
            for (int i = 0; i < 3; i++) DcOnlyBlock(w, 0);            // Y2..Y4
            // qscale 40 so that level 1 dequantises to (1*16*40+4)>>3 = 80: about ±14 levels across the block.
            // At qscale 1 it is 2, a third of a level, which rounds away to nothing — the DC blocks are unaffected
            // because the DC does not see the scale.
            Assert.True(Mdec.TryDecodeFrame(w.ToFrame(40), 16, 16, out var img, out string err), err);
            int left = (int)Pixel(img, 0, 0)[0], right = (int)Pixel(img, 7, 0)[0];
            Assert.True(left > right, $"left {left} should be brighter than right {right}");
            Assert.Equal((int)Pixel(img, 0, 0)[0], (int)Pixel(img, 0, 7)[0]);     // constant down the column
            Assert.Equal((int)Pixel(img, 7, 0)[0], (int)Pixel(img, 7, 7)[0]);
            Assert.Equal(128, (int)Pixel(img, 8, 0)[0]);                     // Y2 untouched
        }

        // The escape code carries a 6-bit run and a 10-bit signed level. Its run of 5 lands the coefficient at
        // scan index 6 = raster (row 0, column 3): still a purely horizontal pattern, now three half-cycles.
        [Fact]
        public void EscapeCodeCarriesSixBitRunAndTenBitLevel()
        {
            var w = new BitWriter();
            DcOnlyBlock(w, 0); DcOnlyBlock(w, 0);
            w.Write(0, 10); w.Write("000001"); w.Write(5, 6); w.Write(-20 & 0x3FF, 10); w.Write(EndOfBlock);
            for (int i = 0; i < 3; i++) DcOnlyBlock(w, 0);
            Assert.True(Mdec.TryDecodeFrame(w.ToFrame(1), 16, 16, out var img, out string err), err);
            Assert.Equal((int)Pixel(img, 2, 0)[0], (int)Pixel(img, 2, 7)[0]);     // horizontal frequency only
            Assert.NotEqual(128, (int)Pixel(img, 0, 0)[0]);                  // and it did something
            Assert.Equal(128, (int)Pixel(img, 0, 8)[0]);                     // Y3 untouched
        }

        [Fact]
        public void RejectsWrongMagicAndOtherVersions()
        {
            var w = new BitWriter();
            FlatMacroblock(w, 0, 0, 0);
            var frame = w.ToFrame(1);
            frame[6] = 3;
            Assert.False(Mdec.TryDecodeFrame(frame, 16, 16, out _, out string err));
            Assert.Contains("version 3", err);
            frame[6] = 2; frame[3] = 0x37;
            Assert.False(Mdec.TryDecodeFrame(frame, 16, 16, out _, out err));
            Assert.Contains("magic", err);
        }

        // A frame that ends before its macroblocks do has been misread or truncated; decoding zeros into the
        // rest would return a plausible half-picture with no complaint.
        [Fact]
        public void RejectsAFrameThatRunsOutOfBits()
        {
            var w = new BitWriter();
            FlatMacroblock(w, 0, 0, 0);
            var frame = w.ToFrame(1);                                  // one macroblock's worth of bits
            Assert.False(Mdec.TryDecodeFrame(frame, 64, 64, out _, out string err));   // asked for sixteen
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void ReportsTheStructuralChecksOnAWellFormedFrame()
        {
            var w = new BitWriter();
            FlatMacroblock(w, 0, 0, 0);
            Assert.True(Mdec.TryDecodeFrame(w.ToFrame(1), 16, 16, null, out _, out string err, out var info), err);
            Assert.Equal(2, info.Version);
            Assert.Equal(1, info.QScale);
            Assert.Equal(12, info.CodeCount);       // six blocks × (DC + EOB)
            Assert.True(info.TailPaddingOk);
        }
    }
}
