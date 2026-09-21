using System;
using System.Collections.Generic;

namespace TPW.Sim;

/// <summary>Host services for the message list. Text is requested only when a card is opened;
/// the host owns localization, glyph decoding, wrapping, drawing, input and card animation.</summary>
public interface IParkMessageHost
{
    /// <summary>READ: 0x8003BB18 → 0x8006F00C. Fetch the whole string; no substitutions.</summary>
    string GetText(short textId);
    /// <summary>READ: textId -1 uses rec+8, in the game's encoding. No UTF-8 assumption.</summary>
    string DecodeText(ReadOnlySpan<byte> text);
    /// <summary>READ: kind 2, 0x8003B8BC..914. Resolve the live object's position and move the
    /// camera. A missing/stale target is a host error, not an ordinary card's no-op.</summary>
    void JumpToObject(object target);
    /// <summary>READ: kind 3 → 0x8001408C. Find the first advisor entry at id 221 or later with
    /// this TEXT id and repost with flag bits 0 and 5 cleared. Keep replay out of this list.</summary>
    void ReplayMessage(short textId);
}

/// <summary>The object-list seam of save.md §3.6, separate from display services. Supply actual
/// saved list order, not object IDs or process pointers. Missing mappings must throw; silently
/// losing a kind-2 link would change the card's action. This validation is a port contract.</summary>
public interface IParkMessageSaveHost
{
    /// <summary>READ: 0x80071B24..B94. Return a nonnegative type and its byte list index.</summary>
    (sbyte Type, byte Index) LocateMessageTarget(object target);
    /// <summary>READ: loader 0x800729A0 calls 0x8005BEC0(type,index). Object lists must exist
    /// before restoring messages. Return the new live object, or throw if it cannot be resolved.</summary>
    object ResolveMessageTarget(sbyte type, byte index);
}

/// <summary>READ: payload of a 0x120-byte record (advisor-presentation.md §3): kind at +0,
/// inline bytes at +8..107, signed text id at +108, object at +11C. Managed storage is not a
/// packed PSX struct. State/cascade/slide/scroll belong to host presentation. The unused +10C
/// value 0xE10 is deliberately NOT treated as a lifetime. Records are immutable snapshots.</summary>
public sealed class ParkMessage
{
    public uint Kind { get; }
    public short TextId { get; }
    public object Target { get; }
    readonly byte[] text;
    public ReadOnlySpan<byte> InlineText => text;

    internal ParkMessage(short textId, uint kind, object target, ReadOnlySpan<byte> inlineText)
    {
        TextId = textId;
        Kind = kind;
        Target = target;
        text = inlineText.ToArray();
    }
}

/// <summary>READ: HUD list at 0x801069E8, registered by 0x800383EC → 0x80039CDC. Owns
/// thirty-two cards in oldest-first order, independent of the advisor's delivery queue.
/// Host seam: at the existing ShowCaption delivery site call PushAdvisor with the delivered
/// text id, resolved card kind and live object (not format arguments). PostMessage/TakeMessage
/// transport advice to that delivery site; poster retraction (0x800139EC) separately calls
/// RetractAdvisor with the message's looked-up text id and Text flag. ParkHud.Messages reads
/// Count, replacing _advisor.Delivered. The advisor author removes
/// AdvisorCaption/PaintAdvisor. Do not wire HideCaption or the end of speech to deletion.
/// L2, selection, badge/cards, sounds and delayed player-delete animation stay in the host;
/// Delete commits a player removal. See findings/messages.md for integration and limits.</summary>
public sealed class ParkMessages
{
    /// <summary>READ: 0x8003A4A0 compares count against 0x20; stride 0x120 at 0x8003A4CC.</summary>
    public const int Capacity = 32;
    /// <summary>READ: 256-byte inline buffer (+8..107), terminating zero; save.md §3.6 saves
    /// a byte length without that terminator. The port rejects overlong input before eviction.</summary>
    public const int MaxInlineBytes = 255;
    readonly List<ParkMessage> live = new(Capacity);
    public IReadOnlyList<ParkMessage> Records { get; }
    public int Count => live.Count;

    public ParkMessages() => Records = live.AsReadOnly();

    /// <summary>READ: 0x800141BC stores three fields; 0x8003A484 copies the record. The text
    /// setter truncates to a signed halfword. No duplicate suppression, formatting or lookup.
    /// Generic append does NOT filter 0x124; that guard belongs to advisor delivery.</summary>
    public ParkMessage Push(int textId, uint kind, object target = null)
        => Append(new ParkMessage(unchecked((short)textId), kind, target, ReadOnlySpan<byte>.Empty));

    /// <summary>READ: 0x8003BB18 selects inline text for -1. Supply original encoded bytes
    /// without a terminator; caller mutation cannot rewrite a copied card.</summary>
    public ParkMessage PushInline(ReadOnlySpan<byte> text, uint kind, object target = null)
    {
        if (text.Length > MaxInlineBytes) throw new ArgumentException("Message text exceeds its inline buffer.", nameof(text));
        return Append(new ParkMessage(-1, kind, target, text));
    }

    /// <summary>READ: 0x80013F20 / 0x80013F4C bypass the ENTIRE construction and push at
    /// 0x80013F54..84 when Text is disabled or textId == 0x124. Voice delivery is independent.
    /// Pass the queue's resolved kind: 0 ordinary, 2 attached object, 3 tutorial/replay.</summary>
    public ParkMessage PushAdvisor(ushort textId, byte kind, object target = null, bool textEnabled = true)
    {
        if (!textEnabled || textId == AdvisorMessages.NoText) return null;
        return Push(textId, kind, target);
    }

    ParkMessage Append(ParkMessage message)
    {
        // READ: 0x8003A4AC..B8 removes slot zero BEFORE appending. ⚠ DO NOT FIX: even
        // identical advice consumes another slot and can evict an unrelated oldest card.
        if (live.Count == Capacity) live.RemoveAt(0);
        live.Add(message);
        return message;
    }

    /// <summary>READ: 0x8003A65C removes the FIRST matching signed text id immediately,
    /// leaving later duplicates and preserving order. No match is harmless.
    /// ⚠ SOURCE DISAGREEMENT: retain §3.5's first-match rule. 0x8003A6A4 flushes a pending
    /// animated deletion before removing the previously found index at 0x8003A6AC..B4;
    /// compaction can shift that index. This component has no pending animation state.
    /// Both readings and the unported overlap case are recorded in findings/messages.md §0.</summary>
    public bool Retract(int textId)
    {
        short id = unchecked((short)textId);
        for (int i = 0; i < live.Count; i++)
            if (live[i].TextId == id)
            {
                live.RemoveAt(i);
                return true;
            }
        return false;
    }

    /// <summary>READ: 0x800139EC gates poster retraction on flag bit 0 and excludes 0x124
    /// at 0x80013A28. This takes a TEXT id, not an advisor message id.</summary>
    public bool RetractAdvisor(ushort textId, bool textEnabled = true)
        => textEnabled && textId != AdvisorMessages.NoText && Retract(textId);

    /// <summary>READ: logical deletion/compaction in 0x8003A52C. The host commits after its
    /// player-delete animation; poster retraction and capacity eviction commit immediately.
    /// An invalid index is a host error. Selection and pending animation indices are host-owned.</summary>
    public void Delete(int index) => live.RemoveAt(index);

    /// <summary>READ: advisor-presentation.md §3.6. ⚠ DO NOT FIX: cards never expire.
    /// Hosts may omit this call; there is no simulation clock or caption timer to supply.</summary>
    public void Tick() { }

    public string TextAt(int index, IParkMessageHost host)
    {
        var message = live[index];
        return message.TextId == -1 ? host.DecodeText(message.InlineText) : host.GetText(message.TextId);
    }

    /// <summary>READ: 0x8003B878, the OK/Replay action, not selection movement. Return true
    /// only for kind 2 so the host closes the list; kind 3 replays without removing the card.</summary>
    public bool Activate(int index, IParkMessageHost host)
    {
        var message = live[index];
        switch (message.Kind)
        {
            case 2: host.JumpToObject(message.Target); return true;
            case 3: host.ReplayMessage(message.TextId); break;
        }
        return false;
    }

    /// <summary>READ: save.md §3.6 / 0x800719CC. Snapshot in list order, excluding kind 4;
    /// only kind 2 carries an object-list address. ParkSaveCodec owns wire alignment/encoding.
    /// Add these to Capture().Messages; this does not wire game/ParkSaveHost.</summary>
    public ParkSaveMessage[] CaptureMessages(IParkMessageSaveHost host)
    {
        var saved = new List<ParkSaveMessage>();
        foreach (var message in live)
        {
            if (message.Kind == 4) continue;
            (sbyte Type, byte Index) target = (-1, 0);
            if (message.Kind == 2 && message.Target != null)
            {
                target = host.LocateMessageTarget(message.Target);
                if (target.Type < 0) throw new InvalidOperationException("Message target has no saved object-list address.");
            }
            saved.Add(new ParkSaveMessage(message.TextId, message.InlineText.ToArray(),
                unchecked((byte)message.Kind), target.Type, target.Index));
        }
        return saved.ToArray();
    }

    /// <summary>READ: save.md §3.6 / 0x800729A0. Call from IParkSaveHost.RestoreMessage on
    /// a fresh list after restoring objects. This is a generic append, without advisor gates;
    /// presentation starts fresh. No display/action services run during restoration.</summary>
    public ParkMessage RestoreMessage(ParkSaveMessage saved, IParkMessageSaveHost host)
    {
        object target = null;
        if (saved.TargetType != -1)
            target = host.ResolveMessageTarget(saved.TargetType, saved.TargetIndex)
                ?? throw new InvalidOperationException("Message target could not be restored.");
        return saved.StringId == -1 ? PushInline(saved.Text, saved.Type, target)
                                   : Push(saved.StringId, saved.Type, target);
    }
}
