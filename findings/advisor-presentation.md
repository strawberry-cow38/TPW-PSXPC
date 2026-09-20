# Theme Park World (SLES-026.88) — the advisor's presentation

`advisor.md` decoded the decision machine and stopped at the boundary it named in its §6: what consumes
the rise (`+0xE8`), the turn (`+0xEC`), the gesture (`+0x1B1`) and the mood (`+0x184`), where the caption
goes, what the two payload fields are, and what the pad tail does. This is that half.

**Marks.** READ = instructions at the quoted TPW.BIN address (two methods: the Ghidra decompile corpus and
`mdis.py` on the image; every load-bearing claim below was checked in the assembly as well, and says so).
MEASURED = bytes of FOLIO.GAZ or of the image's data. GUESS = interpretation, with a confidence. Nothing in
this file was observed on a running console; §7 lists what a live capture would settle.

## 0. Corrections to `advisor.md`

* **The clock is VSync frames, not ticks** (READ). `0x80050560` accumulates `0x800BB380() − last` while
  the pause flag `[0x80102D20]` is clear; `0x800BB380` returns `[0x80103A50]`, and the VSync callback at
  `0x800BB068` increments exactly that word (`lw v0,0x13FC(gp) / addiu v0,v0,1 / sw`, gp = 0x80102654).
  So *arrive* is 50 frames = **1.0 s** PAL, *leave* 1.0 s, *away* 100 frames = **2.0 s**, and the "about
  200 ticks plus the recording" of advisor.md §1 is **4 s plus the recording**. Only state 0's countdown
  is in calls (it steps by exactly 1).
* `0x800385AC` does not draw a text box; it appends a record to the **message list** (§3). `0x80038C8C`
  does not close a box; it restores the panel's four button labels after a **modal** message (§5).
* `0x800141BC` is not a formatter and the string table has no format codes: it is three stores (§4).
* The pad tail does not act on *release*: `0x800897EC(0)` returns the **press-edge** word, and the
  dismissal runs on the next advisor tick after the edge (§5).
* `+0xB5` (the value `0x80013B50` returns in states 2..4) is not "visible"; it is **modal** (§5). He is
  drawn whenever flag bit 2 is set and the state is 2, 3 or 4, regardless of `+0xB5`.
* The gesture table `0x800EFB90` is each clip's animation length **minus one** — the archive agrees (§2).
* The queue's read cursor (advisor.md §6, last item) advances **on delivery**: `0x80013E6C` shows the
  entry at `+0xA9` and then steps `+0xA9 = (+0xA9 + 1) % 20`, called once when the arrival completes
  (state 2 → 3). The port's placement is the game's (READ).

## 1. Where he is drawn — `0x800136F4(advisor)` (READ, asm-checked)

Its only caller is the frame renderer `0x8005814C`, after the world (`0x8003186C`) and before
`0x80089CF4`, inside `if (mainView != 0)`; the tick `0x80013180` draws nothing. `0x800136F4` runs when
**flag bit 2** (`andi 4`) is set and `state ∉ {0, 1, 5}`, i.e. arriving, speaking, leaving.

### 1.1 His own camera and projection
The advisor object (0x1C4 bytes, allocated at `0x800588D0`, built by `0x8006170C` + `0x80012E40`) holds a
**view object at `+0xFC`** (class ctor `0x80061AAC`) whose camera sits at `+0x104` (ctor `0x8002B104`).
`0x80012E40` places it (READ, asm `0x80012E8C..0x80012F40`): position `{0, 0, −256}` from
`0x800DB930`, then the look-at `0x8002B15C(cam, eye = {0,0,0}, up = {0, −4096, 0})` from `0x800DB940`.
Worked through (`VectorNormal` 0x800C9148, `OuterProduct12` 0x800C90C0, `ApplyMatrixLV` 0x800C8F60,
rows assembled at `0x8002B218`):

```
rows:  right = ( 1, 0, 0)   up' = (0, −1, 0)   fwd = (0, 0, 1)        t = (0, 0, +256)
0x8002B31C copies it to cam+0x60 with row 0 and t.x × 0x1999/4096 = 1.6   (the 320→512 stretch)
```
`0x800136F4` makes this the current view (`0x80058F1C(adv+0xFC)`), sets **`SetGeomOffset(416, 176)`**
(`addiu a0,zero,416 / addiu a1,zero,176 / jal 0x800C08B0`), draws, then restores `SetGeomOffset(w/2, h/2)`
(`0x800BB424/430` = the 512×256 framebuffer of panel.md; `w/2 = 256, h/2 = 128`) and the saved view.
The projection distance is **H = 256**: `SetGeomScreen` (= `0x800C94C0`, the only `ctc2 $26` outside
libgte init) is called with `[0x80102D54]` at the top of `0x8005814C` and in `0x80054C1C`; the image holds
256 there and no `sw` to it exists by gp or by absolute address (READ the value; GUESS-high it is never
rewritten). So model-space z = 0 sits exactly at the projection plane: **one model unit = 1 px vertically
and 1.6 px horizontally** before his own scale, and world +Y is screen up (row up' = −Y under a
Y-down raster).

### 1.2 The transform — exactly how `+0xE8` and `+0xEC` are used
```
m = 32-byte GTE MATRIX, zeroed, then
    m[0][0] = m[1][1] = m[2][2] = (+0xE8 >> 2)      sra by 2: 1200 → 300, i.e. scale 300/4096 = 0.0732
RotMatrixZ((+0xEC >> 1), &m)                        0x800C05B0; angle in 4096ths of a turn, masked 0xFFF
handle.matrix (adv+0xBC+0xC, via 0x80031138) = m
0x8002FE14(handle, T[gesture], +0xF8, 0)            draw body clip at frame +0xF8
0x800303D4(handle, T[gesture], 0, &b)               b = bone matrix of the clip's trailing-list[0] bone
CompMatrix(&m, &b, &m2); handle.matrix = m2         0x800C0750
if (mood != 16) 0x8002FE14(handle, T[mood], +0xF8, 0)   draw the face, now attached to that bone
```
`T` is the **byte** table at `0x800EFB9C` (`lbu` after `addu` with the index — no shift):
`T[i] = i + 1` for i = 0..15, `T[16] = 0`. `0x8002FE14` composes `cam+0x60 × handle.matrix` into the
GTE (SetRotMatrix/SetTransMatrix 0x800C4C24/0x800C4C04), evaluates the frame if it changed
(`0x8002CBC4`, frame taken **modulo the clip's length** at `0x8002CC94`, `divu` by mesh+0) and emits the
polygons (`0x8002E43C`), subject to the sphere cull `0x80011340`.

**Rotation is about Z**, i.e. in the screen plane. Arriving, `+0xEC` runs 0 → 0x8000 in 50 frames, so the
angle runs 0 → 0x4000 = **four full turns while he grows from nothing**; leaving unwinds them. Speaking,
`+0xEC` drifts toward `0x7F38 + rand(400)` at 1 per frame, i.e. the angle wanders in
`(0x7F38..0x80C7) >> 1 mod 4096 = −100..+99` = **±8.8°** around upright — a slow lean (200 frames across
the band). GUESS-medium on the *sign* (which way he spins); READ on everything else.

### 1.3 The model — FOLIO entry 0 (READ), and what is in it (MEASURED)
`0x80012E4C..E84`: `addiu a0,s3,0xBC / addu a1,zero,zero / jal 0x800311F4` — the resource handle at
`+0xBC` (base ctor `0x8002FD94`, folio.md §2) is bound to **FOLIO entry 0** (a1 flows
`0x80031228 → 0x80031254(a1) → 0x8003136C(a1)` lookup in the 80-slot resource table at `0x800F112C`, else
`0x80031300` create, then `0x800312A8(res, a1)`; the same path other callers use with entry ids, e.g.
`0x59` from `0x800384CC`). This is **not** the port's `AdvisorModel` (entry 83 sub 10): that one was
identified from the language screen's display list and is that screen's advisor; the park draws entry 0.

Entry 0 is a model pack of 17 LZ sub-entries (all expand to their declared sizes with the port's
`SubLz` algorithm). Headers per folio.md §3.1, vertex extents from the vertex table:

| sub | role via `T` | anim len | bones | verts | extents x / y / z | list[0] |
|---:|---|---:|---:|---:|---|---:|
| 0 | unreached in the park | 151 | 17 | 193 | −871..737 / −543..1075 / −697..272 | 2 |
| 1 | gesture 0 | **51** | 10 | 44 | −525..528 / −543..276 / −251..116 | **2** |
| 2 | gesture 1 | **76** | 10 | 44 | same rest pose | **8** |
| 3 | gesture 2 | **126** | 10 | 44 | same | 8 |
| 4 | gesture 3 | **226** | 10 | 44 | same | 8 |
| 5 | gesture 4 | **301** | 10 | 44 | same | 8 |
| 6, 7 | unreached | 1 | 0 | 36, 45 | static | — |
| 8..15 | moods 7..14 | 1 | 0 | 24..45 | static faces, y ≈ +16..+668 | — |
| 16 | mood 15 | 1 | 0 | 14 | static | — |

⭐ **Two sources agree on the gestures:** the `u16` table at `0x800EFB90` = `50, 75, 125, 225, 300`
(`0x80013B74` wraps when `+0xF8` exceeds it) and the clips' own lengths = 51, 76, 126, 226, 301. The table
is `length − 1`, so every frame plays exactly once per cycle.

**Up is +Y** (GUESS-high): all five body clips and sub 0 share the floor `y = −543` with tops at
+276 / +1075, entry 83's clips share the same floor; a shared *minimum* is a ground plane only if Y is up,
and the camera above renders +Y upward. No live frame of him exists to make this MEASURED.

### 1.4 Where that puts him on screen (READ-derived arithmetic)
At full rise (scale 0.0732; x further ×1.6): the 44-vertex body spans **x 354..478, y 156..216** of the
512×256 frame (origin at 416,176; feet at 176 + 543·0.0732 ≈ 216); the face rides on bone 2 or 8 above
it. In 4:3 terms that is the **lower-right**, roughly 69–93 % across and 55–84 % down, over the world and
(GUESS-medium, from ordering-table slots) under the panel. Nothing translates him: `t` of the body matrix
is zero, the face's translation is the bone's, scaled.

**For the port**: a private camera at (0,0,−256) looking at the origin, Y up, projection distance 256,
principal point (416,176) in 512-px space (= 260 of 320 logical), horizontal stretch 1.6; model matrix =
uniform scale `(+0xE8>>2)/4096` × rotation about the view axis `(+0xEC>>1)·360°/4096`; body = entry 0
sub `gesture+1` at frame `+0xF8`; face = sub `mood+1` (none when mood = 16) attached to the body clip's
bone `list[0]` under the same scale/rotation. Advance `+0xE8`, `+0xEC`, `+0xF8` in **frames at 50 Hz**.

## 2. Gesture `+0x1B1` and mood `+0x184` (READ)

* **Gesture** (0..4, rerolled-until-different by `0x8001404C`) selects the **body clip** `T[g] = g+1`.
  `+0xF8` is the clip frame: reset to 0 when he sets off (state 1→2), **advanced only in state 3** by
  `0x80013B74(adv, elapsed)`, so he holds frame 0 of the chosen clip through the whole entrance spin and
  through the exit; while speaking the clip plays at 50 fps, and each time `+0xF8` passes the table value
  it is reduced by it and **a new gesture is rolled** — a long recording cycles random clips, each played
  once to its end. Clip lengths 1.0 / 1.5 / 2.5 / 4.5 / 6.0 s.
* **Mood** (7, 8, 9, 11, 13, 14, 16; 10 and 12 only on the cut takes) selects the **face mesh**
  `T[m] = m+1`: subs 8, 9, 10, 12, 14, 15, and 11 / 13 for the cut takes — both exist in the archive
  (33 and 45 vertices). **16 = no face** (`lbu +0x184; addiu v0,zero,16; beq`). The face is drawn with
  the same `+0xF8`, which the animator reduces modulo its length of 1, i.e. frame 0 always.
* Which face is which expression is **not established** (no names anywhere; needs a render).

## 3. The caption: a card in the message list, not a speech bubble (READ, asm-checked where marked)

`0x80013EFC` (show one), when flag bit 0 is set and the text id is not `0x124`:
```
0x8003A7BC(rec)          rec is 0x120 bytes on the stack: +4 = 0, +0x108 = 0xFFFF, +0x10C = 0xE10,
                         +0x110 = 0, +0x114 = −0x40, +0x118 = 0            (asm-checked setters)
0x800141BC(rec, textId, queue.kind, queue.value)      → §4
0x800385AC(hud, rec)  =  0x8003A484(&0x801069E8, rec)   a0 is replaced by &0x801069E8, a1 passes through
```
`0x801069E8` is the HUD's **message list** (registered in the panel ctor `0x800383EC` via `0x80039CDC`):
`+4` open flag, `+0xC` count, `+0x10` selected, `+0x18` scroll, `+0x1C` pending-removal index,
`+0x20` a header record, **`+0x144` = 32 records × 0x120**, `+0x2544` a text widget. `0x8003A484` copies
the record to slot `count` and increments; at 32 it first removes slot 0 (`0x8003A654(0)` +
`0x8003A52C`), so **the oldest is dropped**. Its ctor `0x80039C58` inits all 33 records the same way.

**Record layout** (0x120 bytes): `+0 u32 kind`, `+4 u32 state` (0 idle, 1 being removed, 2 selected),
`+8..` text bytes (unused by the advisor path; `0x8003BB18` returns `rec+8` only when `+0x108 == −1`),
`+0x108 s16 textId`, `+0x10C = 0xE10` (no reader found), `+0x110` cascade counter, `+0x114` x-slide,
`+0x118` y-offset, `+0x11C` object pointer.

### 3.1 While the list is closed: only a badge (READ)
The list's per-frame update `0x80039F7C` runs `0x8003A8B0` on each record when closed: its target
x-slide is `−0x40` unless the cascade counter (still 0 for a fresh record) allows 0, and the card drawer
`0x8003B484` skips any record whose `x + slide ≤ 0`. So **a new caption is not shown as a card**; what the
player sees is the badge `0x8003B06C(header, 50, 190, count)`: the count `"%d"` (format at `0x80102A98`)
in the HUD font, centred, at (58, 216 baseline), colour 0x80,0x80,0x80, flanked by sprite **0x12E**
(19×33) at (58,192) and X-mirrored at (39,192) and sprite **0x12A** (16×25) at (58,192) / mirrored at
(42,192) carrying the style-0 gradient (top `0x80102A9C` = FE,91,14 orange; bottom `0x80102AA4` = E7,C3,1A
yellow). GUESS-medium on the composite look; the sprites are HUD sheet **416** (`0x800BE268(&0x80103A80,
0x1A0)`).

### 3.2 Opening it (READ)
Logical button 5 (= **L2**, `0x800`, in both tables of §5) in the panel's input handler `0x80038D80`
calls `0x80039D1C`: open flag on, selection 0, every record's slide reset to −0x40, **event 14** sent to
the advisor's `+0x1C0` object (`0x8001397C(adv, 0xE)` — not a message post: it is gated by flag bit 6 and
dispatches through the vtable `0x800DBD28` at `0x80017304`, which has **no case for 14**, so nothing
happens on this disc), sound `0x800B8E08(6,1)`. Open, `0x8003A834` cascades the cards in
(record i starts when `counter/2 ≥ i − list+0x14`), target slide 0, or **+0x18** for the selected one.
Up/Down move the selection; logical **10** deletes the selected card (state 1 → slides out → removed);
logical **11** acts on it (`0x8003B878`): kind 2 jumps the camera to the object and closes the list;
kind 3 **replays** the tutorial message (`0x8001408C` finds the record ≥ 221 whose text id matches and
posts it with flag bits 0 and 5 cleared). The panel's four labels become `[Close 0x396, Delete 0x222,
OK 0x1CC (kind 2) / Replay 0xF7 (kind 3) / blank, blank]` (`0x80039DFC`). Logical 5 or 1 closes it.

### 3.3 The card (READ; sprite sizes MEASURED from sheet 416)
`0x8003A22C` draws record i at `x = 50 + slide`, `y = 158 − 32·i + scroll` (scroll eases to
`32·selected`), with a skew `x −= 2·(y−158)` above y = 166 and `x −= 2·(32−y)` below y = 32.
`0x8003B484(rec, x, y)`:
* three icon sprites from `0x800E03D0` (`s16[3]` per kind) at `(x−24, y+4)`: the first tinted with the
  kind colour from `0x800E03F0` (`u8[4]` per kind), the second with tpage semi-trans mode 1 (**additive**,
  `|0x20`), the third mode 2 (**subtractive**, `|0x40`); the selected card's tint ×1.33 (`<<15 / 6 >>12`).
* `0x8003A8F8(rec, x, y, 0)`: sprite **0x12E** at (x, y) tinted `0x80102A8C` = 40,40,40 subtractive;
  sprite **0x131** (8×35) stretched as a quad over **(x−80, y+1)..(x, y+35)** with its right-edge u pulled
  in by 2, same tint, subtractive — the dark plate; a gouraud sprite 0x12A at (x, y) and a flat gouraud
  quad over **(x−80, y+4)..(x, y+31)** with the style-0 orange→yellow gradient.
So a card is an 80×34 tab whose left 30 px lie off-screen at rest (x = 50): a rounded-cap plate with the
gradient bar and the kind's icon 26 px in from the screen edge.

| kind | how it arises (§4) | icons (sheet 416) | tint |
|---:|---|---|---|
| 0 | every rule-posted message | 300, 308, 309 | 80,00,00 red |
| 1 | not produced by the advisor path | 299, 306, 307 | 00,80,00 |
| 2 | a value was attached (object link) | 301, 310, 311 | 00,00,80 blue |
| 3 | ids 221..288 (tutorial, replayable) | 301, 310, 311 | 00,80,80 teal |
| 4 | not produced | 300, 308, 309 | 80,00,00 |

### 3.4 The words: the selected card's text box (READ)
`0x8003A35C(list, rec)` configures the widget at `list+0x2544` (class ctor `0x8004CFC0`, vtable
`0x800E0B64`): frame style 1, colour pointers `0x80102AAC` (fill, DE,AB,52) and `0x80102AB0` (80,64,40 —
GUESS-medium the text colour), rect **x = (512−260)/2 = 126, y = 100, w = 260, h = 80**, padding
(16, 14), style flags `0x72`, text = `0x8006F00C(textId)` (the string table) — then `0x8004CE88(w,0,0,10)`:
`0x8004CC18` measures the text (flag 0x10) into `+0x4C`, `0x8004D144` **grows the box to the text**
(flag 0x40), centres it (0x2 horizontally, 0x20 vertically) and draws at `y + 13`; `0x8003C590` draws the
frame: `0x8003C7C0` = the HUD's 9-slice (corners **0x169** 21×16, top/bottom **0x173** 8×3 stretched,
sides **0x161** 6×8 stretched, inner corners **0x149** 12×10, tint `0x80102AD0` = A0,A0,A0, subtractive)
over a flat fill in the widget's colour. `0x80102A88 = boxHeight + 0x80` positions the scroll arrows
(`0x8003A0C4`, gouraud triangles at x 254..279).

**The game wraps at draw time** (READ `0x80029D28`, asm-checked): it walks glyphs, remembers the last
break character (charset virtual `+0xC`) and its width, breaks there when the run exceeds the clip width
set by `0x80029D10` (= the widget's full **260**: the text is laid out before the padding widens the
frame to 292 × (h+28) at (110, 86)), mid-word if no break was seen, honours `'\n'`, aligns
per `ctx+0x20` (0 left, 1 centre, 2 right), advances `y` by the charset's line height and returns the
total height. None of the 270 `STR_ADVMES_*` strings contains `'\n'`; the longest is 207 characters. So
the strings arrive whole and the port's greedy wrap is the right shape, at 260 px.

**The font** (READ `0x8002AB78`): charset `0x800F0908` = sheet **416**, glyph map `0x800DD0E4`
(`s32[c − 0x20]`, −1 = space), **line height 14, space 7, +1 px per glyph**, glyph advance = sprite width
+ 1; capitals 8×10 (A = sprite 466), digits and descenders 13 tall. The alternate charset at
`0x800F0924` (sheet 84, 14/8/−4) is selected only in mode 7 (§5).

### 3.5 Retraction (READ)
The card is also removed by the system that posted it: `0x800139EC(adv, msgId)`, with flag bit 0 set,
looks the message's text id up and calls `0x8003A65C(list, textId)`, which removes the first record
carrying that text id at once (e.g. `0x80079A1C` posts 0x8E/0x8F and retracts them; `0x80017024`
retracts a list of ids). The port's caption timer has no counterpart in the game.

### 3.6 Timing (READ)
The HUD tick `0x80038AA4` (from the game tick `0x80058C20`, next to the advisor tick) runs the list once
per game tick; the cascade and slides are eased (`>>2`, `>>1`) per tick. `+0x10C = 0xE10` (3600) is written
and never read in the message code — a caption does not expire on its own; it leaves the list when the
player deletes it or when 32 newer ones push it out.

## 4. The two payload fields — `0x800141BC(rec, textId, kind, value)` (READ, asm-checked)
```
0x80014280: sh a1, 0x108(a0)     rec.textId = textId
0x80014278: sw a1, 0x000(a0)     rec.kind   = queue entry byte +2
0x80014270: sw a1, 0x11C(a0)     rec.object = queue entry word +4
```
No substitution happens anywhere: the string is fetched by id when the card is opened. The queue entry's
byte `+2` is written by the two routes (`0x80013D30` queue, `0x80013C80` immediate): **2** if the builder
carried a value (`0x80014134` sets word `+4` and byte `+8 = 1`), else **3** for ids 221..288, else **0**.
So for the 107 rule messages the fields are 0 and 0 — a red card that does nothing when selected. Posters
that attach a value: `0x80063184` (message 0x99 with the posting object) and `0x8009C730 / 0x8009C7A8`.

## 5. The pad tail of `0x80013180` (READ, asm-checked)

### 5.1 What the bits are
`0x800891A4` builds the raw word as `~((buf[2] << 8) | buf[3])` from the libpad report at `0x8010A260`
(bytes 4..7 are the sticks, which the analog branch of `0x80089428` reads as RX, RY, LX, LY), and
`0x80089010` remaps it to the game's layout:

| game bit | 1 | 2 | 4 | 8 | 0x10 | 0x20 | 0x40 | 0x80 | 0x100 | 0x200 | 0x400 | 0x800 | 0x1000 | 0x2000 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| button | Up | Down | Left | Right | Start | Select | **Cross** | Square | Circle | **Triangle** | L1 | L2 | R1 | R2 |

(The left stick emulates 1/2/4/8 and the right stick's X emulates R1/L1 in `0x8008938C`, consistent with
the table.) `0x80089124` keeps four words per port: `+0xA20C` held, `+0xA20E` previous, **`+0xA20A` =
held & ~previous (press edge)**, `+0xA208` released. `0x800897EC(port)` returns `+0xA20A`.

So the tail tests **Triangle** (0x200) normally and **Cross** (0x40) when the mode is 7.

### 5.2 The mode is the language
`0x800BE724` returns `[0x80103AA4]`, written only by `0x800BE6DC`, called only from `0x800BC938` with
`func_0x8011C9F0(cfg)` from overlay 2 = `u32 table[0x801147AC][cfg+0x68]`, a **seven-entry** table
`2, 4, 5, 0, 6, 1, 3` (followed by the string "LANGUAGE SELECT"). `0x800BCD00` names 0 ENGLISH, 1 FRENCH,
4 SPANISH and 7 "END". Mode 7 also shifts the display by −32 (`0x800BB43C`), selects the sheet-84 charset,
swaps confirm/cancel in the button tables and permutes the panel slots `[1,2,0,3]` (`0x800E0338`) — a
Japanese-convention configuration that **this disc cannot select** (GUESS-high: the only writer is the
seven-entry lookup).

The logical→physical tables the teammate asked for, decoded with §5.1 (26 `u16` each):
```
0x800E35C0 (modes 0..6): 0 Cross, 1 Triangle, 2 Circle, 3 Triangle, 4 Square, 5 L2, 6 Cross, 7 Triangle,
   8 Circle, 9 Square, 10 Circle, 11 Cross, 12 Square, 13 Circle, 14 Square, 15 Cross, 16 Triangle,
   17 Circle, 18 Square, 19 Cross, 20 Triangle, 21 Square, 22 Circle, 23 Cross, 24 Circle, 25 none
0x800E35F4 (mode 7):     0 Circle, 1 Cross, 2 Square, 3 Cross, 4 Triangle, 5 L2, 6 Circle, 7 Cross,
   8 Triangle, 9 Square, 10 Triangle, 11 Circle, 12 Square, 13 Triangle, 14 Triangle, 15 Square, 16 Cross,
   17 Circle, 18 Triangle, 19 Circle, 20 Cross, 21 Square, 22 Triangle, 23 Circle, 24 Circle, 25 none
```
The advisor does not go through `0x80088F80`; it tests the physical bit directly, and the bit it tests is
what logical 1/3/7 ("cancel") map to in each mode.

### 5.3 What it does — the modal message
`0x80013AD8`, called as he sets off, makes a message **modal** (`+0xB5 = 1`) when flag bit 1 (voice) is set
and the id is **141 or 221..288**; every other message leaves `+0xB5 = 0` and the tail never runs. Modal:
* `0x80089810(1)` sets `[0x801031E8]`, and every filtered pad reader (`0x800896F4`, `0x8008974C`,
  `0x80089828`, `0x800898A8`) returns 0 while it is set — **the game stops seeing the controller**; the
  advisor reads the raw edge word, so Triangle still reaches him.
* `0x80038BF8(hud)` saves the four panel labels and shows `["Cancel" 0x1A, blank, blank, blank]`
  (`0x80039678`), slot 0 being the **Triangle** bubble (port: `HudPrompts`, `0x800E02E8`) — the same
  button by an independent route.
* `+0xB8 = 0`. Then each tick: a Triangle press-edge sets `+0xB8`; on the next tick without an edge:
  `+0xB4 = 0` (drop the waiting interrupt), **event 1** to the `+0x1C0` object if the id was 221 or 288
  (`0x8001397C(adv, 1)` → its vtable slot `+0x2C`; the same hook state 3 fires when those two recordings
  end — advisor.md §3's "completion hooks"),
  `0x8001392C` (**he leaves at once** — state 4, timer 50, the XA stopped if speaking), input unlocked,
  labels restored, `+0xB5 = 0`, and for 141 `0x800BCEA0` (waits for the CD/XA to go idle, then posts
  events 0x10004 and 0x80008 — GUESS-medium: end-of-tutorial hooks).
State 5 repeats the unlock/restore each tick, which is why an un-dismissed modal message also ends cleanly
when the recording runs out.

## 6. Tables and numbers the port needs, in one place
* `T` = `0x800EFB9C`, 17 bytes: `1..16, 0`. Durations `0x800EFB90`: `50, 75, 125, 225, 300` (u16).
* Arrive: `+0xE8 += 24/frame`, `+0xEC += 655/frame`, 50 frames, then `+0xE8 = 1200`, `+0xEC = 0x8000`.
  Speak: `+0xEC` ±1/frame toward `0x7F38 + rand(400)`, rerolled when within ±5. Leave: both reversed,
  ends at 50 frames or `+0xE8 < 0`. Away: 100 frames (flag bit 4), or until an interrupt.
* Camera (0,0,−256) → origin, Y up, H = 256, principal point (416,176) of 512×256, x-stretch 1.6.
* Colours: card tint 40,40,40; style-0 gradient FE,91,14 → E7,C3,1A; style-1 19,AC,E7 → 2F,C4,C9;
  box fill DE,AB,52; box text 80,64,40; frame tint A0,A0,A0; badge count 80,80,80.
* HUD font: sheet 416, map `0x800DD0E4`, line 14, space 7, +1; wrap width 260, text box at (126,100),
  frame 16/14 px wider on each side.

## 7. Not established
* Any live frame of him: the Y-up reading, the spin direction, how the OT orders him against the panel,
  and whether the console ever shows a caption's words without the list being opened are all READ-only.
* Names for the eight faces (subs 8..16) and what messages carry mood 16.
* Who uses entry 0's sub-entries 0, 6 and 7.
* The reader of the record's `+0x10C = 0xE10`; whether `[0x80102D54]` (H) is ever rewritten through a
  pointer (no gp or absolute store exists).
* The text colour of the box (`0x80102AB0` is the widget's second colour pointer; the draw of the text
  itself through `0x80029D28` was not followed to its colour).
* What the `+0x1C0` object is (class vtable `0x800DBD28`, ctor `0x800176A4`, alive only when flag bit 6
  is set, i.e. `0x8006C078() != 0`; `0x8001397C` feeds it 26 distinct event numbers from all over the
  game). GUESS-medium: the tutorial / scenario-goal listener that raises the 221..288 messages. Its
  handlers were not read; nothing in the presentation depends on them.
