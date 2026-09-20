using System;
using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>A tile position, as the turnstile code hands them about: the entrance building's tile
    /// (0x8005439C, halfwords at +4 and +8) and the guest's own tile (vtable slot 10, 0x800939B0, the
    /// 8.8 position shifted right by 8).</summary>
    public readonly struct MapTile
    {
        public MapTile(int x, int y) { X = x; Y = y; }
        public int X { get; }
        public int Y { get; }
    }

    /// <summary>The guest messages the entrance answers (§2.10), by the message vtable's own slot-1 ids.
    /// 6, 7 and 10 are <see cref="QueueMessage"/> and stay with the queue; 3, 5 and 8 are ignored by
    /// the handler (0x800E3B74 sends them to the epilogue).</summary>
    public enum EntranceMessage
    {
        /// <summary>1: the pathfinder has a path. Only honoured in 11.</summary>
        PathReady = 1,
        /// <summary>2: the pathfinder refused. Only honoured in 11; what happens depends on the purpose.</summary>
        PathFailed = 2,
        /// <summary>4: a guard caught up with the guest. Despawn on the spot.</summary>
        ThrownOut = 4,
        /// <summary>9: the park is admitting. Sent by 0x80052148 to everyone in 46.</summary>
        Admit = 9,
    }

    /// <summary>What one of the walking handlers (36, 38, 42, 43, 45, 47, 48) did.</summary>
    public enum GateWalkOutcome
    {
        /// <summary>Path accepted; the guest is in 11 waiting for it.</summary>
        Walking,
        /// <summary>The pathfinder refused. State and purpose as they were; tries again next tick.</summary>
        PathRefused,
        /// <summary>One waypoint set; the guest is in 3.</summary>
        Stepping,
        /// <summary>The waypoint pool is empty. State unchanged; tries again next tick.</summary>
        NoWaypoint,
        /// <summary>State 45 scanned its fifteen rows and found no path tile. Still in 45.</summary>
        NoPathTile,
        /// <summary>State 38's once-per-64-ticks gate: not this guest's tick.</summary>
        NotMyTick,
        /// <summary>State 38 with no exit points in the map: the guest was removed at once.</summary>
        Despawned,
    }

    public enum PayOutcome
    {
        /// <summary>Money taken, income booked, admission counted; now in 45.</summary>
        Paid,
        /// <summary>Money was not strictly more than the fee. No verdict rolled; now in 38.</summary>
        CannotAfford,
        /// <summary>The value verdict was -2. Now in 38.</summary>
        Refused,
    }

    public enum LaneTickOutcome
    {
        /// <summary>Not this lane's tick (the two lanes take turns on `now &amp; 31`).</summary>
        NotMyTick,
        LaneEmpty,
        /// <summary>The head of the lane is not yet in 44: nothing happens, nobody is told to shuffle.</summary>
        HeadNotAtFront,
        /// <summary>The head is in 37 and everyone behind it has been sent message 6.</summary>
        Admitted,
    }

    /// <summary>The turnstile as both the entrance states and the arrival arms 11-16 see it (the
    /// 0x80059xxx cluster, §2.6). The lane LISTS (0x800F2378 + 16 x lane) and the global words stay
    /// with the host; the slot arithmetic and every decision are in <see cref="VisitorEntrance"/>.</summary>
    public interface ITurnstileWorld
    {
        long NowTick { get; }
        /// <summary>P+0x18 / P+0x1A, the guest's position in 8.8 (256 per tile).</summary>
        (int X, int Y) Position(Visitor guest);
        /// <summary>The entrance building for a lane: 0x8005439C(1, lane == 0 ? 1 : 2), the
        /// building-table entry flagged 0x04000000, the second call taking the one with the larger x
        /// (§2.6). Its tile x and y are the halfwords at +4 and +8 (0x800591B8, 0x800591CC).
        /// ⚠ THE ORIGINAL DOES NOT NULL-TEST IT (0x800591B0 branches on the lane, not the result), so a
        /// park with no entrance building read through address 4. A host without one may throw.</summary>
        MapTile EntranceTile(int lane);
        /// <summary>The lane's member list, head first (0x8005CFF0 / 0x8005D010).</summary>
        IReadOnlyList<Visitor> Lane(int lane);
        /// <summary>0x8005C7EC: put me on the end of the lane list.</summary>
        void AppendToLane(int lane, Visitor guest);
        /// <summary>The lane count word at 0x80103948 + 4 x lane, as 0x80059150 reads it.
        /// ⚠ NOT THE LIST'S LENGTH. It is a separate word: +1 on append (0x8005929C), -1 on admission
        /// (0x80059398), and it is what arrival 16 tests against 11 when picking a lane.</summary>
        int LaneCount(int lane);
        void SetLaneCount(int lane, int value);
        /// <summary>READ: the word at 0x80103950. +1 on both arrivals at the gate (14 and 15, via
        /// 0x8005996C), -1 on every crossing (16, via 0x80059984). The same word the guard moves at the
        /// same arrivals (Guard.Arrive; behaviour.md §0 item 7), so a host implements it once for both.
        /// arrivals.md §5 reads it as "guests waiting at the gate" and MEASURED it; named by its address
        /// here to match <c>IGuardWorld</c>.</summary>
        int Counter80103950 { get; set; }
        /// <summary>READ: the word at 0x80103954, +1 on every crossing (0x80059984); the admit routine
        /// zeroes it when a batch opens and stops sending message 9 once it reaches 11. arrivals.md §5:
        /// "admitted this batch".</summary>
        int Counter80103954 { get; set; }
    }

    /// <summary>Everything the entrance and exit states need beyond the turnstile itself.</summary>
    public interface IEntranceWorld : ITurnstileWorld
    {
        /// <summary>0x800540AC(): how many exit points the map has (transport.md §1).</summary>
        int ExitCount { get; }
        /// <summary>0x800599A4(pointIndex, &amp;pos) then 0x800EC9F4 from where I stand. Point 0 is the
        /// spawn side and 1 the exit side; table 0x800E0D28 holds (5376, 1620) and (5376, 2640) in 8.8
        /// (READ from the data segment, no writer found in TPW.BIN).
        /// ⚠ THE LOOKUP ROLLS DICE: x += rand(426) - 213 (0x80059A1C..0x80059A68), then
        /// y += rand(2 x index + 1) - 15 for point 0 and y -= the same for point 1 (0x80059A4C..0x80059A8C).
        /// Two rolls, in that order, that a host keeping the original's stream must draw here. The
        /// guard's <c>PathToParkPoint</c> hides the same routine.</summary>
        bool TryPathToParkPoint(Visitor guest, int pointIndex, int flags, int secondaryFlags);
        /// <summary>0x800540B8(&amp;pos, exitIndex) then 0x800EC9F4 to it. The index was rolled by the caller.</summary>
        bool TryPathToExit(Visitor guest, int exitIndex, int flags, int secondaryFlags);
        /// <summary>0x800EC9F4 to a lane slot, flags (1, 0) (0x80091158 / 0x80091174).</summary>
        bool TryPathToLaneSlot(Visitor guest, int x, int y);
        /// <summary>Allocate ONE waypoint (0x80093A70), place it at 8.8 coordinates (0x80093928) and end
        /// the chain (0x800927BC). False when the pool is exhausted (P+0x28 reads -1 after 0x80093C9C).
        /// The same contract as the queue's.</summary>
        bool TrySetSingleWaypoint(Visitor guest, int x, int y);
        /// <summary>State 47's waypoint: ONE waypoint at (my x, the y of park point pointIndex)
        /// (0x800914E0..0x80091518). The point comes from 0x800599A4 with its dice, as above; the
        /// allocation (0x80093A70 / 0x80093C5C) happens BEFORE the lookup, so a failed one rolls nothing.</summary>
        bool TrySetGateWaypoint(Visitor guest, int pointIndex);
        /// <summary>0x80093C68.</summary>
        void FreeWaypoints(Visitor guest);
        /// <summary>0x8004D670(Tile(x, y)): the tile's flag 0x08 (paths.md §5). What sets it is not
        /// established there either. A y past the map edge is the host's to answer as the original
        /// would have: 0x80053FD0 does not range-check.</summary>
        bool TileHasFlag8(int x, int y);
        /// <summary>0x8004D4EC or 0x8004D558: tile type 2 or 13, the two path types (paths.md §5).</summary>
        bool TileIsPath(int x, int y);
        /// <summary>BANK+0 via 0x80087248: the entry fee, in tenths of a pound.</summary>
        Money EntryFee { get; }
        /// <summary>0x80087258: balance += fee, BANK+0x12C8 += fee, the fee history bucket += fee
        /// (economy.md §4.1). What it books is BANK+0, the same word <see cref="EntryFee"/> reads.</summary>
        void BookEntryFee(Visitor guest);
        /// <summary>0x8009225C(McAi): McAi+0x1C += 1. READ that it increments; "admissions" is a GUESS.</summary>
        void CountAdmission();
        /// <summary>Ride vtable slot 53 of every placed attraction, over all seven typed lists
        /// (iterator 0x8006DCA0 / 0x8006DD3C / 0x8006DE68, as the verdict walks them).</summary>
        IEnumerable<int> AttractionIntensities { get; }
        /// <summary>0x800519B0 then 0x80051D74: out of the guest manager, and every staff list told.</summary>
        void RemoveFromPark(Visitor guest);

        // ── the gate's own machinery: 0x80052148 (admit) and 0x800592CC (the lanes) ──

        /// <summary>0x80103958: 0 idle, 1 admitting a batch, 2 bus present and no batch (arrivals.md §4).</summary>
        int GateBatch { get; set; }
        /// <summary>0x80103964: the bus phase. 2 is "at the gate" (arrivals.md §5).</summary>
        int BusPhase { get; }
        /// <summary>Every guest in the park (list 0x800536B4, next 0x8005DF98).</summary>
        IEnumerable<Visitor> Guests { get; }
        /// <summary>Every staff member (list 0x800536D8, next 0x8005DFB0).</summary>
        IEnumerable<StaffMember> Staff { get; }
        /// <summary>Call the guest's vtable slot 40 with a message. The host routes the id: 6, 7 and 10
        /// to <see cref="VisitorQueue.OnMessage"/>, the rest to <see cref="VisitorEntrance.OnMessage"/>.
        /// Delivery is synchronous, as it is in the original.</summary>
        void DeliverMessage(Visitor guest, int id, int param1, int param2);
        /// <summary>Slot 40 on a staff member: a guard waiting in 46 gets message 9 like a guest.</summary>
        void DeliverStaffMessage(StaffMember staff, int id);
        /// <summary>0x8005CBE8: unlink from the lane list, then 0x80053C04 on the guest (not identified;
        /// the ride queue's leave routine calls it too, and hides it the same way).</summary>
        void RemoveFromLane(int lane, Visitor guest);
    }

    /// <summary>States 36, 42, 43, 44, 46, 37, 45, 38, 47, 48 and the arrival arms 11-16: a guest's way
    /// in through the turnstile and its way out (behaviour.md §2.6, §2.3, §2.10). Every handler was
    /// re-read from TPW.BIN for this port; where the reading differs from §2.6 the text's rule is kept
    /// and the difference is written at the line.
    ///
    /// ⭐ V+0x28 IS A NUMBER AT THE GATE. The target-object pointer is written 0 by arrival 15 and 1 by
    /// arrival 14 (16-bit stores, 0x8008E0A8 / 0x8008E0E4), and that number is what arrival 16 branches
    /// on to tell a guest coming in from a guest going out. A guest going in then gets its LANE stored
    /// in the same word. <see cref="Visitor.GateScratch"/> is that view of the field.
    ///
    /// ⭐ THE LANES ARE QUEUES, AND THEY USE THE QUEUE BIT. States 42 and 43 set P+0x2B bit 0x04
    /// (0x8009113C, 0x80091210) exactly as 41 and 19 do for a ride, and state 37 clears it
    /// (0x80090F14, called with 0). §2.6 mentions neither; the guard's chase test "culprit is in a queue"
    /// (states 18/19/20) does not include 42-44, so a guest in a turnstile lane can still be caught.
    ///
    /// ⭐ ADMISSION IS A BROADCAST. 0x80052148 sends message 9 to EVERY person in 46, guests and staff,
    /// on every tick a batch is open, and counts crossings (arrival 16) rather than deliveries. So a
    /// crowd at the gate all sets off for the gate line on the same tick and sorts itself into lanes on
    /// arrival; nothing admits "one guest".</summary>
    public static class VisitorEntrance
    {
        /// <summary>Park point 0, the spawn side of the gate (0x800599A4 argument, state 36).</summary>
        public const int SpawnPoint = 0;
        /// <summary>Park point 1, the exit side (state 38, the exit retry, and 47's far-side waypoint).</summary>
        public const int ExitPoint = 1;
        /// <summary>State 36's request flags (0x80090CF8; pathfinder.md Appendix).</summary>
        public const int SpawnPathFlags = 1;
        /// <summary>State 38's request flags: paths + gate-side (0x8009104C).</summary>
        public const int LeavePathFlags = 0x21;
        /// <summary>The exit retry's flags: paths + grass (0x8008FA40).</summary>
        public const int RetryPathFlags = 0x23;
        /// <summary>State 42's request flags (0x80091158).</summary>
        public const int LaneSlotPathFlags = 1;
        /// <summary>State 48's request flags (0x80091590).</summary>
        public const int ExitPathFlags = 1;
        /// <summary>The second flag word, 0 at every entrance call site.</summary>
        public const int SecondaryPathFlags = 0;
        /// <summary>State 38 retries when `now &amp; 63` equals `V+0x10 &amp; 63` (0x8009101C..0x80091024).</summary>
        public const int LeaveRetryMask = 0x3F;
        /// <summary>Lane L admits when `now &amp; 31 == L` (0x80059300..0x80059308).</summary>
        public const int LaneAdmitMask = 0x1F;
        /// <summary>Arrival 16 flips its rolled lane when that lane's count is at or above this (0x8008E14C: slti 0xB).</summary>
        public const int LaneFullAt = 11;
        /// <summary>The admit routine stops sending message 9 once this many have crossed (0x80052180: slti 0xB).</summary>
        public const int BatchMax = 11;
        /// <summary>Bus phase 2, "at the gate": a full batch closes only in it (0x800522E0).</summary>
        public const int BusPhaseAtGate = 2;
        /// <summary>A lane slot is 0x180 (1.5 tiles) from the entrance tile's origin in x, +x for lane 0
        /// and -x for lane 1 (0x800591C4 / 0x800591EC).</summary>
        public const int LaneSlotOffsetX = 0x180;
        /// <summary>... and 0x80 (half a tile) in y, with the same sign as the x offset (0x800591DC / 0x80059200).</summary>
        public const int LaneSlotOffsetY = 0x80;
        /// <summary>The turnstile shuffle: message 6 with param1 0 and param2 43 (0x800593F4 / 0x800593FC).</summary>
        public const int ShuffleStagger = 0;
        /// <summary>See <see cref="ShuffleStagger"/>.</summary>
        public const VisitorState ShuffleTarget = VisitorState.ShuffleInLane;
        /// <summary>State 45 scans this many rows, its own tile first (0x800912C4).</summary>
        public const int WalkInScanRows = 15;
        /// <summary>Arrival 14 writes P+0x2E bits 0-2 := 4 (0x8008E110); arrival 15 writes 0 (0x8008E0C8).
        /// Those bits are <see cref="Visitor.Facing"/>; §1 calls them a "sub-mode". Same bits.</summary>
        public const int LeavingFacing = 4;
        /// <summary>See <see cref="LeavingFacing"/>.</summary>
        public const int ArrivingFacing = 0;
        /// <summary>The verdict's divisor base, 0x80103244 = 10000; the roll is rand(base / 2 + 1) on top.</summary>
        public const int VerdictDivisorBase = 10000;
        /// <summary>0x80103248 = 0: unless q or the fee in pounds exceeds this, the verdict is 0 without a
        /// sound (0x80090E30..0x80090E4C). With the shipped value that is "no rides and no fee".</summary>
        public const int VerdictGate = 0;
        /// <summary>0x8010324C = 0x1400, 1.25 in 20.12: a fee strictly below 1.25q is at worst neutral.</summary>
        public const int VerdictNeutralBelow = 0x1400;
        /// <summary>0x80103250 = 0x1800, 1.5 in 20.12: a fee at or above 1.5q is refused.</summary>
        public const int VerdictRefuseAt = 0x1800;
        /// <summary>0x80103254 = 0xC00, 0.75 in 20.12: a fee at or below 0.75q is a bargain (+1).</summary>
        public const int VerdictBargainAt = 0xC00;
        /// <summary>The only verdict that refuses to pay (0x80090F78: `slti -1` then invert).</summary>
        public const int VerdictRefuse = -2;
        /// <summary>Message 2 for a purpose with no arm: happiness -= rand(15) (0x8008FB54).</summary>
        public const int WanderHappinessLossMax = 15;
        /// <summary>... and boredom += rand(2) (0x8008FB6C).</summary>
        public const int WanderBoredomGainMax = 2;

        // ───────────────────────── lane slots ─────────────────────────

        /// <summary>The lane slot arithmetic of 0x80059168 with the list walk replaced by a count.
        ///
        /// Lane 0 stands at (x + 0x180, y + 0x80) from the entrance tile's origin, lane 1 at
        /// (x - 0x180, y - 0x80): the two offsets share a sign, so the lanes sit on opposite corners of
        /// the building rather than mirrored across it. Each guest ahead of me pushes my slot a quarter
        /// tile toward -y (0x80059258), for both lanes.</summary>
        public static (int X, int Y) LaneSlot(MapTile entrance, int lane, int membersAhead)
        {
            int x = entrance.X << 8, y = entrance.Y << 8;
            if (lane == 0) { x += LaneSlotOffsetX; y += LaneSlotOffsetY; }
            else { x -= LaneSlotOffsetX; y -= LaneSlotOffsetY; }
            y -= VisitorQueue.QuarterTile * membersAhead;
            return (x, y);
        }

        /// <summary>0x80059168(lane, guest, &amp;pos) as the guest experiences it: my slot in the lane
        /// V+0x28 names, appended to the lane and counted if I was not yet in it.
        ///
        /// ⚠ IT CANNOT FAIL. Every exit of 0x80059168 returns 1 (0x8005923C, 0x8005924C, 0x80059298),
        /// so the "fails -&gt; nothing" branches §2.6 gives states 42, 43 and arrivals 11 and 12 are
        /// unreachable (0x80091124, 0x800911E4, 0x8008DF54, 0x8008DFB0 all test a value that is never 0).
        /// Not ported as a failure path; there is nothing to port.</summary>
        public static (int X, int Y) LaneSlotFor(Visitor guest, ITurnstileWorld world)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));

            int lane = guest.GateScratch;
            var members = world.Lane(lane);
            int index = IndexOf(members, guest);
            int ahead = index >= 0 ? index : members.Count;
            var slot = LaneSlot(world.EntranceTile(lane), lane, ahead);
            if (index < 0)
            {
                world.AppendToLane(lane, guest);
                world.SetLaneCount(lane, world.LaneCount(lane) + 1);
            }
            return slot;
        }

        static int IndexOf(IReadOnlyList<Visitor> list, Visitor guest)
        {
            for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], guest)) return i;
            return -1;
        }

        // ───────────────────────── the way in ─────────────────────────

        /// <summary>State 36 -- spawn to gate (0x80090CC0): path to the spawn point with purpose 15.
        /// The purpose is written before the request (0x80090CF0), so a refusal leaves it set; an
        /// accepted request SETS 11 (0x80090D40 is 0x80093F80), nothing to come back to.</summary>
        public static GateWalkOutcome SpawnToGate(Visitor guest, IEntranceWorld world)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));

            guest.Purpose = Purpose.Turnstile15;
            if (!world.TryPathToParkPoint(guest, SpawnPoint, SpawnPathFlags, SecondaryPathFlags))
                return GateWalkOutcome.PathRefused;
            guest.WaitUntil = world.NowTick;
            guest.SetState(VisitorState.WalkToBin);   // SET 11: "waiting for the pathfinder", whatever the enum name says
            return GateWalkOutcome.Walking;
        }

        /// <summary>State 47 -- pick a lane (0x80091460): free the path and walk ONE waypoint from my x
        /// to the y of the park point on the far side of the gate, purpose 16.
        ///
        /// ⚠ THE POINT IS `1 - V+0x28` (0x800914D0..0x800914E0: `1 - lh 40(s1)`), not a fixed "gate y":
        /// a guest coming in (0) steps to the exit point's y and a guest going out (1) to the spawn
        /// point's y. §2.6 says "(my x, gate y)"; the guard's 0x80098594 does the same subtraction at
        /// 0x80098604..0x80098614 and its port hides it in the host.
        /// ⚠ PURPOSE IS WRITTEN ONLY AFTER A WAYPOINT WAS HAD (0x800914D8 follows the -1 test at
        /// 0x800914C4), unlike 43, which writes it first. A guest with an empty pool keeps whatever
        /// purpose it arrived with.</summary>
        public static GateWalkOutcome PickLane(Visitor guest, IEntranceWorld world)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));

            world.FreeWaypoints(guest);
            if (!world.TrySetGateWaypoint(guest, 1 - guest.GateScratch)) return GateWalkOutcome.NoWaypoint;
            guest.Purpose = Purpose.Turnstile16;
            guest.SetState(VisitorState.WalkToWaypoint);
            return GateWalkOutcome.Stepping;
        }

        /// <summary>State 42 -- walk to lane slot (0x80091100). The queue bit and the purpose are set
        /// before the request; the queue speed only after it is accepted (0x80091180 follows the
        /// 0x80091178 test), so a refused guest walks at its own pace until it is not refused.</summary>
        public static GateWalkOutcome WalkToLaneSlot(Visitor guest, IEntranceWorld world)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));

            var (x, y) = LaneSlotFor(guest, world);
            guest.InQueue = true;
            guest.Purpose = Purpose.Turnstile11;
            if (!world.TryPathToLaneSlot(guest, x, y)) return GateWalkOutcome.PathRefused;
            guest.WalkSpeed = VisitorQueue.QueueWalkSpeed;
            guest.WaitUntil = world.NowTick;
            guest.SetState(VisitorState.WalkToBin);   // SET 11 (0x800911A0)
            return GateWalkOutcome.Walking;
        }

        /// <summary>State 43 -- shuffle in lane (0x800911BC): the lane's answer to the queue's 19. Free
        /// the old waypoint, recompute the slot, walk ONE waypoint to it with purpose 12. An empty pool
        /// (0x8009124C) leaves the guest in 43 with its waypoints already freed and its purpose already
        /// 12, to try again next tick.</summary>
        public static GateWalkOutcome ShuffleInLane(Visitor guest, IEntranceWorld world)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));

            var (x, y) = LaneSlotFor(guest, world);
            world.FreeWaypoints(guest);
            guest.InQueue = true;
            guest.Purpose = Purpose.Turnstile12;
            if (!world.TrySetSingleWaypoint(guest, x, y)) return GateWalkOutcome.NoWaypoint;
            guest.SetState(VisitorState.WalkToWaypoint);
            return GateWalkOutcome.Stepping;
        }

        /// <summary>The entry-fee verdict 0x80090D5C. q is the park's summed intensity scaled by
        /// 4096 / (10000 + rand(5001)); the fee in whole pounds is then placed against 0.75q, 1.25q
        /// and 1.5q: at or below 0.75q is +1, below 1.25q is 0, below 1.5q is -1, and from 1.5q up it is
        /// -2, the one value that refuses. Only the bands are compared, so the value returned is a
        /// verdict and not an amount.
        ///
        /// ⚠ THE DIVISOR IS 10000 + rand(5001), READ at 0x80090DC0..0x80090DDC: the base is loaded,
        /// rand(base / 2 + 1) is rolled, and the roll is ADDED to the base. §2.6 writes it as
        /// `(10000/2 + 1 + rand(5001))`, which puts the base in the roll's bound and drops it from the
        /// sum; parkopen.md §3.2 had already corrected that to the reading here, and the binary agrees
        /// with parkopen. The division is by a positive value, so it cannot trap.
        ///
        /// ⚠ THE ROLL COMES FIRST. rand(5001) is drawn before the gate test at 0x80090E30, so a park with
        /// no rides and no fee still spends a die on its verdict of 0.</summary>
        public static int FeeVerdict(int intensitySum, Money fee, IRandomSource rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            int divisor = VerdictDivisorBase + rng.Next(VerdictDivisorBase / 2 + 1);
            int q = (intensitySum << 12) / divisor;
            int pounds = (int)fee.Pounds;

            // Signed 32-bit, as the original's compare (0x80090E38..0x80090E48) is: not `q == 0`.
            if (!(q > VerdictGate) && !(pounds > VerdictGate)) return 0;

            int neutralBelow = (q * VerdictNeutralBelow) >> 12;
            int refuseAt = (q * VerdictRefuseAt) >> 12;
            int bargainAt = (q * VerdictBargainAt) >> 12;
            if (pounds < neutralBelow) return pounds > bargainAt ? 0 : 1;
            return pounds < refuseAt ? -1 : VerdictRefuse;
            // sound (gp, 0x13, verdict) follows; no dice.
        }

        /// <summary>State 37 -- pay entry fee (0x80090EDC).
        ///
        /// ⭐ IT SETS 45 FIRST AND 38 OVERRIDES. The handler clears the target, clears the queue bit,
        /// restores the walk speed and SETS 45 (0x80090F34) before it has looked at the fee; only a
        /// guest that cannot or will not pay is then moved to 38 (0x80090FC8). So a guest that pays is
        /// already walking in, and a refused one has already had its speed and bit reset for the walk
        /// out. §2.6 lists the target and the speed; the queue bit (0x80090F14, 0x80093ECC with 0) is
        /// READ here and is what takes a paying guest out of "queued" for every later test of the bit.
        ///
        /// ⚠ DICE: the verdict's rand(5001) is rolled only when money is strictly more than the fee
        /// (0x80090F68 skips the call). A broke guest costs no die.</summary>
        public static PayOutcome PayEntryFee(Visitor guest, IEntranceWorld world, IRandomSource rng)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            guest.HasTarget = false;
            guest.GateScratch = 0;
            guest.InQueue = false;
            guest.WalkSpeed = guest.NormalWalkSpeed;
            guest.SetState(VisitorState.WalkIn);

            Money fee = world.EntryFee;
            if (!(guest.Money > fee))
            {
                guest.SetState(VisitorState.LeavingPark);
                return PayOutcome.CannotAfford;
            }
            int verdict = FeeVerdict(Sum(world.AttractionIntensities), fee, rng);
            if (verdict < VerdictRefuse + 1)
            {
                guest.SetState(VisitorState.LeavingPark);
                return PayOutcome.Refused;
            }
            world.CountAdmission();
            world.BookEntryFee(guest);
            guest.Money = guest.Money - fee;
            return PayOutcome.Paid;
        }

        static int Sum(IEnumerable<int> values)
        {
            int s = 0;
            if (values != null) foreach (int v in values) s += v;
            return s;
        }

        /// <summary>State 45 -- walk in (0x800912BC): from my own tile scan up to fifteen rows in +y and
        /// walk ONE waypoint to the centre of the first path tile, purpose 13. Arrival 13 is the
        /// greeting and Idle.
        ///
        /// ⚠ THE FLAG-0x08 TEST IS STICKY, DO NOT FIX. `seen` is set the first time a row's tile has
        /// flag 0x08 (0x80091344) and never cleared, so every LATER row is tested for path whether or not
        /// it has the flag itself. paths.md §5 reads it the same way: "after a tile with flag 0x08, the
        /// first type-2/13 tile". A port that required the flag and the type on the same tile would walk
        /// nobody in.
        ///
        /// ⚠ AN EMPTY WAYPOINT POOL DOES NOT END THE SCAN. The -1 test at 0x800913BC jumps to the row
        /// step (0x8009142C), so the loop goes on looking with the purpose already 13 and the waypoints
        /// already freed, and the next path tile tries the pool again.</summary>
        public static GateWalkOutcome WalkIn(Visitor guest, IEntranceWorld world)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));

            var (px, py) = world.Position(guest);
            int x = px >> 8, y = py >> 8;
            bool seen = false;
            for (int rows = WalkInScanRows; rows != 0; rows--, y++)
            {
                if (world.TileHasFlag8(x, y)) seen = true;
                if (!seen || !world.TileIsPath(x, y)) continue;
                guest.Purpose = Purpose.Pleased;
                world.FreeWaypoints(guest);
                if (!world.TrySetSingleWaypoint(guest, VisitorQueue.Centre(x), VisitorQueue.Centre(y))) continue;
                guest.SetState(VisitorState.WalkToWaypoint);
                return GateWalkOutcome.Stepping;
            }
            return GateWalkOutcome.NoPathTile;
        }

        // ───────────────────────── the way out ─────────────────────────

        /// <summary>State 38 -- leave (0x80090FF4): with exits, path to the exit point with purpose 14
        /// and gate-side flags; without any, despawn on the spot (0x800910A4: removed, staff told,
        /// bubble off, waypoints freed).
        ///
        /// ⚠ "EVERY 64 TICKS" IS STAGGERED BY V+0x10. 0x80091014..0x80091024 compares `now &amp; 63` with
        /// `V+0x10 &amp; 63`, the same field that staggers the decision and the queue's boredom passes; §2.6
        /// gives the period without the phase. Same period, one request per 64 ticks per guest, but a
        /// park full of leavers does not all ask the pathfinder on the same tick.</summary>
        public static GateWalkOutcome Leave(Visitor guest, IEntranceWorld world)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (world.ExitCount == 0)
            {
                world.RemoveFromPark(guest);
                guest.Bubble = 0;
                world.FreeWaypoints(guest);
                return GateWalkOutcome.Despawned;
            }
            long now = world.NowTick;
            if ((now & LeaveRetryMask) != (guest.DecisionStagger & LeaveRetryMask)) return GateWalkOutcome.NotMyTick;

            guest.Purpose = Purpose.Turnstile14;
            if (!world.TryPathToParkPoint(guest, ExitPoint, LeavePathFlags, SecondaryPathFlags))
                return GateWalkOutcome.PathRefused;
            guest.WaitUntil = now;
            guest.SetState(VisitorState.WalkToBin);   // SET 11 (0x80091094)
            return GateWalkOutcome.Walking;
        }

        /// <summary>State 48 -- walk out (0x8009154C): rand(exit count) picks an exit, purpose 9, and the
        /// request goes out with flags (1, 0). Arrival 9 despawns.
        ///
        /// ⚠ rand(0) IS NOT GUARDED (0x8009155C..0x80091568 rolls straight off 0x800540AC's count). It
        /// cannot happen from 38, which despawns an exitless guest before it gets here; a host that puts
        /// a guest in 48 with no exits is asking the dice for `rand() % 0`, as the original would.</summary>
        public static GateWalkOutcome WalkOut(Visitor guest, IEntranceWorld world, IRandomSource rng)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            int exit = rng.Next(world.ExitCount);
            guest.Purpose = Purpose.LeavePark;
            if (!world.TryPathToExit(guest, exit, ExitPathFlags, SecondaryPathFlags)) return GateWalkOutcome.PathRefused;
            guest.WaitUntil = world.NowTick;
            guest.SetState(VisitorState.WalkToBin);   // SET 11 (0x800915D8)
            return GateWalkOutcome.Walking;
        }

        // ───────────────────────── arrival arms 11-16 (slot 35, table 0x800E3A94) ─────────────────────────

        /// <summary>Arrivals 11 and 12 (0x8008DF44, 0x8008DFA0): recompute my lane slot and compare it
        /// with where I stand. On it: 44. Not on it: 42 for purpose 11, 43 for purpose 12 -- the only
        /// difference between the two arms, as with the queue's 3 and 10.
        ///
        /// ⚠ "AT THE SLOT" IS EXACT EQUALITY OF BOTH 8.8 COORDINATES (0x8008DF5C..0x8008DF7C), not the
        /// same tile. A guest one unit short walks the whole request again.</summary>
        public static Arrival ArriveAtLaneSlot(Visitor guest, ITurnstileWorld world, bool shuffling)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));

            var slot = LaneSlotFor(guest, world);
            var pos = world.Position(guest);
            if (pos.X == slot.X && pos.Y == slot.Y)
            {
                guest.SetState(VisitorState.LaneFront);
                return Arrival.AtLaneFront;
            }
            guest.SetState(shuffling ? VisitorState.ShuffleInLane : VisitorState.WalkToLaneSlot);
            return shuffling ? Arrival.ShufflingInLane : Arrival.WalkingToLaneSlot;
        }

        /// <summary>Arrival 14 (0x8008E0E0), at the exit point on the way out: V+0x28 := 1, 46, one more
        /// waiting, facing 4. The 16-bit store leaves the pointer view of V+0x28 unequal to any object,
        /// so <see cref="Visitor.HasTarget"/> is cleared here as the guard's port clears its culprit at
        /// the same arrival.
        ///
        /// ⚠ THE COUNTER IS IN §2.3, NOT IN §2.6's ROW FOR 38, which stops at "V+0x28 := 1, 46". READ
        /// 0x8008E0FC: `jal 0x8005996C`, the same increment as arrival 15 and as the guard's two gate
        /// arrivals. Both ways up to the gate add one; every crossing takes one away.</summary>
        public static Arrival ArriveAtExitPoint(Visitor guest, ITurnstileWorld world)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));

            guest.GateScratch = 1;
            guest.HasTarget = false;
            guest.SetState(VisitorState.AtGate);
            world.Counter80103950++;
            guest.Facing = LeavingFacing;
            return Arrival.AtGate;
        }

        /// <summary>Arrival 15 (0x8008E0A8), at the spawn point on the way in: V+0x28 := 0, 46, one more
        /// waiting, facing 0.</summary>
        public static Arrival ArriveAtSpawnPoint(Visitor guest, ITurnstileWorld world)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));

            guest.GateScratch = 0;
            guest.HasTarget = false;
            guest.SetState(VisitorState.AtGate);
            world.Counter80103950++;
            guest.Facing = ArrivingFacing;
            return Arrival.AtGate;
        }

        /// <summary>Arrival 16 (0x8008E124), on the gate line. V+0x28 non-zero means the guest is on its
        /// way OUT: the word is zeroed, 48. Zero means it is coming IN: rand(2) picks a lane, a lane whose
        /// count is already 11 or more is swapped for the other, the lane goes into V+0x28, 42. Both ways
        /// then cross (0x80059984: one fewer waiting, one more admitted).
        ///
        /// ⚠ ONE FLIP, NOT A SEARCH. If both lanes are full the guest goes to the other full lane; nothing
        /// waits for room. ⚠ A LEAVER ROLLS NOTHING: rand(2) is inside the entering branch only.</summary>
        public static Arrival ArriveAtGate(Visitor guest, ITurnstileWorld world, IRandomSource rng)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            if (guest.GateScratch != 0)
            {
                guest.GateScratch = 0;
                guest.HasTarget = false;
                guest.SetState(VisitorState.WalkOut);
                Cross(world);
                return Arrival.LeavingThroughGate;
            }
            int lane = rng.Next(2);
            if (world.LaneCount(lane) >= LaneFullAt) lane = 1 - lane;
            guest.GateScratch = lane;
            guest.SetState(VisitorState.WalkToLaneSlot);
            Cross(world);
            return Arrival.WalkingToLaneSlot;
        }

        /// <summary>0x80059984.</summary>
        static void Cross(ITurnstileWorld world)
        {
            world.Counter80103950--;
            world.Counter80103954++;
        }

        // ───────────────────────── messages (slot 40, 0x8008F880) ─────────────────────────

        /// <summary>The rows of the message handler the entrance owns: 1, 2, 4 and 9. Returns whether
        /// the row did anything; 6, 7 and 10 are <see cref="VisitorQueue.OnMessage"/>'s and answer false
        /// here, as do the ignored 3, 5 and 8.
        ///
        /// ⚠ MESSAGE 9 IS NOT GATED ON STATE. 0x8008FC34 sets 47 whatever the guest was doing; only its
        /// sender's filter (0x80052204: state 46) keeps it to the gate. Messages 1 and 2 ARE gated on 11
        /// (0x8008F964, 0x8008F9C0).
        ///
        /// Message 2's purpose table (0x800E3B9C, purposes 3..22): 11 re-enters 42, 15 re-enters 36, 22
        /// drops the target for Idle, 14 gets one retry, 3 is the queue's (leave it or drop the target;
        /// not ported here, answers false), and everything else -- including 9, the walk out -- loses
        /// rand(15) happiness, gains rand(2) boredom, drops the target and wanders (0x8008FB50).</summary>
        public static bool OnMessage(Visitor guest, IEntranceWorld world, EntranceMessage id, IRandomSource rng)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            switch (id)
            {
                case EntranceMessage.PathReady:
                    if (guest.State != VisitorState.WalkToBin) return false;
                    guest.ExitPathRetried = false;
                    guest.SetState(VisitorState.WalkToWaypoint);
                    guest.Animation = VisitorQueue.AnimationWander;
                    return true;

                case EntranceMessage.PathFailed:
                    if (guest.State != VisitorState.WalkToBin) return false;
                    return OnPathFailed(guest, world, rng);

                case EntranceMessage.ThrownOut:
                    world.RemoveFromPark(guest);
                    return true;

                case EntranceMessage.Admit:
                    guest.SetState(VisitorState.PickLane);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>Message 2 in state 11, by purpose (0x8008F9CC..0x8008FB94).
        ///
        /// ⚠ THE EXIT RETRY SETS ITS FLAG ONLY AFTER THE REQUEST IS ACCEPTED (0x8008FA84 follows the
        /// 0x8008FA60 test), so a refused retry leaves the flag clear and the guest in 11 -- with no
        /// handler that will ask again. Only the recovery routine 0x80091EA8 (§2.11, not ported) maps a
        /// stuck purpose 14 back to 38. A second failure WITH the flag set goes to Idle (0x8008FAA0).</summary>
        static bool OnPathFailed(Visitor guest, IEntranceWorld world, IRandomSource rng)
        {
            switch (guest.Purpose)
            {
                case Purpose.QueueWalk:
                    return false;

                case Purpose.Turnstile11:
                    guest.SetState(VisitorState.WalkToLaneSlot);
                    return true;

                case Purpose.Turnstile14:
                    if (guest.ExitPathRetried)
                    {
                        guest.SetState(VisitorState.Idle);
                        return true;
                    }
                    guest.Purpose = Purpose.Turnstile14;   // re-stored at 0x8008FA2C, the same value
                    if (!world.TryPathToParkPoint(guest, ExitPoint, RetryPathFlags, SecondaryPathFlags)) return true;
                    guest.WaitUntil = world.NowTick;
                    guest.ExitPathRetried = true;
                    guest.SetState(VisitorState.WalkToBin);   // SET 11 again (0x8008FA9C)
                    return true;

                case Purpose.Turnstile15:
                    guest.SetState(VisitorState.SpawnToGate);
                    return true;

                case Purpose.ClearTarget:
                    guest.SetState(VisitorState.Idle);
                    guest.HasTarget = false;
                    return true;

                default:
                    guest.Happiness = Stat.Sub(guest.Happiness, rng.Next(WanderHappinessLossMax));
                    guest.Boredom = Stat.Add(guest.Boredom, rng.Next(WanderBoredomGainMax));
                    guest.HasTarget = false;
                    guest.SetState(VisitorState.RandomWander);
                    return true;
            }
        }
    }

    /// <summary>The gate's own two routines, run by the host's frame tick rather than by any guest:
    /// 0x80052148, which opens batches and sends message 9, and 0x800592CC, which moves the head of a
    /// lane through the turnstile. Both are READ (behaviour.md §2.6, arrivals.md §4).</summary>
    public static class Turnstile
    {
        /// <summary>0x80052148, called every tick from the bus tick. Opens a batch when someone is waiting
        /// and none is open; while a batch is open and fewer than 11 have crossed, sends message 9 to
        /// every guest and every staff member in 46; closes the batch when nobody is waiting, or when 11
        /// have crossed and the bus is at the gate. Returns how many message 9s went out.
        ///
        /// ⚠ THE COUNT THAT CLOSES A BATCH IS CROSSINGS, NOT DELIVERIES. 0x80103954 moves at arrival 16
        /// (0x80059984), so a crowd of thirty in 46 is all sent on the first tick, and the batch stays
        /// open, sending to whoever is still in 46, until eleven have actually stepped over the line.
        /// ⚠ A FULL BATCH CLOSES ONLY WITH THE BUS AT THE GATE (0x800522DC..0x800522E4): in any other
        /// phase it stays open, sending nothing, until the waiting count drops to zero.</summary>
        public static int Admit(IEntranceWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (world.Counter80103950 != 0 && world.GateBatch == 0)
            {
                world.GateBatch = 1;
                world.Counter80103954 = 0;
            }

            int sent = 0;
            if (world.Counter80103954 < VisitorEntrance.BatchMax && world.Counter80103950 != 0 && world.GateBatch == 1)
            {
                foreach (var guest in world.Guests)
                    if (guest.State == VisitorState.AtGate) { world.DeliverMessage(guest, (int)EntranceMessage.Admit, 0, 0); sent++; }
                foreach (var staff in world.Staff)
                    if (staff.State == GuardStates.AtGate) { world.DeliverStaffMessage(staff, (int)EntranceMessage.Admit); sent++; }
            }

            bool full = !(world.Counter80103954 < VisitorEntrance.BatchMax);
            if ((full && world.BusPhase == VisitorEntrance.BusPhaseAtGate) || world.Counter80103950 == 0)
                if (world.GateBatch == 1) world.GateBatch = 0;
            return sent;
        }

        /// <summary>One lane of 0x800592CC, called every tick for lanes 0 and 1. On the lane's own tick
        /// (`now &amp; 31 == lane`, so lane 0 on ticks 0, 32, 64... and lane 1 one tick later), if the head
        /// of the lane is in 44: the head goes to 37, the lane count drops by one, the head leaves the
        /// list, and everyone still in the list is sent message 6 with stagger 0 and target 43. A head
        /// that has not yet reached 44 blocks the lane for 32 ticks and nobody behind it is told anything.
        ///
        /// §2.6 says "every 32 ticks"; the per-lane offset is READ (0x80059300..0x80059308: the tick's low
        /// five bits are compared with the lane index). The routine also calls vtable slot 3 on every lane
        /// member every tick (0x8005942C..0x80059444); that slot is not identified and is not modelled.</summary>
        public static LaneTickOutcome LaneTick(IEntranceWorld world, int lane)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            if ((world.NowTick & VisitorEntrance.LaneAdmitMask) != lane) return LaneTickOutcome.NotMyTick;
            var members = world.Lane(lane);
            if (members.Count == 0) return LaneTickOutcome.LaneEmpty;
            var head = members[0];
            if (head.State != VisitorState.LaneFront) return LaneTickOutcome.HeadNotAtFront;

            head.SetState(VisitorState.PayEntryFee);
            world.SetLaneCount(lane, world.LaneCount(lane) - 1);
            world.RemoveFromLane(lane, head);
            var behind = world.Lane(lane);
            for (int i = 0; i < behind.Count; i++)
                world.DeliverMessage(behind[i], (int)QueueMessage.Shuffle,
                    VisitorEntrance.ShuffleStagger, (int)VisitorEntrance.ShuffleTarget);
            return LaneTickOutcome.Admitted;
        }
    }
}
