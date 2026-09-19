# Theme Park World (SLES-026.88) — the audio system

Eleventh report. Addresses are in the raw image (`TPW.BIN` at 0x80010000). Tags: **SOURCED** = read off
instructions or bytes at the quoted address / file offset. **MEASURED** = checked by a script in `fable/au/`
against every relevant byte on the disc. **INFERRED** = reasoning from sourced facts, basis stated, with a
falsifier. Nothing is transcribed from a reference without being checked against the binary; where a PsyQ
constant matters (ADSR encoding, reverb work area, sector mode bits) the libspu/libcd code in this image
was read rather than the manual. Tools and dumps live in `fable/au/` (see its README).

## 0. Answers, shortest form

| # | question | answer | tag |
|---|---|---|---|
| 1 | the .STR files | **Sony MDEC movies, not audio banks.** Every sector is MODE2 **FORM1** (submode 0x48) carrying a standard STR video chunk header (`0x0160, 0x8001, chunk, 11, frame, size, 320, 176`), except every 8th sector (7, 15, 23, …) which is **FORM2 XA-ADPCM 37800 Hz stereo 4-bit** (submode 0x64, coding 0x01), file 1 channel 1. ENGLISH/FRENCH/SPANISH/END are the same movie with different burned-in video and **byte-identical audio**; BF is the Bullfrog logo; GRAV/MIR/JUG are the title-screen attract movies | MEASURED §1 |
| 2 | sound-effect dispatch | one driver, 0x800B7A90–0x800B9B80, over libspu 4.x. `PlaySfx(bank, sample)` = 0x800B8E08; the sample is looked up in an 8-byte record `{u16 loop, u16 pitch, u32 offset}` in the bank's *header* FOLIO entry, and played from `slot.spuAddr + offset` on voices **10–23**. 157 call sites resolved in §3.6 | SOURCED §3 |
| 3 | the sample bank | 12 banks = 12 pairs of FOLIO entries (body, header) listed at **0x800F91D0**; 132 SFX waveforms; **base pitch is in the record**: 0x0400 → 11025 Hz or 0x02E7 → 8000 Hz, nothing else. The 193 music waveforms live in 9 VB entries whose base rates come from the XM sample headers (8363 Hz × 2^(relnote/12)) | SOURCED + MEASURED §3.2, §5.3 |
| 4 | music | **FastTracker II .XM modules** played by an XM player linked into the image (heap tags `XMSong`/`XMHeader`), 9 songs as (VB, VH, XM) FOLIO triples; the world index `[0x801038A0]` picks one of four at 0x80058694, the front end plays a fifth. XM voices are SPU 0–9 | SOURCED §5 |
| 5 | ADVISOR.TPW | **XA-ADPCM 18900 Hz mono 4-bit, 32 interleaved channels** = 4 clip slots × 8 languages. 452 clips indexed by FOLIO entry **405** (`{u32 sector, u8 n[8]}` ×452); channel = `lang + 8·(clip & 3)`; 289 advisor messages map to up to 3 clips each in the table at **0x800EE4FC** | SOURCED + MEASURED §2 |
| 6 | volume / reverb | master 0x3FFF/0x3FFF at init then `vol<<6`; reverb **SPACE** (mode 6) at depth 0x1000/0x1000, switched on, work area 0x70940–0x7FFFF, **but no voice is ever routed into it** (EON = 0); CD mix 0x7F on all four paths while XA or a movie plays, 0 otherwise; SFX voices have **ADSR 0x0000/0x0000** (never written), XM voices 0x100F/0x000C | SOURCED §6 |

Two corrections to `measured.md` fall out of this (§8): the idle pitch is 0x3FFF, not 0x4000, and the
"22050 Hz plus 1.1 %" readings are tracker notes on voices 0–9, not sound effects.

## 1. The .STR files (MEASURED, `au/secscan.py`)

### 1.1 Sector layout
Raw 2352-byte sectors were read from the .bin at each file's LBA and the eight-byte subheader decoded
(file, channel, submode, coding; the copy at +4 always matched).

| file | sectors | FORM1 video (0x48) | FORM2 audio (0x64, coding 0x01) | empty (submode 0) | frames | last sector |
|---|---|---|---|---|---|---|
| BF.STR | 1432 | 1247 | 178 + 1 | 6 | 114 | 0xE4 = audio + EOR + EOF |
| END.STR | 1963 | 1717 | 152 | 93 | 157 | 0xC8 = video + EOF |
| ENGLISH.STR | 1963 | 1717 | 152 | 93 | 157 | 0xC8 |
| FRENCH.STR | 1963 | 1717 | 152 | 93 | 157 | 0xC8 |
| SPANISH.STR | 1963 | 1717 | 152 | 93 | 157 | 0xC8 |
| GRAV.STR | 5928 | 5185 | 740 + 1 | 2 | 474 | 0xE4 |
| JUG.STR | 5744 | 5021 | 717 + 1 | 5 | 459 | 0xE4 |
| MIR.STR | 6856 | 5994 | 856 + 1 | 5 | 548 | 0xE4 |

- Audio sectors sit at index 7 mod 8 in every file (`[7, 15, 23, …]`, period 8 throughout). Coding 0x01 =
  bits 0-1 `01` stereo, bits 2-3 `00` 37800 Hz, bits 4-5 `00` 4-bit. At 2× (150 sectors/s) one stereo
  37800 Hz sector covers 8 sector-times, so 1-in-8 is exactly real time.
- Video sector user data starts with the Sony STR chunk header: `u16 0x0160, u16 0x8001, u16 chunk,
  u16 nchunks, u32 frame, u32 frameBytes, u16 320, u16 176, …`. 11 chunks per frame (10 on 70/90/290/280/340
  frames), frames numbered from 1. So a frame is ~12.5 sectors → **INFERRED 12 fps at 2×**; ffmpeg's
  "15 fps" is its demuxer default, not a measurement. Falsifier: count VSyncs between MDEC frame flips.
- The four "language" files share audio: END vs ENGLISH vs FRENCH vs SPANISH audio sectors identical
  **152/152** in every pairing; video sectors identical only 675–704/1718 (burned-in text differs).
  Decoded frames (ffmpeg `psxstr`/`mdec`, montage in `/tmp/str_montage.png` when this ran): BF = the red
  Bullfrog logo; END/ENGLISH/FRENCH = an in-engine park-gate cutscene; MIR = haunted house + pumpkins
  (Halloween world); JUG = tiki statue + explorer (Lost Kingdom / jungle); GRAV = a candy-coloured control
  panel (Wonderland or Space — not identified from two frames).
- INTRO.STR is named in the image (0x800F1F04) but is not on the disc; it is record 0 of the 28-byte
  movie-record array at 0x800F1F04 (`+16 u16 frames`, `+24 u32 LBA`, filled at runtime by 0x80037634).

### 1.2 Who plays which movie (SOURCED)
Movie descriptors are `{char* name, u32 lastFrame}` — `lastFrame` = frame count − 1 (MEASURED 5/5):

| descriptor | name | lastFrame | used by |
|---|---|---|---|
| 0x801034A8 | BF.STR | 0x71 = 113 | front-end state machine 0x800BCEEC at 0x800BD068 (boot logo) |
| 0x801034B8 | END.STR | 0x9C = 156 | 0x800BCD00 when language = 7 |
| 0x801034C0 | ENGLISH.STR | 0x9C | 0x800BCD00, language 0, 2, 3, 5, 6 |
| 0x801034C8 | FRENCH.STR | 0x9C | 0x800BCD00, language 1 |
| 0x801034D0 | SPANISH.STR | 0x9C | 0x800BCD00, language 4 |
| 0x800E72E4 | GRAV.STR | 0x1D9 = 473 | attract cycler 0x800BCD7C, index `[0x80103A70]` = 0 |
| 0x800E72EC | MIR.STR | 0x223 = 547 | index 1 |
| 0x800E72F4 | JUG.STR | 0x1CA = 458 | index 2 (then wraps to 0) |

0x800BCD00 reads the language with 0x800BE724 (`lhu 0x1450(gp)` = 0x80103AA4) and jumps through
0x800E7300. The language index is written by 0x800BE6DC(lang), whose caller 0x800BC93C takes it from the
overlay function 0x8011C9F0 (the language menu). So 0 = English, 1 = French, 4 = Spanish are SOURCED;
which of 2/3/5/6 are German/Italian/Dutch/Swedish is not, and 7 is a slot with no text movie of its own.

Playback (0x800BCC3C → CdSearchFile via 0x8001836C → 0x80037634 → 0x80036DEC): three `"FMV"` heap
buffers (0x10000, 0x29400, 0x11000), `DecDCTvlcBuild`, `CdMix` with all four gains 0x7F (0x80037604),
`StSetRing(buf, 32 sectors)`, `StSetStream(1, 1, −1, …)`, then 0x8003757C: `CdControl(CdlSeekL)` +
`CdRead2(0x1C0)` = `CdlModeStream | CdlModeSpeed | CdlModeRT` — 2×, XA-ADPCM routed to the SPU, **no
sector filter** (there is only channel 1). On exit the movie player calls 0x800375C4 = `CdMix(0)`.

### 1.3 Falsifiers for §1
- Raw sector `LBA 169368 + 7` (BF.STR) has subheader `01 01 64 01 01 01 64 01`; `+0` has
  `01 01 48 00 …` and user data `60 01 01 80 00 00 0B 00 01 00 00 00 54 12 00 00`.
- During the boot logo the CD mode register (`CdlSetmode` argument) is 0xC0|0x100 via CdRead2; SPU
  register 0x1F801DB0/0x1F801DB2 (CD input volume) are whatever libcd's `CdMix(0x7F)` maps to and drop to
  0 after the movie.

## 2. ADVISOR.TPW — the advisor's speech (SOURCED + MEASURED, `au/advscan.py`, `au/advverify.py`, `au/xadec.py`)

### 2.1 What is on the disc (MEASURED over all 169,344 sectors)
- Every sector is MODE2 **FORM2**, file 1. 122,071 sectors have submode **0x64** (audio + real-time) and
  coding **0x04** = bits 2-3 `01` → **18900 Hz**, bits 0-1 `00` → **mono**, bits 4-5 `00` → **4-bit**;
  their channel byte equals `sector mod 32` without exception (0 sequence breaks among them).
- 47,240 sectors have channel **0xFF** and coding 0 — filler that no filter ever matches.
- 32 sectors are FORM1 data (submode 0x48, coding 0x80) and one is 0xE4 (audio + EOR + EOF, the last).
  The 32 data sectors are the trailing two sectors of 16 early streams (e.g. sectors 785/817 = channel
  17 = clip 2 language 1); they are the only sectors in the file that can raise a data-ready interrupt
  under a channel filter (§2.4).
- The XA sound-group headers are well formed (parameter bytes duplicated at +4/+12) and decode with a
  plain XA-ADPCM decoder to speech (`au/xadec.py`; per-sector RMS shows phrases separated by silences).
- 18900 Hz mono 4-bit needs 1 sector in 32 at 2×, so 32 channels is the densest possible interleave: the
  file is **32 simultaneous streams**, each ~3,400–4,200 sectors (12–15 minutes).

### 2.2 The index: FOLIO entry 405
0x800184AC loads FOLIO entry **0x195 = 405** (5,424 bytes) into the handle at 0x8010269C and finds the
file with `CdSearchFile("\ADVISOR.TPW;1")` (0x8001836C, string 0x800DBE48) → LBA in 0x80102698. The
entry is **452 records × 12 bytes** `{u32 startSector, u8 n[8]}` (`au/advisor_index.tsv`):
- 0x800188B0(clip) = `rec.startSector + LBA` (`lw 0(rec)`), 0x80018904(clip, lang) = `rec.n[lang]`
  (`lbu 4+lang(rec)`).
- Start sectors are monotonic; inside a group of four consecutive clips they step by 8 (0, 8, 16, 24),
  between groups by ≥ 1064. 113 groups. A group is 32 streams = 4 clips × 8 language channels; the
  stream for clip `k`, language `l` is channel **`l + 8·(k & 3)`** — computed at 0x80018590..0x800185BC
  (`(clip & 3) << 3` plus `[0x8010267C]`) and confirmed against the disc: walking `start + l` in steps
  of 32 always lands on that channel until the filler begins.
- The 8 bytes are per language slot (the same `lang` that selects the ending movie). Slots 0–6 are the
  seven boxed languages; slot 7 has its own audio too (channels 7/15/23/31 hold ~4,000 sectors each).
- Clips 442–451 are three-sector stubs in slots 0–6; clip 444 is identical across all eight slots
  (a non-verbal sound). 438 distinct clips are referenced by the message table; 14 are not.

### 2.3 Which message plays which clip: 0x800EE4FC
0x80013EFC(advisor, msg) reads record `msg.id` from a table at **0x800EE4FC**, 20 bytes each:
`{u16 textId (0x124 = none), u8 nVariants, u8 next, {u16 clip, u16 x}[4]}`. It takes variant `next`,
advances `next` modulo `nVariants` (0x80013FE8..0x80014008), stops any running clip (0x80018840) and
calls 0x8001853C(advisor+0x1B8, clip). **289 records** (the 290th is garbage), up to 3 variants each,
clip ids ≤ 451. The second u16 of a variant takes only the values 6–16 and is the same for all variants
of a message — INFERRED an advisor animation/mood id; falsifier: it should index whatever 0x80014024
(`lbu 6(rec)`, stored at obj+388) drives. Full table: `au/advisor_messages.tsv`. Message ids are the
advisor message numbers the other reports already use (rides.md's 0x3E/0x3F "about to break down" etc.),
so *event → message id → clip* closes through this table.

### 2.4 The playback path (SOURCED, `au/xa_module.txt`)
0x8001853C(obj, clip): `CdSync` until idle (0x80018324), `channel = lang + 8·(clip&3)` → 0x80103762,
`count = rec.n[lang]` → 0x80103760, `CdMix(0x7F ×4)` (0x800375E4), then a **CdSyncCallback-driven state
machine** (0x8001862C, jump table 0x800DBE58):
1. `CdlSetmode` **0xE8** = Speed | RT (XA to SPU) | Size1 (2340-byte sectors) | **SF** (sector filter).
2. state 0: `CdlSetfilter {file 1, chan}`; state 1: `CdReadyCallback(0x80018A38)`, `CdlReadN` at
   `CdIntToPos(start)`.
3. data-ready callback 0x800187A8: `CdGetSector(0x80103B48, 0x200 words)`, `count--`; at zero:
   `CdMix(0)`, `CdlPause`, then `CdlSetmode 0x80` and finish. 0x80018840 is the stop entry (pause + mute).

**What `n[lang]` means is not established.** It is used as "number of data-ready interrupts before
pausing", but it does not equal the stream length: clip 0 language 0 has 82 sectors of speech and n = 19;
clip 4 language 2 has 28 and n = 33; clip 448 language 7 has 13 and n = 61 (`au/advverify.py`, all 452×8
checked, 0 matches). And under the rule the emulator implements (`pcsx_rearmed/libpcsxcore/cdrom.c`
around line 1480: with SF set, any audio+RT sector yields **no INT1**, "according to nocash"), the counter
never decrements during a clip at all, so the pause is never issued by this path. So one of these holds:
(a) hardware does deliver INT1 per filtered sector and `n` is a count in some unit I have not found, or
(b) the countdown is dead and clips end only when the message box calls 0x80018840, otherwise the drive
keeps reading filler and, ~40 s later, plays the next clip on that channel. Experiments, in order of
cheapness: **(i)** log every `CdControl` during one advisor line — expect `Setfilter(1, lang+8·(clip&3))`,
`ReadN` at `LBA 24 + rec.start`, and note whether a `Pause` arrives without the game's stop call; **(ii)**
count data-ready callbacks (breakpoint 0x800187A8) per clip — 0 confirms (b); **(iii)** leave a message
open a minute with the sound on and listen for a second, unrelated line.

### 2.5 Falsifiers for §2
- Raw sector `LBA 24 + 0` subheader `01 00 64 04 01 00 64 04`; `LBA 24 + 33` has channel 1.
- RAM: after boot `[0x8010269C]` is a handle whose locked buffer begins `00 00 00 00 13 10 15 25 29 1B 19 3F`
  (clip 0: start 0, n = 19,16,21,37,41,27,25,63).
- While any advisor line plays, SPU registers 0x1F801DB0/0x1F801DB2 (CD volume) are non-zero and the
  emulator's XA decoder is fed 18900 Hz mono; `[0x80103762]` = `lang + 8·(clip&3)`.

## 3. Sound effects (SOURCED, `au/spu_driver.txt`, `au/spu_driver2.txt`, `au/sfx_api.txt`)

### 3.1 Library and module map
PsyQ **libspu 4.x** is linked (signature matches at every 4.1–4.7 version; `SpuSetVoiceAttr` 0x800D3428,
`SpuSetKey` 0x800D2BE8, `SpuMalloc` 0x800D2E18, `SpuFree` 0x800D3158, `SpuSetTransferStartAddr`
0x800CDB1C, `SpuWrite` 0x800D2A08 — matched as "SpuRead" by the signature tool but it calls `_spu_Fw`
0x800D9744, the *write* helper — `SpuWrite0` 0x800D2A68, `SpuSetReverbModeParam` 0x800D2528,
`SpuSetReverbDepth` 0x800D31D8, `SpuSetReverbVoice` 0x800D3258, `SpuSetReverb` 0x800D3288,
`SpuSetCommonMasterVolume` 0x800D3138, `SpuInit` 0x800D3358 → `_SpuInit` 0x800D99C0, `_spu_init`
0x800D8FC8). **No libsnd**: the two "SsInit" hits are 32-byte false positives (0x800375C4 is `CdMix(0)`).

| function | role |
|---|---|
| 0x800B8A10 | audio init: `SpuInit`, master 0x3FFF, XM once-off init 0x800CE1C0(0), registers per-frame callbacks 0x800CDDBC (XM) and 0x800B7D90 (SFX) through 0x800BB0E0 (4-slot list 0x8010AE4C), 0x800B88DC, volume bytes 0..4 = 0xFF, allocates `XMSong` (0xBB0 B) and `XMHeader` (0xF1C B) |
| 0x800B88DC | SFX/reverb init: clears voice tables, frees 8 bank slots, `SpuSetTransferMode(0)`, `SpuWrite0(0x80000)` (zero all SPU RAM), `SpuInitMalloc(38, 0x8010A3FC)`, master 0x3FFF/0x3FFF, reverb (§6) |
| 0x800B8B80(i) | load bank i: `{body, hdr}` from 0x800F91D0 → 0x800B804C, slot index → 0x800F9230[i] |
| 0x800B804C(body, hdr) | find/allocate one of **8 slots** at 0x8010A33C (0x18 B each: `s16 entry, u32 used, u16 refs, u32 hdrHandle, u32 spuAddr, u16 nSamples`); loads body via 0x800BBE78, `SpuMalloc(size)`, `SpuSetTransferMode(1)`, `SpuSetTransferStartAddr`, `SpuWrite`, `SpuIsTransferCompleted(1)`; loads hdr, `nSamples = hdrSize >> 3` |
| 0x800B8BD4(i) / 0x800B8230 | free bank i (refcount, `SpuFree`) |
| 0x800B84AC | the play primitive (§3.3) |
| 0x800B8300 / 0x800B83B8 / 0x800B8438 | voice allocate / release by handle / find by (bank, sample) |
| 0x800B7D90 | per-frame voice service (§3.4; `funcs.py` cuts it at 0x800B7D98, the real entry is two loads earlier) |
| 0x800B8C2C | 3D attenuation (§3.5) |
| 0x800B7CE4(cat, v) / 0x800B7D7C(cat) | volume category set/get (§6) |
| 0x800B7A90 / 0x800B7C10 / 0x800B7C6C | music start / stop / volume (§5) |

### 3.2 The banks and the sample record
`0x800F91D0`: `{s32 bodyEntry, s32 hdrEntry}[12]` (SOURCED; the loader multiplies the index by 8).
The header entry is `nSamples × 8` bytes of **`{u16 loop, u16 pitch, u32 offset}`**; the body is the
concatenation of the ADPCM streams. MEASURED over all 12 banks (`au/banks.py`, `au/banks.txt`,
`au/banks.json`): 24,999 ADPCM blocks, 0 invalid headers; every record's declared stream ends on a
block with the end flag (132/132); `loop == 1` exactly when the stream carries ADPCM loop-start bits
(132/132); every body ends 48 bytes (three blocks) after its last record.

| bank | body | hdr | samples | pitch/rate | loop | notes |
|---|---|---|---|---|---|---|
| 0 | 279 | 280 | 5 | 0x400 / 11025 | all | never loaded by any code found |
| 1 | 281 | 282 | 26 | 0x2E7 / 8000 | none | people (3D, from the person state machines) |
| 2 | 283 | 284 | 4 | 0x400 | none | |
| 3 | 285 | 286 | 6 | 3× 8000, 3× 11025 | 3 | never loaded |
| 4 | 287 | 288 | 9 | mixed | 8 | never loaded |
| 5 | 289 | 290 | 7 | 0x400 | none | UI (front end and in park) |
| 6 | 318 | 319 | 4 | 0x400 | none | |
| 7 | 320 | 321 | 8 | 0x400 | none | UI/menu cluster 0x8001Cxxx–0x80022xxx |
| 8 | 322 | 323 | 29 | 2× 8000, rest 11025 | #12, #13 | rides / park events |
| 9 | 324 | 325 | 8 | mixed | 5 | never loaded |
| 10 | 326 | 327 | 12 | 4× 8000, 8× 11025 | none | |
| 11 | 330 | 331 | 14 | 0x400 | #0–#4 | rides (3D, 0x800AAxxx) |

**The base rate is `pitch`, and only two values occur**: 0x0400 (= 11025 Hz, since 0x400/0x1000 ×
44100) on 108 samples and 0x02E7 (= 7999.6 Hz) on 24. Per-sample lengths and seconds are in
`au/banks.txt`. In-park load list (0x800F23CC, 8 × u16, loaded by 0x80058694 and freed by 0x80058754):
**[1, 10, 11, 7, 2, 6, 5, 8]** — 306,192 bytes. The other park-like mode 0x800383EC loads 7, 2, 5, 6;
the front end (0x800BCE14) loads 5. Banks 0, 3, 4, 9 — the loop-heavy ones — are loaded by no `jal`
in the main image or in the twelve overlays (INFERRED: leftovers; falsifier: the slot table at
0x8010A33C never holds entries 279/285/287/324).

### 3.3 The play primitive 0x800B84AC(slot, sample, vol, pan, pitchOverride, priority)
- Rejects `slot ≥ 8`, `sample < 0`, `sample ≥ nSamples`. `vol = vol × [0x801033FF] >> 8` (the SFX
  volume byte, category 3 of §6).
- Voice = 0x800B8300(priority): first voice in **10..23** whose handle (0x8010AB5C[v]) is −1; otherwise
  the voice with the lowest priority byte (0x8010ABBC[v]) if that is `< priority`, which is released
  first; otherwise −1 (no sound).
- Record `rec = hdr + 8·sample`. Builds a `SpuVoiceAttr` on the stack and copies it to
  **0x8010A55C + 0x40·voice**: `voice = 1 << v`; **`mask = 0x93`** = VOLL | VOLR | PITCH | WDSA;
  `volume.left = ((0x10000 − pan) × vol) >> 16`, `volume.right = (pan × vol) >> 16`;
  `pitch = pitchOverride ? pitchOverride : rec.pitch`; `addr = slot.spuAddr + rec.offset`.
- `rec.loop` sets/clears bit v of 0x801033D0 (voices exempt from auto-release). Priority byte, sample,
  bank, and a **12-bit handle** (counter 0x801033E8) are stored per voice; bit v of 0x801033C8 marks it
  "key-on pending"; the handle is returned.
- Nothing ever writes ADSR for these voices. `SpuInit` leaves every voice at `volL 0, volR 0, pitch
  0x3FFF, addr 0x200, ADSR1 0, ADSR2 0` (0x800D9160..0x800D918C), so **SFX play with ADSR 0x0000/0x0000**
  = instant attack, sustain at full level, instant release — a sample plays flat until it ends.

### 3.4 Per-frame service 0x800B7D90 (registered by 0x800BB0E0 at 0x800B8A80, runs once per frame)
1. Voices flagged in 0x801033CC (parameter change from 0x800B87D8) → `SpuSetVoiceAttr`.
2. `SpuGetVoiceEnvelope` for 10..23. A voice with a live handle, not pending, not looping, whose
   envelope reads **0 on two consecutive frames** (bit v of 0x801033D8 is the "seen once" flag) is put in
   the key-off mask 0x801033D4. Active count → 0x801033F8.
3. Key-off mask → `SpuSetKey(0, mask)`; those voices' handle/sample/bank reset to −1.
4. Pending mask → `SpuSetVoiceAttr` each, then **one** `SpuSetKey(1, mask)`; `SpuFlush`.
So sounds requested in the same frame start together, and a looping sample (bank 8 #12/#13, bank 11
#0–#4) plays until 0x800B8F18(handle) or 0x800B8F38 (stop all) keys it off.

### 3.5 The API the game calls
| entry | args | behaviour |
|---|---|---|
| **PlaySfx** 0x800B8E08 | (bankIndex, sample) | 0x800B84AC(slot[bank], sample, 0x3FFF, 0x8000, 0, 0xFF) |
| **PlaySfxOnce** 0x800B8E4C | (bankIndex, sample) | if a voice already holds (bank, sample) return its handle, else PlaySfx (0x800B8740) |
| **PlaySfx3D** 0x800B8E90 | (bankIndex, sample, pos) | 0x800B8C2C(pos): `d = sqrt(((x − camX) >> 4)² + ((z − camZ) >> 4)²)` (camera from 0x80053F10, shift `[0x801033F4]` = 4), `s = max(0, 255 − d)`; vol = 0x3FFF·s >> 8, **priority = 0xFF·s >> 8**, pan **0x8000 always** (no stereo placement) |
| **Update3D** 0x800B8D78 | (handle, pos) | same attenuation, applied through 0x800B87D8 (mask 0x13, no pitch change) |
| **PlayRandomBank1** 0x800B8FA0 | () | PlaySfx(1, rand(3)) |
| StopHandle 0x800B8F18 / StopAll 0x800B8F38 / IsPlaying 0x800B8D3C | | |

### 3.6 Dispatch: event → (bank, sample) (SOURCED call sites, `au/sfx_sites.txt`)
157 call sites; the constant arguments were resolved by walking back from each `jal` (variable ones are
marked). Names come from the earlier reports where the enclosing function is already identified.

| bank:sample | where | event |
|---|---|---|
| 5:6 | 0x80041408, 0x80048CDC, 0x8004B9EC, 0x8004C3E4, 0x8004C84C, 0x800760BC, 0x80079300, 0x8007A880, 0x8007B188, 0x8007C58C, 0x8007F58C, 0x80080380, 0x80082500, 0x80084530, 0x80020F68 (29 sites, always in pairs) | the generic window/button click — 0x80084530 is the Game Options window, 0x8007A880/0x80080380 are the loan/economy windows (economy.md), 0x8007D554 (5:4) staff confirm (wages.md) |
| 5:3 / 5:4 / 5:5 | PlaySfxOnce at 0x800385D0, 0x80041408, 0x800419D8, 0x80041A68, 0x80048CDC, 0x8004B9EC, 0x8007F58C, 0x80083C50, 0x800857B4 (hiring, economy.md), 0x800193A4 (placement tool, wages.md) | tool pick-up / place / cancel |
| 5:0, 5:1, 5:2 | 0x80038888, 0x80074178, 0x80073EF4 (after StopAll) | park-mode transitions |
| 6:0 / 6:1 / 6:3 | 0x80039D1C, 0x80039DC4, 0x80039DFC, 0x8003B878, 0x8003BF08 | |
| 7:0–7:7, 8:3, 8:6, 8:9 | the menu cluster 0x8001C5C8–0x80022DCC (31 sites) | front-end/menu navigation; 7:2 is the most common (back/cancel) |
| 8:0, 8:1, 8:10 | 0x8009C730 (ride "about to break down", rides.md), 0x8009C7A8 (ride broken down), 0x8009C56C (servicing done), 0x8006E22C | ride state changes |
| 8:5 | 0x800510C0 | |
| 8:var | 0x800962E8 PlaySfx3D, sample = `table[rand % n]` | ride running loop (rides.md) |
| 1:15 / 1:16 / 1:17 / 1:19 / 1:22 / 1:24 | PlaySfx3D from the person states: 0x8008D058 idle (17, 24), 0x8008DB34 walk/arrive (16, 22), 0x8008E240 decide (19), 0x8008FE60 vomit (22), 0x800906EC queue (15); variable at 0x8008FC68/0x8008FD64 (happiness reactions, behaviour.md "sound 0x12") | guest vocalisations; base 8000 Hz |
| 1:rand(3) | PlayRandomBank1 at 0x8001E6BC (ghost commit, wages.md), 0x80079A1C | |
| 11:4, 11:var | PlaySfx3D at 0x800AA290, 0x800AA704 (with IsPlaying), 0x800AAF90; Update3D at 0x800AB1C8 | ride ambient loops (11:0–11:4 are the looping ones) |
| 10:4, 10:7 | 0x800B9A60, 0x800B9B64 | |
| var:2 | 0x8001CCA8, 0x8001CE18, 0x8001CFC8, 0x8001E720, 0x8001E994, 0x8001F038, 0x8001FF30, 0x80021E48, 0x800225C0, 0x80022C9C | bank chosen at runtime (a menu-page register), sample 2 |

Falsifier for the whole of §3: while a UI click plays, exactly one of voices 10–23 has `pitch = 0x0400`,
`volL = volR = 0x1FE0` (0x3FFF × 255 >> 8 = 0x3FC0, halved by pan 0x8000), `ADSR1 = ADSR2 = 0`, and
`addr` = the bank-5 slot base + 0x3AC0 >> 3 for sample 6; the key-on register write happens on the frame
after the request.

## 4. SPU RAM (SOURCED)
- `SpuWrite0(0x80000)` zeroes all 512 KB at init; `SpuInitMalloc(38, 0x8010A3FC)` manages the area from
  **0x1010** (libspu's reserved 4 KB + 16) up to the reverb work area.
- Reverb mode 6 (SPACE) → work area start **0xE128 × 8 = 0x70940**, 63,168 bytes to 0x7FFFF (table
  0x80101B40 indexed by mode, read at 0x800D25FC..0x800D2620). So 461,120 − 4,112 = 457,008 bytes are
  allocatable.
- Each SFX bank body is one `SpuMalloc(bodySize)` block at `slot.spuAddr`; samples are addressed as
  `spuAddr + rec.offset` (every offset is a multiple of 16).
- Music: 0x800D1B74(vh, vb) does one `SpuMalloc` + `SpuWrite` **per VAG** (sizes from the VH's VAG
  table at `0x822 + 2·i`, ×8) and records the SPU address in 0x80112BB0 + 512·vab + 4·i; VAGs of size 0
  get address 0.
- Budget (INFERRED arithmetic): in-park banks 306,192 + the largest VB 136,368 = 442,560 < 457,008, so
  everything fits with ~14 KB to spare; a world's music is freed (0x800B7C10 → 0x800CDD6C → SpuFree)
  before the next is loaded.
- Falsifiers: SPU register **0x1F801DA2 = 0xE128** after init; a SPU RAM dump contains FOLIO entry
  289's 16,224 bytes verbatim (bank 5) and the bytes at 0x1010.. are the first bank loaded.

## 5. Music (SOURCED, `au/xm_entry.txt`, `au/banks.py`, `au/xminst.py`)

### 5.1 The songs
Nine triples of consecutive FOLIO entries **(VB body, VH header, XM)**:

| VB | VH | XM | XM channels | patterns | samples | tempo/BPM | who plays it |
|---|---|---|---|---|---|---|---|
| 291 | 292 | 293 | 8 | 5 | 6 | 6 / 125 | nobody found |
| 294 | 295 | 296 | 10 | 33 | 26 | 8 / 125 | world index 2 |
| 297 | 298 | 299 | 10 | 12 | 15 | 6 / 125 | front end (0x800BCE14) |
| 300 | 301 | 302 | 10 | 46 | 21 | 4 / 125 | world index 1 |
| 303 | 304 | 305 | 10 | 38 | 25 | 6 / 125 | world index 0 |
| 306 | 307 | 308 | 4 | 4 | 22 | 7 / 125 | nobody found (instruments named `00 : halloween`, `01 : fantasy`, `02 : jungle`, `03 : space`) |
| 309 | 310 | 311 | 10 | 5 | 27 | 7 / 125 | nobody found |
| 312 | 313 | 314 | 10 | 12 | 23 | 4 / 125 | nobody found |
| 315 | 316 | 317 | 10 | 32 | 28 | 7 / 125 | world index ≥ 3 |

Selection is at 0x80058694 (after loading the 8 in-park banks): `[0x801038A0]` 0 → (304, 303, 305),
1 → (301, 300, 302), 2 → (295, 294, 296), else → (316, 315, 317), passed as (VH, VB, XM) to
0x800B7A90. The four names in entry 308 are the only in-file evidence of world order (INFERRED
0 = Halloween, 1 = Fantasy/Wonderland, 2 = Jungle/Lost Kingdom, 3 = Space; falsifier: read
`[0x801038A0]` in each world). The four unreferenced songs have no `jal` caller in the image or the
overlays; falsifier: break on 0x800B7A90 and see whether a2 ever equals 293/308/311/314.

### 5.2 The XM files
`Extended Module: ` + blank name, tracker `FastTracker v2.00`, header version field 0xDDBA (not the
usual 0x0104 — rewritten by the conversion tool), flags = 1 (linear frequency table), tempo/BPM above.
**Sample data is stripped** (every sample length is 0) but the instrument and sample headers survive, so
each XM sample keeps its `relative note`, `finetune`, volume, panning and loop type — and the XM sample
count equals the VH's VAG count in all 9 songs (6/26/15/21/25/22/27/23/28). XM sample *i* is VAG *i + 1*
in the VH's VAG table (entry 0 of a VAG table is unused).

### 5.3 The VH/VB pair
VH = a Sony VAB header with **one program** (3,104 bytes = 32 + 2048 + 512 + 512), all 16 tones dummies
(`center 60, shift 0, vol 127, pan 64, adsr1 0x80FF, adsr2 0`), `size` field = VB size + 2592, master
volume 127. The only field the player uses is the VAG size table. The VB is the concatenation of the
VAGs, 193 in total (one is empty: song 1 VAG 3 has size 0). **Base rate of a music waveform** = XM
convention: `8363 × 2^((relnote + finetune/128) / 12)` Hz at C-4, in `au/xm_samples.json` per sample.

### 5.4 The player
The library between 0x800CDB40 and 0x800D2524 is an XM player (INFERRED SCEE's from the API shape and the
`XMSong`/`XMHeader` allocations; the name is not in the image). What is SOURCED:
- 0x800CE1C0(0) once-off init; 0x800CDEC8(xm, songSlot 0, 1) parses the XM header (channels clamped to 24
  at 0x800CDFC4), pattern-order and pattern/instrument pointers into the `XMHeader` block.
- 0x800B7A90(vh, vb, xm): loads VB then VH (§4), then the XM (kept locked at 0x80103A10), sets volume
  category 2 to 0, **0x800CE20C(vab, song 0, channel 0, voiceStart 0, 1, −1, 0, 0)** (the fifth argument is stored at chan+36 and is INFERRED to be the loop flag; the sixth, −1, is the channel-enable mask at chan+80), 0x800CDE68
  (kick), then category 2 to 0xFF. Refcount 0x801033E4; 0x800B7C10 stops (0x800D2214), closes the VAB
  and frees the XM.
- Voices: `voiceStart = 0`, one per XM channel → **SPU voices 0..channels−1** (≤ 10). Each is initialised
  by 0x800D1E10: `SpuSetVoiceAttr` mask 0xFF80 with `a_mode 1 (linear), s_mode 1, r_mode 3, ar 16, dr 0,
  sr 0, rr 12, sl 15`, which libspu's encoder (0x800D3758..0x800D3998) turns into **ADSR1 = 0x100F,
  ADSR2 = 0x000C**.
- Per tick it uses `SpuSetVoicePitch`, `SpuSetVoiceVolume` (×2 sites) and one `SpuSetKey(1, mask)` /
  `SpuSetKey(0, mask)`; the second `SpuSetVoiceAttr` site (0x800D1DF8) sets the start address per note.
- Update: 0x800CDDBC runs from the per-frame list and counts `VSync(1)` deltas into 0x800FB2F0 before
  calling the tick 0x800D01B4 — so timing is frame-derived (INFERRED: BPM 125 → 2.5 ticks per PAL frame
  is handled by the tick's own accumulator; falsifier: pattern rows per second on the console = BPM·2/5 /
  tempo = 8.33 rows/s for tempo 6).
- Volume: 0x800D20C8(handle, v) with v ≤ 0x80, computed as `(byte1 × byte2) >> 9` of the category
  table (§6), so 0xFF × 0xFF → 127.

## 6. Volume, mixing, reverb (SOURCED)
- Five volume bytes at **0x801033FC..0x80103400** (all 0 in the image; set to 0xFF by 0x800B8A10 and then
  from the save card by 0x8006C524; the Game Options window 0x80084530 writes categories 1 and 3):
  cat 0 → `SpuSetCommonMasterVolume(v << 6, v << 6)`; cat 1 and 2 → XM volume `(b1 × b2) >> 9`
  (1 = the user setting, 2 = the mute/fade the music start uses); cat 3 → SFX scale read at play time;
  cat 4 → stored, read by nothing in the image.
- Master volume: 0x3FFF/0x3FFF at 0x800B8984, then 0x3FC0 once category 0 = 0xFF is applied.
- Reverb at 0x800B8990..0x800B89E0: `SpuReverbAttr {mask 7, mode 6 = SPACE, depth 0x1000/0x1000}` →
  `SpuSetReverbModeParam`, `SpuSetReverb(1)`, `SpuReserveReverbWorkArea(1)`, **`SpuSetReverbVoice(8 =
  SPU_BIT, 0)`** = no voice enabled, then `SpuSetReverbDepth` with mask 6. Nobody calls
  `SpuSetReverbVoice` again, and the XM player does not touch EON, so the reverb unit runs on silence.
  `SpuSetEnv({1, 0})` = event queueing off.
- CD audio: `CdMix` with all four gains 0x7F when an advisor line or a movie starts (0x800375E4), 0 when
  it ends (0x800375C4). There is no CD-DA on the disc (one data track).
- Panning: every SFX is centre (0x8000); XM panning is whatever the modules carry (`xm_samples.json`,
  `pan` per sample) and the player's stereo flag argument is always 0 (0x800BE730 returns 0).
- Falsifiers: after boot 0x1F801D80/0x1F801D82 = 0x3FC0; 0x1F801D84/0x1F801D86 = 0x1000; SPUCNT
  (0x1F801DAA) bit 7 set; EON (0x1F801D98/0x1F801D9A) = 0 at all times; 0x1F801DA2 = 0xE128; voices 0–9
  ADSR 0x100F/0x000C while music plays and 10–23 always 0/0.

## 7. Not determined, and the experiment for each
1. **`rec.n[lang]` in the advisor index** (§2.4). Three experiments listed there; (ii) is decisive.
2. **Whether the emulator delivers INT1 for filtered XA sectors** — same experiment; if it does, pcsx's
   rule differs from hardware and the reimplementation must pick one.
3. **World index → theme** and which of language 2/3/5/6 is which: one RAM read each (`[0x801038A0]`,
   `[0x80103AA4]`).
4. **The `x` field of message variants (6–16)**: correlate with the advisor animation shown.
5. **Movie frame rate**: VSync count between MDEC frames (expect 4.17 at 12 fps, 3.33 at 15).
6. **The four unreferenced songs and four unreferenced banks**: breakpoints on 0x800B7A90 / 0x800B8B80.
7. **GRAV.STR's world**: decode a later frame (`ffmpeg -i /tmp/GRAV.STR -vf select=eq(n\,300)`).

## 8. Corrections to measured.md
- "`176389 Hz` = pitch 0x4000, the idle value": the idle value is **0x3FFF**, written by `_spu_init` at
  0x800D9164; 0x3FFF/0x1000 × 44100 = 176,389.2 Hz, which is the number measured. 0x4000 would be
  176,400.
- "22050 Hz confirmed by ear" is not a property of any SFX sample: SFX base rates are 11025 and 8000 Hz
  (§3.2). The readings `v0–v7 = 11143, 11143, 11111, 22298, 22298, 11111, 16710, 8355` are all on voices
  0–7, which belong to the **XM player**, not the SFX driver (voices 10–23). Against the tracker base
  8363 Hz they are notes: 8355 = C-4 (−1.7 cents), 16710 = C-5, 11143 = F-4 (+2 cents), 22298 = F-5,
  11111 = F-4 with finetune ≈ −8/128 (song 305 has instruments with finetune −8). The "constant +1.1 %"
  is 11143/11025, i.e. F-4 in a 8363-based scale versus a 11025 guess, not a clock error. Falsifier:
  voices 0–9 always read 8363·2^(k/12) (± a few cents) while music plays; voices 10–23 read 0x400,
  0x2E7 or 0x3FFF.
- The "192 decodable waveforms" is one short of the 193 music VAGs (one has size 0) and excludes the
  132 SFX records, which are headerless (no `VAGp`) and were presumably not found by a magic search.
