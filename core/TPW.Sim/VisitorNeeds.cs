using System;

namespace TPW.Sim
{
    /// <summary>The tile-influence bits a guest standing somewhere picks up (§2.9).
    ///
    /// ⚠ WHICH OBJECTS SET WHICH BIT IS NOT ESTABLISHED. behaviour.md guesses scenery = 1,
    /// entertainer = 2, litter/ugly = 4 and says so; the bits are modelled here, the mapping is the
    /// host's to supply. The values are the OR of +0x14 over every object in list 0x80103860 whose
    /// radius squared covers the guest.</summary>
    [Flags]
    public enum TileInfluence
    {
        None = 0,
        /// <summary>Bit 1: +6 happiness. GUESS "something nice to look at".</summary>
        Pleasant = 1,
        /// <summary>Bit 2: an entertainer's area.</summary>
        Entertainer = 2,
        /// <summary>Bit 4: costs happiness and adds nausea, worse while walking.</summary>
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
        /// <summary>Litter objects within 2 tiles, and how many of those are vomit (obj+0x1C == 0x9E).</summary>
        (int Litter, int Vomit) LitterNearby(Visitor guest);
        /// <summary>True while the guest is queueing, which blocks the entertainer push.</summary>
        bool InQueue(Visitor guest);
        /// <summary>Claim the nearest entertainer. Returns false if none; otherwise gives the watch
        /// length, which the original computes as 300 + 60 * (entertainer+0x3C &amp; 7).</summary>
        bool TryWatchEntertainer(Visitor guest, out int watchTicks);
    }

    /// <summary>Per-tick needs update -- vtable slot 41, 0x8008FE60 (§2.9).
    ///
    /// ⭐ THIS RUNS BEFORE EVERY STATE HANDLER, not as part of Idle. A guest queueing, riding or walking
    /// to the exit is still getting hungrier, still being depressed by nearby litter and still picking
    /// thought bubbles. Folding it into Idle -- the obvious shortcut, since Idle is where most guests
    /// are -- would freeze the needs of every guest actually doing something.
    ///
    /// ⚠ FOUR DIFFERENT PERIODS, AND ONLY ONE OF THEM IS STAGGERED. The 8-tick influence pass is
    /// staggered per guest by the same field as the decision; the 64, 50, 40 and 128 tick passes are
    /// written in the report without a stagger, so they are implemented on the bare tick and every guest
    /// takes its litter damage on the same tick. That is what the report says rather than what is
    /// tidy -- if a live reading shows otherwise, this is the place.</summary>
    public static class VisitorNeeds
    {
        public const int InfluencePeriod = 8;
        public const int LitterPeriod = 64;
        public const int NeedAPeriod = 50;
        public const int NeedBPeriod = 40;
        public const int BubblePeriod = 128;

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
            if (now % NeedAPeriod == 0) guest.NeedA = Stat.Add(guest.NeedA, rng.Next(2));
            if (now % NeedBPeriod == 0) guest.NeedB = Stat.Add(guest.NeedB, rng.Next(2));
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
                bool walking = guest.State == VisitorState.Wander || guest.State == VisitorState.WalkToBin;
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

            if (!world.TryWatchEntertainer(guest, out int watchTicks)) return;
            guest.PushState(VisitorState.WatchEntertainer);
            guest.WaitUntil = world.NowTick + watchTicks;
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
