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

## Admission

**£40 per guest.** `gate_total` rises 400 per admission.

**Stored at 10x the displayed figure**, like `money`. Proven rather than assumed:
over 12000 frames `money`, `gate_total` and `income_total` all moved by exactly
+3200. If gate were in units of £1, money would have moved ten times as far.

⚠ I first reported this as 80 admissions. It is 8. I divided stored units by the
displayed price — right numerator, wrong denominator's unit.

## Arrival rate

**An empty park gets ZERO arrivals. Any non-empty park gets roughly one guest per
640-730 clock ticks, and how much is built barely moves that number.**

Measured across six saved parks, 4000 frames each, 10-tick resolution:

| park | arrivals | gaps (ticks) | mean |
|---|---|---|---|
| park_ride (one ride) | 3 | 710, 700 | 705 |
| park_shop | 3 | 670, 640 | 655 |
| park_shop5 | 3 | 670, 700 | 685 |
| park_trail | 3 | 710, 730 | 720 |
| park_trail2 | 2 | 640 | 640 |
| park (empty) | **0** | - | - |

⚠ **CORRECTION, and it was mine.** I reported this section earlier as *"periodic,
not stochastic -- a constant interval, so the arrival model is a rate and not a
dice roll."* That was three arrivals in one park, and it does not survive the
wider measurement. The gaps range 640-730, a spread of 90 ticks against a
sampling resolution of 10 -- so the variance is **nine times** what the
instrument could have manufactured. It is real. There is jitter, and I called it
constant from a sample too small to show otherwise.

**But it is not a dice roll either, and that half was worth keeping.** Over the
nine measured gaps: mean 685.6, sd 32.1, **CV 0.047**. A memoryless process -- a
per-tick random draw -- has CV = 1.0, so this is **21x tighter than random**, and
the sd is 11x the 2.9-tick quantisation floor of the sampling. A coin flipped
every tick would give gaps of 12 and 3000; these run 640 to 730. That is a
metronome with a wobble: **a counter reaching a threshold, with something small
modulating it.** (Caught by cow tools, who ran the statistic I should have run on
my own numbers before writing either claim.)

⚠ **"Arrivals scale with what is built" is UNMEASURED, not disproved, and the
distinction matters.** I wrote that the sweep did not support it, on the grounds
that one ride gives 705 and five shops give 685. That is **two samples each**, and
both sit well inside a 640-730 spread -- the difference cannot be told from noise.
Calling it disproved would stop the next person looking. Separating the two needs
roughly 5-9 gaps per condition, which is cheap and not yet done.

What the data *does* show is a **step at zero**: an open but empty park gets no
guests at all, and anything built switches arrivals on. That finding survives.

**What DOES move it enormously is the debug switch.** With `0x80102D30 = 1` and
`0x80102E60 = 1` held on an empty field: 120 guests in 3000 frames, about one per
12.5 ticks -- roughly **55x** faster than any natural park here. So a large lever
exists; it is just not "how much you have built", on this evidence.

⚠ **Scope of all of the above:** six parks, 2-3 arrivals each. That is enough to
falsify "constant" and enough to show the zero step. It is *not* enough to fit a
formula, and I should not have implied a model from three points the first time.

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

⚠ **UNMEASURED: whether a guest buys more than once.** I wrote that they "buy
repeatedly". The counts above are 2 guests / 0 sales, 4 / 2, 5 / 5 -- that is one
purchase per guest with a lag, not repeat buying. The 16-sales-from-8-guests
figure came from a different run whose guest count was derived from the GBP 40
assumption. Answering it needs `served` against guests-admitted in the *same* run.

⚠ **This is a cheat, not the game.** It makes grass universally walkable, so
guests cut straight lines and path capacity stops mattering. Good for unblocking
measurement; wrong for any number that depends on guests queueing or bunching.
fable's 3.1 (write real path tiles) is the faithful version.

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
