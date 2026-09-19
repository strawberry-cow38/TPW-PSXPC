using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>Texture pages of one sheet, each through one palette, packed into a single image: what a renderer
    /// needs to draw polygons that name a (page, palette) and a texel on that page, the way the GPU does.
    ///
    /// A polygon's texel (u, v) on page P with palette C is at <see cref="Origin"/>(P, C) + (u, v). Each tile is a
    /// whole 256x256 page at VRAM addressing (<see cref="TextureSheet.RenderPage"/>), so any UV the GPU could
    /// fetch lands on the right texel, sprite boundaries or not. Palette colour 0 is alpha 0.</summary>
    public sealed class PageAtlas
    {
        public TpwImage Image;
        readonly Dictionary<(ushort, ushort), (int X, int Y)> _origin = new();

        /// <summary>The tpage bits that change what is fetched: page x (0-3), page y (4), depth (7-8). Bits 5-6
        /// are the blend mode, which does not.</summary>
        public static ushort PageKey(ushort tpage) => (ushort)(tpage & 0x19F);

        public static PageAtlas Build(TextureSheet sheet, IEnumerable<(ushort TPage, ushort Clut)> uses)
        {
            var keys = new List<(ushort, ushort)>();
            var seen = new HashSet<(ushort, ushort)>();
            foreach (var (tp, cl) in uses)
            {
                var k = (PageKey(tp), cl);
                if (seen.Add(k)) keys.Add(k);
            }
            int t = TextureSheet.PageTexels;
            int cols = (int)Math.Ceiling(Math.Sqrt(Math.Max(1, keys.Count))), rows = Math.Max(1, (keys.Count + cols - 1) / cols);
            var atlas = new PageAtlas();
            var rgba = new byte[cols * t * rows * t * 4];
            for (int i = 0; i < keys.Count; i++)
            {
                int ox = (i % cols) * t, oy = (i / cols) * t;
                sheet.RenderPage(keys[i].Item1, keys[i].Item2, rgba, cols * t, ox, oy);
                atlas._origin[keys[i]] = (ox, oy);
            }
            atlas.Image = new TpwImage { Width = cols * t, Height = rows * t, Rgba = rgba, Source = $"{keys.Count} pages" };
            return atlas;
        }

        public bool TryOrigin(ushort tpage, ushort clut, out int x, out int y)
        {
            if (_origin.TryGetValue((PageKey(tpage), clut), out var o)) { x = o.X; y = o.Y; return true; }
            x = y = 0;
            return false;
        }
    }
}
