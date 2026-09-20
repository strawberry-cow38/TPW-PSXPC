using System;

namespace TPW.Sim
{
    /// <summary>Mechanic states and purposes (behaviour.md §3.5, and its 2026-09-20 correction).</summary>
    public static class MechanicStates
    {
        public const StaffState Repairing = (StaffState)14;
        public const StaffState ClosingRide = (StaffState)16;
        public const StaffState OpeningRide = (StaffState)17;
        /// <summary>State 52: closing a ride for an UPGRADE. behaviour.md called this "closing for
        /// service"; see <see cref="Upgrading"/> for why that name is retired.</summary>
        public const StaffState ClosingForUpgrade = (StaffState)52;
        /// <summary>State 54: the upgrade job.
        ///
        /// ⭐ "SERVICE DUE" WAS THE UPGRADE QUEUE ALL ALONG. READ: the second job source in Idle,
        /// 0x8005BE44, is `0x8005BAF8(0x801099EC, [0x80102D48], me)` -- nearest unclaimed entry of the
        /// 15-pointer queue that the ride panel's upgrade request 0x8005BE14 appends to -- and this state
        /// ends (0x80096AA4) by calling 0x8009C56C(ride, 0), the PAID level-up that
        /// <see cref="RidePanel.CompleteUpgrade"/> ports, then 0x8005BE70 to pull the ride off that queue.
        /// There is no maintenance job in this machine; a host that wired "service" to a wear reset or
        /// a reliability top-up would have invented a mechanic the game does not have.</summary>
        public const StaffState Upgrading = (StaffState)54;
        public const StaffState GoToBrokenRide = (StaffState)56;
        public const StaffState GoToUpgradeRide = (StaffState)57;
        public const StaffState LeavingRide = (StaffState)58;

        /// <summary>Walking to a ride that has broken down.</summary>
        public const StaffPurpose ToRepair = (StaffPurpose)6;
        /// <summary>Walking to a ride the player has queued for an upgrade.</summary>
        public const StaffPurpose ToUpgrade = (StaffPurpose)20;
        /// <summary>Walking away from a finished job.</summary>
        public const StaffPurpose LeaveRide = (StaffPurpose)22;
    }

    /// <summary>What a mechanic needs of the park. Every "claimed ride" below is the one this mechanic's
    /// target word (M+0x28) points at; the world remembers that association from the claim until the
    /// sim clears <see cref="StaffMember.HasTarget"/>, and NOT merely until <see cref="ReleaseClaim"/>,
    /// which only clears the ride's side of it.</summary>
    public interface IMechanicWorld : IStaffWorld
    {
        /// <summary>Idle, repair-first (0x8005BCB0 then 0x80096C58 with flag 1). Select: the nearest ride
        /// by Manhattan tiles among those whose slot 20 is true (status byte A+0x6E is 4 or 5,
        /// <see cref="AttractionStatus.AboutToBreakDown"/>/<see cref="AttractionStatus.BrokenDown"/>)
        /// and whose claim word A+0x54 is empty OR ALREADY THIS MECHANIC. Claim: slot 20 again, then
        /// slot 84 (A+0x68, remaining lifetime) must be nonzero -- `!= 0`, not `> 0` -- then write the
        /// claim word and this mechanic's target. False when nothing was selected or the claim refused.
        ///
        /// ⚠ ONLY THE NEAREST IS EVER TRIED. If the nearest broken ride is condemned (lifetime 0) the
        /// claim refuses and the whole repair branch reports nothing; the next-nearest broken ride is
        /// not considered. rides.md §6.3 has the consequence: a condemned ride is never repaired.</summary>
        bool TryClaimBrokenRide(StaffMember staff);
        /// <summary>Idle, either order (0x8005BE44 then 0x80096C58 with flag 0). Select: the nearest
        /// UNCLAIMED ride in the upgrade queue 0x801099EC -- unlike the repair select, one this mechanic
        /// already holds is skipped. Claim: no status check, no lifetime check; a condemned or broken
        /// ride is upgraded like any other.</summary>
        bool TryClaimQueuedUpgrade(StaffMember staff);
        /// <summary>Idle, upgrade-first, second attempt (0x80096E4C..E64): the SAME selector 0x8005BE44,
        /// but the claim is made with flag 1, i.e. the repair checks and the repair state. This is the
        /// call the binary makes where a fallback to <see cref="TryClaimBrokenRide"/> would be expected.
        ///
        /// ⚠ DEAD BY CONSTRUCTION, PORTED ANYWAY. It is reached only when the first 0x8005BE44 found
        /// nothing (a found ride is unclaimed, so its flag-0 claim cannot fail), and nothing has changed
        /// since, so it finds nothing again. It is here so the machine is the one in the executable and
        /// so nobody "restores" the symmetry the report assumed.</summary>
        bool TryClaimQueuedUpgradeAsRepair(StaffMember staff);
        /// <summary>0x800632B4(ride, 0): clear the claimed ride's claim word. The mechanic's own target is
        /// untouched; the sim clears it afterwards where the binary does.</summary>
        void ReleaseClaim(StaffMember staff);
        /// <summary>0x80097640: the mechanic after this one in the class list (the link at +0 of the
        /// object), or null at the end. The hand-off walks FORWARD from the failed mechanic only.</summary>
        StaffMember NextMechanic(StaffMember staff);
        /// <summary>0x80096C58(candidate, from's target ride, flag): claim the ride FROM is (or was) on,
        /// for CANDIDATE. Refuses if the ride's claim word names someone other than the candidate; with
        /// <paramref name="forRepair"/> also refuses unless slot 20 (broken) and slot 84 (lifetime != 0)
        /// pass. On success writes the ride's claim word and the candidate's target. Used for the
        /// path-failure hand-off (from != candidate) and for the pick-up sweep's re-claim (from == candidate).</summary>
        bool TryClaimRideFor(StaffMember from, StaffMember candidate, bool forRepair);

        /// <summary>States 56/57 (0x80096E9C/0x80096F70): 0x80093C68 drops the current waypoint
        /// (P+0x28 := -1), then 0x800EC9F4 is asked for a path to the ride's slot 42 position with
        /// (0x11, 0). False when the request was refused; the mechanic keeps its state and asks again
        /// next tick.</summary>
        bool TryPathToClaimedRide(StaffMember staff);
        /// <summary>State 58 (0x80097044): the same request to the ride's slot 26 leave point, WITHOUT
        /// dropping the waypoint first. False keeps state 58 for another try.</summary>
        bool TryPathToLeavePoint(StaffMember staff);

        /// <summary>The claimed ride's closing-progress word A+0xEC. See <see cref="RideClosing"/>:
        /// a fixed-point timer, NOT the rider count A+0xF0.</summary>
        int ClosingProgress(StaffMember staff);
        void SetClosingProgress(StaffMember staff, int value);
        /// <summary>The claimed ride's footprint width + height in tiles: 0x8006A798 + 0x8006A7A4 on the
        /// object its slot 7 returns (0x8009C208..25C). Feeds <see cref="RideClosing.Threshold"/>.</summary>
        int FootprintSpan(StaffMember staff);
        /// <summary>0x800BDD0C: the closing step for this tick, 20.12, capped at 0x4000 by 0x800BDE6C.</summary>
        int ClosingStep { get; }

        /// <summary>Slot 57 with 6: status := <see cref="AttractionStatus.UnderRepair"/>.</summary>
        void MarkRideUnderRepair(StaffMember staff);
        /// <summary>Slot 57 with 7 (<see cref="AttractionStatus.Reopen"/>), THEN 0x800632B4(ride, 0) to
        /// clear the claim word. The mechanic's target survives; state 58 still needs the ride.</summary>
        void MarkRideOpenAndRelease(StaffMember staff);
        /// <summary>State 54's finish (0x80096AA4..AC4): 0x8009C56C(ride, 0) -- the paid level-up,
        /// <see cref="RidePanel.CompleteUpgrade"/> with sound and sparkle -- then 0x8005BE70(ride) to
        /// remove the ride from the upgrade queue. The ride is NOT reopened here; state 17 does that.</summary>
        void CompleteUpgrade(StaffMember staff);
    }

    /// <summary>The mechanic (Update 0x8009710C, behaviour.md §3.5 and its 2026-09-20 correction).
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
    /// two constants are the place to look, and they are named so they can be found.
    ///
    /// Two jobs, one machine: a REPAIR (56 → 16 → 14 → 17 → 58) and an UPGRADE (57 → 52 → 54 → 17 → 58).
    /// The only differences are which selector found the ride, whether the walk costs anything, and
    /// whether 0x8009C56C is paid at the end. Nothing here restores reliability; a repair is a wait.</summary>
    public static class Mechanic
    {
        /// <summary>Ticks to repair or upgrade, by skill, from 0x800E4574 column 0.
        ///
        /// ⚠ THE TABLE IS PAIRS OF s16, NOT A FLAT ARRAY. behaviour.md quotes it as
        /// `0x800E4574[skill] = 240/180/120/60/60` and those are right, but they are every OTHER
        /// halfword: the real layout is five rows of two, and the second column is
        /// <see cref="UnknownSecondColumn"/>. Reading it as a flat s32 array - the obvious thing, since
        /// the other staff tables are s32 - gives 590064, 786612, 917624, which is nonsense that would
        /// have been caught, and reading it as a flat s16 array gives 240, 9, 180, 12, which would NOT
        /// have been: the first value is right and the second is plausible. READ at 0x80096670..688:
        /// `(skill &amp; 7) * 4` then `lhu` -- four bytes a row, first halfword.</summary>
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

            // ⚠ THE TWO ORDERS ARE NOT MIRROR IMAGES. READ at 0x80096DE8..E6C: repair-first is
            // 0x8005BCB0 (broken rides) then 0x8005BE44 (the upgrade queue); upgrade-first is
            // 0x8005BE44 then 0x8005BE44 AGAIN with the repair flag, and never 0x8005BCB0. So half of all
            // idle ticks do not look at broken rides at all, and a park with a breakdown and an empty
            // upgrade queue waits, on average, one extra idle tick for its mechanic. behaviour.md's
            // "or the reverse" is the natural reading and it is wrong. DO NOT make this symmetric.
            if (rng.Next(2) == 0)
            {
                if (Take(staff, world.TryClaimBrokenRide(staff), MechanicStates.GoToBrokenRide)) return;
                if (Take(staff, world.TryClaimQueuedUpgrade(staff), MechanicStates.GoToUpgradeRide)) return;
            }
            else
            {
                if (Take(staff, world.TryClaimQueuedUpgrade(staff), MechanicStates.GoToUpgradeRide)) return;
                if (Take(staff, world.TryClaimQueuedUpgradeAsRepair(staff), MechanicStates.GoToBrokenRide)) return;
            }
            staff.SetState(StaffState.Patrolling);
        }

        /// <summary>The sim side of a successful 0x80096C58: target written, state 56 (flag 1) or 57.</summary>
        static bool Take(StaffMember staff, bool claimed, StaffState go)
        {
            if (!claimed) return false;
            staff.HasTarget = true;
            staff.SetState(go);
            return true;
        }

        /// <summary>States 56 and 57 (0x80096E9C / 0x80096F70): set off for the ride.
        ///
        /// ⚠ A REFUSED REQUEST IS NOT A FAILURE. When 0x800EC9F4 returns 0 the handler simply returns,
        /// so the mechanic sits in 56/57 -- claim held, no path -- and asks again every tick until the
        /// pathfinder takes the job. The claim is released only by a path that was ISSUED and then
        /// failed (<see cref="OnPathMessage"/>). On success M+0x2C is stamped with `now` (READ,
        /// 0x80096F44); nothing reads it before arrival zeroes it, but it is what the code does.</summary>
        public static bool SetOff(StaffMember staff, IMechanicWorld world, bool forRepair)
        {
            if (!world.TryPathToClaimedRide(staff)) return false;
            staff.BusyUntil = world.NowTick;
            staff.Purpose = forRepair ? MechanicStates.ToRepair : MechanicStates.ToUpgrade;
            staff.PushState(StaffState.Walking);
            return true;
        }

        /// <summary>The mechanic's share of a still-walking tick (0x80096BF4..C24). See the class note:
        /// walking to a REPAIR is restful and demoralising; walking to an upgrade is neither.
        /// <see cref="Arrive"/> applies this and then the base's own walking cost.</summary>
        public static void WalkTick(StaffMember staff)
        {
            if (staff.Purpose != MechanicStates.ToRepair) return;
            staff.Tiredness = Stat.Add(staff.Tiredness, RepairWalkTiredness);
            staff.Morale = Stat.Add(staff.Morale, RepairWalkMorale);
        }

        /// <summary>Slot 35 (0x80096AE0): the per-tick handler of state 2, as <see cref="StaffBase.Arrive"/>.
        /// Still walking (waypoint index != -1): <see cref="WalkTick"/>, then the base. Arrived: Set 0
        /// FIRST, then by purpose.
        ///
        /// ⚠ ARRIVING WITHOUT A TARGET LEAVES HIM IDLE. Both job purposes check M+0x28 before closing
        /// the ride (0x80096B74, 0x80096BA4): a claim that was taken away mid-walk (the pick-up sweep,
        /// the ride being demolished) does not put a mechanic to work on a ride he no longer holds. And
        /// M+0x2C is zeroed here (`sw zero,44(s1)`), which is what makes state 14's zero guard
        /// (<see cref="Work"/>) unreachable in the normal flow. Person flag bit 1 is also cleared
        /// (0x80093EFC with 0); its reader is not traced and the staff port does not carry the bit.</summary>
        public static void Arrive(StaffMember staff, IMechanicWorld world, bool stillWalking)
        {
            if (staff == null) throw new ArgumentNullException(nameof(staff));
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (stillWalking)
            {
                WalkTick(staff);
                StaffBase.Arrive(staff, world, stillWalking: true);
                return;
            }

            staff.SetState(StaffState.Idle);
            switch (staff.Purpose)
            {
                case MechanicStates.ToRepair:
                case MechanicStates.ToUpgrade:
                    if (!staff.HasTarget) return;
                    staff.BusyUntil = 0;
                    staff.SetState(staff.Purpose == MechanicStates.ToRepair
                        ? MechanicStates.ClosingRide
                        : MechanicStates.ClosingForUpgrade);
                    return;
                case MechanicStates.LeaveRide:
                    staff.HasTarget = false;
                    return;
                default:
                    StaffBase.Arrive(staff, world, stillWalking: false);
                    return;
            }
        }

        /// <summary>States 16 and 52 (0x8009660C / 0x80096900) -- close the ride, then start the clock.
        ///
        /// ⚠ "CLOSED" MEANS THE PROGRESS WORD REACHED ITS THRESHOLD, NOT THAT THE RIDERS LEFT. Each tick
        /// 0x8009C1E8 compares A+0xEC with `10*(w+h)` fixed-point units and, short of it, 0x8009EEE4 adds
        /// the tick step (<see cref="RideClosing"/>). Nobody is unloaded by this; whatever the ride does
        /// with its guests while its status is 6 is the ride's business. A bigger ride takes longer to
        /// close and the timer does not start until it has. Order on completion is READ: state, then
        /// status 6, then the deadline.</summary>
        public static bool CloseRide(StaffMember staff, IMechanicWorld world, bool forUpgrade)
        {
            int threshold = RideClosing.Threshold(world.FootprintSpan(staff));
            int progress = world.ClosingProgress(staff);
            if (!RideClosing.IsComplete(progress, threshold))
            {
                world.SetClosingProgress(staff, RideClosing.Raise(progress, world.ClosingStep, threshold));
                return false;
            }
            staff.SetState(forUpgrade ? MechanicStates.Upgrading : MechanicStates.Repairing);
            world.MarkRideUnderRepair(staff);
            staff.BusyUntil = world.NowTick + Handyman.BySkill(RepairTicks, staff.Skill);
            return true;
        }

        /// <summary>States 14 and 54 (0x8009682C / 0x80096A08) -- the work itself: wait, then for an
        /// upgrade pay for it. A repair changes nothing about the ride here; reopening is the repair.
        ///
        /// ⚠ DO NOT FIX: A ZERO DEADLINE NEVER FIRES. READ at 0x800968B4 and 0x80096A90: `beq v0,zero`
        /// returns before the comparison, so a mechanic whose M+0x2C is 0 works forever. Unreachable
        /// from the normal path (the deadline is now + at least 60) and ported because a save or a host
        /// that seeds the field with 0 gets the original's hang, not a free repair. The comparison
        /// itself is `deadline &lt; now`, strictly.</summary>
        public static bool Work(StaffMember staff, IMechanicWorld world, bool forUpgrade)
        {
            if (staff.BusyUntil == 0) return false;
            if (!(staff.BusyUntil < world.NowTick)) return false;
            if (forUpgrade) world.CompleteUpgrade(staff);
            staff.SetState(MechanicStates.OpeningRide);
            return true;
        }

        /// <summary>State 17 (0x80096710) -- put the ride back in service.
        ///
        /// ⚠ REOPENING WAITS FOR PROGRESS == 0 (0x8009C294), NOT FOR "BELOW THE THRESHOLD", and lowers
        /// it by the tick step meanwhile. Closing and reopening are therefore the same length for the
        /// same ride, and a ride whose progress is being raised by something else at the same time (the
        /// condemned-ride update 0x8009CCD4 does exactly this while A+0x68 is 0) never reaches zero: a
        /// mechanic sent to upgrade a condemned ride stands in 17 forever. GUESS-high on the last
        /// sentence; READ on everything before it.
        ///
        /// ⭐ THE +10 MORALE IS PAID HERE, at reopening, not at finishing the repair. A mechanic pulled
        /// off a job before the ride reopens never gets paid for it. Order is READ: status 7 and the
        /// claim release, then state 58, then flag bit 1 set, then the morale.</summary>
        public static bool OpenRide(StaffMember staff, IMechanicWorld world)
        {
            int progress = world.ClosingProgress(staff);
            if (progress != 0)
            {
                world.SetClosingProgress(staff, RideClosing.Lower(progress, world.ClosingStep));
                return false;
            }
            world.MarkRideOpenAndRelease(staff);
            staff.SetState(MechanicStates.LeavingRide);
            staff.Morale = Stat.Add(staff.Morale, ReopenMorale);
            return true;
        }

        /// <summary>State 58 (0x80097044) -- walk off the ride. Same shape as <see cref="SetOff"/>: a
        /// refused request keeps him in 58 to ask again, the target stays until arrival 22 clears it.</summary>
        public static bool LeaveRide(StaffMember staff, IMechanicWorld world)
        {
            if (!world.TryPathToLeavePoint(staff)) return false;
            staff.BusyUntil = world.NowTick;
            staff.Purpose = MechanicStates.LeaveRide;
            staff.PushState(StaffState.Walking);
            return true;
        }

        /// <summary>Messages (0x800964B0). 1 → state 3; 2 by purpose; 3 is swallowed (the base ignores it
        /// too); anything else → the base.
        ///
        /// ⭐ A FAILED WALK TO A JOB RELEASES THE RIDE AND OFFERS IT ON. READ at 0x80096554..5BC: clear
        /// the claim word, then walk the mechanic list FROM THE ONE AFTER ME (0x80097640 is the +0
        /// link) and give the ride to the first colleague <see cref="CanTakeHandedJob"/> accepts, via
        /// the same 0x80096C58 that Idle uses -- with the repair checks if this was a repair. Then, taken
        /// or not, target := 0 and state 0. A mechanic earlier in the list is never asked, and a refused
        /// claim (the ride stopped being broken, or was condemned, in the meantime) ends the search
        /// rather than moving to the next candidate.</summary>
        public static void OnPathMessage(StaffMember staff, IMechanicWorld world, bool pathFound)
        {
            if (staff == null) throw new ArgumentNullException(nameof(staff));
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (pathFound) { staff.SetState(StaffState.PathReady); return; }

            if (staff.Purpose == MechanicStates.ToRepair || staff.Purpose == MechanicStates.ToUpgrade)
            {
                world.ReleaseClaim(staff);
                HandJobOn(staff, world, forRepair: staff.Purpose == MechanicStates.ToRepair);
                staff.HasTarget = false;
                staff.SetState(StaffState.Idle);
                return;
            }
            if (staff.Purpose == MechanicStates.LeaveRide)
            {
                staff.HasTarget = false;
                staff.SetState(StaffState.Idle);
                return;
            }
            StaffBase.OnPathMessage(staff, pathFound);
        }

        static void HandJobOn(StaffMember staff, IMechanicWorld world, bool forRepair)
        {
            for (var candidate = world.NextMechanic(staff); candidate != null; candidate = world.NextMechanic(candidate))
            {
                if (!CanTakeHandedJob(candidate)) continue;
                Take(candidate, world.TryClaimRideFor(staff, candidate, forRepair),
                    forRepair ? MechanicStates.GoToBrokenRide : MechanicStates.GoToUpgradeRide);
                return;
            }
        }

        /// <summary>0x80097694: a colleague can be handed a job when it has no target and
        /// <see cref="StaffBase.IsCommittedToStrikeOrRest"/> is false. Note what is NOT checked: its
        /// state. A mechanic mid-patrol (13), or walking a patrol leg, has no target and is taken; the
        /// claim's Set 56/57 clears whatever it was doing.</summary>
        public static bool CanTakeHandedJob(StaffMember candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            return !candidate.HasTarget && !StaffBase.IsCommittedToStrikeOrRest(candidate);
        }

        /// <summary>0x8009731C, run on every mechanic by the catalogue pick-up sweep 0x800EBA2C (from
        /// 0x80058F90, the moment the player lifts a build object; parkopen.md §2 and pathfinder.md
        /// agree on the trigger, and the sweep also parks the pathfinder). behaviour.md guessed "job
        /// cancelled, ride removed"; the code re-claims the SAME ride, so it is a re-plan, not a cancel.
        /// The sweep drops the current waypoint (0x80093C68) before this runs; the host does that.
        ///
        /// READ, by the table at 0x800E467C: walking states 2, 3 and 11 re-issue the job -- purpose 20
        /// re-claims for an upgrade, 6 re-claims for a repair (with the repair checks, so a ride that
        /// stopped qualifying leaves him where he is), 22 goes back to 58, any other purpose releases and
        /// aborts. States 14, 16, 17, 52 and 54 -- at the ride -- are left alone. EVERYTHING ELSE,
        /// including 56/57/58 (states 55+ fall off the table's end), releases the claim if there is a
        /// target and aborts to Idle (0x80095358: target := 0, path dropped, state 0).
        ///
        /// ⚠ THE RELEASE IS A RAW STORE TO THE TARGET'S +0x54. For a mechanic walking to a bench (purpose
        /// 17) the target is the bench, and 0x800632B4 writes its word regardless. What the host does
        /// with a claim release on a non-ride is the host's decision; the sim only reports it.</summary>
        public static void OnCataloguePickUp(StaffMember staff, IMechanicWorld world)
        {
            if (staff == null) throw new ArgumentNullException(nameof(staff));
            if (world == null) throw new ArgumentNullException(nameof(world));

            switch (staff.State)
            {
                case StaffState.WalkToDestination:
                case StaffState.PathReady:
                case StaffState.Walking:
                    switch (staff.Purpose)
                    {
                        case MechanicStates.ToUpgrade:
                            Take(staff, world.TryClaimRideFor(staff, staff, forRepair: false), MechanicStates.GoToUpgradeRide);
                            return;
                        case MechanicStates.ToRepair:
                            Take(staff, world.TryClaimRideFor(staff, staff, forRepair: true), MechanicStates.GoToBrokenRide);
                            return;
                        case MechanicStates.LeaveRide:
                            staff.SetState(MechanicStates.LeavingRide);
                            return;
                        default:
                            Abort(staff, world);
                            return;
                    }
                case MechanicStates.Repairing:
                case MechanicStates.ClosingRide:
                case MechanicStates.OpeningRide:
                case MechanicStates.ClosingForUpgrade:
                case MechanicStates.Upgrading:
                    return;
                default:
                    Abort(staff, world);
                    return;
            }
        }

        static void Abort(StaffMember staff, IMechanicWorld world)
        {
            if (staff.HasTarget) world.ReleaseClaim(staff);
            staff.HasTarget = false;
            staff.SetState(StaffState.Idle);
        }
    }
}
