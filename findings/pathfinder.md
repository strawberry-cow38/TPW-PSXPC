<!-- provenance -->
> **Traced by fable (2026-09-19) on tinyclaw's brief; spot-checked by tinyclaw before landing.**
>
> A subagent's headline is a lead, not a result, so five load-bearing claims were re-read from
> TPW.BIN independently before this file was committed. All five matched exactly:
>
> | claim | checked against | result |
> |---|---|---|
> | request pool lives at 0x8011EDBC | `lui 0x8012; addiu -0x1244` at 0x800ECA00 | ✅ |
> | two silent refusals, then return 1 | 0x800EC9F4..0x800ECA8C | ✅ |
> | 10 request slots of 0xA4 bytes | `slti 0xA` / `addiu 0xA4` at 0x800ED268 | ✅ |
> | 2000 nodes x 20, free count at +0xABE0 | `ori 0x9c40`, `slti 0x7d0`, `sh 0x2be0($v0)` at 0x800ED300 | ✅ |
> | 1000 waypoints x 4 at 0x800F7A38 | `addiu 0x7a38` / `slti 0x3e8` at 0x80093A1C | ✅ |
>
> Everything NOT in that table is fable's reading and carries its own READ/GUESS marks. The sampling
> says the report is careful where it was checked; it is not a blanket warrant for every line.

# Theme Park World (SLES-026.88) — the PATHFINDER, read from TPW.BIN

Every address is in the raw image (`TPW.BIN` at 0x80010000) unless marked **overlay**, which means the
in-game overlay decoded to 0x80114158 (`fable/p/ovl3_ingame_overlay_at_80114158.bin`). **READ** = taken
from the instructions or data at the quoted address. **GUESS** = inference, with a confidence. Anything I
could not pin to an instruction is in §11 "Not established", not smuggled into the body.

Tools used: `tdis.py` (bounded capstone windows + raw JAL scan), `callsites2.py` (flag extraction at every
call site). Both sit beside this file. The full disassembly of the block 0x800E8658..0x800EE4A8 is
`block.dis` (6036 instructions, zero undecodable words).

## 0. One-paragraph answer

It is **A\*** (READ, §4): f = g + h, h = Manhattan distance in tiles, step cost 1 on path-like tiles and 2
on grass/footprint, an open list kept **sorted by f** as a doubly-linked list, a closed set of **4 unsorted
buckets keyed by (x+y)&3**, and closed nodes are re-opened when a strictly better f arrives. Nodes come
from a **global pool of 2000 × 20 bytes** shared by every outstanding request. The request queue holds
**10 requests**; an 11th is **refused at the entry point** (returns 0), never queued or dropped. The
search is **time-sliced**: every unpaused frame the newest request gets up to ~100 hblank lines of
expansion, and each request may be worked on at most **200 frames** before it fails. The result is a
**chain of up to 1000 global 4-byte waypoints**, corners only, head index stored at person+0x28, handed
over with **message 1**; **message 2** is sent on empty open list, on the 200-frame budget, on node-pool
exhaustion, or on waypoint-pool exhaustion. `flagA` is a **bitmask of tile classes the walker may use**;
`flagB` is a **"keep servicing me even when another request already used this frame"** priority bit.

## 1. Entry: 0x800EC9F4(person, fromX, fromY, toX, toY, flagA, flagB) — READ

```
800EC9F4  s2=person s3=fromX s4=fromY s1=toX ; toY=0x48(sp) flagA=0x4C(sp) flagB=0x50(sp)
800ECA28  if 0x800ECBF8(pool 0x8011EDBC) != 0      -> return 0    ; request free-list empty
800ECA3C  if 0x800ECFC0(nodepool 0x801141D8) != 0  -> return 0    ; node pool count == 0
800ECA4C  req = 0x800ECB70(pool)                                    ; pop a free slot, push on ACTIVE list
800ECA78  0x800EBD88(req, person, fromX, fromY, toX, toY, flagA)   ; fill + seed the search (§1.1)
800ECA84  0x800ED384(req, flagB)                                    ; req+0x34 := flagB
800ECA8C  return 1
```

- `0x800ECBF8` is `return pool+4 == 0` (READ 0x800ECBF8..0x800ECC00) — pool+4 is the free-list head.
- `0x800ECFC0` is `return u16[nodepool+0xABE0] == 0` (READ 0x800ECFC0..0x800ECFD0) — the free-node count.
- So the **return value means "accepted", exactly as the brief says**; nothing about reachability is
  known at this point. Both refusals are silent: no message is sent, the caller sees 0.

### 1.1 The request record, filled by 0x800EBD88 — READ (0x800EBD88..0x800EBFD0)

| offset | value | READ at |
|---|---|---|
| +0 / +4 | next / prev in the pool's active or free list | 0x800ED28C, 0x800ECC58 |
| +8 | person (the pointer the caller passed — callers pass `object+8`, see §9) | 0x800EBDA8 |
| +0xC / +0x10 | from tile x,y = `fromX>>8`, `fromY>>8` (arithmetic shift), clamped to `0..w-1` / `0..h-1` | 0x800EBD94..0x800EBE5C |
| +0x14 / +0x18 | to tile x,y = `toX>>8`, `toY>>8`, clamped the same way | 0x800EBDC8..0x800EBEDC |
| +0x1C / +0x20 | **raw** toX, toY (8.8) — becomes the final waypoint verbatim (§7) | 0x800EBDD8, 0x800EBDE4 |
| +0x24 / +0x28 | raw fromX, fromY — **written and never read** anywhere in the block | 0x800EBDB4/B8 |
| +0x2C | flagA | 0x800EBDEC |
| +0x30 | **frame budget := 200** (0xC8) | 0x800EBDC0/C4 |
| +0x34 | flagB | 0x800ED388 |
| +0x38 / +0x3C | tile pointers `Tile(from)`, `Tile(to)` via 0x80053FD0 | 0x800EBEE8..0x800EBF08 |
| +0x40 | open list sentinel (20 bytes, circular, next/prev = self) | 0x800ED198 |
| +0x54 | 4 closed-bucket sentinels × 20 bytes | 0x800ED154 |
| size | **0xA4 = 164 bytes** | slot stride at 0x800ECB1C / 0x800ED270 |

Clamping uses `w` from `[0x801038AC]` and `h` from `[0x801038B0]` (0x80053FB8 = `lw 0x1258(gp)`,
0x80053FC4 = `lw 0x125C(gp)`, gp = 0x80102654 READ at 0x800C0514). Note the clamp is to `w-1`, i.e. the
pathfinder's world is `0 ≤ x < w`, **not** the `x < w-1` of 0x800508C8 — the last row/column exist to it.

Seeding (READ 0x800EBF18..0x800EBFB4): one node is allocated from the pool, `g := 0` (+0xC),
`h := |toX-fromX| + |toY-fromY|` in tiles (+0xE, via abs 0x80012DB8), `x,y := from` (+0x10/+0x11),
`parent := 0` (+8), open and closed lists are reset, and the node is pushed onto the open list.
The two calls `0x800ED6CC(tile)` at 0x800EBF04/0x800EBF10 read the tile type byte and **discard it**
(dead code, GUESS-high: a compiled-out assert).

## 2. The request queue — READ

**Pool object at 0x8011EDBC** (BSS): +4 free-list head, +8 active-list head, +0xC active count, +0x10
ten request slots of 164 bytes (0x800ED21C: `slti s1, 0xA` loop at 0x800ED268, stride 0xA4 at
0x800ED270; ctor loop `s0 = 9 .. 0` at 0x800ECB04..0x800ECB1C). Total 0x668 bytes accounted to a
memory-statistics counter at 0x800ED228/0x800BEBFC.

- **Capacity: 10 outstanding requests.** The 11th call to 0x800EC9F4 **returns 0 immediately**
  (0x800ECA30). Nothing is queued, nothing is evicted, no message is sent. Callers handle it themselves
  (behaviour.md: state 6 adds a 360-tick cooldown and pops; state 23 just retries next tick).
- **Allocation is push-front** (0x800ED28C: `node->next = *head; node->prev = 0; if *head: (*head)->prev
  = node; *head = node`), so the **active list is newest-first**. §3 walks it from the head.
- A finished request goes back to the free list by 0x800ECC04 (count−1, unlink from active, push-front on
  free) — only caller is the scheduler at 0x800EC940.
- **Reset without notice:** 0x800EBBE4 (the "build item put down" unpause, called from 0x8005905C) calls
  0x80093A1C (frees **all 1000 waypoints**), 0x800ED2FC (**resets the node pool**) and 0x800ED21C
  (**rebuilds the request pool — every outstanding request is dropped with no message 1 or 2**), then
  clears bit 0x40 of `+0x2B` on every person (0x80093DDC(p,0) over the six person lists, 0x800EBC0C..
  0x800EBD70) and clears the pause word. Person+0x28 is **not** touched, so a walker's waypoint head
  dangles into a pool that has just been wiped. Its partner 0x800EBA2C (pick-up, from 0x80058FD0) sets
  the 0x40 bit on everyone and sets `[0x801036C8] := 1`, which stops the scheduler (§3).
- Init/shutdown (GUESS-high from their callers): 0x800EC98C (from 0x80058A68) and 0x800EC9C4 (from
  0x80057F54) reset both pools and
  write `[0x801036CC]` (gp+0x1078) := 1 / 0. That word is **write-only** in the whole image (raw scan for
  `lw/sw rX,0x1078(gp)`: only those two stores).

## 3. The per-frame scheduler: 0x800EC8C4 — READ

Called once per game-mode tick at 0x80058DE4 (paths.md calls that function slot 4; I read only the
window 0x80058DC0..0x80058E10), after `0x8005BA74()` (= `[0x80102D20]`, GUESS-medium: the game-pause
flag) tested zero.

```
800EC8C4  if [0x801036C8] != 0 -> return            ; pathfinder paused while a build item is held
800EC8EC  t0 = GetRCnt(RCntCNT1); deadline = t0 + 100 (0x800EC900)
800EC8FC  req = active head (newest) ; done = 0
loop:     next = req->next
800EC918  if done && req->flagB != 1 -> skip         ; 0x800ED378 = lw 0x34(req)
800EC930  r = 0x800EC490(req) ; if r: 0x800ECC04(pool, req)   ; finished -> back to free list
800EC948  done = 1
800EC94C  if deadline < GetRCnt(RCntCNT1) -> return
800EC960  req = next ; loop while req
```

So each frame: the **newest** request always gets a slice; after that, **only requests with flagB == 1**
get one, and only while the frame's 100-count deadline has not passed. Since the worker itself spends up
to ~100 counts (§4), in practice a second request in the same frame only runs when the first finished
early. Older flagB == 0 requests wait — their own 200-frame budget is **not** consumed while waiting,
because the decrement is inside the worker (0x800EC4C0/0x800EC54C).

**What the clock is (READ):** 0x800D40F0 = `GetRCnt(0xF2000001)` (LIBAPI per
psyq-named-functions.json; 0x800D43A8 = GetRCnt), i.e. **root counter 1 = horizontal-blank counter**, set
up at 0x800D4158 with `SetRCnt(RCntCNT1, 0xFFFF, 0x2000)` (target 0xFFFF, RCntMdNOINTR: free-running to
0xFFFF, no interrupt). 100 counts = **100 scanlines ≈ 6.4 ms of a 312-line PAL frame**. All comparisons
are unsigned 32-bit on a 16-bit counter (0x800EC954 `sltu`, 0x800EC648 `sltu`), so when the counter
wraps mid-slice the budget test is wrong for that slice (READ that it wraps; the consequence is
arithmetic, not a guess).

Profiling brackets `0x800BAD24(n)` / `0x800BACBC(n)` with n = 8..12 wrap the expansion sub-steps
(0x800EC07C, 0x800EC2BC, 0x800EC2C4 …). They are start/stop timers into tables at 0x8010ACEC/0x8010AD2C
and change nothing (READ 0x800BACBC..0x800BAD58).

## 4. The per-request worker: 0x800EC490(req) — READ

Returns 1 = request finished (remove me), 0 = yielded (call me again next frame).

```
800EC4B4  b = req->budget(+0x30) ; if b == 0:                      ; 200-frame budget gone
800EC4C4      0x800EC6C0(req)  ; free every node on open + closed back to the pool
800EC4D0      msg = {vtable 0x8011415C, kind 2} ; person->vtable[0x144](person+adjust, &msg)
              return 1
800EC54C  req->budget = b-1 ; t0 = GetRCnt ; deadline = t0+100 ; expanded = 0
check:
800EC654  if open list empty (0x800ECEA0: sentinel->next == sentinel) -> message 2, free lists, return 1
800EC564  n = pop open head (0x800ECE70: first node, unlinked)
800EC57C  if (n.x, n.y) == (req->toX, req->toY):                    ; goal test at POP time
800EC5A4      ok = 0x800EC748(req, n)   ; build the waypoint chain (§7)
800EC5C8      message (ok ? 1 : 2) ; 0x800EC6C0(req) ; return 1
800EC5E4  r = 0x800EBFD4(req, n)      ; expand (§5)
800EC5EC  if r != 0 -> 0x800EC6C0(req) ; message 2 ; return 1     ; node pool ran dry mid-expansion
800EC600  expanded++ ; push n on closed bucket [(n.x+n.y)&3] (0x800ECE40, push-front)
800EC620  now = GetRCnt ; avg = (now - t0) / expanded
800EC648  if deadline < now + avg -> return 0                     ; would overrun: yield
          goto check
```

- **Work per frame:** expansions continue while the *projected* time of the next expansion (`now` plus
  the running average per expansion) stays inside 100 hblank counts of the slice start. There is no
  node-count cap; the cap is time.
- **Lifetime:** at most **200 worker calls** per request (the value at 0x800EBDC0). With a request at the
  head of the active list that is 200 consecutive frames.
- **Leak (READ, 0x800EC5E4..0x800EC504):** when 0x800EBFD4 returns 1 (pool empty), the node `n` that was
  just popped is in neither list, and 0x800EC6C0 only walks the lists, so **one pool node leaks per
  pool-exhaustion failure** until the next pool reset (0x800ED2FC: mode init/shutdown or the §2 unpause).
- The message object is two words on the stack (0x800ED630: `+0 := 0x8011415C`, `+4 := kind`; the
  vtable's only method 0x800ED770 returns +4). Delivery is `lh adj,0x140(vt); lw fn,0x144(vt);
  fn(person+adj, &msg)` (0x800EC4E0..0x800EC4F8, 0x800EC5D8..0x800EC68C) — the 8-byte "slot 40" of the
  person's vtable at person+0xC. What the person does with 1/2 is behaviour.md §2.10 (visitor 0x8008F880)
  and the staff handler 0x80097828; I did not re-read those.

## 5. Expansion: 0x800EBFD4(req, node) — READ (0x800EBFD4..0x800EC48C)

Returns 1 only when the node pool is empty at the moment a new node is needed (0x800EC3C0); else 0.

### 5.1 Setup
```
800EC00C  (x,y) = node pos (u8 +0x10/+0x11 -> s16)
800EC01C  T = Tile(x,y)
800EC028  mask = 0x55 if 0x8004D60C(T)            ; T.type==0 && !(T.flags&0x02)   (READ 0x8004D60C..0x8004D640)
800EC038         0x55 if 0x8004D5E8(T)            ; T.type==5                        (READ 0x8004D5E8..0x8004D608)
800EC048         else T.byte2                     ; the tile's own link bits         (0x800ED6C0 = lbu 2(a0))
800EC05C  perm = 0x80102510 + 16*rand(4)          ; rand(n) = 0x800C2648 = rand()%n
```
`0x80102510` rows (READ from the image): `[0,1,2,3] [3,2,1,0] [2,1,3,0] [1,3,0,2]`. So **neighbour
order is one of four fixed permutations picked at random per expansion** — the search is not
deterministic given the map; equal-f ties resolve differently run to run.

### 5.2 Per neighbour (i = 0..3, 0x800EC07C..0x800EC458)
```
dir = perm[i] ; (dx,dy) = overlay 0x8011417C[dir]  -> 0:(+1,0) 1:(0,+1) 2:(-1,0) 3:(0,-1)   (READ, ovl3)
nx = x+dx ; if nx < 0 || nx >= w  -> next        (0x800EC0BC, 0x800EC0CC)
ny = y+dy ; if ny < 0 || ny >= h  -> next        (0x800EC0F4, 0x800EC104)
cost = 1                                          (0x800EC10C)
N = Tile(nx,ny) ; t = N.type ; if t >= 15 -> next (0x800EC12C)
bit = u8[0x801036D0 + dir]                        -> 0x04, 0x10, 0x40, 0x01 (READ from the image)
jump overlay 0x8011419C[t]:
```
| N.type | handler | admitted when | cost |
|---|---|---|---|
| 0 grass | 0x800EC24C | `flagA&2` **and** `0x8004D388(N)` (§5.4) | **2** |
| 2 path, 13 path∧queue | 0x800EC174 → 0x800EC1A8 | `flagA&1` **and** `mask & bit` | 1 |
| 4 queue | 0x800EC194 → 0x800EC1A8 | `flagA&0x10` **and** `mask & bit` | 1 |
| 5 footprint | 0x800EC1DC | `flagA&8`, **or** N == `req+0x3C` (the destination tile) | **2** |
| 7 entrance | 0x800EC208 | `mask & bit` **and** N == destination tile | 1 |
| 8 exit tile | 0x800EC28C | always | 1 |
| 12 gate | 0x800EC154 | `flagA&1` (no link test) | 1 |
| 14 gate-side | 0x800EC278 | `flagA&0x20` | 1 |
| 1,3,6,9,10,11 | 0x800EC444 | never | — |

The link test (0x800EC1A8..0x800EC1D0 and 0x800EC208..0x800EC230) is **on the current tile's mask**, i.e.
"does T say it is joined toward N"; N's own bits are never consulted by the pathfinder. From grass or a
footprint tile the mask is 0x55 = all four orthogonal bits, so every direction passes.

Only bits **0x01, 0x02, 0x08, 0x10, 0x20** of flagA are ever tested (the six `andi`/`xori` at
0x800EC15C, 0x800EC17C, 0x800EC19C, 0x800EC1E4, 0x800EC254, 0x800EC280). Bits 0x04, 0x40, 0x80 are dead.

### 5.3 Admit (0x800EC28C..0x800EC43C)
```
h  = |nx - toX| + |ny - toY|                       (0x800EC28C..0x800EC2AC)
g  = parent.g + cost                               (0x800ED500 = lhu 0xC ; 0x800EC2C0)
f  = g + h                                         (0x800EC30C)
key = {s16 nx, s16 ny}                             (0x800EC2CC..0x800EC2FC)
c = closed[(nx+ny)&3].find(key)                    (0x800ED058 -> 0x800ED088 linear scan, compare 0x800ED600)
if c && !(f < c.f)  -> next                        (0x800EC310..0x800EC31C ; c.f = 0x800ED570 = g+h)
o = c ? 0 : open.find(key)                         (0x800EC33C..0x800EC348, linear scan of the whole open list)
if o && !(f < o.f)  -> next                        (0x800EC354..0x800EC360)
n = o ? unlink(o) : c ? unlink(c) : alloc          (0x800EC378..0x800EC3EC ; alloc fails -> return 1)
n.g = g ; n.h = h ; n.xy = (nx,ny) ; n.parent = node   (0x800EC3F0..0x800EC41C)
open.insert_sorted(n)                              (0x800ECED8, §6.2)
```
So: **A\* with re-opening of closed nodes on a strictly smaller f**, duplicate detection by coordinates,
and no per-tile "visited" field in the map (the map is read-only to the pathfinder — no store to a tile
anywhere in 0x800EBFD4..0x800EC48C).

### 5.4 The grass gate 0x8004D388(N) — READ (0x8004D388..0x8004D460)
```
A = N is type 2, 13, 7, or (type 0 with flag 0x02 clear)
if A  && !(N.flags & 0x10)                      -> 1
if (N.flags & 0x10) && (N.flags & 0x40)         -> 1
else: ((N.flags & 0x08) || N.type == 12) && IsOpen()   ; IsOpen = 0x800541AC = [0x80102D30] (parkopen.md)
```
For the pathfinder this only runs on `N.type == 0`, so: plain grass (flags 0x02 and 0x10 clear) is
walkable at cost 2; grass with 0x10 set needs 0x40; grass with 0x02 set (or 0x10 without 0x40) needs
flag 0x08 or… never type 12 here, plus the park being open. Meaning of tile flags 0x10/0x40/0x08 beyond
these tests: not established (paths.md §7 says the same).

### 5.5 Heuristic quality (arithmetic on the READ facts, not a guess about code)
h is Manhattan distance in tiles; every step moves exactly one tile and costs 1 or 2. So h ≤ true cost
(**admissible**) and |h(n)−h(n')| ≤ 1 ≤ cost (**consistent**). Given consistency the re-open branch at
0x800EC394 can never fire; it is defensive. Paths are cost-optimal over the admitted tiles; which optimal
path you get depends on the random permutation and the tie rule in §6.2.

## 6. Data structures — READ

### 6.1 Node (20 bytes) and the pool
| +0 | +4 | +8 | +0xC | +0xE | +0x10 | +0x11 | +0x12 |
|---|---|---|---|---|---|---|---|
| next | prev | parent ptr | u16 g | u16 h | u8 x | u8 y | pad |
(setters 0x800ED1CC/0x800ED1D4/0x800ED420/0x800ED430/0x800ED428/0x800ED54C; f = 0x800ED570.)
x,y are **bytes** — maps wider than 255 cannot be represented (READ `sb`/`lbu` at 0x800ED54C/0x800ED558).

**Pool at overlay 0x801141D8**: 2000 nodes × 20 = 0x9C40 bytes, then a **u16 free-index stack** of 2000
at +0x9C40 (0x8011DE18), then the **u16 free count** at +0xABE0 (0x8011EDB8). Reset 0x800ED2FC fills the
stack 0..1999 and sets count 2000 (`slti 0x7D0` at 0x800ED310, `addiu 0x7D0` at 0x800ED324). Alloc
0x800ED1DC: `count--; idx = stack[count]; return pool + idx*20`. Free 0x800ECDE8: `idx = (node-pool)/20`
(multiply-by-0x33333333 reciprocal, 0x800ECDEC..0x800ECE1C), `stack[count++] = idx`.
**The pool is shared by all 10 requests** — open + closed of every in-flight search draw from the same
2000. The pool ends at 0x8011EDBA and the request pool starts at 0x8011EDBC.

### 6.2 Open list (req+0x40): sorted doubly-linked list with sentinel
Insert 0x800ECED8(list, n): `d1 = |f(n) − f(first)|`, `d2 = |f(n) − f(last)|` (0x800ED438);
if `d1 < d2` walk **from the front** while `f(cur) < f(n)` and insert **before** the stop node
(0x800ECF28..0x800ECF5C: new node goes **ahead of equal-f nodes**); else walk **from the back** while
`f(n) < f(cur)` and insert **after** the stop node (0x800ECF64..0x800ECF90: new node goes **behind
equal-f nodes**). Pop = head (0x800ECE70). Both membership scans are linear (0x800ED088), so cost per
expansion is O(open + bucket) — this, not the algorithm, is what the hblank budget is protecting.

### 6.3 Closed set (req+0x54): 4 buckets by `(x+y)&3`
Each bucket is an unsorted list, push-front (0x800ECE40 → 0x800ED0F8 → insert after sentinel). Lookup is a
linear scan of one bucket comparing x then y (0x800ED600).

### 6.4 How big can open/closed get
Bounded only by the shared pool: **open + closed across all requests ≤ 2000 nodes**. A single search on a
large connected path network can hold most of that; when the count hits 0 mid-expansion the request
fails (§4) and one node leaks.

## 7. Output: the waypoint chain — READ

### 7.1 The waypoint pool
**1000 entries × 4 bytes at 0x800F7A38** (0x80093A1C init loop `slti 0x3E8`; 0x8009408C = `0x800F7A38 +
idx*4`). Entry layout, from the encoder 0x80093928 and decoder 0x800938C0:

| bits / bytes | meaning |
|---|---|
| u16 +0 bits 0..10 | **next index**, 0x7FF = end (0x800ED688 / 0x800940DC set, 0x80094114 get → −1) |
| u16 +0 bits 11..12 | x fraction, quarter tiles |
| u16 +0 bits 13..14 | y fraction, quarter tiles |
| u16 +0 bit 15 | allocated (0x800940C8 set, 0x800940B4 clear, 0x800940A0 = is-free) |
| s8 +2 | tile x |
| s8 +3 | tile y |

Encode (0x80093928): `x' = x + 0x20; tileX = x' >> 8; fracX = (x' >> 6) & 3` — positions are **rounded to
the nearest 64 units (quarter tile)**. Decode (0x800938C0): `x = (s8 tileX << 8) + fracX*64`. So a
destination given as `tile<<8 | 0x80` comes back exactly; a gate-lane point at `±0x180` offsets loses up
to 32 units. Tile x,y are sign-extended **bytes**.

Allocation 0x80093A70: first free at or after the hint `[gp+0xC24]` (= 0x80103278), sets the hint to
idx+1, decrements the free count `[gp+0xC20]`; returns **−1 when none is free**. Free 0x80093B04: clear
bit 15, count++, hint = min(hint, idx). Chain free 0x80093B54(idx): follows next until 0x7FF.

### 7.2 Building the chain: 0x800EC748(req, goalNode) — READ (0x800EC748..0x800EC8C0)
```
if person->wp (lh +0x28, 0x80093C5C) != -1: 0x80093C68(person)   ; free the person's old chain first
prevDir = 0xFFFF ; tail = -1 ; first = 1
for n = goalNode ; n ; n = n.parent:
    d = dir(n)                        ; 0x800ED478: 0 if no parent; else the step parent→n: +x→2, −x→6, +y→4, −y→0
    if d == prevDir: continue         ; collinear with the last emitted waypoint: skip
    i = 0x80093A70()                  ; if -1: 0x80093B54(tail) frees what was built, free goalNode, return 0
    w = 0x8009408C(i)
    if first: 0x80093928(w, req->rawToX, req->rawToY) ; first = 0     ; exact 8.8 destination
    else:     0x80093928(w, n.x<<8|0x80, n.y<<8|0x80)                 ; tile centre
    w.next = tail (0x800ED688) ; tail = i ; prevDir = d
free goalNode to the pool (0x800ECDE8) ; person->wp = tail (0x80093C9C = sh 0x28) ; return 1
```
Consequences that follow directly from the code:
- The chain is built goal→start, linked so the **person's head (+0x28) is the start end**; walking follows
  `next` toward the goal. The last waypoint is the raw destination, not its tile centre.
- **Only direction changes become waypoints** (plus the goal). A straight corridor of 30 tiles is 2 entries.
- **The start tile gets a waypoint unless the first leg heads −y (north)**: the start node has no parent
  so `dir = 0`, which is also the code for a −y step, so it is skipped exactly when the first step from it
  was −y. Every other first direction yields a waypoint at the walker's own tile centre first. (READ
  0x800ED4B4..0x800ED4EC: `a2 = parent.x − n.x`, `v0 = parent.y − n.y`; `bltz a2` → 2, else 6;
  `slti v0,1` <<2 → 4 for +y, 0 for −y; no parent → 0.)
- The goal node itself never enters the closed list; it is freed here (0x800EC844 / 0x800EC880).
- When from tile == to tile, the chain is one entry: the raw destination.

### 7.3 Who frees
- The walker itself: 0x80093C68(person) (free chain, +0x28 := −1) — 29 call sites by JAL scan, 28 of
  them in person state code (0x8008DF20..0x80098BB8) plus the pathfinder's own 0x800EC79C before it builds
  a replacement. The pathfinder never frees a delivered chain later.
- The unpause reset 0x80093A1C wipes the whole pool (§2) without touching any person's +0x28.

## 8. Failure — exactly when message 2 is sent (READ)

| condition | where |
|---|---|
| open list empty at the top of a slice or after an expansion — **unreachable** under the flags | 0x800EC654 → 0x800EC664 |
| worker entered with budget 0, i.e. after **200 slices** have been consumed (≈ 200 frames while at the head) | 0x800EC4BC → 0x800EC4D4 |
| **node pool empty** when a new neighbour needs a node | 0x800EBFD4 returns 1 → 0x800EC504..0x800EC518 |
| **waypoint pool empty** while building the chain | 0x800EC748 returns 0 → 0x800EC5C0..0x800EC5C8 |

Cases that produce **no message at all**: request refused at entry (10 requests outstanding, or node
count 0 — return 0 to the caller); every outstanding request when a build item is put down (§2, pool
rebuilt); the pathfinder paused by 0x800EBA2C (requests just sit, budget untouched).
"Queue full" therefore never yields message 2 — it is a synchronous refusal.

## 9. `flagA` and `flagB` at every call site — READ

`flagA` bits (from §5.2): **0x01** path/gate tiles (2, 13, 12) · **0x02** grass (0) at cost 2 via 0x8004D388 ·
**0x08** footprint (5) freely at cost 2 · **0x10** queue (4) · **0x20** gate-side (14). Regardless of flags:
exit tiles (8) always; entrance (7) and footprint (5) when they are the destination. `flagB` = request
field +0x34; only reader is the scheduler (0x800EC920): **1 = also service me in a frame that already
serviced a newer request**.

Every JAL to 0x800EC9F4 in the image (raw opcode scan, 25 sites) with the stores to `0x14(sp)`/`0x18(sp)`
that feed args 6 and 7; the register's last definition is quoted. State/purpose labels are behaviour.md's,
not re-derived here.

| call | function | behaviour.md | flagA | flagB |
|---|---|---|---|---|
| 0x8008D970 | 0x8008D764 | Idle → nearest bin, purpose 19 | 1 (0x8008D954) | **1** (0x8008D954) |
| 0x8008E4C4 | 0x8008E240 | state 6 → attraction entrance | 1 (0x8008E4A8) | 0 |
| 0x8008F70C | 0x8008F688 | state 41 join queue | 0x10 (0x8008F6F4) | 0 |
| 0x8008F81C | 0x8008F7BC | state 23 → ride entrance | 0x18 (0x8008F7FC) | **1** (0x8008F808) |
| 0x8008FA58 | 0x8008F880 | visitor msg handler: msg 2, purpose 14 → re-path to exit | 0x23 (0x8008FA40) | 0 |
| 0x80090D18 | 0x80090CC0 | state 36 spawn → gate | 1 (0x80090CF8) | 0 |
| 0x8009106C | 0x80090FF4 | state 38 leave → exit point | 0x21 (0x8009104C) | 0 |
| 0x80091170 | 0x80091100 | state 42 walk to lane slot | 1 (0x80091158) | 0 |
| 0x800915B0 | 0x8009154C | state 48 walk out | 1 (0x80091590) | 0 |
| 0x80091668 | 0x800915F4 | state 58 removed from queue → leave point | 0x11 (0x8009163C) | 0 |
| 0x80092AF4 | 0x80092844 | wander: random tile near centre | 3 (0x80092AD0) | 0 |
| 0x80094498 | 0x80094378 | staff 26 walk to strike | 3 (0x80094478) | 0 |
| 0x800949AC | 0x800947CC | staff 49 go and rest | 0x11 (0x80094960) | 0 |
| 0x80095204 | 0x80095104 | staff 13 patrol | 0x11 (0x800951E4) | 0 |
| 0x80096F24 | 0x80096E9C | mechanic 56 broken ride | 0x11 (0x80096EF8) | 0 |
| 0x80096FF8 | 0x80096F70 | mechanic 57 service | 0x11 (0x80096FCC) | 0 |
| 0x800970B8 | 0x80097044 | staff 58 leave point | 0x11 (0x8009708C) | 0 |
| 0x80097944 | 0x80097828 | staff msg handler: re-path to exit | 0x23 (0x8009792C) | 0 |
| 0x80097F3C | 0x80097D5C | guard 33 chase | 0x11 (0x80097F1C) | 0 |
| 0x80098000 | 0x80097F8C | guard 39 → exit point | 0x21 (0x80097FE0) | 0 |
| 0x8009818C | 0x8009805C | guard 55 post near entrance | 0x21 (0x8009816C) | 0 |
| 0x800986E4 | 0x80098680 | guard 48 random exit | 1 (0x800986C4) | 0 |
| 0x80098794 | 0x80098728 | guard 59 spawn point | 1 (0x80098774) | 0 |
| 0x80098EB4 | 0x80098D44 | cleaner seek litter | 0x11 (0x80098E94) | 0 |
| 0x80099124 | 0x80098F20 | cleaner seek bin | 0x11 (0x80099108) | 0 |

Reading the values: **1** = paths and gates only; **3** = paths + grass (wander, strike walk); **0x11** =
paths + queue tiles (staff, leaving a queue); **0x21** = paths + gate-side (exits); **0x23** = paths + grass
+ gate-side (the "get to the exit somehow" retry after a failure); **0x10** = queue tiles only (joining a
queue from its mouth); **0x18** = queue + footprint, **no path bit** (from the queue to the ride entrance,
across the ride's own footprint). Only the bin trip and the ride-entrance trip set flagB.

Where the person argument is visible in the window it is `object + 8` (`addiu a0, s0, 8`, e.g.
0x8008F7EC, 0x80090CE4, 0x8009103C), or 0 when the object is null (`move a0, zero` on the null branch);
the pathfinder dereferences it without a null check (0x800EC4D8).

## 10. What a faithful port must replicate (all READ above, listed for the implementer)
1. Ten request slots, newest-first service, synchronous refusal on the 11th.
2. Time slicing: cannot be reproduced exactly (hblank clock), but the observable effects — a request
   living across frames, the 200-call cap, older requests starving behind newer ones — can.
3. Expansion order = one of the four fixed permutations at 0x80102510 chosen by `rand()%4` per node.
4. Costs 1/2, Manhattan h, sorted open list with the two-ended tie rule in §6.2, 4-bucket closed set —
   the tie rule changes which of several equal-cost paths is returned.
5. Corner-only waypoints, quarter-tile quantisation, start-tile waypoint unless the first leg is −y,
   final waypoint = raw destination.
6. The four message-2 causes in §8 and the two silent-drop cases (§2 unpause, §3 pause).

## 11. Not established
- What the person does on receipt of message 1/2 beyond behaviour.md §2.10 (not re-read here).
- The meaning of tile flags 0x08/0x10/0x40 that 0x8004D388 tests, and of `[0x80102D20]` at the slot-4 gate
  (GUESS-medium: the game-pause flag).
- Whether RCnt1 is ever re-programmed after 0x800D4158 (only that one SetRCnt on counter 1 was found by
  JAL scan of 0x800D430C; the other, at 0x800BC328, is counter 2).
- Whether any code cancels a request when its person despawns mid-search. No function in the block walks
  the active list comparing +8 against a person; the only active-list removal is 0x800ECC04 from the
  scheduler. GUESS-medium: a despawned person's request completes and calls through a freed vtable.
- The absolute frame budget in wall time: 100 hblanks is exact, "≈ 6.4 ms" assumes 312 lines at 50 Hz.
- The ordering of the six person lists in the pause/unpause walks (0x800536B4, 0x80053720, 0x800536D8,
  0x80053768, 0x800536FC, 0x80053744) — irrelevant to the pathfinder, listed only for completeness.

## ⚠ THE OLDEST SEARCHES NEVER RUN — measured 2026-09-20

Measured on the standard test park (`--park=203 --park-open --park-guests=25`, two attractions, a
queue, four path runs), reported at frame 3000 by the new `states:` line:

```
                        no bin placed        bin placed        bin + 3 cleaners
guests                  29                   31                31
decisions made          7600                 4610              4837
routes FAILED           7545 (99.3%)         4574 (99.2%)      4796 (99.2%)
searches outstanding    4 / 10               7 / 10            7 / 10
stuck in state 11       4, oldest 7389 tk    7, oldest 4458    7, oldest 4642
nodes held              4 of 2000            7 of 2000         7 of 2000
```

⭐ **One node held per stuck request is the tell.** A seeded search holds exactly one node — the
seed — so "7 outstanding, 7 nodes used" says those seven were accepted and **never stepped once**.
They are not searching slowly; they are not searching at all.

### The mechanism, and it is FAITHFUL

`Request` inserts at the **head** of the active list, and `RunFrame` services the head and then only
requests whose `flagB == 1`. So a steady arrival of new requests keeps jumping the queue in front of
the old ones, which never get a slice, never answer, and never release their slot. With ten slots
total, the park then refuses **99% of all route requests** — which makes guests re-decide, which
produces more new requests, which starves the old ones harder.

⚠ **THIS IS NOT A PORT BUG, AND DO NOT "FIX" IT BY MAKING THE LIST FIFO.** Verified by hand:

| address | what it does |
|---|---|
| `0x800ECB90` | takes the free-list head at pool+4, bumps the count at +12, then calls the insert below with `a1 = pool+8`, the ACTIVE list |
| `0x800ED28C` | `lw a1,0(s1)` old head → `new->next = old head` → `new->prev = 0` → `old head->prev = new` → `sw s0,0(s1)`: **head = new**. Unambiguous insert-at-head. |
| `0x800EC8C4` | the scheduler: reads the pause flag at `0x801036C8`, takes a time budget of `now + 0x64`, walks from the head, and after the first `Work` requires `0x800ED378` (the flagB test) before running any other |

So newest-first scheduling and one-non-flagB-per-frame are both the original's.

⭐ **The port's frame budget is the one real difference, and it is the wrong SHAPE.** The original
spends a **wall-clock budget** (`0x800D40F0` twice, `sltu` against `now + 100`) and keeps walking the
list until the time is gone. The port counts EXPANSIONS (`_expansionsThisFrame >= ExpansionsPerSlice`)
and returns. On hardware a cheap frame therefore services more of the list than an expensive one; in
the port every frame services the same fixed amount regardless. That does not by itself change who
starves, but it is a divergence worth recording before anyone tunes this.

### What actually triggers it

A request for an **unreachable** target burns its whole 200-slice budget before failing, so it holds
the head for many frames while everything behind it waits. The two known sources of unreachable
targets in this park are a queue tile sealed inside a ride's footprint (cow tools, same day) and a
litter bin placed off the path. Remove the bin and the stuck count falls from 7 to 4 — it adds to the
problem without being the cause.

### Not established

Whether the console reaches this state at all. Everything above says the scheduling is identical, so
the honest reading is that the original is equally vulnerable and simply never accumulates enough
unreachable targets to show it. **Do not conclude the port is uniquely broken here** — and do not
conclude it is fine, either. The next step is a console trace of the outstanding-request count on a
park with a sealed tile, which the emulator rig in `tools/oracle/` can take.
