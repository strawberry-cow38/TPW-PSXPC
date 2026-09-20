using System;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    /// <summary>The shop and sideshow record bodies (rides.md §1.3, re-read for the purchase port).</summary>
    public class ShopRecordTests
    {
        /// <summary>The smallest container AttractionDefinition.Read accepts, with a record of the given
        /// type and body length, and the body bytes from +0x24 on supplied raw so a test can put a
        /// specific byte at a specific offset.</summary>
        static byte[] Record(int type, int bodyLength, params byte[] bodyFrom0x24)
        {
            var d = new byte[0x400];
            void W(int at, int v) => BitConverter.GetBytes(v).CopyTo(d, at);

            const int rec = 0x80;
            W(0x00, 0x96); W(0x04, 1); W(0x14, rec); W(0x18, 0x200); W(0x1C, 4);
            W(rec + 0x00, type);
            W(rec + 0x04, 1234);
            d[rec + 0x08] = 2; d[rec + 0x0A] = 2;
            BitConverter.GetBytes((short)1).CopyTo(d, rec + 0x0C);
            BitConverter.GetBytes((short)0).CopyTo(d, rec + 0x0E);
            BitConverter.GetBytes((short)-1).CopyTo(d, rec + 0x10);
            BitConverter.GetBytes((short)-1).CopyTo(d, rec + 0x12);
            W(rec + 0x1C, bodyLength);
            W(rec + 0x20, 250);
            bodyFrom0x24.CopyTo(d, rec + 0x24);
            return d;
        }

        // ⭐ THE DRINKS SHOP SHAPE. Its bytes from +0x2C are 60 00 28 00 01 00 00 28 05 00 05 00: price 60,
        // unit cost 40, kind 1, then +0x32 = 0 and +0x33 = 40. REJECTS reading +0x32 as a u16, which
        // is rides.md's "10240" and would weight need A by ten thousand.
        [Fact]
        public void TheProductParametersAreBytesNotHalfwords()
        {
            var d = Record(4, 0x18,
                0, 0, 0, 0, 0, 0, 0, 0,        // +0x24..+0x2B
                60, 0, 40, 0,                  // +0x2C price, +0x2E unit cost
                1, 0, 0, 40, 5, 0, 5, 0);      // +0x30 kind, +0x31, +0x32, +0x33, +0x34, +0x35, +0x36, +0x37
            var a = AttractionDefinition.Read(1, d);
            Assert.NotNull(a.Shop);
            var s = a.Shop.Value;
            Assert.Equal(60, s.DefaultPrice);
            Assert.Equal(40, s.UnitCost);
            Assert.Equal(1, s.Kind);
            Assert.Equal(0, s.NeedAValue);
            Assert.Equal(40, s.NeedBValue);
            Assert.Equal(5, s.HappinessValue);
            Assert.Equal(5, s.NauseaValue);
        }

        // The Fries shape (60, 40, kind 7, 20 at +0x32, 0 at +0x33, 5, 10). REJECTS swapping +0x32 and
        // +0x33, which would make fries quench thirst.
        [Fact]
        public void NeedAIsAtPlus32AndNeedBAtPlus33()
        {
            var d = Record(4, 0x18, 0, 0, 0, 0, 0, 0, 0, 0, 60, 0, 40, 0, 7, 0, 20, 0, 5, 0, 10, 0);
            var s = AttractionDefinition.Read(1, d).Shop.Value;
            Assert.Equal((7, 20, 0, 5, 10), (s.Kind, s.NeedAValue, s.NeedBValue, s.HappinessValue, s.NauseaValue));
        }

        // ⚠ THE ODD BYTES ARE NOT READ BY ANY GETTER, so junk there must not leak into a field. REJECTS
        // a u16 read at +0x30, +0x34 or +0x36.
        [Fact]
        public void TheUnreadOddBytesDoNotLeak()
        {
            var d = Record(4, 0x18, 0, 0, 0, 0, 0, 0, 0, 0, 60, 0, 40, 0, 7, 0xFF, 20, 0, 5, 0xFF, 10, 0xFF);
            var s = AttractionDefinition.Read(1, d).Shop.Value;
            Assert.Equal((7, 5, 10), (s.Kind, s.HappinessValue, s.NauseaValue));
        }

        // The Arcade: price 10, chance 30, prize 25 (u16, u16, u8). REJECTS a u16 prize, which on a
        // record whose +0x31 is not zero would read a four-figure prize.
        [Fact]
        public void ASideShowHasPriceChanceAndAOneBytePrize()
        {
            var d = Record(5, 0x14, 0, 0, 0, 0, 0, 0, 0, 0, 10, 0, 30, 0, 25, 3, 0, 0);
            var a = AttractionDefinition.Read(1, d);
            Assert.Null(a.Shop);
            Assert.NotNull(a.SideShow);
            var g = a.SideShow.Value;
            Assert.Equal((10, 30, 25), (g.PlayPrice, g.WinChance, g.Prize));
        }

        // Only the two types get their block. REJECTS reading a shop block off a feature or a ride,
        // whose bytes there are something else entirely.
        [Theory]
        [InlineData(2, 0x10)]
        [InlineData(3, 0xA0)]
        public void OtherTypesGetNeither(int type, int body)
        {
            var d = Record(type, body, 0, 0, 0, 0, 0, 0, 0, 0, 60, 0, 40, 0, 7, 0, 20, 0, 5, 0, 10, 0);
            var a = AttractionDefinition.Read(1, d);
            Assert.Null(a.Shop);
            Assert.Null(a.SideShow);
        }

        // A record cut short of +0x38 gives no shop block rather than reading past the buffer.
        [Fact]
        public void ATruncatedShopRecordGivesNoBlock()
        {
            var d = Record(4, 0x18, 0, 0, 0, 0, 0, 0, 0, 0, 60, 0, 40, 0, 7, 0, 20, 0, 5, 0, 10, 0);
            Array.Resize(ref d, 0x80 + 0x37);
            Assert.Null(AttractionDefinition.Read(1, d).Shop);
        }
    }
}
