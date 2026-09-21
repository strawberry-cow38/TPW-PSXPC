#!/usr/bin/env python3
"""Mutate one selection rule at a time with a frozen test assembly/fixture.

Nonempty baseline and identical executed count required; skips, compilation errors,
missing results and changed tests are INVALID, not kills. Restore source on every
exit including SIGINT/SIGTERM. No parallel builds or changes outside this worktree.
"""
import hashlib
import json
from pathlib import Path
import shutil
import signal
import subprocess
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
FILES = {n: ROOT / 'core/TPW.Sim' / n for n in ('ParkSelection.cs', 'ParkSelectionSave.cs')}
ORIGINAL = {n: p.read_bytes() for n, p in FILES.items()}
WORK = ROOT / 'tests/TPW.Sim.Tests/obj/selection-mutations'
OUT = ROOT / 'findings/selection-mutations.json'
FILTER = 'FullyQualifiedName~ParkSelectionTests|FullyQualifiedName~ParkSelectionSaveTests'
TEST_DLL = ROOT / 'tests/TPW.Sim.Tests/bin/Debug/net8.0/TPW.Sim.Tests.dll'
FIXTURE = ROOT / 'findings/selection-audit.json'
MUTATIONS = []


def sha(data):
    return hashlib.sha256(data).hexdigest()


def add(name, old, new, file='ParkSelection.cs'):
    assert ORIGINAL[file].decode().count(old) == 1, (name, 'missing/nonunique anchor')
    MUTATIONS.append(dict(name=name, file=file, old=old, new=new))


for name, old, new in [
    ('node count includes links', 'NodeCount = 8;', 'NodeCount = 19;'),
    ('last link omitted', 'LinkCount = 11;', 'LinkCount = 10;'),
    ('duplicate world park allowed', 'n.Select(x => x.Location).Distinct().Count() != NodeCount', 'false'),
    ('world limit relaxed', 'x.Location.World >= 4', 'x.Location.World > 4'),
    ('link endpoint limit relaxed', 'x.B >= NodeCount', 'x.B > NodeCount'),
    ('node upper bound relaxed', '(uint)node >= NodeCount', '(uint)node > NodeCount'),
    ('table skips final edge', 'foreach (var link in Links)', 'foreach (var link in Links.Take(LinkCount - 1))'),
    ('forward direction omitted', '(link.A == from && link.B == to) || (link.B == from && link.A == to)', '(link.B == from && link.A == to)'),
    ('reverse direction omitted', '(link.A == from && link.B == to) || (link.B == from && link.A == to)', '(link.A == from && link.B == to)'),
    ('endpoint conjunction lost', 'link.A == from && link.B == to', 'link.A == from || link.B == to'),
    ('ticket cost hardcoded one', 'return link.TicketCost;', 'return 1;'),
    ('ticket cost hardcoded two', 'return link.TicketCost;', 'return 2;'),
    ('missing edge free', 'return null;', 'return 0;'),
    ('status open value', 'Open = 0, Closed = 1', 'Open = 3, Closed = 1'),
    ('status closed value', 'Closed = 1, Unopened = 2', 'Closed = 3, Unopened = 2'),
    ('status unopened value', 'Unopened = 2 }', 'Unopened = 3 }'),
    ('cap two', 'MaximumOpenParks = 3;', 'MaximumOpenParks = 2;'),
    ('cap four', 'MaximumOpenParks = 3;', 'MaximumOpenParks = 4;'),
    ('wrong selected pair', 'Table.Nodes[SelectedNode].Location;', 'Table.Nodes[0].Location;'),
    ('closed counted as open', 'statuses.Count(s => s == ParkSelectionStatus.Open)', 'statuses.Count(s => s != ParkSelectionStatus.Unopened)'),
    ('initialize closed', 'Array.Fill(statuses, ParkSelectionStatus.Unopened);', 'Array.Fill(statuses, ParkSelectionStatus.Closed);'),
    ('initial park not open', 'statuses[0] = ParkSelectionStatus.Open;', 'statuses[0] = ParkSelectionStatus.Closed;'),
    ('initial park one', 'statuses[0] = ParkSelectionStatus.Open;', 'statuses[1] = ParkSelectionStatus.Open;'),
    ('snapshot alias', '(ParkSelectionStatus[])statuses.Clone()', 'statuses'),
    ('restore omits statuses', 'saved.CopyTo(statuses); SelectedNode = selectedNode;', 'SelectedNode = selectedNode;'),
    ('restore omits cursor', 'saved.CopyTo(statuses); SelectedNode = selectedNode;', 'saved.CopyTo(statuses);'),
    ('restore accepts invalid status', 'if (status > ParkSelectionStatus.Unopened)', 'if (false)'),
    ('closed needs purchase', 'statuses[target] != ParkSelectionStatus.Unopened', 'statuses[target] == ParkSelectionStatus.Open'),
    ('affordability strict', 'host.SpendableTickets < cost.Value', 'host.SpendableTickets <= cost.Value'),
    ('affordability ignored', 'host.SpendableTickets < cost.Value', 'false'),
    ('affordability uses fixed lifetime50', 'host.SpendableTickets < cost.Value', '50 < cost.Value'),
    ('existing selection cursor lost', 'SelectedNode = target; return result;', 'return result;'),
    ('confirmation ignored', ' || !confirmUnlock', ''),
    ('unlock cursor lost', 'SelectedNode = target;\n        SetClosed(target);', 'SetClosed(target);'),
    ('unlock closes origin', 'SelectedNode = target;\n        SetClosed(target);', 'SetClosed(SelectedNode);\n        SelectedNode = target;'),
    ('unlock opens immediately', 'SetClosed(target);\n        host.SpendTickets(cost);', 'SetClosed(target);\n        statuses[target] = ParkSelectionStatus.Open;\n        host.SpendTickets(cost);'),
    ('unlock no spending', 'host.SpendTickets(cost);', '// omitted spend'),
    ('unlock wrong cost', 'host.SpendTickets(cost);', 'host.SpendTickets(0);'),
    ('unlock spends twice', 'host.SpendTickets(cost);', 'host.SpendTickets(cost); host.SpendTickets(cost);'),
    ('spend before closure', 'SetClosed(target);\n        host.SpendTickets(cost);', 'host.SpendTickets(cost);\n        SetClosed(target);'),
    ('cap omitted', ' || OpenCount == MaximumOpenParks', ''),
    ('cap equality normalized', 'OpenCount == MaximumOpenParks', 'OpenCount >= MaximumOpenParks'),
    ('unopened can open', 'statuses[SelectedNode] != ParkSelectionStatus.Closed', 'statuses[SelectedNode] == ParkSelectionStatus.Open'),
    ('opening write omitted', 'statuses[SelectedNode] = ParkSelectionStatus.Open;', '// omitted open'),
    ('close wrong guard', 'statuses[SelectedNode] != ParkSelectionStatus.Open', 'statuses[SelectedNode] == ParkSelectionStatus.Open'),
    ('close status lost', 'statuses[node] = ParkSelectionStatus.Closed;', '// omitted close'),
    ('close relocks park', 'statuses[node] = ParkSelectionStatus.Closed;', 'statuses[node] = ParkSelectionStatus.Unopened;'),
    ('close keeps buffer', 'host.DiscardParkBuffer(Table.Nodes[node].Location);', '// omitted discard'),
    ('sandbox override lost', 'restrictedMode ? new(0, 0) : SelectedPark', 'SelectedPark'),
    ('campaign pair lost', 'restrictedMode ? new(0, 0) : SelectedPark', 'new(0, 0)'),
]:
    add(name, old, new)

for name, old, new in [
    ('capture skips last node', 'node < ParkSelectionTable.NodeCount', 'node < ParkSelectionTable.NodeCount - 1'),
    ('capture uses node slot', 'int slot = park.World * 2 + park.Park; //', 'int slot = node; //'),
    ('capture drops status', 'archive.SetParkStatus(slot, (byte)status);', '// omitted status'),
    ('capture keeps closed packets', 'if (status != ParkSelectionStatus.Open)', 'if (false)'),
    ('capture drops open packets', 'if (status != ParkSelectionStatus.Open)', 'if (status == ParkSelectionStatus.Open)'),
    ('restore skips last node', 'node < statuses.Length', 'node < statuses.Length - 1'),
    ('restore uses node slot', 'int slot = park.World * 2 + park.Park;\n', 'int slot = node;\n'),
    ('restore fails to mask', 'archive.GetParkStatus(slot) & 0x0F', 'archive.GetParkStatus(slot)'),
    ('restore masks wrong nibble', 'archive.GetParkStatus(slot) & 0x0F', '(archive.GetParkStatus(slot) >> 4) & 0x0F'),
    ('restore open is closed', 'low == 0 ? ParkSelectionStatus.Open', 'low == 0 ? ParkSelectionStatus.Closed'),
    ('restore closed is unopened', 'low == 1 ? ParkSelectionStatus.Closed', 'low == 1 ? ParkSelectionStatus.Unopened'),
    ('restore invalid status is closed', ': ParkSelectionStatus.Unopened;', ': ParkSelectionStatus.Closed;'),
    ('restore cursor hardcoded', 'selection.Restore(statuses, selectedNode);', 'selection.Restore(statuses, 0);'),
    ('capture corrupts awards', 'archive.SetParkStatus(slot, (byte)status);', 'archive.SetParkStatus(slot, (byte)status); archive.Header[60 + slot * 4] = 0;'),
    ('capture corrupts tickets', 'archive.SetParkStatus(slot, (byte)status);', 'archive.SetParkStatus(slot, (byte)status); archive.Header[52] = 0;'),
]:
    add(name, old, new, 'ParkSelectionSave.cs')


def run(full=False):
    trx = WORK / 'result.trx'; trx.unlink(missing_ok=True); start = time.monotonic()
    if full:
        cmd = ['dotnet', 'test', 'tests/TPW.Sim.Tests/', '--nologo', '--logger',
               'trx;LogFileName=result.trx', '--results-directory', str(WORK)]
    else:
        build = subprocess.run(['dotnet', 'build', 'core/TPW.Sim/', '--no-restore', '--nologo'],
                               cwd=ROOT, capture_output=True, text=True, timeout=180)
        if build.returncode:
            return dict(status='invalid', compile_error=True, output=(build.stdout + build.stderr)[-3000:])
        shutil.copy2(ROOT / 'core/TPW.Sim/bin/Debug/net8.0/TPW.Sim.dll', TEST_DLL.parent / 'TPW.Sim.dll')
        cmd = ['dotnet', 'vstest', str(TEST_DLL), '--TestCaseFilter:' + FILTER,
               '--logger:trx;LogFileName=result.trx', '--ResultsDirectory:' + str(WORK)]
    proc = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, timeout=180)
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    counters, failed = {}, []
    if trx.exists():
        tree = ET.parse(trx); counter = tree.find('.//t:Counters', ns)
        if counter is not None:
            counters = {k: int(v) for k, v in counter.attrib.items()}
        failed = [e.get('testName') for e in tree.findall('.//t:UnitTestResult', ns) if e.get('outcome') == 'Failed']
    valid = counters.get('executed', 0) > 0 and counters.get('notExecuted') == 0
    status = ('killed' if valid and failed and proc.returncode != 0 else
              'survived' if valid and not failed and proc.returncode == 0 else 'invalid')
    return dict(status=status, exit_code=proc.returncode, counters=counters, failed_tests=failed,
                seconds=round(time.monotonic() - start, 2),
                **({'output': (proc.stdout + proc.stderr)[-3000:]} if status == 'invalid' else {}))


def main():
    assert subprocess.check_output(['git', 'branch', '--show-current'], cwd=ROOT, text=True).strip() == 'selection'
    WORK.mkdir(parents=True, exist_ok=True)
    for name, data in ORIGINAL.items():
        (WORK / (name + '.backup')).write_bytes(data)
    protected = [FIXTURE, ROOT / 'tools/audit_selection.py',
                 ROOT / 'tests/TPW.Sim.Tests/ParkSelectionTests.cs',
                 ROOT / 'tests/TPW.Sim.Tests/ParkSelectionSaveTests.cs']
    hashes = {str(p.relative_to(ROOT)): sha(p.read_bytes()) for p in protected}
    audit = dict(source_sha256={n: sha(v) for n, v in ORIGINAL.items()}, protected_sha256=hashes,
                 filter=FILTER, planned=len(MUTATIONS), mutations=[], restored=False)

    def save():
        OUT.write_text(json.dumps(audit, indent=2) + '\n')

    def interrupted(signum, frame):
        raise KeyboardInterrupt(f'signal {signum}')

    for sig in (signal.SIGINT, signal.SIGTERM):
        signal.signal(sig, interrupted)
    try:
        audit['baseline'] = run(True); assert audit['baseline']['status'] == 'survived', audit['baseline']
        frozen = sha(TEST_DLL.read_bytes()); audit['frozen_test_assembly_sha256'] = frozen
        audit['target_baseline'] = run()
        assert audit['target_baseline']['status'] == 'survived'
        expected = audit['target_baseline']['counters']['executed']
        assert expected > 0
        for i, mutation in enumerate(MUTATIONS, 1):
            file = mutation['file']
            FILES[file].write_text(ORIGINAL[file].decode().replace(mutation['old'], mutation['new'], 1))
            result = run(); FILES[file].write_bytes(ORIGINAL[file])
            result['frozen_tests'] = sha(TEST_DLL.read_bytes()) == frozen
            result['frozen_inputs'] = hashes == {str(p.relative_to(ROOT)): sha(p.read_bytes()) for p in protected}
            counts = result.get('counters', {})
            if (counts.get('executed') != expected or counts.get('notExecuted') != 0 or
                    not result['frozen_tests'] or not result['frozen_inputs']):
                result['status'] = 'invalid'
            audit['mutations'].append(dict(**mutation, **result)); save()
            print(f"{i}/{len(MUTATIONS)} {result['status']}: {mutation['name']}", flush=True)
    finally:
        for name, path in FILES.items():
            path.write_bytes(ORIGINAL[name])
        audit['restored'] = all(p.read_bytes() == ORIGINAL[n] for n, p in FILES.items())
        audit['summary'] = {k: sum(m['status'] == k for m in audit['mutations']) for k in ('killed', 'survived', 'invalid')}
        audit['summary']['total'] = len(audit['mutations']); save()
    audit['final_full_suite'] = run(True); save(); print(json.dumps(audit['summary']))
    assert len(audit['mutations']) == len(MUTATIONS) and audit['summary']['killed'] == len(MUTATIONS)
    assert audit['final_full_suite']['status'] == 'survived'


if __name__ == '__main__':
    main()
