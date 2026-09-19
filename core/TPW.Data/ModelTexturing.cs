using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>A mesh's textures, gathered into one atlas: every (sheet, page, palette) the mesh draws with,
    /// rendered as a 256x256 tile, plus which tile each face samples.</summary>
    public sealed class MeshTextures
    {
        public TpwImage Atlas;
        public int TilesX, TilesY;
        /// <summary>Per face: the tile it samples, or -1 when no sheet could be found for it.</summary>
        public int[] FaceTile = Array.Empty<int>();
        /// <summary>Faces whose UVs sit inside a sprite of exactly one sheet with the face's page and palette.</summary>
        public int Unique;
        /// <summary>Faces that matched sprites in several sheets (worlds share VRAM) and took the mesh's majority.</summary>
        public int Ambiguous;
        /// <summary>Faces inside no sprite that still found a sheet holding their palette on their page.</summary>
        public int Loose;
        public int Unmatched;
        /// <summary>The sheet (archive entry) most faces came from.</summary>
        public int MainSheetEntry = -1;
    }

    /// <summary>Which texture each model face wears, and the pixels for it.
    ///
    /// ⭐ A FACE NAMES ITS PAGE, ITS PALETTE AND ITS UVs, AND THE SHEETS' SPRITE TABLES SAY WHICH SHEET THAT IS.
    /// Measured over every model on the disc: 53,606 of 54,619 faces (98.1%) have UVs that sit inside a sprite
    /// with the face's own page and palette, and for 51,838 (94.9%) that sprite exists in exactly one sheet.
    /// That matters because sheets for different worlds are uploaded to the SAME VRAM and reuse the same palette
    /// slots, so "which sheet is at this address" has several answers. "Which sheet has a sprite HERE with THIS
    /// palette" usually has one.
    ///
    /// The rest are resolved by the mesh's majority sheet, which is what a mesh belonging to one world would
    /// expect, and counted separately so the fallback cannot hide inside the total.
    ///
    /// ⭐ PSX COLOUR: the GPU modulates a texel by the vertex colour with 128 as neutral, so the shader should
    /// multiply by 2 × COLOR. Palette colour 0 is transparent. Both are the GPU's rules, not choices.</summary>
    public static class ModelTexturing
    {
        public const int Tile = 256;

        public static MeshTextures Build(Mesh m, IReadOnlyList<(GazEntry Entry, TextureSheet Sheet)> sheets)
        {
            var result = new MeshTextures { FaceTile = new int[m.Faces.Count] };
            if (sheets == null || sheets.Count == 0) { Array.Fill(result.FaceTile, -1); result.Unmatched = m.Faces.Count; return result; }

            // Pass 1: the sheets each face could come from.
            var candidates = new List<int>[m.Faces.Count];
            var votes = new int[sheets.Count];
            for (int f = 0; f < m.Faces.Count; f++)
            {
                candidates[f] = SpriteCandidates(m.Faces[f], sheets);
                if (candidates[f].Count == 1) votes[candidates[f][0]]++;
            }
            int major = -1;
            for (int k = 0; k < votes.Length; k++) if (votes[k] > 0 && (major < 0 || votes[k] > votes[major])) major = k;
            if (major >= 0) result.MainSheetEntry = sheets[major].Entry.Index;

            // Pass 2: one sheet per face, then one tile per (sheet, page, palette, depth).
            var tiles = new List<(int Sheet, ushort TPage, ushort Clut)>();
            var tileIndex = new Dictionary<(int, ushort, ushort), int>();
            for (int f = 0; f < m.Faces.Count; f++)
            {
                var face = m.Faces[f];
                var c = candidates[f];
                int pick;
                if (c.Count == 1) { pick = c[0]; result.Unique++; }
                else if (c.Count > 1) { pick = c.Contains(major) ? major : c[0]; result.Ambiguous++; }
                else
                {
                    var loose = LooseCandidates(face, sheets);
                    if (loose.Count == 0) { result.FaceTile[f] = -1; result.Unmatched++; continue; }
                    pick = loose.Contains(major) ? major : loose[0];
                    result.Loose++;
                }
                var key = (pick, (ushort)(face.TPage & 0x1FF), face.Clut);
                if (!tileIndex.TryGetValue(key, out int t)) { t = tiles.Count; tiles.Add(key); tileIndex[key] = t; }
                result.FaceTile[f] = t;
            }

            // The atlas: tiles in a near-square grid.
            int n = Math.Max(1, tiles.Count);
            result.TilesX = (int)Math.Ceiling(Math.Sqrt(n));
            result.TilesY = (n + result.TilesX - 1) / result.TilesX;
            int w = result.TilesX * Tile, h = result.TilesY * Tile;
            var rgba = new byte[w * h * 4];
            for (int t = 0; t < tiles.Count; t++)
            {
                var (sheet, tpage, clut) = tiles[t];
                sheets[sheet].Sheet.RenderPage(tpage, clut, rgba, w, (t % result.TilesX) * Tile, (t / result.TilesX) * Tile);
            }
            result.Atlas = new TpwImage { Width = w, Height = h, Rgba = rgba, Source = $"{tiles.Count} textures" };
            return result;
        }

        /// <summary>Sheets with a sprite on the face's page, with the face's palette, whose rectangle holds the
        /// face's UVs.</summary>
        static List<int> SpriteCandidates(MeshFace f, IReadOnlyList<(GazEntry Entry, TextureSheet Sheet)> sheets)
        {
            int umin = Math.Min(f.U0, Math.Min(f.U1, f.U2)), umax = Math.Max(f.U0, Math.Max(f.U1, f.U2));
            int vmin = Math.Min(f.V0, Math.Min(f.V1, f.V2)), vmax = Math.Max(f.V0, Math.Max(f.V1, f.V2));
            var outp = new List<int>(2);
            for (int k = 0; k < sheets.Count; k++)
                foreach (var sp in sheets[k].Sheet.Sprites)
                    if (sp.Clut == f.Clut && (sp.TPage & 0x1F) == (f.TPage & 0x1F) &&
                        umin >= sp.U && umax <= sp.U + sp.W && vmin >= sp.V && vmax <= sp.V + sp.H)
                    { outp.Add(k); break; }
            return outp;
        }

        /// <summary>Sheets that hold the face's page and whose sprites use the face's palette somewhere.</summary>
        static List<int> LooseCandidates(MeshFace f, IReadOnlyList<(GazEntry Entry, TextureSheet Sheet)> sheets)
        {
            var (px, py) = f.TPageOrigin;
            var outp = new List<int>(2);
            for (int k = 0; k < sheets.Count; k++)
            {
                var s = sheets[k].Sheet;
                if (!s.Contains(px, py)) continue;
                foreach (var sp in s.Sprites) if (sp.Clut == f.Clut) { outp.Add(k); break; }
            }
            return outp;
        }
    }
}
