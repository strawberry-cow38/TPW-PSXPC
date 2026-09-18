using System;
using System.IO;
using System.Text;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    public class TgaTests
    {
        static byte[] Header(int type, int w, int h, int bpp, int desc = 0)
        {
            var d = new byte[Tga.HeaderSize];
            d[2] = (byte)type;
            d[12] = (byte)(w & 0xFF); d[13] = (byte)(w >> 8);
            d[14] = (byte)(h & 0xFF); d[15] = (byte)(h >> 8);
            d[16] = (byte)bpp; d[17] = (byte)desc;
            return d;
        }

        static byte[] Concat(params byte[][] parts)
        {
            int n = 0; foreach (var p in parts) n += p.Length;
            var o = new byte[n]; int k = 0;
            foreach (var p in parts) { Buffer.BlockCopy(p, 0, o, k, p.Length); k += p.Length; }
            return o;
        }

        // ⭐ REJECTS THE 5-BIT SCALING BUG. A plain v<<3 turns 31 into 248, so every white in the game comes
        // out faintly grey and nobody notices until a screenshot is compared side by side.
        [Fact]
        public void FiveBitWhiteBecomesFullWhite()
        {
            var px = new byte[] { 0xFF, 0x7F };                       // A1R5G5B5: r=g=b=31
            Assert.True(Tga.TryDecode(Concat(Header(2, 1, 1, 15, 0x20), px), out var img, out var err), err);
            Assert.Equal(new byte[] { 255, 255, 255, 255 }, img.Rgba);
        }

        [Fact]
        public void FifteenBitChannelsAreNotSwapped()
        {
            // pure red: r=31, g=0, b=0 -> 0x7C00
            Assert.True(Tga.TryDecode(Concat(Header(2, 1, 1, 15, 0x20), new byte[] { 0x00, 0x7C }), out var img, out _));
            Assert.Equal(255, img.Rgba[0]);
            Assert.Equal(0, img.Rgba[1]);
            Assert.Equal(0, img.Rgba[2]);
        }

        // ⭐ REJECTS THE UPSIDE-DOWN BUG. TGA is bottom-up unless descriptor bit 5 says otherwise, and this
        // disc's files leave it clear. A flipped image is perfectly valid and gets debugged as a UV problem.
        [Fact]
        public void BottomUpIsFlippedAndTopLeftIsNot()
        {
            // two rows, one black one white, stored bottom-first
            var rows = new byte[] { 0x00, 0x00, 0xFF, 0x7F };
            Assert.True(Tga.TryDecode(Concat(Header(2, 1, 2, 15, 0x00), rows), out var bottomUp, out _));
            Assert.Equal(255, bottomUp.Rgba[0]);      // stored last, drawn first
            Assert.Equal(0, bottomUp.Rgba[4]);

            Assert.True(Tga.TryDecode(Concat(Header(2, 1, 2, 15, 0x20), rows), out var topLeft, out _));
            Assert.Equal(0, topLeft.Rgba[0]);         // no flip
            Assert.Equal(255, topLeft.Rgba[4]);
        }

        [Fact]
        public void TwentyFourBitIsBgrOnDisk()
        {
            // BGR order: this is blue
            Assert.True(Tga.TryDecode(Concat(Header(2, 1, 1, 24, 0x20), new byte[] { 0xFF, 0x00, 0x00 }), out var img, out _));
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, img.Rgba);
        }

        [Fact]
        public void ThirtyTwoBitKeepsAlpha()
        {
            Assert.True(Tga.TryDecode(Concat(Header(2, 1, 1, 32, 0x20), new byte[] { 0x10, 0x20, 0x30, 0x40 }), out var img, out _));
            Assert.Equal(0x40, img.Rgba[3]);
        }

        [Fact]
        public void RleRunExpandsToTheSamePixels()
        {
            // one packet: RLE, count 4, one 15-bit white pixel
            var body = new byte[] { 0x83, 0xFF, 0x7F };
            Assert.True(Tga.TryDecode(Concat(Header(10, 4, 1, 15, 0x20), body), out var img, out var err), err);
            for (int i = 0; i < 4; i++) Assert.Equal(255, img.Rgba[i * 4]);
        }

        [Fact]
        public void RleLiteralRunIsCopiedVerbatim()
        {
            // raw packet of 2: red then blue
            var body = new byte[] { 0x01, 0x00, 0x7C, 0x1F, 0x00 };
            Assert.True(Tga.TryDecode(Concat(Header(10, 2, 1, 15, 0x20), body), out var img, out _));
            Assert.Equal(255, img.Rgba[0]);   // red
            Assert.Equal(255, img.Rgba[6]);   // blue in the second pixel
        }

        // ⭐ A TRUNCATED FILE MUST BE REPORTED, NOT THROWN AND NOT SILENTLY HALF-DECODED. The self-test runs
        // over every asset; one bad entry must not end the run, and must not pass either.
        [Fact]
        public void TruncatedPixelDataFailsWithAReason()
        {
            var d = Concat(Header(2, 64, 64, 24, 0x20), new byte[10]);
            Assert.False(Tga.TryDecode(d, out _, out string err));
            Assert.Contains("needs", err);
        }

        [Fact]
        public void NonsenseDepthIsRejected()
        {
            Assert.False(Tga.TryDecode(Concat(Header(2, 1, 1, 7, 0x20), new byte[8]), out _, out string err));
            Assert.Contains("7 bits", err);
        }

        [Fact]
        public void ZeroSizeIsRejected()
            => Assert.False(Tga.TryDecode(Concat(Header(2, 0, 0, 24, 0x20), new byte[8]), out _, out _));

        [Fact]
        public void EmptyInputIsRejectedNotThrown()
            => Assert.False(Tga.TryDecode(Array.Empty<byte>(), out _, out _));
    }

    public class GazArchiveTests
    {
        // Builds an archive the way the real one is laid out, so the tests exercise the real reading.
        static byte[] Build(params (int off, int size)[] entries)
        {
            int end = 0;
            foreach (var (o, s) in entries) end = Math.Max(end, o + s);
            var d = new byte[Math.Max(end, GazArchive.HeaderSize + entries.Length * 8)];
            BitConverter.GetBytes((uint)entries.Length).CopyTo(d, 0);
            BitConverter.GetBytes(23u).CopyTo(d, 4);
            for (int i = 0; i < entries.Length; i++)
            {
                BitConverter.GetBytes(entries[i].off).CopyTo(d, GazArchive.HeaderSize + i * 8);
                BitConverter.GetBytes(entries[i].size).CopyTo(d, GazArchive.HeaderSize + i * 8 + 4);
            }
            return d;
        }

        [Fact]
        public void WellFormedArchiveParses()
        {
            var d = Build((0x800, 16), (0x1000, 32));
            Assert.True(GazArchive.TryParse(d, out var a, out string err), err);
            Assert.Equal(2, a.Count);
            Assert.Equal(2, a.AlignedCount());
        }

        // ⭐ EACH OF THESE IS A WAY THE FIELD LAYOUT COULD BE MISREAD. If the parser accepted them it would
        // "work" on garbage, which is how a wrong layout survives: it produces plausible entries instead of
        // an error, and the first thing anyone notices is a texture that looks like noise.
        [Fact]
        public void OverlappingEntriesAreRefused()
        {
            Assert.False(GazArchive.TryParse(Build((0x800, 0x900), (0x1000, 16)), out _, out string err));
            Assert.Contains("overlaps", err);
        }

        [Fact]
        public void EntryRunningPastTheEndIsRefused()
        {
            var d = Build((0x800, 16));
            BitConverter.GetBytes(999999).CopyTo(d, GazArchive.HeaderSize + 4);
            Assert.False(GazArchive.TryParse(d, out _, out string err));
            Assert.Contains("past the end", err);
        }

        [Fact]
        public void EntryPointingIntoItsOwnTableIsRefused()
        {
            Assert.False(GazArchive.TryParse(Build((4, 16)), out _, out string err));
            Assert.Contains("inside the table", err);
        }

        [Fact]
        public void ImplausibleCountIsRefused()
        {
            var d = new byte[64];
            BitConverter.GetBytes(0x7FFFFFFFu).CopyTo(d, 0);
            Assert.False(GazArchive.TryParse(d, out _, out string err));
            Assert.Contains("implausible", err);
        }

        [Fact]
        public void ContainerDeclaringADifferentSizeIsCaught()
        {
            var d = Build((0x800, 64));
            BitConverter.GetBytes(GazArchive.ContainerMagic).CopyTo(d, 0x800);
            BitConverter.GetBytes(999).CopyTo(d, 0x800 + 24);      // lies about its own length
            Assert.True(GazArchive.TryParse(d, out var a, out _));
            Assert.True(a.Entries[0].IsContainer);
            Assert.False(a.ContainerSizeAgrees(a.Entries[0], out int declared));
            Assert.Equal(999, declared);
        }

        [Fact]
        public void ContainerAgreeingWithTheTableIsAccepted()
        {
            var d = Build((0x800, 64));
            BitConverter.GetBytes(GazArchive.ContainerMagic).CopyTo(d, 0x800);
            BitConverter.GetBytes(64).CopyTo(d, 0x800 + 24);
            Assert.True(GazArchive.TryParse(d, out var a, out _));
            Assert.True(a.ContainerSizeAgrees(a.Entries[0], out _));
        }
    }

    public class DiscReaderTests
    {
        /// <summary>Builds a raw 2352-byte-per-sector Mode 2 image with one file in the root, which is the
        /// shape of a real PSX rip.</summary>
        static MemoryStream BuildRawImage(string fileName, byte[] content, out int fileLba)
        {
            const int Sectors = 64;
            fileLba = 20;
            var img = new byte[Sectors * DiscReader.RawSectorSize];

            for (int s = 0; s < Sectors; s++)
            {
                int b = s * DiscReader.RawSectorSize;
                img[b] = 0x00;
                for (int i = 1; i <= 10; i++) img[b + i] = 0xFF;
                img[b + 11] = 0x00;
                img[b + 15] = 2;                       // Mode 2 -> payload starts 24 bytes in
            }

            void Put(int lba, byte[] data, int at = 0)
            {
                int b = lba * DiscReader.RawSectorSize + 24 + at;
                Buffer.BlockCopy(data, 0, img, b, data.Length);
            }

            // primary volume descriptor
            var pvd = new byte[DiscReader.UserDataSize];
            pvd[0] = 1;
            Encoding.ASCII.GetBytes("CD001").CopyTo(pvd, 1);
            Encoding.ASCII.GetBytes("TESTVOL".PadRight(32)).CopyTo(pvd, 40);
            BitConverter.GetBytes(18).CopyTo(pvd, 156 + 2);            // root at lba 18
            BitConverter.GetBytes(DiscReader.UserDataSize).CopyTo(pvd, 156 + 10);
            Put(16, pvd);

            // root directory: the . and .. records, then the file
            var dir = new byte[DiscReader.UserDataSize];
            int p = 0;
            foreach (byte self in new byte[] { 0, 1 })
            {
                dir[p] = 34; dir[p + 25] = 2; dir[p + 32] = 1; dir[p + 33] = self;
                p += 34;
            }
            string iso = fileName + ";1";
            int recLen = 33 + iso.Length;
            if (recLen % 2 != 0) recLen++;
            dir[p] = (byte)recLen;
            BitConverter.GetBytes(fileLba).CopyTo(dir, p + 2);
            BitConverter.GetBytes(content.Length).CopyTo(dir, p + 10);
            dir[p + 25] = 0;
            dir[p + 32] = (byte)iso.Length;
            Encoding.ASCII.GetBytes(iso).CopyTo(dir, p + 33);
            Put(18, dir);

            for (int i = 0; i * DiscReader.UserDataSize < content.Length; i++)
            {
                int n = Math.Min(DiscReader.UserDataSize, content.Length - i * DiscReader.UserDataSize);
                var chunk = new byte[n];
                Buffer.BlockCopy(content, i * DiscReader.UserDataSize, chunk, 0, n);
                Put(fileLba + i, chunk);
            }
            return new MemoryStream(img);
        }

        // ⭐⭐ THE CENTRAL TEST. A PSX rip keeps 2352-byte sectors with the payload 24 bytes in. Read it as a
        // flat 2048-byte ISO and nothing throws -- the bytes are simply taken from the wrong place, drifting
        // 304 further out every sector. Round-tripping real content through the reader is the only thing that
        // catches it, because every structural check still passes on the wrong data.
        [Fact]
        public void RawSectorsAreDetectedAndContentRoundTrips()
        {
            var content = new byte[5000];
            for (int i = 0; i < content.Length; i++) content[i] = (byte)(i * 31 + 7);

            using var disc = DiscReader.Open(BuildRawImage("FOLIO.GAZ", content, out _));
            Assert.True(disc.IsRawSectors);
            Assert.Equal("TESTVOL", disc.VolumeId);

            var f = disc.Find("FOLIO.GAZ");
            Assert.NotNull(f);
            Assert.Equal(content.Length, f.Length);
            Assert.Equal(content, disc.ReadFile(f));
        }

        [Fact]
        public void TheVersionSuffixIsStrippedFromNames()
        {
            using var disc = DiscReader.Open(BuildRawImage("LEGAL.GFX", new byte[16], out _));
            Assert.NotNull(disc.Find("LEGAL.GFX"));
            Assert.Null(disc.Find("LEGAL.GFX;1"));
        }

        [Fact]
        public void DotAndDotDotAreNotListedAsFiles()
        {
            using var disc = DiscReader.Open(BuildRawImage("A.BIN", new byte[16], out _));
            Assert.Single(disc.Files);
        }

        [Fact]
        public void AnImageWithoutAVolumeDescriptorIsRefused()
        {
            var junk = new byte[DiscReader.RawSectorSize * 32];
            Assert.Throws<InvalidDataException>(() => DiscReader.Open(new MemoryStream(junk)));
        }

        [Fact]
        public void SelfTestFailsLoudlyWithNoDisc()
        {
            var r = AssetSelfTest.Run(null);
            Assert.False(r.AllOk);
            Assert.Contains("FAILED", r.Summary());
        }

        // The self-test must fail on a disc that is readable but is not this game -- "it parsed" is not the
        // same as "it is the right disc", and a port that starts on the wrong one produces plausible nonsense.
        [Fact]
        public void SelfTestFailsOnAValidDiscThatIsNotThisGame()
        {
            using var disc = DiscReader.Open(BuildRawImage("OTHER.DAT", new byte[16], out _));
            var r = AssetSelfTest.Run(disc);
            Assert.False(r.AllOk);
            Assert.Contains(r.Checks, c => c.Name == "boot executable" && !c.Ok);
        }
    }
}
