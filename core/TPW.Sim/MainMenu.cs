using System;

namespace TPW.Sim
{
    /// <summary>The main menu as the console actually behaves, measured by pressing every button on
    /// every item and diffing whole frame sequences against a no-press run. See findings/boot.md §C.
    /// </summary>
    public enum MenuPage
    {
        /// <summary>Play Game / Options / Load Game.</summary>
        Root,
        /// <summary>Main Game / Practice Park / Exit. Replaces the root list in place, same backdrop.</summary>
        PlayGame,
        /// <summary>Music / SFX / Tutorial / Screen / Credits / Exit. The logo scrolls off for this one.</summary>
        Options,
    }

    /// <summary>What choosing an item asked the game to do. Only the ones that were OBSERVED to do
    /// something are here.</summary>
    public enum MenuAction
    {
        None,
        OpenPlayGame,
        OpenOptions,
        StartPracticePark,
    }

    public sealed class MainMenu
    {
        public static readonly string[] RootItems    = { "Play Game", "Options", "Load Game" };
        public static readonly string[] PlayGameItems = { "Main Game", "Practice Park", "Exit" };
        public static readonly string[] OptionsItems  = { "Music", "SFX", "Tutorial", "Screen", "Credits", "Exit" };

        /// <summary>What an Options row does when you push LEFT/RIGHT or CROSS.
        ///
        /// ⚠ ONLY THE TWO SLIDERS ARE MEASURED. LEFT on the Music row changed the rendered frame and
        /// CROSS on it did nothing, which is a slider; SFX is drawn identically beside it. Everything
        /// below them is <see cref="OptionKind.Unmeasured"/> -- the row exists and is drawn, and what it
        /// does was never tested. "Tutorial" READS as "Tutorial On" on screen, which strongly suggests a
        /// toggle, but reading a label is not pressing the button and the difference is the whole point
        /// of this enum. Do not promote a row to Toggle or Action without measuring it.</summary>
        public enum OptionKind { Slider, Unmeasured }

        public static readonly OptionKind[] OptionKinds =
        {
            OptionKind.Slider,      // Music
            OptionKind.Slider,      // SFX
            OptionKind.Unmeasured,  // Tutorial -- label says "Tutorial On"; toggling never tested
            OptionKind.Unmeasured,  // Screen
            OptionKind.Unmeasured,  // Credits
            OptionKind.Unmeasured,  // Exit
        };

        /// <summary>⚠ THE NUMBER OF STEPS IS A GUESS AND IS MARKED AS ONE. All that was measured is that
        /// LEFT changes the frame; how many notches the bar has, and where it starts, were not. Eleven
        /// gives a usable 0-100% in tens. If anyone measures the real step count, this is the constant
        /// to change -- and the volumes below are fractions of it, so nothing else needs touching.</summary>
        public const int SliderSteps = 10;

        public int MusicLevel { get; private set; } = SliderSteps;
        public int SfxLevel { get; private set; } = SliderSteps;

        /// <summary>Music volume as 0..1.</summary>
        public float MusicVolume => MusicLevel / (float)SliderSteps;
        public float SfxVolume => SfxLevel / (float)SliderSteps;

        /// <summary>The kind of the highlighted row, or Unmeasured off the Options page.</summary>
        public OptionKind CurrentKind =>
            Page == MenuPage.Options && Index < OptionKinds.Length ? OptionKinds[Index] : OptionKind.Unmeasured;

        /// <summary>LEFT/RIGHT on a slider row. Does nothing anywhere else, which matches the console:
        /// LEFT and RIGHT on the ROOT menu were byte-identical to no press.</summary>
        public bool Adjust(int delta)
        {
            if (CurrentKind != OptionKind.Slider) return false;
            int v = Math.Clamp((Index == 0 ? MusicLevel : SfxLevel) + delta, 0, SliderSteps);
            if (Index == 0) MusicLevel = v; else SfxLevel = v;
            return true;
        }

        public MenuPage Page { get; private set; } = MenuPage.Root;
        public int Index { get; private set; }

        public string[] Items => Page switch
        {
            MenuPage.PlayGame => PlayGameItems,
            MenuPage.Options  => OptionsItems,
            _                 => RootItems,
        };
        public string Current => Items[Index];

        /// <summary>Move the highlight. WRAPS in both directions — measured by pressing DOWN three times
        /// from Play Game (back to Play Game) and UP twice (to Options).</summary>
        public void Move(int delta)
        {
            int n = Items.Length;
            Index = ((Index + delta) % n + n) % n;
        }

        /// <summary>Press CROSS on the highlighted item.
        ///
        /// ⚠ "MAIN GAME" DOES NOTHING, and that is a MEASUREMENT, not a gap in the port. Pressing CROSS
        /// on it was byte-identical to not pressing it, for 900 frames, and again when pressed late —
        /// with no memory card present on the test harness. Whether a card changes that is UNTESTED, so
        /// do not implement "Main Game" as broken; implement it as not-yet-known.
        ///
        /// ⚠ "LOAD GAME" likewise did nothing, and that one has an obvious candidate explanation (no
        /// save existed), which is exactly why it must not be written down as "Load Game is a no-op".
        /// </summary>
        public MenuAction Confirm()
        {
            switch (Page)
            {
                case MenuPage.Root:
                    if (Current == "Play Game") { Page = MenuPage.PlayGame; Index = 0; return MenuAction.OpenPlayGame; }
                    if (Current == "Options")   { Page = MenuPage.Options;  Index = 0; return MenuAction.OpenOptions; }
                    return MenuAction.None;                       // Load Game: nothing observed
                case MenuPage.PlayGame:
                    if (Current == "Practice Park") return MenuAction.StartPracticePark;
                    return MenuAction.None;                       // Main Game: nothing observed. Exit: untested
                default:
                    return MenuAction.None;                       // Options rows: sliders, not confirms
            }
        }

        /// <summary>⚠ THERE IS NO BACK BUTTON, and this is the measurement that would be easiest to
        /// "fix" by accident. Triangle and circle were both pressed on both submenus and both were
        /// byte-identical to no press. A port that helpfully adds a back button has stopped matching the
        /// original — if one is wanted, that is a deliberate change, not a missing feature.
        ///
        /// Both submenus offer their own "Exit" row instead, which was not tested.</summary>
        public const bool HasBackButton = false;
    }
}
