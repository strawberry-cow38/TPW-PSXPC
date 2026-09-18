import sys; sys.path.insert(0,'/home/ec2-user/tpw/fable')
from ann import annotate,w
for a in sys.argv[1:]:
    s=int(a,16); e=s
    while w(e)!=0x03e00008 and e<s+0x2000: e+=4
    print(f'==== leaf {s:08x}..{e+8:08x}'); annotate(s,e+8)
