using System;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    public class SpriteBankTests
    {
        [Fact]
        public void ABankIsSixteenBlocksOfTwoFiftySixBySixtyFour()
        {
            Assert.Equal(131_072, SpriteBank.Bytes);
            Assert.Equal(16, SpriteBank.Blocks);
            Assert.Equal(256, SpriteBank.Width);
            Assert.Equal(1024, SpriteBank.Height);
            Assert.Equal(SpriteBank.Width * SpriteBank.Height / 2, SpriteBank.Bytes);
        }

        // ⭐ REJECTS CONFUSING A BANK WITH A SHEET. They differ by exactly the sheet's 0x54 header and are
        // otherwise both 131KB of 4bpp. Feeding one through the other's decoder shreds the image -- a sheet
        // assembled with bank block order came apart into fragments -- so the size test must be exact.
        [Fact]
        public void ASheetIsNotABankAndViceVersa()
        {
            Assert.True(SpriteBank.LooksLikeBank(new GazEntry { Size = 131_072 }));
            Assert.False(SpriteBank.LooksLikeBank(new GazEntry { Size = 131_156 }));   // that is a sheet
            Assert.False(VramTexture.LooksLikeTexturePage(new GazEntry { Size = 131_072 }));
            Assert.False(SpriteBank.LooksLikeBank(null));
        }

        // ⭐ THE MEASURED VRAM PLACEMENT. These 14 came from content-hash matches against a live capture,
        // reproduced independently. If someone "simplifies" the origin formula to run straight down, every
        // one of these fails -- the columns alternate, which is why the matches did.
        [Theory]
        [InlineData(0, 768, 0)]
        [InlineData(1, 768, 64)]
        [InlineData(2, 768, 128)]
        [InlineData(3, 768, 192)]
        [InlineData(4, 832, 0)]
        [InlineData(5, 832, 64)]
        [InlineData(6, 832, 128)]
        [InlineData(7, 832, 192)]
        [InlineData(8, 768, 256)]
        [InlineData(9, 768, 320)]
        [InlineData(10, 768, 384)]
        [InlineData(11, 768, 448)]
        [InlineData(12, 832, 256)]
        [InlineData(13, 832, 320)]
        public void MeasuredBlocksLandWhereTheHardwarePutThem(int block, int x, int y)
        {
            Assert.Equal((x, y), SpriteBank.VramOrigin(block));
            Assert.True(SpriteBank.PlacementMeasured(block));
        }

        // The last two were blank in the capture, so their placement follows the pattern only. Keeping that
        // distinction in the API stops "14 measured + 2 inferred" quietly becoming "16 measured".
        [Fact]
        public void TheLastTwoBlocksAreInferredNotMeasured()
        {
            Assert.False(SpriteBank.PlacementMeasured(14));
            Assert.False(SpriteBank.PlacementMeasured(15));
            Assert.Equal((832, 384), SpriteBank.VramOrigin(14));
            Assert.Equal((832, 448), SpriteBank.VramOrigin(15));
        }

        [Fact]
        public void BlockIndexIsRangeChecked()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SpriteBank.VramOrigin(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpriteBank.VramOrigin(16));
        }

        [Fact]
        public void LowNibbleIsTheFirstPixel()
        {
            var d = new byte[SpriteBank.Bytes];
            d[0] = 0x0F;                      // pixel 0 = 15, pixel 1 = 0
            Assert.True(SpriteBank.TryDecode(d, out var img, out var err), err);
            Assert.Equal(255, img.Rgba[0]);
            Assert.Equal(0, img.Rgba[4]);
        }

        [Fact]
        public void BlocksAreCutTopToBottom()
        {
            // ⚠ THE MARKER NEEDS TWO PIXELS. It used to be one byte, b + 1, read back from the first pixel, and for
            // the last block that byte is 16 = 0x10, whose LOW nibble (the first 4-bit pixel) is 0. The test went
            // red on block 15 ("the last block comes back empty", tinyclaw's nightly) with the decoder fine: the
            // marker could not be represented. Now the low nibble is the first pixel and the high nibble the
            // second, so 1..16 are all distinct and block 15 cannot be mistaken for block 0.
            var d = new byte[SpriteBank.Bytes];
            for (int b = 0; b < SpriteBank.Blocks; b++) d[b * SpriteBank.BlockBytes] = (byte)(b + 1);
            Assert.True(SpriteBank.TryDecode(d, out var bank, out _));
            for (int b = 0; b < SpriteBank.Blocks; b++)
            {
                var blk = SpriteBank.Block(bank, b);
                Assert.Equal(SpriteBank.Width, blk.Width);
                Assert.Equal(SpriteBank.BlockHeight, blk.Height);
                int marker = b + 1;
                Assert.Equal((byte)((marker & 0x0F) * 17), blk.Rgba[0]);   // first pixel: low nibble
                Assert.Equal((byte)((marker >> 4) * 17), blk.Rgba[4]);     // second pixel: high nibble
            }
            Assert.Null(SpriteBank.Block(bank, 16));
            Assert.Null(SpriteBank.Block(null, 0));
        }

        [Fact]
        public void ShortBufferIsRefusedWithTheExpectedSize()
        {
            Assert.False(SpriteBank.TryDecode(new byte[100], out _, out string err));
            Assert.Contains("131,072", err);
        }

        [Fact]
        public void NullIsRefusedNotThrown()
            => Assert.False(SpriteBank.TryDecode(null, out _, out _));
    }
}
