using System;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    public class VramTextureTests
    {
        // ⭐ THE ARITHMETIC IS THE WHOLE CLAIM. 0x54 + 512*512/2 = 131,156 is why these entries are texture
        // pages at all. If someone "tidies" the dimensions to 256x256 or the depth to 8bpp, this fails --
        // and that matters because the 256x256 16bpp reading looked certain for hours and decodes to noise.
        [Fact]
        public void TheLayoutArithmeticProducesTheEntrySize()
        {
            Assert.Equal(0x54, VramTexture.HeaderBytes);
            Assert.Equal(512, VramTexture.Width);
            Assert.Equal(512, VramTexture.Height);
            Assert.Equal(4, VramTexture.BitsPerPixel);
            Assert.Equal(131_156, VramTexture.EntryBytes);
            Assert.Equal(VramTexture.HeaderBytes + VramTexture.Width * VramTexture.Height / 2,
                         VramTexture.EntryBytes);
        }

        static byte[] Page(params (int index, int value)[] pixels)
        {
            var d = new byte[VramTexture.EntryBytes];
            foreach (var (i, v) in pixels)
            {
                int by = VramTexture.HeaderBytes + (i >> 1);
                if ((i & 1) == 0) d[by] = (byte)((d[by] & 0xF0) | (v & 0x0F));
                else d[by] = (byte)((d[by] & 0x0F) | ((v & 0x0F) << 4));
            }
            return d;
        }

        // ⭐ REJECTS THE NIBBLE ORDER BEING FLIPPED. Low nibble is the FIRST pixel. Swap it and every texture
        // comes out with its pixel pairs transposed -- which at a glance looks like a slightly noisy but
        // otherwise correct image, and is exactly the sort of wrong nobody spots.
        [Fact]
        public void LowNibbleIsTheFirstPixel()
        {
            Assert.True(VramTexture.TryDecode(Page((0, 15), (1, 0)), out var img, out var err), err);
            Assert.Equal(255, img.Rgba[0]);   // pixel 0 = index 15 -> white
            Assert.Equal(0, img.Rgba[4]);     // pixel 1 = index 0  -> black
        }

        [Fact]
        public void IndicesSpanTheFullGreyRange()
        {
            Assert.True(VramTexture.TryDecode(Page((0, 0), (1, 15)), out var img, out _));
            Assert.Equal(0, img.Rgba[0]);
            Assert.Equal(255, img.Rgba[4]);   // 15*17 = 255 exactly, not 240
        }

        [Fact]
        public void DecodedImageIsTheDeclaredSize()
        {
            Assert.True(VramTexture.TryDecode(Page(), out var img, out _));
            Assert.Equal(VramTexture.Width, img.Width);
            Assert.Equal(VramTexture.Height, img.Height);
            Assert.Equal(VramTexture.Width * VramTexture.Height * 4, img.Rgba.Length);
        }

        [Fact]
        public void ShortBufferIsRefusedWithTheExpectedSize()
        {
            Assert.False(VramTexture.TryDecode(new byte[100], out _, out string err));
            Assert.Contains("131,156", err);
        }

        [Fact]
        public void NullIsRefusedNotThrown()
            => Assert.False(VramTexture.TryDecode(null, out _, out _));

        [Fact]
        public void OnlyExactlySizedEntriesAreTexturePages()
        {
            Assert.True(VramTexture.LooksLikeTexturePage(new GazEntry { Size = 131_156 }));
            Assert.False(VramTexture.LooksLikeTexturePage(new GazEntry { Size = 131_072 }));  // a palette strip
            Assert.False(VramTexture.LooksLikeTexturePage(null));
        }
    }

    public class ClutTests
    {
        static byte[] Strip(params ushort[] words)
        {
            var d = new byte[Math.Max(Clut.Bytes, words.Length * 2)];
            for (int i = 0; i < words.Length; i++)
            {
                d[i * 2] = (byte)(words[i] & 0xFF);
                d[i * 2 + 1] = (byte)(words[i] >> 8);
            }
            return d;
        }

        static ushort Bgr(int r, int g, int b) => (ushort)((b << 10) | (g << 5) | r);

        [Fact]
        public void ATableIsSixteenEntriesOfThirtyTwoBytes()
        {
            Assert.Equal(16, Clut.Entries);
            Assert.Equal(32, Clut.Bytes);
        }

        // ⭐ REJECTS RGB ORDER. These are VRAM words: red is in the LOW five bits. Reading them as ARGB1555
        // swaps red and blue, which is what turned the legal screen yellow instead of cyan -- a perfectly
        // plausible picture in the wrong colours.
        [Fact]
        public void RedIsInTheLowFiveBits()
        {
            var strip = Strip(0x0000, 0x8001, Bgr(31, 0, 0), Bgr(0, 0, 31));
            var pal = Clut.Read(strip, 0);
            Assert.Equal(255, pal[2 * 4 + 0]);   // entry 2 is pure red
            Assert.Equal(0, pal[2 * 4 + 2]);
            Assert.Equal(255, pal[3 * 4 + 2]);   // entry 3 is pure blue
            Assert.Equal(0, pal[3 * 4 + 0]);
        }

        // ⭐ REJECTS TREATING INDEX 0 AS OPAQUE BLACK. On this hardware 0x0000 means draw nothing. Render it
        // opaque and every cut-out becomes a black rectangle -- which looks deliberate against a dark
        // background and wrong against everything else.
        [Fact]
        public void ZeroIsTransparentNotBlack()
        {
            var pal = Clut.Read(Strip(0x0000, 0x8001), 0);
            Assert.Equal(0, pal[3]);            // entry 0 alpha
            Assert.Equal(255, pal[1 * 4 + 3]);  // entry 1 (0x8001) is opaque
        }

        [Fact]
        public void FiveBitFullScaleReachesTwoFiftyFive()
        {
            var pal = Clut.Read(Strip(0x0000, 0x8001, Bgr(31, 31, 31)), 0);
            Assert.Equal(255, pal[2 * 4 + 0]);
            Assert.Equal(255, pal[2 * 4 + 1]);
            Assert.Equal(255, pal[2 * 4 + 2]);
        }

        // The signature is what proves the 32-byte alignment on the real disc, so it has to be enforced.
        [Fact]
        public void AWellFormedTableNeedsTheSignatureAndSixteenDistinctColours()
        {
            var words = new ushort[16];
            words[0] = 0x0000; words[1] = 0x8001;
            for (int i = 2; i < 16; i++) words[i] = (ushort)(0x1000 + i);
            Assert.True(Clut.LooksLikeTable(Strip(words), 0));
        }

        [Fact]
        public void AWrongSignatureIsRejected()
        {
            var words = new ushort[16];
            words[0] = 0x1234; words[1] = 0x8001;
            for (int i = 2; i < 16; i++) words[i] = (ushort)(0x1000 + i);
            Assert.False(Clut.LooksLikeTable(Strip(words), 0));
        }

        [Fact]
        public void DuplicateColoursMeanItIsNotATable()
        {
            var words = new ushort[16];
            words[0] = 0x0000; words[1] = 0x8001;
            for (int i = 2; i < 16; i++) words[i] = 0x2222;   // all the same
            Assert.False(Clut.LooksLikeTable(Strip(words), 0));
        }

        [Fact]
        public void OutOfRangeReadsAreSafe()
        {
            Assert.Equal(Clut.Entries * 4, Clut.Read(Strip(0x0000, 0x8001), 9999).Length);
            Assert.False(Clut.LooksLikeTable(Strip(0x0000, 0x8001), 9999));
            Assert.False(Clut.LooksLikeTable(null, 0));
            Assert.Equal(0, Clut.TableCount(null));
        }

        [Fact]
        public void TableCountIsTheStripDividedByThirtyTwo()
            => Assert.Equal(Clut.StripBytes / 32, Clut.TableCount(new byte[Clut.StripBytes]));

        [Fact]
        public void TheTwoPaletteStripsAreNamed()
            => Assert.Equal(new[] { 0x104, 0x10C }, Clut.StripEntryIndices);
    }
}
