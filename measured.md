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

## The texture/palette pairing — a page has MANY palettes, not one

The CLUT logger records the texture page and the palette together per draw, so it
yields the *relationship* rather than the art. One park, 177 draws:

| texture page | draws | distinct palettes |
|---|---|---|
| (704, 0) | 86 | **11** |
| (576, 256) | 33 | 7 |
| **(960, 256)** | 18 | **1** — `clut (928,25)`, every time |
| (576, 0) | 7 | 5 |
| (512, 0) | 6 | 4 |

**This reframes the decoder question.** catboy was rendering a page under CLUTs 0-3
and getting four coherent-looking images, and correctly refused to pick the
plausible one. The reason none of them is *the* answer is that **a page does not
have a palette** — different regions of the same atlas are drawn with different
CLUTs, and the choice lives in the drawing command. Several of those four images
were probably correct, for different sprites on the same sheet.

⭐ **`tpage (960,256)` is the unambiguous test case:** eighteen draws, one palette,
no pairing judgement required. Wrong colours there means the decode is wrong;
right colours there means the decode is good and everything else is a pairing
problem.

**`bpp=0` on all 177 draws** — every texture drawn in that park is 4bpp. That is
the pixel-side finding confirmed from the command side, on an unrelated code path.

The log also carries UV bounds per draw, so the mapping can go to *rectangles of a
page → palette*, which is what a renderer actually needs, rather than a page-level
answer that cannot exist.

## Palettes located in the archive — and NOT in the strips

Searched the live VRAM palettes against the archive bytes:

```
clut (928,25)  -> entry #416, +0x36C0   (duplicated at 0x82B938)
clut (704, 3)  -> entry #169, +0x6A4
clut (720, 1)  -> entry #169, +0x5C4
clut (704, 1)  -> entry #169, +0x5A4
```

**Entry #169 is a contiguous palette array** — those three sit 0x20 apart, exactly
one 16-entry CLUT, so it can be walked rather than searched.

⚠ **This corrects the model fable and I were both using.** I had said the palettes
come from the two headerless 128 KB strips, reasoning from their VRAM band. They
are in ordinary archive entries instead. The strips feed *something* at those
coordinates; they are not where these CLUTs originate.

**Why the match is trustworthy** — checked before reporting, because a 32-byte hit
in a 16 MB file is exactly the kind of thing that is chance:
- the (928,25) run has **16 distinct halfwords out of 16** — maximally varied
- it occurs exactly **twice** in the whole archive
- and the control is the real evidence: **five palettes searched, three land
  clustered in one entry**. Coincidence does not cluster.

⚠ **Two gaps, stated rather than smoothed:**
- `clut (512,73)` and `(544,73)` are **not** in the archive verbatim, so some
  palettes arrive compressed or are built at runtime. A plain lookup will not cover
  all 47.
- **`tpage (960,256)` is not in the archive verbatim either** — three different rows
  searched, no match. So the page I had recommended as the clean single-palette test
  case is *not* one of the twelve uncompressed banks, and testing a decode against
  it would mean feeding a path that cannot yet be fed. My "start there" advice was
  half wrong and is withdrawn; `(576,256)` or `(704,0)` are likelier plain banks.

## ⚠ CORRECTION: "16 of 16 distinct halfwords" was almost a tautology

I cited two reasons for trusting the palette-location match. One of them was
worthless and catboy caught it:

```
P(16 random 16-bit values all distinct) = 0.99817 = 99.82%
```

So "16/16 distinct" is true **99.8% of the time by chance**. Nearly every 32-byte
block in the archive passes it. It constrained nothing, and I offered it as
corroboration alongside the real evidence.

**The clustering control was carrying the whole conclusion alone** — five palettes
searched, three landing in one entry at 0x20 spacing. That is the finding; the
distinctness line was decoration that looked like rigour.

⚠ **This is the marginals lesson from the direction I did not expect.** I checked
the *variance* of my comparison, exactly as the rule says — and then failed to ask
**how often that check passes by chance**. A test can have genuine variance and
still be nearly always true. The full form is therefore:

> Check both sides vary, **and** check what fraction of arbitrary inputs would pass
> the test anyway. Variance is necessary and nowhere near sufficient.

catboy drew the right conclusion from it too: they deleted their palette-finding
predicate rather than repairing it, because **32 bytes of arbitrary colour is
indistinguishable from 32 bytes of anything else** — a property of the format, not
a gap a better scan closes. Palette addresses have to come from the draw commands.

## Delivered: the (rect -> palette) mapping

`texture_clut_map.json` — **12 pages, 177 distinct (UV rectangle -> CLUT) pairs**,
read from the GPU's own draw commands. No pixels and no palette colours: it is a
record of the game's behaviour, not its art.

The structure matters more than the rows: **contiguous regions share a palette**.

```
tpage (576,256)   u 117..145  v 88..116  -> clut (512,73)
                  u  88..115  v 30.. 57  -> clut (512,66)
tpage (704,  0)   u   1.. 26  v 166..191 -> clut (720,1)
```

So the atlas is organised in palette *zones* — a handful of areas each with its own
CLUT, sampled many times — not 177 arbitrary per-quad exceptions. That is a
partition a renderer can hold, rather than a lookup it must carry per triangle.

## Eyes vs. the multiple-of-width error — the two instruments' blind spots

Two results from the same afternoon that look contradictory and are not.

**Eyes won decisively, twice.** A decoded image was flipped and mis-coloured. I had
built a sha256, three more hashes, and two alignment-free invariants; strawberry
glanced at the screen and said *"flipped horizontally and vertically"* and *"yeah
its yellow"*. Three sentences beat the apparatus.

**Eyes lost, twice, on the same image.** The texture sheets decoded at 512×512
rendered a **coherent picture with legible text** — and were wrong. They are
1024×256: four 256×256 pages side by side. Worse, when the correct width was tried
it showed "the same picture twice" and was rejected as a doubling — but that was
pages 0 and 2 carrying *similar terrain art*. **The correct answer looked exactly
like the symptom of the error being avoided.**

**The mechanism (catboy's, and it is the keeper):** re-reading a 2D block at a
**multiple** of its true width preserves the byte total and rearranges content
*without destroying local structure* — rows still neighbour rows they resemble. So
it still looks like a picture, and `0x54 + w*h/2 = 131,156` holds for both layouts.
Neither coherence nor size can separate them. Only evidence from **outside the
file** can: the GPU's draw commands step page origins by 64 halfwords — exactly 256
texels at 4bpp — and run UVs 0..255.

⚠ **I first wrote this as "beyond eyes". catboy corrected it and the correction is
better: it is beyond the QUESTION.**

```
"does this look like a picture?"       -> useless, both layouts do
"does anything cross the cut at 256?"  -> instant, decisive, still just looking
```

Rendering the four tiles stacked and checking that no sprite straddles a boundary
settles it by eye in seconds. The evidence was visible all along — it lived at the
**seams**, while I was looking at the whole sheet.

> **A global question only catches global faults.** A multiple-of-width error is a
> seam fault, so it needs a seam question. The instrument was never the problem.

**And that is the single thread through every error in this file:** *the aggregate
was consulted where the information was in the structure.*

- a **peak** instead of a distribution — two stray pixels set it, and I published it
  as an invariant for someone else to test against
- a **match count** instead of the marginals — nine "matches" that were all zeros
- **"does it look right"** instead of "what happens at the join"
- a **pass** instead of asking what the check could possibly fail on
- a **repeated null** instead of asking what each null could not have contained

Four or five different shapes; one mistake. So "render it and see" is necessary and
nowhere near sufficient, and its failure mode is the dangerous one: a confident,
coherent, wrong picture — the visual equivalent of a test that passes on anything.

**Three layout corrections between us today, each needing the other's instrument.**
The GPU-side observations could not have found the file format, and the file-side
parsing could not have found the page geometry.

## Upload trace: textures arrive as 64×64 BLOCKS, not sheets

Patched the GPU to log every CPU→VRAM transfer as *(destination rect, FNV-1a of
the halfwords landed)* — a content fingerprint, so an archive entry can be bound to
a VRAM position **without pixels crossing between parties**.

Cold boot to a park, 11,481 uploads:

| shape | count | what it is |
|---|---|---|
| 24×176 | **11,440** | the intro FMV, 286 distinct frames per slot |
| 64×64 | **40** | the textures |
| 320×256 | 1 | the legal screen |

**The textures land on an 8×8 grid of 64×64 blocks**, x ∈ {512…960}, y ∈ {0…448},
each destination receiving **exactly one content, once — zero swapping.**

⚠ **This kills the "12 sheets rotating through 4 VRAM slots" model** that catboy
inferred and I had accepted. There is no rotation to trace. The whole
512..1023 × 0..511 region is filled once, block by block, so the sheets are being
**cut into 64×64 tiles on the way in**. That also explains why I could not find
`tpage (960,256)`'s rows verbatim in the archive: **I was searching for a
contiguous page that never exists as one.**

Each block now carries a hash, so the binding is: hash the same 4096 halfwords out
of a decoded sheet and match it against the 40.

⚠ **And my own count was nearly a false headline.** I filtered uploads at
`>= 4096 halfwords` intending to isolate texture-sized ones — and 24×176 is 4,224,
so **the filter matched 11,481 of 11,481 and excluded nothing.** I was one step from
reporting "11,481 texture uploads" when 40 is the answer. A filter that excludes
nothing is not a filter, and the count it produces looks exactly like a result.
Caught only by printing the *distribution of shapes* rather than the total — the
same "structure, not aggregate" correction as everything else today.

## The twelve banks are NOT verbatim in VRAM — with the control that makes the null real

catboy supplied 384 sha256 candidates: each of the twelve 131,156-byte entries cut
two ways — 16 contiguous 8192-byte chunks (block-linear storage), and blocks cut
from a 256-halfword × 256-row image (row-linear storage). Whichever matched would
have given both the storage order and the entry→slot binding in one shot.

**Zero of 40 uploaded blocks matched, in either cutting.**

⚠ **The control, because a no-match is exactly what a mis-aimed instrument
produces:**

```
blocks as logged at upload vs the same blocks in the final VRAM
  37 of 40 unchanged
   3 overwritten later
```

So the capture genuinely holds what was uploaded. **And my first attempt at this
was invalid** — I matched against a *save state* rather than a cold boot, and would
have reported the same zero for an entirely different reason. Redone on a cold boot
with the upload log from the same run.

**So the null is real: what lands in those 40 slots is not a verbatim slice of the
twelve entries.**

⚠ **I proposed compression as the cause and it is wrong.** catboy killed it with one
observation from their side of the wall: reading entry #7's raw bytes as 4bpp with
**no decoding at all** yields *legible lettering* — "Gary Liddon / Lead
Programmer". You cannot get readable text out of compressed bytes with a naive
nibble read. Those twelve entries are definitively uncompressed.

**The better explanation is theirs: those 40 VRAM slots are not fed by those 12
entries at all.** The content is the clue — a credits card has no business being
resident while a park is running. Zero matches is *different pictures*, not a
different encoding.

⚠ **But one of my own captures argues against that too, and I said so rather than
letting a tidy theory stand:** my **cold boot** capture also matched zero. If the
twelve were intro/credits art, the cold-boot blocks should hit. They do not. So
"wrong moment" does not cover it either unless the credits load somewhere neither
capture reached. My instrument cannot separate those cases; scanning all 422
entries at every alignment can, which is why the hashes went over rather than a
guess about timing.

⚠ **This leaves the 1024×256 sheet layout UNRESOLVED, not disproved.** If bytes are
decompressed before upload, the layout question concerns the *decompressed* form,
which my hashes cannot see at all. The comparison tests raw archive bytes against
VRAM and those are simply different representations.

The productive direction is the reverse: hand me a **decompressed** block and I can
say whether it is in VRAM and where — which tests the decoder and the layout
together, and works precisely because the raw bytes do not match.

## The PSX texture model, and what it settles by arithmetic

Worth stating plainly because it turns two open questions into closed ones. The
PlayStation's GPU is publicly and thoroughly documented (Martin Korth's *nocash
PSX spec* is canonical); nothing here needed reverse engineering.

```
VRAM           1024 x 512 halfwords, 16-bit
texture page   64 halfwords wide x 256 rows
at 4bpp        4 texels per halfword -> 256 x 256 TEXELS per page
CLUT           16 colours for 4bpp, addressed as x/16, y
UVs            0..255 within a page
```

**Applied to the archive entries:**

```
entry                131,156 bytes
minus 0x54 header  =  65,536 halfwords
one 4bpp page      =  64 x 256 = 16,384 halfwords
65,536 / 16,384    =  4 exactly
```

⭐ **Each entry is exactly four texture pages**, and four pages of 256×256 texels
side by side is 1024×256 — **catboy's layout, which they reached by eye and then
doubted, is correct and now follows from the hardware.** It stops being "the render
looked coherent" and becomes arithmetic.

The same model explains my upload trace: I logged 64 blocks of 64×64 halfwords, and
a page is four such blocks stacked. **64 blocks = 16 pages = four entries' worth
resident at once** — which also retires my "12 sheets rotating through 4 slots"
worry, since 16 page-slots exist and nothing needs to rotate.

**What remains open is bookkeeping, not format:** *which* archive entries hold the
in-game art. The twelve we characterised are something else. That is a lookup
problem and the format work is done.

---

## Entry 0x10C → VRAM: binding confirmed, and the null that turned into evidence

cow tools reported archive entry `0x10C` binding to VRAM at stride `0x2000`,
block-linear, column-major. I verified it and first got **zero matches** —
against a *fresh cold boot*.

The contradiction was mine. The hashes cow tools matched came from my
`park_ride` capture. Against that capture all 14 blocks land:

```
+0x0     -> 768,  0        +0x10000 -> 768,256
+0x2000  -> 768, 64        +0x12000 -> 768,320
+0x4000  -> 768,128        +0x14000 -> 768,384
+0x6000  -> 768,192        +0x16000 -> 768,448
+0x8000  -> 832,  0        +0x18000 -> 832,256
+0xA000  -> 832, 64        +0x1A000 -> 832,320
+0xC000  -> 832,128
+0xE000  -> 832,192
```

**The failure is itself the result.** Entry `0x10C` is resident during a park
and absent at boot — exactly what in-game art must do, and exactly why my
earlier cold-boot capture matched nothing. I had read that null as "the archive
does not feed these slots" when it meant "I looked before the game loaded them."
The *difference between the two captures* is positive support for the twelve
131,156-byte entries being intro/credits art rather than in-game art.

**Rule: before reporting a null, state when you looked, and check you looked
when the thing would be there.** Three of my errors today were the same shape —
a clean zero I was about to hand to someone else as their problem.

Two blanks-related notes:
- cow tools' raw scan returned 149 matches; 135 were blank-on-blank (one sha256
  of 8192 zero bytes sat at 34 VRAM positions). Real count is 14. Their own
  catch, before sending.
- Their posted table showed 12 of those 14 without saying it was truncated. My
  run found `+0x18000` and `+0x1A000` independently. Had I trusted the paste
  over my own output I would have gone hunting for two absent blocks.

### Two file shapes, not one

Applying the measured binding to *every* 131KB entry is wrong. cow tools
assembled a 131,156-byte entry block-linear and it shattered; row-major is
legible.

```
131,156 bytes (0x54 header) : 1024 x 256 px, ROW-MAJOR, = 4 texture pages side by side
131,072 bytes (no header)   :  256 x 1024 px, 16 blocks of 256 x 64
```

Checks on the second shape: at 256 px wide, 8192 bytes IS one 64x64-cell block,
which is why the contiguous chunks matched; `0x104` rendered as 16 stacked
256x64 blocks gives intact rows of guest sprites; and the VRAM destination
(768..895, 0..511) is 128 cells = 512x512 px = 262,144 px, exactly the entry's
pixel count as two columns of blocks — which is why the matches alternate
`768,y` / `832,y`.

So the 65,536/16,384 = 4 page arithmetic is right for the *sheets*, and the
block layout is right for the *sprite banks*. Both are 4bpp, uncompressed,
131KB, and differently laid out. **A binding verified on one entry does not
generalise to an entry it was never tested on.**

## Is (768..895) a staging buffer? Measured: no within a park, yes across phases

Two instruments, one new. Added a **VRAM→VRAM copy logger** (GP0 0x80) next to the
CLUT logger in `plugins/gpulib/gpu.c`, env var `TPW_COPYLOG`, recording each
distinct (src, dst, size) once.

### 1. The game does not blit textures. At all.

Twelve save states (9 in-park, 3 menu), 30 frames each. Every single run logged
**exactly one** distinct copy, and it is the same one:

```
# nth sx sy dx dy w h
1 0 0 0 0 2 1
```

A 2×1 copy from (0,0) to (0,0). `do_vram_copy_pre` returns 0 for `sx==dx &&
sy==dy`, so the GPU does not even execute it. **Zero real VRAM→VRAM copies.**
Whatever the paging system does, it does it by re-upload (GP0 0xA0), never by blit.

### 2. Within a park, texture VRAM is frozen

sha256 of all 64 blocks (8×8 grid of 64×64 over x 512..1023, y 0..511), 9 in-park
captures:

```
tpage x   distinct values per block, across the 9 parks
512       [1, 9, 9, 9, 1, 1, 1, 1]
576       [1, 1, 1, 1, 1, 1, 1, 1]
640       [1, 1, 1, 1, 1, 1, 1, 0]
704       [1, 1, 1, 1, 1, 0, 0, 0]
768       [1, 1, 1, 1, 1, 1, 1, 1]
832       [1, 1, 1, 1, 1, 1, 0, 0]
896       [1, 1, 1, 1, 1, 1, 1, 1]
960       [1, 1, 1, 1, 1, 1, 1, 1]
```

61 of 64 blocks are byte-identical everywhere. Six are permanently blank.

**The three that vary are not per-park — the control says per-frame.** Comparing
frame 10 against frame 20 *of one capture* changes exactly `512,64`, `512,128`,
`512,192` and nothing else. So "9 distinct values in 9 parks" was 9 samples of a
per-frame scratch strip (64×192 halfwords at x=512, y=64..255), not park content.
Without that control it reads as evidence for per-park paging, which is backwards.

**So cow tools' second prediction is refuted as stated:** 768..895 holds identical
bytes in all 9 in-park captures. No two sprite banks take turns there during play.

### 3. Across phases it IS reused, by upload

Menu states versus a park state, scratch strip excluded:

```
identical        16 blocks
differ           16 blocks   512,256..448  576,256..448  640,256..384  704,256
                             768,256  768,320  768,384  768,448
park-only        23 blocks   (blank at the menu, filled in a park)
menu-only         4 blocks   (freed on park entry)
```

`768,256..448` is in the differing set, so the lower half of 0x10C's destination
does hold other content at the menu. The window is reused — across game phases,
not within a park, and by re-upload since there are no blits.

**Open caveat, stated rather than hidden:** the 9 in-park states were built by
stepping from one another and may all be one park. "Identical across 9 captures"
is therefore safe as *within a park over time* and NOT yet established as *across
different parks*. The falsifier is a second, independently created park.

**For the port:** a park's texture set is fixed for the life of the park. One VRAM
capture per park is complete, and nothing needs to emulate runtime paging.

### CORRECTION: the "resident is not sampled" finding above is withdrawn

The claim that the GPU never samples the pages holding the guest sprites is **not
supported**. It is a statement about my captures, not about the game.

I checked the instrument and cleared it — the raw log carried the same page origins
as the aggregate, the decoder covers the full page range, and it catches sprites as
well as polygons. Having eliminated the instrument, I published the null.

**I never looked at the picture.** `park_ride` is a ride-placement screen pointed at
empty grass, with Place/Cancel/Undo up and `gate_total` = 400 — **one guest admitted
in the park's entire history.** Across all nine "in-park" captures:

```
park  park_shop  park_shop5  practice  practice_open  practice_clean   0-1 guests
park_trail                                                             4 guests
park_trail2                                                            5 guests
```

Running `park_trail2` forward 39,000 frames reaches 32 admitted and **still** logs
nothing from those pages — because the camera stays in placement mode on empty
ground the whole time. Adding cancel presses to escape placement opened a purchase
menu instead. **Every capture I own is build mode or a menu. Not one contains a
guest.** The pages went unsampled because there was nothing to draw.

**What survives this, and what does not.** Note the withdrawal is of my EVIDENCE,
not of the distinction: "present" and "read" are different properties whatever my
captures showed, and keeping that distinction is what makes the presence check
obvious in the first place.


| claim | status |
|---|---|
| zero real VRAM→VRAM copies in 12 states | **stands** — independent of what is on screen |
| 61/64 blocks identical across 9 captures | **stands but weaker** — 9 near-empty early-game parks, not 9 varied parks |
| the 3 varying blocks are per-frame scratch | **stands** — the frame-10-vs-20 control is within one capture |
| menu vs park: 16/16/23/4 block split | **stands** |
| "the GPU never samples 768/832" | **WITHDRAWN** |
| "resident is not sampled" as a principle | **stands** — true by definition, and independent of my bad demonstration of it |
| cow tools' 0x10C -> VRAM binding | **stands** — a content-hash match is a residency claim, established by comparing bytes; it never asserted anything about sampling, so nothing in the sampling log can reach it |

**This also un-blocks the palette join rather than blocking it.** The task is not
"log harder", it is **get the camera onto guests and then log** — save-state
stepping, the technique that already solved the UI-driving problem. A presence
check belongs in the protocol: read `gate_total` and render the frame before
trusting any texture-sampling null.

## The file→colour intersection is not empty: 17 VRAM blocks found verbatim on disc

The working assumption was that the pages with logged palettes (512, 576, 640, 704,
896, 960) and the one page bindable to a file (768/832) do not overlap, making a
guest capture the only route to a file→colour chain. **That is wrong, and the reason
is search granularity.**

Method, deliberately avoiding the archive parser: de-interleave the MODE2/2352 disc
image to a flat user-data image (2048 bytes at offset 24 of each of 219,387
sectors = 449,304,576 bytes), then search it for each non-blank 64×64 VRAM block's
full 8192 bytes.

**17 of 58 non-blank blocks are present verbatim:**

```
768,0  768,64  768,128 768,192 768,256 768,320 768,384 768,448
832,0  832,64  832,128 832,192 832,256 832,320        0x159bf000 .. 0x159d9000
512,0   -> 0x15974b24      4 distinct palettes,  6 rects
704,0   -> 0x1589a524     11 distinct palettes, 86 rects
896,0   -> 0x15d0ea00      4 distinct palettes,  6 rects
```

Two things follow.

**1. The 0x10C binding is confirmed from the disc, with no archive parsing in the
path.** All 14 blocks sit on exact 0x1000 boundaries — sector alignment is the
streaming showing through.

**2. The alignment also explains the apparent gap.** The 14 are sector-aligned
because they are streamed whole, so an entry-granular hash scan finds them. The
other three sit at *unaligned* offsets — they are sub-regions inside a file, not
whole entries, and no entry-granular scan can see them however correct it is. The
intersection looked structurally empty because of how the search was cut, not
because of how the disc is laid out.

**So a complete file→colour chain exists now on `704,0`:** bytes at a known offset,
11 palettes logged against it, 86 sampled rects, and no guest needed on camera.

**Caveat, stated:** "present on the disc" is not "inside the archive". These bytes
may live in a different file. The offsets say where to look, and for a port that
ships no pixels, bytes-in-a-shippable-file is the property that matters.

**This does not affect:** the 0x10C binding (strengthened), the sprite-bank
format work, or the withdrawal above — the guest capture is still worth doing,
it is just no longer the only route.

## Palettes are held by three pages; nine of twelve borrow

**CORRECTED below.** This section first claimed palettes live inside the page they
colour. That is true of exactly the three pages I checked, and they are the three
that hold palettes — a selection effect, caught by cow tools.

cow tools resolved `704,0` to archive entry #169 (`0xA9`) `+0x524` and found its
CLUTs at file offsets that convert exactly to the VRAM coordinates logged from the
GPU — the row stride being 0x80 = 64 halfwords = one page row at 4bpp.

**What is true:** CLUT x offsets are always multiples of 16 halfwords
(0, 16, 32, 48 — four palette slots per row, the hardware's CLUT-x-in-units-of-16
rule), and the three pages that hold palettes hold them inside themselves.

**What is NOT true: "palettes live inside the page they colour."** Resolving every
logged CLUT to its owning page:

```
512,0    -> 512,0   SELF          704,0    -> 704,0   SELF
512,256  -> 512,0                 704,256  -> 704,0
576,0    -> 512,0                 896,0    -> 896,0   SELF
576,256  -> 512,0                 896,256  -> 896,0
640,0    -> 512,0                 960,0    -> 896,0
640,256  -> 512,0                 960,256  -> 896,0

self-contained: 3 of 12     palette-holding pages: 512,0 (25 refs), 704,0 (12), 896,0 (13)
```

Nine of twelve pages borrow. I checked 512,0 / 704,0 / 896,0 — precisely the
palette holders — and concluded a property of the population from a sample selected
*by* that property. The counterexample was inside the JSON I had already published.

**The corrected rule is better news:** palettes are concentrated in three pages, not
scattered across twelve, and those three are exactly the three with disc offsets
(`0x1589a524`, `0x15974b24`, `0x15d0ea00`; entries 0xA9, 0x102, 0x1A0 in cow tools'
parser). Every palette in the map is readable from the archive with no VRAM capture.

**And "which rows are palettes" needs no detector at all.** `clut_y` minus the
page's `y` is the row, stated in the map already — four for four against the
bit-15 measurement below. The scan I declined to ship as a tool turned out to be
a tool nobody needed.

**NOT general — the row numbers:**

```
704,0   rows 0, 1, 2, 3
512,0   rows 20, 21, 26, 68
896,0   rows 19, 25, 43, 45
576,0   rows 10, 17, 22, 23, 80
640,0   rows 14, 15
960,0   rows 17, 25, 27
```

Palettes are at page-specific rows. Hardcoding any fixed row reads texels as
colours on every other page.

### Where the texture starts on 704,0, measured

Fraction of halfwords per row with bit 15 (STP) set, page `704,0`:

```
rows  0..26    98.7%      (95-100% every row, 81% at row 26)
row  27        33%        <- cliff
rows 27..63    40.6%
control: page 768,0 (guest bank)   rows 0..26  68.5%   rows 27..63  32.7%
```

Bit 15 set on essentially every halfword is the signature of CLUT entries; ~40% is
what 4bpp texel data gives, where bit 15 is just another nibble bit. The break at
row 27 is exactly where cow tools' render stops being speckle and becomes framed
carved reliefs — **their top 27 rows are palettes drawn as pixels.** The page
therefore holds up to 27 x 4 = 108 palette slots; the 11 logged are only the ones
that capture used.

**This is a fact about `704,0`, not a detector.** Run on all eight pages, the
bit-15 band test finds a band only on `704,0` and `768,0`, and finds nothing on
`512,0` and `576,0` while palettes demonstrably sit at rows 68 and 80 there. A rule
verified on one page and shipped for all of them is the same error as assuming one
file layout for two — do not promote it.

**Also recorded because it had no teeth:** the first discriminator tried was
"palette entry 0 is transparent, so the first halfword of each slot is 0x0000". It
returned 0 of 27 palette rows and 0 of 37 texture rows — identical on both sides,
therefore no information. Bit-15 density replaced it.

## What `texture_clut_map.json` is FOR

It is an **oracle, not a colour source.** It records what the GPU was asked to draw
in one park, from one camera, across 3000 frames that turned out to be mostly
build-mode terrain and UI. Colouring sprites from it would paint whatever happened
to be photographed and go blank on everything else — **while looking finished**,
with no signal for what it missed.

The game does not consult a palette lookup table. Per fable's analysis of the 0x96
containers, each face is 14 bytes — `{i0, i1, i2, 3x(u,v), clut}` under an
`{nfaces, tpage}` header — and the emitter at `0x800115FC` copies `clut` and
`tpage` straight into the POLY_FT3/GT3 words. **The palette for a triangle is
stored on the triangle.** It is a field on the thing that uses it, complete on the
disc, and needs no capture.

**So the map's job is to test that path.** If reading CLUTs off face records
reproduces the 177 (rect -> clut) pairs logged from the live GPU, the implementation
is verified against real hardware behaviour instead of against itself. A decoder
built *from* the map would be self-consistent and would pass every test written
alongside it — which is the failure this file exists to prevent.

177 pairs across 12 pages. Use them as fixtures.

## CORRECTION: 960,0 and 512,256 ARE palette holders. My detector's resolution hid them.

Earlier this file called four of cow tools' minority palette holders parse artifacts,
then revised to "512,256 real, 576,0 and 960,0 unconfirmed". Both were wrong, for a
reason inside my instrument rather than inside the data.

**A CLUT is 16 halfwords. A VRAM row is 64.** The bit-15 test measured density across
a whole row, so it could only detect a page whose palettes fill all four slots of a
row. `704,0` packs them four-across and passed; `960,0` does not and was invisible
**by construction**, not by absence.

At 16-halfword slot resolution:

```
              512,0  704,0  896,0 | 512,256  576,0  960,0
park          379    107    198   | 4        0      22
menu          0      0      198   | 106      0      22
```

`960,0` carries **22 palette-shaped slots**, clustered at rows 93-95 (x=960) and rows
110-115 (x=976, x=992) — a block, not scatter. Chance is ruled out: at the ~45%
bit-15 density of 4bpp texel data, P(>=15 of 16 halfwords set) is about 6e-5, so a
whole page should yield **0.06** false slots. Twenty-two is four hundred times that.

So cow tools' `896,0 -> 960,0` (77 faces) is real, and `640,256 -> 512,256` is real.
`576,0` alone still reads zero, and with a 0.06 false-positive expectation that zero
is meaningful for this park — though the coverage caveat stands.

**The lesson is separable from the scope one above.** Two different ways to publish a
false null, both mine tonight:

| failure | population | instrument | symptom |
|---|---|---|---|
| wrong scope | wrong | fine | measured the right thing somewhere it isn't |
| wrong resolution | right | too coarse | measured the right place too bluntly to see it |

A null from this rig needs **both** stated: what it covered, and what it could have
resolved. I was bitten by each half separately within two hours.

## CORRECTION from fable's audio analysis: what my SPU pitch readings were measuring

Two of my live SPU findings are wrong in ways that matter for the port.

**1. Idle pitch is 0x3FFF, not 0x4000.** 0x3FFF/0x1000 x 44100 = 176,389 Hz exactly.

**2. My "22050 Hz, +1.1%" readings were taken on voices 0-7 — which are XM music
notes, not sound effects.** The voice assignment is:

```
voices 0-9    XM tracker music,  ADSR 0x100F/0x000C
voices 10-23  SFX,               ADSR never written (0/0)
```

I read the pitch registers of the music voices and reported the number as though it
were a sample rate for the sound bank. It is the playback rate of a *tracker note*,
whose base is 8363 Hz x 2^(relnote/12) from the XM sample header.

**The real per-sample base rates are in the bank header**, which is what I said my
instrument structurally could not recover — correctly, because pitch = base x note.
Fable read them from the file instead: each bank is a pair of FOLIO entries
(body, header) listed at 0x800F91D0, header format `{u16 loop, u16 pitch, u32 offset}`
per sample, and across all **132 SFX only two values occur: 0x0400 (11025 Hz) and
0x02E7 (8000 Hz)**.

**Consequence for the port, and it is not cosmetic.** The 192-ish waveforms currently
played back at a flat 22050 Hz are almost certainly the **193 music waveforms** in the
9 VB entries, not the SFX bank. Those have per-sample bases from the XM headers. A
single global rate is right for at most one sample and wrong for the rest — and wrong
in the direction that sounds *plausible* rather than broken, which is why it survived
a listening check.

**What survives:** 22050 Hz was confirmed by ear by the project owner on the waveforms
actually extracted, so it is a real observation about those files. It is the
*generalisation* to "the game's sample rate" that does not hold.

## Model animation tracks: types 8 and 6 decoded

Using fable's container walk (`fable/f/x96parse.py`), verified first: **1,062 sub-entries
walk to exactly the right end byte, 0 mismatches.** Track counts across all 268 mesh
containers:

```
type 8   2295 tracks   all with ZERO keyframes
type 6   1091 tracks   13,425 keyframes
type 3    523          type 5   169    type 4    51
type 0     50          type 7    34    type 2     8    type 1   1
```

### Type 8 = a static bone pose (not animation)

The 32-byte record after the 8-byte track header is a **3x3 GTE rotation matrix**, s16,
4096 = 1.0, row-major. **2,295 of 2,295 are orthonormal** — every row length within 180
of 4096. Track 0 of entry 39 is the identity: `4096 0 0 | 0 4096 0 | 0 0 4096`.

### Type 6 = the animation, 20 bytes per keyframe

```
s16[0]     keyframe time        non-decreasing across the track in 1023/1023 tracks
s16[1]     duration?            0..380, per-track sum median 136
s16[2..4]  translation x, y, z
s16[5]     always 0
s16[6..9]  unit QUATERNION x, y, z, w
```

The quaternion is exact: `sqrt(x^2+y^2+z^2+w^2)` over all 13,425 records has median
**4096.0**, mean **4096.0**, and **100.0% lie within 1%** of 4096. w's median is 3674,
near identity, which is what a set of mostly-small rotations should give.

**Caveats.** `s16[1]` is bounded but unnamed. And **type 1 shares type 8's size formula
(32*count + 0x28) but NOT its content** — the single type-1 track in the archive has
non-orthonormal records, so the shared row in the jump table must not be read as a
shared format.

**Not decoded:** types 0, 2, 3, 4, 5, 7 — 660 tracks, type 3 the largest at 360.
**Not implemented:** this is a format decode only; nothing is wired into the port.

## Bone hierarchy and rest pose — DECODED (2026-09-19)

The 40-byte bone record at `anim32`. Fields found by property, with controls, not by reading hex:

| off | field | how it was established |
|---|---|---|
| +6  | parent index, -1 at root | the ONLY s16 column of 20 that is always in [-1,nbones) AND always acyclic, over 323 multi-bone meshes. s16[2] is 99.4% in range and 0% acyclic; s16[15] and s16[19] are 100% in range and constant 0 — the acyclic test is what separates them |
| +8  | rest rotation, unit quaternion xyzw @4096 | cross-encoding: for all 2,326 bones that also carry a type-8 matrix track, this quaternion reproduces that matrix. Median error 0.000, worst 0.001 |
| +24 | rest translation | median difference 0.0 against the mean of that bone's own type-6 keyframe translations; next best 3-wide window scores 181 |
| +32 | scale | 4096/4096/4096 on all 3,554 bones. Read, not assumed |

Structure: exactly one root in all 331 skeletons, parent index always < child index, deepest chain 9.
Parents preceding children is what lets world transforms compose in a single forward pass.

⚠ +16..+23 is NOT identified. It passes a unit-quaternion test at 100%, but a control with every
column independently shuffled also passes at 81.7% — so the 100% is the column distributions, not a
relationship inside the record. Unnamed on purpose.

### The +4 field of a track header is NOT always a bone index
Measured share landing inside [0,nbones): type 6 and 8 at 100%, type 0/1/7 at 100% but only 85 tracks
between them, and type 2 at 0.0%, type 4 at 0.0%, type 5 at 7.7%, type 3 at 36.1% — chance for a field
of that width. I shipped it labelled `BoneIndex` for every type and corrected it the same night.

## Vertex→bone binding — STILL UNKNOWN, four candidates eliminated

This is the last thing between the port and playing an animation. Ruled out:

1. **The per-block bit array.** 491 of 531 meshes have exactly one block, so one mask cannot select
   individual vertices. Also n0 is 192 and 239 on meshes with 13 and 55 bones — it is not a bone count.
2. **The second vertex array (`vertA`/nvA).** Zero on 530 of 531 meshes.
3. **Sections or groups standing for bones.** Section count equals bone count on 3.0% of meshes, group
   count on 2.6%. The unread u16 in the section header and u32 in the group header are zero.
4. **A bone index in the per-vertex `r12b` table.** s16[5] scores 100% "valid bone index" and is
   CONSTANT ZERO across 4,118 vertices — vacuous. Caught by a shuffled control: assigning vertices to
   their claimed bone put the cluster centroid no closer to that bone than a random shuffle did
   (479.4 vs 479.4, real beat control in 0 of 68 meshes). A 100% pass rate on a constant field is the
   failure mode to watch for here; every "valid index" claim in this file needs the variation check.

Two structural findings that did hold, neither of them the binding:

- **`r12` (the n8b table) is a contiguous range table.** `start[i+1] == start[i] + count[i]` across the
  whole table, 30 of 30 meshes that have 2+ entries. count at s16[0], start at s16[1]. The first start
  is not 0, and the last range ends on n8 in only 5 of 30, so what it partitions is not yet pinned.
- **The trailing u32 list is a set of bone indices**, confirming fable's read of entry 39. 205 meshes,
  every entry valid, 49 distinct values, and no repeats within a list — a set of bones, not a
  per-vertex map.

Next lead is the code, not the file: the descriptor builder 0x8002C5CC allocates the ARS work buffer
and 0x8002CA6C copies n8 vertices into it; whatever picks a matrix for a vertex is in that path.

## The animation code, read off TPW.BIN (2026-09-19)

`tools/disasm.py` disassembles TPW.BIN at a virtual address. The file is raw code with no PS-EXE
header, so the load base cannot be read out of it; it is **0x80010000**, and the tool CHECKS that
rather than trusting it — under the right base the track dispatch at 0x800DDD78 must read as a run of
code pointers, and it does, 15/16. A wrong base fails the check and the tool refuses rather than
printing plausible nonsense at the wrong address.

### There are TWO dispatch tables, not one
| table | entries | what |
|---|---|---|
| 0x800DDD78 | 9 (types 0-8) | the SIZE functions. Types 2/4 share a handler and 3/5 share one, exactly as those pairs share a row in the size table |
| 0x800DDDA0 | 8 (types 0-7) | the per-type EVALUATORS. Type 8 has none, which fits: it is a static rest pose, not something evaluated against a clock |

Evaluators: type 0 → 0x8002ccfc, 1 → 0x8002d248, 2 → 0x8002d4a8, 3 → 0x8002d4e8, 4 → 0x8002d6f8,
5 → 0x8002d738, 6 → 0x8002d8ec, 7 → 0x8002dd24. They are `j` targets inside one function starting at
0x8002cbc4, so they share its frame. Alignment confirmed two ways: the type-7 slot indexes by 16 and
type 7's stride is 16, and the type-6 slot reads a keyframe-shaped preamble at +8 and compares its
first halfword against the clock.

### Keyframe interpolation — FOUND (this was one of the two things blocking playback)
In the type-0 evaluator at 0x8002cf28:
```
t     = ((now - start) << 12) / duration      ; 0..4096
t_inv = 4096 - t
```
then the two bracketing records are blended with the GTE's own interpolation opcodes, **GPF (0x3D)**
at 0x8002cf88 and **GPL (0x3E)** at 0x8002cfb4. The two records read are 0x24 apart, which is type 0's
36-byte stride. So the game interpolates LINEARLY on a 0..4096 weight; the port's nearest-key sampling
is a placeholder for exactly this.

### The +4 header field is an index into an ARS region with 8-BYTE elements
The type-2 evaluator is short enough to read whole:
```
index  = u16 at track+4
dest   = ARS + rec[0x30] + index*8
src    = track + 8 + clock*8          ; stride 8 = type 2's stride
copy 8 bytes src -> dest
```
Type 3 does the same with its own stride. This is the mechanism behind the earlier measurement that
+4 lands inside [0,nbones) only 0.0% of the time for type 2 and 36.1% for type 3: it was never a bone
index on those types. It indexes an 8-byte-element array.

⚠ WHICH array is NOT established. rec[0x30] is an ARS offset (the record's 0x1c..0x30 are ARS-relative;
its 0x08..0x14 are file-relative — two different bases in one record, worth not mixing up). The obvious
guess is the vertex buffer, since vertices are 8 bytes and the index is < nvc on 100% of tracks, but
nvc is large enough to bound almost anything, and the direct test FAILED: the 8 bytes a type-3 track
writes are no closer to the vertex it names than to a randomly shuffled one (549.3 vs 549.1, real beat
control in 10 of 27 meshes — chance). So the destination region is still open.

### ARS and file layout, confirmed off the builder 0x8002C5CC
ARS, in order: blocks×4, bones×32, n8 verts×8, ntracks×4, ntracks×2, then the rec[0x30] region.
File, re-derived independently of x96parse: bone records 40 bytes (n32×5×8 at 0x8002c908), then
r12 at n8b×12, then r12b at m12×12, then the tracks. Matches the existing walk exactly.

`r12` records split cleanly: bytes 0-3 are the {count, start} range chain, and the evaluator copies
only bytes **4..11** into ARS (`addiu $a0, $s1, 4` at 0x8002cab0), which is why the chain lives in the
half the runtime ignores.

## Vertex→bone binding — FOUND. It is weighted scatter, not a per-vertex bone index (2026-09-19)

Every probe that looked for "a bone index per vertex" failed because there isn't one. The design is a
scatter: a small set of animated SOURCE points, each of which pushes a weighted contribution into a run
of vertices. Read off the blend loop at 0x8002e31c.

```
for each r12 entry i:                       ; n8b of them, 12 bytes each
    {count, start} = r12[i]                 ; +0 count, +2 start  -- the range chain
    src = region[i]                         ; 8 bytes: the animated position, written by the track evaluators
    for k in 0..count-1:
        {dest, weight} = r12b[start + k]    ; +0 destination vertex, +2 weight
        vertexbuf[dest].x += (src.x * weight) >> 14
        vertexbuf[dest].y += (src.y * weight) >> 14
        vertexbuf[dest].z += (src.z * weight) >> 14
```
`vertexbuf` is ARS + rec[0x1c], the n8 vertices copied into the work buffer. The `>> 14` sets the weight
scale: **16384 = 1.0**.

### Why this is the binding and not a coincidence
- **Weights are a partition of unity.** Grouping all 20,002 binding records in 239 meshes by destination
  vertex, the weights per vertex sum to exactly 16384 for 15,306 of 15,633 destinations; every one of
  the rest lands on 16383 or 16385 except 14, which are off by two. Integer rounding, and nothing but
  real skinning weights sums to 1.0 that way.
- **The observed weights are the expected fractions**: 16384 (1.0) dominates at 14,568, then 8192 (½),
  4096 (¼), 12288 (¾), and arbitrary values that pair up to 16384.
- **The two tables interlock exactly.** `start[i+1] == start[i] + count[i]` across the whole r12 table,
  and the final run ends exactly on the binding table's length, in 30 of 30 meshes that have both.
  Mean coverage 1.000 — the runs partition the binding table with nothing left over and no overlap.
- **Every destination is a real vertex**: 6,557 of 6,557 below the mesh's vertex count.

### What this means for the port
A vertex is not owned by a bone. It is the weighted sum of however many animated sources reach it, and
the sources are what the tracks drive — which is also why the +4 track header field was measured as a
valid bone index only 0.0-36.1% of the time on types 2/3/4/5. It was never a bone. Playback needs the
scatter loop, not a skin-matrix palette.

⚠ Still open: bytes 4..11 of each r12b record (the builder copies bytes 4..11 of the **r12** records into
ARS at 0x8002cab0, which is a different table — don't mix them), and the exact indexing of the source
region by a track's +4 field. The binding itself does not depend on either.

## What every track type writes to — COMPLETE (2026-09-19)

The eight evaluators differ mainly in their DESTINATION, which is why types 2/4 share a size row and
3/5 share one: same record shape, different target. Destinations read off the code; the ranges are the
independent check, and they separate cleanly in both directions rather than one bound being loose.

| type | stride | +4 field indexes | destination | measured |
|---|---|---|---|---|
| 0 | 36 | a bone | interpolated pose | +4 < nbones 100% (50 tracks) |
| 1 | 32 | a bone | — | 1 track only |
| 2 | 8 | a scatter SOURCE | ARS + rec[0x30] | +4 < n8b 100%, but < n8b only 11.8% for type 4 |
| 3 | 12 | a scatter SOURCE | ARS + rec[0x30] | +4 < n8b 100%, < n8 97.7% |
| 4 | 8 | a VERTEX | ARS + rec[0x1c], direct | +4 < n8 100%, < n8b 11.8% |
| 5 | 12 | a VERTEX | ARS + rec[0x1c], direct | +4 < n8 100%, < n8b 0.6% |
| 6 | 20 | a bone | bone pose | proven by the quaternion cross-check |
| 7 | 16 | a bone | — | +4 < nbones 100% (34 tracks) |
| 8 | 32 | a bone | static rest pose, no evaluator | proven by the cross-check |

Types 2/3 write to sp+0xb0 (= ARS + rec[0x30], the scatter sources) and types 4/5 to sp+0xa8
(= ARS + rec[0x1c], the vertex buffer itself) — 0x8002d4b8 and 0x8002d50c against 0x8002d708 and
0x8002d75c. So **types 4 and 5 are direct vertex animation that bypasses the scatter entirely**, and
types 2 and 3 move the scatter's source points, which then reach vertices through the weighted runs.

That accounts for 751 of the 836 tracks previously filed as undecoded: their destination and index
space are now known, and only their per-record payload past the first 8 bytes is not.

⚠ Do not infer a shared meaning from a shared row in the size table. Types 2 and 4 have identical
strides and headers and write to different arrays; so do 3 and 5. The size table is about how far to
step, and nothing else. This is the second time that table has invited a wrong inference — the first
was types 1 and 8, which share a size and not a format.

## A fixture that employs staff — `states/park_staff.state` (2026-09-19)

fable's guest report closed with "no fixture employs staff, so §6 is SOURCED only". There is one now.
It is built from `states/park_ride.state` by the script below, and reloading the result cold gives:

```
Guards count=1
  Guard pos=(20.50,39.50) state=0 Idle purpose=2 anim=15 tired=16 morale=92 skill=5 hired=147
```

`hired=147` is the day the source state sits on, so the record is this hire and not a leftover.

`btn/` and `states/` are both untracked, so the script lives here rather than only on the box:

```
# sandbox OFF and the tile check nopped:
#   POKE=0x80102D34:0,0x8001E754:0  POKE_HOLD=1
300 308 triangle
600 608 square      # -> Guards / Mechanics / Cleaners / Researchers / Entertainers, Stock 5 each
1000 1008 cross
1400 1408 cross     # -> the Gary Liddon card: Pay Grade 1, Monthly Wage 100
1800 1808 cross     # -> placement tool armed
2400 2408 cross     # -> committed
```
Run it for 4000 frames with `LOADSTATE=states/park_ride.state SAVESTATE=states/park_staff.state`.

Two things cost several attempts, both worth writing down because each failure LOOKED like a different
bug than it was:

- **Square goes straight to the staff catalogue; the laptop route does not work from a script.** Routing
  through `circle` puts up the Information / Build & Hire / … menu whose highlight answers neither the
  d-pad nor the analog stick, so the `down` does nothing and the next `cross` opens *Ride Information*.
  That reads as a wrong menu path — a plausible-looking screen — rather than as a dead button, which is
  why it survived two attempts.
- **I read a stale contact sheet twice.** The renderer died on a missing frame, left the previous run's
  PNG in place, and I drew a conclusion from it — twice, about two different runs. The image was from
  08:31 both times. A render step that fails without removing its old output is a stale-artifact trap;
  the fix was to make the output path an argument so each run writes its own file.

Also confirmed visually in passing: the HUD reads **$48.080**, which is the figure the port's money test
now asserts. The earlier 48,040 was a transcription error of mine, and here is the screen saying so.

## The type-6 keyframe's second field is a DURATION, and the interpolation is linear (2026-09-19)

`animtracks.py` documents s16[1] as "unidentified, 0..380". It is the keyframe's **duration**, and two
independent things say so:

- **The code.** The type-6 evaluator at 0x8002da60 reads `start = u16[key+0]` and `dur = u16[key+2]`,
  skips the key unless `start <= now < start + dur`, then computes
  `t = ((now - start) << 12) / dur` and `4096 - t`, feeds both to the GTE and blends the three halfwords
  at key+4 with GPF (0x3D) and GPL (0x3E). Same shape as the type-0 evaluator.
- **The file, with no code at all.** Across every pair of consecutive keyframes on the disc,
  `key.Time + key.Duration == next.Time` — 12,680 of 12,680, residual exactly zero on every pair. A
  bounded unnamed number does not do that; a duration does.

**The interpolation is LINEAR.** GPF and GPL are the GTE's general-purpose interpolation opcodes and
there is no slerp on this hardware, so matching the console means an nlerp — blend the components, then
renormalise. Being cleverer than the hardware is being wrong.

Two things worth carrying into any port:
- **q and -q are the same rotation.** Blending an opposite-signed pair componentwise collapses toward
  zero instead of rotating between them, so negate one side when the dot product is negative.
- **The span is half-open**, `[start, start + dur)`. At exactly `start + dur` the NEXT key owns the time.

The port's own tests missed the renormalisation entirely until mutation testing: every quaternion case
blended a rotation with itself or with identity, and both are already unit length after the sign fix.
Two rotations 90° apart have a componentwise average of length 0.924, and that is the case that bites.

## All eight track types, structurally — timed vs untimed (2026-09-19)

Applying the type-6 duration test to every type splits them cleanly:

| type | stride | time+dur chains | reading |
|---|---|---|---|
| 0 | 36 | **100.0%** (646 pairs) | timed: 2+2 timing, 32 bytes payload |
| 1 | 32 | 0.0% | untimed. 1 track on the whole disc |
| 2 | 8 | 0.0% | untimed: 8 bytes, **indexed by the clock directly** |
| 3 | 12 | **100.0%** (2,379) | timed: 2+2 timing + the same 8 bytes |
| 4 | 8 | 0.0% | untimed: 8 bytes, clock-indexed |
| 5 | 12 | **100.0%** (1,079) | timed: 2+2 timing + 8 bytes |
| 6 | 20 | **100.0%** (12,334) | timed: 2+2 timing + 16 (translation + quaternion) |
| 7 | 16 | 6.4% | untimed — 6.4% is chance for a field of that width |

So the strides are not arbitrary and the pairs that share a size row are the same idea twice:

- **2 and 4 are untimed, 8 bytes = one position per tick.** The evaluator indexes the record array by
  the clock itself (`sll $v0, $s6, 3` with $s6 the current time, the same register type 6 compares its
  key times against). One sample per tick, no interpolation needed or possible.
- **3 and 5 are the timed versions of exactly those**, 12 bytes = 4 bytes of timing in front of the same
  8-byte payload, interpolated between keys like type 6.
- The difference WITHIN each pair stays the destination: 2/3 to the scatter sources, 4/5 to the vertex
  buffer. So the four types are a 2x2 — {timed, untimed} x {source, vertex}.

Type 6 is the same idea again with a 16-byte payload (translation + quaternion) aimed at a bone, and
type 0 the same with 32 bytes. Types 1 and 7 are bone-shaped but untimed, and their record layout is
NOT established — 1 track and 34 tracks respectively, too few to read a property off.

## The bones→vertices link: still open, and it may not exist (2026-09-19)

Everything else in the animation pipeline closes. Vertices are fully accounted for **without bones**:
position tracks drive scatter sources, the weighted runs spread those into the vertex buffer, and types
4/5 write vertices directly. Posing 30 animated meshes at two times moves 24 of them.

Bones are equally well accounted for on their own side: types 0/1/6/7 all write into the ARS bone-matrix
region (ARS + rec[0x24]), and type 8 supplies the rest pose. What is NOT established is anything reading
those matrices to move a vertex.

Ruled out as a per-vertex bone index, each with its own measurement:
- the per-block bit array (491 of 531 meshes have ONE block, so one mask cannot select vertices);
- the second vertex array (zero on 530 of 531);
- section or group counts standing for bones (3.0% and 2.6%, and their unread header bytes are zero);
- a bone index inside the per-vertex table (the candidate field is constant zero — a shuffled control
  scored identically, 479.4 vs 479.4).

**Leading hypothesis, NOT established: TPW bones do not deform a skin at all.** They position separate
sub-meshes — riders, carriages, attached props — while deformation is entirely vertex animation. It fits
what is measured: the vertex path needs no bones to be complete; the trailing u32 list is a SET of bone
indices with no repeats (205 meshes, every entry valid), which is the shape of "these bones carry
attachments" rather than a per-vertex map; and a container holds many sub-entries that a parent could
place independently.

What would settle it, cheapest first:
1. Find any read of ARS + rec[0x24] outside the track evaluators. If the only readers are the evaluators
   that WRITE bone poses, nothing consumes them within the mesh and the hypothesis is close to proved.
2. Pose a model on hardware with a bone track running and diff the ARS vertex buffer between two frames;
   if it does not change while the bone matrices do, bones are not reaching vertices.

⚠ A detector note: my first search for GTE matrix loads used the wrong opcode bits (0x4a6 instead of
0x246) and returned 78 hits inside functions that set no matrix at all. The corrected pattern gives 304,
and the disassembler cross-check is what caught it — capstone does not decode GTE control ops, so it
prints them as raw bytes, which is itself the tell that a hit is real.

### Update: my "bones place sub-meshes" hypothesis is NOT supported (2026-09-19)

I floated it above and it is attractive — per-vertex skinning would be expensive on this hardware, and
the vertex path is complete without bones. It has now failed two of its own predictions, and one
measurement points the other way:

- **291 meshes (54.8%) have bone tracks and NO position tracks**, and in those the bone track count
  equals the bone count exactly — every bone driven. If bones never reached vertices, over half the
  animated content on the disc would be completely static. That is not credible for a game of animated
  rides, and it is evidence against the hypothesis rather than for it.
- **The trailing bone list does not name sub-entries.** If bones placed sub-meshes, the list length
  should track the container's sub-entry count. It does on 4 of 74 containers (5.4%).
- **No field of the binding record is a bone index**, tested again on the run-less meshes across all six
  s16 and all twelve bytes. The only fields scoring high are constant 0 or near-constant, which is the
  same vacuous pass caught earlier with the shuffled control.

So the position is: the vertex path is fully decoded, the bone path is fully decoded, and **what joins
them is still unknown** — with no working hypothesis, rather than a hypothesis awaiting confirmation.
The honest next step is the hardware experiment: pose a model on the console with a bone track running
and diff the ARS vertex buffer between frames. If the vertices move while only bone matrices changed,
the link exists and is in code I have not read; if they do not, something outside this mesh consumes
those matrices.

### The hardware experiment, first attempt: the anchor did not hold

Plan was to find a bone-only model's vertex buffer in RAM by searching for its own rest geometry, then
diff it across frames. It did not work, and the way it failed is worth recording:

- A **96-byte signature (12 vertices) produced 71 "hits"**, most of them at the SAME address, 0x000156.
  Those meshes' first vertices are zeros, so the signature was 96 zero bytes and matched the first long
  run of zeros in RAM. A signature has to be checked for content before it is trusted as an anchor.
- The surviving high-address hits did not hold either. e0047 sub2 matched for **14 of its 22 vertices**
  and then diverged into (16191, 63, 16191, 63), which is not geometry. Its first 14 vertices are simple
  values like (100, 0, -100) that occur by chance; the match was a coincidence of trivial geometry, not a
  loaded model.
- Requiring the FULL vertex array as the signature returns zero hits for all three candidates, which is
  the correct answer: those models are not resident in a park save state. Ride models load on demand.

So the experiment needs a state with the target model actually on screen, and an anchor that is unique
and non-trivial by construction rather than by luck. Both are checkable up front:
`count(signature) == 1` and a non-zero byte fraction well above half.

## The r12 record's 8 unnamed bytes are the source's DEFAULT POSITION (2026-09-19)

Left open in the binding work as "bytes 4..11, not identified". They are the position an animated source
holds when no track drives it, and the thing that identified them was a bug report from the port, not a
measurement I set out to take.

**Tracks address only the ODD source slots.** Every animated mesh on the disc drives exactly half of its
sources: 32 sources with indices 1,3,5..31; 24 with 1..23; 8 with 1,3,5,7; 2 with just 1. Never an even
index, on any mesh. 24 of the 30 scattering meshes have "undriven" sources by that reckoning.

They are not undriven, they are **pre-seeded**. The builder copies bytes 4..11 of each r12 record into the
ARS source region (`addiu $a0, $s1, 4` at 0x8002cab0, then an 8-byte copy). Those eight bytes are an
8-byte PSX vertex: three s16 in the same coordinate range as the mesh's own vertices (medians 418/359/459
against the vertex median of 262) and a pad that is zero on **all 560 records**.

So the region holds a default position per source; tracks overwrite the odd half; the blend loop reads all
of them. A port that seeds zero instead drags every vertex an undriven source reaches toward the origin.

⚠ **The symptom was reported before the cause was visible to me.** strawberry saw "the animated parts are
stretching from what i assume is 0,0 of the model" in the browser. My own end-to-end check — pose at two
times, require the vertices to differ — passed the whole way through, because vertices being dragged to
the origin DO differ between two times. A check that the pose CHANGES cannot see a pose that changes
wrongly. The rendered contact sheet showed it immediately once I looked, and I had looked at it before
the fix and not registered the spikes as a fault.
