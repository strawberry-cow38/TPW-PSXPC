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
    "day2":    (0x1E8B5C, "<i"),   # increments in exact lockstep with `day`; purpose unconfirmed,
                                   # but deterministic and day-driven, so valid to hold a port to

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

def main():
    mode, name, state, script, frames = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4], int(sys.argv[5])
    os.makedirs(FIXDIR, exist_ok=True)
    work = f"{TPW}/.oracle_{name}"
    run(work, state, script, frames, 600)
    t = trace(work)
    path = f"{FIXDIR}/{name}.json"
    if mode == "record":
        json.dump({"state": os.path.basename(state), "input": os.path.basename(script),
                   "frames": frames, "fields": list(FIELDS), "trace": t},
                  open(path, "w"), indent=1)
        print(f"recorded {len(t)} samples -> {path}")
        for r in t[:4]: print("  ", r)
    else:
        want = json.load(open(path))["trace"]
        bad = 0
        for a, b in zip(want, t):
            for k in FIELDS:
                if a[k] != b[k]:
                    print(f"  MISMATCH frame {a['frame']} {k}: expected {a[k]}, got {b[k]}")
                    bad += 1
        if len(want) != len(t):
            print(f"  LENGTH differs: expected {len(want)} samples, got {len(t)}"); bad += 1
        print("PASS" if not bad else f"FAIL ({bad} mismatches)")
        sys.exit(1 if bad else 0)

main()
