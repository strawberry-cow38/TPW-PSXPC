using System;
using System.Collections.Generic;
using System.Linq;

namespace TPW.Sim
{
    public interface ICoasterMovementWorld
    {
        /// <summary>READ: 0x800BDD0C's global 20.12 delta, not seconds or NowTick.</summary>
        int MovementDelta { get; }
        /// <summary>READ: live slot 89, 0x800B2080..20C0.</summary>
        int SpeedSlider { get; }
        /// <summary>READ: live slot 91, 0x800B22A0..2B4.</summary>
        int Duration { get; }
        /// <summary>READ: boolean 0x80053D98, halves displacement AFTER its minimum clamp.</summary>
        bool HalfSpeed { get; }
    }

    /// <summary>Host supplies queue/list transactions and already decoded definition/model inputs.
    /// Simulation owns track traversal, the eight-train pool, batches, counters and unloading.</summary>
    public interface ICoasterSimulationWorld : ICoasterMovementWorld
    {
        uint NowTick { get; }
        AttractionStatus Status { get; }
        int Capacity { get; }
        int BoardingBatchSize { get; }
        /// <summary>READ: signed record+0xC8 (0x800B1E90), or the nonzero override at
        /// 0x80103354. 0x800B0084 shifts the selected value by eight.</summary>
        int LaunchSpeed { get; }
        Visitor QueueHead { get; }
        bool AdvanceStationAnimation();
        /// <summary>READ: 0x800B025C queue unlink, state 21, rider-list insertion and shuffle.
        /// The simulation increments its occupancy after this host transaction.</summary>
        void BoardGuest(Visitor guest);
        /// <summary>READ: 0x8009DE88 state-22 exit/list/position transaction.</summary>
        void ExitGuest(Visitor guest);
    }

    /// <summary>READ: motion fields of the 0x88-byte train at 0x800B1F84. Existing CoasterTrain
    /// remains the completion controller; its AfterMovement signature is unchanged.</summary>
    public sealed class CoasterTrainMotion
    {
        // Host diagnostics: stable pool identity, not a PSX field or an active-list index.
        public int PoolId { get; internal set; }
        public long PositionChanges { get; internal set; }
        public CoasterTrain Control { get; } = new();
        public CoasterTrackNode Segment { get; set; } // +0x20
        public CoasterTrackNode LaunchSegment { get; private set; } // +0x24
        public int Speed { get; set; } // +0x18, distance units per delta=4096.
        public int Distance { get; set; } // +0x1C, eight fractional bits.
        public CoasterSample Pose { get; private set; }
        /// <summary>READ: +0x08/+0x0C/+0x10 is the forward unit vector, written by 0x800B28F8..2938.
        /// Headless scheduling GUESS-high: sample after each movement and at launch, replacing
        /// the original draw's cache write. Live draw/update ordering remains unmeasured.</summary>
        public CoasterVector Tangent { get; set; }
        public int LastMovementFraction { get; private set; }

        public const int Gravity = -4096; // READ: 0x80103378.
        public const int Friction = 32; // READ: 0x8010337C.
        public const int ApproachBrake = 512; // READ: 0x80103380.
        public const int MinimumSpeed = 2048; // READ: 0x80103384 (also minimum displacement).
        public const int MaximumSpeed = 20480; // READ: 0x80103388, scaled by slot 89 / 100.
        public const int PreviewDelay = 240; // READ: 0x80103390, compared with elapsed >> 12.

        public void Start(CoasterTrack track, int launchSpeed)
        {
            Segment = LaunchSegment = track.Launch;
            Speed = unchecked(launchSpeed << 8); // READ: 0x800B0084.
            // ⚠ DO NOT FIX: divide BEFORE multiplying by 0xC00; 0x800B2E0C..30.
            Distance = unchecked(((Segment.Length << 8) / CoasterMath.One) * 0xC00);
            Control.StillOnLaunchSegment = true;
            Control.Laps = 0;
            Control.ReadyToUnload = false;
            LastMovementFraction = 0;
            RefreshPose(track);
        }

        /// <summary>READ: 0x800B1F84..22C8. No collision stop or ReadyToUnload brake is present.
        /// Returns false for the preview dwell or zero-length early return.</summary>
        public bool Advance(CoasterTrack track, ICoasterMovementWorld world, int elapsed)
        {
            if (Control.StillOnLaunchSegment && Control.IsPreview && (elapsed >> 12) < PreviewDelay)
                return false; // READ: 0x800B1FA0..FD4.
            unchecked
            {
                if (Segment.Piece.Kind != 1) // READ: special shape skips BOTH force branches, 0x800B1FE8.
                {
                    if (Segment == LaunchSegment.Previous) Speed -= ApproachBrake;
                    else Speed = Speed - Friction + (Tangent.Y * Gravity >> 12);
                }
                int maximum = (MaximumSpeed * world.SpeedSlider) / 100;
                if (Speed > maximum) Speed = maximum;
                if (Speed < MinimumSpeed) Speed = MinimumSpeed; // READ: maximum THEN minimum.
                int step = (int)((uint)(Speed * world.MovementDelta) >> 12); // READ: srl, 0x800B2108.
                if (step < MinimumSpeed) step = MinimumSpeed;
                if (world.HalfSpeed) step >>= 1;
                Distance += step;
                if (Segment.Length == 0) return false; // distance already advanced, 0x800B2148.
                int fraction = CoasterMath.Fraction(Distance, Segment.Length);
                LastMovementFraction = fraction;
                // ⚠ DO NOT FIX: ONE handoff, not a while loop or a distance-preserving subtraction.
                if (fraction < 0)
                {
                    Segment = Segment.Previous ?? throw new InvalidOperationException("Missing previous track link.");
                    Distance = ((Segment.Length << 8) / CoasterMath.One) * (fraction + CoasterMath.One);
                }
                else if (fraction >= CoasterMath.One)
                {
                    Segment = Segment.Next ?? throw new InvalidOperationException("Missing next track link.");
                    Distance = ((Segment.Length << 8) / CoasterMath.One) * (fraction - CoasterMath.One);
                    Control.StillOnLaunchSegment = false; // READ: 0x800B2218, forward only.
                }
                // New segment identity; OLD fraction. This is exactly the existing signature.
                Control.AfterMovement(Segment == track.Launch, fraction, world.Duration);
                RefreshPose(track);
                return true;
            }
        }

        void RefreshPose(CoasterTrack track)
        {
            Pose = track.Sample(Segment, CoasterMath.Fraction(Distance, Segment.Length));
            Tangent = Pose.Tangent;
        }
    }

    /// <summary>Engine-free host adapter and per-update scheduler. READ: 0x800B0C94 orders
    /// movement/ejection, elapsed time, partial dispatch, then status tick. It never measures
    /// a whole-ride trip time. Shared lifecycle/wear remains the host's existing responsibility.</summary>
    public sealed class CoasterSimulation : IRollerCoasterWorld
    {
        public const int TrainCount = 8; // READ: 0x800B1B20..38, eight objects at stride 0x88.
        readonly ICoasterSimulationWorld host;
        readonly Stack<CoasterTrainMotion> free = new();
        readonly List<CoasterTrainMotion> active = new();
        readonly Dictionary<CoasterTrain, List<Visitor>> passengers = new();
        readonly List<Visitor> pending = new();
        public CoasterTrack Track { get; }
        public RollerCoaster Controller { get; } = new();
        public IReadOnlyList<CoasterTrainMotion> Trains => active.AsReadOnly();
        public int Elapsed { get; private set; } // READ: outer+0xE2C, delta accumulator.
        public int Riders { get; private set; }
        public int PendingPassengers => pending.Count;
        public int FreeTrains => free.Count;
        // Observations, not inputs to dispatch/completion. Keep totals after a train is recycled.
        public long Updates { get; private set; }
        public long PositionChanges { get; private set; }
        public long Dispatches { get; private set; }
        public long CompletedTrips { get; private set; }
        public long CompletedLaps { get; private set; }

        public CoasterSimulation(CoasterTrack track, ICoasterSimulationWorld world)
        {
            Track = track ?? throw new ArgumentNullException(nameof(track));
            host = world ?? throw new ArgumentNullException(nameof(world));
            for (int i = 0; i < TrainCount; i++) free.Push(new CoasterTrainMotion { PoolId = i });
        }

        public void Update()
        {
            Updates++;
            if (Track.Connected && Track.ChecksPass)
            {
                foreach (var train in active)
                {
                    var before = train.Pose.Position;
                    train.Advance(Track, host, Elapsed);
                    if (train.Pose.Position != before) { train.PositionChanges++; PositionChanges++; }
                }
            }
            else
            {
                // READ: invalid route ejects ordinary active batches and THEN pending guests,
                // 0x800B00C4/0x800B013C. It does not freeze passengers on a decorative route.
                EjectPassengers();
            }
            Elapsed = unchecked(Elapsed + host.MovementDelta);
            Controller.UpdateDispatch(this);
            switch (host.Status)
            {
                case AttractionStatus.Loading: RollerCoaster.LoadTick(this); break;
                case AttractionStatus.Running: RollerCoaster.RunTick(this); break;
                case AttractionStatus.Unloading: RollerCoaster.UnloadTick(this); break;
                case AttractionStatus.AboutToBreakDown:
                    RollerCoaster.RunTick(this); break; // READ: 0x800B10B4/0x800B10D0, both hooks.
                case AttractionStatus.BrokenDown:
                case AttractionStatus.UnderRepair:
                    RollerCoaster.UnloadTick(this); break; // READ: 0x800B1020..10E8.
            }
        }

        /// <summary>Host lifecycle/track-edit boundary; reuse the same guest exit and pool transactions
        /// as invalid-track ejection (0x800B00C4/013C). Preview remains exempt.</summary>
        public void EjectPassengers()
        {
            foreach (var train in active.ToArray())
                if (!train.Control.IsPreview) UnloadTrain(train.Control);
            foreach (var guest in pending) { host.ExitGuest(guest); Riders--; }
            pending.Clear();
        }

        uint IRollerCoasterWorld.NowTick => host.NowTick;
        bool IRollerCoasterWorld.TrackConnected => Track.Connected;
        bool IRollerCoasterWorld.PieceChecksPass => Track.ChecksPass;
        bool IRollerCoasterWorld.PreviewTrainActive => active.Any(t => t.Control.IsPreview);
        int IRollerCoasterWorld.Capacity => host.Capacity;
        Visitor IRollerCoasterWorld.QueueHead => host.QueueHead;
        int IRollerCoasterWorld.BoardingBatchSize => host.BoardingBatchSize;
        bool IRollerCoasterWorld.FreeTrainAvailable => free.Count != 0;
        bool IRollerCoasterWorld.AdvanceStationAnimation() => host.AdvanceStationAnimation();
        IReadOnlyList<CoasterTrain> IRollerCoasterWorld.ActiveTrains => active.Select(t => t.Control).ToArray();

        void IRollerCoasterWorld.BoardHeadGuest()
        {
            var guest = host.QueueHead;
            host.BoardGuest(guest);
            pending.Add(guest);
            Riders++;
        }

        void IRollerCoasterWorld.DispatchPendingPassengers()
        {
            if (pending.Count == 0 || free.Count == 0) return;
            var train = free.Pop();
            train.Control.IsPreview = false;
            train.Start(Track, host.LaunchSpeed);
            Dispatches++;
            active.Insert(0, train); // READ: intrusive push-front, 0x800B1730 → 0x800B1BD4.
            passengers.Add(train.Control, new List<Visitor>(pending));
            pending.Clear();
            Elapsed = 0; // READ: 0x800B023C, even while other trains travel.
        }

        void IRollerCoasterWorld.UnloadTrain(CoasterTrain train) => UnloadTrain(train);
        void UnloadTrain(CoasterTrain train)
        {
            var motion = active.Find(t => t.Control == train);
            if (motion == null || train.IsPreview) return;
            var guests = passengers[train];
            if (train.ReadyToUnload) { CompletedTrips++; CompletedLaps += train.Laps; }
            for (int i = guests.Count - 1; i >= 0; i--) { host.ExitGuest(guests[i]); Riders--; }
            passengers.Remove(train);
            active.Remove(motion);
            free.Push(motion); // READ: 0x800B043C, recycle rather than allocate a ninth train.
        }
    }
}
