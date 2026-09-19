using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class MainMenuTests
    {
        [Fact]
        public void PlayGameIsHighlightedAtRest()
        {
            var m = new MainMenu();
            Assert.Equal(MenuPage.Root, m.Page);
            Assert.Equal("Play Game", m.Current);
        }

        /// <summary>Both directions wrap — measured by DOWN three times (back to Play Game) and UP once
        /// (straight to Load Game).</summary>
        [Fact]
        public void TheHighlightWrapsBothWays()
        {
            var m = new MainMenu();
            m.Move(+1); Assert.Equal("Options", m.Current);
            m.Move(+1); Assert.Equal("Load Game", m.Current);
            m.Move(+1); Assert.Equal("Play Game", m.Current);
            m.Move(-1); Assert.Equal("Load Game", m.Current);
        }

        [Fact]
        public void PlayGameOpensItsSubmenuWithMainGameHighlighted()
        {
            var m = new MainMenu();
            Assert.Equal(MenuAction.OpenPlayGame, m.Confirm());
            Assert.Equal(MenuPage.PlayGame, m.Page);
            Assert.Equal("Main Game", m.Current);
        }

        [Fact]
        public void OptionsOpensItsOwnPage()
        {
            var m = new MainMenu();
            m.Move(+1);
            Assert.Equal(MenuAction.OpenOptions, m.Confirm());
            Assert.Equal(MenuPage.Options, m.Page);
        }

        /// <summary>⚠ THESE TWO DID NOTHING ON THE CONSOLE, and the test records that rather than
        /// asserting they are correct. Load Game was tested with NO memory-card save present, and Main
        /// Game's null result has no explanation at all. If either is later found to work, this test
        /// should change — it is pinning a measurement, not a requirement.</summary>
        [Fact]
        public void LoadGameAndMainGameDidNothingWhenMeasured()
        {
            var m = new MainMenu();
            m.Move(+2);
            Assert.Equal("Load Game", m.Current);
            Assert.Equal(MenuAction.None, m.Confirm());
            Assert.Equal(MenuPage.Root, m.Page);      // did not navigate anywhere

            var p = new MainMenu();
            p.Confirm();                               // into Play Game
            Assert.Equal("Main Game", p.Current);
            Assert.Equal(MenuAction.None, p.Confirm());
            Assert.Equal(MenuPage.PlayGame, p.Page);
        }

        [Fact]
        public void PracticeParkStartsALoad()
        {
            var m = new MainMenu();
            m.Confirm();
            m.Move(+1);
            Assert.Equal("Practice Park", m.Current);
            Assert.Equal(MenuAction.StartPracticePark, m.Confirm());
        }

        /// <summary>⚠ There is no back button: triangle and circle were both pressed on both submenus
        /// and both were byte-identical to no press. Adding one is a deliberate departure, not a fix.</summary>
        [Fact]
        public void ThereIsNoBackButton() => Assert.False(MainMenu.HasBackButton);
    }
}
