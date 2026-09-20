#!/usr/bin/env python3
"""One compiled behavioral mutant at a time; restore even on ordinary process termination.

SIGKILL/power loss cannot execute finally. The durable backup/journal is recovered on the
next invocation (or --recover), provided the file still matches the recorded mutant.
Run only in the ride-panel worktree, with no concurrent edits of RidePanel.cs.
"""
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'core/TPW.Sim/RidePanel.cs'
RESULT = ROOT / 'findings/ride-panel-mutations.json'
BACKUP = ROOT / '.ride-panel-mutation-original'
JOURNAL = ROOT / '.ride-panel-mutation-journal.json'
PROJECT = ROOT / 'tests/TPW.Sim.Tests'
FILTER = 'FullyQualifiedName~RidePanel|FullyQualifiedName~RideSliderEffects'
MUTANTS = []


def add(name, old, new):
    MUTANTS.append((name, old, new))


add('placement retains old level', 'world.Level = 0;', 'world.Level = 1;')
add('placement does not initialize lifetime', 'world.Lifetime = world.ReadLevel(0).Lifetime;', ';')
add('placement skips reset', '            ResetForLevel(world);\n            world.Lifetime', '            world.Lifetime')
add('default reset keeps old effects', 'world.ClearRideEffects();', ';')
add('default reset keeps reliability', 'world.ReliabilityFixed = RideWear.FullReliability;', ';')
add('default reset keeps cycle count', 'world.CyclesRun = 0;', ';')
add('default reset keeps closing progress', 'world.ClosingProgress = 0;', ';')
add('default reads level zero forever', 'var level = world.ReadLevel(world.Level);', 'var level = world.ReadLevel(0);')
add('capacity default is full maximum', 'world.MaximumSeats >> 1', 'world.MaximumSeats')
add('capacity uses record instead of virtual maximum', 'world.MaximumSeats >> 1', 'level.MaxSeats >> 1')
add('capacity default has no floor', 'Math.Max(MinimumDefault, world.MaximumSeats >> 1)', '(world.MaximumSeats >> 1)')
add('speed hardcoded fifty', 'level.SpeedMin + ((level.SpeedMax - level.SpeedMin) >> 1)', '50')
add('speed loses minimum offset', 'level.SpeedMin + ((level.SpeedMax - level.SpeedMin) >> 1)', '((level.SpeedMax - level.SpeedMin) >> 1)')
add('speed midpoint rounds up', '(level.SpeedMax - level.SpeedMin) >> 1', '(level.SpeedMax - level.SpeedMin + 1) >> 1')
add('duration default is full maximum', 'level.CyclesMax >> 1', 'level.CyclesMax')
add('duration default clamps to level minimum', 'Math.Max(MinimumDefault, level.CyclesMax >> 1)', 'Math.Max(level.CyclesMin, level.CyclesMax >> 1)')
add('duration default loses floor', 'Math.Max(MinimumDefault, level.CyclesMax >> 1)', '(level.CyclesMax >> 1)')
add('default floor two', 'MinimumDefault = 1', 'MinimumDefault = 2')
add('panel ignores third level', 'i < PanelLevelCount', 'i < PanelLevelCount - 1')
add('panel reads current level as initial bound', 'var first = world.ReadLevel(0);', 'var first = world.ReadLevel(world.Level);')
add('speed lower bound uses maximum', 'Math.Min(speedMin, level.SpeedMin)', 'Math.Max(speedMin, level.SpeedMin)')
add('speed upper bound uses minimum', 'Math.Max(speedMax, level.SpeedMax)', 'Math.Min(speedMax, level.SpeedMax)')
add('duration lower bound uses maximum', 'Math.Min(cyclesMin, level.CyclesMin)', 'Math.Max(cyclesMin, level.CyclesMin)')
add('duration upper bound uses minimum', 'Math.Max(cyclesMax, level.CyclesMax)', 'Math.Min(cyclesMax, level.CyclesMax)')
add('capacity bound uses minimum', 'Math.Max(seats, level.MaxSeats)', 'Math.Min(seats, level.MaxSeats)')
add('no coaster virtual capacity override', 'if (world.Type == AttractionType.RollerCoaster) seats = world.MaximumSeats;', ';')
add('coaster override applied to every ride', 'if (world.Type == AttractionType.RollerCoaster) seats = world.MaximumSeats;', 'seats = world.MaximumSeats;')
add('capacity hidden at two', 'seats > 1', 'seats > 2')
add('duration shown on coaster', 'world.Type != AttractionType.RollerCoaster);', 'true);')
add('widget lacks lower clamp', 'Math.Min(Max, Math.Max(Min, value))', 'Math.Min(Max, value)')
add('widget lacks upper clamp', 'Math.Min(Max, Math.Max(Min, value))', 'Math.Max(Min, value)')
add('widget reverses clamp order', 'Math.Min(Max, Math.Max(Min, value))', 'Math.Max(Min, Math.Min(Max, value))')
add('widget uses unsigned getter', 'unchecked((short)Math.Min', 'unchecked((ushort)Math.Min')
add('apply ignores speed', 'world.SpeedSlider = ranges.Speed.Clamp(speed);', ';')
add('apply ignores capacity', 'world.Capacity = ranges.Capacity.Clamp(capacity);', ';')
add('apply ignores duration', 'world.CyclesPerLoad = ranges.Duration.Clamp(duration);', ';')
add('apply clamps capacity to current level', 'ranges.Capacity.Clamp(capacity)', 'Math.Min(world.MaximumSeats, ranges.Capacity.Clamp(capacity))')
add('apply restarts animation', 'world.SpeedSlider = ranges.Speed.Clamp(speed);', 'world.CyclesRun = 0; world.SpeedSlider = ranges.Speed.Clamp(speed);')
add('save speed truncates to byte', 'unchecked((ushort)world.SpeedSlider)', 'unchecked((byte)world.SpeedSlider)')
add('save capacity uses duration', 'unchecked((byte)world.Capacity)', 'unchecked((byte)world.CyclesPerLoad)')
add('save duration uses capacity', 'unchecked((byte)world.CyclesPerLoad)', 'unchecked((byte)world.Capacity)')
add('restore speed sign extends', 'world.SpeedSlider = saved.Speed;', 'world.SpeedSlider = (short)saved.Speed;')
add('restore capacity sign extends', 'world.Capacity = saved.Capacity;', 'world.Capacity = (sbyte)saved.Capacity;')
add('restore duration sign extends', 'world.CyclesPerLoad = saved.Duration;', 'world.CyclesPerLoad = (sbyte)saved.Duration;')
add('restore clamps values', 'world.SpeedSlider = saved.Speed;', 'world.SpeedSlider = Ranges(world).Speed.Clamp(saved.Speed);')
add('panel offers third upgrade', 'world.Level + 1 < PanelLevelCount', 'world.Level + 1 <= PanelLevelCount')
add('offer ignores lifetime', '&& world.Lifetime != 0', '')
add('offer requires positive lifetime', 'world.Lifetime != 0', 'world.Lifetime > 0')
add('offer accepts research equality', 'world.Level + 1 < world.ResearchedLevelCount', 'world.Level + 1 <= world.ResearchedLevelCount')
add('request ignores offer gates', '!CanOfferUpgrade(world) || ', '')
add('request ignores no mechanics', ' || world.MechanicCount == 0', '')
add('request ignores strike', ' || world.MechanicsOnStrike', '')
add('request does not enqueue', 'return world.TryEnqueueUpgrade();', 'return true;')
add('request ignores queue failure', 'return world.TryEnqueueUpgrade();', 'world.TryEnqueueUpgrade(); return true;')
add('request blocks zero reliability', 'if (!CanOfferUpgrade(world)', 'if (world.ReliabilityFixed == 0 || !CanOfferUpgrade(world)')
add('request completes immediately', 'return world.TryEnqueueUpgrade();', 'CompleteUpgrade(world); return world.TryEnqueueUpgrade();')
add('completion stops at two', 'BinaryUpgradeLimit = 3', 'BinaryUpgradeLimit = 2')
add('completion accepts level three', 'if (world.Level >= BinaryUpgradeLimit)', 'if (world.Level > BinaryUpgradeLimit)')
add('completion does not increment', 'world.Level++;', ';')
add('completion increments twice', 'world.Level++;', 'world.Level += 2;')
add('completion keeps lifetime from new block', 'world.Level++;', 'world.Level++; world.Lifetime = world.ReadLevel(world.Level).Lifetime;')
add('completion reapplies panel gates', 'if (world.Level >= BinaryUpgradeLimit)', 'if (!CanOfferUpgrade(world) || world.Level >= BinaryUpgradeLimit)')
add('completion charges before reset', '            ResetForLevel(world);\n            world.TrySpend(Money.FromPounds(world.ReadLevel(world.Level).PricePounds));', '            world.TrySpend(Money.FromPounds(world.ReadLevel(world.Level).PricePounds));\n            ResetForLevel(world);')
add('completion charges previous level', 'world.ReadLevel(world.Level).PricePounds', 'world.ReadLevel(world.Level - 1).PricePounds')
add('completion halves price', 'world.ReadLevel(world.Level).PricePounds', 'world.ReadLevel(world.Level).PricePounds / 2')
add('completion charges price difference', 'world.ReadLevel(world.Level).PricePounds', '(world.ReadLevel(world.Level).PricePounds - world.ReadLevel(world.Level - 1).PricePounds)')
add('completion treats pounds as raw', 'Money.FromPounds(world.ReadLevel(world.Level).PricePounds)', 'Money.FromRaw(world.ReadLevel(world.Level).PricePounds)')
add('completion omits payment', 'world.TrySpend(Money.FromPounds(world.ReadLevel(world.Level).PricePounds));', ';')
add('failed payment blocks success', 'world.TrySpend(Money.FromPounds(world.ReadLevel(world.Level).PricePounds));', 'if (!world.TrySpend(Money.FromPounds(world.ReadLevel(world.Level).PricePounds))) return false;')
add('silent completion has no payment', 'world.TrySpend(Money.FromPounds(world.ReadLevel(world.Level).PricePounds));', 'if (!silent) world.TrySpend(Money.FromPounds(world.ReadLevel(world.Level).PricePounds));')
add('silent completion still shows effect', 'if (!silent) world.ShowUpgradeEffect', 'world.ShowUpgradeEffect')
add('completion omits effect', 'if (!silent) world.ShowUpgradeEffect(world.Level >= BinaryUpgradeLimit);', ';')
add('final effect starts at level two', 'world.ShowUpgradeEffect(world.Level >= BinaryUpgradeLimit)', 'world.ShowUpgradeEffect(world.Level >= 2)')
add('wear multiplier stays at level zero', 'world.ReadLevel(world.Level).WearMultiplier', 'world.ReadLevel(0).WearMultiplier')
add('intensity is cached base', '=> RideSliderEffects.Intensity(world.BaseIntensity, world.SpeedSlider, world.CyclesPerLoad);', '=> world.BaseIntensity;')
add('intensity ignores speed setting', 'Intensity(world.BaseIntensity, world.SpeedSlider, world.CyclesPerLoad)', 'Intensity(world.BaseIntensity, 50, world.CyclesPerLoad)')
add('intensity ignores duration setting', 'Intensity(world.BaseIntensity, world.SpeedSlider, world.CyclesPerLoad)', 'Intensity(world.BaseIntensity, world.SpeedSlider, 5)')
add('candidate wrong id', 'new(id, (int)world.Type, Intensity(world),', 'new(0, (int)world.Type, Intensity(world),')
add('candidate wrong type', 'new(id, (int)world.Type, Intensity(world),', 'new(id, 3, Intensity(world),')
add('candidate enables desire terms', '0, 0, 0, openToGuests, distanceTiles, centreTileValid)', '1, 0, 0, openToGuests, distanceTiles, centreTileValid)')
add('candidate sets need B attribute', '0, 0, 0, openToGuests, distanceTiles, centreTileValid)', '0, 50, 0, openToGuests, distanceTiles, centreTileValid)')
add('candidate sets need A attribute', '0, 0, 0, openToGuests, distanceTiles, centreTileValid)', '0, 0, 50, openToGuests, distanceTiles, centreTileValid)')
add('candidate assumes open', '0, 0, 0, openToGuests, distanceTiles, centreTileValid)', '0, 0, 0, true, distanceTiles, centreTileValid)')
add('candidate discards distance', '0, 0, 0, openToGuests, distanceTiles, centreTileValid)', '0, 0, 0, openToGuests, 0, centreTileValid)')
add('candidate assumes valid centre', '0, 0, 0, openToGuests, distanceTiles, centreTileValid)', '0, 0, 0, openToGuests, distanceTiles, true)')
add('intensity fixed shift wrong', 'FixedShift = 12', 'FixedShift = 11')
add('intensity minimum factor one unit low', 'MinimumFactor = 0xC00', 'MinimumFactor = 0xBFF')
add('intensity maximum factor one unit low', 'MaximumFactor = 0x1400', 'MaximumFactor = 0x13FF')
add('intensity speed divisor ninety nine', 'SpeedDivisor = 100', 'SpeedDivisor = 99')
add('intensity duration divisor one', 'DurationDivisor = 5', 'DurationDivisor = 1')
add('intensity ceiling ninety nine', 'MaximumIntensity = 100', 'MaximumIntensity = 99')
add('intensity loses speed floor', 'unchecked(speed << FixedShift) / SpeedDivisor,\n                MinimumFactor, MaximumFactor', 'unchecked(speed << FixedShift) / SpeedDivisor,\n                0, MaximumFactor')
add('intensity loses duration floor', 'unchecked(duration << FixedShift) / DurationDivisor,\n                MinimumFactor, MaximumFactor', 'unchecked(duration << FixedShift) / DurationDivisor,\n                0, MaximumFactor')
add('intensity loses speed ceiling', 'unchecked(speed << FixedShift) / SpeedDivisor,\n                MinimumFactor, MaximumFactor', 'unchecked(speed << FixedShift) / SpeedDivisor,\n                MinimumFactor, int.MaxValue')
add('intensity loses duration ceiling', 'unchecked(duration << FixedShift) / DurationDivisor,\n                MinimumFactor, MaximumFactor', 'unchecked(duration << FixedShift) / DurationDivisor,\n                MinimumFactor, int.MaxValue')
add('intensity factors multiplied after base', 'int combined = unchecked(speedFactor * durationFactor) >> FixedShift;\n            return Math.Min(MaximumIntensity, unchecked(baseIntensity * combined) >> FixedShift);', 'return Math.Min(MaximumIntensity, (int)(((long)baseIntensity * speedFactor * durationFactor) >> 24));')
add('intensity adds lower clamp', 'return Math.Min(MaximumIntensity, unchecked(baseIntensity * combined) >> FixedShift);', 'return Math.Clamp(unchecked(baseIntensity * combined) >> FixedShift, 0, MaximumIntensity);')
add('intensity widens base product', 'unchecked(baseIntensity * combined) >> FixedShift', '(int)(((long)baseIntensity * combined) >> FixedShift)')
add('tour speed wrong centre', 'Math.Clamp(speed - 100, -50, 50)', 'Math.Clamp(speed - 90, -50, 50)')
add('tour speed ignores lower limit', 'Math.Clamp(speed - 100, -50, 50)', 'Math.Min(speed - 100, 50)')
add('tour speed ignores upper limit', 'Math.Clamp(speed - 100, -50, 50)', 'Math.Max(speed - 100, -50)')
add('tour speed wrong divisor', 'Math.Clamp(speed - 100, -50, 50) / 10', 'Math.Clamp(speed - 100, -50, 50) / 5')
add('tour speed wrong floor', '=> Math.Max(2, (int)unchecked', '=> Math.Max(1, (int)unchecked')
add('tour speed loses signed narrowing', '(int)unchecked((short)(baseSpeed + Math.Clamp(speed - 100, -50, 50) / 10))', '(baseSpeed + Math.Clamp(speed - 100, -50, 50) / 10)')
add('track speed wrong divisor', '((byte)(speed / 20))', '((byte)(speed / 10))')
add('track speed loses byte wrap', 'public static byte TrackVehicleSpeed(int speed) => unchecked((byte)(speed / 20));', 'public static int TrackVehicleSpeed(int speed) => speed / 20;')
add('coaster velocity ignores slider', 'unchecked(maximumAt100 * speed) / SpeedDivisor', 'maximumAt100')
add('coaster velocity ignores minimum', '=> Math.Max(minimum, Math.Min(velocity, unchecked(maximumAt100 * speed) / SpeedDivisor));', '=> Math.Min(velocity, unchecked(maximumAt100 * speed) / SpeedDivisor);')
add('coaster velocity reverses clamp order', 'Math.Max(minimum, Math.Min(velocity, unchecked(maximumAt100 * speed) / SpeedDivisor))', 'Math.Min(unchecked(maximumAt100 * speed) / SpeedDivisor, Math.Max(minimum, velocity))')
add('coaster velocity widens product', 'unchecked(maximumAt100 * speed) / SpeedDivisor', '(int)((long)maximumAt100 * speed / SpeedDivisor)')
add('forecast ignores nine', '(9 * duration)', 'duration')
add('forecast ignores duration', '(9 * duration)', '9')
add('forecast swaps class shifts', '(coaster ? 12 : 15)', '(coaster ? 15 : 12)')
add('forecast no upper cap', '100 - Math.Min(100, unchecked(wearPrediction * (9 * duration)) >> (coaster ? 12 : 15))', '100 - (unchecked(wearPrediction * (9 * duration)) >> (coaster ? 12 : 15))')
add('forecast clamps negative deduction', '100 - Math.Min(100, unchecked(wearPrediction * (9 * duration)) >> (coaster ? 12 : 15))', '100 - Math.Clamp(unchecked(wearPrediction * (9 * duration)) >> (coaster ? 12 : 15), 0, 100)')


def sha(data):
    return hashlib.sha256(data).hexdigest()


def atomic(path, data):
    temp = path.with_suffix(path.suffix + '.tmp')
    with temp.open('wb') as f:
        f.write(data)
        f.flush()
        os.fsync(f.fileno())
    temp.replace(path)


def write_json(path, data):
    atomic(path, (json.dumps(data, indent=2) + '\n').encode())


def recover():
    if not BACKUP.exists():
        return
    original = BACKUP.read_bytes()
    journal = json.loads(JOURNAL.read_text()) if JOURNAL.exists() else {}
    if sha(SOURCE.read_bytes()) not in (sha(original), journal.get('active_sha256')):
        raise RuntimeError('Source changed outside the sweep; refusing to overwrite it. Inspect backup.')
    atomic(SOURCE, original)
    BACKUP.unlink()
    JOURNAL.unlink(missing_ok=True)
    print('Recovered original RidePanel.cs from interrupted sweep.', flush=True)


class Interrupted(BaseException):
    pass


def stop(signum, frame):
    raise Interrupted(f'signal {signum}')


child = None

def run_tests(filter_expression=None):
    global child
    args = ['dotnet', 'test', str(PROJECT), '--no-restore', '--nologo']
    if filter_expression:
        args += ['--filter', filter_expression]
    started = time.monotonic()
    child = subprocess.Popen(args, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                             text=True, start_new_session=True)
    output, _ = child.communicate(timeout=120)
    result = {'exit_code': child.returncode, 'seconds': round(time.monotonic() - started, 2),
              'summary': next((l.strip() for l in output.splitlines() if 'Total:' in l), ''),
              'failed_tests': [l.strip() for l in output.splitlines() if l.strip().startswith('Failed TPW.')],
              'compile_error': 'error CS' in output or 'error MSB' in output}
    child = None
    if not result['summary']:
        result['output'] = output[-4000:]
    return result


if __name__ == '__main__':
    os.chdir(ROOT)
    if subprocess.check_output(['git', 'branch', '--show-current'], text=True).strip() != 'ride-panel':
        raise RuntimeError('Run only on ride-panel.')
    recover()
    if '--recover' in sys.argv:
        sys.exit(0)
    original = SOURCE.read_bytes()
    source_text = original.decode()
    for name, old, new in MUTANTS:
        if source_text.count(old) != 1:
            raise RuntimeError(f'{name}: replacement has {source_text.count(old)} matches, expected one')
    report = {'source': str(SOURCE.relative_to(ROOT)), 'original_sha256': sha(original),
              'filter': FILTER, 'mutations': [], 'status': 'running',
              'restoration': 'top-level try/finally, signal handlers, durable backup and recovery journal'}
    if '--resume' in sys.argv and RESULT.exists():
        previous = json.loads(RESULT.read_text())
        if previous['original_sha256'] != sha(original):
            raise RuntimeError('Cannot reuse mutation results after changing production source.')
        definitions = {name: (old, new) for name, old, new in MUTANTS}
        for result in previous['mutations']:
            if definitions.get(result['name']) != (result['old'], result['new']):
                raise RuntimeError('A previous mutation definition changed; run a fresh sweep.')
        report['mutations'] = previous['mutations']
        report['previous_runs'] = previous.get('previous_runs', []) + [{
            'status': previous['status'], 'baseline': previous.get('baseline'),
            'restored': previous.get('restored'), 'error': previous.get('error')}]
    for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
        signal.signal(sig, stop)
    # Deliberately TOP-LEVEL: restoration wraps baseline, every mutation, verification, and errors.
    try:
        atomic(BACKUP, original)
        write_json(JOURNAL, {'active_sha256': sha(original)})
        report['baseline'] = run_tests()
        if report['baseline']['exit_code'] != 0:
            raise RuntimeError('Baseline failed')
        write_json(RESULT, report)
        for index, (name, old, new) in enumerate(MUTANTS, 1):
            previous = next((m for m in report['mutations'] if m['name'] == name), None)
            if previous is not None and previous['status'] == 'killed':
                continue
            mutant = source_text.replace(old, new).encode()
            write_json(JOURNAL, {'active_sha256': sha(mutant)})
            atomic(SOURCE, mutant)
            try:
                result = run_tests(FILTER)
            finally:
                atomic(SOURCE, original)
            killed = (result['exit_code'] != 0 and not result['compile_error']
                      and bool(result['summary']) and bool(result['failed_tests']))
            result.update({'name': name, 'old': old, 'new': new,
                           'status': 'killed' if killed else 'survived' if result['exit_code'] == 0 else 'invalid'})
            if previous is not None:
                result['previous_attempts'] = previous.get('previous_attempts', []) + [
                    {k: v for k, v in previous.items() if k != 'previous_attempts'}]
                report['mutations'][report['mutations'].index(previous)] = result
            else:
                report['mutations'].append(result)
            write_json(RESULT, report)
            print(f'{index}/{len(MUTANTS)} {result["status"]}: {name}', flush=True)
        report['final_verification'] = run_tests()
        report['killed'] = sum(m['status'] == 'killed' for m in report['mutations'])
        report['survived'] = [m['name'] for m in report['mutations'] if m['status'] == 'survived']
        report['invalid'] = [m['name'] for m in report['mutations'] if m['status'] == 'invalid']
        report['mutation_count'] = len(MUTANTS)
        report['status'] = ('passed' if not report['survived'] and not report['invalid']
                            and report['final_verification']['exit_code'] == 0 else 'failed')
    except BaseException as exc:
        report['status'] = 'interrupted' if isinstance(exc, (Interrupted, KeyboardInterrupt)) else 'error'
        report['error'] = str(exc)
        raise
    finally:
        for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
            signal.signal(sig, signal.SIG_IGN)
        if child is not None and child.poll() is None:
            os.killpg(child.pid, signal.SIGTERM)
            try:
                child.communicate(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(child.pid, signal.SIGKILL)
                child.communicate()
        atomic(SOURCE, original)
        report['restored_sha256'] = sha(SOURCE.read_bytes())
        report['restored'] = report['restored_sha256'] == report['original_sha256']
        write_json(RESULT, report)
        BACKUP.unlink(missing_ok=True)
        JOURNAL.unlink(missing_ok=True)
    sys.exit(0 if report['status'] == 'passed' else 1)
