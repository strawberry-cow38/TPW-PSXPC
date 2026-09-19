using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>One corner of a ground quad.</summary>
    public struct GroundCorner
    {
        /// <summary>World units: the corner tile's byte +1 × 4.</summary>
        public int Height;
        /// <summary>0x00BBGGRR from the map's shade table, 128 neutral: the GPU multiplies the texel by it.</summary>
        public uint Shade;
        /// <summary>The texel on the sprite's page this corner is pinned to.</summary>
        public byte U, V;
    }

    /// <summary>One ground quad, as the game's terrain routine emits it: a textured, Gouraud-shaded POLY_GT4
    /// (GPU command 0x3C) whose corners are, in order, (x, z), (x+1, z), (x, z+1), (x+1, z+1) in tiles.</summary>
    public struct GroundQuad
    {
        public int X, Z;
        /// <summary>The sprite in the world's ground sheet, and the page and palette it names.</summary>
        public int Sprite;
        public ushort TPage, Clut;
        public GroundCorner C0, C1, C2, C3;
        public GroundCorner this[int i] => i switch { 0 => C0, 1 => C1, 2 => C2, _ => C3 };
    }

    /// <summary>The park's ground, built the way the game builds it.
    ///
    /// ⭐ FROM THE GAME'S OWN TERRAIN ROUTINE, 0x80012110: hand-written, it moves the stack onto the scratchpad and
    /// walks the tiles under the camera's view (0x800567F0 scan-converts the view's footprint on the map into
    /// rows) emitting one POLY_GT4 per tile. Per tile (x, z) it:
    /// <list type="bullet">
    /// <item>skips the tile when its flags byte (+7) has bit 0 set;</item>
    /// <item>puts the corners at (x·256, height, z·256), each corner's height being byte +1 × 4 of the tile AT that
    /// corner, so the quad blends the heights of tiles (x, z), (x+1, z), (x, z+1), (x+1, z+1);</item>
    /// <item>colours each corner from the map's shade table, indexed by that corner tile's +6 &amp; 0x3F;</item>
    /// <item>textures the quad with sprite +4 &amp; 0xFFF of the world's ground sheet (whose sprite table the park
    /// loader, 0x800588D0, keeps at gp+0x12A8), mirrored by +4 bits 15 (across) and 14 (down) and turned by
    /// bits 12-13.</item>
    /// </list>
    /// Everything here is that routine's arithmetic, including its edge rules; see <see cref="TryQuadAt"/>.
    ///
    /// ⚠ THE FRAME IS LEFT-HANDED, like the models': x across, y up, z the other way from a right-handed frame.
    /// A viewer in a right-handed frame negates z (the models were mirrored until theirs was).
    ///
    /// Not here yet: the routine the game switches to when the camera is high (0x80059DDC, with depth fog), and
    /// whatever draws the tiles this one skips.</summary>
    public static class ParkTerrain
    {
        public const int TileUnits = 256;
        public const int HeightScale = 4;

        /// <summary>The texel each corner of a tile is pinned to, as 0x80012110 computes it: words of u | v &lt;&lt; 8 in
        /// corner order (x, z), (x+1, z), (x, z+1), (x+1, z+1).
        ///
        /// The words are added as the game adds them, 16 bits at a time, so a u that ran past 255 would carry into
        /// v. No ground sprite on the disc reaches the edge of its page, so it never does.</summary>
        public static void GroundUvs(ushort ground, in SheetSprite sp, Span<ushort> uv)
        {
            // The sprite's four corners, top-left, top-right, bottom-left, bottom-right as the sheet holds them.
            ushort tl, tr, bl, br;
            if (sp.Flags == 0)
            {
                tl = (ushort)(sp.V << 8 | sp.U);
                tr = (ushort)(tl + sp.W);
                bl = (ushort)(((sp.V + sp.H) & 0xFF) << 8 | sp.U);
                br = (ushort)(bl + sp.W);
            }
            else
            {
                // A sprite the packer stored TURNED: H texels across and W down, its first corner at the bottom.
                tl = (ushort)(((sp.V + sp.W) & 0xFF) << 8 | sp.U);
                tr = (ushort)(sp.V << 8 | sp.U);
                bl = (ushort)(tr + sp.H);
                br = (ushort)(tl + sp.H);
            }

            if ((ground & 0x8000) != 0)
            {
                // Mirror across: swap the u of the top pair and of the bottom pair.
                ushort a = tl, b = bl;
                tl = (ushort)((tr & 0xFF) | (tl & 0xFF00));
                tr = (ushort)((a & 0xFF) | (tr & 0xFF00));
                bl = (ushort)((br & 0xFF) | (bl & 0xFF00));
                br = (ushort)((b & 0xFF) | (br & 0xFF00));
            }
            if ((ground & 0x4000) != 0)
            {
                // Mirror down: swap the v of the left pair and of the right pair.
                ushort a = tl, b = tr;
                tl = (ushort)((bl & 0xFF00) | (tl & 0xFF));
                bl = (ushort)((a & 0xFF00) | (bl & 0xFF));
                tr = (ushort)((br & 0xFF00) | (tr & 0xFF));
                br = (ushort)((b & 0xFF00) | (br & 0xFF));
            }

            switch ((ground >> 12) & 3)
            {
                case 0: uv[0] = tl; uv[1] = tr; uv[2] = bl; uv[3] = br; break;
                case 1: uv[0] = bl; uv[1] = tl; uv[2] = br; uv[3] = tr; break;
                case 2: uv[0] = br; uv[1] = bl; uv[2] = tr; uv[3] = tl; break;
                default: uv[0] = tr; uv[1] = br; uv[2] = tl; uv[3] = bl; break;
            }
        }

        /// <summary>The quad the game draws for tile (x, z). False where it draws none: the tile's flags bit 0, or a
        /// sprite number the sheet does not have.
        ///
        /// ⚠ THE EDGE RULES ARE THE GAME'S, ODD AS THEY ARE. Row 0 takes both rows of corners from row 0 (the test is
        /// z &lt; 1, not z &lt; 0), so it is always drawn flat. A column or row past the far edge is drawn flat from
        /// the last-but-one; a column left of 0 flat from column 0. And the right-hand corners' shades always come
        /// from the column after the one the quad is textured from, even where their heights were clamped. None of
        /// this shows inside the map except row 0, which is flat on every shipped map anyway.</summary>
        public static bool TryQuadAt(ParkMap map, IReadOnlyList<SheetSprite> sprites, int x, int z, out GroundQuad q)
        {
            q = default;
            int w = map.Width, h = map.Height;
            int r0, r1;
            if (z < 1) { r0 = 0; r1 = 0; }
            else { r0 = z >= h - 1 ? h - 2 : z; r1 = r0 + 1; }
            int c0, c1;
            if (x < 0) { c0 = 0; c1 = 0; }
            else if (x < w - 1) { c0 = x; c1 = x + 1; }
            else { c0 = w - 2; c1 = w - 2; }

            var t = map[c0, r0];
            if (t.NoGround) return false;
            int s = t.GroundSprite;
            if (s >= sprites.Count) return false;
            var sp = sprites[s];

            Span<ushort> uv = stackalloc ushort[4];
            GroundUvs(t.Ground, sp, uv);
            uint Shade(int cx, int cz)
            {
                int i = map[cx, cz].ShadeIndex;
                return i < map.ShadeTable.Length ? map.ShadeTable[i] & 0xFFFFFF : 0x808080;
            }
            int sc = c0 + 1;        // the game reads these at +0x0E: the next tile along, clamped heights or not
            q = new GroundQuad
            {
                X = x, Z = z, Sprite = s, TPage = sp.TPage, Clut = sp.Clut,
                C0 = new GroundCorner { Height = t.HeightUnits, Shade = Shade(c0, r0), U = (byte)uv[0], V = (byte)(uv[0] >> 8) },
                C1 = new GroundCorner { Height = map[c1, r0].HeightUnits, Shade = Shade(sc, r0), U = (byte)uv[1], V = (byte)(uv[1] >> 8) },
                C2 = new GroundCorner { Height = map[c0, r1].HeightUnits, Shade = Shade(c0, r1), U = (byte)uv[2], V = (byte)(uv[2] >> 8) },
                C3 = new GroundCorner { Height = map[c1, r1].HeightUnits, Shade = Shade(sc, r1), U = (byte)uv[3], V = (byte)(uv[3] >> 8) },
            };
            return true;
        }

        /// <summary>Every quad inside the map: tiles 0..w-2 by 0..h-2, the last row and column being corners only;
        /// plus <paramref name="border"/> tiles of the ground the game draws PAST the edge.
        ///
        /// ⭐ THE GROUND DOES NOT STOP AT THE MAP. The routine draws whatever tiles the view covers and clamps the
        /// ones outside (see <see cref="TryQuadAt"/>), so beyond the edge the ground goes on as copies of the edge
        /// tiles: tinyclaw's console screenshot shows grass continuing past the jungle's perimeter hedge, which
        /// stands on the map's own edge. How far it goes on is the view's reach, not a map property.</summary>
        public static List<GroundQuad> Build(ParkMap map, IReadOnlyList<SheetSprite> sprites, int border = 0)
        {
            var quads = new List<GroundQuad>((map.Width - 1 + 2 * border) * (map.Height - 1 + 2 * border));
            for (int z = -border; z < map.Height - 1 + border; z++)
                for (int x = -border; x < map.Width - 1 + border; x++)
                    if (TryQuadAt(map, sprites, x, z, out var q)) quads.Add(q);
            return quads;
        }

        /// <summary>The ground seen from straight above, <paramref name="texelsPerTile"/> pixels a tile: every quad's
        /// texture pinned and interpolated as the GPU does (two triangles split on the (x+1, z)-(x, z+1) diagonal),
        /// times its interpolated shade with 128 as 1. Tiles the game skips stay transparent.
        ///
        /// ⚠ z runs UP the picture, as it does in the game's left-handed frame seen from above. Drawn with z down,
        /// the map comes out mirrored.</summary>
        public static TpwImage RenderTopDown(ParkMap map, TextureSheet sheet, int texelsPerTile = 28)
        {
            int T = texelsPerTile, cols = map.Width - 1, rows = map.Height - 1;
            int W = cols * T, H = rows * T;
            var rgba = new byte[W * H * 4];
            foreach (var q in Build(map, sheet.Sprites))
                for (int j = 0; j < T; j++)
                    for (int i = 0; i < T; i++)
                    {
                        float s = (i + 0.5f) / T, t = (j + 0.5f) / T;
                        float w0, w1, w2, w3;
                        if (s + t <= 1f) { w0 = 1 - s - t; w1 = s; w2 = t; w3 = 0; }
                        else { w0 = 0; w1 = 1 - t; w2 = 1 - s; w3 = s + t - 1; }
                        float u = w0 * q.C0.U + w1 * q.C1.U + w2 * q.C2.U + w3 * q.C3.U;
                        float v = w0 * q.C0.V + w1 * q.C1.V + w2 * q.C2.V + w3 * q.C3.V;
                        int c = sheet.Texel(q.TPage, q.Clut, (int)u, (int)v);
                        if (c <= 0) continue;
                        float Ch(int shift) =>
                            w0 * ((q.C0.Shade >> shift) & 0xFF) + w1 * ((q.C1.Shade >> shift) & 0xFF) +
                            w2 * ((q.C2.Shade >> shift) & 0xFF) + w3 * ((q.C3.Shade >> shift) & 0xFF);
                        int px = q.X * T + i, py = H - 1 - (q.Z * T + j);
                        int o = (py * W + px) * 4;
                        rgba[o] = (byte)Math.Min(255f, ((c & 31) << 3) * Ch(0) / 128f);
                        rgba[o + 1] = (byte)Math.Min(255f, (((c >> 5) & 31) << 3) * Ch(8) / 128f);
                        rgba[o + 2] = (byte)Math.Min(255f, (((c >> 10) & 31) << 3) * Ch(16) / 128f);
                        rgba[o + 3] = 255;
                    }
            return new TpwImage { Width = W, Height = H, Rgba = rgba, Source = $"{map.Width}x{map.Height} ground" };
        }
    }
}
