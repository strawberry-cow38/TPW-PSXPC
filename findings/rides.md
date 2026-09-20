# Theme Park World (PSX, SLES-026.88) — RIDES, SHOPS AND ATTRACTIONS

Fourth report. Companions: `ai_out.txt` (state map), `behaviour.md` (person behaviour), `economy.md`
(money). Same legend: **READ** = taken from the instructions at the quoted address in `TPW.BIN`
(loaded at 0x80010000); **GUESS** = interpretation, with confidence. New helper scripts beside this
file: `vt.py VT…` (side-by-side vtable dump), `fieldx.py OFF…` (every load/store of a struct offset),
`recs.py` (parses every attraction definition record out of `/home/ec2-user/tpw/ext/rip/` into
`records.json` / `records.txt`), `text.py ID…` (English string table), `records_named.txt` (all 197
records with names), `rides_themes.json` (which record belongs to which theme/set).

Units: 1 tick = 1/25 s. **1 game day = 99 ticks = 3.96 s** (confirmed). 20.12 fixed point = value×4096.
Offsets written `A+0x..` are relative to the **attraction sub-object** — the pointer guests, mechanics
and the UI hold. It sits 8 bytes into the pool entry (`outer+8`), exactly like Person inside Visitor;
subclass overrides in the vtables carry delta −8 and take the outer pointer. Ride-class functions
therefore read `outer+0xF8` for `A+0xF0` etc. — I converted every offset below to A-relative.

## 0. Corrections to the earlier reports — read first

1. **`behaviour.md` §2.4 "type 2 is a shop with a stock check"** — wrong. The type enum is the
   `CONTEXT_*` string list at 0x800DDDD4 (pointer table 0x800F1090, READ): **1 RollerCoaster,
   2 Feature, 3 NonPathedRide ("Rides"), 4 Shop, 5 SideShow, 6 PathedRide ("TrackRides"), 7 TourRide,
   8 TrackUpgrade**, 0 Void. Every one of the 197 definition records carries the same number at
   record+0 (READ, `recs.py`). So type 2 is a **Feature**: toilets, bins, benches, scenery, staff
   room. Its "slot 54" is the record flag `rec+0x2E & 1` (0x80023F0C → 0x80024348, READ: "guests may
   use it") and its "stock" is a **capacity byte A+0x80** 0..100 (0x800241BC get, 0x800241E8 subtract,
   0x80024210 refill — the same byte the handyman empties on bins). Consequently the guest stat
   **V+0x5D is the toilet need** (GUESS-high: fed by shop purchases, zeroed at a type-2 with capacity,
   −40 nausea). And for real rides slot 54 = 0x8009C2A0 = **returns 0** (READ), so the two
   "desire tables gated by slot 54" in the ride score (`behaviour.md` §2.2b, s6/s0) are **always zero
   for rides**; only Features get them.
2. **`economy.md` §4.6/§8 "the numbers are not in TPW.BIN — read FOLIO.GAZ"** — half right, and now
   done. The records are FOLIO entries, but the tables saying *which* entry is which type in which
   theme are static arrays in TPW.BIN (0x800F0CCC..0x800F1090, §1.4). I parsed all 197 records:
   every price, upgrade price, seat count, lifetime and intensity is in `records.json`.
3. **`behaviour.md` §3.5 guesses on the mechanic's ride calls** — READ now: `ride slot 57(6)` sets
   status 6 (under repair) and `slot 57(7)` sets 7, a transient "reopen" that for rides ends in status
   **10 (loading)** via 2 (§3.2). `0x8009C1E8` is not "riders still on it": it tests the **closing
   progress A+0xEC** against 10×(w+h); `0x8009EEE4` advances that progress (closing), `0x8009EFA8`
   retracts it (opening). `0x8005BE44` is not a "service-due list": it is the **player's upgrade queue**
   (array 0x801099EC, count gp 0x80102D48, pushed by the panel button 0x8003BE00 → 0x8005BE14),
   and `0x8005BE70` removes a ride from it. `slot 20` = status ∈ {4, 5} (READ 0x80063910).
4. **`behaviour.md` §2.4 queue cap "4 × ride[0xF6] + 7"** stands, and `economy.md` was right that
   `A+0xF6` is the upgrade level: READ again at 0x8008DA9C..0x8008DAAC → **7 / 11 / 15 guests at
   level 0 / 1 / 2**.
5. **`ai_out.txt`/`behaviour.md` "slot 86 = status ∈ {2,10,11} open"** stands, but note the Coaster
   overrides it (0x800AD78C, not read).
7. **§4.3's own account of the run timer is corrected here (2026-09-20), all re-read.** The mechanism
   is right in outline and wrong in three details that matter to a port.
   - **The phase records are 88 bytes (0x58), not 40.** The stride is computed twice in the image as
     `(((n*3) << 2) - n) << 3` = 88n — at 0x8003010C and again at 0x8002FE58. 40 is the size of a BONE
     record in the same file (folio.md §3.2), which is the easy conflation. Reading the table at 40
     lands in the middle of the wrong record and still produces a plausible u16.
   - **The length is read from the handle at A+0x18, EVERY TICK** — not cached, and not from A+0x20.
     `0x800658D8` → `0x80065DA8(A)` → `0x80030364(A+0x18)` for the current phase → `0x800300B8` →
     `lhu +0x38`. So a model that changes phase changes the ride's cycle length with it.
   - **The earlier “other handle / dead field” claim is withdrawn.** It mixed outer-relative and
     A-relative offsets and missed two direct callers. The tour cache is live; see item 9 below.

   The clock itself, exactly (0x800658D8):
   ```
   len    = status==1 ? BuildAnimLength(A)            # 0x80065994, keyed on A+0x6D
                      : PhaseLength(A) - 1            # the -1 is in a delay slot: RUNNING path only
   target = len << 12
   d = 0x800BC290(A+0x10)                             # wall-clock delta off root counter 2, << 7
   if [0x80103840] == 3: d >>= 1                      # half speed
   if d > 0x4000: d = 0x4000                          # SIGNED test: never catches a negative d
   A+0x60 += d
   if (unsigned) A+0x60 >= target: A+0x60 = 0; phase complete
   ```
   Two consequences worth keeping: the clamp is signed and the completion test is unsigned, so a
   negative delta is not caught and completes the phase if it drives the accumulator negative; and the same
   accumulator, against a different length, is what ends CONSTRUCTION — which is the code behind
   AttractionLifecycle's "the build ends with the animation, not on a timer".

   **Now established:** the per-ride lengths are decoded in [animation-phases.md](animation-phases.md)
   and [ride-phase-lengths.md](ride-phase-lengths.md). Item 9 corrects the remaining pointer and clock
   claims in this earlier audit.

8. **§6.1's heavy-load term, disambiguated (2026-09-20).** The report writes the full-load bonus as
   `((L − 0.8)×4096/64)²/4096`, which under integer arithmetic gives either zero or a number in the
   millions depending on where the fixed point is taken to be, and neither matches the report's own
   stated "+0.035 at full load". The reading that works is

   ```
   if L >= 0.8:  t = (L − 0.8) / 64      # raw 20.12 units, so (L − 3277) / 64
                 L += t * t              # NOT divided again
   ```

   At full load `t = (4096 − 3277)/64 = 12`, `t² = 144`, and 144/4096 = **0.035** — the report's own
   figure. It then reproduces all three of §6.1's worked rates exactly: **3.84** (speed 50, full),
   **1.25** (speed 50, empty) and **5.09** (speed 100, full), and the quoted loss of **0.120** per four
   ticks. Four independent checks on one reading.

   Marked **GUESS-high**, not READ: 0x800A0250 itself was not traced past its vtable calls
   (slots 0x320, 0x328, 0x330, 0x2C8 at 0x800A02AC..0x800A034C). It is the arithmetic that reproduces
   every number the report states, which is a strong constraint and is not the same as having read it.

   Two related facts worth keeping: the speed slider stops being linear at 100 (it is averaged with
   1.0, so 100 is not twice 50 — `s = (s + 4096)/2`), and **reliability zero is unreachable**, which
   §6.2 already says in passing. The breakdown test runs before the wear step and a ride that is not
   running does not wear, so the floor in the wear step is defensive code and nothing can drive a ride
   below about 9.8 points.

9. Nothing else I touched in the three reports turned out wrong. The queue/ride guest chain
   (41→18→21→22→23) and the mechanic states 56/16/14/17/58 and 57/52/54 all matched what the ride
   side does.

9. **Disc phase-table decode (2026-09-20), READ unless qualified.** This supersedes item 7 where
   noted; the complete trace and every descriptor field are in [animation-phases.md](animation-phases.md).
   - **The 88-byte descriptor is built in RAM.** `0x80030A70` calls folio.md's builder
     `0x8002C5CC`; `0x8002C5FC..604` copies **u16(mesh+0) → descriptor+0x38**. No need to find
     an 88-byte array on disc. All **59 flat rides** have one mesh and map `[-1,0,-1,-1]`.
   - **`0x8003084C` returns the container base B.** The computed
     `B+0x20+4*[B+0x1C]` goes into R+0x10 through a1; v0 remains B. Thus the modulo count
     read at `0x800300DC` is **B+4, the mesh count**, not a directory word. `0x80030364`
     maps the supplied logical phase (A+0x64), not an internally stored “current phase”.
   - **The tour cache is neither another handle nor dead.** `0x800A0DF4` passes
     **outer+0x20 = A+0x18** and sub-entry 1; `0x800A0E0C` stores its length byte at
     **outer+0x100 = A+0xF8**. Getter 0x800A2894 is called directly at **0x800A2EB4 and
     0x800A2F04**, to form twice the length as a vehicle-animation modulus.
   - **Phase-counted unloading applies to flat rides.** Status-2 slot 71 is 0x8009CA60
     for type 3; tour/track/coaster overrides are 0x800A1334/0x800A6960/0x800B0D68.
     Their mesh lengths alone do not determine their trip times.
   - **P is now known in clock units:** `32*(L-1)` software IRQ counts per phase, before
     sampling/capping. Crazy Ape **L=41**, The Dizzy Tree **239**, Zero G **801**. At the
     constant reference delta 9984 from arrivals.md §2.1, P is **17/98/329 calls** and
     their five-phase runs are **3.40/19.60/65.80 s** (**READ-derived, conditional**, not
     live measurements). Exact runs depend on raw deltas; the first call after loading can
     be capped because entering running clears the accumulator but not its timestamp.
   - The counter sampled by 0x800BC290 is the **software IRQ count**, not the hardware's
     wrapping 16-bit counter. Ordinary wrap does not itself establish a negative elapsed delta.
   - Space Balls has cycles max **10/10/10**, Slither **1/10/1**, and Bounce on Iggy
     defaults to **22/25/27 despite minima 30/30/30**. These are disc values and the literal
     `max(1,maxCycles>>1)` initializer (0x8009C524..534), not corrections to game behaviour.
   - “All 197 definitions” describes `recs.py`'s output, not all definitions on this disc.
     Its `recOff+0x40 > size` guard excludes **47 short Feature records**; direct headers
     give **244** type-1..8 definitions. All 83 ride definitions were already included.

10. **Other ride classes, breakdown disagreement (2026-09-20; READ, submitted for review).**
    §6.2/§7 say track/coaster reliability <10 goes directly to **5**. The image instead passes
    **4** at **0x800A6754** and **0x800B0B34**, through the unchanged shared SetStatus slot 57.
    Both pass 5 only at exactly zero reliability while already in 4 (0x800A6758..788,
    0x800B0B38..6C). Track's nonzero-low test has no status gate; coaster's admits **2 or 10**
    (0x800B0A88..9C). **The port keeps this report's direct-5/running-only rule** in
    `OtherRideWear`, explicitly labelled as retained, pending acceptance of the disagreement.
11. **Other ride classes, wear scheduling disagreement (READ).** §6's status-2-only account
    does not cover the actual callers. Tour Update calls wear whenever riders !=0
    (0x800A0EAC..ED4); track movement calls it while moving/unloading and in its controller
    path (0x800A8E30..EA8); coaster calls it from loading with riders and a connected track
    (0x800B0DF8..E48) and from status 4 (0x800B1054..109C). **The port retains the report's
    running-only gate.** Newly established: tour's two masks combine to every **32** ticks
    (0x800A1FFC then 0x8009DD74); track adds `(pieceCount<<12)/30` as a **third wear term**
    and averages three, while tour/coaster average speed and load (0x800A8994..9E0).
12. **Tour dispatch disagreement and seat discovery (READ).** §7's queue-empty departure
    omits **0x800A12D4**, which returns if the vehicle is occupied. The binary's partly
    filled last vehicle waits for more guests; an empty spare instead sets a retirement
    request (0x800A27C0, readers 0x800A2B14/0x800A2B54). **The port keeps the report's
    queue-only departure gate**, with the omitted binary condition documented beside it.
    Vehicle capacity is literal **3** at 0x800A335C, after reading and discarding record+0xD4;
    it is separate from the common record's eight-seat maximum. Actual trips count vehicle
    destination arrivals, not the cached model-animation length.
13. **Coaster slot 86 and object distinction (READ).** Slot 86 at 0x800AD78C calls the shared
    `{2,10,11}` predicate and vetoes zero **outer+0x104**, the track-connection flag set by
    0x800ADEA0..EF4. It adds no status. The “32 cars of 0x40” in §7 are route pieces; passenger
    trains are **eight 0x88-byte objects** constructed at outer+0x9A0 (0x800B1ACC →
    0x800B1AFC..BB8). The port leaves route geometry to the world rather than introducing a
    competing model of that disputed layout. Coaster duration=1 is a disc slider value,
    **not a fixed trip time**: trains independently finish at the return-segment predicate
    0x800B2220..2C8. Track has both `2*duration` running ticks (0x800A69B0..A38) and per-vehicle
    duration laps (0x800AA99C..AAA3C). Full traces: [ride-classes.md](ride-classes.md).
14. **Shared wear precision flag (READ).** §6.1's pseudocode clamps reliability before its
    new lifetime band is calculated. The image calculates the band from the **unclamped**
    value (0x8009DDB4..DF0), using signed division by **15*4096** on raw reliability,
    truncating toward zero; only afterward does it clamp reliability (0x8009DE60..70).
    The new class port does not replace the shared arithmetic. Exact speed/load term
    instructions, including `0xCCC` and the unshifted square, are recorded in ride-classes.md §5.

15. **Coaster maximum-capacity override (READ).** §1.3's shared slot-99 → per-level seat word
    mapping is not the coaster implementation. **0x800AD728** returns
    `min(u8(record+0xD2),8) * 0x800AD610()`; the latter is a station-model attachment count,
    falling back from zero to one. Coaster wear divides load by this virtual maximum at
    0x800B07A0 / 0x800B0804. The data's common seat words remain recorded as read, but they
    do not establish that denominator. This port consumes already-computed load terms and
    does not silently change the existing definition/slider maximum implementation.

## 1. The building-type table — settled

### 1.1 Type enum, pools, classes (READ)

Pool creation 0x80050070 allocates one pool per class (`0x800BAB94(size,1)` then a per-class pool
ctor that builds N objects of stride S and links them). Pool pointers live at gp:

| type | class (CONTEXT_ name) | pool name | pool ptr | N | stride | vtable (at outer+0x14 = A+0xC) | Update slot 3 |
|---|---|---|---|---|---|---|---|
| 3 | NonPathedRide | PoolOfRides | 0x80103844 | 15 | 0x104 | 0x800E5514 | 0x800A0084 |
| 7 | TourRide | PoolOfTourRides | 0x80103848 | 3 | 0x14C | 0x800E586C | 0x800A0E88 |
| 6 | PathedRide (track) | PoolOfTrackRides | 0x8010384C | 2 | 0x1D24 | 0x800E5FE8 | 0x800A67B4 |
| 1 | RollerCoaster | PoolOfCoasters | 0x80103850 | 2 | 0xE30 | 0x800E65A0 | 0x800B0C94 |
| 4 | Shop | PoolOfShops | 0x80103854 | 20 | 0x94 | 0x800E6A8C | 0x80065BE0 (base) |
| 2 | Feature | PoolOfFeatures | 0x80103858 | 45 | 0x84 | 0x800DCA5C | 0x80065BE0 (base) |
| 5 | SideShow | PoolOfSideshows | 0x8010385C | 10 | 0x98 | 0x800E6D64 | 0x800B76CC |

So a park holds at most 15 flat rides, 3 tour rides, 2 track rides, 2 coasters, 20 shops, 45
features, 10 sideshows (READ from the pool ctors 0x80060FA8/0DA0/0B98/09A0/0798/05A0/0398).

Class chain (READ from the ctors): base "Attraction" vtable 0x800E1068 (ctor 0x800662D8; sub-objects
at A+0x10 (matrix, 0x800BC288) and A+0x18 (FOLIO handle, 0x800663FC)); **queued-ride base** vtable
0x800E519C (ctor 0x8009F708: adds queue-path list A+0x70, queue-member list A+0xC4, riders list
A+0xD4); the four ride classes derive from that, Shop/Feature/Sideshow from the base. All seven share
slots 6–28, 30, 35–38, 40–41, 51, 57–61, 66–70, 81–85 (see `vt.py 800E5514 800E586C 800E5FE8
800E65A0 800E6A8C 800DCA5C 800E6D64`).

### 1.2 The theme tables (READ, 0x8002ED50)

`0x8006A3CC(mgr, type, variant)` → FOLIO index of the definition record; `0x8006A158(mgr, type)` →
how many variants. `mgr` is the level manager singleton `0x80069610()`; `mgr+8` = **theme 0..3**,
`mgr+4` = **set index 0/1** (GUESS-medium: which of two attraction sets a scenario uses; its writer
is not found — read `*(0x80069610()+4)` live). Table pointer = `0x800DDDC4[theme]` →
0x8010558C / 0x801054DC / 0x8010542C / 0x8010537C (0xAC bytes each, built at boot by 0x8002ED50 from
constants), and with `T' = table + 4×set`:

| T' offset | contents |
|---|---|
| +0x08 / +0x10 | type 3 rides: pointer to array of FOLIO ids / count (count is a gp word filled at runtime) |
| +0x18 / +0x20 | type 7 tour rides (runtime gp array) |
| +0x28 / +0x30 | type 6 track rides (runtime gp array) |
| +0x38 / +0x40 | unknown pair (static arrays [0x100,0] / [0xA5,0] / [0x4F] …) |
| +0x48 / +0x50 | type 1 coasters (runtime gp array) |
| +0x58 / +0x60 | type 2 features: static array / count (13, 14, 12, 13) |
| +0x68 / +0x70 | type 4 shops: static array / 6 |
| +0x78 / +0x80 | type 5 sideshows: static array / 3 |
| +0xA0..+0xA8 | three constants per theme (0x102,0xA9,0xAA / 0xA8,0x5B,0x5C / 0x52,0x11,0x12 / 0x190,0x14C,0x14D) — GUESS: FOLIO ids of theme assets |

The static arrays (all READ, contents in `rides_themes.json`):

| theme (name GUESSED from content) | set A rides (8) | set B rides (6) |
|---|---|---|
| 0 Lost Kingdom | Crazy Ape, Inca Pot, Sun God, Mayan Spinner, Eruption, Tom Tom Twister, Rocky Racers, Aztec Bounce | The Hot Pot, Belly Bounce, Mumbo, Inca Totem, Aztec Mayhem, Diplo-Dip |
| 1 Halloween | Crazy Clown, Thrill Grill, Jaw Dropper, Tentacle Terror, Devils Disc, Eye Slide, Jumping Skulls, Insecticide | Ghost Ship, Putrid Pumpkins, Hocus Pocus, Brain Buster, Rat Race, Phantom |
| 2 Wonderland (7) | Bumper Bugs, Spore Spinner, The Dizzy Tree, Jelly Bounce, Flamingo Fling, Flying Fishes, Flying Fountain | Caterpillar Capers, Escargot A-Go Go, Bugs TV, Woodland Racers, Dragon Fliers, Flower Power |
| 3 Space (7) | Rock 'n Roll, Crater Creature, The Orbiter, Zero G, The Gravatron, Romper Stomper, Cyclone Station | Bounce on Iggy, Hover Bot Havoc, Space Balls, The Areotron, Moon Buggies, Missile Madness |

Shops are the same six per theme: set A = Fries, Burger, Drinks, Ice Cream, Costume, Gift; set B =
Fries, Burger, Drinks, Ice Cream, **Balloon, Restaurant**. Sideshows: 3 per set (e.g. Lost Kingdom A:
Idol Smash, Arcade, Dino Racing; B: Giant Puzzle, Sun Shooter, Strength Bird). Coasters, track rides
and tour rides are **not** in the static tables — their arrays are gp words filled by the scenario
loader, so which of the 12 coasters / 8 track rides / 4 tour rides a park offers is scenario data
(not traced).

### 1.3 FOLIO entry and the definition record (READ)

`FOLIO.GAZ` is a pack: word 0 = 422 entries, word 1 = 0x17, then (offset, size) pairs at +8
(READ 0x800BBFA0); `ext/rip/NNNN.bin` is entry
N. An attraction entry starts `0x96, 1, 0, 0, 0, recOff, size, 4`; the **definition record is at
`recOff`** (0x800307DC: `data + data[0x14]`), and the same entry also carries the model. Guests reach
the record through the handle object at A+0x18: `A+0x18[0]` = handle id, slot 7 (0x80062A20 →
0x80031114) locks it and returns the record pointer, slot 6 (0x80062A40) releases. The record:

| offset | size | meaning | how known |
|---|---|---|---|
| +0x00 | u32 | type (1..8, matches the enum) | READ (equals A+0x6A for every placed object) |
| +0x04 | u32 | **name text id** in the language table | READ: 0x8006A76C → 0x8006F00C(id) → table `gp 0x801039A0`; English table = FOLIO 0x197 = `rip/0407.bin` (word0 = 1031 strings, then offsets) |
| +0x08 | u8 | footprint width (tiles) | READ 0x8006A798, used by slot 12/14 with rotation A+0x6C |
| +0x09 | u8 | 1..7, unknown (GUESS: model/anim variant) | — |
| +0x0A | u8 | footprint depth | READ 0x8006A7A4 |
| +0x0B | u8 | always 0xFF | — |
| +0x0C | s16×2 | **entrance tile offset** (x,y) inside the footprint, rotated by 0x800634B8 | READ 0x80066004 / 0x80062E48 |
| +0x10 | s16×2 | **exit tile offset**; (−1,−1) = none (all shops/features/sideshows) | READ 0x80065FD8 / 0x80062FF8 |
| +0x14..+0x17 | u8×4 | unknown; +0x15 = 2 for most rides, +0x14 read by 0x80065FF8, +0x15 by 0x80065FCC | — |
| +0x18 | u32 | **base intensity** (rides 40..95; coasters 90/95; shops 20; features 0) | READ 0x800A0B1C, §4 |
| +0x1C | u32 | record body length; record ends at +0x20+this (0xA0 rides, 0xB4 coasters, 0xE8 track, 0x18 shops, 0x10 features, 0x14 sideshows) | READ 0x8006A7C8 |
| +0x20 | u32 | 4 for all rides | unknown |
| +0x24 + 0x34×L | 13 words | **per-level block, L = 0,1,2** (below) | READ 0x8009F470..0x8009F5B4 |

Per-level block (rides, coasters, track and tour rides), word offsets from the block start:

| +0 | +4 | +8 **wear multiplier** | +0xC **max seats** | +0x10 **lifetime** | +0x14 speed min | +0x18 speed max | +0x1C cycles min | +0x20 cycles max | +0x24 ? | +0x28 ? | +0x2C **price £** |
|---|---|---|---|---|---|---|---|---|---|---|---|
| always 4 | always 4 | 5 / 3 / 2 (7 for tour rides & Rock 'n Roll) | e.g. 8/11/14 | 45..100 | 1 (tour: 80) | 100 | 1 or 10 or 30 | 1..60 | 0..3 | 0..900 | L0 = build price, L1/L2 = upgrade price |

Which slot reads which (READ): slot 95 → speed min, 96 → speed max, 97 → cycles min, 98 → cycles
max, 99 → max seats (+0xC), 100 → +0, 101 → +4, 102 → wear multiplier (+8); 0x8009F524 → lifetime
(+0x10) used once at placement; 0x8009F470 → price (+0x2C). Words +0x24/+0x28 (`f48`/`f4C` in
`records.json`) have no reader I found in the ride code. **The data holds three levels but the code
allows a fourth**: 0x8009C56C increments `A+0xF6` while it is < 3, so level 3 reads 0x34 bytes past
the record into the model chunk (for Crazy Ape that would be a £65,819 upgrade with 65818 seats).
Whether the panel ever offers a third upgrade is not established — 0x8003BE00 has no level test.
Live check: upgrade one ride three times and watch `A+0xF6` and the bank.

Shop record (type 4, body 0x18): +0x20 build price (e.g. £250), +0x2C u16 **default sale price**
(→ A+0x88, READ 0x800B7110/0x800B7140), +0x2E u16 unit cost (economy.md §4.2), +0x30/+0x32/+0x34/+0x36
u16s (7, 25, 5, 10 for Fries: product parameters read by 0x800B6E18/0x800B6D40/0x800B6E60, not
resolved). Sideshow record (type 5, body 0x14): +0x20 build price, +0x24 (500), +0x28 (1), +0x2C u16
**play price** → A+0x84, +0x2E u16 **win chance %** → A+0x86, +0x30 u8 **prize £** → A+0x78 (READ
0x800B79E0/0x800B79D4/0x800B79C8 → 0x800B773C/0x800B7750/0x800B7770; settles economy.md §6.2).
Feature record (type 2, body 0x10): +0x20 build price, +0x2E flag byte (bit 0 usable-by-guests,
bit 2 bin — GUESS from the handyman filter), +0x2C/+0x2F unknown.

Tying a live object to its record: `A+0x6A` = type, `A+0x6B` = variant index (READ slot 38 0x800625BC
stores them), and `0x8006A3CC(0x80069610(), A+0x6A, A+0x6B)` is the FOLIO id, i.e. the static array
entry above. Live check: place the first ride of a Lost Kingdom park → A+0x6A = 3, A+0x6B = 0,
`0x800F0CF0[0]` = 0xDC = Crazy Ape, and `A+0x68` (lifetime) reads 45 = Crazy Ape's record +0x34.

### 1.4 All 59 flat rides (from `records_named.txt`; price / max seats L0→L2 / wear mult / lifetime / base intensity)

| FOLIO | name | fp | £ build | £ up1 / up2 | seats L0/L1/L2 | life | int |
|---|---|---|---|---|---|---|---|
| 0x27 | The Dizzy Tree | 5×5 | 2500 | 250 / 250 | 4/8/? | 80 | 70 |
| 0x28 | Bumper Bugs | 5×5 | 2000 | 200 / 200 | 2/4/? | 80 | 70 |
| 0xDC | Crazy Ape | 4×4 | 2000 | 200 / 200 | 8/11/14 | 45 | 60 |
| 0xD9 | Inca Pot | 4×4 | 1750 | 200 / 200 | 4/5/? | 80 | 65 |
| 0xE0 | Tom Tom Twister | 5×5 | 4000 | 400 / ? | 20/30/? | 80 | 75 |
| 0x7C | Hocus Pocus | 4×4 | 1250 | 150 / ? | 20/26/? | 50 | 55 |
| 0x81 | Thrill Grill | 5×4 | 2500 | 250 / ? | 18/24/? | 55 | 70 |
| 0x7B | Pumpkin Castle | 5×5 | 1500 | 1500 / ? | 4/6/? | 100 | 70 |
| … | (full list, all fields, all three levels) | | `records_named.txt` / `records.json` | | | | |

Coasters (type 1, 12 records): £10,000–12,500 to build, upgrades £5,000–5,500 then £2,000–2,500,
seats 6..18 → +33% per level, intensity 90/95, lifetime 80 (Ghosta Coasta 65), cycles fixed at 1.
Track rides (type 6, 8 records): £4,000–6,500, 8 seats at every level, intensity 75/80. Tour rides
(type 7, 4 records): £3,000, 8 seats, wear multiplier 7 at every level, speed slider 80–100.

## 2. Attraction object layout (A = pool entry + 8) — READ unless marked

| offset | meaning | where read |
|---|---|---|
| A+0x0C | vptr | ctors |
| A+0x10 | matrix/anim-time object (0x800BC288); A+0x10[0] = last frame counter for the animation step | 0x800658D8 |
| A+0x14 | **guests served, all time** (slot 22 get, 0x80063164 inc from guest state 22) | |
| A+0x18 | FOLIO handle object (H+0 resource pointer; H+8 = resource+0x18; H+0x34 sub-entry index, H+0x38 frame) | 0x800311F4, 0x80066380/88; animation-phases.md §1 |
| A+0x54 | **mechanic claim** (pointer to the mechanic, 0 = free) | 0x800632A8/B8 |
| A+0x58/5A/5C | position x, y, z in tiles (slot 19 set; slot 9 returns x<<8,y,z<<8) | 0x80063334/58 |
| A+0x5E | u16, zeroed at placement, unknown | 0x80062678 |
| A+0x60 | **animation phase timer**, 20.12, += frame delta per tick, phase done at ≥ duration<<12 | 0x800658D8 |
| A+0x64 | u16 **animation phase index** (status hooks write 0 or 1) | 0x80065688.. |
| A+0x66 | s16 = −1 at placement, unknown | 0x80062644 |
| A+0x68 | s16 **remaining lifetime** (§6.3): record+0x34 at placement, −1 per 15 reliability points lost, 0 = condemned. Slot 83 set (posts message 0x99 when it drops to ≤0), slot 84 get | 0x80063184/FC |
| A+0x6A | **type** byte (slot 16) | 0x80063210 |
| A+0x6B | **variant** index into the theme array | 0x80063320/28 |
| A+0x6C | rotation 0..3 (slot 18) | 0x80063294/9C |
| A+0x6D | build-animation variant, taken from the rotating counter gp 0x80102DD8 mod 8 at placement | 0x8006268C |
| A+0x6E | **status** 0..11 (§3) | 0x8006321C / SetStatus 0x80065768 |
| A+0x6F | flags: bit0 set at placement (0x80066240 set / 0x80066260 clear = slots 36/35), bit1 (0x800632C4) | |
| A+0x70 | queue path tile list (ctor 0x8009FAF4; walked by 0x8009D57C) | |
| A+0xB4 | **reliability**, 20.12, 100.0 at placement/upgrade/repair, 0..100 | 0x8009C410, 0x8009C828, 0x8009DD5C |
| A+0xB8 | **speed slider** value (slot 89 get / 92 set), record range [speed min, speed max] = 1..100, default midpoint = 50 | 0x8009ED04/ECFC, 0x8009C410 |
| A+0xBC | **capacity slider** (slot 90 / 93): riders loaded per cycle, 1..max seats, default max(1, max seats / 2) | 0x8009ED18/ED10 |
| A+0xC0 | **duration slider** (slot 91 / 94): animation cycles per run, [cycles min, cycles max], default max(1, cycles max / 2) | 0x8009ED2C/ED24 |
| A+0xC4 | queue member list (0x8009F21C head, 0x8009F23C next, 0x8009F1B8 remove, 0x8009F04C append, 0x8009EE98 count) | |
| A+0xD4 | riders-on-board list (0x8009F110 head, 0x8009F108 end, 0x8009F130 append, 0x8009F0E8 remove) | |
| A+0xE4 / A+0xE8 | effect handles: breakdown smoke (pool desc 0x800F7898) / upgrade sparkle (0x800F78D8) | 0x8009C2A8, 0x8009C56C |
| A+0xEC | **closing progress**, 20.12, 0 = open, 10×(w+h) = fully closed; rises while a mechanic closes the ride (status 16), falls while it reopens (17) | 0x8009C1E8/EEE4/EFA8 |
| A+0xF0 | u16 **riders on board** | 0x8009C884/CB5C |
| A+0xF2 | u16 **cycles completed this run** | 0x8009CA60 |
| A+0xF4 | u16 day built (McAi total days) | 0x8009C3FC |
| A+0xF6 | u8 **upgrade level** 0..3 (0x8009F5E0 get / 0x8009F5EC set) | |
| A+0xF8 | word: total-days stamp of the last tick on which the queue was non-empty (§4.3) | 0x800A01B4 |

Shop/Feature/Sideshow extras: shop A+0x78 day built, A+0x7C / A+0x8A panel modifiers, A+0x88 sale
price, A+0x8C/0x90 takings/profit (economy.md §4.2); sideshow A+0x78 prize, A+0x84 price, A+0x86
chance, A+0x88 wins, A+0x8C prizes paid, A+0x90 takings; feature A+0x80 capacity 0..100.

## 3. Ride lifecycle — the status machine (all READ)

`SetStatus(A, n)` = vtable slot 57 = 0x80065768: writes A+0x6E and calls the **enter hook, slot
58+n** (jump table 0x800E1340). The base `Update` slot 3 = 0x80065BE0 dispatches on status to the
**tick hook, slot 69+n** (table 0x800E1370; status 0 has none) and then advances the animation
(0x80065D6C: `phase → mesh` via 0x80030364, frame via 0x80066388). The queued-ride Update
0x8009CCD4 wraps that with the breakdown logic (§6), and the Ride class's own Update 0x800A0084
prepends the reliability check 0x8009D0FC.

| status | meaning | enter hook (58+n) | tick hook (69+n) | leaves to |
|---|---|---|---|---|
| 0 | just placed | 0x80065688: phase 1, timer 0 | — (animate only) | slot 39 → 1 |
| 1 | **under construction** | 0x80065698: phase 0, frame 0 | 0x800659C4: build matrix, run the build animation; **when the phase completes → SetStatus(10)** | 10 |
| 2 | **running** (rides) / **open** (others) | rides 0x8009C708: phase 1, cycles A+0xF2 := 0 | rides 0x8009CA60: wear (slot 103) every tick; every 16 ticks a positional sound; each animation phase completed → A+0xF2 += 1; **A+0xF2 ≥ A+0xC0 → SetStatus(11)**. Shops/features: nothing | 11 |
| 3 | **closed by the player** (panel button slot 28 = 0x8006382C, caller 0x8007841C) | phase 1 | nothing | 2 via the open button (slot 27 = 0x800637F8, callers 0x80078798, 0x8003BC18, 0x80017304) |
| 4 | **"about to break down"** — reliability below 10 while running, or lifetime exhausted | rides 0x8009C730: post message 0x3E "Your ride is about to break down!", sound (8,0); Ride adds: if lifetime ≤ 0, eject everyone (0x8009D8C4) | nothing | 5 (only if A+0xB4 == 0), or a mechanic: 6 |
| 5 | **broken down** (track rides/coasters jump straight here; rides only from 4 with reliability exactly 0) | 0x8009C7A8: post message 0x3F / 0x40 / 0x41 ("A ride has broken down, and you don't have any mechanics / all your mechanics are busy / a mechanic is on his way"), sound (8,1); Ride ejects everyone | nothing | 6 |
| 6 | **under repair** (mechanic state 16→14 sets it, `slot 57(6)` at 0x80096650) | phase 1 | nothing | 7 |
| 7 | **reopen** command (mechanic state 17, or the upgrade path 0x8005C2F4) | base 0x80065704: **SetStatus(2)**; rides 0x8009C828 additionally: clear smoke, **A+0xB4 := 100.0**, then **SetStatus(10)** | — | 2 (shops) / 10 (rides) |
| 8 | (tick 0x8009EBCC: A+0xEC −= timescale>>12) — no writer of 8 found | | | |
| 9 | (tick 0x8009EC08: A+0xEC += timescale>>12 up to 10×(w+h)) — no writer of 9 found | | | |
| 10 | **loading** (rides) / build-complete (others) | 0x80065750: phase 1, timer 0 | rides: LoadGuests (§4.2); Shop/Feature/Sideshow 0x800B6EA8/0x80024258/0x800B7674: **SetStatus(2)** at once | 2 |
| 11 | **unloading** | nop | rides 0x8009CB5C (§4.2) | 10 when empty |

Guests may join the queue only in **2, 10 or 11** (slot 86 = 0x800632D8). Statuses 8 and 9 have
tick code but no SetStatus caller anywhere in TPW.BIN (census of all 33 `SetStatus` sites: constants
0,1,2,3,4,5,7,10,11,14,54 plus five computed) — dead in this build.

**Placement → running (READ).** The build tool calls slot 37 (Ride: 0x8009FB50): resolves the record
(0x8006A59C(mgr, variant)), binds the handle (0x800A0C98), `InitFromRecord` 0x8009C344 (lists reset,
A+0xB4 := 100, A+0xF2 := 0, A+0xEC := 0, sliders to defaults from level 0 via 0x8009C410, riders 0,
**A+0x68 := record lifetime**, A+0xF4 := today) and slot 38 (type/variant, SetStatus(0), guests 0).
Then slot 39 (0x80062894) = SetStatus(**1**) plus the build animation variant; the construction
animation runs (one phase of the build model; its length is in the model's animation chunk, §4.4)
and the base tick-1 hook sets **10**. Rides then sit in 10 loading; everything else flips to 2.
Money: charged by the tool (economy.md §4.6), not by the object.

**Demolish (READ).** Ride slot 29 = 0x8009FC1C → 0x8009C690: free the queue tiles (slot 32 →
0x8001A170), drop effects, **eject queue and riders** (0x8009D8C4(A,1): message 10 to each guest,
riders placed at the exit, A+0xF0 := 0), then base 0x8006292C: remove from the map/lists, release
the handle, **refund half the level-0 price** (0x8006AD58 → `Income(price/2 ×10)`).

**Upgrade (READ, 0x8009C56C).** Player presses the panel button (0x8003BE00 → 0x8005BE14 pushes the
ride on the upgrade queue 0x801099EC). A mechanic in Idle picks it (0x8005BE44 → state 57 → 52 → 54;
closing/opening as for a repair, duration table 0x800E4574 = 240/180/120/60/60 ticks by skill) and
calls 0x8009C56C(ride, 0): `if A+0xF6 < 3: A+0xF6++`, **0x8009C410 re-derives the three sliders from
the new level's block and resets reliability to 100**, charges `record[level].price`, sound and
sparkle effect. Effects of a level: +seats (max seats 8 → 11 → 14 for Crazy Ape), wear multiplier
5 → 3 → 2 (§6.1), queue cap 7 → 11 → 15. Lifetime A+0x68 is **not** reset by an upgrade.

## 4. Queues and throughput (READ)

### 4.1 Joining
`behaviour.md` §2.4 stands. Re-read of 0x8008DA14: a guest may join when the ride is open (status
2/10/11) and `queue members < 4×level + 7` (**7 / 11 / 15**), or when it is already in the queue.
Guests stand ¼ tile apart along the queue path (0x8009D57C). The queue-boredom exit is at
`V+0x5C > 80` (behaviour.md §2.4).

### 4.2 Loading and unloading — 0x8009C884 (tick-10) and 0x8009CB5C (tick-11)
```
LoadGuests(A):                       # called every tick in status 10 (Ride: via 0x800A0178)
  if clock % 20 != 0: return         # one guest per 20 ticks = 0.8 s
  if A+0xF0 (on board) < A+0xBC (capacity):
      g = head of queue list; return if none or g.state != 18 (Waiting in queue)
      remove g from the queue; SetState(g, 21) Loading; g.slot3/slot4 (hide, attach)
      append g to riders; A+0xF0 += 1
      for every guest still queued: send message 6 with stagger += rand(3), param 19
          → each shuffles forward after 3×stagger ticks
  else:                              # full
      A+0xF2 := 0; SetStatus(2)      # start running
```
Ride tick-10 (0x800A0178) adds the **partial-load timeout**: `days = McAi total days`; while the
queue is non-empty `A+0xF8 := days`; after `LoadGuests`, if `days − A+0xF8 ≥ 5` **and** at least one
rider is on board → `SetStatus(2)`. So a ride waits for a full load as long as guests keep arriving,
and starts a partial run once the queue has been empty for **5 game days = 495 ticks = 19.8 s**.

```
Unload(A):                           # every tick in status 11
  if clock % 10 != 0: return         # one guest per 10 ticks = 0.4 s
  if riders empty: A+0xF0 := 0; SetStatus(10)
  else: g = first rider; remove; SetState(g, 22) Unloading; place g at the exit tile
        (record exit offset rotated, 0x80062FF8 → 0x8009F380); re-register g (0x80053C04); A+0xF0 −= 1
```
Tour rides use the same 20-tick load cadence (0x800A11E8) but load into a vehicle
(0x800A2318) and unload with 0x800A2464; track rides 0x800A80F0 / coasters 0x800B025C load per car
(not traced beyond the SetState(21) sites).

### 4.3 Run time
**READ:** flat-ride status 2 counts completions of logical phase 1's selected mesh, repeatedly;
it does not walk to the next mesh. A+0xF2 increments on completion and unloads at ≥ A+0xC0.
Each target is `(L-1)<<12`, with **L = u16(mesh+0)** copied into an 88-byte runtime descriptor.
The [full table](ride-phase-lengths.md) gives L for every ride and the flat-ride run calculation.

**READ-derived:** with constant positive delta δ, one phase is
`Pδ = max(1,ceil(((L-1)<<12)/min(δ,0x4000)))` calls and a run is `C*Pδ` calls.
The table declares δ=9984, the common measured value from arrivals.md §2.1. With real variable
deltas, half speed, stalls or a first call capped after loading, sum the actual phase intervals
instead. [animation-phases.md §3](animation-phases.md#3-what-p-actually-means) gives the exact
recurrence, nominal hardware conversion and remaining live-measurement requirements.
The tour, track and coaster overrides do not use this flat-ride run formula.

### 4.4 Throughput
Per cycle: `load = 20×capacity` ticks (plus waiting for guests), `run = A+0xC0 × P`, `unload =
10×capacity`, so the cadence-only estimate is
**guests per second = capacity / ((30×capacity + runTicks) / 25)**. Crazy Ape at
defaults (capacity 4, duration 5): **80** ticks load + 40 unload, not the earlier 120+40 arithmetic.
Using the declared δ=9984 reference, its run is 85 ticks (84 with a capped first call).
These are **READ-derived estimates**, excluding cadence alignment and extra transition/wait calls;
the former guessed P≈60 example is withdrawn. Raising the capacity slider
to 8 (max at level 0) almost doubles it because the run time is per cycle, not per rider; raising
duration lowers it linearly and raises intensity (§5).

What stops a queue draining: status 3 (player closed), 4/5/6 (broken, until the mechanic finishes
17 → 7 → 10), the 5-day wait with a queue that has thinned to below capacity, and — because loading
only takes the head if its state is **18** — a head guest still in 19 (shuffling) stalls the load
until it arrives. A guest at the head in any other state blocks loading for the whole ride until
message 6/7/10 moves it.

## 5. Ratings — what per-ride numbers exist (READ)

There is **no excitement, appeal, popularity or queue rating stored per ride.** The values the game
computes or keeps per attraction are exactly:

1. **Intensity** — vtable slot 53, recomputed on every call (not stored). Ride (0x800A0594):
   `base = record+0x18; f1 = clamp(A+0xB8 / 100, 0.75, 1.25); f2 = clamp(A+0xC0 / 5, 0.75, 1.25);
   intensity = min(100, base × f1 × f2)` (20.12 arithmetic; 0x51EB851F = 1/100, 0x66666667 = 1/5).
   With the speed slider capped at 100 by the record, f1 ∈ [0.75, 1.0]; f2 reaches 1.25 at a duration
   of 7+ cycles. Defaults (50, 5) give **0.75 × base** → Crazy Ape 45, Tom Tom Twister 56. Tour
   0x800A202C, track 0x800A8A0C, coaster 0x800B08C8 are the same shape (both sliders read; not
   line-by-line read). Sideshow 0x800B7788: `record+0x18/3 + 50 + max(0, price − prize)²/16` clamped
   to 100 (GUESS-medium on the field names; READ on the shape). Shop/Feature: 0x80066110 = 0.
   Intensity is what the guest score (behaviour.md §2.2b, "ride.slot53") and the ride reward
   (+15/+10/+5 happiness, nausea) use, and the entry-fee verdict sums it over the park.
2. **Reliability** A+0xB4 (§6) — the panel's "State of Repair" (text 0x7A) reads it via 0x8009ECE4
   (`>> 12`).
3. **Lifetime** A+0x68 (§6.3).
4. **Guests served** A+0x14 (all time) and, for shops/sideshows, takings/profit words.
5. Upgrade level A+0xF6 and the three sliders.

The guest-side choice therefore depends only on distance, the guest's intensity preference vs slot 53,
the two need×attribute tables (slots 55/56, which for rides are 0x8009F5FC/0x8009F5F4 = **0**, so those
terms are constant M[need][0]), and the repeat penalty — rides differ to guests only by intensity and
position.

## 6. Breakdowns (READ)

### 6.1 Wear — slot 103 = 0x8009DD5C, called every tick of status 2, acts every 4th tick
```
k  = slot 104 (wear rate, Ride 0x800A0250)            # 20.12
R  = A+0xB4;  R' = R − (k >> 5);  A+0xB4 := max(0, R')
A+0x68 −= floor(R/15) − floor(R'/15)                   # lifetime loses 1 per 15-point band crossed
```
Wear rate (0x800A0250, a1 = 0 from the wear call):
```
if 0x80059A9C() != 0: return 0                     # the mode flag from behaviour.md: no wear
s = A+0xB8/100 (20.12);  if A+0xB8 ≥ 100: s = (s + 1)/2       # speed term, ≈ 0.01..1.0
L = A+0xF0 / maxSeats(level)                        # load fraction, riders on board
if L ≥ 0.8: L += ((L − 0.8)×4096/64)²/4096          # +0.035 at full load
k = ((s + L) / 2) × wearMult(level)                 # wearMult = record +8 of the level block: 5 / 3 / 2
```
(The record words `f24`/`f28` = 4 enter as `1 − 4/4096` and `+4/4096`, i.e. noise.) Per 4 ticks the
reliability loses `k/32` points. Worked numbers, level 0 (wearMult 5):

| speed | load | k | loss / 4 ticks | 100 → 10 takes | in seconds / game days (running time only) |
|---|---|---|---|---|---|
| 50 (default) | full | 3.84 | 0.120 | 3000 ticks | 120 s / 30 days |
| 50 | empty | 1.25 | 0.039 | 9200 ticks | 368 s / 93 days |
| 100 | full | 5.09 | 0.159 | 2260 ticks | 90 s / 23 days |
| 50, **level 2** (mult 2) | full | 1.54 | 0.048 | 7500 ticks | 300 s / 76 days |

Wear accrues **only while the status is 2**, so calendar time is longer by the loading/unloading
duty cycle. Nothing random enters: breakdowns are deterministic in running time, speed and load.
Live check: read A+0xB4 on a full Crazy Ape at default speed every 100 ticks; expect −3.0 per 100
ticks of status 2 (0x0C00 units per 100 ticks), none in 10/11.

### 6.2 Breaking and repairing
- Ride Update 0x8009D0FC (before anything else each tick): `status == 2 and A+0xB4 ≤ 0x9FFF (< 10.0)`
  → smoke effect at the ride centre → **SetStatus(4)** (message 0x3E "about to break down"). Track
  rides (0x800A66EC) and coasters (0x800B0A68) do the same test but go to **5** directly. A ride in
  4 with A+0xB4 exactly 0 goes to 5 (unreachable in practice: the wear step is < 0.2 and stops in 4).
- Mechanic Idle (behaviour.md §3.5) claims a ride with slot 20 true (status 4 or 5), unclaimed and
  **A+0x68 ≠ 0** (0x80096C58 at 0x80096CD4..0x80096CE8, READ) → states 56 → 16 (closing: A+0xEC rises
  by the timescale per tick to 10×(w+h)<<12: **≈ 33 ticks for a 4×4**) → 14 repairing
  (**240/180/120/60/60 ticks by skill 0..4 = 9.6/7.2/4.8/2.4/2.4 s**) → 17 opening (A+0xEC back to 0,
  ≈ 33 ticks) → `SetStatus(7)` → reliability **:= 100**, smoke cleared, status 10. Mechanic morale
  +10. Repair costs nothing (economy.md §4.7 stands). Total ≈ 66 ticks + repair time + walking.
- Player levers: the speed slider (linear in k), capacity vs actual load, the duration slider (longer
  runs = more status-2 time per guest), upgrades (wear multiplier 5 → 3 → 2 and a reliability reset),
  mechanic count and skill (repair duration only — **skill does not change wear or the breakdown
  threshold**), and closing the ride (status 3 stops wear).

### 6.3 Lifetime — the "condemned" mechanic
`A+0x68` starts at the record's lifetime (45 Crazy Ape, 50 Hocus Pocus, 55 Thrill Grill, 60 The
Orbiter, 65 Phantom/Escargot/Ghosta Coasta, 75 Areotron, most rides 80, 100 for Pumpkin Castle /
Spooky Spider / Uforia / Aztec Mayhem / Slither / Tower). Each 15-point band the reliability crosses
costs 1, so a full 100 → 10 cycle costs 6: **Crazy Ape survives 7 breakdown-repair cycles, an
80-lifetime ride 13, a 100-lifetime ride 16.** When it reaches 0: slot 83 posts message **0x99 "One of
your rides has become too old and has been condemned. You should delete it and build a new ride"**,
the queued-ride Update (0x8009CCD4) closes the ride (A+0xEC rises, 0x8005BE70 pulls it off the
upgrade queue) and sets **4**, and no mechanic will ever claim it (the A+0x68 ≠ 0 test) — repair
would in any case re-trip on the next tick because nothing resets A+0x68 except placement
(0x8009C344) and the save-loader (0x8009CEFC). Demolish (half refund) and rebuild is the only exit.
Live check: poke A+0x68 to 1 and A+0xB4 to 0x10000 (16.0) on a running ride; within 4 ticks the
lifetime hits 0, message 0x99 appears and the status goes 2 → 4.

## 7. The other ride classes — original inventory and completed control trace

The following is the **original partly-read inventory**, preserved so the disagreements in
§0 items 10–15 remain reviewable. Its status-5 and “32 cars” claims are disputed by the new trace.
- **TourRide** (0x800E586C): loads one guest per 20 ticks into a transport object (`outer+0x108`,
  0x800A3298 "vehicle available"), dispatches when the queue empties (0x800A27C0/0x800A3368),
  unloads one per 20 ticks (0x800A2464). Own wear slot 103 (0x800A1FE8) and rate (0x800A1CA4).
- **PathedRide/track** (0x800E5FE8): 34 track-piece sub-objects of 0xC0 at outer+0x198; loads at
  0x800A80F0; unloads 0x800A8BD4; own tick-2/10/11 (0x800A6960/0x800A67F4/0x800A6A58); reliability
  < 10 → status **5** with smoke (0x800A66EC); its own SetStatus(1)/(3) sites (0x800A462C/0x800A6F60).
- **RollerCoaster** (0x800E65A0): 32 car sub-objects of 0x40 at outer+0x18C; overrides slot 86
  (0x800AD78C), tick-2/10/11 (0x800B0D68/0x800B0DC8/0x800B0F90), loads at 0x800B025C, wear rate
  0x800B05D0 (reads speed and capacity), breakdown → **5** (0x800B0A68).
The original investigation did not trace these beyond the addresses above. The new
[ride-class report](ride-classes.md) traces their loading, departure, trip completion, unloading,
wear and coaster slot 86, and records the exact boundaries of the C# control port.
Its key result: **track has a short tick gate followed by per-vehicle lap completion; coaster
trains finish independently by route progress; tour vehicles count destination arrivals.**
None uses the flat ride's phase-counted run formula. The common sliders do not imply common clocks.

## 7b. Building one: which tool hands over to which (READ)
The tool objects are constructed in one place, 0x80019E90, each into its own static: the table at
0x800EFD5C then indexes them, so a constructor's argument names its tool.

| tool | object | constructor | prompts (△ ○ ✕ □) |
|---|---|---|---|
| 2 path | 0x80104A08 | 0x8001E4C8 | Cancel · — · Place · Delete |
| 3 queue | 0x801049C8 | 0x8001E490 | Cancel · Undo · Place · — |
| 5 flat ride | 0x80104968 | 0x8001D178 | Cancel · Rotate · Place · — |
| 6 tour ride | 0x80104948 | 0x8001D144 | Cancel · Rotate · Place · — |
| 7 track ride | 0x80104408 | 0x80022F58 | Cancel · Rotate · Place · — |
| **8 track builder** | 0x801043C8 | 0x80022F24 | Cancel · Undo · Place · **Undo All** |
| 11 coaster | 0x801048C8 | 0x80021D28 | Cancel · Rotate · Place · — |
| **12 track builder** | 0x80104678 | 0x80021CF0 | Cancel · Undo · Place · — |
| 14 shop / 15 feature / 16 sideshow | 0x80104928 / 0x801048E8 / 0x80104908 | 0x8001D110 / 0x8001D0A4 / 0x8001D0DC | Cancel · Rotate · Place · — |

⭐ **A COASTER IS PLACED LIKE ANY OTHER RIDE, THEN ITS TRACK IS LAID BY A SECOND TOOL.** The prompts say
it before any disassembly does: 6, 7 and 11 carry the same Cancel/Rotate/Place a flat ride does, and it
is 8 and 12 that carry Undo. The hand-over is in the place handlers, through the tool's slot 0x48:
**tool 7 (track ride) → tool 8** (0x80021EDC), **tool 11 (coaster) → tool 12** (0x8001F0D0), each right
after the placed sound (group 8, sound 3) and before the charge (0x8001C2E0). Tools 9, 10 and 13 are
further builders of the same two shapes.

So the port's flow is the one it already has for a ride and its queue: drop the station as a blueprint,
then open the builder.

### What a press in the builder does (READ)
⭐ **YOU PLACE PYLONS AND THE TRACK ASSEMBLES BETWEEN THEM** (master; the code agrees). A press stores a
POINT, not a run of tiles: 0x800A7CC8 takes the builder's candidate, reads two s16s out of it and writes
them as a pair of u16s into the ride's own array at **+0x106** (4 bytes an entry, the count in the byte
at **+0x104**), refusing the **thirty-third**. Nothing in a press touches the map.

⭐ **THE CIRCUIT CLOSES BY LANDING BACK ON THE START.** The same routine compares the new point against
the ride's start and sets **+0x18A** when they match — the flag the coaster's "may guests queue"
override tests, so an unfinished track is unqueueable rather than merely unattractive.

⭐ **WHERE THE RAILS LEAVE THE STATION** (0x800A6550, off the ride's own tile at +0x60/+0x64, by its turn
at +0x74): rot 0 → (x−2, z+1), rot 1 → (x+1, z+4), rot 2 → (x+4, z+1), rot 3 → (x+1, z−2).

### Where the next pylon actually lands (READ, 0x800221B0)
⭐ **THE BUILDER DOES NOT JOIN TWO POINTS WITH A CORNER — IT RUNS ALONG ONE AXIS IN TWO-TILE STEPS.** Its
per-frame update takes the last point (tool +0x28 / +0x2C) and the cursor (tool +0x00 / +0x04), refuses
outright if any of the four is negative, and then:

- `|dz| < |dx|` → the run goes along **X**, step `±2`; otherwise along **Z**, step `±2` (a tie goes to Z).
- pieces = `abs(delta) >> 1`, so an odd distance is **rounded down** and the cursor's off-axis half is
  simply thrown away.
- the walk then calls 0x80022140 per step with the "last piece" flag `s4 < 1`, and a step that fails
  clears the tool's valid flag at +0x18 — which is the flag the place handler (0x800225C0) tests before it
  will add anything.

So a press lands the pylon on a two-tile lattice along ONE axis from the last one, never on the raw
cursor tile, and the track it assembles is a straight run. The two-tile step is also why the kinds the
generator emits for a run are the 4x3x4 family.

### The pieces the track is made of (READ)
⭐ **THERE IS A PIECE TABLE, AND IT IS PLAIN DATA IN THE EXE: 80 descriptors of 8 bytes at 0x800F8A30.**
(⚠ my earlier note said 0x80108A30 and "RAM-resident, loader not found" — that was an address typo: the
routine builds it from `lui 0x8010` + `addiu -0x75D0`, which is 0x800F8A30, and the table is in the image.
Nothing loads it at runtime because nothing has to.)

| byte | meaning |
| --- | --- |
| +0 | 0 for the small pieces, 1 for the big ones |
| +1 | class: 0, 1, 2, 3, 6, 8, 9, 0x0B, 0x0C, and 0x63 (99) |
| +2 +3 +4 | size — `2,2,2` for the small pieces and `4,3,4` for the big ones (half-tiles: 1×1×1 and 2×1.5×2 tiles) |
| +5 | direction, 0..3 |
| +6 | model, 0..3 |
| +7 | 0x80 on all eighty |

They run in groups of four that share everything but +5 and +6, so the table is **20 piece types × 4
directions**, and a direction picks which of four models is drawn.

**How a kind reaches its descriptor** (0x800A4DE0, called through 0x800A4F10 with the kind the piece
stores at +0xBC): `descriptor = base + kind × 8`, and the base is 0x800F8A30 for **kind < 40**. For
kind ≥ 40 the base is chosen by two gp globals — `gp+0x124C` (0x801038A0, four branches, 0..3) and
`gp+0x1250` (0x801038A4, zero / non-zero):

| gp+0x124C | base when gp+0x1250 == 0 | base when != 0 |
| --- | --- | --- |
| 0 | 0x800F8A30 (entry 0) | 0x800F8A70 (entry 8) |
| 1 | 0x800F8AD0 (entry 20) | 0x800F8A90 (entry 12) |
| 2 | 0x800F8B30 (entry 40) | 0x800F8AF0 (entry 24) |
| 3 | 0x800F8B50 (entry 36) | 0x800F8B50 (entry 36) |

so the first global slides a window over the same eighty descriptors.

⭐ **ONLY KINDS 40..51 TAKE THAT PATH.** The generator (0x800A796C) opens with `sltiu (kind − 40), 12`:
kinds **40 to 51** are the big family, and for those it nudges the point by the direction in the kind's
low two bits — dir 0 → x−1, dir 1 → x−1 z−2, dir 2 → z−1, dir 3 → x−2 z−1 — which is a 2-tile piece
being placed by its corner. Everything else goes in unmoved.

### What the descriptor's two bytes actually do (READ)
⚠ **+6 IS NOT A MODEL INDEX — IT IS THE PIECE'S TURN.** 0x800A4F34 hands the byte to slot 18 of the piece's
class record (0x800E5C90, a proper 8-byte-per-slot vtable whose slot 1 is the constructor that writes it),
and slot 18 is `0x80063294` — three instructions: `sb a1, 108(a0)`. It lands at **piece +0x6C**, and its
consumer (0x80062BB8) reads it and branches: **0 or 2 → the record's WIDTH (0x8006A798), 1 or 3 → its
DEPTH**. That is a rotation, which is also why every group of four carries a permutation of 0..3 rather
than four different numbers.

⭐ **+1, THE CLASS, IS WHAT PICKS THE MESH.** Further down the same routine: for `class == 11` or
`class == 99` the piece gets **nothing** (those two draw no model), and otherwise it calls
`0x800A6088(owner, class)`, which is four instructions — `visual = owner[0xDC + class × 4]` — and the
result is stowed at **piece +0x4C** by 0x800A61C0. The owner comes from 0x80031114 (a dereference of the
piece's +0x18 and 0x800307DC), so the mesh a piece draws is **the owner's class-indexed model slot**.

**Where that owner comes from**: 0x80031114 reads the piece's +0x18 as an ID, hands it to 0x800C0DB8,
which checks the entry is loaded and returns `table[id]` out of the 24-byte-per-entry loaded-archive table
at **0x8010B0D4** [RAM], and 0x800307DC then adds that base's own `+0x14`. So the owner is a LOADED
ARCHIVE ENTRY, resolved the same way everything else on this disc is.

⚠ **IT IS NOT THE RIDE'S OWN RECORD, and which entry it is stays open.** I checked: an attraction record's
tail is not at a fixed offset — the body ends at `+0x1C`'s length and the ground pad after it is
`width x depth x 2` bytes, so Chac Atak's (2x3) extra data starts at +0xC0 while Gorilla Thrilla's (4x4)
starts at +0xD4. A table read at a FIXED `+0xDC` cannot be in a structure that moves with the footprint.
Reading Chac Atak's record there anyway gives `(219, 0) (219, 8) (205, 0) (206, 2) (202, 3) (202, 2)` —
Temple of Gloom, the jungle scenery pack, and the Small Toilet — which is the sort of answer that tells you
the offset is being read against the wrong object.

The likely owner is the WORLD's own shared pack rather than the ride: the descriptor lookup already
branches per world (the table above), a coaster's track is themed per world, and the ride's own entry
carries only its station, four cars and a handful of one-tile parts. NOT ESTABLISHED — what to read next
is what writes the piece's +0x18.

⚠ Two things that follow from the table and are NOT settled. With the window at entry 36 or 40, kinds
40..51 index entries 76..91, and the table stops at 79 — so either those two branches never see a big
kind, or the first global is not what it looks like. And the mesh comes from the CLASS, not
from +6 — see the section above, which followed that call.

⚠ Also not traced: whether the bill is the confirm's `unit × (pieces − 4)` or the per-press charge.
What is known: the piece price is `defPrice(8, kind)` and the charge is `unit × (pieces − 4)`, or − 5
in one branch (economy.md §4.6); the type-8 records that price it are the named specials (Water Jump,
Mammoth Tunnel, Piranha, Firepit); and a ride's own archive entry carries 6 to 13 sub-models, which is
where the track pieces themselves live.

## 8. Live checks (in the order I would run them)
1. `A+0x6E` of a freshly placed ride goes 0 → 1 → 10 and then loops 10 → 2 → 11 → 10; a shop goes
   0 → 1 → 10 → 2 within one tick of 10.
2. `A+0xF0` rises by 1 every 20 ticks in 10 while `A+0xC4[0] ≠ 0`; `A+0xF2` counts phases in 2; the
   run ends when it equals `A+0xC0`.
3. Default sliders on a Lost Kingdom Crazy Ape: `A+0xB8` = 50, `A+0xBC` = 4, `A+0xC0` = 5, `A+0x68` =
   45, `A+0xF6` = 0. After an upgrade: `A+0xBC` = 5, `A+0xC0` = 5, `A+0xB4` = 0x64000, `A+0xF6` = 1,
   bank −£2,000 (level-1 price for Crazy Ape is 200 → −2000 units).
4. Wear: `A+0xB4` falls ≈ 0x0C00 per 100 ticks of status 2 at (50, full load).
5. Queue cap: the 8th guest to arrive at a level-0 ride never enters the queue list (`0x8009EE98`
   stays at 7).
6. The theme/set: `*(0x80069610()+8)` and `+4`; `0x800F0CF0[A+0x6B]` must equal the FOLIO id whose
   record's text id names the ride you placed.

## 9. Not established (honest list)
- Exact live wall times under variable IRQ delivery, first-call timestamps, pauses and stalls;
  complete tour/track/coaster trip timing. The disc phase lengths are now established for all
  83 ride definitions, with 59 flat-ride run calculations in `ride-phase-lengths.md`.
- What `mgr+4` (set A/B) is and who sets it; which coaster/track/tour records each scenario exposes.
- Record header bytes +0x09, +0x14..+0x17, +0x20 and the per-level words +0x24/+0x28 (`f48`/`f4C`).
- Whether the UI ever offers the (out-of-data) third upgrade to level 3.
- Statuses 8/9 (dead); complete track/coaster geometry, movement and park-adapter integration.
  Slot 86, loading/dispatch control and completion/unloading conditions are now READ in
  [ride-classes.md](ride-classes.md); the disagreements in §0 remain pending review.
- The shop product parameters at record +0x30..+0x36 and the feature flag byte +0x2E bits 1–3.
