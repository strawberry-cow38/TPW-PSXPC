#!/usr/bin/env python3
"""One production mutation at a time; restore even on interruption. No parallel builds.

READ evidence/fixtures: findings/statistics.md. --resume retries survivors/invalids and
unattempted mutations only when production is byte-identical; retains earlier test hashes.
"""
import hashlib
import json
import re
import shutil
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FILES = {name: ROOT / ('core/TPW.Sim/' + file) for name, file in {
    'calculator': 'ParkStatistics.cs', 'interpreter': 'StatisticInterpreter.cs',
    'rules': 'ParkStatisticRules.cs'}.items()}
ORIGINALS = {key: path.read_bytes() for key, path in FILES.items()}
OUTPUT = ROOT / 'findings/statistics-mutations.json'
FILTER = 'FullyQualifiedName~ParkStatisticsTests|FullyQualifiedName~StatisticInterpreterTests'
MUTATIONS = []


def change(name, file, old, new):
    if ORIGINALS[file].decode().count(old) != 1:
        raise ValueError(f'nonunique/missing anchor: {name}')
    MUTATIONS.append(dict(name=name, file=file, old=old, new=new))


# Every numbered slot, including the dead slot and interpreter scratch.
enum = ORIGINALS['calculator'].decode().split('public enum ParkStatistic')[1].split('public readonly record')[0]
for name, number in re.findall(r'\b(\w+) = (\d+)[,\n]', enum):
    # Keep the enum legal: +1 made duplicate switch labels, which is not a test kill.
    change('slot number ' + name, 'calculator', f'{name} = {number}', f'{name} = {int(number)+100}')

for name, value, replacement in [('Percent',100,99),('Limit',30000,29999),('MonthsPerYear',12,11),
        ('MoneyScale',10,1),('GradeMaximum',4,5),('GradeMask',7,3),('DefinitionSlots',50,49),
        ('CoverageCellTiles',4,8),('CoverageSampleStep',2,1),('RideWeight',6,5),
        ('ShopWeight',4,3),('FeatureWeight',5,4)]:
    change(name, 'calculator', f'{name} = {value};', f'{name} = {replacement};')
for name, old, new in [
    ('staff count ordering','StaffKind.Entertainer, StaffKind.Mechanic,','StaffKind.Mechanic, StaffKind.Entertainer,'),
    ('staff metric ordering','StaffKind.Mechanic, StaffKind.Cleaner,','StaffKind.Cleaner, StaffKind.Mechanic,'),
    ('held staff included in count','- (world.HeldStaffKind == kind ? 1 : 0)','- 0'),
    ('only running attractions counted','a.Status != AttractionStatus.JustPlaced &&\n                    (Category','a.Status == AttractionStatus.Running &&\n                    (Category'),
    ('unassigned share rounded before complement','Percent - assigned * Percent / staff.Length','(staff.Length - assigned) * Percent / staff.Length'),
    ('grade empty default','staff.Length == 0 ? 0 :','staff.Length == 0 ? Percent :'),
    ('needs use list denominator','* Percent / world.VisitorCount;','* Percent / world.Visitors.Count();'),
    ('condition percentage empty default','world.VisitorCount == 0 ? 0 : world.Visitors.Count','world.VisitorCount == 0 ? Percent : world.Visitors.Count'),
    ('combined needs included','VisitorCondition.Of(v) == (GuestCondition)slot','(VisitorCondition.Of(v) == (GuestCondition)slot || VisitorCondition.Of(v) == GuestCondition.BothNeedsHigh)'),
    ('total days signed cap','Math.Min(world.TotalDays, Limit)','Math.Min((int)world.TotalDays, Limit)'),
    ('years signed cap','Math.Min(world.Year, Limit)','Math.Min((int)world.Year, Limit)'),
    ('month input omitted','world.Month + MonthsPerYear','0u + MonthsPerYear'),
    ('litter aliases dirtiness','case ParkStatistic.LiveLitter: return world.LiveLitterCount;','case ParkStatistic.LiveLitter: return Compute(ParkStatistic.FeatureDirtiness, world);'),
    ('dirtiness includes shops','world.Attractions.Where(a => a.Type == AttractionType.Feature)\n                        .Select','world.Attractions\n                        .Select'),
    ('dirtiness includes every feature','((a.FeatureFlags & 1) != 0, a.Cleanliness)','(true, a.Cleanliness)'),
    ('retained slot31 counts held features','a.Status != AttractionStatus.JustPlaced && (a.FeatureFlags & 1) != 0','(a.FeatureFlags & 1) != 0'),
    ('retained slot31 substitutes percentage','case ParkStatistic.RetainedUsableFeatureCount:\n','case ParkStatistic.RetainedUsableFeatureCount: return 100;\n                case (ParkStatistic)99:\n'),
    ('happiness numerator constant','sum + (uint)v.Happiness','sum + 100u'),
    ('happiness denominator list','sum / (uint)world.VisitorCount','sum / (uint)world.Visitors.Count()'),
    ('happiness empty default','world.VisitorCount == 0 ? 0 : (int)Math.Min','world.VisitorCount == 0 ? Percent : (int)Math.Min'),
    ('happiness cap omitted','(int)Math.Min(sum / (uint)world.VisitorCount, Percent)','(int)(sum / (uint)world.VisitorCount)'),
    ('balance unit pounds','world.BalanceRaw / MoneyScale / Percent','world.BalanceRaw / MoneyScale'),
    ('balance minimum lost','Math.Clamp(world.BalanceRaw / MoneyScale / Percent, -Limit, Limit)','Math.Min(world.BalanceRaw / MoneyScale / Percent, Limit)'),
    ('wages compare raw tenths','world.LastMonthWagesRaw / MoneyScale > world.LastMonthIncomeRaw / MoneyScale','world.LastMonthWagesRaw > world.LastMonthIncomeRaw'),
    ('wages comparison inclusive','world.LastMonthIncomeRaw / MoneyScale ? 1 : 0','world.LastMonthIncomeRaw / MoneyScale ? 1 : 0'),
    ('weighted accumulator does not wrap','unchecked((ushort)(weighted +','unchecked((int)(weighted +'),
    ('weighted limit omitted','return Math.Min(weighted, Limit);','return weighted;'),
    ('variety zero availability included','if (count == 0) continue;','// include unavailable class'),
    ('variety denominator includes locked','.Count(d => world.IsDefinitionAvailable(d));','.Count();'),
    ('variety includes status zero','a.Type == type && a.Status != AttractionStatus.JustPlaced','a.Type == type'),
    ('variety duplicates counted','built += present.Count(p => p);','built += world.Attractions.Count(a => a.Type == type && a.Status != AttractionStatus.JustPlaced);'),
    ('available variety ignores availability','definitions.Count(d => world.IsDefinitionAvailable(d))','definitions.Length'),
    ('applied upgrades last rather than best','Math.Max(bestPlusOne, a.UpgradeLevel + 1)','a.UpgradeLevel + 1'),
    ('applied upgrades excludes status zero','if (a.Type == d.Type && a.DefinitionIndex == d.Index)','if (a.Type == d.Type && a.DefinitionIndex == d.Index && a.Status != AttractionStatus.JustPlaced)'),
    ('applied includes base level','applied += bestPlusOne - 1;','applied += bestPlusOne;'),
    ('applied denominator includes base level','available += world.AvailableLevelCount(d) - 1;','available += world.AvailableLevelCount(d);'),
    ('applied counts unbuilt definitions','if (bestPlusOne == 0) continue;','// unbuilt included'),
    ('unlocked includes unavailable bases','if (levels == 0) continue;','// unavailable included'),
    ('unlocked numerator includes base level','unlocked += levels - 1;','unlocked += levels;'),
    ('three possible upgrades','possible += 2;','possible += 3;'),
    ('coverage ignores assignment','if (!s.HasPatrolArea) continue;','// all rectangles included'),
    ('coverage x far edge exclusive','x <= s.Right >> 2','x < s.Right >> 2'),
    ('coverage y far edge exclusive','y <= s.Bottom >> 2','y < s.Bottom >> 2'),
    ('coverage x rectangle scale','s.Left >> 2','s.Left >> 1'),
    ('coverage y rectangle scale','s.Top >> 2','s.Top >> 1'),
    ('coverage overwrites path bit','cells[y * width + x] |= 2;','cells[y * width + x] = 2;'),
    ('coverage denominator counts empty cells','cells.Count(c => c == 1 || c == 3)','cells.Length'),
    ('coverage counts rectangles without path','cells.Count(c => c == 3)','cells.Count(c => c == 2 || c == 3)'),
    ('coverage no-path default','return covered == 0 ? 0 :','return covered == 0 ? Percent :'),
]:
    if name == 'wages comparison inclusive':
        old = 'world.LastMonthWagesRaw / MoneyScale > world.LastMonthIncomeRaw / MoneyScale'
        new = old.replace(' > ',' >= ')
    change(name,'calculator',old,new)

for name, old, new in [
    ('built availability type order', 'BuiltOrder = { AttractionType.Ride, AttractionType.TourRide,',
        'BuiltOrder = { AttractionType.TourRide, AttractionType.Ride,'),
    ('available variety type order', 'AvailableOrder = { AttractionType.Ride, AttractionType.TourRide,',
        'AvailableOrder = { AttractionType.TourRide, AttractionType.Ride,'),
    ('upgrade query type order', 'UpgradeOrder = { AttractionType.Ride, AttractionType.TrackRide,',
        'UpgradeOrder = { AttractionType.TrackRide, AttractionType.Ride,'),
    ('built definition index order', 'world.Definitions.Where(d => d.Type == type).OrderBy(d => d.Index)\n                    .Count',
        'world.Definitions.Where(d => d.Type == type).OrderByDescending(d => d.Index)\n                    .Count'),
    ('other definition index order', '.OrderBy(d => d.Index));', '.OrderByDescending(d => d.Index));'),
    ('ordered definitions ignore mask', 'order.Where(type => (Category(type, 0) & mask) != 0)', 'order'),
    ('level queries omitted', 'int levels = world.AvailableLevelCount(d);', 'int levels = 3;'),
]: change(name,'calculator',old,new)

# Source-selection branches are separate from the numbered enum and the category classifier.
for name, old, new in [
    ('park-open flag inverted', 'case ParkStatistic.ParkOpen: return world.ParkOpen ? 1 : 0;',
        'case ParkStatistic.ParkOpen: return world.ParkOpen ? 0 : 1;'),
    ('guest source uses enumerated list', 'case ParkStatistic.Visitors: return world.VisitorCount;',
        'case ParkStatistic.Visitors: return world.Visitors.Count();'),
    ('strike flag inverted', 'case ParkStatistic.MechanicsOnStrike: return world.MechanicsOnStrike ? 1 : 0;',
        'case ParkStatistic.MechanicsOnStrike: return world.MechanicsOnStrike ? 0 : 1;'),
    ('research flag inverted', 'case ParkStatistic.AnyResearchActive: return world.AnyResearchActive ? 1 : 0;',
        'case ParkStatistic.AnyResearchActive: return world.AnyResearchActive ? 0 : 1;'),
    ('ride count mask omits ride', '13 => 0xF,', '13 => 0xE,'),
    ('shop count mask selects sideshows', '14 => 0x100,', '14 => 0x200,'),
    ('sideshow count mask selects shops', '15 => 0x200,', '15 => 0x100,'),
    ('feature count selects usable only', '16 => 0xF0,', '16 => 0x20,'),
    ('usable count selects flag3', '17 => 0x20,', '17 => 0x40,'),
    ('flag1 count selects usable', '18 => 0x80,', '18 => 0x20,'),
    ('flag3 count selects flag1', '_ => 0x40 };', '_ => 0x80 };'),
]: change(name,'calculator',old,new)
for slot,helper,mask,replacement in [
    ('BuiltRideVariety','BuiltVariety','0xF','0x100'),
    ('AvailableRideVariety','AvailableVariety','0xF','0x100'),
    ('BuiltShopVariety','BuiltVariety','0x100','0x200'),
    ('AvailableShopVariety','AvailableVariety','0x100','0x200'),
    ('BuiltSideShowVariety','BuiltVariety','0x200','0x100'),
    ('AvailableSideShowVariety','AvailableVariety','0x200','0x100'),
    ('BuiltFeatureVariety','BuiltVariety','0xF0','0x100'),
    ('AvailableFeatureVariety','AvailableVariety','0xF0','0x100')]:
    old = f'case ParkStatistic.{slot}: return {helper}({mask}, world);'
    change(slot+' source mask','calculator',old,old.replace(mask,replacement))
for name,value in [('Equal',1),('NotEqual',2),('Less',3),('Greater',4),('Add',5),('Set',6),
                   ('PostMessage',7),('Action8',8),('ElapsedGreater',9)]:
    change('opcode number '+name,'interpreter',f'{name} = {value},',f'{name} = {value+100},')
for name,value in [('Completed',0),('ConditionFailed',1),('StillWaiting',2)]:
    change('result number '+name,'interpreter',f'{name} = {value}',f'{name} = {value+100}')

# Each category mapping is an independent binary branch.
for typ, old, new in [('Ride','0x01','0x02'),('TourRide','0x02','0x04'),('TrackRide','0x04','0x08'),
                     ('RollerCoaster','0x08','0x01'),('Shop','0x100','0x200'),('SideShow','0x200','0x100')]:
    change('category '+typ,'calculator',f'AttractionType.{typ} => {old},',f'AttractionType.{typ} => {new},')
change('feature usable priority','calculator','(flags & 1) != 0 ? 0x20 :','(flags & 1) != 0 ? 0x80 :')
change('feature bit3 priority','calculator','(flags & 8) != 0 ? 0x40 :','(flags & 8) != 0 ? 0x80 :')
change('feature bit1 category','calculator','(flags & 2) != 0 ? 0x80 : 0x10','(flags & 2) != 0 ? 0x40 : 0x10')
change('ratio caps at99','calculator','numerator >= denominator ? Percent :','numerator >= denominator ? Percent - 1 :')

for name, value, replacement in [('SlotCount',72,71),('RefreshPeriod',4,3),('CycleTicks',288,287),
        ('FirstCounterSlot',51,50),('CounterCount',20,19),('CounterLimit',30000,29999)]:
    change(name,'interpreter',f'{name} = {value};',f'{name} = {replacement};')
for name, old, new in [
    ('init failure date zero','Array.Fill(lastFailureDay, nowDay);','Array.Fill(lastFailureDay, 0u);'),
    ('caller can mutate program','(StatisticInstruction[])instructions.Clone()','instructions'),
    ('event ignores enablement','!world.StatisticsEnabled || eventIndex < 0','eventIndex < 0'),
    ('event freezes while advisor speaks','!world.StatisticsEnabled || eventIndex < 0','!world.StatisticsEnabled || !world.AdvisorIdle || eventIndex < 0'),
    ('event drops last counter','eventIndex >= CounterCount','eventIndex >= CounterCount - 1'),
    ('event wrap lost','short wrapped = unchecked((short)(counters[eventIndex] + amount));','int wrapped = counters[eventIndex] + amount;'),
    ('event addition becomes assignment','counters[eventIndex] + amount','amount'),
    ('event leaks into cache','counters[eventIndex] = (short)Math.Clamp((int)wrapped, -CounterLimit, CounterLimit);','counters[eventIndex] = (short)Math.Clamp((int)wrapped, -CounterLimit, CounterLimit); values[FirstCounterSlot+eventIndex] = counters[eventIndex];'),
    ('refresh ignores enablement','if (!world.StatisticsEnabled || !world.AdvisorIdle) return;','if (!world.AdvisorIdle) return;'),
    ('refresh ignores advisor phase','if (!world.StatisticsEnabled || !world.AdvisorIdle) return;','if (!world.StatisticsEnabled) return;'),
    ('refresh reversed cadence','RefreshCursor % RefreshPeriod == 0','RefreshCursor % RefreshPeriod == 1'),
    ('cache clamp rather than wrap','unchecked((short)ParkStatisticCalculator.Compute((ParkStatistic)slot, world))','(short)Math.Clamp(ParkStatisticCalculator.Compute((ParkStatistic)slot, world), 0, short.MaxValue)'),
    ('counter copy wrong index','counters[slot - FirstCounterSlot];','counters[(slot - FirstCounterSlot + 1) % CounterCount];'),
    ('counter copy clears backing','values[slot] = counters[slot - FirstCounterSlot];','values[slot] = counters[slot - FirstCounterSlot];\n                if (slot >= FirstCounterSlot && slot < FirstCounterSlot + CounterCount) counters[slot - FirstCounterSlot] = 0;'),
    ('first sweep barrier lost','if (!FirstSweepComplete || !world.AdvisorQueueEmpty) return;','if (!world.AdvisorQueueEmpty) return;'),
    ('queue barrier lost','if (!FirstSweepComplete || !world.AdvisorQueueEmpty) return;','if (!FirstSweepComplete) return;'),
    ('rule only every fourth call','if (!FirstSweepComplete || !world.AdvisorQueueEmpty) return;','if (!FirstSweepComplete || !world.AdvisorQueueEmpty || RefreshCursor % RefreshPeriod != 0) return;'),
    ('schedule equality runs','nextCheckDay[current] < now','nextCheckDay[current] <= now'),
    ('schedule comparison signed','nextCheckDay[current] < now','(int)nextCheckDay[current] < (int)now'),
    ('timer ignores last failure','now - lastFailureDay[current]','now'),
    ('elapsed scratch saturates','unchecked((short)(now - lastFailureDay[current]))','(short)Math.Min(unchecked(now - lastFailureDay[current]), (uint)short.MaxValue)'),
    ('success forgets cooldown','unchecked(now + rules[current].CooldownDays)','now'),
    ('cooldown date saturates','unchecked(now + rules[current].CooldownDays)','(uint)Math.Min((ulong)now + rules[current].CooldownDays, uint.MaxValue)'),
    ('waiting restarts timer','else if (result == StatisticRuleResult.ConditionFailed)','else if (result != StatisticRuleResult.Completed)'),
    ('failure time not recorded','lastFailureDay[current] = now;','lastFailureDay[current] = lastFailureDay[current];'),
    ('rule cursor skips one','RuleCursor = (RuleCursor + 1) % rules.Count;','RuleCursor = (RuleCursor + 2) % rules.Count;'),
    ('equal inverted','if (values[index] != operand)','if (values[index] == operand)'),
    ('not-equal inverted','if (values[index] == operand)','if (values[index] != operand)'),
    ('less inclusive','if (values[index] >= operand)','if (values[index] > operand)'),
    ('greater inclusive','if (values[index] <= operand)','if (values[index] < operand)'),
    ('less compares unsigned','if (values[index] >= operand)','if ((ushort)values[index] >= (ushort)operand)'),
    ('greater compares unsigned','if (values[index] <= operand)','if ((ushort)values[index] <= (ushort)operand)'),
    ('add becomes subtract','values[index] + operand','values[index] - operand'),
    ('add clamps','unchecked((short)(values[index] + operand))','(short)Math.Clamp(values[index] + operand, -30000, 30000)'),
    ('set forgets cache','values[index] = operand;','// set cache omitted'),
    ('set forgets backing','else counters[index - FirstCounterSlot] = operand;','else counters[index - FirstCounterSlot] = counters[index - FirstCounterSlot];'),
    ('set first counter not mirrored','if (index >= FirstCounterSlot)','if (index > FirstCounterSlot)'),
    ('set71 cursor alias lost','RefreshCursor = unchecked((ushort)operand);','RefreshCursor = RefreshCursor;'),
    ('post omitted','world.PostMessage(unchecked((ushort)instruction.A));','// post omitted'),
    ('action8 omitted','world.ApplyRuleAction8(instruction.A);','// action8 omitted'),
    ('elapsed equality passes','values[(int)ParkStatistic.RuleElapsedDays] <= instruction.A','values[(int)ParkStatistic.RuleElapsedDays] < instruction.A'),
    ('elapsed reads wrong slot','if (values[(int)ParkStatistic.RuleElapsedDays] <= instruction.A)','if (values[(int)ParkStatistic.EntryValue] <= instruction.A)'),
    ('elapsed failure restarts','return StatisticRuleResult.StillWaiting;','return StatisticRuleResult.ConditionFailed;'),
]: change(name,'interpreter',old,new)

# Mutate each decoded program independently AND its cooldown independently. The independent finding
# rejects corruption even for programs whose triggering world state is rare. Behavioral tests above
# separately kill interpreter mutations, so a golden table is not our only verification.
for row,line in enumerate(l for l in ORIGINALS['rules'].decode().splitlines() if '// rule ' in l):
    match = re.search(r'new\((\d+), new StatisticInstruction',line)
    changed = line[:match.start(1)] + str(int(match[1])+1) + line[match.end(1):]
    change(f'disc rule {row} cooldown','rules',line,changed)
    match = re.search(r'new\(StatisticOpcode\.(\w+), (-?\d+)',line)
    changed = line[:match.start(2)] + str(int(match[2])+1) + line[match.end(2):]
    change(f'disc rule {row} operand','rules',line,changed)

RESULT = dict(sources={k:str(p.relative_to(ROOT)) for k,p in FILES.items()},
              original_sha256={k:hashlib.sha256(v).hexdigest() for k,v in ORIGINALS.items()},
              test_sha256={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest()
                           for p in [ROOT/'tests/TPW.Sim.Tests/ParkStatisticsTests.cs',ROOT/'tests/TPW.Sim.Tests/StatisticInterpreterTests.cs']},
              finding_sha256=hashlib.sha256((ROOT/'findings/statistics-rules.json').read_bytes()).hexdigest(),
              filter=FILTER, mutations=[], restored=False)
todo = MUTATIONS
if '--resume' in sys.argv:
    previous = json.loads(OUTPUT.read_text())
    if not previous['restored'] or previous['original_sha256'] != RESULT['original_sha256']:
        raise SystemExit('Resume requires byte-identical, fully restored production files.')
    RESULT['earlier_sweeps'] = previous.get('earlier_sweeps',[]) + [
        {k:previous[k] for k in ('baseline','test_sha256','summary') if k in previous}]
    RESULT['prior_non_kills'] = previous.get('prior_non_kills',[]) + [m for m in previous['mutations'] if m['status'] != 'killed']
    killed = {m['name']:m for m in previous['mutations'] if m['status']=='killed'}
    RESULT['mutations'] = list(killed.values())
    todo = [m for m in MUTATIONS if m['name'] not in killed]


def run_tests(folder, full=False):
    trx = folder/'result.trx'; trx.unlink(missing_ok=True)
    start = time.monotonic()
    if full:
        cmd = ['dotnet','test','tests/TPW.Sim.Tests/','--no-restore','--nologo',
               '--logger','trx;LogFileName=result.trx','--results-directory',str(folder)]
    else:
        # Keep baseline-compiled tests fixed, including their original inlined enum/constants.
        # Rebuild only production, copy it into THIS worktree's test output, and run the real xunit DLL.
        build = subprocess.run(['dotnet','build','core/TPW.Sim/','--no-restore','--nologo'],
                               cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=180)
        if build.returncode:
            return dict(exit_code=build.returncode,seconds=round(time.monotonic()-start,2),
                        compile_error=True,failed_tests=[],counters={},output=build.stdout[-4000:])
        shutil.copy2(ROOT/'core/TPW.Sim/bin/Debug/net8.0/TPW.Sim.dll',
                     ROOT/'tests/TPW.Sim.Tests/bin/Debug/net8.0/TPW.Sim.dll')
        cmd = ['dotnet','vstest','tests/TPW.Sim.Tests/bin/Debug/net8.0/TPW.Sim.Tests.dll',
               '--TestCaseFilter:'+FILTER,'--logger:trx;LogFileName=result.trx',
               '--ResultsDirectory:'+str(folder)]
    run = subprocess.run(cmd,cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=180)
    result = dict(exit_code=run.returncode,seconds=round(time.monotonic()-start,2),
                  compile_error='error CS' in run.stdout,failed_tests=[],counters={})
    if trx.exists():
        ns = {'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
        tree = ET.parse(trx)
        result['failed_tests'] = [e.get('testName') for e in tree.findall('.//t:UnitTestResult',ns) if e.get('outcome')=='Failed']
        counter = tree.find('.//t:Counters',ns)
        if counter is not None: result['counters'] = {k:int(v) for k,v in counter.attrib.items()}
    if not result['counters']: result['output'] = run.stdout[-4000:]
    return result


# TOP-LEVEL restoration guard. Every production write is inside this try/finally.
try:
    with tempfile.TemporaryDirectory(prefix='tpw-statistics-mutations-') as tmp:
        folder = Path(tmp)
        RESULT['baseline'] = run_tests(folder,full=True)
        if RESULT['baseline']['exit_code']: raise RuntimeError('Baseline is not green.')
        for i,mutation in enumerate(todo,1):
            key = mutation['file']
            FILES[key].write_text(ORIGINALS[key].decode().replace(mutation['old'],mutation['new'],1))
            result = run_tests(folder)
            FILES[key].write_bytes(ORIGINALS[key])
            result.update(mutation)
            result['status'] = ('killed' if result['failed_tests'] and not result['compile_error'] else
                                'survived' if result['exit_code']==0 else 'invalid')
            RESULT['mutations'].append(result)
            OUTPUT.write_text(json.dumps(RESULT,indent=2)+'\n')
            print(f"{i}/{len(todo)} {result['status']}: {mutation['name']}",flush=True)
except BaseException as error:
    RESULT['interruption'] = repr(error)
    raise
finally:
    for key,path in FILES.items(): path.write_bytes(ORIGINALS[key])
    RESULT['restored'] = all(path.read_bytes()==ORIGINALS[key] for key,path in FILES.items())
    RESULT['summary'] = {s:sum(m['status']==s for m in RESULT['mutations']) for s in ('killed','survived','invalid')}
    RESULT['summary']['total'] = len(RESULT['mutations'])
    OUTPUT.write_text(json.dumps(RESULT,indent=2)+'\n')

with tempfile.TemporaryDirectory(prefix='tpw-statistics-final-') as tmp:
    RESULT['final_full_suite'] = run_tests(Path(tmp),full=True)
OUTPUT.write_text(json.dumps(RESULT,indent=2)+'\n')
if RESULT['summary']['survived'] or RESULT['summary']['invalid'] or RESULT['final_full_suite']['exit_code']:
    raise SystemExit('Sweep needs attention; all production files are restored.')
print(json.dumps(RESULT['summary']),flush=True)
