using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>Wear, breakdowns and the condemned mechanic (rides.md §6).</summary>
    public class RideWearTests
    {
        // MUTATION RECORD (2026-09-20): 18 breakages, 17 caught. The survivor is equivalent, not a gap:
        // rewriting `Reliability <= 0x9FFF` as `Reliability < (10 << 12)` is the same test, because
        // 0x9FFF is exactly 0xA000 - 1. Two others survived a first pass and were real - the speed
        // fold, which is invisible at exactly 100 and only shows above it, and the once-only condemned
        // message, which needs a repair to reach a second post. Both now have tests above.

        sealed class World : IRideWearWorld
        {
            public int SpeedSlider { get; set; } = 50;
            public int Riders { get; set; }
            public int MaxSeats { get; set; } = 4;
            public int WearMultiplier { get; set; } = 5;
            public bool NoWear { get; set; }
            public List<int> Messages { get; } = new();
            public void PostMessage(int id) => Messages.Add(id);
        }

        // ⭐⭐ THE THREE WORKED RATES IN §6.1, REPRODUCED EXACTLY. These are the reason the ambiguous
        // heavy-load term is written the way it is: the report gives an expression that can be read
        // several ways and three rates it produces, and only one reading gives all three. REJECTS every
        // other placement of the fixed point, each of which passes no row or one row.
        [Theory]
        [InlineData(50, 4, 4, 5, 15720)]    // 3.84: default speed, full
        [InlineData(50, 0, 4, 5, 5120)]     // 1.25: default speed, empty
        [InlineData(100, 4, 4, 5, 20840)]   // 5.09: full speed, full
        public void TheWearRateMatchesTheWorkedNumbers(int speed, int riders, int seats, int mult, int k)
        {
            Assert.Equal(k, RideWear.RateFor(speed, riders, seats, mult));
        }

        // The same three, stated as the report states them, to a hundredth.
        [Theory]
        [InlineData(50, 4, 3.84)]
        [InlineData(50, 0, 1.25)]
        [InlineData(100, 4, 5.09)]
        public void AndInTheUnitsTheReportQuotes(int speed, int riders, double k)
        {
            Assert.Equal(k, System.Math.Round(RideWear.RateFor(speed, riders, 4, 5) / 4096.0, 2));
        }

        // ⚠ THE FULL-LOAD BONUS IS +0.035, and it only applies from 80% up. REJECTS a linear load term.
        [Fact]
        public void LoadOnlyBitesHardAboveEightyPercent()
        {
            // Below 80% the load term is exactly the fraction: the average of speed and load, x5.
            Assert.Equal((2048 + 2048) / 2 * 5, RideWear.RateFor(50, 2, 4, 5));    // 50% load
            Assert.Equal((2048 + 3072) / 2 * 5, RideWear.RateFor(50, 3, 4, 5));    // 75% load

            // At 100% it is 1.0 PLUS 144/4096 = 0.035, which is the report's stated bonus.
            Assert.Equal((2048 + 4096 + 144) / 2 * 5, RideWear.RateFor(50, 4, 4, 5));
            Assert.Equal(144, 4096 - 4096 + ((4096 - RideWear.HeavyLoadFrom) / 64)
                                          * ((4096 - RideWear.HeavyLoadFrom) / 64));
        }

        // ⚠ THE SPEED SLIDER STOPS BEING LINEAR AT 100: it is averaged with 1.0, so the top of the
        // range is compressed. REJECTS reading the slider as a plain fraction throughout, which would
        // make 100 exactly twice 50 - it is not.
        [Fact]
        public void TheTopOfTheSpeedRangeIsCompressed()
        {
            int at50 = RideWear.RateFor(50, 0, 4, 5);
            int at100 = RideWear.RateFor(100, 0, 4, 5);
            Assert.Equal((2048 + 0) / 2 * 5, at50);             // slider 50 -> 0.5
            Assert.Equal((4096 + 0) / 2 * 5, at100);            // slider 100 -> 1.0, NOT 2.0

            // ⚠ AT EXACTLY 100 THE FOLD CHANGES NOTHING - (1.0 + 1.0)/2 is 1.0 - so a test that only
            // checks 100 cannot see whether the fold is there at all. It has to be checked ABOVE 100,
            // where folded and unfolded differ: 200 gives 1.5, not 2.0.
            Assert.Equal((99 * 4096 / 100 + 0) / 2 * 5, RideWear.RateFor(99, 0, 4, 5));
            Assert.Equal((6144 + 0) / 2 * 5, RideWear.RateFor(200, 0, 4, 5));
            Assert.NotEqual((8192 + 0) / 2 * 5, RideWear.RateFor(200, 0, 4, 5));
        }

        // An upgrade more than halves the wear: the multiplier goes 5 -> 3 -> 2.
        [Theory]
        [InlineData(5, 15720)]
        [InlineData(3, 9432)]
        [InlineData(2, 6288)]
        public void AnUpgradeIsMostlyAWearReduction(int mult, int k)
        {
            Assert.Equal(k, RideWear.RateFor(50, 4, 4, mult));
        }

        // ⚠ WEAR IS EVERY FOURTH TICK, NOT EVERY TICK. REJECTS applying it each tick, which wears a
        // ride out four times too fast and is invisible in any test that only runs one tick.
        [Fact]
        public void WearIsAppliedOnEveryFourthTickOnly()
        {
            var w = new World { Riders = 4 };
            var r = new RideWear { Lifetime = 45 };
            int start = r.Reliability;

            for (int i = 0; i < 3; i++) r.Tick(AttractionStatus.Running, w);
            Assert.Equal(start, r.Reliability);                 // nothing yet

            r.Tick(AttractionStatus.Running, w);
            Assert.Equal(start - (15720 >> 5), r.Reliability);   // 491 raw = 0.120 points
        }

        // Only a RUNNING ride wears. Closing it is the player's lever and it must actually work.
        [Theory]
        [InlineData(AttractionStatus.ClosedByPlayer)]
        [InlineData(AttractionStatus.Loading)]
        [InlineData(AttractionStatus.Unloading)]
        public void ARideThatIsNotRunningDoesNotWear(AttractionStatus status)
        {
            var w = new World { Riders = 4 };
            var r = new RideWear { Lifetime = 45 };
            for (int i = 0; i < 40; i++) r.Tick(status, w);
            Assert.Equal(RideWear.FullReliability, r.Reliability);
        }

        [Fact]
        public void TheNoWearModeStopsItCompletely()
        {
            var w = new World { Riders = 4, NoWear = true };
            var r = new RideWear { Lifetime = 45 };
            for (int i = 0; i < 40; i++) r.Tick(AttractionStatus.Running, w);
            Assert.Equal(RideWear.FullReliability, r.Reliability);
            Assert.Equal(45, r.Lifetime);
        }

        // ⚠ THE THRESHOLD IS 0x9FFF, a hair UNDER ten points, and the test is <=. REJECTS "< 10
        // points", which differs on exactly the value the game uses.
        [Theory]
        [InlineData(0x9FFF, true)]
        [InlineData(0xA000, false)]
        public void TheBreakdownThresholdIsExact(int reliability, bool breaks)
        {
            var w = new World();
            var r = new RideWear { Lifetime = 45, Reliability = reliability };
            var next = r.Tick(AttractionStatus.Running, w);
            Assert.Equal(breaks ? AttractionStatus.AboutToBreakDown : AttractionStatus.Running, next);
        }

        // ⚠ A COASTER NEVER WARNS. Same test, straight to broken, and no message. REJECTS one
        // breakdown path for every ride class.
        [Fact]
        public void TrackRidesAndCoastersSkipTheWarning()
        {
            var flat = new World();
            var rf = new RideWear { Lifetime = 45, Reliability = 0x9000 };
            Assert.Equal(AttractionStatus.AboutToBreakDown, rf.Tick(AttractionStatus.Running, flat));
            Assert.Equal(new[] { AttractionLifecycle.MessageAboutToBreak }, flat.Messages.ToArray());

            var coaster = new World();
            var rc = new RideWear { Lifetime = 45, Reliability = 0x9000 };
            Assert.Equal(AttractionStatus.BrokenDown,
                rc.Tick(AttractionStatus.Running, coaster, isTrackOrCoaster: true));
            Assert.Empty(coaster.Messages);
        }

        // ⭐⭐ ONE LIFETIME PER FIFTEEN POINTS, AND A REPAIR DOES NOT GIVE IT BACK. This is the whole
        // condemned mechanic. REJECTS restoring the lifetime on repair, which would make rides
        // immortal and the message unreachable.
        [Fact]
        public void AFullCycleCostsSixLifetimeAndRepairingGivesNoneBack()
        {
            var w = new World { Riders = 4 };
            var r = new RideWear { Lifetime = 45 };

            while (r.Reliability > RideWear.BreakdownThreshold) r.Tick(AttractionStatus.Running, w);
            Assert.Equal(39, r.Lifetime);                       // 100 -> under 10 crosses six bands

            r.Repaired();
            Assert.Equal(RideWear.FullReliability, r.Reliability);
            Assert.Equal(39, r.Lifetime);                       // and the life is still spent
        }

        // ⭐ A CRAZY APE SURVIVES SEVEN BREAKDOWNS. rides.md says so from the lifetime of 45; this runs
        // it. REJECTS any lifetime arithmetic that does not produce the report's own count.
        [Fact]
        public void ACrazyApeIsCondemnedAfterSevenCycles()
        {
            var w = new World { Riders = 4 };
            var r = new RideWear { Lifetime = 45 };
            int cycles = 0;

            while (!r.Condemned && cycles < 50)
            {
                while (r.Reliability > RideWear.BreakdownThreshold && !r.Condemned)
                    r.Tick(AttractionStatus.Running, w);
                cycles++;
                if (!r.Condemned) r.Repaired();
            }

            // ⚠ SEVEN REPAIRS, CONDEMNED DURING THE EIGHTH. 45 lifetime at six a cycle is 7.5, so the
            // ride survives seven full cycles - which is what rides.md says - and dies part way through
            // the next one. "Survives 7" and "dies on the 8th" are the same fact; a test that asserts
            // one without the other reads as off by one.
            Assert.True(r.Condemned);
            Assert.Equal(8, cycles);
            Assert.Equal(3, 45 - 7 * 6);                        // three lifetime left entering the 8th
            Assert.Equal(new[] { RideWear.MessageCondemned }, w.Messages.ToArray());
        }

        // ⚠ AND THEN NO MECHANIC WILL TOUCH IT. A condemned ride is broken AND unclaimable, which is
        // why demolishing is the only way out. REJECTS claiming on status alone.
        [Fact]
        public void NoMechanicClaimsACondemnedRide()
        {
            var alive = new RideWear { Lifetime = 3 };
            Assert.True(alive.MechanicMayClaim(AttractionStatus.BrokenDown));
            Assert.True(alive.MechanicMayClaim(AttractionStatus.AboutToBreakDown));
            Assert.False(alive.MechanicMayClaim(AttractionStatus.Running));

            var dead = new RideWear { Lifetime = 0 };
            Assert.False(dead.MechanicMayClaim(AttractionStatus.BrokenDown));
        }

        // ⚠ THE MESSAGE IS POSTED ONCE, AND PROVING THAT NEEDS A REPAIR. Once a ride breaks it stops
        // wearing, so simply ticking a condemned ride forever cannot make the message repeat whether
        // the guard is there or not - the test passes for the wrong reason. Repairing it restarts the
        // wear with the lifetime already at zero, which is the only way to reach the second post.
        [Fact]
        public void TheCondemnedMessageIsPostedOnceEvenIfTheRideIsRepairedAndWornOutAgain()
        {
            var w = new World { Riders = 4 };
            var r = new RideWear { Lifetime = 1 };

            for (int cycle = 0; cycle < 4; cycle++)
            {
                var status = AttractionStatus.Running;
                for (int i = 0; i < 4000 && status == AttractionStatus.Running; i++)
                    status = r.Tick(status, w);
                Assert.Equal(AttractionStatus.AboutToBreakDown, status);
                r.Repaired();                               // a mechanic would refuse, but nothing stops a host
            }

            Assert.True(r.Condemned);
            Assert.Equal(0, r.Lifetime);
            Assert.Single(w.Messages, m => m == RideWear.MessageCondemned);
        }

        // ⚠ RELIABILITY ZERO IS UNREACHABLE, and rides.md §6.2 says so independently ("the wear step
        // is < 0.2 and stops in 4"). The breakdown test fires FIRST and a ride that is not running does
        // not wear, so the floor in the wear step is defensive code. Pinned as unreachable rather than
        // deleted, so nobody removes the floor and nobody writes a test that pretends to exercise it.
        [Fact]
        public void ARunningRideCanNeverWearAllTheWayToZero()
        {
            var w = new World { Riders = 4, SpeedSlider = 100, WearMultiplier = 5 };
            var r = new RideWear { Lifetime = 999 };

            var status = AttractionStatus.Running;
            for (int i = 0; i < 4000 && status == AttractionStatus.Running; i++)
                status = r.Tick(status, w);

            Assert.Equal(AttractionStatus.AboutToBreakDown, status);
            Assert.True(r.Reliability > 0);
            Assert.True(r.Reliability <= RideWear.BreakdownThreshold);
        }

        [Fact]
        public void TheConstantsAreTheOriginals()
        {
            Assert.Equal(4, RideWear.WearEveryTicks);
            Assert.Equal(0x64000, RideWear.FullReliability);
            Assert.Equal(0x9FFF, RideWear.BreakdownThreshold);
            Assert.Equal(15, RideWear.LifetimeBandPoints);
            Assert.Equal(0x99, RideWear.MessageCondemned);
        }
    }
}
