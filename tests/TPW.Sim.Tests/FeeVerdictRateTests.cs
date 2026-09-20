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
            // Exhaustive over the divisor range this is 65.9%.
            Assert.InRange(100.0 * refused / n, 65.0, 67.0);
        }
    }
}
