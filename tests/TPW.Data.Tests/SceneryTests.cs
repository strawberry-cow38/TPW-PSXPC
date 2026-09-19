using System;
using System.Collections.Generic;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    /// <summary>The scenery pack and the build list, against the layout 0x80035548 and 0x80057AF0 read. Bytes and
    /// expected values are worked by hand.</summary>
    public class SceneryTests
    {
        static void U16(List<byte> b, int v) { b.Add((byte)v); b.Add((byte)(v >> 8)); }

        /// <summary>One model: scale 256 (x2), 1 texture, 1 triangle, 1 quad, 4 vertices.</summary>
        static List<byte> Model(int verts = 4)
        {
            var b = new List<byte>();
            U16(b, 256); U16(b, 1); U16(b, 1);                       // scale, triangles, quads
            b.Add((byte)verts); b.Add(1); b.Add(10); b.Add(0); b.Add(0); b.Add(0);   // vertices, textures, radius, centre
            U16(b, 0x0005); U16(b, 0x7C40); U16(b, 1);               // texture: tpage, clut, double-sided
            b.AddRange(new byte[] { 0, 1, 2, 0 }); U16(b, 0x0000); U16(b, 0x001F); U16(b, 0x1F00);            // triangle
            U16(b, 0); b.AddRange(new byte[] { 0, 1, 2, 3 }); U16(b, 0x0000); U16(b, 0x001F); U16(b, 0x1F00); U16(b, 0x1F1F); // quad
            for (int i = 0; i < verts; i++) b.AddRange(new byte[] { (byte)(i * 10), 0xFF, 0xFB, (byte)(100 + i) });   // y -1, z -5
            return b;
        }

        static byte[] Pack(params List<byte>[] models)
        {
            var b = new List<byte> { 2, (byte)models.Length };
            int at = 2 + models.Length * 2;
            foreach (var m in models) { U16(b, at); at += m.Count; }
            foreach (var m in models) b.AddRange(m);
            return b.ToArray();
        }

        [Fact]
        public void AModelReadsBackFieldForField()
        {
            Assert.True(SceneryPack.TryParse(Pack(Model(), Model()), out var p, out string err), err);
            Assert.Equal(2, p.Models.Count);
            var m = p.Models[1];
            Assert.Equal(256, m.Scale);
            Assert.Equal(40, m.Radius);
            Assert.True(m.Textures[0].DoubleSided);
            Assert.Equal(0x7C40, m.Textures[0].Clut);
            Assert.Equal(2, m.Polygons.Count);
            Assert.False(m.Polygons[0].IsQuad);
            Assert.True(m.Polygons[1].IsQuad);
            Assert.Equal(3, m.Polygons[1].D);
            Assert.Equal(0x1F1F, m.Polygons[1].Uv3);
            // Vertex 2: bytes (20, 0xFF, -5, 102). × 4 × 256/128 = × 8: (160, -8, -40), shade 102.
            Assert.Equal((160f, -8f, -40f), m.Position(2));
            Assert.Equal(102, m.Vertices[2].Shade);
        }

        // ⭐ THE CHECK THAT MAKES THE VERTEX COUNT A MEASUREMENT: the blocks must end exactly at the next model.
        // A model that claims 3 vertices but carries 4 leaves 4 bytes over, and the reader must refuse it rather
        // than start the next model 4 bytes early. REJECTS: trusting the count byte without the fit.
        [Fact]
        public void AVertexCountThatDoesNotFitIsRefused()
        {
            var bad = Model();
            bad[6] = 3;
            Assert.False(SceneryPack.TryParse(Pack(bad, Model()), out _, out _));
        }

        [Fact]
        public void APolygonNamingAMissingVertexIsRefused()
        {
            var bad = Model(verts: 3);   // the quad still names vertex 3
            Assert.False(SceneryPack.TryParse(Pack(bad), out _, out _));
        }

        // RotMatrixY rows (c, 0, s), (0, 1, 0), (-s, 0, c). One quarter turn: c = 0, s = 1, so (10, 0, 0) goes to
        // x' = 0, z' = -10, and (0, 0, 10) to x' = 10, z' = 0. Then the tile: (3, 256, 5) is (768, 256, 1280).
        // REJECTS: the turn taken the other way ((10, 0, 0) would land at z' = +10) and a tile not shifted by 8.
        [Fact]
        public void AQuarterTurnIsRotMatrixYs()
        {
            var pl = new SceneryPlacement(0, 0, 3, 256, 5, 1);
            Assert.Equal((768f, 256f, 1280f - 10f), pl.Place(10, 0, 0));
            Assert.Equal((768f + 10f, 256f, 1280f), pl.Place(0, 0, 10));
            var half = new SceneryPlacement(0, 0, 0, 0, 0, 2);
            Assert.Equal((-7f, 1f, -9f), half.Place(7, 1, 9));
        }
    }
}
