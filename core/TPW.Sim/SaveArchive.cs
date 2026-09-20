using System;
using System.Buffers.Binary;
using System.Linq;

namespace TPW.Sim;

/// <summary>Optional host compression for the original UNPAK packets. READ: 0x8006C994 / CA44.
/// Encode returns a headerless stream accepted by 0x80018EF0; Decode must enforce output size.
/// TPW.Data.Unpak already provides decoding. No PSX card I/O is required by this interface.</summary>
public interface ISaveCompression
{
    byte[] Encode(ReadOnlySpan<byte> unpacked);
    byte[] Decode(ReadOnlySpan<byte> packed, int unpackedSize);
}

public static class SavePacket
{
    /// <summary>READ: +0 u32 unpacked bytes, +4 u32 total packed bytes INCLUDING this 8-byte header.</summary>
    public static byte[] Pack(ReadOnlySpan<byte> unpacked, ISaveCompression compression)
    {
        byte[] body = compression.Encode(unpacked);
        var packet = new byte[checked(body.Length + 8)];
        BinaryPrimitives.WriteInt32LittleEndian(packet, unpacked.Length);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), packet.Length);
        body.CopyTo(packet, 8); return packet;
    }
    public static byte[] Unpack(ReadOnlySpan<byte> packet, ISaveCompression compression)
    {
        Validate(packet);
        int size = BinaryPrimitives.ReadInt32LittleEndian(packet);
        byte[] result = compression.Decode(packet.Slice(8), size);
        if (result.Length != size) throw new FormatException("UNPAK output size differs from packet header.");
        return result;
    }
    internal static void Validate(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 8 || BinaryPrimitives.ReadInt32LittleEndian(packet) < 0 ||
            BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(4)) != packet.Length)
            throw new FormatException("Invalid packed save header.");
    }
}

/// <summary>READ: card file container, 0x8006C0E8 / 0x8006C524. Exactly 0xA000 bytes (five
/// 0x2000-byte card blocks), with at most three saved parks, among four worlds with two parks each.
/// This class handles bytes only. The host supplies card title/icon bytes if exporting to PSX.</summary>
public sealed class SaveArchive
{
    public const int FileSize = 0xA000; // READ: 0x8006C33C, C368.
    internal byte[] Background { get; set; } = new byte[FileSize];
    public byte[] CardHeader { get; set; } = new byte[0x200];
    /// <summary>READ: 96-byte application header at file +0x200; UNKNOWN bytes retained verbatim.
    /// Offsets/length/checksum/magic/version are rebuilt by Write. Field table in save.md §2.</summary>
    public byte[] Header { get; set; } = new byte[0x60];
    /// <summary>READ: +0x260, four sound volumes (channels 0,1,3,4), UNKNOWN boolean, UNKNOWN/pad,
    /// signed screen X and Y. 0x8006C2F0..350 / C704..750.</summary>
    public byte[] Settings { get; set; } = new byte[10];
    /// <summary>READ: GENDATA is a separate packed stream. Its first 25 unpacked bytes preserve
    /// advisor message-suppression flags (0x800177AC / 0x8001773C); see save.md §2 for all 32 bytes.</summary>
    public byte[] GeneralData { get; set; } = Array.Empty<byte>();
    /// <summary>READ: world-major eight slots, only status exactly F0 is restored. Null means absent.
    /// Each non-null entry includes its 8-byte packed header.</summary>
    public byte[][] ParkPackets { get; } = new byte[8][];
    /// <summary>READ: loader 0x8006C5C0..CC stores (header[42] == 0) to runtime mode.
    /// A NONZERO runtime mode is restricted. Keep parkopen.md §2.4's explicit inversion.</summary>
    public bool Restricted { get => Header[42] == 0; set => Header[42] = value ? (byte)0 : (byte)1; }
    public byte GetParkStatus(int slot) => Header[43 + slot];
    public void SetParkStatus(int slot, byte status) => Header[43 + slot] = status;
}

public static class SaveArchiveCodec
{
    const int HeaderAt = 0x200, DataAt = 0x26A, ChecksumAt = 0x9FFC; // READ: 0x8006C0E8 / C524.
    const uint Magic = 0x47415901; // READ: 0x8006C598..5A4.
    const ushort Version = 0xAC; // READ: 0x8006C5A8..5B4.
    static uint U32(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    static ushort U16(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
    static void Put32(byte[] bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
    static void Put16(byte[] bytes, int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
    static void FixedCopy(byte[] source, byte[] target, int at, int size)
    {
        if (source == null || source.Length != size) throw new ArgumentException("Incorrect archive field size.");
        source.CopyTo(target, at);
    }

    /// <summary>READ: 0x800708A4 / 0x800708EC sums little-endian words modulo 2^32.
    /// ⚠ DO NOT FIX: length is the absolute payload END, yet summing starts at file +0x204!
    /// Round the range up to words and treat its final partial word's unused bytes as zero.</summary>
    public static uint Checksum(ReadOnlySpan<byte> file, int length)
    {
        if (length < 0 || (long)HeaderAt + 4 + ((long)length + 3 & ~3L) > ChecksumAt)
            throw new FormatException("Save checksum range exceeds the container.");
        if (file.Length != SaveArchive.FileSize) throw new FormatException("Save archive must occupy five card blocks.");
        uint sum = 0;
        for (int i = 0; i < length; i += 4)
        {
            uint word = 0;
            for (int b = 0; b < 4 && i + b < length; b++) word |= (uint)file[HeaderAt + 4 + i + b] << (8 * b);
            sum = unchecked(sum + word);
        }
        return sum;
    }

    public static byte[] Write(SaveArchive save)
    {
        byte[] file = (byte[])save.Background.Clone();
        FixedCopy(save.CardHeader, file, 0, 0x200);
        FixedCopy(save.Header, file, HeaderAt, 0x60);
        FixedCopy(save.Settings, file, 0x260, 10);
        Put32(file, 0x208, Magic); Put16(file, 0x20C, Version);
        int length = checked(DataAt + save.GeneralData.Length + save.ParkPackets.Where(p => p != null).Sum(p => p.Length));
        Checksum(file, length); // Validate before copies can run off the fixed buffer.
        Put32(file, 0x204, (uint)length);
        Put32(file, 0x210, DataAt); Put16(file, 0x214, checked((ushort)save.GeneralData.Length));
        save.GeneralData.CopyTo(file, DataAt);
        int cursor = DataAt + save.GeneralData.Length, packedIndex = 0;
        for (int slot = 0; slot < 8; slot++)
        {
            byte status = (byte)(save.Header[43 + slot] & 0x0F);
            byte[] packet = save.ParkPackets[slot];
            file[0x22B + slot] = status;
            if (packet == null) continue;
            if (status != 0) throw new ArgumentException("Only open park slots carry a saved park.");
            if (packedIndex >= 3) throw new ArgumentException("The header holds at most three saved parks.");
            SavePacket.Validate(packet);
            file[0x22B + slot] |= 0xF0;
            Put32(file, 0x218 + 4 * packedIndex, (uint)cursor);
            Put16(file, 0x224 + 2 * packedIndex, checked((ushort)packet.Length));
            packet.CopyTo(file, cursor); cursor += packet.Length; packedIndex++;
        }
        // READ: checksum helper zero-fills the partial last word before summing.
        int end = 0x204 + length;
        while ((end & 3) != 0) file[end++] = 0;
        uint checksum = Checksum(file, length);
        Put32(file, HeaderAt, checksum); Put32(file, ChecksumAt, checksum);
        return file;
    }

    public static SaveArchive Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SaveArchive.FileSize) throw new FormatException("Save archive must occupy five card blocks.");
        byte[] file = bytes.ToArray();
        uint length = U32(file, 0x204);
        if (length > int.MaxValue || U32(file, HeaderAt) != U32(file, ChecksumAt) ||
            Checksum(file, (int)length) != U32(file, HeaderAt)) throw new FormatException("Save checksum mismatch.");
        if (U32(file, 0x208) != Magic || U16(file, 0x20C) != Version) throw new FormatException("Wrong save magic/version.");
        byte[] Slice(uint offset, int size)
        {
            if (offset < DataAt || offset > length || (uint)size > length - offset)
                throw new FormatException("Save block points outside the payload.");
            return file.AsSpan((int)offset, size).ToArray();
        }
        var result = new SaveArchive
        {
            Background = file,
            CardHeader = file.AsSpan(0, 0x200).ToArray(),
            Header = file.AsSpan(HeaderAt, 0x60).ToArray(),
            Settings = file.AsSpan(0x260, 10).ToArray()
        };
        int generalSize = U16(file, 0x214);
        if (generalSize != 0) result.GeneralData = Slice(U32(file, 0x210), generalSize);
        int index = 0;
        for (int slot = 0; slot < 8; slot++)
        {
            if (file[0x22B + slot] != 0xF0) continue; // READ: exact compare at 0x8006C620..624.
            if (index >= 3) throw new FormatException("Too many saved park slots.");
            result.ParkPackets[slot] = Slice(U32(file, 0x218 + 4 * index), U16(file, 0x224 + 2 * index));
            SavePacket.Validate(result.ParkPackets[slot]); index++;
        }
        return result;
    }
}
