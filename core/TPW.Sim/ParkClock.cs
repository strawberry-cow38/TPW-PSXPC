namespace TPW.Sim
{
    /// <summary>The park's clock, in the original's own units.
    ///
    /// ⚠ THE TICK IS THE UNIT OF TRUTH, NOT THE SECOND. Every sim quantity the findings record -- ride
    /// lifetimes, arrival cadence, wage rollover -- is counted in ticks, so the sim advances in whole ticks
    /// and seconds are a presentation detail. A reimplementation that steps on frame delta instead inherits
    /// the frame rate into its economy, which is precisely the bug unturnedGD's weather has (it integrates on
    /// `delta * Cycle.Speed` and diverges per client). Getting this right once, here, is cheaper than finding
    /// it later in six places.
    ///
    /// ⚠ SOURCE: `tick_s = 0.04` and `day_ticks = 99` come from the PSX findings (`findings/rides.json`,
    /// units block) -- DERIVED, from analysis, not independently confirmed against the running game by this
    /// project. 0.04 s is 25 Hz, which is a plausible PAL-ish sim rate and not proof of anything. 99 ticks a
    /// day is odd enough to be real rather than rounded, which is mild evidence it was read rather than
    /// guessed. Both want a fixture before anything depends on them numerically.</summary>
    public sealed class ParkClock
    {
        /// <summary>Seconds of wall time one sim tick represents. Presentation only -- see the class note.</summary>
        public const double TickSeconds = 0.04;

        /// <summary>Ticks in one in-game day.</summary>
        public const int TicksPerDay = 99;

        /// <summary>Ticks since the park was created. The authoritative clock: everything else derives from it,
        /// so two runs on the same input agree by construction rather than by luck.</summary>
        public long Tick { get; private set; }

        public int Day => (int)(Tick / TicksPerDay);
        public int TickOfDay => (int)(Tick % TicksPerDay);

        /// <summary>True on the tick a new day begins -- the edge, not the state. Callers that want "has the
        /// day rolled" must ask on the tick it happens; asking later gets false, which is the correct answer
        /// to a different question. Wages and other daily events hang off this.</summary>
        public bool IsDayRollover => Tick > 0 && TickOfDay == 0;

        public void Advance() => Tick++;

        /// <summary>⚠ ADVANCE IN WHOLE TICKS, one at a time, never by a span. Anything that happens ON a tick
        /// -- a rollover, an arrival, a ride finishing -- is skipped by a caller that jumps the counter, and
        /// the skip is silent. If a caller needs to fast-forward it still pays for every tick.</summary>
        public void Advance(int ticks)
        {
            for (int i = 0; i < ticks; i++) Advance();
        }

        /// <summary>Restore a clock from a saved tick count. Used by fixtures so a comparison against the
        /// oracle can start anywhere rather than only from tick zero.</summary>
        public static ParkClock At(long tick) => new() { Tick = tick };

        public override string ToString() => $"day {Day}, tick {TickOfDay}/{TicksPerDay} (abs {Tick})";
    }
}
