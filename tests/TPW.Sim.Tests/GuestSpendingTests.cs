using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>The purchase arithmetic and the entry decision. Worked from behaviour.md §2.5, economy.md
    /// §4.2/§4.3/§5, and the re-read of 0x8008E5EC / 0x8008EE78 / 0x800B6CBC for the port.</summary>
    public class GuestSpendingTests
    {
        static Money P(long pounds) => Money.FromPounds(pounds);

        /// <summary>The Fries record: unit cost 40, kind 7, need A 20, need B 0, happiness 5, nausea 10.</summary>
        static readonly ShopProduct Fries = new(40, 7, 20, 0, 5, 10);

        // ⭐ THE UNIT COST IS SCALED BY THE TWO SLIDERS: rec × (75 + q/4 − s/4) / 100. Both sliders at
        // zero give three quarters of the record's figure, not the figure. REJECTS economy.md §0 item
        // 3's wording taken literally (the record's unit cost as-is), REJECTS q/4 rounded up.
        [Theory]
        [InlineData(40, 0, 0, 30)]
        [InlineData(100, 0, 0, 75)]       // pins the 75 itself: 40 × 75 and 40 × 76 both truncate to 30
        [InlineData(40, 100, 0, 40)]      // 75 + 25 = 100%
        [InlineData(40, 0, 100, 20)]      // 75 − 25 = 50%
        [InlineData(40, 3, 3, 30)]        // 3/4 truncates to 0 on both sides
        [InlineData(40, 7, 0, 30)]        // (75 + 1) × 40 = 3040 / 100 = 30
        public void TheUnitCostIsThreeQuartersPlusQualityMinusTheSecondSlider(int rec, int q, int s, int expected)
        {
            Assert.Equal(expected, GuestSpending.UnitCost(rec, q, s));
        }

        // ⭐ THE WANT'S BASE IS THE UNIT COST TIMES 1.25, DONE BY THE CALLER (0x8008E680..0x8008E6B8).
        // REJECTS economy.md §5's `want = unitCost × N × …`, which lost the factor when §0 corrected
        // "cost-derived number ×1.25" to "it is the unit cost".
        [Theory]
        [InlineData(40, 50)]
        [InlineData(30, 37)]      // 3750 / 100 truncates
        [InlineData(0, 0)]
        public void TheWantBaseIsFiveQuartersOfTheUnitCost(int unitCost, int expected)
        {
            Assert.Equal(expected, GuestSpending.WantBase(unitCost));
        }

        // ⭐ N, TERM BY TERM. Need B against the record's +0x33, need A against +0x32, nausea against
        // +0x36 (subtracted), misery against +0x34 (added). REJECTS need A against +0x33 (the
        // findings' unresolved "slot55/slot56" the other way round), REJECTS nausea added.
        [Fact]
        public void TheNeedFactorWeightsEachStatByItsOwnRecordByte()
        {
            var p = new ShopProduct(unitCost: 40, kind: 0, needAValue: 20, needBValue: 40, happinessValue: 5, nauseaValue: 10);
            Assert.Equal(100, GuestSpending.NeedFactor(0, 0, 0, 100, p));
            Assert.Equal(110, GuestSpending.NeedFactor(50, 0, 0, 100, p));      // 50 × 20 / 100
            Assert.Equal(140, GuestSpending.NeedFactor(0, 100, 0, 100, p));     // 100 × 40 / 100
            Assert.Equal(95, GuestSpending.NeedFactor(0, 0, 50, 100, p));       // − 50 × 10 / 100
            Assert.Equal(105, GuestSpending.NeedFactor(0, 0, 0, 0, p));         // (100 − 0) × 5 / 100
            Assert.Equal(150, GuestSpending.NeedFactor(50, 100, 50, 0, p));     // all four at once
        }

        // Each term truncates on its own before the sum (four separate mfhi/sra sequences). REJECTS
        // summing the products and dividing once: 39 × 20 + 33 × 40 = 2100 / 100 = 21, not 7 + 13 = 20.
        [Fact]
        public void EachTermOfTheNeedFactorTruncatesSeparately()
        {
            var p = new ShopProduct(40, 0, 20, 40, 0, 0);
            Assert.Equal(120, GuestSpending.NeedFactor(39, 33, 0, 100, p));
        }

        // ⭐ HAPPINESS SCALES WILLINGNESS TO PAY, and the span is exactly 2x across its range: the factor
        // is (happiness + 100)/100, so 0 gives 1x and 100 gives 2x. REJECTS happiness/100, which would
        // make a miserable guest value everything at nothing and never buy anything at all.
        [Theory]
        [InlineData(0, 100)]      // base 100, N 100%, happiness 0   -> 100
        [InlineData(50, 150)]
        [InlineData(100, 200)]
        public void HappinessMultipliesWhatAnItemIsWorth(int happiness, int expected)
        {
            Assert.Equal(expected, GuestSpending.Want(100, needFactor: 100, happiness: happiness));
        }

        // The need factor is a percentage on top: N = 200 doubles it again.
        [Fact]
        public void TheNeedFactorIsAPercentage()
        {
            Assert.Equal(100, GuestSpending.Want(100, 100, 0));
            Assert.Equal(200, GuestSpending.Want(100, 200, 0));
            Assert.Equal(50, GuestSpending.Want(100, 50, 0));
            Assert.Equal(400, GuestSpending.Want(100, 200, 100));   // both multipliers at once
        }

        // ⭐ THE TRUNCATION SITS BETWEEN THE TWO MULTIPLIES (0x8008E7C4..0x8008E7E4). base 50 × N 101 =
        // 5050 → 50, × 199 → 9950 → 99. REJECTS both divisions last, which gives 50 × 101 × 199 =
        // 1004950 / 10000 = 100 -- the reading the previous version of Want took.
        [Fact]
        public void BaseTimesNIsTruncatedBeforeTheHappinessScale()
        {
            Assert.Equal(99, GuestSpending.Want(50, 101, 99));
        }

        // ⚠ STRICTLY GREATER ON WANT. An item worth exactly its price is not bought. REJECTS >=, which
        // is the natural thing to write and makes a guest buy at its exact indifference point.
        [Theory]
        [InlineData(101, true)]
        [InlineData(100, false)]   // worth exactly the price
        [InlineData(99, false)]
        public void AnItemMustBeWorthMoreThanItCosts(int want, bool buys)
        {
            Assert.Equal(buys, GuestSpending.WouldBuy(want, 100, money: P(9999)));
        }

        // ⚠ AND THE GATE IS THE OTHER WAY ROUND. The shop test is money >= price; the entrance is
        // money > fee, so a guest holding exactly the fee buys a drink but cannot get in. That reads
        // like an inconsistency to tidy up, and it is what the original does.
        [Fact]
        public void TheShopTakesExactChangeAndTheGateDoesNot()
        {
            Assert.True(GuestSpending.WouldBuy(want: 500, price: 40, money: P(40)));
            Assert.False(GuestSpending.WouldPayEntry(money: P(40), fee: P(40), parkOpinion: 100));
            Assert.True(GuestSpending.WouldPayEntry(money: P(41), fee: P(40), parkOpinion: 100));
        }

        // Affordability is checked independently of desire: wanting it is not enough. And the price is
        // in POUNDS against a Money balance, so £99 of raw 990 is not enough for a £100 item.
        [Fact]
        public void AGuestWhoCannotAffordItDoesNotBuyHoweverMuchItWantsIt()
        {
            Assert.False(GuestSpending.WouldBuy(want: 10000, price: 100, money: P(99)));
            Assert.False(GuestSpending.WouldBuy(want: 10000, price: 100, money: Money.FromRaw(999)));
            Assert.True(GuestSpending.WouldBuy(want: 10000, price: 100, money: P(100)));
        }

        // ⭐ A SIDESHOW IS PLAYED AT EXACTLY ITS WORTH (`slt want, price` skips only on want < price).
        // REJECTS reusing the shop's strict test.
        [Theory]
        [InlineData(11, true)]
        [InlineData(10, true)]
        [InlineData(9, false)]
        public void ASideShowIsPlayedWhenWorthAtLeastItsPrice(int want, bool plays)
        {
            Assert.Equal(plays, GuestSpending.WouldPlay(want, 10, P(9999)));
            Assert.False(GuestSpending.WouldPlay(want, 10, P(9)));
        }

        // ⭐ THE SIDESHOW WANT: expected prize (chance × prize / 100), doubled by the global at
        // 0x80103240 (100 + 100) / 100, then the happiness scale. The Arcade -- 30% of £25 -- is worth
        // 7 → 14 → 21 at happiness 50. REJECTS leaving out the global, which halves it.
        [Theory]
        [InlineData(30, 25, 50, 21)]
        [InlineData(30, 25, 0, 14)]
        [InlineData(30, 25, 100, 28)]
        [InlineData(100, 25, 0, 50)]
        public void TheSideShowWantIsTheDoubledExpectedPrizeScaledByHappiness(int chance, int prize, int happiness, int expected)
        {
            Assert.Equal(expected, GuestSpending.SideShowWant(chance, prize, happiness));
        }

        // ⭐ A ZERO CHANCE IS VALUED AT 100, NOT 0 (0x8008EEC8 → 0x8008EF0C). The Fortune Teller has
        // chance 0 and prize 0 and is worth 200..400 against its £10 price, so it is always played.
        // REJECTS the arithmetic's own answer of 0, which would make it never played.
        [Theory]
        [InlineData(0, 200)]
        [InlineData(50, 300)]
        [InlineData(100, 400)]
        public void AGameThatCannotBeWonIsWorthTwoHundredAtLeast(int happiness, int expected)
        {
            Assert.Equal(expected, GuestSpending.SideShowWant(0, 0, happiness));
            Assert.Equal(expected, GuestSpending.SideShowWant(0, 250, happiness));   // the prize is irrelevant
        }

        // The verdict is half the gap, toward zero.
        [Theory]
        [InlineData(50, 100, ValueVerdict.RippedOff)]    // −25
        [InlineData(99, 100, ValueVerdict.Fair)]         // −1 / 2 = 0 toward zero, NOT −1
        [InlineData(100, 100, ValueVerdict.Fair)]        // 0
        [InlineData(103, 100, ValueVerdict.Fair)]        // 1
        [InlineData(104, 100, ValueVerdict.Bargain)]     // 2
        [InlineData(110, 100, ValueVerdict.Bargain)]     // 5
        public void TheValueVerdictIsHalfTheGapTowardZero(int want, int price, ValueVerdict expected)
        {
            Assert.Equal(expected, GuestSpending.Verdict(want, price));
        }

        // ⚠ THE BUBBLES GO ON THE ENDS. 0x38 for a verdict of 2 or more, 0x36 below zero, nothing for 0
        // and 1 (0x8008EDD4..0x8008EE3C). REJECTS the findings' "0x38 if verdict < 2", which the
        // previous version of ValueVerdict had encoded and which puts the good-value bubble on a fair
        // price and none on a bargain.
        [Fact]
        public void TheGoodValueBubbleIsForABargainNotAFairPrice()
        {
            Assert.Equal(0x38, GuestSpending.VerdictBubble(ValueVerdict.Bargain));
            Assert.Equal(0, GuestSpending.VerdictBubble(ValueVerdict.Fair));
            Assert.Equal(0x36, GuestSpending.VerdictBubble(ValueVerdict.RippedOff));
        }

        // The satisfaction rating: clamp at zero FIRST, then (x − 50) / 4 toward zero. A guest that got
        // sadder rates the stall as one unmoved. REJECTS clamping after the offset (which would give
        // −20 → −17), REJECTS floor division (−50 / 4 → −13).
        [Theory]
        [InlineData(-20, -12, 0)]
        [InlineData(0, -12, 0)]
        [InlineData(49, 0, 49)]
        [InlineData(50, 0, 50)]
        [InlineData(55, 1, 55)]
        [InlineData(100, 12, 100)]
        public void TheSatisfactionRatingClampsThenOffsetsThenQuarters(int fiveTimesDelta, int rating, int amount)
        {
            Assert.Equal(rating, GuestSpending.SatisfactionRating(fiveTimesDelta));
            Assert.Equal(amount, GuestSpending.SatisfactionAmount(fiveTimesDelta));
        }

        // ⭐ FOOD PAYS HAPPINESS BY (quality − second/15); THE REST BY QUALITY ALONE. Fries at value 5,
        // quality 100, second 60: 5 × (100 − 4) / 100 = 4; a gift at the same numbers: 5 × 100 / 100 = 5.
        // REJECTS one formula for all kinds.
        [Theory]
        [InlineData(5, 100, 60, true, 4)]
        [InlineData(5, 100, 60, false, 5)]
        [InlineData(15, 100, 0, true, 15)]
        [InlineData(5, 0, 100, true, 0)]     // 5 × (0 − 6) = −30 / 100 → 0 toward zero
        public void FoodHappinessIsDockedByTheSecondSlider(int value, int quality, int second, bool food, int expected)
        {
            Assert.Equal(expected, GuestSpending.HappinessGain(value, quality, second, food));
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

        // ⭐ THE WHOLE VISIT IS FUNDED BY WHAT THEY WALKED IN WITH. £200..£499 at the door, gone below
        // £10, and only a sideshow prize ever adds to it. This is the constraint that makes throughput
        // matter more than price, so it is worth a test rather than a comment.
        [Fact]
        public void AGuestArrivesWithBetweenTwoHundredAndFourNinetyNine()
        {
            Assert.Equal(P(200), GuestSpending.StartingMoney(0));
            Assert.Equal(P(499), GuestSpending.StartingMoney(299));     // rand(300) tops out at 299
            Assert.Equal(P(10), GuestSpending.LeaveBelow);

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

        // The Fries record, end to end at the shop's default price of £60 and both sliders at 50: unit
        // cost 30, base 37, N for a hungry guest (need A 80, happiness 50, nausea 10) = 100 + 16 − 1 +
        // 2 = 117, want = 37 × 117 / 100 = 43 × 150 / 100 = 64 > 60: bought. The same guest with no
        // appetite (N = 101): 37 → 55, not bought. A sanity anchor on the whole chain against real
        // record numbers, not a rule of its own.
        [Fact]
        public void AHungryGuestBuysFriesAtTheDefaultPriceAndAFedOneDoesNot()
        {
            int unitCost = GuestSpending.UnitCost(Fries.UnitCost, 50, 50);
            Assert.Equal(30, unitCost);
            int hungry = GuestSpending.Want(GuestSpending.WantBase(unitCost), GuestSpending.NeedFactor(80, 0, 10, 50, Fries), 50);
            int fed = GuestSpending.Want(GuestSpending.WantBase(unitCost), GuestSpending.NeedFactor(0, 0, 10, 50, Fries), 50);
            Assert.Equal(64, hungry);
            Assert.Equal(55, fed);
            Assert.True(GuestSpending.WouldBuy(hungry, 60, P(200)));
            Assert.False(GuestSpending.WouldBuy(fed, 60, P(200)));
        }
    }
}
