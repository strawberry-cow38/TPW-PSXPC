using System;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>What a shop or a sideshow has to be able to answer for a guest standing at it, and
    /// where the money it takes goes. Implemented by the placed attraction itself, because every one
    /// of these is per-INSTANCE state — two Burger Bars set to different prices are the point.
    ///
    /// ⭐ THIS IS WHERE THE PARK'S INCOME COMES FROM. Rides earn nothing in this game (findings/
    /// README, measured); shops and sideshows are the whole revenue side, and until this existed the
    /// port's only income was the entry fee.
    ///
    /// ⚠ THE PANEL THAT SETS THE PRICE AND THE TWO SLIDERS DOES NOT EXIST YET, so every shop runs at
    /// the record's own default price with both sliders at zero. That is what a freshly placed shop IS
    /// — it is not a made-up number — but a player who would have priced things cannot, so every
    /// takings figure this port produces is the default-price one.</summary>
    interface IShopSite
    {
        /// <summary>Type 4 only: the product bytes out of the record. Null for anything else.</summary>
        ShopProduct? Product { get; }

        /// <summary>Type 5 only: the game's price, win chance and prize. Null for anything else.</summary>
        SideShowGame? Game { get; }

        /// <summary>shop+0x88, pounds, player-set 1..500 (economy.md §6.1). Defaults to the record's own.</summary>
        int SalePrice { get; }

        /// <summary>shop+0x8A: raises the unit cost by a quarter per point and scales the happiness a
        /// purchase pays.</summary>
        int QualitySlider { get; }

        /// <summary>shop+0x7C, signed: lowers the unit cost by a quarter per point, lowers a food
        /// purchase's happiness by a fifteenth per point, and for kind 7 adds to need B.</summary>
        int SecondSlider { get; }

        /// <summary>0x800B69E0's books: the bank takes the price, the shop remembers what it took and
        /// what it made after the unit cost.</summary>
        void BookSale(ShopSale sale);

        /// <summary>0x800B7524's books: the bank takes the play's price, and pays out a prize when the
        /// roll wins. ⚠ THE PRIZE IS A SPEND, not a smaller income — the game pays it out of the bank.</summary>
        void BookPlay(SideShowPlay play);

        /// <summary>The stall's satisfaction total (+0x80, already clamped at zero by the caller) and its
        /// visit counter. Called on EVERY visit, bought or not — a stall nobody buys from still records
        /// that people came and did not.</summary>
        void RecordSatisfaction(int amount);

        /// <summary>target+0x14 += 1 (0x80063164). GUESS-high "guests served"; only on a sale or a play.</summary>
        void CountServed();
    }
}
