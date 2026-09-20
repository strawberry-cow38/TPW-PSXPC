# Park score, value and history — SLES-026.88

READ means instructions in `/home/ec2-user/tpw/ext/TPW.BIN`, loaded at `0x80010000`.
GUESS is carried into the C# interface or implementation, with the evidence needed to settle it.
The sim implementation is `ParkScore.cs`, composing the existing `ParkHistory`; save packing is
`ScoreHistoryCodec.cs`. No `game/` file is changed. The host integration remains the user's work.

## §0 SOURCE DISAGREEMENTS

| Existing finding retained | Binary reading, with addresses | Code decision |
|---|---|---|
| `economy.md` §3 step 6: `value = (Σ defPrice(type, level))/2`. Its §4.6 separately describes `defPrice(mgr,type,variant)` and the base record price. These descriptions disagree about the caller's selector. | `0x800878C4..F8` calls the virtual type getter, then `0x80063328` (`lbu v0,107(a0)`, **A+0x6B definition selector**), passing that result as a2 to `0x8006AD58`. It does **not** call upgrade getter `0x8009F5E0` (A+0xF6). `0x8006ADBC..C4` passes level zero to the ride definition's price accessor. | Preserve §3's literal **level** selector as `IParkScoreWorld.ValuePricePounds(type, level)` and pass `StatisticAttraction.UpgradeLevel`. Mark **GUESS-low / SOURCE DISAGREEMENT** on the interface and calculation. A test deliberately gives definition 17 and level 2, and rejects silently switching to definition. This is **not a claim of binary-equivalent park value** until the finding is adjudicated. |
| `happiness.md` §0/§4.3 and `ParkHistory.RatingAtNewYear` name `0x80069314` as an annual-rating reader. | `0x80069314..8006934C` is previous-month length, called by wages. The separate three-instruction leaf **0x80069350..80069358** reads McAi+0x2F8. The whole-function extent heuristic reports it under 69314. Save reads it at `0x80068994`; save restore writes it at `0x800690A4`. | Preserve existing `ParkHistory` January-latch behavior and existing historical comment. Add the precise reader address here. No behavioral substitute. |

Newly traced omissions are not silently presented as disagreements: the save reports explicitly
left interpolation unported, so adding its arithmetic does not contradict them. The codec's
six-month byte interpolation bug and cross-row/padding reads are READ, described below.

## §1 The rating

**Annual rating is a January snapshot of the monthly Overall Rating, not a separate annual
average or the advisor's cache.** `0x800670D4` calls `0x8005B830` at `0x80067100`, before its
visitor aggregation. It writes the result to the Overall ring at **McAi+0x268+slot**
(`0x80067250`), and to the byte **McAi+0x2F8** only when the already advanced year is nonzero
and month is zero (`0x80067180..198`). CalendarSave byte **+0x20** carries that annual byte
(`0x80068994..800689A0`, restore `0x800690A4`). Constructor clears it at `0x80066B8C`.

Let V be live people, R all rides of types 1/3/6/7, U those rides with upgrade byte >=2, S shops,
G sideshows, F features, and C[k] the five staff pool populations. The formula is:

```
min(V,100)/5 + min(3*R/2,20) + min(U,10)
+ min(2*S,10) + min(2*G,10) + min(F,10)
+ sum(k: min(C[k],4))
```

All integer divisions truncate. The range is **0..100** for valid nonnegative pool contents;
100 is the sum of the individual caps, not a final clamp.

| Term | Source and arithmetic |
|---|---|
| People, max 20 points | people-pool pointer **0x80103884**, pool+0xC via **0x8005CCF0**; cap 100 and unsigned /5 at **0x8005B830..88C** |
| Shops, max 10 | pointer **0x80103854**, count **0x8005CF54**; multiply 2, cap 10 at **0x8005B880..89C** |
| Sideshows, max 10 | **0x8010385C**, **0x8005CF3C**, **0x8005B8A0..8B8** |
| Features, max 10 | **0x80103858**, **0x8005CF6C**, **0x8005B8BC..8D0** |
| Cleaner / mechanic / entertainer / guard / researcher, max 4 each | pointers **0x8010386C / 68 / 74 / 64 / 70**; count leaves **0x8005CCE4 / CCD8 / CCC0 / CCFC / CCCC**; caps at **0x8005B8D4..948**; staff kind identities already established in `staff.md` §1 |
| Rides, max 20 | iterator **0x8006DCCC(...,1)** at **0x8005B94C..960**; next **0x8006DE68**; transition routine **0x8006DD48** excludes non-rides for nonzero iterator mode; count and `3*R/2` at **0x8005B96C..9B4** |
| Upgraded rides, max 10 | **0x8009F5E0** reads A+0xF6; compare against 2 at **0x8005B978**, increment **0x8005B984**, cap **0x8005B9BC..9C4** |

Unsigned min leaf **0x8005C7D4..7E8**, signed min **0x8005CBD0..BE4**. Rating sums the terms
at **0x8005B9C8..BA00**. There is no attraction status, availability, guest happiness, balance,
reliability, wage grade, staff strike, or held-staff subtraction in this function. Cursor and
construction objects still in their live pools count. The host must supply those full pools.

The financial comparison page **0x800830CC** reads last month's Overall through
**0x80067098(McAi,0)** at **0x8008313C**, and the annual byte through **0x80069350** at
**0x800831C8**. For its text labels it divides each by **20** (`0x80083144..164`,
`0x800831D0..1E8`), caps the index at **3** (`0x8008322C..248`), and indexes the halfword
text-id table **0x80103164** (`0x80083410..428`, `0x8008345C..470`). This display categorization
is not another numeric score, and is outside this sim component.

### Relationship to the existing advisor

**No advisor statistic id feeds rating or value.** `ParkStatistics` and `ParkAdvisor` are neither
copied nor changed. `StatisticAttraction` and `StatisticStaff` snapshots are reused at the host
boundary. Relevant overlapping inputs, not interchangeable results:

- **11**: visitor pool count; the same population underlies the rating's visitor term.
- **6..10**: entertainer/mechanic/guard/researcher/cleaner counts, but these use the existing
  held-staff subtraction (`0x80053864`). Rating directly reads pool+0xC.
- **13..16**: placed rides/shops/sideshows/features, excluding status zero in the statistics
  calculator (`0x80014554` path); rating includes it.
- **22**: applied upgrade percentage, not rating's count of rides with upgrade >=2.
- **46**: mean guest happiness (`0x80016E90..EF8`); same aggregation as the Happiness ring,
  entirely separate from Overall Rating. Reuse `ParkHistory.MeanHappiness` when needed.
- **47**: balance statistic, not park value. **48**: previous-month wages versus income,
  through **0x8008781C / 0x8008767C**, each with raw age 1; wire these to `ParkScore.Read`.
- **49**: weighted attraction count, explicitly distinct from Overall Rating in
  `statistics.md` §2.6/§6. Do not substitute it for `CalculateRating`.

Advisor idle/statistics-enabled gating only controls its existing cache and rules. It does not
suspend this calendar-driven history writer.

## §2 Park value

A separate computation, **0x80087884..8795C**: enumerate every attraction with
**0x8006DCA0 / 0x8006DD3C / 0x8006DE68**, get a definition price through **0x8006AD58**,
sum whole pounds at **0x800878FC**, convert the sum to raw Money at **0x80087920**, THEN
divide by **2** at **0x80087928..93C** using **0x80088CDC**. Half a pound survives an odd
pound sum. See §0 for the deliberately retained selector disagreement.

`0x8006AD58` selects ride record base +0x50 (types 1,3,6,7, via **0x8006AF9C**) or non-ride
+0x20 (via **0x8006B020**); it is not ticket revenue, cash, debt, a resale event, a staff cost,
or the entry-fee verdict's virtual attraction-value getter. The iterator has no status filter.
It does not traverse placed path tiles. Value is **recorded, never charged**.

No 0..100 range applies: it is a signed 32-bit Money word, in tenths, with no explicit clamp.
For ordinary nonnegative prices it is half their sum. No disc-wide price or maximum-reachable-
value census is claimed; the component obtains prices from its documented host service.

The monthly rollover invokes it at **0x80086EE0** and stores it to
**BANK+0x107C+4*(index%144)** at **0x80086F20**. The value getter **0x80087960** recomputes
LIVE value for age zero (`0x80087978..884`); positive ages read that ring. Call sites include
**0x8007F8A0**, **0x80080B48**, **0x80083124**. The port exposes this distinction as `ReadValue`;
`Read(BankHistoryRow.ParkValue,0)` is explicitly a ring inspection instead.

Yearly value/cash snapshots at **BANK+0x12EC / +0x12F0** are overwritten when the **pre-increment
bank index is nonzero and divisible by 12** (`0x80086F24..78`). The first is the **13th**
rollover, one month after the annual rating and year-total rotation. Cash there is AFTER charges;
the monthly balance ring is BEFORE charges. Do not align these edges for tidiness.

## §3 Live histories and totals

The unpaused McAi tick **0x80066C50** advances the calendar at **0x80066C80** and compares the
old/new month at **0x80066C88..90**. Once per month: strike evaluation **0x80067DF0**,
bank rollover **0x80086D70** at **0x80066CC0**, debt processing, visitor histories
**0x800670D4** at **0x80066DEC**, then increment McAi+0x18 at **0x80066E04**. If year changed,
rotate annual totals through **0x80087570** at **0x80066E08**. No daily accounting is introduced.
The existing Calendar owns its no-leap-year month lengths (table **0x800E1554**).

### Bank: eight rings of 144 signed Money words

| BANK base | Samples/accumulates |
|---|---|
| +0xBC | balance before the month's loan/wage charge, **0x80086DDC** |
| +0x2FC | every booked Income transaction, **0x80086980..A08** |
| +0x53C | entrance receipts, also part of Income, **0x80087258..2D8** |
| +0x77C | shop profit booked as type-4 income, **0x80086AA8..AF4** |
| +0x9BC | sideshow gross takings booked as type-5 income, **0x80086AF8..B48**; prizes are separate spending |
| +0xBFC | every charged TrySpend transaction, **0x800868B8** |
| +0xE3C | monthly wage sum, REPLACED at **0x80086ECC**, not including dismissal pay |
| +0x107C | monthly park value, **0x80086EE0..F20** |

Bank index **+0x12BC**, initially zero (`0x800865F4`), is incremented at **0x80086F84..9C**.
Six running rows (income/entrance/shop/sideshow/spending/wages) at the new index%144 clear
at **0x80086FC8..8716C**. Balance and value do not clear on advance, only on their next write.
Thus once full, the current accumulating slot has already overwritten the oldest transaction
month; balance/value retain that old cell until the next rollover. No unbounded history exists.
The age resolver **0x80087610** checks signed raw `age < index`, then converts age zero to one,
then wraps by adding 144. Positive ages beyond 144 can alias a ring cell rather than being
clamped to the window. Age 1 is unavailable at bank index 1; age zero can read that first result.
The port rejects negative ages as a host error and reuses `ParkHistory.SlotFor` for nonnegative ages.

Historical **totals** are event accumulators, not more sampled rings:

| BANK field | Update/overwrite |
|---|---|
| +0x12C0 | loan repayments this rollover; zero then add each payment, **0x80086E0C..48**; not saved |
| +0x12C4 / C8 / CC | all-time sideshow / entry / shop receipts, typed writers above |
| +0x12D0 | all-time wages, monthly add **0x80086E78..80** AND sacking pay **0x8008717C..198** |
| +0x12D4 / D8 | all-time spending/income, **0x800868B8 / 86980** |
| +0x12DC / E0 | this-year / last-year income |
| +0x12E4 / E8 | this-year / last-year spending |
| +0x12EC / F0 | yearly value / closing balance snapshots described in §2 |

Year rotation **0x80087588..5C0** replaces last-year with this-year and zeroes this-year,
after December's scheduled spending. All-time totals do not reset annually or at ring wrap.
The native additions are 32-bit word operations (**0x80088DFC / DE0**); this owner retains
that wrap even though the shared port `Money` type is wider. Setting initial balance is not income.

### Calendar: existing ParkHistory, not a new machine

The five **byte** rings, each 144 months: people **McAi+0x28**, arrival delta **+0xB8**,
mean happiness **+0x148**, mean days in park **+0x1D8**, Overall Rating **+0x268**.
Writer **0x800670D4..67250**, using the month count BEFORE increment. People is live count;
arrival is `max(0,count-Read(People,monthsBefore,1))`; happiness is sum/count or zero;
time is mean(totalDays-arrivalDay) or zero; Overall is §1's computed result. Every store is a byte:
256 people wraps to zero, and times above 255 wrap. Annual rating is the existing separate byte.

`ParkHistory.SlotFor` reproduces **0x80066F44** and the five accessors
**0x80066FA8 / FE4 / 67020 / 6705C / 67098**. Its unsigned raw-age guard precedes the zero-to-one
bump. In particular, the second writer's arrival-delta previous count is still zero. Those existing
oddities, tests and `MeanHappiness` remain in place. Bank uses the same nonnegative age mapping,
but bank's native comparison is signed. The byte histories and bank histories share cadence and
window, not samples or storage. No new happiness/advisor engine is added.

## §4 The save compression now has an owner

Save records remain exactly the layouts in `save.md`. Host can fill balance, loans, calendar,
admissions and strike state as before, then delegate just these owned fields:

- `score.CaptureBank(bankSave)` / `score.RestoreBank(bankSave)` for the eight histories,
  bank index and twelve historical totals/snapshots. Balance and loan bytes are untouched.
- `score.History.Capture(calendarSave, calendar.TotalMonths)` / `.Restore(calendarSave)`
  for the five histories and annual rating. Other fields and unknown padding are untouched.

The component does not save a current computed rating. It saves the existing annual latch and
monthly samples. Totals are saved in WHOLE pounds (`0x80088E18`); restore multiplies by 10
(`0x80088E50`). Recent bank samples remain raw tenths. Unsaved loan-repayment bookkeeping is
left unchanged by restore, as in the native reader; a fresh owner starts at zero.

| History block | Exact recent | Older groups | Record length |
|---|---|---|---|
| calendar, pack **0x80068740**, unpack **0x80068A54** | 12 bytes | 6 pairs, 8 six-month groups, 9 eight-month groups; integer means | **35 bytes** |
| bank, pack **0x800879DC**, unpack **0x80087F90** | 12 raw words at +8 | same groups, normalized to bytes using whole-pound min/max at +0/+4 and scale 255 | **80 bytes**, +0x4F untouched by writer |

Both start from `(monthIndex+287-i)%144` (`0x80068748`, `0x80087A20`). They pack physical
cells, even when the park has fewer than 144 months. **GUESS-high**: managed unwritten cells
start at zero. The native BANK constructor zeros the whole balance ring (`0x800867CC..F0`)
but only slot zero of each other ring (`0x80086738..C8`); allocator contents for their unwritten
cells have not been established. This affects early saves' old/unavailable data, not valid samples.

Bank extrema inspect indices `i=12 .. min(143,monthIndex-1)-1`, initializing min/max to zero
unless at least one eligible old sample exists (**0x800879E4..87B2C**). This excludes raw age
144 and unavailable early ages. Yet all old groups are encoded. A zero range becomes 1 at
**0x80087B28..30**. Group sums/means and normalization use **unsigned** arithmetic
(**0x80087C10..30 / CC8..CF8 / D80..DA0**), even for negative pound samples. Low-word multiply
wraps. Restore denormalizes with signed low-word multiply and /255 (**0x800880C4..88128**).
These are intentionally not replaced with a general floating-point interpolation library.

Expansion places each group value at its first cell and interpolates toward the next group:

- Byte six-month group subslot 1 duplicates the current group, `(6*a)/6`, at
  **0x80068BE4..C2C**. It does NOT use `(5*a+b)/6`. Other weights are 4:2, 3:3, 2:4, 1:5.
- Money pair midpoint uses arithmetic `>>1` at **0x800881B8..C0**, rounding a negative odd sum
  down; six/eight-month weighted divisions truncate toward zero (**0x80088324..540**,
  **0x800886C8..88968**). Halfway cells use `(a+b)/2` before larger weights, preserving word wrap.
- The last eight-month group reads the NEXT byte: calendar **record+35**, another row's first
  sample or final **CalendarSave+0xDA** padding (`0x80068FE4..6900C`); bank **record+0x4F**
  padding (`0x8008867C`). The implementation passes the remaining enclosing record span and
  preserves that byte. It does not invent zero or clamp to the last group.
- Money restore briefly zeroes the current physical slot (`0x8008803C`), then the full 144-cell
  expansion overwrites every cell including that slot. The net expansion is reproduced.

These routines are lossy and are not an exact frame snapshot. In-progress transaction cells can
participate in the oldest group on a wrapped ring; no new separately saved live-month accumulator
is invented.

## §5 Host contract and integration

`IParkScoreWorld` has four services: full visitor/attraction/staff snapshots and the documented
price lookup. It reuses existing statistics DTOs, includes status-zero/held objects, and has no
Godot or rendering dependency. `ParkScore` owns its bank arrays and historical totals; callers
can inspect detached ring copies, never mutate backing arrays. An existing `ParkHistory` may
be supplied to the constructor, preserving that owner's identity.

Forward every actual booked income once with Other/Entrance/Shop/SideShow classification;
forward ordinary spending once; use `RecordSackingPay` for dismissal wages instead of also
forwarding generic spending. Opening balance is not an income event. The host owns Bank,
loans, transaction success/free-money policy and debt handling. A failed or free TrySpend must
not enter the books.

On the existing day edge, retain the bank balance before `ParkFinances.OnDayRollover`. When
that returns a MonthEndResult, pass that before-balance, the result and advanced Calendar to
`score.RecordMonthEnd`. This records the scheduled combined loans+wages charge; do NOT also
forward that charge through `RecordSpending`. It samples balance/value, clears the next bank
slot, calls the existing visitor-history writer, then rotates annual totals. It changes no money
and advances no clock. Ordinary-day calls return false before querying world services. Like
`MonthRollover.Run`, this API requires the host to deliver each edge exactly once.

The existing game save host will continue to refuse its currently unsupported nonzero fields
until the user wires these methods. This change does not claim to change that executable host.
`ParkSaveHost.cs` and `SaveRecords.cs` only receive integration documentation pointers.

## §6 Validation and measurement with controls

`tools/audit_rating.py` executes original instructions on synthetic RAM using a small fail-closed
MIPS integer interpreter. It emits numbers and hashes, not game bytes. No console process or
other worktree is modified. Rating pool getters/upgrade leaf are executed; only its attraction
iterator is stubbed, with iterator mode **1** and the **nonzero visit count** asserted. Empty,
populated and saturated controls produce **0 / 62 / 100**; the populated fixture visits **4**
rides and the saturated fixture **22**. The first hand-total for the populated control was 60;
the harness rejected it. Re-adding its terms gives 19+6+2+6+8+7+14=62. The check was corrected,
not the binary or production formula.

The codec oracle executes the complete original pack AND unpack routines with actual Money
helpers. Five byte and six money fixtures cover month counts 0,1,14,144,289, mixed-sign samples,
nonzero adjacent byte **173**, and a populated word-overflow fixture. C# checks every emitted
byte and all **144** expanded cells per fixture. No empty-list success is counted. Tests explicitly
reject unchanged/lossless restoration. Known baseline controls and real test counts are also
required by the mutation runner.

`python3 tools/audit_rating.py --value` records a separate disagreement probe in
`rating-value-audit.json`: empty control is **0** with no price calls; populated control visits
**3 objects, skips 0**, and returns **1515 raw** for three stub prices of £101. Definitions
17/19/23 with levels 2/0/0 produce actual selector arguments **17/19/23**. The iterator, type
getter and price lookup are stubs; the original selector load, accumulation, Money conversion
and division run. This corroborates §0, not binary equivalence of the retained port contract.

The call census scans **266,327 aligned words / 1,065,308 bytes; skipped words 0, trailing bytes 0**.
It scans the whole image, including data: direct-call matches are candidates, not proof about
all indirect callers. It recovers rating's call at **0x80067100**. **Record files swept: 0; no
record sweep performed**, so there is no size-filtered price census or claim that records lack a flag.
Focused disassembly/field helpers were used to follow the listed functions, not as an exhaustive
negative proof. `rating-audit.json` records the image SHA-256, counts, fixtures and controls.

Final `dotnet test tests/TPW.Sim.Tests/`: **1,574 passed, zero failed/skipped** (1,544 existing
plus **30 new cases in 15 methods**). Every added method has a REJECTS comment; the explicit
comment check inspected 15 methods, skipped 0, found 0 missing comments.

Mutation sweep: **103/103 caught on the first sweep; survivors 0, invalid 0, skipped 0**.
The unchanged targeted control ran **46 tests** (30 new score tests plus 16 existing history
cases). Every mutant ran those same 46 tests and compiled successfully. The restored full
suite ran all 1,574. Production and test/fixture SHA-256 hashes were rechecked against the
JSON after source restoration; all match. The binary audit was rerun with identical fixtures
and passing controls. No `game/` changes; branch/worktree remain `rating` / `tpwport-rating`.

`tools/mutate_rating.py` compiles tests once against original production, mutates only production,
requires the same nonzero executed test count with no skipped tests, and counts a mutant only if
it compiled and real assertions failed. Sources restore in finally. `rating-mutations.json` retains
hashes, failure names, control counts, previous survivors and final full-suite status.

## §7 What was NOT established

- Resolution of the **level versus definition selector** finding; value intentionally retains the
  findings contract and is not advertised as binary-equivalent until adjudicated.
- Actual price tables or the maximum reachable park value across disc/scenario catalogues; no
  record sweep was performed and no prices are embedded in the sim.
- Native allocator contents of unwritten non-balance history cells, or provenance of unknown
  save-padding bytes. Their observed decoder use is established; why they contain a value is not.
- Live-console gameplay, original-card imports, rendering/UI text labels, or runtime timing
  equivalence. The instruction measurements use synthetic RAM and a documented iterator stub.
- Host wiring: automatic forwarding of transactions, exactly-once month edges, and replacing the
  game save host's refusals. `game/` was not edited or built.
- Class strike deadlines/flags, loan original principal/term/total-repayable metadata, admissions
  ownership, and other save-host gaps outside score/history. No new owner is claimed for them.
- Behavior for corrupt negative calendar/bank ages or invalid pool contents. Public negative-age
  requests are rejected; original out-of-bounds/corrupt-state behavior is not an API contract.
