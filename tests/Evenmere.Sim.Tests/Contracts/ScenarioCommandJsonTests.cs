using System.Text.Json;
using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Advisors.Commands;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Systems.Population.Commands;
using Evenmere.Sim.Systems.Production.Commands;
using Evenmere.Sim.Systems.Research.Commands;
using Evenmere.Sim.Systems.Roads.Commands;
using Evenmere.Sim.Systems.Workforce.Commands;
using Evenmere.Sim.Tests.Fixtures;
using Evenmere.Sim.Tests.Scenarios;

namespace Evenmere.Sim.Tests.Contracts;

/// <summary>
/// Proves that a scenario file can write every command, with the argument shapes scenario authors will use. Without a
/// converter System.Text.Json reads a value type it does not know as zero, silently, so each non-trivial parameter type
/// (tiles, rotations, arrays of tiles and of content IDs) is checked here.
/// </summary>
public sealed class ScenarioCommandJsonTests
{
    private static ContentDb Content => WorldBuilder.Content;

    private static JsonSerializerOptions Options => ScenarioJson.CreateOptions(Content);

    private static Command Read(string name, string args)
    {
        CommandRegistry.Default.TryGet(name, out CommandRegistration? registration).ShouldBeTrue($"{name} is not registered");
        using var document = JsonDocument.Parse(args);
        return CommandRegistry.Deserialize(registration!, document.RootElement, Options);
    }

    private static T Id<T>(string name)
        where T : struct, IContentId<T> => Content.IdOf<T>(name);

    [Fact]
    public void Deserialize_PlaceBuilding_ReadsTypeCoordinatesAndRotation()
    {
        Command command = Read("PlaceBuilding", """{ "type": "woodcutters_hut", "x": 10, "y": 12, "rotation": 1 }""");

        command.ShouldBe(new PlaceBuilding(Id<BuildingTypeId>("woodcutters_hut"), 10, 12, Rotation.Deg90));
    }

    [Fact]
    public void Deserialize_Demolish_ReadsTheEntityId()
    {
        Read("Demolish", """{ "building": 7 }""").ShouldBe(new Demolish(new EntityId(7)));
    }

    [Fact]
    public void Deserialize_UpgradeBuilding_ReadsTheEntityAndTheTargetType()
    {
        Read("UpgradeBuilding", """{ "building": 3, "toType": "storehouse" }""")
            .ShouldBe(new UpgradeBuilding(new EntityId(3), Id<BuildingTypeId>("storehouse")));
    }

    [Fact]
    public void Deserialize_PlaceRoad_ReadsAnArrayOfTiles()
    {
        Command command = Read("PlaceRoad", """{ "path": [[1, 2], [2, 2], [3, 2]], "roadType": "dirt_road" }""");

        command.ShouldBe(new PlaceRoad([new TileCoord(1, 2), new TileCoord(2, 2), new TileCoord(3, 2)], Id<BuildingTypeId>("dirt_road")));
    }

    [Fact]
    public void Deserialize_PlaceRoad_AcceptsAnEmptyPath()
    {
        Command command = Read("PlaceRoad", """{ "path": [], "roadType": "dirt_road" }""");

        command.ShouldBe(new PlaceRoad([], Id<BuildingTypeId>("dirt_road")));
    }

    [Fact]
    public void Deserialize_RemoveRoad_ReadsAnArrayOfTiles()
    {
        Read("RemoveRoad", """{ "tiles": [[4, 5], [4, 6]] }""").ShouldBe(new RemoveRoad([new TileCoord(4, 5), new TileCoord(4, 6)]));
    }

    [Fact]
    public void Deserialize_SetBuildingPaused_ReadsABoolean()
    {
        Read("SetBuildingPaused", """{ "building": 9, "paused": true }""").ShouldBe(new SetBuildingPaused(new EntityId(9), true));
    }

    [Fact]
    public void Deserialize_SetWorkforcePriority_ReadsABoolean()
    {
        Read("SetWorkforcePriority", """{ "building": 9, "high": true }""").ShouldBe(new SetWorkforcePriority(new EntityId(9), true));
    }

    [Fact]
    public void Deserialize_SetHouseUpgradeLock_ReadsABoolean()
    {
        Read("SetHouseUpgradeLock", """{ "house": 2, "locked": false }""").ShouldBe(new SetHouseUpgradeLock(new EntityId(2), false));
    }

    [Fact]
    public void Deserialize_SetResearchTarget_ReadsATechId()
    {
        Read("SetResearchTarget", """{ "tech": "net_weaving" }""").ShouldBe(new SetResearchTarget(Id<TechId>("net_weaving")));
    }

    [Fact]
    public void Deserialize_SetResearchQueue_ReadsAnArrayOfTechIds()
    {
        Command command = Read("SetResearchQueue", """{ "queue": ["net_weaving", "town_charter"] }""");

        command.ShouldBe(new SetResearchQueue([Id<TechId>("net_weaving"), Id<TechId>("town_charter")]));
    }

    [Fact]
    public void Deserialize_SetCivicTarget_ReadsACivicId()
    {
        Read("SetCivicTarget", """{ "civic": "village_moot" }""").ShouldBe(new SetCivicTarget(Id<CivicId>("village_moot")));
    }

    [Fact]
    public void Deserialize_SetCivicQueue_ReadsAnArrayOfCivicIds()
    {
        Read("SetCivicQueue", """{ "queue": ["village_moot"] }""").ShouldBe(new SetCivicQueue([Id<CivicId>("village_moot")]));
    }

    [Fact]
    public void Deserialize_SetResearchQueue_AcceptsAnEmptyQueue()
    {
        Read("SetResearchQueue", """{ "queue": [] }""").ShouldBe(new SetResearchQueue([]));
    }

    [Fact]
    public void Deserialize_DismissAdvice_ReadsAnInteger()
    {
        Read("DismissAdvice", """{ "adviceId": 12 }""").ShouldBe(new DismissAdvice(12));
    }

    [Fact]
    public void Deserialize_PlaceBuilding_NonZeroValuesAreNotSilentlyReadAsZero()
    {
        // The failure the converters exist to prevent: a value type System.Text.Json does not know reads as default.
        var command = (PlaceBuilding)Read("PlaceBuilding", """{ "type": "storehouse", "x": 1, "y": 2, "rotation": 3 }""");

        command.Rotation.ShouldBe(Rotation.Deg270);
        command.Type.ShouldBe(Id<BuildingTypeId>("storehouse"));
        (command.X, command.Y).ShouldBe((1, 2));
    }

    [Theory]
    [InlineData("PlaceBuilding", """{ "type": "storehouse", "x": 1, "y": 2, "rotation": 4 }""", "quarter turns, 0 to 3")]
    [InlineData("PlaceBuilding", """{ "type": "storehouse", "x": 1, "y": 2, "rotation": -1 }""", "quarter turns, 0 to 3")]
    [InlineData("PlaceBuilding", """{ "type": "storehouse", "x": 1, "y": 2, "rotation": "east" }""", "quarter turns, 0 to 3")]
    [InlineData("PlaceBuilding", """{ "type": "no_such_building", "x": 1, "y": 2, "rotation": 0 }""", "No BuildingTypeId named")]
    [InlineData("PlaceBuilding", """{ "type": "storehouse", "x": 1, "y": 2 }""", "missing required properties")]
    [InlineData("PlaceRoad", """{ "path": [[1]], "roadType": "dirt_road" }""", "[x, y]")]
    [InlineData("PlaceRoad", """{ "path": [[1, 2, 3]], "roadType": "dirt_road" }""", "[x, y]")]
    [InlineData("PlaceRoad", """{ "path": [{ "x": 1, "y": 2 }], "roadType": "dirt_road" }""", "[x, y]")]
    [InlineData("PlaceRoad", """{ "path": [[1, "two"]], "roadType": "dirt_road" }""", "[x, y]")]
    [InlineData("PlaceRoad", """{ "path": null, "roadType": "dirt_road" }""", "path")]
    [InlineData("SetResearchQueue", """{ "queue": ["net_weaving", "no_such_tech"] }""", "No TechId named")]
    [InlineData("SetResearchQueue", """{ "queue": "net_weaving" }""", "queue")]
    public void Deserialize_ArgsThatDoNotFit_ThrowJsonException(string name, string args, string expected)
    {
        JsonException error = Should.Throw<JsonException>(() => Read(name, args));

        error.Message.ShouldContain(expected);
    }

    [Fact]
    public void Scenario_UsingEveryM1Command_RunsThroughTheHarnessAndReachesTheHandlers()
    {
        // The end-to-end path: scenario file text -> parsed args -> queued command -> router. The world is empty (no map yet),
        // so every command is refused, but each must reach its handler (never UnknownCommand) with the args the file wrote.
        string json = ScenarioText.Make(ticks: 2, commands: ScenarioText.CommandList(
            """{ "tick": 1, "type": "PlaceBuilding", "args": { "type": "woodcutters_hut", "x": 4, "y": 5, "rotation": 2 } }""",
            """{ "tick": 1, "type": "Demolish", "args": { "building": 1 } }""",
            """{ "tick": 1, "type": "UpgradeBuilding", "args": { "building": 1, "toType": "storehouse" } }""",
            """{ "tick": 1, "type": "PlaceRoad", "args": { "path": [[1, 1], [2, 1]], "roadType": "dirt_road" } }""",
            """{ "tick": 1, "type": "RemoveRoad", "args": { "tiles": [[1, 1]] } }""",
            """{ "tick": 1, "type": "SetBuildingPaused", "args": { "building": 1, "paused": true } }""",
            """{ "tick": 1, "type": "SetWorkforcePriority", "args": { "building": 1, "high": true } }""",
            """{ "tick": 1, "type": "SetHouseUpgradeLock", "args": { "house": 1, "locked": true } }""",
            """{ "tick": 2, "type": "SetResearchTarget", "args": { "tech": "net_weaving" } }""",
            """{ "tick": 2, "type": "SetResearchQueue", "args": { "queue": ["net_weaving"] } }""",
            """{ "tick": 2, "type": "SetCivicTarget", "args": { "civic": "village_moot" } }""",
            """{ "tick": 2, "type": "SetCivicQueue", "args": { "queue": ["village_moot"] } }""",
            """{ "tick": 2, "type": "DismissAdvice", "args": { "adviceId": 1 } }"""));

        ScenarioResult result = ScenarioText.Run(json);

        Command[] sent =
        [
            new PlaceBuilding(Id<BuildingTypeId>("woodcutters_hut"), 4, 5, Rotation.Deg180),
            new Demolish(new EntityId(1)),
            new UpgradeBuilding(new EntityId(1), Id<BuildingTypeId>("storehouse")),
            new PlaceRoad([new TileCoord(1, 1), new TileCoord(2, 1)], Id<BuildingTypeId>("dirt_road")),
            new RemoveRoad([new TileCoord(1, 1)]),
            new SetBuildingPaused(new EntityId(1), true),
            new SetWorkforcePriority(new EntityId(1), true),
            new SetHouseUpgradeLock(new EntityId(1), true),
            new SetResearchTarget(Id<TechId>("net_weaving")),
            new SetResearchQueue([Id<TechId>("net_weaving")]),
            new SetCivicTarget(Id<CivicId>("village_moot")),
            new SetCivicQueue([Id<CivicId>("village_moot")]),
            new DismissAdvice(1),
        ];
        CommandRejected[] rejections = [.. result.Events.OfType<CommandRejected>()];

        rejections.ShouldNotContain(static e => e.Code == RejectReason.UnknownCommand);
        foreach (CommandRejected rejection in rejections)
        {
            sent.ShouldContain(rejection.Command);
        }
    }

    public static TheoryData<string> CommandNames()
    {
        var data = new TheoryData<string>();
        foreach (Type type in CommandSamples.AllCommandTypes())
        {
            data.Add(type.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CommandNames))]
    public void RoundTrip_EveryCommand_SurvivesBeingWrittenAndReadAsScenarioJson(string name)
    {
        // Covers every command the simulation declares, so a command added later with a parameter type that has no
        // converter fails here instead of silently reading as zero in a scenario.
        Type type = CommandSamples.AllCommandTypes().Single(t => t.Name == name);
        Command original = CommandSamples.Create(type, 0, Content);

        string json = JsonSerializer.Serialize(original, type, Options);
        Command read = Read(name, json);

        read.ShouldBe(original, $"{name} written as {json}");
    }
}
