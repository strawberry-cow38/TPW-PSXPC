# Theme Park World (PSX, PAL, SLES-026.88) — RIDE PANEL

READ = instructions in TPW.BIN loaded at 0x80010000; GUESS = interpretation with confidence.
Image SHA-256: `0b708dd74796abfedad55e55fa007ace1b5697407d36e8f7823bb06f4fa53beb`.
A is the attraction subobject, outer+8. A vtable entry is a signed adjustment and a pointer,
8 bytes per slot. Confusing a vtable offset, save offset and object offset gives believable errors.

## SOURCE DISAGREEMENTS

These are submitted for review, not silently substituted into older ports. The older findings
remain intact. Where a new implementation meets a disputed claim, it retains the findings' rule
and names the disagreement. Corrections already accepted in the findings' own preambles take
precedence over their older body text.

| Existing source | Binary READ | Treatment in code |
|---|---|---|
| economy.md §4.7: a dormant ride ticket word at +0x88 is saved/restored | 0x8009CE30..38 copies **A+0xF4** to **save+0x88**; 0x8009CFC8..FD0 restores it. A+0xF4 is placement day (0x8009C3F4..400), consumed as age by 0x8009EDBC. A+0x88 is inside the queue-path storage A+0x70..B3 (copy 0x8009F660). | Keep the report's **no tickets / no ride money flow** rule. No ticket field or setter is invented; no pre-existing save implementation changed. |
| rides.md §5: tour/track/coaster use the same intensity shape | Coaster 0x800B09D0..0x800B0A48 uses duration<<12 **without /5**; flat/tour/track divide by five. With base 90, speed 50, duration 1, binary coaster intensity is 67, common report formula gives 50. | RidePanel.Intensity deliberately keeps the report's common /5 formula for coasters. |
| rides.md §5: intensity base is record+0x18; track described as same shape | Track adds `u8(outer+0x195)>>1` to the base at 0x800A8AA8..AB8 before multiplying factors. | Keep record base in RidePanel.Intensity; do not silently add this bonus. 0x800A727C computes +0x195 as a wrapping byte sum of per-route-piece weights 1/2/4 (0x800A72A8..730C); which piece shapes receive which weights remains unlabelled here. |
| behaviour.md §3.5: service-due jobs, and CloseRide waits for riders to leave | 0x8005BE44 selects the **upgrade queue**; state 54 calls the paid 0x8009C56C at 0x80096AA4. 0x8009C1E8 tests A+0xEC against `10*(w+h)<<12` (0x8009C260..288); 0x8009EEE4/EFA8 raise/lower that progress. It does not count riders. rides.md §0 item 3 already records this disagreement. | Leave Mechanic's existing world contract/state machine intact. CompleteService must be mapped to CompleteUpgrade by the host; closing progress and rider emptiness are explicitly separate here. |
| behaviour.md §3.5 / Mechanic: both coin-flip orders try both kinds of job | Service-first failure calls **0x8005BE44 again**, with repair flag 1, at 0x80096E4C..E64; repair-first uses 0x8005BCB0 then 0x8005BE44. | Existing Mechanic.Idle's report-based fallback is unchanged. No second idle scheduler added. |

Range scope clarification (READ): rides.md §2 records the level block's min/max words;
it does not establish the widget's clamp. Those level words remain unchanged in ReadLevel.
The newly traced panel bounds combine all three levels (0x80078FD0..0x80079154), so they
are a separate result in Ranges, not a replacement for the definition bounds.

Confirmed, not a disagreement: rides.md §0 item 1 is correct that slot 54 is zero.
This leaves slot 53's preference match and hasK live; it does not erase intensity from the
guest score (0x8008CA00..CB4). The existing RideScore formula is retained unchanged.

The prompt's admission-price slider is also contradicted: READ labels at 0x800791C8/218/268
are text 0x1A1 **Speed**, 0x364 **Capacity**, 0x2D5 **Duration**. There is no third ticket slider.
The current rides.md §4.3 has already replaced the older “sliders do not exist yet” text with the
phase trace. This report supplies the panel side; it does not reinstate that older claim.

Existing code discrepancies: TPW.Data.RideLevel.DefaultCycles uses max(CyclesMin,max/2), whereas
rides.md §0 item 9 explicitly accepts max(1,max>>1). The new initializer follows that accepted
finding. The earlier hardcoded speed 50 is correct for 1..100, but tours' 80..100 initialize at 90.
These are differences from existing implementations, not additional binary/findings disagreements.

## 1. Fields, defaults, ranges and save widths

All entries READ. Level block B = record+0x24+0x34*level. Getters at 0x8009F470..5D0.

| Live word | get / set slots and functions | Placement and upgrade reset (0x8009C410) | Panel bounds |
|---|---|---|---|
| A+0xB8 speed | 89/92, 0x8009ED04/ECFC | speedMin + ((speedMax-speedMin)>>1), 0x8009C498..50C | min of B+0x14 over levels 0..2 to max of B+0x18 |
| A+0xBC capacity | 90/93, 0x8009ED18/ED10 | max(1,virtualMaxSeats>>1), 0x8009C45C..494 | 1..max(B+0x0C over 0..2), except coaster virtual max |
| A+0xC0 duration | 91/94, 0x8009ED2C/ED24 | max(1,cyclesMax>>1), 0x8009C510..550 | min of B+0x1C over 0..2 to max of B+0x20 |

READ: coaster slot 90/93 overrides 0x800B0510/51C use outer+0xC4 = **the same A+0xBC**.
Virtual maximum at 0x800AD728 is `min(u8(record+0xD2),8)*attachmentCount`, with the latter's
zero-to-one fallback in 0x800AD610. It is not the common level seat word. Its decoded geometry
remains supplied by the world; the panel never invents a count.

READ: panel creation 0x80078F70 reads all three blocks independent of research/current level.
0x800423CC sets the minimum, 0x80042378 the maximum. 0x80042330 clamps min first, max second
and stores value<<16; 0x8004236C returns signed high halfword. Input handlers 0x800419D8/41A68
also enforce bounds. 0x80079300 calls them and writes **all three** values every panel update
(0x8007940C..478), without status/reliability tests or resetting any ride clock.
Thus a widget initialized out of range is clamped in its UI copy, and the next update commits
that value even without a directional input. No level clamp exists in the object setters.

READ: capacity is hidden when max seats <2 (0x80079888..98); duration is hidden on coasters
(0x800798D8..79900), although its widget still initializes and writes. The speed control remains.
READ: upgrade/placement defaults do not use widget clamping. Bounce on Iggy has minima 30/30/30,
maxima 45/50/55, and starts at 22/25/27 (rides.md §0 item 9 / ride-phase-lengths.md).
Crazy Ape levels have seats 8/11/14 and defaults 4/5/7; speed 50; duration 5 (rides.md §1.4).

READ: save 0x8009CE0C..2C narrows speed to u16 at save+0x8A, capacity to u8 at +0x8C,
duration to u8 at +0x8D. Restore 0x8009CF58..FA8 zero-extends those fields into raw setters,
without invoking the widget clamp. Level is u8 A+0xF6, save+0x92 (0x8009CE6C/CFEC).
Placement (0x8009C344) first sets level zero, clears effects/lists/riders, derives defaults,
sets lifetime from record+0x34, and stamps total days in A+0xF4. Construction charge belongs
to the placement tool (economy.md §4.6); none is made by this initializer.

## 2. Requesting and completing an upgrade

READ: the panel sets next=current+1 (0x80078D98). It suppresses the Upgrades page when
next>=3 or lifetime==0 (0x80078E70..EEC). The list offers next only when next is **strictly
less** than the available research count returned by 0x8006AC04 (0x80078F1C..F44).
That getter also advances completed research and has the all-unlocked override returning 3
(0x8006AC04..AC98); this port consumes its result through the world.

READ: confirming the first offered row checks mechanic count 0x800537F8 and strike type 2
at 0x80068714 (0x80079AA8..B24). Neither a money comparison nor a reliability/status check
occurs. The normal action 0x8003BE00 calls 0x8005BE14; a 15-pointer queue at 0x801099EC
(count 0x80102D48) uses 0x8005BA8C to deduplicate, returning success for an existing pointer
and failure for a new pointer when full. Queue ownership belongs to the world.
A request costs nothing, changes no level/status, and leaves a current run alone.

READ: mechanics select the nearest unclaimed queued ride (0x8005BAF8). Upgrade claiming
passes flag 0 to 0x80096C58; the broken-status/lifetime checks are **only in its flag-1 repair
arm** (0x80096CA0..CF0). The panel blocks a condemned ride; the completion routine does not.
A ride that becomes condemned after enqueue closes and removes itself from the queue when
closing progress completes (0x8009CD0C..40).

READ: mechanic chain 57→52→54→17→58 is already ported under service names. State 52 advances
closing progress until 0x8009C1E8 succeeds, sets ride status 6, and starts the skill timer
(0x80096900..988); state 54 requires a nonzero timer and now strictly beyond it, then upgrades,
sets mechanic state 17 and removes the queue entry (0x80096A88..AC4). Skill table 0x800E4574
first halfwords 240/180/120/60/60; not new panel constants. See SOURCE DISAGREEMENTS before
interpreting the existing IMechanicWorld.RideIsClear as the binary predicate.

READ: actual purchase 0x8009C56C(A,silent):
```
if level >= 3: return
level++
clear both effect handles
reliability = 0x64000
cyclesRun = 0
closingProgress = 0
capacity = max(1,virtualMaxSeats>>1)
speed = speedMin + ((speedMax-speedMin)>>1)
duration = max(1,cyclesMax>>1)
TrySpend(Money(record[level].pricePounds,0))   # full NEW-level price, return ignored
if !silent: sound(8,level<3 ? 11 : 10); create sparkle
```
READ: price getter 0x8009F470 reads record+0x50+0x34*level. Money conversion at 0x8009C600
makes pounds×10. This is neither a price difference nor a half-price charge; economy.md §4.6
agrees. Normal bank permits debt; failure under a different bank flag does not roll back the
already completed upgrade (0x8009C60C..614). Silent only suppresses sound/sparkle, not payment.

READ: no level-decrement writer exists: placement zero, load saved level, upgrade increment
are the writers found by field/accessor census. Upgrade is irreversible. The panel offers
only two upgrades; the lower-level routine's third upgrade overruns the three valid blocks.
No valid fourth block, price or seats is established. ReadLevel(3) must explicitly supply
observed overread data or fail, never silently reuse level 2 or choose a placeholder.

READ: an upgrade changes subsequent level reads (wear multiplier, virtual common seat maximum,
record slider ranges, queue cap 4*level+7) and resets all sliders. It **does not write lifetime**
A+0x68, riders/lists, placement age A+0xF4, status A+0x6E, or animation accumulator A+0x60.
The normal mechanic workflow already put it under repair; reopening command 7 moves rides to
loading, as previously ported. A direct completion during a cycle would reset its phase count
but preserve fractional animation progress and riders. These distinctions are tested.

READ: the other caller 0x8005C2F4 is invoked at 0x80057FE0 in manager cleanup, not a second
per-tick upgrade scheduler. It flushes the upgrade queue, completing only entries with a claimed
mechanic that 0x8009757C matches to the job; it sends reopen 7, clears the claim, calls the same
upgrade with silent=1 and finally clears queue count. GUESS-high: leaving/changing game mode;
the exact player navigation that reaches this cleanup is not established here.

## 3. What reaches simulation

### 3.1 Intensity: slot 53, never slot 54

READ: flat 0x800A0594 and tour 0x800A202C compute the following, with signed 32-bit operations
and low-word multiplication followed by arithmetic shifts:
```
s = clamp((speed<<12)/100, 0xC00, 0x1400)
d = clamp((duration<<12)/5, 0xC00, 0x1400)
f = (s*d)>>12
intensity = min(100,(recordBase*f)>>12)
```
No final lower clamp exists. Factors truncate separately and again before multiplying the base.
At speed 50, duration 5, base 60 gives 45; at 100/5 it gives 60; at 100/7 it gives 75.
Duration 1 lowers the common formula's factor to 0.75. Speed 1..74 is flattened by its floor.
Track and coaster variations are retained as disagreements above, not adopted in code.

READ: slot 54 returns 0 for **every actual ride** (0x8009C2A0, vtables 0x800E5514/586C/5FE8/65A0).
Slots 55/56 also return zero. Candidate wiring must not put intensity in any of those slots.
The guest score 0x8008C818 still uses slot 53 for:
- `match=2*(50-min(abs(preference-intensity),50))` (0x8008CA00..A98);
- `hasK=(intensity!=0)` (0x8008CAAC..CB4), the match weight **and** its divisor contribution.
The ride-desire and nausea-desire terms gated by slot 54 stay zero, regardless of slider values.

READ consumer census of attraction slot 53:
- 0x8008C818, ride choice above;
- 0x800906EC, queue boredom mismatch (calls at 0x80090728/758/77C), as in behaviour.md §0 item 6;
- 0x8008F110 ride unload arm, getter at 0x8008F3F4: happiness mismatch bands +15/+10/+5,
  nausea when intensity>=56, boredom reduction by intensity. No ride price debit or sale call;
- 0x80090D5C, gate fee/value verdict sums attraction intensities (0x80090D94);
- 0x80067400, attraction contribution to arrivals uses level*intensity (0x800674D8/F0).
  arrivals.md's recorded young-ride disagreement remains unchanged in BusArrivals;
- UI reads at 0x80076838, 0x800770A8, 0x80079568, 0x8007B3A8. The last also serves other
  attraction types. Other same-offset hits in staff functions are different vtables, not ride readers.

### 3.2 Speed, capacity and duration: direct consumers

READ census: fieldx.py for 0xB8/BC/C0 and outer-adjusted offsets, accessor callers, and virtual
slot loads at 0x2C8/2D0/2D8. This excludes matches where the base is a visitor, vtable or save.

| Setting | READ consumers beyond panel/save/defaults |
|---|---|
| speed | wear terms in flat 0x800A0250, tour 0x800A1CA4, track 0x800A8690, coaster 0x800B05D0; all four intensity functions; tour vehicle speed initialization 0x800A35B8; track vehicle initialization 0x800ABD8C; coaster velocity clamp each movement call 0x800B1F84 |
| capacity | flat loading 0x8009C884; tour dispatch/loading 0x800A11E8; track loading 0x800A67F4; coaster boarding 0x800B025C and 0x800B0DC8; the four wear functions when called with their prediction flag, using capacity rather than actual riders |
| duration | flat phase count 0x8009CA60 (read 0x8009CB1C); four intensity functions; projected reliability slot 88 (0x8009ED38, coaster 0x800B0524); tour destination-arrival count 0x800A2A4C (0x800A2BB8); track 2*duration running counter 0x800A6960 and laps in 0x800AA704 / 0x800AC03C; track vehicle HUD 0x800AB1C8 (0x800ABAF0/BB38/BB80); coaster train return laps 0x800B1F84 (0x800B22A0) |

READ: the flat animation timer 0x800658D8 has **no speed read**. Speed changes wear/intensity,
not flat phase time; duration changes the number of phases. Changing duration takes effect on
the next comparison, with the existing phase/count retained. The tour speed setter is called on entering moving states; track speed is sampled at
vehicle initialization. Neither is retroactively applied by the panel setter. Coaster velocity
uses speed live each movement call. Trip/lap behavior is already ported in TourRide/PathedRide/
RollerCoaster; the panel supplies their duration and capacity inputs without replacing those clocks.

READ numeric speed effects:
- Tour 0x800A3608..660: `vehicle.s16(+0x28)=max(2,s16(baseSpeed+clamp(speed-100,-50,50)/10))`.
  READ: the moving-state caller derives base from the low byte of record+0xD0, with
  state-dependent fractions (ride-classes.md §2, 0x800A33D8..3544 / 0x800A1744).
  There is no single universal base constant; the port takes this caller result as input.
- Track 0x800ABDE8..E08: `vehicle.u8(+0x1D)=speed/20` at initialization. Its other movement state
  and geometry remain with the vehicle world.
- Coaster 0x800B2094..20EC: `velocity=max(minimum,min(velocity,maximumAt100*speed/100))`.
  Minimum and maximum are gp 0x80103384/388; the multiplication is low signed 32-bit. Minimum
  wins even when greater than the scaled maximum. Port takes the world's coefficients explicitly.

READ: projected reliability slot 88 requests wear with flag=1, multiplies it by **9*duration**,
then returns `100-min(100,product>>15)` for common rides (0x8009ED50..DB0), but **>>12** for
coasters (0x800B0540..5C4). UI 0x80079584 calls it; it is not intensity or a ticket price.
The host supplies the wear prediction, whose existing shared precision disagreements are recorded
in rides.md §0 items 8/14 and ride-classes.md §5; this port does not replace wear arithmetic.

READ clarification: the tour speed input above is resampled each time its state-entry code
calls the setter; it is not a once-per-ride cache. Track debug-display reads of duration do not
constitute another timer. Non-ride field-offset hits in the track-piece functions 0x800A4374..6074
are their own object layouts.

### 3.3 Admission price

READ: none on rides. economy.md §4.7 and findings/README.md's live result agree. Guest state 22
ride arm 0x8008F3E4..4F8 changes reward/served count; no guest-Money subtraction and no bank call.
0x8009C56C is the **upgrade purchase** bank call, not ride income. The park gate admission slider
is BANK+0, 0..1000 pounds, default £40 before scenario override (economy.md §4.1/6.3), and is not
an attraction setting. Shop A+0x88 is also a different object's layout. No guessed admission
constant, setter or receipt is added to RidePanel.

## 4. Port and verification

IRidePanelWorld maps the actual live fields to the already ported wear, loading, status and guest
worlds. IRideUpgradeWorld supplies research, mechanic availability, queue and bank operations.
RidePanel ports defaults, widget bounds/application, save narrowing/restoration, upgrade offer /
request / completion, level wear multiplier and slot-53 candidate wiring. RideSliderEffects ports
the additional scalar consumers. The existing mechanic workflow is retained, with its disputed
world predicate documented above; geometry, pathfinding and sound/effect ownership remain host-side.
This deliverable is a sim API, not a rendered Godot panel or an engine adapter. Tests share
one world across the panel and existing wear, loading, lifecycle and guest reward code; a
mechanic adapter maps CompleteService to CompleteUpgrade and exercises the timed job through
reopening. No existing gameplay rule is silently replaced.

Verification result: **1,076 tests passed**, including **80 new cases** and all **996**
pre-existing cases. **115/115 compiled behavioral mutations killed; zero survivors, zero invalid
mutants.** The full suite passed again after restoration; source SHA-256 matches the original.

Every new test states its rejected alternative. The mutation runner in tools/mutate_ride_panel.py
uses a top-level try/finally, restores after each mutant, handles SIGINT/SIGTERM/SIGHUP, and writes
incremental results to ride-panel-mutations.json. No process can execute finally after SIGKILL or
power loss: a durable original-file backup and startup recovery cover that interruption case.
The result file records actual test counts, killed mutants and the restored-source hash.
A deliberate SIGTERM during a mutant test was caught and restored the original SHA-256
`71bd0b5563384fb41277a1ab0f36dc7252007a4ba497785169fb98fb89b48aff`; both backup and journal
were removed. This interruption and the resumed run are preserved in the JSON history.

Reproduction (in this worktree):
```sh
python3 findings/fable-scripts/ann.py 80078d14 8007949c
python3 findings/fable-scripts/ann.py 8009c344 8009c690
python3 findings/fable-scripts/ann.py 800a0594 800a0750
python3 findings/fable-scripts/fieldx.py 0x2c8 0x2d0 0x2d8
dotnet test tests/TPW.Sim.Tests/
python3 tools/mutate_ride_panel.py
```
`--resume` rechecks the full baseline, retains prior kills on identical production source, and
reruns unresolved/unattempted mutants. `--recover` only restores an interrupted durable backup,
and refuses to overwrite unrelated source edits.
