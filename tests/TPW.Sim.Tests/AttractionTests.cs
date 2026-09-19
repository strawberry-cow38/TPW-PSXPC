using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class AttractionTests
    {
        // ⭐ THE TYPE CODES ARE THE GAME'S OWN SWITCH VALUES and are not in category order. Renumbering them
        // for tidiness forces a translation at every boundary, and one of those will eventually be missed.
        [Fact]
        public void TypeCodesMatchTheGamesOwnNumbering()
        {
            Assert.Equal(1, (int)AttractionType.RollerCoaster);
            Assert.Equal(2, (int)AttractionType.Feature);
            Assert.Equal(3, (int)AttractionType.Ride);
            Assert.Equal(4, (int)AttractionType.Shop);
            Assert.Equal(5, (int)AttractionType.SideShow);
            Assert.Equal(6, (int)AttractionType.TrackRide);
            Assert.Equal(7, (int)AttractionType.TourRide);
        }

        // ⭐ CAPACITY, from the pool table in code -- NOT from the purchase screen. That screen shows what
        // is AVAILABLE TO BUY, which is gated by RESEARCH as well as by what is already placed, so it
        // conflates three things and no arithmetic recovers capacity from it. Two readings of it disagreed
        // (Rides 14/Shops 20 versus Rides 15/Shops 19) for exactly that reason.
        [Theory]
        [InlineData(AttractionType.Ride, 15)]
        [InlineData(AttractionType.TrackRide, 2)]
        [InlineData(AttractionType.RollerCoaster, 2)]
        [InlineData(AttractionType.Shop, 20)]
        [InlineData(AttractionType.SideShow, 10)]
        [InlineData(AttractionType.Feature, 45)]
        [InlineData(AttractionType.TourRide, 3)]
        public void PoolSizesMatchTheGame(AttractionType t, int n) => Assert.Equal(n, Attractions.PoolSize(t));

        // ⚠ REJECTS TAKING THE UI AS THE INVENTORY. The purchase screen lists six types; the game has seven
        // pools. TourRide has three slots and appears on no screen either of us saw.
        [Fact]
        public void TourRideExistsDespiteNotBeingOnThePurchaseScreen()
        {
            Assert.Equal(3, Attractions.PoolSize(AttractionType.TourRide));
            Assert.Equal(7, Attractions.All.Length);
        }
    }

    public class TradingTests
    {
        // ⭐ REJECTS CLAMPING A SHOP SALE AT ZERO PROFIT. Price below cost books a real LOSS per customer.
        // Clamping turns a bankruptcy mechanic into a harmless mistake and the player never learns why
        // undercutting hurt.
        [Fact]
        public void SellingBelowCostChargesTheParkPerSale()
        {
            var r = Trading.ShopSale(pricePounds: 5, unitCostPounds: 12);
            Assert.Equal(Money.FromPounds(5), r.GuestPays);            // the guest still pays full price
            Assert.Equal(Money.FromPounds(-7), r.BankDelta);           // and the park loses the difference
            Assert.True(r.BankDelta < Money.Zero);
        }

        [Fact]
        public void AShopBooksPriceMinusUnitCost()
        {
            var r = Trading.ShopSale(20, 8);
            Assert.Equal(Money.FromPounds(20), r.GuestPays);
            Assert.Equal(Money.FromPounds(12), r.BankDelta);
        }

        // ⭐ REJECTS REUSING THE SHOP PATH FOR A SIDESHOW. The bank takes the FULL price with no unit cost,
        // and the prize is a separate payout -- so the two differ in what the bank gets AND in what the guest
        // hands over. Sharing the code would silently subtract a cost the game never charges.
        [Fact]
        public void ASideShowBooksTheFullPriceAndPaysTheePrizeSeparately()
        {
            var r = Trading.SideShowPlay(pricePounds: 10, prizePounds: 4);
            Assert.Equal(Money.FromPounds(10), r.BankDelta);   // full price, no unit cost
            Assert.Equal(Money.FromPounds(6), r.GuestPays);    // price net of the prize
        }

        [Fact]
        public void AWinningSideShowGuestCanPayNothing()
        {
            var r = Trading.SideShowPlay(10, 10);
            Assert.Equal(Money.Zero, r.GuestPays);
            Assert.Equal(Money.FromPounds(10), r.BankDelta);   // the park still books the price
        }

        [Fact]
        public void AdmissionIsTheEntryFeeBothWays()
        {
            var r = Trading.Admission(ParkEconomy.DefaultEntryFee);
            Assert.Equal(Money.FromPounds(40), r.GuestPays);
            Assert.Equal(Money.FromPounds(40), r.BankDelta);
        }

        // The measured shop base prices: £20 for two variants and £30 for a third. Recorded as a sanity
        // anchor on the arithmetic, not as the price list -- prices are adjustable and these are the bases.
        [Theory]
        [InlineData(20, 8, 12)]
        [InlineData(30, 8, 22)]
        public void MeasuredBasePricesBookTheExpectedMargin(int price, int cost, int margin)
            => Assert.Equal(Money.FromPounds(margin), Trading.ShopSale(price, cost).BankDelta);
    }
}
