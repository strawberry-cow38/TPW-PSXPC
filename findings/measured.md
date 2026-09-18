# Measured live, on the real game

Everything here was read out of the running game's RAM on the emulator, not read
out of its code. That distinction is the point of this file: fable's reports in
`fable/` are **derived** (someone read the disassembly and concluded), these are
**measured** (the game was run and the value observed). Where the two agree, say
so; where they disagree, this file is the one with a reproduction.

Every number below carries its unit, its resolution, and what it cannot see.

---

## Clock

**0.5 clock ticks per emulated frame.** Exactly, zero variance, across all three
fixtures and every 600-frame interval in them (19 intervals in `park_with_ride`).

On PAL that is 25 ticks/sec, so `tick_s = 0.04` — **but the 0.04 depends on the
50 Hz assumption, not on the measurement.** The measurement is the ratio. If the
port ever targets NTSC the ratio holds and the 0.04 does not.

## Game day

**Exactly 99 clock ticks = 198 frames.**

Resolution: measured at 2-frame (1-tick) sampling across two consecutive
rollovers, day 120→121→122 at clock 12090 and 12189. Delta exactly 99.

⚠ **This one nearly went out as 100.** At 600-frame sampling the day advances +3
per interval, which reads as 100 ticks/day, and only a single +4 in eighteen
intervals says otherwise. The coarse instrument could not separate 99 from 100
and reported the wrong one with a perfectly straight face. Agrees with fable's 99.

**Confirmed a second time, from a quantity I was not measuring.** fable's
`economy.json` gives the month-rollover period as 2772 and 3069 ticks. Those are
**exactly 28 x 99 and 31 x 99** -- calendar month lengths. If 99 ticks were an
hour rather than a day, a month would be 28-31 *hours*. And `day2` wraps at 30 in
lockstep, which is the same answer from a third place. (Raised by cow tools, who
noticed that 99 ticks x 0.04 s = a 4-second day and asked whether the label was
right before building on it.)

⚠ **The thing that check relocates:** if any real-time figure looks absurd, the
suspect is `TickSeconds`, not `TicksPerDay`. The 0.5-ticks-per-frame ratio is
exact and measured; the 0.04 seconds comes from *assuming* PAL 50 Hz. That
assumption is the only place real-time enters the model at all.

## Admission

**£40 per guest.** `gate_total` rises 400 per admission.

**Stored at 10x the displayed figure**, like `money`. Proven rather than assumed:
over 12000 frames `money`, `gate_total` and `income_total` all moved by exactly
+3200. If gate were in units of £1, money would have moved ten times as far.

⚠ I first reported this as 80 admissions. It is 8. I divided stored units by the
displayed price — right numerator, wrong denominator's unit.

## Arrival rate — there isn't one. Guests arrive by BUS.

**A bus runs a fixed timetable that the park cannot influence. What you build
changes only the HEAD-COUNT per bus, never the cadence.** (fable, arrivals.md,
from the code; confirmed live below.)

Spawn-to-spawn is **694 sim ticks**, measured by fable over 8 spawns with gaps
694 694 694 695 697 694 694. The bus is stepped once per sim tick by the time
delta at `0x80103A90`; its phase word is `0x80103964` and its position
`0x80103968` in 16.16 fixed point.

**fable's prediction, made before the run, and the result:**

| | predicted | measured |
|---|---|---|
| baseline gap | 694 ticks | 710 (median of 4) |
| holding `0x80102D40 = 1` | **328 ticks** | **320** (median of 7) |

That is a specific number called in advance and hit. Baseline admissions read
slightly above 694 because **admission lags spawn**: a new gate batch cannot start
until the previous bus passes position 25.0, so latency creeps ~+10 per cycle then
snaps back. My earlier "705" is 694 plus that creep.

⚠ **My six-park sweep could never have answered the contents question.** A proper
census of all seven attraction pools says every non-empty state holds **exactly
one** attraction:

| state | contents | gaps (ticks) |
|---|---|---|
| park | 0 | no arrivals |
| park_ride | 1 ride | 710, 700 |
| park_shop | 1 shop | 670, 640 |
| park_shop5 | 1 shop | 670, 700 |
| park_trail | 1 shop | 710, 730 |
| park_trail2 | 1 shop | 640 |

So the independent variable took the values {0, 1} and nothing else. I concluded
"the sweep does not support contents scaling" from a dataset **in which contents
never varied above one**. That is not weak evidence, it is none — and the earlier
softening to "unmeasured" was right for a stronger reason than I knew. The
filenames ("park_shop5") suggested variety that the memory did not contain, and I
took the filename for the contents.

**Head-count per bus** (fable, SOURCED at 0x80067274):
`min(cap − guests, 20 − lanes, floor((S + [0x80102E54]) × 0x1333 / [0x80102E50]))`
with **S = 10 + Σ terms**. ⚠ The base 10 at 0x80067444 was missing from the earlier
parkopen report; without it a young low-level ride computes to zero guests.

**Levers, all held per frame, all measured by fable from park_ride.state:**

| write | effect |
|---|---|
| `0x80102D40 = 1` | bus every 328 ticks (confirmed here: 320) |
| `0x80102D40 = 1` + `0x800E0F0C = 15` | every 131 ticks |
| `0x80102E54 = 100` | cadence unchanged, 7 guests per bus |
| `0x80102E60 = 1` | 20 guests per bus |

**Head-count confirmed, three predictions, three exact hits.** Same state, same
window, only the held word differs. Batch = admissions grouped within 200 frames:

| held word | fable predicted | measured batches | total in 6000 frames |
|---|---|---|---|
| none | 1 per bus | 1, 1, 1, 1, 1 | 5 |
| `0x80102E54 = 100` | **7** per bus | 1, **7, 7, 7**, 6 | 28 |
| `0x80102E60 = 1` | **20** per bus | 1, **20, 20, 20**, 5 | 66 |

The cadence is identical in all three -- five buses either way. Only the load
changes. (The leading 1 is the bus already in flight when the save state is
restored; the trailing short batch is the window ending mid-bus.)

**So the answer to "does building more bring more guests" is yes -- by filling the
bus, never by sending more buses.** `0x80102E54` is an offset added to the park
score, and +100 takes the head-count from 1 to 7. That is the mechanism the score
acts through, and it is now measured rather than assumed.

Together with the cadence test (328 predicted, 320 measured) that is **four
independent numbers called before the run and hit**.

⚠ **Trap:** a phase-4 target ≤ 25 (`0x800E0F18 = 17`) shortens the cycle to 411 but
**freezes admissions permanently**. Use 26 or more.

⚠ **RETRACTED: my "120 guests in 3000 frames on an empty field" was wrong.** fable
could not reproduce it, so I re-ran the exact configuration:

| state + held words | admissions in 3000 frames |
|---|---|
| park (empty), nothing held | 0 |
| park + `0x80102D30 = 1` (open) | **0** |
| park + open + `0x80102E60 = 1` | **39** (I published 120) |
| park_ride + `0x80102E60 = 1` | 21 |

Out by a factor of three, and fable's 60 *spawned* against 39 *admitted* is the
consistent pair. I do not know where 120 came from -- most likely a stored-units
figure divided by the wrong denominator, the same class of error as the GBP 60
shop price. It stood in a published file for two hours because nothing checked it
until a second method disagreed.

Two things the re-run establishes that the wrong number obscured:
- **An open but empty park admits exactly zero**, even with the park-open word
  held. The step at zero is real and survives.
- **`0x80102E60` bypasses that gate**, admitting 39 to a park with nothing in it.
  So it is not simply a score multiplier -- it overrides the empty-park refusal
  as well.

## Shop revenue — first measured, via fable's pathfinder switch

**A shop sells nothing because no guest can reach it, and one word fixes it.**

fable's finding: target selection never tests reachability, so a guest picks the
shop, the path request fails, and it wanders off. The shop's own entrance tile is
a real map tile of type 7, and the A* worker will not step onto it from grass.

`u32 [0x8011419C] = 0x800EC28C`, held every frame -- the type-0 (grass) entry of
the pathfinder's dispatch table redirected to the always-admit case.

Measured on `park_shop5.state`, 12000 frames, identical arms but that one word:

| | guests admitted | gate | income | **non-admission** |
|---|---|---|---|---|
| control | 8 | +3200 | +3200 | **0** |
| with the poke | 8 | +3200 | +6400 | **+3200 (GBP 320)** |

The discriminator was fixed before the run: `income_total - gate_total > 0` means
something other than admissions was paid for. **The control is exactly zero**, so
it is not a test that passes on anything.

Both arms admitted the **same 8 guests**, which is the useful control within the
control: the switch changed what guests could *reach*, not how many arrived.

⚠ **Verify the table before trusting any result here.** fable supplied a sanity
check and it passed live: `u32[0x8011419C] == 0x800EC24C`, `[+4*7] == 0x800EC208`,
`[+4*2] == 0x800EC174`. If those ever differ, the loaded overlay is not the one
the type map was read from and a null result would mean nothing. Also confirm
`u32[0x801036C8] == 0` -- while a build item is held every path request queues
forever, which looks exactly like "unreachable".

**RESOLVED, and it was a mislabelled field, not odd arithmetic.** A second run
read the shop's own counters instead of the park totals:

| frame | gate | income | non-admission | shop `served` | shop `V+0x7C` |
|---|---|---|---|---|---|
| 2000 | 800 | 800 | 0 | 0 | 0 |
| 4000 | 1600 | 2000 | 400 | 2 | 40 |
| 6000 | 2000 | 3000 | 1000 | 5 | 100 |

- **The shop really does sell:** `served` goes 0 -> 2 -> 5, and it stays 0 for the
  whole control run. This is not admissions being counted twice.
- **`V+0x7C` is NOT the price. It is cumulative takings, in pounds.** It equals
  non-admission-income / 10 at every sample. A price does not change when
  customers arrive; that is what gave it away. fable's report labels it price, and
  that label is wrong.
- **An item costs GBP 20.** 400 tenths / 2 sales and 1000 tenths / 5 sales both
  give exactly 20. The earlier GBP 60 came from an anchor fable had already told
  me was 8 bytes low.
- `income = gate + non-admission` holds at all three samples. Three fields at three
  addresses agreeing arithmetically is a check on the *instrument*, not just the
  finding.

**Guests DO buy more than once -- about twice each.** Measured properly, `served`
against guests-admitted in the *same* run, 24000 frames:

```
frame   1000   3000   5000   6000   7000  10000  15000  19000  23000
guests     2      3      5      5      6      8     12     14     17
served     0      0      3      5      7     16     20     31     34
```

Sales cross above admissions at about frame 6500 and end at **34 sales for 17
guests, exactly 2.0 each**. `GBP 20` per sale holds at **every one of the twelve
samples** where there is a sale -- that number is as solid as anything here.

⚠ **This retracts a retraction, and the reason matters more than the answer.** I
first wrote "guests buy repeatedly", then withdrew it as unmeasured when a
6000-frame run showed 2 guests/0 sales, 4/2, 5/5 -- which reads exactly like one
purchase each with a lag. It is not. **The crossover simply had not happened yet.**
A guest has to arrive, walk, and buy, so for the first several thousand frames
sales necessarily trail admissions no matter how many times each guest eventually
buys.

So the short window did not merely fail to show repeat buying -- **it actively
produced the appearance of the opposite**, and both cow tools and I read it that
way. Third time today that a claim was true inside the window I measured and
false just outside it (determinism at 1300 vs 12000 frames; arrival periodicity at
three gaps vs fourteen). The correction is not "be more sceptical" -- scepticism
is what produced the wrong retraction here. It is: **derive the window from the
mechanism before concluding in EITHER direction.** A process with a built-in lag
cannot be measured over a window comparable to the lag.

⚠ **This is a cheat, not the game.** It makes grass universally walkable, so
guests cut straight lines and path capacity stops mattering. Good for unblocking
measurement; wrong for any number that depends on guests queueing or bunching.
fable's 3.1 (write real path tiles) is the faithful version.

## Wages and loans: zero, because no park I have has staff

Over **81 game days (~2.7 months, several rollovers)** in a busy park with the
debug switch held:

| accumulator | address | start | end |
|---|---|---|---|
| cumulative wages | `bank+0x12D0` | 0 | **0** |
| cumulative loan payments | `bank+0x12C0` | 0 | **0** |
| gate | `bank+0x12C8` | — | **+82800** |
| income | `bank+0x12D8` | — | **+82800** |

`bank` is behind a pointer at `0x801031BC`; it resolved to `0x801D5658`, which is
what makes `gate = bank+0x12C8 = 0x801D6920` agree with the address I had already
been using.

**The control that makes the zero meaningful.** My first attempt at this watched
the *balance* for a decrease and found none — worthless, because I was sampling
every 200 frames against a balance rising by 8000 per bus, so any charge absorbed
between samples is invisible. A dedicated accumulator is the right instrument, and
its zero is only trustworthy because **its immediate neighbours in the same struct
moved**: gate at +0x12C8 and income at +0x12D8 both climbed 82800 while wages at
+0x12D0 sat at 0. The base is right, the region is live, the field is genuinely
zero.

So: **no staff are employed in any of my save states**, and wages cannot be
measured until I have a park that has some. Not a fact about the game's wage
model, a fact about my fixtures.

## ⭐ A free-money cheat, confirmed

**`0x801031CC = 1`, held every frame, makes everything free.**

fable's economy.json flagged it: a word that makes `TrySpend` return success
without charging, with **no writer anywhere in TPW.BIN** — the same
reader-with-no-writer signature that found `0x80102E60`. Almost certainly the
mechanism behind the published "build anything for free" cheat.

Tested by laying a path, which costs 100 (GBP 10). Same state, same input script,
one held word apart:

| | flag | money |
|---|---|---|
| control | 0 | 500000 -> **499900** (spent 100) |
| treatment | 1 | 500000 -> **500000** (spent nothing) |

The control spending was **pre-registered as required**: if the path had not been
bought in the control arm the test would have proven nothing, because "no money
spent" is also what a failed purchase looks like.

⚠ **I had written this off as untestable ten minutes earlier**, on the grounds
that spending needs the build UI I cannot drive. That was wrong and the
counter-example was already in my own fixtures: `park_buypath` lays a path from an
input script and has done since this morning. **I reasoned about my capabilities
instead of checking them**, and the check was one command against a file I wrote.

## A debug menu shipped in the build

`FOLIO.GAZ` carries the game's symbolic string table, 1031 ids of the form `STR_*`.
Three of them should not be in a retail game:

```
STR_MAINMENU_DEBUG_MENU
STR_MAINMENU_ENTER_SANDBOX_MODE
STR_MAINMENU_EXIT_SANDBOX_MODE
```

`STR_MAINMENU_DEBUG_MENU` sits in the same id family as the in-game laptop menu's
own entries -- OPEN_PARK, BUILD, FINANCE, RESEARCH, PARK_STATS, PURCHASE, STAFF,
RIDES, SHOPS, SIDE_SHOWS, TOILETS, GOLDEN_TICKETS, LOAD_SAVE, GAME_OPTIONS,
EXIT_TO_MAP_SCREEN, QUIT_GAME. So the menu probably has a debug entry that is in
the build and hidden at runtime.

⚠ **SOURCED only as a string, DERIVED as a menu entry, UNMEASURED as reachable.**
A string id in an archive proves the string exists. It does not prove the menu
entry exists, that it is gated rather than absent, or that anything can reach it.
Handed to fable to resolve; not a finding yet.

**On published cheat codes:** sources claim entering the nickname "bovine" unlocks
all rides and shops and grants money on X+Square+Circle. The word does not appear
in TPW.BIN, SLES_026.88, TPW.OVL or FOLIO.GAZ. ⚠ That is **not** a disproof -- the
overlay is LZ-compressed and a raw grep cannot see inside it, and the comparison
may be case-folded or obfuscated. Most published TPW cheats are for the 1994
*Theme Park*, a different game.

## Addresses (PAL SLES build)

| what | address | notes |
|---|---|---|
| clock | `0x80103A94` | +1 per sim tick |
| day | `0x801E8B60` | +1 per 99 ticks |
| day-of-month | `0x801E8B5C` | same cadence, wraps at 30 |
| money | `0x801D565C` | **x10** stored |
| gate total | `0x801D6920` | x10, +400 per admission |
| income total | `0x801D6930` | x10, tracks gate here |
| park open | `0x80102D30` | 0 shut, 1 open |

⚠ Offsets are for the PAL build only, and are the reason the port should
checksum the user's binary rather than assume one.

## Determinism

Replays are deterministic **only to the length actually verified**, and each
fixture now carries that length in `verified_deterministic_frames`. Currently
1300 / 1500 / 12000, three replays each.

This needs the dynarec compiler thread off (`pcsx_rearmed_drc_thread=disabled`);
with it on, a 12000-frame fixture gave FAIL/FAIL/PASS on identical input while a
1300-frame check called the same setup reproducible.
