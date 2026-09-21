using System;
using System.Collections.Generic;
using System.Linq;

namespace TPW.Sim;

/// <summary>READ: selection node +0A/+0B -> 0x800BCAF4..0x800BCB14 -> park constructor
/// 0x80050524. 0x80057ED4/0x80057EE0 install Park/World in 0x801038A4/0x801038A0.</summary>
public readonly record struct SelectedPark(byte World, byte Park);

/// <summary>READ: OVL11 table 0x801141F4, 28-byte rows; +6/+7 world/park, +0A u16 name.
/// World/park copied at 0x801145BC..CC. findings/objbytes.md §3 is the retained source.</summary>
public readonly record struct ParkSelectionNode(SelectedPark Location, ushort NameTextId);

/// <summary>READ: source row +18/+19/+1A, copied at OVL11 0x80114648..64.
/// Endpoints are node indices, not world numbers; matched either way at 0x80115528..A0.</summary>
public readonly record struct ParkSelectionLink(byte A, byte B, byte TicketCost);

/// <summary>Host supplies the decoded OVL11 table. No file access or engine dependency.
/// READ: 19 rows = 8 nodes + 11 links at 0x801141F4 (findings/objbytes.md §3).
/// Constructor checks are port input validation, not native corrupt-table behavior.</summary>
public sealed class ParkSelectionTable
{
    public const int NodeCount = 8;
    public const int LinkCount = 11;
    public IReadOnlyList<ParkSelectionNode> Nodes { get; }
    public IReadOnlyList<ParkSelectionLink> Links { get; }

    public ParkSelectionTable(IEnumerable<ParkSelectionNode> nodes, IEnumerable<ParkSelectionLink> links)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(links);
        var n = nodes.ToArray(); var l = links.ToArray();
        if (n.Length != NodeCount || l.Length != LinkCount)
            throw new ArgumentException("Selection has 19 rows: 8 nodes and 11 links.");
        if (n.Any(x => x.Location.World >= 4 || x.Location.Park >= 2) ||
            n.Select(x => x.Location).Distinct().Count() != NodeCount)
            throw new ArgumentException("Supply each of the eight world/park pairs once.");
        if (l.Any(x => x.A >= NodeCount || x.B >= NodeCount || x.A == x.B))
            throw new ArgumentException("Invalid selection link endpoints.");
        Nodes = Array.AsReadOnly(n); Links = Array.AsReadOnly(l);
    }

    internal void CheckNode(int node)
    {
        if ((uint)node >= NodeCount) throw new ArgumentOutOfRangeException(nameof(node));
    }

    public int? TicketCost(int from, int to)
    {
        CheckNode(from); CheckNode(to);
        foreach (var link in Links)
            if ((link.A == from && link.B == to) || (link.B == from && link.A == to))
                return link.TicketCost;
        return null;
    }
}

/// <summary>READ: 0x8006BC68 / 0x8006BCE4 / 0x8006C83C..4C.
/// ⚠ DO NOT FIX: these are campaign availability states, not objective completion or gate state.</summary>
public enum ParkSelectionStatus : byte { Open = 0, Closed = 1, Unopened = 2 }

/// <summary>Host effects for campaign selection. Call synchronously; callbacks must not reenter
/// selection or throw after partially applying an effect. No objective/award state crosses this seam.</summary>
public interface IParkSelectionHost
{
    /// <summary>READ: 0x801155AC..C4 -> 0x8006BE1C; current spendable balance, including any
    /// host cheat override. Neither lifetime earned tickets nor advertised park totals are consulted.</summary>
    uint SpendableTickets { get; }
    /// <summary>READ: 0x80117378 -> 0x8006C024. Subtract from spendable tickets only;
    /// leave lifetime/award words intact. Called once after confirmed selection and closure.</summary>
    void SpendTickets(byte count);
    /// <summary>READ: 0x8006BCE4 frees any saved park buffer. Discard that park's packet;
    /// retain objective bits and the separate eight-word award array (objbytes.md §3).</summary>
    void DiscardParkBuffer(SelectedPark park);
}

// Port API results, not PSX numeric event IDs. NoLink is a host guard for unreachable input.
public enum ParkSelectionResult { Selected, UnlockConfirmationRequired, NoLink, InsufficientTickets }

/// <summary>READ: selection/status flow retained from findings/objbytes.md §3.
/// This is the campaign model; input, confirmation dialogs, park construction and ticket storage
/// belong to the host. No terminal campaign victory state is established or introduced.</summary>
public sealed class ParkSelection
{
    // READ: status-zero count 0x80116F70..A8, comparison 0x801173D4..EC.
    public const int MaximumOpenParks = 3;
    readonly IParkSelectionHost host;
    readonly ParkSelectionStatus[] statuses = new ParkSelectionStatus[ParkSelectionTable.NodeCount];
    public ParkSelectionTable Table { get; }
    public int SelectedNode { get; private set; }
    public SelectedPark SelectedPark => Table.Nodes[SelectedNode].Location;
    public int OpenCount => statuses.Count(s => s == ParkSelectionStatus.Open);

    public ParkSelection(ParkSelectionTable table, IParkSelectionHost host)
    {
        Table = table ?? throw new ArgumentNullException(nameof(table));
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        Array.Fill(statuses, ParkSelectionStatus.Unopened); // READ: 0x8006C83C..4C.
        statuses[0] = ParkSelectionStatus.Open; // READ: campaign init 0x8006BB14..20.
    }

    public ParkSelectionStatus Status(int node) { Table.CheckNode(node); return statuses[node]; }
    public ParkSelectionStatus[] CopyStatuses() => (ParkSelectionStatus[])statuses.Clone();

    /// <summary>Restore campaign state without opening/closing buffers or charging tickets.
    /// READ: eight status loads 0x8011512C..48 -> 0x8006BD74. Selected node is a host-supplied
    /// UI cursor; no saved cursor field is established. Validation is a port boundary policy.</summary>
    public void Restore(ReadOnlySpan<ParkSelectionStatus> saved, int selectedNode)
    {
        Table.CheckNode(selectedNode);
        if (saved.Length != ParkSelectionTable.NodeCount)
            throw new ArgumentException("Supply eight campaign statuses.");
        foreach (var status in saved)
            if (status > ParkSelectionStatus.Unopened) throw new ArgumentException("Invalid campaign status.");
        saved.CopyTo(statuses); SelectedNode = selectedNode;
    }

    /// <summary>READ: status 2 alone uses links and spendable tickets (0x801154D0..0x801155C4).
    /// NoLink rejects a host request with no decoded edge; native UI reachability is unestablished.</summary>
    public ParkSelectionResult InspectSelection(int target)
    {
        Table.CheckNode(target);
        if (statuses[target] != ParkSelectionStatus.Unopened) return ParkSelectionResult.Selected;
        int? cost = Table.TicketCost(SelectedNode, target);
        if (!cost.HasValue) return ParkSelectionResult.NoLink;
        if (host.SpendableTickets < cost.Value) return ParkSelectionResult.InsufficientTickets;
        return ParkSelectionResult.UnlockConfirmationRequired;
    }

    /// <summary>READ: confirmation redoes link lookup, moves selection, closes/unlocks, then spends,
    /// 0x801172EC..0x80117378. ⚠ DO NOT FIX: unlocking produces CLOSED, not OPEN.
    /// Rechecking affordability at commit is a port guard against a stale host confirmation.</summary>
    public ParkSelectionResult Select(int target, bool confirmUnlock = false)
    {
        var result = InspectSelection(target);
        if (result == ParkSelectionResult.Selected) { SelectedNode = target; return result; }
        if (result != ParkSelectionResult.UnlockConfirmationRequired || !confirmUnlock) return result;
        byte cost = checked((byte)Table.TicketCost(SelectedNode, target).Value);
        SelectedNode = target;
        SetClosed(target);
        host.SpendTickets(cost);
        return ParkSelectionResult.Selected;
    }

    /// <summary>READ: 0x801173D4..EC blocks opening at exactly three; 0x8006BC68 sets zero.
    /// ⚠ DO NOT FIX the equality into a clamp on restored data. Counts above three cannot be
    /// produced through normal transitions; behavior on such host-supplied states is not repaired.</summary>
    public bool OpenSelected()
    {
        if (statuses[SelectedNode] != ParkSelectionStatus.Closed || OpenCount == MaximumOpenParks) return false;
        statuses[SelectedNode] = ParkSelectionStatus.Open;
        return true;
    }

    public bool CloseSelected()
    {
        if (statuses[SelectedNode] != ParkSelectionStatus.Open) return false;
        SetClosed(SelectedNode);
        return true;
    }

    void SetClosed(int node)
    {
        statuses[node] = ParkSelectionStatus.Closed; // READ: 0x8006BCE4.
        host.DiscardParkBuffer(Table.Nodes[node].Location);
    }

    /// <summary>READ: sandbox explicitly chooses zero/zero at 0x800BCB90..98; campaign passes
    /// the selected pair at 0x800BCB9C..A8. Host calls this after its entry/opening flow.</summary>
    public SelectedPark EntryPark(bool restrictedMode) => restrictedMode ? new(0, 0) : SelectedPark;
}
