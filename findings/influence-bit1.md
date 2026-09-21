# Influence bit 1 — bounded writer closure, SLES-026.88

**Bit 1 is not written through this build's traced, intact effector ownership routes.** There are
**2 effector flag-store instructions**, supplied only the whole-word values **4 and 2**. Neither
value contains mask `0x1`, and ORing any number of these areas cannot produce it. The +6 consumer
at `0x8008FEE4..EFC` is dead on those routes. **No attraction records qualify for a pleasant aura;
no scenery radius, lifetime, record flag, or producer was invented.** This conclusion concerns the
influence bonus, not every possible effect of using a facility.

READ: `/home/ec2-user/tpw/ext/TPW.BIN`, base `0x80010000`, SHA-256
`0b708dd74796abfedad55e55fa007ace1b5697407d36e8f7823bb06f4fa53beb`.
Companion evidence: [JSON audit](influence-bit1-audit.json),
[original influence report](influence.md), [mutation audit](influence-bit1-mutations.json).

## §0 SOURCE DISAGREEMENTS

**0 new behavioral source disagreements.** Existing findings versions stay in production;
only doc comments changed in `core/`. No behavior, enum value, test fixture in the C# suite,
or `game/` source was changed.

| Existing finding retained | Binary reading / addresses | Treatment |
|---|---|---|
| influence.md §0 / behaviour.md: performance expires after 600 ticks AND no audience | `0x80095B68..B94`: OR; release follows in Idle at `0x80095A88..AA0` | Existing AND retained; no new lifetime imposed. |
| influence.md §0 / behaviour.md: Idle roll 5 is litter | `0x8008D710..724` selects descriptor `0x800F79D8`, whose callback sets effector flag 4 | Existing litter hook retained. No reinterpretation as scenery or pleasant influence. |
| influence.md §0: retained litter-mode suppression of roll 5 | `0x8008D6B0..738` and `0x80089E38..EBC` have no such gate | Existing suppression retained. |

Also retained: the existing **1 pelt-summary omission**, `0x80095E4C..E64` removing an aura while
the prior state-handler summary omits it. It is not silently repaired here.

An **inherited terminology discrepancy** is made explicit: influence.md calls the zero at
`0x80023C7C` a feature “constructor” initialization; shop-stock.md §4 identifies its containing
entry `0x80023C3C` as **placement, virtual slot 37**. This report uses the neutral “initialization
write”; the original text and existing code are retained. Both sources agree on the zero and
nullable destructor. The old “scenery” label is expressly GUESS, so a stronger negative is new
evidence, not a conflicting established producer rule.

## 1. Every effector flag writer and its complete ordinary input domain

| Store | Callable entry | Caller and argument source | Entire flag word |
|---|---|---|---:|
| `0x8008C3D0` | `0x8008C3CC` | `0x8008C32C`; delay slot `0x8008C330` assigns `a1=4` | 4 |
| `0x800961DC` | `0x800961D8` | `0x80095A34`; delay slot `0x80095A38` assigns `a1=2` | 2 |

Both leaves are `jr ra; sw a1,20(a0)`: **replacement**, not OR. `ann.py` was used for the leaves,
wrapper argument setup and delay slots. In particular, allocator `0x80053554` starts with a GP
load **before** its stack prologue at `0x80053558`; release `0x8005358C` copies the argument and
loads the pool before its prologue at `0x80053594`. Searching prologues as callable entries would
miss real callers. The audit searches **every instruction address** in the selected regions.

Allocation `0x80053554` has **2/2 direct callers, 0 omitted**: `0x8008C2C4` and `0x800959D4`.
Lower allocators `0x8005CDC8` and `0x8005CDE8` have **1/1 caller each** (`0x80053574`,
`0x8005CDD0`). No alternate flag inputs occur in either producer. Both radius inputs remain 1
(`0x8008C318`, `0x80095A2C`); neither establishes any pleasant radius or lifetime.

### Pointer ownership: why other offsets and bulk writes matter

The negative is based on the following hand-traced ownership closure, checked against the
instruction/caller manifests. It is not inferred just from finding two identical setter leaves.

1. **Pool root:** all **6/6 GP references** to `0x80103860` are creation `0x800501C4`, deletion
   `0x80050418`, allocation `0x80053554/570`, release `0x80053590`, and query `0x800535C0`.
   Whole-image literal-pointer and straight-line constant-construction searches find **0 additional
   root references**. The latter tracks copied GP values and LUI/ADDIU/ORI aliases; its positive
   control recovers all those forms (§4). Arbitrary cross-branch computed addresses remain outside
   the stated bound (§5).
2. **Construction:** `0x80060228` visits exactly 20 entries at pool+0x10, stride 0x1C.
   `0x8006154C → 0x80062168 → 0x80060390/388` initializes only the two links. Reset
   `0x800602B8` clears header+4/+8/+12 and links all entries onto the free list.
   `0x800620F0 → 0x800BAB7C` writes allocation bookkeeping at **pool+0**, not an entry payload.
   Accounting `0x800BEC34` is a return-only leaf; `0x800BEBFC` changes GP accounting counters
   using `a1`, without storing the supplied pool pointer. Neither registers a hidden effector writer.
3. **List operations:** `0x8005CD74..CF30` and `0x80060328..398` mutate headers, entry+0/+4,
   and list-head aliases. They neither copy an entire 0x1C-byte entry nor clear/OR its flag word.
   The public allocator's returned pointer goes only to the two producers above. Release leaves
   stale payload intact; each successful producer replaces its flags before returning to ordinary
   guest updates. Fresh heap bytes and arbitrary corrupted memory are not a third producer.
4. **Query:** `0x800535B4` traverses live entries, calls `0x800614D8` and `0x80061540`, then
   follows entry+0 through `0x8005CF18`. The predicate passes **entry+8 as an input**, with a
   stack output, to `0x800617E8..844`; its partial-word stores go to that stack result. The query
   returns an integer OR, not an entry pointer. There is no escaped writable entry from this API.
5. **Payload helpers:** affine checking covers both geometry leaves, the particle's +0x18 setter,
   and both link setters: **5/5 leaves, 13/13 store instructions, 0 flag overlaps**. Geometry writes
   +8..+15 and +0x10..+0x13; unknown-word writes +0x18..+0x1B; links write +0..+7.
   The two additional flag leaves contribute the **2 actual flag writes**. This checker follows
   copied/adjusted pointers and includes SWL/SWR and return delay slots. Unsupported operations or
   unbounded destinations fail the check instead of producing an empty answer.
6. **Owner handles:** the returned pointers are retained in entertainer outer+0x4C and emitter
   outer+0x30. The entertainer reloads its handle for geometry/flags and for the four established
   releases, with corresponding zeroing. Its initialization zero is `0x80095C40`.
   Emitter initialization zeros +0x30 at `0x8008A764`. Its handle setter `0x8008C458` has **2/2
   callers** (save successful allocation and clear on destruction); getter `0x8008C460` has **1/1
   caller**, destruction. Descriptor callbacks have **2/2 actual pointer words**,
   `0x800F79E0 → 0x8008C2B4` and `0x800F79E4 → 0x8008C348`. These indirect paths were included.
7. **Feature handle:** outer+0x7C has **1 zero initialization and 1 destructor read**,
   `0x80023C7C/3CEC`; the destructor's `0x80023CFC` only releases a non-null handle. The expanded
   owner scan includes the plus-8 base alias (+0x74) and inherited methods. The three additional
   +0x74 reads (`0x80023138`, `0x80062778`, `0x8006345C`) load **vtable function words** from a
   table loaded via object+12, not the feature's handle. Feature save/load `0x80023D28..E54`
   saves/restores stock, status and stamp, not this pointer. No feature-to-effector allocation,
   nonzero assignment, flag-setting call or copy route was found in this ownership closure.

The six owner windows inspect **5,870 instruction words**, with **20 non-stack candidate
accesses**: 17 owner-handle accesses and the 3 vtable reads above. **6 stack accesses** are
explicitly excluded from owner-field classification (4 entertainer, 2 inherited Person/Staff),
**0 window words skipped**. The inherited Person/Staff window has **1,923 words and 0 handle
accesses**; an injected handle write is detected, so that zero has a positive control.

### The seven same-shaped setters are not seven more effectors

There are **9/9 identical `jr ra; sw a1,20(a0)` leaves** in TPW.BIN, not just two.
Their complete direct-call/literal-pointer census is retained in JSON, including interior-store
entry searches. Seven lie outside the traced effector pointer closure:

| Entry | Direct callers | Literal pointers | Disposition |
|---|---:|---:|---|
| `0x8001E920` | 0 | 2 (`0x800DC510/590`) | UI object's virtual setter, outside effector ownership |
| `0x80045AE0` | 2 (`0x8004436C/444C4`) | 0 | UI state copied from getter `0x800458AC`, outside effector ownership |
| `0x80046934` | 0 | 0 | Unreferenced by this direct/pointer census; no effector pointer route |
| `0x8006AF5C` | 1 (`0x8006A824`) | 0 | Record-manager initialization, random low-bit value on its own object |
| `0x80086520` | 0 | 0 | Unreferenced by this direct/pointer census; no effector pointer route |
| `0x8008C3CC` | 1 | 0 | Actual effector flag 4 |
| `0x800961D8` | 1 | 0 | Actual effector flag 2 |
| `0x8009BF58` | 1 (`0x8009B554`) | 0 | Ride-side object setup, outside effector ownership |
| `0x800B6790` | 8 | 0 | UI object setters, including static object address ending `0x4ED8` at `0x8001F654`; outside effector ownership |

The two unreferenced leaves are not assumed absent code. They are counted and excluded from the
ordinary pointer closure; any demonstrated indirect invocation with an effector pointer reopens it.

## 2. Full executable census and explicit exclusions

`fieldx.py 0x14` and `callers.py` were starting handles, not the final proof. `fieldx.py` excludes
SP/GP bases and omits SWL/SWR; the old caller helper searches selected code ranges and JAL.
The new scanner reads every aligned word, including data and library code outside those ranges,
and keeps J, JAL, conditional branches, literal pointers, byte/halfword/partial-word/coprocessor
stores. Data-shaped matches are retained as candidates, not claimed to be executable instructions.

| Payload | Aligned words examined | All store-shaped words | Stores overlapping base+0x14..17 | SP-based candidates / other bases | Words skipped / trailing bytes |
|---|---:|---:|---:|---:|---:|
| TPW.BIN | 266,327 | 29,100 | 3,239 | 2,576 / 663 | 0 / 0 |
| All 12 decoded TPW.OVL entries | 30,634 | 2,729 | 246 | 175 / 71 | 0 / 0 |
| SLES_026.88 payload | 11,776 | 1,111 | 78 | 61 / 17 | 0 / 0 |
| **Total** | **308,737** | **32,940** | **3,563** | **2,812 / 751** | **0 / 0** |

Exactly-offset-0x14 counts, for comparison with `fieldx.py`: **2,832 / 207 / 70** respectively.
There are **0 GP-based flag-offset candidates**; GP was searched, not filtered out.
The other **29,377 store-shaped words** have no byte overlap at the unadjusted flag offset.
An offset census cannot classify pointer provenance: the **3,561 candidates other than the two
flag writers** are excluded from effector classification by stack/other-object provenance in the
ownership closure, not by assuming “same offset = same type.” The alias checker separately covers
the payload helpers; its injected offset-4 write demonstrates why the raw offset search alone is
insufficient. The audit is not a whole-program typed disassembler of those 3,561 candidates.

**361/361 instruction addresses in 7/7 effector regions** are targets of the ingress scan, so
calls/branches/pointers into a store, leaf interior or pre-prologue instruction count too. The main
image has **59 matching edges: 44 internal and 15 external**, all enumerated and classified;
**0 unreviewed/missing manifest entries, 0 literal pointers**. The 15 external sites are:

```
80023CFC 800501B8 8008C2C4 8008C314 8008C320 8008C32C 8008C378 8008FED8
800959D4 80095A28 80095A34 80095A98 80095E5C 800960EC 800962A8
```

**0 additional edges/pointers/root accesses** in **12/12 overlays** and the boot payload.
Each payload has an independent injected call, interior jump, callback pointer, GP access and
flag-store control. Overlay entry 3's **11,447 words** contain **0 flag-offset candidates**; its
injected write is recovered just like those in the other 11 entries. Empty results are not used
as their own controls.

Decoded overlay bytes: **122,536**; **0 unused packed entry tails, 0 trailing decoded bytes,
0 skipped entries**. The **104-byte archive directory** is metadata, excluded as code.
The boot payload is **47,104 bytes** at header load address `0x801E0000`; its **2,048-byte
PS-X EXE header** is excluded as code. The bounded decoder is the previously tested transcription
of `0x800BFD9C..E68` in `audit_prank_reachability.py`; no external decoder CLI is invoked.
Per-image SHA-256s and per-entry counts are in JSON.

## 3. Attraction records and host contract

The existing record audit was rerun, with minimum bounds for each field: **690 files inspected**,
**154 non-records**, **48 other record types**, **488 type-1..8 records** (244 four-digit and
244 prefixed), including **182 type-2 features**. **94 short records** that the old 0x40-byte
guard would discard were included; **0 unreadable/truncated records skipped**. The feature flag
histogram remains `{0:130, 1:18, 2:12, 3:6, 4:8, 8:8}`. These are file counts, not 488 distinct
build-menu objects. The zeros and nonzeros, including short records, are control populations.

Record+0x2E bits are still the established feature-use/bin/statistics flags, with accessors
`0x80024324/330/33C/348`. They do not flow into the effector flag word in the traced build.
**Qualifying attraction records for mask 1: none within this closure.** The record census is a
coverage check, not an independent argument that a zero flag frequency proves absence.

Host integration remains the documented `InfluenceMap` contract:

- Delegate guest influence queries to `InfluenceMap.InfluenceAt`.
- Keep using `TryCreateEntertainer` and the established explicit particle API/lifecycle contract.
- `TryCreateTiles(..., TileInfluence.Pleasant)` is a synthetic/manual injection API; it faithfully
  exercises the +6 consumer. It is not authorization to derive pleasant influence from scenery
  type, feature+0x2E, attractiveness, placement, name, or footprint.
- There is **no new scenery interface to wire**, and no pleasant radius/lifetime to supply.

The consumer and synthetic pleasant tests remain intact. Deleting the +6 branch because it is
unproduced would stop being a 1:1 port of the reader. No simulation behavior was added or removed.

## 4. Positive controls, tests and mutation sweep

All mutations of evidence are in memory; the executable, overlays, boot image and other worktrees
are read only. The controls actually alter bytes supplied to the same readers:

| Control | Original | Injected detection |
|---|---|---|
| Change particle caller's delay literal to 1 | 0 bit-1 writers, domain `{4,2}` | Exactly 1 bit-1 writer |
| Change entertainer caller's delay literal to 1 | 0 bit-1 writers | Exactly 1 bit-1 writer |
| New writer in geometry: `t0=entry+16; a1=1; sw a1,4(t0)` | 0 flag overlaps in that leaf | Exactly 1 new flag write, value 1, bytes +20..23 |
| Construct absolute pool address, use copied GP, construct setter address | 0 non-GP pool/constructed setter references in original census | 4 matching reference events from 7 synthetic instructions |
| Inject a Person/Staff inherited +0x44 write | 0 owner-field accesses in 1,923 words | Exactly 1 access |
| Inject calls/pointer/root/direct flag write into each executable image | 14 nonempty images | **70/70 checks pass**: 5 checks per image; 84 injected instruction/pointer words |

Total: **5/5 domain/alias control groups plus 14/14 image control groups, 0 skipped**.
The real descriptor's **2 callback pointer words** and the **2 actual producer calls/writes**
provide additional non-injected controls.

`tools/test_bit1_audit.py`: **23 tests**, each with a REJECTS comment, covering instruction widths,
delay slots, pointer aliases, unknown-input rejection, nonempty manifests, and actual image counts.
`tools/mutate_bit1_audit.py`: **30/30 audit mutations killed, 0 survivors, 0 invalid final runs**;
unchanged-source controls **23/23 before and after**, **0 skipped tests** in every final run.
Fixed tests run against in-memory tool mutations; compile errors, test errors, skips and missing
tests are invalid runs, never kills. Unchanged-source controls run before and after.

The initial sweep had **28 kills, 0 survivors, 2 invalid runs**. Both invalid runs exposed the
same fixture weakness: the jump-region boundary test indexed an empty result before asserting its
size. It now compares the entire expected one-element list. The rejected run is retained in JSON.
No actual audit-tool mutant survived. These are **audit mutations, 0 new simulation mutations**;
the original influence implementation's 54-mutation evidence remains in influence.md.

`dotnet test tests/TPW.Sim.Tests/`: **1,574 passed, 0 failed, 0 skipped**. Its two existing analyzer
warnings remain (`BootSequenceTests` xUnit2000, `VisitorConditionTests` xUnit1026).
The reused overlay decoder's **18/18 reachability-audit tests** also pass. Final command results,
source hashes and unchanged `game/` checks are recorded in
[influence-bit1-validation.json](influence-bit1-validation.json).

```sh
PYTHONDONTWRITEBYTECODE=1 python3 tools/audit_bit1.py
PYTHONDONTWRITEBYTECODE=1 python3 tools/test_bit1_audit.py
PYTHONDONTWRITEBYTECODE=1 python3 tools/mutate_bit1_audit.py
PYTHONDONTWRITEBYTECODE=1 python3 tools/audit_influence.py
dotnet test tests/TPW.Sim.Tests/
```

## 5. Exact bound and what reopens it

This is a **static, build-specific negative for the enumerated intact ownership and entry routes**.
It includes the main image, all named overlays, boot payload, ordinary callback pointers, direct
and interior entry branches, straight-line computed-address constructions, plus the established
pool/list/owner aliases. It assumes correctly owned heap objects and ordinary single-threaded
game updates after producer initialization. The call/owner manifests and pointer interpretation
were read by hand; hashes and census tests bind that argument to these bytes. The tools do not
formally solve arbitrary whole-program aliasing or all possible constructed indirect addresses.

Reopen on any of the following concrete evidence:

- An additional instruction that can receive an effector pointer and write bytes +0x14..17,
  including an adjusted pointer, partial store, bulk copy or different incoming flag argument.
- A nonzero feature+0x7C writer, another pool-root access/escape, or an unlisted allocator,
  free/live-head or owner-handle reader that supplies such a writer.
- A synthesized indirect target crossing the scanned straight-line boundary, a new executable
  payload, or modified image/data/save bytes that invalidate the ordinary ownership argument.
- A live retail trace/watchpoint showing **mask 1 in a fully initialized live entry** or the +6
  consumer reached through influence. That would identify a missing route; no emulator trace was
  performed in this run.

Uninitialized heap contents, corrupted/malformed/foreign saves and arbitrary memory corruption
are outside the claim. No scenery aura identity, pleasant record flag, radius or lifetime was
established, because there is no producer in the bounded graph. The practical host answer is to
leave scenery disconnected from **this +6 influence path**.
