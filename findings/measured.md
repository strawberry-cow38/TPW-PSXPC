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

## Dead end, recorded so it is not re-walked

**`0x801036C8` (fable's "a build item is held") reads 0 in every save state I
have** — including `park.state`, which successfully lays a path from a scripted
button sequence. So it is not a tool selector and poking it will not let me place
things. It is what fable said it was: a transient flag that pauses the pathfinder
while something is being dragged, and its *correct* value during normal play is 0.

The comment in my own `in_path_350.txt` says "with the tool already selected",
which was my guess at why the script works, not something I verified. Whatever
makes `park.state` accept a path placement from four `cross` presses, it is not
that flag.

⚠ **Why this matters beyond the dead end:** being unable to place attractions is
what keeps three questions unmeasured — whether head-count really scales with
contents, what staff wages are, and shop pricing. Every one of those needs a park
with more in it than my six fixtures have, and all six hold at most one
attraction. **The build UI is the single blocker on the remaining economy work**,
which is why the hidden debug menu is worth more than any individual address.

## No plane, no ferry — the PSX build has one transport

strawberry (who knows the other releases) asked whether the plane and ferry that
deliver guests in other versions exist here. fable checked the code and the answer
is a **confident negative**, verified live on every value I could check:

| | fable said | measured |
|---|---|---|
| exit-point count `[0x80103938]` | 2 | **2** |
| exit array `[0x8010393C]` | 0x80181144 | **0x80181144** |
| entries (tile x,y) | (18,5), (23,5) | **(18,5), (23,5)** |

**The "exit point 0" I flagged as suspicious is a list of GATE TILES, not
transports.** Two entries on every one of the eight maps on the disc, both sitting
on the entrance building's footprint. `Arrivals()` takes the index as a parameter
and has a dead `-1 -> random exit` branch; the only caller passes 0. Exit 0 is
where everyone arrives, and any exit is where anyone may *leave* — that is the
whole distinction.

⚠ **So my inference was half right in the way that matters least.** I said "you do
not index from zero if there is only one of a thing", and that was correct —
there *are* two. They are just gates, not vehicles. Being right that something is
indexed says nothing about what it indexes, and I had quietly supplied "transport"
as the answer. The map record format also *ends* after the exit list, so there is
no section a plane could live in.

## ⚠ `STR_PARKSTATS_ARRIVAL_RATE` is not a rate — do not use it

I had hoped the game's own arrival-rate statistic would be a better instrument
than counting admissions. It is not an instrument at all. fable: it is a byte
written **once per calendar month**, holding
`max(0, guests_now - guests_at_previous_month_boundary)` — monthly growth in
population, not a rate of arrival. Sampling it mid-month reads last month's value.

**The actual better instrument, which I have now confirmed reads sanely:** the
live guest count at `*(u32*)(*(u32*)0x80103884 + 0xC)`, sampled on the bus phase
edge `[0x80103964]: 1 -> 2`, which is exactly the spawn event. Arrivals per bus is
the jump in that word on that edge — direct, rather than inferred from money.

## The debug menu: removed at compile time — but its sliders survive

fable's answer to the `STR_MAINMENU_DEBUG_MENU` lead is a **confident negative**,
and it is worth more than a hedge would have been:

- The three suspicious string ids (0x214, 0xAB, 0x12) are **used as string ids
  nowhere** in TPW.BIN or any of the 12 decompressed overlays. Every raw hit of
  those values is accounted for as something else. `PURCHASE`, `LOAD_SAVE` and
  `MAIN_MENU` are equally dead — **the string table is a PC superset**, shipped
  whole.
- The laptop menu is **15 static rows**, and the builder adds them by literal
  index. No debug row, no index, no handler. **Compile-time absence: there is
  nothing to write.** That is a real answer and it stops the hunt.

**The window half-survived, which is why the strings are there.** Its vtable
(0x800E32B8), tick, draw and label pool are all in the image — I confirmed
"Debug Menu", "Vomit" and "Prank" are literal strings in TPW.BIN at 0xD3210,
0xD23AC and 0xD32A8. No instruction anywhere forms that vtable address. The
constructor was removed and the rest was left behind.

**⭐ The sliders it would have shown are plain globals, and nothing else writes
them. All nine defaults verified live, 9/9 exact:**

| address | label | default |
|---|---|---|
| `0x801031FC` | Vomit / Ride Inc | 1212 |
| `0x8010322C` | Min Litter Increase | 30 |
| `0x80103208` / `0C` / `10` | Ride OK / Good / Excellent Inc | 5 / 10 / 15 |
| `0x8010321C` / `20` / `18` | Vomit / Toilet / Boredom -> Happiness | 85 / 90 / 95 |
| `0x80103230` | Prank Chance | 10 |

So the menu is gone and **its contents are still reachable** — poke the global
instead of moving the slider.

## Sandbox mode is real, and I have been in it all along

`0x80102D34` is the sandbox flag (parkopen.md called it "restricted mode"). The
front end titles saves `[TPW Sandbox] name` exactly when it is set. Its **only**
writer is the memory-card loader, and the saver writes the inverse, so a fresh
game can never reach it — the ENTER/EXIT toggle UI was never built.

⚠ **It reads 1 in `park_ride.state`.** Every measurement I have taken today was
in sandbox mode. It suppresses litter, needs checks, bubbles, goals and advisor
windows, and restricts the menu to Build/Options/Leave. **Anything I have
measured about guest happiness, litter or needs is therefore measured with those
systems switched off**, and I had not known that until now. It does *not* load a
prebuilt park.

## The "bovine" cheat does not exist in this build

Confident negative, and the argument is better than absence-of-string: **the only
text entry in the game is the save-filename keyboard — A-Z plus space, seven
characters, uppercase.** "bovine" is not typeable. The buffer is only `strcmp`'d
against existing card titles; no pad-mask test for Square+Cross+Circle exists
anywhere. Published TPW cheats are for the 1994 *Theme Park*, a different game.

## ⭐ Building works — the full input recipe

The blocker on the rest of the economy work is gone. From `park_ride.state`:

```
triangle                 back out to the top menu   (Build / Path / Laptop)
triangle                 open the purchase screen   (stock counts per category)
cross                    open a category            (Rides -> Crazy Ape, $2000, …)
cross                    buy — the item is now held, footprint shown, Cost: $2000
left or right, ~60 fr    nudge it off the existing ride
cross                    PLACE
```

Result: two rides, both at status 10 (built). Verified in the pool, not just on
screen.

**Up does not work, and the screen says why: the footprint turns RED over water.**
Placement is refused on invalid ground. This is exactly what strawberry described
this morning — "it's red, you are overlapping the border" — and I could not act on
it for eight hours because I had no way to see a colour.

⚠ **Why this took all day, and it is worth writing down.** I concluded early that
I could not drive these menus. Every test pressed a button and then read **memory
addresses** — money, clock, attraction counts. Triangle opens the menu and changes
roughly **1,800 pixels and zero of the words I was watching**, because a menu
opening is a *rendering* event; simulation state does not move until you commit.
So the instrument returned a clean null for a working input, and I believed it.

The fix was to dump the framebuffer to a PNG and look. I had that capability from
the start and had only ever pointed it at texture work. **Match the instrument to
the KIND of effect: UI and selection are visual, and a RAM probe cannot see them.**

The purchase screen also cross-confirms fable's pool table from the game's own UI:
Rides 14 (with 1 built = 15), Track Rides 2, Roller Coasters 2, Shops 20,
Sideshows 10, Features 45 — identical to `rides.json`, arrived at independently.

## ⭐ Does a fuller park bring more guests? YES — by filling the bus

The question that was open all day, finally measured with the independent variable
actually varied. Same save state, same 15,500-frame window, same free-money flag;
one arm builds a second ride first and admissions are only counted afterwards.

| | attractions | admissions | guests per bus |
|---|---|---|---|
| control | 1 | 11 | **1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1** |
| built a 2nd ride | 2 | 15 | **2, 2, 2, 2, 1, 2, 1, 1, 1, 1** |

**The per-bus breakdown is the result, not the total.** With one ride, every single
bus carries exactly one guest, eleven times out of eleven. With two, most carry
two. The number of buses is unchanged — 11 against 10 over the same window — so
the cadence did not move and the *load* did.

That closes the loop on the arrival model: **the timetable is fixed and the park
determines how full each bus is.** It also confirms fable's head-count formula
behaves as described with a real contents change rather than only with the score
offset poked directly.

⚠ The 2-ride arm drops back to 1 on four of its later buses. fable's formula has a
`min(cap - guests, ...)` term, so a park filling up should throttle its own intake
— consistent, but not something I have isolated.

**This is the same question I twice reported on and twice got wrong**: first as
"arrivals scale with what is built" (asserted, unmeasured), then as "the sweep does
not support contents scaling" (retracted to *unmeasured*, correctly, because all
six of those parks held exactly one attraction). It took being able to *build* to
put a second value on the x-axis.

## Sandbox mode is why the laptop was empty — and turning it off restores it

Confirmed visually, which is the first time the sandbox finding has been checked
against anything but the flag's value:

| `0x80102D34` | laptop menu |
|---|---|
| 1 (as shipped in my fixtures) | Build, Game Options |
| 0 (held) | **Information, Build & Hire, Park Statistics, Financial Information, Game Options, Leave Park** |

**`Build & Hire` is the staff route and `Financial Information` is the wage
readout** — both invisible while sandbox is on. So "no park I have employs staff"
had a cause: I could not reach the hire screen, in a mode I did not know I was in.

## The arrival throttle does NOT track guests-in-park

cow tools predicted the drop from 2 guests/bus back to 1 is the `cap - guests_now`
term of fable's head-count `min()` becoming binding — falsifiable, because it
predicts the drop happens at a specific guest count rather than after a specific
number of buses. Measured, two rides, 14 buses:

```
bus    1  2  3  4  5  6  7  8  9 10 11 12 13 14
batch  1  1  2  2  2  2  1  2  1  1  1  1  1  2
guests 2  3  5  5  4  6  5  3  4  5  5  6  6  6
```

**Batch 2 occurs at guest counts 5, 5, 4, 6, 3, 6. Batch 1 occurs at 2, 3, 5, 4,
5, 6, 6.** Five and six each produce both answers, so batch size is not a function
of guests-in-park. It does not track bus number cleanly either.

⚠ **And the strongest signal points the other way.** The first two buses carry
**1** guest at the *lowest* guest counts of the run (2 and 3). If `cap - guests`
were binding, low occupancy should give the *largest* batch. That is the opposite.

⚠ **Limits, stated because this is a weak test either way.** The effect is 1 vs 2
— one unit — my guest count is sampled up to 200 frames before each spawn rather
than at it, and fable's third `min()` term depends on the park score, which moves
as a newly-built ride ages. A score-term explanation fits the early-low pattern
and is **unmeasured**. So: the specific prediction is not supported, and I am not
claiming the replacement.

## Staff and wages — the screen reached, and a number that disagrees with the formula

With sandbox off, `triangle, circle, down, X, down, X` reaches the hire screen:

```
              Stock
Guards          5
Mechanics       5
Cleaners        5
Researchers     5
Entertainers    5
```

Five types, five of each — and **five types is exactly the length of fable's
`mult_by_type_index: [3, 1, 1, 2, 3]`**, which had been an unlabelled array.

One more `X` opens the individual:

```
Gary Liddon
Pay Grade       1
Monthly Wage    $100
Motivation      [bar]
```

(The roster is named after Bullfrog developers — Gary Liddon, Mike Baxter, Dave
Owens. Easter egg, not data.)

⚠ **$100 does not match the formula.** fable's `economy.json` gives
`base[level] * mult[type] * proration`, with `base_by_level = [50, 55, 65, 80, 100]`
and the first type's multiplier 3. Level 1 against that base table gives 50 or 55,
so 150 or 165 — not 100.

**Both readings are credible and I am not picking a winner yet.** Candidates:
the displayed figure may be a different quantity from the charged one (a headline
rate vs. a prorated charge); "Pay Grade 1" may not index `base_by_level` the way I
assumed; or the category I opened may not be the one whose multiplier is 3. What
settles it is neither the screen nor the formula but **the accumulator**: hire
someone, run a month boundary, and read `bank+0x12D0`, which is the figure actually
deducted. That is the next measurement.

## Wages — measured. GBP 100 a month, and neither source was wrong

**Both the screen and fable's formula were right. I was wrong about what I was
comparing**, and my experiments were failing for a reason I never suspected.

fable: `X` on the recruit card does not hire — it starts the **placement tool**. A
second `X` in the world commits. **`triangle` runs the cancel and frees the node.**
My four triangles, added purely to close the menu so time would pass, *destroyed
the recruit every time*. Four month-rollovers then summed an empty list. That is
the third distinct reason one of these runs returned a meaningless zero.

With no triangles, and `0x8001E754 = 0` held to nop the tile check so any tile
accepts the drop:

| | | |
|---|---|---|
| day 155 (first rollover) | wages total **160** | GBP 16 — prorated, hired mid-month |
| day 185 | **1160** | +1000 |
| day 216 | **2160** | +1000 |
| day 246 | **3160** | +1000 |

**GBP 100 per month per staff member, exactly, and the first month prorated by
days employed** — precisely the shape of `base[level] * mult[kind] *
min(100, 100*daysEmployed/monthLen)/100`.

**Why the GBP 100 "disagreement" was mine.** "Pay Grade 1" is `level + 1`, so the
card shows level **0**, not 1. And `rec+0x10` is the staff *kind*, which
economy.md had mapped wrongly — it is 0 Mechanic, 1 Entertainer, 2 Cleaner,
3 Guard, 4 Researcher. Gary Liddon is a Guard, kind 3, multiplier 2. So
`base[0] * mult[3] = 50 * 2 = 100`. The formula predicted the screen all along; I
indexed both tables one place off and called it a contradiction.

⭐ **And a fable claim confirmed by accident:** the arms "X then X (place)" and
"X only (no drop)" produce **byte-identical wage series**. fable said an un-dropped
recruit sitting on the cursor is still paid, and that is exactly what the two
identical columns show.
## Shop prices are PER SHOP TYPE, not universal — GBP 20 and GBP 30

Bought and placed different shop *variants* myself, then measured each:

| variant | sales | takings | per sale |
|---|---|---|---|
| 0 | 18 | GBP 360 | **20.00** |
| 2 | 18 | GBP 360 | **20.00** |
| 4 | 4 | GBP 120 | **30.00** |

Both figures divide exactly, so GBP 30 is a different price and not noise.

⚠ **I nearly published the opposite, and the near-miss is the point.** I had four
confirmations of GBP 20 — 2, 5, 21 and 31 sales, all dividing perfectly. Then I
bought a second shop, measured GBP 20.00 again, and was about to record "GBP 20 is
the universal item price". Checking the **variant byte** showed both shops were
variant 0: I had bought another of the same thing. Six readings of one shop type
look exactly like six readings of the population.

Only after selecting a genuinely different entry in the shop list did variant 4
turn up at GBP 30. **Repetition on one instance is not replication across a
population**, and the check that separates them costs one byte.

This is the same error as the six-park arrival sweep — where all six parks held
exactly one attraction and the independent variable never varied — committed a
second time in one session, in a domain where I had explicitly flagged the risk
("all four readings are the same Fries shop") and then failed to act on my own
flag when the opportunity came.
## ⭐ The radial menus are FACE-BUTTON MAPPED BY POSITION

This is the key that makes every menu in the game navigable, and it explains
every accidental success and failure of the last few hours.

The in-park menu draws its options around a hub. **Each option sits where its
button sits on the pad:**

```
            Build                      triangle  (top)
   Hire       +      Laptop     square    +    circle     (left / right)
            Path                        cross   (bottom)
```

- `triangle` = Build -> the purchase catalogue
- `circle` = Laptop -> Information / Build & Hire / Park Statistics / …
- `square` = Hire -> the staff catalogue
- `cross` = Path

That retroactively explains the sequences I found by brute force: `triangle,
triangle` reaching Purchase is "open menu, pick Build"; `triangle, circle` is
"open menu, pick Laptop". I had been treating those as magic strings.

It also explains the failures. I spent hours pressing `cross` expecting "confirm"
and getting nothing useful — `cross` is not a confirm, it is *the Path option*,
and in a placement context it is whatever sits at the bottom. **There is no
generic OK button.** The same applies inside panels: the Purchase button carries a
**red** glyph, and red is Circle on a PSX pad — pressing `circle` on the staff
detail screen is what finally committed something.

## The save-state stepping technique (and why timed presses had to go)

Timed button presses are an **open-loop instrument**: the script commits at a
fixed frame with no read-back of what is highlighted, so it cannot tell you it
missed. The identical sequence reached the staff screen once and the ride
catalogue the next time. Two wage runs came back zero for two *different* invalid
reasons — that pair is the tell, and a third would have been a third reason.

The fix, which the harness already supported: **`SAVESTATE` after every verified
step.** Run a few hundred frames, screenshot, confirm the screen is what was
expected, save, and make that the base for the next hop. Navigation becomes a
chain of one-move steps that are each checked once and never replayed. No timing
dependence, and a wrong turn costs one short run instead of invalidating an
eight-step sequence.

States now live in `states/ui/` — `01_laptop`, `02_buildhire`, `03_staff`, and so
on, each a verified position in the menu tree.

## Ground truth for a decoder: the legal screen, as the console displays it

An art-free way to check any decoder against the real hardware — exchange a hash,
not an image.

```
320x256, 16bpp little-endian, row-major, no padding
sha256 = 9b8b3bb5a338521c1959d68826130cac07292001c5c44be9fcb4e7c0600fec26
```

Captured from VRAM on a cold boot. **Three caveats, and the first nearly caught me
out:**

⚠ **The screen FADES.** Mean brightness ramps from frame 240, plateaus 280-460,
fades out after 480. Frames 240 and 480 have *completely different hashes* to the
plateau. I was about to publish a hash of a half-faded image as ground truth. The
number above is from the plateau, and frames 280 and 360 are byte-identical, so it
is a stable value rather than one lucky frame.

⚠ **The last 9 pixels may legitimately differ.** The loader reads whole sectors
(`size/2048`, C truncation), so the final 18 bytes of LEGAL.GFX never reach the
console. A mismatch confined to the tail of the last row is that, not a decoder
fault.

⚠ **It is the framebuffer, not the file.** Anything the game does to the image on
its way to the screen is baked in. A match proves agreement with what the hardware
displays, which is the claim worth having.

**Bit 15 (PSX semi-transparency) is set on ZERO of the 81,920 pixels**, so the
masked and unmasked hashes are identical — a TGA round trip cannot differ on it.
Two further fingerprints, so a mismatch narrows itself:

```
full 16bpp             9b8b3bb5a338521c1959d68826130cac07292001c5c44be9fcb4e7c0600fec26
bit 15 cleared         9b8b3bb5a338521c1959d68826130cac07292001c5c44be9fcb4e7c0600fec26
same, minus last 9 px  aa237587d607926e83baf04007098055b7bafe2209af0bcc363cd8735eb8b604
```

Frames 280, 320 and 360 all hash identically, so the plateau value rests on three
samples rather than two.

The general technique is worth keeping: **when two parties need to check that they
decoded the same bytes, compare fingerprints of a canonical layout.** It settles
the question exactly, and nobody has to distribute the asset.

⚠ **And the bare hash turned out to be a poor oracle in practice** — catboy's eight
decode candidates all missed it, and a hash says only "no". Alignment-free
invariants bisect the problem instead:

```
non-black pixels   11,143 of 81,920
peak channel       R=14  G=30  B=29   (max 31)
```

The peak triple is **order-sensitive and position-independent**: a channel-order
bug shows up as 30/29/14 and would miss every hash forever regardless of layout.
The non-black count is invariant under row order, so it separates "decode is
complete and correctly sized" from "ordering is wrong". Two numbers, and a
mismatch localises itself.

⚠ **My end may be the wrong target, and I should say so before someone trusts it.**
I am reading the console's framebuffer *after a fade-in*, not the file. The fade
plateaus and holds constant for 200 frames, so it has plainly finished — but I
cannot prove the plateau is bit-identical to the source rather than the final step
of a ramp that lands very close. Peak channels at the plateau are R=14 G=30 B=29 of
31, which is consistent with the image's own light-blue palette but does not rule
out a scale factor.

**An oracle is more useful when it narrows what a mismatch would MEAN than
when it just answers yes or no.** Three candidate causes were on the table — the
mask bit, sector truncation, row order. Publishing the extra hashes eliminated one
outright and made a second directly testable, so a failure now points at exactly
one hypothesis instead of three.

## Texture format solved (fable) — what I could and could not verify live

fable's `folio.md`: **the `0x96` containers are geometry, not bitmaps.** Skinned
meshes — s16 vertices, RGB vertex colours, 14-byte faces `{i0,i1,i2, 3x(u,v),
clut}` under `{nfaces, tpage}` headers, bone matrices, animation tracks. Its parser
ends exactly on the byte for all 556 sub-entries on the disc. So the pixels were
never in them, and the hunt through those containers was in the wrong place.

**The textures are the twelve 131,156-byte entries**: a 0x54-byte header plus a raw
256-halfword x 256-row VRAM image, uploaded whole to **(512,256)-(767,511)**.
⭐ That explains catboy's flat row-similarity test exactly — **the image starts at
+0x54 with a 512-byte stride**, and a test anchored at +0 with a 1024 stride is
measuring nothing.

It also corrects a premise **I** supplied: I told fable "FOLIO.GAZ is not a string
in TPW.BIN". It is, three times, and the file is opened by name through
`CdSearchFile` — no hardcoded LBA. I relayed catboy's search result as fact and it
was a failed search, not an absence.

**What I verified live:**

| check | result |
|---|---|
| archive table parses at +8 | 422 declared, 433 entries 0x800-aligned |
| twelve entries of exactly 131,156 bytes | **12 found** — matches fable |
| VRAM (512,256)-(767,511) populated | yes, ~200 distinct values per row |

**What did not match, stated honestly:** the 32-byte run I read live at VRAM
(512..527, 272) is **absent from FOLIO.GAZ entirely** — not in the twelve banks,
not anywhere in the file. That is *consistent* with fable's model rather than
against it, since thirteen further entries are compressed, and the resident page is
presumably one of those. But I have not shown that, so the upload path is verified
in shape and not in bytes.

⚠ **And I nearly reported a vacuous confirmation.** fable's cheapest falsifier
reads VRAM at row 256 — which is **all zeros** in my state. My first scan "matched"
nine archive entries, all of which are also zeros there. Nine matches, all
meaningless. I caught it only because catboy had, an hour earlier, described
exactly this: **check both sides vary before comparing them.** Row 272 has 212
distinct values and is the row the test should use.

## All four orientations, and a note on which instrument actually won

Publishing the legal screen in every layout, so a decoder mismatch resolves in one
comparison instead of a hunt:

```
as displayed (top-left)  9b8b3bb5a338521c1959d68826130cac07292001c5c44be9fcb4e7c0600fec26
vertical flip            0c277122a9280ac81e7d6c792ff30327fd7faa8658a7f0e711c452b8418cd29f
horizontal flip          1348b06f8b6ac793efc38664c8ae2eb3a9a7afdfd8c9fc98faff92fc893e8149
both (180 rotation)      8bacca1e196732f1b824d1a449382237433e2985c692cf769b244bc878231711
```

A match on the 180 entry proves the decode is **byte-perfect** and only the
ordering is wrong — and would incidentally settle my open worry that the
framebuffer-after-fade might not be bit-identical to the source file.

⚠ **The honest note: strawberry diagnosed it by looking at it, in about two
seconds.** I had built a hash, then three extra hashes, then two alignment-free
invariants, and a person glancing at the screen beat all of it. For "is this image
right", eyes are the faster and better instrument, and the fingerprints are only
worth their cost for the part a person *cannot* do — confirming byte-exactness,
and doing it without either party distributing the asset.

The invariants did behave as designed, just slower: a 180 rotation leaves the
non-black count and the peak channel triple **identical** and moves only the hash,
so "count matches, peaks match, hash misses" could only ever have meant layout.

## ⚠ CORRECTION: the "peak channel" invariant was a bad statistic

I published `peak R=14 G=30 B=29` as an alignment-free invariant for checking a
decoder. **It is an extremum over 81,920 samples and two stray pixels set it.**
The distribution says the opposite of what the peak implied:

```
non-black pixels in the captured framebuffer   11,143
    with low5  == 0                            11,141
    with high5 == 0                                90
```

So the framebuffer's low 5 bits are **identically zero**, exactly as catboy
measured in the file. The file and the framebuffer agree, and the "there must be a
colour transform between file and screen" conclusion — which my peak triple is what
produced — was wrong.

**The real answer: the pixel data is PSX `BGR555`, not TGA `ARGB1555`.** Same bytes,
opposite channel order:

```
0x73c0   low5=0  mid5=30  high5=28
  as BGR555   (low bits RED)  -> R=0  G=30 B=28   cyan/blue   = what the console draws
  as ARGB1555 (high bits RED) -> R=28 G=30 B=0    yellow      = what a TGA reader gives
```

That is mechanical rather than fitted: the loader DMAs the image straight to VRAM
via `LoadImage`, so the data must already be in the hardware's format. The TGA
header and footer wrap bytes authored for the console.

⚠ **The lesson is one I already had filed and did not apply to my own number.** A
single worst-case value cannot distinguish "one odd pixel" from "the whole image is
like this" — print the DISTRIBUTION, not the extremum. Worse, I published the peak
*as an invariant for someone else to test against*, so my bad statistic became
their false lead. An instrument handed to a peer needs more scrutiny than one I
only use myself, not less.

## SPU voice pitch — the emulator now exposes it, and what it does/doesn't say

Patched pcsx_rearmed to expose the SPU register file (`spu.regArea`, 0x400
halfwords) through the libretro memory interface under a private id, the same way
I exposed VRAM. Voice *n*'s pitch is `regs[((n<<4)|4)>>1]`, and
`rate = pitch / 0x1000 * 44100`.

Read live from a running park, one instant:

```
v0 11143   v1 11143   v2 11111   v3 22298
v4 22298   v5 11111   v6 16710   v7  8355
```

**What this proves: a single global sample rate is wrong.** Voices differ by
nearly 3x at the same moment, and no one rate explains that. strawberry raised
exactly this ("could be multiple different sample rates across different files")
and it is the case a person listening to one sound structurally cannot detect.

⚠ **What it does NOT give: base sample rates.** The pitch register is what the chip
plays *now*, which is the sample's base rate **times the note it is being played
at**. A tracker makes music by playing one waveform at many pitches, so a voice
reading 8355 may be a 22050 sample an octave and a bit down. Getting base rates
needs the note data, not the chip. **This narrows the question; it does not close
it.**

A suggestive pattern, recorded as suggestive: the two commonest values, 11143 and
22298, are 11025 and 22050 **plus 1.1%** — the same ratio on both. Consistent with
22050 being right and everything sitting slightly sharp, but a constant ~1% offset
could equally be the emulator's clock or my arithmetic, so it stays a lead.

⚠ `176389 Hz` recurs and is junk: pitch `0x4000`, the idle value, not a voice
playing at four times CD rate. Worth naming because it is the most common entry in
a naive histogram and would look like a finding.

⚠ **Two masked build failures while doing this**, both from the same habit: I ran
`make … | tail` and then read `$?`, which is the exit status of `tail`. The first
build did not run at all ("run ./configure first") and reported success; the
second failed on an include path and reported success. **A pipeline's exit status
belongs to its last command.** Check the artifact's timestamp, or capture the
compiler's own return code before anything else runs.

## Palettes: 47 in use, all 16-colour, and they live in the STRIPS not the banks

The CLUT logger in my build records the palette of every draw. One park, 1200
frames: **177 draws using 47 distinct palettes, every one of them 16 colours** —
an independent confirmation of 4bpp from the *drawing* side, where catboy reached
it from the file side.

Their VRAM addresses are the useful part:

```
(704,3)  (928,25)  (720,1)  (512,73)  (704,1)  (544,73)  ...
```

**Every one is y < 256**, so none of them sit in the texture page at
(512,256)-(767,511). They fall in the band fable identified as being fed by the two
**headerless 128 KB strips**, entries `0x104` and `0x10C`, streamed sector-by-sector
into (768..895, 0..511).

**So the palettes are not inside the 131,156-byte texture banks.** They arrive from
those strips, uploaded separately — which is why a decoder that has the banks
renders grey levels and has no colours to apply.

⚠ **Not publishing the colour values.** They are small, but they are EA's art coming
off my machine rather than out of a user's own disc. The standing offer instead: send
a page and a CLUT id, and I report which of the sixteen indices differ from what the
hardware holds — a bare yes/no having already proved too blunt once today.
