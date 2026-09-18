namespace TPW.Launcher
{
    /// <summary>Which Godot binary to launch, and whether the one asked for was actually found.</summary>
    public readonly struct GodotChoice
    {
        public readonly string Path;
        /// <summary>False when the requested console/windowed variant was not on disk and the caller is
        /// getting the other one. ⚠ The caller must LOG WHAT IT DID rather than what it intended -- a launcher
        /// that says "starting the windowed build" while starting the console one is worse than silent,
        /// because it actively teaches the user the toggle works.</summary>
        public readonly bool Satisfied;
        public GodotChoice(string path, bool satisfied) { Path = path; Satisfied = satisfied; }
    }
}
