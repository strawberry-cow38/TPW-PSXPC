using System;

namespace TPW.Sim
{
    /// <summary>Mechanic states and purposes (behaviour.md §3.5).</summary>
    public static class MechanicStates
    {
        public const StaffState Repairing = (StaffState)14;
        public const StaffState ClosingRide = (StaffState)16;
        public const StaffState OpeningRide = (StaffState)17;
        public const StaffState ClosingForService = (StaffState)52;
        public const StaffState Servicing = (StaffState)54;
        public const StaffState GoToBrokenRide = (StaffState)56;
        public const StaffState GoToServiceRide = (StaffState)57;
        public const StaffState LeavingRide = (StaffState)58;

        /// <summary>Walking to a ride that has broken down.</summary>
        public const StaffPurpose ToRepair = (StaffPurpose)6;
        /// <summary>Walking to a ride that is due a service.</summary>
        public const StaffPurpose ToService = (StaffPurpose)20;
        /// <summary>Walking away from a finished job.</summary>
        public const StaffPurpose LeaveRide = (StaffPurpose)22;
    }

    /// <summary>What a mechanic needs of the park.</summary>
    public interface IMechanicWorld : IStaffWorld
    {
        /// <summary>Claim a broken ride (unclaimed, or already mine) and path to it. False when there is
        /// none free or the request was refused.</summary>
        bool TryClaimBrokenRide(StaffMember staff);
        /// <summary>The same for a ride whose service is due.</summary>
        bool TryClaimServiceRide(StaffMember staff);
        /// <summary>The ride has emptied out and can be worked on (0x8009C1E8).</summary>
        bool RideIsClear(StaffMember staff);
        /// <summary>Keep unloading it (0x8009EEE4) - called every tick until it is clear.</summary>
        void UnloadRide(StaffMember staff);
        /// <summary>Keep loading it back up (0x8009EFA8) while reopening.</summary>
        void ReloadRide(StaffMember staff);
        /// <summary>Mark the ride as under repair (slot 57 with 6).</summary>
        void MarkRideUnderRepair(StaffMember staff);
        /// <summary>Mark the ride open again (slot 57 with 7) and release the claim.</summary>
        void MarkRideOpenAndRelease(StaffMember staff);
        /// <summary>Book the service against the ride (0x8009C56C) and drop it off the due list.</summary>
        void CompleteService(StaffMember staff);
        /// <summary>Release a claim without finishing, when a walk fails or the job is cancelled.</summary>
        void ReleaseClaim(StaffMember staff);
        /// <summary>Path to the ride's leave point (slot 26).</summary>
        bool TryPathToLeavePoint(StaffMember staff);
    }

    /// <summary>The mechanic (Update 0x8009710C, behaviour.md §3.5).
    ///
    /// ⭐⭐ THIS CLASS IS TIRED BY IDLENESS AND RESTED BY WORK, WHICH IS BACKWARDS AND IS WHAT THE CODE
    /// SAYS. An idle tick costs **+6 tiredness** (and +1 morale); walking to a broken ride costs
    /// **-2 tiredness and -2 morale per tick**. The report flags the second as "odd but there" and the
    /// two only make sense as a pair: a mechanic with nothing to do wears out fast, and a mechanic on a
    /// job physically recovers while resenting it. Every other staff class is the other way round.
    ///
    /// ⚠ DO NOT "FIX" EITHER SIGN. +6 per idle tick is steep enough to look like a transcription error -
    /// it would exhaust a mechanic in seventeen ticks of standing about - and the negative walk cost
    /// looks like a missing minus. They are both READ, they are mutually consistent, and together they
    /// are presumably why the game wants mechanics kept busy. If a live reading contradicts them, these
    /// two constants are the place to look, and they are named so they can be found.</summary>
    public static class Mechanic
    {
        /// <summary>Ticks to repair or service, by skill, from 0x800E4574 column 0.
        ///
        /// ⚠ THE TABLE IS PAIRS OF s16, NOT A FLAT ARRAY. behaviour.md quotes it as
        /// `0x800E4574[skill] = 240/180/120/60/60` and those are right, but they are every OTHER
        /// halfword: the real layout is five rows of two, and the second column is
        /// <see cref="UnknownSecondColumn"/>. Reading it as a flat s32 array - the obvious thing, since
        /// the other staff tables are s32 - gives 590064, 786612, 917624, which is nonsense that would
        /// have been caught, and reading it as a flat s16 array gives 240, 9, 180, 12, which would NOT
        /// have been: the first value is right and the second is plausible.</summary>
        public static readonly int[] RepairTicks = { 240, 180, 120, 60, 60 };

        /// <summary>Column 1 of the same table: 9, 12, 14, 16, 18.
        ///
        /// ⚠ NOT MENTIONED IN THE FINDINGS AND NOT IDENTIFIED HERE. It rises as the repair time falls,
        /// so it improves with skill, but nothing in §3.5 reads it and I have not traced what does.
        /// Recorded rather than dropped, because a column nobody knows about is how a table silently
        /// gets re-derived wrong later.</summary>
        public static readonly int[] UnknownSecondColumn = { 9, 12, 14, 16, 18 };

        /// <summary>Tiredness gained per idle tick. See the class note before changing this.</summary>
        public const int IdleTiredness = 6;
        /// <summary>Morale gained per idle tick.</summary>
        public const int IdleMorale = 1;
        /// <summary>Tiredness and morale per tick while walking to a repair. Both negative.</summary>
        public const int RepairWalkTiredness = -2;
        /// <summary>See <see cref="RepairWalkTiredness"/>.</summary>
        public const int RepairWalkMorale = -2;
        /// <summary>Morale for getting a ride open again.</summary>
        public const int ReopenMorale = 10;

        /// <summary>Idle (0x80096D60).</summary>
        public static void Idle(StaffMember staff, IMechanicWorld world, IRandomSource rng)
        {
            if (staff == null) throw new ArgumentNullException(nameof(staff));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            if (world.IsTypeOnStrike(staff.Kind)) return;

            staff.Tiredness = Stat.Add(staff.Tiredness, IdleTiredness);
            staff.Morale = Stat.Add(staff.Morale, IdleMorale);

            // ⚠ BOTH ORDERS TRY BOTH JOBS. Unlike the handyman, whose litter branch does not fall back,
            // the mechanic always considers the other kind if the first finds nothing - the coin flip
            // only decides which is looked at first, so a broken ride is never ignored.
            bool brokenFirst = rng.Next(2) == 0;
            if (brokenFirst)
            {
                if (TakeRepair(staff, world)) return;
                if (TakeService(staff, world)) return;
            }
            else
            {
                if (TakeService(staff, world)) return;
                if (TakeRepair(staff, world)) return;
            }
            staff.SetState(StaffState.Patrolling);
        }

        static bool TakeRepair(StaffMember staff, IMechanicWorld world)
        {
            if (!world.TryClaimBrokenRide(staff)) return false;
            staff.HasTarget = true;
            staff.SetState(MechanicStates.GoToBrokenRide);
            return true;
        }

        static bool TakeService(StaffMember staff, IMechanicWorld world)
        {
            if (!world.TryClaimServiceRide(staff)) return false;
            staff.HasTarget = true;
            staff.SetState(MechanicStates.GoToServiceRide);
            return true;
        }

        /// <summary>States 56 and 57: set off for the ride.</summary>
        public static void SetOff(StaffMember staff, bool forRepair)
        {
            staff.Purpose = forRepair ? MechanicStates.ToRepair : MechanicStates.ToService;
            staff.PushState(StaffState.Walking);
        }

        /// <summary>Per walking tick. See the class note: walking to a REPAIR is restful and demoralising;
        /// walking to a service is neither.</summary>
        public static void WalkTick(StaffMember staff)
        {
            if (staff.Purpose != MechanicStates.ToRepair) return;
            staff.Tiredness = Stat.Add(staff.Tiredness, RepairWalkTiredness);
            staff.Morale = Stat.Add(staff.Morale, RepairWalkMorale);
        }

        /// <summary>Arrival by purpose.</summary>
        public static void Arrive(StaffMember staff)
        {
            if (staff.Purpose == MechanicStates.ToRepair) staff.SetState(MechanicStates.ClosingRide);
            else if (staff.Purpose == MechanicStates.ToService) staff.SetState(MechanicStates.ClosingForService);
            else if (staff.Purpose == MechanicStates.LeaveRide)
            {
                staff.HasTarget = false;
                staff.SetState(StaffState.Idle);
            }
        }

        /// <summary>States 16 and 52 -- wait for the ride to empty, then start the clock.
        ///
        /// ⚠ THE RIDE MUST EMPTY FIRST and the mechanic keeps unloading it every tick until it does, so
        /// a busy ride delays its own repair. The timer does not start until the last rider is off.</summary>
        public static bool CloseRide(StaffMember staff, IMechanicWorld world, bool forService)
        {
            if (!world.RideIsClear(staff))
            {
                world.UnloadRide(staff);
                return false;
            }
            world.MarkRideUnderRepair(staff);
            staff.BusyUntil = world.NowTick + Handyman.BySkill(RepairTicks, staff.Skill);
            staff.SetState(forService ? MechanicStates.Servicing : MechanicStates.Repairing);
            return true;
        }

        /// <summary>States 14 and 54 -- the work itself.</summary>
        public static bool Work(StaffMember staff, IMechanicWorld world, bool forService)
        {
            if (world.NowTick <= staff.BusyUntil) return false;
            if (forService) world.CompleteService(staff);
            staff.SetState(MechanicStates.OpeningRide);
            return true;
        }

        /// <summary>State 17 -- put the ride back in service.
        ///
        /// ⭐ THE +10 MORALE IS PAID HERE, at reopening, not at finishing the repair. A mechanic pulled
        /// off a job before the ride reopens never gets paid for it.</summary>
        public static bool OpenRide(StaffMember staff, IMechanicWorld world)
        {
            if (!world.RideIsClear(staff))
            {
                world.ReloadRide(staff);
                return false;
            }
            world.MarkRideOpenAndRelease(staff);
            staff.Morale = Stat.Add(staff.Morale, ReopenMorale);
            staff.SetState(MechanicStates.LeavingRide);
            return true;
        }

        /// <summary>State 58 -- walk off the ride.</summary>
        public static void LeaveRide(StaffMember staff, IMechanicWorld world)
        {
            if (!world.TryPathToLeavePoint(staff))
            {
                staff.HasTarget = false;
                staff.SetState(StaffState.Idle);
                return;
            }
            staff.Purpose = MechanicStates.LeaveRide;
            staff.PushState(StaffState.Walking);
        }

        /// <summary>Messages (0x800964B0). A failed walk to a job releases the claim, or that ride stays
        /// reserved by a mechanic who never arrives and no one else is sent to it.</summary>
        public static void OnPathMessage(StaffMember staff, IMechanicWorld world, bool pathFound)
        {
            if (pathFound) { staff.SetState(StaffState.PathReady); return; }

            if (staff.Purpose == MechanicStates.ToRepair || staff.Purpose == MechanicStates.ToService)
            {
                world.ReleaseClaim(staff);
                staff.HasTarget = false;
                staff.SetState(StaffState.Idle);
                return;
            }
            if (staff.Purpose == MechanicStates.LeaveRide) { staff.SetState(StaffState.Idle); return; }
            StaffBase.OnPathMessage(staff, pathFound);
        }
    }
}
