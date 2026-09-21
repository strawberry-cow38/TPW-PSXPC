using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace TPW.Sim
{
    /// <summary>READ: the fourteen-byte saved control point, 0x800ADB20/0x800ADCD8.
    /// Offsets are hexadecimal. These are not mesh IDs or vehicles.</summary>
    public readonly record struct CoasterPiece(short TileX, ushort UnknownPositionPadding, short TileY,
        ushort CheckWord, short Height, short Bank, byte Kind, byte SpecialPhase)
    {
        public const int Size = 0x0E; // READ: 0x800ADC84..90.
        public const int RecordSize = 0x418; // READ: save.md §3.2, hexadecimal.
        public const int SlotsOffset = 0x96; // READ: 0x800ADBE8.
        public const int ReservedSlots = 64; // READ: 0x380 / 0x0E; NOT a live population.

        public static CoasterPiece Read(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < Size) throw new ArgumentException("Truncated coaster piece.", nameof(bytes));
            return new CoasterPiece(BinaryPrimitives.ReadInt16LittleEndian(bytes),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]),
                BinaryPrimitives.ReadInt16LittleEndian(bytes[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]),
                BinaryPrimitives.ReadInt16LittleEndian(bytes[8..]),
                BinaryPrimitives.ReadInt16LittleEndian(bytes[10..]), bytes[12], bytes[13]);
        }

        /// <summary>Lossless codec: retains the report's opaque +2 halfword (disagreement §0).
        /// The original runtime load booleanizes CheckWord at 0x800ADDD4..E0.</summary>
        public void Write(Span<byte> bytes)
        {
            if (bytes.Length < Size) throw new ArgumentException("Truncated coaster piece.", nameof(bytes));
            BinaryPrimitives.WriteInt16LittleEndian(bytes, TileX);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes[2..], UnknownPositionPadding);
            BinaryPrimitives.WriteInt16LittleEndian(bytes[4..], TileY);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes[6..], CheckWord);
            BinaryPrimitives.WriteInt16LittleEndian(bytes[8..], Height);
            BinaryPrimitives.WriteInt16LittleEndian(bytes[10..], Bank);
            bytes[12] = Kind;
            bytes[13] = SpecialPhase;
        }

        /// <summary>READ: only count populated slots; reserved bytes and 0x416/417 stay opaque.
        /// 0x800ADD14..24 masks the low six bits, not the whole flags byte.</summary>
        public static CoasterPiece[] ReadPopulated(ReadOnlySpan<byte> record)
        {
            if (record.Length < RecordSize) throw new ArgumentException("Truncated coaster record.", nameof(record));
            var pieces = new CoasterPiece[record[0x95] & 0x3F];
            for (int i = 0; i < pieces.Length; i++) pieces[i] = Read(record.Slice(SlotsOffset + i * Size, Size));
            return pieces;
        }
    }

    public readonly record struct CoasterVector(int X, int Y, int Z);
    public readonly record struct CoasterSample(CoasterVector Position, CoasterVector Tangent, int Bank);

    /// <summary>Host-owned terrain/model inputs. READ: BaseHeight is the vertical position used by
    /// 0x800B3304; SupportTopOffset is the model/stack contribution in 0x800B340C, excluding this
    /// piece's Height. SplineOffset is 0x800B3660's correction to that support top (including
    /// -model-height(6)+24 and special-phase lateral offsets). All coordinates are signed world
    /// units, 256/tile. No lengths, train speeds, interpolation or lap decisions are delegated.
    /// The save codec deliberately does not reinterpret UnknownPositionPadding; see §0.</summary>
    public readonly record struct CoasterPieceGeometry(short BaseHeight, int SupportTopOffset, CoasterVector SplineOffset);

    public interface ICoasterTrackWorld
    {
        CoasterPieceGeometry ResolvePieceGeometry(CoasterPiece piece);
    }

    /// <summary>READ: runtime 0x40-byte piece's motion-relevant subset. A segment is the span
    /// Previous → this node. Next/Previous are +0x18/+0x14; chord Length is signed +0x36.</summary>
    public sealed class CoasterTrackNode
    {
        public CoasterPiece Piece { get; }
        public CoasterVector Anchor { get; }
        public CoasterVector SplinePoint { get; }
        public bool CheckPassed { get; set; }
        public CoasterTrackNode Previous { get; internal set; }
        public CoasterTrackNode Next { get; internal set; }
        public short Length { get; internal set; }

        public CoasterTrackNode(CoasterPiece piece, CoasterPieceGeometry geometry)
        {
            Piece = piece;
            CheckPassed = piece.CheckWord != 0; // READ: 0x800ADDD4..E0.
            // READ: X/Z << 8 then +0x80, 0x800AF81C..34, 0x800B3304..34.
            Anchor = new CoasterVector(unchecked((short)((piece.TileX << 8) + 0x80)),
                unchecked((short)(geometry.BaseHeight + piece.Height + geometry.SupportTopOffset)),
                unchecked((short)((piece.TileY << 8) + 0x80)));
            SplinePoint = new CoasterVector(unchecked((short)(Anchor.X + geometry.SplineOffset.X)),
                unchecked((short)(Anchor.Y + geometry.SplineOffset.Y)),
                unchecked((short)(Anchor.Z + geometry.SplineOffset.Z)));
        }

        internal void RefreshLength()
        {
            var p = Previous?.Anchor ?? Anchor; // READ: 0x800B3BDC..3C3C.
            int x = Anchor.X - p.X, y = Anchor.Y - p.Y, z = Anchor.Z - p.Z;
            Length = unchecked((short)CoasterMath.SquareRoot(unchecked(x * x + y * y + z * z)));
        }
    }

    /// <summary>READ: two station nodes plus up to 32 populated route nodes, 0x800B1A98..ACC.
    /// No editor mesh family is used to guess route geometry.</summary>
    public sealed class CoasterTrack
    {
        public const int MaximumPieces = 32; // READ: 0x800AF774, before even the closing-point check.
        readonly List<CoasterTrackNode> pieces = new();
        public IReadOnlyList<CoasterTrackNode> Pieces => pieces.AsReadOnly();
        public CoasterTrackNode Launch { get; }
        public CoasterTrackNode Approach { get; }
        public bool Connected { get; private set; }
        public bool ChecksPass => Launch.CheckPassed && Approach.CheckPassed && pieces.All(p => p.CheckPassed);

        /// <summary>READ: station endpoint placement is supplied from the attraction's own
        /// connection getters (0x800AFB60); Approach.Next = Launch, 0x800AFCAC/0x800AFD78.</summary>
        public CoasterTrack(CoasterTrackNode launch, CoasterTrackNode approach)
        {
            Launch = launch ?? throw new ArgumentNullException(nameof(launch));
            Approach = approach ?? throw new ArgumentNullException(nameof(approach));
            if (ReferenceEquals(launch, approach)) throw new ArgumentException("Two distinct station nodes required.");
            Link(Approach, Launch);
            RefreshLengths();
        }

        public enum Addition { Refused, Placed, Closed }

        public Addition Append(CoasterPiece piece, ICoasterTrackWorld world)
        {
            // ⚠ DO NOT FIX: a full 32-piece array refuses even the closing point, 0x800AF76C..78.
            if (pieces.Count >= MaximumPieces) return Addition.Refused;
            if (pieces.Count != 0 && piece.TileX == Approach.Piece.TileX && piece.TileY == Approach.Piece.TileY)
            {
                // READ: 0x800ADE68..8C compares only map X/Z, not height. Closure adds no slot.
                Connect();
                return Addition.Closed;
            }
            var node = new CoasterTrackNode(piece, world.ResolvePieceGeometry(piece));
            Link(pieces.Count == 0 ? Launch : pieces[^1], node); // READ: 0x800AF920..958.
            pieces.Add(node);
            Connected = false; // READ: a subsequent edit clears +0x104, 0x800AFA04.
            RefreshLengths();
            return Addition.Placed;
        }

        /// <summary>READ: loader restores the connection independently, 0x800ADE00..10.
        /// Connection is not inferred from nonzero lengths or validation flags.</summary>
        public static CoasterTrack Restore(ReadOnlySpan<byte> record, CoasterTrackNode launch,
            CoasterTrackNode approach, ICoasterTrackWorld world)
        {
            var saved = CoasterPiece.ReadPopulated(record);
            // Managed boundary: the original loader would dereference null past its 32-object pool.
            if (saved.Length > MaximumPieces) throw new ArgumentException("Saved track exceeds the runtime piece pool.", nameof(record));
            var track = new CoasterTrack(launch, approach);
            foreach (var piece in saved)
                if (track.Append(piece, world) != Addition.Placed)
                    throw new ArgumentException("A populated slot cannot be a closing sentinel.", nameof(record));
            // Retain save.md's bit-6 interpretation; binary also accepts bit 7 (disagreement §0).
            if ((record[0x95] & 0x40) != 0 && saved.Length != 0) track.Connect();
            return track;
        }

        void Connect()
        {
            Link(pieces[^1], Approach); // READ: 0x800ADEA0..EF4.
            Connected = true;
            RefreshLengths();
        }

        static void Link(CoasterTrackNode previous, CoasterTrackNode next)
        { previous.Next = next; next.Previous = previous; }

        void RefreshLengths()
        {
            Approach.RefreshLength(); Launch.RefreshLength();
            foreach (var node in pieces) node.RefreshLength();
        }

        /// <summary>READ: four linked spline controls, 0x800B2340..2468; missing neighbours
        /// duplicate the endpoint. Station span specifically duplicates both ends.</summary>
        public CoasterSample Sample(CoasterTrackNode node, int fraction)
        {
            var previous = node.Previous ?? node;
            var before = previous.Previous ?? previous;
            var next = node.Next ?? node;
            if (previous == Approach && node == Launch) { before = previous; next = node; }
            var sample = CoasterMath.Spline(before.SplinePoint, previous.SplinePoint, node.SplinePoint,
                next.SplinePoint, fraction);
            int bank = unchecked(previous.Piece.Bank + ((node.Piece.Bank - previous.Piece.Bank) * fraction >> 12));
            return new CoasterSample(sample.Position, sample.Tangent, bank); // READ: 0x800B24C8..4FC.
        }
    }
}
