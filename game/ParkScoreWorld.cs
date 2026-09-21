using System;
using System.Collections.Generic;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>The park as its own SCORE reads it (TPW.Sim.IParkScoreWorld), and as its OBJECTIVES
    /// read it (IParkObjectiveWorld).
    ///
    /// ⚠⚠ BOTH SYSTEMS WERE MERGED THIS MORNING AND NEITHER HAD A CALLER. The dead-port audit put
    /// them second and third behind the ride panel: ParkScore.RecordIncome had 21 tests and no call
    /// site, ParkObjectives.AfterDay had 7. I wrote the commit messages for both and reported them as
    /// landed. A sim port is not done when it is merged; it is done when something calls it, and the
    /// person merging is the one who can see the difference.
    ///
    /// ⭐ THE SUPPLIERS ALREADY EXISTED. ParkAdvisorWorld feeds the statistics the same visitors,
    /// attractions and staff these two want, so this composes the same delegates rather than walking
    /// the park a third time.</summary>
    sealed class ParkScoreWorld : IParkScoreWorld, IParkObjectiveWorld
    {
        readonly Func<IEnumerable<Visitor>> _visitors;
        readonly Func<IEnumerable<StatisticAttraction>> _attractions;
        readonly Func<IEnumerable<StatisticStaff>> _staff;
        readonly Func<IReadOnlyList<AttractionDefinition>> _definitions;
        readonly Func<ParkFinances> _finances;
        readonly Func<bool> _parkOpen;
        readonly Func<ParkMap> _map;
        readonly Func<uint> _admissions;

        public ParkScoreWorld(Func<IEnumerable<Visitor>> visitors,
                              Func<IEnumerable<StatisticAttraction>> attractions,
                              Func<IEnumerable<StatisticStaff>> staff,
                              Func<IReadOnlyList<AttractionDefinition>> definitions,
                              Func<ParkFinances> finances, Func<bool> parkOpen,
                              Func<ParkMap> map, Func<uint> admissions)
        { _visitors = visitors; _attractions = attractions; _staff = staff; _definitions = definitions;
          _finances = finances; _parkOpen = parkOpen; _map = map; _admissions = admissions; }

        // ---- IParkScoreWorld ----------------------------------------------------------------------

        public IEnumerable<Visitor> Visitors => _visitors();
        IEnumerable<StatisticAttraction> IParkScoreWorld.Attractions => _attractions();
        public IEnumerable<StatisticStaff> Staff => _staff();

        /// <summary>⚠ GUESS-low AND A RETAINED SOURCE DISAGREEMENT. economy.md §3 step 6 says the
        /// price is selected by LEVEL; the binary at 0x800878E4..F8 passes A+0x6B, a DEFINITION
        /// selector. The findings' version is what the sim asks for and what this supplies, so park
        /// VALUE is not claimed binary-equivalent — see findings/rating.md §0. Answering 0 would have
        /// been worse than answering the disputed number: it would have made every park worthless and
        /// looked like a working system.</summary>
        public int ValuePricePounds(AttractionType type, int level)
        {
            foreach (var d in _definitions())
            {
                if ((AttractionType)d.Type != type) continue;
                if (d.Levels.Length == 0) return d.Price;
                int i = level < 0 ? 0 : level >= d.Levels.Length ? d.Levels.Length - 1 : level;
                return d.Levels[i].Price;
            }
            return 0;
        }

        // ---- IParkObjectiveWorld ------------------------------------------------------------------

        /// <summary>⚠ FALSE, AND A GAP — the same sandbox word research answers false for. There is no
        /// scenario loader, and findings/scenario.md established this morning that there is no single
        /// scenario blob to load either, so nothing sets it.</summary>
        public bool RestrictedMode => false;

        /// <summary>READ: McAi+0x1C — TOTAL SUCCESSFUL ADMISSIONS, not guests standing in the park.
        /// The turnstile already counts exactly this; a headcount would fall as guests leave and make
        /// a met objective un-meet itself.</summary>
        public uint Admissions => _admissions();

        public bool ParkOpen => _parkOpen();

        /// <summary>⚠ 0xFFFF UNTIL THE PARK OPENS, and that is the original's sentinel rather than a
        /// missing value (0x80102D24). ⚠ THE PORT DOES NOT PRESERVE THE OPENING STAMP ACROSS A SAVE —
        /// save.md documents why it is not in the park stream — so this is the CURRENT session's
        /// opening month and a loaded park reports as if it opened when it was loaded. A GAP, named
        /// here rather than papered over, because the years-open objective measures against it.</summary>
        public uint OpeningMonth
        {
            get
            {
                if (!_parkOpen()) return 0xFFFF;
                if (_openedAt == null) _openedAt = (uint)(_finances()?.Calendar.TotalMonths ?? 0);
                return _openedAt.Value;
            }
        }
        uint? _openedAt;

        /// <summary>READ: remaining principal in the four occupied loan slots, in TENTHS not pounds.</summary>
        public Money OutstandingLoans => _finances()?.Loans.TotalOutstanding ?? Money.Zero;

        IEnumerable<ObjectiveAttraction> IParkObjectiveWorld.Attractions
        {
            get
            {
                foreach (var a in _attractions())
                    yield return new ObjectiveAttraction(a, ValuePricePounds((AttractionType)a.Type, a.UpgradeLevel));
            }
        }

        /// <summary>⚠ ZERO, AND A GAP. READ: the fifth pool counted at 0x80059914 is
        /// PoolOfTourTransports (0x80103880), which is NOT another ride list — the port has no tour
        /// transports at all. ⚠ DO NOT FIX by counting tour RIDES: transports help meet the 8/10-ride
        /// gates but are deliberately not upgrade-tested, so folding them into the ride count would
        /// make an objective pass on the wrong evidence.</summary>
        public uint TourTransportCount => 0;

        /// <summary>READ: 0x800598A4 counts tile type 2 over width×height, INCLUDING DISCONNECTED
        /// PATHS. A count of reachable path would be a different and more sensible number, and would
        /// not be this one.</summary>
        public uint PathTileCount
        {
            get
            {
                var map = _map();
                if (map == null) return 0;
                uint n = 0;
                for (int x = 0; x < map.Width; x++)
                    for (int z = 0; z < map.Height; z++)
                        if (map[x, z].Type == TileType.Path) n++;
                return n;
            }
        }
    }
}
