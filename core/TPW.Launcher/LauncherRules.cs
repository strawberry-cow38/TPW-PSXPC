using System;

namespace TPW.Launcher
{
    public static class LauncherRules
    {
        /// <summary>Pick the Godot binary for a given "debug console" preference.
        ///
        /// ⚠ SYMMETRIC ON PURPOSE. The obvious implementation appends "_console" when the box is ticked and
        /// does nothing when it is not -- which is only correct if the resolved Godot is always the windowed
        /// build. It is not: the path can come from an env var or a bare `godot` on PATH, either of which may
        /// already BE a console build, and on such a machine unticking the box does nothing at all. A toggle
        /// that cannot turn a thing off is not a toggle. So: strip any existing suffix to get the base, then
        /// add it back only if wanted.
        ///
        /// ⚠ AND DO NOT USE Path.GetExtension TO SPLIT THIS. A Godot binary is
        /// `Godot_v4.6-stable_mono_win64.exe`, and an extensionless unix build is `Godot_v4.6` -- on which
        /// GetExtension returns ".6", because the VERSION NUMBER contains a dot. Splitting there produces
        /// `Godot_v4_console.6`, which cannot exist, so the toggle fails silently on exactly the platform
        /// where nothing else would notice.</summary>
        public static GodotChoice GodotExeFor(string resolved, bool wantConsole, Func<string, bool> exists)
        {
            if (string.IsNullOrEmpty(resolved) || exists == null) return new GodotChoice(resolved, false);

            const string Exe = ".exe";
            const string Suffix = "_console";

            string stem = resolved, ext = "";
            if (stem.EndsWith(Exe, StringComparison.OrdinalIgnoreCase))
            {
                ext = stem.Substring(stem.Length - Exe.Length);
                stem = stem.Substring(0, stem.Length - Exe.Length);
            }
            if (stem.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
                stem = stem.Substring(0, stem.Length - Suffix.Length);

            string want = wantConsole ? stem + Suffix + ext : stem + ext;
            if (exists(want)) return new GodotChoice(want, true);

            // Asked-for variant is not on disk. Fall back to the other, and say so via Satisfied=false rather
            // than pretending.
            string other = wantConsole ? stem + ext : stem + Suffix + ext;
            if (exists(other)) return new GodotChoice(other, false);

            return new GodotChoice(resolved, false);
        }

        /// <summary>Whether to run Godot's CONSOLE build, given the contents of debug_console.txt
        /// (null when the file does not exist).
        ///
        /// ⚠ THE DEFAULT IS OFF, AND IT USED TO BE ON. With no preference file -- i.e. for every
        /// first-time user -- the launcher picked the *_console build of Godot, which opens a terminal
        /// window beside the game. The port therefore looked broken-by-default to anyone who had never
        /// found the checkbox, and the checkbox was itself inside a collapsed expander. Debug output is
        /// opt-in.
        ///
        /// ⚠ AND ONLY "1" IS ON. Not "true", not "yes", not "anything non-empty" -- an unreadable or
        /// half-written file must fall back to the quiet default rather than to the noisy one, because
        /// the failure mode of guessing wrong here is the bug above.</summary>
        public static bool WantConsole(string fileContents) => (fileContents ?? "0").Trim() == "1";

        /// <summary>Should the launcher replace itself with the published build?
        ///
        /// ⚠ STRICTLY GREATER, never "different". An equal version must not trigger an update or the launcher
        /// downloads itself forever; a LOWER published version must not either, or a bad publish drags every
        /// user backwards with no way out. Unparseable input means do nothing -- a 404 page read as a version
        /// number should never be able to start a self-replacement.</summary>
        public static bool ShouldSelfUpdate(int current, string publishedRaw)
        {
            if (string.IsNullOrWhiteSpace(publishedRaw)) return false;
            if (!int.TryParse(publishedRaw.Trim(), out int published)) return false;
            return published > current;
        }
    }
}
