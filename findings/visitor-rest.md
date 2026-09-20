# Visitor remainder — SLES-026.88, 2026-09-20

READ means instructions or data read from TPW.BIN, loaded at `0x80010000`.
Image SHA-256: `0b708dd74796abfedad55e55fa007ace1b5697407d36e8f7823bb06f4fa53beb`.
No executable or asset bytes are included. The earlier [behaviour report](behaviour.md) is retained;
this report distinguishes refinements from disagreements. GUESS labels below are intentional.

## 1. The product need coefficients are now located

**READ:** for **type 4**, all four coefficients are **unsigned bytes in the attraction definition
record**, not global constants and not visitor-type data. `0x80031114` opens the building's container;
`0x800307FC..804` adds container header word `+0x14` to the container base to locate that record.
The shop's vtable begins at `0x800E6A8C`; its entries are adjustment/function pairs, eight bytes each.

| coefficient in §2.5 | record byte | accessor chain | visitor operand |
|---|---|---|---|
| a, slot 55 | +0x33 | vtable function at 0x800E6C48 → 0x800B6D88 → 0x800B70EC (`lbu +0x33`) | V+0x5E, NeedB |
| b, slot 56 | +0x32 | vtable function at 0x800E6C50 → 0x800B6DD0 → 0x800B70F8 (`lbu +0x32`) | V+0x5B, NeedA |
| c, f1 | +0x36 | 0x800B6E18 → 0x800B70D4 (`lbu +0x36`) | V+0x5A, nausea |
| d, f2 | +0x34 | 0x800B6D40 → 0x800B70E0 (`lbu +0x34`) | 100 − V+0x59, happiness |

**READ:** `N = 100 + trunc(NeedB*a/100) + trunc(NeedA*b/100)
− trunc(nausea*c/100) + trunc((100-happiness)*d/100)`.
Each term is divided separately, with signed truncation towards zero
(`0x8008E6FC..7B0`, multiply by `0x51EB851F`, high word, shift 5, sign correction).
The four visitor operands are fetched at `0x8008E5F8..668`; calls selecting a/b/c/d are at
`0x8008E69C..6F8`. These offsets establish the arithmetic inputs without renaming guessed needs.

**GUESS-high:** NeedB is thirst and NeedA is hunger: the Drinks Shop has a=40/b=0; Burger Shop
has a=0/b=25. This is stronger evidence than the original GUESS-low, but it is still a semantic
inference from product names and coefficients. Code retains the neutral NeedA/NeedB names.

**READ:** the 37 type-4 definition records in the extracted FOLIO directory have the following
values, independently checked against the direct FOLIO.GAZ archive (SHA-256
`5666a0eeb18998a31fafbe845e7490cf65da76e722a69aec8b30ff2ee1a3c951`).
Folio indices and English text-table names identify records, **not a promise that all 37
appear in the build menu**. Kind is byte +0x30 (`0x800B711C`). Names such as "Should be removed"
and records named "Drinks Shops" with kind 2 are kept as stored. Do not assign coefficients by kind:
Restaurant 0x043 differs from the later Restaurants; Fries 0x0ED differs from the other Fries Shops.

| folio | English name | kind | a | b | c | d |
|---|---|---|---|---|---|---|
| 0x03C | Costume Shop | 2 | 0 | 0 | 0 | 15 |
| 0x03D | Should be removed | 2 | 0 | 0 | 0 | 15 |
| 0x03E | Drinks Shop | 1 | 40 | 0 | 5 | 5 |
| 0x03F | Cake Shop | 2 | 0 | 0 | 0 | 15 |
| 0x040 | Balloon Shop | 3 | 0 | 0 | 0 | 10 |
| 0x041 | Fries Shop | 7 | 0 | 20 | 10 | 5 |
| 0x043 | Restaurant | 5 | 5 | 15 | 5 | 5 |
| 0x044 | Ice Cream Shop | 4 | 10 | 25 | 15 | 5 |
| 0x045 | Gift Shop | 6 | 0 | 0 | 0 | 15 |
| 0x047 | Burger Shop | 0 | 0 | 25 | 10 | 5 |
| 0x092 | Burger Shop | 0 | 0 | 25 | 10 | 5 |
| 0x093 | Restaurant | 5 | 5 | 25 | 5 | 5 |
| 0x095 | Fries Shop | 7 | 0 | 20 | 10 | 5 |
| 0x096 | Gift Shop | 6 | 0 | 0 | 0 | 15 |
| 0x097 | Ice Cream Shop | 4 | 10 | 25 | 15 | 5 |
| 0x098 | Drinks Shop | 1 | 40 | 0 | 5 | 5 |
| 0x09E | Balloon Shop | 3 | 0 | 0 | 0 | 10 |
| 0x0A0 | Costume Shop | 2 | 0 | 0 | 0 | 15 |
| 0x0E8 | Balloon Shop | 3 | 0 | 0 | 0 | 10 |
| 0x0E9 | Beef & Chips | 2 | 0 | 0 | 0 | 15 |
| 0x0EA | Burger Shop | 0 | 0 | 25 | 10 | 5 |
| 0x0EB | Costume Shop | 2 | 0 | 0 | 0 | 15 |
| 0x0EC | Drinks Shop | 1 | 40 | 0 | 5 | 5 |
| 0x0ED | Fries | 7 | 0 | 25 | 10 | 5 |
| 0x0EE | Gift Shop | 6 | 0 | 0 | 0 | 15 |
| 0x0EF | Ice Cream Shop | 4 | 10 | 25 | 15 | 5 |
| 0x0F1 | Drinks Shops | 2 | 0 | 0 | 0 | 15 |
| 0x0F2 | Drinks Shops | 2 | 0 | 0 | 0 | 15 |
| 0x0F7 | Restaurant | 5 | 5 | 25 | 5 | 5 |
| 0x17D | Burger Shop | 0 | 0 | 25 | 10 | 5 |
| 0x17E | Costume Shop | 2 | 0 | 0 | 0 | 15 |
| 0x17F | Drinks Shop | 1 | 40 | 0 | 5 | 5 |
| 0x180 | Balloon Shop | 3 | 0 | 0 | 0 | 10 |
| 0x181 | Fries Shop | 7 | 0 | 20 | 10 | 5 |
| 0x182 | Gift Shop | 6 | 0 | 0 | 0 | 15 |
| 0x183 | Ice Cream Shop | 4 | 10 | 25 | 15 | 5 |
| 0x186 | Restaurant | 5 | 5 | 25 | 5 | 5 |

Reproduction of the data read: for each raw FOLIO container whose first word is `0x96`, let
`record = container + u32(container+0x14)`; select `u32(record)==4`, then read bytes
`record+[0x30,0x33,0x32,0x36,0x34]`. Resolve `u32(record+4)` in English FOLIO entry `0x197`.
This does not depend on the old `recs.py` guessed u16 field grouping.

**Disagreement retained:** `GuestSpending.Want` implements the report's two divisions last.
The binary instead computes `base = trunc(125 * 0x800B6CBC(shop) / 100)` at `0x8008E680..6C0`,
then `trunc(base*N/100)` at `0x8008E7B4..7E4`, then multiplies by happiness+100 and divides again
at `0x8008E7E8..810`. Early truncation can change the result. This branch does **not** silently
change Want or wire a different spending formula into the existing shop hooks. The new coefficient
reading is supplied for the spending work to consume. Type-5 coefficients are outside this reading.

**READ:** all 121 stored `VisitorTables.NeedMatch` words agree with the image at `0x800E3820`;
all 22 stored `DesireCurve` words agree at `0x800E3A04`. The latter's XML description previously said
21 entries but the stored array has 22 (indices 0..21); stat 100 needs index 21. The adjacent word at
`0x800E3A5C` is also 100, but adjacency alone does not prove the declared table length.

## 2. Influence producers and remaining guesses

**READ, settles bit 2:** the entertainer's performance-entry arm allocates from `0x80053554`
at `0x800959D4`, stores its influence reference at E+0x4C, and passes **2** to `0x800961D8`
at `0x80095A34..38`. That helper is exactly `sw a1,0x14(a0)` at `0x800961DC`.
The radius helper at `0x800961E0` receives 1 at `0x80095A28..2C` and stores its square.
The old §2.9 scenery/entertainer/litter naming remains present in behaviour.md as GUESS;
`TileInfluence.Entertainer` now cites the producer as READ.

**READ, refines bit 4:** Idle roll 5 is the producer of an unpleasant **particle influence**.
At `0x8008D710..724` it passes descriptor `0x800F79D8` to `0x80089E38`.
The descriptor's +8 callback is `0x8008C2B4` (word `0x800F79E0`), invoked at
`0x80089E78..8C`. This callback allocates an influence object, converts its position to tile
coordinates (`0x8008C2E8..308`), sets squared radius 1, stores zero at +0x18,
and passes **4** through `0x8008C3CC` (`0x8008C328..330` → `sw a1,+0x14`).
Descriptor +12 is `0x8008C348`, which releases the influence.

**Not established:** what the particle visually depicts, or a producer for pleasant bit 1.
Calling bit 4 *all litter* would be unjustified: ordinary litter/vomit also has a separate list scan
in §2.9. The prior Idle port's `DropLitter` abstraction is retained. This reading identifies the
specific producer without turning a visual guess into a fact.

**READ:** `0x800535B4` ORs the flags of covering influence objects. Its predicate
`0x800614D8..538` compares squared distance **<=** stored radius squared, including the boundary.
The park supplies `INeedsWorld.InfluenceAt`; the guest applies +6 for bit 1, then bit 4's penalties,
then tries watching for bit 2. `0x8008FF0C..1C` tests raw states **2 and 3**, agreeing with §2.9.
The previous C# check of **1 and 11** was a port error, now corrected, including its old fixture.

**READ:** the entertainer scan is Manhattan distance (`0x80090078..B8`); strict improvement keeps
first-in-list ties. It has no distance limit or performance-state filter. V+0x4C is stored **before**
Push 28 at `0x800900E4..F8`. The skill getter is `lbu +0x3C; andi 7` at `0x800956F0..F8`.
The deadline is now+300+60*skill (`0x80090110..124`). `VisitorNeeds` now owns this selection and
arithmetic; it no longer asks the world to manufacture an arbitrary watch duration.

**Disagreement retained:** §2.9 describes the 64/50/40/128 passes without staggering, and
VisitorNeeds documents its decision to implement those bare periods. `0x80090128..13C` compares
`now & 63` with `V+0x10 & 63`, so at least the 64-tick pass is staggered in the binary.
This branch preserves the report's existing cadence. It does not silently expand the eight-tick fix
into a rewrite of the other need passes.

**READ, bubble-to-artwork lookup established:** Person drawing reads P+0x2A through
`0x80093E00` at `0x80093748`, passes that byte to `0x800BDCD4`, and draws the returned sprite
at `0x80093790`. The lookup is `global_sprite_table + 12*id` (`0x800BDCD4..CE8`), with no remap.
`0x800BDC70..88` loads FOLIO **0x1A0 (416)** and stores its sprite table in gp+0x1434.
Decoding that sheet with the existing `TextureSheet.TryParse` / `RenderSprite(id)` gives:

| id | artwork description (GUESS-high visual reading of the decoded sprite) |
|---|---|
| 0x32 | drink cup with straw |
| 0x33 | drink cup and burger together |
| 0x34 | burger |
| 0x3B | male/female figures, the toilet pictogram |

**GUESS-high:** these are thirst, both hunger/thirst, hunger, and toilet need respectively.
The 0x3B artwork independently supports rides.md §0's existing GUESS-high toilet-need reading
of V+0x5D, rather than the earlier ride-desire interpretation. The field is **not renamed** in
this branch. The numeric lookup is READ; assigning human need names from pictures is explicitly
an inference. The decoded images remain local analysis artifacts, not committed game assets.
The 0x34/0x32 arms in Idle remain unreachable per the report; their artwork does not make those
branches reachable.

## 3. Wander, walking, vomiting and watching

**READ:** state 1 (`0x800934CC`) calls **SetState(5)** at `0x800934D4..D8`.
Although Idle pushes state 1, this clears that stack. It does not pop back through Idle's push.

**READ:** wander neighbour offsets are signed halfwords in eight-byte records at
`0x800E3EAC` (+x) and `0x800E3EB0` (+y): `(+1,0),(0,+1),(-1,0),(0,-1)`.
Outgoing link masks in that order are bytes **4,16,64,1**, at `0x80103268`.
`0x80092E4C..60` copies them; `0x80092F4C..5C` tests them against the **current** tile's links.
A neighbour must be in bounds and pass `0x8004D4EC` or `0x8004D558`.

**READ:** the 4×4 signed-word turn table at `0x800E3ECC` is:

```
100  40  10  40
 40 100  40  10
 10  40 100  40
 40  10  40 100
```

Rows are the previous direction, columns the candidate direction. Initial previous direction is
unset (`0xFFFF`, `0x80092E6C`); the first weighted selection draws **rand(4) for its row**, even
with a single candidate (`0x80092FB4..FC4`). Once a previous direction is available, **rand(100)>=20**
keeps it; if unavailable, no keep die is drawn. Candidate order remains +x,+y,-x,-y.

**READ, deliberate oddity:** weighted selection advances while **cumulative < roll**
(`0x80093038..3C`, `0x80093074..78`). Equality stays with the earlier candidate; the first interval
gets an extra outcome and the last loses one. This is not conventional weighted sampling.

**READ:** every chosen tile becomes a centre waypoint, including collinear tiles. Allocation runs
backwards from the destination (`0x80093148..1DC`) after freeing the old chain (`0x80093124..144`).
If allocation fails, the already allocated **far suffix survives** (`0x80093180` → `0x800931D4`).
Zero steps, no neighbours, or no free entries still writes purpose 1 and sets state 3.
The nearby-path arm instead goes Idle when its single allocation fails (`0x800929C4..9F4`).

**Disagreements retained in the off-path arm:**

| behaviour.md §2.7 | binary READ | choice in this branch |
|---|---|---|
| ring of rand(5)+4 | four rays in +x,+y,-x,-y order, distances 0 through radius−1; rays stop at blocking terrain (`0x80092918..A68`) | named world query for the report's ring; tie/traversal order remains unestablished |
| four random centre attempts | counter starts at 4 and decrements through zero to −1 (`0x80092A70`, `0x80092B04..10`): **five** attempts | four |
| map centre | both centre components call the **width** getter `0x80053FB8` (`0x80092A6C..7C`), not width/height | width/2, height/2 as reported |
| all fail → Idle | `0x80092B14..DC0` builds an additional rand(10)-step grass wander | Idle |
| path tile at start | starting tile tests additionally accept `0x8004D57C` and `0x8004D5A0` (`0x800928B0..C8`); neighbour test remains two predicates | report's path predicate |

**READ refinements:** centre candidates draw rand(20)−10 per axis, i.e. −10..+9; out-of-map/non-path
candidates spend an attempt without a request (`0x80092A80..AC8`). Accepted requests carry flags
(3,0), write bit 0x20, purpose 1 and the request tick, then **Push 11** (`0x80092E18..40`).
A found nearby path is a direct single centre waypoint (`0x80092DC4..E14`).

**READ:** the supplied `VisitorWalking` implements state 3 and the existing arrival world's
base-step delegate. It uses the low multiply word and logical `>>14` (`0x800933A8..B4`), clamps
both coordinate deltas independently, and stores signed halfwords. Diagonals get a full step on both
axes. An invalid new tile frees the chain and sets Idle without committing the new position
(`0x80093454..78`). Reaching both coordinates, or having no waypoint, sets state 2. The arrival
base step frees exactly the reached head, installs its successor and sets animation 13/state 3,
even when the successor is absent (`0x8009322C..288`). The existing purpose switch remains in
`VisitorArrival`; `IVisitorWalkWorld` owns the shared pool and positions.

**READ:** state 29 waits through equality (`0x80090C20`). Litter placement draws rand(200)−100
in x then y and stores signed halfwords (`0x80066608..54`), sets kind **0x9E** (`0x8006694C`),
then clears nausea, writes animation 11, **Set 0**. Allocation failure branches to the same recovery,
skipping position lookup/placement dice (`0x80090C38` → `0x80090C6C`). Mode suppression is in the
allocator `0x800514F0..508`; it does not prevent the guest from recovering.
Idle now writes animation 12 when entering 29, as §2.1 and `0x8008D620..630` specify;
the earlier Idle port omitted that write.

**Disagreement retained in existing Idle entry:** §2.1 says nausea>92 **or** rand(4)==0.
`0x8008D5D8..5F8` calls the strict greater helper `0x800921EC` and draws rand(4) only if nausea>92,
so its branch requires nausea>92 **and** the one-in-four roll. `VisitorIdle` retains the report's OR;
this branch ports the requested state-29 completion without changing that established entry contract.

**READ:** state 28 sets animation 2 and faces the selected entertainer every tick, including its
finishing tick (`0x80090A80..B5C`). Both positions come through slot 10, which is
`0x800939B0` in the visitor vtable (`0x800E36D0`); it shifts signed 8.8 coordinates down
to **tiles** (`0x800939D4..EC`). Facing therefore ignores sub-tile offsets. x has priority over y: −x→2, +x→6, +y→0, else→4; coincident
positions therefore face 4. End if staff state is not 12 or **now>deadline** (`0x80090B74..90`):
**Pop**, happiness+5, animation 13, clear V+0x4C, V+0x50=now+900 (`0x80090BA4..BE4`).
The selected staff reference must remain valid through the callback: the binary dereferences it
before checking state. No substitute target or position is invented when the host loses it.

## 4. One guest message table

**READ:** visitor vtable slot 40 is always `0x8008F880`; table `0x800E3B74` dispatches on id.
These are not different handlers installed by different states. `VisitorMessages` now owns the
whole dispatch table, delegating established rows to the entrance and queue implementations.
The previously missing path-failure/purpose-3 arm lives there. Existing narrow entry points remain
available; a host that needs all messages uses `IVisitorMessageWorld`. Arrival is **slot 35** and
has a separate purpose table, not its own copy of this message table.

| raw id | state gate | result |
|---|---|---|
| 1 | 11 only | clear 0x20; Set 3; animation 13 |
| 2 | 11 only | purpose subtable below |
| 3 | none | ignored |
| 4 | none | despawn via 0x800519B0 then 0x80051D74 (world removal contract) |
| 5 | none | ignored |
| 6 | 18 or 44 only | free waypoints; deadline=now+3×param1; Set param2 (19 ride queue, 43 turnstile) |
| 7 | none | free waypoints; Set 58; clear in-queue bit; keep target |
| 8 | none | ignored |
| 9 | none | Set 47; the sender filters 46, the handler does not |
| 10 | none | clear target; free waypoints; Set 0; clear in-queue bit |
| all others | none | ignored |

**READ:** message 2's purpose table is at `0x800E3B9C`, dispatch `0x8008F9CC..FB94`:

| purpose | effect |
|---|---|
| 3 | if in queue: call LeaveQueue, synchronously delivering 7 → 58, retain target, set flag 1; otherwise clear target and Set 0 |
| 11 | Set 42 |
| 14 | with retry bit set: Set 0; otherwise request park exit point 1, flags (0x23,0); if accepted, save now, set retry bit, Set 11; if refused, remain in 11 with bit clear |
| 15 | Set 36 |
| 22 | clear target; Set 0 |
| every other purpose | happiness−rand(15), boredom+rand(2), clear target, Set 5 |

The **accepted-only** retry bit rule is the prior entrance port's READ refinement
(`0x8008FA60..A9C`); it remains unchanged here. The synchronous queue removal contract is
behaviour.md §0 item 6 / `0x8009E118..194`. The guard's message-4 sender was originally GUESS;
behaviour.md §0 item 7 already resolves it. No new guessed message id is added.

## 5. Validation

Baseline: **784** passing sim tests. The added rejecting tests cover the two new activity handlers,
the complete message entry point, nearest selection and its gates, all 16 turn weights, edge/link
filtering, RNG bounds/order, zero-step and exhausted-pool cases, and the state-3/2 movement contract.
Every new test states the implementation it rejects. Final suite: **902 passed, 0 failed** with
`dotnet test tests/TPW.Sim.Tests/`.

**208 distinct production mutations killed, 0 survived, 0 invalid.** Each changed one rule, ran the
visitor tests, required an actual failing test (compiler failures do not count), and restored the
source. There were 217 executions: the initial 199, followed by nine new refinement mutations and
nine facing mutations rerun after the final coordinate audit. Both restored visitor runs passed.
The final audit also added rejecting tests for whole-tile watch facing and Idle's animation-12 write.

The [per-rule results](visitor-rest-mutations.json) record every distinct mutation, an example failing
test, and its execution count. Reproduce all 208 with `python3 tools/mutate_visitor_rest.py`;
use `--only TEXT` for a subset.
The runner stores individual failing-test logs outside the checkout and restores sources in `finally`.
