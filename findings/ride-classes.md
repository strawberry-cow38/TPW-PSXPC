# Tour, track and coaster control — PAL SLES-026.88

2026-09-20. **READ** means instructions/data read directly from the supplied disc files;
**GUESS-high/medium/low** identifies an interpretation. All statements below are READ unless
marked otherwise. No live timing measurements were made. `A = outer+8`; offsets in this report
are **outer-relative**, or explicitly relative to a vehicle/record. This avoids silently moving
between the two pointer conventions in a class override.

Sources, read without changing either source tree:

- `/home/ec2-user/tpw/ext/TPW.BIN`, loaded verbatim at `0x80010000`, SHA-256
  `0b708dd74796abfedad55e55fa007ace1b5697407d36e8f7823bb06f4fa53beb`.
- `/home/ec2-user/tpw/ext/FOLIO.GAZ`, SHA-256
  `5666a0eeb18998a31fafbe845e7490cf65da76e722a69aec8b30ff2ee1a3c951`.
- Disassembled as little-endian MIPS32 with Capstone 5.0.7, including branch delay slots.
  Virtual entries are eight bytes: signed adjustment at +0, function at +4.
  Record/model reads use the existing `tools/ride_phases.py` archive parser.

## 1. Answers and scope

| Class | Loading | What duration actually controls | Unloading |
|---|---|---|---|
| Tour | One guest per global tick divisible by 20; three per vehicle | Vehicle destination arrivals in state 2; route movement, not model phases | One guest per 20 ticks, last boarded first |
| Track | One guest per 20 ticks; waits for the entire ride capacity | **Both** a `2*duration` ride-tick gate and a duration-lap limit per vehicle | After the tick gate, finished vehicles unload their whole batch on a 10-tick scan |
| Coaster | One guest per 20 ticks into a staging buffer; batches launch independently | Per-train return-segment counter; all disc duration limits are 1 | Each finished train unloads its whole batch without a cadence gate |

**A coaster does not end one global cycle when the last car gets home.** Its trains finish and
unload independently while another batch can board. The number 1 in the slider is real data,
but it is not one animation phase or a fixed trip time. **A track ride does have a tick gate**;
it is not the trip duration either. It subsequently waits for individual vehicles' lap flags.

The coaster's slot 86 is exactly `sharedOpen(status) && outer[0x104] != 0`.
It allows **only 2, 10, 11**, and only after the route's closing connection has been made.
The aggregate piece check is a separate predicate at outer+0x100. There is no additional
allowed status. **GUESS-high:** that check means the constructed geometry is usable; it is
not established as an animation/build-progress counter.

The C# slice ports scheduling, state decisions, lap counters, batch dispatch, unload traversal,
the additional queue veto, wear cadence and the final class-specific wear-rate combination.
World interfaces retain geometry, steering, animation, queue/list operations and guest transfers.
Those method contracts describe the observed transfers, but they are **not implementations** of
the park adapters. No fixed seconds-per-trip constant is introduced.

In particular, the existing `VisitorQueue` still uses the shared status-only predicate. A host
integrating these controllers must pass the coaster-specific predicate into attraction candidates
and wire the connection veto into its queue adapter; this patch does not change that older queue
API or pretend that every guest already uses the new veto. These are standalone sim routines,
matching the existing class-port pattern.

**Report retention:** the task explicitly requires keeping `rides.md` where it disagrees with
the binary. The code therefore keeps (a) its running-only wear/breakdown gate, (b) its direct
track/coaster breakdown to 5, and (c) its tour queue-empty dispatch without the newly found
empty-vehicle restriction. These are labelled retained rules in code and tested as such.
They are not confirmations of the binary. See §6 and `rides.md` §0 items 10–15.

## 2. TourRide

### Dispatch and boarding

Vtable `0x800E586C`: Update `0x800A0E88`, tick-2 `0x800A1334`, tick-10 `0x800A11E8`,
tick-11 `0x800A133C`, wear `0x800A1FE8`, rate `0x800A1CA4`.

Update walks the transport chain at outer+0x104, calling `0x800A2A4C` on each vehicle.
The dock pointer is outer+0x108; vehicle count is u8 outer+0x101. The allocator
`0x800A22A0` stops at **three** vehicles. With no docked vehicle and a nonempty queue, Update
tries to allocate another (`0x800A0F84..F98`); allocation failure simply leaves it without one.

After transport movement, statuses outside **4..9** are derived from the docked vehicle
(`0x800A0F10..FEC`): vehicle state 0 → attraction 10, vehicle state 7 → attraction 11, otherwise
attraction 2. No docked vehicle also means 2. This includes overwriting status 3; it is not a
generic `{2,10,11}` guard. Tick-2 `0x800A1334` is just `jr ra; nop`.

Tick-10 has **two mutually exclusive branches**:

1. On `unsigned globalTick % 20 == 0` (`0x800A1200..220`), fetch queue head. A missing head or
   state other than **18** ends this call. `0x800A3298` requires the docked vehicle's passenger
   byte +0x68 to be below `0x800A32C4()`'s return value. The latter reads record+0xD4 and
   then **returns literal 3**, discarding that read (`0x800A335C`).
2. On every other tick (`0x800A12C8..31C`), the binary tests vehicle passenger count first:
   **nonzero → return** (`0x800A12D4`). It then requires the queue to be empty. At least two
   vehicles → set vehicle+0x3D via `0x800A27C0`; fewer than two → enter vehicle state 1 directly.
   The flag means retire via state 8, not a normal loaded departure. Consequently the binary
   leaves a partly filled last vehicle waiting indefinitely for more guests. This is the
   explicit disagreement whose occupancy restriction is **not adopted** in the port.

The full-vehicle departure is in the transport's own state-0 tick: if passengers >=3 **or**
retirement was requested, and byte +0x2B is zero, enter state 1 (`0x800A2AF8..B38`).
The proximity sweep `0x800A3678` clears this byte, skips the vehicle itself, then tests other
transports within strict axis distances **2000, 1500, 2000** (`0x800A36DC..3728`). For a
loading vehicle, the first such neighbour leaves the byte zero if that neighbour is also
loading, otherwise writes **3**, blocking departure (`0x800A3730..3754`). The full sweep's
non-loading avoidance/height behavior remains host-side; there is no invented departure timer.

Boarding `0x800A2318` unlinks the queue head; sets guest **21**; calls guest slots 3 and 4;
appends to the ride's rider list; increments signed-halfword outer+0xF8; sends shuffle messages
(state **19**) with cumulative `rand(3)` staggering to the remaining queue; then appends the
guest to vehicle+0x40, incrementing its passenger byte (`0x800A3218`).

### Trip control

State byte is vehicle+0x2C; unsigned lap byte +0x3C; destination +0x30/+0x34/+0x38.
Tick jump table `0x800E5BF4`, entry jump table `0x800E5C1C`:

| Vehicle state | Tick condition and next action | Entry effect relevant to the trip |
|---|---|---|
| 0 | Full or retire requested, and no blocker → 1 (`0x800A2AE4`) | No entry work |
| 1 | Destination reached → 8 if retiring, otherwise 2 (`0x800A2B44`) | Releases dock; chooses departure destination; resets laps to 0 (`0x800A33A8..408`) |
| 2 | Destination reached → increment u8 laps, compare to live duration slot 91 (`0x800A2B74..C10`) | Select next touring destination (`0x800A3410`) |
| 3 | Destination reached → 4 (`0x800A2C18`) | Claims dock and selects return destination (`0x800A343C`) |
| 4 | Destination reached → 5 (`0x800A2C38`) | Next approach destination (`0x800A3498`) |
| 5 | Destination reached → 6 (`0x800A2C58`) | Final docking destination (`0x800A34F8`) |
| 6 | All three coordinates settle → 7 (`0x800A2C78`) | No entry work |
| 7 | Empty and attraction status outside 4..9 → 0 (`0x800A2C98..D10`) | No entry work |
| 8 | Destination reached → remove transport (`0x800A2D1C..D34`) | Retirement destination (`0x800A3550`) |

In state 2, `++laps >= duration` returns via 3 only when the dock pointer is null. Otherwise
**laps := 200**, and the vehicle enters 2 again (`0x800A2BD8..C0C`). It keeps travelling,
including the unsigned byte's eventual wrap. The port keeps this unusual sentinel.

`0x800A382C` follows a spatial destination; `0x800A3C70` settles each axis by subtracting
`(current-target)/5`, truncating toward zero, and snaps to target if that would make no change.
Entering the moving states supplies a base speed from the low byte of **record+0xD0** via
`0x800A1744`, with state-dependent fractions (`0x800A33D8..3544`). Setter `0x800A35B8` then
reads speed slider slot 89, adds `trunc(clamp(slider-100,-50,50)/10)` to that base, stores
the signed-halfword result at vehicle+0x28 and raises values below **2** to 2
(`0x800A3608..3660`). Thus the tour's slider has a discrete additive effect on the target
speed; it does not multiply the base speed by slider/100. The complete steering/waypoint geometry
has not been ported. An animation modulus is not a substitute: the cache documented by the
phase report is used by rendering `0x800A2EB4/0x800A2F04`, not by these trip comparisons.

### Unloading

Tick-11 tests `tick % 20 == 0` and calls `0x800A2464`. It pops **one last passenger** from
the docked vehicle (`0x800A3264` decrements +0x68 before indexing +0x40), unlinks the rider,
sets guest **22**, positions it at the ride exit, calls `0x80053C04`, then decrements riders
(`0x800A2570..57C`). Empty vehicle → no guest action. Vehicle state 7 handles becoming loadable
after it empties; the ride does not count animation phases to do this.

## 3. PathedRide / track

Vtable `0x800E5FE8`: tick-10 `0x800A67F4`, tick-2 `0x800A6960`, tick-11 `0x800A6A58`.
The 34 embedded 0xC0 objects at outer+0x198 are route pieces. Actual vehicles are allocated
as **0x8C** objects by `0x800A82B0/0x800A82D4`, in the pointer array at outer+0x1CA4 with
u8 count outer+0x1CA0. Constructor choice uses record+0xD4 (`0x800A9C78`, 0/1).

### Loading and starting

Every `tick % 20 == 0`, compare signed riders outer+0xF8 with capacity slot 90.
Below capacity, only a queue head in state **18** can board (`0x800A6838..6900`).
There is no short-queue timeout or partial-load departure. At or above capacity, clear
outer+0xFA and SetStatus(2) (`0x800A690C..944`). **The last boarding itself returns without
this transition**: starting waits for the next cadence call.

`0x800A80F0` performs the same guest/list/shuffle sequence as the tour. It reuses the last
vehicle while `0x800AA0C8` says passenger byte vehicle+0x80 < **record+0xD0**; otherwise it
allocates and initializes another. `0x800AA070` appends the new guest at vehicle+0x5C.

### Two different counters

Tick-2 first checks route-closed byte outer+0x18A. Zero → SetStatus(**3**) without counting
(`0x800A6978..9AC`). The byte is set when the newly added route point equals the connection
position (`0x800A7D68..D84`); a subsequent edit clears it. Otherwise increment signed-halfword
outer+0xFA **on every call**, then compare with **slot91 << 1** (`0x800A69B0..A38`).
At or above it, enter **11**. No animation call occurs here.

Independently, moving vehicle route position is u16 +0x24. On reaching/exceeding the route
length from `0x800A9D5C` (outer+0x1CCC), reduce modulo that length, increment **signed-byte**
vehicle+0x2C, and compare with live duration slot 91 (`0x800AA99C..AAA30`). At or above it,
set vehicle+0x81 = **1**, the flag returned by `0x800AA23C`. The comparison is signed after
the increment; wrapping 127→−128 is not saturating arithmetic. Finishing also adjusts +0x28
by `(5 - vehicle[0xC])*100 - routePosition` (`0x800AAA34..A68`); this movement detail is
documented but remains with the host.

Thus the usual default duration **5** opens the unload gate after **10 calls**, but the
passengers remain aboard until their own vehicle finishes its route laps. Neither those
10 calls nor any model's mesh length establishes the actual trip time.

### Unloading and the compacted-array skip

Tick-11 scans on `tick % 10 == 0` (`0x800A6A74..A94`). If riders were already zero, it changes
11→10 only; when called from another status it preserves that status (`0x800A6AF8..B40`).
Otherwise it scans the current vehicle count, calling `0x800A8BD4(index)` for every vehicle
whose +0x81 flag is nonzero.

Unload first returns without action if outer+0x100 is nonzero (`0x800A8C00..C14`);
**GUESS-high:** this pointer is a preview/test controller (movement calls its methods too).
Otherwise it pops **all passengers**, last boarded first (`0x800AA094`), sets each to 22,
positions it at the exit, calls `0x80053C04` and decrements riders. It destroys the vehicle,
compacts the pointer array left, and decrements the count (`0x800A8D2C..DDC`).

**DO NOT FIX:** the caller still increments its index (`0x800A6ADC..AEC`). Adjacent finished
vehicles are skipped after compaction: `[ready A, ready B, ready C]` unloads A and C, leaving B
for another scan. The empty-ride transition also waits for a later cadence; it is not retested
after that pass. Status-4/5/6 tick hooks delegate to this unload routine (`0x800A6B5C..BF4`).

## 4. RollerCoaster

### Slot 86 and the object distinction

`0x800AD78C..D0` calls shared `0x800632D8` and forces false if outer+0x104 is zero.
`0x800AF7C0` checks whether the newly supplied endpoint matches the ride connection;
with at least one route piece it calls `0x800ADEA0`. That routine connects the last piece
and the station-side piece using `0x800B6790/0x800B6788`, refreshes them, then writes **1**
to +0x104 (`0x800ADEF4`). Constructors and route edits clear it (`0x800ACAF8`, `0x800AFA04`,
`0x800AFECC`, `0x800AFFA4`, `0x800B1944`). The predicate is a completed-connection veto.

The reported “32 car sub-objects of 0x40 at outer+0x18C” are **route pieces**, not passenger
trains: the construction/editor adds and links these objects, and movement calls their next/
previous/length accessors (`0x800B6770/77C/798`). Passenger trains are the **eight 0x88-byte
objects** constructed at outer+0x9A0 (`0x800B1ACC` → `0x800B1AFC..BB8`), with free and active
lists at outer+0x994/+0x998. This disagreement is recorded; no new route-geometry model
based on the disputed old layout was implemented.

### Loading, batch launch and partial launch

Tick-10 `0x800B0DC8` calls the station animation clock but **ignores its result**. It admits
one state-18 queue head on `tick % 20 == 0` only if connected (+0x104), piece checks pass (+0x100),
no reserved train (+0xE28), and total riders < capacity (`0x800B0E4C..F58`). **GUESS-high:**
the reserved train is a preview/test train; that name below is shorthand, not a proven UI label.
The aggregate piece check
is recomputed by `0x800B0BE8`: every used piece plus both station pieces must have a nonzero
`0x800B671C()` word (+0x3C). Editors write this word from `0x800AECFC`'s validation result
(`0x800AF654..664`, `0x800B2EEC..F00`); a fresh piece is initially assigned 1
(`0x800AF880..88C`). It is not the connection flag used by slot 86.

The load transaction `0x800B025C` moves the guest to 21 and the ride list, shuffles the queue,
increments total riders and appends to pending buffer outer+0xDE8, count +0xDE0.
After incrementing, it tries to launch if the pending count reaches either:

- `min(0x800AD610(), capacity slot90)`, or
- **16**, the explicit additional bound at `0x800B0400`.

`0x800AD610` obtains the station model's attachment count: `0x800305F8` → descriptor+0x3A,
built from u16(mesh+0x28) at `0x8002C608..610`; zero falls back to **1** at `0x800AD654`.
The mesh selector is `0x800B30D4(station,3)`; the complete selector/geometry adapter remains
host-side, so the port requires its decoded batch size rather than guessing one.

There is also an independent Update counter +0xDE4: increment every update, and when it
reaches **241** call the same launch routine and reset the counter (`0x800B0D14..D34`).
This is not “240 ticks since the first waiting guest”: it is a repeating attempt, even with
no pending passengers, and a failed attempt resets it too.

`0x800B01B8` requires nonzero pending count and a free train (`0x800B1818` tests the free-list
head for null). It takes one of the eight objects, starts it on outer+0x14C, transfers the
whole pending batch into train+0x30, then zeros pending count and elapsed field +0xE2C.
Exhausting the train pool leaves the pending batch intact. No whole-ride SetStatus(2) is
needed for this launch; an attraction in **10** can already be operating trains.

The maximum-capacity slot 99 is also overridden: `0x800AD728` returns
`min(u8(record+0xD2),8) * 0x800AD610()`. Coaster wear's denominator is this virtual result,
not an unconditional read of the common per-upgrade seat word.

### Train completion and unloading

Movement `0x800B1F84` integrates speed and route progress, follows linked segments, and then
tests completion at `0x800B2220..2C8`:

1. Preview train (+0xE28) is exempt.
2. Launch flag train+0x78 must be zero. It starts as 1; forward segment handoff clears it.
3. Current segment train+0x20 must equal the station/return segment from `0x800ACC40`.
4. Progress comparison operand **s2 > 0x800** (strict). **s2 is computed at 0x800B218C before
   the possible segment handoff**; do not recompute a different post-wrap fraction for this test.
5. Increment signed-halfword train+0x74; compare to live duration slot 91; at or above it set
   train+0x7C = 1 (`0x800B22C4`). The flag latches. There is no edge debounce in this block.

This is route return control, not `0x800658D8` animation completion. The pre-handoff progress
detail also prevents calling it an exact geometric “all cars have stopped at home” test.
The twelve disc definitions have duration min=max=1 at every level, but the code still reads
the slider. A higher forced value is not proof of cleanly counted whole laps: repeated calls
in the qualifying return segment can increment again. The port preserves that behavior.

Tick-11 `0x800B0F90` saves the next active-list pointer before processing each train and unloads
every one with +0x7C != 0. **No cadence gate.** `0x800B043C` excludes the preview train, then
pops all passengers in reverse order using `0x800B1D04`, calls shared exit `0x8009DE88`,
decrements riders per guest, returns the train to the free list, and decrements active count.
Other unfinished trains do not hold up the finished train's passengers.

Tick-10 ends by calling tick-11 regardless of boarding eligibility (`0x800B0F5C`).
Tick-2 **calls tick-10 and then tick-11 again** (`0x800B0D90/0x800B0DAC`). The duplicate scan
is retained. No attraction-level phase count or “last train returned” status transition
occurs in either handler.

## 5. Wear differences and the exact arithmetic boundary

### Call scheduling (binary evidence; disputed report gate retained in code)

- Tour Update calls slot 103 whenever total riders != 0 (`0x800A0EAC..ED4`), before stepping
  transports, **without testing attraction status**. Its override admits `(tick & 0x1E)==0`
  (`0x800A1FFC`), then the shared step admits `(tick & 3)==0` (`0x8009DD74`). Together this
  means `(tick & 0x1F)==0`: **once per 32 global ticks**, not twice and not every four.
- Track motion `0x800A8E0C` enters the motion/wear path when `(vehicleCount != 0 && status != 10)
  **or** outer+0x100 != 0 (`0x800A8E30..E58`). It calls slot 103 unless `0x80053D78()==2`
  (`0x800A8E60..EA8`). Thus unloading can still wear the ride. The exact mode label is not
  established here; it is not silently called “paused”. Slot 103 itself is shared, every four ticks.
- Coaster tick-10 calls shared slot 103 when connected and riders != 0 (`0x800B0DF8..E48`),
  independently of its boarding gates. Status 2 reaches that through tick-10; status 4 also
  explicitly invokes wear (`0x800B1054..109C`). Again **not running-only**.

### Rate

The mode suppression `0x80059A9C()!=0 → 0` is present in each rate. Let Q=4096,
`f=slot100`, `g=slot101`, `m=slot102` (record per-level words +0,+4,+8), `speed=slot89`,
and `loadCount = a1 != 0 ? capacity(slot90) : signed riders(outer+0xF8)`.

The common speed/load portion, read at tour `0x800A1DBC..FA4`, track `0x800A8794..8990`,
coaster `0x800B06E8..884`, is:

```
s = (((Q-f) * trunc((speed << 12)/100)) low32) >> 12
s = speed < 100 ? s+f : trunc((s+f+Q)/2)
L = trunc((loadCount << 12) / slot99)
l = (((Q-g) * L) low32) >> 12
l += g
if L >= 0xCCC: l += (((L-0xCCC) >> 6)^2) low32
```

Arithmetic shifts are signed; division truncates toward zero. Products above use **mflo**,
not a widened fixed-point product. The quadratic term has no further `>>12`. The threshold
is the literal **0xCCC**, not a rounded conversion of 0.8. The report's existing approximations
are not silently rewritten as exact shared production arithmetic: only the final combination
below is implemented, taking these already-computed terms as explicit inputs.

- Tour and coaster: `k = low32(trunc((s+l)/2) * m)` (`0x800A1FA8..FBC`, `0x800B0888..89C`).
- **Track adds route size**: `p = trunc((u8(outer+0x1BD8)<<12)/30)`;
  `k = low32(trunc((s+l+p)/3) * m)` (`0x800A8994..9E0`). The byte counts the generated
  0xC0 route pieces traversed at `0x800A8EB0..ED4`; it is not the vehicle count at +0x1CA0.

The shared decrement `0x8009DD5C` subtracts `k>>5`, updates lifetime bands, and finally clamps
negative reliability. **Additional report discrepancy:** it computes the new lifetime band
from the **unclamped** new reliability (`0x8009DDB4..DF0`) and divides raw reliability by
**61440 = 15*4096**, truncating toward zero. The clamp occurs at `0x8009DE60..70` afterward.
The lifetime setter stores a signed halfword (`0x800631E8`); its getter sign-extends
(`0x800631FC`), and negative lifetime is clamped separately (`0x8009DE3C..E5C`). This shared
arithmetic is documented, not added to the new port behind the user's retention rule.

### Disc cross-check

All 12 coasters: duration min/max **1/1** at all three levels; all eight track rides and four
tours: **1/10**, default **5**. All track and tour record seat maxima are **8/8/8**. Tour
wear multipliers are **7/7/7**; track/coaster **5/3/2**. These are direct record reads, not
values inferred from class names. The existing phase report lists all 24 model containers.

These extra fields are outside the shared level blocks; addresses below point to the start
of the definition record within GAZ. For type 7, +0xD0 is movement speed and +0xD4 is the
discarded passenger-limit read. For type 6, +0xD0 is **passengers per vehicle**, +0xD4 selects
one of the two constructors. Identical offsets do not mean identical parameters across classes.

| FOLIO | Name | Type | Record address in GAZ | u32 +0xD0 | u32 +0xD4 |
|---|---|---|---|---:|---:|
| 0x029 | Bumble Buggies | 6 | 0x1AC8EC | 1 | 1 |
| 0x038 | Tweety Tours | 7 | 0x1E2260 | 30 | 9 |
| 0x039 | Taptastic Rapids | 6 | 0x1EB198 | 1 | 0 |
| 0x082 | Crypt Karts | 6 | 0x2C9F10 | 2 | 1 |
| 0x08E | Flightmare Tours | 7 | 0x2F35E4 | 35 | 5 |
| 0x08F | Ooze Crooz | 6 | 0x2FD0DC | 4 | 0 |
| 0x0D0 | Jurassic Tours | 7 | 0x39C61C | 35 | 9 |
| 0x0D7 | Dino Karts | 6 | 0x3C144C | 1 | 1 |
| 0x0E4 | Splish Splash | 6 | 0x3F3900 | 5 | 0 |
| 0x16B | Space Racers | 6 | 0x6C7514 | 1 | 1 |
| 0x174 | Star Tours | 7 | 0x6EB3F8 | 50 | 10 |
| 0x17A | The Blobulator | 6 | 0x70509C | 5 | 0 |

## 6. Disagreements submitted for review

These are flags against the existing report, **not permission to switch the port**.

1. **Track/coaster `<10 → 5` is contradicted.** Track passes **4** at `0x800A6754`; coaster
   passes **4** at `0x800B0B34`. Both pass **5 only when reliability equals zero and status
   already equals 4** (`0x800A6758..788`, `0x800B0B38..6C`). Track's nonzero-low branch has
   no status gate; coaster admits **2 or 10** (`0x800B0A88..9C`). `SetStatus` is the shared
   slot 57, not a class remapping. Existing direct-5/running-only behavior stays in OtherRideWear.
2. **Wear is not restricted to status 2** for these classes; callers listed in §5. The shared
   report gate stays in OtherRideWear. Newly established cadence differences are implemented.
3. **Tour queue-empty dispatch has an additional empty-vehicle condition** and the spare-vehicle
   branch requests retirement. See `0x800A12C8..31C` and the flag readers `0x800A2B14/0x800A2B54`.
   Port retains the report's queue-only gate, with the disagreement attached to the line.
4. **Coaster's “32 cars” are route pieces**; actual trains are eight objects of 0x88, §4. The
   new port does not instantiate either disputed geometry layout; it consumes active trains
   from its world interface and calls the traced completion logic.
5. **The shared wear pseudocode hides the lifetime-band order and signed rounding**, §5. No shared
   reliability/lifetime arithmetic rewrite was introduced.
6. **The shared slot-99 record mapping does not describe coasters**: `0x800AD728` multiplies
   a clamped byte from record+0xD2 by a station-model attachment count. That virtual result
   supplies the wear denominator (`0x800B07A0`, `0x800B0804`). Common per-upgrade seat words
   are still genuine disc values; equating them with this getter is the unsupported step.
   Existing definition/slider code is not replaced; the new final-rate combiner accepts the
   already-computed load term and does not choose a competing denominator.

New evidence filling a gap rather than contradicting it: tour's literal three-seat helper;
track's third wear term and two clocks; coaster's connection veto, 241-call batch attempt,
virtual capacity denominator, live duration read, and duplicate unload scan. The old §7
explicitly stopped at addresses, so these are marked READ in the new code.

## 7. Validation and remaining boundaries

**VALIDATION (host tests, not PSX measurements):** the checkout started with **612 passing
xunit cases**, not the prompt's approximate 640. The restored full suite now passes **704/704**;
the existing archive/phase tests pass **12/12** against FOLIO.GAZ. Tests for the new controllers
state what alternative they reject. Run:

```
dotnet test tests/TPW.Sim.Tests/
python3 tools/mutate_ride_classes.py
TPW_PHASE_ARCHIVE=/path/to/FOLIO.GAZ python3 -B tools/test_ride_phases.py
```

The mutation runner rebuilds after each isolated production change, requires failing executed
xunit tests (a compiler failure is an error, not a kill), restores every file in `finally`, and
reruns the restored baseline. Its TRX files retain the failed test identities. **All 152 distinct
mutations were killed by executed tests; no unresolved survivors.** The initial track 20→10
loading-cadence mutation survived because the fixture omitted tick 10. Adding that negative case
made it fail; the coaster fixture received the same check. The initial run and targeted rerun
together covered every listed mutation, with sources restored and the full suite green afterward.

**NOT ESTABLISHED:** live seconds per tour/track/coaster trip; full steering and collision
behavior; complete destination selection and avoidance response; exact meaning
of the preview/controller pointers; end-to-end rendering/physics and park-adapter wiring.
There is no invented default trip time, route length, speed, or attachment count in the C#.
Tests use declared synthetic world inputs, including width-boundary states, not assumed disc
measurements. They verify this control slice, not the omitted adapters or the live PSX game.
