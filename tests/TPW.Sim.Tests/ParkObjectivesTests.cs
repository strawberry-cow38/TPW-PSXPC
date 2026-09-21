using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests;

public class ParkObjectivesTests
{
    sealed class World : IParkObjectiveWorld
    {
        public bool RestrictedMode { get; set; }
        public uint Admissions { get; set; }
        public bool ParkOpen { get; set; }
        public uint OpeningMonth { get; set; }
        public Money OutstandingLoans { get; set; }
        public uint TourTransportCount { get; set; }
        public uint PathTileCount { get; set; } = 101;
        public List<ObjectiveAttraction> Built = new();
        public IEnumerable<ObjectiveAttraction> Attractions => Built;
        public void Add(AttractionType type, int n = 1, int level = 0, int price = 0,
            AttractionStatus status = AttractionStatus.JustPlaced, byte flags = 0,
            short x = 0, short y = 0, short width = 1, short height = 1, bool bin = false)
        {
            for (int i = 0; i < n; i++) Built.Add(new(new(type, 17, status, flags, UpgradeLevel: level),
                price, x, y, width, height, bin));
        }
    }
    static Calendar Day(int day = 7, int year = 0, int month = 0)
    { var c = new Calendar(); c.RestoreTo(year, month, day, 0, 0); return c; }
    static ParkObjectives New(ParkScore score = null, ObjectiveState state = default, int world = 0, int park = 0)
        => new(ParkObjectiveDefinition.ForPalPark(world, park), score ?? new ParkScore(), state);
    static ushort[] Messages(ParkObjectives o, World w, Calendar c = null)
        => o.AfterDay(c ?? Day(), w).Select(a => a.MessageId).ToArray();

    // REJECTS a vacuous/empty binary audit, corruption of ANY of the eight complete records,
    // and divergence from original selector/comparison instructions on 28 controlled fixtures.
    [Fact]
    public void OriginalInstructionsAndAllRecordBytesAgree()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"goals-audit.json")));
        var root=json.RootElement; var controls=root.GetProperty("controls").EnumerateObject().ToArray();
        Assert.Equal(12,controls.Length); Assert.All(controls,c=>Assert.True(c.Value.GetBoolean(),c.Name));
        var records=root.GetProperty("records").EnumerateArray().ToArray(); Assert.Equal(8,records.Length);
        foreach(var r in records)
            Assert.Equal(r.GetProperty("hex").GetString(),Convert.ToHexString(ParkObjectiveDefinition.ForPalPark(
                r.GetProperty("world").GetInt32(),r.GetProperty("park").GetInt32()).CopyRecord()));
        var cases=root.GetProperty("decision_cases").EnumerateArray().ToArray(); Assert.Equal(28,cases.Length);
        foreach(var c in cases)
        {
            var score=new ParkScore(); score.RecordIncome(Money.FromRaw(c.GetProperty("income").GetInt64()));
            score.RecordSpending(Money.FromRaw(c.GetProperty("spending").GetInt64()));
            var w=new World {Admissions=c.GetProperty("admissions").GetUInt32(),
                OutstandingLoans=Money.FromRaw(c.GetProperty("loans").GetInt64()),OpeningMonth=c.GetProperty("opened").GetUInt32(),
                ParkOpen=c.GetProperty("park_open").GetBoolean(),RestrictedMode=c.GetProperty("sandbox").GetBoolean()};
            var o=New(score,new(0xFFFFFFF1,0xFFFFFFFF),c.GetProperty("world").GetInt32(),c.GetProperty("park").GetInt32());
            Assert.Equal(c.GetProperty("messages").EnumerateArray().Select(m=>(ushort)m.GetInt32()),
                Messages(o,w,Day(7,c.GetProperty("year").GetInt32(),c.GetProperty("month").GetInt32())));
            Assert.Equal(c.GetProperty("park_bits").GetUInt32(),o.State.ParkBits);
        }
    }

    // REJECTS reordered/omitted park records, wrong field offsets, and inferred tutorial flags.
    [Theory]
    [InlineData(0,0,100,2000,1,true)] [InlineData(0,1,200,3000,2,false)]
    [InlineData(1,0,150,2500,1,false)] [InlineData(1,1,250,3000,2,false)]
    [InlineData(2,0,150,2500,1,false)] [InlineData(2,1,500,5000,5,false)]
    [InlineData(3,0,250,3000,2,false)] [InlineData(3,1,500,5000,5,false)]
    public void AllEightDefinitions(int world, int park, uint guests, int profit, uint years, bool tutorial)
    {
        var d = ParkObjectiveDefinition.ForPalPark(world, park);
        Assert.Equal(guests, d.AdmissionsThreshold); Assert.Equal(profit, d.ProfitPounds);
        Assert.Equal(years, d.YearsOpen); Assert.Equal(tutorial, d.TutorialAwardEnabled);
        Assert.Equal(2000u, d.FeatureValuePounds); Assert.Equal(100u, d.MaximumPathTiles);
        Assert.Equal(52, d.CopyRecord().Length);
    }

    // REJECTS aliasing input/output buffers, discarding opaque bytes, and hardcoding evaluator thresholds.
    [Fact]
    public void LoaderOwnsDecodedRecordAndUnknownBytesSurvive()
    {
        var raw = Enumerable.Range(0, 52).Select(i => (byte)i).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(12), 4321);
        var original = (byte[])raw.Clone(); var d = new ParkObjectiveDefinition(raw);
        Array.Clear(raw); var copy = d.CopyRecord(); copy[12] = 0;
        Assert.Equal(original, d.CopyRecord()); Assert.Equal(4321u, d.AdmissionsThreshold);
        var w = new World { Admissions = 4321 }; var o = new ParkObjectives(d, new ParkScore());
        Assert.DoesNotContain((ushort)0xAF, Messages(o,w));
        w.Admissions++; Assert.Contains((ushort)0xAF, Messages(o,w));
        Assert.Throws<ArgumentException>(() => new ParkObjectiveDefinition(new byte[51]));
        Assert.Throws<ArgumentException>(() => new ParkObjectiveDefinition(new byte[53]));
    }

    // REJECTS clamping invalid selectors to a real park, and inventing goals for a missing record.
    [Theory]
    [InlineData(-1,0)] [InlineData(4,0)] [InlineData(0,-1)] [InlineData(0,2)]
    public void InvalidParkHasNoDefinition(int world, int park)
    {
        Assert.Null(ParkObjectiveDefinition.ForPalPark(world,park));
        var o = new ParkObjectives(null,new ParkScore());
        Assert.Empty(o.AfterDay(Day(),null)); Assert.Empty(o.UnmetDescriptions(false));
    }

    // REJECTS real-week cadence, day-1 UI numbering, and excluding the month-boundary day zero.
    [Theory]
    [InlineData(0,true)] [InlineData(1,false)] [InlineData(6,false)] [InlineData(7,true)]
    [InlineData(14,true)] [InlineData(21,true)] [InlineData(28,true)] [InlineData(30,false)]
    public void OnlyEligibleCalendarDays(int day, bool eligible)
    {
        var w = new World { Admissions = 101 };
        Assert.Equal(eligible ? new ushort[]{0xAF} : Array.Empty<ushort>(), Messages(New(),w,Day(day)));
        var c = Day(27); c.AdvanceDay(); Assert.True(c.IsObjectiveDay);
        c.AdvanceDay(); c.AdvanceDay(); c.AdvanceDay(); Assert.Equal(0,c.Day); Assert.True(c.IsObjectiveDay);
    }

    // REJECTS sandbox awards/descriptions, and mutating or clearing restored bits on a skipped check.
    [Fact]
    public void SandboxAndIneligibleDaysDoNotTouchState()
    {
        var state = new ObjectiveState(0x80000001,0x80000000); var o = New(state:state);
        Assert.Empty(o.AfterDay(Day(1),null)); Assert.Equal(state,o.State);
        Assert.Empty(Messages(o,new World {RestrictedMode=true,Admissions=uint.MaxValue,ParkOpen=true},Day(7,100)));
        Assert.Equal(state,o.State); Assert.Empty(o.UnmetDescriptions(true));
        Assert.Single(Messages(o,new World {Admissions=101}));
    }

    // REJECTS >= instead of >, using current visitors, and signed admission comparison.
    [Theory]
    [InlineData(99u,false)] [InlineData(100u,false)] [InlineData(101u,true)] [InlineData(uint.MaxValue,true)]
    public void AdmissionsAreStrictAndUnsigned(uint count,bool expected)
        => Assert.Equal(expected,Messages(New(),new World {Admissions=count}).Contains((ushort)0xAF));

    // REJECTS >=, pounds/raw confusion, ignoring debt/spend, replacing all-time totals with a 12-month ring,
    // and introducing current bank balance/park value into the formula.
    [Theory]
    [InlineData(20000,0,0,false)] [InlineData(20001,0,0,true)]
    [InlineData(30001,10000,0,true)] [InlineData(30000,10000,0,false)]
    [InlineData(30001,0,10000,true)] [InlineData(30000,0,10000,false)]
    [InlineData(-1,0,0,false)]
    public void ProfitUsesParkScoreTotals(long income,long spending,long loan,bool expected)
    {
        var score = new ParkScore(); score.RecordIncome(Money.FromRaw(income)); score.RecordSpending(Money.FromRaw(spending));
        Assert.Equal(expected,Messages(New(score),new World {OutstandingLoans=Money.FromRaw(loan)}).Contains((ushort)0xB0));
    }

    // REJECTS wide arithmetic and converting the live total to whole pounds before comparing.
    [Fact]
    public void ProfitWrapsNativeWordsIncludingThreshold()
    {
        var score = new ParkScore(); score.RecordIncome(Money.FromRaw(int.MaxValue));
        Assert.DoesNotContain((ushort)0xB0,Messages(New(score),new World {OutstandingLoans=Money.FromRaw(-1)}));
        var raw = ParkObjectiveDefinition.ForPalPark(0,0).CopyRecord();
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(16),int.MaxValue);
        Assert.Contains((ushort)0xB0,Messages(new(new(raw),new ParkScore()),new World()));
    }

    // REJECTS calendar-year comparison, forgetting month-opened, a strict anniversary boundary,
    // requiring open for every award, and fixing unsigned opening-stamp underflow.
    [Theory]
    [InlineData(1,4,5u,true,false)] [InlineData(1,5,5u,true,true)]
    [InlineData(1,6,5u,true,true)] [InlineData(1,5,5u,false,false)]
    [InlineData(0,0,65535u,true,true)] [InlineData(0,0,0u,true,false)]
    public void YearsUseUnsignedElapsedMonths(int year,int month,uint opened,bool open,bool expected)
        => Assert.Equal(expected,Messages(New(),new World {OpeningMonth=opened,ParkOpen=open},Day(7,year,month)).Contains((ushort)0xB1));

    // REJECTS forgetting any tutorial prerequisite, counting transports as tutorial rides,
    // excluding status-zero objects, or enabling this award in every park.
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void TutorialRequiresItsFourPoolConditions(int missing)
    {
        var w = new World {TourTransportCount=100};
        if(missing!=0)w.Add(AttractionType.SideShow);
        if(missing!=1)w.Add(AttractionType.Shop);
        if(missing!=2)w.Add(AttractionType.Feature);
        w.Add(AttractionType.Ride,missing==3?1:2);
        Assert.DoesNotContain((ushort)0xBC,Messages(New(),w));
        if(missing==0)w.Add(AttractionType.SideShow);
        if(missing==1)w.Add(AttractionType.Shop);
        if(missing==2)w.Add(AttractionType.Feature);
        if(missing==3)w.Add(AttractionType.TourRide);
        Assert.Contains((ushort)0xBC,Messages(New(),w));
        Assert.DoesNotContain((ushort)0xBC,Messages(New(park:1),w));
    }

    // REJECTS silently resolving the security disagreement, wrong mask/flag precedence/status,
    // >81 or >=80, or treating argument 64 as an advisor statistic index or Overall Rating.
    [Fact]
    public void SecurityRetainsFeatureCountEvenBeyondRetailPoolCapacity()
    {
        var w = new World(); w.Add(AttractionType.Feature,80,status:AttractionStatus.Running,flags:8);
        var o=New(); Assert.DoesNotContain((ushort)0xB2,Messages(o,w));
        w.Add(AttractionType.Feature,flags:8); // held
        w.Add(AttractionType.Feature,status:AttractionStatus.Running,flags:9); // usable wins
        w.Add(AttractionType.Feature,status:AttractionStatus.Running,flags:2); // different class
        Assert.DoesNotContain((ushort)0xB2,Messages(o,w));
        w.Add(AttractionType.Feature,status:AttractionStatus.Running,flags:8);
        Assert.Contains((ushort)0xB2,Messages(o,w));
    }

    // REJECTS >=2 upgrades, a percentage instead of EVERY ride, omission of any ride class,
    // status filtering, and a gate requiring more than eight rides.
    [Theory]
    [InlineData(AttractionType.Ride)] [InlineData(AttractionType.TourRide)]
    [InlineData(AttractionType.TrackRide)] [InlineData(AttractionType.RollerCoaster)]
    public void UpgradeChecksEveryClass(AttractionType type)
    {
        var w=new World(); w.Add(AttractionType.Ride,7,level:1);
        Assert.DoesNotContain((ushort)0xB3,Messages(New(),w));
        w.Add(type,level:0); Assert.DoesNotContain((ushort)0xB3,Messages(New(),w));
        w.Built.RemoveAt(7); w.Add(type,level:1); Assert.Contains((ushort)0xB3,Messages(New(),w));
    }

    // REJECTS forgetting the separate gate on an empty upgrade set, or removing the transport oddity.
    [Fact]
    public void EmptyRideControlAndTransportsAreCountedSeparately()
    {
        var w=new World(); Assert.Empty(w.Built); Assert.Empty(Messages(New(),w));
        w.TourTransportCount=7; Assert.DoesNotContain((ushort)0xB3,Messages(New(),w));
        w.TourTransportCount=8; Assert.Contains((ushort)0xB3,Messages(New(),w));
    }

    // REJECTS strict value threshold, halving as in park value, omitting the eight-ride gate,
    // counting non-features or excluding held features; also rejects signed sum/comparison.
    [Fact]
    public void AestheticUsesWholeFeaturePriceSum()
    {
        var w=new World {TourTransportCount=7}; w.Add(AttractionType.Feature,price:2000);
        var o=New(); Assert.DoesNotContain((ushort)0xB4,Messages(o,w));
        w.TourTransportCount=8; w.Built.Clear(); w.Add(AttractionType.Feature,price:1999);
        w.Add(AttractionType.Shop,price:5000); Assert.DoesNotContain((ushort)0xB4,Messages(o,w));
        w.Add(AttractionType.Feature,price:1); Assert.Contains((ushort)0xB4,Messages(o,w));
        w.Built.Clear(); w.Add(AttractionType.Feature,price:-1); Assert.Contains((ushort)0xB4,Messages(New(),w));
        w.Add(AttractionType.Feature,price:1); Assert.DoesNotContain((ushort)0xB4,Messages(New(),w));
    }

    // REJECTS omitting the ten-ride gate, using >= or < for the path maximum, signed path counts,
    // imposing a minimum number of paths, or using path distance/connectedness instead of count.
    [Theory]
    [InlineData(9u,100u,false)] [InlineData(10u,100u,true)] [InlineData(10u,101u,false)]
    [InlineData(10u,0u,true)] [InlineData(10u,uint.MaxValue,false)]
    public void PathAwardIsAnInclusiveMaximum(uint rides,uint paths,bool expected)
        => Assert.Equal(expected,Messages(New(),new World{TourTransportCount=rides,PathTileCount=paths}).Contains((ushort)0xB6));

    // REJECTS an empty-set pass without five shops, requiring bins for held shops, and omitting
    // the placed-bin/class predicates for an actual placed shop.
    [Fact]
    public void GreenEmptyAndPopulatedControls()
    {
        var w=new World(); Assert.Empty(Messages(New(),w));
        w.Add(AttractionType.Shop,4); Assert.DoesNotContain((ushort)0xB5,Messages(New(),w));
        w.Add(AttractionType.Shop); Assert.Equal(5,w.Built.Count);
        Assert.Contains((ushort)0xB5,Messages(New(),w)); // original skips every held shop
        w.Built.Clear(); w.Add(AttractionType.Shop,5,status:AttractionStatus.Running);
        Assert.DoesNotContain((ushort)0xB5,Messages(New(),w));
        w.Add(AttractionType.Feature,bin:true); Assert.DoesNotContain((ushort)0xB5,Messages(New(),w));
        w.Add(AttractionType.Feature,status:AttractionStatus.Running); Assert.DoesNotContain((ushort)0xB5,Messages(New(),w));
        w.Add(AttractionType.Feature,status:AttractionStatus.Running,bin:true); Assert.Contains((ushort)0xB5,Messages(New(),w));
        w.Add(AttractionType.Shop,status:AttractionStatus.Running,x:100); Assert.DoesNotContain((ushort)0xB5,Messages(New(),w));
    }

    // REJECTS exclusive overlap on each edge, wrong expansion distance, point-distance shortcuts,
    // ignoring footprint extents, and treating all nearby features as bins.
    [Theory]
    [InlineData(-3,0,true)] [InlineData(-4,0,false)] [InlineData(3,0,true)] [InlineData(4,0,false)]
    [InlineData(0,-3,true)] [InlineData(0,-4,false)] [InlineData(0,3,true)] [InlineData(0,4,false)]
    [InlineData(3,3,true)]
    public void BinRectangleEdges(short x,short y,bool expected)
    {
        var w=new World(); w.Add(AttractionType.Shop,5,status:AttractionStatus.Running);
        w.Add(AttractionType.Feature,status:AttractionStatus.Running,x:x,y:y,bin:true);
        Assert.Equal(expected,Messages(New(),w).Contains((ushort)0xB5));
    }

    // REJECTS signed-word rather than signed-halfword far edges and fixing malformed-map overflow.
    [Fact]
    public void BinBoundsWrapHalfwords()
    {
        var w=new World(); w.Add(AttractionType.Shop,5,status:AttractionStatus.Running,x:32766);
        w.Add(AttractionType.Feature,status:AttractionStatus.Running,x:32766,bin:true);
        Assert.DoesNotContain((ushort)0xB5,Messages(New(),w));
    }

    // REJECTS swapped state words, clearing masks on Restore, and showing completed descriptions
    // when a different objective remains unmet. Restore is deliberately allowed to clear bits.
    [Fact]
    public void RestoreAndDescriptionsKeepBitsIndependent()
    {
        var o=New();o.Restore(new(1u<<2,1u<<1));
        Assert.Equal(new[]{1,3},o.UnmetDescriptions(false).Select(d=>d.Bit));
        var w=new World {Admissions=101,TourTransportCount=8};
        Assert.Equal(new ushort[]{0xAF},Messages(o,w));
        o.Restore(default);Assert.Equal(default,o.State); Assert.Equal(3,o.UnmetDescriptions(false).Count);
        Assert.Contains((ushort)0xB3,Messages(o,w));
    }

    // REJECTS clearing unrelated bits, wrong message/bit/order, repeat awards, reward money,
    // shared state between owners, hidden descriptions, and replaying awards when loading.
    [Fact]
    public void NineAwardsLatchInNativeOrderAndReturnOnlyHostEffects()
    {
        var score=new ParkScore();score.RecordIncome(Money.FromRaw(20001));
        var o=New(score,new(0x80000001,0x80000000));
        var w=new World{Admissions=101,ParkOpen=true,PathTileCount=100};
        w.Add(AttractionType.Ride,10,level:1);w.Add(AttractionType.SideShow);
        w.Add(AttractionType.Shop,5,status:AttractionStatus.Running);
        w.Add(AttractionType.Feature,81,price:25,status:AttractionStatus.Running,flags:8,bin:true);
        var descriptions=o.UnmetDescriptions(false);
        Assert.Equal(new[]{new ObjectiveDescription(1,0x161,100),new(2,0x34E,2000),new(3,0x1EB,1)},descriptions);
        var awards=o.AfterDay(Day(7,1),w).ToArray(); Assert.Equal(9,awards.Length);
        Assert.Equal(new ushort[]{0xAF,0xB0,0xB1,0xBC,0xB2,0xB3,0xB4,0xB5,0xB6},awards.Select(a=>a.MessageId));
        Assert.Equal(new[]{1,2,3,4,0,1,2,3,4},awards.Select(a=>a.Bit));
        Assert.Equal(new[]{false,false,false,false,true,true,true,true,true},awards.Select(a=>a.Bonus));
        Assert.All(awards,a=>Assert.Equal(1,a.GoldTickets));
        Assert.All(awards,a=>Assert.True(a.AddToMessageList));
        Assert.Equal(new ObjectiveState(0x8000001F,0x8000001F),o.State);
        Assert.Empty(o.AfterDay(Day(14,1),w)); Assert.Empty(o.UnmetDescriptions(false));
        Assert.Equal(20001,score.Income.Raw); Assert.Equal(0,score.Spending.Raw);
        var restored=New(score);restored.Restore(o.State); Assert.Empty(restored.AfterDay(Day(21,1),w));
        Assert.Empty(restored.AfterDay(Day(),new World())); Assert.Equal(o.State,restored.State);
        Assert.Equal(default,New().State);
    }

    // REJECTS an empty/partial audit, non-weekly bit/message/ticket drift, sandbox latching,
    // replay grants, dropping unrelated bits, or mistaking a destructor argument for list type 2.
    [Fact]
    public void All27CommonMinigameResultsMatchOriginalInstructions()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"objbytes-audit.json")));
        var root = json.RootElement;
        var controls = root.GetProperty("controls").EnumerateObject().ToArray();
        Assert.Equal(12,controls.Length); Assert.All(controls,c=>Assert.True(c.Value.GetBoolean(),c.Name));
        var cases = root.GetProperty("common_award_cases").EnumerateArray().ToArray();
        Assert.Equal(27,cases.Length);
        Assert.Equal(9,cases.Select(c=>c.GetProperty("game").GetInt32()).Distinct().Count());
        foreach (var c in cases)
        {
            int game = c.GetProperty("game").GetInt32();
            var initial = new ObjectiveState(c.GetProperty("before_bits").GetUInt32(),0x81234567);
            var o = New(state:initial);
            var result = o.AfterMinigameWin(game,c.GetProperty("restricted").GetBoolean());
            var events = c.GetProperty("events").EnumerateArray().ToArray();
            Assert.Equal(events.Single(e=>e[0].GetString()=="message")[1].GetInt32(),result.MessageId);
            Assert.Equal(events.Single(e=>e[0].GetString()=="text")[1].GetInt32(),result.TextId);
            Assert.Equal(c.GetProperty("tickets").GetInt32(),result.Award?.GoldTickets ?? 0);
            Assert.Equal(c.GetProperty("park_bits").GetUInt32(),o.State.ParkBits);
            Assert.Equal(c.GetProperty("bonus_bits").GetUInt32(),o.State.BonusBits);
            if (result.Award is { } award)
            {
                Assert.Equal(game+4,award.Bit); Assert.False(award.Bonus);
                Assert.Equal(result.MessageId,award.MessageId); Assert.False(award.AddToMessageList);
            }
        }
        var triggers = root.GetProperty("trigger_cases").EnumerateArray().ToArray();
        Assert.Equal(43,triggers.Length);
        for (int game=1;game<=9;game++)
        {
            var rows = triggers.Where(c=>c.GetProperty("game").GetInt32()==game).ToArray();
            Assert.NotEmpty(rows);
            Assert.Contains(rows,c=>c.GetProperty("expected_win").GetBoolean());
            Assert.Contains(rows,c=>!c.GetProperty("expected_win").GetBoolean());
        }
    }

    // REJECTS sharing a minigame bit between parks, definition/calendar gating, overwriting
    // tutorial/main/high bits, a bonus-bit latch, sandbox consuming the award, or replay on restore.
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public void MinigameAwardsUseExistingPerParkState(int game)
    {
        var score = new ParkScore(); score.RecordIncome(Money.FromPounds(123));
        var initial = new ObjectiveState(0x8000001F,uint.MaxValue);
        var o = new ParkObjectives(null,score,initial);
        Assert.Equal(new ObjectiveMinigameResult(0x298,0xC3,null),o.AfterMinigameWin(game,true));
        Assert.Equal(initial,o.State);
        var win = o.AfterMinigameWin(game,false);
        Assert.Equal(new ObjectiveMinigameResult(0x234,0xC5,
            new ObjectiveAward(false,game+4,0xC5) { AddToMessageList=false }),win);
        Assert.Equal(initial with { ParkBits = initial.ParkBits | (1u << (game+4)) },o.State);
        var loaded = New(state:o.State);
        Assert.Null(loaded.AfterMinigameWin(game,false).Award);
        Assert.Equal(o.State,loaded.State);
        Assert.NotNull(New(state:initial).AfterMinigameWin(game,false).Award);
        Assert.Equal(Money.FromPounds(123),score.Income); Assert.Equal(Money.FromPounds(0),score.Spending);
    }

    // REJECTS accepting IDs that the nine-case native constructor cannot produce, shift masking
    // an invalid ID onto a weekly bit, or mutating state before host input validation.
    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(10)] [InlineData(int.MaxValue)]
    public void MinigameHostBoundaryRejectsInvalidIds(int game)
    {
        var o = New(state:new(0x80000001,0x12345678)); var before=o.State;
        Assert.Throws<ArgumentOutOfRangeException>(()=>o.AfterMinigameWin(game,false));
        Assert.Equal(before,o.State);
    }

    // REJECTS using a tail neighbour, first opaque word, or a constant for the advertised total.
    [Theory]
    [InlineData(0,0,7)] [InlineData(0,1,6)] [InlineData(1,0,5)] [InlineData(1,1,6)]
    [InlineData(2,0,5)] [InlineData(2,1,6)] [InlineData(3,0,5)] [InlineData(3,1,5)]
    public void AdvertisedTotalIsAByteReadByTheWorldMap(int world,int park,byte expected)
    {
        var d=ParkObjectiveDefinition.ForPalPark(world,park);
        Assert.Equal(expected,d.AdvertisedGoldTickets);
        var raw=d.CopyRecord(); raw[0x31]=201;
        Assert.Equal((byte)201,new ParkObjectiveDefinition(raw).AdvertisedGoldTickets);
    }

    // REJECTS inventing evaluator semantics for any of the 30 unread bytes; positive control
    // changes the actually read admissions threshold and changes the decision.
    [Fact]
    public void ThirtyOpaqueBytesSurviveAndDoNotDriveEstablishedChecks()
    {
        int[] offsets = Enumerable.Range(0,12).Concat(Enumerable.Range(0x20,16)).Concat(new[]{0x32,0x33}).ToArray();
        Assert.Equal(30,offsets.Length);
        var raw=ParkObjectiveDefinition.ForPalPark(0,0).CopyRecord();
        var world=new World {Admissions=101};
        var expected=Messages(New(),world);
        Assert.Equal(new ushort[]{0xAF},expected);
        foreach (int offset in offsets)
        {
            var changed=(byte[])raw.Clone(); changed[offset]^=0xFF;
            var d=new ParkObjectiveDefinition(changed); var o=new ParkObjectives(d,new ParkScore());
            Assert.Equal(changed,d.CopyRecord()); Assert.Equal(expected,Messages(o,world));
            Assert.Equal(New().UnmetDescriptions(false),new ParkObjectives(d,new ParkScore()).UnmetDescriptions(false));
        }
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x0C),101);
        Assert.Empty(Messages(new ParkObjectives(new(raw),new ParkScore()),world));
    }
}
