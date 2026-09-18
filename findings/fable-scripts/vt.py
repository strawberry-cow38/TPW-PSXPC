#!/usr/bin/env python3
"""vt.py VT1 VT2 ... [-n SLOTS]: dump GCC2 vtables side by side (8-byte entries, pfn at +4)."""
import sys,struct
sys.path.insert(0,'/home/ec2-user/tpw/fable')
from ann import w,names
args=[a for a in sys.argv[1:] if not a.startswith('-n')]
n=100
for a in sys.argv[1:]:
    if a.startswith('-n'): n=int(a[2:])
vts=[int(a,16) for a in args]
print('slot  '+'  '.join(f'{v:08x}' for v in vts))
for k in range(n):
    row=[]
    for v in vts:
        pfn=w(v+8*k+4); delta=w(v+8*k)&0xffff
        row.append(f'{pfn:08x}'+(f'{delta:+x}' if delta else '  '))
    print(f'{k:3d}   '+'  '.join(row))
