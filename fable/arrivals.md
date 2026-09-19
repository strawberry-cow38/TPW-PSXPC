# Theme Park World (SLES-026.88) — guest arrival rate

Seventh report. Scope: what sets the interval between admissions and the head-count per admission.
Image: `TPW.BIN` at 0x80010000; all globals are gp-statics with fixed addresses.

Tags: **SOURCED** = read off instructions at the quoted address. **MEASURED** = observed on the
running game with the per-frame watch runner in `fable/a/` (state `states/park_ride.state`, input
`btn/in_idle.txt`, PAL, 50 fps, 1 sim tick = 2 frames). **DERIVED** = inferred, source named.
**UNMEASURED** = guess.

## 0. The answer in four lines

1. **There is no rate accumulator and no threshold.** Guests arrive by bus, and the bus runs a
   fixed real-time loop that the park's contents do not touch. Spawn-to-spawn is **694 sim ticks**
   on this emulator (MEASURED, 8 spawns, 7 gaps: 694 694 694 695 697 694 694). The park's contents set only
   **how many guests step off each bus**: one for the fixture park.
2. **The accumulator you can watch** is the bus itself: countdown `0x80103960` (0xC8000 → 0),
   countdown `0x8010395C` (0x50000 → 0), then bus position `0x80103968` (16.16 fixed, −15.0 → 16.0
   = arrival, then 16.0 → 60.0 = drive off). All three step by `[0x80103A90]` once per sim tick.
3. **Head-count per bus** = `min(cap − guests_now, 20 − lanes, floor((S + [0x80102E54]) × 0x1333 /
   [0x80102E50]))` with `S = 10 + Σ per-attraction terms` (SOURCED, §3). For the fixture park
   S ∈ 20..24 → 1. Rate = 1 guest / 694 ticks = one per 7.01 game days.
4. **What to write.** Interval: `[0x80102D40] = 1` → 328 ticks (MEASURED). Head-count:
   `[0x80102E54] = 100` → 7 per bus (MEASURED); `[0x80102E60] = 1` → 20 per bus (MEASURED). Fastest
   data-only setting: `[0x80102D40] = 1` and `[0x800E0F0C] = 15` → **131 ticks** (MEASURED, 9 gaps of
   exactly 262 frames). None of the four words has a writer in TPW.BIN.

Your 705 is 694 plus a walk-to-gate latency that creeps ~10 ticks per cycle and then snaps back:
admission gaps in a 12000-frame run were 705 704 704 703 705 671 704 641 (MEASURED, §2.5).

## 1. Corrections to parkopen.md

| parkopen.md said | actually | evidence |
|---|---|---|
| score = Σ terms, empty park scores 0 | **S = 10 + Σ terms** once any attraction exists; empty park returns 0 | `addiu s6,zero,0xa` at 0x80067444 runs after the "list non-empty" branch (SOURCED). Without the 10, the fixture's 16-day-old level-0 ride scores 10..14 → rate 0 → **no guests**; with it 20..24 → 1. Also `[0x80102E54]=100` gave 7 per bus, which needs S ≥ 117: 10+10..14+100 = 120..124 ✓; 0+10..14+100 = 110..114 would give 6 (MEASURED). |
| ride bonus `level × intensity` only while < 2 days old | bonus is **always** added; the **+20** is what is young-only | 0x80067528 `addu s2,s2,s1` is the delay slot of `beq s0,zero` at 0x80067524 and executes on both paths; `addiu s4,zero,0x14` at 0x80067530 is reached only when `(age<<12)/2024 < 4`, i.e. age ≤ 1 day (SOURCED) |
| countdowns "in McAi day units, 0xF0000 = 1 day"; cycle "≈ 5–8 game days" | units are root-counter time: `[0x80103A90]` = (RCnt2 IRQ count delta) << 7 per sim tick, ≈ 9947 here; cycle = **694 ticks = 7.01 days** | §2.2, MEASURED |
| "one guest every 5–6 days" | one per 694 ticks = 7.01 days | MEASURED |
| paying: fee ≥ 1.5q refuses, so an empty park earns nothing | on `park.state` with the park forced open and `[0x80102E60]=1`, **gate_total rose 400 per admission** (32 paid by frame 2573) | MEASURED, not chased — outside this job's scope, but parkopen §3.2's verdict reading needs a second look |

## 2. The interval: the bus loop

### 2.1 Where it runs (SOURCED)
Game-mode slot 4 `0x80058C20` → bus tick `0x80052324` at 0x80058E34. It is called **once per sim
tick** (MEASURED: `0x80103968` and the clock `0x80103A94` both change on the same every-other frame).
The tick reads its time step from `0x800BDD0C` = `[0x80103A90]`.

`[0x80103A90]` (SOURCED 0x800BDE6C): `min(elapsed, 0x4000)` where `elapsed = 0x800BC290()` =
`([0x80103480] − previous) << 7`; `[0x80103480]` is bumped by the root-counter-2 handler installed at
0x800BC2EC (`RCntCNT2`, target 0x866 = 2150 → 4233600/2150 = 1969.1 Hz). So one sim tick (0.04 s) is
nominally 78.8 × 128 = 10082 units; the emulator delivers 77..79 counts → **δ ≈ 9947 mean** (MEASURED,
5999 ticks: min 9600, max 10112, mostly 9984). One unit ≈ 4 µs. 0xF0000 = 983040 units ≈ 3.9 s.

### 2.2 The machine (SOURCED, 0x8005262C..0x800527D4)
```
A = [0x80103960]; B = [0x8010395C]; phase = [0x80103964]; pos = [0x80103968]; tgt = [0x8010396C]
batch = [0x80103958]; table = (s32*)0x800E0F0C = {-15, 16, 16, 60}; wrap = [0x80102D40] = 4

each sim tick, δ = [0x80103A90]:
  if A > 0: A -= δ; return                                  ; 0x80052634 / 0x800527CC
  if B > 0: B -= δ; return                                  ; 0x80052644 / 0x800527C0
  if phase == 2: if batch == 0: batch = 2                   ; 0x8005265C..0x8005267C
                 elif batch == 1: return        ← HOLD while a gate batch is being admitted
  pos += clamp((tgt − pos) >> 3, 0x100, 0x1000) × δ >> 12   ; 0x80052680..0x800526CC
  if batch == 2 and pos > 0x190000: batch = 0               ; 0x800526D0..0x800526E4  (bus past 25.0)
  if pos < tgt: return                                      ; 0x800526F4
  if phase == 1 and IsOpen() and held == 0: Arrivals(McAi, 0)   ; 0x80052700..0x8005273C
  if phase != 2: B = 0x50000                                ; 0x8005274C..0x80052754
  tgt = table[phase] << 16; phase += 1                      ; 0x80052758..0x80052780
  if phase > wrap: phase = 1; A = 0xC8000; pos = table[0] << 16; tgt = table[1] << 16   ; 0x8005278C..0x800527B4
```
Game start 0x80050600 seeds the same state from the table (0x8005084C..0x800508A8).

A Python model of exactly this (`fable/a/bussim.py`) driven by the measured δ stream reproduces the
live `phase/pos/A/B` words **bit-exactly for 1499 consecutive ticks, zero mismatches** (MEASURED), and
predicted every poke experiment below to the frame before it was run.

### 2.3 Tick budget for one cycle (MEASURED, cycle starting at clock 15181)

| segment | ticks | how it arises |
|---|---|---|
| A countdown | 83 | ceil(0xC8000 / δ) |
| B countdown | 33 | ceil(0x50000 / δ) |
| phase 1 drive, −15.0 → 16.0 (31 units) | 212 | 201 linear at δ/tick while > 0.5 unit away, ~11 decaying tail |
| **arrival: Arrivals() spawns the bus load** | — | phase 1 → 2 edge |
| phase 2: B countdown + 1 move tick | 34 | no hold in this park (guest reaches the gate ~99 ticks after spawning) |
| phase 3 | 1 | instant |
| phase 4: B countdown + drive 16.0 → 60.0 (44 units) | 33 + 298 | |
| **total** | **694** | = 1388 frames = 27.8 s = 7.01 game days |

Closed form: `T = ceil(0xC8000/δ) + 3·ceil(0x50000/δ) + drive(31, δ) + drive(44, δ) + 2 + hold`, with
`drive(n, δ) ≈ (n·65536 − 32768)/δ + ~11`. At the nominal hardware δ = 10082 that is ≈ 688 ticks; at
the emulator's 9947 it is 694. **The park's contents do not appear in it.** The only park-dependent
term is `hold` (§4).

### 2.4 What the interval is NOT a function of
Not park rating, not attraction count, not money, not day. `Arrivals` (0x80067274) is called on the
phase-1 edge regardless and decides only the head-count. An empty open park still runs the bus at
694 ticks with count 0 (MEASURED: `park.state` + `[0x80102D30]=1` alone spawned nobody in parkopen's
run; with `[0x80102E60]=1` it spawned 20 at frames 201, 1589, 2977 — gaps 1388).

### 2.5 Why you measured 705, not 694 (MEASURED, 12000-frame baseline)
Spawn ticks: 15509, 16203, 16897, 17591, 18286, 18983, 19677, 20371 (gaps 694 694 694 695 697 694 694).
Admission ticks (gate_total +400): 15137, 15842, 16546, 17250, 17953, 18658, 19329, 20033, 20674 —
gaps **705 704 704 703 705 671 704 641**, mean 692. Spawn→pay latency: 333 343 353 362 372 346 356 303.
The guest walks to the gate (~99 ticks), waits for the gate batch, and pays; the batch can only start
after the *previous* bus has passed 25.0 (batch 2 → 0 clear, §4), which is phased against the
turnstile cadence, so the latency creeps +10/cycle then drops. Admission gaps therefore read 703–705
for several cycles and then a short one. Sampling at 10-tick resolution over three arrivals lands on
705. **The underlying period is 694.**

## 3. Head-count per bus: `Arrivals` 0x80067274 (SOURCED)

```
if exitPoints() == 0: return                              ; 0x8006729C  ([0x80103938] != 0)
S     = Score(McAi)                                       ; 0x800672C8 → 0x80067400
rate  = (S + [0x80102E54]) * 0x1333 / [0x80102E50]        ; 0x800672D0..0x80067314   statics 0, 0x14000
lanes = lane0 + lane1                                     ; 0x80059150(0) + (1)
cap   = 25 + 75 * kinds_built / kinds_in_catalogue        ; 0x800691F0 (per parkopen; structure re-read, counts not evaluated)
now   = guests_now                                        ; 0x8005BE9C
if [0x80102E60] != 0: count = min(20 − lanes, 20)         ; debug switch, ignores rate and cap
else:                 count = min(cap − now, min(20 − lanes, rate))
spawn `count` Visitors at exit point 0, type rand(8), state 36
```

`Score` 0x80067400: returns 0 if the attraction list is empty, else **10 +** Σ over placed attractions
(iterator 0x8006DCA0/DD3C/DE68 over the kind-3 list at `[0x80103844]`), by type via jump table
0x800E1564:

| type | term | site |
|---|---|---|
| 1 coaster, 3 flat, 6 track, 7 tour | `(20 + rand(10) + level·intensity + (age ≤ 1 day ? 20 : 0)) / 2` | 0x800674C8; level = A+0xF6, intensity = vslot 53, age = McAi+0x10 − A+0xF4 |
| 4 shop | `(20 + rand(10)) / 2` | 0x80067538 (default) |
| 5 sideshow | `(20 + rand(10) + intensity) / 2` | 0x800674A4 |
| 2 feature | `(20 + rand(10)) / 10` | 0x80067534 |

Fixture park: one flat ride, A = 0x801E3A78, built day 131, McAi+0x10 = 147 → age 16, level 0
(MEASURED from `ram_000600.bin`). Term = 10..14, S = 20..24, rate = floor(S × 4915 / 81920) = **1**.

Rate thresholds (DERIVED from the constants): rate n needs S + E54 ≥ ⌈16.667·n⌉ → 17, 34, 51, 67, 84,
100, 117, 134, 150, 167, ... So the head-count is a staircase in S, and one flat ride sits on the first
step with no randomness in the outcome (S never reaches 34). A second flat ride → S = 30..38 → 1 or 2
per bus, random per bus (UNMEASURED, arithmetic only).

`cap − now`: never bound in any run here (guests peaked at 30 with 7 per bus; guests also leave, so the
population oscillated 9..30 over 12000 frames). `20 − lanes`: guests still queued at the two turnstile
lanes; 0 in all runs.

## 4. Gate coupling: the hold, and the trap in the drive table (SOURCED + MEASURED)

`0x80103958` batch state, written by the admit function 0x80052148 (called every tick from the bus
tick) and by the bus tick:
- admit: `if waiting([0x80103950]) && batch == 0: batch = 1, admitted([0x80103954]) = 0`; while
  `batch == 1 && admitted < 11` send message 9 to state-46 guests; `batch = 0` when `waiting == 0`
  (or `admitted ≥ 11 && phase == 2`).
- bus, phase 2: `batch 0 → 2`; **`batch == 1 → hold`** (bus waits for the batch to finish).
- bus, any phase: `batch 2 → 0` only once `pos > 0x190000` (25.0).

Consequences, all MEASURED:
- With 7 guests per bus some reach the gate inside phase 2 and the bus holds: phase-2 durations
  34 34 34 34 **222 178** 34 ticks; those two cycles were 883 and 839 ticks instead of 694. **The hold
  is the only way park contents lengthen the interval**, and it needs guests at the gate during the
  ~34-tick phase-2 window.
- **`[0x800E0F18] = 17`** (phase-4 target 17.0) shortened the cycle to 411 ticks exactly as modelled
  (spawns at frames 958, 1780, 2602), but the bus never passes 25.0, batch sticks at 2, nobody is
  ever admitted: `wait` climbed 0→1→2, gate_total froze. Any phase-4 target must stay **> 25**.

## 5. Addresses (PAL SLES build; all SOURCED, values MEASURED)

| word | address | meaning | fixture behaviour |
|---|---|---|---|
| bus phase | `0x80103964` | 1 drive in, 2 at gate, 3 instant, 4 drive off | cycles 1→2→3→4→1 every 694 ticks |
| bus position | `0x80103968` | s32, 16.16 units | −0xF0000 → 0x100000 (arrival) → 0x3C0000 |
| bus target | `0x8010396C` | 16.16 | 0x100000 / 0x100000 / 0x3C0000 |
| countdown A | `0x80103960` | reset pause | 0xC8000 at reset, −δ per tick, ends ≤ 0 |
| countdown B | `0x8010395C` | phase pause | 0x50000 at each of phases 2, 4 and the reset |
| time step δ | `0x80103A90` | RCnt2 units per sim tick | ≈ 9947 (9600..10112) |
| RCnt2 count | `0x80103480` | +1 per 2150 sysclk/8 | +39 per frame |
| gate batch | `0x80103958` | 0 idle / 1 admitting / 2 bus present, no batch | |
| waiting at gate | `0x80103950` | guests in state 46 | |
| admitted this batch | `0x80103954` | resets per batch, max 11 | |
| **wrap** | **`0x80102D40`** | last phase before reset, static 4, **no writer** | |
| **drive table** | **`0x800E0F0C`** | s32[4] = {−15, 16, 16, 60}, data, **no writer** | |
| **score offset** | **`0x80102E54`** | added to S, static 0, **no writer** | |
| **rate divisor** | **`0x80102E50`** | static 0x14000, **no writer** | |
| **debug 20/bus** | **`0x80102E60`** | static 0, **no writer** | |
| guests now | `*(u32*)(*(u32*)0x80103884 + 0xC)` | PoolOfPeople active count | jumps by the head-count on the phase-1→2 edge |
| ride day built / level | A+0xF4 (u16) / A+0xF6 (u8) | A = first node of `[0x80103844]`+8 | 131 / 0 |
| total days | McAi+0x10 = `0x801E8B60` | the fixture's `day` field | 147 |

"No writer" = no gp-relative store and no lui/addiu-formed store anywhere in TPW.BIN (`xref.py`);
overlays were not scanned (UNMEASURED for overlay code).

## 6. What to write (all held every frame with `POKE_HOLD`, all MEASURED from `park_ride.state`)

| poke | effect | spawn frames from state load | gap |
|---|---|---|---|
| none | baseline | 958, 2346, 3734, 5122, 6512, 7906, 9294, 10682 | 1388 f = 694 t |
| `0x80102D40 = 1` | reset straight after arrival (no phase 2/3/4) | 958, 1614, 2270, 2926 | **656 f = 328 t**; admissions continue (no batch-2 state ever set) |
| `0x800E0F0C = 15` | phase-1 drive 1 unit instead of 31 | 564, 1558, 2552 | 994 f = 497 t (predicted; not run alone) |
| `0x80102D40 = 1` + `0x800E0F0C = 15` | both | 564, 826, 1088, … 2922 | **262 f = 131 t**, 9 gaps all 262; admissions continue |
| `0x800E0F18 = 26` | phase-4 drive 10 units, still passes 25.0 | 958, 1898, 2838 | **940 f = 470 t**, predicted to the frame; admissions continue (gate +400 at frames 1624, 2520) |
| `0x800E0F18 = 17` | phase-4 drive 1 unit | 958, 1780, 2602 | 822 f = 411 t **but admissions stop** (§4) |
| `0x80102E54 = 100` | S + 100 → rate 7 | 958, 2346 | cadence unchanged, **+7 guests per bus** (2→9→16), 7 every bus over 12000 frames |
| `0x80102E60 = 1` | 20 per bus | (park.state, open forced) 201, 1589, 2977 | cadence 1388 f, **+20 per bus** |

Recipe for "arrival rate R guests per tick": pick the interval with wrap/table (131, 328, 470, 694 … all MEASURED)
and the head-count with `0x80102E54` (rate = floor((S + E54)/16.667), capped at 20 − lanes and
cap − now). The countdown constants 0xC8000 / 0x50000 are immediates at 0x8005278C/0x800527A0 and
0x80052750 — patchable as code words if a finer interval dial is wanted, but everything above is data.

Not a dial: `0x80103A90`. It is rewritten from the root counter each tick before the bus reads it, so a
held poke is overwritten.

## 7. Numeric falsifiers (from `states/park_ride.state`, input `btn/in_idle.txt`)

1. **`[0x80102D40] = 1` held → the 4th guest spawns at frame 2926 (clock 16493) and spawns are 656
   frames apart** (MEASURED here: 958, 1614, 2270, 2926). Baseline 4th spawn is frame 5122.
2. **`[0x80102D40] = 1` and `[0x800E0F0C] = 15` held → spawns every 262 frames starting at 564**
   (MEASURED: ten spawns by frame 2922).
3. **`[0x80102E54] = 100` held → the guest count jumps 2 → 9 at frame 958 and 9 → 16 at 2346**
   (MEASURED). `= 50` should give 3 (S+50 = 70..74 ≥ 67); `= 23` should give 2 (43..47 ≥ 34), `= 9`
   should give 1 (29..33 < 34) — those three UNMEASURED, arithmetic only.
4. **Read-only:** `[0x80103960]` reads 0xC8000 at frames 302 and 1690 and nowhere between; `[0x80103968]`
   reads −0xF0000 at the same frames; `[0x80103964]` goes 4→1 there (MEASURED).
5. Baseline arithmetic for the 705: spawn gap 694 = 83 + 33 + 212 + 34 + 1 + 33 + 298 (§2.3); admission
   gaps 705 704 704 703 705 671 704 641 (§2.5). If the port reproduces 694 spawn-to-spawn and a
   ~330–370-tick spawn→pay latency it will reproduce the 705.

## 8. Things I could not close

- **Your "120 guests in 3000 frames" with `0x80102D30=1, 0x80102E60=1` on the empty field.** I ran
  the same pokes on `park.state` for 3000 frames and got **60** spawned (20 at each of frames 201,
  1589, 2977) and ~40 paid. 120 does not fit 20 per bus at 1388 frames per bus; if that run also had
  the force-bus pokes from parkopen §3.3 or started from a different state, that would explain it.
  Worth a re-measure of `*(u32*)(*(u32*)0x80103884+0xC)` rather than a derived count.
- `cap` (0x800691F0): structure confirmed (5 catalogue categories, built/total), the theme's catalogue
  counts were not evaluated, and no run reached the cap. UNMEASURED value.
- Real hardware δ: nominal 10082/tick from the RCnt2 arithmetic gives ≈ 688 ticks; UNMEASURED.
- Whether anything in the overlays writes the five knob words. UNMEASURED.

## 9. Method
`fable/a/wrunner.c` — `runner.c` plus a `WATCH=` list logged to `watch.csv` after every frame (no RAM
dumps). `fable/a/bussim.py` — the §2.2 model; `python3 bussim.py <watch.csv> [wrap] [t0,t1,t2,t3,t4]`
replays the measured δ stream and reports mismatches against the live words (0 on the baseline) and
the predicted arrival frames. Traces: `fable/a/w_*/watch.csv`.
