using System;

namespace TPW.Data
{
    /// <summary>UNPAK — the game's *other* compressor, a port of TPW.BIN <c>0x80018EF0</c>.
    ///
    /// ⚠⚠ THIS IS NOT THE SAME SCHEME AS <see cref="SubLz"/>, AND THE TWO ARE EASY TO CONFUSE BECAUSE BOTH
    /// ARE LZ AND BOTH LIVE IN THE SAME ARCHIVE. SubLz (0x800BFD9C) is bit-flag LZSS and expands compressed
    /// MESH sub-entries. UNPAK is run-and-backref and expands compressed TEXTURE entries. Feeding one to the
    /// other produces garbage of exactly the right size, which is the worst kind: the length check passes.
    ///
    /// Format: <c>u32 unpackedSize, u32 unknown</c>, then the packed stream. A control byte under 0x80 is a
    /// literal run of <c>c + 1</c> bytes; otherwise the next byte is a length and the back-reference offset
    /// is <c>c - 256</c> (always negative), copied byte-by-byte so an overlapping run repeats. A length of
    /// zero ends the stream.
    ///
    /// ⚠ The copy MUST be byte-by-byte from the output built so far. A block move breaks overlapping runs,
    /// which is how this format encodes repeats — and a repeat is exactly what a texture's flat regions are
    /// made of, so the corruption lands on the parts that look most obviously wrong.</summary>
    public static class Unpak
    {
        public const int HeaderBytes = 8;

        /// <summary>Entries fable identified as UNPAK'd texture pages (`compressed=1` in their header).
        ///
        /// ⚠ SUPERSEDED BY THE HEADER RULE: <see cref="TextureSheet.FindAll"/> finds 17 compressed sheets, these
        /// 13 plus #332, #333, #400 and #416, each expanding to exactly its entry's last byte. Kept for the
        /// warning below, which still applies to anyone reading the report.
        ///
        /// ⚠ THESE ARE DECIMAL. The report writes them zero-padded — 0005, 0082, 0258 — which reads as hex
        /// at a glance and is not. Taken as hex, 0x258 is 600 in a 422-entry archive (an index error, so at
        /// least it fails loudly) while 0x0082 and 0x0084 land on real entries that are MESH CONTAINERS, and
        /// those fail quietly as "UNPAK did not expand this", which looks like a decompressor bug.</summary>
        public static readonly int[] CompressedTextureEntries =
            { 5, 17, 18, 82, 84, 91, 92, 168, 169, 170, 258, 269, 278 };

        /// <summary>The game's routine exactly (0x80018EF0): expand one HEADERLESS stream into
        /// <paramref name="dst"/> until its terminator, reporting how many bytes it wrote and how many it
        /// consumed. <paramref name="consumed"/> INCLUDES the two-byte terminator, as the game's own counter
        /// (gp+0x1110, read back by 0x80018F74) does, because the texture loader advances to the next block's
        /// stream by exactly that much.
        ///
        /// ⚠ THIS IS THE FORM THE TEXTURE SHEETS USE, NOT THE 8-BYTE-HEADER FORM ABOVE. The loader (0x800280F8)
        /// calls it once per 64x64-halfword block with no header and no size: the stream runs to its terminator,
        /// and "the block came out at 0x2000 bytes" is then a check the caller makes, not a limit the stream
        /// knows about. <paramref name="dst"/> bounds the output; a stream that would pass it is refused.</summary>
        public static bool TryDecompressBlock(byte[] src, int offset, byte[] dst, out int produced, out int consumed, out string error)
        {
            produced = 0; consumed = 0; error = null;
            if (src == null || dst == null || offset < 0 || offset >= src.Length) { error = "offset outside the buffer"; return false; }
            int i = offset, n = 0;
            for (;;)
            {
                if (i >= src.Length) { error = $"stream ran off the end of the entry after {n:n0} bytes"; return false; }
                int c = src[i++];
                if (c < 0x80)
                {
                    int run = c + 1;
                    if (i + run > src.Length) { error = $"literal run of {run} past the end at {i}"; return false; }
                    if (n + run > dst.Length) { error = $"block would pass {dst.Length:n0} bytes"; return false; }
                    Buffer.BlockCopy(src, i, dst, n, run);
                    i += run; n += run;
                }
                else
                {
                    if (i >= src.Length) { error = "back-reference length byte past the end"; return false; }
                    int len = src[i++];
                    if (len == 0) break;                      // the terminator: counted in `consumed`
                    int from = n + (c - 256);
                    if (from < 0) { error = $"back-reference before the start at {n}"; return false; }
                    if (n + len > dst.Length) { error = $"block would pass {dst.Length:n0} bytes"; return false; }
                    for (int k = 0; k < len; k++) dst[n + k] = dst[from + k];
                    n += len;
                }
            }
            produced = n;
            consumed = i - offset;
            return true;
        }

        /// <summary>Expand a stream that begins with its own 8-byte header.</summary>
        public static bool TryDecompress(byte[] d, int offset, out byte[] dst, out string error)
        {
            dst = null; error = null;
            if (d == null || offset < 0 || offset + HeaderBytes > d.Length)
            { error = "no room for an UNPAK header"; return false; }

            int unpacked = BitConverter.ToInt32(d, offset);
            if (unpacked <= 0 || unpacked > 4 << 20) { error = $"implausible unpacked size {unpacked}"; return false; }
            return TryDecompress(d, offset + HeaderBytes, unpacked, out dst, out error);
        }

        /// <summary>Expand <paramref name="unpackedSize"/> bytes from a headerless stream.
        ///
        /// ⭐ THE DECLARED SIZE IS THE ORACLE, so it is checked rather than trusted: a stream that ends early
        /// or overruns is reported by name. Returning a buffer of the right length full of the wrong bytes is
        /// the failure this format makes easy.</summary>
        public static bool TryDecompress(byte[] d, int offset, int unpackedSize, out byte[] dst, out string error)
        {
            dst = null; error = null;
            if (d == null || offset < 0 || offset > d.Length) { error = "offset outside the buffer"; return false; }
            if (unpackedSize <= 0 || unpackedSize > 4 << 20) { error = $"implausible unpacked size {unpackedSize}"; return false; }

            var outBuf = new byte[unpackedSize];
            int n = 0, i = offset;

            while (i < d.Length)
            {
                int c = d[i++];
                if (c < 0x80)
                {
                    int run = c + 1;
                    if (i + run > d.Length) { error = $"literal run of {run} past the end at {i}"; return false; }
                    if (n + run > unpackedSize) { error = $"literal run would pass the declared size at {n}"; return false; }
                    Buffer.BlockCopy(d, i, outBuf, n, run);
                    i += run; n += run;
                }
                else
                {
                    if (i >= d.Length) { error = "back-reference length byte past the end"; return false; }
                    int len = d[i++];
                    if (len == 0) break;                      // end of stream
                    int from = n + (c - 256);                 // c >= 0x80, so this is always negative
                    if (from < 0) { error = $"back-reference before the start at {n}"; return false; }
                    if (n + len > unpackedSize) { error = $"back-reference would pass the declared size at {n}"; return false; }
                    // ⚠ Byte-by-byte on purpose: an overlapping run must repeat what it just wrote.
                    for (int k = 0; k < len; k++) outBuf[n + k] = outBuf[from + k];
                    n += len;
                }
                if (n == unpackedSize) break;
            }

            if (n != unpackedSize)
            { error = $"stream ended after {n:n0} of {unpackedSize:n0} bytes"; return false; }

            dst = outBuf;
            return true;
        }
    }
}
