using System;
using System.Linq;
using System.Linq;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    public class MenuLayoutTests
    {
        /// <summary>⚠ 512 WIDE. Assuming the menu ran at the legal screen's 320 clipped the THEME PARK
        /// WORLD logo in half, and the picture still looked deliberate -- curtains, floor and half a
        /// logo read as "the art is fine, the logo sprite is wrong". The captured primitives are what
        /// settle it: they reach x=514, which no 320-wide buffer can hold.</summary>
        [Fact]
        public void TheScreenIsWideEnoughForEveryCapturedQuad()
        {
            Assert.Equal(512, MenuLayout.ScreenWidth);
            int maxX = MenuLayout.Backdrop.Max(q => new[] { q.X0, q.X1, q.X2, q.X3 }.Max());
            Assert.True(maxX > 320, $"a quad reaches x={maxX}, so the buffer cannot be 320 wide");
            Assert.True(maxX <= MenuLayout.ScreenWidth + 2, $"a quad reaches x={maxX}, past the buffer");
        }

        /// <summary>The backdrop is the STATIC pieces only. If a font quad ever leaks into this table it
        /// would paint one frozen menu's text under the live text, which reads as a rendering smear
        /// rather than as the wrong data.</summary>
        [Fact]
        public void TheBackdropCarriesNoTextQuads()
        {
            const ushort FontClut = 0x41e3;
            Assert.DoesNotContain(MenuLayout.Backdrop, q => q.Clut == FontClut);
            Assert.NotEmpty(MenuLayout.Backdrop);
        }

        [Fact]
        public void EveryLetterAndDigitHasAGlyph()
        {
            for (char c = 'A'; c <= 'Z'; c++) Assert.True(MenuLayout.Glyph.ContainsKey(c), $"no glyph for {c}");
            for (char c = 'a'; c <= 'z'; c++) Assert.True(MenuLayout.Glyph.ContainsKey(c), $"no glyph for {c}");
            for (char c = '0'; c <= '9'; c++) Assert.True(MenuLayout.Glyph.ContainsKey(c), $"no glyph for {c}");
        }

        /// <summary>Distinct sprite per character: a duplicated index means two letters render the same,
        /// which is what the rotation bug looked like before it was understood.</summary>
        [Fact]
        public void NoTwoCharactersShareAGlyph()
        {
            var v = MenuLayout.Glyph.Values.ToArray();
            Assert.Equal(v.Length, v.Distinct().Count());
        }

        /// <summary>⚠ 0x80 IS 1.0 ON PSX HARDWARE. A primitive's colour is a multiplier where 128 means
        /// "leave the texture alone", so the measured 0x808080 on every menu item is FULL brightness.
        /// Reading it as 50% would dim the entire menu and look like a palette bug. The disabled row's
        /// 0x202020 is a quarter of it.</summary>
        [Fact]
        public void TheNormalShadeIsFullBrightnessAndDisabledIsAQuarter()
        {
            Assert.Equal(1f, MenuRenderer.NormalShade);
            Assert.Equal(0.25f, MenuRenderer.DisabledShade, 3);
        }

        /// <summary>Prepare must tolerate a disc whose sheet did not parse, because the menu still has to
        /// come up -- as the labelled placeholder -- rather than crash the boot.</summary>
        [Fact]
        public void PrepareOnANullSheetIsNotAnError()
        {
            Assert.Null(MenuRenderer.Prepare(null));
            var frame = MenuRenderer.NewFrame();
            MenuRenderer.DrawBackdrop(null, frame);            // must not throw
            Assert.Equal(4, MenuRenderer.DrawText(null, frame, 4, 0, "Play Game"));
            Assert.All(frame, b => Assert.Equal(0, b));
        }
    
        [Fact]
        public void LanguageRingDrawsFiveSlotsSymmetricallyAboutTheCentre()
        {
            var slots = MenuRenderer.RingSlots;
            Assert.Equal(5, slots.Length);
            // The ring holds seven; the two at the back are not drawn at all.
            Assert.True(slots.Length < LanguageRing.Count);
            for (int i = 0; i < slots.Length / 2; i++)
            {
                var a = slots[i];
                var b = slots[slots.Length - 1 - i];
                Assert.Equal(-a.Dx, b.Dx, 1);              // mirrored offsets
                Assert.Equal(a.Baseline, b.Baseline);      // same row
                Assert.Equal(a.Shade, b.Shade, 3);         // same dimming
            }
            // The selected slot is centred and the brightest; dimming falls off monotonically.
            Assert.Equal(0f, slots[2].Dx);
            for (int i = 0; i < 2; i++) Assert.True(slots[i].Shade < slots[i + 1].Shade);
            // ⚠ NOT evenly spaced: this is a projected circle. An even ring would make these equal.
            Assert.NotEqual(MathF.Abs(slots[2].Dx - slots[1].Dx), MathF.Abs(slots[1].Dx - slots[0].Dx), 1);
        }

        [Fact]
        public void NativeNamesAreInRingOrderAndDrawableInTheGameFont()
        {
            Assert.Equal(LanguageRing.Count, LanguageRing.NativeName.Length);
            Assert.Equal("English", LanguageRing.NativeNameAt(LanguageRing.RestPosition));
            Assert.Equal("Nederlands", LanguageRing.NativeNameAt(LanguageRing.Turn(LanguageRing.RestPosition, -1)));
            Assert.Equal("Svenska", LanguageRing.NativeNameAt(LanguageRing.Turn(LanguageRing.RestPosition, 1)));
            // Wrapping, both directions.
            Assert.Equal(LanguageRing.NativeNameAt(0), LanguageRing.NativeNameAt(LanguageRing.Count));
            Assert.Equal(LanguageRing.NativeNameAt(LanguageRing.Count - 1), LanguageRing.NativeNameAt(-1));
            // Every character must exist in the font, accents included -- a missing one is drawn as a
            // silent gap, which looks like a spacing bug rather than a missing glyph.
            foreach (string n in LanguageRing.NativeName)
                foreach (char c in n)
                    Assert.True(c == ' ' || MenuLayout.Glyph.ContainsKey(c), $"no glyph for '{c}' in \"{n}\"");
        }

        [Fact]
        public void EachRingPositionHasItsOwnFlagSprite()
        {
            Assert.Equal(LanguageRing.Count, LanguageRing.FlagSprite.Length);
            Assert.Equal(LanguageRing.Count, LanguageRing.FlagSprite.Distinct().Count());
            Assert.All(LanguageRing.FlagSprite, i => Assert.True(i > 0));
        }

        [Fact]
        public void TheConsoleShowsTheTopTwoHundredAndFortyRowsOnly()
        {
            Assert.True(MenuLayout.VisibleHeight < MenuLayout.ScreenHeight);
            var frame = MenuRenderer.NewFrame();
            // Mark a row the console shows and a row it crops, then check Visible() keeps one and
            // drops the other -- a length check alone would pass on a buffer copied from the wrong end.
            frame[((MenuLayout.VisibleHeight - 1) * MenuLayout.ScreenWidth) * 4] = 0x11;
            frame[(MenuLayout.VisibleHeight * MenuLayout.ScreenWidth) * 4] = 0x22;
            var vis = MenuRenderer.Visible(frame);
            Assert.Equal(MenuLayout.ScreenWidth * MenuLayout.VisibleHeight * 4, vis.Length);
            Assert.Equal(0x11, vis[(MenuLayout.VisibleHeight - 1) * MenuLayout.ScreenWidth * 4]);
            Assert.DoesNotContain((byte)0x22, vis);
            // Drawing still uses the full height: the ring's slots and the menu rows must stay inside
            // the DRAWING area, not the visible one, or the coordinates stop matching the console.
            Assert.All(MenuRenderer.RingSlots, s2 => Assert.True(s2.Baseline < MenuLayout.ScreenHeight));
        }
}
}
