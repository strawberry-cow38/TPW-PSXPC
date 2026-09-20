using System;
using System.Collections.Generic;

namespace TPW.Sim;

/// <summary>READ: 0x80070FA4 collects twelve population min/range pairs, not guest records.
/// Pair order: rubbish, happiness, nausea, need A, boredom, ride desire, need B, tiredness,
/// raw money, current speed, normal speed, UNKNOWN V+0x63. Money uses u16s; all others u8s.</summary>
public sealed class VisitorSaveRanges : SaveRecord
{
    public VisitorSaveRanges(byte[] bytes = null) : base(26, bytes) { }
    public (int Minimum, int Range) Get(int field)
    {
        if ((uint)field >= 12) throw new ArgumentOutOfRangeException(nameof(field));
        if (field == 8) return (U16(16), U16(18));
        int offset = field < 8 ? field * 2 : field * 2 + 2;
        return (Bytes[offset], Bytes[offset + 1]);
    }
    /// <summary>READ: byte/halfword narrowing in 0x80071168..1264, including wrapped ranges.</summary>
    public void Set(int field, int minimum, int range)
    {
        if ((uint)field >= 12) throw new ArgumentOutOfRangeException(nameof(field));
        if (field == 8) { U16(16, unchecked((ushort)minimum)); U16(18, unchecked((ushort)range)); return; }
        int offset = field < 8 ? field * 2 : field * 2 + 2;
        Bytes[offset] = unchecked((byte)minimum); Bytes[offset + 1] = unchecked((byte)range);
    }

    /// <summary>READ: 0x80070FA4. The host supplies V+0x63, whose semantics are UNKNOWN.
    /// ⚠ DO NOT FIX: first ten minima start at 9999, final two at ZERO, every maximum at zero.
    /// The empty-population record is consequently not a zero record.</summary>
    public static VisitorSaveRanges Capture(IEnumerable<(Visitor Guest, byte Unknown63)> guests)
    {
        var minimum = new int[12]; var maximum = new int[12];
        for (int i = 0; i < 10; i++) minimum[i] = 9999;
        foreach (var (v, unknown) in guests)
        {
            int[] values = { v.Rubbish, v.Happiness, v.Nausea, v.NeedA, v.Boredom, v.RideDesire,
                v.NeedB, v.Tiredness, unchecked((short)v.Money.Raw), v.WalkSpeed, v.NormalWalkSpeed, unknown };
            for (int i = 0; i < values.Length; i++)
            { minimum[i] = Math.Min(minimum[i], values[i]); maximum[i] = Math.Max(maximum[i], values[i]); }
        }
        var ranges = new VisitorSaveRanges();
        for (int i = 0; i < minimum.Length; i++) ranges.Set(i, minimum[i], maximum[i] - minimum[i]);
        return ranges;
    }

    /// <summary>READ: 0x80091A3C, on a FRESH, already initialized Visitor supplied by the host.
    /// The original initializes again in slot 2; host owns that initialization and entrance jitter.
    /// ⚠ DO NOT FIX: nausea and boredom ranges are written but NEVER READ. Keep constructor values.
    /// SOURCE DISAGREEMENT save.md §0: use the existing findings' Stat.Clamp, not signed-byte overflow.</summary>
    public byte Restore(Visitor guest, IRandomSource random)
    {
        int Draw(int field, bool high)
        {
            var (min, range) = Get(field); int bound = range + 1;
            int first = random.Next(bound), second = random.Next(bound);
            // READ: 0x8009197C / 0x800919C4. High-biased result may exceed the saved maximum!
            return min + (high ? bound - first * second / bound : (first + second) / 2);
        }
        guest.SetState(VisitorState.WalkIn); // READ: 45 at 0x80091AA8.
        guest.Rubbish = Draw(0, false);
        guest.Happiness = Draw(1, false);
        guest.NeedA = Draw(3, true);
        guest.RideDesire = Draw(5, true);
        guest.NeedB = Draw(6, true);
        guest.Tiredness = Draw(7, true);
        guest.Money = Money.FromRaw(Draw(8, false));
        guest.WalkSpeed = unchecked((sbyte)Draw(9, false));
        guest.NormalWalkSpeed = unchecked((byte)Draw(10, false));
        byte unknown = unchecked((byte)Draw(11, false));
        guest.VisitorType = random.Next(8); // READ: 0x80091BD8.
        guest.Animation = 13; guest.Facing = 0; // READ: 0x00680000, 0x80091BEC..C10.
        return unknown;
    }
}

/// <summary>READ: host services needed for count-based litter reconstruction, 0x80053A98.</summary>
public interface ILitterSaveWorld : ILitterWorld
{
    int Width { get; }
    int Height { get; }
    int TileType(int x, int y);
}

public static class LitterSave
{
    /// <summary>READ: allocate, retry random x/y until type 2/4/7, centre, scatter, then mark vomit.
    /// ⚠ DO NOT FIX: no retry cap, no allocation failure recovery. Host must supply the restored
    /// path map and a fresh pool with capacity, as on the valid original load path (litter.md §2).</summary>
    public static void Restore(byte ordinary, byte vomit, LitterPool pool, ILitterSaveWorld world, IRandomSource random)
    {
        for (int kind = 0; kind < 2; kind++)
            for (int i = 0; i < (kind == 0 ? ordinary : vomit); i++)
            {
                var piece = pool.TryAllocate(world, random);
                int x, y, type;
                do { x = random.Next(world.Width); y = random.Next(world.Height); type = world.TileType(x, y); }
                while (type != 2 && type != 4 && type != 7);
                piece.Scatter((x << 8) + 0x80, (y << 8) + 0x80, random);
                if (kind == 1) piece.Place(piece.X, piece.Y, VisitorActivity.VomitKind);
            }
    }
}
