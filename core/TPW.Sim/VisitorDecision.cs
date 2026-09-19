using System;
using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>One thing a guest could decide to head for.
    ///
    /// ⚠ THE SLOTS ARE NAMED AFTER THEIR SLOT NUMBERS ON PURPOSE. behaviour.md §2.2b says in terms that
    /// which ride attribute each slot holds -- excitement, intensity, price -- is NOT established. Naming
    /// <see cref="Slot53"/> "intensity" would make a guess unfalsifiable the moment a second reader
    /// believed it. They get real names when someone reads the ride vtable, not before.</summary>
    public readonly struct AttractionCandidate
    {
        public AttractionCandidate(int id, int typeIndex, int slot53, int slot54, int slot55, int slot56,
                                   bool openToGuests, int distanceTiles, bool centreTileValid,
                                   int productKind = 0)
        {
            Id = id; TypeIndex = typeIndex;
            Slot53 = slot53; Slot54 = slot54; Slot55 = slot55; Slot56 = slot56;
            OpenToGuests = openToGuests; DistanceTiles = distanceTiles; CentreTileValid = centreTileValid;
            ProductKind = productKind;
        }

        /// <summary>Slot 15, the attraction's id. The repeat history holds these.</summary>
        public int Id { get; }
        /// <summary>The slot-16 type, 1..7. Type 2 is a shop.</summary>
        public int TypeIndex { get; }
        /// <summary>Matched against the guest's visitor-type preference. Zero disables the match term.</summary>
        public int Slot53 { get; }
        /// <summary>Gates BOTH desire terms, and on a shop a zero here scores -1 outright.</summary>
        public int Slot54 { get; }
        /// <summary>Matched against the guest's NeedB on the 11x11 surface.</summary>
        public int Slot55 { get; }
        /// <summary>Matched against the guest's NeedA on the 11x11 surface.</summary>
        public int Slot56 { get; }
        /// <summary>Slot 86. GUESS-high "open to guests" -- only these are considered at all.</summary>
        public bool OpenToGuests { get; }
        /// <summary>Manhattan tiles from the guest.</summary>
        public int DistanceTiles { get; }
        /// <summary>False when the centre tile is off-map or otherwise fails 0x800508C8.</summary>
        public bool CentreTileValid { get; }
        /// <summary>Type-4 only: the product kind from 0x800B6E60, which selects the bonus threshold.</summary>
        public int ProductKind { get; }
    }

    /// <summary>The ride score, 0x8008C818 (formula READ, slot meanings GUESS).</summary>
    public static class RideScore
    {
        /// <summary>Returned when the attraction cannot be chosen at all.</summary>
        public const int Rejected = -1;

        /// <summary>Score one candidate for one guest.</summary>
        /// <param name="history">The guest's last four attraction ids, most recent first. A match
        /// divides the score by 5, 4, 3 or 2 by position -- so the thing just ridden is punished hardest
        /// and the penalty fades, rather than a flat "don't repeat".</param>
        public static int Score(Visitor guest, in AttractionCandidate a, int mapWidth, int mapHeight,
                                IReadOnlyList<int> history = null)
        {
            if (!a.CentreTileValid) return Rejected;
            // A shop with nothing to sell is not a candidate, which is a different thing from scoring
            // badly: it cannot be picked at any distance or need.
            if (a.TypeIndex == 2 && a.Slot54 == 0) return Rejected;

            int span = mapWidth + mapHeight;
            int closeness = span > 0 ? 100 - Clamp0To100(a.DistanceTiles * 100 / span) : 100;

            int pref = Preference(guest);
            int match = (50 - Math.Min(Math.Abs(pref - a.Slot53), 50)) * 2;
            int hasK = a.Slot53 != 0 ? 1 : 0;

            int s7 = VisitorTables.Match(guest.NeedB, a.Slot55);
            int s5 = VisitorTables.Match(guest.NeedA, a.Slot56);
            int s6 = a.Slot54 != 0 ? VisitorTables.Desire(guest.RideDesire) : 0;

            // ⚠ THIS ONE READS ODDLY AND IS WHAT THE REPORT SAYS. The second desire term is indexed by
            // NAUSEA, on the same curve, so a queasy guest scores rides HIGHER. It is weighted 2 against
            // the ride-desire term's 4, so it softens rather than dominates -- but if a live reading ever
            // shows sick guests avoiding rides, this term is the first thing to re-read.
            int s0 = a.Slot54 != 0 ? VisitorTables.Desire(guest.Nausea) : 0;

            int s4 = 0, w4 = 0;
            if (a.TypeIndex == 4)
            {
                int thr = a.ProductKind == 6 ? VisitorTables.ShopBonusThresholdKind6
                        : a.ProductKind is 2 or 3 ? VisitorTables.ShopBonusThresholdKind23
                        : -1;
                if (thr >= 0 && guest.Happiness > thr)
                {
                    s4 = (guest.Happiness - thr) * 100 / (100 - thr);
                    w4 = 3;
                }
            }

            int total = closeness + hasK * match + 3 * s7 + 3 * s5 + 4 * s6 + 2 * s0 + w4 * s4;
            int score = total / (13 + hasK + w4);

            // Repeat penalties, applied in order: 5, 4, 3, 2.
            if (history != null)
                for (int i = 0; i < history.Count && i < 4; i++)
                    if (history[i] == a.Id) score /= 5 - i;

            return score;
        }

        /// <summary>The guest's ride preference, from its visitor type (0x800F79E8).</summary>
        public static int Preference(Visitor guest)
        {
            int t = guest.VisitorType;
            return t >= 0 && t < VisitorTables.TypePreference.Length ? VisitorTables.TypePreference[t] : 0;
        }

        static int Clamp0To100(int v) => v < 0 ? 0 : v > 100 ? 100 : v;
    }

    /// <summary>What state 6 decided.</summary>
    public enum DecisionOutcome
    {
        /// <summary>Not this guest's tick -- the handler runs once every 8 ticks, staggered.</summary>
        NotMyTick,
        /// <summary>Nothing in the park is worth going to. Happiness and boredom took the hit.</summary>
        NothingWorthDoing,
        /// <summary>Picked something but the pathfinder refused it.</summary>
        PathRefused,
        /// <summary>On the way.</summary>
        HeadingThere,
    }

    /// <summary>State 6 -- make major decision (0x8008E240).
    ///
    /// ⚠ IT ONLY RUNS ONCE EVERY 8 TICKS, staggered per guest by `now &amp; 7 == stagger &amp; 7`. The
    /// stagger is why a park full of guests does not re-plan in lockstep, and running this every tick
    /// would multiply the pathfinder's load eightfold for no behavioural gain.
    ///
    /// ⭐ FAILURE COSTS 360 TICKS EITHER WAY. Both "nothing worth doing" and "the path was refused" add
    /// 360 to the guest's cooldown and pop back to Idle, so a guest in a park it cannot navigate retries
    /// about every 15 seconds rather than every tick -- and paths.md §2 records that an unreachable shop
    /// is chosen anyway, so this is the loop that produces 360 wandering guests and no sales.</summary>
    public static class VisitorDecision
    {
        /// <summary>Added to the cooldown whenever a decision comes to nothing.</summary>
        public const int FailureCooldown = 360;
        /// <summary>Below this the guest is disappointed by its own choice: -5 happiness and bubble 0x39.</summary>
        public const int PoorChoiceScore = 8;
        /// <summary>Above this, a chosen non-shop goes into the repeat history.</summary>
        public const int HistoryDesire = 98;

        public const int BubblePoorChoice = 0x39;
        public const int BubbleNothingToDo = 0x37;

        public static DecisionOutcome Tick(Visitor guest, IDecisionWorld world, IRandomSource rng)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            if ((world.NowTick & 7) != (guest.DecisionStagger & 7)) return DecisionOutcome.NotMyTick;

            if (!ChooseTarget(guest, world, rng))
            {
                Fail(guest, world);
                return DecisionOutcome.NothingWorthDoing;
            }

            if (!world.TryPathToTarget(guest))
            {
                Fail(guest, world);
                return DecisionOutcome.PathRefused;
            }

            guest.WaitUntil = world.NowTick;
            guest.HasTarget = true;
            guest.PushState(VisitorState.WalkToBin);   // state 11: walking to the chosen target
            return DecisionOutcome.HeadingThere;
        }

        static void Fail(Visitor guest, IDecisionWorld world)
        {
            guest.WaitUntil += FailureCooldown;
            guest.HasTarget = false;
            guest.PopState();
        }

        /// <summary>0x8008CDC8. True when the guest has something to head for.</summary>
        public static bool ChooseTarget(Visitor guest, IDecisionWorld world, IRandomSource rng)
        {
            if (guest.HasTarget) return true;

            var candidates = world.OpenAttractions;
            int bestScore = int.MinValue, bestIndex = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!candidates[i].OpenToGuests) continue;
                int s = RideScore.Score(guest, candidates[i], world.MapWidth, world.MapHeight, guest.RideHistory);
                if (s == RideScore.Rejected) continue;
                // Ties are broken by a coin flip, so two identical rides share the crowd instead of one
                // of them taking every guest by iteration order.
                if (s > bestScore || (s == bestScore && rng.Next(2) == 0)) { bestScore = s; bestIndex = i; }
            }

            if (bestIndex < 0)
            {
                guest.Happiness = Stat.Sub(guest.Happiness, 10);
                guest.Boredom = Stat.Add(guest.Boredom, 5);
                guest.Bubble = BubbleNothingToDo;
                return false;
            }

            var chosen = candidates[bestIndex];
            guest.HasTarget = true;
            guest.ChosenId = chosen.Id;

            if (guest.RideDesire > HistoryDesire && chosen.TypeIndex != 2) guest.RememberRide(chosen.Id);
            if (bestScore < PoorChoiceScore)
            {
                guest.Happiness = Stat.Sub(guest.Happiness, 5);
                guest.Bubble = BubblePoorChoice;
            }
            return true;
        }
    }

    /// <summary>What state 6 needs of the park, on top of <see cref="IVisitorWorld"/>.</summary>
    public interface IDecisionWorld
    {
        long NowTick { get; }
        int MapWidth { get; }
        int MapHeight { get; }
        /// <summary>Every attraction the iterator would walk. Scoring skips the ones not open.</summary>
        IReadOnlyList<AttractionCandidate> OpenAttractions { get; }
        /// <summary>Ask for a path to the guest's chosen target. The pathfinder itself is untraced --
        /// this only answers whether the request was accepted.</summary>
        bool TryPathToTarget(Visitor guest);
    }
}
