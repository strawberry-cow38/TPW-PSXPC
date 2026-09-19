# tools/

Analysis scripts behind the findings in `measured.md`. Everything here runs against the
disc or against emulator dumps; nothing here is part of the port.

| script | what it does |
|---|---|
| `animtracks.py` | decodes model animation tracks (types 8 and 6) and self-verifies; exit 0 only if every record passes |
| `render_rects.py` | renders every GPU-sampled sprite using the CLUT the game asked for |
| `big.py`, `big2.py` | render selected sprites at full size, filter by dominant colour |
| `discsearch.py`, `fullblock.py`, `fullscan.py` | find VRAM blocks verbatim in the de-interleaved disc image |
| `vramstab.py`, `vramstab2.py` | which VRAM blocks change across captures / within one capture |
| `mkfix.py` | builds `hardware_draw_fixtures.json` from the CLUT logs |
| `patch_*.py` | the emulator instrument patches (also exported whole as `instruments/tpw-instruments.patch`) |

**These lived in `/tmp` until now, which on this box is tmpfs.** A reboot had already
destroyed one working script earlier in the session. Anything worth keeping goes here.

Prerequisites some scripts assume:
- `/tmp/tpw_userdata.bin` — the disc de-interleaved to 2048-byte user data
  (`python3 -c` over the .bin, 2352-byte sectors, payload at offset 24)
- `ext/rip/e*.bin` — the 268 `0x96` mesh containers, extracted by entry number
- emulator dumps under `/tmp/vs_*/` from `runner`
