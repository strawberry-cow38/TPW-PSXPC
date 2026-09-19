# Emulator instruments

These are the measurement patches this project's findings were produced with.
They live as a patch because `pcsx_rearmed/` is an upstream clone, gitignored
here, and they were only ever uncommitted working-tree edits in it — one
`git checkout` or `git pull` there deletes them silently.

Apply against `pcsx_rearmed` at `8625c39`:

    cd pcsx_rearmed && git apply ../instruments/tpw-instruments.patch
    make -f Makefile.libretro -j4      # NOT plain `make` (it demands ./configure)

Build the frontend separately:

    gcc -O2 -I./pcsx_rearmed/deps/libretro-common/include -o runner runner.c -ldl

## What each one records

| env var | file | records |
|---|---|---|
| `TPW_CLUTLOG` | `plugins/gpulib/gpu.c` | every distinct (CLUT, texture page, UV rect) the GPU actually sampled — the palette a sprite is drawn with, from the draw command rather than guessed |
| `TPW_UPLOADLOG` | `plugins/gpulib/gpu.c` | every CPU→VRAM upload rect with an FNV-1a of the halfwords landed |
| `TPW_COPYLOG` | `plugins/gpulib/gpu.c` | every distinct VRAM→VRAM copy (GP0 0x80), src/dst/size |
| — | `plugins/dfsound/spu.c`, `frontend/libretro.c` | SPU register area exposed as memory id `0x1000`; voice *n* pitch is `regs[((n<<4)|4)>>1]`, rate Hz = pitch/0x1000 × 44100 |

`runner.c` dumps `ram_`/`vram_`/`spu_` every `DUMP_EVERY` frames into its outdir.
Note the loop runs frames 0..N-1, so `DUMP_EVERY=20` needs at least 21 frames.

## Gotchas paid for once already

- The CLUT scan runs **before** the renderer executes the list, so `ex_regs[1]`
  still holds the *previous* list's page. The scanner tracks the page itself as
  it walks. Without that, sprites pair with a stale page — a thousand primitives
  land on a bogus page(0,0).
- Only polygons (0x20-0x3f) and rects (0x60-0x7f) carry a CLUT. Lines (0x40-0x5f)
  also have bit 2 set; including them reads two unrelated words as a clut/texpage
  pair.
- The one copy every run logs — `0 0 -> 0 0, 2x1` — is a genuine no-op:
  `do_vram_copy_pre` returns 0 when src == dst, so the GPU never executes it.
  It is not a bug in the logger and not a real copy.
