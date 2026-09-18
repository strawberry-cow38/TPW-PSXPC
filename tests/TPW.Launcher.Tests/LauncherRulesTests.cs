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

    public class SelfUpdateVersionTests
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
        static Dictionary<string, KnownHash> Table(string hash) =>
            new(StringComparer.OrdinalIgnoreCase) { [hash] = new KnownHash { Variant = Pal, What = HashedThing.BootExecutable } };

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
            var r = GameData.Identify("aabbccdd", new Dictionary<string, KnownHash>());
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
            var r = GameData.Identify("HASH1", Table("hash1"));
            Assert.True(r.CanPlay);
            Assert.Equal(0.04, r.Variant.TickSeconds);
        }

        [Fact]
        public void TheShippedTableRecognisesEveryMeasuredHashOfTheOneReleaseAndNothingElse()
        {
            // ⚠ BOTH HALVES MATTER. The first assertion says the one entry is real and works; the second says
            // the table has not quietly grown guesses. A variant added to make a flow "work" -- without a
            // hash someone actually took -- is a lie whose only symptom is wrong offsets producing plausible
            // garbage, so the count is pinned and anyone adding one has to change this line deliberately.
            Assert.Equal(3, GameData.Known.Count);

            // ⭐ The stable key: a boot executable is identical across every dump of the same disc.
            var exe = GameData.Identify("e5cee3b51a26ee3a6965cf20f4394f1c9bb9d741");
            Assert.True(exe.CanPlay);
            Assert.Equal(HashedThing.BootExecutable, exe.What);
            Assert.Equal("PAL", exe.Variant.Region);
            Assert.Equal(0.04, exe.Variant.TickSeconds);   // 2 frames/tick (measured) over 50 Hz (assumed)

            // ⚠ The rip-sensitive one still resolves. Migrating to a better key must not strand the copy
            // already in use -- so this asserts the OLD key keeps working, which is the half a migration
            // usually forgets.
            var img = GameData.Identify("2167C58486F14183E393F2010D33ABBDF958953D");
            Assert.True(img.CanPlay);
            Assert.Equal(HashedThing.DiscImage, img.What);

            // All three hashes are the SAME release, not three releases.
            Assert.Equal(exe.Variant.Id, img.Variant.Id);

            Assert.Equal(GameDataState.Unrecognised, GameData.Identify("anything-at-all").State);
        }

        [Fact]
        public void HashComparisonIsCaseInsensitive()
        {
            // Get-FileHash yields uppercase, sha1sum lowercase, and a user reporting a hash will type either.
            // A case-sensitive table rejects a copy it actually knows, which reads to the user as "your game
            // is wrong" -- the most confusing possible failure.
            Assert.True(GameData.Identify("2167c58486f14183e393f2010d33abbdf958953d").CanPlay);
            Assert.True(GameData.Identify("E5CEE3B51A26EE3A6965CF20F4394F1C9BB9D741").CanPlay);
        }
    }
}

namespace TPW.Launcher.Tests
{
    // ⚠ These guard an IRREVERSIBLE act: the bytes that pass here get written over the only copy of the
    // launcher the user has. Every case below is a way that has actually gone wrong for someone.
    public class SelfUpdateTests
    {
        static byte[] FakeExe(int n = SelfUpdate.MinPlausibleBytes)
        {
            var b = new byte[n];
            b[0] = (byte)'M'; b[1] = (byte)'Z';
            return b;
        }

        [Fact]
        public void AnErrorPageIsRefused()
        {
            // ⚠ THE COMMON DISASTER. A 404 or a redirect body is a SUCCESSFUL http response, so nothing
            // upstream complains -- and "<!DOCTYPE html>" copied over the exe leaves the user with no
            // launcher and no way to get one.
            var html = System.Text.Encoding.ASCII.GetBytes("<!DOCTYPE html><html>404</html>");
            Assert.False(SelfUpdate.Check(html, null).Accept);
        }

        [Fact]
        public void ATruncatedDownloadIsRefused()
        {
            var half = FakeExe(1_000_000);   // starts MZ, far too small
            Assert.False(SelfUpdate.Check(half, null).Accept);
        }

        [Fact]
        public void EmptyAndNullAreRefused()
        {
            Assert.False(SelfUpdate.Check(Array.Empty<byte>(), null).Accept);
            Assert.False(SelfUpdate.Check(null, null).Accept);
        }

        [Fact]
        public void AHashMismatchIsRefusedEvenThoughItIsAValidExe()
        {
            // The shape checks cannot tell a good exe from the WRONG good exe. This is the case where the
            // download succeeded completely and is still not what was published.
            var exe = FakeExe();
            Assert.False(SelfUpdate.Check(exe, "00".PadRight(64, '0')).Accept);
        }

        [Fact]
        public void AMatchingHashIsAccepted()
        {
            var exe = FakeExe();
            var v = SelfUpdate.Check(exe, SelfUpdate.Sha256Of(exe));
            Assert.True(v.Accept);
            Assert.Contains("hash matches", v.Reason);
        }

        [Fact]
        public void NoPublishedHashAcceptsButSaysTheStrongCheckDidNotRun()
        {
            // ⚠ Absent expectation is neither pass nor fail. Accepting silently is how verification becomes
            // decorative -- the reason string is what keeps a launcher that stopped hash-checking honest in
            // its own log.
            var v = SelfUpdate.Check(FakeExe(), null);
            Assert.True(v.Accept);
            Assert.Contains("no published hash", v.Reason);
        }
    }
}
