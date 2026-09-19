# Theme Park World (SLES-026.88) — the save / load format

Sixth report. Scope: what the memory-card save contains, how the park is serialised, and what the
loader accepts. Every address is in the raw image (`TPW.BIN` at 0x80010000). **SOURCED** = read from
the disassembly at the quoted address (or measured on the console, where it says so). **INFERRED** =
a reading not backed by a quoted instruction; each one says what would settle it.

**Numbers are DECIMAL unless written with `0x`.** Record sizes are given both ways where it matters.

Tools beside this file, in `fable/sv/`:

| tool | does |
|---|---|
| `pak.py` | PAK/UNPAK codec (port of 0x80018C08 / 0x80018EF0). `pak.py t FILE` round-trips. **Byte-identical to the game's output** on the captured stream (1993 of 1993 bytes). |
| `parkstream.py` | parses one unpacked park stream into JSON (every record below). |
| `mcsave.py` | parses a 0xA000 save file or a `.mcr` card image; `compose()` builds a save file the loader accepts; `mkcard()` builds a card image around it. |
| `ramscan.py` | pulls SVEDLEV, the packed park streams, GENDATA and any in-flight card buffer out of a RAM dump or a pcsx_rearmed `.state`. |
| `runner_mc.c` | `runner.c` plus `MCARD=` / `MCARD_OUT=` (load / dump memory card 1). `runner.c` itself is untouched. |
| `streams/` | the stream the game itself produced (`sv_quit_ram_000500.{bin,pak}`), its GENDATA, and a save file composed from it (`composed_quit.sav`). |
| `dis/` | annotated disassembly of every function cited here, one file per function. |

**What was measured, not just read.** With the runner I drove Laptop → Game Options → Quit Current
Game from `states/ui/01_laptop.state`. That runs the leave-park snapshot (0x80057EFC), and the RAM
dump afterwards holds a real packed park stream in SVEDLEV: 4596 bytes unpacked (0x11F4), 1993 packed.
`parkstream.py` consumes it to within its 2 trailing pad bytes and every decoded value cross-checks
against known state (ride built on day 131, balance 480800 = £48,080, fee £40, 2 admissions, ride status
10). Sections 3 and 4 are therefore SOURCED twice: once from the code, once from that stream. The
card-file header (section 1) is SOURCED from the composer and loader code only; section 7 lists the
runs that would close that gap.

## 0. Answers in one screen

| question | answer | tag |
|---|---|---|
| card layout | one file per save, **always 5 blocks = 40,960 bytes (0xA000)**, one icon frame, title `[TPW Sandbox] name` / `[TPW FullSim] name`, filename `BESLES-02688` + 8 chars of the name. The file is a *campaign* save: it holds the 4×2 park-slot table and **up to 3 packed park streams** plus options and progress. A park is never more than one stream; the streams share the file. | SOURCED §1 |
| park record | one stream = 18-byte header + 20 sections in a fixed order, fixed-size records (ride 152, shop 28, staff 16, BANK 808, McAi 220 …), padded to 4 after each section. §3. | SOURCED |
| compression | the whole stream is **one** PAK container; GENDATA is a second container; the card header and title are raw. PAK = byte-oriented LZ with a 128-byte window (§2). | SOURCED |
| SVEDLEV | a 64-byte heap block, 8 entries × 8 bytes indexed `park*2+sub`: `{u32 handle of the packed stream or −1, u8 flag}`. Filled on leaving a park, drained on entering one, copied whole into the card file. | SOURCED §4 |
| versioning | magic `0x47415901` at file+0x208, version halfword `0xAC` (172) at +0x20C, u32 sum checksum at +0x200 mirrored at +0x9FFC. Anything else is refused. Inside a stream: park/sub bytes must match the park being entered. | SOURCED §5 |
| guests | **per-guest state is not saved.** One 26-byte record holds `(min, max−min)` per attribute over all guests plus the count; on load that many guests are re-rolled from the ranges and dropped at exit point 1 in state 45. | SOURCED §6 |
| terrain | **not saved.** Only the path bitmap is. The heightmap comes from the level. | SOURCED §3.2 |

## 1. The on-card layout (SOURCED: composer 0x8006C0E8, loader 0x8006C524, write op 0x8007046C)

### 1.1 File
- `MemCardCreateFile(port<<4, name, 5)` at 0x80070500: **5 blocks**, always. The composer allocates a
  0xA000 (40,960) byte block tagged `MCBUFFER` (0x8006C33C) and zero-fills it (0x8006C368..0x8006C37C);
  the writer sends `ceil(size/8192)` = 5 blocks (0x80073544..0x80073560).
- **Name** (0x8006F50C): `"B"` + region string + `"00000000"`, then up to 8 characters overlaid from
  offset 12. Region from 0x800BE730: 0 → `ESLES-02688` (PAL), 1 → `ISLPS-02643`, 2 → `ASLUS-01069`.
  The 8 characters are the park/save name with case forced: **lower-case for Sandbox, upper-case for
  FullSim** (0x80073574..0x80073594). So a PAL FullSim save of "fable" is `BESLES-02688FABLE000`.
- **Title frame** (0x8006F9B0 on the template at 0x800F300C, copied to file+0): `"SC"`, icon flag
  `0x11` (one icon), block count 5, then 32 full-width Shift-JIS characters (64 bytes, big-endian,
  converted by 0x8006F440 through the table at 0x800F32A0; unmapped → `?`), zeros to +0x60, the
  16-entry CLUT at +0x60, the 16×16 4-bpp icon at +0x80, zeros to +0x200. The template's own title
  reads `ＴＥＳＴ　ＳＡＶＥ` and is always overwritten.
- **Title text** (0x80073484): `sprintf("[TPW FullSim] %s" | "[TPW Sandbox] %s", name)`; region 2 uses
  `STP` instead of `TPW`. The Load Game list shows titles, not filenames (0x80070080 reads frame 0 of
  every file whose name starts with the 12-character prefix and whose frame begins `SC`).

### 1.2 Header at file+0x200 (0xAC = 172 bytes; the table in `save.json` is the authoritative copy)

| offset | type | field | source |
|---|---|---|---|
| 0x200 | u32 | checksum = u32 sum over `[0x204, 0x204+len)` zero-padded to 4 (0x800708A4) | 0x8006C4C8 |
| 0x204 | u32 | **len** = composer's end offset counted from the *file start* | 0x8006C1E4 |
| 0x208 | u32 | magic `0x47415901` | 0x8006C1D8 / 0x8006C5A0 |
| 0x20C | u16 | version `0xAC` | 0x8006C1E0 / 0x8006C5B0 |
| 0x210 | u32 | GENDATA offset, always 0x26A | 0x8006C100 |
| 0x214 | u16 | GENDATA container length (0 = none) | 0x8006C138 |
| 0x218 | u32[3] | stream offsets | 0x8006C188 |
| 0x224 | u16[3] | stream container lengths | 0x8006C1A4 |
| 0x22A | u8 | FullSim = `!0x80059A9C()`; loader stores `0x80102D34 := (byte==0)` and, if Sandbox, opens the park (0x80059AA8) | 0x8006C200 / 0x8006C5C0 |
| 0x22B | u8[8] | slot status `[park][sub]`: SVEDLEV flag in the low nibble, `|0xF0` when a stream follows | 0x8006C21C..0x8006C248 |
| 0x233 | u8[3] | progress bytes 0x8010398C, 0x80103984, 0x80103988 | 0x8006C294 |
| 0x238 | u32 | 0x80103990 (a 5-bit mask, counted by 0x8006BE60) | 0x8006C2A0 |
| 0x23C | u32[8] | 0x80109B18 `[park][sub]` (15-bit masks, counted by 0x8006BEF0) | 0x8006C2B4 |
| 0x25C | u8 | `!0x8010399C[0]` | 0x8006C2F8 |
| 0x260 | u8[4] | volumes 0x800B7D7C(0,1,3,4) | 0x8006C300..0x8006C32C |
| 0x264 | u8 | tutorial flag 0x80102EB4 | 0x8006C304 |
| 0x266 | i16,i16 | screen offset 0x800BB39C / 0x800BB3A8 | 0x8006C338 |
| 0x26A | — | payload: GENDATA container, then the streams in slot order | 0x8006C3C0.. |

Three things about the checksum that a re-implementation must copy exactly:
1. **The summed range overruns the data by 0x204 bytes** (SOURCED 0x8006C4C8..0x8006C4D4 and
   0x8006C578..0x8006C584): `len` is an offset from the file start but the sum starts at 0x204, so it
   runs to `0x204+len`. The buffer is zero-filled, so this only matters if the payload ends within
   0x204 of 0x9FFC — then the checksum copy itself is inside the range. Keep payload end ≤ 0x9DF8.
2. The alignment pad is done by **writing zero bytes into the buffer** (0x800708B4 / 0x80070900) — the
   loader mutates the file image while checking it. `len` is not a multiple of 4 in general (the
   captured stream container is 1993 bytes), so up to 3 bytes after the payload are zeroed by the
   check; they are zero already because the composer memsets the block.
3. The word at 0x9FFC is compared with the word at 0x200 **before** the sum (0x8006C55C..0x8006C570).
   A file shorter than 5 blocks therefore fails at once.

### 1.3 Capacity
Payload space is `0x9DF8 − 0x26A` = **39,822 bytes** for GENDATA + up to **3** streams (the header has
three offset slots; the composer walks all 8 slots but only entries flagged 0xF0 get a stream — a fourth
would overflow the on-stack table at sp+0x28, so treat 3 as the hard limit). The captured 44×74 park
with one ride packs to 1993 bytes; the path bitmap (`w*h` bytes, mostly zero) is what the packer
squashes. Whether a park at the upper end of the campaign fits three-to-a-file is UNMEASURED.

### 1.4 What the loader installs (0x8006C524, in order)
FullSim/Sandbox byte → 0x80059AA8; for each of the 8 slots: nibble 0 → 0x8006BC68 (mark playable),
nibble 1 → 0x8006BCE4 (mark finished, free any stream), and **only if the byte is exactly 0xF0** copy
the stream container into a fresh `PAKDATA` heap block and store its handle (0x8006C624..0x8006C65C);
then the progress bytes/words, option byte, volumes, tutorial flag, screen offset; finally GENDATA into
its own heap block (0x8006C76C..0x8006C7B0). Returns 1. Nothing is unpacked here; unpacking happens
when a park is entered (§4).

## 2. Compression (SOURCED: pack 0x8006C994, unpack 0x8006CA44, PAK 0x80018C08 + emitter 0x80018A94, UNPAK 0x80018EF0)

**Container** (`PAKDATA` block): `u32 unpacked_len, u32 total_len (= packed_len + 8), packed bytes`.
This is the "u32 unpacked_size, u32 ?" header `folio.md`'s `unpak.py` guessed at — the second word is
the container length including itself. The two heap tags are `PAKDATA` (packed) and `UNPAK` (the
scratch the unpacker writes into, 0x8006CA74).

**Grammar** (UNPAK 0x80018EF0): read token `c`. `c < 0x80` → copy `c+1` literal bytes. `c ≥ 0x80` →
read `n`; if `n == 0` stop; else copy `n` bytes from `out − (256 − c)`, i.e. offsets −128..−1, byte by
byte (overlap allowed). The terminator is `80 00`.

**Encoder** (0x80018C08): the first two bytes are always literal; from position 2 it searches offsets
`max(−128, −pos)..−1` in that order for the longest match up to `min(255, remaining)`, keeping the
*first* longest; a match shorter than 3 becomes a literal; literals are flushed in runs of ≤128
(`c = count−1`); a pending match is flushed before a literal. Two passes: 0x80018EA8 with a null output
measures, 0x80018E54 writes. `pak.py` is that algorithm and reproduces the game's container for the
captured stream **byte for byte**, so any re-implementation that uses it will produce files the game's
own tests cannot tell from its own.

**What is packed**: exactly two kinds of thing — each park stream (one container per park slot) and
GENDATA. The card header, title, options and progress are raw. This is per-slot, not per-section.

## 3. The park stream (SOURCED: writer 0x80070948, reader 0x80071D0C; measured on the captured stream)

The writer runs twice (0x80070A48): once with `0x801039B8 = 0` to measure, once to write into a heap
block tagged `SAVE`; the primitive 0x80070AF0(ptr, len, align) copies `len` bytes and, when `align`,
rounds the cursor up to 4. The reader's primitive 0x80071E48(len, align) mirrors it. **Every section
below is written with align = 1 unless it says otherwise, so each is followed by 0–3 pad bytes.** The
heap rounds the block to 4, which is the 2-byte trailer the captured stream has.

Section order (writer 0x80070958..0x800709F8 = reader 0x80071D40..0x80071DD8):

| # | section | size (bytes) | record | count from |
|---|---|---|---|---|
| 0 | header | 18 (0x12) | §3.1 | — |
| 1 | open/fee | 4 | `u8 open, u8 pad, u16 fee_pounds` (0x80070C50 / 0x80071F2C) | — |
| 2 | paths | `w*h` | bitmap §3.2 | map 0x801038AC × 0x801038B0 |
| 3 | rides | 152 (0x98) each | §3.3 | header+4 |
| 4 | track rides | 240 (0xF0) | §3.3 | +5 |
| 5 | tour rides | 152 | §3.3 | +6 |
| 6 | coasters | 1048 (0x418) | §3.3 | +7 |
| 7 | features | 16 | §3.4 | +8 |
| 8 | shops | 28 (0x1C) | §3.4 | +9 |
| 9 | sideshows | 28 | §3.4 | +10 |
| 10 | visitor template | 26 (0x1A), **exactly one** | §6 | (+16 is the respawn count) |
| 11–15 | guards, researchers, mechanics, handymen, entertainers | 16 each | §3.5 | +11..+15 |
| 16 | litter | 2 | `u8, u8` counts (0x80053A10 / 0x80053A98) | — |
| 17 | BANK | 808 (0x328) | §3.6 | — |
| 18 | McAi | 220 (0xDC) | §3.7 | — |
| 19 | research | `2·Σcounts + 16`, **absent in Sandbox** | §3.8 | level tables |
| 20 | messages | variable, unaligned | §3.9 | first byte |

After section 20 the reader calls 0x800505A8 (0x80071DE0) — post-load fix-up, not read further.

### 3.1 Header (0x80070BA8 / 0x80071ECC)
`u16 0x3039` (12345 decimal — written, **never checked**); `u8 park` = 0x80054000(); `u8 sub` =
0x80053FF4(); then thirteen `u8` counts in this order: rides, track rides, tour rides, coasters,
features, shops, sideshows, guards, researchers, mechanics, handymen, entertainers, **visitors** (the
writer stores the visitor count at +16 out of order, 0x80070C08); byte 17 is never written (stack).
The reader **rejects the whole stream** if park or sub differs from the park being entered
(0x80071EF8 / 0x80071F0C → returns 0 → 0x80071D38 skips every section). Captured: `39 30 00 00 01 00 …
02 00`.

### 3.2 Paths (0x80071430 / 0x80072820)
For `y in 0..h, x in 0..w` (x fastest) bit `i = y*w + x` is set when the tile is type 2 or type 13
(0x8004D4EC / 0x8004D558 — path and queue path per `paths.md`). Bits are packed LSB-first into
`ceil(w*h/8)` bytes, but the section is **`w*h` bytes long**: the writer allocates `w*h+1` (block `LSVE`)
and sends `w*h`, so bytes beyond the bitmap are **uninitialised heap** (zero in the capture). On load
each set tile is re-laid with the path tool: 0x8001B5A0(2) selects the tool, then 0x8001BD30(x,y,0,0)
is called **twice** — the first call sets the anchor (0x801026F0/F4 were −1 → 0x8001BDF4..0x8001BE08),
the second lays from the anchor to the same tile. Link bits between tiles are therefore rebuilt by the
tool, not stored. Queue paths (type 13) are re-laid as plain paths; whether the ride's queue list (§3.3
points) re-types them is INFERRED, the falsifier is in §7. 44×74 → 3256 bytes; captured: 11 tiles set.

### 3.3 Rides (0x8009CEFC on a 0x80062AE8 base)
Building base, 12 bytes (SOURCED 0x80062AE8), shared by every attraction:
`u32 +0` → A+0x14 guests served; `u8 +4` type (slot 16; rides are 1,3,6,7); `u8 +5` x; `u8 +6` y (slot
19); `u8 +7` rotation 0..3 (slot 18); then slot 39 is called with 1 (commit). Ride record, 152 bytes:

| offset | type | meaning | tag |
|---|---|---|---|
| 8 | i16 x, i16 y ×32 | queue tiles, count at +145, fed to 0x8001A0EC in order | SOURCED |
| 136 | u16 | A+0xF4 day built | SOURCED (captured 131) |
| 138 | u16 | speed slider (slot 92) | SOURCED (captured 50 = default) |
| 140 | u8 | capacity slider (slot 93) | SOURCED |
| 141 | u8 | duration slider (slot 94) | SOURCED |
| 142 | u8 | remaining lifetime (slot 83) | SOURCED |
| 143 | u8 | reliability 0..100 → A+0xB4 `<<12` | SOURCED |
| 144 | u8 | status → slot 57; if 4..6 the loader spawns breakdown smoke (0x8009C2A8) and forces 4 | SOURCED |
| 145 | u8 | queue point count | SOURCED |
| 146 | u8 | upgrade level A+0xF6 | SOURCED |
| 148 | u8 | **variant** = catalogue index; the section reader allocates by it (0x80050B5C) | SOURCED |

A+0xF0 (riders on board) is zeroed; queue members, riders, phase timers are not saved. Track rides
(0xF0) add `u8 +149` piece count and `(u16,u16)` pairs from +0x96, plus `u8 +236` and `i8` pairs from
+0xE6 → A+0x1CF2 (0x800A6E6C); tour rides (0x98) add `u8 +149` → 0x800A22A0; coasters (0x418) read the
**u32** at +148: bits 0–7 variant, bits 8–13 element count ≤63, bits 14–15 → 0x800ADEA0, then 14-byte
elements `{u16,u16,u16,i16,i16,i16,u8,u8}` from +150 into 0x800AF73C / 0x800B6728. Sizes SOURCED;
what the track/coaster fields mean is INFERRED (piece coordinates / segment geometry) — no such ride
was in the capture.

### 3.4 Shops, features, sideshows
- **Shop** 28 bytes (0x800B6BB8): base; `u32 +8` → A+0x84; `u16 +12` sale price (0x800B7140); `u16 +14`
  → A+0x8C takings; `i16 +16` → A+0x90 profit; `u16 +18` → A+0x78 day built; `u8 +20` **variant**
  (allocator 0x80050F7C, A+0x6B); `u8 +21` status; `u8 +22` → A+0x7C; `u8 +23` with A+0x80 := `rec[23] ×
  rec[8..11]`; `u8 +24` → 0x800B706C. Shop prices are per shop, here.
- **Feature** 16 bytes (0x80023DB8): base; `u32 +8` → A+0x78; `u8 +12` variant (0x80050FE8); `u8 +13`
  status; `u8 +14` capacity 0..100 (A+0x80).
- **Sideshow** 28 bytes (0x800B7410): base; `u32 +8` → A+0x7C; `u16 +12` play price; `u16 +14` win
  chance; `u16 +16` wins; `u16 +18` prizes paid; `u16 +20` takings; `u16 +22` → A+0x94; `u8 +24` prize £;
  `u8 +25` variant (0x80051054); `u8 +26` with A+0x80 := `rec[26] × rec[8..11]`; `u8 +27` status.
- **Scenery**: there is no section for it. If the build lets the player place trees or decor, they are
  not in the save (SOURCED by absence in the writer list).

### 3.5 Staff (writer 0x80094BBC, reader 0x80094C64; 16 bytes each, one section per kind)
`u32 +4` hire day (P+0x34); `u8 +8` variant (slot 53 / 52) — **the name is not stored**, it comes from
the catalogue for (kind, variant) via 0x80069EF4; `u8 +9` bits 0–2 level (P+0x3C), bit 7 = was
striking (state 15 → restored into state 15, 0x80094CCC); `u8 +12..15` patrol rectangle S+0x38..0x3B.
Bytes 0–3 and 10–11 are **never written and never read** (stack). Position is not saved (0x8009386C
re-places the person). Allocators: guard 0x80051640, researcher 0x80051850, mechanic 0x800516F0,
handyman 0x800517A0, entertainer 0x80051900, each by variant.

### 3.6 BANK, 808 bytes (writer 0x80087DEC, reader 0x80088A0C)
`8 × 80-byte histories` at 0 (month-end balance, income, entry fees, shop profit, sideshow takings,
spend, wages, park value — the BANK+0xBC..+0x107C order of `economy.md`), `4 × 28-byte loans` at 640
(0x280), then thirteen i32 at 752 (0x2F0): balance (tenths, raw), month index (+0x12BC), income last
year (+0x12E0), spend last year (+0x12E8), income this year (+0x12DC), spend this year (+0x12E4),
sideshow takings all-time (+0x12C4), entry fees all-time (+0x12C8), shop profit all-time (+0x12CC),
wages all-time (+0x12D0), spend all-time (+0x12D4), income all-time (+0x12D8), park value snapshot
(+0x12EC), balance snapshot (+0x12F0) — 0x80087E58..0x80087EF0 (the `sw` sits in each `jal`'s delay
slot and stores the *previous* call's result; read it that way or every field shifts by one).
Captured: 780 = 80 = two £40 admissions, which pins the mapping. All-time and yearly figures are
divided by 10 on save (0x80088E18) and multiplied back on load (0x80088E50); balance and histories
are raw tenths. **The entry fee is not here** — it travels in section 1.

History block (0x800879DC / 0x80087F90): `i32 min, i32 max` over months 13..N, `i32[12]` the last 12
months exact, then 24 bytes: 6 pair-averages (months 13–24), 8 six-month averages (25–72), 9
eight-month averages (73–144), one pad, each byte = `(v−min)·255/(max−min)` (reader: `min +
byte·range·0x80808081>>39`). The 12 exact and the pair tier are read; the six/eight tiers are INFERRED
from the loop shape (the u8 twin in §3.7 is fully read and has the same shape).

Loan (0x80086470 / 0x800864C4): `i32 principal, i32 term months, i32 months left, i32 amount left,
i32 monthly payment, i32 total to repay, u8 available (0 = taken)`. Captured: four untaken loans
100000/3, 50000/2, 25000/2, 10000/2 with totals 104637, 51524, 25762, 10234.

### 3.7 McAi, 220 bytes (writer 0x8006892C, reader 0x80069034)
`u32[5]` at 0 → McAi+0x300+8i; `u32 +20` total days (McAi+0x10; captured 146); `u32 +24` admissions
(McAi+0x1C; captured 2); `u16 +28` year (McAi+8); `u16 +30` McAi+0x18; `u8 +32` McAi+0x2F8; `u8[5] +33`
→ McAi+0x304+8i; `u8 +38` bit i → McAi+0x305+8i; `u8 +39` months in debt (McAi+0x14); `u8 +40` month
0..11 (McAi+4; captured 4); `u8 +41` day of month (McAi+0xC; captured 26); then five 35-byte u8
histories at +43 (0x2B), +78, +113, +148, +183 → McAi+0x28+0x90·i, 144 monthly bytes each, encoded as
12 exact + 6 pair + 8 six-month + 9 eight-month averages (0x80068740, fully read). What the five
tracks and the five (u32,u8,bit) triples are is INFERRED (objectives / park-rating series; the
objective check 0x80067928 reads McAi via s4+0x14 — see `parkopen.md` §3.1).

### 3.8 Research (writer 0x800715F0, reader 0x80072B54)
**Skipped entirely when 0x80059A9C() ≠ 0 (Sandbox)** — both sides, including the 16-byte tail. Else for
category in `[3, 7, 6, 1, 2, 4, 5, 8]`, for each item: `u8 progress (0..99), u8 level`, unaligned. The
per-category counts are **not in the stream**: they come from the level tables — the research manager
0x800F29FC holds (sub at +4, park at +8); `0x800DDDC4[park] + 4·sub` points at a struct whose words at
+16, +32, +48, +80, +96, +112, +128, +64 are the eight counts in stream order (getters 0x8006A214 …
0x8006A2A4). They are filled at level load (zero in the image); park 0 sub 0 live: **8, 0, 1, 1, 13,
6, 3, 2** = 34 items = 68 bytes. Then 16 bytes (0x8009B28C / 0x8009B2EC, the research singleton
tagged `BANK`): `u8 [+4]`, then 5 × `{u8 active, u8 category, i8 index}` — the five research slots,
re-bound through 0x8009B448. A re-implementation needs the count tables; `save.json` carries the
park-0 numbers, the others need one RAM read per park (§7).

### 3.9 Messages (writer 0x800719CC, reader 0x800729A0)
`u8 count` (entries of kind ≠ 4), then per entry: align to 2; `i16 id`; if `id == −1`: `u8 len` + text;
`u8 kind`; `i8 subject type` (0xFF none — written for kind ≠ 2 or no subject); if ≠ −1: `u8 subject
index` (0x8005C0E4 = index of that building among its type). The reader re-creates each with
0x8003A7BC / 0x800385AC on the manager at 0x8010988C and re-resolves the subject with 0x8005BEC0.
INFERRED: this is the pending advisor / message-window queue (`debug.md` puts the window code at
0x8003867C). Captured: count 0.

### 3.10 GENDATA (writer 0x80070A9C, reader 0x80071E04) — vestigial
25 bytes are written from a stack buffer that 0x80013A9C is supposed to fill through the message-box
object's sub-object (`[0x8010265C]+448`, vtable 0x800DBB3C). **Its slots 2 and 3 — 0x8001758C and
0x80017594 — are `jr ra`.** So GENDATA is 32 bytes of whatever was on the stack (25 + pad + one more
uninitialised byte + pad), packed, stored, and on entering a park handed to a no-op. The captured
block is `00 01 00 01 00 00 00 01 00…00 01 | 00 e7 02 | 01 | 32 00 00`. Ignore it when writing;
accept anything when reading. SOURCED (both stubs read; the live vtable read from RAM).

## 4. SVEDLEV (SOURCED: 0x8006BA3C create, 0x8006BB50 destroy, 0x8006BC00 entry, 0x8006C89C snapshot, 0x8006C850 restore)

`SavedLevelManager::create` (the string at 0x800E18B0) allocates a **0x40-byte block tagged SVEDLEV**
(handle at 0x80102EAC), 8 entries of 8 bytes at `(park*2+sub)*8`: `u32 handle` of the park's PAKDATA
container (−1 = none) and `u8 flag`. Flags: **2** never entered (constructor 0x8006C83C), **0**
playable/current (0x8006BC68; create marks (0,0); the count of such entries lives at 0x80103980),
**1** finished (0x8006BCE4 — frees the stream; only the card loader ever sets it, from a status nibble
of 1). Also zeroed at create: the progress words 0x80109B18[4][2], 0x80103984/88/8C/90.

- **Filled** by 0x80057EFC (Leave Park / Quit Current Game, at 0x80058048) → 0x8006B8E8(park, sub) →
  0x8006C89C: free the old stream, serialise (0x80070A48), pack (0x8006C994), store the handle.
  0x8006B980 packs GENDATA the same way just before (0x80058038).
- **Drained** by 0x800588D0 (park start, at 0x80058AC4) → 0x8006B92C(park, sub): if
  `entry.handle ≠ −1` → 0x8006C850: unpack into an `UNPAK` block, run the 21 readers, free. The
  Practice Park and a fresh Main Game have no entry (handle −1 in every save state on disk), so they are
  built from the level, not from a stream. **The stream stays in SVEDLEV after loading**, so re-entering
  the same park without leaving restores the same snapshot.
- **Card round-trip**: the composer copies each flagged entry's container verbatim into the file and
  the loader copies it back into a fresh PAKDATA block; the flag survives in the low nibble of the
  status byte. Only status bytes equal to **0xF0** (flag 0 with data) are restored; 0xF1/0xF2 lose their
  stream (0x8006C620..0x8006C624 compares the whole byte with 0xF0).

The (park, sub) key is simply the two globals 0x801038A0 / 0x801038A4 at the time of leaving; the
stream header repeats them and the reader refuses a mismatch (§3.1), which is what makes a stream
copied into the wrong slot harmless.

## 5. Versioning and what is refused

Loader 0x8006C524 returns 0 (the UI retries the read up to 5 times, 0x80073378 / 0x80073484) when:
1. word 0x200 ≠ word 0x9FFC (0x8006C570);
2. the u32 sum over `[0x204, 0x204+len)` ≠ word 0x200 (0x800708EC);
3. word 0x208 ≠ 0x47415901 (0x8006C5A0);
4. halfword 0x20C ≠ 0xAC (0x8006C5B0).
There is no other version field. Inside a stream the only check is park/sub (§3.1); a stream with the
wrong counts or a truncated section is read straight off the end of the buffer — no length check
exists in 0x80071E48, so a hand-made stream that is too short will read heap garbage rather than fail.
The magic reads as the bytes `01 59 41 47` in the file. 0xAC (172) has no derivation I could find (the
header the composer copies is 0x6A bytes, the block it validates is 0xA000); treat it as a constant.

## 6. Guest re-spawn (SOURCED: writer 0x80070FA4, reader 0x80072464 + 0x80091A3C)

The writer walks every guest (list 0x800536B4) keeping the min and max of 13 attributes, then writes
one 26-byte record of `(min, max−min)` pairs:

| off | attribute | restored? | distribution on load |
|---|---|---|---|
| 0 | rubbish V+0x58 (setter 0x800924C8 adds 0x58 to the pool-entry pointer; `behaviour.md` naming) | yes | `min + (rand(r+1)+rand(r+1))/2` (0x8009197C) |
| 2 | happiness V+0x59 | yes | same |
| 4 | nausea V+0x5A | **no** (ctor random) | — |
| 6 | need V+0x5B | yes | `min + (r+1) − rand(r+1)²/(r+1)` (0x800919C4, skewed high) |
| 8 | boredom V+0x5C | **no** | — |
| 10 | desire V+0x5D | yes | skewed high |
| 12 | need V+0x5E | yes | skewed high |
| 14 | tiredness V+0x5F | yes | skewed high |
| 16 | money V+0x48 (u16 pairs, tenths) | yes | triangular |
| 20 | walk speed V+0x60 | yes | triangular |
| 22 | V+0x62 | yes | triangular |
| 24 | V+0x63 | yes | triangular |

On load, `header[16]` guests are created (0x80051480), each placed at **exit point 1**
(0x800599A4(1, &pos) → 0x80093FC0), put in **state 45** (walk in, 0x80093F80(…, 0x2D)), given the
rolled attributes, a random type `rand(8)` (0x800926AC) and the animation byte set to 0x68 (anim 13 =
wander: `word +0x34 of the pool entry := (w & 0xFF07FFFF) | 0x680000`, 0x80091BEC..0x80091C10). Nothing else survives: not position, not the ride they were queuing for, not their
history. Captured: 2 guests, happiness range 29..50, money 2420..3860 tenths. (`behaviour.md`'s "slot
16 (0x80091A3C): load from save record… fields restored" is this; the fields are ranges, not values.)

## 7. Falsifiers (runner + RAM dump, all one read each)

Run them with `runner` (or `fable/sv/runner_mc`, which adds `MCARD=card.mcr` / `MCARD_OUT=` and
answers the core option `pcsx_rearmed_memcard1=libretro` so that memory card 1 is the file you give
it). Recipe that produced the capture, from `states/ui/01_laptop.state` (laptop list = Information /
Build & Hire / Park Statistics / Financial Information / Game Options / Leave Park):

```
10 14 down      # x5, one every 30 frames: highlight Game Options
170 174 cross   # open it: Music, SFX, Tutorial On, Screen, Save Game, Quit Current Game
250 254 down    # x5, one every 30 frames
400 404 cross   # Quit Current Game -> 0x80057EFC runs; RAM dump at frame 500 holds the stream
```
`python3 fable/sv/ramscan.py ram_000500.bin outdir` then `python3 fable/sv/parkstream.py parse
outdir/stream_p0s0.bin --research 8,0,1,1,13,6,3,2`.

1. **SVEDLEV snapshot** (done, PASS): after Quit, `ramscan.py ram.bin` shows entry (0,0) with a handle,
   the block starts `39 30 00 00`, and `parkstream.py` consumes it to within 3 bytes of its length.
2. **Record sizes**: place one shop, quit, dump: header byte +9 goes 0→1 and the unpacked length grows
   by exactly 28 (+ up to 3 pad). One flat ride: +152. One guard: +16. A path tile at (x,y): bit
   `y*44+x` of the bitmap flips. If a size is off, the section table in §3 is wrong at that row.
3. **Sandbox vs FullSim**: from `park.state` (0x80102D34 = 1) quit and dump: the stream must be shorter
   by `2·34 + 16 = 84` bytes (+pad) than the FullSim one and byte 0x22A of a save file made from it must
   be 0. `ramscan.py` prints `sandbox=`.
4. **Card file**: reach Save Game (Game Options, 4th item — my `cross` there did nothing visible; it may
   need a card-present prompt, see §8) and dump every 20 frames: an 0xA000 block with `01 59 41 47` at
   +0x208 and `AC 00` at +0x20C appears; `mcsave.py` on it must report `accepted_by_loader: true`, and
   `compose()` fed the same stream must reproduce it except for the title text and GENDATA bytes.
5. **Loader rejection**: take `streams/composed_quit.sav`, flip one payload byte, put it on a card
   (`mcsave.mkcard`) and boot to Load Game: the file must not load. Restore it, set 0x20C to 0xAB: not
   loaded. Restore, set status byte 0x22B to 0xF1: loads, but park 0 starts fresh (no ride at 19,40).
6. **Guest respawn**: quit with N guests in the park, reload the stream (enter park 0 again): the pool
   count `*(u32*)(*(u32*)0x80103884+0xC)` == N at once, every guest at exit point 1 in state 45, each
   V+0x59 within [min, min+range] of the record, and V+0x5A/V+0x5C *not* within it in general.
7. **Research counts** for parks 1–3: enter each park, read `0x800F29FC` (+8 park, +4 sub), then the
   eight words at `*(0x800DDDC4+4·park) + 4·sub + {16,32,48,80,96,112,128,64}`. Fill them into
   `save.json`.
8. **GENDATA is garbage**: two Quits from the same state must give identical 32 bytes (deterministic
   stack); two from different states may differ, and the game must load either. If a load ever
   *changes* state depending on those bytes, 0x8001758C is not the function that runs — re-read the
   vtable at `[[0x8010265C]+448]+4`.
9. **Path bitmap re-typing**: save a park with a ride whose queue tiles are type 13, reload, read the
   tile bytes: if they come back 2 not 13, queue paths are lost on save (INFERRED open point in §3.2).

## 8. Not located / open

- **Heightmap, water, land ownership**: no section. Terrain is the level's. SOURCED by the writer list.
- **Scenery**: no section.
- **Save Game UI**: `cross` on the 4th Game Options item produced no visible change and no MCBUFFER
  in RAM (frames 100–800 after the press), while `cross` on the 5th item quit at once. Either the item
  needs a memory-card prompt that the emulator's blank card did not trigger, or the panel expects
  another button. The composer and write op are fully read (§1), the composed file parses and is
  self-consistent, but **no game-written card file has been captured** — falsifier 4/5.
- **Load Game from a save state**: with a composed card inserted into a session restored from
  `states/mainmenu.state`, `cross` on the highlighted Load Game item did nothing (frames 450–1400) and
  the per-port card state at 0x800F335C stayed 0 (not 2 = ready): the card driver in that state never
  saw an insert event. A cold boot with the card present is the test that counts (§7.5); its result is
  recorded at the end of this file.
- **Shop +8 / +23 (A+0x84, A+0x80)** and **sideshow +8 / +22**: stored and restored, purpose unknown.
- **Coaster / track-ride element fields**: sizes and destinations SOURCED, meanings INFERRED.
- **McAi five tracks / five triples**: INFERRED objective series.
- **Research counts for parks 1–3**: runtime tables, need one RAM read each (§7.7).
- **Third stream and beyond**: the header has three slots; what the composer does with a fourth flagged
  park (writes past sp+0x34 into the mode byte) is UNMEASURED and should be treated as "do not".

## 9. End-to-end load test — result (2026-09-19)

Composed `streams/composed_quit.sav` from the captured stream, wrapped it in a card image
(`mcsave.mkcard`, file `BESLES-02688FABLE000`, 5 blocks), and booted the game cold with that card as
memory card 1 (`runner_mc`, core option `pcsx_rearmed_memcard1=libretro`). Outcome:

- The **BIOS** read the card: the kernel's directory cache holds `BESLES-02688FABLE000` (RAM
  0x8000BA92 in the dump at frame 6300). So the image is a valid PS1 card with a valid file entry.
- The **game never initialised its card manager**: 0x8006F61C (MemCardStart + per-port probe) has no
  static caller in TPW.BIN or TPW.OVL, 0x80102F98 stayed 0, the per-port state at 0x800F335C stayed 0,
  the game's own filename buffer 0x80109C58 stayed empty, and `cross` on the highlighted **Load Game**
  did nothing in three runs (from `mainmenu.state`, and from a cold boot at frames 4820 and after).
  The same is true of every save state on disk. The front-end menu code that reaches
  0x80073378 / 0x80073484 / 0x8006F61C is not statically referenced anywhere I can scan, so it is
  data-driven (a table the front end loads at runtime). **The composed file has therefore not been
  fed through 0x8006C524 on the console.** Everything in §1 is read from the composer and loader
  code; §7.5 is the experiment, and the quickest way in is a breakpoint on 0x8006F61C to find the
  caller, then a scripted path through that menu.
- Until then, the parts of this report that are measured as well as read are: the PAK codec
  (byte-identical), the SVEDLEV snapshot, and the park stream (§3, §4, §6).
