using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>The attraction status machine (rides.md, jump tables 0x800E1340 / 0x800E1370).</summary>
    public class AttractionStatusTests
    {
        sealed class World : IAttractionWorld
        {
            public bool IsRide { get; set; } = true;
            public bool BuildAnimationComplete { get; set; }
            public int Reliability { get; set; } = 100;
            public int CyclesRun { get; set; }
            public int CyclesPerLoad { get; set; } = 3;
            public bool IsEmpty { get; set; }
            public bool MechanicAssigned { get; set; }
            public int Ejections { get; private set; }
            public int SmokeCleared { get; private set; }
            public System.Collections.Generic.List<int> Messages { get; } = new();

            public void PostMessage(int id) => Messages.Add(id);
            public void EjectEveryone() => Ejections++;
            public void ClearSmoke() => SmokeCleared++;
        }

        // ⭐ GUESTS SEE ONLY 2, 10 AND 11. This is slot 86, and it is the same flag the ride score reads
        // as OpenToGuests - so a ride under construction or broken down is invisible to the chooser, not
        // merely unattractive. REJECTS treating "not closed" as open, which would let guests queue at a
        // ride that is still being built.
        [Theory]
        [InlineData(AttractionStatus.Running, true)]
        [InlineData(AttractionStatus.Loading, true)]
        [InlineData(AttractionStatus.Unloading, true)]
        [InlineData(AttractionStatus.JustPlaced, false)]
        [InlineData(AttractionStatus.UnderConstruction, false)]
        [InlineData(AttractionStatus.ClosedByPlayer, false)]
        [InlineData(AttractionStatus.AboutToBreakDown, false)]
        [InlineData(AttractionStatus.BrokenDown, false)]
        [InlineData(AttractionStatus.UnderRepair, false)]
        public void OnlyThreeStatusesAcceptGuests(AttractionStatus status, bool open)
        {
            Assert.Equal(open, AttractionLifecycle.OpenToGuests(status));
        }

        // ⭐ THE RIDE LEAVES CONSTRUCTION WHEN THE ANIMATION ENDS, not on a timer. A longer build model
        // means a longer build. REJECTS a fixed construction duration.
        [Fact]
        public void ConstructionEndsWithTheAnimationAndNotBefore()
        {
            var world = new World { BuildAnimationComplete = false };
            Assert.Equal(AttractionStatus.UnderConstruction,
                AttractionLifecycle.Tick(AttractionStatus.UnderConstruction, world));

            world.BuildAnimationComplete = true;
            Assert.Equal(AttractionStatus.Loading,
                AttractionLifecycle.Tick(AttractionStatus.UnderConstruction, world));
        }

        // ⚠ ONLY RIDES WAIT IN LOADING. For a shop this status lasts one tick and is really
        // "build complete". REJECTS making everything queue through a loading phase.
        [Fact]
        public void ShopsPassStraightThroughLoadingAndRidesDoNot()
        {
            Assert.Equal(AttractionStatus.Running,
                AttractionLifecycle.Tick(AttractionStatus.Loading, new World { IsRide = false }));
            Assert.Equal(AttractionStatus.Loading,
                AttractionLifecycle.Tick(AttractionStatus.Loading, new World { IsRide = true }));
        }

        // A ride placed and left alone gets all the way to running on its own.
        [Fact]
        public void APlacedRideOpensItselfWithoutAnythingPushingIt()
        {
            var world = new World { IsRide = true, BuildAnimationComplete = true, IsEmpty = true };

            // The build tool moves 0 -> 1 (slot 39); everything after is the ride's own doing.
            var s = AttractionStatus.UnderConstruction;
            s = AttractionLifecycle.Tick(s, world);
            Assert.Equal(AttractionStatus.Loading, s);

            // A ride sits in loading until its own LoadGuests moves it, so the status machine holds.
            Assert.Equal(AttractionStatus.Loading, AttractionLifecycle.Tick(s, world));

            // A shop, by contrast, is open on the next tick.
            Assert.Equal(AttractionStatus.Running,
                AttractionLifecycle.Tick(s, new World { IsRide = false }));
        }

        // Running counts cycles and unloads when it has done its allotted number.
        [Fact]
        public void RunningUnloadsAfterItsCycleCount()
        {
            var world = new World { CyclesPerLoad = 3, CyclesRun = 2 };
            Assert.Equal(AttractionStatus.Running, AttractionLifecycle.Tick(AttractionStatus.Running, world));
            world.CyclesRun = 3;
            Assert.Equal(AttractionStatus.Unloading, AttractionLifecycle.Tick(AttractionStatus.Running, world));
        }

        [Fact]
        public void UnloadingReturnsToLoadingOnlyWhenEmpty()
        {
            var world = new World { IsEmpty = false };
            Assert.Equal(AttractionStatus.Unloading, AttractionLifecycle.Tick(AttractionStatus.Unloading, world));
            world.IsEmpty = true;
            Assert.Equal(AttractionStatus.Loading, AttractionLifecycle.Tick(AttractionStatus.Unloading, world));
        }

        // Entering Running resets the cycle count, or a reopened ride would unload immediately.
        [Fact]
        public void EnteringRunningResetsTheCycleCount()
        {
            var world = new World { CyclesRun = 99 };
            AttractionLifecycle.Enter(AttractionStatus.Running, world);
            Assert.Equal(0, world.CyclesRun);
        }

        // ⚠ EXACTLY ZERO, NOT "LOW". A ride at reliability 1 stays in the warning state forever; only a
        // mechanic moves it on. REJECTS `<= threshold`, which would break every warned ride immediately.
        [Theory]
        [InlineData(1, AttractionStatus.AboutToBreakDown)]
        [InlineData(0, AttractionStatus.BrokenDown)]
        public void OnlyReliabilityOfExactlyZeroActuallyBreaks(int reliability, AttractionStatus expected)
        {
            var world = new World { Reliability = reliability };
            Assert.Equal(expected, AttractionLifecycle.Tick(AttractionStatus.AboutToBreakDown, world));
        }

        // The warning and the ejection are the same event for a ride with nothing left.
        [Fact]
        public void TheWarningEjectsRidersOnlyWhenThereIsNoLifeLeft()
        {
            var dying = new World { Reliability = 0 };
            AttractionLifecycle.Enter(AttractionStatus.AboutToBreakDown, dying);
            Assert.Equal(new[] { AttractionLifecycle.MessageAboutToBreak }, dying.Messages);
            Assert.Equal(1, dying.Ejections);

            var warned = new World { Reliability = 5 };
            AttractionLifecycle.Enter(AttractionStatus.AboutToBreakDown, warned);
            Assert.Equal(0, warned.Ejections);            // warned, but still running
        }

        // Which breakdown message you get depends on whether help is coming.
        [Theory]
        [InlineData(true, AttractionLifecycle.MessageBrokenMechanicComing)]
        [InlineData(false, AttractionLifecycle.MessageBrokenNoMechanics)]
        public void TheBreakdownMessageDependsOnWhetherAMechanicIsComing(bool assigned, int message)
        {
            var world = new World { MechanicAssigned = assigned };
            AttractionLifecycle.Enter(AttractionStatus.BrokenDown, world);
            Assert.Equal(new[] { message }, world.Messages);
            Assert.Equal(1, world.Ejections);
        }

        // ⭐ REOPEN IS A COMMAND, NOT A STATE, and it forks: a ride goes back through LOADING with its
        // reliability restored, a shop goes straight to open. REJECTS a single destination for both.
        [Fact]
        public void ReopeningForksByKindAndNeverRests()
        {
            var ride = new World { IsRide = true, Reliability = 0 };
            Assert.Equal(AttractionStatus.Loading, AttractionLifecycle.Enter(AttractionStatus.Reopen, ride));
            Assert.Equal(100, ride.Reliability);
            Assert.Equal(1, ride.SmokeCleared);

            var shop = new World { IsRide = false, Reliability = 0 };
            Assert.Equal(AttractionStatus.Running, AttractionLifecycle.Enter(AttractionStatus.Reopen, shop));
            Assert.Equal(0, shop.Reliability);            // a shop's reliability is not touched
            Assert.Equal(1, shop.SmokeCleared);
        }

        // ⚠ THE STATUSES THAT WAIT. These do nothing on their own tick and are moved by the build tool,
        // the player or a mechanic. Pinned so "nothing happens here" stays deliberate rather than looking
        // like an unfinished case.
        [Theory]
        [InlineData(AttractionStatus.JustPlaced)]
        [InlineData(AttractionStatus.ClosedByPlayer)]
        [InlineData(AttractionStatus.BrokenDown)]
        [InlineData(AttractionStatus.UnderRepair)]
        public void TheWaitingStatusesStayPutOnTheirOwn(AttractionStatus status)
        {
            Assert.Equal(status, AttractionLifecycle.Tick(status, new World()));
            Assert.True(AttractionLifecycle.WaitsForSomeoneElse(status));
        }

        // ⚠ 8 AND 9 ARE DEAD IN THIS BUILD: tick code exists, no SetStatus site writes either. Kept at
        // their numbers so the enum matches the jump table.
        [Fact]
        public void TheTwoDeadStatusesKeepTheirNumbersAndDoNothing()
        {
            Assert.Equal(8, (int)AttractionStatus.Dead8);
            Assert.Equal(9, (int)AttractionStatus.Dead9);
            Assert.Equal(AttractionStatus.Dead8, AttractionLifecycle.Tick(AttractionStatus.Dead8, new World()));
            Assert.False(AttractionLifecycle.OpenToGuests(AttractionStatus.Dead8));
        }

        // REJECTS the phase-counted unload applying to anything but a ride. Every class's status-2 tick
        // is the word at its vtable + 0x23C: NonPathedRide 0x800E5514 -> 0x8009CA60 (counts phases, and
        // moves to 11), but Shop 0x800E6A8C, Feature 0x800DCA5C and SideShow 0x800E6D64 all -> 0x80065B78,
        // whose whole body is the animation clock with its completion DISCARDED. A shop counts nothing and
        // never unloads, so the ride's test must not run on it -- a shop asking for zero cycles would
        // otherwise leave for 11 on the tick it opened.
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(3)]
        public void OnlyARideEverLeavesRunningForUnloading(int cyclesPerLoad)
        {
            var shop = new World { IsRide = false, CyclesPerLoad = cyclesPerLoad, CyclesRun = cyclesPerLoad + 5 };
            Assert.Equal(AttractionStatus.Running, AttractionLifecycle.Tick(AttractionStatus.Running, shop));

            var ride = new World { IsRide = true, CyclesPerLoad = cyclesPerLoad, CyclesRun = cyclesPerLoad + 5 };
            Assert.Equal(AttractionStatus.Unloading, AttractionLifecycle.Tick(AttractionStatus.Running, ride));
        }

        // The numbers are the interface: they index the game's own jump tables.
        [Fact]
        public void TheStatusNumbersMatchTheJumpTable()
        {
            Assert.Equal(0, (int)AttractionStatus.JustPlaced);
            Assert.Equal(1, (int)AttractionStatus.UnderConstruction);
            Assert.Equal(2, (int)AttractionStatus.Running);
            Assert.Equal(10, (int)AttractionStatus.Loading);
            Assert.Equal(11, (int)AttractionStatus.Unloading);
        }
    }
}
