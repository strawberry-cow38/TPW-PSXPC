# Where a rider is while the ride runs — PAL SLES-026.88

2026-09-20. **READ** = taken from the instructions at the quoted address in `TPW.BIN` (loaded at
0x80010000) or from bytes of `FOLIO.GAZ`; **GUESS-high/medium/low** = interpretation. `A = outer+8`;
offsets are marked A-, outer-, P- (Person = guest outer+8), H- (the FOLIO handle at A+0x18 =
outer+0x20), D- (the 88-byte runtime model descriptor) or record- (the definition record from the
FOLIO entry) relative. Disc numbers come from the local `folio.gaz` via the parser in
`tools/ride_phases.py`; nothing here is a live-RAM measurement.

## 0. Answer, and two corrections to `rides.md`

**READ. No status hook and no tick writes a rider's position.** The twelve `SetStatus` enter hooks
(§1) and the running tick `0x8009CA60` touch phase, timer, cycle count, wear, sound and status only.
The riders list `A+0xD4` is walked by exactly one routine outside load/unload/eject: the
NonPathedRide **draw** virtual, **slot 4 = `0x8009FC48`** (vtable 0x800E5514+0x24), called for every
object on the world draw list by the object pass `0x80057AF0` (§5). It pairs rider *i* (boarding
order) with **seat *i* of the model**, and a seat is a **bone**: the model's trailing u32 list is a
list of bone indices, and the rider takes the posed 32-byte GTE matrix of that bone from the
animator's workspace (§3). The ride's world matrix `H+0x0C` is composed on top.

What then happens is chosen by **one byte of the definition record, `record+0xBC`** (§4):

| `record+0xBC` | flat rides | rider position field written? | how the rider is drawn |
|---|---|---|---|
| **0** | 54 of 59 | **No.** Nothing on the guest changes. | `0x80035050` draws the guest sprite directly in camera space at `Cam × Mride × Bone`; the sprite frame's facing octant and elevation band are derived from that composed rotation, so riders turn with the car (§2.4) |
| **≠ 0** | Jelly Bounce 54, Brain Buster 121, Belly Bounce 209, Bounce on Iggy 360, Zero G 379 (all = 1) | **x/z only:** `P+0x18/P+0x1A := low16( Mride.R × (bone.tx, 0, bone.tz) + Mride.T )` via `0x80093FC0`; animation id **13** forced into `P+0x2C` bits 19–23 | `Person::Draw` (`0x800935C0`) with **y = A+0x5A + bone.ty + 0x80** passed as a draw-time argument (never stored), facing = the guest's own `P+0x2E & 7` (§2.5) |

So for the common arm the honest answer to "where is the rider's position written every tick" is
**nowhere** — it is recomputed at draw time from the current animation frame and discarded. A port
that wants rider positions as state must compute them at the same point, from the same bone.

**The loop stops at the seat-list length, not at MaxSeats and not at the rider count** (`sltu` at
`0x8009FDD8` against `D+0x3A`). A rider beyond the last seat is neither positioned nor drawn. On the
disc (§3.2) eight flat rides have an **empty** seat list — their riders are invisible while aboard —
and twenty-nine more seat more guests at some upgrade level than they have seats (37 of 59 in all).

Corrections to `rides.md` "Where a ride keeps its SEATS":
1. Seats are per-bone, but **the bones are named by the model, not inferred**: the trailing list at
   the end of the mesh (folio.md §3.2 "u32 list[hdr+0x28]") is the seat list. On Crazy Ape (220) it
   names bones **9, 5, 8, 7, 6, 16, 12, 13, 14, 15** — ten of the eleven skinless bones; bone 10 is
   not a seat. "Skinless bones = seats" was wrong because of that eleventh bone and because seat
   lists are shorter than the seat *count* on 37 rides (29 with a non-empty list).
2. `rides.md` §4.2's "g.slot3/slot4 (hide, attach)" at boarding: **READ**, Visitor slot 3 is the
   Update dispatch `0x800916B4` and slot 4 is Draw `0x80091D54 → 0x800937D0`. LoadGuests runs them
   once so the state-21 entry (`0x8008E538`, clears `P+0x2B` bit 0x01) executes at once and the
   guest's drawable is released (§5). Nothing "attaches" the guest to the ride.

## 1. `SetStatus` and the twelve enter hooks

**READ.** `0x80065768(A, n)`: `sb n, 0x6E(A)`; `sltiu 12` guard; `jr` through the 12-word table at
`0x800E1340`. Each arm loads a vtable entry pair `lh adjust, +0x1D0+8n(vt); lw fn, +0x1D4+8n(vt)` and
tail-calls `fn(A + adjust)` at `0x800658BC`. So arm *n* = **slot 58+n**. The tick hooks are the
parallel table `0x800E1370` in base Update `0x80065BE0` (slot 69+n; status 0 falls through to the
animation step at `0x80065D2C`).

| status | table word | vt offsets | NonPathedRide handler (0x800E5514) | what it does (READ) |
|---|---|---|---|---|
| 0 | 0x800657A4 | +0x1D0/1D4 | 0x80065688 (base) | `A+0x64 := 1` (phase), `A+0x60 := 0` (timer) |
| 1 | 0x800657BC | +0x1D8/1DC | 0x80065698 (base) | `A+0x64 := 0`, `A+0x60 := 0`, `0x80066380(H, 0)` frame 0 |
| 2 | 0x800657D4 | +0x1E0/1E4 | 0x8009C708 (ride) | base `0x800656C4` (`A+0x64 := 1`, `A+0x60 := 0`), then `A+0xF2 := 0` cycles |
| 3 | 0x800657EC | +0x1E8/1EC | 0x800656D4 (base) | `A+0x64 := 1` |
| 4 | 0x80065804 | +0x1F0/1F4 | 0x800A0A14 (NPR, adj −8) | `0x8009C730(A)` (shared "about to break down"), then slot 84 lifetime `≤ 0` → `0x8009D8C4(A, 0)` eject queue and riders |
| 5 | 0x8006581C | +0x1F8/1FC | 0x800A09C0 (NPR, adj −8) | `0x8009C7A8(A)` (shared "broken down"), then `0x8009D8C4(A, 0)` eject |
| 6 | 0x80065834 | +0x200/204 | 0x800656F8 (base) | `A+0x64 := 1` |
| 7 | 0x8006584C | +0x208/20C | 0x8009C828 (ride) | base `0x80065704` = `SetStatus(2)`; `0x8009C308(A, &A+0xE4)` drop smoke, `A+0xE4 := 0`; `A+0xB4 := 0x64000` (100.0); `SetStatus(10)` |
| 8 | 0x80065864 | +0x210/214 | 0x80065738 (base) | `A+0x64 := 1` |
| 9 | 0x8006587C | +0x218/21C | 0x80065744 (base) | `A+0x64 := 1` |
| 10 | 0x80065894 | +0x220/224 | 0x80065750 (base) | `A+0x64 := 1`, `A+0x60 := 0` |
| 11 | 0x800658AC | +0x228/22C | 0x80065760 (base) | `jr ra` — nothing |

None reads `A+0xD4` except through the eject `0x8009D8C4`, which places riders at the exit
(rides.md §3 Demolish) — that is a leave, not a ride position. The running tick `0x8009CA60`
(slot 71) re-read for this report: wear slot 103 (`+0x338`), `0x800660C8(A) & 15` and
`0x800BDD18() & 3` sound gate, `0x800658D8` clock → `A+0xF2++`, `A+0xF2 >= A+0xC0 → SetStatus(11)`.
No rider access. The tick-10/tick-11 hooks (`0x800A0178`, `0x8009CB5C`) only append/pop the list.

## 2. The draw virtual `0x8009FC48`, top to bottom

Registers: `s5 = outer`, `s2 = A`, `s7 = H = outer+0x20`, `s0/s1/s6/s3` as below. All READ.

### 2.1 Setup (`0x8009FC48..0x8009FDC4`)
1. `0x8009CD94(A)` — the queued-ride parent draw (vtable 0x800E519C+0x24): `0x8009D1E4` calls
   **slot 4 (Draw) on every queue member** (`A+0xC4` list), `0x8009E3CC` if `A+0xEC > 0` (closing
   progress), then base `0x80065DE4` (the ride model; its position is `A+0x58<<8, A+0x5A, A+0x5C<<8` at
   `0x80065E34..E58` — x/z shifted, y not).
2. `count = 0x800A0C74(H)` → `0x800305F8(H, [H+0x34])` → `lhu D+0x3A` — the **seat-list length**
   (animation-phases.md: "trailing-list count, low half of M+0x28"). Stored at `sp+0x80`.
3. `fp = 0x800A0B10( slot7(A) )`. Slot 7 = `0x80062A20 → 0x80031114(H) → 0x800307DC`: locks the
   FOLIO entry and returns **the definition record** (`entry + entry[0x14]`, rides.md §1.3).
   `0x800A0B10` = `lbu 0xBC(record) != 0`. Slot 6 (`0x80062A40`) releases it.
4. `Mride = 0x80031138(H) = H + 0x0C`, the ride's world matrix (32-byte GTE MATRIX).
   - `fp != 0`: `SetRotMatrix(Mride)`, `SetTransMatrix(Mride)` (`0x800C4C24/0x800C4C04`, PsyQ).
   - `fp == 0`: `Mcam = CompMatrix( [0x8010388C]+8+0x60, Mride )` into `sp+0x18` (`0x800C0750`
     PsyQ `CompMatrix`; the first operand is a matrix in the object at `gp+0x1238`). **GUESS-high:**
     that object is the camera and `+0x68` its view matrix — `0x8002B31C` fills it from `+0x48` with a
     6553/4096 scale on the first row and on ty, and the ride-cam virtual (§6) writes the same field.
5. `list = outer+0xDC` (= `A+0xD4`); `s1 = head−8` via `0x800A0ACC` (`[list]`), `s6 = list−8` via
   `0x800A0AC4` (the list object is its own end sentinel). `0x800A0C50(H, sp+0x38)` →
   `0x80030668(H, buf, [H+0x34])`: locks the container and stores `buf[0] = D`, `buf[1] = M`
   (the mesh, via `0x800308C4`). `0x8006329C(A)` (rotation byte) is called and **its result
   discarded**.

### 2.2 The loop (`0x8009FDD0..0x800A0048`)
```
i = 0; rider = head
while rider != end and i < count:                # sltu i, count  (0x8009FDD8)
    B = 0x80030734(buf, i, sp+0x40)              # seat i's bone matrix, §3
    if fp == 0: arm A (2.4) else: arm B (2.5)
    rider = next(rider) (0x800A0AEC = [P+0]) ; i += 1
0x800306E8(H)                                    # release the container
```
The riders list is a circular intrusive list threaded through `P+0` (next); `rider outer = node − 8`.
Seat assignment is therefore **positional**: after a partial unload the survivors move up.

### 2.3 What `B` is
`0x80030734(buf, i, out)` → `0x8002E8DC(D, M, i, out)`:
```
W     = lock(D+0x54)                 # 0x800C0DB8 — the animator workspace ("ARS")
bone  = u32 [ M + D[0x18] + 4*i ]    # the trailing list; D+0x18 = its offset from the mesh base
out   = 32 bytes at W + D[0x24] + 32*bone     # D+0x24 = bone-matrix array (animation-phases.md)
```
`out` is a PSX MATRIX: `s16 R[3][3]` at +0..+0x11, `s32 t[3]` at +0x14/+0x18/+0x1C. It is the
per-frame composed matrix that `0x8002CBC4` writes for every bone (folio.md §3.4). **GUESS-high**
that it is already in model space (parent chain applied): the draw applies no parent walk, and the
ride-cam (§6) uses the same table the same way.

### 2.4 Arm A — `record+0xBC == 0` — draw in place, write nothing
`0x8009FEC4..0x800A0014`:
```
R  = Mcam.R × B.R              # three MVMVA (0x4A49E012: sf=1, mx=RT, v=IR, cv=none), one per column,
                               #   RT = Mcam.R loaded by ctc2 from sp+0x18; result columns to sp+0x60
T  = Mcam.R × (s16 B.tx, s16 B.ty, s16 B.tz) + Mcam.T     # 0x4A480012: v=V0, cv=TR; MAC1..3 → sp+0x74..7C
type = lbu outer+0x61 of the rider     # 0x800926A0 — visitor type byte (behaviour.md V+0x61)
0x80035050(&{R,T}, zero8 at sp+0x10, type)
```
`0x80035050` (READ): `0x800BFBDC(M)` decomposes the rotation into three angles (`ratan2`
`0x800C9340` on R31/R33 and R12/R22, and `0x800BFD58` on −R32; a gimbal branch when
`R22²+R12² < 26`); **GUESS-high** these are yaw / roll / pitch in the usual PSX sense. Then
`facing = ((yaw+256) >> 9) & 7` (negatives rounded via +767), `band = max(2, (pitch+256) >> 9)` (band
forced to 2 also zeroes facing), sprite id = `s16 0x800E0140[17*type + facing + 8*band]` (or, when
`[gp+0x3A0] != 0`, `0x800F1EEC[type % [gp+0x39C]]`), sprite = `0x800BDCD4(id)`,
`SetRotMatrix/SetTransMatrix({R,T})`, `0x800348F4(zero8, sprite, facing > 4, roll)` (the third argument
is the X-mirror flag — `slti` on the unmasked facing, corrected in §8 — not a band test). That is a
**billboard picked by the composed orientation**; no Person field is read or written.

### 2.5 Arm B — `record+0xBC != 0` — write x/z, draw with a y override
`0x8009FDFC..0x8009FEBC`, with the GTE holding `Mride`:
```
rider.slot45()                       # 0x80091D80: (re)acquire the drawable at P+0x1C if released
V0 = (s16 B.tx, 0, s16 B.tz)         # sp+0x68/0x6C, y zeroed by the memset at 0x8009FE20
(wx, wy, wz) = Mride.R × V0 + Mride.T          # 0x4A480012; MAC1/2/3 → sp+0x70/0x74/0x78
0x80093FC0(P, wx, wz)                # sh P+0x18 := wx ; sh P+0x1A := wz  — the ONLY position write
0x800935C0(P, y = (s16 A+0x5A) + B.ty + 0x80)  # lh outer+0x62 ; lw sp+0x58 ; addiu 128
```
`wy` (MAC2) is stored to `sp+0x74` and never read. `B.R` is not used at all in this arm.
`0x800935C0` = `Person::Draw(P, yOverride)` (READ): returns if `P+0x1C == 0`; takes x/z from
`P+0x18/0x1A`; if `yOverride != 0` uses it and sets `P+0x2C := (P+0x2C & 0xFF07FFFF) | 0x00680000`
(bits 19–23 = **animation id 13**, the field behaviour.md lists as `P+0x2E` bits 3–7), else y =
terrain `0x80050938(x, z, 0)`; a screen-rect cull via slot 10 and `0x80053F40` (off-screen: release the
drawable, `P+0x1C := 0`); facing `0x800323A0(model, P+0x2E & 7)`, animation `0x800323A8(model,
(P+0x2C>>19)&31)`; skips when `P+0x2B & 0x40`; `0x80034DDC(&pos + camera offsets gp+0xC18/C1A/C1C,
sprite, 2048)`. behaviour.md calls anim 13 "wander" (GUESS-medium there); here it is simply the id
the ride forces.

### 2.6 Units (READ where the arithmetic is quoted; the naming is GUESS-high)
`P+0x18/0x1A` are halfwords in the guest position unit — 256 per tile (queue spacing 0x40 = ¼ tile,
rides.md §4.1; the base draw shifts `A+0x58` left 8, §2.1). Arm B adds a 4.12 rotation of `B.t`
straight onto `Mride.T` and stores the low halfword, so the game treats **bone translation units as
the same 256-per-tile unit**; Crazy Ape's seats at x = ±76, z −229..191 (rides.md) are ±0.30 and
−0.89..+0.75 tile from the model origin. Y: `A+0x5A` is the unshifted height, `B.ty` in the same unit,
`+0x80` = half a tile. `Mride.T`'s writer was not read; **GUESS-high** it equals the base draw's
`(A+0x58<<8, A+0x5A, A+0x5C<<8)`.

## 3. Seat data on the disc

### 3.1 Layout (READ from `0x8002E8DC` and folio.md §3.2)
The seat list is the `u32 list[hdr+0x28]` that ends every mesh sub-entry, immediately before the
next sub-entry (or the definition record). Its length is copied to `D+0x3A` at load (`0x8002C608`).
Each entry is a bone index. Reading it as *the last 4·n bytes of the sub-entry* gives an index below
the bone count for **every one of the 83 ride containers** (script `seats.py` in the scratchpad,
derived from `tools/ride_phases.py`), which is the check that the placement is right.

### 3.2 All 59 flat rides (type 3) — mesh 0
"seat slots" = the list length = the loop bound. Seats per level are `record+0x24+0x34L+0x0C`.
`+0xBC` is the arm selector of §4. **bold yes** = at least one upgrade level seats more guests than
there are slots; those extra riders are never drawn.

| entry | name | seats L0/L1/L2 | bones | seat slots | +0xBC | slots < L2 seats |
|---|---|---|---|---|---|---|
| 39 | The Dizzy Tree | 4/8/12 | 55 | 12 | 0 | no |
| 40 | Bumper Bugs | 2/4/6 | 14 | 4 | 0 | **yes** |
| 42 | Caterpillar Capers | 4/6/8 | 9 | 3 | 0 | **yes** |
| 43 | Bugs TV | 4/8/12 | 17 | 15 | 0 | no |
| 45 | Woodland Racers | 2/4/6 | 15 | 8 | 0 | no |
| 48 | Dragon Fliers | 2/3/4 | 23 | 3 | 0 | **yes** |
| 49 | Flamingo Fling | 2/4/6 | 13 | 8 | 0 | no |
| 50 | Flower Power | 4/6/8 | 23 | 10 | 0 | no |
| 51 | Flying Fishes | 4/5/6 | 16 | 6 | 0 | no |
| 52 | Flying Fountain | 4/6/8 | 14 | 6 | 0 | **yes** |
| 53 | Spore Spinner | 8/10/12 | 27 | 12 | 0 | no |
| 54 | Jelly Bounce | 3/4/5 | 6 | 4 | **1** | **yes** |
| 55 | Escargot A-Go Go | 1/1/1 | 6 | **0** | 0 | **yes** |
| 121 | Brain Buster | 5/7/9 | 6 | 4 | **1** | **yes** |
| 122 | Insecticide | 10/14/18 | 42 | 12 | 0 | **yes** |
| 123 | Pumpkin Castle | 4/6/8 | 12 | 5 | 0 | **yes** |
| 124 | Hocus Pocus | 20/26/32 | 50 | 36 | 0 | no |
| 128 | Jaw Dropper | 5/8/11 | 23 | 6 | 0 | **yes** |
| 129 | Thrill Grill | 18/24/30 | 38 | 14 | 0 | **yes** |
| 131 | Tentacle Terror | 12/17/22 | 15 | 4 | 0 | **yes** |
| 132 | Devils Disc | 8/10/12 | 16 | 8 | 0 | **yes** |
| 133 | Crazy Clown | 4/5/6 | 27 | 7 | 0 | no |
| 134 | Jumping Skulls | 4/5/6 | 26 | 8 | 0 | no |
| 135 | Tower-unconfirmed | 4/5/6 | 0 | **0** | 0 | **yes** |
| 136 | Phantom | 8/12/17 | 2 | **0** | 0 | **yes** |
| 137 | Putrid Pumpkins | 6/9/12 | 23 | 12 | 0 | no |
| 138 | Rat Race | 5/7/9 | 21 | 3 | 0 | **yes** |
| 139 | Ghost Ship | 12/16/20 | 22 | 18 | 0 | **yes** |
| 140 | Eye Slide | 4/5/6 | 5 | **0** | 0 | **yes** |
| 141 | Spooky Spider | 12/16/20 | 48 | 8 | 0 | **yes** |
| 209 | Belly Bounce | 5/7/9 | 19 | 4 | **1** | **yes** |
| 210 | The Hot Pot | 4/6/8 | 18 | 4 | 0 | **yes** |
| 211 | Aztec Bounce | 4/5/6 | 21 | 6 | 0 | no |
| 214 | Diplo-Dip | 4/5/6 | 11 | 1 | 0 | **yes** |
| 216 | Sun God | 16/22/28 | 45 | 32 | 0 | no |
| 217 | Inca Pot | 4/5/6 | 14 | 7 | 0 | no |
| 218 | Mayan Spinner | 6/9/12 | 24 | 14 | 0 | no |
| 220 | Crazy Ape | 8/11/14 | 19 | 10 | 0 | **yes** |
| 221 | Mumbo | 5/5/5 | 38 | 5 | 0 | no |
| 222 | Rocky Racers | 4/6/8 | 11 | 4 | 0 | **yes** |
| 223 | Slither | 1/1/1 | 1 | **0** | 0 | **yes** |
| 224 | Tom Tom Twister | 20/30/40 | 61 | 40 | 0 | no |
| 225 | Inca Totem | 6/8/10 | 16 | 6 | 0 | **yes** |
| 226 | Aztec Mayhem | 5/8/12 | 14 | 9 | 0 | **yes** |
| 227 | Eruption | 8/11/14 | 15 | 12 | 0 | **yes** |
| 360 | Bounce on Iggy | 1/1/1 | 28 | 4 | **1** | no |
| 361 | Uforia | 4/6/8 | 20 | **0** | 0 | **yes** |
| 362 | Crater Creature | 12/18/24 | 21 | 16 | 0 | **yes** |
| 364 | Hover Bot Havoc | 6/8/12 | 26 | 12 | 0 | no |
| 365 | Romper Stomper | 11/15/19 | 13 | **0** | 0 | **yes** |
| 366 | Moon Buggies | 4/6/8 | 15 | 7 | 0 | **yes** |
| 369 | The Orbiter | 10/16/22 | 39 | 27 | 0 | no |
| 370 | Missile Madness | 6/8/12 | 23 | 8 | 0 | **yes** |
| 371 | The Gravatron | 10/16/22 | 37 | 8 | 0 | **yes** |
| 374 | Space Balls | 1/1/1 | 4 | 1 | 0 | no |
| 375 | The Areotron | 18/24/30 | 28 | 20 | 0 | **yes** |
| 376 | Cyclone Station | 15/20/25 | 14 | 10 | 0 | **yes** |
| 377 | Rock 'n Roll | 10/14/14 | 4 | **0** | 0 | **yes** |
| 379 | Zero G | 4/6/8 | 17 | 13 | **1** | no |

Seat lists worth keeping as examples (bone indices, in seat order): Crazy Ape 220 →
`9, 5, 8, 7, 6, 16, 12, 13, 14, 15`; The Dizzy Tree 39 → `23, 21, 25, 30, 34, 32, 13, 17, 15, 5, 9, 7`;
Eruption 227 → `13, 12, 7, 6, 5, 4, 11, 10, 9, 8, 2, 3`. The order is the boarding order.

Consequences (READ code + disc data): a level-0 Crazy Ape shows all 8 riders; upgraded to 11 or 14
seats it still shows **10**. Phantom, Uforia, Romper Stomper, Rock 'n Roll, Escargot A-Go Go,
Slither, Eye Slide and the unconfirmed tower show **no riders at all** — the guests are aboard
(counted, wearing the ride, unloaded later) but invisible.

Not in this table: tour (7), track (6) and coaster (1) containers. Their mesh 0 carries a seat list
too (e.g. Bumble Buggies 16, tours 1) but their vehicles are separate objects and their rider draws
were not traced (§6); do not read those counts as seats.

## 4. The selector byte `record+0xBC`

**READ:** it sits at `0x24 + 2·0x34 + 0x30` — the thirteenth, undocumented word of the level-2 block
(`rides.json` lists the block as twelve fields; the stride is 0x34). Only the low byte is tested.
On the disc the bytes at `+0xBC..+0xBF` are `{0|1, 0, 0x40, 0x59}` on every flat ride, `{0..4, 0,
1|2, 0}` on coasters, `{0|1, 0, 0, 0}` on tours/track rides — so this is a byte field, not part of a
32-bit price. Values for flat rides: **1 on Jelly Bounce, Brain Buster, Belly Bounce, Bounce on Iggy,
Zero G; 0 on the other 54.** Tour/track: 1 on Tweety Tours, Jurassic Tours, Ooze Crooz, Splish
Splash. Coasters: Big Dripper 3, Candy Coaster 3, Hades 2, Scatty Batty 3, Ghosta Coasta 2, Temple
of Gloom 3, Escape Velocity 3, The Shocker 4, the other four 0. What the non-zero values mean for
those classes is **not established**; only the flat-ride test (`!= 0`) was read.

**GUESS-medium** on meaning: the five flat rides are the trampoline/free-fall rides, where the guest
must keep their own sprite and animation (id 13, forced) and only follow the seat's position — hence
the write arm. Everywhere else the guest is a passenger drawn in the car's orientation.

⚠ **Runtime authority.** Every `sb` to `+0xBC..+0xBF` in the image (`0x80016770`, `0x800168E8`,
`0x800A4A7C`, `0x800A4F58`, `0x800A506C`, `0x800A50C8`, `0x800A50D8`) is on another object type —
`0x80016870` counts a u16 at `+0xB8` modulo 288 on an object holding handles at `+0xC0/+0xC8`; the
`0x800A4…/0x800A5…` sites are PathedRide objects. No writer with a record-pointer base was found, so
the disc byte is what the code sees (**GUESS-high**: bases were identified from their neighbours,
not traced to allocation). `0x800A0B10` has exactly one caller, `0x8009FCE4`.

## 5. Why the normal people pass does not draw a rider (READ)

The world draw pass `0x80057AF0` (called from `0x80057E0C`; per-frame or per-tick cadence not
established here — README notes one sim tick per two PAL frames) walks the object list at
`0x8010971C`, sets `SetRotMatrix/SetTransMatrix([gp+0x126C])` and calls **slot 4** on each object.
Rides are on it (`0x80062894`'s `0x800628A8` registers at placement); guests are on it too and
boarding does not remove them (`0x80053C48`'s callers are the person-manager removal paths
`0x800510F0..0x80051D50`, coaster `0x800ACC0C/14` and `0x800AFE58` only). A rider's own Draw
(`0x800937D0`) tests `P+0x2B & 0x01` (`0x80093EF0`); the state-21 entry `0x8008E538` clears that bit
(`0x80093EFC(P,0) → 0x80094050` clear) and LoadGuests forces one Update+Draw right after
`SetState(21)` (`0x8009C960..0x8009C994`), so the guest's drawable is released and it is not drawn
by the pass — the ride draws it. Unload's `0x80053C04` re-appends to the list; `0x8005CC08` unlinks
before linking (`0x8005CC7C` first), so the re-append is harmless.

## 6. Other classes and the ride-cam (sites only, not traced)

The same seat mechanism (`0x80030734` bone fetch + `0x80035050` sprite draw) is used at:
tour `0x800A15A4`, `0x800A3014`/`0x800A3188`; track `0x800A566C`, `0x800A5744`, `0x800AB4F4`/`0x800AB668`,
`0x800AB694`/`0x800AB7E0`, `0x800AC780`/`0x800AC8F4`; coaster `0x800B2C00`/`0x800B2D74`. Their slot-4
overrides are `0x800A21E0`, `0x800A6BF8`, `0x800AD7D4` (each starts with the parent `0x8009CD94`). No
class writes rider x/z outside NonPathedRide's arm B: the other `0x80093FC0` callers are unload/exit
placements (`0x8009CC70`, `0x8009DF88`, `0x8009E0C4`, `0x800A18E4`, `0x800A2560`, `0x800A8CF0`) and
spawn/load code.

NonPathedRide **slot 87 = `0x800A0770`** (base vtable has none; Track's is `jr ra`) is the ride-cam:
it takes bone `[mesh+0x2C]` from the same matrix table, applies the axis swap
`(x, y, z) → (x, −z, −y)` to the bone rotation, composes with `Mride`, and inverts the result
(`TransposeMatrix`, `ApplyMatrixLV`, negate — `0x800BFCF4`) into `(a1+8)+0x40`; its only caller is
`0x80055210` (from `0x80054D14`), which then asks the ride's type and treats TourRide specially.
Mentioned because it is the other consumer of the bone table and because "rider position" searches
land on it first.

## 7. Not established

- The cadence of the draw pass relative to the sim tick, and therefore whether arm B's x/z write
  happens 25 or 50 times a second.
- That `[0x8010388C]+0x68` is the camera view matrix (GUESS-high, §2.1) and that `Mride.T` is
  `(A+0x58<<8, A+0x5A, A+0x5C<<8)` (GUESS-high, §2.6); neither writer was read.
- Whether the bone matrices in `W + D[0x24]` are parent-composed (GUESS-high, §2.3).
- The meaning of `record+0xBC` values 2–4 on coasters and 1 on tours/track rides.
- ~~The sprite-frame table `0x800E0140` layout beyond the index formula; `0x800348F4`'s arguments.~~
  Answered in §8: the sheet, the layout, what the sprites are, and the draw's arguments.
- Anything about live behaviour: no emulator run was made for this report.

## 8. Which sheet the rider sprites come from — and what they are (READ; pictures MEASURED)

**Correction to §2.4 above**: `0x800348F4`'s third argument is `4 < facing` (the *unmasked* quantised
yaw), which the draw uses to **mirror the quad** (`local_2c = −local_2c`); it is not a band test.

### 8.1 The sheet is FOLIO entry 416, and the indices are absolute (READ)
`0x80035050` turns the table entry into a sprite with `0x800BDCD4(id) = [0x80103A88] + 12·id`.
`[0x80103A88]` is written once, in `0x800BDC60`: `0x800BE268(&0x80103A80, 0x1A0)` then
`[0x80103A88] = 0x80028DD4(&0x80103A80)` (= slot+0x1C, the sprite record table). `0x800BE268` →
`0x800BE288(slot, 0x1A0)` → `0x800BE2B4`: find or take a slot in the ten-entry sheet table at
`0x800F07C8` (32 bytes each, ctor `0x8002809C`, vtable `0x800DD024`), then `0x800BE308(slot, id)`
stores the id and calls vtable entry 2 = **`0x800282D0(slot, entry)`**, folio.md §4.1's generic FOLIO
entry loader. So the id is the archive entry: **sheet 416**, the "common" sheet the port already draws
the HUD frames, font, particles and entrance flags from (`ParkHud.cs`, `Particles.cs`,
`EntranceFlags.cs`). There is **no per-type, per-world or per-block base**: `0x800E0140[17·type +
facing + 8·band]` is the sprite number in sheet 416 as it stands. The walking people come from a
different sheet (269, resolved through the person's model object and the `0x800DFFFC` block table —
`PeopleSheet.cs`), which is why 269's sprites 110..120 are a guest body from block 273 and not riders.
The draw then reads the record's own tpage/clut/u/v/w/h (`0x800344F0`, byte +10 = pre-mirrored UVs).

Every site passes `type = 0x800926A0(rider) = V+0x61` (the spawner's `rand(8)`): NonPathedRide
`0x8009FC48`, tour `0x800A2D4C`, track `0x800AC4C8`, `0x800AB1C8`, coaster `0x800B2964`. One call in
`0x800AB1C8` (its second, at the decompile's line 253) passes `byte [obj+0xD]` of another object instead
— not traced.

### 8.2 The table, decoded (MEASURED against the image)
Eight rows of 17 `s16`, `type = 0..7` → rows in the order **110, 121, 132, 143, 66, 77, 88, 99** (first
sprite of each row's 11): types 0..3 use 110..153, types 4..7 use 66..109. In each row:

```
index 0..7   band 0 (level):     s+6, s+7, s+8, s+9, s+10, s+9, s+8, s+7    — 5 sprites, facings 5..7 reuse 3..1
index 8..15  band 1 (elevated):  s+1, s+2, s+3, s+4, s+5,  s+4, s+3, s+2
index 16     band 2 (overhead):  s                                          — one sprite, facing forced to 0
```
where `s` is the row's first sprite. Mirroring for facings 5..7 is done by the draw (§8.1's flag), so
five drawn views cover eight facings: front, front-quarter, profile, back-quarter, back.

### 8.3 They are HEADS, not seated bodies (MEASURED: sheet 416 rendered with the port's own layout)
Rendering sprites 66..153 of sheet 416 (block 0 raw, the rest UNPAK, palettes inside the sheet —
`TextureSheet.cs`) shows **eight hairstyles, 11..20 × 10..18 texels, one palette per row**: black
short hair, blonde short, brown short, dark grey (types 0..3, palettes 3066/3067/3128/3129 — the rows at
110..153); blonde pigtails, black pigtails, red pigtails, dark-blonde bob (types 4..7, palettes
3002/3003/3064/3065 — the rows at 66..109). That pairs with `PeopleSheet.GuestBlocks` (bodies 272, 272,
273, 273, 270, 270, 271, 271): two hair colours per walking body, 270/271 being the pigtailed girls.
Per row, sprite `s` is the crown of the head seen from straight above; `s+1..s+5` the head seen from
above at an angle, front to back; `s+6..s+10` the head seen level, front to back. **A rider on the 54
in-place flat rides is drawn as a floating head at the seat bone — the body is whatever the car mesh
shows.** The five bounce rides (arm B) draw the whole walking sprite instead (§2.5).

### 8.4 The bands are the camera's elevation over the head (READ; naming GUESS-high)
`0x800BFBDC(M)` on the composed `R = Mcam.R × B.R`: the first output is `0x400 − acos(−R32)` with the
acos taken from the table at `0x800DDEF0` (`(x>>1)+0x800` → 2047, 1023, 0 at x = −4096, 0, +4096), i.e.
**`asin(−R32)` in 4096ths of a turn** — the angle between the seat's up axis and the view plane. Then
`band = clamp((a + 256) >> 9, 0, 2)` with negative `a` giving 0:
* band 0: |a| < 22.5°, or any tilt away from the camera — the level views;
* band 1: 22.5°..67.5° toward the camera — the from-above views;
* band 2: > 67.5° — the crown, facing ignored.
So it is not "level versus inclined seating" alone: the park camera's own pitch is in `Mcam.R`, so an
upright seat under the usual elevated view already lands in band 1, and a seat that tips the rider
back or forward moves the head across bands and around the facings as the ride runs. The second output
(`ratan2(R31, R33) + 0x800`) is the facing; the third (`ratan2(R12, R22)`) is the roll, which
`0x800348F4` applies as a rotation of the quad in the screen plane (`rsin`/`rcos` of −roll).

### 8.5 The quad (READ)
`0x800348F4(pos, record, mirror, roll)` runs RTPS on the bone position twice with **H set to the
sprite's w/2 and then h/2** (`ctc2 $26`), so IR0 comes back as each half-extent scaled by 1/SZ; the
half-width is further `<< 9 / 0x140` (×1.6, the 320→512 stretch) and both are shifted by
`[0x801029E4] + 12` (= 14). The four corners are `centre ± (cos·hw ∓ sin·hh)`; the quad is dropped only when
it lies entirely outside x ∈ [0, 512] or y ∈ [0, 256], and is linked into the ordering table at
`(SZ >> 2) + [0x801029E8]` (= SZ/4 − 10, slots 1..0x7CF). Head size on screen therefore follows the sprite's texel size and the seat's
depth, with no per-ride scale.

### 8.6 The override (READ; identification GUESS-high)
When `[0x801029F4]` (gp+0x3A0, 0 in the image, no writer found by gp or absolute store) is non-zero,
the table is bypassed and `sprite = 0x800F1EEC[type % [0x801029F0]]` = sprites **564..569** of the same
sheet, modulo 6. Rendered, those are six 26..32 × 38..46 photographic faces — a developer easter egg or
debug switch that puts real faces on every rider. Nothing on this disc sets it.

