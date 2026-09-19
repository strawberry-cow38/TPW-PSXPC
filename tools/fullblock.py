VRAM = open('/tmp/vs_hook/vram_039000.bin','rb').read()
DISC = open('/tmp/tpw_userdata.bin','rb').read()
W=1024
def blk(bx,by):
    o=bytearray()
    for r in range(by,by+64):
        p=(r*W+bx)*2; o+=VRAM[p:p+128]
    return bytes(o)

cands = [(512,0),(704,0),(768,0),(768,320),(832,192),(896,0),(576,0),(640,0)]
print('%-10s %-12s %s' % ('page','full 8192B?','disc offset'))
for bx,by in cands:
    b=blk(bx,by)
    i=DISC.find(b)
    print('%-10s %-12s %s' % ('%d,%d'%(bx,by), 'YES' if i>=0 else 'no',
          hex(i) if i>=0 else '-'))
