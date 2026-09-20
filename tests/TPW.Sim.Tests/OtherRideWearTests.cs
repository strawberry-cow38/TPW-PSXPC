using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class OtherRideWearTests
    {
        // REJECTS sharing the tour/coaster two-term average with track, ignoring piece count,
        // rounding before dividing, fixed-point multiplying the integer multiplier, or widening mflo.
        [Theory]
        [InlineData(AttractionType.TourRide, 2048, 4232, 7, 30, 21980)]
        [InlineData(AttractionType.RollerCoaster, 2048, 4232, 5, 30, 15700)]
        [InlineData(AttractionType.TrackRide, 2048, 4232, 5, 30, 17290)]
        [InlineData(AttractionType.TrackRide, 2048, 4232, 5, 15, 13880)]
        [InlineData(AttractionType.TrackRide, 0, 0, 3, 1, 135)]
        [InlineData(AttractionType.TourRide, -3, 0, 7, 0, -7)]
        [InlineData(AttractionType.TrackRide, -4, 0, 7, 0, -7)]
        [InlineData(AttractionType.TourRide, 1073741824, 1073741824, 3, 0, 1073741824)]
        public void TrackAddsRouteSizeAsAThirdWearTerm(AttractionType type, int speed, int load,
                                                     int multiplier, byte pieces, int expected)
        {
            Assert.Equal(expected, OtherRideWear.CombineRate(type, speed, load, multiplier, pieces));
        }

        sealed class World : IOtherRideWearWorld
        {
            public uint NowTick { get; set; }
            public int ReliabilityRaw { get; set; }
            public int Steps, Smokes;
            public void ApplySharedWearStep() => Steps++;
            public void EnsureSmoke() => Smokes++;
        }

        // REJECTS applying only the tour's outer mask (which admits odd ticks), borrowing flat cadence,
        // or silently applying the newly found outside-running callers contrary to the report-retention instruction.
        [Theory]
        [InlineData(AttractionType.TourRide, 3)]
        [InlineData(AttractionType.TrackRide, 17)]
        [InlineData(AttractionType.RollerCoaster, 17)]
        public void WearCadencesCombineBothMasksAndKeepTheReportsStatusGate(AttractionType type, int steps)
        {
            var w = new World();
            for (uint tick = 0; tick <= 64; tick++)
            {
                w.NowTick = tick;
                for (int status = 0; status <= 11; status++)
                {
                    int before = w.Steps;
                    OtherRideWear.Tick(type, (AttractionStatus)status, w);
                    if (status != 2) Assert.Equal(before, w.Steps);
                }
            }
            Assert.Equal(steps, w.Steps);
        }

        // REJECTS a whole-integer reliability threshold, <=10, losing smoke, or silently replacing
        // the report's direct-to-5 track/coaster behavior with the binary's newly discovered 4.
        [Theory]
        [InlineData(AttractionType.TourRide, AttractionStatus.AboutToBreakDown)]
        [InlineData(AttractionType.TrackRide, AttractionStatus.BrokenDown)]
        [InlineData(AttractionType.RollerCoaster, AttractionStatus.BrokenDown)]
        public void BreakdownsRetainTheOldReportExplicitly(AttractionType type, AttractionStatus expected)
        {
            var w = new World { ReliabilityRaw = 0xA000 };
            Assert.Equal(AttractionStatus.Running, OtherRideWear.CheckBreakdown(type, AttractionStatus.Running, w));
            Assert.Equal(0, w.Smokes);
            w.ReliabilityRaw = 0x9FFF;
            Assert.Equal(expected, OtherRideWear.CheckBreakdown(type, AttractionStatus.Running, w)); Assert.Equal(1, w.Smokes);
            Assert.Equal(AttractionStatus.Loading, OtherRideWear.CheckBreakdown(type, AttractionStatus.Loading, w));
            Assert.Equal(1, w.Smokes);
        }
    }
}
