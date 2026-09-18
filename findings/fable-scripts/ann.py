#!/usr/bin/env python3
"""annotated disassembler for TPW.BIN. usage: ann.py START END  (hex, no 0x needed)
resolves gp-relative, lui/addiu|lw|lh|lb|sb|sh|sw pairs, jal names."""
import struct,sys,json,re
sys.path.insert(0,'/home/ec2-user/tpw/fable')
from mipsdis import dis,R
d=open('/home/ec2-user/tpw/ext/TPW.BIN','rb').read()
B=0x80010000; GP=0x80102654
names={int(k,16) if isinstance(k,str) else k: v[0] for k,v in json.load(open('/home/ec2-user/tpw/named_union.json')).items()}
try:
    names.update({int(k,16):v for k,v in json.load(open('/home/ec2-user/tpw/fable/mynames.json')).items()})
except Exception: pass
def w(a): return struct.unpack_from('<I',d,a-B)[0]
def strat(a):
    try:
        e=d.index(b'\0',a-B); s=d[a-B:e]
        if 0<len(s)<60 and all(32<=c<127 for c in s): return s.decode()
    except Exception: pass
    return None
def annotate(start,end,out=sys.stdout):
    lui={}  # reg -> hi value
    for pc in range(start,end,4):
        x=w(pc); s=dis(x,pc); note=''
        op=x>>26; rs=(x>>21)&31; rt=(x>>16)&31; imm=x&0xffff; simm=imm-0x10000 if imm&0x8000 else imm
        if op==15: lui[rt]=imm<<16
        elif op in (9,) and rs in lui and rt==rs:  # addiu rt,rs,lo
            a=(lui[rs]+simm)&0xffffffff; note=f'= 0x{a:08x}'; st=strat(a); 
            if st: note+=f' "{st}"'
            if a in names: note+=f' <{names[a]}>'
            lui[rt]=None; lui.pop(rt,None)
        elif op in (32,33,34,35,36,37,38,40,41,42,43,46,50,58):
            if rs==28: a=(GP+simm)&0xffffffff; note=f'[gp] 0x{a:08x}'
            elif rs in lui and lui[rs] is not None: a=(lui[rs]+simm)&0xffffffff; note=f'[0x{a:08x}]'
            if op in (32,33,34,35,36,37,38,50) and rt in lui: lui.pop(rt,None)
        elif op==3:
            t=(pc&0xF0000000)|((x&0x3ffffff)<<2)
            if t in names: note=f'<{names[t]}>'
        elif op==0 and (x&63)==9: pass
        # invalidate lui tracking on writes
        if op==0:
            rd=(x>>11)&31
            if rd in lui and rd!=0 and (x&63) not in (8,): lui.pop(rd,None)
        elif op in (8,9,10,11,12,13,14) and rt in lui and not (op==9 and rs in lui):
            lui.pop(rt,None)
        if op==3 or (op==0 and (x&63)==9): 
            lui.pop(2,None); lui.pop(3,None)
        
        if x==0: continue
        print(f"{pc:08x}: {s:32s} {note}",file=out)
if __name__=='__main__':
    annotate(int(sys.argv[1],16),int(sys.argv[2],16))
