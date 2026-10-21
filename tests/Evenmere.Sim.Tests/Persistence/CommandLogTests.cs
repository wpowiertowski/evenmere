using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Research.Commands;
using Evenmere.Sim.Systems.Roads.Commands;
using Evenmere.Sim.Tests.Integration;

namespace Evenmere.Sim.Tests.Persistence;

/// <summary>The command log: writer and reader round-trip (tick, command) pairs, and unknown content drops commands.</summary>
public sealed class CommandLogTests
{
    [Fact]
    public void WriteRead_RoundTrip_PreservesTicksAndCommands()
    {
        ContentDb content = SiteFinder.Content;
        using var stream = new MemoryStream();
        var writer = new CommandLogWriter(stream);
        writer.Append(1, new PlaceBuilding(content.IdOf<BuildingTypeId>("settler_house"), 20, 96, Rotation.Deg0));
        writer.Append(1, new SetResearchTarget(content.IdOf<TechId>("cultivation")));
        writer.Append(100, new PlaceRoad([new TileCoord(1, 2), new TileCoord(1, 3)], content.Required.DirtRoad));

        stream.Position = 0;
        var reader = new CommandLogReader(stream, IdentityTables(content));
        IReadOnlyList<(long Tick, Command Command)> entries = reader.ReadAll();

        entries.Count.ShouldBe(3);
        entries[0].Tick.ShouldBe(1);
        entries[1].Tick.ShouldBe(1);
        entries[2].Tick.ShouldBe(100);
        entries[0].Command.ShouldBe(new PlaceBuilding(content.IdOf<BuildingTypeId>("settler_house"), 20, 96, Rotation.Deg0));
        entries[1].Command.ShouldBe(new SetResearchTarget(content.IdOf<TechId>("cultivation")));
        entries[2].Command.ShouldBe(new PlaceRoad([new TileCoord(1, 2), new TileCoord(1, 3)], content.Required.DirtRoad));
    }

    [Fact]
    public void Read_UnknownCommandType_ThrowsInvalidData()
    {
        ContentDb content = SiteFinder.Content;
        using var stream = new MemoryStream();
        var writer = new CommandLogWriter(stream);
        writer.Append(1, new PlaceBuilding(content.IdOf<BuildingTypeId>("settler_house"), 20, 96, Rotation.Deg0));

        byte[] bytes = stream.ToArray();
        string renamed = System.Text.Encoding.UTF8.GetString(bytes).Replace("PlaceBuilding", "PlaceDwelling", StringComparison.Ordinal);
        using var tampered = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(renamed));

        var reader = new CommandLogReader(tampered, IdentityTables(content));
        Should.Throw<InvalidDataException>(() => reader.ReadAll());
    }

    [Fact]
    public void Read_UnknownBuilding_DropsCommand()
    {
        ContentDb content = SiteFinder.Content;
        string[] oldBuildings = [.. content.Buildings.Names, "gone_building"];
        var gone = BuildingTypeId.From((ushort)(oldBuildings.Length - 1));

        using var stream = new MemoryStream();
        var writer = new CommandLogWriter(stream);
        writer.Append(5, new PlaceBuilding(gone, 20, 96, Rotation.Deg0));
        writer.Append(6, new PlaceBuilding(content.IdOf<BuildingTypeId>("settler_house"), 21, 96, Rotation.Deg0));

        SaveTables tables = TablesWith(content, oldBuildings);
        stream.Position = 0;
        var reader = new CommandLogReader(stream, tables);

        IReadOnlyList<(long Tick, Command Command)> entries = reader.ReadAll();
        entries.Count.ShouldBe(1);
        entries[0].ShouldBe((6, new PlaceBuilding(content.IdOf<BuildingTypeId>("settler_house"), 21, 96, Rotation.Deg0)));
    }

    [Fact]
    public void Read_UnknownTechInQueue_KeepsKnownEntries()
    {
        ContentDb content = SiteFinder.Content;
        string[] oldTechs = [.. content.Techs.Names, "gone_tech"];
        var gone = TechId.From((ushort)(oldTechs.Length - 1));

        using var stream = new MemoryStream();
        var writer = new CommandLogWriter(stream);
        writer.Append(5, new SetResearchQueue([gone, content.IdOf<TechId>("cultivation")]));

        SaveTables tables = TablesWithTechs(content, oldTechs);
        stream.Position = 0;
        var reader = new CommandLogReader(stream, tables);

        IReadOnlyList<(long Tick, Command Command)> entries = reader.ReadAll();
        entries.Count.ShouldBe(1);
        entries[0].Command.ShouldBe(new SetResearchQueue([content.IdOf<TechId>("cultivation")]));
    }

    [Fact]
    public void Replay_RoundTrip_ReproducesHash()
    {
        ContentDb content = SiteFinder.Content;
        IReadOnlyList<(int Tick, Command Command)> script = SliceHarness.LoadCommands(content);

        Simulation direct = SliceHarness.RunTo(2000, content, commands: script);

        using var log = new MemoryStream();
        var logWriter = new CommandLogWriter(log);
        int next = 0;
        for (int tick = 1; tick <= 2000; tick++)
        {
            while (next < script.Count && script[next].Tick == tick)
            {
                logWriter.Append(tick, script[next].Command);
                next++;
            }
        }

        using var save = new MemoryStream();
        SaveGame.Write(save, direct, new SaveMeta(content, "test", "starter"));
        save.Position = 0;
        log.Position = 0;

        ulong replayed = Replay.Run(save, log, content, 2000);
        replayed.ShouldBe(direct.ComputeStateHash());
    }

    private static SaveTables IdentityTables(ContentDb content) =>
        TablesWith(content, [.. content.Buildings.Names]);

    private static SaveTables TablesWith(ContentDb content, string[] buildings)
    {
        var header = new SaveHeader
        {
            Goods = [.. content.Goods.Names],
            Buildings = buildings,
            Recipes = [.. content.Recipes.Names],
            Tiers = [.. content.Tiers.Names],
            Techs = [.. content.Techs.Names],
            Civics = [.. content.Civics.Names],
            Traditions = [.. content.Traditions.Names],
            Festivals = [.. content.Festivals.Names],
            GreatWorks = [.. content.GreatWorks.Names],
            Monuments = [.. content.Monuments.Names],
            Happenings = [.. content.Happenings.Names],
            Eras = [.. content.Eras.Names],
            Terrains = [.. content.Terrains.Names],
        };
        return SaveTables.Build(header, content);
    }

    private static SaveTables TablesWithTechs(ContentDb content, string[] techs)
    {
        var header = new SaveHeader
        {
            Goods = [.. content.Goods.Names],
            Buildings = [.. content.Buildings.Names],
            Recipes = [.. content.Recipes.Names],
            Tiers = [.. content.Tiers.Names],
            Techs = techs,
            Civics = [.. content.Civics.Names],
            Traditions = [.. content.Traditions.Names],
            Festivals = [.. content.Festivals.Names],
            GreatWorks = [.. content.GreatWorks.Names],
            Monuments = [.. content.Monuments.Names],
            Happenings = [.. content.Happenings.Names],
            Eras = [.. content.Eras.Names],
            Terrains = [.. content.Terrains.Names],
        };
        return SaveTables.Build(header, content);
    }
}
