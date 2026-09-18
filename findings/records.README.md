# `records.json` is deliberately not in this repository

`findings/fable-scripts/recs.py` parses the game's 197 ride and shop definition
records out of the FOLIO data and writes `records.json`. **That output is not
committed, and should not be.**

## Why

Everything else under `findings/` tells you **where to look in your own copy** —
addresses, field offsets, state transitions, Sony SDK function names. None of it
reconstructs game content.

`records.json` is different in kind. It is all 197 definition records with every
field decoded: capacities, footprints, intensities, entrance and exit offsets,
per-level lifetimes. You could rebuild the game's entire ride roster from it
without owning the game. That is EA's design data in a friendlier format, and
this project does not redistribute it — the same line that keeps disc images,
executables, save states and raw disassembly out (see `.gitignore`).

The rule, stated once so it is reproducible rather than a matter of taste:

> **Does the file let someone reconstruct game content without owning the game?**
> If yes, it stays out. If it only tells you where to find something in a copy you
> already own, it stays in.

## How to get it

Run the parser against your own copy:

```
python findings/fable-scripts/recs.py <path to your TPW data>
```

**The tool is ours; the data is yours.** That is the whole architecture in one
line — and it is why the port reads ride definitions from your game files at load
time rather than shipping a table.
