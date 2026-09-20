using System;

namespace TPW.Sim
{
    /// <summary>What wearing out needs of the ride (rides.md §6).</summary>
    public interface IRideWearWorld
    {
        /// <summary>The speed slider, A+0xB8, 0..100ish. Linear in the wear rate.</summary>
        int SpeedSlider { get; }
        /// <summary>Riders on board, A+0xF0.</summary>
        int Riders { get; }
        /// <summary>Seats at this upgrade level. The load fraction's denominator.</summary>
        int MaxSeats { get; }
        /// <summary>The level block's wear multiplier: 5 at level 0, 3 at 1, 2 at 2. An upgrade is the
        /// only thing that changes it, and it more than halves the wear.</summary>
        int WearMultiplier { get; }
        /// <summary>0x80059A9C: the mode in which nothing wears out at all.</summary>
        bool NoWear { get; }
        /// <summary>Post one of the ride messages.</summary>
        void PostMessage(int id);
    }

    /// <summary>Rides wearing out, breaking down and finally being condemned (rides.md §6).
    ///
    /// ⭐⭐ WEAR IS WHY MECHANICS EXIST, and this is the piece that was missing: the mechanic class,
    /// the breakdown statuses and the repair were all built with nothing to make a ride break.
    ///
    /// ⭐ IT COSTS THE RIDE ITS LIFE, NOT JUST ITS RELIABILITY. Every 15 points of reliability a ride
    /// loses takes one off a lifetime that NOTHING ever restores - not a repair, not an upgrade. A full
    /// 100 → 10 cycle costs six, so a Crazy Ape (lifetime 45) survives seven breakdowns and is then
    /// condemned for good. Running a ride fast and full is not a trade against downtime; it is spending
    /// the ride.</summary>
    public sealed class RideWear
    {
        /// <summary>Wear is applied on every fourth tick of status 2, not every tick.</summary>
        public const int WearEveryTicks = 4;

        /// <summary>Reliability is 20.12 fixed point: 100 points is 0x64000.</summary>
        public const int FullReliability = 100 << 12;

        /// <summary>A running ride at or below this breaks (0x8009D0FC). 0x9FFF is a hair under 10
        /// points - the test is `&lt;= 0x9FFF`, not `&lt; 10`.</summary>
        public const int BreakdownThreshold = 0x9FFF;

        /// <summary>One point of lifetime per fifteen points of reliability crossed.</summary>
        public const int LifetimeBandPoints = 15;

        /// <summary>The load fraction above which a ride wears faster, 0.8 in 20.12.</summary>
        public const int HeavyLoadFrom = (4096 * 8) / 10;

        /// <summary>"One of your rides has become too old and has been condemned."</summary>
        public const int MessageCondemned = 0x99;

        /// <summary>A+0xB4, 20.12.</summary>
        public int Reliability { get; set; } = FullReliability;
        /// <summary>A+0x68: how many more 15-point bands this ride has left in it. From the record.</summary>
        public int Lifetime { get; set; }
        /// <summary>True once <see cref="Lifetime"/> has hit zero and the message has been posted.</summary>
        public bool Condemned { get; private set; }

        long _tick;

        /// <summary>Reliability in whole points, which is what the lifetime bands are counted in.</summary>
        public int ReliabilityPoints => Reliability >> 12;

        /// <summary>0x800A0250: how fast this ride is wearing, in 20.12.
        ///
        /// ⚠ THE REPORT'S EXPRESSION FOR THE HEAVY-LOAD TERM IS AMBIGUOUS AND THIS IS THE READING THAT
        /// WORKS. §6.1 writes it as `((L − 0.8)×4096/64)²/4096`, which under integer arithmetic gives
        /// either zero or nonsense depending on where you put the fixed point. Taking it as
        /// `t = (L − 0.8) / 64` in raw 20.12 units and then `L += t²` reproduces §6.1's OWN three
        /// worked rates exactly - 3.84, 1.25 and 5.09 - and its stated "+0.035 at full load" (144/4096).
        /// Three independent checks on one reading is why this is here rather than left as a hole; the
        /// function itself was not traced past its vtable calls, so it is marked GUESS-high, not READ.</summary>
        public static int RateFor(int speedSlider, int riders, int maxSeats, int wearMultiplier)
        {
            if (maxSeats <= 0) return 0;

            int s = speedSlider * 4096 / 100;
            // ⚠ At or above 100 the slider stops being linear: it is averaged with 1.0, so the top of
            // the range is compressed. Going from 50 to 100 does NOT double the wear.
            if (speedSlider >= 100) s = (s + 4096) / 2;

            int load = riders * 4096 / maxSeats;
            if (load >= HeavyLoadFrom)
            {
                int t = (load - HeavyLoadFrom) / 64;
                load += t * t;
            }

            return (s + load) / 2 * wearMultiplier;
        }

        /// <summary>One tick of a ride in status 2. Returns the status it should move to, or the status
        /// it was given.
        ///
        /// ⚠ THE WEAR STEP IS EVERY FOURTH TICK BUT THE BREAKDOWN TEST IS EVERY TICK, and the test runs
        /// FIRST (0x8009D0FC runs before anything else in the ride's update). A ride that crosses the
        /// threshold breaks on the next tick, not on the next wear step.</summary>
        public AttractionStatus Tick(AttractionStatus status, IRideWearWorld world, bool isTrackOrCoaster = false)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (status == AttractionStatus.Running && Reliability <= BreakdownThreshold)
            {
                // ⚠ FLAT RIDES WARN FIRST, TRACK RIDES AND COASTERS DO NOT. 0x800A66EC and 0x800B0A68
                // run the same test and go straight to 5, so a coaster never shows "about to break
                // down" - it is simply broken.
                if (isTrackOrCoaster) return AttractionStatus.BrokenDown;
                world.PostMessage(AttractionLifecycle.MessageAboutToBreak);
                return AttractionStatus.AboutToBreakDown;
            }

            if (status != AttractionStatus.Running) return status;

            if (++_tick % WearEveryTicks != 0) return status;
            if (world.NoWear) return status;

            int k = RateFor(world.SpeedSlider, world.Riders, world.MaxSeats, world.WearMultiplier);
            int before = Reliability;
            int after = Math.Max(0, before - (k >> 5));
            Reliability = after;

            SpendLifetime(before, after, world);
            return status;
        }

        /// <summary>The lifetime cost of a fall in reliability: one per 15-point band crossed.</summary>
        void SpendLifetime(int before, int after, IRideWearWorld world)
        {
            int bands = (before >> 12) / LifetimeBandPoints - (after >> 12) / LifetimeBandPoints;
            if (bands <= 0) return;

            Lifetime = Math.Max(0, Lifetime - bands);
            if (Lifetime != 0 || Condemned) return;

            // ⚠ AND THAT IS PERMANENT. Nothing resets the lifetime but placing a new ride or loading a
            // save, and a mechanic will not claim a ride whose lifetime is zero - so a condemned ride
            // cannot be repaired even though its repair would otherwise work. Demolish and rebuild.
            Condemned = true;
            world.PostMessage(MessageCondemned);
        }

        /// <summary>A repair (mechanic state 17): reliability back to full. ⚠ The LIFETIME is not
        /// touched, which is the whole point of it.</summary>
        public void Repaired() => Reliability = FullReliability;

        /// <summary>Whether a mechanic may claim this ride (0x80096C58): broken, and not condemned.</summary>
        public bool MechanicMayClaim(AttractionStatus status)
            => Lifetime != 0
            && (status == AttractionStatus.AboutToBreakDown || status == AttractionStatus.BrokenDown);

        /// <summary>How many more breakdown-and-repair cycles this ride has in it, at six lifetime a
        /// cycle - the figure rides.md quotes per ride.</summary>
        public int CyclesLeft => Lifetime / 6;
    }
}
