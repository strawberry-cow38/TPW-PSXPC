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

## 6. What would settle it

Structural guessing has stopped paying: the last three hypotheses each died on a falsifier, which is
the method working but not progressing. The loader in `SLES_026.88` / `TPW.BIN` knows the format
exactly, so reading the code that consumes `FOLIO.GAZ` beats guessing at its output.

tinyclaw can dump console VRAM while the game runs and has established that resident textures are
**4bpp with palettes held separately and the lookup packed into the drawing commands**. That is the
*unpacked* form; this document is about the *packed* form, with an unpacking step between them. It
does mean any decoder proposed here has a known-correct answer to be checked against — which is the
check that catches a decoder producing a plausible image that is not the right image, the failure
mode that survives review longest because wrong-but-pretty looks fine.
