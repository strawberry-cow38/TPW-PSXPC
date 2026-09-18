#!/usr/bin/env python3
"""recs.py: parse every attraction definition record in /home/ec2-user/tpw/ext/rip -> records.json + records.txt"""
import struct,glob,json,os
RIP='/home/ec2-user/tpw/ext/rip'
rows=[]
for fn in sorted(glob.glob(RIP+'/*.bin')):
    d=open(fn,'rb').read()
    if len(d)<0x40: continue
    h=struct.unpack_from('<8I',d,0)
    if h[0]!=0x96: continue
    off=h[5]
    if off+0x40>len(d): continue
    t=struct.unpack_from('<I',d,off)[0]
    if not 1<=t<=8: continue
    idx=int(os.path.basename(fn)[:4]); r=d[off:]
    w=lambda o: struct.unpack_from('<I',r,o)[0]; h16=lambda o: struct.unpack_from('<h',r,o)[0]; u16=lambda o: struct.unpack_from('<H',r,o)[0]
    rec={'folio':idx,'file':os.path.basename(fn),'rec_off':off,'type':t,'text_id':w(4),'fp_w':r[8],'b9':r[9],'fp_h':r[10],'b11':r[11],
         'entrance':[h16(0xC),h16(0xE)],'exit':[h16(0x10),h16(0x12)],'b14':r[0x14],'b15':r[0x15],'b16':r[0x16],'b17':r[0x17],
         'base_intensity':w(0x18),'body_len':w(0x1C),'w20':w(0x20)}
    if t in (1,3,6,7):
        rec['levels']=[]
        for L in range(3):
            b=0x24+0x34*L
            rec['levels'].append({'cap_max':w(b),'f28':w(b+4),'f2C':w(b+8),'f30':w(b+12),'lifetime':w(b+16),'b8_min':w(b+20),'b8_max':w(b+24),'f40':w(b+28),'cycles_max':w(b+32),'f48':w(b+36),'f4C':w(b+40),'price':w(b+44)})
    else:
        rec['price']=w(0x20); rec['u16s']=[u16(o) for o in range(0x24,0x24+max(0,w(0x1C)-4),2)]
    rows.append(rec)
rows.sort(key=lambda x:(x['type'],x['folio']))
json.dump(rows,open('/home/ec2-user/tpw/fable/records.json','w'),indent=1)
with open('/home/ec2-user/tpw/fable/records.txt','w') as f:
    for x in rows:
        f.write(f"{x['folio']:4d} 0x{x['folio']:03x} t{x['type']} txt={x['text_id']:4d} fp={x['fp_w']}x{x['fp_h']} b9={x['b9']} ent={x['entrance']} exit={x['exit']} +14={x['b14']},{x['b15']},{x['b16']} int={x['base_intensity']} body={x['body_len']:#x}")
        if 'levels' in x:
            for L,l in enumerate(x['levels']): f.write(f"\n     L{L}: "+' '.join(f'{k}={v}' for k,v in l.items()))
        else: f.write(f" price={x['price']} u16s={x['u16s']}")
        f.write('\n')
from collections import Counter
print(Counter(x['type'] for x in rows), len(rows))
