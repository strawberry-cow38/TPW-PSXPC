using System;
using System.Linq;
using TPW.Sim;
using Xunit;
using static TPW.Sim.Tests.ParkStatisticsTests;

namespace TPW.Sim.Tests
{
    public class StatisticInterpreterTests
    {
        // REJECTS starting rules before ALL statistic cases were visited, or delaying until refresh slot zero again.
        [Fact]
        public void FirstRuleRunsOnCall288AndThenOneRulePerCall()
        {
            var w = new World { TotalDays = 1, AdvisorQueueEmpty = true };
            var s = new ParkStatistics(1, new[] {
                Rule(new StatisticInstruction(StatisticOpcode.PostMessage,123)), Rule(new StatisticInstruction(StatisticOpcode.PostMessage,456)) });
            Ticks(s,w,287); Assert.Empty(w.Effects);
            s.Tick(w); Assert.Equal(new[] {"post:123"},w.Effects); Assert.Equal(1,s.RuleCursor);
            s.Tick(w); Assert.Equal(new[] {"post:123","post:456"},w.Effects); Assert.Equal(0,s.RuleCursor);
            s.Tick(w); Assert.Equal(2,w.Effects.Count); // nextCheck 11 has not passed.
        }

        // REJECTS >= on schedule dates, timer restart on elapsed failure, and timer restart after a successful post.
        [Fact]
        public void SchedulingDistinguishesFailedConditionsFromWaitingAndSuccess()
        {
            var w = new World { TotalDays = 100, AdvisorQueueEmpty = true };
            var rule = new StatisticRule(7,new[] {new StatisticInstruction(StatisticOpcode.Equal,0,1),
                new StatisticInstruction(StatisticOpcode.ElapsedGreater,3),new StatisticInstruction(StatisticOpcode.PostMessage,9)});
            var s = new ParkStatistics(100,new[]{rule});
            Ticks(s,w,288);
            Assert.Equal((uint)100,s.LastFailureDay(0)); Assert.Empty(w.Effects);
            Set(s,w,0,1); w.TotalDays = 103;
            s.Tick(w); // cursor 0 refresh would overwrite ParkOpen; supply it too.
            Assert.Equal((uint)103,s.LastFailureDay(0));
            w.ParkOpen = true; Set(s,w,0,1); w.TotalDays = 106;
            s.Tick(w); Assert.Empty(w.Effects); Assert.Equal((uint)103,s.LastFailureDay(0));
            Assert.Equal((uint)0,s.NextCheckDay(0));
            w.TotalDays = 107; s.Tick(w);
            Assert.Equal(new[]{"post:9"},w.Effects); Assert.Equal((uint)114,s.NextCheckDay(0));
            Assert.Equal((uint)103,s.LastFailureDay(0));
            w.TotalDays = 114; s.Tick(w); Assert.Single(w.Effects);
            w.TotalDays = 115; s.Tick(w); Assert.Equal(2,w.Effects.Count);
            Assert.Equal((uint)122,s.NextCheckDay(0));
        }

        // REJECTS signed schedule comparisons, widening elapsed scratch past s16, and saturating unsigned date addition.
        [Fact]
        public void DatesAreUnsignedButElapsedScratchWrapsSigned()
        {
            var w = new World { TotalDays = 0, AdvisorQueueEmpty = true };
            var s = new ParkStatistics(0,new[]{Rule(new StatisticInstruction(StatisticOpcode.ElapsedGreater,1),new(StatisticOpcode.PostMessage,8))});
            Ticks(s,w,288); Assert.Equal((uint)0,s.NextCheckDay(0)); Assert.Empty(w.Effects);
            w.TotalDays = 32768; s.Tick(w);
            Assert.Equal(short.MinValue,s[ParkStatistic.RuleElapsedDays]); Assert.Empty(w.Effects);
            var t = new ParkStatistics(uint.MaxValue-5,new[]{Rule(new StatisticInstruction(StatisticOpcode.PostMessage,8))});
            w.TotalDays = uint.MaxValue-1; Ticks(t,w,288);
            Assert.Equal((uint)8,t.NextCheckDay(0)); Assert.Single(w.Effects);
        }

        // REJECTS sharing rule dates between parks, retaining caller-owned instruction arrays, or sharing cached values.
        [Fact]
        public void TwoParksHaveIndependentRuleHistory()
        {
            var instructions = new[]{new StatisticInstruction(StatisticOpcode.PostMessage,6)};
            var rule = new StatisticRule(20,instructions);
            instructions[0] = new(StatisticOpcode.PostMessage,7);
            var a = new ParkStatistics(0,new[]{rule}); var b = new ParkStatistics(5,new[]{rule});
            var w = new World { TotalDays = 10, AdvisorQueueEmpty = true };
            Ticks(a,w,288); Assert.Equal(new[]{"post:6"},w.Effects);
            Assert.Equal((uint)0,b.NextCheckDay(0)); Assert.Equal((uint)5,b.LastFailureDay(0));
            Ticks(b,w,288); Assert.Equal(new[]{"post:6","post:6"},w.Effects);
        }

        // REJECTS <=/>=, unsigned operand comparison, inverted equality, and continuing past a failed predicate.
        [Theory]
        [InlineData(1,-1,-1,true)] [InlineData(1,-1,0,false)]
        [InlineData(2,-1,-1,false)] [InlineData(2,-1,0,true)]
        [InlineData(3,-1,0,true)] [InlineData(3,0,0,false)] [InlineData(3,1,0,false)]
        [InlineData(4,-1,0,false)] [InlineData(4,0,0,false)] [InlineData(4,1,0,true)]
        public void ComparisonOpcodesAreSignedAndStrict(int opcode,short a,short b,bool passes)
        {
            var s = new ParkStatistics(0); var w = new World(); Set(s,w,12,a);
            var result = s.Evaluate(Rule(new StatisticInstruction((StatisticOpcode)opcode,12,b),new(StatisticOpcode.PostMessage,23)),w);
            Assert.Equal(passes ? StatisticRuleResult.Completed : StatisticRuleResult.ConditionFailed,result);
            Assert.Equal(passes ? 1 : 0,w.Effects.Count);
        }

        // REJECTS rollback on later failure, sorting actions, treating opcode 8 as a post, and signed message IDs.
        [Fact]
        public void ActionsAreImmediateOrderedAndNotRolledBack()
        {
            var s = new ParkStatistics(0); var w = new World();
            var result = s.Evaluate(Rule(new StatisticInstruction(StatisticOpcode.Set,12,7),new(StatisticOpcode.Action8,12),
                new(StatisticOpcode.PostMessage,-1),new(StatisticOpcode.Greater,12,8),new(StatisticOpcode.PostMessage,13)),w);
            Assert.Equal(StatisticRuleResult.ConditionFailed,result);
            Assert.Equal(7,s[ParkStatistic.LiveLitter]);
            Assert.Equal(new[]{"action8:12","post:65535"},w.Effects);
        }

        // REJECTS event writes being immediately visible, Add persisting into backing counters, or Set forgetting backing storage.
        [Fact]
        public void NativeAddScriptAddAndScriptSetHaveDifferentLifetimes()
        {
            var s = new ParkStatistics(0); var w = new World();
            s.AddEvent(0,23,w); Assert.Equal(0,s[ParkStatistic.RideWearEvents]);
            Ticks(s,w,205); Assert.Equal(23,s[ParkStatistic.RideWearEvents]);
            s.Evaluate(Rule(new StatisticInstruction(StatisticOpcode.Add,51,7)),w);
            Assert.Equal(30,s[ParkStatistic.RideWearEvents]);
            Ticks(s,w,288); Assert.Equal(23,s[ParkStatistic.RideWearEvents]);
            Set(s,w,51,9); Assert.Equal(9,s[ParkStatistic.RideWearEvents]);
            Ticks(s,w,288); Assert.Equal(9,s[ParkStatistic.RideWearEvents]);
            // The last counter has its own storage and refresh slot (70).
            s.AddEvent(19,-3,w); Ticks(s,w,288); Assert.Equal(-3,s[ParkStatistic.EntryValue]);
        }

        // REJECTS event assignment instead of addition, and clearing the event total when it is sampled.
        [Fact]
        public void NativeEventsAccumulateAcrossSeveralRefreshes()
        {
            var s = new ParkStatistics(0); var w = new World();
            s.AddEvent(0,8,w); s.AddEvent(0,8,w); Ticks(s,w,205);
            Assert.Equal(16,s[ParkStatistic.RideWearEvents]);
            s.AddEvent(0,-3,w); Ticks(s,w,288);
            Assert.Equal(13,s[ParkStatistic.RideWearEvents]);
        }

        // REJECTS clamping before wrapping, unsigned clamp comparisons, accepting invalid event indices, and ignoring enablement.
        [Theory]
        [InlineData(30001,30000)] [InlineData(-30001,-30000)]
        [InlineData(32768,-30000)] [InlineData(-32769,30000)] [InlineData(65537,1)]
        public void EventsWrapBeforeClamping(int amount,int expected)
        {
            var s = new ParkStatistics(0); var w = new World { StatisticsEnabled = false };
            s.AddEvent(0,10,w); w.StatisticsEnabled = true;
            s.AddEvent(-1,10,w); s.AddEvent(20,10,w); s.AddEvent(0,amount,w);
            Ticks(s,w,205); Assert.Equal(expected,s[ParkStatistic.RideWearEvents]);
        }

        // REJECTS clamping script arithmetic like native events, eager native reset, and erasing the dead counter's numbered slot.
        [Fact]
        public void ScriptWritesWrapWithoutTheNativeClampAndCounterFourRemainsAddressable()
        {
            var s = new ParkStatistics(0); var w = new World();
            Set(s,w,55,32767); s.Evaluate(Rule(new StatisticInstruction(StatisticOpcode.Add,55,1)),w);
            Assert.Equal(-32768,s[ParkStatistic.DeadCounter4]);
            Ticks(s,w,221); Assert.Equal(32767,s[ParkStatistic.DeadCounter4]);
            Set(s,w,70,-32768); Ticks(s,w,288); Assert.Equal(-32768,s[ParkStatistic.EntryValue]);
        }

        // REJECTS labelling case 71 dead storage; it survives its empty refresh and Set aliases the refresh cursor.
        [Fact]
        public void ScratchIsPreservedByRefreshAndSetHasTheOriginalAlias()
        {
            var s = new ParkStatistics(0); var w = new World();
            Set(s,w,71,284); Assert.Equal(284,s.RefreshCursor);
            s.Tick(w); Assert.Equal(284,s[ParkStatistic.RuleElapsedDays]);
            Assert.Equal(285,s.RefreshCursor);
            s.Evaluate(Rule(new StatisticInstruction(StatisticOpcode.ElapsedGreater,283)),w);
            Assert.Equal(StatisticRuleResult.StillWaiting,s.Evaluate(Rule(new StatisticInstruction(StatisticOpcode.ElapsedGreater,284)),w));
            Assert.Equal(StatisticRuleResult.Completed,s.Evaluate(Rule(new StatisticInstruction(StatisticOpcode.ElapsedGreater,283)),w));
        }

        // REJECTS matching rule N only to statistic N, inventing reasonable happiness thresholds, and firing at need-share equality.
        [Fact]
        public void DiscRulesReadSeveralCachedStatisticsAndKeepTheirSurprisingThresholds()
        {
            var s = new ParkStatistics(0); var w = new World();
            Set(s,w,71,31); Set(s,w,11,21); Set(s,w,1,10);
            Assert.Equal(StatisticRuleResult.ConditionFailed,s.Evaluate(ParkStatisticRules.All[3],w));
            Set(s,w,1,11);
            Assert.Equal(StatisticRuleResult.Completed,s.Evaluate(ParkStatisticRules.All[3],w));
            Assert.Equal(new[]{"action8:1","post:1"},w.Effects);
            w.Effects.Clear(); Set(s,w,11,2); Set(s,w,46,2);
            Assert.Equal(StatisticRuleResult.Completed,s.Evaluate(ParkStatisticRules.All[13],w));
            Assert.Equal(new[]{"action8:122","post:122"},w.Effects);
        }

        // REJECTS conflating litter with toilet dirtiness, inclusive 40/70 boundaries, and selecting the wrong cleaner message.
        [Theory]
        [InlineData(40,0,-1)] [InlineData(41,0,80)] [InlineData(69,0,80)] [InlineData(70,0,-1)] [InlineData(71,0,82)]
        [InlineData(41,1,81)] [InlineData(71,1,83)]
        public void DirtyFeatureMessagesComeFromTheFourDiscRules(short dirt,short cleaners,int message)
        {
            var s = new ParkStatistics(0); var w = new World();
            Set(s,w,30,dirt); Set(s,w,10,cleaners); Set(s,w,12,40); Set(s,w,71,31);
            for (int r = 116; r <= 119; r++) s.Evaluate(ParkStatisticRules.All[r],w);
            Assert.Equal(message < 0 ? Array.Empty<string>() : new[]{"action8:80","action8:81","action8:82","action8:83","post:"+message},w.Effects);
        }

        // REJECTS periodic counter reset being contingent on its warning, and Set resets that leave the backing event total alive.
        [Fact]
        public void ResetRuleClearsBackingCounterEvenWhenNoWarningFired()
        {
            var s = new ParkStatistics(0); var w = new World();
            s.AddEvent(3,32,w); Ticks(s,w,217); Assert.Equal(32,s[ParkStatistic.QueueAbandonmentPoints]);
            s.Evaluate(ParkStatisticRules.All[5],w);
            Assert.Equal(0,s[ParkStatistic.QueueAbandonmentPoints]);
            Ticks(s,w,288); Assert.Equal(0,s[ParkStatistic.QueueAbandonmentPoints]); Assert.Empty(w.Effects);
        }
    }
}
