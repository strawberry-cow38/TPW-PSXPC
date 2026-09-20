# A guest's needs over time — SLES-026.88, 2026-09-20

READ means instructions or data read from `TPW.BIN` loaded at `0x80010000`; GUESS is an inference
with a confidence. This report is the CLOCK for the guest stats: what makes each of V+0x58..V+0x5F
move, how often, by how much, and what each byte in that run actually is. It was traced for the
`needs` branch after the purchase port; behaviour.md §2.9 and §2.12 are the earlier reading and are
retained. Where this report and an earlier findings file disagree, §0 lists it and the CODE KEEPS THE
EARLIER FILE'S VERSION — the disagreement is written down for review, not resolved silently.

Scripts: `findings/fable-scripts/fn.py ADDR`, `callers.py ADDR`, `fieldx.py OFF`. The scan that
underlies the "complete list of writers" claims below is: every `addiu rX, rY, 0x58..0x63` in
`0x80010000..0x800C0498` (the stat helpers take a POINTER to the byte, so this is how a stat is
named), every `jal` to one of the seven stat helpers, and every byte-sized load/store at those
offsets in both code blocks. It found nothing outside the functions listed here.

## 0. SOURCE DISAGREEMENTS (binary vs an existing findings file)

| # | findings file says | binary READ | address | code keeps |
|---|---|---|---|---|
| 1 | behaviour.md §2.9: "Every 64 ticks", "Every 50 ticks", "Every 40 ticks", "Every 128 ticks" — bare periods, no stagger. visitor-rest.md §2 already flags the 64-tick one. | ALL FOUR PASSES ARE STAGGERED BY V+0x10, exactly like the 8-tick one. 64: `now & 63 == V+0x10 & 63`. 50: `now % [0x80103238] == V+0x10 % [0x80103238]` (divu, mfhi on both). 40: same with `[0x8010323C]`. 128: `now & 127 == V+0x10 & 127`. | 0x80090128..0x8009013C (64); 0x80090320..0x80090364 (50); 0x80090384..0x800903C8 (40); 0x800903F8..0x8009040C (128) | the bare periods in `VisitorNeeds.Tick` (behaviour.md), as visitor-rest.md chose. ⚠ On the bare clock every guest in the park takes its litter damage and rolls its hunger on the SAME tick; on the real clock they are spread over the period. Same long-run rate, different texture. |
| 2 | behaviour.md §2.9: litter "within 2 tiles" | the test is `|dx| + |dy| < 2` in WHOLE tiles (slot-10 coordinates, `>> 8`): the guest's own tile and the four edge-neighbours, not a 2-tile radius. | 0x800901B8..0x800901F4 (`slti v0, v0, 2`) | the world query `LitterNearby` (the port never fixed a radius; the doc string said 2 tiles). Nothing to change in code; the interface comment now says what the original tests. |
| 3 | behaviour.md §2.9 (bubble pass): no mention of the park-wide bubble cap | the 128-tick bubble pass is ALSO skipped when the global shown-bubble count `[0x80103264]` is ≥ 25 and this guest's own shown bit (P+0x2B bit 0x08, read by 0x80093E0C) is clear. A guest that already has a bubble up re-evaluates; a guest without one cannot get one while 25 are showing. | 0x80090414..0x80090440; the counter is kept by 0x80093E28 (`+1` when a bubble goes up and the count is < 25, `−1` when it comes down) | nothing — NOT PORTED. The port has no park-wide bubble counter and adding one is a feature, not a constant. Recorded so nobody wonders why a crowded park shows at most 25 bubbles. |
| 4 | behaviour.md §2.1 Idle roll 4: vomit "if nausea > 92, or 25% regardless" | vomit needs nausea > 92 AND `rand(4) == 0`; the die is only drawn when the threshold passes. visitor-rest.md §3 already records this. | 0x8008D5D8..0x8008D5F8 | the OR in `VisitorIdle`, as visitor-rest.md chose. Listed again because §4 below is about what nausea makes a guest do, and this is the one place the port and the console differ on it. |
| 5 | behaviour.md §2.12: "start money £200 + rand(300)" listed before the start stats; the port's `Visitor.Spawn` draws the money die FIRST and comments that V+0x50's position "is a guess" | the constructor's die order is: `rand(40)` rubbish, `rand(50)` nausea, `rand(70)` need A, `rand(300)` money (FOURTH), `rand(40)` boredom, `rand(100)×rand(100)` V+0x5D, `rand(100)×rand(100)` V+0x5E, `rand(50)` tiredness, `rand(15)` speed, `rand(300)` for V+0x50 (LAST — the guess in `Spawn` was right). | 0x8008C59C..0x8008C6E0 | `Spawn`'s order (money first). Only the RNG stream order differs, not any value; changing it is a one-line reorder that would move every dice-driven fixture, so it is flagged here rather than done. |
| 6 | behaviour.md §1: V+0x5C is "unknown; GUESS-low boredom", V+0x5D "unknown", V+0x5B/V+0x5E "GUESS-low hunger/thirst" | the game's own UI ties each byte to an icon and two of them to a park statistic (§6 below): V+0x5B → the burger sprite, V+0x5E → the drink-cup sprite, V+0x5D → the toilet pictogram, V+0x5A → the "nauseous" bubble art. The debug slider pool (debug.md §3) names 0x80103218 "Boredom/Happiness" and it gates V+0x5C. | 0x800E3100 (icon table, indexed by 0x80091C28's code); 0x80081758..0x80081778 | the neutral field names `NeedA`, `NeedB`, `RideDesire`, `Boredom`. The icon table is READ; that a burger means hunger is still a reading of a picture, so the labels stay GUESS-high and the code does not rename anything. |

Everything else in behaviour.md §2.9 and §2.12 about these bytes re-read as written: the gate
expressions, the +6/−3/+5/−1/+2 influence figures, the −3/+3 litter figures, the five happiness
penalties and their thresholds, the `rand(2)` growth, the bubble order, the ride reward, the
purchase arms, the feature's −40, the queue's dice and the walking tiredness.

## 1. The run of bytes, V+0x58..V+0x63 (READ unless marked)

A Visitor is a Person at +8 plus these. The seven helpers are the ONLY way the visitor code touches
V+0x58..V+0x5F: add 0x80092190, sub 0x800921C0, set 0x800924F0 (each takes a pointer to the byte and
a pointer to the delta; add clamps the top only, sub clamps the bottom only, set clamps both — see
`Stat` in Visitor.cs), compare `<` 0x80092168, `>=` 0x80092178, `>` 0x800921EC, read 0x80092650.
Every one of the 111 stat references found by the scan goes through them.

| byte | what | at spawn (ctor 0x8008C534) | in a save (§7) | label status |
|---|---|---|---|---|
| V+0x58 | **rubbish carried**, NOT a need | `rand(40)` | min/range saved, regenerated | GUESS-high (behaviour.md §1) |
| V+0x59 | **happiness**, NOT a need | 50, no die | saved, regenerated | GUESS-high; the park statistic and stats window both average it |
| V+0x5A | **nausea** | `rand(50)` | saved, ⚠ NOT restored | icon: bubble 0x3D (§6); GUESS-high "nausea" |
| V+0x5B | need A | `rand(70)` | saved, regenerated | icon: 0x34 burger (§6); GUESS-high "hunger" |
| V+0x5C | boredom | `rand(40)` | saved, ⚠ NOT restored | debug slider "Boredom/Happiness" gates it (debug.md §3, label DERIVED from pool order); no icon — 0x80091C28 never reports it |
| V+0x5D | need — fed by eating and drinking, emptied at a feature | `rand(100) × rand(100) / 100` | saved, regenerated | icon: 0x3B toilet pictogram (§6); rides.md §0 and debug.md "Toilet/Happiness": GUESS-high "toilet need" |
| V+0x5E | need B | `rand(100) × rand(100) / 100` | saved, regenerated | icon: 0x32 drink cup (§6); GUESS-high "thirst" |
| V+0x5F | **tiredness** | `rand(50)` | saved, regenerated | icon: 0x40 (§6); GUESS-high |
| V+0x60 | walk speed, signed byte (`lb` at 0x80092694, `sb` at 0x80092678) | copied from V+0x62 | saved (as u16 min/range), restored | READ (state 3 multiplies it) |
| V+0x61 | visitor type 0..7, index into 0x800F79E8 | not written by the ctor; the loader rolls `rand(8)` (0x80091BE4); a kind-2 purchase sets 8 | not saved | READ |
| V+0x62 | the speed the guest was born with | `rand(15) + 15` | saved, restored | READ |
| V+0x63 | ⚠ a byte with a getter (0x8009267C) and setter (0x80092664) and NO GAMEPLAY READER. The setter's only caller is the loader (0x80091BD0); the getter's only caller is the save aggregate (0x8007113C). It round-trips through a save and nothing else ever looks at it. | not written | saved, restored | meaning NOT ESTABLISHED. Not ported: nothing in the sim can observe it. |

⚠ V+0x5A and V+0x5C are the two stats the loader does NOT restore (0x80091AC4..0x80091B50 sets
0x58, 0x59, 0x5B, 0x5D, 0x5E, 0x5F and skips 0x5A and 0x5C, though the aggregate saved all eight). A
loaded guest keeps the constructor's `rand(50)` nausea and `rand(40)` boredom.

The constructor's clock fields: V+0x2C := `now` (0x8008C5A4); V+0x50 := V+0x2C + `rand(300)`
(0x8008C6DC..0x8008C6F4); V+0x54 := McAi+0x10 (0x8008C70C).

## 2. The clock — vtable slot 41, 0x8008FE60, one call per tick before the state handler

Five passes. Each is gated on a comparison of `now` (0x800BDD18) against V+0x10, so a park of guests
is spread across the period rather than moving in lockstep. `now` is read fresh for every pass.

| pass | gate (READ) | what moves |
|---|---|---|
| influence | `now & 7 == V+0x10 & 7` (0x8008FE80..0x8008FE94) | happiness +6 for bit 1 (0x8008FEEC..0x8008FEFC); for bit 4, in raw state 2 or 3 (`lbu 53(s2)`, `−2`, `sltiu 2` at 0x8008FF0C..0x8008FF1C): happiness −3, nausea +5, else −1, +2 (0x8008FF20..0x8008FF70); bit 2 → the entertainer push (visitor-rest.md §2) |
| litter and penalties | `now & 63 == V+0x10 & 63` (0x80090128..0x8009013C) | per litter object with `|dx|+|dy| < 2` tiles: happiness −3, and nausea +3 if 0x80066958 says vomit (0x80090200..0x8009022C); then happiness −1 for EACH of boredom ≥ [0x80103218]=95, nausea ≥ [0x8010321C]=85, V+0x5D ≥ [0x80103220]=90, V+0x5B ≥ [0x80103224]=95, V+0x5E ≥ [0x80103228]=85 (0x80090244..0x8009031C, five separate `>=` calls) |
| need A growth | `now % [0x80103238] == V+0x10 % [0x80103238]`, the word is 50 (0x80090320..0x80090364) | V+0x5B += `rand(2)` (0x8009036C..0x80090380) — the die is drawn on every gated tick and adds 0 or 1 |
| need B growth | same shape with [0x8010323C] = 40 (0x80090384..0x800903C8) | V+0x5E += `rand(2)` (0x800903D0..0x800903E4) |
| bubble | sandbox flag 0x80059A9C() == 0 AND `now & 127 == V+0x10 & 127` AND (`[0x80103264] < 25` OR my shown bit is set) (0x800903E8..0x80090440) | the bubble chain of §2.9, unchanged: V+0x5D > 90 → 0x3B (and > 97 → speed := [0x80103214] = 30 via 0x80092674); nausea > 90 → 0x3D; happiness > 90 → 0x31; < 10 → 0x35; > 80 → 0x3E; tiredness > 90 → 0x40; 25 < happiness < 75 and rand(10)==0 → 0x39; else off |

⭐ ONLY TWO STATS GROW WITH TIME. Need A and need B are the only bytes this function, or any
function, raises on a clock. Boredom, the toilet need, nausea and tiredness have NO passive growth:
a guest standing still on a clean tile with nothing nearby gets hungrier and thirstier and NOTHING
ELSE changes. Everything else moves only when the guest does something or something is done to it.

Long-run rates on the console clock (25 ticks/s, 99 ticks/day, README): need A gains ½ point per 50
ticks = about one point per 4 s, one game day takes it up ~1; need B ½ per 40 ticks. From a spawn
median of 35 (need A) / 25 (need B) that is roughly 60 / 50 game days to the 95 / 85 unhappiness
thresholds if nothing is bought — derived from READ constants, not measured.

## 3. Per-need ledger

Every writer, with its address. "Clamped" means through the add/sub helpers (0..100).

### 3.1 Need A — V+0x5B (icon: burger; GUESS-high hunger)
- **Up:** `rand(2)` per 50-tick pass (0x8009037C). A drink purchase adds the product's byte +0x32
  (`NeedAValue`; 0x8008EA30 arm, VisitorPurchase) — 0 for every Drinks Shop on the disc
  (visitor-rest.md §1), so in shipped data this is a no-op.
- **Down:** a food purchase (kinds 0, 4, 5, 7) subtracts +0x32 (0x8008E91C arm): 25 for burgers /
  ice cream / most restaurants, 20 or 25 for fries. Nothing else. Not a ride, not a feature, not time.
- **Read by:** the score (`s5 = M[V+0x5B/10][slot56/10]`, 0x8008CBC8), Idle roll 0's bubble (> 90 →
  0x33, 0x8008D244), the 64-tick penalty (≥ 95), the condition code (> 80, §6), the type-4 want
  (`+ trunc(V+0x5B × b / 100)`).

### 3.2 Need B — V+0x5E (icon: drink cup; GUESS-high thirst)
- **Up:** `rand(2)` per 40-tick pass (0x800903E0). A food purchase adds the product's byte +0x33
  (`NeedBValue`; 0x8008E91C arm): 10 for ice cream, 5 for restaurants, 0 for burgers — eating makes
  you thirsty. Fries add a further `second slider / 15` first (0x8008E8E4).
- **Down:** a drink purchase subtracts +0x33 (0x8008EA30 arm): 40 for every Drinks Shop. Nothing else.
- **Read by:** the score (`s7`, 0x8008CB9C), Idle roll 0's bubble (> 90 → 0x33), the penalty (≥ 85),
  the condition code (> 75 alone, > 80 with need A, §6), the want (`+ trunc(V+0x5E × a / 100)`).

### 3.3 V+0x5D (icon: toilet pictogram; GUESS-high toilet need; `RideDesire` in code)
- **Up:** ONLY purchases. Food adds the same +0x32 it took off need A; drink adds the same +0x33 it
  took off need B (0x8008E964 / 0x8008EA78). No clock. A guest that buys nothing never needs the toilet.
- **Down:** a type-2 feature with its usable flag set SETS it to 0 (0x8008F2CC..0x8008F2D8, the set
  helper, after consuming `(V+0x5D − 60) × 2 / 3` capacity when > 60). Nothing else.
- **Read by:** the score (`s6 = T[(V+0x5D+5)/5]` when slot 54, 0x8008CBFC), target choice (> 98 →
  history / shop slot 54, 0x8008CE98 with 0x62), Idle roll 0 (> 90 → 0x3B), the bubble pass (> 90 →
  0x3B, > 97 → speed 30), the penalty (≥ 90), the condition code (> 75, §6).

### 3.4 Boredom — V+0x5C (no icon; the debug pool's "Boredom")
- **Up:** +5 when the target choice finds nothing open (0x8008CF8C); `+rand(2)` when a path request
  fails for a purpose that is not 3/11/14/15/22 (0x8008FB70, message 2); in a queue (state 18,
  0x800906EC): +1 every 4 ticks staggered while ride slot 20 is set (0x800907C4..0x800907EC), and
  `+(rand(100 − d) < 2)` every 8 ticks staggered where `d = max(|pref − intensity|, 50) >> 1`
  (0x80090800..0x80090820). No clock outside a queue.
- **Down:** a ride (types 1/3/6/7) subtracts `[0x80103200] × intensity >> 12` = intensity
  (0x8008F4B8..0x8008F4D8). Nothing else.
- **Read by:** the queue's leave test (> 80 → leave, 0x80090840..0x80090854), the penalty (≥ 95).

### 3.5 Nausea — V+0x5A — see §4.

### 3.6 Tiredness — V+0x5F
- **Up:** `+(rand(10) == 0)` per tick of state 2 while a waypoint remains (0x8008E1F8..0x8008E210;
  the die is drawn every such tick, the add is 0 or 1). That is the ONLY source: a guest that never
  walks never tires, and standing in a queue REDUCES it.
- **Down:** in a queue, `−(rand(100) < 10)` every 8 ticks staggered (0x80090824..0x8009083C);
  `−rand(20)` on unloading from ANYTHING — the subtract at 0x8008F1E0 is before the type switch, so
  a shop visit or a toilet also rests the guest.
- **Read by:** Idle's leave test (≥ 99, 0x8008D064..0x8008D098), the bubble pass (> 90 → 0x40), the
  condition code (> 75, §6). It is NOT in the 64-tick penalty list.

## 4. Nausea in particular — V+0x5A

**What raises it (READ):**
- A ride: on unloading from types 1/3/6/7, if intensity ≥ 56 (`slti v0, s0, 0x38`, 0x8008F488):
  `+= [0x801031FC] × (intensity − 30) >> 12` with the word = 1212 (0x8008F490..0x8008F4B4) — about
  0.3 per point over 30, +7 at 56, +20 at 100. Intensity is vtable slot 53, recomputed on every
  call: `min(100, base × clamp(speed/100, 0.75, 1.25) × clamp(duration/5, 0.75, 1.25))` (rides.md
  §5, 0x800A0594). So the speed slider raises nausea only THROUGH intensity, and a ride whose base
  is low enough never sickens anyone at any speed: base 40 × 1.25 × 1.25 = 62 just clears the gate,
  base 70 at default sliders (0.75 × 0.75 × 70 = 39) does not.
- Standing in / walking through an unpleasant influence: +2 / +5 per 8-tick pass (§2).
- Vomit within one tile: +3 per 64-tick pass per pile (§2).
- A food or drink purchase: the product's byte +0x36 (`NauseaValue`), 5..15 (0x8008E91C, 0x8008EA30).
- A feature whose capacity byte is < 50 after use: +10 (0x8008F358..0x8008F364).

**What lowers it (READ):**
- Using a type-2 feature: −40 (0x8008F2DC..0x8008F2F0), before the < 50 check above.
- Vomiting: SET to 0 (0x80090C68..0x80090C74, state 29).
- Nothing else. No decay with time.

**What it makes a guest do (READ):**
- ≥ 85: happiness −1 per 64-tick pass (0x80090270..0x80090298, [0x8010321C]).
- > 90: bubble 0x3D, second in the bubble order (0x800904A4..0x800904C8).
- > 92 AND `rand(4) == 0` when Idle's roll lands on case 4 (1/6 per tick): V+0x2C := now + 60,
  animation 12, state 29; after 60 ticks a litter object marked vomit (kind 0x9E) at the feet, nausea
  := 0, state 0 (0x8008D5D0..0x8008D630, 0x80090C00). See §0 item 4 for the port's OR.
- Steers choice: `s0 = T[(nausea + 5) / 5]` for any attraction with slot 54 set, weight 2 (0x8008CC38)
  — a sick guest scores features higher, and the feature is what takes 40 off. The type-4 want
  subtracts `trunc(nausea × c / 100)` so a sick guest buys less food.
- The condition code reports it at > 75 (§6), after the three needs and before happiness/tiredness.

## 5. What is NOT a need in the same run

- **V+0x58 rubbish:** `+30 + rand(25)` on a type-4 sale (0x8008E898, [0x8010322C] = 30); set to 0 at
  a bin (arrival 19, 0x8008E1C0) and when litter is dropped for want of a bin (0x8008D9E8); ≥ 90 →
  Idle roll 3 seeks a bin. No clock.
- **V+0x59 happiness:** the target of most of the above. Its own movers: ride +15/+10/+5 (0x8008F448
  ..0x8008F480), entertainer +5 (0x80090BB4), influence +6/−3/−1, litter −3, the five penalties,
  target choice −5 (score < 8) / −10 (nothing), path failure `−rand(15)` (0x8008FB58), feature < 50
  −10, purchase happiness arms, sideshow ±10, Idle litter roll reads it (< 25).
- **V+0x60..V+0x63:** §1.

## 6. The condition code — 0x80091C28 (READ), and how the game names the bytes

`0x80091C28(guest)` returns one number, first match wins, all strict comparisons through 0x800921EC
(`>`) and 0x80092168 (`<`):

| code | test | icon word at 0x800E3100 + 4×code |
|---|---|---|
| 7 | V+0x5B > 80 AND V+0x5E > 80 (0x80091C48..0x80091C7C) | 0x33 |
| 2 | V+0x5B > 80 (0x80091C80..0x80091C94) | 0x34 |
| 1 | V+0x5E > 75 — ⚠ 75 alone, 80 in the pair (0x80091C98..0x80091CB0) | 0x32 |
| 3 | V+0x5D > 75 (0x80091CB4..0x80091CC8) | 0x3B |
| 4 | V+0x5A > 75 (0x80091CD0..0x80091CE0) | 0x3D |
| 5 | V+0x59 > 75 (0x80091CE8..0x80091CFC) | 0x3E |
| 6 | V+0x5F > 75 (0x80091D04..0x80091D14) | 0x40 |
| 8 | V+0x59 < 25 (0x80091D18..0x80091D30: `sltu; sll 3`) | 0x35 |
| 0 | none of those | 0 |

The words at 0x800E3100 are NOT text ids (as text ids they resolve to unrelated strings); they are
the same numbers the bubble code writes to P+0x2A, and visitor-rest.md §2 decoded that sheet: 0x32 a
drink cup, 0x33 cup and burger, 0x34 a burger, 0x3B the toilet pictogram, and 0x3D/0x3E/0x40/0x35 are
the bubble ids the needs pass uses for nauseous/happy/tired/miserable. **This is the game's own
statement of which picture goes with which byte**, and it is READ. That the burger picture means
"hungry" is a reading of a picture and stays GUESS-high.

Consumers of the code:
- **Park statistics 1 and 2** (switch 0x80016AB4, jump table 0x800DB9F8): case 1 = percentage of
  guests whose code == 1 (0x80016B1C..0x80016B70), case 2 = percentage whose code == 2
  (0x80016B74..0x80016C08); `count × 100 / guests` as a signed divide, 0 when there are no guests.
  Case 46 is the mean happiness (0x80016E90..0x80016EF8: sum / guests, 0 when none). The advisor
  records at 0x800EE4FC + 20n put "Lots of your visitors are thirsty" (text 0x97) at n = 1 and
  "People are getting hungry" (0x403) at n = 2 — the SAME indices — but the code that compares a
  statistic against its record (0x80016870, shop-stock.md §7.6) reads 12-byte level data that was
  not decoded, so the tie between statistic n and record n is GUESS-medium, not READ. If it holds,
  the byte-to-word link becomes READ: statistic 1 counts V+0x5E > 75 and the game calls that "thirsty".
- **The Park Statistics window** (0x800814AC, opened by the laptop's PARK_STATS handler 0x800758B0;
  tabs "Park Overall" / "Park Finance" / "Awards", text 0x246 / 0x9E / 0x1C9 at 0x80103174): counts
  every guest's code into 0x800F4E18[code], buckets happiness into < 25 / 25..75 / > 75 percentages
  (0x80081700..0x80081750, stored at +0x46C/+0x470/+0x474), then picks the THREE most common codes
  1..8 — strict `<`, so a tie goes to the LOWER code; the winner's count is zeroed and its icon word
  stored at 0x800F4E3C[i] (0x8008188C..0x800818F0). So the "Park Overall" tab shows the three commonest
  guest conditions as pictures. Ported as `VisitorCondition.TopThree`.

## 7. Saves do not store guests' stats; they store the population's spread (READ)

The save aggregate 0x80070FA4 walks every guest and keeps, per stat in the order 0x58, 0x59, 0x5A,
0x5B, 0x5C, 0x5D, 0x5E, 0x5F, then money (0x8009252C, u16), V+0x62 and V+0x63, the minimum and the
maximum (0x80070F6C), and writes `min, max − min` pairs (26 bytes) through 0x80070AF0. The loader
0x80091A3C rebuilds each guest as `min + roll(range + 1)` where the roll is 0x8009197C
`(rand(n) + rand(n)) / 2` for rubbish, happiness, money and speed, and 0x800919C4 `n − rand(n) ×
rand(n) / n` — skewed HIGH — for needs A, V+0x5D, B and tiredness (0x80091AB0..0x80091B94). The
constructor skews V+0x5D/V+0x5E LOW (`rand × rand / 100`), so a freshly loaded park's guests need the
toilet more, on average, than the same park's guests did before saving. Not ported: the sim has no
save format.

## 8. What was ported, and the sweep

- `VisitorCondition` (new): the code, the icon table, the two percentage statistics, the window's
  top-three selection. Tests in `VisitorConditionTests`.
- `VisitorNeeds`: the growth die is now the named constant `GrowthRollMax`; the doc comments carry
  §0 item 1 and item 3. Behaviour unchanged (bare periods kept, see §0).
- `Visitor.cs`: doc comments on the six need fields point at the ledger; `Spawn`'s roll-order comment
  says what is now READ. No behaviour change.
- Rejecting tests added to `VisitorNeedsTests` for the rules that had none: the growth is the die's
  value (0 or 1, never a flat +1 and never 2), only two stats move on a clock, the die is drawn even at
  the cap.
- Sweep: `tools/mutate_needs.py`, results in `needs-mutations.json` — see that file for the count.
