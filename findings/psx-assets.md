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

**Still open: the XM-instrument to VAG-index mapping is UNVERIFIED, and probably unverifiable from
these two files.** Index order is the obvious reading and the counts match 9/9, but that is the same
kind of "obvious" that has been wrong repeatedly here, so it was tested against loop points: an XM
sample declaring a loop should line up with a VAG carrying the loop-start flag.

Result 1/193, which reads as a dramatic refutation and **is worth nothing**. ⚠⚠ THE TEST WAS
DEGENERATE AND THE MARGINALS PROVE IT: **0 of 193** XM samples declare a loop (stripping the sample
data zeroed the loop fields too) and **192 of 193** VAGs carry the flag (it is how PSX ADPCM
terminates). With one side constant, agreement is pinned at 0.5% *however the two are paired* —
and 0.5% is exactly what came out. The number measured the marginals, not the ordering.

**Always compute what agreement the marginals alone predict under random pairing, and compare the
measured figure to THAT, not to 50%.** Here they matched to the decimal, which is the signature of a
comparison carrying no information. Reported as a refutation it would have been a confident, precise,
completely false finding.

The stripping removed exactly the fields that could corroborate a mapping — length, loop points — so
there may be no cross-check available between these two files. Do not go hunting for one; settle it
from the player code, or by ear once something renders.

## 5e. The legal screen: decode is faithful, colour is NOT settled

Master ran it and read the text off the screen: the image appeared, **rotated 180 degrees**. The pixel
decode was therefore already sound and only orientation was wrong. `LEGAL.GFX` carries TGA descriptor
`0x00` (bottom-left origin), so a spec-correct decode flips it vertically — and that is what comes out
wrong, meaning the file is stored rotated relative to how the game presents it. The rotation is applied
in the game layer, not in `Tga`, which stays spec-correct and unit tested. **Why** it is stored that
way is not established, and the comment says so instead of inventing a reason.

⭐ **A PERSON LOOKING AT THE SCREEN FOUND THIS IN SECONDS. NOTHING HEADLESS COULD.** The buffer was the
right size, the right format, and full of the right pixels in the wrong order, so every check passed:
the self-test, the decode, the invariants. Orientation is invisible to all of them.

**Colour is a separate, still-open problem, and the file settles who is measuring what.** Compared
against tinyclaw's console framebuffer capture:

    FILE   high5 peaks 30   mid5 peaks 31   low5 peaks 0     non-black 11,234
    VRAM   R = 14           G = 30          B = 29           non-black 11,143

⚠⚠ **THE LOW 5 BITS ARE ZERO ON ALL 81,920 PIXELS OF THE FILE** — identically, not mostly. So under
either labelling the file has one channel entirely absent (`R=30 G=31 B=0` read as ARGB1555, or
`R=0 G=31 B=30` read as ABGR1555, the PSX convention). The framebuffer capture has **no zero channel**.

⭐ **No fade, scale or rotation can turn an identically-zero channel into 14.** That single fact rules
out the fade, the row order and the decoder all at once, and leaves only one explanation: **something
applies a colour transform between file and framebuffer.** And the two are certainly the same picture —
the non-black counts differ by 0.8%, which is what a fade rounding dim pixels to black costs.

So the decode is faithful to the FILE and the capture is faithful to the SCREEN, and those were never
the same target; both of us half-assumed they were. **Do not "fix" the decoder by swapping channels
until the transform is known** — matching numbers by rearranging bits is how a plausible wrong decoder
gets built, which is the failure the oracle existed to prevent.

## 5f. Palettes CANNOT be found by scanning, and I published that they could

❌ **RETRACTED.** I recorded that the palettes live in two headerless 128 KiB entries, `0x104` and
`0x10C`, identified by every 32-byte table opening `0x0000, 0x8001`, and put both facts in code with
a test pinning them. Both halves are wrong.

tinyclaw traced four palettes from the drawing commands back to bytes: three are in entry **#169**, a
contiguous array at a `0x20` stride, and one is in entry **#416** at `+0x36C0`. **Entry #169 contains
zero blocks matching the signature I was testing for.** The signature selects something real — 18
blocks in `0x104`, 47 in `0x10C`, 32 in `0x1A0` — but not the palettes the game draws with.

⚠⚠ **AND THE OBVIOUS FALLBACK IS VACUOUS.** "16 distinct halfwords in 32 bytes" reads like a real
constraint. Sixteen random 16-bit values are all distinct about **99.8%** of the time, so nearly every
block in the archive passes it. There is no structural test for a palette worth writing: 32 bytes of
arbitrary colour is indistinguishable from 32 bytes of anything else.

⭐ **That is a property of the format, not a gap to close with a better scan.** The addresses have to
come from the drawing commands. What makes a traced match trustworthy is the evidence for that match:
the `(928,25)` run has 16 distinct halfwords out of 16 and occurs exactly twice in 16 MB, and three of
five traced palettes cluster in one entry — chance does not cluster.

**Known open:** two of the five traced palettes are not in the archive verbatim, so some are
compressed or built at runtime; a plain lookup will not cover all 47 in use. And **a page has MANY
palettes, not one** — one atlas is drawn with up to 11 different CLUTs depending on the sprite, so
"render page N under palette X until it looks right" is a malformed question, not a weak test.

## 5g. The texture layout, corrected twice — and "it looks right" settled nothing

A sheet entry is `0x54` header + a **1024x256** block at 4bpp: **four 256x256 texture pages side by
side**. A PSX texture page is 64 VRAM halfwords wide, which at 4bpp is 256 texels, so a
256-halfword-wide upload is four pages in a row — and the draw commands' UVs run 0..255 within a
page, saying the same thing from the GPU side.

I reached that after two wrong answers, **both of which looked right**:

1. **256x256 at 16bpp.** 131,072 is exactly that, so the size appeared to settle it. Decodes to
   coloured noise — the one failure mode that announces itself.
2. **512x512 at 4bpp.** This is the instructive one. It renders a *single coherent image with legible
   text in it*, and at 1024x256 the picture seemed to show the same thing twice, which I read as a
   width exactly 2x too large. The "duplicate" was pages 0 and 2 carrying **similar but different**
   terrain art. I mistook a resemblance for a repetition.

⭐ **Reinterpreting a 2D block at a multiple of its true width preserves the byte total and rearranges
the content without destroying local structure, so it still looks like a picture.** Coherence
therefore cannot distinguish these layouts, and neither can the entry size, which both satisfy. What
settled it was tinyclaw's tpage coordinates stepping by 64 halfwords — evidence from outside the
file, of a kind no amount of looking at pixels could supply. Rendered correctly the four pages come
apart cleanly: each is self-contained and no sprite runs across a cut.

**`findings/texture_clut_map.json`** holds the mapping tinyclaw read off the GPU: 12 tpages, 177
`(rect → clut)` pairs. Contiguous regions share a palette, so a sheet partitions into palette zones
rather than needing a lookup per quad.

## 5h. File → VRAM binding, PROVEN by content hash — and two layouts, not one

⭐ **Entry `0x10C` maps block-for-block into VRAM, 14 matches, confirmed independently by tinyclaw
against their own capture.** Consecutive 8192-byte chunks at a `0x2000` stride:

    +0x0     -> 768,0     +0x8000  -> 832,0     +0x10000 -> 768,256   +0x18000 -> 832,256
    +0x2000  -> 768,64    +0xA000  -> 832,64    +0x12000 -> 768,320   +0x1A000 -> 832,320
    +0x4000  -> 768,128   +0xC000  -> 832,128   +0x14000 -> 768,384
    +0x6000  -> 768,192   +0xE000  -> 832,192   +0x16000 -> 768,448

**THERE ARE TWO ENTRY SHAPES AND ONE RULE DOES NOT COVER BOTH:**

| entry | header | layout |
|---|---|---|
| 131,156 bytes | `0x54` | **1024x256 px, ROW-MAJOR** — four 256x256 texture pages side by side |
| 131,072 bytes | none | **256x1024 px, 16 BLOCKS** of 256x64 — uploads as two VRAM columns |

Derivable from the hardware, not just observed: a texture page is 64 cells x 256 rows, and at 4bpp a
cell is 4 pixels, so a page is 256x256 px. 131,072 bytes = 65,536 cells / 16,384 per page = **exactly
4 pages**. At 256 px wide, 8192 bytes is exactly one 64x64-cell block — which is why the second shape
matches VRAM blocks byte-for-byte while the first does not.

⚠ **I nearly forced the block layout onto the sheets on the strength of one measured binding.**
Assembling a 131,156 sheet block-linear shatters it — the credits text comes apart into fragments —
while row-major renders it legibly. **The binding was real; its SCOPE was one entry.** A rule verified
on one file and generalised to a file it was never tested on is the day's last repetition of the same
mistake.

## ⚠ 5i. Two null results that were about to be reported, and what saved them

**135 of 149 "matches" were blank blocks.** Scanning all 422 entries against the VRAM hashes returned
149 hits. The tell was one hash appearing at **34 different positions**, which is impossible for real
content — it is the sha256 of 8192 zero bytes. **An empty block matches every other empty block, so
it confirms whatever hypothesis it is pointed at.** With degenerate blocks excluded on both sides:
14 matches, all one entry, consecutive and evenly spaced — which is what a true binding looks like
and what 149 scattered blanks never could. ⚠ Honestly: this was caught by a number weird enough to
trip over, not by having a control in place first.

**And a clean zero that meant "you looked too early".** tinyclaw's cold-boot capture matched nothing,
read as "the archive does not feed these slots". Entry `0x10C` is resident during a *park* and absent
at *boot* — so the null was a timing artefact, and the DIFFERENCE between the two captures is now
positive evidence that the twelve `0x54` sheets are intro/credits art rather than in-game textures.
**Before reporting a null, say when you looked, and check you looked when the thing would be there.**

`findings/vram_block_hashes.json` holds the console-side hashes; `texture_clut_map.json` the
(rect -> palette) mapping. Neither contains pixels or colours.

## 6. What would settle it

Structural guessing has stopped paying: the last three hypotheses each died on a falsifier, which is
the method working but not progressing. The loader in `SLES_026.88` / `TPW.BIN` knows the format
exactly, so reading the code that consumes `FOLIO.GAZ` beats guessing at its output.

**Where to start (MEASURED).** `SLES_026.88` is a `PS-X EXE`, loading at `0x801e0000`, entry
`0x801e3970`. It names exactly two files:

    0x9508  ram 0x801e8d08   "\\TPW.BIN;1"
    0x9514  ram 0x801e8d14   "\\LEGAL.GFX;1"

❌ **RETRACTED — I CLAIMED `FOLIO.GAZ` APPEARS AS A STRING IN NEITHER BINARY. IT IS IN `TPW.BIN`.**
It sits at file offset `0xcff84`, preceded by the pointer `0x800e00d4`, and the archive is opened by
name through `CdSearchFile` — there is no hardcoded LBA. tinyclaw caught this after I had already
published it, and it had been passed on to fable as a fact by then.

⚠⚠ **THE CAUSE WAS A TRUNCATED LIST READ AS AN EXHAUSTIVE ONE.** My probe collected every printable
string matching a keyword filter and printed `list(found.items())[:14]`. The filter matched **20**.
`ADVISOR.TPW` was the fourteenth, so it was the last thing printed, and `FOLIO.GAZ` was in the six
that never rendered. The output ended exactly at my own limit — **a result whose count equals your
display limit is not a result** — and I read "the last line I can see" as "the last line there is".

Same family as the marginals error and the flat stride test: the instrument described itself and I
quoted it as though it described the disc. The difference is that this one **propagated into someone
else's work before it was caught**, which is the expensive kind. Print the total count next to any
truncated listing, and say "showing 14 of 20".


tinyclaw can dump console VRAM while the game runs and has established that resident textures are
**4bpp with palettes held separately and the lookup packed into the drawing commands**. That is the
*unpacked* form; this document is about the *packed* form, with an unpacking step between them. It
does mean any decoder proposed here has a known-correct answer to be checked against — which is the
check that catches a decoder producing a plausible image that is not the right image, the failure
mode that survives review longest because wrong-but-pretty looks fine.
