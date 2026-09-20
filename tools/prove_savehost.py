#!/usr/bin/env python3
"""Reject an empty/default park round trip using two real Godot processes and the file hooks.

No disc bytes or save files enter the audit: artifacts stay in game/obj/savehost-proof.
The JSON snapshots inspect live objects, not ParkSave.Capture's own output.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / 'game/obj/savehost-proof'
FIXTURE = [
    '--park-place=217,26,29,2;237,10,36,3;195,30,38,1',
    '--park-queue=220,12,24,1', '--park-lay=21,19,21,44',
    '--park-hire=0,20,20;1,21,20;2,22,20;3,23,20;4,24,20', '--park-upgrade=217',
]


def run(godot, data, label, flags):
    env = dict(os.environ, TPW_DATA=str(data))
    command = [str(godot), '--headless', '--path', str(ROOT / 'game'), '--', '--no-boot', '--park=203', *flags]
    result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=90)
    log = result.stdout + result.stderr
    (WORK / f'{label}.log').write_text(log)
    return result.returncode, log


def snapshot(log, label):
    prefix = '[save-proof] ' + label + ' '
    lines = [line[len(prefix):] for line in log.splitlines() if line.startswith(prefix)]
    assert len(lines) == 1, f'Expected exactly one {label} snapshot'
    return json.loads(lines[0])


def prove(godot, data, controls=True):
    WORK.mkdir(parents=True, exist_ok=True)
    save = WORK / 'park.save'
    # REJECTS accidentally loading yesterday's successful fixture after today's save fails.
    save.unlink(missing_ok=True)
    code, writer = run(godot, data, 'write', [*FIXTURE, '--park-save-proof=write', f'--park-save={save}'])
    assert code == 0 and save.is_file(), writer[-6000:]
    before = snapshot(writer, 'before')
    code, reader = run(godot, data, 'read', [f'--park-load={save}', '--park-save-proof=read'])
    assert code == 0 and '[save-proof] PASS:' in reader, reader[-6000:]
    fresh, after = snapshot(reader, 'fresh'), snapshot(reader, 'after')
    # REJECTS a passing count-only comparison with default positions, staff classes or money.
    assert fresh != before and fresh['attractions'] == [] and fresh['staff'] == [] and fresh['guests'] == 0
    assert before == after, json.dumps({'before': before, 'after': after}, indent=2)
    assert len(before['attractions']) == 4 and len(before['staff']) == 5 and before['guests'] == 9
    assert before['bank'] == 314159 and before['totalDays'] == 836 and before['fee'] == 70 and before['open']
    assert before['queues'] and any(a['queue'] for a in before['attractions'])
    audit = dict(before=before, fresh=fresh, after=after, file_bytes=save.stat().st_size,
                 occupied_reload=True, wrong_map_control=True)
    if controls:
        # REJECTS a check that succeeds without loading, and a missing file reported as success.
        code, log = run(godot, data, 'no-load-control', ['--park-save-proof=read'])
        assert code != 0 and 'SAVE PROOF rejects:' in log, log[-2000:]
        code, log = run(godot, data, 'missing-control', [f'--park-load={WORK / "absent.save"}', '--park-save-proof=read'])
        assert code != 0 and '[park-save] FAILED:' in log, log[-2000:]
        audit['no_load_control'] = audit['missing_file_control'] = True
    return audit


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', default=os.environ.get('GODOT', 'godot'))
    parser.add_argument('--data', default=os.environ.get('TPW_DATA'))
    parser.add_argument('--no-build', action='store_true')
    args = parser.parse_args()
    if not args.data:
        parser.error('pass --data or set TPW_DATA to your disc image')
    if not args.no_build:
        subprocess.run(['dotnet', 'build', 'game/', '--nologo'], cwd=ROOT, check=True)
    audit = prove(args.godot, Path(args.data).resolve())
    (ROOT / 'findings/savehost-proof.json').write_text(json.dumps(audit, indent=2) + '\n')
    for side in ('fresh', 'before', 'after'):
        print(side + ': ' + json.dumps(audit[side], separators=(',', ':')))
    print('PASS: file round trip, fresh control, occupied reload, wrong map, no-load and missing-file controls')


if __name__ == '__main__':
    main()
