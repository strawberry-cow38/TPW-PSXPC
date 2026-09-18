# Theme Park World (SLES-026.88) — the debug menu, sandbox mode, and the name cheat

Eighth report. Every address is in the RAW image (`TPW.BIN` at 0x80010000) unless prefixed `ovl2`/`ovl11`
(TPW.OVL entries, decompressed to 0x80114158 with `ovl_lz.py`; copies in `d/ovl/ovlN.bin`).
Tags: **SOURCED** = read off instructions/data at the quoted address. **DERIVED** = inferred, with the
basis stated. **UNMEASURED** = guess. Scratch and tools in `d/` (`refscan.py`, `ovlxref.py`, `strsyms.txt`).

## 0. The answers, shortest form

| # | question | answer | tag |
|---|---|---|---|
| 1 | numeric id of STR_MAINMENU_DEBUG_MENU | **0x214 (532)**; ENTER_SANDBOX 0xAB (171); EXIT_SANDBOX 0x12 (18). None of the three is referenced as a string id anywhere in TPW.BIN or any overlay | SOURCED |
| 2 | how the laptop menu gates the debug entry | it doesn't exist there: the table 0x800F4884 has 15 entries and the builder 0x80075D74 adds entries by hard-coded index. **Compile-time absence**, not a runtime test | SOURCED |
| 3 | what to write to get the debug menu | **nothing can**: the window class survives (vtable 0x800E32B8, tick 0x800840E0, draw 0x8008422C, its label strings at 0x800E3210) but **its constructor is not in the image** and no vptr ever points at that vtable. What the menu *did* is 9 stores into plain globals — write those directly (§3) | SOURCED |
| 4 | sandbox mode | **it is the "restricted mode" flag 0x80102D34** (parkopen.md §2.4). Saves made with it set are titled `[TPW Sandbox] name` (0x80073484). Only writer is the memory-card loader (byte 0x22A of the save == 0); no in-game toggle exists. `u32 [0x80102D34] = 1` live. It does **not** load a prebuilt park | SOURCED |
| 5 | what the debug menu does | 9 sliders → 9 visitor-AI tunables: nausea/ride, litter/shop, ride happiness ×3, three unhappiness thresholds, prank chance. Table in §3 | SOURCED (mapping DERIVED-strong) |
| 6 | name-entry cheat ("bovine") | **does not exist in this build.** The only keyboard is the save-filename one (A–Z + space, 7 chars, uppercase only); the buffer is only ever `strcmp`'d against existing card titles. No pad-mask test for X+□+○ | SOURCED |

## 1. STR_MAINMENU_DEBUG_MENU = 0x214, and where it is (not) used

**Resolution (SOURCED).** The symbolic table is FOLIO entry 0x19A (`rip/0410.bin`); `d/strsyms.txt` pairs
it with the English table (FOLIO 0x197). Id = position: DEBUG_MENU = 532 = **0x214**,
ENTER_SANDBOX_MODE = **0xAB**, EXIT_SANDBOX_MODE = **0x12**. Cross-check: OPEN_PARK 0x242, LEAVE 0x31F,
GAME_OPTIONS 0x20B, BUILD 0x2F5 match the ids parkopen.md read out of the menu table.

**Every occurrence of the three values, all images, all regions (`d/refscan.py`), and what each is:**

| value | where | what it actually is | tag |
|---|---|---|---|
| 0x214 | 0x800502A0 `li a2,0x214` | `__LINE__` argument of the pool ctor for "PoolOfPeople"... (the sequence 0x207..0x214 is line numbers of `psxsrc/land/land.cpp`) | SOURCED |
| 0x214 | 0x800DD280, 0x800DD5FC (u32) | two identical 5-word tables {0x20D,0x212,0x214,0x217,0x218}; no code reference; neighbours are not string ids | SOURCED (unreferenced), meaning UNMEASURED |
| 0x214 | 0x800DF9E2.. (u16 ×3) | monotone ramp 0x216,0x216,0x215,0x215,0x214… (a LUT) | SOURCED |
| 0x214 | 0x801025F0 (u16) | field of a static struct (026C 0213 0004 01E4 **0214** 0002…) | SOURCED |
| 0xAB | 0x8001F594, 0x800B4394 `li` | 0xAF/0xAB sprite-tile parameter; 12-byte record index passed to 0x80028BAC | SOURCED |
| 0xAB | 0x800EEE8C (u16) | rect table {x,y,w,h}: (0x2E4,2),(0x194,0x10),(0xAB,0x10) | SOURCED |
| 0xAB | 0x800F9B64 (u16) | ramp LUT | SOURCED |
| 0x12 | ovl4 +0 | overlay id (entries are numbered 0x0E..0x19) | SOURCED |
| 0x12 | ovl11 0x80114218.. | world-map park index (parks are 0x0F..0x16, paired with STR_MAP_* names) | SOURCED |
| 0x12 | 0x800EF6F8 | parameter field of tutorial record {0x1D2,1,**0x12**,0xF}; siblings carry 0x1BC six times, so not a string id | SOURCED |
| 0x12 | 0x800FB260, 0x800F0460, 0x800F9978 | ramp LUTs | SOURCED |

**Calibration (SOURCED):** the string function is 0x8006F00C (table `gp` 0x801039A0; the only other reader
is 0x8006F048 via 0x8006EFBC). Ids reach it as immediates (29 sites, listed in `d/`) or from tables:
laptop menu 0x800F4884, Game Options 0x800F4E48 {0x17C,0x308,0xCB,0xE,**0xE0**,**0x2CE**}, memcard
messages ovl2 0x80114424 (u16), world-map dialogs ovl11 0x80114404 (u16 triples), tutorial records
0x800EF65C… . The rest of the unused MAINMENU family — PURCHASE 0x62, LOAD_SAVE 0x91, MAIN_MENU 0x3F8 — is
**also unreferenced**, and the table carries "This should be removed" and "StrTestSubgame" entries. The
string table is a superset shared with the PC build; DEBUG_MENU and the two SANDBOX ids are part of the
unused remainder.

## 2. How the laptop menu is declared, and why there is no debug entry (SOURCED)

- **Table** 0x800F4884, 15 × 8-byte `{u32 text_id, u32 handler}`, index 0..14 (0 = 0x1FB "Information",
  handler 0 — a submenu head; 1..5 = ride/shop/sideshow/toilet/staff info; 6 BUILD_AND_HIRE 0x80075354;
  7 RESEARCH 0x80075260; 8 PARK_STATS 0x800758B0; 9 FINANCE 0x80075750; 10 GOLDEN_TICKETS 0x800759A4;
  11 GAME_OPTIONS 0x800743A4; 12 OPEN_PARK 0x80075ABC; 13 LEAVE 0x80075A98; 14 BUILD 0x80075550).
  Entry 15 onward (0x800F48FC..) is zero and is **not spare table space: it is the live item list** (below).
- **Add-item** 0x80075D38 `(menu, idx)`: `0x800F48FC[menu+628] = 0x800F4884 + idx*8; menu+628 += 1`.
  So the current menu's items are readable at **0x800F48FC** (pointers into the table), count at menu+628.
- **Builder** 0x80075D74 adds by literal index, in this order: if `[0x80102D34] != 0` → only **0xE (Build)**
  then 0xB (Game Options) then 0xD (Leave). Otherwise: 0 (Information, only if any info source exists,
  0x8005302C..0x80053344), 6 (Build&Hire, if 0x80052DA8 && 0x80052D3C), 7 (Research, if 0x8005381C), 8, 9,
  0xA (Gold Tickets, if 0x8006BE1C() != 0 && 0x8006C03C() == 0), 0xB, 0xC (Open Park, only while
  IsOpen()==0), 0xD.
- **Verdict:** no index refers to a DEBUG entry, no table row carries 0x214, and no handler function for it
  exists (§3). The entry is absent at compile time. There is no runtime bit to flip.

## 3. The debug window: what survived, what didn't, and the levers it moved

**What survived (SOURCED).** In the object file that also holds the Gold Tickets window (ctor 0x80083ABC,
vtable 0x800E3138, list sub-object vtable 0x800E31B8 ← ctor 0x80083FC4), the linker kept:

- the **literal pool** at 0x800E3210: `"Debug Menu"`, `"Vomit/Ride Inc"`, `"Min Litter Increase"`,
  `"Ride OK Inc"`, `"Ride Good Inc"`, `"Ride Excellent Inc"`, `"Vomit/Happiness"`, `"Toilet/Happiness"`,
  `"Boredom/Happiness"`, `"Prank Chance"` — **referenced by nothing**: no data pointer, no lui/addiu, no
  lui/lw, in any region of TPW.BIN, SLES_026.88, or the 12 overlays;
- a **vtable at 0x800E32B8** whose own slots are 0x8008422C (draw: 9 slider gizmos at obj+0x26C, stride
  0xF0, highlight = obj+616), 0x8008435C (close), **0x800840E0 (tick)**; the rest are base-window slots
  shared with the sibling windows (0x800E3338 Game Options ← ctor 0x800843A0, 0x800E33B8 ← 0x800855B8,
  0x800E3440 ← 0x80085C54). **No instruction anywhere forms 0x800E32B8** — the ctor that would install it
  is not in the image, and the literals it would have passed to the sliders are the orphaned pool above.

**What the tick does (SOURCED, 0x800840E0):** pad 0x8008974C(0); bit0/bit1 move the cursor obj+616 over
0..8; then nine `0x8004236C(slider_i)` reads are stored, in this order:

| slider | writes | default | reader (fn) | what the reader does with it | label (DERIVED: pool order = slider order; all nine agree with the reader semantics) |
|---|---|---|---|---|---|
| 0 (obj+0x26C) | **0x801031FC** = (v<<12)·0.01 | 0x4BC = 1212 | 0x8008F110 (Unloading 22) | nausea += v·(intensity−30)>>12 (behaviour.md §2.4) | Vomit/Ride Inc |
| 1 (+0x35C) | **0x8010322C** | 0x1E = 30 | 0x8008E5EC (shop buy) | rubbish += 30 + rand(25) on a purchase | Min Litter Increase |
| 2 (+0x44C) | **0x80103208** | 5 | 0x8008F110 | happiness +5 when |pref−intensity| ≥ 51 | Ride OK Inc |
| 3 (+0x53C) | **0x8010320C** | 0xA = 10 | 0x8008F110 | +10 when < 51 | Ride Good Inc |
| 4 (+0x62C) | **0x80103210** | 0xF = 15 | 0x8008F110 | +15 when < 21 | Ride Excellent Inc |
| 5 (+0x71C) | **0x8010321C** | 0x55 = 85 | 0x8008FE60 (slot 41) | nausea ≥ 85 → happiness −1 per 64 ticks | Vomit/Happiness |
| 6 (+0x80C) | **0x80103220** | 0x5A = 90 | 0x8008FE60 | toilet need V+0x5D ≥ 90 → −1 | Toilet/Happiness |
| 7 (+0x8FC) | **0x80103218** | 0x5F = 95 | 0x8008FE60 | boredom V+0x5C ≥ 95 → −1 | Boredom/Happiness |
| 8 (+0x9EC) | **0x80103230** | 0xA = 10 | 0x8008D058 (Idle) | `rand(1000) < 10` → 0x80053768 (the prank branch) | Prank Chance |

All nine are plain 32-bit statics (bytes for the thresholds are read with `lbu`, so keep values < 256) and
**nothing else writes them** (`xref.py`). Poke them directly; that is the entire effect the menu had.
Neighbours in the same block that the menu did *not* touch but are the same kind of lever (SOURCED, reader
in brackets): 0x80103200 = 0x1000 boredom drop scale [0x8008F110]; 0x80103214 = 30, 0x80103224 = 95,
0x80103228 = 85, 0x80103238 = 50, 0x8010323C = 40 [0x8008FE60]; 0x80103234 = 100 and 0x80103260 = 25 litter
roll/threshold [0x8008D058]; 0x80103240 = 100 [0x8008EE78]; 0x80103244 = 10000, 0x8010324C = 0x1400,
0x80103250 = 0x1800, 0x80103254 = 0xC00 fee verdict [0x80090D5C]; 0x80103258 = 70, 0x8010325C = 75
[0x8008C818]; 0x8010327C = 63, 0x80103280 = 11 [0x800962E8]. Plus the earlier finds: 0x80102E60 (20
guests/bus), 0x80102E54/0x80102E50 (arrival rate), 0x801031CC (free-money, economy.md).

**Could the window be resurrected?** Only by supplying the missing ctor (allocate ~0xA00 bytes, base-window
init, nine slider gizmos of class 0x800422CC, vptr := 0x800E32B8) and a menu row + handler — i.e. writing
code, not a word. Not worth it: the tick's whole output is the table above.

## 4. Sandbox mode = the flag at 0x80102D34

**Identification (SOURCED).** The front end's save/load code passes `kind = (0x80059A9C() == 0)`
(ovl2 0x8011A77C..0x8011A798 and 0x8011AEE4..0x8011AF94) into 0x80073204 / 0x800730C0 / 0x80073484, which
format the memory-card title as **`"[TPW Sandbox] %s"`** (0x800E1EEC) when kind == 0 and
`"[TPW FullSim] %s"` (0x800E1F00) when 1 (`STP` variants when 0x800BE730() == 2, the US SKU). 0x80059A9C
returns 0x80102D34. So the "restricted mode" of parkopen.md §2.4 / behaviour.md is the sandbox flag.
Sandbox titles are stored lower-cased, FullSim upper-cased (0x80073574..0x80073594) — that is how the
load list filters the two kinds (0x80073204, `strncmp` against the 13-char prefix).

**Writers (SOURCED, exhaustive):** one — 0x80059AA8(v), called only from the card loader 0x8006C524 at
0x8006C5C8 with `v = (save[0x22A] == 0)`; it stores v and, if nonzero and the park is closed, calls
OpenPark 0x80054144. The saver 0x8006C0E8 writes `save[0x22A] = !flag` (0x8006C1EC..0x8006C200). A fresh
game never sets it, so **every save this build can make has byte 0x22A == 1 and reloads as FullSim**: the
mode is unreachable through the UI. ENTER/EXIT_SANDBOX (0xAB/0x12) being unreferenced is the same fact
from the string side — the toggle UI was not built.

**Two ways in:**
1. **Live:** `u32 [0x80102D34] = 1`, plus `u32 [0x80102D30] = 1` (the open-park store the setter would have
   done; do it via parkopen.md §3.1 so 0x80102D24 is sane). Every reader tests the word live, per frame or
   per event — no cached copy found (all 30 read sites are `lw gp+1760` immediately before the branch).
2. **In a save:** set byte **0x22A** (game header at file +0x200: +0x208 magic 0x47415901, +0x20C u16 0xAC,
   +0x22A mode byte, +0x22B.. per-park nibbles) to 0. The loader first checks `u32[+0x200]` against
   0x800708EC(+0x204, len) (DERIVED: a checksum) and `u32[+0x200] == u32[+0x9FFC]`, so the checksum must be
   recomputed — the live poke is cheaper.

**What the flag changes (SOURCED sites; meanings DERIVED from the branch bodies / earlier reports):**
laptop menu = Build, Game Options, Leave only (0x80075D98); park opened by the setter; no litter spawn
(0x800514E0); Idle skips the needs/litter block (0x8008D228); thought bubbles suppressed (0x800903E8);
objective/goal checks skipped (0x800676EC, 0x8006795C); advisor message windows skipped (0x8003867C,
0x8003892C, 0x80038E68, 0x80038F9C, 0x80039608, 0x8003980C); the four ride-class routines at 0x800A0278,
0x800A1CCC, 0x800A86B8, 0x800B05F8 return 0 immediately (the same shape in all four — UNMEASURED which
behaviour, likely wear/breakdown); the saved-park stream skips a block on load and save (0x80071604,
0x80072B6C); year-end/calendar routine skips (0x800B9A6C); info submenu (0x80076044); 0x80013230,
0x80017840, 0x80049324, 0x8006A95C untraced. Front end: with the flag set the post-menu state is 0xC0008
(0x800BCBD8(1), the card screen) instead of the world map 0x50008, and the park runner 0x800BCB34 starts
park **(0,0)** via 0x80050524 — the flag does **not** fetch a prebuilt park. There is no prebuilt-park
source in the image other than a card save (parkopen.md §5 stands). strawberry's "prebuilt park" is
UNMEASURED here; if it exists it is the Practice Park path, not this flag.

## 5. The name-entry cheat: negative, with the evidence

- **The only text entry in the game is the save-filename keyboard** (SOURCED): rows `"ABCDEFGHI"`,
  `"JKLMNOPQR"`, `"STUVWXYZ "` at ovl2 0x80114454/60/6C, reached through the pointer table 0x801024E8
  (ovl2 0x8011ACBC, 0x8011B034); buffer ovl2 **0x80114120**, capped at 7 characters (0x8011B01C..0x8011B030).
  Uppercase only — "bovine" cannot be typed.
- **What happens to the buffer** (SOURCED, ovl2 0x8011AEDC..0x8011AFB0): OK → empty check → 0x800730C0
  (duplicate check: `sprintf("[TPW Sandbox|FullSim] %s")` then `strcmp` 0x800CCEC8 against each existing
  card title) → 0x80073484 (build title, save). No other consumer.
- **All string-compare call sites in game code** (SOURCED, every `jal` to 0x800CCEB8/0x800CCEC8 in
  TPW.BIN + overlays): 0x800731A4 and 0x80073300/0x8007391C (the three above), 0x80070158 (name table
  0x800F336C lookup), and PsyQ-internal ones. No compare of any user text against a constant.
- **No pad-combination test**: no `andi` against 0xE000 (□+×+○) or 0x00E0 anywhere in game code; the five
  `ori ...,0xE000` hits are `lui 0xC000`/`ori` pairs forming the GTE constant 0xC000E000.
- `bovine` (also `BOVINE`, UTF-16, reversed) is absent from TPW.BIN, SLES_026.88, TPW.OVL, all 12
  decompressed overlays, FOLIO.GAZ, and the one UNPAK-compressed FOLIO entry.
- No strcmp-family function is even linked except `strcmp`/`strncmp`/`strlen` used above (named_union has
  no `strcmp`; 0x800CCEC8 is identified by its callers' string args).

The published cheat is a PC-version cheat (the PC front end had a nickname box; this front end has none).

## 6. Falsifiers (read live)

| claim | read | expect |
|---|---|---|
| debug tunables are live globals | `u32 [0x801031FC]`, `u8 [0x80103208/0C/10]`, `u8 [0x8010321C/20/18]`, `u32 [0x8010322C]`, `u32 [0x80103230]` | 0x4BC, 5/10/15, 85/90/95, 30, 10 on any park; after poking 0x80103210 := 100, a guest leaving a matching ride jumps V+0x59 by 100 (clamped) |
| debug window never exists | scan RAM for the u32 0x800E32B8 at any object+0x10 | never found; 0x800E3138 (Gold Tickets) appears while that window is open |
| sandbox flag is live | after `[0x80102D34] := 1`, open the laptop: `u32 [0x800F48FC..0x800F4907]` | exactly {0x800F48F4, 0x800F48DC, 0x800F48EC} (Build, Game Options, Leave) |
| sandbox title | save to card with the flag set; read the card title | begins `[TPW Sandbox] ` (lower-cased name); byte 0x22A of the block == 0 |
| sandbox flag reload | load that save | `[0x80102D34]` == 1 and `[0x80102D30]` == 1 immediately (setter path) |
| name buffer | type a name on the save keyboard | ovl2 `0x80114120` holds it, ≤ 7 uppercase bytes; nothing else changes |
| no debug menu row | `[0x800F48FC..]` after opening the laptop in a FullSim park | never contains a pointer to a row whose text id is 0x214 (none exists) |

## 7. Corrections / notes for earlier reports

- parkopen.md §2.4 called 0x80102D34 "restricted mode, name GUESS": it is **sandbox mode** (SOURCED via
  the save title). Its write path and the `!byte42` inversion are as described there.
- The item list 0x800F48FC.. is not "zero padding after the table"; it is the live menu, rebuilt on open.
- The `0x8006C078` word (0x80102EB4) that the front end branches on is the **speech on/off** option
  (0x8008486C toggles it and swaps 0x800F4E50 between 0xCB SPEECH_ON and 0x320), not a game type.
