using System;
using TPW.Sim;
using Xunit;
using Xunit.Abstractions;

namespace TPW.Sim.Tests
{
    public class FeeVerdictRateTests
    {
        sealed class Uniform : IRandomSource
        {
            readonly Random _r = new Random(4242);
            public int Next(int n) => _r.Next(n);
        }

        readonly ITestOutputHelper _out;
        public FeeVerdictRateTests(ITestOutputHelper o) => _out = o;

        [Fact]
        public void RefusalRateAtTheMeasuredPark()
        {
            var rng = new Uniform();
            int refused = 0, n = 200000;
            for (int i = 0; i < n; i++)
                if (VisitorEntrance.FeeVerdict(80, Money.FromPounds(40), rng) <= -2) refused++;
            _out.WriteLine($"sum 80 fee 40: refused {refused}/{n} = {100.0 * refused / n:F2}%");
            // ⭐⭐ ZERO, AND IT USED TO BE 65.9%. The early-exit gate (0x80103248) is 40 in the running
            // game, not the 0 the static image holds, so a park this small with the default £40 fee
            // never reaches the bands at all: q tops out at 32, which is under 40, and 40 is not OVER
            // 40. Every guest is admitted. That single constant is the whole "guests refuse far more
            // than the real game" report.
            //
            // ⚠ THE OLD 65.9% WAS ARITHMETICALLY CORRECT AND ANSWERED THE WRONG QUESTION — it was the
            // exhaustive rate over the divisor range FOR A GATE OF ZERO. A right number from a wrong
            // constant is the hardest kind to doubt.
            Assert.Equal(0, refused);
        }
    }
}
