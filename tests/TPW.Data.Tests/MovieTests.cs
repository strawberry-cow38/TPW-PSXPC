using System;
using System.Collections.Generic;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    /// <summary>XA-ADPCM, the movies' soundtrack. Every expected value is worked by hand from the format, not
    /// computed with the decoder's own arithmetic, so a test here can disagree with the implementation.</summary>
    public class XaAudioTests
    {
        const byte Stereo4 = 0x01;   // stereo, 37,800 Hz, 4-bit: what every movie on the disc uses
        const byte Mono4 = 0x00;

        static byte[] Sector(byte coding)
        {
            var raw = new byte[DiscReader.RawSectorSize];
            raw[18] = raw[22] = 0x64;          // submode: real-time + Form 2 + audio
            raw[19] = raw[23] = coding;
            return raw;
        }

        /// <summary>Set a unit's parameters in a group, and its copy where the format keeps one.</summary>
        static void Param(byte[] raw, int group, int unit, int filter, int range)
        {
            int p = XaAudio.FirstGroupOffset + group * XaAudio.GroupBytes;
            byte v = (byte)((filter << 4) | range);
            raw[p + 4 + unit] = v;
            if (unit < 4) raw[p + unit] = v;         // bytes 0..3 copy 4..7
            else raw[p + 8 + unit] = v;              // bytes 12..15 copy 8..11
        }

        static void Nibble(byte[] raw, int group, int unit, int j, int value)
        {
            int at = XaAudio.FirstGroupOffset + group * XaAudio.GroupBytes + 16 + unit / 2 + j * 4;
            raw[at] = unit % 2 == 0
                ? (byte)((raw[at] & 0xF0) | (value & 0x0F))
                : (byte)((raw[at] & 0x0F) | ((value & 0x0F) << 4));
        }

        static List<short> Decode(params byte[][] sectors)
        {
            var d = new XaAudio.Decoder();
            var outp = new List<short>();
            foreach (var s in sectors) Assert.True(d.TryDecodeSector(s, outp, out string e), e);
            return outp;
        }

        [Fact]
        public void OneSectorIs2016StereoFramesOr4032MonoSamples()
        {
            Assert.Equal(2016 * 2, Decode(Sector(Stereo4)).Count);
            Assert.Equal(4032, Decode(Sector(Mono4)).Count);
            Assert.Equal(2016, new XaAudio.Coding(Stereo4).SamplesPerChannelPerSector);
        }

        // ⭐ REJECTS TWO LAYOUT BUGS AT ONCE: left and right swapped, and the two nibbles of a byte swapped.
        // Unit 0 is the LOW nibble and goes LEFT; unit 1 is the HIGH nibble and goes RIGHT. Either mistake
        // still produces stereo audio of the right length, which is why it needs a test with teeth.
        [Fact]
        public void EvenUnitsAreLeftAndTheLowNibble()
        {
            var raw = Sector(Stereo4);
            Param(raw, 0, 0, 0, 12);                 // range 12 = shift 0, filter 0: sample = nibble
            Param(raw, 0, 1, 0, 12);
            Nibble(raw, 0, 0, 0, 1);
            Nibble(raw, 0, 1, 0, -2);
            var pcm = Decode(raw);
            Assert.Equal(1, pcm[0]);                 // left
            Assert.Equal(-2, pcm[1]);                // right
        }

        // ⭐ REJECTS DROPPING THE +32. With filter 1 (60/64) and a constant nibble of 1, the rounded filter
        // climbs 1,2,3,…,9,9. Truncating instead gives (60+0)>>6 = 0 at the second sample and stays at 1
        // for ever, so this sequence cannot come out of the unrounded version. Worked by hand:
        //   j1: (1*60+32)>>6 = 92>>6 = 1 -> 2      j8: (8*60+32)>>6 = 512>>6 = 8 -> 9
        //   j2: (2*60+32)>>6 = 152>>6 = 2 -> 3     j9: (9*60+32)>>6 = 572>>6 = 8 -> 9
        [Fact]
        public void TheFilterRoundsToNearest()
        {
            var raw = Sector(Stereo4);
            Param(raw, 0, 0, 1, 12);
            for (int j = 0; j < 10; j++) Nibble(raw, 0, 0, j, 1);
            var pcm = Decode(raw);
            short[] expect = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 9 };
            for (int j = 0; j < expect.Length; j++) Assert.Equal(expect[j], pcm[2 * j]);
        }

        [Fact]
        public void RangeSetsTheShift()
        {
            var raw = Sector(Stereo4);
            Param(raw, 0, 0, 0, 8);                  // range 8 = shift 4
            Nibble(raw, 0, 0, 0, 1);
            Nibble(raw, 0, 0, 1, -1);
            var pcm = Decode(raw);
            Assert.Equal(16, pcm[0]);
            Assert.Equal(-16, pcm[2]);
        }

        // ⭐ REJECTS A DECODER PER SECTOR. The left channel's last sample in a sector is unit 6 of group 17,
        // row 27. Set it to 5; the next sector's first left sample, through filter 1 with a zero nibble, is
        // then (5*60+32)>>6 = 5. A fresh decoder has no history and gives 0: a click every 53 ms.
        [Fact]
        public void TheFilterHistoryCarriesIntoTheNextSector()
        {
            var a = Sector(Stereo4);
            Param(a, 17, 6, 0, 12);
            Nibble(a, 17, 6, 27, 5);
            var b = Sector(Stereo4);
            Param(b, 0, 0, 1, 12);

            var pcm = Decode(a, b);
            Assert.Equal(5, pcm[2016 * 2 - 2]);      // last left sample of the first sector
            Assert.Equal(5, pcm[2016 * 2]);          // first left sample of the second

            var fresh = Decode(b);
            Assert.Equal(0, fresh[0]);
        }

        // ⭐ In MONO both units of a pair run through ONE history, in order. Unit 0 ends on 3; unit 1 starts
        // through filter 1 with a zero nibble, so (3*60+32)>>6 = 3. Keeping two histories, as stereo does,
        // gives 0 here.
        [Fact]
        public void MonoChainsBothUnitsThroughOneHistory()
        {
            var raw = Sector(Mono4);
            Param(raw, 0, 0, 0, 12);
            Nibble(raw, 0, 0, 27, 3);
            Param(raw, 0, 1, 1, 12);
            var pcm = Decode(raw);
            Assert.Equal(3, pcm[27]);
            Assert.Equal(3, pcm[28]);
        }

        // ⭐ THE ORACLE HAS TO BE ABLE TO SAY NO. One corrupted copy, one bad group, out of 18.
        [Fact]
        public void DisagreeingParameterCopiesAreCounted()
        {
            var raw = Sector(Stereo4);
            raw[XaAudio.FirstGroupOffset + 3 * XaAudio.GroupBytes + 0] = 0x2C;
            var d = new XaAudio.Decoder();
            Assert.True(d.TryDecodeSector(raw, new List<short>(), out _));
            Assert.Equal(18, d.Groups);
            Assert.Equal(1, d.BadCopies);
        }

        [Fact]
        public void EightBitIsRefusedRatherThanGuessed()
        {
            var outp = new List<short>();
            Assert.False(new XaAudio.Decoder().TryDecodeSector(Sector(0x11), outp, out string e));
            Assert.Contains("8-bit", e);
            Assert.Empty(outp);
        }

        [Fact]
        public void ReservedCodingIsRefused()
            => Assert.False(new XaAudio.Decoder().TryDecodeSector(Sector(0x03), new List<short>(), out _));
    }

    /// <summary>The .STR container: frame assembly and the sector clock.</summary>
    public class StrVideoTests
    {
        static byte[] Video(int chunk, int chunks, int frame, int frameBytes, byte fill)
        {
            var raw = new byte[DiscReader.RawSectorSize];
            raw[18] = raw[22] = 0x48;          // submode: real-time + data, Form 1
            int at = 24;
            BitConverter.GetBytes(StrVideo.ChunkMagic).CopyTo(raw, at);
            BitConverter.GetBytes((ushort)chunk).CopyTo(raw, at + 4);
            BitConverter.GetBytes((ushort)chunks).CopyTo(raw, at + 6);
            BitConverter.GetBytes(frame).CopyTo(raw, at + 8);
            BitConverter.GetBytes(frameBytes).CopyTo(raw, at + 12);
            BitConverter.GetBytes((ushort)320).CopyTo(raw, at + 16);
            BitConverter.GetBytes((ushort)176).CopyTo(raw, at + 18);
            for (int k = 0; k < StrVideo.ChunkPayloadBytes; k++) raw[at + StrVideo.SectorHeaderBytes + k] = fill;
            return raw;
        }

        static byte[] Audio()
        {
            var raw = new byte[DiscReader.RawSectorSize];
            raw[18] = raw[22] = 0x64;
            return raw;
        }

        // ⭐ REJECTS ASSUMING THE CHUNK COUNT. On the disc it is 10 or 11 per frame. Here a 2-chunk frame is
        // followed by a 3-chunk one: an assembler that assumed any fixed count, or carried the first frame's
        // count forward, gets at least one of them wrong.
        [Fact]
        public void TheChunkCountIsReadFromEachFrame()
        {
            var asm = new StrVideo.FrameAssembler();
            int pay = StrVideo.ChunkPayloadBytes;
            var sectors = new[]
            {
                Video(0, 2, 1, pay + 500, 0xA0), Video(1, 2, 1, pay + 500, 0xA1),
                Audio(),
                Video(0, 3, 2, 2 * pay + 10, 0xB0), Video(1, 3, 2, 2 * pay + 10, 0xB1), Video(2, 3, 2, 2 * pay + 10, 0xB2),
            };
            var frames = new List<StrFrame>();
            for (int i = 0; i < sectors.Length; i++)
            {
                var f = asm.Feed(sectors[i], i);
                if (f != null) frames.Add(f);
            }

            Assert.Equal(2, frames.Count);
            Assert.Equal(pay + 500, frames[0].Data.Length);
            Assert.Equal(0xA1, frames[0].Data[pay]);
            Assert.Equal(1, frames[0].LastSector);
            Assert.Equal(2 * pay + 10, frames[1].Data.Length);
            Assert.Equal(0xB2, frames[1].Data[2 * pay]);
            Assert.Equal(5, frames[1].LastSector);
            Assert.Equal(0, asm.Misassembled);
        }

        [Fact]
        public void AFrameMissingAChunkIsDroppedAndCounted()
        {
            var asm = new StrVideo.FrameAssembler();
            int bytes = 2 * StrVideo.ChunkPayloadBytes + 100;   // needs all three chunks
            Assert.Null(asm.Feed(Video(0, 3, 1, bytes, 1), 0));
            Assert.Null(asm.Feed(Video(2, 3, 1, bytes, 3), 1)); // chunk 1 never arrived
            Assert.Equal(1, asm.Misassembled);
        }

        // Worked by hand at 150 sectors/s: frames complete at sectors 10, 21 and 32, so they are ready at
        // 11/150, 22/150 and 33/150 s.
        [Fact]
        public void FrameAtShowsTheLastFrameReadyByThen()
        {
            var m = new StrMovie { Sectors = 40 };
            foreach (int last in new[] { 10, 21, 32 }) m.Frames.Add(new StrFrame { LastSector = last });

            Assert.Equal(-1, m.FrameAt(0));
            Assert.Equal(-1, m.FrameAt(10.9 / 150));
            Assert.Equal(0, m.FrameAt(11.0 / 150));
            Assert.Equal(1, m.FrameAt(30.0 / 150));
            Assert.Equal(2, m.FrameAt(1.0));
        }
    }
}
