# Influence list — SLES-026.88, 2026-09-20

READ = instructions/data in `/home/ec2-user/tpw/ext/TPW.BIN`, loaded at `0x80010000`.
SHA-256 `0b708dd74796abfedad55e55fa007ace1b5697407d36e8f7823bb06f4fa53beb`.
GUESS = interpretation, not a replacement for a read. All object offsets below are outer-pointer
relative. Companion reports: behaviour.md §2.9/§3.2, visitor-rest.md §2, litter.md §0.

## §0 SOURCE DISAGREEMENTS

The findings' versions remain in the existing code. This component supplies storage and explicit
lifecycle operations; it does not silently rewrite the visitor or entertainer state machines.

| Existing source | Binary reading, with addresses | Version kept in code |
|---|---|---|
| behaviour.md §3.2: after 600 ticks, leave performance **if no guest is adjacent** (AND); already disputed in `Entertainer.Perform` | `0x80095B68..B94` leaves on expired timer **OR** missing audience. The area is returned by the following Idle handler, `0x80095A88..AA0`, not by the state-change instruction. | Existing AND in `Entertainer.Perform`; the map does not impose its own 600-tick expiry. Integration test explicitly keeps the area with an audience at tick 10000. |
| behaviour.md §2.1/§5: Idle roll 5 drops litter; visitor-rest.md §2 and litter.md §0 explicitly retain that interpretation | `0x8008D710..724` creates emitter descriptor `0x800F79D8` via `0x80089E38`. Its initializer at `0x8008C2B4` allocates flag 4, **not** a persistent litter piece. | Existing `VisitorIdle.DropLitter` unchanged. `CreateUnpleasantParticle` is an explicit API for an actual successful emitter allocation, not a replacement for that hook. |
| behaviour.md §1/§4: mode flag suppresses litter everywhere; litter.md §0 item 3 retains that for roll 5 | `0x8008D6B0..738` and `0x80089E38..EBC` have no litter-mode gate; persistent allocator `0x800514F0..50C` does. | Existing Idle suppression unchanged. The separately established effector allocator has no suppression service and cannot invent that gate. |

**Source omission, distinguished from a contradiction:** behaviour.md §3.2's pelt summary does not
mention aura removal; the existing `Entertainer.Pelted` consequently never calls `ReleaseInfluence`.
READ `0x80095E4C..E64` returns the area and clears E+0x4C. This report records the missing operation,
and the new API documents the explicit release point, but does not silently alter the earlier handler.
Likewise slot-46/57 host lifecycle calls must explicitly return their handles (below).

**Confirmed agreement:** visitor-rest.md's inclusive squared-distance boundary, radius 1 for both
producers, bit 2/4 writes, and particle-destructor release all match this read. The old scenery label
for bit 1 was a GUESS, not an established rule disproved by an empty search. It remains unestablished.

## 1. Where it lives; what one entry is

**READ: a fixed pool with a live linked list of circles. There is no per-tile influence array.**

`0x80050198` requests **0x240 bytes** through allocator `0x800BAB94` (`0x800501A0`).
`0x800501B8` calls pool constructor `0x80060228`, passing the string **PoolOfEffectors** at
`0x800E0C28`. `0x800501C4` stores its heap pointer at **0x80103860** (gp+0x120C).
The pointer's absolute heap value is runtime-dependent; it is not the entry base itself.

Constructor `0x80060250..26C` walks **20 entries**, beginning at pool+**0x10**, stride **0x1C**.
`0x80060270..288` independently supplies count 20, pool bytes 0x240 and stride 0x1C to the accounting
helper. Reset `0x800602D4..2E0` clears the two heads and live count; `0x800602F0..30C` puts every
entry onto the free head. The pool's dimensions are **20 objects**, not map width × height.

| Offset | Contents | Evidence |
|---|---|---|
| pool+0x04 | free-list head | `0x8005CE10`, `0x8005CF24` |
| pool+0x08 | live-list head | `0x8005CD68` |
| pool+0x0C | live count | increment `0x8005CE14..24`, decrement `0x8005CD90..DA0` |
| entry+0x00 / +0x04 | next / previous links | `0x8005CF18` / `0x8005CF0C`; setters `0x80060390` / `0x80060388` |
| entry+0x08 / +0x0C | signed halfword x/y in **whole tiles** (8-byte copied coordinate structure) | `0x800961EC..208`, `0x8008C3E0..3FC`; delta reads `0x800617E8..818` |
| entry+0x10 | unsigned 32-bit **radius squared** | `0x800961E0..E8`, `0x8008C3D4..3DC`; reader `0x8006151C..534` |
| entry+0x14 | 32-bit flag word | writes `0x800961DC`, `0x8008C3D0`; read `0x80061540` |
| entry+0x18 | unknown word; particle writes zero, entertainer leaves it alone | `0x8008C31C..324` → `0x8008C3C4..3C8`; no read in the influence query |

Coordinate halfwords at +0x0A/+0x0E are not distance axes; the predicate reads only the low signed
halfword of each copied word. For the unpleasant emitter, +0x0A carries the height supplied at
`0x8008D708..724` (terrain height from `0x80050938`, plus 0x32), copied through emitter+0x0A by
`0x8008A6C0..6C8`; the tile conversion does not shift this middle component. Person's tile getter
does not initialize that component. The unused height/padding is not modeled as influence state.

**Allocation:** `0x80053554..588` checks the free head through `0x8005CF24`; exhausted means null,
without eviction, mode check, dice, id allocation, or drawable registration. `0x8005CDC8` →
`0x8005CDE8` removes the free head, increments count and inserts at the live head. **Release:**
`0x8005358C` → `0x8005CD74` decrements count, unlinks the entry and inserts at the free head.
Both insertions use `0x80060328..384`. Thus entertainer and unpleasant emitters compete for the
same twenty slots. Pool teardown frees the block at `0x80050418..41C`.

### Reader, from guest to entry

Guest needs `0x8008FE80..E94` gates by `now & 7 == V+0x10 & 7`. The virtual call at
`0x8008FED0..ED4` uses Person slot **10** (vtable offset **0x50**), then `0x8008FED8` calls
**0x800535B4**, the implementation of `INeedsWorld.InfluenceAt`.

Slot 10 is **0x800939B0**, verified in the visitor table `0x800E367C` (function word
`0x800E36D0`) and entertainer table `0x800E41D4` (function word `0x800E4228`). Visitor
construction installs its table at `0x8008C4B4..4C4`. Its raw getter `0x80093D44..D80` reads P+0x18/+0x1A
(outer+0x20/+0x22). `0x800939C8..9EC` sign-extends each halfword and shifts it right **8 separately**.
Subtract-before-shift and sub-tile Euclidean distance are both wrong.

`0x800535C0..5DC` obtains the live head from `*(0x80103860)+8`. For each entry it calls
**0x800614D8** at `0x80053624`, fetches the flags with **0x80061540** at `0x80053634`, ORs them at
`0x8005363C`, then follows entry+0 through `0x8005CF18` at `0x80053640`. The accumulator starts
zero at `0x800535C8`. No first-match return, visibility/state/terrain test, or bit mask.

Predicate `0x800614F8` calls **0x800617E8**: subtract corresponding halfwords, store halfword
results (`0x80061804`, `0x8006181C`), then read those deltas signed (`0x80061500`, `0x80061510`).
Multiply each by itself, add low words (`0x8006152C`), and compare **unsigned distance² <= radius²**
(`sltu` then `xori 1`, `0x80061530..534`). **⚠ DO NOT FIX:** the boundary is included and deltas
wrap to signed 16 bits. A radius-1 mark covers its tile and four edge neighbors, excluding diagonals.

### Writers, from owner to entry

- **Entertainer:** `0x800959D4` allocates; `0x800959E0` writes the result, including null, to E+0x4C.
  On success, slot 10 supplies tiles (`0x80095A08..A1C`); `0x80095A28..A2C` supplies radius **1** to
  `0x800961E0`; `0x80095A34..A38` supplies flag **2** to `0x800961D8`. State **12** is entered even
  on allocation failure (`0x80095A3C..A60`). +0x18 is not initialized by this producer.
- **Unpleasant emitter:** Idle call `0x8008D720` (delay slot `0x8008D724`) uses descriptor `0x800F79D8`. Factory
  `0x80089E38..E8C` invokes descriptor+8 only after its own allocation succeeds; that word at
  `0x800F79E0` is **0x8008C2B4**. Callback allocation is `0x8008C2C4`. Getter `0x8008C46C` copies
  emitter+8/+12; `0x8008C2E8..308` performs the same signed tile conversion. The handle is written
  to emitter+0x30 by `0x8008C304` → `0x8008C458`; radius **1** at `0x8008C314..318`; +0x18 := **0**
  at `0x8008C320..324`; flag **4** at `0x8008C32C..330` → `0x8008C3CC`.

## 2. Bit 1: original search and bounded follow-up

**Follow-up:** [influence-bit1.md](influence-bit1.md) closes the ordinary effector ownership routes
with a bounded negative: **2 flag writers, supplied only 2/4; 0 bit-1 producers**. It counts every
instruction-shaped field-store candidate, scans all named executable payloads and interior entries,
traces owner/pool aliases, and injects both direct and aliased writes as controls. See that report
for the exact scope and reopening conditions. The original evidence and limits below are retained
as the record of the earlier investigation; no scenery mapping was introduced.

There is no established scenery record/radius mapping to implement. `Pleasant` stays usable by the
reader and explicit low-level fixtures, but no producer is fabricated.

The reproducible [static audit](influence-audit.json), generated by `tools/audit_influence.py`, finds
exactly the two direct **J/JAL** calls to allocator `0x80053554` listed above. Its lower allocation
helpers `0x8005CDC8` / `0x8005CDE8` each have only the wrapper caller. No aligned literal pointers
to those three functions were found in the executable. The two established flag/geometry setters
have only their known callers. Searching identical `jr ra; sw a1,0x14(a0)` leaves recovers both known
setters as a positive control, but also seven unrelated setters: matching a field offset is not
matching an object type. This search does **not** rule out computed pointers or aliased writes.

Feature constructor `0x80023C7C` initializes feature+0x7C to zero; destructor `0x80023CEC..CFC`
returns that handle if nonzero. No nonzero writer to that particular feature field was established.
Feature load `0x80023DB8..E54` restores status/stock/day, not this handle. The dormant-looking field
is evidence of an intended relationship, **not evidence that scenery currently writes bit 1**.

Feature record+0x2E has accessors at `0x80024324` (mask 8), `0x80024330` (4), `0x8002433C` (2),
and `0x80024348` (1). These are **feature flags**, not the effector's +0x14 word. shop-stock.md §8
traces their use/cleaning/bin/statistics readers; neither matching bit values nor pretty scenery names
establish an influence producer.

Record sweep accounting (minimum bounds for each actual field, **no 0x40-byte requirement**):

| Population/control | Count |
|---|---:|
| `.bin` files inspected | 690 |
| Non-record files, magic !=0x96 | 154 |
| Other record types (type 150) | 48 |
| Type-1..8 attraction records examined | **488** |
| Four-digit `NNNN.bin` subset / prefixed subset | 244 / 244 |
| Type-2 feature records examined | **182** |
| Unreadable/truncated records skipped | **0** |
| Attraction records the old 0x40 guard would skip, included here | **94** |

Feature flag histogram is `{0:130, 1:18, 2:12, 3:6, 4:8, 8:8}`. Zero **and** nonzero controls
are present, including short records. Counts describe extracted files, not 488 unique build-menu
objects: the requested four-digit subset has 244 definitions, consistent with rides.md §0. No flag
histogram is presented as proof that the influence's pleasant flag is absent.

## 3. Lifetime and sticky behavior

**The map has no tick, decay, movement callback, or per-tile clearing pass.** It queries the current
live list every time. Releasing one source cannot erase a same-bit overlapping source. Release only
changes links/count; neither it nor allocation clears the old payload (`0x8005CD74..E4C`). The new
atomic creation API always overwrites the reader's fields as both known producers do. It does not
pretend that the binary initialized fresh heap payload bytes: raw uninitialized allocation is not exposed.

**Entertainer:** a fixed snapshot at performance entry; no radius/position refresh in performance.
The direct release-call census is:

| Site | Owner path |
|---|---|
| `0x80095A98`, clears handle `0x80095AA0` | next Idle handler; even before the strike check |
| `0x80095E5C`, clears handle `0x80095E64` | pelting handler |
| `0x800960EC`, clears handle `0x800960F4` | virtual slot 46 (removal/cancel); table function word `0x800E4348`; then shared `0x8009396C` |
| `0x800962A8`, clears handle `0x800962B0` | virtual slot 57; table word `0x800E43A0`; after shared `0x800952B4` |
| `0x8008C378` | unpleasant emitter, below |
| `0x80023CFC` | feature destructor's nullable handle, above |

**⚠ DO NOT FIX:** changing state to Idle in `0x80095B90..B94` does not itself release the area;
it lasts until the Idle handler runs. Held Update is skipped (`0x80095C5C`, behaviour.md §3.2),
so merely marking an owner held is not a general map cleanup rule. An exhausted allocation is not
retried during performance. Slot 46 is dispatched during staff removal at `0x80051B44..B54`,
before the kind-specific pool return. Placement cancellation calls that remover at `0x8001E888`,
agreeing with staff.md §1. The UI callback `0x8003BEA0` calls slot 57 at `0x8003BEC4..BD4`, then
that same remover at `0x8003BED8`; its exact UI label was not established. Every higher-level
trigger of these dispatches was not traced; the entry points/removal operations are established.

**Unpleasant:** descriptor+12 (`0x800F79E4`) points at **0x8008C348**. That callback fetches
emitter+0x30, clears it (`0x8008C370..374`) and returns the area immediately (`0x8008C378`). The
map has no extra lingering bit after this callback, though other overlapping sources may remain.

The descriptor's first signed halfword at **0x800F79D8 is 0x168 = 360**. Initializer
`0x8008A700..708` copies it to emitter+0x28. Emitter Update is **0x8008A774**, called for each live
emitter at `0x80089C70`. At `0x8008A81C..850` a nonnegative descriptor lifetime enables countdown:
read **0x800BDD0C** (word **0x80103A90**, getter `0x800BDD0C..D14`), logical shift right **12**,
subtract, store a halfword, and test that signed halfword **<0**. Then call **0x8008A66C**, which
marks emitter+0x24 dead (`0x8008A674..67C`) and invokes descriptor+12 (`0x8008A680..690`).
The next manager scan physically returns the dead emitter (`0x80089C40..64`); the influence has
already gone.

**⚠ DO NOT FIX:** at timestep **4096** it survives **360 updates**, including remaining==0, and
releases on **update 361**. At timestep <4096 there is **no fractional accumulation**. Halfword
subtraction wraps; no saturation. The visibility/emission test `0x8008A788..7D4` does not gate this
countdown. These are conditional update counts, not a measured seconds lifetime. The port exposes
`UnpleasantInfluence.Tick(IInfluenceWorld)` and idempotent `Destroy()` for early host deletion.
Even an emitter whose influence allocation failed retains its lifetime, never retries, and must not
return someone else's area. The host still owns successful emitter allocation and rendering.

## 4. Port and host contract

`core/TPW.Sim/InfluenceMap.cs` owns twenty reusable `InfluenceArea` objects and the live/free lists.
It has no engine dependency. `IInfluenceWorld` supplies signed 8.8 guest/staff positions and the raw
engine timestep. Every behavior/constant has READ addresses in the code; no guessed scenery radius.

- `INeedsWorld.InfluenceAt(guest)` delegates to `map.InfluenceAt(guest, world)`.
- `IEntertainerWorld.PlaceInfluence` retains `map.TryCreateEntertainer(staff, world)` as the owner's
  nullable handle. `ReleaseInfluence` calls `map.Release(handle)` and clears the owner handle.
- Explicit lifecycle operations use that same release contract. §0 records the current pelt omission;
  adding a binary-faithful pelt release to the earlier handler is a separate reviewable correction.
- On successful creation of descriptor `0x800F79D8`, retain `map.CreateUnpleasantParticle(rawX, rawY)`.
  Call its `Tick(world)` per actual emitter update, and `Destroy()` on an earlier emitter destruction.
  The returned wrapper is lifetime/aura state, **not** a renderer or permission to spawn a particle.
- `TryCreateTiles`, `PlaceTiles`, `SetFlags`, and `AtTiles` expose the established low-level operations.
  Tests injecting pleasant flags are marked synthetic; they establish the consumer, not a producer.
- Whole-park teardown discards this component with its owners, corresponding to freeing the pool.

No `game/` file or existing behavior handler was modified. The atomic creation API and host-error
checks are managed ownership safeguards; they do not claim binary heap initialization or exception
behavior. Reusing a released handle without clearing the owner can alias a subsequent allocation,
just like the pool; callers must follow the documented ownership contract.

## 5. Verification and controls

All new test methods state what they **REJECT**. `InfluenceMapTests` has **43 test cases** (16 methods).
It exercises actual `VisitorNeeds.Tick` and `Entertainer.Tick`, not only synthetic flag reads.
Control measurements at tick 7, stagger 7, happiness 50/nausea 20:

| Treatment | Measured happiness / nausea | Matched control |
|---|---|---|
| Synthetic pleasant, two overlapping areas | 56 / 20 | outside or both released: 50 / 20 |
| Unpleasant, two overlapping areas, Idle | 49 / 22 | removed: 50 / 20; tick 6: 50 / 20 |
| Unpleasant, raw walking state 2 | 47 / 25 | removed: 50 / 20 |
| Pleasant + unpleasant, raw state 3 | 53 / 25 | removed: 50 / 20 |
| Entertainer | state 28 with exact staff target | same guest after release: stays Idle |
| Particle lifetime at timestep 4096 | area remains after 360 updates, absent after 361 | overlapping entertainer remains throughout |

`tools/mutate_influence.py` builds baseline tests once, rebuilds only mutated production, and runs
those fixed tests (so inlined constants cannot move with a mutant). It requires actual failing tests,
matching executed-test counts, and no compile failure. The unchanged-production run through that
same harness must pass; source restoration and the final full-suite run are recorded in
[influence-mutations.json](influence-mutations.json). Final result: **54/54 mutations killed,
0 survivors, 0 invalid runs**; unchanged targeted control **43/43**; baseline and restored
`dotnet test tests/TPW.Sim.Tests/` suite **1544/1544 passed, 0 skipped**. The initial 53-mutation
sweep and the additional lifetime-saturation mutation are both retained in the audit. No survivor
needed a fixture correction; a test comment was narrowed to avoid claiming to distinguish signed
versus unsigned timestep shifts whose difference vanishes after the final halfword truncation.

`python3 tools/audit_influence.py` regenerates the binary/record census and its positive/negative
controls, counting every excluded non-record and every unreadable file separately. It writes only
inside this worktree and does not invoke `recs.py`'s old size guard or its external output paths.

## 6. What was NOT established

This is the original run's gap list. The first two items have a **bounded static closure**, with
explicit alias/ownership assumptions, in [influence-bit1.md](influence-bit1.md); no dynamic
watchpoint proof is claimed by either report. The remaining gaps are unchanged.

- **Any producer for bit 1**, including which scenery records, whether any scenery produces it, and
  what its radius/lifetime would be. No producer was added.
- A dynamic proof that no indirect/computed/aliased bit-1 writer exists; this was a static trace and
  bounded census, with positive controls, not a live emulator watchpoint run.
- The unpleasant emitter's visual identity. It is not labeled vomit, rubbish, smell, or anything else.
- The purpose/readers of entry+0x18, coordinate padding, or initial heap payload contents. None
  participate in the traced influence query; only the particle's zero write to +0x18 is established.
- The absolute runtime heap address behind 0x80103860. The pool-relative layout is established.
- A complete higher-level caller census for entertainer virtual slots 46 and 57, and the exact
  UI label for the slot-57 path. Removal/cancellation through slot 46 and both slots' effector
  release instructions are established and exposed to the host.
- An end-to-end game measurement, wall-clock seconds conversion, emitter rendering, particle-pool
  integration, or game save/load reconstruction. `game/` wiring was explicitly excluded.
- Resolution of §0's inherited disagreements or the pelt-summary omission. Existing handlers and
  their earlier tests retain the findings' version pending review.
