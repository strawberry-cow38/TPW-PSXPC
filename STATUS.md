# Where the port is

**One page, kept current. If this disagrees with the code, the code is right and this is a bug.**

How to use it: whoever changes an area updates its row in the same commit. State means:
**✅ built + verified** (there is evidence: a measurement, a control, a picture);
**🔶 built, not proved** (it runs, nothing has tested it end to end);
**🚧 in progress**; **⬜ not started**.

⚠ Rows are owned. Do not mark someone else's row done — ask them. `?` means nobody has said.

---

## The game

| system | state | owner | evidence / note |
|---|---|---|---|
| Disc + assets (models, textures, audio, movies, strings) | ✅ | shared | 19-check asset self-test passes on a real PAL disc |
| Park world: terrain, paths, scenery, gate, camera | ✅ | catboy | renders; game camera is the default |
| Placement: attractions, footprints, doors, ghosts, cost | ✅ | catboy | place/delete/replace proved with a control |
| Paths + queues (lay, undo, doors, the type-13 join) | ✅ | catboy | queue→path join measured both ways |
| Track builder (coasters/track rides) | 🔶 | catboy | builds; not driven end to end |
| Attraction panel (Details page) + context menu | ✅ | catboy | drawn, measured against the game's own % |
| Panel commands: Delete, Build/Edit Queue | ✅ | catboy | controls for both |
| Panel: Build/Edit Track, Call Mechanic | ⬜ | catboy | table RE'd (0x80102C20), not wired |
| Sounds: tools, placement, UI, ride ambience | ✅ | catboy | every call site enumerated; probe-verified |
| Bus + arrivals + park draw score | ✅ | catboy | arrivals hard-wired to exit 0, per measurement |
| Park opening (via the gate) | ✅ | catboy | same sequence as the game's menu entry |
| Guests: needs, decisions, spending, queueing, riding | 🔶 | tinyclaw | boards cleanly in their park; see bugs |
| Rides: cycle, loading, wear, breakdown, closing | 🔶 | tinyclaw | |
| Staff: hire, motion, mechanic, guard, entertainer | 🔶 | tinyclaw | |
| Pathfinder | 🔶 | tinyclaw | |
| Turnstile / entry fee | ✅ | tinyclaw | measured: 15 refused, 15 left |
| Statistics (72) + advisor rules (125) | ✅ | astra | spot-checked against the disc |
| Research + ride upgrades | 🚧 | astra | in progress |
| Economy: bank, wages, month end | 🔶 | ? | |
| Front-end: menus, boot, movies, music | 🔶 | ? | |
| Saving / loading a park | ⬜ | — | nothing written |
| Scenarios / win conditions | ⬜ | — | nothing written |
| Multiplayer | ⬜ | — | out of scope for now |

## Known bugs

| what | who found | owner | state |
|---|---|---|---|
| Guests stuck in a queue while later joiners board | master | tinyclaw | open — F3 readout added to name it |
| Guests stall at a ride exit, then walk off | master | tinyclaw | ⚠ **not a code bug**: the game never checks exit connectivity either. Readout names it now. |
| `CompleteUpgrade()` is an empty stub | catboy | astra/catboy | open — will quietly no-op the whole upgrade path |
| `MechanicAssigned` always false | catboy | catboy | open |
| `PostMessage` drops every ride message | catboy | catboy | open |
| Rides never reach Running in catboy's headless parks | catboy | ? | open — tinyclaw's park gets 50 riders, mine gets 0 |

## Open questions (need a human, or a screenshot)

| question | why it is stuck |
|---|---|
| Does the real panel's left frame overhang its backdrop by 19px? | Both rects re-read and both say yes; master could not confirm. Blocks the ornamental borders. |
| Should leavers always use the exit bus stop? | The game rolls `rand(2)`, so half walk to the arrival stop. Changing it is a deliberate deviation. |
| Is effect 0x78 the park-open jingle? | GUESS-high in parkopen.md; not a (group, sound), so nothing is played. |

## Deliberate deviations from the original

| what | why |
|---|---|
| Ride ambience skips group 1 sounds 9 and 10 | They are the toilet's; the game really does play them at rides. Master's call. |
| Queue "stamping" with shift held | Master's, not the game's. |
| Open the park by clicking the gate | The game uses a park menu entry; the port has no park menu yet. |
