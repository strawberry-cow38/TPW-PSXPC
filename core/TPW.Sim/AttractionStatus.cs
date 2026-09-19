using System;

namespace TPW.Sim
{
    /// <summary>An attraction's status, by the original's own numbers (rides.md, jump tables 0x800E1340
    /// enter / 0x800E1370 tick).
    ///
    /// ⚠ 8 AND 9 ARE DEAD IN THIS BUILD. Both have tick code, and a census of all 33 SetStatus sites in
    /// TPW.BIN finds no writer for either. They are kept here with their numbers so the enum matches the
    /// jump table, and so that nobody "tidies up the gap" by renumbering what follows.</summary>
    public enum AttractionStatus
    {
        JustPlaced = 0,
        UnderConstruction = 1,
        /// <summary>Running for a ride, open for anything else.</summary>
        Running = 2,
        ClosedByPlayer = 3,
        AboutToBreakDown = 4,
        BrokenDown = 5,
        UnderRepair = 6,
        /// <summary>A reopen command rather than a resting state: it moves on the same tick.</summary>
        Reopen = 7,
        /// <summary>Dead in this build - tick code exists, nothing ever sets it.</summary>
        Dead8 = 8,
        /// <summary>Dead in this build - see <see cref="Dead8"/>.</summary>
        Dead9 = 9,
        /// <summary>Loading for a ride; build-complete for everything else.</summary>
        Loading = 10,
        Unloading = 11,
    }

    /// <summary>What the status machine needs of the attraction and the park.</summary>
    public interface IAttractionWorld
    {
        /// <summary>Rides load and unload guests; shops, features and sideshows do not.</summary>
        bool IsRide { get; }
        /// <summary>The build animation pass has finished (status 1's tick).</summary>
        bool BuildAnimationComplete { get; }
        /// <summary>Reliability, A+0xB4, 0..100. Exactly zero is what lets 4 fall to 5.</summary>
        int Reliability { get; set; }
        /// <summary>Cycles run since opening, A+0xF2.</summary>
        int CyclesRun { get; set; }
        /// <summary>Cycles allowed before unloading, A+0xC0.</summary>
        int CyclesPerLoad { get; }
        /// <summary>True once every rider is off (status 11 leaves when empty).</summary>
        bool IsEmpty { get; }
        /// <summary>A mechanic has been assigned and is on the way.</summary>
        bool MechanicAssigned { get; }
        /// <summary>Post one of the breakdown messages.</summary>
        void PostMessage(int id);
        /// <summary>Throw every rider and queuer off (statuses 4 and 5 do this).</summary>
        void EjectEveryone();
        /// <summary>Clear the smoke effect when reopening.</summary>
        void ClearSmoke();
    }

    /// <summary>The attraction status machine (rides.md "Placement → running").
    ///
    /// ⭐ THE RIDE DRIVES ITSELF FROM PLACEMENT TO RUNNING. Nothing outside pushes it along: placement
    /// sets 0, the tool's slot 39 sets 1, status 1's own tick sets 10 when the build animation ends, and
    /// 10's tick sets 2. A port that puts a construction timer in the placement tool instead would have
    /// to keep the two in step forever; here the ride owns its own opening.
    ///
    /// ⭐ THIS IS ALSO THE "OPEN TO GUESTS" RULE. Guests may join a queue only in 2, 10 or 11 (slot 86),
    /// which is the same flag <see cref="AttractionCandidate.OpenToGuests"/> carries into the ride
    /// score - so a ride under construction or broken down is not merely unattractive, it is invisible
    /// to the chooser.</summary>
    public static class AttractionLifecycle
    {
        /// <summary>"Your ride is about to break down!"</summary>
        public const int MessageAboutToBreak = 0x3E;
        /// <summary>Broken down, and you have no mechanics.</summary>
        public const int MessageBrokenNoMechanics = 0x3F;
        /// <summary>Broken down, and all your mechanics are busy.</summary>
        public const int MessageBrokenAllBusy = 0x40;
        /// <summary>Broken down, and a mechanic is on his way.</summary>
        public const int MessageBrokenMechanicComing = 0x41;

        /// <summary>Reliability below this while running sends a ride to <see cref="AttractionStatus.AboutToBreakDown"/>.</summary>
        public const int AboutToBreakReliability = 10;

        /// <summary>Whether a guest may join this attraction's queue (slot 86, 0x800632D8).</summary>
        public static bool OpenToGuests(AttractionStatus status)
            => status == AttractionStatus.Running
            || status == AttractionStatus.Loading
            || status == AttractionStatus.Unloading;

        /// <summary>The enter hook. Returns the status actually settled on, because two of them move
        /// again immediately.</summary>
        public static AttractionStatus Enter(AttractionStatus status, IAttractionWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            switch (status)
            {
                case AttractionStatus.Running:
                    world.CyclesRun = 0;
                    return status;

                case AttractionStatus.AboutToBreakDown:
                    world.PostMessage(MessageAboutToBreak);
                    // ⚠ A ride with no life left throws everyone off on ENTERING this status, before it
                    // has actually broken. The warning and the ejection are the same event.
                    if (world.Reliability <= 0) world.EjectEveryone();
                    return status;

                case AttractionStatus.BrokenDown:
                    world.PostMessage(world.MechanicAssigned ? MessageBrokenMechanicComing
                                                             : MessageBrokenNoMechanics);
                    world.EjectEveryone();
                    return status;

                case AttractionStatus.Reopen:
                    // ⭐ REOPEN IS A COMMAND, NOT A STATE. The base sets 2 and a ride then immediately
                    // overrides that with 10, so a ride never rests here for even one tick - and a ride
                    // reopens through LOADING while a shop reopens straight to open.
                    world.ClearSmoke();
                    if (!world.IsRide) return AttractionStatus.Running;
                    world.Reliability = 100;
                    return AttractionStatus.Loading;

                default:
                    return status;
            }
        }

        /// <summary>The tick hook. Returns the next status, or the same one to stay put.</summary>
        public static AttractionStatus Tick(AttractionStatus status, IAttractionWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            switch (status)
            {
                case AttractionStatus.UnderConstruction:
                    // The ride leaves construction when the BUILD ANIMATION ends, not on a timer. Its
                    // length is the animation's, so a longer build model means a longer build.
                    return world.BuildAnimationComplete ? AttractionStatus.Loading : status;

                case AttractionStatus.Loading:
                    // ⚠ ONLY RIDES WAIT HERE. Everything else flips to open on its first tick in 10, so
                    // for a shop this status lasts exactly one tick and is really "build complete".
                    return world.IsRide ? status : AttractionStatus.Running;

                case AttractionStatus.Running:
                    if (world.CyclesRun >= world.CyclesPerLoad) return AttractionStatus.Unloading;
                    return status;

                case AttractionStatus.Unloading:
                    return world.IsEmpty ? AttractionStatus.Loading : status;

                case AttractionStatus.AboutToBreakDown:
                    // ⚠ EXACTLY ZERO, not "low". A ride sitting at reliability 1 stays in the warning
                    // state indefinitely; only a mechanic moves it on.
                    if (world.Reliability == 0) return AttractionStatus.BrokenDown;
                    return status;

                // 0 animates only and is moved on by the build tool's slot 39; 3, 5 and 6 have no tick
                // work at all and are moved by the player or a mechanic; 8 and 9 are dead.
                default:
                    return status;
            }
        }

        /// <summary>Whether this status waits for something outside itself rather than moving on its own.
        /// Useful to a host deciding what to show the player.</summary>
        public static bool WaitsForSomeoneElse(AttractionStatus status)
            => status == AttractionStatus.JustPlaced          // the build tool
            || status == AttractionStatus.ClosedByPlayer      // the player
            || status == AttractionStatus.BrokenDown          // a mechanic
            || status == AttractionStatus.UnderRepair;        // a mechanic
    }
}
