using System;
using System.Collections.Generic;
using System.Linq;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests;

public class SaveRestoreTests
{
    static Visitor Guest(Action<Visitor> set) { var v = Visitor.Spawn(new Dice(), 0); set(v); return v; }

    sealed class Dice : IRandomSource
    {
        public List<int> Bounds = new(); public Queue<int> Values = new();
        public int Next(int n) { Bounds.Add(n); int value = Values.Count == 0 ? 0 : Values.Dequeue(); Assert.InRange(value, 0, n - 1); return value; }
    }

    // REJECTS per-guest snapshots, missing fields, money converted to pounds, and minima initialized like maxima.
    [Fact]
    public void PopulationRangesCaptureAllTwelveFieldsAndOddEmptyDefaults()
    {
        var a = Guest(v => {  v.Rubbish = 11;  v.Happiness = 22;  v.Nausea = 33;  v.NeedA = 44;  v.Boredom = 55;  v.RideDesire = 66;
            v.NeedB = 77;  v.Tiredness = 88;  v.Money = Money.FromRaw(3210);  v.WalkSpeed = 17;  v.NormalWalkSpeed = 19; });
        var b = Guest(v => {  v.Rubbish = 18;  v.Happiness = 29;  v.Nausea = 40;  v.NeedA = 51;  v.Boredom = 62;  v.RideDesire = 73;
            v.NeedB = 84;  v.Tiredness = 95;  v.Money = Money.FromRaw(4321);  v.WalkSpeed = 23;  v.NormalWalkSpeed = 27; });
        var r = VisitorSaveRanges.Capture(new[] { (a, (byte)13), (b, (byte)31) });
        for (int i = 0; i < 8; i++) Assert.Equal((11 * (i + 1), 7), r.Get(i));
        Assert.Equal((3210, 1111), r.Get(8)); Assert.Equal((17, 6), r.Get(9));
        Assert.Equal((0, 27), r.Get(10)); Assert.Equal((0, 31), r.Get(11));
        var empty = VisitorSaveRanges.Capture(Array.Empty<(Visitor, byte)>());
        Assert.Equal((15, 241), empty.Get(0)); Assert.Equal((9999, 55537), empty.Get(8)); Assert.Equal((0, 0), empty.Get(10));
        b.Money = Money.FromRaw(0x8001); r = VisitorSaveRanges.Capture(new[] { (b, (byte)0) });
        Assert.Equal((32769, 32767), r.Get(8));
    }

    // REJECTS a shared incorrect range layout, lost upper money byte, clamps in place of narrowing, or a thirteenth field.
    [Fact]
    public void RangeOffsetsAndNarrowingAreIndependentOfTheCodec()
    {
        var r = new VisitorSaveRanges();
        for (int i = 0; i < 12; i++) r.Set(i, 257 + i, 514 + i);
        var expected = new byte[] { 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 1, 10, 2, 10, 11, 11, 12, 12, 13 };
        Assert.Equal(expected, r.Bytes);
        Assert.Equal((265, 522), r.Get(8)); Assert.Equal((12, 13), r.Get(11));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Get(-1)); Assert.Throws<ArgumentOutOfRangeException>(() => r.Set(12, 1, 1));
    }

    // REJECTS uniform draws, range rather than range+1, the wrong bias, restoring skipped stats, preserving appearance or state.
    [Fact]
    public void GuestsAreReconstructedWithTheOriginalDiceAndSkippedFields()
    {
        var r = new VisitorSaveRanges(); for (int i = 0; i < 12; i++) r.Set(i, 3 * i + 1, 2 * i + 3);
        var dice = new Dice();
        int[] fields = { 0, 1, 3, 5, 6, 7, 8, 9, 10, 11 };
        foreach (int f in fields) { int bound = 2 * f + 4; dice.Values.Enqueue(bound - 1); dice.Values.Enqueue(bound / 2); }
        dice.Values.Enqueue(6);
        var guest = Guest(v => {  v.Nausea = 71;  v.Boredom = 83;  v.VisitorType = 2; });
        guest.PushState(VisitorState.Loading);
        byte unknown = r.Restore(guest, dice);
        Assert.Equal(new[] { 4,4,6,6,10,10,14,14,16,16,18,18,20,20,22,22,24,24,26,26,8 }, dice.Bounds);
        Assert.Equal(3, guest.Rubbish); Assert.Equal(8, guest.Happiness); Assert.Equal(16, guest.NeedA);
        Assert.Equal(24, guest.RideDesire); Assert.Equal(28, guest.NeedB); Assert.Equal(32, guest.Tiredness);
        Assert.Equal(39, guest.Money.Raw); Assert.Equal(44, guest.WalkSpeed); Assert.Equal(48, guest.NormalWalkSpeed); Assert.Equal(53, unknown);
        Assert.Equal(71, guest.Nausea); Assert.Equal(83, guest.Boredom); Assert.Equal(6, guest.VisitorType);
        Assert.Equal(VisitorState.WalkIn, guest.State); Assert.Equal(0, guest.StackDepth); Assert.Equal(13, guest.Animation); Assert.Equal(0, guest.Facing);
    }

    // REJECTS 'fixing' high bias to stay inside the saved range, copying zero-range fields directly, or changing the retained clamp.
    [Fact]
    public void ZeroWidthHighBiasedRangesStillAddOneAndRetainFindingsClamp()
    {
        var r = new VisitorSaveRanges(); for (int i = 0; i < 12; i++) r.Set(i, 47, 0);
        var guest = Visitor.Spawn(new Dice(), 0); var dice = new Dice(); r.Restore(guest, dice);
        Assert.Equal(47, guest.Happiness); Assert.Equal(48, guest.NeedA); Assert.Equal(48, guest.Tiredness);
        Assert.Equal(21, dice.Bounds.Count); Assert.Equal(20, dice.Bounds.Count(n => n == 1));
        r.Set(3, 127, 0); r.Set(9, 255, 0); r.Restore(guest, new Dice());
        Assert.Equal(100, guest.NeedA); // §0: preserve existing Stat.Clamp; binary signed-byte set would return 0.
        Assert.Equal(-1, guest.WalkSpeed);
    }

    sealed class LitterWorld : ILitterSaveWorld
    {
        public int Width => 5; public int Height => 3; public bool LitterSuppressed => false;
        public List<(int, int)> Tiles = new(); public int Ids; public int Allowed = 7;
        public int TakeObjectId() => ++Ids;
        public void RegisterLitter(Litter l) { }
        public void UnregisterLitter(Litter l) { }
        public int TileType(int x, int y) { Tiles.Add((x, y)); return x == 4 ? 13 : Allowed; }
    }

    // REJECTS accepting type 13, choosing tiles before allocation, rerolling sprites on retry, reversing counts, or preserving positions/claims.
    [Theory]
    [InlineData(2)] [InlineData(4)] [InlineData(7)]
    public void LitterRebuildsCountsUsingOnlyTheThreeAcceptedTileTypes(int allowed)
    {
        var w = new LitterWorld { Allowed = allowed }; var pool = new LitterPool(); var dice = new Dice();
        foreach (int value in new[] { 2, 4, 2, 1, 1, 100, 101, 3, 2, 0, 99, 98 }) dice.Values.Enqueue(value);
        LitterSave.Restore(1, 1, pool, w, dice);
        Assert.Equal(new[] { 6, 5, 3, 5, 3, 200, 200, 6, 5, 3, 200, 200 }, dice.Bounds);
        Assert.Equal(new[] { (4, 2), (1, 1), (2, 0) }, w.Tiles);
        Assert.Equal(2, pool.Count); Assert.Equal(2, w.Ids);
        var ordinary = pool.Pieces.Single(x => !x.IsVomit); var vomit = pool.Pieces.Single(x => x.IsVomit);
        Assert.Equal(384, ordinary.X); Assert.Equal(385, ordinary.Y); Assert.Equal(0x9C, ordinary.SpriteId);
        Assert.Equal(639, vomit.X); Assert.Equal(126, vomit.Y); Assert.Null(ordinary.ClaimedBy); Assert.Null(vomit.ClaimedBy);
    }

    // REJECTS swapping ordinary/vomit counts, skipping a count, or resetting the phase after the first piece.
    [Fact]
    public void LitterUsesUnequalCountsInOrdinaryThenVomitOrder()
    {
        var pool = new LitterPool(); var w = new LitterWorld(); var dice = new Dice();
        LitterSave.Restore(2, 3, pool, w, dice);
        Assert.Equal(5, pool.Count); Assert.Equal(2, pool.Pieces.Count(p => !p.IsVomit)); Assert.Equal(3, pool.Pieces.Count(p => p.IsVomit));
        Assert.Equal(new[] { 1, 2 }, pool.Pieces.Where(p => !p.IsVomit).Select(p => p.Id).OrderBy(x => x));
        Assert.Equal(new[] { 3, 4, 5 }, pool.Pieces.Where(p => p.IsVomit).Select(p => p.Id).OrderBy(x => x));
    }

    // REJECTS a fabricated retry cap, and consuming random numbers when both counts are zero.
    [Fact]
    public void LitterDoesNotCapRetriesOrRollForZeroCounts()
    {
        var pool = new LitterPool(); var w = new LitterWorld(); var dice = new Dice();
        LitterSave.Restore(0, 0, pool, w, dice); Assert.Empty(dice.Bounds); Assert.Empty(w.Tiles);
        dice.Values.Enqueue(0);
        for (int i = 0; i < 257; i++) { dice.Values.Enqueue(4); dice.Values.Enqueue(0); }
        dice.Values.Enqueue(1); dice.Values.Enqueue(2); dice.Values.Enqueue(100); dice.Values.Enqueue(100);
        LitterSave.Restore(1, 0, pool, w, dice); Assert.Equal(258, w.Tiles.Count); Assert.Equal(1, pool.Count);
    }

    sealed class Host : IParkSaveHost
    {
        public ParkSave Captured = SaveFixture.Park(); public List<string> Calls = new(); public List<Visitor> Guests = new();
        public List<AttractionSave> Attractions = new(); public List<StaffSave> Staff = new(); public List<ParkSaveMessage> Messages = new();
        public Money Fee; public BankSave Bank; public CalendarSave Calendar; public byte[] Catalogue, Topics;
        public IRandomSource Random { get; } = new Dice();
        public ParkSave Capture() => Captured;
        public void BeginPark(ParkSaveLayout layout) { Calls.Add("begin:" + layout.Restricted); Guests.Clear(); }
        public void OpenPark() => Calls.Add("open");
        public void SetEntryFee(Money fee) { Fee = fee; Calls.Add("fee"); }
        public void PlacePath(int x, int y) => Calls.Add($"path:{x},{y}");
        public void RestoreAttraction(AttractionSave a) { Attractions.Add(a); Calls.Add("attraction:" + a.Type); }
        public Visitor CreateVisitor() { var v = Guest(v => {  v.Nausea = 27;  v.Boredom = 41; }); Guests.Add(v); Calls.Add("guest"); return v; }
        public void SetVisitorUnknown63(Visitor v, byte value) => Calls.Add("guest63");
        public void RestoreStaff(int block, StaffSave s) { Staff.Add(s); Calls.Add("staff:" + block); }
        public void RestoreLitter(byte a, byte b) => Calls.Add($"litter:{a},{b}");
        public void RestoreBank(BankSave b) { Bank = b; Calls.Add("bank"); }
        public void RestoreCalendar(CalendarSave c) { Calendar = c; Calls.Add("calendar"); }
        public void RestoreCatalogue(byte[] c) { Catalogue = c; Calls.Add("catalogue"); }
        public void RestoreResearchTopics(byte[] b) { Topics = b; Calls.Add("research"); }
        public void RestoreMessage(ParkSaveMessage m) { Messages.Add(m); Calls.Add("message"); }
        public void FinishPark() => Calls.Add("finish");
    }

    // REJECTS reordering dependencies, single path placement, charging tenths as pounds, per-record guest data, or skipping late blocks.
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HostRestorationUsesTheWholeParkInBinaryOrder(bool restricted)
    {
        var host = new Host { Captured = SaveFixture.Park(restricted) };
        var p = host.Captured;
        p.Paths = new byte[15]; p.Paths[0] = 0x81; p.Paths[1] = 0x41; p.Paths[14] = 0xFF; // The excess reservation is not another path.
        byte[] written = ParkSaving.Save(host); ParkSaving.Load(written, p.Layout, host);
        var expected = new List<string> { "begin:" + restricted, "open", "fee", "path:0,0", "path:0,0", "path:2,1", "path:2,1",
            "path:3,1", "path:3,1", "path:4,2", "path:4,2", "attraction:1", "attraction:1", "attraction:6", "attraction:3",
            "attraction:7", "attraction:2", "attraction:2", "attraction:4", "attraction:5", "guest", "guest63", "guest", "guest63",
            "guest", "guest63", "staff:0", "staff:1", "staff:1", "staff:2", "staff:3", "staff:4", "staff:4", "litter:7,3", "bank", "calendar" };
        if (!restricted) expected.AddRange(new[] { "catalogue", "research" });
        expected.AddRange(new[] { "message", "message", "message", "message", "finish" });
        Assert.Equal(expected, host.Calls); Assert.Equal(46600, host.Fee.Raw);
        Assert.Equal(p.Bank.Bytes, host.Bank.Bytes); Assert.Equal(p.Calendar.Bytes, host.Calendar.Bytes);
        Assert.Equal(p.Attractions.SelectMany(a => a).SelectMany(a => a.Bytes), host.Attractions.SelectMany(a => a.Bytes));
        Assert.Equal(p.Staff.SelectMany(a => a).SelectMany(a => a.Bytes), host.Staff.SelectMany(a => a.Bytes));
        Assert.Equal(3, host.Guests.Count); Assert.All(host.Guests, g => { Assert.Equal(VisitorState.WalkIn, g.State); Assert.Equal(27, g.Nausea); Assert.Equal(41, g.Boredom); });
        if (!restricted) { Assert.Equal(p.Catalogue, host.Catalogue); Assert.Equal(p.ResearchTopics, host.Topics); }
        else { Assert.Null(host.Catalogue); Assert.Null(host.Topics); }
        Assert.Equal(4, host.Messages.Count);
    }

    // REJECTS mutating the live park before whole-buffer validation and unconditionally opening a closed saved park.
    [Fact]
    public void InvalidSaveNeverCallsHostAndClosedSaveStaysClosed()
    {
        var host = new Host(); var data = SaveFixture.Golden();
        Assert.Throws<FormatException>(() => ParkSaving.Load(data.AsSpan(0, data.Length - 1), host.Captured.Layout, host));
        Assert.Empty(host.Calls);
        host.Captured.Open = 0; host.Captured.VisitorCount = 0;
        ParkSaving.Load(ParkSaving.Save(host), host.Captured.Layout, host);
        Assert.DoesNotContain("open", host.Calls); Assert.Empty(host.Guests);
    }
}
