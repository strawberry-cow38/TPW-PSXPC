# Measured live, on the real game

Everything here was read out of the running game's RAM on the emulator, not read
out of its code. That distinction is the point of this file: fable's reports in
`fable/` are **derived** (someone read the disassembly and concluded), these are
**measured** (the game was run and the value observed). Where the two agree, say
so; where they disagree, this file is the one with a reproduction.

Every number below carries its unit, its resolution, and what it cannot see.

---

## Clock

**0.5 clock ticks per emulated frame.** Exactly, zero variance, across all three
fixtures and every 600-frame interval in them (19 intervals in `park_with_ride`).

On PAL that is 25 ticks/sec, so `tick_s = 0.04` — **but the 0.04 depends on the
50 Hz assumption, not on the measurement.** The measurement is the ratio. If the
port ever targets NTSC the ratio holds and the 0.04 does not.

## Game day

**Exactly 99 clock ticks = 198 frames.**

Resolution: measured at 2-frame (1-tick) sampling across two consecutive
rollovers, day 120→121→122 at clock 12090 and 12189. Delta exactly 99.

⚠ **This one nearly went out as 100.** At 600-frame sampling the day advances +3
per interval, which reads as 100 ticks/day, and only a single +4 in eighteen
intervals says otherwise. The coarse instrument could not separate 99 from 100
and reported the wrong one with a perfectly straight face. Agrees with fable's 99.

## Admission

**£40 per guest.** `gate_total` rises 400 per admission.

**Stored at 10x the displayed figure**, like `money`. Proven rather than assumed:
over 12000 frames `money`, `gate_total` and `income_total` all moved by exactly
+3200. If gate were in units of £1, money would have moved ten times as far.

⚠ I first reported this as 80 admissions. It is 8. I divided stored units by the
displayed price — right numerator, wrong denominator's unit.

## Arrival rate (park_ride.state: one ride, nothing else)

**Periodic, not stochastic.** ~705 clock ticks between admissions — three
arrivals, gaps of 710 and 700, at 10-tick resolution. Roughly one guest per 7
game days.

⚠ **Scope:** this is the rate for *this park*, not a constant of the game. A park
with more built should arrive faster; that is the thing to vary next. What
generalises is the *shape*: for a fixed park the interval is constant, so the
arrival model is a rate, not a dice roll.

⚠ At 600-frame sampling the gaps look like an alternating 1200/1800, which is an
aliasing artifact of a constant ~1410-frame period. Same failure as the day
period, one measurement apart.

## Addresses (PAL SLES build)

| what | address | notes |
|---|---|---|
| clock | `0x80103A94` | +1 per sim tick |
| day | `0x801E8B60` | +1 per 99 ticks |
| day-of-month | `0x801E8B5C` | same cadence, wraps at 30 |
| money | `0x801D565C` | **x10** stored |
| gate total | `0x801D6920` | x10, +400 per admission |
| income total | `0x801D6930` | x10, tracks gate here |
| park open | `0x80102D30` | 0 shut, 1 open |

⚠ Offsets are for the PAL build only, and are the reason the port should
checksum the user's binary rather than assume one.

## Determinism

Replays are deterministic **only to the length actually verified**, and each
fixture now carries that length in `verified_deterministic_frames`. Currently
1300 / 1500 / 12000, three replays each.

This needs the dynarec compiler thread off (`pcsx_rearmed_drc_thread=disabled`);
with it on, a 12000-frame fixture gave FAIL/FAIL/PASS on identical input while a
1300-frame check called the same setup reproducible.
