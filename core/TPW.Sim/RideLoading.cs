using System;

namespace TPW.Sim
{
    /// <summary>What loading and unloading needs of the ride and its queue (rides.md §4.2).</summary>
    public interface IRideLoadWorld
    {
        /// <summary>The park clock in ticks. The cadences below are `clock % n`, on the PARK's clock and
        /// not on a per-ride counter, so every ride in a park loads on the same ticks.</summary>
        long NowTick { get; }
        /// <summary>Riders on board, A+0xF0.</summary>
        int Riders { get; }
        /// <summary>Seats at this level, A+0xBC.</summary>
        int Capacity { get; }
        /// <summary>The guest at the front of the queue, or null. ⚠ Its STATE matters - see Load.</summary>
        Visitor QueueHead { get; }
        /// <summary>Guests still in the queue after the head.</summary>
        int QueueCount { get; }
        /// <summary>Take the head out of the queue and put it on the ride: the guest goes to state 21,
        /// is hidden and attached to the ride, and the rider count goes up.</summary>
        void BoardQueueHead();
        /// <summary>Send message 6 to everyone still queued, each with its own `rand(3)` stagger, so
        /// they shuffle forward at slightly different times rather than as one block.</summary>
        void ShuffleTheRestForward();
        /// <summary>The first rider steps off: state 22, placed at the ride's exit tile and put back on
        /// the map, and the rider count goes down.</summary>
        void UnloadFirstRider();
        /// <summary>McAi's total days, A+0xF8's yardstick. The partial-load timeout counts in DAYS.</summary>
        int TotalDays { get; }
    }

    /// <summary>A ride taking guests on and putting them off again (rides.md §4.2).
    ///
    /// ⭐ THE RIDE PULLS, THE QUEUE DOES NOT PUSH. Nothing in the guest's own machine puts it on a ride:
    /// it stands in state 18 until the RIDE decides to load it. That is why a queue with a stuck head
    /// stops the whole ride - see the state check in <see cref="Load"/>.
    ///
    /// ⭐ LOADING IS SLOW ON PURPOSE. One guest every 20 ticks on, one every 10 off, so a ride with
    /// eight seats spends 240 ticks - nearly ten seconds - just moving people, whatever its run length.
    /// Raising the capacity slider therefore raises throughput far less than it looks like it should,
    /// and that is the game's own arithmetic, not a port artefact.</summary>
    public static class RideLoading
    {
        /// <summary>One guest boards every 20 ticks (0.8 s).</summary>
        public const int LoadEveryTicks = 20;
        /// <summary>One guest steps off every 10 ticks (0.4 s).</summary>
        public const int UnloadEveryTicks = 10;
        /// <summary>Days the queue must have been empty before a part-full ride sets off anyway.</summary>
        public const int PartialLoadDays = 5;
        /// <summary>The state a guest must be in to be taken aboard: 18, waiting in the queue.</summary>
        public const VisitorState Boardable = VisitorState.WaitingInQueue;

        /// <summary>Status 10's tick. Returns the status to move to, or 10 to keep loading.
        ///
        /// <paramref name="queueEmptySince"/> is A+0xF8: the day the queue last had somebody in it. Pass
        /// it back in each tick; it is updated through <paramref name="queueEmptySince"/>.</summary>
        public static AttractionStatus Load(IRideLoadWorld world, ref int queueEmptySince)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            // The ride's own tick-10 keeps the clock pinned to today while anyone is queueing, so the
            // timeout below measures how long the queue has been EMPTY, not how long loading has taken.
            if (world.QueueCount > 0 || world.QueueHead != null) queueEmptySince = world.TotalDays;

            if (world.NowTick % LoadEveryTicks != 0) return AttractionStatus.Loading;

            if (world.Riders >= world.Capacity)
            {
                // Full: off it goes.
                return AttractionStatus.Running;
            }

            var head = world.QueueHead;

            // ⚠ THE HEAD MUST BE IN STATE 18, AND A HEAD IN ANY OTHER STATE BLOCKS THE WHOLE RIDE. A
            // guest still shuffling (19) stalls loading until it arrives; the game has no "skip him".
            // Reproduced: it is a real cause of a ride that looks open and takes nobody.
            if (head != null && head.State == Boardable)
            {
                world.BoardQueueHead();
                world.ShuffleTheRestForward();
                return AttractionStatus.Loading;
            }

            // Nobody boardable. A ride that has somebody on it and has had an empty queue for five game
            // days sets off part full rather than waiting for ever.
            if (world.Riders > 0 && world.TotalDays - queueEmptySince >= PartialLoadDays)
                return AttractionStatus.Running;

            return AttractionStatus.Loading;
        }

        /// <summary>Status 11's tick. Returns the status to move to, or 11 to keep unloading.</summary>
        public static AttractionStatus Unload(IRideLoadWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (world.NowTick % UnloadEveryTicks != 0) return AttractionStatus.Unloading;

            if (world.Riders <= 0) return AttractionStatus.Loading;

            world.UnloadFirstRider();
            return AttractionStatus.Unloading;
        }

        /// <summary>How long one full cycle takes, in ticks, ignoring the wait for guests: the load, the
        /// run and the unload. Useful for showing a throughput, and it is why the capacity slider is
        /// worth less than it looks - the run is per CYCLE, the loading is per RIDER.</summary>
        public static int CycleTicks(int capacity, int cyclesPerLoad, int phaseTicks)
            => capacity * LoadEveryTicks + cyclesPerLoad * phaseTicks + capacity * UnloadEveryTicks;
    }
}
