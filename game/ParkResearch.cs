using System;
using System.Collections.Generic;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>The attraction catalogue as the research system reads it
    /// (TPW.Sim.IResearchCatalogueWorld), and the park as a RESEARCHER reads it (IResearchWorld).
    ///
    /// ⚠⚠ THE SIXTH PORTED-BUT-NEVER-CALLED SYSTEM. `Researcher.Idle` and `.Research` had no call
    /// sites in the game project, `IResearchWorld` had no implementation, and nothing anywhere owned
    /// a `ResearchSystem` — so a hired researcher drew a wage and wandered, and the entire research
    /// tree (topics, tiers, funding, progress, discovery) sat in the sim doing nothing. Four of the
    /// five staff classes now work; the guard is the last, and it is blocked on there being no guard.
    ///
    /// ⭐ TWO FIELDS WERE MISSING FROM THE RECORD AND THAT IS WHY THIS COULD NOT BE WIRED BEFORE.
    /// `TPW.Data.RideLevel` read every level field from +0x08 to +0x2C EXCEPT +0x24 and +0x28 — the
    /// research tier and the work. They are exactly what `ReadResearchLevel` returns. Added with this,
    /// and the values confirm the offsets rather than just compiling: over all 498 level blocks on the
    /// disc (0 skipped) tier is an ordinal 0..3, and work takes seventeen distinct values, every one a
    /// multiple of fifty, 0 to 2750. The Crazy Ape's level 0 reads tier 0 / work 0 — a starter ride,
    /// available with no research at all — and Eruption reads tier 3 / work 550.</summary>
    sealed class ParkResearchWorld : IResearchCatalogueWorld, IResearchWorld
    {
        readonly ParkStaffWorld _base;
        readonly Func<IReadOnlyList<AttractionDefinition>> _definitions;
        readonly Func<IEnumerable<int>> _placedEntries;
        readonly Func<IEnumerable<StaffMember>> _staff;
        readonly Action<ushort> _announce;

        public ParkResearchWorld(ParkStaffWorld shared,
                                 Func<IReadOnlyList<AttractionDefinition>> definitions,
                                 Func<IEnumerable<int>> placedEntries,
                                 Func<IEnumerable<StaffMember>> staff,
                                 Action<ushort> announce)
        { _base = shared; _definitions = definitions; _placedEntries = placedEntries;
          _staff = staff; _announce = announce; }

        public ResearchSystem System { get; private set; }
        public void Attach(ResearchSystem system) => System = system;

        public Staffer Current { get => _base.Current; set => _base.Current = value; }

        public long NowTick => _base.NowTick;
        public bool IsTypeOnStrike(StaffKind kind) => _base.IsTypeOnStrike(kind);
        public bool HasPatrolRect(StaffMember s) => _base.HasPatrolRect(s);
        public bool TryPathIntoPatrolArea(StaffMember s) => _base.TryPathIntoPatrolArea(s);
        public bool StrikeMusterExists => _base.StrikeMusterExists;
        public bool TryPathToStrikeMuster(StaffMember s) => _base.TryPathToStrikeMuster(s);
        public bool TryPathToRest(StaffMember s) => _base.TryPathToRest(s);

        // ---- IResearchWorld: the two things a researcher actually does ----------------------------

        /// <summary>Spread this tick's points over the active topics. ⭐ IT IS A TRICKLE, NOT A RATE:
        /// a researcher only chooses to work on three idle decisions in ten, and a patrol leg is many
        /// ticks, so the walking is why research crawls rather than being decoration.</summary>
        public void ContributeResearch(int points) => System?.ContributeResearch(points);

        /// <summary>The research funding slider, 70..100, default 80. ⚠ NO UI SETS IT — the panel that
        /// moves this slider does not exist, so it sits at the default. That is a missing input, not a
        /// measured value, and it feeds BOTH the points multiplier and the researcher's tiredness.</summary>
        public int ResearchFunding => System?.Funding ?? ResearchSystem.DefaultFunding;

        // ---- IResearchCatalogueWorld -------------------------------------------------------------

        /// <summary>How many definitions of this type the catalogue holds.
        ///
        /// ⚠ GUESS-medium ON THE ORDER, and it is the one thing here that could be silently wrong.
        /// A ResearchDefinition is (type, index) and this port takes the index to mean "position among
        /// the definitions of that type, in catalogue order". The binary carries a per-object
        /// definition selector at A+0x6B (see findings/rating.md §0) which would settle whether that
        /// ordering matches; it has not been read against this list. A wrong order does not crash —
        /// it researches the wrong ride, which looks exactly like working software.</summary>
        public int DefinitionCount(int type)
        {
            int n = 0;
            foreach (var d in _definitions()) if (d.Type == type) n++;
            return n;
        }

        /// <summary>READ: rides' level fields are record +0x48+0x34×L = tier, +0x4C+0x34×L = work
        /// (research.md §3.2). ⚠ A LEVEL THE RECORD DOES NOT HAVE READS (0, 0), and that is not a
        /// neutral default: tier 0 with zero work is the auto-unlock case, so a missing level reads as
        /// "already researched" rather than as unavailable. That is what the sim does with an absent
        /// level too, so it is reproduced rather than guarded.</summary>
        public ResearchLevel ReadResearchLevel(ResearchDefinition definition, int level)
        {
            var d = Definition(definition);
            if (d == null || level < 0 || level >= d.Levels.Length) return new ResearchLevel(0, 0);
            var l = d.Levels[level];
            return new ResearchLevel(l.ResearchTier, l.ResearchWork);
        }

        /// <summary>How many of this definition stand in the park. Used by the tier rule, which is a
        /// two-thirds test over what you have already built (research.md §3.2).</summary>
        public int BuiltCount(ResearchDefinition definition)
        {
            var d = Definition(definition);
            if (d == null) return 0;
            int n = 0;
            foreach (int entry in _placedEntries()) if (entry == d.Entry) n++;
            return n;
        }

        /// <summary>⚠ FALSE BY DEFAULT, AND STILL A GAP. This is the scenario flag that hands a park the
        /// whole catalogue up front; the port has no scenario loader, so every park researches from
        /// scratch and answering true by default would silently skip the system this file exists to
        /// start. It is now SETTABLE, because it is the only way to reach the upgrade chain in a test --
        /// every ride needs a researched level before its panel will offer one, and research takes game
        /// months. Set only by an explicit debug flag, never by a park.</summary>
        public bool AllUnlocked;
        public bool AllResearchUnlocked => AllUnlocked;

        /// <summary>⚠ FALSE, AND A GAP. Restricted mode limits which definitions a scenario offers.
        /// Same reason as above: no scenario loader, so nothing restricts anything.</summary>
        public bool RestrictedMode => false;

        /// <summary>Hired mechanics. ⭐ THEY ARE AN INPUT TO RESEARCH, which is not obvious: the
        /// system asks how many you employ, so a park with no mechanics researches differently.</summary>
        public int MechanicCount
        {
            get
            {
                int n = 0;
                foreach (var s in _staff()) if (s.Kind == StaffKind.Mechanic) n++;
                return n;
            }
        }

        /// <summary>0x8009BF68: tell the player what was discovered. Routed to the advisor, which is
        /// the thing in this port that owns messages. ⚠ THE ID IS CHOSEN BY THE SIM and passed through
        /// unexamined; if it ever falls outside the advisor's table the advisor rejects it, and that
        /// rejection is the correct place for the complaint, not here.</summary>
        public void AnnounceDiscovery(ResearchDefinition definition, int messageId)
            => _announce?.Invoke(unchecked((ushort)messageId));

        AttractionDefinition Definition(ResearchDefinition definition)
        {
            int i = 0;
            foreach (var d in _definitions())
            {
                if (d.Type != definition.Type) continue;
                if (i++ == definition.Index) return d;
            }
            return null;
        }
    }
}
