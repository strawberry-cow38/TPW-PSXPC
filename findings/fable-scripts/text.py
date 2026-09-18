#!/usr/bin/env python3
"""text.py [ID...]: look up strings in the English FOLIO text table. No args: pick table + print count."""
import struct,sys,glob
RIP='/home/ec2-user/tpw/ext/rip/'
def load(fn):
    d=open(fn,'rb').read(); n=struct.unpack_from('<I',d,0)[0]
    out=[]
    for i in range(n):
        off=struct.unpack_from('<I',d,4+4*i)[0]; e=d.index(b'\0',off); out.append(d[off:e].decode('latin1'))
    return out
tables={fn:load(fn) for fn in sorted(glob.glob(RIP+'04[01][0-9].bin')) if open(fn,'rb').read(4)==b'\x07\x04\x00\x00'}
eng=None
for fn,t in tables.items():
    if any('Build' in s for s in t) and any(' the ' in s for s in t): eng=fn; break
T=tables[eng]
if __name__=='__main__':
    if len(sys.argv)==1: print(eng,len(T)); [print(fn,len(t),t[0]) for fn,t in tables.items()]
    for a in sys.argv[1:]:
        i=int(a,0); print(f'{i} 0x{i:x}: {T[i]!r}')
