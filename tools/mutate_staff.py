#!/usr/bin/env python3
"""Compile one staff-rule mutant at a time, require a failing test, restore in top-level finally.

Only the four new staff source files are mutated; Mechanic.cs is never edited. SIGINT/TERM/HUP
restore too. SIGKILL/power loss need --recover; a durable journal refuses unrelated source edits.
Run on staff-trace, with no concurrent changes to these four files.
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
RESULT = ROOT / 'findings/staff-mutations.json'
BACKUP = ROOT / '.staff-mutation-backup.json'
FILTER = 'FullyQualifiedName~StaffTraceTests'
MUTANTS = []


def add(file, name, old, new):
    MUTANTS.append({'file': f'core/TPW.Sim/{file}.cs', 'name': name, 'old': old, 'new': new})


def motion(name, old, new):
    add('StaffMotion', name, old, new)


motion('mechanic selects cleaner table', 'StaffKind.Mechanic => Mechanic.UnknownSecondColumn',
       'StaffKind.Mechanic => Handyman.UnusedThirdColumn')
motion('cleaner selects mechanic table', 'StaffKind.Cleaner => Handyman.UnusedThirdColumn',
       'StaffKind.Cleaner => Mechanic.UnknownSecondColumn')
motion('mechanic uses repair duration as speed', 'StaffKind.Mechanic => Mechanic.UnknownSecondColumn',
       'StaffKind.Mechanic => Mechanic.RepairTicks')
motion('base speed born at visitor maximum', 'InitialBaseSpeed = 15', 'InitialBaseSpeed = 29')
motion('base speed ignores current packed field', 'return packedBaseSpeed & 0x1F;', 'return 15;')
motion('base speed loses fifth bit', 'packedBaseSpeed & 0x1F', 'packedBaseSpeed & 0x0F')
motion('base speed does not narrow', 'packedBaseSpeed & 0x1F', 'packedBaseSpeed')
motion('skill does not mask', 'staff.Skill & 7', 'staff.Skill')
motion('skill uses only two bits', 'staff.Skill & 7', 'staff.Skill & 3')
motion('absent skill rows silently clamp', 'throw new ArgumentOutOfRangeException(nameof(staff.Skill));',
       'return table[table.Length - 1];')
motion('skill lookup always first row', 'return table[row];', 'return table[0];')
for kind in ('Mechanic', 'Cleaner'):
    for row in range(5):
        motion(f'{kind} speed row {row} changed', 'return table[row];',
               f'return table[row] + (staff.Kind == StaffKind.{kind} && row == {row} ? 1 : 0);')
motion('step ignores supplied base speed', 'Speed(staff, packedBaseSpeed) * timescale', 'Speed(staff) * timescale')
motion('step ignores timescale', '* timescale', '* 0x4000')
motion('step rounds up', 'Speed(staff, packedBaseSpeed) * timescale)',
       'Speed(staff, packedBaseSpeed) * timescale + 0x3FFF)')
motion('step shift twelve', '>> VisitorWalking.StepShift', '>> 12')
motion('step arithmetic shift', '(uint)(Speed', '(int)(Speed')
motion('step widens product', '(uint)(Speed(staff, packedBaseSpeed) * timescale)',
       '(ulong)((long)Speed(staff, packedBaseSpeed) * timescale)')


def art(name, old, new):
    add('StaffAppearance', name, old, new)


for kind, values in [('Mechanic', (263, 264, 8)), ('Guard', (264, 308, 9)),
                     ('Cleaner', (266, 352, 10)), ('Researcher', (274, 396, 11))]:
    old = f'StaffKind.{kind} => new{values}'
    for index, field in enumerate(('resource', 'sheet base', 'people block')):
        changed = list(values)
        changed[index] += 1
        art(f'{kind} wrong {field}', old, f'StaffKind.{kind} => new{tuple(changed)}')
for theme, resource in enumerate((403, 401, 402, 404)):
    art(f'entertainer theme {theme} wrong resource', f'{theme} => {resource}', f'{theme} => {resource + 1}')
art('entertainer given sheet base', '}, null, null)', '}, 440, null)')
art('entertainer given costume block', '}, null, null)', '}, null, 4)')
art('unknown theme silently substituted', 'throw new ArgumentOutOfRangeException(nameof(theme))', '403')
art('unknown class silently substituted', 'throw new ArgumentOutOfRangeException(nameof(kind))', 'new(263, 264, 8)')
art('five walk frames', 'WalkFrames = 8', 'WalkFrames = 5')
art('negative frame accepted', '(uint)frame >= WalkFrames', 'frame >= WalkFrames')
art('frame eight accepted', '(uint)frame >= WalkFrames', '(uint)frame > WalkFrames')
art('camera rotation subtracted', 'personFacing + cameraFacing', 'personFacing - cameraFacing')
art('camera rotation ignored', 'personFacing + cameraFacing', 'personFacing')
art('relative facing not wrapped', '(personFacing + cameraFacing) & 7', '(personFacing + cameraFacing)')
art('direction four not mirrored', 'facing < 5', 'facing < 4')
art('direction five mirrored', 'facing < 5', 'facing <= 5')
art('mirror flag reversed', '+ frame, mirror)', '+ frame, !mirror)')
art('fold from seven', '8 - facing', '7 - facing')
art('no stored-facing fold', 'mirror ? facing : 8 - facing', 'facing')
art('frame ignores facing', 'storedFacing * WalkFrames + frame', 'frame')
art('frame ignores animation phase', 'storedFacing * WalkFrames + frame', 'storedFacing * WalkFrames')
art('mirror flag discarded', '+ frame, mirror)', '+ frame, false)')


def hire(name, old, new):
    add('StaffHiring', name, old, new)


hire('always allocate mechanic', 'world.AllocateAndEmploy(kind, variant)', 'world.AllocateAndEmploy(StaffKind.Mechanic, variant)')
hire('always allocate variant zero', 'world.AllocateAndEmploy(kind, variant)', 'world.AllocateAndEmploy(kind, 0)')
hire('pool exhaustion falls through', 'if (staff == null) return null;', '')
hire('record kind ignored', 'world.RecruitLevel(kind, variant)', 'world.RecruitLevel(StaffKind.Guard, variant)')
hire('record variant ignored', 'world.RecruitLevel(kind, variant)', 'world.RecruitLevel(kind, 0)')
hire('skill uses full record word', 'world.RecruitLevel(kind, variant) & 7', 'world.RecruitLevel(kind, variant)')
hire('pay grade used as skill', 'world.RecruitLevel(kind, variant) & 7', '(world.RecruitLevel(kind, variant) & 7) + 1')
for name, old in [('morale not reset', 'staff.Morale = InitialMorale;'),
                  ('tiredness not reset', 'staff.Tiredness = InitialTiredness;'),
                  ('skill not reset', 'staff.Skill = world.RecruitLevel(kind, variant) & 7;'),
                  ('purpose not reset', 'staff.Purpose = InitialPurpose;'),
                  ('target not reset', 'staff.HasTarget = false;'),
                  ('patrol assignment retained', 'world.ClearPatrolArea(staff);'),
                  ('appearance not initialized', 'world.InitializeAppearance(staff, InitialAnimation, StaffMotion.InitialBaseSpeed);'),
                  ('hire day omitted', 'world.SetHireDay(staff, world.TotalDays);'),
                  ('not held at hire', 'world.SetHeld(staff, true);'),
                  ('payment omitted', 'world.TrySpend(Money.Zero);'),
                  ('placement never finished', 'world.FinishPlacement(staff);')]:
    hire(name, old, ';')
hire('report morale silently changed', 'InitialMorale = 100', 'InitialMorale = 99')
hire('report tiredness silently changed', 'InitialTiredness = 0', 'InitialTiredness = 1')
hire('initial animation wrong', 'InitialAnimation = 13', 'InitialAnimation = 15')
hire('initial purpose wrong', 'InitialPurpose = (StaffPurpose)2', 'InitialPurpose = (StaffPurpose)0')
hire('begin preserves state stack', 'staff.SetState(StaffState.Idle);\n            world.ClearPatrolArea',
     'staff.PushState(StaffState.Idle);\n            world.ClearPatrolArea')
hire('employment date shifted', 'world.SetHireDay(staff, world.TotalDays);', 'world.SetHireDay(staff, world.TotalDays + 1);')
hire('invalid placement accepted', 'if (!world.IsPlacementPath(staff)) return false;', 'world.IsPlacementPath(staff);')
hire('valid placement refused', 'if (!world.IsPlacementPath(staff)) return false;', 'if (world.IsPlacementPath(staff)) return false;')
hire('ghost never removed', 'world.SetHeld(staff, false);', ';')
hire('placement keeps previous state', 'world.SetHeld(staff, false);\n            staff.SetState(StaffState.Idle);',
     'world.SetHeld(staff, false);')
hire('hire charges one pound', 'world.TrySpend(Money.Zero);', 'world.TrySpend(Money.FromPounds(1));')
hire('failed zero payment aborts placement', 'world.TrySpend(Money.Zero);', 'if (!world.TrySpend(Money.Zero)) return false;')
hire('placed member reported as refused', '            return true;', '            return false;')
hire('cancel leaves employee on wage list', '=> world.Release(staff);', '{ }')


def path(name, old, new):
    add('StaffRideNavigation', name, old, new)


path('job retains old waypoints', 'world.FreeWaypoints(staff);', ';')
path('job selects queue head', 'world.EntranceOutside(staff)', 'world.QueuePath(staff)[0]')
path('job changes controller state', 'world.FreeWaypoints(staff);', 'staff.SetState(StaffState.Idle); world.FreeWaypoints(staff);')
path('leave explicitly frees path', 'var path = world.QueuePath(staff);', 'world.FreeWaypoints(staff); var path = world.QueuePath(staff);')
path('leave changes controller state', 'var path = world.QueuePath(staff);', 'staff.SetState(StaffState.Idle); var path = world.QueuePath(staff);')
path('leave selects first queue tile', 'path[path.Count - 1]', 'path[0]')
path('empty queue falls back to entrance', 'new QueueTile(0, 0)', 'world.EntranceOutside(staff)')
path('empty queue refuses request', 'var path = world.QueuePath(staff);', 'var path = world.QueuePath(staff); if (path.Count == 0) return false;')
for axis in ('X', 'Y'):
    path(f'queue {axis} read signed', f'(byte)tile.{axis}', f'(sbyte)tile.{axis}')
    path(f'queue {axis} not narrowed', f'unchecked((byte)tile.{axis})', f'tile.{axis}')
path('destination x not centred', '(tile.X << 8) | 0x80', '(tile.X << 8)')
path('destination y not centred', '(tile.Y << 8) + 0x80', '(tile.Y << 8)')
path('destination x units wrong', 'tile.X << 8', 'tile.X << 7')
path('destination y units wrong', 'tile.Y << 8', 'tile.Y << 7')
path('destination axes exchanged', '(tile.X << 8) | 0x80, (tile.Y << 8) + 0x80',
     '(tile.Y << 8) | 0x80, (tile.X << 8) + 0x80')
path('path flags wrong', '0x11, 0);', '0x10, 0);')
path('secondary flags wrong', '0x11, 0);', '0x11, 1);')
path('path refusal ignored', '0x11, 0);', '0x11, 0) || true;')


def sha(data):
    return hashlib.sha256(data).hexdigest()


def atomic_json(path, value):
    tmp = path.with_suffix(path.suffix + '.tmp')
    with tmp.open('w') as f:
        json.dump(value, f, indent=2)
        f.write('\n')
        f.flush()
        os.fsync(f.fileno())
    tmp.replace(path)


def recover():
    if not BACKUP.exists():
        return
    journal = json.loads(BACKUP.read_text())
    for file, original in journal['originals'].items():
        current = sha((ROOT / file).read_bytes())
        if current not in (sha(original.encode()), journal.get('active', {}).get(file)):
            raise RuntimeError(f'{file} changed externally; refusing to overwrite. Inspect {BACKUP}.')
    for file, original in journal['originals'].items():
        (ROOT / file).write_bytes(original.encode())
    BACKUP.unlink()


class Interrupted(BaseException):
    pass


def stop(signum, frame):
    raise Interrupted(f'signal {signum}')


child = None


def run_tests(filtered=False):
    global child
    args = ['dotnet', 'test', 'tests/TPW.Sim.Tests/', '--no-restore', '--nologo']
    if filtered:
        args += ['--filter', FILTER]
    started = time.monotonic()
    child = subprocess.Popen(args, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                             text=True, start_new_session=True)
    output, _ = child.communicate(timeout=120)
    result = {'exit_code': child.returncode, 'seconds': round(time.monotonic() - started, 2),
              'summary': next((line.strip() for line in output.splitlines() if 'Total:' in line), ''),
              'failed_tests': [line.strip() for line in output.splitlines() if line.strip().startswith('Failed TPW.')],
              'compile_error': 'error CS' in output or 'error MSB' in output}
    child = None
    if not result['summary']:
        result['output'] = output[-3000:]
    return result


if __name__ == '__main__':
    os.chdir(ROOT)
    if subprocess.check_output(['git', 'branch', '--show-current'], text=True).strip() != 'staff-trace':
        raise RuntimeError('Run only on staff-trace.')
    recover()
    if '--recover' in sys.argv:
        sys.exit(0)
    originals = {m['file']: (ROOT / m['file']).read_text() for m in MUTANTS}
    for m in MUTANTS:
        if originals[m['file']].count(m['old']) != 1:
            raise RuntimeError(f"{m['name']}: expected one replacement match")
    report = {'status': 'running', 'filter': FILTER, 'mutation_count': len(MUTANTS),
              'source_sha256': {f: sha(s.encode()) for f, s in originals.items()},
              'test_sha256': sha((ROOT / 'tests/TPW.Sim.Tests/StaffTraceTests.cs').read_bytes()),
              'mutations': [], 'restoration': 'top-level try/finally; signals; durable --recover journal'}
    if RESULT.exists():
        previous = json.loads(RESULT.read_text())
        report['interruption_checks'] = previous.get('interruption_checks', [])
        if previous['status'] == 'interrupted':
            report['interruption_checks'].append({
                'reason': previous.get('error'), 'restored': previous.get('restored'),
                'completed_mutations': len(previous['mutations']),
                'source_sha256': previous['source_sha256'],
                'restored_sha256': previous['restored_sha256']})
    for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
        signal.signal(sig, stop)
    # TOP LEVEL: baseline, each compiled mutation, final verification and all failure paths are covered.
    try:
        atomic_json(BACKUP, {'originals': originals})
        report['baseline'] = run_tests()
        if report['baseline']['exit_code'] != 0 or not report['baseline']['summary']:
            raise RuntimeError('Baseline failed or did not run tests')
        for index, m in enumerate(MUTANTS, 1):
            mutant = originals[m['file']].replace(m['old'], m['new'])
            atomic_json(BACKUP, {'originals': originals, 'active': {m['file']: sha(mutant.encode())}})
            (ROOT / m['file']).write_text(mutant)
            try:
                result = run_tests(filtered=True)
            finally:
                (ROOT / m['file']).write_text(originals[m['file']])
            killed = result['exit_code'] != 0 and not result['compile_error'] and bool(result['failed_tests'])
            result.update(m)
            result['status'] = 'killed' if killed else 'survived' if result['exit_code'] == 0 else 'invalid'
            report['mutations'].append(result)
            atomic_json(RESULT, report)
            print(f"{index}/{len(MUTANTS)} {result['status']}: {m['name']}", flush=True)
        report['final_verification'] = run_tests()
        report['killed'] = sum(m['status'] == 'killed' for m in report['mutations'])
        report['survived'] = [m['name'] for m in report['mutations'] if m['status'] == 'survived']
        report['invalid'] = [m['name'] for m in report['mutations'] if m['status'] == 'invalid']
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
        for file, original in originals.items():
            (ROOT / file).write_text(original)
        report['restored_sha256'] = {f: sha((ROOT / f).read_bytes()) for f in originals}
        report['restored'] = report['restored_sha256'] == report['source_sha256']
        atomic_json(RESULT, report)
        BACKUP.unlink(missing_ok=True)
    sys.exit(0 if report['status'] == 'passed' else 1)
