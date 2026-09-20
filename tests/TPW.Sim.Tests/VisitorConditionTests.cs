using System.Collections.Generic;
using System.Linq;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests
{
    /// <summary>0x80091C28's condition code and its two consumers (findings/needs.md §6).</summary>
    public class VisitorConditionTests
    {
        sealed class Dice : IRandomSource { public int Next(int n) => 0; }

        static Visitor Guest(int needA = 0, int needB = 0, int toilet = 0, int nausea = 0, int happiness = 50, int tired = 0, int boredom = 0, int rubbish = 0)
        {
            var v = Visitor.Spawn(new Dice(), 0);
            v.NeedA = needA; v.NeedB = needB; v.RideDesire = toilet; v.Nausea = nausea;
            v.Happiness = happiness; v.Tiredness = tired; v.Boredom = boredom; v.Rubbish = rubbish;
            return v;
        }

        // ⚠ THE PAIR NEEDS BOTH OVER 80; NEED B ALONE IS OVER 75. REJECTS one threshold for need B, and
        // REJECTS "either need over 80 is the pair". Need B at 78 is thirsty alone but not part of a pair.
        [Theory]
        [InlineData(81, 81, GuestCondition.BothNeedsHigh)]
        [InlineData(81, 80, GuestCondition.NeedAHigh)]      // need B at exactly 80: not the pair
        [InlineData(80, 81, GuestCondition.NeedBHigh)]      // need A at exactly 80: not hungry, so thirsty
        [InlineData(81, 78, GuestCondition.NeedAHigh)]      // 78 would be thirsty alone, but hungry wins
        [InlineData(0, 76, GuestCondition.NeedBHigh)]
        [InlineData(0, 75, GuestCondition.None)]            // strict
        [InlineData(80, 0, GuestCondition.None)]            // strict
        public void TheTwoNeedsUseDifferentThresholdsAloneAndTogether(int needA, int needB, GuestCondition expected)
        {
            Assert.Equal(expected, VisitorCondition.Of(Guest(needA: needA, needB: needB)));
        }

        // ⚠ FIRST MATCH WINS IN THE ORIGINAL'S ORDER: needs, toilet, nausea, happy, tired, miserable.
        // REJECTS sorting by field or threshold; REJECTS "miserable beats tired"; REJECTS "happy beats nausea".
        [Fact]
        public void EarlierTestsWinOverLaterOnes()
        {
            Assert.Equal(GuestCondition.NeedBHigh, VisitorCondition.Of(Guest(needB: 76, toilet: 76, nausea: 76, happiness: 76, tired: 76)));
            Assert.Equal(GuestCondition.RideDesireHigh, VisitorCondition.Of(Guest(toilet: 76, nausea: 76, happiness: 76, tired: 76)));
            Assert.Equal(GuestCondition.NauseaHigh, VisitorCondition.Of(Guest(nausea: 76, happiness: 76, tired: 76)));
            Assert.Equal(GuestCondition.HappinessHigh, VisitorCondition.Of(Guest(happiness: 76, tired: 76)));
            Assert.Equal(GuestCondition.TirednessHigh, VisitorCondition.Of(Guest(happiness: 24, tired: 76)));
            Assert.Equal(GuestCondition.HappinessLow, VisitorCondition.Of(Guest(happiness: 24)));
        }

        // All four "others" are strict at 75 and misery is strict at 25. REJECTS >= anywhere.
        [Theory]
        [InlineData(75, 0, 0, 50, 0, GuestCondition.None)]
        [InlineData(76, 0, 0, 50, 0, GuestCondition.RideDesireHigh)]
        [InlineData(0, 75, 0, 50, 0, GuestCondition.None)]
        [InlineData(0, 76, 0, 50, 0, GuestCondition.NauseaHigh)]
        [InlineData(0, 0, 0, 75, 0, GuestCondition.None)]
        [InlineData(0, 0, 0, 76, 0, GuestCondition.HappinessHigh)]
        [InlineData(0, 0, 75, 50, 0, GuestCondition.None)]
        [InlineData(0, 0, 76, 50, 0, GuestCondition.TirednessHigh)]
        [InlineData(0, 0, 0, 25, 0, GuestCondition.None)]
        [InlineData(0, 0, 0, 24, 0, GuestCondition.HappinessLow)]
        public void TheOtherThresholdsAreStrict(int toilet, int nausea, int tired, int happiness, int unused, GuestCondition expected)
        {
            Assert.Equal(expected, VisitorCondition.Of(Guest(toilet: toilet, nausea: nausea, tired: tired, happiness: happiness)));
        }

        // ⭐ BOREDOM AND RUBBISH HAVE NO CODE. The original never reads V+0x5C or V+0x58 here, so a bored,
        // littered guest at neutral happiness is "nothing to report". REJECTS inventing a boredom condition.
        [Fact]
        public void BoredomAndRubbishNeverProduceACode()
        {
            Assert.Equal(GuestCondition.None, VisitorCondition.Of(Guest(boredom: 100, rubbish: 100)));
        }

        // The icon table is the bubble-sprite ids at 0x800E3100, in code order. REJECTS text ids and
        // REJECTS the bubble pass's own order (which puts nausea at 0x3D but tests it second, not fourth).
        [Theory]
        [InlineData(GuestCondition.None, 0x00)]
        [InlineData(GuestCondition.NeedBHigh, 0x32)]
        [InlineData(GuestCondition.NeedAHigh, 0x34)]
        [InlineData(GuestCondition.RideDesireHigh, 0x3B)]
        [InlineData(GuestCondition.NauseaHigh, 0x3D)]
        [InlineData(GuestCondition.HappinessHigh, 0x3E)]
        [InlineData(GuestCondition.TirednessHigh, 0x40)]
        [InlineData(GuestCondition.BothNeedsHigh, 0x33)]
        [InlineData(GuestCondition.HappinessLow, 0x35)]
        public void EachCodeHasItsIcon(GuestCondition code, int icon)
        {
            Assert.Equal(icon, VisitorCondition.Icon(code));
        }

        // ⚠ THE STATISTIC IS AN EXACT MATCH AND TRUNCATES. Three of seven is 42, not 43; and a guest at
        // code 7 is in neither the hungry nor the thirsty statistic. REJECTS rounding, REJECTS "code 7
        // counts as both", REJECTS dividing by zero guests.
        [Fact]
        public void ThePercentageStatisticsMatchExactlyAndTruncate()
        {
            var park = new List<Visitor>
            {
                Guest(needA: 90), Guest(needA: 90), Guest(needA: 90),   // code 2 x3
                Guest(needB: 90),                                      // code 1
                Guest(needA: 90, needB: 90),                           // code 7
                Guest(), Guest(),
            };
            Assert.Equal(42, VisitorCondition.PercentWith(park, GuestCondition.NeedAHigh));   // 300 / 7
            Assert.Equal(14, VisitorCondition.PercentWith(park, GuestCondition.NeedBHigh));   // 100 / 7
            Assert.Equal(14, VisitorCondition.PercentWith(park, GuestCondition.BothNeedsHigh));
            Assert.Equal(0, VisitorCondition.PercentWith(new List<Visitor>(), GuestCondition.NeedAHigh));
        }

        // ⚠ TIES GO TO THE LOWER CODE, and "none" is never shown. REJECTS a stable sort by insertion order,
        // REJECTS ties to the higher code, REJECTS showing code 0, REJECTS padding to three.
        [Fact]
        public void TheWindowShowsTheThreeCommonestConditionsWithTiesToTheLowerCode()
        {
            var park = new List<Visitor>
            {
                Guest(), Guest(), Guest(), Guest(), Guest(),          // five at code 0: must not appear
                Guest(tired: 90), Guest(tired: 90), Guest(tired: 90), // code 6 x3
                Guest(toilet: 90), Guest(toilet: 90),                 // code 3 x2
                Guest(needB: 90), Guest(needB: 90),                   // code 1 x2 -- ties code 3, wins on number
                Guest(needA: 90),                                     // code 2 x1
            };
            var top = VisitorCondition.TopThree(park);
            Assert.Equal(new[] { GuestCondition.TirednessHigh, GuestCondition.NeedBHigh, GuestCondition.RideDesireHigh }, top);

            var sparse = VisitorCondition.TopThree(new List<Visitor> { Guest(), Guest(nausea: 90) });
            Assert.Equal(new[] { GuestCondition.NauseaHigh }, sparse);
            Assert.Empty(VisitorCondition.TopThree(new List<Visitor> { Guest() }));
        }

        // The selection zeroes the winner and rescans, so a code is never listed twice. REJECTS a
        // rescan that forgets to clear the count.
        [Fact]
        public void ACodeIsListedAtMostOnce()
        {
            var park = Enumerable.Range(0, 6).Select(_ => Guest(needA: 90)).ToList();
            Assert.Equal(new[] { GuestCondition.NeedAHigh }, VisitorCondition.TopThree(park));
        }
    }
}
