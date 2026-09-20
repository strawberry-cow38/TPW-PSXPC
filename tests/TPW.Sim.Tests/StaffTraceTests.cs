using System;
using System.Collections.Generic;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    public class StaffTraceTests
    {
        // REJECTS using visitor speeds, repair durations, a flat halfword table, or one speed per class.
        [Theory]
        [InlineData(0, 9, 10)] [InlineData(1, 12, 15)] [InlineData(2, 14, 20)]
        [InlineData(3, 16, 20)] [InlineData(4, 18, 18)]
        public void SkillControlsMechanicAndCleanerSpeed(int skill, int mechanic, int cleaner)
        {
            var m = new StaffMember(StaffKind.Mechanic) { Skill = skill };
            var c = new StaffMember(StaffKind.Cleaner) { Skill = skill };
            Assert.Equal(mechanic, StaffMotion.Speed(m, 31));
            Assert.Equal(cleaner, StaffMotion.Speed(c, 1));
            m.Skill = (skill + 1) % 5;
            Assert.Equal(new[] { 12, 14, 16, 18, 9 }[skill], StaffMotion.Speed(m));
        }

        // REJECTS using the whole skill byte or fabricating a clamped speed for the three absent rows.
        [Theory]
        [InlineData(StaffKind.Mechanic, 9)] [InlineData(StaffKind.Cleaner, 10)]
        public void SkillUsesThreeBitsButAbsentRowsAreExplicitlyUnsupported(StaffKind kind, int first)
        {
            var s = new StaffMember(kind) { Skill = 8 };
            Assert.Equal(first, StaffMotion.Speed(s));
            foreach (int skill in new[] { 5, 6, 7 })
            {
                s.Skill = skill;
                Assert.Throws<ArgumentOutOfRangeException>(() => StaffMotion.Speed(s));
            }
        }

        // REJECTS a visitor random roll, skill-based speed for the other classes, or dropping the fifth bit.
        [Theory]
        [InlineData(StaffKind.Guard)] [InlineData(StaffKind.Researcher)] [InlineData(StaffKind.Entertainer)]
        public void OtherClassesUseThePackedBaseSpeed(StaffKind kind)
        {
            var s = new StaffMember(kind) { Skill = 7 };
            Assert.Equal(15, StaffMotion.Speed(s));
            Assert.Equal(30, StaffMotion.Speed(s, 30));
            Assert.Equal(0, StaffMotion.Speed(s, 32));
            Assert.Equal(31, StaffMotion.Speed(s, 63));
        }

        // REJECTS rounding up, shifting by 12/8, treating displacement as tiles, and ignoring current skill.
        [Fact]
        public void StaffStepIsATruncatedEightPointEightDisplacement()
        {
            var s = new StaffMember(StaffKind.Mechanic) { Skill = 0 };
            Assert.Equal(9, StaffMotion.Step(s, 0x4000));
            Assert.Equal(5, StaffMotion.Step(s, 10000));
            s.Skill = 4;
            Assert.Equal(10, StaffMotion.Step(s, 10000));
            Assert.Equal(0, StaffMotion.Step(s, 0));
        }

        // REJECTS widening the multiply, arithmetic shift, or ignoring the host's current packed speed.
        [Fact]
        public void StepKeepsTheOriginalLowWordAndLogicalShift()
        {
            var s = new StaffMember(StaffKind.Guard);
            Assert.Equal(0, StaffMotion.Step(s, 143165577, 30)); // low word = 14
            Assert.Equal(245760, StaffMotion.Step(s, 0x08000000, 30)); // 0xF0000000 >> 14
        }

        // REJECTS screen-row/enum-order mixups and choosing the costumed guest for a uniformed employee.
        [Theory]
        [InlineData(StaffKind.Mechanic, 263, 264, 8)]
        [InlineData(StaffKind.Guard, 264, 308, 9)]
        [InlineData(StaffKind.Cleaner, 266, 352, 10)]
        [InlineData(StaffKind.Researcher, 274, 396, 11)]
        public void UniformedClassesHaveFixedResourceAndPeopleSheetRows(StaffKind kind, int resource, int first, int block)
        {
            for (int theme = 0; theme < 4; theme++)
                Assert.Equal(new StaffArt(resource, first, block), StaffAppearance.Art(kind, theme));
        }

        // REJECTS mapping a hired entertainer to sheet 269's costume block or sorting resource ids by theme.
        [Theory]
        [InlineData(0, 403)] [InlineData(1, 401)] [InlineData(2, 402)] [InlineData(3, 404)]
        public void EntertainersHaveSeparateFrameResources(int theme, int resource)
            => Assert.Equal(new StaffArt(resource, null, null), StaffAppearance.Art(StaffKind.Entertainer, theme));

        // REJECTS a plausible fallback guest sprite for an unknown kind or a fifth theme.
        [Fact]
        public void UnknownArtHasNoInventedFallback()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => StaffAppearance.Art((StaffKind)5, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => StaffAppearance.Art(StaffKind.Entertainer, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => StaffAppearance.Art(StaffKind.Entertainer, -1));
        }

        // REJECTS eight rows of five frames, mirroring only the folded directions, or subtracting camera rotation.
        [Theory]
        [InlineData(0, 0, 7, 7, true)] [InlineData(1, 0, 3, 11, true)]
        [InlineData(2, 0, 2, 18, true)] [InlineData(3, 0, 1, 25, true)]
        [InlineData(4, 0, 0, 32, true)] [InlineData(5, 0, 6, 30, false)]
        [InlineData(6, 0, 7, 23, false)] [InlineData(7, 0, 4, 12, false)]
        [InlineData(7, 2, 5, 13, true)] [InlineData(3, 2, 0, 24, false)]
        public void WalkUsesFiveStoredDirectionsAndEightFrames(int person, int camera, int frame, int expected, bool mirror)
            => Assert.Equal((expected, mirror), StaffAppearance.Walk(person, camera, frame));

        // REJECTS silently wrapping invalid frames, which can conceal a host still ticking a five-frame cycle.
        [Theory]
        [InlineData(-1)] [InlineData(8)]
        public void InvalidWalkFrameIsNotAValidSprite(int frame)
            => Assert.Throws<ArgumentOutOfRangeException>(() => StaffAppearance.Walk(0, 0, frame));

        sealed class HireWorld : IStaffHireWorld
        {
            public readonly List<string> Calls = new();
            public StaffMember Employee;
            public StaffKind AllocatedKind, RecordKind;
            public int AllocatedVariant, RecordVariant, Level = 9;
            public int Animation = -1, Speed = -1;
            public long HireDay = -1;
            public bool Full, Valid = true, Held, Finished, Employed;
            public Money? Charge;
            public long TotalDays => 123;
            public StaffMember AllocateAndEmploy(StaffKind kind, int variant)
            {
                Calls.Add("allocate"); AllocatedKind = kind; AllocatedVariant = variant;
                if (Full) return null;
                Employee = new StaffMember(kind) { Skill = 4, Morale = 12, Tiredness = 89,
                    Purpose = StaffPurpose.Strike, HasTarget = true };
                Employee.PushState(StaffState.Resting);
                Employed = true;
                return Employee;
            }
            public int RecruitLevel(StaffKind kind, int variant)
            { Assert.True(Employed); RecordKind = kind; RecordVariant = variant; return Level; }
            public void SetHireDay(StaffMember staff, long day)
            { Assert.Same(Employee, staff); HireDay = day; Calls.Add("date"); }
            public void ClearPatrolArea(StaffMember staff) { Assert.Same(Employee, staff); Calls.Add("patrol"); }
            public void InitializeAppearance(StaffMember staff, int animation, int packedBaseSpeed)
            { Assert.Same(Employee, staff); Animation = animation; Speed = packedBaseSpeed; Calls.Add("appearance"); }
            public void SetHeld(StaffMember staff, bool held)
            { Assert.Same(Employee, staff); Held = held; Calls.Add($"held:{held}"); }
            public bool IsPlacementPath(StaffMember staff) { Assert.Same(Employee, staff); Calls.Add("validate"); return Valid; }
            public bool TrySpend(Money amount)
            {
                Assert.False(Held); Assert.Equal(StaffState.Idle, Employee.State);
                Assert.Equal(0, Employee.StackDepth); Charge = amount; Calls.Add("spend");
                return false; // deliberately refused: the original does not test this result
            }
            public void FinishPlacement(StaffMember staff) { Assert.Same(Employee, staff); Finished = true; Calls.Add("finish"); }
            public void Release(StaffMember staff) { Assert.Same(Employee, staff); Employed = false; Calls.Add("release"); }
        }

        // REJECTS employment only at drop, random/zero skill, pay-grade +1, leaked reused-node state,
        // and quietly replacing wages.md's 100/0 with the disputed binary constructor rolls.
        [Theory]
        [InlineData(StaffKind.Mechanic)] [InlineData(StaffKind.Entertainer)] [InlineData(StaffKind.Cleaner)]
        [InlineData(StaffKind.Guard)] [InlineData(StaffKind.Researcher)]
        public void HireInitializesTheSelectedRecruitBeforeTheDrop(StaffKind kind)
        {
            var w = new HireWorld(); var s = StaffHiring.Begin(kind, 3, w);
            Assert.Same(w.Employee, s); Assert.Equal(kind, s.Kind);
            Assert.Equal(kind, w.AllocatedKind); Assert.Equal(kind, w.RecordKind);
            Assert.Equal(3, w.AllocatedVariant); Assert.Equal(3, w.RecordVariant);
            Assert.Equal(1, s.Skill); Assert.Equal(100, s.Morale); Assert.Equal(0, s.Tiredness);
            Assert.Equal((StaffPurpose)2, s.Purpose); Assert.False(s.HasTarget);
            Assert.Equal(StaffState.Idle, s.State); Assert.Equal(0, s.StackDepth);
            Assert.Equal(123, w.HireDay); Assert.Equal(13, w.Animation); Assert.Equal(15, w.Speed);
            Assert.True(w.Employed); Assert.True(w.Held); Assert.False(w.Finished); Assert.Null(w.Charge);
            Assert.Equal(new[] { "allocate", "patrol", "appearance", "date", "held:True" }, w.Calls);
        }

        // REJECTS initializing or charging a nonexistent employee when the host pool is exhausted.
        [Fact]
        public void NoPoolNodeDoesNotStartPlacement()
        {
            var w = new HireWorld { Full = true };
            Assert.Null(StaffHiring.Begin(StaffKind.Mechanic, 0, w));
            Assert.Equal(new[] { "allocate" }, w.Calls); Assert.False(w.Employed);
        }

        // REJECTS committing an invalid tile, removing an undropped recruit from employment,
        // charging its wage/training cost, ignoring the placement gate, or treating payment refusal as cancellation.
        [Fact]
        public void InvalidDropKeepsHoldingThenValidDropCommitsForZero()
        {
            var w = new HireWorld { Valid = false }; var s = StaffHiring.Begin(StaffKind.Mechanic, 1, w);
            s.PushState(StaffState.Walking); w.Calls.Clear();
            Assert.False(StaffHiring.TryPlace(s, w));
            Assert.True(w.Held); Assert.True(w.Employed); Assert.False(w.Finished); Assert.Null(w.Charge);
            Assert.Equal(StaffState.Walking, s.State); Assert.Equal(1, s.StackDepth);
            Assert.Equal(new[] { "validate" }, w.Calls);
            w.Valid = true; w.Calls.Clear();
            Assert.True(StaffHiring.TryPlace(s, w)); Assert.Equal(Money.Zero, w.Charge.Value);
            Assert.False(w.Held); Assert.True(w.Employed); Assert.True(w.Finished);
            Assert.Equal(new[] { "validate", "held:False", "spend", "finish" }, w.Calls);
        }

        // REJECTS cancellation that leaves the recruit in the wage list or issues a refund for a free hire.
        [Fact]
        public void CancelReturnsTheEmployedNodeWithoutPayment()
        {
            var w = new HireWorld(); var s = StaffHiring.Begin(StaffKind.Cleaner, 4, w);
            w.Calls.Clear(); StaffHiring.Cancel(s, w);
            Assert.False(w.Employed); Assert.False(w.Finished); Assert.Null(w.Charge);
            Assert.Equal(new[] { "release" }, w.Calls);
        }

        sealed class PathWorld : IStaffRidePathWorld
        {
            public readonly List<string> Calls = new();
            public QueueTile Entrance = new(4, 7);
            public List<QueueTile> Path = new() { new(4, 7), new(5, 7), new(5, 8) };
            public bool Accept = true;
            public (int X, int Y, int Flags, int Secondary) Request;
            public StaffMember RequestedStaff;
            public QueueTile EntranceOutside(StaffMember staff) { Calls.Add("entrance"); return Entrance; }
            public IReadOnlyList<QueueTile> QueuePath(StaffMember staff) { Calls.Add("queue"); return Path; }
            public void FreeWaypoints(StaffMember staff) { Calls.Add("free"); }
            public bool TryPath(StaffMember staff, int x, int y, int flags, int secondaryFlags)
            { Calls.Add("request"); RequestedStaff = staff; Request = (x, y, flags, secondaryFlags); return Accept; }
        }

        // REJECTS the queue tail/exit for a job, tile corners, reversed axes, wrong flags,
        // retaining the old path, or converting pathfinder refusal into success.
        [Theory]
        [InlineData(true)] [InlineData(false)]
        public void JobFreesOldPathAndRequestsTheOutsideEntranceCentre(bool accept)
        {
            var s = new StaffMember(StaffKind.Mechanic); var w = new PathWorld { Accept = accept };
            s.PushState(MechanicStates.GoToBrokenRide);
            Assert.Equal(accept, StaffRideNavigation.TryPathToJob(s, w));
            Assert.Equal((1152, 1920, 0x11, 0), w.Request); Assert.Same(s, w.RequestedStaff);
            Assert.Equal(new[] { "free", "entrance", "request" }, w.Calls);
            Assert.Equal(MechanicStates.GoToBrokenRide, s.State); Assert.Equal(1, s.StackDepth);
        }

        // REJECTS returning to the entrance/queue head, freeing the path in the leave handler,
        // changing state within this adapter, or swallowing a refused leave request.
        [Theory]
        [InlineData(true)] [InlineData(false)]
        public void LeaveRequestsTheLastStoredQueueTile(bool accept)
        {
            var s = new StaffMember(StaffKind.Mechanic); s.PushState(MechanicStates.LeavingRide);
            var w = new PathWorld { Accept = accept };
            Assert.Equal(accept, StaffRideNavigation.TryPathToLeavePoint(s, w));
            Assert.Equal((1408, 2176, 0x11, 0), w.Request); Assert.Same(s, w.RequestedStaff);
            Assert.Equal(new[] { "queue", "request" }, w.Calls);
            Assert.Equal(MechanicStates.LeavingRide, s.State); Assert.Equal(1, s.StackDepth);
        }

        // REJECTS refusing an empty queue or fabricating an entrance/exit fallback: slot 26 reads (0,0).
        [Fact]
        public void EmptyQueueStillPathsToTheZeroCountWordsUpperBytes()
        {
            var w = new PathWorld(); w.Path.Clear();
            Assert.True(StaffRideNavigation.TryPathToLeavePoint(new StaffMember(StaffKind.Mechanic), w));
            Assert.Equal((128, 128, 0x11, 0), w.Request);
        }

        // REJECTS signed-byte queue coordinates and failing to narrow host coordinates to stored bytes.
        [Fact]
        public void QueueTailCoordinatesAreUnsignedBytes()
        {
            var w = new PathWorld { Path = new() { new(-1, 384) } };
            StaffRideNavigation.TryPathToLeavePoint(new StaffMember(StaffKind.Mechanic), w);
            Assert.Equal((65408, 32896, 0x11, 0), w.Request);
        }
    }
}
