# Theme Park World (SLES-026.88) — what gates guest arrival

Fifth report. Scope: the park-open gate only. Every address is in the RAW image (`TPW.BIN` at
0x80010000); all "globals" below are gp-relative statics with fixed addresses, none are heap.
READ = read from the disassembly at the quoted address. GUESS = inference, tagged with how strong.
Helper scripts beside this file; new one: `unpak.py` (port of the UNPAK decompressor 0x80018EF0).

## 0a. OBSERVED ON THE CONSOLE — the park IS opened from the gate

**2026-09-20, tinyclaw, pcsx_rearmed headless, PAL SCPH-5502, save state `practice.state`.** With
the park gate the selected object, pressing **✕ (libretro `_B`)** brings up the context list at the
cursor and its one row is **"Open"**. Screenshot in-channel; the control is the same state run with
no input at all, which keeps the plain Build/OK radial.

⭐ SO THE GATE PATH IS THE GAME'S, NOT A PORT DEVIATION. `parkopen.md` §2.1 has the park MENU entry
12 as the trigger, and that is still true; what was not established is that the gate's own context
list carries the same command. It does. The port's "click the gate to open the park" is faithful
and should not be listed as a deviation.

⚠ AND IT IS THE CONTEXT LIST, NOT THE PANEL. The command arrives on ✕, the same button that opens a
placed shop's context list (verified in the same session: a shop with no queue offers exactly
"Delete"). Nothing here says what ○ does.

### The whole pad, from one state, with a control

Same save, one button pulsed at frame 60 (8 frames down), final frame at 751 compared against a
run with no input at all. Park gate selected, root radial (Build / OK) already open:

| button | changed | what |
|---|---|---|
| ✕ cross | 2694 | the context list at the cursor — one row, **Open** |
| △ triangle | 3396 | back / closes |
| L1 | 3035 | camera |
| R1 | 3020 | camera |
| Start | 3495 | **PAUSED** on screen |
| ○ circle | **0** | nothing |
| □ square | **0** | nothing |
| Select | **0** | nothing |

⭐ FIVE BUTTONS REGISTER, so the pad is reaching the console and a zero is a real zero rather than a
dead instrument — that is the control this table needed, and without it "○ does nothing" would be
indistinguishable from "○ never arrived".

⚠ ○ COULD NOT BE REPRODUCED DOING ANYTHING. Pressed from two different selected objects at four
delays (100, 300, 500, 700 frames after the restore), every final frame came back BYTE-IDENTICAL to
a do-nothing control. I hedged this as "a negative about these two
states, not a refutation" — and the author of the ○ claim went back to the code and found it WAS a
refutation: 0x80038D80 never tests a CIRCLE bit at all, it asks about LOGICAL button 0, and the
logical table at 0x800E35C0 does not decode as index→button. The button was mapped without reading
that table. The route 0x80038D80 → 0x80038900 stands; which button reaches it is now unknown.

⭐ THE HEDGE WAS TOO GENEROUS, AND THAT IS WORTH RECORDING. Being careful about the scope of a
negative result is right; softening it past what the evidence supports hands the other side a reason
not to re-check. The measurement said "no button press produced any change"; the code said "no
CIRCLE bit is ever tested". Those agree, and I had written them as if they might not.

## 0. Corrections to earlier reports

1. **behaviour.md §2.6 called 0x80103958 == 1 "GUESS: park open". Wrong.** 0x80103958 is the
   gate-batch state of the bus/turnstile machine (0 idle, 1 admitting a batch of up to 11, 2 bus
   pulling away). It is set automatically whenever anyone is waiting (0x80052148, READ). The park-open
   flag is a different word, **0x80102D30** (§1).
2. **behaviour.md called 0x8010387C "the guest manager". It is `PoolOfBalloons`** (READ, pool ctor
   list 0x800502C0..0x8005032C). The people pool is **0x80103884 `PoolOfPeople`**, 100 Visitors of
   0x64 bytes (0x8005EFF0). 0x800519B0 (despawn) touches both: balloon first, then the guest.
3. **behaviour.md's "mode flag at 0x80102D34" is identified** (§2.4): written only by the memory-card
   loader from save-header byte 42; nonzero = restricted mode (menu is Build + Game Options only, park
   auto-opened, no litter/needs). A fresh game never sets it. Its name is still a GUESS.
4. **economy.md "entry fee is overridden by the scenario"** — precisely: 0x80071F2C reads a 4-byte
   record `{u8 open, u8 pad, u16 fee_pounds}` from the saved-park stream and stores fee×10 into
   BANK+0 via 0x80072F40/0x80087240. Same record carries the open flag (§2.2).
5. ai_out.txt's "which of the 27 slot-4 call sites is the main loop" — the game-mode object's slot 4
   is 0x80058C20 and it calls the bus/guest tick 0x80052324 at 0x80058E34, on the same unpaused path
   that runs the calendar (0x80058E24). READ.

## 1. The flag

| what | address | type | closed | open | READ |
|---|---|---|---|---|---|
| **park open** | **0x80102D30** | u32 | 0 | 1 | writers below |
| month opened | 0x80102D24 | u32 | 0xFFFF (static) | `McAi+4 + 12*McAi+8` at open time | 0x80054188 |

Writers (READ, and these are the only ones — `xref.py 80102d30` finds three sites):

| function | address | does |
|---|---|---|
| **OpenPark()** | **0x80054144** | 0x80102D24 := month + 12×year (McAi via 0x80066AC8, fields +4/+8); 0x80102D30 := 1 |
| ClosePark() | 0x800541A0 | 0x80102D30 := 0 |
| IsOpen() | 0x800541AC | returns 0x80102D30 |

The game-mode class (vtable 0x800E0D3C, vptr at object+0x28, ctor 0x800BD7AC) wraps them as virtual
slots **36 = 0x800622A0** (plays effect 0x78 then OpenPark), **37 = 0x80062280** (ClosePark),
**39 = 0x800622E8** (IsOpen). No static call site for slot 36/37 exists (scanned for `lh 0x120/0x128`
and computed dispatch); treat them as dead or script-driven. The live UI path is §2.1.

Callers of OpenPark (all four, READ): 0x80075ABC (menu), 0x800622A0 (slot 36), 0x80071F2C
(saved-park stream), 0x80059AA8 (memory-card mode byte). Callers of ClosePark: 0x80050600 (game
start, at 0x8005081C) and slot 37.

### 1a. ✅ SETTLED: the gate IS a route in (2026-09-20)

**Measured on the real game** (tinyclaw, emulator): selecting the park gate and pressing **✕** gives a
context list with **"Open"** in it. So the port's gate click is the game's own behaviour, not a
deviation. The reasoning below stands and its open question is now answered: the flag still has only
three writers, and the gate reaches one of them **through the context list**, which is the untraced
route this section guessed at.

### 1a-old. ⚠ The reasoning, kept because it was right for the wrong-looking reason

Master: "the real game DOES let u open the park from the gate, because WHY would it be selectable?" — a
fair question, and the answer here is partial. **Re-checked: the flag at gp+0x06DC = 0x80102D30 has
exactly three references in the whole image** — the two writers in §1 and `IsOpen`. So **no click handler
sets it**, and a gate click cannot open the park directly.

What that does NOT settle: whether picking the gate opens the **root menu**, which carries "Open Park"
(§2.1). That would make the gate the way in with the menu in between, and master's design argument is
exactly that. The gate object is `gp+0x1320` = 0x80103974, referenced six times — created at 0x80058BB4,
drawn at 0x80057C74/F68/F8C, and ticked from the bus tick at 0x800527D4 through its own vtable. **None of
those six is a pick handler**, so if the route exists it is reached some other way and is not yet traced.

## 2. What normally sets it

### 2.1 The park menu — "Open Park"
The park menu is the table at 0x800F4884, 8-byte entries `{text id, handler}` (READ). English text via
FOLIO 0x197: index 12 = **0x242 "Open Park" → handler 0x80075ABC**, index 13 = 0x31F "Leave Park"
→ 0x80075A98 (quits: 0x800580F0(0x50008)), index 11 = 0x20B "Game Options" → 0x800743A4, index 14 =
0x2F5 "Build" → 0x80075550.

0x80075ABC (READ): copies the entrance position (0x80054B04 → gp+0x1324 = 0x80103978), plays effect
**0x78** (0x80014118/0x8001412C/0x80014144 — GUESS-high: the "park open" jingle), calls
**OpenPark 0x80054144**, then points the camera at that position (0x8005400C → 0x800192C8).

The menu builder 0x80075D74 (READ) inserts "Open Park" **only while IsOpen() == 0** (0x80075F3C..
0x80075F50), so the same button sequence lands on different items before and after opening. With a
blind input script that is the most likely explanation for "ride-placement mode opened the park":
the radial park menu shifted by one entry. Falsifier in §4 settles it in one read.

### 2.2 Saved park stream (memory card)
0x800588D0 (game start) → 0x8006B92C → only if the SVEDLEV entry for (park 0x801038A0, sub
0x801038A4) is not −1 → 0x8006C850 → UNPAK → **0x80071D0C** → **0x80071F2C**: reads record
`{open, pad, fee£}`; if `open != 0` → OpenPark; fee×10 → BANK+0. Then 0x80072464 re-spawns the saved
guests (0x80051480 + 0x80091A3C per record). READ. A fresh game has no SVEDLEV entry, so this path
does not run for it (GUESS-high; the table is filled by the memory-card loader 0x8006C524 only).

### 2.3 Fresh game starts CLOSED
0x800588D0 calls 0x80050600 at 0x800589AC, which calls ClosePark at 0x8005081C. Nothing later in the
start path opens it (all OpenPark callers listed above; none are on the new-game path). So a fresh Main
Game and the prebuilt Practice Park both start with 0x80102D30 == 0 — matching your "idle 91 days,
zero guests" and "Practice Park closed". READ for the callers, GUESS-high for "nothing dynamic".

### 2.4 Memory-card header mode byte
0x8006C524 (save loader, magic 0x47415901 at +0x208, hword 0xAC at +0x20C) → 0x80059AA8(byte42 == 0):
stores 0x80102D34 := (byte42 == 0) and, if nonzero, OpenPark. READ. Not relevant to a fresh park.

## 3. What to poke, and what else must be true

### 3.1 The poke
```
u32 [0x80102D30] = 1                                   ; park open
u32 [0x80102D24] = McAi+4 + 12*McAi+8                  ; McAi = *(u32*)0x80102E48 (month 0..11, year)
```
The second line is optional but recommended (GUESS-medium): 0x80067928 at 0x80067A48 computes
`(now_month − 0x80102D24)/12` = years open and compares it with an objective threshold (s4+0x14) before
calling 0x80067658(…, 3). Leaving 0xFFFF in there makes "years open" wrap.

### 3.2 The spawn path, and every condition on it (READ unless marked)
Guests are created in exactly one place: **0x80051480** (alloc from PoolOfPeople, Visitor slot 2 =
0x8008C534 → SetState(36), add to the active list). Its two callers: 0x80072464 (save restore) and
**0x80067274 = Arrivals(McAi, entrance)**, called from the bus tick 0x80052324 at 0x8005273C.

The bus tick (0x80052324, every unpaused frame from 0x80058C20) runs a 4-phase machine on globals
0x80103964 (phase), 0x80103968 (bus position, 16.16), 0x8010396C (target), 0x80103960 / 0x8010395C
(two countdowns in McAi day units, 0xF0000 = 1 day), table 0x800E0F0C = {−15, 16, 16, 60}, wrap
constant 0x80102D40 = 4. Cycle: phase 1 drive in −15→16; **on reaching 16, if `IsOpen() && held ==
0` → Arrivals**; phase 2 (5/15 day, and held while 0x80103958 == 1 i.e. a gate batch is still being
admitted); phase 3 (instant); phase 4 drive off 16→60; reset with a 0xC8000 (0.83 day) pause. Step per
frame = clamp((target−pos)>>3, 0x100, 0x1000) × timescale >> 12. One cycle ≈ 5–8 game days
(GUESS-medium on the timescale constant; it is what you measured as "one guest every 5–6 days").

Conditions, in order, with the value to read:

| # | condition | where | read this |
|---|---|---|---|
| 1 | park open | 0x80052710 | `[0x80102D30] == 1` |
| 2 | **no build object held** | 0x80052720 → 0x80059074 | `[0x80103940] == 0`. Set by 0x80058F90 (catalogue pick-up, from 0x80023988) to the cursor object from 0x800BA45C; cleared by 0x80058FFC when that object reaches state 8 (placed), and by game start. **While you hold a ride, buses arrive empty.** |
| 3 | park has an entrance/exit point | 0x800672A4 | `[0x80103938] != 0` (count of exit points; building-table entries at *0x80103930 flagged 0x04000000) |
| 4 | arrival count > 0 | 0x80067390 | see formula below |
| 5 | a free Visitor in the pool | 0x80051490 | active count `*(u32*)(*(u32*)0x80103884 + 0xC)` < 100 |

Arrival count (0x80067274, READ):
```
score  = Σ over placed attractions (iterator 0x8006DCA0/0x8006DD3C/0x8006DE68) of:
           ride (types 1,3,6,7): (20 + rand(10) + bonus + 20) / 2, bonus = upgradeLevel × intensity
                                  only while the ride is < 2 days old ((days<<12)/2024 < 4)
           shop (4):             (20 + rand(10)) / 2
           sideshow (5):         (20 + rand(10) + intensity) / 2
           feature (2):          (20 + rand(10)) / 10
rate   = (score + [0x80102E54]) × 0x1333 / [0x80102E50]      ; statics 0 and 0x14000 → ≈ 0.06 × score
cap    = 25 + 75 × (distinct attraction kinds built) / (kinds in the catalogue)   ; 0x800691F0
lanes  = guests already queued at the two turnstile lanes (0x80059150(0)+(1))
count  = min(cap − guests_now, min(20 − lanes, rate))
if [0x80102E60] != 0: count = min(20 − lanes, 20)             ; ignores rate AND cap
```
So an empty park has score 0 → rate 0 → **an open park with no rides still gets zero guests**; one
flat ride (score ≈ 22) → rate 1 → one guest per bus, which is exactly your measurement. Each spawned
guest gets type rand(8) (0x800926AC) and is placed at exit point 0 (0x800540B8(&pos, 0); −1 would mean
a random one), state 36 → walks to the gate → state 46 → the admit function 0x80052148 sends message 9
as soon as 0x80103950 > 0 (no open check there) → lanes → turnstile every 32 ticks → state 37 pays.

**0x80102E60 is a debug switch** (READ: one reader, no writer, static 0). Poke `[0x80102E60] = 1`
to get 20 guests per bus in an empty park. Pool is 100, so five buses fill it.

Paying (state 37 → 0x80090D5C, READ, corrects the fuzzy version in behaviour.md):
```
q = Σintensity(all attractions, slot 53) × 4096 / (10000 + rand(5001))
fee_pounds ≤ 0.75q → +1 ; < 1.25q → 0 ; < 1.5q → −1 (all pay) ; ≥ 1.5q → −2 refuses, leaves by the exit
q == 0 and fee == 0 → 0 (pays nothing)
```
For £40 that needs Σintensity ≥ 65..98, i.e. more than one default flat ride (45–56 per rides.md §5).
If guests spawn but BANK+0x12C8 does not move, this is why: lower BANK+0 (fee×10) or add intensity.
No path from the gate is needed for the fee — it is booked before state 45 walks in.

### 3.3 Forcing a bus right now (GUESS-high, straight from the phase machine)
```
[0x80103960] = 0 ; [0x8010395C] = 0 ; [0x80103964] = 1 ; [0x8010396C] = 0x00100000 ; [0x80103968] = 0x00100000
```
Next unpaused frame: pos ≥ target in phase 1 → Arrivals → phase 2. Then the normal cycle resumes.

## 4. Falsifiers (read live, no waiting)

1. **`[0x80102D30]`** — 0 on a fresh Main Game and on Practice Park (if §2.3 is right), 1 the instant
   "Open Park" is selected; 0x80102D24 flips from 0xFFFF to the month index at the same moment and
   effect 0x78 plays. If a fresh park reads 1 with no menu use, §2.3 is wrong and something dynamic
   opens it — tell me and I will chase the writer (it would have to be a base-register store, not a
   gp-relative one, since all three gp-relative sites are listed).
2. **`[0x80103940]`** — nonzero exactly while a build item is held. If it is nonzero when you observe
   guests arriving, my reading of 0x80052720 is wrong.
3. **`[0x80103964]`, `[0x80103968]`** — the bus machine: phase cycling 1→2→3→4→1 and the position
   sweeping 0xFFF10000 → 0x00100000 → 0x003C0000 prove the tick runs even in a closed park.
4. **Guest count** `*(u32*)(*(u32*)0x80103884 + 0xC)` (0x8005CCF0) jumps by `count` at the phase-1→2
   edge; `[0x80103950]` = guests waiting at the gate, `[0x80103954]` = admitted in the current batch.
5. With `[0x80102E60] = 1` and the park open, an empty park must show 20 new guests per bus.

## 5. Not established
- Whether any dynamic path (script, advisor) reaches game-mode slot 36; no static caller found.
- The exact timescale used by the bus lerp (economy.md's 99 ticks/day gives ≈ 7.5 days per cycle
  against your measured 5–6).
- What the Practice Park's saved-stream record says (SVEDLEV entries are −1 in the image; the
  prebuilt park's stream is not among the raw rip files — `unpak.py` found only 0050.bin as a valid
  UNPAK payload and it is not a park stream).
