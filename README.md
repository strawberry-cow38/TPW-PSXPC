# TPW-PSXPC

Bringing **Theme Park World**'s PlayStation release to PC — reverse-engineering
notes and tooling.

> ⚠ **This repository contains no game data and never will.** No executable, no
> assets, no archives, no disc images. Everything here is our own documentation
> and our own code, and anything built from it requires **your own legally
> obtained copy** of the game. Same posture as ScummVM, OpenRCT2 and OpenMW.

## Scope

**The PSX version, targeting PC. That is the whole scope.**

Explicitly **not** in this project:

- the native Windows release, its file formats, or anything derived from them
- the PS2 release
- OpenTPW, which is a reimplementation of the *PC* version

Those are different games' worth of different data, and mixing them is how a
project ends up half-doing three things.

## Why the PSX release is the tractable one

Of the three releases it is the **best-tooled for decompilation by a wide
margin**, and the reason is hardware:

- the PSX **GTE is fixed-function** — perspective transform, rotation, lighting,
  depth cue and fixed-point matrix ops, fully specified publicly. Model data
  reaches it directly.
- there is **no microcode wall**. The PS2 equivalent (VU1) is an independent
  processor running per-game microcode in a custom assembly, commonly built
  separately from the ELF — which is why static extraction from a PS2 build
  produces garbage and why that release is not the starting point.
- the matching-decomp scene around PSX is mature: **splat**, **maspsx** (which
  replicates the SDK's custom `aspsx` assembler), **decomp-permuter**, **m2c**
  and **objdiff**, with the original **PsyQ GCC** obtainable and pinnable.
  Precedents go the whole way to byte-identical rebuilds.

⚠ The PSX build is likely a **reduced** port of the PC game. Treat any number
taken from it as describing the PSX version, not as defining the original — until
that is actually checked.

## Status

Early — but the chain is joined end to end, verified rather than asserted.

Run headless on 2026-09-18 against a real PAL disc:

```
[tpw] data: Found Theme Park World (PSX) (PAL) -- matched your disc image.
[tpw] launcher said variant=SLES-026.88, we identified SLES-026.88
```

That single line exercises every seam built so far: the launcher identifies the
user's own copy by hash, hands the answer to the game as environment, Godot
starts, and the **engine-free** sim in `core/` ticks at the tick rate *that
variant* carries. Each piece was built and tested separately; this is the first
thing showing they meet.

| part | state |
|---|---|
| `core/TPW.Sim` | fixed-point arithmetic + park clock, 8 tests, no engine |
| `core/TPW.Launcher` | game-data identification + launcher rules, 17 tests |
| `launcher/` | Avalonia, released as v1, identifies a copy and refuses unknown ones |
| `game/` | Godot 4.6 C#, builds and runs, reports the identified variant |
| the actual game | **not written.** No park, no guests, no rides. |

⚠ The last row is the honest one. What exists is a verified skeleton and a real
launcher; what does not exist is Theme Park World.

## Building

⚠⚠ **`dotnet build` AT THE ROOT DOES NOT BUILD THE GAME, AND SAYS "Build succeeded".**
`TPW.sln` does not contain `game/TPWGodot.csproj`, so a solution build leaves the
assembly Godot loads — `game/.godot/mono/temp/bin/Debug/TPWGodot.dll` — exactly as
it was, reports success, and the next run of the port executes your PREVIOUS code.
Measured: source at 11:44:54, assembly still 11:39:42 after a clean solution build;
`dotnet build game` moved it to 11:48:15.

    dotnet build game     # the port — ALWAYS this before running or rendering it
    dotnet build          # core + launcher + tools only
    dotnet test           # the three test projects

This is not theoretical. It cost a debugging session here: a rendered frame was
missing a widget that was present in the source, and the obvious readings — a
layout bug, a visibility bug — were both wrong and both took time to rule out.
A stale build is invisible from the output; it looks like your change not working.
The launcher already does the right thing (it builds the game project by name),
so this bites developers at a terminal, not users.

## Driving it headless

Every part of the port can be built, run and photographed from a command line, which
is how nearly every claim in `findings/` was checked. The pattern:

    TPW_DATA=/path/to/tpw.iso xvfb-run -a <godot> --path game --rendering-driver opengl3 -- \
        --no-boot --park=203 --park-open --shot=/tmp/a.png:1200

`--no-boot` skips the intro, `--park=N` opens a map, `--shot=PATH:FRAME` photographs it
and exits. On a headless box `xvfb-run -a` is required and `TPW_DATA` must point at the
disc image, or the port starts, prints "No game data found" and photographs an empty
launcher.

⚠ **A CAPTURE IS NOT FRAME-DETERMINISTIC.** The park's catch-up loop runs up to ten
simulation frames per drawn one, sized from the real frame delta, so how far the sim has
got by frame 1200 depends on how fast that machine drew the first 1199. Two runs of the
identical command put a ride on animation tick 14 and tick 29. **Do not use a frame
number as a control** — address the state instead (`--shot=PATH:running`), or log the
state you care about and check the two runs agree on it.

### Building a park without a mouse

| flag | what it does |
|---|---|
| `--park=N` | open map entry N |
| `--park-open` | open the park to guests (the bus only brings anyone to an OPEN park) |
| `--park-place=entry,x,z,rot;...` | place attractions at a footprint corner |
| `--park-lay=x0,z0,x1,z1;...` | lay path runs as the path tool would |
| `--park-queue=entry,x,z,rot:cx,cz:...` | place a ride, then click its queue tool at each tile |
| `--park-track=...` / `--park-select=x,z` / `--park-slider=...` | the track builder and the attraction panel |
| `--park-view=x,z,yaw,pitch,distance` | put the camera somewhere specific |
| `--park-hire=kind,x,z;...` | hire staff (0 mechanic, 1 entertainer, 2 cleaner, 3 guard, 4 researcher) |

⚠ **A QUEUE MUST END ON A PATH TILE.** A queue run that stops one tile short of your path
looks connected and can never be routed into — see `findings/paths.md` §0 item 7. It is
the game's own rule and it will cost you an afternoon.

### Test hooks, which are not rules

These exist so a behaviour can be watched without playing the game to it. Each one moves
a number the real rules already read; none of them changes a rule.

| flag | why it exists |
|---|---|
| `--park-guests=N` | keep N guests in the park regardless of the bus |
| `--park-break=entry` | wear a ride out now, so a mechanic can be watched fixing it |
| `--park-log-rides` | every ride's status changes, its animation clock, the mesh the RENDERER holds, and every staff member's state changes |
| `--shot=PATH:running[:N]` | photograph when a ride is mid-cycle rather than at a frame number |

### Reading the park back

The shot prints one line per run: guests, how many are queueing, riding and served, the
pathfinder's outstanding searches and free pools, how many connected pieces the walkable
map is in, and then a line per ride and per staff member.

⚠ **`failures N stranded / M SAME AREA` is two numbers on purpose.** A single total of
failed routes can never be zero in a park with an island in it, so it only ever climbs and
says nothing. The second is the one to gate on: it means the walker and its destination
were in the same connected piece and the search still found nothing, and it should be 0.

## Conventions

Every claim recorded here is marked **sourced**, **derived** or **unmeasured**.
That distinction is deliberate and load-bearing: a note that blends the three is
how the next person inherits a previous person's confidence along with their
mistakes.

## Licence

MIT — see [`LICENSE`](LICENSE).
