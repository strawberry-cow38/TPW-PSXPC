using System;
using System.Collections.Generic;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>The park's forty pieces of litter, and the handyman whose whole job is clearing them.
    ///
    /// ⚠⚠ THIS ENTIRE LOOP WAS PORTED AND NEVER CALLED — the fourth "ported but never called" found in
    /// one day, and the widest. <see cref="LitterPool"/>, <see cref="HandymanLitter"/> and every member
    /// of <see cref="Handyman"/> (Idle, Arrive, CleanLitter, EmptyBin, OnPathMessage, WantsEmptying,
    /// BinScore) compiled, passed their tests, and were named by NOTHING in the game project. Two
    /// independent sweeps agreed before a line of this was written: a grep here found zero `Handyman.`
    /// call sites, and cow tools' dead-port audit found zero references to the STAFF CLASSES TYPE at
    /// all. A cleaner hired into the park drew a wage and wandered at random, for ever.
    ///
    /// ⭐ IT IS A FEEDBACK LOOP, WHICH IS WHY THE GAP COST MORE THAN A MISSING SPRITE. A guest drops
    /// rubbish when it wants a bin and cannot reach one, and again when it is miserable; rubbish inside
    /// the strict radius costs happiness and adds nausea to EVERYONE standing near it; unhappier guests
    /// drop more. Nothing ages litter away — <see cref="LitterPool.Tick"/> is `jr ra` and is marked DO
    /// NOT FIX — so a park with no handyman only ever gets dirtier, and once all forty slots are taken
    /// it loses even the ability to express that it is getting worse.</summary>
    sealed class ParkLitterWorld : ILitterWorld
    {
        public readonly LitterPool Pool = new();

        /// <summary>⚠ FALSE, NOT TRACED. This is the SAME word as
        /// <see cref="ParkIdleWorld.IdleNeedsSuppressed"/> and <see cref="ParkNeedsWorld"/>'s — one
        /// getter at 0x80059A9C that gates the needs bubble, all littering and all ride wear. The port
        /// has never traced what sets it; answering true on a guess would silence three systems at
        /// once. See findings/litter.md.</summary>
        public bool LitterSuppressed => false;

        /// <summary>READ: 0x80099D58 takes and post-increments the shared word at 0x80103284. The port
        /// has no shared drawable-object list, so this sequence is the litter pool's own. ⚠ THAT IS A
        /// DIVERGENCE, not a match: in the original every object in the park draws from one counter, so
        /// a piece of litter's id is not comparable with the original's for the same park.</summary>
        public int TakeObjectId() => _nextId++;
        int _nextId = 1;

        /// <summary>⚠ NOTHING IS DRAWN YET. The original inserts the piece into the park's drawable
        /// list here and takes it out again in Unregister. The port has no litter sprite, so the pool is
        /// simulated and invisible: guests react to rubbish they walk past, handymen walk to it and
        /// clear it, and the player cannot see any of it. A GAP, deliberately left rather than faked,
        /// and the reason the counters below exist — they are currently the only way to observe it.</summary>
        public void RegisterLitter(Litter piece) { Dropped++; }
        public void UnregisterLitter(Litter piece) { }

        /// <summary>How many pieces have ever been dropped, and how many a handyman has cleared. Live
        /// count is <see cref="LitterPool.Count"/>; these two say whether the loop is turning.</summary>
        public int Dropped, Cleaned, BinsEmptied, BinsNeglected;
    }

    /// <summary>The park as a handyman reads it (TPW.Sim.IHandymanWorld + IHandymanLitterWorld).
    ///
    /// ⭐ THE TWO JOBS ARE NOT SYMMETRIC AND THE ASYMMETRY IS THE PORT. Handyman.Idle tosses a coin:
    /// heads it looks for a bin and FALLS BACK to litter, tails it looks only for litter. So a park
    /// with full bins and no litter is served half as often as one with litter and no bins. Likewise a
    /// bin trip Sets state 11 where a litter trip Pushes it. Both are reproduced in the sim; this
    /// adapter only has to not get in the way.
    ///
    /// ⚠ MORALE IS MOSTLY DOWNSIDE. Clearing rubbish is +1 and clearing vomit is −6; emptying a bin in
    /// good time is +5 and emptying one over 60% full is −10. A neglected park grinds its handymen down
    /// about twice as fast as a tidy one builds them up, which is how mess turns into a strike.</summary>
    sealed class ParkHandymanWorld : IHandymanWorld, IHandymanLitterWorld
    {
        readonly ParkStaffWorld _base;
        readonly ParkLitterWorld _litter;
        readonly Func<IReadOnlyList<GuestTarget>> _targets;
        readonly Func<int, FeatureStock> _stockOf;
        readonly Func<Staffer, int, int, bool> _pathTo88;
        readonly Func<int> _today;

        /// <summary>The feature this handyman is walking to, kept beside the StaffMember for the same
        /// reason the mechanic's ride is: the sim's type is not widened for a host's bookkeeping.</summary>
        readonly Dictionary<StaffMember, GuestTarget> _bin = new();

        public ParkHandymanWorld(ParkStaffWorld shared, ParkLitterWorld litter,
                                 Func<IReadOnlyList<GuestTarget>> targets,
                                 Func<int, FeatureStock> stockOf,
                                 Func<Staffer, int, int, bool> pathToWorldPoint,
                                 Func<int> today)
        {
            _base = shared; _litter = litter; _targets = targets;
            _stockOf = stockOf; _pathTo88 = pathToWorldPoint; _today = today;
        }

        public Staffer Current { get => _base.Current; set => _base.Current = value; }

        public long NowTick => _base.NowTick;
        public bool IsTypeOnStrike(StaffKind kind) => _base.IsTypeOnStrike(kind);
        public bool HasPatrolRect(StaffMember s) => _base.HasPatrolRect(s);
        public bool TryPathIntoPatrolArea(StaffMember s) => _base.TryPathIntoPatrolArea(s);
        public bool StrikeMusterExists => _base.StrikeMusterExists;
        public bool TryPathToStrikeMuster(StaffMember s) => _base.TryPathToStrikeMuster(s);
        public bool TryPathToRest(StaffMember s) => _base.TryPathToRest(s);

        // ---- ILitterWorld, because IHandymanLitterWorld extends it ---------------------------------
        // ⚠ ONE POOL, TWO DOORS INTO IT. The handyman's world inherits the allocator's interface, so
        // these must forward to the SAME ParkLitterWorld the guests drop through — a second
        // implementation here would give the staff their own object-id sequence and their own
        // suppression answer, and the two would drift the moment either changed.
        public bool LitterSuppressed => _litter.LitterSuppressed;
        public int TakeObjectId() => _litter.TakeObjectId();
        public void RegisterLitter(Litter piece) => _litter.RegisterLitter(piece);
        public void UnregisterLitter(Litter piece) => _litter.UnregisterLitter(piece);

        // ---- IHandymanLitterWorld: the concrete litter side --------------------------------------

        /// <summary>⚠ SIGNED 8.8 WORLD UNITS, NOT TILES. LitterPool.NearestUnclaimed and Nearby both
        /// shift their arguments down by 8 before comparing, so handing them tiles would put every
        /// piece of litter within one tile of the origin and a handyman would always claim the same
        /// one. The staff body already stores 8.8; pass it straight through.</summary>
        public (int X, int Y) Position(StaffMember staff)
            => Current != null && ReferenceEquals(Current.S, staff) ? (Current.X, Current.Z) : (0, 0);

        /// <summary>READ: request to the RAW litter position with flags (0x11, 0), 0x80098E94..EB4 —
        /// the piece's own scattered 8.8 coordinate, not the centre of its tile. The scatter is ±100 in
        /// 8.8, so a piece sits off-centre and the handyman walks to where it actually is.</summary>
        public bool TryPathToLitter(StaffMember staff, int x, int y, int flags, int argument)
            => Current != null && ReferenceEquals(Current.S, staff) && _pathTo88(Current, x, y);

        // ---- IHandymanWorld ------------------------------------------------------------------------

        public bool TryClaimNearestLitter(StaffMember staff)
            => HandymanLitter.TryClaimNearest(staff, _litter.Pool, this);

        public bool ClaimedLitterIsVomit(StaffMember staff) => staff.TargetLitter?.IsVomit ?? false;

        public void DeleteClaimedLitter(StaffMember staff)
        {
            HandymanLitter.DeleteClaimed(staff, _litter.Pool, _litter);
            _litter.Cleaned++;
        }

        public void UnclaimLitter(StaffMember staff) => HandymanLitter.Unclaim(staff);

        /// <summary>READ (0x80098F20): over every feature a guest may use whose capacity byte is
        /// strictly under 60, take the lowest <see cref="Handyman.BinScore"/> — so near and fairly full
        /// beats far and fuller — then ask for a path to it.
        ///
        /// ⭐ IT IS NOT ONLY BINS. The filter is record flag `rec+0x2E &amp; 1`, the SAME bit a guest's
        /// slot 54 reads, so a handyman services every guest-usable feature — toilets included. That is
        /// why <see cref="GuestTarget.Usable"/> is the test here and not a bin-specific flag.</summary>
        public bool TryChooseBin(StaffMember staff)
        {
            if (Current == null || !ReferenceEquals(Current.S, staff)) return false;
            int sx = Current.X >> 8, sz = Current.Z >> 8;
            GuestTarget best = null;
            FeatureStock bestStock = null;
            int bestScore = int.MaxValue;
            foreach (var t in _targets())
            {
                if (t.Usable == 0 || !t.Built) continue;
                var stock = _stockOf(t.Id);
                if (stock == null || !Handyman.WantsEmptying(stock.Level)) continue;
                int score = Handyman.BinScore(t.CentreX - sx, t.CentreZ - sz, stock.Level);
                if (score >= bestScore) continue;
                bestScore = score; best = t; bestStock = stock;
            }
            if (best == null) return false;
            _bin[staff] = best;
            if (_pathTo88(Current, ParkGuests.Centre(best.DoorX), ParkGuests.Centre(best.DoorZ))) return true;
            _bin.Remove(staff);
            return false;
        }

        /// <summary>The REMAINING capacity when the handyman got there, 0..100 — under 40 means the bin
        /// was more than 60% full and the trip costs morale instead of paying it.</summary>
        public int ChosenBinRemaining(StaffMember staff)
            => _bin.TryGetValue(staff, out var t) && _stockOf(t.Id) is { } s ? s.Level : FeatureStock.Full;

        /// <summary>READ: 0x80024210, <see cref="FeatureStock.Refill"/> with today's total-day count.
        /// ⭐ FREE, INSTANT, AND THE ONLY THING IN THE GAME THAT RAISES THE BYTE — a feature that no
        /// handyman ever reaches can only ever run down.</summary>
        public void EmptyChosenBin(StaffMember staff)
        {
            if (!_bin.TryGetValue(staff, out var t)) return;
            if (_stockOf(t.Id) is { } s)
            {
                if (s.Level < Handyman.NeglectedBinRemaining) _litter.BinsNeglected++;
                s.Refill(_today());
                _litter.BinsEmptied++;
            }
            _bin.Remove(staff);
        }
    }

    /// <summary>The park as a guest's two ACTIVITY states read it (TPW.Sim.IVisitorActivityWorld):
    /// being sick (29) and watching an entertainer (28).
    ///
    /// ⚠⚠ BOTH HANDLERS WERE UNREACHABLE, AND WIRING THE OTHER TWO PASSES IS WHAT ARMED THEM. Nothing
    /// ever entered state 28 or 29 until the needs clock and the idle pass were connected earlier
    /// today; those two are the only things that push them. From that commit until this one, a guest
    /// whose nausea crossed the line went to state 29 and STAYED there — VisitorActivity.Vomit is the
    /// handler and no code in the game project called it, so the nausea never cleared, the state never
    /// returned, and the guest stood still for the rest of the park's life. State 28 is the same shape
    /// via the needs pass.
    ///
    /// ⭐ FIXING A PASS CAN ARM A TRAP ONE STATE DOWNSTREAM. The freeze was not in the code I changed;
    /// it was in the code my change made reachable for the first time. Anything that newly enters a
    /// state is a reason to check who, if anyone, leaves it.</summary>
    sealed class ParkActivityWorld : IVisitorActivityWorld
    {
        readonly Func<long> _now;
        readonly ParkLitterWorld _litter;
        readonly IRandomSource _rng;
        readonly Func<Visitor, (int X, int Y)> _guestPos;
        readonly Func<StaffMember, (int X, int Y)> _staffPos;

        public ParkActivityWorld(Func<long> now, ParkLitterWorld litter, IRandomSource rng,
                                 Func<Visitor, (int X, int Y)> guestPos,
                                 Func<StaffMember, (int X, int Y)> staffPos)
        { _now = now; _litter = litter; _rng = rng; _guestPos = guestPos; _staffPos = staffPos; }

        public long NowTick => _now();

        /// <summary>⚠ RAW SIGNED 8.8 (P+0x18/P+0x1A), NOT TILES — Vomit adds a ±100 scatter to it and
        /// Watch shifts it down itself. The sim's own comment on Watch flags that the FACING uses tiles
        /// while the litter placement uses the raw value, out of the same getter.</summary>
        public (int X, int Y) Position(Visitor guest) => _guestPos(guest);
        public (int X, int Y) Position(StaffMember entertainer) => _staffPos(entertainer);

        /// <summary>READ: 0x800514E0. ⚠ EVEN A FUTURE VOMIT PIECE DRAWS ITS ORDINARY SPRITE FIRST
        /// (0x800665CC) and PlaceLitter overwrites it — so the ordinary-sprite die is consumed either
        /// way, and short-circuiting it here would shift every later roll in the park.</summary>
        public object TryAllocateLitter() => _litter.Pool.TryAllocate(_litter, _rng);

        /// <summary>⚠ PLACE, NOT SCATTER. Vomit has already applied its own ±100 offset to the guest's
        /// position; calling Scatter here would apply a second one and put the mess a tile away.</summary>
        public void PlaceLitter(object litter, int x, int y, int kind) => ((Litter)litter).Place(x, y, kind);
    }
}
