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

## 5j. Palettes: three pages hold them all, and the per-draw CLUT is NOT a lookup

✅ **Every palette is readable from the archive today.** Only **three** pages hold palettes —
`512,0`, `704,0`, `896,0` — and the other nine borrow from those three; all 50 palette references in
the capture resolve to them. tinyclaw's raw-disc search gives file offsets for all three: entries
`0x102 +0x1324`, `0xA9 +0x524`, `0x1A0 +0x2A00`. No VRAM, no emulator, no capture in the path.

⭐ **Palettes live INSIDE the texture page, in its rows** — which is why a page and its palettes share
an archive entry, and why "the palettes are somewhere else" led nowhere for an hour. The rows are a
LOOKUP, not a measurement: `clut_y` minus the page's `y` IS the row, and that reproduces the measured
rows exactly on all four pages checked ([0,1,2,3] / [20,21,26,68] / [19,25,43,45] / [10,17,22,23,80]).
⚠ Do not hardcode rows 1 and 3 — that is true of `704,0` and no other page.

⚠ **"Every palette sits inside the page it colours" was a selection effect** — true of exactly the
three pages that were sampled, which are the three palette-holders. The counterexample was already in
the capture file; it was never queried, because the query went to the pages already judged
interesting. **A space you hold is not a space you have searched.**

## ⚠⚠ 5k. BEFORE WIRING THE (rect → clut) MAP: it is a CAPTURE, not the source

The obvious next step is to drive colouring from `texture_clut_map.json`. **Think before doing that.**
That file is one park's draws: 12 pages, 177 rectangles, one camera, mostly build-mode terrain. It
colours what happened to be drawn and nothing else, so anything it does not cover comes out untinted
with no signal that it was missed.

**The game does not use a lookup.** fable's report on the `0x96` containers describes 14-byte faces
**carrying their own CLUT** — so the palette is chosen per face, from the model data, which is on the
disc and complete. That is the real source and it needs no capture at all.

So the map's proper use is as an **oracle** — check a face's CLUT against what the GPU actually bound
for that rectangle — rather than as the mechanism. Building it as the mechanism produces something
that works for one park, looks finished, and quietly has no answer for any asset nobody photographed.

⭐ **THE REAL PATH IS A FIELD READ, NOT A DERIVATION.** fable's folio analysis gives the `0x96`
container's face record as **`{i0, i1, i2, 3x(u,v), clut}`** — 14 bytes — under a
**`{nfaces, tpage}`** header, and the emitter at **`0x800115FC`** copies `clut` and `tpage` straight
into the GPU poly words. So the palette for a triangle is stored on the triangle. Nothing is inferred,
nothing is matched, and no capture is involved: read the field, use it.

⭐ **AND THAT MAKES THE CAPTURE MAP THE TEST FOR IT.** If reading CLUTs off faces reproduces the 177
`(rect → clut)` pairs logged off the live GPU, the implementation is verified against real hardware
behaviour rather than against itself — which is the difference between a decoder that is correct and
one that is merely self-consistent. **177 ready-made fixtures.** That is the right relationship
between the two artefacts, and it is the opposite of using the map to produce the colours.

## 5l. The mesh parser, and a corroboration I overstated

531 of 531 plain sub-entries parse, 0 failures: 38,680 vertices, 53,382 faces across 268 containers.
The 25 LZSS-compressed ones (all in entries 0 and 3) are expanded by `core/TPW.Data/SubLz.cs`, a port
of 0x800BFD9C read off the MIPS listing: **25/25 reach the stream terminator at exactly their declared
size and parse as meshes**, for 556/556 in all — 39,521 vertices, 54,619 faces
(`tpwcheck --gaz FOLIO.GAZ --meshes`, exit code = failures). The count is the falsifier — every field
feeds the walk that finds the next, so a wrong layout fails on most sub-entries, not a few.

⭐ **Reading `tpage` off the file reproduces the GPU's own page list**: `512/576/640/704/896/960` at
y=0 and y=256. Two methods sharing no code, no file and no assumption reaching the same twelve.

⚠ **BUT I ALSO CLAIMED THE "THREE STRAYS AT 0,0" AGREED, AND THAT IS COINCIDENCE.** tinyclaw checked:
their three come from a single capture where a misfire opened the purchase menu, and are large rects
of **menu art**, not geometry. Mine are three strays in mesh files. Two unrelated things that both
happen to number three. **The twelve is the finding and is strong enough alone**; quoting the strays
alongside it dressed one real result up as two.

⚠ **AND A CONSTANT I TRANSCRIBED RATHER THAN CHECKED.** fable's report gives the tpage X decode as
`(t & 0x1F) * 64`. Bit 4 is the Y select and `0x1F * 64 = 1984` exceeds VRAM's 1024-pixel width, so it
cannot be right — but I copied it and got pages at x = 1536..1920, addresses that do not exist.
Corrected to bits 0-3, which is what makes the twelve line up. tinyclaw's logger has used bits 0-3
since it was written, making that a third independent read agreeing with the correction rather than
the report. **A source being reliable does not make a line right.**

## 5m. Palette residency, proven by its own reversal

The mesh parse says some faces reference palettes in pages beyond the three "holders". tinyclaw first
checked one park's VRAM, found 26/41/91 palette-shaped rows in the three known holders and **exactly
zero** in all three of mine, and called them parse artefacts. It is a good control with three known
positives calibrating it.

⚠ **I nearly accepted that, and it would have sent me re-reading a correct parser.** What the tails
actually are: entry #83 is **ten identical 72-vertex, 12-bone, 12-track sub-meshes plus a 205-vertex
structure** — a ride with ten animated cars, the most coherent thing in the archive. #236/#241/#242
are the same shape. And **0 of 531 face-walks end past the next sub-entry**, while the tails sit in 14
meshes with 68% in one entry. A broken minority path overruns or scatters; this does neither.

⭐ **Then the population was widened from one park to twelve captures and the answer flipped in
seconds:**

    capture     512,0  704,0  896,0 | 512,256
    9x park     91     26     41    | 0
    menus       0      0      41    | 10

**The same pages hold palettes or not according to what is resident** — 512,0 and 704,0 carry 91 and
26 rows in a park and **zero in every menu**, while 512,256 does the exact opposite. That is the
mechanism demonstrated in both directions at once, which no single observation could do, and it
confirms `640,256 -> 512,256` (192 faces) as real. `576,0` and `960,0` remain
**unconfirmed-for-coverage, not refuted** — `960,0` is entry #83's ride, which appears in neither a
menu nor that one park.

⚠⚠ **THE LESSON IS NOT "CHECK YOUR CONTROLS", IT IS "CHECK WHAT YOUR CONTROL COULD SEE".** The
measurement was well-built, well-calibrated and pointed at one park. **A well-controlled measurement
of the wrong population is still the wrong population.** It was caught only because the result stated
its own scope — bare numbers would have cost an hour of re-reading working code. Across two days the
recoveries came from stated limits, not from stated findings.

(Also settled: the nine "parks" are byte-identical, so they were always one park. A caveat that had
been flagged in prose is now a measurement.)

## 5n. The mesh archive holds objects, not landscape — with a behavioural test

Measuring face-normal handedness in the file gives 53-71% per page: nothing is one-handed. That is
correct for **closed objects**, whose faces point every way, and it means the console's 100.0%
one-handed terrain triangles are **not in this archive**. "Terrain page" names a TEXTURE; it does not
name a source of triangles.

⭐ **tinyclaw then falsified it properly, by moving the camera:**

    page          idle       panning    change
    640        221,400      222,268     +0.4%
    576         61,452       19,094      -69%
    512         19,200        5,868      -69%

**Page 640 does not move at all** — about 185 triangles every frame whether the camera is still or
sweeping. 512 and 576 collapse by two thirds as the view changes. A fixed grid around the camera
costs the same regardless of where it points; placed objects come and go with the view. So the claim
now rests on behaviour rather than on the absence of one-handedness.

⚠ **And it refines the attribution both of us were using.** The earlier reading had 512 AND 640 as
"the two pure-terrain pages". The panning test says only **640** is landscape; 512 and 576 are things
standing on it. A number measured in one static scene could not separate those, and only changing the
camera could.

**For the port: terrain is ~185 triangles per frame, constant.** Generate a fixed patch around the
camera; do not stream variable geometry for it.

## 5o. Texture page header: the size field, and why UNPAK is not enough on its own

✅ **`blocks * 0x8000 = size - 0x54`, exactly.** The `u16` at header offset `+0x02` is a BLOCK COUNT, and
each block is 0x8000 bytes of 4bpp pixels. Verified against the uncompressed pages: entries 6, 7 and 8
all declare 4 blocks and are 131,072 bytes past their header. So a compressed entry declaring 6 blocks
must expand to 196,608 bytes, and that figure is an oracle for any decompressor.

⚠ **The first `u32` is NOT a size**, though it reads like one — `0x40011` is 262,161, which sits
temptingly close to 262,144. It is two `u16` fields, a count and the block count, and reading it whole
is how a plausible wrong size appears. The `compressed=1` flag is the low `u16` at `+0x0C`.

❌ **UNPAK over the whole payload does not work.** Running it from `+0x54` on all 13 compressed texture
entries produced between 0 and 4,010 bytes against targets of 65,536 to 393,216 — nothing close. That
is consistent with fable's own description, "UNPAK'd **64x64 blocks**": the compression is **per block**,
so there is framing between `+0x54` and the first stream that has not been worked out. The port of UNPAK
itself is in `core/TPW.Data/Unpak.cs` and is probably fine; what is missing is where each block starts.

⚠⚠ **AND UNPAK IS NOT SubLz.** Both are LZ, both live in this archive, and they expand different things:
SubLz (`0x800BFD9C`) for compressed MESH sub-entries, UNPAK (`0x80018EF0`) for compressed TEXTURE
entries. Feeding one to the other yields garbage of exactly the right length, so the size oracle passes.

⚠ **The entry numbers in fable's report are DECIMAL.** They are written zero-padded — `0005, 0082, 0258`
— which reads as hex. As hex, `0x258` is 600 in a 422-entry archive, and `0x0082`/`0x0084` land on real
entries that are mesh containers, failing quietly as "UNPAK did not expand this" and looking like a
decompressor bug.

## 5p. ✅ The texture sheets, solved: layout, compression, and the palettes (2026-09-19)

Read off the game's loader, `0x800280F8`, the only caller of UNPAK besides the generic resource path.
The "0x54-byte header" was never a fixed size. It is:

```
+0x00 u16 sprites      +0x02 u16 pages (= cols*rows)   +0x04 u16 texpage of the top-left page
+0x06 u16 cols         +0x08 u16 rows                  +0x0A u16 free rectangles
+0x0C u16 compressed   +0x0E u16 0
+0x10 sprites x 12 bytes: u16 texpage, u16 CLUT, s8 ox, s8 oy, u8 w, u8 h, u8 u, u8 v, u8 flags, u8 ?
      free rects x 8 bytes: u16 x, y, w, h (sheet texels; the packer's unused space)
      pixels
```

The raw sheets all have 3 sprites and 4 free rects, and 0x10 + 36 + 32 = 0x54. That coincidence is where
the fixed size came from. The compressed sheets have hundreds of sprites, so UNPAK run from +0x54 started in
the middle of the sprite table.

**Compressed sheets** are uploaded in 64x64-halfword blocks (0x2000 bytes = 256x64 texels at 4bpp), four per
page: **block 0 is stored raw** (the loader's buffer doubles as its scratch), and every later block is its own
**headerless** UNPAK stream straight after the previous one. The loader advances by UNPAK's consumed count,
which includes the 2-byte terminator. Block i lands at page `i/4` (across `cols`), strip `i%4`.

Checks, all of which a wrong reading fails:
- all 17 compressed sheets: every block expands to exactly 0x2000, and the last stream ends **on the entry's
  last byte** (13 in fable's list, plus #332, #333, #400, #416);
- all 12 raw sheets: pixels = pages x 32 KB, filling the entry exactly;
- against tinyclaw's live VRAM hashes (`vram_block_hashes.json`), decoded blocks match **hash-exactly at the
  position the header predicts**: park_ride 55/58 non-blank (was 17), cold_boot 33/36. The 6 left match
  nothing on the disc at any position. tinyclaw: the park three are per-frame scratch (they change between
  frames 10 and 20 of one capture); the boot three are written once at load and then frozen.

⭐⭐ **THE PALETTES ARE INSIDE THE SHEETS.** Every sprite record names its CLUT, and for **4,409 of 4,409**
sprites across all 29 sheets, that CLUT lies inside the sprite's own sheet. By chance a CLUT word would land
inside a sheet 6-40% of the time, depending on its size. So the "(rect -> clut) per page" mapping this whole
search was missing is the sprite table. Rendered with it, the front-end sheet shows seven flags in their
true colours: UK, France, Germany, Spain, Italy, Netherlands, Sweden, the disc's seven languages.
45 sprites are 8-bit (texpage bits 7-8 = 1).

Code: `core/TPW.Data/TextureSheet.cs`, `Unpak.TryDecompressBlock`. `tpwcheck --sheets vram_block_hashes.json`
reproduces the VRAM numbers through the C# path.

## 5q. ✅ The park ground: the terrain routine, and the world table (2026-09-19)

Read off the game's own terrain routine, **0x80012110** (hand-written; it moves the stack onto the scratchpad).
0x800567F0 scan-converts the camera's view footprint onto the map, then this emits one textured, Gouraud
POLY_GT4 (command 0x3C) per tile:

| tile byte | meaning (from the routine) |
|---|---|
| +1 | height at the tile's **corner**, × 4 world units (a tile is 256) |
| +4 bits 0-11 | sprite in the world's ground sheet |
| +4 bits 12-13 / 14 / 15 | quarter turns / mirror down / mirror across of that texture |
| +6 bits 0-5 | shade at the corner: index into the map's 64-entry table (`u32 N; u32 shade[N]` at the top of the map) |
| +7 bit 0 | no ground here (on map #203, a river that drops down a waterfall; drawn by something else) |

A quad blends the four corner tiles (x, z), (x+1, z), (x, z+1), (x+1, z+1) for height and shade, and takes its
texture from (x, z). The GPU folds it on the (x+1, z)–(x, z+1) diagonal. Edge rules are odd but the game's:
row 0 is always flat (the test is z < 1), and out-of-map tiles are flat copies of the edge.

**The world table** (0x8002ED50 → records at 0x8010558C / 0x801054DC / 0x8010542C / 0x8010537C, listed by
0x800DDDC4): world 0 = maps #203/#204, ground sheet #258 (jungle, confirmed in RAM); world 1 = #116/#117,
#168; world 2 = #34/#35, #82; world 3 = #355/#356, #400. ⚠ A "which sheet fits" guess picks #82 for map #116
where the game uses #168; the table is the answer, not the fit.

✅ Checked: map #203's ground rendered top-down through `tpwcheck --ground 203 258` puts the rocky cliff
textures exactly on the tiles whose corner heights fall 204 → 64, the road's lane lines run unbroken across
tiles (turns and mirrors right), and the grass with its stone and twig decals looks like the console screenshot's.
⚠ The world frame is **left-handed** like the models': a right-handed viewer negates z, or the park is mirrored
(master caught it).

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
