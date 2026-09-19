import json, collections
caps = json.load(open('/tmp/state_vram_hashes.json'))
PARKS = "park park_ride park_shop park_shop5 park_trail park_trail2 practice practice_open practice_clean".split()
MENUS = "menu1 mainmenu language".split()
import hashlib
zh = hashlib.sha256(b'\0'*128*64).hexdigest()[:16]
keys = sorted(caps['park'], key=lambda k:(int(k.split(',')[0]), int(k.split(',')[1])))

print('distinct values among the 9 IN-PARK captures only (blank excluded):')
print('%-10s %-9s %-7s' % ('block','distinct','blank'))
percol = collections.defaultdict(list)
for k in keys:
    vals = [caps[s][k] for s in PARKS]
    nb = sum(1 for v in vals if v == zh)
    nd = len(set(v for v in vals if v != zh))
    percol[int(k.split(',')[0])].append(nd)
    if nd > 1 or nb:
        print('%-10s %-9d %-7d  <-- varies' % (k, nd, nb) if nd > 1 else '%-10s %-9d %-7d' % (k, nd, nb))
print()
print('%-8s %s' % ('tpage x','distinct-per-block across the 9 parks'))
for x in sorted(percol):
    print('%-8d %s' % (x, percol[x]))
print()
print('menu captures, non-blank blocks:')
for s in MENUS:
    nb = sum(1 for k in keys if caps[s][k] != zh)
    print('  %-10s %d/64 non-blank' % (s, nb))
