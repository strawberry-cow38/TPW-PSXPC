using System;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    public class VagTests
    {
        static byte[] Block(int shift, int filter, int flags, params int[] nibbles)
        {
            var b = new byte[Vag.BlockBytes];
            b[0] = (byte)((filter << 4) | (shift & 0x0F));
            b[1] = (byte)flags;
            for (int i = 0; i < nibbles.Length && i < 28; i++)
            {
                int by = 2 + i / 2;
                if (i % 2 == 0) b[by] |= (byte)(nibbles[i] & 0x0F);
                else b[by] |= (byte)((nibbles[i] & 0x0F) << 4);
            }
            return b;
        }

        [Fact]
        public void OneBlockGivesTwentyEightSamples()
            => Assert.Equal(28, Vag.Decode(Block(12, 0, 0)).SampleCount);

        // ⭐ REJECTS SIGN-EXTENSION BEING DROPPED. Nibble 8..15 is NEGATIVE. Treat the nibble as unsigned and
        // every waveform gains a large DC offset and clips -- which still sounds like audio, just wrong.
        [Fact]
        public void HighNibblesAreNegative()
        {
            var pos = Vag.Decode(Block(12, 0, 0, 1));
            var neg = Vag.Decode(Block(12, 0, 0, 0x0F));   // -1
            Assert.True(pos.Samples[0] > 0, $"expected positive, got {pos.Samples[0]}");
            Assert.True(neg.Samples[0] < 0, $"expected negative, got {neg.Samples[0]}");
        }

        [Fact]
        public void ShiftScalesTheSample()
        {
            // shift 12 -> <<0, shift 11 -> <<1
            Assert.Equal(1, Vag.Decode(Block(12, 0, 0, 1)).Samples[0]);
            Assert.Equal(2, Vag.Decode(Block(11, 0, 0, 1)).Samples[0]);
        }

        // ⭐ REJECTS DECODING PAST THE TERMINATOR. Flag 7 ends the stream; running on reads whatever sits
        // next in the bank and welds a burst of noise onto the end of a correct sound.
        [Fact]
        public void EndOfStreamFlagStopsDecoding()
        {
            var two = new byte[Vag.BlockBytes * 2];
            Block(12, 0, 0, 1).CopyTo(two, 0);
            Block(12, 0, 7, 1).CopyTo(two, Vag.BlockBytes);
            Assert.Equal(28, Vag.Decode(two).SampleCount);   // not 56
        }

        [Fact]
        public void EndMarkerStopsAfterTheBlockThatCarriesIt()
        {
            var two = new byte[Vag.BlockBytes * 2];
            Block(12, 0, 1, 1).CopyTo(two, 0);
            Block(12, 0, 0, 1).CopyTo(two, Vag.BlockBytes);
            Assert.Equal(28, Vag.Decode(two).SampleCount);
        }

        [Fact]
        public void LoopStartIsRecorded()
        {
            var two = new byte[Vag.BlockBytes * 2];
            Block(12, 0, 0, 1).CopyTo(two, 0);
            Block(12, 0, 4, 1).CopyTo(two, Vag.BlockBytes);
            Assert.Equal(28, Vag.Decode(two).LoopStart);
        }

        [Fact]
        public void NoLoopFlagLeavesLoopStartUnset()
            => Assert.Equal(-1, Vag.Decode(Block(12, 0, 0, 1)).LoopStart);

        // Real banks contain filter indices above the five defined ones; hardware treats them as filter 0.
        // Faulting instead would drop legitimate sounds.
        [Fact]
        public void OutOfRangeFilterDoesNotThrow()
            => Assert.Equal(28, Vag.Decode(Block(12, 9, 0, 1)).SampleCount);

        [Fact]
        public void EmptyAndNullInputsAreSafe()
        {
            Assert.Equal(0, Vag.Decode(null).SampleCount);
            Assert.Equal(0, Vag.Decode(Array.Empty<byte>()).SampleCount);
        }

        [Fact]
        public void OutputStaysInSixteenBitRange()
        {
            // drive it hard: biggest shift, most negative nibble, repeatedly
            var nib = new int[28];
            for (int i = 0; i < nib.Length; i++) nib[i] = 0x08;
            var pcm = Vag.Decode(Block(0, 4, 0, nib));
            foreach (short s in pcm.Samples)
                Assert.InRange(s, short.MinValue, short.MaxValue);
        }
    }

    public class VabHeaderTests
    {
        static byte[] Header(int waveCount, params int[] sizesInBytes)
        {
            var d = new byte[VabHeader.SplitHeaderSize];
            d[0] = (byte)'p'; d[1] = (byte)'B'; d[2] = (byte)'A'; d[3] = (byte)'V';
            BitConverter.GetBytes((ushort)waveCount).CopyTo(d, 22);
            int off = VabHeader.HeaderBytes + VabHeader.ProgramTableBytes + VabHeader.ToneTableBytes;
            for (int i = 0; i < sizesInBytes.Length; i++)
                BitConverter.GetBytes((ushort)(sizesInBytes[i] / 8)).CopyTo(d, off + (i + 1) * 2);
            return d;
        }

        [Fact]
        public void TheSplitHeaderIsThirtyOneOhFourBytes()
            => Assert.Equal(3104, VabHeader.SplitHeaderSize);

        [Fact]
        public void SignatureIsRequired()
        {
            var d = new byte[VabHeader.SplitHeaderSize];
            Assert.False(VabHeader.TryParse(d, out _, out string err));
            Assert.Contains("pBAV", err);
        }

        // ⭐ REJECTS READING THE SIZE TABLE AS BYTES. It is in 8-byte units. Read as bytes, every sound is an
        // eighth of its length: still audible, just truncated, which is a horrible thing to track down.
        [Fact]
        public void SizeTableIsInEightByteUnits()
        {
            Assert.True(VabHeader.TryParse(Header(2, 64, 1264), out var v, out var err), err);
            Assert.Equal(new[] { 64, 1264 }, v.WaveSizes);
            Assert.Equal(1328, v.BodyBytes);
        }

        [Fact]
        public void BodySlicesAtTheDeclaredLengths()
        {
            Assert.True(VabHeader.TryParse(Header(2, 16, 32), out var v, out _));
            var slices = v.SliceBody(new byte[48]);
            Assert.Equal(2, slices.Count);
            Assert.Equal(16, slices[0].Length);
            Assert.Equal(32, slices[1].Length);
        }

        [Fact]
        public void AShortBodyDoesNotThrow()
        {
            Assert.True(VabHeader.TryParse(Header(2, 16, 32), out var v, out _));
            var slices = v.SliceBody(new byte[8]);
            Assert.Equal(2, slices.Count);
        }

        [Fact]
        public void ImplausibleWaveCountIsRefused()
        {
            var d = Header(0);
            Assert.False(VabHeader.TryParse(d, out _, out string err));
            Assert.Contains("implausible", err);
        }

        [Fact]
        public void TooShortIsRefusedWithTheExpectedLength()
        {
            Assert.False(VabHeader.TryParse(new byte[100], out _, out string err));
            Assert.Contains("3104", err);
        }
    }

    public class XmModuleTests
    {
        static byte[] Xm(int channels, int patterns, int instruments)
        {
            var d = new byte[80];
            for (int i = 0; i < XmModule.Signature.Length; i++) d[i] = (byte)XmModule.Signature[i];
            BitConverter.GetBytes((ushort)channels).CopyTo(d, 68);
            BitConverter.GetBytes((ushort)patterns).CopyTo(d, 70);
            BitConverter.GetBytes((ushort)instruments).CopyTo(d, 72);
            return d;
        }

        [Fact]
        public void CountsAreReadFromTheHeader()
        {
            Assert.True(XmModule.TryReadCounts(Xm(10, 33, 26), out int ch, out int pat, out int ins));
            Assert.Equal(10, ch); Assert.Equal(33, pat); Assert.Equal(26, ins);
        }

        [Fact]
        public void NonModulesAreRejected()
        {
            Assert.False(XmModule.TryReadCounts(new byte[80], out _, out _, out _));
            Assert.False(XmModule.TryReadCounts(null, out _, out _, out _));
            Assert.False(XmModule.TryReadCounts(new byte[4], out _, out _, out _));
        }
    }
}
