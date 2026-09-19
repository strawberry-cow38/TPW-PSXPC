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
