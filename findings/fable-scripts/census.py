import sys; sys.path.insert(0,'/home/ec2-user/tpw/fable')
from ann import w
from funcs import extent
SET=0x80093f80; PUSH=0x80093f20; POP=0x80093d84
def a1_const(site):
    # check delay slot then preceding 8 insns for addiu a1,zero,N / addu a1,zero,zero
    for pc in [site+4]+[site-4*i for i in range(1,9)]:
        x=w(pc)
        if (x>>16)==0x2405: v=x&0xffff; return v-0x10000 if v&0x8000 else v
        if x==0x00002821: return 0
        if ((x>>16)&0x1f)==5 and (x>>26) in (9,) : return '?'
        if (x>>26)==0 and ((x>>11)&31)==5: return '?'
        if (x>>26)==0x23 and ((x>>16)&31)==5: return '?'  # lw a1
        if (x>>26)==0x24 and ((x>>16)&31)==5: return 'lbu?'
    return '?'
rows=[]
for pc in list(range(0x80010000,0x800C0498,4))+list(range(0x800E8000,0x800EF000,4)):
    x=w(pc)
    if (x>>26)==3:
        t=(pc&0xF0000000)|((x&0x3ffffff)<<2)
        if t in (SET,PUSH,POP):
            kind={SET:'Set',PUSH:'Push',POP:'Pop'}[t]
            s,e=extent(pc)
            rows.append((s,pc,kind,a1_const(pc) if t!=POP else ''))
for s,pc,k,v in rows: print(f'fn {s:08x}  site {pc:08x}  {k:4s} {v}')
