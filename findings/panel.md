# The attraction panel — PAL SLES-026.88

2026-09-20. **READ** = off the instructions at the quoted address; **INF** = inference; **GUESS** = guess.
Coordinates are absolute on the 512x256 screen, and **every text y is a BASELINE**: the font's glyph
entries carry `yoff = -13, h = 13`, so a glyph occupies `y-13 .. y` (char table 0x800DD0E4, `'H'` → sprite
0x19D 9x13). A first pass of this report had the strip at the top left and y as the text bottom; both were
wrong and are corrected here. `A` = the attraction sub-object the panel stores at
panel+0x270; `outer` = A−8 (the C getters take outer).

## 0. Shape

The panel is ONE class family with **four pages behind a tab menu** — Details, Options, Upgrades, Addons
— run as a **blocking modal task**, not a HUD overlay. The same loop serves both the in-park route and
the "Ride Information" list route.

⚠⚠ **"CIRCLE opens it" IS DOWNGRADED TO UNPROVEN — 2026-09-20.** Not refuted: *never established*.
0x80038D80 does not test a CIRCLE bit. It asks `0x8008974C(0)` and `0x800898A8(0)` — **logical button
0** — and the logical table at 0x800E35C0 does not decode as a plain index→button map (idx 0 reads
0x0040/0x0200, DOWN and R2 as raw pad bits, so it holds pairs of something else). **The claim mapped
logical 0 to CIRCLE without ever reading that table.** The route 0x80038D80 → 0x80038900 stands; which
button reaches it was asserted, not read.

⚠ **And the live test does NOT settle it either way.** tinyclaw pressed ○ in the real game and got
frames byte-identical to a do-nothing control — but in a state with an object already selected and the
root menu up, which is not the state this dispatcher describes, and §0 itself says the buttons mean
different things once a menu is open. Their run is good evidence about THAT state and says nothing about
this one. ⭐ Worth copying as method: they proved the instrument first — ✕, △, L1/R1 and Start all did
their jobs from the same save, so the three zeros are real zeros and not a pad that never arrived.

⭐ **CROSS → the context list IS confirmed live**, by the same test: ✕ on a park gate gives a list
containing "Open", and ✕ on a placed shop gives a list containing only "Delete" — no queue built, so no
queue row, which is §3's rule-driven list arriving from the real game instead of from the table.

⭐ **AND THE GATE REALLY DOES OPEN THE PARK**, so the port's gate click is NOT a deviation (parkopen.md
§1a guessed the untraced menu route; that guess was right).

⭐ **AND CROSS OPENS THE CONTEXT LIST — THE SAME COMMANDS, BUILT BY THE SAME ROUTINE.** In the park, CROSS
goes to 0x800387AC, which reads the highlighted object's TYPE (class record slot 16) and jumps through a
19-entry table at 0x800E0358. Every attraction type lands on one handler, which fires a tutorial hint
(id 51, or 57 for a coaster, via 0x8001397C) and then calls 0x800385D0: clear the list at 0x80108F88,
fill it with **0x8004A0B4** — *the Options page's own builder* — and show it at the cursor. So CROSS is
the shortcut to Options and CIRCLE is the whole panel, which is why the two share a builder.
⚠ CROSS on a STAFF member (type 10) instead GRABS them to carry, 0x80093DDC(staff, 1) — the "Grab"
command. In the park TRIANGLE opens Build (loop 0xE) and SQUARE opens Hire (0xF). Buttons
(0x80089010, logical table 0x800E35C0): **CROSS** activate/OK, **TRIANGLE** back (page → tab menu; tab
menu → close), **CIRCLE** close the chain, **SQUARE** close-all then root menu. Panel flags +0x4C
activate, +0x48 cancel, +0x50 done, +0x54 close-all (base input 0x80044D38).

⭐ **A PLAIN FEATURE HAS NO PANEL.** 0x80038900 maps type → loop: 1/3/6/7 → ride (0x800745E0), 4 → shop
(0x80074820), 5 → sideshow (0x80074A60), 10 → staff (0x80074EF0), 2 → **the root menu** unless it is a
toilet (feature slot 33 → 0x80074CA0) or a staff room (record flag via 0x80024110 → 0x80075158). A bench
or a bin opens the main menu instead.

## 1. Chrome

| part | rect | address |
| --- | --- | --- |
| panel | (35,31) 440x191 | 0x80044684 |
| page-name strip | **(306,197)-(506,223), BOTTOM RIGHT** | 0x80044564 |
| its icon | (318,201) + the sprite's own xoff/yoff | 0x80042CAC |
| its text | baseline (354,216), left | |
| info frame (left) | (16,64) 280x152 | 0x80044EBC |
| control frame (right) | (280,80) 180x110, only while a page is active | 0x80044F64 |
| 3D model view | (52,70) 224x140, zoom 1400, spins every frame | 0x8004A7EC / 0x8004B3B4 |
| tab menu box | (154,90) 200x90, rows centred x=254 | 0x8004BBE4 |

### 1a. The rects are the SAME four fields, and they are ABSOLUTE

Re-read 2026-09-20 after the first draw looked wrong. `0x80045AE8(obj,x,y,w,h)` → `0x80040ED0`/`0x80045B34`
→ four leaf setters that write **x→+8, y→+10, w→+20, h→+22**, and the panel's own init (0x800446C4) writes
those same four fields directly. So panel and frames are one widget family with one kind of rect, and
w/h are sizes, not a second corner. The frame rects are not literals: the routines pass four **globals**,
`0x80102B30..3C` = 16/64/280/152 (info) and `0x80102B40..4C` = 280/80/180/110 (control).

Three independent checks say the coordinates are **absolute screen**, not panel-relative:
the name at x=156 is exactly the info frame's centre (16+140); the slider labels at x=370 are exactly the
control frame's centre (280+90); and the tab menu's row centre x=254 lands on the **panel's** centre
(35+220 = 255), which is only true in absolute space.

⚠ **Which leaves the info frame starting 19px LEFT of the panel rect** (16 vs 35). Either the panel rect
is not drawn as a backdrop, or it is not the backdrop. Unresolved — flagged to master against the real game.
**GUESS**: the four greys at `0x80102B20` (0x202020, 0x404040, 0x606060, 0x808080) sit immediately before
the two frame rects in the same data block and read like a gouraud quad's corners, so the port draws the
backdrop with them. Nothing proves it.

Frames are a gouraud quad, orange (0xE7,0x80,0x1A) top to yellow (0xE8,0xCA,0x2D) bottom, with corner
sprite 0x169 + inner 0x148 and edge 0x173 (0x8003EDCC). Sprites come from FOLIO 0x1A0 (#416), 12-byte
entries at table+id*12 (0x80029450). Title ids: ride 0x186, shop 0x129, sideshow 0x17, toilet 0x21D,
staff room 0x2E9, replaced per tab by 0x39A Details / 0x255 Options / 0x291 Upgrade.

Widgets: **bar** 0x8004696C (caps 0x163/0x14B/0x164, knob 0x165/0x166, fill (0xF0,0x40,0x40), track
(0x00,0x68,0x72)); **slider** 0x800422CC (label above, embedded bar, LEFT/RIGHT ±step, width 100, NO
numeric readout); **spinner** 0x8004BFC4 (renders `< N >`, plain int unless money mode); **list**
0x80047FD4 (rows centred, empty → 0x13A "Not Available").

## 2. The ride's Details page (0x800794C4)

⭐ **SIX READINGS, FOUR OF THEM BARS, AND UP TO THREE SLIDERS.**

| label | id | at | value |
| --- | --- | --- | --- |
| name | | (156,80) | slot 11 |
| Age | 0x1DC | (66,114) | `totalDays − u16 A+0xF4` DAYS (0x8009EDBC), shown in YEARS — see below |
| Users | 0x18C | (206,114) | slot 22 = A+0x14, guests served all-time |
| Excitement | 0x37 | bar (176,134) w80 | slot 53, intensity |
| Reliability | 0x3ED | bar (176,154) w80 | slot 88 — the **PROJECTED** value |
| Repair | 0x25C | bar (176,174) w80 | `A+0xB4 >> 12` — the **LIVE** value |
| Life | 0x3FA | bar (176,194) w80 | s16 A+0x68 |

⭐ **THE AGE GETTER RETURNS DAYS; THE ROW SHOWS YEARS.** Re-read 2026-09-20 because "years" had been
asserted without the arithmetic. 0x8009EDBC is `0x80066E78(calendar) − u16 A+0xF4`, and 0x80066E78 is
`lw v0,16(a0)` = McAi+0x10, which transport.md already pins as the DAY counter (McAi+0x18 is months).
The row divides: 0x80079520 loads 0x6719F361 and runs the magic-number sequence
`q = (hi + ((n − hi) >> 1)) >> 8`, i.e. M' = 0x16719F361 over 2^41 = **÷365**. So a ride reads 0 for its
first year. The getter has exactly two callers — this row and 0x800674C8, the park draw score — so the
score's young test `(age << 12) / 2024 < 4` is in the same DAYS unit, true while the age is 0 or 1.

⭐ **RELIABILITY AND REPAIR ARE DIFFERENT NUMBERS** — what the current slider settings will wear the ride
down to, against where it is now. Showing both side by side is the page's purpose.

Sliders (x=320, w=100, labels centred at (370, y−4)), init 0x80078F70, input 0x80079300:
**Speed** 0x1A1 at y=100; **Capacity** 0x364 at y=134 **only when max seats > 1**; **Duration** 0x2D5 at
y=168 (or 134 when Capacity is hidden) and ⚠ **HIDDEN ENTIRELY FOR A COASTER** (0x800798D8). Ranges are
the **union over all three level blocks**, not the current one. Slots 92/93/94 are written back **every
frame** from the widgets.

## 2b. The panel's SOUNDS (group 5), and one trap

Sound calls are `0x800B8E08(a0 = group, a1 = sound)` / `0x800B8E4C` likewise; convention checked against
the known placed sound, group 8 sound 3 at 0x8001C614. A park loads eight groups, listed as u16 at
**0x800F23CC**: `1, 10, 11, 7, 2, 6, 5, 8` (loader 0x80058694 → 0x800B8B80). Group 5 is the UI's.

| what | sound | where |
| --- | --- | --- |
| the panel opens | **(5, 2)** | 0x80038900 — the CIRCLE/open dispatcher of §0 — tail-calls 0x80073EF4, which plays it at 0x800740D4 |
| the context list opens | **(5, 3)** | 0x800385D0, the §0 routine that fills it and shows it at the cursor, at 0x8003877C |
| a slider moves | **(5, 6)** | 0x80079300, §2's slider input, at BOTH 0x80079354 (decrement) and 0x80079378 (increment) |

Each was taken from a routine this report had already identified for a different reason, so the sound and
the thing it belongs to were established separately rather than inferred from one address.

⚠ **THE BASE INPUT 0x80044D38 IS NOT (5,1).** It reads that way to any scan that takes the nearest
`addiu a1,zero,N`: the real `a1` is `addu a1,s2,zero` in the call's delay slot at 0x80044EA0 — a
per-widget value, skipped entirely when negative (`bltz s2` at 0x80044E94) — and the `addiu a1,zero,1`
above it is the PREVIOUS call's argument. Not wired, because the per-widget value is not yet read.

## 3. The other pages

**Options** (list 0x8004A0B4, right at (280,80) 180x110): Build/Edit Queue 0x374/0x37F; type 6 Build/Edit
Track 0x0/0xD; type 1 Edit Track and **Edit Pylons** 0x3DF; **Call Mechanic** 0xAE when status 4/5 and
lifetime ≠ 0; **Delete** 0x24D always; **Zoom To** 0x174 when opened from a list. ⚠ There is **no
Open/Close entry** for types 1..7 — the panel does not open or close a ride.

### 3a. ⭐ THE COMMAND TABLE: label id → handler (READ 2026-09-20)

The Options/context entries are **(u32 label id, u32 handler) pairs at 0x80102C20**, which is why none of
these ids appears as an `addiu` immediate anywhere and grepping for them finds nothing:

| label | id | handler |
| --- | --- | --- |
| Open | 0x102 | 0x8003BC18 |
| Build Queue | 0x374 | 0x8003BC5C |
| Edit Queue | 0x37F | 0x8003BC5C (same one; the id is what differs) |
| **Delete** | **0x24D** | **0x8003BBE4** |
| Build Track | 0x0 | 0x8003BC90 |
| Edit Track | 0xD | 0x8003BC90 |
| (0xD / 0x0 again) | | 0x8003BCC4 — the second pair, presumably the coaster's pylons |

**Delete** (0x8003BBE4) resolves the selection through 0x80050530 → 0x8003C2BC and calls
**0x800510C0(attraction, 1)**. That routine is shared with the placement tool's cancel (0x8001C7E4), and
its `a1` is what separates them: **1** runs the record's teardown vtable (+0xE8/+0xEC), 0x80053C48,
0x80051D74 and plays **(8, 5)**, the demolish sound; **0** skips straight past all of it. ⚠ **No refund**
— nothing in either half touches money, so deleting a ride returns nothing.

✅ **Wired 2026-09-20.** Delete and Build/Edit Queue both run from the context list. Delete restores the
map from a snapshot taken before the placement overwrote it, which is the only honest inverse: `Place`
overwrites each tile's type, ground and flags outright, so what was underneath cannot be recovered from
the result. Proved with a control — place, delete, place again on the same tiles all succeed, and the
same run WITHOUT the delete is refused the second time, so the test rejects the bug instead of passing
because placement is lenient.

**Upgrades** (0x80079E38), only while `level+1 < researched` and the ride is not condemned: Upgrade Cost
0x119 = `record+0x50 + 0x34*(level+1)`; for a track ride **Stock** 0x363 = `35 − u8(outer+0x1BD8)` ⭐
which is the track-piece stock, matching the 35 piece objects the ride's constructor builds. Refusals:
0x31A no mechanic, 0x17D mechanics on strike.

**Addons** (track rides only, 0x8007A0AC): type-8 records, cost = addon record +0x20, **Stock** =
`3 − u8(outer+0x1CF8)`, builds via command 10.

**Shop** (0x8007A9E4): Customers 0x4C, Cost of Goods 0x11, Takings 0x63 (outer+0x8C), Profit 0x3A4
(outer+0x90), **Customer Satisfaction** bar 0x3FB; right: **Quality Of Goods** slider 0x127 (50..100 in
steps of 25), a product slider whose label is one of Fat/Ice/Sugar/Salt, and a **Sale Price** spinner
0x230 (1..500, u16 outer+0x88). ⚠ **No stock row.**

**Sideshow** (0x8007B2F4): Customers 0x293, Winners 0x257 (only with a prize), Takings 0x383, Profit
0xE2, Excitement 0x350, Satisfaction 0x2B9; right: **Chance of Winning** 0x116, **Prize Cost** 0x313
(1..1000), **Game Price** 0xB6 (1..1000).

**Toilet** (0x8007C014): Users 0xA3, Last Cleaned 0x400 (as `Nw Md`), Cleanliness 0x7E bar. No sliders.
**Staff room** (0x80085F7C): a count per staff type, plus Kick Out entries.

## 4. ⚠ Disagreement with rides.md line 415

rides.md says a ride is opened/closed through attraction slots 27/28 from 0x80078798 / 0x8007841C. Those
two call the **PANEL's** vtable, not the attraction's (0x800784BC..D4: `lw v0,16(s1)` is panel+0x10, and
slot 28 is 0x80079C98, the ride panel's own). The Options list has no Open/Close row for types 1..7; the
only "Open" entry (0x102 → attraction slot 27) is added for objects of type 0x12. **The panel does not
open or close rides.** Recorded as a disagreement rather than silently swapped.

## 4b. Labels that are NOT drawn by any of these routines
Checked against the draw code rather than the string table, because grepping words produced four wrong
answers: **0x7A "State of Repair"**, **0x2DF "Purchase Cost"**, **0x27B "Open"** and **0x401 "Close"** are
not drawn by any attraction panel (0x396 "Close" is only the button legend, and 0x102 "Open" is an Options
entry for type-0x12 objects alone). **0x233 "Upgrade 3" never appears** — the row is only ever Upgrade 1 or
2. **0x2E "Ticket Price" is the park gate's**, not a ride's. And **0x363 "Stock"** is the track ride's
Upgrades/Addons readout, not a shop's.

## 4c. ⚠ OPEN: the 19px overhang (flagged 2026-09-20, master could not confirm)

**The left info frame starts 19px LEFT of the panel's own rect** — frame x = 16 (global 0x80102B30),
panel x = 35 (0x800446C4) — and both were re-read and are what the binary says. Asked master to check the
real game; they do not know either, so it stays open by agreement rather than being guessed.

**What it blocks:** the ornamental border (corner 0x169 + inner 0x148, edge 0x173/0x161, 0x8003EDCC) is
NOT drawn on either frame. A decorative border running off the screen edge reads as a bug whether or not
it is faithful, so the port draws the frames as plain gouraud fills until this is settled. The fills
themselves are at the read coordinates and are correct; only the border waits.

**How to settle it:** one screenshot of the real panel. If the left frame sits inside the backdrop, then
one of the two rects is misread and the frame globals are the likelier suspect, since the panel's rect is
written as four literals in its own init. If it really does overhang, draw the border and let it clip.

## 5. Not established
Text y semantics (top vs baseline); legend slot → button icon; what sets the ctx+4 gate and the fade
timing; spinner input step/repeat; three of the four frame edges; the backdrop quad colour; OT ordering
(assumed lower = nearer); the "Zoom To" condition; what object type 0x12 is; the shop satisfaction
formula; whether the Details page really shows through the tab menu.
