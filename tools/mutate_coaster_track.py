#!/usr/bin/env python3
"""Isolated production mutations; only an executed failing xunit test counts as a kill.

Sources are restored in finally, baseline is tested before and after, and each TRX is
retained. --resume reruns survivors/unattempted cases, requiring identical production.
"""
import argparse
import hashlib
import json
import subprocess
import time
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
FILES={key:ROOT/'core/TPW.Sim'/name for key,name in
       [('track','CoasterTrack.cs'),('math','CoasterMath.cs'),('sim','CoasterSimulation.cs')]}
ORIGINAL={key:path.read_bytes() for key,path in FILES.items()}
AUDIT=ROOT/'findings/coaster-track-mutations.json'
RESULTS=ROOT/'tests/TPW.Sim.Tests/TestResults/coaster-mutations'
MUTATIONS=[]


def change(name,file,old,new):
    if ORIGINAL[file].decode().count(old)!=1: raise ValueError('nonunique anchor: '+name)
    MUTATIONS.append(dict(name=name,file=file,old=old,new=new))


for name,old,new in [
    ('piece stride','Size = 0x0E','Size = 0x0C'),
    ('record bound','record.Length < RecordSize','record.Length < RecordSize - 1'),
    ('slot origin','SlotsOffset = 0x96','SlotsOffset = 0x98'),
    ('six-bit count','record[0x95] & 0x3F','record[0x95] & 0x1F'),
    ('count includes connection','record[0x95] & 0x3F','record[0x95] & 0x7F'),
    ('read X sign','ReadInt16LittleEndian(bytes),','ReadInt16LittleEndian(bytes) & 0x7FFF,'),
    ('read height offset','ReadInt16LittleEndian(bytes[8..])','ReadInt16LittleEndian(bytes[10..])'),
    ('read kind phase','bytes[12], bytes[13]','bytes[13], bytes[12]'),
    ('write opaque padding','bytes[2..], UnknownPositionPadding','bytes[2..], 0'),
    ('write Z','bytes[4..], TileY','bytes[4..], TileX'),
    ('write check','bytes[6..], CheckWord','bytes[6..], (ushort)(CheckWord & 1)'),
    ('write bank','bytes[10..], Bank','bytes[10..], Height'),
    ('tile scale','(piece.TileX << 8)','(piece.TileX << 7)'),
    ('tile center','(piece.TileY << 8) + 0x80','(piece.TileY << 8) + 0x00'),
    ('host terrain boundary','geometry.BaseHeight + piece.Height','(short)piece.UnknownPositionPadding + piece.Height'),
    ('support height','+ geometry.SupportTopOffset','+ 0'),
    ('height sign narrowing','unchecked((short)(geometry.BaseHeight + piece.Height + geometry.SupportTopOffset))',
        '(geometry.BaseHeight + piece.Height + geometry.SupportTopOffset)'),
    ('spline correction','Anchor.Y + geometry.SplineOffset.Y','Anchor.Y'),
    ('check booleanization','piece.CheckWord != 0','piece.CheckWord == 1'),
    ('length uses next','Previous?.Anchor ?? Anchor','Next?.Anchor ?? Anchor'),
    ('length omits elevation','x * x + y * y + z * z','x * x + z * z'),
    ('runtime capacity','MaximumPieces = 32','MaximumPieces = 64'),
    ('allow closing at full pool','pieces.Count >= MaximumPieces','pieces.Count > MaximumPieces'),
    ('empty closure','pieces.Count != 0 && piece.TileX','piece.TileX'),
    ('closure checks height','piece.TileY == Approach.Piece.TileY)',
        'piece.TileY == Approach.Piece.TileY && piece.Height == Approach.Piece.Height)'),
    ('forget connection on edit','Connected = false; // READ: a subsequent','Connected = true; // READ: a subsequent'),
    ('restore binary bit7 against report','record[0x95] & 0x40','record[0x95] & 0xC0'),
    ('restore assumes closure','if ((record[0x95] & 0x40) != 0 && saved.Length != 0)', 'if (saved.Length != 0)'),
    ('launch validation omitted','Launch.CheckPassed && Approach.CheckPassed','Approach.CheckPassed'),
    ('approach validation omitted','Launch.CheckPassed && Approach.CheckPassed','Launch.CheckPassed'),
    ('used piece validation omitted','pieces.All(p => p.CheckPassed)','true'),
    ('station spline caps','if (previous == Approach && node == Launch)','if (false)'),
    ('spline before neighbour','var before = previous.Previous ?? previous;','var before = previous;'),
    ('bank interpolation','previous.Piece.Bank + ((node.Piece.Bank - previous.Piece.Bank) * fraction >> 12)',
        'previous.Piece.Bank'),
]:
    if name=='read X sign':
        new='unchecked((short)(BinaryPrimitives.ReadInt16LittleEndian(bytes) & 0x7FFF)),'
        old='BinaryPrimitives.ReadInt16LittleEndian(bytes),'
    change(name,'track',old,new)

for name,old,new in [
    ('fraction multiply first','unchecked((One / length) * distance) >> 8','unchecked(One * distance / length) >> 8'),
    ('fraction distance scale','* distance) >> 8','* distance) >> 12'),
    ('conventional spline tension','Tension = -2048','Tension = 0'),
    ('exact sqrt instead of lookup','(int)((uint)(Root[mantissa - 64] << exponent) >> 12)','(int)Math.Sqrt(squared)'),
    ('root table last entry','8143,8159,8175','8143,8159,8160'),
    ('normal scale','int factor = Normal[mantissa - 64];','int factor = Normal[mantissa - 64] / 2;'),
    ('omit derivative normalization','var tangent = Normalize(Weighted(p0, p1, p2, p3, a, b, c, d));',
        'var tangent = Weighted(p0, p1, p2, p3, a, b, c, d);'),
    ('spline swaps middle controls','var position = Weighted(p0, p1, p2, p3, a, b, c, d);',
        'var position = Weighted(p0, p2, p1, p3, a, b, c, d);'),
    ('derivative last coefficient sign','k * (3 * t2 - 2 * t)','k * (2 * t - 3 * t2)'),
]: change(name,'math',old,new)

for name,old,new in [
    ('gravity sign','Gravity = -4096','Gravity = 4096'),
    ('friction','Friction = 32','Friction = 0'),
    ('approach brake','ApproachBrake = 512','ApproachBrake = 32'),
    ('minimum speed','MinimumSpeed = 2048','MinimumSpeed = 1024'),
    ('maximum speed','MaximumSpeed = 20480','MaximumSpeed = 40960'),
    ('preview delay','PreviewDelay = 240','PreviewDelay = 241'),
    ('launch speed shift','launchSpeed << 8','launchSpeed << 12'),
    ('launch starting fraction','* 0xC00','* 0x800'),
    ('launch flag reset','Control.StillOnLaunchSegment = true;','Control.StillOnLaunchSegment = false;'),
    ('launch ready reset','Control.ReadyToUnload = false;','// no ready reset'),
    ('launch lap reset','Control.Laps = 0;','// no lap reset'),
    ('preview hold units','(elapsed >> 12) < PreviewDelay','elapsed < PreviewDelay'),
    ('preview applies to ordinary trains','Control.StillOnLaunchSegment && Control.IsPreview &&','Control.StillOnLaunchSegment &&'),
    ('special force exemption','if (Segment.Piece.Kind != 1)','if (true)'),
    ('brake identifies launch','Segment == LaunchSegment.Previous','Segment == LaunchSegment'),
    ('omit slope','(Tangent.Y * Gravity >> 12)','0'),
    ('wide slope product','Tangent.Y * Gravity >> 12','(int)((long)Tangent.Y * Gravity >> 12)'),
    ('ignore speed slider','(MaximumSpeed * world.SpeedSlider) / 100','MaximumSpeed'),
    ('wide slider product','(MaximumSpeed * world.SpeedSlider) / 100','(int)((long)MaximumSpeed * world.SpeedSlider / 100)'),
    ('no speed maximum','if (Speed > maximum) Speed = maximum;','// no maximum'),
    ('no speed minimum','if (Speed < MinimumSpeed) Speed = MinimumSpeed;','// no minimum'),
    ('signed movement shift','(int)((uint)(Speed * world.MovementDelta) >> 12)','(Speed * world.MovementDelta) >> 12'),
    ('delta ignored','Speed * world.MovementDelta','Speed * 4096'),
    ('no displacement minimum','if (step < MinimumSpeed) step = MinimumSpeed;','// no displacement minimum'),
    ('half speed omitted','if (world.HalfSpeed) step >>= 1;','// no half speed'),
    ('distance integration omitted','Distance += step;','// no integration'),
    ('zero length completion','if (Segment.Length == 0) return false;','// no zero-length return'),
    ('backwards traversal omitted','if (fraction < 0)','if (false)'),
    ('handoff threshold inclusive','fraction >= CoasterMath.One','fraction > CoasterMath.One'),
    ('forward handoff goes backwards','Segment = Segment.Next ??','Segment = Segment.Previous ??'),
    ('handoff ignores new length','(Segment.Length << 8) / CoasterMath.One) * (fraction - CoasterMath.One)',
        '(LaunchSegment.Length << 8) / CoasterMath.One) * (fraction - CoasterMath.One)'),
    ('no launch flag clear','Control.StillOnLaunchSegment = false;','// no clear'),
    ('completion call removed','Control.AfterMovement(Segment == track.Launch, fraction, world.Duration);','// no completion call'),
    ('completion return identity','Control.AfterMovement(Segment == track.Launch, fraction, world.Duration);',
        'Control.AfterMovement(Segment == track.Approach, fraction, world.Duration);'),
    ('postwrap fraction substituted','Control.AfterMovement(Segment == track.Launch, fraction, world.Duration);',
        'Control.AfterMovement(Segment == track.Launch, CoasterMath.Fraction(Distance, Segment.Length), world.Duration);'),
    ('fixed duration','fraction, world.Duration);','fraction, 1);'),
    ('stale slope cache','Tangent = Pose.Tangent;','// no slope refresh'),
    ('train pool count','TrainCount = 8','TrainCount = 9'),
    ('update dispatch removed','Controller.UpdateDispatch(this);','// no dispatch update'),
    ('elapsed delta ignored','Elapsed + host.MovementDelta','Elapsed + 1'),
    ('invalid route does not eject','if (!train.Control.IsPreview) UnloadTrain(train.Control);','// no active ejection'),
    ('invalid pending not emptied','pending.Clear();\n            }','// no pending clear\n            }'),
    ('running controller omitted','case AttractionStatus.Running: RollerCoaster.RunTick(this); break;',
        'case AttractionStatus.Running: break;'),
    ('loading controller omitted','case AttractionStatus.Loading: RollerCoaster.LoadTick(this); break;',
        'case AttractionStatus.Loading: break;'),
    ('warning controller omitted','RollerCoaster.RunTick(this); break; // READ: 0x800B10B4','break; // READ: 0x800B10B4'),
    ('preview boarding veto omitted','active.Any(t => t.Control.IsPreview)','false'),
    ('pending not counted as riders','Riders++;','// no rider increment'),
    ('dispatch active order','active.Insert(0, train);','active.Add(train);'),
    ('dispatch pending copy','new List<Visitor>(pending)','new List<Visitor>()'),
    ('dispatch elapsed reset omitted','Elapsed = 0; // READ: 0x800B023C','// no reset; // READ: 0x800B023C'),
    ('unload FIFO','for (int i = guests.Count - 1; i >= 0; i--)','for (int i = 0; i < guests.Count; i++)'),
    ('unload pool recycling omitted','free.Push(motion);','// no recycling'),
]: change(name,'sim',old,new)


def run(label):
    directory=RESULTS/label;directory.mkdir(parents=True,exist_ok=True)
    trx=directory/'result.trx'
    if trx.exists():trx.unlink()
    command=['dotnet','test',str(ROOT/'tests/TPW.Sim.Tests'),'--no-restore','--nologo','--verbosity','quiet',
             '--filter','FullyQualifiedName~CoasterTrackTests','--logger','trx;LogFileName=result.trx',
             '--results-directory',str(directory)]
    start=time.monotonic()
    p=subprocess.run(command,cwd=ROOT,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=180)
    (directory/'output.txt').write_text(p.stdout)
    failures=[];counts={}
    if trx.exists():
        root=ET.parse(trx).getroot();ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
        counts=root.find('t:ResultSummary/t:Counters',ns).attrib
        failures=[x.attrib['testName'] for x in root.findall('t:Results/t:UnitTestResult',ns) if x.attrib['outcome']=='Failed']
    status='killed' if p.returncode!=0 and failures else 'survived' if p.returncode==0 and int(counts.get('executed',0))>0 else 'invalid'
    return dict(result=status,returncode=p.returncode,counts=counts,failed_tests=failures,
                seconds=round(time.monotonic()-start,3),trx=str(trx.relative_to(ROOT)))


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--resume',action='store_true');args=parser.parse_args()
    hashes={k:hashlib.sha256(v).hexdigest() for k,v in ORIGINAL.items()}
    audit=dict(production_sha256=hashes,mutations=[],filter='FullyQualifiedName~CoasterTrackTests')
    if args.resume and AUDIT.exists():
        audit=json.loads(AUDIT.read_text());assert audit['production_sha256']==hashes,'production changed since audit'
    audit['test_sha256']=hashlib.sha256((ROOT/'tests/TPW.Sim.Tests/CoasterTrackTests.cs').read_bytes()).hexdigest()
    audit['fixture_sha256']=hashlib.sha256((ROOT/'findings/coaster-source.json').read_bytes()).hexdigest()
    completed={m['name']:m for m in audit['mutations']}
    def save():AUDIT.write_text(json.dumps(audit,indent=2)+'\n')
    baseline=run('baseline');audit['baseline']=baseline;save()
    if baseline['result']!='survived':raise SystemExit('Baseline must pass before mutations')
    try:
        for i,mutation in enumerate(MUTATIONS):
            name=mutation['name']
            if name in completed and completed[name]['result']=='killed':continue
            key=mutation['file'];FILES[key].write_text(ORIGINAL[key].decode().replace(mutation['old'],mutation['new'],1))
            try:result=run(f'{i:03d}-{audit["test_sha256"][:8]}-{audit["fixture_sha256"][:8]}')
            finally:FILES[key].write_bytes(ORIGINAL[key])
            history=[]
            if name in completed:
                previous=completed[name]
                history=previous.get('previous_attempts',[])+[{k:v for k,v in previous.items()
                    if k not in ('previous_attempts','old','new','file','name')}]
            entry=mutation|result|dict(test_sha256=audit['test_sha256'],fixture_sha256=audit['fixture_sha256'],
                                      previous_attempts=history)
            completed[name]=entry;audit['mutations']=list(completed.values());save()
            print(f'{i+1}/{len(MUTATIONS)} {result["result"]}: {name}',flush=True)
    finally:
        for key,path in FILES.items():path.write_bytes(ORIGINAL[key])
        audit['restored_baseline']=run('restored');save()
    audit['total']=len(MUTATIONS);audit['killed']=sum(m['result']=='killed' for m in audit['mutations'])
    audit['all_killed']=audit['killed']==audit['total'] and audit['restored_baseline']['result']=='survived';save()
    print(f'{audit["killed"]}/{audit["total"]} killed; {AUDIT}')
    if not audit['all_killed']:raise SystemExit(1)


if __name__=='__main__':main()
