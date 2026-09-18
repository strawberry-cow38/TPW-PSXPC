"""Per-FUNCTION PsyQ signature matching.

Whole-OBJ matching fails on tiny version skew: libcd BIOS.OBJ agrees for a
clean 4352-byte prefix against TPW.BIN and then diverges, which discards ~20
good functions to pay for one changed one. Labels inside each OBJ sig give
function boundaries, so match each function independently instead.

Discipline: a short pattern matches by luck, so every hit records how many
DISTINCT positions in the target it matched. Ambiguous hits (>1 site) are
counted separately and never used for naming.
"""
import json, os, sys, glob, collections, re

MIN_LEN = 32        # bytes; below this MIPS prologues collide constantly
MIN_CONCRETE = 20
INTERNAL = re.compile(r'^(loc|text|sub|byte|word|dword|unk|off|jpt|asc)_', re.I)

def parse(s):
    return [None if '?' in t else int(t, 16) for t in s.split()]

def functions(entry):
    """[(name, pat)] for each real function in an OBJ signature."""
    pat = parse(entry['sig'])
    labs = sorted(entry.get('labels', []), key=lambda L: L['offset'])
    real = [L for L in labs if not INTERNAL.match(L['name'])]
    out = []
    for i, L in enumerate(real):
        start = L['offset']
        end = real[i + 1]['offset'] if i + 1 < len(real) else len(pat)
        if end - start >= MIN_LEN:
            out.append((L['name'], start, pat[start:end]))
    return out

def find_all(data, pat, cap=4):
    """every position where the masked pattern matches, up to cap"""
    concrete = [(i, b) for i, b in enumerate(pat) if b is not None]
    if len(concrete) < MIN_CONCRETE:
        return []
    # anchor on longest concrete run
    best_i = best_n = i = 0
    while i < len(pat):
        if pat[i] is None:
            i += 1; continue
        j = i
        while j < len(pat) and pat[j] is not None:
            j += 1
        if j - i > best_n:
            best_i, best_n = i, j - i
        i = j
    if best_n < 8:
        return []
    ab = bytes(pat[best_i:best_i + best_n])
    hits, start = [], 0
    while len(hits) < cap:
        p = data.find(ab, start)
        if p < 0:
            break
        c = p - best_i
        if c >= 0 and c + len(pat) <= len(data) and \
           all(data[c + k] == b for k, b in concrete):
            hits.append(c)
        start = p + 1
    return hits

def main():
    target, sigroot, base = sys.argv[1], sys.argv[2], int(sys.argv[3], 16)
    data = open(target, 'rb').read()
    print(f"target {os.path.basename(target)} {len(data)} bytes base 0x{base:08X}\n")
    print(f"{'ver':>6} {'funcs':>7} {'uniq':>6} {'ambig':>6} {'bytes':>8}")
    out = {}
    for v in sorted(os.listdir(sigroot)):
        if not v.isdigit():
            continue
        uniq, ambig, seen = {}, 0, set()
        for f in sorted(glob.glob(os.path.join(sigroot, v, '*.json'))):
            try: entries = json.load(open(f))
            except Exception: continue
            lib = os.path.basename(f).split('.')[0]
            for e in entries:
                if not e.get('sig'): continue
                for name, off, pat in functions(e):
                    hs = find_all(data, pat)
                    if len(hs) == 1:
                        a = base + hs[0]
                        if a not in uniq or len(pat) > uniq[a][2]:
                            uniq[a] = (name, lib, len(pat), e['name'])
                    elif len(hs) > 1:
                        ambig += 1
        cov = sum(v2[2] for v2 in uniq.values())
        print(f"{v:>6} {len(uniq)+ambig:>7} {len(uniq):>6} {ambig:>6} {cov:>8}")
        out[v] = {f"{a:08X}": list(t) for a, t in uniq.items()}
    json.dump(out, open(sys.argv[4], 'w'))

main()
