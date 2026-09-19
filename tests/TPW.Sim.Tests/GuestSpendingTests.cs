using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>The purchase and entry decisions. Worked from economy.md §5 and behaviour.md §2.5.</summary>
    public class GuestSpendingTests
    {
        static Money P(long pounds) => Money.FromPounds(pounds);

        // ⭐ HAPPINESS SCALES WILLINGNESS TO PAY, and the span is exactly 2x across its range: the factor
        // is (happiness + 100)/100, so 0 gives 1x and 100 gives 2x. REJECTS happiness/100, which would
        // make a miserable guest value everything at nothing and never buy anything at all.
        [Theory]
        [InlineData(0, 100)]      // base 100, N 100%, happiness 0   -> 100
        [InlineData(50, 150)]
        [InlineData(100, 200)]
        public void HappinessMultipliesWhatAnItemIsWorth(int happiness, long expectedPounds)
        {
            Assert.Equal(P(expectedPounds), GuestSpending.Want(P(100), needFactor: 100, happiness: happiness));
        }

        // The need factor is a percentage on top: N = 200 doubles it again.
        [Fact]
        public void TheNeedFactorIsAPercentage()
        {
            Assert.Equal(P(100), GuestSpending.Want(P(100), 100, 0));
            Assert.Equal(P(200), GuestSpending.Want(P(100), 200, 0));
            Assert.Equal(P(50), GuestSpending.Want(P(100), 50, 0));
            Assert.Equal(P(400), GuestSpending.Want(P(100), 200, 100));   // both multipliers at once
        }

        // ⚠ STRICTLY GREATER ON WANT. An item worth exactly its price is not bought. REJECTS >=, which
        // is the natural thing to write and makes a guest buy at its exact indifference point.
        [Theory]
        [InlineData(101, true)]
        [InlineData(100, false)]   // worth exactly the price
        [InlineData(99, false)]
        public void AnItemMustBeWorthMoreThanItCosts(long wantPounds, bool buys)
        {
            Assert.Equal(buys, GuestSpending.WouldBuy(P(wantPounds), P(100), money: P(9999)));
        }

        // ⚠ AND THE GATE IS THE OTHER WAY ROUND. The shop test is money >= price; the entrance is
        // money > fee, so a guest holding exactly the fee buys a drink but cannot get in. That reads
        // like an inconsistency to tidy up, and it is what the original does.
        [Fact]
        public void TheShopTakesExactChangeAndTheGateDoesNot()
        {
            Assert.True(GuestSpending.WouldBuy(want: P(500), price: P(40), money: P(40)));
            Assert.False(GuestSpending.WouldPayEntry(money: P(40), fee: P(40), parkOpinion: 100));
            Assert.True(GuestSpending.WouldPayEntry(money: P(41), fee: P(40), parkOpinion: 100));
        }

        // Affordability is checked independently of desire: wanting it is not enough.
        [Fact]
        public void AGuestWhoCannotAffordItDoesNotBuyHoweverMuchItWantsIt()
        {
            Assert.False(GuestSpending.WouldBuy(want: P(10000), price: P(100), money: P(99)));
            Assert.True(GuestSpending.WouldBuy(want: P(10000), price: P(100), money: P(100)));
        }

        // The park opinion gate lets -1 through and stops -2. REJECTS a >= 0 test, which would turn a
        // mildly unimpressed guest away at the door.
        [Theory]
        [InlineData(5, true)]
        [InlineData(0, true)]
        [InlineData(-1, true)]
        [InlineData(-2, false)]
        public void EntryAllowsASlightlyNegativeOpinion(int opinion, bool pays)
        {
            Assert.Equal(pays, GuestSpending.WouldPayEntry(P(500), P(40), opinion));
        }

        // Negative wins over "below 2", which is the ordering the overlapping clauses force.
        [Theory]
        [InlineData(50, 100, ValueVerdict.RippedOff)]    // worth half what it cost
        [InlineData(100, 100, ValueVerdict.Fair)]        // verdict 0
        [InlineData(103, 100, ValueVerdict.Fair)]        // verdict 1.5 -> under 2
        [InlineData(110, 100, ValueVerdict.Bargain)]     // verdict 5
        public void TheValueVerdictIsHalfTheGapAndNegativeWins(long want, long price, ValueVerdict expected)
        {
            Assert.Equal(expected, GuestSpending.Verdict(P(want), P(price)));
        }

        // ⭐ THE WHOLE VISIT IS FUNDED BY WHAT THEY WALKED IN WITH. £200..£499 at the door, gone below
        // £10, and nothing anywhere adds to it. This is the constraint that makes throughput matter more
        // than price, so it is worth a test rather than a comment.
        [Fact]
        public void AGuestArrivesWithBetweenTwoHundredAndFourNinetyNineAndIsNeverToppedUp()
        {
            Assert.Equal(P(200), GuestSpending.StartingMoney(0));
            Assert.Equal(P(499), GuestSpending.StartingMoney(299));     // rand(300) tops out at 299
            Assert.Equal(P(10), GuestSpending.LeaveBelow);
            Assert.True(GuestSpending.GuestsHaveNoIncome);

            // The richest possible guest, against the default gate, can make this many £40 buys before
            // the leave threshold bites -- and no amount of park design changes it.
            Money left = GuestSpending.StartingMoney(299) - ParkEconomy.DefaultEntryFee;
            int buys = 0;
            while (left - P(40) >= GuestSpending.LeaveBelow) { left -= P(40); buys++; }
            Assert.Equal(11, buys);
        }

        [Theory]
        [InlineData(0, 30)]
        [InlineData(24, 54)]      // rand(25) tops out at 24
        public void EveryPurchaseDropsLitter(int roll, int expected)
        {
            Assert.Equal(expected, GuestSpending.LitterDropped(roll));
        }
    }
}
