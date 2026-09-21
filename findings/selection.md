# Campaign park selection (PAL SLES-026.88)

READ here means the established executable/overlay analysis in
[objbytes.md §3](objbytes.md#3-world-selection-and-ticket-spending) and the existing container
layout in [save.md §2](save.md#2-card-container-only-as-it-shapes-the-format-read), plus the
decoded table bytes. Those control-flow findings are **cited, not re-derived**. There is no
new whole-program census, scenario blob, objective evaluator, or terminal victory transition.

## §0 SOURCE DISAGREEMENTS

**No new computational source disagreement found.** The extracted **19 rows: 8 nodes + 11
links** agree with the retained findings. Existing disputed rules outside selection are untouched.

| Source / addresses | Retained reading | This implementation / disagreement |
|---|---|---|
| `objbytes.md` §3, OVL11 **0x801141F4**, **0x801145BC..CC**, **0x80114648..64** | 19 rows, comprising 8 nodes and 11 links; node world/park and link endpoints/costs | Decoded bytes agree, including all 11 links and 8 name IDs; no disagreement. |
| `objbytes.md` §3, **0x8006BC68**, **0x8006BCE4**, **0x8006C83C..4C** | Open 0 / closed 1 / unopened 2; closing frees a park buffer without changing objectives | Retained. `ParkSave.Open` is the separate entrance-gate byte; reusing it for campaign state would contradict the source. |
| `save.md` §2, **0x8006C5D0..680**, especially **0x8006C620..624** | Eight status bytes in the existing archive; low nibble controls status, exact F0 controls a packet | Retained through `SaveArchive`. `save.md`'s formerly unnamed ticket/award fields are refined by `objbytes.md` §5, not a conflicting layout. This adapter leaves those fields unchanged. |
| `scenario.md`, `goals.md`, `objbytes.md` §5; **0x8006BFE4**, **0x8006BE3C**, **0x8006BF4C**, **0x80117180..E4** | No scenario file or terminal campaign-victory branch established in the bounded consumers | Retained. No campaign-complete flag or transition. `ParkObjectives` remains the award owner. |

Port boundary policies are labeled in code: validating supplied table/state data, rejecting a
request to unlock a node with no direct link, and rechecking affordability when committing a
host confirmation. These are not claims about corrupt-table behavior or native UI reachability.

## 1. The supplied table: 19 rows, 8 nodes, 11 links

All **12** overlays expand at **0x80114158**. `tools/audit_selection.py` imports
`overlays()`, `OVL_BASE`, and the extracted-data location from `tools/audit_scenario.py`, exactly
as `audit_objbytes.py` does. It expands **12 overlays**, decodes the selection table only in
**overlay 11**, and writes [selection-audit.json](selection-audit.json). There is no second extractor.

The table is **19 × 28 = 532 bytes**, starting at **0x801141F4**. The audit accounts for
**19 rows: 8 nodes + 11 links, 0 skipped rows**, preserving the row address and raw bytes beside
each decoded record. It records packed-file, expanded-overlay, and table SHA-256 hashes.
Fields outside the named selection values have no new semantic interpretation.

Node source bytes **+6/+7** become live-node **+0A/+0B** at **0x801145BC..CC**. The eight
known name IDs appear as little-endian halfwords at source **+0A**; matching those bytes adds
no text-loading or localization behavior.

| Node / row | Row address | World, park | Name text ID |
|---|---|---|---|
| 0 | **0x801141F4** | 0,0 | **0x3B1** |
| 1 | **0x80114210** | 0,1 | **0x3B2** |
| 2 | **0x8011422C** | 1,0 | **0x175** |
| 3 | **0x80114248** | 1,1 | **0x177** |
| 4 | **0x80114264** | 2,0 | **0x2F0** |
| 5 | **0x80114280** | 2,1 | **0x2F1** |
| 6 | **0x8011429C** | 3,0 | **0x196** |
| 7 | **0x801142B8** | 3,1 | **0x197** |

Link source bytes **+18/+19/+1A** hold endpoint A, endpoint B, ticket cost, copied by
**0x80114648..64**. Endpoints are node indices, not world indices. The exact **11 links** are:

| Row | Row address | Endpoints, undirected | Spendable tickets |
|---|---|---|---:|
| 8 | **0x801142D4** | 0–2 | 1 |
| 9 | **0x801142F0** | 0–4 | 1 |
| 10 | **0x8011430C** | 4–2 | 1 |
| 11 | **0x80114328** | 2–1 | 2 |
| 12 | **0x80114344** | 4–1 | 2 |
| 13 | **0x80114360** | 1–6 | 2 |
| 14 | **0x8011437C** | 1–3 | 2 |
| 15 | **0x80114398** | 6–3 | 2 |
| 16 | **0x801143B4** | 6–5 | 2 |
| 17 | **0x801143D0** | 3–7 | 2 |
| 18 | **0x801143EC** | 5–7 | 2 |

`ParkSelectionTable` takes the host's decoded nodes/links and owns read-only copies. Tests
read this JSON, pin each of the **11** real links independently against the published topology,
and exercise each in both directions with one ticket too few and with the exact price. The
**8** world/park/name tuples are independently pinned. All **64** ordered endpoint pairs have
an edge-present/edge-absent expectation; for example 0–7 is a negative control, not a free link.

## 2. Selection, unlock, close, and open

The source loads statuses for **8 parks**, **0x8011512C..48 → 0x8006BD74**. New saved slots
initialize to **2**, **0x8006C83C..4C**; campaign initialization opens node **0**,
**0x8006BB14..20**. `ParkSelection` starts with that status arrangement and a cursor at node 0.
`Restore` replaces the **8** statuses and accepts a host-supplied cursor without gameplay effects.

The API separates the operations already distinguished by the source:

* `InspectSelection(target)` / `Select(target)` use the locked path only for status **2**,
  **0x801154D0..D8**. A status-0 or status-1 target selects immediately, without a link lookup
  or a ticket query; the native cursor is **object+0x33**. An unopened target needs a direct
  edge, checked in either direction as at **0x80115528..A0**. A nonadjacent host request is
  rejected with `NoLink`; no multi-hop pathfinding or zero-cost fallback is invented.
* A linked unopened target compares its cost with **spendable** tickets,
  **0x801155AC..C4 → 0x8006BE1C**. Exact equality suffices. A successful preview requests
  confirmation and has no status/cursor/ticket effects. The host cancels by not confirming.
* `Select(target, confirmUnlock: true)` repeats the current-node link lookup, changes the
  selection, performs the close/unlock operation, then calls `SpendTickets(cost)`, following
  **0x801172EC..0x80117378 → 0x8006C024**. Unlocking leaves status **1**, not status 0.
  Tests verify effect order and reject a second charge when selecting the same unlocked node.
  The port also rechecks the live balance to prevent a stale host confirmation from overspending.
* `OpenSelected()` changes a closed park to **0**, **0x8006BC68**. It counts only status-0
  parks, **0x80116F70..A8**, and blocks when the count equals **3**, **0x801173D4..EC**.
  Unlocking a fourth park is permitted into CLOSED; opening it waits until a park closes.
  **⚠ DO NOT FIX:** the comparison is equality, not a clamp or a repair of abnormal restored
  states. A test of a host-supplied count of four distinguishes `==3` from `>=3`. Ordinary
  transitions starting from the campaign initializer cannot produce that abnormal state.
* `CloseSelected()` sets an open park to **1** and requests buffer disposal, **0x8006BCE4**.
  Opening a previously unlocked closed park needs no further ticket payment. No objective bit,
  bonus bit, or award word is cleared. Availability is independent of the entrance gate.

`IParkSelectionHost` supplies the current spendable balance and synchronous spending/buffer
effects. It owns ticket storage and implements the subtraction; selection never consults lifetime
tickets, advertised park totals, or `ParkObjectives`. The host must leave the lifetime word
**0x80103988** and the eight award words at **0x80109B18** intact when spending or closing.
The spendable word is **0x80103984**. These are the already named globals in `objbytes.md`.
Callbacks must not reenter selection or throw after partial effects; transaction rollback is not
introduced. Tests use a host that actually subtracts the requested amount while retaining lifetime
**50**, making earned-vs-spendable confusion observable even with an insufficient balance.

## 3. The world/park pair on entry

`SelectedPark` reads the supplied node fields, including when a host provides a different node
ordering. Source getters are **0x80114A00** (world, live +0A) and **0x801149DC** (park, live +0B).
Main-image **0x800BCAF4..0x800BCB14** copies them out; **0x800BCB9C..A8** passes them through
**0x80050524** to the park constructor. Entry stores park at **0x80057ED4 → 0x801038A4** and
world at **0x80057EE0 → 0x801038A0**, feeding the existing map/catalogue/objective selectors.

`EntryPark(restrictedMode)` exposes that pair for the host's entry flow. Sandbox explicitly
returns **(0,0)**, **0x800BCB90..98**, without modifying the campaign cursor/statuses. Tests
cover **8** selected pairs, each with a campaign and sandbox control. No Godot scene or global
variable is added to the engine-free sim.

## 4. Existing save slots, no new format

`ParkSelectionSave` uses `SaveArchive.GetParkStatus` / `SetParkStatus`. The existing application
header starts at file **+0x200**; its **+0x2B..0x32** (file **+0x22B..0x232**) contains the
**8** world-major status bytes, indexed by **2×world+park**, **0x8006C5D0..680**.

Restore dispatches low nibble **0 → open**, **1 → closed**; other values leave initialized
**2 → unopened**. **⚠ DO NOT FIX:** status and packet presence are distinct. The unchanged
archive codec loads a packet only for exact byte **0xF0**, **0x8006C620..624**. An open park
without a saved packet can have status byte 0. Capture writes the status and keeps packets only
for open parks, discarding stale copies for closed/unopened parks. The existing archive writer
adds the F0 marker when an open slot actually contains a packet.

The round-trip test includes **8 mixed statuses**, **3 real `ParkSaveCodec` streams**, and
`SavePacket` / `SaveArchiveCodec` write-read-write. The packet compressor in this test is
explicitly an identity transport, not a claim of PSX UNPAK compatibility. Open campaign parks
carry closed entrance gates in their streams as a negative control against conflating the states.
It then closes/reopens node 7 and verifies buffer disposal. Another fixture reverses the supplied
node ordering to check that save slots use the world/park fields, not the selection index.

No new save bytes hold the UI cursor. Restore takes it explicitly from the host. Header
**+0x33..0x5F**, including the existing ticket/award storage, remains byte-identical across the
selection adapter and archive round-trip. `ParkSave`, `ParkSaveHost`, `ResearchSave`, and the
existing archive codec are unchanged; this is a campaign-level adapter alongside those layers.

## 5. Validation

Reproduce from this worktree:

```
PYTHONDONTWRITEBYTECODE=1 python3 tools/audit_selection.py
dotnet test tests/TPW.Sim.Tests/
PYTHONDONTWRITEBYTECODE=1 python3 tools/mutate_selection.py
```

Each new test has a REJECTS comment. The positive/negative controls include each of **11** real
edge prices in both directions, absent edges among **64** ordered pairs, exact affordability,
stale balance/cursor confirmations, unlocking at the **3**-open cap, closing to free capacity,
repeat selection, campaign/sandbox entry, status-vs-packet markers, gate-vs-campaign state, and
preservation of ticket/award bytes.

`tools/mutate_selection.py` changes one production rule at a time, freezes the compiled tests
and fixture, and requires exactly the baseline selection-test count for every mutant. Build
errors, missing/partial runs, skips, and changed test inputs are invalid, never kills. It handles
SIGINT/SIGTERM and restores sources in a `finally` guard. Source/input/assembly hashes,
per-mutation failing tests, and baseline/final full suites are recorded in
[selection-mutations.json](selection-mutations.json).

Completed sweep: **65/65 mutations killed, 0 survived, 0 invalid**, with **38/38 selection
tests executed for each mutant**, no skips, unchanged tests/fixture, and byte-identical source
restoration. There were no survivors requiring a revised fixture. The baseline and final restored
`dotnet test tests/TPW.Sim.Tests/` suites both passed **1,835/1,835**, **0 skipped**. This adds
**38** tests to the worktree's prior **1,797**. **0 new source disagreements** were found.

## 6. NOT ESTABLISHED

* Playable world-map UI, input navigation, confirmation-dialog timing, rendering, or game-host
  wiring. `game/` is unchanged. Native reachability of arbitrary nonadjacent target requests is
  outside the retained findings; `NoLink` is a host boundary policy.
* A new scenario blob, meanings for the remaining objective bytes, a terminal campaign victory,
  or a campaign-complete transition. `scenario.md`, `goals.md`, and `objbytes.md` retain their
  bounded conclusions; this is not a new whole-program proof or console campaign playthrough.
* A native saved selection-cursor field, new ticket/award save ownership, or automatic entry into
  a constructed park. The host supplies the cursor, ticket storage/effects, packet ownership, and
  construction using the returned world/park pair. It must call the existing award machinery.
* Physical card compatibility or a retail save/console measurement. The existing archive format
  is exercised in memory; the new round-trip's compression is a labeled test transport.
* New meanings for selection-table presentation/unknown bytes, cheat-input activation, or repairs
  of corrupt saves/abnormal restored states. No objective or award behavior is changed by closing.
