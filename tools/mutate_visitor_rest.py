#!/usr/bin/env python3
"""One production mutation at a time; test failure required, compiler errors do not count.
Run from any directory. Sources are restored in finally; logs go outside the checkout.
Use --only TEXT to rerun a rule after strengthening a rejecting fixture.
"""
import argparse
import json
from pathlib import Path
import re
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
MUTATIONS = []
def add(file, name, old, new, occurrence=0):
    MUTATIONS.append((file, name, old, new, occurrence))

A = 'VisitorActivity.cs'
for name, old, new in [
    ('vomit early equality', 'world.NowTick <= guest.WaitUntil', 'world.NowTick < guest.WaitUntil'),
    ('vomit ignores deadline', 'if (world.NowTick <= guest.WaitUntil) return false;', ''),
    ('vomit ignores allocation', 'if (litter != null)', 'if (true)'),
    ('vomit skips placement', 'world.PlaceLitter(litter, x, y, VomitKind);', ';'),
    ('vomit keeps nausea', 'guest.Nausea = 0;', ';'),
    ('vomit keeps animation', 'guest.Animation = VisitorQueue.AnimationIdle;', ';'),
    ('vomit pops', 'guest.SetState(VisitorState.Idle);', 'guest.PopState();'),
    ('vomit remains sick', 'guest.SetState(VisitorState.Idle);', ';'),
    ('vomit wide x', 'unchecked((short)(position.X + rng.Next(LitterOffsetBound) - LitterOffsetSubtract))', '(position.X + rng.Next(LitterOffsetBound) - LitterOffsetSubtract)'),
    ('vomit wide y', 'unchecked((short)(position.Y + rng.Next(LitterOffsetBound) - LitterOffsetSubtract))', '(position.Y + rng.Next(LitterOffsetBound) - LitterOffsetSubtract)'),
    ('vomit offset bound', 'LitterOffsetBound = 200', 'LitterOffsetBound = 201'),
    ('vomit offset subtraction', 'LitterOffsetSubtract = 100', 'LitterOffsetSubtract = 99'),
    ('vomit wrong kind', 'VomitKind = 0x9E', 'VomitKind = 0x9D'),
    ('watch animation', 'WatchAnimation = 2', 'WatchAnimation = 3'),
    ('watch bonus', 'WatchHappiness = 5', 'WatchHappiness = 4'),
    ('watch cooldown', 'WatchCooldownTicks = 900', 'WatchCooldownTicks = 899'),
    ('watch base duration', 'WatchBaseTicks = 300', 'WatchBaseTicks = 301'),
    ('watch skill duration', 'WatchSkillTicks = 60', 'WatchSkillTicks = 61'),
    ('watch skill mask', 'WatchSkillMask = 7', 'WatchSkillMask = 3'),
    ('watch skips animation', 'guest.Animation = WatchAnimation;', ';'),
    ('watch skips facing', 'guest.Facing = Facing((target.X >> 8) - (position.X >> 8), (target.Y >> 8) - (position.Y >> 8));', ';'),
    ('watch reversed x', 'Facing((target.X >> 8) - (position.X >> 8), (target.Y >> 8) - (position.Y >> 8))', 'Facing((position.X >> 8) - (target.X >> 8), (target.Y >> 8) - (position.Y >> 8))'),
    ('watch reversed y', 'Facing((target.X >> 8) - (position.X >> 8), (target.Y >> 8) - (position.Y >> 8))', 'Facing((target.X >> 8) - (position.X >> 8), (position.Y >> 8) - (target.Y >> 8))'),
    ('watch ignores performance', 'entertainer.State == EntertainerStates.Entertaining &&', 'true &&'),
    ('watch ignores timeout', 'world.NowTick <= guest.WaitUntil)\n                return false;', 'true)\n                return false;'),
    ('watch requires both end conditions', 'EntertainerStates.Entertaining && world.NowTick', 'EntertainerStates.Entertaining || world.NowTick'),
    ('watch ends at equality', 'world.NowTick <= guest.WaitUntil)\n                return false;', 'world.NowTick < guest.WaitUntil)\n                return false;'),
    ('watch sets idle', 'guest.PopState();', 'guest.SetState(VisitorState.Idle);'),
    ('watch omits pop', 'guest.PopState();', ';'),
    ('watch omits bonus', 'guest.Happiness = Stat.Add(guest.Happiness, WatchHappiness);', ';'),
    ('watch leaves animation two', 'guest.Animation = VisitorQueue.AnimationWander;', ';'),
    ('watch keeps target', 'guest.WatchedEntertainer = null;', ';'),
    ('watch omits cooldown', 'guest.EntertainerNotBefore = world.NowTick + WatchCooldownTicks;', ';'),
    ('watch absolute cooldown', 'world.NowTick + WatchCooldownTicks', 'WatchCooldownTicks'),
    ('facing negative x', 'dx < 0 ? 2', 'dx < 0 ? 6'),
    ('facing positive x', 'dx > 0 ? 6', 'dx > 0 ? 2'),
    ('facing positive y', 'dy > 0 ? 0', 'dy > 0 ? 4'),
    ('facing negative y', 'dy > 0 ? 0 : 4', 'dy > 0 ? 0 : 0'),
    ('facing y priority', 'dx < 0 ? 2 : dx > 0 ? 6 : dy > 0 ? 0 : 4', 'dy > 0 ? 0 : dy < 0 ? 4 : dx < 0 ? 2 : 6'),
]: add(A, name, old, new)

N = 'VisitorNeeds.cs'
for name, old, new in [
    ('influence raw state two', 'guest.State == VisitorState.WalkToDestination', 'guest.State == VisitorState.Wander'),
    ('influence raw state three', 'guest.State == VisitorState.WalkToWaypoint', 'guest.State == VisitorState.WalkToBin'),
    ('watch no queue gate', 'if (world.InQueue(guest)) return;', ''),
    ('watch no depth gate', 'if (guest.StackDepth >= 2) return;', ''),
    ('watch depth one blocked', 'guest.StackDepth >= 2', 'guest.StackDepth >= 1'),
    ('watch no cooldown gate', 'if (world.NowTick <= guest.EntertainerNotBefore) return;', ''),
    ('watch cooldown equality', 'world.NowTick <= guest.EntertainerNotBefore', 'world.NowTick < guest.EntertainerNotBefore'),
    ('watch no current state gate', 'if (guest.State == VisitorState.WatchEntertainer) return;', ''),
    ('watch ignores influence bit', 'if ((flags & TileInfluence.Entertainer) != 0) TryWatch(guest, world);', 'TryWatch(guest, world);'),
    ('watch last equal wins', 'candidate.Distance < distance', 'candidate.Distance <= distance'),
    ('watch chooses farthest', 'candidate.Distance < distance', 'candidate.Distance > distance'),
    ('watch first only', 'nearest == null || candidate.Distance < distance', 'nearest == null'),
    ('watch no distance update', 'distance = candidate.Distance;', ';'),
    ('watch skips target assignment', 'guest.WatchedEntertainer = nearest;', ';'),
    ('watch sets instead of pushes', 'guest.PushState(VisitorState.WatchEntertainer);', 'guest.SetState(VisitorState.WatchEntertainer);'),
    ('watch skips state push', 'guest.PushState(VisitorState.WatchEntertainer);', ';'),
    ('watch ignores empty list', 'if (nearest == null) return;', ''),
    ('watch absolute deadline', 'world.NowTick + VisitorActivity.WatchBaseTicks', 'VisitorActivity.WatchBaseTicks'),
    ('watch no skill mask', '(nearest.Skill & VisitorActivity.WatchSkillMask)', 'nearest.Skill'),
]: add(N, name, old, new)

W = 'VisitorWander.cs'
for constant, value in [('StepRollBound', 10), ('KeepDirectionRollBound', 100), ('RerollBelow', 20),
                        ('RingRollBound', 5), ('RingBaseRadius', 4), ('CentreRollBound', 20),
                        ('CentreOffset', 10), ('CentreAttempts', 4), ('PathFlags', 3), ('SecondaryPathFlags', 0)]:
    add(W, 'wander constant ' + constant, f'{constant} = {value}', f'{constant} = {value+1}')
for name, old, new, *occurrence in [
    ('wander one pushes five', 'guest.SetState(VisitorState.RandomWander);', 'guest.PushState(VisitorState.RandomWander);'),
    ('wander one skips transition', 'guest.SetState(VisitorState.RandomWander);', ';'),
    ('wander skips state one gate', 'guest.State == VisitorState.Wander', 'false'),
    ('wander swaps terrain arms', 'if (!world.IsPath(x, y))', 'if (world.IsPath(x, y))'),
    ('wander extra step', 'step < steps', 'step <= steps'),
    ('wander ignores map edge', 'VisitorWalking.OnMap(world, nx, ny) &&', 'true &&'),
    ('wander ignores path predicate', '&& world.IsPath(nx, ny)', '&& true'),
    ('wander ignores links', '&& (links & LinkBits[direction]) != 0', '&& true'),
    ('wander reads neighbour links', '(links & LinkBits[direction])', '(world.PathLinks(nx, ny) & LinkBits[direction])'),
    ('wander weighted even when keeping', 'rng.Next(KeepDirectionRollBound) < RerollBelow', 'rng.Next(KeepDirectionRollBound) >= 0'),
    ('wander rerolls equality', 'rng.Next(KeepDirectionRollBound) < RerollBelow', 'rng.Next(KeepDirectionRollBound) <= RerollBelow'),
    ('wander rolls keep when blocked', '!candidates.Contains(previous) || rng.Next(KeepDirectionRollBound) < RerollBelow', 'rng.Next(KeepDirectionRollBound) < RerollBelow || !candidates.Contains(previous)'),
    ('wander always new turn row', 'if (previous < 0)', 'if (true)'),
    ('wander missing row die', 'previous = rng.Next(Dx.Length);', 'previous = 0;'),
    ('wander conventional weight boundary', 'cumulative >= roll', 'cumulative > roll'),
    ('wander no x step', 'x += Dx[previous];', ';'),
    ('wander no y step', 'y += Dy[previous];', ';'),
    ('wander omits centres', 'points.Add((VisitorQueue.Centre(x), VisitorQueue.Centre(y)));', 'points.Add((x << 8, y << 8));'),
    ('wander no waypoint', 'points.Add((VisitorQueue.Centre(x), VisitorQueue.Centre(y)));', ';'),
    ('wander ring no purpose', 'guest.Purpose = Purpose.Finished;', ';', 1),
    ('wander path no purpose', 'guest.Purpose = Purpose.Finished;', ';', 0),
    ('wander request no purpose', 'guest.Purpose = Purpose.Finished;', ';', 2),
    ('wander path no state three', 'guest.SetState(VisitorState.WalkToWaypoint);', ';', 0),
    ('wander ring no state three', 'guest.SetState(VisitorState.WalkToWaypoint);', ';', 1),
    ('wander ring skip failed allocation gate', 'if (world.WaypointHead(guest) < 0)', 'if (false)'),
    ('wander ring skips idle', 'guest.SetState(VisitorState.Idle);', ';', 0),
    ('wander y centre uses width', 'world.MapHeight / 2', 'world.MapWidth / 2'),
    ('wander x centre uses height', 'world.MapWidth / 2', 'world.MapHeight / 2'),
    ('wander fallback no map gate', '!VisitorWalking.OnMap(world, x, y) ||', 'false ||'),
    ('wander fallback no path gate', '|| !world.IsPath(x, y)', '|| false'),
    ('wander no retry flag', 'guest.ExitPathRetried = true;', ';'),
    ('wander no request timestamp', 'guest.WaitUntil = world.NowTick;', ';'),
    ('wander request sets eleven', 'guest.PushState(VisitorState.WalkToBin);', 'guest.SetState(VisitorState.WalkToBin);'),
    ('wander ignores path acceptance', 'if (!world.TryRequestPath', 'if (world.TryRequestPath'),
    ('wander failure stays five', 'guest.SetState(VisitorState.Idle);', ';', 1),
    ('wander forgets old chain', 'VisitorWalking.FreeWaypoints(guest, world);', ';'),
    ('wander forward allocation', 'for (int i = points.Count - 1; i >= 0; i--)', 'for (int i = 0; i < points.Count; i++)'),
    ('wander rollback partial allocation', 'if (entry < 0) break;', 'if (entry < 0) { world.Waypoints.FreeChain(head); head = -1; break; }'),
    ('wander omits encode', 'world.Waypoints.Encode(entry, points[i].X, points[i].Y);', ';'),
    ('wander omits links', 'world.Waypoints.SetNext(entry, head);', ';'),
    ('wander skips head update', 'head = entry;', ';'),
    ('wander skips installation', 'world.SetWaypointHead(guest, head);', ';'),
]:
    add(W, name, old, new, occurrence[0] if occurrence else 0)
# Each stored table entry is challenged independently, including otherwise easy-to-miss zero offsets.
source = (ROOT / 'core/TPW.Sim' / W).read_text()
for table in ['Dx', 'Dy', 'LinkBits', 'TurnWeights']:
    match = re.search(r'(?:int\[\]|int\[,\]) ' + table + r' =\s*\{.*?\};', source, re.S)
    original = match.group()
    for i, number in enumerate(re.finditer(r'-?\d+', original)):
        wrong = original[:number.start()] + str(int(number.group()) + 1) + original[number.end():]
        add(W, f'wander {table} entry {i}', original, wrong)

S = 'VisitorWalking.cs'
for name, old, new in [
    ('walk wrong step shift', 'StepShift = 14', 'StepShift = 13'),
    ('walk empty chain stays three', 'guest.SetState(VisitorState.WalkToDestination);', ';'),
    ('walk ignores facing', 'guest.Facing = VisitorActivity.Facing(dx, dy);', ';'),
    ('walk arithmetic shift', '(int)(unchecked((uint)(guest.WalkSpeed * world.Timescale)) >> StepShift)', '(guest.WalkSpeed * world.Timescale) >> StepShift'),
    ('walk wide multiply', '(uint)(guest.WalkSpeed * world.Timescale)', '(ulong)((long)guest.WalkSpeed * world.Timescale)'),
    ('walk spawn speed', 'guest.WalkSpeed * world.Timescale', 'guest.NormalWalkSpeed * world.Timescale'),
    ('walk omits timescale', 'guest.WalkSpeed * world.Timescale', 'guest.WalkSpeed * 16384'),
    ('walk no x clamp', 'Math.Clamp(dx, -step, step)', 'dx'),
    ('walk no y clamp', 'Math.Clamp(dy, -step, step)', 'dy'),
    ('walk omits position write', 'world.SetPosition(guest, x, y);', ';'),
    ('walk one coordinate arrives', 'x == target.X && y == target.Y', 'x == target.X || y == target.Y'),
    ('walk omits reached state two', 'if (x == target.X && y == target.Y) guest.SetState(VisitorState.WalkToDestination);', ';'),
    ('walk no lower x bound', 'x >= 0 &&', 'true &&'),
    ('walk no lower y bound', 'y >= 0 &&', 'true &&'),
    ('walk upper x equality', 'x < world.MapWidth', 'x <= world.MapWidth'),
    ('walk upper y equality', 'y < world.MapHeight', 'y <= world.MapHeight'),
    ('walk off-map skips cleanup', 'FreeWaypoints(guest, world);', ';'),
    ('walk off-map skips idle', 'guest.SetState(VisitorState.Idle);', ';'),
    ('walk off-map commits position', 'return; // READ: an invalid', 'world.SetPosition(guest, x, y); return; // READ: an invalid'),
    ('advance skips free', 'world.Waypoints.Free(head);', ';'),
    ('advance skips successor', 'world.SetWaypointHead(guest, next);', ';'),
    ('advance drops successor', 'int next = world.Waypoints.Next(head);', 'int next = -1;'),
    ('advance skips animation', 'guest.Animation = VisitorQueue.AnimationWander;', ';'),
    ('advance skips state three', 'guest.SetState(VisitorState.WalkToWaypoint);', ';'),
    ('free head only', 'world.Waypoints.FreeChain(world.WaypointHead(guest));', 'if (world.WaypointHead(guest) >= 0) world.Waypoints.Free(world.WaypointHead(guest));'),
    ('free leaves dangling head', 'world.SetWaypointHead(guest, WaypointPool.NoChain);', ';'),
]: add(S, name, old, new)

M = 'VisitorMessages.cs'
for name, old, new in [
    ('message queue failure no state gate', 'if (guest.State != VisitorState.WalkToBin) return false;', ''),
    ('message queue failure ignores purpose', 'if (guest.Purpose == Purpose.QueueWalk)', 'if (true)'),
    ('message queue failure never runs', 'if (guest.Purpose == Purpose.QueueWalk)', 'if (false)'),
    ('message queue failure ignores membership', 'if (guest.InQueue)', 'if (true)'),
    ('message queue failure skips removal', 'VisitorQueue.LeaveQueue(guest, world);', ';'),
    ('message queue failure keeps target', 'guest.HasTarget = false;', ';'),
    ('message queue failure skips idle', 'guest.SetState(VisitorState.Idle);', ';'),
    ('message ready misroute', 'world, EntranceMessage.PathReady, rng', 'world, EntranceMessage.PathFailed, rng'),
    ('message failed misroute', 'world, EntranceMessage.PathFailed, rng', 'world, EntranceMessage.PathReady, rng'),
    ('message thrown out misroute', 'world, EntranceMessage.ThrownOut, rng', 'world, EntranceMessage.Admit, rng'),
    ('message admit misroute', 'world, EntranceMessage.Admit, rng', 'world, EntranceMessage.ThrownOut, rng'),
    ('message shuffle missing stagger', 'QueueMessage.Shuffle, param1, param2', 'QueueMessage.Shuffle, 0, param2'),
    ('message shuffle missing state', 'QueueMessage.Shuffle, param1, param2', 'QueueMessage.Shuffle, param1, 0'),
    ('message removal misroute', 'world, QueueMessage.RemovedFromQueue', 'world, QueueMessage.Ejected'),
    ('message ejection misroute', 'world, QueueMessage.Ejected', 'world, QueueMessage.RemovedFromQueue'),
]: add(M, name, old, new)
for label, value in [('PathReady',1),('PathFailed',2),('ThrownOut',4),('Shuffle',6),('RemovedFromQueue',7),('Admit',9),('Ejected',10)]:
    add(M, 'message raw id ' + label, f'{label} = {value}', f'{label} = {value+100}')

# Exercise the delegated message rows too; the central entry point must preserve their distinct effects.
for file, marker in [('VisitorQueue.cs','        public static bool OnMessage'),
                     ('VisitorEntrance.cs','        public static bool OnMessage')]:
    original = (ROOT / 'core/TPW.Sim' / file).read_text()
    start = original.index(marker)
    end = original.index('\n    }\n', start)
    block = original[start:end]
    # Each effect/gate mutation stays inside the message implementation, not other state handlers.
    patterns = ['world.FreeWaypoints(guest);','guest.InQueue = false;','guest.HasTarget = false;',
                'guest.ExitPathRetried = false;','guest.WaitUntil = world.NowTick;',
                'guest.ExitPathRetried = true;','guest.Animation = VisitorQueue.AnimationWander;',
                'world.RemoveFromPark(guest);']
    for pattern in patterns:
        for occurrence in range(block.count(pattern)):
            matches = list(re.finditer(re.escape(pattern), block)); m = matches[occurrence]
            wrong = block[:m.start()] + ';' + block[m.end():]
            add(file, f'delegated {file} omit {pattern} #{occurrence}', block, wrong)


# Refinements added by the final trace audit. Their fixtures reject tile-vs-subtile confusion
# and make the new raw state number and the two spatial fallback gates observable.
add('Visitor.cs', 'refinement raw arrival state two', 'WalkToDestination = 2', 'WalkToDestination = 4')
add(A, 'refinement watch raw x', '(target.X >> 8) - (position.X >> 8)', 'target.X - position.X')
add(A, 'refinement watch raw y', '(target.Y >> 8) - (position.Y >> 8)', 'target.Y - position.Y')
add(N, 'refinement watch before penalties', 'var flags = world.InfluenceAt(guest);',
    'var flags = world.InfluenceAt(guest); if ((flags & TileInfluence.Entertainer) != 0) TryWatch(guest, world);')
add(W, 'refinement no candidate gate', 'if (candidates.Count == 0) break;', '')
add(W, 'refinement skips ring result', 'if (world.TryFindPathInRing(guest, radius, out var tile))',
    'if (!world.TryFindPathInRing(guest, radius, out var tile))')

add(S, 'refinement normalised diagonal',
    'int x = unchecked((short)(position.X + Math.Clamp(dx, -step, step)));',
    'if (dx != 0 && dy != 0) step = (int)(step / Math.Sqrt(2)); int x = unchecked((short)(position.X + Math.Clamp(dx, -step, step)));')

add(A, 'refinement vomit entry animation value', 'VomitAnimation = 12', 'VomitAnimation = 13')
add('VisitorIdle.cs', 'refinement vomit entry animation write', 'guest.Animation = VisitorActivity.VomitAnimation;', ';')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--only', action='append', default=[])
    args = parser.parse_args()
    selected = [m for m in MUTATIONS if not args.only or any(term in m[1] for term in args.only)]
    if not selected: raise SystemExit('no matching mutations')
    files = {name: ROOT / 'core/TPW.Sim' / name for name, *_ in selected}
    originals = {name: path.read_text() for name, path in files.items()}
    logs = Path(tempfile.mkdtemp(prefix='visitor-rest-mutations-'))
    command = ['dotnet', 'test', 'tests/TPW.Sim.Tests/', '--no-restore', '--nologo',
               '--filter', 'FullyQualifiedName~Visitor']
    outcomes = []
    print(f'{len(selected)} mutations; logs: {logs}', flush=True)
    try:
        baseline = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=120)
        (logs / 'baseline.log').write_text(baseline.stdout + baseline.stderr)
        if baseline.returncode: raise RuntimeError('baseline is not green')
        for index, (name, label, old, new, occurrence) in enumerate(selected, 1):
            original = originals[name]
            matches = list(re.finditer(re.escape(old), original))
            if occurrence >= len(matches): raise RuntimeError(f'missing mutation anchor: {label}')
            match = matches[occurrence]
            try:
                files[name].write_text(original[:match.start()] + new + original[match.end():])
                result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=120)
                output = result.stdout + result.stderr
                status = ('KILLED' if result.returncode and re.search(r'Failed:\s+[1-9]', output)
                          else 'SURVIVED' if result.returncode == 0 else 'INVALID')
                (logs / f'{index:03}.log').write_text(label + '\n' + output)
                failing = re.search(r'^\s+Failed (.+?) \[', output, re.MULTILINE)
                outcomes.append(dict(index=index, file=name, rule=label, status=status,
                                     example_failing_test=failing.group(1) if failing else None))
                print(f'{index}/{len(selected)} {status}: {label}', flush=True)
            finally:
                files[name].write_text(original)
    finally:
        for name, path in files.items(): path.write_text(originals[name])
        (logs / 'results.json').write_text(json.dumps(outcomes, indent=2) + '\n')
    restored = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=120)
    (logs / 'restored.log').write_text(restored.stdout + restored.stderr)
    bad = [m for m in outcomes if m['status'] != 'KILLED']
    print(f'{len(outcomes)-len(bad)}/{len(outcomes)} killed; restored exit {restored.returncode}', flush=True)
    if bad or restored.returncode: raise SystemExit(1)

if __name__ == '__main__': main()
