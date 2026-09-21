using System;
using System.Collections.Generic;

namespace TPW.Sim
{
    /// <summary>READ: one-based opcodes, jump table 0x800DBB18. Zero terminates a program.</summary>
    public enum StatisticOpcode
    {
        Equal = 1, NotEqual = 2, Less = 3, Greater = 4, Add = 5, Set = 6,
        PostMessage = 7, Action8 = 8, ElapsedGreater = 9,
    }

    /// <summary>READ: signed halfword operands, 0x80017078..0x800171F0.</summary>
    public readonly record struct StatisticInstruction(StatisticOpcode Op, short A, short B = 0);

    /// <summary>READ: FOLIO entry 1 supplies the cooldown; entry 2 supplies the instructions.
    /// Runtime dates live separately, so parks sharing these definitions do not share rule history.</summary>
    public sealed class StatisticRule
    {
        public StatisticRule(ushort cooldownDays, StatisticInstruction[] instructions)
        {
            CooldownDays = cooldownDays;
            Instructions = Array.AsReadOnly((StatisticInstruction[])instructions.Clone());
        }
        public ushort CooldownDays { get; }
        public IReadOnlyList<StatisticInstruction> Instructions { get; }
    }

    /// <summary>READ: 0x8001720C / 0x800170A0 / 0x800171F8. A failed elapsed test leaves BOTH dates
    /// alone; a failed statistic test restarts the continuous-condition timer.</summary>
    public enum StatisticRuleResult { Completed = 0, ConditionFailed = 1, StillWaiting = 2 }

    /// <summary>The advisor's 72 signed halfwords, 0x80016718 / 0x80016870.
    ///
    /// ⭐ THESE ARE A CACHE, NOT THE PARK. Refreshes copy one live result, never interpolate or clear
    /// others. The advisor starts reading only after the first complete sweep; it then examines ONE
    /// rule on EVERY eligible call, independently of which statistic was just refreshed.
    ///
    /// ⚠ DO NOT FIX: event counters have a second copy. An event changes only the backing counter;
    /// a script Add changes only the cached statistic; a script Set changes both. Making those writes
    /// consistent changes messages and reset timing. findings/statistics.md §3/§4.</summary>
    public sealed class ParkStatistics
    {
        public const int SlotCount = 72;                 // 0x80016748
        public const int RefreshPeriod = 4;              // 0x80016890 / 0x800168A0
        public const int CycleTicks = 288;               // 0x800168A8..D4
        public const int FirstCounterSlot = 51;          // 0x80016FEC / 0x80017164
        public const int CounterCount = 20;              // 0x80016814 / 0x80016A50
        public const int CounterLimit = 30000;           // 0x80016A84..A8

        readonly short[] values = new short[SlotCount];
        readonly short[] counters = new short[CounterCount];
        readonly IReadOnlyList<StatisticRule> rules;
        readonly uint[] nextCheckDay, lastFailureDay;

        /// <summary>READ: zero statistics/counters/cursors, not an eager computation; initialize
        /// every rule's last-failure date to now and its next-check date to zero (0x80016738..81C).</summary>
        public ParkStatistics(uint nowDay, IReadOnlyList<StatisticRule> rules = null)
        {
            this.rules = rules ?? ParkStatisticRules.All;
            nextCheckDay = new uint[this.rules.Count];
            lastFailureDay = new uint[this.rules.Count];
            Array.Fill(lastFailureDay, nowDay);
        }

        public int RefreshCursor { get; private set; }
        public int RuleCursor { get; private set; }
        public bool FirstSweepComplete { get; private set; }
        /// <summary>READ: signed cached value; no refresh or validity flag on a read.</summary>
        public short this[ParkStatistic statistic] => values[(int)statistic];
        /// <summary>The RAW event counter, before the refresh sweep copies it into slot 51+index.
        /// ⭐ THE READ THAT TELLS TWO BUGS APART: a zero cached slot means either nothing was ever
        /// raised or the sweep has not reached it, and only the raw counter distinguishes them.</summary>
        public short EventCounter(int index) => counters[index];
        public uint NextCheckDay(int rule) => nextCheckDay[rule];
        public uint LastFailureDay(int rule) => lastFailureDay[rule];

        /// <summary>READ: 0x800139B4 → 0x80016A50, event index 0..19. Other indices are ignored.
        /// ⚠ DO NOT FIX: cast to signed 16 bits BEFORE clamping, so large positive additions can
        /// turn negative. This does not update the cached statistic until its next refresh.</summary>
        public void AddEvent(int eventIndex, int amount, IParkStatisticsWorld world)
        {
            if (!world.StatisticsEnabled || eventIndex < 0 || eventIndex >= CounterCount) return;
            short wrapped = unchecked((short)(counters[eventIndex] + amount));
            counters[eventIndex] = (short)Math.Clamp((int)wrapped, -CounterLimit, CounterLimit);
        }

        /// <summary>READ: 0x80013298 gates the whole call; 0x80016870 then increments the refresh
        /// cursor even when the advisor queue is nonempty. The first rule may run on call 288 itself.</summary>
        public void Tick(IParkStatisticsWorld world)
        {
            if (!world.StatisticsEnabled || !world.AdvisorIdle) return;
            if (RefreshCursor % RefreshPeriod == 0)
            {
                int slot = RefreshCursor / RefreshPeriod;
                if (slot < FirstCounterSlot)
                    values[slot] = unchecked((short)ParkStatisticCalculator.Compute((ParkStatistic)slot, world));
                else if (slot < FirstCounterSlot + CounterCount)
                    values[slot] = counters[slot - FirstCounterSlot];
                // 71 is deliberately unwritten by its refresh case (0x800DBB14 → 0x80017004).
            }
            RefreshCursor = (RefreshCursor + 1) % CycleTicks;
            if (RefreshCursor == 0) FirstSweepComplete = true;
            if (!FirstSweepComplete || !world.AdvisorQueueEmpty) return;

            uint now = world.TotalDays;
            int current = RuleCursor;
            if (nextCheckDay[current] < now)
            {
                values[(int)ParkStatistic.RuleElapsedDays] = unchecked((short)(now - lastFailureDay[current]));
                var result = Evaluate(rules[current], world);
                if (result == StatisticRuleResult.Completed)
                    nextCheckDay[current] = unchecked(now + rules[current].CooldownDays);
                else if (result == StatisticRuleResult.ConditionFailed)
                    lastFailureDay[current] = now;
            }
            RuleCursor = (RuleCursor + 1) % rules.Count;
        }

        /// <summary>READ: 0x80017024. Conditions are an ordered AND, actions take effect immediately,
        /// and a later failed condition does not undo earlier actions. Actual disc programs contain
        /// no Add, but its case is live code and retained. Action8's meaning stays at the world boundary.</summary>
        public StatisticRuleResult Evaluate(StatisticRule rule, IParkStatisticsWorld world)
        {
            foreach (var instruction in rule.Instructions)
            {
                int index = instruction.A;
                short operand = instruction.B;
                switch (instruction.Op)
                {
                    case StatisticOpcode.Equal:
                        if (values[index] != operand) return StatisticRuleResult.ConditionFailed;
                        break;
                    case StatisticOpcode.NotEqual:
                        if (values[index] == operand) return StatisticRuleResult.ConditionFailed;
                        break;
                    case StatisticOpcode.Less:
                        if (values[index] >= operand) return StatisticRuleResult.ConditionFailed;
                        break;
                    case StatisticOpcode.Greater:
                        if (values[index] <= operand) return StatisticRuleResult.ConditionFailed;
                        break;
                    case StatisticOpcode.Add:
                        values[index] = unchecked((short)(values[index] + operand));
                        break;
                    case StatisticOpcode.Set:
                        values[index] = operand;
                        if (index >= FirstCounterSlot)
                        {
                            if (index == (int)ParkStatistic.RuleElapsedDays)
                                RefreshCursor = unchecked((ushort)operand); // +0xB8 aliases the cursor.
                            else counters[index - FirstCounterSlot] = operand;
                        }
                        break;
                    case StatisticOpcode.PostMessage:
                        world.PostMessage(unchecked((ushort)instruction.A));
                        break;
                    case StatisticOpcode.Action8:
                        world.ApplyRuleAction8(instruction.A);
                        break;
                    case StatisticOpcode.ElapsedGreater:
                        if (values[(int)ParkStatistic.RuleElapsedDays] <= instruction.A)
                            return StatisticRuleResult.StillWaiting;
                        break;
                }
            }
            return StatisticRuleResult.Completed;
        }
    }
}
