# Theme Park World (PC) — reverse-engineered file formats

Recovered 2026-05-06. Written out 2026-09-18 because the working code lived in
`/tmp` and is gone; this is the knowledge, so a rebuild is a session and not a
rediscovery.

**Everything here was derived from `demon.MD2` and its companions and was
verified by playing the animations back in Godot.**

---

## 1. `.MD2` is Bullfrog's own format — nothing to do with Quake 2

The extension is reused. This is the single most expensive fact in the file:
every off-the-shelf MD2 tool parses it, produces garbage, and does not error.
An extractor that is confidently wrong costs more than one that visibly fails.

### Header (offsets into the main `.MD2`)

| off | type | meaning |
|-----|------|---------|
| 0x36 | u16 | frame count (25 for demon) |
| 0x44 | u16 | mesh count (9) |
| 0x50 | u32 | texture list |
| 0x54 | u32 | texture name table (25 x 16-byte ptrs into the string blob at 384..884) |
| 0x58 | u32 | static vertex section |
| 0x5c | u32 | **morph direction block** (see §2) |
| 0x60 | u32 | UV section |
| 0x64 | u32 | material entries |
| 0x68 | u32 | face index section |
| 0x70 | u32 | mesh table (9 x 160 bytes) |
| 0x80..0x9c | 8 x f32 | model bounding box |

### Mesh table entry — 160 bytes

| off | type | meaning |
|-----|------|---------|
| 0..15 | — | scene-graph hierarchy pointers |
| 16..79 | mat4 | transform, column-major |
| 80 | u32 | texture index |
| 84 | u32 | name offset |
| 88 | u16 | vertex count |
| 90 | u16 | material count |
| 92 | u16 | face count |
| 94 | u16 | vertex-order length |
| 96 | u32 | vertex offset |
| 104 | u32 | UV offset |
| 108 | u32 | material offset |
| 112 | u32 | face offset |
| 116..147 | 6 x f32 + pad | per-mesh bounding box |
| 148 | u32 | vertex-order offset |
| 152..159 | — | unknown (possibly a material index range) |

- **static verts** 12 bytes each (3 x f32); some meshes carry 12 bytes of trailing pad
- **UVs** 8 bytes each (2 x f32); 1556 entries for demon = sum of vertex-order lengths
- **vertex-order** u16 indices, 1556 for demon
- trailing region 72072..73832 = 22 x 80-byte scene-instance records (decoration
  placements, each with a mat4). Unrelated to animation.

## 2. The animation is BLEND-SHAPE, not skeletal

This is why it looks broken: there are no bones to find, so anything hunting a
skeleton comes back empty and anything assuming one produces confetti.

- `0x5c` holds **one unit-length direction vector per static vertex**, parallel
  to the static vertex section (same offsets relative to their own bases).
- The **curves live in companion overlay `.MD2` files** sitting next to the main
  one, not inside it.

Each overlay is a list of 20-byte channel records:

```
[u32 pad=0][u16 nTimes][u16 nTargets][u32 ptrTargets][u32 ptrTimes][u32 ptrValues]
```

- `ptrTargets` -> `u16[nTargets]`, global vertex indices into the flat
  `mesh0 ++ mesh1 ++ ...` array
- `ptrTimes` -> `u16[nTimes]`, keyframe time units
- `ptrValues` -> `f32[nTimes * nTargets]`, row-major: `values[k * nTargets + slot]`

```
animated_pos[v, t] = rest_pos[v] + lerp(scalar[v], t) * morph_dir[v]
```

⚠ Some overlays have `nValues != nTimes * nTargets` (a 2.68 ratio was seen once).
Clamp on read rather than trusting the product.

### Overlay -> RSE `TRIGANIM` id (demon ride, confirmed against the runtime)

| id | file | what it is | channels | maxT |
|----|------|-----------|----------|------|
| 0 | `demoni.MD2` | idle | 12 | 90 |
| 2 | `demonl.MD2` | load loop | 10 | 170 |
| 4 | `demonc.MD2` | **THE DROP** | 39 | 200 |
| 5 | `demonr.MD2` | return up | 4 | 60 |
| 9 | `demonb1.MD2` | broken | 7 | — |
| 10 | `demonb2.MD2` | repaired | 4 | — |

id 3 has no dedicated overlay and falls back to `demoni`.

Time units -> ms: `dur = maxT * 25ms` was the working guess; an RSE `TRIGANIM`
duration hint overrides it where present.

## 3. Ride signs are rendered at runtime — there is no texture to extract

No `.wct` will ever appear for a sign. Stop looking.

`.sgn` per ride: u32 header `(100, 1, 256, 256)` — the last two are texture
width/height — then records of font display name (`"LinusPlay AOE"`), TTF
filename (`"LINUPA__.TTF"`), and a 4-int bbox. Multiple font records per ride.

At runtime the engine reads `.sgn`, picks the record for the locale, renders the
ride name — from `.sam` `Info.RideTypeStringIndex` into the localised string
tables under `English/`, `French/`, `Danish/`, `German/`, `Swedish/` — with that
TTF onto a 256x256 dynamic texture, and binds it to the sign quad.

## 4. What this cost, and what to do differently

- The models were tractable by **static analysis** because the PC format really
  is a vertex array. The **PS2 version is not**: VIF1 unpacks into VU1 memory and
  a per-game microprogram emits the primitives, so the bytes on disk only become
  geometry after code runs. Static extraction there means reimplementing an
  assembly program you have never read, and it yields exactly the "extracted
  something, utterly broken" result. Capture at the hardware boundary instead —
  `chaoticgd/vutrace` exists for precisely that, with `Goatman13/ps2_ida_vu_micro`
  to find the microcode in the ELF. (tinyclaw, 2026-09-18.)
- The thing that made the PC work painful was having **no running game to diff
  against**. That is the same gap the Blockheads oracle fills, and the reason the
  PS2 route is attractive despite the harder model format: pcsx2 is a live
  reference.
- Ghidra was the right tool for the **ride scripts** and the wrong one for
  geometry that only exists at runtime.

## 5. If a PS2 oracle gets built, the first question to measure

⚠ **Not** *"does pcsx2 run headless"*. The narrower and decisive one is:

> **does `vutrace` emit a usable VU1 trace with no GPU?**

(tinyclaw, 2026-09-18.) That is the only part a phase one actually needs, and it
is cheaper to answer.

The reason it is plausibly yes: VIF1 unpacks into VU1's memory, VU1 runs the
transform, and only *then* does it emit primitives to the GS over GIF path 1. So
the geometry exists at **VU1's output — upstream of anything rasterising**. VU1
is a vector coprocessor on the EE side, not the graphics chip. GS dumps and VU1
traces are therefore different asks with possibly different requirements, and
**VU1 is the half that matters for model formats**.

⚠ That is reasoning from the shape of the hardware, **not a measured result**.
Nobody has run it. If it gets answered, replace this section with the answer.

Related: official PCSX2 is x86-64 only (v2.8.1), ARM64 exists only in community
forks of disputed provenance, and vutrace is a modified PCSX2 so it inherits
that. Capture therefore wants an x86-64 machine; the artifacts are files and can
be consumed anywhere, which is what makes the fixture-generator shape work.

## 6. How big are the artifacts? (bounded, not measured)

Asked 2026-09-18. **Sourced facts, then arithmetic, kept apart on purpose —
mixing them is how a disk budget comes out wrong.**

**Sourced.** vutrace's trace format **v3 stores only registers and sections of
memory that were MODIFIED** — it is delta-encoded, and that exists because
earlier versions were too large. GS dumps ship in three forms: uncompressed
`.gs`, `.gs.xz` (LZMA, high ratio), `.gs.zst` (zstd, balanced); the documented
xz pause is *"a couple of seconds for a single frame, easily dozens for longer
multi-frame dumps"* — which says multi-frame is much bigger without saying by how
much. (tinyclaw found these; no published byte figures exist for either.)

**Derived from hardware, not measured.** VU1 carries 32 x 128-bit VF registers
and 16 KB of data memory, so a naive per-instruction full snapshot is ~16 KB+,
and at tens of thousands of VU1 instructions per frame that is hundreds of MB to
GB — which is precisely why the deltas exist. Post-delta a modified register is
~16-32 bytes, putting a traced frame at an expected **single-digit to low-tens of
MB**. Nobody has opened such a file.

**⭐ The sizing question is the wrong shape anyway.** Both are **per-frame
captures, not recordings** — you trace *a frame*, not a play session. For model
work you want one object's microprogram executing once: *"what does VU1 emit when
it transforms THIS model"*. And the **committed artifact is not the trace**, it
is what was distilled from it — vertex layout, unpack pattern, a golden. Large
transient files on the capture machine, small permanent ones in the repo. That
makes it a don't-let-scratch-accumulate problem rather than a storage one.

**The real number is one capture and an `ls -la`.** Worth doing before budgeting;
not worth doing before deciding.
