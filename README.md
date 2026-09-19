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

## Conventions

Every claim recorded here is marked **sourced**, **derived** or **unmeasured**.
That distinction is deliberate and load-bearing: a note that blends the three is
how the next person inherits a previous person's confidence along with their
mistakes.

## Licence

MIT — see [`LICENSE`](LICENSE).
