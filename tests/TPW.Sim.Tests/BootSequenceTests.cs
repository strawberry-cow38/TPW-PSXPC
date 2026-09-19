using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class BootSequenceTests
    {
        /// <summary>Frames of BIOS before the game's own code starts. findings/boot.md counts from
        /// power-on; this table counts from the executable, so the two differ by exactly this.</summary>
        const int Bios = 1022;

        static BootSequence RunTo(BootScreen want, bool confirmWhenWaiting = true, int limit = 20000)
        {
            var b = new BootSequence();
            for (int i = 0; i < limit && b.Screen != want; i++)
            {
                // Hold confirm down from the moment the screen appears and let the MACHINE's own
                // delay decide when it counts -- gating it in the harness would test the harness.
                bool confirm = confirmWhenWaiting && b.Screen == BootScreen.LanguageSelect;
                b.Tick(anyButtonEdge: false, confirmEdge: confirm);
            }
            return b;
        }

        /// <summary>⭐ THE WHOLE TABLE IN ONE ASSERTION. Every duration was measured separately; this
        /// checks they still ADD UP to the two frame numbers the console was observed at — the language
        /// screen appearing at 4585 and the main menu at 4708, counting from power-on. A typo in any one
        /// duration moves both totals, and nothing else in the suite would notice.
        ///
        /// It also pins the offset: these are console numbers, so the test has to add the BIOS back.
        /// If someone "fixes" the table by folding the BIOS frames in, this fails.</summary>
        [Fact]
        public void TheTimelineReproducesTheConsoleFrameNumbers()
        {
            var b = RunTo(BootScreen.LanguageSelect);
            Assert.Equal(BootScreen.LanguageSelect, b.Screen);
            Assert.Equal(4585, b.TotalFrames + Bios);

            // Earliest a press is accepted: 9 frames after the screen appears.
            var m = RunTo(BootScreen.MainMenu);
            Assert.Equal(BootScreen.MainMenu, m.Screen);
            Assert.Equal(4708, m.TotalFrames + Bios);
            Assert.True(m.Finished);
        }

        /// <summary>⚠ THE ONE THAT MATTERS. On the console the FMV skip is an EDGE tested against a
        /// snapshot at the video player's init frame, so a button HELD from before the movie starts
        /// skips nothing — a hold across 2005-2050 does not skip, a hold across 2006-2050 does. A port
        /// that treats the skip as a level boots straight past both movies for anyone resting a finger
        /// on the pad, and it would pass any test that only ever presses.</summary>
        [Fact]
        public void AHeldButtonDoesNotSkipTheMovies()
        {
            // ⚠ Stop AT the language screen. It waits forever, so ticking a fixed number of frames
            // would measure the loop bound, not the boot.
            var held = new BootSequence();
            for (int i = 0; i < 20000 && held.Screen != BootScreen.LanguageSelect; i++)
                held.Tick(anyButtonEdge: false, confirmEdge: false);
            Assert.Equal(BootScreen.LanguageSelect, held.Screen);
            Assert.Equal(4585, held.TotalFrames + Bios);   // full length, nothing skipped

            // One edge during the Bullfrog logo does skip it.
            var pressed = new BootSequence();
            bool fired = false;
            for (int i = 0; i < 4000 && pressed.Screen != BootScreen.LanguageSelect; i++)
            {
                bool edge = !fired && pressed.Screen == BootScreen.BullfrogLogo;
                if (edge) fired = true;
                pressed.Tick(edge, false);
            }
            Assert.True(fired);
            Assert.True(pressed.TotalFrames < held.TotalFrames,
                        "an edge during the logo must shorten the boot");
        }

        /// <summary>The legal screen is NOT skippable: six buttons at four points in it were all
        /// byte-identical to no press. Asserted because "let the player skip the legal screen" is the
        /// single most tempting unmeasured kindness in this whole chain.</summary>
        [Fact]
        public void TheLegalScreenCannotBeSkipped()
        {
            var b = RunTo(BootScreen.Legal);
            int at = b.TotalFrames;
            for (int i = 0; i < BootSequence.Steps[1].Frames - 1; i++) b.Tick(anyButtonEdge: true, confirmEdge: true);
            Assert.Equal(BootScreen.Legal, b.Screen);
            Assert.Equal(at + BootSequence.Steps[1].Frames - 1, b.TotalFrames);
        }

        /// <summary>The language screen waits indefinitely — the console sat on it for 160 s.</summary>
        [Fact]
        public void TheLanguageScreenWaitsForever()
        {
            var b = RunTo(BootScreen.LanguageSelect, confirmWhenWaiting: false);
            for (int i = 0; i < 10000; i++) b.Tick(anyButtonEdge: true, confirmEdge: false);
            Assert.Equal(BootScreen.LanguageSelect, b.Screen);
        }

        /// <summary>It also ignores a press for its first 9 frames: a 1-frame CROSS at 4593 was
        /// identical to no press, at 4594 it advanced.</summary>
        [Fact]
        public void TheLanguageScreenIgnoresAPressInItsFirstNineFrames()
        {
            // The screen's first displayed frame is frame 0; the press counts ON frame 9 (console
            // 4585 + 9 = 4594), so frames 1..8 must be ignored and the 9th tick must take.
            var b = RunTo(BootScreen.LanguageSelect, confirmWhenWaiting: false);
            for (int i = 0; i < BootSequence.LanguageInputDelay - 1; i++) b.Tick(false, confirmEdge: true);
            Assert.Equal(BootScreen.LanguageSelect, b.Screen);
            b.Tick(false, confirmEdge: true);
            Assert.NotEqual(BootScreen.LanguageSelect, b.Screen);
        }

        [Fact]
        public void MoviesAndBlackScreensAreNamedConsistently()
        {
            Assert.Equal("BF.STR", BootSequence.MovieFor(BootScreen.BullfrogLogo));
            Assert.Equal("GRAV.STR", BootSequence.MovieFor(BootScreen.Intro));
            Assert.Null(BootSequence.MovieFor(BootScreen.MainMenu));
            // A screen is never both a movie and a black gap.
            foreach (BootStep s in BootSequence.Steps)
                Assert.False(BootSequence.IsBlack(s.Screen) && BootSequence.MovieFor(s.Screen) != null);
        }

        /// <summary>The legal screen's three phases must account for its whole length, or a fade will
        /// run past the screen it belongs to.</summary>
        [Fact]
        public void TheLegalPhasesSumToItsLength()
            => Assert.Equal(BootSequence.Steps[1].Frames,
                            BootSequence.LegalFadeIn + BootSequence.LegalHold + BootSequence.LegalFadeOut);
    }
}
