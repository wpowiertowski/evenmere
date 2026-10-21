using Evenmere.Content;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Tests.Integration;

namespace Evenmere.Sim.Tests.Persistence;

/// <summary>
/// Loading the slice save against changed content: an extra good shifts every ID, a missing windmill ruins buildings.
/// </summary>
public sealed class SaveRemapSliceTests
{
    [Fact]
    public void RoundTrip_20000_40000_MatchesUninterrupted()
    {
        ContentDb content = SiteFinder.Content;
        (ISimulation resumed, _) = ReadFixture(content);
        resumed.CurrentTick.ShouldBe(20_000);

        using var stream = new MemoryStream();
        SaveGame.Write(stream, resumed, new SaveMeta(content, "test", "starter"));
        stream.Position = 0;

        (ISimulation loaded, SaveReport loadReport) = SaveGame.Read(stream, content);
        loadReport.IsClean.ShouldBeTrue();
        for (long tick = loaded.CurrentTick + 1; tick <= 40_000; tick++)
        {
            loaded.Tick();
        }

        Simulation direct = SliceHarness.RunTo(40_000);
        loaded.ComputeStateHash().ShouldBe(direct.ComputeStateHash());
    }
    [Fact]
    public void LoadAgainstContentWithExtraGood_ValuesLandOnRightGoods()
    {
        ContentDb oldContent = SiteFinder.Content;
        ContentOverlays.ExtraGood(out ContentDb content, out MapDef _);
        GoodId amber = content.IdOf<GoodId>("amber");

        (ISimulation oldSimulation, _) = ReadFixture(oldContent);
        using var stream = new MemoryStream();
        SaveGame.Write(stream, oldSimulation, new SaveMeta(oldContent, "test", "starter"));
        stream.Position = 0;

        (ISimulation simulation, SaveReport report) = SaveGame.Read(stream, content);

        // Every slice good keeps its stock, production, research, statistics and counters under its own name.
        foreach (string name in oldContent.Goods.Names)
        {
            GoodId oldGood = oldContent.IdOf<GoodId>(name);
            GoodId newGood = content.IdOf<GoodId>(name);
            simulation.Query.Storage.Stock(newGood).ShouldBe(oldSimulation.Query.Storage.Stock(oldGood));
            simulation.Query.Production.ProducedYesterday(newGood).ShouldBe(oldSimulation.Query.Production.ProducedYesterday(oldGood));
            simulation.Query.Progression.Counter(CounterKey.Produced(newGood))
                .ShouldBe(oldSimulation.Query.Progression.Counter(CounterKey.Produced(oldGood)));
        }

        foreach (string name in oldContent.Techs.Names)
        {
            simulation.Query.Research.IsDone(content.IdOf<TechId>(name))
                .ShouldBe(oldSimulation.Query.Research.IsDone(oldContent.IdOf<TechId>(name)));
        }

        simulation.Query.Storage.Stock(amber).ShouldBe(Amount.Zero);
        report.Remapped.ShouldContain(line => line.Contains("bread") || line.Contains("amber"));
        report.Dropped.ShouldBeEmpty();
        report.Ruins.ShouldBeEmpty();
    }

    [Fact]
    public void LoadAgainstContentWithExtraGood_ContinuesToSameHashAsDirectRun()
    {
        ContentDb oldContent = SiteFinder.Content;
        ContentOverlays.ExtraGood(out ContentDb content, out MapDef map);

        (ISimulation resumed, _) = ReadFixture(oldContent);
        using var stream = new MemoryStream();
        SaveGame.Write(stream, resumed, new SaveMeta(oldContent, "test", "starter"));
        stream.Position = 0;

        (ISimulation loaded, SaveReport report) = SaveGame.Read(stream, content);
        report.Dropped.ShouldBeEmpty();

        // The direct run plays the same script on the new content from tick zero.
        IReadOnlyList<(int Tick, Command Command)> script = SliceHarness.LoadCommands(content);
        Simulation direct = SliceHarness.RunTo(21_000, content, map, commands: script);

        for (long tick = loaded.CurrentTick + 1; tick <= 21_000; tick++)
        {
            loaded.Tick();
        }

        loaded.ComputeStateHash().ShouldBe(direct.ComputeStateHash());
    }

    [Fact]
    public void LoadAgainstContentMissingWindmill_RuinsPlusRefundAndReport()
    {
        ContentDb oldContent = SiteFinder.Content;
        ContentOverlays.MissingWindmill(out ContentDb content, out MapDef _);

        (ISimulation oldSimulation, _) = ReadFixture(oldContent);
        BuildingTypeId windmill = oldContent.IdOf<BuildingTypeId>("windmill");
        int windmillBuildings = CountBuildings(oldSimulation.Query.Map, windmill);

        using var stream = new MemoryStream();
        SaveGame.Write(stream, oldSimulation, new SaveMeta(oldContent, "test", "starter"));
        stream.Position = 0;

        (ISimulation simulation, SaveReport report) = SaveGame.Read(stream, content);

        int ruins = CountRuins(simulation.Query.Map);
        ruins.ShouldBe(windmillBuildings);
        windmillBuildings.ShouldBeGreaterThan(0);

        report.Ruins.Count.ShouldBe(windmillBuildings);
        foreach (string line in report.Ruins)
        {
            line.ShouldContain("windmill");
        }

        report.Dropped.ShouldContain(line => line.Contains("windmill"));
    }

    private static int CountBuildings(IMapQueries map, BuildingTypeId type)
    {
        int count = 0;
        foreach (BuildingInfo building in map.Buildings)
        {
            if (building.Type == type)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountRuins(IMapQueries map)
    {
        int count = 0;
        foreach (BuildingInfo building in map.Buildings)
        {
            if (building.State == BuildingState.Ruin)
            {
                count++;
            }
        }

        return count;
    }

    private static (ISimulation Simulation, SaveReport Report) ReadFixture(ContentDb content)
    {
        if (!File.Exists(SaveFixtureTests.FixturePath))
        {
            Assert.Fail($"The save fixture is missing: run with EVENMERE_UPDATE_GOLDEN=1 to generate {SaveFixtureTests.FixturePath}.");
        }

        using FileStream stream = File.OpenRead(SaveFixtureTests.FixturePath);
        return SaveGame.Read(stream, content);
    }
}
