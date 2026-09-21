using System;
using System.Collections.Generic;
using System.Linq;
using TPW.Sim;
using Xunit;
using Xunit.Abstractions;

namespace TPW.Sim.Tests;

public class ParkMessagesTests
{
    readonly ITestOutputHelper output;
    public ParkMessagesTests(ITestOutputHelper output) => this.output = output;

    sealed class Host : IParkMessageHost, IParkMessageSaveHost
    {
        public string Text = "A whole %s string {0} with spaces";
        public readonly List<short> TextReads = new(), Replays = new();
        public readonly List<byte[]> Decodes = new();
        public readonly List<object> Jumps = new(), Located = new();
        public readonly List<(sbyte, byte)> Resolved = new();
        public readonly Dictionary<object, (sbyte, byte)> Addresses = new();
        public readonly Dictionary<(sbyte, byte), object> Objects = new();
        public string GetText(short id) { TextReads.Add(id); return Text; }
        public string DecodeText(ReadOnlySpan<byte> text) { Decodes.Add(text.ToArray()); return "decoded by host"; }
        public void JumpToObject(object target) => Jumps.Add(target);
        public void ReplayMessage(short id) => Replays.Add(id);
        public (sbyte Type, byte Index) LocateMessageTarget(object target)
        { Located.Add(target); return Addresses[target]; }
        public object ResolveMessageTarget(sbyte type, byte index)
        { Resolved.Add((type, index)); return Objects[(type, index)]; }
    }

    static ParkMessages Full()
    {
        var list = new ParkMessages();
        for (int i = 0; i < 32; i++) list.Push(1000 + i, 0);
        Assert.Equal(32, list.Count);
        return list;
    }

    // REJECTS a prepopulated badge, exposing a mutable collection, or sharing records between parks.
    [Fact]
    public void FreshListIsEmptyAndOnlyTheComponentCanChangeMembership()
    {
        var list = new ParkMessages();
        Assert.Equal(0, list.Count); Assert.Empty(list.Records);
        var card = list.Push(17, 0);
        Assert.Same(card, Assert.Single(list.Records));
        Assert.Throws<NotSupportedException>(() => ((IList<ParkMessage>)list.Records).Clear());
        Assert.Empty(new ParkMessages().Records);
    }

    // REJECTS formatting, unsigned/wide text IDs, lost kind/object fields, and aliased payload copies.
    [Fact]
    public void PushCopiesTheThreeFieldsAndInlineBytes()
    {
        var list = new ParkMessages(); var target = new object();
        var card = list.Push(0x18023, 0x102, target);
        Assert.Equal(-32733, card.TextId); Assert.Equal(0x102u, card.Kind); Assert.Same(target, card.Target);
        Assert.Empty(card.InlineText.ToArray());
        byte[] bytes = { 0x41, 0x80, 0xE3 };
        var inline = list.PushInline(bytes, 1, target); bytes[0] = 0x42;
        Assert.Equal(-1, inline.TextId); Assert.Equal(1u, inline.Kind); Assert.Same(target, inline.Target);
        Assert.Equal(new byte[] { 0x41, 0x80, 0xE3 }, inline.InlineText.ToArray());
        Assert.Equal(2, list.Count);
    }

    // REJECTS capacity 31/33, rejecting the 33rd card, newest eviction, prepend, and broken repeated compaction.
    [Fact]
    public void ThirtyThirdAndLaterPushesEvictOnlyTheOldest()
    {
        var list = Full();
        Assert.Equal(Enumerable.Range(1000, 32), list.Records.Select(x => (int)x.TextId));
        var oldest = list.Records[0];
        list.Push(1032, 2, new object());
        Assert.Equal(32, list.Count); Assert.DoesNotContain(oldest, list.Records);
        Assert.Equal(Enumerable.Range(1001, 32), list.Records.Select(x => (int)x.TextId));
        for (int i = 1033; i < 1100; i++) list.Push(i, 0);
        Assert.Equal(32, list.Count);
        Assert.Equal(Enumerable.Range(1068, 32), list.Records.Select(x => (int)x.TextId));
    }

    // REJECTS deduplication, matching kind/object instead of text, deleting the last/all duplicates,
    // and narrowing only the stored ID but not a retraction argument.
    [Fact]
    public void RetractionRemovesFirstTextMatchAndLeavesOtherCopiesInOrder()
    {
        var list = new ParkMessages();
        var head = list.Push(11, 0);
        var first = list.Push(0x8023, 2, new object());
        var middle = list.Push(12, 0);
        var second = list.Push(0x8023, 3);
        Assert.Equal(4, list.Count);
        Assert.True(list.Retract(0x18023));
        Assert.Equal(new[] { head, middle, second }, list.Records);
        Assert.DoesNotContain(first, list.Records);
        Assert.True(list.Retract(0x8023));
        Assert.Equal(new[] { head, middle }, list.Records);
        Assert.False(list.Retract(0x8023)); Assert.Equal(2, list.Count);
        Assert.True(list.Retract(11)); Assert.True(list.Retract(12));
        Assert.False(list.Retract(12)); Assert.Empty(list.Records);
    }

    // REJECTS confusing a slot index with a text id, removing the tail, or reordering surviving cards.
    [Fact]
    public void PlayerDeletionCommitsTheRequestedSlot()
    {
        var list = new ParkMessages();
        var a = list.Push(90, 0); list.Push(91, 0); var c = list.Push(92, 0);
        list.Delete(1); Assert.Equal(new[] { a, c }, list.Records);
        list.Delete(1); Assert.Same(a, Assert.Single(list.Records));
        list.Delete(0); Assert.Equal(0, list.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => list.Delete(0));
    }

    // REJECTS blank advisor cards, filtering the adjacent IDs, gating on voice, and eviction BEFORE suppression.
    [Theory]
    [InlineData(0x123, true, true)] [InlineData(0x124, true, false)] [InlineData(0x125, true, true)]
    [InlineData(0x123, false, false)] [InlineData(0x124, false, false)] [InlineData(0x125, false, false)]
    public void AdvisorGateSuppressesTheWholePush(int textId, bool enabled, bool pushed)
    {
        var list = Full(); var before = list.Records.ToArray(); var target = new object();
        var card = list.PushAdvisor((ushort)textId, 2, target, enabled);
        Assert.Equal(32, list.Count);
        if (pushed)
        {
            Assert.NotNull(card); Assert.Same(card, list.Records[31]);
            Assert.Equal((short)textId, card.TextId); Assert.Equal(2u, card.Kind); Assert.Same(target, card.Target);
            Assert.Equal(before.Skip(1), list.Records.Take(31));
        }
        else { Assert.Null(card); Assert.Equal(before, list.Records); }
    }

    // REJECTS a vacuous/shortened table census and a filter that drops voiced advice with real captions.
    [Fact]
    public void All289AdvisorEntriesHave46SuppressedAnd243PositiveControls()
    {
        Assert.Equal(289, AdvisorMessages.All.Length);
        int suppressed = 0, accepted = 0;
        foreach (var message in AdvisorMessages.All)
        {
            var list = new ParkMessages();
            var card = list.PushAdvisor(message.TextId, 0);
            if (message.TextId == 0x124)
            { suppressed++; Assert.Null(card); Assert.Equal(0, list.Count); }
            else
            { accepted++; Assert.NotNull(card); Assert.Same(card, Assert.Single(list.Records)); }
        }
        output.WriteLine($"Scanned {suppressed + accepted}; suppressed {suppressed}; positive controls {accepted}; unexamined 0.");
        Assert.Equal(46, suppressed); Assert.Equal(243, accepted);
    }

    // REJECTS applying the advisor's sentinel filter to the generic append or restore route.
    [Fact]
    public void GenericRecordsKeep124AndPosterRetractionStillHonoursItsGate()
    {
        var list = new ParkMessages();
        var card = list.Push(0x124, 0);
        Assert.Same(card, Assert.Single(list.Records));
        Assert.False(list.RetractAdvisor(0x124)); Assert.Same(card, Assert.Single(list.Records));
        Assert.True(list.Retract(0x124)); Assert.Empty(list.Records);
        list.RestoreMessage(new(0x124, Array.Empty<byte>(), 0, -1, 0), new Host());
        Assert.Equal(0x124, Assert.Single(list.Records).TextId);
    }

    // REJECTS retracting when Text is disabled, ignoring the requested text, or disabling all retractions.
    [Fact]
    public void PosterRetractionUsesTheTextFlagAndTextId()
    {
        var list = new ParkMessages(); var first = list.Push(123, 0); var last = list.Push(321, 0);
        Assert.False(list.RetractAdvisor(123, false)); Assert.Equal(2, list.Count);
        Assert.False(list.RetractAdvisor(122)); Assert.Equal(new[] { first, last }, list.Records);
        Assert.True(list.RetractAdvisor(123)); Assert.Same(last, Assert.Single(list.Records));
    }

    // REJECTS interpreting +10C=3600 as an expiry, or removing a card when its words/action are read.
    [Fact]
    public void CardsSurvivePast3600TicksAndRepeatedViewing()
    {
        var list = new ParkMessages(); var host = new Host(); var card = list.Push(23, 0);
        for (int i = 0; i < 10000; i++) list.Tick();
        Assert.Same(card, Assert.Single(list.Records));
        for (int i = 0; i < 4; i++) { list.TextAt(0, host); Assert.False(list.Activate(0, host)); }
        Assert.Equal(4, host.TextReads.Count); Assert.Same(card, Assert.Single(list.Records));
        list.Delete(0); Assert.Empty(list.Records); // Positive removal control.
    }

    // REJECTS cached/localized-at-push strings, substitution/wrapping, the wrong ID, or decoding IDs as bytes.
    [Fact]
    public void TextIsFetchedWholeAtOpenTimeAndInlineTextUsesTheHostEncoding()
    {
        var list = new ParkMessages(); var host = new Host();
        list.Push(0x8023, 2, new object());
        Assert.Empty(host.TextReads); Assert.Empty(host.Decodes);
        Assert.Equal("A whole %s string {0} with spaces", list.TextAt(0, host));
        host.Text = "A different language";
        Assert.Equal("A different language", list.TextAt(0, host));
        Assert.Equal(new short[] { -32733, -32733 }, host.TextReads);
        list.PushInline(new byte[] { 0x80, 0xE3, 0x41 }, 1);
        Assert.Equal("decoded by host", list.TextAt(1, host));
        Assert.Equal(new byte[] { 0x80, 0xE3, 0x41 }, Assert.Single(host.Decodes));
        Assert.Equal(2, host.TextReads.Count); Assert.Empty(host.Jumps); Assert.Empty(host.Replays);
    }

    // REJECTS conflating hover with activation, replay with camera jump, narrowing kind to a byte,
    // closing on replay, deleting on activation, and passing the object as a formatting parameter.
    [Theory]
    [InlineData(0u)] [InlineData(1u)] [InlineData(2u)] [InlineData(3u)] [InlineData(4u)] [InlineData(0x103u)]
    public void ActivationDispatchesOnlyKindsTwoAndThree(uint kind)
    {
        var list = new ParkMessages(); var host = new Host(); var target = new object();
        var card = list.Push(0x8123, kind, target);
        Assert.Empty(host.Jumps); Assert.Empty(host.Replays);
        Assert.Equal(kind == 2, list.Activate(0, host));
        if (kind == 2) Assert.Same(target, Assert.Single(host.Jumps)); else Assert.Empty(host.Jumps);
        if (kind == 3) Assert.Equal(-32477, Assert.Single(host.Replays)); else Assert.Empty(host.Replays);
        Assert.Empty(host.TextReads); Assert.Empty(host.Decodes);
        Assert.Same(card, Assert.Single(list.Records));
    }

    // REJECTS an invented 254/256-byte wire limit, implicit text truncation, and eviction on invalid input.
    [Fact]
    public void InlineTextAcceptsZeroThrough255BytesAndRejects256BeforeEviction()
    {
        var list = Full(); var before = list.Records.ToArray();
        Assert.Throws<ArgumentException>(() => list.PushInline(new byte[256], 1));
        Assert.Equal(before, list.Records);
        var bytes = Enumerable.Repeat((byte)0xE3, 255).ToArray();
        var card = list.PushInline(bytes, 1); Assert.Equal(bytes, card.InlineText.ToArray());
        Assert.Empty(list.PushInline(Array.Empty<byte>(), 1).InlineText.ToArray());
        Assert.Equal(32, list.Count);
    }

    // REJECTS an empty save proof, reordered/lost fields, persisting kind 4, looking up non-object kinds,
    // treating targets as IDs/pointers, UTF-8 conversion, and reusing old live objects on restore.
    [Fact]
    public void NonemptyMessagesRoundTripThroughTheExistingParkCodecWithFreshTargets()
    {
        var list = new ParkMessages(); var writer = new Host(); var reader = new Host();
        var a = new object(); var b = new object(); var newA = new object(); var newB = new object();
        writer.Addresses.Add(a, (6, 19)); writer.Addresses.Add(b, (7, 23));
        reader.Objects.Add((6, 19), newA); reader.Objects.Add((7, 23), newB);
        list.Push(0x3456, 0, new object());
        list.PushInline(new byte[] { 0x80, 0xE3 }, 1);
        list.Push(0x2345, 2, a);
        list.PushInline(new byte[] { 0xD7, 0x29, 0x41 }, 2, b);
        list.Push(700, 2);
        list.Push(800, 3, new object());
        list.Push(900, 4, new object());
        list.Push(0x124, 0);
        list.Push(0x8023, 1);
        var saved = list.CaptureMessages(writer);
        Assert.Equal(9, list.Count); Assert.Equal(8, saved.Length);
        Assert.Equal(new object[] { a, b }, writer.Located);
        Assert.Equal(new short[] { 0x3456, -1, 0x2345, -1, 700, 800, 0x124, -32733 }, saved.Select(m => m.StringId));
        Assert.Equal(new byte[] { 0, 1, 2, 2, 2, 3, 0, 1 }, saved.Select(m => m.Type));
        Assert.Equal(new sbyte[] { -1, -1, 6, 7, -1, -1, -1, -1 }, saved.Select(m => m.TargetType));
        Assert.Equal(new byte[] { 0, 0, 19, 23, 0, 0, 0, 0 }, saved.Select(m => m.TargetIndex));
        var park = new ParkSave(SaveFixture.Layout()); park.Messages.AddRange(saved);
        var bytes = ParkSaveCodec.Write(park);
        var decoded = ParkSaveCodec.Read(bytes, park.Layout);
        Assert.Equal(8, decoded.Messages.Count);
        var restored = new ParkMessages(); Assert.Equal(0, restored.Count); // Fresh/omitted-load control.
        foreach (var message in decoded.Messages) restored.RestoreMessage(message, reader);
        Assert.Equal(8, restored.Count);
        Assert.Equal(new[] { ((sbyte)6, (byte)19), ((sbyte)7, (byte)23) }, reader.Resolved);
        Assert.Same(newA, restored.Records[2].Target); Assert.Same(newB, restored.Records[3].Target);
        Assert.Null(restored.Records[0].Target); Assert.Null(restored.Records[4].Target); Assert.Null(restored.Records[5].Target);
        Assert.Equal(new byte[] { 0x80, 0xE3 }, restored.Records[1].InlineText.ToArray());
        Assert.Equal(new byte[] { 0xD7, 0x29, 0x41 }, restored.Records[3].InlineText.ToArray());
        Assert.Equal(saved.Select(m => m.StringId), restored.Records.Select(m => m.TextId));
        Assert.Equal(saved.Select(m => (uint)m.Type), restored.Records.Select(m => m.Kind));
        decoded.Messages[1].Text[0] = 0;
        Assert.Equal(0x80, restored.Records[1].InlineText[0]);
        saved[1].Text[0] = 0;
        Assert.Equal(0x80, list.Records[1].InlineText[0]);
        Assert.Empty(reader.TextReads); Assert.Empty(reader.Decodes); Assert.Empty(reader.Jumps); Assert.Empty(reader.Replays);
        output.WriteLine("Live 9; saved/restored 8; excluded kind-4 1; remapped targets 2; unexamined 0.");
    }

    // REJECTS narrowing kind before the kind-4 filter and losing original inline bytes on capture.
    [Fact]
    public void SaveNarrowsTheKindOnlyAtTheWireBoundary()
    {
        var list = new ParkMessages();
        list.PushInline(new byte[] { 0xFE }, 0x104);
        var saved = Assert.Single(list.CaptureMessages(new Host()));
        Assert.Equal(4, saved.Type); Assert.Equal(new byte[] { 0xFE }, saved.Text);
    }

    // REJECTS silently dropping a target when host mapping fails and mutating membership on failed restore.
    [Fact]
    public void MissingObjectMappingsAreExplicitHostErrors()
    {
        var list = new ParkMessages(); var host = new Host(); var target = new object();
        list.Push(22, 2, target); host.Addresses.Add(target, (-1, 0));
        Assert.Throws<InvalidOperationException>(() => list.CaptureMessages(host)); Assert.Equal(1, list.Count);
        host.Objects.Add((6, 19), null);
        Assert.Throws<InvalidOperationException>(() => list.RestoreMessage(new(23, Array.Empty<byte>(), 2, 6, 19), host));
        Assert.Equal(1, list.Count); Assert.Equal(22, Assert.Single(list.Records).TextId);
    }
}
