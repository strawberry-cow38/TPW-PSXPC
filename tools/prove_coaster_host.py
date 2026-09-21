#!/usr/bin/env python3
"""Run the real placement/route/guest/tick/render path under Xvfb, with rejecting controls.
Always build game/ explicitly. The frozen control removes only train advancement, then
restores and rebuilds game/ in finally. Images/logs stay under game/obj/coaster-proof.
"""
import argparse
import hashlib
import json
import os
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / 'game/obj/coaster-proof'
ROUTE = '--park-track=212,24,24,0:0,0:19,25:19,31:30,31:30,25:26,25'
FIXTURE = ['--no-boot', '--park=203', '--park-open', '--park-guests=25', '--park-log-rides',
           '--park-place=212,24,24,0', '--park-lay=21,19,21,22;21,22,25,22',
           '--park-queue=212,24,24,0:24,22']
SUMMARY = re.compile(r'coaster 212: ([-\w]+), accepted (\d+), pieces (\d+)/32, checks (\w+), trains (\d+)/8, free (\d+), pending (\d+), updates (\d+), dispatches (\d+), position-changes (\d+), completed (\d+), laps (\d+)')
TRAIN = re.compile(r'^      train (\d+): (\w+), segment (.*?), position \(([^)]+)\), distance (-?\d+), speed (-?\d+), laps (-?\d+), position-changes (\d+)$')
DRAWN = re.compile(r'^      drawn-train (\d+): position \(([^)]+)\)$')


def build(label):
    result = subprocess.run(['dotnet', 'build', 'game/', '--nologo'], cwd=ROOT, capture_output=True, text=True)
    (WORK / f'{label}-build.log').write_text(result.stdout + result.stderr)
    assert result.returncode == 0, result.stdout + result.stderr


def run(args, label, track=True):
    shot = WORK / f'{label}.png'
    shot.unlink(missing_ok=True)
    command = ['xvfb-run','-a',args.godot,'--path','game','--rendering-driver','opengl3','--',
               *FIXTURE, *([ROUTE] if track else []), f'--shot={shot}:{args.frames}']
    print(f'RUN {label}: '+ ' '.join(command), flush=True)
    result = subprocess.run(command, cwd=ROOT, env=dict(os.environ, TPW_DATA=args.data),
                            capture_output=True, text=True, timeout=args.timeout)
    log = result.stdout + result.stderr
    path = WORK / f'{label}.log'; path.write_text(log)
    assert result.returncode == 0 and shot.exists(), log[-5000:]
    assert '--park-place 212,24,24,0: placed' in log
    assert '--park-queue 212,24,24,0:24,22: Finished' in log
    # REJECTS a short route silently left in the builder after a refusal. The first invalid
    # click is intentional; all five subsequent requests must succeed individually.
    steps = re.findall(r'\[track-step\].*?: (Refused|Placed|Closed)',log)
    assert steps == (['Refused','Placed','Placed','Placed','Placed','Closed'] if track else []), steps
    reports = []
    current = None
    tick = None
    for line in log.splitlines():
        clock = re.match(r'\[tpw\] coaster-report tick (\d+), frame (\d+)',line)
        if clock: tick = list(map(int,clock.groups()))
        match = SUMMARY.search(line)
        if match:
            values = match.groups()
            current = dict(tick=tick, track=values[0], checks=values[3], **dict(zip(
                ['accepted','pieces','active','free','pending','updates','dispatches','changes','completed','laps'],
                map(int,values[1:3]+values[4:]))), trains={}, drawn={})
            reports.append(current)
        match = TRAIN.match(line)
        if match and current:
            ident,state,segment,position,distance,speed,laps,changes = match.groups()
            current['trains'][ident] = dict(state=state, segment=segment,
                position=list(map(int,position.split(','))), distance=int(distance), speed=int(speed), laps=int(laps), changes=int(changes))
        match = DRAWN.match(line)
        if match and current:
            current['drawn'][match[1]]=list(map(float,match[2].split(',')))
    assert reports, 'No runtime reports'
    served = re.findall(r'guests \d+, \d+ queueing, \d+ riding, (\d+) served',log)
    return dict(command=command, log=str(path.relative_to(ROOT)), log_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                screenshot=str(shot.relative_to(ROOT)), reports=reports, served=int(served[-1]) if served else 0)


def moving_evidence(run):
    # REJECTS counters/dispatch alone, a different train on each sample, and simulation motion
    # that never reaches a visible scene node. Actual drawn world coordinates must match.
    for a,b in zip(run['reports'],run['reports'][1:]):
        if a['dispatches'] != b['dispatches']: continue
        for ident in a['trains'].keys() & b['trains'].keys():
            pa,pb=a['trains'][ident]['position'],b['trains'][ident]['position']
            if pa != pb and a['drawn'].get(ident)==pa and b['drawn'].get(ident)==pb:
                return dict(train=ident, first=a, second=b)
    raise AssertionError('No same-train position change reaches the scene between two reports')


def compact(run):
    reports=run.pop('reports')
    run['last']=reports[-1]
    return run


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--godot',default=os.environ.get('GODOT','godot'))
    p.add_argument('--data',default=os.environ.get('TPW_DATA','/home/ec2-user/tpw/tpw_psx.iso'))
    p.add_argument('--frames',type=int,default=1600)
    p.add_argument('--timeout',type=int,default=900)
    args=p.parse_args(); WORK.mkdir(parents=True,exist_ok=True)
    build('baseline')
    moving=run(args,'moving'); evidence=moving_evidence(moving)
    assert moving['reports'][-1]['completed']>0 and moving['reports'][-1]['laps']>0 and moving['served']>0
    no_track=run(args,'no-track',False)
    assert all(r['track']=='no-track' and r['dispatches']==r['changes']==r['completed']==0 and not r['trains'] for r in no_track['reports'])
    source=ROOT/'core/TPW.Sim/CoasterSimulation.cs'; original=source.read_bytes()
    try:
        old='train.Advance(Track, host, Elapsed);'
        assert original.decode().count(old)==1
        source.write_text(original.decode().replace(old,'; // Mutation: dispatched trains never advance.'))
        build('frozen')
        frozen=run(args,'frozen')
        assert any(r['active']>0 for r in frozen['reports']), 'Frozen control never dispatched'
        assert all(r['track']=='connected' and r['changes']==r['completed']==0 for r in frozen['reports'])
        try: moving_evidence(frozen)
        except AssertionError: rejected=True
        else: rejected=False
        assert rejected, 'Movement check accepted the stationary control'
    finally:
        source.write_bytes(original)
        build('restored')
    audit=dict(evidence=evidence,moving=compact(moving),no_track=compact(no_track),frozen=compact(frozen),
               frozen_rejected=rejected, restored_sha256=hashlib.sha256(source.read_bytes()).hexdigest())
    (ROOT/'findings/coaster-host-run.json').write_text(json.dumps(audit,indent=2)+'\n')
    print('PASS: accepted route, changing sim and drawn positions, completed/unloaded guests; no-track and dispatched-but-frozen controls rejected.',flush=True)
    print(json.dumps(evidence,indent=2))

if __name__=='__main__': main()
