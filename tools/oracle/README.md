# Running the real game headlessly

The port is written from `findings/`, which is read from the executable. This is the other half: a way
to **run the actual disc** on a box with no screen, drive it with a scripted pad, and dump frames,
RAM and VRAM on a schedule. It is how `findings/measured.md` was produced and how a claim gets
settled when two readings of the code disagree.

⭐ **When to reach for it.** Anything where the answer is "what does the game DO", as opposed to "what
does the code SAY". Today it settled whether the park can be opened from the gate (it can — ✕ on the
selected gate gives a context list with **Open**), after two people had read the dispatcher and come
to opposite conclusions.

## What you need

| piece | where | note |
|---|---|---|
| `pcsx_rearmed` libretro core | built from source | `make -f Makefile.libretro platform=unix -j4`. Builds clean on aarch64 with its ARM64 dynarec, so it runs at full speed. No Qt, no X, no GL, no SDL. |
| `runner.c` | here | ~370 lines. dlopens the core, drives `retro_run()`, writes PPM frames and dumps memory. `gcc -O2 -o runner runner.c -I <core>/deps/libretro-common/include -ldl` |
| a PS1 BIOS | **not in this repo and never will be** | `scph5502.bin` for PAL/EU, crc32 `d786f0b9`. Console firmware — it is on nobody's disc. Put it in `$SYSDIR`. |
| the disc | your own | a 2048-byte/sector `.iso` loads directly; a `.cue` next to a missing `.bin` does not, and the core's error for that is "unsupported/invalid CD image". |

⚠ **DuckStation is not the route here.** Its AppImage needs a newer glibc than AL2023 has, and in a
container it still hangs before emulating — it is a desktop GUI app and wants a window manager. The
libretro path is strictly better anyway, because it makes dump timing programmable.

## Running

```
SYSDIR=<dir with the bios> INSCRIPT=<input script> [LOADSTATE=<state>] [SAVESTATE=<state>] \
  ./runner <core.so> <game.iso> <outdir> <frames> <frame_every>
```

Input script lines are `<start_frame> <end_frame> <button>`; names are `cross circle square triangle
l1 r1 l2 r2 l3 r3 start select up down left right`.

Save states turn "replay 26,000 frames of intro and menus" into "resume in 1,200", so experiments
become branches off one saved point. `POKE=0xADDR:VALUE` writes a word after the restore, and
`POKE_HOLD=1` re-applies it every frame.

## The traps, each of which cost a full debug cycle

1. ⚠ **libretro joypad ids are SNES-style.** `_B` is **CROSS**, `_A` is **CIRCLE**, `_Y` is SQUARE,
   `_X` is **TRIANGLE**. A menu that looks frozen is this until proven otherwise.
2. ⚠ **A HELD button produces no press EDGE**, so menus never advance. Pulse it: ~8 frames down.
3. ⚠⚠ **YOUR MEASUREMENT WINDOW MUST OUTLIVE THE PRESS.** Dumping at frame 450 cannot see a press at
   500. This is embarrassing and it has happened twice in one session — the run *looks* like a clean
   null, four times over, because every branch is identical up to the frame you looked at.
4. ⚠ **Menus swallow input during their opening animation** — up to ~360 frames (~7 s at 50 Hz). So
   when blind-driving a UI, **sweep the DELAY** before concluding anything about which button is
   needed. A wrong claim once went out because three successful branches all pressed a direction AND
   all waited longer; the delay was the cause and the direction was noise.
5. ⚠ **Replay determinism requires `pcsx_rearmed_drc_thread = "disabled"`** (the runner answers this).
   The default compiles on a background thread and long replays drift. Verified determinism over 1,300
   frames once and generalised; a 12,000-frame fixture then gave FAIL/FAIL/PASS. **Verify
   reproducibility at the length you intend to use.**
6. ⚠ **Diff named fields or the framebuffer, never whole RAM.** The simulation is deterministic; the
   periphery is not. 172 bytes of 2 MB differ run to run — BIOS scratch and audio/peripheral timing.

## The method that works: branch once per button, against a null control

Restore one state, run one branch per button with a single pulse, and diff each final frame against a
branch that pressed **nothing**. Measured this way from a practice park with the gate selected and the
root radial open:

| button | changed | what |
|---|---|---|
| ✕ cross | yes | the context list at the cursor |
| △ triangle | yes | back / closes |
| L1 / R1 | yes | camera |
| Start | yes | PAUSED |
| ○ circle, □ square, Select | **no** | nothing, in this state |

⭐ **The five that move are what make the three zeros mean anything.** Without them, "○ does nothing"
and "○ never reached the pad" are the same observation — and a wrong input conclusion has already been
published here off an instrument that was simply not connected.

## Where it stops

Scripted input is excellent for *stateless* actions and close to useless for anything **cursor-driven**:
the whole sequence is written before the run, one frame is visible every few hundred, and nothing is
readable about what is highlighted. A 30-frame d-pad nudge — about the smallest a pulse can express —
throws the TPW camera clear across the park, so aiming at a tile is a resolution limit, not a missing
button.

⭐ **If two different inputs produce byte-identical frames, stop sweeping.** The control is not wired
the way you think. Get a person who knows the game to answer, or ask them for a save state. Five
minutes of someone playing has repeatedly beaten hours of blind sweeps here.
