using System;
using System.Buffers.Binary;
using System.Linq;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests;

public class ParkSaveTests
{
    // REJECTS MSB-first/transposed path bits, saving queue/entrance as ordinary paths, or shrinking the W*H allocation.
    [Fact]
    public void PathCaptureUsesOnlyTypesTwoAndThirteen()
    {
        var layout = SaveFixture.Layout() with { Width = 4, Height = 4 };
        var calls = new System.Collections.Generic.List<(int, int)>();
        byte[] path = ParkSaveCodec.CapturePaths(layout, (x, y) => { calls.Add((x, y)); return 4 * y + x; });
        Assert.Equal(16, path.Length); Assert.Equal(new byte[] { 4, 32 }, path[..2]); Assert.All(path[2..], b => Assert.Equal(0, b));
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (i % 4, i / 4)), calls);
    }

    // REJECTS restoring fractional reliability, clamping saved sliders, confusing the placement-day/ticket word, or using a feature as a ride.
    [Fact]
    public void RideSaveFieldsPreserveTheirExactWidthsAndExistingSliderContract()
    {
        var a = new AttractionSave(1) { PlacementDay = 0xE123, Sliders = new(0xFEDC, 203, 179), ReliabilityFixed = 0x63ABC, Level = 2 };
        Assert.Equal(new byte[] { 0x23, 0xE1, 0xDC, 0xFE, 203, 179, 0, 99, 0, 0, 2 }, a.Bytes[0x88..0x93]);
        Assert.Equal(0xE123, a.PlacementDay); Assert.Equal(new RideSliderSave(0xFEDC, 203, 179), a.Sliders);
        Assert.Equal(0x63000, a.ReliabilityFixed); Assert.Equal(2, a.Level);
        a.ReliabilityFixed = -1; Assert.Equal(0xFF000, a.ReliabilityFixed);
        Assert.Throws<InvalidOperationException>(() => new AttractionSave(2).Sliders);
    }

    // REJECTS empty/default-only round trips, omitted fields, lost unknown bytes and swapped unequal blocks.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NontrivialParkRoundTripsEveryField(bool restricted)
    {
        var original = SaveFixture.Park(restricted);
        byte[] written = ParkSaveCodec.Write(original);
        Assert.Equal(SaveFixture.Golden(restricted), written);
        var loaded = ParkSaveCodec.Read(written, original.Layout);
        SaveFixture.Equal(original, loaded);
        Assert.Equal(written, ParkSaveCodec.Write(loaded));
    }

    // REJECTS a reader and writer sharing the same wrong offsets, sizes, count order or padding formula.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsAnIndependentlyAssembledImageAndPreservesPadding(bool restricted)
    {
        var p = SaveFixture.Park(restricted); var golden = SaveFixture.Golden(restricted, true);
        var read = ParkSaveCodec.Read(golden, p.Layout); SaveFixture.Equal(p, read);
        Assert.NotEmpty(read.Padding); Assert.Contains((byte)0xA0, read.Padding);
        Assert.Equal(golden, ParkSaveCodec.Write(read));
    }

    // REJECTS silent partial loads at EVERY byte boundary, and accepting an unexplained trailing block.
    [Fact]
    public void EveryTruncationAndTrailingDataAreRejected()
    {
        var good = SaveFixture.Golden(); var layout = SaveFixture.Layout();
        for (int i = 0; i < good.Length; i++)
        {
            int length = i;
            Assert.Throws<FormatException>(() => ParkSaveCodec.Read(good.AsSpan(0, length), layout));
        }
        Assert.Throws<FormatException>(() => ParkSaveCodec.Read(good.Concat(new byte[] { 7 }).ToArray(), layout));
        Assert.NotNull(ParkSaveCodec.Read(good, layout));
    }

    // REJECTS guessing dimensions/catalogue, conflating restriction with marker, or accepting another park.
    [Fact]
    public void ExternalLayoutMustMatch()
    {
        var good = SaveFixture.Golden(); var layout = SaveFixture.Layout();
        Assert.Throws<FormatException>(() => ParkSaveCodec.Read(good, layout with { World = 1 }));
        Assert.Throws<FormatException>(() => ParkSaveCodec.Read(good, layout with { Park = 0 }));
        Assert.Throws<FormatException>(() => ParkSaveCodec.Read(good, layout with { Width = 4 }));
        Assert.Throws<FormatException>(() => ParkSaveCodec.Read(good, layout with { Restricted = true }));
        Assert.Throws<ArgumentException>(() => new ParkSave(layout with { Height = -1 }));
        Assert.Throws<ArgumentException>(() => new ParkSave(layout with { Width = 32768 }));
        Assert.Throws<ArgumentException>(() => new ParkSave(layout with { CatalogueCounts = new[] { 1 } }));
        Assert.Throws<ArgumentException>(() => new ParkSave(layout with { CatalogueCounts = new[] { 1, 1, 1, 1, 1, 1, 1, -1 } }));
        Assert.Equal(0xA1B2, ParkSaveCodec.Read(good, layout).Marker); // Original does NOT validate 0x3039.
        Assert.Equal(0x3039, new ParkSave(layout).Marker);
    }

    // REJECTS writing a partial record, wrong class, overflowing byte count or inconsistent supplied padding.
    [Fact]
    public void WriterRejectsMalformedHostImages()
    {
        var p = SaveFixture.Park(); p.Paths = new byte[2]; Assert.Throws<ArgumentException>(() => ParkSaveCodec.Write(p));
        p = SaveFixture.Park(); p.ResearchTopics = new byte[15]; Assert.Throws<ArgumentException>(() => ParkSaveCodec.Write(p));
        p = SaveFixture.Park(); p.Attractions[0].Add(new(2)); Assert.Throws<ArgumentException>(() => ParkSaveCodec.Write(p));
        p = SaveFixture.Park(); p.Padding = new byte[1]; Assert.Throws<ArgumentException>(() => ParkSaveCodec.Write(p));
        p = SaveFixture.Park(); p.Padding = new byte[100]; Assert.Throws<ArgumentException>(() => ParkSaveCodec.Write(p));
        p = SaveFixture.Park(); for (int i = 0; i < 256; i++) p.Staff[0].Add(new());
        Assert.Throws<OverflowException>(() => ParkSaveCodec.Write(p));
        Assert.Throws<ArgumentException>(() => new StaffSave(new byte[15]));
        Assert.Throws<ArgumentException>(() => new AttractionSave(99));
    }

    // REJECTS persisting type-4 messages or adding a target index to non-target message types.
    [Fact]
    public void MessagesFilterAndConditionalFields()
    {
        var p = SaveFixture.Park(); p.Messages.Add(new(71, Array.Empty<byte>(), 4, 3, 8));
        Assert.Equal(SaveFixture.Golden(), ParkSaveCodec.Write(p));
        p.Messages[0] = new(0x3456, Array.Empty<byte>(), 1, 7, 23);
        Assert.Equal(SaveFixture.Golden(), ParkSaveCodec.Write(p));
        p.Messages.Add(new(-1, Array.Empty<byte>(), 2, -1, 0));
        Assert.Empty(ParkSaveCodec.Read(ParkSaveCodec.Write(p), p.Layout).Messages.Last().Text);
    }

    // REJECTS truncating the path reservation to bits/8 and incorrect research-to-message alignment.
    [Theory]
    [InlineData(1, 1, 0)] [InlineData(3, 3, 1)] [InlineData(4, 4, 2)] [InlineData(7, 3, 3)]
    public void AlignmentVariesWithMapAndCatalogue(int width, int height, int definitions)
    {
        var layout = SaveFixture.Layout() with { Width = width, Height = height, CatalogueCounts = new[] { definitions, 0, 0, 0, 0, 0, 0, 0 } };
        var p = new ParkSave(layout) { Paths = SaveFixture.Pattern(width * height, 11), Catalogue = SaveFixture.Pattern(definitions * 2, 13) };
        byte[] data = ParkSaveCodec.Write(p);
        int expected = 24 + ((width * height + 3) / 4 * 4) + 28 + 4 + 808 + 220 + ((2 * definitions + 16 + 3) / 4 * 4) + 1;
        Assert.Equal(expected, data.Length); SaveFixture.Equal(p, ParkSaveCodec.Read(data, layout));
    }

    // REJECTS getter/setter pairs that agree at the WRONG offset, wrong signedness, or wrong class selector.
    [Theory]
    [InlineData(1, 152, 148, 144)] [InlineData(6, 240, 148, 144)] [InlineData(3, 152, 148, 144)]
    [InlineData(7, 1048, 148, 144)] [InlineData(2, 16, 12, 13)] [InlineData(4, 28, 20, 21)] [InlineData(5, 28, 25, 27)]
    public void AttractionFieldOffsets(int type, int size, int definition, int status)
    {
        var a = new AttractionSave(type) { GuestsServed = -1234567, SavedType = 9, X = 201, Y = 143, Rotation = 3, Definition = 23, Status = 6 };
        var expected = new byte[size]; BinaryPrimitives.WriteInt32LittleEndian(expected, -1234567);
        expected[4] = 9; expected[5] = 201; expected[6] = 143; expected[7] = 3; expected[definition] = 23; expected[status] = 6;
        Assert.Equal(expected, a.Bytes);
        Assert.Equal(type is 1 or 3 or 6 or 7 ? 4 : 6, a.RestoredStatus);
        a.Status = 3; Assert.Equal(3, a.RestoredStatus); a.Status = 7; Assert.Equal(7, a.RestoredStatus);
        a.Status = 4; Assert.Equal(4, a.RestoredStatus); a.Status = 5; Assert.Equal(type is 1 or 3 or 6 or 7 ? 4 : 5, a.RestoredStatus);
    }

    // REJECTS treating coordinates as unsigned, strike as bit 0, and overwriting UNKNOWN staff +10..11.
    [Fact]
    public void StaffFieldsHaveIndependentWireValues()
    {
        var s = new StaffSave { X = -1250, Y = 2345, HireDay = -234567, Variant = 4, SkillAndStrike = 0x85, Left = 3, Top = 7, Right = 11, Bottom = 19 };
        byte[] expected = { 0x1E, 0xFB, 0x29, 0x09, 0xB9, 0x6B, 0xFC, 0xFF, 4, 0x85, 0, 0, 3, 7, 11, 19 };
        Assert.Equal(expected, s.Bytes); Assert.Equal(StaffState.Striking, s.RestoredState);
        s.SkillAndStrike = 5; Assert.Equal(StaffState.Idle, s.RestoredState);
    }

    // REJECTS shared wrong offsets, narrowing signed balance, or converting totals to tenths on the wire.
    [Fact]
    public void BankAndCalendarFieldsUseDocumentedOffsets()
    {
        var b = new BankSave { BalanceRaw = -1900123, MonthIndex = 37, LastYearIncomePounds = 101, LastYearSpendPounds = 203,
            ThisYearIncomePounds = 307, ThisYearSpendPounds = 409, SideshowTakingsPounds = 503, EntryTakingsPounds = 607,
            ShopProfitPounds = 709, WagesPounds = 811, SpendPounds = 907, IncomePounds = 1009, YearlyValuePounds = 1103, YearlyBalancePounds = -1201 };
        int[] expected = { -1900123, 37, 101, 203, 307, 409, 503, 607, 709, 811, 907, 1009, 1103, -1201 };
        for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], BinaryPrimitives.ReadInt32LittleEndian(b.Bytes.AsSpan(752 + 4 * i)));
        var c = new CalendarSave { TotalDays = 0xFEEDBEEF, Admissions = 0x8ABCDEF1, Year = 0x1234, TotalMonths = 0x5678,
            RatingAtNewYear = 63, StrikeBits = 21, MonthsInDebt = 3, Month = 7, Day = 23 };
        Assert.Equal(new byte[] { 0xEF, 0xBE, 0xED, 0xFE, 0xF1, 0xDE, 0xBC, 0x8A, 0x34, 0x12, 0x78, 0x56, 63 }, c.Bytes[20..33]);
        Assert.Equal(new byte[] { 21, 3, 7, 23 }, c.Bytes[38..42]);
    }
}
