using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>The two purchase routines, 0x8008E5EC (shop) and 0x8008EE78 (sideshow), and the two
    /// sell routines they call through slot 25 (behaviour.md §2.5, economy.md §4.2/§4.3, re-read).</summary>
    public class VisitorPurchaseTests
    {
        sealed class Dice : IRandomSource
        {
            readonly Queue<int> _v;
            public Dice(params int[] v) => _v = new Queue<int>(v);
            public List<int> Bounds { get; } = new();
            public int Next(int n) { Bounds.Add(n); return _v.Count > 0 ? _v.Dequeue() : 1; }
        }

        sealed class World : IShopWorld
        {
            /// <summary>The Fries record: unit cost 40, kind 7, need A 20, need B 0, happiness 5, nausea 10.</summary>
            public ShopProduct ProductValue { get; set; } = new(40, 7, 20, 0, 5, 10);
            public int Price { get; set; } = 60;
            public int Quality { get; set; } = 50;
            public int Second { get; set; } = 50;
            public SideShowGame GameValue { get; set; } = new(10, 30, 25);
            public bool SpawnAllowed { get; set; } = true;

            public List<ShopSale> Sales { get; } = new();
            public List<SideShowPlay> Plays { get; } = new();
            public int Served { get; private set; }
            public List<int> Satisfaction { get; } = new();
            public List<(int Id, int Value)> Events { get; } = new();
            public int Spawns { get; private set; }
            public int ModelReleases { get; private set; }

            public ShopProduct Product(Visitor g) => ProductValue;
            public int SalePrice(Visitor g) => Price;
            public int QualitySlider(Visitor g) => Quality;
            public int SecondSlider(Visitor g) => Second;
            public void BookSale(Visitor g, ShopSale s) => Sales.Add(s);
            public SideShowGame Game(Visitor g) => GameValue;
            public void BookPlay(Visitor g, SideShowPlay p) => Plays.Add(p);
            public void CountGuestServed(Visitor g) => Served++;
            public void RecordSatisfaction(Visitor g, int amount) => Satisfaction.Add(amount);
            public void PostEvent(int id, int value) => Events.Add((id, value));
            public bool TrySpawnProp(Visitor g) { if (!SpawnAllowed) return false; Spawns++; return true; }
            public void ReleaseModel(Visitor g) => ModelReleases++;
        }

        /// <summary>A guest with £200, happiness 50, nausea 10, need A 80 (hungry), need B 30, and the
        /// rest clear of any threshold. Against the default World it wants the fries at 64 (see
        /// GuestSpendingTests) and buys them at 60.</summary>
        static Visitor Guest(int money = 200)
        {
            var v = Visitor.Spawn(new Dice(), 0);
            v.Money = Money.FromPounds(money);
            v.Happiness = 50; v.Nausea = 10; v.NeedA = 80; v.NeedB = 30; v.RideDesire = 10;
            v.Rubbish = 20; v.Tiredness = 40; v.Boredom = 20; v.VisitorType = 0;
            v.HasTarget = true;
            return v;
        }

        // ───────────────────────── the shop's books (0x800B69E0) ─────────────────────────

        // ⭐ MARGIN TO THE BANK AS TYPED INCOME, PRICE FROM THE GUEST, TAKINGS AND PROFIT TO THE SHOP.
        // REJECTS booking the price as income (it is price − unit cost), REJECTS charging the guest
        // the margin.
        [Fact]
        public void AShopSaleBooksTheMarginAndChargesThePrice()
        {
            var s = new ShopSale(price: 60, unitCost: 30);
            Assert.Equal(30, s.Margin);
            Assert.False(s.IsLoss);
            Assert.Equal(Money.FromPounds(30), s.Income);
            Assert.Equal(Money.Zero, s.Spend);
            Assert.Equal(Money.FromPounds(60), s.GuestPays);
            Assert.Equal((60, 30), (s.TakingsDelta, s.ProfitDelta));
        }

        // ⚠ BELOW COST IS A TRYSPEND, NOT NEGATIVE INCOME -- a different ledger. And a margin of EXACTLY
        // zero takes the spend path too (`blez`), with £0. REJECTS `< 0` for the loss test, REJECTS a
        // signed income.
        [Theory]
        [InlineData(20, 30, true, 0, 10, -10)]
        [InlineData(30, 30, true, 0, 0, 0)]
        [InlineData(31, 30, false, 1, 0, 1)]
        public void SellingAtOrBelowCostTakesTheSpendPath(int price, int cost, bool loss, int income, int spend, int profit)
        {
            var s = new ShopSale(price, cost);
            Assert.Equal(loss, s.IsLoss);
            Assert.Equal(Money.FromPounds(income), s.Income);
            Assert.Equal(Money.FromPounds(spend), s.Spend);
            Assert.Equal(Money.FromPounds(price), s.GuestPays);
            Assert.Equal(profit, s.ProfitDelta);
        }

        // ───────────────────────── the sideshow's books (0x800B7524) ─────────────────────────

        // ⭐ THE ROLL IS STRICTLY BELOW THE CHANCE, the bank takes the whole price, and the prize is a
        // separate TrySpend. A guest that wins £25 on a £10 play is charged −£15. REJECTS `<=` on the
        // roll, REJECTS netting the prize out of the income.
        [Theory]
        [InlineData(29, true, 25, -15, 1)]
        [InlineData(30, false, 0, 10, 0)]
        [InlineData(0, true, 25, -15, 1)]
        public void ASideShowPlayWinsBelowTheChanceAndPaysThePrizeSeparately(int roll, bool won, int payout, int net, int wins)
        {
            var p = new SideShowPlay(new SideShowGame(10, 30, 25), roll);
            Assert.Equal(won, p.Won);
            Assert.Equal(payout, p.Payout);
            Assert.Equal(net, p.Net);
            Assert.Equal(Money.FromPounds(net), p.GuestPays);
            Assert.Equal(Money.FromPounds(10), p.Income);
            Assert.Equal(Money.FromPounds(payout), p.Spend);
            Assert.Equal((wins, payout, 10), (p.WinsDelta, p.PrizesDelta, p.TakingsDelta));
        }

        // ⚠ A WON PRIZE OF £0 IS NOT A WIN (`beq s2, zero` on the payout). REJECTS counting it from the roll.
        [Fact]
        public void WinningNothingIsNotRecordedAsAWin()
        {
            var p = new SideShowPlay(new SideShowGame(10, 100, 0), 0);
            Assert.True(p.Won);
            Assert.False(p.PaysPrize);
            Assert.Equal((0, 0, 10), (p.WinsDelta, p.PrizesDelta, p.Net));
            Assert.Equal(Money.Zero, p.Spend);
        }

        // ───────────────────────── 0x8008E5EC: the shop purchase ─────────────────────────

        // ⭐ THE SALE, END TO END: the guest's want is 64 against £60, so it buys; the books get a
        // (60, 30) sale, £60 leaves the guest in Money units, one rand(25) is rolled for the litter, and
        // the served counter ticks. REJECTS charging in pounds (600 raw), REJECTS a litter roll before
        // the decision.
        [Fact]
        public void BuyingFriesChargesThePriceDropsLitterAndCountsTheGuest()
        {
            var g = Guest(); var w = new World(); var dice = new Dice(7);
            Assert.True(VisitorPurchase.BuyAtShop(g, w, dice));
            Assert.Equal(Money.FromPounds(140), g.Money);
            Assert.Equal(new[] { 25 }, dice.Bounds);
            Assert.Equal(20 + 30 + 7, g.Rubbish);
            Assert.Single(w.Sales);
            Assert.Equal((60, 30), (w.Sales[0].Price, w.Sales[0].UnitCost));
            Assert.Equal(1, w.Served);
        }

        // ⭐ FRIES: need B += second/15 FIRST, then the burger arm -- need A down by the record's 20 and
        // the toilet need up by the same, nausea up by 10, happiness up by 5 × (50 − 4) / 100 = 2, need
        // B up by the record's 0. REJECTS skipping the second-slider term (kind 7 is not kind 0),
        // REJECTS forgetting the toilet need. (Second at 60 drops the unit cost to 28 and the want to
        // exactly 60, so the price is £50 here: the strict test is its own test above.)
        [Fact]
        public void FriesRelieveNeedAFeedTheToiletNeedAndMakeTheGuestThirstier()
        {
            var g = Guest(); var w = new World { Second = 60, Price = 50 };
            Assert.True(VisitorPurchase.BuyAtShop(g, w, new Dice(0)));
            Assert.Equal(80 - 20, g.NeedA);
            Assert.Equal(10 + 20, g.RideDesire);
            Assert.Equal(10 + 10, g.Nausea);
            Assert.Equal(30 + 4, g.NeedB);                 // 60 / 15, plus the record's 0
            Assert.Equal(50 + 2, g.Happiness);             // 5 × (50 − 4) / 100 = 2
        }

        // ⭐ DRINKS ARE THE MIRROR: need B down by +0x33, the toilet need up by it, need A UP by +0x32.
        // The Drinks record is (40, kind 1, 0, 40, 5, 5). REJECTS applying the burger arm to a drink.
        [Fact]
        public void DrinksRelieveNeedBAndSwapTheTwoNeeds()
        {
            var g = Guest(); g.NeedB = 90;
            var w = new World { ProductValue = new ShopProduct(40, 1, 0, 40, 5, 5), Price = 30 };
            Assert.True(VisitorPurchase.BuyAtShop(g, w, new Dice(0)));
            Assert.Equal(90 - 40, g.NeedB);
            Assert.Equal(10 + 40, g.RideDesire);
            Assert.Equal(80, g.NeedA);                     // + the record's 0
            Assert.Equal(10 + 5, g.Nausea);
        }

        // Burger, ice cream and restaurant share the arm, without the fries' extra term. REJECTS the
        // second-slider need-B term for kind 0.
        [Theory]
        [InlineData(0)] [InlineData(4)] [InlineData(5)]
        public void TheOtherFoodKindsUseTheBurgerArmWithoutTheThirstTerm(int kind)
        {
            var g = Guest(); var w = new World { ProductValue = new ShopProduct(40, kind, 20, 0, 5, 10), Second = 60, Price = 50 };
            Assert.True(VisitorPurchase.BuyAtShop(g, w, new Dice(0)));
            Assert.Equal(60, g.NeedA);
            Assert.Equal(30, g.NeedB);
        }

        // ⭐ A COSTUME REWRITES THE GUEST: type 8, flag 0x80, the model handle released, event (6, 1), and
        // happiness by quality alone (15 × 50 / 100 = 7). The Costume record is (60, kind 2, 0, 0, 15, 0)
        // at £90. REJECTS the food happiness formula, REJECTS leaving the type alone.
        [Fact]
        public void ACostumeChangesTheVisitorTypeToEight()
        {
            var g = Guest(); g.Happiness = 0;             // misery: N = 100 + 0 + 0 − 0 + 15 = 115
            var w = new World { ProductValue = new ShopProduct(60, 2, 0, 0, 15, 0), Price = 40, Second = 100 };
            // unit cost 60 × (75 + 12 − 25) / 100 = 37, base 46, want 46 × 115 / 100 = 52 × 100 / 100 = 52 > 40
            Assert.True(VisitorPurchase.BuyAtShop(g, w, new Dice(0)));
            Assert.Equal(8, g.VisitorType);
            Assert.True(g.Flag80);
            Assert.Equal(1, w.ModelReleases);
            Assert.Contains((6, 1), w.Events);
            Assert.Equal(7, g.Happiness);                  // 15 × 50 / 100, no second-slider term
            Assert.Equal(80, g.NeedA);                     // untouched
        }

        // ⭐ A BALLOON IS SPAWNED ONCE. First purchase: the prop is spawned, flag 0x10 set, event (5, 1),
        // happiness 10 × 50 / 100 = 5. REJECTS spawning without setting the flag.
        [Fact]
        public void ABalloonIsSpawnedAndFlagged()
        {
            var g = Guest();
            var w = new World { ProductValue = new ShopProduct(40, 3, 0, 0, 10, 0), Price = 20 };
            Assert.True(VisitorPurchase.BuyAtShop(g, w, new Dice(0)));
            Assert.True(g.Flag10);
            Assert.Equal(1, w.Spawns);
            Assert.Contains((5, 1), w.Events);
            Assert.Equal(55, g.Happiness);
        }

        // ⚠ A GUEST ALREADY CARRYING ONE PAYS AND GETS NOTHING: no spawn, no event, no happiness -- but
        // the money and the litter have already gone. REJECTS refusing the sale, REJECTS paying the
        // happiness anyway.
        [Fact]
        public void ASecondBalloonIsPaidForAndGivesNothing()
        {
            var g = Guest(); g.Flag10 = true;
            var w = new World { ProductValue = new ShopProduct(40, 3, 0, 0, 10, 0), Price = 20 };
            Assert.True(VisitorPurchase.BuyAtShop(g, w, new Dice(0)));
            Assert.Equal(Money.FromPounds(180), g.Money);
            Assert.Equal(0, w.Spawns);
            Assert.DoesNotContain((5, 1), w.Events);
            Assert.Equal(50, g.Happiness);
            Assert.Equal(1, w.Served);
        }

        // A refused spawn (pool full, or the mode flag) still pays the happiness; only the flag, the
        // attach and the event are skipped. REJECTS treating a refusal like the already-carrying case.
        [Fact]
        public void ARefusedBalloonStillPaysTheHappiness()
        {
            var g = Guest();
            var w = new World { ProductValue = new ShopProduct(40, 3, 0, 0, 10, 0), Price = 20, SpawnAllowed = false };
            Assert.True(VisitorPurchase.BuyAtShop(g, w, new Dice(0)));
            Assert.False(g.Flag10);
            Assert.DoesNotContain((5, 1), w.Events);
            Assert.Equal(55, g.Happiness);
        }

        // A gift posts (7, 1) and pays by quality alone.
        [Fact]
        public void AGiftPostsItsEventAndPaysByQuality()
        {
            var g = Guest();
            var w = new World { ProductValue = new ShopProduct(40, 6, 0, 0, 15, 0), Price = 20, Second = 100 };
            Assert.True(VisitorPurchase.BuyAtShop(g, w, new Dice(0)));
            Assert.Contains((7, 1), w.Events);
            Assert.Equal(57, g.Happiness);                 // 15 × 50 / 100 = 7, second slider ignored
        }

        // A kind past 7 matches no arm: the sale still goes through (money, litter, counter) and no stat
        // moves. REJECTS refusing the sale, REJECTS a default arm.
        [Fact]
        public void AnUnknownKindSellsButChangesNoStat()
        {
            var g = Guest();
            var w = new World { ProductValue = new ShopProduct(40, 8, 20, 20, 5, 10), Price = 20 };
            Assert.True(VisitorPurchase.BuyAtShop(g, w, new Dice(0)));
            Assert.Equal(Money.FromPounds(180), g.Money);
            Assert.Equal((80, 30, 10, 50, 10), (g.NeedA, g.NeedB, g.Nausea, g.Happiness, g.RideDesire));
            Assert.Equal(1, w.Served);
            Assert.Single(w.Satisfaction);
            Assert.Empty(w.Events);                        // both event tables stop at kind 7
        }

        // ⭐ THE WANT IS FROM THE ENTRY SNAPSHOT. The drink lowers need B from 90 to 50, but the want that
        // decided the sale was the 90's; and the satisfaction delta is against the entry happiness. A
        // second purchase right after uses the new stats. REJECTS re-reading the stats after the arm.
        [Fact]
        public void TheWantUsesTheStatsAsTheyWereOnEntry()
        {
            var g = Guest(); g.NeedB = 90;
            var w = new World { ProductValue = new ShopProduct(40, 1, 0, 40, 5, 5), Price = 60 };
            // N = 100 + 90×40/100 = 36 + 0 − 0 + 2 = 138; unit cost 30, base 37; 37×138/100 = 51 × 150/100 = 76 > 60.
            Assert.True(VisitorPurchase.BuyAtShop(g, w, new Dice(0)));
            Assert.Equal(50, g.NeedB);
            // Now N = 100 + 20 + 2 = 122: 37 × 122 / 100 = 45 × 152 / 100 = 68 > 60 still; at £70 it is not.
            w.Price = 70;
            Assert.False(VisitorPurchase.BuyAtShop(g, w, new Dice(0)));
            Assert.Equal(50, g.NeedB);
        }

        // ⭐ NO SALE, BUT A VERDICT ALL THE SAME. At £100 the fries (worth 64) are not bought: no money
        // moves, no die is rolled, no litter, no counter -- and the guest still gets the rip-off bubble,
        // the satisfaction is still recorded (0, rating −12) and both events still post. REJECTS
        // returning early on a refusal.
        [Fact]
        public void ARefusedSaleStillRecordsTheVisitAndPassesJudgement()
        {
            var g = Guest(); var w = new World { Price = 100 }; var dice = new Dice(7);
            Assert.False(VisitorPurchase.BuyAtShop(g, w, dice));
            Assert.Equal(Money.FromPounds(200), g.Money);
            Assert.Empty(dice.Bounds);
            Assert.Equal(20, g.Rubbish);
            Assert.Equal(0, w.Served);
            Assert.Empty(w.Sales);
            Assert.Equal(new[] { 0 }, w.Satisfaction);
            Assert.Equal(new[] { (0xE, -12), (0x9, -18) }, w.Events);   // kind 7: rating event 0xE, verdict event 9; (64 − 100) / 2
            Assert.Equal(0x36, g.Bubble);
        }

        // The affordability half: a guest with £50 wants the fries at 64 but cannot pay £60. Same
        // aftermath, and the verdict is on the price it could not pay: (64 − 60) / 2 = 2, a bargain.
        [Fact]
        public void AGuestThatCannotAffordItGetsTheBargainBubbleAnyway()
        {
            var g = Guest(money: 50); var w = new World();
            Assert.False(VisitorPurchase.BuyAtShop(g, w, new Dice()));
            Assert.Equal(Money.FromPounds(50), g.Money);
            Assert.Equal(0x38, g.Bubble);
        }

        // A verdict of 0 or 1 leaves whatever bubble was there ALONE -- it does not clear it. Price £62
        // against want 64: verdict 1. REJECTS writing a "no bubble" over an existing one.
        [Fact]
        public void AFairPriceLeavesTheBubbleAlone()
        {
            var g = Guest(); g.Bubble = 0x33; var w = new World { Price = 62 };
            Assert.True(VisitorPurchase.BuyAtShop(g, w, new Dice(0)));
            Assert.Equal(0x33, g.Bubble);
        }

        // ⭐ THE EVENT TABLES BY KIND: verdict 9/0xC/0xA/0xA/9/0xB/0xA/9 and rating 0xE/0x11/0xF/0xF/0xE/
        // 0x10/0xF/0xE (0x800E3B34, 0x800E6D44). REJECTS one id for all kinds.
        [Theory]
        [InlineData(0, 0xE, 0x9)] [InlineData(1, 0x11, 0xC)] [InlineData(2, 0xF, 0xA)] [InlineData(3, 0xF, 0xA)]
        [InlineData(4, 0xE, 0x9)] [InlineData(5, 0x10, 0xB)] [InlineData(6, 0xF, 0xA)] [InlineData(7, 0xE, 0x9)]
        public void TheRatingAndVerdictEventsAreLookedUpByKind(int kind, int rating, int verdict)
        {
            var g = Guest(); var w = new World { ProductValue = new ShopProduct(40, kind, 0, 0, 0, 0), Price = 1000 };
            VisitorPurchase.BuyAtShop(g, w, new Dice());
            Assert.Equal(new[] { rating, verdict }, new[] { w.Events[0].Id, w.Events[1].Id });
        }

        // The satisfaction recorded is 5 × Δhappiness, clamped at zero, and the rating posted with it is
        // (that − 50) / 4: fries at quality 100 pay 5 × (100 − 3) / 100 = 4 happiness → 20 → rating −7.
        [Fact]
        public void TheSatisfactionIsFiveTimesTheHappinessGained()
        {
            var g = Guest(); var w = new World { Quality = 100 };
            Assert.True(VisitorPurchase.BuyAtShop(g, w, new Dice(0)));
            Assert.Equal(54, g.Happiness);
            Assert.Equal(new[] { 20 }, w.Satisfaction);
            Assert.Contains((0xE, -7), w.Events);
        }

        // ───────────────────────── 0x8008EE78: the sideshow ─────────────────────────

        // ⭐ A LOSS: the Arcade is worth 21 to this guest, it pays £10, rolls 30 (not below the 30%
        // chance), loses, and is +10 happier for it. One rand(100), after the decision. REJECTS the
        // roll before the affordability test, REJECTS −10 on a loss.
        [Fact]
        public void LosingAtTheArcadeCostsTenPoundsAndPaysTenHappiness()
        {
            var g = Guest(); var w = new World(); var dice = new Dice(30);
            Assert.True(VisitorPurchase.PlaySideShow(g, w, dice));
            Assert.Equal(new[] { 100 }, dice.Bounds);
            Assert.Equal(Money.FromPounds(190), g.Money);
            Assert.Equal(60, g.Happiness);
            Assert.Single(w.Plays);
            Assert.False(w.Plays[0].Won);
            Assert.Equal(1, w.Served);
        }

        // ⚠ DO NOT FIX: A WIN WORTH MORE THAN THE PLAY MAKES THE GUEST £15 RICHER AND 10 SADDER. The
        // happiness follows the sign of `price − payout` (0x8009279C × 0x8010320C), and net is −15.
        // REJECTS the intuitive sign, REJECTS charging the price and refunding nothing.
        [Fact]
        public void WinningTheArcadeRefundsMoreThanThePriceAndDocksHappiness()
        {
            var g = Guest(); var w = new World();
            Assert.True(VisitorPurchase.PlaySideShow(g, w, new Dice(29)));
            Assert.Equal(Money.FromPounds(215), g.Money);
            Assert.Equal(40, g.Happiness);
            Assert.True(w.Plays[0].Won);
        }

        // Break-even (prize equal to the price) pays nothing either way: sign 0. A sure thing at £10 for
        // £10 is worth 10 → 20 → 30, so it is played.
        [Fact]
        public void BreakingEvenLeavesHappinessAlone()
        {
            var g = Guest(); var w = new World { GameValue = new SideShowGame(10, 100, 10) };
            Assert.True(VisitorPurchase.PlaySideShow(g, w, new Dice(0)));
            Assert.Equal(Money.FromPounds(200), g.Money);
            Assert.Equal(50, g.Happiness);
        }

        // ⭐ THE FORTUNE TELLER (chance 0, prize 0, £10) IS ALWAYS PLAYED, never won, and always +10.
        // REJECTS valuing a no-win game at nothing.
        [Fact]
        public void TheFortuneTellerIsAlwaysWorthPlaying()
        {
            var g = Guest(); g.Happiness = 0;
            var w = new World { GameValue = new SideShowGame(10, 0, 0) };
            Assert.True(VisitorPurchase.PlaySideShow(g, w, new Dice(0)));
            Assert.Equal(Money.FromPounds(190), g.Money);
            Assert.Equal(10, g.Happiness);
            Assert.False(w.Plays[0].PaysPrize);
        }

        // ⭐ NOT PLAYED: too dear (worth 21, priced 22) or unaffordable (£5 in hand against £10). No die,
        // no money, no counter -- and the aftermath still fires: satisfaction 0, events (0x12, −12) and
        // (0xD, verdict), the bubble. At £22 the verdict is (21 − 22) / 2 = 0 toward zero, FAIR, so the
        // bubble the guest walked in with stays; at £10 unaffordable it is 5, a bargain it cannot have.
        // REJECTS returning early on a refusal.
        [Theory]
        [InlineData(22, 200, 0, 0x33)]
        [InlineData(10, 5, 5, 0x38)]
        public void ARefusedPlayStillRecordsTheVisit(int price, int money, int verdict, int bubble)
        {
            var g = Guest(money); g.Bubble = 0x33;
            var w = new World { GameValue = new SideShowGame(price, 30, 25) }; var dice = new Dice(0);
            Assert.False(VisitorPurchase.PlaySideShow(g, w, dice));
            Assert.Empty(dice.Bounds);
            Assert.Equal(Money.FromPounds(money), g.Money);
            Assert.Equal(0, w.Served);
            Assert.Empty(w.Plays);
            Assert.Equal(new[] { 0 }, w.Satisfaction);
            Assert.Equal(new[] { (0x12, -12), (0xD, verdict) }, w.Events);
            Assert.Equal(bubble, g.Bubble);
        }

        // A play priced two over its worth is a rip-off: (21 − 23) / 2 = −1 → bubble 0x36.
        [Fact]
        public void APlayPricedWellOverItsWorthIsARipOff()
        {
            var g = Guest(); var w = new World { GameValue = new SideShowGame(23, 30, 25) };
            Assert.False(VisitorPurchase.PlaySideShow(g, w, new Dice()));
            Assert.Equal((0xD, -1), w.Events[1]);
            Assert.Equal(0x36, g.Bubble);
        }

        // The sideshow's satisfaction is 5 × Δhappiness too: a loss's +10 → 50 → rating 0.
        [Fact]
        public void TheSideShowRecordsFiveTimesTheHappinessDelta()
        {
            var g = Guest(); var w = new World();
            VisitorPurchase.PlaySideShow(g, w, new Dice(99));
            Assert.Equal(new[] { 50 }, w.Satisfaction);
            Assert.Equal((0x12, 0), w.Events[0]);
        }

        // ⚠ NOT REPRODUCED, AND PINNED SO THAT IT IS KNOWN: a guest at happiness 5 that wins big ends
        // at 0 here; the console's add (0x80092190) has no lower clamp and leaves the byte at −5.
        [Fact]
        public void AWinFromLowHappinessStopsAtZeroHereWhereTheConsoleGoesNegative()
        {
            var g = Guest(); g.Happiness = 5; var w = new World();
            VisitorPurchase.PlaySideShow(g, w, new Dice(0));
            Assert.Equal(0, g.Happiness);
        }
    }

    /// <summary>What a costume does downstream: the visitor type 8 it writes is one past the type table.</summary>
    public class CostumePreferenceTests
    {
        // ⚠ DO NOT FIX: type 8 reads the word after the eight rows (0x800F7A28 = 14) and the lookup does
        // not check its index. REJECTS returning 0 (the previous clamp), REJECTS wrapping to row 0 (90).
        [Fact]
        public void ACostumedGuestPrefersTheWordPastTheTable()
        {
            var v = Visitor.Spawn(new RideScoreDice(), 0);
            v.VisitorType = 8;
            Assert.Equal(14, RideScore.Preference(v));
            Assert.Equal(14, VisitorTables.CostumePreference);
            v.VisitorType = 0;
            Assert.Equal(90, RideScore.Preference(v));
        }

        sealed class RideScoreDice : IRandomSource { public int Next(int n) => 0; }
    }
}
