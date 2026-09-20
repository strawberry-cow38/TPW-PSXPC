#!/usr/bin/env python3
"""Mutate one ride controller rule at a time, require a real xunit failure, restore.

Run from any directory: python3 tools/mutate_ride_classes.py
Only the four new C# files in this worktree are changed. Compilation failures,
timeouts and missing tests are errors, never mutation kills. TRX evidence is
written to a temporary directory printed at startup. No disc bytes are needed.
"""
import argparse
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path


MUTATIONS = []


def add(file, name, before, after):
    MUTATIONS.append((file, name, before, after))


T = 'TourRide.cs'
add(T, 'tour seats from ride maximum', 'TransportSeats = 3', 'TransportSeats = 8')
add(T, 'tour ten tick cadence', 'GuestCadence = 20', 'GuestCadence = 10')
add(T, 'tour board off cadence', 'if (world.NowTick % GuestCadence == 0)\n            {', 'if (world.NowTick % GuestCadence != 0)\n            {')
add(T, 'tour board shuffling head', 'head.State == VisitorState.WaitingInQueue', 'head.State == VisitorState.ShuffleForward')
add(T, 'tour board when full', 'world.DockedPassengers < TransportSeats', 'world.DockedPassengers <= TransportSeats')
add(T, 'tour omit boarding', 'world.BoardHeadGuest();', ';')
add(T, 'tour fall through boarding', '                return;\n            }', '            }')
add(T, 'tour discard queue gate', 'if (world.QueueHead != null) return;', 'if (false) return;')
add(T, 'tour silently adopt disputed empty restriction', 'if (world.QueueHead != null) return;', 'if (world.DockedPassengers != 0 || world.QueueHead != null) return;')
add(T, 'tour retire only above two', 'world.TransportCount >= 2', 'world.TransportCount > 2')
add(T, 'tour omit retirement request', 'world.RequestRetirement();', ';')
add(T, 'tour omit empty departure', 'else world.Depart();', 'else { }')
add(T, 'tour omit unloading', 'if (world.NowTick % GuestCadence == 0) world.UnloadLastGuest();', ';')
add(T, 'tour unload every tick', 'if (world.NowTick % GuestCadence == 0) world.UnloadLastGuest();', 'world.UnloadLastGuest();')
add(T, 'tour preserve status three', '(int)status >= 4', '(int)status >= 3')
add(T, 'tour preserve status ten', '(int)status < 10', '(int)status <= 10')
add(T, 'tour no loading state from dock', 'docked == TourTransportState.Loading', 'false')
add(T, 'tour no unloading state from dock', 'docked == TourTransportState.Unloading', 'false')
add(T, 'tour absent dock closes', 'return AttractionStatus.Running;', 'return AttractionStatus.ClosedByPlayer;')
add(T, 'tour busy lap sentinel', 'BusyDockLaps = 200', 'BusyDockLaps = 199')
add(T, 'tour no lap reset on departure', 'if (state == TourTransportState.Departing) Laps = 0;', ';')
add(T, 'tour omit state entry hooks', 'world.EnterState(state);', ';')
add(T, 'tour full requires overshoot', 'world.Passengers >= TourRide.TransportSeats', 'world.Passengers > TourRide.TransportSeats')
add(T, 'tour retirement also requires full', '|| world.RetirementRequested)', '&& world.RetirementRequested)')
add(T, 'tour ignore departure blocker', '&& !world.DepartureBlocked)', ')')
add(T, 'tour departing ignores arrival', 'if (world.MoveToDestination())\n                        Enter(world.RetirementRequested', 'if (true)\n                        Enter(world.RetirementRequested')
add(T, 'tour no retirement route', 'world.RetirementRequested ? TourTransportState.Retiring', 'false ? TourTransportState.Retiring')
add(T, 'tour count moving ticks', 'if (!world.MoveToDestination()) break;', 'world.MoveToDestination();')
add(T, 'tour lap preincrement removed', 'Laps = unchecked((byte)(Laps + 1));', 'Laps = Laps;')
add(T, 'tour signed lap comparison', 'if (Laps >= world.Duration)', 'if (unchecked((sbyte)Laps) >= world.Duration)')
add(T, 'tour lap equality missed', 'if (Laps >= world.Duration)', 'if (Laps > world.Duration)')
add(T, 'tour fixed five laps', 'if (Laps >= world.Duration)', 'if (Laps >= 5)')
add(T, 'tour ignore occupied dock', 'if (!world.DockOccupied)', 'if (true)')
add(T, 'tour remove busy sentinel assignment', 'Laps = BusyDockLaps;', ';')
add(T, 'tour no repeat touring entry', 'Enter(TourTransportState.Touring, world);', ';')
add(T, 'tour skip return movement', 'if (world.MoveToDestination()) Enter(State + 1, world);', 'Enter(State + 1, world);')
add(T, 'tour skip return stage', 'Enter(State + 1, world)', 'Enter(State + 2, world)')
add(T, 'tour ignore settling', 'if (world.SettleAtDock())', 'if (true)')
add(T, 'tour reload with passengers', 'world.Passengers == 0 &&', 'true &&')
add(T, 'tour hold closed vehicle', '(int)world.RideStatus < 4', '(int)world.RideStatus < 3')
add(T, 'tour hold loading vehicle', '(int)world.RideStatus >= 10', '(int)world.RideStatus > 10')
add(T, 'tour retire before arrival', 'if (world.MoveToDestination()) world.RemoveTransport();', 'world.RemoveTransport();')
add(T, 'tour omit removal', 'if (world.MoveToDestination()) world.RemoveTransport();', 'world.MoveToDestination();')

P = 'PathedRide.cs'
add(P, 'track load ten ticks', 'LoadCadence = 20', 'LoadCadence = 10')
add(P, 'track unload twenty ticks', 'UnloadCadence = 10', 'UnloadCadence = 20')
add(P, 'track run duration undoubled', 'RunTicksPerDuration = 2', 'RunTicksPerDuration = 1')
add(P, 'track dispatch off cadence', 'if (world.NowTick % LoadCadence != 0) return AttractionStatus.Loading;', ';')
add(P, 'track equality capacity waits', 'world.Riders < world.Capacity', 'world.Riders <= world.Capacity')
add(P, 'track boarding ignores head state', 'head.State == VisitorState.WaitingInQueue', 'true')
add(P, 'track missing head guard', 'head != null &&', '')
add(P, 'track omit board', 'world.BoardHeadGuest();', ';')
add(P, 'track no run reset', 'RunTicks = 0;', ';')
add(P, 'track incomplete route runs', 'if (!world.TrackConnected) return AttractionStatus.ClosedByPlayer;', ';')
add(P, 'track incomplete route counts', 'if (!world.TrackConnected) return AttractionStatus.ClosedByPlayer;', 'if (!world.TrackConnected) { RunTicks++; return AttractionStatus.ClosedByPlayer; }')
add(P, 'track run does not count', 'RunTicks = unchecked((short)(RunTicks + 1));', ';')
add(P, 'track wide run comparison', 'RunTicks >= unchecked(world.Duration * RunTicksPerDuration)', '(ushort)RunTicks >= unchecked(world.Duration * RunTicksPerDuration)')
add(P, 'track misses run equality', 'RunTicks >= unchecked', 'RunTicks > unchecked')
add(P, 'track unload ignores cadence', 'if (world.NowTick % UnloadCadence != 0) return status;', ';')
add(P, 'track empty status never reloads', 'status == AttractionStatus.Unloading ? AttractionStatus.Loading : status', 'status')
add(P, 'track broken empty ride reopens', 'status == AttractionStatus.Unloading ? AttractionStatus.Loading : status', 'AttractionStatus.Loading')
add(P, 'track ignores finished flag', 'world.VehicleReadyToUnload(i) &&', 'true &&')
add(P, 'track ignores preview veto', '&& !world.PreviewActive', '')
add(P, 'track fixes compaction skip', 'world.UnloadVehicle(i);', '{ world.UnloadVehicle(i); i--; }')
add(P, 'track omit unload', 'world.UnloadVehicle(i);', ';')
add(P, 'track lap does not count', 'Laps = unchecked((sbyte)(Laps + 1));', ';')
add(P, 'track unsigned lap byte', 'if (Laps >= duration)', 'if ((byte)Laps >= duration)')
add(P, 'track misses lap equality', 'if (Laps >= duration)', 'if (Laps > duration)')
add(P, 'track lap equality only', 'if (Laps >= duration)', 'if (Laps == duration)')
add(P, 'track clears latched finish', 'if (Laps >= duration) ReadyToUnload = true;', 'ReadyToUnload = Laps >= duration;')

C = 'RollerCoaster.cs'
add(C, 'coaster load ten ticks', 'LoadCadence = 20', 'LoadCadence = 10')
add(C, 'coaster buffer limit 17', 'MaxPendingPassengers = 16', 'MaxPendingPassengers = 17')
add(C, 'coaster dispatch every 240', 'PartialDispatchInterval = 241', 'PartialDispatchInterval = 240')
add(C, 'coaster no connection veto', '&& trackConnected;', ';')
add(C, 'coaster ignores status predicate', 'AttractionLifecycle.OpenToGuests(status) && trackConnected', 'trackConnected')
add(C, 'coaster timer not incremented', 'DispatchTicks = unchecked(DispatchTicks + 1);', ';')
add(C, 'coaster timer misses equality', 'DispatchTicks < PartialDispatchInterval', 'DispatchTicks <= PartialDispatchInterval')
add(C, 'coaster no periodic dispatch', '            TryDispatch(world);\n            DispatchTicks = 0;', '            DispatchTicks = 0;')
add(C, 'coaster timer not reset', 'DispatchTicks = 0;', ';')
add(C, 'coaster dispatch empty batch', 'world.PendingPassengers != 0 &&', 'true &&')
add(C, 'coaster ignores train pool', '&& world.FreeTrainAvailable', '')
add(C, 'coaster omits dispatch transfer', 'world.DispatchPendingPassengers();', ';')
add(C, 'coaster skips animation advance', 'world.AdvanceStationAnimation();', ';')
add(C, 'coaster uses animation as run timer', 'world.AdvanceStationAnimation();', 'if (!world.AdvanceStationAnimation()) return;')
add(C, 'coaster boards disconnected track', 'world.TrackConnected &&', 'true &&')
add(C, 'coaster ignores piece checks', '&& world.PieceChecksPass', '')
add(C, 'coaster boards during preview', '&& !world.PreviewTrainActive', '')
add(C, 'coaster ignores load cadence', '&& world.NowTick % LoadCadence == 0', '')
add(C, 'coaster boards at capacity', 'world.Riders < world.Capacity', 'world.Riders <= world.Capacity')
add(C, 'coaster takes shuffling head', 'head.State == VisitorState.WaitingInQueue', 'true')
add(C, 'coaster missing head guard', 'head != null &&', '')
add(C, 'coaster batch uses max', 'System.Math.Min(world.BoardingBatchSize, world.Capacity)', 'System.Math.Max(world.BoardingBatchSize, world.Capacity)')
add(C, 'coaster omit boarding', 'world.BoardHeadGuest();', ';')
add(C, 'coaster misses batch equality', 'world.PendingPassengers >= batch', 'world.PendingPassengers > batch')
add(C, 'coaster ignores hard buffer cap', '|| world.PendingPassengers >= MaxPendingPassengers', '')
add(C, 'coaster load does not unload', '            UnloadTick(world);\n        }\n\n        /// <summary>READ: 0x800B0D68.', '        }\n\n        /// <summary>READ: 0x800B0D68.')
add(C, 'coaster running omits load', '            LoadTick(world);', ';')
add(C, 'coaster running omits second unload', '            LoadTick(world);\n            UnloadTick(world);', '            LoadTick(world);')
add(C, 'coaster unload before boarding', '            LoadTick(world);\n            UnloadTick(world);', '            UnloadTick(world);\n            LoadTick(world);')
add(C, 'coaster no saved next snapshot', 'new List<CoasterTrain>(world.ActiveTrains)', 'world.ActiveTrains')
add(C, 'coaster ignores train ready', 'train.ReadyToUnload &&', 'true &&')
add(C, 'coaster unloads preview train', '&& !train.IsPreview', '')
add(C, 'coaster omit train unload', 'world.UnloadTrain(train);', ';')
add(C, 'coaster midpoint late', 'ReturnMidpoint = 0x800', 'ReturnMidpoint = 0x801')
add(C, 'coaster preview counts laps', 'IsPreview || StillOnLaunchSegment', 'StillOnLaunchSegment')
add(C, 'coaster launch counts laps', 'IsPreview || StillOnLaunchSegment', 'IsPreview')
add(C, 'coaster counts away from return', '|| !onReturnSegment', '')
add(C, 'coaster midpoint equality finishes', 'segmentFraction <= ReturnMidpoint', 'segmentFraction < ReturnMidpoint')
add(C, 'coaster lap not incremented', 'Laps = unchecked((short)(Laps + 1));', ';')
add(C, 'coaster unsigned lap comparison', 'if (Laps >= duration)', 'if ((ushort)Laps >= duration)')
add(C, 'coaster hardcoded one lap', 'if (Laps >= duration)', 'if (Laps >= 1)')
add(C, 'coaster misses lap equality', 'if (Laps >= duration)', 'if (Laps > duration)')
add(C, 'coaster equality only laps', 'if (Laps >= duration)', 'if (Laps == duration)')
add(C, 'coaster clears latched finish', 'if (Laps >= duration) ReadyToUnload = true;', 'ReadyToUnload = Laps >= duration;')
add(C, 'coaster add lap debounce', 'Laps = unchecked((short)(Laps + 1));', 'if (Laps != 0) return; Laps = unchecked((short)(Laps + 1));')

W = 'OtherRideWear.cs'
add(W, 'wear tour outer mask alone', 'TourTickMask = 0x1F', 'TourTickMask = 0x1E')
add(W, 'wear shared every eight', 'SharedTickMask = 3', 'SharedTickMask = 7')
add(W, 'wear reliability threshold unscaled', 'BreakReliabilityRaw = 10 << 12', 'BreakReliabilityRaw = 10')
add(W, 'wear piece term divisor', 'TrackPiecesPerWearUnit = 30', 'TrackPiecesPerWearUnit = 29')
add(W, 'wear omit load term', 'unchecked(speedTermRaw + loadTermRaw)', 'speedTermRaw')
add(W, 'wear omit speed term', 'unchecked(speedTermRaw + loadTermRaw)', 'loadTermRaw')
add(W, 'wear coaster gets track third term', 'if (type == AttractionType.TrackRide)', 'if (type != AttractionType.TourRide)')
add(W, 'wear track lacks third term', 'sum = unchecked(sum + ((trackPieces << 12) / TrackPiecesPerWearUnit));', ';')
add(W, 'wear piece count wrong scale', 'trackPieces << 12', 'trackPieces << 11')
add(W, 'wear track averages two', 'average = sum / 3;', 'average = sum / 2;')
add(W, 'wear others average three', 'else average = sum / 2;', 'else average = sum / 3;')
add(W, 'wear signed average rounds down', 'else average = sum / 2;', 'else average = sum >> 1;')
add(W, 'wear omit record multiplier', 'unchecked(average * wearMultiplier)', 'average')
add(W, 'wear fixed-point scales multiplier', 'unchecked(average * wearMultiplier)', 'unchecked(average * wearMultiplier) >> 12')
add(W, 'wear adopt disputed outside-running calls', 'if (status != AttractionStatus.Running) return;', ';')
add(W, 'wear tour uses shared cadence', 'type == AttractionType.TourRide ? TourTickMask : SharedTickMask', 'SharedTickMask')
add(W, 'wear ignore cadence', 'if ((world.NowTick & mask) == 0)', 'if (true)')
add(W, 'wear omit step', 'world.ApplySharedWearStep();', ';')
add(W, 'breakdown ignores status', 'status != AttractionStatus.Running ||', '')
add(W, 'breakdown at exactly ten', 'world.ReliabilityRaw >= BreakReliabilityRaw', 'world.ReliabilityRaw > BreakReliabilityRaw')
add(W, 'breakdown no smoke', 'world.EnsureSmoke();', ';')
add(W, 'breakdown adopt binary track status four', ': AttractionStatus.BrokenDown;', ': AttractionStatus.AboutToBreakDown;')
add(W, 'breakdown tour directly five', '? AttractionStatus.AboutToBreakDown', '? AttractionStatus.BrokenDown')

# Keep appended indices stable so a failed run can be resumed with --only.
for state, number in [('Loading', 0), ('Departing', 1), ('Touring', 2), ('Returning', 3),
                      ('Approaching', 4), ('Docking', 5), ('Settling', 6), ('Unloading', 7), ('Retiring', 8)]:
    add(T, f'tour renumber {state}', f'{state} = {number}', f'{state} = {number + 20}')
add(T, 'tour rejects byte wrap', 'unchecked((byte)(Laps + 1))', 'checked((byte)(Laps + 1))')
add(P, 'track rejects run halfword wrap', 'unchecked((short)(RunTicks + 1))', 'checked((short)(RunTicks + 1))')
add(P, 'track rejects lap byte wrap', 'unchecked((sbyte)(Laps + 1))', 'checked((sbyte)(Laps + 1))')
add(C, 'coaster rejects lap halfword wrap', 'unchecked((short)(Laps + 1))', 'checked((short)(Laps + 1))')
add(W, 'wear rejects wrapped sum', 'unchecked(speedTermRaw + loadTermRaw)', 'checked(speedTermRaw + loadTermRaw)')
add(W, 'wear rejects low32 multiplication', 'unchecked(average * wearMultiplier)', 'checked(average * wearMultiplier)')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--only', type=int, nargs='+', help='rerun these one-based mutation indices')
    args = parser.parse_args()
    selected = [(i, m) for i, m in enumerate(MUTATIONS, 1) if not args.only or i in args.only]
    if args.only and set(args.only) - set(range(1, len(MUTATIONS) + 1)):
        parser.error('mutation index out of range')
    root = Path(__file__).resolve().parent.parent
    directory = root / 'core/TPW.Sim'
    originals = {name: (directory / name).read_text() for name in (T, P, C, W)}
    results = Path(tempfile.mkdtemp(prefix='tpw-ride-mutations-'))
    print(f'TRX evidence: {results}', flush=True)
    selection = '|'.join('FullyQualifiedName~' + name for name in
                         ('TourRideTests', 'PathedRideTests', 'RollerCoasterTests', 'OtherRideWearTests'))

    def check(index):
        trx = f'{index}.trx'
        p = subprocess.run(['dotnet', 'test', 'tests/TPW.Sim.Tests/', '--nologo', '--verbosity', 'quiet',
                            '--filter', selection, '--logger', f'trx;LogFileName={trx}',
                            '--results-directory', str(results)], cwd=root,
                           capture_output=True, text=True, timeout=120)
        (results / f'{index}.log').write_text(p.stdout + p.stderr)
        path = results / trx
        if not path.exists() or re.search(r'error [A-Z]+\d+', p.stdout + p.stderr):
            raise RuntimeError(f'{index}: did not run tests; see {results / (str(index) + ".log")}')
        counters = ET.parse(path).find('.//{*}Counters')
        if counters is None or int(counters.attrib['executed']) == 0:
            raise RuntimeError(f'{index}: no executed tests')
        failed = int(counters.attrib['failed'])
        if p.returncode != 0 and failed == 0:
            raise RuntimeError(f'{index}: failure without red tests')
        return failed

    if check('baseline'):
        raise RuntimeError('baseline not green')
    survivors = []
    try:
        for index, (file, name, before, after) in selected:
            original = originals[file]
            if original.count(before) != 1:
                raise RuntimeError(f'{name}: expected unique site, found {original.count(before)}')
            path = directory / file
            path.write_text(original.replace(before, after))
            try:
                failed = check(index)
                print(f'{index}/{len(MUTATIONS)} {"KILLED" if failed else "SURVIVED"}: {name} ({failed} red tests)', flush=True)
                if not failed:
                    survivors.append(name)
            finally:
                path.write_text(original)
    finally:
        for file, original in originals.items():
            (directory / file).write_text(original)
    if check('restored'):
        raise RuntimeError('restored source is not green')
    print(f'{len(selected) - len(survivors)}/{len(selected)} killed; survivors: {survivors}; restored green')
    if survivors:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
