using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>The game's texture sheets: a short header, then a raw block of PlayStation VRAM holding
    /// **four 256x256 texture pages side by side** at 4 bits per pixel.
    ///
    /// ⭐ LAYOUT: `0x54` header + a **1024x256** sheet at 4bpp = 0x54 + 1024*256/2 = 131,156, which is
    /// exactly the size of the twelve entries that carry it. A PSX texture page is 64 VRAM halfwords wide,
    /// which at 4bpp is 256 texels, so a 256-halfword-wide upload is four pages in a row. UVs in the draw
    /// commands run 0..255 within a page, confirming the same thing from the GPU side.
    ///
    /// ⚠⚠ I GOT THIS WRONG TWICE, AND BOTH WRONG ANSWERS LOOKED RIGHT. First as 256x256 at 16bpp, because
    /// 131,072 is exactly that and the size seemed to settle it — that decodes to coloured noise. Then as
    /// **512x512 at 4bpp**, because rendering it that way produces a single coherent image with legible text
    /// in it, while 1024x256 appeared to show the same picture twice. That "doubling" was not a duplicate: it
    /// was pages 0 and 2 carrying **similar but different** terrain art, and I read a resemblance as a
    /// repetition.
    ///
    /// ⭐ THE LESSON IS THAT "IT LOOKS COHERENT" IDENTIFIED NOTHING HERE. Both 512x512 and 1024x256 produce
    /// plausible pictures, because reinterpreting a 2D layout at a multiple of its true width rearranges the
    /// content without destroying local structure. What settled it was tinyclaw's tpage coordinates stepping
    /// by 64 halfwords — evidence from outside the file, of a kind no amount of looking at the pixels could
    /// supply. Rendered at this layout the four pages come apart cleanly: each is self-contained and no
    /// sprite runs across a cut.
    ///
    /// ⚠ THE PIXELS ARE PALETTE INDICES AND THIS CLASS HAS NO PALETTE. The CLUT is chosen per DRAW, not per
    /// page — one sheet is drawn with up to 11 different palettes for different rectangles of it — so a page
    /// cannot know its own colours. `TryDecode` renders the index as a grey level: enough to identify content
    /// and lay out an atlas, and not the real image.</summary>
    public static class VramTexture
    {
        public const int HeaderBytes = 0x54;
        /// <summary>One PSX texture page: 64 VRAM halfwords wide, which is 256 texels at 4bpp.</summary>
        public const int PageWidth = 256;
        public const int PageHeight = 256;
        public const int PagesPerSheet = 4;
        public const int Width = PageWidth * PagesPerSheet;   // 1024
        public const int Height = PageHeight;                 // 256
        public const int BitsPerPixel = 4;
        public const int StrideBytes = Width / 2;
        /// <summary>Exactly the size of a sheet entry in the archive: 0x54 + 1024*256/2.</summary>
        public const int EntryBytes = HeaderBytes + StrideBytes * Height;   // 131,156

        public static bool LooksLikeTexturePage(GazEntry e) => e != null && e.Size == EntryBytes;

        /// <summary>Decode one page to RGBA8, top-left origin.</summary>
        public static bool TryDecode(byte[] d, out TpwImage img, out string error)
        {
            img = null; error = null;
            if (d == null || d.Length < EntryBytes)
            { error = $"need {EntryBytes:n0} bytes for a texture page, have {d?.Length ?? 0:n0}"; return false; }

            var rgba = new byte[Width * Height * 4];
            for (int i = 0; i < Width * Height; i++)
            {
                int by = HeaderBytes + (i >> 1);
                // Low nibble first: the same order the hardware reads a 4bpp run in.
                int index = (i & 1) == 0 ? d[by] & 0x0F : d[by] >> 4;
                // Grey preview only — see the class note. 17 spreads 0..15 across 0..255 exactly.
                byte g = (byte)(index * 17);
                int dst = i * 4;
                rgba[dst] = g; rgba[dst + 1] = g; rgba[dst + 2] = g; rgba[dst + 3] = 255;
            }
            img = new TpwImage { Width = Width, Height = Height, Rgba = rgba };
            return true;
        }

        /// <summary>One of the four 256x256 pages out of a decoded sheet. `pageIndex` 0..3 left to right,
        /// which is the order their VRAM x coordinates run in.</summary>
        public static TpwImage Page(TpwImage sheet, int pageIndex)
        {
            if (sheet == null || pageIndex < 0 || pageIndex >= PagesPerSheet) return null;
            var rgba = new byte[PageWidth * PageHeight * 4];
            for (int y = 0; y < PageHeight; y++)
            {
                int src = (y * Width + pageIndex * PageWidth) * 4;
                Buffer.BlockCopy(sheet.Rgba, src, rgba, y * PageWidth * 4, PageWidth * 4);
            }
            return new TpwImage { Width = PageWidth, Height = PageHeight, Rgba = rgba, Source = $"{sheet.Source} page {pageIndex}" };
        }

        /// <summary>Every texture sheet in the archive, in entry order.</summary>
        public static List<(GazEntry Entry, TpwImage Image)> DecodeAll(GazArchive gaz)
        {
            var outp = new List<(GazEntry, TpwImage)>();
            if (gaz == null) return outp;
            foreach (var e in gaz.Entries)
            {
                if (!LooksLikeTexturePage(e)) continue;
                if (TryDecode(gaz.Read(e), out var img, out _))
                {
                    img.Source = $"entry #{e.Index}";
                    outp.Add((e, img));
                }
            }
            return outp;
        }
    }
}
