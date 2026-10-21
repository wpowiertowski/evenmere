using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Tests.Scenarios;

public sealed class ScenarioParserTests
{
    // The file does not exist; Parse only needs a path to show in messages, and a path inside the repository shows relative to it.
    private const string FileName = "tests/fixtures/scenarios/example.json";

    private static ScenarioDefinition Parse(string json) => ScenarioParser.Parse(json, Path.Combine(ScenarioPaths.RepoRoot, FileName));

    private static string ErrorOf(string json) => Should.Throw<ScenarioException>(() => Parse(json)).Message;

    [Fact]
    public void Parse_FullScenario_ReadsEveryField()
    {
        const string Json = """
            {
              "description": "everything",
              "content": "tests/fixtures/content/minimal",
              "map": null,
              "seed": 18446744073709551615,
              "ticks": 500,
              "commands": [
                { "tick": 1, "type": "RecordTick", "args": { "label": "a" } },
                { "tick": 500, "type": "UnservedCommand" }
              ],
              "expect": {
                "hash": "00ff00ff00ff00ff",
                "events": [
                  { "type": "TickRecorded", "atLeast": 1 },
                  { "type": "CommandRejected", "exactly": 0 }
                ],
                "stock": { "timber": ">= 10", "fish": "==0.5", "wood": "< 3" }
              }
            }
            """;

        ScenarioDefinition scenario = Parse(Json);

        scenario.Description.ShouldBe("everything");
        scenario.ContentPath.ShouldBe("tests/fixtures/content/minimal");
        scenario.Map.ShouldBeNull();
        scenario.Seed.ShouldBe(ulong.MaxValue);
        scenario.Ticks.ShouldBe(500);
        scenario.Commands.Select(static c => (c.Index, c.Tick, c.Type)).ShouldBe([(0, 1, "RecordTick"), (1, 500, "UnservedCommand")]);
        scenario.Commands[0].Args.GetProperty("label").GetString().ShouldBe("a");
        scenario.Commands[1].Args.EnumerateObject().ShouldBeEmpty();
        scenario.Expect.Hash.ShouldBe(0x00ff00ff00ff00ffUL);
        scenario.Expect.Events.ShouldBe([new EventExpectation("TickRecorded", 1, Exact: false), new EventExpectation("CommandRejected", 0, Exact: true)]);
        scenario.Expect.Stock.ShouldBe(
        [
            new StockExpectation("timber", ComparisonOp.GreaterOrEqual, Amount.FromUnits(10)),
            new StockExpectation("fish", ComparisonOp.Equal, Amount.Parse("0.5")),
            new StockExpectation("wood", ComparisonOp.Less, Amount.FromUnits(3)),
        ]);
    }

    [Fact]
    public void Parse_OnlyRequiredFields_UsesEmptyDefaults()
    {
        ScenarioDefinition scenario = Parse("""{ "content": "c", "seed": 1, "ticks": 0 }""");

        scenario.Description.ShouldBeEmpty();
        scenario.Map.ShouldBeNull();
        scenario.Commands.ShouldBeEmpty();
        scenario.Expect.ShouldBe(ScenarioExpectations.None);
    }

    [Fact]
    public void Parse_NullHash_IsTreatedAsNoHash()
    {
        ScenarioDefinition scenario = Parse("""{ "content": "c", "seed": 1, "ticks": 0, "expect": { "hash": null } }""");

        scenario.Expect.Hash.ShouldBeNull();
    }

    [Fact]
    public void Parse_MapGiven_IsReadAndLeftToTheRunnerToRefuse()
    {
        ScenarioDefinition scenario = Parse("""{ "content": "c", "map": "starter", "seed": 1, "ticks": 0 }""");

        scenario.Map.ShouldBe("starter");
    }

    [Theory]
    [InlineData("""{ "seed": 1, "ticks": 0 }""", "content: is required")]
    [InlineData("""{ "content": "c", "ticks": 0 }""", "seed: is required")]
    [InlineData("""{ "content": "c", "seed": 1 }""", "ticks: is required")]
    [InlineData("""{ "content": "c", "seed": -1, "ticks": 0 }""", "seed: must be a non-negative integer")]
    [InlineData("""{ "content": "c", "seed": 1.5, "ticks": 0 }""", "seed: must be a non-negative integer")]
    [InlineData("""{ "content": "c", "seed": "42", "ticks": 0 }""", "seed: must be a non-negative integer")]
    [InlineData("""{ "content": "c", "seed": 1, "ticks": -1 }""", "ticks: must be a whole number at least 0")]
    [InlineData("""{ "content": "c", "seed": 1, "ticks": 2.5 }""", "ticks: must be a whole number")]
    [InlineData("""{ "content": "", "seed": 1, "ticks": 0 }""", "content: must be a non-empty string")]
    [InlineData("""{ "content": "c", "map": 3, "seed": 1, "ticks": 0 }""", "map: must be a string or null")]
    [InlineData("""{ "content": "c", "seed": 1, "tick": 5, "ticks": 0 }""", "has an unknown property \"tick\"")]
    [InlineData("""[1, 2]""", "the scenario: must be a JSON object")]
    public void Parse_BadTopLevel_NamesTheFileAndTheProperty(string json, string expected)
    {
        string message = ErrorOf(json);

        message.ShouldStartWith(FileName + ": ");
        message.ShouldContain(expected);
    }

    [Theory]
    [InlineData("""{ "tick": 0, "type": "RecordTick" }""", "commands[0].tick: must be a whole number from 1 to 10")]
    [InlineData("""{ "tick": 11, "type": "RecordTick" }""", "commands[0].tick: must be a whole number from 1 to 10")]
    [InlineData("""{ "tick": 1.5, "type": "RecordTick" }""", "commands[0].tick: must be a whole number")]
    [InlineData("""{ "type": "RecordTick" }""", "commands[0].tick: is required")]
    [InlineData("""{ "tick": 1 }""", "commands[0].type: is required")]
    [InlineData("""{ "tick": 1, "type": "" }""", "commands[0].type: must be a non-empty string")]
    [InlineData("""{ "tick": 1, "type": "RecordTick", "args": [] }""", "commands[0].args: must be an object")]
    [InlineData("""{ "tick": 1, "type": "RecordTick", "arg": {} }""", "commands[0]: has an unknown property \"arg\"")]
    public void Parse_BadCommand_NamesTheFileAndTheCommandIndex(string entry, string expected)
    {
        string json = $$"""{ "content": "c", "seed": 1, "ticks": 10, "commands": [ {{entry}} ] }""";

        string message = ErrorOf(json);

        message.ShouldStartWith(FileName + ": ");
        message.ShouldContain(expected);
    }

    [Fact]
    public void Parse_SecondCommandBad_ReportsIndexOne()
    {
        const string Json = """
            { "content": "c", "seed": 1, "ticks": 10, "commands": [
              { "tick": 1, "type": "RecordTick" },
              { "tick": 99, "type": "RecordTick" } ] }
            """;

        ErrorOf(Json).ShouldContain("commands[1].tick");
    }

    [Theory]
    [InlineData("""{ "hash": "ABCDEF0123456789" }""", "expect.hash: must be 16 lowercase hex characters")]
    [InlineData("""{ "hash": "abc" }""", "expect.hash: must be 16 lowercase hex characters")]
    [InlineData("""{ "hash": 5 }""", "expect.hash: must be 16 lowercase hex characters")]
    [InlineData("""{ "hash": "0123456789abcdeg" }""", "expect.hash: must be 16 lowercase hex characters")]
    [InlineData("""{ "events": [ { "type": "A" } ] }""", "expect.events[0]: needs exactly one of \"atLeast\" and \"exactly\"")]
    [InlineData("""{ "events": [ { "type": "A", "atLeast": 1, "exactly": 1 } ] }""", "expect.events[0]: needs exactly one of")]
    [InlineData("""{ "events": [ { "type": "A", "atLeast": -1 } ] }""", "expect.events[0].atLeast: must be a whole number at least 0")]
    [InlineData("""{ "events": [ { "atLeast": 1 } ] }""", "expect.events[0].type: is required")]
    [InlineData("""{ "events": [ { "type": "A", "atLeast": 1, "min": 1 } ] }""", "has an unknown property \"min\"")]
    [InlineData("""{ "events": {} }""", "expect.events: must be an array")]
    [InlineData("""{ "stock": { "timber": "10" } }""", "expect.stock.timber: \"10\" must start with one of >=, <=, ==, >, <")]
    [InlineData("""{ "stock": { "timber": "!= 10" } }""", "expect.stock.timber")]
    [InlineData("""{ "stock": { "timber": ">= ten" } }""", "expect.stock.timber: \"ten\" is not a quantity")]
    [InlineData("""{ "stock": { "timber": ">= 1.2345" } }""", "is not a quantity")]
    [InlineData("""{ "stock": { "timber": 10 } }""", "expect.stock.timber: must be a string")]
    [InlineData("""{ "stock": [] }""", "expect.stock: must be an object")]
    [InlineData("""{ "residents": { "settlers": "10" } }""", "expect.residents.settlers: \"10\" must start with one of >=, <=, ==, >, <")]
    [InlineData("""{ "residents": { "settlers": ">= ten" } }""", "expect.residents.settlers: \"ten\" is not a quantity")]
    [InlineData("""{ "residents": { "settlers": ">= 1.5" } }""", "expect.residents.settlers: must name a whole non-negative count of residents")]
    [InlineData("""{ "residents": { "settlers": ">= -1" } }""", "expect.residents.settlers: must name a whole non-negative count of residents")]
    [InlineData("""{ "residents": { "settlers": 10 } }""", "expect.residents.settlers: must be a string")]
    [InlineData("""{ "residents": [] }""", "expect.residents: must be an object")]
    [InlineData("""{ "hashes": [] }""", "expect: has an unknown property \"hashes\"")]
    public void Parse_BadExpect_NamesTheFileAndTheProperty(string expect, string expected)
    {
        string json = $$"""{ "content": "c", "seed": 1, "ticks": 10, "expect": {{expect}} }""";

        string message = ErrorOf(json);

        message.ShouldStartWith(FileName + ": ");
        message.ShouldContain(expected);
    }

    [Fact]
    public void Parse_ResidentsExpectations_ReadTierOperatorAndCount()
    {
        const string Json = """{ "content": "c", "seed": 1, "ticks": 10, "expect": { "residents": { "settlers": ">= 8", "villagers": "== 100" } } }""";

        ScenarioDefinition scenario = Parse(Json);

        scenario.Expect.Residents.ShouldBe(
        [
            new ResidentExpectation("settlers", ComparisonOp.GreaterOrEqual, 8),
            new ResidentExpectation("villagers", ComparisonOp.Equal, 100),
        ]);
    }

    [Theory]
    [InlineData("{ \"content\": \"c\", ")]
    [InlineData("""{ "content": "c", "seed": 1, "ticks": 0, }""")]
    [InlineData("{ \"content\": \"c\", \"seed\": 1, // note\n\"ticks\": 0 }")]
    [InlineData("""{ "content": "c", "seed": 1, "seed": 2, "ticks": 0 }""")]
    public void Parse_MalformedJson_FailsWithTheFileName(string json)
    {
        string message = ErrorOf(json);

        message.ShouldStartWith(FileName + ": ");
    }

    [Fact]
    public void ParseFile_FileOnDisk_ReportsItsPathRelativeToTheRepository()
    {
        string path = Path.Combine(ScenarioPaths.ScenarioDirectory, "empty_world_10k.json");

        ScenarioDefinition scenario = ScenarioParser.ParseFile(path);

        scenario.DisplayName.ShouldBe("tests/fixtures/scenarios/empty_world_10k.json");
        scenario.SourcePath.ShouldBe(path);
    }
}
