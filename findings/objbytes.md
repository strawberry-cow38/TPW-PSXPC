# Objective bytes, world records, and the remaining Gold Tickets (PAL SLES-026.88)

READ means executable/overlay instructions or disc data, not a console measurement. TPW.BIN is
loaded at **0x80010000**; all twelve expanded overlays use **0x80114158**. Reproduce with
`PYTHONDONTWRITEBYTECODE=1 python3 tools/audit_objbytes.py`. The complete census and fixtures are
in [objbytes-audit.json](objbytes-audit.json); candidate dispositions and gap controls are in
[objbytes-references.json](objbytes-references.json).

**Results:** **0 of the remaining 30 objective bytes acquire semantic names. All 30 lack a reader
in the recovered objective-pointer provenance slice**, with the stronger, fully accounted search
below. The three missing world pointer/count pairs select **grass, path and queue sprites**.
The nine non-weekly minigame checks account for **20 park tickets**; together with **24 main-goal
tickets, one tutorial ticket and five shared bonuses**, they explain the advertised **50**.
There is **no terminal campaign-victory branch in the bounded ticket/completion consumers**.
This is not a claim to have proved arbitrary whole-program aliasing or played through the campaign.

## §0 SOURCE DISAGREEMENTS

No new computational disagreement was found. Existing findings and their retained implementations
stay in force. One previously recorded misidentification is encountered at the new API boundary:

| Existing source | This read, with addresses | Retained behavior / boundary |
|---|---|---|
| `debug.md` §4 calls **0x800B9A6C** a year-end/calendar routine; `goals.md` §0 already records the disagreement | **0x800BA45C** creates nine minigame classes, writes their ID to **object+0x24 at 0x800BA5C0**. Their overlay win paths call **0x800B9A60**, which checks that ID's **per-park bit ID+4** and awards a ticket at **0x800B9AB8**. All nine callers are identified below. | Leave the old findings text and existing calendar behavior unchanged. Add an explicit host-called `AfterMinigameWin` seam; do not replace a calendar routine or introduce a year-end award. |

The two inherited **security-count versus coverage** and **LEVEL-price versus definition-selector**
disagreements in `goals.md` §0 remain unchanged in `AfterDay`. In particular, the retained security
count cannot exceed 80 in the retail 45-feature pool. Matching the **advertised** total is not a
claim that the retained C# rules make all 50 tickets achievable in ordinary play. The earlier
opaque bankruptcy/end-hook contract is also unchanged; advisor and message code are untouched.

`scenario.md` identifying +31 after `goals.md` is a refinement, not a contradiction. The new
`ParkObjectiveDefinition.AdvertisedGoldTickets` property exposes that already established byte.

## 1. The 30 bytes: bounded unread data, not thirty guessed names

The eight 52-byte records are still at **0x800E1930 + 0x34×(2×world+park)**. No new schema is
substituted for them. Every complete byte survives `CopyRecord()`.

| Offset range | Bytes per record | Result |
|---|---:|---|
| **+00..0B** | **12** | Three opaque words; no reader in the proven selector-consumer slice. The two asset-ID value matches still do not name a use. |
| **+20..2F** | **16** | Four opaque words, all `0xFFFFFFFF` in each of **8** records. No reader in that slice. |
| **+32..33** | **2** | Both zero in each of **8** records. No reader in that slice; not renamed padding. |
| **Total** | **30** | **0 newly named; 30 still semantically uninterpreted.** |

The complete recovered pointer path is:

* Selector **0x80067CD8** has **3 direct call sites**: wrapper **0x80067DD8**, OVL11
  **0x80117154**, OVL11 **0x801171A0**.
* Wrapper **0x80067DBC** has **2 direct call sites**: descriptions **0x800676FC** and weekly
  evaluation **0x80067944**.
* All **10 uses** of the retained pointer registers in the two main-image consumers are
  accounted: **9 loads plus the null check at 0x80067954**. There is no pointer store, pointer
  arithmetic, or forwarding of that pointer to a further callee in these ranges. The audit
  checks every instruction in **0x80067714..0x800677A4** and **0x80067954..0x80067CC4**,
  including delay slots. The two OVL11 return values each feed one byte load directly.

All **11 load sites**, covering **22 distinct record bytes**:

| Field | Description reader | Evaluator / other reader |
|---|---|---|
| +0C u32 admissions | **0x80067728** | **0x80067980** |
| +10 word profit pounds | **0x8006775C** | **0x800679C8** |
| +14 u32 years | **0x80067790** | **0x80067A60** |
| +18 u32 feature value | — | **0x80067BF0** |
| +1C u32 maximum paths | — | **0x80067C8C** |
| +30 u8 tutorial flag | — | **0x80067A90** |
| +31 u8 advertised tickets | — | OVL11 **0x8011715C / 0x801171A8** |

**Search accounting:** TPW.BIN **266,327 aligned words**, twelve overlays **30,634**, combined
**296,961**. **0 aligned words skipped, 0 trailing bytes, 0 overlays skipped, 0 records skipped.**
The direct-call and raw-pointer scans include data as candidates rather than excluding presumed
non-code. Objective-range raw pointer words and GP accesses both have **0 candidates**.
Positive raw-pointer control: **0x800F322C → 0x80102E88**, the known research-override pointer.
Positive GP-reader control: **0x8006BE3C → 0x80103988**, the lifetime-ticket getter.

The stronger address-pair scan retains **every earlier same-register LUI high half**, with **no
16-word cutoff and no branch-unsafe register-clobber pruning**. It produces **230 objective-range
candidates**, all classified, **0 unclassified**:

* **8** real selector return addresses, the required positive control.
* **220** stale-high false matches: an explicit local **LUI 0x8010** binds the actual base;
  **218** are immediately adjacent, **2** have one intervening instruction. Their actual
  addresses are **0x80101Axx**, not **0x800E1Axx**. The audit records each local LUI and address.
* **2** data words at **0x800F32A4 / A8** in the character-conversion table **0x800F32A0**.
  Readers **0x8006F270 / 0x8006F2F0** use four-byte rows, not MIPS loads from objectives.
  Executed controls convert **'A' ↔ 0x8260** and **'a' ↔ 0x8281** through those original leaves.

**Limit:** this closes the named selector path and conventional literal/GP references. It does
not prove that arbitrary multi-step address synthesis, an unrecognized indirect call, dynamically
written code, or a different executable could never access these bytes. “Uninterpreted and unread
in this bounded path” is the result; “unused padding everywhere” is not.

## 2. What the world record carries

Initializer **0x8002ED50** constructs the records addressed by **0x800DDDC4**:
world 0 **0x8010558C**, 1 **0x801054DC**, 2 **0x8010542C**, 3 **0x8010537C**.
Its logical copied size is **0xAC**; address spacing is **0xB0**.

| Record offset | Layout | Established consumer |
|---|---|---|
| +00/+04 | Map/scenery-pair pointer and declared count 2 | +00 read by **0x80054760 / 0x80054794**, index by park×8; +04 runtime reader unestablished |
| +08..17 | Two ride-3 list pointers and two counts | **0x8006A214**, entry getter **0x8006A3CC** |
| +18..27 | Tour-7 lists | **0x8006A244** |
| +28..37 | Track-6 lists | **0x8006A274** |
| +38..47 | Upgrade-8 lists, inner stride 8, entry/runtime-handle pairs | **0x8006A2A4**, **0x80069790..D4** |
| +48..57 | Coaster-1 lists | **0x8006A2D4** |
| +58..67 | Feature-2 lists | **0x8006A304** |
| +68..77 | Shop-4 lists | **0x8006A334** |
| +78..87 | Sideshow-5 lists | **0x8006A364** |
| **+88/+8C** | **Grass sprite s16-list pointer / count 2** | pointer **0x8005480C**; count **0x80054898**; random index modulo count **0x800548A4..C8** |
| **+90/+94** | **Path sprite s16-list pointer / declared count 16** | pointer **0x80054834**; indexed signed-halfword load **0x8004DC04..0C**, selected by **0x8004DBE0** |
| **+98/+9C** | **Queue sprite s16-list pointer / declared count 4** | pointer **0x8005485C**; same indexed load, selected by **0x8004DBF0** |
| +A0 | Ground texture FOLIO entry | **0x800547E4**, load **0x8005893C..48** |
| +A4/+A8 | Extra texture FOLIO entries indexed by park | **0x80058924..34** |
| **+AC..AF** | **Four-byte spacing gap; no field meaning assigned** | Not copied/initialized by **0x8002ED50** |

The path and queue **pointer meanings** have consumers. Their adjacent **16/4 values are declared
lengths installed by the initializer**; no runtime consumer of +94/+9C is established here. The
same limitation applies to the map-pair count at +04. These count labels are retained from
`scenario.md`; matching array lengths alone does not establish runtime semantics. Do not
invent bounds checks for **0x8004DC0C**, which simply indexes the selected halfword list.
The established catalogue restrictions and all **64** list counts remain in `scenario.md`;
these sprite IDs are texture-sheet indices, not additional FOLIO attraction entries.

All **12** sprite lists, **88 halfwords total**, are recorded in the audit:

| World | Grass pointer / 2 sprites | Path pointer / 16 sprites | Queue pointer / 4 sprites | Ground / extra textures |
|---|---|---|---|---|
| 0 jungle | **0x80102DE8**: 166,167 | **0x800E14D4** | **0x80102DF4**: 213,212,210,211 | 258 / 169,170 |
| 1 halloween | **0x80102E30**: 145,145 | **0x800E1534** | **0x80102E3C**: 230,230,230,229 | 168 / 91,92 |
| 2 fantasy | **0x80102E00**: 198,198 | **0x800E14F4** | **0x80102E0C**: 236,235,233,234 | 82 / 17,18 |
| 3 space | **0x80102E18**: 152,152 | **0x800E1514** | **0x80102E24**: 169,169,167,168 | 400 / 332,333 |

`paths.md` already identifies the queue/grass use; these reads agree with it. The random-grass
audit executes all **4 worlds × 4 RNG values**, including world 0's distinct 166/167 control.
Repeated sprite IDs in the other worlds are retained. A second initializer run starts each entire
176-byte destination with **0x5A**: **172 bytes in each of 4 records** become the expected record,
while **all 16 gap bytes** remain 0x5A. That is a positive-write control for the gap negative,
not proof that no later code can touch the gaps.

## 3. World selection and ticket spending

The selection UI is **overlay 11**. It has **19 compiled 28-byte rows** at **0x801141F4**:
**8 park nodes and 11 links**. This is separate from the 172-byte world records and the 52-byte
objective records. Node bytes **+6/+7** are world/park; constructor **0x801145BC..CC** copies
them to live-node **+0A/+0B**. The rows are ordered `(0,0),(0,1),...,(3,1)`; park-name text IDs
are **3B1,3B2,175,177,2F0,2F1,196,197**. Thus selection is a park node, with its world supplied
by the node, rather than a campaign “world complete” flag.

Each link's source bytes **+18/+19/+1A** hold its two node indices and ticket cost, copied at
**0x80114648..64**. The exact **11 undirected links** are:

| Nodes | Cost | Nodes | Cost |
|---|---:|---|---:|
| 0–2 | 1 | 0–4 | 1 |
| 4–2 | 1 | 2–1 | 2 |
| 4–1 | 2 | 1–6 | 2 |
| 1–3 | 2 | 6–3 | 2 |
| 6–5 | 2 | 3–7 | 2 |
| 5–7 | 2 | — | — |

At **0x801154D0..D8**, selected node status **2** takes the locked-park path; otherwise the
selected-node byte **object+0x33** changes immediately. **0x80115528..A0** walks all **11**
links and matches the current/target endpoints in either order. **0x801155AC..C4** compares
the resulting cost with spendable tickets. Confirmation **0x801172EC..378** repeats the link
lookup, changes the selected node, calls the unlock/close-state operation, and spends that
cost through **0x8006C024**. It does not compare earned tickets against the park's advertised total.
UI reachability beyond these decoded cases was not exercised on a console.

Node statuses are loaded for all **8** parks by **0x8011512C..48 → 0x8006BD74**. The saved
slot initializer **0x8006C83C..4C** writes status **2**. **0x8006BC68** makes a park status
**0**; **0x8006BCE4** makes it **1**, freeing an existing park buffer when closing it.
The UI counts status-0 parks at **0x80116F70..A8** and blocks another opening when the count is
**3**, **0x801173D4..EC**. These are open/closed/unopened states, not goal completion states.
Campaign initialization opens node 0 via **0x8006BB14..20**. Closing does not call the objective-bit
setter or clear the separate eight-word award array in these operations.

On entering a park, **0x80114A00** returns node world (+0A), **0x801149DC** returns park (+0B).
Main-image **0x800BCAF4..0x800BCB14** copies both out of the UI, and **0x800BCB9C..A8** passes
them into the park constructor via **0x80050524**. Park entry **0x80057ED4 / 0x80057EE0** stores
park/world into **0x801038A4 / 0x801038A0**. The established map, world-list and objective selectors
then read those globals. Sandbox takes the explicit zero/zero branch **0x800BCB90..98**.

## 4. All nine non-weekly award paths in the recovered grant census

The interaction path reads **attraction descriptor+0x16 as u16**, leaf **0x80023C0C**. Caller
**0x8002389C..A8** retains that ID, and interaction activation calls **0x80058F90** at
**0x80023988**. **0x800BA45C** uses the ID to load an overlay and instantiate its class;
**0x800BA5C0** stores the ID at **minigame object+0x24**. The ID-to-overlay byte table is
**0x800F9260**; the nine constructor cases are **0x800E70F8**, dispatched at **0x800BA494..B4**.

The following are **completion tests inside the active minigame**, not conditions polled by the
weekly park evaluator. Offsets below are from the minigame object, except the kart's separate
transport object. Exact boundary fixtures execute the original instructions for every row.

| Game ID / per-park bit / overlay | Game | Win trigger and readers | Grant-helper call |
|---|---|---|---|
| **1 / 5 / 10** | Idol Smash, Bone Crusher, Worm Bash, Martian Mash | u16 timer **+12A** decrements to **0**, then signed s16 hit count **+12E >=21**; **0x801146A4..CC**. Hits increment at **0x8011464C..58**; timer initializes to **750** at **0x80114464..68**. | **0x801146D4** |
| **2 / 6 / 9** | Strength Flower / Strength Rocket | In the result animation, signed word displayed gauge **+104 >= target +100**, then **target >=104**; **0x80114A38..48**, **0x80114AC8..CC**. Punch capture copies strength **+BC** and caps it to 104 at **0x801149E4..0x80114A00**. | **0x80114AE4** |
| **3 / 7 / 8** | Strength Bird | Same terminal threshold with **signed halfwords**: gauge **+DE >= target +DC**, then **target >=104**; **0x80114918..28**, **0x801149B0..B4**. | **0x801149BC** |
| **4 / 8 / 6** | Dino Racing | After all **5** motion-update results sum to zero, completed races **word +D8 >=5** and successful bets **word +E4 >=3**; **0x801143E8..0x80114434**. Selection matching the winning dinosaur increments wins at **0x80114940..68**. | **0x8011443C** |
| **5 / 9 / 5** | Giant Puzzle | Moving byte **+F4 ==0**, then all **8** signed-halfword tile values **+BC+6×i == i+1**, i=0..7; **0x80114850..98**. The ninth/hole slot is not tested. | **0x801148A0** |
| **6 / 10 / 1** | Fortune Teller | Random answer **random(20)+1** equals saved target **word +B8**; target also initialized with **random(20)+1** at **0x801144EC..0x80114504**, answer produced at **0x80114570..80**. Compare **0x80114298..A0**, then require per-park **bit 10 clear** at **0x801142B4..BC**. | **0x801142C4** |
| **7 / 11 / 4** | Go-kart race | Selected kart has **u8 +81 !=0** (finished getter **0x800AA23C**, called **0x8011462C**) and **u8 +0C ==1** (rank, **0x80114644..4C**). This is not owning/building the track. | **0x80114654** |
| **8 / 12 / 0** | Sun Shooter | Signed s16 good-target count **+2D8 >=6**, and local state **s16 +28 !=3**; **0x801149B4..D0**. Good hits increment at **0x80114C34..44**; hitting the bad target resets the count at **0x80114CB0**. | **0x801149D8** |
| **9 / 13 / 7** | Pumpkin Shy / Fruit Shy | At the finish state, active-ball count **word +1E0 ==0**, then success count **word +1E4 ==3**, strictly equality; **0x80114A98..B0**. The finish-state transition happens at **10 throws or 3 successes**, **0x80114AD8..0x80114B0C**. | **0x80114AB8** |

These **9 call sites** are the complete direct-call census for **0x800B9A60** across **13 images**.
The audit executes **43 trigger fixtures**, with at least one passing and one failing case for
each of **9 games**. It tests the 20/21 and 103/104 boundaries, signed negatives, pending movement,
timer underflow, the eight-slot puzzle loop with first/last-slot failures, rank 1/2, fortune replay suppression,
five/six targets, and coconut count **2/3/4**. It does not emulate complete input, physics or rendering.

**Shared win behavior, READ:** **0x800B9A6C** checks sandbox. Outside sandbox, **0x800B9A84..94**
tests **McAi+20 bit (game ID+4)**; **0x800B9AB0** latches it before **0x800B9AB8** grants one
ticket. First awards select text **0x234**, advisor message **0xC5**. Sandbox/replay wins select
text **0x298**, message **0xC3**, and neither grant nor latch. Both texts say “Play again?”
The helper updates its presentation state to **4** and configures replay buttons/sound.

**No type-2 message-list insertion on this path.** **0x800B9B10 → 0x800BA5E4** with argument
2 destroys the temporary advisor-message object; it is not **0x800693C8**, the weekly list route.
All **27 common-helper fixtures** execute the native bit leaves and ticket incrementer, with
only external presentation calls stubbed. Each of **9 IDs** has fresh/replay/sandbox controls;
bonus and unrelated high park bits remain intact. Fresh fixtures take lifetime tickets **49→50**
without an additional campaign effect. Fortune's extra caller precheck suppresses the common
helper entirely on a later matching answer; a host must preserve that caller rule.

Failure helper **0x800B9B64** sets text **0x25D**, message **0xC4**, without a ticket. The new C#
seam handles the common **win** helper; it does not implement the nine minigames or failure UI.

### The advertised totals reconcile without the opaque objective fields

The catalogue audit examines **all 253 ordinary occurrences**, including **233 zero-game-ID
occurrences**, excluding and counting **10 type-8 upgrade records** whose format is different.
There are **20 nonzero minigame IDs**, with no duplicate game ID inside any one park:

| World,park | Minigame IDs | Main goals | Tutorial | Minigames | +31 total |
|---|---|---:|---:|---:|---:|
| 0,0 | 7,1,4 | 3 | 1 | 3 | **7** |
| 0,1 | 5,8,3 | 3 | 0 | 3 | **6** |
| 1,0 | 1,9 | 3 | 0 | 2 | **5** |
| 1,1 | 7,5,6 | 3 | 0 | 3 | **6** |
| 2,0 | 9,5 | 3 | 0 | 2 | **5** |
| 2,1 | 7,1,2 | 3 | 0 | 3 | **6** |
| 3,0 | 2,5 | 3 | 0 | 2 | **5** |
| 3,1 | 7,1 | 3 | 0 | 2 | **5** |
| **All 8** | **20 occurrences** | **24** | **1** | **20** | **45** |

Add the **5** shared bonuses: **50**, exactly the OVL11 display. This demonstrates that no
additional use of +00/+04/+08 or +20..2F is required to account for the advertised ticket sources.
It does not derive meanings for those fields from their values.

**Other ticket modifications:** the grant incrementer **0x8006BFE4** has exactly **3 direct
callers**: weekly wrapper **0x800677C8**, minigame helper **0x800B9AB8**, and OVL11
**0x80114D8C**. The last grants **2**, gated by **[0x80103744] !=0**, pad-1 held mask **0x80**
and newly pressed mask **0x200**, **0x80114D54..90**. This is a cheat/input route, with no award
bit or objective requirement. Separately **0x8006BE1C** forces spendable tickets to **255** when
**[0x80102EC0] !=0**, **0x8006BE1C..30**; it does not increase lifetime tickets.

Both flags have input-table producers, not just inferred cheat-like behavior. The **6** 12-byte
records at **0x800F320C** feed matcher **0x8006E22C**. Its completion path toggles the pointed-to
word at **0x8006E2E0..300**. Record **0x800F323C**, sequence **0x800E1AF8**, toggles
**0x80103744** after **4 repetitions** of masks **4,1,8,2,0x400,0x2000**. Record **0x800F320C**,
sequence **0x800E1AD0**, toggles **0x80102EC0** after **4 repetitions** of masks
**1,2,4,8,0x100,8,4,2,1,0x100**. The matcher reads pad index **0**, **0x8006E23C..50**;
the enabled world-map +2 action above reads pad index **1**. These indices are preserved without
guessing physical pad/button labels. The audit decodes **6 records**, executes **3 toggle effects**
(these two plus the established research control), and leaves **3 unrelated effects unexecuted**.
Each executed sequence has a wrong-input control and toggles **0→1→0**. The +2 action has **4**
fixtures: flag clear, missing held mask, missing pressed mask, and successful grant. The 255
override has **2** fixtures and preserves lifetime 49 in both. This establishes code-level input
activation, not a console measurement of retail reachability.

## 5. Terminal campaign victory: the bounded negative

**No all-tickets/all-worlds terminal transition is present in these recovered consumers.**
This is stronger than an unsuccessful search for a “won” string:

1. **Grant routes:** all **3** callers of **0x8006BFE4** are accounted above. The incrementer
   just adds to spendable/lifetime words and calls the HUD helper **0x80037FBC**. That short
   helper **0x80037FBC..FE4** resets animation counters, not a win predicate. The **9 weekly**
   grant calls and **9 minigame** calls finish with their individual effects.
2. **Lifetime total:** getter **0x8006BE3C** has exactly **1 direct consumer**, **0x80013248**.
   It chooses startup greeting **0xBD for zero / 0xCE for nonzero**, **0x80013250..64**;
   there is no threshold of 50. Other live-word accesses are initialization, increment and
   card serialization, recorded individually in the audit.
3. **Spendable total:** getter **0x8006BE1C** has **4 direct consumers**: ticket-menu availability
   **0x80075EEC**, ticket-shop balance **0x80083B78**, map affordability **OVL11 0x801155AC**,
   map balance formatting **OVL11 0x80116A3C**. The shop carries its copy into a price comparison
   and subtraction at **0x80083CDC..CF0**. None treats this balance as campaign completion.
4. **Completion masks:** saved per-park popcount **0x8006BEF0** counts bits **0..14**, shared
   bonus popcount **0x8006BE60** counts **0..4**. Their callers are the aggregate and the selected
   park/bonus display: **0x8006BF78 / 0x8006BFA4**, OVL11 **0x80117190 / 0x801171AC**.
   Aggregate **0x8006BF4C** has exactly **1 direct consumer**, OVL11 **0x80117180**: formatting
   acquired/advertised and remaining/total at **0x801171C4..E4**. The separate live-McAi bonus
   count **0x800675F4** has **1 direct consumer**, **0x80082FC8**, drawing bonus icons through
   **0x80083018..60**. It does not dispatch an ending when the count is five.
5. **Storage access:** the eight-word array **0x80109B18** has **6 literal-address producer
   sites**: initialization **0x8006BA80**, setter **0x8006BEA8**, getter **0x8006BED0**, popcount
   **0x8006BF10**, card writer **0x8006C290**, card reader **0x8006C6B0**. The current-McAi
   bit tests/setters lead back to descriptions, the nine weekly checks, minigame wins, and the
   bonus-icon count. No additional mask-completion branch was found in these consumers.
6. **Known ending, positive control:** the search finds **2 calls** to **0x800BCEA0**,
   **0x8001347C / 0x800136D8**. Both are gated on advisor message **0x8D**, the established
   bankruptcy route. Its event reaches the **single** direct **0x800BCD00** call at
   **0x800BD2E8**, which plays the known END.STR sequence. Minigame message 0xC5 does not satisfy
   either 0x8D comparison. The existing opaque-hook contract remains as described in §0.

The supplemental scan checks raw pointer words **and constructed LUI/addiu-or-ori addresses**
for the grant wrappers, incrementer, lifetime getter and aggregate/bonus counters, across the same
**296,961 words**. It finds **0 indirect-pointer candidates** for those **6 functions**. Positive
constructed-function control: callback **0x800BA814** is found at **0x800BA6CC**. An eight-park
synthetic complete mask plus all five bonus bits executes the original aggregate and OVL11
formatting slice: **earned 50, total 50, selected park remaining 0 of 7, bonus remaining 0**.
The audit also checks **7 sparse/high-bit fixtures** so “all flags count” cannot pass vacuously.

**Bound:** normal objective/minigame grant paths, named total/mask accessors and their direct
consumers, world-map selection/status transitions, and the known end-sequence dispatch. Arbitrary
computed aliases/indirect targets, unrelated front-end event producers and complete dynamic gameplay
are outside the proof. The result warrants **no new campaign Won flag or transition in this port**;
it does not warrant saying no conceivable ending exists anywhere on the disc.

## 6. Additive port and validation

`ParkObjectives.cs` retains its existing constructor, `State`, `Restore`, descriptions, and weekly
evaluator. `AfterMinigameWin(id, restrictedMode)` adds the common helper through those same per-park
bits and `ObjectiveAward`; it needs no objective record or calendar gate, matching the native
helper. It returns feedback even without an award. `ObjectiveAward.AddToMessageList` defaults true
for existing weekly events and is false for minigames. The host validates constructed IDs 1..9;
native invalid-ID behavior is not invented. No wallet, advisor, message list, scene transition,
minigame simulation or world selection UI is wired here.

`ParkObjectiveDefinition` adds only the previously read +31 property. No unknown bytes are given
behavior. Every new test says what it rejects. The interpreter gains one instruction, **SLLV**,
so the new audit executes the original bit leaves rather than stubbing their arithmetic.

Reproduce:

```
PYTHONDONTWRITEBYTECODE=1 python3 tools/audit_objbytes.py
dotnet test tests/TPW.Sim.Tests/
PYTHONDONTWRITEBYTECODE=1 python3 tools/mutate_objbytes.py
```

The sweep includes **109 inherited mutations and 19 new mutations**, with frozen tests and exactly
**93 objective tests** required per mutant. Compile errors, empty/partial test runs, changed test
assemblies and skips are invalid, never kills. [objbytes-mutations.json](objbytes-mutations.json)
records every mutation, baseline/final full suites, hashes and restoration. **128/128 mutations
killed; 0 survived, 0 invalid.** Source restoration is verified. The final restored
`dotnet test tests/TPW.Sim.Tests/` run passed **1,711/1,711**, **0 skipped**, up from 1,688.

## 7. Not established

* Semantic meanings for **all 30 remaining objective bytes**, or a whole-program alias proof.
* A field purpose for **world+AC..AF**, or later use of those gaps. Runtime readers of the path
  and queue declared count words **+94/+9C**, and the map-pair count **+04**, remain unestablished.
* An ending outside the explicit campaign-consumer boundary above; no console campaign-completion
  measurement, save deletion rule, or new terminal-state contract.
* Full minigame physics/input/timing and playable C# minigames. The recovered triggers are exact
  completion decisions; the added API starts at their common win helper. Failure/replay UI and
  Fortune Teller's caller suppression remain host responsibilities.
* Physical pad names or console reachability of the world-map +2-ticket cheat; possible ticket changes
  through arbitrary computed aliases or dynamically supplied code.
* Host/game wiring, world-menu implementation, or new save ownership. `game/`, `ParkAdvisor.cs`,
  `ParkMessages.cs`, and `RidePanel.cs` were not modified.
