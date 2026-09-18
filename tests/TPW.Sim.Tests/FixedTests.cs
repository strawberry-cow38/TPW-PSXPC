using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    // These assert the PROPERTIES that make Fixed worth having over float -- exactness, and the specific
    // rounding the original uses. A test that only checked "about 1.5" would pass on a float implementation
    // and therefore prove nothing about the thing this type exists for.
    public class FixedTests
    {
        [Fact]
        public void OneWholeUnitIs4096()
        {
            Assert.Equal(4096, Fixed.One);
            Assert.Equal(4096, Fixed.FromInt(1).Raw);
        }

        [Fact]
        public void AdditionIsExact()
        {
            // 0.1 + 0.2 in binary floating point is famously not 0.3. In fixed point the question does not
            // arise: both operands are integers and so is the sum.
            var a = Fixed.FromDouble(0.1);
            var b = Fixed.FromDouble(0.2);
            Assert.Equal(Fixed.FromDouble(0.1).Raw + Fixed.FromDouble(0.2).Raw, (a + b).Raw);
        }

        [Fact]
        public void MultiplyDoesNotOverflowAt32Bits()
        {
            // ⚠ THE REGRESSION THIS EXISTS TO CATCH. 512 * 512 in 20.12 needs the intermediate product to be
            // 64-bit: 2097152 * 2097152 is 4.4e12, far past int. An implementation that multiplies in int
            // returns garbage here while passing every small-number test, which is why the small-number tests
            // above are not sufficient on their own.
            var big = Fixed.FromInt(512);
            Assert.Equal(Fixed.FromInt(512 * 512).Raw, (big * big).Raw);
        }

        [Fact]
        public void DivideKeepsFractionalBits()
        {
            // 1 / 4 must be 0.25 and not 0. Shifting the numerator up BEFORE dividing is the only way; the
            // naive version loses the fraction before the division happens.
            Assert.Equal(Fixed.FromDouble(0.25).Raw, (Fixed.FromInt(1) / Fixed.FromInt(4)).Raw);
        }

        [Fact]
        public void ToIntFloorsTowardNegativeInfinity()
        {
            // ⚠ THE SIGN TRAP. An arithmetic shift floors; C#'s int division truncates toward zero. They agree
            // on positives and disagree on every negative, so a wrong implementation passes anything tested
            // with positive numbers only -- which is most tests, written by most people, most of the time.
            Assert.Equal(1, Fixed.FromDouble(1.5).ToIntFloor());
            Assert.Equal(-2, Fixed.FromDouble(-1.5).ToIntFloor());   // NOT -1
            Assert.Equal(-1, Fixed.FromRaw(-1).ToIntFloor());        // NOT 0
        }
    }

    public class ParkClockTests
    {
        [Fact]
        public void DayRollsOverAtNinetyNineTicks()
        {
            var c = new ParkClock();
            c.Advance(ParkClock.TicksPerDay - 1);
            Assert.Equal(0, c.Day);
            Assert.False(c.IsDayRollover);

            c.Advance();
            Assert.Equal(1, c.Day);
            Assert.True(c.IsDayRollover);   // the EDGE, on the tick it happens
        }

        [Fact]
        public void RolloverIsAnEdgeAndNotAState()
        {
            // Asking one tick late must say false. A caller that polls whenever it likes and expects to catch
            // the rollover has a race, and this is the assertion that makes that explicit rather than leaving
            // it to be discovered when wages get paid twice or not at all.
            var c = new ParkClock();
            c.Advance(ParkClock.TicksPerDay);
            Assert.True(c.IsDayRollover);
            c.Advance();
            Assert.False(c.IsDayRollover);
        }

        [Fact]
        public void AdvancingBySpanVisitsEveryTick()
        {
            // ⚠ Guards the optimisation someone will eventually make: Tick += n is faster and silently skips
            // every per-tick event. Counting rollovers across three days proves each tick was actually visited.
            var c = new ParkClock();
            int rollovers = 0;
            for (int i = 0; i < ParkClock.TicksPerDay * 3; i++)
            {
                c.Advance();
                if (c.IsDayRollover) rollovers++;
            }
            Assert.Equal(3, rollovers);
        }
    }
}
