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

        /// <summary>LEFT/RIGHT do nothing on the ROOT menu — measured, byte-identical to no press —
        /// and the model must enforce that itself rather than relying on every caller to check.</summary>
        [Fact]
        public void SlidersOnlyRespondOnASliderRow()
        {
            var m = new MainMenu();
            Assert.False(m.Adjust(-1));                       // root, Play Game
            m.Move(+1); m.Confirm();                          // into Options, on Music
            Assert.Equal(MainMenu.OptionKind.Slider, m.CurrentKind);
            Assert.True(m.Adjust(-1));
            m.Move(+2);                                       // Tutorial -- never measured
            Assert.Equal(MainMenu.OptionKind.Unmeasured, m.CurrentKind);
            Assert.False(m.Adjust(-1));
        }

        [Fact]
        public void ASliderClampsAtBothEnds()
        {
            var m = new MainMenu();
            m.Move(+1); m.Confirm();
            for (int i = 0; i < MainMenu.SliderSteps + 5; i++) m.Adjust(-1);
            Assert.Equal(0, m.MusicLevel);
            Assert.Equal(0f, m.MusicVolume);
            for (int i = 0; i < MainMenu.SliderSteps + 5; i++) m.Adjust(+1);
            Assert.Equal(MainMenu.SliderSteps, m.MusicLevel);
            Assert.Equal(1f, m.MusicVolume);
        }

        /// <summary>Music and SFX are separate: moving one must not move the other. They sit next to
        /// each other and share all their code, which is exactly the shape that ends up writing one
        /// field twice.</summary>
        [Fact]
        public void MusicAndSfxAreIndependent()
        {
            var m = new MainMenu();
            m.Move(+1); m.Confirm();
            m.Adjust(-3);
            Assert.Equal(MainMenu.SliderSteps - 3, m.MusicLevel);
            Assert.Equal(MainMenu.SliderSteps, m.SfxLevel);
            m.Move(+1);
            m.Adjust(-1);
            Assert.Equal(MainMenu.SliderSteps - 3, m.MusicLevel);
            Assert.Equal(MainMenu.SliderSteps - 1, m.SfxLevel);
        }

        /// <summary>⚠ Only the two sliders were measured. If someone promotes Tutorial to a toggle
        /// because the label reads "Tutorial On", this fails and makes them go and press the button.</summary>
        [Fact]
        public void OnlyMusicAndSfxAreMeasuredBehaviours()
        {
            Assert.Equal(MainMenu.OptionsItems.Length, MainMenu.OptionKinds.Length);
            Assert.Equal(MainMenu.OptionKind.Slider, MainMenu.OptionKinds[0]);
            Assert.Equal(MainMenu.OptionKind.Slider, MainMenu.OptionKinds[1]);
            for (int i = 2; i < MainMenu.OptionKinds.Length; i++)
                Assert.Equal(MainMenu.OptionKind.Unmeasured, MainMenu.OptionKinds[i]);
        }

        /// <summary>⚠ There is no back button: triangle and circle were both pressed on both submenus
        /// and both were byte-identical to no press. Adding one is a deliberate departure, not a fix.</summary>
        [Fact]
        public void ThereIsNoBackButton() => Assert.False(MainMenu.HasBackButton);
    }
}
