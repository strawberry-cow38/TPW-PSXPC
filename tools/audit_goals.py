#!/usr/bin/env python3
"""Read-only PAL image audit; outputs only in this worktree. No live gameplay claim.
Execute the original weekly decision branches with documented live-quantity/effect stubs.
Scan EVERY aligned TPW.BIN word for direct calls/pointers, including non-code candidates.
"""
import hashlib
import json
import struct
from pathlib import Path
from audit_rating import Machine, IMAGE, BASE, s, u

ROOT = Path(__file__).resolve().parents[1]
AWARDS = (0xAF, 0xB0, 0xB1, 0xBC, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6)
MC = 0x80180000

def weekly(admissions=0, income=0, spending=0, loans=0, year=0, month=0, opened=0,
           park_open=False, sandbox=False, world=0, park=0):
    m = Machine(); events=[]
    m.write(0x801038A0,world,4); m.write(0x801038A4,park,4)
    m.write(MC+28,admissions,4); m.write(MC+4,month,4); m.write(MC+8,year,4)
    m.write(MC+32,0xfffffff1,4); m.write(MC+36,0xffffffff,4)
    def constant(v):
        def hook(vm): vm.r[2]=u(v)
        return hook
    def getbit(vm): vm.r[2]=int(bool(vm.read(MC+32,4) & (1 << (vm.r[5]&31))))
    def setbit(vm): vm.write(MC+32,vm.read(MC+32,4)|(1 << (vm.r[5]&31)),4)
    def profit(vm): vm.write(vm.r[4],income-loans-spending,4)
    def award(vm): events.append(vm.r[5])
    m.hooks.update({0x80086814:constant(0x80181000),0x80059a9c:constant(sandbox),
                   0x800675a8:getbit,0x80067658:setbit,0x80067590:constant(1),
                   0x800873d8:profit,0x800541ac:constant(park_open),0x8005ba80:constant(opened),
                   0x800677b8:award})
    m.call(0x80067928,MC)
    return dict(admissions=admissions,income=income,spending=spending,loans=loans,year=year,
                month=month,opened=opened,park_open=park_open,sandbox=sandbox,world=world,park=park,
                messages=events,park_bits=m.read(MC+32,4),instructions=m.steps)

def main():
    records=[]
    for i in range(8):
        address=0x800e1930+i*52
        data=IMAGE[address-BASE:address-BASE+52]
        m=Machine(); selected=m.call(0x80067cd8,i//2,i%2)
        assert selected==address
        records.append(dict(world=i//2,park=i%2,address=f'0x{address:08X}',hex=data.hex().upper(),
                            words=list(struct.unpack('<12I',data[:48])),tail=list(data[48:])))
    cases=[weekly(admissions=v) for v in (99,100,101,0xffffffff)]
    cases += [weekly(income=i,spending=e,loans=l) for i,e,l in
              [(20000,0,0),(20001,0,0),(30001,10000,0),(30000,10000,0),
               (30001,0,10000),(30000,0,10000),(-1,0,0),(0x7fffffff,0,-1)]]
    cases += [weekly(year=y,month=m,opened=o,park_open=p) for y,m,o,p in
              [(1,4,5,True),(1,5,5,True),(1,6,5,True),(1,5,5,False),(0,0,65535,True),(0,0,0,True)]]
    cases += [weekly(admissions=1000,income=90000,year=20,park_open=True,world=w,park=p)
              for w in range(4) for p in range(2)]
    cases += [weekly(admissions=1000,income=90000,year=20,park_open=True,sandbox=True),
              weekly(admissions=1000,world=4)]
    targets=[0x80067928,0x800676dc,0x80067cd8,0x800677b8,0x8005b830,0x80017024,
             0x800675a8,0x80067590,0x80067658,0x800675c0]
    calls={f'0x{t:08X}':[] for t in targets}; pointers={f'0x{t:08X}':[] for t in targets}
    for off in range(0,len(IMAGE)-3,4):
        word=struct.unpack_from('<I',IMAGE,off)[0];pc=BASE+off
        if word in targets:pointers[f'0x{word:08X}'].append(f'0x{pc:08X}')
        if word>>26 in (2,3):
            target=(pc&0xf0000000)|((word&0x3ffffff)<<2)
            if target in targets:calls[f'0x{target:08X}'].append(f'0x{pc:08X}')
    body_calls={k:[a for a in v if 0x80067928<=int(a,16)<0x80067cd8] for k,v in calls.items()}
    # Original award wrapper, not its effect stub: prove both posting routes and ticket count.
    effects=[];m=Machine()
    def log(label):
        def hook(vm):effects.append([label,vm.r[4] if label=='tickets' else vm.r[5]])
        return hook
    m.hooks.update({0x8006bfe4:log('tickets'),0x80014118:lambda vm:None,
                    0x8001412c:log('message'),0x80014144:lambda vm:effects.append(['advisor']),
                    0x800693c8:log('list_type')})
    m.call(0x800677b8,MC,0xAF)
    # Original net-profit helper; loan-total loop is the only arithmetic stub.
    profit_cases=[]
    for income,spending,loans in [(12345,2345,1000),(20001,0,0),(0x7fffffff,0,-1)]:
        m=Machine();bank=0x80181000;out=0x80182000
        m.write(bank+0x12d8,income,4);m.write(bank+0x12d4,spending,4)
        def loan(vm):vm.write(vm.r[4],loans,4)
        m.hooks[0x8008748c]=loan
        m.call(0x800873d8,out,bank)
        profit_cases.append(dict(income=income,spending=spending,loans=loans,raw=s(m.read(out,4))))
    gaz=Path('/home/ec2-user/tpw/ext/FOLIO.GAZ').read_bytes()
    off,size=struct.unpack_from('<II',gaz,8+8*0x197);strings=gaz[off:off+size]
    captions=[]
    for ident in [0x8d,*AWARDS]:
        addr=0x800ee4fc+20*ident;tid=struct.unpack_from('<H',IMAGE,addr-BASE)[0]
        at=struct.unpack_from('<I',strings,4+4*tid)[0]
        captions.append(dict(id=ident,record=f'0x{addr:08X}',text_id=tid,
            english=strings[at:strings.index(b'\0',at)].decode('latin1')))
    controls=dict(records_nonempty=len(records)==8,decision_cases_nonempty=len(cases)==28,
                  failing_admissions=cases[1]['messages']==[],passing_admissions=cases[2]['messages']==[0xaf],
                  original_money=all(c['raw']==s(c['income']-c['spending']-c['loans']) for c in profit_cases),
                  wrapper_effects=effects==[['tickets',1],['message',0xaf],['advisor'],['list_type',2]],
                  weekly_caller=calls['0x80067928']==['0x80066E4C'],
                  nine_award_calls=len(body_calls['0x800677B8'])==9,
                  rating_positive_control=calls['0x8005B830']==['0x80067100'],
                  interpreter_positive_control=len(calls['0x80017024'])>0,
                  no_direct_rating_call_in_weekly=body_calls['0x8005B830']==[],
                  no_direct_interpreter_call_in_weekly=body_calls['0x80017024']==[])
    result=dict(image_sha256=hashlib.sha256(IMAGE).hexdigest(),
        census=dict(bytes=len(IMAGE),aligned_words=len(IMAGE)//4,skipped_aligned_words=0,
                    trailing_bytes=len(IMAGE)%4,records=8,records_skipped=0,decision_cases=len(cases),
                    overlays_scanned=0,overlays_not_scanned=12),
        controls=controls,direct_call_candidates=calls,pointer_candidates=pointers,weekly_body_calls=body_calls,
        records=records,decision_cases=cases,profit_cases=profit_cases,wrapper_effects=effects,captions=captions,
        limitations=['Synthetic RAM; no emulator/live gameplay measurement.',
            'Weekly oracle stubs bit helpers, live profit, open/sandbox getters and award effects; compiled selector, comparisons, calendar getters and Money conversion execute.',
            'All hidden awards are masked in weekly decision fixtures; C# tests cover them independently from the disassembly.',
            'Profit helper executes original subtraction; outstanding-loan summation is stubbed.',
            'Direct calls/pointer words are candidates, including data. No indirect/aliased-write exclusion is claimed.',
            'No overlay or level-script sweep; no claim that other awards or terminal paths do not exist.'])
    (ROOT/'findings/goals-audit.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(dict(census=result['census'],controls=controls),indent=2))
    assert len(controls)>0 and all(controls.values())
if __name__=='__main__':main()
