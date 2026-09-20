namespace TPW.Sim
{
    /// <summary>READ: the tour's vehicle states, from jump tables 0x800E5BF4/0x800E5C1C.
    /// Names describe the calls they make; flight geometry stays with the world.</summary>
    public enum TourTransportState
    {
        Loading = 0, Departing = 1, Touring = 2, Returning = 3,
        Approaching = 4, Docking = 5, Settling = 6, Unloading = 7, Retiring = 8,
    }

    /// <summary>What the tour controller needs of the park (findings/ride-classes.md §2).</summary>
    public interface ITourRideWorld
    {
        uint NowTick { get; }
        Visitor QueueHead { get; }
        int TransportCount { get; }
        int DockedPassengers { get; }
        /// <summary>READ: 0x800A2318. Remove the head, set guest state 21, hide it, put it in the
        /// ride list, increment riders, shuffle the remaining queue, and append to the transport.</summary>
        void BoardHeadGuest();
        /// <summary>READ: pop the LAST passenger (0x800A3264), remove from the ride list, set state
        /// 22, place at the exit, call 0x80053C04, decrement riders. Empty transport does nothing.</summary>
        void UnloadLastGuest();
        /// <summary>READ: set transport+0x3D (0x800A27C0). This requests retirement, not a normal trip.</summary>
        void RequestRetirement();
        /// <summary>READ: call 0x800A3368(1), including its destination/speed setup.</summary>
        void Depart();
    }

    /// <summary>READ: control flow established in ride-classes.md §2. No animation-phase run clock:
    /// the status-2 hook at 0x800A1334 is empty. The transport does the travelling.</summary>
    public static class TourRide
    {
        /// <summary>READ: the helper reads rec+0xD4, disc values 5/9/10, then discards it and
        /// returns 3 (0x800A335C). ⚠ DO NOT FIX: neither the record nor the ride's eight-seat
        /// maximum is the capacity of an individual transport.</summary>
        public const int TransportSeats = 3;
        /// <summary>READ: unsigned global tick modulo 20 (0x800A1200..220 / 0x800A1350..370).</summary>
        public const int GuestCadence = 20;

        /// <summary>0x800A11E8, with rides.md §7's queue-empty departure rule RETAINED where it
        /// disagrees. Called with a docked loading transport; see rides.md §0 item 12.</summary>
        public static void LoadTick(ITourRideWorld world)
        {
            if (world.NowTick % GuestCadence == 0)
            {
                var head = world.QueueHead;
                if (head != null && head.State == VisitorState.WaitingInQueue
                    && world.DockedPassengers < TransportSeats)
                    world.BoardHeadGuest();
                return;
            }

            // READ: this branch is on NON-loading ticks. ⚠ Report retention: the binary also
            // requires an EMPTY transport (0x800A12D4); rides.md says queue-empty dispatch. Keep
            // the report here until that disagreement is accepted, including partly filled loads.
            if (world.QueueHead != null) return;
            if (world.TransportCount >= 2) world.RequestRetirement();
            else world.Depart();
        }

        /// <summary>READ: 0x800A133C. One passenger per global cadence, in reverse boarding order.</summary>
        public static void UnloadTick(ITourRideWorld world)
        {
            if (world.NowTick % GuestCadence == 0) world.UnloadLastGuest();
        }

        /// <summary>READ: 0x800A0F10..FEC, after stepping the transports. Statuses 4..9 are preserved.
        /// No docked vehicle means running, even while another transport is being allocated.</summary>
        public static AttractionStatus StatusAfterMovement(AttractionStatus status,
                                                            TourTransportState? docked)
        {
            if ((int)status >= 4 && (int)status < 10) return status;
            if (docked == TourTransportState.Loading) return AttractionStatus.Loading;
            if (docked == TourTransportState.Unloading) return AttractionStatus.Unloading;
            return AttractionStatus.Running;
        }
    }

    /// <summary>World answers the geometry and owns the dock pointer; the vehicle keeps its lap byte.</summary>
    public interface ITourTransportWorld
    {
        int Passengers { get; }
        int Duration { get; }
        bool DepartureBlocked { get; }
        bool RetirementRequested { get; }
        bool DockOccupied { get; }
        AttractionStatus RideStatus { get; }
        /// <summary>READ: 0x800A382C, destination following; true on arrival.</summary>
        bool MoveToDestination();
        /// <summary>READ: 0x800A3C70, approach each axis by one fifth, snapping when integer
        /// rounding stops progress. True once all three axes have settled.</summary>
        bool SettleAtDock();
        /// <summary>READ: 0x800A3368. Includes assigning/releasing the dock on states 3/1,
        /// selecting a destination and setting the state's speed.</summary>
        void EnterState(TourTransportState state);
        void RemoveTransport();
    }

    /// <summary>READ: control portion of 0x800A2A4C. Duration counts destination arrivals in state 2,
    /// not the vehicle mesh animation. See ride-classes.md §2 for the movement boundary.</summary>
    public sealed class TourTransport
    {
        public TourTransportState State { get; set; }
        /// <summary>READ: transport+0x3C is an unsigned byte.</summary>
        public byte Laps { get; set; }
        /// <summary>READ: 0x800A2BE8/0x800A2C00. ⚠ DO NOT FIX: a busy dock writes 200 into the
        /// lap byte and sends the vehicle round again. It does not wait motionless for a berth.</summary>
        public const byte BusyDockLaps = 200;

        void Enter(TourTransportState state, ITourTransportWorld world)
        {
            State = state;
            if (state == TourTransportState.Departing) Laps = 0;
            world.EnterState(state);
        }

        /// <summary>READ: state decisions; per-tick steering/speed smoothing is host-side.</summary>
        public void Tick(ITourTransportWorld world)
        {
            switch (State)
            {
                case TourTransportState.Loading:
                    if ((world.Passengers >= TourRide.TransportSeats || world.RetirementRequested)
                        && !world.DepartureBlocked)
                        Enter(TourTransportState.Departing, world);
                    break;
                case TourTransportState.Departing:
                    if (world.MoveToDestination())
                        Enter(world.RetirementRequested ? TourTransportState.Retiring
                                                       : TourTransportState.Touring, world);
                    break;
                case TourTransportState.Touring:
                    if (!world.MoveToDestination()) break;
                    Laps = unchecked((byte)(Laps + 1));
                    if (Laps >= world.Duration)
                    {
                        if (!world.DockOccupied) { Enter(TourTransportState.Returning, world); break; }
                        Laps = BusyDockLaps;
                    }
                    Enter(TourTransportState.Touring, world);
                    break;
                case TourTransportState.Returning:
                case TourTransportState.Approaching:
                case TourTransportState.Docking:
                    if (world.MoveToDestination()) Enter(State + 1, world);
                    break;
                case TourTransportState.Settling:
                    if (world.SettleAtDock()) Enter(TourTransportState.Unloading, world);
                    break;
                case TourTransportState.Unloading:
                    if (world.Passengers == 0 && ((int)world.RideStatus < 4 || (int)world.RideStatus >= 10))
                        Enter(TourTransportState.Loading, world);
                    break;
                case TourTransportState.Retiring:
                    if (world.MoveToDestination()) world.RemoveTransport();
                    break;
            }
        }
    }
}
