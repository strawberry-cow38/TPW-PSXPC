using System;
using System.Buffers.Binary;

namespace TPW.Sim;

/// <summary>READ: eight compiled 0x34-byte records selected by 0x80067CD8(world,park).
/// This is not a discovered scenario file format. The constructor accepts an already decoded record;
/// unknown fields remain opaque and survive CopyRecord. No research or entry-fee flag is inferred.</summary>
public sealed class ParkObjectiveDefinition
{
    public const int RecordSize = 0x34; // READ: 0x800E1930,1964,1998,19CC,1A00,1A34,1A68,1A9C.
    readonly byte[] bytes;
    public ParkObjectiveDefinition(ReadOnlySpan<byte> record)
    {
        if (record.Length != RecordSize) throw new ArgumentException("Expected the 52-byte compiled park record.", nameof(record));
        bytes = record.ToArray();
    }
    public byte[] CopyRecord() => (byte[])bytes.Clone();
    uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    public uint AdmissionsThreshold => Word(0x0C); // READ: 0x80067980.
    public int ProfitPounds => unchecked((int)Word(0x10)); // READ: 0x800679C8, 0x800694C0.
    public uint YearsOpen => Word(0x14); // READ: 0x80067A60.
    public uint FeatureValuePounds => Word(0x18); // READ: 0x80067BF0.
    public uint MaximumPathTiles => Word(0x1C); // READ: 0x80067C8C.
    public bool TutorialAwardEnabled => bytes[0x30] != 0; // READ: 0x80067A90.

    /// <summary>READ: 0x80067CD8 returns null outside world 0..3 / park 0..1.
    /// Returned owners have independent byte storage. Loader may supply another decoded record
    /// via the constructor without changing the evaluator.</summary>
    public static ParkObjectiveDefinition ForPalPark(int world, int park)
    {
        if ((uint)world >= 4 || (uint)park >= 2) return null;
        return new ParkObjectiveDefinition(Convert.FromHexString(Records[world * 2 + park]));
    }
    // READ: complete records, including uninterpreted fields. Generated from TPW.BIN.
    static readonly string[] Records =
    {
        "E3000000EC0000000300000064000000D007000001000000D007000064000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF01070000", // 0x800E1930
        "D1000000EC00000005000000C8000000B80B000002000000D007000064000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00060000", // 0x800E1964
        "83000000A00000000500000096000000C409000001000000D007000064000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00050000", // 0x800E1998
        "7C0000009700000005000000FA000000B80B000002000000D007000064000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00060000", // 0x800E19CC
        "280000003C0000000300000096000000C409000001000000D007000064000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00050000", // 0x800E1A00
        "370000004100000003000000F40100008813000005000000D007000064000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00060000", // 0x800E1A34
        "6D0100007E01000003000000FA000000B80B000002000000D007000064000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00050000", // 0x800E1A68
        "6C0100008001000003000000F40100008813000005000000D007000064000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00050000", // 0x800E1A9C
    };
}
