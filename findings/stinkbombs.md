# Stinkbomb identity: NOT ESTABLISHED — SLES-026.88

The executable and text contain a **stink-bomb advisor message**, but this investigation did not
establish the runtime object to which it refers. **No stinkbomb, prankster flag, producer, or cleaning
behavior was added to `core/TPW.Sim/`.** The requested stop condition applies. A caption is not an
object identity, and neither a green sprite nor a nausea effect supplies the missing link.

READ = bytes/instructions in `/home/ec2-user/tpw/ext/TPW.BIN`, base `0x80010000`, SHA-256
`0b708dd74796abfedad55e55fa007ace1b5697407d36e8f7823bb06f4fa53beb`, or the identified FOLIO records.
GUESS = interpretation. All object offsets are outer-pointer offsets unless explicitly marked Person.
The owner's report that handymen sweep stinkbombs remains evidence from play, not a sprite mapping.

## §0 SOURCE DISAGREEMENTS

**No new behavior disagreement was established.** These relevant, already documented disagreements
remain unresolved. Existing findings' versions remain in code; no existing production file was edited.

| Findings version retained | Binary reading and addresses | Treatment here |
|---|---|---|
| behaviour.md §2.1: Idle roll 5 drops litter; explicitly retained by litter.md §0 and influence.md §0 | `0x8008D710..724` creates emitter descriptor `0x800F79D8` through `0x80089E38`; initializer `0x8008C2B4..330` allocates an effector with bit 4. It does not call litter allocator `0x800514E0`. | Rechecked. Keep `VisitorIdle.DropLitter`; do not silently redirect the hook or rename it stinkbomb. |
| behaviour.md §1/§4: mode suppresses litter everywhere, including the retained Idle hook | Persistent allocator tests the mode at `0x800514F0..50C`; roll-5 block `0x8008D6B0..738` and factory `0x80089E38..EBC` have no such check. | Keep existing suppression. No new producer API. |
| behaviour.md §2.1: nausea >92 **OR** rand(4)==0 | `0x8008D5CC..5F8` requires **AND** and skips the die unless nausea >92. | Rechecked. Keep existing OR; do not use its port behavior to identify a retail stinkbomb. |
| behaviour.md §2.9 “within 2 tiles”, retained as inclusive by `LitterPool.Nearby` | litter.md §0 item 5 records Manhattan distance **<2** at `0x800901B8..1F8`. | Inherited boundary ambiguity, not a newly verified correction. Existing inclusive contract stays. |
| behaviour.md §2.9 bare 64-tick cadence, retained by `VisitorNeeds` | litter.md §0 item 4 records `now &63 == V+0x10 &63` at `0x80090128..13C`. | Inherited cadence disagreement. Existing handler stays. |

The newly found text is an **additional lead**, not a contradiction of litter.md §8's statement
that no runtime record has been identified. `AdvisorMessages.cs` already contains numeric text id
864; its number alone had not connected it to any producer.

## 1. The name is present; its runtime identity is not

**READ:** English FOLIO 407 (`0407.bin`), text **864 / 0x360**, file offset **0x76A2**:

> Some prankster has left a stink bomb in your park. Get it cleaned up before too many people get ill.

FOLIO 410's symbol table gives the same id **`STR_ADVMES_ADD_PRANK_SBOMB`**, at file offset
**0x7D4B**. The advisor table row at **`0x800EEE20`** is message **117**, and its caption halfword
is **864**. Two neighboring rows distinguish three prank notifications:

| Advisor message / table address | Text id | FOLIO 410 symbol |
|---|---:|---|
| 117 / `0x800EEE20` | 864 | `STR_ADVMES_ADD_PRANK_SBOMB` |
| 118 / `0x800EEE34` | 484 | `STR_ADVMES_ADD_PRANK_LITTER` |
| 119 / `0x800EEE48` | 68 | `STR_ADVMES_ADD_PRANK_BALLOON` |

**Accounting beside this result:** all **289 advisor rows**, **0 skipped**; both selected text
tables have **1,031 entries**, **0 skipped entries**. There are nine extracted tables with that
entry count: **2 parsed, 7 excluded from structured language parsing**, but all nine were included
in the raw search. All **690 extracted `.bin` files** received the ASCII search, **0 skipped**.
Raw ASCII searching does not decompress assets or identify artwork/audio.

**READ, bounded negative:** all **125 scheduled advisor programs**, **662 non-End instructions**,
and **1,802 code halfwords** were decoded; **0 programs skipped, 0 code halfwords unreferenced**.
Their **107 PostMessage instructions contain no 117, 118, or 119**. Positive control: rule 0,
word offset 11, posts message 0. This does not exclude an unscheduled/native/computed producer.

All **266,327 aligned executable words** were scanned for direct J/JAL, literal pointers, and
simple ADDIU/ORI-from-zero id loads, **0 skipped words, 0 trailing bytes**. Of these, **187,686**
are in the helpers' established code ranges and **78,641** are outside them but still scanned.
The three code loads of 117 are `0x80083294`, `0x80083310`, `0x80083388`, passing **a3** to
`0x80029B40` in UI work, not posting that advisor id. No such literal load of 118 was found.
The 119 matches at `0x8005073C` and `0x800589DC` are unrelated; another at `0x800C5C30` lies
outside those code ranges. These exact-pattern results do not resolve general dataflow.

**Record-name result:** **488/488 attraction records** examined, **0 unreadable/truncated records
skipped**. The **154 non-record files** and **48 type-150 records** are explicitly excluded from
attraction-name decoding, not silently lost. Counts include **244 numeric +244 prefixed files**,
not 488 distinct menu entries. **All 94 records rejected by the old off+0x40 guard are included.**
No attraction name matched stink/bomb/prank/hooligan/vandal/troublemaker terms. The positive controls
recover the short `0019.bin` Litter Bin and the named Security Cameras; all eight Litter Bin file
copies are recovered by the broader litter keyword. This cannot identify an unnamed pooled object.

## 2. The two candidates remain separate

| Established object | Owner and evidence | Relationship to a stinkbomb |
|---|---|---|
| Persistent litter, including sprite `0x9E` | `PoolOfLitter`, pointer `0x80103878`; 40 objects of stride `0x24`, as established in litter.md §2. Allocator `0x800514E0`; drawable and active-pool lists. | No name-to-sprite mapping established. |
| Unhappy visitor's emitter | Factory `0x80089E38` allocates from emitter pool **`0x800F4EA0`** at `0x80089E44..58`; reset loop `0x80089B38..54` visits **32** emitter slots. Manager reads that pool at `0x80089BFC..C18`, ticks at `0x80089C70`, returns dead emitters at `0x80089C50`. | No evidence connects it to advisor message 117 or handyman targeting. |
| That emitter's area | Descriptor `0x800F79D8`, initializer pointer **`0x800F79E0 →0x8008C2B4`**; emitter+`0x30` owns a nullable handle into the **20-entry PoolOfEffectors**, global `0x80103860`. | Bit 4 establishes an unpleasant effect, not a stinkbomb identity. |

**Census accounting:** the entire **266,327-word image**, **0 skipped**, recovers exactly these
direct persistent-litter allocation sites:

| Call site | Established producer |
|---|---|
| `0x8008D9B0` | Ordinary visitor's failed bin search, reached from Idle selector 3 and carried byte >=90. |
| `0x80090C2C` | State 29 completion; sets the allocated piece to `0x9E` at `0x80090C60`. |
| `0x80053AFC` | Save-count reconstruction; optional `0x9E` setter call at `0x80053B8C`. |

No aligned function pointers to that allocator were found. As an independent positive control for
indirect callbacks, the same pointer scan **does** find `0x8008C2B4` at `0x800F79E0`, despite
finding no direct call to that callback. Computed pointers and aliased writes remain outside the
search's proof. No “three matches means no other possible producer” claim is made.

The ordinary table is still **`9A,9B,9C,9D,A0,9F`** at `0x800E14B8..4CC`. I decoded and visually
inspected those six sprites plus **`9E`** from FOLIO 416: **7 requested, 7 rendered, 0 skipped**.
`9E` looks like a greenish spill (GUESS visual description); the ordinary sprites are too small
and ambiguous to establish named contents. **No sprite was relabeled.** The precise rendering
command is below; image similarity is not a control for runtime identity.

## 3. Who produces trouble?

**READ:** the unhappy-guest branch `0x8008D6B0..738` tests V+`0x59` < **25**
(`0x80103260`) and `rand(1000)` < **100** (`0x80103234`), after selector **5** of six.
It creates the emitter at the guest position, height `terrain+50` (`0x8008D708..724`). This
branch has **no visitor-type or prankster-bit test**. Its conditional chance is 1/60 per Idle
selection once the preceding departure checks have allowed the guest to remain Idle. This is
the producer of that emitter; it is **not established as the producer of a stinkbomb**.

**READ:** the type byte V+`0x61` has accessor `0x800926A0` and setter **`0x800926AC`**
(checked with `ann.py`, not the function-extent heuristic). Its direct setter census finds:

| Call site | Assignment |
|---|---|
| `0x800673B8` | New arrival gets `rand(8)` from `0x800673AC..B0`. |
| `0x80091BE4` | Load path gets another `rand(8)` from `0x80091BD8..DC`. |
| `0x8008EB48` | Purchase branch supplies **8**, then sets Person flag **0x80** at `0x8008EB5C..64`. |

**Accounting:** all **266,327 aligned words**, **0 skipped**, three calls and zero literal
pointers to the setter. The narrower `fieldx.py 0x61` search examines **180,518 main-code words**,
excludes **85,809 other image words**, and finds the direct-offset accesses at `0x8008C764`,
`0x80091DC0`, `0x800926A0`, `0x800926B0`. It does not follow rebased pointers or every flag.
The positive control is the known setter store at `0x800926B0`.

Type influences preference (`0x8008C760..77C`) and model selection (`0x80091DBC..DE0`);
the purchase flag selects model 8 there. The existing costume/souvenir label remains a GUESS.
**Neither type 8 nor flag 0x80 is established as a hooligan marker.** Ordinary guest misbehavior
does exist independently: Idle's entertainer-pelting path calls `0x80095DCC` at `0x8008D590`,
and the entertainer can dispatch a guard through `0x80098494` at `0x80095FFC` (one direct match,
same whole-image census, 0 skipped). This establishes that route, not a persistent vandal class.

## 4. Effects and cleaning: what can and cannot be said

The persistent-litter rules already ported in `Litter.cs` and `StaffClasses.cs` remain unchanged.
`0x8006694C..954` writes **0x9E**; separate leaf **`0x80066958..968`** tests it. It is a
sprite comparison, not an independently established stinkbomb flag.

| Candidate | Existing established effect | Existing established cleanup |
|---|---|---|
| Any persistent litter piece | Nearby-litter scan at `0x80090144..244`: happiness -3 per piece; `0x9E` additionally nausea +3. Port retains §0 radius/cadence differences. | Claim purpose 7, states 11/3/2 then 27. `0x80098C60..CA8` selects 120/60/30/20/10 ticks. `0x80099194..250`: strict deadline, morale +1 ordinary / -6 for `0x9E`, tiredness +5, delete. |
| Descriptor `0x800F79D8` emitter | Radius-squared 1 and flag 4 at `0x8008C314..330`. Influence pass `0x8008FED4..FF78`: walking 2/3 costs happiness 3/nausea +5; otherwise happiness 1/nausea +2. | Its destructor `0x8008C348` releases the area. Lifetime behavior already lives in `UnpleasantInfluence`; no link to handyman claims established. |

**Cleaning census/accounting:** all **266,327 aligned words**, **0 skipped**, find one direct
caller of litter delete `0x80051A94`: **`0x80099230`**. Rechecked the whole class dispatch
`0x80099490..510`: class cases 0, 27, 51; purposes 7 and 18 lead to 27 and 51 respectively
at `0x80098C40..D14`. The second job is the existing usable-feature service job (litter.md §6/§8),
not evidence of another stinkbomb target list. No claimed-object comparison in the complete
`0x80099194..264` function identifies a special ordinary sprite: only the `0x9E` predicate is used.

**Unresolved answer:** if a stinkbomb were one of the ordinary six sprites, that path would give
it the ordinary +1 morale and no extra nausea. If it were `0x9E`, the -6/+3 branches would apply.
If it were the emitter, the effect and ownership would be different. **None of those premises
was established**, so no one of those answers is substituted into the port.

## 5. Reproduction, controls, tests, mutations

Artifacts:

- [audit_stinkbombs.py](../tools/audit_stinkbombs.py) → [stinkbombs-audit.json](stinkbombs-audit.json).
  Reads the original image/archive/extracted files; writes only this worktree. Every record name
  and raw keyword hit is retained with offsets. No `recs.py` size guard or external output path.
- [test_stinkbomb_audit.py](../tools/test_stinkbomb_audit.py): **8 tests**, each states what it
  REJECTS. Positive and negative controls cover J versus JAL, callback pointers versus calls,
  literal ids versus arithmetic, short and truncated records, included/excluded record types,
  name case, string boundaries, and actual PostMessage versus Action8.
- [mutate_stinkbomb_audit.py](../tools/mutate_stinkbomb_audit.py) →
  [stinkbombs-mutations.json](stinkbombs-mutations.json): **12/12 audit-tool mutants killed,
  0 survivors, 0 invalid runs, 0 skipped tests**. Fixed tests, in-memory source mutations,
  unchanged-source controls before and after **8/8**. Reinstating the bad 0x40 guard is one mutant.
  **These are search-tool mutations. There is no stinkbomb simulation mutation result.**
- `dotnet test tests/TPW.Sim.Tests/`: baseline **1,574 passed, 0 failed, 0 skipped**.
  Final suite result is recorded in [stinkbombs-validation.json](stinkbombs-validation.json).

Run:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 tools/audit_stinkbombs.py
PYTHONDONTWRITEBYTECODE=1 python3 tools/test_stinkbomb_audit.py
PYTHONDONTWRITEBYTECODE=1 python3 tools/mutate_stinkbomb_audit.py
dotnet test tests/TPW.Sim.Tests/
```

Useful leaf checks: `ann.py 800926A0 800926B4`, `ann.py 8006694C 8006696C`, and
`ann.py 8008C760 8008C780`. `fn.py 8008C760` swallows the preceding constructor and following
leaves; its reported extent must not be used as that accessor's address.

Artwork inspection used the existing decoder:

```sh
dotnet run --project tools/TPW.Check -- /home/ec2-user/tpw/tpw_psx.iso \
  --sprites 416 /absolute/path/in/this/worktree/litter.rgba 154,155,156,157,158,159,160
```

It emitted **111×14 RGBA**, seven sprites, zero skipped. The tool also printed its existing
“Unrecognised build” warning for disc identification, then completed its explicit sprite command.
That render is not a successful boot or a live-game measurement. Temporary artwork was not added
to the repository.

## 6. What was NOT established

- Which runtime object, record, sprite, pool, or list the retail stinkbomb uses.
- Which guest condition/class/flag produces one; whether a persistent prankster class exists.
- Any connection from message 117/text 864 to a native producer, object, or ordinary sprite.
- Whether the six ordinary sprites include a stinkbomb, or whether `0x9E` has any role beyond the
  established vomiting/load paths. The pictured identities remain unestablished.
- Whether a stinkbomb uses nearby litter, bit 4, both, or a third effect.
- Its handyman route, duration, morale adjustment, or eligibility for the vomit branch.
- That the prank notifications are unused in all circumstances. They may be retained data, but
  the missing traced caller does not prove that. No such absence is encoded as a game rule.
- General absence of indirect/computed/aliased producers. The separate **43,967-byte TPW.OVL**
  and **49,152-byte boot executable** were **2 files excluded from code analysis**; the complete
  TPW.BIN and 690 extracted record files are the enumerated search scope.
- A live retail/emulator experiment, watcher-triggered object identity, game integration, or a
  newly ported simulation component. No existing core behavior or `game/` file was changed.

The next decisive observation would be a retail stinkbomb appearing/disappearing while watching
the litter, emitter, and effector lists, or a traced native route linking the named advisor event
to an allocation. Until then, this remains a named gap.
