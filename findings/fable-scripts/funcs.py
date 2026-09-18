import struct,sys
d=open('/home/ec2-user/tpw/ext/TPW.BIN','rb').read()
B=0x80010000
def w(a): return struct.unpack_from('<I',d,a-B)[0]
starts=[]
for a in list(range(B,0x800C0498,4))+list(range(0x800E8000,0x800EF000,4)):
    x=w(a)
    if (x>>16)==0x27bd and (x&0x8000): starts.append(a)
def extent(a):
    """function containing a: from nearest prologue <= a to the jr ra followed by delay slot before next prologue"""
    import bisect
    i=bisect.bisect_right(starts,a)-1
    s=starts[i]; n=starts[i+1] if i+1<len(starts) else 0x800C0498
    # find last 'jr ra' before n
    e=n
    for p in range(n-4,s,-4):
        if w(p)==0x03e00008: e=p+8; break
    return s,e
if __name__=='__main__':
    for a in sys.argv[1:]:
        s,e=extent(int(a,16)); print(f'{a}: {s:08x}..{e:08x} ({(e-s)//4} insns)')
