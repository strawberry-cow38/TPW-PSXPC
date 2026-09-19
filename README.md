# TPW-PSX — tooling and findings

Reverse-engineering notes and harness for **Theme Park World (PSX, PAL, SLES-026.88)**.

## No game data, no firmware, ever
This repo contains **tooling and findings only**. It ships no disc image, no executable, no assets,
no PlayStation BIOS, and no save states (a save state embeds the game's own RAM, so it is game data).
`.gitignore` refuses those outright so a careless `git add -A` cannot turn into a distribution problem.
Disassembly is excluded for the same reason — it is the game's code in another form.

Bring your own disc and your own BIOS.

## What is here
- `runner.c` — a ~250-line headless libretro frontend: boots a PSX disc, saves frames, dumps RAM and
  VRAM on a schedule, scripted input, save/restore, and memory poking (`POKE_HOLD` re-applies each
  frame, which is required for anything the game rewrites every tick).
- `oracle.py` — records and checks **fixtures**: a save state + an input script + the trace the real
  console produced. A reimplementation must reproduce the trace. Fixtures are teeth-tested: each one is
  verified to FAIL when the behaviour it covers is removed.
- `sigmatch.py` / `funcmatch.py` — match PsyQ library signatures against the image to separate Sony's
  library from game code.
- `boot_practice.txt` — deterministic cold boot to Practice Park.
- `fable/` — analysis reports (people AI, behaviour, economy, rides, park-open), each marking every
  claim READ or GUESS, plus the scripts that produced them.

## Verified facts worth knowing
- The image loads verbatim at `0x80010000` — **100.00%** match against live console RAM.
- The sim ticks **25/second** (one per 2 PAL frames); a game day is **99 ticks (~3.96 s)**.
- Money is stored at **ten times** the displayed figure.
- **The game is analog-native** and enables DualShock analog mode itself; the d-pad barely moves the
  camera. Getting this wrong produced three separate confident wrong conclusions.
