import json
VRAM=open('/tmp/vs_hook/vram_039000.bin','rb').read()
DISC=open('/tmp/tpw_userdata.bin','rb').read()
W=1024; zb=b'\0'*8192
clut=json.load(open('texture_clut_map.json'))['pages']
have_clut={k for k in clut}
def blk(bx,by):
    o=bytearray()
    for r in range(by,by+64):
        p=(r*W+bx)*2; o+=VRAM[p:p+128]
    return bytes(o)
res=[]
for bx in range(512,1024,64):
    for by in range(0,512,64):
        b=blk(bx,by)
        if b==zb: continue
        i=DISC.find(b)
        res.append((bx,by,i))
found=[r for r in res if r[2]>=0]
print('non-blank blocks   :', len(res))
print('found verbatim     :', len(found))
print()
print('%-10s %-12s %-10s %s' % ('block','disc offset','aligned','page has CLUTs'))
for bx,by,i in found:
    pg='%d,%d'%(bx, 0 if by<256 else 256)
    print('%-10s %-12s %-10s %s' % ('%d,%d'%(bx,by), hex(i),
          'yes' if i%0x1000==0 else 'no',
          'YES (%d)'%len(clut[pg]) if pg in have_clut else 'no'))
cols=sorted({bx for bx,_,_ in found})
print(); print('columns with verbatim disc bytes:', cols)
