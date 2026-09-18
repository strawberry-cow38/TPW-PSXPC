#!/usr/bin/env python3
"""fieldx.py OFF [OFF...] [-r LO HI]: every load/store with immediate OFF (decimal or 0x) in code; prints fn start, site, insn. Also lh/lw of 8*slot+4 style vtable reads if OFF>=... (no)."""
import sys; sys.path.insert(0,'/home/ec2-user/tpw/fable')
from ann import w
from funcs import extent
from mipsdis import dis
args=sys.argv[1:]; lo=0x80010000; hi=0x800C0498
if '-r' in args:
    i=args.index('-r'); lo=int(args[i+1],16); hi=int(args[i+2],16); args=args[:i]+args[i+3:]
offs=set(int(a,0) for a in args)
LS={32:'lb',33:'lh',34:'lwl',35:'lw',36:'lbu',37:'lhu',40:'sb',41:'sh',43:'sw'}
for pc in range(lo,hi,4):
    x=w(pc); op=x>>26
    if op in LS:
        imm=x&0xffff; simm=imm-0x10000 if imm&0x8000 else imm
        if simm in offs and ((x>>21)&31)!=29 and ((x>>21)&31)!=28:
            s,e=extent(pc); print(f'{simm:5d} fn {s:08x} site {pc:08x} {dis(x,pc)}')
