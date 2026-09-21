# Theme Park World (PSX, PAL, SLES-026.88) — STAFF: hire, walk, draw, reach a ride

READ = instructions/data at the quoted address in TPW.BIN (load base 0x80010000).
GUESS = interpretation, with confidence. Image SHA-256:
`0b708dd74796abfedad55e55fa007ace1b5697407d36e8f7823bb06f4fa53beb`.
Disc data below is READ from the named FOLIO.GAZ entries, with the consuming code quoted.
`P` is the Person/Staff subobject; `O` is the pool node / concrete object: **P = O+8**.
Vtables have eight-byte entries: signed adjustment at +0, function pointer at +4.

## SOURCE DISAGREEMENTS

The earlier findings remain the specification where they conflict, including their accepted
corrections in wages.md and ride-panel.md. New disagreements are review items, not silent
corrections. `Mechanic.cs` is untouched. Previously identified corrections below are carried
forward, not re-derived.

| Existing source | Binary READ | Treatment |
|---|---|---|
| economy.md §4.4's “hiring” price and §3.1's old kind mapping | **Carried from wages.md §1:** 0x80085728 / 0x80094DC0 is training. Real placement calls TrySpend(0) at 0x8001E6EC. The placer switch 0x800DC5A4 orders kinds mechanic/entertainer/cleaner/guard/researcher. | Retain wages.md's accepted correction: hiring is free, with the existing StaffKind order. |
| wages.md §3.2 / §4: new staff morale 100, tiredness 0 | 0x800941E0..0x80094224 calls rand(30) **twice**, first morale = 70+roll, then tiredness = roll. The 100/0 reset actually belongs to **training upward**, 0x80095650..84. | StaffHiring retains 100/0 and does not invent constructor RNG consumption under the older specification. Binary ranges are 70..99 / 0..29. |
| behaviour.md §3 layout: P+0x3C is an animation word, skill at S+0x44 | 0x800956F0 reads skill from **P+0x3C & 7 = O+0x44 & 7**. 0x8009571C reads **P[0x3C] >> 3**, the base walk speed. The actual animation is P+0x2C bits 19..23 (0x800936F0..700). | Existing StaffMember.Skill remains the semantic skill. New speed helper documents the actual offsets; no existing state handler is relabelled or changed. |
| behaviour.md §3.4 / Guard.cs: animation 15 on idle, 30 on dispatch | Calls to 0x800956FC at 0x8009821C / 0x8009850C write the **speed** bits: 15 / 30. | Guard's existing SetAnimation calls retained. Host integration must keep this discrepancy visible; StaffMotion accepts the packed speed explicitly rather than inferring it from state or silently changing Guard. |
| wages.md §3.4: 0x8004D800 is a path-type drop test, apparently accepting type 0xC | 0x8004D874..8A4 **rejects** raw tile type 12. Full predicate accepts any type except 4,5,7,8,10,12, with tile flags 0x01,0x02,0x10 all clear; raw ground type 0 with flags 0 is accepted (0x8004D800, helpers 0x8004D510..650, flag getter 0x8004D358). | Hiring's world interface retains the report's path-placement policy. This port does not quietly replace it with the broader binary predicate. |
| wages.md §3.3: placer movement “snaps” | 0x8001E64C..68C copies tool+0x2C/+0x30's **8.8 cursor position**, not tool+0x3C/+0x40's tile coordinates. 0x80019D90..DCC eases the cursor; 0x80019E50..70 derives the separate tile. No staff tile-centre snap appears here. | Cursor positioning is left to the world, with no guessed snap interval. No snapping code was present to change. |
| wages.md §3.2 names 0x8005D8C8 as the allocation operation for every class | Mechanic creator 0x800516F0 calls 0x8005D74C → **0x8005D76C**. Guard uses the 0x8005D8C8 family; same list algorithm, different instantiated pool types. | Pool ownership remains behind IStaffHireWorld. |
| behaviour.md §3.5: service-due queue; CloseRide interpreted as waiting for riders | **Carried from ride-panel.md SOURCE DISAGREEMENTS:** 0x8005BE44 is the upgrade queue; 0x8009C1E8 compares A+0xEC against `10*(w+h)<<12`, not a rider count. | Another workstream owns the Mechanic correction. Both repair and upgrade use the job point established here. |

Additional **code** disagreements exposed by this trace (not claims in an older findings file):

- READ: `Mechanic.UnknownSecondColumn` is slot-44 speed (0x800972BC); the third column called
  `Handyman.UnusedThirdColumn` is also slot-44 speed (0x80098A68). behaviour.md correctly said these
  columns were not read by the handlers it had examined; their meaning is now established, not a
  contradiction of those narrower statements. StaffMotion uses the existing arrays, without copying
  the numbers or editing Mechanic.cs.
- READ: PeopleSheet's **eight facings × five frames** interpretation is reversed for the four
  uniformed staff. Their resource tables are **five stored facings × eight frames** (FOLIO 263/264/
  266/274, animation pointer at +0xC4 → +0xCC); 0x8002BB58..68 folds facing 5..7 to 3..1.
  The new staff frame helper does not use PeopleSheet.WalkSprite's picture-based heuristic.
- READ: PeopleSheet's costume blocks are table slot **8**, whereas the hired entertainer requests
  slot **13** (0x80096084..88). It has **no row in sheet 269**: its resource is 403/401/402/404 by
  theme. Do not choose a CostumeBlocks entry as an established substitute.
- READ: ParkRideWorld.TryPathToLeavePoint currently delegates to its entrance helper. Ride slot 26
  returns the **last queue-path tile**, not the door (0x8009D508 → 0x8009FAA8). No game host changed.
- READ: Mechanic.LeaveRide falls back to Idle on request refusal. 0x800970C0 branches directly
  to the return on refusal, preserving its state. The controller is left untouched.
- READ: StaffBase.IdleCheck implements morale loss above 90 tiredness as `now%4 == 0`.
  0x80094774..8C uses **((now XOR P+8) & 3) == 0**, a per-person phase. behaviour.md §3.1
  specified the period but omitted this phase; its cadence is right. Existing code is retained.

## 1. Hiring and initial placement

### 1.1 Button to pool (READ, wages.md §3 confirmed and extended)

The recruit-card confirm 0x8007D554 starts tool state 1 with record variant (+2) and kind (+0x10)
through 0x800193A4. The staff placer is 0x80104A98, vtable 0x800DC524, create 0x8001E508.
It dispatches by **kind**, not the order of rows on the hire screen:

| Kind | Staff | Pool pointer | Creator | Object constructor | Hire initializer | Node stride |
|---|---|---|---|---|---|---|
| 0 | Mechanic | 0x80103868 | 0x800516F0 | 0x80096374 | 0x80096408 | 0x4C |
| 1 | Entertainer | 0x80103874 | 0x80051900 | 0x80095778 | 0x80095BAC | 0x120 |
| 2 | Cleaner / handyman | 0x8010386C | 0x800517A0 | 0x80098930 | 0x800989C4 | 0x4C |
| 3 | Guard | 0x80103864 | 0x80051640 | 0x800976F0 | 0x80097784 | 0x4C |
| 4 | Researcher | 0x80103870 | 0x80051850 | 0x80099830 | 0x800998C4 | 0x50 |

READ: each pool holds **five**, free head +4, employed head +8, employed count +0xC, nodes at +0x10.
Mechanic pool construction 0x8005FE18 / reset 0x8005FEA8 constructs all five nodes and prepends slots
0..4, so first allocation takes slot 4. Allocation 0x8005D76C moves the free head to employed and
increments the count. The creator stores the variant through slot 52, calls slot 2's initializer,
and registers P for updates at 0x80053C04. Thus employment begins **on the cursor**, before dropping.

READ: class initializers look up `(kind, variant)` at 0x80069EF4, store the record at P+0x30 and
clear record+0x1C. They all call Staff init **0x800941C0**, which calls Person init 0x800927F4.
The constructor/initializer distinction matters: pooled objects already have their class vtable
before hiring; a reused node still needs its per-hire initializer.

### 1.2 Starting fields (READ unless the semantic label says GUESS)

| Field | Binary initialization | Evidence |
|---|---|---|
| state / target / waypoint head | 0 / null / -1 | 0x8009281C..30; held reset 0x80093BAC |
| animation | 13 | 0x800941D8..F8 sets P+0x2C bits 19..23 |
| morale (GUESS-high label, behaviour.md §3) | 70 + rand(30), **first roll** | 0x800941E0..0x8009420C |
| tiredness (GUESS-high label) | rand(30), **second roll** | 0x80094210..24 |
| skill | record+0x14 & 7 → P+0x3C bits 0..2 | 0x80094248..6C, getter 0x80095764 |
| record motivation | record+0x18 → P+0x3E, signed-byte clamp 0..100 | 0x80094268..7C, 0x80095758, 0x800956B4 |
| patrol rectangle | (0,0,-1,-1), unassigned | 0x800950EC |
| packed base speed | 15 → P+0x3C bits 3..7 | 0x800942A8..AC, 0x800956FC |
| hired day | McAi total days → P+0x34 | 0x800942B0..C8 |

READ: record motivation is **not** the starting morale byte; these are different fields.
READ inherited evidence: wages.md §2.4's measured recruits have levels 0 or 1; named examples are
Mike Armstrong 0 and Karl Jeffery 1. Their binary mechanic speeds are consequently **9 and 12**.
The port intentionally keeps the older report's initial 100/0, not the binary rolls (disagreement).
READ evidence boundary: wages.md's static-address table at 0x800F2AF0 was **MEASURED in RAM**;
that block is zero in TPW.BIN. This does not contradict its measured contents. Do not mistake the
image's zeros for every recruit's initial skill. The runtime lookup scans manager+0xF4, count at
manager+0xF0 (0x80069EF4); the host supplies the selected record level.

### 1.3 Where the new employee stands; cost; cancellation

READ: 0x80095358 → 0x80093BAC clears target/path, sets state 0, and sets the held/ghost flag
P+0x2B bit 0x40. Placer movement 0x8001E5E8 gets the **current placement cursor's position** from
the tool singleton 0x8010972C, via 0x8001A014. It writes its x/z as signed 8.8 halfwords at
P+0x18/+0x1A (0x80093FC0). It does not ask for a gate or recruit-specific spawn location.
Cursor initialization from a tile can centre it (0x8001934C); subsequent easing can leave fractional
positions (0x80019D5C). GUESS-high: this is the visible placement cursor the player moves over the park;
no claim here about which pad input first positioned it relative to the camera.

READ: confirm 0x8001E720 checks the cursor's tile (`x>>8,z>>8`) through 0x8004D800; failure keeps
the recruit held. Success calls 0x8001E6BC: ghost off, **SetState(0)** clearing the state stack,
TrySpend of placer+8, finish placement. Move explicitly zeroes placer+8 at 0x8001E650, so hiring
costs **£0**. Monthly wages and training are separate (wages.md). The TrySpend result is not a
commit veto. The position already written by movement remains the position after dropping.

READ: cancel 0x8001E870 → 0x80051B2C releases the employed node and its record. There is no
“pay for the recruit first” step. An undropped recruit is already in the wage list; cancelling
removes it. Pool-full handling and pad/UI ownership stay with the host in the port.

## 2. THE WALK SPEED

⭐ READ: **there is no staff P+0x60/P+0x62 speed field.** A normal uniformed node is only 0x4C
bytes, including its eight-byte list prefix. Those offsets belong to the larger Visitor object.
The shared Person walker calls **virtual slot 44**, not a common field getter.

| Class | Person vtable | Slot 44 (adjustment) | Result |
|---|---|---|---|
| Mechanic | 0x800E43A4 | 0x800972BC (-8) | u16 table 0x800E4574[skill & 7].second: **9,12,14,16,18** |
| Cleaner | 0x800E4A68 | 0x80098A68 (-8) | s32 table 0x800E4C38[skill & 7].third: **10,15,20,20,18** |
| Entertainer | 0x800E41D4 | 0x8009571C (0) | P[0x3C] >> 3, initialized **15** |
| Guard | 0x800E4750 | 0x8009571C (0) | same; dispatch writes **30**, idle writes **15** |
| Researcher | 0x800E4C74 | 0x8009571C (0) | same, initialized **15** |

READ: mechanic rows are `(repair ticks, speed)` u16 pairs: `(240,9),(180,12),(120,14),(60,16),(60,18)`.
Cleaner rows are s32 triples `(clean ticks, empty ticks, speed)`:
`(120,180,10),(60,120,15),(30,60,20),(20,30,20),(10,15,18)`.
⚠ DO NOT FIX: the most skilled cleaner walks **slower** than skill 2/3.
The skill is read each call, so training changes speed immediately; there is no birth-speed roll.
READ scope clarification: behaviour.md §2.7's `slot 44 = V+0x60, 15..29` describes the **Visitor
override**, not a field common to all Person subclasses. Its visitor rule is unchanged.

READ: 0x80093384..B4 dispatches the getter, multiplies it by 0x800BDD0C's timescale, takes the
**low 32 bits**, and shifts **logically right 14**. The result is an 8.8 displacement, not tiles.
At timescale 0x4000 the displacement is the speed itself. Both axes independently clamp their
remaining distance to [-step,+step], storing signed halfwords (0x800933B8..400).
⚠ DO NOT FIX: diagonal movement gets a full step on each axis. Existing VisitorWalking ports
the rest of this shared walker; StaffMotion exposes the staff getter and displacement without
duplicating another state machine.

READ bound: legitimate skill rows 0..4 are established. The binary masks to three bits and can
read past the table for 5..7; no meaning is established for those rows. StaffMotion throws for
these indices instead of fabricating a speed or silently saturating to row 4. Packed base-speed
inputs represent the five stored bits. Guard's speed/animation disagreement remains unresolved
in the existing state controller; do not infer speed 30 solely from current state, since the
speed persists until idle writes 15 again.

## 3. Drawing: class, resource, sheet row, animation

READ: Person draw 0x800937D0 calls virtual slot 45 to acquire a drawable, then 0x800935C0 supplies
terrain height, facing (P+0x2C bits 16..18) and animation (bits 19..23). The drawing manager
0x80058F34 → 0x80031680 chooses its resource by the class's fixed index in the theme table.
Theme table pointers are at 0x800DFF74; four rows of fourteen u32 entries begin at 0x800E002C.

| Staff | Slot 45 | Manager index | FOLIO animation resource | Sheet 269 base sprite | PeopleSheet.Blocks index |
|---|---|---|---|---|---|
| Mechanic | 0x800972E0 | 9 | 263 | 264 | 8 |
| Guard | 0x8009852C | 10 | 264 | 308 | 9 |
| Cleaner | 0x80099590 | 11 | 266 | 352 | 10 |
| Researcher | 0x80099C5C | 12 | 274 | 396 | 11 |
| Entertainer | 0x80096064 | 13 | **403,401,402,404** by theme index 0..3 | **none** | **none** |

READ: the four uniformed entries are invariant across themes. Resource→base pairs are u16 pairs
at 0x800DFFFC, searched by 0x800315A8. They are not indexed by StaffKind enum order.
READ: the class slot-45 methods use literal manager indices, with no record-variant lookup; every
recruit of a given uniformed class therefore selects the same resource.
READ: 0x80031668 selects the separate drawable pool only for manager index 13; the entertainer
uses 0x80032118's frame upload/draw route (0x8002B63C / 0x8002B6D8). Uniformed staff use
0x800324D0, adding their base sprite to the resource's frame number in sheet **269**, loaded at
0x80031A0C..30. Resources 401..404 are animation packs with frame image data, **not TextureSheet
headers** and not blocks of sheet 269. Their visual character names are not established here.

### Frame table (READ, not a picture-based guess)

FOLIO 263/264/266/274 each has two animations: pointer at +0xC4 → +0xCC, pointer at +0xC8 → +0x120.
The first starts bytes `08 05`, then forty u16 frame ids **0..39**. The second starts `04 00`
and repeats **40,41,42,43** for each stored facing. The same two frame-id tables are at the tails
of the entertainer resources (walk pointers 401:+0x3B6C, 402:+0x1FC0, 403:+0x2BCC, 404:+0x3654).
The consuming getter 0x8002BDA8 computes `table[2 + 2*(facing*frameCount+frame)]`.

READ: manager remap at 0x800F1EAC maps Person animation **13 → resource animation 0**, and
**15 → resource animation 1** (0x80032408..1C). Counter initialization 0x80032374..90 sets the
frame period to **4 draw calls**; 0x8003243C..A4 advances and wraps against the animation's count.
This is render cadence, not staff movement speed. The new helper takes the caller's frame index;
it does not introduce a guessed simulation-to-render clock conversion.

READ: relative facing = `(personFacing + cameraFacing) & 7` (0x80032488..94). For 5/6/7, the lookup
uses **8-facing** = 3/2/1 (0x8002BB58..68). The renderer passes `relativeFacing < 5`
(0x800325C8..E4) to 0x8003377C: **0..4 flip horizontally**, 5..7 do not. READ: the true arm
writes `U+width-1` to the left vertices and U to the right (0x800337EC..83C); the false arm writes
U on the left (0x80033840..88). ⚠ DO NOT FIX: the folded directions are the unflipped draws.
Thus walk frame id is `foldedFacing*8 + frame`, frame 0..7. Static uniformed sprite = base + id;
the entertainer uses the id within its separate resource. Frames 40..43 are the standing cycle.

## 4. Mechanic destinations

### Job point: outside the entrance (READ)

0x80096E9C (repair) and 0x80096F70 (upgrade) free old waypoints and ask the attraction's **slot 42**.
The Ride, FlatRide, TourRide, TrackRide and Coaster vtables (0x800E519C / 0x800E5514 / 0x800E586C /
0x800E5FE8 / 0x800E65A0) all point to **0x8009D240**. It adds the ride's origin tile to the rotated
record entrance offset, then moves one tile outward along `(rotation + entranceFacing) & 3`.

READ: record entrance offset = s16 pair +0x0C/+0x0E (0x80066004); facing = byte +0x14's low two
bits (0x80062F48 → 0x80065FF8). Offset rotation is 0x800634B8, with unrotated record width w/depth h:

| rotation | rotated offset (ex,ey) |
|---|---|
| 0 | (ex,ey) |
| 1 | (ey,w-1-ex) |
| 2 | (w-1-ex,h-1-ey) |
| 3 | (h-1-ey,ex) |

READ: outward steps for facing 0/1/2/3 are `(0,-1),(-1,0),(0,1),(1,0)` (0x8009D2DC..35C).
This is precisely `TPW.Data.AttractionDefinition.EntranceTile(originX,originZ,rotation)` in the current
data API; it is **not EntranceDoor**, ExitTile, or footprint centre.
Negative record door offsets return a sentinel in 0x80062E48; their downstream geometry is not
ported as a valid door. StaffRideNavigation requires an established entrance from its world.

READ: path target is the tile **centre**: `(tileX<<8)|0x80, (tileY<<8)+0x80`, flags **(0x11,0)**
(0x80096EF8..28 / 0x80096FCC..FFC). Only accepted requests write the job purpose and push state 11
in the original. Claim selection and the class state machine remain in the existing Mechanic port.

### Leave point: queue tail, not the exit door (READ)

0x80097044 asks attraction **slot 26**, all the same ride vtables → **0x8009D508**. This calls
slot 32 → 0x8009F658 (`A+0x70`, queue-path array), then 0x8009FAA8 returns
`queue + 2*count + 2 = &entries[count-1]`. 0x8009D538..54 reads x/y as **unsigned bytes**.
0x8009708C..BC paths to that tile centre with **(0x11,0)**, purpose 22 on success. Unlike job
departure, this handler does not explicitly free old waypoints first. The helper preserves that.

⚠ READ, DO NOT FIX: count **zero** is not checked. The address becomes queue+2, the upper two
bytes of the zero count word; both coordinates read **0**, so the requested point is **(128,128)**.
A host that substitutes the ride exit or refuses an empty queue changes the original rule.
The leave request returning false makes no state change inside 0x80097044; Mechanic.LeaveRide's
current fallback to Idle is a port discrepancy, left untouched and documented here.

## 5. Port boundary and verification

StaffHiring exposes allocation/employment, record skill, placement validity, held state, money and
release through IStaffHireWorld. It preserves the earlier findings' 100/0 and path-only placement
contract. The world positions the held employee; no camera/gate/snap number is invented.
StaffMotion supplies virtual speed and the fixed step; StaffAppearance supplies resource, nullable
sheet location and frame lookup; StaffRideNavigation supplies job/leave path requests through its
world. Existing Mechanic/Guard handlers and game rendering remain unchanged.

The baseline command `dotnet test tests/TPW.Sim.Tests/` passed **1,118 tests** in this worktree.
Every added test names the incorrect implementation it rejects. The restoring mutation runner is
`tools/mutate_staff.py`; results, failing test names, source hashes and final full-suite verification
are recorded in `staff-mutations.json`. No runtime hire or screenshot is claimed as a new observation.

Verification result: **1,165 tests pass** after restoration; **113/113 compiled mutations killed**,
no survivors and no invalid mutations. All four production files match their pre-sweep SHA-256
hashes. SIGINT restoration was also exercised before the final sweep; its matching hashes are
retained under `interruption_checks` in the JSON. Mechanic.cs was neither edited nor mutated.

### Reproducing the decisive reads

Run in this worktree; the repository's fable scripts read the supplied TPW.BIN without modifying it.
`fn.py`'s extent heuristic can miss a leading instruction at a leaf entry; `ann.py START END` is the
check for that case (notably slot 26 starts at 0x8009D508, before fn.py's 0x8009D50C prologue).
The annotated output omits nops: an address gap is **not** permission to move the following
instruction into a branch delay slot.

```sh
python3 findings/fable-scripts/fn.py 800941C0 80096408 800972BC 80098A68
python3 findings/fable-scripts/vt.py 800E43A4 800E41D4 800E4A68 800E4750 800E4C74 -n46
python3 findings/fable-scripts/fn.py 80096E9C 80096F70 80097044 8009D240
python3 findings/fable-scripts/ann.py 8009D508 8009D57C
python3 findings/fable-scripts/fn.py 80031680 800323B0 8002BB30 8003377C
```

READ: the disc entry table is `8 + entry*8`, `(offset,size)` u32 pairs. Direct archive slices were
compared byte-for-byte with the extracted files used for §3. Uniformed resources 263/264/266/274
are identical 332-byte animation tables (SHA-256
`2a650361911d8c40d23779c98c7c6e0db4fd4675defa8e647aa3e78326048590`). The separate entertainer
resources occupy these archive ranges; these are asset identities, not guesses based on costume art:

| Entry | FOLIO.GAZ offset | Bytes | Walk table offset inside entry |
|---|---|---|---|
| 401 | 0x763800 | 15340 | 0x3B6C |
| 402 | 0x767800 | 8256 | 0x1FC0 |
| 403 | 0x76A000 | 11340 | 0x2BCC |
| 404 | 0x76D000 | 14036 | 0x3654 |

## 6. The guard chain, end to end (2026-09-21)

**It fires now. Measured:** `pelt -> shock -> dispatch -> chase -> mid-walk catch`, in one run, with
the log reading `staff #2 Guard walkstep: PathReady -> 39, purpose 8` and the park reporting
`1 caught`. It had never once completed before today. Four separate breaks were stacked on it, and
each one hid the next, which is why "the guard never chases" survived so long as a single symptom.

### 6.1 What was broken

1. **`ParkEntertainerWorld.FreeWaypoints` and `.SetAnimation` threw.** The class note said
   "everything the entertainer never calls THROWS"; the premise was wrong. `Guard.Dispatch`
   (0x80098494) opens with exactly those two calls, on the GUARD, through the world the ENTERTAINER
   was handed. Every dispatch raised before `Shock` could `PopState`, so the entertainer stayed in
   state 32 for the rest of the park's life and re-dispatched every tick: one run read
   **1769 offers, 0 busy, 0 too far, 0 chasing**. The exception was in the log **7,076 times** and
   my own grep filtered it out.
2. **`FreeWaypoints`/`SetAnimation` no-opped even once wired**, because both tested
   `ReferenceEquals(Current.S, staff)` and `Current` is whoever's tick is running — the entertainer,
   not the guard. Every `IGuardWorld` member takes its `StaffMember` explicitly; that is the argument
   to trust. They resolve the host's Staffer by member now.
3. **A leftover waypoint chain pre-empted the class switch.** The host's walk branch tested only
   `WaypointHead != NoChain`, and a chain OUTLIVES a state change — nothing clears it when a class
   PUSHES a state on top. An entertainer pelted mid-stroll was stepped along its old chain and
   `continue`d, so `Entertainer.Tick` never ran at all. ⚠ The gate is on the states that actually
   walk, **2 (`WalkToDestination`) and 3 (`PathReady`)** — `Walking = 11` is the REQUEST, and gating
   on it froze every staff member the moment its path arrived. The obvious name was the wrong state.
4. **A stale path answer overwrote the chase the tick it started.** The port DEFERS path messages:
   the pathfinder calls `OnPathMessage`, which only parks the result on `Answer`, and the staff loop
   consumes it at the top of that member's next iteration. The original has no such gap — delivery is
   a direct virtual call, `person->vtable[0x144](person+adjust, &msg)` (pathfinder.md). So a message
   the original would have processed BEFORE `Dispatch` ran arrives after it here, and the shared
   handler sets the state from the purpose byte alone — wiping state 33. The guard's `FreeWaypoints`
   door drops the queued answer, which is what `Dispatch` calling `FreeWaypoints` first is FOR.
5. **`Guard.WalkStep` (0x80097A1C) had no caller.** It is the state-3 override that checks, on every
   step, whether the guard is standing on the culprit's tile. Without it a chase is a walk to a tile
   the guest has already left, repeated — which is precisely why the chase ran and nobody was ever
   caught. Wiring it produced the first catch.

### 6.2 READ, and ⚠ DO NOT FIX: the path-message handler ignores the state

`StaffBase.OnPathMessage`, slot 40 at **0x800942D8**, branches on the purpose byte and nothing else:
`0x80094304..0x80094358` reads purpose at P+0x2C and sets state **0** for purpose 5, **5** for
purpose 1, **13** otherwise. There is no test of the current state anywhere in it. So an entertainer
with a walk in flight genuinely loses a shock when its answer lands, and that is the game's
behaviour, not the port's. What was wrong was the harness reporting "pelted" for something that then
vanished; `--park-pelt` now refuses while the entertainer is mid-walk and retries, printing the frame
it actually landed on.

⚠ NOT ESTABLISHED: whether the entertainer's class vtable overrides slot 40. The base handler's
content is READ; the claim that the entertainer uses it is inherited from the existing port, not
re-verified here.

### 6.3 The throw-out, and a value that was a side effect of a diagnostic

The catch left the guard in state **39** (`ToExitPoint`) and dropped it to `Idle` on the next tick.
The tell was the PURPOSE in the log: still **8** (`ToCulprit`), so `Guard.GoToExitPoint` had returned
at its first line — `if (!world.HasExits)` — before it could set its own purpose. `HasExits` is
`GateTile.X >= 0`, and **`GateTile` was only ever assigned as a side effect of `GateArea()`**, which
is the reachability DIAGNOSTIC. A park with no attractions never runs that readout, so the tile
stayed `(-1,-1)` and the guard threw nobody out. Resolving it through `GateArea()` at the point of
use — one cached flood fill — makes it depend on the map instead of on whether a readout happened
to run first. After: `39 -> Walking, purpose 14` (`ExitPoint`), the ejection walk under way.

### 6.4 The guard's arrival table was dead, which was the whole gate machine

`Guard.Arrive`'s purpose switch is the gate: **8** (`ToCulprit`) returns to Chase so a chase that
reaches its tile carries on; **14** (`ExitPoint`) starts the ejection and bumps the gate counter;
**Gate** sends the guard to its POST or out of the park; **Post** is the only thing that returns a
posted guard to Idle. The host's `OnArrive` handled the mechanic and the cleaner specially and gave
**everything else to `StaffBase.Arrive`**, so none of it was reachable. That is the same bug as the
cleaner's, one class further on, and it is why the report had read `0 on post` since the guard was
wired and why the ejection walk ended in an ordinary patrol leg.

⭐ Routing guards through `Guard.Arrive` is safe where routing cleaners through `Handyman.Arrive` was
not: the guard's switch ends in `default: StaffBase.Arrive(...)`, so an ordinary patrol leg still
gets the shared answer. That fall-through is the whole difference between the two classes.

**Measured after, one run, one guard's lines:**
```
staff #2 Guard walkstep: PathReady -> 39, purpose 8      caught
staff #2 Guard: 39 -> Walking, purpose 14                 walking them to the exit
staff #2 Guard: 46 -> PathReady, purpose 16, counter 1    at the gate
staff #2 Guard: 48 -> Walking, purpose 9                  out to a random exit
staff #2 Guard: 59 -> Walking, purpose 15                 back to the spawn point
staff #2 Guard: 46 -> PathReady, purpose 16, counter 1    at the gate again
staff #2 Guard: 55 -> Walking, purpose 21                 TAKING A POST
```

### 6.5 "0 on post" was my instrument asking for a state the game does not have

The post leg works. Measured with counters that split the four causes apart:
`post: 1 taken of 1 tiles tried, 0 gave up after five, 0 with no gate` — the first sample was
accepted, and the guard's own lines read:

```
staff #1 Guard: 46 -> PathReady, purpose 16, counter 1     at the gate
staff #1 Guard: 55 -> Walking, purpose 21                  walking to the post
staff #1 Guard: Idle -> Patrolling, purpose 21             ARRIVED
```

⭐ **And arriving at a post means going Idle.** `Guard.Arrive`'s `case GuardStates.Post` is
`SetState(StaffState.Idle)`, so the base machine takes the guard Idle → Patrolling → RandomWander on
the next two ticks. **There is no standing-at-a-post state in this game.** "Take a post" is a walk to
a tile near the gate and then back to ordinary patrol; the report's `N on post`, which counted
`State == TakePost || Purpose == Post`, was sampling a transient that is two ticks wide. The honest
measure is how many posts have been TAKEN, which is what the line prints now.

⚠ Note which guard took it: **#1, not #2** — the one that caught the culprit ejected it, and a
different guard reached `TakePost` through the spawn-point route. Both paths into the arrival table
fire; they are not the same guard's story, and an unnumbered staff log cannot tell them apart, which
is why the log numbers them.

### 6.6 Why a chase often ends without a catch: the chase cannot use the gate apron

`Guard.ChasePathFlags` is **0x11** = `Path | Queue` (READ, pathfinder.md's call-site table), while
the guests' own `WalkFlags` is **0x31** = `Path | Queue | GateSide`. So a guard **cannot step on a
type-14 tile beside the gate** and a guest standing on one, or reachable only across one, cannot be
chased. In a small test park most of the path network runs past the gate, so this fires often; in a
big park it would be rare. It is the binary's own flag set, so it is ⚠ NOT to be fixed.

**Measured, with the counters that separate the three outcomes:**
`chase paths 1 asked, 0 refused at entry, 0 with no guest` — the request was ACCEPTED, so this is not
the ten-slot refusal that produces no message at all; the search ran and found nothing.

**And the state log needed two more fields before it could say that.** The transition reads
`Walking -> RandomWander, purpose 8, waiting False, answer none`, which looks like a request that was
never answered. It is not: the answer arrived and was consumed at the top of that same iteration,
`StaffBase.OnPathMessage` sent purpose 8 to **Patrolling**, and `StaffBase.Patrol` on a guard with no
patrol rectangle turned that into **RandomWander** — three state changes inside one tick, of which
the log could only ever show the first and the last. `waiting` and `answer` are printed now because
"accepted and never replied to" and "replied to with a failure" are different bugs that produce the
same two-state line.

⚠ **A note on the preflight, which is consistent and worth writing down rather than fixing.** The
area map is built with `WalkFlags` — a SUPERSET of the chase's flags — so it can answer "same piece"
for a route the chase itself cannot walk. That is harmless only because the preflight exists to
REFUSE: a false "same area" means the search runs and fails, which is what already happens. A
preflight that ever starts *allowing* things on that answer would be wrong.

### 6.7 What is still not proved

- Whether the ejected guest actually ends up outside the park. The guard's half of the ejection is
  now watched end to end; the GUEST's half — message 4, `VisitorMessages.OnMessage` — has not been
  followed to a guest leaving.
- `0 on post`: `TakePost` has still never been seen to place a guard at the gate.
- The measurement needs a **connected** park. On a map in two pieces every guest is stranded and the
  chase's path request is refused at issue, with no message, leaving the guard in state 11 until the
  base machine wanders it away. The park report's `map in N connected pieces` line is the check.

### 6.8 The pattern, audited rather than the instance fixed

`FreeWaypoints`/`SetAnimation` no-opping on a foreign member is a SHAPE, not one bug, so I swept every
`ReferenceEquals(Current.S, staff)` guard in the game project: **nine, across `ParkGuard.cs` (six) and
`ParkLitter.cs` (three)**. For each I checked whether the sim ever reaches it from a DIFFERENT member's
tick. Only one place in the whole sim does that — `Entertainer.Shock` calling `chosen.Dispatch(...)` —
and it touches exactly the two that were wrong. The other seven are only ever reached from their own
member's handler, so they stay as they are.

Two near misses worth recording because they look like the same bug and are not:
`Mechanic.HandJobOn` hands a ride to a COLLEAGUE, but through `world.TryClaimRideFor(staff, candidate,
...)` in a world with no `Current` guard; and `VisitorActivity.Watch` reads
`world.Position(entertainer)` from a GUEST's tick, but `ParkActivityWorld.Position(StaffMember)` is a
direct lookup. Both cross member boundaries and both are already correct.
