# Theme Park World (PSX, SLES-026.88) — SHOP STOCK, FEATURE CAPACITY, RESTOCKING, THE MISSING EMPLOYEE

Ninth report. Companions: `behaviour.md` (visitor state 22 and the handyman, §2.4 / §3.6), `rides.md`
(object layouts, §0 item 1, §1.3, §2), `economy.md` (§4.7 "restocking: none"), `wages.md` (the five
staff kinds). Same legend: **READ** = taken from the instructions at the quoted address in `TPW.BIN`
(loaded at 0x80010000); **GUESS** = interpretation, with confidence. Scripts: `fable-scripts/fn.py`,
`callers.py`, `fieldx.py`, `vt.py`, `text.py`.

**Offsets.** The accessors in this report take the OUTER pool pointer (every caller subtracts 8 from
the attraction pointer first: 0x8008F254, 0x8007BF78, 0x8007C060, 0x80078054), so `obj+0x80` below is
**A+0x78** in rides.md's A-relative convention. rides.md §2 writes "feature A+0x80 capacity" and
economy.md writes "shop+0x88 price"; both are outer-relative. Same bytes, one convention slip, noted in
§0 item 3 and not repeated.

## 0. SOURCE DISAGREEMENTS — where the binary disagrees with an existing findings file

The port keeps the findings' version in the code wherever a findings file states one, marks it
`⚠ DISPUTED` with the binary's reading, and pins it with a test that says which side it is pinning.
Flip the formula and the test together once reviewed.

1. **`behaviour.md` §3.6 handyman bin choice: "score = Manhattan distance × (remaining+1)".** READ at
   0x80099018..0x80099050: `|dx|` is formed first (0x80099018..0x80099024), then `mult v0, a2` with
   `a2 = remaining + 1` sits in the DELAY SLOT of the y-sign branch (0x80099038/0x8009903C, repeated
   at 0x80099044 for the negative arm), and `|dx|` is added to `mflo` afterwards (0x80099050:
   `addu v1, a1, v0`). The score the binary minimises is **`|dx| + |dy| × (remaining + 1)`** — only the
   y term is weighted. That reads like a precedence slip in the original source (`abs(dx) + abs(dy) *
   (cap+1)`), and it changes which bin wins whenever two candidates sit at different x offsets.
   **Kept the report's formula** in `Handyman.BinScore` (StaffClasses.cs) with the ⚠, and
   `HandymanTests.BinScoreIsManhattanDistanceTimesRemainingPlusOne` pins it and says so.
2. **`rides.md` §1.3 feature record: "+0x2E flag byte (bit 0 usable-by-guests, bit 2 bin — GUESS from
   the handyman filter)".** READ: the handyman's filter is vtable slot 33 = 0x80023E60, which calls
   0x80024348 = `rec+0x2E & 1` (0x80023EB0) — **bit 0, the same bit the guests' slot 54 reads**
   (0x80023F0C → 0x80024348 at 0x80023F5C). So a handyman services every guest-usable feature, toilets
   included, not only bins; text 0x15D "Make sure a cleaner is patrolling them [the toilets]" agrees.
   Bit 2 has its own wrapper (0x80023FB8 → 0x80024330) and it is read by the VISITORS' code
   (0x8008D764 at 0x8008D7D8, 0x800595B8 at 0x8005972C), never by the handyman. The GUESS was about the
   wrong reader; §8 below gives every reader of every bit. Nothing in the code carried the bit-2 guess,
   so there was nothing to keep; the interface doc on `IHandymanWorld.TryChooseBin` now states bit 0.
3. **`rides.md` §2 "feature A+0x80 capacity 0..100" and economy.md's `shop+0x78..0x90`** are
   outer-relative offsets in a table that says it converted everything to A-relative. The feature
   byte is A+0x78 (0x80023C70 `addiu a0, s0, 0x80` with `s0` the outer pointer, the same function
   calling the vtable through `s0+8` at 0x80023C88). No code carries an offset; no change.
4. **`behaviour.md` §2.4 "type 2 (shop with stock) … GUESS: consume that many units of stock … GUESS:
   remaining stock %".** Already corrected by rides.md §0 item 1 (type 2 is a Feature); the two GUESSes
   are now READ (§3) and the doc comments in `VisitorQueue.cs` / `VisitorArrival.cs` say so. The
   arithmetic there was READ already and is unchanged.
5. **`behaviour.md` §3.6 "bin capacity := 100 and its +0x78 := McAi time"** — agrees, refined: the
   stamp is `McAi+0x10` = the total-day count (0x80066E78 = `lw 0x10(a0)`), the same word rides.md
   calls "day built" for a shop's `+0x78`.

Everything else this report touches agrees with the files it cites (economy.md §4.7 restocking: none;
wages.md's five staff kinds; behaviour.md's 60 / 40 / 50 thresholds).

## 1. Answers first

1. **A shop has no stock.** The type-4 object is 0x94 bytes and every field past the base is a price, a
   slider, a counter or a day (§2, all READ). Its sell routine 0x800B69E0 moves money and bumps two
   counters; it reads nothing that could run out. The only "stock" in the game's vocabulary is the
   purchase screen's build inventory (text 0x363 "Stock", 0x11F "Your stock of shops is getting
   low…" — the 20-slot pool). A shop sells forever, at any volume, to anyone who wants the item.
2. **The stock VisitorQueue consumes belongs to a type-2 Feature**: a SIGNED byte at obj+0x80 (A+0x78),
   100 at placement, drawn down by `(V+0x5D − 60) × 2 / 3` per use, floored at 0, never decaying (§3, §4).
   The game calls it **"Cleanliness"** (text 0x7E).
3. **Restocking is the handyman's bin-emptying, and only that** (§5): a cleaner picks any slot-33
   feature reading under 60, walks there, stands for 180/120/60/30/15 ticks by skill, then 0x80024210
   sets the byte to 100 and stamps the day. **No money moves** (no bank call on the path; economy.md
   §4.7 by exhaustion of the 41 GetBank sites). **It needs a member of staff**: nothing else raises the
   byte. **At zero a feature refuses nothing, closes nothing and posts nothing**: the guest uses it
   anyway, then takes the < 50 penalty.
4. **There is no shop employee.** Five staff pools, five kinds (wages.md §1: Mechanic, Entertainer,
   Cleaner, Guard, Researcher), the wage sum walks exactly those five lists, and the shop object has no
   staff pointer (§6).
5. **The low-stock threshold on the shop side is nothing, because there is no byte to compare.** The
   FEATURE's byte is read at six places (§7): the guest's < 50 penalty and bubble 0x3C, the handyman's
   < 60 pick and < 40 morale, the Toilet Information card's Cleanliness bar, the all-toilets panel's
   Overall Cleanliness average, and park statistic 30 = `100 − mean`. The four "your toilets are
   dirty" advisor lines exist (messages 0x50..0x53 → texts 0x15B/0x15D/0x15E/0x15F) but no code posts
   them by id; they come from level data (GUESS-medium, §7.6).

## 2. The shop has no stock (READ)

Pool stride 0x94 (rides.md §1.1), so the object is `obj+0x00..0x93`. The base Attraction ends at
obj+0x78 (A+0x70; rides.md §2's last base field is A+0x6F). Every shop field above it, with every
reader and writer in the shop's own code (`fieldx.py 0x78 0x7c 0x84 0x86 0x88 0x8a 0x8c 0x90 -r
800B6800 800B7A20`):

| obj+ | A+ | size | what | written by | read by |
|---|---|---|---|---|---|
| 0x78 | 0x70 | word | **day built** = McAi+0x10 | init 0x800B6980; loader 0x800B6C18 | saver 0x800B6B40 |
| 0x7C | 0x74 | word | **second slider** (signed) | 0x800B70B4 setter (init passes 0 at 0x800B698C); loader 0x800B6C7C | 0x800B70A8 getter; unit cost 0x800B6CF4 |
| 0x80 | 0x78 | word | **satisfaction total** | init `sw zero` 0x800B695C; recorder 0x800B6FC8 | recorder 0x800B6FB8; 0x800B6F68 (panel) |
| 0x84 | 0x7C | word | **visit counter** | init 0x800B6968; recorder 0x800B6FD0 | 0x800B6F6C, saver 0x800B6B98 |
| 0x88 | 0x80 | u16 | **sale price** (default `rec+0x2C` via 0x800B7110 → 0x800B7140) | 0x800B7144 setter | 0x800B7148 getter (the sell, 0x800B69FC) |
| 0x8A | 0x82 | u16 | **quality slider** (init 100 at 0x800B6978 → 0x800B706C) | 0x800B7070 setter | 0x800B7060 getter; unit cost 0x800B6CF0 |
| 0x8C | 0x84 | word | **takings** (+= price per sale) | init 0x800B6960; sell 0x800B6AD4 | 0x800B6F0C |
| 0x90 | 0x88 | word | **profit** (+= price − unit cost per sale) | init 0x800B6958; sell 0x800B6AD8 | 0x800B6F00 |

0x94 bytes, all accounted for. The sell routine 0x800B69E0 (READ, full): price getter, unit cost
0x800B6CBC, `margin = price − unitCost`; `blez` → the spend path (0x800B6A88..0x800B6AAC, TrySpend of
−margin) else the income path (0x800B6A1C..0x800B6A7C, typed income); then `takings += price`,
`profit += margin` (0x800B6AB0..0x800B6AD8). No load of anything else on the object; no store of
anything else. The status hooks are the base's: status 10's tick 0x800B6EA8 is `SetStatus(2)` at
once (slot 57 through the vtable at 0x800B6EE8), status 2's tick is base slot 71 = 0x80065B78, which
only advances the animation clock (0x800658D8). A shop does not close itself for any reason, and the
guest's type-4 arm (0x8008E5EC, ported in VisitorPurchase.cs) never asks a stock question — the
"slot 54" test exists only in the type-2 arm.

**The UI's "Stock".** text 0x363 "Stock" heads the purchase screen; 0x11F "Your stock of shops is
getting low. It might be good to get rid of some of the older ones in the park." and its siblings
0x131 (rides), 0x1ED (sideshows), 0x24B (features), 0xD4 "Pylon Stock", 0x13C "Track Stock" are all
about how many more of a thing can be BUILT (pool capacity minus placed; Attractions.cs). That is the
whole meaning of the word in this game.

## 3. The feature's capacity byte (READ)

Pool stride 0x84: `obj+0x00..0x83`. Above the base: `obj+0x78` word (stamp), `obj+0x7C` word (a
handle: zeroed at init 0x80023C7C, released through 0x8005358C at demolish 0x80023CEC..0x80023CFC —
not identified), `obj+0x80` **the capacity byte**, then padding.

Four accessors, all on `obj+0x80` (they `addiu a0, a0, 0x80` and hand the byte's address to a tiny
clamped-byte helper):

| fn | does | helper |
|---|---|---|
| 0x800241BC(obj) | **get**: `lb` (signed) | 0x800242DC |
| 0x800241E8(obj, units) | **subtract**: `sb a1, 16(sp)` parks the units in a stack BYTE (0x800241F0), then the helper does `v1 = lb obj − lb arg; v1 ≥ 0 ? sb v1 : sb 0` (0x800242B0..0x800242D4) | 0x800242B0 |
| 0x800242E8(&byte, &value) | **set**: `sb (lbu value)`, then if the byte reads negative (`sll 24; bgez`, 0x800242F4..0x800242FC) → 0; then if `lb` ≥ 101 (`slti 0x65`, 0x8002430C) → 100 (0x80024314..0x80024318) | — |
| 0x80024210(obj) | **refill**: set(100) (0x80024220 `addiu v0, zero, 0x64` → 0x8002422C), then `obj+0x78 := 0x80066E78(0x80066AC8())` = McAi+0x10, the total-day count (0x80024234..0x80024248) | 0x800242E8 |
| 0x800241DC(obj) | **stamp get**: `lw 0x78(a0)` | — |

Consequences, all reproduced in `FeatureStock` (core/TPW.Sim/FeatureStock.cs):
- **Set clamps a SIGNED byte.** 101..127 → 100; 128..255 → 0 (they read negative before the upper
  clamp runs); 256 is the byte 0.
- **Subtract reads the units as a signed byte and stores the difference with no upper clamp.**
  Subtract(255) on a full feature leaves 101; Subtract(128) stores the byte 0xE4 (−28). The one
  caller passes 0..26 (§4) so this is unreachable in play; it is in the class so the port's byte holds
  what the console's would.
- **Placement** (slot 37 = 0x80023C3C, READ): record lookup (0x80069610 → 0x8006A68C(mgr, variant) →
  0x80024430 binds the handle), `obj+0x7C := 0` (0x80023C7C), **set(100)** (0x80023C78/0x80023C80),
  `obj+0x78 := 0` (0x80023C94 — ZERO, not the build day; a shop's init stamps the day into the same
  slot, 0x800B6980), then slot 38 (type 2, variant). The feature then runs 0 → 1 → 10 → 2 like a shop:
  status 10's tick 0x80024258 is `SetStatus(2)` at once (rides.md §3, confirmed).
- **Save/load**: the saver 0x80023D28 writes the byte (0x80023D8C via get) and the stamp
  (0x80023D98); the loader 0x80023DB8 restores status from save+0xD (0x80023E0C, through slot 57),
  the byte from save+0xE **through the clamping setter** (0x80023E28..0x80023E34), and the stamp word
  raw from save+8 (0x80023E38/0x80023E48).
- **Nothing decays it.** Callers of the subtract helper 0x800242B0: only 0x800241E8; callers of that:
  only the visitor's type-2 arm 0x8008F110 (0x8008F2C4). Callers of the setter 0x800242E8: placement
  0x80023C80, the loader 0x80023E30, the refill 0x8002422C. Callers of the getter 0x800241BC: the
  visitor 0x8008F308, the handyman 0x80098FBC / 0x80099004 / 0x800992A8, the two cards 0x8007BF9C /
  0x8007C0A0, the panel average 0x8007805C, the park statistic 0x80015418. That is the complete list
  (`callers.py 800241BC 800241E8 80024210 800242B0 800242DC 800242E8`).
- **What a feature does at zero: nothing special.** The guest's arm tests slot 54 (the record flag,
  §8), not the byte; the subtract floors; the need is zeroed; nausea −40; Users +1; then the byte reads
  0 < 50 → bubble 0x3C, happiness −10, nausea +10. No status change (no SetStatus reachable from the
  byte), no message (no 0x8001412C site in any reader), and a cleaner will pick it up next (§5).

## 4. The consumption — the visitor's type-2 arm (READ; already ported, VisitorQueue.UseFeature)

0x8008F248..0x8008F36C: `s2 = V+0x28 − 8` (outer, 0x8008F254); slot 54 through the A pointer
(0x8008F264..0x8008F270; 0 → target := 0, Set 0); if `V+0x5D > 60` (0x800921EC against the byte 0x3C):
units = `(V+0x5D − 60) × 2 / 3` as a signed divide (0x8008F2A4..0x8008F2C0: ×0x55555556, `mfhi`,
minus the sign) → 0x800241E8(outer, units) (0x8008F2C4); `V+0x5D := 0` (0x8008F2D4); nausea −40
(0x8008F2E8..0x8008F2EC); 0x80063164(A) `A+0x14 += 1` — **"Users"** on the toilet's card (§7.3);
0x800241BC(outer) `< 50` (`slti 0x32`, 0x8008F310) → 0x80093E28(V, 1), bubble 0x3C (0x8008F340),
happiness −10 (0x8008F344..0x8008F350), nausea +10 (0x8008F358..0x8008F364); target := 0, Set 0.

So a use takes at most `(100 − 60) × 2 / 3 = 26` off the byte, and the byte is only dented at all by
a guest whose toilet need exceeds 60. Four full-need visits take a fresh toilet from 100 to 0 (74, 48,
22, 0); the third visitor already sees it under 50.

## 5. Restocking — the handyman, and nothing else (READ)

behaviour.md §3.6 has the state machine; re-read here for the stock questions.

- **Who.** A **Cleaner** (kind 2, wages.md; "handyman" in behaviour.md; `StaffKind.Cleaner` in the
  port). No other class calls 0x80024210; no ride, shop or timer does.
- **Which feature.** Seek 0x80098F20: over the feature list (0x80053248 head, 0x80099750 next), keep
  those whose **slot 33** is true (0x80098FA0..0x80098FB4 — the record flag bit 0, §0 item 2) and whose
  byte reads **< 60** (0x80098FBC..0x80098FC8, `slti 0x3C`); score per §0 item 1; lowest wins
  (0x80099054 `sltu`, so the first candidate at an equal score keeps it). None → fall back to litter
  (0x80099168 → 0x80098D44). Chosen → purpose 0x12 (18) (0x800990D0..0x800990D4), path request
  0x800EC9F4 to the feature's tile (0x80099124), **Set 11** (0x8009914C..0x80099150).
- **How long.** Arrival with purpose 18: `H+0x2C := now + 0x800E4C38[skill].1` = **180 / 120 / 60 /
  30 / 15 ticks** (behaviour.md §3.6; `Handyman.EmptyTicks`), state 51.
- **Then what.** State 51 (0x80099264): when `now > H+0x2C` (0x80099280..0x8009928C): byte `< 40`
  (0x800992B0, `slti 0x28`) → morale −10 (0x800992C0..0x800992C4) else +5 (0x800992EC..0x800992F8);
  tiredness +5 (0x80099304..0x80099310); **0x80024210(feature)** (0x80099318) — byte := 100, stamp :=
  today; Set 0, flag bit 1.
- **Cost.** None. 0x80024210 calls only 0x800242E8, 0x80066AC8 and 0x80066E78; state 51 calls no bank
  function; economy.md §4.7's exhaustion of the 41 GetBank sites already said "restocking: none".
- **Rate.** There is no rate: the byte jumps to 100 at the end of the wait. The only pacing is the
  cleaner's own — one feature per trip, the wait above, then Idle picks again (bins first half the
  time, litter otherwise).
- **Without a cleaner** nothing raises the byte, ever. A park with toilets and no cleaners reaches
  the < 50 penalty on every toilet after three heavy visits and stays there.

## 6. The employee — there is none (READ)

- Five staff pools at gp 0x80103864..0x80103874 (wages.md §4), five creators in the placer's switch
  0x800DC5A4, five kinds in the record table 0x800F2AF0 (`rec+0x10` = 0..4), and the wage sum
  0x80086B60 walks exactly those five lists (wages.md §1). No sixth pool, no shop-keeper record.
- The shop object (§2) holds no person pointer; the sell routine and the status hooks read none.
- Staff touch shops nowhere: `callers.py` on the shop's getters (0x800B7148 price, 0x800B7060 /
  0x800B70A8 sliders) finds the sell, the unit cost and the panel code only.
- So the answer to "what they cost and what they change" is £0 and nothing: a shop's throughput is
  bounded only by guests wanting the item (VisitorPurchase), and its costs are the build price and the
  per-sale unit cost.

## 7. Where the byte is read back — every "low stock" surface (READ unless marked)

### 7.1 The guest (ported)
`< 50` → bubble 0x3C, happiness −10, nausea +10 (§4). `VisitorQueue.FeatureLowStock = 50`.

### 7.2 The handyman (ported)
`< 60` picks it (`Handyman.BinPickBelow`, `Handyman.WantsEmptying`); `< 40` on arrival costs morale
(`Handyman.NeglectedBinRemaining`). §5.

### 7.3 The Toilet Information card — 0x8007C014 (table 0x800E2B40), refreshed by 0x8007BF5C (0x800E2B18)
Draws, for the object at panel+0x270 (0x8007C04C): the name via slot 11 (0x8007C0F8..0x8007C108,
0x80029D28 draws it; GUESS-high that slot 11 is the name); **"Users" (text 0xA3) = slot 22 =
`A+0x14`** (0x8007C094 → s3 at 0x8007C0A4; 0x80040DD8 formats it, 0x8007C14C) — the same counter the
visitor bumps at 0x8008F300; **"Last Cleaned" (text 0x400) = the stamp** (0x8007C0AC → sp+28 →
0x80040E0C at 0x8007C194, GUESS-high a day-to-date formatter); **"Cleanliness" (text 0x7E) = a bar
whose value is the byte** (0x8007C0A0 → sp+24 → 0x80047F4C(bar, byte) at 0x8007C1F0; the bar is built
by 0x8007C2B4 at 0x8007C1E4). The refresh 0x8007BF5C sets the bar's range 0..100 (0x80047F6C(bar, 0),
0x80047F64(bar, 100)) and its value to the byte. There is no threshold and no colour change in this
code: the bar just shows the number.

### 7.4 The all-toilets panel — 0x800780D0, label 0xFB "Overall Cleanliness"
0x80078010 sums the byte over every entry of the panel's list (0x8004A664 count, 0x8004A530 item,
`item − 8` → 0x800241BC) and returns `(sum << 16) / count >> 16`, 0 for an empty list or a zero sum
(0x80078074..0x800780B0). `FeatureStock.PanelAverage`. Which features the panel lists is not traced
(text 0x324 "All Toilets" suggests the toilets; GUESS-medium).

### 7.5 Park statistic 30 — 0x800153B4
Case 0x1E of the 72-way statistic switch 0x80016AB4 (jump table 0x800DB9F8; 0x80016DC8), stored as
an s16 at `stats+0x3C` by 0x80016870, which refreshes one statistic every four ticks round-robin
(counter mod 288 = 72 × 4). Over every feature whose **slot 33** is true: count and a **16-bit** running
sum (`sll 16; sra 16` after each add, 0x80015424); none → **0** (the register is preloaded with 100 at
0x80015444 and overwritten at 0x8001544C); else `(100 − sum / count) & 0xFFFF`
(0x80015450..0x80015484). `FeatureStock.ParkDirtiness`. Its neighbours in the switch are the
"count features by flag" statistic 0x80015A68 (§8) and the two flag-driven ones at 0x800142E8 /
0x80014554.

### 7.6 The advisor lines (GUESS-medium on the trigger)
The message records are 20 bytes at 0x800EE4FC + 20n with the text id in the first u16 (READ: 0x3E →
0x2F6 "Your ride is about to break down!", 0x41 → 0x272, 0x99 → 0x3F2, all three matching the ids
rides.md quotes). The toilet family:

| message | text | says |
|---|---|---|
| 0x4D | 0x387 | Your park needs some toilets, so your visitors have somewhere to go. |
| 0x4E | 0x031 | Some visitors are having to walk miles to a toilet. Perhaps you should build some more? |
| 0x4F | 0x392 | A park this size should have more toilets. |
| 0x50 | 0x15B | Your toilets are getting dirty and you have no cleaners. If you don't want to lose visitors, you should hire some. |
| 0x51 | 0x15D | Your toilets are getting dirty. Make sure a cleaner is patrolling them. |
| 0x52 | 0x15E | People are complaining that your toilets are dirty. You should hire some cleaners! |
| 0x53 | 0x15F | Your toilets are so filthy they are making people sick. A cleaner should be patrolling them at all times. |
| 0x68 | 0x269 | One of your toilets is not connected to a path. |

**No code posts any of 0x4D..0x53 by id**: a scan of every `jal 0x8001412C` (the message-id setter)
for an `addiu a1, zero, ID` beside it finds only 0x3E (0x8009C750) among these. 0x80016870 compares
each freshly computed statistic against 12-byte records reached through the handle at its object's
+0xC0 (0x800C0DB8 is a PsyQ-range call; 0x80016928..0x800169F4), and economy.md §3 has the
objectives code 0x80067928 running weekly; so the trigger for "your toilets are dirty" is most
likely **data, compared against statistic 30**, and the threshold is not established. What IS established: the
game-side quantity those lines are about is `100 − mean cleanliness over slot-33 features`, and it
reads 0 when there are no toilets — so the "getting dirty" family cannot fire in a park without them,
which is what 0x4D..0x4F are for.

## 8. The feature record's flag byte rec+0x2E (READ readers, GUESS meanings)

Four one-bit accessors on the record (0x80024348 bit 0, 0x8002433C bit 1, 0x80024330 bit 2,
0x80024324 bit 3), each with a wrapper that locks the handle (slot 7), reads, and releases (slot 6):

| bit | wrapper | wrapper's callers | meaning |
|---|---|---|---|
| 0 | slot 54 = 0x80023F0C and slot 33 = 0x80023E60 | guests (score 0x8008C818, target choice 0x8008CDC8, unload 0x8008F110); handyman 0x80098F20; statistics 0x800153B4 / 0x80015A68 (mask 0x20) | **guests may use it, and cleaners service it** (READ for both readers) |
| 1 | 0x80024110 | statistics 0x800142E8 / 0x80014554 / 0x80015A68 (mask 0x80); UI 0x800385D0 / 0x80038900 / 0x800396FC / 0x8004A0B4; staff 0x800947CC | not established |
| 2 | 0x80023FB8 | visitors 0x8008D764, 0x800595B8 | GUESS-high **bin**: both callers walk the feature list from the guests' side (state 11 is "walk to bin"); the handyman never reads it |
| 3 | 0x80024064 | statistics 0x800142E8 / 0x80014554 / 0x80015A68 (mask 0x40) | not established |

`AttractionDefinition.UsableByGuests` (core/TPW.Data) is bit 0 and is now also the handyman's filter.

## 9. What was ported

- `core/TPW.Sim/FeatureStock.cs` — the byte (signed, both clamps, the subtract's slip), the stamp,
  placement, save-load, the refill, `ParkDirtiness` (statistic 30), `PanelAverage` (0x80078010).
- `core/TPW.Sim/StaffClasses.cs` — `Handyman.BinPickBelow = 60`, `WantsEmptying`, `BinScore` (the
  findings' formula, ⚠ DISPUTED per §0 item 1); interface docs corrected to READ.
- `core/TPW.Sim/VisitorQueue.cs`, `VisitorArrival.cs` — doc comments on the three stock members and
  the type-2 arm upgraded from GUESS to READ; no arithmetic changed.
- `game/ParkRideWorld.cs` — the three fakes replaced: `TargetHasStock` is the record flag,
  `ConsumeStock` / `StockLevel` go to a `FeatureStock` on the attraction's runtime, placed full.
  ⚠ Runtimes there are keyed by definition entry, so two toilets of one kind share a byte (a
  pre-existing property of that world's ids, flagged in the file, not widened here).
- Tests: `tests/TPW.Sim.Tests/FeatureStockTests.cs` (12), `HandymanTests.cs` (+2). Each says what
  it REJECTS. The mutation sweep is `tools/mutate_shop_stock.py`; results in
  `shop-stock-mutations.json` (§10).

## 10. Mutation sweep

MUTATION_SUMMARY_PLACEHOLDER

## 11. Not established
- The meaning of record bits 1 and 3, and of the feature's `obj+0x7C` handle.
- The level-data threshold(s) that post messages 0x50..0x53 from statistic 30, and the 20-byte
  message record's other fields.
- Which features the all-toilets panel lists (§7.4) and what 0x80040E0C prints for a stamp of 0.
- Whether slot 11 is the name (§7.3) — drawn where a name would be, not read.
