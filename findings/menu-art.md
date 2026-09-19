# Theme Park World (PSX, SLES-026.88): where the main menu, language screen and "Now Loading" art lives

Twelfth report. Independently corroborated before being committed: the port's own sheet parser, which
never saw this report, reads entry **84** as texpage 0x18, **6x1 pages, 136 sprites** and entry **416**
as texpage 0x0e, **2x2 pages, 894 sprites** — matching the page ranges and the sprite indices used
throughout below. `menu-art.json` holds the machine-readable form, including the per-screen 2D
primitive lists this report's reconstruction was drawn from.

⚠ THE LAYOUTS ARE CAPTURED FRAMES, NOT THE GAME'S LAYOUT RULES. `layouts.menu_2d_frame450` is the 74
primitives the GPU was given at ONE instant, with the highlight on one particular item and the
spotlight at one particular angle. Replaying it reproduces that frame exactly and is the right way to
draw the static backdrop; it is NOT the menu's logic, and anything that moves (the highlight, the
spotlight fan, the advisor's pose) has to come from the game's behaviour, which §C of boot.md covers.

Measured on the libretro harness from the language-screen save state `fable/b/states/lang4700.st`
(CROSS pulsed at rel 20-27, so P = 20). VRAM + RAM dumped at rel **5** (language screen), **60** (jester,
P+40), **450** (main menu at rest, P+430) with `fable/m/runner_m` (runner_b plus VRAM on `DUMP_LIST`).
Everything below is tied one of three ways, and each row says which:

* **[hash]** an 8 KB VRAM block equals a decoded disc block byte-for-byte at the position the sheet header predicts
* **[dlist]** the GPU display list, reconstructed from the RAM dump (`fable/m/dlist.py`), names the texture page,
  CLUT and UV rectangle, and those are looked up in the sheet's own sprite table
* **[render]** the whole 2D layer of the screen was re-drawn from the DISC bytes through those display-list quads
  (`fable/m/recon.py`) and matches the emulator frame: `png/recon_menu_2d.png` vs `png/c20_frame_000456.png`,
  `recon_language_2d.png` vs `fable/b/png/09_language_f4650.png`, `recon_jester_2d.png` vs `c20_frame_000060.png`

Scratch, tools, dumps and every PNG named here: `fable/m/` (`png/` for images). Machine-readable version of
this report, including the full sprite records with their 16-colour palettes and the raw per-screen 2D
primitive lists: `fable/menuart.json`.

## The table

| piece | entry | format | palette (CLUT word -> VRAM x,y; lives inside the same sheet) | tied how |
|---|---|---|---|---|
| menu curtain, top drapes | **84** sprite #1, 122x124, rotated | texture sheet, UNPAK'd, page 0x18 | `0x4163` -> (560,261) | hash + dlist + render |
| menu curtain, valance | **84** sprite #2, 135x53 | page 0x1b | `0x41a0` -> (512,262) | hash + dlist + render |
| menu curtain, side drapes | **84** sprite #3, 91x116, rotated | page 0x18 | `0x41a1` -> (528,262) | hash + dlist + render |
| menu floor | **84** sprites #131 #132 #133 #134, 128x43 each (#131-133 rotated) | pages 0x1a/0x1b/0x1b/0x1c | `0x4121 0x4122 0x4123 0x4160` -> (528,260) (544,260) (560,260) (512,261) | hash + dlist + render |
| THEME PARK WORLD logo | **84** sprites #114 (left half "theme"+"WO", 166x70, rotated) and #115 (right half "PARK"+"RLD", 171x73) | pages 0x19 | `0x4060` -> (512,257), `0x4061` -> (528,257) | hash + dlist + render |
| highlight glow under the selected item | **84** sprites #129 (69x44 cap) + #130 (8x44 strip) | page 0x18, drawn additive | `0x40e3` -> (560,259), `0x4120` -> (512,260) | dlist + render |
| sweeping spotlight | none: 35 untextured additive Gouraud quads per frame | - | - | dlist |
| menu font (orange) | **84** sprites #9..#100, one CLUT for all | pages 0x18..0x1c | `0x41e3` -> (560,263) | dlist + render; glyph order derived from the drawn strings |
| advisor on the menu | **83** sub-mesh **7** (72 verts, 12 bones, 103 faces) | 0x96 model pack, skinned mesh | textures in **416** (below) | face-UV match + RAM pointers |
| language screen flags | **84** sprites #119-#125, 150x87 each | pages 0x18/0x19/0x1a/0x1b/0x1c | `0x40a1 0x40a2 0x4021 0x4023 0x4022 0x4020 0x40a3` (rows 256-258 of page 0x18) | hash; English and Nederlands verified in the flag slot |
| language screen advisor + flagpole + flag | **83** sub-mesh **10** (205 verts, 29 bones, 319 faces) | skinned mesh | flag: slot sprite #126 `0x40e0` -> (512,259); pole: #127 `0x40e1` -> (528,259); body: 416 | 217/217 drawn triangles are its faces + RAM pointers |
| language names | **84** font, drawn additive at 3 brightnesses | same as menu font | `0x41e3` | dlist + render |
| language arrows | none: 8 flat additive triangles | - | - | dlist + render |
| language / loading background | none: one Gouraud quad, RGB(153,163,254) -> RGB(42,32,87) | - | - | dlist + render |
| jester ("Now Loading") | **0** sub-mesh **0** (193 verts, 17 bones, 301 faces, LZ-compressed sub-entry) | 0x96 model pack | textures in **416** | 32/37 drawn triangles are its faces; entry 83 not even resident |
| "Now Loading..." text | **416** small white font, sprites 410-491 | texture sheet, UNPAK'd, page 0x1f/0x1e | `0x067a` -> (928,25) | dlist + hash |
| advisor / jester skin | **416** sprites #0 #2 #3 #4 #12 #17 #43 #44 + 1x1 flats #23 #24 #25 #31 #45 | texture sheet | each sprite's own CLUT (0x8f8, 0x8fa, 0x8fb, 0x938, 0x979, 0x9b8, 0x7ba, 0x7bb, 0x7a, 0x7b, 0x7c, 0xb9, 0x3c) | face CLUTs of the meshes = these sprites' CLUTs |

Nothing on these three screens comes from anywhere else: at the main menu **every non-blank texture block in
VRAM is a block of sheet 84 or sheet 416** (20/20 and 16/16 by hash). The 131,072-byte headerless banks
(0x104/0x10C) and the 0x20054 raw sheets are not involved.

## 1. VRAM on the three screens

`png/vram_000005.png`, `vram_000060.png`, `vram_000450.png` (1024x512, 15-bit view).

| region | content | evidence |
|---|---|---|
| (0..511, 0..511) | the two 512x256 framebuffers | the screen image is visible in it |
| (512..895, 256..511) | **sheet 84**, six pages 0x18..0x1d in a row | 20/20 non-blank blocks hash-equal at frame 450 |
| (896..1023, 0..511) | **sheet 416**, 2x2 pages 0x0e 0x0f / 0x1e 0x1f | 16/16 blocks hash-equal on all three dumps |
| (512..895, 0..255) | black | - |

At frames 5 and 60 three blocks of sheet 84 differ from the disc: (512,256), (640,320), (640,384). They are
exactly one CLUT row and one 150x87 rectangle, the flag slot, see 4.1. At the menu the sheet has been reloaded
and is disc-identical again.

Entries whose bytes sit **verbatim in RAM** (`fable/m/resident.py`): 0, 83, 299 (menu XM), 405, 413 and the
headers + sprite tables of 84 (1648 bytes) and 416 (10744 bytes) on all three screens; 83 is unloaded during
the jester (frame 60) and back by frame 90; 276, 277, 278 (the park-select map sheet), 306-308 arrive with the
menu. The sheets' pixel bytes are not in RAM: they went to VRAM, the HDRS copy of the header stayed.

## 2. Sheet 84, the front-end sheet

Entry 84: file offset 0x248800, 77,555 bytes. Header `0x88 0x00 | 6 pages | tpage 0x18 | 6 cols | 1 row | 1 free
rect | compressed 1`, then 136 sprite records and one free rectangle, then 24 UNPAK'd 64x64-halfword blocks
(block 0 raw), exactly as `TextureSheet.cs` / 5p describe. Decoded and rendered sprite by sprite with each
sprite's own CLUT: `png/sheet_0084_sprites.png`; every sprite cut out and labelled: `png/s84_all_sprites_atlas.png`.

**Rotated sprites.** Sprite flags bit 0 is set on 46 of the 136 sprites (#1, #3, #4-#7, #114, #116, #117,
#131-#133, 25 glyphs, and the Japanese strings #102 #106 #109-#112). The menu quads show what it means: for sprite #1 drawn at (10,5)-(132,129)
the UVs are (1,132) (1,10) (125,132) (125,10), so **screen x runs along v decreasing and screen y along u
increasing**: the sprite is stored turned a quarter turn, and `w`/`h` in the record are the on-screen size, so
the texture rectangle is `h` wide by `w` tall. Un-rotate with a clockwise quarter turn (`np.rot90(a, -1)`,
`fable/m/pieces.py`). The mirrored right-hand copies of the curtain reverse v instead.

Sprite table (full list with palettes in the JSON; `fable/m/sheet84_sprites.txt` is the raw dump):

| # | page (VRAM) | uv | w x h | rot | CLUT | what | PNG |
|---|---|---|---|---|---|---|---|
| 1 | 0x18 (512,256) | 1,11 | 122x124 | yes | 0x4163 | top-left curtain drape, gold fringe, rope tie | `s84_001_curtain_drape_topleft.png` |
| 2 | 0x1b (704,256) | 1,175 | 135x53 | no | 0x41a0 | valance (top swag with fringe) | `s84_002_valance_top.png` |
| 3 | 0x18 | 125,11 | 91x116 | yes | 0x41a1 | side drape (lower left/right) | `s84_003_curtain_side_drape.png` |
| 114 | 0x19 (576,256) | 151,1 | 166x70 | yes | 0x4060 | logo left half: "theme" box + "WO" | `s84_114_logo_theme.png` |
| 115 | 0x19 | 1,175 | 171x73 | no | 0x4061 | logo right half: "PARK" box + "RLD" | `s84_115_logo_PARK.png` |
| 129 | 0x18 | 151,133 | 69x44 | no | 0x40e3 | yellow glow end cap | `s84_129_glow_endcap.png` |
| 130 | 0x18 | 151,177 | 8x44 | no | 0x4120 | glow middle strip (stretched) | `s84_130_glow_middle.png` |
| 131 | 0x1a (640,256) | 207,1 | 128x43 | yes | 0x4121 | floor plank, screen x 0..128 | `s84_131_floor_plank_A.png` |
| 132 | 0x1b | 151,1 | 128x43 | yes | 0x4122 | floor plank, x 128..256 | `s84_132_floor_plank_B.png` |
| 133 | 0x1b | 194,1 | 128x43 | yes | 0x4123 | floor plank, x 256..384 | `s84_133_floor_plank_C.png` |
| 134 | 0x1c (768,256) | 1,88 | 128x43 | no | 0x4160 | floor plank, x 384..512 | `s84_134_floor_plank_D.png` |
| 119 | 0x18 | 1,133 | 150x87 | no | 0x40a1 | flag: Netherlands (ring 2) | `s84_119_flag_119_netherlands.png` |
| 120 | 0x19 | 1,1 | 150x87 | no | 0x40a2 | flag: United Kingdom (ring 3, English) | `s84_120_flag_120_uk.png` |
| 121 | 0x19 | 1,88 | 150x87 | no | 0x4021 | flag: France (ring 5) | `s84_121_flag_121_france.png` |
| 122 | 0x1a | 1,1 | 150x87 | no | 0x4023 | flag: Germany (ring 0) | `s84_122_flag_122_germany.png` |
| 123 | 0x1c | 1,1 | 150x87 | no | 0x4022 | flag: Italy (ring 6) | `s84_123_flag_123_italy.png` |
| 124 | 0x1b | 1,88 | 150x87 | no | 0x4020 | flag: Spain (ring 1) | `s84_124_flag_124_spain.png` |
| 125 | 0x1b | 1,1 | 150x87 | no | 0x40a3 | flag: Sweden (ring 4) | `s84_125_flag_125_sweden.png` |
| 126 | 0x1a | 1,88 | 150x87 | no | 0x40e0 | **flag slot** (on disc: "TPW Boot Boys" placeholder) | `s84_126_flag_slot_placeholder.png` |
| 127 | 0x18 | 249,133 | 6x88 | no | 0x40e1 | flagpole (wood) | `s84_127_flagpole.png` |
| 9-34 | various | | ~24x21 | mixed | 0x41e3 | font A-Z | `s84_font_atlas_9-100.png` |
| 35-44 | | | | | 0x41e3 | font 0-9 | same |
| 45-64 | | | | | 0x41e3 | font punctuation (see 3.4) | `s84_punct_45-64_x3.png` |
| 65-90 | | | | | 0x41e3 | font a-z | atlas |
| 91-100 | | | | | 0x41e3 | font accents (see 3.4) | `s84_accents_91-100_x3.png` |
| 4,5,6,7 | 0x18 | | 21x14 | yes | 0x41a2.. | PS triangle / circle / cross / square icons | `s84_misc_x3.png` |
| 128 | 0x1c | 193,203 | 14x14 | no | 0x40e2 | music note | same |
| 135 | 0x18 | 1,220 | 32x32 | no | 0x4161 | wooden crate | same |
| 0, 8, 101-113, 116-118 | | | | | | Japanese menu strings (クレジット, もどる, むずかしい, サウンド, BGM ...) and a small "theme Park" logo (#107): leftovers, not drawn on this disc's screens | atlas |

The 136 sprites use 44 distinct CLUTs, all in rows 256..266 of page 0x18 (VRAM x 512..575), i.e. the sheet's
own top-left corner, as 5p says for every sheet.

## 3. The main menu, piece by piece (frame 450 display list, `fable/m/menu_2d.txt`)

The 2D layer is 38 primitives: 6 curtain quads, 4 floor quads, 2 logo quads, 3 glow quads and 23 glyph quads,
plus the spotlight fan and the advisor mesh. All 2D quads are axis-aligned POLY_FT4/GT4 with the shade word
0x808080 (= x1.0).

### 3.1 Curtains and floor
```
drape      0x2c (10,5)-(132,129)    uv (1,132)(1,10)(125,132)(125,10)     clut 0x4163 page 0x18   <- #1 rotated
drape mirr 0x2c (392,5)-(514,129)   uv (1,11)(1,133)(125,11)(125,133)     clut 0x4163 page 0x18   <- #1 rotated, v reversed
valance    0x2c (132,5)-(267,58)    uv (1,175)(136,175)(1,228)(136,228)   clut 0x41a0 page 0x1b   <- #2
valance m  0x2c (270,5)-(405,58)    uv (135,175)(0,175)(135,228)(0,228)   clut 0x41a0 page 0x1b   <- #2 mirrored
side       0x2c (10,129)-(101,245)  uv (125,101)(125,10)(241,101)(241,10) clut 0x41a1 page 0x18   <- #3 rotated
side mirr  0x2c (423,129)-(514,245) uv (125,11)(125,102)(241,11)(241,102) clut 0x41a1 page 0x18
floor      0x2c (0,213)-(128,256)   uv (207,128)(207,0)(250,128)(250,0)   clut 0x4121 page 0x1a   <- #131 rotated
           0x2c (128,213)-(256,256) uv (151,128)(151,0)(194,128)(194,0)   clut 0x4122 page 0x1b   <- #132 rotated
           0x2c (256,213)-(384,256) uv (194,128)(194,0)(237,128)(237,0)   clut 0x4123 page 0x1b   <- #133 rotated
           0x2c (384,213)-(512,256) uv (1,88)(129,88)(1,131)(129,131)     clut 0x4160 page 0x1c   <- #134
```
The black between the drapes is the cleared framebuffer, not a texture.

### 3.2 Logo
```
0x2c (87,66)-(253,136)  uv (151,166)(151,0)(221,166)(221,0)  clut 0x4060 page 0x19   <- #114 rotated
0x2c (253,64)-(424,137) uv (1,175)(172,175)(1,248)(172,248)  clut 0x4061 page 0x19   <- #115
```
`png/check_pieces_strip.png` shows the two halves un-rotated and butted together.

### 3.3 Highlight glow and spotlight
The selected item is not recoloured. Three **additive** quads (0x2e, tpage 0x0038 = page 0x18 with abr=1)
sit behind it: cap #129 at (105,130)-(174,174), the same cap mirrored at (338,130)-(407,174), strip #130
stretched over (174,130)-(338,174). Orange glyphs plus additive yellow = the bright yellow "Play Game".

The beam is not art: 35 Gouraud quads (0x3a) per frame share two vertices at the off-screen apex (-103,-64)
and put the other two about 15 px apart along an arc that passes through (411,319)..(644,113); colours
alternate 0x000000 and 0x001818 (BBGGRR, so dark yellow) and they are additive because a degenerate textured
triangle (0x27, three vertices at (512,512)) latches tpage 0x0020 (abr=1) just before them. The arc end
changes every frame, which is the sweep boot.md saw in the hashes. `menuart.json` has one frame of it.

### 3.4 Font
Glyph quads are opaque POLY_GT4 (0x3c), all with CLUT 0x41e3 at (560,263), shade 0x808080; the disabled
"Load Game" is shaded 0x202020. Items at frame 450: Play Game glyphs x 175..344 y 139..165, Options x 194..325
y 171..196, Load Game x 169..350 y 203..224. Each glyph quad's UV rectangle is one sprite of sheet 84; mapping
the drawn strings ("Play Game", "Options", "Load Game", and on the language screen "Español", "Nederlands",
"English", "Svenska", "Français") back to sprite indices gives a **fixed offset per case**: `a`..`z` are
sprites 65..90 (index = ASCII - 32), `A`..`Z` are 9..34 (ASCII - 56), ç = 99, ñ = 100. The atlas then reads:

| sprites | glyphs | how read |
|---|---|---|
| 9-34 | A B C ... Z | from P, L, O, S, E, N, F, G in drawn strings + atlas |
| 35-44 | 0 1 2 3 4 5 6 7 8 9 | atlas `s84_font_atlas_9-100.png` |
| 45-64 | · ! " % & * ( ) - : ; , ? \ / £ $ ' [ ] | by eye, `s84_punct_45-64_x3.png` |
| 65-90 | a b c ... z | from a d e g h i k l m n o p r s t v y in drawn strings |
| 91-100 | ü å ö ú é á ä î ç ñ | by eye, `s84_accents_91-100_x3.png`; ç and ñ confirmed by "Français"/"Español" |

Glyph advance on screen is the sprite width minus 1-3 px (e.g. "Play": P at 175 w 23, l at 194, a at 202, y at 222).
The record's `offset` bytes are 0 for every glyph except a few with oy=1; the drawn quads use the sprite's w/h.

### 3.5 Advisor
Textured Gouraud triangles (0x34) with CLUTs 0x7b9, 0x7ba, 0x7bb, 0x9b8, 0x938, 0x979, 0x8f8, all pointing into
sheet 416. Of the 31 distinct (uv, clut, page) triangles in the list 23 are faces of **entry 83 sub 0-9** (the
ten share one face/UV set; the 8 others carry nonsense CLUTs and are false positives of the packet scan).
Which of the ten: RAM pointers at frame 450 into entry 83 (loaded at 0x80169920) hit **sub 7** twelve times,
four of them at its first byte and one at +0x48 (the vertex array), and no other sub-entry more than twice.
The same test on the language screen picks sub 10 (7 hits), agreeing with the UV match there, so the test
discriminates. The ten differ in vertex positions and animation tracks (sub 3 and 7 share sub 0's vertices):
ten poses of one 72-vertex advisor.

## 4. The language screen (frame 5, `dl_000005.txt`)

* **Background**: one Gouraud quad 0x38 (0,0)-(512,256), colours 0xfea399 top / 0x57202a bottom (BBGGRR), i.e.
  RGB(153,163,254) fading to RGB(42,32,87). No texture.
* **Names**: the sheet-84 font again, but as POLY_GT4 **semi-transparent** (0x3e) with tpage 0x003b (abr=1,
  additive) and shade 0x646464 for the centre word, 0x2e2e2e for its two neighbours, 0x191919 for the outer
  two; additive orange over blue is the lilac look. Positions at rest: "Español" x -34.., "Nederlands" 6..194
  y 179-200, "English" 200..318 y 192-213, "Svenska" 347..488, "Français" 415..561.
* **Arrows**: 8 flat semi-transparent triangles (0x22), four per side, each 32 px wide and 24 tall at y 192..216,
  left set x 77..157 pointing left, right set 353..433 pointing right, colours 0x011012 0x053d44 0x096a76
  0x0d97a8 from outer to inner (dark to gold).
* **Advisor + pole + flag**: entry **83 sub 10**. 217 of the 217 distinct textured triangles in the list are
  faces of that mesh (its face set has 222 distinct UV/CLUT triples). 192 of them are the flag (page 0x1a,
  CLUT 0x40e0, UVs inside (1..150, 88..174) = sprite #126), 2 are the pole (page 0x18, CLUT 0x40e1 = #127,
  flat-textured 0x24), the rest the body from sheet 416.

### 4.1 The flag slot
On the disc sprite #126 (page 0x1a, uv (1,88), 150x87, CLUT 0x40e0 at (512,259)) holds a white box reading
"TPW Boot Boys". On the language screen that rectangle and that CLUT row are the only three blocks of sheet 84
that differ from the disc (`png/flagarea_page1a_clut40e0_f5.png` vs `_f450.png`): the slot holds the Union
Jack and row 259 holds the Union Jack's palette. Halfword-exact compare of the slot against the seven flag
sprites (38 columns x 87 rows): **#120 97.4%** (the last column differs because u=1 is not halfword aligned),
every other flag under 8%, and row 259 == #120's CLUT row (0x40a2 at (544,258)). Pressing LEFT (Nederlands)
and dumping again: slot == **#119** 97.4%, CLUT row == #119's (`runs/left20`). So the game copies the chosen
flag's texels and palette into the slot, and the flag mesh never changes. Ring index (boot.md) to sprite:
0 Deutsch #122, 1 Español #124, 2 Nederlands #119 (verified), 3 English #120 (verified), 4 Svenska #125,
5 Français #121, 6 Italiano #123; the five unverified ones are named by the flag itself.

For the port: draw the flag mesh with the selected language's flag sprite and that sprite's own CLUT; the slot
and its placeholder CLUT are never what is seen.

## 5. "Now Loading" (frame 60, `dl_000060.txt`)

* **Background**: the same gradient quad as the language screen (0xfea399 -> 0x57202a).
* **Iris**: a fan of flat quads (0x2a, white, semi-transparent) from the centre (256,128), growing each frame;
  with the latched blend mode it darkens rather than lightens. No art.
* **Jester**: entry **0 sub 0** (193 verts, 17 bones, 301 faces; the sub-entry is LZ-compressed, 0x800BFD9C,
  to 0x3fc8 bytes). 32 of the 37 distinct textured triangles in the list are its faces (the 5 others are scan
  junk with impossible CLUTs). Entry 83 is not in RAM at frame 60 at all, so the jester is not the menu advisor
  re-dressed at runtime. Its skin: sheet 416 eyes #0 (CLUT 0x8f8, 30x32), #3 (0x8fb), body sphere #4 (0x938,
  48x47), and the 1x1 flat-colour sprites #23 #24 #25 #31 (CLUTs 0x7a 0x7b 0x7c 0xb9) for the hat, bells and
  balls (`png/s416_advisor_sprites.png`).
* **Text**: opaque POLY_GT4 (0x3c) glyphs 8-13 px tall, CLUT **0x067a at (928,25)**, pages 0x1f/0x1e/0x0f of
  **sheet 416**, baseline y 220, x 208..306. Glyph rects match sprites N=423 o=480 w=488 L=421 a=466 d=469
  i=474 n=479 g=472 and the dot = 463 (drawn three times): lowercase = ASCII + 369, uppercase = ASCII + 345,
  which is the big font's order shifted by +401 (big A=9, small A=410). 155 sprites carry CLUT 0x67a;
  `png/s416_loading_font_clut067a.png` shows them all.

## 6. Sheet 416 pieces used here

Entry 416: offset 0x7c9000, 103,640 bytes, 894 sprites, 2x2 pages at tpage 0x0e -> VRAM (896,0). It is the
in-game UI sheet (icons, faces, fonts, `png/sheet_0416_sprites.png`); the front end borrows the advisor skin
and the small font from it. Advisor/jester sprites (`png/s416_advisor_sprites.png`): #0 eyes 30x32 (0x8f8),
#2 15x7 (0x8fa), #3 18x14 (0x8fb), #4 body sphere 48x47 (0x938), #12 badge 24x24 (0x979), #17 16x8 (0x9b8),
#40 16x16 (0x9b8), #43 orange 24x24 (0x7ba), #44 green 24x24 (0x7bb), #39/#46 4x4 (0x7b9), flats #23 #24 #25
#31 #45. Every face CLUT the three meshes use is one of these sprites' CLUTs, which is how the skin was
attributed: the faces carry their CLUT, the sheet's sprite table says which rectangle that CLUT colours.

## 7. Where the mesh triangles come from, in numbers

| screen | distinct drawn (uv,clut,page) | entry 83 sub 0-9 | 83 sub 10 | entry 0 sub 0 | left over |
|---|---|---|---|---|---|
| menu (450) | 31 | **23** (of 26 in the set) | 23 (shares them) | 12 | 8, all with CLUTs that exist in no sheet |
| language (5) | 217 | 22 | **217** (of 222) | 9 | 0 |
| jester (60) | 37 | 10 | 10 | **32** (of 52) | 5, junk CLUTs |

(`fable/m/meshmatch.py`; entry 0 sub 0's 12/9/10 hits on the other screens are the shared face and eye
sprites, which every advisor variant uses.)

## 8. Not established
* Which pose (sub 0-9 of entry 83) the menu advisor plays at other moments; only frame P+430 was read (sub 7).
* The GPU command that fills the flag slot (a 0x80 VRAM copy or a draw into the page): only the resulting VRAM
  was measured. The result is a plain copy of the sprite's texels and CLUT row.
* Punctuation #45-#64 and accents #91-#100 are read by eye from the zoomed atlases; big-font #62 looks like an
  apostrophe while the small font's slot 62+401=463 is the period "Now Loading..." uses, so either #62 is a
  low-set period or the two orders differ there.
* No rendered PNG of the three meshes; the report gives entry/sub, dimensions and every texture they sample.
* The submenus (Play Game, Options) and Load Game with a save present were not captured.

## 9. Files
* `fable/menuart.json`: everything above as data, with each sprite's 16-colour palette and the raw 2D
  primitive lists for the three frames (the menu lists contain each quad twice: both frame buffers are in RAM).
* `fable/m/sheet.py` (sheet decoder, Python port of TextureSheet.cs), `vram.py`, `resident.py`,
  `dlist.py` (display-list reconstruction), `meshmatch.py`, `pieces.py` (sprite renderer with un-rotation),
  `recon.py` (2D layer re-render), `mkjson.py`, `runner_m.c` (runner_b + VRAM on DUMP_LIST).
* Dumps: `fable/m/runs/c20/` (rel 5/60/90/450 RAM+VRAM, frames every 10), `runs/left20/` (LEFT, rel 100).
* Display lists: `fable/m/dl_000005.txt`, `dl_000060.txt`, `dl_000450.txt`; filtered menu 2D: `menu_2d.txt`.
* PNGs: `fable/m/png/` as named above.


## Correction: the language screen's flag IS a stored mesh (2026-09-19)

An earlier note here concluded the 12x8 flag cloth was generated in code, because no sub-mesh on the
disc has 192 faces and none has 117 vertices. **That was the wrong question.** The flag is 192 of the
319 faces of **entry 83 sub 10** -- the language-screen advisor, 205 verts, 29 bones -- carrying CLUT
0x40e0 over u 1..149, v 88..173, which is exactly the range measured off the console's display list.
The remaining 88 vertices are his body and the pole.

The search that missed it fingerprinted WHOLE meshes, so an object that is part of a bigger object was
invisible to it, and its silence read as "generated". Found by cow tools, who spotted that 205 - 117 = 88
and asked the question the other way round; confirmed here with `--faceclut 83 10`.

Consequence: the wave is BONE ANIMATION, not a cloth simulation, and the port already decodes that
format (MeshAnimation.cs / MeshPose.cs). So the flag and the advisor are one job with one blocker --
the boot and menu screens are a 2D pixel renderer with no 3D path -- not two separate problems.
