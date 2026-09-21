#!/usr/bin/env python3
"""Mutate one production rule at a time; freeze test assembly, reject empty runs/compile failures.
All writes (backups, builds, TRX and audit) stay in the goals worktree. Restore on signals/errors.
"""
import hashlib
import json
from pathlib import Path
import shutil
import signal
import subprocess
import time
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
FILES={n:ROOT/'core/TPW.Sim'/n for n in ('ParkObjectives.cs','ParkObjectiveDefinition.cs')}
ORIGINAL={n:p.read_bytes() for n,p in FILES.items()}
OUT=ROOT/'findings/goals-mutations.json'
WORK=ROOT/'tests/TPW.Sim.Tests/obj/goals-mutations'
M=[]
def add(name,old,new,file='ParkObjectives.cs'):
    assert ORIGINAL[file].decode().count(old)==1,(name,'nonunique/missing anchor')
    M.append(dict(name=name,file=file,old=old,new=new))
for name,old,new in [
 ('day gate','!calendar.IsObjectiveDay','calendar.IsObjectiveDay'),
 ('sandbox gate','|| world.RestrictedMode','|| !world.RestrictedMode'),
 ('null definition gate','Definition == null || world.RestrictedMode','Definition != null || world.RestrictedMode'),
 ('admissions inclusive','world.Admissions > Definition.AdmissionsThreshold','world.Admissions >= Definition.AdmissionsThreshold'),
 ('admissions signed','world.Admissions > Definition.AdmissionsThreshold','(int)world.Admissions > (int)Definition.AdmissionsThreshold'),
 ('profit inclusive','> unchecked(Definition.ProfitPounds * 10)','>= unchecked(Definition.ProfitPounds * 10)'),
 ('profit pounds','Definition.ProfitPounds * 10','Definition.ProfitPounds'),
 ('profit no debt','score.Income.Raw - world.OutstandingLoans.Raw - score.Spending.Raw','score.Income.Raw - score.Spending.Raw'),
 ('profit no spending','score.Income.Raw - world.OutstandingLoans.Raw - score.Spending.Raw','score.Income.Raw - world.OutstandingLoans.Raw'),
 ('profit wide','unchecked((int)(score.Income.Raw - world.OutstandingLoans.Raw - score.Spending.Raw))','(score.Income.Raw - world.OutstandingLoans.Raw - score.Spending.Raw)'),
 ('profit unsigned','unchecked((int)(score.Income.Raw - world.OutstandingLoans.Raw - score.Spending.Raw))','unchecked((uint)(score.Income.Raw - world.OutstandingLoans.Raw - score.Spending.Raw))'),
 ('profit threshold wide','unchecked(Definition.ProfitPounds * 10)','unchecked(Definition.ProfitPounds * 10L)'),
 ('years open gate','&& world.ParkOpen &&','&& !world.ParkOpen &&'),
 ('years strict','/ 12 >= Definition.YearsOpen','/ 12 > Definition.YearsOpen'),
 ('years divisor','/ 12 >= Definition.YearsOpen','/ 11 >= Definition.YearsOpen'),
 ('years omit month','calendar.Month + calendar.Year * 12','calendar.Year * 12'),
 ('years omit stamp','- world.OpeningMonth','- 0u'),
 ('years fix underflow','unchecked((uint)(calendar.Month + calendar.Year * 12) - world.OpeningMonth)','Math.Max(0L, (long)calendar.Month + calendar.Year * 12 - world.OpeningMonth)'),
 ('tutorial flag','Definition.TutorialAwardEnabled &&','!Definition.TutorialAwardEnabled &&'),
 ('tutorial sideshow','all.Any(a => a.Item.Type == AttractionType.SideShow)','true'),
 ('tutorial shop','&& shops.Length != 0 &&','&& true &&'),
 ('tutorial feature','features.Length != 0 && rides.Length >= 2','true && rides.Length >= 2'),
 ('tutorial ride minimum','rides.Length >= 2','rides.Length >= 1'),
 ('tutorial transports','rides.Length >= 2','rideCount >= 2'),
 ('security inclusive',') > 80)',') >= 80)'),
 ('security too high',') > 80)',') > 81)'),
 ('security status','features.Count(a => a.Item.Status != AttractionStatus.JustPlaced &&','features.Count(a =>'),
 ('security mask','a.Item.FeatureFlags) & 0x40','a.Item.FeatureFlags) & 0x20'),
 ('security priority','(ParkStatisticCalculator.Category(a.Item.Type, a.Item.FeatureFlags) & 0x40) != 0','(a.Item.FeatureFlags & 8) != 0'),
 ('upgrade gate','rideCount >= 8 && rides.All','rideCount > 8 && rides.All'),
 ('upgrade no gate','rideCount >= 8 && rides.All','rides.All'),
 ('upgrade all','rides.All(a => a.Item.UpgradeLevel != 0)','rides.Any(a => a.Item.UpgradeLevel != 0)'),
 ('upgrade level','a.Item.UpgradeLevel != 0','a.Item.UpgradeLevel >= 2'),
 ('upgrade empty','rides.All(a => a.Item.UpgradeLevel != 0)','rides.Length != 0 && rides.All(a => a.Item.UpgradeLevel != 0)'),
 ('transport omitted','(uint)rides.Length + world.TourTransportCount','(uint)rides.Length'),
 ('aesthetic gate','rideCount >= 8 && featureValue','rideCount >= 7 && featureValue'),
 ('aesthetic strict','featureValue >= Definition.FeatureValuePounds','featureValue > Definition.FeatureValuePounds'),
 ('aesthetic signed','featureValue >= Definition.FeatureValuePounds','(int)featureValue >= (int)Definition.FeatureValuePounds'),
 ('aesthetic halve','(uint)feature.FeaturePricePounds','(uint)feature.FeaturePricePounds / 2'),
 ('aesthetic no sum','featureValue + (uint)feature.FeaturePricePounds','(uint)feature.FeaturePricePounds'),
 ('aesthetic all objects','foreach (var feature in features)','foreach (var feature in all)'),
 ('path gate','rideCount >= 10','rideCount > 10'),
 ('path no gate','rideCount >= 10 &&','true &&'),
 ('path strict','world.PathTileCount <= Definition.MaximumPathTiles','world.PathTileCount < Definition.MaximumPathTiles'),
 ('path inverted','world.PathTileCount <= Definition.MaximumPathTiles','world.PathTileCount >= Definition.MaximumPathTiles'),
 ('path signed','world.PathTileCount <= Definition.MaximumPathTiles','(int)world.PathTileCount <= (int)Definition.MaximumPathTiles'),
 ('green minimum','shops.Length < 5','shops.Length < 4'),
 ('green no minimum','if (shops.Length < 5) return false;','if (shops.Length < 0) return false;'),
 ('green skip placed','shop.Item.Status == AttractionStatus.JustPlaced','shop.Item.Status != AttractionStatus.JustPlaced'),
 ('green bin status','f.Item.Status != AttractionStatus.JustPlaced && f.IsLitterBin','f.IsLitterBin'),
 ('green bin class','&& f.IsLitterBin &&','&& true &&'),
 ('green all shops','return false;\n        }\n        return true;','return true;\n        }\n        return true;'),
 ('green left exclusive',') >= left',') > left'),('green right exclusive','f.X <= right','f.X < right'),
 ('green top exclusive',') >= top',') > top'),('green bottom exclusive','f.Y <= bottom','f.Y < bottom'),
 ('green width omitted','f.X + f.Width','f.X'),('green height omitted','f.Y + f.Height','f.Y'),
 ('green shop width','shop.X + shop.Width + 2','shop.X + 2'),('green shop height','shop.Y + shop.Height + 2','shop.Y + 2'),
 ('green left distance','shop.X - 2','shop.X - 1'),('green top distance','shop.Y - 2','shop.Y - 1'),
 ('green right distance','shop.X + shop.Width + 2','shop.X + shop.Width + 1'),
 ('green bottom distance','shop.Y + shop.Height + 2','shop.Y + shop.Height + 1'),
 ('park mask replace','State.ParkBits | (1u << bit)','(1u << bit)'),
 ('bonus mask replace','State.BonusBits | (1u << bit)','(1u << bit)'),
 ('has swaps masks','bonus ? State.BonusBits : State.ParkBits','bonus ? State.ParkBits : State.BonusBits'),
 ('restore drops bits','public void Restore(ObjectiveState state) => State = state;','public void Restore(ObjectiveState state) => State = default;'),
 ('description sandbox','Definition == null || restrictedMode','Definition == null || !restrictedMode'),
 ('tickets','public int GoldTickets => 1;','public int GoldTickets => 2;'),
]:add(name,old,new)
for bit in range(1,4):
    add('description bit '+str(bit),f'if (!Has(false, {bit})) result.Add',f'if (Has(false, {bit})) result.Add')
for bonus,bit,msg in [(False,1,'AF'),(False,2,'B0'),(False,3,'B1'),(False,4,'BC'),(True,0,'B2'),(True,1,'B3'),(True,2,'B4'),(True,3,'B5'),(True,4,'B6')]:
    flag=str(bonus).lower();old=f'Award({flag}, {bit}, 0x{msg}, result);'
    add('message '+msg,old,f'Award({flag}, {bit}, 0x00, result);')
    add('award bit '+msg,old,f'Award({flag}, {bit+1}, 0x{msg}, result);')
for mask in ('E','D','B','7'):
    add('ride class mask '+mask,'Category(a.Item.Type, 0) & 0xF','Category(a.Item.Type, 0) & 0x'+mask)
for off in ('0C','10','14','18','1C'):
    add('definition offset '+off,f'Word(0x{off})','Word(0x00)','ParkObjectiveDefinition.cs')
for i,line in enumerate(ORIGINAL['ParkObjectiveDefinition.cs'].decode().splitlines()):
    if '", // 0x800E' in line:
        at=line.index('"')+1
        changed=line[:at]+('1' if line[at]!='1' else '2')+line[at+1:]
        add('record '+line.split('// ')[1],line,changed,'ParkObjectiveDefinition.cs')
add('definition aliases export','(byte[])bytes.Clone()','bytes','ParkObjectiveDefinition.cs')

def sha(b):return hashlib.sha256(b).hexdigest()
def run(full=False):
    trx=WORK/'result.trx';trx.unlink(missing_ok=True);start=time.monotonic()
    if full:
        cmd=['dotnet','test','tests/TPW.Sim.Tests/','--nologo','--logger','trx;LogFileName=result.trx','--results-directory',str(WORK)]
    else:
        build=subprocess.run(['dotnet','build','core/TPW.Sim/','--no-restore','--nologo'],cwd=ROOT,capture_output=True,text=True,timeout=180)
        if build.returncode:return dict(status='invalid',compile_error=True,output=build.stdout[-3000:])
        shutil.copy2(ROOT/'core/TPW.Sim/bin/Debug/net8.0/TPW.Sim.dll',ROOT/'tests/TPW.Sim.Tests/bin/Debug/net8.0/TPW.Sim.dll')
        cmd=['dotnet','vstest','tests/TPW.Sim.Tests/bin/Debug/net8.0/TPW.Sim.Tests.dll','--TestCaseFilter:FullyQualifiedName~ParkObjectivesTests',
             '--logger:trx;LogFileName=result.trx','--ResultsDirectory:'+str(WORK)]
    p=subprocess.run(cmd,cwd=ROOT,capture_output=True,text=True,timeout=180)
    ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    counters={};failed=[]
    if trx.exists():
        tree=ET.parse(trx);c=tree.find('.//t:Counters',ns)
        if c is not None:counters={k:int(v) for k,v in c.attrib.items()}
        failed=[e.get('testName') for e in tree.findall('.//t:UnitTestResult',ns) if e.get('outcome')=='Failed']
    valid=counters.get('executed',0)>0 and 'error CS' not in p.stdout
    status='killed' if valid and failed else 'survived' if valid and p.returncode==0 else 'invalid'
    r=dict(status=status,exit_code=p.returncode,counters=counters,failed_tests=failed,seconds=round(time.monotonic()-start,2))
    if status=='invalid':r['output']=(p.stdout+p.stderr)[-3000:]
    return r

def main():
    assert subprocess.check_output(['git','branch','--show-current'],cwd=ROOT,text=True).strip()=='goals'
    WORK.mkdir(parents=True,exist_ok=True)
    for name,data in ORIGINAL.items():(WORK/(name+'.backup')).write_bytes(data)
    audit=dict(sources={n:sha(v) for n,v in ORIGINAL.items()},
        tests_sha256=sha((ROOT/'tests/TPW.Sim.Tests/ParkObjectivesTests.cs').read_bytes()),
        binary_audit_sha256=sha((ROOT/'findings/goals-audit.json').read_bytes()),mutations=[],planned=len(M),restored=False)
    def save():OUT.write_text(json.dumps(audit,indent=2)+'\n')
    def interrupted(signum,frame):raise KeyboardInterrupt(f'signal {signum}')
    for sig in (signal.SIGINT,signal.SIGTERM):signal.signal(sig,interrupted)
    try:
        audit['baseline']=run(True);assert audit['baseline']['status']=='survived',audit['baseline']
        for i,m in enumerate(M,1):
            file=m['file'];FILES[file].write_text(ORIGINAL[file].decode().replace(m['old'],m['new'],1))
            r=run();FILES[file].write_bytes(ORIGINAL[file]);audit['mutations'].append(dict(**m,**r));save()
            print(f"{i}/{len(M)} {r['status']}: {m['name']}",flush=True)
    finally:
        for name,p in FILES.items():p.write_bytes(ORIGINAL[name])
        audit['restored']=all(p.read_bytes()==ORIGINAL[n] for n,p in FILES.items())
        audit['summary']={k:sum(m['status']==k for m in audit['mutations']) for k in ('killed','survived','invalid')}
        audit['summary']['total']=len(audit['mutations']);save()
    audit['final_full_suite']=run(True);save();print(json.dumps(audit['summary']))
    assert len(audit['mutations'])==len(M)>0
    assert audit['summary']['killed']==len(M) and audit['final_full_suite']['status']=='survived'
if __name__=='__main__':main()
