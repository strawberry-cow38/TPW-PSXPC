using System;

namespace TPW.Sim
{
    /// <summary>P+0x2C, the reason a guest is walking somewhere, consumed when it gets there
    /// (table 0x800E3A94, 23 entries). Numbered as the original numbers them.
    ///
    /// ⭐ ARRIVAL IS SELF-CANCELLING. Every arm except <see cref="LeavePark"/>, <see cref="Turnstile14"/>
    /// and <see cref="Turnstile15"/> ends by setting purpose to <see cref="Spent"/>, so a second arrival
    /// tick does nothing. That is the mechanism that stops a guest buying twice or joining a queue twice
    /// if it is ticked again before its new state takes effect -- not a guard somewhere else.</summary>
    public enum Purpose
    {
        /// <summary>At a ride or shop -- the ordinary case.</summary>
        AtAttraction = 0,
        /// <summary>Done; go back to standing around.</summary>
        Finished = 1,
        /// <summary>Consumed. Arriving again is a no-op.</summary>
        Spent = 2,
        /// <summary>Walking to a queue slot.</summary>
        QueueWalk = 3,
        /// <summary>Give up and stand around.</summary>
        Abandon = 4,
        /// <summary>Walk out of the park and despawn.</summary>
        LeavePark = 9,
        /// <summary>Like <see cref="QueueWalk"/> but shuffling forward rather than joining.</summary>
        QueueShuffle = 10,
        /// <summary>At my turnstile lane slot, having pathed to it (state 42). §2.6.</summary>
        Turnstile11 = 11,
        /// <summary>At my turnstile lane slot, having shuffled to it (state 43). §2.6.</summary>
        Turnstile12 = 12,
        /// <summary>Arrived somewhere pleasant: back to Idle, sometimes with a sound. Also state 45's
        /// arrival, the first path tile inside the gate.</summary>
        Pleased = 13,
        /// <summary>At the exit point on the way out (state 38). §2.6.</summary>
        Turnstile14 = 14,
        /// <summary>At the spawn point on the way in (state 36). §2.6.</summary>
        Turnstile15 = 15,
        /// <summary>On the gate line (state 47): pick a lane, or walk out. §2.6.</summary>
        Turnstile16 = 16,
        /// <summary>At a bin.</summary>
        AtBin = 19,
        /// <summary>Clear the target and stand around.</summary>
        ClearTarget = 22,
    }

    /// <summary>What the arrival did.</summary>
    public enum Arrival
    {
        /// <summary>Still walking: a waypoint remains.</summary>
        StillWalking,
        /// <summary>Nothing happens for this purpose -- one of the documented no-op arms.</summary>
        NoOp,
        BackToIdle,
        UsingAttraction,
        JoinedQueue,
        WaitingInQueue,
        ShufflingForward,
        EmptiedRubbish,
        LeftThePark,
        /// <summary>Purposes 11 and 12: standing exactly on my lane slot, now in 44.</summary>
        AtLaneFront,
        /// <summary>Purposes 11 and 16: not on the slot (11) or lane just chosen (16); now in 42.</summary>
        WalkingToLaneSlot,
        /// <summary>Purpose 12: not on the slot; now in 43.</summary>
        ShufflingInLane,
        /// <summary>Purposes 14 and 15: at the gate, now in 46 and counted as waiting.</summary>
        AtGate,
        /// <summary>Purpose 16 with V+0x28 set: crossed on the way out, now in 48.</summary>
        LeavingThroughGate,
    }

    /// <summary>What arrival needs of the park. The six turnstile arms (11-16) ask for the lane lists
    /// and the gate counters, which is <see cref="ITurnstileWorld"/>; a host implements that once for
    /// this and for <see cref="IEntranceWorld"/>.</summary>
    public interface IArrivalWorld : ITurnstileWorld
    {
        /// <summary>P+0x28 != -1: there is still a waypoint to walk to.</summary>
        bool HasWaypoint(Visitor guest);
        /// <summary>The Person base step, 0x8009322C -- walk on toward the next waypoint.</summary>
        void StepToNextWaypoint(Visitor guest);
        /// <summary>The slot-16 type of the guest's current target, or 0 when it has none.</summary>
        int TargetType(Visitor guest);
        /// <summary>The target's slot 54 is non-zero (a shop with stock).</summary>
        bool TargetHasStock(Visitor guest);
        /// <summary>0x8009F614 found an entrance point on the target.</summary>
        bool TargetHasEntrance(Visitor guest);
        /// <summary>0x8008DA14: try to join the ride's queue.</summary>
        bool TryJoinQueue(Visitor guest);
        /// <summary>0x8009D57C: find my slot in the queue. <paramref name="atSlot"/> is true when the
        /// guest is already standing on it.</summary>
        bool TryQueueSlot(Visitor guest, out bool atSlot);
        /// <summary>Purpose 9: take the guest out of the park entirely.</summary>
        void RemoveFromPark(Visitor guest);
    }

    /// <summary>State 2 -- walk to destination, then act on arrival (vtable slot 35, 0x8008DB34).
    ///
    /// ⚠ THE WALK AND THE ARRIVAL ARE THE SAME HANDLER. While a waypoint remains this only steps and
    /// tires the guest; the 23-entry purpose table fires on the tick the waypoints run out. Splitting
    /// them into two states would need a transition the original does not have.</summary>
    public static class VisitorArrival
    {
        /// <summary>Chance per walking tick of gaining a point of tiredness: rand(10) == 0.</summary>
        public const int TirednessChanceIn = 10;
        /// <summary>A shop visit occupies the guest for this long.</summary>
        public const int ShopDwellTicks = 120;
        /// <summary>Sideshow and game stalls add rand(300) on top.</summary>
        public const int StallExtraDwellMax = 300;
        /// <summary>Chance of the arrival sound on purpose 13: rand(4) == 0.</summary>
        public const int PleasedSoundChanceIn = 4;

        public static Arrival Tick(Visitor guest, IArrivalWorld world, IRandomSource rng)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            if (world.HasWaypoint(guest))
            {
                // ⚠ TIREDNESS IS PER WALKING TICK AND PROBABILISTIC, not per tile and not per second.
                // A guest sent the long way round really is more tired when it arrives.
                if (rng.Next(TirednessChanceIn) == 0) guest.Tiredness = Stat.Add(guest.Tiredness, 1);
                world.StepToNextWaypoint(guest);
                return Arrival.StillWalking;
            }

            var result = Act(guest, world, rng);

            // Every arm but these three spends the purpose, so arriving again does nothing.
            // ⚠ §2.3 SAYS 14 AND 15 KEEP THEIRS, AND THE BINARY DISAGREES: both arms end with
            // `j 0x8008E1EC` (0x8008E0D8, 0x8008E11C), and 0x8008E1EC..0x8008E1F4 is the shared
            // `purpose := 2`; only arm 9 (0x8008DF3C) jumps past it to the epilogue. Nothing reads the
            // purpose in 46, and 47 overwrites it with 16, so no behaviour hangs on it. §2.3's rule is
            // kept here as written and the reading is recorded (2026-09-20, entrance port).
            if (guest.Purpose != Purpose.LeavePark &&
                guest.Purpose != Purpose.Turnstile14 &&
                guest.Purpose != Purpose.Turnstile15)
                guest.Purpose = Purpose.Spent;

            return result;
        }

        static Arrival Act(Visitor guest, IArrivalWorld world, IRandomSource rng) => guest.Purpose switch
        {
            Purpose.AtAttraction => AtAttraction(guest, world, rng),
            Purpose.Finished => AnimateAndIdle(guest),
            Purpose.QueueWalk => Queue(guest, world, joinIfNotAtSlot: false),
            Purpose.QueueShuffle => Queue(guest, world, joinIfNotAtSlot: true),
            Purpose.Abandon => AbandonTarget(guest),
            Purpose.ClearTarget => ClearAndIdle(guest),
            Purpose.AtBin => AtBin(guest),
            Purpose.Pleased => Pleased(guest, rng),
            Purpose.LeavePark => Leave(guest, world),
            // The turnstile arms live with the rest of the entrance (behaviour.md §2.6).
            Purpose.Turnstile11 => VisitorEntrance.ArriveAtLaneSlot(guest, world, shuffling: false),
            Purpose.Turnstile12 => VisitorEntrance.ArriveAtLaneSlot(guest, world, shuffling: true),
            Purpose.Turnstile14 => VisitorEntrance.ArriveAtExitPoint(guest, world),
            Purpose.Turnstile15 => VisitorEntrance.ArriveAtSpawnPoint(guest, world),
            Purpose.Turnstile16 => VisitorEntrance.ArriveAtGate(guest, world, rng),
            // Purposes 2, 5-8, 17, 18, 20, 21 and anything past 22 do nothing at all. The report lists
            // them explicitly as no-ops rather than leaving them undefined, so this is the documented
            // behaviour and not a gap.
            _ => Arrival.NoOp,
        };

        static Arrival AtAttraction(Visitor guest, IArrivalWorld world, IRandomSource rng)
        {
            int type = world.TargetType(guest);
            if (type == 0) return Idle(guest);

            // ⚠ THREE DIFFERENT ARRIVALS BY TYPE, and the dwell times differ. A shop is a flat 120
            // ticks; a stall is 120 plus up to 300 more, so stalls hold a guest far longer and for a
            // variable time. Everything else is a ride and goes to the queue instead.
            if (type == 2)
            {
                guest.Flag1 = false;   // READ 0x8008DC04: bit 0x01 cleared on reaching a type-2 building
                guest.WaitUntil = world.NowTick + ShopDwellTicks;
                guest.SetState(VisitorState.UsingAttraction);
                return Arrival.UsingAttraction;
            }
            if (type == 4 || type == 5)
            {
                guest.Flag1 = false;   // READ 0x8008DCC8: cleared here too, before the dwell roll
                guest.WaitUntil = world.NowTick + ShopDwellTicks + rng.Next(StallExtraDwellMax);
                guest.SetState(VisitorState.UsingAttraction);
                return Arrival.UsingAttraction;
            }

            guest.WaitUntil = world.NowTick;
            if (!world.TargetHasEntrance(guest))
            {
                guest.HasTarget = false;
                return Idle(guest);
            }
            if (!world.TryJoinQueue(guest)) return Idle(guest);

            guest.SetState(VisitorState.JoiningQueue);
            return Arrival.JoinedQueue;
        }

        static Arrival Queue(Visitor guest, IArrivalWorld world, bool joinIfNotAtSlot)
        {
            if (!world.TryQueueSlot(guest, out bool atSlot))
            {
                guest.HasTarget = false;
                return Idle(guest);
            }
            // The in-queue bit is set here as well as in state 41 (READ 0x8008DDAC / 0x8008DE1C), so a
            // guest that reached its slot counts as queued for the can-join test even if 41 was skipped.
            guest.InQueue = true;
            if (atSlot)
            {
                guest.Animation = VisitorQueue.AnimationIdle;
                guest.SetState(VisitorState.WaitingInQueue);
                return Arrival.WaitingInQueue;
            }
            // ⚠ THE ONLY DIFFERENCE BETWEEN PURPOSES 3 AND 10 is what happens when the guest is NOT yet
            // on its slot: 3 shuffles forward, 10 re-joins. Same lookup, same failure, one branch apart.
            guest.SetState(joinIfNotAtSlot ? VisitorState.JoiningQueue : VisitorState.ShuffleForward);
            return joinIfNotAtSlot ? Arrival.JoinedQueue : Arrival.ShufflingForward;
        }

        static Arrival AtBin(Visitor guest)
        {
            guest.Rubbish = 0;
            Idle(guest);
            // Reports the emptying rather than the state change: going back to Idle is what almost every
            // arm does, and the caller needs to know THIS one is where the rubbish went.
            return Arrival.EmptiedRubbish;
        }

        static Arrival Pleased(Visitor guest, IRandomSource rng)
        {
            // ⚠ TWO DICE, NOT ONE: rand(4) == 0 fires the greeting, and THEN rand(2) picks which of the
            // two sounds (0x8008E02C, then 0x8008E080 with a0 = 2: 0 -> 0x14, else 0x16). §2.3 gives only
            // the 25%. The second roll is inside the branch, so a missed greeting costs one die and a
            // greeting costs two. Both happen whether or not audio is wired. (Set 0 comes first, 0x8008E024.)
            Idle(guest);
            if (rng.Next(PleasedSoundChanceIn) == 0) rng.Next(2);
            return Arrival.BackToIdle;
        }

        static Arrival Leave(Visitor guest, IArrivalWorld world)
        {
            guest.Bubble = 0;
            world.RemoveFromPark(guest);
            return Arrival.LeftThePark;
        }

        static Arrival ClearAndIdle(Visitor guest)
        {
            guest.HasTarget = false;
            return Idle(guest);
        }

        // Purpose 4 (back from a ride's entrance) resets the animation as well as the target; purpose
        // 22 (back from a leave point) does not touch the animation. Two arms, not one.
        static Arrival AbandonTarget(Visitor guest)
        {
            guest.HasTarget = false;
            guest.Animation = VisitorQueue.AnimationIdle;
            return Idle(guest);
        }

        static Arrival AnimateAndIdle(Visitor guest)
        {
            guest.Animation = VisitorQueue.AnimationIdle;
            return Idle(guest);
        }

        static Arrival Idle(Visitor guest)
        {
            guest.SetState(VisitorState.Idle);
            return Arrival.BackToIdle;
        }
    }
}
