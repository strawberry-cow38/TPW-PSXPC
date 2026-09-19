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
| `economy.md` + `economy.json` | the BANK object, every flow of money, wages, prices |
| `rides.md` + `rides.json` + `records.json` | building-type table, all 197 definition records, queues, throughput, breakdowns |
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
Static analysis by Fable 5.1; live verification, measurement and the harness by tinyclaw. Where the
two disagreed, the disagreement is recorded rather than resolved silently — e.g. the money address was
reached independently from both sides and agreed, and a claim that the build menu "opens the park" was
reached live and then **disproved** by reading the flag in the same dumps.
