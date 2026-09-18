import sys; sys.path.insert(0,'/home/ec2-user/tpw/fable')
from funcs import extent
from ann import annotate
for a in sys.argv[1:]:
    s,e=extent(int(a,16)); print(f'==== fn {s:08x}..{e:08x} (requested {a})'); annotate(s,e); print()
