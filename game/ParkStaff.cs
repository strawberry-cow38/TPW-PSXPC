using System;
using Godot;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>A member of staff standing in the park: the sim's <see cref="StaffMember"/> on the same
    /// body a guest uses (<see cref="Walker"/>).
    ///
    /// ⭐ THE BODY IS SHARED AND THE MACHINE IS NOT. In the original, staff and guests are both Person:
    /// the same pathfinder, the same waypoint chain, the same eight drawn facings, the same P+0x60 step.
    /// What differs is the state machine hanging off it and, per class, the arrival table - behaviour.md
    /// §3.1 is explicit that staff purpose 1 and guest purpose 1 mean different things even though they
    /// are the same field.</summary>
    sealed class Staffer : Walker
    {
        public StaffMember S;

        /// <summary>READ (findings/staff.md §2), and it is NOT a field. There is no staff P+0x60: a
        /// uniformed staff node is 0x4C bytes and P+0x60/P+0x62 belong to the larger Visitor object.
        /// The walker asks virtual slot 44 instead, and for a mechanic that is a per-SKILL row whose
        /// other column is its repair time — (240,9), (180,12), (120,14), (60,16), (60,18).
        ///
        /// ⭐ SO SPEED IS A CONSEQUENCE OF TRAINING, READ FRESH EVERY CALL. A new mechanic walks at 9,
        /// barely over half the 15 this port had borrowed from the visitor while it was unread, and it
        /// gets faster the moment it is trained rather than at its next hire.</summary>
        public override int WalkSpeed => StaffMotion.Speed(S);

        /// <summary>The park's total day when this one was hired. Wages are pro-rated over the days of
        /// the month actually worked (Wages.Monthly), so a member taken on mid-month costs part of a
        /// wage — and the game floors TWICE on the way there, which is why the day matters rather than
        /// a fraction computed at the end.</summary>
        public int HiredDay;
    }

    /// <summary>The park as the shared staff machine reads it (TPW.Sim.StaffBase, behaviour.md §3.1).
    ///
    /// ⚠ EVERY ANSWER HERE IS "NO", AND EACH ONE IS A MISSING FEATURE RATHER THAN A CHOICE. Patrol
    /// rectangles are assigned from a UI the port does not have; the strike system needs wages, which
    /// are not wired; benches and staff rooms are features the park does not yet track. StaffBase is
    /// written so that every one of those falls through to random wander rather than standing still,
    /// which is why a mechanic with nothing to do still moves.</summary>
    sealed class ParkStaffWorld : IStaffWorld
    {
        readonly Func<long> _now;
        readonly Func<System.Collections.Generic.IReadOnlyList<GuestTarget>> _targets;
        readonly Func<Staffer, int, int, bool> _pathTo;

        public ParkStaffWorld(Func<long> now,
                              Func<System.Collections.Generic.IReadOnlyList<GuestTarget>> targets,
                              Func<Staffer, int, int, bool> pathToTile)
        { _now = now; _targets = targets; _pathTo = pathToTile; }

        /// <summary>The member the next world call is about. Set before each one, the same way
        /// GuestBrain is set per guest, because "nearest" is measured from where THIS one stands.</summary>
        public Staffer Current;

        public long NowTick => _now();

        /// <summary>⚠ NO STRIKES. The strike test averages morale against (100 - tiredness) and is driven
        /// by pay; McAi's wage machinery is not wired, so nothing can ever call one.</summary>
        public bool IsTypeOnStrike(StaffKind kind) => false;

        /// <summary>⚠ NO PATROL AREAS. S+0x38..0x3B is set by dragging a rectangle in the staff panel,
        /// which does not exist yet. StaffBase sends a member with no rectangle to random wander.</summary>
        public bool HasPatrolRect(StaffMember staff) => false;
        public bool TryPathIntoPatrolArea(StaffMember staff) => false;

        /// <summary>⚠ NO MUSTER BUILDING tracked, so a strike would start where they stand. Unreachable
        /// while <see cref="IsTypeOnStrike"/> is false.</summary>
        public bool StrikeMusterExists => false;
        public bool TryPathToStrikeMuster(StaffMember staff) => false;

        /// <summary>State 49's search (0x800947CC): the NEAREST placed feature whose record +0x2E bit 1
        /// is set (0x80024110 -> 0x8002433C) and whose status byte A+0x6E is non-zero (0x800660DC), then
        /// a path to it.
        ///
        /// ⭐ THE FLAG IS READ AND THE OBJECT NAMES ITSELF. behaviour.md §3.1 marks the target a GUESS
        /// ("a bench/staff room") because it read the flag and not what carries it. Reading the bit out
        /// of all 197 records settles it: bit 1 is on the STAFF ROOM in every one of the four worlds
        /// (entries 32, 109, 197, 353) and on nothing else that appears in more than one. So there is no
        /// bench - the staff room is the thing - and a park with none has staff who can never recover.
        ///
        /// ⚠ DISTANCE IS MEASURED IN TILES, NOT BY THE GAME'S METRIC. 0x800947CC compares a distance it
        /// computes through a vtable call this does not follow; Manhattan on tile centres is the port's
        /// choice, and it can pick a different staff room when two are close to equal.</summary>
        public bool TryPathToRest(StaffMember staff)
        {
            if (Current == null || _targets == null) return false;
            GuestTarget best = null;
            int bestD = int.MaxValue;
            foreach (var t in _targets())
            {
                if (!t.StaffMayRest || !t.Built) continue;
                int d = Math.Abs(t.CentreX - (Current.X >> 8)) + Math.Abs(t.CentreZ - (Current.Z >> 8));
                if (d >= bestD) continue;
                bestD = d; best = t;
            }
            return best != null && _pathTo(Current, best.DoorX, best.DoorZ);
        }
    }

    /// <summary>One placed ride as a mechanic acts on it: a live handle, not a snapshot, because the
    /// claim, the status and the closing progress are all written back through it.</summary>
    interface IRideJob
    {
        int Id { get; }
        AttractionStatus Status { get; set; }
        /// <summary>Remaining lifetime. ZERO MEANS CONDEMNED, and a condemned ride is never claimed for
        /// repair — the test is `!= 0`, not `> 0` (Mechanic's own note).</summary>
        int Lifetime { get; }
        /// <summary>The footprint's centre tile, for "nearest".</summary>
        int CentreX { get; }
        int CentreZ { get; }
        /// <summary>Where a mechanic walks. ⚠ THE PORT'S CHOICE: the binary asks the ride for its slot
        /// 42 position and its slot 26 leave point; both are vtable slots not yet followed. A ride's
        /// entrance tile is a real tile of the right building and is reachable with flags 0x11, which
        /// the slot-42 position may not be.</summary>
        int DoorX { get; }
        int DoorZ { get; }
        /// <summary>Footprint width + height in tiles, which is what the closing threshold scales.</summary>
        int FootprintSpan { get; }
        /// <summary>A+0xEC, the closing-progress word. NOT a rider count (RideClosing).</summary>
        int Closing { get; set; }
        /// <summary>A+0x54: the mechanic that holds this ride, or null.</summary>
        StaffMember Claim { get; set; }
        void CompleteUpgrade();
    }

    /// <summary>The park as the mechanic reads it (TPW.Sim.IMechanicWorld).
    ///
    /// ⭐ THIS IS WHAT UNSTICKS A WORN-OUT PARK. Wear sends a ride to status 4 and nothing in the ride's
    /// own machine ever brings it back: 4 leaves only for 5 at reliability EXACTLY zero, which the wear
    /// arithmetic cannot reach because a ride that is not running does not wear. Without a mechanic
    /// every ride in the park freezes permanently the first time it crosses the threshold.</summary>
    sealed class ParkMechanicWorld : IMechanicWorld
    {
        readonly ParkStaffWorld _base;
        readonly Func<System.Collections.Generic.IReadOnlyList<IRideJob>> _rides;
        readonly Func<Staffer, int, int, bool> _pathTo;
        readonly System.Collections.Generic.Dictionary<StaffMember, IRideJob> _target = new();
        readonly System.Collections.Generic.List<Staffer> _order;

        public ParkMechanicWorld(ParkStaffWorld shared,
                                 Func<System.Collections.Generic.IReadOnlyList<IRideJob>> rides,
                                 Func<Staffer, int, int, bool> pathToTile,
                                 System.Collections.Generic.List<Staffer> order)
        { _base = shared; _rides = rides; _pathTo = pathToTile; _order = order; }

        public Staffer Current { get => _base.Current; set => _base.Current = value; }
        public bool Log;

        public long NowTick => _base.NowTick;
        public bool IsTypeOnStrike(StaffKind kind) => _base.IsTypeOnStrike(kind);
        public bool HasPatrolRect(StaffMember s) => _base.HasPatrolRect(s);
        public bool TryPathIntoPatrolArea(StaffMember s) => _base.TryPathIntoPatrolArea(s);
        public bool StrikeMusterExists => _base.StrikeMusterExists;
        public bool TryPathToStrikeMuster(StaffMember s) => _base.TryPathToStrikeMuster(s);
        public bool TryPathToRest(StaffMember s) => _base.TryPathToRest(s);

        /// <summary>The ride this mechanic is on, or null. The binary keeps it on the object; the port
        /// keeps it beside the StaffMember rather than widening the sim's type for a host's bookkeeping.</summary>
        public IRideJob TargetOf(StaffMember s) => s != null && _target.TryGetValue(s, out var r) ? r : null;

        int DistanceTo(IRideJob r) => Math.Abs(r.CentreX - (Current.X >> 8)) + Math.Abs(r.CentreZ - (Current.Z >> 8));

        IRideJob Nearest(Func<IRideJob, bool> ok)
        {
            IRideJob best = null;
            int bestD = int.MaxValue;
            foreach (var r in _rides())
            {
                if (!ok(r)) continue;
                int d = DistanceTo(r);
                if (d >= bestD) continue;
                bestD = d; best = r;
            }
            return best;
        }

        public bool TryClaimBrokenRide(StaffMember staff)
        {
            // ⚠ ONLY THE NEAREST IS EVER TRIED, and a condemned one refuses without falling through to
            // the next. That is the reason rides.md §6.3 says a condemned ride is never repaired, and
            // reproducing it means selecting first and testing after.
            var r = Nearest(x => (x.Status == AttractionStatus.AboutToBreakDown
                               || x.Status == AttractionStatus.BrokenDown)
                              && (x.Claim == null || ReferenceEquals(x.Claim, staff)));
            if (r == null || r.Lifetime == 0) return false;
            r.Claim = staff;
            _target[staff] = r;
            return true;
        }

        /// <summary>The rides a player has ASKED to have upgraded, and how to clear one once it is done.
        /// Set by the host (ParkView owns the fifteen slots).
        ///
        /// ⚠⚠ THESE TWO RETURNED FALSE, ALWAYS. The comment said it was empty because no panel could
        /// queue anything -- which was true, and which made the pair invisible: the mechanic asked "is
        /// there an upgrade waiting" every tick, was told no, and there was nothing to notice. Both ends
        /// of the feature were missing, and each one was a reason the other could not be tested.</summary>
        public Func<IRideJob, bool> QueuedForUpgrade;
        public Action<IRideJob> DequeueUpgrade;

        /// <summary>READ 0x8005BA8C/0x8005BE70: claim the nearest ride the player has asked to upgrade.
        /// The same selection shape as a repair -- nearest first, then test -- so a ride whose lifetime
        /// has run out is passed over rather than filtered before choosing.</summary>
        public bool TryClaimQueuedUpgrade(StaffMember staff)
        {
            if (QueuedForUpgrade == null) return false;
            var r = Nearest(x => QueuedForUpgrade(x) && (x.Claim == null || ReferenceEquals(x.Claim, staff)));
            if (r == null || r.Lifetime == 0) return false;
            r.Claim = staff;
            _target[staff] = r;
            return true;
        }

        /// <summary>⚠ NOT THE SAME CALL. The mechanic takes a queued upgrade as REPAIR work when it is
        /// already on its way somewhere; the original distinguishes them and the difference has not been
        /// traced, so this stays refused rather than aliased to the one above. A wrong yes here sends a
        /// mechanic to the wrong job; a no leaves the ordinary path working.</summary>
        public bool TryClaimQueuedUpgradeAsRepair(StaffMember staff) => false;

        public void ReleaseClaim(StaffMember staff)
        {
            if (TargetOf(staff) is { } r && ReferenceEquals(r.Claim, staff)) r.Claim = null;
        }

        public StaffMember NextMechanic(StaffMember staff)
        {
            int i = _order.FindIndex(s => ReferenceEquals(s.S, staff));
            for (int j = i + 1; i >= 0 && j < _order.Count; j++)
                if (_order[j].S.Kind == StaffKind.Mechanic) return _order[j].S;
            return null;
        }

        public bool TryClaimRideFor(StaffMember from, StaffMember candidate, bool forRepair)
        {
            if (TargetOf(from) is not { } r) return false;
            if (r.Claim != null && !ReferenceEquals(r.Claim, candidate)) return false;
            if (forRepair && (r.Lifetime == 0
                || (r.Status != AttractionStatus.AboutToBreakDown && r.Status != AttractionStatus.BrokenDown)))
                return false;
            r.Claim = candidate;
            _target[candidate] = r;
            return true;
        }

        public bool TryPathToClaimedRide(StaffMember staff)
        {
            if (TargetOf(staff) is not { } r || Current == null) return false;
            Current.WaypointHead = TPW.Sim.WaypointPool.NoChain;   // 0x80093C68 drops the waypoint first
            bool ok = _pathTo(Current, r.DoorX, r.DoorZ);
            if (Log) Godot.GD.Print($"[tpw] mechanic path from ({Current.X >> 8},{Current.Z >> 8}) "
                                  + $"to ride {r.Id} at ({r.DoorX},{r.DoorZ}): {(ok ? "accepted" : "refused")}");
            return ok;
        }

        public bool TryPathToLeavePoint(StaffMember staff)
        {
            if (TargetOf(staff) is not { } r || Current == null) return false;
            return _pathTo(Current, r.DoorX, r.DoorZ);
        }

        public int ClosingProgress(StaffMember staff) => TargetOf(staff)?.Closing ?? 0;
        public void SetClosingProgress(StaffMember staff, int value)
        { if (TargetOf(staff) is { } r) r.Closing = value; }
        public int FootprintSpan(StaffMember staff) => TargetOf(staff)?.FootprintSpan ?? 0;

        /// <summary>The closing step, 20.12, capped at 0x4000. The park's own frame time, the same one
        /// the ride cycle races.</summary>
        public int ClosingStep => Math.Min(0x4000, (int)(TPW.Data.EntranceFlags.TimeUnitsPerSecond / ParticleSystem.FramesPerSecond));

        public void MarkRideUnderRepair(StaffMember staff)
        { if (TargetOf(staff) is { } r) r.Status = AttractionStatus.UnderRepair; }

        public void MarkRideOpenAndRelease(StaffMember staff)
        {
            if (TargetOf(staff) is not { } r) return;
            r.Status = AttractionStatus.Reopen;
            r.Claim = null;
        }

        /// <summary>READ 0x8005BE70: the ride comes OFF the queue when the work is finished, not when it
        /// is claimed -- so a mechanic that dies or is re-tasked leaves the request standing.</summary>
        public void CompleteUpgrade(StaffMember staff)
        {
            if (TargetOf(staff) is not { } r) return;
            r.CompleteUpgrade();
            DequeueUpgrade?.Invoke(r);
        }
    }
}
