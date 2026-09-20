# Theme Park World (SLES-026.88) — the advisor's 72 statistics

**READ** = instructions/data in `TPW.BIN`, loaded at `0x80010000`, or a quoted FOLIO entry/offset.
**GUESS** = interpretation, with confidence. Addresses refer to this PAL build. No new console
measurement was made. The machine-readable companion [statistics-rules.json](statistics-rules.json)
contains every decoded rule, every slot's rule readers/writers, and message-to-string IDs.

## 0. SOURCE DISAGREEMENTS

These are disagreements found in this investigation, including earlier guesses that the binary
now resolves. Existing findings and existing production files have **not** been rewritten. Where
an older finding specifies different behavior, the port retains it or leaves its opaque world
boundary intact. Refinements with no conflicting behavior are identified separately below.

| existing source | binary evidence (READ) | retained behavior / consequence |
|---|---|---|
| `shop-stock.md` §7.5 calls `0x80015A68` the “count features by flag” statistic | `0x80015AA0..AF4` allocates a bitmap; `0x80015C14..C80` maps feature positions to bits; `0x80015CA4..CF4` returns `100*popcount/bitmap_bytes`. It is spatial occupancy, **not feature count**, and the denominator is bytes, not bits. | Slot 31 is explicitly `RetainedUsableFeatureCount`, GUESS-low. It counts qualifying placed usable features. The binary formula is documented in §2.5 but is not silently substituted. This changes rule 114's threshold units. |
| `shop-stock.md` §7.6 says the loop compares “each freshly computed statistic” with the 12-byte records; `needs.md` §6 guesses statistic n corresponds to record n | `0x80016888..D8` uses cursor +0xB8 for refresh; `0x80016930..44` uses independent cursor +0xBA for a rule. Entry 1 has **125**, not 72 records; entry 2 rule 3 reads slots 11 and 1. | No existing code implements the proposed one-to-one correspondence. The new service keeps statistics and rule programs separate; it does not attach a message to a same-numbered statistic. The old one-to-one guess remains recorded here. |
| `shop-stock.md` §7.6 / `needs.md` §6 describe the rule records as level data | `0x8001675C..84` loads global FOLIO entries **1 and 2**, without a park/level selector. Direct archive decode confirms them. | The port loads the common decoded definitions. It adds no invented scenario-dependent threshold. |
| `litter.md` §5 and `shop-stock.md` §7.5 give four ticks per statistic / 288 ticks per cycle without the caller's gate | `0x80013298..B0` calls the loop only in advisor state **1**, flag bit **3** enabled. Other advisor states never call it. The loop itself advances once per call, not by elapsed global ticks. | Four eligible calls per statistic and 288 eligible calls per cycle retain the established cadence constants. `IParkStatisticsWorld.AdvisorIdle` and `StatisticsEnabled` expose the newly traced gate. A 288-tick wall-clock guarantee would be wrong while the advisor talks. |
| `litter.md` §5 says `0x80017124..1D0` can “change game state” | Opcode 8 at `0x800171BC` calls `0x800139EC`; this resolves message ID to text ID, then `0x8003A65C` finds that text in the UI message list and removes entries through `0x8003A52C`. It is **message removal**, not a simulation-state command. | `ApplyRuleAction8` remains opaque in the world interface, with both readings in its XML comment. No win/loss, arrival, guest, or staff change is invented. A host needs to resolve this disagreement before giving that action concrete behavior. |

**READ, refinements rather than contradictions:** litter really is slot **12**, not 30. Slot 30
really is the existing signed-16-bit cleanliness average. Need conditions really are exact codes
1 and 2. The unestablished toilet thresholds are now decoded (§4). `needs.md`'s GUESS-high
“happiness” name for V+0x59 remains GUESS-high in the code; it has not become a fact by repetition.
The feature-bit labels remain neutral: e.g. FOLIO entries 335/338/339 have flags `0x03`, while
349 has `0x02`; naming every bit-1 object a staff room would be wrong even though 353 is one.

**Independently re-read (tinyclaw, 2026-09-20), two of the rows above:**

- *The occupancy statistic.* 0x80015CA4 popcounts one BYTE per iteration (`lbu a0,0(v0)` →
  `jal 0x80015A44`) over s6 iterations, then builds 100·n by `n<<1 + n`, `<<3`, `+ n`, `<<2`
  and divides by s6 (0x80015CC8..0x80015CDC). So it is `100 × popcount / bitmap_BYTES` and the
  denominator really is bytes, not bits. shop-stock.md §7.5's "count features by flag" does not
  describe this routine.
- *The cadence gate.* 0x80013298 loads the advisor's flag byte, `andi v0,v0,0x8`, and branches
  past `jal 0x80016870` when it is clear. The statistics loop genuinely is not called unless
  that bit is set, so "288 ticks per cycle" is 288 ELIGIBLE calls and not wall-clock.

## 1. What owns the numbers, and who reads them

**READ:** this object belongs to the **advisor**, at advisor+0x1BC, not the park-statistics window.
Allocation/init: `0x80012F3C..50`. Pointer-access census (`fieldx.py 0x1bc`) finds the advisor's
constructor, destructor `0x80013134..64`, tick `0x800132AC`, and event wrapper `0x800139D0`.
Other classes' +0x1BC hits are unrelated window fields or virtual entries, inspected separately.
The constructor does not publish the statistics pointer as a park-global property. Its vtable
`0x800DB950` has only destructor `0x8001423C` after three zero words; it supplies no virtual getter.
A scan for address-forming `addiu` with immediate 0x1BC found no additional access path.

**READ:** the only located indexed **reader** of cached slots is interpreter `0x80017024`:
four comparisons at `0x80017078/A4/D0/F4`, Add's read-modify-write at `0x80017138`, and the
elapsed read at `0x800171DC`. Refresh `0x80016AB4` does not read another cached slot. It reads the
park and copies its result. There is no cached-stat getter called by the UI, guest, arrivals, or staff.
`callers.py` finds only `0x8001689C` calling the refresh switch and only `0x80016994` calling the
interpreter. Event writers are listed in §5; they do not read the cached slots.

**READ:** 67 slots have actual disc comparison readers. Slots **3, 5, 45, 55** have none; slot
**71** is the interpreter's special timer input, not an indexed comparison in the disc programs.
Thus 68 slots have a rule-reading role when scratch is included. No slot in this cache is a
display-only statistic. The three computed-but-unread slots 3/5/45 are unused outputs, **not**
evidence for an unseen display. Slot 55's counter is dead in this build (§5).

**READ:** these statistics drive **advisor decisions**. The active disc opcodes compare cached
values, reset event counters, post messages, and perform action 8. None of their instructions sets
guest needs, changes bus arrivals, hires staff, assigns patrols, initiates strikes, or awards research.
Guest/staff decisions read the underlying world independently. In particular:

- Need slots 1/2 → rules 3/4 → messages `0x01/0x02` (thirst/food advice). These do not change need.
- Litter 12 → rules 53/66/67/68 → cleaner praise/advice `0x7D/0x06/0x07/0x08`.
- Dirtiness 30 → rules 116..119 → `0x50..0x53`; it does not make guests sick through the cache.
- Patrol shares 32..39 and grades 40..44 → staffing/training advice. Slot 45 observes the mechanic
  strike flag but is never read. The strike evaluator `0x800681E4` works on staff directly.
- Ride variety/upgrades 20..29 and active research 50 → research/build advice. Their **refresh
  getters** can complete zero-cost research (§2.2); this is separate from reading the cached values.
- Finances 47/48 → wealth, low-balance, and wages advice; they do not book money.
- Event counters 51..70 → guest-experience, queue, ride-wear, price/quality, and catalogue advice.

**GUESS-high, bounded negative:** no cache-driven guest/arrival/staff mechanic exists in this build.
This conclusion combines the owned-pointer census with the fully decoded interpreter and disc
programs. It does not claim that an arbitrary mod, corrupt pointer, or different executable cannot
add another reader. Arrival formulas `0x80067274` / `0x800691F0` and the direct litter effects in
`litter.md` remain separate. The advisor's sentences are advice, not proof that their suggestions
are implemented by the quantity they mention.

## 2. All 72 slots

**READ:** `0x800DB9F8 + 4*n` is the switch target. Storage is **signed 16-bit**, read with `lh`,
written with `sh`. The table below gives the ordinary computation's range, before arbitrary script
writes. Script Add/Set can put **−32768..32767** in a cache slot. “A” means an actual advisor
comparison reader; “U” means no located reader; “T” means special interpreter timer. Exact rule
numbers and their associated message IDs are enumerated per slot in `statistics-rules.json`.

### 2.1 State, time and counts

| slot | measure (READ unless labelled) | case → source | units / normal range | reader |
|---:|---|---|---|---|
| 0 | park-open flag | `0x80016B0C` → `0x800541AC` | bool 0/1 | A |
| 1 | exact guest condition **1** share (need B alone) | `0x80016B1C` → `0x80091C28` | integer %, 0..100 | A |
| 2 | exact guest condition **2** share (need A alone) | `0x80016B74` → `0x80091C28` | integer %, 0..100 | A |
| 3 | total elapsed days, unsigned cap 30000 | `0x80016C0C` → `0x80066E78` | days, 0..30000 | U |
| 4 | month + 12*year, unsigned cap 30000 | `0x80016C30` → `0x80066E90/9C` | months, 0..30000 | A |
| 5 | calendar year, unsigned cap 30000 | `0x80016C78` → `0x80066E9C` | years, 0..30000 | U |
| 6 | entertainers, subtract one held on cursor | `0x80016C9C` → `0x800539D4` | people, 0..5 | A |
| 7 | mechanics, subtract one held | `0x80016CAC` → `0x8005395C` | people, 0..5 | A |
| 8 | guards, subtract one held | `0x80016CBC` → `0x800538E4` | people, 0..5 | A |
| 9 | researchers, subtract one held | `0x80016CCC` → `0x80053998` | people, 0..5 | A |
| 10 | cleaners, subtract one held | `0x80016CDC` → `0x80053920` | people, 0..5 | A |
| 11 | all live guest-pool entries, including gate guests | `0x80016CEC` → `0x800537B0` | people, 0..100 | A |
| 12 | all live litter-pool entries, including vomit | `0x80016CFC` → `0x80053690` | pieces, 0..40 | A |
| 13 | nonzero-status rides (types 1/3/6/7) | `0x80016D0C` → `0x80014554(0xF)` | attractions, 0..22 | A |
| 14 | nonzero-status shops | `0x80016D14` → `0x80014554(0x100)` | attractions, 0..20 | A |
| 15 | nonzero-status sideshows | `0x80016D1C` → `0x80014554(0x200)` | attractions, 0..10 | A |
| 16 | nonzero-status features, all subclasses | `0x80016D24` → `0x80014554(0xF0)` | attractions, 0..45 | A |
| 17 | nonzero-status usable features (slot 33) | `0x80016D2C` → `0x80014554(0x20)` | attractions, 0..45 | A |
| 18 | nonzero-status features in flag-bit-1 category | `0x80016D34` → `0x80014554(0x80)` | attractions, 0..45 | A |
| 19 | nonzero-status features in flag-bit-3 category | `0x80016D3C` → `0x80014554(0x40)` | attractions, 0..45 | A |

**READ:** 1/2 divide matching linked-list guests by the live pool count, multiply by 100 **before**
division, truncate, and return zero if the pool count is zero. Code 7 (both needs high) matches
neither. Existing `VisitorCondition` supplies the priority/threshold behavior; its GUESS names
remain qualified. Capacity bounds above come from the existing pool findings (`Attractions.PoolSize`,
`wages.md` §4, `litter.md`), not the purchase screen.

**READ:** “placed” in 13..19 means `status != 0`, via `0x800660DC`, **not** “open to guests”.
Construction, player-closed, broken and condemned attractions count. Feature classification is
an **else-chain**: slot 33/bit 0 → mask 0x20; else bit 3 → 0x40; else bit 1 → 0x80; else → 0x10
(`0x8001466C..E8`). Bits are not independent; rubbish bins (bit 2) go in the remainder.

### 2.2 Variety, upgrades and features

| slot | measure | case → computation | units / normal range | reader |
|---:|---|---|---|---|
| 20 | distinct built ride definitions / available ride definitions | `0x80016D50` → `0x8001472C(0xF)` | %, 0..100 | A |
| 21 | available ride definitions / all ride definitions | `0x80016D58` → `0x800150C0(0xF)` | %, 0..100 | A |
| 22 | best applied upgrades per built ride definition / unlocked upgrades on those definitions | `0x80016D60` → `0x80015750` | %, 0..100 | A |
| 23 | unlocked upgrades / two possible upgrades per unlocked ride base | `0x80016D70` → `0x80015920` | %, 0..100 | A |
| 24 | distinct built / available shop definitions | `0x80016D80` → `0x8001472C(0x100)` | %, 0..100 | A |
| 25 | available / all shop definitions | `0x80016D88` → `0x800150C0(0x100)` | %, 0..100 | A |
| 26 | distinct built / available sideshow definitions | `0x80016D90` → `0x8001472C(0x200)` | %, 0..100 | A |
| 27 | available / all sideshow definitions | `0x80016D98` → `0x800150C0(0x200)` | %, 0..100 | A |
| 28 | distinct built / available feature definitions | `0x80016DA0` → `0x8001472C(0xF0)` | %, 0..100 | A |
| 29 | available / all feature definitions | `0x80016DB4` → `0x800150C0(0xF0)` | %, 0..100 | A |
| 30 | 100 − mean usable-feature cleanliness | `0x80016DC8` → `0x800153B4` | dirtiness points, normally 0..100 | A |
| 31 | binary: spatial usable-feature occupancy; retained code: feature count, GUESS-low | `0x80016DD8` → `0x80015A68(0x20)` | binary 0..800 (see below); retained count 0..45 | A |

**READ:** 20/24/26/28 use a 50-byte scratch set **per attraction type** (`0x80014778..90` and
repeated blocks); duplicate instances of an index count once, but equal indices in different
types are distinct. Only nonzero-status instances count. A type with zero available definitions
contributes neither numerator nor denominator. All four cap at 100, and **0/0 returns 100**
(`0x8001503C..90`). Availability is `0x8006A92C`, not a pool vacancy or status flag.

**READ:** 21/25/27/29 count availability over catalogue definitions, ignoring instances and status.
They also return 100 for 0/0 (`0x8001533C..88`). **⚠ DO NOT FIX:** the helper tests bit 4 twice,
for track rides then coasters (`0x8001519C..1F4`), rather than testing bit 8 for coasters. The only
ride-mask call is 0xF, so both implementations agree on these 72 slots. The port intentionally
does not expose this helper as a general arbitrary-mask API.

**READ:** 22 uses maximum `upgradeLevel+1` over instances **including status 0**, then subtracts
one; its denominator is `0x8006AC04(type,index)-1` only for represented definitions
(`0x800155B8..E8`, `0x800156BC..708`). Empty, or numerator >= denominator, returns 100. 23 visits
all ride definitions with nonzero `0x8006AC04`; each adds 2 to the denominator and `count-1` to
the numerator (`0x800158C4..EC`). It does not divide by three levels. Count is 0..3
(`0x8006AC84..94`). These are weighted sums, not means of already-rounded percentages.

**READ:** 30 includes usable features even at status zero; it adds signed cleanliness bytes with
a signed-16-bit running sum/count, returns zero for an empty set, and truncates signed division
before subtracting from 100. `FeatureStock.ParkDirtiness` is reused, including its established
overflow behavior. This does not count pieces on the ground.

**READ — ⭐ the catalogue query is effectful:** `0x8006A92C` returns true directly in debug
(`[0x80102E88]`) or restricted mode (`0x80059A9C`); otherwise it asks `0x8006AB54` for base-level
progress and compares with 100. That helper calls `0x8006A9A8` for the required research cost.
If the requested level is already below the progress record's completed-level byte (+3), it
returns 100. If its cost is zero, it calls `0x8006ACB8` to complete the level, then returns 100.
Otherwise it returns the record's progress byte (+2). Cost comes from ride definition
`+0x48+0x34*level` (`0x8006AFC0`) or other definition `+0x28` (`0x8006B008`).

**READ:** the level-count query `0x8006AC04` repeatedly completes successive zero-cost levels
until the count reaches three or the next cost is nonzero. Debug mode returns three directly.
Completion `0x8006ACB8 → 0x8006ACDC` increments the completed-level count and resets progress
to zero via `0x8006AF24/38`; the readers are `0x8006AF18/2C`. Thus statistics refreshes can
change catalogue research records even though **cached values** have no research-unlock reader.
The world interface exposes these as explicit calls, not eagerly populated availability fields.

**READ:** call order follows the helpers: built variety (`0x8001472C`) visits Ride, Tour, Track,
Coaster, Feature, Shop, Sideshow; available variety (`0x800150C0`) visits Ride, Tour, Track,
Coaster, Shop, Sideshow, Feature. Only selected categories are queried, with ascending definition
indices. Upgrade helpers (`0x80015750` / `0x80015920`) visit Ride, Track, Tour, Coaster. Slot 22
queries levels only for represented definitions, including status-zero instances; slot 23 queries
all ride definitions. Preserving these calls preserves the opportunity and timing for the host's
zero-cost completion side effect. Implementing the catalogue itself remains the world's job.

### 2.3 Staff summaries

| slot | measure | case → helper(mask) | units / range | reader |
|---:|---|---|---|---|
| 32 | mechanics without a patrol rectangle | `0x80016DE8` → `0x800161F0(1)` | %, 0..100 | A |
| 33 | cleaners without a patrol rectangle | `0x80016DF0` → `0x800161F0(2)` | %, 0..100 | A |
| 34 | guards without a patrol rectangle | `0x80016DF8` → `0x800161F0(4)` | %, 0..100 | A |
| 35 | entertainers without a patrol rectangle | `0x80016E00` → `0x800161F0(8)` | %, 0..100 | A |
| 36 | sampled path cells covered by mechanic rectangles | `0x80016E14` → `0x80015D2C(1)` | %, 0..100 | A |
| 37 | sampled path cells covered by cleaner rectangles | `0x80016E1C` → `0x80015D2C(2)` | %, 0..100 | A |
| 38 | sampled path cells covered by guard rectangles | `0x80016E24` → `0x80015D2C(4)` | %, 0..100 | A |
| 39 | sampled path cells covered by entertainer rectangles | `0x80016E2C` → `0x80015D2C(8)` | %, 0..100 | A |
| 40 | mechanic grade sum *100 / (4*count) | `0x80016E40` → `0x800163F8(1)` | normal 0..100; 3-bit input permits 175 | A |
| 41 | cleaner grade percentage | `0x80016E48` → `0x800163F8(2)` | same | A |
| 42 | guard grade percentage | `0x80016E50` → `0x800163F8(4)` | same | A |
| 43 | entertainer grade percentage | `0x80016E58` → `0x800163F8(8)` | same | A |
| 44 | researcher grade percentage | `0x80016E60` → `0x800163F8(16)` | same | A |
| 45 | mechanic strike flag | `0x80016E74` → `0x80068714(calendar,2)` | bool 0/1 | U |

**READ:** class masks differ from both `StaffKind` and the count-slot order. All these helpers walk
the entire class list, including staff held on the cursor. Grades read `(P+0x3C)&7`
(`0x800956F0`), not morale or tiredness; grade 4 yields 100 and the helper does not clamp to 100.
Grade means and no-staff patrol shares return zero. No-rectangle share is
`100-floor(assigned*100/total)` (`0x80016380..D4`), **not** `floor(unassigned*100/total)`.

**READ:** coverage uses `width>>2` by `height>>2` cells (`0x80015D60..88`). A cell is a path cell
if ANY of its four even-offset tile samples (0,0), (0,2), (2,0), (2,2) has tile type 2
(`0x80015DD8..E64`, `0x8004D4EC`). **⚠ DO NOT FIX:** odd-offset paths are not sampled.
Assigned patrol bounds are signed tile coordinates, shifted right two, both far edges inclusive
(`0x80015F64..0x80016000`); OR bit 2 into those cells. Count cells equal to 3 / cells equal to 1
or 3, truncate; no covered cells returns zero (`0x80016110..B8`). Overlap is not counted twice.
There is **no security-camera pass**, despite message 0x16 mentioning cameras. The original assumes
valid in-map patrol rectangles; the port adds no clipping behavior to malformed host input.

### 2.4 Money, experience, event copies and timer

| slot | measure | source (READ unless GUESS noted) | units / range | reader |
|---:|---|---|---|---|
| 46 | mean V+0x59; **GUESS-high happiness** | case `0x80016E90`, `0x80092610` | points, 0..100 | A |
| 47 | current bank balance /10 /100, signed, clamp ±30000 | `0x80016F08`, `0x800868A0`, `0x80017274` | £100 units, −30000..30000 | A |
| 48 | previous month's whole-pound wages > whole-pound total income | `0x80016F68`, `0x8008781C` / `0x8008767C`, age 1 | bool 0/1 | A |
| 49 | weighted attraction pool count | `0x80016FB8` → `0x800142E8(0x3FF)` | score, 0..477 at pool capacities | A |
| 50 | at least one active research topic | `0x80016FD4` → `0x8009B5B8` | bool 0/1 | A |
| 51 | copy event 0: ride warning entries | `0x80016FEC`; writer `0x8009C778` | event count, ±30000 native | A |
| 52 | copy event 1: ride broken entries | same; `0x8009C7F8` | event count, ±30000 native | A |
| 53 | copy event 2: guest misbehaviour events | same; `0x8008D584/734` | event count, ±30000 native | A |
| 54 | copy event 3: queue abandonment, +8 each | same; `0x800908A4` | points, ±30000 native | A |
| 55 | copy event 4: **dead counter in this build** | same; no native writer, no disc reader/writer | zero after init in normal flow | U |
| 56 | copy event 5: balloon allocations | same; `0x8008EC54` | event count, ±30000 native | A |
| 57 | copy event 6: costume branch | same; `0x8008EB90` | event count, ±30000 native | A |
| 58 | copy event 7: souvenir branch | same; `0x8008ECB0` | event count, ±30000 native | A |
| 59 | copy event 8: catalogue nonzero-result branch; **GUESS-medium rejected build attempts** | same; `0x8002391C` | event count, ±30000 native; rule 124 writes 2001 | A |
| 60 | copy event 9: shop value group 0/4/7 | same; `0x8008ED90..CC` | signed points, ±30000 native | A |
| 61 | copy event 10: shop value group 2/3/6 | same; `0x8008EDC0..CC` | signed points, ±30000 native | A |
| 62 | copy event 11: shop value group 5 | same; `0x8008EDA0..CC` | signed points, ±30000 native | A |
| 63 | copy event 12: shop value group 1 | same; `0x8008EDB0..CC` | signed points, ±30000 native | A |
| 64 | copy event 13: sideshow value | same; `0x8008F064` | signed points, ±30000 native | A |
| 65 | copy event 14: shop satisfaction group 0/4/7 | same; `0x800B700C..48` | signed points, ±30000 native | A |
| 66 | copy event 15: shop satisfaction group 2/3/6 | same; `0x800B703C..48` | signed points, ±30000 native | A |
| 67 | copy event 16: shop satisfaction group 5 | same; `0x800B701C..48` | signed points, ±30000 native | A |
| 68 | copy event 17: shop satisfaction group 1 | same; `0x800B702C..48` | signed points, ±30000 native | A |
| 69 | copy event 18: sideshow satisfaction | same; `0x800B7980` | signed points, ±30000 native | A |
| 70 | copy event 19: entry-price judgement | same; `0x80090EBC` | signed points, ±30000 native | A |
| 71 | elapsed days since this rule last failed a comparison | **no refresh** (`0x800DBB14` → `0x80017004`); `0x80016974` writes it | signed low 16 bits of day difference | T |

**READ:** 46 uses an unsigned sum divided by live pool count, empty returns zero, result capped at
100 (`0x80016EC4..F04`). 47's divisions truncate toward zero. 48 converts **both operands** to whole
pounds before its strict comparison; wages £10.9 and income £10.1 therefore compare equal. Its
history accessors return zero if bank history index <= 1 (`0x80087610..74`). It compares wages,
not all spending, not net profit, and not this month's running totals (`economy.md` BANK+0xE3C/2FC).

**READ:** 49 gives **6 per ride, 4 per shop/sideshow, 5 per feature**, including status zero. It does
not count people or staff: those require mask bits 0x400/0x800, absent from 0x3FF. Accumulation
wraps unsigned 16 bits inside the helper, then the switch caps at 30000. `0..477` is the derived
capacity bound: `6*(15+3+2+2)+4*(20+10)+5*45`. This is not the arrivals score or the UI Overall Rating.
50 checks each of five 0x1C-byte research records' +0xC active words (`0x8009B5D8..F8`).

### 2.5 Slot 31's binary formula, intentionally not substituted in the port

**READ:** `bytes_per_row = trunc((map_width+127)/128)`, `rows = trunc((map_height+15)/16)`;
the bitmap has `bytes_per_row*rows` bytes (`0x80015A98..AE0`). For each nonzero-status feature
whose requested classification test succeeds, use its slot-10 position: signed components /16,
X/8 to pick a byte and X&7 to pick a bit, Y as row (`0x80015BDC..C80`). For mask 0x20 the test is
slot 33. Finally count set bits and return `100*bits/bytes` (`0x80015C98..CF4`). It can reach 800,
does not clamp at 100, and traps on a zero-byte bitmap. Duplicate positions share a bit. This is
why the old count label and a plausible “percentage of park with toilets” label are both unsafe.

## 3. The round robins and uncomputed reads

**READ:** object layout, initialization `0x80016718..838`:

| offset | field |
|---|---|
| +0x00..8F | 72 signed halfwords, initialized zero |
| +0x90..B7 | 20 backing event counters, initialized zero |
| +0xB8 | u16 refresh cursor, zero |
| +0xBA | u16 rule cursor, zero |
| +0xBC | first-sweep-complete byte, zero |
| +0xC0 | handle for FOLIO entry 1 (rule scheduling records) |
| +0xC4 | u8 rule count = entry-1 byte length /12 = 125 |
| +0xC8 | handle for FOLIO entry 2 (rule programs) |

**READ:** on an eligible call (`0x80016870`): if `(cursor&3)==0`, refresh `cursor>>2`.
Then `cursor=(cursor+1)%288`. On wrap, set the first-sweep byte permanently. Thus slot n first
refreshes on **call 4*n+1** (slot 71 has no write); slot 0 changes on calls 1, 289, 577…
Nothing ages, averages, interpolates, or clears an unrefreshed value. Native counters and script
writes are the explicit exceptions, with different destinations (§4/§5).

**READ:** before first wrap, no rule reads anything. On call **288 itself**, rules become eligible;
all computed/counter slots 0..70 have been visited. If advisor queue cursors differ, return without
advancing the rule cursor (`0x800168EC..910`). Refresh cursor still advances. Otherwise visit exactly
one of 125 rules **on every call**, even the three calls without a statistic refresh. No same-index
pairing. A skipped/not-yet-due rule still advances. A rule may read values up to 287 eligible calls
old and may combine values from different world times.

**READ:** slot 71 is initialized zero but has no compute case. Before each due rule evaluation,
the scheduler writes `(short)(nowDay-lastFailureDay)` there (`0x80016960..74`). Opcode 9 reads it.
No located consumer reads an uninitialized heap halfword: initialization zeroes all 72, the
first-sweep barrier precedes rule reads, and timer scratch is written before use. A zero value
can nevertheless mean “empty set”, “a genuine zero”, or “never computed by this empty case”.
There is no per-slot validity flag in the original.

## 4. All rule programs, timers and effects

**READ:** FOLIO entry 1 is **1500 bytes / 12 = 125 records**; entry 2 is **3604 bytes** of s16 words.
`tools/statistics.py` reads the archive directory directly, decodes every record/program, and
records source archive offsets and SHA-256 fingerprints in `statistics-rules.json`. It emits the
reviewable C# rule definitions. Every instruction carries its entry-2 word offset in the finding.
The source date words are `0xCCCCCCCC` placeholders; init overwrites them, they are not thresholds.

**READ:** scheduling record is `{s16 program_word_offset, u32 next_check_day, u16 cooldown_days,
u32 last_failure_day}`, with the words at unaligned +2/+8. Init sets next-check=0 and last-failure=now.
Run only if **next_check_day < now**, unsigned. Before running set slot 71 as above.

| opcode | operation (READ, `0x800DBB18`) | failure / completion effect |
|---:|---|---|
| 0 | end | result 0: next-check := now + cooldown (u32 wrap) |
| 1 | require cached[slot] == signed value | failed → result 1; last-failure := now |
| 2 | require cached[slot] != signed value | same |
| 3 | require cached[slot] < signed value | same |
| 4 | require cached[slot] > signed value | same |
| 5 | cached[slot] += value; wrap s16 | backing counter unchanged; no disc program uses opcode 5 |
| 6 | cached[slot] := value | slots >=51 also write backing storage at `+0x90+2*(slot-51)` |
| 7 | post message (u16 ID) | immediate, `0x80017184..B0` |
| 8 | action on message (s16 ID) | source disagreement §0; binary removes UI message |
| 9 | require cached[71] > signed duration | failed → result 2; leave BOTH scheduling dates alone |

**READ:** predicates form an ordered AND, with immediate actions interspersed; a later failure
does not roll back earlier actions. A successful run does not reset last-failure. Continuous-condition
waiting is therefore measured since the last failed comparison, with time passing during cooldowns
and advisor pauses. No 32-bit widening of slot 71: after 32767 elapsed days it becomes negative.
**⚠ DO NOT FIX:** hypothetical opcode-6 Set of slot 71 also writes +0xB8 (the refresh cursor).
No disc program does this, but the addressed store is retained in the interpreter.

Selected fully decoded examples (**READ**, rule numbers zero-based, offsets in the companion):

- Rule 3, word 47, cooldown 100 days: `S11>20 && S1>10 && elapsed>30`, action8(1), post(1).
  Rule 4, word 60: identical with S2/message2. The “thirsty/hungry” label link is now established
  by the message **IDs**; the neutral field names in existing code remain unchanged.
- Rule 13, word 135, cooldown 10: `S11>1 && S46>1 && elapsed>10`, post 0x7A. **⚠ DO NOT FIX:**
  the second threshold is **1**, not a plausible high-happiness threshold.
- Rules 66/67/68: increasing cleaner warnings from litter **>10 / >20 / >30**, with calendar-month,
  cleaner-count, and elapsed conditions. The exact conjunctions, including strict edges, are in JSON.
- Rules 116..119, words 1655/1677/1696/1718: dirty-feature messages. All require elapsed>30.
  No cleaners and `40<S30<70` posts 0x50; no cleaners and S30>70 posts 0x52. With cleaners,
  the corresponding messages are 0x51 and 0x53. All four first action8 the IDs 0x50..0x53.
  **Exactly 40 or 70 fires none**. Cooldowns are 100/100/200/100 days respectively.
- Rule 114, word 1623: months>19, S17!=0, S31<70, elapsed>60 → toilet-distribution advice 0x4E.
  Its retained count-vs-binary-coverage disagreement is material, not just a name change.
- Rule 121, word 1750: S48==1, elapsed>1 → wages-over-income message 0x5D, cooldown10.
- Rule 124, word 1790: S59!=0 and S59<2000 → post0xBE, Set S59=2001, cooldown900.
  It is a one-time latch unless some other writer changes the value; not a count of placed buildings.

**READ:** explicit periodic counter resets exist independently of warning rules, e.g. rule 5 sets
S54=0 every 180 days when visited/due; rule 6 tests S54>40 continuously for >20 days, then sets it
zero and posts 0x37. These programs cannot be replaced by a threshold table alone.

## 5. Native event producers and the second copy

**READ:** every located native writer calls `0x800139B4(advisor,eventIndex,amount)`, gated by
advisor flag bit 3; it calls `0x80016A50`. Valid indices are 0..19, others ignored. Read backing
halfword, add, store low 16 bits, interpret that as signed, then clamp to **[−30000,30000]**.
**⚠ DO NOT FIX:** wrap happens **before** clamp; 32768 added to zero becomes −30000, not +30000.
The cache changes only when case 51+index copies the backing value (`0x80016FEC..17000`).

**READ:** writers of 0/1 count warning/broken **state entries** at `0x8009C778/7F8`; they are not
the number of rides presently broken. Event 2 counts both branches of visitor misbehaviour
`0x8008D584/734`, even if the projectile allocation failed. Event 3 adds **8**, not 1, on queue
abandonment (`0x800908A4..A8`). Event 4 has no writer in the full static-call census. Event 5
increments only after balloon allocation succeeds (`0x8008EBFC..C58`). Costume/souvenir branches
write events 6/7 independently (`0x8008EB44..B94`, `0x8008ECA4..B4`). Event 8's caller and limits
are known; its full high-level name remains GUESS-medium (§2.4).

**READ:** shop value events 9/10/11/12 select groups by table `0x800E3B34`:
product codes 0/4/7 → 9; 2/3/6 → 10; 5 → 11; 1 → 12. Amount is signed truncation of
`(guest's calculated value − shop price)/2` (`0x8008ED40..60`). Sideshow value event 13 uses the
analogous difference/2 (`0x8008F040..68`). Product satisfaction events 14/15/16/17 use the same
grouping (`0x800E6D44`), with `trunc((max(0,input)-50)/4)` (`0x800B6FA0..FDC`). Sideshow event 18
uses that same expression (`0x800B7940..84`). The caller passes five times the relevant guest-stat
improvement (`0x8008ED1C..3C`, `0x8008F024..3C`). Entry event 19 receives the entry-price judgement
at `0x80090EBC`; its producer's existing `parkopen.md`/visitor findings are not rewritten here.

**READ:** advisor texts call the five groups food, shops, restaurants, drinks, sideshows, in that
order (message IDs 0x9A..0xA3/0xA4..0xAD). **GUESS-high:** those are useful user-facing group
descriptions. The code keeps product-group numbers rather than claiming every definition in a
group has a uniquely established business name. These counters are signed **sums**, not mean
customer satisfaction, percentages, revenue, nor numbers of unhappy guests.

## 6. Where the numbers are shown and string IDs

**READ:** no 72-label array and no display reader of this cache was located. Do not attach a
same-numbered English string to slot n. There are two relevant, different display surfaces:

1. Advisor messages: the exact rules above read the cache. Message table is
   `0x800EE4FC + 20*messageId`; first u16 is the English string ID in **FOLIO 0x197**
   (`0x800139EC..3A4C`, also established in `shop-stock.md`). The companion includes all **107**
   message IDs appearing in these rules, their text IDs, record addresses, and text. Per-slot
   `associated_message_ids` is the union of posts in rules comparing that slot: it does **not**
   mean that slot alone is sufficient to post the message. The rule's complete AND is authoritative.
2. The laptop's Park Statistics/Information window `0x800814AC` reads live guests and the calendar's
   **separate histories**. Its labels and icons must not be treated as labels for all 72 cache slots.

**READ:** specific UI labels useful to the panel implementation:

| surface | string ID | source / relationship |
|---|---:|---|
| Information title | 0x1FB | `0x80081574` |
| Visitor Information / Statistics / Park Finance / Awards tabs | 0x31C / 0x3EF / 0x09E / 0x05D | u16 table `0x8010315C`, read `0x80081618` |
| Park Overall / Park Finance / Awards sub-tabs | 0x246 / 0x09E / 0x1C9 | u16 table `0x80103174`, read `0x80081964` |
| People's Feelings / Dominant Thoughts | 0x329 / 0x123 | `0x80082004/24`; live guest mean and `VisitorCondition.TopThree` |
| People Visited | 0x085 | `0x80082048`, separate counter `0x80069308`, not S11 |
| People In Park | 0x1E8 | `0x800E30F4[0]`; history accessor `0x80066FA8`, same underlying pool concept as S11 |
| Arrival Rate | 0x243 | `0x800E30F4[1]`; history `0x80066FE4`, **not** S49 or any arrival-controlling cache slot |
| Happiness | 0x36B | `0x800E30F4[2]`; history `0x80067020`, related to S46 but stored independently |
| Time In Park | 0x205 | `0x800E30F4[3]`; history `0x8006705C`, no corresponding cache slot |
| Overall Rating | 0x2A6 | `0x800E30F4[4]`; history `0x80067098`, **not** S49 |
| Years (graph axis) | 0x394 | `0x8008284C`, not a reader of unused S5 |

**READ:** graph labels at `0x80082A2C..54`; history dispatch `0x80082964..BC`, five independent
144-byte histories at McAi+0x28/+0xB8/+0x148/+0x1D8/+0x268. The visitor window recomputes its
mean and condition histogram directly at `0x80081700..8F0`; condition icons are the existing
sprite table `0x800E3100`, not string IDs.

**READ:** opcode 7 builds a message with payload-present byte zero (`0x80014118/2C`) and submits
it via `0x80014144 → 0x80013C1C`. The advisor either queues it (`0x80013D30`, deduplicating IDs)
or makes it pending (`0x80013C80`); showing it goes through `0x80013EFC`. That function sends the
text to `0x800385AC` and, if enabled, plays audio with `0x8001853C`. **Text ID 0x124 is an explicit
no-text sentinel** (`0x80013F48..50`), not a missing caption to fill in. Message 0xBE, used by rule
124, has this ID and an empty English string. Its voice/complete gameplay meaning is not established.
Advisor-completion special cases for IDs 0x8D, 0xDD and 0x120 at `0x8001346C..A4` and
`0x80013680..DC` are not reachable from any of the 125 statistic programs' post IDs.

**READ:** there is no eager refresh of the advisor cache on opening
this window, so a graph/window and an advisor message can describe different sampling times.

## 7. Port boundaries, verification and reproducibility

The port consists of `ParkStatistics`, `ParkStatisticCalculator`, the instruction/rule types,
the decoded `ParkStatisticRules`, and `IParkStatisticsWorld`. The host supplies live lists/pool
counts, effectful catalogue queries, bank history accessors, tile queries and advisor state. It reports native
events through `AddEvent` at the §5 sites; the service owns only its cache/counters/rule clocks.
`game/` and other simulation systems were not changed. Action8 and slot31 retain the explicit
source disagreements. A complete advisor UI machine and bank/research/catalogue ownership are
outside this service. Catalogue queries must run when requested, including their zero-cost research
completion behavior; see §2.2 and the interface XML comments.

Reproduce the data decode:

```sh
python3 tools/statistics.py /home/ec2-user/tpw/ext/TPW.BIN /home/ec2-user/tpw/ext/FOLIO.GAZ
dotnet test tests/TPW.Sim.Tests/
python3 tools/mutate_statistics.py
```

Tests state the wrong implementation they reject. The disc-table test compares every instruction,
operand, cooldown and order against the separately decoded, addressed finding; behavioral fixtures
also exercise actual rule predicates and effects. The mutation runner restores all production
sources in a **top-level try/finally**, records SHA-256 fingerprints, baseline, real failing test
names and final full-suite result in [statistics-mutations.json](statistics-mutations.json).
There were **1309** passing baseline test cases in this worktree before these changes.

**Verification:** **1371 passed**, including **62 new cases**. **480/480 planned mutations killed**,
zero remaining survivors, zero invalid mutants. This includes all 72 slot numbers, both a cooldown
and an operand mutation for each of the 125 disc programs, and 158 calculation/world-query/opcode/
scheduling/counter mutations. The runner rebuilds the changed production assembly and runs the
baseline-compiled xunit tests, preserving their original inlined constants. All recorded production
and current test SHA-256 values match the restored files. Re-decoding the executable and archive
also reproduced the findings JSON and original generated C# table byte for byte.

The happiness-sum mutation initially survived: fixture sums 199 and 200 both truncate to 66 when
divided by three. Changing the actual sum to 197 distinguishes 65 from 66 and killed it on rerun.
The JSON retains the initial survivor in `prior_non_kills` and earlier test hashes in `earlier_sweeps`;
`--resume` retries non-kills and newly added mutations only after verifying exact production restoration.

**Not established:** the complete semantics of catalogue-result event 8; a literal human category
name covering every unusual feature flag combination; any additional dynamic reader beyond the
bounded census above. The code assigns no guessed numerical fallback to these unknowns.
