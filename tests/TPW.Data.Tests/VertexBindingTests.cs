using System;
using System.Collections.Generic;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    /// <summary>Weighted scatter from animated sources into vertices. Values worked by hand.</summary>
    public class VertexBindingTests
    {
        static void U16(List<byte> b, int v) { b.Add((byte)v); b.Add((byte)(v >> 8)); }

        // ⚠ count is at +0 and start at +2 -- the opposite order to how the pair reads aloud.
        static void Run(List<byte> b, int start, int count)
        { U16(b, count); U16(b, start); b.AddRange(new byte[8]); }

        static void Rec(List<byte> b, int vertex, int weight)
        { U16(b, vertex); U16(b, weight); b.AddRange(new byte[8]); }

        static VertexBinding Parse(List<byte> runs, List<byte> recs, int nRuns, int nRecs)
        {
            var all = new List<byte>(runs); all.AddRange(recs);
            Assert.True(VertexBinding.TryParse(all.ToArray(), 0, nRuns, nRecs, out var b, out string e), e);
            return b;
        }

        [Fact]
        public void ARunNamesAStretchOfBindingRecords()
        {
            var runs = new List<byte>(); Run(runs, 0, 2); Run(runs, 2, 1);
            var recs = new List<byte>(); Rec(recs, 5, 16384); Rec(recs, 6, 8192); Rec(recs, 7, 4096);

            var b = Parse(runs, recs, 2, 3);
            Assert.Equal(2, b.SourceCount);
            Assert.Equal((0, 2), b.Runs[0]);
            Assert.Equal((2, 1), b.Runs[1]);
            Assert.Equal(5, b.Records[0].Vertex);
            Assert.Equal(16384, b.Records[0].Weight);
            Assert.Equal(1f, b.Records[0].WeightF, 4);
            Assert.Equal(0.5f, b.Records[1].WeightF, 4);
            Assert.True(b.RunsTileTheTable());
        }

        // REJECTS a run layout that leaves records unreachable or reads one twice. Both shapes parse
        // perfectly well and only this check tells them from the real thing.
        [Theory]
        [InlineData(0, 1, 1, 2, true)]    // 0..0 then 1..2 -- tiles exactly
        [InlineData(0, 1, 2, 1, false)]   // gap: record 1 belongs to nobody
        [InlineData(0, 2, 1, 2, false)]   // overlap: record 1 is claimed twice
        [InlineData(0, 1, 1, 1, false)]   // short: the last record is never reached
        public void RunsMustTileTheRecordTable(int s0, int c0, int s1, int c1, bool ok)
        {
            var runs = new List<byte>(); Run(runs, s0, c0); Run(runs, s1, c1);
            var recs = new List<byte>(); for (int i = 0; i < 3; i++) Rec(recs, i, 16384);
            Assert.Equal(ok, Parse(runs, recs, 2, 3).RunsTileTheTable());
        }

        // ⭐ THE PROPERTY THAT IDENTIFIED THIS TABLE AS THE BINDING. Two half weights onto one vertex is
        // a partition of unity; 16384 plus 8192 is not.
        [Fact]
        public void WeightsReachingAVertexMustSumToOne()
        {
            var runs = new List<byte>(); Run(runs, 0, 2);
            var good = new List<byte>(); Rec(good, 3, 8192); Rec(good, 3, 8192);
            Assert.True(Parse(runs, good, 1, 2).WeightsSumToOne(8));

            var bad = new List<byte>(); Rec(bad, 3, 16384); Rec(bad, 3, 8192);
            Assert.False(Parse(runs, bad, 1, 2).WeightsSumToOne(8));
        }

        // ⭐ TWO SOURCES ON ONE VERTEX, because a single full-weight source would land on the right answer
        // whatever the weight arithmetic did. Vertex 0 is pulled half way between (100,0,0) and (0,200,0).
        // REJECTS dropping the weight, REJECTS overwriting instead of accumulating, and REJECTS a wrong shift.
        [Fact]
        public void AVertexIsTheWeightedSumOfEverySourceThatReachesIt()
        {
            var runs = new List<byte>(); Run(runs, 0, 2); Run(runs, 2, 1);
            var recs = new List<byte>();
            Rec(recs, 0, 8192);        // source 0 -> vertex 0 at one half
            Rec(recs, 1, 16384);       // source 0 -> vertex 1 in full
            Rec(recs, 0, 8192);        // source 1 -> vertex 0 at one half
            var b = Parse(runs, recs, 2, 3);

            // ⚠ BOTH sources push on the SAME AXIS of vertex 0. An earlier version of this test had them
            // on different axes, and per-axis `=` instead of `+=` survived it -- the sum was right because
            // the other source contributed nothing to that axis. Overlap on one axis is what catches it.
            var sources = new (short, short, short)[] { (100, 0, 0), (60, 200, 0) };
            var outv = new (int X, int Y, int Z)[3];
            b.Scatter(sources, outv);

            Assert.Equal(80, outv[0].X);      // 50 from source 0 AND 30 from source 1, accumulated
            Assert.Equal(100, outv[0].Y);     // half of 200, from source 1 only
            Assert.Equal(100, outv[1].X);     // full weight
            Assert.Equal(0, outv[1].Y);
            Assert.Equal(0, outv[2].X);       // nothing reaches vertex 2
        }

        // Scatter clears first, so replaying a frame does not pile onto the previous one.
        [Fact]
        public void ScatterDoesNotAccumulateAcrossCalls()
        {
            var runs = new List<byte>(); Run(runs, 0, 1);
            var recs = new List<byte>(); Rec(recs, 0, 16384);
            var b = Parse(runs, recs, 1, 1);
            var sources = new (short, short, short)[] { (70, 0, 0) };
            var outv = new (int X, int Y, int Z)[1];
            b.Scatter(sources, outv);
            b.Scatter(sources, outv);
            Assert.Equal(70, outv[0].X);
        }

        [Fact]
        public void ATableRunningPastTheEndIsRefused()
        {
            Assert.False(VertexBinding.TryParse(new byte[12], 0, 1, 5, out _, out string e));
            Assert.Contains("past the end", e);
        }
    }
}
