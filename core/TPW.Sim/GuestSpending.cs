using System;

namespace TPW.Sim
{
    /// <summary>What a guest thinks of the price it just paid, or was asked to pay (the "value verdict").
    ///
    /// ⭐ THE VERDICT FIRES WHETHER OR NOT ANYTHING WAS BOUGHT. Both purchase routines compute
    /// `(want − price) / 2` after the buy/no-buy branches have rejoined (0x8008ED48..0x8008EE44,
    /// 0x8008F040..0x8008F0D4), so a guest that found the item too dear walks away with the rip-off
    /// bubble over its head. The bubble is about the PRICE, not the purchase.
    ///
    /// ⚠ THE FINDINGS' BUBBLE ASSIGNMENT WAS GARBLED, AND SO WAS THIS TYPE'S. behaviour.md §2.5 and
    /// economy.md §5 give "bubble 0x38 if verdict &lt; 2, bubble 0x36 if negative"; an earlier version of
    /// this enum put 0x38 on <see cref="Fair"/>. READ at 0x8008EDD4..0x8008EE3C and 0x8008F06C..0x8008F0D0:
    /// `slti v0, s0, 2; bne` skips to the negative test, so the 0x38 arm is the one taken for verdict
    /// ≥ 2, the 0x36 arm for verdict &lt; 0, and 0 and 1 show nothing. The good-value bubble goes on the
    /// bargain, not on the fair price.</summary>
    public enum ValueVerdict
    {
        /// <summary>Verdict below zero: bubble 0x36.</summary>
        RippedOff,
        /// <summary>Verdict 0 or 1: no bubble.</summary>
        Fair,
        /// <summary>Verdict 2 or more: bubble 0x38.</summary>
        Bargain,
    }

    /// <summary>The five product numbers a shop's definition record carries, as the purchase routine
    /// reads them (rec+0x2E..+0x36 through the handle at A+0x18; TPW.Data.ShopFields is the same
    /// block on the parser's side). The two "slots" behaviour.md §2.5 could not resolve are two of these
    /// bytes: the shop vtable at 0x800E6A8C (installed by its own slot 1, 0x800B715C, at 0x800B716C)
    /// has slot 55 = 0x800B6D88 → `lbu rec+0x33` and slot 56 = 0x800B6DD0 → `lbu rec+0x32`.
    ///
    /// ⭐ WHICH NEED IS WHICH, FROM THE DATA. The catalogued Drinks Shop records carry +0x32 = 0 and
    /// +0x33 = 40 and their product kind 1 takes <see cref="NeedBValue"/> off V+0x5E; the Fries and
    /// Burger records carry +0x32 = 20..25, +0x33 = 0 and their kinds take <see cref="NeedAValue"/> off
    /// V+0x5B. So V+0x5E is relieved by drinks and V+0x5B by food, which is behaviour.md §1's GUESS-low
    /// "thirst"/"hunger" the right way round. Kept as NeedA/NeedB here all the same: the arithmetic is
    /// READ, the names are still an inference from what the shops are called.</summary>
    public readonly struct ShopProduct
    {
        /// <summary>rec+0x2E u16: the base the unit cost is scaled from (<see cref="GuestSpending.UnitCost"/>).</summary>
        public readonly int UnitCost;
        /// <summary>rec+0x30 u8: 0..7, the index into the effect table 0x800E3B14 and the two event
        /// tables. See <see cref="ProductKind"/>. 8 and up match no arm and change nothing.</summary>
        public readonly int Kind;
        /// <summary>rec+0x32 u8 (slot 56): the weight of V+0x5B in the want, and what the food kinds
        /// take off V+0x5B and add to V+0x5D.</summary>
        public readonly int NeedAValue;
        /// <summary>rec+0x33 u8 (slot 55): the weight of V+0x5E in the want, and what kind 1 takes off
        /// V+0x5E and adds to V+0x5D.</summary>
        public readonly int NeedBValue;
        /// <summary>rec+0x34 u8 (0x800B6D40, "f2"): the weight of (100 − happiness) in the want, and the
        /// multiplier on the happiness a purchase pays.</summary>
        public readonly int HappinessValue;
        /// <summary>rec+0x36 u8 (0x800B6E18, "f1"): the weight of nausea AGAINST the want, and the nausea
        /// a food purchase adds.</summary>
        public readonly int NauseaValue;

        public ShopProduct(int unitCost, int kind, int needAValue, int needBValue, int happinessValue, int nauseaValue)
        {
            UnitCost = unitCost; Kind = kind; NeedAValue = needAValue; NeedBValue = needBValue;
            HappinessValue = happinessValue; NauseaValue = nauseaValue;
        }
    }

    /// <summary>The product kinds, by the jump-table index at rec+0x30. The BEHAVIOUR of each index is
    /// code (the arms of 0x8008E5EC); the NAME beside it is which of the 32 catalogued shop records on
    /// the disc carries that index (records.json), so the names are data and the numbers are not.
    ///
    /// Four arms: 0/4/5 and 7 relieve need A (7 first adds the second slider / 15 to need B, then falls
    /// into the same arm: 0x8008E8E4 → 0x8008E91C); 1 relieves need B; 2 swaps the guest's model; 3 hands
    /// it a prop; 6 only posts an event. 2, 3 and 6 pay happiness by quality alone.</summary>
    public enum ProductKind
    {
        Burger = 0,
        Drinks = 1,
        Costume = 2,
        Balloon = 3,
        IceCream = 4,
        Restaurant = 5,
        Gift = 6,
        Fries = 7,
    }

    /// <summary>A sideshow's three numbers on the live object (A+0x84, A+0x86, A+0x78; economy.md §4.3),
    /// copied from its record's +0x2C/+0x2E/+0x30 at placement (rides.md §1.3).</summary>
    public readonly struct SideShowGame
    {
        /// <summary>A+0x84 u16 (0x800B7744), pounds.</summary>
        public readonly int Price;
        /// <summary>A+0x86 u16 (0x800B7758), a percentage.</summary>
        public readonly int Chance;
        /// <summary>A+0x78 (0x800B7764), pounds.</summary>
        public readonly int Prize;
        public SideShowGame(int price, int chance, int prize) { Price = price; Chance = chance; Prize = prize; }
    }

    /// <summary>The purchase rules, as far as they are READ and no further. Sources: behaviour.md §2.5,
    /// economy.md §4.2, §4.3 and §5, and a re-read of 0x8008E5EC and 0x8008EE78 for this port.
    ///
    /// ⚠ N IS AN INPUT, NOT A PLAUSIBLE DEFAULT. The original behaviour.md §2.5 left the per-product
    /// coefficients undecoded, so this interface requires the host's need factor. A fabricated N would
    /// have exactly the right shape and the wrong value, and tests written against it would agree.
    ///
    /// READ refinement (findings/visitor-rest.md, 2026-09-20): type-4 coefficients are now located at
    /// definition bytes +0x33/+0x32/+0x36/+0x34, with all 37 records tabulated. The binary also divides
    /// earlier than this report's Want formula. N remains an input here; the coefficient discovery
    /// does not silently substitute that disputed arithmetic into the existing purchase contract.
    ///
    /// ⚠ THE WANT IS IN POUNDS, NOT MONEY. Every number in the two routines is an int of pounds: the
    /// record's unit cost, the shop's price word, the want, the verdict. Money enters only at the two
    /// affordability tests and the two deductions, where 0x80092778 builds `pounds × 10`. Doing the
    /// arithmetic in Money would put the truncations at a different scale and move the answer by up to
    /// a pound, which is exactly enough to flip a `want &gt; price` on a small item.
    ///
    /// ⭐ THE NEED FACTOR IS NOW RESOLVED. An earlier version of this file took N as an input because
    /// its coefficients were "per-product data fields nobody has decoded". They are the record bytes in
    /// <see cref="ShopProduct"/>, read through vtable slots 55/56 and two getters, and
    /// <see cref="NeedFactor"/> computes it.</summary>
    public static class GuestSpending
    {
        /// <summary>The want's base is the unit cost times this over 100 (0x8008E680..0x8008E6B8: ×125,
        /// then the signed /100). behaviour.md §2.5 called it "a cost-derived number ×1.25" and
        /// economy.md §0 item 3 "corrected" that to "it is the unit cost"; both are right about their
        /// own half, because 0x800B6CBC returns the unit cost and the CALLER scales it. economy.md §5's
        /// `want = unitCost × N × …` has lost the ×1.25.</summary>
        public const int WantBaseNumerator = 125;

        /// <summary>The unit-cost factor's constant term: 75 (0x800B6D00).</summary>
        public const int UnitCostBase = 75;

        /// <summary>Rubbish per purchase is this plus rand(<see cref="LitterRollMax"/>): 0x8010322C, read
        /// as 30 in the image (0x1E).</summary>
        public const int LitterBase = 30;
        /// <summary>The purchase's rand(25) (0x8008E894).</summary>
        public const int LitterRollMax = 25;

        /// <summary>Kind 7 adds the second slider over this to need B, and every food kind subtracts
        /// the second slider over this from the quality before scaling the happiness gain (the
        /// 0x88888889 signed divide at 0x8008E8EC and 0x8008E9AC).</summary>
        public const int SecondSliderDivisor = 15;

        /// <summary>The global at 0x80103240, read as 100 in the image. The sideshow want multiplies by
        /// (this + 100) / 100, so as shipped it doubles the expected prize. What the global is FOR is not
        /// established; it is named by its role here rather than given a meaning.</summary>
        public const int SideShowValueGlobal = 100;

        /// <summary>What a sideshow with a ZERO win chance is valued at before the multipliers: the
        /// routine writes 100 in place of `chance × prize / 100` (0x8008EEC8 → 0x8008EF0C). See
        /// <see cref="SideShowWant"/>.</summary>
        public const int ZeroChanceValue = 100;

        /// <summary>The sideshow's happiness step, ±this by the sign of what was paid: 0x8010320C, read
        /// as 10 in the image.</summary>
        public const int SideShowHappinessStep = 10;

        /// <summary>A kind-2 purchase writes this visitor type (0x8008EB4C: `addiu a1, zero, 8`).
        /// ⚠ ONE PAST THE TYPE TABLE; see VisitorTables.CostumePreference.</summary>
        public const int CostumeVisitorType = 8;

        /// <summary>The message-box events the purchase routines post (0x800139B4 on the object at
        /// 0x8010265C; behaviour.md §2.5 calls them "sounds", transport.md §2.1 identifies the object as
        /// the message-box state machine). What the box does with each id is not traced.</summary>
        public const int EventBalloon = 5, EventCostume = 6, EventGift = 7, EventSideShowVerdict = 0xD, EventSideShowRating = 0x12;

        /// <summary>The verdict event by product kind, table 0x800E3B34 (0x8008ED74). Kinds 8 and up
        /// post nothing.</summary>
        public static readonly int[] VerdictEventByKind = { 0x9, 0xC, 0xA, 0xA, 0x9, 0xB, 0xA, 0x9 };

        /// <summary>The satisfaction event by product kind, table 0x800E6D44 (0x800B6FF0). Kinds 8 and
        /// up post nothing.</summary>
        public static readonly int[] RatingEventByKind = { 0xE, 0x11, 0xF, 0xF, 0xE, 0x10, 0xF, 0xE };

        /// <summary>The satisfaction recorders subtract this from the clamped 5×Δhappiness before
        /// dividing by four (0x800B6FB4 / 0x800B7950: `addiu a2, a1, -0x32`).</summary>
        public const int RatingOffset = 50;

        /// <summary>What one unit costs the shop (0x800B6CBC, READ):
        /// `rec+0x2E × (75 + quality/4 − second/4) / 100`.
        ///
        /// ⚠ THE LAST DIVIDE IS UNSIGNED (`multu` then `srl 5` at 0x800B6D20..0x800B6D34) while the
        /// second slider's /4 is signed (0x800B6CFC..0x800B6D08 adds 3 first for a negative word). With
        /// the sliders in their panel range the product is never negative and the two agree; reproduced
        /// as written so that if a saved game carries a word outside that range the port does what the
        /// console does rather than something tidier.</summary>
        /// <param name="recordUnitCost">rec+0x2E, u16.</param>
        /// <param name="quality">shop+0x8A, u16 (the panel's 0x800B7060 getter).</param>
        /// <param name="second">shop+0x7C, a signed word (0x800B70A8).</param>
        public static int UnitCost(int recordUnitCost, int quality, int second)
        {
            int factor = UnitCostBase + ((quality & 0xFFFF) >> 2) - second / 4;
            uint product = (uint)(recordUnitCost * factor);
            return (int)(product / 100);
        }

        /// <summary>The want's base: the unit cost × 125 / 100, signed truncation.</summary>
        public static int WantBase(int unitCost) => unitCost * WantBaseNumerator / 100;

        /// <summary>N (0x8008E6FC..0x8008E7B0, READ):
        /// `100 + needB × NeedBValue/100 + needA × NeedAValue/100 − nausea × NauseaValue/100 + (100 − happiness) × HappinessValue/100`,
        /// each term truncated on its own before the sum.
        ///
        /// ⭐ MISERY RAISES THE WANT. The happiness term is (100 − happiness), so an unhappy guest values
        /// a product MORE through N and then LESS through the (happiness + 100) multiplier in
        /// <see cref="Want"/>. The two pull against each other and neither is a bug to tidy.</summary>
        public static int NeedFactor(int needA, int needB, int nausea, int happiness, ShopProduct p)
            => 100
             + needB * p.NeedBValue / 100
             + needA * p.NeedAValue / 100
             - nausea * p.NauseaValue / 100
             + (100 - happiness) * p.HappinessValue / 100;

        /// <summary>What the item is worth to the guest, in pounds (0x8008E7B4..0x8008E810, READ):
        /// `(base × N / 100) × (happiness + 100) / 100`.
        ///
        /// ⭐ THE INTERMEDIATE TRUNCATION IS NOW READ, and it is between the two multiplies: base × N is
        /// divided by 100 (0x8008E7C4..0x8008E7E4) BEFORE the happiness scale is applied. An earlier
        /// version of this function put both divisions last, which agrees on most inputs and differs by
        /// one pound on the rest -- base 50, N 101, happiness 99 gives 99 this way and 100 that way.
        ///
        /// ⭐ HAPPINESS IS A PRICE MULTIPLIER, NOT A GATE. At 0 the guest still pays base × N / 100; at
        /// 100 it pays double. Nothing refuses a sale for misery; the refusal falls out of want ≤ price.</summary>
        public static int Want(int wantBase, int needFactor, int happiness)
            => wantBase * needFactor / 100 * (happiness + 100) / 100;

        /// <summary>Will the guest buy at this price? `price &lt; want` (0x8008E81C, STRICT) and
        /// `money ≥ Money(price)` (0x8008E848, 0x800926B4). A guest does not buy an item worth exactly
        /// what it costs, but does spend its last pound.</summary>
        public static bool WouldBuy(int want, int price, Money money)
            => want > price && money >= Money.FromPounds(price);

        /// <summary>What a game is worth to the guest, in pounds (0x8008EEBC..0x8008EF6C, READ):
        /// `((chance × prize / 100) × (G + 100) / 100) × (happiness + 100) / 100` with G the global at
        /// 0x80103240 -- except that a ZERO chance substitutes <see cref="ZeroChanceValue"/> for the
        /// expected prize.
        ///
        /// ⭐ A GAME NOBODY CAN WIN IS ALWAYS WORTH PLAYING. The Fortune Teller record has chance 0 and
        /// prize 0; through the substitution it is valued at 200..400 against a £10 price, so every guest
        /// that can pay, plays. A port that reads chance 0 as "worth nothing" turns that stall into
        /// scenery. The Arcade (30% of £25 at price £10) is worth 14..28 -- also always played.</summary>
        public static int SideShowWant(int chance, int prize, int happiness)
        {
            int expected = chance == 0 ? ZeroChanceValue : chance * prize / 100;
            return expected * (SideShowValueGlobal + 100) / 100 * (happiness + 100) / 100;
        }

        /// <summary>Will the guest play? `want ≥ price` (0x8008EF70: skips on `want &lt; price`) and
        /// `money ≥ Money(price)` (0x8008EF9C).
        ///
        /// ⚠ NOT THE SHOP'S TEST. A shop needs the item to be worth MORE than it costs; a sideshow is
        /// played at exactly its worth. The asymmetry is in the original (slt with the operands the other
        /// way round) and a shared helper would erase it.</summary>
        public static bool WouldPlay(int want, int price, Money money)
            => want >= price && money >= Money.FromPounds(price);

        /// <summary>`(want − price) / 2`, toward zero (0x8008ED50..0x8008ED60: adds the sign bit before
        /// the shift).</summary>
        public static int VerdictValue(int want, int price) => (want - price) / 2;

        /// <summary>The verdict, classified. See <see cref="ValueVerdict"/> for which bubble each shows.</summary>
        public static ValueVerdict Verdict(int want, int price)
        {
            int v = VerdictValue(want, price);
            if (v < 0) return ValueVerdict.RippedOff;
            return v < 2 ? ValueVerdict.Fair : ValueVerdict.Bargain;
        }

        /// <summary>The bubble a verdict shows (0x80093E20), or 0 for none.</summary>
        public static int VerdictBubble(ValueVerdict v) => v switch
        {
            ValueVerdict.RippedOff => BubbleRippedOff,
            ValueVerdict.Bargain => BubbleBargain,
            _ => 0,
        };

        public const int BubbleRippedOff = 0x36;
        public const int BubbleBargain = 0x38;

        /// <summary>What the satisfaction recorders (0x800B6FA0 for a shop, 0x800B7940 for a sideshow)
        /// post with their event: `(max(0, fiveTimesDelta) − 50) / 4`, toward zero. The clamp comes FIRST
        /// (bgez at entry), so a guest that got sadder rates the stall the same as one unmoved: −12.</summary>
        public static int SatisfactionRating(int fiveTimesDelta)
            => (Math.Max(0, fiveTimesDelta) - RatingOffset) / 4;

        /// <summary>What the recorders ADD to the stall's running total (+0x80): the delta clamped at
        /// zero. The visit counter beside it goes up by one regardless.</summary>
        public static int SatisfactionAmount(int fiveTimesDelta) => Math.Max(0, fiveTimesDelta);

        /// <summary>The happiness a purchase pays. Food kinds (0, 1, 4, 5, 7):
        /// `HappinessValue × (quality − second/15) / 100` (0x8008E98C..0x8008E9FC); the others (2, 3, 6):
        /// `HappinessValue × quality / 100` (0x8008EB98..0x8008EBD8). Signed truncation both times.
        ///
        /// ⚠ CAN BE NEGATIVE ON PAPER: quality 0 against a second slider over 15 gives a negative
        /// factor. In the panel's range (both sliders top out at 100: 0x8007A780, 0x8007A7C8) the product
        /// is at most −6 × HappinessValue, which /100 truncates to 0 for every catalogued value (≤ 15),
        /// so no shipped shop pays negative happiness; the formula is not clamped here because the
        /// original does not clamp it.</summary>
        public static int HappinessGain(int happinessValue, int quality, int second, bool food)
            => food ? happinessValue * (quality - second / SecondSliderDivisor) / 100
                    : happinessValue * quality / 100;

        /// <summary>Litter dropped by one purchase: 30 + rand(25), so 30..54.</summary>
        /// <param name="roll">The rand(25) result, 0..24. Passed in so a test is exact.</param>
        public static int LitterDropped(int roll) => LitterBase + roll;

        /// <summary>Will the guest pay to get in?
        ///
        /// READ (economy.md §5, behaviour.md §2.6): pays iff `money &gt; fee` and its opinion of the park
        /// is at least -1. Note the gate is STRICTLY greater -- a guest holding exactly the fee does not
        /// come in -- where the shop test is &gt;=. That asymmetry is in the original.</summary>
        public static bool WouldPayEntry(Money money, Money fee, int parkOpinion)
            => money > fee && parkOpinion >= -1;

        /// <summary>What a guest starts with: £200 + rand(300), i.e. £200..£499 (ctor 0x8008C5FC).</summary>
        /// <param name="roll">The rand(300) result, 0..299.</param>
        public static Money StartingMoney(int roll) => Money.FromPounds(200 + roll);

        /// <summary>The balance below which a guest gives up and leaves: £10 (Idle's leave check).</summary>
        public static readonly Money LeaveBelow = Money.FromPounds(10);

        /// <summary>⚠ NOTHING REFILLS A GUEST, EXCEPT A SIDESHOW WIN. The only things that touch its
        /// money are the constructor, the gate, a type-4 purchase and a type-5 game. The first three are
        /// drains; the game deducts `price − prize` and a prize worth more than the price is a negative
        /// deduction (economy.md §4.3, <see cref="SideShowPlay"/>), so a lucky guest leaves a stall richer
        /// than it arrived. Apart from that, a guest's whole visit is funded by what it walked in with,
        /// which is why throughput matters more than price: raising prices does not raise the total a
        /// guest can ever spend, it only spends it faster. An earlier version of this file carried a
        /// `GuestsHaveNoIncome = true` constant; the prize makes that false, so it is gone.</summary>
        public static readonly Money NoIncomeFrom = Money.Zero;
    }
}
