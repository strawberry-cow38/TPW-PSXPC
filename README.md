# TPW-PSXPC

Reverse-engineering notes and tooling for **Theme Park World** (Bullfrog / EA,
1999), working from the **PC** and **PSX** releases.

> ⚠ **This repository contains no game data and never will.** No executables, no
> assets, no archives, no disc images. Everything here is our own documentation
> and our own code. Anything built from it requires **your own legally obtained
> copy** of the game — the same model as ScummVM, OpenRCT2, OpenMW and OpenTPW.

## Why this exists

Theme Park World sits in an awkward gap. Theme Park (1994) and Theme Hospital
(1997) are DOS-era, so they get re-released wrapped in DOSBox. TPW is 1999
Direct3D — too late for DOSBox, too early to simply run on a modern machine — and
it is routinely skipped when Bullfrog's back catalogue is reissued. A game that
nobody reissues, that runs badly on anything current, and whose file formats
almost nobody has decoded is a game that quietly stops being playable.

## Why two platforms

Each release is best at a different job:

| release | good for | why |
|---|---|---|
| **PC** | assets, and the large maps | formats substantially decoded; the maps are the big ones |
| **PSX** | readable algorithms | best-tooled decomp scene of the three; **no VU1 microcode wall** — the GTE is fixed-function and fully specified, so model data reaches it directly |
| PS2 | the extra rides and content | needs a VU1 tracer first; a later job, not a foundation |

The PS2 release is widely considered the best version and is the least preserved,
but its geometry only exists after a per-game VU1 microprogram runs — which is why
static extraction from it produces garbage, and why it is not the starting point.

⚠ The PSX sim is likely a **reduced** port. Treat numbers taken from it as
approximating the PC logic rather than defining it, until that is checked.

## What is here

- [`docs/pc-formats.md`](docs/pc-formats.md) — the PC `.MD2` model format (which
  is **Bullfrog's own, unrelated to Quake 2** despite the extension), its
  blend-shape animation and overlay files, the `TRIGANIM` id mapping, and the
  runtime-rendered ride signs. Derived from `demon.MD2` and verified by playing
  the animations back.

Each section states whether it is **sourced**, **derived**, or **unmeasured**.
That distinction is deliberate: a note that quietly mixes the three is how a wrong
conclusion gets inherited.

## Prior art

[**OpenTPW**](https://github.com/OpenTPW/OpenTPW) (MIT) is an existing C#
reimplementation and is further along than it looks: container archives, EA's
Refpack/LZSS compression, textures, the BFMU/BFST/SAM string tables, saves, and a
ride-script VM with the full 105-opcode table. It requires an installation of the
original game, and it is the obvious thing to build with rather than duplicate.

Its VM names `TRIGANIM` as opcode 16 but has no handler for it; our notes cover
what a `TRIGANIM` argument actually resolves to. The two halves meet exactly
there.

## Licence

MIT — see [`LICENSE`](LICENSE). Chosen to match OpenTPW so code can move between
them; change it if you would rather something else.
