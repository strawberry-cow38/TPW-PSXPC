# How a WALKING person's sprite is picked — PAL SLES-026.88

Companion to `rider-positions.md` §8 (riders: heads from sheet 416) and `staff.md` §3 (the four uniformed
staff): same method as §8 — trace from the draw to the table, decode the table, render the sheet to see
what the indices are — applied to the guests, whose resources turn out to be a different shape from the
staff's. `V` is the visitor object, `P = V+8` its Person subobject (as in `staff.md`), `d` the drawable the
drawing manager lends a person while it is in view. TPW.BIN loads at `0x80010000`; FOLIO.GAZ entries by
index.

READ = instructions or data at the quoted address; the Ghidra decompile corpus and the raw MIPS agree
wherever both were consulted, and where only one was, it says so. MEASURED = observed in disc data or in
a render of sheet 269 made for this note (`sheetrender.py`, palettes from inside the sheet). DERIVED =
arithmetic on READ facts. GUESS carries a confidence.

## 0. The answer

```
idx      = (P+0x2B & 0x80) ? 8 : 0x800E37FC[(type + [V+0x10]) & 7]   type = V+0x61 = rand(8) at spawn; 0x800E37FC is the identity 0..7
entry    = 0x800DFF74[theme][idx]                                     idx 0..7 → 272 272 273 273 270 270 271 271; idx 8 → the theme's costume
base     = 0x800DFFFC pair for entry                                  272→0  273→66  270→132  271→198   (costumes 267→440 265→506 259→643 275→577)
anim     = s16 0x800F1ECC[personAnim]                                 personAnim = P+0x2C bits 19..23: 13 (walk)→7, 11→5, 12→6, 2→3, 14→2, 5→2, 6→3; −1 = draw nothing
table    = C[anim] of FOLIO entry `entry`                             the resource's animation-table pointer array; NULL = draw nothing
rel      = (facing + cam) & 7                                         facing = P+0x2C bits 16..18 (cardinal, §3.1); cam = camera yaw in 45° steps (§3.2)
stored   = rel < 5 ? rel : 8 − rel                                    FIVE drawn views: 0 back, 1 back-quarter, 2 profile, 3 front-quarter, 4 front
frameId  = table.ids[stored * table.fpf + frame]                      fpf = frames per facing (u8 at table+0); frame = the drawable's counter (§4.3)
sprite   = base + frameId                                             absolute index into sheet 269
mirror   = rel < 5                                                    relative facings 0..4 are drawn X-flipped; 5..7 as stored (the art faces screen-right)
```

For a walking guest `anim` is 7, whose table is `08 05` followed by ids 26..65, so

```
sprite = base + 26 + stored*8 + frame        base ∈ {0, 66, 132, 198}, stored 0..4, frame 0..7
```

**Five stored facings × eight frames, facing-major, starting 26 sprites into the block** — the transpose of
the port's `8 facings × 5 frames`. The port's `FindWalk` already lands on sprite 26 (the walk is the one
forty-sprite palette run in the block), so only the stride, the fold and the mirror are wrong. The 26
sprites before it are seven facing-independent poses (§4.2). The frame is a per-drawable counter that
advances once every four draw calls, not distance walked (§4.3).

## 1. Why both readings of forty failed (MEASURED)

* Sprite 15 is animation 3, frame 3 — a front-facing *pose*, not a walk frame; sprite 30 is the walk,
  stored facing 0 (back), frame 4. "Three apart" compared a pose with a walk frame.
* Sprites 0..7 are animations 0 and 1: two four-frame poses in which the figure turns on the spot — they
  are not one direction's cycle because they are not a walk.

Rendering block 0 (entry 272) in the layout of §4.1 shows seven short rows of poses and then five rows of
eight in which every row is one consistent view: back, back-quarter, profile, front-quarter, front. The
three intermediate rows all face screen-right (the back-quarter walks away to the right, the profile's
face and stride point right, the front-quarter comes toward the viewer's right). The mechanic's block
(264..307, `staff.md`'s 5 × 8 + 4) renders the same way at 2–3× the size.

## 2. The chain, draw to sheet (READ)

### 2.1 The person draw
Visitor vtable `0x800E367C` (46 entries, 8 bytes each; slot 45 at `+0x16C` is `0x80091D80`, slot 4 is
`0x80091D54`). Slot 4 → `0x800937D0(P)`: if `0x80093EF0()` says the person is active, call slot 45 to
acquire a drawable, then `0x800935C0(P, 0)`; otherwise release the drawable (`0x80058F5C`, `P+0x1C = 0`).

`0x800935C0(P, yOverride)` needs `P+0x1C` (the drawable). Position is `(P+0x18, height, P+0x1A)` with the
height from `0x80050938(x, y, 0)` unless `yOverride` is given, in which case it is used as the height and
the animation is **forced to 13** (`P+0x2C = P+0x2C & 0xFF07FFFF | 0x680000`) — the bounce-ride case of
`rider-positions.md` §2.5. Vtable `+0x54` then yields the person's position (its 1st and 3rd shorts are
tested against the rectangle from `0x80053F40()`; GUESS-high: the visible tile window). Inside it:
`0x800323A0(d, P+0x2E & 7)` sets the facing, `0x800323A8(d, (P+0x2C >> 19) & 0x1F)` the animation, and
the drawable's vtable entry 2 (`+0x14`) is called with `&position` (GUESS-high: stores it at `d+0x20`, which
is where `0x800324D0` reads the position from). Outside it the drawable is released. A sheet-416 icon may
also be drawn over the head (`0x80093E00` → `0x800BDCD4` → `0x80034DDC`, gated by `0x80093E0C(P)` and
`P+0x2B & 0x40`) — not traced.

### 2.2 Acquiring the drawable — which body (slot 45, `0x80091D80`)
If `V+0x24` (= `P+0x1C`) is null: `idx = 8` when `0x8009403C(P, 0x80)` (flag byte `P+0x2B` bit 7) else
`idx = u32 0x800E37FC[(V+0x61 + [V+0x10]) & 7]`; `0x800E37FC` is `0, 1, 2, 3, 4, 5, 6, 7` (MEASURED),
then `V+0x24 = 0x80058F34(idx)`.

* `V+0x61` is the type, `rand(8)` written at spawn (`0x80091A3C`, last thing before it sets animation 13;
  also `0x80067274`, the arrivals spawner) — the same byte `rider-positions.md` §8.1 reads as the rider's
  head type. Buying a costume (`0x8008E5EC` case 2) writes type **8** and sets flag `0x80`
  (`0x80094050(P, 0x80, 1)`), so a costumed guest takes `idx 8` regardless of type.
* `[V+0x10]` (= `P+8`): **no writer found** — not in the constructor chain (`0x8008C498` → `0x80093FF4` →
  `0x80094134`; `0x80094030` zeroes `P+0x10`, not `P+8`), not in the reset `0x8008C534` / `0x800927F4`, not
  in the spawn init `0x80091A3C`, not in the pool/list code (`0x8005DE68`, `0x80053C04`, `0x8005CC4C`).
  Two methods: a corpus grep for every `+ 8) =` / `+ 0x10) =` spelling, and a raw-MIPS scan of every
  `sw x,8(r)` / `sw x,16(r)` in `0x8008C000..0x80095000` (one hit, `0x8008C3DC`, a circle object's radius²).
  It is only ever read, as a per-person phase compared with the clock (`0x800906EC`, `0x8008E240`,
  `0x8008FE60`; staff at `0x80094774`). The pool comes from the game heap (`0x800BAB94` → `0x800C0BA8`), no
  zero-fill read. GUESS-high: it is 0, so **`idx = type`** — which is also what pairs the four bodies with
  the eight head rows of §8.3 by type.

### 2.3 The drawing manager — resource, base sprite, remap (`0x80058F34` → `0x80031680`)
`0x80058F34(idx)` = `0x80031680(&0x8010978C, idx)`. For `idx != 13` (`0x80031668`): take a slot from the
pool at `0x80105668`; `entry = 0x80033590(themeRow, idx) = themeRow[idx]` with `themeRow =
0x800DFF74[manager+0x10]` (the theme, set by `0x800319F8(manager, theme)`, which also loads every row
entry through `0x80032008` and the sheets `0x10D` → slot `0x801037EC` and `0x105` → slot `0x801037E4`);
`base = 0x800315A8(entry)`, a linear search of the twelve `(entry, base)` u16 pairs at `0x800DFFFC`,
stored at `d+0x28` by `0x800335A4` (0 if not found). For `idx == 13` the other pool (`0x801067A8`, the
entertainer's image-bearing route of `staff.md` §3). Then `0x8003236C(d+8, idx, manager+0x18+idx*0x10,
remap)` with `remap = 0x80031674(idx) ? 0x800F1EAC : 0x800F1ECC`, where `0x80031674(idx) = (idx − 9 < 6)`
— **guests and the costume (idx 0..8) use `0x800F1ECC`; staff and entertainers (9..14) use `0x800F1EAC`**
(the table `staff.md` decoded). Init: `+0xA` animation 0, `+0xB` last animation 0, `+0xC` frame 0,
`+0xD` countdown 4, `+0xE` period 4, `+0xF` facing 0, `+0x10` idx, `+0x8` (u16) last frame 0xFFFF.

Decoded (MEASURED):
```
0x800DFF74 → rows 0x800E002C / 64 / 9C / D4 (theme 0..3), 14 u32 each:
  272 272 273 273 270 270 271 271 | 267 or 265 or 259 or 275 | 263 264 266 274 | 403 or 401 or 402 or 404
0x800DFFFC (entry, base) ×12: (272,0) (273,66) (270,132) (271,198) (267,440) (265,506) (275,577) (259,643)
                              (263,264) (264,308) (266,352) (274,396)      — PeopleSheet.Blocks, same order
0x800F1ECC (idx 0..8), s16 ×16, indexed by person animation:
  0→2  1→0  2→3  3→−1  4→1  5→2  6→3  7→4  8→−1  9→−1  10→−1  11→5  12→6  13→7  14→2  15→−1
0x800F1EAC (idx 9..14): everything −1 except 13→0, 15→1                  (as staff.md §3)
```

### 2.4 The draw pass — where the camera facing comes from (`0x8003186C`)
The frame renderer `0x8005814C` (advisor-presentation.md) calls `0x8003186C(&0x8010978C, M)` with `M =
0x80061B70(cam+8) = cam+0x68`, the camera's GTE MATRIX (it is what `SetRotMatrix`/`SetTransMatrix`,
`0x800C4C24`/`0x800C4C04`, are given in both functions). Then

```
a   = ratan2(M.m[0][2], M.m[0][0])          0x800C9340 = LIBGTE ratan2 (psyq-named-functions.json); m[0][*] is the matrix's first row
cam = ((0x100 − a) mod 0x1000) >> 9         negate, +0x100 rounds to the nearest 45° step, & 7 happens in the consumer
```
and every drawable in both pools gets its vtable entry 1 (`+0xC`) called with `cam`: `0x800324D0` for
sheet people, `0x80032118` for the entertainer (vtable pointers at `0x800DFFA0` / `0x800DFFB8`). When
`manager+0x14` is non-zero the pass instead calls `0x800326BC` for the sheet-pool drawables whose idx is
outside 9..14 and draws nothing else — §5.

### 2.5 Per-drawable frame and the table lookup (`0x800324D0` → `0x800323B0` → `0x8002BB30`)
`0x800324D0(d, cam)`: `frameId = 0x800323B0(d+8, cam, d+0xC)`; if `≥ 0`, `record = 0x80028DD4(0x801037EC)
+ (frameId + u16 d+0x28) * 12` — **sheet 269, absolute index `base + frameId`** — copy the record's tpage
(+0), clut (+2), ox (+4, s8), oy (+5, s8), w (+6), h (+7), u (+8), v (+9) (the flag byte +10 is not read
here), position from `d+0x20`, and `0x8003377C(&quad, prim, out, mirror = ((u8 d+0x17 + cam) & 7) < 5)`;
on success link the primitive at ordering slot `SZ >> 2` and remember `frameId` at `d+0x10`.

`0x800323B0(s, cam, res)` (`s = d+8`; `res` defaults to `s[1]`, the resource holder):
```
anim = u8 s+0xA;  if anim != u8 s+0xB: s+0xB = anim, frame(s+0xC) = 0, countdown(s+0xD) = period(s+0xE)   — reset on change
if s[0] (remap) != 0: anim = s16 remap[anim]
if anim < 0 → return −1
count = 0x8002BAEC(res, anim)      = u8 at C[anim]+0, or −1 when C[anim] is NULL → return −1
countdown −= 1; if 0: countdown = period, frame += 1
frame %= count                      (div/mfhi at 0x80032458..0x80032480)
frameId = 0x8002BB30(res, (u8 s+0xF + cam) & 7, anim, frame)
```
The fourth argument is real: the raw MIPS at `0x80032440..0x8003249C` loads the frame byte into `a3`,
reduces it, stores it back in the delay slot and jumps — the corpus decompile of this caller drops it.

`0x8002BB30(res, rel, anim, frame)`: `if rel > 4: rel = 8 − rel`; `table = C[anim]` where `C =
0x8002BD18(holder) = [holder+0x18]`; NULL → −1; else `0x8002BDA8(table, rel, frame) = u16 at table + 2 +
2*(rel * u8 table[0] + frame)`.

### 2.6 The quad and the mirror (`0x8003377C`)
Vertices 0 and 2 are the left edge, 1 and 3 the right. `mirror == 0`: `u0 = u2 = U`, `u1 = u3 = U+w−1`
(as stored). `mirror != 0`: `u0 = u2 = U+w−1`, `u1 = u3 = U` — **relative facings 0..4 are X-flipped,
5..7 are drawn as stored** (the same reading as `staff.md` §3, confirmed here on the guest route).
Placement: with `(sx, sy, SZ)` the projected point (`0x80010978`), `s = (0x8001096C() << 8) / SZ` and
`sx' = s * 0x800BB424() / 0x140` (the 320→512 stretch, cf. `rider-positions.md` §8.5): `x0 = sx +
(ox*8*sx' >> 8)`, `x1 = x0 + (w*8*sx' >> 8)`, `y0 = sy + (oy*8*s >> 8)`, `y1 = y0 + (h*8*s >> 8)`; the quad
is dropped when `x0 < 0`, `y1 < 0`, `x0 > 0x800BB424()` or `y0 > 0x800BB430()`. `0x8001096C`'s identity is
not established (GUESS-medium: the projection distance). The record's `ox/oy` are the offsets of the
sprite's top-left from the projected foot point (block 0: `(−5,−12)` … `(−7,−16)`), so a whole block is
placed by its own records and nothing per-person.

## 3. Facing

### 3.1 The person's facing is always a cardinal (READ, two methods)
Every writer of `P+0x2C` bits 16..18 (corpus grep for the `0xFFF8FFFF` mask and a raw-MIPS census of
`lui r,0xFFF8` agree: `0x8008DB34`, `0x800906EC`, `0x80090A50`, `0x800932C8`, `0x80097BA0`; the sixth site
`0x800D98B8` is library code):

| writer | facing written |
|---|---|
| `0x800932C8` walk step (vtable 36), from the offset to the next waypoint `(dx, dy) = target − (P+0x18, P+0x1A)` | `dx == 0`: `dy > 0` → **0**, else **4**; `dx < 0` → **2**; `dx > 0` → **6** |
| `0x800906EC` state 18 waiting in queue | `2 * rand(4)` |
| `0x8008DB34` state 2 walk-to-dest / arrival | 4 or 0 |
| `0x80090A50` state 28 watching the entertainer | toward the entertainer, same code as the walk step |
| `0x80097BA0` (staff) | 4 or 0 |

So `facing 0 = +y (P+0x1A increasing), 2 = −x, 4 = −y (and "not moving"), 6 = +x` in the 8.8 tile
coordinates of `behaviour.md` §2's layout table. The quarter views (stored 1 and 3) are reached **only
through the camera's yaw**, never from the person.

### 3.2 The camera's facing
`cam` of §2.4: the first row of the world→view rotation is the world direction that maps to screen-right,
so for the port, with `(r_x, r_y)` the tile-plane components of the world direction that maps to
screen-right:

```
cam = round(−atan2(r_y, r_x) / 45°) mod 8          rel = (facing + cam) & 7
```
(the original's `+0x100` before the `>> 9` is the rounding). `cam = 0` when screen-right is world `+x`.

### 3.3 The five views and the sign cross-check (MEASURED + DERIVED)
Stored 0 = back (walking away), 1 = back-quarter going away to the right, 2 = profile facing right,
3 = front-quarter coming toward the right, 4 = front — all the intermediate art faces screen-right
(rendered at 8×; the mechanic's rows 1–2 likewise). Checks:
* screen-right = `+x` (`cam 0`): walking `+y` (facing 0) → `rel 0` → back ✓ (the one direction the port
  already gets right); walking `+x` (6) → `rel 6` → stored 2 unflipped → faces right = the direction it
  moves ✓; walking `−x` (2) → `rel 2` → stored 2 flipped → faces left ✓; walking `−y` (4) → front ✓.
* camera turned to look along `+x` (screen-right = `−y`): `cam = round(−atan2(−1, 0)/45°) = 2`; walking
  `+x` → `rel 0` → back ✓.
With the camera at an exact multiple of 90° only stored 0, 2 and 4 are used; the eight-way art is for the
analog camera.

## 4. Animations

### 4.1 The person resource format (READ `0x8002B4DC`) and the decoded tables (MEASURED)
Each people entry (259..275, 401..404, 420/421/262) is one file: header `u32 nA, nB, nC`, then `nA` words
(array A), `nB` words (array B: one pointer per frame), `nC` words (array C: one pointer per animation),
each relocated by the load address except that a zero C entry stays NULL. The loader keeps `A` at
`holder+0x10`, `B` at `+0xC`, `C` at `+0x18` (what `0x8002BD18` returns), the data start at `+0x14`, the
counts at `+0x20/+0x1C/+0x24`, and `+0x2C = (B[0] == B[1])` — for the sheet people every B word is the
data start, i.e. "no per-frame image, look in the sheet". An animation table is `u8 fpf, u8 x, u16
ids[5 * fpf]`, facing-major; `x` is 5 on walks and 0 on poses and is not read by the getter.

| entry | nA/nB/nC | C (animation → frame ids, per stored facing) |
|---|---|---|
| **272, 273, 270, 271** (guests), 267, 275 (costumes) — byte-identical | 8 / 66 / 8 | 0: 0..3 · 1: 4..7 · 2: 8..11 · 3: 12..15 · 4: 16..19 · 5: 20,21 · 6: 22..25 — each the same four (two) ids for all five facings; **7: fpf 8, ids `26 + f*8 + k`** |
| 265 (theme-1 costume) | 8 / 71 / 8 | as above but **7: fpf 9, ids `26 + f*9 + k`** (26..70; its block 506..576 is 71 sprites) |
| 259 (theme-2 costume) | 6 / 60 / 8 | **0: NULL** · 1: 0..3 · 2: 4..7 · **3: NULL** · 4: 8..11 · 5: 12..15 · 6: 16..19 · 7: fpf 8, ids `20 + f*8 + k` (block 643..702 is 60 sprites) |
| 263, 264, 266, 274 (staff) | 2 / 44 / 2 | 0: fpf 8, ids `f*8 + k` · 1: fpf 4, ids 40..43 (as `staff.md` §3) |
| 401..404 (entertainers) | 2 / 44 / 2 | the same two tables at the tail of each image-bearing file (`staff.md`) |
| 420, 421 (ride-cam guests, §5) | 7 / 62 / 8 | 0: NULL · 1..4: fpf 4 · 5: fpf 2 · 6: fpf 4 · 7: fpf 8 (ids not listed) |
| 262 (ride-cam guests, §5) | 8 / 66 / 8 | as the guests |

Palettes (MEASURED from the sheet records): each animation of a guest block has its own clut — block 0
runs `819 ×4, 818 ×4, 817 ×4, 816 ×4, 755 ×4, 754 ×2, 753 ×4, 752 ×40` — which is exactly why the port's
"first run of forty sprites sharing a palette" finds 26. Sprite sizes: guests 8..17 × 10..20 texels;
uniformed staff 13..39 × 20..33; drawn by the same rule (§2.6), so staff really are 2–3× larger on screen.

### 4.2 Which animations a guest uses, and what the 26 non-walk sprites are
Writers of `P+0x2C` bits 19..23 in the visitor code (corpus grep for the `0xFF07FFFF` mask and a raw-MIPS
census of `lui r,0xFF07`; the two lists agree: `0x8008C57C, 0x8008D340, 0x8008D608, 0x8008DDD8..DEB8 (one
write, hoisted), 0x8008F1BC, 0x8008F99C, 0x80090A0C, 0x80090A6C, 0x80090BAC, 0x80090C78, 0x80091BEC`; the
rest are Person/staff: `0x80093230, 0x800935F8, 0x800941D8, 0x80094554, 0x800945F4, 0x80094CD4/CE8`):

| person anim | written by (names per `behaviour.md`) | → resource slot | sprites in the block | the pose (MEASURED, block 0 render) |
|---|---|---|---|---|
| **13** walk | spawn init `0x80091A3C`; state 0 Idle case 1; unloading `0x8008F110`; message handler `0x8008F880`; end of state 28; `0x800935C0`'s override | 7 | 26..65 | the walk |
| **11** idle | constructor `0x8008C534`; state 2 arrival `0x8008DB34` (two sites); litter state 29 `0x80090C00` | 5 | 20, 21 | two frames standing, facing front |
| **12** vomit | state 0 Idle case 4 (`0x8008D058`, nausea > 92 or 1-in-4, for 60 ticks) | 6 | 22..25 | bends forward to the right and back up |
| **2** watching | state 28 `0x80090A50` (watching the entertainer) | 3 | 12..15 | two narrow side-on frames then front with arms |
| **14, 5, 6, 13** | the random idle picker `0x80090994`, `0x800F7A28[rand(4)]` = `{14, 5, 6, 13}` (MEASURED; stride 4) | 2, 2, 3, 7 | 8..11 / 12..15 | slot 2: arms spread wide, front (a cheer/wave) |

Never written by guests: person animations 1, 4, 7 (→ slots 0, 1, 4: sprites 0..7, a raised-arm turn and a
turn on the spot, and 16..19, a narrow standing figure) and 15 (staff standing; `0x80094590`'s callers are
all staff classes, and for idx 0..8 it remaps to −1 = nothing drawn). So **of the 26 non-walk sprites a
guest ever shows 20, 21 (idle), 22..25 (vomit), 8..11 and 12..15 (gestures / watching)**; 0..7 and 16..19
are dead art on this disc. For the theme-2 costume (259) a costumed guest in state 28 or on gesture 6 hits
the NULL slot 3 and is not drawn for those ticks.

### 4.3 What advances the frame
The drawable's counter (§2.5): `+1` every fourth call of the draw pass (`period 4` from `0x8003236C`),
wrapping at the table's `fpf`, and **reset to frame 0 whenever the person's animation id changes** —
compared on the raw id, so switching between gestures 14 and 5 (both slot 2) restarts the cycle. Nothing
about distance or walk speed enters; the draw pass runs once per rendered frame (`0x8005814C`), so at
PAL 25 fps a guest's eight-frame walk takes 32 frames ≈ 1.3 s whatever the speed, and the counter
restarts from 0 (with a fresh drawable) every time the person re-enters the visible window.

## 5. Ride-cam: half-size people from sheet 261 (READ; the mode's meaning GUESS-high)
`manager+0x14` is set to 1 by `0x80032B28` and to 0 by `0x80032B68`, both only from `0x80053C90(mode)`
(folio.md §4.3: mode 1 also swaps sheet `0x10C` for `0x104`; its callers include the ride-cam entry
`0x80055210`, rider-positions.md §6). In that mode `0x8003186C` draws only sheet-pool drawables with
`idx` outside 9..14, through `0x800326BC(node, cam, k)` with `k = 0x8003182C(idx)` = `0, 1 → 0`;
`4..7 → 2`; anything else (2, 3, 8) `→ 1`: the same `0x800323B0` frame logic against the holder at
`0x80105638 + k*0x10` (loaded with entries `0x1A4 = 420`, `0x1A5 = 421`, `0x106 = 262` at `0x800319F8`),
sprite `0x800F1F4C[k] + frameId` = **base 0 / 62 / 124 in sheet 261** (190 sprites = 62 + 62 + 66,
MEASURED), quad at `w >> 1`, `h >> 1`, `ox / 2`, `oy * 3 / 4`, same mirror rule. Staff and the entertainer
are not drawn at all in this mode. GUESS-high: this is the on-ride camera's view of the crowd.

## 6. Corrections to `core/TPW.Data/PeopleSheet.cs`
1. `Facings = 8, WalkFrames = 5` and "eight rows that turn a full circle" are wrong: the walk is **five
   stored facings × eight frames, facing-major** (nine frames for costume 265), and the doc comment's
   "off the pictures" reading is what misled it — the pictures are consistent with §1 once the 26
   poses are separated out.
2. `WalkSprite(block, facing, frame)` must take the **relative** facing `rel = (personFacing + cam) & 7`
   (§3.2 for `cam`), fold `stored = rel < 5 ? rel : 8 − rel`, return `walkFirst + stored*fpf + frame`,
   and tell the caller `mirror = rel < 5`. Today it indexes `facing*5 + frame` with the raw person facing
   and no mirror, so only `rel 0` (and by luck one other cell) can look right.
3. `FindWalk`'s palette-run heuristic gives the right first sprite for every block on this disc (26 for
   the guests and 267/275, 26 for 265 — but with `fpf 9`, 20 for 259, 0 for the staff) yet cannot give
   `fpf`. Replace it with the resource: parse FOLIO entry `Blocks[b].Entry` per §4.1 (`nA, nB, nC`, skip
   `nA + nB` words, `nC` offsets, each table `u8 fpf, u8, u16 ids[5*fpf]`), and index animation 7 (guests,
   costumes) or 0 (staff).
4. Add the non-walk poses. The port draws every guest as walking; the original shows idle (slot 5, two
   frames) whenever it stands — queue, arrival, litter — vomit (slot 6) and gestures (slots 2/3), all
   facing-independent (the same ids for all five facings, still mirrored by `rel`). Map person animation →
   slot with `0x800F1ECC` (§2.3) and −1 = draw nothing.
5. `WalkSprites()` (the atlas) must include all 66 sprites of a block, not `walk + 40`.
6. `GuestBlocks = {0,0,1,1,2,2,3,3}` indexed by type is right **on the assumption `[V+0x10] == 0`** (§2.2);
   `Blocks`, `StaffBlocks`/named staff and `CostumeBlocks` (267/265/259/275 for themes 0..3) match
   `0x800DFFFC` and the theme rows exactly. The costume is worn when `P+0x2B & 0x80` (bought), which also
   sets type 8 — note for `rider-positions.md` §8.2 that such a rider's head row is `type 8`, beyond the
   eight rows decoded there (not traced).
7. Person facing is cardinal only, `0 = +y, 2 = −x, 4 = −y (also standing), 6 = +x` in the 8.8 tile
   frame; the port must not synthesise eight-way facings from the velocity — the quarter views come from
   the camera yaw.
8. Frame cadence is a per-drawable draw-call counter, period 4, reset on animation change and whenever
   the person leaves and re-enters view (§4.3) — not distance walked.
9. Placement is the sheet record's own `ox/oy` from the projected foot point, scaled by `8/SZ` with the
   ×1.6 horizontal stretch (§2.6); staff sprites are 2–3× the guests' texel size and are not rescaled.

## 7. Not established
* The writer or initial value of `[V+0x10]` (§2.2). If it is ever non-zero the body is `(type + phase) & 7`
  — still uniformly random, but decorrelated from the rider head row.
* The vtable path from the resource holder's ctor (`0x8002BC18`, vtable `0x800DDCD8`, entry 1
  `0x8002B4B0` → `0x8002B8C0`) to the parser `0x8002B4DC`; the parser was identified by its unique
  `+0x18` store and by the values it produces (the C offsets of every entry decode to valid tables).
* What mode word `[0x80103840] = 1` is on screen, and what the sheet-261 art looks like (not rendered).
* `0x8001096C` in the placement scale; the `+0x54` position virtual and `0x80053F40`'s window were not read.
* The head icon over a person (`0x80093E00` → sheet 416) and why 259's slots 0 and 3 are empty.

## 8. Reused from earlier findings
`staff.md` §3 supplied the staff tables, the remap `0x800F1EAC`, the fold `0x8002BB58..68`, the flip arms
of `0x8003377C` and the period-4 counter; this note re-read those sites on the guest route (the same
functions), added the guest remap `0x800F1ECC`, the guest resources, the slot-45 body choice, the camera
facing, the writer census and the pictures. `behaviour.md` §2 supplied the state names and the meaning of
animations 11/12/13 and the table `0x800F7A28`; `rider-positions.md` §8 the method and the sheet-416
contrast; `folio.md` §4.3 the mode-word switch sites; `psyq-named-functions.json` the identity of
`0x800C9340`, `0x800C4C24`, `0x800C4C04`.
