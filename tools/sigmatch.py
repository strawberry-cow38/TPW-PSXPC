"""Match PsyQ per-OBJ signatures against a raw PSX memory image.

Sigs are whole-translation-unit byte patterns with '??' where the linker
relocated a word. A hit therefore names an entire OBJ, not one function.

Anchor strategy: take the longest run of CONCRETE bytes in the sig, find every
occurrence of that run in the target, and verify the full masked pattern at the
implied start. That makes the cost proportional to anchor rarity, not sig count.
"""
import json, os, sys, glob, collections

MIN_ANCHOR = 8      # shorter anchors scan the whole file for nothing
MIN_CONCRETE = 24   # a sig with fewer real bytes than this cannot separate

def parse_sig(s):
    out = []
    for t in s.split():
        out.append(None if '?' in t else int(t, 16))
    return out

def anchor_of(pat):
    """longest concrete run -> (offset_in_pat, bytes)"""
    best_i = best_n = 0
    i = 0
    while i < len(pat):
        if pat[i] is None:
            i += 1; continue
        j = i
        while j < len(pat) and pat[j] is not None:
            j += 1
        if j - i > best_n:
            best_i, best_n = i, j - i
        i = j
    if best_n == 0:
        return None
    return best_i, bytes(pat[best_i:best_i + best_n])

def verify(data, pos, pat):
    if pos < 0 or pos + len(pat) > len(data):
        return False
    for k, b in enumerate(pat):
        if b is not None and data[pos + k] != b:
            return False
    return True

def match_file(data, entries, libname):
    hits = []
    for e in entries:
        if not e.get('sig'):
            continue
        pat = parse_sig(e['sig'])
        concrete = sum(1 for b in pat if b is not None)
        if concrete < MIN_CONCRETE:
            continue
        a = anchor_of(pat)
        if a is None or len(a[1]) < MIN_ANCHOR:
            continue
        aoff, abytes = a
        start = 0
        while True:
            p = data.find(abytes, start)
            if p < 0:
                break
            cand = p - aoff
            if verify(data, cand, pat):
                hits.append((cand, libname, e['name'], len(pat), concrete, e.get('labels', [])))
                break          # first hit per OBJ is enough
            start = p + 1
    return hits

def main():
    target, sigroot = sys.argv[1], sys.argv[2]
    base = int(sys.argv[3], 16) if len(sys.argv) > 3 else 0x80010000
    data = open(target, 'rb').read()
    print(f"target {os.path.basename(target)}  {len(data)} bytes  base 0x{base:08X}")
    print(f"{'ver':>6} {'objs':>6} {'hit':>5} {'bytes':>8}  top libs")
    results = {}
    for v in sorted(os.listdir(sigroot)):
        if not v.isdigit():
            continue
        allhits, nobj = [], 0
        for f in sorted(glob.glob(os.path.join(sigroot, v, '*.json'))):
            try:
                entries = json.load(open(f))
            except Exception:
                continue
            nobj += len(entries)
            allhits += match_file(data, entries, os.path.basename(f).split('.')[0])
        cov = sum(h[3] for h in allhits)
        bylib = collections.Counter(h[1] for h in allhits)
        top = ', '.join(f"{k}:{n}" for k, n in bylib.most_common(4))
        print(f"{v:>6} {nobj:>6} {len(allhits):>5} {cov:>8}  {top}")
        results[v] = allhits
    best = max(results, key=lambda v: sum(h[3] for h in results[v]))
    print(f"\nBEST: {best}")
    json.dump({v: [[h[0], h[1], h[2], h[3], h[4], h[5]] for h in hs]
               for v, hs in results.items()},
              open(sys.argv[4] if len(sys.argv) > 4 else '/home/ec2-user/tpw/sigres.json', 'w'))

main()
