using System;
using System.Buffers.Binary;
using System.Linq;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests;

public class SaveArchiveTests
{
    // Synthetic compression exercises the host boundary only; it is explicitly NOT UNPAK evidence.
    sealed class Compression : ISaveCompression
    {
        public byte[] Encode(ReadOnlySpan<byte> input) => input.ToArray().Select(x => (byte)(x ^ 0x69)).Reverse().ToArray();
        public byte[] Decode(ReadOnlySpan<byte> input, int size) => Encode(input);
    }
    static SaveArchive Archive()
    {
        var a = new SaveArchive { CardHeader = SaveFixture.Pattern(512, 7), Header = SaveFixture.Pattern(96, 9),
            Settings = SaveFixture.Pattern(10, 13), GeneralData = new byte[] { 3, 7, 11, 17, 23 } };
        for (int i = 0; i < 8; i++) a.SetParkStatus(i, 2);
        a.Restricted = false;
        foreach (int slot in new[] { 0, 3, 7 })
        {
            a.SetParkStatus(slot, 0);
            a.ParkPackets[slot] = SavePacket.Pack(SaveFixture.Pattern(13 + slot, 29 + slot), new Compression());
        }
        return a;
    }
    static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    static void Put32(byte[] b, int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), v);
    static void Repair(byte[] b)
    {
        int n = (int)U32(b, 0x204); uint sum = 0;
        for (int i = 0; i < n; i++) sum = unchecked(sum + ((uint)b[0x204 + i] << (8 * (i % 4))));
        Put32(b, 0x200, sum); Put32(b, 0x9FFC, sum);
    }

    // REJECTS default-only archive fixtures, wrong park-slot order, lost title/settings/general data or omitted packed headers.
    [Fact]
    public void ThreeDistinctParksAndEveryArchiveFieldRoundTrip()
    {
        var a = Archive(); byte[] bytes = SaveArchiveCodec.Write(a); var b = SaveArchiveCodec.Read(bytes);
        Assert.Equal(40960, bytes.Length); Assert.Equal(a.CardHeader, b.CardHeader); Assert.Equal(a.Settings, b.Settings);
        Assert.Equal(a.GeneralData, b.GeneralData); Assert.False(b.Restricted);
        for (int i = 0; i < 8; i++) Assert.Equal(a.ParkPackets[i], b.ParkPackets[i]);
        Assert.Equal(new byte[] { 0xF0, 2, 2, 0xF0, 2, 2, 2, 0xF0 }, b.Header[43..51]);
        foreach (int i in new[] { 14,15,22,23,51,52,53,54,55,56,57,58,59,60,61,62,63,91,92,93,94,95 }) Assert.Equal(a.Header[i], b.Header[i]);
        Assert.Equal(bytes, SaveArchiveCodec.Write(b));
        Assert.Equal(0x47415901u, U32(bytes, 0x208)); Assert.Equal(0xAC, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x20C)));
        Assert.Equal(618u, U32(bytes, 0x210)); Assert.Equal(5, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x214)));
        Assert.Equal(623u, U32(bytes, 0x218)); Assert.Equal(644u, U32(bytes, 0x21C)); Assert.Equal(668u, U32(bytes, 0x220));
        Assert.Equal(696u, U32(bytes, 0x204)); Assert.Equal(U32(bytes, 0x200), U32(bytes, 0x9FFC));
        byte[] independent = (byte[])bytes.Clone(); Repair(independent); Assert.Equal(independent, bytes);
    }

    // REJECTS forgetting the inversion between header byte 42 and the nonzero runtime restricted flag.
    [Theory]
    [InlineData(0, true)] [InlineData(1, false)] [InlineData(231, false)]
    public void RestrictedModeUsesTheEstablishedHeaderInversion(byte value, bool restricted)
    {
        var a = Archive(); a.Header[42] = value;
        Assert.Equal(restricted, a.Restricted); Assert.Equal(restricted, SaveArchiveCodec.Read(SaveArchiveCodec.Write(a)).Restricted);
        a.Restricted = restricted; Assert.Equal(restricted ? 0 : 1, a.Header[42]);
    }

    // REJECTS summing only to the payload end, CRC/XOR substitutes, big-endian words and dropping partial words.
    [Fact]
    public void ChecksumUsesAbsoluteLengthBeyondPayloadAndZeroPadsPartialWords()
    {
        byte[] file = new byte[40960];
        file[0x204] = 1; file[0x205] = 2; file[0x206] = 3; file[0x207] = 4;
        file[0x208] = 5; file[0x209] = 6; file[0x20A] = 7; file[0x20B] = 0xCC;
        Assert.Equal(0x040A0806u, SaveArchiveCodec.Checksum(file, 7));
        Assert.Equal(0u, SaveArchiveCodec.Checksum(file, 0));
        var good = SaveArchiveCodec.Write(Archive()); var changed = (byte[])good.Clone();
        changed[(int)U32(good, 0x204) + 3] ^= 0x80; // Beyond payload, INSIDE native checksum coverage.
        Assert.Throws<FormatException>(() => SaveArchiveCodec.Read(changed));
        changed = (byte[])good.Clone(); changed[0x180] ^= 0x80; // Card artwork is OUTSIDE checksum coverage.
        Assert.NotNull(SaveArchiveCodec.Read(changed));
        Array.Fill(file, (byte)255, 0x204, 8); Assert.Equal(0xFFFFFFFEu, SaveArchiveCodec.Checksum(file, 8));
    }

    // REJECTS accepting a damaged leading checksum, footer, magic, version, payload bound or packet size; includes repaired-checksum controls.
    [Theory]
    [InlineData(0x200, false)] [InlineData(0x9FFC, false)] [InlineData(0x208, true)] [InlineData(0x20C, true)]
    [InlineData(0x218, true)] [InlineData(0x224, true)]
    public void StructuralCorruptionIsRejectedEvenWithAValidChecksum(int offset, bool repair)
    {
        var bytes = SaveArchiveCodec.Write(Archive()); bytes[offset] ^= 0x80; if (repair) Repair(bytes);
        Assert.Throws<FormatException>(() => SaveArchiveCodec.Read(bytes));
    }

    // REJECTS unbounded slices, fourth saved parks, malformed record sizes, or accepting anything merely having its high nibble set.
    [Fact]
    public void ArchiveBoundsAndExactSlotStatus()
    {
        var a = Archive(); byte[] good = SaveArchiveCodec.Write(a);
        Assert.Throws<FormatException>(() => SaveArchiveCodec.Read(good.AsSpan(0, good.Length - 1)));
        var changed = (byte[])good.Clone(); Put32(changed, 0x204, 0xFFFFFFFF); Assert.Throws<FormatException>(() => SaveArchiveCodec.Read(changed));
        changed = (byte[])good.Clone(); Put32(changed, 0x218, 0); Repair(changed); Assert.Throws<FormatException>(() => SaveArchiveCodec.Read(changed));
        changed = (byte[])good.Clone(); changed[0x22C] = 0xF0; Repair(changed); Assert.Throws<FormatException>(() => SaveArchiveCodec.Read(changed));
        changed = (byte[])good.Clone(); changed[0x232] = 0xF1; Repair(changed);
        Assert.Null(SaveArchiveCodec.Read(changed).ParkPackets[7]);
        a.ParkPackets[1] = a.ParkPackets[0]; Assert.Throws<ArgumentException>(() => SaveArchiveCodec.Write(a));
        a.SetParkStatus(1, 0); Assert.Throws<ArgumentException>(() => SaveArchiveCodec.Write(a));
        a = Archive(); a.GeneralData = new byte[40000]; Assert.Throws<FormatException>(() => SaveArchiveCodec.Write(a));
        a = Archive(); a.Settings = new byte[9]; Assert.Throws<ArgumentException>(() => SaveArchiveCodec.Write(a));
    }

    // REJECTS omitting/overcounting the 8-byte packet header, accepting wrong codec output size or conflating packed/unpacked lengths.
    [Fact]
    public void PacketCompressionContractRoundTripsNontrivialPark()
    {
        var p = SaveFixture.Park(); byte[] raw = ParkSaveCodec.Write(p); var compression = new Compression();
        var packet = SavePacket.Pack(raw, compression);
        Assert.Equal(raw.Length, (int)U32(packet, 0)); Assert.Equal(raw.Length + 8, (int)U32(packet, 4));
        Assert.Equal(compression.Encode(raw), packet[8..]);
        byte[] decoded = SavePacket.Unpack(packet, compression); Assert.Equal(raw, decoded);
        SaveFixture.Equal(p, ParkSaveCodec.Read(decoded, p.Layout));
        Assert.Throws<FormatException>(() => SavePacket.Unpack(packet.AsSpan(0, 7), compression));
        var corrupt = (byte[])packet.Clone(); Put32(corrupt, 4, 1); Assert.Throws<FormatException>(() => SavePacket.Unpack(corrupt, compression));
        corrupt = (byte[])packet.Clone(); Put32(corrupt, 0, 0xFFFFFFFF); Assert.Throws<FormatException>(() => SavePacket.Unpack(corrupt, compression));
        corrupt = (byte[])packet.Clone(); Put32(corrupt, 0, 1); Assert.Throws<FormatException>(() => SavePacket.Unpack(corrupt, compression));
    }
}
