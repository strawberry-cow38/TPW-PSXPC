#!/usr/bin/env python3
"""Mutation sweep for the feature-stock port (FeatureStock.cs, the handyman's two stock helpers, and the
visitor's type-2 arm). One production mutation at a time; a test failure is required, a compiler error
does not count. Sources are restored in a top-level try/finally that also runs on SIGTERM, and the LAST
thing the run does is compare every touched file to its original byte for byte and print `git status`,
so a killed run cannot leave a mutated line on disk unnoticed. Results: findings/shop-stock-mutations.json.
Use --only TEXT to rerun a rule after strengthening a rejecting fixture.
"""
import argparse
import datetime
import json
from pathlib import Path
import re
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
MUTATIONS = []
def add(file, name, *pairs):
    """pairs: one or more (old, new) replacements applied together as ONE mutation."""
    MUTATIONS.append((file, name, list(pairs)))

F = 'FeatureStock.cs'
add(F, 'placement not full', ('Set(Full);\n            LastServicedDay = 0;', 'Set(Full - 1);\n            LastServicedDay = 0;'))
add(F, 'placement stamps a day', ('LastServicedDay = 0;', 'LastServicedDay = 1;'))
add(F, 'full is 99', ('Full = 100', 'Full = 99'))
add(F, 'set clamps an int not a byte', ('_level = unchecked((sbyte)value);', '_level = (sbyte)Math.Clamp(value, 0, 100);'))
add(F, 'set stores the raw byte', ('_level = unchecked((sbyte)value);\n            if (_level < 0) _level = 0;\n            if (_level > Full) _level = (sbyte)Full;', '_level = unchecked((sbyte)value);'))
add(F, 'set skips the sign clamp', ('if (_level < 0) _level = 0;', ';'))
add(F, 'set skips the upper clamp', ('if (_level > Full) _level = (sbyte)Full;', ';'))
add(F, 'set upper clamp off by one', ('if (_level > Full) _level = (sbyte)Full;', 'if (_level > Full + 1) _level = (sbyte)Full;'))
add(F, 'subtract reads units as an int', ('int d = _level - unchecked((sbyte)units);', 'int d = _level - units;'))
add(F, 'subtract adds', ('int d = _level - unchecked((sbyte)units);', 'int d = _level + unchecked((sbyte)units);'))
add(F, 'subtract clamps at full', ('_level = d >= 0 ? unchecked((sbyte)d) : (sbyte)0;', '_level = d >= 0 ? (sbyte)Math.Min(d, 100) : (sbyte)0;'))
add(F, 'subtract wraps below zero', ('_level = d >= 0 ? unchecked((sbyte)d) : (sbyte)0;', '_level = unchecked((sbyte)d);'))
add(F, 'subtract stores a wide difference', ('_level = d >= 0 ? unchecked((sbyte)d) : (sbyte)0;', '_level = d >= 0 ? (sbyte)Math.Min(d, sbyte.MaxValue) : (sbyte)0;'))
add(F, 'refill partial', ('Set(Full);\n            LastServicedDay = today;', 'Set(Full - 1);\n            LastServicedDay = today;'))
add(F, 'refill no stamp', ('LastServicedDay = today;', ';'))
add(F, 'refill stamps zero', ('LastServicedDay = today;', 'LastServicedDay = 0;'))
add(F, 'load restores the byte unclamped', ('s.Set(capacityByte);', 's._level = unchecked((sbyte)capacityByte);'))
add(F, 'load skips the byte', ('s.Set(capacityByte);', ';'))
add(F, 'load clamps the stamp', ('s.LastServicedDay = stampWord;', 's.LastServicedDay = Math.Max(0, stampWord);'))
add(F, 'dirtiness counts unusable features', ('if (!usable) continue;', ''))
add(F, 'dirtiness counts unusable but sums usable', ('if (!usable) continue;\n                count++;', 'count++;\n                if (!usable) continue;'))
add(F, 'dirtiness returns 100 for none', ('if (count == 0) return 0;', 'if (count == 0) return Full;'))
add(F, 'dirtiness divides by zero for none', ('if (count == 0) return 0;', ''))
add(F, 'dirtiness returns the mean', ('return (Full - sum / count) & 0xFFFF;', 'return (sum / count) & 0xFFFF;'))
add(F, 'dirtiness rounds the mean', ('return (Full - sum / count) & 0xFFFF;', 'return (Full - (sum + count / 2) / count) & 0xFFFF;'))
add(F, 'dirtiness no 16-bit mask', ('return (Full - sum / count) & 0xFFFF;', 'return Full - sum / count;'))
add(F, 'dirtiness 32-bit sum', ('short sum = 0;', 'int sum = 0;'), ('sum = unchecked((short)(sum + level));', 'sum = sum + level;'))
add(F, 'panel average rounds', ('return (scaled / levels.Count) >> 16;', 'return (sum + levels.Count / 2) / levels.Count;'))
add(F, 'panel average divides an empty list', ('if (scaled == 0) return 0;', ''))
add(F, 'panel average returns the complement', ('return (scaled / levels.Count) >> 16;', 'return Full - ((scaled / levels.Count) >> 16);'))
add(F, 'panel average skips the first entry', ('for (int i = 0; i < levels.Count; i++) sum += levels[i];', 'for (int i = 1; i < levels.Count; i++) sum += levels[i];'))

S = 'StaffClasses.cs'
add(S, 'bin pick threshold 59', ('BinPickBelow = 60', 'BinPickBelow = 59'))
add(S, 'bin pick inclusive', ('remaining < BinPickBelow', 'remaining <= BinPickBelow'))
add(S, 'bin pick uses the neglect threshold', ('remaining < BinPickBelow', 'remaining < NeglectedBinRemaining'))
add(S, 'bin score unweighted distance', ('(Math.Abs(dx) + Math.Abs(dy)) * (remaining + 1)', '(Math.Abs(dx) + Math.Abs(dy))'))
add(S, 'bin score binary y-only weighting (deliberately rejected pending review)', ('(Math.Abs(dx) + Math.Abs(dy)) * (remaining + 1)', 'Math.Abs(dx) + Math.Abs(dy) * (remaining + 1)'))
add(S, 'bin score signed distance', ('(Math.Abs(dx) + Math.Abs(dy)) * (remaining + 1)', '(dx + dy) * (remaining + 1)'))
add(S, 'bin score remaining without plus one', ('(Math.Abs(dx) + Math.Abs(dy)) * (remaining + 1)', '(Math.Abs(dx) + Math.Abs(dy)) * remaining'))

Q = 'VisitorQueue.cs'
add(Q, 'feature ignores the usable flag', ('if (!world.TargetHasStock(guest))\n            {\n                guest.HasTarget = false;', 'if (false)\n            {\n                guest.HasTarget = false;'))
add(Q, 'feature consume threshold 61', ('FeatureUseAbove = 60', 'FeatureUseAbove = 61'))
add(Q, 'feature consume gate inclusive', ('if (guest.RideDesire > FeatureUseAbove)', 'if (guest.RideDesire >= FeatureUseAbove)'))
add(Q, 'feature consume formula inverted', ('(guest.RideDesire - FeatureUseAbove) * 2 / 3', '(guest.RideDesire - FeatureUseAbove) * 3 / 2'))
add(Q, 'feature skips the consume', ('world.ConsumeStock(guest, (guest.RideDesire - FeatureUseAbove) * 2 / 3);', ';'))
add(Q, 'feature low stock inclusive', ('world.StockLevel(guest) < FeatureLowStock', 'world.StockLevel(guest) <= FeatureLowStock'))
add(Q, 'feature low stock threshold 49', ('FeatureLowStock = 50', 'FeatureLowStock = 49'))
add(Q, 'feature low stock no penalty', ('guest.Happiness = Stat.Sub(guest.Happiness, FeatureLowStockPenalty);', ';'))

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--only', action='append', default=[])
    args = parser.parse_args()
    selected = [m for m in MUTATIONS if not args.only or any(term in m[1] for term in args.only)]
    if not selected: raise SystemExit('no matching mutations')
    files = {name: ROOT / 'core/TPW.Sim' / name for name, *_ in selected}
    originals = {name: path.read_bytes() for name, path in files.items()}
    logs = Path(tempfile.mkdtemp(prefix='shop-stock-mutations-'))
    command = ['dotnet', 'test', 'tests/TPW.Sim.Tests/', '--no-restore', '--nologo',
               '--filter', 'FullyQualifiedName~FeatureStock|FullyQualifiedName~Handyman|FullyQualifiedName~VisitorQueue']
    outcomes = []
    print(f'{len(selected)} mutations; logs: {logs}', flush=True)

    def restore_all():
        for name, path in files.items(): path.write_bytes(originals[name])
    def on_term(signum, frame):
        raise SystemExit(f'signal {signum}')
    signal.signal(signal.SIGTERM, on_term)
    signal.signal(signal.SIGHUP, on_term)

    try:
        baseline = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=300)
        (logs / 'baseline.log').write_text(baseline.stdout + baseline.stderr)
        if baseline.returncode: raise RuntimeError('baseline is not green')
        for index, (name, label, pairs) in enumerate(selected, 1):
            original = originals[name].decode()
            mutated = original
            for old, new in pairs:
                n = mutated.count(old)
                if n != 1: raise RuntimeError(f'mutation anchor count {n} for {label!r}: {old[:50]!r}')
                mutated = mutated.replace(old, new)
            try:
                files[name].write_text(mutated)
                result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=300)
                output = result.stdout + result.stderr
                status = ('KILLED' if result.returncode and re.search(r'Failed:\s+[1-9]', output)
                          else 'SURVIVED' if result.returncode == 0 else 'INVALID')
                (logs / f'{index:03}.log').write_text(label + '\n' + output)
                failing = re.search(r'^\s+Failed (.+?) \[', output, re.MULTILINE)
                outcomes.append(dict(file=name, rule=label, status=status, executions=1,
                                     example_failing_test=failing.group(1) if failing else None))
                print(f'{index}/{len(selected)} {status}: {label}', flush=True)
            finally:
                files[name].write_bytes(originals[name])
    finally:
        restore_all()
        summary = dict(date=datetime.date.today().isoformat(),
                       distinct_mutations=len(outcomes), executions=len(outcomes),
                       killed=sum(o['status'] == 'KILLED' for o in outcomes),
                       survived=sum(o['status'] == 'SURVIVED' for o in outcomes),
                       invalid=sum(o['status'] == 'INVALID' for o in outcomes),
                       not_run=len(selected) - len(outcomes),
                       filter=command[-1], mutations=outcomes)
        (ROOT / 'findings/shop-stock-mutations.json').write_text(json.dumps(summary, indent=2) + '\n')
        # The last thing this run does: prove the tree holds no mutated line.
        dirty = [name for name, path in files.items() if path.read_bytes() != originals[name]]
        status = subprocess.run(['git', 'status', '--short'], cwd=ROOT, capture_output=True, text=True)
        print('--- git status --short (must show only intended changes):', flush=True)
        print(status.stdout, flush=True)
        if dirty:
            print(f'!!! MUTATED FILES STILL ON DISK: {dirty}', flush=True)
            sys.exit(3)
        print('restore check: every mutated source is byte-identical to its original', flush=True)
    bad = [m for m in outcomes if m['status'] != 'KILLED']
    print(f"{len(outcomes)-len(bad)}/{len(outcomes)} killed", flush=True)
    if bad or len(outcomes) != len(selected): raise SystemExit(1)

if __name__ == '__main__': main()
