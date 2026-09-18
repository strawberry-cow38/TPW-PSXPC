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
    /// ✅ SOURCE, UPGRADED 2026-09-18: both numbers are now MEASURED LIVE off the running game by tinyclaw,
    /// independently of fable's report, and both match it. The clock ticks at exactly half the frame rate, and
    /// a game day is exactly 99 ticks. The 20.12 fixed-point format in `Fixed` is NOT covered by that and
    /// remains fable's alone -- see its own note.
    ///
    /// ⚠⚠ AND 99 IS THE NUMBER THAT NEARLY GOT AWAY. Sampled every 600 frames it reads as exactly 100: one
    /// blip in eighteen intervals is the only thing that says otherwise, and it took 300x finer sampling to
    /// see. A coarse instrument cannot resolve a phenomenon finer than its interval, and it does not report
    /// uncertainty -- it reports a confident round number. 100 is the most dangerous possible wrong answer
    /// here precisely BECAUSE it looks like a designed constant; 97.3 would have been investigated on sight.
    ///
    /// ⚠ TickSeconds IS REGION-DEPENDENT AND 0.04 ASSUMES PAL. "Half the frame rate" is 25 Hz on a 50 Hz PAL
    /// machine (SLES) and 30 Hz on a 60 Hz NTSC one (SLUS) -- so the same rule gives 0.04 s or 0.0333 s
    /// depending on the disc. TicksPerDay is a count and does not change; this does. It belongs in the
    /// per-variant table the checksum selects, not in a const, the day a second SKU is supported.</summary>
    public sealed class ParkClock
    {
        /// <summary>Seconds of wall time one sim tick represents. Presentation only -- see the class note.
        /// ⚠ PAL value. NTSC discs run the same rule at 60 Hz and get 0.0333 s; this moves to the per-variant
        /// table when a second SKU lands.</summary>
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
