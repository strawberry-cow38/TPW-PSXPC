# Prank queue producers: NOT ESTABLISHED — SLES-026.88

**I could not establish what queues message 117. The requested stop condition applies.**
No prank simulation, actor, object identity, or host interface was invented. `core/` and `game/`
are unchanged. This report follows the queue's callers, extending the negative handoff in
[stinkbombs.md](stinkbombs.md); adjacency of three captions does not establish one producer.

READ = instructions/data in TPW.BIN, loaded at `0x80010000`, SHA-256
`0b708dd74796abfedad55e55fa007ace1b5697407d36e8f7823bb06f4fa53beb`, or the named FOLIO entries.
The overlay scan uses the existing decoder of `0x800BFD9C`, in memory; its source and all decoded
images are hashed in [pranks-audit.json](pranks-audit.json). No live retail measurement was made.

## §0 SOURCE DISAGREEMENTS

**No new contradiction of an existing findings claim within the 289 valid advisor IDs was
established.** One boundary discrepancy with the existing port was exposed; its precise scope is
recorded below rather than presented as a correction to the findings. Existing code stays intact.

| Existing version retained | Binary reading, with addresses | Treatment |
|---|---|---|
| advisor.md §2 declares 289 records; §3 describes the ordinary queue as IDs below 221. `ParkAdvisor.Post` additionally rejects IDs outside that table through `AdvisorMessages.Has`. | `0x80013C38..48` tests unsigned `(id - 221) < 68`, with no preceding table-range rejection. With queue flag set, other IDs route to `0x80013D30` at `0x80013C54`. Builder `0x8001411C..20` initializes ID **290**; paths at `0x800178B8`/table `0x800DBE10` and `0x8009BFE8` can submit that default without using the setter. | Retain the findings' valid-ID model and the port's guard. This is a port/binary boundary mismatch, **not proof that the findings say invalid IDs are rejected**, and not a prank producer. No invalid-ID behavior ported. |
| behaviour.md §2.1 calls Idle roll 5 litter; retained in litter.md §0 and stinkbombs.md §0. | The handoff records descriptor `0x800F79D8` allocated through `0x80089E38` at `0x8008D710..724`, with effector bit 4 initialized at `0x8008C314..330`. | Inherited disagreement, not a new discovery. Keep `VisitorIdle.DropLitter`; no stinkbomb alias. |
| behaviour.md §1/§4 mode suppression and §2.1 nausea OR/random condition; behaviour.md §2.9 nearby-litter boundary/cadence. | stinkbombs.md §0 preserves the discrepancies at `0x800514F0..50C`, `0x8008D6B0..738`, `0x8008D5CC..5F8`, `0x800901B8..1F8`, and `0x80090128..13C`. | Inherited, not re-audited here. All existing versions remain in code. |

Accounting: **0 existing production files changed, 0 new findings contradictions established,
1 port/binary boundary discrepancy recorded, 5 inherited disagreements left unresolved**.

## 1. Follow the actual submission route

READ, checked with `ann.py` to avoid function-extent errors:

```
0x80014118: message builder; default id 290, payload-present byte 0
0x8001412C: id setter (sh a1,0(a0) at 0x80014130)
0x80014144: submit; a1 := message; a0 := [0x8010265C]
0x80014154: call 0x80013C1C
0x80013C54: call 0x80013D30 (ordinary queue)
0x80013C68: call 0x80013C80 (interrupt)
```

`fn.py` starts the submit wrapper at its stack prologue **0x8001414C**, losing the argument setup
at **0x80014144..48**. Searching callers of the prologue gives zero; callers of the actual entry
give **52**. Similarly, `fn.py 80013D30` includes the preceding interrupt function, and
`fn.py 80053768` reports the preceding function ending at the requested address. Those are helper
extent traps, not proof of absent functions.

| Target | Direct J/JAL matches | Literal pointer matches |
|---|---:|---:|
| queue `0x80013D30` | 1: `0x80013C54` | 0 |
| route `0x80013C1C` | 1: `0x80014154` | 0 |
| submit `0x80014144` | 52 | 0 |
| interior prologue `0x8001414C` | 0 | 0 |
| setter `0x8001412C` | 52 | 0 |
| builder `0x80014118` | 62 | 0 |
| caption replay `0x8001408C` | 1: `0x8003B950` | 0 |
| award-message wrapper `0x800677B8` | 9 | 0 |
| breakdown-message selector `0x80014288` | 1: `0x8009C7C4` | 0 |

**Accounting for every row:** **266,327/266,327 aligned TPW.BIN words**, **0 skipped**, **0 trailing
bytes**. Of those, 187,686 are in `callers.py`'s code ranges and 78,641 are outside but still scanned.
The original helper omits those 78,641 from instruction scanning and its pointer loop omits the last
aligned word; the new audit includes both. These are syntactic matches, not arbitrary dataflow proof.
Positive controls recover the queue edge and the independently known callback pointer
`0x800F79E0 → 0x8008C2B4`; that callback has zero direct calls in the handoff.

## 2. All seven computed setter paths

| Setter call | Where the ID comes from | Bounded result and limitation |
|---|---|---|
| `0x800140EC` | `0x8001408C..C4` walks captions starting at row 221; `0x800140F0` passes the index. Called by kind-3 HUD card replay at `0x8003B944..950`. | Normal tutorial rows are 221..288, not prank rows. **The search has no upper bound**; arbitrary input, out-of-table traversal, and eventual halfword wrap are not proved impossible. |
| `0x80017198` | VM operand loaded at `0x80017184`; masked at `0x8001719C`. | All **125 scheduled programs**, **662 non-End instructions**, **1,802 halfwords**, **107 posts** decoded: none post 117/118/119. **0 skipped programs, 0 unreferenced halfwords**. Positive control: rule 0, word 11, posts 0. Unscheduled/runtime-modified input remains unresolved. |
| `0x800677DC` | Wrapper preserves incoming a1 at `0x800677CC`, masks it at `0x800677E0`. | **9/9 callers, 0 skipped**, pass 175,176,177,188,178,179,180,181,182 at `0x800679A4`, `0x80067A00`, `0x80067A8C`, `0x80067B1C`, `0x80067B5C`, `0x80067BAC`, `0x80067C08`, `0x80067C48`, `0x80067CA4`. No prank ID. |
| `0x80067F28` | Byte permutation at `0x80102E68` is **3,0,2,4,1**; add 50 at `0x80067F24`. | Produces 50..54 for staff kinds 1..5, bounded by `0x80067E28..38` and `0x80067F54..5C`. **5 bytes read, 0 skipped**. |
| `0x8006813C` | Same permutation, branches add 30/35/40/40/40/50 at `0x80068018/54/90/CC`, `0x80068108/38`. | Produces 30..44 or 50..54. **6 switch arms read, 0 skipped**. Only direct caller of containing `0x80067F84` is `0x80067F4C`, within that bounded staff loop; 0 literal pointers. |
| `0x8006819C` | Same permutation +45 at `0x80068198`. | Produces 45..49 for that caller/domain. **5 bytes read, 0 skipped**. No proof against arbitrary memory corruption/aliasing. |
| `0x8009C7D0` | Return from `0x80014288`, passed by `0x8009C7D4`. | Selector loads only **63,64,65** at `0x8001429C/B0/C0/D4`. **Entire 0x60-byte function read, 0 instructions skipped** (disassembler suppresses NOP display). |

Accounting: **7/7 computed paths reviewed, 0 skipped**. Two have explicitly unresolved general input
domains (caption replay and VM); the other five are bounded through the observed callers/data.
“Reviewed” does not mean every arbitrary indirect invocation or aliased write has been excluded.

## 3. The 45 fixed setter sites

**45/45 sites, 71 literal source instructions, 0 skipped.** None supplies 117/118/119.
The complete address/ID table follows. Branch alternatives and call delay slots are included.
Two submit paths may bypass a setter with default 290 (§0); they were not mistaken for prank IDs.
The tool validates the hand-read manifest's coverage and instructions, **not the entire control-flow
graph**. Source lists are therefore reviewable evidence rather than a claim of formal verification.

| Setter call | IDs | Literal source instruction addresses |
|---|---|---|
| `0x80013264` | 189, 206 | `0x8001325C`, `0x80013260` |
| `0x8001786C` | 288 | `0x80017870` |
| `0x80017938` | 221, 222, 223, 224, 225, 226, 227, 228 | `0x800178E4`, `0x800178F0`, `0x800178FC`, `0x80017908`, `0x80017914`, `0x80017920`, `0x8001792C`, `0x80017934` |
| `0x800179AC` | 230 | `0x800179B0` |
| `0x80017A0C` | 237 | `0x80017A10` |
| `0x80017A6C` | 238 | `0x80017A70` |
| `0x80017ACC` | 239 | `0x80017AD0` |
| `0x80017B2C` | 241 | `0x80017B30` |
| `0x80017B8C` | 244 | `0x80017B90` |
| `0x80017BEC` | 247 | `0x80017BF0` |
| `0x80017C4C` | 250 | `0x80017C50` |
| `0x80017CAC` | 253 | `0x80017CB0` |
| `0x80017D0C` | 257 | `0x80017D10` |
| `0x80017D6C` | 261 | `0x80017D70` |
| `0x80017DCC` | 267 | `0x80017DD0` |
| `0x80017E2C` | 270 | `0x80017E30` |
| `0x80017E8C` | 274 | `0x80017E90` |
| `0x80017EEC` | 275 | `0x80017EF0` |
| `0x80017F4C` | 276 | `0x80017F50` |
| `0x80017FAC` | 278 | `0x80017FB0` |
| `0x8001800C` | 279 | `0x80018010` |
| `0x8001806C` | 280 | `0x80018070` |
| `0x800180CC` | 281 | `0x800180D0` |
| `0x8001812C` | 283 | `0x80018130` |
| `0x8001818C` | 284 | `0x80018190` |
| `0x800181EC` | 269 | `0x800181F0` |
| `0x8001824C` | 241 | `0x80018250` |
| `0x8001C9D8` | 208, 207 | `0x8001C9BC`, `0x8001C9D4` |
| `0x8001CB84` | 218 | `0x8001CB88` |
| `0x8001CD0C` | 210, 209 | `0x8001CCF0`, `0x8001CD08` |
| `0x8001CE7C` | 212, 211 | `0x8001CE60`, `0x8001CE78` |
| `0x8001D00C` | 214 | `0x8001D010` |
| `0x8001D038` | 213 | `0x8001D03C` |
| `0x8001E824` | 110, 114, 112, 106, 108 | `0x8001E7B4`, `0x8001E7D0`, `0x8001E7EC`, `0x8001E808`, `0x8001E820` |
| `0x8001F088` | 219 | `0x8001F08C` |
| `0x80021E88` | 217 | `0x80021E8C` |
| `0x800622B4` | 120 | `0x800622B8` |
| `0x800631BC` | 153 | `0x800631C0` |
| `0x80066D94` | 137, 138, 139, 140, 141 | `0x80066D68`, `0x80066D74`, `0x80066D80`, `0x80066D8C`, `0x80066D90` |
| `0x80075B08` | 120 | `0x80075B0C` |
| `0x80079B54` | 143, 142 | `0x80079AFC`, `0x80079B50` |
| `0x8009C078` | 144, 86, 88, 89, 90, 87 | `0x8009C040`, `0x8009C048`, `0x8009C054`, `0x8009C060`, `0x8009C06C`, `0x8009C074` |
| `0x8009C750` | 62 | `0x8009C754` |
| `0x800B9AFC` | 197, 195 | `0x800B9ADC`, `0x800B9AF8` |
| `0x800B9B88` | 196 | `0x800B9B8C` |

## 4. Overlays and the misleading “Prank Chance” lead

All **12/12 TPW.OVL entries** were decoded in memory, totaling **122,536 bytes / 30,634 aligned
words**: **0 entries skipped, 0 aligned words skipped, 0 trailing decoded bytes**. No direct J/JAL
or literal pointer matches to the **nine targets in §1**. Positive control: **28 real calls** to
string lookup `0x8006F00C` across overlay entries **1,2,6,10,11**. Addresses and per-entry hashes are
in the audit JSON. **1 boot executable excluded:** `SLES_026.88`, 49,152 bytes. This expands the
handoff's overlay exclusion, without claiming a complete search of every executable asset.

READ: debug.md §3's **Prank Chance** maps to `[0x80103230]` (10). At `0x8008D37C..390` it gates
the entertainer branch, which gets a list via `0x80053768` at `0x8008D398`, emits descriptor
`0x800F77D8` via `0x80089EBC` at `0x8008D540`, and calls the entertainer reaction `0x80095DCC`
at `0x8008D590`. The supposed producer `0x80053768` is a list accessor: load pool
`[0x80103874]`, call `0x8005CD08` at `0x80053774`. This agrees with behaviour.md §2.1's pelting
route; the label **does not identify any of the three advisor notifications**.
Accounting: the complete branch `0x8008D37C..59C` was read, **0 branch instructions skipped**;
other visitor branches were not re-audited as candidate pranks after the producer stop condition.

## 5. Answers to the requested system questions

| Requested result | Established result | Scope / exclusions |
|---|---|---|
| Who is the prankster, what marks one, and when? | **NOT ESTABLISHED.** No producer links an actor/class/flag to 117/118/119. The ordinary entertainer-pelting guest remains separate evidence. | 52 submit sites and 7 computed paths reviewed, 0 skipped; no new exhaustive visitor-flag census. |
| What do the three pranks leave behind? | **NOT ESTABLISHED.** No connection to a pool allocation, effector, particle, or message-only event. No `0x9E` or ordinary-litter identity assigned. | All 3 named events remain unresolved; 0 speculative mappings added. Allocation mechanisms were not ported after the stop condition. |
| What does a handyman do? | **NOT ESTABLISHED for pranks.** Existing Litter claims, ordinary +1 / `0x9E` -6 morale, timing and effect distinctions remain as documented in stinkbombs.md §4. | 0 new cleaning paths traced or ported; all 3 prank-cleaning relationships unresolved. |
| What is the balloon? | READ **caption only**: English text 68, FOLIO 407 offset **0x1877**, says “A prankster is popping other kids balloons. A guard would stop him.” Symbol `STR_ADVMES_ADD_PRANK_BALLOON`, FOLIO 410 offset **0x1866**; advisor row 119 at **0x800EEE48**. This describes popping another visitor's balloon, not a named dropped-balloon object. **Runtime mechanic NOT ESTABLISHED.** | All 3 selected advisor rows and both 1,031-entry text tables read, 0 selected rows/entries skipped; **286 other advisor rows not re-analysed**, **7 other language tables not structurally parsed**. No artwork identification attempted. |
| Port it with a narrow host interface | **Not done, per the explicit stop condition.** A new API would invent the unestablished object and producer. | 0 simulation components, 0 `game/` changes; only findings and search-verification artifacts added. |

## 6. Controls, tests, and mutation sweep

- [audit_prank_callers.py](../tools/audit_prank_callers.py) produces
  [pranks-audit.json](pranks-audit.json). Paired control: original ID load at **0x800622B8** is
  `addiu a1,zero,120`; changing **only an in-memory copy** to 117 gives exactly one fixed prank hit
  at setter **0x800622B4**. Original: **0 hits**; injected: **1 hit**; **0 controls skipped**.
  This measures detection sensitivity, not retail behavior or the completeness of hand dataflow.
- [test_prank_callers.py](../tools/test_prank_callers.py): **10 tests**, every test states what it
  REJECTS. Both matching and nonmatching calls, pointers, partial tails, argument registers,
  branch sources, computed/unknown/stale entries, malformed manifests, and all three IDs covered.
- [mutate_prank_callers.py](../tools/mutate_prank_callers.py) produces
  [pranks-mutations.json](pranks-mutations.json): final **16/16 search-tool mutants killed**,
  **0 survivors, 0 invalid runs, 0 skipped tests**; unchanged-source controls **10/10 before and after**.
  Initial run: 14 killed, 0 survivors, **1 invalid**. Omitting ORI made a valid-manifest fixture
  raise an uncaught ValueError; the test now turns unexpected rejection into an explicit assertion
  failure. The rejected run is retained in `prior_rejected_runs`; no error counted as a kill.
  Final hand review also caught four omitted literals feeding `0x8001E824` (110/114/112/106).
  An independent whole-image check now rejects missing literal delay-slot sources on direct J
  entries to fixed setter sites; its new mutation is killed. This guards that omission pattern,
  without claiming to prove all indirect or conditional control flow.
- Existing [test_stinkbomb_audit.py](../tools/test_stinkbomb_audit.py) covers the reused rule/text
  readers: **8 tests**. No claim that the new 16-mutant sweep covers the imported overlay decoder,
  the imported rule decoder, every hand-read edge, or any simulation behavior.
- `dotnet test tests/TPW.Sim.Tests/`: baseline **1,574 passed, 0 failed, 0 skipped**. Final result
  and audit-test logs are in [pranks-validation.json](pranks-validation.json).

Reproduce from this worktree:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 tools/audit_prank_callers.py
PYTHONDONTWRITEBYTECODE=1 python3 tools/test_prank_callers.py
PYTHONDONTWRITEBYTECODE=1 python3 tools/test_stinkbomb_audit.py
PYTHONDONTWRITEBYTECODE=1 python3 tools/mutate_prank_callers.py
dotnet test tests/TPW.Sim.Tests/
```

## 7. What was NOT established

- A producer for message 117, 118, or 119, or proof that the notifications are unused.
- A single shared producer, prank selection probabilities, a visitor class/flag, or its activation.
- Any stinkbomb/litter/balloon allocation, sprite identity, lifecycle, or area effect.
- A prank handyman claim route, duration, morale effect, or eligibility for the `0x9E` branch.
- Balloon ownership, popping implementation, victim reaction, or guard intervention.
- General absence of indirect/computed/aliased calls or direct queue-memory writes. The unbounded
  caption lookup and unscheduled VM inputs remain named gaps, as does the excluded boot image.
- Independent validation of the existing overlay decompressor; runtime reachability of every
  decoded instruction or hand-classified branch; compressed gameplay code outside these images.
- A retail/emulator run, live actor/object measurement, simulation port, or game integration.

This result narrows the static caller search and leaves the producer explicitly unresolved.
