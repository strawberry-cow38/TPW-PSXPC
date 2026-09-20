namespace TPW.Sim
{
    /// <summary>What a guest thinks of the price it just paid (the "value verdict").
    ///
    /// ⚠ THE REPORT'S WORDING LEAVES THE PRECEDENCE OPEN. economy.md §5 and behaviour.md §2.5 give
    /// "bubble 0x38 if verdict &lt; 2, bubble 0x36 if negative", and those two overlap -- every negative
    /// verdict is also below 2. Read here as most-specific-first (negative wins), which is the only
    /// ordering that makes both clauses reachable. If a live reading ever shows the good-value bubble
    /// on a rip-off, this ordering is the thing to flip.</summary>
    public enum ValueVerdict
    {
        /// <summary>Paid well over what it was worth: bubble 0x36.</summary>
        RippedOff,
        /// <summary>Close to what it was worth: bubble 0x38.</summary>
        Fair,
        /// <summary>Comfortably worth it. No bubble.</summary>
        Bargain,
    }

    /// <summary>The purchase rules, exactly as far as they are READ and no further.
    ///
    /// Sources: economy.md §5 (the decision and the drains), behaviour.md §2.5 (the want formula).
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
    /// ⚠ UNITS. economy.md §5 states the affordability test as `money >= price x 10` because it is
    /// counting raw Money units against a price in POUNDS; behaviour.md §2.5 states the same test as
    /// `money >= price` having already converted. They are the same rule. Everything here takes Money
    /// on both sides so the x10 cannot be applied twice or forgotten -- which is exactly the slip those
    /// two phrasings invite.</summary>
    public static class GuestSpending
    {
        /// <summary>What the item is worth to this guest.
        ///
        /// `want = base * N * (happiness + 100) / 100 / 100` (behaviour.md §2.5, formula READ at
        /// 0x8008E814). Integer arithmetic throughout.
        ///
        /// ⚠ DO NOT FIX: this keeps the report's two divisions last. The subsequent READ at
        /// 0x8008E7B4..810 establishes a division BETWEEN the N and happiness multiplies, which can
        /// change the result. findings/visitor-rest.md records the disagreement; the original report's
        /// formula remains the contract here rather than being silently replaced by that reading.
        ///
        /// ⭐ HAPPINESS IS A PRICE MULTIPLIER, NOT A GATE. At happiness 0 a guest still pays base*N/100;
        /// at 100 it pays double that. Nothing here refuses a sale because the guest is miserable -- a sad
        /// guest simply values things less, and the refusal falls out of want &lt;= price on its own.</summary>
        /// <param name="unitBase">The shop's cost-derived base (0x800B6CBC).</param>
        /// <param name="needFactor">N, the need-weighted percentage. See the class note: an input.</param>
        /// <param name="happiness">0..100.</param>
        public static Money Want(Money unitBase, int needFactor, int happiness)
        {
            long v = unitBase.Raw * needFactor * (happiness + 100);
            v /= 100;
            v /= 100;
            return Money.FromRaw(v);
        }

        /// <summary>Will the guest buy at this price? Both halves must hold: it must be worth more than
        /// it costs AND be affordable. STRICTLY greater on the want side -- a guest does not buy an item
        /// worth exactly what it costs.</summary>
        public static bool WouldBuy(Money want, Money price, Money money)
            => want > price && money >= price;

        /// <summary>`(want - price) / 2`, then the bubble. See <see cref="ValueVerdict"/> on ordering.</summary>
        public static ValueVerdict Verdict(Money want, Money price)
        {
            long v = (want.Raw - price.Raw) / 2;
            if (v < 0) return ValueVerdict.RippedOff;
            return v < Money.FromPounds(2).Raw ? ValueVerdict.Fair : ValueVerdict.Bargain;
        }

        /// <summary>Litter dropped by one purchase: 30 + rand(25), so 30..54 (0x8010322C = 30).</summary>
        /// <param name="roll">The rand(25) result, 0..24. Passed in so a test is exact.</param>
        public static int LitterDropped(int roll) => 30 + roll;

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

        /// <summary>⚠ NOTHING REFILLS A GUEST. The only things that touch its money are the constructor,
        /// the gate, a type-4 purchase and a type-5 game -- all drains. A guest's whole visit is funded
        /// by what it walked in with, which is why throughput matters more than price: raising prices
        /// does not raise the total a guest can ever spend, it only spends it faster and pushes the
        /// guest below the leave threshold sooner.</summary>
        public const bool GuestsHaveNoIncome = true;
    }
}
