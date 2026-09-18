using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>The game's texture pages: a short header followed by a raw block of PlayStation VRAM.
    ///
    /// ⭐ `0x54` bytes of header, then **512x512 texels at 4 BITS PER PIXEL**. Twelve archive entries on the
    /// shipped disc, 131,156 bytes each, and 0x54 + 512*512/2 = 131,156.
    ///
    /// ⚠ NOT 16bpp. fable's report described the destination as a 256-word-wide, 256-row VRAM region at
    /// (512,256), which is the same 131,072 bytes and is true of the upload — but read as 256x256 16-bit
    /// pixels it decodes to coloured noise. 4bpp was the right depth (tinyclaw had already measured resident
    /// textures as 4bpp with separate palettes) and **the width still had to be found by looking**: at
    /// 1024x256 the picture comes out DOUBLED side by side, which is the signature of a width exactly twice
    /// too large, since adjacent rows are similar. At 512x512 it resolves into a single coherent atlas with
    /// legible text — a credits card reading "Gary Liddon / Lead Programmer" sits in the top right of page 1.
    ///
    /// ⚠⚠ THE PIXELS ARE PALETTE INDICES AND THIS CLASS HAS NO PALETTE. The CLUT id is packed into the
    /// drawing commands, so a page on its own cannot know its colours. `TryDecode` renders the index as a
    /// grey level, which is enough to identify content and lay out an atlas and is NOT the real image.
    ///
    /// ⚠ I HAD THESE ENTRIES AND FAILED TO CRACK THEM, AND THE FAILURE IS INSTRUCTIVE. 131,072 is precisely a
    /// 256x256 16-bit page, so the size alone made it look certain. I derived the stride from the data by
    /// row-to-row difference — the right method — and got a flat result, so I recorded them as "not plain
    /// bitmaps". The method was sound; I searched a grid of candidate header offsets and strides that did not
    /// contain the true pair. **A negative from a search is only as strong as the space it covered**, and I
    /// wrote mine down as though it had covered everything.
    ///
    /// ⚠ AND THE CHECK I FIRST WROTE FOR THESE WAS VACUOUS. "12/12 texture pages decoded" passed while the
    /// output was noise, because the only way decoding could fail was a short buffer — so it restated "12
    /// entries are 131,156 bytes long" and called it a decode. A check that cannot fail on wrong pixels must
    /// not be worded as though it validated them.</summary>
    public static class VramTexture
    {
        public const int HeaderBytes = 0x54;
        public const int Width = 512;
        public const int Height = 512;
        public const int BitsPerPixel = 4;
        public const int StrideBytes = Width / 2;
        /// <summary>Exactly the size of a texture-page entry in the archive: 0x54 + 512*512/2.</summary>
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

        /// <summary>Every texture page in the archive, in entry order.</summary>
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
