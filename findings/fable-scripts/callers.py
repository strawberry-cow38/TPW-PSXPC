import sys; sys.path.insert(0,'/home/ec2-user/tpw/fable')
from ann import w,names
from funcs import extent
from mipsdis import dis
ts=set(int(a,16) for a in sys.argv[1:])
for pc in list(range(0x80010000,0x800C0498,4))+list(range(0x800E8000,0x800EF000,4)):
    x=w(pc)
    if (x>>26)==3:
        t=(pc&0xF0000000)|((x&0x3ffffff)<<2)
        if t in ts:
            s,e=extent(pc); print(f'{t:08x} <- fn {s:08x} site {pc:08x} {"<"+names[s]+">" if s in names else ""}')
# also pointer words in data referencing target (vtable entries)
import struct
from ann import d,B
for off in range(0,len(d)-4,4):
    v=struct.unpack_from('<I',d,off)[0]
    if v in ts: print(f'{v:08x} <- data word at {B+off:08x}')
