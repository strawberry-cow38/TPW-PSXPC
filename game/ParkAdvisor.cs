using System;
using System.Collections.Generic;
using System.Linq;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>The advisor's world: what the 72 statistics read and where a rule's message goes.
    ///
    /// ⭐⭐ THE RULES WERE PORTED AND NEVER CALLED. `ParkStatistics`, `ParkStatisticRules` and
    /// `StatisticInterpreter` carry 72 statistics and 125 rule programs, all tested, and `game/` named
    /// none of them — so the advisor could talk (its model and speech drive the loading screen) and had
    /// nothing to say, because the thing that decides what it says never ran. That is the fourth instance
    /// of this port's dominant failure today, and the one `tools/dead_port_audit.py` predicted.
    ///
    /// ⚠ WHERE THIS LIES, IT SAYS SO. Several inputs have no source in the port yet. Each one below names
    /// what it is standing in for rather than returning a quiet zero, because a statistic fed a silent
    /// default produces advice that is confidently wrong — which is worse than an advisor that stays
    /// quiet. <see cref="Gaps"/> lists them at runtime so the gap is visible rather than remembered.</summary>
    public sealed class ParkAdvisorWorld : IParkStatisticsWorld
    {
        readonly Func<ParkMap> _map;
        readonly Func<Bank> _bank;
        readonly Func<ParkFinances> _finances;
        readonly Func<uint> _totalDays;
        readonly Func<bool> _parkOpen;
        readonly Func<IEnumerable<Visitor>> _visitors;
        readonly Func<IEnumerable<StatisticAttraction>> _attractions;
        readonly Func<IEnumerable<StatisticStaff>> _staff;
        readonly Func<IEnumerable<StatisticDefinition>> _definitions;
        readonly Func<bool> _idle;

        /// <summary>Message ids a rule has posted and the advisor has not spoken yet. The interpreter will
        /// not start another rule while this is non-empty (0x80016870), so it is the advisor's own queue
        /// and not merely a log.</summary>
        readonly Queue<ushort> _say = new();

        public ParkAdvisorWorld(Func<ParkMap> map, Func<Bank> bank, Func<ParkFinances> finances,
                                Func<uint> totalDays, Func<bool> parkOpen,
                                Func<IEnumerable<Visitor>> visitors,
                                Func<IEnumerable<StatisticAttraction>> attractions,
                                Func<IEnumerable<StatisticStaff>> staff,
                                Func<IEnumerable<StatisticDefinition>> definitions, Func<bool> idle = null)
        {
            _map = map; _bank = bank; _finances = finances; _totalDays = totalDays; _parkOpen = parkOpen;
            _visitors = visitors; _attractions = attractions; _staff = staff; _definitions = definitions;
            _idle = idle;
        }

        // ── what the port can answer honestly ────────────────────────────────────────────────────────
        public bool StatisticsEnabled => _map() != null;
        public bool ParkOpen => _parkOpen();
        public uint TotalDays => _totalDays();
        public uint Month => (uint)(TotalDays / 30 % 12);
        public uint Year => (uint)(TotalDays / 365);
        public int VisitorCount => _visitors().Count();
        public IEnumerable<Visitor> Visitors => _visitors();
        public IEnumerable<StatisticAttraction> Attractions => _attractions();
        public IEnumerable<StatisticDefinition> Definitions => _definitions();
        public IEnumerable<StatisticStaff> Staff => _staff();
        public int BalanceRaw => (int)(_bank()?.Balance.Raw ?? 0);
        public int MapWidthTiles => _map()?.Width ?? 0;
        public int MapHeightTiles => _map()?.Height ?? 0;
        public bool IsPathTile(int x, int y)
        {
            var m = _map();
            if (m == null || x < 0 || y < 0 || x >= m.Width || y >= m.Height) return false;
            var t = m[x, y].Type;
            return t == TileType.Path || t == TileType.QueuePath || t == TileType.PathQueueOverlap;
        }

        // ── the advisor's own state ─────────────────────────────────────────────────────────────────
        /// <summary>⭐ READ (0x80013298): the whole sweep is gated on the advisor being idle, so a park
        /// whose advisor is mid-sentence stops refreshing statistics ENTIRELY — for the ~200 ticks he takes
        /// to arrive, speak, leave and stay away, plus the length of the recording. This was a stand-in
        /// returning true until `ParkAdvisor` existed to answer it; it is now the real state.</summary>
        public bool AdvisorIdle => _idle == null || _idle();
        public bool AdvisorQueueEmpty => _say.Count == 0;
        public void PostMessage(ushort messageId) => _say.Enqueue(messageId);

        /// <summary>Take the next thing the advisor has to say, or null. Draining this is what lets the
        /// interpreter move to the next rule.</summary>
        public ushort? TakeMessage() => _say.Count == 0 ? null : _say.Dequeue();
        public int Pending => _say.Count;

        /// <summary>⚠ Opcode 8. findings/statistics.md §0 records a SOURCE DISAGREEMENT here — the binary
        /// removes a UI message where a report said it acts — so the port does nothing and says nothing,
        /// rather than picking one reading and hiding the choice.</summary>
        public void ApplyRuleAction8(short messageId) { }

        // ── the gaps, named ─────────────────────────────────────────────────────────────────────────
        /// <summary>⚠ Litter is not pooled in the port yet (tinyclaw flagged the same gap from a soak), so
        /// no guest ever drops any and this is structurally zero rather than measured. Any rule that reads
        /// it is therefore answering about a park that cannot get dirty.</summary>
        public int LiveLitterCount => 0;

        /// <summary>⚠ No staff-carrying in the port, so nothing is ever held.</summary>
        public StaffKind? HeldStaffKind => null;

        /// <summary>⚠ No strike model in the port.</summary>
        public bool MechanicsOnStrike => false;

        /// <summary>⚠ Research landed today and is not wired to the park yet.</summary>
        public bool AnyResearchActive => false;

        /// <summary>Last month's wages, really last month's: ParkFinances keeps the closed month's result.
        /// ⚠ Zero until the first month end, which is a true statement about a park that has not had one
        /// rather than a stand-in.</summary>
        public int LastMonthWagesRaw => (int)(_finances()?.LastMonthEnd?.Wages.Raw ?? 0);

        /// <summary>⚠ INCOME IS NOT KEPT. The month end records what was CHARGED — loans and wages — and
        /// the closing balance, but not what came in. Deriving it from the balance change would fold in
        /// every purchase and refund made during the month, which is a different number wearing the right
        /// name. So this is 0 and listed as a gap rather than computed from something that only looks
        /// like income.</summary>
        public int LastMonthIncomeRaw => 0;

        public bool IsDefinitionAvailable(StatisticDefinition definition) => true;
        public int AvailableLevelCount(StatisticDefinition definition) => 1;

        /// <summary>Every input above that is a stand-in rather than a measurement, for the readout. An
        /// advisor answering from these is answering about a park that is not quite the one on screen.</summary>
        public static readonly string[] Gaps =
        {
            "LiveLitterCount: no litter pool, so the park cannot get dirty",
            "HeldStaffKind: no staff carrying",
            "MechanicsOnStrike: no strike model",
            "AnyResearchActive: research not wired to the park",
            "LastMonthIncomeRaw: the month end records what was CHARGED, not what came in",
            "IsDefinitionAvailable / AvailableLevelCount: no research gate, everything reads available",
            "StatisticStaff.Grade: the port has no pay grade, so slots 40..44 read as untrained staff",
        };
    }
}
