using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>Colour lookup tables — the palettes the 4bpp texture pages index into. 32 bytes: 16 entries
    /// of BGR555.
    ///
    /// ⚠⚠ PALETTES CANNOT BE FOUND BY SCANNING THIS ARCHIVE, AND AN EARLIER VERSION OF THIS FILE CLAIMED
    /// THEY COULD. It said they live in two headerless 128 KiB entries, `0x104` and `0x10C`, and identified
    /// them by every table opening `0x0000, 0x8001`. Both halves are wrong:
    ///
    ///   • The palettes the game actually draws with are NOT in those strips. tinyclaw traced four of them
    ///     back through the drawing commands: three are in entry **#169**, a contiguous array at a `0x20`
    ///     stride, and one is in entry **#416** at `+0x36C0`. Entry #169 contains **zero** blocks matching
    ///     the signature I was testing for.
    ///   • The signature itself selects something, but not these. It hits 18 blocks in `0x104`, 47 in
    ///     `0x10C` and 32 in `0x1A0` — structured data, just not the palettes in use.
    ///
    /// ⚠ AND THE OBVIOUS FALLBACK IS VACUOUS. "16 distinct halfwords in 32 bytes" looks like a real
    /// constraint and is not: sixteen random 16-bit values are all distinct about **99.8%** of the time, so
    /// nearly every block in the archive passes it. There is no structural test here worth writing — a
    /// palette is 32 bytes of arbitrary colour, which is indistinguishable from 32 bytes of anything else.
    ///
    /// ⭐ SO THE ADDRESSES MUST COME FROM THE DRAWING COMMANDS, NOT FROM THE FILE. That is not a gap to be
    /// closed by a better scan; it is a property of the format. tinyclaw's tracing is the source, and the
    /// reason to trust a given match is the evidence for THAT match — the (928,25) run has 16 distinct
    /// halfwords out of 16 and occurs exactly twice in 16 MB, and three of five traced palettes cluster in
    /// one entry, which chance does not do.
    ///
    /// ⚠ Two of the five traced palettes are not in the archive verbatim at all, so some arrive compressed
    /// or are built at runtime. A plain lookup will not cover all 47 in use.</summary>
    public static class Clut
    {
        public const int Entries = 16;
        public const int Bytes = Entries * 2;
        /// <summary>Entries known to hold palettes, from tinyclaw tracing draw commands back to bytes.
        /// ⚠ NOT exhaustive and NOT derived from any property of the file — see the class note. #169 is a
        /// contiguous array at a 0x20 stride; #416 holds at least one at +0x36C0.</summary>
        public static readonly int[] KnownPaletteEntries = { 169, 416 };

        /// <summary>Read one table as RGBA8, 16 colours.
        ///
        /// ⚠ INDEX 0 IS TRANSPARENT ON THIS HARDWARE, not black. `0x0000` means "draw nothing here", and a
        /// decoder that renders it as opaque black fills every cut-out with a black rectangle — which looks
        /// deliberate on a dark background and wrong everywhere else.</summary>
        public static byte[] Read(byte[] strip, int tableIndex)
        {
            var rgba = new byte[Entries * 4];
            if (strip == null) return rgba;
            int at = tableIndex * Bytes;
            if (at < 0 || at + Bytes > strip.Length) return rgba;

            for (int i = 0; i < Entries; i++)
            {
                int w = strip[at + i * 2] | (strip[at + i * 2 + 1] << 8);
                // BGR555, red in the LOW five bits — the hardware's order, same as the texture pages.
                int r = w & 31, g = (w >> 5) & 31, b = (w >> 10) & 31;
                int o = i * 4;
                rgba[o] = (byte)((r << 3) | (r >> 2));
                rgba[o + 1] = (byte)((g << 3) | (g >> 2));
                rgba[o + 2] = (byte)((b << 3) | (b >> 2));
                rgba[o + 3] = (byte)((w & 0x7FFF) == 0 ? 0 : 255);
            }
            return rgba;
        }

        /// <summary>How many tables a strip can hold.</summary>
        public static int TableCount(byte[] strip) => strip == null ? 0 : strip.Length / Bytes;

        // ⚠ THERE IS DELIBERATELY NO LooksLikeTable HERE ANY MORE. The version that existed tested for a
        // `0x0000, 0x8001` prefix that the game's actual palettes do not have, and the obvious replacement —
        // "16 distinct halfwords" — passes ~99.8% of random blocks. Offering either would invite callers to
        // scan for palettes, which cannot work. Read a table only at an address something else established.
    }
}
