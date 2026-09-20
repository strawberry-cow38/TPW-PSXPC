# Every writer of guest happiness — SLES-026.88, 2026-09-20

READ means instructions or data read from `TPW.BIN` loaded at `0x80010000`; GUESS is an inference
with a confidence. This report is the LEDGER for one byte, V+0x59: everything that moves it, by how
much and when; everything that reads it; and what it does not touch. needs.md is the clock for the
seven bytes around it and deliberately left this one to "the target of most of the above" (its §5);
behaviour.md §2.x has each writer in the state it belongs to. Where this report and an earlier
findings file disagree, §0 lists it and the CODE KEEPS THE EARLIER FILE'S VERSION.

Scripts: `findings/fable-scripts/fn.py ADDR`, `callers.py ADDR`, `fieldx.py OFF`. The scans behind
the word EVERY are in §6; read that before trusting the lists.

## 0. SOURCE DISAGREEMENTS (binary vs an existing findings file)

| # | findings file says | binary READ | address | code keeps |
|---|---|---|---|---|
| 1 | behaviour.md §2.5: type 5 "on purchase happiness += 10 × 0x8009279C(price) (sign)" | the sign is of the sideshow's slot-25 RETURN, which is `price − payout` (net), not of the price: the `jalr` at 0x8008EFC0 returns into s1 (0x8008EFD0), and 0x8009279C is called on s1 at 0x8008EFE8. A won prize larger than the play pays −10. | 0x8008EFBC..0x8008F00C | economy.md §4.3's reading (`sign(price − prize)`), which VisitorPurchase.PlaySideShow already ports; it is the binary's. Nothing to change. |
| 2 | transport.md §4.2: `arrival[slot] = max(0, guests_now − people[slot − 1])` | `people[slot − 1]` is fetched through the accessor 0x80066FA8(McAi, 1), whose guard is `sltu n, months` on the RAW n (0x80066F4C..0x80066F54) with months still un-incremented; so at the first TWO month changes the previous count is 0 and the row is the head count. From the third change on the formula holds. | 0x8006719C..0x800671C8 | new code (`ParkHistory.RecordMonth`) reproduces the guard; there was nothing older to keep. |
| 3 | transport.md §4.2 lists five rows and `months += 1`, nothing else in the writer | the writer also stores the rating at McAi+0x2F8 (`sb s5, 760(s3)`) when McAi+8 (year) ≠ 0 AND McAi+4 (month) == 0 — every January after the first. Readers 0x8006892C / 0x80069314 not followed. | 0x80067180..0x80067198 | an omission, not a contradiction; ported as `ParkHistory.RatingAtNewYear`. |
| 4 | behaviour.md §1: V+0x59 "**happiness** … GUESS-high" | the game's own word for this byte's park-wide mean is "Happiness": text 0x36B is the third row of the Visitor Information page (ids at 0x800E30F4, page 0x80082790), that row reads 0x80067020 → McAi+0x148, and McAi+0x148 is `Σ V+0x59 / guests` written by 0x800670D4 through 0x80092610. Same chain as needs.md §6 used for the icons. | 0x800E30F4; 0x80082964 + 0x14×2; 0x80067020; 0x800671CC..0x80067200 | the field was already `Happiness`; the label's status is now READ for the mean. A string is still a picture of the byte, not the byte. |
| 5 | needs.md §1: "Every one of the 111 stat references found by the scan goes through them" | there are **118** `jal` sites to the seven stat helpers in the two code blocks (§6 scan C), and the same conclusion holds for all 118. needs.md does not say what unit its 111 counts; the difference is most likely the unit, not a missed site, but the two numbers are not the same. | §6 | nothing; a count. |

Everything else this report touched re-read as behaviour.md / needs.md / litter.md / economy.md /
visitor-rest.md / debug.md have it: the ±15/10/5 bands and their 21/51 edges, the entertainer's 5,
the choice's 5 and 10 and its `< 8`, the failed route's `rand(15)`, the feature's −10 at `< 50`, the
influence 6/3/1, the litter 3, the five −1 thresholds, the leave `< 5` and exit-bubble `< 3`, the litter
roll `< 25 && rand(1000) < 100`, the bubble chain, the condition code's `> 75` / `< 25`, the window's
buckets, the shop bonus's 70 / 75, the purchase formulas and the constructor's 50.

**Independently re-read (tinyclaw, 2026-09-20):** the constructor at 0x8008C5B8..0x8008C5C8 sets
`a0 = V+0x59`, `v1 = 0x32`, `jal 0x800924F0` — while V+0x58 and V+0x5A on either side each take
their value from a `jal 0x800C2648` roll (0x8008C5CC, 0x8008C5E4). The "starts at 50, no die"
claim is visible in one window with both of its neighbours disagreeing with it. Disagreement #2's
guard also checks out: 0x80066F4C is `sltu v0,a1,a0` on the raw n against the month count.

## 1. The byte

V+0x59 is a signed byte in the run V+0x58..V+0x5F, clamped 0..100 by the helpers (Visitor.cs `Stat`
has the one-ended clamps). The visitor code never loads or stores it directly (§6 scan B): every
touch is one of add 0x80092190, sub 0x800921C0, set 0x800924F0, `<` 0x80092168, `>=` 0x80092178,
`>` 0x800921EC or get 0x80092650, called with `V + 0x59` in a0. Outside the visitor code the byte is
reached ONLY through two one-line wrappers, and those are how the park sees it:

| wrapper | does | callers (callers.py, READ) |
|---|---|---|
| 0x80092610(V) | `get(V+0x59)` | 0x80016AB4 statistic 46 (§4.4); 0x800670D4 the monthly writer (§4.3); 0x80070FA4 the save aggregate; 0x800814AC the Park Statistics window |
| 0x800924A0(V, b) | `set(V+0x59, b)` | 0x80091A3C the save loader, only |

It starts at **50** (constructor 0x8008C5B8..0x8008C5C8, `addiu v1, zero, 0x32`, the set helper —
no die) and a loaded guest gets `min + (rand(range+1) + rand(range+1)) / 2` from the save's spread
(0x80091ACC..0x80091AE4 via 0x8009197C; needs.md §7).

⭐ HAPPINESS HAS NO CLOCK. Nothing raises or lowers it for time passing. The two periodic passes that
touch it (§2 rows 9 and 10) are gated on what is on the guest's tile, and a guest standing still on a
clean, unremarkable tile with no need past its threshold keeps its happiness for ever. Compare need A
and need B (needs.md §2), which climb on a clock regardless.

## 2. Every writer (READ; addresses are the add/sub call and the delta's origin)

| # | when | Δ | condition | where | tunable word | ported in |
|---|---|---|---|---|---|---|
| 1 | constructor | := 50 | always | 0x8008C5B8..0x8008C5C8 (set) | literal 0x32 | `Visitor.Spawn` |
| 2 | save load | := min + roll | always | 0x80091ACC..0x80091AE4 (wrapper set) | — | not ported: no save format |
| 3a | target choice, nothing open scored | **−10** | no attraction with slot 86 set | 0x8008CF78..0x8008CF88 (sub) | literal 0xA | `VisitorDecision` |
| 3b | target choice, best found | **−5** | best score `< 8` (`slti v0, s2, 8` at 0x8008CF28), bubble 0x39 | 0x8008CF30..0x8008CF40 (sub) | literal 5 | `VisitorDecision`, `PoorChoiceScore` |
| 4 | message 2, path failed | **−rand(15)** = 0..14 | purpose has no arm (not 3/11/14/15/22); then boredom +rand(2), target := 0, state 5 | 0x8008FB50..0x8008FB64 (sub) | literal 0xF | `VisitorEntrance` message handler, `WanderHappinessLossMax` |
| 5 | unloading from a ride (types 1/3/6/7) | **+15 / +10 / +5** | `m = |pref − intensity|`: `sltiu 0x15` → < 21 pays [0x80103210]; `sltiu 0x33` → < 51 pays [0x8010320C]; else [0x80103208] | 0x8008F414..0x8008F480 (add) | 0x80103210 = 15, 0x8010320C = 10, 0x80103208 = 5 — the debug menu's "Ride Excellent/Good/OK Inc" (debug.md §3) | `VisitorQueue.RideHappiness` |
| 6 | unloading from a type-2 feature | **−10** | its capacity byte `< 50` after use (bubble 0x3C, nausea +10 beside it) | 0x8008F344..0x8008F350 (sub) | literal 0xA | `VisitorQueue`, `FeatureLowStockPenalty` |
| 7a | type-4 shop, on a SALE only | **+ d × (q − s/15) / 100** | kinds 0, 1, 4, 5, 7 (food and drink); d = record byte +0x34 (5 for every food/drink record on the disc, visitor-rest.md §1), q = quality slider shop+0x8A, s = second slider shop+0x7C; signed truncation | food 0x8008E98C..0x8008E9FC, drink 0x8008EAA0..0x8008EB10 (add through the spilled pointer 64(sp)) | — | `GuestSpending.HappinessGain(food: true)` |
| 7b | type-4 shop, on a SALE only | **+ d × q / 100** | kinds 2, 3, 6 (costume, balloon, gift); d = 15 / 10 / 15. ⚠ A balloon buyer already carrying one pays and gets nothing (0x8008EBE8 → 0x8008ED04 skips the add) | 0x8008EC60..0x8008ECFC (add) | — | `GuestSpending.HappinessGain(food: false)` |
| 8 | type-5 sideshow, on a PLAY only | **+10 × sign(price − payout)** | so a loss pays +10, a break-even 0, a prize worth more than the play −10 (§0 item 1). ⚠ The −10 goes through the ADD helper, which clamps the top only: the original byte can go below 0 here (Visitor.cs `Stat`) | 0x8008EFE8..0x8008F00C (add) | 0x8010320C = 10 | `VisitorPurchase.PlaySideShow`, `SideShowHappinessStep` |
| 9 | watching an entertainer to the end (state 28 exit) | **+5** | the entertainer left state 12 or the watch timer ran out | 0x80090BBC..0x80090BD4 (add) | literal 5 | `VisitorActivity`, `WatchHappiness` |
| 10a | needs pass, every 8 ticks staggered | **+6** | tile influence bit 1 ("pleasant"; which objects set it is not resolved, behaviour.md §2.9) | 0x8008FEEC..0x8008FEF8 (add) | literal 6 | `VisitorNeeds.Influence` |
| 10b | same pass | **−3** / **−1** | bit 4 ("unpleasant"): −3 in raw state 2 or 3 (walking), −1 otherwise; nausea +5 / +2 beside it | 0x8008FF0C..0x8008FF58 (sub) | literals 3, 1 | `VisitorNeeds.Influence` |
| 11a | needs pass, every 64 ticks staggered | **−3 per piece** | each litter object on my tile or an edge-neighbour (`|dx|+|dy| < 2`, needs.md §0 item 2); s3 = 3 set once at 0x80090194 and used for both this and the vomit nausea | 0x80090220..0x80090228 (sub) | literal 3 | `VisitorNeeds.LitterAndNeedPenalties`, `LitterHappiness` |
| 11b | same pass | **−1 each** | boredom ≥ [0x80103218], nausea ≥ [0x8010321C], V+0x5D ≥ [0x80103220], need A ≥ [0x80103224], need B ≥ [0x80103228]; five separate `>=` calls, five separate subtracts | 0x80090244..0x8009031C (sub ×5) | 95, 85, 90, 95, 85 — the debug menu's three "…/Happiness" sliders are 0x80103218/1C/20 | `VisitorNeeds.LitterAndNeedPenalties` |

That is the whole list. In particular, none of these move it: walking (tiredness does), queueing
(boredom and tiredness do), vomiting (nausea does), emptying rubbish at a bin, the turnstile, being
pelted or watched by a guard, a handyman, a mechanic, a breakdown, a ride closing under a guest, the
park closing, or a day or month passing. Staff have a byte of their own — S+0x40 morale,
`StaffBase.Morale` — and "reopening a ride pays a mechanic 10" is a write to THAT; it is not V+0x59
and is not in this ledger.

## 3. Sign, size, and addend versus multiplier

Every writer is an ADDEND through the add or sub helper; nothing multiplies or scales the byte
itself, and nothing sets it after the constructor and the loader. The largest single gain is +15 (a
well-matched ride, or a gift at quality 100: 15 × 100 / 100); the largest single loss is −14 (a
failed route rolling 14), then −10. The shop gains are small by data: with d = 5 for every food and
drink record and the quality slider capped at 100, a meal or drink pays at most +5, and the second
slider only trims it (q − s/15; at s = 100 that is q − 6). Gains are rows 5, 7, 8 (net > 0), 9 and
10a; losses are everything else. Row 8 is the only signed one.

The multiplication runs the OTHER way. Happiness is a multiplier INTO three things a guest computes:

- the shop want: `base × N / 100 × (happiness + 100) / 100`, where N itself carries
  `+ (100 − happiness) × d / 100` — misery raises N and lowers the multiplier, and the two pull
  against each other (0x8008E6FC..0x8008E810; GuestSpending.NeedFactor / Want);
- the sideshow want: `expected × (G + 100) / 100 × (happiness + 100) / 100` (0x8008EF30..0x8008EF48);
- the type-4 score bonus: `(happiness − thr) × 100 / (100 − thr)` at weight 3 when happiness > thr,
  thr = [0x80103258] = 70 for product kinds 2 and 3, [0x8010325C] = 75 for kind 6, nothing for the
  rest (0x8008CAD8..0x8008CB90).

The words that size the writers, all read from the image (0x801031FC..0x80103264, one table):

| word | value | sizes |
|---|---|---|
| 0x80103208 / 0C / 10 | 5 / 10 / 15 | ride reward bands (row 5); 0x8010320C doubles as the sideshow step (row 8) |
| 0x80103218 / 1C / 20 / 24 / 28 | 95 / 85 / 90 / 95 / 85 | the five penalty thresholds (row 11b) |
| 0x80103234 | 100 | the misery-litter roll's `rand(1000) < 100` (§4.1) |
| 0x80103258 / 5C | 70 / 75 | the shop-bonus thresholds (above) |
| 0x80103260 | 25 | the misery-litter happiness gate (§4.1) |

Everything else in §2 is an immediate in the instruction stream.

## 4. What happiness decides

### 4.1 The guest's own behaviour (all READ)

| test | effect | where |
|---|---|---|
| `< 5` | Idle resolves to leave (after tiredness ≥ 99, before money < £10 and the time roll) → SetState(38) | 0x8008D09C..0x8008D0B0 (`<` with 5); `VisitorIdle.LeaveHappiness` |
| `< 3` | on leaving, bubble 0x3A and sound (1, 0x11) — the "left unhappy" cue | 0x8008D10C..0x8008D120; `UnhappyExitHappiness` |
| `< [0x80103260] = 25` AND `rand(1000) < [0x80103234] = 100` | Idle roll 5 drops a litter piece out of misery (litter.md §3); the roll is only drawn when the gate passes | 0x8008D6B4..0x8008D6E0; `VisitorIdle.LitterHappiness` |
| `> 90` | bubble 0x31 + sound 0x15 (helper 0x8008FC68, `slti 0x5b`), third in the bubble chain after V+0x5D > 90 and nausea > 90 | 0x800904D0..0x800904F0 |
| `< 10` | bubble 0x35 + sound 0x12 (helper 0x8008FD64, `slti 0xa`) | 0x80090504..0x80090520 |
| `> 80` | bubble 0x3E, sound 0x14 or 0x16 the first time | 0x80090530..0x800905F4 |
| `> 25 AND < 75` AND `rand(10) == 0` | bubble 0x39, last in the chain, after tiredness > 90 | 0x80090630..0x80090660 |
| `> 75` | condition code 5 (icon 0x3E), tested after nausea, before tiredness | 0x80091CE8..0x80091CFC; `VisitorCondition` |
| `< 25` | condition code 8 (icon 0x35), the last test | 0x80091D18..0x80091D30 |

### 4.2 What the guest will pay, and what the stall remembers

The two wants above (§3) decide whether a shop or sideshow sells at all: `want > price` for a shop
(strict, 0x8008E81C), `want ≥ price` for a sideshow (0x8008EF70). Bought or not, both routines then
record the visit: `5 × (happiness now − happiness on entry)`, clamped at 0, is added to the stall's
running total at +0x80 and its visit counter (+0x84 shop / +0x7C sideshow) goes up by one
(0x8008ED1C..0x8008ED3C → 0x800B6FA0; 0x8008F024..0x8008F03C → 0x800B7940). The panel's "Customer
Satisfaction" bar (text 0x3FB; panel.md) is `min(100, total / visits)` (0x800B6F68..0x800B6F8C, `divu`
then 0x800B70BC = min). So a stall's satisfaction is literally five times the happiness its sales
paid, averaged over every visit including the ones that bought nothing — which is why a shop whose
product pays +5 (d = 5, quality 100) tops out at 25, and a gift shop at 75. That reading of the bar is
READ; "Customer Satisfaction" as the label is panel.md's.

### 4.3 The park's view: the monthly ring (READ, 0x800670D4)

Called from the McAi tick at 0x80066DEC when McAi+4 (month) changed, BEFORE `McAi+0x18 += 1` at
0x80066E04. With `slot = months % 144` (0x80067154..0x8006717C):

```
rating           = 0x8005B830()                                    ; 0x80067100, before the guest loop
count, Σh, Σt    = over 0x800536B4 / 0x800693BC: get(V+0x59), 0x80091DFC(V)   ; 0x80067114..0x8006714C
if year != 0 && month == 0: McAi+0x2F8 = rating                     ; 0x80067180..0x80067198 (§0 item 3)
people[slot]     = count                                           ; sb, McAi+0x28
arrival[slot]    = max(0, count − 0x80066FA8(McAi, 1))              ; sb, McAi+0xB8 (§0 item 2)
happy[slot]      = count ? Σh / count : 0                          ; sb, McAi+0x148  (signed div)
time[slot]       = count ? Σt / count : 0                          ; sb, McAi+0x1D8
overall[slot]    = rating                                          ; sb, McAi+0x268
```

The five accessors 0x80066FA8 / FE4 / 0x80067020 / 705C / 7098 read `ring[0x80066F44(McAi, n)]`, where
the index routine refuses `n ≥ months` (raw n, `sltu`), then treats n = 0 as 1, then wraps
`months % 144 − n` up by 144s. The Visitor Information page passes n = 0 (transport.md §4.1), which
is why the guard admits it from the first rollover. Ported as `ParkHistory`; the guard, the byte
stores and the two-month blindness are in its tests.

### 4.4 The park's other views

- **Park statistic 46** (0x80016AB4 case 46, 0x80016E90..0x80016EF8): `Σ get(V+0x59) / guests`
  (`divu`), 0 when there are no guests (the divide is skipped). The `sltiu 0x65` after it is dead: a
  mean of 0..100 bytes cannot exceed 100. Its only caller is the round-robin refresher 0x80016870,
  which compares statistics against 12-byte level records (shop-stock.md §7.6) — so an advisor line
  about happiness would be DATA-driven; the string table has none that names it (the "happy" strings
  are about staff, shops and balloons: 0x013, 0x22A, 0x365 and the like). `ParkHistory.MeanHappiness`
  is the same formula as the ring's row.
- **The Park Statistics window** (0x800814AC, 0x80081700..0x80081750): buckets every guest into
  `< 25` / `25..75` / `≥ 76` (`slti 0x4c`, `slti 0x19`) beside the condition-code icons (needs.md §6,
  `VisitorCondition`).
- **The save aggregate** (0x80070FA4, 0x80071038): min and max over guests, written as `min, max − min`
  (needs.md §7). Not ported.

### 4.5 What it does NOT feed (READ by absence)

- **The park rating** 0x8005B830 reads nine gp-relative manager words (0x80103854..0x80103884) and
  walks the attraction list (0x8006DCCC / 0x8006DD3C / 0x8006DE68 with 0x8009F5E0 per attraction);
  no guest is visited and 0x80092610 has no caller in it. "Overall Rating" on the same page as
  "Happiness" is independent of it.
- **The arrival score** (arrivals.md §3: 0x800672C8 → 0x80067400) — no call to the wrapper, no
  `0x59`; arrivals are computed from what is built, as that report says.
- **Which ride a guest picks**: the score 0x8008C818 reads happiness ONLY for the type-4 bonus (§3).
  Distance, type preference, the need lookups and the repeat penalties do not.
- **Ride wear, queue length, the turnstile verdict, wages, staff.**

## 5. What was ported

- `ParkHistory` (new): `RecordMonth` (0x800670D4), `SlotFor` / `Read` (0x80066F44 and the five
  accessors), `MeanHappiness` (statistic 46 and the Happiness row), `RatingAtNewYear` (McAi+0x2F8).
  Tests in `ParkHistoryTests`, each stating what it rejects.
- `Visitor.cs`: the `Happiness` doc comment carries the short ledger and the READ label; `Calendar.cs`:
  the `TotalMonths` caveat is resolved as READ (the increment is inside the month-changed branch).
- Every writer in §2 was already in the tree, in the class the last column names; no production
  behaviour changed. The sweep (§7) is the check that each of them is pinned.

## 6. Completeness — how EVERY was established

Four scans over both code blocks, `0x80010000..0x800C0498` (game code below the PsyQ library) and
`0x800E8000..0x800EF000` (the code after the data segment, where the pathfinder lives):

- **A. Pointer formation.** Every `addiu rX, rY, 0x59` with rY not zero/gp/sp: **27** sites — 25 in
  visitor functions (0x8008C534 ctor, 0x8008C818 score, 0x8008CDC8 choice, 0x8008D058 Idle,
  0x8008E5EC purchase, 0x8008EE78 sideshow, 0x8008F110 unload, 0x8008F880 messages, 0x8008FE60
  needs, 0x80090A50 state 28, 0x80091C28 condition) and the two wrappers. Two further
  `addiu a1, zero, 0x59` (0x800384CC → 0x800311F4, 0x8009C060 → 0x8001412C) are text/resource ids
  passed as arguments, not fields. The same scan at 0x51 (Person-relative, P = V + 8) finds nothing.
- **B. Direct access.** Every byte load or store with immediate 0x59 (`fieldx.py 0x59`, both blocks,
  and `addi` as well as `addiu`): **none**. The byte is never touched except through the helpers.
- **C. Helper calls.** Every `jal` to the seven helpers: **118** sites. For each, a0 was traced to
  its definition: 106 are `addiu` with 0x58..0x63, directly or through a register copy; the other 12
  are the purchase routine's stack spills (`sw t0, 48/52/56/64(sp)` from `addiu t0, fp, 0x5e/0x5a/
  0x5b/0x59` at 0x8008E5F8..0x8008E668, reloaded with `lw a0, N(sp)`), the sideshow's s7
  (`addiu s7, s6, 0x59` at 0x8008EE90) and the needs pass's s4 (`addiu s4, s2, 0x5a` at 0x80090158),
  resolved by hand. **36** of the 118 reach 0x59: 1 set, 15 add/sub, 20 compares and gets. Every one
  is in §2 or §4.
- **D. The wrappers' callers** (`callers.py 0x800924A0 0x80092610`): five, all in §1's table; no data
  word (vtable entry) points at either.

What these scans would miss, honestly: (a) a pointer built by register arithmetic (base + index)
rather than an immediate — the one loop that walks the stat run, the save aggregate 0x80070FA4,
does not do this (it calls a per-stat wrapper for each byte, 0x80071020..0x80071060, and scan D saw
it); (b) code outside `TPW.BIN` — the overlays (`TPW.OVL`, the debug menu in ovl2) could write V+0x59
directly and this image would not show it; the debug sliders read so far write the tunable WORDS in
§3, not the byte (debug.md §3); (c) a write through the Person base with an offset other than 0x51
if the Person is not at V + 8 — behaviour.md §1 is the source for +8, and no other small offset in
0x50..0x5F resolves to happiness in any visitor function. Within `TPW.BIN` the list is closed.

## 7. Sweep

`tools/mutate_happiness.py`, results in `happiness-mutations.json`. See that file for the counts;
the paragraph below is written from it.
