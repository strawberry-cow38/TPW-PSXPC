namespace TPW.Sim
{
    /// <summary>The game's 20.12 fixed-point number: a 32-bit integer holding value * 4096.
    ///
    /// ⚠ WHY THIS EXISTS AT ALL, rather than using float. The PSX has no FPU worth the name, so the original
    /// does its arithmetic in fixed point -- and a reimplementation that switches to float will agree with it
    /// for a while and then diverge, because the rounding is different at every operation. Divergence that
    /// starts small and accumulates is the hardest kind to attribute later: by the time a number is visibly
    /// wrong, thousands of correct-looking operations sit between you and the cause.
    ///
    /// So the sim carries the original's representation and its rounding, and the oracle can then compare
    /// exact integers rather than "close enough" floats.
    ///
    /// ⚠ SOURCE AND ITS STANDING: the 20.12 format is reported in the PSX findings (`findings/rides.json`,
    /// "fixed_point": "20.12 (x4096) where noted"), which is DERIVED analysis, not something this project has
    /// independently verified against the running game. Two of those findings' offsets have already been
    /// corrected by live reads. So treat the SHAPE as established and any specific rounding behaviour as
    /// unconfirmed until a fixture says otherwise -- which is exactly what `TPW.Sim.Tests` is for.</summary>
    public readonly struct Fixed : System.IEquatable<Fixed>, System.IComparable<Fixed>
    {
        /// <summary>Fractional bits. 20.12 means 12 of them, so one whole unit is 4096.</summary>
        public const int FracBits = 12;
        public const int One = 1 << FracBits;   // 4096

        /// <summary>The stored integer. This IS the game's value -- compare these, not the decimal form.</summary>
        public readonly int Raw;

        public Fixed(int raw) => Raw = raw;

        public static Fixed FromRaw(int raw) => new(raw);
        public static Fixed FromInt(int whole) => new(whole << FracBits);

        /// <summary>⚠ For reading test data and printing only. Going through double loses the exactness this
        /// type exists to preserve, so it must not appear in sim arithmetic.</summary>
        public static Fixed FromDouble(double v) => new((int)System.Math.Round(v * One));
        public double ToDouble() => Raw / (double)One;

        /// <summary>Truncation toward NEGATIVE infinity, which is what an arithmetic shift does -- and the
        /// original is doing a shift. C#'s `/` truncates toward ZERO instead, so -1 >> 12 and -1 / 4096 give
        /// different answers (-1 and 0). Using the wrong one is a one-off error that only shows on negatives,
        /// i.e. exactly where nobody tests.</summary>
        public int ToIntFloor() => Raw >> FracBits;

        public static Fixed operator +(Fixed a, Fixed b) => new(a.Raw + b.Raw);
        public static Fixed operator -(Fixed a, Fixed b) => new(a.Raw - b.Raw);
        public static Fixed operator -(Fixed a) => new(-a.Raw);

        /// <summary>⚠ THE PRODUCT NEEDS 64 BITS BEFORE THE SHIFT. Two 20.12 values multiplied hold 24
        /// fractional bits and can overflow a 32-bit intermediate long before either operand is large --
        /// 512.0 * 512.0 already does it. Widening to long first and shifting after is not defensive
        /// programming, it is the only version that is correct.</summary>
        public static Fixed operator *(Fixed a, Fixed b) => new((int)(((long)a.Raw * b.Raw) >> FracBits));

        /// <summary>Likewise: shift the numerator UP into 64 bits before dividing, or the fractional bits are
        /// gone before the division ever happens.</summary>
        public static Fixed operator /(Fixed a, Fixed b) => new((int)(((long)a.Raw << FracBits) / b.Raw));

        public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;
        public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;
        public static bool operator <(Fixed a, Fixed b) => a.Raw < b.Raw;
        public static bool operator >(Fixed a, Fixed b) => a.Raw > b.Raw;
        public static bool operator <=(Fixed a, Fixed b) => a.Raw <= b.Raw;
        public static bool operator >=(Fixed a, Fixed b) => a.Raw >= b.Raw;

        public bool Equals(Fixed other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fixed f && Equals(f);
        public override int GetHashCode() => Raw;
        public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);

        /// <summary>Prints the raw alongside the decimal, because the raw is the thing that has to match the
        /// game and a decimal alone hides a one-bit disagreement.</summary>
        public override string ToString() => $"{ToDouble():0.####} (raw {Raw})";
    }
}
