using System;

namespace TPW.Sim;

/// <summary>READ: findings/save.md §2, existing SaveArchive header +2B..32 (file +22B..232).
/// Four worlds, two parks each, low-nibble status; exact F0 additionally carries a park packet.
/// This is campaign metadata, not ParkSave.Open (the gate). No new bytes are invented.</summary>
public static class ParkSelectionSave
{
    public static void Capture(ParkSelection selection, SaveArchive archive)
    {
        for (int node = 0; node < ParkSelectionTable.NodeCount; node++)
        {
            var park = selection.Table.Nodes[node].Location;
            int slot = park.World * 2 + park.Park; // READ: 0x8006C5D0..680, world-major slots.
            var status = selection.Status(node);
            archive.SetParkStatus(slot, (byte)status);
            // READ: 0x8006BCE4 frees the packet. Capture also removes stale host archive copies.
            if (status != ParkSelectionStatus.Open) archive.ParkPackets[slot] = null;
        }
        // SaveArchiveCodec supplies F0 only for open slots with packets (0x8006C620..624).
    }

    /// <summary>READ: 0x8006C5D0..680 dispatches low nibble 0/1; other values leave the
    /// initialized status 2. Packet inclusion remains SaveArchiveCodec's exact-F0 decision.
    /// SelectedNode is not serialized here: the host supplies its restored UI cursor.</summary>
    public static void Restore(ParkSelection selection, SaveArchive archive, int selectedNode)
    {
        var statuses = new ParkSelectionStatus[ParkSelectionTable.NodeCount];
        for (int node = 0; node < statuses.Length; node++)
        {
            var park = selection.Table.Nodes[node].Location;
            int slot = park.World * 2 + park.Park;
            int low = archive.GetParkStatus(slot) & 0x0F;
            statuses[node] = low == 0 ? ParkSelectionStatus.Open :
                low == 1 ? ParkSelectionStatus.Closed : ParkSelectionStatus.Unopened;
        }
        selection.Restore(statuses, selectedNode);
    }
}
