#!/usr/bin/env python3
"""Mutation sweep for every writer and reader of guest happiness (findings/happiness.md §7).

Same shape as mutate_needs.py: one production mutation at a time, a real failing test required
(a compiler error is INVALID, not a kill), sources restored in `finally`, results merged into
findings/happiness-mutations.json so ranges (--from/--to) and single rules (--only TEXT) re-run.
"""
import argparse, json, re, subprocess, sys, datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'core' / 'TPW.Sim'
OUT = ROOT / 'findings' / 'happiness-mutations.json'
MUTATIONS = []
def add(file, name, old, new, occurrence=0):
    MUTATIONS.append((file, name, old, new, occurrence))

# ── the new port: 0x800670D4 and the ring accessor ──
H = 'ParkHistory.cs'
MEAN = 'return count == 0 ? 0 : sum / count;'
GUARD = 'if (!(unchecked((uint)n) < unchecked((uint)months))) return -1;\n            if (n == 0) n = 1;'
for name, old, new in [
    ('ring has 144 slots', 'Slots = 144', 'Slots = 145'),
    ('mean rounds to nearest', MEAN, 'return count == 0 ? 0 : (sum + count / 2) / count;'),
    ('mean of an empty park is 50', MEAN, 'return count == 0 ? 50 : sum / count;'),
    ('slot is the post-increment month count', 'int slot = monthsBefore % Slots;', 'int slot = (monthsBefore + 1) % Slots;'),
    ('new-year rating taken every january', 'if (year != 0 && month == 0)', 'if (month == 0)'),
    ('new-year rating taken on the year change alone', 'if (year != 0 && month == 0)', 'if (year != 0)'),
    ('people saturates instead of wrapping', '_people[slot] = (byte)count;', '_people[slot] = (byte)Math.Min(count, 255);'),
    ('time in park saturates instead of wrapping', '_timeInPark[slot] = (byte)(count == 0 ? 0 : timeSum / count);',
     '_timeInPark[slot] = (byte)Math.Min(255, count == 0 ? 0 : timeSum / count);'),
    ('arrival reads the previous slot directly', 'int previous = Read(HistoryRow.People, monthsBefore, 1);',
     'int previous = _people[(slot + Slots - 1) % Slots];'),
    ('arrival is a signed delta', '_arrivalRate[slot] = (byte)Math.Max(0, count - previous);', '_arrivalRate[slot] = (byte)(count - previous);'),
    ('arrival is the head count', '_arrivalRate[slot] = (byte)Math.Max(0, count - previous);', '_arrivalRate[slot] = (byte)count;'),
    ('happiness row carries forward when empty', '_happiness[slot] = (byte)(count == 0 ? 0 : happinessSum / count);',
     '_happiness[slot] = (byte)(count == 0 ? _happiness[(slot + Slots - 1) % Slots] : happinessSum / count);'),
    ('happiness row is the sum', '_happiness[slot] = (byte)(count == 0 ? 0 : happinessSum / count);', '_happiness[slot] = (byte)happinessSum;'),
    ('time in park measured from day 0', 'timeSum += totalDays - g.ArrivedOnDay;', 'timeSum += totalDays;'),
    ('overall row not stored', '_overall[slot] = (byte)rating;', ';'),
    ('accessor guards after the bump', GUARD, 'if (n == 0) n = 1;\n            if (!(unchecked((uint)n) < unchecked((uint)months))) return -1;'),
    ('accessor allows n == months', '< unchecked((uint)months))) return -1;', '<= unchecked((uint)months))) return -1;'),
    ('accessor n=0 is this month', 'if (n == 0) n = 1;', ';'),
    ('accessor clamps instead of wrapping', 'while (idx < 0) idx += Slots;', 'if (idx < 0) idx = 0;'),
]: add(H, name, old, new)

# ── the ride reward (state 22) and the dirty feature ──
Q = 'VisitorQueue.cs'
BANDS = 'difference < ExcellentBelow ? RideExcellentHappiness\n             : difference < GoodBelow ? RideGoodHappiness'
for name, old, new in [
    ('ride excellent +15', 'RideExcellentHappiness = 15', 'RideExcellentHappiness = 16'),
    ('ride good +10', 'RideGoodHappiness = 10', 'RideGoodHappiness = 11'),
    ('ride ok +5', 'RideOkHappiness = 5', 'RideOkHappiness = 6'),
    ('excellent band below 21', 'ExcellentBelow = 21', 'ExcellentBelow = 22'),
    ('good band below 51', 'GoodBelow = 51', 'GoodBelow = 52'),
    ('ride bands swapped', BANDS, BANDS.replace('RideExcellentHappiness', 'TMP').replace('RideGoodHappiness', 'RideExcellentHappiness').replace('TMP', 'RideGoodHappiness')),
    ('ride reward dropped', 'guest.Happiness = Stat.Add(guest.Happiness, RideHappiness(m));', ';'),
    ('ride mismatch is signed', 'int m = Math.Abs(RideScore.Preference(guest) - intensity);', 'int m = RideScore.Preference(guest) - intensity;'),
    ('dirty feature -10', 'FeatureLowStockPenalty = 10', 'FeatureLowStockPenalty = 9'),
    ('dirty feature happiness dropped', 'guest.Happiness = Stat.Sub(guest.Happiness, FeatureLowStockPenalty);', ';'),
]: add(Q, name, old, new)

# ── the entertainer, the failed route, the target choice ──
add('VisitorActivity.cs', 'entertainer +5', 'WatchHappiness = 5', 'WatchHappiness = 4')
add('VisitorActivity.cs', 'entertainer reward dropped', 'guest.Happiness = Stat.Add(guest.Happiness, WatchHappiness);', ';')
E = 'VisitorEntrance.cs'
PATHLOSS = 'guest.Happiness = Stat.Sub(guest.Happiness, rng.Next(WanderHappinessLossMax));'
add(E, 'failed route rand(15)', 'WanderHappinessLossMax = 15', 'WanderHappinessLossMax = 16')
add(E, 'failed route loss dropped', PATHLOSS, ';')
add(E, 'failed route loss is flat 15', PATHLOSS, 'guest.Happiness = Stat.Sub(guest.Happiness, WanderHappinessLossMax);')
add(E, 'failed route loss is a gain', PATHLOSS, 'guest.Happiness = Stat.Add(guest.Happiness, rng.Next(WanderHappinessLossMax));')
D = 'VisitorDecision.cs'
for name, old, new in [
    ('nothing to do -10', 'guest.Happiness = Stat.Sub(guest.Happiness, 10);', 'guest.Happiness = Stat.Sub(guest.Happiness, 9);'),
    ('poor choice -5', 'guest.Happiness = Stat.Sub(guest.Happiness, 5);', 'guest.Happiness = Stat.Sub(guest.Happiness, 4);'),
    ('poor choice below 8', 'PoorChoiceScore = 8', 'PoorChoiceScore = 9'),
    ('shop bonus non-strict', 'if (thr >= 0 && guest.Happiness > thr)', 'if (thr >= 0 && guest.Happiness >= thr)'),
    ('shop bonus not rescaled', 's4 = (guest.Happiness - thr) * 100 / (100 - thr);', 's4 = (guest.Happiness - thr);'),
    ('shop bonus weight 3', 'w4 = 3;', 'w4 = 2;'),
    ('kind 6 uses the kind 2/3 threshold', 'a.ProductKind == 6 ? VisitorTables.ShopBonusThresholdKind6', 'a.ProductKind == 6 ? VisitorTables.ShopBonusThresholdKind23'),
]: add(D, name, old, new)
add('VisitorTables.cs', 'shop bonus threshold 70', 'ShopBonusThresholdKind23 = 70', 'ShopBonusThresholdKind23 = 71')
add('VisitorTables.cs', 'shop bonus threshold 75', 'ShopBonusThresholdKind6 = 75', 'ShopBonusThresholdKind6 = 76')

# ── Idle: leave, the unhappy exit, the misery litter ──
I = 'VisitorIdle.cs'
for name, old, new in [
    ('leave below 5', 'LeaveHappiness = 5', 'LeaveHappiness = 6'),
    ('leave non-strict', 'if (guest.Happiness < LeaveHappiness) return true;', 'if (guest.Happiness <= LeaveHappiness) return true;'),
    ('unhappy exit bubble below 3', 'UnhappyExitHappiness = 3', 'UnhappyExitHappiness = 4'),
    ('unhappy exit bubble dropped', 'if (guest.Happiness < UnhappyExitHappiness) guest.Bubble = BubbleLeftUnhappy;', ';'),
    ('misery litter below 25', 'LitterHappiness = 25', 'LitterHappiness = 26'),
    ('misery litter 100 in 1000', 'rng.Next(1000) < 100', 'rng.Next(1000) < 101'),
]: add(I, name, old, new)

# ── the needs pass: influence, litter, the five penalties, the bubbles ──
N = 'VisitorNeeds.cs'
BUBBLES = ('if (guest.Happiness > 90) { guest.Bubble = BubbleDelighted; return; }\n'
           '            if (guest.Happiness < 10) { guest.Bubble = BubbleMiserable; return; }\n'
           '            if (guest.Happiness > 80) { guest.Bubble = BubbleHappy; return; }')
for name, old, new in [
    ('pleasant tile +6', 'Stat.Add(guest.Happiness, 6)', 'Stat.Add(guest.Happiness, 7)'),
    ('unpleasant tile walking -3', 'walking ? 3 : 1', 'walking ? 2 : 1'),
    ('unpleasant tile standing -1', 'walking ? 3 : 1', 'walking ? 3 : 2'),
    ('litter -3 per piece', 'LitterHappiness = 3', 'LitterHappiness = 4'),
    ('litter once regardless of count', 'LitterHappiness * litter', 'LitterHappiness'),
    ('boredom penalty -1', 'if (guest.Boredom >= 95) guest.Happiness = Stat.Sub(guest.Happiness, 1);', 'if (guest.Boredom >= 95) guest.Happiness = Stat.Sub(guest.Happiness, 2);'),
    ('needB penalty dropped', 'if (guest.NeedB >= 85) guest.Happiness = Stat.Sub(guest.Happiness, 1);', ';'),
    ('delighted bubble non-strict', 'if (guest.Happiness > 90)', 'if (guest.Happiness >= 90)'),
    ('miserable bubble non-strict', 'if (guest.Happiness < 10)', 'if (guest.Happiness <= 10)'),
    ('happy bubble non-strict', 'if (guest.Happiness > 80)', 'if (guest.Happiness >= 80)'),
    ('content bubble lower bound non-strict', 'guest.Happiness > 25 && guest.Happiness < 75', 'guest.Happiness >= 25 && guest.Happiness < 75'),
    ('content bubble upper bound non-strict', 'guest.Happiness > 25 && guest.Happiness < 75', 'guest.Happiness > 25 && guest.Happiness <= 75'),
    ('happy bubble tested before delighted', BUBBLES,
     'if (guest.Happiness > 80) { guest.Bubble = BubbleHappy; return; }\n'
     '            if (guest.Happiness > 90) { guest.Bubble = BubbleDelighted; return; }\n'
     '            if (guest.Happiness < 10) { guest.Bubble = BubbleMiserable; return; }'),
]: add(N, name, old, new)

# ── the condition code's two happiness tests ──
add('VisitorCondition.cs', 'condition happy non-strict', 'if (guest.Happiness > OthersAbove) return', 'if (guest.Happiness >= OthersAbove) return')
add('VisitorCondition.cs', 'condition miserable below 25', 'MiserableBelow = 25', 'MiserableBelow = 24')

# ── what a guest will pay, and what a purchase pays back ──
G = 'GuestSpending.cs'
for name, old, new in [
    ('sideshow step 10', 'SideShowHappinessStep = 10', 'SideShowHappinessStep = 9'),
    ('want ignores happiness', '=> wantBase * needFactor / 100 * (happiness + 100) / 100;', '=> wantBase * needFactor / 100;'),
    ('want multiplies by happiness alone', '=> wantBase * needFactor / 100 * (happiness + 100) / 100;', '=> wantBase * needFactor / 100 * happiness / 100;'),
    ('need factor uses happiness not misery', '+ (100 - happiness) * p.HappinessValue / 100;', '+ happiness * p.HappinessValue / 100;'),
    ('need factor drops the happiness term', '+ (100 - happiness) * p.HappinessValue / 100;', ';'),
    ('sideshow want ignores happiness', 'return expected * (SideShowValueGlobal + 100) / 100 * (happiness + 100) / 100;', 'return expected * (SideShowValueGlobal + 100) / 100;'),
    ('satisfaction not clamped at zero', 'public static int SatisfactionAmount(int fiveTimesDelta) => Math.Max(0, fiveTimesDelta);', 'public static int SatisfactionAmount(int fiveTimesDelta) => fiveTimesDelta;'),
    ('purchase gain: food ignores the second slider', '=> food ? happinessValue * (quality - second / SecondSliderDivisor) / 100', '=> food ? happinessValue * quality / 100'),
    ('purchase gain: non-food uses the second slider', ': happinessValue * quality / 100;', ': happinessValue * (quality - second / SecondSliderDivisor) / 100;'),
    ('purchase gain: not divided by 100', ': happinessValue * quality / 100;', ': happinessValue * quality / 10;'),
]: add(G, name, old, new)
P = 'VisitorPurchase.cs'
FOODGAIN = 'guest.Happiness = Stat.Add(guest.Happiness, GuestSpending.HappinessGain(p.HappinessValue, quality, second, food: true));'
for name, old, new, occ in [
    ('sideshow happiness always +10', 'GuestSpending.SideShowHappinessStep * Math.Sign(play.Net)', 'GuestSpending.SideShowHappinessStep', 0),
    ('sideshow happiness follows the payout', 'GuestSpending.SideShowHappinessStep * Math.Sign(play.Net)', 'GuestSpending.SideShowHappinessStep * Math.Sign(-play.Net)', 0),
    ('satisfaction is 4x the delta', 'int fiveTimesDelta = (guest.Happiness - happiness0) * 5;', 'int fiveTimesDelta = (guest.Happiness - happiness0) * 4;', 0),
    ('satisfaction against current happiness', 'int fiveTimesDelta = (guest.Happiness - happiness0) * 5;', 'int fiveTimesDelta = 0;', 0),
    ('food purchase pays no happiness', FOODGAIN, ';', 0),
    ('drink purchase uses the non-food formula', FOODGAIN, FOODGAIN.replace('food: true', 'food: false'), 1),
    ('want computed after the product applied', 'int needB = guest.NeedB, nausea = guest.Nausea, needA = guest.NeedA, happiness0 = guest.Happiness;',
     'int needB = guest.NeedB, nausea = guest.Nausea, needA = guest.NeedA, happiness0 = guest.Happiness + 1;', 0),
]: add(P, name, old, new, occ)

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
            print(f'[{i}] {file}: {name}: pattern not found', flush=True); results[(file, name)] = {'file': file, 'rule': name, 'status': 'INVALID', 'note': 'pattern not found'}; continue
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
        'runner': 'tools/mutate_happiness.py',
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
