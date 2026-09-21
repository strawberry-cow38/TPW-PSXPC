#!/usr/bin/env python3
"""PAL objective/world/ticket follow-up. Read-only disc input; worktree-only output.

This is a bounded static census plus execution of original decision slices. It is
NOT a console playthrough or a proof against arbitrary computed/aliased pointers.
Every slice has a passing and failing fixture. No empty universal checks.
"""
import json
import struct
from pathlib import Path
from audit_rating import Machine, IMAGE, BASE, u
from audit_scenario import overlays, initialized, EXT, OVL_BASE, GROUPS, sha, hx

ROOT = Path(__file__).resolve().parents[1]
MC, OBJ = 0x80180000, 0x80181000
OVLS = overlays((EXT/'TPW.OVL').read_bytes())
IMAGES = [('TPW.BIN', BASE, IMAGE)] + [(f'OVL{i}', OVL_BASE, d) for i,(d,_) in enumerate(OVLS)]
TARGETS = {0x80067cd8,0x80067dbc,0x800677b8,0x8006bfe4,0x800b9a60,
           0x8006be1c,0x8006be3c,0x8006be60,0x8006bef0,0x8006bf4c,
           0x800675a8,0x80067590,0x80067658,0x800675c0,
           0x8006be48,0x8006be54,0x8006be9c,0x8006bec4,
           0x800bcea0,0x800bcd00,0x800ba45c,0x80058f90,0x80023c0c,
           0x800547f0,0x80054818,0x80054840,0x80054868}
GLOBALS = {0x80103984,0x80103988,0x80103990,0x80102e88,0x800dddc4}
RANGES = [(0x800e1930,0x800e1ad0,'objective'),(0x80109b18,0x80109b38,'park_bits'),
          (0x8010537c,0x8010563c,'world_objects')]

def region(a):
    if a in GLOBALS: return 'global'
    return next((n for lo,hi,n in RANGES if lo<=a<hi),None)

def scan(name,base,data):
    calls=[]; pointers=[]; gp=[]; pairs=[]
    # Deliberately NO 16-instruction cutoff or linear clobber pruning. All preceding
    # same-register LUI high halves remain candidates (including data/false matches).
    high=[set() for _ in range(32)]
    for off in range(0,len(data)-3,4):
        x=struct.unpack_from('<I',data,off)[0];pc=base+off
        op,rs,rt,imm=x>>26,(x>>21)&31,(x>>16)&31,x&65535
        si=imm-65536 if imm&32768 else imm
        if op in (2,3):
            t=((pc+4)&0xf0000000)|((x&0x3ffffff)<<2)
            if t in TARGETS: calls.append(dict(site=hx(pc),target=hx(t)))
        if x in TARGETS or region(x): pointers.append(dict(site=hx(pc),target=hx(x)))
        if op==15: high[rt].add(imm<<16)
        if rs==28 and op in (9,32,33,34,35,36,37,38,40,41,42,43,46):
            v=u(0x80102654+si)
            if region(v): gp.append(dict(site=hx(pc),target=hx(v),op=op))
        if op in (9,13,32,33,34,35,36,37,38,40,41,42,43,46):
            for hi in high[rs]:
                v=(hi|imm) if op==13 else u(hi+si)
                if region(v): pairs.append(dict(site=hx(pc),target=hx(v),op=op))
    return dict(image=name,bytes=len(data),sha256=sha(data),aligned_words=len(data)//4,
                skipped_aligned_words=0,trailing_bytes=len(data)%4,calls=calls,
                pointer_candidates=pointers,gp_candidates=gp,address_pair_candidates=pairs)

def const(value):
    def f(m): m.r[2]=u(value)
    return f

class StopSlice(Exception): pass
def stop(m): raise StopSlice()

def vm(overlay=None):
    m=Machine()
    if overlay is not None:
        data=OVLS[overlay][0];at=OVL_BASE&0x1fffffff;m.mem[at:at+len(data)]=data
    m.write(0x80102e48,MC,4)
    return m

def common_award(game,restricted,bits):
    m=vm();events=[]
    m.write(OBJ+36,game,4);m.write(MC+32,bits,4);m.write(MC+36,0x81234567,4)
    m.write(0x80102d34,restricted,4)
    m.write(0x80103984,11,4);m.write(0x80103988,49,4)
    def event(tag,arg=None):
        def f(v): events.append([tag] if arg is None else [tag,v.r[arg]])
        return f
    m.hooks.update({0x80050530:const(OBJ+0x1000),0x80037fbc:event('hud'),
        0x800b99ec:event('text',5),0x80014118:event('message_init'),
        0x8001412c:event('message',5),0x80014144:event('advisor'),
        0x800ba5e4:event('message_destroy',5),0x800b9bf0:event('buttons'),
        0x800b8e08:event('sound'),0x800693c8:event('list',5),
        0x800bcea0:event('end_sequence')})
    m.call(0x800b9a60,OBJ)
    awarded=not restricted and not bits&(1<<(game+4))
    assert m.read(MC+32,4)==(bits|(1<<(game+4)) if awarded else bits)
    assert m.read(MC+36,4)==0x81234567
    assert m.read(0x80103984,4)==11+awarded and m.read(0x80103988,4)==49+awarded
    assert ['message',0xc5 if awarded else 0xc3] in events
    assert ['text',0x234 if awarded else 0x298] in events
    assert not any(e[0] in ('list','end_sequence') for e in events)
    return dict(game=game,restricted=bool(restricted),before_bits=bits,park_bits=m.read(MC+32,4),
                bonus_bits=m.read(MC+36,4),tickets=int(awarded),events=events,
                spendable=m.read(0x80103984,4),lifetime=m.read(0x80103988,4),instructions=m.steps)

def trigger_cases():
    out=[]
    def case(game,overlay,start,end,values,expected,register=16,regs=None,hooks=None,bits=0):
        m=vm(overlay);m.r[register]=OBJ;events=[]
        for off,size,v in values:m.write(OBJ+off,v,size)
        for r,v in (regs or {}).items():m.r[r]=u(v)
        m.write(MC+32,bits,4)
        if game==7:m.write(0x80103b44,OBJ+0x1000,4)
        m.hooks.update({0x800b9a60:lambda v:events.append('win'),
            0x800b9b64:lambda v:events.append('loss'),0x800b8e08:lambda v:None,
            0x800bdd0c:const(0),end:stop})
        m.hooks.update(hooks or {})
        try:m.call(start,OBJ)
        except StopSlice:pass
        won='win' in events;assert won==expected,(game,values,events)
        out.append(dict(game=game,overlay=overlay,start=hx(start),end_exclusive=hx(end),
            fields=[dict(offset=o,size=s,value=v) for o,s,v in values],park_bits=bits,
            expected_win=expected,events=events,instructions=m.steps))
    # READ: game 1, mallet: decrement u16 timer, at zero require signed hits >=21.
    for timer,hits in [(1,20),(1,21),(1,22),(2,21),(0,21),(1,-1)]:
        case(1,10,0x801146a4,0x801146f4,[(298,2,timer),(302,2,hits)],timer==1 and hits>=21)
    # READ: two strength games, different native field widths. Wait until gauge catches target.
    for game,ov,start,end,target,progress,size in [(2,9,0x80114a2c,0x80114b18,256,260,4),
            (3,8,0x801148d4,0x801149f8,220,222,2)]:
        for t,p in [(103,103),(104,104),(104,103),(104,105),(-1,-1)]:
            case(game,ov,start,end,[(target,size,t),(progress,size,p)],t>=104 and p>=t)
    # READ: Dino Racing: all five motion helpers idle; >=5 races and >=3 correct bets.
    for races,wins,active in [(4,3,0),(5,2,0),(5,3,0),(5,4,0),(6,3,0),(5,3,1)]:
        case(4,6,0x801143e8,0x801144ac,[(216,4,races),(228,4,wins)],
            races>=5 and wins>=3 and not active,register=18,regs={17:0,19:0},
            hooks={0x801147b4:const(active),0x800b9bf0:lambda v:None})
    # READ: exactly eight six-byte slots. Zero moving byte required; ninth hole is untested.
    for bad,moving in [(-1,0),(0,0),(7,0),(-1,1)]:
        values=[(188+i*6,2,0 if i==bad else i+1) for i in range(8)]+[(236,2,77),(244,1,moving)]
        case(5,5,0x80114850,0x801148b8,values,bad==-1 and moving==0)
    # READ: fortune compares returned answer against constructor random(20)+1; bit 10 precheck.
    for answer,target,bits in [(1,1,0),(20,20,0),(2,1,0),(1,1,1<<10)]:
        case(6,1,0x80114298,0x801142d0,[(184,4,target)],answer==target and not bits,
             register=19,regs={18:answer},bits=bits)
    # READ: kart +0x81 finished and +0x0C rank 1. Real getter 0x800AA23C executes.
    for finished,rank in [(0,1),(1,2),(1,1),(2,1)]:
        case(7,4,0x80114628,0x80114674,[(0x1081,1,finished),(0x100c,1,rank)],
             bool(finished) and rank==1,register=17)
    # READ: shooting gallery >=6 good targets, not already in local state 3.
    for hits,state in [(5,0),(6,0),(7,0),(6,3),(-1,0)]:
        case(8,0,0x801149b4,0x801149ec,[(728,2,hits),(40,2,state)],hits>=6 and state!=3,register=17)
    # READ: coconut shy exactly three successes, after active throws drain to zero.
    for active,hits in [(0,2),(0,3),(0,4),(1,3)]:
        case(9,7,0x80114a98,0x80114ad8,[(480,4,active),(484,4,hits)],active==0 and hits==3)
    assert len(out)==43
    for game in range(1,10):
        rows=[r for r in out if r['game']==game]
        assert len(rows)>0 and {r['expected_win'] for r in rows}=={True,False}
    return out

def ticket_input_controls():
    # READ: six 12-byte pad-sequence records at 0x800F320C; matcher 0x8006E22C.
    # Execute the two ticket flags and the already established research positive control.
    # The other three record effects are outside this ticket audit, explicitly counted.
    rows=[];toggles=[]
    for index in range(6):
        m=vm();a=0x800f320c+12*index;p=m.read(a,4);n=m.read(a+4,1)
        repeats=m.read(a+5,1);target=m.read(a+8,4)
        masks=[m.read(p+2*j,2) for j in range(n)]
        rows.append(dict(index=index,record=hx(a),sequence=hx(p),length=n,
            repetitions=repeats,target=hx(target),masks=masks))
        if index not in (0,2,4):continue
        pad=[0]
        m.hooks[0x8008974c]=lambda v:v.r.__setitem__(2,pad[0])
        m.hooks[0x800b8e08]=lambda v:None
        def press(mask):pad[0]=mask;m.call(0x8006e22c)
        initial=m.read(target,4);press(0x80000000);wrong=m.read(target,4)
        for mask in masks*repeats:press(mask)
        first=m.read(target,4)
        for mask in masks*repeats:press(mask)
        second=m.read(target,4)
        assert (initial,wrong,first,second)==(0,0,1,0)
        toggles.append(dict(index=index,initial=initial,after_wrong=wrong,
            after_sequence=first,after_second_sequence=second))
    assert len(rows)==6 and len(toggles)==3
    grants=[]
    for flag,held,pressed in [(0,128,512),(1,0,512),(1,128,0),(1,128,512)]:
        m=vm(11);m.write(0x80103744,flag,4)
        m.write(0x80103984,11,4);m.write(0x80103988,49,4)
        m.hooks.update({0x800898a8:const(held),0x8008974c:const(pressed),
            0x80037fbc:lambda v:None,0x80114dc8:stop})
        try:m.call(0x80114d54)
        except StopSlice:pass
        delta=2 if flag and held and pressed else 0
        assert (m.read(0x80103984,4),m.read(0x80103988,4))==(11+delta,49+delta)
        grants.append(dict(flag=flag,held_pad1=held,pressed_pad1=pressed,tickets=delta))
    balances=[]
    for flag in (0,1):
        m=vm();m.write(0x80102ec0,flag,4)
        m.write(0x80103984,11,4);m.write(0x80103988,49,4)
        value=m.call(0x8006be1c)
        assert value==(255 if flag else 11) and m.read(0x80103984,4)==value
        assert m.read(0x80103988,4)==49
        balances.append(dict(flag=flag,returned_and_stored=value,lifetime=49))
    return dict(records=rows,records_decoded=6,toggle_effects_executed=3,
        other_toggle_effects_not_executed=3,toggles=toggles,
        world_map_grant_controls=grants,balance_override_controls=balances)

def reference_dispositions(scans,reads):
    """Resolve every objective hit from the deliberately overinclusive high-half scan.
    Separately check EVERY use of each retained objective-pointer register, after
    acquisition and before its epilogue restore, in the two main-image consumers.
    No arbitrary alias analysis or indirect-call completeness is implied.
    """
    gp_control=[r for s in scans if s['image']=='TPW.BIN' for r in s['gp_candidates']
        if r['site']==hx(0x8006be3c)]
    assert gp_control==[dict(site=hx(0x8006be3c),target=hx(0x80103988),op=35)]
    rows=[]
    for scan,(_,base,data) in zip(scans,IMAGES):
        for r in scan['address_pair_candidates']:
            if not 0x800e1930<=int(r['target'],16)<0x800e1ad0:continue
            pc=int(r['site'],16);x=struct.unpack_from('<I',data,pc-base)[0];rs=(x>>21)&31
            if base==BASE and 0x80067cd8<=pc<0x80067dbc:
                rows.append(dict(**r,disposition='selector return'));continue
            if base==BASE and pc in (0x800f32a4,0x800f32a8):
                rows.append(dict(**r,disposition='character conversion data',
                    reader=hx(0x8006f2f0)));continue
            # All remaining 220 candidates have an explicit LUI 0x8010 immediately
            # before the operand (two have one intervening instruction). The stale
            # high 0x800E from elsewhere in the image is not their base.
            matches=[]
            for delta in (4,8):
                y=struct.unpack_from('<I',data,pc-base-delta)[0]
                if y>>26==15 and (y>>16)&31==rs and y&65535==0x8010:matches.append(pc-delta)
            assert matches,(scan['image'],r)
            rows.append(dict(**r,disposition='local high is 0x8010',lui=hx(max(matches)),
                actual_target=hx(0x80100000+(x&65535))))
    assert len(rows)==230
    assert sum(r['disposition']=='selector return' for r in rows)==8
    assert sum(r['disposition']=='local high is 0x8010' for r in rows)==220
    assert sum(r['disposition']=='character conversion data' for r in rows)==2
    m=vm();conversion=[]
    for char,encoded in [(65,0x8260),(97,0x8281)]:
        actual=m.call(0x8006f2f0,char);back=m.call(0x8006f270,encoded)
        assert actual==encoded and back==char
        conversion.append(dict(character=char,encoded=actual,decoded=back))
    pointer_uses=[]
    for start,end,reg in [(0x80067714,0x800677a4,16),(0x80067954,0x80067cc4,20)]:
        for pc in range(start,end,4):
            x=struct.unpack_from('<I',IMAGE,pc-BASE)[0]
            op,rs,rt,fn=x>>26,(x>>21)&31,(x>>16)&31,x&63
            uses=(rs==reg and op not in (2,3,15)) or (rt==reg and (op in (4,5,40,41,43) or (op==0 and fn!=8)))
            if not uses:continue
            if pc==0x80067954:
                assert op==4 and rs==20 and rt==0 # explicit null-definition guard
                pointer_uses.append(hx(pc));continue
            assert (pc,x&65535,4 if op==35 else 1) in reads and op in (35,36),(hex(pc),hex(x))
            pointer_uses.append(hx(pc))
    assert len(pointer_uses)==10
    # Supplemental function-address search includes constructed pointers, not just JAL
    # or pointer words. 0x800BA814's callback setup at 0x800BA6CC is the positive control.
    functions={0x8006bfe4,0x800677b8,0x800b9a60,0x8006bf4c,0x8006be3c,0x800675f4,0x800ba814}
    split_functions=[];live_bonus_count_calls=[];function_words=[]
    for name,base,data in IMAGES:
        high=[set() for _ in range(32)]
        for off in range(0,len(data)-3,4):
            x=struct.unpack_from('<I',data,off)[0];pc=base+off
            op,rs,rt,imm=x>>26,(x>>21)&31,(x>>16)&31,x&65535
            if op==15:high[rt].add(imm<<16)
            if x in functions:function_words.append(dict(image=name,site=hx(pc),target=hx(x)))
            if op in (2,3) and ((pc&0xf0000000)|((x&0x3ffffff)<<2))==0x800675f4:
                live_bonus_count_calls.append(dict(image=name,site=hx(pc)))
            if op in (9,13):
                for hi in high[rs]:
                    v=(hi|imm) if op==13 else u(hi+(imm-65536 if imm&32768 else imm))
                    if v in functions:split_functions.append(dict(image=name,site=hx(pc),target=hx(v)))
    assert any(r['site']==hx(0x800ba6cc) and r['target']==hx(0x800ba814) for r in split_functions)
    assert live_bonus_count_calls==[dict(image='TPW.BIN',site=hx(0x80082fc8))]
    assert not [r for r in split_functions+function_words if r['target']!=hx(0x800ba814)]
    # Positive-write control: constructor overwrites all 172 record bytes while leaving
    # the four adjacent bytes intact, on all four records, even with nonzero sentinels.
    m=vm();reference=initialized();gaps=[]
    addresses=[reference.read(0x800dddc4+i*4,4) for i in range(4)]
    for a in addresses:
        for i in range(0xb0):m.write(a+i,0x5a,1)
    def memset(v):
        for i in range(v.r[6]):v.write(v.r[4]+i,v.r[5],1)
        v.r[2]=v.r[4]
    m.hooks[0x80012c90]=memset;m.call(0x8002ed50)
    for a in addresses:
        assert all(m.read(a+i,1)==reference.read(a+i,1) for i in range(0xac))
        assert all(m.read(a+i,1)==0x5a for i in range(0xac,0xb0))
        gaps.append(dict(address=hx(a),rewritten_bytes=0xac,untouched_gap_bytes=4,sentinel=0x5a))
    result=dict(source_audit_sha256=sha((ROOT/'findings/objbytes-audit.json').read_bytes()),
        objective_address_candidates=len(rows),classified=len(rows),unclassified=0,
        dispositions=rows,gp_positive_control=gp_control,
        character_table_controls=conversion,pointer_register_uses=pointer_uses,
        pointer_register_ranges=[dict(start=hx(a),end_exclusive=hx(b),register=r) for a,b,r in
            [(0x80067714,0x800677a4,16),(0x80067954,0x80067cc4,20)]],
        constructed_function_candidates=split_functions,function_pointer_words=function_words,
        live_bonus_count_calls=live_bonus_count_calls,constructor_gap_controls=gaps,
        ticket_input_controls=ticket_input_controls(),
        scope='Conventional literal/GP references plus the selector provenance slice, not arbitrary aliases')
    (ROOT/'findings/objbytes-references.json').write_text(json.dumps(result,indent=2)+'\n')

def main():
    scans=[scan(*im) for im in IMAGES]
    m=initialized();worlds=[]
    for w in range(4):
        a=m.read(0x800dddc4+w*4,4);m.write(0x801038a0,w,4);groups=[]
        for off,getter,name in [(0x88,0x800547f0,'grass'),(0x90,0x80054818,'path'),(0x98,0x80054840,'queue')]:
            p=m.read(a+off,4);n=m.read(a+off+4,4);assert m.call(getter)==p
            groups.append(dict(kind=name,record_offset=off,source=hx(p),count=n,
                element_bytes=2,sprites=[m.read(p+i*2,2) for i in range(n)]))
        choices=[]
        for rng in (0,1,2,0xffffffff):
            m.hooks[0x800c25b8]=const(rng);v=m.call(0x80054868)
            assert v==groups[0]['sprites'][rng%groups[0]['count']]
            choices.append(dict(random=rng,sprite=v))
        worlds.append(dict(world=w,address=hx(a),initialized_bytes=0xac,spacing=0xb0,
            groups=groups,ground_texture=m.read(a+0xa0,4),extra_textures=[m.read(a+0xa4+p*4,4) for p in range(2)],
            random_grass_controls=choices))
    # All catalogue occurrences are checked, not only sideshows with a promising name.
    gaz=(EXT/'FOLIO.GAZ').read_bytes();parks=[];occurrences=0
    for w in range(4):
        a=m.read(0x800dddc4+w*4,4)
        for p in range(2):
            games=[];zero=0
            for typ,off,_ in GROUPS:
                if typ==8:continue # separately accounted: 10 upgrades, no ordinary descriptor.
                ptr=m.read(a+off+p*4,4);n=m.read(a+off+8+p*4,4)
                for i in range(n):
                    occurrences+=1;e=m.read(ptr+i*4,4);eo,es=struct.unpack_from('<II',gaz,8+8*e)
                    data=gaz[eo:eo+es];r=struct.unpack_from('<I',data,20)[0]
                    game=struct.unpack_from('<H',data,r+22)[0]
                    if game:
                        assert 1<=game<=9
                        games.append(dict(entry=e,type=typ,descriptor_offset=r,game=game,bit=game+4))
                    else:zero+=1
            ids={g['game'] for g in games};advertised=IMAGE[0x800e1930+(w*2+p)*52+49-BASE]
            tutorial=IMAGE[0x800e1930+(w*2+p)*52+48-BASE]!=0
            assert advertised==3+tutorial+len(ids)
            parks.append(dict(world=w,park=p,games=games,ordinary_zero_game_occurrences=zero,
                advertised=advertised,tutorial=tutorial,expected_park_bits=sum(1<<i for i in [1,2,3]+([4] if tutorial else [])+[g+4 for g in ids])))
    assert occurrences==253 and sum(len(p['games']) for p in parks)==20
    # The objective-pointer consumers are a manually read, fully enumerated provenance slice.
    reads=[(0x80067728,12,4),(0x8006775c,16,4),(0x80067790,20,4),
           (0x80067980,12,4),(0x800679c8,16,4),(0x80067a60,20,4),
           (0x80067a90,48,1),(0x80067bf0,24,4),(0x80067c8c,28,4),
           (0x8011715c,49,1),(0x801171a8,49,1)]
    read_rows=[]
    for pc,off,size in reads:
        data=IMAGE if pc<OVL_BASE else OVLS[11][0];base=BASE if pc<OVL_BASE else OVL_BASE
        ins=struct.unpack_from('<I',data,pc-base)[0]
        assert ins>>26==(35 if size==4 else 36) and ins&65535==off
        read_rows.append(dict(image='TPW.BIN' if pc<OVL_BASE else 'OVL11',site=hx(pc),offset=off,size=size))
    used={b for _,o,n in reads for b in range(o,o+n)};unread=sorted(set(range(52))-used)
    assert len(used)==22 and len(unread)==30
    # Run popcounts on sparse/high bits (non-vacuous domain controls), then all actual tickets.
    counts=[];m=vm()
    for bits in (0,1,1<<14,1<<15,0x80000000,0x7fff,0xffffffff):
        m.write(0x80109b18,bits,4);m.write(0x80103990,bits,4)
        park=m.call(0x8006bef0,0,0);bonus=m.call(0x8006be60)
        assert park==bin(bits&0x7fff).count('1') and bonus==bin(bits&31).count('1')
        counts.append(dict(bits=bits,park=park,bonus=bonus))
    for i,p in enumerate(parks):m.write(0x80109b18+i*4,p['expected_park_bits'],4)
    m.write(0x80103990,31,4);assert m.call(0x8006bf4c)==50
    # Execute the OVL11 total-display arithmetic at full completion. Stop only on entering
    # formatting: no branch on 50 or all masks precedes it in this slice.
    m=vm(11)
    for i,p in enumerate(parks):m.write(0x80109b18+i*4,p['expected_park_bits'],4)
    m.write(0x80103990,31,4);m.r[23]=OBJ;m.write(OBJ+51,0,1)
    m.hooks[0x800c41e4]=stop
    try:m.call(0x8011712c)
    except StopSlice:pass
    display=dict(earned=m.r[6],total=m.r[7],park_remaining=m.read(m.r[29]+16,4),
                 park_total=m.read(m.r[29]+20,4),bonus_remaining=m.read(m.r[29]+24,4))
    assert display==dict(earned=50,total=50,park_remaining=0,park_total=7,bonus_remaining=0)
    common=[common_award(g,r,b) for g in range(1,10) for r,b in [(0,0x80000001),(0,0x80000001|(1<<(g+4))),(1,0x80000001)]]
    cases=trigger_cases()
    ov11=OVLS[11][0];graph=[]
    for i in range(19):
        at=0x801141f4+i*28;row=ov11[at-OVL_BASE:at-OVL_BASE+28]
        graph.append(dict(index=i,address=hx(at),kind=row[0],world=row[6],park=row[7]) if i<8
                     else dict(index=i,address=hx(at),kind=row[0],a=row[24],b=row[25],cost=row[26]))
    controls=dict(all_13_images=len(scans)==13,all_12_overlays=len(OVLS)==12,
        all_words_counted=sum(s['aligned_words'] for s in scans)==296961 and all(s['trailing_bytes']==0 for s in scans),
        objective_address_positive={hx(0x800e1930+i*52) for i in range(8)} <= {r['target'] for r in scans[0]['address_pair_candidates']},
        pointer_positive=any(r['target']==hx(0x80102e88) for r in scans[0]['pointer_candidates']),
        weekly_grant_positive=any(c['site']==hx(0x800677c8) and c['target']==hx(0x8006bfe4) for c in scans[0]['calls']),
        end_sequence_positive=any(c['site']==hx(0x8001347c) and c['target']==hx(0x800bcea0) for c in scans[0]['calls']),
        all_8_park_totals=len(parks)==8,all_4_worlds=len(worlds)==4,
        all_9_minigames_controlled=len({c['game'] for c in cases})==9,
        common_27_cases=len(common)==27,full_50_formats_progress=display['earned']==50)
    assert len(controls)==12 and all(controls.values())
    result=dict(controls=controls,scans=scans,overlays=[meta for _,meta in OVLS],
        objective=dict(bytes_named_before=22,newly_named_of_remaining_30=0,unread_in_provenance_slice=unread,
            reads=read_rows,records=8,skipped_records=0,scope='Selector and wrapper direct consumers; conventional literal/GP references. Arbitrary computed aliases excluded.'),
        worlds=worlds,parks=parks,catalogue=dict(ordinary_examined=occurrences,type8_excluded=10,nonzero_game_occurrences=20),
        common_award_cases=common,trigger_cases=cases,popcount_cases=counts,full_completion_display=display,
        world_map=graph,limitations=['No console measurement','Partial decision slices, not complete minigame simulation',
            'Candidate scans are not a whole-program alias proof','No semantic names for the 30 unread objective bytes'])
    out=ROOT/'findings/objbytes-audit.json';out.write_text(json.dumps(result,indent=2)+'\n')
    reference_dispositions(scans,reads)
    print(json.dumps(dict(controls=len(controls),images=len(scans),aligned_words=sum(s['aligned_words'] for s in scans),
        skipped_aligned_words=0,objective_read_bytes=len(used),remaining_bytes=len(unread),worlds=len(worlds),
        parks=len(parks),common_cases=len(common),trigger_cases=len(cases)),indent=2))

if __name__=='__main__':main()
