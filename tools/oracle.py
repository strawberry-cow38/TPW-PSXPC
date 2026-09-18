#!/usr/bin/env python3
"""Record an ORACLE FIXTURE from the real game.

A fixture is: a save state + an input script + the trace of what the real game
did next. A reimplementation replays the same inputs from the same state and
must reproduce the trace. That is the thing that can say "you're wrong" without
going through anyone's opinion.

Why named fields and not whole RAM: replay is deterministic in the SIMULATION
but not in BIOS/peripheral scratch (172 bytes of 2 MB drift between identical
runs), so a whole-RAM diff would report a false failure every single time.

  python3 oracle.py record <name> <state> <input.txt> <frames>
  python3 oracle.py check  <name> <state> <input.txt> <frames>
"""
import json, os, struct, subprocess, sys, glob, shutil

TPW = "/home/ec2-user/tpw"
CORE = f"{TPW}/pcsx_rearmed/pcsx_rearmed_libretro.so"
GAME = f"{TPW}/disc/tpw.cue"
FIXDIR = f"{TPW}/fixtures"

# name -> (ram offset, struct fmt). Grows as fields are identified; every entry
# must be something confirmed, not a candidate.
FIELDS = {
    "clock":   (0x103A94, "<I"),   # +1 per sim tick, 25/sec, verified 145 intervals
    "day":     (0x1E8B60, "<H"),   # game day, 99 ticks each
    # REAL balance, found by diffing a buying branch against a non-buying one off
    # the same save. Stored at TEN TIMES the displayed figure (500000 = "$50,000"),
    # which is why every search for 50000 missed it. A path cost 100 = GBP 10.
    "money":    (0x1D565C, "<i"),
    # 0x801D524C tracks it but read 0 once mid-run, so it looks like a transient copy.
    "money_tmp": (0x1D524C, "<i"),
    # these two hold 50000 and do NOT move on a purchase -- not the balance.
    "notmoney_a": (0x1D5694, "<i"),
    "notmoney_b": (0x1D56AC, "<i"),
    "day2":    (0x1E8B5C, "<i"),
    # guest economy: admissions total and overall income (BANK = 0x801D5658)
    "gate_total":   (0x1D6920, "<i"),   # BANK+0x12C8, rises GBP 40 per guest admitted
    "income_total": (0x1D6930, "<i"),   # BANK+0x12D8
    "park_open":    (0x102D30, "<I"),   # 0 shut, 1 open   # increments in exact lockstep with `day`; purpose unconfirmed,
                                   # but deterministic and day-driven, so valid to hold a port to

}

# What a reader of the fixture JSON cannot see from the numbers alone, and gets
# wrong if it is not written down. `money` reads 480800 and means GBP 48,080;
# `gate_total` rises 400 and means one guest at GBP 40, not ten at GBP 4. That
# exact mistake was made against this data, so the legend ships IN the fixture
# rather than in a doc beside it.
UNITS = {
    "clock":        "sim ticks; +0.5 per emulated frame; 99 ticks = 1 game day",
    "day":          "game days elapsed",
    "day2":         "day of month, wraps at 30",
    "money":        "TENTHS of a pound (480800 = GBP 48,080)",
    "money_tmp":    "tenths of a pound; transient copy, read 0 mid-run once",
    "notmoney_a":   "NOT the balance; holds 50000 and does not move on a purchase",
    "notmoney_b":   "NOT the balance; as above",
    "gate_total":   "tenths of a pound, admissions only; +400 = ONE guest at GBP 40",
    "income_total": "tenths of a pound, all income; moves with gate_total here",
    "park_open":    "0 shut, 1 open",
}

def run(outdir, state, script, frames, every):
    shutil.rmtree(outdir, ignore_errors=True)
    os.makedirs(outdir, exist_ok=True)
    env = dict(os.environ, SYSDIR=f"{TPW}/sysdir", LOADSTATE=state, INSCRIPT=script)
    subprocess.run([f"{TPW}/runner", CORE, GAME, outdir, str(frames), str(every)],
                   env=env, cwd=TPW, stdout=subprocess.DEVNULL,
                   stderr=subprocess.DEVNULL, timeout=900)

def trace(outdir):
    out = []
    for f in sorted(glob.glob(f"{outdir}/ram_*.bin")):
        frame = int(f.split("_")[-1].split(".")[0])
        d = open(f, "rb").read()
        out.append({"frame": frame,
                    **{k: struct.unpack_from(fmt, d, off)[0] for k, (off, fmt) in FIELDS.items()}})
    return out

def load(name):
    return json.load(open(f"{FIXDIR}/{name}.json"))

def compare(want, got):
    """Returns a list of human-readable mismatches."""
    bad = []
    for a, b in zip(want, got):
        for k in FIELDS:
            if k not in a:      # field added after this fixture was recorded
                continue
            if a[k] != b[k]:
                bad.append(f"frame {a['frame']} {k}: expected {a[k]}, got {b[k]}")
    if len(want) != len(got):
        bad.append(f"LENGTH differs: expected {len(want)} samples, got {len(got)}")
    return bad

def main():
    mode, name, state, script, frames = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4], int(sys.argv[5])
    os.makedirs(FIXDIR, exist_ok=True)
    work = f"{TPW}/.oracle_{name}"
    path = f"{FIXDIR}/{name}.json"

    if mode == "record":
        run(work, state, script, frames, 600)
        t = trace(work)
        json.dump({"state": os.path.basename(state), "input": os.path.basename(script),
                   "frames": frames, "fields": list(FIELDS),
                   "field_units": {k: UNITS.get(k, "UNDOCUMENTED -- do not lean on this") for k in FIELDS},
                   # ---- SCOPE OF THE GUARANTEE ------------------------------
                   # null until `verify` has actually replayed it. A fixture
                   # RECORDED to N frames has not been SHOWN deterministic to N
                   # frames -- recording it once proves nothing. Non-determinism
                   # ACCUMULATES, so a pass at 1300 cannot speak for 12000: the
                   # short run could not have failed. (cow tools' suggestion,
                   # after a 12000-frame fixture gave FAIL/FAIL/PASS on identical
                   # input while a 1300-frame check had called it reproducible.)
                   "verified_deterministic_frames": None,
                   "verify_runs": 0,
                   "trace": t}, open(path, "w"), indent=1)
        print(f"recorded {len(t)} samples -> {path}")
        print("  verified_deterministic_frames: null  (run `verify` to earn it)")
        for r in t[:4]:
            print("  ", r)

    elif mode == "verify":
        runs = int(sys.argv[6]) if len(sys.argv) > 6 else 3
        fx = load(name)
        want = fx["trace"]
        for i in range(runs):
            run(work, state, script, frames, 600)
            bad = compare(want, trace(work))
            if bad:
                for b in bad:
                    print(f"  MISMATCH {b}")
                print(f"FAIL on run {i+1}/{runs} -- NOT deterministic to {frames} frames")
                sys.exit(1)
            print(f"  run {i+1}/{runs} identical")
        fx["verified_deterministic_frames"] = frames
        fx["verify_runs"] = runs
        json.dump(fx, open(path, "w"), indent=1)
        print(f"PASS {runs}/{runs} -- verified_deterministic_frames = {frames}")

    else:  # check
        fx = load(name)
        vdf = fx.get("verified_deterministic_frames")
        if vdf is None:
            print(f"  ! {name} carries NO determinism guarantee (verified_deterministic_frames: null).")
            print("  ! A mismatch below may be the emulator, not the port. Run `verify` first.")
        elif frames > vdf:
            print(f"  ! ASKING FOR {frames} FRAMES, GUARANTEE IS {vdf}.")
            print(f"  ! Determinism does not stretch -- re-verify at {frames} before trusting a FAIL.")
        run(work, state, script, frames, 600)
        bad = compare(fx["trace"], trace(work))
        for b in bad:
            print(f"  MISMATCH {b}")
        print("PASS" if not bad else f"FAIL ({len(bad)} mismatches)")
        sys.exit(1 if bad else 0)

main()
