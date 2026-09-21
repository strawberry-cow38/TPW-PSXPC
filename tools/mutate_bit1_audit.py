#!/usr/bin/env python3
"""Fixed tests, in-memory audit mutations, assertion failures only. No binary edits."""
import hashlib
import io
import json
from pathlib import Path
import types
import unittest

import test_bit1_audit as tests

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'tools/audit_bit1.py'
MUTATIONS = [
    ('drop final scan word', 'range(0, len(data) - 3, 4):', 'range(0, len(data) - 7, 4):'),
    ('JAL only', 'if op in (2, 3):', 'if op == 3:'),
    ('omit conditional branch ingress', 'if op in (4, 5, 6, 7) or (op == 1 and rt in (0, 1, 16, 17)):', 'if False:'),
    ('wrong J region at boundary', '((pc + 4) & 0xF0000000)', '(pc & 0xF0000000)'),
    ('omit literal pointers', 'if ins in targets:', 'if False:'),
    ('hide trailing bytes', 'trailing_bytes=len(data) % 4', 'trailing_bytes=0'),
    ('restore stack/GP store exclusion', 'if op in STORES:\n            op_counts', 'if op in STORES and rs not in (28, 29):\n            op_counts'),
    ('drop byte-store opcode', "40: 'sb', 41: 'sh'", "41: 'sh'"),
    ('drop partial-store opcode', "42: 'swl', 43: 'sw'", "43: 'sw'"),
    ('drop coprocessor store opcode', "58: 'swc2', 59: 'swc3'", "59: 'swc3'"),
    ('miss upper-byte field stores', 'if any(20 <= b < 24 for b in touched):', 'if 20 in touched:'),
    ('SWL lower extent wrong', 'range(offset & ~3, offset + 1)', 'range(offset, offset + 1)'),
    ('SWR upper extent wrong', 'range(offset, (offset & ~3) + 4)', 'range(offset, offset + 4)'),
    ('halfword width one', 'range(offset, offset + 2)', 'range(offset, offset + 1)'),
    ('unsigned instruction immediate', 'return imm - 65536 if imm & 0x8000 else imm', 'return imm'),
    ('drop GP roots', 'if rs == 28 and GP + imm == POOL', 'if rs == 4 and GP + imm == POOL'),
    ('omit final setter clone', 'off + 8 <= len(data)', 'off + 8 < len(data)'),
    ('omit materialized references', 'if value in wanted:', 'if False:'),
    ('retain constants across calls', 'reset_at = pc + 8', 'reset_at = pc + len(data) + 8'),
    ('lose ADDIU alias displacement', 'regs[rt] = add(regs[rs], imm)', 'regs[rt] = regs[rs]'),
    ('lose copied pointer aliases', 'regs[rd] = regs[rs]', 'regs[rd] = None'),
    ('ignore leaf flag overlap', "flag_overlap=any(20 <= b < 24 for b in touched)", 'flag_overlap=False'),
    ('accept empty leaf', "raise ValueError('empty leaf')", 'return []'),
    ('ignore unexpected ingress', "unexpected=[r for r in external if expected.get(r['site']) != r['target']]", 'unexpected=[]'),
    ('ignore missing ingress', "missing=[dict(site=p, target=t) for p, t in expected.items() if found.get(p) != t]", 'missing=[]'),
    ('allow empty ingress domain', "raise ValueError('empty ingress domain')", 'return dict(external=[], internal_count=0, unexpected=[], missing=[])'),
    ('bit 2 instead of bit 1', 'bit1=bool(flags & 1)', 'bit1=bool(flags & 2)'),
    ('omit owner upper bytes', 'offset+i for offset in offsets for i in range(4)', 'offset+i for offset in offsets for i in range(1)'),
    ('hide owner stack exclusions', 'stack_accesses_excluded=stack', 'stack_accesses_excluded=[]'),
    ('omit final overlay', 'enumerate(directory):', 'enumerate(directory[:-1]):'),
]


def run(source):
    module = types.ModuleType('mutated_bit1_audit')
    module.__file__ = str(SOURCE)
    try:
        exec(compile(source, str(SOURCE), 'exec'), module.__dict__)
    except Exception as exc:
        return dict(status='invalid', error=repr(exc))
    tests.audit = module
    output = io.StringIO()
    result = unittest.TextTestRunner(stream=output).run(unittest.defaultTestLoader.loadTestsFromModule(tests))
    return dict(status='invalid' if result.errors else 'killed' if result.failures else 'passed',
                executed=result.testsRun, failures=len(result.failures), errors=len(result.errors),
                skipped=len(result.skipped), failing_tests=[t.id() for t, _ in result.failures],
                output=output.getvalue())


def main():
    destination = ROOT / 'findings/influence-bit1-mutations.json'
    history = []
    if destination.exists():
        previous = json.loads(destination.read_text())
        history = previous.get('prior_rejected_runs', [])
        if previous['survivors'] or previous['invalid']:
            history.append({key: previous[key] for key in ('source_sha256', 'tests_sha256', 'killed', 'survivors', 'invalid', 'mutations')})
    original = SOURCE.read_bytes()
    source = original.decode()
    baseline = run(source)
    if baseline['status'] != 'passed' or baseline['executed'] != 23 or baseline['skipped']:
        raise SystemExit('Unchanged-source control failed: '+str(baseline))
    rows = []
    for name, old, new in MUTATIONS:
        if source.count(old) != 1:
            raise SystemExit('Nonunique mutation anchor: '+name)
        row = dict(name=name, old=old, new=new, **run(source.replace(old, new)))
        rows.append(row)
        print(name, row['status'], 'tests:', row.get('executed'), 'skipped:', row.get('skipped'), flush=True)
    final = run(SOURCE.read_text())
    result = dict(scope='Audit-tool mutations only; no simulation behavior added.', prior_rejected_runs=history,
                  source_sha256=hashlib.sha256(original).hexdigest(),
                  tests_sha256=hashlib.sha256((ROOT/'tools/test_bit1_audit.py').read_bytes()).hexdigest(),
                  unchanged_control=baseline, mutations=rows, final_unchanged_control=final,
                  source_unchanged=SOURCE.read_bytes() == original,
                  killed=sum(r['status'] == 'killed' for r in rows),
                  survivors=sum(r['status'] == 'passed' for r in rows),
                  invalid=sum(r['status'] == 'invalid' for r in rows))
    destination.write_text(json.dumps(result, indent=2)+'\n')
    if (not result['source_unchanged'] or final['status'] != 'passed' or final['skipped'] or final['executed'] != 23
            or any(r['status'] != 'killed' or r.get('executed') != 23 or r.get('skipped') for r in rows)):
        raise SystemExit('Survivor/invalid run: inspect influence-bit1-mutations.json')


if __name__ == '__main__':
    main()
