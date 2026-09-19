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
            foreach (int fi in LanguageRing.FlagSprite)
                if (fi >= 0 && fi < sheet.Sprites.Count)
                { var fs = sheet.Sprites[fi]; uses.Add((fs.TPage, fs.Clut)); }
            foreach (var kv in MenuLayout.Glyph)
                if (kv.Value >= 0 && kv.Value < sheet.Sprites.Count)
                {
                    var sp = sheet.Sprites[kv.Value];
                    uses.Add((sp.TPage, sp.Clut));
                }
            return new Prepared { Atlas = PageAtlas.Build(sheet, uses), Sheet = sheet, SpriteCount = sheet.Sprites.Count };
        }

        /// <summary>The console's visible window over a drawn frame: the top
        /// <see cref="MenuLayout.VisibleHeight"/> rows. Present this, not the whole buffer.</summary>
        public static byte[] Visible(byte[] frame)
        {
            int n = W * MenuLayout.VisibleHeight * 4;
            if (frame == null || frame.Length <= n) return frame;
            var outp = new byte[n];
            Array.Copy(frame, outp, n);
            return outp;
        }

        /// <summary>A fresh transparent frame.</summary>
        public static byte[] NewFrame() => new byte[W * H * 4];

        /// <summary>The highlight glow's own texture page. It is the one backdrop piece that MOVES --
        /// it sits under whichever item is selected -- so it is drawn separately, at an offset, rather
        /// than baked into the static layer where it would be frozen on the captured frame's row.</summary>
        const ushort GlowPage = 56;

        /// <summary>Back-to-front depth of a backdrop piece.
        ///
        /// ⚠ THE CAPTURED LIST'S ORDER IS NOT THE DRAW ORDER, IN EITHER DIRECTION, and this cost three
        /// attempts to accept. Walked forward it puts the curtains over the logo; walked backward it
        /// puts the floor over the curtains; and the list is palindromic, so "backward" was partly
        /// meaningless anyway. The dump followed addresses, which is not the ordering table's chain.
        ///
        /// So the depth is stated per PIECE, from the one piece of evidence that settles it -- the
        /// console screenshot: the curtains overlap the floor at the bottom, and the logo's 't' sits in
        /// front of the left curtain. Anything not recognised sorts with the curtains, which is the
        /// middle, so a new piece cannot silently land on top of everything.</summary>
        static int Depth(in ScreenQuad q) => q.Clut switch
        {
            0x4121 or 0x4122 or 0x4123 or 0x4160 => 0,   // stage floor
            0x4060 or 0x4061                     => 3,   // THEME PARK WORLD logo, frontmost
            _ when q.TPage == GlowPage           => 2,   // highlight glow
            _                                    => 1,   // curtains, valance, side drapes
        };

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
            // ⚠ FORWARD, and the reversal that used to be here was treating a symptom. The captured
            // list is PALINDROMIC -- the same scene twice, once each way -- so whichever direction it
            // was walked, the second copy of the floor landed on top of the curtains. Reversing made
            // that look better without fixing it. The duplicate is gone from the table now, and the
            // order is the console's own.
            for (int layer = 0; layer <= 3; layer++)
                foreach (var q in MenuLayout.Backdrop)
                    if (q.TPage != GlowPage && Depth(q) == layer) DrawQuad(p, dst, q, 0);

            // ⚠ THE RAYS GO ON TOP OF THE CURTAINS, and I had them behind.
            //
            // I put them behind because the console frame's drapes looked barely lit next to mine, and
            // reasoned that additive light which is never occluded is the mark of a fan pasted over a
            // finished image. That reasoning is fine and the premise was false: the two captures are
            // at different PHASES OF THE SWEEP, so the areas that read brighter in one are simply
            // where the rays happen to be. Corrected by the person who knows the screen, not by the
            // comparison -- a single frame of a MOVING effect cannot tell you its depth, because you
            // cannot tell "this is drawn behind" from "this ray is somewhere else right now".
            DrawSpotlight(dst);
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
            foreach (var q in MenuLayout.Backdrop)
                if (q.TPage == GlowPage) DrawQuad(p, dst, q, dy);
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
        static void Blend(byte[] dst, int d, TpwImage src, int s, bool additive, float shade = 1f)
        {
            if (s < 0 || s + 3 >= src.Rgba.Length || d + 3 >= dst.Length) return;
            byte a = src.Rgba[s + 3];
            if (a == 0) return;
            byte R = Shade(src.Rgba[s], shade), G = Shade(src.Rgba[s + 1], shade), B = Shade(src.Rgba[s + 2], shade);
            if (additive)
            {
                dst[d]     = (byte)Math.Min(255, dst[d]     + R);
                dst[d + 1] = (byte)Math.Min(255, dst[d + 1] + G);
                dst[d + 2] = (byte)Math.Min(255, dst[d + 2] + B);
                dst[d + 3] = 255;
                return;
            }
            dst[d] = R; dst[d + 1] = G; dst[d + 2] = B; dst[d + 3] = 255;
        }

        static byte Shade(byte v, float f) => f >= 1f ? v : (byte)Math.Clamp((int)(v * f + 0.5f), 0, 255);

        /// <summary>The GPU shade the console applies to a normal menu item and to a disabled one.
        ///
        /// ⚠ 0x80 IS 1.0 ON THIS HARDWARE, NOT A HALF. A PSX primitive's colour is a multiplier where
        /// 128 means "leave the texture alone", so the measured 0x808080 on every menu item is FULL
        /// brightness and reading it as 50% would dim the whole menu. The disabled row's 0x202020 is
        /// 0x20/0x80 = a quarter.</summary>
        public const float NormalShade = 1f, DisabledShade = 0x20 / (float)0x80;

        /// <summary>The sweeping light: additive Gouraud wedges, no texture.
        ///
        /// ⚠ ADDITIVE, SO IT CANNOT DARKEN ANYTHING and its own black vertices contribute nothing.
        /// That is what makes the fan read as rays rather than as solid wedges -- each wedge fades from
        /// dark yellow to black across its width, and where two meet the sum is the bright edge.</summary>
        static void DrawSpotlight(byte[] dst)
        {
            foreach (var t in MenuLayout.Spotlight) FillTri(dst, t);
        }

        /// <summary>Barycentric fill. Clipped to the screen rather than to the triangle's own bounds,
        /// because the fan's apex sits at (-103,-64) -- off the top-left corner -- and a rasteriser
        /// that assumed on-screen vertices would drop every wedge in the sweep.</summary>
        static void FillTri(byte[] dst, in ScreenTri t)
        {
            int x0 = t.X0, x1 = t.X1, x2 = t.X2, y0 = t.Y0, y1 = t.Y1, y2 = t.Y2;
            int minX = Math.Max(0, Math.Min(x0, Math.Min(x1, x2)));
            int maxX = Math.Min(W - 1, Math.Max(x0, Math.Max(x1, x2)));
            int minY = Math.Max(0, Math.Min(y0, Math.Min(y1, y2)));
            int maxY = Math.Min(H - 1, Math.Max(y0, Math.Max(y1, y2)));
            float den = (t.Y1 - t.Y2) * (float)(t.X0 - t.X2) + (t.X2 - t.X1) * (float)(t.Y0 - t.Y2);
            if (Math.Abs(den) < 1e-6f) return;                    // degenerate, and the fan has some

            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    float w0 = ((t.Y1 - t.Y2) * (x - t.X2) + (t.X2 - t.X1) * (y - t.Y2)) / den;
                    float w1 = ((t.Y2 - t.Y0) * (x - t.X2) + (t.X0 - t.X2) * (y - t.Y2)) / den;
                    float w2 = 1f - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    int o = (y * W + x) * 4;
                    dst[o]     = (byte)Math.Min(255, dst[o]     + (int)(w0 * t.R0 + w1 * t.R1 + w2 * t.R2));
                    dst[o + 1] = (byte)Math.Min(255, dst[o + 1] + (int)(w0 * t.G0 + w1 * t.G1 + w2 * t.G2));
                    dst[o + 2] = (byte)Math.Min(255, dst[o + 2] + (int)(w0 * t.B0 + w1 * t.B1 + w2 * t.B2));
                    dst[o + 3] = 255;
                }
        }

        /// <summary>The language ring's five VISIBLE slots, read off the console's own display list on
        /// the language screen (RAM dump, frame 30 of states/language.state).
        ///
        /// The ring holds seven languages but only FIVE are drawn -- the two at the back are omitted
        /// entirely, not merely dimmed. Each slot is an x offset from the ring's centre, a text
        /// baseline, and a shade; the shades are the primitives' own vertex colour over 0x80, which is
        /// 1.0 on this hardware. ⚠ The offsets are NOT evenly spaced (158.5 then 229.5) because this is
        /// a perspective projection of a circle, not a row -- an evenly-spaced guess would look wrong
        /// in a way that is hard to name once it is on screen.</summary>
        public static readonly (float Dx, int Baseline, float Shade)[] RingSlots =
        {
            (-229.5f, 172, 0x19 / 128f),
            (-158.5f, 200, 0x2e / 128f),
            (   0.0f, 213, 0x64 / 128f),
            ( 158.5f, 200, 0x2e / 128f),
            ( 229.5f, 172, 0x19 / 128f),
        };

        /// <summary>Where the ring is centred. ⚠ 259, not 256: the screen's centre is 255.5 and the
        /// text ring does not sit on it. Measured, not assumed.</summary>
        public const int RingCentreX = 259;

        /// <summary>The two sets of four chevrons either side of the selected name, as the console
        /// draws them: flat semi-transparent triangles 32 wide and 24 tall, stepping 16 apart, on a
        /// 1:2:3:4 brightness ramp with the OUTERMOST darkest. Colour is (50,45,4) times the step --
        /// gold, not the cyan a raw little-endian read of the GPU word suggests.</summary>
        public const int ChevronLeftApexX = 87, ChevronRightApexX = 423;
        public const int ChevronMidY = 204, ChevronTopY = 192, ChevronBotY = 216;
        public const int ChevronStep = 16, ChevronWidth = 32, ChevronCount = 4;

        static void Chevrons(byte[] dst)
        {
            for (int k = 0; k < ChevronCount; k++)
            {
                byte r = (byte)(50 * (k + 1)), g = (byte)(45 * (k + 1)), b = (byte)(4 * (k + 1));
                int la = ChevronLeftApexX + k * ChevronStep, lb = la + ChevronWidth;
                int ra = ChevronRightApexX - k * ChevronStep, rb = ra - ChevronWidth;
                FillTri(dst, new ScreenTri((short)la, ChevronMidY, (short)lb, ChevronBotY, (short)lb, ChevronTopY,
                                           r, g, b, r, g, b, r, g, b));
                FillTri(dst, new ScreenTri((short)ra, ChevronMidY, (short)rb, ChevronBotY, (short)rb, ChevronTopY,
                                           r, g, b, r, g, b, r, g, b));
            }
        }

        /// <summary>Which language occupies a given ring SLOT when <paramref name="sel"/> is chosen.
        /// Slot 0 is the far left, slot 2 the selected one.
        ///
        /// ⚠ Public so the tests can assert the order the RENDERER uses. A test that recomputes this
        /// expression itself passes when the renderer's copy is reversed -- which is exactly what
        /// happened: flipping the sign here left 254 tests green until they called this instead.</summary>
        public static int SlotLanguage(int sel, int slot)
        {
            int n = LanguageRing.Count;
            return (((sel + slot - RingSlots.Length / 2) % n) + n) % n;
        }

        /// <summary>The whole language-select screen, composed. Lives HERE rather than in the Godot
        /// layer so that the offline render and the running game are the SAME code -- a harness that
        /// re-composes the screen itself would be testing the harness.</summary>
        public static void DrawLanguageScreen(Prepared p, byte[] dst, int ring, bool flatFlag = true)
        {
            // ⭐ MEASURED: one Gouraud quad, RGB(153,163,254) at the top to RGB(42,32,87) at the bottom.
            for (int yy = 0; yy < H; yy++)
            {
                float t = yy / (float)(H - 1);
                byte r = (byte)(153 + (42 - 153) * t), g = (byte)(163 + (32 - 163) * t), b = (byte)(254 + (87 - 254) * t);
                for (int xx = 0; xx < W; xx++)
                {
                    int o = (yy * W + xx) * 4;
                    dst[o] = r; dst[o + 1] = g; dst[o + 2] = b; dst[o + 3] = 255;
                }
            }
            int n = LanguageRing.Count;
            int sel = ((ring % n) + n) % n;

            // ⚠ A STAND-IN, drawn only when the advisor's MODEL is not available. The console has no 2D
            // flag at all: it hangs the cloth on a pole the advisor holds and waves it with his bones.
            // Drawing both would put two flags on screen, which is worse than either alone.
            if (flatFlag) DrawSprite(p, dst, LanguageRing.FlagSprite[sel], 181, 40);

            Chevrons(dst);

            for (int i = 0; i < RingSlots.Length; i++)
            {
                // ⚠ ADDITIVE, and this is the difference between the ring reading right and reading
                // like a list of labels. The primitives carry abr=1, which on this GPU is back+front,
                // NOT the back/2+front/2 of abr=0 -- I implemented the half blend first and it left the
                // selected name muddy brown where the console's is vivid orange. Additive over the blue
                // gradient is also why the dim slots tint lavender instead of showing a black outline:
                // the font's outline pixels add nothing and simply stay background.
                var (dx, baseline, shade) = RingSlots[i];
                int idx = SlotLanguage(sel, i);
                string name = LanguageRing.NativeNameAt(idx);
                int w = MeasureText(p, name);
                DrawText(p, dst, (int)MathF.Round(RingCentreX + dx - w / 2f), baseline, name, additive: true, shade: shade);
            }
        }

        /// <summary>Draw one sheet sprite by index, top-left at (x, y). Honours the rotation flag the
        /// same way the glyph path does, since the flag sprites come off the same sheet.</summary>
        public static void DrawSprite(Prepared p, byte[] dst, int index, int x, int y)
        {
            if (p == null || index < 0 || index >= p.SpriteCount) return;
            var sp = p.Sheet.Sprites[index];
            if (!p.Atlas.TryOrigin(sp.TPage, sp.Clut, out int ax, out int ay)) return;
            var src = p.Atlas.Image;
            bool rot = (sp.Flags & 1) != 0;
            for (int gy = 0; gy < sp.H; gy++)
            {
                int dy = y + gy;
                if (dy < 0 || dy >= H) continue;
                for (int gx = 0; gx < sp.W; gx++)
                {
                    int dx = x + gx;
                    if (dx < 0 || dx >= W) continue;
                    int su = rot ? sp.U + gy : sp.U + gx;
                    int sv = rot ? sp.V + (sp.W - 1 - gx) : sp.V + gy;
                    Blend(dst, (dy * W + dx) * 4, src, ((ay + sv) * src.Width + ax + su) * 4, false);
                }
            }
        }

        /// <summary>Width of a string in the menu font, for centring.</summary>
        public static int MeasureText(Prepared p, string text)
        {
            int w = 0;
            foreach (char c in text) w += GlyphWidth(p, c);
            return w;
        }

        /// <summary>⚠ THE ADVANCE IS W MINUS FOUR, not W, and certainly not W plus a gap. Read off the
        /// console's own display list: in the captured menu every consecutive pair of glyphs is exactly
        /// (width - 4) apart -- P 23 -> 19, l 12 -> 8, a 24 -> 20, G 29 -> 25, m 30 -> 26, and the same
        /// on all seven letters of "Options". The glyph bitmaps evidently carry two columns of padding
        /// each side. I had used W + 1, which is five pixels too wide PER LETTER, so "Play Game" came
        /// out 40 pixels over-long -- visible immediately beside the real thing as text that is "much
        /// more spread out", which is exactly how it was reported.
        ///
        /// ⭐ AND THE GAME'S OWN TEXT ROUTINES ARE NOW LOCATED, which is where this would be settled
        /// exactly rather than measured. The title screen's draw is ovl2 0x8011534C; two of the five
        /// sub-draws it calls (0x80114C20 and 0x80114EDC) are text, and they go through main-image
        /// helpers 0x800E9A44 (string width), 0x800E9A38, 0x800E9070 and 0x800E95F0 (draw string).
        /// The -4 is computed inside 0x800E9A44, as is the real centring rule -- so anyone wanting the
        /// row positions and alignment exactly right should read those four rather than re-measure.</summary>
        public const int Advance = -4, SpaceWidth = 8;

        static int GlyphWidth(Prepared p, char c)
        {
            if (c == ' ') return SpaceWidth;
            if (p == null || !MenuLayout.Glyph.TryGetValue(c, out int i) || i >= p.SpriteCount) return SpaceWidth;
            return Math.Max(1, p.Sheet.Sprites[i].W + Advance);
        }

        /// <summary>Draw a string in the game's own font. <paramref name="y"/> is the BASELINE, not the
        /// top.
        ///
        /// ⚠ EVERY SPRITE CARRIES ITS OWN OffsetX/OffsetY AND THEY ARE NOT DECORATION -- they are what
        /// puts a glyph on the baseline. Ignoring them top-aligns the whole font, so 'a' and 'c' ride
        /// up level with 'b' and 'h', and 'g', 'p', 'q' and 'y' lose their descenders entirely. The
        /// text stays perfectly legible while being wrong in a way that is obvious the moment it is
        /// put beside the real game -- which is how it was caught.
        ///
        /// Characters with no glyph advance as a space rather than being dropped, so a missing
        /// punctuation mark shows as a gap instead of silently closing up the text.</summary>
        public static int DrawText(Prepared p, byte[] dst, int x, int y, string text, bool additive = false,
                                   float shade = NormalShade)
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
                        // ⚠ AND I GOT THIS WRONG TWICE BEFORE GETTING IT RIGHT, both times by
                        // comparing variants in a picture I could not actually read -- the probe was
                        // drawn over the curtains. Rendered on BLACK the answer was obvious in one
                        // look: two of the readings produced a correct M that was merely MIRRORED or
                        // UPSIDE DOWN, which named the fix immediately (a transpose is a rotation plus
                        // a mirror, so flip the other axis). I had meanwhile written a commit saying
                        // orientation was ruled out and the cause lay elsewhere. It did not. When a
                        // visual comparison is inconclusive, fix the VIEW before drawing a conclusion
                        // from it.
                        // The sprite still DRAWS W x H; what rotation changes is where the pixels are
                        // read from -- the stored rect is H wide and W tall, turned a quarter turn.
                        bool rot = (sp.Flags & 1) != 0;
                        int gx0 = x + sp.OffsetX, gy0 = y + sp.OffsetY;
                        for (int gy = 0; gy < sp.H; gy++)
                        {
                            int dy = gy0 + gy;
                            if (dy < 0 || dy >= H) continue;
                            for (int gx = 0; gx < sp.W; gx++)
                            {
                                int dx = gx0 + gx;
                                if (dx < 0 || dx >= W) continue;
                                int su = rot ? sp.U + gy : sp.U + gx;
                                int sv = rot ? sp.V + (sp.W - 1 - gx) : sp.V + gy;
                                Blend(dst, (dy * W + dx) * 4, src, ((ay + sv) * src.Width + ax + su) * 4, additive, shade);
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
