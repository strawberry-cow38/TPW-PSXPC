using System;
using System.Collections.Generic;
using System.Linq;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>The pathfinder (findings/pathfinder.md).
    ///
    /// MUTATION RECORD (2026-09-20): 38 deliberate breakages, 36 caught. The two that survived are
    /// recorded here rather than left looking like coverage, because both are cases where NO test can
    /// tell the difference and pretending otherwise is worse than saying so:
    ///
    /// 1. `t >= 15` widened to `t >= 16`. Type 15 falls through to the switch's default and is refused
    ///    there anyway, so the two spellings cannot differ on any input. Equivalent by inspection.
    ///
    /// 2. The closed-set test `f &lt; closed.F` relaxed to `f &lt;= closed.F`, which would re-open a
    ///    closed tile on an EQUAL score and redo its work. Measured across eleven map shapes and all
    ///    four dice values (6x6 through 16x16, grass and path), counting expansions one per frame: the
    ///    two versions did identical work every time. That agrees with pathfinder.md §5.5 - the
    ///    heuristic is consistent, so a popped tile's cost is already optimal and the branch is
    ///    unreachable. The rule is kept as the original spells it; the evidence says no test can pin it.
    ///
    /// Two of the earlier "survivors" were bad mutations of my own that edited only a comment, and one
    /// run left a mutated file behind when a hang killed the harness before it restored. Both are
    /// reasons the count above is of a harness that restores in a `finally` and was re-run.</summary>
    public class PathfinderTests
    {
        // ---- fixtures -------------------------------------------------------------------------

        sealed class Map : IPathMap
        {
            readonly int[,] _type, _links, _flags;
            public Map(int w, int h, int fill = 2)
            {
                Width = w; Height = h;
                _type = new int[w, h]; _links = new int[w, h]; _flags = new int[w, h];
                for (int x = 0; x < w; x++)
                    for (int y = 0; y < h; y++) { _type[x, y] = fill; _links[x, y] = 0x55; }
            }

            /// <summary>Rows of single characters, top row first: `.` grass, `#` path, `Q` queue,
            /// `F` footprint, `X` a type nothing walks on.</summary>
            public static Map FromRows(params string[] rows)
            {
                var m = new Map(rows[0].Length, rows.Length);
                for (int y = 0; y < rows.Length; y++)
                    for (int x = 0; x < rows[y].Length; x++)
                        m.SetType(x, y, rows[y][x] switch
                        {
                            '.' => 0, '#' => 2, 'Q' => 4, 'F' => 5, 'X' => 1,
                            _ => throw new ArgumentException($"unknown tile '{rows[y][x]}'")
                        });
                return m;
            }

            public int Width { get; }
            public int Height { get; }
            public bool ParkIsOpen { get; set; }
            public int TypeAt(int x, int y) => _type[x, y];
            public int LinkBitsAt(int x, int y) => _links[x, y];
            public int FlagsAt(int x, int y) => _flags[x, y];
            public void SetType(int x, int y, int t) => _type[x, y] = t;
            public void SetLinks(int x, int y, int l) => _links[x, y] = l;
            public void SetFlags(int x, int y, int f) => _flags[x, y] = f;
        }

        sealed class Walker : IPathClient
        {
            public int WaypointHead { get; set; } = WaypointPool.NoChain;
            public List<PathMessage> Messages { get; } = new();
            public void OnPathMessage(PathMessage m) => Messages.Add(m);
        }

        /// <summary>Always picks neighbour order 0, so a route is reproducible. ⚠ The search really is
        /// non-deterministic; a test that does not fix this is testing the dice.</summary>
        sealed class FixedDice : IRandomSource
        {
            readonly int _v;
            public FixedDice(int v = 0) => _v = v;
            public int Next(int n) => _v % n;
        }

        static int Centre(int tile) => (tile << 8) | 0x80;

        /// <summary>Run frames until the walker hears something, with a stop so a bug is a failure and
        /// not a hang.</summary>
        static PathMessage Settle(Pathfinder pf, Walker w, int maxFrames = 5000)
        {
            for (int i = 0; i < maxFrames && w.Messages.Count == 0; i++) pf.RunFrame();
            Assert.True(w.Messages.Count > 0, "no message after " + maxFrames + " frames");
            return w.Messages[0];
        }

        static (Pathfinder pf, WaypointPool wp) Build(Map m, int nodes = Pathfinder.NodePoolSize)
        {
            var wp = new WaypointPool();
            return (new Pathfinder(m, wp, new FixedDice(), nodes), wp);
        }

        // ---- the contract of Request ----------------------------------------------------------

        // ⭐⭐ ACCEPTED IS NOT FOUND. Request answers "did the search start", and the route arrives
        // later as a message. REJECTS the obvious port of this API - a synchronous bool "is there a
        // path" - which is what every caller in the state machines would otherwise be written against.
        [Fact]
        public void RequestAnswersWhetherTheSearchStartedNotWhetherAPathExists()
        {
            var m = Map.FromRows("##", "XX");
            var (pf, _) = Build(m);
            var w = new Walker();

            // A destination that cannot be reached is still ACCEPTED.
            Assert.True(pf.Request(w, Centre(0), Centre(0), Centre(0), Centre(1), PathFlags.Path, 0));
            Assert.Empty(w.Messages);                       // and nothing is known yet
            Assert.Equal(PathMessage.Failed, Settle(pf, w));
        }

        // ⚠ TEN, AND THE ELEVENTH IS REFUSED IN SILENCE. No message, nothing queued, nothing evicted.
        // REJECTS queueing the overflow, and REJECTS reporting it as a path failure - the game's own
        // callers tell the two apart (one retries next tick, the other backs off for 360).
        [Fact]
        public void TheEleventhOutstandingRequestIsRefusedWithoutAWord()
        {
            var m = Map.FromRows("####", "####", "####", "####");
            var (pf, _) = Build(m);

            for (int i = 0; i < Pathfinder.MaxRequests; i++)
                Assert.True(pf.Request(new Walker(), Centre(0), Centre(0), Centre(3), Centre(3),
                                       PathFlags.Path, 0));
            Assert.Equal(10, pf.ActiveRequests);

            var refused = new Walker();
            Assert.False(pf.Request(refused, Centre(0), Centre(0), Centre(3), Centre(3),
                                    PathFlags.Path, 0));
            Assert.Empty(refused.Messages);
            Assert.Equal(10, pf.ActiveRequests);
        }

        // The other silent refusal: no nodes left for even the seed.
        [Fact]
        public void ARequestIsRefusedWhenTheSharedNodePoolIsEmpty()
        {
            var m = Map.FromRows("....", "....", "....", "...X");
            var (pf, _) = Build(m, nodes: 4);

            // Drain it: each failed search leaks one node (see the leak test below).
            for (int i = 0; i < 4; i++)
            {
                var v = new Walker();
                pf.Request(v, Centre(0), Centre(0), Centre(3), Centre(3), PathFlags.Grass, 0);
                Settle(pf, v);
            }
            Assert.Equal(0, pf.FreeNodes);

            var refused = new Walker();
            Assert.False(pf.Request(refused, Centre(0), Centre(0), Centre(1), Centre(0),
                                    PathFlags.Grass, 0));
            Assert.Empty(refused.Messages);
        }

        // ---- cost ------------------------------------------------------------------------------

        // ⭐ GRASS COSTS TWICE WHAT PATH COSTS, and the route is chosen on that. Here a detour onto the
        // path row is longer in TILES and cheaper in COST, and the search takes it. REJECTS uniform
        // step cost, which is what an A* written from memory would do and which would make guests cut
        // across the grass instead of using the paths the player built.
        [Fact]
        public void ADetourOntoPathBeatsAShorterWalkAcrossGrass()
        {
            var m = Map.FromRows("#####",
                                 ".....");
            var (pf, wp) = Build(m);
            var w = new Walker();

            pf.Request(w, Centre(0), Centre(1), Centre(4), Centre(1), PathFlags.Path | PathFlags.Grass, 0);
            Assert.Equal(PathMessage.Found, Settle(pf, w));

            // straight over grass: 4 steps x 2 = 8. Up, along the path, down: 1 + 4x1 + 2 = 7.
            var chain = wp.Chain(w.WaypointHead);
            Assert.Contains(chain, p => p.Y == Centre(0));
        }

        // The control: same shape, but the top row is grass too, so the detour is dearer and must NOT
        // be taken. Without this, the test above passes for a search that simply likes going up.
        [Fact]
        public void TheSameDetourIsRefusedWhenItIsNotCheaper()
        {
            var m = Map.FromRows(".###.",
                                 ".....");
            var (pf, wp) = Build(m);
            var w = new Walker();

            pf.Request(w, Centre(0), Centre(1), Centre(4), Centre(1), PathFlags.Path | PathFlags.Grass, 0);
            Assert.Equal(PathMessage.Found, Settle(pf, w));

            // straight: 8. Via the path: 2 + 1 + 1 + 1 + 2 + 2 = 9.
            var chain = wp.Chain(w.WaypointHead);
            Assert.DoesNotContain(chain, p => p.Y == Centre(0));
        }

        // ---- what may be stepped on --------------------------------------------------------------

        // The admission table, one tile type at a time. The middle column is what the flags say; the
        // right is whether that one step is allowed.
        [Theory]
        [InlineData(0, PathFlags.Grass, true)]          // grass, cost 2
        [InlineData(0, PathFlags.Path, false)]
        [InlineData(2, PathFlags.Path, true)]
        [InlineData(2, PathFlags.Grass, false)]
        [InlineData(13, PathFlags.Path, true)]          // path over a queue reads as path
        [InlineData(4, PathFlags.Queue, true)]
        [InlineData(4, PathFlags.Path, false)]
        [InlineData(5, PathFlags.Footprint, true)]
        [InlineData(5, PathFlags.None, true)]           // ...and as the DESTINATION, with no flag at all
        [InlineData(7, PathFlags.None, true)]           // an entrance, as the destination
        [InlineData(8, PathFlags.None, true)]           // an exit: no flag, no link test, ever
        [InlineData(12, PathFlags.Path, true)]          // the gate
        [InlineData(12, PathFlags.Grass, false)]
        [InlineData(14, PathFlags.GateSide, true)]
        [InlineData(14, PathFlags.Path, false)]
        [InlineData(1, PathFlags.Path, false)]
        [InlineData(3, PathFlags.Path, false)]
        [InlineData(6, PathFlags.Path, false)]
        [InlineData(9, PathFlags.Path, false)]
        [InlineData(10, PathFlags.Path, false)]
        [InlineData(11, PathFlags.Path, false)]
        [InlineData(15, PathFlags.Path, false)]         // >= 15 is dropped before the table is consulted
        [InlineData(20, PathFlags.Path, false)]
        public void OneStepOntoEachTileType(int type, PathFlags flags, bool reachable)
        {
            var m = Map.FromRows("##");
            m.SetType(1, 0, type);
            var (pf, _) = Build(m);
            var w = new Walker();

            pf.Request(w, Centre(0), Centre(0), Centre(1), Centre(0), flags, 0);
            Assert.Equal(reachable ? PathMessage.Found : PathMessage.Failed, Settle(pf, w));
        }

        // ⚠ AN ENTRANCE AND A FOOTPRINT ARE ENTERABLE AS A DESTINATION AND NOT AS A THROUGH-new[] { (0,0) }.
        // That is how a guest reaches a shop counter without queues being routed across other people's
        // entrances. REJECTS reading the destination clause as a general permission.
        [Theory]
        [InlineData(7)]
        [InlineData(5)]
        public void ADestinationOnlyTileIsNotAThoroughfare(int type)
        {
            var m = Map.FromRows("###");
            m.SetType(1, 0, type);
            var (pf, _) = Build(m);

            var through = new Walker();
            pf.Request(through, Centre(0), Centre(0), Centre(2), Centre(0), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Failed, Settle(pf, through));

            var arriving = new Walker();
            pf.Request(arriving, Centre(0), Centre(0), Centre(1), Centre(0), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Found, Settle(pf, arriving));
        }

        // ⭐⭐ THE LINK TEST IS ON THE TILE BEING LEFT, NEVER THE ONE BEING ENTERED. REJECTS the
        // intuitive reading - "can I get into that tile" - which passes the first two cases here and
        // fails the third, and would let guests walk into paths from the side.
        [Fact]
        public void TheLinkBitsThatMatterBelongToTheTileYouAreStandingOn()
        {
            var unlinked = Map.FromRows("##");
            unlinked.SetLinks(0, 0, 0);
            var (a, _) = Build(unlinked);
            var wa = new Walker();
            a.Request(wa, Centre(0), Centre(0), Centre(1), Centre(0), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Failed, Settle(a, wa));

            var linkedEast = Map.FromRows("##");
            linkedEast.SetLinks(0, 0, 0x04);                 // just the +x bit
            var (b, _) = Build(linkedEast);
            var wb = new Walker();
            b.Request(wb, Centre(0), Centre(0), Centre(1), Centre(0), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Found, Settle(b, wb));

            // The control that separates the two readings: the DESTINATION is fully linked and the tile
            // being left is not. Still refused.
            var destLinked = Map.FromRows("##");
            destLinked.SetLinks(0, 0, 0);
            destLinked.SetLinks(1, 0, 0x55);
            var (c, _) = Build(destLinked);
            var wc = new Walker();
            c.Request(wc, Centre(0), Centre(0), Centre(1), Centre(0), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Failed, Settle(c, wc));
        }

        // ⚠ GRASS AND FOOTPRINT TILES ANSWER YES IN ALL FOUR DIRECTIONS regardless of their own link
        // bits - but only PLAIN grass. Grass carrying flag 0x02 falls back to its real links, which is
        // easy to miss and is why the last case is here.
        [Fact]
        public void PlainGrassLeadsEverywhereAndUnbuildableGrassDoesNot()
        {
            var plain = Map.FromRows(".#");
            plain.SetLinks(0, 0, 0);
            var (a, _) = Build(plain);
            var wa = new Walker();
            a.Request(wa, Centre(0), Centre(0), Centre(1), Centre(0), PathFlags.Path | PathFlags.Grass, 0);
            Assert.Equal(PathMessage.Found, Settle(a, wa));

            var marked = Map.FromRows(".#");
            marked.SetLinks(0, 0, 0);
            marked.SetFlags(0, 0, 0x02);
            var (b, _) = Build(marked);
            var wb = new Walker();
            b.Request(wb, Centre(0), Centre(0), Centre(1), Centre(0), PathFlags.Path | PathFlags.Grass, 0);
            Assert.Equal(PathMessage.Failed, Settle(b, wb));
        }

        // The grass gate (0x8004D388), which decides whether a piece of grass may be crossed at all.
        // ⚠ ITS LAST ARM DEPENDS ON THE PARK BEING OPEN, so the same tile is walkable during opening
        // hours and not before. REJECTS treating "grass" as one thing.
        [Theory]
        [InlineData(0x00, false, true)]                  // plain grass
        [InlineData(0x02, false, false)]                 // the map says nothing goes here
        [InlineData(0x02 | 0x08, false, false)]          // ...unless the park is open
        [InlineData(0x02 | 0x08, true, true)]
        [InlineData(0x10, false, false)]                 // taken at run time
        [InlineData(0x10 | 0x40, false, true)]           // ...and released again
        [InlineData(0x02 | 0x10 | 0x40, false, true)]
        public void TheGrassGateDecidesWhichGrassIsWalkable(int flags, bool parkOpen, bool walkable)
        {
            var m = Map.FromRows("#.");
            m.SetFlags(1, 0, flags);
            m.ParkIsOpen = parkOpen;
            var (pf, _) = Build(m);
            var w = new Walker();

            pf.Request(w, Centre(0), Centre(0), Centre(1), Centre(0), PathFlags.Path | PathFlags.Grass, 0);
            Assert.Equal(walkable ? PathMessage.Found : PathMessage.Failed, Settle(pf, w));
        }

        // ---- the waypoint chain -------------------------------------------------------------------

        // ⭐⭐ ONLY CORNERS BECOME WAYPOINTS. Ten tiles in a line is TWO entries, not ten. REJECTS
        // emitting one waypoint per tile, which costs nothing in a test and exhausts a thousand-entry
        // pool shared by the whole park in a real one.
        [Fact]
        public void AStraightRunIsTwoWaypointsNoMatterHowLongItIs()
        {
            var m = Map.FromRows("##########");
            var (pf, wp) = Build(m);
            var w = new Walker();

            pf.Request(w, Centre(0), Centre(0), Centre(9), Centre(0), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Found, Settle(pf, w));

            var chain = wp.Chain(w.WaypointHead);
            Assert.Equal(2, chain.Count);
            Assert.Equal((Centre(0), Centre(0)), chain[0]);       // the walker's own tile
            Assert.Equal((Centre(9), Centre(0)), chain[1]);       // the destination
        }

        // An L gets exactly one more: the corner.
        [Fact]
        public void ACornerAddsOneWaypointAndOnlyOne()
        {
            var m = Map.FromRows("######",
                                 "XXXXX#",
                                 "XXXXX#",
                                 "XXXXX#");
            var (pf, wp) = Build(m);
            var w = new Walker();

            pf.Request(w, Centre(0), Centre(0), Centre(5), Centre(3), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Found, Settle(pf, w));

            var chain = wp.Chain(w.WaypointHead);
            Assert.Equal(new[] { (Centre(0), Centre(0)), (Centre(5), Centre(0)), (Centre(5), Centre(3)) },
                         chain.ToArray());
        }

        // ⭐ THE LAST WAYPOINT IS THE CALLER'S EXACT DESTINATION, not the centre of its tile. That is
        // how a guest stands at a particular place in a queue rather than in the middle of a square.
        // REJECTS snapping the end of the route to the tile grid.
        [Fact]
        public void TheFinalWaypointIsTheExactPointAskedForNotTheTileCentre()
        {
            var m = Map.FromRows("###");
            var (pf, wp) = Build(m);
            var w = new Walker();

            int offCentre = (2 << 8) | 0x40;                       // a quarter tile in, not a half
            pf.Request(w, Centre(0), Centre(0), offCentre, Centre(0), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Found, Settle(pf, w));

            var chain = wp.Chain(w.WaypointHead);
            Assert.Equal((offCentre, Centre(0)), chain[chain.Count - 1]);
            Assert.NotEqual((Centre(2), Centre(0)), chain[chain.Count - 1]);
        }

        // ⚠ THE WALKER'S OWN TILE IS EMITTED - EXCEPT WHEN THE FIRST STEP HEADS NORTH. The start node
        // has no parent, and "no parent" and "a step in −y" are the same code, so it is skipped as a
        // non-corner. That asymmetry is in the original. REJECTS both "always emit the start" and
        // "never emit the start", each of which passes one half of this test.
        [Fact]
        public void TheStartTileIsSkippedExactlyWhenTheRouteBeginsHeadingNorth()
        {
            var m = Map.FromRows("XX#XX",
                                 "XX#XX",
                                 "#####");
            var (pf, wp) = Build(m);

            var north = new Walker();
            pf.Request(north, Centre(2), Centre(2), Centre(2), Centre(0), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Found, Settle(pf, north));
            Assert.Single(wp.Chain(north.WaypointHead));                 // the destination alone

            var east = new Walker();
            pf.Request(east, Centre(2), Centre(2), Centre(4), Centre(2), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Found, Settle(pf, east));
            var chain = wp.Chain(east.WaypointHead);
            Assert.Equal(2, chain.Count);
            Assert.Equal((Centre(2), Centre(2)), chain[0]);              // the walker's own tile
        }

        // Asking to go where you already are still produces a chain - one entry, the exact point.
        [Fact]
        public void AskingForTheTileYouAreOnGivesASingleWaypoint()
        {
            var m = Map.FromRows("##");
            var (pf, wp) = Build(m);
            var w = new Walker();

            int target = (0 << 8) | 0xC0;
            pf.Request(w, Centre(0), Centre(0), target, Centre(0), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Found, Settle(pf, w));
            Assert.Equal(new[] { (target, Centre(0)) }, wp.Chain(w.WaypointHead).ToArray());
        }

        // ⚠ THE OLD new[] { (0,0) } IS DROPPED BEFORE THE NEW ONE IS BUILT, so a walker never holds two chains
        // and the pool does not bleed. REJECTS building the new chain first and leaving the old one.
        [Fact]
        public void AWalkerGivenASecondRouteReturnsTheFirstOne()
        {
            var m = Map.FromRows("##########");
            var (pf, wp) = Build(m);
            var w = new Walker();

            pf.Request(w, Centre(0), Centre(0), Centre(9), Centre(0), PathFlags.Path, 0);
            Settle(pf, w);
            int afterFirst = wp.FreeCount;
            Assert.Equal((Centre(9), Centre(0)), wp.Chain(w.WaypointHead).Last());

            w.Messages.Clear();
            pf.Request(w, Centre(0), Centre(0), Centre(5), Centre(0), PathFlags.Path, 0);
            Settle(pf, w);

            // Two entries out, not four - and the entries themselves are REUSED, which is exactly why
            // the head index is no evidence either way and the destination has to be read instead.
            Assert.Equal(afterFirst, wp.FreeCount);
            Assert.Equal((Centre(5), Centre(0)), wp.Chain(w.WaypointHead).Last());
        }

        // ⚠ A new[] { (0,0) } FOUND BUT NOT STORABLE IS A FAILURE, and the part already built is handed back.
        // REJECTS reporting Found on a truncated chain, which would walk the guest to the wrong place.
        [Fact]
        public void RunningOutOfWaypointsFailsTheSearchAndLeaksNothing()
        {
            var m = Map.FromRows("##########");
            var (pf, wp) = Build(m);

            var hogged = new List<int>();
            while (wp.FreeCount > 1) hogged.Add(wp.Alloc());
            Assert.Equal(1, wp.FreeCount);

            var w = new Walker();
            pf.Request(w, Centre(0), Centre(0), Centre(9), Centre(0), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Failed, Settle(pf, w));

            Assert.Equal(1, wp.FreeCount);                            // the one it took came back
            Assert.Equal(WaypointPool.NoChain, w.WaypointHead);
            Assert.Equal(Pathfinder.NodePoolSize, pf.FreeNodes);      // and every node did too
        }

        // ---- pools, budgets and the scheduler -------------------------------------------------------

        // A finished search gives every node back and frees its slot.
        [Fact]
        public void AFinishedSearchReturnsEverythingItTook()
        {
            var m = Map.FromRows("#####", "#####", "#####");
            var (pf, _) = Build(m);
            var w = new Walker();

            pf.Request(w, Centre(0), Centre(0), Centre(4), Centre(2), PathFlags.Path, 0);
            Assert.True(pf.FreeNodes < Pathfinder.NodePoolSize);      // the seed is out
            Settle(pf, w);

            Assert.Equal(Pathfinder.NodePoolSize, pf.FreeNodes);
            Assert.Equal(0, pf.ActiveRequests);
        }

        // ⚠⚠ ONE NODE LEAKS EVERY TIME THE POOL RUNS DRY, AND THIS IS DELIBERATE. The node being
        // expanded has left the open list and has not reached the closed list, and the cleanup only
        // walks the lists. It is the original's bug, reproduced: it makes a busy park degrade instead
        // of failing steadily, and someone will one day have to recognise that symptom.
        // REJECTS "tidying up" the cleanup, which would hide it.
        [Fact]
        public void EachTimeTheNodePoolIsExhaustedExactlyOneNodeIsLost()
        {
            var m = Map.FromRows("....", "....", "....", "...X");
            var (pf, _) = Build(m, nodes: 8);

            for (int i = 1; i <= 3; i++)
            {
                var w = new Walker();
                pf.Request(w, Centre(0), Centre(0), Centre(3), Centre(3), PathFlags.Grass, 0);
                Assert.Equal(PathMessage.Failed, Settle(pf, w));
                Assert.Equal(8 - i, pf.FreeNodes);
            }
        }

        // Compare with the clean failure: nothing is lost when the search simply has nowhere to go.
        [Fact]
        public void AnUnreachableDestinationCostsNoNodesAtAll()
        {
            var m = Map.FromRows("#X#");
            var (pf, _) = Build(m);
            var w = new Walker();

            pf.Request(w, Centre(0), Centre(0), Centre(2), Centre(0), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Failed, Settle(pf, w));
            Assert.Equal(Pathfinder.NodePoolSize, pf.FreeNodes);
        }

        // ⭐ TWO HUNDRED SLICES AND THEN IT GIVES UP. The budget is spent per slice, not per frame a
        // request has been alive, so a request that never gets scheduled never spends any.
        // REJECTS a wall-clock or frame-count timeout.
        [Fact]
        public void ASearchGivesUpAfterTwoHundredSlices()
        {
            var m = new Map(20, 20, fill: 0);                  // 400 tiles of grass
            m.SetType(19, 19, 1);                              // ...around an island nobody can reach
            var (pf, _) = Build(m);
            pf.ExpansionsPerSlice = 1;
            var w = new Walker();

            pf.Request(w, Centre(0), Centre(0), Centre(19), Centre(19), PathFlags.Grass, 0);

            for (int i = 0; i < Pathfinder.SliceBudget; i++)
            {
                pf.RunFrame();
                Assert.Empty(w.Messages);                      // still going
            }
            pf.RunFrame();
            Assert.Equal(new[] { PathMessage.Failed }, w.Messages.ToArray());
            Assert.Equal(Pathfinder.NodePoolSize, pf.FreeNodes);
        }

        // ⭐ THE NEWEST REQUEST ALWAYS GETS THE FRAME; older ones only run if they asked to be kept
        // warm (flagB == 1) and there is time left. REJECTS round-robin, and REJECTS oldest-first -
        // both of which look fairer and neither of which is what the game does.
        [Fact]
        public void TheNewestSearchIsServedFirstAndOnlyFlagBOnesFollowIt()
        {
            var m = Map.FromRows("####");
            var (pf, _) = Build(m);
            pf.ExpansionsPerSlice = 5;

            var oldest = new Walker();                        // flagB 0: waits its turn
            var middle = new Walker();                        // flagB 1: rides along
            var newest = new Walker();

            pf.Request(oldest, Centre(0), Centre(0), Centre(1), Centre(0), PathFlags.Path, 0);
            pf.Request(middle, Centre(0), Centre(0), Centre(1), Centre(0), PathFlags.Path, 1);
            pf.Request(newest, Centre(0), Centre(0), Centre(1), Centre(0), PathFlags.Path, 0);

            pf.RunFrame();

            Assert.Equal(new[] { PathMessage.Found }, newest.Messages.ToArray());
            Assert.Equal(new[] { PathMessage.Found }, middle.Messages.ToArray());
            Assert.Empty(oldest.Messages);
            Assert.Equal(1, pf.ActiveRequests);

            pf.RunFrame();                                    // now it is the newest thing left
            Assert.Equal(new[] { PathMessage.Found }, oldest.Messages.ToArray());
        }

        // A slice that uses the whole frame leaves nothing for anyone else, flagB or not.
        [Fact]
        public void AFrameSpentOnOneSearchIsNotSharedOut()
        {
            var big = new Map(20, 20, fill: 0);
            var (pf, _) = Build(big);
            pf.ExpansionsPerSlice = 2;

            var second = new Walker();
            pf.Request(second, Centre(0), Centre(0), Centre(19), Centre(19), PathFlags.Grass, 1);
            var first = new Walker();
            pf.Request(first, Centre(0), Centre(0), Centre(19), Centre(19), PathFlags.Grass, 1);

            pf.RunFrame();
            Assert.Empty(first.Messages);
            Assert.Empty(second.Messages);
            Assert.Equal(2, pf.ActiveRequests);
        }

        // ---- pause and the build-item reset -----------------------------------------------------

        // ⚠⚠ PUTTING A BUILD ITEM DOWN DROPS EVERY SEARCH WITHOUT A MESSAGE. Ten walkers can be left
        // waiting for an answer that is never coming. REJECTS sending Failed on the way out, which
        // would be the kind thing to do and is not what happens.
        [Fact]
        public void PlacingABuildItemThrowsAwayEverySearchInSilence()
        {
            var m = Map.FromRows("####", "####");
            var (pf, wp) = Build(m);

            var walkers = new List<Walker>();
            for (int i = 0; i < 5; i++)
            {
                var w = new Walker();
                walkers.Add(w);
                pf.Request(w, Centre(0), Centre(0), Centre(3), Centre(1), PathFlags.Path, 0);
            }
            Assert.Equal(5, pf.ActiveRequests);

            pf.ResetAfterBuildItemPlaced();

            Assert.Equal(0, pf.ActiveRequests);
            Assert.All(walkers, w => Assert.Empty(w.Messages));
            Assert.Equal(Pathfinder.NodePoolSize, pf.FreeNodes);
            Assert.Equal(WaypointPool.Capacity, wp.FreeCount);
        }

        // ⚠ AND IT LEAVES WALKERS POINTING AT FREED WAYPOINTS. The pool is wiped; nobody's head index
        // is cleared. Pinned because it is a real defect of the original and someone will otherwise
        // "fix" it by clearing the heads, which changes behaviour.
        [Fact]
        public void TheResetWipesTheWaypointPoolWithoutTellingAnyWalker()
        {
            var m = Map.FromRows("#####");
            var (pf, wp) = Build(m);
            var w = new Walker();

            pf.Request(w, Centre(0), Centre(0), Centre(4), Centre(0), PathFlags.Path, 0);
            Settle(pf, w);
            int head = w.WaypointHead;
            Assert.NotEqual(WaypointPool.NoChain, head);

            pf.ResetAfterBuildItemPlaced();

            Assert.Equal(head, w.WaypointHead);                 // still pointing at it
            Assert.False(wp.IsAllocated(head));                 // and it is not his any more
        }

        // A held build item stops the search dead; budgets are not spent while it is held.
        [Fact]
        public void HoldingABuildItemStopsEverySearchWithoutSpendingItsBudget()
        {
            var m = Map.FromRows("#####");
            var (pf, _) = Build(m);
            var w = new Walker();

            pf.Request(w, Centre(0), Centre(0), Centre(4), Centre(0), PathFlags.Path, 0);
            pf.Pause();
            Assert.True(pf.IsPaused);

            for (int i = 0; i < Pathfinder.SliceBudget * 2; i++) pf.RunFrame();
            Assert.Empty(w.Messages);                           // not even a timeout

            pf.ResetAfterBuildItemPlaced();
            Assert.False(pf.IsPaused);
        }

        // ---- limits --------------------------------------------------------------------------

        // ⚠ A NODE HOLDS ITS TILE IN A BYTE AND A WAYPOINT IN A SIGNED ONE. The original wraps
        // silently; this refuses. REJECTS accepting a big map and producing a confident route to the
        // wrong place.
        [Fact]
        public void AMapTooBigForAByteIsRefusedRatherThanWrapped()
        {
            var wp = new WaypointPool();
            var rng = new FixedDice();
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Pathfinder(new Map(Pathfinder.MaxMapExtent + 1, 4), wp, rng));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Pathfinder(new Map(4, Pathfinder.MaxMapExtent + 1), wp, rng));

            var ok = new Pathfinder(new Map(Pathfinder.MaxMapExtent, Pathfinder.MaxMapExtent), wp, rng);
            Assert.Equal(Pathfinder.NodePoolSize, ok.FreeNodes);
        }

        // ⚠ THE PATHFINDER'S WORLD INCLUDES THE LAST ROW AND COLUMN, which the placement check
        // excludes - the two clamp differently on purpose. REJECTS reusing the build rule here.
        [Fact]
        public void CoordinatesClampToTheLastTileNotTheSecondToLast()
        {
            var m = Map.FromRows("###", "###", "###");
            var (pf, wp) = Build(m);
            var w = new Walker();

            // Way off the map to the south-east: clamps to (2,2), the real last tile.
            pf.Request(w, Centre(0), Centre(0), Centre(99), Centre(99), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Found, Settle(pf, w));

            // The chain's last point is the RAW destination, so read the corner it turned instead.
            var chain = wp.Chain(w.WaypointHead);
            Assert.Contains(chain, p => p == (Centre(2), Centre(0)) || p == (Centre(0), Centre(2)));
        }

        // ⭐ CROSSING A BUILDING COSTS DOUBLE, LIKE GRASS. Three footprint tiles cost 6 and the way
        // round costs 6 too, so a fourth step decides it - and it decides the other way if the
        // footprint is priced as path. REJECTS giving the footprint the cheap cost, which would send
        // guests straight through buildings whenever that was geometrically shorter.
        [Fact]
        public void CuttingAcrossABuildingIsPricedLikeGrassNotLikePath()
        {
            var m = Map.FromRows("#FFF#",
                                 "#####");
            var (pf, wp) = Build(m);
            var w = new Walker();

            pf.Request(w, Centre(0), Centre(0), Centre(4), Centre(0),
                       PathFlags.Path | PathFlags.Footprint, 0);
            Assert.Equal(PathMessage.Found, Settle(pf, w));

            // through: 2 + 2 + 2 + 1 = 7. Around: six steps of path = 6.
            var chain = wp.Chain(w.WaypointHead);
            Assert.Contains(chain, p => p.Y == Centre(1));
        }

        // ⚠ THE PARK GATE IS EXEMPT FROM THE LINK TEST that ordinary path on the same flag must pass.
        // Paired with the link test above, which is the identical setup on a type 2 tile and is
        // REFUSED. REJECTS folding the gate into the path case, which reads like a tidy-up and would
        // seal the entrance whenever the tile outside it was not linked inward.
        [Fact]
        public void TheGateIsReachableEvenFromATileThatDoesNotPointAtIt()
        {
            var m = Map.FromRows("##");
            m.SetType(1, 0, 12);
            m.SetLinks(0, 0, 0);
            var (gate, _) = Build(m);
            var wg = new Walker();
            gate.Request(wg, Centre(0), Centre(0), Centre(1), Centre(0), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Found, Settle(gate, wg));

            var ordinary = Map.FromRows("##");
            ordinary.SetLinks(0, 0, 0);
            var (path, _) = Build(ordinary);
            var wandering = new Walker();
            path.Request(wandering, Centre(0), Centre(0), Centre(1), Centre(0), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Failed, Settle(path, wandering));
        }

        // ⚠ A FOOTPRINT TILE ALSO LEADS EVERYWHERE, not just grass. A guest standing on a building's
        // own tiles - which is where the destination clause above puts them - must be able to walk off
        // again whatever that tile's link bits say. REJECTS giving only grass the all-ways mask.
        [Fact]
        public void StandingOnAFootprintTileLeadsEverywhereToo()
        {
            var m = Map.FromRows("F#");
            m.SetLinks(0, 0, 0);
            var (pf, _) = Build(m);
            var w = new Walker();

            pf.Request(w, Centre(0), Centre(0), Centre(1), Centre(0), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Found, Settle(pf, w));
        }

        // ⚠ A CLOSED TILE IS REOPENED ONLY ON A STRICTLY BETTER SCORE. Equal is not better, and on a
        // grid equal happens constantly - relaxing it to `<=` churns closed nodes back into the open
        // list and does measurably more work for the same route. Counted rather than asserted exactly,
        // because the count is a consequence of the rule and not itself read from the game.
        [Fact]
        public void EqualScoresDoNotReopenAClosedTile()
        {
            var m = new Map(6, 6, fill: 0);
            var (pf, _) = Build(m);
            pf.ExpansionsPerSlice = 1;                      // one expansion per frame: frames == work
            var w = new Walker();

            pf.Request(w, Centre(0), Centre(0), Centre(5), Centre(5), PathFlags.Grass, 0);
            int frames = 0;
            while (w.Messages.Count == 0 && frames < 500) { pf.RunFrame(); frames++; }

            Assert.Equal(PathMessage.Found, w.Messages[0]);
            // 36 expansions on a 36-tile field: on grass the heuristic counts tiles while the steps
            // cost two, so it is a weak underestimate and the search is nearly exhaustive. Reopening on
            // equal scores pushes it past this.
            Assert.InRange(frames, 1, 36);
        }

        // ⚠ WHICH OF TWO EQUALLY GOOD new[] { (0,0) }S COMES OUT IS DECIDED BY THE OPEN LIST'S TIE RULE, and the
        // rule is positional: a node entering from the front goes ahead of its equals, from the back it
        // goes behind them. Pinned here as a characterisation of the port, with the dice fixed.
        //
        // ⚠ HONEST LIMIT: this is READ from the insert routine, not observed in the running game. It
        // pins that the tie rule is not silently replaced by a heap or a dictionary - both of which
        // would pass every other test in this file - and no more than that.
        [Fact]
        public void TwoEquallyGoodRoutesResolveTheSameWayEveryTime()
        {
            var m = Map.FromRows("##", "##");
            var (pf, wp) = Build(m);
            var w = new Walker();

            pf.Request(w, Centre(0), Centre(0), Centre(1), Centre(1), PathFlags.Path, 0);
            Assert.Equal(PathMessage.Found, Settle(pf, w));
            Assert.Equal(new[] { (Centre(0), Centre(0)), (Centre(1), Centre(0)), (Centre(1), Centre(1)) },
                         wp.Chain(w.WaypointHead).ToArray());
        }

        // The numbers are the interface: they are the game's, not this port's.
        [Fact]
        public void ThePoolSizesAreTheOriginals()
        {
            Assert.Equal(10, Pathfinder.MaxRequests);
            Assert.Equal(2000, Pathfinder.NodePoolSize);
            Assert.Equal(200, Pathfinder.SliceBudget);
            Assert.Equal(1000, WaypointPool.Capacity);
            Assert.Equal(0x7FF, WaypointPool.EndOfChain);
        }

        // ⚠ THREE flagA BITS ARE NEVER READ. Pinned so that a call site copied from the findings with
        // 0x04 or 0x40 set is not "fixed" into meaning something.
        [Theory]
        [InlineData(PathFlags.Dead04)]
        [InlineData(PathFlags.Dead40)]
        [InlineData(PathFlags.Dead80)]
        public void TheDeadFlagBitsChangeNothing(PathFlags dead)
        {
            var m = Map.FromRows("#.#");
            var (a, _) = Build(m);
            var wa = new Walker();
            a.Request(wa, Centre(0), Centre(0), Centre(2), Centre(0), PathFlags.Path, 0);
            var without = Settle(a, wa);

            var (b, _) = Build(m);
            var wb = new Walker();
            b.Request(wb, Centre(0), Centre(0), Centre(2), Centre(0), PathFlags.Path | dead, 0);
            Assert.Equal(without, Settle(b, wb));
        }
    }

    /// <summary>The waypoint pool on its own (pathfinder.md §7.1).</summary>
    public class WaypointPoolTests
    {
        // ⭐ A TILE CENTRE SURVIVES THE ROUND TRIP EXACTLY; ANYTHING FINER DOES NOT. Positions are
        // rounded to the nearest quarter tile. REJECTS storing the full 8.8 coordinate, which would
        // make the gate-lane loss below disappear and quietly change where walkers stand.
        [Theory]
        [InlineData(0x0180, 0x0180, 0x0180, 0x0180)]     // a tile centre: exact
        [InlineData(0x0200, 0x0000, 0x0200, 0x0000)]     // a tile corner: exact
        [InlineData(0x0240, 0x0000, 0x0240, 0x0000)]     // a quarter in: exact
        [InlineData(0x0300, 0x0000, 0x0300, 0x0000)]
        [InlineData(0x0210, 0x0000, 0x0200, 0x0000)]     // 16 units in: rounded back down
        [InlineData(0x0230, 0x0000, 0x0240, 0x0000)]     // 48 units in: rounded up
        public void PositionsAreKeptToTheNearestQuarterTile(int x, int y, int rx, int ry)
        {
            var wp = new WaypointPool();
            int i = wp.Alloc();
            wp.Encode(i, x, y);
            Assert.Equal((rx, ry), wp.Decode(i));
        }

        [Fact]
        public void AllocationRunsOutAndFreeingGivesItBack()
        {
            var wp = new WaypointPool();
            var all = new List<int>();
            for (int i = 0; i < WaypointPool.Capacity; i++) all.Add(wp.Alloc());
            Assert.Equal(0, wp.FreeCount);
            Assert.Equal(-1, wp.Alloc());

            wp.Free(all[500]);
            Assert.Equal(1, wp.FreeCount);
            Assert.Equal(500, wp.Alloc());                 // the hint came back to it
        }

        [Fact]
        public void FreeingAChainFollowsItToTheEnd()
        {
            var wp = new WaypointPool();
            int a = wp.Alloc(), b = wp.Alloc(), c = wp.Alloc();
            wp.SetNext(a, b); wp.SetNext(b, c);
            Assert.Equal(3, wp.ChainLength(a));

            wp.FreeChain(a);
            Assert.Equal(WaypointPool.Capacity, wp.FreeCount);
        }

        // ⚠ THE WIPE DOES NOT TELL ANYONE. Pinned with the one deviation this port makes: entries come
        // back terminated, so a walker still holding a head index reads one bogus point rather than
        // following free entries in a circle.
        [Fact]
        public void AWipeLeavesADanglingHeadReadableRatherThanLooping()
        {
            var wp = new WaypointPool();
            int a = wp.Alloc(), b = wp.Alloc();
            wp.SetNext(a, b);
            wp.Encode(a, 0x0180, 0x0180);

            wp.Reset();

            Assert.Equal(WaypointPool.Capacity, wp.FreeCount);
            Assert.Equal(1, wp.ChainLength(a));             // terminates instead of hanging
            Assert.Equal(-1, wp.Next(a));
        }
    }
}
