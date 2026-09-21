# Coaster control points and moving trains — PAL SLES-026.88

2026-09-21. **READ** = instructions or data read from the supplied disc; **GUESS** is
explicitly qualified. Addresses and offsets below are hexadecimal unless stated otherwise.
Attraction offsets are **outer-relative**, not `outer+8`-relative. Piece and train offsets
are relative to their own objects. There are no live PSX timing measurements in this report.

## §0 SOURCE DISAGREEMENTS

| Existing source / contract | Binary reading and addresses | Version retained in this port |
|---|---|---|
| `save.md` §3.2 calls saved piece `+2` “UNKNOWN copied position padding”. | The position getter `0x800B31FC..3250` copies runtime `+0x26` into this halfword. `0x800B3304..3334` writes it as the vertical coordinate; `0x800B35E8..365C` adds the support height to it. This is vertical position, not unused vector padding. The unused fourth halfword of that eight-byte vector is overwritten by the saved check word. | **Keep `UnknownPositionPadding`, opaque and lossless in the codec.** Do not silently turn old save data into terrain heights. `ICoasterTrackWorld` explicitly supplies `BaseHeight`. Two of fourteen saved bytes remain semantically unnamed in the public codec under this retention rule, despite the new binary evidence. |
| `save.md` §3.2 assigns the connection boolean to record `0x95` bit 6 and says bit 7 is cleared. | This is correct for the writer (`0x800ADBA4..DBCC`), but the loader accepts **either** bit: `andi 0xC000` on the word at `0x94`, `0x800ADE00..DE10`. | Restore accepts **bit 6 only**, as the report's contract specifies. A bit-7-only synthetic record is the rejecting control. This is an additional loader edge, not a claim that the writer sets bit 7. |
| The old `rides.md`/`TrackRun` material associated “32 cars” with the coaster pieces and reused track-ride construction assumptions. | Already settled by `ride-classes.md` §4: **32 route-object slots of 0x40**, constructed at `0x800B1AAC..AC8`; **eight train objects of 0x88**, constructed at `0x800B1B1C..38`. Coaster closure is `+0x104`, written at `0x800ADEF4`, not track ride `+0x18A`. | Follow the user's explicitly supplied controller report: eight trains; separate coaster route code. Existing `TrackRun` and `game/` are untouched and are not adapters for this module. This is inherited evidence, not a newly discovered disagreement. |

`AfterMovement(bool onReturnSegment, int segmentFraction, int duration)` **agrees** with
the instructions when its boolean uses the current segment **after** the possible handoff
and its fraction uses `s2` **before** the handoff. Its signature and body are unchanged.
The source disagrees with neither the strict midpoint nor the absence of a return-edge latch.

There is no §0 in this checkout's `ride-classes.md`; its §4 and §6 disagreement list were
read. That report remains the authority for dispatch/status behavior. Its known dispatch,
eight-train distinction, independent completion, and twelve definitions' duration limits are
the starting point here, not reclassified as new findings.

## 1. Sources, counts, and reproduction

- `TPW.BIN`, loaded at `0x80010000`, SHA-256
  `0b708dd74796abfedad55e55fa007ace1b5697407d36e8f7823bb06f4fa53beb`.
- `FOLIO.GAZ`, SHA-256
  `5666a0eeb18998a31fafbe845e7490cf65da76e722a69aec8b30ff2ee1a3c951`.
- Prior reports: `ride-classes.md` §4/§6, `save.md` §3.2, `paths.md` §10.2,
  and `rider-positions.md` §3.2/§4/§7.
- `tools/coaster_source.py` reads **one archive**, never both `NNNN.bin` and `eNNNNN.bin`.
  Its census is **244 definition containers, 12 coasters**. Its JSON records the twelve
  definition addresses and newly read launch-speed fields.

The save report uses **hexadecimal sizes**: record `0x418` = **1048 decimal bytes**;
reservation `0x380` = **896 decimal bytes** = **64 slots × 14 bytes**. The count is a separate
six-bit value, with at most 63 representable, and the runtime builder has a **32-slot** pool.
No real saved park was sampled here: **zero observed persisted track populations**. The
64-slot reservation, 32-slot object pool, and eight trains are three different counts.

Reproduce the binary/data audit and important disassemblies:

```sh
python3 tools/coaster_source.py
python3 findings/fable-scripts/fn.py 8001F038 8001F118 8001FF30 800AF73C 800AFA38
python3 findings/fable-scripts/fn.py 800ADB20 800ADCD8 800B3B7C 800B0C94 800B1F84 800B22FC
dotnet test tests/TPW.Sim.Tests/
python3 tools/mutate_coaster_track.py
```

The function script's prologue search can include adjoining leaf functions. For example,
the leaf at `0x800BF218` and the entry at `0x800BF2A8` need those exact instruction
boundaries; do not interpret a guessed prologue extent as another function's body.

## 2. The fourteen bytes

Writer `0x800ADB20..DCD4`, loader `0x800ADCD8..DE34`; table starts at record `0x96` and
advances by `0x0E` at `0x800ADC84..90`. Only `outer+0x108` populated pieces are written.
Runtime restore masks the count at `0x800ADD14..24` and rebuilds the pieces through
`0x800AF73C`, rather than restoring pointers or train states.

| Slot bytes | Width | Runtime field/getter | Meaning established from builder and consumer |
|---|---:|---|---|
| `+0..1` | s16 | piece `+0x24`, position slot/getter `0x800B31FC` | Map X. Runtime builder shifts it by eight (`0x800AF81C..834`) and the setter adds `0x80` (`0x800B331C..32C`): tile-center world X. |
| `+2..3` | raw u16 in codec | piece `+0x26`, `0x800B31FC/0x800B3304` | **Retained UNKNOWN position padding**, disagreement §0. Binary evidence says base vertical position; not silently adopted as a save-field name. |
| `+4..5` | s16 | piece `+0x28`, `0x800B31FC` | Second map axis, called Y in `save.md`, world Z here. Also shifted by eight and centered (`0x800AF820..834`, `0x800B3320..334`). |
| `+6..7` | saved low halfword | piece word `+0x3C`, getter `0x800B671C`, setter `0x800B6728` | Nonzero validation result. Fresh append sets 1 at `0x800AF880..88C`; editor refresh writes validator `0x800AECFC`'s result at `0x800B2EEC..F00`. Restore turns **any** nonzero halfword into 1 (`0x800ADDD4..E0`). |
| `+8..9` | s16 | signed-extended word `+0x2C`, getter `0x800B3400`, setter `0x800B67E8` | User height/support extension above the base. Builder passes editor `+0x28` at `0x8001F1CC..1D4`; support-top calculation adds it at `0x800B3424..3438`. It is not a direction. |
| `+A..B` | s16 | piece `+0x34`, getter `0x800B67AC`, setter `0x800B67B8` | Banking angle. Builder uses editor `+0x2C` (`0x8001F1B8..1C4`); runtime interpolates previous/current banks at `0x800B24C8..4FC`, then rotates about the longitudinal axis through `RotMatrixZ(-bank)` at `0x800BF820..828`. Angle scale is 4096/revolution. |
| `+C` | u8 | piece `+0x38`, getter `0x800B6744`, setter `0x800B6750` | Shape/motion mode. **1** selects the structured special shape and bypasses ordinary friction/gravity/approach braking (`0x800B1FDC..2058`). **0** is the ordinary editor mode; rendering also distinguishes **2** (`0x800B2A0C..A98`). This is not a universal mesh index or cardinal direction. |
| `+D` | u8 | piece `+0x39`, getter `0x800B6730`, setter `0x800B673C` | Phase/role within the special shape. Editor initializes 1 (`0x80020DF4..E14`); increments and skips interactive phase 2 (`0x80020100..128`), because `0x800AFA38` inserts an additional phase-2 point after phase 1 (`0x800AFAC8..FB38`). Roles 1 and 3 alter spline control positions (`0x800B36E0..37AC`). |

Thus **12/14 bytes have retained semantic names; 2/14 remain opaque** under §0. There
is no persisted next/previous pointer, direction angle, segment length, train count, or lap
count in these fourteen bytes. The original save's final record bytes `0x416/0x417` remain
UNKNOWN and are not accessed by the new piece reader.

Special shape height offsets are the **five** signed words at `0x800DC764`:
`0, 512, 1024, 512, 0`; `0x8001FFF0..20008` adds the selected value to the user's height
and clears banking. **GUESS-high:** this shape is a vertical loop. The role sequence and
height offsets are READ; the player-facing name is not needed or assumed in code.
Primitive `Append` corresponds to `0x800AF73C`, not the complete interactive special-shape
macro at `0x800AFA38`. Persisted/generated role-2 slots must be supplied as populated pieces.

### Connectivity and validity

`paths.md` §10.2's `0x8001F038` is the tool-11 handover: it requests tool **12** at
`0x8001F0C8..0E0`. Tool 12's point configuration routine `0x8001F118` writes the two mode
bytes, position, bank and height through the accessors above. Confirm `0x8001FF30` sends
ordinary points to `0x800AF73C` at `0x8002007C` and special additions to `0x800AFA38`.

The runtime object holds previous at `+0x14` (`0x800B677C`) and next at `+0x18`
(`0x800B6770`). It also has separate same-footprint/stack links at `+0x1C/+0x20`
(`0x800B6764/0x800B6758`), linked at `0x800AF990..9E4`; these are not route connectivity.
The initial station chain is:

```
Approach (outer+0x10C) → Launch/return (outer+0x14C) → populated pieces in append order
```

`0x800AFCAC..CBC` and `0x800AFD68..D7C` create the first station link. Append creates
last→new and new→last (`0x800AF920..958`). Closure compares map X/Z only, ignoring height
(`0x800ADE68..8C`), requires an existing piece, and links last→Approach→Launch
(`0x800ADEA0..EF4`). It does **not** allocate the proposed closing point. A later append
clears the connection flag (`0x800AFA04`).

**⚠ DO NOT FIX:** `0x800AF76C..778` checks the **32-slot limit before closure**. A full
32-slot builder refuses even the closing point. The loader can restore a full 32-slot
connected track by restoring the separate flag afterward. Managed restore rejects counts
above 32 rather than reproducing the source's null dereference beyond the object pool.

`0x800B0BE8..C90` separately aggregates nonzero validation words from **n used pieces plus
two station pieces**. It does not validate unused slots and does not equate usable geometry
with a closed route. Scene collision and terrain validation inside `0x800AECFC` are host-side.

### Length and spline geometry

`0x800B3B7C` computes **current support top minus previous support top**, including all three
axes (`0x800B3C40..C90`); it calls SDK `SquareRoot0` and stores the narrowed signed result
at `+0x36` (`0x800B3C94..CA0`). Getter `0x800B6798` sign-extends it. No spline arc-length
integration occurs. Missing previous means a duplicated endpoint, hence zero chord length.
`0x800B3CAC..3DF8` additionally derives pitch and yaw; neither angle is persisted.

The train uses four spline controls: previous.previous, previous, current, next, with missing
neighbours duplicated (`0x800B2340..2468`). For Approach→Launch it duplicates both station
ends (`0x800B2390..3A4`). `0x800BF218` transposes those four points for `0x800BF2A8`.
The latter is a cubic cardinal spline with signed tension **−2048** at `0x80103558`, not
a guessed default Catmull–Rom spline. It calculates both position and a normalized derivative
(`0x800BF44C..570`). `CoasterMath` preserves the low-word products, logical shifts, halfword
coefficients and separate rounding of the fourth control's contribution.

The **192-entry** square-root table starts at `0x801018D0`, used at `0x800CD8D0..8EC`.
The **192-entry** reciprocal-root table starts at `0x800F97B4`, used by `VectorNormal`
at `0x800C9214..925C`. Example rejecting a plausible substitute: `SquareRoot0(1048575)`
is **1021**, whereas exact floor-sqrt is **1023**. The audit checks **384/384 literal table
entries** against the executable.

Support top includes terrain/base elevation, the piece's own height, model heights and
same-footprint support stacking (`0x800B340C..3524`). Spline controls are further corrected
by `0x800B3660`: subtract model-height selector 6, add **24** (`0x801033A0`), and apply
special-role lateral offsets. Roles 1/3 ordinarily use `-200*sin(yaw)` and `-64*cos(yaw)`
with fixed-point shifts (`0x800B36E0..37AC`); one world/type/index branch suppresses the
first displacement (`0x800B3718..3770`).

Those **terrain/model-dependent** quantities are `CoasterPieceGeometry` inputs from
`ICoasterTrackWorld`. The host supplies base height, support-top contribution excluding
the saved Height, and the correction from support top to spline control. The sim itself
centers/scales X/Z, adds Height, narrows coordinates, links nodes, calculates chord lengths,
interpolates positions and banking, and supplies the physics tangent. There is no invented
universal model height, terrain level, station endpoint or special-shape offset in production.

## 3. The 0x88-byte train

The controller report's eight-object census is retained. Pool initialization
`0x800B1B64..BD0` clears the list heads/count and pushes the **eight** objects onto the free list;
`0x800B16E8..174C` moves one to the active list. Both lists push at the front through
`0x800B1BD4..C30`. Simulation owns eight reusable `CoasterTrainMotion` objects, each with
the existing `CoasterTrain` completion controller.

| Train offset | Width | Reading |
|---|---:|---|
| `+00`, `+04` | pointer each | Intrusive next/previous links; `0x800B1824`, `0x800B1DF4/0x800B1DFC`. |
| `+08`, `+0C`, `+10` | s32 each | Forward unit vector in 12-bit fixed point. `0x800B28F8..2938` transforms `(0,0,4096)` from `0x800F8CF0`, then subtracts translation. `+0C` supplies slope to motion at `0x800B2030..2054`. |
| `+14` | 4 bytes | UNKNOWN in this trace. No motion-field name assigned. |
| `+18` | s32 | Speed, `0x800B2010..2118`; setter `0x800B1D64`. |
| `+1C` | s32 | Segment distance accumulator, eight fractional bits, `0x800B2130..2140`. |
| `+20` | pointer | Current segment endpoint, updated by `0x800B21AC/0x800B21F0`. |
| `+24` | pointer | Launch segment, retained throughout the trip, `0x800B2DE0/0x800B2E04`. Its previous link identifies the braking approach. |
| `+28` | pointer | Owning coaster; setter `0x800B1D5C`, launch writer `0x800B0074`. |
| `+2C` | s16 | Passenger count. Append `0x800B1D38..58`, pop `0x800B1D04..34`. |
| `+2E` | 2 bytes | UNKNOWN/padding, no semantics assigned. |
| `+30..6F` | 16 pointers | Passenger storage; independently bounded by the controller's 16-guest staging limit (`0x800B0400`). |
| `+70`, `+72` | s16 each | Current/previous displayed vertical position: copy old value at `0x800B2344..350`, store translated Y at `0x800B28F4..2900`. Not the route distance. |
| `+74` | s16 | Per-train return counter, `0x800B2E78/0x800B2E84`. |
| `+76` | 2 bytes | UNKNOWN/padding. |
| `+78` | word | Still on launch segment. Set 1 at `0x800B2DD4..DE8`, clear on forward handoff at `0x800B2218`. |
| `+7C` | word | Latched ready-to-unload flag, `0x800B2E98`. |
| `+80` | word | Set −1 at `0x800B2E40`; examined before calling `0x800B8F18` at `0x800B22CC..2E0`. **GUESS-medium:** sound-related sentinel/handle; no audio port. |
| `+84` | word | Set −1 at `0x800B2E44`; further purpose UNKNOWN here. |

This accounts for the **136 bytes** including **12 bytes left wholly unnamed**
(`+14`, `+2E`, `+76`, `+84`) and the separately qualified four-byte sound-related field.
The managed object is a semantic subset, not a binary-layout marshalled struct.

### Launch and speed

`0x800AFFB8..B00C0` takes a free train, assigns the owner, sets speed to `launchSpeed<<8`
and starts on `outer+0x14C`. Launch speed is the nonzero override at `0x80103354`, or
signed record `+0xC8` via `0x800B1E90`; the override's initial image value is **0**.
The **12/12** definition values are in `coaster-source.json`: **four** have 16
(Chac Atak, Gorilla Thrilla, Temple of Gloom, Escape Velocity), **eight** have 0.
Zero initial speed is permitted: the motion routine raises it to its minimum.

`0x800B2DC0..E54` clears passenger count, return counter and completion, sets launch flag 1,
and sets current and launch pointers to the same node. Initial distance is exactly:

```
distance = trunc((length << 8) / 4096) * 3072
```

**⚠ DO NOT FIX:** integer division precedes multiplication (`0x800B2E0C..30`); this is
only nominally three-quarters of the span. For length 513, distance is 98304 and the
subsequent reciprocal-quantized fraction is 2688, not 3072.

### A movement update, in source order

`0x800B1F84..22FC`, with signed comparisons and low-32-bit multiplication unless noted:

1. If launch flag is nonzero **and** this is the reserved preview pointer, wait while
   `(owner.elapsed >> 12) < 240` (`0x800B1F98..FD4`, constant `0x80103390`). This is not
   an ordinary passenger-train departure delay. The preview label remains GUESS-high.
2. If current mode is **not 1**, apply exactly one force branch (`0x800B1FDC..2054`):
   on `launch.previous`, subtract **512**; elsewhere subtract **32** and add
   `low32(forwardY * -4096) >> 12`. Constants are `0x80103380`, `0x8010337C`,
   `0x80103378`. Mode 1 skips **both** branches, not just gravity.
3. Compute `maximum = trunc(low32(20480 * liveSpeedSlider) / 100)`, cap speed above it,
   **then** raise speed below **2048** (`0x800B2058..20EC`; data `0x80103388/0x80103384`).
   Slider zero therefore does not stop a train.
4. `step = logicalRightShift(low32(speed * movementDelta), 12)`; raise step below **2048**,
   then halve it if `0x80053D98()!=0` (`0x800B20F0..212C`). Delta comes from
   `0x800BDD0C`, the word at `0x80103A90`. It is supplied by the host, with no file/clock access.
   **⚠ DO NOT FIX:** even delta 0 advances by the minimum displacement; half speed halves
   that minimum only after the clamp.
5. Add step to distance. Read signed segment length. Length zero returns **after the
   distance addition**, before fraction calculation/completion (`0x800B2130..214C`).
6. Compute the exact pre-handoff operand:

   ```
   s2 = arithmeticRightShift(low32(trunc(4096 / length) * distance), 8)
   ```

   This is `0x800B2150..218C`, not `(distance<<4)/length`. Distance has eight fractional
   bits; `s2` uses 4096 per segment. Length 768 gives reciprocal 5, so distance 196608
   produces fraction 3840, not 4096. Progress is chord-scaled, quantized route progress,
   not a distance measured along the displayed cubic.
7. Perform **at most one** handoff (`0x800B2190..221C`): negative fraction → previous,
   distance `trunc((newLength<<8)/4096) * (s2+4096)`; fraction at least 4096 → next,
   distance `trunc((newLength<<8)/4096) * (s2-4096)` and clear launch flag. These scale
   the old fraction by the **new length**. They do not carry a metric distance remainder
   across arbitrarily many segments. Backward handoff does not clear the launch flag.
8. Invoke the existing completion rule with the operands in §4.

Speed and distance both have eight fractional bits relative to world coordinates when delta
is 4096. There is no attraction-status gate in this movement routine, no inter-train collision
query, and no ready-flag stop. Readiness causes unloading through the controller scan.
Missing links on an allegedly valid route throw in the managed boundary rather than causing
the original null dereference. Large squared-coordinate overflow and zero-vector normalization
are not claimed as exact GTE error emulation.

## 4. The three `AfterMovement` arguments

| Existing argument | Exact original source |
|---|---|
| `onReturnSegment` | Current pointer `train+0x20`, **after** handoff, equals `0x800ACC40(owner) = owner+0x14C`. Compare at `0x800B2244..2258`. This is Launch/return, not the approach at `owner+0x10C`. |
| `segmentFraction` | **s2 from `0x800B218C`**, retained through either handoff; strict `s2 > 0x800` at `0x800B225C..260`. Do not recompute it from the new distance. |
| `duration` | Live virtual slot 91: signed adjustment at vtable `+0x2D8`, function at `+0x2DC`, call at `0x800B22A0..2B0`. Compare the incremented signed-halfword return counter at `0x800B22B4`. |

The preview exemption and launch flag tests are `0x800B2220..2240`. Increment
`0x800B2268 → 0x800B2E84` wraps the signed halfword; completion sets the flag through
`0x800B22C4 → 0x800B2E98`. There is **no edge detector**.

Concrete rejecting controls in the executed instruction vectors:

- Arrive from Approach with old fraction **4096**: the new segment is Launch with distance
  **0**, but completion succeeds with duration 1. Substituting the new fraction would fail.
- Already on Launch with fraction **2048**: no increment. At **2049**: increment.
- Leave Launch with old fraction **4096**: the new segment is not Launch, so no increment.
- Preview, or still-on-launch flag: no completion. A signed counter at 32767 wraps negative.

As established in `ride-classes.md` §4, **12/12 coasters**, **three levels each**, have
duration min/max 1/1. Forced larger durations still use this block; repeated qualifying
updates can increment without another circuit. Neither that slider nor this report establishes
seconds per trip or an exact geometric “all cars at the platform” stopping point.

## 5. Headless integration and the rendering dependency

`CoasterSimulation.Update` follows `0x800B0C94..D64`: check route; advance active trains or
eject ordinary active/pending batches; accumulate delta; call the existing
`RollerCoaster.UpdateDispatch`; then dispatch the applicable existing status hook. It owns
pending guests, occupancy, the eight-object free pool, and active push-front order. Unloading
returns each object to the pool and pops its whole guest batch last-boarded-first.

Invalid route ejection uses the saved-next traversal of `0x800B00C4..138`; pending guests
are then ejected in **forward** staging order (`0x800B013C..1B8`). The preview object is
exempt. Dispatch resets elapsed for the whole owner at `0x800B023C`, even when another
train is running. Running calls load/unload and then unload again through the existing
controller (`0x800B0D90/0x800B0DAC`). Status 4 also calls both hooks at
`0x800B10B4/0x800B10D0`; shared lifecycle/wear remains with the existing host layer.

**READ:** the original slope cache comes from `0x800B22FC`, called by the train's drawing
routine at `0x800B2990`. Position is sampled, its derivative normalized, and a matrix built.
Both ordinary and special orientation paths retain the derivative as the third matrix
column (`0x800BF894..8D0`, `0x800BF9EC..FA0C`). Transforming `(0,0,4096)` and subtracting
translation at `0x800B28F8..2938` gives the cached forward vector consumed by physics.
Banking rotates the other two columns; it does not become an extra gravity term.

**GUESS-high, explicit scheduling boundary:** the headless module refreshes this tangent
after every movement and at launch. This removes the dependency on drawing to make physics
run. Actual PSX draw/update ordering, skipped draws, and stale initial/reused cache effects
have not been measured. The original launch routine does not clear the cached vector.
The 32 instruction-oracle cases hold the incoming cache fixed and establish the movement
step itself; they do not prove this new sampling schedule equals every original frame.

`Pose.Position` is the sampled **track centerline**, not the final rendered seat/model
translation. The source additionally applies record `+0xCA`, model dimensions, mode offsets
and camera-relative operations (`0x800B2758..28E0`). Rendering and rider matrices are not
part of this job. `rider-positions.md`'s mesh-0 seat list is not used as a train count.

Host use is concrete: construct the two station nodes from the attraction connection inputs;
append primitive decoded control points or call `CoasterTrack.Restore` with an
`ICoasterTrackWorld`; instantiate `CoasterSimulation(track, host)`; call `Update` once per
host update. `ICoasterSimulationWorld` supplies live delta/sliders/status, model batch size,
launch speed and guest-transfer transactions. It does not implement movement or completion.
No files, clocks or Godot types occur in the module, and `game/` is unchanged.

## 6. Validation

The pre-existing full suite passed **1725/1725** before adding these tests. The **68** new
xunit cases each have a **REJECTS** comment above their test method. Synthetic fixtures exercise full boarding→dispatch→spline motion→
return→unload, independent simultaneous batches, pool exhaustion/reuse, invalid-track
ejection, partial dispatch, and connection/validation controls. The completed-trip test only
calls `Update`; it never sets the completion flag itself. Its disconnected control leaves
the guest queued and launches **zero** trains.

`coaster-source.json` contains **32** cases produced by executing the actual instructions
at `0x800B1F84` in a bounded MIPS interpreter, with supplied getter inputs. It implements
branch delay slots, signed comparisons, unsigned shifts, low/high multiplication results
and truncating division. The C# theory compares speed, distance, segment identity, launch
flag, return counter and completion with those results. It does not use C# code to generate
the expected movement results. The source audit separately checks **384** math-table values.

`tools/mutate_coaster_track.py` makes one production edit at a time, requires failing
**executed** xunit tests for a kill, retains TRX identities, restores sources in `finally`,
and tests the restored baseline. Compiler failures and missing test execution are errors,
not kills. The JSON audit records source/test hashes and every attempted replacement.
The first pass killed **93/95** mutations. Two survived because the fixtures were weak:
the unsigned-shift fixture's multiplication did not set bit 31, and the zero-length fixture
only checked scalar state, missing the early return before pose refresh. The replacement
shift input uses delta **200000** instead of **600000** and the source audit asserts that
the product's sign bit is actually set. A new zero-length test checks retained pose and uses
the same span with restored nonzero length as its moving control. Production did not change.
The JSON preserves the two earlier survivor attempts and both source/test fixture hashes.
**Final validation:** `dotnet test tests/TPW.Sim.Tests/` passes **1793/1793** cases,
including **68/68** new coaster cases. **95/95** production mutations are killed by
executed tests, with no surviving or invalid mutation. The restored focused baseline
passes **68/68**; final production, test and fixture hashes match the audit. Results are
in `coaster-track-mutations.json`. Detailed local TRX/log files are retained under the
ignored `tests/TPW.Sim.Tests/TestResults/coaster-mutations/` directory.

## 7. NOT ESTABLISHED / NOT PORTED

- Live seconds per trip for **any of the 12 coasters**; no emulator park was timed.
- Exact original draw/update cadence and cache lifetime. Headless tangent sampling is an
  explicit scheduling assumption, not a hidden timing constant.
- Complete scene collision/terrain validation, maximum legal grade, clearance rejection,
  or inter-train avoidance. No collision stop was found in the **one** train movement routine
  traced here; that does not prove the whole game has no related behavior elsewhere.
- Automatic asset/terrain adapters for support stacking, the two station endpoints, and
  special-role spline offsets. Their consumed arithmetic and sources are described; these
  are explicit `I…World` inputs, not guessed universal dimensions.
- The full interactive special-shape macro, undo/editor UI, or rebuilding the parked ride
  through the application's park/save host. Primitive construction and restoration are
  implemented and called in tests; no `game/` integration is claimed.
- The meaning of definition `record+0xBC` values 2–4 on coasters remains open, as in
  `rider-positions.md`. The **12** raw byte groups are retained in the source audit.
- Final car/seat matrices, orientation through special inversions, audio handle behavior,
  camera work, and the **12 wholly unnamed train bytes** identified above.
- Exact GTE behavior for invalid/extreme overflowing coordinates and zero-length derivative
  normalization. Zero tangents return zero (GUESS-high); negative wrapped squared lengths
  and missing route links are rejected at the managed boundary.
- Save states the builder cannot create, including a connection flag with zero populated
  pieces: the managed restore currently requires a populated route to apply that flag.
- Real populated save-slot distribution: **zero saved coaster routes** were sampled.
  Tests use synthetic counts, never count the 64-slot reservation as 64 live pieces.
