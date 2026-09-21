using System;
using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>The bus timetable. Guests do not trickle in at a rate — a bus arrives on a fixed loop that
    /// nothing in the park can speed up or slow down, and the park decides only how full it is.
    ///
    /// ⭐ THIS IS A SCHEDULER, NOT AN ACCUMULATOR, AND THE DIFFERENCE IS THE WHOLE DESIGN. The obvious
    /// implementation of "guests arrive faster when your park is better" is a rate that fills a counter until
    /// it crosses a threshold and emits someone. That is not this game, and building it that way would feel
    /// wrong in a way that is very hard to trace back: a better park would shorten the gaps, when really it
    /// should leave the gaps alone and put more people on each bus. An empty park does not get people staying
    /// away; it gets a bus that turns up on time with nobody on it.
    ///
    /// ✅ MEASURED (tinyclaw, live, PAL): spawn-to-spawn is 694 sim ticks, over 7 consecutive gaps reading
    /// 694 694 694 695 697 694 694. Building a second ride did NOT change the cadence — 11 buses before and
    /// 10 after, same interval — it changed the load from 1 guest to 2.</summary>
    public sealed class BusSchedule
    {
        /// <summary>Ticks between one bus and the next. ✅ MEASURED at 694 on the shipped data. The ±1..3
        /// jitter in the observed gaps is the root-counter step, not a designed variation, so this is a
        /// constant rather than a range.</summary>
        public const int DefaultIntervalTicks = 694;

        /// <summary>⚠ NOT THE SAME THING AS THE INTERVAL. The gaps between guests actually *admitted* were
        /// measured at 705 704 704 703 705 671 704 641. That is NOT the bus being held and not a walk latency
        /// that "creeps then snaps back": it is the TURNSTILE'S OWN BEAT. 0x800592CC runs once a sim tick and
        /// fires lane L only when `(clock &amp; 31) == L`, so a payment can land on one tick in 32 per lane,
        /// with no phase relation to the bus at all. 694 ≡ 22 (mod 32), so each cycle's first payer waits ten
        /// ticks longer than the last until it drops a whole slot: 704, 704, 704, 672, and round again. The
        /// proof it is not a hold is in the same run — the SPAWN gaps were 694 694 694 695 697 694 694, and a
        /// held bus would have stretched those too.</summary>
        public const int ObservedAdmissionGapNote = DefaultIntervalTicks;

        public int IntervalTicks { get; }
        public long LastArrivalTick { get; private set; }
        public int BusesArrived { get; private set; }

        public BusSchedule(int intervalTicks = DefaultIntervalTicks)
        {
            if (intervalTicks <= 0) throw new ArgumentOutOfRangeException(nameof(intervalTicks));
            IntervalTicks = intervalTicks;
            LastArrivalTick = 0;
        }

        /// <summary>Advance to <paramref name="tick"/> and report whether a bus arrives on it. Asked once per
        /// tick, in step with <see cref="ParkClock"/> — a caller that jumps the clock skips arrivals silently,
        /// which is the same trap ParkClock.Advance(int) documents.</summary>
        public bool IsArrivalTick(long tick)
        {
            if (tick <= LastArrivalTick || tick - LastArrivalTick < IntervalTicks) return false;
            LastArrivalTick = tick;
            BusesArrived++;
            return true;
        }
    }

    /// <summary>One attraction's contribution to the park's draw. <paramref name="Type"/> is the record's own
    /// type byte — 1 coaster, 2 feature, 3 ride, 4 shop, 5 sideshow, 6 track ride, 7 tour ride — because the
    /// score branches on it through a seven-entry jump table and the kinds are not scored alike. The dispatch
    /// is `sltiu (type − 1), 7` (0x80067478), so type 0 and anything from 8 up fall through and score like a
    /// SHOP rather than being skipped.
    ///
    /// ⚠ <see cref="Intensity"/> IS NOT ONE QUANTITY. The score reads it through a virtual (this-adjust at
    /// class record +0x1A8, function at +0x1AC) and the classes bind it differently: a ride's is 0x800A0594
    /// and is RECOMPUTED per call from the record and that ride's own sliders rather than stored, while a
    /// sideshow's is 0x800B7788 and a different formula altogether. Passing a record's base intensity for
    /// both is the port's approximation and will drift the moment sliders move.</summary>
    public readonly struct AttractionDraw
    {
        public readonly int Type;
        public readonly int Level;
        public readonly int Intensity;
        public readonly bool BuiltToday;   // age <= 1 game day
        public AttractionDraw(int type, int level, int intensity, bool builtToday)
        { Type = type; Level = level; Intensity = intensity; BuiltToday = builtToday; }
    }

    /// <summary>How many guests step off a bus.
    ///
    /// Source: fable read this off the instructions and tinyclaw confirmed the outputs live. The shape is
    ///     headCount = min( capacity - guestsNow,
    ///                      20 - lanes,
    ///                      floor((S + bonus) * 0x1333 / divisor) )
    /// with S = 10 + sum of per-attraction terms, and S = 0 for a park with nothing in it.</summary>
    public static class BusLoad
    {
        /// <summary>⚠ THE +10 THAT WAS NEARLY MISSED, AND IT IS THE DIFFERENCE BETWEEN A WORKING PARK AND A
        /// DEAD ONE. An earlier report had the score as just the sum of per-attraction terms. Without this
        /// base, a 16-day-old level-0 ride scores 10..14, the head-count floors to ZERO, and the park receives
        /// no guests at all while looking entirely correct. With it, 20..24 gives 1 guest a bus, which is what
        /// the running game does. SOURCED to `addiu s6,zero,0xa`, and corroborated by an independent output:
        /// forcing the bonus to 100 gives 7 guests a bus, which needs S >= 117 — 10+10..14+100 = 120..124
        /// clears it, and 0+10..14+100 = 110..114 would give 6.</summary>
        public const int BaseScore = 10;

        /// <summary>The multiplier in the head-count expression. SOURCED.</summary>
        public const int Numerator = 0x1333;   // 4915

        /// <summary>⚠⚠ OPEN — NOT MEASURED, AND DELIBERATELY NOT GUESSED IN CODE. The divisor's shipped value
        /// has not been read out. It can be BOUNDED from two measured input/output pairs: S in 20..24 yields
        /// 1 and S in 120..124 yields 7, which together pin it to roughly 76,182 &lt; divisor &lt;= 84,257.
        /// 0x14000 (81,920) sits in that window and is the kind of number a programmer picks — which is
        /// exactly why it is not hard-coded here. A plausible constant inside a derived range is still a
        /// guess, and one that would silently reprice every arrival in the game.
        ///
        /// Callers pass it explicitly. The tests pin the measured PAIRS instead, so they keep their meaning
        /// whatever the true divisor turns out to be.</summary>
        public const int DerivedDivisorLowerExclusive = 76_182;
        public const int DerivedDivisorUpperInclusive = 84_257;

        /// <summary>Park draw score, 0x80067400. Returns 0 for an empty park — not <see cref="BaseScore"/>.
        /// The base is added only once something exists, which is why "empty park" and "park with one bad
        /// ride" are genuinely different states rather than differing by a little.
        ///
        /// ⭐⭐ EVERY ATTRACTION IS SCORED THROUGH A DIE ROLL, AND THAT IS WHERE MOST OF THE SCORE LIVES.
        /// <code>
        ///   S = 0                                                  if there are no attractions
        ///   S = 10 + Σ (rand(10) + 20 + term + young) / divisor     one term per attraction
        ///       roll    = rand(10), RE-ROLLED PER ATTRACTION        0x80067448
        ///       term    = Level × Intensity  for a ride, coaster, track ride or tour ride   0x800674C8
        ///               = Intensity          for a sideshow (no level)                      0x800674A4
        ///               = 0                  for a shop, and for anything unrecognised      0x80067538
        ///       young   = 20 when (age &lt;&lt; 12) / 2024 &lt; 4, age in WHOLE DAYS, so exactly {0, 1}:
        ///                 age 1 gives 4096/2024 = 2 and passes, age 2 gives 8192/2024 = 4 and fails  0x80067520
        ///       divisor = 10 for a FEATURE, otherwise 2             0x80067454 / 0x80067534
        /// </code>
        /// The three ride getters: age from 0x8009EDBC (now − attraction+0xF4), level from 0x8009F5E0
        /// (attraction+0xF6, a byte, whose setter is the next routine along), intensity from the class
        /// record's slot at +0x1A8.
        ///
        /// ⚠ THE PREVIOUS PORT OF THIS WAS `s += Level × Intensity` AND IT MADE EVERY NEW PARK DEAD. The term
        /// was right; everything around it was missing. A freshly built ride is level 0 (placement sets it, and
        /// the upgrade path is `if level &gt;= 3 return; level++`), so `Level × Intensity` is exactly zero, the
        /// score stayed at the bare base of 10, the head-count floored, and the bus arrived on time with nobody
        /// on it forever. What actually carries a new park is the **roll**: 20..29 halved is 10..14, which is
        /// precisely the contribution the live measurement recorded for a 16-day-old level-0 ride.
        ///
        /// ⭐ AND IT EXPLAINS THE UNEXPLAINED. An earlier report withdrew its story for why batch sizes varied
        /// (5, 5, 4, 6, 3, 6 guests with the park unchanged) and left the pattern open. The score is re-rolled
        /// per attraction on every call, so the load genuinely varies bus to bus with nothing else changing.
        ///
        /// <paramref name="rng"/> is the game's `rand(10)`; passing null takes the LOW end of every roll, which
        /// is a floor rather than a sample and is only there so an empty-park check needs no dice.</summary>
        public static int ParkScore(IReadOnlyList<AttractionDraw> attractions, IRandomSource rng = null)
        {
            if (attractions == null || attractions.Count == 0) return 0;
            int s = BaseScore;
            foreach (var a in attractions)
            {
                int roll = (rng?.Next(10) ?? 0) + 20;
                int divisor = 2, young = 0, term = 0;
                switch (a.Type)
                {
                    case 1: case 3: case 6: case 7:                 // the four ride kinds share one branch
                        // ⚠ THE BONUS IS ALWAYS ADDED; IT IS THE +20 THAT IS YOUNG-ONLY. The instruction adding
                        // level×intensity sits in a branch delay slot and therefore executes on BOTH paths. A
                        // delay slot is not inside the branch it follows, and reading it as though it were is an
                        // easy way to invent a condition the machine does not have.
                        term = a.Level * a.Intensity;
                        if (a.BuiltToday) young = 20;
                        break;
                    // ⚠ A sideshow has no level to multiply by — and its "intensity" is a DIFFERENT virtual
                    // from a ride's (0x800B7788 against 0x800A0594), not the same number left unmultiplied.
                    case 5: term = a.Intensity; break;
                    case 2: divisor = 10; break;                    // a feature is worth a fifth of a ride
                }
                s += (roll + term + young) / divisor;
            }
            return s;
        }

        /// <summary>⚠ THE POPULATION CAP COUNTS RIDES ONLY — NOT SHOPS, FEATURES OR SIDESHOWS. This is
        /// the whole reason a park stops filling, so getting the denominator wrong changes the ceiling and
        /// nothing else, silently.
        ///
        /// READ at 0x800691F0. It zeroes two accumulators on the stack and makes FIVE identical passes
        /// through the tally helper 0x8006914C, each pass handed one category's catalogue count:
        /// <code>
        ///   0x8006A214 -> world record +0x10, passed with kind 3   NonPathedRide  (flat rides)
        ///   0x8006A274 -> +0x30,              kind 6               PathedRide     (track rides)
        ///   0x8006A244 -> +0x20,              kind 7               TourRide
        ///   0x8006A2A4 -> +0x40,              kind 8               TrackUpgrade
        ///   0x8006A2D4 -> +0x50,              kind 1               RollerCoaster
        ///   cap = 25 + 75 * built / total     0x800692B0..0x800692FC  (5*t0, then (x&lt;&lt;4)-x = 75*t0)
        /// </code>
        /// ✅ THE PAIRINGS ARE CORROBORATED BY A SECOND, INDEPENDENT SITE. 0x8006A158 is the generic
        /// "catalogue count for kind" dispatcher — `kind - 1` bounded below 7, jump table 0x800E16B8 — and
        /// its table maps kind 1→+0x50, 2→+0x60, 3→+0x10, 4→+0x70, 5→+0x80, 6→+0x30, 7→+0x20. Every kind
        /// the cap passes lands on the field the cap reads, so the five categories above are named by the
        /// game's own mapping rather than by my reading order. The type names are the CONTEXT_* strings at
        /// 0x800DDDD4 (rides.md §0.1): 1 RollerCoaster, 2 Feature, 3 NonPathedRide, 4 Shop, 5 SideShow,
        /// 6 PathedRide, 7 TourRide, 8 TrackUpgrade.
        ///
        /// ⭐ WHAT IS ABSENT IS THE FINDING. Fields +0x60 (Feature), +0x70 (Shop) and +0x80 (SideShow)
        /// exist, have getters, and the cap never calls them. A hundred shops do not raise the ceiling by
        /// one guest.
        ///
        /// ⚠ AND NOTE WHAT "BUILT" COUNTS. The tally helper loops i = 0..count-1 over the category's
        /// catalogue and adds `0x80069E30(mgr, kind, i) != 0 ? 1 : 0` — a walk of the live object list that
        /// returns HOW MANY instances exist, collapsed to a bit. So it is distinct catalogue ENTRIES with at
        /// least one built instance, not instances. Eight of one ride is one.</summary>
        ///
        /// ✅ AND TYPE 8 IS EMPTY, WHICH IS WHY ITS ABSENCE FROM THE PORT'S CATALOGUE COSTS NOTHING. A
        /// census of every definition record on the disc — all 488 files with the 0x96 header and a type
        /// byte, which is TWO extractions of the same 244 records, so halve it — gives 12 coaster, 91
        /// feature, 59 flat, 37 shop, 33 sideshow, 8 track, 4 tour and **0 TrackUpgrade**. It is kept in
        /// the predicate anyway, because the binary passes kind 8 and a category that is empty today is
        /// not a category that may be dropped.
        public static bool CountsTowardCap(int type)
            => type == 1 || type == 3 || type == 6 || type == 7 || type == 8;

        /// <summary>25 at the bottom, 100 once every ride in the catalogue has been built at least once.</summary>
        public const int CapBase = 25, CapSpan = 75;

        /// <summary>The cap itself. Both arguments are counted over <see cref="CountsTowardCap"/> types only.
        /// ⚠ The catalogue count is NOT in TPW.BIN: 0x800DDDC4 is a table of four per-world records that live
        /// in BSS and are filled by the scenario loader, so every word of it reads zero in the image. The
        /// caller supplies it.</summary>
        public static int PopulationCap(int entriesBuilt, int entriesInCatalogue)
            => CapBase + CapSpan * entriesBuilt / Math.Max(1, entriesInCatalogue);

        /// <summary>Guests on the next bus.</summary>
        public static int HeadCount(int parkScore, int capacity, int guestsNow, int lanes, int divisor, int bonus = 0)
        {
            if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
            if (parkScore <= 0) return 0;

            long draw = (long)(parkScore + bonus) * Numerator / divisor;

            // ⚠ I PREDICTED THIS TERM EXPLAINS THE OBSERVED BATCH SIZES. IT DOES NOT. The reasoning was that
            // capacity - guestsNow shrinks as the park fills, so the live observation "two rides fill early
            // buses with 2 and later ones drop back to 1" would just be this term becoming binding. I said so,
            // and offered the falsifier: it should track the GUEST COUNT, not the bus number.
            //
            // tinyclaw ran it. Batch 2 occurred at 5, 5, 4, 6, 3 and 6 guests; batch 1 at 2, 3, 5, 4, 5, 6 and
            // 6. **Five and six each produce both answers**, so batch size is not a function of park
            // population. Worse for my story: the first two buses carry ONE guest when the park is at its
            // emptiest, which is backwards — most headroom should mean the biggest load.
            //
            // The term is still in the expression, SOURCED, and still caps the bus. What is withdrawn is my
            // claim that it explains the pattern. That was two real observations (a shrinking term exists; the
            // load falls over time) welded into a mechanism nobody had tested on its own.
            //
            // Current best guess, tinyclaw's and labelled as one: the score term, since ride age feeds the
            // score and the ride was brand new — which fits the early-low shape my story got backwards.
            // ⚠ NOT SUPPORTED, not refuted: the effect is one guest versus two, sampled up to 200 frames
            // before each bus. Do not build on either reading.
            long n = Math.Min(draw, (long)capacity - guestsNow);
            n = Math.Min(n, 20L - lanes);
            return n <= 0 ? 0 : (int)n;
        }
    }

    /// <summary>The bus itself: the four-phase machine the game runs once a sim tick, transcribed from
    /// 0x8005262C..0x800527D4 (arrivals.md §2.2).
    ///
    /// ⭐ THE BUS IS THE CLOCK. Nothing about the park speeds it up — it counts down, drives on, drops its load
    /// on the phase 1→2 edge, waits, drives off, and starts again. <see cref="BusSchedule"/> is that loop's
    /// PERIOD (694 ticks); this is the loop, and it is what you have to run if you want to SEE a bus rather
    /// than just have guests appear.
    ///
    /// ⭐ WHERE IT DRIVES. The park draw places it at (<see cref="Pos"/> &gt;&gt; 8, 256, 1900) world units
    /// (0x80057CB0..0x80057CE4) and skips drawing it entirely while <see cref="A"/> is still counting — so the
    /// bus runs along X at a fixed z of 1900 and one tile up, from −15 tiles to 16 (the stop) and on to 60.
    ///
    /// ⚠ The hold: while a gate batch is mid-admission (batch == 1) the bus does not move at all, and it will
    /// not clear its own batch flag until it has driven past x = 25. That is the only park-dependent term in
    /// the period, and it is why measured ADMISSION gaps read 705 rather than 694.</summary>
    public sealed class BusRoute
    {
        /// <summary>The bus's model: folio entry 90, a scenery-style pack of one model. Identified by the call
        /// that builds it (0x800351BC at 0x800508AC) being the same one the four GATE packs go through with
        /// 87, 86, 85 and 88 — so the id it takes is the folio entry.</summary>
        public const int ModelEntry = 90;

        /// <summary>0x800E0F0C: where each phase drives to, in tiles. −15 is off-screen left, 16 is the stop,
        /// 60 is off-screen right.</summary>
        public static readonly int[] Stops = { -15, 16, 16, 60 };
        /// <summary>[0x80102D40]: the phase the machine wraps after.</summary>
        public const int Wrap = 4;
        /// <summary>The long wait between buses (A) and the short one at each phase change (B), in time units.</summary>
        public const int LongWait = 0xC8000, ShortWait = 0x50000;
        /// <summary>The bus has to be past 25.0 tiles before it lets the gate start another batch.</summary>
        public const int BatchClearPos = 0x190000;

        public int A, B, Phase, Pos, Target;

        /// <summary>The gate batch, [0x80103958]. The bus itself only cycles it 0 → 2 (on reaching the stop)
        /// → 0 (once past 25.0 tiles); the value that HOLDS the bus is 1, and only `admit()` (0x80052148, run
        /// once a tick before the machine) sets that — the first tick somebody is standing at the gate in
        /// state 46 while the batch is 0. It clears when the last of them has crossed, or when eleven have
        /// crossed and the bus is at the stop, which is what bounds a hold.
        ///
        /// ⚠ DEAD IN THIS PORT UNTIL THE TURNSTILE IS WIRED. Nothing here feeds the gate's waiting/admitted
        /// counters, so this never reaches 1 and the hold branch never fires. The core already has the
        /// routine (VisitorEntrance's Turnstile.Admit matches 0x80052148 line for line); what is missing is
        /// calling it once a tick before <see cref="Step"/> against the same word, and feeding guest arrivals
        /// 14, 15 and 16 into the counters. ⭐ A LEAVER HOLDS THE BUS EXACTLY LIKE AN ARRIVAL — the waiting
        /// count is people AT the gate in either direction.</summary>
        public int Batch;

        public BusRoute() => Reset();

        /// <summary>Game start, 0x8005084C..0x800508A8: phase 1, parked off-screen, the long wait running.</summary>
        public void Reset()
        {
            Phase = 1; A = LongWait; B = 0; Batch = 0;
            Pos = Stops[0] << 16; Target = Stops[1] << 16;
        }

        /// <summary>Whether the bus is on the road at all: the draw skips it while A is counting
        /// (0x80057C98).</summary>
        public bool OnRoad => A <= 0;

        /// <summary>Its position in world units, which is what the draw uses.</summary>
        public int WorldX => Pos >> 8;

        /// <summary>✅ CHECKED AGAINST THE MEASUREMENT: run with the emulator's own time step (δ = 9947) this
        /// machine arrives every **694 ticks exactly**, which is what tinyclaw measured live (694 694 694 695
        /// 697 694 694 — the ±1..3 there is the root counter, not the machine). At the nominal hardware step
        /// (10082) it gives 687, matching arrivals.md's closed form of ≈688. The port runs at 10081, so its
        /// bus is 687 ticks, 27.5 seconds.</summary>
        /// <summary>One sim tick. <paramref name="delta"/> is the game's own time step
        /// ([0x80103A90], ≈10081 a tick). Returns true on the tick the bus DROPS ITS LOAD — the phase 1→2 edge,
        /// and only with the park open and nothing carried on the cursor.
        ///
        /// ⚠ <paramref name="carryingFromCatalogue"/> IS NOT A GATE THING, despite sitting next to the batch.
        /// It is `held` = [0x80103940] (0x80059080), set when a catalogue object is picked up with the cursor
        /// (0x80058FE0) and cleared when it is placed or cancelled. While it is set the bus still runs and
        /// still arrives on time — it just arrives EMPTY, because the arrival condition at 0x80052720 tests
        /// it. Building something costs you the bus you were waiting for.</summary>
        public bool Step(int delta, bool parkOpen, bool carryingFromCatalogue)
        {
            if (A > 0) { A -= delta; return false; }
            if (B > 0) { B -= delta; return false; }
            if (Phase == 2)
            {
                if (Batch == 0) Batch = 2;
                else if (Batch == 1) return false;     // a gate batch is being admitted: the bus waits
            }
            int speed = Math.Clamp((Target - Pos) >> 3, 0x100, 0x1000);
            Pos += (int)((long)speed * delta >> 12);
            if (Batch == 2 && Pos > BatchClearPos) Batch = 0;
            if (Pos < Target) return false;
            bool arrived = Phase == 1 && parkOpen && !carryingFromCatalogue;
            if (Phase != 2) B = ShortWait;
            // ⚠ THE GAME READS PAST ITS OWN TABLE HERE AND GETS AWAY WITH IT. `tgt = table[phase] << 16` runs
            // before the wrap check, so on the last phase it reads table[4] — one word past a four-word table —
            // and the wrap two instructions later overwrites the result. Harmless on a PSX; in C# it is an
            // IndexOutOfRangeException that would kill the park 27 seconds in. Skipped rather than clamped,
            // because clamping would invent a value the game never uses.
            if (Phase < Stops.Length) Target = Stops[Phase] << 16;
            Phase++;
            if (Phase > Wrap)
            {
                Phase = 1; A = LongWait;
                Pos = Stops[0] << 16; Target = Stops[1] << 16;
            }
            return arrived;
        }
    }
}
