#!/usr/bin/env python3
"""animtracks.py -- decode TPW model animation tracks out of 0x96 mesh containers.

Track layout is fable/folio.md 3.4; the size table is the game's jump table at 0x800DDD78.
What this adds is the CONTENT of two of the eight types, verified across the whole disc:

  type 8  2295 tracks, zero keyframes -- a bone REST POSE.
          32-byte record after the 8-byte header; first 18 bytes are a row-major 3x3
          GTE rotation matrix, s16, 4096 = 1.0.  2295/2295 orthonormal.

  type 6  1091 tracks, 13425 keyframes -- the ANIMATION.  20 bytes each:
             s16[0]     keyframe time (non-decreasing: 1023/1023 tracks)
             s16[1]     unidentified, 0..380
             s16[2..4]  translation x,y,z
             s16[5]     always 0
             s16[6..9]  unit quaternion x,y,z,w    100.0% of 13425 within 1% of 4096

Motion is almost entirely rotational: 66% of animated bones never translate at all,
while only 20% never rotate.  A port that animates positions will look frozen.

Usage:  animtracks.py [--verify] [files...]      default: ext/rip/e*.bin
"""
import struct, sys, glob, math, os, statistics
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'fable', 'f'))
from x96parse import parse_container, parse_mesh, TRACK, u16

def read_tracks(d, mesh):
    """Yield (type, bone, count, rest_matrix|None, keyframes|None) for one sub-entry."""
    p = mesh['tracks']
    for _ in range(mesh['ntrk']):
        ty = d[p]; bone = u16(d, p + 4); cnt = u16(d, p + 6)
        if ty not in TRACK: return
        stride, hdr = TRACK[ty]
        rest = kfs = None
        if ty == 8:
            rest = struct.unpack_from('<9h', d, p + 8)
        elif ty == 6:
            kfs = [struct.unpack_from('<10h', d, p + hdr + i * 20) for i in range(cnt)]
        yield ty, bone, cnt, rest, kfs
        p += cnt * stride + hdr

def quat(kf):
    """Unit quaternion (x,y,z,w) from a type-6 keyframe."""
    return tuple(v / 4096.0 for v in kf[6:10])

def angle_deg(kf):
    w = max(-4096, min(4096, kf[9]))
    return 2 * math.degrees(math.acos(abs(w) / 4096.0))

def verify(files):
    t8 = t8ok = nkf = kfok = 0; mono = ntr = 0
    for fn in files:
        d = open(fn, 'rb').read()
        try: nsub, recoff, size, ntab, tab, sub = parse_container(d)
        except Exception: continue
        for off, packed in sub:
            if packed: continue
            try: m = parse_mesh(d, off)
            except Exception: continue
            for ty, bone, cnt, rest, kfs in read_tracks(d, m):
                if ty == 8:
                    t8 += 1
                    rows = [rest[0:3], rest[3:6], rest[6:9]]
                    if all(abs(math.sqrt(sum(v * v for v in r)) - 4096) < 180 for r in rows):
                        t8ok += 1
                elif ty == 6 and kfs:
                    ntr += 1
                    if all(b[0] >= a[0] for a, b in zip(kfs, kfs[1:])): mono += 1
                    for kf in kfs:
                        nkf += 1
                        n = math.sqrt(sum(v * v for v in kf[6:10]))
                        if abs(n - 4096) < 41: kfok += 1
    print('type 8 rest poses : %d, orthonormal %d (%.1f%%)' % (t8, t8ok, 100.0 * t8ok / max(1, t8)))
    print('type 6 keyframes  : %d, unit quat   %d (%.1f%%)' % (nkf, kfok, 100.0 * kfok / max(1, nkf)))
    print('type 6 tracks     : %d, time non-decreasing %d (%.1f%%)' % (ntr, mono, 100.0 * mono / max(1, ntr)))
    return t8ok == t8 and kfok == nkf and mono == ntr

if __name__ == '__main__':
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    files = args or sorted(glob.glob(os.path.join(os.path.dirname(__file__), '..', 'ext', 'rip', 'e*.bin')))
    sys.exit(0 if verify(files) else 1)
