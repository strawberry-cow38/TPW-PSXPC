#!/usr/bin/env python3
"""Run all inherited objective mutations plus the additive minigame/display rules.
Freeze tests; require the same nonzero executed count for every mutant. Compile
errors, missing results and skips are invalid, never kills. Restore on failure.
"""
import json
import signal
import subprocess
import mutate_goals as g

g.OUT=g.ROOT/'findings/objbytes-mutations.json'
g.WORK=g.ROOT/'tests/TPW.Sim.Tests/obj/objbytes-mutations'
for name,old,new in [
    ('minigame lower ID','minigameId < 1','minigameId < 0'),
    ('minigame upper ID','minigameId > 9','minigameId > 10'),
    ('minigame bit too low','int bit = minigameId + 4;','int bit = minigameId + 3;'),
    ('minigame bit too high','int bit = minigameId + 4;','int bit = minigameId + 5;'),
    ('minigame sandbox','if (restrictedMode || Has(false, bit))','if (!restrictedMode || Has(false, bit))'),
    ('minigame no sandbox','if (restrictedMode || Has(false, bit))','if (Has(false, bit))'),
    ('minigame replay','if (restrictedMode || Has(false, bit))','if (restrictedMode)'),
    ('minigame wrong state gate','if (restrictedMode || Has(false, bit))','if (restrictedMode || Has(true, bit))'),
    ('minigame feedback text','return new(0x298, 0xC3, null);','return new(0x234, 0xC3, null);'),
    ('minigame feedback message','return new(0x298, 0xC3, null);','return new(0x298, 0xC5, null);'),
    ('minigame replay ticket','return new(0x298, 0xC3, null);','return new(0x298, 0xC3, new ObjectiveAward(false, bit, 0xC3));'),
    ('minigame wrong state latch','Award(false, bit, 0xC5, result);','Award(true, bit, 0xC5, result);'),
    ('minigame award message','Award(false, bit, 0xC5, result);','Award(false, bit, 0xC3, result);'),
    ('minigame result text','return new(0x234, 0xC5, result[0]','return new(0x298, 0xC5, result[0]'),
    ('minigame result message','return new(0x234, 0xC5, result[0]','return new(0x234, 0xC3, result[0]'),
    ('minigame wrong list route','with { AddToMessageList = false }','with { AddToMessageList = true }'),
    ('weekly list lost','bool AddToMessageList { get; init; } = true;','bool AddToMessageList { get; init; } = false;'),
    ('minigame definition gate','if (restrictedMode || Has(false, bit))','if (Definition == null || restrictedMode || Has(false, bit))'),
]:g.add(name,old,new)
g.add('advertised total offset','AdvertisedGoldTickets => bytes[0x31]','AdvertisedGoldTickets => bytes[0x32]','ParkObjectiveDefinition.cs')

def main():
    assert subprocess.check_output(['git','branch','--show-current'],cwd=g.ROOT,text=True).strip()=='objbytes'
    g.WORK.mkdir(parents=True,exist_ok=True)
    for name,data in g.ORIGINAL.items():(g.WORK/(name+'.backup')).write_bytes(data)
    test_dll=g.ROOT/'tests/TPW.Sim.Tests/bin/Debug/net8.0/TPW.Sim.Tests.dll'
    audit=dict(sources={n:g.sha(v) for n,v in g.ORIGINAL.items()},
        tests_sha256=g.sha((g.ROOT/'tests/TPW.Sim.Tests/ParkObjectivesTests.cs').read_bytes()),
        binary_audit_sha256=g.sha((g.ROOT/'findings/objbytes-audit.json').read_bytes()),
        planned=len(g.M),mutations=[],restored=False)
    def save():g.OUT.write_text(json.dumps(audit,indent=2)+'\n')
    def interrupted(signum,frame):raise KeyboardInterrupt(f'signal {signum}')
    for sig in (signal.SIGINT,signal.SIGTERM):signal.signal(sig,interrupted)
    try:
        audit['baseline']=g.run(True)
        assert audit['baseline']['status']=='survived' and audit['baseline']['counters']['notExecuted']==0
        audit['test_assembly_sha256']=g.sha(test_dll.read_bytes())
        audit['target_baseline']=g.run()
        expected=audit['target_baseline']['counters']['executed']
        assert expected>0 and audit['target_baseline']['status']=='survived'
        for i,m in enumerate(g.M,1):
            file=m['file'];g.FILES[file].write_text(g.ORIGINAL[file].decode().replace(m['old'],m['new'],1))
            result=g.run();g.FILES[file].write_bytes(g.ORIGINAL[file])
            result['frozen_tests']=g.sha(test_dll.read_bytes())==audit['test_assembly_sha256']
            c=result.get('counters',{})
            if c.get('executed')!=expected or c.get('notExecuted')!=0 or not result['frozen_tests']:
                result['status']='invalid'
            audit['mutations'].append(dict(**m,**result));save()
            print(f"{i}/{len(g.M)} {result['status']}: {m['name']}",flush=True)
    finally:
        for name,p in g.FILES.items():p.write_bytes(g.ORIGINAL[name])
        audit['restored']=all(p.read_bytes()==g.ORIGINAL[n] for n,p in g.FILES.items())
        audit['summary']={k:sum(m['status']==k for m in audit['mutations']) for k in ('killed','survived','invalid')}
        audit['summary']['total']=len(audit['mutations']);save()
    audit['final_full_suite']=g.run(True);save();print(json.dumps(audit['summary']))
    assert len(audit['mutations'])==len(g.M)>0 and audit['summary']['killed']==len(g.M)
    assert audit['final_full_suite']['status']=='survived' and audit['final_full_suite']['counters']['notExecuted']==0

if __name__=='__main__':main()
