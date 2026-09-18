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
    /// ✅ SOURCE, CORRECTED 2026-09-18. An earlier version of this note said "both numbers are MEASURED LIVE".
    /// That was true of one of them. Split properly:
    ///   TicksPerDay = 99   MEASURED, and since corroborated FOUR ways that share no method -- the guest bus
    ///                      at 694 ticks = 7.01 days; the root-counter day unit 0xF0000 at ~3.9 s; month
    ///                      rollover at 2772/99 = 28 and 3069/99 = 31 days exactly (calendar month lengths,
    ///                      which an hour-sized unit could not produce); and a day-of-month field wrapping at
    ///                      30 in lockstep.
    ///   FramesPerTick = 2  MEASURED, and it is a RATIO, so it is exact and region-independent.
    ///   TickSeconds        DERIVED, never measured. It is FramesPerTick over an ASSUMED PAL 50 Hz. The frame
    ///                      rate is the single place real-time enters this whole model, and nobody has watched
    ///                      it on hardware.
    /// The 20.12 fixed-point format in `Fixed` is not covered by any of this and remains fable's alone.
    ///
    /// ⚠⚠ AND 99 IS THE NUMBER THAT NEARLY GOT AWAY. Sampled every 600 frames it reads as exactly 100: one
    /// blip in eighteen intervals is the only thing that says otherwise, and it took 300x finer sampling to
    /// see. A coarse instrument cannot resolve a phenomenon finer than its interval, and it does not report
    /// uncertainty -- it reports a confident round number. 100 is the most dangerous possible wrong answer
    /// here precisely BECAUSE it looks like a designed constant; 97.3 would have been investigated on sight.
    ///
    /// ⚠ TickSeconds IS REGION-DEPENDENT; TicksPerDay IS NOT. A tick is 2 frames on every disc, but that is
    /// 50 Hz on PAL (SLES) and 60 Hz on NTSC (SLUS), giving 0.04 s or 0.0333 s. TicksPerDay is a count of
    /// ticks and does not move with the region. Only the seconds do.
    ///
    /// ✅ 99 RE-CONFIRMED 2026-09-18, INDEPENDENTLY, TWICE — and my challenge to it was wrong. I argued the
    /// label had to be an HOUR, because tinyclaw's guest bus runs every ~694 ticks, which against 99 is a bus
    /// every 7 in-game days with each day lasting 3.96 real seconds. Both looked absurd. They are both simply
    /// true: `findings/arrivals.md` measures the bus at "694 ticks = 7.01 game days" (694/7.01 = 99.0), and
    /// separately measures the root-counter day unit 0xF0000 at ~3.9 s. Two measurements sharing no method,
    /// both landing on this constant.
    ///
    /// ⚠ I CHALLENGED THE WRONG CONSTANT, BUT THE SMELL WAS REAL. I argued 99 was mislabelled and should be
    /// an hour, on the grounds that a 3.96-second day is absurd. 99 was never the problem -- TickSeconds was,
    /// and every quantity I called absurd was a SECONDS quantity. The tick relationships were all sound; the
    /// one number converting them to wall time was the guess.
    ///
    /// Two things to take from it. First, when a derived value looks wrong, suspect the conversion before the
    /// measurement: I had one assumption and three measurements, and I accused the measurements. Second, the
    /// hedge is what saved it -- the argument went out as a question and the constant was left alone, so being
    /// wrong cost one message instead of silently repricing wages, ride lifetimes and arrivals at once.
    /// Plausibility is not a measurement, in either direction: it was not evidence against 99, and it is not
    /// evidence for 50 Hz either.
    public sealed class ParkClock
    {
        /// <summary>Frames per sim tick. ⭐ MEASURED, and the durable one: it is a ratio, so it holds on every
        /// region and every frame rate. Prefer it over TickSeconds anywhere a comparison can be expressed in
        /// ticks or frames -- if you ever see a stray factor of two between a tick count and a frame count,
        /// this is it.</summary>
        public const int FramesPerTick = 2;

        /// <summary>Seconds of wall time one sim tick represents, at a given frame rate. ⚠ DERIVED. The frame
        /// rate is an input because it is the ONE assumption in this clock: 50 Hz PAL is inherited, not
        /// observed. Pass the rate from the variant table so a wrong guess is visible at the call site rather
        /// than baked into a constant everything quietly trusts.</summary>
        public static double SecondsPerTick(double frameHz) => FramesPerTick / frameHz;

        /// <summary>Seconds per tick assuming PAL 50 Hz. Presentation only -- see the class note.
        /// ⚠ DERIVED, NOT MEASURED, and the only real-time number here. An NTSC disc runs the same measured
        /// 2-frames-per-tick at 60 Hz and gets 0.0333 s. This belongs in the per-variant table the checksum
        /// already selects; it is a const only until a second SKU lands. ⚠⚠ DO NOT DESIGN PACING, FEEL OR
        /// ANIMATION AGAINST IT -- a game day is under four seconds under this assumption, which is either the
        /// real article or the assumption showing, and nobody has watched it on hardware to say which.</summary>
        public const double TickSeconds = FramesPerTick / 50.0;

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
