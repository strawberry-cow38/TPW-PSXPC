using System;

namespace TPW.Sim
{
    /// <summary>The visitor states this port models, by the original's own numbers.
    ///
    /// ⚠ THE NUMBERS ARE THE INTERFACE. The original dispatches on a raw state byte (P+0x2D) and the
    /// findings name states by number throughout, so these carry their real values rather than being a
    /// tidy 0..n sequence. Renumbering them would silently break every cross-reference to behaviour.md.
    /// States are added with their documented number, not appended; the enum is not in numeric order
    /// because each section of the findings was ported in turn.</summary>
    public enum VisitorState
    {
        Idle = 0,
        /// <summary>Pushed by Idle roll 1; immediately becomes <see cref="RandomWander"/> (§2.7).</summary>
        Wander = 1,
        RandomWander = 5,
        /// <summary>Make major decision (§2.2) -- pick a ride, shop or exit.</summary>
        MajorDecision = 6,
        /// <summary>Walking to a bin, purpose 19 (§2.1 roll 3).</summary>
        WalkToBin = 11,
        /// <summary>Watching an entertainer (§2.9); pushed by the needs update, not by Idle.</summary>
        WatchEntertainer = 28,
        /// <summary>Using a shop or stall (§2.3 purpose 0). Its own handler is in a section of the
        /// findings not yet read, so only the transition INTO it is modelled.</summary>
        UsingAttraction = 35,
        /// <summary>Joining a ride's queue (§2.4).</summary>
        JoiningQueue = 41,
        /// <summary>Shuffling forward within a queue (§2.4).</summary>
        ShuffleForward = 19,
        /// <summary>Standing in a queue waiting (§2.4).</summary>
        WaitingInQueue = 18,
        /// <summary>Walking a single waypoint (state 3, 0x800932C8). Shuffle forward sets it directly with
        /// one waypoint at the new slot; the path-ready message sets it after a pathfind (§0 item 5).</summary>
        WalkToWaypoint = 3,
        /// <summary>⚠ NEVER ENTERED IN THIS BUILD. behaviour.md §2.4: no SetState or PushState with 4 and
        /// no raw write of the state byte anywhere in TPW.BIN, checked across the whole image. The name
        /// table has it, nothing sets it, and nothing in this port does either. Kept at its number so the
        /// enum still matches the table.</summary>
        OnRide = 4,
        /// <summary>Aboard and hidden; the ride owns the guest until it unloads (§2.4).</summary>
        Loading = 21,
        /// <summary>Applying the ride's or building's effect on the way off (§2.4).</summary>
        Unloading = 22,
        /// <summary>Walking back to the ride's entrance point after riding (§2.4).</summary>
        GotoEntrance = 23,
        /// <summary>Taken out of a queue: walking to the ride's leave point (§2.4).</summary>
        RemovedFromQueue = 58,
        Vomiting = 29,
        /// <summary>Pathing to the park exit -- the leaving state (§2.6).</summary>
        LeavingPark = 38,
        // ── the park entrance and exit, behaviour.md §2.6 ──
        /// <summary>Just spawned: path to the spawn side of the gate, purpose 15 (§2.6).</summary>
        SpawnToGate = 36,
        /// <summary>At the turnstile: pay, or turn round (§2.6).</summary>
        PayEntryFee = 37,
        /// <summary>Path to my slot in a turnstile lane, purpose 11 (§2.6).</summary>
        WalkToLaneSlot = 42,
        /// <summary>One waypoint to my recomputed lane slot, purpose 12 (§2.6).</summary>
        ShuffleInLane = 43,
        /// <summary>Head of a turnstile lane, waiting for the turnstile's tick (§2.6). No per-tick
        /// handler; message 6 from the turnstile is the only thing that moves the others behind it.</summary>
        LaneFront = 44,
        /// <summary>Through the turnstile: find the first path tile in +y and walk to it, purpose 13 (§2.6).</summary>
        WalkIn = 45,
        /// <summary>Outside the gate, waiting to be admitted (§2.6). No per-tick handler; message 9
        /// moves it on. Shared with the guard, which sits in the same 46 (§3.4).</summary>
        AtGate = 46,
        /// <summary>One waypoint across the gate line, purpose 16; arrival picks a lane or leaves (§2.6).</summary>
        PickLane = 47,
        /// <summary>Path to a random exit point, purpose 9; arrival despawns (§2.6).</summary>
        WalkOut = 48,
    }

    /// <summary>Everything Idle can decide to do on one tick, so a caller can act on the decision
    /// without this type needing a park to act on.</summary>
    public enum IdleAction
    {
        /// <summary>Stood still. The commonest outcome by far.</summary>
        Nothing,
        Leave,
        MakeMajorDecision,
        Wander,
        PeltEntertainer,
        WalkToBin,
        DropLitterBecauseNoBin,
        Vomit,
        DropLitterFromMisery,
    }

    /// <summary>A number source with the original's shape: `rand(n)` is `rand() % n` (0x800C2648).
    ///
    /// ⚠ INJECTED, NOT AMBIENT. Every branch in Idle is a dice roll, so a test that cannot fix the dice
    /// can only observe the common case and will report "passing" while five of the six branches have
    /// never run. This is the whole reason the state machine takes a source rather than calling a static.</summary>
    public interface IRandomSource
    {
        /// <summary>Uniform in [0, n). n is always positive in the original's call sites.</summary>
        int Next(int n);
    }

    /// <summary>The visitor's stats and the clamping the original does to them.
    ///
    /// ⭐ ALL OF V+0x58..0x5F ARE CLAMPED TO 0..100, NOT WRAPPED. They are signed bytes and the game
    /// never touches them directly: 0x80092190 adds, 0x800921C0 subtracts and 0x800924F0 sets, and all
    /// three clamp. That matters because several effects push the same stat repeatedly -- a guest next
    /// to three litter piles takes -3 happiness three times -- and a byte that wrapped would turn a
    /// miserable guest delighted at exactly the wrong moment.</summary>
    public static class Stat
    {
        public const int Min = 0;
        public const int Max = 100;

        public static int Clamp(int v) => v < Min ? Min : v > Max ? Max : v;
        public static int Add(int stat, int delta) => Clamp(stat + delta);
        public static int Sub(int stat, int delta) => Clamp(stat - delta);
    }

    /// <summary>One guest.
    ///
    /// Fields carry the findings' own names where a name is READ and a neutral one where it is not.
    /// behaviour.md §1 is explicit that V+0x5B, V+0x5C, V+0x5D and V+0x5E are unidentified needs with
    /// GUESS-low labels, so they are <see cref="NeedA"/>, <see cref="Boredom"/>, <see cref="RideDesire"/>
    /// and <see cref="NeedB"/> here rather than hunger and thirst. Naming a guess after a real need is
    /// how a guess becomes a fact nobody rechecks.
    ///
    /// ⚠ THE STATE STACK IS A STACK, and Idle uses both operations on it: rolls 0 and 1 PUSH (so the
    /// guest returns to Idle afterwards) while rolls 3 and 4 and the leave check SET (which zeroes the
    /// stack and does not return). Collapsing the two loses the distinction between "go and do this,
    /// then carry on" and "stop being idle".</summary>
    public sealed class Visitor
    {
        /// <summary>Spawn a guest with the constructor's own rolls (§2.12, ctor 0x8008C534).</summary>
        public static Visitor Spawn(IRandomSource rng, long nowTick)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            var v = new Visitor
            {
                Money = GuestSpending.StartingMoney(rng.Next(300)),
                Rubbish = rng.Next(40),
                Happiness = 50,
                Nausea = rng.Next(50),
                NeedA = rng.Next(70),
                Boredom = rng.Next(40),
                // ⚠ rand(100)*rand(100)/100 is NOT uniform: it is the product of two rolls, so it
                // clusters low. Written as the original writes it rather than as one rand(100).
                RideDesire = rng.Next(100) * rng.Next(100) / 100,
                NeedB = rng.Next(100) * rng.Next(100) / 100,
                Tiredness = rng.Next(50),
                WalkSpeed = rng.Next(15) + 15,
                ArrivedOnDay = nowTick,
                // ⚠ ROLL ORDER HERE IS A GUESS. The report gives V+0x50 as `now + rand(300)` at
                // spawn but not WHERE in the constructor it is drawn, and the position matters to
                // anything trying to reproduce the original's RNG stream tick for tick. Placed last
                // so it cannot disturb the fields whose order IS known.
                EntertainerNotBefore = nowTick + rng.Next(300),
            };
            // ONE roll for both speeds: the constructor writes rand(15)+15 to V+0x62 and then copies it
            // to V+0x60 (READ 0x8008C6CC..0x8008C704). Copied after the initializer so no die is added.
            v.NormalWalkSpeed = v.WalkSpeed;
            return v;
        }

        Visitor() { }

        public VisitorState State { get; private set; } = VisitorState.Idle;

        // ⚠ THE 0..100 STATS CLAMP ON ASSIGNMENT, deliberately. The original never writes these
        // bytes directly either -- every touch goes through an add/sub/set helper that clamps
        // (0x80092190 / 0x800921C0 / 0x800924F0). A plain auto-property would let a caller store 500
        // happiness and have every threshold downstream read it as fine.
        /// <summary>V+0x48.</summary>
        public Money Money { get; set; }
        /// <summary>V+0x58, 0..100. At 90 or more the guest looks for a bin.</summary>
        public int Rubbish { get => _rubbish; set => _rubbish = Stat.Clamp(value); }
        int _rubbish;
        /// <summary>V+0x59, 0..100, starts at 50.</summary>
        public int Happiness { get => _happiness; set => _happiness = Stat.Clamp(value); }
        int _happiness;
        /// <summary>V+0x5A, 0..100. Above 92 the guest is sick.</summary>
        public int Nausea { get => _nausea; set => _nausea = Stat.Clamp(value); }
        int _nausea;
        /// <summary>V+0x5B -- an unidentified need. GUESS-low "hunger"; do not rely on that.</summary>
        public int NeedA { get => _needa; set => _needa = Stat.Clamp(value); }
        int _needa;
        /// <summary>V+0x5C -- GUESS-low "boredom". +5 when no ride can be found.</summary>
        public int Boredom { get => _boredom; set => _boredom = Stat.Clamp(value); }
        int _boredom;
        /// <summary>V+0x5D -- ride-related desire. Above 97 the guest speeds up to 30.</summary>
        public int RideDesire { get => _ridedesire; set => _ridedesire = Stat.Clamp(value); }
        int _ridedesire;
        /// <summary>V+0x5E -- the other unidentified need.</summary>
        public int NeedB { get => _needb; set => _needb = Stat.Clamp(value); }
        int _needb;
        /// <summary>V+0x5F, 0..100. At 99 the guest goes home.</summary>
        public int Tiredness { get => _tiredness; set => _tiredness = Stat.Clamp(value); }
        int _tiredness;

        /// <summary>V+0x60: rand(15)+15 at spawn, 15 in a queue, 30 while RideDesire > 97.</summary>
        public int WalkSpeed { get; set; }

        /// <summary>V+0x62: the speed the guest was born with. Unloading restores <see cref="WalkSpeed"/>
        /// from it after the queue lowered it to 15 (§2.4).</summary>
        public int NormalWalkSpeed { get; set; }

        /// <summary>V+0x54: the slow-clock reading at spawn. Time in park is `McAi+0x10 - this`.</summary>
        public long ArrivedOnDay { get; set; }

        /// <summary>P+0x2A, the thought bubble currently shown, or 0 for none.</summary>
        public int Bubble { get; set; }

        /// <summary>P+0x2B bit 0x04 (0x80093ECC sets it, 0x80093EB8 reads it): the guest is in a ride's
        /// queue member list. The can-join test lets a queued guest through the cap; messages 7 and 10
        /// and Unloading clear it (§2.4, §2.10).</summary>
        public bool InQueue { get; set; }

        /// <summary>P+0x2B bit 0x01 (0x80093EFC). Set on coming off a ride and on leaving a queue,
        /// cleared on boarding (state 21) and on reaching a type-2 building (§2.3, §2.4).
        /// ⚠ WHAT READS IT IS NOT ESTABLISHED. It is carried because the handlers write it, and it is
        /// named by its bit so nobody mistakes the name for a meaning.</summary>
        public bool Flag1 { get; set; }

        /// <summary>P+0x2E bits 3-7: 11 idle/walk, 12 vomit, 13 wander (§1). That it is an animation id
        /// is GUESS-medium; the values the handlers write are READ.</summary>
        public int Animation { get; set; }

        /// <summary>P+0x2E bits 0-2: facing. 0/2/4/6 by direction of travel while walking (§2.7), and
        /// rand(4)*2 when a queued guest fidgets (§2.4).</summary>
        public int Facing { get; set; }

        /// <summary>P+0x2C: the "wait until" deadline in ticks, and the decision cooldown.</summary>
        public long WaitUntil { get; set; }

        /// <summary>V+0x61: index into the 8-entry visitor-type table at 0x800F79E8, which supplies the
        /// ride preference the score matches against.</summary>
        public int VisitorType { get; set; }

        /// <summary>V+0x10: the per-guest offset that staggers the once-every-8-ticks decision, so a
        /// park full of guests does not re-plan in lockstep.</summary>
        public int DecisionStagger { get; set; }

        /// <summary>The id of whatever the guest most recently chose to head for.</summary>
        public int ChosenId { get; set; }

        readonly int[] _history = { -1, -1, -1, -1 };

        /// <summary>V+0x38..0x44: the last four attraction ids, most recent first, -1 when unused.
        /// The score divides by 5, 4, 3 and 2 by position, so the thing just ridden is punished hardest
        /// and the penalty fades rather than being a flat ban.</summary>
        public System.Collections.Generic.IReadOnlyList<int> RideHistory => _history;

        /// <summary>Push an id onto the four-slot history, dropping the oldest (0x8008CFF0).</summary>
        public void RememberRide(int id)
        {
            for (int i = _history.Length - 1; i > 0; i--) _history[i] = _history[i - 1];
            _history[0] = id;
        }

        /// <summary>V+0x50: `now + rand(300)` at spawn. The needs update refuses to stop a guest for an
        /// entertainer until the clock passes it, so a guest that has just walked in does not immediately
        /// stand and watch a show. Its meaning beyond that gate is unknown.</summary>
        public long EntertainerNotBefore { get; set; }

        /// <summary>P+0x2C: why the guest is walking somewhere, consumed on arrival.</summary>
        public Purpose Purpose { get; set; } = Purpose.Spent;

        /// <summary>V+0x28: whether the guest currently has something it is heading for.</summary>
        public bool HasTarget { get; set; }

        /// <summary>V+0x28 read as a NUMBER, which is what the turnstile states do with it (§1: "also
        /// used as a 16-bit scratch by purposes 11-16"). 0 after arriving at the spawn side (arrival 15),
        /// 1 after arriving at the exit side (arrival 14), and then, for a guest going in, the lane it
        /// rolled at arrival 16. The same word as <see cref="HasTarget"/>: state 37 and the leaving arm of
        /// arrival 16 store a whole zero to it (0x80090F0C, 0x8008E17C), so both views are cleared there.
        /// The guard's <c>GateDirection</c> is the same use of the same offset.</summary>
        public int GateScratch { get; set; }

        /// <summary>P+0x2B bit 0x20 (0x80094050 sets, 0x8009403C reads): the one retry a guest whose
        /// path to the exit failed is allowed. Message 2 with purpose 14 sets it after re-requesting with
        /// grass flags; message 1 clears it (0x8008F97C); a second failure with it set goes to Idle (§2.10).</summary>
        public bool ExitPathRetried { get; set; }

        readonly System.Collections.Generic.Stack<VisitorState> _stack = new();

        /// <summary>P+0x10, the state-stack depth. The needs update refuses to push a guest into
        /// "watch entertainer" once this reaches 2.</summary>
        public int StackDepth => _stack.Count;

        /// <summary>Replace the state and CLEAR the stack (SetState zeroes P+0x10).</summary>
        public void SetState(VisitorState s) { _stack.Clear(); State = s; }

        /// <summary>Enter a state the guest will return from.</summary>
        public void PushState(VisitorState s) { _stack.Push(State); State = s; }

        /// <summary>Return to whatever pushed us, or Idle if nothing did.</summary>
        public void PopState() => State = _stack.Count > 0 ? _stack.Pop() : VisitorState.Idle;
    }
}
