using System;

namespace TPW.Sim
{
    /// <summary>Per-class staff states and purposes that the base machine does not own.</summary>
    public static class StaffClassStates
    {
        /// <summary>READ: name-table state 25, "I will clear litter" (0x800E2360). No Set/Push
        /// writer in this build; the staff dispatch entry 0x800E416C takes the no-work default.
        /// The live litter job uses purpose 7, states 11/3/2, then 27. There is no mowing handler
        /// in the handyman's dispatch (0x80099490..510). See findings/litter.md §6.</summary>
        public const StaffState DeadClearLitter = (StaffState)25;
        /// <summary>Handyman: cleaning up a piece of litter.</summary>
        public const StaffState CleaningLitter = (StaffState)27;
        /// <summary>Handyman: emptying a bin.</summary>
        public const StaffState EmptyingBin = (StaffState)51;
        /// <summary>Researcher: the one-tick research state.</summary>
        public const StaffState Researching = (StaffState)31;

        /// <summary>Handyman purpose: walking to a claimed piece of litter.</summary>
        public const StaffPurpose ToLitter = (StaffPurpose)7;
        /// <summary>Handyman purpose: walking to a bin.</summary>
        public const StaffPurpose ToBin = (StaffPurpose)18;
    }

    /// <summary>What a handyman needs of the park.</summary>
    public interface IHandymanWorld : IStaffWorld
    {
        /// <summary>Nearest UNCLAIMED litter by Manhattan tiles; claims it for this handyman and asks
        /// for a path. False when there is none or the request was refused.</summary>
        bool TryClaimNearestLitter(StaffMember staff);
        /// <summary>The bin worth going to (0x80098F20): over every feature whose slot 33 is set and
        /// whose capacity byte reads under <see cref="Handyman.BinPickBelow"/> (<see cref="Handyman.WantsEmptying"/>),
        /// the lowest <see cref="Handyman.BinScore"/> -- so near and full beats far and fuller. READ
        /// (0x80023E60 → 0x80024348): slot 33 is the record flag `rec+0x2E &amp; 1`, the SAME bit guests'
        /// slot 54 reads, so a handyman services every guest-usable feature, toilets included, not only
        /// bins. rides.md §1.3 guessed bit 2 for this filter; see findings/shop-stock.md §disagreements.</summary>
        bool TryChooseBin(StaffMember staff);
        /// <summary>The claimed litter is vomit (obj+0x1C == 0x9E) rather than ordinary rubbish.</summary>
        bool ClaimedLitterIsVomit(StaffMember staff);
        /// <summary>Remove the claimed litter from the park.</summary>
        void DeleteClaimedLitter(StaffMember staff);
        /// <summary>Release a claim without cleaning, when the walk is abandoned.</summary>
        void UnclaimLitter(StaffMember staff);
        /// <summary>How full the chosen bin was when the handyman got to it: the REMAINING capacity,
        /// 0..100. Below 40 means it was more than 60% full.</summary>
        int ChosenBinRemaining(StaffMember staff);
        /// <summary>0x80024210 on the chosen feature: <see cref="FeatureStock.Refill"/> with today's
        /// total-day count. Free, instant, and the only thing in the game that raises the byte.</summary>
        void EmptyChosenBin(StaffMember staff);
    }

    /// <summary>The handyman (Update 0x80099418, behaviour.md §3.6).
    ///
    /// ⚠ THIS CLASS IS <see cref="StaffKind.Cleaner"/>. behaviour.md calls it the handyman and the
    /// economy code calls it the cleaner; they are the same staff class. The enum's spelling wins
    /// because its ORDER was established three independent ways and renaming a member to match prose
    /// risks disturbing that -- getting the order wrong pays a mechanic a cleaner's wage and looks
    /// entirely reasonable.
    ///
    /// ⭐ THE JOB PAYS OR COSTS MORALE BY WHAT HE FINDS, and the penalties are far bigger than the
    /// rewards: clearing ordinary litter is +1, clearing vomit is -6; emptying a bin in good time is +5,
    /// emptying one that was over 60% full is -10. A park that lets its bins fill grinds its handymen
    /// down roughly twice as fast as a tidy one builds them up, which is how neglect turns into a
    /// strike rather than just into mess.</summary>
    public static class Handyman
    {
        /// <summary>Ticks to clean one piece of litter, by skill (0x800E4C38 column 0). Read out of the
        /// executable: a top-skill handyman is twelve times faster than a new one.</summary>
        public static readonly int[] CleanTicks = { 120, 60, 30, 20, 10 };

        /// <summary>Ticks to empty one bin, by skill (0x800E4C38 column 1).</summary>
        public static readonly int[] EmptyTicks = { 180, 120, 60, 30, 15 };

        /// <summary>The table's third column, 10/15/20/20/18.
        ///
        /// READ refinement: slot-44 speed getter 0x80098A68..88 reads this column. StaffMotion uses
        /// it. behaviour.md only said the job handlers did not read it; the old claim that NOTHING
        /// reads it was too broad. The historical name is retained. This is not mowing data.</summary>
        public static readonly int[] UnusedThirdColumn = { 10, 15, 20, 20, 18 };

        /// <summary>Morale for clearing ordinary litter.</summary>
        public const int LitterMorale = 1;
        /// <summary>Morale for clearing vomit.</summary>
        public const int VomitMorale = -6;
        /// <summary>Morale for emptying a bin that had not overflowed.</summary>
        public const int BinMorale = 5;
        /// <summary>Morale for emptying a bin that was more than 60% full.</summary>
        public const int FullBinMorale = -10;
        /// <summary>Remaining capacity below which the bin counts as neglected.</summary>
        public const int NeglectedBinRemaining = 40;
        /// <summary>Remaining capacity strictly below which a feature is worth a trip (0x80098FC4,
        /// `slti 0x3C`): the low-stock threshold on the STAFF side. The guest's is 50
        /// (VisitorQueue.FeatureLowStock), so a handyman starts caring ten points before a guest does.</summary>
        public const int BinPickBelow = 60;
        /// <summary>Tiredness added by finishing either job.</summary>
        public const int JobTiredness = 5;

        /// <summary>Does this feature's capacity byte make it a candidate for emptying? Strictly below
        /// <see cref="BinPickBelow"/> (0x80098FC4..0x80098FC8).</summary>
        public static bool WantsEmptying(int remaining) => remaining < BinPickBelow;

        /// <summary>The score the bin choice MINIMISES, behaviour.md §3.6: `Manhattan distance ×
        /// (remaining + 1)`, distance in tiles between the handyman and the feature.
        ///
        /// ⚠ DISPUTED, KEPT AS THE FINDINGS STATE IT. The instructions at 0x80099018..0x80099050 weight
        /// only the y term: `|dx| + |dy| × (remaining + 1)` (the `mult` sits in the delay slot of the
        /// y-sign branch and `|dx|` is added after `mflo`). That reads like a precedence slip in the
        /// original source, and it changes which bin wins whenever two are at different x offsets. This
        /// port keeps the report's formula pending review -- findings/shop-stock.md §disagreements item
        /// 1 -- and the test that pins it says so; flip both together.</summary>
        public static int BinScore(int dx, int dy, int remaining)
            => (Math.Abs(dx) + Math.Abs(dy)) * (remaining + 1);

        /// <summary>Index a skill table the way the original does, but safely.
        ///
        /// ⚠ THE ORIGINAL INDEXES THESE BY `skill &amp; 7` AND THE TABLES ONLY HAVE FIVE ROWS. A skill of
        /// 5, 6 or 7 would read past the end on hardware. Nothing observed produces one, so this clamps
        /// rather than reproducing an out-of-bounds read -- but if a save ever turns up with skill &gt; 4,
        /// the original's behaviour there is garbage and this is deliberately not bug-compatible.</summary>
        public static int BySkill(int[] table, int skill)
        {
            int i = skill & 7;
            return table[i < table.Length ? i : table.Length - 1];
        }

        /// <summary>Idle (0x80099364): half the time look for a bin first, otherwise litter.</summary>
        public static void Idle(StaffMember staff, IHandymanWorld world, IRandomSource rng)
        {
            if (staff == null) throw new ArgumentNullException(nameof(staff));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            if (world.IsTypeOnStrike(staff.Kind)) return;      // the base machine handles strikes

            // ⚠ THE BIN BRANCH FALLS BACK TO LITTER; the litter branch does NOT fall back to bins. So a
            // park with full bins and no litter is served half as often as one with litter and no bins.
            bool binsFirst = rng.Next(2) == 0;
            if (binsFirst && SeekBin(staff, world)) return;
            if (SeekLitter(staff, world)) return;
            staff.SetState(StaffState.Patrolling);
        }

        static bool SeekLitter(StaffMember staff, IHandymanWorld world)
        {
            if (!world.TryClaimNearestLitter(staff)) return false;
            staff.Purpose = StaffClassStates.ToLitter;
            staff.HasTarget = true;
            staff.PushState(StaffState.Walking);
            return true;
        }

        static bool SeekBin(StaffMember staff, IHandymanWorld world)
        {
            if (!world.TryChooseBin(staff)) return false;
            staff.Purpose = StaffClassStates.ToBin;
            staff.HasTarget = true;
            // ⚠ SET, not push -- unlike the litter walk. The report has 0x80098F20 using Set 11 where
            // 0x80098D44 uses Push 11, so a bin trip does not return to whatever came before it.
            staff.SetState(StaffState.Walking);
            return true;
        }

        /// <summary>Arrival: start the timer for whichever job this was.</summary>
        public static void Arrive(StaffMember staff, IHandymanWorld world)
        {
            if (staff.Purpose == StaffClassStates.ToLitter)
            {
                staff.BusyUntil = world.NowTick + BySkill(CleanTicks, staff.Skill);
                staff.SetState(StaffClassStates.CleaningLitter);
            }
            else if (staff.Purpose == StaffClassStates.ToBin)
            {
                staff.BusyUntil = world.NowTick + BySkill(EmptyTicks, staff.Skill);
                staff.SetState(StaffClassStates.EmptyingBin);
            }
        }

        /// <summary>State 27 -- cleaning (0x80099194). Nothing happens until the timer runs out.</summary>
        public static bool CleanLitter(StaffMember staff, IHandymanWorld world)
        {
            if (world.NowTick <= staff.BusyUntil) return false;

            staff.Morale = Stat.Add(staff.Morale,
                world.ClaimedLitterIsVomit(staff) ? VomitMorale : LitterMorale);
            staff.Tiredness = Stat.Add(staff.Tiredness, JobTiredness);
            world.DeleteClaimedLitter(staff);
            staff.SetState(StaffState.Idle);
            return true;
        }

        /// <summary>State 51 -- emptying a bin (0x80099264).</summary>
        public static bool EmptyBin(StaffMember staff, IHandymanWorld world)
        {
            if (world.NowTick <= staff.BusyUntil) return false;

            staff.Morale = Stat.Add(staff.Morale,
                world.ChosenBinRemaining(staff) < NeglectedBinRemaining ? FullBinMorale : BinMorale);
            staff.Tiredness = Stat.Add(staff.Tiredness, JobTiredness);
            world.EmptyChosenBin(staff);
            staff.SetState(StaffState.Idle);
            return true;
        }

        /// <summary>Messages (0x80098A94). A failed walk to litter releases the claim, so the piece does
        /// not stay reserved by a handyman that never got there.</summary>
        public static void OnPathMessage(StaffMember staff, IHandymanWorld world, bool pathFound)
        {
            if (pathFound) { staff.SetState(StaffState.PathReady); return; }
            if (staff.Purpose == StaffClassStates.ToLitter)
            {
                world.UnclaimLitter(staff);
                staff.SetState(StaffState.Patrolling);
                return;
            }
            StaffBase.OnPathMessage(staff, pathFound);
        }
    }

    /// <summary>What a researcher needs of the park.</summary>
    public interface IResearchWorld : IStaffWorld
    {
        /// <summary>Spread this many points evenly across the active research topics (up to 5).</summary>
        void ContributeResearch(int points);
        /// <summary>The bank's research multiplier, BANK+4 -- the money put behind research.</summary>
        int ResearchFunding { get; }
    }

    /// <summary>The researcher (Update 0x80099B10, behaviour.md §3.3).
    ///
    /// ⭐ A RESEARCHER ALTERNATES: roughly three ticks in ten it researches, the rest it walks a patrol.
    /// It is not a stationary worker, and the walking is not decoration -- it is why a researcher tires
    /// like everyone else and why its output is a trickle rather than a steady rate.</summary>
    public static class Researcher
    {
        /// <summary>Research points by skill (0x800E4E44). Read out of the executable; note the curve
        /// flattens badly at the top - 20, 30, 35, 40, 43 - so the fifth grade is worth little more
        /// than the fourth.</summary>
        public static readonly int[] PointsBySkill = { 20, 30, 35, 40, 43 };

        /// <summary>Chance in ten of researching rather than patrolling.</summary>
        public const int ResearchChanceInTen = 3;

        /// <summary>Idle (0x800999C0).</summary>
        public static void Idle(StaffMember staff, IResearchWorld world, IRandomSource rng)
        {
            if (world.IsTypeOnStrike(staff.Kind)) return;
            staff.SetState(rng.Next(10) < ResearchChanceInTen
                ? StaffClassStates.Researching
                : StaffState.Patrolling);
        }

        /// <summary>State 31 (0x80099A70): one tick of work, then straight back to Idle.</summary>
        public static void Research(StaffMember staff, IResearchWorld world)
        {
            int points = Handyman.BySkill(PointsBySkill, staff.Skill);
            world.ContributeResearch(points * world.ResearchFunding);
            staff.SetState(StaffState.Idle);
        }
    }
}
