using System;
using System.Linq;
using TPW.Data;
using Xunit;

namespace TPW.Data.Tests
{
    public class WorldMapTests
    {
        /// <summary>Eight parks, two per world, covering all four worlds exactly once each.</summary>
        [Fact]
        public void EveryWorldHasExactlyTwoParks()
        {
            Assert.Equal(8, WorldMap.Islands.Length);
            Assert.Equal(ParkWorlds.All.Count * 2, WorldMap.Islands.Length);
            foreach (var w in ParkWorlds.All)
            {
                Assert.NotNull(WorldMap.Find(w.Index, 0));
                Assert.NotNull(WorldMap.Find(w.Index, 1));
            }
            Assert.Null(WorldMap.Find(0, 2));
            Assert.Null(WorldMap.Find(ParkWorlds.All.Count, 0));
        }

        /// <summary>Every park has its own name, and every world index is one the port actually has. A name id
        /// shared between two parks would mean a copy-paste in the table I transcribed.</summary>
        [Fact]
        public void NameIdsAreDistinctAndWorldIndicesAreReal()
        {
            Assert.Equal(WorldMap.Islands.Length, WorldMap.Islands.Select(i => i.NameId).Distinct().Count());
            Assert.All(WorldMap.Islands, i => Assert.InRange(i.World, 0, ParkWorlds.All.Count - 1));
            Assert.All(WorldMap.Islands, i => Assert.InRange(i.Park, 0, 1));
            Assert.All(WorldMap.Islands, i => Assert.True(i.NameId > 0));
        }

        /// <summary>⭐ The point of the table: the world map's indices agree with the world table's, which is a
        /// DIFFERENT table written by different code. Pinned as the specific pairing, because "both have four
        /// worlds" would pass on any shuffle.
        ///
        /// ⚠ The port's world 0 is named "jungle" and this park is called "Lost Kingdom", so the two cannot be
        /// matched by name -- which is exactly why the index has to come from the game rather than from a
        /// reader's sense of which theme is which.</summary>
        [Theory]
        [InlineData(0, "jungle", 945, 946)]
        [InlineData(1, "halloween", 373, 375)]
        [InlineData(2, "fantasy", 752, 753)]
        [InlineData(3, "space", 406, 407)]
        public void TheMapsWorldIndicesMatchTheWorldTables(int world, string portName, int firstName, int secondName)
        {
            Assert.Equal(portName, ParkWorlds.All[world].Name);
            Assert.Equal(firstName, WorldMap.Find(world, 0).Value.NameId);
            Assert.Equal(secondName, WorldMap.Find(world, 1).Value.NameId);
        }

        /// <summary>The record layout is what the transcription came from; if these move, the table above was
        /// read at the wrong offsets and every name id in it is suspect.</summary>
        [Fact]
        public void TheRecordLayoutIsRecorded()
        {
            Assert.Equal(0x1C, WorldMap.RecordStride);
            Assert.True(WorldMap.NameIdOffset + 2 <= WorldMap.RecordStride);
            Assert.True(WorldMap.WorldOffset < WorldMap.ParkOffset);
            Assert.Equal(11, WorldMap.Overlay);
        }
    }
}
