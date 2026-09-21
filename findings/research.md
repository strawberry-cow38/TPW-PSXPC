# Theme Park World (PSX, PAL, SLES-026.88) — RESEARCH AND UPGRADES

READ = instructions/data in TPW.BIN loaded at **0x80010000**, or the named disc entry.
GUESS = interpretation, with confidence and a way to settle it. Executable SHA-256:
`0b708dd74796abfedad55e55fa007ace1b5697407d36e8f7823bb06f4fa53beb`.
P is the Person/Staff subobject, **P = outer+8**. A is the attraction subobject, also outer+8.
R below is the **research** BANK singleton, not the monetary bank. T is one research topic.

Start points retained: [ride-panel.md](ride-panel.md) §1–2 owns upgrade gates, costs and resets;
[staff.md](staff.md) owns researcher class/resource/offset corrections; [rides.md](rides.md)
owns attraction layouts, status and accepted corrections. Their accepted preambles take priority
over their older body text. No game host or assets are changed by this port.

## 0. SOURCE DISAGREEMENTS

These are review items. The findings' versions remain in code, including the existing shared
staff-stat bounds. There is no silent replacement with the newly read binary behavior.

| Existing source | Binary READ | Treatment in code |
|---|---|---|
| behaviour.md §3.3: `S+0x47 += (BANK+4 - 80)/3`, an ordinary signed subtraction/division; §3.1 and StaffMember bound tiredness to 0..100 | 0x80099AB8..ADC subtracts 80 as a wrapping word, divides **unsigned** by 3 (`multu 0xAAAAAAAB`, high word >>1), narrows to a byte; 0x80099CC4..CEC reads that byte **signed**, adds it to the signed existing byte and caps **only above 100**. At effort 70, tiredness 5: binary adds **82**, giving **87**; signed report arithmetic adds **−3**, giving **2**. The address passed is outer+0x47 = P+0x3F, the tiredness identified in staff.md. | Researcher.Research uses the report's **signed `(Funding-80)/3`**, then StaffMember's existing 0..100 clamp. The missing fatigue operation is added, but its newly discovered unsigned/narrowing oddity is explicitly NOT substituted. `ResearchFatigueKeepsTheFindingsVersion` and a mutation enforce this decision. |
| behaviour.md §3.3: Idle “not on strike → rand(10) < 3”; existing Researcher.Idle only rolled after its strike check | 0x800999D8 calls rand(10) **before** calling shared slot 51 at 0x80099A14. The RNG is consumed even when strike/rest takes over. | Retain the earlier decision order: shared staff check first, then roll only if still Idle. Shared slot-51 strike/rest states follow behaviour.md §3.1. `IdleRunsTheSharedStaffCheck` rejects the silent RNG-order change. |
| behaviour.md §3 introduction / StaffBase summary: every class switch then falls through to Staff::Update | Researcher Update 0x80099B10 dispatches Idle at 0x80099BBC and state 31 at 0x80099BCC, then **jumps to the epilogue** at 0x80099BC4/BD4. Only the other states call 0x80094AC4 at 0x80099BE8. | Keep the existing separate class/shared-handler API; do not introduce a binary-specific dispatcher that silently changes established host scheduling. The native dispatch is recorded in §1, not installed as a replacement scheduler. |

Terminology clarification, **not a changed rule**: statistics.md §2.2 calls the return from
0x8006A9A8 a “required research cost.” It is a **tier ordinal**, used as an index into five bins
at 0x8009B7BC / 0x8009BA08. Actual work comes from **0x8006AA78**. Its existing **zero-value
auto-unlock rule** is correct and retained. The names `ResearchLevel.Tier` and `.Work` keep these
distinct. rides.md §2's formerly unidentified +0x48/+0x4C words acquire meanings here; that is
additional evidence, not a disagreement with “no reader I found in the ride code.”

Carried forward without re-derivation: ride-panel.md's intensity disagreements and the accepted
mechanic upgrade/closing corrections remain untouched. staff.md's starting morale/tiredness
**100/0** policy remains unchanged. The older rides.md §2 uncertainty about a third panel upgrade
was already settled by ride-panel.md: the panel stops at level 2, the raw purchase allows level 3.

Code-only gap filled: Researcher.Research previously omitted the reported fatigue operation.
Researcher.Idle previously omitted the shared rest check. Its use of Handyman.BySkill also
fabricated row 4 for corrupt grades 5..7; no finding establishes those rows. The new researcher
lookup still masks with 7, but explicitly rejects an unestablished overread instead of inventing
points. No cleaner/mechanic table or handler is altered.

**Independently re-read (tinyclaw, 2026-09-20), the two load-bearing disagreements above:**

- *The fatigue arithmetic.* 0x80099AB8 is `addiu v0,v0,-0x50`, then `lui/ori v1,0xAAAAAAAB`,
  `multu v0,v1`, `mfhi v0`, `srl v0,v0,1` — an UNSIGNED divide by three — and `sb v0,16(sp)` narrows
  the result to a byte before 0x80099CC4 is called with `a0 = s1+0x47`. Unsigned, and narrowed, both
  as reported. The code keeps behaviour.md's signed version on purpose.
- *The idle ordering.* 0x800999D8 is `jal 0x800C2648`, the roll, and it is the FIRST call in the
  function; the vtable call is at 0x80099A14 and the `slti v0,s2,0x3` that tests the roll sits after
  it at 0x80099A20. So the RNG really is consumed before the shared check, and the port really does
  keep the other order on purpose.

## 1. What the researcher does each tick

READ: class constructor/init **0x80099830 / 0x800998C4**, update **0x80099B10**. staff.md's
kind **4**, five-person pool at **0x80103870**, sprite resource **274**, sheet base **396** and
Person vtable **0x800E4C74** remain the established class identification. Resource 274 is not a
research lab or progress record.

| Native tick phase/state | Work and transition | Evidence |
|---|---|---|
| Skip held/selected employee | P flag 0x40 or current selected Person pointer suppresses the update | 0x80099B34..64; selection getter 0x80054024 |
| Pre-dispatch hook | Calls Person slot 41; Staff base implementation is a no-op | 0x80099B6C..84; 0x800947C4 |
| **0 Idle** | Roll 0..9; shared slot 51 may redirect to strike/rest; otherwise roll <3 sets **31**, roll ≥3 sets **13**. No work is contributed on this tick. | 0x800999C0..A58; source-order disagreement above |
| **31 Research** | Read skill from outer+0x44 & 7 (P+0x3C & 7); fetch points; contribute to R; adjust tiredness using R+4; **SetState(0)**, clearing the state stack. | 0x80099A70..AF0 |
| **13 Patrol** | Shared machine requests a patrol path: purpose 1, state **11**; no rectangle/refused path → **5** random wander. | behaviour.md §3.1; staff.md §2 |
| **11 → 3 → 2** | Shared path request, path-ready and walking states. Purpose 1 arrival returns to **0**; walking raises tiredness on the shared cadence. | behaviour.md §3.1; StaffBase/StaffMotion |
| **26 / 15** | Shared walk-to-strike / striking. Slot 51 aborts the current target and enters 26; strike clears → Idle. | 0x80094698..750; behaviour.md §3.1 |
| **49 / 50** | Shared seek-rest / resting. Idle with tiredness **>80** seeks a bench/staff room. These are staff recovery facilities, not research multipliers. | 0x8009470C..744; behaviour.md §3.1 |

READ: work points table **0x800E4E44**, s32 by skill 0..4: **20,30,35,40,43**.
There is no duration timer in state 31. “30%” is a probability per **Idle decision**, not 30% of
all simulation ticks: Idle itself takes a tick and patrol walks take more. Contributing does
not test whether a topic exists first; even with zero active topics the fatigue adjustment and
return to Idle happen. A strike beginning after the Idle decision is not rechecked inside
the state-31 work handler.

READ: no lab lookup, world position, researcher-count multiplier, ride level, park grade,
timescale or bank-money debit is in **0x80099A70 → 0x8009B618 → 0x8009BEBC**. Every researcher
contributes separately when its own state-31 tick runs. More researchers add more such calls;
their shared patrol/rest/strike behavior changes the realized rate. Skill and R+4 effort are
the only per-worker rate multipliers in that chain.

READ: R+4 is initialized to **80** (0x8009B230/240); research panel slider bounds are **70..100**
(0x8007C3D8..E8), and panel update writes it at **0x8007C81C** through raw setter **0x8009B40C**.
The raw setter does not clamp. No money is withdrawn for changing it. “Funding” is the inherited
interface name; **effort setting** is a more accurate description of the traced effect.

## 2. Where progress lives and how it advances

### 2.1 Two representations, with different precision

READ: singleton getter **0x8009B380** allocates **0xAC bytes**, tag `BANK`, through handle
0x80103308 and stores the resolved pointer at **0x80103304**. This is separate from the real
monetary bank (economy.md §1). Initialization is **0x8009B214**.

| Address/field | Meaning | Evidence |
|---|---|---|
| R+0 | tier-cache dirty word, initialized 1; active completion sets 1; refresh clears it | 0x8009B234..244, 0x8009C174, 0x8009BD18 |
| R+4 | effort, raw s32 | 0x8009B240, 0x8009B40C, 0x80099D3C |
| R+8 | initialized zero; purpose not needed/established here | 0x8009B23C |
| R+0xC + 0x1C×slot | **five** topic records | 0x8009B640..668 |
| R+0x98 + 4×category | five cached maximum eligible tier ordinals | 0x8009B254, 0x8009BAAC, 0x8009B858, 0x8009BC38 |

| Topic T field | Meaning | Evidence |
|---|---|---|
| +0 | auxiliary UI/attention word; start clears it, focused category writes 100. Not read in contribution arithmetic. GUESS-low label “attention”; trace panel presentation consumers to settle the label. | 0x8009BEA8, 0x8009C0DC, 0x8007C834..83C |
| +4 | **required work <<12**, u32 comparison/division | 0x8009BEB0..B8 |
| +8 | **accumulated work**, 20.12, u32 | 0x8009BEEC..F8 |
| +0xC | active word | 0x8009BF60, 0x8009C194 |
| +0x10 | finished flag, set 1 at completion, reset on start | 0x8009BF80, 0x8009C0C0 |
| +0x14 | signed definition index; completion sets **−1** | 0x8009BF58, 0x8009C088..8C |
| +0x18 | definition type | 0x8009C180 |

READ boundary: the native initializer explicitly clears each topic's **active word**; it does
not establish the other inactive words. Managed new topics have zero defaults there, which are
not claimed as PSX initial-memory observations. Start initializes all fields used by active work;
the loader ignores type/index bytes for inactive saved topics.

READ: the **catalogue** stores a separate compact entry at **0x80109A28**, capacity **60** entries,
stride **4 bytes**, searched/allocated by **0x8006B094 / 0x8006B02C**, obtained by **0x8006AE4C**:

| Byte | Meaning | Accessor |
|---|---|---|
| +0 | type, read signed; −1 is unused | 0x8006B11C, 0x8006AF0C |
| +1 | type-local index, read signed; −1 is unused | 0x8006B12C, 0x8006AF14 |
| +2 | **whole percent** toward the next level, read unsigned | 0x8006AF2C / 0x8006AF38 |
| +3 | **completed-level count**, read unsigned | 0x8006AF18 / 0x8006AF24 |

The key has no park/theme field because the whole table belongs to the current loaded park.
A completed count of 1 means the **base** is available; 2 means first upgrade research is
available; 3 means both normal upgrades are researched. It is not the level of any built ride.

### 2.2 Exact arithmetic

READ, **0x8009B618**: count the currently active topics, N. If N=0, return. Otherwise every
active topic receives the same share, processing slot 0 through 4:

```
funded = low32(skillPoints * effort)
share = u32(funded << 12) / u32(100 * N)       # unsigned division, truncate
T.progress = u32(T.progress + share)
level = AvailableLevelCount(type,index)      # may finish zero-tier levels
percent = T.required == 0 ? 100 : u32(100*T.progress) / T.required
StoreCatalogueProgress(type,index,level,s32(percent))
if T.progress >= T.required: FinishTopic()
```

READ: denominator is **100×N**, not N alone (0x8009B674..684). **⚠ DO NOT FIX:** fractional
remainders are discarded separately on every contribution. N is fixed before the slot loop:
when an earlier topic finishes, later topics do **not** get its spare share on that call.
The per-topic attention word T+0 is not a weight. The host maps the existing
`IResearchWorld.ContributeResearch(points * ResearchFunding)` to `ResearchSystem.ContributeResearch`;
the native multiply moves across that existing interface without being performed twice.

Examples derived from the READ arithmetic:

- One skill-0 worker at effort 80, one active topic: **16.0 work** per research tick (65536 raw).
- Same worker, three active topics: **21845 raw** each (5.333251953125 work), total 65535.
- A required-work value 200 takes **13 state-31 contributions** at 16 work each, starting from zero;
  this is not 13 ordinary elapsed ticks. Excess eight work is not carried to another level.

READ: **0x8006ACDC** completes at most **one** catalogue level when signed percent≥100,
sets percent zero, then writes only if the proposed level is not older than the stored count.
Both stores narrow to bytes. A same-level percent can decrease. **⚠ DO NOT FIX:** the percentage
getter uses low-word multiplication before unsigned division and does not clamp; large/corrupt
progress can wrap and its displayed percentage can go backward. Completion's target comparison
is separately unsigned (0x8009BF20..3C). Ordinary disc values do not require overflow to be useful,
but widening all arithmetic would change the native rule.

### 2.3 Selecting, stopping and finishing

READ: research panel confirm **0x8007C510** gets the chosen type/index, **stops** the existing
slot using **0x8009BD30**, and calls **0x8009B448** unless the row is blank (−1). A failed replacement
leaves the previous topic stopped. There is no automatic choice or topic queue after completion.

READ: start **0x8009B448** queries level count, whole percent, tier and required work **before**
testing whether the slot is already active. An active slot refuses. Otherwise refresh the dirty
tier cache; slots 0..3 require cached tier≥candidate tier; slot 4 bypasses that check. Start sets
active=1, finished=0, attention=0, key, required=`work<<12`, and accumulated=`u32(percent*required)/100`.
This lower-level entry does **not** repeat the menu's built-ride predicate.

READ: stopping clears active only. Each contribution has already copied whole percent to the
catalogue. **⚠ DO NOT FIX:** reselecting an old topic reconstructs work from the byte percent,
discarding its sub-percent remainder. No work transfers between definitions.

READ: finish **0x8009BF68** sets finished=1, active=0 and R.dirty=1; resolves the definition name
(0x8006A09C → 0x8006A76C), releases the asset reference, posts a message and sets index=−1.
The contribution already wrote the completed catalogue level before this function. It changes
no ride's level, reliability, sliders or status, and makes no bank call.

| Completed definition | Advisor message ID | Evidence |
|---|---|---|
| ride types 1/3/6/7, available count <2 | **0x56** | 0x8009C00C..48 |
| ride upgrade research, zero mechanics | **0x90** | 0x8009C028..40 |
| ride upgrade research with mechanics, or type-8 track piece | **0x57** | 0x8009C070..78; jump table 0x800E517C |
| shop / sideshow / feature | **0x58 / 0x59 / 0x5A** | 0x8009C04C..6C |

READ: zero **work** with a nonzero tier does not auto-unlock during a catalogue query. Once
selected, even a zero-point contribution completes it because required=0 and percent getter=100.

## 3. What becomes available, and in what order

### 3.1 Player choices and definition lists

READ: research unlocks **base attraction definitions**, **ride upgrade levels**, and **type-8
track-piece definitions**. This is the same current theme/set catalogue used by the build lists,
not staff training, golden tickets, another park, or attraction-pool capacity. See rides.md §1.2
for the established FOLIO table mapping at **0x800DDDC4** and the current theme/set selectors.

| Topic slot | Eligible list, in menu order | Native builder |
|---|---|---|
| **0** new rides | type **3 flat**, **6 track**, **7 tour**, **1 coaster**; increasing type-local index in each | 0x8007C498 → 0x800493AC, helpers 0x80049410/45C/4A8/4F4 |
| **1** shops | type **4**, increasing index | 0x8007C4AC → 0x80049540 |
| **2** sideshows | type **5**, increasing index | 0x8007C4C0 → 0x8004958C |
| **3** features | type **2**, increasing index | 0x8007C4D4 → 0x800495D8 |
| **4** upgrades | types **3,6,7,1**, then **8 track pieces**, increasing index within each | 0x8007C4E8 → 0x80049624 |

READ: 0x800492A4 walks `index=0..count-1`; 0x80049144 appends in that order.
0x8004912C appends the final blank/stop choice, **not a sorting pass**. Ordinary lists exclude
already available bases. The upgrade list needs an available base, at least one built matching
type/index (0x80069E30 includes placed instances without a status filter), and completed count<3.
**⚠ DO NOT FIX:** type-8 research instead requires **type 6, index 0** to be available, then the
piece itself still locked (0x8009BE18..40). It does not require a built track ride and does not
substitute whichever track ride happens to exist. Its menu/start path bypasses category tier tests.

READ: these are **choices**, not a fixed “first X, then Y” sequence. Tier eligibility below constrains
the next choices. Within one ride definition the sequence is always base → upgrade 1 → upgrade 2.
For specific ride names and their initial definition index, retain rides.md §1.2's existing theme
lists; for example its Lost Kingdom set-A flat order starts Crazy Ape, Inca Pot, Sun God, Mayan
Spinner. The current scenario supplies the track/tour/coaster arrays; inventing one global roster
would contradict that established layout. The host supplies counts and decoded fields from the
user's disc. No complete game-content table is redistributed (records.README.md).

GUESS-medium boundary, inherited from rides.md §1.2: the exact named track/tour/coaster candidates
for a particular scenario depend on its runtime arrays, whose population is not established here.
Capture current manager theme/set and those arrays in that scenario, or finish the scenario-loader
trace, to settle the names. Their **type order, index order and eligibility rules** above are READ.

### 3.2 Tier eligibility is a two-thirds rule

READ: rides' level fields are record **+0x48+0x34×L = tier**, **+0x4C+0x34×L = work**;
other types use **+0x28 = tier**, **+0x24 = work**. Getters: **0x8006A9A8 → 0x8006AFC0/6B008**
and **0x8006AA78 → 0x8006AFE4/6B014**. The latter returns zero if that requested level is already
available. These are neither the price word +0x50 nor the ride's wear/seat words.

READ disc cross-check: ISO9660 root entry `FOLIO.GAZ;1`, LBA **174726**, size **16164376**;
FOLIO entry **220** starts at GAZ+**0x3CE800**, length **9928**, its definition at entry+**0x25C8**.
The three `(tier, work, price-pounds)` triples at definition+0x48+0x34×L are **(0,0,2000)**,
**(1,200,200)**, **(1,200,200)**. The entry was read directly from the supplied 2048-byte-sector ISO
and compared byte-for-byte with `ext/rip/0220.bin`. This narrow Crazy Ape check distinguishes
work 200 from tier 1 and independently agrees with the established full-price upgrade rule.

READ: `RefreshTiers` **0x8009BC8C** runs only when R+0 is set. It bins all **base** definitions by
tier (five stack words each for total/unlocked), asks base availability, and walks tiers from zero
while **3×unlocked≥2×total**. Each passing tier writes the next tier to R+0x98+4×category. Equality
counts; an empty tier passes. First failing tier ends the scan. Initial failure makes **no store**,
so the previous cached ceiling survives.

READ: ride-category census order differs from menu order: **6,7,1,3**, table **0x800E516C**, consumed
by **0x8009B8AC**. Then refresh shops, sideshows, features through **0x8009B708**, and track pieces
through **0x8009BAFC**. Getter side effects make this ordering worth preserving. Free unlock
queries and save-loader catalogue writes do not set dirty; active completion does.

**⚠ DO NOT FIX / unestablished overread:** the scan has **no bound after tier 4**
(0x8009BA98..C4, 0x8009B84C..74, 0x8009BC2C..54). If all five bins pass, it reads adjacent stack
words. GUESS-high: real incomplete catalogues normally stop before this; a live fully-unlocked
catalogue trace and stack capture would settle the terminal behavior. The managed port throws
at that boundary rather than inventing a tier cap, next work constant, or deterministic garbage.
It likewise fails explicitly when the 60-entry catalogue overflows; the native allocator returns
null, subsequently dereferenced. Valid disc grades/records remain host-supplied inputs.

### 3.3 Availability queries also unlock

READ: **0x8006AB54** returns 100 if the requested level is below the completed count. Otherwise a
**zero tier** calls **0x8006ACB8** and returns 100; nonzero tier returns the current byte percent.
Even a query for a future level returns that same byte. **0x8006AC04** loops over zero-tier levels
until count reaches 3 or a nonzero tier is encountered. This behavior is already required by
statistics.md §2.2 and the ride-panel gate; the port provides the concrete implementation.

READ: override **[0x80102E88]** makes level count return 3 directly without allocation or completion.
Availability **0x8006A92C** accepts that override **or** restricted mode **0x80059A9C**; otherwise it
tests percent **==100**, not ≥100. Restricted mode does **not** override the level-count getter.
Free queries do not invoke the active-topic completion announcement.

## 4. Per-park lifetime and save precision

READ: current-manager getter **0x80069610** initializes the manager from current set/theme via
**0x80069580** and resets the **entire 60-entry progress table** at **0x80069638 → 0x8006AEC8**.
Cleanup **0x80057EFC**, calls **0x80058070/78**, destroys research R and clears manager initialization
through **0x80069654**. A fresh manager initialization clears the catalogue again. This is
**per loaded park**, despite the storage having a process-global address. No persistent global
unlock-across-parks mechanism is in this chain.

READ: park save **0x800715F0..7198C** writes catalogue whole-percent/completed bytes in current
definition order; loader **0x80072B54..72E50** restores these through **0x8006ACDC**. Only afterwards
does it restore research BANK. **0x8009B28C** writes **16 bytes**: effort narrowed to one byte,
then five `(active, type, index)` triples via **0x8009C128**. **0x8009B2EC** reads funding unsigned,
type unsigned, **index signed**, and restarts each saved active topic through **0x8009B448**.

**⚠ DO NOT FIX:** neither raw accumulated work, finished flags nor topic attention are saved.
Restart reconstructs progress from the catalogue's whole-percent byte, losing fractional work
just as changing topic does. Catalogue restore must precede topic restore. A save does not make
the research table global; each park save reconstructs its own table and active selections.

## 5. The ride upgrade path, retained end to end

This section integrates the established READ in ride-panel.md §1–2 and the accepted mechanic
correction. It is not a replacement derivation of its costs, gates or slider rules.

1. Research completes the ride definition's next level in the catalogue. Existing ride **A+0xF6
   stays unchanged**. `ResearchSystem.LevelCount` now supplies `IRideUpgradeWorld.ResearchedLevelCount`.
2. Panel **0x80078D98..78F44** offers next=current+1 only when next<3, lifetime!=0, and
   **next<researched count**. At count 2, level-0 rides can upgrade; level-1 rides cannot yet.
3. Confirm **0x80079AA8..79B24** requires a mechanic and no mechanic strike. It does not test cash,
   current reliability or ride status. Action **0x8003BE00 → 0x8005BE14** enqueues the ride in the
   **15-pointer** queue **0x801099EC**, count **0x80102D48**. Duplicate succeeds; new entry when full
   fails (**0x8005BA8C**). No money, level, status, or run-clock change at request time.
4. Mechanic selector **0x8005BE44 → 0x8005BAF8** chooses the nearest unclaimed queued ride. Claim
   **0x80096C58**, flag 0, does not repeat flag-1 repair's broken/lifetime checks. States:
   **57 → path 11/3/2 (purpose 20) → 52 → 54 → 17 → 58**.
5. State **52** closes using **A+0xEC**, threshold `10*(width+height)<<12`
   (**0x8009C1E8**), not rider count; then status **6** and a skill deadline. Durations are the
   existing mechanic table **240/180/120/60/60** at **0x800E4574**. State **54**, **0x80096A08**,
   requires deadline!=0 and **now>deadline**, calls paid upgrade **0x8009C56C**, removes queue entry,
   and enters state 17. No equality-edge completion.
6. Paid upgrade **0x8009C56C(A,silent)** checks only current level<3, increments byte **A+0xF6**, then
   **0x8009C410** clears both effect handles, sets reliability **A+0xB4=0x64000**, zeros cycles-run
   **A+0xF2** and closing-progress **A+0xEC**, and resets the three settings from the new level.
7. Charge is the **full NEW-level price**, record **+0x50+0x34×level**, in **pounds converted ×10**
   to Money (**0x8009F470**, **0x8009C600..614**). **⚠ DO NOT FIX:** mutation precedes payment and
   TrySpend's result is ignored. Silent suppresses sound/sparkle only; it still charges.
8. Mechanic state **17** waits for closing progress==0 then sends command **7**, reopening to
   **10 Loading** under AttractionLifecycle, and moves to state **58**. This reopening is separate
   from purchase. Manager cleanup **0x8005C2F4** has the established alternate silent completion
   of already-claimed matching queued jobs; it is not a research or auto-upgrade tick.

| Effect of applied level | Established record/live rule |
|---|---|
| Maximum seats | common slot 99 reads level block +0x0C; coaster uses its established geometry override instead |
| Capacity setting A+0xBC | `max(1,virtualMaximumSeats>>1)` |
| Wear multiplier | subsequent reads use level block +8; common example 5→3→2 |
| Speed A+0xB8 | midpoint `min+((max-min)>>1)`, block +0x14/+0x18 |
| Duration A+0xC0 | `max(1,cyclesMax>>1)`, block +0x20; **does not clamp to cyclesMin** |
| Queue capacity | subsequent rule `4*level+7`, 7→11→15 |
| Slider UI ranges | union over all **three** level blocks, as already ported; research does not narrow these |
| Lifetime A+0x68 | **unchanged**; block lifetime is only used at placement |
| Riders/lists, status A+0x6E, placement day A+0xF4, phase accumulator A+0x60 | **unchanged by purchase**; mechanic/status controllers handle their own changes |

READ distinction: the disc **definition record is not rewritten** by an upgrade. The instance's
A+0xF6 selects another immutable level block for subsequent seat/wear/range/price reads, and
the listed instance fields are reset. Research's catalogue record is a third, separate object.

READ: ordinary maximum is **level 2**, two applied upgrades. The raw routine allows **level 3**
and then stops, overreading the three-block definition. No fourth valid price, seat count or
range is established. Existing RidePanel keeps that oddity and requires the host to supply
observed overread data or fail. This port does not “fix” the raw limit to match the panel.

### Research versus applying an upgrade

**READ: separate operations linked by an availability gate.** Research raises the **definition's
catalogue count**, costs work, and may announce “upgrade available.” A mechanic raises the
**individual ride's A+0xF6**, charges money, resets settings/reliability and later reopens it.
All instances of the same type/index in the current park share the research gate; each still
needs its own queued, paid upgrade. No contribution call invokes 0x8009C56C; its existing callers
are mechanic completion and manager queue cleanup (ride-panel.md §2). No paid-upgrade store writes
the research progress table. Type-8 track-piece research uses the same work/census system but
unlocks build components rather than changing a ride's level.

## 6. Port boundary, verification and reproduction

`core/TPW.Sim/Research.cs` owns per-park compact progress, five topics, tier cache, ordered choices,
fixed arithmetic, completion, and topic save/restore. `IResearchCatalogueWorld` supplies decoded
definition counts/tier/work, placed-definition counts, mode flags, mechanic availability and
announcement output. Researcher handlers stay in `StaffClasses.cs`; shared walking/rest/strike,
the actual employee scheduler and asset loading remain with their existing owners. There is no
Godot dependency, new lab rule, new bank fee, or duplicate implementation of RidePanel/Mechanic.

The host maps:

- `IResearchWorld.ResearchFunding` → `ResearchSystem.Funding` and its funded contribution →
  `ResearchSystem.ContributeResearch` (no second multiply).
- Ride panel `ResearchedLevelCount` and statistics `AvailableLevelCount` → `LevelCount(key)`;
  build/statistics availability → `IsAvailable(key)`; statistic 50 → `AnyActive`.
- Definition/level reader → the offsets in §3.2 from the current park's disc catalogue.
- Save: whole catalogue percent/count first; then `SaveTopics`/`RestoreTopics`. Construct a fresh
  ResearchSystem on park load, not a process-global static one.

GUESS boundaries are explicit above: auxiliary attention label, terminal tier-stack overread,
and corrupt grade/record overreads. A fully reconstructed active-emulation dispatch would also
need resolution of §0's RNG and fallthrough disputes before changing established host scheduling.
No frame-perfect emulator measurement or new in-game UI is claimed.

Verification: **1,451 tests pass**, including **61 new cases** and the **1,390** pre-existing cases.
**104/104 compiled behavioral mutations killed**, **zero survivors**, **zero invalid mutants**.
Every new test states its rejected alternative. Synthetic fixture data intentionally distinguishes
tier from work and research from purchase; it is not a substitute game-content table.

The initial sweep killed 101 and exposed **three test gaps**. All were fixed and rerun; their
original surviving attempts, subsequent failing test names, full-suite results and test hashes
remain in [research-mutations.json](research-mutations.json):

| Initial survivor | Test strengthened to reject it |
|---|---|
| Clamp topic percentage to 100 | Explicit **150%** raw getter after 300 work on a 200-work topic; catalogue still completes only once and clears its percent. |
| Advance tiers at one-half instead of two-thirds | Independent **1 of 2** base definitions available must leave the next tier locked. |
| Bin by next-level tier instead of base tier | Two of three available **ride** bases have different next-level tier words; eligibility must still advance using the base tiers. This was rechecked after making the fixture specifically use rides, whose disc layout contains separate tier words per level. |

`tools/mutate_research.py` rebuilds only production for each mutant and runs baseline-compiled
tests, so changed public constants cannot be silently inlined into both sides of an assertion.
Compilation failure is **invalid**, never a kill. The runner serializes builds, restores through
a top-level try/finally and SIGINT/SIGTERM/SIGHUP handlers, and keeps durable backups/journal for
`--recover` after interruptions that cannot execute finally. All working files stay under this
worktree's ignored `obj/` directories. `--resume` preserves prior attempts; `--resume --retry NAME`
can recheck an already killed mutation when its fixture is improved. No forced-interruption test
is claimed for this run.

Final full-suite verification passed after restoration. SHA-256 checks confirm all **three**
mutated production files match their originals, and all three filtered test files match the
audit's final hashes. RidePanel.cs is byte-identical to the starting branch; the permanent changes
are the new research service, researcher handler gap fixes, tests, this ledger/index and the audit.

Reproduce decisive reads (the leaf-boundary checks use `ann.py`; `fn.py` can miss an entry's first
instruction when it precedes the stack prologue). Neither script modifies the executable:

```sh
python3 findings/fable-scripts/fn.py 800999C0 80099A70 80099B10 8009B618 8009BEBC
python3 findings/fable-scripts/fn.py 8009B448 8009BD64 8009B8AC 8009B708 8009BAFC 8009BF68
python3 findings/fable-scripts/fn.py 8006A9A8 8006AA78 8006AB54 8006ACDC 8006AE4C
python3 findings/fable-scripts/ann.py 8006A92C 8006A9A8
python3 findings/fable-scripts/ann.py 8006AC04 8006ACB8
python3 findings/fable-scripts/ann.py 8006AF0C 8006B02C
python3 findings/fable-scripts/fn.py 8009B28C 8009B2EC 800715F0 80072B54
python3 findings/fable-scripts/callers.py 8009B618 8009B448 8009BD64 8006AEC8
python3 findings/fable-scripts/xref.py 80109A28
python3 findings/fable-scripts/fieldx.py 47
dotnet test tests/TPW.Sim.Tests/
python3 tools/mutate_research.py
```

`fieldx.py` hits are a census, not proof that equal offsets share an object type. Research fatigue's
outer+0x47 is passed by address; the signed load/add/store occurs inside 0x80099CC4. Similarly,
`fn.py 8006AC04` can select the preceding function; the explicit aligned `ann.py` span above is
necessary. Nop-elision in annotation must never be read as moving an instruction into a delay slot.

## ⚠⚠ RETRACTED 2026-09-21 — see §9. The section below is kept for its measurements, which
## stand; its CONCLUSION was two port bugs, not a property of the disc.

## ⚠ NO TOPIC CAN BE STARTED ON THIS DISC — measured 2026-09-20

Wiring the researcher end to end (game/ParkResearch.cs) turned up a hard stop that reading the code
alone did not:

```
[tpw] --park-research 0,3,2: tier scan refused: Research tier scan overruns its five PSX bins.
```

`RefreshTier` walks five tier bins and advances while `3 × unlocked[cursor] >= 2 × totals[cursor]`.
⭐ **An EMPTY bin passes that test vacuously** — `3×0 >= 2×0` is `0 >= 0` — so the walk only stops at
a bin that is populated AND under two-thirds unlocked.

**Bin 4 is empty on this disc.** Over all **498 ride level blocks** in `/home/ec2-user/tpw/ext/rip`
(types 1/3/6/7, **0 skipped for length**), the research tier takes only four values:

```
tier 0 ×52    tier 1 ×220    tier 2 ×126    tier 3 ×100    tier 4 ×0
```

So as soon as tiers 0..3 pass, the cursor reaches 4, finds nothing, passes again, reaches 5 and runs
off the end of both five-word arrays. The port throws there deliberately —
⚠ `DO NOT FIX`, `0x8009BA98..C4` has no bound and no deterministic overread is established — and the
host now catches it at the harness door and reports it rather than swallowing it.

### What this does and does not establish

- **Established:** with the port's current catalogue — every definition the port loads, all worlds —
  the tier scan cannot terminate, so `CanSelect` throws and no topic can be selected.
- ⚠ **THE ONE-WORLD HYPOTHESIS IS FALSIFIED.** The first version of this section guessed that the
  original's candidate set is one world's definitions and that a single world has a populated tier 4.
  Checked: it does not. Splitting the ride records by world at the boundaries the per-world markers
  already establish (litter bins 19/98/193/351, staff rooms 32/109/197/353, cameras 30/105/185/350):

  ```
  W1  tier0×6  tier1×24  tier2×15  tier3×12
  W2  tier0×8  tier1×32  tier2×16  tier3×13
  W3  tier0×5  tier1×25  tier2×20  tier3×13
  W4  tier0×7  tier1×29  tier2×12  tier3×12
  ```

  Four worlds, near-identical shapes, **not one tier 4 between them** — which the global histogram
  already implied, since tier 4 is zero over all 498 blocks. Accounting: 249 of the 498 blocks carry
  a numbered entry and are classified above; the other 83 records have no entry number in their
  filename and are unclassified, but they cannot change the answer because the global count of tier 4
  is zero.

- **So the remaining reading is that the scan's last bin is simply never reached in play.** The walk
  stops at the first bin that is populated AND under two-thirds unlocked, so reaching bin 4 requires
  two-thirds of every tier 0..3 ride already unlocked — a park that has researched nearly the whole
  game. NOT ESTABLISHED, but it is now the only candidate left standing, and it would mean the
  original's unbounded read is reachable in principle and effectively never taken.
- **NOT established:** the definition ORDER. `ResearchDefinition(type, index)` is taken by the host
  to mean "position among the definitions of that type in catalogue order"; the binary carries a
  per-object definition selector at `A+0x6B` (rating.md §0) that would settle it. ⚠ A wrong order
  does not crash — it researches the wrong ride, which looks exactly like working software.

### The two record fields this needed

`TPW.Data.RideLevel` read every level field from `+0x08` to `+0x2C` **except** `+0x24` and `+0x28` —
record `+0x48+0x34×L` and `+0x4C+0x34×L`, the research **tier** and **work** (§3.2, READ). They are
exactly what `IResearchCatalogueWorld.ReadResearchLevel` returns, and their absence is why nothing
could be wired before. The values confirm the offsets rather than merely compiling: tier is an
ordinal 0..3, and work takes seventeen distinct values, every one a multiple of fifty, 0..2750.
Crazy Ape level 0 reads tier 0 / work 0 — a starter ride needing no research — and Eruption reads
tier 3 / work 550.


## 9. The retraction, and research actually running — 2026-09-21

§8's headline was **wrong**, and the way it was wrong is worth more than the fix. It reported the
overrun, attributed it to the empty **tier bin 4** over 498 ride level blocks, and concluded the
scan "cannot terminate" for structural reasons. The first thing I did was make the exception name
which scan it was:

```
tier scan refused: ... slot 1, totals [8,0,0,0,0], unlocked [8,0,0,0,0].
```

**Slot 1 is the SHOP scan.** Not the rides. Eight shops, every one of them at tier 0, every one of
them already unlocked — so bin 0 passes the two-thirds test on the first call, the four empty bins
pass vacuously, and the cursor runs off the end. Nothing to do with ride tiers, and it fires
immediately rather than "effectively never". One line of diagnostic that the earlier pass did not
print; every other word of §8's analysis was reasoning over a number that never named its subject.

### 9.1 Why every shop was already researched: the port never read a non-ride's tier

**READ 0x8006A9A8**, the research-tier getter. It dispatches on type through the table at
**0x800E1710** (index `type - 1`, valid 1..17) and the eight live rows split cleanly in two:

| types | target | reads |
|---|---|---|
| 1, 3, 6, 7 — the rides | `0x8006AA04` → `0x8006AFC0` | `record + 0x48 + 0x34×level` |
| 2, 4, 5, 8 — feature, shop, sideshow, upgrade | `0x8006AA28` → `0x8006B008` | **`record + 0x28`**, no level |

The work getter is the same shape: `0x8006AA78` returns 0 when the definition is already available
(which is the port's `work = IsAvailable(...) ? 0 : data.Work`), and otherwise dispatches through
`0x800E1758` to `0x8006AFE4` for rides and **`0x8006B014` = `record + 0x24`** for non-rides. Each
offset was reached from its own consumer, not inferred from the one next to it.

**The disc agrees.** Across every non-ride record, `+0x28` takes only the values **0, 1, 2, 3** —
37 shops as 12/9/8/8, 44 features as 16/15/7/6, 33 sideshows as 16/13/4 — which is exactly the
rides' tier range; and `+0x24` is 0 or a multiple of fifty, which is exactly the rides' work range.

`AttractionDefinition.Read` parsed level blocks **only `if (a.IsRide)`**, so every non-ride carried
an empty `Levels` array and `ReadResearchLevel` answered `(0, 0)`. ⚠ That is not a neutral default:
**tier 0 with zero work is the auto-unlock case**, so every shop, feature and sideshow in the game
was already researched at park open. The port now reads both words for types 2/4/5/8.

### 9.2 The one ceiling nothing ever reads

With the shops fixed, slots 0..3 all terminate and the overrun moves to **slot 4** —
`totals [0,0,0,0,0]` — which is the ride-upgrade scan over **type 8, and type 8 has no records on
this disc** (arrivals.md's census). Every bin is `(0, 0)`, `0 >= 0` passes, and the walk leaves the
array on its first call. The original has the same unbounded walk (0x8009BAFC).

But `ceilings[4]` **has no consumer**: `CanSelect` takes its slot-4 branch before the tier test and
`Start`'s gate is explicitly `slot != 4`. So the overread's only product is a number the game never
asks for, and throwing on it took the entire research system down for it. Slot 4 now stops instead
of throwing; slots 0..3 still throw, because their ceilings ARE read and garbage there would be a
wrong answer rather than an unused one.

### 9.3 It runs

```
[tpw] --park-research 0,3,2: slot 0 researching type 3#2
research tick  250: slot 0 type 3#2 4%
research tick  750: slot 0 type 3#2 9%
```

A topic is selected and its progress moves. Each researcher contribution is worth exactly 4.5% of
this topic — 2 contributions read 9%, and a six-researcher run read 27% off 6 — so the fixed-point
arithmetic in `ContributeResearch` is linear and correct.

⚠ **It also appeared to stop — and that was MY PARK, not the port.** Recorded in full because the
wrong reading survived three rounds of instrumentation and I published it before the last one.

```
park in 2 pieces:   3 took the work,  6 patrolled, 72 never chose;  rest: 0 of 69 (69 no route)
park in 1 piece:    3 took the work, 14 patrolled,  8 never chose;  rest: 8 of 8 granted
```

Same code, same seed, shorter run on the right. **The rest system works.** In the first park the
staff room's door tile sat in the *smaller* of two connected pieces — the room's own footprint had
blocked the path I laid past it — so every rest request was refused, tiredness never fell, and the
loop's pre-pass diverted 89% of the researcher's idle ticks before they reached its own decision.
Connect the park and the diversions fall from 72 to 8 and every one of them is granted.

⚠⚠ **findings/staff.md §6.7 already says this: "the measurement needs a CONNECTED park … the park
report's `map in N connected pieces` line is the check."** I wrote that, and then built four fixtures
without once looking at it. The line is in the report now next to the rest breakdown so the next
reading cannot be taken without it.

⚠⚠ **Two separate instruments lied about this on the way, in the same manner.**

1. **The state log.** It showed ~100 `Idle -> Patrolling` against 2 `Idle -> 31` — a 2% outcome from
   a 30% roll, which reads as a broken RNG. It is not. The whole diversion — `Idle` → `GoAndRest` →
   *no rest place* → `Patrolling` — happens **inside one tick**, so a before/after line prints its
   two endpoints and the middle is invisible. `Idle -> Patrolling` from a diversion and
   `Idle -> Patrolling` from the roll are the same eight characters. Grepping for `GoAndRest` in the
   log returns **zero** for the same reason, and zero there means "never printed", not "never
   entered".
2. **The first version of this counter**, which read `0 never chose` — because it was placed inside
   `RunResearcher`, and the pre-pass diversion happens *before* `RunResearcher` is called. It was
   built to catch exactly this case and was positioned where the case cannot occur. Moving it to the
   pre-pass turned 0 into 72 with no other change.

So the research RATE is still **NOT ESTABLISHED** — 3 of 17 decisions took the work in the connected
park, against a documented 30%, which is far too small a sample to call. What IS established: a topic
can be selected, contributions arrive, they accumulate exactly, staff rest when there is somewhere to
rest, and a park in **two pieces** stops researching within about nine decisions.

### 9.3b The offset has teeth

`+0x28` was moved to each of its three neighbours and the park re-run. All three **crash**, and they
crash in a way that says exactly what is wrong:

```
REAL     --park-research 0,3,2: slot 0 researching type 3#2
+0x2C    IndexOutOfRangeException at RefreshTier -> totals[tier]++
+0x24    IndexOutOfRangeException at RefreshTier -> totals[tier]++
+0x20    IndexOutOfRangeException at RefreshTier -> totals[tier]++
```

The bin index is the tier, so a word that is not a small ordinal walks straight out of the five-bin
array. Only `+0x28` yields values the tier machinery can even accept — which is a stronger result
than "the scan refuses", because a wrong-but-plausible word would have refused too.

(⚠ `totals[tier]++` is unbounded in the original as well — `0x8009B7C0` shifts the tier and indexes
a stack array with no check. The port throwing there is a wrong-data guard, not a modelled
behaviour; nothing in play should reach it now that the offset is right.)

### 9.4 What this retraction does NOT reach

- **The ride scan was never the problem.** §8's arithmetic about tier bin 4 over the 498 ride level
  blocks is still correct as arithmetic; it simply was not what the port was hitting.
- **The one-world hypothesis stays falsified.** §8 checked it properly and it remains checked.
- **The definition ORDER is still NOT ESTABLISHED** (§8). A wrong order researches the wrong ride and
  looks exactly like working software; nothing here touched it.
- **Nothing here says the original does not overread.** It does — 0x8009BAFC has the same unbounded
  walk. The port's choice to stop on slot 4 is a decision about a value nothing consumes, not a claim
  about what the hardware returns.


## 10. The four entry points the panel needs — 2026-09-21

`Candidates`, `TierCeiling`, `Progress` and `ApplyFundingSlider` were ported, tested and had **no
caller in `game/`**. They are what a research panel is made of: the shortlist you pick from, the tier
that gates it, the stored percentage, and the funding slider. All four are reachable now, and the
report carries the three that are readable:

```
--park-funding 250: funding := 250 -> 100
--park-research 0,0: slot 0 researching type 3#2 (#0 of 6 offered)
--park-research 1,0: slot 1 researching type 4#1 (#0 of 2 offered)
--park-research 4,0: slot 4 offers nothing

research: funding 100, 2 researchers,
  slot 0 type 3#2 4% (stored 0 levels, 4%),  slot 1 type 4#1 7% (stored 0 levels, 7%),
  slot 2 type 5#0 3% (stored 0 levels, 3%),  slot 3 type 2#7 10% (stored 0 levels, 10%);
  offered per slot (count@ceiling) 0:6@1 1:2@1 2:2@1 3:2@1 4:0@5
```

**`--park-research=SLOT,CHOICE` picks by position in the game's own shortlist**, which is what the
menu does; `SLOT,TYPE,INDEX` still names a definition directly, because a control needs to be able to
ask for something and be told no.

**Controls, all four refusing for their own reason:**

| asked | answered |
|---|---|
| `--park-funding=250` | `250 -> 100` — the upper clamp |
| `--park-funding=10` | `10 -> 70` — the lower clamp |
| `--park-research=1,99` | `slot 1 offers 2, not #99` |
| `--park-research=4,0` | `slot 4 offers nothing` — type 8 has no records |
| `--park-research=0,0` twice | `start refused` — `Start` refuses an occupied slot |

⚠ **`offered per slot` prints slot 4's ceiling as 5, and that is the overrun's leftover** (§9.2)
showing itself rather than hiding. Nothing reads it; it is printed so that nobody later mistakes it
for a computed ceiling.

⚠ **`stored` is printed beside the live percentage on purpose.** `Topic.Percent` is the live
fixed-point and `ResearchSystem.Progress` is the record a save keeps; they are allowed to differ
mid-level, and printing one while calling it the other is how a save round-trip bug hides.
