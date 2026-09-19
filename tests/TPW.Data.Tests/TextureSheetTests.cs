using System;
using System.Collections.Generic;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    /// <summary>The texture sheet format and the game's headerless UNPAK. Expected values are worked by hand.</summary>
    public class TextureSheetTests
    {
        // ⭐ Worked by hand: 02 'A' 'B' 'C' is a literal run of 3; FE 04 copies 4 bytes from 2 back, one at a
        // time, so it reads what it has just written: B C B C. 80 00 is the terminator.
        //   out = A B C B C B C (7 bytes), consumed = 8 (the terminator counts).
        // REJECTS: a block copy (the overlap would read bytes not yet written), and a consumed count that
        // leaves out the terminator (the next block's stream would then start two bytes early).
        [Fact]
        public void TheBlockRoutineCopiesOverlapsAndCountsItsTerminator()
        {
            var src = new byte[] { 0x02, (byte)'A', (byte)'B', (byte)'C', 0xFE, 0x04, 0x80, 0x00, 0x55 };
            var dst = new byte[16];
            Assert.True(Unpak.TryDecompressBlock(src, 0, dst, out int produced, out int consumed, out string err), err);
            Assert.Equal(7, produced);
            Assert.Equal(8, consumed);
            Assert.Equal("ABCBCBC", System.Text.Encoding.ASCII.GetString(dst, 0, 7));
        }

        [Fact]
        public void TheBlockRoutineRefusesToOverrunItsBlock()
        {
            var src = new byte[] { 0x07, 1, 2, 3, 4, 5, 6, 7, 8, 0x80, 0x00 };   // 8 literal bytes into a 4-byte block
            Assert.False(Unpak.TryDecompressBlock(src, 0, new byte[4], out _, out _, out _));
        }

        static void U16(List<byte> b, int v) { b.Add((byte)v); b.Add((byte)(v >> 8)); }

        static List<byte> Header(int sprites, int pages, int tpage, int cols, int rows, int free, bool compressed)
        {
            var b = new List<byte>();
            U16(b, sprites); U16(b, pages); U16(b, tpage); U16(b, cols); U16(b, rows); U16(b, free);
            U16(b, compressed ? 1 : 0); U16(b, 0);
            return b;
        }

        /// <summary>A stream that expands to exactly one 0x2000 block of <paramref name="fill"/>: one literal, then
        /// back-references of 255 (32 of them, 8,160 bytes) and one of 31, then the terminator. 1 + 8160 + 31 = 8192.</summary>
        static IEnumerable<byte> FilledBlockStream(byte fill)
        {
            yield return 0x00; yield return fill;
            for (int i = 0; i < 32; i++) { yield return 0xFF; yield return 0xFF; }
            yield return 0xFF; yield return 31;
            yield return 0x80; yield return 0x00;
        }

        // ⭐ REJECTS THE FIXED 0x54 HEADER, which is what made UNPAK "fail" on every compressed sheet. With no
        // sprites and no free rectangles the pixels start at 0x10, and the blocks land a strip at a time.
        [Fact]
        public void CompressedBlocksExpandInOrderIntoTheirStrips()
        {
            var b = Header(0, 1, 0x08, 1, 1, 0, compressed: true);
            for (int i = 0; i < TextureSheet.BlockBytes; i++) b.Add(0x11);   // block 0 is stored raw
            b.AddRange(FilledBlockStream(0xAB));
            b.AddRange(FilledBlockStream(0xCD));
            b.AddRange(FilledBlockStream(0xEF));
            Assert.True(TextureSheet.TryParse(b.ToArray(), out var s, out string err), err);
            Assert.Equal(0x10, s.DataOffset);
            Assert.Equal(0x11, s.Pixels[0]);
            Assert.Equal(0xAB, s.Pixels[64 * 128]);       // strip 1 starts at row 64
            Assert.Equal(0xCD, s.Pixels[128 * 128]);
            Assert.Equal(0xEF, s.Pixels[192 * 128 + 127]);
        }

        // ⭐ THE EXACT-END CHECK HAS TO BE ABLE TO SAY NO. One trailing byte, and the entry is not a sheet.
        [Fact]
        public void ACompressedSheetMustEndOnItsLastStream()
        {
            var b = Header(0, 1, 0x08, 1, 1, 0, compressed: true);
            for (int i = 0; i < TextureSheet.BlockBytes; i++) b.Add(0x11);
            for (int k = 0; k < 3; k++) b.AddRange(FilledBlockStream(0x22));
            b.Add(0x00);
            Assert.False(TextureSheet.TryParse(b.ToArray(), out _, out _));
        }

        [Fact]
        public void ARawSheetMustFillItsEntryExactly()
        {
            var b = Header(0, 1, 0x08, 1, 1, 0, compressed: false);
            b.AddRange(new byte[TextureSheet.PageBytes]);
            Assert.True(TextureSheet.TryParse(b.ToArray(), out _, out string err), err);
            b.Add(0);
            Assert.False(TextureSheet.TryParse(b.ToArray(), out _, out _));
        }

        // ⭐ A SPRITE IS COLOURED BY THE PALETTE ITS RECORD NAMES, READ OUT OF THE SHEET'S OWN PIXELS.
        // Sheet at texpage 8 = VRAM (512, 0). The palette sits at VRAM (528, 10): clut word = 528/16 | 10<<6 =
        // 33 | 640 = 673. Its entry 1 is 0x001F, pure red in BGR555, at sheet byte 10*128 + 16*2 + 2 = 1314.
        // The sprite is 2x1 texels at (0,0), both index 1 (byte 0x11). Red comes out as (248, 0, 0).
        [Fact]
        public void ASpriteIsDrawnWithThePaletteItNames()
        {
            var b = Header(1, 1, 0x08, 1, 1, 0, compressed: false);
            U16(b, 0x0008); U16(b, 673);
            b.AddRange(new byte[] { 0, 0, 2, 1, 0, 0, 0, 0 });           // offset, w=2, h=1, u=0, v=0, flags
            var px = new byte[TextureSheet.PageBytes];
            px[0] = 0x11;
            px[1314] = 0x1F; px[1315] = 0x00;
            b.AddRange(px);
            Assert.True(TextureSheet.TryParse(b.ToArray(), out var s, out string err), err);
            Assert.Equal(1, s.SpritesWithPaletteInside());
            var img = s.RenderSprites();
            Assert.Equal(new byte[] { 248, 0, 0, 255 }, new[] { img.Rgba[0], img.Rgba[1], img.Rgba[2], img.Rgba[3] });
            Assert.Equal(new byte[] { 248, 0, 0, 255 }, new[] { img.Rgba[4], img.Rgba[5], img.Rgba[6], img.Rgba[7] });
        }

        // The same sprite naming a palette at VRAM x 0, which is not inside a sheet at x 512: not counted.
        [Fact]
        public void APaletteOutsideTheSheetIsNotCounted()
        {
            var b = Header(1, 1, 0x08, 1, 1, 0, compressed: false);
            U16(b, 0x0008); U16(b, 10 << 6);
            b.AddRange(new byte[] { 0, 0, 2, 1, 0, 0, 0, 0 });
            b.AddRange(new byte[TextureSheet.PageBytes]);
            Assert.True(TextureSheet.TryParse(b.ToArray(), out var s, out string err), err);
            Assert.Equal(0, s.SpritesWithPaletteInside());
        }
    }
}
