# How a PAL park is defined on the disc

READ = disc bytes or the instructions at the quoted address. Addresses in TPW.BIN use
base **0x80010000**, so its file offset is **address − 0x80010000**. Overlay addresses
use base **0x80114158**; an offset in an expanded overlay is not an offset in its compressed file.
This is SLES-026.88 only. No console measurement was made.

**Result:** there is no single established scenario blob. The producer is a combination of
**compiled world/park catalogue lists and objective records in TPW.BIN**, **terrain and individual
attraction definitions in FOLIO.GAZ**, and separate **initialization code/live mode state**.
The catalogue's previously unknown track/coaster/tour lists are recoverable from executable data.
The supposed scenario fee override is a **memory-card save record**. One more objective byte is
resolved: **+0x31 is the advertised park Gold Ticket total**. **30 bytes remain uninterpreted.**

There is also real bytecode: **FOLIO entries 1/2**, interpreted by **0x80017024**, drive the common
advisor rules. This was already established in `statistics.md`; it is not a newly discovered
per-park objective VM. No VM is added here.

**Follow-up:** [objbytes.md](objbytes.md) supplies readers for the remaining world sprite-list
pointers, decodes world-map selection, accounts for the missing ticket sources, and bounds the
remaining **30** objective bytes. The original findings and disagreement decisions below remain.

## §0 SOURCE DISAGREEMENTS

Existing findings and their existing implementations are retained. The new decoder is an opt-in
source reader; it does not replace the game's current union catalogue or sandbox behavior.

| Existing source and address | This read, with addresses | Retained behavior / scope |
|---|---|---|
| `rides.md` §1.2 and `research.md` §3.1 leave the track/tour/coaster arrays behind the table at **0x800DDDC4** runtime-populated/unestablished; `Attraction.cs` says those rosters are not in TPW.BIN | Initializer **0x8002ED50..F554** installs pointers to **initialized executable data**, including **0x80102880/88/98/A4/B8**, not absent BSS arrays. **0x8006A3CC** reads them directly. All 64 list counts and all 253 ordinary catalogue entries agree with executed original getters. | Leave `AttractionCatalog.ForWorld` and its current union policy unchanged. `PalParkDefinition` separately decodes the actual selected list from the caller's executable. This supplies the previously missing producer without silently changing gameplay. |
| `debug.md` §4: **0x80059AA8** is called **only** from card loader **0x8006C5C8**, therefore no UI setter path | Expanded **overlay 2** also contains **0x80117A30 → 0x80059AA8**, with **a0=0** at **0x80117A28** or **a0=1** at **0x80117A2C**. OVL2 compressed stream is **TPW.OVL+0x11B4**, size **0x406D**; call is expanded **+0x38D8**. | Retain existing inverse card-mode contract and initial false state. This establishes another caller, **not** that a retail menu can reach either case. Menu-command reachability remains open. |
| `psx-assets.md` §1 lists **7 STRs**, omits LOG.BAK, and describes raw-sector audio evidence | This supplied ISO's PVD is at **ISO+0x8000** with **2048-byte sectors**; its root has **18 files**, including **8 STRs** and **LOG.BAK**, LBA **196335**, length **28895**. | Keep prior asset/audio findings. This cooked image cannot independently repeat the earlier Form-2 subheader measurement. LOG.BAK is inspected separately below; none of these files is reclassified as a scenario. |

**Already corrected upstream, not a new computational disagreement:** `economy.md` §4.1/§6.3
calls **0x80071F2C** a scenario fee override; `parkopen.md` §0/§2.2 and `goals.md` §4 explicitly
identify its **saved gate record**. The save interpretation is retained. Likewise the old
`litter.md` script caveat is narrowed by `statistics.md`'s subsequent complete advisor-program
decode, not by assuming that scripts do not exist. Its opcode-8 disagreement remains untouched.

## 1. Files, locations, and the selection chain

The directory audit reads the ISO itself and compares five extracted files byte-for-byte.
There are no subdirectories in its 18-file root. For this cooked ISO, an extracted-file byte
offset **f** maps to **2048 × LBA + f**.

| File | LBA | First byte in supplied ISO | Role established here |
|---|---:|---:|---|
| TPW.BIN | 205194 | **0x190C5000** | executable initialization, static world/park lists, objectives, mode globals |
| FOLIO.GAZ | 174726 | **0x15543000** | terrain/scenery, attraction definitions, common advisor programs |
| TPW.OVL | 205715 | **0x191C9800** | twelve compressed MIPS overlays; world-map UI reads objective +31 |
| SLES_026.88 / SYSTEM.CNF | 203206 / 205193 | **0x18CE3000 / 0x190C4800** | boot chain; existing `psx-assets.md` analysis retained |

READ, established file lookup (`folio.md` §1, not re-decoded): **0x80024624** checks the file
cache; **0x800249B0** constructs `\\%s;1` and calls CdSearchFile. **0x800BBF2C** loads FOLIO
entries through **0x80025650 → 0x800252D0 → 0x80024538**. **0x800BBFA0** reads an entry offset
at directory **+8+8×index**, and the size is the following word. The file's **422** pairs are
`{u32 offset,u32 size}` after the two-word header. Entry numbers are decimal throughout this report.
No filename such as `park0.scn` is synthesized by the recovered park-loading path.

READ park selection:

1. Live world and park are **[0x801038A0] / [0x801038A4]**, getters **0x80054000 / 0x80053FF4**.
2. **0x8002ED50** constructs the four world structures listed by **0x800DDDC4**
   (file **+0xCDDC4**), from immediates and initialized data. Structures are spaced **0xB0**;
   the initializer zeroes and copies **0xAC** bytes of each. No file parser is hidden inside it:
   it executes **914 instructions**, with only memset stubbed in the audit.
3. **0x800588F4 → 0x8002EB98(world,park)** preloads the selected ordinary attraction lists.
   **0x8002EACC** walks count entries at four-byte strides and calls **0x8002F5CC** for each.
4. Manager constructor **0x80069580** stores **park at +4**, **world at +8**. Its count/entry
   getters select those same lists. **0x8006972C** separately loads type-8 entry/handle pairs.
5. **0x80050638/40** get map/scenery IDs through leaves **0x80054760 / 0x80054794**;
   **0x8005064C → 0x800544E0** loads and parses the selected map.
6. Objective selector **0x80067CD8(world,park)** independently returns a compiled 52-byte record.
   It returns null outside world **0..3**, park **0..1**, including negative inputs.

`ext/rip/` is an extraction convention, not a disc directory. It currently contains **690** files:
**422 numeric `NNNN.bin` archive entries**, all compared to the ISO-backed GAZ, and **268 `eNNNN.bin`
derivatives**, not a second population of disc records. No numeric entry is omitted by size.

**Additional directory check:** LOG.BAK occupies **ISO+0x17F77800**, length **0x70DF**. It is ASCII
CD-recording output: timestamps, ADAPTEC adapter / SONY CDW-900E, writer status and cue-sheet
operations. All **359 lines** were included in the keyword check; park/map/goal/script/level/
research/scenario/catalogue terms have **0 hits**, with **“Operation started” present** as a
positive text-search control. Its readable content, not the filename or the negative alone,
identifies its role. The existing media/padding classification is retained for other files;
those payloads were not rescanned for hidden data.

## 2. Compiled world record and catalogue restriction

World pointers in order 0..3: **0x8010558C, 0x801054DC, 0x8010542C, 0x8010537C**.
For each ordinary category, read the list pointer at **record+P+4×park**, and the count at
**record+P+8+4×park**. A list consists of **u32 FOLIO entry IDs**, in menu/type-local order.

| World-record offset | Size/layout | Meaning / reader |
|---|---|---|
| +00/+04 | pointer/count | two `{u32 map,u32 scenery}` pairs, **0x80054760/794** |
| +08..17 | two pointers, two counts | type **3**, ordinary rides; count **0x8006A214** |
| +18..27 | same | type **7**, tour rides; **0x8006A244** |
| +28..37 | same | type **6**, track rides; **0x8006A274** |
| +38..47 | same outer layout | type **8**, track upgrades; **0x8006A2A4**; inner records have **8-byte stride** |
| +48..57 | ordinary lists | type **1**, roller coasters; **0x8006A2D4** |
| +58..67 | ordinary lists | type **2**, features; **0x8006A304** |
| +68..77 | ordinary lists | type **4**, shops; **0x8006A334** |
| +78..87 | ordinary lists | type **5**, sideshows; **0x8006A364** |
| +88..9F | three pointer/count pairs | not interpreted by this decoder; bounded **24 bytes** |
| +A0 | u32 | ground texture entry; existing `ParkWorlds`, **0x800547C8** |
| +A4/+A8 | two u32 | extra texture entry indexed by park; **0x8005891C..34** |
| +AC..AF | four-byte spacing gap | not initialized/copied by the traced constructor; not labelled a field |

**The restriction is the selected roster, not a discovered restriction bit.** Counts:

| world,park | ride 3 | track 6 | tour 7 | coaster 1 | feature 2 | shop 4 | show 5 | upgrade 8 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 0,0 | 8 | 1 | 0 | 1 | 13 | 6 | 3 | 2 |
| 0,1 | 6 | 1 | 1 | 2 | 13 | 6 | 3 | 1 |
| 1,0 | 8 | 1 | 1 | 1 | 14 | 6 | 3 | 1 |
| 1,1 | 6 | 1 | 0 | 2 | 14 | 6 | 3 | 2 |
| 2,0 | 7 | 1 | 0 | 2 | 12 | 6 | 3 | 1 |
| 2,1 | 6 | 1 | 1 | 1 | 12 | 6 | 3 | 2 |
| 3,0 | 7 | 1 | 0 | 1 | 13 | 6 | 3 | 1 |
| 3,1 | 6 | 1 | 1 | 2 | 12 | 6 | 3 | 0 |

The audit records **all 64 source addresses/file offsets, lengths and hashes** without distributing
the attraction parameters. It executes the original count leaves for all 64 groups and entry
selector **0x8006A3CC** for **253 ordinary occurrences**. All eight native preload walks also
match the decoded order. Together with **10 type-8 pairs**, there are **263 occurrences / 196
distinct assets**. These are selected assets, not a claim that the entire disc has only 196 definitions.
There are **five empty category lists**, and all were tested.

Concrete control: world 0/park 0's track list is **0x80102888**, file **+0xF2888**, count 1,
entry **215**. Park 1 selects **0x801028A4**, file **+0xF28A4**, entry **228**. Coasters use
**0x80102880** (one entry) versus **0x801028B8** (two). The source words themselves are in TPW.BIN.
No spatial/theme-range guess is needed.

Type-8 pair format: **u32 entry; u32 runtime handle**. **0x80069790** computes `index*8`,
**0x80069798** loads entry +0, and **0x800697D4** stores the returned handle at +4.
**0x80069F94** later reads/locks that handle. These assets have the type-8 definition at
**file +0**, whereas ordinary catalogue definitions sit at the **0x96 container's header +0x14
offset**. All 263 selected records' type words match their lists, including the short records.
Handle words survive the decoder's round trip even when a synthetic fixture makes them nonzero.

Research tier/work fields are already decoded in `research.md` §3.2: rides at
**definition+0x48/+0x4C+0x34×level**, other types at **+0x28/+0x24**. Their values belong to the
individual FOLIO definitions, not the 52-byte objective record. Initial free availability is
determined by those tiers through the existing `ResearchSystem`. This work supplies type-local
roster indices, including the separately loaded type-8 roster; it does not merge the two parks.

## 3. The 52-byte record: one more byte resolved, thirty bounded

Records start at **0x800E1930 / file+0xD1930**, stride **0x34**, in world-major/park-minor order.
The eight addresses and threshold values remain those in `goals.md` §1.

| Offset | Size | Established meaning / source |
|---|---:|---|
| +00,+04,+08 | 12 | **OpaqueWord00/04/08**; no interpretation established |
| +0C | 4 | admissions threshold, **0x80067980** |
| +10 | 4 | signed profit pounds, **0x800679C8** |
| +14 | 4 | years-open minimum, **0x80067A60** |
| +18 | 4 | feature-value pounds, **0x80067BF0** |
| +1C | 4 | maximum path tiles, **0x80067C8C** |
| +20,+24,+28,+2C | 16 | **OpaqueWord20/24/28/2C**, each 0xFFFFFFFF in all eight |
| +30 | 1 | tutorial award enabled, **0x80067A90** |
| **+31** | **1** | **advertised Gold Tickets for this park**, overlay 11 **0x8011715C / 0x801171A8** |
| +32,+33 | 2 | **OpaqueByte32/33**, both zero in all eight; not assumed padding |

READ new consumer: overlay 11 calls **0x80067CD8** for all eight pairs at **0x80117154**, sums
`lbu +49` into an accumulator initialized to **5** at **0x8011712C**, then reads the selected
park's same byte at **0x801171A8**. It subtracts that park's acquired-count getter
**0x8006BEF0**, and formats obtained/total and remaining/total values at **0x801171C4..1E4**.
The values are **7,6,5,6,5,6,5,5**: 45 park tickets plus the five common bonuses = **50** advertised.
This identifies a display total; it does not establish the missing ticket-award conditions.

OVL11's compressed stream starts at **TPW.OVL+0x8C7E**, length **0x1F41**, and expands to
**0x33CC bytes**. Reads **0x8011715C / 0x801171A8** are expanded offsets **+0x3004 / +0x3050**.
There is no direct compressed-file byte offset for an expanded instruction without replaying LZ.

The first two opaque words happen to equal assets offered by each selected catalogue, e.g.
world 0/park 0 has **227 and 236**. This is a **value match**, not evidence of what those fields
do. They are not renamed “starting rides”, “unlocks”, “fees” or “script pointers.” The third word
is 3 or 5 and remains opaque.

Bounded reference search: **all 296,961 aligned words** of TPW.BIN plus all twelve expanded
overlays were scanned, skipping **0 aligned words / 0 trailing bytes**. The selector's direct
callers are its current-park wrapper and the two overlay-11 sites; the wrapper's two callers
are the already traced descriptions/evaluator. Eight address-pair candidates recover exactly
the eight selector returns; there are no objective-range pointer-word candidates. No extra
reader of the remaining bytes was established in these consumers.

**Control caught an instrument failure:** linear register-clobber tracking initially found zero
objective addresses, including the known selector returns. Conditional branches and return delay
slots break that search. The recorded search instead conservatively checks the next 16 words
after each LUI, and requires **all eight known return addresses** as its positive control.
It is a candidate search, not a proof against arbitrary pointer arithmetic or aliased access.

## 4. Economics and research flags have different producers

**Starting money, READ:** **0x80058AA0**, file **+0x48AA0**, is `ori a1,zero,0xC350` = **50000**;
**0x80058AA4** supplies zero pence; **0x80058AA8 → 0x80061848** converts to native money,
and **0x80058AB4 → 0x800868B0** stores BANK+4. This supersedes the bank constructor's £10000
default on the fresh park path. It is common initialization code, not a per-park table field.
The subsequent saved-park restore may replace it.

**Entry fee, READ:** bank constructor **0x800865D4**, file **+0x765D4**, supplies **40 pounds**
to **0x80088E88**, then setter **0x80087240** at **0x800865DC**. Saved park loading
**0x800588D0 → 0x8006B92C → 0x8006C850 → 0x80071D0C** reaches **0x80071F2C**.
That routine reads four aligned bytes **{u8 open, opaque byte, u16 fee pounds}**; fee is read at
**0x80071F6C**, converted and stored at **0x80071F7C**. The established park stream places it at
**stream+0x14**, fee at **+0x16** (`save.md` §3). This is a stream-relative offset, **not a FOLIO
entry offset**. No new saved-gate codec is needed; the existing save implementation owns it.

**AllResearchUnlocked, producer READ:** the initial word at **0x80102E88**, file **+0xF2E88**, is
zero. It is targeted by the third **12-byte pad-sequence record** at **0x800F3224**, file
**+0xE3224**:

| record field | value / meaning |
|---|---|
| +0 u32 | sequence pointer **0x800E1B04**, file **+0xD1B04** |
| +4 u8 | sequence length **10** |
| +5 u8 | repetition count **8** |
| +6/+7 u8 | runtime sequence/repetition cursors, initially zero |
| +8 u32 | target **0x80102E88** |

The ten u16 masks are **1,2,1,2,4,1,2,1,2,8**. These are masks, not labelled physical buttons
without tracing the pad mapper. **0x8006E22C** walks six such records; **0x8006E27C..294** tests
input intersection with the next mask and advances, **0x8006E298..29C** resets on mismatch,
**0x8006E2CC..300** toggles `*target` after the requested repetitions. Its park-tick caller is
**0x800527F8**, and overlay 11 also calls it at **0x80114B00**.

Audit control executes the original matcher with only pad-input/effect hooks: wrong nonzero input
leaves the override zero; 80 matching inputs turn it **0→1**, a second 80 turn it **1→0**.
Thus a direct-store-only search would incorrectly miss its producer. The pointer-word search's
positive control is **0x800F322C → 0x80102E88**. The existing research readers remain
**0x8006A92C / 0x8006AC04**. This six-pattern input table is not a park behavior VM.

**RestrictedMode, READ:** initial **[0x80102D34]**, file **+0xF2D34**, is zero. Setter
**0x80059AA8** stores it at **0x80059AB0**, and opens a closed park if nonzero. Card loader
**0x8006C5C0..CC** passes **header[+0x2A] == 0** (whole card file +0x22A). It is independently
named Sandbox by the existing save-title evidence. See §0 for the extra overlay setter caller;
this decoder does not infer a retail UI mode selection from it.

## 5. Terrain files: reuse the established format

The map-pair lists at **0x800DDE88/98/A8/B8**, TPW.BIN file **+0xCDE88/98/A8/B8**, select:

| world,park | FOLIO map entry | GAZ byte offset | Map bytes | Scenery entry |
|---|---:|---:|---:|---:|
| 0,0 | 203 | 0x381000 | 27552 | 205 |
| 0,1 | 204 | 0x388000 | 27684 | 205 |
| 1,0 | 116 | 0x288000 | 27300 | 118 |
| 1,1 | 117 | 0x28F000 | 27300 | 118 |
| 2,0 | 34 | 0x186000 | 28212 | 36 |
| 2,1 | 35 | 0x18D000 | 28356 | 36 |
| 3,0 | 355 | 0x6A0000 | 27504 | 359 |
| 3,1 | 356 | 0x6A7000 | 27504 | 359 |

`psx-assets.md` §5q/5r and `ParkMap` already decode the terrain; no replacement is introduced.
READ **0x8005452C..5C**: `u32 shadeCount; u32 shades[shadeCount]; u32 width,height`.
**0x80054564..78** advances over **width×height×8 tile bytes**. **0x8005457C..B4** reads
`u32 sceneryCount; sceneryCount×12 placement bytes`, then **0x800545B8..C4** reads
`u32 spawnCount; spawnCount×2 coordinate bytes`. Every shipped map is **44×74**, shade count 64;
tile grid begins at **map+0x10C**, ends **+0x66CC**. The eight variable scenery lists contain
**971 placements**, followed by two spawn pairs per map. Parsing reaches **all eight exact file
ends**, leaving **zero map-tail bytes** for an extra objectives/economics/script block. This
negative is specifically about the known map format, not arbitrary data hidden in asset fields.

## 6. Scripts: present, global, and bounded

Established by `statistics.md` and rechecked using its existing decoder:

| FOLIO entry | GAZ offset | Bytes | Structure |
|---|---:|---:|---|
| 1 | **0x8000** | **1500** | 125 scheduling records, stride 12 |
| 2 | **0x8800** | **3604** | 1802 signed halfwords of program words |

Scheduler record = **s16 wordOffset at +0**, **u32 nextCheck at +2**, **u16 cooldown at +6**,
**u32 lastFailure at +8**. The unaligned dates are runtime state initialized from their
0xCCCCCCCC source placeholders. **0x8001675C..84** loads entry IDs 1 and 2 directly, without
world/park selection. **0x80016994** calls interpreter **0x80017024**; jump table
**0x800DBB18**, file **+0xCBB18**, covers opcodes 1..9, and zero terminates.

| Opcode | Operands (s16 words) | Operation |
|---:|---|---|
| 0 | none | end |
| 1/2/3/4 | slot, value | require == / != / < / > |
| 5 | slot, value | add to cached statistic, wrap s16; unused in shipped programs |
| 6 | slot, value | set statistic; event slots also set backing counter |
| 7 | message ID | post message |
| 8 | message ID | existing opaque action; retain `statistics.md` §0 disagreement |
| 9 | duration | require elapsed-condition timer > duration |

These are ordered predicates and immediate actions, not a general stack VM. All **125 records**
are followed; their programs and terminators cover **all 1802 words**, skipping **0**.
No program posts any of the nine weekly award IDs **AF,B0,B1,BC,B2,B3,B4,B5,B6**. Positive
control: **107 PostMessage instructions** are decoded, including known cleaner praise **0x7D**.
The opcode-5 absence is controlled by **50 opcode-6 Set instructions**. Existing opcode-8
semantics are not silently replaced. This bounds these common advisor programs; it does **not**
prove no other interpreter or ticket producer exists anywhere on the disc.

## 7. Decoder, tests, mutation audit

New production file: **`core/TPW.Data/PalParkDefinition.cs`**. It reads from the owner's extracted
PAL executable, exports each ordered list with a tested word-by-word encoder, preserves all
objective bytes, and writes the decoded regions into an independent executable template.
The tested template has those regions erased first: simply returning the template cannot pass.
Empty lists and type-8 handle words are preserved. Maps/scenery and +31 are exposed; the existing
objective constructor receives `CopyObjectiveRecord()` without changing `ParkObjectives.cs`.
`PalParkDefaults.Read` reads the initialization immediates and independent initial mode words.
It is initial state, not a replacement owner for live research/sandbox flags or card restoration.

This is intentionally a fixed-layout PAL decoder: addresses and counts describe this compiled
build. Reordering/changing entries or opaque bytes round-trips; resizing a compiler-owned list
requires another layout. Unsupported/truncated regions fail explicitly. No complete attraction
design records, executable, map payloads or expanded overlays are added to the repository.

Reproduce:

```
PYTHONDONTWRITEBYTECODE=1 python3 tools/audit_scenario.py
dotnet test tests/TPW.Sim.Tests/
PYTHONDONTWRITEBYTECODE=1 python3 tools/mutate_scenario.py
```

`scenario-audit.json` includes image fingerprints, ISO-directory/extraction comparisons,
all list source offsets/hashes, original-instruction controls, all-overlay call accounting,
map boundaries, and script counts. `scenario-mutations.json` records baseline/final suites,
each mutation, failing tests, frozen test-assembly hash and source restoration. Compile errors,
empty runs and skipped disc tests are invalid mutation runs, never kills. On machines without
the disc, the eight disc theories report explicit skips; set `TPW_DISC_DATA_ROOT` to its extracted
directory. No disc test silently returns success.

**Validation:** `dotnet test tests/TPW.Sim.Tests/` passes **1688/1688**, including **18 new tests**
(eight real-disc park cases), **zero skipped**. `dotnet test tests/TPW.Data.Tests/` passes
**295/295**, **zero skipped**. Mutation sweep: **117/117 killed, zero survivors, zero invalid**;
all source/test/audit hashes match the recorded inputs, and source restoration is confirmed.
The final full simulation suite passed after restoration. The sweep changes every one of the
64 list counts, selected source addresses, decoder/encoder rules, unknown-byte preservation,
selector boundaries and initial flag/money reads. No survivor was discarded or reclassified.

## 8. Not established

- Meanings or additional readers of the **30 remaining objective bytes**; the first two word-to-
  asset matches are not assigned behavior.
- A standalone scenario-file format, a per-park economics/research flag block, or a universal
  park VM. The proven producer is the distributed compiled-data/asset selection above.
- The semantics of world-record **+88..9F**, and the **+AC..AF** spacing gap.
- The retail UI reachability of overlay 2's sandbox-setting cases.
- Conditions/sources for every ticket implied by the advertised totals, or every possible
  script/indirect caller. The known global advisor programs and direct selector callers are bounded.
- A disc-resident prebuilt Practice Park save stream. This work does not repeat the earlier
  UNPAK signature sweep or turn its negative into a proof.
- Host/game wiring, live pad-sequence handling, or a new save codec. `game/`, `ParkAdvisor.cs`,
  `ParkMessages.cs`, and `ParkObjectives.cs` remain unchanged.

To settle the remaining producer questions: trace reads/watchpoints on the opaque objective
fields and the world-record pointer pairs during actual park entry/ticket events; drive the
overlay-2 command cases while recording mode setters; capture every resource request on the
Practice Park path and the buffer passed into a save/other interpreter. A new interpreted stream
needs a traced loader and interpreter before it gets a schema.
