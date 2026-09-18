#!/usr/bin/env python3
"""xref.py ADDR [ADDR...]  -- find every lui/(addiu|lw|sw|lh|sh|lb|lbu|sb|lhu) pair that forms ADDR,
plus gp-relative accesses. Tracks lui per register linearly (across function boundaries; may give rare
false positives). Prints function start, site, insn."""
import sys,struct
sys.path.insert(0,'/home/ec2-user/tpw/fable')
from ann import w,names
from funcs import extent
from mipsdis import dis
GP=0x80102654
targets=set(int(a,16) for a in sys.argv[1:])
lui={}
LO=0x800E8000; HI=0x800EF000
for pc in list(range(0x80010000,0x800C0498,4))+list(range(0x800E8000,0x800EF000,4)):
    x=w(pc); op=x>>26; rs=(x>>21)&31; rt=(x>>16)&31; imm=x&0xffff; simm=imm-0x10000 if imm&0x8000 else imm
    hit=None
    if op==15: lui[rt]=(imm<<16,pc); continue
    if op in (9,8) and rs in lui:  # addiu
        a=(lui[rs][0]+simm)&0xffffffff
        if a in targets: hit=a
        if rt==rs: lui.pop(rt,None)
        else: lui.pop(rt,None)
    elif op==13 and rs in lui:  # ori
        a=lui[rs][0]|imm
        if a in targets: hit=a
        lui.pop(rt,None)
    elif op in (32,33,34,35,36,37,38,40,41,42,43,46,50,58):
        if rs==28:
            a=(GP+simm)&0xffffffff
            if a in targets: hit=a
        elif rs in lui:
            a=(lui[rs][0]+simm)&0xffffffff
            if a in targets: hit=a
        if op in (32,33,34,35,36,37,38,50): lui.pop(rt,None)
    elif op==0:
        rd=(x>>11)&31
        if rd!=0: lui.pop(rd,None)
    elif op in (10,11,12,14): lui.pop(rt,None)
    if op==3 or (op==0 and (x&63)==9): lui.pop(2,None); lui.pop(3,None); lui.pop(31,None)
    if hit is not None:
        s,e=extent(pc)
        print(f'{hit:08x}  fn {s:08x}  site {pc:08x}  {dis(x,pc)}  {"<"+names[s]+">" if s in names else ""}')
