using System;

namespace TPW.Data
{
    /// <summary>What a decoded frame's bitstream said about itself, for self-tests and diagnostics. The picture is
    /// the product; these are the two numbers that can only come out right if every symbol was read in step.</summary>
    public struct MdecFrameInfo
    {
        public int Version;
        public int QScale;
        /// <summary>Header word 0: the size, in 32-bit words, of the frame as the MDEC would receive it.</summary>
        public int HeaderWords;
        /// <summary>MDEC codes actually decoded: one DC, one per AC coefficient and one end-of-block, per block.</summary>
        public int CodeCount;
        public int BitsUsed;
        public int BitsAvailable;
        /// <summary>True when the ten bits after the last macroblock are the encoder's 0111111111 end-of-frame
        /// padding. A decoder that has lost sync anywhere in the frame will not land on them.</summary>
        public bool TailPaddingOk;
        /// <summary>True when HeaderWords equals the decoded code count halved and rounded up to 32 words, which is
        /// how the encoder derived it. Measured true on every frame of every movie on the disc.</summary>
        public bool HeaderWordsOk;
    }

    /// <summary>PlayStation MDEC frame decoder for the console-standard "BS" bitstream, version 2, which is what
    /// every frame of every .STR on the TPW disc carries (checked: first, second, middle and last frame of all
    /// eight movies — magic 3800h, version 2, quantisation scale 1..4).
    ///
    /// ⭐ THE FORMAT IS A DOCUMENTED STANDARD, NOT A GAME INVENTION, so nothing here was tuned by eye. Every
    /// convention was taken from the documentation and then CROSS-CHECKED between independent sources before a
    /// single pixel was looked at:
    ///   • the AC variable-length table is MPEG-1 table B.5; ffmpeg's mpeg12data.c and jpsxdec's explicit bit
    ///     strings were parsed programmatically and agree on all 111 codes (escape 000001, end-of-block 10);
    ///   • macroblocks are COLUMN-MAJOR (down the first 16-pixel column, then the next), per psx-spx
    ///     ("vertically arranged") and ffmpeg's mdec.c loop order; blocks within one are Cr, Cb, Y1, Y2, Y3, Y4;
    ///   • the bitstream is 16-bit little-endian halfwords read from bit 15 down (psx-spx, jpsxdec);
    ///   • the DC is a plain signed 10-bit value in v2, scaled by the table's first entry (2) and NOT by the
    ///     frame's quantisation scale nor divided by 8 (psx-spx rl_decode_block, jpsxdec MdecDecoder_int).
    ///
    /// ⚠ THE QUANTISATION TABLE HAS AN INDEXING TRAP that gives a nearly-right picture if you get it wrong. The
    /// hardware multiplies the k-th coefficient IN SCAN ORDER by the k-th uploaded byte (psx-spx: qt[k]), while
    /// ffmpeg and jpsxdec index the MPEG raster matrix by RASTER position. Both are right only if the game uploads
    /// the raster matrix permuted into zig-zag order, and it does: /TPW.BIN holds exactly that 64-byte sequence
    /// (02 10 10 13 10 13 16 16 ... 45 45 53) twice — luma and chroma — followed by the standard IDCT scale table,
    /// which is libpress's reset data. The raster-ordered sequence appears nowhere on the disc. So the weight for a
    /// coefficient is the MPEG matrix at that coefficient's raster position, exactly as implemented below.
    ///
    /// ⭐ THE ORACLE WAS A PIXEL-LEVEL REFERENCE, NOT A PLAUSIBLE PICTURE. A DCT that is close enough gives
    /// recognisable shapes in roughly the right colours, so "it looks like the Bullfrog logo" proves too little.
    /// The Python prototype this was ported from was compared against ffmpeg's independent decode of the same
    /// frames PLANE BY PLANE, before colour conversion: Y, Cb and Cr agree to within 3 levels everywhere (mean
    /// about 0.2), and the frame is consumed to within 48 bits of its end landing exactly on the encoder's
    /// 0111111111 padding. Then the four movies master remembers (Bullfrog kite, bounce-house, mirror, jungle)
    /// were looked at.</summary>
    public static class Mdec
    {
        public const int HeaderBytes = 8;
        public const ushort Magic = 0x3800;
        public const int MaxDimension = 1024;

        /// <summary>Scan (zig-zag) index → raster index. psx-spx "zagzig", ffmpeg ff_zigzag_direct.</summary>
        static readonly byte[] ZigZag =
        {
             0,  1,  8, 16,  9,  2,  3, 10,
            17, 24, 32, 25, 18, 11,  4,  5,
            12, 19, 26, 33, 40, 48, 41, 34,
            27, 20, 13,  6,  7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36,
            29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46,
            53, 60, 61, 54, 47, 55, 62, 63,
        };

        /// <summary>Quantisation weights in RASTER order: the MPEG-1 default intra matrix with the DC entry set to 2.
        /// This is what TPW.BIN's zig-zag-ordered upload amounts to per frequency (see the class comment).</summary>
        static readonly byte[] Quant =
        {
             2, 16, 19, 22, 26, 27, 29, 34,
            16, 16, 22, 24, 27, 29, 34, 37,
            19, 22, 26, 27, 29, 34, 34, 38,
            22, 22, 26, 27, 29, 34, 37, 40,
            22, 26, 27, 29, 32, 35, 40, 48,
            26, 27, 29, 32, 35, 40, 48, 58,
            26, 27, 29, 34, 38, 46, 56, 69,
            27, 29, 35, 38, 46, 56, 69, 83,
        };

        // MPEG-1 table B.5 (run, level) codes, sign bit follows the code. Cross-checked: ffmpeg mpeg12data.c == jpsxdec ZeroRunLengthAcLookup_STR.
        static readonly (string Code, byte Run, byte Level)[] AcCodes =
        {
            ("11", 0, 1),
            ("011", 1, 1),
            ("0100", 0, 2),
            ("0101", 2, 1),
            ("00101", 0, 3),
            ("00110", 4, 1),
            ("00111", 3, 1),
            ("000100", 7, 1),
            ("000101", 6, 1),
            ("000110", 1, 2),
            ("000111", 5, 1),
            ("0000100", 2, 2),
            ("0000101", 9, 1),
            ("0000110", 0, 4),
            ("0000111", 8, 1),
            ("00100000", 13, 1),
            ("00100001", 0, 6),
            ("00100010", 12, 1),
            ("00100011", 11, 1),
            ("00100100", 3, 2),
            ("00100101", 1, 3),
            ("00100110", 0, 5),
            ("00100111", 10, 1),
            ("0000001000", 16, 1),
            ("0000001001", 5, 2),
            ("0000001010", 0, 7),
            ("0000001011", 2, 3),
            ("0000001100", 1, 4),
            ("0000001101", 15, 1),
            ("0000001110", 14, 1),
            ("0000001111", 4, 2),
            ("000000010000", 0, 11),
            ("000000010001", 8, 2),
            ("000000010010", 4, 3),
            ("000000010011", 0, 10),
            ("000000010100", 2, 4),
            ("000000010101", 7, 2),
            ("000000010110", 21, 1),
            ("000000010111", 20, 1),
            ("000000011000", 0, 9),
            ("000000011001", 19, 1),
            ("000000011010", 18, 1),
            ("000000011011", 1, 5),
            ("000000011100", 3, 3),
            ("000000011101", 0, 8),
            ("000000011110", 6, 2),
            ("000000011111", 17, 1),
            ("0000000010000", 10, 2),
            ("0000000010001", 9, 2),
            ("0000000010010", 5, 3),
            ("0000000010011", 3, 4),
            ("0000000010100", 2, 5),
            ("0000000010101", 1, 7),
            ("0000000010110", 1, 6),
            ("0000000010111", 0, 15),
            ("0000000011000", 0, 14),
            ("0000000011001", 0, 13),
            ("0000000011010", 0, 12),
            ("0000000011011", 26, 1),
            ("0000000011100", 25, 1),
            ("0000000011101", 24, 1),
            ("0000000011110", 23, 1),
            ("0000000011111", 22, 1),
            ("00000000010000", 0, 31),
            ("00000000010001", 0, 30),
            ("00000000010010", 0, 29),
            ("00000000010011", 0, 28),
            ("00000000010100", 0, 27),
            ("00000000010101", 0, 26),
            ("00000000010110", 0, 25),
            ("00000000010111", 0, 24),
            ("00000000011000", 0, 23),
            ("00000000011001", 0, 22),
            ("00000000011010", 0, 21),
            ("00000000011011", 0, 20),
            ("00000000011100", 0, 19),
            ("00000000011101", 0, 18),
            ("00000000011110", 0, 17),
            ("00000000011111", 0, 16),
            ("000000000010000", 0, 40),
            ("000000000010001", 0, 39),
            ("000000000010010", 0, 38),
            ("000000000010011", 0, 37),
            ("000000000010100", 0, 36),
            ("000000000010101", 0, 35),
            ("000000000010110", 0, 34),
            ("000000000010111", 0, 33),
            ("000000000011000", 0, 32),
            ("000000000011001", 1, 14),
            ("000000000011010", 1, 13),
            ("000000000011011", 1, 12),
            ("000000000011100", 1, 11),
            ("000000000011101", 1, 10),
            ("000000000011110", 1, 9),
            ("000000000011111", 1, 8),
            ("0000000000010000", 1, 18),
            ("0000000000010001", 1, 17),
            ("0000000000010010", 1, 16),
            ("0000000000010011", 1, 15),
            ("0000000000010100", 6, 3),
            ("0000000000010101", 16, 2),
            ("0000000000010110", 15, 2),
            ("0000000000010111", 14, 2),
            ("0000000000011000", 13, 2),
            ("0000000000011001", 12, 2),
            ("0000000000011010", 11, 2),
            ("0000000000011011", 31, 1),
            ("0000000000011100", 30, 1),
            ("0000000000011101", 29, 1),
            ("0000000000011110", 28, 1),
            ("0000000000011111", 27, 1),
        };
        const string EscapeCode = "000001";
        const string EndOfBlockCode = "10";

        // VLC lookup keyed by the next 16 bits: (length << 24) | (run << 16) | (level << 8) | kind; 0 = no such code.
        const int KindCoefficient = 1, KindEscape = 2, KindEndOfBlock = 3;
        static readonly int[] Vlc = BuildVlc();

        static int[] BuildVlc()
        {
            var t = new int[1 << 16];
            void Add(string code, int run, int level, int kind)
            {
                int len = code.Length;
                int prefix = Convert.ToInt32(code, 2) << (16 - len);
                int span = 1 << (16 - len);
                int entry = (len << 24) | (run << 16) | (level << 8) | kind;
                for (int i = 0; i < span; i++)
                {
                    if (t[prefix + i] != 0) throw new InvalidOperationException($"MDEC VLC table is not prefix-free at {code}");
                    t[prefix + i] = entry;
                }
            }
            foreach (var (code, run, level) in AcCodes) Add(code, run, level, KindCoefficient);
            Add(EscapeCode, 0, 0, KindEscape);
            Add(EndOfBlockCode, 0, 0, KindEndOfBlock);
            return t;
        }

        /// <summary>Orthonormal IDCT basis, Cos[k * 8 + x] = a(k) cos((2x+1) k π / 16), a(0) = √⅛, a(k) = ½. This is
        /// the console's MDEC(3) scale table divided by 65536 (jpsxdec PsxMdecIDCT), i.e. the standard JPEG IDCT.</summary>
        static readonly float[] Cos = BuildCos();

        static float[] BuildCos()
        {
            var c = new float[64];
            for (int k = 0; k < 8; k++)
                for (int x = 0; x < 8; x++)
                    c[k * 8 + x] = (float)((k == 0 ? Math.Sqrt(0.125) : 0.5) * Math.Cos((2 * x + 1) * k * Math.PI / 16));
            return c;
        }

        /// <summary>Decode one frame's bitstream (as StrVideo hands it over) into an RGBA image.</summary>
        public static bool TryDecodeFrame(byte[] bitstream, int width, int height, out TpwImage img, out string error)
            => TryDecodeFrame(bitstream, width, height, null, out img, out error, out _);

        /// <summary>As above, reusing <paramref name="reuse"/> as the output when it already has the right size, so a
        /// player decoding at frame rate allocates nothing per frame. <paramref name="info"/> reports the structural
        /// checks whether or not the frame decoded.</summary>
        public static bool TryDecodeFrame(byte[] bitstream, int width, int height, TpwImage reuse,
                                          out TpwImage img, out string error, out MdecFrameInfo info)
        {
            img = null; error = null; info = default;
            if (bitstream == null || bitstream.Length < HeaderBytes) { error = "shorter than the 8-byte BS header"; return false; }
            if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension)
            { error = $"unreasonable frame size {width}x{height}"; return false; }

            info.HeaderWords = BitConverter.ToUInt16(bitstream, 0);
            int magic = BitConverter.ToUInt16(bitstream, 2);
            info.QScale = BitConverter.ToUInt16(bitstream, 4);
            info.Version = BitConverter.ToUInt16(bitstream, 6);
            info.BitsAvailable = (bitstream.Length - HeaderBytes) * 8;
            if (magic != Magic) { error = $"BS magic {magic:x4}h is not {Magic:x4}h"; return false; }
            if (info.Version != 2)
            { error = $"BS version {info.Version}: only version 2 is implemented (every frame on the TPW disc is v2)"; return false; }
            if (info.QScale > 63) { error = $"quantisation scale {info.QScale} exceeds the 6-bit range"; return false; }

            var d = t_decoder ??= new FrameDecoder();
            if (!d.Decode(bitstream, width, height, info.QScale, ref info, out error)) return false;

            if (reuse != null && reuse.Width == width && reuse.Height == height && reuse.Rgba != null && reuse.Rgba.Length == width * height * 4)
                img = reuse;
            else
                img = new TpwImage { Width = width, Height = height, Rgba = new byte[width * height * 4] };
            img.Source = $"MDEC BS v{info.Version} q={info.QScale}";
            d.ToRgba(width, height, img.Rgba);
            return true;
        }

        [ThreadStatic] static FrameDecoder t_decoder;

        /// <summary>Holds the scratch planes between frames so decoding at frame rate allocates nothing.</summary>
        sealed class FrameDecoder
        {
            // bit reader over 16-bit little-endian halfwords, most significant bit first
            byte[] _d; int _pos; int _end; ulong _acc; int _n; int _overrun;
            readonly int[] _coef = new int[64];
            readonly float[] _tmp = new float[64];
            byte[] _y = Array.Empty<byte>(), _cb = Array.Empty<byte>(), _cr = Array.Empty<byte>();
            int _yStride, _cStride;
            int _codes;

            void Fill(int need)
            {
                while (_n < need)
                {
                    uint w;
                    if (_pos + 1 < _end) w = (uint)(_d[_pos] | (_d[_pos + 1] << 8));
                    else { w = 0; _overrun++; }
                    _pos += 2;
                    _acc = (_acc << 16) | w;
                    _n += 16;
                }
            }
            int Peek(int k) { if (_n < k) Fill(k); return (int)((_acc >> (_n - k)) & ((1u << k) - 1)); }
            int Read(int k) { int v = Peek(k); _n -= k; return v; }
            int ReadSigned(int k) { int v = Read(k); return (v << (32 - k)) >> (32 - k); }
            int BitsUsed => _pos * 8 - _n;

            public bool Decode(byte[] bitstream, int width, int height, int qscale, ref MdecFrameInfo info, out string error)
            {
                error = null;
                _d = bitstream; _pos = HeaderBytes; _end = bitstream.Length; _acc = 0; _n = 0; _overrun = 0; _codes = 0;
                int mbw = (width + 15) / 16, mbh = (height + 15) / 16;
                _yStride = mbw * 16; _cStride = mbw * 8;
                if (_y.Length != _yStride * mbh * 16) { _y = new byte[_yStride * mbh * 16]; _cb = new byte[_cStride * mbh * 8]; _cr = new byte[_cStride * mbh * 8]; }

                for (int mbx = 0; mbx < mbw; mbx++)
                {
                    for (int mby = 0; mby < mbh; mby++)
                    {
                        int c = mby * 8 * _cStride + mbx * 8;
                        int y = mby * 16 * _yStride + mbx * 16;
                        if (!DecodeBlock(qscale, _cr, c, _cStride, out error) ||
                            !DecodeBlock(qscale, _cb, c, _cStride, out error) ||
                            !DecodeBlock(qscale, _y, y, _yStride, out error) ||
                            !DecodeBlock(qscale, _y, y + 8, _yStride, out error) ||
                            !DecodeBlock(qscale, _y, y + 8 * _yStride, _yStride, out error) ||
                            !DecodeBlock(qscale, _y, y + 8 * _yStride + 8, _yStride, out error))
                        {
                            error = $"macroblock ({mbx},{mby}) of {mbw}x{mbh}: {error}";
                            info.CodeCount = _codes; info.BitsUsed = BitsUsed;
                            return false;
                        }
                        // The reader pads with zero halfwords past the end; a frame that needs more than a couple of
                        // those has not merely finished early, it has been misread somewhere and is now decoding air.
                        if (_overrun > 2)
                        {
                            error = $"bitstream ran out {_overrun * 16} bits short at macroblock ({mbx},{mby}) of {mbw}x{mbh}";
                            info.CodeCount = _codes; info.BitsUsed = BitsUsed;
                            return false;
                        }
                    }
                }
                info.CodeCount = _codes;
                info.BitsUsed = BitsUsed;
                info.TailPaddingOk = Peek(10) == 0x1FF;
                int words = ((_codes + 1) / 2 + 31) / 32 * 32;
                info.HeaderWordsOk = words == info.HeaderWords;
                return true;
            }

            /// <summary>Read one 8x8 block, dequantise, inverse-transform and store it as unsigned samples (+128).</summary>
            bool DecodeBlock(int qscale, byte[] plane, int offset, int stride, out string error)
            {
                error = null;
                var coef = _coef;
                Array.Clear(coef, 0, 64);
                int dc = ReadSigned(10);
                _codes++;
                coef[0] = Math.Clamp(dc * Quant[0], -1024, 1023);     // DC: table weight only, no scale, no /8
                int k = 0, acCount = 0;
                for (;;)
                {
                    int e = Vlc[Peek(16)];
                    if (e == 0) { error = $"no such AC code {Convert.ToString(Peek(16), 2).PadLeft(16, '0')} at bit {BitsUsed}"; return false; }
                    _n -= e >> 24;                                   // consume the code
                    _codes++;
                    int kind = e & 0xFF;
                    if (kind == KindEndOfBlock) break;
                    int run, level;
                    if (kind == KindEscape) { run = Read(6); level = ReadSigned(10); }
                    else
                    {
                        run = (e >> 16) & 0xFF; level = (e >> 8) & 0xFF;
                        if (Read(1) != 0) level = -level;
                    }
                    k += run + 1;
                    if (k > 63) { error = $"run carries coefficient index to {k} at bit {BitsUsed}"; return false; }
                    int v, pos;
                    if (qscale == 0) { pos = k; v = level * 2; }      // MDEC's unscaled mode: no table, no zig-zag (psx-spx)
                    else { pos = ZigZag[k]; v = (level * Quant[pos] * qscale + 4) >> 3; }
                    coef[pos] = Math.Clamp(v, -1024, 1023);
                    acCount++;
                }
                Idct(coef, acCount, plane, offset, stride);
                return true;
            }

            /// <summary>Separable orthonormal IDCT. coef[v*8+u]: u horizontal, v vertical frequency. Output samples
            /// are clamped to 0..255 after the +128 level shift.</summary>
            void Idct(int[] coef, int acCount, byte[] plane, int offset, int stride)
            {
                if (acCount == 0)
                {
                    // flat block: every sample is the DC term a(0)² · coef[0] = coef[0] / 8
                    int p = Math.Clamp((int)Math.Floor(coef[0] / 8.0 + 128.5), 0, 255);
                    for (int yy = 0; yy < 8; yy++)
                    {
                        int o = offset + yy * stride;
                        for (int x = 0; x < 8; x++) plane[o + x] = (byte)p;
                    }
                    return;
                }
                var tmp = _tmp;
                var cos = Cos;
                // pass 1: vertical — for each column u, tmp[y*8+u] = Σv cos[v*8+y] · coef[v*8+u]
                for (int u = 0; u < 8; u++)
                {
                    int c0 = coef[u], c1 = coef[8 + u], c2 = coef[16 + u], c3 = coef[24 + u],
                        c4 = coef[32 + u], c5 = coef[40 + u], c6 = coef[48 + u], c7 = coef[56 + u];
                    if ((c1 | c2 | c3 | c4 | c5 | c6 | c7) == 0)
                    {
                        float f = c0 * cos[0];
                        for (int y = 0; y < 8; y++) tmp[y * 8 + u] = f;
                        continue;
                    }
                    for (int y = 0; y < 8; y++)
                        tmp[y * 8 + u] = c0 * cos[y] + c1 * cos[8 + y] + c2 * cos[16 + y] + c3 * cos[24 + y]
                                       + c4 * cos[32 + y] + c5 * cos[40 + y] + c6 * cos[48 + y] + c7 * cos[56 + y];
                }
                // pass 2: horizontal — out[y][x] = Σu cos[u*8+x] · tmp[y*8+u]
                for (int y = 0; y < 8; y++)
                {
                    int t = y * 8, o = offset + y * stride;
                    float t0 = tmp[t], t1 = tmp[t + 1], t2 = tmp[t + 2], t3 = tmp[t + 3],
                          t4 = tmp[t + 4], t5 = tmp[t + 5], t6 = tmp[t + 6], t7 = tmp[t + 7];
                    for (int x = 0; x < 8; x++)
                    {
                        float v = t0 * cos[x] + t1 * cos[8 + x] + t2 * cos[16 + x] + t3 * cos[24 + x]
                                + t4 * cos[32 + x] + t5 * cos[40 + x] + t6 * cos[48 + x] + t7 * cos[56 + x];
                        int p = (int)(v + 128.5f + 4096f) - 4096;    // floor(v + 128.5) without a call, v ≥ -4096 always
                        plane[o + x] = (byte)(p < 0 ? 0 : p > 255 ? 255 : p);
                    }
                }
            }

            /// <summary>YCbCr → RGBA with each chroma sample covering its 2x2 pixels, as the console does (psx-spx
            /// yuv_to_rgb: R = Y + 1.402 Cr, G = Y − 0.3437 Cb − 0.7143 Cr, B = Y + 1.772 Cb). 16.16 fixed point.</summary>
            public void ToRgba(int width, int height, byte[] rgba)
            {
                for (int y = 0; y < height; y++)
                {
                    int yRow = y * _yStride, cRow = (y >> 1) * _cStride, o = y * width * 4;
                    for (int x = 0; x < width; x++, o += 4)
                        {
                        int ci = cRow + (x >> 1);
                        int cr = _cr[ci] - 128, cb = _cb[ci] - 128;
                        int luma = _y[yRow + x];
                        int r = luma + ((91881 * cr + 32768) >> 16);
                        int g = luma + ((-22525 * cb - 46812 * cr + 32768) >> 16);
                        int b = luma + ((116130 * cb + 32768) >> 16);
                        rgba[o] = (byte)(r < 0 ? 0 : r > 255 ? 255 : r);
                        rgba[o + 1] = (byte)(g < 0 ? 0 : g > 255 ? 255 : g);
                        rgba[o + 2] = (byte)(b < 0 ? 0 : b > 255 ? 255 : b);
                        rgba[o + 3] = 255;
                    }
                }
            }
        }
    }
}
