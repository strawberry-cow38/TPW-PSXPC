# Theme Park World (PSX, SLES-026.88) — STAFF WAGES: what the screens show, what commits a hire, where staff live

Tags: **SOURCED** = read off instructions at the quoted address. **MEASURED** = read off a RAM dump
(`.oracle_park_idle/ram_000600.bin`, day 123 of the idle park) with `fable/w/staffdump.py`. **DERIVED** =
inferred, from what is stated. **UNMEASURED** = guess. Scratch: `fable/w/`.

## 0. Answers first

1. **The £100 is right and the formula is right; economy.md's TYPE MAPPING was wrong, and the screen you
   opened is the HIRE OFFER, which prices the recruit at the record's default level.** Offer wage =
   `base[rec+0x14] × mult[rec+0x10]` = `50 × 2` for Gary Liddon (guard record, default level 0). "Pay Grade"
   on that screen = `rec+0x14 + 1`, i.e. level 0 shows as Pay Grade 1. `level` (P+0x3C & 7) = Pay Grade − 1
   everywhere. (SOURCED §2, MEASURED §2.4.)
2. **X on the offer does not hire. It starts the placement tool (state 1), which creates the staffer from
   the pool and hangs them on the cursor. A second X in the world commits; triangle destroys them.** Your
   four triangles cancelled the hire. (SOURCED §3.)
3. **Employed staff live in five pools** whose pointers sit at `0x80103864..0x80103874`; `pool+8` is the
   employed list, `pool+0xC` the count, person = node+8. (SOURCED + MEASURED §4.)
4. **Deliverable poke:** write `0x00000000` to `0x8001E754` (currently `0x10400040`). That nops the
   tile-validity branch in the placer's confirm, so after X on the offer, *any* X in the world commits the
   hire. One word, no hold needed. (SOURCED §5.1.) A pure-data relink is possible but UNMEASURED (§5.3).
5. **Nothing else gates wages.** The sandbox flag is not read by the tick, the rollover or the wage
   function. Hiring is free (TrySpend of £0). An un-dropped recruit on the cursor IS paid; a cancelled one
   is not. (SOURCED §6.)
6. **Falsifier:** from the idle-park state (May, day 3, totalDays 123), hire Gary Liddon and drop him:
   at the first rollover `BANK+0x12D0` = **900**, `hist_wages[4]` = 900, balance = **499100**; every
   later rollover adds **1000**. Full table in §7.

## 1. Corrections to economy.md (read before trusting §3.1 / §4.4 there)

- **§4.4 "Hiring 0x80085728 … cost 0x80094DC0 = [250..500]×[3,1,1,2,6]" is TRAINING, not hiring.**
  0x80085728 is the Purchase handler of the *Training* card (Staff Information → employee → "Training",
  text 0x36E). It raises the level by one and charges `hireBase[newLevel] × hmult[kind]`. Real hiring
  charges nothing (§3.3). (SOURCED)
- **§3.1 "rec+0x10 = slot-17 type − 1 (Entertainer/Mechanic/Guard/Researcher/Handyman)" is wrong.**
  `rec+0x10` is the staff KIND, and the kind→pool mapping is **0 Mechanic, 1 Entertainer, 2 Cleaner
  (Handyman), 3 Guard, 4 Researcher** (SOURCED three ways: the placer's create switch 0x800DC5A4 →
  creators 0x800516F0/0x80051900/0x800517A0/0x80051640/0x80051850; the confirm's "pool full" message
  switch 0x800DC5BC → counters 0x800537F8/0x80053840/0x800537D4/0x8005378C/0x8005381C; and each pool's
  init function passing its kind to the record lookup 0x80069EF4 — 0x80096408→0, 0x80095BAC→1,
  0x800989C4→2, 0x80097784→3, 0x800998C4→4). So the multipliers are:

  | kind | staff | wage mult (0x800E1640) | training mult (0x800E1668) |
  |---|---|---|---|
  | 0 | Mechanic | 3 | 3 |
  | 1 | Entertainer | 1 | 1 |
  | 2 | Cleaner | 1 | 1 |
  | 3 | Guard | 2 | 2 |
  | 4 | Researcher | 3 | 6 |

- The wage formula itself (economy.md §3.1) holds: `wage = floor(pct × base[level] × mult[kind] / 100)`
  pounds, `pct = days < monthLen ? floor(100×days/monthLen) : 100`, `days = McAi+0x10 − P+0x34`,
  `monthLen` = the month that just ended (0x80069314), `base = [50,55,65,80,100]`, striking (state 15)
  → 0. Money() makes it ×10. (SOURCED 0x80094E3C, re-read.)
- economy.md §1.3's guess **BANK = 0x801D5658** is MEASURED correct (idle dump), and McAi = 0x801E8B50.

## 2. Three staff screens, three different numbers under "Monthly Wage"

### 2.1 The HIRE OFFER (what you opened): laptop → Build & Hire → Hire → type → recruit (SOURCED)
Laptop table 0x800F4884 row 6 "Build & Hire" → 0x80075354. Its sub-panel is filled by 0x8007D230:
category 1 (staff) adds the five type rows from tables 0x800E2D40 (text: Guards, Mechanics, Cleaners,
Researchers, Entertainers — the order you listed) with the number drawn beside each = **remaining
hireable** = `0x8006A394(mgr, kind)` (= 5, the pool capacity) **minus `pool+0xC` employed**
(0x80052EA0, case 0xA). "Guards 5" therefore means **0 guards employed**, not 5. Selecting a type fills
the recruit list (widget panel+0x530) with the five records of that kind (0x8007D4FC); the recruit card is
drawn by **0x8007E37C** from the *record* (no person exists yet):

| line | text id | value | source |
|---|---|---|---|
| name | rec+4 | "Gary Liddon" (0x3C1) | 0x8006A76C → 0x8006F00C |
| Pay Grade | 0x2DA | `rec+0x14 + 1` | 0x8007E704 at 0x8007E458, +1 at 0x8007E464 |
| Monthly Wage | 0x348 | **`0x8006A874(rec, −1)` = `base[rec+0x14] × mult[rec+0x10]`** | 0x8007E4A8 with a1 = −1 → the `bltz` path at 0x8006A874 reads rec+0x14 |
| Motivation bar | 0x314 | `rec+0x18` | 0x8007E6F8 |

So the offer is the un-pro-rated wage at the level the recruit starts at. Nothing on it reads a person.

### 2.2 The STAFF INFORMATION card (laptop row 5 → type → employee) — 0x8007BA74 (SOURCED)
Only lists types whose `pool+0xC ≠ 0` (0x800772C4), which is why you never saw it. Draws: name; "Time
Employed" (0x273) = `McAi+0x10 − P+0x34` (0x80094FDC); **"Monthly Wage" = `base[P+0x3C&7] × mult`** at
the CURRENT level, not pro-rated (0x8006A874(rec, level) at 0x8007BB10); "Skill Level" bar = 25×level;
two more bars (0x27D = (100 − P[0x3F] + P[0x40])/2, 0x154 = P[0x3F]). Rows tagged "Training" (0x36E)
open §2.3.

### 2.3 The TRAINING card — draw 0x80085908, purchase 0x80085728 (SOURCED)
Draws "Training Cost" = `hireBase[L] × hmult` (0x80094DC0 at the current level L), **"Monthly Wage" =
`base[min(L+1,5)] × mult`** (the NEXT level's wage, 0x800859BC with a1 = min(L+1,5)), "Skill Level" bar
= 25×min(L+1,5), and a one-entry slider whose text is `0x800E3498[L+1]` = "Level (L+2)". Purchase sets
level = L+1 (0x8009563C) and THEN charges `hireBase[L+1] × hmult` — one row dearer than the card showed
(for a level-0 guard: card says 500, bank loses 550). Two bugs worth a falsifier: at L = 4 the wage index
runs off the table (`0x800E162C[5]` = 3, so "Monthly Wage 3×mult"), and the afford check at 0x800857B4
tests the un-bumped price.

### 2.4 Predicted offer cards, all 25 recruits (MEASURED record table, SOURCED formula)
The 25 staff records are static, at **0x800F2AF0**, 0x20 bytes each, count at 0x800F2AEC (the manager
object is the static block at 0x800F29FC; `+0xF0` count, `+0xF4` table). Layout: `+2` u16 variant,
`+4` name text id, `+0x10` kind, `+0x14` default level, `+0x18` motivation.

| kind (mult) | variant 0 | 1 | 2 | 3 | 4 |
|---|---|---|---|---|---|
| Mechanics 0 (×3) | Mike Armstrong L0 **£150** | Karl Jeffery L1 165 | Richard Edwards L1 165 | Caroline Miller L1 165 | James Parham L0 150 |
| Entertainers 1 (×1) | Tim Swann L0 **£50** | Andy Nuttall L1 55 | Simon Harris L0 50 | Phil Williams L1 55 | Emma Barrett L1 55 |
| Cleaners 2 (×1) | Chris Eden L1 **£55** | Elco Vossers L1 55 | Stuart Thomson L1 55 | Debi Fearn L1 55 | Nick Dry L1 55 |
| Guards 3 (×2) | **Gary Liddon L0 £100** (rec 0x800F2CD0) | Mike Baxter L1 110 | Dave Owens L1 110 | Sarah Burfoot L1 110 | Michael Archer L1 110 |
| Researchers 4 (×3) | Paul Grenfell L0 **£150** | Chris Hadley L0 150 | Thor Hayton L0 150 | Niki Broughton L1 165 | Leigh Bird L1 165 |

"Pay Grade" shows L+1. Each is a one-screen falsifier of the mapping.

## 3. What actually commits a hire (SOURCED)

1. **X on the recruit card** → 0x8007D7F4 (panel update) → member "confirm" → **0x8007D554**. For staff
   (category 1, 0x8007D728..): `0x800193A4(tool 0x8010972C, state 1, rec+2 variant, rec+0x10 kind)`,
   sound 0x1D, then the panel's close member with 1 (the laptop closes). Before that it runs an afford
   check that prices `0x8006AD58(mgr, type, variant)` off the *attraction* widget (+0x268), not the
   recruit — if that ever fails you get sound (5,4) and no tool start. Verify with the read in step 2.
2. **Tool start** 0x800193A4 sets `tool+0x20 = 1` and calls the state-1 placer's create slot. The staff
   placer object is **0x80104A98** (table 0x800EFD5C[1]; vtable 0x800DC524, ctor 0x8001EF38); slot +0xC =
   **0x8001E508**: switch on kind → the pool creator (0x800516F0 mech / 0x80051900 ent / 0x800517A0 clean /
   0x80051640 guard / 0x80051850 res). The creator **allocates a node from the pool's free list
   (`pool+4` → `pool+8`, `pool+0xC += 1`, 0x8005D8C8)** and runs the kind's init (record lookup
   0x80069EF4(mgr, kind, variant) → P+0x30; base ctor 0x800941C0: level = rec+0x14, P+0x40 = 100,
   P+0x3F = 0, **P+0x34 = McAi+0x10 (hire day)**, P+0x3C bits 3..7 = 15). The person pointer is stored at
   `placer+0x14` (**0x80104AAC**), kind at +0x18, and 0x80095358 marks them held.
   **Read after this X:** `[0x8010974C] == 1`, `[0x80104AAC] == P ≠ 0`, `[P+0x34] == [McAi+0x10]`,
   `pool+0xC == 1`, and P is already the head of `pool+8`.
3. **Cursor movement** → placer "move" slot 0x8001E5E8: snaps, writes the position into P (0x80093FC0),
   sets the ghost bit (P+0x2B |= 0x40) and **zeroes `placer+8` (the price)**.
4. **X in the world** → confirm slot **0x8001E720**: `0x8004D800(tile)` must be true (six tile-class tests
   false, then `0x80050064(tile) == 0xC` or the 0x8004D358 flags — a path-type tile; drop them on a path).
   Then **commit 0x8001E6BC**: ghost bit off, `setState(P, 0)` (0x80093F80: P+0x2D = 0, P+0x10 = 0),
   **`0x8001C2E0` = TrySpend(Money(placer+8)) = TrySpend(£0)**, placer done. If the pool is now full it
   posts the "5 is the most you can employ" message. Sound 0xD.
5. **Triangle (or anything that cancels)** → 0x8001E870: `0x80051B2C(P)` → 0x80051C34-family → node back
   to the free list, count −1, record released. That is what your run did.

There is no assignment step; a staffer counts as employed from step 2 and is simulated from step 4.

## 4. Where employed staff live (SOURCED layout, MEASURED addresses for the idle park)

| pool | ptr global | pool (measured) | node stride | first free node → person |
|---|---|---|---|---|
| Guards | 0x80103864 | 0x801DA8B0 | 0x4C | 0x801DA9F0 → **P = 0x801DA9F8** |
| Mechanics | 0x80103868 | 0x801DA724 | 0x4C | 0x801DA864 → 0x801DA86C |
| Cleaners (Handymen) | 0x8010386C | 0x801DA598 | 0x4C | 0x801DA6D8 → 0x801DA6E0 |
| Researchers | 0x80103870 | 0x801DA40C | 0x50 | 0x801DA54C → 0x801DA554 |
| Entertainers | 0x80103874 | 0x801DA26C | 0x120 | 0x801DA3BC → 0x801DA3C4 |

- Pool object (ctor 0x80060020 family, list init 0x800600B0): `+4` free-list head, `+8` employed-list
  head, `+0xC` employed count, `+0x10` five slots back to back. Init pushes slot 0..4 onto the free list
  in order, so the **first hire takes slot 4** (the addresses above). Capacity 5 = `0x8006A394` = the
  "5" on the type list. Heap addresses are for this park image; re-read `pool+4` if the park differs.
- Node: `+0` next, `+4` prev, **person P = node+8**. The wage sum 0x80086B60 walks `pool+8` of all five
  pools (mechanics, entertainers, guards, researchers, cleaners) calling 0x80094E3C(node+8).
- Person fields used here: `P+0x2B` flags (bit 0x40 = held/ghost), `P+0x2D` state (15 = striking → £0),
  `P+0x30` record pointer, `P+0x34` hire day (McAi+0x10 at creation), `P+0x3C` bits 0..2 level, bits
  3..7 a 5-bit field the ctor sets to 15, `P+0x3F`/`P+0x40` the two bars (0/100 at hire).
- Level-record pre-hires (0x80071D0C → 0x80072500 family) go through the same creators but take
  P+0x34 from the level record's `+4` (0x80094C64) and can start striking (`rec[9] bit 7`). Your park has
  none (all five counts 0, MEASURED).

`fable/w/staffdump.py ram_NNNNNN.bin` prints all of this plus the wage each employed person will draw
at the next rollover and the predicted `BANK+0x12D0` increment.

## 5. The poke

### 5.1 Recommended: nop the tile check, then hire through the offer (SOURCED, effect UNMEASURED)
```
POKE=0x8001E754:0x00000000     # was 0x10400040 = beq v0,zero,0x8001E858 (the "tile not placeable" exit)
```
Then: laptop → Build & Hire → Hire → type → recruit → X (tool state 1, person created and stamped) →
**one X in the world** (no triangle) → committed at the cursor's tile, whatever it is. Everything the game
would normally do to the person still runs (create, stamp, state 0, ghost off), only the placement
geometry is unchecked, so where they stand afterwards is UNMEASURED (irrelevant to wages).

### 5.2 Nothing to poke for the stamp
`P+0x34` is written by the ctor at creation with the day the laptop was opened (paused). To make the
first month a full one, poke `P+0x34 = 0` after creation (any value ≤ `McAi+0x10 − monthLen` gives pct
100). Word-aligned, safe to hold.

### 5.3 Pure-data hire, UNMEASURED and not recommended
All the list plumbing is plain words, so a hire *can* be faked without the tool (guards, this park):
`[0x801DA8B4] = [0x801DA9F0]` (free head ← next free), `[0x801DA9F0] = [0x801DA8B8]`, `[0x801DA8B8] =
0x801DA9F0`, `[0x801DA8BC] = 1`, `[0x801DA9F8+0x30] = 0x800F2CD0` (Gary's record), `[+0x34] = hire day`,
`[+0x3C] = 0x00000078` (level 0, 5-bit field 15). The wage sum would then pay him. But the free-slot
persons only have the class pointer (slot ctor 0x800976F0); state, flags, position and `+0x3E..0x44` are
whatever the allocator left, and the AI tick walks the same list and runs their state machine — so this
can crash or misbehave, and the runner only pokes whole words (state P+0x2D and flags P+0x2B share
words with neighbours). Use 5.1.

## 6. What else gates wages (SOURCED)

- **Sandbox `0x80102D34`: not read by 0x80066C50 (tick), 0x80086D70 (rollover) or 0x80094E3C (wage).**
  Its readers are UI/menu builders (0x80075D74 hides Build & Hire when set), the objectives check
  0x80067928, guest leave 0x8008D058, game init 0x800588D0 (a UI flag bit) and others outside the money
  path. Holding it at 0 is needed only to *reach* the hire screen.
- **Free-money `0x801031CC`**: nothing in TPW.BIN sets it; if set, TrySpend skips the balance but
  `BANK+0x12D0` and `hist_wages` still accumulate (written before TrySpend, 0x80086EC8).
- **Pause**: the tick is skipped while `[0x80103940] ≠ 0` (0x80059074 — the modal window pointer, i.e. the
  laptop) or 0x800617D4. The placement tool does not set it, so the sim runs while a recruit hangs on the
  cursor.
- **Hiring is free**: the only staff charges are training (§2.3) and wages. `spend_total` (+0x12D4) must
  not move on a hire.
- **Unplaced recruit**: in `pool+8` from creation, state not 15 → **paid at the next rollover even while
  still on the cursor**. Cancelled recruit: freed → not paid. There is no other "assignment" state.
- Rollover 0x80086D70 is called only from 0x80066C50, which is also the only caller of the calendar
  0x80066EA8: if `McAi+0x10` advanced, the rollovers ran. Your zero was a zero wage sum, not a skipped
  rollover — check `BANK+0x12BC` moved (idle dump: 4 at day 123).

## 7. Numeric falsifiers

Definitions: `T` = `McAi+0x10` at the rollover tick (the first day of the new month), `D` = `P+0x34`,
`ML` = length of the month just ended, `pct = T−D < ML ? floor(100(T−D)/ML) : 100`,
`wage = floor(pct × base[L] × mult[kind] / 100)` pounds; `BANK+0x12D0 += 10 × Σ wage`.

- **From the idle dump state** (month 4 = May, ML 31, day-of-month 3, T at the next rollover = 151),
  hire **Gary Liddon** (L0 guard, £100/month) with the laptop opened on day 123 → D = 123:
  first rollover: `T−D = 28`, pct = floor(2800/31) = **90** → `BANK+0x12D0 = 900`, `hist_wages[4] = 900`,
  balance `500000 − 900 = 499100` (nothing else spends). June (30 days): T−D = 58 ≥ 30 → **+1000** →
  1900; then +1000 per month.
- Same recruit hired on the first day of a 31-day month → **1000** at the first rollover.
- Any two recruits: add their columns from §2.4 (×10, pro-rated separately by their own D).
- Poke `P+0x34 = 0` right after creation → first rollover pays **1000** (full month) regardless of D.
- Train Gary once (Training card, X): balance −5500, level → 1, next rollover pays **1100**; the card
  showed 500 and 110 before the purchase.
- `spend_total` (+0x12D4) unchanged by the hire itself; `+0x12C0` (loans) untouched.

## 8. Why your run read 0
The X on the recruit card put Gary on the cursor (`pool+0xC = 1`); the first triangle ran the placer's
cancel 0x8001E870 and freed him. Four month rollovers then summed an empty list. The control run was
identical for the same reason.

## 9. Not established
- The exact pad button the placer's confirm listens to (it is the same handler path as ride placement,
  which you have driven; UNMEASURED that it is X).
- Whether the offer's misdirected afford check (§3.1) can ever fail for staff.
- Where a tile-check-bypassed staffer wanders afterwards.
- The text ids of the five "5 is the most you can employ" messages beyond their ids (0x6E/0x6C/0x6A/0x70/0x72 by kind 0..4).
