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
