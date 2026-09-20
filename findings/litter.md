# Litter and the handyman — SLES-026.88, 2026-09-20

**READ** = instructions/data in `/home/ec2-user/tpw/ext/TPW.BIN`, loaded at `0x80010000`.
Image SHA-256: `0b708dd74796abfedad55e55fa007ace1b5697407d36e8f7823bb06f4fa53beb`.
**GUESS** = interpretation, with confidence. Object offsets below are **outer pool-pointer** offsets;
the drawable base is object+8. This matters: a guest's raw position is outer+0x20/+0x22, but a litter
piece's is outer+0x18/+0x1A. Companions: behaviour.md §§2/3.6, visitor-rest.md, staff.md, shop-stock.md.

## 0. SOURCE DISAGREEMENTS

The existing findings' rules remain in code. This report records the binary separately; it does not
authorise silently changing the earlier port. Boundary interpretations are distinguished from definite
contradictions. Rechecked disagreements already recorded elsewhere are repeated here for review.

1. **behaviour.md §2.1/§5 and states.json state 0 →29: nausea >92 OR rand(4)==0.** READ `0x8008D5CC..5F8` starts the result at
   zero, skips the random draw unless the `>92` comparison succeeds, and requires rand(4)==0:
   **AND**, not OR. `VisitorIdle.RollVomit` retains OR, including its different dice consumption;
   visitor-rest.md already recorded this. High nausea is not a guarantee in the binary; low nausea
   cannot trigger this branch. Stat label “nausea” remains **GUESS-medium** as in behaviour.md;
   the offset, comparison, state 29 and its reset are READ.
2. **behaviour.md §2.1 roll 5/§5 “litter sprite” and “litter drop happiness <25 and 10%”.** READ
   `0x8008D710..724` calls particle factory `0x80089E38` with `0x800F79D8`, not litter allocator
   `0x800514E0`. Its callback `0x8008C2B4` creates a radius-squared-1 influence and writes bit **4**
   (`0x8008C328..330`); destructor `0x8008C348` removes that influence. It is **not a piece in the
   handyman's litter list**. Its visual identity remains NOT ESTABLISHED, not renamed “rubbish” or
   “vomit”. `VisitorIdle` retains `DropLitter` for this case, as visitor-rest.md explicitly retained
   it. Consequently a host connecting that hook to this pool creates persistent misery litter in the
   port. That is a deliberate findings/binary divergence, not newly discovered binary behaviour.
3. **behaviour.md §1/§4: mode flag suppresses litter everywhere.** READ allocator `0x800514F0..50C`
   does suppress persistent pieces. Roll 5's particle path `0x8008D6B0..738` has no `0x80059A9C`
   call; particle allocation `0x80089E38` and its influence constructor are distinct from that gate.
   The earlier `VisitorIdle` suppression of roll-5 `DropLitter` is retained. The narrower statement
   “suppresses the persistent litter allocator” is established; applying it to that particle is not.
4. **behaviour.md §2.9/§5: every 64 ticks; no stagger specified.** READ `0x80090128..13C` compares
   `now & 63` against `V+0x10 & 63`. `VisitorNeeds` retains its documented bare-tick cadence. This
   disagreement was already recorded by visitor-rest.md. Claiming the report's cadence is the binary's
   would hide a material difference: all guests take damage together in the current port.
5. **behaviour.md §2.9/§5: “within 2 tiles”.** READ `0x800901B8..1F8` sums absolute differences of
   whole-tile coordinates and tests **distance <2**, so only the same tile and its four orthogonal
   neighbours count. **GUESS-medium:** the prose's natural inclusive reading is distance <=2; it did
   not explicitly spell out equality. `LitterPool.Nearby` preserves that reading and its XML/tests flag
   the boundary. It is not silently presented as an instruction-level <= comparison.
6. **behaviour.md §2.1 bin search: record bit 0x04 “clear”.** READ `0x80024330..338` returns
   `record+0x2E &4`; `0x8008D7D8..7E0` skips a zero result. It selects **set**, agreeing with the
   later behaviour.md §0 bit table and shop-stock.md §8. The prose contradicts itself. There is no
   bit predicate in the existing `IVisitorWorld.TryWalkToBin` abstraction to replace on this branch.
   Its “within 6” is another boundary ambiguity: READ `0x8008D888..890` requires distance **<6**.
   The existing world contract remains “within 6”; no new bin finder is introduced here.
7. **behaviour.md §3.6 bin score:** READ `0x80099018..050` computes **|dx| + |dy|*(remaining+1)**,
   not **(|dx|+|dy|)*(remaining+1)**. The existing `Handyman.BinScore` retains the report's formula;
   shop-stock.md §0 already details the branch-delay multiply. Likewise rides.md §1.3's GUESS that
   handyman filtering uses bin bit 2 was superseded by READ slot 33 → `0x80023E60` → `0x80024348`
   (bit 0), already documented by shop-stock.md. No conflicting new formula/filter is added.
**Code-only correction in this branch:** `Handyman.UnusedThirdColumn`'s XML used to say “nothing
reads it”. READ `0x80098A68..88` reads column 2 as movement speed; staff.md §2 already established
this and `StaffMotion` already uses it. behaviour.md's narrower “not used by these handlers” is true.
The array/name are retained for compatibility; its documentation now explains it is not mowing data.

## 1. What creates persistent litter?

**READ:** the complete direct-call census of `0x800514E0` has three sites:

| caller | what creates it | position/kind |
|---|---|---|
| `0x8008D9B0`, bin search | Idle selector rand(6)==3, carried byte V+0x58 >=90 (`0x8008D5A0..5C0`), no eligible bin in range | guest raw position, two scatter draws, ordinary random sprite |
| `0x80090C2C`, state 29 | after now is strictly greater than deadline; entry set now+60 | same scatter, then sprite overwritten with **0x9E** at `0x80090C60..68` |
| `0x80053AFC`, load helper | restore the saved ordinary/vomit counts | random tile of type 2/4/7, centred and scattered, then optional 0x9E |

**READ:** no additional probability is rolled for the no-bin drop: conditional on staying Idle,
carrying >=90 and having no nearby bin, its selector chance is **1/6 per tick**. Bin path refusal
clears the target but retains the rubbish (`0x8008D9A8..9AC`); absence of a bin attempts allocation
then clears rubbish **even on allocation failure** (`0x8008D9B0..9F0`). The names “rubbish carried”
and “happiness” remain **GUESS-high** from behaviour.md; the byte operations are READ.

**READ:** binary vomiting chance, conditional on remaining Idle and V+0x5A>92, is **1/6 * 1/4 = 1/24**
per tick; it is zero from this action at lower V+0x5A (§0 explains the retained OR port). State 29
waits through equality, attempts allocation once, and resets nausea/animation/state regardless of
success (`0x80090C20..CA8`). It does not clear carried rubbish. There is no nauseousness stored on a
piece: `0x8006694C` writes 0x9E, `0x80066958` compares it. Even vomit consumes rand(6) for an
ordinary sprite during allocation, **then** rand(200), rand(200), **then** overwrites the sprite.

**READ:** shop purchases increase the carried byte by `30+rand(25)` (behaviour.md §2.5,
`0x8008E890..8AC`); this is not an allocation of an object. `GuestSpending.LitterDropped`'s name
must not be interpreted as an immediate world drop.

**READ:** misery roll 5 has its own 1/6 selector and `happiness<25 && rand(1000)<100` gate
(`0x8008D6B4..6E4`, words `0x80103260=25`, `0x80103234=100`), hence 1/60 conditional per Idle tick.
It is the particle in §0, not a fourth persistent-litter caller. Its unpleasant influence has a
separate guest effect: bit 4 costs happiness 3/nausea +5 in walking states 2/3, otherwise happiness
1/nausea +2, in the staggered 8-tick influence pass (`0x8008FED4..FF78`). Do not charge that aura
again for every ordinary litter piece.

## 2. The object, pool and lifetime

**READ:** `0x80050288..2B4` allocates **0x5B0 bytes**, names it **PoolOfLitter** (`0x800E0C90`),
and stores the pool at `0x80103878`. Constructor `0x8005F5F8..65C` constructs **40** objects,
stride **0x24**, after a **0x10** header. This address is litter, not a generic prop pool.

| outer offset | field | evidence |
|---|---|---|
| +0/+4 | next/previous in free or active pool list | `0x8005F6F8..764`, `0x8006696C..9B0` |
| +8/+0xC | links in common drawable list | `0x80053C04`, `0x80061A54/5C` |
| +0x10 | shared instance id | base init `0x80099D58..6C`, called with object+8 |
| +0x14 | vtable `0x800E13A0` | `0x80066524..534` |
| +0x18/+0x1A | signed 8.8 X/Y, two halfwords | placement `0x800665FC..654`; raw getter `0x80066A54` |
| +0x1C | global sprite id, word | init `0x800665CC..5EC`, vomit setter `0x8006694C` |
| +0x20 | handyman claim pointer (drawable base), zero means unclaimed | `0x800669BC/C4`, `0x80098E80` |

**READ:** ordinary sprite table `0x800E14B8` is **9A, 9B, 9C, 9D, A0, 9F**, indexed by rand(6).
The non-numeric order matters. Their individual pictured rubbish identities are NOT ESTABLISHED.
Type getter `0x800669B4` returns **13**. Slot 10 `0x800669E8` returns each signed position >>8.
Placement uses rand(200)-100 on x, then y, with halfword wrapping. It does not snap to a tile,
check the map, or reject a non-path result.

**READ:** allocation first checks the suppression mode and empty free list (`0x800514F0..50C`),
then takes its head, increments pool live count and inserts at live-list head (`0x8005DD20..D6C`).
Initialisation resets the claim, consumes the shared id and chooses a sprite; it does **not** zero
old coordinates (`0x8006659C..5FC`). The producer supplies the position next. Full pool returns
null, consumes no dice/id, and evicts nothing. The host must place a successfully allocated piece
before drawing it, just as the synchronous producers do.

**READ:** vtable slot 3 points to **`0x80066664`: jr ra / nop**. There is **no decay**. The object
has no age/lifetime field, and its full 0x24-byte layout is accounted for. The only direct caller of
delete `0x80051A94` is cleaning `0x80099230`. Park teardown `0x80050374` also destroys the pool.
Claims do not hide pieces from drawing, counting or nearby penalties.

**READ:** save wrapper `0x80071584` writes just **two bytes**, from `0x80053A10`: ordinary count,
then vomit count. Loader `0x80072938` passes them to `0x80053A98`, which samples x/y until tile
predicate `0x8004D4EC` or `0x8004D510` passes: exactly types **2, 4, 7**
(`0x8004D4FC`, `0x8004D524..544`), not the path/queue overlap type 13. Tile names path, queue and
attraction entrance retain paths.md's **GUESS-high** labels. It centres at `(tile<<8)+0x80`, scatters, and sets vomit
in the second loop. Claims/positions are not preserved. **⚠ DO NOT FIX in a future loader port:**
that helper never checks allocation failure (`0x80053B04`), and its tile retry has no attempt limit.
This branch exposes `SaveCounts`; loading belongs to the host and is not reimplemented with invented
fallback positions or a fabricated retry limit.

## 3. Drawing

**READ:** slot 4 → `0x8006666C`. Sprite lookup `0x800BDCD4` is the global sprite table at
`0x80103A88`, entries of 12 bytes, loaded from FOLIO **0x1A0** (`0x800BDC70..88`, visitor-rest.md).
Descriptor +0/+2 are page/CLUT; +6/+7 are width/height; +8/+9 are U/V.

**READ:** this is a **flat ground quad**, not a billboard. Half extents are
`(width>>1)<<3`, `(height>>1)<<3` (`0x800666B4..6CC`, shift word `0x80102DE4=3`, no other direct
reader/writer found). Each of the four signed-halfword corners asks `0x80050938(x,y,0)` for terrain
height (`0x800666E0`, `0x80066714`, `0x80066748`, `0x8006677C`). Order: -x/-y, +x/-y, -x/+y,
+x/+y. Halving before scaling discards an odd dimension's half pixel; do not “improve” it.

**READ:** RTPT projects the first three, negative GTE flags reject, AVSZ3 supplies ordering depth,
unsigned depth **>=2000 rejects**, and the fourth corner is projected/flag-checked separately
(`0x80066794..848`). FT4 command **0x2C**, RGB **128/128/128**, UV far ends **u+width-1**, **v+height-1**
with byte stores, page/CLUT copied (`0x80066850..8D0`). No semitransparency bit, animation or fade.
`LitterDrawing.Draw` supplies these corners/material/depth contract through `ILitterDrawWorld`;
the host owns projection, GTE-equivalent rejection and GPU submission. No changes under `game/`.

## 4. What cleaning changes

**READ:** nearest **unclaimed** by whole-tile Manhattan distance, with strict improvement and no
range limit (`0x80098DBC..E4C`). Equal distances keep the first active-list entry, hence newest
allocation. A piece already claimed by the same handyman is still skipped. Store target, purpose
7 and claim **before** path request to the raw coordinates, flags `(0x11,0)` (`0x80098E54..EB4`).
Accepted: stamp now and Push 11. Refused: clear both pointers and return failure; no attempt at the
second-nearest piece (`0x80098EBC..EF8`). Later path-failure message 2 does the same release and
sets patrol 13 (`0x80098B24..B9C`).

**READ:** arrival purpose 7 starts **120/60/30/20/10** ticks by skill and sets **27**
(`0x80098C60..CA8`). At **now > deadline**, morale **+1 ordinary / -6 vomit**, tiredness **+5**,
release claim, unregister drawable, return pool slot, clear staff target, Set 0
(`0x80099194..250`, deletion `0x80051A94`). All existing stat clamps remain in effect.

**READ, bounded negative:** the complete cleaning/deletion path writes staff stats, claim/target,
and the two lists/count. It calls **no bank, tile-dirt, park-score or guest-stat setter**. Cleaning
does not refund happiness already lost. Its guest benefit is removal from later scans, and its
park statistic benefit is one fewer live piece on the next statistic-12 refresh.

## 5. What dirt costs

**READ:** it **does cost guest happiness**, including outside Idle. Slot 41 `0x8008FE60` scans
all active litter `0x80090144..244`. At the binary's staggered 64-tick pass and distance <2, **each
piece costs 3 happiness; each vomit piece additionally adds 3 nausea**. Claimed pieces count.
Nausea changes before the five unmet-need comparisons, so a pile can push nausea over 85 and cause
another happiness -1 in that same pass (`0x80090270..298`). Existing `VisitorNeeds` applies this
with the retained report cadence/radius (§0). Stats clamp; no penalty is multiplied by age.

**READ:** unhappiness can drive departure: Idle tests happiness <5 before its six-way roll
(`0x8008D09C..0B0`). Thus litter can shorten visits without changing arrival desirability.
The position/stat meaning labels retain behaviour.md's GUESS markings; the causal arithmetic is READ.

**READ:** park **statistic 12 = live litter count**, including vomit (`0x800DBA28` → `0x80016CFC`
→ `0x80053690` → `0x8005CD50`, pool+0xC). It is refreshed by the 72-statistic round-robin
`0x80016870`: one statistic per four ticks, full cycle **288** ticks. It is **not statistic 30**,
`FeatureStock.ParkDirtiness = 100 - mean usable-feature cleanliness` (`0x800153B4`, shop-stock.md).
There is no per-tile persistent dirt byte in the litter object or its create/delete paths.

**READ, bounded negative:** there is **no direct litter penalty in arrival score or bus head-count**.
`0x80067400..58C` iterates attractions only and uses type, random draw, age, level and intensity;
`0x80067274..3FC` combines that score with lane counts and population cap. Neither reads the litter
pool, statistic 12, guest happiness, or feature dirtiness. The bus phase logic `0x8005262C..7D4`
uses its timing/hold inputs, not litter. Shared RNG consumption and a changed guest population can
still change a later run; this is not a promise of identical future bus loads after adding litter.

**NOT ESTABLISHED:** a universal additional “park rating per pile” or level-specific penalty.
Statistic 12 is available to the data-driven evaluator (`0x80017078..114` reads indexed statistics;
`0x80017124..1D0` can write statistics/post messages/change game state). This trace does not decode
every level script, so it cannot rule out script-driven objectives/advice or assign their thresholds.
No numerical rating penalty is fabricated in code. The established costs above are already real;
“litter is cosmetic” would be false.

## 6. Second job and dead states

**READ:** handyman Update `0x80099490..510` dispatches exactly **0**, **27**, **51**, otherwise
Staff base. Job arrivals are purpose **7 →27** and **18 →51** (`0x80098C40..D14`). The second
job is **emptying/cleaning usable features**, not mowing. It selects feature slot 33, i.e. record
bit 0, remaining cleanliness <60; after **180/120/60/30/15** ticks it refills to 100 and stamps the
day; morale **+5** if remaining >=40, otherwise **-10**, tiredness **+5** (`0x80098F20`,
`0x80099264`, `0x80024210`; shop-stock.md). Toilets are included, not just litter bins.

**READ:** state **25**, name “I will clear litter”: name-switch entry `0x800E24D8`
→ `0x80077DD0..DD8` returns the string at `0x800E2360`. It has **no Set/Push writer** in
the executable's direct-call census. Staff jump-table entry `0x800E416C` → `0x80094BA4` → Person
`0x800934F4`; Person only handles states 1..11, so 25 performs no job. It is retained as
`StaffClassStates.DeadClearLitter`. States 27 and 51 have the two live writers `0x80098CA4` and
`0x80098CF8`. There is no mowing handler, mower target, grass timer or grass-editing call in these
job paths. This is a bound on **this build's handyman**, not a claim about other releases.

**READ refinement:** third skill-table column **10/15/20/20/18** is walk speed, getter
`0x80098A68..88`, already used by StaffMotion. It is not a third job duration. No mowing state
number or grass-growth constant is established; none is invented or exposed with a plausible value.

## 7. Port, checks and reproduction

- `Litter.cs`: forty-slot pool, exact allocation/scatter/order/refusal, concrete claim/delete helpers,
  nearby counts, two-byte save counts and explicit no-op lifetime.
- `LitterDrawing.cs`: terrain quad and sprite/material/depth submission contract.
- `StaffBase.cs`: real litter target; `StaffClasses.cs`: dead state 25 and corrected speed-table docs.
- Existing guest decision, nausea, happiness and handyman rewards stay in their existing classes.
  The host composes the pool behind `IVisitorWorld`, `IVisitorActivityWorld`, `INeedsWorld` and
  `IHandymanWorld`; `LitterFixture.cs` demonstrates that wiring with one shared random source.
- `LitterTests.cs`: binary-anchored cases and end-to-end guest → piece → needs → cleaner sequences.
  Each test states the alternative it rejects. No changes to `game/` or other worktrees.

Host wiring (the test fixture runs this composition, including a single shared RNG):

| existing hook | implementation supplied here |
|---|---|
| `IVisitorWorld.DropLitter` | `pool.Drop(guestX, guestY, world, rng)` |
| `IVisitorActivityWorld.TryAllocateLitter` | `pool.TryAllocate(world, rng)`; this consumes the ordinary sprite draw even for vomit |
| `IVisitorActivityWorld.PlaceLitter` | `((Litter)piece).Place(x, y, kind)`; do not scatter these already-scattered coordinates again |
| `INeedsWorld.LitterNearby` | `pool.Nearby(guestX, guestY)` |
| `IHandymanWorld.TryClaimNearestLitter` | `HandymanLitter.TryClaimNearest(staff, pool, world)` |
| `IHandymanWorld.ClaimedLitterIsVomit` | `staff.TargetLitter.IsVomit` |
| `IHandymanWorld.UnclaimLitter` | `HandymanLitter.Unclaim(staff)` |
| `IHandymanWorld.DeleteClaimedLitter` | `HandymanLitter.DeleteClaimed(staff, pool, world)` |
| render pass | `LitterDrawing.Draw(piece, world)` for live pieces; host projects/submits the supplied quad |
| statistic 12 | `pool.Count` at the host's statistic refresh |

Reproduce with `python3 findings/fable-scripts/fn.py ADDR`, `ann.py START END` for tiny leafs
(the fn extent heuristic sometimes includes adjacent leafs), `callers.py 800514E0 80051A94
8005366C 80053690`, `xref.py 80103878`, `vt.py 800E13A0 -n17`, and `census.py`.
Mutation driver: `python3 findings/fable-scripts/litter_mutations.py`, with a top-level try/finally
restoring every original source byte even on interruption. Results: `litter-mutations.json`.

Validation: the starting full suite passed **1,219** tests; the final full suite passed **1,262**
(**43** added cases), zero skipped/failed. The only build warning is the existing xUnit2000 warning
in BootSequenceTests.cs. **111 distinct production mutations killed; zero remaining survivors and
zero invalid/compile-error mutations**, across **113 mutation executions**. The first sweep exposed
two weak drawing fixtures: clamping the left/bottom edges survived because the fixture crossed only
the opposite limits. Adding the opposite-corner case killed both on rerun. A separate selection case
pins Manhattan ranking against Euclidean ranking, which the inclusive integer radius-two neighbourhood
alone cannot distinguish. The JSON preserves the initial survivors under `prior_non_kills`, the
original test hashes under `earlier_sweeps`, and the final green suite. Every recorded production
source hash matches the restored file. `--append` reproduces the selective rerun without discarding
that history; running the driver without it executes all 111 mutations afresh.

## 8. Which features a handyman actually services (measured 2026-09-20)

### ⚠ RETRACTION FIRST

An earlier version of this section claimed **"the bin flag is never set in the data"** and that
`ParkView.NearestBinTile` could therefore never succeed. **That was wrong.** The flag is set, on
exactly the four entries `behaviour.md` §0 item 8 already names — 19, 98, 193, 351, one Litter Bin per
world — and `behaviour.md` was right the whole time.

⭐ **The absence was manufactured by my own filter.** The sweep skipped any record whose header offset
left fewer than 0x40 bytes in the file. Litter bins are small records at the end of small files, so
that test dropped **94 of 488 records**, and every bin was among them. With the filter relaxed to the
0x2F bytes actually read, the four bins and the four Security Cameras (30, 105, 185, 350) appear
immediately and match `behaviour.md` entry for entry.

**Scope of the retraction:** only the "never set" claim and everything downstream of it — the
`NearestBinTile` search is fine and finds bins when one is placed. The hand-verified readings below
were checked against the executable, not against that sweep, and are unaffected.

### The four accessors, verified by hand

`ann.py 80024324 80024354` prints the whole set — four identical `lbu v0,46(a0)` / `jr ra` /
`andi v0,v0,<mask>` leafs on the feature record's `+0x2E`:

| address | mask | bit | carried by |
|---|---|---|---|
| `0x80024348` | `0x1` | 0 | guests may use it, **and cleaners service it** (READ, both readers) |
| `0x8002433C` | `0x2` | 1 | the Staff Room (32, 109, 197, 353) + four space-world features |
| `0x80024330` | `0x4` | 2 | the **Litter Bin**, exactly (19, 98, 193, 351) |
| `0x80024324` | `0x8` | 3 | the **Security Camera**, exactly (30, 105, 185, 350) |

Full `+0x2E` histogram over the 182 type-2 records: `0x00 ×130, 0x01 ×18, 0x02 ×12, 0x03 ×6,
0x04 ×8, 0x08 ×8`. **No feature carries more than one of these bits**, which is the part that matters.

### ⭐ SO A HANDYMAN NEVER EMPTIES A LITTER BIN

`0x80098F20`'s filter is slot 33 → `0x80024348` → **bit 0** (verified by hand, and `0x80098FC4:
slti v0,v0,0x3c` gives `BinPickBelow = 60` strictly, inside that same function). Bit 0 and bit 2 are
disjoint in the data: all 8 litter-bin records read `0x04` and not one of them reads `0x05`.

So the handyman's "bin round" is really a **service-the-guest-usable-features** round — toilets and
the like — and TPW's actual Litter Bin object is not in it. ⚠ §4's line "Toilets are included, not
just litter bins" has it exactly backwards and should be read as corrected here: toilets are included
and **litter bins are not**.

⭐ **CONFIRMED FROM PLAY, 2026-09-20.** Asked directly, strawberry_cow — who has played the game —
answered without prompting: *"handymen dont touch bins. just toilets."* That is the same split the two
bits make, arrived at from the opposite end, and it is the kind of confirmation a disassembly cannot
give itself. The bit-0 filter is not a port limitation to be fixed later; it is the behaviour.

He added what the handyman DOES clear: *"they sweep litter and stinkbombs on paths."* Litter is this
pool. ⚠ **"Stinkbomb" has no name in the port and no identified record**, and it is not safe to assume
it is the vomit kind (0x9E) just because both are unpleasant things on a path — the pool's six ordinary
sprites are `0x9A 0x9B 0x9C 0x9D 0xA0 0x9F` and §2 records that their individual identities are NOT
ESTABLISHED. One of them may be it. That is a lead to chase, not a mapping to wire.

⚠ **STILL NOT ESTABLISHED: what a full litter bin does, or whether anything empties it.** The
confirmation above says the handyman does not, and says nothing about what replaces him. Do not wire a
guess.

### Verified by hand for this work

- `0x80098FC4: slti v0,v0,0x3c` — `BinPickBelow = 60`, strictly below, inside `0x80098F20..0x80099194`.
- `0x80024348: andi v0,v0,0x1` — the handyman's filter is bit 0. `rides.md` §1.3's guess of bit 2 for
  this filter is wrong, and wrong in a way that would have looked right: bit 2 IS the bin.
