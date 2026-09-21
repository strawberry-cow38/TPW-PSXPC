using System;
using System.Linq;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests;

public class ParkSelectionSaveTests
{
    // Synthetic packet transport only, not evidence of PSX UNPAK compatibility.
    sealed class IdentityCompression : ISaveCompression
    {
        public byte[] Encode(ReadOnlySpan<byte> unpacked) => unpacked.ToArray();
        public byte[] Decode(ReadOnlySpan<byte> packed, int size) => packed.ToArray();
    }

    // REJECTS writing campaign status into the gate byte, inventing save fields, dropping the last
    // of eight statuses, losing open packets, retaining closed packets, or touching ticket/award bytes.
    [Fact]
    public void EightStatusesRoundTripThroughExistingArchiveWithThreeActualParkStreams()
    {
        var (s, h) = ParkSelectionTests.New(17);
        ParkSelectionStatus[] statuses = { ParkSelectionStatus.Open, ParkSelectionStatus.Closed,
            ParkSelectionStatus.Unopened, ParkSelectionStatus.Open, ParkSelectionStatus.Closed,
            ParkSelectionStatus.Unopened, ParkSelectionStatus.Closed, ParkSelectionStatus.Open };
        s.Restore(statuses, 7);
        var archive = new SaveArchive();
        for (int i = 51; i < 96; i++) archive.Header[i] = (byte)(i * 3 + 1);
        var surrounding = archive.Header[51..];
        var codec = new IdentityCompression();
        foreach (int slot in new[] { 0, 3, 7 })
        {
            var layout = new ParkSaveLayout((byte)(slot / 2), (byte)(slot % 2), 3, 2, true, new int[8]);
            var park = new ParkSave(layout) { Open = 0, EntryFeePounds = (ushort)(31 + slot) };
            archive.ParkPackets[slot] = SavePacket.Pack(ParkSaveCodec.Write(park), codec);
        }
        archive.ParkPackets[1] = archive.ParkPackets[0]; // Stale closed-park copy must be discarded.
        archive.ParkPackets[2] = archive.ParkPackets[0]; // Likewise unopened, not silently made open.
        ParkSelectionSave.Capture(s, archive);
        Assert.Null(archive.ParkPackets[1]); Assert.Null(archive.ParkPackets[2]);
        Assert.Equal(surrounding, archive.Header[51..]);
        byte[] bytes = SaveArchiveCodec.Write(archive);
        Assert.Equal(new byte[] { 0xF0, 1, 2, 0xF0, 1, 2, 1, 0xF0 }, bytes[0x22B..0x233]);
        var loaded = SaveArchiveCodec.Read(bytes); var (restored, restoreHost) = ParkSelectionTests.New();
        ParkSelectionSave.Restore(restored, loaded, 7);
        Assert.Equal(statuses, restored.CopyStatuses()); Assert.Equal(3, restored.OpenCount);
        Assert.Equal(new SelectedPark(3, 1), restored.EntryPark(false));
        Assert.Equal(surrounding, loaded.Header[51..]); Assert.Empty(h.Effects); Assert.Empty(restoreHost.Effects);
        foreach (int slot in new[] { 0, 3, 7 })
        {
            var layout = new ParkSaveLayout((byte)(slot / 2), (byte)(slot % 2), 3, 2, true, new int[8]);
            var park = ParkSaveCodec.Read(SavePacket.Unpack(loaded.ParkPackets[slot], codec), layout);
            Assert.Equal(0, park.Open); Assert.Equal(31 + slot, park.EntryFeePounds);
            Assert.Equal(ParkSelectionStatus.Open, restored.Status(slot));
        }
        ParkSelectionSave.Capture(restored, loaded);
        Assert.Equal(bytes, SaveArchiveCodec.Write(loaded));
        restored.CloseSelected(); ParkSelectionSave.Capture(restored, loaded);
        Assert.Null(loaded.ParkPackets[7]); Assert.NotNull(loaded.ParkPackets[0]);
        var afterClose = SaveArchiveCodec.Read(SaveArchiveCodec.Write(loaded));
        ParkSelectionSave.Restore(restored, afterClose, 7);
        Assert.Equal(ParkSelectionStatus.Closed, restored.Status(7));
        Assert.Equal(surrounding, afterClose.Header[51..]);
        Assert.True(restored.OpenSelected()); Assert.Equal(0u, restoreHost.Balance);
    }

    // REJECTS interpreting F0 as a fourth status, confusing non-F0 bytes with packet presence,
    // or mapping unrecognized low nibbles to CLOSED instead of leaving initialized status 2.
    [Theory]
    [InlineData(0, ParkSelectionStatus.Open)] [InlineData(0xF0, ParkSelectionStatus.Open)]
    [InlineData(0xA0, ParkSelectionStatus.Open)] [InlineData(1, ParkSelectionStatus.Closed)]
    [InlineData(0xF1, ParkSelectionStatus.Closed)] [InlineData(2, ParkSelectionStatus.Unopened)]
    [InlineData(0xF2, ParkSelectionStatus.Unopened)] [InlineData(0xAF, ParkSelectionStatus.Unopened)]
    public void RestoreUsesLowNibbleWhileArchiveOwnsExactPacketPresence(byte value, ParkSelectionStatus expected)
    {
        var archive = new SaveArchive(); for (int i = 0; i < 8; i++) archive.SetParkStatus(i, 2);
        archive.SetParkStatus(7, value); var (s, h) = ParkSelectionTests.New();
        ParkSelectionSave.Restore(s, archive, 7);
        Assert.Equal(expected, s.Status(7)); Assert.Equal(7, s.SelectedNode);
        Assert.Equal(ParkSelectionStatus.Unopened, s.Status(0)); Assert.Empty(h.Effects);
    }

    // REJECTS serializing by node index instead of world-major pair, saving an implicit UI cursor,
    // or failing to restore CLOSED/UNOPENED when the supplied table uses a different node order.
    [Fact]
    public void ArchiveSlotsUseWorldAndParkFromTheSuppliedTable()
    {
        var source = ParkSelectionTests.Table();
        var table = new ParkSelectionTable(source.Nodes.Reverse(), source.Links);
        var host = new ParkSelectionTests.Host(); var s = new ParkSelection(table, host); host.Selection = s;
        ParkSelectionStatus[] states = { ParkSelectionStatus.Open, ParkSelectionStatus.Closed,
            ParkSelectionStatus.Unopened, ParkSelectionStatus.Closed, ParkSelectionStatus.Open,
            ParkSelectionStatus.Unopened, ParkSelectionStatus.Closed, ParkSelectionStatus.Open };
        s.Restore(states, 6); var archive = new SaveArchive(); ParkSelectionSave.Capture(s, archive);
        Assert.Equal(states.Reverse().Select(x => (byte)x).ToArray(), archive.Header[43..51]);
        var (normal, _) = ParkSelectionTests.New(); ParkSelectionSave.Restore(normal, archive, 2);
        Assert.Equal(states.Reverse().ToArray(), normal.CopyStatuses()); Assert.Equal(2, normal.SelectedNode);
        s.Restore(new ParkSelectionStatus[8], 0);
        ParkSelectionSave.Restore(s, archive, 5);
        Assert.Equal(states, s.CopyStatuses()); Assert.Equal(5, s.SelectedNode);
    }
}
