using System;

namespace TPW.Sim
{
    /// <summary>What happened when a guest looked for a bin (§2.1 roll 3). Three outcomes, not two --
    /// "there is no bin" and "there is a bin but the path request failed" lead to opposite behaviour:
    /// the first drops litter on the spot, the second just stays idle holding its rubbish.</summary>
    public enum BinSearch
    {
        /// <summary>No bin within 6 tiles. The guest litters.</summary>
        NoBinInRange,
        /// <summary>A bin was found but the pathfinder refused. Target cleared, stay Idle, keep rubbish.</summary>
        PathRefused,
        /// <summary>Walking to it.</summary>
        WalkingToBin,
    }

    /// <summary>The park, as far as an idle guest needs it.
    ///
    /// ⚠ DELIBERATELY NARROW, AND THE PATHFINDER IS NOT BEHIND IT. behaviour.md §1 says in terms that
    /// the pathfinder's internals were never traced, so nothing here pretends to path -- the host
    /// answers whether a request was ACCEPTED and owns the walking. Everything this interface exposes
    /// is something the findings actually state.</summary>
    public interface IVisitorWorld
    {
        /// <summary>The fast clock, in ticks.</summary>
        long NowTick { get; }

        /// <summary>McAi+0x10, the slow day counter the leave check measures time-in-park against.</summary>
        int SlowClockDay { get; }

        /// <summary>0x80059A9C() != 0. Its meaning is unknown; its effects are not. When set, Idle skips
        /// the needs check on roll 0 and no litter is spawned anywhere.</summary>
        bool IdleNeedsSuppressed { get; }

        /// <summary>Roll 3: find a bin within 6 tiles and ask to walk to the tile in front of it.</summary>
        BinSearch TryWalkToBin(Visitor guest);

        /// <summary>Roll 2: an entertainer within 5 tiles (Manhattan) gets hit -- it loses 5 morale and
        /// is pushed into Shocked. Returns whether one was in range. The guest's own state never changes.</summary>
        bool TryPeltEntertainer(Visitor guest);

        /// <summary>Drop a litter object at the guest's feet. Used by roll 3 (no bin, and rubbish is
        /// then zeroed) and roll 5 (misery, and rubbish is NOT touched).</summary>
        void DropLitter(Visitor guest);
    }

    /// <summary>State 0 -- Idle (0x8008D058). The guest stands around; every tick it first decides
    /// whether to go home, then rolls one of six micro-decisions.
    ///
    /// ⚠ THE LEAVE CHECK RUNS FIRST AND UNCONDITIONALLY, every tick, before any roll. A guest that has
    /// run out of money or patience leaves on the tick it notices, not when a 1-in-6 roll lets it.
    ///
    /// ⚠ ROLL ORDER IS THE DICE ORDER. The rolls are consumed in a fixed sequence -- the leave check's
    /// rand(20) first (only when the time condition is met), then the rand(6) selector, then whatever
    /// that branch needs. A caller feeding scripted numbers must supply them in that order, which is
    /// also why <see cref="IRandomSource"/> exists rather than a static.</summary>
    public static class VisitorIdle
    {
        /// <summary>Tiredness at or above this and the guest goes home.</summary>
        public const int LeaveTiredness = 99;
        /// <summary>Happiness below this and the guest goes home.</summary>
        public const int LeaveHappiness = 5;
        /// <summary>Below this and it shows the "left unhappy" bubble on the way out.</summary>
        public const int UnhappyExitHappiness = 3;
        /// <summary>Days in the park before the random departure roll starts applying.</summary>
        public const int LeaveAfterDays = 81;
        /// <summary>Rubbish at or above this and the guest looks for a bin.</summary>
        public const int SeekBinRubbish = 90;
        /// <summary>Above this and the guest is sick.</summary>
        public const int VomitNausea = 92;
        /// <summary>Below this the guest may drop litter out of misery (0x80103260).</summary>
        public const int LitterHappiness = 25;
        /// <summary>Bubble shown by a guest leaving in a foul mood.</summary>
        public const int BubbleLeftUnhappy = 0x3A;
        /// <summary>Bubble for an unmet need A/B.</summary>
        public const int BubbleNeed = 0x33;
        /// <summary>Bubble for ride desire.</summary>
        public const int BubbleRideDesire = 0x3B;

        /// <summary>Run one Idle tick and report what the guest decided.</summary>
        public static IdleAction Tick(Visitor guest, IVisitorWorld world, IRandomSource rng)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            if (ResolvesToLeave(guest, world, rng))
            {
                if (guest.Happiness < UnhappyExitHappiness) guest.Bubble = BubbleLeftUnhappy;
                guest.SetState(VisitorState.LeavingPark);
                return IdleAction.Leave;
            }

            switch (rng.Next(6))
            {
                case 0: return RollDecision(guest, world, rng);
                case 1:
                    // READ: Idle PUSHES 1, but 1 then SETS 5 and discards that stack (§2.7).
                    // The wander's purpose-1 arrival eventually sets Idle; it does not pop this push.
                    guest.PushState(VisitorState.Wander);
                    return IdleAction.Wander;
                case 2:
                    // 10 in 1000. The guest's own state does not change either way.
                    if (rng.Next(1000) < 10 && world.TryPeltEntertainer(guest)) return IdleAction.PeltEntertainer;
                    return IdleAction.Nothing;
                case 3: return RollBin(guest, world);
                case 4: return RollVomit(guest, world, rng);
                case 5:
                    // ⚠ rubbish is NOT cleared here. This is a guest dropping litter because it is
                    // miserable, which is a different act from emptying its hands for want of a bin.
                    if (guest.Happiness < LitterHappiness && rng.Next(1000) < 100 && !world.IdleNeedsSuppressed)
                    {
                        world.DropLitter(guest);
                        return IdleAction.DropLitterFromMisery;
                    }
                    return IdleAction.Nothing;
                default: return IdleAction.Nothing;
            }
        }

        /// <summary>The four ways a guest decides to go home (§2.1). Any one is enough.</summary>
        public static bool ResolvesToLeave(Visitor guest, IVisitorWorld world, IRandomSource rng)
        {
            if (guest.Tiredness >= LeaveTiredness) return true;
            if (guest.Happiness < LeaveHappiness) return true;
            if (guest.Money < GuestSpending.LeaveBelow) return true;

            // ⚠ The dice are only rolled when the time condition already holds. Rolling first would
            // consume a number the original never takes and desynchronise every later roll.
            int daysInPark = world.SlowClockDay - (int)guest.ArrivedOnDay;
            return daysInPark >= LeaveAfterDays && rng.Next(20) < 2;
        }

        static IdleAction RollDecision(Visitor guest, IVisitorWorld world, IRandomSource rng)
        {
            // Do nothing while the cooldown is still running AND the guest has nothing in mind.
            if (!world.IdleNeedsSuppressed && !guest.HasTarget
                && guest.WaitUntil + 60 + rng.Next(300) > world.NowTick)
                return IdleAction.Nothing;

            // Needs bubbles, first match wins. The 0x34 and 0x32 bubbles the findings mention are
            // unreachable dead code in the original and are deliberately not reproduced.
            if (guest.NeedA > 90 || guest.NeedB > 90) guest.Bubble = BubbleNeed;
            else if (guest.RideDesire > 90) guest.Bubble = BubbleRideDesire;

            guest.PushState(VisitorState.MajorDecision);
            return IdleAction.MakeMajorDecision;
        }

        static IdleAction RollBin(Visitor guest, IVisitorWorld world)
        {
            if (guest.Rubbish < SeekBinRubbish) return IdleAction.Nothing;

            switch (world.TryWalkToBin(guest))
            {
                case BinSearch.WalkingToBin:
                    guest.HasTarget = true;
                    guest.SetState(VisitorState.WalkToBin);
                    return IdleAction.WalkToBin;
                case BinSearch.PathRefused:
                    guest.HasTarget = false;
                    return IdleAction.Nothing;
                default:
                    // No bin in reach: empty its hands on the spot. This one DOES clear the rubbish.
                    if (!world.IdleNeedsSuppressed) world.DropLitter(guest);
                    guest.Rubbish = 0;
                    return IdleAction.DropLitterBecauseNoBin;
            }
        }

        static IdleAction RollVomit(Visitor guest, IVisitorWorld world, IRandomSource rng)
        {
            // ⚠ DO NOT FIX: §2.1 says OR, so a settled stomach can still be sick. The binary instead
            // requires nausea > 92 AND rand(4)==0 (0x8008D5D8..5F8). Keep the report's rule here;
            // findings/visitor-rest.md records the disagreement rather than silently changing it.
            if (guest.Nausea <= VomitNausea && rng.Next(4) != 0) return IdleAction.Nothing;

            guest.WaitUntil = world.NowTick + 60;
            guest.Animation = VisitorActivity.VomitAnimation;
            guest.SetState(VisitorState.Vomiting);
            return IdleAction.Vomit;
        }
    }
}
