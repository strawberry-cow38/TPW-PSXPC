using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class MoneyTests
    {
        // ⭐ REJECTS TREATING A STORED VALUE AS POUNDS. tinyclaw measured a HUD reading $48,040 while memory
        // held 480800. Every economy number in the game is out by 10x if this is missed, and still looks like
        // money, which is why it needs a type rather than a convention.
        [Fact]
        public void TheGameStoresTenTimesWhatItDisplays()
        {
            Assert.Equal(500000, Money.FromPounds(50_000).Raw);
            Assert.Equal(48_040, Money.FromRaw(480_800).Pounds);
        }

        [Fact]
        public void ArithmeticStaysInTheStoredUnit()
        {
            var a = Money.FromPounds(100);
            var b = Money.FromPounds(40);
            Assert.Equal(Money.FromPounds(140), a + b);
            Assert.Equal(Money.FromPounds(60), a - b);
            Assert.Equal(Money.FromPounds(300), a * 3);
            Assert.True(b < a);
        }

        [Fact]
        public void BalanceCanGoNegativeBecauseTheOriginalAllowsIt()
        {
            // ⚠ The bank's constructor sets "may go negative", so TrySpend never refuses. A port that blocks
            // the purchase is stricter than the game and diverges the first time a player overspends.
            Assert.True(ParkEconomy.BalanceMayGoNegative);
            Assert.True(Money.FromPounds(10) - Money.FromPounds(50) < Money.Zero);
        }
    }

    public class WageTests
    {
        // ⭐ THE FIXTURE THAT SETTLED A FALSE CONTRADICTION. The hire screen shows "Pay Grade 1" for Gary
        // Liddon at £100/month. That looked like it disagreed with a formula predicting 150 -- until two
        // off-by-ones were found: Pay Grade 1 is LEVEL 0, and a guard's multiplier is 2, not 3.
        [Fact]
        public void AGradeOneGuardCostsAHundredAMonth()
            => Assert.Equal(Money.FromPounds(100), Wages.Monthly(level: 0, StaffKind.Guard, daysWorked: 31, monthLength: 31));

        // ⭐ MEASURED PRO-RATA: hired mid-month, first bill £16, then £100 a month after.
        // 5 days of 31 -> pct = floor(500/31) = 16 -> floor(16 * 50 * 2 / 100) = 16.
        [Fact]
        public void TheFirstMonthIsProRated()
        {
            Assert.Equal(Money.FromPounds(16), Wages.Monthly(0, StaffKind.Guard, daysWorked: 5, monthLength: 31));
            Assert.Equal(Money.FromPounds(100), Wages.Monthly(0, StaffKind.Guard, daysWorked: 31, monthLength: 31));
        }

        // ⚠⚠ REJECTS COLLAPSING THE TWO FLOORS INTO ONE DIVISION — with values that actually
        // discriminate. An earlier version of this test computed its own expectation with the same
        // arithmetic as the implementation, which is vacuous: it would have passed on any formula.
        //
        // A Mechanic (base 50, mult 3) is used because base*mult = 150, so the /100 does not cancel and the
        // two orderings diverge. Worked by hand:
        //     7 of 30 : pct = floor(700/30) = 23 -> floor(23*150/100) = 34   single-floor gives 35
        //    29 of 30 : pct = floor(2900/30) = 96 -> floor(96*150/100) = 144  single-floor gives 145
        // A Guard would hide this entirely, because base*mult = 100 makes both orderings agree.
        [Theory]
        [InlineData(1, 31, 4)]
        [InlineData(7, 30, 34)]     // single-floor: 35
        [InlineData(15, 31, 72)]
        [InlineData(29, 30, 144)]   // single-floor: 145
        public void ProRataFloorsThePercentageBeforeTheWage(int days, int monthLen, int expectedPounds)
            => Assert.Equal(Money.FromPounds(expectedPounds), Wages.Monthly(0, StaffKind.Mechanic, days, monthLen));

        // ⭐⭐ MEASURED ON HARDWARE, and the measurement is one the other formula cannot produce. tinyclaw
        // hired a mechanic (card: Monthly Wage £150) and read the bank: 240 tenths for the first, part month,
        // then +1500 a month. With one division the wage is 150*d/30 = 5d pounds, always a multiple of 5, so £24
        // is out of reach for ANY whole number of days. With two floors, floor(100*5/30) = 16 and
        // floor(16*150/100) = 24. Nothing else fits.
        [Fact]
        public void AMechanicsFirstPartMonthIs24PoundsAsMeasured()
            => Assert.Equal(Money.FromRaw(240), Wages.Monthly(0, StaffKind.Mechanic, daysWorked: 5, monthLength: 30));

        [Fact]
        public void AFullMonthIsNeverProRated()
        {
            // days >= monthLength pins pct at 100 rather than letting it exceed it.
            Assert.Equal(Money.FromPounds(100), Wages.Monthly(0, StaffKind.Guard, daysWorked: 40, monthLength: 31));
        }

        // ⭐ REJECTS THE WRONG KIND ORDER. An earlier report had Entertainer/Mechanic/Guard/Researcher/Handyman.
        // fable established this order three independent ways. Wrong order pays a mechanic a cleaner's wage,
        // which is a plausible-looking number and therefore invisible.
        [Fact]
        public void KindOrderIsMechanicEntertainerCleanerGuardResearcher()
        {
            Assert.Equal(0, (int)StaffKind.Mechanic);
            Assert.Equal(1, (int)StaffKind.Entertainer);
            Assert.Equal(2, (int)StaffKind.Cleaner);
            Assert.Equal(3, (int)StaffKind.Guard);
            Assert.Equal(4, (int)StaffKind.Researcher);
            Assert.Equal(new[] { 3, 1, 1, 2, 3 }, Wages.MultiplierByKind);
        }

        [Fact]
        public void EachKindIsPaidItsOwnMultiple()
        {
            const int days = 30, len = 30;
            Assert.Equal(Money.FromPounds(150), Wages.Monthly(0, StaffKind.Mechanic, days, len));     // 50*3
            Assert.Equal(Money.FromPounds(50), Wages.Monthly(0, StaffKind.Entertainer, days, len));   // 50*1
            Assert.Equal(Money.FromPounds(50), Wages.Monthly(0, StaffKind.Cleaner, days, len));       // 50*1
            Assert.Equal(Money.FromPounds(100), Wages.Monthly(0, StaffKind.Guard, days, len));        // 50*2
            Assert.Equal(Money.FromPounds(150), Wages.Monthly(0, StaffKind.Researcher, days, len));   // 50*3
        }

        [Fact]
        public void LevelRaisesTheBase()
        {
            Assert.Equal(new[] { 50, 55, 65, 80, 100 }, Wages.BaseByLevel);
            Assert.Equal(Money.FromPounds(200), Wages.Monthly(4, StaffKind.Guard, 30, 30));   // 100*2
        }

        // ⭐ REJECTS USING THE TRAINING TABLE FOR WAGES. The two differ only at Researcher -- 3 versus 6 --
        // so three of five kinds agree and the mistake hides behind them.
        [Fact]
        public void TrainingAndWageMultipliersDifferOnlyForResearcher()
        {
            for (int k = 0; k < 5; k++)
                if (k == (int)StaffKind.Researcher)
                    Assert.NotEqual(Wages.MultiplierByKind[k], Wages.TrainingMultiplierByKind[k]);
                else
                    Assert.Equal(Wages.MultiplierByKind[k], Wages.TrainingMultiplierByKind[k]);

            Assert.Equal(Money.FromPounds(300), Wages.TrainingCost(0, StaffKind.Researcher));  // 50*6
            Assert.Equal(Money.FromPounds(150), Wages.Monthly(0, StaffKind.Researcher, 30, 30));
        }

        // ⚠ REJECTS CHARGING FOR A HIRE. An earlier report read the TRAINING card's purchase handler as the
        // hire cost. The game spends £0 to hire; billing for it drains a balance the original never touched.
        [Fact]
        public void HiringIsFree() => Assert.Equal(Money.Zero, ParkEconomy.HireCost);

        [Fact]
        public void AStrikingMemberIsPaidNothing()
            => Assert.Equal(Money.Zero, Wages.Monthly(0, StaffKind.Guard, 31, 31, striking: true));

        // ⭐ THE OFF-BY-ONE THAT MADE A CORRECT FORMULA LOOK WRONG.
        [Fact]
        public void PayGradeOneIsLevelZero() => Assert.Equal(1, Wages.DisplayedPayGrade(0));

        [Fact]
        public void DegenerateMonthsDoNotThrow()
        {
            Assert.Equal(Money.Zero, Wages.Monthly(0, StaffKind.Guard, 5, 0));
            Assert.Equal(Money.Zero, Wages.Monthly(0, StaffKind.Guard, -3, 31));
        }

        [Fact]
        public void TheDefaultEntryFeeIsForty()
            => Assert.Equal(Money.FromPounds(40), ParkEconomy.DefaultEntryFee);
    }
}
