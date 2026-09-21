using System;
using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>One tile of a ride's queue path (ride+0x70; two bytes per entry, x then y, read with
    /// 0x8001A13C). Index 0 is the tile nearest the queue's origin -- that is the order 0x8009D57C walks
    /// the list in, and the only thing this port asks of the host's ordering.</summary>
    public readonly struct QueueTile
    {
        public QueueTile(int x, int y) { X = x; Y = y; }
        public int X { get; }
        public int Y { get; }
    }

    /// <summary>The guest messages the queue chain answers (§2.10), by the message vtable's own slot-1
    /// ids. The rest of the handler (1, 2, 4, 9) belongs to the walking and exit states.</summary>
    public enum QueueMessage
    {
        /// <summary>6: shuffle forward. param1 is a stagger, param2 the state to enter -- queues send 19,
        /// the turnstile sends 43. Only honoured in states 18 and 44.</summary>
        Shuffle = 6,
        /// <summary>7: you have been taken out of the queue. Sent by 0x8009E118 to the leaver.</summary>
        RemovedFromQueue = 7,
        /// <summary>10: the ride is emptying everyone out (demolish, 0x8009D8C4). Drop it all.</summary>
        Ejected = 10,
    }

    public enum JoinOutcome
    {
        /// <summary>Path accepted; the guest is in 11 waiting for it.</summary>
        Walking,
        /// <summary>The pathfinder refused. Still in 41, already listed and flagged; tries again next tick.</summary>
        PathRefused,
        /// <summary>Could not join but was already queued: taken out via message 7, now in 58.</summary>
        LeftQueue,
        /// <summary>Could not join and was not queued: target dropped, back to Idle.</summary>
        GaveUp,
    }

    public enum ShuffleOutcome
    {
        /// <summary>Not yet time to move.</summary>
        Waiting,
        /// <summary>One waypoint set at the new slot; now in 3.</summary>
        Stepping,
        /// <summary>The waypoint pool is empty. Still in 19 with purpose 10; tries again next tick.</summary>
        NoWaypoint,
        LeftQueue,
        GaveUp,
    }

    public enum WaitOutcome
    {
        Standing,
        /// <summary>Turned to face a random way and set the next fidget deadline.</summary>
        Fidgeted,
        /// <summary>Boredom went over 80: bubble, out of the queue, now in 58.</summary>
        LeftBored,
    }

    public enum UnloadOutcome
    {
        /// <summary>A ride (types 1, 3, 6, 7): reward applied, now in 23 heading for the entrance.</summary>
        RodeIt,
        /// <summary>A type-2 building with stock: used it, back to Idle.</summary>
        UsedFeature,
        /// <summary>A type-2 building with no stock: nothing happened, back to Idle.</summary>
        FeatureEmpty,
        /// <summary>Type 4 or 5: the purchase routine ran (§2.5, VisitorPurchase) and money moved,
        /// back to Idle.</summary>
        Bought,
        /// <summary>Type 4 or 5: the purchase routine ran and the guest did not buy or play (too dear,
        /// or could not afford it). Back to Idle just the same; the state-22 handler ignores the routine's
        /// return value. Distinguished here for tests, not by the original.</summary>
        DidNotBuy,
        /// <summary>No target, or a type the table has no arm for. The common effects still applied and
        /// the guest is in 23.</summary>
        NothingToUnload,
    }

    public enum EntranceOutcome
    {
        Walking,
        /// <summary>Refused: still in 23 with purpose 4, retries next tick.</summary>
        PathRefused,
        /// <summary>The ride has no entrance point: back to Idle, target kept.</summary>
        NoEntrance,
    }

    /// <summary>What the queue chain needs of the park. The target is always the guest's current one
    /// (V+0x28); every method is asked about that.
    ///
    /// ⚠ THE PATHFINDER AND THE MEMBER LIST STAY WITH THE HOST. The slot ARITHMETIC is here
    /// (<see cref="VisitorQueue.WalkSlot"/>), because it is read and testable; the list it walks, the
    /// waypoint pool and 0x800EC9F4 are not, and the host answers only what they answered.</summary>
    public interface IQueueWorld : IShopWorld
    {
        long NowTick { get; }
        /// <summary>The target's slot-16 type, or 0 with no target.</summary>
        int TargetType(Visitor guest);
        /// <summary>The target's status byte (+0x6E). Both slot 86 "open" and slot 20 "broken" are
        /// functions of it (rides.md §0 items 3 and 5) and are computed here, not asked for.</summary>
        AttractionStatus RideStatus(Visitor guest);
        /// <summary>Members of the queue list at ride+0xC4 (0x8009EE98).</summary>
        int QueueCount(Visitor guest);
        /// <summary>ride[0xF6], the upgrade level 0..3 (0x8009F5E0; rides.md §1.3).</summary>
        int UpgradeLevel(Visitor guest);
        /// <summary>Ride vtable slot 53, intensity 0..100 (rides.md §5).</summary>
        int Intensity(Visitor guest);
        /// <summary>Ride vtable slot 42 called with 0 (0x8009D5E8): the tile the HEAD of the queue stands
        /// on. GUESS-high that it is the queue's entrance tile; the mechanic walks to the same slot-42
        /// position when it comes to repair (§3.5).</summary>
        QueueTile QueueOrigin(Visitor guest);
        /// <summary>The queue path tile list at ride+0x70, index 0 nearest the origin.</summary>
        IReadOnlyList<QueueTile> QueuePath(Visitor guest);
        /// <summary>My position in the member list, or -1 if I am not in it.</summary>
        int QueueIndexOf(Visitor guest);
        /// <summary>0x8009F04C: put me on the end of the member list.</summary>
        void AppendToQueue(Visitor guest);
        /// <summary>0x8009E118, the ride's half: take me off the list (0x8009F1B8), call 0x80053C04 on
        /// me (not identified), and send message 6 to everyone who was behind me with a stagger that
        /// grows by rand(3) per guest and param2 = 19. The message 7 to ME is applied by the caller.</summary>
        void LeaveQueueList(Visitor guest);
        /// <summary>Bump one of the advisor's 20 event counters (0x800139B4).</summary>
        void AdvisorEvent(int index, int amount);
        /// <summary>0x800EC9F4 from where I stand to the slot, flags (0x10, 0). Accepted, not reachable.</summary>
        bool TryPathToSlot(Visitor guest, int x, int y);
        /// <summary>State 19's walk: allocate ONE waypoint (0x80093A70), place it at the slot at
        /// quarter-tile resolution (0x80093928) and end the chain there (0x800927BC). False when the
        /// pool of 1000 is exhausted.</summary>
        bool TrySetSingleWaypoint(Visitor guest, int x, int y);
        /// <summary>0x80093C68.</summary>
        void FreeWaypoints(Visitor guest);
        /// <summary>0x8009F614 found an entrance point on the target.</summary>
        bool TargetHasEntrance(Visitor guest);
        /// <summary>0x800EC9F4 to that entrance point, flags (0x18, 1).</summary>
        bool TryPathToEntrance(Visitor guest);
        /// <summary>Ride slot 26 for the leave point, then 0x800EC9F4 to its tile centre, flags (0x11, 0).</summary>
        bool TryPathToLeavePoint(Visitor guest);
        /// <summary>Type 2 only: vtable slot 54 is non-zero. READ (0x80023F0C → 0x80024348): for a
        /// feature that is the RECORD's flag `rec+0x2E &amp; 1`, "guests may use it" -- a property of the
        /// definition, not of the stock. A toilet at zero still answers true here and is still used.</summary>
        bool TargetHasStock(Visitor guest);
        /// <summary>Type 2 only: 0x800241E8(building, units). READ: takes <paramref name="units"/> off
        /// the feature's capacity byte, floored at zero (<see cref="FeatureStock.Subtract"/>).</summary>
        void ConsumeStock(Visitor guest, int units);
        /// <summary>Type 2 only: 0x800241BC(building). READ: the feature's capacity byte, 0..100
        /// (<see cref="FeatureStock.Level"/>); the handler only compares it against 50.</summary>
        int StockLevel(Visitor guest);
    }

    /// <summary>States 41, 19, 18, 21, 22, 23 and 58: from "I have chosen a ride" to "I am walking away
    /// from it" (behaviour.md §2.4). Every handler here was re-read from TPW.BIN for this port; where
    /// that reading differs from §2.4 the difference is recorded in §0 item 6 and noted at the line.
    ///
    /// ⭐ THE RIDE OWNS THE GUEST BETWEEN 18 AND 22. Nothing here moves a guest from Waiting to Loading
    /// or from Loading to Unloading: the ride's own load code (0x8009C884 and its siblings) pulls the
    /// head of the queue into 21 and its unload code sets 22. A port that let the guest decide when it
    /// had boarded would be adding a rule the original does not have.
    ///
    /// ⭐ MESSAGES ARE DELIVERED SYNCHRONOUSLY. 0x8009E118 builds message 7 and calls the leaver's own
    /// vtable slot 40 with it before returning (READ 0x8009E148..0x8009E184), so the guest is already in
    /// 58 when its caller gets control back. That is why state 41's "cannot join but already queued"
    /// path does no SetState of its own and why state 18 never names 58: the message did it. Modelled
    /// as a direct call to <see cref="OnMessage"/>, not a queue.
    ///
    /// ⚠ STATE 4 "ON RIDE" IS NEVER ENTERED in this build (§2.4, checked across the whole image). It is
    /// in <see cref="VisitorState"/> at its number and nothing here sets it.</summary>
    public static class VisitorQueue
    {
        /// <summary>The queue cap is 4 x level + 7: 7 / 11 / 15 guests at levels 0 / 1 / 2
        /// (0x8008DA9C..0x8008DAAC; rides.md §0 item 4).</summary>
        public const int QueueCapPerLevel = 4;
        /// <summary>See <see cref="QueueCapPerLevel"/>.</summary>
        public const int QueueCapBase = 7;
        /// <summary>Guests stand a quarter tile apart along the queue path (0x8009D57C: `sll 6`).</summary>
        public const int QuarterTile = 0x40;
        /// <summary>Walk speed while queueing and while returning to the entrance (0x8008F720, 0x8008F830).</summary>
        public const int QueueWalkSpeed = 15;
        /// <summary>Boredom strictly above this and a waiting guest leaves the queue (0x80090840).</summary>
        public const int BoredomLeaveThreshold = 80;
        /// <summary>The floor under |pref - intensity| before halving (0x800907A4). See <see cref="BoredomDifference"/>.</summary>
        public const int DifferenceFloor = 50;
        /// <summary>Every 8 ticks: boredom +1 when rand(100 - d) is below this (0x80090818).</summary>
        public const int BoredomRollBelow = 2;
        /// <summary>Every 8 ticks: tiredness -1 when rand(100) is below this (0x80090834).</summary>
        public const int TirednessRollBelow = 10;
        /// <summary>A fidgeting guest waits rand(300) before the next one (0x800908E0).</summary>
        public const int FidgetMax = 300;
        /// <summary>The fidget sound plays when rand(10) == 0 (0x800908FC).</summary>
        public const int FidgetSoundChanceIn = 10;
        /// <summary>Bubble shown by a guest leaving a queue out of boredom (0x80090880).</summary>
        public const int BubbleBoredInQueue = 0x30;
        /// <summary>Bubble shown at a type-2 building whose stock reads under 50 (0x8008F340).</summary>
        public const int BubbleFeatureLow = 0x3C;
        /// <summary>Unloading: V+0x50 := now + 60 + rand(60) (0x8008F17C..0x8008F194).</summary>
        public const int UnloadCooldownBase = 60;
        /// <summary>See <see cref="UnloadCooldownBase"/>.</summary>
        public const int UnloadCooldownMax = 60;
        /// <summary>Unloading: V+0x2C := now + 300 + rand(300) (0x8008F198..0x8008F1B8).</summary>
        public const int UnloadWaitBase = 300;
        /// <summary>See <see cref="UnloadWaitBase"/>.</summary>
        public const int UnloadWaitMax = 300;
        /// <summary>Unloading: tiredness -= rand(20) (0x8008F1C4).</summary>
        public const int UnloadTirednessMax = 20;
        /// <summary>P+0x2E animation 11, the idle/walk default (§1).</summary>
        public const int AnimationIdle = 11;
        /// <summary>P+0x2E animation 13, written on unloading (0x8008F1CC: 0x68 = 13 &lt;&lt; 3).</summary>
        public const int AnimationWander = 13;
        /// <summary>Happiness for a ride within 20 of the guest's preference (0x80103210, "Ride Excellent Inc").</summary>
        public const int RideExcellentHappiness = 15;
        /// <summary>Within 50 (0x8010320C, "Ride Good Inc").</summary>
        public const int RideGoodHappiness = 10;
        /// <summary>Anything worse (0x80103208, "Ride OK Inc").</summary>
        public const int RideOkHappiness = 5;
        /// <summary>|pref - intensity| strictly below this is excellent (0x8008F444: sltiu 0x15).</summary>
        public const int ExcellentBelow = 21;
        /// <summary>Strictly below this is good (0x8008F43C: sltiu 0x33).</summary>
        public const int GoodBelow = 51;
        /// <summary>Intensity at or above this makes a rider nauseous (0x8008F488: slti 0x38 skips below it).</summary>
        public const int NauseaFromIntensity = 56;
        /// <summary>The nausea slider 0x801031FC, default 1212 = 0.296 in 20.12: nausea += 1212 x (I - 30) &gt;&gt; 12,
        /// so +7 at 56 and +20 at 100. ⚠ A DEBUG-MENU TUNABLE (debug.md): the menu is gone from this build
        /// and nothing else writes it, so the default is what ships.</summary>
        public const int NauseaScale = 1212;
        /// <summary>Intensity is measured from 30 for the nausea term (0x8008F490).</summary>
        public const int NauseaBase = 30;
        /// <summary>0x80103200 = 4096 = exactly 1.0 in 20.12: boredom -= 4096 x I &gt;&gt; 12 = I. Kept as the
        /// multiply it is rather than folded to "boredom -= I", because the scale is a data word.</summary>
        public const int BoredomDropScale = 4096;
        /// <summary>Type 2: the V+0x5D need is spent only when strictly above this (0x8008F27C).</summary>
        public const int FeatureUseAbove = 60;
        /// <summary>Type 2: nausea -= 40 (0x8008F2E8).</summary>
        public const int FeatureNauseaRelief = 40;
        /// <summary>Type 2: a stock reading strictly below this upsets the guest (0x8008F310).</summary>
        public const int FeatureLowStock = 50;
        /// <summary>Type 2, low stock: happiness -10 and nausea +10 (0x8008F34C).</summary>
        public const int FeatureLowStockPenalty = 10;
        /// <summary>Message 6: V+0x2C := now + 3 x param1 (0x8008FBDC..0x8008FBE4).</summary>
        public const int ShuffleTicksPerStagger = 3;
        /// <summary>State 44, "front of turnstile lane" -- the other state that honours message 6. The
        /// turnstile sends it with stagger 0 and target 43 (<see cref="Turnstile.LaneTick"/>).</summary>
        public const VisitorState TurnstileFront = VisitorState.LaneFront;

        /// <summary>Types 1, 3, 6 and 7 have an entrance and a queue; 2, 4 and 5 are walk-ins (§2.4).</summary>
        public static bool HasQueue(int type)
            => type == (int)AttractionType.RollerCoaster || type == (int)AttractionType.Ride
            || type == (int)AttractionType.TrackRide || type == (int)AttractionType.TourRide;

        /// <summary>Ride slot 20: status is 4 or 5 (READ 0x80063910, rides.md §0 item 3). A guest in a
        /// queue gets bored faster while the ride is warned or broken.</summary>
        public static bool IsBrokenOrAboutTo(AttractionStatus status)
            => status == AttractionStatus.AboutToBreakDown || status == AttractionStatus.BrokenDown;

        /// <summary>The can-join test, 0x8008DA14(guest, ride).
        ///
        /// ⭐ BEING IN THE QUEUE BEATS THE CAP BUT NOT A CLOSED RIDE. A queued guest re-checking on every
        /// shuffle is let through however long the list has grown; a ride that has stopped being open
        /// refuses everyone, queued or not, and that is what empties a queue when a ride breaks down.
        /// Walk-in types always pass.</summary>
        public static bool CanJoin(Visitor guest, IQueueWorld world)
        {
            if (!guest.HasTarget) return false;
            if (!HasQueue(world.TargetType(guest))) return true;

            int count = world.QueueCount(guest);
            int cap = QueueCapPerLevel * world.UpgradeLevel(guest) + QueueCapBase;
            if (!AttractionLifecycle.OpenToGuests(world.RideStatus(guest))) return false;
            // Strictly below: the 8th guest at a level-0 ride is refused (rides.md §7 item 5).
            return guest.InQueue || count < cap;
        }

        /// <summary>The queue slot, 0x8009D57C(ride, guest, &amp;pos): where this guest should stand,
        /// in 8.8 coordinates. Appends the guest to the member list if it is not yet in it -- but only
        /// when a slot was found, which is the order the original does it in.</summary>
        public static bool TryQueueSlot(Visitor guest, IQueueWorld world, out int x, out int y)
        {
            var path = world.QueuePath(guest);
            if (path == null || path.Count == 0) { x = y = 0; return false; }

            int index = world.QueueIndexOf(guest);
            int ahead = index >= 0 ? index : world.QueueCount(guest);
            if (!WalkSlot(world.QueueOrigin(guest), path, ahead, out x, out y)) return false;
            if (index < 0) world.AppendToQueue(guest);
            return true;
        }

        /// <summary>The slot arithmetic of 0x8009D57C, with the list walk replaced by a count.
        ///
        /// The head of the queue stands on the centre of the origin tile. Each member ahead of me pushes
        /// my slot one quarter tile further along: first toward path tile 0, and, whenever the slot lands
        /// exactly on a path tile's centre, the direction turns toward the next tile. Two ways to fail,
        /// both READ: the slot would have to advance past the LAST tile (0x8009D7C8..0x8009D7D0), or a
        /// step lands anywhere ON the last tile (0x8009D838..0x8009D864).
        ///
        /// ⚠ DO NOT FIX THE ASYMMETRY. The "on the last tile" test is on the tile of an 8.8 coordinate,
        /// so walking in the +x/+y direction gets ONE step past the second-to-last centre (0x80 -&gt; 0xC0)
        /// while walking in -x/-y gets TWO (0x80 -&gt; 0x40 -&gt; 0x00) before crossing. A queue path laid
        /// toward the origin holds one more guest than the same path laid away from it. That is the
        /// original's behaviour and this reproduces it.
        ///
        /// ⚠ The original also computes a fallback direction from the ride's facing when the origin IS
        /// path tile 0 (0x8009D6D4..0x8009D740). It is dead: in exactly that case the first iteration's
        /// centre test passes and overwrites the direction before it is used, so it is not ported and
        /// the host is not asked for a facing.</summary>
        public static bool WalkSlot(QueueTile origin, IReadOnlyList<QueueTile> path, int membersAhead, out int x, out int y)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (path.Count == 0) throw new ArgumentException("a queue path needs at least one tile", nameof(path));

            x = Centre(origin.X);
            y = Centre(origin.Y);
            var last = path[path.Count - 1];
            int cur = 0;
            int dx = Sign(path[0].X - origin.X) * QuarterTile;
            int dy = Sign(path[0].Y - origin.Y) * QuarterTile;

            for (int i = 0; i < membersAhead; i++)
            {
                if (x == Centre(path[cur].X) && y == Centre(path[cur].Y))
                {
                    cur++;
                    if (cur == path.Count) return false;
                    dx = Sign(path[cur].X - path[cur - 1].X) * QuarterTile;
                    dy = Sign(path[cur].Y - path[cur - 1].Y) * QuarterTile;
                }
                x += dx;
                y += dy;
                if ((x >> 8) == last.X && (y >> 8) == last.Y) return false;
            }
            return true;
        }

        /// <summary>A tile's centre in 8.8: (t &lt;&lt; 8) + 0x80, as 0x8009D638..0x8009D64C forms it.</summary>
        public static int Centre(int tile) => (tile << 8) + 0x80;

        /// <summary>0x8009F450: -1, 0 or 1.</summary>
        static int Sign(int v) => v < 0 ? -1 : v > 0 ? 1 : 0;

        /// <summary>State 41 -- join queue (0x8008F688).
        ///
        /// ⚠ A REFUSED PATH IS NOT A FAILURE TO JOIN. By the time the pathfinder is asked the guest is
        /// already in the member list with its in-queue bit set, and a refusal simply returns with the
        /// state unchanged (0x8008F714 -&gt; epilogue), so next tick the can-join test passes on the bit and
        /// the request is made again. §2.4 lists only the two other exits.</summary>
        public static JoinOutcome JoinQueue(Visitor guest, IQueueWorld world)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (!CanJoin(guest, world) || !TryQueueSlot(guest, world, out int x, out int y))
                return GiveUp(guest, world) ? JoinOutcome.LeftQueue : JoinOutcome.GaveUp;

            guest.InQueue = true;
            guest.Purpose = Purpose.QueueWalk;
            if (!world.TryPathToSlot(guest, x, y)) return JoinOutcome.PathRefused;

            guest.WalkSpeed = QueueWalkSpeed;
            guest.WaitUntil = world.NowTick;
            // SET, not push (0x8008F794 is 0x80093F80): unlike the decision's walk, there is nothing to
            // come back to. State 11 is "waiting for the pathfinder" whatever its enum name says.
            guest.SetState(VisitorState.WalkToBin);
            return JoinOutcome.Walking;
        }

        /// <summary>The failure shared by 41 and 19 (0x8008F744, 0x8008F618). Returns true when the guest
        /// was queued and has been taken out -- which lands it in 58 by message 7, with no SetState here.</summary>
        static bool GiveUp(Visitor guest, IQueueWorld world)
        {
            if (guest.InQueue)
            {
                LeaveQueue(guest, world);
                return true;
            }
            guest.HasTarget = false;
            guest.SetState(VisitorState.Idle);
            return false;
        }

        /// <summary>State 19 -- shuffle forward (0x8008F508). Waits for the deadline message 6 set, then
        /// re-runs the can-join and slot tests and walks ONE waypoint to the new slot without the
        /// pathfinder, arriving with purpose 10.</summary>
        public static ShuffleOutcome Shuffle(Visitor guest, IQueueWorld world)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));

            // Strictly after the deadline (0x8008F52C: sltu V+0x2C < now).
            if (!(guest.WaitUntil < world.NowTick)) return ShuffleOutcome.Waiting;

            if (!CanJoin(guest, world) || !TryQueueSlot(guest, world, out int x, out int y))
                return GiveUp(guest, world) ? ShuffleOutcome.LeftQueue : ShuffleOutcome.GaveUp;

            world.FreeWaypoints(guest);
            guest.InQueue = true;
            guest.Purpose = Purpose.QueueShuffle;
            // The pool can be empty (0x8008F5BC checks for -1), in which case the guest stays in 19 with
            // its waypoints already freed and its purpose already 10, and tries again next tick.
            if (!world.TrySetSingleWaypoint(guest, x, y)) return ShuffleOutcome.NoWaypoint;
            guest.SetState(VisitorState.WalkToWaypoint);
            return ShuffleOutcome.Stepping;
        }

        /// <summary>The per-tick boredom parameter of state 18: `max(|pref - intensity|, 50) &gt;&gt; 1`, so
        /// 25 for any ride within 50 of the guest's taste and up to 50 for the worst mismatch.
        ///
        /// ⚠ THE ABSOLUTE VALUE IS READ (0x8009073C..0x8009079C computes both orders and picks by sign);
        /// §2.4 wrote `max(pref - intensity, 50)` without it. The floor IS there: 0x80092118 is max, and
        /// the halving follows. It reads like a bug because a perfect match and a ride 50 away bore a
        /// guest at exactly the same rate -- 2 in 75 per 8 ticks -- and only mismatches beyond 50 bore it
        /// faster, up to 2 in 50. ⚠ DO NOT FIX: it is what the code does.</summary>
        public static int BoredomDifference(int pref, int intensity)
            => Math.Max(Math.Abs(pref - intensity), DifferenceFloor) >> 1;

        /// <summary>State 18 -- waiting in queue (0x800906EC).
        ///
        /// ⭐ BOTH PERIODIC PASSES ARE STAGGERED BY V+0x10, the same field that staggers the decision
        /// (0x800907CC and 0x800907F0 compare `now &amp; 3` / `now &amp; 7` against it). §2.4 says "every 4
        /// ticks" and "every 8 ticks" without the stagger; the code has it, so a queue of guests does not
        /// all grow bored on the same tick.
        ///
        /// ⚠ DICE ORDER on a stagger tick: rand(100 - d), rand(100); then on a fidget: rand(300), rand(4),
        /// rand(10). Leaving the queue rolls nothing here -- the rand(3) staggers belong to
        /// <see cref="IQueueWorld.LeaveQueueList"/>.</summary>
        public static WaitOutcome Wait(Visitor guest, IQueueWorld world, IRandomSource rng)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            long now = world.NowTick;
            int d = BoredomDifference(RideScore.Preference(guest), world.Intensity(guest));

            if (IsBrokenOrAboutTo(world.RideStatus(guest)) && (now & 3) == (guest.DecisionStagger & 3))
                guest.Boredom = Stat.Add(guest.Boredom, 1);

            if ((now & 7) == (guest.DecisionStagger & 7))
            {
                guest.Boredom = Stat.Add(guest.Boredom, rng.Next(100 - d) < BoredomRollBelow ? 1 : 0);
                guest.Tiredness = Stat.Sub(guest.Tiredness, rng.Next(100) < TirednessRollBelow ? 1 : 0);
            }

            if (guest.Boredom > BoredomLeaveThreshold)
            {
                guest.Bubble = BubbleBoredInQueue;
                LeaveQueue(guest, world);
                // ⭐ THE FIRST OF THOSE TWO CALLS IS NOT A SOUND. `(gp, 3, 8)` is 0x800139B4 with event
                // index 3 and amount 8 -- the advisor's queue-abandonment counter, read off its own
                // argument list. It adds EIGHT, not one, so the counter measures how much queue-leaving
                // is going on rather than how many guests did it. ⚠ DO NOT "FIX" THE 8.
                //
                // ⚠ AND IT BELONGS HERE, NOT IN LeaveQueueList. Three paths call LeaveQueue -- a message
                // throwing the guest out, GiveUp, and this one -- and only boredom counts. Hooking the
                // list removal instead would count a broken ride's ejections as guests losing patience.
                world.AdvisorEvent(AdvisorEventQueueAbandoned, AdvisorQueueAbandonPoints);
                // the second, (1, 0x17), is a sound; no dice.
                return WaitOutcome.LeftBored;
            }

            if (!(guest.WaitUntil < now)) return WaitOutcome.Standing;
            guest.WaitUntil = now + rng.Next(FidgetMax);
            guest.Facing = rng.Next(4) << 1;
            rng.Next(FidgetSoundChanceIn);   // the sound roll happens whether or not audio is wired
            return WaitOutcome.Fidgeted;
        }

        /// <summary>State 21 -- loading (0x8008E538). The ride has taken the guest; this runs every tick
        /// it is aboard and only keeps three bits straight. Nothing here ends it: the ride's unload code
        /// sets 22.</summary>
        public static void Loading(Visitor guest)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            guest.Flag1 = false;
            guest.InQueue = true;
            guest.Bubble = 0;
        }

        /// <summary>State 22 -- unloading (0x8008F110). Applies the target's effect by type
        /// (table 0x800E3B54) and moves on.
        ///
        /// ⭐ IT SETS 23 FIRST, THEN OVERRIDES FOR WALK-INS. The handler's third instruction is
        /// SetState(23); the type-2, 4 and 5 arms each end with SetState(0). So a ride leaves the guest
        /// in 23 with its target intact (the entrance walk needs it), and everything else drops the target
        /// and stands around. A guest with no target, or a type the table has no arm for, stays in 23.
        ///
        /// ⚠ DICE ORDER: rand(60), rand(300), rand(20), before the type is even looked at.</summary>
        public static UnloadOutcome Unload(Visitor guest, IQueueWorld world, IRandomSource rng)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            long now = world.NowTick;
            guest.Flag1 = true;
            guest.InQueue = false;
            guest.SetState(VisitorState.GotoEntrance);
            guest.EntertainerNotBefore = now + rng.Next(UnloadCooldownMax) + UnloadCooldownBase;
            guest.WaitUntil = now + rng.Next(UnloadWaitMax) + UnloadWaitBase;
            guest.WalkSpeed = guest.NormalWalkSpeed;
            guest.Animation = AnimationWander;
            guest.Tiredness = Stat.Sub(guest.Tiredness, rng.Next(UnloadTirednessMax));

            if (!guest.HasTarget) return UnloadOutcome.NothingToUnload;
            switch (world.TargetType(guest))
            {
                case (int)AttractionType.RollerCoaster:
                case (int)AttractionType.Ride:
                case (int)AttractionType.TrackRide:
                case (int)AttractionType.TourRide:
                    RideReward(guest, world);
                    return UnloadOutcome.RodeIt;

                case (int)AttractionType.Feature:
                    return UseFeature(guest, world);

                // The two purchase routines (§2.5, VisitorPurchase) roll their own dice AFTER the three
                // above: rand(25) on a sale, rand(100) on a play, nothing on a refusal.
                case (int)AttractionType.Shop:
                {
                    bool bought = VisitorPurchase.BuyAtShop(guest, world, rng);
                    guest.HasTarget = false;
                    guest.SetState(VisitorState.Idle);
                    return bought ? UnloadOutcome.Bought : UnloadOutcome.DidNotBuy;
                }

                case (int)AttractionType.SideShow:
                {
                    bool played = VisitorPurchase.PlaySideShow(guest, world, rng);
                    guest.SetState(VisitorState.Idle);
                    guest.HasTarget = false;
                    return played ? UnloadOutcome.Bought : UnloadOutcome.DidNotBuy;
                }

                default:
                    return UnloadOutcome.NothingToUnload;
            }
        }

        /// <summary>The happiness a ride pays by how far its intensity sits from the guest's preference:
        /// +15 under 21 away, +10 under 51, +5 otherwise (0x8008F43C..0x8008F480).</summary>
        public static int RideHappiness(int difference)
            => difference < ExcellentBelow ? RideExcellentHappiness
             : difference < GoodBelow ? RideGoodHappiness
             : RideOkHappiness;

        /// <summary>Types 1, 3, 6, 7 (0x8008F3E4). Target is kept: state 23 needs it.
        ///
        /// ⚠ The mismatch is |pref - intensity| in BOTH directions (0x8008F414..0x8008F438 branches on the
        /// sign), so a ride far too tame pays the same +5 as one far too wild. §2.4 lists a further
        /// "ride slot 25" call here; there is none in this arm (the only calls are slot 53, 0x8008C760 and
        /// 0x80063164), and §2.4 itself says it would be a no-op for rides.</summary>
        static void RideReward(Visitor guest, IQueueWorld world)
        {
            int intensity = world.Intensity(guest);
            int m = Math.Abs(RideScore.Preference(guest) - intensity);
            guest.Happiness = Stat.Add(guest.Happiness, RideHappiness(m));
            if (intensity >= NauseaFromIntensity)
                guest.Nausea = Stat.Add(guest.Nausea, (NauseaScale * (intensity - NauseaBase)) >> 12);
            guest.Boredom = Stat.Sub(guest.Boredom, (BoredomDropScale * intensity) >> 12);
            world.CountGuestServed(guest);
        }

        /// <summary>Type 2 (0x8008F248). behaviour.md calls it "a shop with a stock check"; rides.md §0
        /// item 1 corrects that to a FEATURE (toilets, bins, benches...) whose "stock" is the capacity
        /// byte in <see cref="FeatureStock"/>, and debug.md's slider labels put V+0x5D down as a toilet
        /// need. The arithmetic is READ.
        ///
        /// ⚠ NOTHING HERE REFUSES, CLOSES OR REPORTS AN EMPTY FEATURE. The stock test at the top is the
        /// record flag (slot 54), not the byte; a feature at zero is used anyway (the subtract floors),
        /// the need is zeroed anyway, and the only consequence is the low-stock arm below. No status
        /// change, no message: by exhaustion of the byte's callers (findings/shop-stock.md §3).</summary>
        static UnloadOutcome UseFeature(Visitor guest, IQueueWorld world)
        {
            if (!world.TargetHasStock(guest))
            {
                guest.HasTarget = false;
                guest.SetState(VisitorState.Idle);
                return UnloadOutcome.FeatureEmpty;
            }

            // (need - 60) x 2 / 3, as a signed divide by 3 (0x8008F2A4: x 0x55555556, mfhi, minus sign).
            if (guest.RideDesire > FeatureUseAbove)
                world.ConsumeStock(guest, (guest.RideDesire - FeatureUseAbove) * 2 / 3);
            guest.RideDesire = 0;
            guest.Nausea = Stat.Sub(guest.Nausea, FeatureNauseaRelief);
            world.CountGuestServed(guest);
            if (world.StockLevel(guest) < FeatureLowStock)
            {
                guest.Bubble = BubbleFeatureLow;
                guest.Happiness = Stat.Sub(guest.Happiness, FeatureLowStockPenalty);
                guest.Nausea = Stat.Add(guest.Nausea, FeatureLowStockPenalty);
            }
            guest.HasTarget = false;
            guest.SetState(VisitorState.Idle);
            return UnloadOutcome.UsedFeature;
        }

        /// <summary>State 23 -- go to the ride's entrance point (0x8008F7BC), to walk back out through it.
        /// A refused path leaves the guest in 23 with purpose already 4, retrying each tick; no entrance
        /// point at all goes to Idle WITHOUT clearing the target (unlike the arrival's version of that).</summary>
        public static EntranceOutcome GotoEntrance(Visitor guest, IQueueWorld world)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (!world.TargetHasEntrance(guest))
            {
                guest.SetState(VisitorState.Idle);
                return EntranceOutcome.NoEntrance;
            }
            guest.Purpose = Purpose.Abandon;
            if (!world.TryPathToEntrance(guest)) return EntranceOutcome.PathRefused;

            guest.WalkSpeed = QueueWalkSpeed;
            guest.WaitUntil = world.NowTick;
            guest.SetState(VisitorState.WalkToBin);   // SET 11 (0x8008F864), as in 41
            return EntranceOutcome.Walking;
        }

        /// <summary>State 58 -- removed from queue (0x800915F4): path to the ride's leave point and PUSH 11
        /// (0x80091698 is 0x80093F20), so the walk returns here and arrival 22 ends it. Purpose is only
        /// written once the request is accepted; a refusal changes nothing and retries.</summary>
        public static bool WalkToLeavePoint(Visitor guest, IQueueWorld world)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (!world.TryPathToLeavePoint(guest)) return false;
            guest.WaitUntil = world.NowTick;
            guest.Purpose = Purpose.ClearTarget;
            guest.PushState(VisitorState.WalkToBin);
            return true;
        }

        /// <summary>Advisor event 3: a guest gave up on a queue (0x800908A4).</summary>
        public const int AdvisorEventQueueAbandoned = 3;
        /// <summary>...and it is worth EIGHT points, not one (0x800908A8).</summary>
        public const int AdvisorQueueAbandonPoints = 8;

        /// <summary>0x8009E118 as the guest experiences it: off the list, message 7 to itself (so it is
        /// in 58 before this returns), bit 0x01 set (0x8009E194 -- §2.4 does not mention it), and
        /// everyone behind told to shuffle.</summary>
        public static void LeaveQueue(Visitor guest, IQueueWorld world)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));

            world.LeaveQueueList(guest);
            OnMessage(guest, world, QueueMessage.RemovedFromQueue);
            guest.Flag1 = true;
        }

        /// <summary>Vtable slot 40 (0x8008F880), the three rows that concern the queue. Returns whether the
        /// message did anything.
        ///
        /// ⚠ SHUFFLE IS GATED ON STATE, THE OTHER TWO ARE NOT. A shuffle that reaches a guest already
        /// loading, or already walking, is dropped (0x8008FB98..0x8008FBA8); "removed" and "ejected" act
        /// whatever the guest is doing. Ejected clears the target, removed keeps it -- 58 needs it to find
        /// the leave point.</summary>
        public static bool OnMessage(Visitor guest, IQueueWorld world, QueueMessage id, int param1 = 0, int param2 = 0)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));

            switch (id)
            {
                case QueueMessage.Shuffle:
                    if (guest.State != VisitorState.WaitingInQueue && guest.State != TurnstileFront) return false;
                    world.FreeWaypoints(guest);
                    guest.WaitUntil = world.NowTick + ShuffleTicksPerStagger * param1;
                    guest.SetState((VisitorState)param2);
                    return true;

                case QueueMessage.RemovedFromQueue:
                    world.FreeWaypoints(guest);
                    guest.SetState(VisitorState.RemovedFromQueue);
                    guest.InQueue = false;
                    return true;

                case QueueMessage.Ejected:
                    guest.HasTarget = false;
                    world.FreeWaypoints(guest);
                    guest.SetState(VisitorState.Idle);
                    guest.InQueue = false;
                    return true;

                default:
                    return false;
            }
        }
    }
}
