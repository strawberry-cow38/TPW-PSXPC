namespace TPW.Sim
{
    /// <summary>A ride's CLOSING PROGRESS word, A+0xEC, and the four routines that read and move it
    /// (0x8009C1E8, 0x8009C294, 0x8009EEE4, 0x8009EFA8; behaviour.md §3.5 correction of 2026-09-20).
    ///
    /// ⭐ THIS IS NOT A RIDER COUNT. behaviour.md guessed "riders still on it" and the first port of the
    /// mechanic wrote its interface as "the ride has emptied out"; a host that wires that up to
    /// A+0xF0 (riders) has a mechanic who starts work the moment the last guest leaves and reopens with
    /// no delay. The word is a 20.12 fixed-point timer the ride runs UP while a mechanic (or the
    /// condemned-ride update 0x8009CCD4) is closing it and DOWN while he is reopening it, and the
    /// finish line scales with the ride's footprint: `10 * (width + height)` whole units
    /// (0x8009C260..288 computes it as `(5*(w+h)) &lt;&lt; 13`). A 2x2 ride closes after 40.0 units of
    /// progress, a 4x4 after 80.0. Riders are irrelevant to it and it is irrelevant to riders.
    ///
    /// ⚠ CLOSING AND REOPENING USE DIFFERENT PREDICATES. Closing (states 16/52) waits for
    /// <see cref="IsComplete"/>: progress AT OR ABOVE the threshold. Reopening (state 17) waits for
    /// progress EXACTLY ZERO (0x8009C294, `sltiu v0,v0,1`). The first port used one predicate for both.
    ///
    /// The step each tick is 0x800BDD0C, the global 0x80103A90 which 0x800BDE6C sets from a timer and
    /// caps at 0x4000 (4.0 units); the host's clock owns that value and this class only consumes it.</summary>
    public static class RideClosing
    {
        /// <summary>Whole units of progress per tile of footprint span (width + height). READ at
        /// 0x8009C264..26C: `(s0 &lt;&lt; 2) + s0` is ×5, then `&lt;&lt; 13`, i.e. 10 per tile in 20.12.</summary>
        public const int UnitsPerSpanTile = 10;

        /// <summary>The finish line for a ride whose footprint is `width + height` tiles across.</summary>
        public static int Threshold(int footprintSpan) => (UnitsPerSpanTile * footprintSpan) << Fixed.FracBits;

        /// <summary>0x8009C1E8: closing is complete once progress has reached the threshold. `>=`, not
        /// `>`: the last raise clamps EXACTLY to the threshold (<see cref="Raise"/>), so `>` would never fire.</summary>
        public static bool IsComplete(int progress, int threshold) => progress >= threshold;

        /// <summary>0x8009EEE4: one tick of closing. Adds the step and clamps to the threshold.</summary>
        public static int Raise(int progress, int step, int threshold)
        {
            int next = progress + step;
            return threshold < next ? threshold : next;
        }

        /// <summary>0x8009EFA8: one tick of reopening. Subtracts the step and clamps at zero.</summary>
        public static int Lower(int progress, int step)
        {
            int next = progress - step;
            return next < 0 ? 0 : next;
        }
    }
}
