#!/usr/bin/env python3
"""Mutate production one rule at a time against fixed baseline-compiled tests.

Restores source on failure/interruption. Compile errors and missing tests are INVALID, not kills.
JSON includes passing controls, failing test names, hashes, and the restored full-suite result.
--resume requires identical production, retains prior kills and records prior non-kills/test hashes.
"""
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'core/TPW.Sim/InfluenceMap.cs'
TESTS = ROOT / 'tests/TPW.Sim.Tests/InfluenceMapTests.cs'
OUTPUT = ROOT / 'findings/influence-mutations.json'
ORIGINAL = SOURCE.read_bytes()
FILTER = 'FullyQualifiedName~InfluenceMapTests'
mutations = []
def change(name, old, new):
    if ORIGINAL.decode().count(old) != 1:
        raise ValueError('Missing/nonunique anchor: ' + name)
    mutations.append(dict(name=name, old=old, new=new))

for name, old, new in [
    ('capacity 19', 'Capacity = 20;', 'Capacity = 19;'),
    ('capacity 21', 'Capacity = 20;', 'Capacity = 21;'),
    ('producer radius zero', 'ProducerRadius = 1;', 'ProducerRadius = 0;'),
    ('producer radius two', 'ProducerRadius = 1;', 'ProducerRadius = 2;'),
    ('x coordinate discarded', 'X = unchecked((short)x);', 'X = 0;'),
    ('y coordinate discarded', 'Y = unchecked((short)y);', 'Y = 0;'),
    ('radius not squared', '(uint)(radius * radius)', '(uint)radius'),
    ('radius doubles', '(uint)(radius * radius)', '(uint)(radius * 2)'),
    ('flag write ORs', '=> Flags = flags;', '=> Flags |= flags;'),
    ('x delta widened', 'unchecked((short)(X - x))', '(X - x)'),
    ('y delta widened', 'unchecked((short)(Y - y))', '(Y - y)'),
    ('Manhattan circle', 'dx * dx + dy * dy', 'Math.Abs(dx) + Math.Abs(dy)'),
    ('Chebyshev circle', 'dx * dx + dy * dy', 'Math.Max(dx * dx, dy * dy)'),
    ('distance loses x', 'dx * dx + dy * dy', 'dy * dy'),
    ('distance loses y', 'dx * dx + dy * dy', 'dx * dx'),
    ('distance subtracts y', 'dx * dx + dy * dy', 'dx * dx - dy * dy'),
    ('exclusive circle', 'distanceSquared <= RadiusSquared', 'distanceSquared < RadiusSquared'),
    ('signed squared comparison', 'distanceSquared <= RadiusSquared', '(int)distanceSquared <= (int)RadiusSquared'),
    ('allocation always fails', 'if (free.Count == 0) return null;\n            var area', 'if (free.Count >= 0) return null;\n            var area'),
    ('allocation does not consume slot', 'var area = free.Pop();', 'var area = free.Peek();'),
    ('allocation appends', 'live.Insert(0, area);', 'live.Add(area);'),
    ('allocation omits geometry', 'area.PlaceTiles(x, y, radius);', '// geometry omitted'),
    ('allocation omits flags', 'area.SetFlags(flags);', '// flags omitted'),
    ('entertainer reads position when full', 'if (free.Count == 0) return null;\n            var position', 'if (false) return null;\n            var position'),
    ('entertainer swaps axes', 'TryCreateTiles(ToTile(position.X), ToTile(position.Y), ProducerRadius,', 'TryCreateTiles(ToTile(position.Y), ToTile(position.X), ProducerRadius,'),
    ('entertainer wrong bit', 'TileInfluence.Entertainer);', 'TileInfluence.Pleasant);'),
    ('release leaves area live', 'if (!live.Remove(area))', 'if (!live.Contains(area))'),
    ('release does not return slot', 'free.Push(area);', '// free return omitted'),
    ('release clears flags', 'free.Push(area);', 'area.SetFlags(TileInfluence.None); free.Push(area);'),
    ('release clears geometry', 'free.Push(area);', 'area.PlaceTiles(0, 0, 0); free.Push(area);'),
    ('release clears other owners', 'free.Push(area);', 'live.Clear(); free.Push(area);'),
    ('OR becomes assignment', 'result |= area.Flags;', 'result = area.Flags;'),
    ('OR becomes XOR', 'result |= area.Flags;', 'result ^= area.Flags;'),
    ('OR becomes sum', 'result |= area.Flags;', 'result = (TileInfluence)((int)result + (int)area.Flags);'),
    ('all areas cover', 'if (area.Covers(x, y))', 'if (true)'),
    ('no areas cover', 'if (area.Covers(x, y))', 'if (false)'),
    ('tile shift seven', 'unchecked((short)coordinate) >> 8', 'unchecked((short)coordinate) >> 7'),
    ('coordinates unsigned', 'unchecked((short)coordinate) >> 8', 'unchecked((ushort)coordinate) >> 8'),
    ('coordinates not narrowed', 'unchecked((short)coordinate) >> 8', 'coordinate >> 8'),
    ('negative coordinates truncate', 'unchecked((short)coordinate) >> 8', 'unchecked((short)coordinate) / 256'),
    ('query swaps axes', 'AtTiles(ToTile(x), ToTile(y));', 'AtTiles(ToTile(y), ToTile(x));'),
    ('particle wrong bit', 'ProducerRadius, TileInfluence.Unpleasant));', 'ProducerRadius, TileInfluence.Entertainer));'),
    ('particle swaps axes', 'TryCreateTiles(ToTile(x), ToTile(y), ProducerRadius,', 'TryCreateTiles(ToTile(y), ToTile(x), ProducerRadius,'),
    ('lifetime 359', 'InitialLifetime = 360;', 'InitialLifetime = 359;'),
    ('lifetime 361', 'InitialLifetime = 360;', 'InitialLifetime = 361;'),
    ('lifetime wrong shift', 'world.TimeStep >> 12', 'world.TimeStep >> 11'),
    ('lifetime increases', 'Remaining - (world.TimeStep >> 12)', 'Remaining + (world.TimeStep >> 12)'),
    ('lifetime frozen', 'Remaining - (world.TimeStep >> 12)', 'Remaining'),
    ('lifetime saturates instead of wrapping', 'unchecked((short)(Remaining - (world.TimeStep >> 12)))', '(short)Math.Clamp((long)Remaining - (world.TimeStep >> 12), short.MinValue, short.MaxValue)'),
    ('expiry at zero', 'if (Remaining < 0) Destroy();', 'if (Remaining <= 0) Destroy();'),
    ('expired emitter still updates', 'if (Destroyed) return;\n            // READ:', '// guard omitted\n            // READ:'),
    ('destroyed flag omitted', 'Destroyed = true;', '// flag omitted'),
    ('destroy retains owner handle', 'Area = null;', '// handle retained'),
    ('destroy omits release', 'map.Release(area);', '// release omitted'),
]: change(name, old, new)

sha = lambda data: hashlib.sha256(data).hexdigest()
audit = dict(source=str(SOURCE.relative_to(ROOT)), source_sha256=sha(ORIGINAL),
             tests_sha256=sha(TESTS.read_bytes()), filter=FILTER, mutations=[], restored=False)
todo = mutations
if '--resume' in sys.argv:
    prior = json.loads(OUTPUT.read_text())
    if not prior['restored'] or prior['source_sha256'] != audit['source_sha256']:
        raise SystemExit('Resume requires restored, byte-identical source.')
    audit['earlier_sweeps'] = prior.get('earlier_sweeps', []) + [
        {key: prior[key] for key in ('baseline', 'tests_sha256', 'summary')}]
    audit['prior_non_kills'] = prior.get('prior_non_kills', []) + [
        m for m in prior['mutations'] if m['status'] != 'killed']
    audit['mutations'] = [m for m in prior['mutations'] if m['status'] == 'killed']
    done = {m['name'] for m in audit['mutations']}
    todo = [m for m in mutations if m['name'] not in done]

def run(folder, full=False):
    trx = folder / 'result.trx'
    trx.unlink(missing_ok=True)
    start = time.monotonic()
    if full:
        command = ['dotnet', 'test', 'tests/TPW.Sim.Tests/', '--no-restore', '--nologo',
                   '--logger', 'trx;LogFileName=result.trx', '--results-directory', str(folder)]
    else:
        build = subprocess.run(['dotnet', 'build', 'core/TPW.Sim/', '--no-restore', '--nologo'],
                               cwd=ROOT, capture_output=True, text=True, timeout=180)
        if build.returncode:
            return dict(exit_code=build.returncode, compile_error=True, failed_tests=[], counters={},
                        output=(build.stdout + build.stderr)[-4000:])
        shutil.copy2(ROOT / 'core/TPW.Sim/bin/Debug/net8.0/TPW.Sim.dll',
                     ROOT / 'tests/TPW.Sim.Tests/bin/Debug/net8.0/TPW.Sim.dll')
        command = ['dotnet', 'vstest', 'tests/TPW.Sim.Tests/bin/Debug/net8.0/TPW.Sim.Tests.dll',
                   '--TestCaseFilter:' + FILTER, '--logger:trx;LogFileName=result.trx',
                   '--ResultsDirectory:' + str(folder)]
    result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=180)
    out = dict(exit_code=result.returncode, seconds=round(time.monotonic() - start, 2),
               compile_error='error CS' in result.stdout, failed_tests=[], counters={})
    if trx.exists():
        ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
        tree = ET.parse(trx)
        out['failed_tests'] = [e.get('testName') for e in tree.findall('.//t:UnitTestResult', ns)
                               if e.get('outcome') == 'Failed']
        count = tree.find('.//t:Counters', ns)
        if count is not None:
            out['counters'] = {k: int(v) for k, v in count.attrib.items()}
    if not out['counters']:
        out['output'] = (result.stdout + result.stderr)[-4000:]
    return out

try:
    with tempfile.TemporaryDirectory(prefix='influence-mutations-', dir=ROOT / 'tools') as directory:
        folder = Path(directory)
        audit['baseline'] = run(folder, full=True)
        if audit['baseline']['exit_code'] or not audit['baseline']['counters'].get('passed'):
            raise RuntimeError('Full baseline did not pass with real tests.')
        audit['unchanged_control'] = run(folder)
        expected = audit['unchanged_control']['counters'].get('executed', 0)
        if audit['unchanged_control']['exit_code'] or not expected:
            raise RuntimeError('Unchanged production control did not pass with real tests.')
        print(f'Controls passed: full={audit["baseline"]["counters"]["passed"]}, targeted={expected}', flush=True)
        for index, mutation in enumerate(todo, 1):
            SOURCE.write_text(ORIGINAL.decode().replace(mutation['old'], mutation['new'], 1))
            result = run(folder)
            SOURCE.write_bytes(ORIGINAL)
            valid = not result['compile_error'] and result['counters'].get('executed') == expected
            result['status'] = ('killed' if valid and result['failed_tests'] and result['exit_code'] != 0
                                else 'survived' if valid and result['exit_code'] == 0 else 'invalid')
            result.update(mutation)
            audit['mutations'].append(result)
            OUTPUT.write_text(json.dumps(audit, indent=2) + '\n')
            print(f'{index}/{len(todo)} {result["status"]}: {mutation["name"]}', flush=True)
finally:
    SOURCE.write_bytes(ORIGINAL)
    audit['restored'] = SOURCE.read_bytes() == ORIGINAL
    audit['summary'] = {status: sum(m['status'] == status for m in audit['mutations'])
                        for status in ('killed', 'survived', 'invalid')}
    audit['summary']['total'] = len(audit['mutations'])
    OUTPUT.write_text(json.dumps(audit, indent=2) + '\n')

with tempfile.TemporaryDirectory(prefix='influence-final-', dir=ROOT / 'tools') as directory:
    audit['final_full_suite'] = run(Path(directory), full=True)
OUTPUT.write_text(json.dumps(audit, indent=2) + '\n')
if len(audit['mutations']) != len(mutations) or audit['summary']['survived'] or audit['summary']['invalid'] or audit['final_full_suite']['exit_code']:
    raise SystemExit('Mutation audit is not green; inspect JSON.')
print(json.dumps(audit['summary']), flush=True)
