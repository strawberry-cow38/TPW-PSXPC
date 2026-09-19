# Theme Park World (PSX, PAL, SLES-026.88): cold boot -> main menu, frame-accurate

Eleventh report. Measured on the emulator, not read out of the executable: every row below is a
frame that was rendered or a value read from console RAM, and the method is stated per claim.

⚠ ONE CORRECTION, CHECKED AGAINST THE FILE. §A "Asset ties" calls LEGAL.GFX "a raw 320-pixel-wide
15-bit image (320 x 258 rows ... plus 283 trailing bytes)". It is not raw: it is an ordinary
uncompressed TGA -- 18-byte header, id length 0, image type 2, **320x256**, 15 bits per pixel, with
a TRUEVISION-XFILE footer at 165,385. 18 + 320*256*2 = 163,858, exactly what the header declares
and what the port's own self-test reports. The raw reading renders almost correctly because the
header is smaller than one row (18 bytes = 9 pixels), so the image is shifted rather than garbled
and looks right -- which is why it passed inspection. The VRAM offsets in that section are
unaffected; only the file's shape was misread.

Measured on the libretro harness (pcsx_rearmed core, PAL BIOS scph5502, disc `disc/tpw.cue`),
with an instrumented copy of the runner (`fable/b/runner_b.c`: per-frame FNV hash of every
non-duplicate video frame, per-frame audio sample stats, frame/RAM capture lists, POKE). The
core reports **50.00 fps** (`[fe] loaded. 256x240 @ 50.00 fps`, run stderr), so every
"seconds" figure below is **frames / 50**. Frame N = the N-th `retro_run` call counting from 0
at power-on.

Two things about the harness matter for the numbers:

* **The core's default is to SKIP the BIOS boot screens** (`pcsx_rearmed_show_bios_bootlogo`
  defaults to disabled, `frontend/libretro.c:3833`). All figures here are from runs with that
  option answered `enabled` (env `BIOSLOGO=1` in `runner_b`). With the default (as in
  `boot_practice.txt` and every earlier script in `btn/`), every game-side frame number is
  **exactly 924 frames earlier** (run `cold0` vs `cold1`: legal fade-in at 214 vs 1138, BF.STR
  at 1120 vs 2044, language screen at 3661 vs 4585 — identical hashes, shifted by 924).
* Runs are deterministic: two cold boots (`cold1`, `canon`, `canon2`, `lang_cross`,
  `ds/lang_none`) produce byte-identical hash sequences up to the first input, and a save state
  restored at frame 4700 reproduced the next 900 hashes of the cold run exactly (restore verified
  by consequence, `runs/s_base` vs `runs/lang_cross`).

PNGs referenced below are in `fable/b/png/`. Every claim is tagged with its evidence:
`[hash]` = per-frame hash log of the named run, `[png]` = a rendered frame, `[RAM]` = a value
read from a console RAM dump, `[audio]` = the emulator audio callback, `[diverge]` = first frame
whose image differs from the no-input run.

## A. Timeline (run `cold1`: no input, BIOS logo enabled, plain digital pad)

| # | Screen | First | Last | Frames | Seconds | Output mode | PNG | Evidence |
|---|--------|------:|-----:|-------:|--------:|-------------|-----|----------|
| 0 | Nothing (black, no video mode yet) | 0 | 85 | 86 | 1.72 | 256x240 | `00_power_on_f0.png` | [hash] one static hash `d6ebfd6e` |
| 1 | BIOS "Sony Computer Entertainment" screen | 86 | 555 | 470 | 9.40 | 640x480 | `01_bios_sce_f188.png` | [hash] anim 86-187 (69 distinct frames), static 188-555 |
| 1a | . fade black -> grey | 86 | ~137 | | | | sheet `sheet_canon_bios1.png` | [png] frames 86..134 are flat grey levels rising |
| 1b | . diamond appears and animates | 138 | 187 | | | | | [png] diamond first visible at 138, shading animates to 186 |
| 1c | . hold with "SONY COMPUTER ENTERTAINMENT" text | 188 | 555 | 368 | 7.36 | | | [hash] static `a6c38e2d`; text visible at 190 [png] |
| 2 | BIOS PlayStation logo screen | 556 | 1021 | 466 | 9.32 | 640x480 | `02_bios_ps_f632.png` | [hash] anim 556-631 (72 distinct), static 632-1021 |
| 2a | . grey fades to black | 556 | ~571 | | | | sheet `sheet_canon_bios2.png` | [png] |
| 2b | . PS logo fades in on black | 572 | 603 | | | | | [png] dim at 576, full by 600 |
| 2c | . "Licensed by Sony Computer Entertainment Europe" line appears 604, "PlayStation" wordmark 608 | 604 | 631 | | | | | [png] (the small "PCSX" mark under the licence line is the emulator's patch, not the game) |
| 2d | . hold | 632 | 1021 | 390 | 7.80 | | | [hash] static `3bf1f945` |
| 3 | Black: game executable starts (256x240 for 1022-1028, then 320x240) | 1022 | 1137 | 116 | 2.32 | 256/320x240 | `03_black_f1029.png` | [hash] |
| 4 | Legal/copyright screen = **LEGAL.GFX** | 1138 | 1450 | 313 | 6.26 | 320x240 | `04_legal_f1199.png` | [hash]; asset tie below |
| 4a | . fade in, one distinct frame per frame | 1138 | 1198 | 61 | 1.22 | | | [hash] 61 changes in 61 frames |
| 4b | . hold | 1199 | 1387 | 189 | 3.78 | | | [hash] static `70020286` |
| 4c | . fade out | 1388 | 1450 | 63 | 1.26 | | | [hash] 63 changes in 63 frames |
| 5 | Black (loading). Modes: 320x240 1451-1463, 256x240 1464-1484, 320x240 1485-2004, 512x240 2005-2043 | 1451 | 2043 | 593 | 11.86 | mixed | `05_black_load_f1485.png` | [hash] |
| 6 | Bullfrog logo FMV = **BF.STR** (video 320x176 letterboxed in a 320x240 frame) | 2044 | 2487 | 444 | 8.88 | 320x240 | `06_bf_str_f2303.png` | [hash] 107 distinct frames; [RAM] tie below |
| 7 | Black | 2488 | 2511 | 24 | 0.48 | 512x240 | | [hash] |
| 8 | Intro FMV = **GRAV.STR** | 2512 | 4455 | 1944 | 38.88 | 320x240 | `07_grav_str_f3200.png` | [hash] 467 distinct frames (STR at 12 fps = 1 frame per 4.17 console frames, matches 120 STR frames per 500 console frames read from RAM); [RAM] tie below |
| 9 | Black | 4456 | 4584 | 129 | 2.58 | 512x240 | `08_black_f4456.png` | [hash] |
| 10 | Language select (waits forever; still there at frame 7999 = 160 s with no input) | 4585 | - | - | - | 512x240 | `09_language_f4650.png` | [hash] a new frame every frame (waving flag) |

After CROSS on the language screen at frame P (measured with P = 4700, run `lang_cross`; repeated
with P = 5200, run `lang_cross5200`, every offset below reproduced to within 1 frame):

| # | Screen | First | Last | Frames | Seconds | PNG | Evidence |
|---|--------|------:|-----:|-------:|--------:|-----|----------|
| 11 | Language screen fades to black (click SFX P+1..P+10) | P+2 | P+13 | 12 | 0.24 | sheet `sheet_st_loading.png` | [diverge] 4702; [audio] 4701-4710 |
| 12 | Black | P+14 | P+29 | 16 | 0.32 | | [hash] static 4714-4729 |
| 13 | "Now Loading..." (juggling jester on blue; a dark iris grows from the centre from ~P+52 and fills the screen by ~P+81; jester fades out P+85..P+96) | P+30 | P+96 | 67 | 1.34 | `10_now_loading_f4771.png` | [hash] 4730-4796, 51 distinct frames; [png] |
| 14 | Black | P+97 | P+113 | 17 | 0.34 | | [hash] static 4797-4813 |
| 15 | **Main menu** appears (fades up over ~10 frames; "Load Game" label present by P+124) | P+114 | - | - | - | `11_menu_first_f4820.png`, `12_main_menu_f5100.png` | [hash] new frame every frame from 4814 |
| 15a | . first frame at which a press registers | P+152 | | | | | [diverge] 1-frame DOWN at rel 151: no effect; at 152: highlight moves (`pin_151`/`pin_152`) |
| 15b | . menu music starts | P+305 | | | | | [audio] 5005 for P=4700, 5506 for P=5200 |

Totals (cold1, no skipping, earliest possible CROSS on the language screen at 4594):
main menu visible at **4708 (94.2 s)**, accepting input at **4746 (94.9 s)**.
Fastest path (run `fastest`: CROSS pulsed every 30 frames from 2044): BF.STR cut at 2048, black
2051-2074, GRAV.STR 2075-2111 (cut), black 2112-2239, language 2240, CROSS accepted 2254,
main menu at **2368 (47.4 s)**, accepting input from **2406 (48.1 s)**, music at 2559. [hash] [audio]

### Asset ties
* **LEGAL.GFX** (165,403 bytes, lba 196254) is a raw 320-pixel-wide 15-bit image (320 x 258 rows
  = 165,120 bytes, plus 283 trailing bytes). Rendering it as such gives the legal text
  (`fable/b/legal_gfx_render.png`) [png]. Byte runs from the file at offsets 0x4b7a, 0x4f7a,
  0x20000 are found in the VRAM dump taken during the legal fade-out (cold0 frame 500 = cold1
  frame 1424) at VRAM offsets 0xf47a, 0xfdfa, 0x66600 — exactly the offsets of a 640-byte-per-row
  image uploaded at VRAM x=512, y=0 (row r -> 0x400 + r*2048 + col) [RAM/VRAM].
* **BF.STR** (lba 169368): RAM dump at frame 2500 contains BF.STR sectors 1325-1339 (STR frame
  numbers 107-108, read from the sector headers) in a ring buffer at 0x8015DC20-0x80163AA0 [RAM].
  Its STR header says 320x176, 11 sectors per frame; 1432 sectors / 11 = 130 frames at 150
  sectors/s = 9.5 s, vs 8.88 s of distinct video measured.
* **GRAV.STR** (lba 184582): RAM dumps at frames 3000 / 3500 / 4000 contain GRAV.STR sectors
  1470-1494 / 2970-2994 / 4470-4494 (STR frames 118-120 / 238-240 / 358-360) at the same ring
  buffer 0x8015DC20.. [RAM]. No sector of END/ENGLISH/FRENCH/SPANISH/JUG/MIR.STR was found in any
  dump between 1500 and 4500, so none of those plays during boot.
* The BIOS screens come from the BIOS ROM, not the disc (they run before any disc file is read;
  frame 1022 is where the 640x480 BIOS mode ends).

## B. What is skippable, and by what

Method: one press (8 frames down unless stated), then compare the whole hash sequence against the
no-input run; "identical" means byte-identical video for the rest of the run (>=300 frames after
the press). Runs in `fable/b/runs/sw*`, `runs/st/*`.

| Screen | Skippable? | Buttons tested | Result |
|--------|-----------|----------------|--------|
| BIOS screens (0-1021) | **No** | cross, start at 300 and 800 | identical through 1299 |
| Legal screen (1138-1450) | **No** | cross, circle, triangle, square, start, select at 1150, 1250, 1350, 1420 | all identical |
| Black loading (1451-2043) | **No**, and a press here is not queued | same six at 1600, 1900; cross at 2010, 2030 | all identical |
| **BF.STR** (2044-2487) | **Yes, any button** | cross, circle, triangle, square, start, select at 2060, 2100, 2200, 2400 | all six diverge at the same frame; cross/circle/start/select runs at 2200 are byte-identical to each other (same effect) |
| Black (2488-2511) | not queued | cross at 2490, 2500 | identical |
| **GRAV.STR** (2512-4455) | **Yes, any button** | same six at 2530, 2600, 2800, 3000, 3500, 4000, 4400 | all diverge; triangle/square at 3000 byte-identical to cross |
| Black (4456-4584) | not queued | cross at 4460, 4500, 4560, 4585, 4590 | identical |
| Language screen (4585-) | advances on **CROSS only** | cross, circle, triangle, square, start, select at 4600, 4700, 4800, 4900, 5000, 5200 | only cross diverges (2 frames after the press), every other button identical |

**Exact rules for the FMV skip** (from 1-frame presses and long holds, `runs/sw3`, `runs/sw4`):
* A press is seen from the FMV's **first displayed frame**: 1-frame CROSS at 2043 does nothing,
  at 2044 it skips (image diverges at 2048). Same for GRAV.STR at 2512.
* The button must have been **up at the player's init frame**, which is the first black 512x240
  frame before the FMV: **2005** for BF.STR (hold 2005-2050: no skip; hold 2006-2050: skip) and
  **2488** for GRAV.STR (hold 2488-2520: no; 2489-2520: yes). A single hold 2000-2600 skips
  neither FMV. So it is an edge relative to a snapshot at the init frame, not a level.
* Latency press -> image change: 4-7 frames (2044->2048, 2060->2065, 2100->2106, 2200->2207,
  2400->2407, 2512->2516, 2600->2604, 3000->3004). The video stops, the audio stops 1 frame after
  the press is first seen ([audio] press 2060: sound ends 2064; press 3000: ends 3003).
* After a BF.STR skip: black for 24 frames, then GRAV.STR starts (press 2060 -> video off 2068,
  black 2068-2091, GRAV.STR audio 2085, first frame 2092). Same 24-frame gap and 7-frame
  audio lead as the unskipped boot.
* After a GRAV.STR skip: black 128 frames, then the language screen (press 3000 -> black
  3007-3134, language 3135). Unskipped gap is 129.
* The last ~5 frames of an FMV are dead: press at 4440 still cuts GRAV.STR (diverges 4445), a
  press at 4450-4457 is identical to no press (FMV ends 4455 anyway).

**Language screen input**: accepts from **frame 4594** (9 frames after it appears): 1-frame CROSS
at 4593 identical, at 4594 diverges at 4596. It is **edge-triggered per frame**: an 8-frame press
4590-4597 does nothing, and a hold 4500-4700 does nothing (`hold_lang`), even though both cover
4594. LEFT/RIGHT (and the left stick when the pad is a DualShock) rotate the language ring; UP,
DOWN, L1, R1, and the sticks on a plain pad do nothing (`runs/st/lang_*`, `runs/ds/lang_lsleft`).

**Main menu input**: accepts from **P+152** (38 frames after it appears at P+114), also edge
per frame: 8-frame DOWN at 150-157 does nothing, 1-frame DOWN at 152 moves, a hold 140-170 does
nothing (`holddown`).

## C. The main menu (`12_main_menu_f5100.png`)

* Backdrop: red/purple theatre curtains, the THEME PARK WORLD logo, a spotlight that sweeps
  continuously (every frame's hash differs), and the advisor character (black body, green cap,
  white glove) standing at the right, pointing at the highlighted item.
* Items, top to bottom: **Play Game**, **Options**, **Load Game** [png]. The highlighted item is
  drawn bright yellow with the glove beside it; the others dull orange-red. "Load Game" is drawn
  dimmer still when not highlighted [png 12].
* At rest **Play Game** is highlighted [png 12, 11].
* **DOWN** moves the highlight down, **UP** up, both **wrap**: DOWN x1 -> Options
  (`13_menu_after_down_rel480.png`), x2 -> Load Game (`19_..._down_down_rel540.png`), x3 -> Play
  Game (`21_..._down_x3_rel600.png`); UP x2 from Play Game -> Options (`18_..._up_up_rel540.png`),
  i.e. UP x1 -> Load Game. With a DualShock pad (`PAD_DUALSHOCK=1`) **left stick up/down** also
  moves it (`runs/ds/lsdown`, `lsup` diverge 2 frames after deflection); right stick does not.
  With the plain pad the analog ids do nothing (`runs/st/m_ls*`, `m_rs*` identical).
* **Nothing else does anything at the main menu**: LEFT, RIGHT, circle, triangle, square, start,
  select, L1, R1, L2, R2 at rel 400 are all byte-identical to no press (`runs/st/m_*`).
* **CROSS** on each item (one level deep):
  * **Play Game** -> the item list is replaced in place (same backdrop) by **Main Game / Practice
    Park / Exit**, Main Game highlighted (`14_playgame_submenu_rel500.png`; visible 10 frames after
    the press). There, UP/DOWN move (diverge), CROSS on **Main Game** does **nothing** (identical
    for 900 frames, `pg_cross` vs `pg_base`), CROSS on **Practice Park** starts a load: music
    stops at rel 801, silence until 1084, then sound resumes on the loading screen
    (`20_practice_park_loading_rel1084.png`). Triangle/circle do not back out (identical).
  * **Options** -> the logo scrolls up off-screen and the list becomes **Music [slider] / SFX
    [slider] / Tutorial On / Screen / Credits / Exit** (`15_options_rel600.png`). DOWN moves,
    LEFT on the Music row changes the frame (diverges), CROSS on Music does nothing,
    triangle/circle do not back out (identical through rel 1199).
  * **Load Game** -> **nothing**: highlight it (DOWN x2) and press CROSS, the run is
    byte-identical to just highlighting it, with CROSS 60 or 240 frames later (`load`, `load2`).
    No memory-card save exists on this harness; whether the item works with one was not tested.

### Language screen (for completeness, `09_language_f4650.png`)
Advisor holds a waving flag; the current language's name is centred between two arrow glyphs,
the neighbours are drawn smaller to each side. Ring order (index -> name), established by writing
the index word once at restore and reading the label ring that appears:
0 Deutsch, 1 Espanol, 2 Nederlands, 3 English, 4 Svenska, 5 Francais, 6 Italiano
(`sheet_poke1_lang.png`). **RAM 0x801EF3D0 (u32, and a copy at 0x801EF3D4)** holds the index:
**3 at rest, 2 after LEFT, 4 after RIGHT** (dumps at rel 60/100/300, all stable) [RAM].
LEFT -> Nederlands with the Dutch flag (`16_language_after_left_rel100.png`), RIGHT -> Svenska
with the Swedish flag (`17_language_after_right_rel100.png`) [png]. Caveat: a one-shot write of
the word re-labels the ring but the **flag stays the Union Jack** (all seven values), and a write
held every frame changes nothing at all (`sheet_poke_lang.png`) — the flag is switched by the
input handler, and the word is not the only state involved.

## D. Audio (emulator audio callback; "sound" = any non-zero sample in the frame's batch; 882 samples/frame)

| From | To | What | Evidence |
|-----:|---:|------|----------|
| 0 | 42 | silent | [audio] |
| 43 | 1028 | BIOS sounds: continuous non-silent, RMS peaks at frames 40-130 and 200-270, then a slow decay to near-silence by ~950 | [audio] rms per 20 frames in `cold1/audio.log` |
| 1029 | 2036 | silent (game has taken over; legal screen and loading are silent) | [audio] |
| 2037 | 2484 | BF.STR audio (starts 7 frames before its first video frame 2044, ends 3 before its last 2487) | [audio] |
| 2485 | 2504 | silent | |
| 2505 | 4452 | GRAV.STR audio (7 frames before first video frame 2512) | [audio] |
| 4453 | - | **silent through the black gap and the whole language screen** (checked to 7999) — no music on the language screen | [audio] |
| P+1 | P+10 | click SFX for the CROSS on the language screen | [audio] 4701-4710 |
| P+113 | P+158, P+161..P+206, P+209..P+302 | three menu SFX as the menu appears (first two identical, maxabs 10894) | [audio] |
| **P+305** | - | **main-menu music starts**, continuous (maxabs ~27000-31000), keeps playing through the Play Game submenu and Options; stops when Practice Park starts loading | [audio] 5005 (P=4700), 5506 (P=5200), `pg_d_cross` silence 801-1083 |

## Emulator caveats worth knowing
* The PAD type changes the boot: with `PAD_DUALSHOCK=1` the run diverges from the plain-pad run at
  frame **2282** (inside BF.STR), while every segment boundary in the table stays identical
  (`runs/ds/lang_none`). Not investigated further.
* The "PCSX" mark on the PlayStation logo screen is pcsx_rearmed's own patch of the BIOS text.
* pcsx passes NULL for duplicate frames; "last frame" of a static screen means the last frame
  before the next non-duplicate frame.

## FALSIFIERS

The three claims I was least sure of, each with the cheapest experiment that would break it.
**I then ran all three; results follow each one.**

1. **"0x801EF3D0 is the selected-language index, ring order De/Es/Nl/En/Sv/Fr/It."**
   Test: RIGHT twice from the state, dump RAM (predict 5), then CROSS: the main menu must be in
   French. **Ran it** (`runs/st/rr_cross`): word reads **5 / 5** at rel 250 [RAM], and the menu
   shows **Jouer / Options / Charger partie** (`22_menu_after_right_x2_cross.png`) [png]. Holds.
2. **"The FMV skip needs the button up at the init frame (2005) and down at a poll from the
   first displayed frame (2044) on."** Test: hold CROSS 2006-2043 only (up at 2005, released
   before 2044); the rule predicts no skip. **Ran it** (`runs/sw4/f2`): byte-identical to the
   no-input run through 2399. Holds.
3. **"CROSS on Main Game does nothing."** Test: press again at rel 1000 (8 frames) and rel 1100
   held 30 frames. **Ran it** (`runs/st/pg_late`): identical to the submenu-at-rest run through
   rel 1599. Holds — with no memory-card save present.

Next three least-certain claims, NOT yet tested:

* **"Any button skips the FMVs."** Only cross/circle/triangle/square/start/select were tried.
  Test: d-pad, L1/L2/R1/R2 at 2200; if any is identical to no press, "any" is wrong.
* **"The BIOS screens cannot be skipped from the pad."** Only cross and start at 300 and 800.
  Test: the other buttons at 100, 600, 900 — a divergence anywhere breaks it.
* **"Menu music starts at P+305 because of the menu, not the loading screen."** Only inferred
  from two values of P. Test: press cross on the language screen, then hold a d-pad direction
  through P+114..P+305; music timing must not move. Also test with the Options SFX sliders
  (not possible to reach before the music, so this one may stay unproven).

## NOT established
* What "Main Game" leads to (see falsifier 3), what "Load Game" does with a save present, what
  the Options sub-items Screen / Credits / Exit / Tutorial show, whether the Options sliders
  respond to LEFT/RIGHT beyond "the frame changed".
* The mechanism behind the language word (why a held write is ignored and the flag is not
  re-derived from it).
* Which BIOS sound is which: I have one continuous non-silent stretch 43-1028 with two RMS peaks,
  not two separated chimes.
* Why the FMV skip latency varies 4-7 frames (probably the next STR frame boundary; not checked).
* Why the DualShock pad type changes frame 2282.
* Anything about the intro on other language settings (the FMV files ENGLISH/FRENCH/SPANISH.STR
  were never read during this boot; what plays them is unknown).

## Files
* Report data: `fable/boot.json`. PNGs: `fable/b/png/`. Contact sheets: `fable/b/sheet_*.png`.
* Harness: `fable/b/runner_b.c` (instrumented runner), `run.sh` (cold run), `srun.sh` (from a
  state), `seg.py` (segment a hash log), `diverge.py` (first differing frame), `sheet.py`,
  `strmatch.py`. Save state: `fable/b/states/lang4700.st` (frame 4700, language screen, no input).
* Raw runs: `fable/b/runs/` (~2 GB; `cold1` is the canonical no-input boot).
