#!/usr/bin/env python3
"""One production mutation at a time; frozen tests, explicit nonempty/no-skip control.
Compilation failures never count as kills. Restore source on every exit. Worktree only.
"""
import hashlib
import json
from pathlib import Path
import re
import shutil
import signal
import subprocess
import time
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'core/TPW.Data/PalParkDefinition.cs'
ORIGINAL=SOURCE.read_text()
WORK=ROOT/'tests/TPW.Sim.Tests/obj/scenario-mutations'
OUT=ROOT/'findings/scenario-mutations.json'
M=[]
def add(name,old,new):
    assert ORIGINAL.count(old)==1,(name,'missing/nonunique anchor')
    M.append(dict(name=name,old=old,new=new))

for name,old,new in [
 ('pair stride','public int Stride => Type == 8 ? 8 : 4;','public int Stride => Type == 8 ? 4 : 4;'),
 ('pair count','words.Length / (Stride / 4)','words.Length'),
 ('entry index','words[index * (Stride / 4)]','words[index]'),
 ('index upper bound','(uint)index >= (uint)Count','(uint)index > (uint)Count'),
 ('list endian','ReadUInt32LittleEndian(data.Slice(i * 4, 4))','ReadUInt32BigEndian(data.Slice(i * 4, 4))'),
 ('list encoder endian','WriteUInt32LittleEndian(result.AsSpan(i * 4, 4), words[i])','WriteUInt32BigEndian(result.AsSpan(i * 4, 4), words[i])'),
 ('drop list tail','i < words.Length; i++)\n            BinaryPrimitives.Write','i < words.Length - 1; i++)\n            BinaryPrimitives.Write'),
 ('objective size','ObjectiveRecordSize = 0x34','ObjectiveRecordSize = 0x30'),
 ('objective stride','(World * 2 + Park) * ObjectiveRecordSize','(World * 2 + Park) * 0x30'),
 ('objective park selector','(World * 2 + Park) * ObjectiveRecordSize','(World * 2) * ObjectiveRecordSize'),
 ('objective base','=> 0xD1930 +','=> 0xD1934 +'),
 ('map world stride','World * 0x10','World * 8'),
 ('map park stride','Park * 8;','Park * 4;'),
 ('map endian','MapEntry = BinaryPrimitives.ReadUInt32LittleEndian(map);','MapEntry = BinaryPrimitives.ReadUInt32BigEndian(map);'),
 ('scenery offset','map.Slice(4)','map.Slice(0)'),
 ('ticket field','objective[0x31]','objective[0x30]'),
 ('world bound','(uint)world >= 4','(uint)world > 4'),
 ('park bound','(uint)park >= 2','(uint)park > 2'),
 ('world signed','(uint)world >= 4','world >= 4'),
 ('park signed','(uint)park >= 2','park >= 2'),
 ('layout park selection','Layouts[world * 2 + park]','Layouts[world * 2]'),
 ('loaded pair stride','count * (Types[i] == 8 ? 8 : 4)','count * 4'),
 ('type lookup','catalogues[i].Type == type','catalogues[i].Type != type'),
 ('objective alias export','(byte[])objective.Clone()','objective'),
 ('drop objective write','objective.CopyTo(result, ObjectiveFileOffset);','// omitted objective write'),
 ('swap map encoder','result.AsSpan(MapPairFileOffset), MapEntry','result.AsSpan(MapPairFileOffset), SceneryEntry'),
 ('swap scenery encoder','result.AsSpan(MapPairFileOffset + 4), SceneryEntry','result.AsSpan(MapPairFileOffset + 4), MapEntry'),
 ('drop catalogue write','foreach (var group in catalogues) group.Encode().CopyTo(result, group.FileOffset);','// omitted catalogue write'),
 ('unsigned fee immediate','instructionHigh == 0x2405 ? unchecked((short)word) : (int)(word & 0xFFFF)','(int)(word & 0xFFFF)'),
 ('signed starting money','instructionHigh == 0x2405 ? unchecked((short)word) : (int)(word & 0xFFFF)','unchecked((short)word)'),
 ('default money hardcoded','=> new(Immediate(executable, 0x80058AA0, 0x3405)','=> new(50000'),
 ('flag low byte','Word(executable, 0x80102E88) != 0','(Word(executable, 0x80102E88) & 0xFF) != 0'),
 ('flag exactly one','Word(executable, 0x80102E88) != 0','Word(executable, 0x80102E88) == 1'),
 ('sandbox inverted','Word(executable, 0x80102D34) != 0','Word(executable, 0x80102D34) == 0'),
 ('flag swapped','Word(executable, 0x80102E88) != 0','Word(executable, 0x80102D34) != 0'),
 ('instruction validation','word >> 16 != instructionHigh','false'),
]:add(name,old,new)
for off in (0,4,8,0x20,0x24,0x28,0x2c,0x32,0x33):
    old='objective = Slice(exe, ObjectiveFileOffset, ObjectiveRecordSize).ToArray();'
    add(f'normalize opaque byte {off:02x}',old,old+f' objective[{off}] = 0;')
# Every one of 64 lists, including five empty lists and ten short type-8 pairs.
rows=[x for x in ORIGINAL.splitlines() if 'new (uint, int)[] {' in x]
assert len(rows)==8
for park,row in enumerate(rows):
    pairs=list(re.finditer(r'\(0x([0-9A-F]+),(\d+)\)',row));assert len(pairs)==8
    for group,match in enumerate(pairs):
        a,n=match.groups()
        changed=row[:match.start()]+f'(0x{a},{int(n)+1})'+row[match.end():]
        add(f'park {park} group {group} count',row,changed)
    match=pairs[park];a,n=match.groups()
    changed=row[:match.start()]+f'(0x{int(a,16)+4:08X},{n})'+row[match.end():]
    add(f'park {park} group {park} address',row,changed)

def run(full=False):
    trx=WORK/'result.trx';trx.unlink(missing_ok=True);start=time.monotonic()
    if full:
        cmd=['dotnet','test','tests/TPW.Sim.Tests/','--nologo','--logger','trx;LogFileName=result.trx','--results-directory',str(WORK)]
    else:
        p=subprocess.run(['dotnet','build','core/TPW.Data/','--no-restore','--nologo'],cwd=ROOT,capture_output=True,text=True,timeout=180)
        if p.returncode:return dict(status='invalid',output=(p.stdout+p.stderr)[-3000:])
        shutil.copy2(ROOT/'core/TPW.Data/bin/Debug/net8.0/TPW.Data.dll',ROOT/'tests/TPW.Sim.Tests/bin/Debug/net8.0/TPW.Data.dll')
        cmd=['dotnet','vstest','tests/TPW.Sim.Tests/bin/Debug/net8.0/TPW.Sim.Tests.dll',
            '--TestCaseFilter:FullyQualifiedName~PalParkDefinitionTests','--logger:trx;LogFileName=result.trx','--ResultsDirectory:'+str(WORK)]
    p=subprocess.run(cmd,cwd=ROOT,capture_output=True,text=True,timeout=180)
    ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};counts={};failed=[]
    if trx.exists():
        tree=ET.parse(trx);c=tree.find('.//t:Counters',ns)
        if c is not None:counts={k:int(v) for k,v in c.attrib.items()}
        failed=[e.get('testName') for e in tree.findall('.//t:UnitTestResult',ns) if e.get('outcome')=='Failed']
    valid=counts.get('executed',0)>=18 and counts.get('notExecuted',0)==0
    status='killed' if valid and failed else 'survived' if valid and p.returncode==0 else 'invalid'
    return dict(status=status,exit_code=p.returncode,counters=counts,failed_tests=failed,
                seconds=round(time.monotonic()-start,2),**({'output':(p.stdout+p.stderr)[-3000:]} if status=='invalid' else {}))

def main():
    assert ROOT==Path('/home/ec2-user/tpwport-scenario')
    assert subprocess.check_output(['git','branch','--show-current'],cwd=ROOT,text=True).strip()=='scenario'
    WORK.mkdir(parents=True,exist_ok=True)
    (WORK/'PalParkDefinition.cs.backup').write_text(ORIGINAL)
    audit=dict(source_sha256=hashlib.sha256(ORIGINAL.encode()).hexdigest(),
        tests_sha256=hashlib.sha256((ROOT/'tests/TPW.Sim.Tests/PalParkDefinitionTests.cs').read_bytes()).hexdigest(),
        binary_audit_sha256=hashlib.sha256((ROOT/'findings/scenario-audit.json').read_bytes()).hexdigest(),
        planned=len(M),mutations=[],restored=False)
    def save():OUT.write_text(json.dumps(audit,indent=2)+'\n')
    def interrupted(signum,frame):raise KeyboardInterrupt(f'signal {signum}')
    for sig in (signal.SIGINT,signal.SIGTERM):signal.signal(sig,interrupted)
    try:
        audit['baseline']=run(True);assert audit['baseline']['status']=='survived',audit['baseline'];save()
        frozen=hashlib.sha256((ROOT/'tests/TPW.Sim.Tests/bin/Debug/net8.0/TPW.Sim.Tests.dll').read_bytes()).hexdigest()
        audit['frozen_test_assembly_sha256']=frozen
        for i,m in enumerate(M,1):
            SOURCE.write_text(ORIGINAL.replace(m['old'],m['new'],1))
            r=run();SOURCE.write_text(ORIGINAL)
            assert hashlib.sha256((ROOT/'tests/TPW.Sim.Tests/bin/Debug/net8.0/TPW.Sim.Tests.dll').read_bytes()).hexdigest()==frozen
            audit['mutations'].append(dict(**m,**r));save()
            print(f"{i}/{len(M)} {r['status']}: {m['name']}",flush=True)
    finally:
        SOURCE.write_text(ORIGINAL);audit['restored']=SOURCE.read_text()==ORIGINAL
        audit['summary']={k:sum(m['status']==k for m in audit['mutations']) for k in ('killed','survived','invalid')}
        audit['summary']['total']=len(audit['mutations']);save()
    audit['final_full_suite']=run(True);save();print(json.dumps(audit['summary']))
    assert len(audit['mutations'])==len(M)>0 and audit['summary']['killed']==len(M)
    assert audit['final_full_suite']['status']=='survived'

if __name__=='__main__':main()
