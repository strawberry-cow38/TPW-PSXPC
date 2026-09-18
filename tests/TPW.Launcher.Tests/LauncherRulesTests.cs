using System;
using System.Collections.Generic;
using TPW.Launcher;
using Xunit;

namespace TPW.Launcher.Tests
{
    public class GodotExeTests
    {
        static Func<string, bool> Has(params string[] present) =>
            p => Array.Exists(present, x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase));

        [Fact]
        public void TurnsTheConsoleOn()
        {
            var c = LauncherRules.GodotExeFor(@"C:\g\Godot_v4.6_mono_win64.exe", true,
                                              Has(@"C:\g\Godot_v4.6_mono_win64.exe", @"C:\g\Godot_v4.6_mono_win64_console.exe"));
            Assert.EndsWith("_console.exe", c.Path);
            Assert.True(c.Satisfied);
        }

        [Fact]
        public void AndTurnsItOffAgain()
        {
            // ⚠ THE BUG THIS EXISTS FOR. A one-directional implementation appends "_console" when asked and
            // does nothing when not -- so if the RESOLVED path is already a console build, unticking the box
            // changes nothing and the launcher reports a windowed start it did not perform. Starting from a
            // console path and asking for windowed must give the windowed binary.
            var c = LauncherRules.GodotExeFor(@"C:\g\Godot_v4.6_mono_win64_console.exe", false,
                                              Has(@"C:\g\Godot_v4.6_mono_win64.exe", @"C:\g\Godot_v4.6_mono_win64_console.exe"));
            Assert.Equal(@"C:\g\Godot_v4.6_mono_win64.exe", c.Path);
            Assert.True(c.Satisfied);
        }

        [Fact]
        public void VersionDotIsNotAFileExtension()
        {
            // ⚠ Path.GetExtension("Godot_v4.6") returns ".6". Splitting there yields "Godot_v4_console.6",
            // which cannot exist -- so the toggle silently does nothing on extensionless unix builds.
            var c = LauncherRules.GodotExeFor("/opt/Godot_v4.6", true, Has("/opt/Godot_v4.6_console", "/opt/Godot_v4.6"));
            Assert.Equal("/opt/Godot_v4.6_console", c.Path);
            Assert.True(c.Satisfied);
        }

        [Fact]
        public void UnsatisfiedWhenTheWantedBuildIsAbsent()
        {
            // The caller must be able to tell it did not get what it asked for, so it can log what it DID.
            var c = LauncherRules.GodotExeFor(@"C:\g\Godot.exe", true, Has(@"C:\g\Godot.exe"));
            Assert.False(c.Satisfied);
            Assert.Equal(@"C:\g\Godot.exe", c.Path);
        }

        [Fact]
        public void NullsDoNotThrow()
        {
            Assert.False(LauncherRules.GodotExeFor(null, true, p => true).Satisfied);
            Assert.False(LauncherRules.GodotExeFor(@"C:\g\Godot.exe", true, null).Satisfied);
        }
    }

    public class SelfUpdateTests
    {
        [Theory]
        [InlineData(1, "2", true)]     // newer published -> update
        [InlineData(2, "2", false)]    // ⚠ equal must NOT update, or it downloads itself forever
        [InlineData(3, "2", false)]    // ⚠ lower must NOT update, or a bad publish drags everyone backwards
        [InlineData(1, "", false)]
        [InlineData(1, null, false)]
        [InlineData(1, "<!DOCTYPE html>", false)]   // ⚠ a 404 page must never start a self-replacement
        [InlineData(1, "  2  ", true)]             // whitespace is normal in a one-line file
        public void OnlyStrictlyGreaterUpdates(int current, string published, bool expected) =>
            Assert.Equal(expected, LauncherRules.ShouldSelfUpdate(current, published));
    }

    public class GameDataTests
    {
        static readonly GameVariant Pal = new() { Id = "TEST-PAL", Name = "Test PAL", Region = "PAL", TickSeconds = 0.04 };

        [Fact]
        public void MissingHashIsMissingNotUnrecognised()
        {
            // Different states because they need different words to the user: one says "find your copy",
            // the other says "your copy is not one we know".
            Assert.Equal(GameDataState.Missing, GameData.Identify(null).State);
            Assert.Equal(GameDataState.Missing, GameData.Identify("   ").State);
        }

        [Fact]
        public void UnknownHashRefusesAndNamesItself()
        {
            var r = GameData.Identify("aabbccdd", new Dictionary<string, GameVariant>());
            Assert.Equal(GameDataState.Unrecognised, r.State);
            Assert.False(r.CanPlay);
            // ⚠ The hash must appear in the message. Without it the user cannot tell us what they have and we
            // cannot add it -- the dead end stays a dead end.
            Assert.Contains("aabbccdd", r.Message);
        }

        [Fact]
        public void KnownHashCarriesItsTickRate()
        {
            // The whole reason identification happens before anything is read: PAL and NTSC tick differently,
            // so the variant supplies the constant rather than the sim assuming one.
            var r = GameData.Identify("HASH1", new Dictionary<string, GameVariant>(StringComparer.OrdinalIgnoreCase) { ["hash1"] = Pal });
            Assert.True(r.CanPlay);
            Assert.Equal(0.04, r.Variant.TickSeconds);
        }

        [Fact]
        public void TheShippedTableIsEmptyAndThatIsDeliberate()
        {
            // ⚠ This asserts an ABSENCE on purpose. Nobody has hashed a real disc, so the launcher refuses
            // every copy -- correct, not a gap. If someone adds an invented entry to make the flow "work",
            // this test fails and makes them say so out loud.
            Assert.Empty(GameData.Known);
            Assert.Equal(GameDataState.Unrecognised, GameData.Identify("anything-at-all").State);
        }
    }
}
