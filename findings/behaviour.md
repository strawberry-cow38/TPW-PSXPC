# Theme Park World (PSX, SLES-026.88) — person state BEHAVIOUR

The [visitor remainder reading](visitor-rest.md) records the decoded shop need coefficients,
influence producers, wander tables and binary disagreements found on 2026-09-20. This report's
earlier READ/GUESS statements are retained for comparison.

Companion to `ai_out.txt` (the state MAP). Everything here is read from `TPW.BIN` loaded at 0x80010000.
Helper scripts beside this file: `ann.py START END` (annotated disassembly), `fn.py ADDR` (whole
function), `leaf.py ADDR` (to first `jr ra`), `census.py` (every SetState/PushState/PopState site).

Legend used throughout:
- **READ** = taken directly from the instructions at the quoted address.
- **GUESS** = my interpretation of what a field/function means. Confidence given as high / medium / low.
- Tick = one call of the person's Update. The clock is the word at 0x80103a94 (READ: returned by
  0x800BDD18, used everywhere as `now`). Durations below are in ticks of that clock; I have NOT
  established how many ticks make a game second/minute (unknown).
- Person offsets are given relative to the **Person sub-object**. A Visitor has the Person at +8, so
  `Visitor+0x34` is `Person+0x2C`. I write `V+0x..` for Visitor-object offsets and `P+0x..` for Person.

## 0. Corrections to `ai_out.txt`

1. **State 11 is NOT a "walking state with purpose".** The earlier report said "byte +0x2C is a walk
   purpose that the walking states 2, 3 and 11 carry". Read now: the Visitor handler for state 11
   (0x80090994) does nothing but pick idle animations; the Person/Staff handler for 11 (0x800934EC) is
   `jr ra`. State 11 is **"waiting for the pathfinder"** — it is entered right after a path request
   (0x800EC9F4) and left when the path system fires vtable slot 40 on the person, which sets state 3.
   The name table's index 11 = "Idle" was right all along.
2. **Slot 35 is the per-tick handler of state 2 ("Walk to dest"), not a separate "arrival hook".** It
   does double duty: while waypoints remain it steps (via the Person base 0x8009322C), and when
   `P+0x28 == -1` (no waypoint left, READ at 0x8008DB5C..DB68) it switches on the purpose byte. So
   "arrival" is a branch inside the state-2 handler. The earlier description of what it does on arrival
   (switch on P+0x2C) stands.
3. **"44/46 inline with SetState(45) at 0x80091AA8"** was wrong: the Visitor Update table sends 44 and
   46 to the epilogue (no per-tick work at all). The SetState(45) at 0x80091AA8 is in 0x80091A3C, a
   separate function (Visitor vtable slot 16), not in Update.
4. **Entertainer handlers were swapped** in `ai_out.txt` ("32 → 0x80095AFC, 12 → 0x80095E7C").
   READ at 0x80095CD4..0x80095D3C: state **12 → 0x80095AFC** (Entertaining) and state **32 →
   0x80095E7C** (Shocked). The name table's "Shocked" is correct: guests pelt entertainers (§2.1
   case 2, §3.2), and a guard is called to chase the culprit (§3.4).
5. Walking chain, corrected (READ, from the census of every SetState/PushState site):
   `request path (0x800EC9F4) → Push 11 → [path ready: slot 40 → Set 3] → 3 per tick (slot 36,
   0x800932C8) steps to a waypoint → Set 2 → 2 per tick (slot 35): more waypoints? call base
   0x8009322C which Sets 3 again : arrived → purpose switch`. State 1 ("Set random dest") is a
   one-instruction trampoline to state 5 (0x800934CC: `SetState(5)`), and 5 ("Walking") is the random
   wander in 0x80092844.
6. **§2.4 re-read for the port (2026-09-20), all READ.** State 18's `d` is `max(|pref − intensity|, 50)
   >> 1` **with an absolute value**: 0x8009073C..0x8009079C computes `pref − intensity`, branches on the
   sign and recomputes the other way round, then 0x80092118 (= max) and `sra 1`. The floor stands. The
   every-4 and every-8 tick passes in 18 are **staggered by V+0x10** (`now & 3` / `now & 7` against it,
   0x800907CC / 0x800907F0) like the decision; the 2/(100−d) roll is `rand(100−d) < 2`, the 10% is
   `rand(100) < 10`, and the fidget is `rand(300)` then facing `:= rand(4) << 1` then `rand(10) == 0` for
   the sound. **0x8009E118 delivers message 7 to the leaver synchronously** through its vtable slot 40
   (0x8009E180) and then sets flag bit 0x01 (0x8009E194), so the "can't join while in-queue" path of 41
   and 19 does **not** SetState(0) itself — the guest ends in **58**, not 0 (0x8008F778 / 0x8008F64C jump
   straight to the epilogue). Message 2's purpose-3 arm is the same shape (0x8008FAEC). **41 and 23
   SetState(11)** (0x80093F80); only 58 pushes. A refused pathfind in 41, 23 or 58 returns with the state
   unchanged and retries next tick (41 has already listed the guest and set its in-queue bit by then).
   Arrival purposes 3 and 10 also set the in-queue bit (0x8008DDAC / 0x8008DE1C) and test "at the slot"
   by **exact equality** of both 8.8 coordinates (0x8008DDB4..0x8008DDD4). 0x8009D57C's origin is ride
   slot 42 (arg 0) as a tile centre; each member ahead is one 0x40 step, first toward path tile 0 and
   turning toward the next tile whenever the slot sits exactly on a tile's centre; it **fails (returns 0,
   no append)** when it would advance past the last tile or when a step lands on the last tile's tile
   (0x8009D838..0x8009D864) — so +x/+y paths get one step past the penultimate centre and −x/−y paths get
   two. The unload ride arm (0x8008F3E4) has no slot-25 call; its mismatch is `|pref − intensity|` too.
   The constructor rolls the walk speed **once** (0x8008C6CC) into V+0x62 and copies it to V+0x60.
   Arrival purpose 0 clears flag bit 0x01 for type 2 (0x8008DC04, listed above) **and for types 4 and 5**
   (0x8008DCC8, not listed above); the ride arm does not touch it.

7. **§3.4 (guard) corrected in five places (2026-09-20), all re-read from TPW.BIN.** Found by astra
   while porting the class, which kept the text below and flagged the conflict rather than substituting;
   each was then verified independently before the text was changed.
   - **State 33 charges −5 for a culprit that no longer exists**, not −2. The gone test at 0x80097D9C
     branches to the *same* abort block (0x80097DC8) as the queue test and the deadline, and that block
     subtracts 5 via 0x80098808. The −2 belongs only to the state-3 override.
   - **The state-3 override aborts for a queued culprit too** (0x80097A84), sharing its −2 block with
     the gone case (0x80097A94..0x80097ABC). §3.4 listed only "culprit gone" there.
   - **A catch made during a walking step pays nothing.** 0x80097B44..0x80097B84 sends message 4 and
     sets 39 with no stat call between; only state 33's catch pays +10 morale and +3 tiredness.
   - **Arrival 15 increments 0x80103950** (0x80097C7C calls 0x8005996C, which is exactly
     `[gp+0x12FC] += 1`). §3.4 gave the increment to arrival 14 only. Both arrivals AT the gate add one
     and both crossings (0x80059984) take one away, so an ejection round trip balances; the old reading
     lost one per ejection permanently, which is the strongest argument that this correction is right.
   - **Message 2 with purpose 8 ends in 13, not 0.** 0x800979B8 clears the culprit and sets 0, then
     falls into the person base at 0x800942D8, which re-reads the purpose and sets 13 for anything that
     is not 1 or 5. The 0 is real and is overwritten before anything can observe it.

   **Not corrected, because the window does not settle it:** 0x80097F4C..0x80097F54 writes
   `now` into the chase clock (+0x2C) after the pathfinder ACCEPTS a request, which taken alone would
   end the chase a tick or two later regardless of the 3600 set at dispatch. Either +0x2C is refreshed
   deliberately every tick the guard re-paths, or the field is doing double duty. READ that the store
   happens; the consequence is NOT ESTABLISHED and nothing was changed on it.

## 1. Object model needed to read the rest

READ from the setters/getters at 0x80093F80..0x80094160 and the constructor 0x8008C534:

| field | meaning | how known |
|---|---|---|
| P+0x0C | vptr | READ |
| P+0x10 | state-stack depth (int) | READ (SetState zeroes it) |
| P+0x14.. | state-stack bytes | READ |
| P+0x18 / P+0x1A | position x / y, **8.8 fixed point** (256 per tile) | READ: slot 10 (0x800939B0) shifts >>8 to get tile coords |
| P+0x20 | `V+0x28`: pointer to current target object (ride/shop/bin/entertainer); also used as a 16-bit scratch by purposes 11–16 | READ |
| P+0x24 | `V+0x2C`: deadline/timestamp in ticks ("wait until") | READ, GUESS-high |
| P+0x28 | current waypoint index, -1 = none (lh, 0x80093C5C) | READ |
| P+0x2A | thought/speech-bubble id (byte) | READ 0x80093E20; meaning GUESS-medium (ids 0x33,0x34,0x37,0x39,0x3A,0x3B seen) |
| P+0x2B | flag byte: bit 0x01 (set by 0x80093EFC), bit 0x04 (0x80093ECC), bit 0x08 = bubble shown (0x80093E28, counts into global 0x80103264, max 25), bit 0x40 = "skip Update" (Visitor::Update returns early if set) | READ |
| P+0x2C | **purpose** byte, consumed on arrival by slot 35 | READ |
| P+0x2D | state byte | READ |
| P+0x2E | bits 0–2: sub-mode 0..7 (written 0/4 by purposes 14/15); bits 3–7: **animation id** (11 = idle/walk default, 12 = vomit, 13 = wander; random idle anims from table 0x800F7A28) | READ; "animation" is GUESS-medium |
| V+0x38,0x3C,0x40,0x44 | last four ride ids (ctor sets -1) | READ (shift in 0x8008CFF0) |
| V+0x48 | **money**, in units of 1/10 £ (0x80092740 builds `pounds*10 + pence/10`) | READ; "money" GUESS-high (ctor gives £200–£499, leave when < £10) |
| V+0x50 | `now + rand(300)` at spawn | READ; meaning unknown |
| V+0x54 | McAi.field+0x10 at spawn (arrival time on a slow clock) | READ; GUESS-high |
| V+0x58 | **rubbish carried** 0..100 | GUESS-high: ≥90 → seek bin, zeroed at bin, zeroed when dropped as litter |
| V+0x59 | **happiness** 0..100, starts 50 | GUESS-high: leave when <5, -10 when no ride found, -5 on poor choice, <25 → litters |
| V+0x5A | **nausea** 0..100 | GUESS-medium: >92 → vomit; ctor rand(50) |
| V+0x5B | need A (bubble 0x33/0x34 when >90; matched against ride slot 56) | unknown which need; GUESS-low "hunger" |
| V+0x5C | +5 when no ride available | unknown; GUESS-low "boredom" |
| V+0x5D | ride-related desire (>98 → ride goes into history; matched via table 0x800E3A04) | unknown |
| V+0x5E | need B (bubble 0x33/0x32 when >90; matched against ride slot 55) | unknown; GUESS-low "thirst" |
| V+0x5F | **tiredness** 0..100 | GUESS-medium: +1 per walking tick with 10% chance, ≥99 → leave |
| V+0x60, V+0x62 | both = rand(15)+15 at spawn | unknown (age? group size?) |
| V+0x61 | visitor type index into 8-byte table at 0x800F79E8 | READ (used by 0x8008C760) |

All stats V+0x58..0x5F are signed bytes clamped 0..100 by 0x80092190 (add) / 0x800921C0 (sub) /
0x800924F0 (set). Comparators: 0x80092168 `a<b`, 0x80092178 `a>=b`, 0x800921EC `a>b`.

Global helpers (READ): `rand(n)` = 0x800C2648 (`rand()%n`); positional sound = 0x800B8E90(bank, id,
&pos); terrain height = 0x80050938(x,y,0); `0x80059A9C()` returns global 0x80102D34 — a mode flag whose
meaning is unknown (when nonzero: no litter is spawned and Idle skips the needs check).

Pathfinder entry: **0x800EC9F4(person, fromX, fromY, toX, toY, flagA, flagB)** lives in the code block
after the data segment (0x800E8658..0x800EE4A8, not library). Returns nonzero if a request was accepted.
I did not trace its internals.

## 2. VISITOR — the guest loop

Dispatch (READ, 0x800916B4): if flag 0x40 of P+0x2B is set, do nothing. Else call vtable slot 41
(0x8008FE60, the per-tick needs update, §2.9) and then the state handler.

### 2.1 State 0 — Idle (0x8008D058)

**Does:** the guest stands around and, every tick, first decides whether to leave, then rolls one of six
random micro-decisions.

**Leave check (READ, 0x8008D064..D104):** the guest resolves to leave if ANY of
- tiredness V+0x5F ≥ 99,
- happiness V+0x59 < 5,
- money V+0x48 < £10.00,
- (time-in-park ≥ 81 slow-clock units AND rand(20) < 2, i.e. 10% per tick). Time-in-park =
  `McAi+0x10 − V+0x54`, via 0x80091DFC; McAi is the singleton created at 0x80066AC8 (tagged "McAi").

If leaving: when happiness < 3 also show bubble 0x3A and play sound (bank 1, id 0x11) — the "left
unhappy" cue. Then **SetState(38)** (path to the exit, §2.6).

**Otherwise roll rand(6)** (table 0x800E3A64):
0. If `0x80059A9C()` is 0 AND `V+0x2C + 60 + rand(300)` is still in the future AND no target
   (V+0x28 == 0): do nothing. Else: needs bubbles (V+0x5B > 90 or V+0x5E > 90 → bubble 0x33; else
   V+0x5D > 90 → bubble 0x3B; the 0x34/0x32 bubbles are unreachable dead code, READ) and
   **PushState(6)** Make major decision.
1. animation := 13, **PushState(1)** → immediately state 5, random wander.
2. **Pelt an entertainer** (GUESS-high on the label, READ on the mechanics): with probability
   `0x80103230 / 1000` (= 10/1000): find the nearest entertainer (list 0x80053768) within 5 tiles
   (Manhattan). If found: spawn a projectile-like effect from pool descriptor 0x800F77D8 at the guest,
   aimed at the entertainer (angle = ratan2 of the delta), sound (gp-object, 2, 1), and call
   0x80095DCC(entertainer, guest): the **entertainer** loses 5 morale (its +0x48 = Staff+0x40), records
   this guest as the culprit (+0x28), sets a deadline now + rand(5)×60 and is **pushed into state 32
   "Shocked"** (§3.2). The guest's own state does not change. (Watching an entertainer is a different
   path: §2.9.)
3. If rubbish V+0x58 ≥ 90: call 0x8008D764 — nearest bin (list 0x80053248, filter 0x80023FB8 = bin's
   flag bit 0x04 at +0x2E clear) within 6 tiles → purpose := 19, target := bin, path to the tile in front
   of it (offset ±0x80 or ±0x180 by the bin's facing from 0x8006329C), **SetState(11)**. If the path
   request fails → target cleared, stay Idle. **If no bin within 6 tiles: drop litter** (new object
   from manager 0x80103878 via 0x800514E0, placed at pos ± rand(200)−100 by 0x800665FC) and rubbish := 0.
4. If nausea V+0x5A > 92, or 25% (rand(4)==0) regardless: V+0x2C := now+60, animation := 12,
   **SetState(29)** Vomiting, sound (1, 0x18 or 0x19).
5. If happiness V+0x59 < `0x80103260` (= 25) AND rand(1000) < `0x80103234` (= 100): spawn a litter
   sprite (pool desc 0x800F79D8 via 0x80089E38) at the feet, sound (2,1). No state change.

**Exits:** 0→38 (leave), 0→6 (push), 0→1→5 (push), 0→11 (via bin), 0→29 (vomit).

**Constants:** leave thresholds 99 / 5 / £10 / 81+10%; wander/decision cooldown 60+rand(300) ticks;
entertainer radius 5 tiles; bin radius 6 tiles; rubbish threshold 90; vomit threshold 92 (+25% random);
litter: happiness < 25 and 10% per roll; each roll has 1/6 chance per tick.

### 2.2 State 6 — Make major decision (0x8008E240)

**Does:** picks the best ride/shop in the park and requests a path to it. Runs only on ticks where
`now & 7 == V+0x10 & 7` (READ) — i.e. once every 8 ticks, staggered per guest.

1. `0x8008CDC8(guest)` chooses a target (§2.2a). If it returns 0 → **V+0x2C += 360, PopState()** (back
   to Idle; the +360 delays the next decision roll).
2. 10% chance: sound (1, 0x13).
3. Target type (vtable slot 16 of the target): types **1, 3, 6, 7** → destination is the entrance point
   from 0x8009F614(target) (NULL → SetState(0)). Other types → destination = target position +
   half its footprint (0x80062E48 + slot 10). Destination is snapped to tile centre (`(t<<8)+0x80`).
4. purpose := 0, pathfind(guest, pos, dest, 1, 0). Success → V+0x2C := now, **PushState(11)**,
   V+0x28 := target. Failure → V+0x2C += 360, PopState().

**Exits:** 6→11 (push), 6→pop (Idle), 6→0.

#### 2.2a Target choice 0x8008CDC8 (READ)
If V+0x28 already set → return 1. Else iterate every attraction (iterator 0x8006DCA0/DD3C/DE68 walks
seven typed lists, one per slot-16 type 1..7) and for each with **slot 86 nonzero** (GUESS-high: "open
to guests") compute score = 0x8008C818; keep the max (ties broken by coin flip). Then:
- best found: V+0x28 := it. If V+0x5D > 98: non-shop (type≠2) → push its id into the 4-slot history
  V+0x38..0x44 (0x8008CFF0); shop → call its slot 54. If best score < 8: happiness −5, bubble 0x39.
- nothing found: happiness −10, V+0x5C +5, bubble 0x37, return 0.

#### 2.2b Ride score 0x8008C818(guest, ride) (READ formula; meanings of ride slots are GUESSES)
Returns −1 if the ride centre tile fails 0x800508C8 (off-map/unreachable), or if type==2 (shop) and its
slot 54 is 0 (GUESS: no stock / closed).
```
closeness  = 100 − clamp(dist*100 / (mapW+mapH), 0, 100)          # dist = Manhattan tiles
match      = (50 − min(|pref − ride.slot53|, 50)) * 2               # pref = table 0x800F79E8[V+0x61].u16
hasK       = ride.slot53 != 0
s7 = M[V+0x5E/10][ride.slot55/10]      # M = 11x11 int table at 0x800E3820
s5 = M[V+0x5B/10][ride.slot56/10]
s6 = T[(V+0x5D+5)/5] if ride.slot54 else 0     # T = 21-entry table 0x800E3A04
s0 = T[(V+0x5A+5)/5] if ride.slot54 else 0
type-4 only: k = 0x800B6E60(ride); thr = 0x80103258 (k in 2..3) or 0x8010325C (k==6);
             if happiness > thr: s4 = (happiness−thr)*100/(100−thr), w4 = 3
score = (closeness + hasK*match + 3*s7 + 3*s5 + 4*s6 + 2*s0 + w4*s4) / (13 + hasK + w4)
then ÷5, ÷4, ÷3, ÷2 if ride.slot15 (id) equals history slot 1, 2, 3, 4 respectively
```
So: distance, personal-type preference vs ride slot 53, two need-vs-attribute lookups, two desire tables
gated by slot 54, a happiness bonus for type 4, and a strong penalty for repeats. Which ride attribute
each slot is (excitement, intensity, price…) is **not established** — see §5 for the ride vtable work.

### 2.3 State 2 — Walk to dest / arrival (vtable slot 35, 0x8008DB34)

**Does:** if a waypoint remains (P+0x28 ≠ −1): 10% chance tiredness +1, then the Person base step
0x8009322C (which Sets 3 to walk to the next waypoint). When no waypoint remains, act on **purpose**
(P+0x2C, table 0x800E3A94, 23 entries):

| purpose | on arrival (READ) | → state |
|---|---|---|
| 0 | at a ride/shop. No target → 0. Else by target type: **2** → flag bit1 clear, V+0x2C := now+120, Set 35, then if target's slot 54 ≠ 0 play sound (1,0x10); **4,5** → V+0x2C := now+120+rand(300), Set 35; **1,3,6,7,other** → V+0x2C := now, entrance = 0x8009F614(target) (none → target:=0, Set 0), then 0x8008DA14(guest, ride) "join queue" (§2.4): ok → Set 41, fail → Set 0 | 35 / 41 / 0 |
| 1 | animation := 11, Set 0 | 0 |
| 3 | queue walk: 0x8009D57C(ride, guest, &slot) gives my queue slot; fail → Set 0, target := 0. Set flag bit4. At the slot → animation 11, **Set 18** Waiting in queue; else **Set 19** Shuffle forward | 18 / 19 / 0 |
| 4 | target := 0, animation 11, Set 0 | 0 |
| 9 | **leave the park**: 0x800519B0(guest) removes it from the guest manager (0x8010387C) and calls its slot 46; 0x80051D74 tells every staff list about the departure (slot 47 on each staff); waypoints freed (0x80093C68); bubble off | (despawned) |
| 10 | like 3 but not-at-slot → **Set 41** | 18 / 41 / 0 |
| 11 | 0x80059168(V+0x28 as index, guest, &pos) fails → nothing; at pos → Set 44; else Set 42 | 44 / 42 |
| 12 | same but else → Set 43 | 44 / 43 |
| 13 | Set 0; 25%: sound (1, 0x14 or 0x16) | 0 |
| 14 | V+0x28 := 1 (as u16), Set 46, 0x8005996C(), submode := 4 | 46 |
| 15 | V+0x28 := 0, Set 46, 0x8005996C(), submode := 0 | 46 |
| 16 | if V+0x28==0: side := rand(2); if 0x80059150(side) ≥ 11 flip side; Set 42. else V+0x28 := 0, Set 48. Then 0x80059984() | 42 / 48 |
| 19 | at the bin: Set 0, **rubbish := 0** | 0 |
| 22 | Set 0, target := 0 | 0 |
| 2,5–8,17,18,20,21,≥23 | nothing | (stays 2 — see note) |

Note: every arm except 9, 14, 15 ends with purpose := 2 (READ, 0x8008E1EC/E1F0), so a second arrival
tick is a no-op. Purposes 11–16 belong to the turnstile subsystem (§2.6).

### 2.4 Queue-and-ride chain (states 41 → 19/18 → 21 → 22 → 23), all READ

Ride types (byte at building+0x6A, returned by vtable slot 16): **1, 3, 6, 7 have an entrance and a
queue**; **2** is a shop with a stock check (slot 54); **4, 5** are walk-in attractions (guest goes to the
building centre, waits, then "unloads"). The type→class mapping is data, not code, so I cannot say which
type is "roller coaster" vs "shop" from the binary alone.

**Can-join test 0x8008DA14(guest, ride):** for queued types: ride must be open (slot 86: status byte
+0x6E ∈ {2, 10, 11}, via 0x800632D8) and either the guest's in-queue flag (P+0x2B bit 4) is already set,
or `queue_count < 4 × ride[0xF6] + 7` (queue_count = members of the list at ride+0xC4 via 0x8009EE98;
ride[0xF6] GUESS: queue path length in tiles — guests stand ¼ tile apart, so 4 per tile plus 7 slack).
Always true for other types.

**Queue slot 0x8009D57C(ride, guest, &pos):** walks the ride's queue path (tile list at ride+0x70, read
with 0x8009FAC0/0x8001A13C) and the member list (ride+0xC4); each member ahead of me pushes my slot one
step (0x40 = ¼ tile) back along the path; if I am not yet a member, I am appended (0x8009F04C). Returns
my slot in 8.8 coords.

| state | handler | does | exits |
|---|---|---|---|
| **41** join queue | 0x8008F688 | can-join + queue-slot; set in-queue flag; purpose := 3; walk speed V+0x60 := 15; pathfind(…, 0x10, 0) | ok → 11 (then arrival 3: at slot → **18**, else **19**); can't join → if in-queue flag: leave queue (0x8009E118) else target := 0; → **0** |
| **19** Shuffle forward | 0x8008F508 | waits until now > V+0x2C, then re-queries the slot and walks a single waypoint to it (purpose := 10, state **3**). Arrival 10: at slot → **18**, else → **41** | as listed; can't join → leave queue / **0** |
| **18** Waiting in queue | 0x800906EC | see below | boredom > 80 → leaves queue (message 7 → **58**); ride loads → **21** (set by the ride classes at 0x8009C884/0x800A2318/0x800A80F0/0x800B025C); shuffle message 6 → **19** after 3×stagger ticks; message 10 → **0** |
| **21** Loading | 0x8008E538 | clears flag bit 1, sets flag bit 4, hides bubble. Nothing per tick — the ride owns the guest now | ride unload code sets **22** (0x8009CB5C/0x8009DE88/0x800A17F0/0x800A2464/0x800A8BD4) |
| **22** Unloading | 0x8008F110 | applies the ride's/shop's effect (below), then **immediately SetState(23)** at the top of the handler; shops/walk-ins override that with 0 | **23** (rides) / **0** (shops, walk-ins) |
| **23** Goto entrance | 0x8008F7BC | purpose := 4; pathfind to the ride's entrance point 0x8009F614 (flags 0x18, 1); speed := 15 | ok → **11** → arrival 4 → **0**; no entrance point → **0**; pathfind refused → stays 23 and retries next tick |
| **58** removed from queue | 0x800915F4 | pathfind to the ride's leave point (ride slot 26, 0x8009D508), purpose := 22, flags (0x11, 0) | **Push 11** → arrival 22 → **0** |
| **4** On ride | — | **never entered**: no SetState/PushState with 4 and no raw write of the state byte anywhere in TPW.BIN (checked 0x80010000..0x800C0498 and the post-data code). The name table has it, this build does not use it. | — |

**Waiting in queue (18), per tick:** `d = max(pref − ride.intensity, 50) >> 1` (pref = type table
0x800F79E8[V+0x61]; note the `max`, so d ≥ 25 — READ, looks like a bug or a deliberate floor). Every 4
ticks if ride slot 20 is nonzero (GUESS: "ride is broken/closed") boredom V+0x5C +1. Every 8 ticks:
boredom +1 with probability 2/(100−d); tiredness V+0x5F −1 with 10%. If boredom > 80: bubble 0x30,
leave the queue (0x8009E118: removes me, sends message 7 to me, message 6 with a random stagger to
everyone behind me), sounds (gp,3,8) and (1,0x17). Otherwise when now > V+0x2C: fidget (facing bits :=
random), V+0x2C := now + rand(300), 10% sound (1,0x0F).

**Unloading (22) effects (READ, table 0x800E3B54 by type):**
- set flag bit 1, clear in-queue flag; V+0x50 := now + 60 + rand(60); V+0x2C := now + 300 + rand(300);
  speed V+0x60 := V+0x62 (restore normal speed); animation 13; **tiredness −rand(20)**.
- **types 1, 3, 6, 7 (rides):** `m = |pref − ride.intensity|` (ride vtable slot 53). Happiness
  **+15** if m < 21, **+10** if m < 51, **+5** otherwise (bytes at 0x80103210/0C/08). If intensity ≥ 56:
  nausea += `1212 × (intensity − 30) >> 12` (≈ 0.3 per point over 30; +20 at 100). Boredom V+0x5C −=
  intensity (0x80103200 = 4096 → ×1). Then ride slot 25 (0x80099DF4 = no-op for rides) and
  0x80063164(ride) (ride+0x14 += 1: ride's guest counter, GUESS-high).
- **type 2 (shop with stock):** if slot 54 == 0 (no stock) → target := 0, **Set 0**. Else if V+0x5D > 60:
  0x800241E8(shop, (V+0x5D−60)×2/3) (GUESS: consume that many units of stock); V+0x5D := 0; nausea
  −40; shop guest counter +1; if 0x800241BC(shop) < 50 (GUESS: remaining stock %): bubble 0x3C,
  happiness −10, nausea +10. target := 0, **Set 0**.
- **type 4:** 0x8008E5EC(guest, shop) — the purchase routine (§2.5). Then target := 0, **Set 0**.
- **type 5:** 0x8008EE78(guest, shop) — a second purchase routine (§2.5). **Set 0**, target := 0.

### 2.5 Shop purchase 0x8008E5EC (type 4) and 0x8008EE78 (type 5) — formula READ, field names GUESS

Type 4: `want = base × N × (happiness+100)/100 / 100` where base = 0x800B6CBC(shop) (a cost-derived
number ×1.25) and `N = 100 + V+0x5E×slot55/100 + V+0x5B×slot56/100 − nausea×f1/100 +
(100−happiness)×f2/100` (f1 = 0x800B6E18, f2 = 0x800B6D40: per-product data fields, unknown). If
`want > price` (price = u16 shop+0x88 via 0x800B7148) and money ≥ price: **money −= price** (via slot 25
= 0x800B69E0, which also books profit to the finance object 0x80086814), **rubbish += 30 + rand(25)**
(0x8010322C = 30), then per product kind (0x800B6E60, table 0x800E3B14) adjust stats — e.g. kind 7:
V+0x5E += f3/15, V+0x5B −= slot56, V+0x5D += slot56, nausea += f1, happiness += …; kind 1: V+0x5E −=
slot55, V+0x5D += slot55, nausea += f1, V+0x5B += slot56; kind 2: **visitor type V+0x61 := 8** and flag
0x80 set (GUESS: a costume/balloon that changes the model), sound (gp,6,1); kind 3: spawn something
via 0x8005156C and attach it (0x8006B898/0x8006B85C), sound (gp,5,1); kind 6: sound (gp,7,1). Then
happiness += … and the shop's guest counter. Finally a "value verdict" `(want − price)/2` → sound
(gp, 9/0xB/0xC/0xA by kind) and bubble 0x38 if verdict < 2, bubble 0x36 if negative.
If the guest cannot afford it or does not want it: nothing bought.
Type 5 (0x8008EE78): similar shape using 0x800B7758/7764/7744 (price/quality fields) and global
0x80103240 (=100); on purchase happiness += 10 × 0x8009279C(price) (sign) and the shop's slot 25.
I did not resolve the product-definition fields, so treat every "f1/f2/slot55/slot56" as *unknown
per-product numbers from data*, not as named stats.

### 2.6 Park entrance and exit (states 36, 42, 43, 44, 46, 37, 45, 38, 47, 48) — READ

The 0x80059xxx cluster is the **turnstile** system: two lanes, lists at 0x800F2378 (+16 per lane),
counts at 0x80103948+4×lane (0x80059150), entrance building found by 0x8005439C(1, lane+1) (scans the
building table 0x80103930 for the entry flagged 0x04000000; lane 1 picks the one with larger x).

| state | handler | does | exits |
|---|---|---|---|
| **36** spawn → gate | 0x80090CC0 | pathfind to the spawn/entry point 0x800599A4(0) (table 0x800E0D28 with ±213 x-jitter), purpose := 15 | **11**; arrival 15: V+0x28 := 0 (lane), **46**, counter 0x80103950 += 1 |
| **46** at the gate | — (no per-tick) | waits for the park to admit it | message 9 (from 0x800521A0 when 0x80103954 < 11 and 0x80103958 == 1: GUESS "park open") → **47**; message 10 → **0** |
| **47** pick a lane | 0x80091460 | purpose := 16; one waypoint to (my x, gate y) | **3** → arrival 16: lane := rand(2), flip if that lane already has ≥ 11 guests, **42**; if V+0x28 ≠ 0: **48**, counters 0x80103950 −1 / 0x80103954 +1 |
| **42** walk to lane slot | 0x80091100 | 0x80059168 gives my slot (entrance ±0x180/±0x80, minus ¼ tile per guest ahead; appends me to the lane); purpose := 11; speed := 15 | **11** → arrival 11: at slot → **44**, else **42** again |
| **43** shuffle in lane | 0x800911BC | one waypoint to the recomputed slot, purpose := 12 | **3** → arrival 12: at slot → **44**, else **43** |
| **44** front of lane | — | waits | every 32 ticks the turnstile 0x800592CC takes the head guest whose state is 44: **SetState(37)**, lane count −1, and sends message 6 (via 0x80061358 class 0x800E0E9C, params 0 and 43) to everyone else in the lane → their handler sets **43** |
| **37** pay entry | 0x80090EDC | target := 0, speed restored, **SetState(45)** first; fee = finance+0 (0x80087248); if money > fee and the value verdict 0x80090D5C ≥ −1: **money −= fee**, finance income booked (0x80087258), McAi+0x1C += 1 (GUESS: admissions counter). Else **SetState(38)** (leave) | **45** / **38** |
| **45** walk in | 0x800912BC | scan up to 15 tiles in +y from my tile for the first path tile (0x8004D670 then 0x8004D4EC/0x8004D558); purpose := 13; one waypoint | **3** → arrival 13: **0**, 25% greeting sound |
| **38** leave | 0x80090FF4 | if the park has exits (0x800540AC() ≠ 0): every 64 ticks, pathfind to the exit point 0x800599A4(1), purpose := 14, flags (0x21, 0) → **11**; arrival 14: V+0x28 := 1, **46**. If no exits: despawn now | **11** → **46**; despawn |
| **48** walk out | 0x8009154C | pick a random exit 0x800540B8(rand(N)), purpose := 9 | **11** → arrival 9: **despawn** (0x800519B0 removes from the guest manager 0x8010387C and calls slot 46; 0x80051D74 notifies every staff list via slot 47) |

The entry-fee verdict 0x80090D5C: `q = Σ intensity over all attractions × 4096 / (10000/2 + 1 +
rand(5001))`; returns 1 if fee_pounds < q×5120>>12, 0 if < q×6144>>12, −1 if < q×3072>>12 … (order in
code: ≥ 0x80103248 gate first), −2 otherwise; plays sound (gp, 0x13, verdict). Only −2 refuses to pay.

### 2.7 Wander (states 1, 5, 3) — READ (0x800934CC, 0x80092844, 0x800932C8)

State **1** is `SetState(5)`. State **5**: if standing on a path tile: choose `rand(10)` steps; at each
step the four neighbours (offset tables 0x800E3EAC/0x800E3EB0) that are path (0x8004D4EC/0x8004D558) and
connected per the tile's link byte (+2) are candidates; 80% keep the previous direction if it is a
candidate (`rand(100) < 20` re-rolls), otherwise weighted by the 4×4 turn table 0x800E3ECC. Waypoints
are allocated from the pool 0x800F7A38 (4 bytes each, next-index in the low 11 bits, 0x7FF = end) and
chained; purpose := 1; **SetState(3)**. If not on a path: look for a path tile within a ring of
`rand(5)+4`, else four tries at random tiles within ±10 of the map centre with the pathfinder (flags
3, 0) → **Push 11** with flag 0x20 set; if all fails → **0**.

State **3** per tick (0x800932C8): target = current waypoint; facing (P+0x2E bits 0-2) := 2/6 for
−x/+x else 0/4 for +y/−y; `step = speed × timescale >> 14` (speed = vtable slot 44 = **V+0x60**, 15..29;
timescale = word 0x80103A90); move up to `step` in x and in y independently; if the new tile is off the
map → free waypoints, **0**; when position == waypoint → **2** (which advances to the next waypoint via
0x8009322C or, at the end, runs the arrival switch).

### 2.8 Vomiting (29) and Watch entertainer (28) — READ

**29** (0x80090C00): after V+0x2C (now+60 at entry): spawn a litter object (0x800514E0), place it at my
feet ±0.4 tile (0x800665FC) and mark it as vomit (0x8006694C sets obj+0x1C = 0x9E); **nausea := 0**;
animation 11; **SetState(0)**.

**28** (0x80090A50): animation := 2; face the entertainer (V+0x4C) each tick. Ends when the entertainer's
state is no longer 12 (0x800962C4 checks byte +0x35 == 0x0C) **or** now > V+0x2C (set at push: now +
300 + 60 × (entertainer byte +0x3C & 7)): **PopState()**, **happiness +5**, animation 13, V+0x4C := 0,
V+0x50 := now + 900 (no re-watch for 900 ticks).

### 2.9 Per-tick needs update — vtable slot 41, 0x8008FE60 (runs before every state handler) — READ

Every 8 ticks (staggered by V+0x10): tile influence flags at my position (OR of +0x14 of every object in
list 0x80103860 whose radius² (+0x10) covers me): **bit 1 → happiness +6**; **bit 4 → happiness −3 and
nausea +5 while walking (state 2/3), else −1 / +2**; **bit 2 → entertainer area**: if not in a queue,
state-stack depth < 2, now > V+0x50 and not already in 28: V+0x4C := nearest entertainer,
**Push 28**, V+0x2C := now + 300 + 60×(ent+0x3C & 7). (Which objects set bits 1/2/4 is not resolved;
GUESS: scenery = 1, entertainer = 2, litter/ugly = 4.)

Every 64 ticks: for each litter object (list 0x8005366C = manager 0x80103878) within 2 tiles:
**happiness −3**, and **nausea +3 if it is vomit** (obj+0x1C == 0x9E). Then need penalties: boredom
V+0x5C ≥ 95, nausea ≥ 85, V+0x5D ≥ 90, V+0x5B ≥ 95, V+0x5E ≥ 85 → **happiness −1 each** (bytes
0x80103218..28).

Every 50 ticks: **V+0x5B += rand(2)**; every 40 ticks: **V+0x5E += rand(2)** (words 0x80103238/3C).
These are the only growth sources I found for the two needs, so they climb ~1 point per 100/80 ticks.

Every 128 ticks (if 0x80059A9C() == 0): thought bubble selection, first match wins: V+0x5D > 90 →
0x3B (and if > 97 speed := 30 from 0x80103214); nausea > 90 → 0x3D; happiness > 90 → 0x31 + sound
0x15; happiness < 10 → 0x35 + sound 0x12 (0x8008FD64 is the "< threshold" variant); happiness > 80 →
0x3E (+ sound 0x14/0x16 first time); tiredness > 90 → 0x40 + sound 0x17; else 25 < happiness < 75 and
rand(10)==0 → 0x39; else bubble off. Bubble sounds fire with 1/3 chance and only if no bubble is up.

### 2.10 Message handler — vtable slot 40, 0x8008F880 — READ

Messages carry their id in the message vtable's slot 1. Senders found: the queue code (ids 6, 7), the
turnstile (6), the guest manager 0x800521A0 (9), guards 0x80097B48/0x80097EA4 (id from builder
0x8009887C(…,4) — not resolved, GUESS 4), and the pathfinder (1, 2 — sender not located in TPW.BIN; it
is presumably in the post-data block or TPW.OVL).

| id | effect |
|---|---|
| 1 | if state 11: clear flag 0x20, **SetState(3)**, animation 13 (path ready) |
| 2 | if state 11 (path failed), by purpose: 3 → leave queue or target := 0, **0**; 11 → **42**; 14 → if flag 0x20 clear: re-request path to exit with flags (0x23,0), set 0x20, stay 11; else **0**; 15 → **36**; 22 → **0**; any other → **happiness −rand(15)**, boredom +rand(2), target := 0, **5** (wander) |
| 4 | **despawn** (0x800519B0 + 0x80051D74) |
| 6 | if state 44 or 18: free waypoints, V+0x2C := now + 3×param1, **SetState(param2)** (queues send 19, turnstile sends 43) |
| 7 | free waypoints, **58**, clear in-queue flag |
| 9 | **47** |
| 10 | target := 0, free waypoints, **0**, clear in-queue flag |
| 3, 5, 8 | ignored |

### 2.11 Other Visitor vtable hooks (not states)
- slot 47 (0x80091E30)(obj): "obj was removed" — if obj == V+0x28: target := 0, waypoints freed, **0**.
- 0x80091EA8: recovery — if state 11, re-map purpose → the state that will re-issue the request
  (3→41, 4→23, 9→48, 10→19, 11→42, 12→43, 13→0 or 45, 14→38, 15→36, 16→47, 22→58); if 28 → 0.
- slot 16 (0x80091A3C): load from save record (position := exit point, **45**, fields restored,
  visitor type := rand(8)).
- slot 45 (0x80091D80): allocate the model (table 0x800E37FC[(type + V+0x10) & 7]).

### 2.12 Visitor constants summary
| what | value | where |
|---|---|---|
| start money | £200 + rand(300) | ctor 0x8008C5FC |
| start stats: rubbish rand(40), happiness 50, nausea rand(50), V+0x5B rand(70), boredom rand(40), V+0x5D/V+0x5E rand(100)×rand(100)/100, tiredness rand(50) | | ctor |
| walk speed | rand(15)+15, 15 in queues, 30 when V+0x5D > 97 | V+0x60 |
| leave: tiredness ≥ 99, happiness < 5, money < £10, time ≥ 81 (10%/tick) | | Idle |
| decision cooldown | 60 + rand(300) ticks; +360 on failure | Idle / state 6 |
| decision tick | every 8 ticks | state 6 |
| ride pick: score formula §2.2b, repeat penalties ÷5 ÷4 ÷3 ÷2, score < 8 ⇒ happiness −5 | | 0x8008CDC8 |
| no ride at all ⇒ happiness −10, boredom +5 | | 0x8008CDC8 |
| queue cap | 4 × ride[0xF6] + 7 | 0x8008DA14 |
| queue boredom cap | 80 | state 18 |
| ride reward | +15 / +10 / +5 happiness by intensity match; nausea 0.3×(I−30) if I ≥ 56; boredom −I; tiredness −rand(20) | state 22 |
| entertainer | radius 5 tiles (Idle case 2); watch 300+60×k ticks; +5 happiness; 900-tick cooldown | |
| bin radius | 6 tiles; rubbish ≥ 90 | Idle case 3 |
| litter drop | happiness < 25 and 10%; shop buys add 30+rand(25) rubbish | |
| vomit | nausea > 92 or 25% when the roll lands; 60 ticks | |
| litter aura | −3 happiness per litter within 2 tiles per 64 ticks (+3 nausea if vomit) | slot 41 |
| need growth | V+0x5B +0..1 per 50 ticks, V+0x5E +0..1 per 40 ticks | slot 41 |
| unhappy thresholds | boredom 95, nausea 85, V+0x5D 90, V+0x5B 95, V+0x5E 85 → −1 each per 64 ticks | slot 41 |
| lane choice | flip if lane has ≥ 11 guests; turnstile admits one per 32 ticks per lane | |

## 3. STAFF

Layout (READ): a staff object embeds the Staff-base (a Person, 0x30 bytes) at +8, then extra fields.
Written relative to the Staff-base (`S+`): **S+0x38..0x3B patrol rectangle** (x0,y0,x1,y1 bytes, from
the save/assignment record at 0x80094C64), **S+0x3C animation word** (bits 3–7 anim id via 0x800956FC,
bits 0–2 via 0x800956F0), **S+0x3F tiredness** 0..100, **S+0x40 morale** 0..100, **S+0x44 skill/level**
(low 3 bits index the duration tables), S+0x47/S+0x48 unknown. The strike test 0x800681E4 in
`ai_out.txt` averages `100 − S+0x3F` and `S+0x40`, which is why I call them tiredness and morale
(GUESS-high). Every staff Update first calls slot 41 (the Staff-base one, 0x800947C4, is `jr ra`), then
its own switch, then falls through to Staff::Update 0x80094AC4 and Person::Update.

### 3.1 Staff base (0x80094AC4) — READ
| state | handler | does | exits |
|---|---|---|---|
| 0 Idle | slot 48 (per class, see below) | | |
| 13 Patrolling | 0x80095260 → 0x80095104 | if the patrol rectangle S+0x38..3B is non-empty: up to 10 random tiles inside it, first path tile → pathfind (0x11,0), purpose := 1, **Set 11**. | no rectangle / no path → **Set 5** (random wander) |
| 15 Striking | slot 49 0x80094510 | stands; each tick asks IsTypeOnStrike(McAi, myType) | flag clear → animation 13, **Set 0** |
| 26 Walk to strike | slot 50 0x80094378 | up to 10 random tiles within ±2 of building 0x8005439C(0,0) (GUESS: park entrance/office), path tile → pathfind (3,0), purpose := 5, **Push 11**; no such building → **Set 15** directly | arrival 5 → animation 15, **Set 15** |
| 49 go and rest | 0x800947CC | nearest object in list 0x80053248 whose 0x8002433C-flag and status byte +0x6E are set (GUESS: a bench/staff room) → target, purpose := 17, pathfind (0x11,0), **Push 11** | none → **Set 13**; arrival 17 → **Set 50** |
| 50 resting | 0x80094A10 | every 4 ticks: **morale +1, tiredness −2** | tiredness ≤ 0 → flag bit1, **Set 13**, target := 0 |
| 2 (arrival, slot 35 0x80094590) | | purpose 5 → 15; 1 → 0; 17 → 50; walking: **tiredness +1 every 4 ticks** | |
| slot 51 (0x80094698, called from every Idle) | | on strike → slot 54 (abort, 0x80095614) and **Set 26**; else if tiredness > 80 and state 0 → **Set 49**; else if tiredness > 90: morale −1 every 4 ticks | |
| slot 40 messages (0x800942D8) | | 1 → **Set 3**; 2 (path failed): purpose 5 → 0, purpose 1 → 5, else → 13 | |

### 3.2 Entertainer (Update 0x80095C5C; states 12, 32; also skips Update while it is the "held" object 0x80103920) — READ
- **Idle** (0x80095A78): release the aura object (+0x4C) if any; if not on strike → 0x8009598C:
  `rand(3)==0` AND a guest is on my tile or an adjacent one (0x8009580C scans list 0x800536B4 for
  distance² < 2) AND state ≠ 12 → E+0x2C := now, allocate an influence object 0x80053554 at my
  position with flag 2 (0x800961D8) — this is the bit the guest per-tick reads as "entertainer area" —
  **Set 12**. Otherwise **Set 13** Patrolling.
- **12 Entertaining** (0x80095AFC): performs. Quirk (READ): if P+0x08 == 3 and now is odd, tiredness
  +2 and morale +1. After **600 ticks** (E+0x2C + 0x258): if no guest is adjacent any more → **Set 0**.
- **32 Shocked** (0x80095E7C), pushed by 0x80095DCC when a guest pelts it (§2.1 case 2; morale −5,
  culprit in +0x28, deadline now + rand(5)×60): each tick **morale −10**; when the deadline passes:
  nearest guard (list 0x800536D8) within 7 tiles that is not busy (0x8009843C: not in 33/39/15, not
  walking with purpose 5/8/9) → 0x80098494(guard, culprit) sends the guard to **chase** (§3.4); else
  morale −5. Then **PopState** (back to whatever it was doing).

### 3.3 Researcher (Update 0x80099B10; state 31) — READ
- **Idle** (0x800999C0): not on strike → `rand(10) < 3` → **Set 31**, else **Set 13**.
- **31 Research** (0x80099A70): one tick: points = table 0x800E4E44[S+0x44 & 7] = **20/30/35/40/43**
  by skill; 0x8009B618 splits `points × bank+4` evenly across the active research topics (up to 5, 0x1C
  bytes each at bank+0xC; bank = the "BANK" singleton 0x8009B380); then S+0x47 += (bank+4 − 80)/3;
  **Set 0**. So a researcher alternates one research tick with patrol walks.

### 3.4 Guard (Update 0x80098290; states 33, 39, 46, 47, 48, 55, 59) — READ
- **Idle** (0x800981F8): animation 15; not on strike → **Set 13**.
- **33 Chase** (0x80097D5C; entered via 0x80098494: waypoints freed, deadline now + **3600**, target :=
  culprit, animation 30): if the culprit is in a queue (state 18/19/20, 0x800923A8) or the deadline has
  passed → **Set 0**, target := 0, **morale −5**. If I am on the culprit's tile → send it message **4**
  (the guest despawns: thrown out of the park), **Set 39**, morale +10, tiredness +3. Else purpose :=
  8, pathfind to the culprit (0x11,0), **Push 11**; arrival 8 → **Set 33** again. The same catch test
  runs every step in the guard's state-3 override 0x80097A1C (purpose 8; culprit gone → morale −2, Set 0).
- **39** (0x80097F8C): walk to the exit point 0x800599A4(1), purpose := 14, flags (0x21,0) → **11**;
  no exits → **0**. Arrival 14 → +0x28 := 1, **Set 46**, counter 0x80103950 +1.
- **46** at the gate: no per-tick work; message 9 → **47**.
- **47** (0x80098594): one waypoint to (my x, gate y), purpose := 16, **Set 3**. Arrival 16: +0x28 == 0
  → **Set 55**, else → **Set 48**; counters 0x80103950 −1 / 0x80103954 +1.
- **48** (0x80098680): random exit, purpose := 9 → **11**; arrival 9 → target := 0, **Set 59**.
- **59** (0x80098728): walk to the spawn point 0x800599A4(0), purpose := 15 → **11**; arrival 15 →
  +0x28 := 0, **Set 46**.
- **55** (0x8009805C): up to 5 random tiles near building 0x8005439C(0,0) (x ± 3 tiles, y + 1..5),
  path tile → pathfind (0x21,0), purpose := 21, **Push 11**; arrival 21 → **Set 0**.
- messages (0x80097828): 1 → 3; 2 by purpose: 9 → re-path to a random exit once (flag 0x20) else 0;
  21 → 55; 8 → 0; 9 (id) → 47.
So a guard that catches someone walks them to the exit point, leaves the park, re-enters through the
turnstile and takes up a post by the entrance before patrolling again.

### 3.5 Mechanic (Update 0x8009710C; states 14, 16, 17, 52, 54, 56, 57, 58) — READ

> ⚠ **CORRECTION (tinyclaw, 2026-09-19): `0x800E4574` is PAIRS of s16, not a flat array.** The
> durations quoted below — 240/180/120/60/60 — are right, but they are every *other* halfword. The
> real layout is five rows of two: `(240,9) (180,12) (120,14) (60,16) (60,18)`. The second column
> rises as the duration falls, so it improves with skill; nothing in this section reads it and I have
> not traced what does.
>
> Worth the warning because of how the wrong readings fail. As flat **s32** it is
> 590064/786612/917624 — obvious nonsense, caught immediately. As flat **s16** it is 240, 9, 180, 12 —
> the first value correct and the second plausible, which is the reading that survives review. Ported
> in `TPW.Sim.Mechanic.RepairTicks` and `UnknownSecondColumn`.
>
> ⚠ **CORRECTION (tinyclaw, 2026-09-20): three claims in the entry below are wrong and most of the
> machine was unread. Everything here is READ from TPW.BIN unless marked; ported in
> `TPW.Sim.Mechanic`, `TPW.Sim.RideClosing` and `StaffBase.IsCommittedToStrikeOrRest`, sweep in
> `findings/mechanic-mutations.json`.**
>
> 1. **"Service-due" is the UPGRADE queue.** 0x8005BE44 is `0x8005BAF8(0x801099EC, [0x80102D48], me)`:
>    the nearest UNCLAIMED entry of the 15-pointer queue the ride panel's request 0x8005BE14 appends to
>    (ride-panel.md §2). State 54 ends (0x80096AA4) with the PAID 0x8009C56C(ride, 0) and 0x8005BE70
>    pulls the ride off that queue. There is no maintenance job anywhere in 0x8009710C.
> 2. **0x8009C1E8 does not count riders.** It is `A+0xEC >= (5*(w+h))<<13` (0x8009C260..288), w/h being
>    0x8006A798/0x8006A7A4 on the object slot 7 returns; 0x8009EEE4 adds 0x800BDD0C (the per-tick step
>    0x80103A90, capped at 0x4000 by 0x800BDE6C) clamped at that threshold, 0x8009EFA8 subtracts it
>    clamped at 0. State 17 uses a DIFFERENT predicate, 0x8009C294 = `A+0xEC == 0`. Nothing unloads
>    anyone; A+0xEC is a closing timer that scales with the footprint.
> 3. **The coin flip is not "or the reverse".** rand==0: 0x8005BCB0 → 0x80096C58(flag 1), then
>    0x8005BE44 → 0x80096C58(flag 0). rand!=0: 0x8005BE44 → flag 0, then 0x8005BE44 AGAIN → flag 1
>    (0x80096E4C..E64). 0x8005BCB0 is never called in the second order, so half of all idle ticks never
>    look at broken rides. The second 0x8005BE44 runs only after the first found nothing (a found entry
>    is unclaimed, so its flag-0 claim cannot fail) and therefore finds nothing too: dead, ported anyway.
>
> Unread before, READ now:
> - **Claim 0x80096C58(me, ride, flag):** refuses if A+0x54 names someone else; flag 1 additionally
>   requires slot 20 (A+0x6E ∈ {4,5}) and slot 84 (A+0x68 lifetime) **≠ 0** (not > 0); then A+0x54 := me,
>   M+0x28 := ride, Set 56 (flag 1) / 57. 0x8005BCB0 admits rides already claimed by me; 0x8005BAF8
>   admits only unclaimed ones. Only the NEAREST candidate is ever tried: a nearest broken ride that is
>   condemned makes the repair branch report nothing.
> - **56/57/58:** 0x800EC9F4 returning 0 leaves the state untouched (retried next tick, claim held). On
>   success M+0x2C := now, purpose 6/20/22, **Push 11**. 56/57 first call 0x80093C68 (P+0x28 := −1); 58
>   does not.
> - **Arrival (slot 35 = 0x80096AE0):** Set 0 FIRST; purpose 6/20 with M+0x28 == 0 → stays 0 (no ride to
>   close); else M+0x2C := 0, Set 16/52, flag bit 1 := 0. Purpose 22 → M+0x28 := 0. Else base 0x80094590.
>   Still walking with purpose 6: morale −2, tiredness −2 (0x800975E4), then base.
> - **14/54:** `if (M+0x2C != 0 && M+0x2C < now)` — 0x800968B4/0x80096A90: a zero deadline never fires.
> - **17 order:** slot 57(7), A+0x54 := 0, Set 58, flag bit 1 := 1, morale +10.
> - **Messages type 2, purpose 6/20 (0x80096554..5BC):** A+0x54 := 0; then walk the mechanic list from
>   0x80097640(me) = me→next (the +0 link, forward only) and for the FIRST one where 0x80097694 == 0
>   (M+0x28 == 0 and 0x800955B4 == 0) call 0x80096C58(candidate, my ride, purpose==6) — one offer,
>   refused or not — then M+0x28 := 0, Set 0. 0x800955B4: state 26 or 15 → 1; state 2/3 with purpose 5
>   or 17 → 1; otherwise purpose == 0x32 → 1 (no store of 0x32 exists in 0x80090000..0x8009B000).
>   Purpose 22 → M+0x28 := 0, Set 0. Type 3 → nothing (the base ignores it too).
> - **0x8009731C is NOT "job cancelled (ride removed)".** Its only caller is the catalogue pick-up sweep
>   0x800EBA2C (from 0x80058F90; parkopen.md §2 and pathfinder.md name the trigger), run on every person
>   when the player lifts a build object. After 0x80093C68, by table 0x800E467C: states 2/3/11 → purpose
>   20: 0x80096C58(me, my ride, 0); 6: 0x80096C58(me, my ride, 1); 22: Set 58; else release +
>   0x80095358. States 14/16/17/52/54 → nothing. Everything else — including 56/57/58, which fall off the
>   table's end — → release A+0x54 if M+0x28, then 0x80095358 (M+0x28 := 0, 0x80093C68, Set 0, flag
>   0x40 := 1). It re-claims the SAME ride: a re-plan, not a cancel.
> - **Condemned rides (A+0x68 == 0):** never claimed for repair (the slot-84 test); claimed for an
>   upgrade with no check. 0x8009CCD4 raises A+0xEC every tick while A+0x68 == 0 and it is short of the
>   threshold. GUESS-high: a mechanic in 17 on such a ride lowers by the same step the ride raises, never
>   sees 0, and never leaves.
- **Idle** (0x80096D60): not on strike → **tiredness +6, morale +1** (once per idle tick), then 50/50:
  broken ride first (0x8005BCB0) then service-due ride (0x8005BE44), or the reverse. A ride is taken via
  0x80096C58 only if unclaimed (ride+0x54 == 0 or me), and for repairs its slots 20 and 84 are set;
  claiming writes ride+0x54 := me, target := ride, **Set 56** (repair) / **Set 57** (service). Nothing
  → **Set 13**.
- **56** go to broken ride (0x80096E9C): pathfind to ride slot 42 position (0x11,0), purpose := 6, **Push 11**.
  While walking with purpose 6: tiredness −2 and morale −2 per tick (READ, odd but there). Arrival 6 →
  M+0x2C := 0, **Set 16**, flag bit1 cleared.
- **57** go to service (0x80096F70): same, purpose := 20; arrival 20 → **Set 52**.
- **16 Closing ride** (0x8009660C): while 0x8009C1E8(ride) is false (ride+0xEC still above a size-based
  threshold — GUESS: riders still on it) call 0x8009EEE4(ride) each tick (GUESS: stop/unload). When
  true: **Set 14**, ride slot 57(6) (GUESS: status "under repair"), M+0x2C := now + **repair time**
  table 0x800E4574[skill] = **240/180/120/60/60** ticks.
- **14 Repairing** (0x8009682C): animates at the ride; when now > M+0x2C → **Set 17**, 0x8005BE0C (no-op).
- **52** (0x80096900): like 16 but → **Set 54** with the same duration table.
- **54** servicing (0x80096A08): when now > M+0x2C → 0x8009C56C(ride,0), **Set 17**, 0x8005BE70(ride)
  (GUESS: remove from the service-due list).
- **17 Opening ride** (0x80096710): when ride+0xEC == 0: ride slot 57(7) (GUESS: status "open"), release
  the claim (ride+0x54 := 0), **Set 58**, flag bit1, **morale +10** (0x80097610 is add-clamp). Otherwise
  0x8009EFA8(ride) each tick.
- **58** (0x80097044): pathfind to the ride's leave point (slot 26), purpose := 22, **Push 11**;
  arrival 22 → target := 0, **Set 0**.
- messages (0x800964B0): 1 → 3; 2 with purpose 6/20 → release the claim, try the next candidate, else
  0; purpose 22 → 0. 0x8009731C is the "job cancelled" hook (ride removed): re-issue or Set 58.

### 3.6 Handyman (Update 0x80099418; states 27, 51) — READ
- **Idle** (0x80099364): not on strike → 50%: bins first (0x80098F20, falls back to litter), else litter
  (0x80098D44). Nothing → **Set 13**.
- **Seek litter** 0x80098D44: nearest unclaimed litter (list 0x8005366C, litter+0x20 == 0) by Manhattan
  tiles; claim it (litter+0x20 := me), purpose := 7, pathfind to 0x80066A54(litter) (0x11,0), **Push 11**.
  Arrival 7 → H+0x2C := now + **clean time** 0x800E4C38[skill].0 = **120/60/30/20/10**, **Set 27**.
- **27 Cleaning up litter** (0x80099194): when now > H+0x2C: vomit (litter+0x1C == 0x9E) → **morale −6**,
  else morale +1; **tiredness +5**; unclaim and delete the litter (0x80051A94); **Set 0**.
- **Seek bin** 0x80098F20: over list 0x80053248, objects whose slot 33 is true and whose remaining
  capacity 0x800241BC < 60 (i.e. more than 40% full); score = Manhattan distance × (remaining+1),
  lowest wins (near and full); purpose := 18, pathfind to the bin (0x11,0), **Set 11**. Arrival 18 →
  H+0x2C := now + **empty time** 0x800E4C38[skill].1 = **180/120/60/30/15**, **Set 51**.
- **51 emptying a bin** (0x80099264): when done: remaining < 40 (more than 60% full) → **morale −10**,
  else morale +5; tiredness +5; bin capacity := 100 and its +0x78 := McAi time (0x80024210); **Set 0**,
  flag bit1.
- messages (0x80098A94): 1 → 3; 2 with purpose 7 and target type 13 → unclaim, **13**; 5 → target :=
  0, **5**; else base. The third column of the skill table (10/15/20/20/18) is not used by these
  handlers (unknown).

## 4. What I could not identify (honest list)
- 0x800EC9F4 pathfinder internals and who sends messages 1/2 (path ready/failed) — not in the code I read.
- 0x80059A9C / global 0x80102D34: a mode flag (nonzero suppresses litter, needs checks, bubbles).
- Ride attribute slots: 53 = intensity 0..100 computed by the ride class from its base value and two
  tunables (89, 91) clamped 0.75..1.25 (READ at 0x800A0594); 54/55/56 = shop product numbers; 86 =
  status byte +0x6E ∈ {2,10,11}; 20 = status byte +0x6E − 4 < 2 (i.e. 4 or 5) — GUESS "broken"; 25 =
  "sell one" for shops. Which building type byte (+0x6A) is which ride is data I did not decode.
- The two needs V+0x5B / V+0x5E (hunger/thirst or the reverse), V+0x5D (a desire fed by shops, ×4 in
  ride scoring, > 98 pushes the ride into history), V+0x5C (I call it boredom), V+0x50 (a cooldown).
- Tile influence flag bits 1/2/4 — which scenery classes set which.
- Bubble ids 0x30..0x40 and sound ids — labels unknown; the code paths that show them are documented.
- Staff S+0x47/S+0x48 (Entertainer +0x47/+0x48 are tiredness/morale of the embedded Staff base at
  +8, i.e. S+0x3F/S+0x40; I wrote the entertainer/mechanic/handyman numbers using that identity).
