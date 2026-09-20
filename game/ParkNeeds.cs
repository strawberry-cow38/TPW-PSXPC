using System.Collections.Generic;
using System.Linq;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>The park as the per-tick needs pass reads it (TPW.Sim.VisitorNeeds).
    ///
    /// ⚠⚠ THIS PASS WAS PORTED AND NEVER CALLED. VisitorNeeds.Tick had no caller anywhere in the game
    /// project, so every guest kept the needs it spawned with for its whole visit: measured at need A
    /// 36 after 600 frames and 33 after 3000, against a spawn average of 35 — flat, when the clock
    /// should have pinned it at 100 within about fifteen game days.
    ///
    /// ⭐ AND IT IS WHY SHOPS LOOKED BROKEN. A purchase is the base want times the NEED FACTOR times
    /// (happiness + 100)/100. A park of guests who never get hungry buys nothing at any sensible price,
    /// which is indistinguishable from a broken shop until somebody prints the needs. The shop wiring,
    /// the want formula and the prices were all correct; the clock that feeds them was not running.</summary>
    sealed class ParkNeedsWorld : INeedsWorld
    {
        readonly System.Func<long> _now;
        readonly System.Func<IEnumerable<StaffMember>> _staff;
        readonly System.Func<Visitor, (int X, int Z)> _tileOf;
        readonly System.Func<StaffMember, (int X, int Z)> _staffTile;

        public ParkNeedsWorld(System.Func<long> now, System.Func<IEnumerable<StaffMember>> staff,
                              System.Func<Visitor, (int X, int Z)> tileOf,
                              System.Func<StaffMember, (int X, int Z)> staffTile)
        { _now = now; _staff = staff; _tileOf = tileOf; _staffTile = staffTile; }

        public long NowTick => _now();

        /// <summary>0x80059A9C() != 0. ⚠ FALSE, NOT TRACED: the port has no reader for that word, and the
        /// suppression only silences the BUBBLE pass — the needs themselves climb either way. Answering
        /// true would hide every bubble in the game on a guess.</summary>
        public bool IdleNeedsSuppressed => false;

        /// <summary>⚠ NONE, AND THAT IS A GAP RATHER THAN AN ANSWER. The influence bits come from a
        /// per-tile map the port does not build: bit 1 is +6 happiness for something nice to look at,
        /// bit 2 is the entertainer's own mark, bit 4 costs happiness and adds nausea. With None, a
        /// guest gets none of the three — so scenery does nothing for mood yet, and that is a missing
        /// input, not a measured zero.</summary>
        public TileInfluence InfluenceAt(Visitor guest) => TileInfluence.None;

        /// <summary>⚠ (0, 0) BECAUSE THE PORT HAS NO LITTER POOL WIRED. LitterPool exists in the sim and
        /// nothing in the park owns one, so nobody is ever standing near rubbish. Again a gap: with a
        /// pool wired this would cost happiness and add nausea.</summary>
        public (int Litter, int Vomit) LitterNearby(Visitor guest) => (0, 0);

        public bool InQueue(Visitor guest) => guest.InQueue;

        /// <summary>Entertainers with their Manhattan distance in whole tiles. ⭐ THE SIM PICKS THE
        /// NEAREST and equal distances keep the first, so the ORDER of this sequence is part of the
        /// behaviour — it is the park's staff list order, which is hiring order, as the original's
        /// list is.</summary>
        public IEnumerable<(StaffMember Entertainer, int Distance)> EntertainersWithDistances(Visitor guest)
        {
            var (gx, gz) = _tileOf(guest);
            foreach (var s in _staff())
            {
                if (s.Kind != StaffKind.Entertainer) continue;
                var (sx, sz) = _staffTile(s);
                yield return (s, System.Math.Abs(sx - gx) + System.Math.Abs(sz - gz));
            }
        }
    }
}
