using System;
using System.Buffers.Binary;

namespace TPW.Data
{
    /// <summary>READ: definition inputs to the two station nodes, 0x800AFB60..FDC0.</summary>
    public sealed record CoasterDefinition(short LaunchX, short LaunchZ, short ApproachX, short ApproachZ,
        short LaunchHeight, short ApproachHeight, short LaunchSpeed, int LaunchFacing, int ApproachFacing)
    {
        public static CoasterDefinition Read(ReadOnlySpan<byte> record) => new(
            BinaryPrimitives.ReadInt16LittleEndian(record[0xBC..]),
            BinaryPrimitives.ReadInt16LittleEndian(record[0xBE..]),
            BinaryPrimitives.ReadInt16LittleEndian(record[0xC0..]),
            BinaryPrimitives.ReadInt16LittleEndian(record[0xC2..]),
            BinaryPrimitives.ReadInt16LittleEndian(record[0xC4..]),
            BinaryPrimitives.ReadInt16LittleEndian(record[0xC6..]),
            BinaryPrimitives.ReadInt16LittleEndian(record[0xC8..]),
            (int)(BinaryPrimitives.ReadUInt32LittleEndian(record[0xD0..]) >> 28) & 3,
            (int)(BinaryPrimitives.ReadUInt32LittleEndian(record[0xD0..]) >> 30));

        // READ: rotate local coordinate, step outside along facing+rotation, add placement:
        // 0x800ACDA8..CF90 (launch), 0x800AD28C..D474 (approach).
        public (int X, int Z) Connection(AttractionDefinition ride, int ox, int oz, int rot, bool launch)
        {
            var p = ride.Rotate(launch ? LaunchX : ApproachX, launch ? LaunchZ : ApproachZ, rot);
            var direction = ((launch ? LaunchFacing : ApproachFacing) + rot) & 3;
            return direction switch
            {
                0 => (ox + p.X, oz + p.Z - 1),
                1 => (ox + p.X - 1, oz + p.Z),
                2 => (ox + p.X, oz + p.Z + 1),
                _ => (ox + p.X + 1, oz + p.Z),
            };
        }
    }
}
