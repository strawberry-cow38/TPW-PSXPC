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

            Assert.Equal(Money.FromPounds(1500), Wages.TrainingCost(0, StaffKind.Researcher));  // 250*6
            Assert.Equal(Money.FromPounds(150), Wages.Monthly(0, StaffKind.Researcher, 30, 30));
        }

        // ⭐ REJECTS PRICING TRAINING OFF THE WAGE TABLE, which this code did. fable's falsifier (wages.md §7),
        // predicted from the code and NOT yet measured: train Gary Liddon, a grade-1 guard, once, and the bank
        // loses 5500 tenths = £550 = 275*2, the NEXT row of the training table. The wage table would charge
        // 55*2 = £110, a fifth of it.
        [Fact]
        public void TrainingAGradeOneGuardCharges550()
            => Assert.Equal(Money.FromPounds(550), Wages.TrainingCost(newLevel: 1, StaffKind.Guard));

        // ⭐ The same falsifier's other half: BEFORE the purchase, that card showed 500 and 110. The printed
        // cost is the current row (250*2), one row cheaper than the charge, and the printed wage is the next
        // level's (55*2). A port that shows what it charges, or charges what it shows, fails one of these.
        [Fact]
        public void TheTrainingCardShowsTheCurrentRowAndTheNextWage()
        {
            Assert.Equal(Money.FromPounds(500), Wages.TrainingCardCost(0, StaffKind.Guard));
            Assert.NotEqual(Wages.TrainingCardCost(0, StaffKind.Guard), Wages.TrainingCost(1, StaffKind.Guard));
            Assert.Equal(Money.FromPounds(110), Wages.TrainingCardWage(0, StaffKind.Guard));
        }

        // The original's own bug, reproduced on purpose: at level 4 the card indexes base[5], one past the
        // five-entry table, and reads the 3 that follows it. 3 * 3 = £9 for a top-grade mechanic.
        [Fact]
        public void AtTheTopLevelTheTrainingCardReadsPastTheTable()
            => Assert.Equal(Money.FromPounds(9), Wages.TrainingCardWage(4, StaffKind.Mechanic));

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

    public class DebtTests
    {
        static readonly Money Red = Money.FromPounds(-1);
        static readonly Money Black = Money.Zero;

        // ⭐ Hand-worked from the READ sequence: messages on months 1, 3 and 5, game over on 6, silence between.
        [Fact]
        public void SixMonthsInTheRedEndTheGame()
        {
            var d = new DebtWatch();
            var got = new DebtNotice[6];
            for (int i = 0; i < 6; i++) got[i] = d.MonthEnd(Red, loansOutstanding: false);
            Assert.Equal(new[] { DebtNotice.InDebt, DebtNotice.None, DebtNotice.ThirdMonth, DebtNotice.None,
                                 DebtNotice.FifthMonth, DebtNotice.Bankrupt }, got);
            Assert.True(d.GameOver);
        }

        // ⭐ REJECTS A CUMULATIVE COUNT. One month at zero or above clears everything: five months in the red,
        // one in the black, then red again starts over at month one, not month six.
        [Fact]
        public void OneSolventMonthResetsTheClock()
        {
            var d = new DebtWatch();
            for (int i = 0; i < 5; i++) d.MonthEnd(Red, false);
            Assert.Equal(DebtNotice.None, d.MonthEnd(Black, false));
            Assert.Equal(DebtNotice.InDebt, d.MonthEnd(Red, false));
            Assert.Equal(1, d.MonthsInDebt);
            Assert.False(d.GameOver);
        }

        [Fact]
        public void ZeroIsNotDebt()
        {
            var d = new DebtWatch();
            Assert.Equal(DebtNotice.None, d.MonthEnd(Money.Zero, false));
            Assert.Equal(0, d.MonthsInDebt);
        }

        // The loan only changes the first message, never the clock.
        [Fact]
        public void ALoanChangesTheFirstMessageOnly()
        {
            var d = new DebtWatch();
            Assert.Equal(DebtNotice.InDebtWithLoans, d.MonthEnd(Red, loansOutstanding: true));
            for (int i = 0; i < 4; i++) d.MonthEnd(Red, true);
            Assert.Equal(DebtNotice.Bankrupt, d.MonthEnd(Red, true));
        }
    }
}
