#!/usr/bin/env python3
"""One production mutation at a time, against fixed baseline-compiled tests.
Compilation errors, skipped/empty tests and timeouts are INVALID, never kills.
Restores all sources in finally; JSON retains failing names, counts, controls and hashes.
--resume keeps previous kills only with identical restored production; records earlier survivors.
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
ROOT=Path(__file__).resolve().parents[1]
PATHS=[ROOT/'core/TPW.Sim'/n for n in ('ParkScore.cs','ScoreHistoryCodec.cs','ParkHistory.cs')]
ORIGINAL={p:p.read_bytes() for p in PATHS}
OUTPUT=ROOT/'findings/rating-mutations.json'
FILTER='FullyQualifiedName~ParkScoreTests|FullyQualifiedName~ParkHistoryTests'
mutations=[]
def change(file,name,old,new):
    p=ROOT/'core/TPW.Sim'/file
    if ORIGINAL[p].decode().count(old)!=1: raise ValueError('missing/nonunique anchor: '+name)
    mutations.append(dict(file=str(p.relative_to(ROOT)),name=name,old=old,new=new))
def score(name,old,new):change('ParkScore.cs',name,old,new)
def codec(name,old,new):change('ScoreHistoryCodec.cs',name,old,new)
for name,old,new in [
 ('rating dead','return rating;','return 0;'),
 ('guest cap 99','Math.Min(world.Visitors.Count(), 100)','Math.Min(world.Visitors.Count(), 99)'),
 ('guest cap omitted','Math.Min(world.Visitors.Count(), 100)','world.Visitors.Count()'),
 ('guest divisor 4','Count(), 100) / 5','Count(), 100) / 4'),
 ('ride rounding','rides * 3 / 2','rides * (3 / 2)'),
 ('ride cap 19','rides * 3 / 2, 20','rides * 3 / 2, 19'),
 ('upgraded threshold 1','a.UpgradeLevel >= 2','a.UpgradeLevel >= 1'),
 ('upgraded threshold 3','a.UpgradeLevel >= 2','a.UpgradeLevel >= 3'),
 ('upgrade cap 9','Math.Min(upgraded, 10)','Math.Min(upgraded, 9)'),
 ('shop multiplier 1','AttractionType.Shop) * 2','AttractionType.Shop) * 1'),
 ('sideshow multiplier 1','AttractionType.SideShow) * 2','AttractionType.SideShow) * 1'),
 ('feature omitted','a.Type == AttractionType.Feature), 10','a.Type == AttractionType.Feature), 0'),
 ('staff cap 3','staff.Count(s => s.Kind == kind), 4','staff.Count(s => s.Kind == kind), 3'),
 ('staff kinds share cap','staff.Count(s => s.Kind == kind), 4','staff.Length, 4'),
 ('value silently adopts binary selector','world.ValuePricePounds(a.Type, a.UpgradeLevel)','world.ValuePricePounds(a.Type, a.DefinitionIndex)'),
 ('value no halving','unchecked(pounds * 10) / 2','unchecked(pounds * 10)'),
 ('value whole pound halving','unchecked(pounds * 10) / 2','unchecked((pounds / 2) * 10)'),
 ('value widened conversion','unchecked(pounds * 10) / 2','((long)pounds * 10) / 2'),
 ('value stale at age zero','monthsBack == 0 ? CalculateValue(world)','monthsBack == -1 ? CalculateValue(world)'),
 ('totals widened','unchecked((int)(a.Raw + b.Raw))','a.Raw + b.Raw'),
 ('event overwrites slot','unchecked(bank[(int)row][MonthIndex % Slots] + (int)amount.Raw)','unchecked((int)amount.Raw)'),
 ('income omitted','Income = Add(Income, amount);','Income = Income;'),
 ('year income omitted','ThisYearIncome = Add(ThisYearIncome, amount);','ThisYearIncome = ThisYearIncome;'),
 ('spending omitted','Spending = Add(Spending, amount);','Spending = Spending;'),
 ('year spending omitted','ThisYearSpending = Add(ThisYearSpending, amount);','ThisYearSpending = ThisYearSpending;'),
 ('sack wages omitted','Wages = Add(Wages, amount);','Wages = Wages;'),
 ('sack enters wage ring','Wages = Add(Wages, amount);','Wages = Add(Wages, amount); AddTo(BankHistoryRow.Wages, amount);'),
 ('monthly gate inverted','if (!calendar.MonthRolledOver) return false;','if (calendar.MonthRolledOver) return false;'),
 ('balance after charge','unchecked((int)balanceBeforeCharges.Raw)','unchecked((int)result.ClosingBalance.Raw)'),
 ('loan bookkeeping omitted','LoanRepayments = Money.FromRaw(unchecked((int)result.LoanPayments.Raw));','LoanRepayments = Money.Zero;'),
 ('monthly wages omitted','Wages = Add(Wages, result.Wages);','Wages = Wages;'),
 ('wages row wrong amount','bank[(int)BankHistoryRow.Wages][slot] = unchecked((int)result.Wages.Raw);','bank[(int)BankHistoryRow.Wages][slot] = unchecked((int)result.TotalCharged.Raw);'),
 ('scheduled loans not spending','RecordSpending(result.TotalCharged);','RecordSpending(result.Wages);'),
 ('value never sampled','bank[(int)BankHistoryRow.ParkValue][slot] = unchecked((int)value.Raw);','bank[(int)BankHistoryRow.ParkValue][slot] = 0;'),
 ('yearly snapshot on first rollover','MonthIndex != 0 && MonthIndex % 12 == 0','MonthIndex % 12 == 0'),
 ('yearly snapshot aligned with calendar','MonthIndex != 0 && MonthIndex % 12 == 0','(MonthIndex + 1) % 12 == 0'),
 ('yearly snapshot always','MonthIndex != 0 && MonthIndex % 12 == 0','true'),
 ('yearly balance before charges','YearlyBalance = Money.FromRaw(unchecked((int)result.ClosingBalance.Raw));','YearlyBalance = balanceBeforeCharges;'),
 ('month index frozen','MonthIndex++;','MonthIndex += 0;'),
 ('clear includes balance','int row = (int)BankHistoryRow.Income;','int row = (int)BankHistoryRow.Balance;'),
 ('clear includes value','row <= (int)BankHistoryRow.Wages;','row <= (int)BankHistoryRow.ParkValue;'),
 ('clear misses wages','row <= (int)BankHistoryRow.Wages;','row < (int)BankHistoryRow.Wages;'),
 ('wrong calendar write slot','calendar.TotalMonths - 1, calendar.Month','calendar.TotalMonths, calendar.Month'),
 ('year rotates monthly','if (calendar.YearRolledOver)','if (calendar.MonthRolledOver)'),
 ('income annual reset omitted','ThisYearIncome = Money.Zero;','ThisYearIncome = ThisYearIncome;'),
 ('spend annual reset omitted','ThisYearSpending = Money.Zero;','ThisYearSpending = ThisYearSpending;'),
 ('missing history returns money','slot < 0 ? Money.Zero','slot < 0 ? Money.FromRaw(1)'),
 ('copy aliases owner','(int[])bank[(int)row].Clone()','bank[(int)row]'),
 ('bank capture offset','row * ScoreHistoryCodec.MoneyRecordSize','(row + 1) * ScoreHistoryCodec.MoneyRecordSize'),
]:
    # capture/restore share the stride anchor; mutate the capture line specifically.
    if name=='bank capture offset':
        old='save.Bytes.AsSpan(row * ScoreHistoryCodec.MoneyRecordSize));'
        new='save.Bytes.AsSpan((row + 1) * ScoreHistoryCodec.MoneyRecordSize));'
    score(name,old,new)
for ride in ('RollerCoaster','Ride','TrackRide','TourRide'):
    score('exclude '+ride,'AttractionType.'+ride+(')\n' if ride=='TourRide' else ' or'),'(AttractionType)99'+(')\n' if ride=='TourRide' else ' or'))
for prop,row in [('EntryTakings','Entrance'),('ShopProfit','Shop'),('SideshowTakings','SideShow')]:
    score('missing '+prop,prop+' = Add('+prop+', amount);',prop+' = '+prop+';')
    score('missing '+row+' row','AddTo(BankHistoryRow.'+row+', amount);',';')
for prop in ('LastYearIncome','LastYearSpend','ThisYearIncome','ThisYearSpend','SideshowTakings','EntryTakings','ShopProfit','Wages','Spend','Income','YearlyValue','YearlyBalance'):
    # Test every destination/source independently: no mirror-shaped round-trip can hide a lost field.
    import re
    text=ORIGINAL[PATHS[0]].decode()
    old=re.search(r'save\.'+prop+r'Pounds = [^;]+;',text).group()
    score('capture '+prop+' lost',old,'save.'+prop+'Pounds = 0;')
    old='FromSavedPounds(save.'+prop+'Pounds)'
    score('restore '+prop+' lost',old,'Money.Zero')
for name,old,new in [
 ('recent window 11','const int Recent = 12;','const int Recent = 11;'),
 ('normalization scale 254','const int Scale = 255;','const int Scale = 254;'),
 ('group pairs width 3','(6, 2), (8, 6)','(6, 3), (8, 6)'),
 ('ring reversed','+ 2 * ParkHistory.Slots - age','+ 2 * ParkHistory.Slots + age'),
 ('byte interpolation bug fixed','group.Width == 6 && j == 1 ? 0 : j','j'),
 ('byte neighbor ignored','nextWeight * input[at + 1]','nextWeight * input[at]'),
 ('extrema oldest included','Math.Min(143, months - 1)','Math.Min(144, months)'),
 ('money normalization signed','(sum / (uint)group.Width - (uint)min)','((uint)((int)sum / group.Width) - (uint)min)'),
 ('money normalization rounded','unchecked((byte)normalized)','unchecked((byte)(normalized + 1))'),
 ('restore range off by one','LittleEndian(input.Slice(4)) - min','LittleEndian(input.Slice(4)) - min + 1'),
 ('restore pair truncates negative','unchecked(a + b) >> 1','unchecked(a + b) / 2'),
 ('restore omits tenths conversion','unchecked(pounds * 10);','pounds;'),
 ('restore neighbor ignored','range * input[at + 1] / Scale','range * input[at] / Scale'),
 ('restore product widened','range * input[at] / Scale','(int)((long)range * input[at] / Scale)'),
]:codec(name,old,new)
for name,old,new in [
 ('history annual latch missing','if (year != 0 && month == 0) RatingAtNewYear = rating & 0xFF;','if (false) RatingAtNewYear = rating & 0xFF;'),
 ('history annual latch every month','year != 0 && month == 0','year != 0'),
 ('history guard inclusive','(uint)n) < unchecked','(uint)n) <= unchecked'),
 ('history arrivals reads direct prior','int previous = Read(HistoryRow.People, monthsBefore, 1);','int previous = _people[(slot + Slots - 1) % Slots];'),
 ('history annual restore missing','RatingAtNewYear = save.RatingAtNewYear;','RatingAtNewYear = 0;'),
 ('history annual capture missing','save.RatingAtNewYear = (byte)RatingAtNewYear;','save.RatingAtNewYear = 0;'),
]:change('ParkHistory.cs',name,old,new)

sha=lambda data:hashlib.sha256(data).hexdigest()
hashes={str(p.relative_to(ROOT)):sha(b) for p,b in ORIGINAL.items()}
tests={str(p.relative_to(ROOT)):sha(p.read_bytes()) for p in [ROOT/'tests/TPW.Sim.Tests/ParkScoreTests.cs',ROOT/'tests/TPW.Sim.Tests/ParkHistoryTests.cs',ROOT/'findings/rating-audit.json']}
audit=dict(source_sha256=hashes,tests_sha256=tests,filter=FILTER,mutations=[],restored=False)
todo=mutations
if '--resume' in sys.argv:
    prior=json.loads(OUTPUT.read_text())
    if not prior['restored'] or prior['source_sha256']!=hashes:raise SystemExit('Cannot resume changed production')
    audit['earlier_sweeps']=prior.get('earlier_sweeps',[])+[{k:prior[k] for k in ('baseline','tests_sha256','summary')}]
    audit['prior_non_kills']=prior.get('prior_non_kills',[])+[m for m in prior['mutations'] if m['status']!='killed']
    audit['mutations']=[m for m in prior['mutations'] if m['status']=='killed']
    done={m['name'] for m in audit['mutations']};todo=[m for m in mutations if m['name'] not in done]
def run(folder,full=False):
    trx=folder/'result.trx';trx.unlink(missing_ok=True);start=time.monotonic()
    if full:command=['dotnet','test','tests/TPW.Sim.Tests/','--no-restore','--nologo','--logger','trx;LogFileName=result.trx','--results-directory',str(folder)]
    else:
        build=subprocess.run(['dotnet','build','core/TPW.Sim/','--no-restore','--nologo'],cwd=ROOT,capture_output=True,text=True,timeout=240)
        if build.returncode:return dict(exit_code=build.returncode,compile_error=True,counters={},failed_tests=[],output=(build.stdout+build.stderr)[-4000:])
        shutil.copy2(ROOT/'core/TPW.Sim/bin/Debug/net8.0/TPW.Sim.dll',ROOT/'tests/TPW.Sim.Tests/bin/Debug/net8.0/TPW.Sim.dll')
        command=['dotnet','vstest','tests/TPW.Sim.Tests/bin/Debug/net8.0/TPW.Sim.Tests.dll','--TestCaseFilter:'+FILTER,'--logger:trx;LogFileName=result.trx','--ResultsDirectory:'+str(folder)]
    result=subprocess.run(command,cwd=ROOT,capture_output=True,text=True,timeout=240)
    out=dict(exit_code=result.returncode,seconds=round(time.monotonic()-start,2),compile_error='error CS' in result.stdout,counters={},failed_tests=[])
    if trx.exists():
        ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};tree=ET.parse(trx)
        out['failed_tests']=[x.get('testName') for x in tree.findall('.//t:UnitTestResult',ns) if x.get('outcome')=='Failed']
        count=tree.find('.//t:Counters',ns)
        if count is not None:out['counters']={k:int(v) for k,v in count.attrib.items()}
    if not out['counters']:out['output']=(result.stdout+result.stderr)[-4000:]
    return out
try:
    with tempfile.TemporaryDirectory(prefix='rating-mutations-',dir=ROOT/'tools') as directory:
        folder=Path(directory);audit['baseline']=run(folder,True)
        if audit['baseline']['exit_code'] or not audit['baseline']['counters'].get('passed'):raise RuntimeError('Full baseline failed/empty')
        audit['unchanged_control']=run(folder)
        expected=audit['unchanged_control']['counters'].get('executed',0)
        if audit['unchanged_control']['exit_code'] or not expected:raise RuntimeError('Targeted control failed/empty')
        print(f'Controls: full={audit["baseline"]["counters"]["passed"]}, targeted={expected}; planned={len(mutations)}, resumed={len(mutations)-len(todo)}',flush=True)
        for i,m in enumerate(todo,1):
            p=ROOT/m['file'];p.write_text(ORIGINAL[p].decode().replace(m['old'],m['new'],1))
            result=run(folder);p.write_bytes(ORIGINAL[p])
            count=result['counters'];valid=not result['compile_error'] and count.get('executed')==expected and count.get('notExecuted',0)==0
            result['status']='killed' if valid and result['exit_code']!=0 and result['failed_tests'] else 'survived' if valid and result['exit_code']==0 else 'invalid'
            result.update(m);audit['mutations'].append(result);OUTPUT.write_text(json.dumps(audit,indent=2)+'\n')
            print(f'{i}/{len(todo)} {result["status"]}: {m["name"]}',flush=True)
finally:
    for p,data in ORIGINAL.items():p.write_bytes(data)
    audit['restored']=all(p.read_bytes()==b for p,b in ORIGINAL.items())
    audit['summary']={status:sum(m['status']==status for m in audit['mutations']) for status in ('killed','survived','invalid')}
    audit['summary'].update(planned=len(mutations),executed=len(audit['mutations']),skipped=len(mutations)-len(audit['mutations']))
    OUTPUT.write_text(json.dumps(audit,indent=2)+'\n')
with tempfile.TemporaryDirectory(prefix='rating-final-',dir=ROOT/'tools') as directory:audit['final_full_suite']=run(Path(directory),True)
OUTPUT.write_text(json.dumps(audit,indent=2)+'\n')
print(json.dumps(audit['summary']),flush=True)
if audit['summary']['skipped'] or audit['summary']['survived'] or audit['summary']['invalid'] or audit['final_full_suite']['exit_code']:raise SystemExit('Mutation audit not green')
