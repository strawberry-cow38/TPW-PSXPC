#!/usr/bin/env python3
"""Mutate the production builder/park host; an executed failing test is the only kill.
Restores every source in finally and retests the restored baseline. No Godot mock host.
The engine-free game/ParkCoasterWorld.cs is linked verbatim into TPW.Sim.Tests.
"""
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / 'tests/TPW.Sim.Tests/TestResults/coaster-host-mutations'
AUDIT = ROOT / 'findings/coaster-host-mutations.json'
CASES = [
    ('coaster start uses track-ride station', 'core/TPW.Data/TrackBuilder.cs',
     'Start = ride.Coaster?.Connection(ride, ox, oz, rot, launch: true) ?? StartFor(ox, oz, rot);', 'Start = StartFor(ox, oz, rot);'),
    ('approach collapsed into launch', 'core/TPW.Data/TrackBuilder.cs',
     'Finish = ride.Coaster?.Connection(ride, ox, oz, rot, launch: false) ?? Start;', 'Finish = Start;'),
    ('coaster incorrectly projects onto two-tile grid', 'core/TPW.Data/TrackBuilder.cs',
     'Ride.Coaster != null ? (x, z) : Project(End, (x, z))', 'Project(End, (x, z))'),
    ('terrain replaced by opaque padding', 'game/ParkCoasterWorld.cs',
     'BaseHeight(piece.TileX, piece.TileY), supportHeight', '(short)piece.UnknownPositionPadding, supportHeight'),
    ('model height omitted', 'game/ParkCoasterWorld.cs',
     'supportHeight = bounds[pieces.Straight].H;', 'supportHeight = 0;'),
    ('spline correction omitted', 'game/ParkCoasterWorld.cs',
     'new(0, 24 - supportHeight, 0)', 'new(0, 0, 0)'),
    ('ignored accepted route', 'game/ParkCoasterWorld.cs',
     'i < accepted.Length; i++', 'i < 0; i++'),
    ('ejection omitted on route rebuild', 'game/ParkCoasterWorld.cs',
     'Simulation?.EjectPassengers();', ';'),
    ('host never ticks simulation', 'game/ParkCoasterWorld.cs',
     'Simulation.Update();', ';'),
    ('stale movement delta', 'game/ParkCoasterWorld.cs',
     'MovementDelta = frameTime;', 'MovementDelta = 0;'),
    ('batch uses eight train objects', 'game/ParkCoasterWorld.cs',
     'BoardingBatchSize = Math.Max(1, models[CarSub].SeatCount);', 'BoardingBatchSize = CoasterSimulation.TrainCount;'),
    ('open without a connection', 'game/ParkCoasterWorld.cs',
     'RollerCoaster.OpenToGuests(Status, Simulation.Track.Connected)', 'AttractionLifecycle.OpenToGuests(Status)'),
    ('connected and missing-track reports indistinguishable', 'game/ParkCoasterWorld.cs',
     'accepted.Length == 0 ? "no-track"', 'accepted.Length == 0 ? "connected"'),
    ('launch speed reads wrong field', 'core/TPW.Data/CoasterDefinition.cs',
     'ReadInt16LittleEndian(record[0xC8..])', 'ReadInt16LittleEndian(record[0xCA..])'),
    ('park train movement severed after dispatch', 'core/TPW.Sim/CoasterSimulation.cs',
     'train.Advance(Track, host, Elapsed);', ';'),
    ('completed laps not retained after unload', 'core/TPW.Sim/CoasterSimulation.cs',
     'CompletedLaps += train.Laps;', 'CompletedLaps += 0;'),
]


def test(label):
    folder = WORK / label
    folder.mkdir(parents=True, exist_ok=True)
    trx = folder / 'result.trx'
    trx.unlink(missing_ok=True)
    command = ['dotnet', 'test', 'tests/TPW.Sim.Tests/', '--no-restore', '--nologo',
               '--filter', 'FullyQualifiedName~ParkCoasterTests', '--logger', 'trx;LogFileName=result.trx',
               '--results-directory', str(folder)]
    result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True)
    (folder / 'run.log').write_text(result.stdout + result.stderr)
    tests = []
    if trx.exists():
        doc = ET.parse(trx)
        tests = [dict(name=t.attrib['testName'], outcome=t.attrib['outcome'])
                 for t in doc.findall('.//{*}UnitTestResult')]
    failed = [t['name'] for t in tests if t['outcome'] == 'Failed']
    return dict(returncode=result.returncode, executed=len(tests), failed=failed,
                trx=str(trx.relative_to(ROOT)))


def main():
    sources = {p: (ROOT / p).read_bytes() for _, p, _, _ in CASES}
    audit = dict(source_sha256={p: hashlib.sha256(b).hexdigest() for p,b in sources.items()},
                 test_sha256=hashlib.sha256((ROOT/'tests/TPW.Sim.Tests/ParkCoasterTests.cs').read_bytes()).hexdigest(), mutations=[])
    try:
        audit['baseline'] = test('baseline')
        assert audit['baseline']['returncode'] == 0 and audit['baseline']['executed'] == 12
        for i,(name,path,old,new) in enumerate(CASES):
            original = sources[path].decode()
            assert original.count(old) == 1, name
            try:
                (ROOT/path).write_text(original.replace(old,new))
                result = test(f'{i:02}')
            finally:
                (ROOT/path).write_bytes(sources[path])
            outcome = 'killed' if result['returncode'] != 0 and result['executed'] == 12 and result['failed'] else 'survived' if result['returncode'] == 0 else 'invalid'
            audit['mutations'].append(dict(name=name,path=path,old=old,new=new,outcome=outcome,**result))
            AUDIT.write_text(json.dumps(audit,indent=2)+'\n')
            print(f'{i+1}/{len(CASES)} {outcome}: {name}',flush=True)
    finally:
        for p,b in sources.items(): (ROOT/p).write_bytes(b)
        audit['restored_baseline'] = test('restored')
        AUDIT.write_text(json.dumps(audit,indent=2)+'\n')
    assert all(m['outcome']=='killed' for m in audit['mutations']) and len(audit['mutations']) == len(CASES)
    assert audit['restored_baseline']['returncode']==0

if __name__ == '__main__': main()
