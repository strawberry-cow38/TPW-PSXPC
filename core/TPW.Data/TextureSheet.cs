using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>One sprite of a texture sheet: a rectangle of one page, drawn with one palette.</summary>
    public readonly struct SheetSprite
    {
        /// <summary>The GPU texpage word it is drawn with: page x in bits 0-3 (× 64 halfwords), page y in bit 4
        /// (× 256), colour depth in bits 7-8 (0 = 4-bit, 1 = 8-bit).</summary>
        public readonly ushort TPage;
        /// <summary>The GPU CLUT word: palette x in bits 0-5 (× 16 halfwords), palette y in bits 6-14.</summary>
        public readonly ushort Clut;
        public readonly sbyte OffsetX, OffsetY;
        public readonly byte W, H, U, V;
        public readonly byte Flags, Extra;

        public SheetSprite(ushort tpage, ushort clut, sbyte ox, sbyte oy, byte w, byte h, byte u, byte v, byte flags, byte extra)
        { TPage = tpage; Clut = clut; OffsetX = ox; OffsetY = oy; W = w; H = h; U = u; V = v; Flags = flags; Extra = extra; }

        public bool EightBit => ((TPage >> 7) & 3) == 1;
        public int PageX => (TPage & 0x0F) * 64;          // VRAM halfwords
        public int PageY => ((TPage >> 4) & 1) * 256;
        public int ClutX => (Clut & 0x3F) * 16;           // VRAM halfwords
        public int ClutY => (Clut >> 6) & 0x1FF;
    }

    /// <summary>A texture sheet: one or more 256x256 texture pages, the palettes that colour them, and a table
    /// of the sprites cut from them. 29 entries in FOLIO.GAZ, 12 stored raw and 17 compressed.
    ///
    /// ⭐ LAYOUT, from the game's texture loader (0x800280F8) and checked on every sheet:
    /// <code>
    ///   +0x00 u16 sprites      +0x02 u16 pages (= cols × rows)   +0x04 u16 texpage of the top-left page
    ///   +0x06 u16 cols         +0x08 u16 rows                    +0x0A u16 free rectangles
    ///   +0x0C u16 compressed   +0x0E u16 0
    ///   +0x10 sprites × 12 bytes, then free rectangles × 8 bytes, then the pixels
    /// </code>
    /// ⚠ The pixels do NOT start at a fixed offset. "The header is 0x54 bytes" was true of the 12 raw sheets only
    /// because each happens to have 3 sprites and 4 free rectangles (0x10 + 36 + 32 = 0x54). Every compressed
    /// sheet has hundreds of sprites, which is why UNPAK run from +0x54 produced rubbish on all 13 of them.
    ///
    /// Raw sheets are one VRAM image, cols×64 halfwords wide and rows×256 tall. COMPRESSED sheets are cut into
    /// 64x64-halfword blocks (0x2000 bytes: a quarter of a page, 256×64 texels), four per page, uploaded one at a
    /// time: block 0 stored RAW (the loader's buffer is that block's scratch space), then each later block as
    /// its own headerless UNPAK stream, one straight after another.
    ///
    /// ✅ CHECKED THREE WAYS. Every block of all 17 compressed sheets expands to exactly 0x2000 bytes and the
    /// last stream ends exactly on the entry's last byte, which a wrong start offset or a wrong block count
    /// cannot do. The raw sheets' pixels are exactly pages × 32 KB. And against tinyclaw's hashes of live VRAM,
    /// decoded blocks match hash-exactly AT THE POSITION THIS HEADER PREDICTS: 55 of the 58 non-blank blocks in a
    /// running park (17 before this), 33 of 36 at cold boot. The 6 left are nowhere on the disc; runtime writes.
    ///
    /// ⭐⭐ AND THE PALETTES ARE INSIDE THE SHEETS. Every sprite names its CLUT in VRAM, and for 4,409 of 4,409
    /// sprites across all 29 sheets that address lies inside the sprite's own sheet. A random CLUT word would land
    /// inside a sheet 6-40% of the time depending on its size. So a sheet carries its colours in its own pixels,
    /// and the sprite table says which palette colours which rectangle: the "(rect -> clut) per page" mapping
    /// that the whole palette search was missing. Rendered that way, the front-end sheet's flags come out in
    /// their true colours.</summary>
    public sealed class TextureSheet
    {
        public const int HeaderBytes = 0x10;
        public const int SpriteBytes = 12;
        public const int FreeRectBytes = 8;
        public const int BlockBytes = 0x2000;       // 64x64 VRAM halfwords
        public const int PageBytes = 0x8000;        // 256x256 texels at 4bpp
        public const int PageTexels = 256;

        public int SpriteCount, PageCount, TPage, Columns, Rows, FreeRectCount;
        public bool Compressed;
        public int DataOffset;
        public readonly List<SheetSprite> Sprites = new();
        /// <summary>Unused space the packer left, in sheet texels: (x, y, width, height).</summary>
        public readonly List<(int X, int Y, int W, int H)> FreeRects = new();
        /// <summary>The sheet as VRAM would hold it: row-major, <see cref="StrideBytes"/> per row.</summary>
        public byte[] Pixels = Array.Empty<byte>();

        public int StrideBytes => Columns * 128;
        public int WidthTexels => Columns * PageTexels;      // at 4bpp
        public int HeightTexels => Rows * PageTexels;
        /// <summary>Where the sheet sits in VRAM, in halfwords.</summary>
        public int VramX => (TPage & 0x0F) * 64;
        public int VramY => ((TPage >> 4) & 1) * 256;

        /// <summary>A VRAM halfword, in VRAM coordinates. The caller checks it is inside the sheet.</summary>
        public ushort Halfword(int vramX, int vramY)
        {
            int o = (vramY - VramY) * StrideBytes + (vramX - VramX) * 2;
            return (ushort)(Pixels[o] | (Pixels[o + 1] << 8));
        }

        public bool Contains(int vramX, int vramY) =>
            vramX >= VramX && vramX < VramX + Columns * 64 && vramY >= VramY && vramY < VramY + Rows * 256;

        public static bool TryParse(byte[] d, out TextureSheet sheet, out string error)
        {
            sheet = null; error = null;
            if (d == null || d.Length < HeaderBytes) { error = "too short for a sheet header"; return false; }
            int U16(int o) => BitConverter.ToUInt16(d, o);
            var s = new TextureSheet
            {
                SpriteCount = U16(0), PageCount = U16(2), TPage = U16(4), Columns = U16(6), Rows = U16(8),
                FreeRectCount = U16(0x0A),
            };
            int compressed = U16(0x0C);
            if (U16(0x0E) != 0 || compressed > 1) { error = "not a sheet header"; return false; }
            if (s.PageCount == 0 || s.PageCount > 16 || s.Columns * s.Rows != s.PageCount)
            { error = $"{s.Columns}x{s.Rows} pages does not make {s.PageCount}"; return false; }
            if (s.TPage >= 32) { error = $"texpage {s.TPage} is off the texture area"; return false; }
            s.Compressed = compressed == 1;
            s.DataOffset = HeaderBytes + s.SpriteCount * SpriteBytes + s.FreeRectCount * FreeRectBytes;
            if (s.DataOffset + BlockBytes > d.Length) { error = "sprite and free-rect tables run past the entry"; return false; }

            for (int i = 0; i < s.SpriteCount; i++)
            {
                int o = HeaderBytes + i * SpriteBytes;
                s.Sprites.Add(new SheetSprite(BitConverter.ToUInt16(d, o), BitConverter.ToUInt16(d, o + 2),
                    (sbyte)d[o + 4], (sbyte)d[o + 5], d[o + 6], d[o + 7], d[o + 8], d[o + 9], d[o + 10], d[o + 11]));
            }
            for (int i = 0; i < s.FreeRectCount; i++)
            {
                int o = HeaderBytes + s.SpriteCount * SpriteBytes + i * FreeRectBytes;
                s.FreeRects.Add((U16(o), U16(o + 2), U16(o + 4), U16(o + 6)));
            }

            int size = s.PageCount * PageBytes;
            s.Pixels = new byte[size];
            if (!s.Compressed)
            {
                // ⭐ The size IS the check: pixels must fill the rest of the entry exactly.
                if (s.DataOffset + size != d.Length)
                { error = $"raw pixels would end at {s.DataOffset + size:n0}, the entry is {d.Length:n0}"; return false; }
                Buffer.BlockCopy(d, s.DataOffset, s.Pixels, 0, size);
            }
            else if (!s.TryExpandBlocks(d, out error)) return false;

            sheet = s;
            return true;
        }

        bool TryExpandBlocks(byte[] d, out string error)
        {
            error = null;
            int blocks = PageCount * 4;
            var block = new byte[BlockBytes];
            int at = DataOffset;
            for (int i = 0; i < blocks; i++)
            {
                if (i == 0) { Buffer.BlockCopy(d, at, block, 0, BlockBytes); at += BlockBytes; }
                else
                {
                    if (!Unpak.TryDecompressBlock(d, at, block, out int produced, out int consumed, out string e))
                    { error = $"block {i}: {e}"; return false; }
                    if (produced != BlockBytes) { error = $"block {i} expanded to {produced:n0} bytes, not {BlockBytes:n0}"; return false; }
                    at += consumed;
                }
                // The loader's placement (0x800280F8): page q = i/4 across `cols`, and the block is strip i%4.
                int q = i / 4;
                int x = (q % Columns) * 128, y = (q / Columns) * PageTexels + (i & 3) * 64;
                for (int r = 0; r < 64; r++) Buffer.BlockCopy(block, r * 128, Pixels, (y + r) * StrideBytes + x, 128);
            }
            // ⭐ The last stream must end exactly on the entry's last byte. A wrong start or count cannot.
            if (at != d.Length) { error = $"the {blocks} blocks end at {at:n0}, the entry is {d.Length:n0}"; return false; }
            return true;
        }

        /// <summary>The 64x64-halfword block at index i as VRAM holds it, for comparing against a VRAM dump.</summary>
        public byte[] Block(int i, out int vramX, out int vramY)
        {
            int q = i / 4;
            int x = (q % Columns) * 128, y = (q / Columns) * PageTexels + (i & 3) * 64;
            vramX = VramX + x / 2; vramY = VramY + y;
            var b = new byte[BlockBytes];
            for (int r = 0; r < 64; r++) Buffer.BlockCopy(Pixels, (y + r) * StrideBytes + x, b, r * 128, 128);
            return b;
        }

        /// <summary>How many sprites name a palette that lies inside this sheet. On the disc, all of them.</summary>
        public int SpritesWithPaletteInside()
        {
            int n = 0;
            foreach (var sp in Sprites)
                if (Contains(sp.ClutX, sp.ClutY) && Contains(sp.ClutX + (sp.EightBit ? 255 : 15), sp.ClutY)) n++;
            return n;
        }

        /// <summary>Every sprite drawn with its own palette, at its place on the sheet. Space no sprite covers is
        /// left dark, and palette colour 0 is transparent, as the GPU treats it.</summary>
        public TpwImage RenderSprites(string source = "")
        {
            int w = WidthTexels, h = HeightTexels;
            var rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++) { rgba[i * 4] = 24; rgba[i * 4 + 1] = 16; rgba[i * 4 + 2] = 28; rgba[i * 4 + 3] = 255; }

            foreach (var sp in Sprites)
            {
                int n = sp.EightBit ? 256 : 16;
                if (!Contains(sp.ClutX, sp.ClutY) || !Contains(sp.ClutX + n - 1, sp.ClutY) || !Contains(sp.PageX, sp.PageY)) continue;
                var pal = new ushort[n];
                for (int k = 0; k < n; k++) pal[k] = Halfword(sp.ClutX + k, sp.ClutY);

                int pageCol = (sp.PageX - VramX) / 64, pageRow = (sp.PageY - VramY) / 256;
                for (int yy = sp.V; yy < sp.V + sp.H && yy < PageTexels; yy++)
                {
                    int row = (pageRow * PageTexels + yy) * StrideBytes + pageCol * 128;
                    for (int xx = sp.U; xx < sp.U + sp.W && xx < PageTexels; xx++)
                    {
                        int ix;
                        if (sp.EightBit)
                        {
                            if (xx >= 128) continue;          // an 8-bit page is 128 texels wide
                            ix = Pixels[row + xx];
                        }
                        else
                        {
                            int b = Pixels[row + (xx >> 1)];
                            ix = (xx & 1) == 0 ? b & 0x0F : b >> 4;
                        }
                        ushort c = pal[ix];
                        if (c == 0) continue;                 // transparent, as the GPU treats it
                        int X = pageCol * PageTexels + (sp.EightBit ? xx * 2 : xx);
                        int o = ((pageRow * PageTexels + yy) * w + X) * 4;
                        byte r = (byte)((c & 31) << 3), g = (byte)(((c >> 5) & 31) << 3), bl = (byte)(((c >> 10) & 31) << 3);
                        rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = bl; rgba[o + 3] = 255;
                        if (sp.EightBit && X + 1 < w) { rgba[o + 4] = r; rgba[o + 5] = g; rgba[o + 6] = bl; rgba[o + 7] = 255; }
                    }
                }
            }
            return new TpwImage { Width = w, Height = h, Rgba = rgba, Source = source };
        }

        /// <summary>Every sheet in the archive. The header rule is strict enough to find them without a size filter:
        /// raw ones must fill their entry exactly and compressed ones must expand to exactly its last byte.</summary>
        public static List<(GazEntry Entry, TextureSheet Sheet)> FindAll(GazArchive gaz)
        {
            var outp = new List<(GazEntry, TextureSheet)>();
            if (gaz == null) return outp;
            foreach (var e in gaz.Entries)
            {
                if (e.Size < HeaderBytes + BlockBytes) continue;
                if (TryParse(gaz.Read(e), out var s, out _)) outp.Add((e, s));
            }
            return outp;
        }
    }
}
