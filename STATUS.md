# Where the port is

⭐ **THE LIVE BOARD IS THE ONE TO READ AND TICK:** https://claude.ai/artifact/RoeedNAubVjynBFaM2wacn
It saves itself — no pull, no commit — and every row shows how long since it last moved, so a row
nobody has touched says so in red instead of quietly going stale. This file is the git-side copy for
anyone who cannot open that link.

⚠ **Two copies is how a tracker starts lying.** If you change one, change the other, or say so.

**If this disagrees with the code, the code is right and this is a bug.**

How to use it: whoever changes an area updates its row in the same commit. State means:
**✅ built + verified** (there is evidence: a measurement, a control, a picture);
**🔶 built, not proved** (it runs, nothing has tested it end to end);
**🚧 in progress**; **⬜ not started**.

⚠ Rows are owned. Do not mark someone else's row done — ask them. `?` means nobody has said.

**Read it in a browser: https://claw.bitvox.me/tpw/** — `python3 tools/status_board.py STATUS.md
/home/ec2-user/apps/static/tpw/index.html` regenerates it. ⭐ THE BOARD IS A VIEW OF THIS FILE, NOT
A SECOND COPY OF THE TRUTH. A tracker with its own store has the README's disease twice over: edit
the row here, in the commit that changes the code, and the page follows.

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
| Guests: needs, decisions, spending, queueing, riding | 🔶 | tinyclaw | boards cleanly here — 50 served in a 2500-frame run; wander, litter, happiness all wired. Shops/sideshows buy nothing yet |
| Rides: cycle, loading, wear, breakdown, closing | 🔶 | tinyclaw | load/run/unload/wear/repair all measured. Breakdown now ejects (`1 thrown off, 1 out of the queue`) — it was an empty method and ate guests |
| Staff: hire, motion, mechanic, guard, entertainer | 🔶 | tinyclaw | mechanic proved end to end (status 4 reliability 1 → status 10 reliability 98); wages measured £150 against a £0 control. Entertainer/researcher have no per-class behaviour yet |
| Pathfinder | 🔶 | tinyclaw | routes, queues, waypoints, the type-13 join. ⚠ 10/10 request slots saturate on a park with a disconnected piece — guests there retry for ever |
| Turnstile / entry fee | ✅ | tinyclaw | measured: 15 refused, 15 left |
| Connectivity diagnostic (door + exit reachable from the gate) | ✅ | tinyclaw | ⭐ the game has NO such check — diagnostic only, never refuses a build. `ParkView.TileJoinedToGate` / `AttractionJoined` are callable from the build tool |
| Statistics (72) + advisor rules (125) | ✅ | astra | spot-checked against the disc |
| Research + ride upgrades | 🚧 | astra | in progress |
| Economy: bank, wages, month end | 🔶 | tinyclaw | bank, entry fee (£40/head, measured against a no-gate control), wages, month end. ⚠ Rides earn nothing and shops are not wired, so income is entry fees only |
| Front-end: menus, boot, movies, music | 🔶 | ? | |
| Saving / loading a park | ⬜ | — | nothing written |
| Scenarios / win conditions | ⬜ | — | nothing written |
| Multiplayer | ⬜ | — | out of scope for now |

## Known bugs

| what | who found | owner | state |
|---|---|---|---|
| Guests stuck in a queue while later joiners board | master | tinyclaw | **one cause fixed** — the shuffle carried no stagger, so each board froze the queue for up to 300 ticks (measured 236 → 7). A second, self-inflicted one fixed after it: the shuffle message freed the WRONG guest's waypoints. Still reported on master; F3 lists every queuer's wait so an outlier names itself |
| Guests stall at a ride exit, then walk off | master | tinyclaw | ⚠ **not a code bug**: the game never checks exit connectivity either. Readout names it now. |
| `CompleteUpgrade()` is an empty stub | catboy | astra/catboy | open — will quietly no-op the whole upgrade path |
| `MechanicAssigned` always false | catboy | tinyclaw | **fixed** — MechanicClaim was one line away the whole time |
| `PostMessage` drops every ride message | catboy | catboy | open |
| Rides never reach Running in catboy's headless parks | catboy | tinyclaw | open. ⚠ First thing to rule out is a **stale game assembly**: a root `dotnet build` does NOT build `game/TPWGodot.csproj` and still says "Build succeeded", so the run uses an old DLL. `dotnet build game/` and check with `strings -el game/.godot/mono/temp/bin/Debug/TPWGodot.dll`. That trap has cost an afternoon here before. ⚠ Also: `--park-lay 18,6,18,31: 0 tiles took path` is NORMAL — those tiles are already path on map 203 — so it is not evidence the spine is missing |

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
