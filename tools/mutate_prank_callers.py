#!/usr/bin/env python3
"""In-memory mutations of the queue SEARCH TOOL. No game/simulation mutations.

Fixed tests, unchanged-source controls before/after, assertion kills only.
Survivors, invalid runs, missing tests and skips all fail the sweep.
"""
import hashlib
import io
import json
from pathlib import Path
import types
import unittest

import test_prank_callers as tests

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'tools/audit_prank_callers.py'
MUTATIONS = [
    ('omit final aligned word', 'len(data) - 3, 4', 'len(data) - 4, 4'),
    ('omit tail jumps', 'word >> 26 in (2, 3)', 'word >> 26 == 3'),
    ('omit calls', 'word >> 26 in (2, 3)', 'word >> 26 == 2'),
    ('wrong jump region at boundary', '(pc + 4) & 0xF0000000', 'pc & 0xF0000000'),
    ('accept other call targets', 'if target in targets:', 'if True:'),
    ('omit callback pointers', 'if word in targets:', 'if False:'),
    ('hide trailing bytes', 'trailing_bytes_not_decoded=len(data) % 4', 'trailing_bytes_not_decoded=0'),
    ('omit ORI literal', 'word >> 26 in (9, 13)', 'word >> 26 == 9'),
    ('accept arithmetic as literal', '(word >> 21) & 31 == 0', 'True'),
    ('accept wrong argument', '(word >> 16) & 31 == 5', 'True'),
    ('omit branch alternatives', 'for address in fixed[site]:', 'for address in fixed[site][:1]:'),
    ('hide unreviewed callers', "unreviewed=sum(r['kind'] == 'UNREVIEWED' for r in rows)", 'unreviewed=0'),
    ('hide stale manifest entries', 'sorted((set(fixed) | set(dynamic)) - set(sites))', '[]'),
    ('invent zero for unknown id', 'return None\n', 'return 0\n'),
    ('omit balloon id', 'PRANK_IDS = (117, 118, 119)', 'PRANK_IDS = (117, 118)'),
    ('hide omitted jump alternatives', "value is not None and source not in fixed[edge['target']]", 'False'),
]


def run(source):
    module = types.ModuleType('mutated_prank_audit')
    module.__file__ = str(SOURCE)
    try:
        exec(compile(source, str(SOURCE), 'exec'), module.__dict__)
    except Exception as exc:
        return dict(status='invalid', error=repr(exc))
    tests.audit = module
    stream = io.StringIO()
    result = unittest.TextTestRunner(stream=stream).run(unittest.defaultTestLoader.loadTestsFromModule(tests))
    return dict(status='invalid' if result.errors else 'killed' if result.failures else 'passed',
                executed=result.testsRun, failed=len(result.failures), errors=len(result.errors),
                skipped=len(result.skipped), failing_tests=[t.id() for t, _ in result.failures],
                output=stream.getvalue())


def main():
    output_path = ROOT / 'findings/pranks-mutations.json'
    history = []
    if output_path.exists():
        previous = json.loads(output_path.read_text())
        history = previous.get('prior_rejected_runs', [])
        if previous['survivors'] or previous['invalid']:
            history.append(dict(source_sha256=previous['source_sha256'], tests_sha256=previous['tests_sha256'],
                                killed=previous['killed'], survivors=previous['survivors'], invalid=previous['invalid'],
                                rejected=[r for r in previous['mutations'] if r['status'] != 'killed']))
    original = SOURCE.read_bytes()
    source = original.decode()
    baseline = run(source)
    if baseline['status'] != 'passed' or baseline['skipped'] or baseline['executed'] != 10:
        raise SystemExit('Unchanged-source control failed: ' + str(baseline))
    rows = []
    for name, old, new in MUTATIONS:
        if source.count(old) != 1:
            raise SystemExit('Mutation anchor not unique: ' + name)
        row = dict(name=name, old=old, new=new, **run(source.replace(old, new)))
        rows.append(row)
        print(name, row['status'], 'tests:', row.get('executed'), 'skipped:', row.get('skipped'))
    final = run(SOURCE.read_text())
    result = dict(scope='SEARCH TOOL ONLY; no prank simulation established or ported.',
                  prior_rejected_runs=history,
                  source_sha256=hashlib.sha256(original).hexdigest(),
                  tests_sha256=hashlib.sha256((ROOT / 'tools/test_prank_callers.py').read_bytes()).hexdigest(),
                  unchanged_control=baseline, mutations=rows, final_unchanged_control=final,
                  source_unchanged=SOURCE.read_bytes() == original,
                  killed=sum(r['status'] == 'killed' for r in rows),
                  survivors=sum(r['status'] == 'passed' for r in rows),
                  invalid=sum(r['status'] == 'invalid' for r in rows))
    output_path.write_text(json.dumps(result, indent=2) + '\n')
    if (not result['source_unchanged'] or final['status'] != 'passed' or final['skipped']
            or final['executed'] != baseline['executed'] or any(
                r['status'] != 'killed' or r.get('executed') != baseline['executed'] or r.get('skipped') for r in rows)):
        raise SystemExit('Survivor or invalid run: inspect findings/pranks-mutations.json')


if __name__ == '__main__':
    main()
