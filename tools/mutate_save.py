#!/usr/bin/env python3
"""Save buffer/restore mutations. Run on saveload; no concurrent production edits.

Baseline-compiled tests reject production mutants. Compile failures are invalid, never kills.
Durable journal, signal restoration and --recover follow tools/mutate_research.py.
All work stays in this worktree; --resume retains earlier survivors and test hashes.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
FILES = {key: ROOT / 'core/TPW.Sim' / name for key, name in {
    'park': 'ParkSave.cs', 'records': 'SaveRecords.cs', 'visitors': 'VisitorSave.cs',
    'host': 'ParkSaveHost.cs', 'archive': 'SaveArchive.cs', 'research': 'ResearchSave.cs'}.items()}
OUTPUT = ROOT / 'findings/save-mutations.json'
STATE = ROOT / 'core/TPW.Sim/obj/save-mutation-backup'
RESULTS = ROOT / 'tests/TPW.Sim.Tests/obj/save-mutation-results'
FILTER = 'FullyQualifiedName~ParkSaveTests|FullyQualifiedName~SaveRestoreTests|FullyQualifiedName~SaveArchiveTests|FullyQualifiedName~ResearchSaveTests'
MUTATIONS = []

# Historical controls from development, retained even when the final full sweep is rerun.
# This passing mutant is mathematically equivalent, not a test strengthened into a false kill.
DEVELOPMENT_CONTROLS = {
    'equivalent_mutation': {
        'file': 'SaveRecords.cs', 'old': 'Status >= 4', 'new': 'Status >= 5',
        'observed_exit_code': 0, 'tests_passed': 50, 'tests_failed': 0,
        'reason': 'Excluding status 4 still returns unchanged status 4; both branches return 4.',
        'replacement': 'Status >= 6; status 5 now incorrectly survives restoration.',
    },
    'signal_restoration': {
        'signal': 'SIGTERM', 'completed_mutations': 99, 'killed': 98, 'equivalent_survivors': 1,
        'restored': True, 'verification': 'All six restored SHA-256 hashes equaled their original hashes.',
    },
    'final_rerun': 'Fresh full sweep after correcting staff/admission field labels and the equivalent mutation; GENDATA flag meanings documented.',
}

def change(name, old, new, file='park'):
    MUTATIONS.append(dict(name=name, file=file, old=old, new=new))

for name, old, new in [
    ('path type2', 'type == 2 || type == 13', 'type == 4 || type == 13'),
    ('path type13', 'type == 2 || type == 13', 'type == 2 || type == 7'),
    ('path bit order', '(byte)(1 << (tile & 7))', '(byte)(1 << (7 - (tile & 7)))'),
    ('path row stride', 'tile = y * layout.Width + x', 'tile = x * layout.Height + y'),
    ('path reservation', 'return Width * Height;', 'return (Width * Height + 7) / 8;'),
    ('dimension bound', '(long)Width * Height > short.MaxValue', '(long)Width * Height > int.MaxValue'),
    ('catalogue order', 'new[] { 3, 7, 6, 1, 2, 4, 5, 8 }', 'new[] { 7, 3, 6, 1, 2, 4, 5, 8 }'),
    ('catalogue pair size', 'CatalogueCounts.Sum() * 2', 'CatalogueCounts.Sum()'),
    ('header marker', 'Marker { get; set; } = 0x3039', 'Marker { get; set; } = 0x3038'),
    ('write header world', 'w.Byte(park.Layout.World); w.Byte(park.Layout.Park);', 'w.Byte(park.Layout.Park); w.Byte(park.Layout.World);'),
    ('write attraction counts reverse', 'foreach (var group in park.Attractions) w.Byte', 'foreach (var group in park.Attractions.Reverse()) w.Byte'),
    ('write staff counts reverse', 'foreach (var group in park.Staff) w.Byte', 'foreach (var group in park.Staff.Reverse()) w.Byte'),
    ('write visitor count', 'w.Byte(park.VisitorCount);', 'w.Byte(0);'),
    ('write unknown header', 'w.Byte(park.UnknownHeader17);', 'w.Byte(0);'),
    ('write gate', 'w.Byte(park.Open);', 'w.Byte(0);'),
    ('write unknown gate', 'w.Byte(park.UnknownGate1);', 'w.Byte(0);'),
    ('write fee', 'w.U16(park.EntryFeePounds);', 'w.U16((ushort)(park.EntryFeePounds / 10));'),
    ('write litter order', 'w.Byte(park.OrdinaryLitter); w.Byte(park.Vomit);', 'w.Byte(park.Vomit); w.Byte(park.OrdinaryLitter);'),
    ('write omit bank', 'w.Block(park.Bank.Bytes, 0x328);', 'w.Block(new byte[0x328], 0x328);'),
    ('write omit calendar', 'w.Block(park.Calendar.Bytes, 0xDC);', 'w.Block(new byte[0xDC], 0xDC);'),
    ('write ignore catalogue', 'w.Block(park.Catalogue, park.Layout.Restricted ? 0 : park.Layout.CatalogueBytes);', 'w.Block(new byte[park.Catalogue.Length], park.Layout.Restricted ? 0 : park.Layout.CatalogueBytes);'),
    ('write ignore topics', 'w.Block(park.ResearchTopics, park.Layout.Restricted ? 0 : 16);', 'w.Block(new byte[park.ResearchTopics.Length], park.Layout.Restricted ? 0 : 16);'),
    ('write type4 message', 'm => m.Type != 4', 'm => true'),
    ('write string discriminator', 'if (message.StringId == -1)', 'if (message.StringId == -2)'),
    ('write target discriminator', 'message.Type == 2 ? message.TargetType', 'message.Type == 1 ? message.TargetType'),
    ('read world check', 'world != layout.World || subpark != layout.Park', 'world != layout.World && subpark != layout.Park'),
    ('read counts length', 'var counts = r.Block(12);', 'var counts = r.Block(13);'),
    ('read staff header index', 'counts[7 + i]', 'counts[6 + i]'),
    ('read unknown header', 'park.UnknownHeader17 = r.Byte();', 'r.Byte(); park.UnknownHeader17 = 0;'),
    ('read unknown gate', 'park.UnknownGate1 = r.Byte();', 'r.Byte(); park.UnknownGate1 = 0;'),
    ('read fee', 'park.EntryFeePounds = r.U16();', 'park.EntryFeePounds = (ushort)(r.U16() / 10);'),
    ('read string discriminator', 'id == -1 ? r.Block(r.Byte())', 'id == -2 ? r.Block(r.Byte())'),
    ('read target index', 'target == -1 ? (byte)0 : r.Byte()', 'target == -1 ? (byte)0 : (byte)(r.Byte() + 1)'),
    ('reject trailing data removed', 'r.RequireEnd(); park.Padding', 'park.Padding'),
    ('read byte order', '(ushort)(Byte() | Byte() << 8)', '(ushort)(Byte() << 8 | Byte())'),
    ('write byte order', 'Byte((byte)value); Byte((byte)(value >> 8));', 'Byte((byte)(value >> 8)); Byte((byte)value);'),
    ('copy block reversed', 'output.AddRange(bytes);', 'output.AddRange(bytes.Reverse());'),
    ('alignment', 'int count = (alignment - position % alignment) % alignment;', 'int count = position % alignment;'),
    ('alignment preserve', 'suppliedPadding[paddingPosition++]', '(byte)(suppliedPadding[paddingPosition++] ^ 1)'),
    ('padding read', 'Padding.Add(Byte())', 'Padding.Add((byte)(Byte() ^ 1))'),
    ('padding unused guard', 'if (suppliedPadding.Length != paddingPosition)', 'if (false)'),
]: change(name, old, new)

# Each named record field's pair is changed together: independent byte-layout tests must kill it,
# so a matching wrong getter and setter cannot hide behind a round trip.
source = FILES['records'].read_text()
for line in source.splitlines():
    if 'public ' not in line or 'get =>' not in line or 'set =>' not in line:
        continue
    matches = list(re.finditer(r'(?:I32|U16)\((0x[0-9A-F]+|\d+)\)|Bytes\[(\d+)\]', line))
    if not matches: continue
    changed = re.sub(r'(I32|U16)\((0x[0-9A-F]+|\d+)(?=[,)])',lambda m:m[1]+'('+str(int(m[2],0)+1),line)
    changed = re.sub(r'Bytes\[(\d+)\]',lambda m:'Bytes['+str(int(m[1])+1)+']',changed)
    change('record field '+line.strip().split('{')[0].strip(),line,changed,'records')
for name, old, new in [
    ('ride placement day offset', 'return U16(0x88);', 'return U16(0x8A);'),
    ('ride saved speed offset', 'return new(U16(0x8A), Bytes[0x8C], Bytes[0x8D]);', 'return new(U16(0x88), Bytes[0x8C], Bytes[0x8D]);'),
    ('ride reliability read precision', 'return Bytes[0x8F] << 12;', 'return Bytes[0x8F] << 8;'),
    ('ride reliability write precision', '(byte)(value >> 12)', '(byte)(value >> 8)'),
    ('ride saved level offset', 'return Bytes[0x92];', 'return Bytes[0x91];'),
]: change(name, old, new, 'records')
for name,old,new in [
    ('attraction types', 'new[] { 1, 6, 3, 7, 2, 4, 5 }', 'new[] { 1, 3, 6, 7, 2, 4, 5 }'),
    ('attraction stride', '0x98, 0xF0, 0x98, 0x418', '0x98, 0xEC, 0x98, 0x418'),
    # >=5 is equivalent: excluded status 4 still returns 4. >=6 must change status 5.
    ('ride restore status low', 'Status >= 4', 'Status >= 6'),
    ('ride restore status high', 'Status <= 6', 'Status <= 5'),
    ('staff strike bit', 'SkillAndStrike & 0x80', 'SkillAndStrike & 0x01'),
]: change(name,old,new,'records')
for name,old,new in [
    ('visitor minima count', 'i < 10', 'i < 12'),
    ('visitor empty minimum', '= 9999;', '= 0;'),
    ('visitor min becomes max', 'Math.Min(minimum[i], values[i])', 'Math.Max(minimum[i], values[i])'),
    ('visitor max becomes min', 'Math.Max(maximum[i], values[i])', 'Math.Min(maximum[i], values[i])'),
    ('visitor capture money signed', 'unchecked((short)v.Money.Raw)', 'unchecked((ushort)v.Money.Raw)'),
    ('visitor current normal swapped', 'v.WalkSpeed, v.NormalWalkSpeed, unknown', 'v.NormalWalkSpeed, v.WalkSpeed, unknown'),
    ('visitor byte range width', 'Bytes[offset + 1] = unchecked((byte)range);', 'Bytes[offset + 1] = unchecked((byte)(range + 1));'),
    ('visitor halfword range', 'U16(18, unchecked((ushort)range))', 'U16(18, unchecked((ushort)minimum))'),
    ('visitor range inclusive', 'int bound = range + 1;', 'int bound = Math.Max(1, range);'),
    ('visitor mean single roll', '(first + second) / 2', 'first'),
    ('visitor high bias fixed', 'bound - first * second / bound', 'bound - 1 - first * second / bound'),
    ('visitor high becomes uniform', 'bound - first * second / bound', '(first + second) / 2'),
    ('visitor rubbish omitted', 'guest.Rubbish = Draw(0, false);', '_ = Draw(0, false);'),
    ('visitor happiness source', 'Draw(1, false)', 'Draw(2, false)'),
    ('visitor needA source', 'Draw(3, true)', 'Draw(4, true)'),
    ('visitor nausea restored', 'guest.NeedA = Draw(3, true);', 'guest.NeedA = Draw(3, true); guest.Nausea = Get(2).Minimum;'),
    ('visitor boredom restored', 'guest.RideDesire = Draw(5, true);', 'guest.RideDesire = Draw(5, true); guest.Boredom = Get(4).Minimum;'),
    ('visitor money units', 'Money.FromRaw(Draw(8, false))', 'Money.FromPounds(Draw(8, false))'),
    ('visitor speed sign', '(sbyte)Draw(9, false)', '(byte)Draw(9, false)'),
    ('visitor appearance', 'random.Next(8)', 'random.Next(7)'),
    ('visitor state', 'VisitorState.WalkIn', 'VisitorState.SpawnToGate'),
    ('visitor animation', 'guest.Animation = 13;', 'guest.Animation = 11;'),
    ('visitor facing', 'guest.Facing = 0;', 'guest.Facing = 2;'),
    ('litter count order', 'kind == 0 ? ordinary : vomit', 'kind == 0 ? vomit : ordinary'),
    ('litter type13 accepted', 'type != 2 && type != 4 && type != 7', 'type != 2 && type != 4 && type != 7 && type != 13'),
    ('litter centre x', '(x << 8) + 0x80', '(x << 8)'),
    ('litter centre y', '(y << 8) + 0x80', '(y << 8)'),
    ('litter vomit marking', 'if (kind == 1)', 'if (kind == 0)'),
]: change(name,old,new,'visitors')
for name,old,new in [
    ('host gate inverted', 'if (park.Open != 0)', 'if (park.Open == 0)'),
    ('host fee units', 'Money.FromPounds(park.EntryFeePounds)', 'Money.FromRaw(park.EntryFeePounds)'),
    ('host path duplicated call removed', 'host.PlacePath(x, y);\n                host.PlacePath(x, y);', 'host.PlacePath(x, y);'),
    ('host path bit order', '(1 << (tile & 7))', '(1 << (7 - (tile & 7)))'),
    ('host guest count', 'i < park.VisitorCount', 'i < park.VisitorCount - 1'),
    ('host guest unknown callback', 'host.SetVisitorUnknown63(guest, unknown);', '// callback omitted'),
    ('host litter order', 'host.RestoreLitter(park.OrdinaryLitter, park.Vomit)', 'host.RestoreLitter(park.Vomit, park.OrdinaryLitter)'),
    ('host bank calendar order', 'host.RestoreBank(park.Bank);\n        host.RestoreCalendar(park.Calendar);', 'host.RestoreCalendar(park.Calendar);\n        host.RestoreBank(park.Bank);'),
    ('host research catalogue order', 'host.RestoreCatalogue(park.Catalogue);\n            host.RestoreResearchTopics(park.ResearchTopics);', 'host.RestoreResearchTopics(park.ResearchTopics);\n            host.RestoreCatalogue(park.Catalogue);'),
    ('host finish omitted', 'host.FinishPark();', '// omitted'),
]: change(name,old,new,'host')
for name,old,new in [
    ('archive mode getter', 'get => Header[42] == 0', 'get => Header[42] != 0'),
    ('archive mode writer', 'value ? (byte)0 : (byte)1', 'value ? (byte)1 : (byte)0'),
    ('archive checksum range', 'i < length; i += 4', 'i < Math.Max(0, length - 0x204); i += 4'),
    ('archive checksum byte order', '<< (8 * b)', '<< (8 * (3 - b))'),
    ('archive checksum partial word', 'i + b < length', 'i + b <= length'),
    ('archive checksum sums xor', 'sum + word', 'sum ^ word'),
    ('archive footer comparison', 'U32(file, HeaderAt) != U32(file, ChecksumAt)', 'false'),
    ('archive checksum comparison', 'Checksum(file, (int)length) != U32(file, HeaderAt)', 'false'),
    ('archive magic comparison', 'U32(file, 0x208) != Magic', 'false'),
    ('archive version comparison', 'U16(file, 0x20C) != Version', 'false'),
    ('archive magic writer', 'Put32(file, 0x208, Magic);', 'Put32(file, 0x208, Magic + 1);'),
    ('archive version writer', 'Put16(file, 0x20C, Version);', 'Put16(file, 0x20C, Version + 1);'),
    ('archive exact status', 'file[0x22B + slot] != 0xF0', '(file[0x22B + slot] & 0xF0) != 0xF0'),
    ('archive park size offset', 'U16(file, 0x224 + 2 * index)', 'U16(file, 0x226 + 2 * index)'),
    ('archive general bytes', 'save.GeneralData.CopyTo(file, DataAt);', '// omitted'),
    ('archive payload bytes', 'packet.CopyTo(file, cursor);', '// omitted'),
    ('archive length absolute', 'Put32(file, 0x204, (uint)length);', 'Put32(file, 0x204, (uint)(length - DataAt));'),
    ('archive file size', 'FileSize = 0xA000;', 'FileSize = 0xA004;'),
    ('packet total length', 'packet.Length);\n        body.CopyTo', 'body.Length);\n        body.CopyTo'),
    ('packet decode slice', 'compression.Decode(packet.Slice(8), size)', 'compression.Decode(packet.Slice(4), size)'),
    ('packet decoded size check', 'if (result.Length != size)', 'if (false)'),
]: change(name,old,new,'archive')
for name,old,new in [
    ('research write percent', 'unchecked((byte)research.ProgressPercent(definition, levels))', '(byte)0'),
    ('research write levels', 'result[at++] = levels;', 'result[at++] = 0;'),
    ('research restore pair', 'int percent = bytes[at++], levels = bytes[at++];', 'int levels = bytes[at++], percent = bytes[at++];'),
    ('research last definition skipped', 'index < layout.CatalogueCounts[group]', 'index < layout.CatalogueCounts[group] - 1'),
    ('research restore omitted', 'freshResearch.StoreProgress(definition, levels, percent);', '// omitted'),
]:
    if name == 'research last definition skipped':
        # Select writer only, leave the loader intact.
        old = 'for (int index = 0; index < layout.CatalogueCounts[group]; index++)\n            {\n                var definition = new ResearchDefinition(ParkSaveLayout.CatalogueTypes[group], index);\n                byte levels'
        new = old.replace('index < layout.CatalogueCounts[group]', 'index < layout.CatalogueCounts[group] - 1')
    change(name,old,new,'research')

def sha(data):
    return hashlib.sha256(data).hexdigest()


def atomic(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + '.tmp')
    with temp.open('wb') as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())
    temp.replace(path)


def write_json(path, data):
    atomic(path, (json.dumps(data, indent=2) + '\n').encode())


def recover():
    journal = STATE / 'journal.json'
    if not journal.exists():
        return
    state = json.loads(journal.read_text())
    for key, meta in state.items():
        original = (STATE / key).read_bytes()
        if sha(original) != meta['original'] or sha(FILES[key].read_bytes()) not in (meta['original'], meta['active']):
            raise RuntimeError('Source changed outside sweep; inspect durable backups before recovery.')
    for key in state:
        atomic(FILES[key], (STATE / key).read_bytes())
    shutil.rmtree(STATE)
    print('Recovered interrupted save sweep.', flush=True)


child = None


def run(command):
    global child
    child = subprocess.Popen(command, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                             text=True, start_new_session=True)
    output, _ = child.communicate(timeout=180)
    code = child.returncode
    child = None
    return code, output


def test(full=False):
    RESULTS.mkdir(parents=True, exist_ok=True)
    trx = RESULTS / 'result.trx'
    trx.unlink(missing_ok=True)
    start = time.monotonic()
    if full:
        code, output = run(['dotnet', 'test', 'tests/TPW.Sim.Tests/', '--nologo',
                            '--logger', 'trx;LogFileName=result.trx', '--results-directory', str(RESULTS)])
    else:
        code, output = run(['dotnet', 'build', 'core/TPW.Sim/', '--no-restore', '--nologo'])
        if code:
            return dict(exit_code=code, compile_error=True, failed_tests=[], output=output[-4000:])
        shutil.copy2(ROOT / 'core/TPW.Sim/bin/Debug/net8.0/TPW.Sim.dll',
                     ROOT / 'tests/TPW.Sim.Tests/bin/Debug/net8.0/TPW.Sim.dll')
        code, output = run(['dotnet', 'vstest', 'tests/TPW.Sim.Tests/bin/Debug/net8.0/TPW.Sim.Tests.dll',
                            '--TestCaseFilter:' + FILTER, '--logger:trx;LogFileName=result.trx',
                            '--ResultsDirectory:' + str(RESULTS)])
    result = dict(exit_code=code, seconds=round(time.monotonic()-start, 2),
                  compile_error='error CS' in output or 'error MSB' in output, counters={}, failed_tests=[])
    if trx.exists():
        ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
        tree = ET.parse(trx)
        result['failed_tests'] = [e.get('testName') for e in tree.findall('.//t:UnitTestResult', ns)
                                  if e.get('outcome') == 'Failed']
        counter = tree.find('.//t:Counters', ns)
        if counter is not None:
            result['counters'] = {k: int(v) for k, v in counter.attrib.items()}
    if not result['counters']:
        result['output'] = output[-4000:]
    return result


class Interrupted(BaseException):
    pass


def stop(signum, frame):
    raise Interrupted(f'signal {signum}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--recover', action='store_true')
    parser.add_argument('--resume', action='store_true')
    parser.add_argument('--retry', action='append', default=[], choices=[m['name'] for m in MUTATIONS])
    args = parser.parse_args()
    if subprocess.check_output(['git', 'branch', '--show-current'], cwd=ROOT, text=True).strip() != 'saveload':
        raise RuntimeError('Run only on saveload.')
    recover()
    if args.recover:
        return 0
    originals = {key: path.read_bytes() for key, path in FILES.items()}
    for mutation in MUTATIONS:
        if originals[mutation['file']].decode().count(mutation['old']) != 1:
            raise RuntimeError('Nonunique/missing mutation anchor: ' + mutation['name'])
    report = dict(development_controls=DEVELOPMENT_CONTROLS,
                  sources={k: str(p.relative_to(ROOT)) for k, p in FILES.items()},
                  original_sha256={k: sha(v) for k, v in originals.items()}, filter=FILTER,
                  test_sha256={str(p.relative_to(ROOT)): sha(p.read_bytes()) for p in
                               [ROOT / 'tests/TPW.Sim.Tests' / name for name in
                                ('ParkSaveTests.cs', 'SaveFixture.cs', 'SaveRestoreTests.cs', 'SaveArchiveTests.cs', 'ResearchSaveTests.cs')]},
                  mutations=[], restored=False)
    if args.resume:
        previous = json.loads(OUTPUT.read_text())
        if previous['original_sha256'] != report['original_sha256'] or not previous['restored']:
            raise RuntimeError('Resume requires byte-identical restored production sources.')
        report['previous_runs'] = previous.get('previous_runs', []) + [
            {k: previous[k] for k in ('baseline', 'summary', 'test_sha256') if k in previous}]
        report['mutations'] = previous['mutations']
    for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
        signal.signal(sig, stop)
    try:
        STATE.mkdir(parents=True, exist_ok=True)
        for key, original in originals.items():
            atomic(STATE / key, original)
        journal = {key: dict(original=sha(data), active=sha(data)) for key, data in originals.items()}
        write_json(STATE / 'journal.json', journal)
        report['baseline'] = test(full=True)
        if report['baseline']['exit_code']:
            raise RuntimeError('Baseline failed.')
        for i, mutation in enumerate(MUTATIONS, 1):
            prior = next((m for m in report['mutations'] if m['name'] == mutation['name']), None)
            if prior and any(prior[k] != mutation[k] for k in ('file', 'old', 'new')):
                raise RuntimeError('Mutation changed since previous sweep; use a fresh run.')
            if prior and prior['status'] == 'killed' and mutation['name'] not in args.retry:
                continue
            key = mutation['file']
            mutant = originals[key].decode().replace(mutation['old'], mutation['new'], 1).encode()
            journal[key]['active'] = sha(mutant)
            write_json(STATE / 'journal.json', journal)
            atomic(FILES[key], mutant)
            try:
                result = test()
            finally:
                atomic(FILES[key], originals[key])
            result.update(mutation)
            result['status'] = ('killed' if result['failed_tests'] and not result['compile_error'] else
                                'survived' if result['exit_code'] == 0 else 'invalid')
            if prior:
                result['previous_attempts'] = prior.get('previous_attempts', []) + [
                    {k: v for k, v in prior.items() if k != 'previous_attempts'}]
                report['mutations'][report['mutations'].index(prior)] = result
            else:
                report['mutations'].append(result)
            write_json(OUTPUT, report)
            print(f"{i}/{len(MUTATIONS)} {result['status']}: {mutation['name']}", flush=True)
        report['final_full_suite'] = test(full=True)
    except BaseException as error:
        report['interruption'] = repr(error)
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
        for key, original in originals.items():
            atomic(FILES[key], original)
        report['restored_sha256'] = {k: sha(p.read_bytes()) for k, p in FILES.items()}
        report['restored'] = report['restored_sha256'] == report['original_sha256']
        report['summary'] = {s: sum(m['status'] == s for m in report['mutations'])
                             for s in ('killed', 'survived', 'invalid')}
        report['summary']['total'] = len(report['mutations'])
        write_json(OUTPUT, report)
        shutil.rmtree(STATE)
    print(json.dumps(report['summary']), flush=True)
    return int(report['summary']['survived'] != 0 or report['summary']['invalid'] != 0
               or report['final_full_suite']['exit_code'] != 0)


if __name__ == '__main__':
    sys.exit(main())
