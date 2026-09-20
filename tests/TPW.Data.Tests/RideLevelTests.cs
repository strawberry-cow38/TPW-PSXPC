using System;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    /// <summary>The per-level block of a ride's definition record (rides.md §1.3).</summary>
    public class RideLevelTests
    {
        /// <summary>The smallest thing AttractionDefinition.Read will accept: a 0x96 container whose
        /// header +0x14 points at a record, with three level blocks after it. Built by hand because the
        /// values the parse must pick out are at fixed offsets, and a fixture taken from the disc would
        /// only prove the parse agrees with itself.</summary>
        static byte[] Record(params (int wear, int seats, int life, int spMin, int spMax,
                                     int cyMin, int cyMax, int price)[] levels)
        {
            var d = new byte[0x400];
            void W(int at, int v) => BitConverter.GetBytes(v).CopyTo(d, at);

            const int rec = 0x80;
            // container: 0x96, 1, 0, 0, 0, recOff, size, 4
            W(0x00, 0x96); W(0x04, 1); W(0x14, rec); W(0x18, 0x200); W(0x1C, 4);

            W(rec + 0x00, 3);          // type 3: a flat ride
            W(rec + 0x04, 1234);       // name id
            d[rec + 0x08] = 4;         // width
            d[rec + 0x0A] = 4;         // depth
            BitConverter.GetBytes((short)1).CopyTo(d, rec + 0x0C);   // entrance x
            BitConverter.GetBytes((short)0).CopyTo(d, rec + 0x0E);
            BitConverter.GetBytes((short)-1).CopyTo(d, rec + 0x10);  // no exit
            BitConverter.GetBytes((short)-1).CopyTo(d, rec + 0x12);
            W(rec + 0x18, 60);         // base intensity
            W(rec + 0x1C, 0xA0);       // body length

            for (int L = 0; L < levels.Length; L++)
            {
                int b = rec + 0x24 + 0x34 * L;
                var v = levels[L];
                W(b + 0x00, 4); W(b + 0x04, 4);
                W(b + 0x08, v.wear); W(b + 0x0C, v.seats); W(b + 0x10, v.life);
                W(b + 0x14, v.spMin); W(b + 0x18, v.spMax);
                W(b + 0x1C, v.cyMin); W(b + 0x20, v.cyMax);
                W(b + 0x2C, v.price);
            }
            return d;
        }

        // ⭐ THE LEVEL BLOCK IS WHERE A RIDE'S REAL NUMBERS LIVE. Nothing could read it before, so wear,
        // seats, lifetime and the slider ranges all had to be assumed. REJECTS a stride other than
        // 0x34, which reads plausible numbers out of the neighbouring level.
        [Fact]
        public void AllThreeLevelsAreReadAtTheRightStride()
        {
            var d = Record((5, 8, 45, 1, 100, 1, 10, 2000),
                           (3, 11, 45, 1, 100, 1, 10, 200),
                           (2, 14, 45, 1, 100, 1, 10, 200));
            var a = AttractionDefinition.Read(0xDC, d);

            Assert.Equal(3, a.Levels.Length);
            Assert.Equal(new[] { 5, 3, 2 }, new[] { a.Levels[0].WearMultiplier, a.Levels[1].WearMultiplier, a.Levels[2].WearMultiplier });
            Assert.Equal(new[] { 8, 11, 14 }, new[] { a.Levels[0].MaxSeats, a.Levels[1].MaxSeats, a.Levels[2].MaxSeats });
            Assert.Equal(2000, a.Levels[0].Price);
            Assert.Equal(200, a.Levels[1].Price);
        }

        // The build price is level 0's, which the parse already relied on before the block was read.
        [Fact]
        public void TheBuildPriceIsLevelZerosAndTheOthersAreUpgrades()
        {
            var a = AttractionDefinition.Read(0xDC, Record((5, 8, 45, 1, 100, 1, 10, 2000),
                                                           (3, 11, 45, 1, 100, 1, 10, 200)));
            Assert.Equal(2000, a.Price);
            Assert.Equal(a.Levels[0].Price, a.Price);
            Assert.Equal(200, a.Levels[1].Price);
        }

        // ⚠ ONLY RIDES HAVE THE BLOCK. A shop's record is 0x18 long and +0x24 is past the end of it, so
        // reading a level block there is reading the ground pad. REJECTS parsing it for every type.
        [Fact]
        public void OnlyRidesGetLevels()
        {
            var d = Record((5, 8, 45, 1, 100, 1, 10, 2000));
            BitConverter.GetBytes(4).CopyTo(d, 0x80);        // type 4: a shop
            var shop = AttractionDefinition.Read(1, d);
            Assert.False(shop.IsRide);
            Assert.Empty(shop.Levels);
        }

        // ⚠ THREE LEVELS, NOT FOUR. The code allows a fourth (0x8009C56C increments while < 3), which
        // reads 0x34 bytes past the record into the model chunk - for a Crazy Ape a £65,819 upgrade
        // with 65,818 seats. REJECTS parsing a fourth, which would be reproducing a buffer overrun.
        [Fact]
        public void ExactlyThreeLevelsAreParsedEvenThoughTheCodeWouldReadAFourth()
        {
            var a = AttractionDefinition.Read(0xDC, Record((5, 8, 45, 1, 100, 1, 10, 2000),
                                                           (3, 11, 45, 1, 100, 1, 10, 200),
                                                           (2, 14, 45, 1, 100, 1, 10, 200)));
            Assert.Equal(3, a.Levels.Length);
        }

        // The duration slider starts at half its maximum: 5 for most rides, 25 for a bouncer.
        [Theory]
        [InlineData(1, 10, 5)]
        [InlineData(10, 50, 25)]
        [InlineData(1, 1, 1)]
        public void TheDurationSliderStartsAtHalfItsMaximum(int min, int max, int expected)
        {
            var a = AttractionDefinition.Read(1, Record((5, 8, 45, 1, 100, min, max, 2000)));
            Assert.Equal(expected, a.Levels[0].DefaultCycles);
        }

        // A truncated record gives the levels it actually has rather than reading off the end.
        [Fact]
        public void ATruncatedRecordStopsRatherThanReadingPastIt()
        {
            var d = Record((5, 8, 45, 1, 100, 1, 10, 2000), (3, 11, 45, 1, 100, 1, 10, 200));
            Array.Resize(ref d, 0x80 + 0x24 + 0x34 + 0x30);   // room for level 0 and 1 only
            var a = AttractionDefinition.Read(1, d);
            Assert.Equal(2, a.Levels.Length);
        }
    }
}
