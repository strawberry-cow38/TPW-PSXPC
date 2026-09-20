# Theme Park World (SLES-026.88) — the advisor

`findings/statistics.md` decoded the advisor's **brain**: 72 statistics, 125 rule programs, and the
message ids they post. It stopped at the boundary and said so — *"a complete advisor UI machine …
[is] outside this service."* That sentence is why a port could hold every rule, tested, and never
hear a word of advice: the thing that CALLS the rules is this machine, and nobody had read it.

This is that machine: when he speaks, what he picks, how he says it, and what stops while he does.

## 1. The tick

`0x80013180(advisor)`. It reads a running counter (`0x80050560`), passes the difference since the
last call, **clamps it to 50**, and dispatches on the state byte at `+2` through the 6-entry table at
`0x800DB960`. State 0 always steps by exactly 1.

✅ **The counter is VSYNC FRAMES** (`advisor-presentation.md` §0: `0x80050560` accumulates
`0x800BB380` → `[0x80103A50]`, which the VSync callback at `0x800BB068` increments). So every duration
below is in frames: arriving and leaving are **one PAL second each**, away is **two**.

| state | handler | what it is |
|---:|---|---|
| 0 | `0x80013208` | boot; posts message 189 or 206 (both caption-less) and settles to idle |
| 1 | `0x80013298` | **idle — the only state that refreshes statistics or runs rules** |
| 2 | `0x800133C4` | arriving: 50 frames, SCALE 24/frame to 1200 and SPIN 655/frame to 0x8000 — he grows from nothing while turning four times |
| 3 | `0x8001345C` | speaking: **until the recording ends**, no timer |
| 4 | `0x80013518` | leaving: the same 50 frames, both unwound |
| 5 | `0x800135AC` | away for 100 frames, then idle |

⭐⭐ **THE FREEZE IS THE FINDING.** `0x800132A0` tests flag bit 3 and calls the statistics loop, and
it is reached in state 1 alone. A park's whole sense of what is wrong with it therefore stops the
moment the advisor opens his mouth and does not resume until he has gone and served out his pause —
**about 200 frames — four seconds — plus the length of the recording, per message.** A port that ticks the rules every
frame is not merely cosmetically different: it runs the advice engine several seconds per message
faster than the console can, and it turns a character who visits into a notification feed.

⚠ In state 5, flag bit 4 serves out the pause; without it he is available again immediately. A
waiting interrupt (`+0xB4`) also cuts the pause short.

## 2. The message table — `0x800EE4FC`, 289 records of 20 bytes

Read by `0x80013EFC` (show), `0x800132FC` (latch) and `0x80014024` (mood).

```
+0  u16  textId        string id in FOLIO 0x197; 0x124 = NO CAPTION (0x80013F48), not a gap
+2  u8   count         takes to rotate through, 1..3
+3  u8   cursor        next take -- LIVE: the game writes it back into the record
+4  {u16 line, u16 mood} x 4      line indexes ADVISOR.TPW; mood -> advisor+0x184
```

⭐ **HE NEVER SAYS IT THE SAME WAY TWICE.** `0x80013FE8` advances the cursor after every delivery and
wraps at `count`. 107 of the 289 messages are the statistic rules'; they carry 213 recordings between
them. **46 records caption nothing at all** and are voice-only by design.

⭐ **AND HE DOES NOT STAND THE SAME WAY EITHER.** Starting to arrive calls `0x8001404C`, which rolls
`rand(5)` **in a loop until it differs from the last value** and stores it at `+0x1B1`.

✅ `mood` **is his FACE** (`advisor-presentation.md` §1.2): it picks a sub-mesh of FOLIO entry 0, attached
to the body clip's first listed bone, and 16 means no face at all. It is identical across every take of a
message because it is latched from the message, not the take. Its two
remaining values, 10 and 12, appear ONLY on takes past `count`: **four records (22, 62, 63, 77) hold
a take that was recorded and then cut.**

**The table is 289 long**, established twice without a shared method: record 289 is structurally
garbage (`count` 75), and `0x80013C1C`'s interrupt test is `(id - 221) < 68` — ids 221..288, exactly
its last record.

## 3. The queue — `0x80013D30`, 20 entries of 8 bytes at advisor+8

Write cursor `+0xA8`, read cursor `+0xA9`; equal means empty (which is `AdvisorQueueEmpty`).

⭐ **DEDUPED BY ID.** A new message is compared against every entry from read to write and dropped if
its id is already waiting. A park failing one rule for a week queues the complaint once. On overflow
the read cursor advances too, so the oldest is dropped rather than the newest refused.

`0x80013C1C` chooses the route: with flag bit 5 set, ids **below 221** queue; ids **221..288**
bypass it entirely via `0x80013C80`, which sends him to state 4 mid-sentence (stopping the recording
if he is already speaking) so he can come straight back with the urgent one.

After the recording ends, `0x8001346C..A4` fires completion hooks for ids **141, 221 and 288** only.

## 4. Showing one — `0x80013EFC`

Flag bit 0 gates the caption, bit 1 the voice; they are independent.

* caption: `0x800141BC(buffer, textId, msg+2, msg+4)` then `0x800385AC`. ⚠ The two payload fields are
  substituted into the string by a formatter that has **not** been read. No rule-posted message
  carries one (`0x80014118` builds them with the present byte zero), so the 107 are unaffected.
* voice: `0x8001853C(advisor+0x1B8, line)`, after stopping anything already playing. It resolves the
  XA channel as `(line % 4) * 8 + language` — which is why ADVISOR.TPW has exactly 32 channels for
  8 languages, and why the index (FOLIO 405) holds 452 lines.

## 5. The port

`core/TPW.Sim/ParkAdvisor.cs` is the machine, `core/TPW.Sim/AdvisorMessages.cs` the table
(generated by `tools/dump_advisor_messages.py`), `game/AdvisorVoice.cs` the disc read, and the
caption is drawn by `ParkHud.PaintAdvisor`. `ParkView` is his host.

**Verification** — `TPW.Check --advisor 300` runs a park forward three times and needs all three:

| run | expectation |
|---|---|
| shut park, one ride, statistics **on** | rule 0 fires; message 0 delivered; takes rotate |
| the same park **open** | 4 other messages still post, message 0 **must not** |
| shut park, statistics **off** | nothing posted at all |

⭐ The middle row is the one with teeth. Silence is what the ORIGINAL BUG looked like, so a test that
only checks for silence when disabled would have passed against a port that posts messages without
testing conditions. The open park proves the pipeline is alive AND that rule 0's own condition was
what suppressed the message.

Live, `--advisor-say=0` on a loaded park delivers at tick 51 — one tick to notice, fifty to arrive —
and draws the caption the string table gives for text 0x3CF.

## 6. What is not read

Everything below is now in `advisor-presentation.md`:

* what consumes `mood` (`+0x184`) — **read**: a face sub-mesh of FOLIO entry 0, attached to the body
  clip's bone (advisor-presentation.md §1.2, §2); `+0x1B1` selects the body clip; `+0xE8`/`+0xEC` are a
  uniform scale and a spin in the screen plane, drawn by `0x800136F4` from the renderer, not the tick (§1)
* the caption formatter's codes (`0x800141BC`) — **read**: there are none; it stores text id, card kind
  and object pointer (§4)
* the text box's own art (`0x800385AC`) — **read**: it is not a box; the caption becomes a card in the
  HUD's message list, readable in a 260-px box only when the list is opened with L2 (§3)
* whether the elapsed counter is in ticks or frames — **read: VSync frames** (§0): arrive/leave are
  1.0 s each, the pause 2.0 s, and §1's "about 200 ticks" is 4 s
* where the queue's read cursor advances — **read** (`0x80013E6C`, advisor-presentation.md §0): on
  delivery, when the arrival completes, exactly where the port advances it
