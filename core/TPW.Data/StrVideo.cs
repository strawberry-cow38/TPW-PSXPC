using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>One frame's compressed bitstream, reassembled from its sectors.</summary>
    public sealed class StrFrame
    {
        public int Number;
        public int Width;
        public int Height;
        /// <summary>The MDEC bitstream. Still compressed — see Mdec.</summary>
        public byte[] Data = Array.Empty<byte>();
        public int ChunkCount;
    }

    /// <summary>The `.STR` movie container: PlayStation streaming video with audio interleaved.
    ///
    /// ⭐ WHAT THESE FILES ARE. Five of them, 12-14 MB each, and everyone including me assumed they were the
    /// music — they are 320x176 VIDEO. The actual music was the XM tracker in the archive all along.
    ///
    /// ⚠ THEY ARE NOT THE TITLE-SCREEN ATTRACT CYCLE, which is what fable's report calls GRAV/MIR/JUG. They
    /// play ON ENTERING A WORLD, one per world. Master remembers it that way, and tinyclaw's failed capture
    /// turned into the control: 9,000 frames idling the title screen with ZERO vram uploads, and a playing
    /// movie uploads a frame at a time. The absence carries information because something was predicted to
    /// be there. It also fits the rest of that same report better than the report's own label does — it
    /// found the music picks its module by WORLD INDEX. Four worlds, four modules, one intro movie each.
    ///
    /// ⭐ SO THE DECODER HAS FOUR POSITIVE CONTROLS, NOT ONE — from master, who has played it:
    ///     BF     the Bullfrog logo        (publicly known)
    ///     GRAV   a gravity bounce-house   (space)
    ///     MIR    a freaky mirror thing    (halloween)
    ///     JUG    jungle                   (jungle)
    /// A logo only proves the output is not noise. "A gravity bounce-house" catches the failure a logo
    /// cannot: a DCT close enough to give plausible shapes in roughly the right colours.
    ///
    /// english/french/spanish/END are the same ending cutscene four times, the text burned into the picture
    /// and the audio byte-identical.
    ///
    /// ⚠ THE SECTORS ARE NOT ALL THE SAME KIND, and a reader that treats them uniformly produces corrupt
    /// frames rather than an error. The Mode 2 subheader's submode byte says which: bit 0x04 is audio and
    /// bit 0x08 is data. Measured on BF.STR, the first 64 sectors are 56 video and 8 audio — audio every
    /// eighth sector, exactly as fable reported.
    ///
    /// ⚠ AND VIDEO SECTORS ARE FORM 1, NOT FORM 2. Their submode is 0x48: real-time (0x40) plus data
    /// (0x08), with the Form 2 bit clear. So video carries 2048 bytes and the interleaved audio carries
    /// 2324 — reading one size for both drifts 276 bytes per audio sector.
    ///
    /// Each video sector opens with a 32-byte header: magic `0x80010160`, chunk index and count, frame
    /// number, the frame's total byte count, then width and height.</summary>
    public static class StrVideo
    {
        public const uint ChunkMagic = 0x80010160;
        public const int SectorHeaderBytes = 32;
        /// <summary>Video payload per sector: 2048 of user data less the 32-byte chunk header.</summary>
        public const int ChunkPayloadBytes = DiscReader.UserDataSize - SectorHeaderBytes;

        public static bool IsAudioSector(byte[] rawSector) =>
            rawSector != null && rawSector.Length >= 24 && (rawSector[18] & 0x04) != 0;

        public static bool IsVideoSector(byte[] rawSector) =>
            rawSector != null && rawSector.Length >= 24 && (rawSector[18] & 0x04) == 0 && (rawSector[18] & 0x08) != 0;

        /// <summary>Read frames from a `.STR` on the disc, up to <paramref name="maxFrames"/>.
        ///
        /// ⭐ THE ORACLE IS IN THE CONTAINER: every chunk of a frame repeats that frame's total byte count,
        /// so a frame whose assembled chunks do not add up to the declared size has been misassembled. That
        /// is checked rather than assumed, because a short frame still decodes — into rubbish.</summary>
        public static List<StrFrame> ReadFrames(DiscReader disc, DiscFile file, int maxFrames = 1)
        {
            var frames = new List<StrFrame>();
            if (disc == null || file == null) return frames;

            int sectors = file.Length / DiscReader.UserDataSize;
            StrFrame current = null;
            var buf = new List<byte>();
            int declared = 0, seen = 0;

            for (int i = 0; i < sectors && frames.Count < maxFrames; i++)
            {
                var rawSector = disc.ReadRawSector(file.Lba + i);
                if (rawSector == null) break;
                if (!IsVideoSector(rawSector)) continue;           // audio: not this reader's business

                int at = 24;                                       // Mode 2 user data starts 24 bytes in
                if (BitConverter.ToUInt32(rawSector, at) != ChunkMagic) continue;

                int chunk = BitConverter.ToUInt16(rawSector, at + 4);
                int chunks = BitConverter.ToUInt16(rawSector, at + 6);
                int frameNo = BitConverter.ToInt32(rawSector, at + 8);
                int frameBytes = BitConverter.ToInt32(rawSector, at + 12);
                int w = BitConverter.ToUInt16(rawSector, at + 16);
                int h = BitConverter.ToUInt16(rawSector, at + 18);

                if (chunk == 0)
                {
                    current = new StrFrame { Number = frameNo, Width = w, Height = h, ChunkCount = chunks };
                    buf.Clear(); declared = frameBytes; seen = 0;
                }
                if (current == null) continue;                     // joined mid-frame; wait for the next chunk 0

                int take = Math.Min(ChunkPayloadBytes, declared - seen);
                if (take <= 0) take = 0;
                for (int k = 0; k < take; k++) buf.Add(rawSector[at + SectorHeaderBytes + k]);
                seen += take;

                if (chunk == chunks - 1)
                {
                    if (seen == declared)
                    {
                        current.Data = buf.ToArray();
                        frames.Add(current);
                    }
                    current = null;
                }
            }
            return frames;
        }
    }
}
