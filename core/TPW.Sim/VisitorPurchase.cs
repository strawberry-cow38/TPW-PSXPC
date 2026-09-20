using System;

namespace TPW.Sim
{
    /// <summary>One sale at a type-4 shop, as its vtable slot 25 books it (0x800B69E0, READ; economy.md
    /// §4.2). Built by the guest's purchase routine and handed to the host to apply.
    ///
    /// ⚠⚠ THE BANK BOOKS THE MARGIN, THE GUEST PAYS THE PRICE, AND THEY GO THROUGH DIFFERENT PRIMITIVES.
    /// A positive margin is typed income (0x80086A5C, type 4: the balance AND the type-4 income history);
    /// a zero or negative one is a TrySpend of its negation (0x800868B8: the balance and the SPEND
    /// history). `blez` at 0x800B6A14 sends a margin of exactly zero down the spend path with £0, which
    /// moves nothing but is not the income path either. The two ledgers differ, so the host is told
    /// which one, not just a delta.</summary>
    public readonly struct ShopSale
    {
        /// <summary>shop+0x88, pounds.</summary>
        public readonly int Price;
        /// <summary>0x800B6CBC's result, pounds (<see cref="GuestSpending.UnitCost"/>).</summary>
        public readonly int UnitCost;

        public ShopSale(int price, int unitCost) { Price = price; UnitCost = unitCost; }

        public int Margin => Price - UnitCost;
        /// <summary>The spend path is taken for a margin of zero too (`blez`).</summary>
        public bool IsLoss => Margin <= 0;
        /// <summary>Slot 25 returns the price, and the caller deducts exactly that (0x8008E874..0x8008E88C).</summary>
        public Money GuestPays => Money.FromPounds(Price);
        /// <summary>Typed income, type 4. Zero on the loss path.</summary>
        public Money Income => IsLoss ? Money.Zero : Money.FromPounds(Margin);
        /// <summary>TrySpend. Zero on the income path; £0 (still a call) on a zero margin.</summary>
        public Money Spend => IsLoss ? Money.FromPounds(-Margin) : Money.Zero;
        /// <summary>shop+0x8C += this (per-shop takings, pounds).</summary>
        public int TakingsDelta => Price;
        /// <summary>shop+0x90 += this (per-shop profit, pounds; goes negative with the margin).</summary>
        public int ProfitDelta => Margin;
    }

    /// <summary>One play at a type-5 sideshow, as its slot 25 books it (0x800B7524, READ; economy.md
    /// §4.3). The win roll is the ONE die in the sell routine and is passed in so the order is testable.
    ///
    /// ⚠ A WON PRIZE OF £0 IS NOT A WIN. The routine keeps `won ? prize : 0` in one register and tests
    /// THAT for the win bookkeeping (0x800B75E8: `beq s2, zero`), so a stall whose prize is £0 -- the
    /// Fortune Teller -- never counts a win, never pays one, whatever it rolls.</summary>
    public readonly struct SideShowPlay
    {
        public readonly SideShowGame Game;
        /// <summary>rand(100) (0x800B754C).</summary>
        public readonly int Roll;

        public SideShowPlay(SideShowGame game, int roll) { Game = game; Roll = roll; }

        /// <summary>`rand(100) &lt; chance` (0x800B7564, strict).</summary>
        public bool Won => Roll < Game.Chance;
        public int Payout => Won ? Game.Prize : 0;
        /// <summary>The bookkeeping's own win test, on the payout rather than the roll.</summary>
        public bool PaysPrize => Payout != 0;
        /// <summary>What slot 25 returns and the guest is charged: `price − payout`, which is NEGATIVE
        /// when the prize is worth more than the play (0x800B7580, 0x8008EFDC..0x8008EFE4).</summary>
        public int Net => Game.Price - Payout;
        public Money GuestPays => Money.FromPounds(Net);
        /// <summary>Typed income, type 5, of the FULL price: no unit cost (0x800B75E0).</summary>
        public Money Income => Money.FromPounds(Game.Price);
        /// <summary>TrySpend of the prize when <see cref="PaysPrize"/> (0x800B7620).</summary>
        public Money Spend => PaysPrize ? Money.FromPounds(Game.Prize) : Money.Zero;
        /// <summary>A+0x88 += this (wins).</summary>
        public int WinsDelta => PaysPrize ? 1 : 0;
        /// <summary>A+0x8C += this (prizes paid, pounds).</summary>
        public int PrizesDelta => Payout;
        /// <summary>A+0x90 += this (takings, pounds).</summary>
        public int TakingsDelta => Game.Price;
    }

    /// <summary>What the two purchase routines need of the building the guest is standing in (V+0x28).
    /// Every method is about that target. The routines do the arithmetic, the rolls and the guest;
    /// the host owns the bank, the stall's counters, the model pool and the message box.</summary>
    public interface IShopWorld
    {
        /// <summary>Type 4: the target's record bytes (rec+0x2E..+0x36 through the handle at A+0x18).</summary>
        ShopProduct Product(Visitor guest);
        /// <summary>Type 4: shop+0x88 (0x800B7148), pounds. Player-set, 1..500 (economy.md §6.1).</summary>
        int SalePrice(Visitor guest);
        /// <summary>Type 4: shop+0x8A (0x800B7060), the panel slider that raises the unit cost by a
        /// quarter per point and scales the happiness a purchase pays. economy.md's GUESS-medium
        /// "quality"; the panel's label is not read.</summary>
        int QualitySlider(Visitor guest);
        /// <summary>Type 4: shop+0x7C (0x800B70A8), the panel's other slider, a signed word. Lowers the
        /// unit cost by a quarter per point, lowers a food purchase's happiness by a fifteenth per point,
        /// and for kind 7 adds a fifteenth per point to need B. What the panel calls it is not read;
        /// economy.md's GUESS "discount/research modifier" does not fit the need-B effect.</summary>
        int SecondSlider(Visitor guest);
        /// <summary>Type 4: apply 0x800B69E0's books -- the bank primitive the sale names, and the
        /// shop's takings and profit words.</summary>
        void BookSale(Visitor guest, ShopSale sale);
        /// <summary>Type 5: A+0x84 / A+0x86 / A+0x78.</summary>
        SideShowGame Game(Visitor guest);
        /// <summary>Type 5: apply 0x800B7524's books -- typed income of the price, the prize's TrySpend
        /// and the three counters.</summary>
        void BookPlay(Visitor guest, SideShowPlay play);
        /// <summary>0x80063164: target+0x14 += 1. GUESS-high "guests served". Only on a sale or a play.</summary>
        void CountGuestServed(Visitor guest);
        /// <summary>The satisfaction recorder's counters (0x800B6FA0 for a shop, 0x800B7940 for a
        /// sideshow): the stall's +0x80 += <paramref name="amount"/> (already clamped at zero) and its
        /// visit counter += 1 (shop+0x84 / sideshow+0x7C). Called on EVERY visit, bought or not.</summary>
        void RecordSatisfaction(Visitor guest, int amount);
        /// <summary>0x800139B4 on the object at 0x8010265C with (id, value). transport.md §2.1 has that
        /// object as the message-box state machine; behaviour.md §2.5 calls these calls "sounds". The
        /// box forwards the pair to its +0x1BC when its flag bit 3 is set (0x800139BC..0x800139D4); what
        /// each id means to it is not traced.</summary>
        void PostEvent(int id, int value);
        /// <summary>Kind 3: 0x8005156C spawns from the pool at 0x80103878 (refused when the mode flag
        /// 0x80059A9C is set or the pool is full), then 0x8006B898 attaches it to the guest and
        /// 0x8006B85C puts it at the guest's position. True if something was spawned.</summary>
        bool TrySpawnProp(Visitor guest);
        /// <summary>Kind 2: if V+0x24 is non-zero, 0x80058F5C releases it (through 0x800317BC on the
        /// table at 0x8010978C) and V+0x24 is zeroed (0x8008EB68..0x8008EB80). GUESS-medium that V+0x24
        /// is the guest's model handle, released so the type-8 model is picked up.</summary>
        void ReleaseModel(Visitor guest);
    }

    /// <summary>The two purchase routines: 0x8008E5EC for a shop (type 4) and 0x8008EE78 for a sideshow
    /// (type 5), called from the unloading arms of state 22 (VisitorQueue.Unload). Both re-read from
    /// TPW.BIN for this port; the formulas are in <see cref="GuestSpending"/> and the books in
    /// <see cref="ShopSale"/> / <see cref="SideShowPlay"/>.
    ///
    /// ⭐ THE SAME SHAPE TWICE: value the thing, buy it if it is worth it and affordable, then --
    /// bought or not -- record how the visit went and pass judgement on the price. The routines return
    /// whether money moved; the state-22 handler ignores that and sets Idle either way.</summary>
    public static class VisitorPurchase
    {
        /// <summary>0x8008E5EC(guest, shop). Returns true if the guest bought.
        ///
        /// ⚠ DICE: rand(25) for the litter, and ONLY on a sale (0x8008E890 is past both refusals).
        ///
        /// ⭐ THE WANT IS COMPUTED FROM THE STATS AS THEY WERE ON ENTRY. Need B, nausea, need A and
        /// happiness are read into registers first (0x8008E5F8..0x8008E66C) and nothing after that
        /// re-reads them for the want; the satisfaction delta at the end is against the same entry
        /// happiness. So a product's own effects never feed back into its price.</summary>
        public static bool BuyAtShop(Visitor guest, IShopWorld world, IRandomSource rng)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            int needB = guest.NeedB, nausea = guest.Nausea, needA = guest.NeedA, happiness0 = guest.Happiness;
            var product = world.Product(guest);
            int quality = world.QualitySlider(guest), second = world.SecondSlider(guest);
            int unitCost = GuestSpending.UnitCost(product.UnitCost, quality, second);
            int n = GuestSpending.NeedFactor(needA, needB, nausea, happiness0, product);
            int want = GuestSpending.Want(GuestSpending.WantBase(unitCost), n, happiness0);
            int price = world.SalePrice(guest);

            bool bought = false;
            if (GuestSpending.WouldBuy(want, price, guest.Money))
            {
                var sale = new ShopSale(price, unitCost);
                world.BookSale(guest, sale);
                guest.Money -= sale.GuestPays;
                guest.Rubbish = Stat.Add(guest.Rubbish, GuestSpending.LitterDropped(rng.Next(GuestSpending.LitterRollMax)));
                ApplyProduct(guest, world, product, quality, second);
                world.CountGuestServed(guest);
                bought = true;
            }

            int kind = product.Kind;
            int ratingEvent = kind >= 0 && kind < GuestSpending.RatingEventByKind.Length ? GuestSpending.RatingEventByKind[kind] : 0;
            int verdictEvent = kind >= 0 && kind < GuestSpending.VerdictEventByKind.Length ? GuestSpending.VerdictEventByKind[kind] : 0;
            Aftermath(guest, world, want, price, happiness0, ratingEvent, verdictEvent);
            return bought;
        }

        /// <summary>The per-kind arms of the effect table 0x800E3B14 (0x8008E8B0..0x8008ED04). Every stat
        /// write goes through the clamped add (0x80092190) or subtract (0x800921C0).</summary>
        static void ApplyProduct(Visitor guest, IShopWorld world, ShopProduct p, int quality, int second)
        {
            switch (p.Kind)
            {
                case (int)ProductKind.Fries:
                    // 0x8008E8E4: need B += second / 15, then fall into the burger arm.
                    guest.NeedB = Stat.Add(guest.NeedB, second / GuestSpending.SecondSliderDivisor);
                    goto case (int)ProductKind.Burger;

                case (int)ProductKind.Burger:
                case (int)ProductKind.IceCream:
                case (int)ProductKind.Restaurant:
                    // 0x8008E91C. ⭐ RELIEVING A NEED FEEDS THE TOILET NEED BY THE SAME AMOUNT (V+0x5D,
                    // rides.md §0 item 1), which is what makes a type-2 toilet worth visiting afterwards.
                    guest.NeedA = Stat.Sub(guest.NeedA, p.NeedAValue);
                    guest.RideDesire = Stat.Add(guest.RideDesire, p.NeedAValue);
                    guest.Nausea = Stat.Add(guest.Nausea, p.NauseaValue);
                    guest.Happiness = Stat.Add(guest.Happiness, GuestSpending.HappinessGain(p.HappinessValue, quality, second, food: true));
                    guest.NeedB = Stat.Add(guest.NeedB, p.NeedBValue);
                    break;

                case (int)ProductKind.Drinks:
                    // 0x8008EA30: the burger arm with the two needs swapped.
                    guest.NeedB = Stat.Sub(guest.NeedB, p.NeedBValue);
                    guest.RideDesire = Stat.Add(guest.RideDesire, p.NeedBValue);
                    guest.Nausea = Stat.Add(guest.Nausea, p.NauseaValue);
                    guest.Happiness = Stat.Add(guest.Happiness, GuestSpending.HappinessGain(p.HappinessValue, quality, second, food: true));
                    guest.NeedA = Stat.Add(guest.NeedA, p.NeedAValue);
                    break;

                case (int)ProductKind.Costume:
                    // 0x8008EB44: type := 8 (0x800926AC), flag 0x80 (0x80094050), release V+0x24, event.
                    guest.VisitorType = GuestSpending.CostumeVisitorType;
                    guest.Flag80 = true;
                    world.ReleaseModel(guest);
                    world.PostEvent(GuestSpending.EventCostume, 1);
                    guest.Happiness = Stat.Add(guest.Happiness, GuestSpending.HappinessGain(p.HappinessValue, quality, second, food: false));
                    break;

                case (int)ProductKind.Balloon:
                    // 0x8008EBE0. ⚠ A GUEST ALREADY CARRYING ONE PAYS AND GETS NOTHING: the flag test
                    // jumps straight to the guest counter (0x8008EBE8 → 0x8008ED04), past the happiness.
                    // A refused spawn still pays the happiness (0x8008EBFC → 0x8008EC5C); only the flag,
                    // the attach and the event are skipped.
                    if (guest.Flag10) break;
                    if (world.TrySpawnProp(guest))
                    {
                        guest.Flag10 = true;
                        world.PostEvent(GuestSpending.EventBalloon, 1);
                    }
                    guest.Happiness = Stat.Add(guest.Happiness, GuestSpending.HappinessGain(p.HappinessValue, quality, second, food: false));
                    break;

                case (int)ProductKind.Gift:
                    // 0x8008ECA4.
                    world.PostEvent(GuestSpending.EventGift, 1);
                    guest.Happiness = Stat.Add(guest.Happiness, GuestSpending.HappinessGain(p.HappinessValue, quality, second, food: false));
                    break;

                default:
                    // `sltiu v1, a0, 8; beq` (0x8008E8BC): no arm, nothing changes.
                    break;
            }
        }

        /// <summary>0x8008EE78(guest, sideshow). Returns true if the guest played.
        ///
        /// ⚠ DICE: rand(100) for the win, inside the sell (0x800B754C), and ONLY on a play.
        ///
        /// ⚠ DO NOT FIX: HAPPINESS FOLLOWS THE SIGN OF WHAT WAS PAID. `happiness += 10 × sign(net)`
        /// (0x8008EFE8..0x8008F00C, sign at 0x8009279C), and net is `price − payout`. Losing pays +10;
        /// winning a prize worth more than the play pays −10; breaking even pays nothing. It reads
        /// inverted and it is what the console does.
        ///
        /// ⚠ NOT REPRODUCED: the −10 through 0x80092190, which clamps the top only (`slti 0x65`, no
        /// lower test), can leave the original's happiness byte NEGATIVE for a guest under 10 that
        /// wins big. The port's Visitor stats clamp at 0 on assignment, so here it stops at 0. Both
        /// sides then meet Idle's leave check (&lt; 5); the byte's value afterwards is the divergence.</summary>
        public static bool PlaySideShow(Visitor guest, IShopWorld world, IRandomSource rng)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            int happiness0 = guest.Happiness;
            var game = world.Game(guest);
            int want = GuestSpending.SideShowWant(game.Chance, game.Prize, happiness0);

            bool played = false;
            if (GuestSpending.WouldPlay(want, game.Price, guest.Money))
            {
                var play = new SideShowPlay(game, rng.Next(100));
                world.BookPlay(guest, play);
                guest.Money -= play.GuestPays;
                guest.Happiness = Stat.Add(guest.Happiness, GuestSpending.SideShowHappinessStep * Math.Sign(play.Net));
                world.CountGuestServed(guest);
                played = true;
            }

            Aftermath(guest, world, want, game.Price, happiness0, GuestSpending.EventSideShowRating, GuestSpending.EventSideShowVerdict);
            return played;
        }

        /// <summary>The tail both routines share once the buy/no-buy branches rejoin (0x8008ED1C.. and
        /// 0x8008F024..): the satisfaction recorder with 5 × Δhappiness, then the verdict's event and
        /// bubble. A zero event id means "post nothing" -- the shop's tables have no row past kind 7.</summary>
        static void Aftermath(Visitor guest, IShopWorld world, int want, int price, int happiness0, int ratingEvent, int verdictEvent)
        {
            int fiveTimesDelta = (guest.Happiness - happiness0) * 5;
            world.RecordSatisfaction(guest, GuestSpending.SatisfactionAmount(fiveTimesDelta));
            if (ratingEvent != 0) world.PostEvent(ratingEvent, GuestSpending.SatisfactionRating(fiveTimesDelta));

            int verdict = GuestSpending.VerdictValue(want, price);
            if (verdictEvent != 0) world.PostEvent(verdictEvent, verdict);
            int bubble = GuestSpending.VerdictBubble(GuestSpending.Verdict(want, price));
            if (bubble != 0) guest.Bubble = bubble;
        }
    }
}
