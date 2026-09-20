# The attraction panel — PAL SLES-026.88

2026-09-20. **READ** = off the instructions at the quoted address; **INF** = inference; **GUESS** = guess.
Coordinates are absolute on the 512x256 screen. `A` = the attraction sub-object the panel stores at
panel+0x270; `outer` = A−8 (the C getters take outer).

## 0. Shape

The panel is ONE class family with **four pages behind a tab menu** — Details, Options, Upgrades, Addons
— run as a **blocking modal task**, not a HUD overlay. The same loop serves both the in-park route and
the "Ride Information" list route.

⭐ **CIRCLE opens it** over a highlighted attraction (0x80038D80 → 0x80038900), not CROSS. Buttons
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
| title icon | (41,36) | 0x80044564 |
| title text | (83,50), left | |
| info frame (left) | (16,64) 280x152 | 0x80044EBC |
| control frame (right) | (280,80) 180x110, only while a page is active | 0x80044F64 |
| 3D model view | (52,70) 224x140, zoom 1400, spins every frame | 0x8004A7EC / 0x8004B3B4 |
| tab menu box | (154,90) 200x90, rows centred x=254 | 0x8004BBE4 |

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
| Age | 0x1DC | (66,114) | `totalDays − u16 A+0xF4`, shown in YEARS (0x8009EDBC) |
| Users | 0x18C | (206,114) | slot 22 = A+0x14, guests served all-time |
| Excitement | 0x37 | bar (176,134) w80 | slot 53, intensity |
| Reliability | 0x3ED | bar (176,154) w80 | slot 88 — the **PROJECTED** value |
| Repair | 0x25C | bar (176,174) w80 | `A+0xB4 >> 12` — the **LIVE** value |
| Life | 0x3FA | bar (176,194) w80 | s16 A+0x68 |

⭐ **RELIABILITY AND REPAIR ARE DIFFERENT NUMBERS** — what the current slider settings will wear the ride
down to, against where it is now. Showing both side by side is the page's purpose.

Sliders (x=320, w=100, labels centred at (370, y−4)), init 0x80078F70, input 0x80079300:
**Speed** 0x1A1 at y=100; **Capacity** 0x364 at y=134 **only when max seats > 1**; **Duration** 0x2D5 at
y=168 (or 134 when Capacity is hidden) and ⚠ **HIDDEN ENTIRELY FOR A COASTER** (0x800798D8). Ranges are
the **union over all three level blocks**, not the current one. Slots 92/93/94 are written back **every
frame** from the widgets.

## 3. The other pages

**Options** (list 0x8004A0B4, right at (280,80) 180x110): Build/Edit Queue 0x374/0x37F; type 6 Build/Edit
Track 0x0/0xD; type 1 Edit Track and **Edit Pylons** 0x3DF; **Call Mechanic** 0xAE when status 4/5 and
lifetime ≠ 0; **Delete** 0x24D always; **Zoom To** 0x174 when opened from a list. ⚠ There is **no
Open/Close entry** for types 1..7 — the panel does not open or close a ride.

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

## 5. Not established
Text y semantics (top vs baseline); legend slot → button icon; what sets the ctx+4 gate and the fade
timing; spinner input step/repeat; three of the four frame edges; the backdrop quad colour; OT ordering
(assumed lower = nearer); the "Zoom To" condition; what object type 0x12 is; the shop satisfaction
formula; whether the Details page really shows through the tab menu.
