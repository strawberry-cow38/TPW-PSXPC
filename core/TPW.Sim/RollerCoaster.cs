using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>World boundary for the coaster controller (ride-classes.md §4).</summary>
    public interface IRollerCoasterWorld
    {
        uint NowTick { get; }
        bool TrackConnected { get; }
        /// <summary>READ: outer+0x100, recomputed by 0x800B0BE8 from each piece's +0x3C word.
        /// GUESS-high: usable geometry. It is not established as build-animation progress.</summary>
        bool PieceChecksPass { get; }
        /// <summary>READ: outer+0xE28 != 0 vetoes boarding. GUESS-high: a preview/test train;
        /// the reserved pointer's exact UI role is not established.</summary>
        bool PreviewTrainActive { get; }
        int Riders { get; }
        int Capacity { get; }
        Visitor QueueHead { get; }
        int PendingPassengers { get; }
        /// <summary>READ: 0x800AD610, model descriptor attachment count, with zero replaced by one.
        /// The adapter supplies the decoded value; it is not the record's maximum seat count.</summary>
        int BoardingBatchSize { get; }
        /// <summary>READ: outer+0x994 free-list head != 0 (0x800B1818), eight train objects.</summary>
        bool FreeTrainAvailable { get; }
        /// <summary>READ: 0x800658D8 is called but its phase-complete return value is ignored.</summary>
        bool AdvanceStationAnimation();
        /// <summary>READ: 0x800B025C moves the head into state 21 and the ride list, shuffles the
        /// queue and appends it to outer+0xDE8, incrementing PendingPassengers and Riders.</summary>
        void BoardHeadGuest();
        /// <summary>READ: 0x800B01B8 initializes a free train on the launch segment (launch flag 1,
        /// laps 0, finished flag 0; 0x800B2DC0), transfers the pending batch, and resets both
        /// the pending count and outer+0xE2C elapsed accumulator.</summary>
        void DispatchPendingPassengers();
        /// <summary>READ: active list order, captured before removals (next is saved before unlink).</summary>
        IReadOnlyList<CoasterTrain> ActiveTrains { get; }
        /// <summary>READ: 0x800B043C pops ALL passengers in reverse boarding order, uses the shared
        /// state-22 exit routine, unlinks the train and returns it to the free list.</summary>
        void UnloadTrain(CoasterTrain train);
    }

    /// <summary>READ: train control, not flat-ride phase counting. Each train finishes independently;
    /// loading, travelling and unloading can overlap. No whole-ride "last car home" timer.</summary>
    public sealed class RollerCoaster
    {
        public const int LoadCadence = 20;
        /// <summary>READ: 0x800B0400, hard buffer limit independent of the model batch size.</summary>
        public const int MaxPendingPassengers = 16;
        /// <summary>READ: 0x800B0D20 compares the incremented counter with 0xF1.</summary>
        public const int PartialDispatchInterval = 241;
        /// <summary>READ: outer+0xDE4. This is a repeating update counter, not the age of the batch.</summary>
        public int DispatchTicks { get; set; }

        /// <summary>READ: slot 86 at 0x800AD78C calls the shared predicate then vetoes a zero
        /// outer+0x104. The closing-link writer is 0x800ADEF4; track build progress is a different field.</summary>
        public static bool OpenToGuests(AttractionStatus status, bool trackConnected)
            => AttractionLifecycle.OpenToGuests(status) && trackConnected;

        /// <summary>READ: 0x800B0D04..D34, once per coaster Update, before status dispatch.</summary>
        public void UpdateDispatch(IRollerCoasterWorld world)
        {
            DispatchTicks = unchecked(DispatchTicks + 1);
            if (DispatchTicks < PartialDispatchInterval) return;
            TryDispatch(world);
            DispatchTicks = 0;
        }

        static void TryDispatch(IRollerCoasterWorld world)
        {
            if (world.PendingPassengers != 0 && world.FreeTrainAvailable)
                world.DispatchPendingPassengers();
        }

        /// <summary>READ: status 10 (0x800B0DC8); also called by running. Wear is exposed separately
        /// through OtherRideWear because the report's status gate is retained pending review.</summary>
        public static void LoadTick(IRollerCoasterWorld world)
        {
            world.AdvanceStationAnimation();
            if (world.TrackConnected && world.PieceChecksPass && !world.PreviewTrainActive
                && world.NowTick % LoadCadence == 0 && world.Riders < world.Capacity)
            {
                var head = world.QueueHead;
                if (head != null && head.State == VisitorState.WaitingInQueue)
                {
                    int batch = System.Math.Min(world.BoardingBatchSize, world.Capacity);
                    world.BoardHeadGuest();
                    if (world.PendingPassengers >= batch || world.PendingPassengers >= MaxPendingPassengers)
                        TryDispatch(world);
                }
            }
            UnloadTick(world);
        }

        /// <summary>READ: 0x800B0D68. ⚠ DO NOT FIX: LoadTick already scans for unloading, and running
        /// calls it again. Both virtual calls are in the binary (0x800B0D90 / 0x800B0DAC).</summary>
        public static void RunTick(IRollerCoasterWorld world)
        {
            LoadTick(world);
            UnloadTick(world);
        }

        /// <summary>READ: 0x800B0F90. No 10- or 20-tick cadence; each ready train empties in one call.</summary>
        public static void UnloadTick(IRollerCoasterWorld world)
        {
            // A snapshot represents the saved-next traversal of the intrusive original list.
            var trains = new List<CoasterTrain>(world.ActiveTrains);
            foreach (var train in trains)
                if (train.ReadyToUnload && !train.IsPreview) world.UnloadTrain(train);
        }
    }

    /// <summary>READ: the 0x88-byte train object's completion fields, 0x800B2220..2C8.
    /// Its segment geometry, velocity and acceleration are deliberately world-side.</summary>
    public sealed class CoasterTrain
    {
        /// <summary>READ: this train equals the pointer at owner+0xE28. GUESS-high: preview/test
        /// train. The pointer comparison and its exemptions are established, regardless of the label.</summary>
        public bool IsPreview { get; set; }
        /// <summary>READ: train+0x78. Set on launch, cleared when advancing into the next segment.</summary>
        public bool StillOnLaunchSegment { get; set; }
        public short Laps { get; set; }
        public bool ReadyToUnload { get; set; }
        /// <summary>READ: 0x800B225C, strictly beyond the midpoint in 20.12 segment coordinates.</summary>
        public const int ReturnMidpoint = 0x800;

        /// <summary>READ: called after movement. segmentFraction must be s2 from 0x800B218C,
        /// computed BEFORE any segment handoff; do not substitute a fresh post-wrap fraction.
        /// Duration is still READ each time, even though all
        /// twelve coaster definitions have min=max=1. ⚠ DO NOT FIX: the source has no edge detector
        /// here; the counter can increment on repeated qualifying calls.</summary>
        public void AfterMovement(bool onReturnSegment, int segmentFraction, int duration)
        {
            if (IsPreview || StillOnLaunchSegment || !onReturnSegment || segmentFraction <= ReturnMidpoint) return;
            Laps = unchecked((short)(Laps + 1));
            if (Laps >= duration) ReadyToUnload = true;
        }
    }
}
