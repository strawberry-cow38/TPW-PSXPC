#!/usr/bin/env python3
"""Mutation sweep for the guest needs clock (findings/needs.md §8).

One production mutation at a time; a test failure is required, a compiler error does not count
(INVALID). Sources are restored in `finally`. Results merge into findings/needs-mutations.json so
the sweep can be run in ranges (--from/--to) or re-run for one rule (--only TEXT).
"""
import argparse, json, re, subprocess, sys, datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'core' / 'TPW.Sim'
OUT = ROOT / 'findings' / 'needs-mutations.json'
MUTATIONS = []
def add(file, name, old, new, occurrence=0):
    MUTATIONS.append((file, name, old, new, occurrence))

C = 'VisitorCondition.cs'
PAIR = 'if (guest.NeedA > NeedAAbove && guest.NeedB > NeedAAbove) return GuestCondition.BothNeedsHigh;'
for name, old, new in [
    ('needA threshold 80', 'NeedAAbove = 80', 'NeedAAbove = 81'),
    ('needB alone threshold 75', 'NeedBAloneAbove = 75', 'NeedBAloneAbove = 80'),
    ('others threshold 75', 'OthersAbove = 75', 'OthersAbove = 76'),
    ('miserable threshold 25', 'MiserableBelow = 25', 'MiserableBelow = 26'),
    ('pair uses needB alone threshold', PAIR, PAIR.replace('guest.NeedB > NeedAAbove', 'guest.NeedB > NeedBAloneAbove')),
    ('pair is either need', PAIR, PAIR.replace('&&', '||')),
    ('pair dropped', PAIR, ''),
    ('needA non-strict', 'if (guest.NeedA > NeedAAbove) return', 'if (guest.NeedA >= NeedAAbove) return'),
    ('needB non-strict', 'if (guest.NeedB > NeedBAloneAbove) return', 'if (guest.NeedB >= NeedBAloneAbove) return'),
    ('toilet non-strict', 'if (guest.RideDesire > OthersAbove) return', 'if (guest.RideDesire >= OthersAbove) return'),
    ('nausea non-strict', 'if (guest.Nausea > OthersAbove) return', 'if (guest.Nausea >= OthersAbove) return'),
    ('happy non-strict', 'if (guest.Happiness > OthersAbove) return', 'if (guest.Happiness >= OthersAbove) return'),
    ('tired non-strict', 'if (guest.Tiredness > OthersAbove) return', 'if (guest.Tiredness >= OthersAbove) return'),
    ('miserable non-strict', 'if (guest.Happiness < MiserableBelow) return', 'if (guest.Happiness <= MiserableBelow) return'),
    ('order needB before needA',
     'if (guest.NeedA > NeedAAbove) return GuestCondition.NeedAHigh;\n            if (guest.NeedB > NeedBAloneAbove) return GuestCondition.NeedBHigh;',
     'if (guest.NeedB > NeedBAloneAbove) return GuestCondition.NeedBHigh;\n            if (guest.NeedA > NeedAAbove) return GuestCondition.NeedAHigh;'),
    ('order nausea before toilet',
     'if (guest.RideDesire > OthersAbove) return GuestCondition.RideDesireHigh;\n            if (guest.Nausea > OthersAbove) return GuestCondition.NauseaHigh;',
     'if (guest.Nausea > OthersAbove) return GuestCondition.NauseaHigh;\n            if (guest.RideDesire > OthersAbove) return GuestCondition.RideDesireHigh;'),
    ('order happy before nausea',
     'if (guest.Nausea > OthersAbove) return GuestCondition.NauseaHigh;\n            if (guest.Happiness > OthersAbove) return GuestCondition.HappinessHigh;',
     'if (guest.Happiness > OthersAbove) return GuestCondition.HappinessHigh;\n            if (guest.Nausea > OthersAbove) return GuestCondition.NauseaHigh;'),
    ('order tired before happy',
     'if (guest.Happiness > OthersAbove) return GuestCondition.HappinessHigh;\n            if (guest.Tiredness > OthersAbove) return GuestCondition.TirednessHigh;',
     'if (guest.Tiredness > OthersAbove) return GuestCondition.TirednessHigh;\n            if (guest.Happiness > OthersAbove) return GuestCondition.HappinessHigh;'),
    ('order miserable before tired',
     'if (guest.Tiredness > OthersAbove) return GuestCondition.TirednessHigh;\n            if (guest.Happiness < MiserableBelow) return GuestCondition.HappinessLow;',
     'if (guest.Happiness < MiserableBelow) return GuestCondition.HappinessLow;\n            if (guest.Tiredness > OthersAbove) return GuestCondition.TirednessHigh;'),
    ('boredom code invented', '            return GuestCondition.None;\n        }',
     '            if (guest.Boredom > OthersAbove) return GuestCondition.NeedBHigh;\n            return GuestCondition.None;\n        }'),
    ('icon needB/needA swapped', '0x32, 0x34', '0x34, 0x32'),
    ('icon toilet/nausea swapped', '0x3B, 0x3D', '0x3D, 0x3B'),
    ('icon happy/tired swapped', '0x3E, 0x40', '0x40, 0x3E'),
    ('icon both/miserable swapped', '0x33, 0x35', '0x35, 0x33'),
    ('icon none nonzero', 'IconByCode = { 0x00,', 'IconByCode = { 0x01,'),
    ('percent rounds', 'return matching * 100 / total;', 'return (matching * 100 + total / 2) / total;'),
    ('percent divides by zero guests', 'if (total == 0) return 0;', ''),
    ('percent counts the pair as both', 'if (Of(g) == code) matching++;',
     'if (Of(g) == code || (code != GuestCondition.None && Of(g) == GuestCondition.BothNeedsHigh)) matching++;'),
    ('top three ties to higher code', 'if (best < counts[code])', 'if (best <= counts[code])'),
    ('top three includes none', 'for (int code = 1; code < counts.Length; code++)', 'for (int code = 0; code < counts.Length; code++)'),
    ('top three never clears winner', 'counts[bestCode] = 0;', ';'),
    ('top three pads with empty', 'if (best == 0) continue;', ''),
    ('window slots 3', 'WindowSlots = 3', 'WindowSlots = 4'),
]: add(C, name, old, new)

N = 'VisitorNeeds.cs'
GA = 'if (now % NeedAPeriod == 0) guest.NeedA = Stat.Add(guest.NeedA, rng.Next(GrowthRollMax));'
GB = 'if (now % NeedBPeriod == 0) guest.NeedB = Stat.Add(guest.NeedB, rng.Next(GrowthRollMax));'
for name, old, new in [
    ('growth die rand(2)', 'GrowthRollMax = 2', 'GrowthRollMax = 3'),
    ('growth A is die plus one', GA, GA.replace('rng.Next(GrowthRollMax)', 'rng.Next(GrowthRollMax) + 1')),
    ('growth B is a flat point', GB, GB.replace('rng.Next(GrowthRollMax)', '1')),
    ('needA period 50', 'NeedAPeriod = 50', 'NeedAPeriod = 49'),
    ('needB period 40', 'NeedBPeriod = 40', 'NeedBPeriod = 39'),
    ('growth A lands on boredom', GA, GA.replace('guest.NeedA = Stat.Add(guest.NeedA', 'guest.Boredom = Stat.Add(guest.Boredom')),
    ('growth B lands on toilet', GB, GB.replace('guest.NeedB = Stat.Add(guest.NeedB', 'guest.RideDesire = Stat.Add(guest.RideDesire')),
    ('growth A skips the die at the cap', GA, GA.replace('if (now % NeedAPeriod == 0)', 'if (now % NeedAPeriod == 0 && guest.NeedA < 100)')),
    ('growth only while idle', GA, GA.replace('if (now % NeedAPeriod == 0)', 'if (now % NeedAPeriod == 0 && guest.State == VisitorState.Idle)')),
    ('tiredness drift invented', GB, GB + '\n            if (now % NeedBPeriod == 0) guest.Tiredness = Stat.Add(guest.Tiredness, 1);'),
    ('nausea drift invented', GB, GB + '\n            if (now % LitterPeriod == 0) guest.Nausea = Stat.Add(guest.Nausea, 1);'),
    ('vomit nausea 3', 'VomitNausea = 3', 'VomitNausea = 2'),
    ('litter happiness 3', 'LitterHappiness = 3', 'LitterHappiness = 2'),
    ('boredom penalty 95', 'guest.Boredom >= 95', 'guest.Boredom >= 96'),
    ('nausea penalty 85', 'guest.Nausea >= 85', 'guest.Nausea >= 86'),
    ('toilet penalty 90', 'guest.RideDesire >= 90', 'guest.RideDesire >= 91'),
    ('needA penalty 95', 'guest.NeedA >= 95', 'guest.NeedA >= 94'),
    ('needB penalty 85', 'guest.NeedB >= 85', 'guest.NeedB >= 86'),
    ('unpleasant standing nausea 2', 'walking ? 5 : 2', 'walking ? 5 : 5'),
    ('unpleasant standing happiness 1', 'walking ? 3 : 1', 'walking ? 3 : 3'),
    ('pleasant happiness 6', 'Stat.Add(guest.Happiness, 6)', 'Stat.Add(guest.Happiness, 5)'),
]: add(N, name, old, new)

for name, old, new in [
    ('walk tiredness chance 1 in 10', 'TirednessChanceIn = 10', 'TirednessChanceIn = 9'),
    ('walk tiredness +1', 'if (rng.Next(TirednessChanceIn) == 0) guest.Tiredness = Stat.Add(guest.Tiredness, 1);',
     'if (rng.Next(TirednessChanceIn) == 0) guest.Tiredness = Stat.Add(guest.Tiredness, 2);'),
    ('walk tiredness every tick', 'if (rng.Next(TirednessChanceIn) == 0) guest.Tiredness', 'if (rng.Next(TirednessChanceIn) >= 0) guest.Tiredness'),
]: add('VisitorArrival.cs', name, old, new)

Q = 'VisitorQueue.cs'
for name, old, new in [
    ('queue boredom roll below 2', 'BoredomRollBelow = 2', 'BoredomRollBelow = 3'),
    ('queue tiredness roll below 10', 'TirednessRollBelow = 10', 'TirednessRollBelow = 11'),
    ('unload tiredness rand(20)', 'UnloadTirednessMax = 20', 'UnloadTirednessMax = 21'),
    ('ride nausea from 56', 'NauseaFromIntensity = 56', 'NauseaFromIntensity = 55'),
    ('ride nausea scale 1212', 'NauseaScale = 1212', 'NauseaScale = 1213'),
    ('ride nausea scale off by a tenth', 'NauseaScale = 1212', 'NauseaScale = 1100'),
    ('ride nausea base 30', 'NauseaBase = 30', 'NauseaBase = 29'),
    ('ride boredom drop scale 4096', 'BoredomDropScale = 4096', 'BoredomDropScale = 4095'),
    ('feature nausea relief 40', 'FeatureNauseaRelief = 40', 'FeatureNauseaRelief = 39'),
    ('feature low stock penalty 10', 'FeatureLowStockPenalty = 10', 'FeatureLowStockPenalty = 9'),
    ('feature use above 60', 'FeatureUseAbove = 60', 'FeatureUseAbove = 61'),
    ('feature keeps toilet need', 'guest.RideDesire = 0;', ';'),
    ('queue boredom leave 80', 'BoredomLeaveThreshold = 80', 'BoredomLeaveThreshold = 81'),
    ('queue broken boredom +1 dropped', 'guest.Boredom = Stat.Add(guest.Boredom, 1);', ';'),
    ('queue tiredness adds instead of subtracts', 'guest.Tiredness = Stat.Sub(guest.Tiredness, rng.Next(100) < TirednessRollBelow ? 1 : 0);',
     'guest.Tiredness = Stat.Add(guest.Tiredness, rng.Next(100) < TirednessRollBelow ? 1 : 0);'),
    ('ride nausea shift 12', '(NauseaScale * (intensity - NauseaBase)) >> 12', '(NauseaScale * (intensity - NauseaBase)) >> 11'),
    ('unload tiredness dropped', 'guest.Tiredness = Stat.Sub(guest.Tiredness, rng.Next(UnloadTirednessMax));', ';'),
    ('feature nausea relief dropped', 'guest.Nausea = Stat.Sub(guest.Nausea, FeatureNauseaRelief);', ';'),
]: add(Q, name, old, new)

add('VisitorDecision.cs', 'no ride boredom +5', 'guest.Boredom = Stat.Add(guest.Boredom, 5);', 'guest.Boredom = Stat.Add(guest.Boredom, 4);')
add('VisitorEntrance.cs', 'path failure boredom rand(2)', 'WanderBoredomGainMax = 2', 'WanderBoredomGainMax = 3')
add('VisitorEntrance.cs', 'path failure boredom dropped', 'guest.Boredom = Stat.Add(guest.Boredom, rng.Next(WanderBoredomGainMax));', ';')
add('VisitorActivity.cs', 'vomit keeps nausea', 'guest.Nausea = 0;', ';')
add('VisitorIdle.cs', 'vomit threshold 92', 'VomitNausea = 92', 'VomitNausea = 91')
P = 'VisitorPurchase.cs'
for name, old, new in [
    ('food adds needA', 'guest.NeedA = Stat.Sub(guest.NeedA, p.NeedAValue);', 'guest.NeedA = Stat.Add(guest.NeedA, p.NeedAValue);'),
    ('food skips toilet need', 'guest.RideDesire = Stat.Add(guest.RideDesire, p.NeedAValue);', ';'),
    ('drink adds needB', 'guest.NeedB = Stat.Sub(guest.NeedB, p.NeedBValue);', 'guest.NeedB = Stat.Add(guest.NeedB, p.NeedBValue);'),
    ('drink skips toilet need', 'guest.RideDesire = Stat.Add(guest.RideDesire, p.NeedBValue);', ';'),
    ('food skips nausea', 'guest.Nausea = Stat.Add(guest.Nausea, p.NauseaValue);', ';'),
    ('food skips needB', 'guest.NeedB = Stat.Add(guest.NeedB, p.NeedBValue);', ';'),
]: add(P, name, old, new)
add('GuestSpending.cs', 'fries second slider /15', 'SecondSliderDivisor = 15', 'SecondSliderDivisor = 14')

def run_tests():
    r = subprocess.run(['dotnet', 'test', str(ROOT / 'tests' / 'TPW.Sim.Tests')], capture_output=True, text=True, cwd=ROOT)
    out = r.stdout + r.stderr
    if 'error CS' in out or 'Build FAILED' in out: return 'INVALID', None
    m = re.search(r'Failed (TPW\.Sim\.Tests\.[^\[\n]+?) \[', out)
    if 'Failed!' in out or m: return 'KILLED', (m.group(1) if m else 'unnamed')
    if 'Passed!' in out: return 'SURVIVED', None
    return 'INVALID', None

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--only'); ap.add_argument('--from', dest='lo', type=int, default=0); ap.add_argument('--to', dest='hi', type=int, default=len(MUTATIONS))
    ap.add_argument('--list', action='store_true')
    a = ap.parse_args()
    if a.list:
        for i, m in enumerate(MUTATIONS): print(i, m[0], m[1])
        return
    prev = json.loads(OUT.read_text()) if OUT.exists() else {'mutations': []}
    results = {(m['file'], m['rule']): m for m in prev.get('mutations', [])}
    for i, (file, name, old, new, occ) in enumerate(MUTATIONS):
        if i < a.lo or i >= a.hi: continue
        if a.only and a.only not in name: continue
        path = SRC / file; original = path.read_text()
        parts = original.split(old)
        if len(parts) < occ + 2:
            print(f'[{i}] {file}: {name}: pattern not found'); results[(file, name)] = {'file': file, 'rule': name, 'status': 'INVALID', 'note': 'pattern not found'}; continue
        mutated = old.join(parts[:occ + 1]) + new + old.join(parts[occ + 1:])
        try:
            path.write_text(mutated)
            status, example = run_tests()
        finally:
            path.write_text(original)
        assert path.read_text() == original
        entry = {'file': file, 'rule': name, 'status': status}
        if example: entry['example_failing_test'] = example
        e = results.get((file, name)); entry['executions'] = (e.get('executions', 0) if e else 0) + 1
        results[(file, name)] = entry
        print(f'[{i}] {file}: {name}: {status}' + (f' ({example})' if example else ''), flush=True)
    ordered = [results[(m[0], m[1])] for m in MUTATIONS if (m[0], m[1]) in results]
    summary = {
        'date': datetime.date.today().isoformat(),
        'runner': 'tools/mutate_needs.py',
        'distinct_mutations': len(ordered),
        'executions': sum(m.get('executions', 1) for m in ordered),
        'killed': sum(m['status'] == 'KILLED' for m in ordered),
        'survived': sum(m['status'] == 'SURVIVED' for m in ordered),
        'invalid': sum(m['status'] == 'INVALID' for m in ordered),
        'not_run': len(MUTATIONS) - len(ordered),
        'mutations': ordered,
    }
    OUT.write_text(json.dumps(summary, indent=2) + '\n')

if __name__ == '__main__': main()
