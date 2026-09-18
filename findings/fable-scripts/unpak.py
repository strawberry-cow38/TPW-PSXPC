#!/usr/bin/env python3
"""unpak.py: port of TPW.BIN 0x80018EF0 (UNPAK). File = u32 unpacked_size, u32 ?, packed bytes."""
import struct,sys,glob,os
def unpak(src,limit=1<<22):
    out=bytearray(); i=0
    while i<len(src):
        c=src[i]; i+=1
        if c<0x80:
            n=c+1; out+=src[i:i+n]; i+=n
        else:
            if i>=len(src): break
            n=src[i]; i+=1
            if n==0: break
            off=c-256
            p=len(out)+off
            if p<0: raise ValueError('bad backref')
            for k in range(n): out.append(out[p+k])
        if len(out)>limit: raise ValueError('too big')
    return bytes(out)
if __name__=='__main__':
    for fn in sorted(glob.glob('/home/ec2-user/tpw/ext/rip/*')):
        d=open(fn,'rb').read()
        if len(d)<16: continue
        usz,w1=struct.unpack_from('<II',d,0)
        if not (0x20<=usz<=0x100000): continue
        try: u=unpak(d[8:],usz+64)
        except Exception as e: continue
        if len(u)!=usz: continue
        print(os.path.basename(fn), hex(len(d)), 'unpacked',hex(usz),'w1',hex(w1),'hdr',u[:0x18].hex(), 'open',u[0x14],'fee',struct.unpack_from('<H',u,0x16)[0])
