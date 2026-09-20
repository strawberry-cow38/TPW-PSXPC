using System;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    /// <summary>The four one-bit properties packed into a FEATURE record's byte +0x2E
    /// (behaviour.md §0 item 8). Each has its own three-instruction accessor in the binary and they are
    /// read by completely different systems, so they must not collapse into one another.</summary>
    public class FeatureFlagTests
    {
        static byte[] Feature(int type, byte flags)
        {
            var d = new byte[0x400];
            void W(int at, int v) => BitConverter.GetBytes(v).CopyTo(d, at);
            const int rec = 0x80;
            W(0x00, 0x96); W(0x04, 1); W(0x14, rec); W(0x18, 0x200); W(0x1C, 4);
            W(rec + 0x00, type);
            W(rec + 0x04, 7);
            d[rec + 0x08] = 2; d[rec + 0x0A] = 2;
            BitConverter.GetBytes((short)-1).CopyTo(d, rec + 0x0C);
            BitConverter.GetBytes((short)-1).CopyTo(d, rec + 0x0E);
            BitConverter.GetBytes((short)-1).CopyTo(d, rec + 0x10);
            BitConverter.GetBytes((short)-1).CopyTo(d, rec + 0x12);
            d[rec + 0x2E] = flags;
            return d;
        }

        // REJECTS reading either flag as the whole byte, and REJECTS the two sharing a bit: bit 0 is
        // 0x80024348 ("guests may use it", the ride score's slot 54) and bit 1 is 0x8002433C (the flag
        // staff state 49 searches for). A litter bin carries 0x04 and must answer NO to both.
        [Theory]
        [InlineData(0x00, false, false)]
        [InlineData(0x01, true, false)]     // a usable feature: a toilet
        [InlineData(0x02, false, true)]     // a staff room
        [InlineData(0x03, true, true)]      // both, as the space world's crystals carry
        [InlineData(0x04, false, false)]    // a litter bin: neither
        [InlineData(0x08, false, false)]    // a security camera: neither
        [InlineData(0xFF, true, true)]
        public void TheTwoNamedBitsAreReadSeparately(int flags, bool usable, bool rest)
        {
            var a = AttractionDefinition.Read(1, Feature(2, (byte)flags));
            Assert.Equal(flags, a.FeatureFlags);
            Assert.Equal(usable, a.UsableByGuests);
            Assert.Equal(rest, a.StaffMayRest);
        }

        // REJECTS parsing the byte for anything but a FEATURE. The same offset on a ride's record is
        // inside its level blocks and on a shop's it is the unit-cost word (Attraction.cs), so reading
        // it by type is not tidiness - it is the difference between a flag and half a price.
        [Theory]
        [InlineData(1)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
        public void OnlyAFeatureHasFlags(int type)
        {
            var a = AttractionDefinition.Read(1, Feature(type, 0x03));
            Assert.Equal(0, a.FeatureFlags);
            Assert.False(a.UsableByGuests);
            Assert.False(a.StaffMayRest);
        }
    }
}
