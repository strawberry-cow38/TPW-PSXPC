# Park message list port

## §0 SOURCE DISAGREEMENTS

The starting authority is `advisor-presentation.md` §3, not a new trace from the save stream.
One narrower overlapping-deletion case disagrees with that section's unconditional summary.
The implementation keeps the findings' version, as required by `tools/agent-brief.md`.

| Existing source and address | Assembly reading and addresses | Treatment |
| --- | --- | --- |
| `advisor-presentation.md` §3.5, `0x800139EC → 0x8003A65C`: remove the first record carrying the text id immediately. | After finding index `s1`, `0x8003A6A4` calls `0x8003A52C` to flush an already-pending deletion. `0x8003A6AC` then stores the **old scanned index** in `+0x1C`; `0x8003A6B0` calls removal again. `0x8003A590..5F0` compacts/decrements. If an earlier slot was pending, the original match has moved before the second removal. | `Retract` retains §3.5: remove exactly the first match. The code has a SOURCE DISAGREEMENT comment. Animated pending deletion and this overlap case are not implemented; this is not a claim of identical behavior during overlapping deletions. |

No other disagreement was encountered in the bounded checks. Existing stale descriptions in
`ParkAdvisor.cs` (format substitution, caption closing, untraced queue cursor) are already corrected
by `advisor-presentation.md`; they were not changed here. The advisor's author owns that work.

## The 0x124 answer: no record

**READ: 0x124 bypasses the entire advisor card push, not just text rendering.**

- `0x80013F1C..20` tests advisor flag bit 0 and skips the card path when it is clear.
- `0x80013F44` loads the message table's text id. `0x80013F48` loads `0x124`.
- `0x80013F4C` branches on equality to `0x80013F94`. It therefore skips record construction
  (`0x80013F54 → 0x8003A7BC`), three-field setup (`0x80013F6C → 0x800141BC`), the HUD lookup
  (`0x80013F74 → 0x80050530`), and push (`0x80013F80 → 0x800385AC`).
- The branch delay slot at `0x80013F50` tests flag bit 1. The branch target is the **voice gate**;
  voice-only messages still proceed to speech when that flag is enabled.
- Poster retraction has the same sentinel guard at `0x80013A24..28`, in addition to its Text flag.

`tools/prove_messages.py` checks the actual branch instructions/delay slot and all four calls in
that block, then compares the **289** established table text ids with the image. Result: **46**
sentinel messages suppress the record; **243** captioned entries are positive controls. No table
rows are skipped. Only the text-id field of each existing record is rechecked; take/mood data is
outside this proof. The guarded block contains **13 words: 4 direct calls and 9 noncall words**.
All **12** combinations of low flag bits and text ids `0x123/0x124/0x125` are checked: **4** reach
the four calls, **8** bypass them, **0** cases skipped. This is a bounded branch proof, not a claim
to have searched the whole image for alternate producers. See `messages-proof.json` for the image
hash, case results and all 46 advisor message ids.

The guard belongs to **advisor delivery**, not generic append. `0x8003A484` copies its supplied
record without inspecting its text id. Consequently `PushAdvisor` suppresses `0x124`, while
`Push` and `RestoreMessage` preserve it. This does not assert that a non-advisor producer actually
uses that id; it avoids inventing a global filter that would change saved/generic records.

## Component and host contract

`core/TPW.Sim/ParkMessages.cs` owns its list and immutable card payloads. It ports the semantic
fields of the **0x120-byte** record; it is not a binary struct with PSX pointers. Capacity is **32**,
oldest first. A push copies its payload and evicts slot zero only when full. Duplicates occupy
separate slots. Retraction removes the first matching signed-halfword text id; explicit player
deletion removes a chosen slot and compacts. `Tick` is deliberately empty: **no expiry**.

The host supplies `IParkMessageHost`: localization, original-byte text decoding, camera movement,
and advisor replay. Fetching text is deferred to `TextAt`, so no text is formatted, wrapped or
localized at push time. `Activate` runs on OK/Replay, not merely on moving selection. Kind **2**
calls the object camera service and returns `true` to request closing the list; kind **3** calls
replay by text id and leaves it open. Ordinary kind **0** does nothing. No activation deletes a card.
Replay lookup/temporary advisor flags remain the host's job, as documented on the interface.

The inline-text route retains original bytes, including non-ASCII values; no encoding is invented.
The **255-byte** limit follows the 256-byte record buffer/terminator and established save byte
length. Overlong input and missing save target mappings throw as **port validation**, not as a
claim that retail safely rejects bad data. Managed collection encapsulation and opaque `object`
handles are port interface choices. Camera target position/lifetime remains host-owned.

Required calls for the other author (none wired here):

1. Own one `ParkMessages` per park and create a fresh instance when `BeginPark` rebuilds managers.
2. Keep `ParkAdvisor.PostMessage` / `TakeMessage` as the transport to advisor delivery. At the
   existing `ShowCaption` call site, call `PushAdvisor(textId, kind, liveTarget, textEnabled)`.
   Pass the resolved queue kind: **0** ordinary, **2** when an object value was attached, **3** for
   tutorial ids 221..288 without an object value. Kind and object are payload, not format arguments.
   A raw `uint` object token must first be resolved to the host's live object.
3. For the separate poster-retraction hook (`0x800139EC`), look up the advisor message's text id
   and call `RetractAdvisor(textId, textEnabled)`. `TakeMessage` dequeuing is **not** retraction.
   Speech ending or `HideCaption` must not delete cards.
4. Set `ParkHud.Messages` to `Count`, replacing `_advisor.Delivered`. The advisor author removes
   `ParkHud.AdvisorCaption` and `PaintAdvisor` and provides the badge/card presentation.
5. Own L2 open/close, selection, scrolling, cascade/slide state, button labels, sound, and wrapping.
   Use `Records` for membership and payloads, `TextAt` for selected text, and `Activate` for OK/Replay.
   Reconcile selection after a membership change. `Delete` commits a player deletion; the host
   decides its animation timing. The overlapping pending-deletion limitation is explicit in §0.

The assembly was read with `ann.py`, including the short leaves at `0x80014270/278/280` and
`0x8003BB0C`. No prologue-only `fn.py` view was used for those checks. The checks verified the
already-established append/first-match rules and deferred text/action readers as they were used;
they did not repeat the system-wide research in §3.

## Save seam

**The component makes the established message section portable at the core boundary. It does
not make the current game adapter accept messages.** `game/ParkSaveHost.cs` remains unchanged,
including its `RestoreMessage` refusal, and still has no wired list owner.

`CaptureMessages(IParkMessageSaveHost)` returns the existing `ParkSaveMessage` representation in
list order, excluding kind **4**. Only a kind **2** non-null target invokes the save address lookup.
`RestoreMessage` copies original text bytes or retains the string id, resolves any supplied target
through the host, and appends without advisor suppression. `ParkSaveCodec` continues to own the
byte count, signed string id, conditional inline data, type, target type/index and alignment.
The source for those rules is **save.md §3.6**, not newly inferred save semantics.

Exactly what the game integration still needs:

- A live `ParkMessages` owner and lifecycle/reset in `BeginPark`.
- `Capture().Messages.AddRange(messages.CaptureMessages(targetHost))`.
- `IParkSaveHost.RestoreMessage` delegation to `messages.RestoreMessage(saved, targetHost)`.
- An `IParkMessageSaveHost` mapping each linked live object to its **saved type and list index**,
  and resolving that address to the **new** live object after object lists have been restored
  (the established `0x8005BEC0(type,index)` seam). Process pointers and shared object IDs are not
  substitutes for that ordering. Unmapped objects must be diagnosed, not silently stripped.

The nonempty test starts with **9 live records**, captures/restores **8**, and excludes **1 kind-4**
record. **2 target addresses** resolve to distinct new objects; **6 saved records** have no target.
The fixture includes ordinary, inline, object-linked, replay, sentinel and negative-halfword ids.
It goes through the actual `ParkSaveCodec`, with a fresh empty-list control and exact field/byte
assertions. Caller mutation of captured or decoded byte arrays cannot alter live records. No
records in that fixture are left unchecked. It proves the core conversion, not game integration
or arbitrary original memory-card imports. Presentation state is intentionally not serialized.

## Validation

Reproduce from this worktree:

```sh
python3 tools/prove_messages.py
dotnet test tests/TPW.Sim.Tests/
python3 tools/mutate_messages.py
```

Baseline before changes: **1,574 passed, 0 failed, 0 skipped**. There are **26 new test cases**;
each test states what it REJECTS. Final restored full suite: **1,600 passed, 0 failed, 0 skipped**.
Mutation sweep: **59 planned, 59 executed, 59 caught, 0 survivors, 0 invalid mutants, 0 skipped**.
All were caught in the first sweep; there was no survivor requiring a fixture change.
The two pre-existing xUnit analyzer warnings are unrelated to this port.

The mutation runner compiles tests once against unchanged production, then builds only the
mutated production assembly and runs those fixed tests. Both a full-suite passing baseline and
an unchanged targeted control are required. It checks that the expected nonzero number of tests
actually executed; compile errors and missing tests are **invalid**, not kills. It restores the
source and reruns the full suite. `messages-mutations.json` includes source/test hashes, every
replacement, failing test names, counters, and counts of planned/executed/unexecuted mutations.

Scope check: branch **messages**, worktree `/home/ec2-user/tpwport-messages`. All **30** tracked
files in the combined protected set (`game/` and `core/TPW.Sim/ParkAdvisor.cs`) were compared
byte-for-byte with HEAD: **30 unchanged, 0 skipped**. No existing source/report was modified;
the component, tests, proof/runner, and this report/audits are new files in this worktree.

## What this work did NOT establish

- Any live-console rendering or timing capture, including whether words ever appear while closed.
- Exact behavior during overlapping animated player deletion and poster retraction; §0 records
  the disagreement, and the component deliberately retains §3's first-match rule.
- A host implementation of badge/card graphics, selection, cascade/slide/scroll, button labels,
  input, sounds, draw-time wrapping or the selected text widget.
- The advisor replay search/flag changes or live camera movement working in the game; these are
  documented host services, tested here with recording fakes.
- The other author's final advisor queue/card-kind/object-token plumbing or poster retraction hook.
- Live object-to-save-list address mappings, object-destruction handling for linked cards, or a
  successfully saved/loaded nonempty message section through `game/ParkSaveHost`.
- Non-advisor producers of kinds 1/4 or any producer of a generic `0x124` card.
- A wider reader census for record `+0x10C`, or any new lifetime/expiry interpretation beyond §3.
- Whole-image alternate entry paths, malformed retail-record behavior, or arbitrary original-card
  compatibility beyond the existing save codec's documented scope.
