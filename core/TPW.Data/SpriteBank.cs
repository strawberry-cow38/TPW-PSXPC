using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>The 131,072-byte headerless banks — guest sprites and the like. **This is the format that
    /// actually matters**, and it is not the same shape as the `0x54`-header sheets.
    ///
    /// ⭐ LAYOUT: no header at all, 4bpp, **256 pixels wide**, stored as **16 consecutive 8192-byte blocks**
    /// of 256x64. Because the blocks are contiguous and each holds 64 whole rows, the file is also plain
    /// row-major at 256 wide — the two descriptions coincide here, which is exactly why the same bytes can
    /// match VRAM block-for-block AND render correctly as one tall image.
    ///
    /// ⚠ DO NOT APPLY THE SHEET LAYOUT TO THESE. A `VramTexture` sheet is 1024x256 row-major with a `0x54`
    /// header. Feeding a bank through it, or a sheet through this, produces a shredded image — I assembled a
    /// sheet with this block order once and the credits text came apart into fragments. Both files are
    /// 131KB of 4bpp and the sizes differ by exactly the header, which is the only thing separating them.
    ///
    /// ✅ VRAM PLACEMENT MEASURED, NOT ASSUMED. Entry `0x10C`'s blocks were bound to hardware addresses by
    /// CONTENT HASH — 14 of 16 matched a live capture exactly, at a `0x2000` stride, and tinyclaw reproduced
    /// it independently against their own dump. The two that did not match are blank in that capture, and
    /// their placement below follows the pattern rather than a measurement; they are marked.
    ///
    /// The destination is 512x512 pixels made of two block columns, which is why the matches alternate
    /// between x=768 and x=832 rather than running straight down.</summary>
    public static class SpriteBank
    {
        public const int Bytes = 128 * 1024;          // 131,072, and no header
        public const int BlockBytes = 8192;           // one 64x64-cell VRAM block
        public const int Blocks = Bytes / BlockBytes; // 16
        public const int Width = 256;                 // pixels, at 4bpp
        public const int BlockHeight = 64;
        public const int Height = Blocks * BlockHeight;   // 1024

        /// <summary>True for an entry shaped like a bank. ⚠ Size alone: a bank and a sheet differ by exactly
        /// the sheet's 0x54 header, so this must be checked against the exact byte count, never a range.</summary>
        public static bool LooksLikeBank(GazEntry e) => e != null && e.Size == Bytes;

        /// <summary>Where block <paramref name="block"/> lands in video memory, as (x, y) in PIXELS.
        ///
        /// Derived from the 14 measured matches: four blocks run down x=768, then four down x=832, then the
        /// pair repeats for the lower half. ⚠ Blocks 14 and 15 were blank in the capture, so their positions
        /// follow the pattern and are the only part of this not directly observed.</summary>
        public static (int X, int Y) VramOrigin(int block)
        {
            if (block < 0 || block >= Blocks) throw new ArgumentOutOfRangeException(nameof(block));
            int column = (block / 4) % 2;             // 0 -> x 768, 1 -> x 832
            int half = block / 8;                     // lower half starts at y 256
            return (768 + column * 64, (block % 4) * BlockHeight + half * 256);
        }

        /// <summary>Blocks whose VRAM placement was confirmed by content hash rather than inferred.</summary>
        public static bool PlacementMeasured(int block) => block >= 0 && block < 14;

        /// <summary>Decode the whole bank to RGBA8 as one 256x1024 image, top-left origin.
        ///
        /// ⚠ Pixels are palette INDICES rendered as grey levels. The CLUT is chosen per DRAW, so a bank
        /// cannot know its own colours — see <see cref="Clut"/>. Grey is enough to identify sprites and cut
        /// them out, and is not the real image.</summary>
        public static bool TryDecode(byte[] d, out TpwImage img, out string error)
        {
            img = null; error = null;
            if (d == null || d.Length < Bytes)
            { error = $"need {Bytes:n0} bytes for a sprite bank, have {d?.Length ?? 0:n0}"; return false; }

            var rgba = new byte[Width * Height * 4];
            for (int i = 0; i < Width * Height; i++)
            {
                // Low nibble is the first pixel, the order the hardware reads a 4bpp run in.
                int index = (i & 1) == 0 ? d[i >> 1] & 0x0F : d[i >> 1] >> 4;
                byte g = (byte)(index * 17);          // 15*17 = 255 exactly
                int o = i * 4;
                rgba[o] = g; rgba[o + 1] = g; rgba[o + 2] = g; rgba[o + 3] = 255;
            }
            img = new TpwImage { Width = Width, Height = Height, Rgba = rgba };
            return true;
        }

        /// <summary>One 256x64 block out of a decoded bank.</summary>
        public static TpwImage Block(TpwImage bank, int block)
        {
            if (bank == null || block < 0 || block >= Blocks) return null;
            var rgba = new byte[Width * BlockHeight * 4];
            Buffer.BlockCopy(bank.Rgba, block * Width * BlockHeight * 4, rgba, 0, rgba.Length);
            var (x, y) = VramOrigin(block);
            return new TpwImage
            {
                Width = Width,
                Height = BlockHeight,
                Rgba = rgba,
                Source = $"{bank.Source} block {block} -> vram {x},{y}" + (PlacementMeasured(block) ? "" : " (placement inferred)"),
            };
        }

        /// <summary>Every sprite bank in the archive, in entry order.</summary>
        public static List<(GazEntry Entry, TpwImage Image)> DecodeAll(GazArchive gaz)
        {
            var outp = new List<(GazEntry, TpwImage)>();
            if (gaz == null) return outp;
            foreach (var e in gaz.Entries)
            {
                if (!LooksLikeBank(e)) continue;
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
