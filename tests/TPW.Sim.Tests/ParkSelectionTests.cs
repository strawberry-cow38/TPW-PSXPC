using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests;

public class ParkSelectionTests
{
    static JsonDocument Fixture() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "selection-audit.json")));

    internal static ParkSelectionTable Table()
    {
        using var json = Fixture(); var root = json.RootElement;
        return new(root.GetProperty("nodes").EnumerateArray().Select(n => new ParkSelectionNode(
            new(n.GetProperty("world").GetByte(), n.GetProperty("park").GetByte()),
            n.GetProperty("name_text_id").GetUInt16())),
            root.GetProperty("links").EnumerateArray().Select(l => new ParkSelectionLink(
                l.GetProperty("a").GetByte(), l.GetProperty("b").GetByte(), l.GetProperty("cost").GetByte())));
    }

    internal sealed class Host : IParkSelectionHost
    {
        internal uint Balance, Lifetime = 50;
        internal int TicketReads;
        internal readonly List<string> Effects = new();
        internal ParkSelection Selection;
        public uint SpendableTickets { get { TicketReads++; return Balance; } }
        public void SpendTickets(byte count)
        {
            Assert.True(Balance >= count);
            Assert.Equal(ParkSelectionStatus.Closed, Selection.Status(Selection.SelectedNode));
            Balance -= count; Effects.Add($"spend:{count}");
        }
        public void DiscardParkBuffer(SelectedPark park)
        {
            Assert.Equal(Selection.SelectedPark, park);
            Assert.Equal(ParkSelectionStatus.Closed, Selection.Status(Selection.SelectedNode));
            Effects.Add($"discard:{park.World},{park.Park}");
        }
    }

    internal static (ParkSelection Selection, Host Host) New(uint balance = 0)
    {
        var host = new Host { Balance = balance }; var selection = new ParkSelection(Table(), host);
        host.Selection = selection; return (selection, host);
    }

    static ParkSelectionStatus[] OnlyOpen(int node)
    {
        var statuses = Enumerable.Repeat(ParkSelectionStatus.Unopened, 8).ToArray();
        statuses[node] = ParkSelectionStatus.Open; return statuses;
    }

    // REJECTS 19 parks, omitted rows, wrong byte offsets/endianness, swapped pair order,
    // invented park names, and a fixture whose decoded values disagree with its original rows.
    [Fact]
    public void RealOverlayHasNineteenRowsEightNodesAndElevenLinks()
    {
        using var json = Fixture(); var root = json.RootElement; var table = Table();
        Assert.Equal(19, root.GetProperty("row_count").GetInt32());
        Assert.Equal(28, root.GetProperty("row_stride").GetInt32());
        Assert.Equal(8, root.GetProperty("node_count").GetInt32());
        Assert.Equal(11, root.GetProperty("link_count").GetInt32());
        Assert.Equal(0, root.GetProperty("skipped_rows").GetInt32());
        Assert.Equal(11, root.GetProperty("overlay").GetProperty("index").GetInt32());
        Assert.Equal("0x80114158", root.GetProperty("overlay_base").GetString());
        Assert.Equal("0x801141F4", root.GetProperty("table_address").GetString());
        Assert.Equal(8, table.Nodes.Count); Assert.Equal(11, table.Links.Count);
        ushort[] names = { 0x3B1, 0x3B2, 0x175, 0x177, 0x2F0, 0x2F1, 0x196, 0x197 };
        var rows = root.GetProperty("rows").EnumerateArray().ToArray(); Assert.Equal(19, rows.Length);
        var bytes = new List<byte>();
        for (int i = 0; i < 19; i++)
        {
            Assert.Equal($"0x{0x801141F4u + i * 28:X8}", rows[i].GetProperty("address").GetString());
            var row = Convert.FromHexString(rows[i].GetProperty("hex").GetString()); Assert.Equal(28, row.Length);
            bytes.AddRange(row);
            Assert.Equal(i < 8 ? 0 : 1, BinaryPrimitives.ReadUInt16LittleEndian(row));
            if (i < 8)
            {
                Assert.Equal(new SelectedPark((byte)(i / 2), (byte)(i % 2)), table.Nodes[i].Location);
                Assert.Equal(names[i], table.Nodes[i].NameTextId);
                Assert.Equal(new SelectedPark(row[6], row[7]), table.Nodes[i].Location);
                Assert.Equal(BinaryPrimitives.ReadUInt16LittleEndian(row.AsSpan(10)), table.Nodes[i].NameTextId);
            }
            else Assert.Equal(new ParkSelectionLink(row[24], row[25], row[26]), table.Links[i - 8]);
        }
        Assert.Equal(root.GetProperty("table_sha256").GetString(),
            Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant());
    }

    // REJECTS a synthetic topology, directed edges, omitted last link, free/incorrect unlock cost,
    // strict-greater affordability, use of lifetime 50 as balance, and opening/charging on preview.
    [Theory]
    [InlineData(0, 0, 2, 1)] [InlineData(1, 0, 4, 1)] [InlineData(2, 4, 2, 1)]
    [InlineData(3, 2, 1, 2)] [InlineData(4, 4, 1, 2)] [InlineData(5, 1, 6, 2)]
    [InlineData(6, 1, 3, 2)] [InlineData(7, 6, 3, 2)] [InlineData(8, 6, 5, 2)]
    [InlineData(9, 3, 7, 2)] [InlineData(10, 5, 7, 2)]
    public void EachOfElevenRealLinksUsesItsCostInBothDirections(int index, byte a, byte b, byte cost)
    {
        Assert.Equal(new ParkSelectionLink(a, b, cost), Table().Links[index]);
        foreach (var (from, to) in new[] { ((int)a, (int)b), ((int)b, (int)a) })
        {
            var (s, h) = New((uint)(cost - 1)); s.Restore(OnlyOpen(from), from);
            Assert.Equal((int)cost, s.Table.TicketCost(from, to));
            Assert.Equal(ParkSelectionResult.InsufficientTickets, s.Select(to, confirmUnlock: true));
            Assert.Equal(from, s.SelectedNode); Assert.Equal(OnlyOpen(from), s.CopyStatuses()); Assert.Empty(h.Effects);
            h.Balance = cost;
            Assert.Equal(ParkSelectionResult.UnlockConfirmationRequired, s.Select(to));
            Assert.Equal(from, s.SelectedNode); Assert.Equal(OnlyOpen(from), s.CopyStatuses()); Assert.Empty(h.Effects);
            Assert.Equal(ParkSelectionResult.Selected, s.Select(to, confirmUnlock: true));
            Assert.Equal(to, s.SelectedNode); Assert.Equal(ParkSelectionStatus.Closed, s.Status(to));
            Assert.Equal(1, s.OpenCount); Assert.Equal(0u, h.Balance); Assert.Equal(50u, h.Lifetime);
            Assert.Equal(new[] { $"discard:{to / 2},{to % 2}", $"spend:{cost}" }, h.Effects);
            Assert.Equal(ParkSelectionResult.Selected, s.Select(to, confirmUnlock: true));
            Assert.Equal(2, h.Effects.Count); // No second purchase of an unlocked park.
        }
    }

    // REJECTS treating a missing direct link as a zero-cost edge or searching a multi-edge route.
    // Each of 64 endpoint pairs has an independently transcribed positive/negative expectation.
    [Fact]
    public void MissingLinksNeverUnlockUnopenedParks()
    {
        (int A, int B)[] edges = { (0,2), (0,4), (4,2), (2,1), (4,1), (1,6), (1,3), (6,3), (6,5), (3,7), (5,7) };
        var (s, h) = New(100);
        for (int a = 0; a < 8; a++) for (int b = 0; b < 8; b++)
        {
            bool linked = edges.Any(e => (e.A == a && e.B == b) || (e.B == a && e.A == b));
            Assert.Equal(linked, s.Table.TicketCost(a, b).HasValue);
            if (a == b || linked) continue;
            s.Restore(OnlyOpen(a), a);
            Assert.Equal(ParkSelectionResult.NoLink, s.Select(b, confirmUnlock: true));
            Assert.Equal(a, s.SelectedNode); Assert.Equal(OnlyOpen(a), s.CopyStatuses());
        }
        Assert.Empty(h.Effects); Assert.Equal(0, h.TicketReads); Assert.Equal(100u, h.Balance);
    }

    // REJECTS gating an already open/closed park on adjacency, tickets or confirmation.
    [Theory]
    [InlineData(ParkSelectionStatus.Open)] [InlineData(ParkSelectionStatus.Closed)]
    public void UnlockedParksSelectImmediatelyWithoutALink(ParkSelectionStatus status)
    {
        var (s, h) = New(); var saved = OnlyOpen(0); saved[7] = status; s.Restore(saved, 0);
        Assert.Equal(ParkSelectionResult.Selected, s.Select(7)); Assert.Equal(7, s.SelectedNode);
        Assert.Equal(saved, s.CopyStatuses()); Assert.Empty(h.Effects); Assert.Equal(0, h.TicketReads);
    }

    // REJECTS charging a stale quoted cost, using the old source node, spending a now insufficient
    // balance, or mutating selection/status during the confirmation preview/cancel path.
    [Fact]
    public void ConfirmationUsesCurrentBalanceAndCurrentSelectedNode()
    {
        var (s, h) = New(1); var saved = OnlyOpen(0); saved[1] = ParkSelectionStatus.Open;
        saved[7] = ParkSelectionStatus.Closed; s.Restore(saved, 0);
        Assert.Equal(ParkSelectionResult.UnlockConfirmationRequired, s.InspectSelection(4));
        h.Balance = 0;
        Assert.Equal(ParkSelectionResult.InsufficientTickets, s.Select(4, true));
        Assert.Equal(0, s.SelectedNode); Assert.Equal(saved, s.CopyStatuses()); Assert.Empty(h.Effects);
        h.Balance = 1; s.Select(1);
        Assert.Equal(ParkSelectionResult.InsufficientTickets, s.Select(4, true)); // 1–4 costs 2, 0–4 costs 1.
        h.Balance = 2; s.Select(7);
        Assert.Equal(ParkSelectionResult.NoLink, s.Select(4, true));
        s.Select(1); Assert.Equal(ParkSelectionResult.Selected, s.Select(4, true));
        Assert.Equal(0u, h.Balance); Assert.Equal(new[] { "discard:2,0", "spend:2" }, h.Effects);
    }

    // REJECTS opening every node initially, status/goal conflation, and a fourth open park.
    // Unlocking at capacity succeeds into CLOSED; closing then permits opening without another charge.
    [Fact]
    public void CampaignStartsAtNodeZeroAndOpeningHasASeparateThreeParkCap()
    {
        var (s, h) = New(20);
        Assert.Equal(0, s.SelectedNode); Assert.Equal(OnlyOpen(0), s.CopyStatuses());
        Assert.Equal(1, s.OpenCount); Assert.False(s.OpenSelected());
        foreach (int node in new[] { 2, 1 })
        {
            Assert.Equal(ParkSelectionResult.Selected, s.Select(node, true));
            Assert.True(s.OpenSelected());
        }
        Assert.Equal(3, s.OpenCount);
        Assert.Equal(ParkSelectionResult.Selected, s.Select(6, true));
        Assert.Equal(ParkSelectionStatus.Closed, s.Status(6)); Assert.False(s.OpenSelected());
        Assert.Equal(15u, h.Balance);
        Assert.Equal(ParkSelectionResult.Selected, s.Select(0));
        Assert.True(s.CloseSelected()); Assert.Equal(2, s.OpenCount); Assert.False(s.CloseSelected());
        s.Select(6); Assert.True(s.OpenSelected()); Assert.Equal(3, s.OpenCount);
        s.Select(0); Assert.False(s.OpenSelected());
        s.Select(6); Assert.True(s.CloseSelected());
        s.Select(0); Assert.True(s.OpenSelected());
        Assert.Equal(15u, h.Balance); Assert.Equal(50u, h.Lifetime);
        Assert.Equal(3, h.Effects.Count(e => e.StartsWith("spend:")));
    }

    // REJECTS opening/closing status 2 without purchasing, counting CLOSED as open, or
    // changing the native ==3 test to >=3 for an abnormal host-restored state. ⚠ DO NOT FIX.
    [Fact]
    public void StatusGuardsAndExactCapComparisonSurviveRestore()
    {
        var (s, h) = New(); s.Restore(OnlyOpen(0), 7);
        Assert.False(s.OpenSelected()); Assert.False(s.CloseSelected());
        var states = Enumerable.Repeat(ParkSelectionStatus.Closed, 8).ToArray();
        for (int i = 0; i < 4; i++) states[i] = ParkSelectionStatus.Open;
        s.Restore(states, 7); Assert.Equal(4, s.OpenCount); Assert.True(s.OpenSelected());
        Assert.Equal(5, s.OpenCount); Assert.Empty(h.Effects);
    }

    // REJECTS deriving world from node index in the entry path, swapping world/park, always using
    // (0,0), mutating the cursor in sandbox, or losing node 7. Campaign and sandbox are controls.
    [Theory]
    [InlineData(0, 0, 0)] [InlineData(1, 0, 1)] [InlineData(2, 1, 0)] [InlineData(3, 1, 1)]
    [InlineData(4, 2, 0)] [InlineData(5, 2, 1)] [InlineData(6, 3, 0)] [InlineData(7, 3, 1)]
    public void EntryYieldsTheSelectedPairAndSandboxOverridesIt(int node, byte world, byte park)
    {
        var (s, h) = New(); s.Restore(OnlyOpen(node), node);
        Assert.Equal(new SelectedPark(world, park), s.SelectedPark);
        Assert.Equal(new SelectedPark(world, park), s.EntryPark(false));
        Assert.Equal(new SelectedPark(0, 0), s.EntryPark(true));
        Assert.Equal(node, s.SelectedNode); Assert.Empty(h.Effects);
    }

    // REJECTS writable table/status aliases, a restore which omits the last node or applies its
    // initial-open default, and entry selection inferred from index instead of supplied node fields.
    [Fact]
    public void SuppliedTableAndStatusSnapshotsAreOwnedAndEntryReadsNodeFields()
    {
        var original = Table(); var nodes = original.Nodes.Reverse().ToArray(); var links = original.Links.ToArray();
        var table = new ParkSelectionTable(nodes, links); nodes[0] = original.Nodes[0]; links[0] = links[10];
        Assert.Equal(original.Nodes[7], table.Nodes[0]); Assert.Equal(original.Links[0], table.Links[0]);
        var host = new Host(); var s = new ParkSelection(table, host); host.Selection = s;
        Assert.Equal(new SelectedPark(3, 1), s.EntryPark(false));
        var states = Enumerable.Repeat(ParkSelectionStatus.Closed, 8).ToArray(); s.Restore(states, 7);
        states[7] = ParkSelectionStatus.Unopened;
        var copy = s.CopyStatuses(); copy[0] = ParkSelectionStatus.Open;
        Assert.Equal(0, s.OpenCount); Assert.Equal(ParkSelectionStatus.Closed, s.Status(7));
        Assert.Equal(ParkSelectionStatus.Closed, s.Status(0)); Assert.Equal(new SelectedPark(0, 0), s.SelectedPark);
    }

    // REJECTS partial/invalid host tables or statuses and signed/upper-bound node indices;
    // rejected restore must leave the old cursor and all eight statuses intact.
    [Fact]
    public void HostBoundaryValidationIsAtomic()
    {
        var table = Table(); var (s, _) = New();
        Assert.Throws<ArgumentException>(() => new ParkSelectionTable(table.Nodes.Take(7), table.Links));
        Assert.Throws<ArgumentException>(() => new ParkSelectionTable(table.Nodes, table.Links.Take(10)));
        var nodes = table.Nodes.ToArray(); nodes[7] = nodes[0];
        Assert.Throws<ArgumentException>(() => new ParkSelectionTable(nodes, table.Links));
        nodes = table.Nodes.ToArray(); nodes[7] = new(new(4, 0), 0);
        Assert.Throws<ArgumentException>(() => new ParkSelectionTable(nodes, table.Links));
        var links = table.Links.ToArray(); links[10] = new(0, 8, 1);
        Assert.Throws<ArgumentException>(() => new ParkSelectionTable(table.Nodes, links));
        foreach (int node in new[] { -1, 8 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Status(node));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Select(node));
            Assert.Throws<ArgumentOutOfRangeException>(() => table.TicketCost(node, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => table.TicketCost(0, node));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Restore(OnlyOpen(7), node));
        }
        Assert.Throws<ArgumentException>(() => s.Restore(new ParkSelectionStatus[7], 0));
        var states = OnlyOpen(7); states[7] = (ParkSelectionStatus)3;
        Assert.Throws<ArgumentException>(() => s.Restore(states, 7));
        Assert.Equal(OnlyOpen(0), s.CopyStatuses()); Assert.Equal(0, s.SelectedNode);
    }
}
