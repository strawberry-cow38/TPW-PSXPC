#!/usr/bin/env python3
"""Read PAL evidence and run its actual 0x800B1F84 instructions with host getters stubbed.

No decompiler algebra is used for the movement oracle. Branch delay slots and mflo/mfhi
are executed. Geometry/delta/slider getters are controlled inputs, not emulator claims.
Also checks both 192-entry C# math tables against the executable and counts one archive.
"""
import argparse
import hashlib
import json
import re
import struct
from pathlib import Path
from ride_phases import archive_entries, ride_rows, u32

ROOT = Path(__file__).resolve().parents[1]
BASE = 0x80010000


def signed(x, bits=32):
    x &= (1 << bits) - 1
    return x - (1 << bits) if x & (1 << (bits - 1)) else x


class Machine:
    def __init__(self, binary):
        self.binary = binary
        self.mem = {}
        self.r = [0] * 32
        self.hi = self.lo = 0
        self.hooks = {}

    def read(self, address, size=4):
        return sum((self.mem.get(address+i, self.binary[address+i-BASE]
                    if BASE <= address+i < BASE+len(self.binary) else 0)) << (8*i) for i in range(size))

    def write(self, address, value, size=4):
        for i in range(size): self.mem[address+i] = (value >> (8*i)) & 255

    def run(self):
        pc, following = 0x800B1F84, 0x800B1F88
        self.r[31] = 0x7FFF0000
        for _ in range(10000):
            if pc == 0x7FFF0000: return
            if pc in self.hooks:
                self.hooks[pc]()
                pc = self.r[31]; following = pc + 4
                continue
            w = self.read(pc)
            op, rs, rt, rd, sh, fn = w >> 26, w >> 21 & 31, w >> 16 & 31, w >> 11 & 31, w >> 6 & 31, w & 63
            imm = signed(w & 65535, 16)
            next_after = (following + 4) & 0xFFFFFFFF
            if op == 0:
                a, b = self.r[rs], self.r[rt]
                if fn == 0: self.r[rd] = b << sh
                elif fn == 2: self.r[rd] = b >> sh
                elif fn == 3: self.r[rd] = signed(b) >> sh
                elif fn in (8, 9):
                    next_after = a
                    if fn == 9: self.r[rd] = pc + 8
                elif fn == 16: self.r[rd] = self.hi
                elif fn == 18: self.r[rd] = self.lo
                elif fn == 24:
                    product = signed(a) * signed(b)
                    self.lo, self.hi = product & 0xFFFFFFFF, product >> 32 & 0xFFFFFFFF
                elif fn == 26:
                    a, b = signed(a), signed(b)
                    self.lo = (abs(a)//abs(b)) * (-1 if (a < 0) != (b < 0) else 1)
                    self.hi = a - self.lo*b
                elif fn == 33: self.r[rd] = a+b
                elif fn == 35: self.r[rd] = a-b
                elif fn == 37: self.r[rd] = a|b
                elif fn == 42: self.r[rd] = int(signed(a) < signed(b))
                else: raise ValueError(f'unsupported SPECIAL {pc:08X} {w:08X}')
            elif op in (2, 3):
                next_after = (pc & 0xF0000000) | ((w & 0x3FFFFFF) << 2)
                if op == 3: self.r[31] = pc + 8
            elif op in (1, 4, 5):
                take = ((signed(self.r[rs]) >= 0 if rt == 1 else signed(self.r[rs]) < 0) if op == 1
                        else (self.r[rs] == self.r[rt] if op == 4 else self.r[rs] != self.r[rt]))
                if take: next_after = pc + 4 + (imm << 2)
            elif op == 9: self.r[rt] = self.r[rs] + imm
            elif op == 10: self.r[rt] = int(signed(self.r[rs]) < imm)
            elif op == 13: self.r[rt] = self.r[rs] | (w & 65535)
            elif op == 15: self.r[rt] = (w & 65535) << 16
            elif op in (33, 35, 36):
                n = {33: 2, 35: 4, 36: 1}[op]
                value = self.read((self.r[rs] + imm) & 0xFFFFFFFF, n)
                self.r[rt] = signed(value, 16) if op == 33 else value
            elif op == 43: self.write((self.r[rs] + imm) & 0xFFFFFFFF, self.r[rt])
            else: raise ValueError(f'unsupported instruction {pc:08X} {w:08X}')
            self.r = [v & 0xFFFFFFFF for v in self.r]; self.r[0] = 0
            pc, following = following, next_after
        raise ValueError('instruction budget exhausted')


def movement(binary, case):
    m = Machine(binary)
    train, owner, vt = 0x10000, 0x20000, 0x30000
    # Same topology as C# fixture: Approach → Launch → A → B → Approach.
    nodes = [owner+0x10C, owner+0x14C, 0x40000, 0x40100]
    for i, p in enumerate(nodes):
        m.write(p+0x14, nodes[(i-1) % 4]); m.write(p+0x18, nodes[(i+1) % 4])
        m.write(p+0x36, case['lengths'][i], 2)
    m.write(nodes[case['segment']]+0x38, case['kind'], 1)
    m.r[4], m.r[29], m.r[28] = train, 0x70000, 0x80102654
    for offset, value in [(0xC,case['slope']), (0x18,case['speed']), (0x1C,case['distance']),
                          (0x20,nodes[case['segment']]), (0x24,nodes[1]), (0x28,owner),
                          (0x78,case['launch']), (0x7C,case['ready']), (0x80,0)]: m.write(train+offset,value)
    m.write(train+0x74,case['laps'],2)
    m.write(owner+0x14,vt)
    m.write(vt+0x2CC,0x60000); m.write(vt+0x2DC,0x60004)
    def result(value): m.r[2] = value & 0xFFFFFFFF
    def get(offset, size=4, sign=False):
        value = m.read(m.r[4]+offset,size)
        result(signed(value,size*8) if sign else value)
    m.hooks = {
        0x800B2E64: lambda: get(0x78),
        0x800B18C8: lambda: result(train if case['preview'] else 0),
        0x800B18BC: lambda: result(case['elapsed'] >> 12),
        0x800B6744: lambda: get(0x38,1),
        0x800B677C: lambda: get(0x14),
        0x800B6770: lambda: get(0x18),
        0x800B6798: lambda: get(0x36,2,True),
        0x800BDD0C: lambda: result(case['delta']),
        0x80053D98: lambda: result(case['half']),
        0x800B2E70: lambda: m.write(train+0x78,m.r[5]),
        0x800ACC40: lambda: result(nodes[1]),
        0x800B2E84: lambda: m.write(train+0x74,m.read(train+0x74,2)+1,2),
        0x800B2E78: lambda: get(0x74,2,True),
        0x800B2E98: lambda: m.write(train+0x7C,m.r[5]),
        0x800B8F18: lambda: None,
        0x60000: lambda: result(case['slider']),
        0x60004: lambda: result(case['duration']),
    }
    m.run()
    return dict(speed=signed(m.read(train+0x18)), distance=signed(m.read(train+0x1C)),
                segment=nodes.index(m.read(train+0x20)), launch=bool(m.read(train+0x78)),
                laps=signed(m.read(train+0x74,2),16), ready=bool(m.read(train+0x7C)))


def cases():
    base = dict(lengths=[512,512,512,512], segment=2, kind=0, slope=0, speed=8192,
                distance=5000, launch=False, ready=False, laps=0, preview=False, elapsed=0,
                delta=4096, half=False, slider=100, duration=1)
    changes = [
        ('flat_friction',{}), ('uphill',dict(slope=1536)), ('downhill',dict(slope=-1536)),
        ('special_skips_forces',dict(kind=1,slope=1536)), ('approach_brake',dict(segment=0,slope=-1536)),
        ('special_skips_brake',dict(segment=0,kind=1)), ('maximum',dict(speed=50000,slider=75)),
        ('maximum_then_minimum',dict(speed=50000,slider=0)), ('minimum',dict(speed=-3000)),
        ('delta_zero_moves',dict(delta=0)), ('half_after_minimum',dict(delta=0,half=True)),
        ('logical_shift',dict(delta=200000,speed=16384)),
        ('force_low_word',dict(slope=1048577,speed=10000)),
        ('slider_low_word',dict(slider=2147483647)),
        ('forward_exact',dict(kind=1,speed=2048,distance=129024,launch=True)),
        ('forward_below',dict(kind=1,speed=2048,distance=129023,launch=True)),
        ('only_one_link',dict(kind=1,speed=2048,distance=400000)),
        ('unequal_handoff',dict(lengths=[256,256,512,1536],kind=1,speed=2048,distance=150000)),
        ('reciprocal_first',dict(lengths=[512,512,768,512],kind=1,speed=2048,distance=194000)),
        ('backward',dict(kind=1,speed=2048,distance=-10000,launch=True)),
        ('zero_length',dict(lengths=[512,512,0,512])),
        ('preview_wait',dict(preview=True,launch=True,elapsed=239*4096)),
        ('preview_boundary',dict(preview=True,launch=True,elapsed=240*4096)),
        ('return_midpoint',dict(segment=1,kind=1,speed=2048,distance=63488)),
        ('return_above',dict(segment=1,kind=1,speed=2048,distance=63520)),
        ('prewrap_return',dict(segment=0,kind=1,speed=2048,distance=129024)),
        ('leaving_return',dict(segment=1,kind=1,speed=2048,distance=129024)),
        ('launch_veto',dict(segment=1,kind=1,speed=2048,distance=70000,launch=True)),
        ('preview_veto',dict(segment=1,kind=1,speed=2048,distance=70000,preview=True)),
        ('live_duration',dict(segment=1,kind=1,speed=2048,distance=70000,duration=3)),
        ('signed_counter_wrap',dict(segment=1,kind=1,speed=2048,distance=70000,laps=32767)),
        ('latched_ready',dict(ready=True)),
    ]
    return [dict(name=name, **(base | change)) for name, change in changes]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--binary',type=Path,default=Path('/home/ec2-user/tpw/ext/TPW.BIN'))
    parser.add_argument('--archive',type=Path,default=Path('/home/ec2-user/tpw/ext/FOLIO.GAZ'))
    args = parser.parse_args()
    binary, archive = args.binary.read_bytes(), args.archive.read_bytes()
    tables = {}
    text = (ROOT/'core/TPW.Sim/CoasterMath.cs').read_text()
    for name, address in [('Root',0x801018D0),('Normal',0x800F97B4)]:
        literal = re.search(r'short\[\] '+name+r' = \{(.*?)\};',text,re.S).group(1)
        actual = list(struct.unpack_from('<192h',binary,address-BASE))
        assert list(map(int,re.findall(r'\d+',literal))) == actual, name
        tables[name] = dict(address=f'0x{address:08X}',count=len(actual),matches=True)
    definitions = [e for _,e in archive_entries(archive) if len(e)>=32 and u32(e,0)==0x96 and u32(e,0x14)!=0]
    coaster_rows = [r for r in ride_rows(archive) if r['type']==1]
    assert len(definitions)==244 and len(coaster_rows)==12
    rows=[]
    for r in coaster_rows:
        e = archive_entries(archive)[r['folio']][1]; p=r['record_offset']
        rows.append(dict(name=r['name'], folio=r['folio'], record_address=f"0x{r['archive_offset']+p:X}",
                         launch_speed=struct.unpack_from('<h',e,p+0xC8)[0],
                         duration_min=r['cycles_min'],duration_max=r['cycles_max'],
                         raw_BC=list(e[p+0xBC:p+0xC0])))
    vectors=[dict(input=c,expected=movement(binary,c)) for c in cases()]
    # Control: changing the encoded force from downhill to uphill must change velocity.
    assert vectors[1]['expected']['speed'] < vectors[0]['expected']['speed'] < vectors[2]['expected']['speed']
    assert vectors[23]['expected']['ready'] is False and vectors[24]['expected']['ready'] is True
    shift_case = vectors[11]
    assert (shift_case['expected']['speed'] * shift_case['input']['delta']) & 0x80000000
    output=dict(binary_sha256=hashlib.sha256(binary).hexdigest(),archive_sha256=hashlib.sha256(archive).hexdigest(),
                definition_count=len(definitions),coaster_count=len(rows),coasters=rows,tables=tables,
                movement_address='0x800B1F84',vector_count=len(vectors),vectors=vectors)
    path=ROOT/'findings/coaster-source.json'; path.write_text(json.dumps(output,indent=2)+'\n')
    print(f'{path}: {len(definitions)} definitions, {len(rows)} coasters, {len(vectors)} executed movement vectors, 384 checked table entries')


if __name__ == '__main__': main()
