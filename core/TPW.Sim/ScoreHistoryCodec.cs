using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TPW.Sim.Tests")]

namespace TPW.Sim;

/// <summary>READ: lossy history packing 0x80068740 / 0x800879DC and expansion
/// 0x80068A54 / 0x80087F90. Internal because each owner supplies the adjacent bytes too.
/// Twelve exact months, six pairs, eight six-month groups, nine eight-month groups = 144.
/// The oldest group's interpolation reads one byte PAST the group data: next calendar row
/// (or calendar padding), and money-record padding. ⚠ DO NOT FIX or silently supply zero.</summary>
internal static class ScoreHistoryCodec
{
    internal const int ByteRecordSize = 35; // 0x800689F4..8A1C, stride 0x23
    internal const int MoneyRecordSize = 80; // 0x80087E04..38, stride 0x50
    // READ: group counts/widths 0x80068790, 68810, 68894, 68918; money 87B8C..87DAC.
    static readonly (int Count, int Width)[] Groups = { (6, 2), (8, 6), (9, 8) };
    const int Recent = 12;
    const int Scale = 255; // 0x80087C18..20; signed /255 on restore at 0x80088114..28
    static int Slot(int months, int age) => (int)(((long)months + 2 * ParkHistory.Slots - age) % ParkHistory.Slots);

    internal static void PackBytes(byte[] ring, int months, Span<byte> output)
    {
        int age = 1, at = 0;
        for (; at < Recent; at++, age++) output[at] = ring[Slot(months, age)];
        foreach (var group in Groups)
            for (int i = 0; i < group.Count; i++)
            {
                int sum = 0;
                for (int j = 0; j < group.Width; j++, age++) sum += ring[Slot(months, age)];
                output[at++] = (byte)(sum / group.Width);
            }
    }

    internal static void UnpackBytes(ReadOnlySpan<byte> input, int months, byte[] ring)
    {
        int age = 1, at = 0;
        for (; at < Recent; at++, age++) ring[Slot(months, age)] = input[at];
        foreach (var group in Groups)
            for (int i = 0; i < group.Count; i++, at++)
                for (int j = 0; j < group.Width; j++, age++)
                {
                    // READ 0x80068BE4..C2C: six-month subslot 1 uses (6*a)/6, NOT (5*a+b)/6.
                    int nextWeight = group.Width == 6 && j == 1 ? 0 : j;
                    ring[Slot(months, age)] = (byte)(((group.Width - nextWeight) * input[at]
                        + nextWeight * input[at + 1]) / group.Width);
                }
    }

    internal static void PackMoney(int[] ring, int months, Span<byte> output)
    {
        // READ 0x800879E4..87B2C: excludes raw ages >= monthIndex and age 144 from extrema,
        // yet packs ALL physical ring cells, including cells never written this session.
        int end = Math.Min(143, months - 1), min = 0, max = 0;
        if (end > Recent) min = max = ring[Slot(months, Recent + 1)] / 10;
        for (int i = Recent; i < end; i++)
        {
            int pounds = ring[Slot(months, i + 1)] / 10;
            min = Math.Min(min, pounds);
            max = Math.Max(max, pounds);
        }
        BinaryPrimitives.WriteInt32LittleEndian(output, min);
        BinaryPrimitives.WriteInt32LittleEndian(output.Slice(4), max);
        uint range = unchecked((uint)(max - min));
        if (range == 0) range = 1; // 0x80087B28..30
        int age = 1, at = 8;       // 0x80087B40
        for (int i = 0; i < Recent; i++, age++, at += 4)
            BinaryPrimitives.WriteInt32LittleEndian(output.Slice(at), ring[Slot(months, age)]);
        foreach (var group in Groups)
            for (int i = 0; i < group.Count; i++)
            {
                uint sum = 0;
                for (int j = 0; j < group.Width; j++, age++)
                    sum = unchecked(sum + (uint)(ring[Slot(months, age)] / 10));
                // READ 0x80087C10..30 / CC8..CF8 / D80..DA0: unsigned mean AND division.
                // Negative balances deliberately retain their surprising low-byte result.
                uint normalized = unchecked((sum / (uint)group.Width - (uint)min) * Scale) / range;
                output[at++] = unchecked((byte)normalized);
            }
        // +0x4F is untouched; decoder reads it as its final neighbor.
    }

    internal static void UnpackMoney(ReadOnlySpan<byte> input, int months, int[] ring)
    {
        int min = BinaryPrimitives.ReadInt32LittleEndian(input);
        int range = unchecked(BinaryPrimitives.ReadInt32LittleEndian(input.Slice(4)) - min);
        int age = 1, at = 8;
        for (int i = 0; i < Recent; i++, age++, at += 4)
            ring[Slot(months, age)] = BinaryPrimitives.ReadInt32LittleEndian(input.Slice(at));
        foreach (var group in Groups)
            for (int i = 0; i < group.Count; i++, at++)
            {
                // READ 0x800880C4..88128: low-word product, signed /255, then add min.
                int a = unchecked(range * input[at] / Scale + min);
                int b = unchecked(range * input[at + 1] / Scale + min);
                for (int j = 0; j < group.Width; j++, age++)
                {
                    int pounds;
                    // READ 0x800881B8..C0: pair midpoint rounds negative DOWN via sra.
                    if (j == 0) pounds = a;
                    else if (group.Width == 2 && j == 1) pounds = unchecked(a + b) >> 1;
                    // READ 0x800883DC..E8 / 88800..04: middle is (a+b)/2 BEFORE scaling.
                    else if (j == group.Width / 2) pounds = unchecked(a + b) / 2;
                    else pounds = unchecked((group.Width - j) * a + j * b) / group.Width;
                    ring[Slot(months, age)] = unchecked(pounds * 10); // 0x80088E50
                }
            }
    }
}
