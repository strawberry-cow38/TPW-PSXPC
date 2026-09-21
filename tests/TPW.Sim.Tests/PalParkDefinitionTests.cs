using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using TPW.Data;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests;

// Explicit discovery-time skips on machines without the disc, never a passing empty test.
public sealed class PalDiscTheoryAttribute : TheoryAttribute
{
    public PalDiscTheoryAttribute()
    {
        if (!File.Exists(PalParkDefinitionTests.ExecutablePath))
            Skip = "PAL disc unavailable; set TPW_DISC_DATA_ROOT to its extracted files.";
    }
}

public class PalParkDefinitionTests
{
    public static string ExecutablePath => Path.Combine(
        Environment.GetEnvironmentVariable("TPW_DISC_DATA_ROOT") ?? "/home/ec2-user/tpw/ext", "TPW.BIN");
    static byte[] SyntheticImage()
    {
        var bytes = new byte[0x100000];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = unchecked((byte)(i * 71 + (i >> 8) * 23));
        return bytes;
    }
    static void Put(byte[] data, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    // REJECTS transcribed rosters/counts, a swapped world/park, dropped short/empty lists,
    // type-8 stride 4, and a round trip that merely returns its supplied executable template.
    // The hashes/counts/addresses in the audit were produced by the ORIGINAL MIPS initializer
    // and getters, independently of PalParkDefinition's layout table.
    [PalDiscTheory]
    [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(1, 0)] [InlineData(1, 1)]
    [InlineData(2, 0)] [InlineData(2, 1)] [InlineData(3, 0)] [InlineData(3, 1)]
    public void EveryDiscParkMatchesNativeProducerAndRoundTrips(int world, int park)
    {
        var exe = File.ReadAllBytes(ExecutablePath);
        using var audit = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scenario-audit.json")));
        Assert.Equal(audit.RootElement.GetProperty("binary_sha256").GetString(), Sha(exe));
        var expected = audit.RootElement.GetProperty("parks")[world * 2 + park];
        var data = PalParkDefinition.Read(exe, world, park);
        Assert.Equal(world, data.World); Assert.Equal(park, data.Park);
        Assert.Equal(expected.GetProperty("objective_file_offset").GetInt32(), data.ObjectiveFileOffset);
        Assert.Equal(expected.GetProperty("objective_sha256").GetString(), Sha(data.CopyObjectiveRecord()));
        Assert.Equal(expected.GetProperty("advertised_gold_tickets").GetByte(), data.AdvertisedGoldTickets);
        Assert.Equal(ParkObjectiveDefinition.ForPalPark(world, park).CopyRecord(), data.CopyObjectiveRecord());
        Assert.Equal(ParkWorlds.All[world].Maps[park], (int)data.MapEntry);
        Assert.Equal(ParkWorlds.All[world].SceneryEntry, (int)data.SceneryEntry);
        Assert.Equal(8, data.Catalogues.Count);
        var template = (byte[])exe.Clone();
        template.AsSpan(data.ObjectiveFileOffset, 52).Fill(0xA5);
        template.AsSpan(data.MapPairFileOffset, 8).Fill(0xA5);
        foreach (var group in expected.GetProperty("groups").EnumerateArray())
        {
            var actual = data.Catalogue(group.GetProperty("type").GetInt32());
            Assert.Equal(group.GetProperty("file_offset").GetInt32(), actual.FileOffset);
            Assert.Equal(group.GetProperty("count").GetInt32(), actual.Count);
            Assert.Equal(group.GetProperty("stride").GetInt32(), actual.Stride);
            Assert.Equal(group.GetProperty("sha256").GetString(), Sha(actual.Encode()));
            for (int i = 0; i < actual.Count; i++)
                Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(actual.FileOffset + i * actual.Stride)), actual.Entry(i));
            template.AsSpan(actual.FileOffset, actual.Count * actual.Stride).Fill(0xA5);
        }
        var encoded = data.WriteToExecutable(template);
        Assert.Equal(exe, encoded);
        Assert.NotEqual(Sha(exe), Sha(template));
        Assert.Equal(0xA5, template[data.ObjectiveFileOffset]);
        var defaults = PalParkDefaults.Read(exe);
        Assert.Equal(new PalParkDefaults(50000, 40, false, false), defaults);
    }

    // REJECTS flag inference from tutorial/ticket/opaque bytes, normalization of opaque
    // fields, aliased input/output, an unsigned profit, or bypassing the existing objective API.
    [Fact]
    public void OpaqueObjectiveBytesAndNewTicketFieldSurviveExistingObjectiveInterface()
    {
        var exe = SyntheticImage();
        Put(exe, 0xD1930 + 0x10, 0xFFFFFFF9);
        exe[0xD1930 + 0x30] = 0;
        exe[0xD1930 + 0x31] = 219;
        var data = PalParkDefinition.Read(exe, 0, 0);
        var original = exe.AsSpan(0xD1930, 52).ToArray();
        exe.AsSpan(0xD1930, 52).Clear();
        var copy = data.CopyObjectiveRecord();
        Assert.Equal(original, copy);
        Assert.Equal(219, data.AdvertisedGoldTickets);
        var goals = new ParkObjectiveDefinition(copy);
        Assert.Equal(-7, goals.ProfitPounds);
        Assert.False(goals.TutorialAwardEnabled);
        copy[0] ^= 0xFF; copy[0x31] = 0;
        Assert.Equal(original, data.CopyObjectiveRecord());
        Assert.Equal(original, goals.CopyRecord());
        Assert.Equal(original, data.WriteToExecutable(exe).AsSpan(0xD1930, 52).ToArray());
    }

    // REJECTS taking runtime handles as entries, dropping nonzero handles as "padding",
    // reordering lists, aliasing, or overwriting the byte immediately after a two-record list.
    [Fact]
    public void TrackUpgradePairsPreserveHandlesAndOrdinaryListsKeepTheirOrder()
    {
        var exe = SyntheticImage();
        const int pairs = 0xE0D44;
        Put(exe, pairs, 17); Put(exe, pairs + 4, 0xDEADBEEF);
        Put(exe, pairs + 8, 19); Put(exe, pairs + 12, 0x10203040);
        Put(exe, 0xE0CF0, 0xFEDCBA98); Put(exe, 0xE0CF4, 0x76543210);
        var data = PalParkDefinition.Read(exe, 0, 0);
        var original = exe.AsSpan(pairs, 16).ToArray();
        var tracks = data.Catalogue(8);
        Assert.Equal(2, tracks.Count); Assert.Equal(8, tracks.Stride);
        Assert.Equal(17u, tracks.Entry(0)); Assert.Equal(19u, tracks.Entry(1));
        Assert.Equal(original, tracks.Encode());
        exe.AsSpan(pairs, 16).Clear();
        var exported = tracks.Encode(); exported[4] = 0;
        Assert.Equal(original, tracks.Encode());
        Assert.Equal(0xFEDCBA98u, data.Catalogue(3).Entry(0));
        Assert.Equal(0x76543210u, data.Catalogue(3).Entry(1));
        Assert.Empty(data.Catalogue(7).Encode());
        Assert.Throws<ArgumentOutOfRangeException>(() => data.Catalogue(7).Entry(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracks.Entry(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracks.Entry(2));
        var encoded = data.WriteToExecutable(exe);
        Assert.Equal(original, encoded.AsSpan(pairs, 16).ToArray());
        Assert.Equal(exe[pairs + 16], encoded[pairs + 16]);
    }

    // REJECTS flattening world and park incorrectly, accepting invalid pairs, silently empty
    // truncated regions, and accepting an unknown catalogue type as the first list.
    [Theory]
    [InlineData(-1, 0)] [InlineData(4, 0)] [InlineData(0, -1)] [InlineData(0, 2)]
    [InlineData(int.MaxValue, 0)] [InlineData(0, int.MinValue)]
    public void InvalidSelectorsReturnNullLikeNativeLeaf(int world, int park)
        => Assert.Null(PalParkDefinition.Read(ReadOnlySpan<byte>.Empty, world, park));

    // REJECTS partial parsing, missing last catalogue words, and accepting undersized output.
    [Fact]
    public void ValidSelectorsRequireAllTheirRegions()
    {
        var exe = SyntheticImage();
        var data = PalParkDefinition.Read(exe, 3, 1);
        Assert.Throws<ArgumentException>(() => PalParkDefinition.Read(Array.Empty<byte>(), 0, 0));
        Assert.Throws<ArgumentException>(() => PalParkDefinition.Read(new byte[0xF2997], 3, 1));
        Assert.NotNull(PalParkDefinition.Read(new byte[0xF2998], 3, 1));
        Assert.Throws<ArgumentException>(() => data.WriteToExecutable(new byte[0xF2997]));
        Assert.Throws<ArgumentOutOfRangeException>(() => data.Catalogue(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => data.Catalogue(9));
    }

    // REJECTS a 16-bit signed interpretation of ORI's £50000, an unsigned ADDIU, testing
    // only the low flag byte, treating any nonzero as exactly 1, and swapping the two globals.
    [Fact]
    public void DefaultsDecodeInstructionImmediatesAndIndependentGlobalWords()
    {
        var exe = SyntheticImage();
        Put(exe, 0x48AA0, 0x3405C350); Put(exe, 0x765D4, 0x24050028);
        Put(exe, 0xF2E88, 0x100); Put(exe, 0xF2D34, 0);
        Assert.Equal(new PalParkDefaults(50000, 40, true, false), PalParkDefaults.Read(exe));
        Put(exe, 0xF2E88, 0); Put(exe, 0xF2D34, 0x80000000);
        Put(exe, 0x48AA0, 0x3405EA61);
        Put(exe, 0x765D4, 0x2405FFFF);
        Assert.Equal(new PalParkDefaults(60001, -1, false, true), PalParkDefaults.Read(exe));
        Put(exe, 0x48AA0, 0x2405C350);
        Assert.Throws<ArgumentException>(() => PalParkDefaults.Read(exe));
        Assert.Throws<ArgumentOutOfRangeException>(() => PalParkDefaults.Read(Array.Empty<byte>()));
    }
}
