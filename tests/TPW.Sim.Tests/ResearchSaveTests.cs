using System;
using System.Collections.Generic;
using System.Linq;
using TPW.Sim;
using Xunit;

namespace TPW.Sim.Tests;

public class ResearchSaveTests
{
    sealed class World : IResearchCatalogueWorld
    {
        public int DefinitionCount(int type) => 3;
        public ResearchLevel ReadResearchLevel(ResearchDefinition d, int level) => new(1, 200 + d.Index * 100);
        public int BuiltCount(ResearchDefinition d) => 1;
        public bool AllResearchUnlocked => false; public bool RestrictedMode => false; public int MechanicCount => 1;
        public void AnnounceDiscovery(ResearchDefinition d, int message) { }
    }

    // REJECTS reversed percent/count, wrong class/index order and preserving fractional work or losing active topic/funding.
    [Fact]
    public void NontrivialResearchRestoresAfterCatalogueAndLosesOnlyEstablishedPrecision()
    {
        var layout = SaveFixture.Layout() with { CatalogueCounts = Enumerable.Repeat(3, 8).ToArray() };
        var research = new ResearchSystem(new World()) { Funding = 93 };
        int[] order = { 3, 7, 6, 1, 2, 4, 5, 8 };
        int n = 0;
        foreach (int type in order) for (int index = 0; index < 3; index++) research.StoreProgress(new(type, index), index == 1 ? 1 : 0, 11 + n++);
        Assert.True(research.Start(0, new(3, 0))); research.ContributeResearch(137);
        uint before = research.Topic(0).ProgressFixed;
        var p = new ParkSave(layout) { Catalogue = ResearchSave.CaptureCatalogue(layout, research), ResearchTopics = research.SaveTopics() };
        n = 0;
        foreach (int type in order) for (int index = 0; index < 3; index++)
        {
            var progress = research.Progress(new(type, index));
            Assert.Equal(progress.Percent, p.Catalogue[n++]); Assert.Equal(progress.CompletedLevels, p.Catalogue[n++]);
        }
        var read = ParkSaveCodec.Read(ParkSaveCodec.Write(p), layout);
        var restored = new ResearchSystem(new World());
        ResearchSave.RestoreCatalogue(layout, read.Catalogue, restored); restored.RestoreTopics(read.ResearchTopics);
        foreach (int type in order) for (int index = 0; index < 3; index++) Assert.Equal(research.Progress(new(type, index)), restored.Progress(new(type, index)));
        Assert.Equal(93, restored.Funding); Assert.True(restored.Topic(0).Active); Assert.Equal(new ResearchDefinition(3, 0), restored.Topic(0).Definition);
        Assert.Equal((uint)p.Catalogue[0] * (200u << 12) / 100u, restored.Topic(0).ProgressFixed);
        Assert.True(restored.Topic(0).ProgressFixed < before); Assert.Equal(0, restored.Topic(0).Attention); Assert.False(restored.Topic(0).Finished);
        var wrongOrder = new ResearchSystem(new World()); wrongOrder.RestoreTopics(read.ResearchTopics);
        ResearchSave.RestoreCatalogue(layout, read.Catalogue, wrongOrder);
        Assert.NotEqual(restored.Topic(0).ProgressFixed, wrongOrder.Topic(0).ProgressFixed); // Control: order really matters.
    }

    // REJECTS serializing research in restricted mode or silently truncating the host catalogue.
    [Fact]
    public void RestrictedCatalogueIsAbsentAndWrongSizeIsRejected()
    {
        var layout = SaveFixture.Layout(true); var r = new ResearchSystem(new World());
        Assert.Empty(ResearchSave.CaptureCatalogue(layout, r)); ResearchSave.RestoreCatalogue(layout, Array.Empty<byte>(), r);
        Assert.Throws<ArgumentException>(() => ResearchSave.RestoreCatalogue(layout, new byte[2], r));
        Assert.Throws<ArgumentException>(() => ResearchSave.RestoreCatalogue(SaveFixture.Layout(), new byte[2], r));
    }
}
