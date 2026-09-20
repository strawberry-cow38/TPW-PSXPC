using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace TPW.Sim;

/// <summary>Byte-exact record with named READ fields and preserved UNKNOWN storage. All numeric
/// offsets below are from the cited writer/reader, not runtime struct offsets. See save.md §3.</summary>
public abstract class SaveRecord
{
    public byte[] Bytes { get; }
    protected SaveRecord(int size, byte[] bytes)
    {
        if (bytes != null && bytes.Length != size) throw new ArgumentException("Incorrect record size.");
        Bytes = bytes == null ? new byte[size] : (byte[])bytes.Clone();
    }
    protected ushort U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(offset));
    protected void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(offset), value);
    protected int I32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(offset));
    protected void I32(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(offset), value);
}

/// <summary>READ: shared attraction record 0x80062A60, extensions 0x8009CDD8, 0x800A00BC,
/// 0x800A6D5C, 0x800A1020, 0x800ADB20, 0x80023D28, 0x800B6AE8, 0x800B7330.
/// Host supplies definition assets and rebuilds geometry; Bytes retains every class-specific field.</summary>
public sealed class AttractionSave : SaveRecord
{
    public static IReadOnlyList<int> Types { get; } = Array.AsReadOnly(new[] { 1, 6, 3, 7, 2, 4, 5 });
    public static IReadOnlyList<int> Sizes { get; } = Array.AsReadOnly(new[] { 0x98, 0xF0, 0x98, 0x418, 0x10, 0x1C, 0x1C });
    public int Type { get; }
    static int Index(int type)
    {
        for (int i = 0; i < Types.Count; i++) if (Types[i] == type) return i;
        throw new ArgumentException("Unknown saved attraction class.");
    }
    public AttractionSave(int type, byte[] bytes = null) : base(Sizes[Index(type)], bytes) { Type = type; }
    /// <summary>READ: A+0x14 guests served, saved at +0 by 0x80062ACC; rides.md §5.</summary>
    public int GuestsServed { get => I32(0); set => I32(0, value); }
    public byte SavedType { get => Bytes[4]; set => Bytes[4] = value; }
    public byte X { get => Bytes[5]; set => Bytes[5] = value; }
    public byte Y { get => Bytes[6]; set => Bytes[6] = value; }
    public byte Rotation { get => Bytes[7]; set => Bytes[7] = value; }
    // READ: definition selectors consumed by 0x80071FEC..72460.
    int DefinitionOffset => Type == 2 ? 12 : Type == 4 ? 20 : Type == 5 ? 25 : 148;
    int StatusOffset => Type == 2 ? 13 : Type == 4 ? 21 : Type == 5 ? 27 : 144;
    public byte Definition { get => Bytes[DefinitionOffset]; set => Bytes[DefinitionOffset] = value; }
    public byte Status { get => Bytes[StatusOffset]; set => Bytes[StatusOffset] = value; }
    void RequireRide()
    {
        if (Type is not (1 or 3 or 6 or 7)) throw new InvalidOperationException("This record is not a ride.");
    }
    /// <summary>READ: 0x8009CE30 / CFD0; rides.md and ride-panel.md identify save+0x88
    /// as placement day. Retain the established no-ticket rule; see save.md §0.</summary>
    public ushort PlacementDay
    {
        get { RequireRide(); return U16(0x88); }
        set { RequireRide(); U16(0x88, value); }
    }
    /// <summary>READ: 0x8009CE0C..2C. Use existing RidePanel.SaveSliders / RestoreSliders.
    /// These raw values are NOT passed through the UI clamp (ride-panel.md §1).</summary>
    public RideSliderSave Sliders
    {
        get { RequireRide(); return new(U16(0x8A), Bytes[0x8C], Bytes[0x8D]); }
        set { RequireRide(); U16(0x8A, value.Speed); Bytes[0x8C] = value.Capacity; Bytes[0x8D] = value.Duration; }
    }
    /// <summary>READ: byte save+0x8F, 0x8009CE5C..68, restored with <<12 at 0x8009CFD4.
    /// ⚠ DO NOT FIX: fractional reliability is lost and no slider/stat clamp applies.</summary>
    public int ReliabilityFixed
    {
        get { RequireRide(); return Bytes[0x8F] << 12; }
        set { RequireRide(); Bytes[0x8F] = unchecked((byte)(value >> 12)); }
    }
    /// <summary>READ: save+0x92 to A+0xF6, 0x8009CE6C / CFEC.</summary>
    public byte Level
    {
        get { RequireRide(); return Bytes[0x92]; }
        set { RequireRide(); Bytes[0x92] = value; }
    }
    /// <summary>READ: 0x8009D0A0..D8 collapses ride statuses 4,5,6 to 4 after setup.</summary>
    public byte RestoredStatus => Type is 1 or 3 or 6 or 7 && Status >= 4 && Status <= 6 ? (byte)4 : Status;
}

/// <summary>READ: 16-byte staff record at 0x80094BBC / 0x80094C64. Position survives;
/// tiredness, morale, jobs, paths and claims do not. +10..11 UNKNOWN (2 bytes).</summary>
public sealed class StaffSave : SaveRecord
{
    public StaffSave(byte[] bytes = null) : base(16, bytes) { }
    public short X { get => unchecked((short)U16(0)); set => U16(0, unchecked((ushort)value)); }
    public short Y { get => unchecked((short)U16(2)); set => U16(2, unchecked((ushort)value)); }
    /// <summary>READ: S+0x34 hired day, save +4; staff.md §1.2, 0x800942B0..C8 / 0x80094C4C.</summary>
    public int HireDay { get => I32(4); set => I32(4, value); }
    /// <summary>READ: recruit variant, S+0x3D through slots 53/52 (0x80095728 / 734).
    /// Save +8 supplies the allocator's variant argument at 0x80072544..48.</summary>
    public byte Variant { get => Bytes[8]; set => Bytes[8] = value; }
    /// <summary>READ: low 3 bits skill (0x800956F0; wages.md), bit 7 striking (0x80094C04..18);
    /// other bits retained UNKNOWN. This is not the variant/appearance selector.</summary>
    public byte SkillAndStrike { get => Bytes[9]; set => Bytes[9] = value; }
    public byte Left { get => Bytes[12]; set => Bytes[12] = value; }
    public byte Top { get => Bytes[13]; set => Bytes[13] = value; }
    public byte Right { get => Bytes[14]; set => Bytes[14] = value; }
    public byte Bottom { get => Bytes[15]; set => Bytes[15] = value; }
    public StaffState RestoredState => (SkillAndStrike & 0x80) != 0 ? StaffState.Striking : StaffState.Idle;
}

/// <summary>READ: 0x328 bytes, 0x80087DEC / 0x80088A0C. Eight compressed history records
/// (+0..27F, 0x50 each), four loan records (+280..2EF, 0x1C each), then current totals.
/// ParkScore can capture/restore the lossy histories and totals; the host still supplies balance
/// and loan slots. No exact 144-month snapshot is claimed. Loan +25..27 are UNKNOWN padding.</summary>
public sealed class BankSave : SaveRecord
{
    public BankSave(byte[] bytes = null) : base(0x328, bytes) { }
    /// <summary>READ: raw tenths, getter 0x80088E44, unlike the whole-pound totals below.</summary>
    public int BalanceRaw { get => I32(0x2F0); set => I32(0x2F0, value); }
    public int MonthIndex { get => I32(0x2F4); set => I32(0x2F4, value); }
    // READ: source BANK offsets +12E0,+12E8,+12DC,+12E4,+12C4..12D8,+12EC,+12F0.
    public int LastYearIncomePounds { get => I32(0x2F8); set => I32(0x2F8, value); }
    public int LastYearSpendPounds { get => I32(0x2FC); set => I32(0x2FC, value); }
    public int ThisYearIncomePounds { get => I32(0x300); set => I32(0x300, value); }
    public int ThisYearSpendPounds { get => I32(0x304); set => I32(0x304, value); }
    public int SideshowTakingsPounds { get => I32(0x308); set => I32(0x308, value); }
    public int EntryTakingsPounds { get => I32(0x30C); set => I32(0x30C, value); }
    public int ShopProfitPounds { get => I32(0x310); set => I32(0x310, value); }
    public int WagesPounds { get => I32(0x314); set => I32(0x314, value); }
    public int SpendPounds { get => I32(0x318); set => I32(0x318, value); }
    public int IncomePounds { get => I32(0x31C); set => I32(0x31C, value); }
    public int YearlyValuePounds { get => I32(0x320); set => I32(0x320, value); }
    public int YearlyBalancePounds { get => I32(0x324); set => I32(0x324, value); }
}

/// <summary>READ: 0xDC-byte McAi record, 0x8006892C / 0x80069034. Five staff strike/deadline
/// words at +0, five class bytes +33, strike bits +38, then five 35-byte histories +43.
/// UNKNOWN: +42 and +218..219 padding; class words/bytes retain their raw bits.</summary>
public sealed class CalendarSave : SaveRecord
{
    public CalendarSave(byte[] bytes = null) : base(0xDC, bytes) { }
    public uint TotalDays { get => unchecked((uint)I32(20)); set => I32(20, unchecked((int)value)); }
    /// <summary>READ: McAi+0x1C, saved by 0x80068988 and read by 0x80069308 for the
    /// "People Visited" display (statistics.md §6); successful state-37 admissions increment it.</summary>
    public uint Admissions { get => unchecked((uint)I32(24)); set => I32(24, unchecked((int)value)); }
    public ushort Year { get => U16(28); set => U16(28, value); }
    public ushort TotalMonths { get => U16(30); set => U16(30, value); }
    public byte RatingAtNewYear { get => Bytes[32]; set => Bytes[32] = value; }
    public byte StrikeBits { get => Bytes[38]; set => Bytes[38] = value; }
    public byte MonthsInDebt { get => Bytes[39]; set => Bytes[39] = value; }
    public byte Month { get => Bytes[40]; set => Bytes[40] = value; }
    public byte Day { get => Bytes[41]; set => Bytes[41] = value; }
}
