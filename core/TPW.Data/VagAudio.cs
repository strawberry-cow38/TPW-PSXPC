using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>One decoded sample: 16-bit mono PCM.</summary>
    public sealed class PcmSample
    {
        public short[] Samples;
        public int LoopStart = -1;   // in samples; -1 when the sound does not loop
        public string Source = "";
        public int SampleCount => Samples?.Length ?? 0;
    }

    /// <summary>A VAB sound bank's header (the "VH" half): how many waveforms there are and how long each is.
    ///
    /// ⭐ THE HEADER AND THE WAVEFORMS ARE SEPARATE ARCHIVE ENTRIES, AND THE BODY COMES FIRST. A VAB is
    /// normally one file; this game ships it split, the standard VH/VB pair. The pairing was established by
    /// measurement rather than assumed: for each of the 9 banks, the sum of the VAG size table equals the size
    /// of **the entry immediately BEFORE it**, exactly, 9 times out of 9. Guessing "the body follows the
    /// header" would have been the natural reading and is wrong.
    ///
    /// ⭐ AND THE GROUPING IS CORROBORATED BY SOMETHING THE HEADER DOES NOT CONTROL. The archive holds 9
    /// triples of (waveform body, VAB header, XM tracker module), and in every one the XM's instrument count
    /// equals the VAB's waveform count — 6/6, 26/26, 15/15, 21/21, 25/25, 22/22, 27/27, 23/23, 28/28. Two
    /// independently-authored files agreeing nine times is not a coincidence: the tracker's instruments ARE
    /// these waveforms, which is how the music and the samples fit together.</summary>
    public sealed class VabHeader
    {
        public const int Magic = 0x56414270;         // "pBAV" as a big-endian read of "VABp"
        public const int HeaderBytes = 32;
        public const int ProgramTableBytes = 128 * 16;
        public const int ToneTableBytes = 16 * 32;
        public const int OffsetTableBytes = 256 * 2;
        /// <summary>Total size of a split VH file: 32 + 2048 + 512 + 512. Every bank on this disc is exactly
        /// this, which is what identifies them as headers rather than whole VABs.</summary>
        public const int SplitHeaderSize = HeaderBytes + ProgramTableBytes + ToneTableBytes + OffsetTableBytes;

        public int Version;
        public int BankId;
        public int DeclaredTotal;
        public int ProgramCount;
        public int ToneCount;
        public int WaveCount;
        public byte MasterVolume;
        public byte MasterPan;
        /// <summary>Byte length of each waveform, in order. Index 0 of the on-disc table is unused.</summary>
        public int[] WaveSizes = Array.Empty<int>();

        public int BodyBytes
        {
            get { int n = 0; foreach (int s in WaveSizes) n += s; return n; }
        }

        public static bool TryParse(byte[] d, out VabHeader h, out string error)
        {
            h = null; error = null;
            if (d == null || d.Length < SplitHeaderSize)
            { error = $"need {SplitHeaderSize} bytes for a VAB header, have {d?.Length ?? 0}"; return false; }
            if (!(d[0] == (byte)'p' && d[1] == (byte)'B' && d[2] == (byte)'A' && d[3] == (byte)'V'))
            { error = "no pBAV signature"; return false; }

            var v = new VabHeader
            {
                Version = BitConverter.ToInt32(d, 4),
                BankId = BitConverter.ToInt32(d, 8),
                DeclaredTotal = BitConverter.ToInt32(d, 12),
                ProgramCount = BitConverter.ToUInt16(d, 18),
                ToneCount = BitConverter.ToUInt16(d, 20),
                WaveCount = BitConverter.ToUInt16(d, 22),
                MasterVolume = d[24],
                MasterPan = d[25],
            };
            if (v.WaveCount <= 0 || v.WaveCount > 254) { error = $"implausible waveform count {v.WaveCount}"; return false; }

            // ⚠ THE SIZE TABLE IS IN 8-BYTE UNITS, AND ENTRY 0 IS NOT A WAVEFORM. Reading it as bytes gives
            // every sound an eighth of its length, which decodes to real-sounding audio that simply stops
            // early -- a failure that is easy to hear and hard to attribute.
            int off = HeaderBytes + ProgramTableBytes + ToneTableBytes;
            v.WaveSizes = new int[v.WaveCount];
            for (int i = 0; i < v.WaveCount; i++)
                v.WaveSizes[i] = BitConverter.ToUInt16(d, off + (i + 1) * 2) * 8;

            h = v;
            return true;
        }

        /// <summary>Split the paired body into its individual waveforms, in table order.</summary>
        public List<byte[]> SliceBody(byte[] body)
        {
            var outp = new List<byte[]>(WaveCount);
            int p = 0;
            foreach (int size in WaveSizes)
            {
                if (size <= 0 || p + size > (body?.Length ?? 0)) { outp.Add(Array.Empty<byte>()); p += Math.Max(size, 0); continue; }
                var b = new byte[size];
                Buffer.BlockCopy(body, p, b, 0, size);
                outp.Add(b);
                p += size;
            }
            return outp;
        }
    }

    /// <summary>PlayStation ADPCM ("VAG") decoder: 16-byte blocks to 16-bit PCM.
    ///
    /// Each block is one control byte (shift in the low nibble, filter index in the high), one flag byte, then
    /// 14 bytes holding 28 four-bit samples. Output is the running second-order filter
    /// `s = nibble &lt;&lt; (12 - shift) + (h1*f0 + h2*f1) / 64`.</summary>
    public static class Vag
    {
        public const int BlockBytes = 16;
        public const int SamplesPerBlock = 28;

        // Fixed coefficients, /64. Standard across every PSX title.
        static readonly int[] F0 = { 0, 60, 115, 98, 122 };
        static readonly int[] F1 = { 0, 0, -52, -55, -60 };

        public static PcmSample Decode(byte[] adpcm, string source = "")
        {
            var outp = new List<short>((adpcm?.Length ?? 0) / BlockBytes * SamplesPerBlock);
            var result = new PcmSample { Source = source };
            if (adpcm == null) { result.Samples = Array.Empty<short>(); return result; }

            int h1 = 0, h2 = 0;
            for (int p = 0; p + BlockBytes <= adpcm.Length; p += BlockBytes)
            {
                int shift = adpcm[p] & 0x0F;
                int filter = (adpcm[p] >> 4) & 0x0F;
                int flags = adpcm[p + 1];

                // ⚠ FLAG 7 IS "END OF STREAM" AND MUST STOP THE LOOP. Decoding past it walks into whatever
                // follows in the bank and produces a burst of noise welded onto the end of an otherwise
                // correct sound -- which gets blamed on the decoder rather than on the terminator.
                if (flags == 7) break;
                if ((flags & 4) != 0 && result.LoopStart < 0) result.LoopStart = outp.Count;

                // ⚠ OUT-OF-RANGE FILTER INDICES DO OCCUR. Real banks contain blocks with a filter above the
                // five defined ones; hardware treats those as filter 0 rather than faulting, so clamping here
                // is fidelity, not defensiveness. Same for a shift above 12, which would shift a nibble off
                // the top of the word.
                if (filter >= F0.Length) filter = 0;
                if (shift > 12) shift = 9;

                for (int i = 0; i < 14; i++)
                {
                    int b = adpcm[p + 2 + i];
                    for (int half = 0; half < 2; half++)
                    {
                        int nib = half == 0 ? (b & 0x0F) : (b >> 4);
                        // sign-extend the 4-bit sample
                        int s = nib > 7 ? nib - 16 : nib;
                        int v = s << (12 - shift);
                        v += (h1 * F0[filter] + h2 * F1[filter]) >> 6;
                        if (v > short.MaxValue) v = short.MaxValue;
                        else if (v < short.MinValue) v = short.MinValue;
                        outp.Add((short)v);
                        h2 = h1; h1 = v;
                    }
                }
                if ((flags & 1) != 0) break;   // end marker
            }

            result.Samples = outp.ToArray();
            return result;
        }

        /// <summary>A quick structural check used by the self-test: do the control bytes look like ADPCM?
        /// Cheap, and it fails loudly on data that is not audio at all.</summary>
        public static bool LooksLikeAdpcm(byte[] d, int blocksToCheck = 64)
        {
            if (d == null || d.Length < BlockBytes) return false;
            int checkedBlocks = 0, plausible = 0;
            for (int p = 0; p + BlockBytes <= d.Length && checkedBlocks < blocksToCheck; p += BlockBytes, checkedBlocks++)
                if (d[p + 1] <= 7 && (d[p] >> 4) <= 4) plausible++;
            return checkedBlocks > 0 && plausible * 100 / checkedBlocks >= 90;
        }
    }

    /// <summary>The XM tracker modules that carry the music. Not played here — recognising and counting them
    /// is what the self-test needs, and the instrument count is the cross-check that ties a module to its
    /// sample bank.</summary>
    public static class XmModule
    {
        public const string Signature = "Extended Module: ";

        public static bool TryReadCounts(byte[] d, out int channels, out int patterns, out int instruments)
        {
            channels = patterns = instruments = 0;
            if (d == null || d.Length < 80) return false;
            for (int i = 0; i < Signature.Length; i++) if (d[i] != (byte)Signature[i]) return false;
            channels = BitConverter.ToUInt16(d, 68);
            patterns = BitConverter.ToUInt16(d, 70);
            instruments = BitConverter.ToUInt16(d, 72);
            return true;
        }
    }
}
