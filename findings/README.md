# Findings — Theme Park World (PSX, PAL, SLES-026.88)

Analysis reports and the machine-readable data behind them. **No game bytes here** — these are
derived findings: addresses, formulas, structure layouts.

Every report marks each claim **READ** (taken from instructions at a quoted address) or **GUESS**
(with a confidence level), and each opens with corrections to its own earlier reports. Treat a GUESS
as a lead, not a fact.

| file | what |
|---|---|
| `ai_out.txt` | person state MAP — classes, vtables, Update dispatch, per-state handlers |
| `behaviour.md` + `states.json` | what each state DOES, exits, fields read/written |
| `staff.md` + `staff-mutations.json` | hire-to-placement lifecycle, staff virtual walk speeds, class sprite resources and frame tables, mechanic job/leave points; source disagreements and restoring mutation sweep |
| `visitor-rest.md` | visitor remainder; decoded type-4 need coefficients for 37 definitions, influence producers 2/4, full message table, wander tables, retained binary disagreements and mutation checks |
| `economy.md` + `economy.json` | the BANK object, every flow of money, wages, prices |
| `rides.md` + `rides.json` + locally generated `records.json` | building-type table, 197 definitions selected by the original parser, queues, throughput, breakdowns; phase report corrects the full count to 244 |
| `animation-phases.md` + `ride-phase-lengths.md` | 88-byte runtime descriptor, disc lengths for 83 rides, conditional run times for 59 flat rides; reproducible extractor and mutation checks in `tools/` |
| `ride-panel.md` + `ride-panel-mutations.json` | speed/capacity/duration settings, upgrade gates and costs, intensity consumers, retained source disagreements; restoring mutation sweep |
| `ride-classes.md` | Tour/track/coaster loading, trip control, unloading, wear, coaster connection veto; binary disagreements retained for review in rides.md §0; controller ports and mutation runner |
| `shop-stock.md` + `shop-stock-mutations.json` | a shop has NO stock; the feature's cleanliness byte (signed, clamped), its consumption, the handyman's free refill, the absent shop employee, every low-stock surface; binary disagreements with behaviour.md/rides.md retained in §0 |
| `happiness.md` + `happiness-mutations.json` | every writer and reader of guest happiness V+0x59: the eleven writers with sign, size and address, no clock, what it multiplies (the wants, the type-4 score bonus), what it decides (departure, litter, bubbles, the condition code, statistic 46, the monthly ring, a stall's satisfaction bar) and what it does not (the park rating, arrivals); the four scans behind "every"; source disagreements in §0 |
| `litter.md` + `litter-mutations.json` | forty persistent litter pieces, guest producers versus the misery particle, terrain drawing, concrete handyman claims/cleanup, nearby happiness/nausea costs, statistic 12, no direct arrival penalty, dead state 25 and no mowing; source disagreements retained in §0 |
| `statistics.md` + `statistics-rules.json` + `statistics-mutations.json` | all 72 advisor-cache slots and their readers, 125 decoded disc rules with string IDs, separate refresh/rule clocks and event counters, retained source disagreements, restoring mutation sweep |
| `parkopen.md` | what gates guest arrival |
| `disc-check.md` | why Main Game and Load Game are greyed: a disc check at boot, not save data |
| `psyq-named-functions.json` | 219 Sony PsyQ library functions located in the image by signature |

## Facts verified on the running console, not just read
- The image loads verbatim at `0x80010000` — **100.00%** byte match against live RAM (controls: the
  off-by-four base scores 17.6%).
- The sim ticks **25/second**, one per two PAL frames — measured across 90,000 frames, zero variance.
  A game day is **99 ticks (~3.96 s)**; a month is ~30 days.
- Money is stored at **ten times** the displayed figure (500000 renders as "$50,000").
- Sony's PsyQ library occupies `0x800C0498..0x800DB838`; everything below is game code.
- **Rides earn nothing** — shops and sideshows do. An open park with nothing built gets **zero**
  guests, because arrivals are computed from what is built.
- The game is **analog-native** and enables DualShock analog mode itself.

## Provenance
The phase-table investigation explicitly includes a narrow per-ride timing table, as requested.
It contains lengths and timing calculations, not the excluded full `records.json` or raw assets.
Its direct archive scan also found 47 short Feature definitions omitted by `recs.py`'s 64-byte guard:
197 is that script's output count; the disc has 244 type-1..8 definition headers.

Static analysis by Fable 5.1; live verification, measurement and the harness by tinyclaw. Where the
two disagreed, the disagreement is recorded rather than resolved silently — e.g. the money address was
reached independently from both sides and agreed, and a claim that the build menu "opens the park" was
reached live and then **disproved** by reading the flag in the same dumps.
