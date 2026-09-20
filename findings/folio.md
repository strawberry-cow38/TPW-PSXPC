# Theme Park World (SLES-026.88) — FOLIO.GAZ: the reader, the 0x96 container, and where the pixels are

Tenth report. Addresses are in the RAW image (`TPW.BIN` at 0x80010000). Tags: **SOURCED** = read off
instructions/data at the quoted address. **DERIVED** = inferred, basis stated. **MEASURED** = checked
against every entry on the disc by a script in `f/`. **UNMEASURED** = guess. Scratch and tools in
`fable/f/`: `x96parse.py` (container + mesh walker), `sublz.py` (sub-entry decompressor), `entries.txt`,
`x96.txt`, and the disassembly dumps `fn_*.txt` this report was read from.

**2026-09-20 correction, READ:** [animation-phases.md](animation-phases.md) identifies the complete
88-byte runtime descriptor and **mesh+0 as the phase length**. `0x8003084C` returns the container
base in v0; it stores the computed directory pointer separately. Direct archive counts are 244
record-bearing containers, 17 without a record but with a four-slot map, and 7 model packs without
a map. The earlier claim that all 261 map-bearing containers have a record and one mesh was wrong.

## 0. The answers, shortest form

| # | question | answer | tag |
|---|---|---|---|
| 1 | code that reads FOLIO.GAZ | **The string IS in TPW.BIN, three times** (§1). Opened by name through `CdSearchFile("\FOLIO.GAZ;1")` at 0x800249B0; LBA is discovered at runtime and cached, never hardcoded. Whole-entry loads: 0x800BBF2C → 0x80025650 → 0x800252D0 → CdRead wrapper 0x80024538. Streamed loads: 0x80032A5C → CdRead2 wrapper 0x80024DA4 with a per-sector callback | SOURCED |
| 2 | what 0x96 is | a **container magic**, tested nowhere (the loader trusts it); the header is `{0x96, nsub, 0,0,0, recOff, size, ntab}` + `s32 tab[ntab]` + `{u32 off, u32 unpackedSize} sub[nsub]`; sub-entries are **meshes**, `recOff` is the definition record rides.md already decoded | SOURCED (0x8003084C / 0x800308C4 / 0x80030364) |
| 3 | bitmaps or geometry | **Geometry.** A sub-entry is a skinned mesh: s16 vertices, RGB vertex colours, 14-byte faces `{i0,i1,i2; u0v0 u1v1 u2v2; clut}` under a `{nfaces, tpage}` header, then skeleton matrices and animation tracks. The emitter 0x800115FC copies `clut`/`tpage` straight into POLY_FT3/GT3 words. **No pixels in any 0x96 entry.** `f4 01 00 30` is the first four bytes of an LZ stream (§3.5), not a GPU opcode | SOURCED + MEASURED 556/556 |
| 4 | where the pixels are, and where they land | three carriers, none of them 0x96: **(a)** twelve 0x20054 entries = `0x54` header + raw 256×256-halfword image → **VRAM (512,256)–(767,511)**; **(b)** same header with `compressed=1` (0005, 0082, 0084, 0168, 0258, 0017, 0018, 0091, 0092, 0169, 0170, 0269, 0278) = UNPAK'd 64×64 blocks; **(c)** entries 0x104 / 0x10C = raw 128 KB streamed straight from disc into **(768..895, 0..511)** | SOURCED (§4) |
| 5 | falsifier | §5: the 16 halfwords at file offset 0x54 of whichever bank is loaded must sit at VRAM (512..527, 256); entry 19's face 0 is at file 0x178 with clut 0x14E1 / tpage 0x19 | — |

**Why the three earlier approaches failed:** (1) 0x96 is never compared by code, so grepping for it
could only find camera clamps. (2) The 131,156-byte entries are not "256×256 16bpp": they are a 256
halfword × 256 row VRAM image starting at +0x54, i.e. **stride 512 bytes** — the two strides tested
(1024, 512) both miss because the test started at +0, 84 bytes into a row. (3) There are no TIMs because
every image is a headerless VRAM rectangle with its geometry (page id, columns, rows) in a 16-byte game
header, or none at all.

## 1. The reader (SOURCED)

### 1.1 The string is in TPW.BIN
| address | text | referenced by |
|---|---|---|
| 0x800DFF84 | `FOLIO.GAZ` | 0x80032ABC (`lui/addiu`) in the **streamer** 0x80032A5C |
| 0x800E7174 | `folio.gaz` | 0x800BBF68 in the **generic entry loader** 0x800BBF2C |
| 0x800DCEAC | `folio.gaz` | nothing: no lui/addiu pair and no data pointer (xref.py + data-word scan). Sits in the loading-screen/FMV literal pool ("LOADING : %d", "SCENE ONE") |

It is not in the boot exe or the overlays; the prompt's search evidently stopped at the boot exe.

### 1.2 Opening: directory walk, not LBA
- 0x80024624(name): looks the name up in a **10-entry file cache** at 0x800F023C (0x1C bytes each,
  0x800248E8 compares names, 0x800247F0 fills an entry). On a miss it calls 0x800249B0, which
  `sprintf`s `"\%s;1"` (format at 0x801027AC) and calls **CdSearchFile**; `CdPosToInt` of the result is
  stored in the cache entry (read back by 0x80024EFC = `[entry+20]`). The LBA 174726 appears nowhere in
  the image.
- The other CD user, 0x8001836C (`"\"` + name + `";1"`, CdSearchFile), belongs to the 0x80018xxx
  XA/stream module and is not on the FOLIO path.

### 1.3 Two read paths
**Whole entry** — 0x80024538(name, byteOffset, buf, byteCount): `CdIntToPos(lba + (byteOffset>>11))`,
`CdControlB(CdlPause)`, `CdControlB(CdlSetloc)`, `CdRead(byteCount>>11, buf, CdlModeSpeed)`,
`CdReadSync` loop. Called from 0x80025650 (mgr, name, ?, size, offset) via 0x800252D0, which rounds the
size up to 0x800 (0x800C2684 = round-up) and allocates the buffer with heap tag **"LOADF"**
(0x801027DC). The entry is byte-exact in RAM afterwards: every consumer below indexes the locked
buffer with the file offsets.

**Streamed** — 0x80024DA4(mgr, req) where `req = {char* name; u32 byteOffset; u32 length; u32 buf;
u32 chunk; void (*cb)(buf, offsetInRequest, bytes, 0)}` (fields at +0..+20, read at 0x80024DE8..
0x80024E98): same seek, then `CdRead2(CdlModeSpeed)` and a loop of `CdGetSector(buf, 0x200 words)` +
`cb(buf, runningOffset, min(chunk, remaining))`. Only user: 0x80032A5C (§4.3).

### 1.4 The entry table in RAM
The table is loaded at init into `[0x8010346C]`. **0x800BBFA0(i) = `u32 [[0x8010346C] + 8 + 8*i]`** =
file offset of entry i (0x800BC1BC wraps it). So the on-disc layout is
`u32 n=422; u32 0x17; {u32 offset, u32 size}[422]` — pairs start at **+8**, not +4 (rides.md §1.3 and
the prompt both skip the 0x17 word; `ext/rip/` was cut correctly). Sizes are read the same way at +12.
Generic entry load: **0x800BBF2C(obj, entryIndex, flags)** → 0x800BBE44/0x800BBD30 → 0x80025650 with
size and offset from that table; returns a heap handle. 0x800351BC(ctx, entry) and 0x800282D0(obj, entry)
are the two thin wrappers everything else uses.

## 2. The 0x96 container (SOURCED)

Read by the resource object (base ctor 0x8002FD94, vtable 0x800DDEC8): 0x8003084C(res) locks `res+24`
(the LOADF handle), stores the base at `res+12` and **`res+16 = base + 0x20 + 4*base[0x1C]`**;
0x800308C4(res, i) takes sub-entry i from that table; 0x80030364(handle, phase) returns
`base[0x20 + 4*phase]` if `phase < base[0x1C]`; 0x800307DC returns `base + base[0x14]` (the record,
rides.md). Nothing ever compares word 0 with 0x96.

```
+0x00 u32 0x96          magic (unchecked)
+0x04 u32 nsub          number of sub-entries
+0x08 u32 ×3 = 0        (never read)
+0x14 u32 recOff        definition record offset (0 = none)         ← rides.md's "recOff"
+0x18 u32 size          == entry size in the table (MEASURED 268/268)
+0x1C u32 ntab          phase-map length (4 on 261 entries, of which 244 bear records; 0 on 7)
+0x20 s32 tab[ntab]     animation phase → sub-entry index, −1 = none (0x80030364; rides.md's A+0x64 phase)
      {u32 off, u32 unpacked} sub[nsub]   off = sub-entry offset from entry start;
                                          unpacked ≠ 0 → LZ-compressed, decompress with 0x800BFD9C (§3.5)
      sub-entries…  then the record at recOff (rides.md §1.3 layout)
```
Two map families (READ, direct archive re-scan 2026-09-20): **261 entries** with ntab=4;
244 have a definition record, of which 220 have one mesh. All 59 flat rides have one raw sub-entry
`(0x38, 0)` and map `[-1, 0, -1, -1]`; the 24 tour/track/coaster entries have multiple meshes.
**7 entries** (0000, 0003, 0004, 0083, 0089, 0277, 0417) with ntab=0, recOff=0, 1..17 sub-entries — model
packs (people etc., DERIVED from the count and the skeletons). Entry 0000 sub-entry 0 decompresses to a
193-vertex, 17-bone, 301-face mesh.

## 3. A sub-entry is a skinned mesh (SOURCED; layout MEASURED 556/556)

Read off the descriptor builder **0x8002C5CC(rec, mesh)** (computes every region's size, allocates the
"ARS" work buffer, records region offsets at rec+0..+24), the texture relocator **0x8002E574** and the
polygon emitter **0x800115FC** (hand-written asm; face fields → primitive words). `f/x96parse.py`
implements exactly this walk and ends on the record / next sub-entry for all 531 raw sub-entries and
all 25 decompressed ones (`x96parse.py`, `sublz.py`).

### 3.1 Header (0x48 bytes)
| off | field | how known |
|---|---|---|
| +0x00 | u32 animation length; its low u16 is copied to runtime descriptor+0x38. Also sizes the per-block bit array (`align2(ceil(n0/8)) & 0xFF` = rec+52) | READ 0x8002C5FC..604, 0x8002C6D0..6E8; animator modulo at 0x8002CC94 |
| +0x04 | u32 n32 — **bones**: 32-byte GTE matrices in ARS, 40-byte records in the file after the blocks | 0x8002C700, 0x8002C904 |
| +0x08 | u32 nvc — **vertices** (8 bytes each) and vertex **colours** (4 bytes each) | 0x8002C810 (×12) |
| +0x0C | u32 m12 — 12-byte records after the bone records | 0x8002C924 |
| +0x10 | u32 n8 — vertices copied into ARS (8 bytes each) | 0x8002C714, copy at 0x8002CA6C |
| +0x18 | u32 nblocks — face blocks; ARS keeps a u32 offset per block | 0x8002C6EC, 0x8002C82C |
| +0x1C | u32 ntracks — animation tracks (§3.4) | 0x8002C948 |
| +0x20 | u32 n8b — 12-byte records after the bones, 8 bytes of each copied to ARS | 0x8002C904, 0x8002CAB4 |
| +0x24 | u32 nvA — extra 8-byte vertex records placed **first** at +0x48 | 0x8002C804 |
| +0x28 | u32 — length of the trailing u32 list (§3.4); also stored as u16 at rec+58 | 0x8002C608; list MEASURED |
| +0x38..+0x47 | two s16[4]: treated as bounds; centre → rec+76/78/80, radius → rec+82 (sqrt 0x800BF1CC) for the cull test 0x80011340. x/z match the vertex extents exactly; y does not (UNMEASURED what y is) | 0x8002C614..0x8002C6CC |

### 3.2 Data, in file order
```
+0x48                       vertA[nvA]   8 bytes each
                            vert[nvc]    {s16 x, s16 y, s16 z, s16 pad}   (GTE loads at 0x8002CF68: +0,+2,+4)
                            rgb[nvc]     {u8 r, u8 g, u8 b, u8 0}
                            block[nblocks]:
                                u16 nsections
                                u8  bits[rec+52]            (ceil(n0/8) rounded up to even)
                                section: u16 ngroups, u16 ?
                                  group: u16 nsub, u16 flags (bit0 textured, bit1 gouraud — 0x800115FC picks
                                         F3 / FT3 / GT3 by them), u32 ?
                                    subgroup: u16 nfaces, u16 tpage
                                      face[nfaces] (14 bytes): u16 i0,i1,i2 ; u8 u0,v0,u1,v1,u2,v2 ; u16 clut
align 4
                            bone[n32]      40 bytes each
                            r12[n8b]       12 bytes each
                            r12b[m12]      12 bytes each
align 4
                            track[ntracks] (§3.4)
                            u32 list[hdr+0x28]   (e.g. entry 39: 12 bone indices)
recOff:                     definition record (rides.md §1.3)
```
Face fields are copied verbatim by the emitter: `lh 0/2/4(face)` → GTE RTPT of the three cached vertices,
`lh 6/8/10(face)` → u0v0/u1v1/u2v2 at prim+0xC/+0x14/+0x1C (FT3) or +0xC/+0x18/+0x24 (GT3),
`lh 12(face)` → **clut at prim+0xE**, subgroup `tpage` → prim+0x16 (FT3) / +0x1A (GT3)
(0x8001191C..0x8001193C and 0x80011C04..0x80011C24). Length byte 4 / 7 / 9 for F3 / FT3 / GT3.

### 3.3 The tpage / clut words are VRAM coordinates, sometimes rewritten at load
`clut` is the standard PSX id (`x = (clut & 0x3F) * 16`, `y = clut >> 6`; 0x8002E7B4 decodes it exactly
so). `tpage` is decoded at 0x8002E688 as `x = (t & 0x1F) * 64`, `y = (t & 0x10 ? 256 : 0) + (t & 0x800
? 512 : 0)`, bits 5–8 = the PSX tp/abr bits. When a texture bank is **paged** (§4.4), 0x8002E574 walks
every face of every mesh of the model, and for each subgroup whose `tpage` lies inside the bank's source
rectangle rewrites `tpage`, the six UV bytes and `clut` to the destination page. For an entry never
paged (all 261 attraction models, DERIVED: their tpages 0x18..0x1B are already inside the bank of §4.1)
the words in RAM equal the words on disc — that is the falsifier in §5.3.

### 3.4 Animation (SOURCED sizes, meaning DERIVED)
0x8002CBC4 composes `n32` GTE matrices per frame by interpolating between keyframes (36-byte pairs, MVMVA);
each track starts `{u8 type, u8, u16, u16 boneIndex, u16 count}` and its size is from the jump table
0x800DDD78: type 0 → 36·count+0x2C, 1/8 → 32·count+0x28, 2/4 → 8·count+0x10, 3/5 → 12·count+0x14,
6 → 20·count+0x1C, 7 → 16·count+0x18. Entry 39 ("The Dizzy Tree" per rides.md): 257 vertices, 262
faces in 4 subgroups (tpages 0x1B, 0x18, 0x1B, 0x0B), 55 bones, 55 tracks (types 6 and 8), 12-entry
trailing list. The per-handle sub-entry (`+0x34`, from `tab[phase]`) and frame (`+0x38`, timer>>12)
select the mesh and keyframe. **READ correction:** 0x8002FE24 saves a1 (sub-entry) into s1, and
0x8002FEE8 divides **s1** by nsub; a2 (frame) is held in s6. It is not `frame % nsub`.
The animator's own time modulo is by mesh+0, at 0x8002CC94.

### 3.5 Compressed sub-entries and the `f4 01 00 30` observation
Sub-entries with a non-zero second word are LZ-compressed; 0x800308C4 decompresses them once into a
cached handle with **0x800BFD9C(src, dst)** (bit-flag LZSS, ported in `f/sublz.py` and, from the
listing, in `core/TPW.Data/SubLz.cs` — 8 flag bits/byte LSB-first, 0 = literal; code ≥ 0x60 → 2 bytes
at distance 0x100−code, else 12-bit distance with length nibble+3 (nibble 5 → next byte+8), distance 0
= end; all 25 such
sub-entries — every one in entries 0000 and 0003 — expand to exactly their declared size and then parse
as meshes to the byte). `f4 01 00 30 …` at entry 0000 offset 0x49D0 is sub-entry 6's flag byte and first
literals; decompressed it begins `01 00 00 00 00 00 00 00 24 00 00 00` — a mesh header. Not a GP0 packet.
UNPAK (0x80018EF0, `unpak.py`) is a different scheme and is not used here.

## 4. Where the pixels are (SOURCED), and where they land in VRAM

Page ids everywhere use one encoding, 0x80029A04 / 0x8002FAD8 / 0x800314EC: **x = (id & 0xF) * 64,
y = (id >> 4) * 256** — the low 5 bits of a PSX tpage word.

### 4.1 Texture banks: the twelve 131,156-byte entries (6–15, 418, 419)
Loaded by 0x800282D0(obj, entry) — vtable 0x800DD038 — which copies the header into a "HDRS" allocation
and reads it as **u16 fields**:
```
+0  n12   (=3)   count of 12-byte records at +0x10        (0x80028340: n12*12, rounded to 4)
+2  ?     (=4)
+4  page  (=0x18)  destination page id → (512, 256)        (0x80028420)
+6  cols  (=4)   width in 64-halfword columns → 256 halfwords
+8  rows  (=1)   height in 256-row pages
+10 n8    (=4)   count of 8-byte records after the 12-byte ones  (0x80028360: n8*8)
+12 compressed (=0)                                        (0x80028130)
+14 ?     (=0)
+0x10 12-byte records ×n12 (raw: 98 00 60 40 00 00 a0 c8 01 02 00 00 …; first u16 = 0x98/0x9A/0x19 are
      tpage words inside this bank, the rest UNMEASURED)
+0x34 8-byte records ×n8  {s16 x, y, w, h} in 4bpp pixels; 0x80028444 converts them in place to VRAM
      halfword rects (x/4 + page x, y + page y) — the four here are {0..3, 202, 256, 53}
+0x54 pixel data = 0x20000 bytes = 256 halfwords × 256 rows, uploaded as ONE rect
```
Upload (0x80028254..0x80028288, `compressed == 0`): rect `{x=512, y=256, w=cols*64=256, h=rows*256=256}`
registered with 0x80027E70 against a sub-handle at +0x54 (0x800C08E0(handle, 0x54)), then 0x80027C98
performs the `LoadImage`. So VRAM (512..767, 256..511) = file bytes 0x54.. in row-major order, **512 bytes
per row**. The first row is 16 halfwords that look like a 4bpp CLUT at (512,256) — CLUT-aligned, and every
one of the twelve banks differs there (so a VRAM dump identifies which bank is up). Which of the twelve is
loaded when is not traced (the caller reaches 0x800282D0 through the vtable; theme-dependent, UNMEASURED).

### 4.2 Compressed banks: same header, `compressed == 1`
0x80028170..0x80028234: `n = cols*rows*4` blocks of **64×64 halfwords** (8 KB); block k is UNPAK'd
(0x80018EF0, the `unpak.py` scheme) into an 8 KB buffer and `LoadImage`d at
`(pagex + 64*((k>>2) % cols), pagey + 256*((k>>2) / cols) + 64*(k & 3))`. Entries with this header:
0005, 0082, 0084, 0168, 0258 (page 8 → (512,0), 4×1), 0017, 0018, 0091, 0092, 0169, 0170 (page 0xB →
(704,0), 1×2), 0269 (page 0xC → (768,0), 2×2), 0278 (page 8, 6×2 = the whole 512..895 strip). Whether the
block order and the UNPAK stream boundaries are right is UNMEASURED — nothing here was decoded end to
end; the 64×64 tiling is read off the loop, the entry list off the headers.

### 4.3 Streamed strips: entries 0x104 and 0x10C (0x20000 bytes, no header)
0x80032A5C(entry) builds a stream request `{ "FOLIO.GAZ", mgr, 0x20000, offset(entry), 0x800,
0x80032978 }` and hands it to the CD manager (`[mgr+1828]` PMF → 0x80024DA4). The callback 0x80032978
(buf, k*0x800) `LoadImage`s each **2048-byte sector as a 64×16 halfword block**: with `g = k >> 4` it sets
`x = 768 + 64*(g' & 1)`, `y = 16*k + 256*(g' >> 1)` where g' permutes g as 0→0, 1→3, 2→2, 3→1. Because
`16*k` already carries the group, y exceeds 511 for g ≥ 1 and the GPU's 9-bit y wrap is what makes it
land (DERIVED — the permutation is exactly what cancels the double counting): final positions are
**g=0 → (768, 0..255), g=1 → (832, 0..255), g=2 → (768, 256..511), g=3 → (832, 256..511)**, i.e. tpages
0x0C, 0x0D, 0x1C, 0x1D. File offset of VRAM (x,y): `sector = 16*(2*(y>=256) + (x>=832)) + (y&255)/16`,
`byte = sector*2048 + (y&15)*128 + (x - 768 - 64*(x>=832))*2`.
Chosen by 0x80053C90(newMode): argument 0 → 0x10C; argument 1 → 0x104 unless the previous mode word
`[0x80103840]` was 2 or 3; 2 and 3 → nothing. Callers pass 0 at 0x80023298 (start), 1 at 0x80054DB8,
0x8005538C, 0x8005904C, 0x80059570, 0x800A197C, 2 at 0x80058FE4.

### 4.4 The paging system for model textures (mechanism SOURCED, feed not traced)
0x8002F7E4(res, data, srcPage, cols, rows) — resource-object vtable 0x800DDEC8, no direct caller —
takes one of **ten 64×128-halfword slots** from the static table 0x800F10B4 `{page, yOffset, inUse}`:
(0x1B,0), (0x1B,128), (0x1C,0), (0x1C,128), (0x1D,0), (0x1D,128), (0x1E,0), (0x1E,128), (0x1F,0),
(0x1F,128) → the strip **(704..1023, 256..511)**; uploads `data` there as a 64×128 rect, then relocates
every mesh of the model whose tpage falls in `{srcPage, cols*64, rows*256}` (§3.3). What calls it and
with which entry's bytes is UNMEASURED; the slot table alone is enough to recognise its output in a dump.

### 4.5 Two more consumers, for completeness
- **Sprite sheets** (0x80032118, vtable 0x800DFFB8): 0x8002B63C uploads a 4bpp record `{…, u8 w @6, u8 h
  @7}` (w/4 halfwords), 0x8002B6D8 uploads a CLUT stored as `u16 count` + colours as a `count×1` rect.
  Which entries: UNMEASURED (candidates: the `0x0092`-headed family 0017/0018/0091/0092/0169/0170 —
  no, those are §4.2; more likely the `0x8`- and `0x2`-headed 0x14C/0x2C8-byte entries 0262–0275).
- **Compact meshes** for buildings, the gate and the bus (renderer 0x80035D04, entry via 0x80035358 —
  transport.md's "vehicle mesh"): container `{u8, u8 count, u16 off[count]}`; mesh `{u16 scale, u16 ntri,
  u16 nquad, u8, u8 nmat, s8 pos[4], +0xC: mat[nmat] {u16 tpage, u16 clut, u16 flags} (6 B), tri[ntri]
  {u8 i0,i1,i2, u8 mat, u16 uv×3} (10 B), quad[nquad] (14 B), vertices s8 x,y,z,pad ×4}`. tpage/clut here
  are final VRAM ids (0x80035F28/0x80035F3C copy them into the primitive). Entries 0036, 0118, 0205 (20
  sub-meshes each = the theme building sets, DERIVED) and 0085–0090.

## 5. Falsifiers — one VRAM dump each

1. **Bank (§4.1).** In any park scene, VRAM halfwords (512..527, 256) must equal the 16 halfwords at file
   offset 0x54 of one of the twelve banks. For entry 6: `8c64 992a a18d 8971 a5f1 b254 91f5 cab6 9a57
   a278 aed9 a6ba bb1b b2fb bf3c c35d`; row 257 starts `8422 8618 b210 80f0`. Any of the other eleven is
   equally fine — they all differ in that row. If (512,256) holds `0003 0004 0018 …` instead, the header
   is uploaded too and the sub-handle reading is wrong; if it holds neither, §4.1 is wrong.
2. **Strip (§4.3).** After the first mode-0 load, (768..771, 0) = `0000 8885 c1ef 8133` (entry 0x10C) and
   (768..771, 15) = `0000 8001 c5ec 8d64`; sector 53 must be at **(832..835, 336)** = `6c30 ccc6 f60c
   f361` — that last one tests the group permutation and the y-wrap claim. With 0x104 instead: (768..771,
   0) = `0000 8001 a1f3 bcc3`, sector 49 at (832..835, 272) = `5000 bccc bbbb 00bb`.
3. **Geometry (§3).** Entry 19 in RAM (find `96 00 00 00 01 00 00 00 … 00 03 00 00 34 03 00 00`): vertices
   at +0x80 (19 × 8, first `54 00 00 01 8e 00 00 00`), colours at +0x118, block at +0x164, subgroup
   header at +0x174 = `1c 00 19 00`, face 0 at +0x178 = `00 00 01 00 06 00 43 57 48 57 47 4d e1 14`, last
   face ends at +0x300 = recOff. If the words at +0x176/+0x184 are not 0x0019/0x14E1 the model was paged
   (§4.4) and the new values name the slot. A RAM dump that shows a different byte at any of those
   offsets falsifies the layout.
4. **Reader (§1).** Break on CdSearchFile: the first call with `\FOLIO.GAZ;1` or `\folio.gaz;1` comes from
   0x800249D8; the cache entry then holds `CdPosToInt` = 174726 at +20. A hardcoded 0x2AA46 would have
   shown up in the image; it does not.

## 6. Corrections to earlier notes
- rides.md §1.3 "word 0 = 422 entries, then (offset,size) pairs": the pairs start at **+8**; word 1 is
  0x17. rides.md's record layout itself is unchanged and its `recOff` is this report's +0x14.
- rides.md §2 "+0x34/+0x38 current frame/mesh": +0x34 is `tab[phase]` (a sub-entry index), +0x38 is the
  frame counter; the mesh pointer is recomputed each draw by 0x8002FE14.
- The prompt's "FOLIO.GAZ is NOT in TPW.BIN": it is, at 0x800DFF84 (and lowercase at two more places).
