#!/usr/bin/env python3
"""In-memory mutations of the reachability audit; no simulation mutations.

Fixed tests, assertion failures only, unchanged-source controls before and after.
Keep rejected-run history. Survivors, invalid runs and skipped tests fail the run.
"""
import hashlib
import io
import json
from pathlib import Path
import types
import unittest

import test_prank_reachability as tests

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'tools/audit_prank_reachability.py'
MUTATIONS = [
    ('omit final tutorial table entry', 'for index in range(count):', 'for index in range(count - 1):'),
    ('lose jump delay literal', 'value = literal\n            elif delay != 0:', 'value = default\n            elif delay != 0:'),
    ('accept unknown switch arm', "raise ValueError('unknown switch arm instruction')", 'return dict(id=default, exit=submit, instructions=visited)'),
    ('omit final replay row', 'enumerate(captions):', 'enumerate(captions[:-1]):'),
    ('return last caption match', 'enumerate(captions):', 'reversed(list(enumerate(captions))):'),
    ('lose replay ordinal base', 'return start + index', 'return index'),
    ('invent result for missing caption', "raise ValueError('caption has no in-table termination witness')", 'return start'),
    ('include captionless cards', "creates_card=caption != sentinel", 'creates_card=True'),
    ('omit final schedule row', 'range(len(schedule) // 12):', 'range(len(schedule) // 12 - 1):'),
    ('mistake Action8 for Post', 'if op == 7:', 'if op == 8:'),
    ('lose post halfword mask', "id=args[0] & 0xffff", 'id=args[0]'),
    ('hide unreferenced code', 'unreferenced_code_words=len(words) - len(covered)', 'unreferenced_code_words=0'),
    ('omit backing counter store', 'if op == 6 and args[0] >= 51:', 'if False:'),
    ('allow write to scheduler cursor', 'off >= 184 for off in offsets', 'off > 184 for off in offsets'),
    ('omit cache index bound', 'not 0 <= args[0] < 72', 'False'),
    ('interpret permutation as unsigned', 'b - 256 if b & 128 else b', 'b'),
    ('lose staff halfword wrap', '((b - 256 if b & 128 else b) + off) & 0xffff', '((b - 256 if b & 128 else b) + off)'),
    ('omit staff offset alternatives', 'for b in permutation for off in offsets', 'for b in permutation for off in offsets[:1]'),
    ('omit balloon detection', 'PRANKS = {117, 118, 119}', 'PRANKS = {117, 118}'),
    ('accept truncated archive data', 'if off + size > len(archive):', 'if False:'),
    ('reverse overlay literal flag', 'if flags & 1 == 0:', 'if flags & 1 != 0:'),
    ('short overlay copy off by one', '256 - token, 2', '255 - token, 2'),
    ('extended overlay length off by one', 'byte() + 8 if nibble == 5', 'byte() + 7 if nibble == 5'),
    ('ordinary overlay length off by one', 'else nibble + 3', 'else nibble + 2'),
]


def run(source):
    module = types.ModuleType('mutated_prank_reachability')
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
    destination = ROOT / 'findings/prank-reachability-mutations.json'
    history = []
    if destination.exists():
        previous = json.loads(destination.read_text())
        history = previous.get('prior_rejected_runs', [])
        if previous['survivors'] or previous['invalid']:
            history.append(dict(source_sha256=previous['source_sha256'], tests_sha256=previous['tests_sha256'],
                                killed=previous['killed'], survivors=previous['survivors'], invalid=previous['invalid'],
                                rejected=[r for r in previous['mutations'] if r['status'] != 'killed']))
    original = SOURCE.read_bytes()
    source = original.decode()
    baseline = run(source)
    if baseline['status'] != 'passed' or baseline['skipped'] or baseline['executed'] != 18:
        raise SystemExit('Unchanged-source control failed: ' + str(baseline))
    rows = []
    for name, old, new in MUTATIONS:
        if source.count(old) != 1:
            raise SystemExit('Mutation anchor not unique: ' + name)
        row = dict(name=name, old=old, new=new, **run(source.replace(old, new)))
        rows.append(row)
        print(name, row['status'], 'tests:', row.get('executed'), 'skipped:', row.get('skipped'))
    final = run(SOURCE.read_text())
    result = dict(scope='SEARCH/DOMAIN AUDIT ONLY; no prank simulation port.', prior_rejected_runs=history,
                  source_sha256=hashlib.sha256(original).hexdigest(),
                  tests_sha256=hashlib.sha256((ROOT / 'tools/test_prank_reachability.py').read_bytes()).hexdigest(),
                  unchanged_control=baseline, mutations=rows, final_unchanged_control=final,
                  source_unchanged=SOURCE.read_bytes() == original,
                  killed=sum(r['status'] == 'killed' for r in rows),
                  survivors=sum(r['status'] == 'passed' for r in rows),
                  invalid=sum(r['status'] == 'invalid' for r in rows))
    destination.write_text(json.dumps(result, indent=2) + '\n')
    if (not result['source_unchanged'] or final['status'] != 'passed' or final['skipped']
            or final['executed'] != baseline['executed'] or any(
                r['status'] != 'killed' or r.get('executed') != baseline['executed'] or r.get('skipped') for r in rows)):
        raise SystemExit('Survivor or invalid run: inspect findings/prank-reachability-mutations.json')


if __name__ == '__main__':
    main()
