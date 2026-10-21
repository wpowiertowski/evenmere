using Evenmere.Sim.Tests.Fixtures;
using static Evenmere.Sim.Tests.Scenarios.ScenarioText;

namespace Evenmere.Sim.Tests.Scenarios;

public sealed class ScenarioRunnerTests
{
    private const string TimberGrant = """{ "tick": 5, "type": "GrantGoods", "args": { "good": "timber", "amount": "10" } }""";

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(59)]
    [InlineData(120)]
    public void Run_CommandWithTickT_IsAppliedOnTickT(int tick)
    {
        ScenarioResult result = Run(Make(ticks: 120, commands: CommandList(RecordTickAt(tick))));

        result.Events.OfType<TickRecorded>().Single().Tick.ShouldBe(tick);
    }

    [Fact]
    public void Run_CommandsOnTheSameTick_ApplyInFileOrder()
    {
        ScenarioResult result = Run(Make(commands: CommandList(RecordTickAt(4, "a"), RecordTickAt(4, "b"), RecordTickAt(4, "c"))));

        result.Events.OfType<TickRecorded>().Select(static e => (e.Tick, e.Label)).ShouldBe([(4L, "a"), (4L, "b"), (4L, "c")]);
    }

    [Fact]
    public void Run_CommandsOutOfTickOrderInTheFile_StillApplyOnTheirOwnTicks()
    {
        ScenarioResult result = Run(Make(commands: CommandList(RecordTickAt(7, "late"), RecordTickAt(2, "early"), RecordTickAt(7, "late too"))));

        result.Events.OfType<TickRecorded>().Select(static e => (e.Tick, e.Label)).ShouldBe([(2L, "early"), (7L, "late"), (7L, "late too")]);
    }

    [Fact]
    public void Run_CommandOnTheLastTick_IsApplied()
    {
        ScenarioResult result = Run(Make(ticks: 10, commands: CommandList(RecordTickAt(10))));

        result.Events.OfType<TickRecorded>().Single().Tick.ShouldBe(10);
    }

    [Fact]
    public void Run_Scenario_TicksExactlyTheRequestedNumberOfTimes()
    {
        Simulation sim = new WorldBuilder().WithSeed(9).Build();
        for (int i = 0; i < 37; i++)
        {
            sim.Tick();
        }

        ScenarioResult result = Run(Make(ticks: 37, seed: 9));

        result.ActualHash.ShouldBe(sim.ComputeStateHash());
    }

    [Fact]
    public void Run_ZeroTicks_HashesTheFreshWorld()
    {
        Simulation sim = new WorldBuilder().WithSeed(3).Build();

        ScenarioResult result = Run(Make(ticks: 0, seed: 3));

        result.ActualHash.ShouldBe(sim.ComputeStateHash());
        result.Events.ShouldBeEmpty();
    }

    [Fact]
    public void Run_SameScenarioTwice_GivesTheSameHash()
    {
        string json = Make(ticks: 200, seed: 11, commands: CommandList(TimberGrant));

        Run(json).ActualHash.ShouldBe(Run(json).ActualHash);
    }

    [Fact]
    public void Run_DifferentSeeds_GiveDifferentHashes()
    {
        Run(Make(ticks: 50, seed: 1)).ActualHash.ShouldNotBe(Run(Make(ticks: 50, seed: 2)).ActualHash);
    }

    [Fact]
    public void Run_ACommandThatChangesState_ChangesTheHash()
    {
        ulong without = Run(Make(ticks: 20)).ActualHash;

        ulong with = Run(Make(ticks: 20, commands: CommandList(TimberGrant))).ActualHash;

        with.ShouldNotBe(without);
    }

    [Fact]
    public void Run_GrantedGoods_ShowUpInTheStockExpectation()
    {
        ScenarioResult result = Run(Make(
            commands: CommandList(TimberGrant, """{ "tick": 6, "type": "GrantGoods", "args": { "good": "timber", "amount": "2.5" } }"""),
            expect: """{ "stock": { "timber": "== 12.5", "fish": "== 0", "wood": "<= 0" }, "events": [ { "type": "GoodsGranted", "exactly": 2 } ] }"""));

        result.Passed.ShouldBeTrue(result.Report);
        result.Failures.ShouldBeEmpty();
    }

    [Fact]
    public void Run_CommandTheHandlerRejects_EmitsCommandRejectedAndLeavesTheStock()
    {
        ScenarioResult result = Run(Make(
            commands: CommandList("""{ "tick": 3, "type": "GrantGoods", "args": { "good": "timber", "amount": "-1" } }"""),
            expect: """{ "stock": { "timber": "== 0" } }"""));

        CommandRejected rejected = result.Events.ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>();
        rejected.Tick.ShouldBe(3);
        rejected.Code.ShouldBe(RejectReason.Invalid);
        result.Passed.ShouldBeTrue(result.Report);
    }

    [Fact]
    public void Run_CommandNothingServes_EmitsUnknownCommandRejection()
    {
        ScenarioResult result = Run(Make(commands: CommandList("""{ "tick": 2, "type": "UnservedCommand" }""")));

        CommandRejected rejected = result.Events.ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>();
        rejected.Code.ShouldBe(RejectReason.UnknownCommand);
        rejected.Tick.ShouldBe(2);
    }

    [Fact]
    public void Run_EventExpectations_AtLeastAndExactlyAreChecked()
    {
        const string Expect = """
            { "events": [
                { "type": "TickRecorded", "atLeast": 2 },
                { "type": "TickRecorded", "exactly": 3 },
                { "type": "CommandRejected", "atLeast": 0 },
                { "type": "CommandRejected", "exactly": 0 } ] }
            """;

        ScenarioResult result = Run(Make(commands: CommandList(RecordTickAt(1), RecordTickAt(2), RecordTickAt(3)), expect: Expect));

        result.Passed.ShouldBeTrue(result.Report);
    }

    [Fact]
    public void Run_TooFewEvents_FailsNamingTheCounts()
    {
        ScenarioResult result = Run(Make(
            commands: CommandList(RecordTickAt(1)),
            expect: """{ "events": [ { "type": "TickRecorded", "atLeast": 3 } ] }"""));

        result.Passed.ShouldBeFalse();
        result.Failures.ShouldBe(["events: expected at least 3 TickRecorded event(s), but the run emitted 1."]);
    }

    [Fact]
    public void Run_WrongExactEventCount_FailsNamingTheCounts()
    {
        ScenarioResult result = Run(Make(
            commands: CommandList(RecordTickAt(1)),
            expect: """{ "events": [ { "type": "TickRecorded", "exactly": 2 } ] }"""));

        result.Failures.ShouldBe(["events: expected exactly 2 TickRecorded event(s), but the run emitted 1."]);
    }

    [Theory]
    [InlineData(">= 11", false)]
    [InlineData(">= 10", true)]
    [InlineData("> 10", false)]
    [InlineData("> 9.999", true)]
    [InlineData("<= 9.999", false)]
    [InlineData("<= 10", true)]
    [InlineData("< 10", false)]
    [InlineData("< 10.001", true)]
    [InlineData("== 10", true)]
    [InlineData("== 10.001", false)]
    public void Run_StockOperators_CompareAgainstTheFinalStock(string comparison, bool holds)
    {
        ScenarioResult result = Run(Make(commands: CommandList(TimberGrant), expect: $$"""{ "stock": { "timber": "{{comparison}}" } }"""));

        result.Passed.ShouldBe(holds);
    }

    [Fact]
    public void Run_StockExpectationNotMet_FailsNamingTheActualStock()
    {
        ScenarioResult result = Run(Make(commands: CommandList(TimberGrant), expect: """{ "stock": { "timber": ">= 25" } }"""));

        result.Failures.ShouldBe(["stock: expected timber >= 25, but the island holds 10."]);
    }

    [Fact]
    public void Run_SeveralFailures_ReportEventsThenStockThenHash()
    {
        ScenarioResult result = Run(Make(
            expect: """{ "hash": "0000000000000000", "stock": { "timber": ">= 1" }, "events": [ { "type": "TickRecorded", "atLeast": 1 } ] }"""));

        result.Failures.Count.ShouldBe(3);
        result.Failures[0].ShouldStartWith("events:");
        result.Failures[1].ShouldStartWith("stock:");
        result.Failures[2].ShouldStartWith("hash:");
        result.Report.ShouldContain("First failing expectation: events:");
        result.Report.ShouldContain("Also failing:");
    }

    [Fact]
    public void Run_NoHashInTheFile_PassesWithoutAHashCheck()
    {
        ScenarioResult result = Run(Make(ticks: 5));

        result.Passed.ShouldBeTrue();
        result.ExpectedHash.ShouldBeNull();
        result.GoldenUpdated.ShouldBeFalse();
    }

    [Fact]
    public void Run_MatchingHash_Passes()
    {
        ulong hash = Run(Make(ticks: 30, seed: 5)).ActualHash;

        ScenarioResult result = Run(Make(ticks: 30, seed: 5, expect: $$"""{ "hash": "{{HashText.Format(hash)}}" }"""));

        result.Passed.ShouldBeTrue(result.Report);
        result.ExpectedHash.ShouldBe(hash);
    }

    [Fact]
    public void Run_WrongHash_FailsAndShowsBothHashes()
    {
        ulong actual = Run(Make(ticks: 30, seed: 5)).ActualHash;

        ScenarioResult result = Run(Make(ticks: 30, seed: 5, expect: """{ "hash": "0123456789abcdef" }"""));

        result.Passed.ShouldBeFalse();
        result.ActualHash.ShouldBe(actual);
        result.ExpectedHash.ShouldBe(0x0123456789abcdefUL);
        result.Report.ShouldContain("expected hash: 0123456789abcdef");
        result.Report.ShouldContain("actual hash:   " + HashText.Format(actual));
    }

    [Fact]
    public void Run_FailureReport_NamesTheFileTheExpectationBothHashesAndTheEvents()
    {
        ScenarioResult result = Run(Make(
            ticks: 30,
            commands: CommandList(RecordTickAt(4, "four"), """{ "tick": 9, "type": "UnservedCommand" }"""),
            expect: """{ "hash": "0123456789abcdef", "events": [ { "type": "CommandRejected", "exactly": 0 } ] }"""));

        result.Report.ShouldStartWith("Scenario FAILED: ");
        result.Report.ShouldContain("harness-test.json");
        result.Report.ShouldContain("First failing expectation: events: expected exactly 0 CommandRejected event(s), but the run emitted 1.");
        result.Report.ShouldContain("expected hash: 0123456789abcdef");
        result.Report.ShouldContain("actual hash:   " + HashText.Format(result.ActualHash));
        result.Report.ShouldContain("tick       4  TickRecorded");
        result.Report.ShouldContain("tick       9  CommandRejected  UnknownCommand for UnservedCommand: No handler is registered for UnservedCommand.");
        result.Report.ShouldContain("seed: 1");
        result.Report.ShouldContain("ticks: 30");
    }

    [Fact]
    public void Run_FailureReport_ListsOnlyTheLastTwentyEvents()
    {
        string[] unserved = [.. Enumerable.Range(1, 25).Select(static t => $$"""{ "tick": {{t}}, "type": "UnservedCommand" }""")];

        ScenarioResult result = Run(Make(
            ticks: 30,
            commands: CommandList(unserved),
            expect: """{ "events": [ { "type": "CommandRejected", "exactly": 0 } ] }"""));

        result.Events.Count.ShouldBe(25);
        result.Report.ShouldContain("Events: 25 emitted; the last 20, oldest first:");
        result.Report.ShouldNotContain("tick       5  CommandRejected");
        result.Report.ShouldContain("tick       6  CommandRejected");
        result.Report.ShouldContain("tick      25  CommandRejected");
        result.Report.Split('\n').Count(static line => line.Contains("  CommandRejected  ", StringComparison.Ordinal)).ShouldBe(20);
    }

    [Fact]
    public void Run_FailureReportWithNoEvents_SaysSo()
    {
        ScenarioResult result = Run(Make(expect: """{ "hash": "0123456789abcdef" }"""));

        result.Report.ShouldContain("Events: none were emitted.");
        result.Report.ShouldContain("EVENMERE_UPDATE_GOLDEN=1");
    }

    [Fact]
    public void Run_PassingScenario_HasASummaryReport()
    {
        ScenarioResult result = Run(Make(ticks: 12, seed: 8));

        result.Report.ShouldStartWith("Scenario passed: ");
        result.Report.ShouldContain("12 ticks, seed 8, hash " + HashText.Format(result.ActualHash));
    }

    [Fact]
    public void Run_CommittedEmptyWorldSample_PassesAndIsTenThousandTicks()
    {
        string path = Path.Combine(ScenarioPaths.ScenarioDirectory, "empty_world_10k.json");

        ScenarioResult result = ScenarioRunner.RunFile(path, ScenarioRunOptions.Strict);

        result.Passed.ShouldBeTrue(result.Report);
        result.ExpectedHash.ShouldNotBeNull();
        result.Events.ShouldBeEmpty();
    }

    [Fact]
    public void Run_SeedEditedInACopyOfTheSample_FailsAndShowsBothHashes()
    {
        using var temp = new ScenarioTempFolder();
        string original = File.ReadAllText(Path.Combine(ScenarioPaths.ScenarioDirectory, "empty_world_10k.json"));
        original.ShouldContain("\"seed\": 42");
        string path = temp.Write("edited.json", original.Replace("\"seed\": 42", "\"seed\": 43", StringComparison.Ordinal));

        ScenarioResult result = ScenarioRunner.RunFile(path, ScenarioRunOptions.Strict);

        result.Passed.ShouldBeFalse();
        result.ExpectedHash.ShouldNotBe(result.ActualHash);
        result.Report.ShouldContain("expected hash: " + HashText.Format(result.ExpectedHash));
        result.Report.ShouldContain("actual hash:   " + HashText.Format(result.ActualHash));
    }

    [Fact]
    public void Run_MapGiven_LoadsTheNamedMapWithTheRealContent()
    {
        const string Json = """{ "content": "content", "map": "starter", "seed": 1, "ticks": 10 }""";

        ScenarioResult result = Run(Json);

        result.Passed.ShouldBeTrue(result.Report);
    }

    [Fact]
    public void Run_UnknownMap_FailsNamingTheFileAndTheMapId()
    {
        const string Json = """{ "content": "content", "map": "no_such_island", "seed": 1, "ticks": 1 }""";

        ScenarioException error = Should.Throw<ScenarioException>(() => Run(Json));

        error.Message.ShouldContain("harness-test.json");
        error.Message.ShouldContain("map: cannot load map \"no_such_island\"");
        error.Message.ShouldContain("maps/no_such_island");
    }

    [Fact]
    public void Run_AtForm_ResolvesToTheBuildingOnTheTile()
    {
        const string Json = """
            { "content": "content", "map": "starter", "seed": 1, "ticks": 300,
              "commands": [
                { "tick": 1, "type": "PlaceBuilding", "args": { "type": "sawpit", "x": 32, "y": 93, "rotation": 0 } },
                { "tick": 200, "type": "Demolish", "args": { "building": { "at": [32, 93] } } } ],
              "expect": { "events": [
                { "type": "BuildingDemolished", "exactly": 1 },
                { "type": "CommandRejected", "exactly": 0 } ] } }
            """;

        ScenarioResult result = Run(Json);

        result.Passed.ShouldBeTrue(result.Report);
    }

    [Fact]
    public void Run_PlainIntegerId_StillDemolishesById()
    {
        const string Json = """
            { "content": "content", "map": "starter", "seed": 1, "ticks": 300,
              "commands": [
                { "tick": 1, "type": "PlaceBuilding", "args": { "type": "sawpit", "x": 32, "y": 93, "rotation": 0 } },
                { "tick": 200, "type": "Demolish", "args": { "building": 2 } } ],
              "expect": { "events": [
                { "type": "BuildingDemolished", "exactly": 1 },
                { "type": "CommandRejected", "exactly": 0 } ] } }
            """;

        ScenarioResult result = Run(Json);

        result.Passed.ShouldBeTrue(result.Report);
    }

    [Fact]
    public void Run_AtFormWithNoBuilding_FailsNamingTheFileAndTheCommandIndex()
    {
        const string Json = """
            { "content": "content", "map": "starter", "seed": 1, "ticks": 10,
              "commands": [ { "tick": 5, "type": "Demolish", "args": { "building": { "at": [10, 10] } } } ] }
            """;

        ScenarioException error = Should.Throw<ScenarioException>(() => Run(Json));

        error.Message.ShouldContain("harness-test.json");
        error.Message.ShouldContain("commands[0]");
        error.Message.ShouldContain("{ \"at\": [10, 10] } names no building");
    }

    [Theory]
    [InlineData("{ \"at\": [1] }", "commands[0].args.building: must be { \"at\": [x, y] }")]
    [InlineData("{ \"at\": [1, 2, 3] }", "commands[0].args.building: must be { \"at\": [x, y] }")]
    [InlineData("{ \"at\": \"hut\" }", "commands[0].args.building: must be { \"at\": [x, y] }")]
    [InlineData("{ \"at\": [1, 2], \"extra\": 3 }", "commands[0].args: the args do not fit")]
    public void Run_MalformedAtForm_FailsNamingTheCommandIndex(string at, string expected)
    {
        string json = "{ \"content\": \"content\", \"map\": \"starter\", \"seed\": 1, \"ticks\": 10, "
            + "\"commands\": [ { \"tick\": 5, \"type\": \"Demolish\", \"args\": { \"building\": " + at + " } } ] }";

        ScenarioException error = Should.Throw<ScenarioException>(() => Run(json));

        error.Message.ShouldContain("harness-test.json");
        error.Message.ShouldContain(expected);
    }

    [Fact]
    public void Run_AtFormOnANonEntityProperty_FailsSayingItFitsOnlyEntityIds()
    {
        const string Json = """
            { "content": "content", "map": "starter", "seed": 1, "ticks": 10,
              "commands": [ { "tick": 5, "type": "PlaceBuilding", "args": { "type": { "at": [32, 93] }, "x": 32, "y": 93, "rotation": 0 } } ] }
            """;

        ScenarioException error = Should.Throw<ScenarioException>(() => Run(Json));

        error.Message.ShouldContain("harness-test.json");
        error.Message.ShouldContain("commands[0].args.type");
        error.Message.ShouldContain("fits only an entity id");
    }

    [Fact]
    public void Run_ResidentsExpectations_CompareAgainstTheFinalCounts()
    {
        ScenarioResult result = Run(Make(expect: """{ "residents": { "settlers": "== 0", "villagers": "<= 0" } }"""));

        result.Passed.ShouldBeTrue(result.Report);
    }

    [Fact]
    public void Run_ResidentsExpectationNotMet_FailsNamingTheActualCount()
    {
        ScenarioResult result = Run(Make(expect: """{ "residents": { "settlers": ">= 1" } }"""));

        result.Passed.ShouldBeFalse();
        result.Failures.ShouldBe(["residents: expected settlers >= 1, but the island holds 0."]);
    }

    [Fact]
    public void Run_UnknownTierInResidents_FailsNamingTheTier()
    {
        ScenarioException error = Should.Throw<ScenarioException>(() => Run(Make(expect: """{ "residents": { "peasants": ">= 1" } }""")));

        error.Message.ShouldContain("harness-test.json");
        error.Message.ShouldContain("expect.residents.peasants: no tier named \"peasants\"");
    }

    [Fact]
    public void Run_UnknownCommandType_FailsNamingTheFileAndTheCommandIndex()
    {
        // The name must stay unknown for good, so it is not one a real command could take (PlaceBuilding became real in M1-00).
        string json = Make(commands: CommandList(RecordTickAt(1), """{ "tick": 2, "type": "BuildTheMoon", "args": {} }"""));

        ScenarioException error = Should.Throw<ScenarioException>(() => Run(json));

        error.Message.ShouldContain("harness-test.json");
        error.Message.ShouldContain("commands[1].type: unknown command type \"BuildTheMoon\"");
        error.Message.ShouldContain("Known command types:");
    }

    [Fact]
    public void Run_ArgsThatDoNotFit_FailNamingTheFileAndTheCommandIndex()
    {
        string json = Make(commands: CommandList(RecordTickAt(1), RecordTickAt(2), """{ "tick": 3, "type": "RecordTick", "args": { "lable": "typo" } }"""));

        ScenarioException error = Should.Throw<ScenarioException>(() => Run(json));

        error.Message.ShouldContain("harness-test.json");
        error.Message.ShouldContain("commands[2].args: the args do not fit RecordTick");
        error.Message.ShouldContain("could not be mapped");
        error.Message.ShouldNotContain("LineNumber");
    }

    [Fact]
    public void Run_UnknownGoodInArgs_FailsNamingTheCommandIndex()
    {
        string json = Make(commands: CommandList("""{ "tick": 1, "type": "GrantGoods", "args": { "good": "timbr", "amount": "1" } }"""));

        ScenarioException error = Should.Throw<ScenarioException>(() => Run(json));

        error.Message.ShouldContain("commands[0].args");
        error.Message.ShouldContain("No GoodId named \"timbr\"");
    }

    [Fact]
    public void Run_BadCommand_FailsBeforeTheFirstTick()
    {
        // Ten million ticks would take far longer than the test run, so failing fast proves the commands are checked first.
        string json = Make(ticks: 10_000_000, commands: CommandList("""{ "tick": 1, "type": "Nope" }"""));

        Should.Throw<ScenarioException>(() => Run(json)).Message.ShouldContain("commands[0].type");
    }

    [Fact]
    public void Run_UnknownEventType_FailsNamingTheExpectation()
    {
        ScenarioException error = Should.Throw<ScenarioException>(
            () => Run(Make(expect: """{ "events": [ { "type": "CommandRejected", "atLeast": 0 }, { "type": "BuildingFinished", "exactly": 0 } ] }""")));

        error.Message.ShouldContain("expect.events[1].type: unknown event type \"BuildingFinished\"");
    }

    [Fact]
    public void Run_UnknownGoodInStock_FailsNamingTheGoods()
    {
        ScenarioException error = Should.Throw<ScenarioException>(
            () => Run(Make(expect: """{ "stock": { "gold": ">= 1" } }""")));

        error.Message.ShouldContain("expect.stock.gold: no good named \"gold\"");
        error.Message.ShouldContain("fish, timber, wood");
    }

    [Fact]
    public void Run_MissingContentFolder_FailsNamingThePath()
    {
        const string Json = """{ "content": "tests/fixtures/content/nowhere", "seed": 1, "ticks": 1 }""";

        ScenarioException error = Should.Throw<ScenarioException>(() => Run(Json));

        error.Message.ShouldContain("content: the folder \"tests/fixtures/content/nowhere\" does not exist");
    }

    [Fact]
    public void RunFile_ScenarioWithNoExpectations_Passes()
    {
        using var temp = new ScenarioTempFolder();
        string path = temp.Write("plain.json", Make(ticks: 3, expect: "{}"));

        ScenarioResult result = ScenarioRunner.RunFile(path, ScenarioRunOptions.Strict);

        result.Passed.ShouldBeTrue();
        result.Scenario.ShouldBe(path);
    }
}
