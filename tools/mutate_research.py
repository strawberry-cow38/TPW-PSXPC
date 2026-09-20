#!/usr/bin/env python3
"""Research/upgrade behavioral mutations. Run on research, with no concurrent source edits.

Only failed tests kill mutants; compilation failures are invalid. Tests remain compiled against
the baseline (including inlined constants). --resume retains survivors and their failing history.
Use --resume --retry NAME to recheck a previously killed mutation after strengthening its fixture.
SIGINT/TERM/HUP restore sources; durable backups permit --recover after SIGKILL/power loss.
All temporary files and builds stay in this worktree. See findings/research-mutations.json.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
FILES = {key: ROOT / 'core/TPW.Sim' / name for key, name in {
    'research': 'Research.cs', 'staff': 'StaffClasses.cs', 'upgrade': 'RidePanel.cs'}.items()}
OUTPUT = ROOT / 'findings/research-mutations.json'
STATE = ROOT / 'core/TPW.Sim/obj/research-mutation-backup'
RESULTS = ROOT / 'tests/TPW.Sim.Tests/obj/research-mutation-results'
FILTER = 'FullyQualifiedName~ResearchTests|FullyQualifiedName~RidePanelTests|FullyQualifiedName~RideSliderEffectsTests'
MUTATIONS = []


def change(name, old, new, file='research'):
    MUTATIONS.append(dict(name=name, file=file, old=old, new=new))


for name, old, new in [
    ('topic count', 'TopicCount = 5;', 'TopicCount = 4;'),
    ('catalogue size', 'ProgressRecordCount = 60;', 'ProgressRecordCount = 59;'),
    ('level count maximum', 'MaximumLevelCount = 3;', 'MaximumLevelCount = 2;'),
    ('funding default', 'DefaultFunding = 80;', 'DefaultFunding = 81;'),
    ('funding minimum', 'MinimumFunding = 70,', 'MinimumFunding = 71,'),
    ('funding maximum', 'MaximumFunding = 100;', 'MaximumFunding = 99;'),
    ('fixed shift', 'FixedShift = 12;', 'FixedShift = 11;'),
    ('zero-work percent', 'RequiredFixed == 0 ? 100u', 'RequiredFixed == 0 ? 0u'),
    ('widen percent multiplication', 'unchecked(ProgressFixed * 100u) / RequiredFixed', '(uint)((ulong)ProgressFixed * 100u / RequiredFixed)'),
    ('clamp percent', 'unchecked(ProgressFixed * 100u) / RequiredFixed', 'Math.Min(100u, unchecked(ProgressFixed * 100u) / RequiredFixed)'),
    ('progress ignores type', 'if (!progress.TryGetValue(definition, out var value))', 'definition = new(3, definition.Index); if (!progress.TryGetValue(definition, out var value))'),
    ('completion strict', 'if (percent >= 100)', 'if (percent > 100)'),
    ('completion increments twice', 'level++; percent = 0;', 'level += 2; percent = 0;'),
    ('carry excess percent', 'level++; percent = 0;', 'level++; percent -= 100;'),
    ('allow level rollback', 'if (level < previous.CompletedLevels) return;', '// rollback allowed'),
    ('refuse same-level updates', 'if (level < previous.CompletedLevels)', 'if (level <= previous.CompletedLevels)'),
    ('saturate level byte', 'unchecked((byte)level)', '(byte)Math.Clamp(level, 0, 255)'),
    ('saturate percent byte', 'unchecked((byte)percent)', '(byte)Math.Clamp(percent, 0, 100)'),
    ('progress completed inclusive', 'if (level < value.CompletedLevels)', 'if (level <= value.CompletedLevels)'),
    ('free criterion uses work', 'int tier = world.ReadResearchLevel(definition, level).Tier;', 'int tier = world.ReadResearchLevel(definition, level).Work;'),
    ('free query has no side effect', 'StoreProgress(definition, level, 100);\n                return 100;', 'return 100;'),
    ('future query returns zero', 'return value.Percent;', 'return level > value.CompletedLevels ? 0 : value.Percent;'),
    ('availability ignores restricted mode', 'world.AllResearchUnlocked || world.RestrictedMode ||', 'world.AllResearchUnlocked ||'),
    ('availability accepts over100', 'ProgressPercent(definition, level) == 100', 'ProgressPercent(definition, level) >= 100'),
    ('count applies restricted override', 'if (world.AllResearchUnlocked) return MaximumLevelCount;', 'if (world.AllResearchUnlocked || world.RestrictedMode) return MaximumLevelCount;'),
    ('count ignores debug override', 'if (world.AllResearchUnlocked) return MaximumLevelCount;', '// debug ignored'),
    ('count completes only one free level', 'while (level < MaximumLevelCount &&', 'if (level < MaximumLevelCount &&'),
    ('menu type order', 'RideMenuOrder = { 3, 6, 7, 1 }', 'RideMenuOrder = { 1, 3, 6, 7 }'),
    ('tier type order', 'RideTierOrder = { 6, 7, 1, 3 }', 'RideTierOrder = { 3, 6, 7, 1 }'),
    ('definition skips zero', 'for (int index = 0; index < world.DefinitionCount(type); index++)', 'for (int index = 1; index < world.DefinitionCount(type); index++)'),
    ('menu stops filtering', 'if (CanSelect(slot, definition)) result.Add(definition);', 'result.Add(definition);'),
    ('shop menu aliases sideshow', '1 => new[] { 4 }', '1 => new[] { 5 }'),
    ('feature menu aliases shop', '3 => new[] { 2 }', '3 => new[] { 4 }'),
    ('upgrade menu omits pieces', '4 => new[] { 3, 6, 7, 1, 8 }', '4 => new[] { 3, 6, 7, 1 }'),
    ('track pieces require variant one', 'IsAvailable(new(6, 0)) && !available', 'IsAvailable(new(6, 1)) && !available'),
    ('track pieces ignore prerequisite', 'return IsAvailable(new(6, 0)) && !available;', 'return !available;'),
    ('ordinary tier strict', 'ceilings[slot] >= world.ReadResearchLevel(definition, 0).Tier', 'ceilings[slot] > world.ReadResearchLevel(definition, 0).Tier'),
    ('upgrade ignores built instance', 'available && world.BuiltCount(definition) != 0 &&', 'available &&'),
    ('upgrade ignores availability', 'return available && world.BuiltCount', 'return world.BuiltCount'),
    ('upgrade offers fourth level', 'LevelCount(definition) < MaximumLevelCount;', 'LevelCount(definition) <= MaximumLevelCount;'),
    ('start replaces occupied topic', 'if (topic.Active) return false;', '// replace occupied'),
    ('start gates upgrades by tier', 'if (slot != 4 && ceilings[slot] < data.Tier)', 'if (ceilings[slot] < data.Tier)'),
    ('start ignores tier', 'if (slot != 4 && ceilings[slot] < data.Tier) return false;', '// no tier gate'),
    ('start ignores resumed percent', 'unchecked((uint)percent * topic.RequiredFixed) / 100u', '0u'),
    ('start leaves finished flag', 'topic.Finished = false;', '// finished retained'),
    ('select fails to stop first', 'Stop(slot);\n            return definition.HasValue', 'return definition.HasValue'),
    ('stop clears progress', 'public void Stop(int slot) => topics[slot].Active = false;', 'public void Stop(int slot) { topics[slot].Active = false; topics[slot].ProgressFixed = 0; }'),
    ('active count includes idle', 'foreach (var topic in topics) if (topic.Active) active++;', 'foreach (var topic in topics) active++;'),
    ('share signed division', 'unchecked((uint)(fundedPoints << FixedShift)) / (uint)(100 * active)', '(uint)(unchecked(fundedPoints << FixedShift) / (100 * active))'),
    ('share integer division first', 'unchecked((uint)(fundedPoints << FixedShift)) / (uint)(100 * active)', '(uint)(fundedPoints / (100 * active)) << FixedShift'),
    ('share funding twice', 'fundedPoints << FixedShift', '(fundedPoints * Funding) << FixedShift'),
    ('contribute skips inactive guard', 'if (!topic.Active) continue;', '// inactive included'),
    ('contribute replaces accumulator', 'unchecked(topic.ProgressFixed + share)', 'share'),
    ('contribute does not persist percent', 'StoreProgress(definition, level, unchecked((int)topic.Percent));', '// catalogue not updated'),
    ('finish strict inequality', 'if (topic.ProgressFixed < topic.RequiredFixed)', 'if (topic.ProgressFixed <= topic.RequiredFixed)'),
    ('finish leaves active', 'topic.Active = false;', 'topic.Active = true;'),
    ('finish loses finished flag', 'topic.Finished = true;', 'topic.Finished = false;'),
    ('finish does not dirty tiers', 'tiersDirty = true;\n                int message', 'tiersDirty = false;\n                int message'),
    ('finish does not clear index', 'topic.Definition = new(definition.Type, -1);', '// index retained'),
    ('finish changes later denominator', 'topic.Active = false;', 'topic.Active = false; if (--active > 0) share = unchecked((uint)(fundedPoints << FixedShift)) / (uint)(100 * active);'),
    ('base message off by one', 'LevelCount(definition) < 2 ? 0x56', 'LevelCount(definition) < 1 ? 0x56'),
    ('mechanic advice reversed', 'world.MechanicCount == 0 ? 0x90 : 0x57', 'world.MechanicCount != 0 ? 0x90 : 0x57'),
    ('shop announcement', '4 => 0x58', '4 => 0x59'),
    ('sideshow announcement', '5 => 0x59', '5 => 0x58'),
    ('feature announcement', '2 => 0x5A', '2 => 0x58'),
    ('piece announcement', '8 => 0x57', '8 => 0x56'),
    ('announcement omitted', 'world.AnnounceDiscovery(definition, message);', '// no announcement'),
    ('tiers recompute every query', 'if (!tiersDirty) return;', '// cache ignored'),
    ('tier threshold strict', '>= unchecked(2u * totals[cursor])', '> unchecked(2u * totals[cursor])'),
    ('tier threshold half', '3u * unlocked[cursor]', '4u * unlocked[cursor]'),
    ('tier uses next-level tier', 'world.ReadResearchLevel(definition, 0).Tier;\n                totals[tier]++', 'world.ReadResearchLevel(definition, LevelCount(definition)).Tier;\n                totals[tier]++'),
    ('tier count ignores availability', 'if (IsAvailable(definition)) unlocked[tier]++;', 'unlocked[tier]++;'),
    ('tier empty stops', 'while (unchecked(3u * unlocked[cursor])', 'while (totals[cursor] != 0 && unchecked(3u * unlocked[cursor])'),
    ('tier overrun silently clamps', 'throw new InvalidOperationException("Research tier scan overruns its five PSX bins.");', 'break;'),
    ('save loses active bits', 'topics[i].Active ? (byte)1 : (byte)0', '(byte)0'),
    ('save loses type', 'unchecked((byte)topics[i].Definition.Type)', '(byte)0'),
    ('restore effort clamped', 'Funding = saved[0];', 'ApplyFundingSlider(saved[0]);'),
    ('restore ignores active', 'if (saved[1 + 3 * i] != 0)', 'if (false)'),
    ('restore index unsigned', 'unchecked((sbyte)saved[3 + 3 * i])', 'saved[3 + 3 * i]'),
]: change(name, old, new)

for name, old, new in [
    ('research skill0 rate', 'PointsBySkill = { 20, 30, 35, 40, 43 }', 'PointsBySkill = { 21, 30, 35, 40, 43 }'),
    ('research skill1 rate', 'PointsBySkill = { 20, 30, 35, 40, 43 }', 'PointsBySkill = { 20, 31, 35, 40, 43 }'),
    ('research skill2 rate', 'PointsBySkill = { 20, 30, 35, 40, 43 }', 'PointsBySkill = { 20, 30, 36, 40, 43 }'),
    ('research skill3 rate', 'PointsBySkill = { 20, 30, 35, 40, 43 }', 'PointsBySkill = { 20, 30, 35, 41, 43 }'),
    ('research skill4 rate', 'PointsBySkill = { 20, 30, 35, 40, 43 }', 'PointsBySkill = { 20, 30, 35, 40, 44 }'),
    ('research probability', 'ResearchChanceInTen = 3;', 'ResearchChanceInTen = 4;'),
    ('research state id', 'Researching = (StaffState)31;', 'Researching = (StaffState)30;'),
    ('idle skips shared check', 'StaffBase.IdleCheck(staff, world); // slot 51;', '// slot 51;'),
    ('research skill mask', 'int skill = staff.Skill & 7;', 'int skill = staff.Skill & 3;'),
    ('research ignores funding', 'unchecked(points * world.ResearchFunding)', 'points'),
    ('research ignores fatigue', 'staff.Tiredness += (world.ResearchFunding - ResearchSystem.DefaultFunding) / 3;', '// fatigue omitted'),
    ('unsigned binary fatigue silently adopted', '(world.ResearchFunding - ResearchSystem.DefaultFunding) / 3', '(sbyte)(unchecked((uint)(world.ResearchFunding - ResearchSystem.DefaultFunding)) / 3u)'),
    ('research remains working', 'staff.Tiredness += (world.ResearchFunding - ResearchSystem.DefaultFunding) / 3;\n            staff.SetState(StaffState.Idle);', 'staff.Tiredness += (world.ResearchFunding - ResearchSystem.DefaultFunding) / 3;'),
]: change(name, old, new, 'staff')

for name, old, new in [
    ('upgrade research equality accepted', 'world.Level + 1 < world.ResearchedLevelCount', 'world.Level + 1 <= world.ResearchedLevelCount'),
    ('upgrade ignores condemnation', '&& world.Lifetime != 0', ''),
    ('upgrade ignores mechanic count', ' || world.MechanicCount == 0', ''),
    ('upgrade ignores strike', ' || world.MechanicsOnStrike', ''),
    ('upgrade fourth panel level', 'world.Level + 1 < PanelLevelCount', 'world.Level + 1 <= PanelLevelCount'),
    ('upgrade low level limit corrected', 'if (world.Level >= BinaryUpgradeLimit)', 'if (world.Level >= 2)'),
    ('upgrade not applied', 'world.Level++;', '// level unchanged'),
    ('upgrade half price', 'Money.FromPounds(world.ReadLevel(world.Level).PricePounds)', 'Money.FromPounds(world.ReadLevel(world.Level).PricePounds / 2)'),
    ('upgrade ignores bank rejection by returning false', 'world.TrySpend(Money.FromPounds(world.ReadLevel(world.Level).PricePounds));', 'if (!world.TrySpend(Money.FromPounds(world.ReadLevel(world.Level).PricePounds))) return false;'),
    ('upgrade resets lifetime', 'world.Level++;', 'world.Level++; world.Lifetime = world.ReadLevel(world.Level).Lifetime;'),
    ('upgrade uses old level defaults', 'var level = world.ReadLevel(world.Level);', 'var level = world.ReadLevel(0);'),
    ('upgrade clamps duration to minimum', 'Math.Max(MinimumDefault, level.CyclesMax >> 1)', 'Math.Max(level.CyclesMin, level.CyclesMax >> 1)'),
]: change(name, old, new, 'upgrade')


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
    print('Recovered interrupted research sweep.', flush=True)


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
    if subprocess.check_output(['git', 'branch', '--show-current'], cwd=ROOT, text=True).strip() != 'research':
        raise RuntimeError('Run only on research.')
    recover()
    if args.recover:
        return 0
    originals = {key: path.read_bytes() for key, path in FILES.items()}
    for mutation in MUTATIONS:
        if originals[mutation['file']].decode().count(mutation['old']) != 1:
            raise RuntimeError('Nonunique/missing mutation anchor: ' + mutation['name'])
    report = dict(sources={k: str(p.relative_to(ROOT)) for k, p in FILES.items()},
                  original_sha256={k: sha(v) for k, v in originals.items()}, filter=FILTER,
                  test_sha256={str(p.relative_to(ROOT)): sha(p.read_bytes()) for p in
                               [ROOT / 'tests/TPW.Sim.Tests' / name for name in
                                ('ResearchTests.cs', 'RidePanelTests.cs', 'RideSliderEffectsTests.cs')]},
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
