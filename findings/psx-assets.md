# PSX disc assets — what is established, and what is ruled out

Scope: where the art lives on the PAL disc (`SLES_026.88`), and what its container looks like.
Method: structural tests against the real image, each one written so a wrong hypothesis fails it.
**MEASURED** = checked against the disc. **OPEN** = not established; do not build on it.

## 1. The disc map (MEASURED)

| file | size | what it is |
|---|---|---|
| `SLES_026.88` | 49,152 | boot executable, PAL. `SYSTEM.CNF` names it. |
| `FOLIO.GAZ` | 16,164,376 | **the asset archive** |
| `LEGAL.GFX` | 165,403 | a **Targa**, 320x256, 15-bit, uncompressed |
| `TPW.BIN` | 1,065,308 | MIPS overlay code (opens `sw ra,-4(sp)`) |
| `TPW.OVL` | 43,967 | overlay table |
| `ADVISOR.TPW` | 346,816,512 | **XA audio**, not data — see §4 |
| `*.STR` x7 | ~58 MB | FMV |
| `ZZZZZZZZ.ZZZ` | 27,648,000 | **100% zero bytes**, disc padding |
| `AAAAAAAA.AAA` | 2,048 | padding |

Two thirds of the disc is ruled out by the two rows in bold. The art is inside 8.5 MB of `FOLIO.GAZ`.

## 2. `FOLIO.GAZ` layout (MEASURED)

    u32 count = 422
    u32 unknown = 23          # OPEN: meaning not established
    count x { u32 offset; u32 size }
    payloads, each padded up to a 0x800 boundary

Not a guess that looked right — a hypothesis that survived four independent falsifiers on the real
file: **0 out of bounds, 0 overlapping, strictly ascending, and all 422 offsets aligned to 0x800.**
A wrong field layout does not produce that; misread the stride or the field order and offsets
scatter or collide. The alignment is the strongest of the four, because nothing about a wrong
reading would put 422 numbers on multiples of 2048.

⚠ **Only 53% of the file is reachable from the table, and that is correct.** Past the last entry
(ends 0x825288) the remaining 7.6 MB is the string `" BOYS ROCK OUT - TPW BOOT "` repeating to the
end. A reader that scans the whole file for payloads finds megabytes of it.

Entry composition: **268 containers** (first word `0x96`) and **154 others**, of which 9 are `pBAV`
(VAB sound banks) and 9 start `"Exte"` (text).

## 3. Ruled out (MEASURED negatives — each cost real time, don't repeat them)

- **No TIM anywhere on the disc.** Zero, in every file. The scan was structural: a TIM's block
  length must equal `12 + w*h*2` exactly. ⚠ A magic-bytes scan instead returns **four hits inside
  `FOLIO.GAZ`, all of them ordinary numbers in the archive's own offset table.** Four bytes is not a
  format check; any table of small integers eventually produces them.
- **The `0x96` container is not a simple sub-table.** Seven candidate layouts were scored across all
  268 containers (header 28/32 bytes, stride 8/12/16, offset-first and size-first). Best score
  **5/268**. Offsets-only, with sizes taken as the gaps, scored **7/268**. A correct reading would
  pass nearly all 268; these are noise.
- **The uniform entries are not plain bitmaps.** Twelve entries are *exactly* 131,156 bytes
  (indices 6-15, 418, 419), which is 131,072 + 84 — and 131,072 is precisely a 256x256 16-bit PSX
  texture page, so this looked certain. It is not. Stride was derived from the data by row-to-row
  difference, which drops sharply at the true width of a real image: scores came out **1024 -> 35.0,
  512 -> 35.1, 1536 -> 37.9**. A 0.3% spread is flat. There is no image-like row structure at any
  common stride, so the payload is compressed or interleaved. ⭐ The temptation was to report 1024
  because it was lowest; a minimum that shallow is the instrument saying "no", not "1024".

## 4. `ADVISOR.TPW` is audio (MEASURED)

346 MB, two thirds of the disc, and its sectors are **Mode 2 Form 2 with the audio bit set** in the
subheader. Ruled out by the sector form, not by guessing from the name. Anything that walks sectors
blindly will read it as file data.

## 5. Open questions

- **The `0x96` container's internal layout.** Sub-record first words include `f4 01 00 30` and
  `54 xx 00 ff`. Read little-endian, `0x300001f4` has top byte `0x30` — the PSX GP0 opcode for a
  gouraud triangle. If that holds these are **display lists / geometry**, not texture bitmaps, which
  would mean the pixels live elsewhere and this whole container is the wrong place to look for them.
  **OPEN — one plausible reading of one field, nothing more.**
- What the 12 uniform 131,156-byte entries are.
- `FOLIO.GAZ` header word 2 (= 23).

## 5b. The overlay is now decompilable (MEASURED)

`TPW.BIN` is a raw overlay with no header, so it needs a load address before a decompiler can do
anything with it. **It is `0x80010000`**, established two independent ways:

- Of its 17,862 `jal` instructions, **92.9% target an address inside the overlay** at this base. The
  next best candidate manages 87%, and `0x800c0000` — which the file's own first word,
  `0x800c0498`, invites you to try — manages **15%**. That first word is not a load address.
- Ghidra at this base produces coherent C with sane control flow. A wrong base decodes into
  instruction midpoints and the decompiler produces noise.

Recipe: import `TPW.BIN` raw, processor `MIPS:LE:32:default`, base `0x80010000`.

⚠ **`0x96` IS NOT A MAGIC NUMBER IN THE CODE — the code search for it was a dead end.** Six sites in
`TPW.BIN` load the immediate `0x96`, and every one decompiles to a **camera clamp**: `limit = 150`,
or `25` in one mode, then a value pinned to ±limit. `FUN_800ce1c0` likewise just initialises a
global to 150.

It remains true that 268 archive payloads open with the word `0x96` — that is measured, and it is
what makes them a distinguishable class. What is now disproven is the assumption that some routine
tests for it, and therefore that finding the constant would find the parser. **The same lesson as
the TIM scan, one level up: a small integer is not a signature. 150 is a perfectly ordinary number
for a clamp to be, and there is no reason a format tag and an angle limit should not collide.**
Either the parser switches on it in a way that does not materialise as that immediate, or the word
is a length or count that happens to be 150 for this class of asset.

## 5c. Three false trails in the code, and why each looked real

All three were pursued and all three died. Written down because each looks like a lead again from
cold, and because they share one shape: **a name or a number that was never a reference at all.**

1. **`0x96` in the code = camera clamps** (§5b). A small integer is not a signature.

2. **"`FUN_801e3a30` passes `\TPW.BIN;1` to a loader" — it does not.** The decompiler shows the call
   `FUN_801e6bd0(s__TPW_BIN_1, s__TPW_BIN_1)`, and the raw instructions confirm both `r4` and `r5`
   really are built as `0x801e8d08`, the string's address. But the callee's body is
   `for (; p < end; p++) if (*p) (*p)();` — a **static-constructor runner**, the classic
   `__main` / `__do_global_ctors(__CTOR_LIST__, __CTOR_END__)`. Both arguments are equal, so the loop
   never executes: it is an EMPTY constructor list, and the linker happened to place its bounds
   symbol at the same address the string table begins. **The filename is not an argument. It is a
   different thing living at the same address.** Ghidra names an address after whatever symbol it
   knows there, so the call reads as if it takes the filename, and the decompiled C is not wrong —
   only the name in it is.

3. **`\LEGAL.GFX;1` is never referenced by any `lui`/`addiu` pair in the boot executable.** Zero
   sites, against four for `\TPW.BIN;1` (all four being case 2 above).

⚠ **AND THE INSTRUMENT THAT FOUND THAT WAS BROKEN THE FIRST TIME.** The first run of that search
returned **0 sites for both strings** — including for an address I could see being constructed two
instructions earlier in my own disassembly output. The bug: MIPS builds an address as
`lui` + `addiu`, and `addiu` **sign-extends**. `0x801e8d08` is assembled as
`lui 0x801f` + `addiu -0x72f8`, so a search filtering for `lui 0x801e` misses every real site. The
only reason it was caught is that a known-present target came back empty. **Run any search for
something you already know is there; if it does not come back, the search is broken rather than the
world empty.**

## 5d. Audio (MEASURED — this part came out clean)

The archive holds **9 triples**: `[VAG body][VAB header][XM module]`, at entries 291-293, 294-296, …
315-317. Sound effects are PlayStation ADPCM; music is FastTracker 2 XM whose instruments are
supplied by the ADPCM bank rather than carried inline. 192 waveforms decode.

- **The body is the entry BEFORE its header**, not after. Verified: a header's VAG size table must sum
  to exactly a neighbour's size, and it matches the preceding entry **9 times out of 9**. The natural
  guess fails all nine.
- **Every VAB header is exactly 3104 bytes** = 32 + 2048 + 512 + 512, which is what marks them as
  split VH headers rather than whole banks.
- ⭐ **The grouping is confirmed from outside the parser.** Each XM declares an instrument count and
  each VAB a waveform count; nothing makes those agree except being a true pair — 6/6, 26/26, 15/15,
  21/21, 25/25, 22/22, 27/27, 23/23, 28/28.
- ⚠ The size table is in **8-byte units**, and **flag 7 ends the stream** (decoding past it welds
  noise from the next sound onto the end of a correct one).

**Sample rate — DERIVED, and it caught a bad guess.** VAG carries no rate. The first implementation
used 22,050 Hz "because that is a common PSX rate", which is not a reason. The XM modules keep their
instrument headers with sample length ZEROED (data stripped, it lives in the VAG bank) while
**relative note and finetune survive**: 0 or -12, finetune 0, giving **8363 Hz** and **4182 Hz** under
`8363 * 2^((rel + fine/128)/12)`. The guess was 2.6x out. **A stripped file is not an empty one — every
field describing the missing data was still present.**

⭐ **The VAB tone attributes are vestigial, so they cannot be the authority.** Across all 9 banks every
one of the 16 tone slots is byte-identical boilerplate (`centre=60, vol=127, pan=64, min=0, max=127`)
and **every tone points at waveform index 1**, while the banks hold 6-28 waveforms; each declares
`tones=1`. Do not spend time reading them, and do not expose the console's SPU registers to settle
pitch: there is nothing there to settle it with. The tracker side wins by elimination.

**Still open:** the rate is per-instrument (half an octave down), so it wants a per-waveform mapping
from XM instrument index to VAG index.

## 6. What would settle it

Structural guessing has stopped paying: the last three hypotheses each died on a falsifier, which is
the method working but not progressing. The loader in `SLES_026.88` / `TPW.BIN` knows the format
exactly, so reading the code that consumes `FOLIO.GAZ` beats guessing at its output.

**Where to start (MEASURED).** `SLES_026.88` is a `PS-X EXE`, loading at `0x801e0000`, entry
`0x801e3970`. It names exactly two files:

    0x9508  ram 0x801e8d08   "\\TPW.BIN;1"
    0x9514  ram 0x801e8d14   "\\LEGAL.GFX;1"

⭐ **`FOLIO.GAZ` does not appear as a string in either the boot executable or `TPW.BIN`.** `TPW.BIN`
names only `ADVISOR.TPW`. So the boot exe's whole job is to pull in the `TPW.BIN` overlay and show
the legal screen, and the archive is reached from inside that overlay either by walking the ISO
directory generically or by a baked-in LBA (it sits at 174726 = 0x2AA46). Either way **the loader is
in `TPW.BIN`, not in the boot executable** — which is the 1 MB of MIPS to point a decompiler at, and
a smaller haystack than "somewhere on the disc".

tinyclaw can dump console VRAM while the game runs and has established that resident textures are
**4bpp with palettes held separately and the lookup packed into the drawing commands**. That is the
*unpacked* form; this document is about the *packed* form, with an unpacking step between them. It
does mean any decoder proposed here has a known-correct answer to be checked against — which is the
check that catches a decoder producing a plausible image that is not the right image, the failure
mode that survives review longest because wrong-but-pretty looks fine.
