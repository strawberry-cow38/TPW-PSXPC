using System;

namespace TPW.Data
{
    /// <summary>A decoded image, always RGBA8 top-left origin — the one layout every consumer wants.</summary>
    public sealed class TpwImage
    {
        public int Width;
        public int Height;
        public byte[] Rgba;          // Width * Height * 4, row-major from the top
        public string Source = "";   // where it came from, for the self-test report
    }

    /// <summary>Targa decoder.
    ///
    /// ⭐ WHY THIS EXISTS AT ALL: this is a PlayStation game, so the expected image format was TIM. There is
    /// not one TIM on the disc. I scanned for them with a STRUCTURAL check -- a TIM's block length must equal
    /// 12 + w*h*2 exactly -- and got zero across every file. Matching the four magic bytes alone instead gave
    /// four hits inside FOLIO.GAZ, all of them ordinary numbers in the archive's own offset table. A magic
    /// number is not a format check: it is four bytes that any table of small integers will eventually
    /// produce. What the disc actually carries is PC-authored Targa, footer and all.</summary>
    public static class Tga
    {
        public const string Footer = "TRUEVISION-XFILE.";
        public const int HeaderSize = 18;

        /// <summary>Decode a TGA. Returns false with a reason rather than throwing: the self-test reports on
        /// every asset, so one bad file must not end the run.</summary>
        /// <summary>Channel order of the 5-5-5 fields in a 15/16-bit pixel.</summary>
        public enum Order
        {
            /// <summary>Red in the high bits. What the TGA specification says.</summary>
            Argb1555,
            /// <summary>Blue in the high bits. What PlayStation VRAM holds natively, so a file whose pixel
            /// block was produced ready to upload carries this regardless of the container it sits in.</summary>
            Abgr1555,
        }

        public static bool TryDecode(byte[] d, out TpwImage img, out string error)
            => TryDecode(d, Order.Argb1555, out img, out error);

        /// <summary>Decode a file whose pixel block was authored ready to DMA into PlayStation VRAM.
        ///
        /// ⭐ SUCH A FILE IS NOT REALLY A TGA — it is a VRAM image with a TGA header bolted on, so the parts
        /// of the header that describe LAYOUT are meaningless and must be ignored. Two consequences, both
        /// established by running it and looking:
        ///   • channel order is BGR555 (low 5 bits RED), because that is what the hardware reads;
        ///   • the descriptor's origin bits are NOT honoured, because the rows are already in upload order.
        /// Applying the spec-correct bottom-up flip to this file produces a mirrored image, which is how this
        /// was found: master read the text off the screen twice and said which way it was wrong.</summary>
        public static bool TryDecodeVramBlock(byte[] d, out TpwImage img, out string error)
            => TryDecode(d, Order.Abgr1555, respectDescriptor: false, out img, out error);

        public static bool TryDecode(byte[] d, Order order, out TpwImage img, out string error)
            => TryDecode(d, order, true, out img, out error);

        public static bool TryDecode(byte[] d, Order order, bool respectDescriptor, out TpwImage img, out string error)
        {
            img = null; error = null;
            if (d == null || d.Length < HeaderSize) { error = "shorter than a TGA header"; return false; }

            int idLen = d[0];
            int cmapType = d[1];
            int imgType = d[2];
            int cmapFirst = BitConverter.ToUInt16(d, 3);
            int cmapLen = BitConverter.ToUInt16(d, 5);
            int cmapBits = d[7];
            int w = BitConverter.ToUInt16(d, 12);
            int h = BitConverter.ToUInt16(d, 14);
            int bpp = d[16];
            int desc = d[17];

            if (cmapType > 1) { error = $"colour-map type {cmapType} is not 0 or 1"; return false; }
            if (w <= 0 || h <= 0) { error = $"degenerate size {w}x{h}"; return false; }
            if (bpp != 8 && bpp != 15 && bpp != 16 && bpp != 24 && bpp != 32)
            { error = $"{bpp} bits per pixel is not a TGA depth"; return false; }

            bool rle = imgType is 9 or 10 or 11;
            bool mapped = imgType is 1 or 9;
            bool gray = imgType is 3 or 11;
            if (imgType is not (1 or 2 or 3 or 9 or 10 or 11)) { error = $"image type {imgType}"; return false; }

            int p = HeaderSize + idLen;
            byte[] palette = null;
            if (cmapType == 1)
            {
                int entryBytes = (cmapBits + 7) / 8;
                int mapBytes = cmapLen * entryBytes;
                if (p + mapBytes > d.Length) { error = "colour map runs past the end"; return false; }
                palette = new byte[cmapLen * 4];
                for (int i = 0; i < cmapLen; i++)
                    WritePixel(d, p + i * entryBytes, cmapBits, palette, i * 4, null, 0, false, order);
                p += mapBytes;
            }
            if (mapped && palette == null) { error = "colour-mapped image with no colour map"; return false; }

            int srcBytes = (bpp + 7) / 8;
            var rgba = new byte[w * h * 4];
            int pixels = w * h;

            if (!rle)
            {
                long need = (long)pixels * srcBytes;
                if (p + need > d.Length) { error = $"pixel data needs {need:n0} bytes, {d.Length - p:n0} remain"; return false; }
                for (int i = 0; i < pixels; i++)
                    WritePixel(d, p + i * srcBytes, bpp, rgba, i * 4, palette, cmapFirst, gray, order);
            }
            else
            {
                int i = 0;
                while (i < pixels)
                {
                    if (p >= d.Length) { error = $"RLE data ended after {i:n0} of {pixels:n0} pixels"; return false; }
                    int packet = d[p++];
                    int count = (packet & 0x7F) + 1;
                    if (i + count > pixels) count = pixels - i;      // tolerate a run overshooting the last row
                    if ((packet & 0x80) != 0)
                    {
                        if (p + srcBytes > d.Length) { error = "RLE run header ran past the end"; return false; }
                        for (int k = 0; k < count; k++)
                            WritePixel(d, p, bpp, rgba, (i + k) * 4, palette, cmapFirst, gray, order);
                        p += srcBytes;
                    }
                    else
                    {
                        if (p + count * srcBytes > d.Length) { error = "RLE literal run ran past the end"; return false; }
                        for (int k = 0; k < count; k++)
                            WritePixel(d, p + k * srcBytes, bpp, rgba, (i + k) * 4, palette, cmapFirst, gray, order);
                        p += count * srcBytes;
                    }
                    i += count;
                }
            }

            // ⚠ TGA IS BOTTOM-UP BY DEFAULT. Bit 5 of the descriptor set means the first row stored is the TOP
            // row; clear -- which is the common case and this disc's case -- means it is the BOTTOM. Getting
            // this wrong produces a perfectly valid, perfectly upside-down image, which reads as an engine or
            // UV problem and gets debugged in the wrong file entirely.
            if (respectDescriptor)
            {
                if ((desc & 0x20) == 0) FlipVertical(rgba, w, h);
                // Bit 4 set means right-to-left. Rare, but free to honour.
                if ((desc & 0x10) != 0) FlipHorizontal(rgba, w, h);
            }

            img = new TpwImage { Width = w, Height = h, Rgba = rgba };
            return true;
        }

        static void WritePixel(byte[] src, int sp, int bits, byte[] dst, int dp, byte[] palette, int cmapFirst,
                               bool gray, Order order = Order.Argb1555)
        {
            switch (bits)
            {
                case 8:
                    if (palette != null)
                    {
                        int idx = (src[sp] - cmapFirst) * 4;
                        if (idx >= 0 && idx + 3 < palette.Length)
                        {
                            dst[dp] = palette[idx]; dst[dp + 1] = palette[idx + 1];
                            dst[dp + 2] = palette[idx + 2]; dst[dp + 3] = palette[idx + 3];
                            return;
                        }
                    }
                    dst[dp] = dst[dp + 1] = dst[dp + 2] = src[sp];
                    dst[dp + 3] = 255;
                    return;

                case 15:
                case 16:
                {
                    // A1R5G5B5, little-endian. 5 bits scale to 8 as (v<<3)|(v>>2): that maps 31 to 255, where a
                    // plain v<<3 maps it to 248 and every white in the image comes out faintly grey.
                    int v = src[sp] | (src[sp + 1] << 8);
                    int hi = (v >> 10) & 31, g = (v >> 5) & 31, lo = v & 31;
                    int r = order == Order.Abgr1555 ? lo : hi;
                    int b = order == Order.Abgr1555 ? hi : lo;
                    dst[dp] = (byte)((r << 3) | (r >> 2));
                    dst[dp + 1] = (byte)((g << 3) | (g >> 2));
                    dst[dp + 2] = (byte)((b << 3) | (b >> 2));
                    // ⚠ 15-bit has no alpha channel. 16-bit nominally puts alpha in bit 15, but writers that
                    // emit 16 while meaning 15 leave it clear, so honouring it turns whole images transparent.
                    // Opaque either way is the safe reading; a format that truly needs keying says so elsewhere.
                    dst[dp + 3] = 255;
                    return;
                }

                case 24:
                    dst[dp] = src[sp + 2]; dst[dp + 1] = src[sp + 1]; dst[dp + 2] = src[sp]; dst[dp + 3] = 255;
                    return;

                case 32:
                    dst[dp] = src[sp + 2]; dst[dp + 1] = src[sp + 1]; dst[dp + 2] = src[sp]; dst[dp + 3] = src[sp + 3];
                    return;
            }
        }

        static void FlipVertical(byte[] px, int w, int h)
        {
            int stride = w * 4;
            var tmp = new byte[stride];
            for (int y = 0; y < h / 2; y++)
            {
                int a = y * stride, b = (h - 1 - y) * stride;
                Buffer.BlockCopy(px, a, tmp, 0, stride);
                Buffer.BlockCopy(px, b, px, a, stride);
                Buffer.BlockCopy(tmp, 0, px, b, stride);
            }
        }

        static void FlipHorizontal(byte[] px, int w, int h)
        {
            for (int y = 0; y < h; y++)
            {
                int row = y * w * 4;
                for (int x = 0; x < w / 2; x++)
                {
                    int a = row + x * 4, b = row + (w - 1 - x) * 4;
                    for (int k = 0; k < 4; k++) (px[a + k], px[b + k]) = (px[b + k], px[a + k]);
                }
            }
        }
    }
}
