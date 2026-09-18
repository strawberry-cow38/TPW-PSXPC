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

Early. Nothing is claimed as working yet.

## Conventions

Every claim recorded here is marked **sourced**, **derived** or **unmeasured**.
That distinction is deliberate and load-bearing: a note that blends the three is
how the next person inherits a previous person's confidence along with their
mistakes.

## Licence

MIT — see [`LICENSE`](LICENSE).
