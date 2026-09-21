# Park objectives, hidden awards, and the boundary of “winning” (PAL SLES-026.88)

READ means instructions or data at the quoted address in `TPW.BIN`, loaded at `0x80010000`.
GUESS marks interpretation. No console measurement was made. Reproduce the binary audit with
`PYTHONDONTWRITEBYTECODE=1 python3 tools/audit_goals.py`; its image hash and exact search accounting
are in [goals-audit.json](goals-audit.json).

**Result:** the supplied handles lead to **three advertised goals, one optional tutorial award,
and five hidden awards**, checked by executable code. They grant **Gold Tickets**, not a terminal
“park won” state. This is not a level-script objective implementation, so the task's script-only
stop condition does not apply to these nine checks. Other goal/award paths remain outside this
bounded result. Bankruptcy has a separate monthly warning → advisor completion → end sequence.
The existing `DebtWatch` already implements six-month bankruptcy; its documentation records
tinyclaw's console measurement of GAME OVER and the following movie. This report supplies the
missing executable route, not a second debt-clock implementation.

**Follow-up:** [objbytes.md](objbytes.md) accounts for the nine non-weekly minigame award paths,
the advertised 50 tickets, and the campaign-completion consumers. It bounds the remaining **30**
objective bytes as unread in the recovered pointer paths, with explicit search counts and controls.
The original readings and source-disagreement decisions below are retained.

## §0 SOURCE DISAGREEMENTS

Existing findings remain unchanged. The two inherited computational disagreements remain visible
in the implementation, including the consequence that the retained security award is unreachable
at the retail feature-pool limit.

| Existing source and address | This read, with addresses | Retained behavior / boundary |
|---|---|---|
| `shop-stock.md` §7.5 calls `0x80015A68` “count features by flag”; `statistics.md` §0 already retains that reading for statistic 31 | Here the same helper is called with **mask 0x40**, `0x80067B44..48`, and compared unsigned **>80**, `0x80067B4C..50`. Its native bitmap occupancy formula is documented in `statistics.md` §2.5, `0x80015CA4..CF4`. This is not statistic index 64 and not Overall Rating. | Keep a count of nonzero-status features selected by category mask 0x40, including flag priority from `0x80014554`. Do not substitute coverage. At the original **45**-feature pool capacity (`rides.md` §1), the >80 count cannot succeed. Tests use a labelled synthetic 81-feature world to prove the retained branch, with 80 as its failing control. |
| `economy.md` §3 step 6 / `rating.md` §0 retain **level** as the `0x8006AD58` price selector | The scenery-only value helper `0x80067860` calls `0x80063328` at **0x800678D8** (A+0x6B definition selector), then `0x8006AD58` at **0x800678E8**, just like the disputed whole-park value helper `0x800878E4..F8` | Keep the existing LEVEL-price host contract in `ObjectiveAttraction.FeaturePricePounds`, GUESS-low. Sum only features, without the whole-park-value division by two. No new price tables or interpretation are introduced. |
| `advisor-presentation.md` §5.3 calls `0x800BCEA0` for message **141 / 0x8D** “GUESS-medium: end-of-tutorial hooks” | `0x80066D90..AC` issues 0x8D at the sixth negative month. FOLIO 0x197 string **0x373**, selected by message record **0x800EF000**, explicitly says the banks closed the park and declared bankruptcy. Completion `0x8001347C` and dismissal `0x800136D8` call `0x800BCEA0`. | Preserve the existing opaque end-sequence boundary; no advisor changes and no new concrete loss flag. Record the bankruptcy-triggered route here rather than silently replacing the older hook interpretation. |
| `debug.md` §4 calls the sandbox reader **0x800B9A6C** a “year-end/calendar routine” | `0x800B9A60` reads **object+0x24**, sets per-park bit **index+4** at `0x800B9AB0`, grants a ticket at `0x800B9AB8`, then reaches the “Play again?” text **0x234**, message **0xC5**, at `0x800B9AC4..ADC` | No replacement calendar behavior is ported. Preserve unknown higher per-park bits across capture/restore; this additional award producer and its trigger remain outside the weekly component. |

**Refinements, not replacements:** `parkopen.md` §0 correction 4 and §2.2 already supersede
`economy.md`'s loose “scenario override” wording: `0x80071F2C` reads a saved-park gate record.
`debug.md`'s `0x800676EC` handle is the sandbox gate in the **description** function, not a second
reward evaluator. The actual check's gate is `0x8006795C`.

**Text/code mismatch:** goal text **0x34E** and reward message **0xB0** say “within any 12 months” /
“last 12 months.” The instructions read all-time BANK totals, not a 12-month history. Existing
`economy.md` §2 calls it the bank's net position and does not prescribe a rolling-year formula.
The component preserves the read arithmetic, marked **⚠ DO NOT FIX**.

## 1. What s4 is, where definitions live, and where completion lives

At **0x80067944**, `0x80067DBC` selects the current record; **0x80067950** puts the return value
in **s4**. `0x80067DBC` calls **0x80054000** (world, `[0x801038A0]`) and **0x80053FF4**
(park within world, `[0x801038A4]`), then **0x80067CD8(world,park)** at **0x80067DD8**.

The selector is a short leaf, read with `ann.py`, including its argument tests and all delay slots.
It returns one of **eight compiled records of 0x34 = 52 bytes**, or null for world outside 0..3 /
park outside 0..1. There is no heap objective list, count field, kind tag, or met byte in these
records. **Kinds and their bit assignments are hardcoded in the linear check function.**
The binary audit executes the selector for all **8** valid pairs, skipping **0** records.

| World, park | Record address | Admissions threshold | Profit £ | Years open | Feature £ | Maximum path tiles | Tutorial enabled byte |
|---|---|---:|---:|---:|---:|---:|---:|
| 0,0 | 0x800E1930 | 100 | 2000 | 1 | 2000 | 100 | 1 |
| 0,1 | 0x800E1964 | 200 | 3000 | 2 | 2000 | 100 | 0 |
| 1,0 | 0x800E1998 | 150 | 2500 | 1 | 2000 | 100 | 0 |
| 1,1 | 0x800E19CC | 250 | 3000 | 2 | 2000 | 100 | 0 |
| 2,0 | 0x800E1A00 | 150 | 2500 | 1 | 2000 | 100 | 0 |
| 2,1 | 0x800E1A34 | 500 | 5000 | 5 | 2000 | 100 | 0 |
| 3,0 | 0x800E1A68 | 250 | 3000 | 2 | 2000 | 100 | 0 |
| 3,1 | 0x800E1A9C | 500 | 5000 | 5 | 2000 | 100 | 0 |

World labels agree with `disc-check.md` / `TPW.Data.WorldMap`: jungle, halloween, fantasy, space.
They are not needed by the evaluator.

| Record offset, size | Established interpretation | Reader |
|---|---|---|
| +00, +04, +08: three u32 | Opaque; values below | Neither of the two traced selector consumers interprets them |
| +0C: u32 | Strict total-admissions threshold | 0x80067980 |
| +10: signed word used as pounds | Strict net-profit threshold, converted to native Money | 0x800679C8 → 0x800694C0 |
| +14: u32 | Minimum whole years open | 0x80067A60 |
| +18: u32 | Minimum feature purchase-price sum, pounds | 0x80067BF0 |
| +1C: u32 | Maximum count of path tiles | 0x80067C8C |
| +20..2F: four words | All 0xFFFFFFFF in all **8** records; meaning unestablished | Not consumed in traced descriptions/weekly checks |
| +30: u8 | Enables the tutorial building-mix award when nonzero | 0x80067A90 |
| +31..33: three bytes | Opaque, not labelled padding without evidence | Not consumed in those two functions |

Opaque first triples, in table order: **(227,236,3), (209,236,5), (131,160,5), (124,151,5),
(40,60,3), (55,65,3), (365,382,3), (364,384,3)**. Opaque tail bytes +31..33:
**(7,0,0), (6,0,0), (5,0,0), (6,0,0), (5,0,0), (6,0,0), (5,0,0), (5,0,0)**.
In total **21 bytes have established evaluator meanings; 31 bytes remain opaque**.
`ParkObjectiveDefinition` copies and retains all 52 bytes. No scenario FILE loader is claimed:
the established supplier of thresholds is executable data plus the selector. Aliased writes or
other consumers outside these traces are not excluded.

**Completion is separate.** McAi is a **0x328-byte allocation** at **0x80066AD8**,
heap pointer **0x80102E48**. Its **+0x20 word** holds per-park bits; **+0x24 word** holds
campaign-wide hidden-award bits. Test leaves: **0x800675A8** and **0x80067590**;
set/clear leaves: **0x80067658** and **0x800675C0**. These leaves begin above any stack prologue;
`ann.py` preserves their shifts and delay-slot stores.

Constructor **0x80066B54**, calls **0x80066B9C / 0x80066BA4**, loads per-park bits via
**0x8006BEC4** from **0x80109B18 + world*8 + park*4**, and bonus bits via **0x8006BE54**
from **0x80103990**. Destructor **0x80066BFC**, calls **0x80066C14 / 0x80066C34**, writes them
back with **0x8006BE48 / 0x8006BE9C**. Bits are sticky in the weekly check. No unmet marker is
written when a condition fails; a clear bit means “not yet awarded.”

Card header readers **0x8006C6AC..6F4** restore bonus word at header **+0x38**, and all **8**
per-park words at **+0x3C..5B**. Writers **0x8006C28C..2D8** copy the same data. These are
separate from the park stream's `CalendarSave`. Spendable/lifetime ticket totals are header
bytes **+0x34/+0x35**, read **0x8006C694..6A8**, written **0x8006C284..29C**; live totals
are words. Do not persist a fresh zero bonus mask independently for each park.

## 2. The complete weekly check, in execution order

`0x80067928..0x80067CD8` contains **nine award calls**. This is a sequence of tests, not a
kind-dispatch table. Every row first checks its completion bit. Main bits are McAi+0x20;
bonus bits are McAi+0x24. Missing definition or sandbox skips the whole evaluation.

| Kind / mask bit | Exact comparison and live source | Instruction addresses | Message |
|---|---|---|---|
| Admissions / main 1 | **unsigned total admissions > record+0C**. McAi+1C increments at **0x8009225C..6C**, called from successful entrance payment; it is not current population | 0x8006796C..9C; counter load 0x8006797C | 0xAF |
| Profit / main 2 | **signed native raw Money > record+10 × 10**, strict. Raw quantity = **all-time income − outstanding loan principal − all-time spending**; each subtraction wraps to 32 bits | 0x800679BC..A00; helper 0x800873D8, totals readers 0x800873B8 (+12D8 income), 0x800873C8 (+12D4 spending), loans 0x8008748C, subtraction 0x80088D60, comparison 0x80069444 | 0xB0 |
| Years open / main 3 | Only if park open. **unsigned ((month + 12×year − openingMonth) mod 2^32) / 12 >= record+14**. Opening stamp is `[0x80102D24]` via **0x8005BA80** | 0x80067A04..8C; division/compare 0x80067A50..70 | 0xB1 |
| Tutorial mix / main 4 | record+30 nonzero; **at least one sideshow, one shop, one feature, and at least two rides** across ride/tour/track/coaster pools. Raw pool counts, including status zero; tour transport pool is absent here | 0x80067A90..B2C; getters 0x800532D8, 0x8005326C, 0x80053200, 0x8005302C, 0x80053098, 0x80053128, 0x80053194 | 0xBC |
| Security / bonus 0 | **0x80015A68(mask 0x40) > 80 unsigned**. Retained implementation is count of placed category-0x40 features; see §0 | 0x80067B34..6C | 0xB2 |
| Upgrade / bonus 1 | **ride count >=8**, then **every ride in the four ride pools has upgrade !=0**. An empty upgrade iteration returns true, but cannot pass with fewer than eight in the separate count | 0x80067B74..BBC; helper 0x80067808..85C; level reader 0x8009F5E0 | 0xB3 |
| Aesthetic / bonus 2 | **ride count >=8**, then **unsigned sum(feature purchase-price pounds) >= record+18**. All feature objects, no status filter, no halving. Retained price-selector disagreement in §0 | 0x80067BC4..C18; sum 0x80067860..924 | 0xB4 |
| Green / bonus 3 | **shop pool count >=5**, then **every placed shop has at least one placed bin whose rectangle intersects its two-tile-expanded footprint**, inclusive edges | 0x80067C20..58; helper 0x800595B8(5,2) | 0xB5 |
| Path / bonus 4 | **ride count >=10** and **unsigned path-tile count <= record+1C**. Zero paths meets this maximum if the ride gate passes; there is no actual ride-distance check | 0x80067C60..CB4; path counter 0x800598A4..914 → tile predicate 0x8004D4EC | 0xB6 |

**Ride-count oddity, READ:** **0x80059914..968** sums ride, tour, track, coaster **and tour
transport** pool counts. `PoolOfTourTransports` is named at **0x800502F8..304**, pointer
**0x80103880**, count leaf **0x8005CFB4**. It helps the 8/10 gates, but is not visited by the
upgrade helper or tutorial ride sum. Tests include **0 rides / 0 transports (fail)** and
**0 rides / 8 transports (upgrade succeeds)**; the latter is a synthetic oddity control, not
an assertion that the retail game can construct that state. **⚠ DO NOT FIX.**

**Green geometry, READ:** shop count comes from **0x800595D8..E4** before status filtering.
Shop/feature placed predicates are **0x80059610 / 0x8005971C → 0x800660DC** (`A+0x6E !=0`).
Bin predicate **0x8005972C → 0x80023FB8 → 0x80024330** is descriptor **+0x2E bit 2**,
not the feature's research/status/category flags. Shop bounds expand by argument 2 at
**0x80059660..6FC**. Feature far edges are origin+width/height, not minus one;
**0x800597F4..84C** uses inclusive overlap on signed halfwords. One bin may serve many shops.
Five status-zero shops with no placed shop make the universal test succeed; **zero shops fail
before the loop**. Both controls are tested. Signed-halfword coordinate wrap is retained.

**No Overall Rating goal in this bounded function.** All nine conditions and their metric
helpers above were inspected. The whole-image direct-call control finds **0x80067100 →
0x8005B830** (monthly rating sampler), while the weekly function has **0** such direct calls.
Its price sum is a different measure from `ParkScore.CalculateValue`. The component reuses
`ParkScore.Income/Spending`; it neither reimplements rating nor duplicates histories. A future
separately established rating goal should use the existing `ParkScore` API.

## 3. Timing, success, failure, and what “win” means here

**Weekly timing:** caller **0x80066C50** first advances calendar at **0x80066C80**. The month
branch performs finance/history/year-total work before **0x80066E10**. At **0x80066E18** it
requires a changed day-of-month; **0x80066E20..44** tests remainder modulo **7**; the call is
**0x80066E4C**. Eligible zero-based days are **0,7,14,21,28**. This is not every seven elapsed
days: January 28 → February 0 is three days. The host calls `AfterDay` once per actual calendar
day edge, after existing month-end work. The component uses `Calendar.IsObjectiveDay`.

**Descriptions:** **0x800676DC**, sandbox call at **0x800676EC**, checks only main bits 1,2,3
and formats strings **0x161, 0x34E, 0x1EB** with record+0C/+10/+14. Wrapper **0x8006768C**
inserts them as **type 4**. Startup advisor code calls it at **0x8001327C**. `UnmetDescriptions`
returns those three decoded descriptors for host formatting; it does not post messages.

**Success:** wrapper **0x800677B8** calls **0x8006BFE4(1)** at **0x800677C8**, which increments
both live ticket words **0x80103984/88**, then refreshes the ticket HUD through
**0x80037FBC**. It initializes/posts an advisor message using **0x80014118 / 0x8001412C /
0x80014144**, then invokes **0x800693C8(message,2)** for the list. The binary audit executes
this wrapper and observes **ticket +1, chosen message, advisor route, list type 2** in order.
Main 1/2/3 latch before that wrapper; main 4 and the five bonuses latch afterward. The port
returns effects after committing its bits, so host delivery is deliberately outside execution
of the original callback ordering. Do not re-enter evaluation from a notification callback.
Multiple awards can succeed on one day. The populated test asserts exactly **9** events in
native order, then **0** on replay, preserving unrelated high bits.

**Unmet conditions:** skip that award, keep the bit clear, check the next condition. There is
no deadline failure branch, no subtraction of tickets, and no revocation on later decline in
this function. There is no terminal all-complete branch: inspect the tail **0x80067CA0..CD4**;
it awards the path ticket and returns. `disc-check.md` records that tickets open other parks.
The exact overall campaign completion condition is not established here. The additional
bit-index+4 producer at **0x800B9A60** is evidence against calling these nine the complete set
of every possible ticket source.

**Bankruptcy, separate from goals:** **0x80066CF4..D90** counts consecutive negative month-end
balances, issuing 0x89/8A at one, 0x8B at three, 0x8C at five, and **0x8D at six**.
Nonnegative balance resets the counter at **0x80066DE8**; incoming-money behavior is already
in `DebtWatch` / `ParkFinances`. Goal evaluation must not count those months again.
The existing `core/TPW.Sim/Economy.cs` documents a held **−£435,000** balance followed by GAME
OVER at approximately **day 172** and the game-over movie. That is prior project measurement,
not a new run made for this task. Its `DebtWatch.GameOver` behavior is left intact.

The sixth message's English text (FOLIO **0x197**, string **0x373**) explicitly declares
bankruptcy and park closure. When its recording ends, **0x8001346C..84** calls
**0x800BCEA0**; dismissal has the same hook at **0x800136CC..D8**. The hook waits for CD/XA
idle (**0x800BCEB0..B8**) and posts **0x10004**, then **0x80008** at **0x800BCEC4 / CED4**.
The dispatcher matches the latter at **0x800BD018..20**, reaches **0x800BD2E8**, and calls
**0x800BCD00**; the default movie descriptor at **0x801034B8** points to **END.STR** at
**0x801034B0** (language alternatives **0x800E7198 / 0x800E71A4 / 0x800E71B0**).
It then queues **0xD0008**, **0x800BD2F0..2FC**. This establishes an end sequence triggered
by bankruptcy. The subsequent front-end state, save deletion, and every voice/text-disabled
path were not traced. Because §0 retains the earlier opaque-hook interpretation, the code
only documents the host seam, and neither advisor nor message code is changed.

## 4. Scenario data versus save data and research switches

The recovered **52-byte objective record** and **4-byte saved gate record are different things**.
`0x80071F2C` requests **4 bytes aligned** at **0x80071F30..40**, reads **u8 open at +0**
(**0x80071F48**), calls OpenPark **0x80054144** when nonzero (**0x80071F58**), reads
**u16 fee pounds at +2** (**0x80071F6C**), converts with **0x80072F40**, and stores BANK+0
through **0x80087240** (**0x80071F7C**). Byte +1 is unestablished padding. This is already
represented by the save port; the objectives component adds no second fee owner.

The save stream also carries the world, construction, bank/calendar/history, staff, research,
and messages established in `save.md`. They are not fields that can be appended to s4 merely
because they belong to the same park. In particular, the saved open flag runs **before** the
saved calendar is restored (`save.md` §4); opening stamp **0x80102D24** is not a saved field.
Preserve that ordering instead of reconstructing an invented original opening date.

`IResearchCatalogueWorld.AllResearchUnlocked` reads override **0x80102E88**, as `research.md`
§3 says. **0x8006A92C** reads that word *before its stack prologue*, then calls the separate
sandbox getter **0x80059A9C** at **0x8006A95C**. The research loader cannot infer either from
an objective threshold. Sandbox is restored from card header **+0x2A** using the inversion
at **0x8006C5C0..CC**; `debug.md` identifies its FullSim/Sandbox meaning. This work does not
establish an objective-record field that supplies AllResearchUnlocked. The component interface
therefore accepts the live restricted-mode flag and leaves research's existing interface intact.

## 5. Port and loader contract

Files: `core/TPW.Sim/ParkObjectiveDefinition.cs`, `core/TPW.Sim/ParkObjectives.cs`.
The component owns a defensive copy of all definition bytes and both completion words.
`IParkObjectiveWorld` supplies live admissions, open/stamp, restricted mode, debt principal,
all attraction snapshots, tour transport count, and whole-map path count. Existing
`ParkScore` supplies all-time income/spending. Snapshots document units, descriptor flags,
status inclusion, signed tile footprints, and the inherited price contract.

A future loader/host needs to:

1. Select `(world,park)` with `ForPalPark`, or decode a complete 52-byte record into the public
   constructor. Invalid PAL selectors return null and perform no weekly checks. This byte
   constructor is an adapter contract, **not a claim about a scenario file's on-disc format**.
2. Restore **per-park** completion from the eight-word campaign array and the **shared** bonus
   word; capture `State` back on leaving a park. Restore keeps all 32 bits of each word, including
   higher bits not decoded here. Fresh values come from campaign initialization, not from guessing
   that every per-park or campaign save is new.
3. Restore admissions through the existing calendar/entrance save owner, plus bank and score
   totals/loans. Supply the park-opening stamp from the existing open lifecycle, honoring the
   save ordering above. Supply sandbox separately; configure research through its existing API.
4. Supply live pool snapshots, including status-zero objects, real descriptor-bin predicates,
   tile footprints, retained level-selected feature prices, and tour transport count.
5. Call `AfterDay(calendar,world)` once after each actual `AdvanceDay` and after finance/history
   rollover. Persist returned bits; deliver each returned `ObjectiveAward` once and in order:
   one Gold Ticket into both totals, HUD refresh, advisor message, type-2 list entry. The component
   returns effects but owns no message queue, advisor, ticket wallet, or scene router.
6. Format `UnmetDescriptions` on the established startup seam as type-4 entries. For bankruptcy,
   retain the existing monthly debt notices and let the advisor-completion owner handle message
   0x8D's end-sequence hook. Do not wire that into weekly award success/failure.

No `game/`, `ParkAdvisor.cs`, or `ParkMessages.cs` file was modified.

## 6. Verification and search bounds

`tools/audit_goals.py` scans **266,327 aligned words / 1,065,308 bytes** in `TPW.BIN`, skipping
**0** aligned words with **0** trailing bytes. Data words are included as candidates, not
claimed to be instructions. It records direct jump/call and literal-pointer candidates for
**10** selected targets. It checks **8** complete records, **28** original decision fixtures,
**3** original profit-helper fixtures, and the original award wrapper. All **12** audit controls
must pass. Positive controls: rating call **0x80067100**, a nonempty interpreter caller set,
and all **9** weekly award calls. The audit explicitly scans **0** overlays and excludes **12**;
it is not a claim about script-driven awards elsewhere.

An exploratory exact-record-pointer scan likewise visited **266,327** words, skipped **0**,
and found **0** literal pointers to the eight record starts; positive control for that scanner:
constructor **0x800A0084** is a pointer at **0x800E5530**. The selector instead constructs
addresses with `lui/addiu`. This negative is not used to claim records have no consumers.

The MIPS harness is the existing fail-closed interpreter from `tools/audit_rating.py`. Weekly
fixtures stub bit helpers, live profit/open/sandbox getters, and effects; original selector,
comparison instructions, calendar getters, and Money threshold conversion execute. Hidden
awards are masked in those **28** fixtures and tested separately in C# against the disassembly;
no oracle claim is made for their native metric helpers. The independent profit test executes
original subtraction with only the loan-total calculation stubbed. No console run is claimed.

The C# tests state what each rejects. Nonempty controls include exactly **8** records,
**28** binary cases, **9** simultaneous rewards, and every side of count, money, anniversary,
upgrade, scenery, path and four rectangle-edge boundaries. Explicit empty-world and held-shop
controls prevent a universal test over no relevant objects from masquerading as a populated test.

**Completed verification:** `dotnet test tests/TPW.Sim.Tests/` passed **1,670 / 1,670** tests,
**0 failed, 0 skipped**, both before the sweep and after source restoration. This adds **70**
objective tests to the starting **1,600**. No new test warning remains; existing unrelated
BootSequence/VisitorCondition analyzer warnings were observed on the initial build.

`PYTHONDONTWRITEBYTECODE=1 python3 tools/mutate_goals.py` completed **109 / 109 mutations**:
**109 killed, 0 survived, 0 invalid**. Each mutant ran exactly **70** objective tests with at
least **1** actual failed assertion/test. Compile errors and zero-test runs are invalid, never
kills. There were **0 initial survivors** requiring fixture repair. The test assembly is fixed
after the baseline build; only the production assembly is rebuilt for each mutation. The sweep
covers comparisons, signedness/wrap, gates, all four ride classes, all nine message/bit pairs,
bin rectangles and status predicates, state ownership, field offsets, and every one of the eight
record byte arrays. Full details and per-mutation failures are in
[goals-mutations.json](goals-mutations.json).

Both **2** production sources were restored byte-for-byte and their SHA-256 hashes checked
against the audit. Test-source and binary-fixture hashes also match. Protected-path diff count:
**0** (`game/`, `ParkAdvisor.cs`, `ParkMessages.cs`); the test project's fixture entry is the
positive control for that diff check. `git diff --check` passed.

## 7. Not established

- A terminal “park won” or overall campaign-complete condition; these nine checks award tickets.
- The full set of non-weekly ticket producers and the object/trigger of `0x800B9A60`.
- The meanings of **31 opaque bytes** in each compiled objective record, or every possible
  consumer/aliased writer of those fields.
- A separate scenario FILE format or loader that supplies this record, attraction catalogues,
  research override, starting money, or other economic settings.
- A scenario field for AllResearchUnlocked; its established input is the separate global override.
- Resolution of security coverage versus retained feature count, or definition versus level price.
- The end sequence's later front-end/save-deletion behavior, and all disabled-voice/text branches.
- Overlay/script reachability outside the executable paths described here, or console behavior.
- Host notification, ticket-wallet, lifecycle, and UI wiring; those seams are documented and left
  for the host and the concurrent advisor/message work.
