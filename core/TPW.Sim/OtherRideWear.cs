namespace TPW.Sim
{
    /// <summary>The shared wear arithmetic and smoke belong to the attraction adapter.</summary>
    public interface IOtherRideWearWorld
    {
        uint NowTick { get; }
        /// <summary>READ: A+0xB4, raw 20.12 reliability.</summary>
        int ReliabilityRaw { get; }
        /// <summary>Apply the shared slot-103 reliability/lifetime step once. The host must not add
        /// another cadence gate. Exact rate arithmetic is not ported here: ride-classes.md §5/§6
        /// records the discrepancies with rides.md, whose existing rules remain authoritative.</summary>
        void ApplySharedWearStep();
        void EnsureSmoke();
    }

    /// <summary>Report-compatible wear boundary. ⚠ The binary has additional callers in loading,
    /// unloading and preview paths; per the task's retention rule this port keeps rides.md §6's
    /// running-only gate. See rides.md §0 item 11 before treating it as a complete binary port.</summary>
    public static class OtherRideWear
    {
        /// <summary>READ: 0x800A1FFC masks 0x1E, then the shared step masks 3: combined mask 0x1F.</summary>
        public const int TourTickMask = 0x1F;
        /// <summary>READ: shared step at 0x8009DD74.</summary>
        public const int SharedTickMask = 3;
        /// <summary>READ: the 20.12 threshold in rides.md §6.2 and 0x800A670C / 0x800B0AA0.</summary>
        public const int BreakReliabilityRaw = 10 << 12;

        /// <summary>READ: 0x800A8994..9E0 divides the piece count in 20.12 by 30. This is a third
        /// wear term for track rides; tours and coasters average only speed and load.</summary>
        public const int TrackPiecesPerWearUnit = 30;

        /// <summary>READ: final rate combination, 0x800A1FA8..FBC / 0x800A8994..9E0 /
        /// 0x800B0888..89C. Inputs are the already-computed raw speed/load terms, NOT sliders.
        /// Shared term calculation stays outside this slice; the precise reread is in §5.
        /// ⚠ DO NOT FIX: division truncates toward zero and multiplication keeps only the low
        /// 32 bits, as the MIPS mflo does. Fixed.operator* has different widening/scaling semantics.</summary>
        public static int CombineRate(AttractionType type, int speedTermRaw, int loadTermRaw,
                                      int wearMultiplier, byte trackPieces)
        {
            int sum = unchecked(speedTermRaw + loadTermRaw);
            int average;
            if (type == AttractionType.TrackRide)
            {
                sum = unchecked(sum + ((trackPieces << 12) / TrackPiecesPerWearUnit));
                average = sum / 3;
            }
            else average = sum / 2;
            return unchecked(average * wearMultiplier);
        }

        public static void Tick(AttractionType type, AttractionStatus status, IOtherRideWearWorld world)
        {
            if (status != AttractionStatus.Running) return; // report retained, not newly READ
            int mask = type == AttractionType.TourRide ? TourTickMask : SharedTickMask;
            if ((world.NowTick & mask) == 0) world.ApplySharedWearStep();
        }

        /// <summary>rides.md §6.2 RETAINED: track/coaster go directly to 5. ⚠ Binary disagreement:
        /// 0x800A6754 and 0x800B0B34 actually pass 4. This is a deliberate compatibility boundary,
        /// not a claim that the reread confirmed 5. The tour uses the flat ride's warning status.</summary>
        public static AttractionStatus CheckBreakdown(AttractionType type, AttractionStatus status,
                                                       IOtherRideWearWorld world)
        {
            if (status != AttractionStatus.Running || world.ReliabilityRaw >= BreakReliabilityRaw) return status;
            world.EnsureSmoke();
            return type == AttractionType.TourRide ? AttractionStatus.AboutToBreakDown
                                                  : AttractionStatus.BrokenDown;
        }
    }
}
