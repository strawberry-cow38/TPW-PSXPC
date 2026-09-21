#!/usr/bin/env python3
"""Mutate production track/tour hosts and controllers. Only an executed failed test kills.
Sources are restored in finally; the JSON includes hashes, failed test names and TRX paths.
"""
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / 'tests/TPW.Sim.Tests/TestResults/moving-rides-mutations'
AUDIT = ROOT / 'findings/moving-rides-mutations.json'
TRACK = 'game/ParkTrackRideWorld.cs'
TOUR = 'game/ParkTourRideWorld.cs'
COMMON = 'game/ParkMovingRideWorld.cs'
CASES = [
 ('track ignores accepted points', TRACK, 'run?.Pylons.ToArray()', 'null'),
 ('track ignores closure', TRACK, 'connected = run?.Circuit ?? false;', 'connected = false;'),
 ('track does not eject on edit', TRACK, 'EjectPassengers(); Controller.RunTicks = 0;', 'Controller.RunTicks = 0;'),
 ('track route has half length', TRACK, '* 256).ToArray()', '* 128).ToArray()'),
 ('track uses ride capacity per vehicle', TRACK, '>= ride.TrackPassengersPerVehicle', '>= Capacity'),
 ('track does not move', TRACK, 'foreach (var c in cars) Advance(c);', 'foreach (var c in cars) { }'),
 ('track freezes at unload gate', TRACK, 'Status != AttractionStatus.Loading', 'Status == AttractionStatus.Running'),
 ('track never completes a lap', TRACK, 'c.Track.CompleteLap(Duration);', ';'),
 ('track ignores live duration', TRACK, 'c.Track.CompleteLap(Duration);', 'c.Track.CompleteLap(1);'),
 ('track loses completion history', TRACK, 'if (c.Track.ReadyToUnload) Completed++;', 'if (c.Track.ReadyToUnload) Completed = 0;'),
 ('track samples speed each tick', TRACK, 'c.Progress += c.Speed;', 'c.Progress += RideSliderEffects.TrackVehicleSpeed(LiveSpeed());'),
 ('track speed field disconnected', TRACK, 'RideSliderEffects.TrackVehicleSpeed(LiveSpeed())', '0'),
 ('track loads at ten ticks', 'core/TPW.Sim/PathedRide.cs', 'LoadCadence = 20', 'LoadCadence = 10'),
 ('track starts partially loaded', 'core/TPW.Sim/PathedRide.cs', 'world.Riders < world.Capacity', 'world.Riders < 1'),
 ('track gate is duration not twice duration', 'core/TPW.Sim/PathedRide.cs', 'RunTicksPerDuration = 2', 'RunTicksPerDuration = 1'),
 ('track removes compaction skip', 'core/TPW.Sim/PathedRide.cs', 'world.UnloadVehicle(i);', 'world.UnloadVehicle(i--);'),
 ('tour does not tick transports', TOUR, 'c.Tour.Tick(new TransportWorld(this, c));', ';'),
 ('tour status never follows dock', TOUR, 'SetStatus(TourRide.StatusAfterMovement(Status, docked?.Tour.State));', ';'),
 ('tour never loads', TOUR, 'TourRide.LoadTick(this);', ';'),
 ('tour never unloads', TOUR, 'TourRide.UnloadTick(this);', ';'),
 ('tour fourth transport allowed', TOUR, 'MaximumTransports = 3', 'MaximumTransports = 4'),
 ('tour retains dock on departure', TOUR, 'if (ReferenceEquals(docked, c)) docked = null;', ';'),
 ('tour does not claim returning dock', TOUR, 'docked = c; c.Destination', 'c.Destination'),
 ('tour arrival never reported', TOUR, 'return arrived;', 'return false;'),
 ('tour loses completion history', TOUR, 'Completed++; break;', 'Completed = 0; break;'),
 ('tour retirement orphans passengers', TOUR, 'while (car.Passengers.Count != 0) host.ExitLast(car);', ';'),
 ('tour becomes a whole-ride-capacity machine', 'core/TPW.Sim/TourRide.cs', 'TransportSeats = 3', 'TransportSeats = 8'),
 ('tour unload loses cadence', 'core/TPW.Sim/TourRide.cs', 'if (world.NowTick % GuestCadence == 0) world.UnloadLastGuest();', 'world.UnloadLastGuest();'),
 ('tour silently adopts disputed empty-vehicle gate', 'core/TPW.Sim/TourRide.cs', 'if (world.QueueHead != null) return;', 'if (world.QueueHead != null || world.DockedPassengers != 0) return;'),
 ('host drops board transaction', COMMON, 'Board(guest); c.Passengers.Add(guest);', 'c.Passengers.Add(guest);'),
 ('host drops exit transaction', COMMON, 'Exit(guest); c.Passengers.RemoveAt', 'c.Passengers.RemoveAt'),
 ('host hides missing track', TRACK, 'accepted.Length == 0 ? "no-track"', 'accepted.Length == 0 ? "connected"'),
 ('host reports occupancy as completion', COMMON, 'completed {Completed}', 'completed {ready}'),
 ('host reuses vehicle identity after ejection', COMMON, 'cars.Clear();', 'cars.Clear(); nextId = 0;'),
 ('host clears vehicles but leaves tour dock', TOUR, 'base.EjectPassengers(transfer); docked = null;', 'base.EjectPassengers(transfer);'),
 ('track reads wrong passenger field', 'core/TPW.Data/Attraction.cs', 'a.TrackPassengersPerVehicle = BitConverter.ToInt32(d, r + 0xD0)', 'a.TrackPassengersPerVehicle = BitConverter.ToInt32(d, r + 0xD4)'),
 ('track car selector points at rails', 'core/TPW.Data/Attraction.cs', 'a.TrackCarSub = BitConverter.ToInt32(d, r + 0xEC)', 'a.TrackCarSub = BitConverter.ToInt32(d, r + 0xDC)'),
 ('tour reads high speed byte', 'core/TPW.Data/Attraction.cs', 'a.TourBaseSpeed = d[r + 0xD0]', 'a.TourBaseSpeed = d[r + 0xD1]'),
]


def test(label):
    folder = WORK / label
    folder.mkdir(parents=True, exist_ok=True)
    trx = folder / 'result.trx'; trx.unlink(missing_ok=True)
    command = ['dotnet','test','tests/TPW.Sim.Tests/','--no-restore','--nologo',
               '--filter','FullyQualifiedName~ParkMovingRideTests','--logger','trx;LogFileName=result.trx',
               '--results-directory',str(folder)]
    result = subprocess.run(command,cwd=ROOT,capture_output=True,text=True,timeout=180)
    (folder/'run.log').write_text(result.stdout+result.stderr)
    tests = [] if not trx.exists() else ET.parse(trx).findall('.//{*}UnitTestResult')
    return dict(returncode=result.returncode,executed=len(tests),
                failed=[t.attrib['testName'] for t in tests if t.attrib['outcome']=='Failed'],
                trx=str(trx.relative_to(ROOT)))


def main():
    sources = {p:(ROOT/p).read_bytes() for _,p,_,_ in CASES}
    audit = dict(source_sha256={p:hashlib.sha256(b).hexdigest() for p,b in sources.items()},
                 test_sha256=hashlib.sha256((ROOT/'tests/TPW.Sim.Tests/ParkMovingRideTests.cs').read_bytes()).hexdigest(),mutations=[])
    try:
        audit['baseline']=test('baseline'); count=audit['baseline']['executed']
        assert audit['baseline']['returncode']==0 and count>0, audit['baseline']
        for i,(name,path,old,new) in enumerate(CASES):
            original=sources[path].decode(); assert original.count(old)==1,(name,original.count(old))
            try:
                (ROOT/path).write_text(original.replace(old,new)); result=test(f'{i:02}')
            finally:
                (ROOT/path).write_bytes(sources[path])
            outcome='killed' if result['returncode']!=0 and result['executed']==count and result['failed'] else 'survived' if result['returncode']==0 else 'invalid'
            audit['mutations'].append(dict(name=name,path=path,old=old,new=new,outcome=outcome,**result))
            AUDIT.write_text(json.dumps(audit,indent=2)+'\n')
            print(f'{i+1}/{len(CASES)} {outcome}: {name}',flush=True)
    finally:
        for p,b in sources.items(): (ROOT/p).write_bytes(b)
        audit['restored_baseline']=test('restored')
        audit['restored_sha256']={p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in sources}
        AUDIT.write_text(json.dumps(audit,indent=2)+'\n')
    assert len(audit['mutations'])==len(CASES) and all(m['outcome']=='killed' for m in audit['mutations'])
    assert audit['restored_baseline']['returncode']==0 and audit['source_sha256']==audit['restored_sha256']

if __name__=='__main__': main()
