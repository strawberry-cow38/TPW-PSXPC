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
        readonly Action<Visitor> _dropLitter;
        readonly Action<StaffMember, Visitor> _pelt;

        public ParkIdleWorld(Func<long> now, Func<int> day, Func<IEnumerable<StaffMember>> staff,
                             Func<Visitor, (int X, int Z)> tileOf, Func<StaffMember, (int X, int Z)> staffTile,
                             Func<Visitor, (int X, int Z)?> nearestBin, Func<Visitor, int, int, bool> walkTo,
                             Action<Visitor> dropLitter, Action<StaffMember, Visitor> pelt)
        {
            _pelt = pelt;
            _now = now; _day = day; _staff = staff; _tileOf = tileOf;
            _staffTile = staffTile; _nearestBin = nearestBin; _walkTo = walkTo;
            _dropLitter = dropLitter;
        }

        public long NowTick => _now();

        /// <summary>McAi+0x10, the SLOW day counter the leave check measures time-in-park against.
        /// ⚠ NOT the tick clock divided down — the park's calendar owns this, and the guest's
        /// ArrivedOnDay is stamped from the same source or the subtraction is meaningless.</summary>
        public int SlowClockDay => _day();

        /// <summary>⚠ FALSE, NOT TRACED — the same word the needs pass asks about (0x80059A9C). It
        /// suppresses a needs check and all littering; answering true on a guess would silence both.</summary>
        public bool IdleNeedsSuppressed => false;

        /// <summary>Roll 3: a litter bin within 6 tiles, and walk to it.
        ///
        /// ⭐ THE BINS ARE REAL AND THE SEARCH IS RIGHT: bit 2 of a feature record's +0x2E byte is the
        /// Litter Bin and nothing else — entries 19, 98, 193 and 351, one per world. ⚠ An earlier
        /// version of this comment said the flag was never set anywhere; that was a bad sweep of mine
        /// (it skipped short records, which is exactly what a bin is), retracted in
        /// findings/litter.md §8. A park with no bin placed is the only reason this returns nothing.
        ///
        /// ⚠ AND A BIN IS NOT SERVICED BY A HANDYMAN. Bit 2 and bit 0 are disjoint across all 182
        /// feature records — the cleaner's bin round reads bit 0, which is toilets and the like, so
        /// nothing in the port empties a Litter Bin. What does, if anything, is NOT ESTABLISHED.</summary>
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
                // ⭐ THROUGH THE REAL HANDLER NOW. This used to dock 5 morale and set state 32 by
                // hand, which is what Entertainer.Pelted does plus nothing else — it dropped the
                // CULPRIT (the sim records which guest threw it, for the guard to be sent after), and
                // it dropped the random shock duration, so a pelted entertainer recovered on the next
                // tick instead of standing there losing 10 morale every tick for up to four seconds.
                // An approximation that matches the visible half of a handler is the hardest kind of
                // wrong to notice.
                _pelt(s, guest);
                return true;
            }
            return false;
        }

        /// <summary>Roll 3 with no bin in range, and roll 5 out of pure misery: leave rubbish where the
        /// guest is standing. ⭐ THE POOL IS WIRED NOW — this used to be an empty body, which quietly
        /// removed the whole dirt feedback loop: nobody littered, so nobody's happiness was ever docked
        /// for standing near rubbish, so the handyman had nothing to do and the cleaner wage bought
        /// nothing.
        ///
        /// ⚠ DISPUTED FOR ROLL 5, KEPT AS THE FINDINGS STATE IT. VisitorIdle's own note says the misery
        /// branch creates a PARTICLE in the binary rather than a pool object; behaviour.md reads it as
        /// litter and this port keeps behaviour.md. Both paths land here, so if that is ever settled the
        /// fix is one branch in the sim, not here.</summary>
        public void DropLitter(Visitor guest) => _dropLitter(guest);
    }
}
