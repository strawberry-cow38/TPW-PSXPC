using System;

namespace TPW.Launcher
{
    /// <summary>What the launcher's single action button currently is.</summary>
    public enum ActionKind
    {
        Broken,     // a prerequisite is missing; nothing to click
        NeedData,   // ready to run, but the user's copy of the game has not been identified
        Build,      // clone / build / update needed before anything can run
        Play,       // everything agrees; go
    }

    public readonly struct ActionState
    {
        public readonly ActionKind Kind;
        public readonly string Label;    // the button caption
        public readonly string Status;   // the line beside it
        public ActionState(ActionKind k, string label, string status) { Kind = k; Label = label; Status = status; }
        public bool Enabled => Kind != ActionKind.Broken;
    }

    public static class LauncherState
    {
        /// <summary>Decide the one button from the whole world state.
        ///
        /// ⭐ THE POINT OF MERGING Install/Update/Play INTO ONE BUTTON is that these are stages of a single
        /// intent ("I want to play"), not a menu. Offering Play beside Update lets someone Play a stale build,
        /// which is the one combination that produces a confusing crash instead of an honest refusal.
        ///
        /// ⚠ THE ORDER OF THESE TESTS IS NOT THE ORDER OF THE PIPELINE, deliberately. Game data is needed
        /// sooner at runtime than a build is, but it is asked for LAST, because building is the slow step:
        /// sending someone to hunt for a disc and THEN making them sit through a five-minute build is the
        /// same two waits arranged so the annoying one is discovered last.
        ///
        /// <paramref name="localHash"/> empty = not cloned. <paramref name="builtHash"/> is the commit our own
        /// build marker records, which is the ONLY evidence a build exists -- never infer it from a directory.
        /// <paramref name="remoteHash"/> empty = the remote was unreachable, which must stay actionable
        /// offline rather than becoming Broken.</summary>
        public static ActionState Decide(
            string localHash, string remoteHash, string builtHash, bool canPlay, string branch,
            bool haveGit = true, bool haveDotnet = true)
        {
            if (!haveGit) return new ActionState(ActionKind.Broken, "—", "git not found — install it, then reopen.");
            if (!haveDotnet) return new ActionState(ActionKind.Broken, "—", "dotnet SDK not found — install it, then reopen.");

            localHash = localHash ?? ""; remoteHash = remoteHash ?? ""; builtHash = builtHash ?? "";
            branch = string.IsNullOrEmpty(branch) ? "main" : branch;

            if (localHash.Length == 0)
                return new ActionState(ActionKind.Build, "Install & Play",
                    remoteHash.Length > 0 ? $"First run — install from {branch}." : "First run — remote unreachable, try anyway.");

            // All three must agree: we built something, it is what is checked out, and that is the branch tip.
            // A branch switch is self-correcting here -- the marker still holds the old commit, so !isBuilt.
            bool isBuilt = builtHash.Length > 0 && builtHash == localHash;
            bool behind = remoteHash.Length > 0 && remoteHash != localHash;

            if (!isBuilt) return new ActionState(ActionKind.Build, "Build & Play", "Fetched, but not built yet.");
            if (behind) return new ActionState(ActionKind.Build, "Update & Play", "Update available.");
            if (!canPlay)
                // ⚠ THIS REFUSAL IS THE BRING-YOUR-OWN-ASSETS PROMISE. A port that half-runs on an
                // unrecognised copy produces plausible nonsense, which is worse than not starting.
                return new ActionState(ActionKind.NeedData, "Locate game data…", "Up to date — point me at your copy of the game.");
            return new ActionState(ActionKind.Play, "Play", "Up to date.");
        }
    }
}
