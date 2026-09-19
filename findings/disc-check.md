# Why Main Game and Load Game are greyed out: a disc check at boot

**Short version:** the front end greys out **Main Game** and **Load Game** (draws them at 0x202020, not 0x808080)
when a copy-protection check at boot fails. It has nothing to do with the memory card or saved data. On a genuine
disc the check passes and both are available. **The port should draw both enabled.** An emulator whose disc image
lacks the protection's subchannel data fails the check and leaves only the Practice Park.

## The chain (READ)
| step | where | what |
|---|---|---|
| dim the row | overlay 2, both menu draw routines (root menu and the Play Game submenu) | `if (0x800BEBF0() != 0) SetColour(prim, 0x20, 0x20, 0x20)` before one row, back to `0x80, 0x80, 0x80` after. Root menu: its **third** row (Load Game). Submenu: its **first** row (Main Game). Same test for both. |
| the flag | `0x800BEBF0` | returns u32 `[0x80103540]` |
| its only writer | `0x800BEB4C`, called once from the boot sequence `0x800BCEEC` | `CdControl(CdlSetmode, 0)`, then `CdControlB(CdlSetmode, 0x20)` until `CdSync` completes; then up to three times `0x800D5B9C` (writes five COP0 debug registers, DCIC = 0xE1800000 among them), `0x800D5C0C` (`CdControlB(CdlNop)`, installs a callback, `CdControlF(CdlReadS, …)`) and `0x800BE9F8`, stopping at the first 0; the flag is the last result; `CdControl(CdlSetmode, 0x80)` |
| the test | `0x800BE9F8` | loads the disc file **`AAAAAAAA.AAA`** (2,048 bytes), XORs each u32 with `k << 16 \| k` where k is COP0 register 3 read back, and sums the 512 words (`0x800708EC`). Returns **0** if the sum is **0x017F2480**, else 1. |

`0x800D5B9C` and `0x800D5C0C` lie inside the linked Sony library range (0x800C0498..0x800DB838) but match
no PsyQ signature. The CD calls around them do (CdControl 0x800C28D0, CdControlF 0x800C2A0C, CdControlB
0x800C2B40). A library module that sets the debug registers, reads the drive and leaves a 16-bit key in a
COP0 register fits LibCrypt, the PAL copy protection of the time (GUESS-high: the subchannel read itself
was not traced).

## The key (MEASURED)
All 65,536 values of k were tried against the file on the shipped disc. **Exactly one** gives the checksum:
**k = 0x7368**. With nothing in the register (k = 0) the words sum to 0xD181B7B0, so the check fails and
the flag is 1.

## Consequences
- **Port:** Main Game and Load Game are ordinary menu rows. Draw them at 0x808080 like the others.
- **Emulator tests:** a test that needs Main Game or Load Game needs a disc image that passes the check,
  e.g. the disc's subchannel data (`.sbi`) next to the image, which emulators with LibCrypt support read.
  A cold boot with a memory card does not change the flag (tinyclaw, 2026-09-19). A save state does not
  either, because the check runs once at boot.
- Nothing on screen explains the greying. The game has no string for it (tinyclaw).

## Reaching the world map anyway (MEASURED, tinyclaw 2026-09-19)

The check can be stepped over without a `.sbi`, which is how the consequences above were confirmed
rather than reasoned about. `fable/b/runner_b.c` now takes a `POKE=addr:value[,…]` environment
variable and writes those u32s into RAM after every frame:

```
POKE=80103540:0 LOADSTATE=states/mainmenu.state ./run.sh out 1400 150 in_cross.txt
```

With the flag held at 0, **Main Game is drawn at 0x808080 instead of 0x202020**, and pressing it loads
**overlay 11 and the world map** — islands joined by rope bridges, the first being "Lost Kingdom:
Prehistoric World". This is the screen that earlier notes recorded as unreachable and whose overlay
"never appears in RAM"; it never appeared because the menu row was never pressable.

Holding the poke every frame matters: the flag is read live per draw, not cached, so a one-shot write
before the menu appears is overwritten by nothing but is also not required — the per-frame write is
simply the version that cannot be raced.

⚠ **This is a debugging override, not a fix.** It proves what the flag gates; it is not a way to make
the emulator pass the check, and the port should not imitate any of it. The port has no disc to check.

### What the world map turned out to contain
Read from the island table at **0x801141F4** (8 records, 0x1C bytes: world at +6, park at +7, name
string id at +10) — ported in `TPW.Data.WorldMap`:

| world | parks |
|---|---|
| 0 jungle | Lost Kingdom: Prehistoric World / The Park That Time Forgot |
| 1 halloween | Halloween World: Realm of Terror / Halloween: Ghost World |
| 2 fantasy | Wonder Land: Land of Dreams / Wonder Land: Enchanted Island |
| 3 space | Space Zone: The Final Frontier / Space Zone: Star Park |

Parks are opened with Gold Tickets, at most three open at once (string 318), and closing one deletes
everything built in it (string 319). The table's world indices agree with the world-record table at
0x800DDDC4, checked by following each record to the map list it loads (world 2 → maps 34/35, world 3 →
355/356) rather than by matching themes to names.
