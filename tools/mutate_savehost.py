#!/usr/bin/env python3
"""Mutation audit of the live game adapter. Compile errors are invalid, never kills.

Every mutant runs the two-process file proof against the unchanged live-field assertions.
Backups are durable under game/obj; signals restore sources. --recover handles a killed runner.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys

from prove_savehost import ROOT, WORK, prove

FILES = {n: ROOT / 'game' / n for n in ('ParkSaveHost.cs', 'ParkGuests.cs', 'Main.cs')}
BACKUP = WORK / 'mutation-backup'
AUDIT = ROOT / 'findings/savehost-mutations.json'
MUTATIONS = [
    ('capture x', 'X = checked((byte)a.Ox)', 'X = checked((byte)(a.Ox + 1))'),
    ('capture rotation', 'Rotation = checked((byte)a.Rot)', 'Rotation = 0'),
    ('capture level', 's.Level = checked((byte)a.Level)', 's.Level = 0'),
    ('capture raw bank', 'BalanceRaw = checked((int)Finances.Bank.Balance.Raw)', 'BalanceRaw = checked((int)Finances.Bank.Balance.Pounds)'),
    ('capture calendar day', 'Day = checked((byte)cal.Day)', 'Day = 0'),
    ('capture open', 'Open = park.ParkOpen ? (byte)1 : (byte)0', 'Open = 0'),
    ('capture fee', 'EntryFeePounds = checked((ushort)Guests.SaveEntrance.EntryFee.Pounds)', 'EntryFeePounds = 40'),
    ('capture guests', 'VisitorCount = checked((byte)Guests.Count)', 'VisitorCount = 0'),
    ('capture staff position', 'X = checked((short)st.X)', 'X = 0'),
    ('capture hire day', 'HireDay = st.HiredDay', 'HireDay = 0'),
    ('staff order', 'StaffKind.Guard, StaffKind.Researcher, StaffKind.Mechanic', 'StaffKind.Mechanic, StaffKind.Researcher, StaffKind.Guard'),
    ('restore fee empty', 'public void SetEntryFee(Money fee) => Guests.SaveEntrance.EntryFee = fee;', 'public void SetEntryFee(Money fee) { }'),
    ('restore level empty', 'a.Level = s.Level;', 'a.Level = 0;'),
    ('restore staff empty', 'var st = Guests.Hire(StaffOrder[block], saved.X >> 8, saved.Y >> 8);', 'if (saved.HireDay >= 0) return; var st = Guests.Hire(StaffOrder[block], saved.X >> 8, saved.Y >> 8);'),
    ('restore bank empty', 'Finances.Bank.Receive(Money.FromRaw(saved.BalanceRaw) - Finances.Bank.Balance);', '/* dropped bank restore */'),
    ('restore admissions empty', 'Guests.SaveEntrance.Counter_McAi1C = unchecked((int)saved.Admissions);', '/* dropped admission restore */'),
    ('restore paths empty', 'public void PlacePath(int x, int y) => park.RestorePath(x, y);', 'public void PlacePath(int x, int y) { }'),
    ('restore queue empty', 'int count = s.Bytes[0x91];', 'int count = 0;'),
    ('restore sliders empty', 'TPW.Sim.RidePanel.RestoreSliders(a, s.Sliders);', '/* dropped sliders */'),
    ('restore feature stock empty', 'FeatureStock.FromSave(unchecked((sbyte)s.Bytes[14]), ParkSaveHost.Get32(s.Bytes, 8))', 'new FeatureStock()'),
    ('restore shop price', 'a.SalePrice = unchecked((ushort)ParkSaveHost.Get16(s.Bytes, 12));', 'a.SalePrice = 0;'),
    ('restore unknown visitor byte empty', 'guest.Unknown63 = value;', '/* dropped visitor byte */'),
    ('repaid loan slot released', 'loan.Taken ? (byte)0 : (byte)1', 'loan.Remaining.Raw > 0 ? (byte)0 : (byte)1'),
    ('staff cleanup empty', '_staff.Clear();', '/* retained old staff */', 'ParkGuests.cs'),
    ('save before construction', 'if (_autoPlace != null)', 'if (_parkSavePath != null) SaveParkFile();\n                    if (_autoPlace != null)', 'Main.cs'),
]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def build():
    return subprocess.run(['dotnet', 'build', 'game/', '--nologo', '--no-restore'], cwd=ROOT,
                          capture_output=True, text=True, timeout=90)


def write(report):
    temporary = AUDIT.with_suffix('.tmp')
    temporary.write_text(json.dumps(report, indent=2) + '\n')
    temporary.replace(AUDIT)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', default=os.environ.get('GODOT', 'godot'))
    parser.add_argument('--data', default=os.environ.get('TPW_DATA'))
    parser.add_argument('--recover', action='store_true')
    args = parser.parse_args()
    if args.recover:
        for name, path in FILES.items():
            path.write_bytes((BACKUP / (name + '.backup')).read_bytes())
        for p in BACKUP.iterdir():
            p.unlink()
        BACKUP.rmdir()
        print('Recovered original sources; rebuild game/ before running it.')
        return 0
    if not args.data:
        parser.error('pass --data or set TPW_DATA')
    if BACKUP.exists():
        parser.error('Unfinished mutation backup exists; inspect it and run --recover first.')
    original = {name: path.read_bytes() for name, path in FILES.items()}
    report = {'original_sha256': {n: sha(b) for n, b in original.items()}, 'mutations': [],
              'test_sha256': {str(p.relative_to(ROOT)): sha(p.read_bytes()) for p in
                             (ROOT / 'game/ParkSaveProof.cs', ROOT / 'tools/prove_savehost.py')}}
    BACKUP.mkdir(parents=True)
    for n, data in original.items():
        (BACKUP / (n + '.backup')).write_bytes(data)
    def stop(signum, frame):
        raise KeyboardInterrupt(f'signal {signum}')
    for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
        signal.signal(sig, stop)
    try:
        baseline = build()
        assert baseline.returncode == 0, baseline.stdout + baseline.stderr
        report['baseline'] = prove(args.godot, Path(args.data).resolve())
        write(report)
        for n, mutation in enumerate(MUTATIONS, 1):
            name, old, new, *file = mutation
            file = file[0] if file else 'ParkSaveHost.cs'
            src = original[file].decode()
            assert src.count(old) == 1, (name, src.count(old))
            result = dict(name=name, file=file, old=old, new=new)
            try:
                FILES[file].write_text(src.replace(old, new, 1))
                compilation = build()
                if compilation.returncode:
                    result.update(status='invalid', detail=(compilation.stdout + compilation.stderr)[-3000:])
                else:
                    try:
                        prove(args.godot, Path(args.data).resolve(), controls=False)
                        result['status'] = 'survived'
                    except AssertionError as e:
                        result.update(status='killed', detail=str(e)[-4000:])
                    except Exception as e:
                        result.update(status='invalid', detail=repr(e))
            finally:
                FILES[file].write_bytes(original[file])
            report['mutations'].append(result)
            write(report)
            print(f'{n}/{len(MUTATIONS)} {result["status"]}: {name}', flush=True)
    finally:
        for n, data in original.items():
            FILES[n].write_bytes(data)
        report['restored_sha256'] = {n: sha(p.read_bytes()) for n, p in FILES.items()}
        report['restored'] = report['original_sha256'] == report['restored_sha256']
        report['summary'] = {s: sum(m['status'] == s for m in report['mutations']) for s in ('killed', 'survived', 'invalid')}
        report['summary']['total'] = len(report['mutations'])
        clean = build()
        report['restored_build_exit'] = clean.returncode
        write(report)
        for p in BACKUP.iterdir():
            p.unlink()
        BACKUP.rmdir()
    print(json.dumps(report['summary']), flush=True)
    return int(report['summary']['total'] != len(MUTATIONS) or report['summary']['killed'] != len(MUTATIONS)
               or not report['restored'] or report['restored_build_exit'] != 0)


if __name__ == '__main__':
    sys.exit(main())
