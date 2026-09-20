#!/usr/bin/env python3
"""Mutation sweep for the mechanic: one compiled behavioural mutant at a time, across three files.

Same shape as mutate_ride_panel.py (top-level try/finally, signal handlers, durable backups and a
journal for SIGKILL recovery), generalised to several source files and with --limit N so a sweep
can be run in foreground slices that each finish inside a shell timeout; the report's status is
'partial' until every mutant has been tried, then the final verification runs.

Run only on the mechanic-fix worktree, with no concurrent edits of the mutated files.
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
FILES = {
    'mechanic': ROOT / 'core/TPW.Sim/Mechanic.cs',
    'closing': ROOT / 'core/TPW.Sim/RideClosing.cs',
    'staff': ROOT / 'core/TPW.Sim/StaffBase.cs',
}
RESULT = ROOT / 'findings/mechanic-mutations.json'
BACKUP_DIR = ROOT / '.mechanic-mutation-backup'
JOURNAL = ROOT / '.mechanic-mutation-journal.json'
PROJECT = ROOT / 'tests/TPW.Sim.Tests'
FILTER = 'FullyQualifiedName~MechanicTests|FullyQualifiedName~RidePanelTests'
BRANCH = 'mechanic-fix'
MUTANTS = []


def add(name, old, new, file='mechanic'):
    MUTANTS.append((name, file, old, new))


# --- constants (all READ) ---
add('idle tiredness sign flipped', 'IdleTiredness = 6;', 'IdleTiredness = -6;')
add('idle morale dropped', 'IdleMorale = 1;', 'IdleMorale = 0;')
add('repair walk tiredness sign flipped', 'RepairWalkTiredness = -2;', 'RepairWalkTiredness = 2;')
add('repair walk morale sign flipped', 'RepairWalkMorale = -2;', 'RepairWalkMorale = 2;')
add('reopen morale dropped', 'ReopenMorale = 10;', 'ReopenMorale = 0;')
add('repair table read as flat s16', 'RepairTicks = { 240, 180, 120, 60, 60 };', 'RepairTicks = { 240, 9, 180, 12, 120 };')

# --- idle ---
add('walk cost charged on upgrade walks too', 'if (staff.Purpose != MechanicStates.ToRepair) return;', ';')
add('idle ignores the strike',
    '            if (world.IsTypeOnStrike(staff.Kind)) return;\n\n            staff.Tiredness',
    '            staff.Tiredness')
add('coin flip inverted', 'if (rng.Next(2) == 0)', 'if (rng.Next(2) != 0)')
add('upgrade-first falls back to broken rides (the report\'s symmetry)',
    'if (Take(staff, world.TryClaimQueuedUpgradeAsRepair(staff), MechanicStates.GoToBrokenRide)) return;',
    'if (Take(staff, world.TryClaimBrokenRide(staff), MechanicStates.GoToBrokenRide)) return;')
add('dead second queue call dropped',
    'if (Take(staff, world.TryClaimQueuedUpgradeAsRepair(staff), MechanicStates.GoToBrokenRide)) return;', ';')
add('second queue call lands in 57',
    'if (Take(staff, world.TryClaimQueuedUpgradeAsRepair(staff), MechanicStates.GoToBrokenRide)) return;',
    'if (Take(staff, world.TryClaimQueuedUpgradeAsRepair(staff), MechanicStates.GoToUpgradeRide)) return;')
add('repair-first does not fall back to the queue',
    '                if (Take(staff, world.TryClaimQueuedUpgrade(staff), MechanicStates.GoToUpgradeRide)) return;\n            }\n            else\n',
    '            }\n            else\n')
add('claim does not set the target', '            staff.HasTarget = true;\n            staff.SetState(go);', '            staff.SetState(go);')
add('claim pushes instead of setting', '            staff.HasTarget = true;\n            staff.SetState(go);', '            staff.HasTarget = true;\n            staff.PushState(go);')

# --- 56/57 ---
add('refused path request still sets off', 'if (!world.TryPathToClaimedRide(staff)) return false;', 'world.TryPathToClaimedRide(staff);')
add('set-off does not stamp the clock',
    '            staff.BusyUntil = world.NowTick;\n            staff.Purpose = forRepair', '            staff.Purpose = forRepair')
add('set-off purposes swapped',
    'staff.Purpose = forRepair ? MechanicStates.ToRepair : MechanicStates.ToUpgrade;',
    'staff.Purpose = forRepair ? MechanicStates.ToUpgrade : MechanicStates.ToRepair;')
add('set-off sets instead of pushing',
    '            staff.PushState(StaffState.Walking);\n            return true;\n        }\n\n        /// <summary>The mechanic\'s share',
    '            staff.SetState(StaffState.Walking);\n            return true;\n        }\n\n        /// <summary>The mechanic\'s share')

# --- arrival ---
add('still-walking skips the base cost',
    '                WalkTick(staff);\n                StaffBase.Arrive(staff, world, stillWalking: true);', '                WalkTick(staff);')
add('arrival does not Set 0 first',
    '            staff.SetState(StaffState.Idle);\n            switch (staff.Purpose)', '            switch (staff.Purpose)')
add('arrival closes a ride he no longer holds',
    '                    if (!staff.HasTarget) return;\n                    staff.BusyUntil = 0;', '                    staff.BusyUntil = 0;')
add('arrival keeps the set-off stamp',
    '                    staff.BusyUntil = 0;\n                    staff.SetState(staff.Purpose == MechanicStates.ToRepair',
    '                    staff.SetState(staff.Purpose == MechanicStates.ToRepair')
add('arrival closing states swapped',
    '? MechanicStates.ClosingRide\n                        : MechanicStates.ClosingForUpgrade',
    '? MechanicStates.ClosingForUpgrade\n                        : MechanicStates.ClosingRide')
add('arrival 22 keeps the target',
    '                case MechanicStates.LeaveRide:\n                    staff.HasTarget = false;\n                    return;\n                default:\n                    StaffBase.Arrive',
    '                case MechanicStates.LeaveRide:\n                    return;\n                default:\n                    StaffBase.Arrive')
add('arrival swallows base purposes', '                    StaffBase.Arrive(staff, world, stillWalking: false);', '                    ;')

# --- closing ---
add('closing threshold ignores the footprint', 'RideClosing.Threshold(world.FootprintSpan(staff))', 'RideClosing.Threshold(1)')
add('closing does not raise the progress', 'world.SetClosingProgress(staff, RideClosing.Raise(progress, world.ClosingStep, threshold));', ';')
add('closing states swapped',
    'staff.SetState(forUpgrade ? MechanicStates.Upgrading : MechanicStates.Repairing);',
    'staff.SetState(forUpgrade ? MechanicStates.Repairing : MechanicStates.Upgrading);')
add('closing does not mark the ride under repair', '            world.MarkRideUnderRepair(staff);\n', '')
add('timer from the second column', 'Handyman.BySkill(RepairTicks, staff.Skill)', 'Handyman.BySkill(UnknownSecondColumn, staff.Skill)')
add('timer ignores skill', 'Handyman.BySkill(RepairTicks, staff.Skill)', 'RepairTicks[0]')

# --- work ---
add('zero deadline fires', '            if (staff.BusyUntil == 0) return false;\n', '')
add('work finishes at equality', 'if (!(staff.BusyUntil < world.NowTick)) return false;', 'if (!(staff.BusyUntil <= world.NowTick)) return false;')
add('repair also buys the upgrade', 'if (forUpgrade) world.CompleteUpgrade(staff);', 'world.CompleteUpgrade(staff);')
add('upgrade is never bought', 'if (forUpgrade) world.CompleteUpgrade(staff);', ';')
add('work pays the morale',
    '            staff.SetState(MechanicStates.OpeningRide);\n            return true;',
    '            staff.Morale = Stat.Add(staff.Morale, ReopenMorale);\n            staff.SetState(MechanicStates.OpeningRide);\n            return true;')
add('work skips reopening', '            staff.SetState(MechanicStates.OpeningRide);\n            return true;', '            staff.SetState(MechanicStates.LeavingRide);\n            return true;')

# --- reopening ---
add('reopening uses the closing predicate', 'if (progress != 0)', 'if (progress >= RideClosing.Threshold(world.FootprintSpan(staff)))')
add('reopening does not lower the progress', 'world.SetClosingProgress(staff, RideClosing.Lower(progress, world.ClosingStep));', ';')
add('reopening pays no morale',
    '            staff.Morale = Stat.Add(staff.Morale, ReopenMorale);\n            return true;', '            return true;')
add('reopening drops the target',
    '            world.MarkRideOpenAndRelease(staff);\n            staff.SetState(MechanicStates.LeavingRide);',
    '            world.MarkRideOpenAndRelease(staff);\n            staff.HasTarget = false;\n            staff.SetState(MechanicStates.LeavingRide);')
add('reopening goes idle', '            staff.SetState(MechanicStates.LeavingRide);\n            staff.Morale', '            staff.SetState(StaffState.Idle);\n            staff.Morale')

# --- leaving ---
add('refused leave path goes idle',
    'if (!world.TryPathToLeavePoint(staff)) return false;',
    'if (!world.TryPathToLeavePoint(staff)) { staff.HasTarget = false; staff.SetState(StaffState.Idle); return false; }')
add('leaving sets instead of pushing',
    '            staff.Purpose = MechanicStates.LeaveRide;\n            staff.PushState(StaffState.Walking);',
    '            staff.Purpose = MechanicStates.LeaveRide;\n            staff.SetState(StaffState.Walking);')

# --- messages ---
add('failed walk keeps the claim', '                world.ReleaseClaim(staff);\n                HandJobOn', '                HandJobOn')
add('failed walk does not offer the job on', '                HandJobOn(staff, world, forRepair: staff.Purpose == MechanicStates.ToRepair);\n', '')
add('job is always offered as an upgrade', 'forRepair: staff.Purpose == MechanicStates.ToRepair', 'forRepair: false')
add('failed job walk keeps the target',
    '                staff.HasTarget = false;\n                staff.SetState(StaffState.Idle);\n                return;\n            }\n            if (staff.Purpose == MechanicStates.LeaveRide)',
    '                staff.SetState(StaffState.Idle);\n                return;\n            }\n            if (staff.Purpose == MechanicStates.LeaveRide)')
add('failed leave walk keeps the target',
    '            if (staff.Purpose == MechanicStates.LeaveRide)\n            {\n                staff.HasTarget = false;',
    '            if (staff.Purpose == MechanicStates.LeaveRide)\n            {')
add('refused hand-off moves to the next candidate',
    '                    forRepair ? MechanicStates.GoToBrokenRide : MechanicStates.GoToUpgradeRide);\n                return;',
    '                    forRepair ? MechanicStates.GoToBrokenRide : MechanicStates.GoToUpgradeRide);\n                if (candidate.HasTarget) return;')
add('hand-off offers to everyone',
    '                    forRepair ? MechanicStates.GoToBrokenRide : MechanicStates.GoToUpgradeRide);\n                return;',
    '                    forRepair ? MechanicStates.GoToBrokenRide : MechanicStates.GoToUpgradeRide);\n                continue;')
add('hand-off does not skip unsuitable candidates', 'if (!CanTakeHandedJob(candidate)) continue;', ';')
add('hand-off states swapped',
    '                    forRepair ? MechanicStates.GoToBrokenRide : MechanicStates.GoToUpgradeRide);\n                return;',
    '                    forRepair ? MechanicStates.GoToUpgradeRide : MechanicStates.GoToBrokenRide);\n                return;')
add('candidate filter ignores the target',
    'return !candidate.HasTarget && !StaffBase.IsCommittedToStrikeOrRest(candidate);', 'return !StaffBase.IsCommittedToStrikeOrRest(candidate);')
add('candidate filter ignores strike and rest',
    'return !candidate.HasTarget && !StaffBase.IsCommittedToStrikeOrRest(candidate);', 'return !candidate.HasTarget;')

# --- pick-up sweep ---
add('sweep aborts a mechanic in state 11', '                case StaffState.Walking:\n                    switch (staff.Purpose)', '                    switch (staff.Purpose)')
add('sweep re-claims an upgrade with the repair checks',
    'Take(staff, world.TryClaimRideFor(staff, staff, forRepair: false), MechanicStates.GoToUpgradeRide);',
    'Take(staff, world.TryClaimRideFor(staff, staff, forRepair: true), MechanicStates.GoToUpgradeRide);')
add('sweep re-claims a repair without the checks',
    'Take(staff, world.TryClaimRideFor(staff, staff, forRepair: true), MechanicStates.GoToBrokenRide);',
    'Take(staff, world.TryClaimRideFor(staff, staff, forRepair: false), MechanicStates.GoToBrokenRide);')
add('sweep aborts a leaving mechanic',
    '                        case MechanicStates.LeaveRide:\n                            staff.SetState(MechanicStates.LeavingRide);\n                            return;',
    '                        case MechanicStates.LeaveRide:\n                            Abort(staff, world);\n                            return;')
add('sweep aborts a mechanic at the ride',
    '                case MechanicStates.Upgrading:\n                    return;\n                default:',
    '                case MechanicStates.Upgrading:\n                default:')
add('sweep treats 56 as a walking state',
    '                case StaffState.Walking:\n                    switch (staff.Purpose)',
    '                case StaffState.Walking:\n                case MechanicStates.GoToBrokenRide:\n                    switch (staff.Purpose)')
add('abort keeps the claim', '            if (staff.HasTarget) world.ReleaseClaim(staff);\n            staff.HasTarget = false;', '            staff.HasTarget = false;')
add('abort keeps the target',
    '            staff.HasTarget = false;\n            staff.SetState(StaffState.Idle);\n        }\n    }\n}',
    '            staff.SetState(StaffState.Idle);\n        }\n    }\n}')

# --- RideClosing ---
add('threshold five per tile', 'UnitsPerSpanTile = 10;', 'UnitsPerSpanTile = 5;', 'closing')
add('threshold twenty per tile', 'UnitsPerSpanTile = 10;', 'UnitsPerSpanTile = 20;', 'closing')
add('threshold shifted one too many', '(UnitsPerSpanTile * footprintSpan) << Fixed.FracBits', '(UnitsPerSpanTile * footprintSpan) << (Fixed.FracBits + 1)', 'closing')
add('complete needs strictly above', 'progress >= threshold;', 'progress > threshold;', 'closing')
add('raise does not clamp', 'return threshold < next ? threshold : next;', 'return next;', 'closing')
add('lower does not clamp', 'return next < 0 ? 0 : next;', 'return next;', 'closing')

# --- StaffBase.IsCommittedToStrikeOrRest ---
add('striking is not a commitment',
    'if (staff.State == StaffState.WalkToStrike || staff.State == StaffState.Striking) return true;',
    'if (staff.State == StaffState.WalkToStrike) return true;', 'staff')
add('purpose test in every state',
    '            if ((staff.State == StaffState.WalkToDestination || staff.State == StaffState.PathReady)\n                && (staff.Purpose == StaffPurpose.Strike || staff.Purpose == StaffPurpose.Rest)) return true;',
    '            if (staff.Purpose == StaffPurpose.Strike || staff.Purpose == StaffPurpose.Rest) return true;', 'staff')
add('rest walk is not a commitment',
    '(staff.Purpose == StaffPurpose.Strike || staff.Purpose == StaffPurpose.Rest)) return true;',
    '(staff.Purpose == StaffPurpose.Strike)) return true;', 'staff')
add('purpose 50 dropped', 'return staff.Purpose == StaffPurpose.Unidentified50;', 'return false;', 'staff')


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
    if not BACKUP_DIR.exists():
        return
    journal = json.loads(JOURNAL.read_text()) if JOURNAL.exists() else {}
    for key, path in FILES.items():
        backup = BACKUP_DIR / path.name
        if not backup.exists():
            continue
        original = backup.read_bytes()
        if sha(path.read_bytes()) not in (sha(original), journal.get('active', {}).get(key)):
            raise RuntimeError(f'{path.name} changed outside the sweep; refusing to overwrite it. Inspect {backup}.')
        atomic(path, original)
        backup.unlink()
        print(f'Recovered original {path.name} from interrupted sweep.', flush=True)
    BACKUP_DIR.rmdir()
    JOURNAL.unlink(missing_ok=True)


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
    output, _ = child.communicate(timeout=180)
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
    if subprocess.check_output(['git', 'branch', '--show-current'], text=True).strip() != BRANCH:
        raise RuntimeError(f'Run only on {BRANCH}.')
    recover()
    if '--recover' in sys.argv:
        sys.exit(0)
    limit = None
    if '--limit' in sys.argv:
        limit = int(sys.argv[sys.argv.index('--limit') + 1])
    originals = {key: path.read_bytes() for key, path in FILES.items()}
    texts = {key: data.decode() for key, data in originals.items()}
    for name, file, old, new in MUTANTS:
        if texts[file].count(old) != 1:
            raise RuntimeError(f'{name}: replacement has {texts[file].count(old)} matches in {file}, expected one')
    if len({m[0] for m in MUTANTS}) != len(MUTANTS):
        raise RuntimeError('duplicate mutant names')
    report = {'sources': {key: str(path.relative_to(ROOT)) for key, path in FILES.items()},
              'original_sha256': {key: sha(data) for key, data in originals.items()},
              'filter': FILTER, 'mutations': [], 'status': 'running',
              'restoration': 'top-level try/finally, signal handlers, durable backups and recovery journal'}
    if RESULT.exists() and '--fresh' not in sys.argv:
        previous = json.loads(RESULT.read_text())
        if previous.get('original_sha256') != report['original_sha256']:
            raise RuntimeError('Cannot reuse mutation results after changing production source (pass --fresh).')
        definitions = {name: (file, old, new) for name, file, old, new in MUTANTS}
        for result in previous['mutations']:
            if definitions.get(result['name']) != (result['file'], result['old'], result['new']):
                raise RuntimeError('A previous mutation definition changed; run with --fresh.')
        report['mutations'] = previous['mutations']
        report['previous_runs'] = previous.get('previous_runs', []) + [{
            'status': previous['status'], 'baseline': previous.get('baseline'),
            'restored': previous.get('restored'), 'error': previous.get('error')}]
    for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
        signal.signal(sig, stop)
    # Deliberately TOP-LEVEL: restoration wraps baseline, every mutation, verification, and errors.
    try:
        BACKUP_DIR.mkdir(exist_ok=True)
        for key, path in FILES.items():
            atomic(BACKUP_DIR / path.name, originals[key])
        write_json(JOURNAL, {'active': {}})
        report['baseline'] = run_tests()
        if report['baseline']['exit_code'] != 0:
            raise RuntimeError('Baseline failed')
        write_json(RESULT, report)
        tried = 0
        for index, (name, file, old, new) in enumerate(MUTANTS, 1):
            previous = next((m for m in report['mutations'] if m['name'] == name), None)
            if previous is not None and previous['status'] == 'killed':
                continue
            if limit is not None and tried >= limit:
                break
            tried += 1
            mutant = texts[file].replace(old, new).encode()
            write_json(JOURNAL, {'active': {file: sha(mutant)}})
            atomic(FILES[file], mutant)
            try:
                result = run_tests(FILTER)
            finally:
                atomic(FILES[file], originals[file])
                write_json(JOURNAL, {'active': {}})
            killed = (result['exit_code'] != 0 and not result['compile_error']
                      and bool(result['summary']) and bool(result['failed_tests']))
            result.update({'name': name, 'file': file, 'old': old, 'new': new,
                           'status': 'killed' if killed else 'survived' if result['exit_code'] == 0 else 'invalid'})
            if previous is not None:
                result['previous_attempts'] = previous.get('previous_attempts', []) + [
                    {k: v for k, v in previous.items() if k != 'previous_attempts'}]
                report['mutations'][report['mutations'].index(previous)] = result
            else:
                report['mutations'].append(result)
            write_json(RESULT, report)
            print(f'{index}/{len(MUTANTS)} {result["status"]}: {name}', flush=True)
        remaining = [name for name, *_ in MUTANTS
                     if not any(m['name'] == name and m['status'] == 'killed' for m in report['mutations'])]
        report['killed'] = sum(m['status'] == 'killed' for m in report['mutations'])
        report['survived'] = [m['name'] for m in report['mutations'] if m['status'] == 'survived']
        report['invalid'] = [m['name'] for m in report['mutations'] if m['status'] == 'invalid']
        report['mutation_count'] = len(MUTANTS)
        untried = [name for name in remaining if name not in report['survived'] and name not in report['invalid']]
        if untried:
            report['untried'] = untried
            report['status'] = 'partial'
        else:
            report.pop('untried', None)
            report['final_verification'] = run_tests()
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
        for key, path in FILES.items():
            atomic(path, originals[key])
        report['restored_sha256'] = {key: sha(path.read_bytes()) for key, path in FILES.items()}
        report['restored'] = report['restored_sha256'] == report['original_sha256']
        write_json(RESULT, report)
        for path in FILES.values():
            (BACKUP_DIR / path.name).unlink(missing_ok=True)
        if BACKUP_DIR.exists():
            BACKUP_DIR.rmdir()
        JOURNAL.unlink(missing_ok=True)
    sys.exit(0 if report['status'] in ('passed', 'partial') else 1)
