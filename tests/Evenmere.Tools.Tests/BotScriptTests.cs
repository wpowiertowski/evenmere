using Evenmere.Tools.Balance;

namespace Evenmere.Tools.Tests.Balance;

/// <summary>Parses bot scripts and rejects bad ones with the goal index.</summary>
public sealed class BotScriptTests
{
    [Fact]
    public void Parse_SliceScript_HasExpectedGoals()
    {
        var script = BotScript.ParseFile(Path.Combine(RepoRoot(), "tools", "bots", "slice.json"));

        script.Name.ShouldBe("slice");
        script.Goals.Length.ShouldBeGreaterThan(10);
        script.Goals[0].ShouldBeOfType<ResearchGoal>();
        script.Goals[2].ShouldBeOfType<BuildGoal>();
    }

    [Fact]
    public void Parse_AllGoalKinds_ReadsArguments()
    {
        var script = BotScript.Parse("""
            {
              "name": "probe",
              "goals": [
                { "research": "cultivation", "queue": ["milling"], "timeoutMinutes": 45 },
                { "civic": "hearth_and_shrine" },
                { "build": "market", "count": 2, "near": "founders_hall" },
                { "ensureSupply": "bread", "targetFulfilment": 0.9 },
                { "ensureSupply": { "good": "fish", "targetFulfilment": 1.0 } },
                { "housesUntil": { "tier": "villagers", "residents": 100 }, "timeoutMinutes": 60 },
                { "lockHouses": 6 },
                { "unlockHouses": 4 },
                { "waitUntilTick": 3600 }
              ]
            }
            """);

        script.Name.ShouldBe("probe");
        ResearchGoal research = script.Goals[0].ShouldBeOfType<ResearchGoal>();
        research.Tech.ShouldBe("cultivation");
        research.Queue.ShouldBe(["milling"]);
        research.TimeoutTicks.ShouldBe(45 * BotScript.TicksPerGameMinute);
        CivicGoal civic = script.Goals[1].ShouldBeOfType<CivicGoal>();
        civic.TimeoutTicks.ShouldBe(BotGoal.DefaultTimeoutMinutes * BotScript.TicksPerGameMinute);
        BuildGoal build = script.Goals[2].ShouldBeOfType<BuildGoal>();
        (build.Building, build.Count, build.Near).ShouldBe(("market", 2, "founders_hall"));
        EnsureSupplyGoal flat = script.Goals[3].ShouldBeOfType<EnsureSupplyGoal>();
        flat.TargetPerMille.ShouldBe(900);
        EnsureSupplyGoal nested = script.Goals[4].ShouldBeOfType<EnsureSupplyGoal>();
        nested.TargetPerMille.ShouldBe(1000);
        HousesUntilGoal houses = script.Goals[5].ShouldBeOfType<HousesUntilGoal>();
        (houses.Tier, houses.Residents).ShouldBe(("villagers", 100));
        script.Goals[6].ShouldBeOfType<LockHousesGoal>().Count.ShouldBe(6);
        script.Goals[7].ShouldBeOfType<UnlockHousesGoal>().Count.ShouldBe(4);
        script.Goals[8].ShouldBeOfType<WaitUntilTickGoal>().Tick.ShouldBe(3600);
    }

    [Theory]
    [InlineData("""{ "goals": [{ "build": "market", "bulid": 1 }] }""", "goal 0", "bulid")]
    [InlineData("""{ "goals": [{ "research": "cultivation" }, { "civic": "x", "bogus": 1 }] }""", "goal 1", "bogus")]
    [InlineData("""{ "goals": [{ "housesUntil": { "tier": "villagers", "residents": 3, "extra": 1 } }] }""", "goal 0", "extra")]
    public void Parse_UnknownKey_NamesGoalIndex(string text, string index, string key)
    {
        BotScriptException ex = Should.Throw<BotScriptException>(() => BotScript.Parse(text));
        ex.Message.ShouldContain(index);
        ex.Message.ShouldContain(key);
    }

    [Theory]
    [InlineData("""{ "goals": [{ "build": "market", "research": "cultivation" }] }""")]
    [InlineData("""{ "goals": [{ "count": 2 }] }""")]
    [InlineData("""{ "goals": [{ "build": 7 }] }""")]
    [InlineData("""{ "goals": [{ "build": "market", "count": 0 }] }""")]
    [InlineData("""{ "goals": [] }""")]
    [InlineData("""{ "goals": [{ "waitUntilTick": 0 }] }""")]
    public void Parse_MalformedGoal_Throws(string text)
    {
        Should.Throw<BotScriptException>(() => BotScript.Parse(text));
    }

    [Fact]
    public void Parse_NotJson_Throws()
    {
        Should.Throw<BotScriptException>(() => BotScript.Parse("not json"));
    }

    [Fact]
    public void Parse_MissingFile_NamesPath()
    {
        BotScriptException ex = Should.Throw<BotScriptException>(() => BotScript.ParseFile(Path.Combine(RepoRoot(), "tools", "bots", "missing.json")));
        ex.Message.ShouldContain("missing.json");
    }

    [Fact]
    public void ValidateContent_UnknownName_NamesGoalIndex()
    {
        var script = BotScript.Parse("""{ "goals": [{ "build": "market", "count": 1 }, { "civic": "no_such_civic" }] }""");
        BotScriptException ex = Should.Throw<BotScriptException>(() => script.ValidateContent(BotWorld.Content));
        ex.Message.ShouldContain("goal 1");
        ex.Message.ShouldContain("no_such_civic");
    }

    [Fact]
    public void ValidateContent_SliceScript_Passes()
    {
        var script = BotScript.ParseFile(Path.Combine(RepoRoot(), "tools", "bots", "slice.json"));
        Should.NotThrow(() => script.ValidateContent(BotWorld.Content));
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Evenmere.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Evenmere.sln was not found above " + AppContext.BaseDirectory);
    }
}
