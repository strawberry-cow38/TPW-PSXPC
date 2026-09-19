# Theme Park World (SLES-026.88) — paths, connectivity, and how to make a shop reachable

Sixth report. Every address is in the RAW image (`TPW.BIN` at 0x80010000) unless it is marked
**overlay**, which means it lives in the in-game overlay that the game decompresses into RAM at
0x80114158 (§1.5 — this is why those addresses are past the end of the file). READ = read from the
instructions/data at the quoted address; GUESS = inference, with confidence. New helpers beside this
file: `paths_tool.py` (turns a live tile-map dump into the exact pokes, and runs the game's own
reachability test), `ovl_lz.py` (decoder for `TPW.OVL`, port of 0x800BFD9C).

## 0. Corrections to earlier reports — read first

1. **behaviour.md §2.2 "other types → destination = target position + half its footprint"** — wrong.
   0x80062E48 is *GetEntranceTile*: it takes the record's entrance offset (rec+0x0C) rotated by
   0x800634B8 and state 6 adds the attraction's tile position to it (0x8008E3F4..0x8008E468, READ).
   So a guest heading for a shop walks to **the shop's own entrance tile** — the "built-in path tile"
   strawberry described. It is a real tile in the map: **type 7** (§2.3).
2. **behaviour.md §2.2b "returns −1 if the ride centre tile fails 0x800508C8 (off-map/unreachable)"** —
   0x800508C8 is a pure bounds test (`0 ≤ x < w−1 && 0 ≤ y < h−1`, READ). **Reachability is never
   tested when a guest chooses a target.** An unreachable shop is chosen, the path request fails, the
   guest loses `rand(15)` happiness and wanders (§2.3). That is exactly your 360 guests / 0 sales.
3. **behaviour.md §2.10 "pathfinder messages 1/2 — sender not located"** — located: the pathfinder
   worker 0x800EC490 sends message 1 (path ready) or 2 (failed) through person vtable slot 40
   (0x800EC5C8..0x800EC5E0 and 0x800EC664..0x800EC688, READ). The message object's vtable is
   0x8011415C (overlay).
4. **rides.md §2 shop extras ("shop A+0x78 day built … A+0x88 sale price")** stand as A-relative:
   the price getter 0x800B7148 is `lhu 0x88(a0)` and the guest purchase 0x8008E5EC calls it with the
   same pointer it uses for the vtable at +0xC (READ). See §6 for an anchor that cannot slip.
5. Your live correction "status is at +0x6A, not +0x6E": in the code the **type** byte is at A+0x6A
   (0x80063210 `lbu 0x6A`) and the **status** byte at A+0x6E (0x8006321C `lbu 0x6E`), READ. A Fries
   shop must read **4** at the type byte. If you read 2 at "+0x6A" and 0xFFFF at "+0x6E" your anchor is
   8 bytes low (A+0x62 is the high half of the animation timer, A+0x66 is the s16 that is −1 from
   placement). §6 gives offsets from the vtable word itself so this cannot happen again.
6. parkopen.md's spawn conditions get a sibling: **while a build item is held, the pathfinder is
   paused** as well as arrivals (§4 item 5). Not a correction, but it is the same trap.

## 1. How the game represents paths and connectivity

### 1.1 The tile map (READ, loader 0x800544E0)
The map is a FOLIO resource, so its tiles live in the heap and the base must be read live:

| what | address | READ at |
|---|---|---|
| **tile array base** (tile (0,0)) | `u32 [0x801038A8]` | 0x8005456C |
| width w | `u32 [0x801038AC]` | 0x80054554 |
| height h | `u32 [0x801038B0]` | 0x80054558 |
| tile (x,y) | `base + (x + y*w) * 8` | 0x80053FD0 (`Tile(x,y)`) |
| in-bounds | `0 ≤ x < w−1 && 0 ≤ y < h−1` — the last row/column never counts | 0x800508C8 |
| building table | count `[0x8010392C]`, entries of 12 bytes at `[0x80103930]` | 0x80054584/88 |
| exit points | count `[0x80103938]`, at `[0x8010393C]` | 0x800545C0/C4 |

Resource layout: `u32 N; u32 tab[N]; u32 w; u32 h; tile[w*h] (8 bytes each); u32 nbuild; …`.
(`tab` is stored at 0x80103928 / count 0x80103924; its meaning is not established.)

### 1.2 The 8-byte tile (READ unless marked)
| byte | meaning | evidence |
|---|---|---|
| +0 | **type** (§1.3) | getter 0x80050064, setter 0x8004D350 |
| +1 | unknown | no reader found |
| +2 | **link bits** — which neighbours this tile is joined to (§1.4) | getter 0x80050050, set 0x8004E1F8, clear 0x8004E1E0 |
| +3 | **facing** of an entrance/exit tile, one bit in the same encoding as +2 | set 0x8004FA60 from placement 0x80064034 |
| +4 | u16, written by 0x80050060; GUESS-medium: model/piece index | |
| +6 | bits 0–5 and 6–7 packed (0x8004D208/0x8004D21C); GUESS: appearance variant | |
| +7 | **flags**: 0x01, 0x02, 0x08, 0x10, 0x40 tested (0x8004D358). 0x08 marks the gate line the walk-in state 45 scans for (0x80091334); 0x02 excludes a grass tile from the "empty" test; 0x10/0x40 gate the grass-walkability test (§2.3). Meanings beyond that are GUESSES. | |

### 1.3 Tile types (READ from every `SetType` site and predicate; names are GUESS-high from use)
| type | what | who writes it |
|---|---|---|
| 0 | grass / nothing | map data; 0x800541BC at gate-side tiles |
| 2 | **path** | path tool 0x8004E034 (kind 2), demolish 0x80064314 → 0x8004D19C |
| 4 | queue path (rides) | path tool kind 4 |
| 5 | building footprint | placement 0x80063B98 at 0x80063E00 |
| 7 | **attraction entrance tile** — the shop's own path tile | placement 0x80063F94 |
| 8 | attraction exit tile (rides) | placement 0x800641A0 |
| 10 | track-ride piece | 0x800B38F4 |
| 12 | park gate tile | map post-load 0x800541BC at 0x8005423C (where 0x800620C4 == 2) |
| 13 | path ∧ queue overlap (0x8004E034 turns 2↔4 collisions into 13); walk-in and wander treat it as path | path tool |
| 14 | gate-side tile | 0x800541BC at 0x80054254 / 0x80054338 |

### 1.4 Connectivity is a per-tile flag, not a flood fill (READ)
Each tile's byte +2 holds one bit per neighbour it is joined to. **Stored on both sides** — laying a
path writes the new tile's bit toward the neighbour and the neighbour's bit back
(0x8004E20C, e.g. 0x8004E2C4..0x8004E2D4 and 0x8004E540..0x8004E550):

| bit | direction | (dx,dy) |
|---|---|---|
| 0x01 | north | (0, −1) |
| 0x04 | east | (+1, 0) |
| 0x10 | south | (0, +1) |
| 0x40 | west | (−1, 0) |
| 0x02 / 0x08 / 0x20 / 0x80 | NE / SE / SW / NW — model choice only, never walked | |

The pathfinder's direction table (overlay 0x8011417C = (1,0),(0,1),(−1,0),(0,−1)) and its bit table
(0x801036D0 = 04,10,40,01) agree with this (READ from the decoded overlay and the image).

Nothing recomputes connectivity globally: **no flood fill, no graph, no "connected to entrance"
flag on the shop.** Every guest trip is a fresh A* over the tiles (§2.3), and the *only* things it
looks at are tile type, the current tile's link bits, and the request flags.

### 1.5 Where the pathfinder's data lives (READ) — why some addresses are past the file
The pathfinder code is in the post-data block 0x800E8658..0x800EE4A8 of `TPW.BIN`, but its data
(0x8011415C..) is not in the file. `TPW.OVL` is a 12-entry pack of LZ-compressed overlays
(decoder 0x800BFD9C, loader 0x800BE640); **every entry decompresses to 0x80114158**
(`u32 [0x800DB914]`). Entry **3** is the in-game overlay and carries the pathfinder's tables:

| overlay address | contents |
|---|---|
| 0x8011415C / 0x8011416C | two 1-slot vtables (message class → 0x800ED770; a game-mode-sibling class → 0x800ED77C) |
| 0x8011417C | direction table, 4 × {s16 dx, pad, s16 dy, pad} |
| **0x8011419C** | **15-word jump table indexed by tile type** (§2.3) |
| 0x801141D8 | A* node pool: 2000 nodes × 20 bytes |
| 0x8011EDBC | request list (BSS) |

`ovl_lz.py` reproduces this; `p/ovl3_ingame_overlay_at_80114158.bin` is the decoded overlay. Live check:
`u32 [0x8011419C]` must read **0x800EC24C** whenever you are in the park.

## 2. What makes a guest walk to a shop and buy — and the test an unreachable shop fails

### 2.1 Selection (READ, corrects behaviour.md §2.2)
Idle → state 6 (every 8 ticks) → 0x8008CDC8 picks the best-scoring open attraction; the score
0x8008C818 uses distance, needs and history only. **No reachability test.** For a shop (type 4):
```
E = shopTile + rotate(rec+0x0C entrance offset, A+0x6C)      ; 0x80062E48, the type-7 tile
dest = (E.x<<8 | 0x80, E.y<<8 | 0x80)                         ; tile centre
0x800EC9F4(guest, pos.x, pos.y, dest.x, dest.y, flagA = 1, flagB = 0)   ; 0x8008E4A8..0x8008E4C4
success → V+0x28 := shop, PushState(11) ; refused (pool/queue full) → cooldown +360, PopState
```
Rotation 0x800634B8 (READ), with w,d the footprint and (ex,ey) the record offset:
rot 0 → (ex, ey); 1 → (ey, w−1−ex); 2 → (w−1−ex, d−1−ey); 3 → (d−1−ey, ex).

### 2.2 The request (READ, 0x800EC9F4 → 0x800EBD88)
The entry only *queues* a request (pool 0x80110DBC; refused if the pool or the node pool is full).
Request fields: +8 person, +0xC/+0x10 from tile (clamped to the map), +0x14/+0x18 to tile,
+0x2C flags, +0x30 tick budget := **200**, +0x38/+0x3C from/to tile pointers, +0x40 open list,
+0x54 closed buckets. The worker runs every unpaused frame from game-mode slot 4 at 0x80058DE4
(0x800EC8C4), spends 100 clock units per frame across all requests, and per request either
delivers waypoints + **message 1**, or, when the open list runs dry / the 200-tick budget is gone /
the node pool is exhausted, **message 2** (0x800EC490).

### 2.3 The step test — the condition an unreachable shop fails (READ, 0x800EBFD4 + overlay table)
For the node at tile T, each orthogonal neighbour N (random order) is admitted by N's type:
```
mask = 0x55 if T is grass (type 0, flag 0x02 clear) or footprint (5), else T.link        ; 0x800EC028..
switch (N.type)  via 0x8011419C[type]:
  0  grass      : flags&2  AND 0x8004D388(N) (flag 0x10 clear, or 0x40 set) → cost 2    ; 0x800EC24C
  2  path       : flags&1  AND (mask & bit(dir))                         → cost 1        ; 0x800EC174
  13 path∧queue : same as 2                                                              ; 0x800EC174
  4  queue      : flags&0x10 AND (mask & bit(dir))                                       ; 0x800EC194
  5  footprint  : flags&8 → yes; else only if N == destination           → cost 2        ; 0x800EC1DC
  7  ENTRANCE   : (mask & bit(dir)) AND N == destination                                 ; 0x800EC208
  8  exit tile  : always                                                                 ; 0x800EC28C
  12 gate       : flags&1 (no link test)                                                 ; 0x800EC154
  14 gate-side  : flags&0x20                                                             ; 0x800EC278
  1,3,6,9,10,11 : never                                                                  ; 0x800EC444
```
Cost is added to g; h is Manhattan distance; closed/open lists are checked before a node is
allocated (0x800EC2B8..0x800EC43C).

A shop trip carries **flags = 1**. So the guest may only move along tiles of type 2/13 (and gate
tiles) whose **current tile's link bit points at the next one**, and the final step onto the shop's
type-7 tile needs **the tile in front of the entrance to hold the link bit toward it**. A shop on an
open field fails at the very first expansion: every neighbour of the guest's tile that is grass is
rejected (`flags&2` is clear), and the search dies with an empty open list → message 2.

### 2.4 After the fix: arrival and purchase (READ, unchanged from behaviour.md)
Message 1 → state 3 → walks the waypoints → state 2 arrival, purpose 0, type 4 → V+0x2C := now +
120 + rand(300), state 35 → state 22 → 0x8008E5EC: buys iff `want > price` and `money ≥ price×10`.
Fries record (`records_named.txt` 0x0ED): default price **60**, unit cost **40** → base 50 → want ≈
50 × N × (happiness+100)/100 /100 with N ≈ 100..150, i.e. 75..150 > 60 for most guests. Sales
book into A+0x8C/A+0x90 and the guest counter A+0x14.

### 2.5 What placement already does (READ, 0x80063B98)
Footprint tiles := type 5; the entrance tile := type **7** with **byte +3 = facing bit**
(0x80063FD0..0x80064034: facing index 0/1/2/3 → 0x01/0x40/0x10/0x04); then it calls the world
linker 0x8001BD30 for that tile with kind 2 (shops/features/sideshows) or 4 (rides). Laying a path
in front of an entrance later goes through 0x8004E20C, which sets the path tile's bit toward the
entrance **and the entrance tile's bit back** when `entrance.byte3 & bit` matches
(0x8004E3BC..0x8004E3F4 for north, 0x8004E638..0x8004E670 for east; the other two directions are
the same code shape, GUESS-high). That pair of bits is the whole "connection".
⚠ Correction (cow tools, §10.1): the 7 goes on the door's OWN tile, on the footprint's edge, and the
0x8001BD30 run is on the tile one step outside it (0x800635E8), not on the 7.

## 3. ★ What to write

Three ways, in order of faithfulness. All are plain memory writes; none needs a function call.

### 3.1 Lay the path in memory (game-exact; what the path tool would have written)
Read live: `base = u32[0x801038A8]`, `w = u32[0x801038AC]`, `h = u32[0x801038B0]`; dump `w*h*8`
bytes from `base` to a file. Then:
```
python3 paths_tool.py scan  DUMP W H BASE          # finds the type-7 tile E and its facing bit f
python3 paths_tool.py lay   DUMP W H BASE          # prints the pokes
```
What `lay` emits (this is the recipe if you do it by hand):
1. **E** = the type-7 tile (there is exactly one per placed shop); **f** = E.byte3 (one of
   0x01/0x04/0x10/0x40); **P0** = the tile in front, E + dir(f).
2. Chain P0, P1, …, Pn as an L from P0 to **Q**, the nearest existing path tile (type 2 or 13 with
   guests standing on it — the plaza the walk-in state 45 puts them on).
3. For each Pi: `byte+0 := 2`, `byte+2 := bit(toward Pi−1) | bit(toward Pi+1)`; P0 additionally gets
   `bit(toward E)` = the opposite of f.
4. `Q.byte+2 |= bit(toward Pn)` — the network end must point back.
5. `E.byte+2 |= f` — so guests can step **off** the shop tile afterwards (0x8004E20C would set this).
Write once after the park is loaded; the tiles are not rewritten unless the player builds there.
Re-dump after writing and run `paths_tool.py reach DUMP W H GX GY BASE` with a guest's tile
(`P+0x18>>8`, `P+0x1A>>8`) — it must print REACHABLE (that command is the game's own step test).
Visuals: a path tile's look is its ground word (+4), which 0x8004D9DC rewrites from the link bits: see
`core/TPW.Data/ParkPaths.cs` for the piece table (0x800EFFCC) and the world's path sprites. Write +4 too, or
the new path shows the grass it was laid on; the simulation does not care.

### 3.2 One-word switch: make grass walkable for every request (data, not code)
```
u32 [0x8011419C] = 0x800EC28C        ; type-0 entry of the jump table → "always admit, cost 1"
```
Hold it every frame (the word is rewritten only when an overlay reloads, but holding costs nothing).
Effect (READ from the table semantics): every path request can cross grass; from a grass tile the
mask is 0x55 so the last step onto the type-7 tile passes its link test automatically. Guests take
straight lines over grass instead of paths, and they will also cross tiles that 0x8004D388 would
have refused (flag 0x10 set — whatever those are). This is **not a debug flag** (there is none in the
pathfinder — I looked for a reader-without-writer like 0x80102E60 and there isn't one), it is the
game's own dispatch table with one entry redirected.

### 3.3 One-word code patch: give the shop/ride trip the wander flags
```
u32 [0x8008E4A8] = 0x24030003        ; was 0x24030001  (addiu v1,zero,1 → 3)  in state 6
```
Only state-6 trips change: grass is admitted through the normal 0x8004D388 test at cost 2, so guests
still prefer real paths where they exist. Queue joins, exits and staff are untouched.

Pick 3.1 if you want the measurement to reflect the real game (path capacity, link bits, guests
bunching on the path); 3.2 if you just want every placed thing reachable now.

## 4. Falsifiers — read live, no waiting

1. **Static, exact:** `paths_tool.py reach` on a fresh dump (§3.1). It implements §2.3 verbatim; if
   it says REACHABLE and no guest ever arrives, §2.3 is wrong and I want the dump.
2. **The step that fails today:** with the shop on a field, `E.byte+2 == 0` and P0 is type 0. After
   3.1, `P0.byte+0 == 2`, `P0.byte+2 & opposite(f) != 0`, `E.byte+2 & f != 0`.
3. **Per guest:** a Visitor with `P+0x20 == shop` (the A pointer) in state **3 or 2** has had its
   request *accepted* (message 1 → state 3, 0x8008F880). Before the fix you only ever see
   `P+0x20 == shop` in states 6/11, then `P+0x20 == 0` and state 5. A guest in **state 35 with
   P+0x20 == shop** is standing on E; the purchase follows within 120..420 ticks (state 22).
4. **Table sanity:** `u32[0x8011419C] == 0x800EC24C`, `u32[0x8011419C+4*7] == 0x800EC208`,
   `u32[0x8011419C+4*2] == 0x800EC174` in the park. If not, the overlay index I read (entry 3) is
   not the one loaded and §2.3's type→case map is unverified.
5. **Pathfinder alive:** `u32[0x801036C8] == 0` (set to 1 by 0x800EBA2C from the catalogue pick-up
   0x80058F90, cleared by 0x800EBBE4 from 0x80059004). Nonzero = every request sits in the queue
   forever, which looks exactly like "unreachable".
6. The first sale: `A+0x14` (guests served) goes 0 → 1 and `A+0x8C` moves.

## 5. Everything else the tile code told me (READ)
- `0x8004D4EC(tile)` = type 2; `0x8004D558` = 13; `0x8004D510` = 4 or 7; `0x8004D57C` = 7;
  `0x8004D5A0` = 8; `0x8004D5C4` = 10; `0x8004D5E8` = 5; `0x8004D60C` = grass with flag 0x02 clear;
  `0x8004D670` = flag 0x08; `0x8004D650` = flag 0x10. `0x8004D388` is the "may a guest cross this
  non-path tile" predicate used for grass (§2.3) and by the wander.
- `0x8004E20C(tile, kind, …)` = LinkTile for kinds 0/2/4/13; `0x8004DE04(tile, kind, dx, dy)` is the
  path-tool per-tile dispatcher (kind < 0x80 = place piece via 0x8004E034; 0x80 = relink; 0x82 = set
  facing; 0x32 = unlink 0x8004FBEC); `0x8001B924(kind, x0, y0, &x1, &y1)` lays a straight run.
- `0x800590A0(tile, &pos)` = tile pointer → (x<<8, height, y<<8); `0x8004DC78(tile, dx, dy)` =
  neighbour or NULL; `0x800598B4` counts type-2 tiles in the map.
- Walk-in (state 45, 0x800912BC): from the guest's tile scan up to 15 tiles in +y; after a tile with
  flag 0x08, the first type-2/13 tile becomes the waypoint. That is where guests idle, and where the
  chain in §3.1 must end.
- Wander (state 5) follows link bits on path tiles and uses flags (3,0) otherwise, so once §3.1 is
  written idle guests also drift onto the new path by themselves.

## 6. Attraction anchor that cannot slip
Let **V** = the address of the word holding the shop vtable 0x800E6A8C (stored by the ctor at
0x800B7188 `sw v0,0x14(outer)`, i.e. A+0xC). Then, all READ:

| field | address | expect for your Fries shop |
|---|---|---|
| type | V+0x5E (A+0x6A) | 4 |
| variant | V+0x5F | 0 |
| rotation | V+0x60 (A+0x6C) | 0..3 |
| status | V+0x62 (A+0x6E) | 2 |
| x, y tile | u16 V+0x4C, V+0x4E (A+0x58/0x5A) | matches the footprint tiles of type 5 |
| guests served | V+0x08 (A+0x14) | 0 until the first sale |
| price | u16 V+0x7C (A+0x88) | 60 |
| quality | u16 V+0x7E (A+0x8A) | |

## 7. Not established
- Tile bytes +1, +4, +6 and flags 0x01/0x02/0x10/0x40 beyond the tests that use them.
- ~~What 0x800620C4 returns~~ SETTLED: it is the type byte (+0). The post-load pass (0x800541B8) makes every
  authored path tile entrance road (12) and the non-path tile after it in z a road end (14), then lays path
  on the flag-0x08 tiles with the path tool; the two road ends among them stay 14 and wear path sprite 11.
  Port: `ParkPaths.LayStartingPaths`.
- Whether the plaza tiles in a fresh map are type 2 or 13, and their authored link bits (read them:
  `paths_tool.py scan`). §3.1 ORs the bit into whatever is there, so the recipe holds either way.
- The link-bit code for the west and south neighbours in 0x8004E20C was not read line by line
  (0x8004E794..0x8004ECC8); the encoding is fixed by the two directions that were and by the
  overlay's direction table.

## 8. The path tool's ghost markers — how the game actually draws them (READ)

Entry point `0x8001D9D0` (the L-shaped run walker). For each tile in the run it calls the validator
`0x8004F360`, uses the returned verdict as an index into a **sprite table at 0x800DBEFC**, and draws via
`0x800553B0(x, y, sprite, 0, 1, 1)`.

**Verdict → sprite id** (READ, the six words at 0x800DBEFC):

| idx | 0 | 1 | 2 | 3 | 4 | 5 |
|---|---|---|---|---|---|---|
| sprite | 165 | 175 | 168 | 169 | 170 | 173 |

Which index means ok / blocked / ends-on-path is **not established here** — it was determined
empirically (blue = will lay, dark red = refused, green ring = run ends on existing path).

### 8.1 Two primitives per tile, both semi-transparent

`0x800553B0` emits **two** quads, and this is the part worth having:

| | primitive | code | sprite | shade |
|---|---|---|---|---|
| underlay | POLY_FT4 | 0x2C | **fixed 171** (`gp[0x12ac] + 0x804`, stride 12) | flat **0x40** on r/g/b |
| marker | POLY_GT4 | 0x3C | the verdict sprite | **four different per-vertex shades** |

Both get `code |= 2` (`0x80055C2C` / `0x800561F8`) — **setSemiTrans, so both are see-through** — and
`code &= ~1` right after, i.e. texture blending stays ON.

The underlay's shade is `0x40` where `0x80` is 1.0, so the "dim quad the game lays under the markers"
is dim *because of a half-brightness shade*, not because of the blend.

### 8.2 The ripple: a gouraud gradient AND moving geometry (READ)

The GT4's four vertex colours are computed separately (`$s0/$s1/$s2/$s3` → prim +4/+0x10/+0x1C/+0x28)
and each is driven by **`rsin`** (`0x800C4AD4`, confirmed against `psyq-named-functions.json` as
LIBGTE `rsin`/GEO.OBJ), angle masked to `0xFFF` (4096 = full circle), result `>>8` and added to a base,
`andi 0xFF`. Four *different* vertex values means the brightness **travels across the tile** — a moving
gradient, not a uniform pulse. Reproducing it as a single time-varying brightness will look wrong.

⚠ **That was only half of it — I traced the colour path and stopped there.** cow tools read the
same routine further (2026-09-19) and found the same sines also move the GEOMETRY: the marker quad
floats **64 to 192 units above the ground** and every corner rides two diagonal waves at different
speeds (one forward, one back; amplitudes 32+32, phase speeds 128 and −64 per frame time), with the
sprite-171 underlay wobbling on the same wave beneath it. So the blueprint **rolls like a wave** rather
than merely brightening — which is what strawberry meant by "ripple". Shipped in 43dc1b8.

The lesson worth keeping: finding one consumer of `rsin` here did not mean finding them all. I wrote the
effect up as a brightness because the colour stores were the first thing I hit; asking what ELSE the same
angle fed would have found the vertex displacement in the same function.

### 8.3 ⚠ The blend mode is per-sprite DATA, not a constant in the draw code

`0x800553B0` never chooses a blend. Both texture setup helpers — `0x80061F10` (FT4) and `0x80061EB0`
(GT4) — do `lhu $s2, ($a0)` and store it to the primitive's **tpage** field (+0x16 FT4 / +0x1A GT4),
taking the CLUT from `2($a0)` the same way. The ABR bits (tpage 5-6) therefore come straight out of
**each sprite's own 12-byte record**, so "half" vs "additive" is a value to be read per sprite.

**RESOLVED (cow tools, 2026-09-19): the markers' tpage is `0x01E` → ABR 0, i.e. half (B/2+F/2).**
Bits 5-6 of `0x01E` are clear; the rest decodes as tpage X base 14 (=896), Y base 256, 4-bit CLUT.
So "50/50 see-through" was right. The defect the reading found was different and real: the port had
the blend *hardcoded* rather than taken from the sprite record, which this section's point stands on —
the draw code does not choose a blend, the sprite's own tpage does. It now reads per-sprite.

Worth keeping anyway: `abr=1` (additive) is also used on this disc, and additive dark red over grass
brightens toward olive where half-blend darkens toward brown, so the two are visually distinguishable
and the value is always worth reading rather than assuming — that mistake was already made once here
on the language-screen text.

## 9. The path tool's sounds — one dispatch, and where the sample rate comes from (READ)

`PlaySfx(group, index)` is `0x800B8E08`. Group 7 is the build tools. The four path-tool sounds are all
emitted by **one function, `0x8001D5C0`** — the click handler — not by four separate events:

| when | sound | evidence |
|---|---|---|
| state word `0x14(obj)` is 0, **or** `0x8001D21C` returns 0 | **g7_2** (refused) | `0x8001D700` / `0x8001D704` |
| first click (run not started, `0x18(obj)` == 0) — stores the start point, sets the flag | **g7_0** | `0x8001D648` |
| second click — the run is laid | **g7_4** | `0x8001D69C` |
| immediately after g7_4, when `0x8001B5D4` returns 0 | **g7_3** | `0x8001D6D0` |

**g7_3 is not a tool-close sound.** There is no RMB/teardown path here at all — it is a second sound
layered on top of g7_4 within the same second-click branch, on separate voices so neither cuts the
other. strawberry called this from memory ("plays when a path is connected to another one
successfully") before the code was read, and the code agrees: it is the success sub-case of laying a
run, and it is the same event the ghost markers give a dedicated verdict and green ring to (§8).
That branch also calls `0x8001C328` and shows message 5, where the other branch shows message 4.
`0x8001B5D4` itself is just `return gp[0xB8]`.

### 9.1 Sample rate is archive data, not a code constant (READ, cow tools)

`0x800B84AC` is the real play routine. Its **5th argument is a pitch override**, read at `0x88($sp)`:

    800b859c  lw   $t2, 0x88($sp)     ; arg5 = pitch override
    800b85bc  beqz $t2, 0x800b85cc    ; zero -> use the sample's own
    800b85c8  sh   $t2, 0x24($sp)     ; else the caller's value
    800b85cc  lhu  $v0, 2($a2)        ; record+2  ($a2 = index*8 + table)
    800b85d4  sh   $v0, 0x24($sp)

`0x24($sp)` sits at **+0x14** inside the block copied to the voice-attr struct at `0x800B8644`, which is
`SpuVoiceAttr.pitch`. **`PlaySfx` always passes 0**, so every sound plays at the rate stored in its own
8-byte record at +2. Group 7's records hold `0x0400`; PSX pitch `0x1000` == 44100 Hz, so
`0x400` == **11025 Hz exactly**.

⚠ A .wav exported from the port proves nothing about this — the exporter chose that header. The rate
is `record+2`, and that is the only thing worth quoting.

## 10. Doors, the queue piece and the queue tool (READ, cow tools)

### 10.1 What placing lays at the doors (0x80063B98)
- The door tiles are the record's offsets turned (0x80062E48 entrance, 0x80062FF8 exit), and every flat
  ride, shop and sideshow record READ has them INSIDE the footprint, on its edge. They become 7 / 8 with
  byte +3 = the facing bit, after the footprint loop has made them 5 and given them the pad sprite.
- The tile one step outside (0x800635E8 / 0x800636F0) gets a one-tile run: 0x8001B5A0(kind), then
  0x8001BD30 twice on the same tile (start, then end == start, which finishes it). Entrance: kind 4 for
  types 1, 3, 6, 7 (the rides), else 2. Exit: always kind 2 when placing interactively (the second
  argument is 0; when it is set, only if that tile is already path).
- The placement ghost's door markers (0x80064558) stand on those outside tiles.

### 10.2 Which tool hands over to which (tool objects 0x800EFD5C[id], vtable at tool+0x10)
| tool | builds | after a good press |
|---|---|---|
| 5 (vt 0x800DC124) | flat ride, kind 3 | g8_3, then tool **3** (0x8001C92C) |
| 6 (vt 0x800DC0A4) | tour ride, kind 7 | g8_3, then tool **3** (0x8001CAFC) |
| 14 / 16 | shop / sideshow | g8_3, then tool 0: closed (0x8001C5C8) |
| 15 | feature | g8_3, then tool 15 again while the park may take more (count limit 0x2D) |
| 7 / 11 | track ride, kind 6 / coaster, kind 1 | g8_3, then tool 8 / 12, their track builders (0x80021E48 / 0x8001F038) |

Every placement tool shares move 0x8001C454 (corner = cursor − (w >> 1, d >> 1) of the turned footprint,
every frame), turn 0x8001C6BC / 0x8001C750 (+1 only, instant, g8_9), cancel 0x8001C7E4 (g8_6); a
refused press is g7_2.

### 10.3 The queue tool (tool 3, vt 0x800DC324)
- Start 0x8001DDD8: the run starts on the queue piece outside the entrance (ride slot 0x154 = 0x8009D240);
  the camera's target yaw := ((entrance facing + rotation) & 3) << 10 (0x80053F04) and the cursor goes
  there (0x8001934C); g7_0.
- Press 0x8001DF08: only when the ghost has no refused tile and the run has fewer than 0x20 corners.
  0x8001BD30 lays from the last corner to the cursor (axis-snapped) and the run goes on from there.
  Finished (0x8001B5D4 == 0) → for a ride just placed (previous tool 5..8, 0xB..0xD: 0x80019230) the
  camera moves to the tile outside the exit and turns to face it, and the tool switches to 2, the path
  tool, whose start (0x8001D36C) starts a run there at once because it came from tool 3: g7_0 then
  g7_3. Not finished: g7_4.
- Undo 0x8001E114 (with more than one corner; g7_5) → 0x8001BB08; close 0x8001E178 (g7_7), the queue
  so far staying.

### 10.4 The queue's rules
- **Finished** when the segment's last tile was 4, 2, 13 or 10 before laying (0x8004DE04 sets
  0x801026D0; pass 0x81 returns 0 on it), or when the press lands on the run's own end.
- **Lay** (0x8004E034, validator 0x8004F200): grass takes 4. Queue onto path is refused, but the tile
  becomes 13 (path and queue) and the run stops there: that is how a queue joins the paths.
- **Ghost** (0x8004F360, kind 4) per tile: flag 0x02 → 1; a queue tile → 1 if it has two links or is not
  one of the run's corners (0x8004D298), else 0; grass → 0; path or 13 → 5 on the LAST tile (the join,
  marker #173), else 1; anything else → 1; after a 1, every tile is 1. Markers 0x800DBEFC: 0 → #165,
  1 → #175, 5 → #173, 6 → #167.
- **Linking** (0x8004E20C, kind 4): a queue tile joins only the tile BEHIND it along the run, and only if
  that one is a queue with fewer than two links that lies on the run (0x8001B76C), so a queue is one line
  and never joins a stretch of itself it runs beside. The first tile of a segment also joins the
  ride's entrance tile if it is beside it and has no links yet, and looks behind along the previous
  segment's way (0x80102D0C / 10; 0x80 when unset). A 13 drops its diagonal links on the joined side.
- **Facing** (pass 0x82, 0x8004FA68): each tile ORs in the way back along the run.
- **Pieces** (0x8004D9DC): table 0x800EFF48 (count 0x801026DC, 11 records: straight, end, corner, lone)
  over the world's four queue sprites, world record +0x98 (0x80102DF4 / 0x80102E3C / 0x80102E0C /
  0x80102E24 for worlds 0..3). Lost Kingdom's are a wooden walkway: 213 lone, 212 end, 210 straight,
  211 corner.
- **Undo** (0x8001BB08 → pass 0x32 → 0x8004FBEC) removes the last segment but its first corner: each
  tile drops its four links, from the neighbours too, and is grass again (kind 0) wearing one of the
  world's two grass sprites (+0x88) at a random turn.
- **Paths meet doors** (0x8004E20C, kind 2): an exit (8) or entrance (7) whose facing points at the path
  tile is joined both ways; a queue whose own link already points at it only picks its piece again.

### 10.5 ⚠ Where the port's path tool is not the game's (master's controls)
The game's path tool is chained like the queue: a press lays the ghost only if NO tile of it refuses
(0x8001D5C0 checks the ghost first), the run then goes on from its end, and the tool CLOSES itself
when a run finishes on existing path (0x8001C328 after g7_3). The port's path tool is click start /
click end (master's controls), and it also lays up to the first refusal and stays open: those two are
the port's, not the game's, and wait on master's call.

