using System;
using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>The tile-influence bits a guest standing somewhere picks up (§2.9).
    ///
    /// behaviour.md's original producer labels remain GUESS. READ refinement: the entertainer writes
    /// 2 at 0x80095A34..38 through 0x800961D8; bit 1's producer remains NOT ESTABLISHED.
    /// See findings/visitor-rest.md. Values are ORed over covering objects in list 0x80103860.</summary>
    [Flags]
    public enum TileInfluence
    {
        None = 0,
        /// <summary>Bit 1: +6 happiness. GUESS "something nice to look at".</summary>
        Pleasant = 1,
        /// <summary>READ: bit 2 is written by the entertainer, 0x80095A34..38.</summary>
        Entertainer = 2,
        /// <summary>READ: bit 4 costs happiness and adds nausea. Idle roll 5's particle sets it
        /// (0x8008C32C..330); its visual identity and the old "all litter" label remain GUESS.</summary>
        Unpleasant = 4,
    }

    /// <summary>What the park tells a guest about where it is standing.</summary>
    public interface INeedsWorld
    {
        long NowTick { get; }
        /// <summary>0x80059A9C() != 0. Suppresses the bubble pass.</summary>
        bool IdleNeedsSuppressed { get; }
        /// <summary>The OR of the influence bits covering this guest.</summary>
        TileInfluence InfluenceAt(Visitor guest);
        /// <summary>Litter objects the original counts, and how many of those are vomit (obj+0x1C == 0x9E).
        ///
        /// ⭐ TWO INDEPENDENT TRACES AGREE AGAINST THE REPORT. behaviour.md §2.9 says "within 2 tiles".
        /// Both the needs pass and the litter pass were traced separately, by different agents, on the
        /// same day and without sight of each other's work, and both read the test at
        /// 0x800901B8..0x800901F4 (`slti v0, v0, 2`) as `|dx| + |dy| &lt; 2` in WHOLE tiles — the guest's
        /// own tile and its four edge-neighbours, which is FIVE tiles, not a 5x5 block. Agreement
        /// reached twice from the same instruction and against a shared prior is the strongest evidence
        /// this project has produced for a correction.
        ///
        /// ⚠ AND THE IMPLEMENTATION HAS NOT MOVED YET. LitterPool.Nearby still counts the report's wider
        /// radius and its tests pin that, which is the right state for a correction that arrived at
        /// merge time: flipping a behaviour under tests written for the other one is how a merge ships a
        /// silent change. findings/needs.md §0 item 2 and findings/litter.md §0 both carry it.</summary>
        (int Litter, int Vomit) LitterNearby(Visitor guest);
        /// <summary>True while the guest is queueing, which blocks the entertainer push.</summary>
        bool InQueue(Visitor guest);
        /// <summary>READ: entertainers in list 0x80053768 order, with abs(dx)+abs(dy) in whole tiles
        /// (slot 10, 0x800939B0; distance 0x80090078..B8). No performance-state or distance cutoff here.
        /// The sim chooses the nearest; equal distances keep the first. This does not claim staff.</summary>
        IEnumerable<(StaffMember Entertainer, int Distance)> EntertainersWithDistances(Visitor guest);
    }

    /// <summary>Per-tick needs update -- vtable slot 41, 0x8008FE60 (§2.9).
    ///
    /// ⭐ THIS RUNS BEFORE EVERY STATE HANDLER, not as part of Idle. A guest queueing, riding or walking
    /// to the exit is still getting hungrier, still being depressed by nearby litter and still picking
    /// thought bubbles. Folding it into Idle -- the obvious shortcut, since Idle is where most guests
    /// are -- would freeze the needs of every guest actually doing something.
    ///
    /// ⚠ FOUR DIFFERENT PERIODS, AND ONLY ONE OF THEM IS STAGGERED HERE. The 8-tick influence pass is
    /// staggered per guest by the same field as the decision; the 64, 50, 40 and 128 tick passes are
    /// written in behaviour.md §2.9 without a stagger, so they are implemented on the bare tick and every
    /// guest takes its litter damage on the same tick. findings/needs.md §0 item 1 records that the
    /// binary staggers ALL FOUR by V+0x10 (0x80090128, 0x80090320, 0x80090384, 0x800903F8); the report's
    /// version is kept deliberately until that disagreement is reviewed. Same long-run rate either way.
    ///
    /// ⭐ ONLY TWO STATS GROW ON A CLOCK. Need A and need B are the only bytes anything raises with time
    /// (needs.md §2). Boredom, V+0x5D, nausea and tiredness move only when the guest does something or
    /// something is done to it -- a guest stood still on a clean tile gets hungrier and thirstier and
    /// nothing else. Adding a passive drift to any of the other four is inventing a mechanic.
    ///
    /// NOT PORTED: the bubble pass is also skipped when 25 bubbles are already showing park-wide and
    /// this guest has none (0x80090414..0x80090440, counter at 0x80103264); the sim has no park-wide
    /// bubble counter (needs.md §0 item 3).</summary>
    public static class VisitorNeeds
    {
        public const int InfluencePeriod = 8;
        public const int LitterPeriod = 64;
        public const int NeedAPeriod = 50;
        public const int NeedBPeriod = 40;
        public const int BubblePeriod = 128;

        /// <summary>The growth die: `rand(2)` at 0x8009036C and 0x800903D0, so each pass adds 0 or 1 --
        /// half a point per period on average. The die is drawn on every gated tick, even at 100.</summary>
        public const int GrowthRollMax = 2;

        /// <summary>Happiness lost per litter object within 2 tiles, per 64 ticks.</summary>
        public const int LitterHappiness = 3;
        /// <summary>Nausea gained per nearby object that is vomit rather than plain litter.</summary>
        public const int VomitNausea = 3;

        // Bubble ids, in the order the original tests them. First match wins.
        public const int BubbleRideDesire = 0x3B;
        public const int BubbleNauseous = 0x3D;
        public const int BubbleDelighted = 0x31;
        public const int BubbleMiserable = 0x35;
        public const int BubbleHappy = 0x3E;
        public const int BubbleTired = 0x40;
        public const int BubbleContent = 0x39;
        public const int BubbleNone = 0;

        /// <summary>Walk speed a guest adopts when it badly wants a ride (RideDesire &gt; 97).</summary>
        public const int HurryingSpeed = 30;

        /// <summary>Run the update for one tick. Call before the state handler, every tick.</summary>
        public static void Tick(Visitor guest, INeedsWorld world, IRandomSource rng)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            long now = world.NowTick;

            if ((now & (InfluencePeriod - 1)) == (guest.DecisionStagger & (InfluencePeriod - 1)))
                Influence(guest, world);

            if (now % LitterPeriod == 0) LitterAndNeedPenalties(guest, world);
            if (now % NeedAPeriod == 0) guest.NeedA = Stat.Add(guest.NeedA, rng.Next(GrowthRollMax));
            if (now % NeedBPeriod == 0) guest.NeedB = Stat.Add(guest.NeedB, rng.Next(GrowthRollMax));
            if (now % BubblePeriod == 0 && !world.IdleNeedsSuppressed) ChooseBubble(guest, rng);
        }

        static void Influence(Visitor guest, INeedsWorld world)
        {
            var flags = world.InfluenceAt(guest);

            if ((flags & TileInfluence.Pleasant) != 0)
                guest.Happiness = Stat.Add(guest.Happiness, 6);

            if ((flags & TileInfluence.Unpleasant) != 0)
            {
                // ⚠ WORSE WHILE WALKING. Standing in something unpleasant costs 1 happiness and 2
                // nausea; walking through it costs 3 and 5. States 2 and 3 are the moving ones.
                bool walking = guest.State == VisitorState.WalkToDestination || guest.State == VisitorState.WalkToWaypoint;
                guest.Happiness = Stat.Sub(guest.Happiness, walking ? 3 : 1);
                guest.Nausea = Stat.Add(guest.Nausea, walking ? 5 : 2);
            }

            if ((flags & TileInfluence.Entertainer) != 0) TryWatch(guest, world);
        }

        static void TryWatch(Visitor guest, INeedsWorld world)
        {
            // All four gates, and the depth one is the interesting gate: a guest already two states deep
            // will not stop for a show, so an entertainer cannot interrupt a guest mid-errand.
            if (world.InQueue(guest)) return;
            if (guest.StackDepth >= 2) return;
            if (world.NowTick <= guest.EntertainerNotBefore) return;
            if (guest.State == VisitorState.WatchEntertainer) return;

            StaffMember nearest = null;
            int distance = int.MaxValue;
            foreach (var candidate in world.EntertainersWithDistances(guest))
                if (nearest == null || candidate.Distance < distance)
                {
                    nearest = candidate.Entertainer;
                    distance = candidate.Distance;
                }
            if (nearest == null) return;
            guest.WatchedEntertainer = nearest;
            guest.PushState(VisitorState.WatchEntertainer);
            guest.WaitUntil = world.NowTick + VisitorActivity.WatchBaseTicks
                + VisitorActivity.WatchSkillTicks * (nearest.Skill & VisitorActivity.WatchSkillMask);
        }

        static void LitterAndNeedPenalties(Visitor guest, INeedsWorld world)
        {
            var (litter, vomit) = world.LitterNearby(guest);
            if (litter > 0) guest.Happiness = Stat.Sub(guest.Happiness, LitterHappiness * litter);
            if (vomit > 0) guest.Nausea = Stat.Add(guest.Nausea, VomitNausea * vomit);

            // ⭐ FIVE SEPARATE -1s, NOT ONE. Each unmet need docks happiness independently, so a guest
            // that is bored AND queasy AND tired of waiting loses 3 per pass, not 1. The thresholds are
            // all different and none of them is 90 across the board.
            if (guest.Boredom >= 95) guest.Happiness = Stat.Sub(guest.Happiness, 1);
            if (guest.Nausea >= 85) guest.Happiness = Stat.Sub(guest.Happiness, 1);
            if (guest.RideDesire >= 90) guest.Happiness = Stat.Sub(guest.Happiness, 1);
            if (guest.NeedA >= 95) guest.Happiness = Stat.Sub(guest.Happiness, 1);
            if (guest.NeedB >= 85) guest.Happiness = Stat.Sub(guest.Happiness, 1);
        }

        /// <summary>The thought bubble, first match wins (§2.9).
        ///
        /// ⚠ THE ORDER IS NOT THE OBVIOUS ONE. "Delighted" (happiness &gt; 90) is tested BEFORE "happy"
        /// (&gt; 80), so a guest at 95 shows the delighted bubble and never the happy one -- but it is
        /// also tested before "miserable" (&lt; 10), which costs nothing only because the two cannot both
        /// hold. Sorting these by threshold would change which bubble a delighted guest shows.</summary>
        static void ChooseBubble(Visitor guest, IRandomSource rng)
        {
            if (guest.RideDesire > 90)
            {
                guest.Bubble = BubbleRideDesire;
                if (guest.RideDesire > 97) guest.WalkSpeed = HurryingSpeed;
                return;
            }
            if (guest.Nausea > 90) { guest.Bubble = BubbleNauseous; return; }
            if (guest.Happiness > 90) { guest.Bubble = BubbleDelighted; return; }
            if (guest.Happiness < 10) { guest.Bubble = BubbleMiserable; return; }
            if (guest.Happiness > 80) { guest.Bubble = BubbleHappy; return; }
            if (guest.Tiredness > 90) { guest.Bubble = BubbleTired; return; }
            if (guest.Happiness > 25 && guest.Happiness < 75 && rng.Next(10) == 0)
            {
                guest.Bubble = BubbleContent;
                return;
            }
            guest.Bubble = BubbleNone;
        }
    }
}
