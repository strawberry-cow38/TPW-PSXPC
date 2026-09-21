#!/usr/bin/env python3
"""Read PAL producers, execute the world initializer/selectors, account for every input.
Only this worktree receives output. No roster/asset payload is redistributed in the audit.
"""
import hashlib
import json
import struct
from collections import Counter
from pathlib import Path
from audit_rating import Machine, IMAGE, BASE

ROOT = Path(__file__).resolve().parents[1]
EXT = Path('/home/ec2-user/tpw/ext')
ISO = Path('/home/ec2-user/tpw/tpw_psx.iso')
OVL_BASE = 0x80114158
# READ: count getters 6A214/244/274/2A4/2D4/304/334/364 and 69A08's type arguments.
GROUPS = [(3, 8, 0x8006a214), (7, 24, 0x8006a244), (6, 40, 0x8006a274),
          (8, 56, 0x8006a2a4), (1, 72, 0x8006a2d4), (2, 88, 0x8006a304),
          (4, 104, 0x8006a334), (5, 120, 0x8006a364)]

def sha(d): return hashlib.sha256(d).hexdigest()
def word(d, off): return struct.unpack_from('<I', d, off)[0]
def hx(a): return f'0x{a:08X}'

def overlays(packed):
    """Existing SubLz format, 800BFD9C. Require terminator AND exact input consumption."""
    result = []
    for index in range(word(packed, 0)):
        offset, size = struct.unpack_from('<II', packed, 8 + index * 8)
        src = packed[offset:offset+size]; out = bytearray(); i = flags = 0
        while True:
            flags >>= 1
            if flags & 0xff00 == 0: flags = src[i] | 0xff00; i += 1
            if not flags & 1: out.append(src[i]); i += 1; continue
            c = src[i]; i += 1
            if c >= 0x60: distance, length = 256-c, 2
            else:
                distance = ((c & 15) << 8) | src[i]; i += 1
                if distance == 0: break
                length = (c >> 4) + 3
                if c >> 4 == 5: length = src[i] + 8; i += 1
            assert 0 < distance <= len(out)
            for _ in range(length): out.append(out[-distance])
            assert len(out) < 0x200000
        assert i == size
        result.append((bytes(out), dict(index=index, file_offset=offset, compressed_bytes=size,
            decoded_bytes=len(out), consumed_bytes=i, sha256=sha(out))))
    return result

def initialized():
    m = Machine()
    def memset(vm):
        for i in range(vm.r[6]): vm.write(vm.r[4]+i, vm.r[5], 1)
        vm.r[2] = vm.r[4]
    m.hooks[0x80012c90] = memset
    m.call(0x8002ed50)
    return m

def disc_files():
    with ISO.open('rb') as f:
        head = f.read(24)
        raw = head[:12] == b'\0'+b'\xff'*10+b'\0'
        stride = 2352 if raw else 2048
        skip = (24 if head[15] == 2 else 16) if raw else 0
        def read(lba, length):
            data = bytearray()
            for i in range((length+2047)//2048):
                f.seek((lba+i)*stride+skip); data.extend(f.read(2048))
            return bytes(data[:length])
        pvd = read(16, 2048); assert pvd[:7] == b'\x01CD001\x01'
        found = []
        def walk(lba, size, prefix=''):
            d = read(lba, size); i = 0
            while i < len(d):
                n = d[i]
                if not n: i = (i//2048+1)*2048; continue
                assert n >= 33 and i+n <= len(d)
                name = d[i+33:i+33+d[i+32]]
                if name not in (b'\0', b'\1'):
                    name = prefix + name.decode('ascii').split(';')[0]
                    rec = dict(name=name, lba=word(d,i+2), bytes=word(d,i+10),
                               directory=bool(d[i+25]&2))
                    found.append(rec)
                    if rec['directory']: walk(rec['lba'], rec['bytes'], name+'/')
                i += n
        walk(word(pvd,158), word(pvd,166))
        for rec in found:
            if rec['name'] in ('TPW.BIN','TPW.OVL','FOLIO.GAZ','SLES_026.88','SYSTEM.CNF'):
                d = read(rec['lba'],rec['bytes'])
                assert d == (EXT/rec['name']).read_bytes()
                rec['extracted_sha256'] = sha(d)
                rec['first_iso_byte'] = rec['lba']*stride+skip
        return dict(sector_bytes=stride, payload_offset=skip, files=found,
                    directory_entries=len(found), extracted_files_compared=5,
                    other_files_not_payload_scanned=len(found)-5)

def scan(name, base, data):
    targets = {0x80067cd8,0x80067dbc,0x80017024,0x80059aa8,0x8006e22c}
    calls = {hx(t):[] for t in targets}; pointers=[]; refs=set(); gp=[]
    for off in range(0,len(data)-3,4):
        x=word(data,off); pc=base+off; op=x>>26; rs=(x>>21)&31; rt=(x>>16)&31
        imm=x&65535; si=imm-65536 if imm&32768 else imm
        if op in (2,3):
            t=(pc&0xf0000000)|((x&0x3ffffff)<<2)
            if t in targets: calls[hx(t)].append(hx(pc))
        if 0x800e1930<=x<0x800e1ad0 or x==0x80102e88:
            pointers.append(dict(at=hx(pc),target=hx(x)))
        if rs==28 and op in (32,33,35,36,37,40,41,43) and 0x80102654+si==0x80102e88:
            gp.append(hx(pc))
        if op==15:
            # Conservative candidates across branches. Linear clobber tracking MISSES the
            # selector's return delay slots. The eight real selectors are the positive control.
            for o in range(off+4,min(off+68,len(data)-3),4):
                y=word(data,o); q=y>>26; s=(y>>21)&31; lo=y&65535
                if s!=rt or q not in (8,9,13,32,33,35,36,37,40,41,43): continue
                signed=lo-65536 if lo&32768 else lo
                v=((imm<<16)|lo) if q==13 else ((imm<<16)+signed)&0xffffffff
                if 0x800e1930<=v<0x800e1ad0 or v==0x80102e88: refs.add((hx(base+o),hx(v)))
    return dict(image=name,bytes=len(data),aligned_words=len(data)//4,
        skipped_aligned_words=0,trailing_bytes=len(data)%4,calls=calls,
        pointer_candidates=pointers,address_pair_candidates=sorted(refs),research_gp_accesses=gp)

def main():
    gaz=(EXT/'FOLIO.GAZ').read_bytes(); packed=(EXT/'TPW.OVL').read_bytes()
    ovls=overlays(packed); m=initialized(); initializer_steps=m.steps
    entries=[struct.unpack_from('<II',gaz,8+i*8) for i in range(word(gaz,0))]
    rip=list((EXT/'rip').iterdir()); numeric=[p for p in rip if p.stem.isdigit() and p.suffix=='.bin']
    assert len(numeric)==len(entries)
    for i,(off,size) in enumerate(entries): assert gaz[off:off+size]==(EXT/'rip'/f'{i:04}.bin').read_bytes()
    parks=[]; layouts=[]; source_types=Counter(); source_definitions=set(); map_records=[]
    for w in range(4):
        a=m.read(0x800dddc4+w*4,4)
        for p in range(2):
            manager=0x80180000; m.write(manager+4,p,4);m.write(manager+8,w,4)
            obj=m.call(0x80067cd8,w,p)
            assert obj==0x800e1930+(w*2+p)*52
            groups=[]; layout=[]
            for t,o,getter in GROUPS:
                addr=m.read(a+o+p*4,4);n=m.read(a+o+8+p*4,4);stride=8 if t==8 else 4
                assert m.call(getter,manager)==n
                raw=bytes(m.mem[(addr&0x1fffffff):(addr&0x1fffffff)+n*stride])
                assert raw==IMAGE[addr-BASE:addr-BASE+n*stride]
                layout.append((addr,n))
                for i in range(n):
                    entry=word(raw,i*stride); assert entry<len(entries)
                    if t!=8: assert m.call(0x8006a3cc,manager,t,i)==entry
                    eo,es=entries[entry];payload=gaz[eo:eo+es]
                    ro=word(payload,20) if t!=8 else 0
                    assert ro+0x2c<=len(payload)
                    assert word(payload,ro)==t
                    source_types[t]+=1;source_definitions.add(entry)
                groups.append(dict(type=t,source_address=hx(addr),file_offset=addr-BASE,
                    count=n,stride=stride,bytes=len(raw),sha256=sha(raw)))
            map_addr=m.read(a,4)+p*8; map_id=m.read(map_addr,4); scenery=m.read(map_addr+4,4)
            off,size=entries[map_id];d=gaz[off:off+size];colors=word(d,0);at=4+colors*4
            width,height=struct.unpack_from('<II',d,at);end=at+8+width*height*8
            scenery_count=word(d,end);end+=4+12*scenery_count
            spawns=word(d,end);end+=4+spawns*2;assert end==len(d)
            map_records.append(dict(world=w,park=p,entry=map_id,archive_offset=off,bytes=size,
                width=width,height=height,scenery_entry=scenery,scenery_count=scenery_count,
                spawn_count=spawns,parsed_bytes=end,unparsed_bytes=size-end))
            parks.append(dict(world=w,park=p,objective_address=hx(obj),objective_file_offset=obj-BASE,
                objective_sha256=sha(IMAGE[obj-BASE:obj-BASE+52]),
                advertised_gold_tickets=IMAGE[obj-BASE+49],map_pair_address=hx(map_addr),groups=groups))
            layouts.append(layout)
    # The producer's preload walk independently confirms the seven ordinary groups' order.
    preloads=[]
    for w in range(4):
        for p in range(2):
            loaded=[];m.hooks[0x8002f5cc]=lambda vm:loaded.append(vm.r[4])
            m.call(0x8002eb98,w,p)
            expected=[]
            for g in parks[w*2+p]['groups']:
                if g['type']==8:continue
                expected += [word(IMAGE,g['file_offset']+i*g['stride']) for i in range(g['count'])]
            assert loaded==expected
            preloads.append(dict(world=w,park=p,entries=len(loaded),ordered_sha256=sha(struct.pack('<'+'I'*len(loaded),*loaded))))
    scans=[scan('TPW.BIN',BASE,IMAGE)]+[scan('TPW.OVL/'+str(i),OVL_BASE,d) for i,(d,_) in enumerate(ovls)]
    # Input-table producer: execute original pad matcher including its indirect stores.
    cheat=0x800f3224; sequence=m.read(cheat,4);length=m.read(cheat+4,1);repetitions=m.read(cheat+5,1)
    masks=[m.read(sequence+i*2,2) for i in range(length)]; vm=Machine();pad=[0]
    vm.hooks[0x8008974c]=lambda v:setattr(v,'r',v.r[:2]+[pad[0]]+v.r[3:])
    vm.hooks[0x800b8e08]=lambda v:None
    def press(mask):pad[0]=mask;vm.call(0x8006e22c)
    press(0xffff);press(0x80000000) # wrong nonzero input resets sequences
    assert vm.read(0x80102e88,4)==0
    for mask in masks*repetitions:press(mask)
    first=vm.read(0x80102e88,4)
    for mask in masks*repetitions:press(mask)
    second=vm.read(0x80102e88,4)
    # Reuse established script decoder; do not generate or change its production rules.
    from statistics import decode as rules_decode
    rules=rules_decode(IMAGE,gaz)['rules'];ops=Counter(i['op'] for r in rules for i in r['instructions'])
    posted={i['args'][0] for r in rules for i in r['instructions'] if i['op']=='PostMessage'}
    controls=dict(all_eight_selectors=True,all_64_count_getters=True,all_ordinary_entry_getters=True,
        all_eight_preload_orders=True,all_422_rip_entries_match=True,all_eight_maps_end_exactly=True,
        selector_address_search_positive=len({v for _,v in scans[0]['address_pair_candidates'] if 0x800e1930<=int(v,16)<0x800e1ad0})==8,
        interpreter_search_positive=scans[0]['calls'][hx(0x80017024)]==[hx(0x80016994)],
        research_pointer_positive=any(x['at']==hx(0x800f322c) for x in scans[0]['pointer_candidates']),
        unlock_sequence_sets_and_clears=(first,second)==(1,0),
        script_post_positive=0x7d in posted,
        no_nine_weekly_award_messages_in_scripts=not posted.intersection({0xaf,0xb0,0xb1,0xbc,0xb2,0xb3,0xb4,0xb5,0xb6}))
    out=dict(binary_sha256=sha(IMAGE),archive_sha256=sha(gaz),overlay_sha256=sha(packed),disc=disc_files(),
        census=dict(rip_files=len(rip),numeric_entries_scanned=len(numeric),numeric_entries_skipped=0,
            derivative_files_not_rescanned=len(rip)-len(numeric),parks=8,park_records_skipped=0,
            catalogue_groups=64,catalogue_occurrences=sum(source_types.values()),
            catalogue_occurrences_skipped=0,unique_catalogue_assets=len(source_definitions),
            occurrences_by_type=dict(source_types),overlays_scanned=len(ovls),overlays_skipped=0),
        initializer=dict(address=hx(0x8002ed50),instructions=initializer_steps,stub='memset only'),
        parks=parks,maps=map_records,preload_controls=preloads,overlays=[v for _,v in ovls],scans=scans,
        research_toggle=dict(record_address=hx(cheat),file_offset=cheat-BASE,sequence_address=hx(sequence),
            sequence_file_offset=sequence-BASE,length=length,repetitions=repetitions,
            target_address=hx(m.read(cheat+8,4)),initial=0,after_sequence=first,after_second_sequence=second),
        scripts=dict(schedule_entry=1,schedule_offset=entries[1][0],schedule_bytes=entries[1][1],
            code_entry=2,code_offset=entries[2][0],code_bytes=entries[2][1],rules=len(rules),
            rules_skipped=0,opcodes=dict(ops)),controls=controls,
        limitations=['Static PAL analysis, no console measurement.',
            'All aligned direct calls and pointers scanned, including data candidates; indirect calls/aliases not excluded.',
            'Address-pair search is conservative within 16 following words, not a proof against arbitrary pointer construction.',
            'No signature sweep for unknown VMs or compressed park streams; known map/catalogue producers were followed.',
            'Media and padding payloads are not rescanned; disc-check.md and psx-assets.md retain their scope.'])
    (ROOT/'findings/scenario-audit.json').write_text(json.dumps(out,indent=2)+'\n')
    print(json.dumps(dict(census=out['census'],controls=controls),indent=2))
    assert all(controls.values())
    return layouts

if __name__=='__main__':main()
