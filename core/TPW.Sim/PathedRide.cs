namespace TPW.Sim
{
    /// <summary>What the track ride needs of its queue and vehicles (ride-classes.md §3).</summary>
    public interface IPathedRideWorld
    {
        uint NowTick { get; }
        int Riders { get; }
        int Capacity { get; }
        int Duration { get; }
        bool TrackConnected { get; }
        Visitor QueueHead { get; }
        /// <summary>READ: 0x800A80F0. Board into the last vehicle if its rec+0xD0 capacity permits;
        /// otherwise allocate a vehicle, then append. Guest/list/shuffle effects match the tour.</summary>
        void BoardHeadGuest();
        int VehicleCount { get; }
        bool VehicleReadyToUnload(int index);
        /// <summary>READ: outer+0x100 suppresses unloading (0x800A8C00). GUESS-high: a ride preview
        /// or test controller; its exact UI meaning remains unestablished.</summary>
        bool PreviewActive { get; }
        /// <summary>READ: 0x800A8BD4. Pop ALL passengers in reverse order into state 22, destroy the
        /// empty vehicle, compact the pointer array and decrement VehicleCount.</summary>
        void UnloadVehicle(int index);
    }

    /// <summary>READ: 0x800A67F4/0x800A6960/0x800A6A58. ⭐ TWO CLOCKS: the ride enables unloading
    /// after 2*duration ticks; vehicles become eligible only after duration laps (0x800AAA20).</summary>
    public sealed class PathedRide
    {
        public const int LoadCadence = 20;
        public const int UnloadCadence = 10;
        /// <summary>READ: 0x800A69F8 shifts slot 91 left one.</summary>
        public const int RunTicksPerDuration = 2;
        /// <summary>READ: A+0xF2, incremented and compared as a signed halfword.</summary>
        public short RunTicks { get; set; }

        /// <summary>READ: no partial-load timeout or empty-queue dispatch in 0x800A67F4.</summary>
        public AttractionStatus LoadTick(IPathedRideWorld world)
        {
            if (world.NowTick % LoadCadence != 0) return AttractionStatus.Loading;
            if (world.Riders < world.Capacity)
            {
                var head = world.QueueHead;
                if (head != null && head.State == VisitorState.WaitingInQueue) world.BoardHeadGuest();
                return AttractionStatus.Loading;
            }
            // READ: even the last boarding waits until the NEXT cadence to set off.
            RunTicks = 0;
            return AttractionStatus.Running;
        }

        /// <summary>READ: 0x800A6978 closes an incomplete track before touching the counter.</summary>
        public AttractionStatus RunTick(IPathedRideWorld world)
        {
            if (!world.TrackConnected) return AttractionStatus.ClosedByPlayer;
            RunTicks = unchecked((short)(RunTicks + 1));
            return RunTicks >= unchecked(world.Duration * RunTicksPerDuration)
                ? AttractionStatus.Unloading : AttractionStatus.Running;
        }

        /// <summary>READ: reused in statuses 4/5/6, so an empty broken ride must not reopen here.</summary>
        public static AttractionStatus UnloadTick(AttractionStatus status, IPathedRideWorld world)
        {
            if (world.NowTick % UnloadCadence != 0) return status;
            if (world.Riders == 0)
                return status == AttractionStatus.Unloading ? AttractionStatus.Loading : status;
            for (int i = 0; i < world.VehicleCount; i++)
            {
                if (world.VehicleReadyToUnload(i) && !world.PreviewActive) world.UnloadVehicle(i);
                // ⚠ DO NOT FIX: destruction compacts the array but the original still increments i
                // (0x800A6ADC..AEC). The adjacent vehicle waits for a later scan.
            }
            return status;
        }
    }

    /// <summary>READ: the vehicle's lap counter at +0x2C, incremented on route-position wrap
    /// (0x800AA99C..0x800AAA3C). Movement and route length belong to the world.</summary>
    public sealed class PathedRideVehicle
    {
        public sbyte Laps { get; set; }
        public bool ReadyToUnload { get; set; }

        /// <summary>READ: called for a route wrap, not an animation completion.</summary>
        public void CompleteLap(int duration)
        {
            Laps = unchecked((sbyte)(Laps + 1));
            if (Laps >= duration) ReadyToUnload = true;
        }
    }
}
