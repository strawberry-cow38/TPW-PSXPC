using System;
using System.Collections.Generic;
using System.Linq;

namespace TPW.Sim;

/// <summary>Live pool contents for 0x8005B830 and 0x80087884. Reuse the host's existing
/// statistics snapshots, including objects on the cursor and status-zero attractions.
/// These are live inputs, never ParkStatistics' staggered advisor cache.</summary>
public interface IParkScoreWorld
{
    /// <summary>READ: people pool 0x80103884; all guests, including those at the gate.
    /// The monthly history also reads happiness and arrival day from this population.</summary>
    IEnumerable<Visitor> Visitors { get; }
    /// <summary>READ: all seven attraction pools, iterator 0x8006DCA0; the rating's
    /// ride-only iterator 0x8006DCCC(...,1) selects types 1,3,6,7 without a status test.</summary>
    IEnumerable<StatisticAttraction> Attractions { get; }
    /// <summary>READ: full staff pools at 0x80103864..74. Include held and striking staff;
    /// unlike statistic ids 6..10, this calculation does not subtract held staff.</summary>
    IEnumerable<StatisticStaff> Staff { get; }
    /// <summary>GUESS-low, retained SOURCE DISAGREEMENT: economy.md §3 step 6 says
    /// defPrice(type, level). Supply that whole-pound price, not a ticket, refund, or net asset
    /// value. 0x800878E4..F8 actually passes A+0x6B (definition selector, 0x80063328),
    /// and 0x8006AD58 selects the base price. Keep the findings' LEVEL contract until
    /// adjudicated; see findings/rating.md §0. No price table is invented here.</summary>
    int ValuePricePounds(AttractionType type, int level);
}

/// <summary>READ: save order and BANK bases from 0x80087DEC / economy.md §1.2.</summary>
public enum BankHistoryRow
{
    Balance = 0, Income = 1, Entrance = 2, Shop = 3, SideShow = 4,
    Spending = 5, Wages = 6, ParkValue = 7,
}

/// <summary>Port API labels (not binary ids) for the three extra income ledgers.
/// READ: entrance 0x80087258; shop/sideshow 0x80086A5C; other income 0x80086980.</summary>
public enum ScoreIncome { Other, Entrance, Shop, SideShow }

/// <summary>Owns BANK's eight monthly rings and historical totals, and composes the existing
/// ParkHistory owner for McAi's five byte rings and annual rating. No money is moved here.
/// READ: 0x80066C50 orders bank rollover, visitor history, then calendar-year totals rotation.
/// Host forwards each booked transaction exactly once; see RecordMonthEnd for scheduled charges.
/// GUESS-high: unwritten history cells start at zero in this managed owner. The BANK constructor
/// 0x80086560 zeros every balance cell but only slot zero of the other rings; allocator contents
/// of unwritten cells were not established. Valid monthly sampling does not depend on them.</summary>
public sealed class ParkScore
{
    /// <summary>READ: 0x80086D98..DCC, twelve years of months, shared with McAi.</summary>
    public const int Slots = ParkHistory.Slots;
    readonly int[][] bank = Enumerable.Range(0, 8).Select(_ => new int[Slots]).ToArray();
    public ParkHistory History { get; }
    public ParkScore(ParkHistory history = null) { History = history ?? new ParkHistory(); }

    /// <summary>READ: BANK+0x12BC; independent of McAi+0x18, saved as a full word.</summary>
    public int MonthIndex { get; private set; }
    /// <summary>READ: BANK+0x12C0, replaced each month; deliberately absent from BankSave.</summary>
    public Money LoanRepayments { get; private set; }
    // READ: BANK+12C4..12F0, 0x80086560 / 0x80086D70 / 0x80087570.
    public Money SideshowTakings { get; private set; }
    public Money EntryTakings { get; private set; }
    public Money ShopProfit { get; private set; }
    public Money Wages { get; private set; }
    public Money Spending { get; private set; }
    public Money Income { get; private set; }
    public Money ThisYearIncome { get; private set; }
    public Money LastYearIncome { get; private set; }
    public Money ThisYearSpending { get; private set; }
    public Money LastYearSpending { get; private set; }
    public Money YearlyValue { get; private set; }
    public Money YearlyBalance { get; private set; }
    public int RatingAtNewYear => History.RatingAtNewYear;

    /// <summary>READ: 0x8005B830..BA24. Range 0..100 for valid pool contents.
    /// No happiness, finance, research, availability, status or advisor-state inputs.</summary>
    public static int CalculateRating(IParkScoreWorld world)
    {
        var attractions = world.Attractions.ToArray();
        var staff = world.Staff.ToArray();
        int rides = 0, upgraded = 0;
        foreach (var a in attractions)
            if (a.Type is AttractionType.RollerCoaster or AttractionType.Ride or
                AttractionType.TrackRide or AttractionType.TourRide)
            {
                rides++;
                if (a.UpgradeLevel >= 2) upgraded++; // 0x8005B978, getter 0x8009F5E0
            }
        int rating = Math.Min(world.Visitors.Count(), 100) / 5; // 0x8005B868..88
        rating += Math.Min(rides * 3 / 2, 20);                  // 0x8005B998..B4
        rating += Math.Min(upgraded, 10);                      // 0x8005B9BC..C4
        rating += Math.Min(attractions.Count(a => a.Type == AttractionType.Shop) * 2, 10); // B894..9C
        rating += Math.Min(attractions.Count(a => a.Type == AttractionType.SideShow) * 2, 10); // B8B0..B8
        rating += Math.Min(attractions.Count(a => a.Type == AttractionType.Feature), 10); // B8C8..D0
        foreach (StaffKind kind in Enum.GetValues<StaffKind>())
            rating += Math.Min(staff.Count(s => s.Kind == kind), 4); // 0x8005B8D4..948
        return rating;
    }

    /// <summary>READ: 0x800878FC sums pounds; 0x80087920 converts to Money BEFORE division
    /// by two at 0x80087938 (an odd pound retains a half pound). No cash/debt/path/staff inputs.
    /// ⚠ SOURCE DISAGREEMENT: retain economy.md's level selector, documented on the interface.</summary>
    public static Money CalculateValue(IParkScoreWorld world)
    {
        int pounds = 0;
        foreach (var a in world.Attractions)
            pounds = unchecked(pounds + world.ValuePricePounds(a.Type, a.UpgradeLevel));
        return Money.FromRaw(unchecked(pounds * 10) / 2);
    }

    /// <summary>READ: 0x80087960. Unlike the ordinary history accessors, age zero
    /// computes LIVE park value (0x80087978..84); positive ages read the monthly ring.</summary>
    public Money ReadValue(IParkScoreWorld world, int monthsBack = 0)
        => monthsBack == 0 ? CalculateValue(world) : Read(BankHistoryRow.ParkValue, monthsBack);

    // READ: native Money is a signed WORD (0x80088DFC/DE0), although the shared Money type is long.
    static Money Add(Money a, Money b) => Money.FromRaw(unchecked((int)(a.Raw + b.Raw)));
    void AddTo(BankHistoryRow row, Money amount)
        => bank[(int)row][MonthIndex % Slots] = unchecked(bank[(int)row][MonthIndex % Slots] + (int)amount.Raw);

    /// <summary>READ: 0x80086980 / 86A5C / 87258. Forward a booked receipt once, with its
    /// category; loans and demolition refunds are Other. A shop loss is spending instead.
    /// Host remains responsible for Bank.Receive and its existing debt-reset behavior.</summary>
    public void RecordIncome(Money amount, ScoreIncome source = ScoreIncome.Other)
    {
        if (!Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(source));
        Income = Add(Income, amount);
        ThisYearIncome = Add(ThisYearIncome, amount);
        AddTo(BankHistoryRow.Income, amount);
        switch (source)
        {
            case ScoreIncome.Entrance:
                EntryTakings = Add(EntryTakings, amount); AddTo(BankHistoryRow.Entrance, amount); break;
            case ScoreIncome.Shop:
                ShopProfit = Add(ShopProfit, amount); AddTo(BankHistoryRow.Shop, amount); break;
            case ScoreIncome.SideShow:
                SideshowTakings = Add(SideshowTakings, amount); AddTo(BankHistoryRow.SideShow, amount); break;
        }
    }

    /// <summary>READ: 0x800868B8. Forward actual charged spending only; refused/free-switch
    /// transactions do not enter these books. Do not forward the scheduled charge a second time:
    /// RecordMonthEnd records that charge. No balance mutation is performed here.</summary>
    public void RecordSpending(Money amount)
    {
        Spending = Add(Spending, amount);
        ThisYearSpending = Add(ThisYearSpending, amount);
        AddTo(BankHistoryRow.Spending, amount);
    }

    /// <summary>READ: 0x8008717C. Sacking pay enters all-time wages and spending, but NOT
    /// the wages ring. ⚠ DO NOT FIX: statistic 48 therefore excludes this wage payment.</summary>
    public void RecordSackingPay(Money amount)
    {
        Wages = Add(Wages, amount);
        RecordSpending(amount);
    }

    /// <summary>Call exactly once after ParkFinances produces a MonthEndResult; supply the
    /// balance captured BEFORE its loan/wage charge and the already advanced Calendar.
    /// Return false on ordinary days (no world queries or writes). The host owns edge delivery;
    /// repeated calls on the same edge are an error, like calling MonthRollover.Run twice.
    /// READ: bank balance sampled before charges (0x80086DDC), wages replace the slot (86ECC),
    /// value sampled after charges (86EE0). Yearly bank snapshots use pre-increment bank index
    /// 12,24,... (86F24..78), so the first is the THIRTEENTH rollover. Calendar's annual rating
    /// and totals instead change on the twelfth. ⚠ DO NOT FIX this one-month difference.</summary>
    public bool RecordMonthEnd(Calendar calendar, Money balanceBeforeCharges, MonthEndResult result,
                               IParkScoreWorld world)
    {
        if (!calendar.MonthRolledOver) return false;
        int slot = MonthIndex % Slots;
        bank[(int)BankHistoryRow.Balance][slot] = unchecked((int)balanceBeforeCharges.Raw);
        LoanRepayments = Money.FromRaw(unchecked((int)result.LoanPayments.Raw));
        Wages = Add(Wages, result.Wages);
        bank[(int)BankHistoryRow.Wages][slot] = unchecked((int)result.Wages.Raw);
        RecordSpending(result.TotalCharged);
        Money value = CalculateValue(world);
        bank[(int)BankHistoryRow.ParkValue][slot] = unchecked((int)value.Raw);
        if (MonthIndex != 0 && MonthIndex % 12 == 0)
        {
            YearlyValue = value;
            YearlyBalance = Money.FromRaw(unchecked((int)result.ClosingBalance.Raw));
        }
        MonthIndex++;
        // READ 0x80086FC8..8716C: six running rows clear; balance/value are NOT cleared.
        for (int row = (int)BankHistoryRow.Income; row <= (int)BankHistoryRow.Wages; row++)
            bank[row][MonthIndex % Slots] = 0;
        History.RecordMonth(calendar.TotalMonths - 1, calendar.Month, calendar.Year,
            calendar.TotalDays, world.Visitors, CalculateRating(world));
        if (calendar.YearRolledOver) // 0x80066E00..0C -> 0x80087570, after December charges
        {
            LastYearIncome = ThisYearIncome;
            LastYearSpending = ThisYearSpending;
            ThisYearIncome = Money.Zero;
            ThisYearSpending = Money.Zero;
        }
        return true;
    }

    /// <summary>READ: 0x80087610 uses signed n&lt;index before changing zero to one. For
    /// nonnegative inputs it agrees with ParkHistory.SlotFor. Negative ages are host errors.
    /// This reads ring samples; use ReadValue for the original value getter's live age-zero case.</summary>
    public Money Read(BankHistoryRow row, int monthsBack)
    {
        if (monthsBack < 0) throw new ArgumentOutOfRangeException(nameof(monthsBack));
        int slot = ParkHistory.SlotFor(MonthIndex, monthsBack);
        return slot < 0 ? Money.Zero : Money.FromRaw(bank[(int)row][slot]);
    }

    /// <summary>Inspection of the owned physical ring, including the current accumulating slot.
    /// This is a port API, not the age-based original UI accessor. Returns an independent copy.</summary>
    public int[] CopyBankRing(BankHistoryRow row) => (int[])bank[(int)row].Clone();

    /// <summary>Fill ONLY this owner's fields in an existing BankSave (0x80087DEC).
    /// Host supplies balance and loans. Unknown padding is retained; totals lose tenths.</summary>
    public void CaptureBank(BankSave save)
    {
        for (int row = 0; row < bank.Length; row++)
            ScoreHistoryCodec.PackMoney(bank[row], MonthIndex, save.Bytes.AsSpan(row * ScoreHistoryCodec.MoneyRecordSize));
        save.MonthIndex = MonthIndex;
        save.LastYearIncomePounds = (int)LastYearIncome.Pounds;
        save.LastYearSpendPounds = (int)LastYearSpending.Pounds;
        save.ThisYearIncomePounds = (int)ThisYearIncome.Pounds;
        save.ThisYearSpendPounds = (int)ThisYearSpending.Pounds;
        save.SideshowTakingsPounds = (int)SideshowTakings.Pounds;
        save.EntryTakingsPounds = (int)EntryTakings.Pounds;
        save.ShopProfitPounds = (int)ShopProfit.Pounds;
        save.WagesPounds = (int)Wages.Pounds;
        save.SpendPounds = (int)Spending.Pounds;
        save.IncomePounds = (int)Income.Pounds;
        save.YearlyValuePounds = (int)YearlyValue.Pounds;
        save.YearlyBalancePounds = (int)YearlyBalance.Pounds;
    }

    /// <summary>READ: 0x80088A0C. Restore compressed histories and totals; no Bank/LoanBook
    /// changes. LoanRepayments is unsaved and left as-is, as in the original reader.</summary>
    public void RestoreBank(BankSave save)
    {
        if (save.MonthIndex < 0) throw new ArgumentOutOfRangeException(nameof(save));
        MonthIndex = save.MonthIndex;
        for (int row = 0; row < bank.Length; row++)
            ScoreHistoryCodec.UnpackMoney(save.Bytes.AsSpan(row * ScoreHistoryCodec.MoneyRecordSize), MonthIndex, bank[row]);
        LastYearIncome = FromSavedPounds(save.LastYearIncomePounds);
        LastYearSpending = FromSavedPounds(save.LastYearSpendPounds);
        ThisYearIncome = FromSavedPounds(save.ThisYearIncomePounds);
        ThisYearSpending = FromSavedPounds(save.ThisYearSpendPounds);
        SideshowTakings = FromSavedPounds(save.SideshowTakingsPounds);
        EntryTakings = FromSavedPounds(save.EntryTakingsPounds);
        ShopProfit = FromSavedPounds(save.ShopProfitPounds);
        Wages = FromSavedPounds(save.WagesPounds);
        Spending = FromSavedPounds(save.SpendPounds);
        Income = FromSavedPounds(save.IncomePounds);
        YearlyValue = FromSavedPounds(save.YearlyValuePounds);
        YearlyBalance = FromSavedPounds(save.YearlyBalancePounds);
    }

    // READ: 0x80088E50 reconstructs a signed 32-bit Money word.
    static Money FromSavedPounds(int pounds) => Money.FromRaw(unchecked(pounds * 10));
}
