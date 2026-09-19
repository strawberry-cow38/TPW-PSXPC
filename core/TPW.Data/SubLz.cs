using System;

namespace TPW.Data
{
    /// <summary>The LZSS behind every compressed FOLIO.GAZ sub-entry (and every TPW.OVL overlay): a port of
    /// TPW.BIN <c>0x800BFD9C(src, dst)</c>, read off the MIPS listing instruction by instruction rather than
    /// picked from the textbook variants and tuned until the output looked right.
    ///
    /// The routine (0x800BFD9C..0x800BFE68, 52 instructions, no calls, delay slots accounted for):
    /// <code>
    ///   flags = 0                                      clear t0
    ///   loop:
    ///     flags >>= 1                                  srl  t0,t0,1
    ///     if (flags &amp; 0xFF00) == 0:                    andi v0,t0,0xff00 ; bne
    ///         flags = *src++ | 0xFF00                  lbu ; ori t0,v0,0xff00
    ///     if (flags &amp; 1) == 0:                         xori v0,t0,1 ; andi v0,v0,1 ; beq → match
    ///         *dst++ = *src++                          literal
    ///     else:
    ///         c = *src++
    ///         if c >= 0x60:                            slti v0,a3,0x60 ; bne
    ///             offset = 0x100 - c                   subu a3,t3(=0x100),a3        → 1..160
    ///             length = 2                           li a2,2
    ///         else:
    ///             offset = ((c &amp; 0xF) &lt;&lt; 8) | *src++    sll/or                       → 12 bits
    ///             if offset == 0: return dst - start   bne a3,zero ; jr ra          ← THE ONLY EXIT
    ///             length = (c >> 4) + 3                sra a2,a3,4 ; addiu a2,a2,3  → 3..7
    ///             if (c >> 4) == 5: length = *src++ + 8    bne a2,t2(=5) ; lbu ; addiu v0,8  → 8..263
    ///         repeat length: *dst = *(dst - offset); dst++    byte-wise, so an overlap is a run
    /// </code>
    /// Eight flag bits per byte, consumed LSB first (the reload ORs in 0xFF00 and the shift carries those
    /// ones down as a counter); 0 = literal, 1 = match. The stream carries no size and the game checks no
    /// bounds: its caller 0x800308C4 allocates the container's declared unpackedSize, calls this once, and
    /// trusts the terminator. The overlay loader 0x800BE640 uses the same routine.
    ///
    /// ⭐ THE ORACLE THAT PROVES THE PORT, not "the output looks plausible": every one of the 25 compressed
    /// sub-entries on the disc (entries 0 and 3) reaches the terminator at EXACTLY its declared size, and
    /// each buffer then walks as a mesh -- vertices, faces, bones, animation tracks, trailing list -- to land
    /// on its own last byte. Entry 0 sub-entry 6's `f4 01 00 30 01 24 40 08` hand-decodes to
    /// `01 00 | 00×6 | 24 | 00×7`, the mesh header the loader reads. A wrong bit order, a wrong length
    /// bias or a wrong 0x60 split does not fail some of those; it fails nearly all of them.</summary>
    public static class SubLz
    {
        /// <summary>Longest match a single code can produce: nibble 5 plus a byte, plus 8.</summary>
        public const int MaxMatch = 255 + 8;

        /// <summary>Expand the stream at <paramref name="src"/>[<paramref name="offset"/>..] into exactly
        /// <paramref name="unpackedSize"/> bytes. Refuses -- rather than returning a partial buffer -- when
        /// the source runs out, a match reaches before the start of the output, the terminator arrives early,
        /// or the output would pass <paramref name="unpackedSize"/> before the terminator. Each of those is a
        /// defect in the data or in this port, and a mesh parsed out of a half-expanded buffer would report a
        /// wrong shape rather than an error.</summary>
        public static bool TryDecompress(byte[] src, int offset, int unpackedSize, out byte[] dst, out string error)
            => TryDecompress(src, offset, unpackedSize, out dst, out _, out error);

        /// <summary>As above, also reporting how many source bytes the stream occupied, terminator included.
        /// On the disc that is the packed gap to the next sub-entry less 0..3 bytes of alignment padding.</summary>
        public static bool TryDecompress(byte[] src, int offset, int unpackedSize, out byte[] dst, out int consumed, out string error)
        {
            dst = null; consumed = 0; error = null;
            if (src == null) { error = "no source"; return false; }
            if (offset < 0 || offset > src.Length) { error = $"offset 0x{offset:x} is outside the {src.Length:n0}-byte source"; return false; }
            if (unpackedSize <= 0) { error = $"unpacked size {unpackedSize} does not describe a compressed sub-entry"; return false; }

            var o = new byte[unpackedSize];
            int s = offset, d = 0;
            uint flags = 0;
            while (true)
            {
                flags >>= 1;
                if ((flags & 0xFF00) == 0)
                {
                    if (s >= src.Length) { error = Where("source ended where a flag byte was due", s, d, unpackedSize); return false; }
                    flags = (uint)src[s++] | 0xFF00;
                }

                if ((flags & 1) == 0)
                {
                    if (s >= src.Length) { error = Where("source ended inside a literal", s, d, unpackedSize); return false; }
                    if (d >= unpackedSize) { error = Where("a literal would pass the declared size", s, d, unpackedSize); return false; }
                    o[d++] = src[s++];
                    continue;
                }

                if (s >= src.Length) { error = Where("source ended where a match code was due", s, d, unpackedSize); return false; }
                int c = src[s++];
                int distance, length;
                if (c >= 0x60)
                {
                    distance = 0x100 - c;
                    length = 2;
                }
                else
                {
                    if (s >= src.Length) { error = Where("source ended inside a long match code", s, d, unpackedSize); return false; }
                    distance = ((c & 0x0F) << 8) | src[s++];
                    if (distance == 0)
                    {
                        // The terminator. The game returns dst - start here and its caller ignores it; we
                        // hold the stream to the size the container table promised.
                        consumed = s - offset;
                        if (d != unpackedSize) { error = $"stream ended after {d:n0} of the declared {unpackedSize:n0} bytes"; return false; }
                        dst = o;
                        return true;
                    }
                    int nibble = c >> 4;                       // 0..5, because c < 0x60
                    if (nibble == 5)
                    {
                        if (s >= src.Length) { error = Where("source ended inside a long-length match code", s, d, unpackedSize); return false; }
                        length = src[s++] + 8;
                    }
                    else length = nibble + 3;
                }

                if (distance > d) { error = Where($"a match reaches {distance} bytes back with only {d} written", s, d, unpackedSize); return false; }
                if (d + length > unpackedSize) { error = Where($"a {length}-byte match would pass the declared size", s, d, unpackedSize); return false; }
                // ⚠ ONE BYTE AT A TIME, ON PURPOSE. distance may be less than length (distance 1 is how the
                // game writes a run of zeros), so the copy must read bytes it has just written. A block copy
                // here would read stale bytes and produce a stream that is the right length and wrong.
                for (int i = 0; i < length; i++) { o[d] = o[d - distance]; d++; }
            }
        }

        static string Where(string what, int s, int d, int size) => $"{what} (source +0x{s:x}, {d:n0} of {size:n0} bytes out)";
    }
}
