import hashlib, collections
VRAM = open('/tmp/vs_hook/vram_039000.bin','rb').read()
DISC = open('/tmp/tpw_userdata.bin','rb').read()
W = 1024
zblk = b'\0' * (128*64)

def block_bytes(bx, by):
    out = bytearray()
    for row in range(by, by+64):
        o = (row*W + bx)*2
        out += VRAM[o:o+128]
    return bytes(out)

print('%-10s %-8s %s' % ('page','status','probes found on disc (of 6)'))
rows=[]
for bx in range(512, 1024, 64):
    for by in range(0, 512, 64):
        b = block_bytes(bx, by)
        if b == zblk:
            rows.append((bx,by,'blank',-1)); continue
        # entropy guard: a probe of repeated bytes matches everywhere and means nothing
        hits = 0; tried = 0
        for k in range(6):
            off = 200 + k*1300
            p = b[off:off+48]
            if len(set(p)) < 12:      # low-variety probe -> vacuous, skip it
                continue
            tried += 1
            if DISC.find(p) >= 0: hits += 1
        rows.append((bx,by,'present' if tried else 'flat', hits if tried else -1))

for bx,by,st,h in rows:
    if st=='blank': continue
    print('%-10s %-8s %s' % ('%d,%d'%(bx,by), st, h if h>=0 else 'all probes too flat to test'))
