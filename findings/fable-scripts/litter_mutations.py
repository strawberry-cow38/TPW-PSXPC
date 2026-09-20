#!/usr/bin/env python3
"""One production mutation at a time; a failed assertion, not a failed build, must kill it.
Run from any directory. No package install, binary patch, other worktree, or game/ access.
The outer finally restores all captured bytes even on Ctrl-C or a tool/test exception.
--append retests non-kills and newly added mutations against unchanged production, retaining history.
"""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
FILES = {name: ROOT / 'core/TPW.Sim' / file for name, file in {
    'pool': 'Litter.cs', 'drawing': 'LitterDrawing.cs', 'idle': 'VisitorIdle.cs',
    'activity': 'VisitorActivity.cs', 'needs': 'VisitorNeeds.cs', 'handyman': 'StaffClasses.cs',
}.items()}
FILTER = '|'.join('FullyQualifiedName~' + c for c in (
    'LitterTests', 'HandymanTests', 'VisitorIdleTests', 'VisitorActivityTests', 'VisitorNeedsTests'))
MUTATIONS = []

def change(name, file, old, new):
    MUTATIONS.append(dict(name=name, file=file, old=old, new=new))

change('capacity raised to 41', 'pool', 'Capacity = 40;', 'Capacity = 41;')
change('one slot lost at construction', 'pool', 'i < Capacity;', 'i < Capacity - 1;')
change('wrong park statistic', 'pool', 'CountStatistic = 12;', 'CountStatistic = 30;')
change('suppression ignored', 'pool', 'world.LitterSuppressed || free.Count == 0', 'free.Count == 0')
change('pool exhaustion not refused', 'pool', 'world.LitterSuppressed || free.Count == 0', 'world.LitterSuppressed')
change('private object id instead of shared sequence', 'pool', 'int id = world.TakeObjectId();', 'int id = 0;')
change('ordinary sprite draw omitted', 'pool', 'rng.Next(OrdinarySprites.Length)', '0')
for old, new in [('0x9A', '0x9B'), ('0x9B', '0x9C'), ('0x9C', '0x9D'),
                 ('0x9D', '0x9E'), ('0xA0', '0x9F'), ('0x9F', '0xA0')]:
    table = '{ 0x9A, 0x9B, 0x9C, 0x9D, 0xA0, 0x9F }'
    change('sprite table entry ' + old, 'pool', table, table.replace(old, new))
change('initializer retains old id', 'pool', 'Id = id;', 'Id = 0;')
change('initializer retains old sprite', 'pool', 'SpriteId = sprite;\n            ClaimedBy', 'SpriteId = 0;\n            ClaimedBy')
change('initializer retains stale claim', 'pool', 'ClaimedBy = null;\n            // READ', '// ClaimedBy deliberately retained\n            // READ')
change('initializer zeros old coordinates', 'pool', 'Id = id;', 'Id = id; X = Y = 0;')
change('active objects appended not prepended', 'pool', 'live.Insert(0, piece);', 'live.Add(piece);')
change('drawable registration omitted', 'pool', 'world.RegisterLitter(piece);', '// registration omitted')
change('successful drop never placed', 'pool', 'if (piece != null) piece.Scatter(x, y, rng);', '// placement omitted')
change('placement clamps x instead of wrapping', 'pool', 'X = unchecked((short)x);', 'X = (short)Math.Clamp(x, short.MinValue, short.MaxValue);')
change('placement clamps y instead of wrapping', 'pool', 'Y = unchecked((short)y);', 'Y = (short)Math.Clamp(y, short.MinValue, short.MaxValue);')
change('placement ignores requested kind', 'pool', 'SpriteId = sprite;\n        }\n\n        /// <summary>READ: 0x800665FC', '// kind omitted\n        }\n\n        /// <summary>READ: 0x800665FC')
change('scatter axes swapped', 'pool', '=> Place(x + rng.Next', '=> Place(y + rng.Next')
change('scatter second coordinate copied from x', 'pool', 'y + rng.Next(VisitorActivity.LitterOffsetBound)', 'x + rng.Next(VisitorActivity.LitterOffsetBound)')
change('vomit predicate negated', 'pool', 'SpriteId == VisitorActivity.VomitKind', 'SpriteId != VisitorActivity.VomitKind')
change('foreign slots accepted', 'pool', 'if (!live.Contains(piece)) throw new InvalidOperationException("Litter is not in this pool.");', '// ownership check omitted')
change('drawable removal omitted', 'pool', 'world.UnregisterLitter(piece);', '// drawable removal omitted')
change('deleted piece remains live', 'pool', 'live.Remove(piece);', '// live removal omitted')
change('deleted slot never returned', 'pool', 'free.Push(piece);', '// free return omitted')
change('litter decays on its next tick', 'pool', 'public void Tick() { }', 'public void Tick() { live.Clear(); }')
change('claimed pieces can be selected', 'pool', 'piece.ClaimedBy == null && candidate < distance', 'candidate < distance')
change('last equal-distance candidate wins', 'pool', 'candidate < distance', 'candidate <= distance')
change('closest distance not updated', 'pool', 'distance = candidate;', '// distance not updated')
change('nearby radius widened', 'pool', 'NearbyRadius = 2;', 'NearbyRadius = 3;')
change('binary strict radius replaces retained report', 'pool', 'TileDistance(piece, x, y) <= NearbyRadius', 'TileDistance(piece, x, y) < NearbyRadius')
change('vomit excluded from total litter damage', 'pool', 'litter++;', 'if (!piece.IsVomit) litter++;')
change('vomit nearby count omitted', 'pool', 'if (piece.IsVomit) vomit++;\n                }', 'if (piece.IsVomit) { }\n                }')
change('claimed litter stops damaging guests', 'pool', 'if (TileDistance(piece, x, y) <= NearbyRadius)', 'if (piece.ClaimedBy == null && TileDistance(piece, x, y) <= NearbyRadius)')
change('wrong x fixed-point scale', 'pool', '(piece.X >> 8)', '(piece.X >> 7)')
change('wrong y fixed-point scale', 'pool', '(piece.Y >> 8)', '(piece.Y >> 7)')
change('guest x loses halfword semantics', 'pool', '(unchecked((short)x) >> 8)', '(x >> 8)')
change('guest y loses halfword semantics', 'pool', '(unchecked((short)y) >> 8)', '(y >> 8)')
change('negative tiles truncate towards zero', 'pool', '(piece.X >> 8)', '(piece.X / 256)')
change('save ordinary and vomit counts reversed', 'pool', 'return (rubbish, vomit);', 'return (vomit, rubbish);')
change('save omits ordinary pieces', 'pool', 'if (piece.IsVomit) vomit++; else rubbish++;', 'if (piece.IsVomit) vomit++;')
change('save omits vomit pieces', 'pool', 'if (piece.IsVomit) vomit++; else rubbish++;', 'if (!piece.IsVomit) rubbish++;')
change('empty search reports success', 'pool', 'if (piece == null) return false;', 'if (piece == null) return true;')
change('purpose not set before requesting path', 'pool', 'staff.Purpose = StaffClassStates.ToLitter;', '// purpose omitted')
change('staff target not assigned', 'pool', 'staff.TargetLitter = piece;', '// target omitted')
change('staff target flag not assigned before callback', 'pool', 'staff.HasTarget = true;', '// flag omitted')
change('piece not reserved before callback', 'pool', 'piece.ClaimedBy = staff;', '// claim omitted')
change('path is requested to tile centre', 'pool', 'staff, piece.X, piece.Y, 0x11, 0', 'staff, (piece.X & ~255) + 128, (piece.Y & ~255) + 128, 0x11, 0')
change('path flags lose queue support', 'pool', 'staff, piece.X, piece.Y, 0x11, 0', 'staff, piece.X, piece.Y, 1, 0')
change('path argument changed', 'pool', 'staff, piece.X, piece.Y, 0x11, 0', 'staff, piece.X, piece.Y, 0x11, 1')
change('path refusal leaks claim', 'pool', 'Unclaim(staff);\n                return false;', 'return false;')
change('accepted path loses timestamp', 'pool', 'staff.BusyUntil = world.NowTick;', 'staff.BusyUntil = 0;')
change('accepted path reports failure', 'pool', 'return true; // Handyman.Idle', 'return false; // Handyman.Idle')
change('unclaim leaves piece reserved', 'pool', 'if (staff.TargetLitter != null) staff.TargetLitter.ClaimedBy = null;', '// release omitted')
change('unclaim leaves staff pointer', 'pool', 'staff.TargetLitter = null;\n            staff.HasTarget = false;\n        }\n\n        /// <summary>READ: 0x80099224', 'staff.HasTarget = false;\n        }\n\n        /// <summary>READ: 0x80099224')
change('unclaim leaves target flag', 'pool', 'staff.TargetLitter = null;\n            staff.HasTarget = false;\n        }\n\n        /// <summary>READ: 0x80099224', 'staff.TargetLitter = null;\n        }\n\n        /// <summary>READ: 0x80099224')
change('cleaning leaves old claimant', 'pool', 'piece.ClaimedBy = null;\n            pool.Delete', '// release omitted\n            pool.Delete')
change('cleaning leaves piece in park', 'pool', 'pool.Delete(piece, world);', '// deletion omitted')
change('cleaning leaves staff target', 'pool', 'pool.Delete(piece, world);\n            staff.TargetLitter = null;', 'pool.Delete(piece, world);')
change('cleaning leaves target flag', 'pool', 'pool.Delete(piece, world);\n            staff.TargetLitter = null;\n            staff.HasTarget = false;', 'pool.Delete(piece, world);\n            staff.TargetLitter = null;')
change('draws ordinary artwork for vomit', 'drawing', 'world.Sprite(piece.SpriteId)', 'world.Sprite(0x9A)')
change('drawing scale changed', 'drawing', 'SpriteScaleShift = 3;', 'SpriteScaleShift = 2;')
change('width halved after scaling', 'drawing', '(sprite.Width >> 1) << SpriteScaleShift', '(sprite.Width << SpriteScaleShift) >> 1')
change('height halved after scaling', 'drawing', '(sprite.Height >> 1) << SpriteScaleShift', '(sprite.Height << SpriteScaleShift) >> 1')
for variable, coord, sign, extent in [('left', 'X', '-', 'halfX'), ('right', 'X', '+', 'halfX'),
                                      ('top', 'Y', '-', 'halfY'), ('bottom', 'Y', '+', 'halfY')]:
    old = f'short {variable} = unchecked((short)(piece.{coord} {sign} {extent}));'
    new = f'short {variable} = (short)System.Math.Clamp(piece.{coord} {sign} {extent}, short.MinValue, short.MaxValue);'
    change('drawing clamps ' + variable + ' edge', 'drawing', old, new)
change('u far edge not inclusive', 'drawing', 'sprite.U + sprite.Width - 1', 'sprite.U + sprite.Width')
change('v far edge not inclusive', 'drawing', 'sprite.V + sprite.Height - 1', 'sprite.V + sprite.Height')
change('u endpoint saturates instead of wrapping', 'drawing', 'unchecked((byte)(sprite.U + sprite.Width - 1))', '(byte)System.Math.Min(255, sprite.U + sprite.Width - 1)')
change('v endpoint saturates instead of wrapping', 'drawing', 'unchecked((byte)(sprite.V + sprite.Height - 1))', '(byte)System.Math.Min(255, sprite.V + sprite.Height - 1)')
for point in [('left, top', '0'), ('right, top', 'a.Height'), ('left, bottom', 'a.Height'), ('right, bottom', 'a.Height')]:
    change('terrain sample omitted at ' + point[0], 'drawing', 'world.TerrainHeight(' + point[0] + ')', point[1])
change('quad corners C and D swapped', 'drawing', 'sprite, a, b, c, d, Command', 'sprite, a, b, d, c, Command')
change('page and CLUT lost', 'drawing', 'new LitterQuad(sprite, a, b, c, d,', 'new LitterQuad(default, a, b, c, d,')
change('quad not submitted', 'drawing', 'world.SubmitLitterQuad(new LitterQuad(sprite, a, b, c, d, Command, NeutralColour, DepthLimit));', '// draw omitted')
change('transparent rather than opaque FT4', 'drawing', 'Command = 0x2C;', 'Command = 0x2E;')
change('litter tinted black', 'drawing', 'NeutralColour = 0x80;', 'NeutralColour = 0;')
change('depth limit off by one', 'drawing', 'DepthLimit = 2000;', 'DepthLimit = 2001;')
change('bin disposal threshold raised', 'idle', 'SeekBinRubbish = 90;', 'SeekBinRubbish = 91;')
change('misery happiness boundary widened', 'idle', 'guest.Happiness < LitterHappiness', 'guest.Happiness <= LitterHappiness')
change('misery probability boundary widened', 'idle', 'rng.Next(1000) < 100', 'rng.Next(1000) <= 100')
change('binary AND replaces retained vomiting OR', 'idle', 'guest.Nausea <= VomitNausea && rng.Next(4) != 0', 'guest.Nausea <= VomitNausea || rng.Next(4) != 0')
change('wrong vomit sprite', 'activity', 'VomitKind = 0x9E;', 'VomitKind = 0x9D;')
change('symmetric rather than half-open scatter', 'activity', 'LitterOffsetBound = 200;', 'LitterOffsetBound = 201;')
change('scatter subtract changed', 'activity', 'LitterOffsetSubtract = 100;', 'LitterOffsetSubtract = 99;')
change('vomiting ends at deadline equality', 'activity', 'if (world.NowTick <= guest.WaitUntil) return false;', 'if (world.NowTick < guest.WaitUntil) return false;')
change('failed allocation does not cure nausea', 'activity', 'guest.Nausea = 0;', 'if (litter != null) guest.Nausea = 0;')
change('vomiting empties carried rubbish', 'activity', 'guest.Nausea = 0;', 'guest.Nausea = 0; guest.Rubbish = 0;')
change('binary stagger replaces retained litter cadence', 'needs', 'now % LitterPeriod == 0', 'now % LitterPeriod == guest.DecisionStagger % LitterPeriod')
change('litter costs no happiness', 'needs', 'LitterHappiness = 3;', 'LitterHappiness = 0;')
change('vomit costs no nausea', 'needs', 'VomitNausea = 3;', 'VomitNausea = 0;')
change('only one pile charged per needs pass', 'needs', 'LitterHappiness * litter', 'LitterHappiness')
change('only one vomit charged per needs pass', 'needs', 'VomitNausea * vomit', 'VomitNausea')
change('needs penalties only while idle', 'needs', 'if (now % LitterPeriod == 0)', 'if (guest.State == VisitorState.Idle && now % LitterPeriod == 0)')
change('suppression blocks old litter damage', 'needs', 'if (now % LitterPeriod == 0)', 'if (!world.IdleNeedsSuppressed && now % LitterPeriod == 0)')
change('dead state aliases live cleaning', 'handyman', 'DeadClearLitter = (StaffState)25;', 'DeadClearLitter = (StaffState)27;')
change('cleaning ends at equality', 'handyman', 'public static bool CleanLitter(StaffMember staff, IHandymanWorld world)\n        {\n            if (world.NowTick <= staff.BusyUntil)', 'public static bool CleanLitter(StaffMember staff, IHandymanWorld world)\n        {\n            if (world.NowTick < staff.BusyUntil)')
change('ordinary cleanup gives no morale', 'handyman', 'LitterMorale = 1;', 'LitterMorale = 0;')
change('vomit rewards instead of costs morale', 'handyman', 'VomitMorale = -6;', 'VomitMorale = 6;')
change('cleanup costs no tiredness', 'handyman', 'JobTiredness = 5;', 'JobTiredness = 0;')
change('cleaning duration table changed', 'handyman', '{ 120, 60, 30, 20, 10 }', '{ 121, 61, 31, 21, 11 }')
change('Euclidean rather than Manhattan nearest-piece ranking', 'pool',
       'int candidate = TileDistance(piece, x, y);',
       'int dx = (piece.X >> 8) - (unchecked((short)x) >> 8); '
       'int dy = (piece.Y >> 8) - (unchecked((short)y) >> 8); '
       'int candidate = dx * dx + dy * dy;')

ORIGINALS = {key: path.read_bytes() for key, path in FILES.items()}
RESULT = {
    'sources': {key: str(path.relative_to(ROOT)) for key, path in FILES.items()},
    'original_sha256': {key: hashlib.sha256(data).hexdigest() for key, data in ORIGINALS.items()},
    'test_sha256': {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                    for p in sorted((ROOT / 'tests/TPW.Sim.Tests').glob('Litter*.cs'))},
    'filter': FILTER, 'mutations': [], 'restored': False,
}
OUTPUT = ROOT / 'findings/litter-mutations.json'
TODO = MUTATIONS
if '--append' in sys.argv:
    # Preserve evidence from an earlier completed sweep when adding a stronger fixture/mutation.
    # Production must be byte-identical. Old test hashes stay in the history; no result is invented.
    previous = json.loads(OUTPUT.read_text())
    if not previous['restored'] or previous['original_sha256'] != RESULT['original_sha256']:
        raise SystemExit('Append requires the same, fully restored production source.')
    RESULT['earlier_sweeps'] = previous.get('earlier_sweeps', []) + [{
        'baseline': previous['baseline'], 'test_sha256': previous['test_sha256'],
        'summary': previous['summary'], 'final_full_suite': previous.get('final_full_suite'),
        'note': 'Existing assertions retained; a metric-selection fixture and the opposite coordinate-wrap corner were added during the first sweep.',
    }]
    killed = {m['name']: m for m in previous['mutations'] if m['status'] == 'killed'}
    RESULT['mutations'] = list(killed.values())
    RESULT['prior_non_kills'] = [m for m in previous['mutations'] if m['status'] != 'killed']
    TODO = [m for m in MUTATIONS if m['name'] not in killed]

def run_tests(folder, full=False):
    trx = folder / 'result.trx'
    trx.unlink(missing_ok=True)
    command = ['dotnet', 'test', 'tests/TPW.Sim.Tests/', '--no-restore', '--nologo',
               '--logger', 'trx;LogFileName=result.trx', '--results-directory', str(folder)]
    if not full:
        command.extend(['--filter', FILTER])
    start = time.monotonic()
    run = subprocess.run(command, cwd=ROOT, text=True, stdout=subprocess.PIPE,
                         stderr=subprocess.STDOUT, timeout=180)
    data = dict(exit_code=run.returncode, seconds=round(time.monotonic() - start, 2),
                compile_error='error CS' in run.stdout, failed_tests=[], counters={})
    if trx.exists():
        tree = ET.parse(trx)
        ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
        data['failed_tests'] = [e.get('testName') for e in tree.findall('.//t:UnitTestResult', ns)
                                if e.get('outcome') == 'Failed']
        counter = tree.find('.//t:Counters', ns)
        if counter is not None:
            data['counters'] = {k: int(v) for k, v in counter.attrib.items()}
    if not data['counters']:
        data['output'] = run.stdout[-5000:]
    return data

# Top-level restoration guard: no production writes happen outside it.
try:
    with tempfile.TemporaryDirectory(prefix='tpw-litter-mutations-') as tmp:
        folder = Path(tmp)
        RESULT['baseline'] = run_tests(folder, full=True)
        if RESULT['baseline']['exit_code'] != 0:
            raise RuntimeError('Baseline is not green; no mutation was applied.')
        for index, mutation in enumerate(TODO, 1):
            key = mutation['file']
            source = ORIGINALS[key].decode()
            if source.count(mutation['old']) != 1:
                raise RuntimeError(f"Mutation must have one anchor: {mutation['name']!r}")
            FILES[key].write_text(source.replace(mutation['old'], mutation['new'], 1))
            result = run_tests(folder)
            FILES[key].write_bytes(ORIGINALS[key])
            result.update(mutation)
            result['status'] = ('killed' if result['exit_code'] != 0 and result['failed_tests']
                                and not result['compile_error'] else
                                'survived' if result['exit_code'] == 0 else 'invalid')
            RESULT['mutations'].append(result)
            print(f"{index}/{len(TODO)} {result['status']}: {mutation['name']}", flush=True)
            OUTPUT.write_text(json.dumps(RESULT, indent=2) + '\n')
except BaseException as error:
    RESULT['interruption'] = repr(error)
    raise
finally:
    for key, path in FILES.items():
        path.write_bytes(ORIGINALS[key])
    RESULT['restored'] = all(path.read_bytes() == ORIGINALS[key] for key, path in FILES.items())
    RESULT['summary'] = {status: sum(m['status'] == status for m in RESULT['mutations'])
                         for status in ('killed', 'survived', 'invalid')}
    RESULT['summary']['total'] = len(RESULT['mutations'])
    OUTPUT.write_text(json.dumps(RESULT, indent=2) + '\n')

with tempfile.TemporaryDirectory(prefix='tpw-litter-final-') as tmp:
    RESULT['final_full_suite'] = run_tests(Path(tmp), full=True)
OUTPUT.write_text(json.dumps(RESULT, indent=2) + '\n')
if (RESULT['summary']['survived'] or RESULT['summary']['invalid']
        or RESULT['final_full_suite']['exit_code'] != 0):
    raise SystemExit('Sweep needs attention; production files have been restored.')
print(json.dumps(RESULT['summary']), flush=True)
