using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>One triangle. The palette and page it draws with are stored ON it, not looked up.</summary>
    public readonly struct MeshFace
    {
        public readonly ushort I0, I1, I2;
        public readonly byte U0, V0, U1, V1, U2, V2;
        /// <summary>PSX CLUT id. Decodes to VRAM as x = (Clut &amp; 0x3F) * 16, y = Clut &gt;&gt; 6.</summary>
        public readonly ushort Clut;
        /// <summary>The subgroup's texture page, carried down so a face is self-describing.</summary>
        public readonly ushort TPage;

        /// <summary>The low five bits of the face's group flags: which primitive the game draws it with. The draw
        /// loop 0x800115FC indexes a 32-entry table at 0x80011F34 with them; each entry is a two-instruction stub
        /// that loads the GPU command and jumps to one of four builders (read off the stubs and the builders):
        /// bit 0 textured (clear: the one untextured routine), bit 1 double-sided (its builders skip the NCLIP
        /// back-face test), bit 2 flat 0x24 instead of gouraud 0x34, bit 3 semi-transparent (ORs 2 into the
        /// command), bit 4 unused.</summary>
        public readonly byte Kind;

        public MeshFace(ushort i0, ushort i1, ushort i2, byte u0, byte v0, byte u1, byte v1, byte u2, byte v2,
                        ushort clut, ushort tpage, byte kind = 0)
        { I0 = i0; I1 = i1; I2 = i2; U0 = u0; V0 = v0; U1 = u1; V1 = v1; U2 = u2; V2 = v2; Clut = clut; TPage = tpage; Kind = kind; }

        public bool Textured => (Kind & 1) != 0;
        public bool DoubleSided => (Kind & 2) != 0;
        /// <summary>Drawn flat (0x24): the builder writes the colour 0x808080, so the texel is not shaded at all.</summary>
        public bool Flat => (Kind & 4) != 0;
        /// <summary>Drawn semi-transparent: texels whose palette colour has bit 15 set blend with what is behind by
        /// the page's mode (<see cref="BlendMode"/>); the rest draw solid, and colour 0 not at all, as ever.</summary>
        public bool SemiTransparent => (Kind & 8) != 0;
        /// <summary>The page's blend mode, tpage bits 5-6: 0 = (behind + face) / 2, 1 = behind + face,
        /// 2 = behind - face, 3 = behind + face / 4.</summary>
        public int BlendMode => (TPage >> 5) & 3;

        /// <summary>VRAM pixel origin of this face's palette.</summary>
        public (int X, int Y) ClutOrigin => ((Clut & 0x3F) * 16, Clut >> 6);

        /// <summary>VRAM pixel origin of this face's texture page.
        ///
        /// ⚠ X IS bits 0-3, NOT bits 0-4. fable's report writes `x = (t &amp; 0x1F) * 64`, which cannot be
        /// right because bit 4 is the Y select and 0x1F * 64 = 1984 exceeds VRAM's 1024-pixel width. I copied
        /// it anyway and got pages at x = 1536..1920 — addresses that do not exist. Corrected against the
        /// hardware's own encoding (bits 0-3 X/64, bit 4 Y/256, bits 5-6 semi-transparency, 7-8 depth), and
        /// the corrected decode reproduces exactly the 12 pages tinyclaw logged off the live GPU, strays
        /// included. **Transcribing a constant is not verifying it.**</summary>
        public (int X, int Y) TPageOrigin => ((TPage & 0x0F) * 64, (TPage & 0x10) != 0 ? 256 : 0);
    }

    public sealed class Mesh
    {
        public int VertexCount;
        public int BoneCount;

        /// <summary>Header +0x28: how many entries the trailing u32 list has (`0x8002C608` copies it to the
        /// loaded record's +0x3A).</summary>
        public int SeatCount;

        /// <summary>⭐ THE SEAT LIST, AND IT IS THE MODEL THAT NAMES THE SEATS. The u32 list that ends every
        /// mesh sub-entry is a list of BONE INDICES, one per place a rider can sit, and a ride's draw pairs
        /// rider i with entry i of it (findings/rider-positions.md §2.2, §3.1).
        ///
        /// ⚠⚠ ITS LENGTH IS THE DRAW LOOP'S BOUND, NOT THE SEAT COUNT, AND THE TWO DISAGREE ON 37 OF THE 59
        /// FLAT RIDES. Eight rides have an EMPTY list, so their riders are invisible aboard; Caterpillar
        /// Capers seats eight at its top level and has three entries here, and the other five riders are
        /// simply never drawn. That is the retail game's behaviour, not a gap to fill in — a port that seats
        /// everybody is wrong in a way that looks right.
        ///
        /// ⚠ "Skinless bones are the seats" was a guess of mine and it is dead: Crazy Ape names ten of its
        /// eleven skinless bones and leaves bone 10 out. Read the list; do not infer it.</summary>
        public int[] Seats = System.Array.Empty<int>();

        /// <summary>Why the seat list could not be read, or null. Non-fatal, like the animation.</summary>
        public string SeatError;
        public int BlockCount;
        public int TrackCount;

        /// <summary>Header +0x00, raw. ⭐ The animation's CYCLE is this word PLUS ONE.
        ///
        /// The game's track dispatcher (0x8002cbc4) takes the time modulo a word from this structure --
        /// `divu` then `mfhi`, a real modulo and not a mask -- before evaluating any track. So a clip's
        /// cycle comes from the data, not from where its keys happen to end.
        ///
        /// ⭐ CONFIRMED AGAINST THE CONSOLE rather than inferred from the offset. For the language
        /// advisor (entry 83 sub 10) this word is 127. At the game's animation rate of 61.54
        /// units/second that is 103.2 frames per cycle if the cycle IS the word, and 104.0 if it is the
        /// word plus one; the console's flag repeats every 104 frames exactly, measured point for point
        /// at frames 30, 134 and 238. Eight tenths of a frame separates the two readings, which is why
        /// this is an identification and not a preference.
        ///
        /// ⚠ In all eleven of entry 83's clips this word EQUALS the keys' end time, so the port's
        /// "cycle = AnimationLength + 1" is the same number here. If a clip is ever found where they
        /// differ, THIS word is the one to trust.
        ///
        /// ⚠ The cycle is not the whole story: even at 128 the step from the last pose back to the
        /// first is 2,781 against a mean of 237, because the keys start at t=25 and nothing covers the
        /// head of the timeline. That is a separate gap in MeshPose, open on 200 models.</summary>
        public int HeaderWord0;

        /// <summary>{x, y, z} per vertex, in the file's own s16 units.</summary>
        public short[] Vertices = Array.Empty<short>();
        public byte[] VertexColours = Array.Empty<byte>();     // RGB triplets
        public List<MeshFace> Faces = new();
        /// <summary>Distinct texture pages this mesh draws from.</summary>
        public SortedSet<ushort> TPages = new();
        /// <summary>Bytes the face walk consumed. A parse that does not land inside the sub-entry is wrong.
        /// For a compressed sub-entry this is an offset into the EXPANDED buffer (MeshContainer.TryExpand),
        /// not into the entry.</summary>
        public int FaceBytesEnd;
        /// <summary>True when the sub-entry was LZSS-expanded (SubLz) before the walk.</summary>
        public bool WasCompressed;

        /// <summary>Animation tracks, walked after the faces. See <see cref="MeshAnimation"/>.
        /// Empty when TrackCount is 0 or the walk failed; <see cref="AnimationError"/> says which.</summary>
        public List<AnimTrack> Tracks = new();

        /// <summary>The bone hierarchy and rest pose. Empty when the walk failed.</summary>
        public Skeleton Skeleton = new();

        /// <summary>How animated sources scatter into vertices. Not a bone-per-vertex map.</summary>
        public VertexBinding Binding = new();
        /// <summary>Where the track walk stopped. Should land on the trailing u32 index list.</summary>
        public int TrackBytesEnd;
        /// <summary>Null when the tracks parsed. Non-null leaves Tracks empty rather than half-filled.</summary>
        public string AnimationError;
    }

    /// <summary>A `0x96` archive entry: a container of skinned meshes.
    ///
    /// ⭐ THIS IS THE KEYSTONE. One parser yields the geometry, the per-face CLUT (the real colour path,
    /// rather than colouring from a capture that only covers what someone photographed), and the `tpage`
    /// each mesh draws from — which is what gives a ride's artwork a file address.
    ///
    /// Layout, all SOURCED by fable from the loader at 0x8003084C / 0x800308C4 / 0x80030364 and measured
    /// against 556 of 556 sub-entries:
    ///
    ///     container  {u32 0x96, u32 nsub, 0, 0, 0, u32 recOff, u32 size, u32 ntab}
    ///                s32 tab[ntab]
    ///                {u32 off, u32 unpackedSize} sub[nsub]
    ///
    /// ⚠ `unpackedSize != 0` MEANS COMPRESSED, and it is easy to read as "size". Those sub-entries are LZSS
    /// (SubLz, the port of 0x800BFD9C) and TryParseMesh expands them before the walk. There are **25 of
    /// them and they live entirely in entries 0 and 3**; the other 531 are plain. All 25 reach their stream
    /// terminator at exactly the declared size and then walk as meshes to the byte — which is also the
    /// explanation for something that puzzled me for hours: the sub-entry "sizes" that overlapped their
    /// neighbours were UNPACKED sizes, 1.45 to 2.48 times the packed gap.
    ///
    /// ⚠ AND `f4 01 00 30` IS NOT A GPU OPCODE. I read it as a gouraud-triangle command and built a whole
    /// theory on it; fable identifies it as an LZ stream's flag byte and first literals. A plausible reading
    /// of four bytes is not a format.</summary>
    public sealed class MeshContainer
    {
        public const uint Magic = 0x96;
        public const int HeaderBytes = 0x20;
        public const int MeshHeaderBytes = 0x48;

        public int SubCount;
        public uint RecordOffset;
        public uint DeclaredSize;
        public int TableCount;
        /// <summary>Per sub-entry: offset from the entry start, and unpacked size (0 = stored plain).</summary>
        public List<(int Offset, int UnpackedSize)> Subs = new();

        public static bool IsContainer(byte[] d) =>
            d != null && d.Length >= HeaderBytes && BitConverter.ToUInt32(d, 0) == Magic;

        public static bool TryParse(byte[] d, out MeshContainer c, out string error)
        {
            c = null; error = null;
            if (!IsContainer(d)) { error = "not a 0x96 container"; return false; }

            var m = new MeshContainer
            {
                SubCount = BitConverter.ToInt32(d, 0x04),
                RecordOffset = BitConverter.ToUInt32(d, 0x14),
                DeclaredSize = BitConverter.ToUInt32(d, 0x18),
                TableCount = BitConverter.ToInt32(d, 0x1C),
            };
            if (m.SubCount < 0 || m.SubCount > 4096) { error = $"implausible sub-entry count {m.SubCount}"; return false; }
            if (m.TableCount < 0 || m.TableCount > 65536) { error = $"implausible table count {m.TableCount}"; return false; }

            int at = HeaderBytes + m.TableCount * 4;
            if (at + m.SubCount * 8 > d.Length) { error = "sub-entry table runs past the end"; return false; }
            for (int i = 0; i < m.SubCount; i++)
            {
                m.Subs.Add((BitConverter.ToInt32(d, at + i * 8), BitConverter.ToInt32(d, at + i * 8 + 4)));
            }
            c = m;
            return true;
        }

        public bool IsCompressed(int sub) => sub >= 0 && sub < Subs.Count && Subs[sub].UnpackedSize != 0;

        /// <summary>The bytes of one sub-entry as the loader sees them. A plain sub-entry is a view into the
        /// entry, beginning at <paramref name="start"/>; a compressed one is expanded through
        /// <see cref="SubLz"/> into a buffer of its own, beginning at 0. Expansion fails by name if the
        /// stream does not reach its terminator at exactly the declared size.</summary>
        public bool TryExpand(byte[] d, int sub, out byte[] data, out int start, out string error)
        {
            data = null; start = 0; error = null;
            if (d == null) { error = "no entry bytes"; return false; }
            if (sub < 0 || sub >= Subs.Count) { error = "no such sub-entry"; return false; }
            var (off, unpacked) = Subs[sub];
            if (unpacked == 0) { data = d; start = off; return true; }
            if (!SubLz.TryDecompress(d, off, unpacked, out data, out string lz))
            { error = $"sub-entry {sub} did not expand ({unpacked:n0} declared): {lz}"; return false; }
            return true;
        }

        /// <summary>Parse one sub-entry as a skinned mesh, expanding it first if it is compressed.</summary>
        public bool TryParseMesh(byte[] d, int sub, out Mesh mesh, out string error)
        {
            mesh = null;
            if (!TryExpand(d, sub, out var data, out int start, out error)) return false;
            if (!TryParseMeshAt(data, start, out mesh, out error)) return false;
            mesh.WasCompressed = IsCompressed(sub);
            return true;
        }

        /// <summary>Walk the mesh that begins at <paramref name="b"/> in <paramref name="d"/>: the entry for
        /// a plain sub-entry, an expanded buffer (b = 0) for a compressed one.</summary>
        public static bool TryParseMeshAt(byte[] d, int b, out Mesh mesh, out string error)
        {
            mesh = null; error = null;
            if (d == null || b < 0 || b + MeshHeaderBytes > d.Length) { error = "sub-entry offset is outside the entry"; return false; }

            int n0 = BitConverter.ToInt32(d, b + 0x00);
            var m = new Mesh
            {
                HeaderWord0 = n0,
                BoneCount = BitConverter.ToInt32(d, b + 0x04),
                VertexCount = BitConverter.ToInt32(d, b + 0x08),
                BlockCount = BitConverter.ToInt32(d, b + 0x18),
                TrackCount = BitConverter.ToInt32(d, b + 0x1C),
            };
            int nvA = BitConverter.ToInt32(d, b + 0x24);
            if (m.VertexCount < 0 || m.VertexCount > 65536 || nvA < 0 || nvA > 65536 ||
                m.BlockCount < 0 || m.BlockCount > 65536)
            { error = "implausible mesh header counts"; return false; }

            // ⚠ vertA COMES FIRST. The extra 8-byte vertex records sit before the main array; skipping them
            // shifts every vertex index by nvA and produces a mesh that parses and is geometrically wrong.
            int p = b + MeshHeaderBytes + nvA * 8;
            if (p + m.VertexCount * 8 > d.Length) { error = "vertex array runs past the end"; return false; }

            m.Vertices = new short[m.VertexCount * 3];
            for (int i = 0; i < m.VertexCount; i++)
            {
                m.Vertices[i * 3 + 0] = BitConverter.ToInt16(d, p + i * 8 + 0);
                m.Vertices[i * 3 + 1] = BitConverter.ToInt16(d, p + i * 8 + 2);
                m.Vertices[i * 3 + 2] = BitConverter.ToInt16(d, p + i * 8 + 4);
            }
            p += m.VertexCount * 8;

            if (p + m.VertexCount * 4 > d.Length) { error = "colour array runs past the end"; return false; }
            m.VertexColours = new byte[m.VertexCount * 3];
            for (int i = 0; i < m.VertexCount; i++)
            {
                m.VertexColours[i * 3 + 0] = d[p + i * 4 + 0];
                m.VertexColours[i * 3 + 1] = d[p + i * 4 + 1];
                m.VertexColours[i * 3 + 2] = d[p + i * 4 + 2];
            }
            p += m.VertexCount * 4;

            // Per-block bit array: ceil(n0/8), rounded up to an even byte count.
            int bitBytes = (n0 + 7) / 8;
            if ((bitBytes & 1) != 0) bitBytes++;

            for (int blk = 0; blk < m.BlockCount; blk++)
            {
                if (p + 2 > d.Length) { error = $"block {blk} header past the end"; return false; }
                int nsections = BitConverter.ToUInt16(d, p); p += 2;
                p += bitBytes;
                for (int sec = 0; sec < nsections; sec++)
                {
                    if (p + 4 > d.Length) { error = $"section header past the end in block {blk}"; return false; }
                    int ngroups = BitConverter.ToUInt16(d, p); p += 4;       // second u16 unidentified
                    for (int g = 0; g < ngroups; g++)
                    {
                        if (p + 8 > d.Length) { error = $"group header past the end in block {blk}"; return false; }
                        int nsub = BitConverter.ToUInt16(d, p);
                        // ⭐ +2 is the group's flags word: its low five bits choose the primitive (MeshFace.Kind).
                        // The draw loop reads it there (0x800116D4: lh t0,2(s2); andi t0,t0,0x1f).
                        byte kind = (byte)(BitConverter.ToUInt16(d, p + 2) & 0x1F);
                        p += 8;                                               // + one u32 unidentified
                        for (int sg = 0; sg < nsub; sg++)
                        {
                            if (p + 4 > d.Length) { error = $"subgroup header past the end in block {blk}"; return false; }
                            int nfaces = BitConverter.ToUInt16(d, p);
                            ushort tpage = BitConverter.ToUInt16(d, p + 2);
                            p += 4;
                            if (p + nfaces * 14 > d.Length) { error = $"face array past the end in block {blk}"; return false; }
                            m.TPages.Add(tpage);
                            for (int f = 0; f < nfaces; f++)
                            {
                                int q = p + f * 14;
                                m.Faces.Add(new MeshFace(
                                    BitConverter.ToUInt16(d, q), BitConverter.ToUInt16(d, q + 2), BitConverter.ToUInt16(d, q + 4),
                                    d[q + 6], d[q + 7], d[q + 8], d[q + 9], d[q + 10], d[q + 11],
                                    BitConverter.ToUInt16(d, q + 12), tpage, kind));
                            }
                            p += nfaces * 14;
                        }
                    }
                }
            }

            m.FaceBytesEnd = p;

            // Bones and animation tracks follow the faces. A failure here is recorded, not fatal:
            // the geometry above is already good and callers that only draw should still get it.
            int m12 = BitConverter.ToInt32(d, b + 0x0C);
            int n8b = BitConverter.ToInt32(d, b + 0x20);
            if (MeshAnimation.TryParse(d, b, p, m.BoneCount, m.TrackCount, m12, n8b,
                                       out var tracks, out var skel, out var bind, out int trackEnd, out string animErr))
            {
                m.Tracks = tracks;
                m.Skeleton = skel;
                m.Binding = bind;
                m.TrackBytesEnd = trackEnd;
            }
            else m.AnimationError = animErr;

            // The seat list is the last thing in the sub-entry, after the tracks.
            // ⚠ IT HANGS OFF THE ANIMATION PARSE. Without TrackBytesEnd there is no way to find the list
            // from the front, and guessing at the sub-entry's end from the outside would put a plausible
            // wrong offset in a field callers trust. So it stays empty and says why.
            m.SeatCount = BitConverter.ToInt32(d, b + 0x28);
            if (m.SeatCount < 0 || m.SeatCount > 4096)
                m.SeatError = $"implausible seat count {m.SeatCount}";
            else if (m.SeatCount == 0) { }
            else if (m.TrackBytesEnd <= 0)
                m.SeatError = "the animation did not parse, so the list's start is unknown";
            else if (m.TrackBytesEnd + m.SeatCount * 4 > d.Length)
                m.SeatError = $"{m.SeatCount} seats at {m.TrackBytesEnd:X} run past the end";
            else
            {
                m.Seats = new int[m.SeatCount];
                for (int i = 0; i < m.SeatCount; i++)
                    m.Seats[i] = BitConverter.ToInt32(d, m.TrackBytesEnd + i * 4);
            }

            mesh = m;
            return true;
        }
    }
}
