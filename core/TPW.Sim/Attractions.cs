using System;

namespace TPW.Sim
{
    /// <summary>The game's own attraction type codes.
    ///
    /// ⚠ THESE ARE NOT IN CATEGORY ORDER AND THE NUMBERING IS NOT INCIDENTAL — it is the switch value the
    /// game itself dispatches on, so a port that renumbers them for tidiness has to translate at every
    /// boundary and will eventually forget to. Type 2 is "Feature", which means scenery, toilets, bins,
    /// benches and the staff room, not decoration alone.</summary>
    public enum AttractionType
    {
        RollerCoaster = 1,
        Feature = 2,
        Ride = 3,          // NonPathedRide — what the purchase screen calls "Rides"
        Shop = 4,
        SideShow = 5,
        TrackRide = 6,     // PathedRide
        TourRide = 7,
    }

    public static class Attractions
    {
        /// <summary>How many of each type a park can hold. ⭐ CAPACITY, read off the POOLS in code — the
        /// `N` column of fable's pool table, beside each pool's pointer and stride (PoolOfRides 0x80103844
        /// N=15, PoolOfShops 0x80103854 N=20, PoolOfFeatures 0x80103858 N=45, and so on).
        ///
        /// ⚠⚠ NOT FROM THE PURCHASE SCREEN, AND IT IS WORSE THAN A SIMPLE OFFSET. That screen is headed
        /// "Stock" and shows what is AVAILABLE TO BUY — which master points out is gated by RESEARCH, and
        /// not only for rides: **features and shops are researched too**. Three readings of that screen
        /// across three parks gave rides 15 / 14 / 15 and shops 20 / 20 / 19 while capacity never moved.
        ///
        /// So the screen conflates three separate things — pool capacity, items placed, and research
        /// progress — and no arithmetic recovers capacity from it without knowing the other two. My first
        /// reading, "it prints capacity minus placed", was a tidier rule than the game has. The pool table is
        /// the only source that states capacity alone.
        ///
        /// ⚠ TourRide has three slots and appears on no purchase screen we have seen, so the UI is not even
        /// a complete list of the types. Reading capacity off the interface would have lost one entirely.</summary>
        public static int PoolSize(AttractionType t) => t switch
        {
            AttractionType.Ride => 15,
            AttractionType.TourRide => 3,
            AttractionType.TrackRide => 2,
            AttractionType.RollerCoaster => 2,
            AttractionType.Shop => 20,
            AttractionType.Feature => 45,
            AttractionType.SideShow => 10,
            _ => 0,
        };

        /// <summary>Every type a park has a pool for.</summary>
        public static readonly AttractionType[] All =
        {
            AttractionType.RollerCoaster, AttractionType.Feature, AttractionType.Ride,
            AttractionType.Shop, AttractionType.SideShow, AttractionType.TrackRide, AttractionType.TourRide,
        };
    }

    /// <summary>What a sale does to the books.</summary>
    public readonly struct SaleResult
    {
        /// <summary>What the guest hands over.</summary>
        public readonly Money GuestPays;
        /// <summary>Change to the park's balance. ⚠ May be NEGATIVE — see ShopSale.</summary>
        public readonly Money BankDelta;
        public SaleResult(Money guestPays, Money bankDelta) { GuestPays = guestPays; BankDelta = bankDelta; }
    }

    public static class Trading
    {
        /// <summary>A shop sale.
        ///
        /// ⚠⚠ THE BANK BOOKS price − unitCost, AND IT IS NOT CLAMPED AT ZERO. Price the goods below cost and
        /// every sale charges the difference to the park: the game books a real LOSS per customer rather than
        /// refusing the sale or earning nothing. A port that clamps turns a bankruptcy mechanic into a
        /// harmless mistake, and the player never learns why undercutting was a bad idea.
        ///
        /// ⚠ The GUEST still pays the full price either way. What the guest pays and what the park earns are
        /// two different numbers, and conflating them silently swallows the unit cost.</summary>
        public static SaleResult ShopSale(int pricePounds, int unitCostPounds)
            => new(Money.FromPounds(pricePounds), Money.FromPounds(pricePounds - unitCostPounds));

        /// <summary>A sideshow play.
        ///
        /// ⚠ DIFFERENT BOOKKEEPING FROM A SHOP, NOT A VARIATION OF IT. The bank takes the FULL price with no
        /// unit cost, and the prize is a separate payout. The guest pays price − prize, i.e. they hand over
        /// the price and get the prize's worth back, so a winning guest can effectively pay nothing.
        ///
        /// Reusing the shop path here would silently subtract a unit cost the game never charges.</summary>
        public static SaleResult SideShowPlay(int pricePounds, int prizePounds)
            => new(Money.FromPounds(pricePounds - prizePounds), Money.FromPounds(pricePounds));

        /// <summary>Admission. The bank's constructed default is £40.</summary>
        public static SaleResult Admission(Money entryFee) => new(entryFee, entryFee);
    }
}
