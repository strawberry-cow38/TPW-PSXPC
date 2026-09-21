using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace TPW.Data;

/// <summary>READ: one compiled catalogue list, selected by world and park. Ordinary lists
/// contain FOLIO entry words (0x8006A3CC); type 8 contains entry/handle pairs
/// (0x80069790..D4). The second word is retained, not treated as another asset.
/// No attraction parameters or names are shipped in this decoder.</summary>
public sealed class PalParkCatalogue
{
    readonly uint[] words;
    public int Type { get; }
    public int FileOffset { get; }
    public int Stride => Type == 8 ? 8 : 4;
    public int Count => words.Length / (Stride / 4);
    public uint Entry(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        return words[index * (Stride / 4)];
    }

    internal PalParkCatalogue(int type, int fileOffset, ReadOnlySpan<byte> data)
    {
        Type = type; FileOffset = fileOffset;
        words = new uint[data.Length / 4];
        for (int i = 0; i < words.Length; i++)
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i * 4, 4));
    }

    /// <summary>READ: includes type-8 handle words. Encode every word, including unknown
    /// or nonzero handle values; no normalization of the original bytes.</summary>
    public byte[] Encode()
    {
        var result = new byte[words.Length * 4];
        for (int i = 0; i < words.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(i * 4, 4), words[i]);
        return result;
    }
}

/// <summary>READ: the PAL park definition is distributed through TPW.BIN and FOLIO.GAZ.
/// This decoder reads the compiled 52-byte objective record, ordered asset lists, and map pair.
/// Layout addresses/counts come from executing 0x8002ED50 and the 0x8006A214..364 getters;
/// tools/audit_scenario.py checks every list against those original instructions.
/// This is a fixed-layout SLES-026.88 decoder, not a general scenario format or a VM.
/// Pass CopyObjectiveRecord() to TPW.Sim.ParkObjectiveDefinition's existing constructor.
/// See findings/scenario.md, especially §0 and the unknown fields.</summary>
public sealed class PalParkDefinition
{
    public const uint ImageBase = 0x80010000;
    public const int ObjectiveRecordSize = 0x34; // READ: 0x80067CD8's eight return addresses.
    readonly byte[] objective;
    readonly PalParkCatalogue[] catalogues;
    public int World { get; }
    public int Park { get; }
    public int ObjectiveFileOffset => 0xD1930 + (World * 2 + Park) * ObjectiveRecordSize;
    public int MapPairFileOffset => 0xCDE88 + World * 0x10 + Park * 8;
    public uint MapEntry { get; }
    public uint SceneryEntry { get; }
    /// <summary>READ: overlay 11 0x8011715C/1A8 uses +0x31 as the park ticket total.
    /// This is an advertised total, not an additional award or completion condition.</summary>
    public byte AdvertisedGoldTickets => objective[0x31];
    public IReadOnlyList<PalParkCatalogue> Catalogues { get; }

    PalParkDefinition(ReadOnlySpan<byte> exe, int world, int park)
    {
        World = world; Park = park;
        objective = Slice(exe, ObjectiveFileOffset, ObjectiveRecordSize).ToArray();
        var map = Slice(exe, MapPairFileOffset, 8);
        MapEntry = BinaryPrimitives.ReadUInt32LittleEndian(map);
        SceneryEntry = BinaryPrimitives.ReadUInt32LittleEndian(map.Slice(4));
        var layouts = Layouts[world * 2 + park];
        catalogues = new PalParkCatalogue[Types.Length];
        for (int i = 0; i < Types.Length; i++)
        {
            var (address, count) = layouts[i];
            int offset = checked((int)(address - ImageBase));
            int size = count * (Types[i] == 8 ? 8 : 4);
            catalogues[i] = new PalParkCatalogue(Types[i], offset, Slice(exe, offset, size));
        }
        Catalogues = Array.AsReadOnly(catalogues);
    }

    /// <summary>READ: selector 0x80067CD8 returns null for every other world/park pair.
    /// A valid pair with truncated source bytes throws; it is not an empty catalogue.</summary>
    public static PalParkDefinition Read(ReadOnlySpan<byte> executable, int world, int park)
    {
        if ((uint)world >= 4 || (uint)park >= 2) return null;
        return new PalParkDefinition(executable, world, park);
    }

    public PalParkCatalogue Catalogue(int type)
    {
        for (int i = 0; i < catalogues.Length; i++)
            if (catalogues[i].Type == type) return catalogues[i];
        throw new ArgumentOutOfRangeException(nameof(type));
    }

    /// <summary>All 52 bytes, including the remaining 30 uninterpreted bytes. Independent copy.</summary>
    public byte[] CopyObjectiveRecord() => (byte[])objective.Clone();

    /// <summary>Re-encode the decoded regions into an independent copy of an executable.
    /// Unknown objective bytes, type-8 handle words, empty lists and list order survive.
    /// Other regions come from the supplied template, not from a retained executable buffer.</summary>
    public byte[] WriteToExecutable(ReadOnlySpan<byte> template)
    {
        // Validate every destination before writing any region.
        Slice(template, ObjectiveFileOffset, ObjectiveRecordSize);
        Slice(template, MapPairFileOffset, 8);
        foreach (var group in catalogues) Slice(template, group.FileOffset, group.Count * group.Stride);
        var result = template.ToArray();
        objective.CopyTo(result, ObjectiveFileOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(MapPairFileOffset), MapEntry);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(MapPairFileOffset + 4), SceneryEntry);
        foreach (var group in catalogues) group.Encode().CopyTo(result, group.FileOffset);
        return result;
    }

    static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> source, int offset, int count)
    {
        if (offset < 0 || offset > source.Length || count > source.Length - offset)
            throw new ArgumentException("Truncated PAL executable region.", nameof(source));
        return source.Slice(offset, count);
    }

    // READ: types from 0x80069A08; list offsets +08,+18,+28,+38,+48,+58,+68,+78
    // and count offsets eight bytes later, each indexed by park*4. Initializer:
    // world 0 0x8002ED88..EF40, 1 0x8002EF74..F138,
    // world 2 0x8002F168..F328, 3 0x8002F364..F538.
    // Addresses refer to source TPW.BIN data, NOT the constructed world objects in BSS.
    static readonly int[] Types = { 3, 7, 6, 8, 1, 2, 4, 5 };
    static readonly (uint Address, int Count)[][] Layouts =
    {
        new (uint, int)[] { (0x800F0CF0,8), (0x800F0D54,0), (0x80102888,1), (0x800F0D44,2), (0x80102880,1), (0x800F0D10,13), (0x800F0CCC,6), (0x800F0CE4,3) },
        new (uint, int)[] { (0x800F0D54,6), (0x80102898,1), (0x801028A4,1), (0x801028AC,1), (0x801028B8,2), (0x800F0D6C,13), (0x800F0DA0,6), (0x800F0DB8,3) },
        new (uint, int)[] { (0x800F0DC4,8), (0x801028C8,1), (0x801028D0,1), (0x801028D8,1), (0x801028E4,1), (0x800F0DE4,14), (0x800F0E1C,6), (0x800F0E34,3) },
        new (uint, int)[] { (0x800F0E40,6), (0x800F0E58,0), (0x801028F4,1), (0x800F0E58,2), (0x80102900,2), (0x800F0E68,14), (0x800F0EA0,6), (0x800F0EB8,3) },
        new (uint, int)[] { (0x800F0EC4,7), (0x800F0EE0,0), (0x80102914,1), (0x8010291C,1), (0x80102928,2), (0x800F0EE0,12), (0x800F0F10,6), (0x800F0F28,3) },
        new (uint, int)[] { (0x800F0F34,6), (0x80102938,1), (0x80102940,1), (0x800F0F4C,2), (0x8010294C,1), (0x800F0F5C,12), (0x800F0F8C,6), (0x800F0FA4,3) },
        new (uint, int)[] { (0x800F0FB0,7), (0x800F0FCC,0), (0x8010295C,1), (0x80102980,1), (0x80102964,1), (0x800F0FCC,13), (0x800F1000,6), (0x800F1018,3) },
        new (uint, int)[] { (0x800F1024,6), (0x80102970,1), (0x80102978,1), (0x800F103C,0), (0x80102990,2), (0x800F103C,12), (0x800F106C,6), (0x800F1084,3) },
    };
}

/// <summary>READ: executable defaults, separate from objective records and card state.
/// AllResearchUnlocked can subsequently change via the pad matcher 0x8006E22C;
/// RestrictedMode is live state restored from the inverted card-header byte +0x2A.
/// §0 retains the existing sandbox contract despite an additional overlay caller.</summary>
public readonly record struct PalParkDefaults(int StartingMoneyPounds, int EntryFeePounds,
    bool AllResearchUnlocked, bool RestrictedMode)
{
    public static PalParkDefaults Read(ReadOnlySpan<byte> executable)
        => new(Immediate(executable, 0x80058AA0, 0x3405), // ori a1,zero,50000
               Immediate(executable, 0x800865D4, 0x2405), // addiu a1,zero,40
               Word(executable, 0x80102E88) != 0, Word(executable, 0x80102D34) != 0);

    static uint Word(ReadOnlySpan<byte> data, uint address)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(checked((int)(address - PalParkDefinition.ImageBase)), 4));
    static int Immediate(ReadOnlySpan<byte> data, uint address, uint instructionHigh)
    {
        uint word = Word(data, address);
        if (word >> 16 != instructionHigh) throw new ArgumentException("Unrecognized PAL initialization instruction.");
        return instructionHigh == 0x2405 ? unchecked((short)word) : (int)(word & 0xFFFF);
    }
}
