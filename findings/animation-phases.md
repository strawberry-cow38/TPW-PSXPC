# Animation descriptors and ride lengths — SLES-026.88

2026-09-20. **READ** = instructions at the quoted TPW.BIN address, or bytes at an
explicit FOLIO.GAZ offset. **READ-derived** = arithmetic on those observations,
with assumptions stated. **GUESS** = interpretation, confidence stated. No live
ride timing was captured for this report.

**The lengths are on the disc.** The 88-byte object is exactly folio.md §3's
runtime mesh descriptor. Its length halfword at +0x38 is copied from **mesh+0x00**,
not mesh+0x38. Crazy Ape has length **41**, The Dizzy Tree **239**, Zero G **801**.
The complete [table](ride-phase-lengths.md) covers 59 flat rides and the model
lengths of another 24 rides. Only flat rides use the phase-counted unload rule.

## 1. The two tables, and the pointer that was misread

**READ**, with `H = A+0x18`, `R = [H]`, `B = locked archive entry`:

1. `0x800311F4..218` sets `[H+8] = R+0x18` through `0x8003140C`.
   Consequently `[[H+8]+4]` is **R+0x1C's descriptor-allocation handle**.
   H+8 is a pointer to resource members, not a second independently loaded model.
2. `0x8003084C` locks `[R+0x18]`. At `0x80030868` it copies the returned **v0 = B**
   into a1. It stores B at R+0x0C and computes
   `R+0x10 = B + 0x20 + 4*u32(B+0x1C)` in **a1**. **v0 is never changed**.
   Its return is B, not the computed directory pointer.
3. Thus `lw 4(v0)` at `0x800300DC` reads **B+4 = number of mesh sub-entries**.
   It does not read the first directory pair's second word (an unpacked-size flag).
4. `0x80030A24..A4C` allocates `88*u32(B+4)` bytes. The loop at
   `0x80030A5C..A9C` loads/decompresses each mesh, calls `0x8002C5CC(D,M)`,
   evaluates frame zero, and advances D by **0x58**. This proves the identity
   with folio.md's descriptor without relying on coincident field offsets.
5. The **logical phase map**, at B+0x20 with count at B+0x1C, is separate.
   `0x80030364` takes a logical phase argument; it is not a getter of a handle's
   current phase. It returns the signed map word, or -1 for an out-of-range index
   (unsigned bound check at `0x80030394`). `0x800658D8` supplies `lh(A+0x64)`.
6. `0x80065DC0..DC8` changes a negative mapped index to zero. `0x800300E4`
   takes the resulting **sub-entry index modulo B+4**; `0x8003010C..124` indexes
   the locked descriptor allocation at `88*index`. `0x800314D4` reads its +0x38.

All **59 flat-ride containers**, READ from the archive, have one sub-entry and
the four-slot map `[-1,0,-1,-1]`. Running selects logical phase **1**
(`0x8009C708 → 0x800656C4`), hence descriptor **0**, repeatedly. The completed
cycle counter does **not** advance through the four logical map slots.

## 2. Every byte of the 88-byte descriptor

`D` is the runtime descriptor, `M` its raw/decompressed 0x48-byte-header mesh,
`W` the locked **ARS** working allocation. Offsets below are u32 byte offsets,
not absolute pointers, except the handle at +0x54. Counts used in the layout are
u32 fields of M. `align4` rounds upwards. The builder is `0x8002C5CC`.

| D offset | Storage | Meaning / source | Evidence and status |
|---|---|---|---|
| +0x00 | u32 | Main vertex array offset from M, `0x48+8*M[0x24]` | **READ** 0x8002C804..818, 0x8002CB54..B64 |
| +0x04 | u32 | Extra vertex array offset from M, 0x48 | **READ** 0x8002C800, 0x8002CB50..B5C |
| +0x08 | u32 | Vertex-colour array offset from M; after `8*M[8]` main vertex bytes | **READ** 0x8002C810..828, 0x8002CB40..B4C |
| +0x0C | u32 | Bone records offset from M, after face blocks, aligned to 4 | **READ** 0x8002C8EC..900, 0x8002CB30..B3C; each bone consumes 40 bytes, 0x8002C908..914 |
| +0x10 | u32 | First 12-byte-record array offset from M, after bones; count M+0x20 | **READ** 0x8002C904..920, 0x8002CB20..B2C; animator reads it at 0x8002CC4C..C60. **GUESS-medium:** binding/deformation records |
| +0x14 | u32 | Second 12-byte-record array offset from M; count M+0x0C | **READ** 0x8002C924..93C, 0x8002CB14..B28; **GUESS-medium:** vertex-binding records; exact record semantics not established here |
| +0x18 | u32 | Offset from M immediately after all animation tracks, start of trailing list | **READ** 0x8002C954, 0x8002CA44, 0x8002CB24; list length copied to D+0x3A |
| +0x1C | u32 | Mutable vertex array offset from W, after block-offset table and bone matrices | **READ** 0x8002C7AC..7D0, 0x8002CA5C..A98, 0x8002CAF0/B00 |
| +0x20 | 4 bytes | **NOT ESTABLISHED**; builder leaves these bytes unwritten | **READ** complete stores in 0x8002C5CC..B6C omit this range. **GUESS-low:** unused/reserved member. No default value assigned |
| +0x24 | u32 | Bone-matrix array offset from W: `align4(4*M[0x18])`; 32 bytes per bone | **READ** 0x8002C794..7BC, 0x8002CAEC..AF8; use at 0x8002CC20..C28 |
| +0x28 | u32 | Track-offset array offset from W, after mutable vertices; u32[M+0x1C] | **READ** 0x8002C7D8..7E4, 0x8002C958..968, 0x8002CAFC/B08 |
| +0x2C | u32 | Cached track-key indices offset from W; u16[M+0x1C], region rounded to 4 bytes | **READ** 0x8002C7EC..808, 0x8002C960..96C, 0x8002CB04/B0C; key selection writes 0x8002CD10..D18 |
| +0x30 | u32 | Working 8-byte records offset from W; count M+0x20; copied from file records at +4 | **READ** 0x8002C750..75C, 0x8002CAB0..AE8, 0x8002CB10..B1C; **GUESS-medium:** deformation scratch |
| +0x34 | u16 | Face-block bit-array byte count: `align2((u32(M+0)+7)>>3) & 0xFF` | **READ** 0x8002C6D0..6E8; consumed at 0x8002C844 and 0x8002E4A4 |
| +0x36 | u16 | Cached evaluated frame, initially 0xFDFD, then `time % u32(M+0)` truncated to u16 | **READ** 0x8002CB60..B6C, 0x8002CC8C..CA4, 0x8002E408; getter 0x8003145C |
| **+0x38** | **u16** | **Phase length = u16(M+0x00)** | **READ** `lhu` 0x8002C5FC → `sh` 0x8002C604; getter 0x800314D4 |
| +0x3A | u16 | Trailing-list count, low half of M+0x28 | **READ** 0x8002C608..610; getter 0x800314E0 |
| +0x3C/+0x3E/+0x40 | s16 x/y/z | First bounds vector copied from M+0x38/+0x3A/+0x3C | **READ** 0x8002C614..630; getter copies 8 bytes at 0x800314A8. **GUESS-high:** lower extent |
| +0x42 | 2 bytes | Fourth halfword of that vector, copied from M+0x3E; meaning not established | **READ** same 8-byte copy |
| +0x44/+0x46/+0x48 | s16 x/y/z | Second bounds vector copied from M+0x40/+0x42/+0x44 | **READ** 0x8002C634..650; getter 0x8003147C. **GUESS-high:** upper extent |
| +0x4A | 2 bytes | Fourth halfword of that vector, copied from M+0x46; meaning not established | **READ** same 8-byte copy |
| +0x4C/+0x4E/+0x50 | s16 x/y/z | Centre: each `(first+second) >> 1`, arithmetic shift | **READ** 0x8002C654..6B8; pointer getter 0x80031474 |
| +0x52 | s16 on read | Radius: `0x800BF1CC(sum((second-centre)^2))`, stored as halfword | **READ** 0x8002C668..6CC; signed getter 0x80031468; cull use 0x8002FEA8..EC8. **GUESS-high:** enclosing-sphere radius, consistent with folio.md |
| +0x54 | u32 handle | ARS working allocation | **READ** allocation 0x8002C778..78C, locks at 0x8002CBF4/2E450; freed by 0x8002CBA0 |

The block-offset table starts at **W+0**, implicitly; there is no descriptor
field holding its offset. **READ** 0x8002C83C..84C fills it. W regions then occur
in this order: block offsets, matrices, mutable vertices, track offsets, cached
key indices, working 8-byte records. Their sizes respectively are aligned
`4*M[0x18]`, `32*M[4]`, `8*M[0x10]`, `4*M[0x1C]`, `2*M[0x1C]`, `8*M[0x20]`.

⚠ **DO NOT FIX** the `&0xFF` in D+0x34, the u16 truncation at D+0x38, or the
ride's length-minus-one. They are actual instructions. Also do not substitute
the largest key time: that is a different quantity. The animator itself divides
by **u32(M+0)** at 0x8002CC94, with **no +1**. `Mesh.HeaderWord0`'s existing
“plus one” comment reports conflicting advisor observations; those do not change
the ride clock's explicit u16 load and subtraction. Resolving the advisor/pose
discrepancy is outside this decode.

## 3. What P actually means

**READ**, flat-ride running path `0x8009CAF8..B48 → 0x800658D8`:

```
L = u16(mesh[selected sub-entry] + 0)
target = (L - 1) << 12
delta = (software IRQ counter now - saved counter) << 7
if mode == 3: delta >>= 1
if signed(delta) > 0x4000: delta = 0x4000
accumulator += delta
if unsigned(accumulator) >= unsigned(target):
    accumulator = 0
    completedCycles += 1
if signed16(completedCycles) >= A[0xC0]: status = 11
```

**READ-derived:** at normal speed, with no cap, the required counter advance per
phase is **32×(L−1) IRQs**, from `4096/128 = 32`. This supplies the formerly
unknown P in exact clock units. For Crazy Ape it is **1280 IRQs / 163840 fixed
units**; The Dizzy Tree **7616 / 974848**; Zero G **25600 / 3276800**. Completion
is sampled only on update calls and overshoot is discarded after **each** phase.

**READ** `0x800BC2EC..328` installs counter 2 with target 0x866 (2150) and flags
0x1000; `0x800D430C..39C` translates those flags to hardware mode 0x258.
`0x800BC258 → 0x800BC384` increments the **software** counter at 0x80103480,
unless paused; `0x800BC35C` reads it. `arrivals.md §2.1` establishes the nominal
counter input 4233600 Hz and measured deltas 9600..10112, commonly 9984.

**READ-derived, nominal hardware rate:** `r = 4233600/2150` IRQ/s, so the
continuous threshold time is `32*(L-1)*2150/4233600` seconds, approximately
**0.6500 s** for Crazy Ape, **3.8677 s** for The Dizzy Tree and **13.0008 s** for
Zero G. These are thresholds, not tick-rounded measured ride times. Normal
continuous time doubles under mode 3; sampled durations need separate rounding.

The committed table uses a **declared constant-delta reference** drawn from the
existing measurements, δ=9984, rather than inventing a timing constant:

```
Pδ = max(1, ceil(((L-1)<<12) / min(δ,0x4000))) update calls
Tδ = C * Pδ / 25 seconds
```

That gives Crazy Ape **Pδ=17 calls, default C=5, Tδ=3.40 s**; The Dizzy Tree
**98 calls, C=5, 19.60 s**; Zero G **329 calls, C=5, 65.80 s**. These exclude
loading, unloading and breakdowns. They are exact for the specified input
sequence and **not live measurements**. A nominal hardware-rate calculation and
the emulator's measured reference differ; neither should silently replace raw
deltas in `RideCycle`.

**READ:** entering running clears A+0x60 and A+0xF2 (`0x800656C4..D0`,
`0x8009C708..720`), but does not reset the timestamp A+0x10. Loading calls
`0x8009C884`, whose base hook 0x80065BD0 is empty, and does not advance this
clock. The first running delta can therefore include loading time and hit the
cap. **READ-derived:** if the first call contributes 0x4000 and all following
calls contribute 9984, Crazy Ape takes 16 calls for its first phase and 17 for
each later phase: **84 calls / 3.36 s** for five. Thus even `C×P` is only a
constant-phase simplification. In general the exact run is the **sum of the
per-phase completion intervals under the actual delta sequence**.

**NOT ESTABLISHED:** exact measured wall times for individual placed rides,
including first-call stamps, pauses, IRQ delivery and stalls. A trace of
`A+0x10`, `A+0x60`, `A+0xF2`, status and the software IRQ counter per update
would close that remaining measurement question. The disc lengths themselves
are established for every ride record, without this trace.

**READ:** the signed cap/unsigned completion mismatch is real. A negative raw
delta can complete a phase if it drives the accumulator negative. The earlier
claim that this follows from *ordinary hardware-counter wrap* was too strong:
the sampled value is a software IRQ count, and small elapsed differences across
its 32-bit wrap remain small under `subu`. No new wrap frequency is asserted.

## 4. Scope: other ride classes and the allegedly dead cache

**READ:** status 2 dispatches vtable slot **71** (0x80065C38..C48; status jump
table 0x800E1370). The actual slots are:

| Type | Vtable | Slot 71 | Consequence |
|---|---|---|---|
| 3 flat ride | 0x800E5514 | delta 0, 0x8009CA60 | Calls the phase clock and counts completions |
| 7 tour ride | 0x800E586C | delta −8, 0x800A1334 | Empty handler; cannot infer a tour duration from the flat-ride formula |
| 6 track ride | 0x800E5FE8 | delta −8, 0x800A6960 | Increments outer+0xFA each update; tests against twice slot-91 value; no phase-clock call |
| 1 coaster | 0x800E65A0 | delta −8, 0x800B0D68 | Calls loading/unloading slots 79/80; no phase-clock call |

**READ:** the tour load hook `0x800A0D88` receives **outer**, as its vtable slot
at 0x800E59A4 has delta −8. `0x800A0DF4` passes **outer+0x20 = A+0x18** to
`0x800300B8` with sub-entry **1**; `0x800A0E0C` caches the low byte at
**outer+0x100 = A+0xF8**, not A+0x100. It bypasses the logical phase map.

The getter `0x800A2894` **has direct JAL callers at 0x800A2EB4 and 0x800A2F04**.
They use **twice** this cached byte as a modulus for the vehicle's byte at +0x2A
(`0x800A2ECC`, `0x800A2F18`). The cache is live. The four tour records' sub-entry
1 lengths are **51, 51, 48, 101** (Tweety Tours, Flightmare Tours, Jurassic Tours,
Star Tours); none truncates in this build. The vehicle's complete trip time is
not established by this cache or this report.

## 5. Reproduction, counterexamples and validation

Inputs read directly, without using the old extracted `rip/` directory:

| File | Bytes | SHA-256 |
|---|---:|---|
| FOLIO.GAZ | 16164376 | `5666a0eeb18998a31fafbe845e7490cf65da76e722a69aec8b30ff2ee1a3c951` |
| TPW.BIN | 1065308 | `0b708dd74796abfedad55e55fa007ace1b5697407d36e8f7823bb06f4fa53beb` |

```
python3 -B tools/ride_phases.py /home/ec2-user/tpw/ext/FOLIO.GAZ --markdown
TPW_PHASE_ARCHIVE=/home/ec2-user/tpw/ext/FOLIO.GAZ python3 -B tools/test_ride_phases.py
python3 -B tools/mutate_ride_phases.py /home/ec2-user/tpw/ext/FOLIO.GAZ
dotnet test tests/TPW.Sim.Tests/
```

Default extractor output is JSON, including the archive hash, each mesh offset,
all mesh lengths, logical map, and per-level slider bounds/defaults. The Markdown
output is the committed table. The narrow timing table is explicitly requested
for this investigation; it does not import the full excluded `records.json` or
any raw meshes, strings table, binary or bulk disassembly.

**READ counterexamples:** Space Balls has cycles max 10 at all three levels, so
its default is 5, not fixed 1. Slither is 1/10/1 at the three levels, hence
default 1/5/1. Bounce on Iggy has minima 30/30/30 and maxima 45/50/55, but the
initialization `max(1,maxCycles>>1)` produces **22/25/27**. Do not add a minimum
clamp that the original does not execute. Tower-unconfirmed has L=1; its zero
target still needs an update call, so it is not a zero-second run.

The direct archive scan also found **244** definition headers with types 1..8:
91 Features, 59 flat rides, 37 shops, 33 sideshows, 12 coasters, 8 track rides,
4 tour rides. The old parser's `recOff+0x40 <= size` test excludes 47 short
Feature records and produces 197. No ride record is lost by that old guard.
Of all 268 containers, 244 have a record and a four-slot map, 17 have only a
four-slot map, and 7 have neither. These are **READ** directory/header counts.

Validation: **12 extractor tests pass; 34/34 deliberate mutations killed,
zero survivors**, restored baseline passes. The mutations cover archive
addressing, mesh count/stride, map ordering/fallback/modulo, packed-data rejection,
length offset/width/signedness, per-level slider reads and defaults, decrement,
fixed-point scale, cap, rounding/equality, zero target, overshoot discard,
class scope and seconds conversion. The runner edits the production extractor
one rule at a time and restores it in `finally`; a clean baseline is required
before and after. The C# simulator's **612 tests pass**; its changes here are
source/provenance comments, with no new simulation rules.
