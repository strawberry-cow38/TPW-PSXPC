#!/usr/bin/env python3
"""Real TrackRun/queue/host/render proof with rejected missing-track and frozen controls.
Tours have no TrackRun: their positive run itself verifies that distinction. Frozen controls
keep boarding and dispatch, and sever only movement. Every changed build targets game/.
"""
import argparse
import hashlib
import json
import os
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / 'game/obj/moving-rides-proof'
AUDIT = ROOT / 'findings/moving-rides-run.json'
ROUTE = '--park-track=215,24,24,0:0,0:18,25:18,29:22,29:22,25'
FIXTURES = {
 'track': ['--park-place=215,24,24,0', '--park-lay=21,19,21,22;21,22,26,22', '--park-queue=215,24,24,0:25,22'],
 'tour': ['--park-place=208,24,24,0', '--park-lay=21,19,21,25;21,25,22,25', '--park-queue=208,24,24,0:22,25'],
}
SUMMARY = re.compile(r'(track|tour)-ride (\d+): ([-\w]+), vehicles (\d+), riders (\d+), updates (\d+), dispatches (\d+), position-changes (\d+), completed (\d+) \(ready-now (\d+)\), arrivals (\d+), unloaded (\d+), transitions (\d+)')
VEHICLE = re.compile(r'^      vehicle (\d+): (\w+), passengers (\d+), position \(([^)]+)\), laps (-?\d+), position-changes (\d+)$')
DRAWN = re.compile(r'^      drawn-vehicle (\d+): position \(([^)]+)\), model (\d+), surfaces (\d+)$')


def build(label):
    result = subprocess.run(['dotnet','build','game/','--nologo'],cwd=ROOT,capture_output=True,text=True,timeout=180)
    (WORK/f'{label}-build.log').write_text(result.stdout+result.stderr)
    assert result.returncode == 0, result.stdout+result.stderr


def run(args, kind, label, track=True, control=False):
    shot = WORK/f'{label}.png'; shot.unlink(missing_ok=True)
    # Capture policy, not a trip timer: deterministic 10 render frames/second,
    # with the park retaining its own 25 Hz clock. CPU load cannot change tick coverage.
    frames = args.control_frames if control else args.frames
    command = ['xvfb-run','-a',args.godot,'--path',str(ROOT/'game'),'--rendering-driver','opengl3','--resolution','800x600','--fixed-fps','10','--',
               '--no-boot','--park=203','--park-open','--park-guests=40','--park-log-rides',
               *FIXTURES[kind], *([ROUTE] if kind=='track' and track else []),
               '--park-select=24,24','--park-slider=duration,1','--park-deselect',f'--shot={shot}:{frames}']
    print('RUN '+label+': '+' '.join(command),flush=True)
    result = subprocess.run(command,cwd=ROOT,env=dict(os.environ,TPW_DATA=args.data),capture_output=True,text=True,timeout=args.timeout)
    log=result.stdout+result.stderr; path=WORK/f'{label}.log'; path.write_text(log)
    assert result.returncode==0 and shot.exists(),f'exit={result.returncode}, screenshot={shot.exists()}\n'+log[-5000:]
    assert 'Exception:' not in log and 'SCRIPT ERROR:' not in log, 'Runtime exception in '+str(path)
    entry=215 if kind=='track' else 208
    assert f'--park-place {entry},24,24,0: placed' in log
    assert f'--park-queue {entry},24,24,0:' in log and ': Finished' in log
    assert 'slider duration := 1 ->' in log
    assert '[panel] deselected after scripted edits' in log
    steps=re.findall(r'\[track-step\].*?: (Refused|Placed|Closed)',log)
    assert steps==(['Refused','Placed','Placed','Placed','Closed'] if kind=='track' and track else []),steps
    reports=[]; current=None; tick=None
    for line in log.splitlines():
        clock=re.match(r'\[tpw\] moving-ride-report tick (\d+), frame (\d+)',line)
        if clock: tick=list(map(int,clock.groups()))
        match=SUMMARY.search(line)
        if match:
            v=match.groups(); current=dict(kind=v[0],entry=int(v[1]),route=v[2],tick=tick,
                **dict(zip(['active','riders','updates','dispatches','changes','completed','ready_now','arrivals','unloaded','transitions'],map(int,v[3:]))),vehicles={},drawn={})
            reports.append(current)
        match=VEHICLE.match(line)
        if match and current is not None:
            ident,state,passengers,position,laps,changes=match.groups()
            current['vehicles'][ident]=dict(state=state,passengers=int(passengers),position=list(map(int,position.split(','))),laps=int(laps),changes=int(changes))
        match=DRAWN.match(line)
        if match and current is not None:
            ident,position,model,surfaces=match.groups()
            current['drawn'][ident]=dict(position=list(map(float,position.split(','))),model=int(model),surfaces=int(surfaces))
    assert reports,'No runtime reports'
    served=re.findall(r'guests \d+, \d+ queueing, \d+ riding, (\d+) served',log)
    return dict(command=command,log=str(path.relative_to(ROOT)),log_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                screenshot=str(shot.relative_to(ROOT)),screenshot_sha256=hashlib.sha256(shot.read_bytes()).hexdigest(),reports=reports,served=int(served[-1]) if served else 0)


def moving_evidence(run):
    # REJECTS counters alone, replacement identities, empty trips, and a simulation whose real
    # scene node has stopped moving or has no mesh. Both sampled drawn poses must match the sim.
    for a,b in zip(run['reports'],run['reports'][1:]):
        if a['dispatches']!=b['dispatches'] or a['tick']==b['tick']: continue
        for ident in a['vehicles'].keys() & b['vehicles'].keys():
            ca,cb=a['vehicles'][ident],b['vehicles'][ident]
            pa,pb=ca['position'],cb['position']; da,db=a['drawn'].get(ident,{}),b['drawn'].get(ident,{})
            expected_model=5 if a['kind']=='track' else 1
            if (ca['passengers']>0 and cb['passengers']>0 and pa!=pb
                and da.get('position')==pa and db.get('position')==pb
                and da.get('surfaces',0)>0 and db.get('surfaces',0)>0
                and da.get('model')==db.get('model')==expected_model):
                return dict(vehicle=ident,first=a,second=b)
    raise AssertionError('No loaded same-vehicle movement reaches a nonempty scene mesh between reports')


def rejected(run):
    try: moving_evidence(run)
    except AssertionError as error: return dict(rejected=True,reason=str(error))
    raise AssertionError('Movement oracle accepted negative control')


def compact(run):
    result={k:v for k,v in run.items() if k!='reports'}
    result.update(samples=len(run['reports']),last=run['reports'][-1])
    return result


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--godot',default=os.environ.get('GODOT','godot'))
    p.add_argument('--data',default=os.environ.get('TPW_DATA','/home/ec2-user/tpw/tpw_psx.iso'))
    p.add_argument('--frames',type=int,default=1200)
    p.add_argument('--control-frames',type=int,default=400)
    p.add_argument('--timeout',type=int,default=1200)
    args=p.parse_args(); WORK.mkdir(parents=True,exist_ok=True)
    inputs = ['game/ParkMovingRideWorld.cs','game/ParkTrackRideWorld.cs','game/ParkTourRideWorld.cs',
              'game/ParkMovingRides.cs','game/ParkView.cs','game/Main.cs','core/TPW.Data/Attraction.cs',
              'core/TPW.Sim/PathedRide.cs','core/TPW.Sim/TourRide.cs','tools/prove_moving_rides.py']
    audit=dict(positive={},controls={},inputs_sha256={path:hashlib.sha256((ROOT/path).read_bytes()).hexdigest() for path in inputs})
    mutations={
        'game/ParkTrackRideWorld.cs':('foreach (var c in cars) Advance(c);','foreach (var c in cars) { }'),
        'game/ParkTourRideWorld.cs':('public bool MoveToDestination() => host.Advance(car);','public bool MoveToDestination() => false;'),
    }
    originals={path:(ROOT/path).read_bytes() for path in mutations}
    audit['source_sha256']={path:hashlib.sha256(data).hexdigest() for path,data in originals.items()}
    try:
        build('baseline')
        for kind in FIXTURES:
            live=run(args,kind,kind+'-moving'); evidence=moving_evidence(live)
            last=live['reports'][-1]
            assert last['completed']>0 and last['arrivals']>0 and last['unloaded']>0 and live['served']>0,last
            audit['positive'][kind]=dict(evidence=evidence,**compact(live))
            AUDIT.write_text(json.dumps(audit,indent=2)+'\n')
        missing=run(args,'track','track-no-track',track=False,control=True)
        assert all(r['route']=='no-track' and r['dispatches']==r['changes']==r['completed']==0 and not r['vehicles'] for r in missing['reports'])
        audit['controls']['track-no-track']=dict(**rejected(missing),**compact(missing))
        for path,(old,new) in mutations.items():
            original=originals[path].decode(); assert original.count(old)==1
            (ROOT/path).write_text(original.replace(old,new))
        build('frozen')
        for kind in FIXTURES:
            frozen=run(args,kind,kind+'-frozen',control=True)
            assert any(r['dispatches']>0 and r['riders']>0 for r in frozen['reports']),'Frozen control never dispatched passengers'
            assert all(r['route']==('connected' if kind=='track' else 'air-route') and r['changes']==r['completed']==0 for r in frozen['reports'])
            audit['controls'][kind+'-frozen']=dict(**rejected(frozen),**compact(frozen))
            AUDIT.write_text(json.dumps(audit,indent=2)+'\n')
    finally:
        for path,data in originals.items(): (ROOT/path).write_bytes(data)
        build('restored')
        audit['restored_sha256']={path:hashlib.sha256((ROOT/path).read_bytes()).hexdigest() for path in originals}
        AUDIT.write_text(json.dumps(audit,indent=2)+'\n')
    assert audit['source_sha256']==audit['restored_sha256']
    assert len(audit['positive'])==2 and len(audit['controls'])==3
    print('PASS: 2 moving hosts, 3 rejected controls, restored game/ build.',flush=True)
    for kind,result in audit['positive'].items():
        e=result['evidence']; ident=e['vehicle']
        print(kind,ident,e['first']['vehicles'][ident]['position'],'->',e['second']['vehicles'][ident]['position'])

if __name__=='__main__': main()
