using System;

namespace TPW.Data
{
    /// <summary>One weighted contribution: a destination vertex and how much of a source reaches it.</summary>
    public readonly struct BindingRecord
    {
        public const int Scale = 16384;             // the game shifts the product right by 14

        public readonly ushort Vertex;
        public readonly ushort Weight;              // 16384 == 1.0

        /// <summary>The vertex's position in the BONE's own space, transformed by that bone's matrix
        /// before being weighted in. Formerly "bytes 4..11, not identified".</summary>
        public readonly short X, Y, Z;

        public BindingRecord(ReadOnlySpan<byte> d)
        {
            Vertex = BitConverter.ToUInt16(d.Slice(0, 2));
            Weight = BitConverter.ToUInt16(d.Slice(2, 2));
            X = BitConverter.ToInt16(d.Slice(4, 2));
            Y = BitConverter.ToInt16(d.Slice(6, 2));
            Z = BitConverter.ToInt16(d.Slice(8, 2));
            // d[10..11] is the 8-byte vertex's pad, zero on every record on the disc.
        }
        public float WeightF => Weight / (float)Scale;
    }

    /// <summary>
    /// How animated source points reach vertices.
    ///
    /// ⭐ THERE IS NO PER-VERTEX BONE INDEX IN THIS FORMAT. A vertex is not owned by a bone; it is the
    /// weighted sum of however many animated sources reach it. Ported from the blend loop at 0x8002e31c:
    ///
    ///     for each source i:  {count, start} = run[i]
    ///         for k in 0..count-1:  {vertex, weight} = record[start + k]
    ///             out[vertex] += source[i] * weight >> 14
    ///
    /// ⚠ So a skin-matrix palette is the WRONG shape to build against this, and would fight the data.
    ///
    /// Evidence this is the binding rather than a plausible-looking table, measured over the disc:
    /// weights grouped by destination vertex sum to exactly 16384 for 15,306 of 15,633 destinations and
    /// to within one unit for all but 14 of the rest -- a partition of unity. The runs also cover the
    /// record table with no gap and no overlap in 30 of 30 meshes that carry both.
    /// </summary>
    public sealed class VertexBinding
    {
        /// <summary>One per animated source: which binding records it feeds, and where the source sits
        /// when no track drives it.
        ///
        /// ⚠ THE DEFAULT IS NOT ZERO AND ASSUMING IT IS SHREDS THE MODEL. Tracks address only the ODD
        /// source slots — every animated mesh on the disc drives exactly half its sources, indices
        /// 1,3,5,...  The even ones are never written by a track; the builder seeds the whole region from
        /// bytes 4..11 of these records (0x8002cab0 copies from record+4), which is an 8-byte PSX vertex
        /// with the pad zero on all 560 records. Seeding zero instead drags every vertex an undriven
        /// source reaches toward the origin, which is visible as parts stretching out of the model.</summary>
        public (int Start, int Count, short X, short Y, short Z)[] Runs =
            Array.Empty<(int, int, short, short, short)>();
        public BindingRecord[] Records = Array.Empty<BindingRecord>();

        public int SourceCount => Runs.Length;

        /// <summary>Runs tile the record table exactly: consecutive, no gap, no overlap, ending on the
        /// last record. True of all 30 meshes on the disc that have both tables.</summary>
        public bool RunsTileTheTable()
        {
            if (Runs.Length == 0) return Records.Length == 0;
            int at = Runs[0].Start;
            foreach (var r in Runs)
            {
                if (r.Start != at) return false;
                at += r.Count;
            }
            return at == Records.Length;
        }

        /// <summary>Weights reaching each destination sum to 1.0, within <paramref name="tol"/> units of
        /// 16384.
        ///
        /// The default is the MEASURED worst case, not a round number: over 20,431 records on the disc
        /// every destination sums to exactly 16384 but for a handful at 16383/16385, and a single vertex
        /// (e0071 sub0, vertex 17) reaching 16387 across about six influences. Three units of drift over
        /// six roundings is arithmetic, not a bad read. Pass tol 0 to demand exactness.</summary>
        public bool WeightsSumToOne(int vertexCount, int tol = 3)
        {
            var acc = new int[vertexCount];
            foreach (var r in Records)
            {
                if (r.Vertex >= vertexCount) return false;
                acc[r.Vertex] += r.Weight;
            }
            foreach (int v in acc)
                if (v != 0 && Math.Abs(v - BindingRecord.Scale) > tol) return false;
            return true;
        }

        /// <summary>Accumulate the sources into vertex positions. Destinations that no source reaches are
        /// left at zero, exactly as the game leaves them -- this ADDS, it does not interpolate.</summary>
        public void Scatter(ReadOnlySpan<(short X, short Y, short Z)> sources,
                            Span<(int X, int Y, int Z)> outVertices)
        {
            outVertices.Clear();
            int n = Math.Min(sources.Length, Runs.Length);
            for (int i = 0; i < n; i++)
            {
                var (start, count, _, _, _) = Runs[i];
                var s = sources[i];
                for (int k = 0; k < count; k++)
                {
                    var r = Records[start + k];
                    if (r.Vertex >= outVertices.Length) continue;
                    ref var o = ref outVertices[r.Vertex];
                    o.X += (s.X * r.Weight) >> 14;
                    o.Y += (s.Y * r.Weight) >> 14;
                    o.Z += (s.Z * r.Weight) >> 14;
                }
            }
        }

        /// <summary>Parse both tables. They sit between the bone records and the animation tracks.</summary>
        public static bool TryParse(ReadOnlySpan<byte> d, int at, int runCount, int recordCount,
                                    out VertexBinding binding, out string error)
        {
            binding = new VertexBinding();
            error = null;
            if (at + (runCount + recordCount) * 12 > d.Length)
            { error = "binding tables past the end"; return false; }

            var runs = new (int, int, short, short, short)[runCount];
            for (int i = 0; i < runCount; i++)
            {
                int o = at + i * 12;
                runs[i] = (BitConverter.ToUInt16(d.Slice(o + 2, 2)), BitConverter.ToUInt16(d.Slice(o, 2)),
                           BitConverter.ToInt16(d.Slice(o + 4, 2)), BitConverter.ToInt16(d.Slice(o + 6, 2)),
                           BitConverter.ToInt16(d.Slice(o + 8, 2)));
            }
            var recs = new BindingRecord[recordCount];
            int rb = at + runCount * 12;
            for (int i = 0; i < recordCount; i++) recs[i] = new BindingRecord(d.Slice(rb + i * 12, 12));

            binding.Runs = runs;
            binding.Records = recs;
            return true;
        }
    }
}
