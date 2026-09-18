# Theme Park World (PSX, SLES-026.88) — the ECONOMY

Companion to `ai_out.txt` (state map) and `behaviour.md` (state behaviour). Everything here is read
from `TPW.BIN` loaded at 0x80010000. Same legend: **READ** = taken from the instructions at the quoted
address; **GUESS** = my interpretation, with confidence. Helper scripts beside this file: `ann.py`,
`fn.py`, `leaf.py`, `census.py`, plus two new ones: `xref.py ADDR…` (every lui/gp access forming an
address) and `callers.py ADDR…` (every `jal` to an address, plus data words holding it).

Units used throughout: **1 tick = 1/25 s (0.04 s)**. **1 game day = 99 ticks = 3.96 s** (measured; the
formula is in §2). A month is 28–31 days, a year 365 days.

## 0. Corrections to the earlier reports — read these first

1. **`ai_out.txt` §3 step 5 calls 0x80067DF0 "the daily loop". It is MONTHLY.** READ at
   0x80066C88..0x80066C98: 0x80066C50 (run every unpaused tick from 0x80058E24) saves `McAi+4`, calls
   the calendar 0x80066EA8, and only if `McAi+4` **changed** calls 0x80067DF0 and then the bank
   rollover 0x80086D70. `McAi+4` is the **month** (§2). So strike evaluation, wages and loan
   repayments all happen once per game month (≈111–123 s), not once per day.
2. **`behaviour.md` §3.5 calls 0x8009C56C(ride,0) "servicing". It is a PAID UPGRADE.** READ at
   0x8009C588..0x8009C610: if `ride+0xF6 < 3` it increments it, takes a price from the ride's
   definition record for the new level (0x8009F470: `rec + 0x34×level + 0x50`) and charges it through
   TrySpend. So a mechanic finishing state 54 raises the ride one level and the park pays for it.
   Consequently **`ride[0xF6]` is the upgrade level 0..3**, not "queue path length in tiles" — the
   queue cap `4×ride[0xF6]+7` (behaviour.md §2.4) grows with upgrades.
3. **`behaviour.md` §2.5: "base = 0x800B6CBC (a cost-derived number ×1.25)".** It is the **unit cost**:
   `rec+0x2E × (75 + (shop+0x8A >> 2) − (shop+0x7C / 4)) / 100` (READ, 0x800B6CF0..0x800B6D34).
   And slot 25 does not "book profit"; it books **price − unit cost** to the bank and books a
   **loss** when the price is below cost (§4.2).
4. `behaviour.md` writes "finance object 0x80086814". 0x80086814 is **GetBank()** (READ: `lw v0,
   2920(gp)` = 0x801031BC, lazily creating the object); the object itself is on the heap (§1).
5. Refinement, not a correction: `behaviour.md`'s "slow clock" `McAi+0x10` is **total days elapsed**
   (READ, incremented once per day at 0x80066EFC). So the guest "time-in-park ≥ 81" leave rule is
   81 game days ≈ 5.3 minutes, and staff wage pro-rating counts days.

Everything else in both earlier reports that I touched (turnstile fee path, purchase routines, the
Money representation, the strike chain) held up.

## 1. The money address — settled

### 1.1 Money representation (READ)
A `Money` is one signed 32-bit word in **tenths of a pound**. Constructors: 0x80088E50(&m, pounds,
pence) = `pounds×10 + pence/10`; 0x80088E88(&m, pounds) = `pounds×10`. Helpers: += 0x80088DFC,
−= 0x80088DE0, a−b 0x80088D60, a/b 0x80088CDC, a≥b 0x80088DB8, a>b 0x80088DD0, to-pounds
0x80088E18 (/10). The same class is duplicated at 0x80092740.., 0x80069488.., 0x8001D348.. etc. for
other compilation units; every one is `×10` on the way in.

**So £50,000 is stored as 500000 (0x7A120), not 50000.**

### 1.2 The BANK object (READ)
- Created lazily by **GetBank() = 0x80086814**: `lw v0,2920(gp)`; if zero, 0x80086818 allocates
  **0x12F4 bytes** through 0x800C0BA8 with the tag string "BANK" (0x801031C4), stores the handle at
  gp+0x801031C0 and the **object pointer at 0x801031BC**, then runs the constructor 0x80086560.
- **The authoritative park balance is `BANK+4`.** Written only by 0x800868B0 (set), 0x80086980
  (Income), 0x800868B8 (TrySpend), and the save-loader 0x80088A0C. Read by 0x800868A0(&out, bank),
  whose callers are the HUD/UI (0x80016AB4 ×2, 0x800390C8, 0x8004F360, 0x80064558, 0x8007F820,
  0x800830CC …) and the afford checks.
- Start of a park (READ, 0x80058A94..0x80058AB8 in the game-mode init 0x800588D0):
  `balance := Money(50000, 0)` = **500000**. The constructor's own default is Money(10000) = 100000.

Full layout (all READ from the constructor 0x80086560, the rollover 0x80086D70 and the accessors):

| offset | meaning |
|---|---|
| +0x00 | entry fee (Money). ctor default Money(40) = £40. Get 0x80087248, set 0x80087240 |
| +0x04 | **balance** (Money) |
| +0x08 | "may go negative" flag, ctor sets 1 → TrySpend never refuses (§1.4) |
| +0x0C..+0xBB | 4 loan slots × 0x2C bytes (§4.5) |
| +0xBC | [144] Money: balance at each month end |
| +0x2FC | [144] Money: income per month (all sources) |
| +0x53C | [144] Money: entry-fee income per month |
| +0x77C | [144] Money: type-4 shop profit per month |
| +0x9BC | [144] Money: type-5 game takings per month |
| +0xBFC | [144] Money: spend per month (all) |
| +0xE3C | [144] Money: wages per month |
| +0x107C | [144] Money: park value snapshot per month (§3 step 6) |
| +0x12BC | month index (int); histories are indexed `idx % 144` (12 years) |
| +0x12C0 | loan repayments this month |
| +0x12C4 | type-5 takings, all time |
| +0x12C8 | entry fees, all time |
| +0x12CC | type-4 profit, all time |
| +0x12D0 | wages, all time (also += sacking pay-offs) |
| +0x12D4 | spend, all time |
| +0x12D8 | income, all time |
| +0x12DC / +0x12E0 | income this year / last year |
| +0x12E4 / +0x12E8 | spend this year / last year |
| +0x12EC / +0x12F0 | park value / balance, snapshot every 12th month |

### 1.3 What 0x801D5694 and 0x801D56AC actually are (GUESS-high, with a one-read verification)
Neither is the balance. Two facts pin it:
- The balance word holds **500000** for the £50,000 you see, so a memory search for 50000 cannot hit it.
- The loan constructor 0x80086268 stores the raw pound figure **50000** into loan slot 1 at offsets
  +4 (principal), +0x14 (remaining) and +0x1C (READ, 0x800862D4..0x800862EC). Loan slot 1 starts at
  BANK+0x38, so those are **BANK+0x3C, BANK+0x4C, BANK+0x54**. The only pair among them exactly 0x18
  apart is **BANK+0x3C / BANK+0x54**, which matches 0x801D5694 / 0x801D56AC if **BANK = 0x801D5658**
  — and then the real balance is **0x801D565C**.
- That is also why poking them changed nothing: loan slot 1's fields are only ever read when you take
  loan 1 (0x800871B8) or when the rollover repays it.

**Verify in one read:** the word at **0x801031BC** is the BANK pointer. Expect 0x801D5658. Then
`BANK+4` should read 500000, and writing e.g. 1234560 there should make the HUD show £123,456 (the
HUD reads it through 0x800868A0 every frame; nothing caches it).

Two other things a memory hunt will trip on:
- There are **two objects tagged "BANK"**. The research singleton (0x8009B384, pointer at gp
  0x80103308) uses the tag string at 0x8010330C. It holds research points, not money.
- The day counter you found at 0x801E8B60 is inside **McAi** (pointer at gp **0x80102E48**, 0x328
  bytes, tag "McAi"). If McAi = 0x801E8B54 it is `McAi+0xC` = day-of-month (wraps at 28..31); if
  McAi = 0x801E8B50 it is `McAi+0x10` = total days. Both are 32-bit words. §2 has the layout.

### 1.4 The two primitives every flow goes through (READ)
- **Income(bank, m) = 0x80086980**: `balance += m; +0x12D8 += m; +0x12DC += m; income[idx] += m`;
  then if balance ≥ 0, `McAi+0x14 := 0` (0x80088CD4 — resets the months-in-debt counter, §3).
- **TrySpend(bank, m) = 0x800868B8 → bool**: if the global at gp **0x801031CC** is nonzero, return 1
  without charging (a free-money switch; **nothing in TPW.BIN writes it** — GUESS: cheat/tutorial
  flag set by the overlay). Else if `BANK+8 == 0` and `m > balance` return 0. Else `balance −= m;
  +0x12E4 += m; +0x12D4 += m; spend[idx] += m; return 1`. **Since the constructor sets BANK+8 = 1,
  TrySpend never refuses in this build** — the park simply goes negative. Affordability is checked
  separately by the UI before it calls (e.g. 0x8001C22C for placement, 0x800857B4 for hiring).
- **Typed income 0x80086A5C(bank, attractionType, m)**: Income(m), then type 4 → `+0x77C[idx] += m,
  +0x12CC += m`; type 5 → `+0x9BC[idx] += m, +0x12C4 += m`; any other type → nothing extra (jump
  table 0x800E3588). Only the two shop sell routines call it.

## 2. The calendar — ticks → days → months → years (READ, 0x80066EA8)

McAi layout (pointer at gp 0x80102E48): `+0` sub-day accumulator, `+4` **month 0..11**, `+8`
**year**, `+0xC` **day-of-month 0..N−1**, `+0x10` **total days**, `+0x14` consecutive months in debt,
`+0x18` total months elapsed.

Each unpaused game-mode tick (0x80058E24 → 0x80066C50 → 0x80066EA8):
```
McAi+0 += timescale                      # timescale = word 0x80103A90 (0x800BDD0C returns it)
if McAi+0 >= 0xF0000 (983040):           # READ: sltu against 0xEFFFF, then reset to 0
    McAi+0 = 0; day++; totalDays++
    if day >= monthLen[month]:           # table 0x800E1554 = 31,28,31,30,31,30,31,31,30,31,30,31
        day = 0; month++
        if month >= 12: month = 0; year++
```
- **Day length = ceil(983040 / timescale) ticks.** Your measured 99 ticks means timescale is in
  9930..10030; GUESS-high it is 10000. The timescale is written at 0x800BDE88/0x800BDE98 from a
  frame-time source (0x800BC290) and clamped to ≤ 0x4000 — so a dropped frame makes that tick worth
  more calendar time, but the day still cannot be shorter than 61 ticks.
- **Month = 28–31 days = 2772–3069 ticks = 110.9–122.8 s.** Year = 365 days = 36135 ticks = 24.1 min.
- After the calendar step, 0x80066C50 (READ): if month changed → §3. Then `McAi+0x18++`; if year
  changed → 0x80087570 (rotate this-year/last-year totals, zero this-year). Then, separately, if the
  day changed and `day % 7 == 0` (day-of-month 0, 7, 14, 21, 28) → 0x80067928, the **objectives /
  award check** (compares park stats and the bank's net position from 0x800873D8 against thresholds
  in the level record; posts messages 0xAF–0xB6, 0xBC). It moves no money.

There is **no per-day accounting**. The day rollover itself only drives the weekly objectives check
and the guests' day-based timers. All money on a schedule moves at the **month** rollover.

## 3. The MONTH rollover — 0x80086D70(bank), called from 0x80066CC0 (READ)

Runs once when `McAi+4` changes, i.e. every 2772–3069 ticks (110.9–122.8 s), after the staff-strike
evaluation 0x80067DF0 (which touches no money — checked: zero bank calls). Let `i = BANK+0x12BC`.

1. `balanceHist[i % 144] := balance` (BANK+0xBC array).
2. `BANK+0x12C0 := 0`. For each of the 4 loan slots whose "available" flag (+0x28) is **0** (= taken):
   `pay = min(slot+0x18 monthly payment, slot+0x14 remaining)` (0x80088EC0); `remaining −= pay`;
   `monthsLeft(+0x10) −= 1`; `BANK+0x12C0 += pay×10`; `due += pay×10`.
3. `wages = 0x80086B60()` = Σ over the five staff lists (0x80053720, 0x80053768 entertainers,
   0x800536D8 guards, 0x80053744, 0x800536FC) of `Money(0x80094E3C(staff+8), 0)`. `due += wages`;
   `BANK+0x12D0 += wages`; `wagesHist[i] := wages`.
4. **`TrySpend(bank, due)`** — one charge for loans + wages (0x80086EC8). With BANK+8 = 1 this
   always succeeds and can drive the balance negative.
5. `Income(bank, 0)` (0x80086ED4 with a zero Money) — a no-op except its side effect: if the balance
   is ≥ 0 it resets `McAi+0x14`.
6. `value = 0x80087884()` = `(Σ over every attraction of defPrice(type, level)) / 2` where defPrice
   is 0x8006AD58 (§4.6) — i.e. **half the purchase price of everything built**. `valueHist[i] :=
   value`. **It is recorded, never charged.** There is no upkeep.
7. If `i != 0 && i % 12 == 0`: `BANK+0x12EC := value; BANK+0x12F0 := balance` (yearly snapshot).
8. `i += 1`; zero `incomeHist, feeHist, type4Hist, type5Hist, spendHist, wagesHist` at the new
   `i % 144`.

Then back in 0x80066C50 (READ, 0x80066CC8..0x80066DEC): `bal = balance`; if `bal < 0`:
`McAi+0x14 += 1` and post a message by count — 1 → 0x89 if any loan is outstanding (0x8008748C =
Σ remaining) else 0x8A; 3 → 0x8B; 5 → 0x8C; 6 → 0x8D (via 0x800693C8(msg, 2)). If `bal ≥ 0`:
`McAi+0x14 := 0`. I did not find a "bankrupt → game over" transition; whether 0x8D is terminal is
handled elsewhere (not traced).

### 3.1 Wage per staff member per month — 0x80094E3C(person) (READ)
```
if state == 15 (Striking): wage = 0
level  = P+0x3C & 7                              # 0..4, set by the hire panel via 0x8009563C
base   = [50, 55, 65, 80, 100][level]            # table 0x800E162C, pounds
mult   = [3, 1, 1, 2, 3][rec+0x10]               # table 0x800E1640; rec = P+0x30 = definition record
daysEmployed = McAi+0x10 − P+0x34                # P+0x34 = total-days at hire (0x800941C0/0x80097BA0)
pct    = min(100, 100 × daysEmployed / monthLen[month−1])   # 0x80069314: length of the month just ended
wage   = base × mult × pct / 100                 # pounds; Money() makes it ×10
```
GUESS-medium: `rec+0x10` is the staff type index 0..4 (the multipliers 3/1/1/2/3 then belong to
types in the slot-17 order Entertainer/Mechanic/Guard/Researcher/Handyman minus one, but I have not
read a record, so do not trust the assignment — only the table). Pro-rating means a worker hired
mid-month costs a fraction that month and a full month thereafter. **Striking staff draw no pay.**

## 4. Every flow of money

Frequencies: "event" means it fires when the game event happens, not on the clock. The turnstile
admits at most one guest per 32 ticks per lane (behaviour.md §2.6), so **entry fees arrive at most
2 per 1.28 s ≈ 6 per game day**.

### 4.1 Entry fee — IN (READ)
- Where: guest state 37 handler 0x80090EDC; fee read 0x80087248; verdict 0x80090D5C; booking
  **0x80087258**: `balance += fee; +0x12C8 += fee; feeHist[i] += fee` (and Income's totals via
  0x80086980). Guest: `money −= fee` (Money −= at 0x80090F..). Refused only when the verdict is −2
  (guest leaves via state 38).
- Amount: `BANK+0` in tenths. Default £40 (ctor). **Scenario override:** 0x80071F2C sets it from the
  level record's u16 at +2 (pounds ×10) when the park loads. **Player range £0–£1000**: the fee
  panel 0x800814AC creates its slider with (min 0, max 1000) at 0x80081668 and 0x80081E40 writes
  `Money(slider, 0)` back through 0x80087240.

### 4.2 Shop sale, type 4 (walk-in shops) — IN, occasionally OUT (READ)
- Where: guest state 22 → 0x8008E5EC (§5) → shop vtable slot 25 = **0x800B69E0(shop)**.
- Amount to the bank per sale: **`(price − unitCost) × 10`** via typed income (type 4). If
  `price < unitCost` the difference is charged with TrySpend instead (a loss per sale). Also
  `shop+0x8C += price`, `shop+0x90 += price − unitCost` (per-shop takings and profit, pounds).
- `price` = u16 `shop+0x88`, pounds (§6.1). `unitCost` = 0x800B6CBC =
  `rec+0x2E × (75 + (shop+0x8A>>2) − (shop+0x7C/4)) / 100`, where `rec` is the shop's definition
  record (disc data), `shop+0x8A` and `shop+0x7C` are two player-set sliders on the same panel
  (setters 0x800B706C / 0x800B70B4; GUESS-medium: quality and a discount/research modifier).
- The guest pays `price × 10` (§5).

### 4.3 Sideshow / game, type 5 — IN and OUT (READ)
- Where: guest state 22 → 0x8008EE78 → slot 25 = **0x800B7524(shop)**.
- `price = shop+0x84` (u16 pounds); `prize = shop+0x78` (int pounds); `chance = shop+0x86` (u16 %).
  Bank: typed income (type 5) of **`price × 10`** — the full price, no unit cost. Then if
  `rand(100) < chance`: `shop+0x88 += 1` (wins), `shop+0x8C += prize`, **TrySpend(prize × 10)**.
  `shop+0x90 += price`. Returns `price − prize`; the guest pays that (net of the prize) and gets
  happiness `±10` by its sign (0x8010320C = 10). Where +0x84/+0x86/+0x78 are set was not traced
  (GUESS: from the definition record at load).

### 4.4 Staff — OUT
- **Wages**: monthly, §3 step 3 / §3.1.
- **Hiring** 0x80085728 (READ): level from the panel's table 0x8010A1F4 (runtime-filled) → 0x8009563C;
  cost = **0x80094DC0** = `[250, 275, 325, 400, 500][level] × [3, 1, 1, 2, 6][rec+0x10]` pounds
  (tables 0x800E1654 / 0x800E1668) → TrySpend(×10). Afford check is separate (0x800857B4 compares
  balance to the same number).
- **Sacking** 0x800952B4 (READ; staff vtable slot 57, e.g. 0x800E4104): pays the wage accrued so far
  this month by the §3.1 formula — `BANK+0x12D0 += w; TrySpend(w)` via 0x8008717C.
- **Strike**: strikers cost nothing while striking (§3.1). Strike evaluation is monthly (§0.1).

### 4.5 Loans — IN once, OUT monthly (READ)
Four fixed slots, constructor 0x80086268(slot, i). Fields: +0 index, +4 principal (£), +8 term in
months, +0xC annual rate %, +0x10 months left, +0x14 remaining (£), +0x18 monthly payment (£),
+0x1C = principal copy, +0x24 total repayable, +0x28 available flag.

| slot | principal | term | rate | monthly payment (computed by 0x8008634C) |
|---|---|---|---|---|
| 0 | £100,000 | 3 mo | 20 % | `100000 × 1.20^(3/12) / 3` ≈ £34,888 (total ≈ £104,664) |
| 1 | £50,000 | 2 mo | 20 % | `50000 × 1.20^(2/12) / 2` ≈ £25,771 |
| 2 | £25,000 | 2 mo | 20 % | ≈ £12,886 |
| 3 | £10,000 | 2 mo | 15 % | `10000 × 1.15^(2/12) / 2` ≈ £5,118 |

Payment formula (READ, 0x8008634C): `total = principal × pow(1 + rate/100, term/12)` in 20.12 fixed
point (pow = exp(ln·) at 0x800BEF28 using PsyQ `cln`; GUESS-high on the rounding), `payment =
total / term`, `+0x24 = payment × term`. **Taking a loan** 0x800871B8(bank, i) (from the loan panel
0x80080380, item index − 1): only if the flag is 1; sets it 0; `Income(principal × 10)`. **The flag
is never set back to 1** (callers of 0x80088EAC: constructor and take-loan only) — each loan can be
taken exactly once per park. Repayment: §3 step 2, monthly, interest already inside the payment;
no separate interest flow exists.

### 4.6 Construction, demolition, paths and scenery — OUT / IN (READ)
- **Attraction price** = 0x8006AD58(mgr, type, variant): for types 1,3,6,7 (queue rides) the
  definition record's `rec + 0x50` (per-level entry 0, 0x34 bytes each); for types 2,4,5,8 `rec +
  0x20`. Records come from the disc (0x8006A3CC → 0x800310C8 loads them), so **the numbers are not in
  TPW.BIN** — read them from FOLIO.GAZ. Pounds.
- **Placing** an attraction: 0x8001C454 stores that price in the placer (+8); 0x8001C22C sets
  "affordable" if `balance − price×10 ≥ 0` (or unconditionally if gp 0x801031CC is set);
  **0x8001C2E0 charges TrySpend(price × 10)** on confirm.
- **Demolishing** 0x8006292C: `Income((price >> 1) × 10)` — **refund half** the level-0 price
  regardless of upgrades.
- **Upgrading** (§0.2): `TrySpend(rec[level].+0x50 × 10)` for level 1..3, triggered by a mechanic
  finishing state 54 (0x80096AA4) or the manager path 0x8005C2F4 (0x8005C388).
- **Paths / scenery / terrain (type 8)**: tool init 0x80022B1C stores `defPrice(8, kind)` (rec+0x20)
  in the tool (+8); the piece tools accumulate `count × unit` (+0x14, set at 0x80021730 /
  0x80021FF8 / 0x800228E4 / 0x80022C7C) and **charge on confirm** (0x800229E4 → TrySpend at
  0x80022AF4) or **refund with Income on cancel/undo** (0x80020254, 0x8002037C, 0x800226C8,
  0x80022764, 0x80022818, 0x800229E4 @ 0x80022A58). GUESS-high on which branch is confirm vs cancel;
  READ on the amounts being count × unit price.

### 4.7 Things that do NOT move money (READ, by exhaustion of the 41 GetBank call sites)
- **Ride tickets: none.** No ride class calls GetBank, and only four visitor functions touch guest
  money (§5). Rides are free to ride in this build; their +0x88 word is saved/restored (0x8009CDD8 /
  0x8009CEFC) but never read by ride logic.
- **Upkeep / running costs: none** (§3 step 6 records value only).
- **Research: no money.** No GetBank caller in 0x8009B000..0x8009C000; the research "BANK" is points.
- **Fines, interest, repairs, restocking: none.** Every TrySpend caller is listed in §4 (placement,
  paths, hiring, sacking, upgrade, the two shop sells, the rollover).

## 5. Guest spending (READ; stat names as in behaviour.md)
- Money lives at `V+0x48` (a Money): start **£200 + rand(300)** → 2000..4990 units (ctor 0x8008C5FC).
  Only these touch it: the ctor, Idle's leave check 0x8008D058 (**leave when < £10** = 100 units),
  the entry fee 0x80090EDC, the type-4 purchase 0x8008E5EC, the type-5 purchase 0x8008EE78, and two
  accessors (0x800923E0 set, used by the save-loader; 0x8009252C get, used by the guest info panel).
- **Type-4 buy** (0x8008E814..0x8008E88C): buys iff `want > price` **and** `money ≥ price×10`,
  where `want = unitCost × N × (happiness+100)/100 / 100` with N the need-weighted factor in
  behaviour.md §2.5. Then `money −= price×10`, rubbish += 30+rand(25), product effects, and the
  "value verdict" `(want − price)/2` → bubble 0x38 / 0x36.
- **Type-5 play** (0x8008EF8C..0x8008F00C): iff `money ≥ price×10`; pays `(price − prize)×10`;
  happiness `+10 × sign(price − prize)`.
- **Entry**: pays iff `money > fee` and verdict ≥ −1 (behaviour.md §2.6).
- Drains are only those three; nothing refills a guest. With £200–£499 and a £40 gate a guest can
  afford roughly 4–11 £40 purchases before the £10 floor makes it leave.

## 6. Prices
### 6.1 Shop price (type 4) — player set (READ)
`shop+0x88`, u16, **pounds**. Default from the definition record `rec+0x2C` (0x800B68A0 at
0x800B691C). Restored from the save record's +0xC (0x800B6BB8). **Slider 1..500** (panel
0x8007A734 creates it with (1, 500) at 0x8007A82C; 0x8007A880 writes it back through 0x800B7140).
The same panel's two other controls write `shop+0x8A` and `shop+0x7C` (unit-cost modifiers, §4.2).
### 6.2 Game price (type 5)
`shop+0x84` u16 pounds, prize `shop+0x78`, win chance `shop+0x86` %. Setters not traced.
### 6.3 Entry fee
`BANK+0`, default £40, scenario u16 at record+2, **slider 0..1000** (§4.1).
### 6.4 Rides
No ticket price exists (§4.7). Purchase/upgrade prices are the disc record's per-level `+0x50`.

## 7. Frequency summary

| flow | trigger | ticks | seconds | per game day (99 ticks) |
|---|---|---|---|---|
| entry fee | guest through turnstile | ≤ 1 per 32 per lane | ≤ 1 per 1.28 s per lane | ≤ ~6 |
| shop sale / game | guest state 22 at a type 4/5 | event | event | depends on guests |
| wages + loan repayments | month change | 2772–3069 | 110.9–122.8 | once per 28–31 days |
| year rotate (no money moves) | year change | 36135 | 1445 | once per 365 days |
| objectives check (no money) | day-of-month 0,7,14,21,28 | 693 | 27.7 | every 7 days |
| build / demolish / hire / sack / upgrade / loan | player or mechanic action | event | event | — |

## 8. What I could not establish (honest list)
- The actual pound figures for every attraction, upgrade level, path and scenery piece, shop default
  price and unit cost: they are in the disc definition records (FOLIO.GAZ), reached via 0x8006A3CC /
  0x800310C8. Offsets are given above; values are not in TPW.BIN.
- Which staff type has which `rec+0x10` index (the 3/1/1/2/3 and 3/1/1/2/6 multipliers).
- Who sets the free-money flag 0x801031CC and the type-5 fields +0x84/+0x86/+0x78.
- Whether six months of debt (message 0x8D) ends the game.
- The heap addresses themselves (BANK = 0x801D5658, McAi = 0x801E8B5x) are inferred from your two
  observations plus the constructor; one read of 0x801031BC / 0x80102E48 confirms or refutes them.
