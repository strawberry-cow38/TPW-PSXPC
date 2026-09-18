using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>Colour lookup tables — the palettes the 4bpp texture pages index into.
    ///
    /// ⭐ THEY ARE NOT IN THE TEXTURE PAGES. They live in two headerless 128 KiB archive entries, `0x104` and
    /// `0x10C`, which are streamed into a separate band of VRAM. tinyclaw found this from the drawing side:
    /// their build logs every draw's CLUT address, and all of them sit at y &lt; 256, outside the region the
    /// texture pages occupy. Searching the pages for palettes would have found nothing, forever.
    ///
    /// ✅ STRUCTURE CONFIRMED BY A SIGNATURE THE ALIGNMENT CANNOT FAKE. Each table is 32 bytes: 16 entries of
    /// BGR555. Read at that stride, **every table begins `0000 8001`** — index 0 is `0x0000`, which is PSX
    /// transparent, and index 1 is `0x8001`, black with the semi-transparency bit set. A wrong stride would
    /// scatter those two words instead of landing them at the head of every block. Each table also holds 16
    /// distinct colours, which a misaligned read would not reliably produce.
    ///
    /// Corroborated from the other direction: tinyclaw counted 177 draws using 47 distinct palettes in one
    /// park, **every one of them 16 colours** — the drawing side independently agreeing with 4bpp.
    ///
    /// ⚠⚠ WHICH TABLE BELONGS TO WHICH TEXTURE IS NOT KNOWN AND IS NOT GUESSABLE. The CLUT id is packed into
    /// the drawing commands, not stored with the page. There are 4,096 tables in a strip and roughly 47 in
    /// use, so trying them until a picture looks plausible is not identification — it is the
    /// match-by-rearranging failure that produces a decoder nobody can trust. Rendering a page under four
    /// different tables gives four differently-coloured versions of the same coherent image, so "it looks
    /// like something" cannot distinguish the right one.</summary>
    public static class Clut
    {
        public const int Entries = 16;
        public const int Bytes = Entries * 2;
        /// <summary>The two archive entries that carry palettes, and nothing else.</summary>
        public static readonly int[] StripEntryIndices = { 0x104, 0x10C };
        public const int StripBytes = 128 * 1024;

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

        /// <summary>True if the block at <paramref name="tableIndex"/> carries the shape every real table on
        /// this disc has. Used to check a strip is what we think it is, not to identify a palette.</summary>
        public static bool LooksLikeTable(byte[] strip, int tableIndex)
        {
            if (strip == null) return false;
            int at = tableIndex * Bytes;
            if (at < 0 || at + Bytes > strip.Length) return false;
            int first = strip[at] | (strip[at + 1] << 8);
            int second = strip[at + 2] | (strip[at + 3] << 8);
            if (first != 0x0000 || second != 0x8001) return false;

            var seen = new HashSet<int>();
            for (int i = 0; i < Entries; i++) seen.Add(strip[at + i * 2] | (strip[at + i * 2 + 1] << 8));
            return seen.Count == Entries;
        }
    }
}
