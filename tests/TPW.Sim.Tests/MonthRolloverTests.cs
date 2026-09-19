using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>Loans, the bank, and the month rollover. Worked from economy.md §3.</summary>
    public class MonthRolloverTests
    {
        static Money P(long pounds) => Money.FromPounds(pounds);

        // ⭐ pay = min(monthlyPayment, remaining), READ at 0x80088EC0. The clamp is the whole point: the
        // last instalment is a part payment, and without it the debt overshoots into a credit.
        // REJECTS an unclamped subtraction, which would leave Remaining at -£20 here.
        [Fact]
        public void TheFinalInstalmentIsClampedToWhatIsLeft()
        {
            var loan = new Loan();
            loan.Grant(P(250), P(100), months: 3);

            Assert.Equal(P(100), loan.PayMonth());
            Assert.Equal(P(150), loan.Remaining);
            Assert.Equal(P(100), loan.PayMonth());
            Assert.Equal(P(50), loan.Remaining);

            Assert.Equal(P(50), loan.PayMonth());       // not £100
            Assert.Equal(Money.Zero, loan.Remaining);   // not -£50
        }

        // ⚠ THE SLOT IS NEVER RELEASED. Step 2 pays every slot whose available flag is 0 and never
        // clears that flag, so a repaid loan keeps occupying one of the four. REJECTS modelling
        // repayment as freeing the slot, which would let a player take a fifth loan.
        [Fact]
        public void ARepaidLoanKeepsItsSlotAndKeepsCountingDown()
        {
            var book = new LoanBook();
            for (int i = 0; i < LoanBook.Slots; i++)
                book[i].Grant(P(100), P(100), months: 1);

            Assert.Null(book.FreeSlot());
            Assert.Equal(P(400), book.PayMonth());          // all four settled in one month
            Assert.Equal(Money.Zero, book.TotalOutstanding);
            Assert.False(book.AnyOutstanding);

            Assert.Null(book.FreeSlot());                    // still no room
            Assert.Equal(Money.Zero, book.PayMonth());       // visited, pays nothing
            Assert.Equal(-1, book[0].MonthsLeft);            // and the counter kept going
        }

        // The charge is loans and wages together, and it is what the result reports. Asserted as the
        // balance DELTA rather than as a pair of numbers, so the assertion is about money that actually
        // moved. (One charge versus two is not observable through Bank as it stands -- it never refuses
        // -- so this does not claim to test that; the ordering note lives on MonthRollover.)
        [Fact]
        public void TheRolloverChargesLoansAndWagesAgainstTheBalance()
        {
            var bank = new Bank(P(1000));
            var book = new LoanBook();
            book[0].Grant(P(500), P(120), months: 5);

            var r = MonthRollover.Run(bank, book, wages: P(230), debt: new DebtWatch());

            Assert.Equal(P(120), r.LoanPayments);
            Assert.Equal(P(230), r.Wages);
            Assert.Equal(P(350), r.TotalCharged);
            Assert.Equal(P(650), bank.Balance);
            Assert.Equal(bank.Balance, r.ClosingBalance);
            Assert.Equal(P(1000).Raw - r.TotalCharged.Raw, bank.Balance.Raw);
        }

        // ⚠ THE BANK GOES NEGATIVE RATHER THAN REFUSING. Everything below depends on this: a port that
        // blocked the charge would never reach the debt clock at all. REJECTS a spend guard.
        [Fact]
        public void TheChargeIsTakenEvenWhenItCannotBeAfforded()
        {
            var bank = new Bank(P(50));
            var r = MonthRollover.Run(bank, new LoanBook(), wages: P(500), debt: new DebtWatch());

            Assert.Equal(P(-450), bank.Balance);
            Assert.True(bank.Balance.Raw < 0);
            Assert.Equal(P(500), r.TotalCharged);
            Assert.True(ParkEconomy.BalanceMayGoNegative);
        }

        // ⭐ Which FIRST-month message appears depends on whether any loan is still outstanding, and
        // nothing else. REJECTS keying it on whether a loan slot is taken: here a slot is taken and
        // fully repaid, so the answer must be the no-loans message.
        [Theory]
        [InlineData(true, DebtNotice.InDebtWithLoans)]
        [InlineData(false, DebtNotice.InDebt)]
        public void TheFirstMonthMessageDependsOnDebtStillOwed(bool owing, DebtNotice expected)
        {
            var bank = new Bank(P(0));
            var book = new LoanBook();
            book[0].Grant(owing ? P(900) : P(100), P(100), months: 9);

            var r = MonthRollover.Run(bank, book, wages: P(10), debt: new DebtWatch());

            Assert.Equal(owing, book.AnyOutstanding);
            Assert.Equal(expected, r.Notice);
        }

        // ✅ SIX MONTHS ENDS THE GAME -- measured on hardware, not inferred (see DebtWatch). Runs the
        // rollover six times with the bank underwater and checks the messages land on 1, 3, 5 and 6 with
        // nothing in between. REJECTS an off-by-one in the escalation and REJECTS a clock that keeps
        // counting past bankruptcy into some seventh state.
        [Fact]
        public void SixConsecutiveMonthsInTheRedEndsTheGame()
        {
            var bank = new Bank(P(-1000));
            var book = new LoanBook();
            var debt = new DebtWatch();
            var seen = new DebtNotice[6];
            for (int m = 0; m < 6; m++)
                seen[m] = MonthRollover.Run(bank, book, Money.Zero, debt).Notice;

            Assert.Equal(DebtNotice.InDebt, seen[0]);
            Assert.Equal(DebtNotice.None, seen[1]);
            Assert.Equal(DebtNotice.ThirdMonth, seen[2]);
            Assert.Equal(DebtNotice.None, seen[3]);
            Assert.Equal(DebtNotice.FifthMonth, seen[4]);
            Assert.Equal(DebtNotice.Bankrupt, seen[5]);
            Assert.True(debt.GameOver);
        }

        // ⚠ ONE GOOD MONTH CLEARS THE WHOLE COUNT -- there is no partial credit and no memory. REJECTS
        // a decaying counter: after five months under and one above, the next bad month is month ONE.
        [Fact]
        public void OneSolventMonthResetsTheDebtClockCompletely()
        {
            var debt = new DebtWatch();
            var book = new LoanBook();
            var bank = new Bank(P(-100));
            for (int m = 0; m < 5; m++) MonthRollover.Run(bank, book, Money.Zero, debt);
            Assert.Equal(5, debt.MonthsInDebt);

            bank.Receive(P(10000));
            Assert.Equal(DebtNotice.None, MonthRollover.Run(bank, book, Money.Zero, debt).Notice);
            Assert.Equal(0, debt.MonthsInDebt);

            bank.Spend(P(20000));
            Assert.Equal(DebtNotice.InDebt, MonthRollover.Run(bank, book, Money.Zero, debt).Notice);
        }

        // ⭐ NO UPKEEP AND NO DAILY DRIP. With no loans and no staff, a month costs exactly nothing --
        // the rollover records the value of everything built but never charges for it. REJECTS the
        // reflex of adding a maintenance cost, which is the most plausible wrong feature here.
        [Fact]
        public void AMonthWithNoLoansAndNoStaffCostsNothing()
        {
            var bank = new Bank(P(777));
            var r = MonthRollover.Run(bank, new LoanBook(), Money.Zero, new DebtWatch());

            Assert.Equal(Money.Zero, r.TotalCharged);
            Assert.Equal(P(777), bank.Balance);
            Assert.Equal(DebtNotice.None, r.Notice);
        }
    }
}
