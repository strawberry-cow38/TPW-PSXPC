using System;

namespace TPW.Sim
{
    /// <summary>READ: the level words used by the panel, record+0x24+0x34*level
    /// (ride-panel.md §1). The host copies these from TPW.Data.RideLevel; the sim does not load assets.</summary>
    public readonly record struct RidePanelLevel(int WearMultiplier, int MaxSeats, int Lifetime,
        int SpeedMin, int SpeedMax, int CyclesMin, int CyclesMax, int PricePounds);

    /// <summary>One ride as the panel sees it. These setters are the unclamped object stores;
    /// panel clamping is a separate operation (0x8009ECFC..0x8009ED30).</summary>
    public interface IRidePanelWorld
    {
        AttractionType Type { get; }
        int Level { get; set; }
        RidePanelLevel ReadLevel(int level);
        /// <summary>READ: slot 99. Usually ReadLevel(Level).MaxSeats; coasters override it with
        /// model attachment count * min(record train count,8), 0x800AD728. Do not use capacity.</summary>
        int MaximumSeats { get; }
        int BaseIntensity { get; }
        int SpeedSlider { get; set; }
        int Capacity { get; set; }
        int CyclesPerLoad { get; set; }
        /// <summary>A+0xB4, in 20.12; map to RideWear.Reliability, not ReliabilityPoints.</summary>
        int ReliabilityFixed { get; set; }
        int Lifetime { get; set; }
        int CyclesRun { get; set; }
        /// <summary>A+0xEC, in 20.12. Separate from animation accumulator A+0x60.</summary>
        int ClosingProgress { get; set; }
        /// <summary>Release and zero BOTH handles, smoke A+0xE4 and sparkle A+0xE8 (0x8009C428/434).</summary>
        void ClearRideEffects();
    }

    /// <summary>The park owns the research, mechanic list, upgrade queue and bank.</summary>
    public interface IRideUpgradeWorld : IRidePanelWorld
    {
        /// <summary>READ: 0x8006AC04 returns an exclusive upper bound; next level must be BELOW it.</summary>
        int ResearchedLevelCount { get; }
        int MechanicCount { get; }
        bool MechanicsOnStrike { get; }
        /// <summary>READ: queue 0x801099EC holds 15 distinct ride pointers. A duplicate succeeds;
        /// a new entry when full fails (0x8005BA8C). Requesting does not close or charge the ride.</summary>
        bool TryEnqueueUpgrade();
        bool TrySpend(Money amount);
        /// <summary>READ: sound (8,final?10:11) and sparkle, 0x8009C61C..670.</summary>
        void ShowUpgradeEffect(bool finalLevel);
    }

    /// <summary>READ: widget limits, independent of the selected level (0x80078F70).
    /// Widget storage is 16.16 and its public getter is a SIGNED halfword, 0x8004236C.</summary>
    public readonly record struct RideSliderRange(int Min, int Max)
    {
        public int Clamp(int value) => unchecked((short)Math.Min(Max, Math.Max(Min, value)));
    }

    public readonly record struct RidePanelRanges(RideSliderRange Speed, RideSliderRange Capacity,
        RideSliderRange Duration, bool CapacityVisible, bool DurationVisible);

    /// <summary>READ: save+0x8A/8C/8D (0x8009CE0C..2C). Live sliders are all 32-bit words.</summary>
    public readonly record struct RideSliderSave(ushort Speed, byte Capacity, byte Duration);

    /// <summary>The ride settings and paid upgrade (ride-panel.md).
    ///
    /// ⭐ THE THIRD SLIDER IS CAPACITY, NOT A TICKET PRICE. READ: labels Speed/Capacity/Duration
    /// at 0x800791C8/218/268 and stores at 0x80079420/444/468. economy.md §4.7's no-ticket rule
    /// is retained. There is no ride-price setter, sale, or guest debit to invent here.
    ///
    /// This class uses the same world fields as RideWear, RideLoading, RideCycle and VisitorQueue.
    /// It owns neither guests nor the park, and changing a slider does not restart a run.</summary>
    public static class RidePanel
    {
        /// <summary>READ: levels 0,1,2 are examined even when research has not unlocked them
        /// (0x80078FD0..0x80079154).</summary>
        public const int PanelLevelCount = 3;
        /// <summary>READ: the completion routine accepts current level less than THREE, although
        /// the panel only offers next level less than three (0x8009C590 / 0x80078E78).
        /// ⚠ NOT ESTABLISHED: a valid fourth level's values. ReadLevel(3) must either supply the
        /// actual overread words or fail explicitly; never substitute a plausible price or seat count.</summary>
        public const int BinaryUpgradeLimit = 3;
        /// <summary>READ: both half-maximum defaults have a floor of one (0x8009C470/52C).</summary>
        public const int MinimumDefault = 1;

        /// <summary>READ: placement sets level zero before deriving defaults, then reads lifetime
        /// from level zero (0x8009C354 / 0x8009C3C8). Placement money belongs to the build tool.</summary>
        public static void Place(IRidePanelWorld world)
        {
            world.Level = 0;
            ResetForLevel(world);
            world.Lifetime = world.ReadLevel(0).Lifetime;
        }

        static void ResetForLevel(IRidePanelWorld world)
        {
            world.ClearRideEffects();
            world.ReliabilityFixed = RideWear.FullReliability;
            world.CyclesRun = 0;
            world.ClosingProgress = 0;
            var level = world.ReadLevel(world.Level);
            world.Capacity = Math.Max(MinimumDefault, world.MaximumSeats >> 1);
            world.SpeedSlider = level.SpeedMin + ((level.SpeedMax - level.SpeedMin) >> 1);
            // ⚠ DO NOT FIX: Bounce on Iggy starts at 22 with a minimum of 30. The initializer
            // uses max(1,max>>1), NOT the range clamp used later by the widget (0x8009C52C..550).
            world.CyclesPerLoad = Math.Max(MinimumDefault, level.CyclesMax >> 1);
        }

        /// <summary>READ: union bounds across all THREE blocks, not the current or researched
        /// blocks (0x80078FD0..0x80079154). The coaster capacity uses its virtual maximum instead.</summary>
        public static RidePanelRanges Ranges(IRidePanelWorld world)
        {
            var first = world.ReadLevel(0);
            int speedMin = first.SpeedMin, speedMax = first.SpeedMax;
            int cyclesMin = first.CyclesMin, cyclesMax = first.CyclesMax;
            int seats = first.MaxSeats;
            for (int i = 1; i < PanelLevelCount; i++)
            {
                var level = world.ReadLevel(i);
                speedMin = Math.Min(speedMin, level.SpeedMin);
                speedMax = Math.Max(speedMax, level.SpeedMax);
                cyclesMin = Math.Min(cyclesMin, level.CyclesMin);
                cyclesMax = Math.Max(cyclesMax, level.CyclesMax);
                seats = Math.Max(seats, level.MaxSeats);
            }
            if (world.Type == AttractionType.RollerCoaster) seats = world.MaximumSeats;
            return new(new(speedMin, speedMax), new(MinimumDefault, seats),
                new(cyclesMin, cyclesMax), seats > 1, world.Type != AttractionType.RollerCoaster);
        }

        /// <summary>READ: widget update writes ALL three values, including the hidden coaster
        /// duration (0x8007940C..478). Opening initializes clamped widget copies; its first update
        /// can therefore change an out-of-range placement default without moving a control.</summary>
        public static void Apply(IRidePanelWorld world, int speed, int capacity, int duration)
        {
            var ranges = Ranges(world);
            world.SpeedSlider = ranges.Speed.Clamp(speed);
            world.Capacity = ranges.Capacity.Clamp(capacity);
            world.CyclesPerLoad = ranges.Duration.Clamp(duration);
        }

        /// <summary>READ: save narrows speed to u16 and capacity/duration to u8 (0x8009CE0C..2C).</summary>
        public static RideSliderSave SaveSliders(IRidePanelWorld world) => new(
            unchecked((ushort)world.SpeedSlider), unchecked((byte)world.Capacity),
            unchecked((byte)world.CyclesPerLoad));

        /// <summary>READ: loading calls the raw setters, without a range check (0x8009CF58..FA8).</summary>
        public static void RestoreSliders(IRidePanelWorld world, RideSliderSave saved)
        {
            world.SpeedSlider = saved.Speed;
            world.Capacity = saved.Capacity;
            world.CyclesPerLoad = saved.Duration;
        }

        /// <summary>READ: visibility and research availability, 0x80078E70..F44.
        /// Lifetime is tested for ZERO, not against a positive reliability threshold.</summary>
        public static bool CanOfferUpgrade(IRideUpgradeWorld world)
            => world.Level + 1 < PanelLevelCount && world.Lifetime != 0
                && world.Level + 1 < world.ResearchedLevelCount;

        /// <summary>READ: 0x80079AA8..B24. No balance, reliability or attraction-status check.
        /// The action queues work for a mechanic; it does not buy an instantaneous upgrade.</summary>
        public static bool RequestUpgrade(IRideUpgradeWorld world)
        {
            if (!CanOfferUpgrade(world) || world.MechanicCount == 0 || world.MechanicsOnStrike)
                return false;
            return world.TryEnqueueUpgrade();
        }

        /// <summary>READ: 0x8009C56C. The mechanic calls this AFTER its closing/work states;
        /// the manager flush path passes silent=true. There is no downgrade path.
        ///
        /// ⚠ DO NOT FIX: level and sliders change BEFORE TrySpend, and its return is ignored.
        /// The object checks neither lifetime nor status nor affordability here. The panel's gates
        /// are separate. Resetting lifetime, ejecting riders, changing status or clearing A+0x60
        /// here would each add a write the original does not make.</summary>
        public static bool CompleteUpgrade(IRideUpgradeWorld world, bool silent = false)
        {
            if (world.Level >= BinaryUpgradeLimit) return false;
            world.Level++;
            ResetForLevel(world);
            world.TrySpend(Money.FromPounds(world.ReadLevel(world.Level).PricePounds));
            if (!silent) world.ShowUpgradeEffect(world.Level >= BinaryUpgradeLimit);
            return true;
        }

        /// <summary>READ: the live level's multiplier (slot 102, 0x8009DC7C).</summary>
        public static int WearMultiplier(IRidePanelWorld world) => world.ReadLevel(world.Level).WearMultiplier;

        /// <summary>READ: flat/tour slot 53; track/coaster retain rides.md §5's common formula
        /// pending the source disagreements in ride-panel.md. In particular this does not add the
        /// track's outer+0x195/2 bonus or remove the coaster duration divisor by silent correction.</summary>
        public static int Intensity(IRidePanelWorld world)
            => RideSliderEffects.Intensity(world.BaseIntensity, world.SpeedSlider, world.CyclesPerLoad);

        /// <summary>READ: slot 53 reaches the preference match and hasK in 0x8008C818.
        /// Slots 54/55/56 remain zero (0x8009C2A0 / 0x8009F5FC / 0x8009F5F4).
        /// The host supplies the virtual slot-86 result, including the coaster connection veto.</summary>
        public static AttractionCandidate Candidate(IRidePanelWorld world, int id, bool openToGuests,
            int distanceTiles, bool centreTileValid) => new(id, (int)world.Type, Intensity(world),
                0, 0, 0, openToGuests, distanceTiles, centreTileValid);
    }

    /// <summary>Slider consumers not already expressed by RideWear, RideCycle and the vehicle
    /// controllers. Inputs describing geometry or vehicle base speed remain the world's concern.</summary>
    public static class RideSliderEffects
    {
        public const int FixedShift = 12;
        /// <summary>READ: 0x800A06D0..710, raw 20.12 factor bounds.</summary>
        public const int MinimumFactor = 0xC00, MaximumFactor = 0x1400;
        /// <summary>READ: signed divisions at 0x800A06A8 / 0x800A06BC.</summary>
        public const int SpeedDivisor = 100, DurationDivisor = 5;
        /// <summary>READ: final min(100,value), 0x800A072C → 0x800A0AF8.</summary>
        public const int MaximumIntensity = 100;

        /// <summary>READ: flat and tour slot 53 (0x800A069C..730 / 0x800A212C..21C0).
        /// Both factors are clamped separately; multiply the factors and truncate BEFORE multiplying
        /// the base. This is integer 20.12, not floating point, and there is no final lower clamp.</summary>
        public static int Intensity(int baseIntensity, int speed, int duration)
        {
            int speedFactor = Math.Clamp(unchecked(speed << FixedShift) / SpeedDivisor,
                MinimumFactor, MaximumFactor);
            int durationFactor = Math.Clamp(unchecked(duration << FixedShift) / DurationDivisor,
                MinimumFactor, MaximumFactor);
            int combined = unchecked(speedFactor * durationFactor) >> FixedShift;
            return Math.Min(MaximumIntensity, unchecked(baseIntensity * combined) >> FixedShift);
        }

        /// <summary>READ: tour vehicle+0x28, 0x800A3608..660. GUESS-high: movement speed;
        /// the numeric rule and signed-halfword store are established. baseSpeed is supplied by
        /// the vehicle's caller, not a newly guessed global speed.</summary>
        public static int TourVehicleSpeed(int baseSpeed, int speed)
            => Math.Max(2, (int)unchecked((short)(baseSpeed + Math.Clamp(speed - 100, -50, 50) / 10)));

        /// <summary>READ: track vehicle+0x1D at initialization (0x800ABDE8..E08), speed/20
        /// narrowed to a byte. This is sampled when the vehicle is initialized, not each tick.</summary>
        public static byte TrackVehicleSpeed(int speed) => unchecked((byte)(speed / 20));

        /// <summary>READ: coaster velocity clamp at 0x800B2094..20EC. The world supplies the
        /// current velocity and the gp coefficients (0x80103384/388); no coefficient is invented.
        /// The minimum is applied LAST, even if it exceeds the slider-scaled maximum.</summary>
        public static int CoasterVelocity(int velocity, int speed, int minimum, int maximumAt100)
            => Math.Max(minimum, Math.Min(velocity, unchecked(maximumAt100 * speed) / SpeedDivisor));

        /// <summary>READ: slot 88, 0x8009ED84..DB0 / 0x800B0594..5C4. wearPrediction is slot
        /// 104 called with flag 1 (capacity rather than current riders). This panel forecast does
        /// not change actual reliability. ⚠ DO NOT FIX: the coaster shifts by 12, the others by 15.</summary>
        public static int ProjectedReliability(int wearPrediction, int duration, bool coaster)
            => 100 - Math.Min(100, unchecked(wearPrediction * (9 * duration)) >> (coaster ? 12 : 15));
    }
}
