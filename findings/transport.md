# Theme Park World (SLES-026.88) — is the bus the only guest transport?

Ninth report. Scope: (1) are there plane/ferry/other guest-delivery transports in the PSX build, (2) what
the "exit point" array is and what indexes it, (3) what to write to make guests arrive somewhere other
than off the bus, (4) where `STR_PARKSTATS_ARRIVAL_RATE` gets its number.

Image `TPW.BIN` at 0x80010000. Overlays (TPW.OVL, 12 entries, all LZ → 0x80114158) were scanned as
well. Tags: **SOURCED** = read off instructions at the quoted address. **MEASURED** = observed on the
running game with `fable/a/wrunner` from `states/park_ride.state`, input `btn/in_idle.txt`, PAL, 50 fps,
1 sim tick = 2 frames. **DERIVED** = inferred, source named. **UNMEASURED** = guess.

## 0. The answer in five lines

1. **No plane, no ferry, no second anything. The PSX build has exactly one guest-delivery vehicle and it
   is the bus.** Confident negative; §2 lists what was checked. Nothing gates a hidden transport on park
   rating, research, a built structure or a level flag, because there is no code for one to gate.
2. **The "exit point" array is the map's list of gate tiles, not a list of transports.** Two entries on
   every one of the eight maps on the disc, both on the entrance building's footprint (SOURCED loader +
   all eight map records parsed). `Arrivals()` takes the index as a parameter and has a dead `−1 →
   random exit` branch; the only caller passes 0. That parameter is the fossil you noticed.
3. **What to write.** Held `[0x80103938] = 0` → the bus keeps driving, nobody ever steps off (MEASURED).
   Held bytes at `[0x8010393C]+0` (tile x, tile y of exit 0) → the bus load appears on that tile
   (MEASURED: `(23,5)` put all 20 guests at 8.8 position `(6016,1408)`). Code word `0x80052740`
   = `0x24050001` → same result via exit index 1 (MEASURED); `0x2405FFFF` → random exit per bus (SOURCED,
   not run). None of these is a second transport; they relocate the one you have.
4. **`STR_PARKSTATS_ARRIVAL_RATE` is not a rate.** It is a byte written **once per calendar month**:
   `max(0, guests_now − guests_at_previous_month_boundary)` (SOURCED 0x800670d4, MEASURED twice). The
   displayed value is `McAi[184 + ((months−1) mod 144)]`, `McAi = [0x80102E48] = 0x801E8B50` in this
   state → byte `0x801E8C0C` after the May→June boundary. Do not use it as an instrument.
5. **Better instrument for everything downstream:** the guest pool count `*(u32*)(*(u32*)0x80103884+0xC)`
   sampled on the bus phase edge `[0x80103964]: 1 → 2`, which is exactly the spawn event. Arrivals per
   cycle = the jump in that word on that edge; you already have both words in your watch list.

## 1. The exit-point array (SOURCED unless marked)

| what | where | source |
|---|---|---|
| count | `u32 [0x80103938]` | loader 0x800544E0 stores it at 0x800545C4 |
| entries | `[0x8010393C]` → `u8 x, u8 y` per entry, tile coordinates | loader 0x800545C0; reader 0x800540B8 |
| readers of the count | 0x800540AC only | `xref.py` |
| readers of the array | 0x800540B8 only | `xref.py` |
| writers | the map loader only; **no overlay touches either word** | `xref.py` + overlay scan (§2.6) |

The map resource is `u32 N; u32 tab[N]; u32 w; u32 h; tile[w*h]; u32 nbuild; build[12*nbuild];
u32 nexit; (u8 x, u8 y)[nexit]` and **ends there** — there is no further section for a plane or a
ferry to live in (SOURCED from the loader; the parse of all eight map records lands exactly on the record
end, `fable/t/` scan, §2.5).

`0x800540B8(out, i)` converts entry `i` to an 8.8 position `(x*256+128, y*256+128)` plus a height from
0x80050938. Callers: `Arrivals` 0x80067274 (spawn), guest state 48 "walk out" 0x8009154C (`rand(N)`),
and staff leave states 0x80097828 / 0x80098680 (`rand(N)`). So **exit 0 is where everyone arrives and
every exit is where anyone may leave** — that is the whole distinction between the entries. Nothing in the
entry says "bus" or "gate"; it is a tile.

Live values, this state (MEASURED, `fable/t/live0/ram_000002.bin`): count 2, array at 0x80181144,
entries `(18,5)` and `(23,5)`. Both sit on tile type 12 ("park gate tile", paths.md §1.3) — the gate
building's footprint runs x = 17..24, y = 5..6 (MEASURED from the tile map). This map is FOLIO record
`ext/rip/0203.bin` (44×74, 102 buildings, the same two exits).

### 1.1 The index parameter of `Arrivals` (SOURCED 0x80067274)
```
Arrivals(McAi, exitIdx):
  if exitCount() == 0: return                       ; 0x8006729C..A4
  if exitIdx == -1: exitIdx = rand(exitCount())     ; 0x800672A8..C4   ← dead in this build
  ... rate / head-count as in arrivals.md §3 ...
  pos = exitPoint(exitIdx)                          ; 0x80067384..8C
  spawn count Visitors at pos                       ; 0x80067398..D8
```
The only caller is the bus tick, `jal 0x80067274` at 0x8005273C with `addu a1,zero,zero` in the delay
slot at **0x80052740** (SOURCED). Nothing in TPW.BIN or the overlays calls it with anything else (§2.6).
A function that accepts "which gate" and "any gate" but is only ever asked for gate 0 is the shape of code
written for a game whose other releases had more than one drop-off. That is DERIVED and it is the only
trace of them; the code that would have used it is not here.

## 2. What was checked for a second transport (all negative)

### 2.1 The per-frame tick that hosts the bus (SOURCED 0x80058C20)
Your "game-mode slot 4" is the 4th entry (index 3, at 0x800E0D60) of the in-game mode's vtable at
0x800E0D48. It is a method of the mode object, not a table of transports; the other entries are the mode's
constructor 0x80057EA8 (sets the level index `[0x801038A4]`), enter 0x80057EFC, input 0x8005814C, leave
0x80058E80, and three trivial ones (0x800618BC, 0x80061268, 0x80099D58). None is a vehicle.

Inside 0x80058C20, mode state `[0x80103838] == 3` (in-game) runs, in order, once per frame:

| call | what it is | source |
|---|---|---|
| 0x80038AA4, 0x800EC8C4 | camera / path worker | paths.md |
| 0x800548D8 | **weather**: sun angle via rsin/rcos, rain/snow particle bursts on `[0x80103904]` 1/2 | SOURCED 0x80054930..0x80054B00 |
| 0x80066AC8 → 0x80066C50 | **McAi calendar tick** (§4) | SOURCED |
| 0x800592CC | the **two turnstile lanes** (0x800F2378 + 16·lane, lane 0..1) | SOURCED |
| **0x80052324** | cursor pick-list update (0x8010971C), then **the bus machine** at 0x8005262C | SOURCED; arrivals.md §2.2 |
| 0x80013180 | message-box state machine on `[0x8010265C]` | SOURCED |
| 0x80089BF0 | guest/staff pool sweep (0x800F4EA0, 0x800F5530 lists) | SOURCED |
| 0x80034464 | per-element update over a 16-byte array at `[0x8010383C]` | SOURCED |

One vehicle state machine in the whole list. The bus tick's own front half (0x80052338..0x80052628) is
cursor picking; it does not loop over vehicles.

### 2.2 The bus's neighbours in RAM (SOURCED, `xref.py` over 0x80103940..0x801039B0)
Bus words are 0x80103958..0x8010396C (batch, B, A, phase, pos, tgt) plus the gate words 0x80103950/54.
Every other word in that range belongs to something else: 0x80103940 to the mode object, 0x80103974 to the
**gate building object** (created at level init 0x80058BB4, drawn by the renderer), 0x80103978/7C to the
map cursor, 0x80103980..94 to a 0x8006BA3C-family subsystem, 0x801039A0.. to another. There is no second
{countdown, phase, position} block and no writer of a second one.

### 2.3 Rendering (SOURCED 0x80057AF0)
The world renderer draws: the pick list, every building in the building table, the gate object
`[0x80103974]`, then **one** vehicle mesh — handle at 0x80103970 (loaded at game start 0x800508AC as
FOLIO record 0x5A via 0x800351BC) at world `(pos>>8, 0x100, 0x76C)` while `[0x80103960] ≤ 0`. The drive
line `z = 0x76C` and `y = 0x100` are immediates at 0x80057CB0/0x80057CB8. No second mesh, no second
position source.

### 2.4 Names (SOURCED, string scans)
Class pools in TPW.BIN: PoolOfPeople, PoolOfRides, PoolOfCoasters, PoolOfTrackRides, PoolOfTourRides,
**PoolOfTourTransports** (the in-park tour-ride vehicles, type 7 — not guest delivery), PoolOfShops,
PoolOfSideshows, PoolOfFeatures, PoolOfBalloons, PoolOfLitter, PoolOfCameras, PoolOfEffectors and the
five staff pools. No bus/vehicle pool either — the bus is five gp words, not an object.

A case-insensitive scan of **every file in `ext/` and all 12 decompressed overlays** for
plane|ferry|boat|ship|heli|coach|airport|harbour|zeppelin|blimp|balloon|train|shuttle|taxi|monorail|airship|jet
hits only localized UI text ("training", "entrenar"), the DMA "bus error" string, and PoolOfBalloons.
FOLIO.GAZ's string table was already known clean; this extends that to the binaries and the overlays.

### 2.5 Every map on the disc (SOURCED layout, parsed data)
Eight FOLIO records match the map layout exactly (parse ends on the record's last byte): 0034, 0035,
0116, 0117, 0203, 0204, 0355, 0356 — four themes × two. **All 44×74, all with `nexit == 2`**, exits at
`(17,5),(24,5)` or `(18,5),(23,5)`, i.e. the two ends of the gate footprint on row 5. No map carries a
third point where a plane or a ferry could deliver.

### 2.6 Overlays (SOURCED scan of ovl0..ovl11 at 0x80114158)
No `jal` to Arrivals 0x80067274, the bus tick 0x80052324, the exit accessors 0x800540AC/B8, the new-
visitor allocator 0x80051480, or the stats writer 0x800670D4; no gp-relative access to the exit words, the
bus words, or the knobs (0x80102D40, 0x80102E50/54/60); no reference to the drive table 0x800E0F0C.

### 2.7 Gating (SOURCED)
`Arrivals` is reached only when phase 1 completes **and** `IsOpen()` 0x800541AC **and** no build item is
held (0x80059080), and returns early only on `exitCount == 0` (0x8005270C..0x80052744, 0x8006729C). No
rating, research, structure or level test sits in front of it. Your "the bus is the arrival model"
conclusion is **right, not partial**.

## 3. What to write (MEASURED unless marked, `POKE_HOLD`, from `park_ride.state`)

| poke | effect | evidence |
|---|---|---|
| `[0x80103938] = 0` (u32) | **bus keeps cycling, nobody spawns.** Phase edges at frames 302/958/1690/2346 as baseline; guests 2 → 2 at 958 (baseline 3), then 1 as one leaves | `fable/t/w_noexit` |
| `[0x80181144] = 0x05170517` (exit array entry 0 → tile (23,5)) + `[0x80102E60] = 1` | all 20 spawn at 8.8 `(6016,1408)`; 12 still exactly there at frame 980, none at `(4736,1408)` | `fable/t/w_exit1` vs baseline `fable/t/w_exit0` (12 at `(4736,1408)`) |
| code `[0x80052740] = 0x24050001` (`addiu a1,zero,1`) + E60 | identical: 12 at `(6016,1408)` at frame 980 — the dynarec picks the held code word up | `fable/t/w_a1eq1` |
| code `[0x80052740] = 0x2405FFFF` (`addiu a1,zero,-1`) | each bus picks `rand(2)` — a whole bus load to one gate or the other | SOURCED 0x800672A8..C4, not run |
| `[0x80103938] = 1` | leaving guests and staff always use exit 0 | SOURCED 0x8009154C etc., not run |

Note the exit array lives in the loaded map (heap, 0x80181144 **for this state**); read `[0x8010393C]`
first on any other state. A second bus load per cycle is not available by data; the interval/head-count
dials in arrivals.md §6 remain the only way to raise throughput.

## 4. `STR_PARKSTATS_ARRIVAL_RATE` (string id 0x243)

### 4.1 Where the number comes from (SOURCED)
The "Visitor Information" page (0x80082790) has five rows, ids at 0x800E30F4:
`{0x1E8 People In Park, 0x243 Arrival Rate, 0x36B Happiness, 0x205 Time In Park, 0x2A6 Overall Rating}`,
each fetched by a getter at 0x80082964 + 0x14·row → McAi accessors 0x80066FA8 / **0x80066FE4** /
0x80067020 / 0x8006705C / 0x80067098, all called with `(McAi, 0)`. The value is printed as-is.

Each accessor returns `(u8) McAi[base + idx]` with `idx = 0x80066F44(McAi, n)`:
`idx = (McAi.months mod 144) − max(n, 1)`, wrapped into 0..143; returns −1 (→ value 0) if `n ≥ months`.
Bases: People +40, **Arrival Rate +184**, Happiness +328, Time In Park +472, Overall +616. Five 144-entry
byte rings, one slot per calendar month, 12 years of history.

### 4.2 Who writes it (SOURCED 0x800670D4, called from the McAi tick 0x80066DEC)
McAi layout (SOURCED 0x80066EA8; `McAi = [0x80102E48]`, 0x328 bytes, allocated with the name "McAi"):
`+0` day accumulator (+δ per tick, wraps at 0xF0000 → 99 ticks/day), `+4` month 0..11, `+8` year,
`+0xC` day-of-month, `+0x10` total days, `+0x18` total months (the `sw v0,24(s2)` at 0x80066E04 is in a
branch delay slot and runs on every month change; the branch only guards the year-end call). Month
lengths are the real calendar, `{31,28,31,30,31,30,31,31,30,31,30,31}` at 0x800E1554.

When `+4` (month) changes, the tick runs the stats writer with `slot = months mod 144`:
```
people[slot]   = guests_now                                   ; 0x800671A8
arrival[slot]  = max(0, guests_now − people[slot − 1])        ; 0x800671AC..C8  (0x800693A4 = max)
happy[slot]    = Σ happiness / guests   (0 if none)          ; 0x800671CC..
time[slot]     = Σ time-in-park / guests                      ; 0x80067210..
overall[slot]  = park rating (0x8005B830)                     ; 0x80067250
months += 1                                                   ; 0x80066E04
```
So **"Arrival Rate" = the month-over-month change in People In Park, clamped at zero**. Departures
subtract from it, a busy month with a big exodus at the end reads 0, and it updates once per ~30 game
days (≈ 2970 ticks, ≈ 2 minutes real time). It is a population delta with a misleading label.

### 4.3 Where it lives in RAM (this state)
`McAi = 0x801E8B50` (MEASURED: `[0x80102E48]`; fields at frame 2: month 4, day-of-month 24, days 144,
months 4). Displayed byte = `0x801E8B50 + 184 + ((months − 1) mod 144)`; after the boundary below that is
**`0x801E8C0C`**. People In Park byte for the same month: `0x801E8B7C`. Months: `u32 0x801E8B68`.

### 4.4 Measured
| run | month edge | people[4] | arrival[4] | notes |
|---|---|---|---|---|
| baseline | frame **1270** (clock 15665; predicted ~1270 from acc 587008/0xF0000 + 6 days) | 3 | **3** | guests 2 → 3 at 958 (bus), previous slot 0 |
| `[0x80102E54] = 100` held | frame **1270** | 9 | **9** | guests 2 → 9 at 958; slot 3 = 0 so delta = 9 |

Both `fable/t/w_stats*/watch.csv`. Next boundary (June, 31 days) at frame 1270 + 31·198 = **7408**
(DERIVED from the calendar; not run).

## 5. Numeric falsifiers

1. **`[0x80103938] = 0` held → `[0x80103964]` still goes 4→1 at frame 302 and 1→2 at 958, and the guest
   pool count does not rise at 958** (MEASURED; baseline rises 2 → 3 there).
2. **`[0x80181144] = 0x05170517` + `[0x80102E60] = 1` held → at frame 980, twelve person records read
   position `(6016, 1408)` and none read `(4736, 1408)`** (MEASURED; baseline: twelve at `(4736,1408)`).
3. **Code `[0x80052740] = 0x24050001` + E60 → same twelve at `(6016, 1408)`** (MEASURED).
4. **Byte `0x801E8C0C` is 0 until frame 1269 and 3 from frame 1270; `u32 0x801E8B68` goes 4 → 5 there**
   (MEASURED). With `[0x80102E54] = 100` held the byte becomes **9** at the same frame (MEASURED).
5. **Read-only:** `[0x80103938]` reads 2 and `[0x8010393C]` reads 0x80181144 on every frame of every run
   from this state; the halfwords at 0x80181144 read `0x0512, 0x0517` (MEASURED).
6. **Predicted, not run:** code `[0x80052740] = 0x2405FFFF` + E60 → at frame 980 all twelve stationary
   guests are on ONE of the two tiles (never split), and which one changes between bus loads.
7. **Predicted, not run:** `[0x80102E54] = 100` for 8000 frames → `0x801E8C0D` (slot 5) written at frame
   7408 with `max(0, guests(7408) − 9)`.

## 6. Corrections / notes to earlier reports
- arrivals.md §3 "spawn at exit point 0" — correct, and now: exit points are gate tiles from the map, two
  per map, index 0 hard-wired at 0x80052740; the index/−1 machinery in `Arrivals` is unused.
- "Game-mode slot 4" = vtable index 3 (4th entry) at 0x800E0D60 of the in-game mode vtable 0x800E0D48.
- The McAi object is `[0x80102E48]`, not `[0x8010280C]` (that word is something else; reading it as
  McAi gives garbage — I did, once, in this job).
- The ride "young" test in arrivals.md is in **days** (`McAi+0x10`); the stats history is in **months**
  (`McAi+0x18`). Different counters, same object.

## 7. Method / files
`fable/t/live0` RAM at frame 2; `fable/t/w_stats`, `w_stats_e54` (3000 / 1300 frames, McAi watch);
`fable/t/w_noexit` (exit count held 0); `fable/t/w_exit0`, `w_exit1`, `w_a1eq1` (981 frames, RAM dump
at 980, 20 per bus). Position search = every 2-byte-aligned `(s16 x, s16 y)` pair in RAM near the two
tile centres; the count of exact-centre hits (12) is guests not yet moved 22 frames after spawning.
Static: `fable/xref.py`, `fable/callers.py`, `fable/ann.py`; overlay scan and map-record parse are
one-off scripts, reproduced in transport.json `method`.

## 6. THE BUS MAKES NO SOUND (2026-09-20)

Asked to wire the bus's sounds; there are none to wire. Checked exhaustively rather than by grep:

1. **Every sound call site in the executable was enumerated.** The audio module's entry points are
   0x800B8E08 (89 sites), 0x800B8E4C (13) and 0x800B8E90 (16, positional), all reaching 0x800B84AC.
   Argument convention `(a0 = group, a1 = sound)`, checked against the known placed sound = group 8
   sound 3 at 0x8001C614 (rides.md §placement). **Not one site lies in the bus machine (0x8005262C),
   the bus half of its tick, or `Arrivals` (0x80067274).**
2. **Call-graph reachability, 6 levels deep, from the bus machine and each of its 16 callees**
   (0x8001397C, 0x80019FE8, 0x8001A054, 0x8001B064, 0x800396FC, 0x80052148, 0x80053D78, 0x8005400C,
   0x800541AC, 0x80059080, 0x8005CFF0, 0x8005D010, 0x80061748, 0x800617D4, 0x800BDD0C, 0x80067274):
   **none reaches any audio entry point.**
3. ⚠ The hosting tick 0x80052324 *does* reach one, which is the trap: `jal 0x8006E22C` at **0x800527F8**,
   past the bus machine, plays group 8 sound 10 at 0x8006E308. That routine is **the cheat-code checker**
   — six 12-byte entries at 0x800F320C, each matching a sequence of pad masks from 0x8008974C(0), playing
   one confirmation sound and toggling a flag. It is hosted in the same per-frame tick as the bus and has
   nothing to do with it. Reading the tick's first 200 instructions misses the call entirely.

**A park loads 8 groups**, listed as u16 at **0x800F23CC**: `1, 10, 11, 7, 2, 6, 5, 8` (loader 0x80058694,
eight iterations through 0x800B8B80). Group table 0x800F91D0, `u32 samples, u32 table` per group.

⭐ **Group 2 is loaded by every park and no resolved call site plays any of its 4 sounds.** They are all
~0.3-0.43s at 11,025 Hz (pitch 0x0400), archive entries 283/284. ⚠ Eleven `play` sites compute their group
at runtime, so one of those may own it — "no resolved site" is not yet "nothing". Sent to master to
identify by ear.
