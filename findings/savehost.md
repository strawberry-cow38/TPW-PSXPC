# Live park file saving

## §0 SOURCE DISAGREEMENTS

No new disassembly was substituted for existing findings, and **core/TPW.Sim is unchanged**.
The adapter encountered this existing disagreement between reports/the live catalogue:

| Sources and addresses | Both readings | Treatment |
| --- | --- | --- |
| `save.md` §3.2: writers/loaders `0x800A00BC/0x800A0120`, `0x800A1020/0x800A1090`, `0x800ADB20/0x800ADCD8`; `rides.md` §1 and `TPW.Data.AttractionCatalog` theme/record readers (`0x8002ED50`, `0x8006A7C8`) | Save findings/core assign type 1 the short flat record, type 3 the short tour record, type 7 the large coaster record. Live catalogue identifies type 1 as coaster, type 3 as flat, type 7 as tour. | Keep the established codec sizes/type order. Shared ride fields round-trip for the live flat rides. Refuse nonempty coaster geometry and conflicting class-specific tails by name. No invented type remap or storage in UNKNOWN bytes. A source correction needs its own investigation. |

Inherited save-boundary disagreements remain as documented in `save.md` §0: guest constructor dice order, integer stat clamping, staff morale/tiredness initialization, and placement-day rather than ride-ticket interpretation. `ParkPoints.Aim(1)` also retains its existing disputed Z jitter. This work does not resolve those readings.

## What is wired

`game/ParkSaveHost.cs` implements every `IParkSaveHost` member over the live `ParkView`, `ParkGuests`, and `ParkFinances`. The file is exactly `ParkSaving.Save(host)`'s unpacked binary stream. Loading uses `ParkSaving.Load(bytes, layout, host)`.

Capture/restore covers attraction definitions, positions, rotations, levels, status normalization, queue routes, ride sliders/reliability/lifetime/build day/served count; feature cleanliness/refill stamp; shop visits/price/takings/profit/satisfaction/settings; visitor population ranges; staff classes/positions/hire dates/skill/strike state; bank balance and the live loan fields; calendar and admissions; park-open flag and entry fee.

`BeginPark` reloads pristine terrain and assets, creates fresh people/pathfinding/ride managers, resets finances and clocks, and reconnects the bank to Main, HUD, entrance and attractions. Reload now clears staff bodies as well as guests. Saved visitors spawn using the existing initializer and `ParkPoints.Aim(1)`, then the core restores population ranges and state 45; loading charges no entry fee. The core's intentional two path-placement calls and opening-before-calendar order remain intact.

Queue/track builders now retain their route on the owning attraction after the tool closes. Supported type-6 track routes (up to the save record's 20 points) rebuild through the existing `TrackRun`; the current renderer supports one retained track. Nonempty unsupported sections throw `NotSupportedException` naming the missing integration. There are no empty/default-return interface implementations.

## File hooks and reproduction

Load is applied immediately after `ShowPark` loads the map/assets/finances, before construction hooks. Save is applied after placement, paths, queue/track, hires, demolition/replacement, prices, upgrades and immediate breaks. An optional render-frame delay allows normal simulation before capture and does not depend on the screenshot clock.

```sh
TPW_DATA=/path/to/disc.iso godot --headless --path game -- \
  --no-boot --park=203 --park-open \
  '--park-place=217,26,29,2' '--park-hire=0,20,20' \
  --park-upgrade=217 --park-save=/absolute/path/park.save

TPW_DATA=/path/to/disc.iso godot --headless --path game -- \
  --no-boot --park=203 --park-load=/absolute/path/park.save

# To capture after guests have had time to arrive:
# add --park-save-after=5000 to the saving run.
```

Select the same `--park=MAP` on load: map dimensions and catalogue counts are external to the core format. Missing/invalid map selections and file failures exit nonzero. Serialization completes before a temporary sibling file atomically replaces the destination; a failed capture cannot truncate an existing save.

Run the reproducible proof with your Godot Mono executable and disc:

```sh
python3 tools/prove_savehost.py --godot /path/to/godot-mono --data /path/to/disc.iso
python3 tools/mutate_savehost.py --godot /path/to/godot-mono --data /path/to/disc.iso
```

The first command builds **game/** and runs separate writer/reader processes with the actual Main file hooks. The writer places through CLI tools, upgrades rides, hires all five staff classes, and spawns nine visitors, two of whom pay through the real turnstile. It sets distinctive test values before saving. Live-field JSON snapshots, independent of `Capture`, are compared in full; they are diagnostic output, not an alternate save format. Save artifacts/logs stay in ignored `game/obj/savehost-proof/`.

## Round-trip evidence

The file is **4,881 bytes**. [`savehost-proof.json`](savehost-proof.json) records the full before/fresh/after snapshots.

| Field | Before save | After fresh load |
| --- | --- | --- |
| Attractions | 4 | 4 |
| Entry 195 | (30,38), rotation 1, feature cleanliness 63, refill day 819 | same |
| Entry 217 | (26,29), rotation 2, level 1 | same |
| Entry 220 | (12,24), rotation 1, level 2; queue (11,26) → (8,26) | same |
| Entry 237 | (10,36), rotation 3, shop price £19; takings 1,234 raw, profit −321 raw | same |
| Guests / paid admissions | 9 / 2 | 9 / 2 |
| Staff | 5, one of each class | 5, same classes |
| Staff raw positions | X 5261,5517,5773,6029,6285; Z 5495 | same |
| Staff hire days / skills | 803..807 / 0..4; researcher striking | same |
| Bank | 314,159 raw = £31,415.90 | same |
| Calendar | total day 836; year 2, month 3, day 16 (zero based); total months 27 | same |
| Park open / entry fee | true / £7 | true / £7 |
| Loan slots | slot 0: £2468 remaining, £73/month, 19 months; slot 2: taken, repaid, −2 months | same |
| Path / queue tiles | 38 / 5 | same |

The reader's **fresh control** has zero attractions, guests and staff, balance 500,000 raw (£50,000), day 0, closed gate, £40 fee. The assertions reject that park if loading is omitted. Additional controls reject a missing file and wrong map, verify an occupied reload clears old bodies/pools, and verify a closed save closes an already-open park. Visitor assertions exercise live restored ranges and the unknown byte, not just the headcount.

The supplied ISO is cooked (2048-byte sectors). The existing asset self-test reports the disc-layout and movie checks failing and the launcher hash as unrecognized. The real map/catalogue/models/terrain/people assets decode and drive this proof. This is not a claim that movies/raw-sector audio or full disc identification passed. Godot also prints its existing exit resource-leak warning; the proof explicitly checks the old staff/guest nodes are queued for deletion on reload.

## Gaps and explicit refusals

- No live research catalogue/topics, park message manager, influence map, or litter pool. Capture writes zero/empty sections (influence has no separate field). Zero sections are validated on load; nonzero research/litter and any message throw by name. No invented managers.
- No PSX memory-card framing or compression: plain unpacked park files only. The `--park-nogate` diagnostic has no live entry-fee owner and is explicitly refused by the save host.
- The live catalogue is the port's documented union of attraction definitions, not a recovered PSX scenario catalogue. Type-8 research definitions and restricted-mode selection are not wired in Main; these files use the current full-sim port layout. They are not advertised as arbitrary original-card imports.
- Nonempty coaster geometry is blocked by the class/record disagreement above. Nonempty secondary track records and more than 20 type-6 route points throw. Multiple retained tracks throw because the current renderer only owns one track's geometry. These are integration limits, not silently discarded construction.
- Sideshow counter semantics are still UNKNOWN in `save.md` §3.2, despite live takings/profit fields. Sideshows throw on capture/load instead of inventing offset mappings.
- Staff recruit variants and patrol rectangles have no live implementation; capture writes zero, nonzero input throws. Staff constructor morale/tiredness and transient jobs/claims reset as the format requires. Appearance remains the port's existing staff sprite selection.
- Bank/calendar histories, historical totals, annual rating, class strike deadlines, and original loan principal/term/total-repayable metadata have no live owner. Capture zeroes them and loading nonzero fields for these missing systems throws. The live loan fields (remaining/payment/months/taken) and debt counter are supported; `ParkFinances.MonthsRun`/`LastMonthEnd` are session diagnostics, not wire fields.
- Save is intentionally not an exact frame snapshot: guest identity/positions/targets/correlations and ride motion/riders reset; reliability fractions, narrow fields, older histories and satisfaction mean have the existing format's losses. Sub-day timing restarts at the saved day. No promise of deterministic replay or per-guest equality.
- The proven fixture covers flat rides, one feature, one shop and one queue. Track rendering, every world's assets, and gameplay after an extended restored session are not established by this fixture.

## Validation

- `dotnet test tests/TPW.Sim.Tests/`: **1,501 passed**, zero failed/skipped.
- `dotnet build game/`: **zero warnings, zero errors**.
- Separate-process nonempty file proof and the controls above: passed.
- Mutation audit: **25/25 caught, zero survivors, zero invalid mutants**. See [`savehost-mutations.json`](savehost-mutations.json). Each mutation must compile and be rejected by the live proof; compilation failures are invalid, never counted as kills. The runner restores sources and rebuilds the clean game. Final review moved delayed save after same-frame delayed edits; the Main ordering mutant was caught again and the unmodified full proof rerun, recorded under `post_review_main_recheck`.
