using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>XA-ADPCM: the CD drive's own audio format, and the soundtrack of every movie.
    ///
    /// ⚠ NOT THE SAME FORMAT AS <see cref="Vag"/>, THOUGH IT IS THE SAME IDEA, WHICH IS THE TRAP. Both are
    /// 4-bit ADPCM with the same filter coefficients, so reusing the VAG decoder is tempting. The packing is
    /// different in every way that matters: VAG is 16-byte blocks of 28 samples for one channel, while XA is
    /// 128-byte "sound groups" holding EIGHT interleaved 28-sample units whose nibbles are spread across the
    /// whole group, alternating left and right in stereo. Put an XA sector through the VAG decoder and you get
    /// a buffer of the right length full of noise.
    ///
    /// Layout of one Mode 2 Form 2 sector: 18 groups of 128 bytes starting at raw offset 24, then 20 bytes of
    /// padding and the EDC. In a 4-bit group, bytes 4..11 are the eight units' parameters (shift in the low
    /// nibble, filter in bits 4-5), and bytes 0..3 and 12..15 are copies of 4..7 and 8..11. Bytes 16..127 are
    /// 28 rows of 4 bytes. Unit u's sample j is nibble (u &amp; 1) of byte 16 + j*4 + u/2. In stereo the even
    /// units are left and the odd ones right.
    ///
    /// ⭐ THE COPIES ARE THE ORACLE. Every group carries its parameters twice, so a decoder reading from the
    /// wrong offset, or a sector that is not XA at all, fails a check that random bytes pass with probability
    /// 2^-64 per group. It is counted on every group rather than trusted: see <see cref="Decoder.BadCopies"/>.
    ///
    /// ✅ All eight movies on the disc use the same coding byte, 0x01: stereo, 37,800 Hz, 4-bit, no emphasis.</summary>
    public static class XaAudio
    {
        public const int GroupsPerSector = 18;
        public const int GroupBytes = 128;
        public const int SamplesPerUnit = 28;
        /// <summary>Raw offset of the first sound group: 12 sync + 4 header + 8 subheader.</summary>
        public const int FirstGroupOffset = 24;
        /// <summary>Raw offset of the coding-info byte in the Mode 2 subheader.</summary>
        public const int CodingInfoOffset = 19;

        // The SPU's first four filters, /64. XA has two filter bits, so there is no fifth.
        static readonly int[] F0 = { 0, 60, 115, 98 };
        static readonly int[] F1 = { 0, 0, -52, -55 };

        /// <summary>The subheader's coding-info byte: channels, rate and sample width.</summary>
        public readonly struct Coding
        {
            public readonly byte Raw;
            public Coding(byte raw) { Raw = raw; }

            public bool Stereo => (Raw & 0x03) == 1;
            public int Channels => Stereo ? 2 : 1;
            public int SampleRate => ((Raw >> 2) & 0x03) == 0 ? 37800 : 18900;
            public int BitsPerSample => ((Raw >> 4) & 0x03) == 0 ? 4 : 8;
            public bool Emphasis => (Raw & 0x40) != 0;

            /// <summary>False if any field holds one of its reserved values, or the unused top bit is set.</summary>
            public bool IsValid => (Raw & 0x03) < 2 && ((Raw >> 2) & 0x03) < 2 && ((Raw >> 4) & 0x03) < 2 && (Raw & 0x80) == 0;

            /// <summary>How many samples each channel gets from one sector: 2016 for stereo 4-bit.</summary>
            public int SamplesPerChannelPerSector => GroupsPerSector * (BitsPerSample == 4 ? 8 : 4) * SamplesPerUnit / Channels;

            public override string ToString() =>
                $"{(Stereo ? "stereo" : "mono")} {SampleRate:n0} Hz {BitsPerSample}-bit{(Emphasis ? " +emphasis" : "")}";
        }

        public static Coding ReadCoding(byte[] rawSector) => new(rawSector[CodingInfoOffset]);

        /// <summary>Decodes the audio sectors of ONE stream, in order.
        ///
        /// ⚠ ONE INSTANCE PER STREAM, FED IN ORDER. The prediction filter's history runs from each sector into
        /// the next, so a fresh decoder per sector restarts the filter from silence 18.75 times a second. That
        /// produces a click at every boundary, not a failure, so it sounds like a bad recording.</summary>
        public sealed class Decoder
        {
            int _l1, _l2, _r1, _r2;
            readonly short[] _left = new short[SamplesPerUnit];
            readonly short[] _right = new short[SamplesPerUnit];

            /// <summary>Sound groups decoded so far.</summary>
            public int Groups { get; private set; }
            /// <summary>Groups whose two copies of the parameters disagree. Zero on real XA.</summary>
            public int BadCopies { get; private set; }
            /// <summary>Parameter bytes using a reserved value: a shift above 12, or bits 6-7 set.</summary>
            public int ReservedParams { get; private set; }

            /// <summary>Decode one raw 2352-byte sector, appending interleaved samples (L, R, L, R… in stereo).</summary>
            public bool TryDecodeSector(byte[] raw, List<short> interleaved, out string error)
            {
                error = null;
                if (raw == null || raw.Length < DiscReader.RawSectorSize) { error = "not a raw 2352-byte sector"; return false; }
                var c = ReadCoding(raw);
                if (!c.IsValid) { error = $"reserved coding byte 0x{c.Raw:X2}"; return false; }
                // ⚠ Refused rather than guessed at. No file on this disc uses 8-bit XA, so an implementation
                // could not be checked against anything, and an unchecked decoder here would look finished.
                if (c.BitsPerSample != 4) { error = "8-bit XA is not implemented: nothing on this disc uses it"; return false; }

                for (int g = 0; g < GroupsPerSector; g++)
                {
                    int p = FirstGroupOffset + g * GroupBytes;
                    Groups++;
                    if (!CopiesAgree(raw, p)) BadCopies++;

                    for (int blk = 0; blk < 4; blk++)
                    {
                        if (c.Stereo)
                        {
                            DecodeUnit(raw, p, blk, 0, ref _l1, ref _l2, _left);
                            DecodeUnit(raw, p, blk, 1, ref _r1, ref _r2, _right);
                            for (int j = 0; j < SamplesPerUnit; j++) { interleaved.Add(_left[j]); interleaved.Add(_right[j]); }
                        }
                        else
                        {
                            // ⚠ Mono runs BOTH units of the pair through the same filter history, in order.
                            DecodeUnit(raw, p, blk, 0, ref _l1, ref _l2, _left);
                            interleaved.AddRange(_left);
                            DecodeUnit(raw, p, blk, 1, ref _l1, ref _l2, _left);
                            interleaved.AddRange(_left);
                        }
                    }
                }
                return true;
            }

            void DecodeUnit(byte[] raw, int group, int blk, int nibble, ref int h1, ref int h2, short[] dst)
            {
                int param = raw[group + 4 + blk * 2 + nibble];
                int range = param & 0x0F;
                int filter = (param >> 4) & 0x03;
                if (range > 12 || (param & 0xC0) != 0) ReservedParams++;
                // Same treatment as the SPU decoder gives a reserved range, see Vag.Decode. Counted above, so
                // a disc where this matters says so instead of relying on it.
                if (range > 12) range = 9;
                int shift = 12 - range;
                int f0 = F0[filter], f1 = F1[filter];

                for (int j = 0; j < SamplesPerUnit; j++)
                {
                    int b = raw[group + 16 + blk + j * 4];
                    int nib = nibble == 0 ? (b & 0x0F) : (b >> 4);
                    int t = nib > 7 ? nib - 16 : nib;
                    int s = (t << shift) + ((h1 * f0 + h2 * f1 + 32) >> 6);
                    if (s > short.MaxValue) s = short.MaxValue;
                    else if (s < short.MinValue) s = short.MinValue;
                    dst[j] = (short)s;
                    h2 = h1; h1 = s;
                }
            }

            /// <summary>Bytes 0..3 must equal 4..7 and 12..15 must equal 8..11.</summary>
            static bool CopiesAgree(byte[] raw, int p)
            {
                for (int k = 0; k < 4; k++)
                    if (raw[p + k] != raw[p + 4 + k] || raw[p + 12 + k] != raw[p + 8 + k]) return false;
                return true;
            }
        }
    }
}
