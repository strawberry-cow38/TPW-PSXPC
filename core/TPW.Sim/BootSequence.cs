using System;
using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>The screens a cold boot walks through, in order, with the durations and input rules
    /// MEASURED off the console rather than chosen. See findings/boot.md for the frame numbers and how
    /// each was established.
    ///
    /// ⚠ THE BIOS SCREENS ARE NOT IN HERE. They are the console's firmware, run before the executable
    /// is loaded at all, and a port has nothing to reproduce. The measured timeline counts them (they
    /// are 1,022 frames of it), so every frame number below is stated RELATIVE TO THE GAME STARTING,
    /// not to power-on -- subtract 1022 from findings/boot.md to get these. Carrying the console's
    /// numbers into a port that never shows a BIOS screen would put a 20-second black gap at the front.
    /// </summary>
    public enum BootScreen
    {
        /// <summary>Black, while the game's own code starts. 116 frames on console.</summary>
        Startup,
        /// <summary>LEGAL.GFX, the EA copyright screen. Fades in, holds, fades out. NOT skippable --
        /// every button was tried at four points in it and all were byte-identical to no press.</summary>
        Legal,
        /// <summary>Black while the first movie is fetched off the disc.</summary>
        LoadingBullfrog,
        /// <summary>BF.STR, the Bullfrog logo. Skippable.</summary>
        BullfrogLogo,
        BlackAfterBullfrog,
        /// <summary>GRAV.STR, the intro. Skippable.</summary>
        Intro,
        BlackAfterIntro,
        /// <summary>The language ring. Waits indefinitely -- the console sat here for 160 s with no
        /// input and never moved on.</summary>
        LanguageSelect,
        /// <summary>Language screen fading out after the choice, then black.</summary>
        LanguageFadeOut,
        /// <summary>The juggling jester on blue.</summary>
        NowLoading,
        BlackBeforeMenu,
        /// <summary>The main menu. Terminal state of the boot chain.</summary>
        MainMenu,
    }

    /// <summary>What ends a screen.</summary>
    public enum BootAdvance
    {
        /// <summary>Runs its measured length, then moves on by itself.</summary>
        Timed,
        /// <summary>Runs to its end OR until any button, whichever comes first.</summary>
        TimedOrAnyButton,
        /// <summary>Never ends on its own; waits for Confirm.</summary>
        WaitForConfirm,
        /// <summary>The chain is over.</summary>
        Terminal,
    }

    public readonly struct BootStep
    {
        public readonly BootScreen Screen;
        public readonly int Frames;              // at 50 Hz, the PAL rate the measurements were taken at
        public readonly BootAdvance Advance;
        public BootStep(BootScreen s, int frames, BootAdvance a) { Screen = s; Frames = frames; Advance = a; }
    }

    /// <summary>
    /// ⭐ WHY THIS IS A STATE MACHINE IN core AND NOT A COROUTINE IN THE GODOT SCENE: the interesting
    /// part of the boot is the TIMING AND THE INPUT RULES, and both were measured to the frame. A
    /// coroutine in the render layer can only be checked by watching it, which is how a port ends up
    /// with an intro that "feels about right" and a skip that fires on the wrong edge.
    /// </summary>
    public sealed class BootSequence
    {
        /// <summary>PAL. Every measurement in findings/boot.md is frames at this rate.</summary>
        public const int Hz = 50;

        /// <summary>⚠ FRAME COUNTS ARE THE CONSOLE'S, INCLUDING ITS DISC WAITS. The two long black gaps
        /// (593 and 129 frames) are the CD seeking and buffering, not authored pauses -- a port reading
        /// from a local file has nothing to wait for. They are kept at their measured length because
        /// matching the original is the point of this port; a build that wants to boot fast should skip
        /// them explicitly rather than by quietly shortening the table.</summary>
        public static readonly BootStep[] Steps =
        {
            new(BootScreen.Startup,            116, BootAdvance.Timed),
            new(BootScreen.Legal,              313, BootAdvance.Timed),
            new(BootScreen.LoadingBullfrog,    593, BootAdvance.Timed),
            new(BootScreen.BullfrogLogo,       444, BootAdvance.TimedOrAnyButton),
            new(BootScreen.BlackAfterBullfrog,  24, BootAdvance.Timed),
            new(BootScreen.Intro,             1944, BootAdvance.TimedOrAnyButton),
            new(BootScreen.BlackAfterIntro,    129, BootAdvance.Timed),
            new(BootScreen.LanguageSelect,       0, BootAdvance.WaitForConfirm),
            new(BootScreen.LanguageFadeOut,     30, BootAdvance.Timed),
            new(BootScreen.NowLoading,          67, BootAdvance.Timed),
            new(BootScreen.BlackBeforeMenu,     17, BootAdvance.Timed),
            new(BootScreen.MainMenu,             0, BootAdvance.Terminal),
        };

        /// <summary>The legal screen's own three phases, as fractions of its 313 frames: 61 fading in,
        /// 189 holding, 63 fading out.</summary>
        public const int LegalFadeIn = 61, LegalHold = 189, LegalFadeOut = 63;

        /// <summary>Frames after the main menu appears before it accepts a press. Measured: a 1-frame
        /// press at +151 does nothing and at +152 moves the highlight.
        ///
        /// ⚠ THIS IS NOT COSMETIC AND IT IS NOT A LOADING DELAY. A menu that accepts input the instant
        /// it is drawn eats the press that the player is still holding from the screen before.</summary>
        public const int MenuInputDelay = 152;

        /// <summary>Frames after the main menu appears before its music starts.</summary>
        public const int MenuMusicDelay = 305;

        /// <summary>Frames after the language screen appears before it accepts a press.</summary>
        public const int LanguageInputDelay = 9;

        int _index;
        int _frame;

        public BootScreen Screen => Steps[_index].Screen;
        public int FrameInScreen => _frame;
        public bool Finished => Steps[_index].Advance == BootAdvance.Terminal;
        /// <summary>Total frames since the game's own code started.</summary>
        public int TotalFrames { get; private set; }

        /// <summary>0..1 through a timed screen; 0 for one that waits.</summary>
        public float Progress
        {
            get { int n = Steps[_index].Frames; return n <= 0 ? 0f : Math.Min(1f, (float)_frame / n); }
        }

        /// <summary>Advance one frame.
        ///
        /// ⚠ anyButtonEdge AND confirmEdge ARE EDGES, NOT LEVELS, and that is measured, not a
        /// convention: on the console a button HELD across the whole intro skips nothing, because the
        /// skip is tested against a snapshot taken when the video player initialises. A held CROSS also
        /// fails to advance the language screen. Pass true on the frame a press BEGINS and false while
        /// it is held, or the port will skip the intro for anyone who boots with a finger down -- which
        /// is exactly the bug the measurement exists to prevent.</summary>
        public void Tick(bool anyButtonEdge, bool confirmEdge)
        {
            if (Finished) return;
            TotalFrames++;
            _frame++;

            var step = Steps[_index];
            bool done = step.Advance switch
            {
                BootAdvance.Timed            => _frame >= step.Frames,
                BootAdvance.TimedOrAnyButton => _frame >= step.Frames || anyButtonEdge,
                // >= not >: the press frame is frame 0 of what follows, not the last frame of this
                // screen. Off by one here puts the whole rest of the boot one frame late, which the
                // console frame numbers catch and nothing else would.
                BootAdvance.WaitForConfirm   => confirmEdge && _frame >= LanguageInputDelay,
                _                            => false,
            };
            if (done) { _index++; _frame = 0; }
        }

        /// <summary>Move to the next screen now, whatever the frame count says.
        ///
        /// ⭐ THIS IS FOR THE MOVIES, AND IT IS NOT A CHEAT. The table's 444 and 1944 frames are how long
        /// BF.STR and GRAV.STR ran ON THE CONSOLE; the port plays those same two files, so the FILE is
        /// the authority on its own length and the table is only a record of what it came to. Ticking
        /// the table alongside a real decoder would give two clocks that drift apart, and the visible
        /// symptom would be the intro being cut off or held on its last frame.</summary>
        public void Advance()
        {
            if (Finished) return;
            _index++;
            _frame = 0;
        }

        /// <summary>Jump straight to a screen, for anyone working on a later one. Does not run the
        /// screens it passes.</summary>
        public void SkipTo(BootScreen s)
        {
            for (int i = 0; i < Steps.Length; i++)
                if (Steps[i].Screen == s) { _index = i; _frame = 0; return; }
            throw new ArgumentOutOfRangeException(nameof(s), $"no boot step for {s}");
        }

        /// <summary>Which disc movie a screen plays, or null. Named rather than indexed because the
        /// disc is the authority on what exists.</summary>
        public static string MovieFor(BootScreen s) => s switch
        {
            BootScreen.BullfrogLogo => "BF.STR",
            BootScreen.Intro        => "GRAV.STR",
            _                       => null,
        };

        /// <summary>Whether a screen is drawn as plain black.</summary>
        public static bool IsBlack(BootScreen s) => s is BootScreen.Startup or BootScreen.LoadingBullfrog
            or BootScreen.BlackAfterBullfrog or BootScreen.BlackAfterIntro or BootScreen.BlackBeforeMenu;
    }
}
