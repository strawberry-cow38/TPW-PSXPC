# Theme Park World (PSX, PAL, SLES-026.88) — SAVING AND LOADING A PARK

READ = instructions/data at the quoted address. GUESS = interpretation, with confidence and a
settling observation. All executable addresses use TPW.BIN at **0x80010000**. Image SHA-256:
`0b708dd74796abfedad55e55fa007ace1b5697407d36e8f7823bb06f4fa53beb`.
Overlay addresses below are **overlay 2**, expanded from TPW.OVL, loaded at **0x80114158**.
Offsets and layout-table byte sizes are hexadecimal unless expressly called decimal; table order
numbers and prose counts are decimal. For example a `98`-byte table record is 152 bytes.
`A` is attraction outer+8; `S` is
staff outer+8; `V` is the visitor outer pointer. File, stream, record and object offsets are
DIFFERENT coordinate systems.

**Read first:** [disc-check.md](disc-check.md). The disabled Load Game row is a DISC check,
not a test for saved parks. Nothing here changes that finding. [parkopen.md §2.4](parkopen.md)
is the starting reader, not evidence that parks cannot save.

**The design:** a card file holds general state and up to three compressed park streams.
The park stream holds construction, money/calendar/history, staff and catalogue/research data,
but **guests are reconstructed from population ranges**, litter from two counts, queues from
saved route points, and moving objects from their definitions. It is not a frame snapshot.

## §0 SOURCE DISAGREEMENTS

Existing findings win in the port. Previously accepted corrections in a findings preamble remain
accepted; an old paragraph is not silently resurrected in their place.

| Source | Binary reading, with addresses | Retained code/treatment |
|---|---|---|
| needs.md/behaviour.md describe the stat setter as clamping to 0..100; the existing `Visitor` properties implement that for integers | `0x800924F0..0x80092520` first stores a byte, interprets it as SIGNED, then clamps. A supplied 128 becomes -128 and finally 0; the existing properties return 100. Entry at `0x800924F0`, sign test `0x80092500`, upper test `0x80092514`. | `VisitorSaveRanges.Restore` uses the EXISTING properties/Stat.Clamp. Explicit test: saved need-A minimum 127, range 0, high-biased load produces 128 and retains 100. No replacement signed-byte setter. This affects out-of-domain/corrupt range inputs; valid 0..100 population extrema cannot produce 128. |
| debug.md §4 names the card saver `0x8006C0E8` | Its prologue is indeed there, but calls at `0x80073524` and `0x800736B8` target **0x8006C0E4**, the preceding `lw v0,2140(gp)` that obtains GENDATA. Starting at the prologue loses that load. | Existing address remains in code documentation as the report's function label. This report supplies both entry and prologue; no behavioural alternative is introduced. |
| economy.md §4.7 calls a saved ride +0x88 word a dormant ticket field; already disputed in ride-panel.md's SOURCE DISAGREEMENTS | `0x8009CE30..38` copies **A+0xF4** into **save+0x88**, `0x8009CFC8..FD0` restores it. rides.md identifies A+0xF4 as placement day (`0x8009C3F4..400`, age reader `0x8009EDBC`). | Keep ride-panel.md's established no-ticket/no-ride-money rule and placement-day interpretation. `AttractionSave.PlacementDay` transports that word; it creates no ticket price or money flow. This is an inherited disagreement encountered at the new save boundary, not a newly substituted economy rule. |
| Guest initialization on load reaches the already documented needs.md §0 #5 disagreement with behaviour.md §2.12 / `Visitor.Spawn`'s money-first draw order | **Inherited, not re-derived:** constructor `0x8008C59C..0x8008C6E0` draws rubbish, nausea, need A, then money fourth. Loader `0x80091A3C` invokes initialization before its range draws. | Keep the existing `Visitor.Spawn` initialization behind `CreateVisitor`. This port does not replace its dice order while adding the subsequent save-range draws. |
| Fresh staff initialization reaches staff.md §0's disagreement with wages.md §3.2 / §4: morale 100, tiredness 0 | **Inherited, not re-derived:** `0x800941E0..0x80094224` draws morale `70+rand(30)`, then tiredness `rand(30)`. The 100/0 reset is training upward, `0x80095650..84`. | `RestoreStaff` must retain the existing `StaffHiring.InitialMorale/InitialTiredness` 100/0 policy. No new constructor dice or saved morale/tiredness fields are introduced. |

**Header clarification, not a disagreement:** decimal header byte **42** is file byte **0x22A**.
`0x8006C5C0..CC` passes **(byte42 == 0)** to `0x80059AA8`. The NONZERO **runtime flag** means
restricted mode; a ZERO **stored byte** produces it. This is exactly parkopen.md §2.4's explicit
expression. debug.md §4 additionally identifies it as Sandbox via the save titles. A normal
FullSim save writes byte 1 (`0x8006C1EC..200`).

**Refinement, not contradiction:** behaviour.md identifies `0x80091A3C` as the guest loader.
It is. parkopen.md's “per record” spawn description does not establish individual saved guest
records: `0x800724C8` passes the SAME range-record pointer for every guest. Research whole-percent
loss and the litter ordinary/vomit byte order agree with their existing findings.

**Independently re-read (tinyclaw, 2026-09-20), two of the disagreements above:**

- *The stat setter really is signed.* 0x800924F0 is `lbu v0,0(a1)` then `sb v0,0(a0)`, and the test
  that follows is `sll v0,v0,24` / `bgez` — a SIGN test on the stored byte — zeroing it when negative,
  after which `lb v0,0(a0)` reloads it SIGNED and `slti v0,v0,0x65` caps at 100. So 128 really does
  become -128 and then 0, where the port's integer properties return 100. Kept out of the code on
  purpose; it can only differ on out-of-domain input.
- *The ride's saved +0x88 really is the placement day.* 0x8009CE30 is `lhu v0,244(s0)` — 244 is 0xF4 —
  and 0x8009CE38 is `sh v0,136(s3)`, 136 being 0x88. A+0xF4 to save+0x88, halfword, exactly as
  reported, so economy.md's "dormant ticket" reading has nothing to do with this word.

## 1. Entry points and callers (READ)

| Work | Routine and callers |
|---|---|
| Capture the park on exit | teardown `0x80057EFC`, call at `0x80058048` → `0x8006B8E8(world,park)` → saved-level slot `0x8006BC00` → `0x8006C89C` |
| Construct an unpacked park buffer | `0x8006C89C` → **0x80070A48** → **0x80070948** twice: first a size pass (a0=0), then a writing pass (a0=1). Globals `0x801039B0` cursor, `B4` byte count, `B8` writing flag |
| Pack that buffer | `0x8006C89C` → **0x8006C994** → pack-size `0x80018EA8`, encoder `0x80018E54`; replace the slot's former packet. Slot freeing `0x8006C7FC` |
| Save new card file | overlay 2 **0x8011AF90** → `0x80073484` → **0x8006C0E4** (prologue C0E8) at `0x80073524`; then card write wrapper `0x8006FAE0` |
| Overwrite card file | overlay 2 **0x8011A7D0** → `0x80073678` → same builder at `0x800736B8`, then same card write wrapper |
| Load card file | overlay 2 handler **0x8011A1CC**, call **0x8011A228** → `0x80073378` → card read `0x8006FA3C` → **0x8006C524** at `0x80073414` |
| Load the selected saved park | game start `0x800588D0` → `0x8006B92C` → saved-slot check/get → `0x8006C850` → **0x8006CA44** (UNPAK through `0x80018EF0`) → **0x80071D0C** |
| Read/write one block | write **0x80070AF0**, read **0x80071E48**. Both optionally advance to four-byte alignment, helpers `0x80070B60` / `0x80071E98`. The stream is LITTLE-ENDIAN; there are no per-block tags or size words |

Overlay call sites were scanned in every expanded overlay: only overlay 2 directly calls the
three card dialog wrappers above. TPW.OVL on this ISO is directory entry `TPW.OVL;1`, extent
205715, 43967 bytes; use the ISO directory rather than assuming a raw-sector file. No runtime
button sequence or successful physical card write is claimed here.

The front-end save operation packages the saved-level slots. It does **not** call the live park
serializer directly: the teardown path captured the park first. This matters to a host: capture
current construction/state before presenting or writing a save, rather than saving stale slots.

## 2. Card container, only as it shapes the format (READ)

File size **0xA000 = five 0x2000-byte blocks**, allocated at `0x8006C33C..350`. Builder zeroes it,
then copies the 0x200-byte Sony header/title/icon template (`0x8006C398..3A4`). The buffer port
accepts those bytes from the host and performs **no card I/O**.

Application header starts at **file+0x200**, size **0x60**. Offsets in the first column are
RELATIVE TO THAT HEADER; the second column is relative to the file.

| Header offset / size | File offset | Field / evidence |
|---|---|---|
| +00 / 4 | 200 | additive checksum; duplicate at file 9FFC, writer C50C/C518, reader C55C..588 |
| +04 / 4 | 204 | absolute end of payload, ALSO checksum length, C1E4/C4D4 |
| +08 / 4 | 208 | magic **0x47415901**, C1D8..1E8 / C598..5A4 |
| +0C / 2 | 20C | version/format word **0x00AC**, C1E0..1F0 / C5A8..5B4; semantic name GUESS-medium |
| +0E / 2 | 20E | UNKNOWN, copied stack bytes |
| +10 / 4 | 210 | GENDATA packet file offset, normally **26A**, C128 |
| +14 / 2 | 214 | GENDATA packet length, zero means absent, C12C..13C / C76C |
| +16 / 2 | 216 | UNKNOWN |
| +18 / 0C | 218 | three u32 park-packet file offsets, in included-slot order, C180..18C / C64C |
| +24 / 6 | 224 | three u16 park-packet lengths, same order, C190..1A4 / C650 |
| +2A / 1 (decimal **42**) | 22A | inverse restricted/Sandbox flag, C1FC..200 / C5C0..CC |
| +2B / 8 | 22B | slot status bytes, world-major (four worlds × two parks). Low nibble 0/1 dispatches `0x8006BC68` / `0x8006BCE4`; **exact byte F0** loads a packet. Others do not. C5D0..680 |
| +33 / 1 | 233 | UNKNOWN semantic, global `0x8010398C`, C688..690 |
| +34 / 1 | 234 | UNKNOWN semantic, global `0x80103984`, C694..69C |
| +35 / 1 | 235 | UNKNOWN semantic, global `0x80103988`, C6A0..6A8 |
| +36 / 2 | 236 | UNKNOWN |
| +38 / 4 | 238 | UNKNOWN semantic, global `0x80103990`, C6AC..6B4 |
| +3C / 20 | 23C | four pairs of u32, copied to `0x80109B18`, C6BC..6F0; UNKNOWN field semantics, offsets +3C+8*w and +40+8*w, four bytes each |
| +5C / 1 | 25C | inverted first flag of `0x8006C0B8` / `0x8006C0D0`, C2E0..2F8 / C6F8..700; UNKNOWN semantic |
| +5D / 3 | 25D | UNKNOWN |

Then:

| File offset / size | Content |
|---|---|
| 000 / 200 | Sony header/title/icon bytes; transport metadata, not park simulation |
| 200 / 60 | application header above |
| 260 / 4 | sound-channel volume bytes for channels **0,1,3,4**, C300..330 / C714..740 |
| 264 / 1 | global `0x80102EB4` boolean, advisor/audio preference label UNKNOWN; C704..710 |
| 265 / 1 | zero written at C2FC; ignored padding |
| 266 / 2, 268 / 2 | signed screen-offset pair to `0x800BB38C`, C744..750 |
| **26A / general-length** | optional GENDATA packet |
| immediately afterwards | saved park packets, world-major, **no inter-packet alignment** |
| payload-end .. 9FFB | otherwise zero in a newly built file; reader does not interpret as blocks |
| **9FFC / 4** | checksum duplicate |

The header has only three offset/length entries, consistent with disc-check.md's at-most-three
open parks. Unused entries/padding are not initialized by all stack writers: they are **UNKNOWN**,
not extra slots. `SaveArchiveCodec` bounds-checks counts and slices. Those checks are port input
validation; the PSX code is not claimed to reject malformed data safely.

**⚠ DO NOT FIX — checksum length:** `0x8006C4CC` starts at file **204**, while C4D4 passes
**the absolute payload end**, not `(end - 204)`. Thus the checksum covers bytes BEYOND the payload.
`0x800708A4` zero-pads its last partial word, then sums LE u32s modulo 2^32; `0x800708EC` verifies
in the same way. It is neither CRC nor XOR. The port retains this and validates that the range
fits before the footer. A test flips a byte beyond the payload but inside coverage and rejects
it; its control changes card artwork before 200, which remains loadable.

Each packed packet (`0x8006C994`): **+0 u32 unpacked length; +4 u32 packed length INCLUDING
this eight-byte header; +8 headerless UNPAK data**. `0x8006CA44` allocates from +0 and decodes +8.
The core offers `ISaveCompression`; TPW.Data already has UNPAK decoding. **No new UNPAK encoder
is claimed.** The standalone park codec needs no compression service. Archive construction needs
valid packets supplied through that interface, or existing packets being retained.

GENDATA's unpacked stream is distinct: `0x80070A14` writes **25 bytes** via virtual manager slot 3
(`0x80013A9C`), aligns to 28 bytes, writes **one UNKNOWN byte** at +0x1C and aligns to **32 bytes**. Reader
`0x80071E04` → `0x80071F94` / `0x80071FC8`, virtual manager slot 2 through `0x80013A60`.
The advisor pointer is **0x8010265C**, manager at advisor+1C0. Allocation **0x80012F98..FA8**
calls constructor **0x800176A4**, installing vtable **0x800DBD28**. Its slot 3 is **0x800177AC**,
slot 2 **0x8001773C**: copy exactly manager+8..20 (25 bytes) to/from this record. These are
**advisor message-suppression flags**, not the statistics cache at advisor+1BC (statistics.md).
“Submitted” below means passed to `0x80014144`; it does not prove the player saw the queued text.

| GENDATA offset / size | Meaning / flag-check routine |
|---|---|
| 00 / 1 | per-park introductory message mask, bit `(world<<2)\|park`, posts DD..E4; 17890..17960 |
| 01 / 1 | message E6 submitted, 17980 |
| 02 / 1 | **UNKNOWN**, copied and zeroed by constructor but no consumer established |
| 03 / 1 | message ED submitted, 179E0 |
| 04 / 1 | message EE submitted, 17A40 |
| 05 / 1 | message EF submitted, 17AA0 |
| 06 / 1 | message F1 submitted, 17B00 and 18220 |
| 07 / 1 | message F4 submitted, 17B60 |
| 08 / 1 | message F7 submitted, 17BC0 |
| 09 / 1 | message FA submitted, 17C20 |
| 0A / 1 | message FD submitted, 17C80 |
| 0B / 1 | message 101 submitted, 17CE0 |
| 0C / 1 | message 105 submitted, 17D40 |
| 0D / 1 | message 10B submitted, 17DA0 |
| 0E / 1 | message 10D submitted, 181C0 |
| 0F / 1 | message 10E submitted, 17E00 |
| 10 / 1 | message 112 submitted, 17E60 |
| 11 / 1 | message 113 submitted, 17EC0 |
| 12 / 1 | **shared** submitted flag for messages 114 (17F20) AND 118 (18040) |
| 13 / 1 | message 116 submitted, 17F80 |
| 14 / 1 | message 117 submitted, 17FE0 |
| 15 / 1 | message 119 submitted, 180A0 |
| 16 / 1 | message 11B submitted, 18100 |
| 17 / 1 | message 11C submitted, 18160 |
| 18 / 1 | restricted/Sandbox intro message 120 submitted, 17840..17880 |
| 19 / 3 | UNKNOWN alignment padding |
| 1C / 1 | UNKNOWN unwritten stack byte from 70CDC; 71FC8 consumes it without using it |
| 1D / 3 | UNKNOWN alignment padding |

Routine suffixes above have prefix **0x800**. **⚠ DO NOT FIX:** the mask at +00 remains one byte
despite shifts 8/9/12/13 for worlds 2/3 (`0x80017950..60`). The two messages at +12 share one
flag; do not split it into two fields. Flags are copied raw, not normalized. The packet is
retained whole by `SaveArchive`; advisor behaviour remains a host concern. To settle UNKNOWN +02,
trace accesses to manager+0A through its virtual callers or compare saves around advisor events.

## 3. Unpacked park layout (READ)

All ordinary blocks end with **four-byte alignment**, even when the payload is only two bytes.
Header **18 bytes → 20**: writer `0x80070BA8`, reader `0x80071ECC`. Reader checks world/park,
**NOT** marker 3039. Dimensions and catalogue counts come from the original map/assets, not
from saved data. `ParkSaveLayout` therefore requires them from the host.

| Stream header offset / size | Field |
|---|---|
| 00 / 2 | marker **3039** (12345 decimal), writer 70BAC; not checked on load |
| 02 / 1, 03 / 1 | world, park, getters `0x80054000` / `0x80053FF4` |
| 04..0A / seven bytes | counts: **type 1 flat, 6 track, 3 tour, 7 coaster, 2 feature, 4 shop, 5 sideshow** |
| 0B..0F / five bytes | counts: **guard, researcher, mechanic, handyman, entertainer** |
| 10 / 1 | guest count (decimal offset 16) |
| 11 / 1 | UNKNOWN, unwritten stack byte |
| 12 / 2 | UNKNOWN alignment bytes |

`align4(x)=(x+3)&~3`. After the fixed first two blocks, offsets are a running cursor: consume the
listed length then align4, unless expressly indicated. **No count is inferred from buffer length.**

| Order | Start / payload size | Writer | Reader | Content |
|---|---|---|---|---|
| 0 | 0 / 12 hex (=18 decimal) | 70BA8 | 71ECC | header above, end cursor 14 hex (=20 decimal) |
| 1 | **14 / 4** | 70C50 | 71F2C | +0 open byte; +1 UNKNOWN byte; +2 u16 entry fee **whole pounds**, restore ×10 |
| 2 | **18 / W*H bytes** | 71430 | 72820 | path bitmap, only ceil(W*H/8) meaningful bytes; excess UNKNOWN |
| 3 | aligned cursor / count[04] × **98** | 70D04 → A00BC | 71FEC → A0120 | flat ride records |
| 4 | cursor / count[05] × **F0** | 70D5C → A6D5C | 720A4 → A6E6C | track ride records |
| 5 | cursor / count[06] × **98** | 70DB4 → A1020 | 72144 → A1090 | tour ride records |
| 6 | cursor / count[07] × **418** | 70E0C → ADB20 | 721E4 → ADCD8 | coaster records |
| 7 | cursor / count[08] × **10** | 70E64 → 23D28 | 72284 → 23DB8 | features |
| 8 | cursor / count[09] × **1C** | 70EBC → B6AE8 | 72324 → B6BB8 | shops |
| 9 | cursor / count[0A] × **1C** | 70F14 → B7330 | 723C4 → B7410 | sideshows |
| 10 | cursor / **1A** (always, even no guests) | 70FA4 | 72464 → 91A3C | one shared guest range record, pad 2 |
| 11 | cursor / count[0B] × **10** | 71278 → 983E4 | 72500 | guards |
| 12 | cursor / count[0C] × **10** | 712D0 → 99C04 | 725A0 | researchers |
| 13 | cursor / count[0D] × **10** | 71328 → 97264 | 72640 | mechanics |
| 14 | cursor / count[0E] × **10** | 71380 → 99538 | 726E0 | handymen |
| 15 | cursor / count[0F] × **10** | 713D8 → 95D74 | 72780 | entertainers |
| 16 | cursor / **2**, pad 2 | 71584 → 53A10 | 72938 → 53A98 | ordinary litter count, vomit count |
| 17 | cursor / **328** | 715B4 → 87DEC | 72964 → 88A0C | bank, loans, money histories |
| 18 | cursor / **DC** | 71990 → 6892C | 72E54 → 69034 | calendar, class strike state, visitor histories |
| 19 | cursor / **2*Σ definitions** | 715F0 | 72B54 | percent/count pairs, type order **3,7,6,1,2,4,5,8**, index ascending. NO alignment between pairs |
| 20 | immediately after pairs / **10** | 71954..71978 → 9B28C | 72E1C..E38 → 9B2EC | research topics, THEN align4 |
| 21 | cursor / variable, no final alignment | 719CC | 729A0 | messages, below |

All routine suffixes in this table have prefix **0x800**. Orders **19 AND 20 are wholly absent
in restricted mode** (715F0 branch at 7160C; 72B54 branch at 72B74). Nothing else is skipped by
that branch. A host must not guess the mode from a short buffer.

### 3.1 Paths

`0x800714B8..7150C`: row-major y then x, bit `(y*W+x)&7`, LSB first. Set for tile types **2 or
13** (`0x8004D4EC` / `0x8004D558`). Queue/entrance types **4/7** are not standalone path bits.
On load each set bit causes **TWO calls** to `0x8001BD30(x,y,0,0)` after selecting path kind 2
(`0x800728AC..D8`). Attraction restoration later rebuilds its queue/footprint.

**⚠ DO NOT FIX:** reservation and copy length are **W*H**, not `(W*H+7)/8` (71530..544 / 72854..870).
The writer allocates W*H+1 and initializes only the bytes it packs, including the next byte after
a complete group of eight. Remaining bytes are UNKNOWN uninitialized allocation contents. The
port captures meaningful bits into a zeroed reservation (declared storage policy), and preserves
all supplied excess bytes on read/write. It never interprets that tail as paths.

### 3.2 Attractions

All seven classes begin with this **8-byte** base, `0x80062A60` / `0x80062AE8`:

| Offset / size | Field |
|---|---|
| 00 / 4 | **guests served all time**, A+14; save 62ACC..AD8, rides.md §5 |
| 04 / 1 | attraction type, virtual slot 16 |
| 05 / 1, 06 / 1 | whole-tile x/y, low bytes of slot-10 position |
| 07 / 1 | orientation byte, A+6C, `0x8006329C` |

Queued-ride base (**00..93**, `0x8009CDD8` / `0x8009CEFC`), for 1/3/6/7:

| Offset / size | Field |
|---|---|
| 08 / 80 | 32 reserved queue/path points, each s16 x then s16 y. Only count at 91 meaningful; remaining `08+4*q .. 87` UNKNOWN. Writer obtains u8 coordinates and stores them as halfwords (9CEAC..CC); reader takes signed halfwords (9D044..4C) |
| 88 / 2 | placement day, A+F4; inherited source disagreement §0 |
| 8A / 2 | speed slider, u16 |
| 8C / 1 | capacity, u8 |
| 8D / 1 | duration, u8 |
| 8E / 1 | remaining lifetime, narrowed from virtual slot 84 / A+68 (rides.md §5), restored by slot 83 |
| 8F / 1 | reliability `A+B4 >> 12`, restored `byte << 12` |
| 90 / 1 | status |
| 91 / 1 | saved queue-path point count |
| 92 / 1 | upgrade level, A+F6 |
| 93 / 1 | UNKNOWN |

After raw placement/status/settings, restore clears rider count A+F0, empties/rebuilds queue
geometry, then maps saved statuses **4,5,6 → 4** (`0x8009D0A0..D8`). It clears effects/lists at
9CF20..2C. **⚠ DO NOT FIX:** reliability loses fractional precision, settings are zero-extended
without widget clamping, and motion/riders are not resumed. `RidePanel.SaveSliders/RestoreSliders`
remain the established slider implementation; `AttractionSave.Sliders` supplies their wire fields.

Class tails (offsets relative to the whole record):

| Class | Tail |
|---|---|
| flat 1 (98 bytes) | 94 / 1 definition index; **95 / 3 UNKNOWN**. A00FC..108 |
| tour 3 (98 bytes) | 94 / 1 definition; 95 / 1 boolean from outer+101 (route state semantic UNKNOWN); **96 / 2 UNKNOWN**. A1060..1080 |
| track 6 (F0 bytes) | 94 / 1 definition; 95 / 1 route-point count n; 96 / 50 reserved bytes = twenty `(s16,s16)` points, unused `96+4*n .. E5` UNKNOWN; E6 / 6 reserved bytes = three two-byte records copied from outer+1CF2, count at EC, field semantics UNKNOWN; EC / 1 second count; **ED / 3 UNKNOWN**. A6DA8..E50 |
| coaster 7 (418 bytes) | 94 / 1 definition; 95 bits 0..5 piece count, bit 6 boolean from outer+104 (GUESS-high route-complete flag; trace its writers to settle), bit 7 cleared; 96 / 380 reserved bytes = sixty-four 0E-byte piece slots (reservation, NOT an asserted live capacity); only count slots populated; **416 / 2 UNKNOWN**. ADB84..ADCA4 |

Each coaster piece at `0x96+0x0E*i`: **+0 / 2 position X, +2 / 2 UNKNOWN copied position padding,
+4 / 2 position Y**, then **+6 / 2** getter B671C, **+8 / 2** getter B3400, **+A / 2** getter B67AC,
**+C / 1** getter B6744, **+D / 1** getter B6730. These final five field semantics are **UNKNOWN**;
the source getter and width are established. Their containing piece geometry is rebuilt by the
class loader, with the final coaster pass at **0x800505A8 → 0x800AF614**. Do not label these as
vehicle positions or train motion without following those getters/setters.

Other records, after common +0..7:

| Class | Remaining fields, all offsets/sizes explicit |
|---|---|
| feature 2 (10) | +8 / 4 **last-refill day stamp**, outer+78; +C / 1 definition; +D / 1 status; +E / 1 signed cleanliness/capacity byte; **+F / 1 UNKNOWN**. 23D28 / 23DB8; shop-stock.md §4. Restore capacity through its established clamping setter, not as a new stock field |
| shop 4 (1C) | +8 / 4 **visit count**, outer+84; +C / 2 sale price; +E / 2 takings (low halfword of outer+8C); +10 / 2 profit (low halfword of outer+90); +12 / 2 build day; +14 / 1 definition; +15 / 1 status; +16 / 1 second slider; +17 / 1 mean satisfaction (B6F18); +18 / 1 quality slider; **+19 / 3 UNKNOWN**. B6AE8 / B6BB8, shop-stock.md §3 |
| sideshow 5 (1C) | +8 / 4 visit count, outer+7C; +C / 2 price (outer+84); +E / 2 outer+78 UNKNOWN semantic; +10 / 2 outer+88, +12 / 2 outer+8C, +14 / 2 outer+90, +16 / 2 outer+94: UNKNOWN counter semantics, low halfwords; +18 / 1 outer+86 setting (low byte); +19 / 1 definition; +1A / 1 mean satisfaction (B78B8); +1B / 1 status. B7330 / B7410. No unassigned bytes in this record |

“UNKNOWN” is deliberate even when a neighbouring report has a plausible name for a similar
class. The byte codec retains those fields; the host must map them from the cited class, not
from a same-offset field in another class.

### 3.3 Guests — one 26-byte record, not N records

Writer **0x80070FA4..71274** scans the entire visitor list, accumulating twelve min/max pairs.
Record contains **minimum, range=max-min**, narrowed on store. Offsets below are record offsets.

| Offset / pair size | Field / getter | Load |
|---|---|---|
| 00 / 2 | rubbish V+58, 92630 | mean of two draws |
| 02 / 2 | happiness V+59, 92610 | mean of two draws |
| 04 / 2 | nausea V+5A, 925F0 | **IGNORED**, constructor survives |
| 06 / 2 | need A V+5B, 925D0 | high-biased |
| 08 / 2 | boredom V+5C, 925B0 | **IGNORED**, constructor survives |
| 0A / 2 | ride desire / toilet need V+5D, 92590 | high-biased |
| 0C / 2 | need B V+5E, 92570 | high-biased |
| 0E / 2 | tiredness V+5F, 92550 | high-biased |
| 10 / **4** | raw money V+48, **two u16s**; getter 9252C sign-narrows the source to s16 | mean of two draws, **raw tenths**, no £ conversion |
| 14 / 2 | current speed V+60, virtual slot 44 = 92694 | mean of two draws, stored byte, signed speed |
| 16 / 2 | normal speed V+62, 92688 | mean of two draws |
| 18 / 2 | UNKNOWN V+63, 9267C | mean of two draws |
| 1A / 2 | UNKNOWN alignment bytes outside the record | skipped |

**⚠ DO NOT FIX:** minima for the first ten fields start at **9999**, maxima at 0; for the last
two fields BOTH start at **0** (70FBC..71014). Therefore normal-speed minimum remains zero even
when everyone walks at 19+, and a zero-population record has byte pairs `(15,241)` in the first
eight fields and speed, and money `(9999,55537)`. It is not an all-zero record.

Loader **0x80072464** reads this one block then loops **header[16]** times, allocating through
**0x80051480**, invoking **0x80091A3C** each time with the SAME pointer. It initializes the visitor
again through slot 2, positions via **0x800599A4(1)** (jittered exit-side entrance point), sets
**state 45**, restores only the ten listed fields, rolls a new **rand(8)** type, and writes
animation/facing bits from **0x00680000** (animation 13, facing 0).

Let `b = storedRange + 1`, consume `a=rand(b)`, then `c=rand(b)`:

- ordinary: `minimum + (a+c)/2`, integer truncation, **0x8009197C**;
- high-biased: `minimum + b - a*c/b`, **0x800919C4**.

**⚠ DO NOT FIX:** high-biased can exceed stored maximum by one; range zero still adds one.
Load draw order is rubbish, happiness, need A, ride desire, need B, tiredness, money, current
speed, normal speed, UNKNOWN63, then appearance. Nausea and boredom consume NO range dice.
Existing constructor RNG order is retained through the host's existing `Visitor.Spawn`; its
already documented ordering disagreement is not silently replaced by this save port.

Targets, ride history, balloons/props, waiting/ride state, navigation, stack, identity/appearance,
and visit-age timers are not individual records here. A round trip of the **save image** is exact;
a round trip of individual live guests intentionally is not. Correlations between guest fields
are lost, not just precision. “Guests are serialized” is true; “each guest is serialized” is not.

### 3.4 Staff and litter

Every staff class forwards to **0x80094BBC / 0x80094C64**, same **16-byte** record:

| Offset / size | Field |
|---|---|
| 0 / 2, 2 / 2 | signed 8.8 x/y, Person 93854 / 9386C |
| 4 / 4 | S+34 hired day, staff.md §1.2 (942B0..C8); exact word copied 94C4C / 94D34 |
| 8 / 1 | recruit variant S+3D through virtual getter 53 / setter 52 = 95728 / 95734. Used as allocator argument at 72544..48, before class initialization |
| 9 / 1 | low three **skill** bits, getter 956F0, S+3C & 7 (wages.md); bit 7 = state was 15 (strike); other bits UNKNOWN |
| A / 2 | UNKNOWN, unwritten stack bytes |
| C / 1, D / 1, E / 1, F / 1 | patrol rectangle left, top, right, bottom |

All five Person vtables (E43A4/E4A68/E41D4/E4750/E4C74) have these same slot 52/53 targets.
The variant byte and skill bits are different fields; skill is not an appearance selector.
Restore initializes the selected recruit/class, restores position and fields, and enters strike 15 if bit 7 is
set. Normal work/path/job state, morale, tiredness and target/claim pointers are **not saved**.
The host must use class initialization, not leave an old employee's claim alive. Staff active
list order is serialized; allocation can rebuild list linkage rather than preserve pointers.

Litter is exactly [litter.md](litter.md)'s **ordinary count, vomit count**, two bytes.
`0x80053A98`: allocate FIRST, then repeatedly draw x in [0,W), y in [0,H) until tile type **2,4,7**;
centre at `(tile<<8)+80`, scatter using the existing `Litter.Scatter`, mark vomit in the second
loop. Ordinary sprite selection is consumed even for vomit. **⚠ DO NOT FIX:** no retry limit,
no null-allocation recovery, no saved claims/positions. `LitterSave.Restore` ports that loop
against a fresh pool and `ILitterSaveWorld`; the host supplies the restored map and shared RNG.

### 3.5 Bank, calendar and histories

**Bank: 0x328 (808) bytes**, writer 87DEC, reader 88A0C. Offsets within this block:

| Offset / size | Field |
|---|---|
| 000 / 280 | eight 50-byte history records: balance, income, entrance, shop, sideshow, spending, wages, park value. Source ring bases BANK+BC,2FC,53C,77C,9BC,BFC,E3C,107C (economy.md §1.2) |
| 280 / 70 | four **1C-byte loan records**, exact fields below |
| 2F0 / 4 | current balance **RAW tenths**, 87E4C / 88A44 |
| 2F4 / 4 | money-history month index, BANK+12BC |
| 2F8 / 4, 2FC / 4 | last-year income, last-year spend, **whole pounds** |
| 300 / 4, 304 / 4 | this-year income, this-year spend, whole pounds |
| 308 / 4, 30C / 4, 310 / 4 | all-time sideshow takings, entry takings, shop profit, whole pounds |
| 314 / 4, 318 / 4, 31C / 4 | all-time wages, spend, income, whole pounds |
| 320 / 4, 324 / 4 | yearly park-value and balance snapshots, whole pounds |

Money conversion is `0x80088E18` signed truncation /10 and `0x80088E50` multiplication ×10;
raw balance/recent history words use `0x80088E44` / `0x80088E3C`. **⚠ DO NOT FIX:** tenths in
totals and older history are lost, current balance is not. BANK+12C0 current loan repayments,
BANK+8 spending permission and the entry fee are not extra words in this block (fee has §3 order 1).

Loan record **0x80086470 / 0x800864C4**, all offsets relative to one 0x1C-byte slot;
names from economy.md §4.5:
+0 / 4 principal pounds (loan+4); +4 / 4 term in months (loan+8); +8 / 4 months left;
+C / 4 remaining pounds; +10 / 4 monthly payment pounds; +14 / 4 total repayable (loan+24);
+18 / 1 available flag (nonzero normalized on load); **+19 / 3 UNKNOWN padding**. Keep existing
LoanBook's repaid-but-still-taken behaviour; saving is not a loan-slot release.

Each money history, **0x800879DC** (pack) / **0x80087F90** (restore):
**+0 / 4 older minimum pounds; +4 / 4 older maximum pounds; +8 / 30 twelve recent raw Money
words; +38 / 6 six normalized two-month averages; +3E / 8 eight normalized six-month averages;
+46 / 9 nine normalized eight-month averages; +4F / 1 UNKNOWN padding**. Normalization uses
255 and `(max-min)`, with zero range replaced by 1 at 87B28..30. Recent samples start at
`(monthIndex+287-i)%144`. The groups cover 12 + 12 + 48 + 72 = **144 months**.
Restore interpolates grouped samples into ring entries; it does not resurrect individual older
months. The precise signed/unsigned interpolation arithmetic at **87F90..88A08 is NOT PORTED**;
the byte image retains all 80 bytes per history and `RestoreBank` delegates expansion to the
host. This is an explicit implementation boundary, not a claimed exact-history snapshot.

**Calendar/McAi: 0xDC (220) bytes**, 6892C / 69034:

| Offset / size | Field |
|---|---|
| 00 / 14 | five u32 class words from McAi+300+8*i, UNKNOWN precise deadline semantics |
| 14 / 4 | total days McAi+10 |
| 18 / 4 | total admissions, McAi+1C; state 37 increments it, getter 69308 feeds the "People Visited" display (statistics.md §6) |
| 1C / 2, 1E / 2 | year, total months (narrowed halfwords) |
| 20 / 1 | rating at new year, McAi+2F8 (happiness.md) |
| 21 / 5 | five class bytes from McAi+304+8*i, UNKNOWN semantics |
| 26 / 1 | five strike bits from McAi+305+8*i |
| 27 / 1 | months in debt, McAi+14 |
| 28 / 1, 29 / 1 | calendar month, day |
| 2A / 1 | UNKNOWN |
| 2B / AF | five **23-byte** (=35 decimal) packed byte histories: people, arrival delta, happiness, time in park, overall rating; source rings McAi+28,B8,148,1D8,268 |
| DA / 2 | UNKNOWN padding |

Each byte history **68740 / 68A54**: +0 / C twelve recent bytes; +C / 6 six two-month means;
+12 / 8 eight six-month means; +1A / 9 nine eight-month means. Old samples are means, not retained
individual months. The buffer layer retains the 35-byte representation. Runtime interpolation
is a `RestoreCalendar` host responsibility. The fractional calendar accumulator McAi+0 is not
in this record. **⚠ DO NOT FIX:** strike-bit restoration stores the bit mask itself (1/2/4/8/16),
not boolean 1, into each class byte (`0x800690D0..DC`).

### 3.6 Catalogue, research, messages

**Catalogue:** percent first, completed-count second. This is established by writer **71634..664**
(count saved on stack+21, percent stack+20, stack+20 written first), and loader **72B84..BB8**
(read first byte as percent, second as completed count into `0x8006ACDC`). Definition counts are
from getters 6A214,244,274,2D4,304,334,364,2A4 in that order. There are no embedded catalogue
counts. `ResearchSave` adapts the EXISTING `ResearchSystem`; it does not create another research
implementation. Saving calls level/percent getters, so their existing free-unlock side effects
remain possible. Count is captured before querying the next-level percent.

**Research BANK:** **16 bytes**, funding byte followed by five `(active u8,type u8,index s8)`
triples, 9B28C / 9B2EC. Restore catalogue first, then call `ResearchSystem.RestoreTopics`.
[research.md §4](research.md) stands: raw work, finished flags and attention are absent;
whole-percent catalogue progress reconstructs work. A test carries non-default progress in
24 definitions and an active topic through the park codec, and demonstrates that reversing the
restore order actually changes reconstructed progress (the control).

**Messages**, 719CC / 729A0: one u8 count; align to TWO before each record; then s16 string ID.
If ID=-1: u8 byte length then exactly that many text bytes, no saved terminator. Next u8 message
type, then s8 target type; if target type!=-1, one u8 target-list index. No final alignment.
Writer excludes **type 4**, and only **type 2** with a target writes a target index. Target lookup
on load is **0x8005BEC0(type,index)**, so object lists must already exist. Text remains bytes in
the original encoding. UNKNOWN two-byte-boundary padding is retained; no arbitrary text charset,
UTF-8 conversion, or pointer serialization is invented.

## 4. Saved versus recomputed (READ, bounded to this stream)

| Saved representation | Recomputed or deliberately discarded on load |
|---|---|
| world/park identity, path bits, attraction class/definition/position/orientation, queue/track points | base terrain/build lists from assets, runtime geometry, drawable lists, pools and pointer relationships |
| users, settings, placement days, remaining life, coarse reliability, status, upgrade levels; shop prices/counters | active rider lists, queue guests, vehicle motion, transient effects; ride statuses 4..6 normalized to 4 |
| visitor count and shared ranges | individual visitors, appearance, positions, state45, targets/history and correlations; nausea/boredom remain freshly initialized |
| staff position/patrol/recruit variant/skill/strike/hire day | job/state stack (except striking), morale/tiredness, targets and claims |
| two litter counts | every position, sprite and claim; no saved age |
| fee, bank balance/totals/loans, compressed histories | old individual history samples, some monetary fractions, runtime bookkeeping |
| calendar whole fields/strike data, packed visitor histories | fractional day accumulator and old individual monthly samples |
| whole catalogue progress and active topics/funding | fractional work, finished and attention flags |
| messages and target list indices | target pointers and presentation objects |

OpenPark is called from the gate record **BEFORE** the saved calendar is restored (`71D40` vs
`71DC8`), and stamps the then-current calendar through the existing OpenPark routine. The original
month-opened global **0x80102D24 is not a park-stream field**. Do not invent a persisted opening
date and move the call after calendar load. Similarly, no per-guest identity or park-stream RNG
seed has been found; the established GENDATA fields are advisor flags, not an RNG snapshot.

## 5. Port contract, tests and remaining boundaries

- `ParkSaveCodec.Write/Read`: complete unpacked stream over byte buffers; `CapturePaths` preserves
  the unusual reservation and packing. Structural parsing finishes before host mutation in `ParkSaving.Load`;
  definition validity and runtime object construction still belong to the host.
- `SaveArchiveCodec`: byte-only container, exact header/mode, packet offsets, checksum/footer;
  `SavePacket` supplies the eight-byte wrapper and `ISaveCompression` host boundary.
- `SaveRecords`: named fields plus exact retained byte records for every attraction/staff/bank/
  calendar block. Unknown bytes are not thrown away to make a tidier object model.
- `VisitorSaveRanges`, `LitterSave`, `ResearchSave`: concrete lossy capture/reconstruction rules,
  using existing sim classes and documented source precedence.
- `IParkSaveHost`: fresh map/catalogue/assets, object construction and geometry, encoded history
  capture/expansion, initial RNG/guest spawn/entrance position, messages, and final coaster pass.
  The tests supply this interface and assert each restore callback in order with distinctive data.

No Godot reference, filesystem I/O or `game/` edit. This establishes a usable **core save boundary**;
it does not claim that the untouched game host now has a Save menu or implements these callbacks.
Bank/history capture here is the **encoded record** contract; a host starting only with live
144-entry histories still needs the cited compression/interpolation adapters. UNPAK encoding is
also host-supplied for PSX container export. Neither gap is disguised as an empty/default record.

**NOT ESTABLISHED:** actual emulator-produced populated save as an interoperability oracle;
byte-for-byte identity with PSX compression choices; GENDATA byte +02; UNKNOWN header,
coaster-piece and sideshow fields named above; precise history interpolation port;
all reset side effects inside every class's geometry initializer. No claim of frame-exact or
physical-card compatibility without those checks. To settle: capture a populated PSX card file
with a no-change control, decompress each packet, compare boundaries and known values, then
step the remaining class/history loaders and GENDATA byte +02's consumers.

**GUESS summary:** header AC's semantic version label (medium), coaster route flag (high).
Their offsets/widths are READ. Resolve by tracing their getter/
setter consumers or changing only that field in paired console saves. No new numerical gameplay
constant depends on any of these labels.

Reproduce static reads with `python3 findings/fable-scripts/fn.py ADDR` and `ann.py START END` for
leafs; note the leading load at **6C0E4**, which the prologue heuristic excludes. A full-function
print may include adjacent leafs: quoted instruction boundaries, not the heuristic heading, are
the evidence. Overlay call sites can be reproduced with TPW.OVL's existing `ovl_lz.lz` decoder
and a word-aligned `jal` scan for 73378/73484/73678.

Mutation runner: `python3 tools/mutate_save.py`, audit `findings/save-mutations.json`. It compiles
tests against baseline production, then rebuilds only production for each mutant. A compile
failure is INVALID, never a kill. SHA-256 checks, durable backups/journal, SIGINT/SIGTERM/SIGHUP
restoration, `--recover` and `--resume` follow the house research runner. No parallel builds.
The tests state what they REJECT. Golden fixtures use literal format sizes/order independently
of the production tables; round trips carry all fields, not just counts and cash.
The synthetic park image has **nine attractions across all seven classes, seven staff across all
five classes, three guests, seven ordinary litter plus three vomit, fourteen catalogue definitions,
four messages, and patterned bank/calendar/UNKNOWN storage**. Every exposed field and every record
byte is compared. A separate independently assembled image exercises nonzero padding. Live
`Visitor`, `LitterPool` and `ResearchSystem` fixtures test reconstruction, including a control that
restores research in the wrong order and obtains a different result. These are buffer/simulation
tests, not a claim that the synthetic image was played on a console.

**Validation:** `dotnet test tests/TPW.Sim.Tests/` passes **1,501 tests** (1,451 before this work,
**50 added save/load cases**). The mutation sweep catches **152/152**, with **0 survivors and
0 invalid builds**; baseline and final full-suite controls both pass. All six production source
SHA-256 hashes match after restoration. The audit includes each exact edit and rejecting test names.

One development candidate, changing the ride-state lower bound from `>=4` to `>=5`, survived
because it is **equivalent**: status 4 still returns 4. It was replaced with `>=6`, which wrongly
preserves status 5 and is rejected for all four ride classes. The original passing candidate is
retained in `development_controls`, not counted as a kill. An intentional **SIGTERM** after 99
completed candidates verified byte-identical restoration of all six files. The final full sweep
was run afresh after correcting draft field labels against staff.md/statistics.md.

The final `--resume` refreshes baseline/full-suite controls and test hashes after removing two
trailing spaces only; no assertion or production source changed. Earlier test hashes and totals
remain in `previous_runs`. The 152 successful mutation results are retained.
