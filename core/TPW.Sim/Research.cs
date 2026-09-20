using System;
using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>READ: signed type/index bytes in the 60-entry table at 0x80109A28.
    /// Type 8 is a track-piece unlock, not a placed attraction (research.md §3).</summary>
    public readonly record struct ResearchDefinition(int Type, int Index);

    /// <summary>READ: rides record+0x48/+0x4C+0x34*level; other records +0x28/+0x24.
    /// Tier gates eligibility; Work is the accumulator's target, not pounds or ticks.</summary>
    public readonly record struct ResearchLevel(int Tier, int Work);

    /// <summary>READ: catalogue+3 completed count, catalogue+2 whole percent (0x8006AF18/2C).</summary>
    public readonly record struct ResearchProgress(byte CompletedLevels, byte Percent);

    /// <summary>The host supplies the CURRENT park's decoded catalogue and effects. No bank,
    /// laboratory, timescale, researcher count or engine is needed by this arithmetic.</summary>
    public interface IResearchCatalogueWorld
    {
        int DefinitionCount(int type);
        ResearchLevel ReadResearchLevel(ResearchDefinition definition, int level);
        int BuiltCount(ResearchDefinition definition);
        bool AllResearchUnlocked { get; }
        bool RestrictedMode { get; }
        int MechanicCount { get; }
        /// <summary>0x8009BF68: display discovery/advisor message after updating the catalogue.
        /// Message ids are selected here; text, queue ownership and rendering belong to the host.</summary>
        void AnnounceDiscovery(ResearchDefinition definition, int messageId);
    }

    /// <summary>READ: one 0x1C-byte topic at research BANK+0xC+0x1C*slot.</summary>
    public sealed class ResearchTopic
    {
        public int Attention { get; internal set; } // +0, UI writes 100; not a rate multiplier.
        public uint RequiredFixed { get; internal set; } // +4, Work<<12.
        public uint ProgressFixed { get; internal set; } // +8.
        public bool Active { get; internal set; } // +0xC.
        public bool Finished { get; internal set; } // +0x10.
        public ResearchDefinition Definition { get; internal set; }
        /// <summary>0x8009C1A0: unsigned low-word multiply, no clamp to 100.</summary>
        public uint Percent => RequiredFixed == 0 ? 100u
            : unchecked(ProgressFixed * 100u) / RequiredFixed;
    }

    /// <summary>READ: the separate research BANK, 0x8009B380. Own one per loaded park.
    /// See research.md §0 before changing the older Researcher or RidePanel rules.</summary>
    public sealed class ResearchSystem
    {
        public const int TopicCount = 5; // 0x8009B660.
        public const int ProgressRecordCount = 60; // 0x8006AEEC.
        public const int MaximumLevelCount = 3; // 0x8006AC88.
        public const int DefaultFunding = 80; // 0x8009B230/240.
        public const int MinimumFunding = 70, MaximumFunding = 100; // 0x8007C3D8..E8.
        public const int FixedShift = 12; // 0x8009B6AC.
        readonly IResearchCatalogueWorld world;
        readonly Dictionary<ResearchDefinition, ResearchProgress> progress = new();
        readonly ResearchTopic[] topics = new ResearchTopic[TopicCount];
        readonly int[] ceilings = new int[TopicCount];
        bool tiersDirty = true;

        public ResearchSystem(IResearchCatalogueWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            for (int i = 0; i < TopicCount; i++) topics[i] = new ResearchTopic();
        }

        /// <summary>Raw research BANK+4. Its setter does not clamp (0x8009B40C).
        /// This is effort, despite the inherited IResearchWorld name; it never spends money.</summary>
        public int Funding { get; set; } = DefaultFunding;
        public ResearchTopic Topic(int slot) => topics[slot];
        public bool AnyActive
        {
            get { foreach (var topic in topics) if (topic.Active) return true; return false; }
        }

        public void ApplyFundingSlider(int value) => Funding = Math.Clamp(value, MinimumFunding, MaximumFunding);

        public ResearchProgress Progress(ResearchDefinition definition)
        {
            if (!progress.TryGetValue(definition, out var value))
            {
                // The PSX dereferences null when its fixed table is exhausted. No invented eviction.
                if (progress.Count == ProgressRecordCount)
                    throw new InvalidOperationException("The PSX's 60 research records are exhausted.");
                progress.Add(definition, value);
            }
            return value;
        }

        /// <summary>READ: 0x8006ACDC, also the save loader's entry. Completes at most ONE level,
        /// drops excess percent, refuses older levels but permits a lower percent on the same level.</summary>
        public void StoreProgress(ResearchDefinition definition, int level, int percent)
        {
            var previous = Progress(definition);
            if (percent >= 100) { level++; percent = 0; }
            if (level < previous.CompletedLevels) return;
            progress[definition] = new(unchecked((byte)level), unchecked((byte)percent));
        }

        /// <summary>READ: 0x8006AB54. A zero TIER unlocks for free; Work is not tested here.
        /// Querying a future level returns the current percent, rather than a fabricated zero.</summary>
        public int ProgressPercent(ResearchDefinition definition, int level)
        {
            int tier = world.ReadResearchLevel(definition, level).Tier;
            var value = Progress(definition);
            if (level < value.CompletedLevels) return 100;
            if (tier == 0)
            {
                StoreProgress(definition, level, 100);
                return 100;
            }
            return value.Percent;
        }

        /// <summary>READ: 0x8006A92C. Restricted mode bypasses availability, but NOT LevelCount.</summary>
        public bool IsAvailable(ResearchDefinition definition, int level = 0)
            => world.AllResearchUnlocked || world.RestrictedMode || ProgressPercent(definition, level) == 100;

        /// <summary>READ: 0x8006AC04, directly supplies IRideUpgradeWorld.ResearchedLevelCount
        /// and IParkStatisticsWorld.AvailableLevelCount. Queries can finish free levels.</summary>
        public int LevelCount(ResearchDefinition definition)
        {
            if (world.AllResearchUnlocked) return MaximumLevelCount;
            int level = Progress(definition).CompletedLevels;
            while (level < MaximumLevelCount && world.ReadResearchLevel(definition, level).Tier == 0)
            {
                StoreProgress(definition, level, 100);
                level = Progress(definition).CompletedLevels;
            }
            return level;
        }

        static readonly int[] RideMenuOrder = { 3, 6, 7, 1 }; // 0x800493AC / 0x80049624.
        static readonly int[] RideTierOrder = { 6, 7, 1, 3 }; // table 0x800E516C.

        IEnumerable<ResearchDefinition> Definitions(IEnumerable<int> types)
        {
            foreach (int type in types)
                for (int index = 0; index < world.DefinitionCount(type); index++)
                    yield return new(type, index);
        }

        /// <summary>READ: 0x8007C450 / 0x800493AC..0x80049760: ordered, player-selected lists.
        /// The UI appends a blank row; it is represented by Stop, not a fake definition.</summary>
        public IReadOnlyList<ResearchDefinition> Candidates(int slot)
        {
            IEnumerable<int> types = slot switch
            {
                0 => RideMenuOrder, 1 => new[] { 4 }, 2 => new[] { 5 }, 3 => new[] { 2 },
                4 => new[] { 3, 6, 7, 1, 8 },
                _ => throw new ArgumentOutOfRangeException(nameof(slot)),
            };
            var result = new List<ResearchDefinition>();
            foreach (var definition in Definitions(types))
                if (CanSelect(slot, definition)) result.Add(definition);
            return result;
        }

        /// <summary>READ: 0x8009BD64. Upgrade topics require a built instance; track pieces instead
        /// require track-ride variant ZERO, regardless of which track ride the player has built.</summary>
        public bool CanSelect(int slot, ResearchDefinition definition)
        {
            RefreshTiers();
            bool available = IsAvailable(definition);
            if (definition.Type == 8)
                return IsAvailable(new(6, 0)) && !available;
            if (slot != 4)
                return ceilings[slot] >= world.ReadResearchLevel(definition, 0).Tier && !available;
            return available && world.BuiltCount(definition) != 0 && LevelCount(definition) < MaximumLevelCount;
        }

        /// <summary>READ: 0x8009B448, lower-level start. Does NOT repeat the menu's built-instance
        /// predicate. It refuses an occupied slot and tier-gates slots 0..3; slot 4 bypasses tiers.</summary>
        public bool Start(int slot, ResearchDefinition definition)
        {
            int level = LevelCount(definition);
            int percent = ProgressPercent(definition, level);
            var data = world.ReadResearchLevel(definition, level);
            int work = IsAvailable(definition, level) ? 0 : data.Work;
            var topic = topics[slot];
            if (topic.Active) return false;
            RefreshTiers();
            if (slot != 4 && ceilings[slot] < data.Tier) return false;
            topic.Active = true;
            topic.Finished = false;
            topic.Attention = 0;
            topic.Definition = definition;
            topic.RequiredFixed = unchecked((uint)(work << FixedShift));
            // ⚠ DO NOT FIX: switching/resuming loses fractional percent, 0x8009C0FC.
            topic.ProgressFixed = unchecked((uint)percent * topic.RequiredFixed) / 100u;
            return true;
        }

        public void Stop(int slot) => topics[slot].Active = false; // 0x8009BD30.

        /// <summary>READ: confirm stops the previous topic even if its replacement fails (0x8007C510).</summary>
        public bool Select(int slot, ResearchDefinition? definition)
        {
            Stop(slot);
            return definition.HasValue && Start(slot, definition.Value);
        }

        /// <summary>Wire IResearchWorld.ContributeResearch here: the EXISTING Researcher.Research
        /// already multiplies skill points by Funding. Do not multiply funding a second time.
        /// Native multiplication is inside 0x8009B618; moving it across this interface is exact.</summary>
        public void ContributeResearch(int fundedPoints)
        {
            int active = 0;
            foreach (var topic in topics) if (topic.Active) active++;
            if (active == 0) return;
            uint share = unchecked((uint)(fundedPoints << FixedShift)) / (uint)(100 * active);
            foreach (var topic in topics)
            {
                if (!topic.Active) continue;
                topic.ProgressFixed = unchecked(topic.ProgressFixed + share);
                var definition = topic.Definition;
                int level = LevelCount(definition);
                StoreProgress(definition, level, unchecked((int)topic.Percent));
                if (topic.ProgressFixed < topic.RequiredFixed) continue;
                topic.Finished = true;
                topic.Active = false;
                tiersDirty = true;
                int message = definition.Type switch
                {
                    1 or 3 or 6 or 7 => LevelCount(definition) < 2 ? 0x56
                        : world.MechanicCount == 0 ? 0x90 : 0x57,
                    4 => 0x58, 5 => 0x59, 2 => 0x5A, 8 => 0x57,
                    _ => throw new InvalidOperationException("Unknown research completion type."),
                };
                world.AnnounceDiscovery(definition, message);
                topic.Definition = new(definition.Type, -1);
                // ⚠ DO NOT FIX: surplus is discarded, no next topic, and the original denominator
                // remains in force for later slots in this contribution (0x8009B684..6E4).
            }
        }

        /// <summary>READ: 0x8009BC8C, recomputed only initially or after an active topic finishes.
        /// Zero-tier query unlocks and save loads do NOT set this dirty flag.</summary>
        public void RefreshTiers()
        {
            if (!tiersDirty) return;
            RefreshTier(0, RideTierOrder);
            RefreshTier(1, new[] { 4 });
            RefreshTier(2, new[] { 5 });
            RefreshTier(3, new[] { 2 });
            RefreshTier(4, new[] { 8 });
            tiersDirty = false;
        }

        public int TierCeiling(int slot) { RefreshTiers(); return ceilings[slot]; }

        void RefreshTier(int slot, IEnumerable<int> types)
        {
            var totals = new uint[5];
            var unlocked = new uint[5];
            foreach (var definition in Definitions(types))
            {
                int tier = world.ReadResearchLevel(definition, 0).Tier;
                totals[tier]++;
                if (IsAvailable(definition)) unlocked[tier]++;
            }
            int cursor = 0;
            while (unchecked(3u * unlocked[cursor]) >= unchecked(2u * totals[cursor]))
            {
                cursor++;
                ceilings[slot] = cursor;
                // ⚠ DO NOT FIX: the PSX has NO bound here and reads beyond both five-word arrays
                // (0x8009BA98..C4). No deterministic overread is established. Fail explicitly.
                if (cursor == totals.Length)
                    throw new InvalidOperationException("Research tier scan overruns its five PSX bins.");
            }
            // No store on initial failure: the old ceiling survives, 0x8009BA84.
        }

        /// <summary>READ: 16-byte BANK save, funding then five (active,type,index) byte triples.
        /// Save catalogue Percent/CompletedLevels separately with Progress/StoreProgress FIRST.</summary>
        public byte[] SaveTopics()
        {
            var saved = new byte[16];
            saved[0] = unchecked((byte)Funding);
            for (int i = 0; i < TopicCount; i++)
            {
                saved[1 + 3 * i] = topics[i].Active ? (byte)1 : (byte)0;
                saved[2 + 3 * i] = unchecked((byte)topics[i].Definition.Type);
                saved[3 + 3 * i] = unchecked((byte)topics[i].Definition.Index);
            }
            return saved;
        }

        /// <summary>READ: 0x8009B2EC on a fresh park instance, AFTER catalogue restoration.
        /// Active topics are restarted via Start; fractional work is not in the save.</summary>
        public void RestoreTopics(ReadOnlySpan<byte> saved)
        {
            if (saved.Length != 16) throw new ArgumentException("Research BANK save is 16 bytes.", nameof(saved));
            Funding = saved[0];
            for (int i = 0; i < TopicCount; i++)
                if (saved[1 + 3 * i] != 0)
                    Start(i, new(saved[2 + 3 * i], unchecked((sbyte)saved[3 + 3 * i])));
        }
    }
}
