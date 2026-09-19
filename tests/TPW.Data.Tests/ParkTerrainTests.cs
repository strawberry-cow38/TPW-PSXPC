using System;
using System.Collections.Generic;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    /// <summary>The ground, against the game's terrain routine (0x80012110). Expected values are worked by hand from
    /// its arithmetic, not read back from the code under test.</summary>
    public class ParkTerrainTests
    {
        static SheetSprite Sprite(byte u, byte v, byte w, byte h, byte flags = 0) =>
            new SheetSprite(0x0005, 0x7C00, 0, 0, w, h, u, v, flags, 0);

        static (int U, int V)[] Uvs(ushort ground, SheetSprite sp)
        {
            Span<ushort> uv = stackalloc ushort[4];
            ParkTerrain.GroundUvs(ground, sp, uv);
            var o = new (int, int)[4];
            for (int i = 0; i < 4; i++) o[i] = (uv[i] & 0xFF, uv[i] >> 8);
            return o;
        }

        // A 28x28 sprite at (10, 20): its corners are TL (10,20), TR (38,20), BL (10,48), BR (38,48), and the
        // quad's corners run (x,z), (x+1,z), (x,z+1), (x+1,z+1).
        //   plain        TL TR BL BR
        //   one turn     BL TL BR TR   (the routine's 0x1000 case)
        //   two turns    BR BL TR TL
        //   three turns  TR BR TL BL
        // REJECTS: turning the other way (one and three swapped), and a turn table that ignores bits 12-13.
        [Fact]
        public void TheTurnBitsRotateTheCornersTheGamesWay()
        {
            var sp = Sprite(10, 20, 28, 28);
            (int, int) TL = (10, 20), TR = (38, 20), BL = (10, 48), BR = (38, 48);
            Assert.Equal(new[] { TL, TR, BL, BR }, Uvs(0x0000, sp));
            Assert.Equal(new[] { BL, TL, BR, TR }, Uvs(0x1000, sp));
            Assert.Equal(new[] { BR, BL, TR, TL }, Uvs(0x2000, sp));
            Assert.Equal(new[] { TR, BR, TL, BL }, Uvs(0x3000, sp));
        }

        // Bit 15 swaps u between left and right, bit 14 swaps v between top and bottom.
        // REJECTS: the two bits the other way round, and a mirror that moves whole corners instead of one axis.
        [Fact]
        public void TheMirrorBitsSwapOneAxisEach()
        {
            var sp = Sprite(10, 20, 28, 28);
            (int, int) TL = (10, 20), TR = (38, 20), BL = (10, 48), BR = (38, 48);
            Assert.Equal(new[] { TR, TL, BR, BL }, Uvs(0x8000, sp));
            Assert.Equal(new[] { BL, BR, TL, TR }, Uvs(0x4000, sp));
        }

        // ⭐ An invariant rather than a copy: a half turn IS both mirrors, whatever the table says. A wrong turn
        // table or a wrong mirror breaks this even if its own expected values were copied from the code.
        [Fact]
        public void TwoTurnsEqualBothMirrors()
        {
            var sp = Sprite(3, 100, 28, 17);
            Assert.Equal(Uvs(0x2000, sp), Uvs(0xC000, sp));
            Assert.NotEqual(Uvs(0x0000, sp), Uvs(0x2000, sp));
        }

        // A sprite the packer stored turned (flags != 0) lies H across and W down in the sheet, first corner at the
        // bottom: W 28, H 16 at (10, 20) gives (10,48), (10,20), (26,20), (26,48).
        [Fact]
        public void ASpriteStoredTurnedIsReadTurned()
        {
            var sp = Sprite(10, 20, 28, 16, flags: 1);
            Assert.Equal(new[] { (10, 48), (10, 20), (26, 20), (26, 48) }, Uvs(0x0000, sp));
        }

        /// <summary>A 4x4 map: shade table {0x808080, 0x404040}; tile (x, z) has height 10·x + z, ground sprite 0,
        /// shade index (x + z) &amp; 1, and flags from <paramref name="flags"/>.</summary>
        static ParkMap Map(Func<int, int, byte> flags = null)
        {
            var b = new List<byte>();
            void U32(uint v) { b.Add((byte)v); b.Add((byte)(v >> 8)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 24)); }
            U32(2); U32(0x808080); U32(0x404040); U32(4); U32(4);
            for (int z = 0; z < 4; z++)
                for (int x = 0; x < 4; x++)
                    b.AddRange(new byte[] { 0, (byte)(10 * x + z), 0, 0, 0, 0, (byte)((x + z) & 1), (byte)(flags?.Invoke(x, z) ?? 0) });
            U32(0);
            Assert.True(ParkMap.TryParse(b.ToArray(), out var m, out string err), err);
            return m;
        }

        static readonly SheetSprite[] OneSprite = { new SheetSprite(0x0005, 0x7C00, 0, 0, 28, 28, 0, 0, 0, 0) };

        // Quad (1, 1) takes its corners from tiles (1,1), (2,1), (1,2), (2,2): heights 11, 21, 12, 22 (× 4) and
        // shades 0, 1, 1, 0 → 0x808080, 0x404040, 0x404040, 0x808080.
        // REJECTS: heights taken from the tile's own byte for every corner (a flat quad), and x/z swapped (12 and 21
        // would trade places).
        [Fact]
        public void AQuadTakesEachCornerFromTheTileAtThatCorner()
        {
            Assert.True(ParkTerrain.TryQuadAt(Map(), OneSprite, 1, 1, out var q));
            Assert.Equal(new[] { 44, 84, 48, 88 }, new[] { q.C0.Height, q.C1.Height, q.C2.Height, q.C3.Height });
            Assert.Equal(new uint[] { 0x808080, 0x404040, 0x404040, 0x808080 }, new[] { q.C0.Shade, q.C1.Shade, q.C2.Shade, q.C3.Shade });
        }

        // The game tests z < 1, so row 0 takes BOTH rows of corners from row 0: quad (1, 0) has heights 10, 20, 10,
        // 20 (× 4), not 10, 20, 11, 21.
        [Fact]
        public void RowZeroIsDrawnFlatAsTheGameDrawsIt()
        {
            Assert.True(ParkTerrain.TryQuadAt(Map(), OneSprite, 1, 0, out var q));
            Assert.Equal(new[] { 40, 80, 40, 80 }, new[] { q.C0.Height, q.C1.Height, q.C2.Height, q.C3.Height });
        }

        // Flags bit 0 means no ground; bit 1 alone does not.
        [Fact]
        public void FlagBitZeroLeavesTheTileOpen()
        {
            var m = Map((x, z) => (byte)(x == 2 && z == 2 ? 1 : x == 1 && z == 2 ? 2 : 0));
            Assert.False(ParkTerrain.TryQuadAt(m, OneSprite, 2, 2, out _));
            Assert.True(ParkTerrain.TryQuadAt(m, OneSprite, 1, 2, out _));
            Assert.Equal(9 - 1, ParkTerrain.Build(m, OneSprite).Count);
        }
    }
}
