using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests;

public class ParkScoreTests
{
    sealed class Dice : IRandomSource { public int Next(int n) => 0; }
    sealed class World : IParkScoreWorld
    {
        public List<Visitor> Guests = new();
        public List<StatisticAttraction> Built = new();
        public List<StatisticStaff> Workers = new();
        public List<(AttractionType Type, int Level)> PricesRead = new();
        public int Price = 101;
        public IEnumerable<Visitor> Visitors => Guests;
        public IEnumerable<StatisticAttraction> Attractions => Built;
        public IEnumerable<StatisticStaff> Staff => Workers;
        public int ValuePricePounds(AttractionType type, int level) { PricesRead.Add((type, level)); return Price; }
        public void AddGuests(int count)
        {
            for (int i = 0; i < count; i++) Guests.Add(Visitor.Spawn(new Dice(), 0));
        }
        public void Add(AttractionType type, int count, int level = 0)
        {
            for (int i = 0; i < count; i++) Built.Add(new(type, 17, AttractionStatus.JustPlaced, UpgradeLevel: level));
        }
    }
    static void End(ParkScore score, Calendar calendar, World world, long before = 1234, long wages = 0, long loans = 0)
    {
        do { calendar.AdvanceDay(); } while (!calendar.MonthRolledOver);
        Assert.True(score.RecordMonthEnd(calendar, Money.FromRaw(before),
            new(Money.FromRaw(loans), Money.FromRaw(wages), Money.FromRaw(before-wages-loans), DebtNotice.None), world));
    }
    static int[] Ints(JsonElement x) => x.EnumerateArray().Select(e => e.GetInt32()).ToArray();
    static byte[] Bytes(JsonElement x) => Ints(x).Select(i => (byte)i).ToArray();
    static JsonDocument Audit() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rating-audit.json")));

    // REJECTS a dead/empty oracle harness and a shared handwritten rating oracle.
    [Fact]
    public void PopulatedBinaryRatingControlsAgree()
    {
        using var audit = Audit();
        Assert.All(audit.RootElement.GetProperty("controls").EnumerateObject(), x => Assert.True(x.Value.GetBoolean(), x.Name));
        var cases = audit.RootElement.GetProperty("rating_cases").EnumerateArray().ToArray();
        Assert.Equal(3, cases.Length);
        foreach (var c in cases)
        {
            var w = new World();
            w.AddGuests(c.GetProperty("visitors").GetInt32());
            w.Add(AttractionType.Shop,c.GetProperty("shops").GetInt32());
            w.Add(AttractionType.SideShow,c.GetProperty("shows").GetInt32());
            w.Add(AttractionType.Feature,c.GetProperty("features").GetInt32());
            foreach (int level in Ints(c.GetProperty("levels"))) w.Add(AttractionType.Ride,1,level);
            var kinds = new[] {StaffKind.Cleaner,StaffKind.Mechanic,StaffKind.Entertainer,StaffKind.Guard,StaffKind.Researcher};
            var counts = Ints(c.GetProperty("staff"));
            for (int i=0;i<kinds.Length;i++) for(int j=0;j<counts[i];j++) w.Workers.Add(new(kinds[i],0));
            Assert.Equal(c.GetProperty("rating").GetInt32(), ParkScore.CalculateRating(w));
            Assert.Empty(w.PricesRead);
        }
    }

    // REJECTS rounding visitors up, omitting the cap, and visitor happiness affecting the score.
    [Theory]
    [InlineData(0,0)] [InlineData(4,0)] [InlineData(5,1)] [InlineData(99,19)] [InlineData(100,20)] [InlineData(105,20)]
    public void VisitorTerm(int count,int expected)
    {
        var w=new World(); w.AddGuests(count);
        Assert.Equal(expected,ParkScore.CalculateRating(w));
        w.Guests.ForEach(g=>g.Happiness=100);
        Assert.Equal(expected,ParkScore.CalculateRating(w));
    }

    // REJECTS excluding any ride class, excluding JustPlaced, per-ride rounding, and upgrade threshold 1 or 3.
    [Theory]
    [InlineData(AttractionType.Ride)] [InlineData(AttractionType.RollerCoaster)]
    [InlineData(AttractionType.TrackRide)] [InlineData(AttractionType.TourRide)]
    public void RideTerms(AttractionType type)
    {
        var w=new World(); w.Add(type,1,1); Assert.Equal(1,ParkScore.CalculateRating(w));
        w.Add(type,1,2); Assert.Equal(4,ParkScore.CalculateRating(w));
        w.Add(type,1,3); Assert.Equal(6,ParkScore.CalculateRating(w));
        w.Add(type,20,2); Assert.Equal(30,ParkScore.CalculateRating(w));
    }

    // REJECTS mixing shop/sideshow/feature weights, upgrade credit for non-rides, and removing any cap.
    [Theory]
    [InlineData(AttractionType.Shop,2)] [InlineData(AttractionType.SideShow,2)] [InlineData(AttractionType.Feature,1)]
    public void NonRideTerms(AttractionType type,int weight)
    {
        var w=new World();w.Add(type,1,3); Assert.Equal(weight,ParkScore.CalculateRating(w));
        w.Add(type,20,3);Assert.Equal(10,ParkScore.CalculateRating(w));
    }

    // REJECTS sharing a staff cap across classes, filtering by patrol/grade, and omitting a class.
    [Theory]
    [InlineData(StaffKind.Mechanic)] [InlineData(StaffKind.Cleaner)] [InlineData(StaffKind.Entertainer)]
    [InlineData(StaffKind.Guard)] [InlineData(StaffKind.Researcher)]
    public void StaffTerms(StaffKind kind)
    {
        var w=new World();w.Workers.Add(new(kind,0));Assert.Equal(1,ParkScore.CalculateRating(w));
        for(int i=0;i<6;i++)w.Workers.Add(new(kind,4,true));
        Assert.Equal(4,ParkScore.CalculateRating(w));
    }

    // REJECTS silently resolving the source disagreement, whole-pound halving, status filters, and excluding a class.
    [Fact]
    public void ValueRetainsFindingsLevelContractAndHalfPounds()
    {
        var w=new World(); Assert.Equal(0,ParkScore.CalculateValue(w).Raw);
        foreach(var type in Attractions.All)w.Add(type,1,2);
        Assert.Equal(3535,ParkScore.CalculateValue(w).Raw); // 7*£101 /2
        Assert.Equal(Attractions.All.Select(t=>(t,2)),w.PricesRead);
        Assert.All(w.Built,a=>Assert.NotEqual(a.DefinitionIndex,a.UpgradeLevel));
        w.Price=201;Assert.Equal(7035,ParkScore.CalculateValue(w).Raw);
    }

    // REJECTS treating value age zero as last month, eagerly querying prices for historical reads,
    // and using the current value for a positive age whose history does not exist.
    [Fact]
    public void ValueGetterComputesOnlyAtAgeZero()
    {
        var s=new ParkScore();var w=new World();w.Add(AttractionType.Shop,1);
        Assert.Equal(505,s.ReadValue(w).Raw);Assert.Single(w.PricesRead);
        Assert.Equal(0,s.ReadValue(w,1).Raw);Assert.Single(w.PricesRead);
        var c=new Calendar();End(s,c,w);End(s,c,w);w.PricesRead.Clear();w.Price=301;
        Assert.Equal(505,s.ReadValue(w,1).Raw);Assert.Empty(w.PricesRead);
        Assert.Equal(1505,s.ReadValue(w).Raw);Assert.Single(w.PricesRead);
    }

    // REJECTS wide-integer arithmetic in native Money sums, price conversion, and monthly accumulation.
    [Fact]
    public void NativeMoneyWordsWrap()
    {
        var s=new ParkScore();s.RecordIncome(Money.FromRaw(int.MaxValue));s.RecordIncome(Money.FromRaw(1));
        Assert.Equal(int.MinValue,s.Income.Raw);Assert.Equal(int.MinValue,s.ThisYearIncome.Raw);
        Assert.Equal(int.MinValue,s.CopyBankRing(BankHistoryRow.Income)[0]);
        s.RecordSpending(Money.FromRaw(int.MaxValue));s.RecordSpending(Money.FromRaw(1));
        Assert.Equal(int.MinValue,s.Spending.Raw);Assert.Equal(int.MinValue,s.ThisYearSpending.Raw);
        var w=new World {Price=int.MaxValue};w.Add(AttractionType.Ride,1);
        Assert.Equal(-5,ParkScore.CalculateValue(w).Raw);
    }

    // REJECTS dead event hooks, duplicate generic income, wrong typed ledgers, and sacking in monthly wages.
    [Fact]
    public void TransactionsAccumulateInDistinctBooks()
    {
        var s=new ParkScore();
        s.RecordIncome(Money.FromRaw(11),ScoreIncome.Other);
        s.RecordIncome(Money.FromRaw(23),ScoreIncome.Entrance);
        s.RecordIncome(Money.FromRaw(37),ScoreIncome.Shop);
        s.RecordIncome(Money.FromRaw(41),ScoreIncome.SideShow);
        s.RecordSpending(Money.FromRaw(53));s.RecordSackingPay(Money.FromRaw(67));
        Assert.Equal(112,s.Income.Raw);Assert.Equal(112,s.ThisYearIncome.Raw);
        Assert.Equal(23,s.EntryTakings.Raw);Assert.Equal(37,s.ShopProfit.Raw);Assert.Equal(41,s.SideshowTakings.Raw);
        Assert.Equal(120,s.Spending.Raw);Assert.Equal(120,s.ThisYearSpending.Raw);Assert.Equal(67,s.Wages.Raw);
        Assert.Equal(new[]{0,112,23,37,41,120,0,0},Enum.GetValues<BankHistoryRow>().Select(r=>s.CopyBankRing(r)[0]));
        var detached=s.CopyBankRing(BankHistoryRow.Income);detached[0]=999;Assert.Equal(112,s.CopyBankRing(BankHistoryRow.Income)[0]);
        Assert.Throws<ArgumentOutOfRangeException>(()=>s.RecordIncome(Money.FromRaw(99),(ScoreIncome)99));
        Assert.Equal(112,s.Income.Raw);
    }

    // REJECTS daily sampling, post-charge balance samples, unbooked loans/wages, and a second calendar-history owner.
    [Fact]
    public void MonthEdgeHasPreChargeBalanceAndPostChargeValue()
    {
        var h=new ParkHistory();var s=new ParkScore(h);var c=new Calendar();var w=new World();
        w.Add(AttractionType.Ride,1,2);w.AddGuests(5);w.Guests.ForEach(g=>g.Happiness=77);
        c.AdvanceDay();Assert.False(s.RecordMonthEnd(c,Money.FromRaw(99),default,null));
        Assert.Equal(0,s.MonthIndex);Assert.Empty(w.PricesRead);
        s.RecordIncome(Money.FromRaw(51));End(s,c,w,1000,70,30);
        Assert.Same(h,s.History);Assert.Equal(1,s.MonthIndex);Assert.Equal(100,s.Spending.Raw);
        Assert.Equal(70,s.Wages.Raw);Assert.Equal(30,s.LoanRepayments.Raw);
        Assert.Equal(1000,s.Read(BankHistoryRow.Balance,0).Raw);
        Assert.Equal(505,s.Read(BankHistoryRow.ParkValue,0).Raw);
        Assert.Equal(70,s.Read(BankHistoryRow.Wages,0).Raw);
        Assert.Equal(0,s.Read(BankHistoryRow.Wages,1).Raw); // raw guard before 0->1
        Assert.Equal(77,h.Read(HistoryRow.Happiness,c.TotalMonths,0));
        Assert.Equal(3,h.Read(HistoryRow.Overall,c.TotalMonths,0));
        Assert.Equal(0,s.RatingAtNewYear);
        End(s,c,w,900,12,8);Assert.Equal(12,s.Read(BankHistoryRow.Wages,1).Raw);
        Assert.Equal(0,s.Read(BankHistoryRow.Wages,2).Raw);
        Assert.Throws<ArgumentOutOfRangeException>(()=>s.Read(BankHistoryRow.Income,-1));
    }

    // REJECTS January rating as an annual mean, aligning bank snapshots with January, and rotating before December costs.
    [Fact]
    public void AnnualRatingAndBankSnapshotsHaveDifferentEdges()
    {
        var s=new ParkScore();var c=new Calendar();var w=new World();
        s.RecordIncome(Money.FromRaw(199));
        for(int i=0;i<11;i++)End(s,c,w,500,10,2);
        Assert.Equal(0,s.LastYearIncome.Raw);Assert.Equal(0,s.YearlyBalance.Raw);
        w.AddGuests(50);End(s,c,w,600,10,2);
        Assert.Equal(10,s.RatingAtNewYear);Assert.Equal(0,s.YearlyValue.Raw);Assert.Equal(0,s.YearlyBalance.Raw);
        Assert.Equal(199,s.LastYearIncome.Raw);Assert.Equal(144,s.LastYearSpending.Raw);
        Assert.Equal(0,s.ThisYearIncome.Raw);Assert.Equal(0,s.ThisYearSpending.Raw);
        w.Add(AttractionType.Shop,1);s.RecordIncome(Money.FromRaw(33));End(s,c,w,700,10,2);
        Assert.Equal(505,s.YearlyValue.Raw);Assert.Equal(688,s.YearlyBalance.Raw);
        Assert.Equal(10,s.RatingAtNewYear);Assert.Equal(33,s.ThisYearIncome.Raw);
        Assert.Equal(12,s.ThisYearSpending.Raw);
        for(int i=0;i<11;i++)End(s,c,w,800);
        Assert.Equal(12,s.RatingAtNewYear);Assert.Equal(33,s.LastYearIncome.Raw);
        Assert.Equal(688,s.YearlyBalance.Raw);
        End(s,c,w,900);Assert.Equal(900,s.YearlyBalance.Raw);
    }

    // REJECTS a 12-month window, clearing balance/value on advance, retaining running rows, and age clamping at 144.
    [Fact]
    public void RingsWrapAndOnlySixRunningRowsClear()
    {
        var s=new ParkScore();var c=new Calendar();var w=new World();w.Add(AttractionType.Shop,1);
        s.RecordIncome(Money.FromRaw(19),ScoreIncome.Entrance);s.RecordIncome(Money.FromRaw(23),ScoreIncome.Shop);
        s.RecordIncome(Money.FromRaw(29),ScoreIncome.SideShow);
        End(s,c,w,1001,31,37);
        for(int i=1;i<144;i++)End(s,c,w,1001+i,31+i,37);
        Assert.Equal(1001,s.CopyBankRing(BankHistoryRow.Balance)[0]);
        Assert.Equal(505,s.CopyBankRing(BankHistoryRow.ParkValue)[0]);
        foreach(var r in new[]{BankHistoryRow.Income,BankHistoryRow.Entrance,BankHistoryRow.Shop,BankHistoryRow.SideShow,BankHistoryRow.Spending,BankHistoryRow.Wages})
            Assert.Equal(0,s.CopyBankRing(r)[0]);
        w.Price=303;End(s,c,w,2001,88);
        Assert.Equal(2001,s.Read(BankHistoryRow.Balance,0).Raw);Assert.Equal(1515,s.Read(BankHistoryRow.ParkValue,0).Raw);
        Assert.Equal(1002,s.Read(BankHistoryRow.Balance,144).Raw);
        Assert.Equal(0,s.Read(BankHistoryRow.Balance,145).Raw);
    }

    // REJECTS invented save offsets, lost totals, tenths-preserving totals, zeroed foreign fields, and loan bookkeeping serialization.
    [Fact]
    public void BankSaveFieldsAndContinuation()
    {
        var s=new ParkScore();var c=new Calendar();var w=new World();w.Add(AttractionType.Feature,1);
        s.RecordIncome(Money.FromRaw(129),ScoreIncome.Entrance);s.RecordIncome(Money.FromRaw(237),ScoreIncome.Shop);
        s.RecordIncome(Money.FromRaw(341),ScoreIncome.SideShow);s.RecordSackingPay(Money.FromRaw(49));
        for(int i=0;i<13;i++)End(s,c,w,1009,11,3);
        s.RecordIncome(Money.FromRaw(79));s.RecordSpending(Money.FromRaw(27));
        var save=new BankSave();save.BalanceRaw=987;save.Bytes[0x280]=123;save.Bytes[0x4F]=173;
        s.CaptureBank(save);
        Assert.Equal(987,save.BalanceRaw);Assert.Equal(123,save.Bytes[0x280]);Assert.Equal(173,save.Bytes[0x4F]);
        var values=new[]{save.LastYearIncomePounds,save.LastYearSpendPounds,save.ThisYearIncomePounds,save.ThisYearSpendPounds,
            save.SideshowTakingsPounds,save.EntryTakingsPounds,save.ShopProfitPounds,save.WagesPounds,save.SpendPounds,save.IncomePounds,save.YearlyValuePounds,save.YearlyBalancePounds};
        Assert.Equal(new[]{70,21,7,4,34,12,23,19,25,78,50,99},values);
        var restored=new ParkScore();restored.RestoreBank(save);
        Assert.Equal(13,restored.MonthIndex);Assert.Equal(0,restored.LoanRepayments.Raw);
        Assert.Equal(700,restored.LastYearIncome.Raw);Assert.Equal(210,restored.LastYearSpending.Raw);
        Assert.Equal(70,restored.ThisYearIncome.Raw);Assert.Equal(40,restored.ThisYearSpending.Raw);
        Assert.Equal(340,restored.SideshowTakings.Raw);Assert.Equal(120,restored.EntryTakings.Raw);
        Assert.Equal(230,restored.ShopProfit.Raw);Assert.Equal(190,restored.Wages.Raw);
        Assert.Equal(250,restored.Spending.Raw);Assert.Equal(780,restored.Income.Raw);
        Assert.Equal(500,restored.YearlyValue.Raw);Assert.Equal(990,restored.YearlyBalance.Raw);
        End(restored,c,w,444,5,2);Assert.Equal(14,restored.MonthIndex);Assert.Equal(257,restored.Spending.Raw);
        Assert.Equal(444,restored.Read(BankHistoryRow.Balance,0).Raw);
    }

    // REJECTS a codec that exercises no samples, unsigned/signed substitutions, swapped ring direction,
    // fake lossless round trips, padding zero-fill, and ordinary interpolation in the buggy byte six-month subslot.
    [Theory]
    [InlineData("byte")] [InlineData("money")]
    public void EveryCodecCellMatchesOriginalInstructions(string kind)
    {
        using var audit=Audit();var cases=audit.RootElement.GetProperty("history_cases").EnumerateArray()
            .Where(c=>c.GetProperty("kind").GetString()==kind).ToArray();
        Assert.Equal(kind=="byte"?5:6,cases.Length);
        foreach(var c in cases)
        {
            int months=c.GetProperty("months").GetInt32();
            int[] ring=Ints(c.GetProperty("ring")), expected=Ints(c.GetProperty("restored"));
            Assert.Equal(144,ring.Length);Assert.True(ring.Distinct().Count()>100);
            byte[] packed=Enumerable.Repeat((byte)173,kind=="byte"?36:80).ToArray();
            if(kind=="byte")ScoreHistoryCodec.PackBytes(ring.Select(i=>(byte)i).ToArray(),months,packed);
            else ScoreHistoryCodec.PackMoney(ring,months,packed);
            Assert.Equal(Bytes(c.GetProperty("encoded")),packed.Take(kind=="byte"?35:80));
            int[] actual;
            if(kind=="byte") {var r=new byte[144];ScoreHistoryCodec.UnpackBytes(packed,months,r);actual=r.Select(b=>(int)b).ToArray();}
            else {actual=new int[144];ScoreHistoryCodec.UnpackMoney(packed,months,actual);}
            Assert.Equal(expected,actual);Assert.False(ring.SequenceEqual(actual));
        }
    }

    // REJECTS calendar save rows in the wrong order, missing annual state, clobbered host bytes,
    // ignoring the next row/padding in oldest interpolation, and failure to continue sampling after load.
    [Fact]
    public void CalendarCaptureRestoreUsesExistingHistoryOwner()
    {
        var h=new ParkHistory();var w=new World();w.AddGuests(3);w.Guests.ForEach(v=>{v.Happiness=77;v.ArrivedOnDay=8;});
        for(int m=0;m<144;m++)h.RecordMonth(m,0,1,31,w.Guests,42);
        var save=new CalendarSave {TotalMonths=144,Admissions=999};save.Bytes[0xDA]=173;
        h.Capture(save,144);
        Assert.Equal(999u,save.Admissions);Assert.Equal(42,save.RatingAtNewYear);Assert.Equal(173,save.Bytes[0xDA]);
        Assert.Equal(new byte[]{3,0,77,23,42},Enumerable.Range(0,5).Select(i=>save.Bytes[0x2B+35*i]));
        var restored=new ParkHistory();restored.Restore(save);
        Assert.Equal(42,restored.RatingAtNewYear);
        Assert.Equal(77,restored.Read(HistoryRow.Happiness,144,1));
        // At age 143 the oldest interpolation weights current 42 by 2, padding 173 by 6.
        Assert.Equal(140,restored.Read(HistoryRow.Overall,144,143));
        restored.RecordMonth(144,1,12,40,w.Guests,11);
        Assert.Equal(11,restored.Read(HistoryRow.Overall,145,0));Assert.Equal(42,restored.RatingAtNewYear);
        restored.RecordMonth(155,0,13,50,w.Guests,13);Assert.Equal(13,restored.RatingAtNewYear);
    }
}
