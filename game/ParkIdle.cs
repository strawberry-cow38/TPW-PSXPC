using System;
using System.Collections.Generic;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>The park as an idle guest reads it (TPW.Sim.VisitorIdle, state 0).
    ///
    /// ⚠⚠ THIS PASS WAS PORTED AND NEVER CALLED, the same as the needs clock. Its first act every tick
    /// is the LEAVE check — too tired, too unhappy, out of money, or here long enough — and with it
    /// unwired NOBODY EVER DECIDED TO GO HOME. Measured on a long soak: 29 guests, average happiness
    /// 0, need A 99, need B 100, and "0 actually left". A park that fills with maximally miserable
    /// people who will not leave is not a slow drift, it is the population model missing.</summary>
    sealed class ParkIdleWorld : IVisitorWorld
    {
        readonly Func<long> _now;
        readonly Func<int> _day;
        readonly Func<IEnumerable<StaffMember>> _staff;
        readonly Func<Visitor, (int X, int Z)> _tileOf;
        readonly Func<StaffMember, (int X, int Z)> _staffTile;
        readonly Func<Visitor, (int X, int Z)?> _nearestBin;
        readonly Func<Visitor, int, int, bool> _walkTo;

        public ParkIdleWorld(Func<long> now, Func<int> day, Func<IEnumerable<StaffMember>> staff,
                             Func<Visitor, (int X, int Z)> tileOf, Func<StaffMember, (int X, int Z)> staffTile,
                             Func<Visitor, (int X, int Z)?> nearestBin, Func<Visitor, int, int, bool> walkTo)
        {
            _now = now; _day = day; _staff = staff; _tileOf = tileOf;
            _staffTile = staffTile; _nearestBin = nearestBin; _walkTo = walkTo;
        }

        public long NowTick => _now();

        /// <summary>McAi+0x10, the SLOW day counter the leave check measures time-in-park against.
        /// ⚠ NOT the tick clock divided down — the park's calendar owns this, and the guest's
        /// ArrivedOnDay is stamped from the same source or the subtraction is meaningless.</summary>
        public int SlowClockDay => _day();

        /// <summary>⚠ FALSE, NOT TRACED — the same word the needs pass asks about (0x80059A9C). It
        /// suppresses a needs check and all littering; answering true on a guess would silence both.</summary>
        public bool IdleNeedsSuppressed => false;

        /// <summary>Roll 3: a litter bin within 6 tiles, and walk to it. ⭐ THE BINS ARE REAL: a feature
        /// whose record byte +0x2E has bit 2 set IS a litter bin (behaviour.md §0 item 8), and the park
        /// already carries that flag on every placed attraction.</summary>
        public BinSearch TryWalkToBin(Visitor guest)
        {
            if (_nearestBin(guest) is not { } bin) return BinSearch.NoBinInRange;
            return _walkTo(guest, bin.X, bin.Z) ? BinSearch.WalkingToBin : BinSearch.PathRefused;
        }

        /// <summary>Roll 2: an entertainer within 5 tiles loses 5 morale and is shocked. ⭐ THE GUEST'S
        /// OWN STATE NEVER CHANGES — this is something that happens TO the entertainer, which is why it
        /// returns only whether one was in range.</summary>
        public bool TryPeltEntertainer(Visitor guest)
        {
            var (gx, gz) = _tileOf(guest);
            foreach (var s in _staff())
            {
                if (s.Kind != StaffKind.Entertainer) continue;
                var (sx, sz) = _staffTile(s);
                if (Math.Abs(sx - gx) + Math.Abs(sz - gz) > 5) continue;
                s.Morale = Stat.Sub(s.Morale, 5);
                s.SetState(EntertainerStates.Shocked);
                return true;
            }
            return false;
        }

        /// <summary>⚠ NOTHING, BECAUSE THE PORT HAS NO LITTER POOL WIRED. LitterPool exists in the sim
        /// and nothing in the park owns one. This is a GAP: with a pool, roll 3 without a bin and roll 5
        /// from misery would both leave rubbish, which then costs happiness and adds nausea to everyone
        /// standing near it. Silently doing nothing here removes a whole feedback loop from the park.</summary>
        public void DropLitter(Visitor guest) { }
    }
}
