# Prank hunt CLOSED: IDs 117–119 are unreachable in this build

**Outcome 2. All seven computed-ID paths are now bounded. None can submit 117, 118 or
119 during normal execution of this unmodified PAL build, including loading saves produced
by the build. The 45 fixed setter sites also exclude them. No computed path was left unbounded:
7/7 settled, 0 skipped.** The three named notifications are retained, unreachable message data
in this posting graph. There is no evidenced prank producer to port.

This closes the outstanding work in [pranks.md](pranks.md) and [stinkbombs.md](stinkbombs.md).
Those reports remain intact as historical evidence. This conclusion is about the three named
advisor events; it does not rename an unrelated litter sprite or claim that caption text proves
an otherwise silent mechanic. No simulation component or host interface was invented.

READ: TPW.BIN at `0x80010000`, SHA-256
`0b708dd74796abfedad55e55fa007ace1b5697407d36e8f7823bb06f4fa53beb`, FOLIO entries 1/2,
all 12 TPW.OVL entries, and the boot payload. The audit records source hashes, every table entry,
every decoded VM instruction, and instruction-region hashes. This is a static domain proof using
hand-traced callers and data, not a whole-machine formal verification or a live emulator result.
The explicit assumptions and evidence that could change the answer are in §6.

## §0 SOURCE DISAGREEMENTS

**0 new contradictions of existing findings; 0 production files changed.** The two formerly
unbounded paths are resolved by tracing their input lifecycles, not by adding bounds the binary
does not have. In particular, the replay loop still has no upper-bound test.

| Existing version retained | Reading / addresses | Treatment |
|---|---|---|
| pranks.md §0: valid-ID port guard; binary can submit default 290 | `0x8001411C..20` initializes 290. The 14 targets at `0x800DBE10` include six direct jumps to submission `0x80017940`, bypassing the setter. Out-of-range switch indices do the same at `0x800178B8`. | Confirmed, not corrected. The table's complete mapping is below. The second previously recorded default route at `0x8009BFE8` remains covered by the prior handoff. |
| stinkbombs.md §0 / pranks.md §0: Idle roll 5 is retained as litter, with existing mode suppression and OR nausea condition; inclusive litter radius and retained cadence | Inherited discrepancies at `0x8008D710..724`, `0x800514F0..50C`, `0x8008D6B0..738`, `0x8008D5CC..5F8`, `0x800901B8..1F8`, `0x80090128..13C`. | All five retained; 0 reinterpreted or changed in production. These are not evidence of a named prank producer. |

Accounting: **1 inherited port/binary boundary discrepancy confirmed, 5 inherited behavior
disagreements retained, 0 new source disagreements.** The historical statement that replay/VM
input domains were unresolved was true of the previous investigation; the additional proof here
is not a conflicting binary reading.

## 1. Start at `0x800DBE10`: it is a jump table, not a message-ID array

`0x800178B4` checks unsigned `s0 < 14`; `0x800178C0..D4` indexes the table by `4*s0`
and jumps to its code pointer. `s0` is formed from the two getters at `0x8001782C/34`
as `(first << 2) | second`; the guard suffices for **every** 32-bit result, without needing
to assume the getters' ranges. All outside values leave the message builder's default 290.

| Index | Pointer read from `0x800DBE10 + 4*index` | Submitted ID |
|---:|---|---:|
| 0 | `0x800178DC` | 221 |
| 1 | `0x800178E8` | 222 |
| 2 | `0x80017940` | 290 |
| 3 | `0x80017940` | 290 |
| 4 | `0x800178F4` | 223 |
| 5 | `0x80017900` | 224 |
| 6 | `0x80017940` | 290 |
| 7 | `0x80017940` | 290 |
| 8 | `0x8001790C` | 225 |
| 9 | `0x80017918` | 226 |
| 10 | `0x80017940` | 290 |
| 11 | `0x80017940` | 290 |
| 12 | `0x80017924` | 227 |
| 13 | `0x80017930` | 228 |

The literals in J delay slots are part of those arms. The setter is `0x80017938`; the
shared submit is `0x80017940`. The separate mode branch sets 288 at `0x80017870`.
**Result: table arm domain `{221..228, 290}`, no prank ID; 14/14 entries read, 0 skipped.**
Positive control: changing the first arm's delay-slot literal at `0x800178E4` to 117 in an
in-memory image makes the table audit report 117. Original: 0 prank IDs; injected: exactly 117;
0 controls skipped.

## 2. The seven paths, settled

The domains below are safe upper bounds at the setter: allowing every condition to succeed
can overestimate reachability, but cannot hide a prank ID. Exact decoded sets are in the JSON.

| Setter call | Where the ID comes from and why the domain is bounded | ID domain | Prank reachability / accounting |
|---|---|---|---|
| `0x800140EC` | HUD kind-3 card caption; its source ID is 221..288. The matching caption exists within those rows before the loop could escape or wrap (§3). | 40 caption-bearing tutorial IDs, all 221..288; 41 IDs in the conservative calculation including the suppressed sentinel | **No**; 68/68 table rows, 0 skipped; injected earlier-start/matching-caption control finds 117 |
| `0x80017198` | Operand of opcode 7 in global FOLIO entry 2, selected by the only VM caller using entry 1's 125 schedule rows (§4) | 107 distinct IDs, enumerated below | **No**; 125/125 programs, 1,802/1,802 halfwords, 107/107 posts, 0 skipped or unreferenced; separate injected operands find each of 117/118/119 exactly once |
| `0x800677DC` | Incoming `a1` saved at `0x800677CC`, masked at `0x800677E0`; all 9 wrapper callers set it in their call delay slot | 175..182, 188 | **No**; 9/9 callers, 0 skipped; injected first caller literal finds 117 |
| `0x80067F28` | Signed byte `0x80102E68[kind-1] + 50`; loop starts at 1 (`0x80067E28`), increments once per iteration, continues only below 6 (`0x80067F54..5C`) | 50..54 | **No**; 5/5 bytes, 0 skipped; changing the first byte to 67 makes 117 |
| `0x8006813C` | Same five-byte permutation, plus six switch-arm additions from table `0x800E159C`; only caller `0x80067F4C` passes the bounded loop kind | 30..44, 50..54 | **No**; 5/5 bytes and 6/6 arms, 0 skipped; changing the first byte to 87 makes 117 in the +30 arm |
| `0x8006819C` | Same bounded caller and permutation, plus 45 at `0x80068198` | 45..49 | **No**; 5/5 bytes, 0 skipped; changing the first byte to 72 makes 117 |
| `0x8009C7D0` | Return value of `0x80014288`, forwarded in the setter's delay slot at `0x8009C7D4`; all return exits assign one of four literals | 63, 64, 65 | **No**; 24/24 instruction words (including NOPs), 4/4 return sources, 0 skipped; injecting 117 at `0x8001429C` is detected |

**Total: 7/7 computed paths bounded, 0 skipped, 0 unbounded paths.** The positive controls mutate
copies of evidence, not the original executable or the simulation.

The staff permutation really is **`3,0,2,4,1`**, read as a signed byte by the `lbu` then
`sll/sra 24` sequences. At `0x80067FC0..E0`, the second jump table has an unsigned `<6` guard:

| State | `0x800E159C` target | Addend instruction | Addend |
|---:|---|---|---:|
| 0 | `0x80067FE8` | `0x80068018` | 30 |
| 1 | `0x8006801C` | `0x80068054` | 35 |
| 2 | `0x80068058` | `0x80068090` | 40 |
| 3 | `0x80068094` | `0x800680CC` | 40 |
| 4 | `0x800680D0` | `0x80068108` | 40 |
| 5 | `0x8006810C` | `0x80068138` | 50 |

State >=6 returns without posting; no unexamined table tail can provide another addend.
Accounting: **6/6 pointers and addends read, 0 skipped**. The byte-table controls in the main
table show that the audit reads these values rather than substituting an assumed permutation.

The award wrapper's nine source calls remain `0x800679A4`, `0x80067A00`, `0x80067A8C`,
`0x80067B1C`, `0x80067B5C`, `0x80067BAC`, `0x80067C08`, `0x80067C48`, `0x80067CA4`.
The breakdown selector's four return literals are `0x8001429C/B0/C0/D4` (63/64/65/64).
These source sets have **9/9 and 4/4 entries respectively, 0 skipped**.

## 3. Why caption replay cannot run past the table during ordinary use

The missing argument was the **provenance of the card**, not a nonexistent check in the loop.

1. Queue insertion `0x80013DB0..DE4` and interrupt insertion `0x80013CD4..D08` assign kind 2
   when a payload is present, otherwise kind 3 **only** when
   `((id - 221) & 0xFFFF) < 68`; all other IDs get kind 0. For a stored u16 ID, that condition
   is exactly 221..288. Default 290 cannot become kind 3.
2. Showing the message gets its caption from `0x800EE4FC + 20*id` at `0x80013F2C..44`.
   `0x80013F48..4C` suppresses text 292. The formatter at `0x800141BC` passes through
   the caption and kind; setters `0x80014280/78` write card+`0x108` and card+0.
3. The only direct card-append entry `0x8003A484` is reached through `0x800385AC`.
   All **3/3 callers, 0 skipped**, were traced: advisor show `0x80013F80`, custom notices
   `0x800676C4`, and save restoration `0x80072B0C`. Custom notices explicitly use kind 4
   (`0x800676AC` → `0x800693F0`). The appender copies the entire 0x120-byte record;
   list compaction at `0x8003B974` copies kind and caption without changing their relationship.
4. Save writer `0x800719CC` writes the existing caption (`0x80071A98..B0`) and kind
   (`0x80071AFC..B14`), excluding kind 4. Getter leaves `0x80071C9C` and `0x80071CA8`
   read card+0 and card+`0x108`. Loader `0x800729A0` restores that pair via
   `0x80072E90` and the two setters `0x80072F60/68`. Loading a save produced by the game
   therefore preserves the invariant. It does not create an unscheduled tutorial caption.
   **1/1 writer and 1/1 reader traced, 0 skipped.** A deliberately malformed/foreign save
   is a separate input domain: the loader does not validate kind against caption.
5. Replay's only caller is `0x8003B950`. Its branch at `0x8003B8A8..AC` requires kind 3,
   then passes the saved caption from `0x8003B94C`. Replay starts at row 221 and increments
   the row pointer by 20 and the index by 1 (`0x800140A0..C4`). The originating row supplies
   a finite match by row 288 at the latest. An earlier duplicate would only return an earlier
   tutorial row. The final `&0xFFFF` at `0x800140F0` cannot wrap this bounded index into 117.

All **68/68 rows** were read, **0 skipped**. **28** have suppressed caption 292; the remaining
**40** have distinct captions and map back to themselves. Exact generated-card replay IDs:

```
221..236, 239..245, 247..248, 250, 257..258, 267, 269..270,
273, 276, 278..280, 282..284, 288
```

The conservative lookup of all 68 rows also returns 237 for the repeated sentinel, so even
that larger set is safe. **0 prank IDs in either set; 0 rows skipped.** Positive control changes
the replay start/index offsets to 117 and puts a matching tutorial caption in row 117; the
same reader then returns 117. A caption-only mutation cannot make the actual loop return an
ordinal before its starting row. The test suite also rejects missing in-table termination
witnesses rather than reporting them as a negative.

## 4. Why the VM has no unscheduled source in this build's caller graph

The single VM call is `0x80016994`; **1/1 direct caller, 0 skipped, 0 literal callbacks** in
the main image, with no additional entry in the overlays or boot image (§5, including controls).

The statistics constructor `0x80016718` loads **global FOLIO entry 1** at `0x8001675C..84`
and **entry 2** at `0x80016778..90`. Entry numbers are read from those instructions by the audit.
The loader `0x800BBE78` uses `folio.gaz`; there is no park-selected rule archive on this route.
Entry 1 is **1,500 bytes**, giving **125** 12-byte scheduling rows (`0x80016794..B0`).
Entry 2 is **3,604 bytes**, or **1,802 signed halfwords**. **2/2 relevant entries decoded,
0 truncated entries or skipped bytes. FOLIO's header declares 422 entries; the other 420 are
excluded from VM decoding because this constructor does not select them.**

The rule cursor starts at zero (`0x80016768`), selects `schedule + 12*cursor`
(`0x80016930..44`), and advances modulo the count at `0x800169F4..0x80016A34`.
The first halfword of the selected record is sign-extended and doubled, then added to the
entry-2 data pointer before the VM call (`0x80016968..98`). Every one of those 125 starts
was decoded through its terminating zero. No hidden/unreferenced halfwords remain.

The instruction table at **`0x800DBB18`** contains these nine targets in order:

```
80017078, 800170A4, 800170D0, 800170F4, 80017124,
8001714C, 80017184, 800171BC, 800171DC
```

The dispatch uses `opcode-1` with `<9`, so these are opcodes 1..9. Post is opcode **7**,
not opcode 8. Conditionals return or continue at the next instruction; none jumps into a
different operand/program. The decoder allows all conditions to succeed, preserving an upper
bound even for predicates that might not coexist in a real park. **9/9 targets and all interpreter
instruction words read, 0 skipped.**

There is also no script self-modification on this source set: all **50/50 write instructions**
are opcode-6 assignments to slots **51..54, 56..70**: 49 write zero; rule 124 at word 1798
writes **2001** to slot 59. There are **0 opcode-5 instructions**, measured by the
full decoder with an opcode-5 test control. The stores at `0x8001716C/80` target the statistics
cache and its 20 backing counters, ending at object+`0xB6`. They cannot reach cursor+`0xBA`,
schedule handle+`0xC0`, count+`0xC4`, code handle+`0xC8`, or either loaded blob. Initialization
and scheduling only change the scheduling dates at record+2 and +8, leaving start+0 intact.
The hypothetical opcode-6 slot-71 write discussed in statistics.md is absent from these programs;
the new audit explicitly rejects it as a scheduler-state write if injected.

The **107 posts have 107 distinct IDs**:

```
0..29, 55..61, 66..85, 91..93, 95..99, 107, 109, 111, 113, 115..116,
122..127, 132..133, 146..152, 154..173, 190
```

**No 117/118/119; 125/125 programs, 662/662 non-End instructions, 1,802/1,802 halfwords,
107/107 posts, 0 skipped, 0 unreferenced.** Real positive control: rule 0 posts 0 at word 11.
Three additional controls replace just that operand with 117, 118 or 119; each produces
exactly one corresponding hit. This closes the previous “unscheduled/runtime-modified VM
input” gap for intact execution of the traced constructor and scheduler.

## 5. Census and controls: no excluded named executable remains

The expanded target census covers **25 entry points**, including the queue/interrupt route,
true submit entry `0x80014144`, builder/setter, replay, VM/constructor, staff wrapper, and
card creation/restoration wrappers. `fn.py`'s interior submit prologue `0x8001414C` is retained
only as a trap control, never mistaken for the callable entry. `ann.py` was used for the
argument setup, small leaves and delay slots.

| Input | Examined | Skipped / excluded | Result and positive control |
|---|---:|---|---|
| TPW.BIN | 266,327 aligned words, including data/outside helper code ranges | 0 words skipped, 0 trailing bytes | 52 submit and 52 setter calls; 45 fixed sites with 71 literal sources still exclude pranks. 0 fixed sites skipped. Injected 117 at `0x800622B8` finds exactly one hit. No literal pointers to the 25 targets; independent real callback control finds `0x800F79E0 → 0x8008C2B4`. |
| TPW.OVL | 12/12 entries; 122,536 decoded bytes / 30,634 words | 0 entries/words skipped, 0 undecoded packed entry tails, 0 decoded trailing bytes; 104-byte directory is metadata | 0 additional calls/pointers to the 25 targets. All 12 injected copies recover one VM call and one pointer each. Real control recovers 28 string-lookup calls across entries 1/2/6/10/11. |
| SLES_026.88 | Entire 47,104-byte payload at header load address `0x801E0000`; 11,776 words | 0 payload words skipped; 2,048-byte PS-X EXE header excluded as code | 0 additional calls/pointers. Real control: `0x801E001C → 0x801E3A88`. Injected copy recovers one submit call and one pointer. |

The new bounded overlay decoder was transcribed from `0x800BFD9C..E68`; it cannot run past
an entry into its neighbor to hide truncation. It consumes every packed entry exactly and
reproduces **12/12 previous decoded SHA-256 hashes, 0 mismatches or skipped comparisons**.
Tests distinguish literal, short, ordinary and extended copies, including overlapping copies
and malformed back references. This adds independent implementation and fixture checks to the
prior decoder dependency; it is not a hardware execution comparison.

Call and literal-pointer scans are syntactic. They are not an assertion that every matching
word is executable, nor a general proof about manufactured indirect addresses. Every zero
reported by these scans has both nonempty control evidence and injected detections. The
domain proof depends on the specifically traced callers and ordinary object/data lifecycles.

## 6. What would change the answer

**None of the seven computed paths defeated this investigation.** All their original unknown
input domains now have finite, build-specific bounds. No TPW.OVL entry, boot payload, relevant
VM program or referenced ID table was left unread: **0 excluded named executable payloads,
0 undecoded relevant tables, 0 unbounded computed paths**.

The closure applies to the shipped image/data with intact memory and saves made by this build.
Evidence outside that scope could change it:

- A modified, malformed or foreign save can supply kind 3 with an arbitrary caption at
  `0x800729A0`. That bypasses the normal card invariant. The unchecked replay loop may then
  escape its table; no safe bound or intended prank behavior is claimed for such input.
- A different FOLIO entry-1/2 payload, executable patch, or actual write corrupting their handles,
  program starts or instructions invalidates the VM argument. Likewise, changing the staff
  permutation or code literals changes those finite domains, as the controls demonstrate.
- An additional executable asset/overlay or a demonstrated synthesized indirect entry/aliased
  queue write outside the enumerated posting graph would require a new trace. **All 12 named
  overlays and the boot payload were included here; none is being deferred.** This audit does
  excludes the other **420 FOLIO entries** from hypothetical executable/VM decoding.
- A live retail trace connecting an actor to one of these named events would contradict the
  stated closure and identify the concrete missing route. No live run was performed here.

The practical answer is settled for this build's ordinary posting routes: **the prank producer
is not reachable; the three captions do not justify a prankster class, dropped object, or
handyman implementation.** Existing litter and influence behavior stays as previously documented.

## 7. Reproduction, tests and mutations

- [audit_prank_reachability.py](../tools/audit_prank_reachability.py) writes
  [prank-reachability-audit.json](prank-reachability-audit.json): all seven ID domains, exact table
  mappings/programs, 10 domain positive controls, fixed-source control, all executable census
  counts, and per-overlay/boot call-and-pointer injections. **0 skipped controls.**
- [test_prank_reachability.py](../tools/test_prank_reachability.py): **18 tests**, each says what
  it REJECTS. These test evidence readers and domain calculations, not prank simulation.
- [mutate_prank_reachability.py](../tools/mutate_prank_reachability.py) writes
  [prank-reachability-mutations.json](prank-reachability-mutations.json): **24/24 killed,
  0 survivors, 0 invalid final runs, 0 skipped tests**; unchanged-source controls **18/18 before
  and after**. Fixed tests and in-memory mutations; source files remain unchanged by the runner.
- Initial rejected sweep: **22 kills, 0 survivors, 2 invalid runs**, retained in JSON history.
  Removing the final caption row raised an uncaught error on a valid fixture; valid-input
  rejection is now an assertion failure. A short-copy distance mutant exposed an uncaught
  `IndexError` in the malformed-input test; the test now asserts explicit `ValueError` rejection.
  A new `ABAB` fixture distinguishes distance 2 from 1 where repeated `A` could not. An earlier
  baseline fixture's consumed-byte expectation was corrected from 9 to its actual 10 encoded
  bytes; decoded output was already correct. No exception was counted as a mutation kill.
- Existing caller and stinkbomb audit suites: **10 + 8 passed, 0 failed, 0 skipped**.
- `dotnet test tests/TPW.Sim.Tests/`: **1,574 passed, 0 failed, 0 skipped**. Two existing analyzer
  warnings remain (`BootSequenceTests` xUnit2000 and `VisitorConditionTests` xUnit1026).
  [prank-reachability-validation.json](prank-reachability-validation.json) records validation.

**Mutation scope: 24 audit-tool mutations, 0 simulation mutations, because no simulation behavior
was added.** The tests do not purport to mutate every hand-read MIPS instruction or prove every
possible aliased write. `core/`, `tests/TPW.Sim.Tests/` sources, and `game/` are unchanged.

```sh
PYTHONDONTWRITEBYTECODE=1 python3 tools/audit_prank_reachability.py
PYTHONDONTWRITEBYTECODE=1 python3 tools/test_prank_reachability.py
PYTHONDONTWRITEBYTECODE=1 python3 tools/mutate_prank_reachability.py
PYTHONDONTWRITEBYTECODE=1 python3 tools/test_prank_callers.py
PYTHONDONTWRITEBYTECODE=1 python3 tools/test_stinkbomb_audit.py
dotnet test tests/TPW.Sim.Tests/
```
