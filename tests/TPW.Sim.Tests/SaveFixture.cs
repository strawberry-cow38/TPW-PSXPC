using System;
using System.Collections.Generic;
using System.Linq;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests;

internal static class SaveFixture
{
    internal static byte[] Pattern(int length, int seed) => Enumerable.Range(0, length).Select(i => (byte)(1 + (i * 37 + seed) % 251)).ToArray();
    internal static ParkSaveLayout Layout(bool restricted = false) => new(2, 1, 5, 3, restricted, new[] { 2, 1, 2, 3, 2, 1, 2, 1 });
    internal static ParkSave Park(bool restricted = false)
    {
        var p = new ParkSave(Layout(restricted))
        {
            Marker = 0xA1B2, UnknownHeader17 = 0xE7, Open = 3, UnknownGate1 = 0xED, EntryFeePounds = 0x1234,
            Paths = Pattern(15, 12), VisitorCount = 3, Visitors = new(Pattern(26, 40)),
            OrdinaryLitter = 7, Vomit = 3, Bank = new(Pattern(0x328, 19)), Calendar = new(Pattern(0xDC, 53)),
            Catalogue = restricted ? Array.Empty<byte>() : Pattern(28, 61),
            ResearchTopics = restricted ? Array.Empty<byte>() : Pattern(16, 87)
        };
        int[] types = { 1, 6, 3, 7, 2, 4, 5 }, sizes = { 152, 240, 152, 1048, 16, 28, 28 }, counts = { 2, 1, 1, 1, 2, 1, 1 };
        for (int i = 0; i < 7; i++) for (int j = 0; j < counts[i]; j++)
            p.Attractions[i].Add(new(types[i], Pattern(sizes[i], 101 + 11 * i + 5 * j)));
        int[] staffCounts = { 1, 2, 1, 1, 2 };
        for (int i = 0; i < 5; i++) for (int j = 0; j < staffCounts[i]; j++) p.Staff[i].Add(new(Pattern(16, 44 + i * 9 + j * 21)));
        p.Messages.Add(new(0x3456, Array.Empty<byte>(), 1, -1, 0));
        p.Messages.Add(new(-1, new byte[] { 0x41, 0x80, 0xE3 }, 2, 6, 19));
        p.Messages.Add(new(-1, new byte[] { 0xD7, 0x29 }, 3, -1, 0));
        p.Messages.Add(new(0x2345, Array.Empty<byte>(), 2, 7, 23));
        return p;
    }

    // Independent byte fixture: literal header, literal block ordering/lengths and explicit alignment.
    // No production serializer, size table, property-offset getter, or enum feeds expected bytes.
    internal static byte[] Golden(bool restricted = false, bool patternedPadding = false)
    {
        var bytes = new List<byte>(); int pad = 0;
        void Add(int size, int seed) => bytes.AddRange(Pattern(size, seed));
        void Align(int n) { while (bytes.Count % n != 0) bytes.Add(patternedPadding ? (byte)(0xA0 + pad++) : (byte)0); }
        bytes.AddRange(new byte[] { 0xB2, 0xA1, 2, 1, 2, 1, 1, 1, 2, 1, 1, 1, 2, 1, 1, 2, 3, 0xE7 }); Align(4);
        bytes.AddRange(new byte[] { 3, 0xED, 0x34, 0x12 }); Add(15, 12); Align(4);
        Add(152, 101); Add(152, 106); Add(240, 112); Add(152, 123); Add(1048, 134);
        Add(16, 145); Add(16, 150); Add(28, 156); Add(28, 167);
        Add(26, 40); Align(4);
        Add(16, 44); Add(16, 53); Add(16, 74); Add(16, 62); Add(16, 71); Add(16, 80); Add(16, 101);
        bytes.AddRange(new byte[] { 7, 3 }); Align(4);
        Add(808, 19); Add(220, 53);
        if (!restricted) { Add(28, 61); Add(16, 87); Align(4); }
        bytes.Add(4); Align(2); bytes.AddRange(new byte[] { 0x56, 0x34, 1, 0xFF });
        Align(2); bytes.AddRange(new byte[] { 0xFF, 0xFF, 3, 0x41, 0x80, 0xE3, 2, 6, 19 });
        Align(2); bytes.AddRange(new byte[] { 0xFF, 0xFF, 2, 0xD7, 0x29, 3, 0xFF });
        Align(2); bytes.AddRange(new byte[] { 0x45, 0x23, 2, 7, 23 });
        return bytes.ToArray();
    }

    internal static void Equal(ParkSave a, ParkSave b)
    {
        Assert.Equal(a.Layout, b.Layout); Assert.Equal(a.Marker, b.Marker); Assert.Equal(a.UnknownHeader17, b.UnknownHeader17);
        Assert.Equal(a.Open, b.Open); Assert.Equal(a.UnknownGate1, b.UnknownGate1); Assert.Equal(a.EntryFeePounds, b.EntryFeePounds);
        Assert.Equal(a.Paths, b.Paths); Assert.Equal(a.VisitorCount, b.VisitorCount);
        for (int i = 0; i < 12; i++) Assert.Equal(a.Visitors.Get(i), b.Visitors.Get(i));
        for (int i = 0; i < 7; i++)
        {
            Assert.Equal(a.Attractions[i].Count, b.Attractions[i].Count);
            for (int j = 0; j < a.Attractions[i].Count; j++)
            {
                var x = a.Attractions[i][j]; var y = b.Attractions[i][j];
                Assert.Equal(x.Type, y.Type); Assert.Equal(x.GuestsServed, y.GuestsServed); Assert.Equal(x.SavedType, y.SavedType);
                Assert.Equal(x.X, y.X); Assert.Equal(x.Y, y.Y); Assert.Equal(x.Rotation, y.Rotation);
                Assert.Equal(x.Definition, y.Definition); Assert.Equal(x.Status, y.Status);
                if (x.Type is 1 or 3 or 6 or 7)
                {
                    Assert.Equal(x.PlacementDay, y.PlacementDay); Assert.Equal(x.Sliders, y.Sliders);
                    Assert.Equal(x.ReliabilityFixed, y.ReliabilityFixed); Assert.Equal(x.Level, y.Level);
                }
                Assert.Equal(x.Bytes, y.Bytes); // Every named extension AND UNKNOWN field, not just position.
            }
        }
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(a.Staff[i].Count, b.Staff[i].Count);
            for (int j = 0; j < a.Staff[i].Count; j++)
            {
                var x = a.Staff[i][j]; var y = b.Staff[i][j];
                Assert.Equal(x.X, y.X); Assert.Equal(x.Y, y.Y); Assert.Equal(x.HireDay, y.HireDay);
                Assert.Equal(x.Variant, y.Variant); Assert.Equal(x.SkillAndStrike, y.SkillAndStrike);
                Assert.Equal(x.Left, y.Left); Assert.Equal(x.Top, y.Top); Assert.Equal(x.Right, y.Right); Assert.Equal(x.Bottom, y.Bottom);
                Assert.Equal(x.Bytes, y.Bytes);
            }
        }
        Assert.Equal(a.OrdinaryLitter, b.OrdinaryLitter); Assert.Equal(a.Vomit, b.Vomit);
        Assert.Equal(a.Bank.BalanceRaw, b.Bank.BalanceRaw); Assert.Equal(a.Bank.MonthIndex, b.Bank.MonthIndex);
        Assert.Equal(a.Bank.LastYearIncomePounds, b.Bank.LastYearIncomePounds); Assert.Equal(a.Bank.LastYearSpendPounds, b.Bank.LastYearSpendPounds);
        Assert.Equal(a.Bank.ThisYearIncomePounds, b.Bank.ThisYearIncomePounds); Assert.Equal(a.Bank.ThisYearSpendPounds, b.Bank.ThisYearSpendPounds);
        Assert.Equal(a.Bank.SideshowTakingsPounds, b.Bank.SideshowTakingsPounds); Assert.Equal(a.Bank.EntryTakingsPounds, b.Bank.EntryTakingsPounds);
        Assert.Equal(a.Bank.ShopProfitPounds, b.Bank.ShopProfitPounds); Assert.Equal(a.Bank.WagesPounds, b.Bank.WagesPounds);
        Assert.Equal(a.Bank.SpendPounds, b.Bank.SpendPounds); Assert.Equal(a.Bank.IncomePounds, b.Bank.IncomePounds);
        Assert.Equal(a.Bank.YearlyValuePounds, b.Bank.YearlyValuePounds); Assert.Equal(a.Bank.YearlyBalancePounds, b.Bank.YearlyBalancePounds);
        Assert.Equal(a.Bank.Bytes, b.Bank.Bytes);
        Assert.Equal(a.Calendar.TotalDays, b.Calendar.TotalDays); Assert.Equal(a.Calendar.Admissions, b.Calendar.Admissions);
        Assert.Equal(a.Calendar.Year, b.Calendar.Year); Assert.Equal(a.Calendar.TotalMonths, b.Calendar.TotalMonths);
        Assert.Equal(a.Calendar.RatingAtNewYear, b.Calendar.RatingAtNewYear); Assert.Equal(a.Calendar.StrikeBits, b.Calendar.StrikeBits);
        Assert.Equal(a.Calendar.MonthsInDebt, b.Calendar.MonthsInDebt); Assert.Equal(a.Calendar.Month, b.Calendar.Month); Assert.Equal(a.Calendar.Day, b.Calendar.Day);
        Assert.Equal(a.Calendar.Bytes, b.Calendar.Bytes); Assert.Equal(a.Catalogue, b.Catalogue); Assert.Equal(a.ResearchTopics, b.ResearchTopics);
        Assert.Equal(a.Messages.Count, b.Messages.Count);
        for (int i = 0; i < a.Messages.Count; i++)
        {
            var x = a.Messages[i]; var y = b.Messages[i];
            Assert.Equal(x.StringId, y.StringId); Assert.Equal(x.Text, y.Text); Assert.Equal(x.Type, y.Type);
            Assert.Equal(x.TargetType, y.TargetType); Assert.Equal(x.TargetIndex, y.TargetIndex);
        }
    }
}
