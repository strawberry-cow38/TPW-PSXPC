using System;
using System.Collections.Generic;
using System.Linq;

namespace TPW.Sim;

/// <summary>READ: dimensions come from the loaded map (0x80072840..870); catalogue counts
/// come from its definitions (0x80072B54). Neither is embedded in the park stream.
/// Restricted is the runtime flag, NOT card-header byte 42 (see SaveArchive).</summary>
public sealed record ParkSaveLayout(byte World, byte Park, int Width, int Height,
    bool Restricted, int[] CatalogueCounts)
{
    // READ: eight definition classes, in the order used at 0x800715F0 / 0x80072B54.
    public static IReadOnlyList<int> CatalogueTypes { get; } = Array.AsReadOnly(new[] { 3, 7, 6, 1, 2, 4, 5, 8 });
    internal int TileBytes
    {
        get
        {
            // Port buffer validation: signed-halfword length in 0x80071E48 must stay positive.
            if (Width <= 0 || Height <= 0 || (long)Width * Height > short.MaxValue)
                throw new ArgumentException("Map dimensions exceed the original stream reader's positive length.");
            return Width * Height;
        }
    }
    internal int CatalogueBytes
    {
        get
        {
            if (CatalogueCounts == null || CatalogueCounts.Length != CatalogueTypes.Count || CatalogueCounts.Any(n => n < 0))
                throw new ArgumentException("Supply all eight catalogue counts in disc order.");
            return checked(CatalogueCounts.Sum() * 2);
        }
    }
}

/// <summary>READ: full UNPACKED park stream, 0x80070948 / 0x80071D0C. This is a save image,
/// not a snapshot of live pointers or an exact continuation of a frame. Unknown bytes are retained.
/// Host supplies the existing map/catalogue and constructs runtime objects through IParkSaveHost.</summary>
public sealed class ParkSave
{
    public ParkSaveLayout Layout { get; }
    /// <summary>READ: 0x3039 is written at 0x80070BAC. ⚠ DO NOT FIX: loader does not test it.</summary>
    public ushort Marker { get; set; } = 0x3039;
    /// <summary>UNKNOWN: header +17, one byte; writer copies unwritten stack storage.</summary>
    public byte UnknownHeader17 { get; set; }
    public byte Open { get; set; }
    /// <summary>UNKNOWN: gate record +1, one byte, not written/read semantically.</summary>
    public byte UnknownGate1 { get; set; }
    /// <summary>READ: unsigned whole pounds, 0x80070C78..90 / 0x80071F6C..80.</summary>
    public ushort EntryFeePounds { get; set; }
    /// <summary>READ: W*H bytes, although only ceil(W*H/8) encode path bits. 0x80071530..44.
    /// ⚠ DO NOT FIX the excess reservation. Unused bytes are UNKNOWN, not extra terrain.</summary>
    public byte[] Paths { get; set; }
    /// <summary>READ: type order 1,6,3,7,2,4,5, 0x80070D04..F68. Individual layouts in save.md.</summary>
    public List<AttractionSave>[] Attractions { get; } = Enumerable.Range(0, 7).Select(_ => new List<AttractionSave>()).ToArray();
    /// <summary>READ: header +16 is count, followed by ONE shared 26-byte range record, even at zero.</summary>
    public byte VisitorCount { get; set; }
    public VisitorSaveRanges Visitors { get; set; } = VisitorSaveRanges.Capture(Array.Empty<(Visitor, byte)>());
    /// <summary>READ: guard, researcher, mechanic, handyman, entertainer, 0x80071278..7142C.</summary>
    public List<StaffSave>[] Staff { get; } = Enumerable.Range(0, 5).Select(_ => new List<StaffSave>()).ToArray();
    /// <summary>READ: ordinary then vomit; 0x80071584 / litter.md.</summary>
    public byte OrdinaryLitter { get; set; }
    public byte Vomit { get; set; }
    public BankSave Bank { get; set; } = new();
    public CalendarSave Calendar { get; set; } = new();
    /// <summary>READ: whole-percent, completed-level pairs; catalogue order is supplied by Layout.
    /// Omitted entirely in restricted mode, including the research BANK.</summary>
    public byte[] Catalogue { get; set; }
    /// <summary>READ: funding plus five (active,type,signed index) triples; research.md §4.</summary>
    public byte[] ResearchTopics { get; set; }
    public List<ParkSaveMessage> Messages { get; } = new();
    /// <summary>UNKNOWN: alignment bytes, in stream order. Read preserves them. For a newly captured
    /// image an empty array requests zero-filled padding (port storage policy; PSX skips unwritten RAM).</summary>
    public byte[] Padding { get; set; } = Array.Empty<byte>();

    public ParkSave(ParkSaveLayout layout)
    {
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        Paths = new byte[layout.TileBytes];
        Catalogue = new byte[layout.Restricted ? 0 : layout.CatalogueBytes];
        ResearchTopics = new byte[layout.Restricted ? 0 : 16]; // READ: 0x80072E1C.
    }
}

/// <summary>READ: 0x800719CC / 0x800729A0. Type 4 messages are excluded by capture.
/// Text bytes use the game's encoding; no Unicode or terminator is invented.</summary>
public sealed record ParkSaveMessage(short StringId, byte[] Text, byte Type, sbyte TargetType, byte TargetIndex);

/// <summary>READ: aligned little-endian park writer/reader at 0x80070948 / 0x80071D0C.
/// Buffer boundary checks are port validation, not claims that the PSX rejects corrupt saves safely.</summary>
public static class ParkSaveCodec
{
    /// <summary>READ: 0x80071430, row-major LSB-first flags for types 2 and 13 ONLY.
    /// Queue/entrance tiles are rebuilt by attraction records. UNKNOWN excess storage is zeroed
    /// for a new capture (port storage policy), not filled with invented terrain fields.</summary>
    public static byte[] CapturePaths(ParkSaveLayout layout, Func<int, int, int> tileType)
    {
        var bytes = new byte[layout.TileBytes];
        for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
            {
                int type = tileType(x, y), tile = y * layout.Width + x;
                if (type == 2 || type == 13) bytes[tile >> 3] |= (byte)(1 << (tile & 7));
            }
        return bytes;
    }

    public static byte[] Write(ParkSave park)
    {
        ArgumentNullException.ThrowIfNull(park);
        var w = new SaveCursor(park.Padding, writing: true);
        w.U16(park.Marker); w.Byte(park.Layout.World); w.Byte(park.Layout.Park);
        foreach (var group in park.Attractions) w.Byte(Count(group.Count));
        foreach (var group in park.Staff) w.Byte(Count(group.Count));
        w.Byte(park.VisitorCount); w.Byte(park.UnknownHeader17); w.Align(4);
        w.Byte(park.Open); w.Byte(park.UnknownGate1); w.U16(park.EntryFeePounds);
        w.Block(park.Paths, park.Layout.TileBytes); w.Align(4);
        for (int i = 0; i < park.Attractions.Length; i++)
            foreach (var record in park.Attractions[i])
            {
                if (record.Type != AttractionSave.Types[i]) throw new ArgumentException("Attraction in wrong save block.");
                w.Block(record.Bytes, AttractionSave.Sizes[i]); w.Align(4);
            }
        w.Block(park.Visitors.Bytes, 26); w.Align(4);
        foreach (var group in park.Staff)
            foreach (var record in group) { w.Block(record.Bytes, 16); w.Align(4); }
        w.Byte(park.OrdinaryLitter); w.Byte(park.Vomit); w.Align(4);
        w.Block(park.Bank.Bytes, 0x328); w.Align(4);
        w.Block(park.Calendar.Bytes, 0xDC); w.Align(4);
        w.Block(park.Catalogue, park.Layout.Restricted ? 0 : park.Layout.CatalogueBytes);
        w.Block(park.ResearchTopics, park.Layout.Restricted ? 0 : 16);
        if (!park.Layout.Restricted) w.Align(4);
        var messages = park.Messages.Where(m => m.Type != 4).ToArray(); // READ: 0x80071A2C / A88.
        w.Byte(Count(messages.Length));
        foreach (var message in messages)
        {
            w.Align(2); w.U16(unchecked((ushort)message.StringId));
            if (message.StringId == -1) { w.Byte(Count(message.Text.Length)); w.Block(message.Text, message.Text.Length); }
            w.Byte(message.Type);
            // READ: only type 2 with a target writes its index (0x80071B24..94).
            sbyte target = message.Type == 2 ? message.TargetType : (sbyte)-1;
            w.Byte(unchecked((byte)target));
            if (target != -1) w.Byte(message.TargetIndex);
        }
        return w.FinishWriting();
    }

    public static ParkSave Read(ReadOnlySpan<byte> bytes, ParkSaveLayout layout)
    {
        var r = new SaveCursor(bytes.ToArray());
        var park = new ParkSave(layout) { Marker = r.U16() };
        byte world = r.Byte(), subpark = r.Byte();
        if (world != layout.World || subpark != layout.Park) throw new FormatException("Save belongs to a different map.");
        var counts = r.Block(12);
        park.VisitorCount = r.Byte(); park.UnknownHeader17 = r.Byte(); r.Align(4);
        park.Open = r.Byte(); park.UnknownGate1 = r.Byte(); park.EntryFeePounds = r.U16();
        park.Paths = r.Block(layout.TileBytes); r.Align(4);
        for (int i = 0; i < park.Attractions.Length; i++)
            for (int j = 0; j < counts[i]; j++)
            { park.Attractions[i].Add(new(AttractionSave.Types[i], r.Block(AttractionSave.Sizes[i]))); r.Align(4); }
        park.Visitors = new(r.Block(26)); r.Align(4);
        for (int i = 0; i < park.Staff.Length; i++)
            for (int j = 0; j < counts[7 + i]; j++) { park.Staff[i].Add(new(r.Block(16))); r.Align(4); }
        park.OrdinaryLitter = r.Byte(); park.Vomit = r.Byte(); r.Align(4);
        park.Bank = new(r.Block(0x328)); r.Align(4);
        park.Calendar = new(r.Block(0xDC)); r.Align(4);
        park.Catalogue = r.Block(layout.Restricted ? 0 : layout.CatalogueBytes);
        park.ResearchTopics = r.Block(layout.Restricted ? 0 : 16);
        if (!layout.Restricted) r.Align(4);
        int count = r.Byte();
        for (int i = 0; i < count; i++)
        {
            r.Align(2);
            short id = unchecked((short)r.U16());
            byte[] text = id == -1 ? r.Block(r.Byte()) : Array.Empty<byte>();
            byte type = r.Byte(); sbyte target = unchecked((sbyte)r.Byte());
            byte index = target == -1 ? (byte)0 : r.Byte();
            park.Messages.Add(new(id, text, type, target, index));
        }
        r.RequireEnd(); park.Padding = r.Padding.ToArray();
        return park;
    }

    static byte Count(int count) => checked((byte)count); // READ: count storage is u8; refuse host overflow.
}

/// <summary>Internal byte cursor. Alignment READ: 0x80070B60 / 0x80071E98.</summary>
internal sealed class SaveCursor
{
    readonly byte[] input;
    readonly List<byte> output;
    readonly byte[] suppliedPadding;
    int position, paddingPosition;
    public List<byte> Padding { get; } = new();
    public SaveCursor(byte[] bytes) { input = bytes; }
    public SaveCursor(byte[] padding, bool writing) { output = new(); suppliedPadding = padding; }
    public byte Byte()
    {
        if (position >= input.Length) throw new FormatException("Truncated park save.");
        return input[position++];
    }
    public ushort U16() => (ushort)(Byte() | Byte() << 8);
    public byte[] Block(int size)
    {
        if (size < 0 || size > input.Length - position) throw new FormatException("Truncated park save block.");
        byte[] result = input.AsSpan(position, size).ToArray(); position += size; return result;
    }
    public void Byte(byte value) { output.Add(value); position++; }
    public void U16(ushort value) { Byte((byte)value); Byte((byte)(value >> 8)); }
    public void Block(byte[] bytes, int size)
    {
        if (bytes == null || bytes.Length != size) throw new ArgumentException("Incorrect save block size.");
        output.AddRange(bytes); position += size;
    }
    public void Align(int alignment)
    {
        int count = (alignment - position % alignment) % alignment;
        for (int i = 0; i < count; i++)
            if (output == null) Padding.Add(Byte());
            else
            {
                if (suppliedPadding.Length != 0 && paddingPosition >= suppliedPadding.Length)
                    throw new ArgumentException("Missing saved padding bytes.");
                Byte(suppliedPadding.Length == 0 ? (byte)0 : suppliedPadding[paddingPosition++]);
            }
    }
    public byte[] FinishWriting()
    {
        if (suppliedPadding.Length != paddingPosition) throw new ArgumentException("Unused saved padding bytes.");
        return output.ToArray();
    }
    public void RequireEnd() { if (position != input.Length) throw new FormatException("Trailing park save data or wrong catalogue/mode."); }
}
