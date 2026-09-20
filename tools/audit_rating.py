#!/usr/bin/env python3
"""Execute selected original MIPS functions on synthetic populated fixtures; no emulator service.
No game bytes are emitted. Unknown instructions fail closed. Read-only input, worktree-only output.
Also count EVERY aligned image word in a call-site census (including non-code as candidates).
"""
import hashlib
import json
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
IMAGE = Path('/home/ec2-user/tpw/ext/TPW.BIN').read_bytes()
BASE = 0x80010000
MASK = 0xffffffff
u = lambda x: x & MASK
s = lambda x: (x & 0x7fffffff) - (x & 0x80000000)
def trunc(a, b):
    return (abs(a) // abs(b)) * (-1 if (a < 0) != (b < 0) else 1)

class Machine:
    def __init__(self):
        self.mem = bytearray(0x200000)
        self.mem[0x10000:0x10000 + len(IMAGE)] = IMAGE
        self.r = [0] * 32
        self.r[28], self.r[29] = 0x80102654, 0x801f0000
        self.hi = self.lo = 0
        self.hooks = {}
        self.steps = 0
    def read(self, addr, size):
        at = addr & 0x1fffffff
        if not 0 <= at <= len(self.mem) - size: raise ValueError(hex(addr))
        return int.from_bytes(self.mem[at:at+size], 'little')
    def write(self, addr, value, size):
        at = addr & 0x1fffffff
        if not 0 <= at <= len(self.mem) - size: raise ValueError(hex(addr))
        self.mem[at:at+size] = (value & ((1 << (8*size))-1)).to_bytes(size, 'little')
    def call(self, pc, *args):
        self.r[4:4+len(args)] = args
        self.r[31] = stop = 0x801ffffc
        pending = None
        for _ in range(1000000):
            if pc == stop: return self.r[2]
            self.steps += 1
            if pc in self.hooks:
                self.hooks[pc](self)
                pc = self.r[31]
                continue
            x = self.read(pc, 4)
            op, rs, rt, rd, sh, fn = x >> 26, (x >> 21)&31, (x >> 16)&31, (x >> 11)&31, (x >> 6)&31, x&63
            imm = x & 65535
            si = imm - 65536 if imm & 32768 else imm
            r = self.r
            jump = None
            if op == 0:
                if fn == 0: r[rd] = u(r[rt] << sh)
                elif fn == 2: r[rd] = r[rt] >> sh
                elif fn == 3: r[rd] = u(s(r[rt]) >> sh)
                elif fn == 8: jump = r[rs]
                elif fn == 9: jump = r[rs]; r[rd] = pc + 8
                elif fn == 16: r[rd] = self.hi
                elif fn == 18: r[rd] = self.lo
                elif fn in (24, 25):
                    a,b = (s(r[rs]), s(r[rt])) if fn == 24 else (r[rs],r[rt])
                    n = a*b; self.lo = u(n); self.hi = u(n >> 32)
                elif fn in (26,27):
                    a,b = (s(r[rs]), s(r[rt])) if fn == 26 else (r[rs],r[rt])
                    q = trunc(a,b); self.lo = u(q); self.hi = u(a-q*b)
                elif fn == 33: r[rd] = u(r[rs]+r[rt])
                elif fn == 35: r[rd] = u(r[rs]-r[rt])
                elif fn == 36: r[rd] = r[rs]&r[rt]
                elif fn == 37: r[rd] = r[rs]|r[rt]
                elif fn == 42: r[rd] = int(s(r[rs]) < s(r[rt]))
                elif fn == 43: r[rd] = int(r[rs] < r[rt])
                else: raise ValueError(f'Unsupported {pc:08x}: {x:08x}')
            elif op in (2,3):
                jump = (pc & 0xf0000000) | ((x & 0x3ffffff)<<2)
                if op == 3: r[31] = pc+8
            elif op in (1,4,5,6,7):
                take = {1: (s(r[rs]) < 0 if rt == 0 else s(r[rs]) >= 0),
                        4:r[rs] == r[rt], 5:r[rs] != r[rt], 6:s(r[rs]) <= 0, 7:s(r[rs]) > 0}[op]
                if take: jump = u(pc + 4 + si*4)
            elif op == 9: r[rt] = u(r[rs]+si)
            elif op == 10: r[rt] = int(s(r[rs]) < si)
            elif op == 11: r[rt] = int(r[rs] < u(si))
            elif op == 12: r[rt] = r[rs]&imm
            elif op == 13: r[rt] = r[rs]|imm
            elif op == 15: r[rt] = imm<<16
            elif op in (32,33,35,36,37):
                size = {32:1,33:2,35:4,36:1,37:2}[op]
                v = self.read(u(r[rs]+si),size)
                if op in (32,33) and v & (1 << (size*8-1)): v -= 1 << (size*8)
                r[rt] = u(v)
            elif op in (40,41,43): self.write(u(r[rs]+si),r[rt],{40:1,41:2,43:4}[op])
            else: raise ValueError(f'Unsupported {pc:08x}: {x:08x}')
            r[0] = 0
            next_pc = pending if pending is not None else pc+4
            pending = jump
            pc = next_pc
        raise RuntimeError('instruction budget exhausted')

def history_case(kind, months, seed, extreme=False):
    m = Machine()
    ring, packed, restored = 0x80180000, 0x80181000, 0x80182000
    width = 1 if kind == 'byte' else 4
    values = [((i*37+seed)%256 if kind == 'byte' else ((i*971+seed)%100001)-50000) for i in range(144)]
    if extreme: values = [s(i*79199237 + seed*331) for i in range(144)]
    for i,v in enumerate(values): m.write(ring+i*width,v,width)
    size = 35 if kind == 'byte' else 80
    # nonzero adjacent byte / padding control, never a silently zero neighboring buffer
    m.write(packed+(35 if kind == 'byte' else 79), 173, 1)
    m.call(0x80068740 if kind == 'byte' else 0x800879dc, ring,months,packed)
    encoded = [m.read(packed+i,1) for i in range(size)]
    m.call(0x80068a54 if kind == 'byte' else 0x80087f90,packed,months,restored)
    expanded = [m.read(restored+i*width,width) for i in range(144)]
    if kind != 'byte': expanded = [s(v) for v in expanded]
    return dict(kind=kind,months=months,seed=seed,ring=values,encoded=encoded,neighbor=173,extreme=extreme,restored=expanded,instructions=m.steps)

def rating(visitors, shops, shows, features, staff, levels):
    m = Machine()
    for idx,(gp,count) in enumerate(zip([0x80103884,0x80103854,0x8010385c,0x80103858,0x8010386c,0x80103868,0x80103874,0x80103864,0x80103870], [visitors,shops,shows,features]+staff)):
        pool=0x80190000+idx*32; m.write(gp,pool,4); m.write(pool+12,count,4)
    cursor=[0]; calls=[]
    def init(vm):
        calls.append(vm.r[5]); cursor[0]=0
    def get(vm): vm.r[2] = 0x80180000 + cursor[0]*256 if cursor[0]<len(levels) else 0
    def advance(vm): cursor[0]+=1; get(vm)
    m.hooks.update({0x8006dccc:init,0x8006dd3c:get,0x8006de68:advance})
    for i,level in enumerate(levels): m.write(0x80180000+i*256+246,level,1)
    result=m.call(0x8005b830)
    assert calls == [1] and cursor[0] == len(levels)
    return dict(visitors=visitors,shops=shops,shows=shows,features=features,staff=staff,levels=levels,rating=result,instructions=m.steps,rides_visited=cursor[0])

def value_control(rows):
    m=Machine();cursor=[0];reads=[]
    def init(vm):cursor[0]=0
    def get(vm):vm.r[2]=0x80180000+cursor[0]*512 if cursor[0]<len(rows) else 0
    def advance(vm):cursor[0]+=1;get(vm)
    def manager(vm):vm.r[2]=0x80190000
    def kind(vm):vm.r[2]=rows[cursor[0]][0]
    def price(vm):reads.append([vm.r[5],vm.r[6]]);vm.r[2]=101
    m.hooks.update({0x8006dca0:init,0x8006dd3c:get,0x8006de68:advance,
                   0x80069610:manager,0x80191000:kind,0x8006ad58:price})
    for i,(t,definition,level) in enumerate(rows):
        a=0x80180000+i*512
        m.write(a+12,0x80192000,4);m.write(a+107,definition,1);m.write(a+246,level,1)
    m.write(0x80192000+128,0,2);m.write(0x80192000+132,0x80191000,4)
    m.call(0x80087884,0x80193000)
    assert cursor[0]==len(rows)
    return dict(rows=rows,raw_value=s(m.read(0x80193000,4)),price_calls=reads,
                instructions=m.steps,rows_visited=cursor[0],rows_skipped=0)

def value_audit():
    empty=value_control([]);built=value_control([(3,17,2),(4,19,0),(2,23,0)])
    controls=dict(empty=empty['raw_value']==0 and empty['price_calls']==[],
                  populated=built['raw_value']==1515 and built['rows_visited']==3,
                  selector_is_definition=built['price_calls']==[[3,17],[4,19],[2,23]])
    result=dict(image_sha256=hashlib.sha256(IMAGE).hexdigest(),empty_control=empty,
                populated_control=built,controls=controls,
                limitation='Iterator, type getter and 101-pound price getter are stubs. Original selector load, sum, Money conversion and division execute. The port intentionally retains the conflicting findings level contract.')
    (ROOT/'findings/rating-value-audit.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result,indent=2))
    assert all(controls.values())

def main():
    cases=[history_case(k,n,seed) for k in ('byte','money') for n,seed in [(0,1),(1,7),(14,3),(144,19),(289,87)]]
    cases.append(history_case('money', 289, 187, extreme=True))
    ratings=[rating(0,0,0,0,[0]*5,[]),rating(99,3,4,7,[1,2,3,4,5],[0,1,2,3]),rating(150,9,8,40,[9]*5,[2]*22)]
    targets=[0x8005b830,0x80087884,0x800670d4,0x80087570]
    calls={hex(t):[] for t in targets}
    for off in range(0,len(IMAGE)-3,4):
        ins=struct.unpack_from('<I',IMAGE,off)[0]
        if ins>>26 in (2,3):
            target=0x80000000|((ins&0x3ffffff)<<2)
            if target in targets: calls[hex(target)].append(hex(BASE+off))
    controls=dict(empty_rating=ratings[0]['rating']==0,populated_rating=ratings[1]['rating']==62,
                  saturated_rating=ratings[2]['rating']==100,
                  nonempty_rides=ratings[1]['rides_visited']==4,
                  histories_executed=all(x['instructions']>1000 for x in cases),
                  histories_change=all(x['restored'] != x['ring'] for x in cases),
                  known_rating_call=calls['0x8005b830']==['0x80067100'])
    out=dict(image_sha256=hashlib.sha256(IMAGE).hexdigest(),census=dict(bytes=len(IMAGE),aligned_words_scanned=len(IMAGE)//4,
             skipped_aligned_words=0,trailing_bytes=len(IMAGE)%4,records_scanned=0,record_sweep_performed=False),
             direct_call_candidates=calls,controls=controls,rating_cases=ratings,history_cases=cases,
             limitations=['Synthetic RAM, no console gameplay measurement.','Rating iterator is a stub; mask and nonempty visit count asserted.','Whole-image call candidates include data; indirect calls not enumerated.'])
    (ROOT/'findings/rating-audit.json').write_text(json.dumps(out,indent=2)+'\n')
    print(json.dumps(dict(census=out['census'],controls=controls,ratings=[r['rating'] for r in ratings]),indent=2))
    assert all(controls.values()), 'Control failed; do not claim validation.'
if __name__=='__main__':
    if '--value' in sys.argv: value_audit()
    else: main()
