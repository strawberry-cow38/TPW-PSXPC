import hashlib, json, os, collections
STATES = "park park_ride park_shop park_shop5 park_trail park_trail2 practice practice_open practice_clean menu1 mainmenu language".split()
W = 1024  # halfwords per VRAM row
def blocks(path):
    d = open(path,'rb').read()
    assert len(d) == 1048576, len(d)
    out = {}
    for bx in range(512, 1024, 64):
        for by in range(0, 512, 64):
            h = hashlib.sha256()
            for row in range(by, by+64):
                off = (row*W + bx)*2
                h.update(d[off:off+128])
            out['%d,%d'%(bx,by)] = h.hexdigest()[:16]
    return out

BLANK = blocks.__doc__
caps = {}
for s in STATES:
    f = '/tmp/vs_%s/vram_000020.bin'%s
    if os.path.exists(f): caps[s] = blocks(f)
print('captures:', len(caps))

# what a blank block hashes to
zh = hashlib.sha256(b'\0'*128*64).hexdigest()[:16]

keys = sorted(caps[STATES[0]], key=lambda k:(int(k.split(',')[0]), int(k.split(',')[1])))
print()
print('%-10s %-8s %s' % ('block','distinct','states-with-blank'))
changing = []
for k in keys:
    vals = [caps[s][k] for s in caps]
    nblank = sum(1 for v in vals if v == zh)
    nd = len(set(vals))
    if nd > 1: changing.append(k)
    print('%-10s %-8d %d/%d' % (k, nd, nblank, len(vals)))

print()
print('STABLE across all %d states : %d blocks' % (len(caps), 64-len(changing)))
print('CHANGING                    : %d blocks' % len(changing))
print()
# summarise by tpage column
col = collections.defaultdict(lambda: [0,0])
for k in keys:
    x = int(k.split(',')[0])
    col[x][0] += 1
    if k in changing: col[x][1] += 1
print('%-8s %-8s %-8s' % ('tpage x','blocks','changing'))
for x in sorted(col):
    print('%-8d %-8d %-8d' % (x, col[x][0], col[x][1]))
json.dump({s:caps[s] for s in caps}, open('/tmp/state_vram_hashes.json','w'), indent=1)
