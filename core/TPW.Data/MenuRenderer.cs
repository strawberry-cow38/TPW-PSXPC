using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>Draws the main menu into a 320x256 RGBA buffer from the real disc art: the captured
    /// backdrop quads of <see cref="MenuLayout"/> plus text set in the game's own font.
    ///
    /// Engine-free on purpose. Everything here is arithmetic over byte arrays, so the UV mapping, the
    /// additive blend and the glyph lookup can be tested without a window -- and the failure this code
    /// is most likely to have (a transposed UV, a sprite index off by one) is exactly the kind that a
    /// screenshot shows and a "does it run" test does not.</summary>
    public static class MenuRenderer
    {

        public const int W = MenuLayout.ScreenWidth, H = MenuLayout.ScreenHeight;

        /// <summary>Everything the renderer needs, built once.</summary>
        public sealed class Prepared
        {
            public PageAtlas Atlas;
            public TextureSheet Sheet;
            /// <summary>Sprite index of the first glyph, for bounds checks.</summary>
            public int SpriteCount;
        }

        /// <summary>Build the page atlas for the backdrop AND every glyph, so text and scenery come from
        /// one upload rather than one per draw.</summary>
        public static Prepared Prepare(TextureSheet sheet)
        {
            if (sheet == null) return null;
            var uses = new List<(ushort, ushort)>();
            foreach (var q in MenuLayout.Backdrop) uses.Add((q.TPage, q.Clut));
            foreach (var kv in MenuLayout.Glyph)
                if (kv.Value >= 0 && kv.Value < sheet.Sprites.Count)
                {
                    var sp = sheet.Sprites[kv.Value];
                    uses.Add((sp.TPage, sp.Clut));
                }
            return new Prepared { Atlas = PageAtlas.Build(sheet, uses), Sheet = sheet, SpriteCount = sheet.Sprites.Count };
        }

        /// <summary>A fresh transparent frame.</summary>
        public static byte[] NewFrame() => new byte[W * H * 4];

        /// <summary>The highlight glow's own texture page. It is the one backdrop piece that MOVES --
        /// it sits under whichever item is selected -- so it is drawn separately, at an offset, rather
        /// than baked into the static layer where it would be frozen on the captured frame's row.</summary>
        const ushort GlowPage = 56;

        /// <summary>The captured glow's top edge, so the offset can be measured from it.</summary>
        public static int GlowY
        {
            get
            {
                int y = int.MaxValue;
                foreach (var q in MenuLayout.Backdrop)
                    if (q.TPage == GlowPage) y = Math.Min(y, Math.Min(Math.Min(q.Y0, q.Y1), Math.Min(q.Y2, q.Y3)));
                return y == int.MaxValue ? 0 : y;
            }
        }

        /// <summary>Draw the static backdrop: curtains, valance, floor, logo. NOT the glow.</summary>
        public static void DrawBackdrop(Prepared p, byte[] dst)
        {
            if (p == null) return;
            // ⚠ BACK TO FRONT. A PSX ordering table is built by PREPENDING, so the display list as
            // walked from its head runs FRONT to BACK -- replaying it in that order paints the floor
            // over the curtains, the curtains over the logo, and the highlight glow over the text that
            // is supposed to sit inside it. All three were spotted at a glance by someone who knows
            // what the screen should look like, and all three are the one reversal.
            for (int i = MenuLayout.Backdrop.Length - 1; i >= 0; i--)
                if (MenuLayout.Backdrop[i].TPage != GlowPage) DrawQuad(p, dst, MenuLayout.Backdrop[i], 0);
        }

        /// <summary>One textured quad.
        ///
        /// ⚠ THE UVs CAN BE TRANSPOSED, not merely offset. Several curtain and floor sprites are stored
        /// ROTATED in the sheet -- their quad has u constant down one edge and v constant along the
        /// other -- so the mapping has to interpolate the corner UVs bilinearly rather than assume
        /// u tracks x. Assuming it does produces a picture that is recognisably the right art in the
        /// right place and subtly wrong, which is the hardest kind to notice.</summary>
        /// <summary>The glow, shifted <paramref name="dy"/> rows down from where it was captured.</summary>
        public static void DrawHighlight(Prepared p, byte[] dst, int dy)
        {
            if (p == null) return;
            for (int i = MenuLayout.Backdrop.Length - 1; i >= 0; i--)
                if (MenuLayout.Backdrop[i].TPage == GlowPage) DrawQuad(p, dst, MenuLayout.Backdrop[i], dy);
        }

        static void DrawQuad(Prepared p, byte[] dst, in ScreenQuad q, int dy)
        {
            if (!p.Atlas.TryOrigin(q.TPage, q.Clut, out int ax, out int ay)) return;
            int x0 = Math.Min(Math.Min(q.X0, q.X1), Math.Min(q.X2, q.X3));
            int x1 = Math.Max(Math.Max(q.X0, q.X1), Math.Max(q.X2, q.X3));
            int y0 = Math.Min(Math.Min(q.Y0, q.Y1), Math.Min(q.Y2, q.Y3)) + dy;
            int y1 = Math.Max(Math.Max(q.Y0, q.Y1), Math.Max(q.Y2, q.Y3)) + dy;
            if (x1 <= x0 || y1 <= y0) return;

            var src = p.Atlas.Image;
            for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++)
            {
                float t = (y - y0) / (float)(y1 - y0);
                for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++)
                {
                    float s = (x - x0) / (float)(x1 - x0);
                    // Bilinear over the four corner UVs: corners are (0,0) (1,0) (0,1) (1,1) in the
                    // order the display list stores them.
                    float u = (1 - s) * (1 - t) * q.U0 + s * (1 - t) * q.U1 + (1 - s) * t * q.U2 + s * t * q.U3;
                    float v = (1 - s) * (1 - t) * q.V0 + s * (1 - t) * q.V1 + (1 - s) * t * q.V2 + s * t * q.V3;
                    Blend(dst, (y * W + x) * 4, src, (( ay + (int)(v + 0.5f)) * src.Width + ax + (int)(u + 0.5f)) * 4, q.Additive);
                }
            }
        }

        /// <summary>⚠ INDEX 0 OF A PSX PALETTE IS TRANSPARENT, not black, and every one of these sprites
        /// relies on it -- the curtains are cut-outs. RenderPage already writes alpha 0 there; this just
        /// has to honour it instead of copying the pixel regardless.</summary>
        static void Blend(byte[] dst, int d, TpwImage src, int s, bool additive)
        {
            if (s < 0 || s + 3 >= src.Rgba.Length || d + 3 >= dst.Length) return;
            byte a = src.Rgba[s + 3];
            if (a == 0) return;
            if (additive)
            {
                dst[d]     = (byte)Math.Min(255, dst[d]     + src.Rgba[s]);
                dst[d + 1] = (byte)Math.Min(255, dst[d + 1] + src.Rgba[s + 1]);
                dst[d + 2] = (byte)Math.Min(255, dst[d + 2] + src.Rgba[s + 2]);
                dst[d + 3] = 255;
                return;
            }
            dst[d] = src.Rgba[s]; dst[d + 1] = src.Rgba[s + 1]; dst[d + 2] = src.Rgba[s + 2]; dst[d + 3] = 255;
        }

        /// <summary>Width of a string in the menu font, for centring.</summary>
        public static int MeasureText(Prepared p, string text)
        {
            int w = 0;
            foreach (char c in text) w += GlyphWidth(p, c);
            return w;
        }

        static int GlyphWidth(Prepared p, char c)
        {
            if (c == ' ') return 8;
            if (p == null || !MenuLayout.Glyph.TryGetValue(c, out int i) || i >= p.SpriteCount) return 8;
            var sp = p.Sheet.Sprites[i];
            // ⚠ A ROTATED GLYPH'S ADVANCE IS ITS H, not its W -- the stored rect is the turned image,
            // so the drawn width is the stored height. Using W regardless overlaps the next letter.
            return ((sp.Flags & 1) != 0 ? sp.H : sp.W) + 1;
        }

        /// <summary>Draw a string in the game's own font, top-left at (x, y). Returns the x it ended at.
        /// Characters with no glyph advance as a space rather than being dropped, so a missing
        /// punctuation mark shows as a gap instead of silently closing up the text.</summary>
        public static int DrawText(Prepared p, byte[] dst, int x, int y, string text, bool additive = false)
        {
            if (p == null) return x;
            foreach (char c in text)
            {
                if (c != ' ' && MenuLayout.Glyph.TryGetValue(c, out int i) && i < p.SpriteCount)
                {
                    var sp = p.Sheet.Sprites[i];
                    if (p.Atlas.TryOrigin(sp.TPage, sp.Clut, out int ax, out int ay))
                    {
                        var src = p.Atlas.Image;
                        // ⚠ BIT 0 OF Flags MEANS THE SPRITE IS STORED TURNED 90 DEGREES. Ignoring it
                        // does not produce an obviously broken glyph -- it produces a DIFFERENT
                        // LETTER, because the neighbouring sheet pixels are other glyphs. The probe
                        // that found it rendered A-Z and got A..L right and then nonsense, and the
                        // wrong ones were exactly the five with this bit set: I, M, O, P, Q.
                        // ⚠ W AND H ARE THE DISPLAY SIZE; ROTATION TRANSPOSES THE SAMPLING, NOT THE
                        // OUTPUT. I had it the other way round first, which made the widest lowercase
                        // letter -- 'm', stored 30x17 -- draw 17 wide and 30 tall. The aspect ratios
                        // are what settle it: every glyph's stored H matches the font's line height,
                        // so H cannot be a width. A rotated sprite occupies H x W on the SHEET and
                        // still draws W x H on screen.
                        // ⚠ BIT 0 OF Flags MEANS THE SPRITE IS STORED TURNED 90 DEGREES, and ignoring
                        // it does not draw a broken glyph -- it draws a DIFFERENT LETTER, because what
                        // sits beside a glyph in the sheet is other glyphs. Found by rendering A-Z:
                        // correct through L, then nonsense, and the wrong ones were exactly the five
                        // with the bit set (I, M, O, P, Q).
                        //
                        // ⚠ THE ORIENTATION BELOW WAS CHOSEN BY RENDERING ALL FOUR READINGS SIDE BY
                        // SIDE, not by reasoning about it -- I argued myself into a different one from
                        // the stored W/H aspect ratios and it was wrong. The picture decided.
                        //
                        // ⚠ 'M' AND 'm' ARE STILL WRONG (both draw as N/n) AND ORIENTATION IS NOT THE
                        // CAUSE: all four readings were rendered and M draws as N in every one of
                        // them, so no choice here fixes it. They are the widest glyph of each case
                        // (W=34 and W=30) and 'm' is the one whose U+W runs off the 256-texel page
                        // edge (237+30), which the other flagged sprites do not -- so the next thing
                        // to test is whether a glyph too wide for its page is stored split or indexed
                        // differently, NOT another rotation.
                        bool rot = (sp.Flags & 1) != 0;
                        int outW = rot ? sp.H : sp.W, outH = rot ? sp.W : sp.H;
                        for (int gy = 0; gy < outH; gy++)
                        {
                            int dy = y + gy;
                            if (dy < 0 || dy >= H) continue;
                            for (int gx = 0; gx < outW; gx++)
                            {
                                int dx = x + gx;
                                if (dx < 0 || dx >= W) continue;
                                int su = rot ? sp.U + gy : sp.U + gx;
                                int sv = rot ? sp.V + (outW - 1 - gx) : sp.V + gy;
                                Blend(dst, (dy * W + dx) * 4, src, ((ay + sv) * src.Width + ax + su) * 4, additive);
                            }
                        }
                    }
                }
                x += GlyphWidth(p, c);
            }
            return x;
        }
    }
}
