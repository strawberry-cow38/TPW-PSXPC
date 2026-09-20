using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>A feature's capacity byte, its stamp, and the two park-wide readings of it
    /// (findings/shop-stock.md). The visitor's use of it is covered in VisitorQueueTests; the handyman's
    /// choice and reward in HandymanTests.</summary>
    public class FeatureStockTests
    {
        // ⭐ A FEATURE IS PLACED FULL WITH A ZERO STAMP (0x80023C70..0x80023C94). REJECTS a port that
        // starts a toilet empty or half-full, and one that stamps the build day the way a shop's init
        // does (0x800B6980) -- a feature's "Last Cleaned" is 0 until a cleaner has been.
        [Fact]
        public void PlacementIsFullWithAZeroStamp()
        {
            var s = new FeatureStock();
            Assert.Equal(100, s.Level);
            Assert.Equal(100, FeatureStock.Full);
            Assert.Equal(0, s.LastServicedDay);
        }

        // ⚠ THE SETTER CLAMPS A SIGNED BYTE, NOT AN INT (0x800242E8). 101..127 clamp to 100; 128..255
        // read as negative and clamp to ZERO; 256 is the byte 0. REJECTS an int clamp (which would put
        // 200 at 100), a raw byte store (255 stays 255), and an unsigned clamp (128 at 100).
        [Theory]
        [InlineData(0, 0)]
        [InlineData(50, 50)]
        [InlineData(100, 100)]
        [InlineData(101, 100)]
        [InlineData(127, 100)]
        [InlineData(128, 0)]
        [InlineData(200, 0)]
        [InlineData(255, 0)]
        [InlineData(256, 0)]
        [InlineData(-1, 0)]
        public void SetClampsAsASignedByte(int value, int expected)
        {
            var s = new FeatureStock();
            s.Set(value);
            Assert.Equal(expected, s.Level);
        }

        // Subtract takes the units off and floors at zero (0x800242B0: the negative branch stores 0).
        // REJECTS wrapping below zero, subtracting from 100 instead of the level, and a port that
        // refuses the subtraction when it would go negative (the level must reach exactly 0).
        [Theory]
        [InlineData(100, 26, 74)]
        [InlineData(100, 0, 100)]
        [InlineData(30, 26, 4)]
        [InlineData(26, 26, 0)]
        [InlineData(20, 26, 0)]
        [InlineData(0, 5, 0)]
        public void SubtractTakesUnitsOffAndFloorsAtZero(int level, int units, int expected)
        {
            var s = new FeatureStock();
            s.Set(level);
            s.Subtract(units);
            Assert.Equal(expected, s.Level);
        }

        // ⚠ DO NOT FIX: the units go through a stack BYTE read back signed, and the difference is stored
        // with no upper clamp (0x800241F0, 0x800242B0..0x800242D0). 255 is −1, so a full feature goes to
        // 101; 128 is −128, so 100 − (−128) = 228 is stored as the byte 0xE4 and reads back as −28.
        // REJECTS treating the units as an int (both would floor at 0) and clamping the result at 100.
        [Theory]
        [InlineData(100, 255, 101)]
        [InlineData(100, 128, -28)]
        [InlineData(50, 200, 106)]
        public void SubtractReadsTheUnitsAsASignedByteAndStoresTheLowByte(int level, int units, int expected)
        {
            var s = new FeatureStock();
            s.Set(level);
            s.Subtract(units);
            Assert.Equal(expected, s.Level);
        }

        // ⭐ A REFILL IS INSTANT AND TOTAL (0x80024210): the byte goes straight to 100 whatever it was,
        // and the stamp becomes the day passed in. REJECTS a gradual refill, one that adds a fixed
        // amount, one that leaves the stamp alone, and one that stamps something other than the day.
        [Theory]
        [InlineData(0, 123)]
        [InlineData(37, 0)]
        [InlineData(100, 999)]
        public void RefillSetsFullAndStampsTheDay(int level, int day)
        {
            var s = new FeatureStock();
            s.Set(level);
            s.Refill(day);
            Assert.Equal(100, s.Level);
            Assert.Equal(day, s.LastServicedDay);
        }

        // The loader (0x80023DB8) clamps the saved byte through the setter but copies the stamp word
        // raw. REJECTS restoring the byte unclamped (200 would stay 200) and clamping the stamp.
        [Fact]
        public void FromSaveClampsTheByteButNotTheStamp()
        {
            var s = FeatureStock.FromSave(200, 12345);
            Assert.Equal(0, s.Level);
            Assert.Equal(12345, s.LastServicedDay);
            var t = FeatureStock.FromSave(60, -7);
            Assert.Equal(60, t.Level);
            Assert.Equal(-7, t.LastServicedDay);
        }

        // ⭐ PARK DIRTINESS IS 100 MINUS THE MEAN OVER USABLE FEATURES ONLY (0x800153B4): a bench with
        // slot 33 clear is in neither the sum nor the count. REJECTS averaging over every feature (the
        // bench's 0 would drag the mean to 50), counting it without summing it, and returning the
        // mean itself rather than its complement.
        [Fact]
        public void ParkDirtinessIsHundredMinusTheMeanOverUsableFeaturesOnly()
        {
            var features = new List<(bool, int)> { (true, 100), (true, 50), (false, 0) };
            Assert.Equal(25, FeatureStock.ParkDirtiness(features));
        }

        // ⚠ NO USABLE FEATURE READS AS ZERO, NOT 100: the register is preloaded with 100 and then
        // overwritten (0x80015440..0x8001544C). REJECTS the "nothing to be clean = filthy" reading,
        // and a divide by zero.
        [Fact]
        public void ParkDirtinessIsZeroWithNoUsableFeature()
        {
            Assert.Equal(0, FeatureStock.ParkDirtiness(new List<(bool, int)>()));
            Assert.Equal(0, FeatureStock.ParkDirtiness(new List<(bool, int)> { (false, 10), (false, 90) }));
        }

        // The mean is a truncating signed divide (0x80015450). 299 / 3 = 99, so the answer is 1, not 0.
        // REJECTS rounding the mean to nearest (which would say 100 and answer 0) and dividing the
        // complement instead of the sum.
        [Fact]
        public void ParkDirtinessTruncatesTheMean()
        {
            var features = new List<(bool, int)> { (true, 100), (true, 100), (true, 99) };
            Assert.Equal(1, FeatureStock.ParkDirtiness(features));
        }

        // ⚠ THE RESULT IS MASKED TO 16 BITS (`andi 0xFFFF`, 0x80015484), which only shows when the
        // complement is negative -- a feature whose byte sits above 100, which Subtract's signed-byte
        // slip can produce. 100 − 101 = −1 comes out as 65535. REJECTS returning the signed −1.
        [Fact]
        public void ParkDirtinessMasksToSixteenBits()
        {
            Assert.Equal(65535, FeatureStock.ParkDirtiness(new List<(bool, int)> { (true, 101) }));
        }

        // ⚠ THE RUNNING SUM IS A 16-BIT REGISTER (0x80015424). 400 full features sum to 40,000, which
        // wraps to −25,536; the mean is −63 and the complement 163. Unreachable with a 45-feature pool
        // and pinned so the port holds the console's register width. REJECTS a 32-bit accumulator
        // (which would give 0).
        [Fact]
        public void ParkDirtinessSumsInSixteenBits()
        {
            var features = new List<(bool, int)>();
            for (int i = 0; i < 400; i++) features.Add((true, 100));
            Assert.Equal(163, FeatureStock.ParkDirtiness(features));
        }

        // The panel average (0x80078010) is the truncated mean, and 0 for an empty list. REJECTS
        // rounding (200 / 3 → 66, not 67), a divide by zero on an empty panel, skipping the first
        // entry, and returning the complement the way the park statistic does.
        [Fact]
        public void PanelAverageIsTheTruncatedMeanAndZeroForAnEmptyList()
        {
            Assert.Equal(0, FeatureStock.PanelAverage(new int[0]));
            Assert.Equal(66, FeatureStock.PanelAverage(new[] { 100, 50, 50 }));
            Assert.Equal(100, FeatureStock.PanelAverage(new[] { 100 }));
        }
    }
}
