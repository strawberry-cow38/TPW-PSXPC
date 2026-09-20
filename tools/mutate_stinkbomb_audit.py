#!/usr/bin/env python3
"""Mutate the audit tool in memory against fixed tests; no simulation was implemented.

An unchanged-source control must pass. Each mutant must produce an assertion failure,
not a compile/import/test error. No input or production source file is overwritten.
"""
import hashlib
import io
import json
from pathlib import Path
import types
import unittest
import test_stinkbomb_audit as tests

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'tools/audit_stinkbombs.py'
MUTATIONS = [
    ('omit tail word', 'len(data) - 3, 4', 'len(data) - 4, 4'),
    ('ignore tail jumps', 'w >> 26 in (2, 3)', 'w >> 26 == 3'),
    ('lose high target bits', '(pc & 0xF0000000)', '(pc & 0)'),
    ('ignore callback pointers', 'if w == target', 'if w == target + 4'),
    ('miss ORI id loads', 'w >> 26 in (9, 13)', 'w >> 26 == 9'),
    ('accept nonliteral id arithmetic', 'and (w >> 21) & 31 == 0 and', 'and'),
    ('restore bad size guard', 'if off + 8 > len(data):', 'if off + 0x40 > len(data):'),
    ('exclude type 8', '1 <= kind <= 8', '1 <= kind < 8'),
    ('misread name offset', 'text_id=word(data, off + 4)', 'text_id=word(data, off)'),
    ('case-sensitive names', 'delinquen|litter|vomit|fart\', re.I)', 'delinquen|litter|vomit|fart\')'),
    ('omit last text entry', 'for i in range(count):', 'for i in range(count - 1):'),
    ('count Action8 as a post', 'if op == 7:', 'if op == 8:'),
]


def run(source):
    module = types.ModuleType('mutated_audit')
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
                skipped=len(result.skipped),
                failing_tests=[test.id() for test, _ in result.failures], output=stream.getvalue())


def main():
    original = SOURCE.read_bytes()
    source = original.decode()
    baseline = run(source)
    if baseline['status'] != 'passed' or baseline['executed'] != 8 or baseline['skipped']:
        raise SystemExit('Unchanged-source control failed.')
    rows = []
    for name, old, new in MUTATIONS:
        if source.count(old) != 1:
            raise SystemExit(f'Mutation anchor is not unique: {name}')
        row = dict(name=name, old=old, new=new, **run(source.replace(old, new)))
        rows.append(row)
        print(name, row['status'], 'executed', row.get('executed'), 'skipped', row.get('skipped'))
    restored = run(SOURCE.read_text())
    identical = SOURCE.read_bytes() == original
    result = dict(scope='Audit-tool mutations ONLY; no stinkbomb simulation established or implemented.',
                  source_sha256=hashlib.sha256(original).hexdigest(),
                  test_sha256=hashlib.sha256((ROOT / 'tools/test_stinkbomb_audit.py').read_bytes()).hexdigest(),
                  unchanged_control=baseline, mutations=rows, final_unchanged_control=restored,
                  source_unchanged=identical,
                  killed=sum(r['status'] == 'killed' for r in rows),
                  survivors=sum(r['status'] == 'passed' for r in rows),
                  invalid=sum(r['status'] == 'invalid' for r in rows))
    (ROOT / 'findings/stinkbombs-mutations.json').write_text(json.dumps(result, indent=2) + '\n')
    if not identical or restored['status'] != 'passed' or any(
            r['status'] != 'killed' or r['executed'] != baseline['executed'] or r['skipped'] for r in rows):
        raise SystemExit('Mutation survived or invalid audit run; inspect JSON.')


if __name__ == '__main__':
    main()
