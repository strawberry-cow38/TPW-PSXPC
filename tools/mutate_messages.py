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
SOURCE = ROOT / 'core/TPW.Sim/ParkMessages.cs'
TESTS = ROOT / 'tests/TPW.Sim.Tests/ParkMessagesTests.cs'
OUTPUT = ROOT / 'findings/messages-mutations.json'
ORIGINAL = SOURCE.read_bytes()
FILTER = 'FullyQualifiedName~ParkMessagesTests'
mutations = []
def change(name, old, new):
    if ORIGINAL.decode().count(old) != 1:
        raise ValueError('Missing/nonunique anchor: ' + name)
    mutations.append(dict(name=name, old=old, new=new))

for name, old, new in [
    ('capacity 31', 'Capacity = 32;', 'Capacity = 31;'),
    ('capacity 33', 'Capacity = 32;', 'Capacity = 33;'),
    ('inline limit 254', 'MaxInlineBytes = 255;', 'MaxInlineBytes = 254;'),
    ('inline limit 256', 'MaxInlineBytes = 255;', 'MaxInlineBytes = 256;'),
    ('text id discarded', 'TextId = textId;', 'TextId = 0;'),
    ('kind narrowed at runtime', 'Kind = kind;', 'Kind = unchecked((byte)kind);'),
    ('kind discarded', 'Kind = kind;', 'Kind = 0;'),
    ('target discarded', 'Target = target;', 'Target = null;'),
    ('inline text discarded', 'text = inlineText.ToArray();', 'text = Array.Empty<byte>();'),
    ('push loses text sign bit', 'unchecked((short)textId), kind, target', 'unchecked((short)(textId & 0x7FFF)), kind, target'),
    ('inline sentinel wrong', 'new ParkMessage(-1, kind, target, text)', 'new ParkMessage(0, kind, target, text)'),
    ('advisor ignores text flag', 'if (!textEnabled || textId == AdvisorMessages.NoText)', 'if (textId == AdvisorMessages.NoText)'),
    ('advisor keeps voice-only cards', 'if (!textEnabled || textId == AdvisorMessages.NoText)', 'if (!textEnabled)'),
    ('advisor suppresses adjacent text', 'textId == AdvisorMessages.NoText', 'textId == AdvisorMessages.NoText + 1'),
    ('advisor gate uses AND', '!textEnabled || textId == AdvisorMessages.NoText', '!textEnabled && textId == AdvisorMessages.NoText'),
    ('advisor discards kind', 'return Push(textId, kind, target);', 'return Push(textId, 0, target);'),
    ('advisor discards target', 'return Push(textId, kind, target);', 'return Push(textId, kind);'),
    ('evict newest', 'if (live.Count == Capacity) live.RemoveAt(0);', 'if (live.Count == Capacity) live.RemoveAt(live.Count - 1);'),
    ('never evict', 'if (live.Count == Capacity) live.RemoveAt(0);', '// eviction omitted'),
    ('reject overflow', 'if (live.Count == Capacity) live.RemoveAt(0);', 'if (live.Count == Capacity) return message;'),
    ('deduplicate text', 'live.Add(message);', 'if (live.Exists(m => m.TextId == message.TextId)) return message; live.Add(message);'),
    ('prepend newest', 'live.Add(message);', 'live.Insert(0, message);'),
    ('retract compares wide input', 'if (live[i].TextId == id)', 'if (live[i].TextId == textId)'),
    ('retract starts at one', 'for (int i = 0; i < live.Count; i++)', 'for (int i = 1; i < live.Count; i++)'),
    ('retract last duplicate', 'for (int i = 0; i < live.Count; i++)', 'for (int i = live.Count - 1; i >= 0; i--)'),
    ('retract removes head', 'live.RemoveAt(i);', 'live.RemoveAt(0);'),
    ('retract keeps matching card', 'live.RemoveAt(i);', '// removal omitted'),
    ('retract removes all duplicates', 'live.RemoveAt(i);', 'live.RemoveAll(m => m.TextId == id);'),
    ('retract wrong success', 'live.RemoveAt(i);\n                return true;', 'live.RemoveAt(i);\n                return false;'),
    ('poster retract ignores text flag', '=> textEnabled && textId != AdvisorMessages.NoText && Retract(textId);', '=> textId != AdvisorMessages.NoText && Retract(textId);'),
    ('poster retract ignores sentinel', '=> textEnabled && textId != AdvisorMessages.NoText && Retract(textId);', '=> textEnabled && Retract(textId);'),
    ('player deletes oldest', 'Delete(int index) => live.RemoveAt(index);', 'Delete(int index) => live.RemoveAt(0);'),
    ('cards expire', 'public void Tick() { }', 'public void Tick() { if (live.Count > 0) live.RemoveAt(0); }'),
    ('text always localized', 'message.TextId == -1 ? host.DecodeText(message.InlineText) : host.GetText(message.TextId)', 'host.GetText(message.TextId)'),
    ('text always inline', 'message.TextId == -1 ? host.DecodeText(message.InlineText) : host.GetText(message.TextId)', 'host.DecodeText(message.InlineText)'),
    ('text lookup wrong id', 'host.GetText(message.TextId)', 'host.GetText(0)'),
    ('camera action absent', 'host.JumpToObject(message.Target); return true;', 'return true;'),
    ('camera loses object', 'host.JumpToObject(message.Target);', 'host.JumpToObject(null);'),
    ('camera leaves list open', 'host.JumpToObject(message.Target); return true;', 'host.JumpToObject(message.Target); return false;'),
    ('replay action absent', 'host.ReplayMessage(message.TextId);', '// replay omitted\n                '),
    ('replay wrong text id', 'host.ReplayMessage(message.TextId);', 'host.ReplayMessage(0);'),
    ('replay closes list', 'host.ReplayMessage(message.TextId); break;', 'host.ReplayMessage(message.TextId); return true;'),
    ('narrow activation kind', 'switch (message.Kind)', 'switch (unchecked((byte)message.Kind))'),
    ('save includes kind 4', 'if (message.Kind == 4) continue;', '// filter omitted'),
    ('save filters kind after narrowing', 'if (message.Kind == 4) continue;', 'if (unchecked((byte)message.Kind) == 4) continue;'),
    ('save maps every target kind', 'message.Kind == 2 && message.Target != null', 'message.Target != null'),
    ('save omits target mapping', 'target = host.LocateMessageTarget(message.Target);', 'target = (0, 0);'),
    ('save omits target index', 'target.Type, target.Index));', 'target.Type, 0));'),
    ('save loses inline bytes', 'message.TextId, message.InlineText.ToArray(),', 'message.TextId, Array.Empty<byte>(),'),
    ('save loses kind', 'unchecked((byte)message.Kind), target.Type', '(byte)0, target.Type'),
    ('save loses text id', 'new ParkSaveMessage(message.TextId,', 'new ParkSaveMessage(0,'),
    ('invalid save mapping accepted', 'if (target.Type < 0)', 'if (false)'),
    ('restore skips target lookup', 'if (saved.TargetType != -1)', 'if (false)'),
    ('restore wrong target index', 'host.ResolveMessageTarget(saved.TargetType, saved.TargetIndex)', 'host.ResolveMessageTarget(saved.TargetType, 0)'),
    ('restore loses inline bytes', 'PushInline(saved.Text, saved.Type, target)', 'PushInline(Array.Empty<byte>(), saved.Type, target)'),
    ('restore loses kind', 'Push(saved.StringId, saved.Type, target)', 'Push(saved.StringId, 0, target)'),
    ('restore loses target', 'Push(saved.StringId, saved.Type, target)', 'Push(saved.StringId, saved.Type)'),
    ('restore loses text id', 'Push(saved.StringId, saved.Type, target)', 'Push(0, saved.Type, target)'),
    ('restore drops voice-only id', 'return saved.StringId == -1 ?', 'if (saved.StringId == 0x124) return null;\n        return saved.StringId == -1 ?'),
]:
    change(name, old, new)

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
    with tempfile.TemporaryDirectory(prefix='messages-mutations-', dir=ROOT / 'tools') as directory:
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
    audit['summary']['planned'] = len(mutations)
    audit['summary']['unexecuted'] = len(mutations) - len(audit['mutations'])
    OUTPUT.write_text(json.dumps(audit, indent=2) + '\n')

with tempfile.TemporaryDirectory(prefix='messages-final-', dir=ROOT / 'tools') as directory:
    audit['final_full_suite'] = run(Path(directory), full=True)
OUTPUT.write_text(json.dumps(audit, indent=2) + '\n')
if len(audit['mutations']) != len(mutations) or audit['summary']['survived'] or audit['summary']['invalid'] or audit['final_full_suite']['exit_code']:
    raise SystemExit('Mutation audit is not green; inspect JSON.')
print(json.dumps(audit['summary']), flush=True)
