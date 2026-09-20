using System;

namespace TPW.Sim
{
    /// <summary>What the cycle needs to know about the model that is playing on an attraction.
    ///
    /// READ: flat rides count completions of the selected model phase (0x8009CA60).
    /// Tour, track and coaster status-2 handlers override this rule; their model lengths alone do
    /// not determine their trip times. See findings/animation-phases.md §4.</summary>
    public interface IRideAnimation
    {
        /// <summary>READ: the mapped sub-entry index from the attraction's handle at A+0x18.
        /// 0x80030364 maps the logical phase A+0x64 through the container's signed map. This property
        /// represents its RESULT, not the logical map index. Negative results fall back to mesh 0
        /// (0x80065DC0..DC8).</summary>
        int CurrentPhase { get; }

        /// <summary>READ: the u16 at +0x38 of this sub-entry's 88-byte runtime descriptor
        /// (0x800300B8 → 0x800314D4). The builder copies it from u16(mesh+0) at 0x8002C5FC..604;
        /// the descriptor itself is not stored on disc. See findings/ride-phase-lengths.md.
        ///
        /// ⚠ THE RECORDS ARE 88 BYTES, NOT 40. rides.md §4.3 said 40, which is the size of a BONE
        /// record in the same file; the phase stride is computed twice in the image as
        /// `(((n*3) &lt;&lt; 2) - n) &lt;&lt; 3` = 88n (0x8003010C and 0x8002FE58). Getting it wrong reads
        /// a plausible number out of the middle of the wrong record.
        ///
        /// ⚠ THE MESH INDEX WRAPS. 0x800300E4 divides by container+4, the mesh count, and keeps the
        /// remainder. This is separate from the logical phase-map count at container+0x1C.</summary>
        int PhaseLength(int phase);

        /// <summary>The build animation's length (0x80065994 → 0x80033AA4, keyed on A+0x6D). A different
        /// resource from the running phases, and used only while the attraction is under
        /// construction.</summary>
        int BuildAnimationLength { get; }
    }

    /// <summary>The animation clock that drives a ride's cycle (0x800658D8), and with it the end of
    /// construction and the end of a run.
    ///
    /// ⭐ A FLAT RIDE'S RUN LENGTH COMES FROM ITS ANIMATION. READ: status 2 counts
    /// PHASES of the model's animation, and the ride unloads when it has run <see cref="Attraction"/>'s
    /// cycles-per-load of them. So a longer model animation is literally a longer ride, and the
    /// duration slider multiplies whatever the artist drew.
    ///
    /// ⭐ THE SAME CLOCK ENDS CONSTRUCTION. Under status 1 the accumulator runs against the BUILD
    /// animation's length instead, which is why <see cref="AttractionLifecycle"/> leaves construction
    /// when the animation finishes rather than on a timer - this is the code behind that.
    ///
    /// ⚠ IT IS WALL-CLOCK, NOT PER-TICK. The increment is a delta off root counter 2 (0x800BC290),
    /// shifted left 7, so it advances with real time and not with the 25 Hz sim. Two things follow that
    /// a per-tick port would lose: the cap below, and the fact that a stalled frame makes the animation
    /// JUMP rather than slow down.</summary>
    public sealed class RideCycle
    {
        /// <summary>The most the accumulator may gain in one call (0x8006594C). A frame longer than
        /// this is clamped, so a long stall costs animation time instead of fast-forwarding through a
        /// whole phase.</summary>
        public const int MaxDelta = 0x4000;

        /// <summary>The fixed point the phase length is compared in: length &lt;&lt; 12.</summary>
        public const int FixedShift = 12;

        /// <summary>A+0x60: how far into the current phase, in 1.12 fixed point.</summary>
        public int Accumulator { get; set; }

        /// <summary>A+0xF2: phases completed since the run began. Reset when the ride starts running.</summary>
        public int CyclesRun { get; set; }

        /// <summary>The target the accumulator is racing, in 1.12 (0x80065920).
        ///
        /// ⚠ THE MINUS ONE IS ONLY ON THE RUNNING PATH. A running phase races `length - 1`, the build
        /// animation races its length as-is - the decrement sits in the delay slot of the branch that
        /// skips the construction case (0x80065914), so it applies to one and not the other. It is one
        /// instruction and it is easy to hand to both.</summary>
        public static int TargetFor(AttractionStatus status, IRideAnimation animation)
        {
            if (animation == null) throw new ArgumentNullException(nameof(animation));

            if (status == AttractionStatus.UnderConstruction)
                return animation.BuildAnimationLength << FixedShift;

            int phase = animation.CurrentPhase;
            if (phase < 0) phase = 0;
            return (animation.PhaseLength(phase) - 1) << FixedShift;
        }

        /// <summary>One call of 0x800658D8: advance the clock and say whether a phase just ended.
        ///
        /// <paramref name="rawDelta"/> is what 0x800BC290 returned - counter ticks since this
        /// attraction was last stepped, already shifted left 7.</summary>
        public bool Advance(AttractionStatus status, IRideAnimation animation, int rawDelta,
                            bool halfSpeed)
        {
            int target = TargetFor(status, animation);

            int d = rawDelta;
            if (halfSpeed) d >>= 1;          // arithmetic: a negative delta stays negative

            // ⚠ THE CLAMP IS A SIGNED TEST (`slti`, 0x80065938/0x80065940), so it only ever catches a
            // delta that is too LARGE. A negative raw delta
            // passes straight through and winds the accumulator BACKWARDS. Reproduced rather than
            // guarded. The source is a software IRQ count; ordinary hardware-counter wrap is not
            // evidence that this negative path occurs (findings/animation-phases.md §3).
            if (d > MaxDelta) d = MaxDelta;

            Accumulator += d;

            // Unsigned (`sltu`, 0x80065960): an accumulator driven negative by the paragraph above
            // reads as enormous and completes the phase instantly.
            if ((uint)Accumulator < (uint)target) return false;

            Accumulator = 0;
            return true;
        }

        /// <summary>The status-2 tick (0x8009CAF8..0x8009CB48): advance, count the phase, and say
        /// whether the run is over.
        ///
        /// ⚠ THE COMPARISON IS `&gt;=` ON A SIGNED HALFWORD against a 32-bit cycles-per-load
        /// (0x8009CB18/0x8009CB24), and the count is incremented BEFORE it is tested, so a ride set to
        /// one cycle runs exactly one phase.</summary>
        public bool RunTick(IRideAnimation animation, int cyclesPerLoad, int rawDelta, bool halfSpeed)
        {
            if (Advance(AttractionStatus.Running, animation, rawDelta, halfSpeed)) CyclesRun++;
            return CyclesRun >= cyclesPerLoad;
        }

        /// <summary>Entering status 2 restarts the count (0x8009C454 and its siblings write 0).</summary>
        public void StartRun()
        {
            CyclesRun = 0;
            Accumulator = 0;
        }
    }
}
