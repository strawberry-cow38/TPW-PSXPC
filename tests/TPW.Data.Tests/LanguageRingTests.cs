using System;
using System.Linq;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    public class LanguageRingTests
    {
        /// <summary>⭐ THE POINT OF THE WHOLE CLASS. The ring position is NOT the language index, and
        /// the two are both small integers over overlapping ranges, so a mix-up is silent. This asserts
        /// they actually DIFFER — if someone decides the mapping table is redundant and drops it, the
        /// identity mapping fails here rather than in a German player's menu.</summary>
        [Fact]
        public void TheRingPositionIsNotTheLanguageIndex()
        {
            int same = Enumerable.Range(0, LanguageRing.Count).Count(i => LanguageRing.ToLanguage(i) == i);
            Assert.True(same < LanguageRing.Count, "ring position must not be the language index");
            Assert.NotEqual(0, LanguageRing.ToLanguage(0));     // ring 0 is German, language 0 is English
        }

        /// <summary>English is where the ring rests, and it is language 0. Both halves matter: the
        /// resting position being the one language that happens to look right under the identity
        /// mapping is exactly why this bug would survive testing.</summary>
        [Fact]
        public void TheRingRestsOnEnglish()
        {
            Assert.Equal("English", LanguageRing.NameAt(LanguageRing.RestPosition));
            Assert.Equal(0, LanguageRing.ToLanguage(LanguageRing.RestPosition));
        }

        /// <summary>⭐ THE ONE VERIFIED END TO END. RIGHT twice from rest then CROSS produced a main menu
        /// reading "Jouer / Options / Charger partie" on the console — ring position, through the
        /// language number, into the string table, confirmed by its OUTPUT rather than by an address.
        /// Every other row of the table rests on labels rendered after a RAM write, which is weaker.</summary>
        [Fact]
        public void TwoRightFromRestIsFrench()
        {
            int pos = LanguageRing.Turn(LanguageRing.RestPosition, +2);
            Assert.Equal(5, pos);
            Assert.Equal("French", LanguageRing.NameAt(pos));
        }

        /// <summary>LEFT decrements: the index word read 2 after one LEFT from rest, and 2 is Dutch.</summary>
        [Fact]
        public void OneLeftFromRestIsDutch()
            => Assert.Equal("Dutch", LanguageRing.NameAt(LanguageRing.Turn(LanguageRing.RestPosition, -1)));

        /// <summary>Every position maps to a distinct, real language.</summary>
        [Fact]
        public void TheRingIsAOneToOneMapOntoRealLanguages()
        {
            var mapped = Enumerable.Range(0, LanguageRing.Count).Select(LanguageRing.ToLanguage).ToArray();
            Assert.Equal(mapped.Length, mapped.Distinct().Count());
            foreach (int l in mapped) Assert.InRange(l, 0, StringTable.LanguageNames.Length - 1);
        }

        /// <summary>⚠ THE RING IS SHORTER THAN THE LANGUAGE LIST. Japanese has string tables on this
        /// disc but no position on the ring, so a port that iterates languages and expects a ring slot
        /// for each is wrong. Stated as a test so the asymmetry is impossible to miss.</summary>
        [Fact]
        public void JapaneseHasNoPositionOnTheRing()
        {
            Assert.Equal(8, StringTable.LanguageNames.Length);
            Assert.Equal(7, LanguageRing.Count);
            Assert.False(LanguageRing.IsReachable(StringTable.Japanese));
            for (int l = 0; l < StringTable.Japanese; l++) Assert.True(LanguageRing.IsReachable(l));
        }

        [Fact]
        public void TheRingWrapsBothWays()
        {
            Assert.Equal(0, LanguageRing.Turn(LanguageRing.Count - 1, +1));
            Assert.Equal(LanguageRing.Count - 1, LanguageRing.Turn(0, -1));
        }

        [Fact]
        public void APositionOffTheRingIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LanguageRing.ToLanguage(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => LanguageRing.ToLanguage(LanguageRing.Count));
        }
    }
}
