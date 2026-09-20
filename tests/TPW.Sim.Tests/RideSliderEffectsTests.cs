using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class RideSliderEffectsTests
    {
        // REJECTS using only one slider, swapping divisors, omitting either factor's upper/lower
        // clamp, rounding instead of truncating, and allowing intensity past 100.
        [Theory]
        [InlineData(60, 50, 5, 45)]
        [InlineData(60, 100, 5, 60)]
        [InlineData(60, 100, 7, 75)]
        [InlineData(60, 1, 1, 33)]
        [InlineData(60, 200, 5, 75)]
        [InlineData(60, 100, 1, 45)]
        [InlineData(60, 80, 6, 57)]
        [InlineData(95, 200, 10, 100)]
        [InlineData(0, 100, 5, 0)]
        public void FactorsClampIndependentlyInFixedPoint(int b, int s, int d, int expected)
            => Assert.Equal(expected, RideSliderEffects.Intensity(b, s, d));

        // REJECTS a floating-point product or multiplying by the base before truncating the combined
        // factor. 40*.75*.8 is 24, but the two fixed-point truncations make the game return 23.
        [Fact]
        public void IntermediateTruncationIsObservable()
            => Assert.Equal(23, RideSliderEffects.Intensity(40, 75, 4));

        // REJECTS silently adopting the newly read coaster /1 rule or the track's extra base bonus.
        // This deliberately pins the older rides.md §5 common formula pending source review.
        [Fact]
        public void RetainedCommonFormulaIncludesDurationDivisionByFive()
            => Assert.Equal(50, RideSliderEffects.Intensity(90, 50, 1));

        // REJECTS adding a final lower clamp or widening the MIPS low-word multiply/shift into long.
        // These synthetic raw inputs describe arithmetic width, not values from real ride records.
        [Theory]
        [InlineData(-60, 100, 5, -60)]
        [InlineData(268435456, 100, 5, 0)]
        [InlineData(60, 524288, 5, 45)]
        public void IntensityKeepsSignedWordArithmetic(int b, int s, int d, int expected)
            => Assert.Equal(expected, RideSliderEffects.Intensity(b, s, d));

        // REJECTS proportional tour speed, the wrong centre or divisor, missing saturation at either
        // end, and arithmetic shifting a negative delta rather than division toward zero.
        [Theory]
        [InlineData(20, 0, 15)]
        [InlineData(20, 80, 18)]
        [InlineData(20, 99, 20)]
        [InlineData(20, 100, 20)]
        [InlineData(20, 120, 22)]
        [InlineData(20, 200, 25)]
        [InlineData(1, 100, 2)]
        [InlineData(32767, 150, 2)]
        public void TourSpeedIsACappedOffsetAndSignedHalfword(int b, int speed, int expected)
            => Assert.Equal(expected, RideSliderEffects.TourVehicleSpeed(b, speed));

        // REJECTS continuous floating speed or preserving more than the initialization byte.
        [Theory]
        [InlineData(19, 0)]
        [InlineData(20, 1)]
        [InlineData(99, 4)]
        [InlineData(100, 5)]
        [InlineData(5140, 1)]
        [InlineData(-20, 255)]
        public void TrackSpeedDividesByTwentyThenNarrows(int speed, int expected)
            => Assert.Equal(expected, RideSliderEffects.TrackVehicleSpeed(speed));

        // REJECTS ignoring speed, scaling the wrong bound, or applying the maximum after the minimum
        // when those bounds cross. The coefficients here are synthetic world inputs, not defaults.
        [Theory]
        [InlineData(900, 50, 10, 1000, 500)]
        [InlineData(250, 50, 10, 1000, 250)]
        [InlineData(1, 50, 10, 1000, 10)]
        [InlineData(900, 0, 10, 1000, 10)]
        [InlineData(900, 75, 10, 999, 749)]
        [InlineData(900, 100, -100, 2147483647, -1)]
        public void CoasterMinimumWinsAfterScaledMaximum(int v, int s, int min, int max, int expected)
            => Assert.Equal(expected, RideSliderEffects.CoasterVelocity(v, s, min, max));

        // REJECTS confusing this forecast with intensity or actual wear: it starts at 100, uses
        // nine times duration, and the coaster's shift is three bits smaller. Negative raw predictions
        // are not clamped away and the positive deduction caps at 100.
        [Theory]
        [InlineData(4096, 1, false, 99)]
        [InlineData(4096, 1, true, 91)]
        [InlineData(8192, 2, false, 96)]
        [InlineData(8192, 2, true, 64)]
        [InlineData(40960, 20, false, 0)]
        [InlineData(-4096, 1, false, 102)]
        public void ProjectedReliabilityKeepsClassSpecificShift(int wear, int duration, bool coaster, int expected)
            => Assert.Equal(expected, RideSliderEffects.ProjectedReliability(wear, duration, coaster));
    }
}
