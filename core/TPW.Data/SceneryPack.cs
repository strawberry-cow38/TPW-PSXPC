using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>A texture a scenery polygon names: a page and palette in VRAM, and whether it is drawn from both sides.</summary>
    public readonly struct SceneryTexture
    {
        public readonly ushort TPage, Clut, Flags;
        public SceneryTexture(ushort tpage, ushort clut, ushort flags) { TPage = tpage; Clut = clut; Flags = flags; }
        /// <summary>Flags bit 0: drawn whichever way it faces. Without it the game drops the polygon when it faces
        /// away (0x80035548 tests the GTE's NCLIP result).</summary>
        public bool DoubleSided => (Flags & 1) != 0;
    }

    /// <summary>One vertex: a signed byte per axis, × 4 before the model's scale, and a grey shade (128 neutral).</summary>
    public readonly struct SceneryVertex
    {
        public readonly sbyte X, Y, Z;
        public readonly byte Shade;
        public SceneryVertex(sbyte x, sbyte y, sbyte z, byte shade) { X = x; Y = y; Z = z; Shade = shade; }
    }

    /// <summary>A triangle or a quad. Corners in the GPU's order; a quad is drawn as 0-1-2 and 1-2-3. UVs are
    /// u | v &lt;&lt; 8 on the texture's page.</summary>
    public readonly struct SceneryPolygon
    {
        public readonly byte A, B, C, D;
        public readonly bool IsQuad;
        public readonly int Texture;
        public readonly ushort Uv0, Uv1, Uv2, Uv3;
        public SceneryPolygon(byte a, byte b, byte c, byte d, bool quad, int texture, ushort uv0, ushort uv1, ushort uv2, ushort uv3)
        { A = a; B = b; C = c; D = d; IsQuad = quad; Texture = texture; Uv0 = uv0; Uv1 = uv1; Uv2 = uv2; Uv3 = uv3; }
        public byte Corner(int i) => i switch { 0 => A, 1 => B, 2 => C, _ => D };
        public ushort Uv(int i) => i switch { 0 => Uv0, 1 => Uv1, 2 => Uv2, _ => Uv3 };
    }

    /// <summary>One static scenery model.</summary>
    public sealed class SceneryModel
    {
        /// <summary>The game multiplies the placement's rotation by Scale / 128 (0x80035358).</summary>
        public int Scale;
        /// <summary>Bounding sphere in model units: the game culls the model with it.</summary>
        public int Radius, CentreX, CentreY, CentreZ;
        public readonly List<SceneryTexture> Textures = new();
        public readonly List<SceneryPolygon> Polygons = new();
        public readonly List<SceneryVertex> Vertices = new();

        /// <summary>A vertex in world units, before rotation and placement: byte × 4 × Scale / 128.</summary>
        public (float X, float Y, float Z) Position(int i)
        {
            var v = Vertices[i];
            float k = 4f * Scale / 128f;
            return (v.X * k, v.Y * k, v.Z * k);
        }
    }

    /// <summary>A world's scenery models: the entry each map is listed with in the world table (#205 for the
    /// jungle's maps, #118, #36, #359).
    ///
    /// ⭐ FROM THE GAME'S DRAWING CODE, 0x80035358 → 0x80035548 (0x80035D04 is the same with depth fog):
    /// <code>
    ///   u8 2, u8 model count, u16 offset per model (from the start of the entry)
    ///   model: u16 scale; u16 triangles; u16 quads; u8 vertices; u8 textures; u8 radius/4; s8 centre x,y,z /4
    ///          textures × 6:  u16 tpage, u16 clut, u16 flags (bit 0 double-sided)
    ///          triangles × 10: u8 a, b, c, texture; u16 uv0, uv1, uv2          (drawn as POLY_GT3, 0x34)
    ///          quads × 14:     u16 texture; u8 a, b, c, d; u16 uv0..uv3        (drawn as POLY_GT4, 0x3C)
    ///          vertices × 4:   s8 x, y, z (× 4), u8 shade (the Gouraud grey of that corner)
    /// </code>
    /// ✅ All 80 models in the four packs parse with every vertex block ending exactly at the next model's offset
    /// (or the entry's end) and every polygon's corners inside its vertex count; the vertex count byte had no
    /// other reader to confirm it, and that fit is what does.
    ///
    /// Their textures come from the world's GROUND sheet: in the jungle pack, 920 of 1,073 faces sit inside a
    /// sprite of #258, with that sprite's page and palette, and in no other sheet; the rest use the same pages.</summary>
    public sealed class SceneryPack
    {
        public readonly List<SceneryModel> Models = new();

        public static bool TryParse(byte[] d, out SceneryPack pack, out string error)
        {
            pack = null; error = null;
            if (d == null || d.Length < 4) { error = "too short"; return false; }
            if (d[0] != 2) { error = $"type byte {d[0]}, expected 2"; return false; }
            int n = d[1];
            if (n == 0 || 2 + n * 2 > d.Length) { error = $"model count {n} does not fit"; return false; }
            var offsets = new int[n];
            for (int i = 0; i < n; i++) offsets[i] = BitConverter.ToUInt16(d, 2 + i * 2);

            var p = new SceneryPack();
            for (int i = 0; i < n; i++)
            {
                int o = offsets[i], end = i + 1 < n ? offsets[i + 1] : d.Length;
                if (o < 2 + n * 2 || end > d.Length || o + 12 > end) { error = $"model {i}: offset {o} out of order or past the end"; return false; }
                var m = new SceneryModel
                {
                    Scale = BitConverter.ToUInt16(d, o),
                    Radius = d[o + 8] * 4,
                    CentreX = (sbyte)d[o + 9] * 4, CentreY = (sbyte)d[o + 10] * 4, CentreZ = (sbyte)d[o + 11] * 4,
                };
                int tris = BitConverter.ToUInt16(d, o + 2), quads = BitConverter.ToUInt16(d, o + 4);
                int verts = d[o + 6], texs = d[o + 7];
                int at = o + 12;
                if (at + texs * 6 + tris * 10 + quads * 14 + verts * 4 != end)
                { error = $"model {i}: {texs} textures, {tris} triangles, {quads} quads and {verts} vertices do not end at {end}"; return false; }

                for (int t = 0; t < texs; t++, at += 6)
                    m.Textures.Add(new SceneryTexture(BitConverter.ToUInt16(d, at), BitConverter.ToUInt16(d, at + 2), BitConverter.ToUInt16(d, at + 4)));
                for (int t = 0; t < tris; t++, at += 10)
                    m.Polygons.Add(new SceneryPolygon(d[at], d[at + 1], d[at + 2], 0, false, d[at + 3],
                        BitConverter.ToUInt16(d, at + 4), BitConverter.ToUInt16(d, at + 6), BitConverter.ToUInt16(d, at + 8), 0));
                for (int q = 0; q < quads; q++, at += 14)
                    m.Polygons.Add(new SceneryPolygon(d[at + 2], d[at + 3], d[at + 4], d[at + 5], true, BitConverter.ToUInt16(d, at),
                        BitConverter.ToUInt16(d, at + 6), BitConverter.ToUInt16(d, at + 8), BitConverter.ToUInt16(d, at + 10), BitConverter.ToUInt16(d, at + 12)));
                for (int v = 0; v < verts; v++, at += 4)
                    m.Vertices.Add(new SceneryVertex((sbyte)d[at], (sbyte)d[at + 1], (sbyte)d[at + 2], d[at + 3]));

                foreach (var poly in m.Polygons)
                {
                    int corners = poly.IsQuad ? 4 : 3;
                    for (int c = 0; c < corners; c++)
                        if (poly.Corner(c) >= verts) { error = $"model {i}: a polygon names vertex {poly.Corner(c)} of {verts}"; return false; }
                    if (poly.Texture >= texs) { error = $"model {i}: a polygon names texture {poly.Texture} of {texs}"; return false; }
                }
                p.Models.Add(m);
            }
            pack = p;
            return true;
        }
    }

    /// <summary>One placed scenery model, from the map's build list.</summary>
    public readonly struct SceneryPlacement
    {
        /// <summary>The model in the world's <see cref="SceneryPack"/>: word 0's low 24 bits.</summary>
        public readonly int Model;
        /// <summary>Word 0's top byte. 0x8005439C searches the list for its bits 3, 2 and 1 (the word's bits 27,
        /// 26, 25): records that stand for something, not yet named.</summary>
        public readonly byte Flags;
        /// <summary>Tile coordinates (the game shifts them left 8) and a height in world units (it does not).</summary>
        public readonly int X, Y, Z;
        /// <summary>Quarter turns about the vertical: the game rotates by this × 1024 (4096 = a full turn).</summary>
        public readonly int Turns;

        public SceneryPlacement(int model, byte flags, int x, int y, int z, int turns)
        { Model = model; Flags = flags; X = x; Y = y; Z = z; Turns = turns; }

        /// <summary>Where a model-space point lands, in world units: rotated as PsyQ's RotMatrixY does, rows
        /// (c, 0, s), (0, 1, 0), (-s, 0, c) — read off 0x800CAF60 for a Y-only angle — then moved to the tile.</summary>
        public (float X, float Y, float Z) Place(float x, float y, float z)
        {
            int t = Turns & 3;
            int c = t == 0 ? 1 : t == 2 ? -1 : 0, s = t == 1 ? 1 : t == 3 ? -1 : 0;
            return (c * x + s * z + X * ParkTerrain.TileUnits, y + Y, -s * x + c * z + Z * ParkTerrain.TileUnits);
        }
    }
}
