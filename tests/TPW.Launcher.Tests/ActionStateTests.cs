using TPW.Launcher;
using Xunit;

namespace TPW.Launcher.Tests
{
    /// <summary>The button's caption IS the feature, so these pin the caption, not just the enum.
    /// Each case names the wrong answer it exists to reject.</summary>
    public class ActionStateTests
    {
        const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";   // a commit
        const string Bc = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";  // a different commit

        static ActionState S(string local, string remote, string built, bool canPlay = true)
            => LauncherState.Decide(local, remote, built, canPlay, "main");

        [Fact]
        public void FreshMachineInstalls()
        {
            var s = S("", A, "");
            Assert.Equal(ActionKind.Build, s.Kind);
            Assert.Equal("Install & Play", s.Label);
        }

        // Rejects: "data is checked first", which would send a new user disc-hunting before the long step.
        [Fact]
        public void FreshMachineInstallsEvenWithNoGameData()
            => Assert.Equal("Install & Play", S("", A, "", canPlay: false).Label);

        // Rejects: inferring "built" from the clone existing. A clone is not a build.
        [Fact]
        public void ClonedButNeverBuiltMustBuild()
        {
            var s = S(A, A, "");
            Assert.Equal(ActionKind.Build, s.Kind);
            Assert.Equal("Build & Play", s.Label);
        }

        [Fact]
        public void BuiltAndCurrentPlays()
        {
            var s = S(A, A, A);
            Assert.Equal(ActionKind.Play, s.Kind);
            Assert.Equal("Play", s.Label);
        }

        [Fact]
        public void BuiltButBehindUpdates()
        {
            var s = S(A, Bc, A);
            Assert.Equal(ActionKind.Build, s.Kind);
            Assert.Equal("Update & Play", s.Label);
        }

        // ⭐ THE STALE-BUILD TRAP, and the reason the marker exists at all. After a branch switch the working
        // tree is at Bc while the marker still records A. Everything on disk looks present. If this ever
        // returns Play, the user runs assemblies built from a different commit and gets a crash that looks
        // like a bug in the game.
        [Fact]
        public void MarkerFromAnotherCommitIsNotABuild()
        {
            var s = S(Bc, Bc, A);
            Assert.Equal(ActionKind.Build, s.Kind);
            Assert.Equal("Build & Play", s.Label);
        }

        // Rejects: treating an unreachable remote as an error. Offline with a good build must still play.
        [Fact]
        public void OfflineWithAGoodBuildStillPlays()
            => Assert.Equal(ActionKind.Play, S(A, "", A).Kind);

        // Rejects: "no remote hash" reading as "behind" (empty != A would be true under a naive compare).
        [Fact]
        public void OfflineIsNotMistakenForBehind()
            => Assert.NotEqual("Update & Play", S(A, "", A).Label);

        [Fact]
        public void OfflineOnAFreshMachineIsStillActionable()
        {
            var s = S("", "", "");
            Assert.Equal(ActionKind.Build, s.Kind);
            Assert.True(s.Enabled);
        }

        // The gate that keeps the bring-your-own-assets promise: current in every respect, but no disc.
        [Fact]
        public void UpToDateWithoutGameDataAsksForIt()
        {
            var s = S(A, A, A, canPlay: false);
            Assert.Equal(ActionKind.NeedData, s.Kind);
            Assert.Contains("Locate", s.Label);
            Assert.True(s.Enabled);   // it must be clickable -- it opens the picker
        }

        // Rejects: a marker alone satisfying the build test when nothing is checked out.
        [Fact]
        public void MarkerWithoutACheckoutIsNotABuild()
            => Assert.Equal(ActionKind.Build, S("", A, A).Kind);

        [Theory]
        [InlineData(false, true, "git")]
        [InlineData(true, false, "dotnet")]
        public void MissingToolchainIsBrokenAndSaysWhich(bool git, bool dotnet, string expect)
        {
            var s = LauncherState.Decide(A, A, A, true, "main", haveGit: git, haveDotnet: dotnet);
            Assert.Equal(ActionKind.Broken, s.Kind);
            Assert.False(s.Enabled);
            Assert.Contains(expect, s.Status);
        }

        // Null must behave as "absent", not throw: these arrive from file reads that return null when missing.
        [Fact]
        public void NullsAreTreatedAsAbsent()
            => Assert.Equal(ActionKind.Build, LauncherState.Decide(null, null, null, true, null).Kind);

        // The status line names the branch, so "install from a work branch" is visibly not "install from main".
        [Fact]
        public void FirstRunStatusNamesTheBranch()
            => Assert.Contains("wip", LauncherState.Decide("", A, "", true, "wip").Status);
    }
}
