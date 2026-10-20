using System.Collections.Immutable;
using Evenmere.Content.Lint;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Lint;

/// <summary>EVL005 beyond the committed fixtures: every kind of unlockable item and every kind of source.</summary>
public sealed class UnlockRuleTests
{
    private const string TierJson = """
        { "id": "townsfolk", "order": 3, "era": 1, "capacity": 15, "advanceFrom": "villagers", "basicNeeds": { "services": ["market"] } }
        """;

    private const string TraditionJson = """{ "id": "kinship", "slot": "hearth" }""";

    private const string FestivalJson = """{ "id": "harvest_fair", "cost": { "timber": 2 }, "days": 3, "culturePerDay": 2 }""";

    private const string RecipeJson = """
        { "id": "mill_boards", "inputs": { "wood": 1 }, "outputs": { "timber": 1 }, "cycleSeconds": 20 }
        """;

    private const string MonumentBuildingJson = """
        { "id": "standing_stones", "category": "monument", "era": 1, "footprint": [3, 3], "buildDays": 12, "unique": true }
        """;

    [Fact]
    public void Run_TierNothingUnlocks_HasNoSource()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content => AddTier(content, TierJson));

        diagnostics.WithCode(LintCodes.Evl005).ShouldBeExactly(
            Expected.Error(LintCodes.Evl005, "tiers/townsfolk.json", string.Empty, "tier 'townsfolk' has no unlock source"));
    }

    [Fact]
    public void Run_TierUnlockedByATech_HasOneSource()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            AddTier(content, TierJson);
            AddEffect(content, "techs/town_charter.json", """{ "unlockTier": "townsfolk" }""");
        });

        diagnostics.WithCode(LintCodes.Evl005).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_TierUnlockedByATechAndStart_HasTwoSources()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            AddTier(content, TierJson.Replace("\"capacity\"", "\"start\": true, \"capacity\"", StringComparison.Ordinal));
            AddEffect(content, "techs/town_charter.json", """{ "unlockTier": "townsfolk" }""");
        });

        diagnostics.WithCode(LintCodes.Evl005).ShouldBeExactly(
            Expected.Error(LintCodes.Evl005, "tiers/townsfolk.json", "/start", "2 unlock sources"));
    }

    [Fact]
    public void Run_TraditionUnlockedByTwoNodes_HasTwoSources()
    {
        // village_moot already unlocks common_hearth; a tech unlocking it too makes two.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
            AddEffect(content, "techs/net_weaving.json", """{ "unlockTradition": "common_hearth" }"""));

        diagnostics.WithCode(LintCodes.Evl005).ShouldBeExactly(
            Expected.Error(LintCodes.Evl005, "traditions/common_hearth.json", string.Empty, "tech 'net_weaving'"));
        diagnostics.Single(d => d.Code == LintCodes.Evl005).Message.ShouldContain("civic 'village_moot'");
    }

    [Fact]
    public void Run_TraditionNothingUnlocks_HasNoSource_AndAStartFlagFixesIt()
    {
        ImmutableArray<ContentDiagnostic> without = LintVariants.Lint(content => AddTradition(content, TraditionJson));
        ImmutableArray<ContentDiagnostic> with = LintVariants.Lint(content =>
            AddTradition(content, TraditionJson.Replace("\"hearth\"", "\"hearth\", \"start\": true", StringComparison.Ordinal)));

        without.WithCode(LintCodes.Evl005).ShouldBeExactly(
            Expected.Error(LintCodes.Evl005, "traditions/kinship.json", string.Empty, "has no unlock source"));
        with.WithCode(LintCodes.Evl005).ShouldBeEmpty();
    }

    [Fact]
    public void Run_FestivalNothingUnlocks_HasNoSource_AndUnlockFestivalFixesIt()
    {
        ImmutableArray<ContentDiagnostic> without = LintVariants.Lint(content => AddFestival(content, FestivalJson));
        ImmutableArray<ContentDiagnostic> with = LintVariants.Lint(content =>
        {
            AddFestival(content, FestivalJson);
            AddEffect(content, "civics/village_moot.json", """{ "unlockFestival": "harvest_fair" }""");
        });

        without.WithCode(LintCodes.Evl005).ShouldBeExactly(
            Expected.Error(LintCodes.Evl005, "festivals/harvest_fair.json", string.Empty, "has no unlock source"));
        with.WithCode(LintCodes.Evl005).ShouldBeEmpty();
    }

    [Fact]
    public void Run_RecipeNoBuildingNames_NeedsOneSource()
    {
        ImmutableArray<ContentDiagnostic> none = LintVariants.Lint(content => AddRecipe(content, RecipeJson));
        ImmutableArray<ContentDiagnostic> start = LintVariants.Lint(content =>
            AddRecipe(content, RecipeJson.Replace("\"cycleSeconds\": 20", "\"cycleSeconds\": 20, \"start\": true", StringComparison.Ordinal)));
        ImmutableArray<ContentDiagnostic> unlocked = LintVariants.Lint(content =>
        {
            AddRecipe(content, RecipeJson);
            AddEffect(content, "techs/net_weaving.json", """{ "unlockRecipe": "mill_boards" }""");
        });
        ImmutableArray<ContentDiagnostic> twice = LintVariants.Lint(content =>
        {
            AddRecipe(content, RecipeJson.Replace("\"cycleSeconds\": 20", "\"cycleSeconds\": 20, \"start\": true", StringComparison.Ordinal));
            AddEffect(content, "techs/net_weaving.json", """{ "unlockRecipe": "mill_boards" }""");
        });

        none.WithCode(LintCodes.Evl005).ShouldBeExactly(
            Expected.Error(LintCodes.Evl005, "recipes/mill_boards.json", string.Empty, "no building names this recipe"));
        start.WithCode(LintCodes.Evl005).ShouldBeEmpty();
        unlocked.WithCode(LintCodes.Evl005).ShouldBeEmpty();
        twice.WithCode(LintCodes.Evl005).ShouldBeExactly(
            Expected.Error(LintCodes.Evl005, "recipes/mill_boards.json", "/start", "2 unlock sources"));
    }

    [Fact]
    public void Run_RecipeANamedBuildingRuns_NeedsNoSource_EvenIfOtherSourcesExist()
    {
        // saw_timber is run by the sawpit, so it comes with the sawpit: no start flag, and no complaint about a stray unlock.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Replace("recipes/saw_timber.json", "\"cycleSeconds\": 20,\n  \"start\": true", "\"cycleSeconds\": 20");
            AddEffect(content, "techs/net_weaving.json", """{ "unlockRecipe": "saw_timber" }""");
        });

        diagnostics.WithCode(LintCodes.Evl005).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_BuildingUnlockedByAMonument_HasOneSourceInTheMonumentsEffects()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            AddMonument(content);
            content.Write("buildings/windmill.json", Windmill());
            content.Write("locale/en/windmill.json", """{ "building.windmill.name": "Windmill", "building.windmill.desc": "Grinds." }""");
            AddEffect(content, "monuments/standing_stones.json", """{ "unlockBuilding": "windmill" }""");
        });

        diagnostics.WithCode(LintCodes.Evl005).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_BuildingUnlockedByAMonumentAndATech_HasTwoSources()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            AddMonument(content);
            content.Write("buildings/windmill.json", Windmill());
            content.Write("locale/en/windmill.json", """{ "building.windmill.name": "Windmill", "building.windmill.desc": "Grinds." }""");
            AddEffect(content, "monuments/standing_stones.json", """{ "unlockBuilding": "windmill" }""");
            AddEffect(content, "techs/net_weaving.json", """{ "unlockBuilding": "windmill" }""");
        });

        ContentDiagnostic diagnostic = diagnostics.WithCode(LintCodes.Evl005).ShouldHaveOne(LintCodes.Evl005, "buildings/windmill.json", string.Empty);
        diagnostic.Message.ShouldContain("tech 'net_weaving' (techs/net_weaving.json#/effects/1)");
        diagnostic.Message.ShouldContain("monument 'standing_stones' (monuments/standing_stones.json#/effects/0)");
    }

    [Fact]
    public void Run_BuildingUnlockedTwiceByOneTech_HasTwoSources()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("buildings/windmill.json", Windmill());
            content.Write("locale/en/windmill.json", """{ "building.windmill.name": "Windmill", "building.windmill.desc": "Grinds." }""");
            AddEffect(content, "techs/net_weaving.json", """{ "unlockBuilding": "windmill" }""");
            AddEffect(content, "techs/net_weaving.json", """{ "unlockBuilding": "windmill" }""");
        });

        ContentDiagnostic diagnostic = diagnostics.WithCode(LintCodes.Evl005).ShouldHaveOne(LintCodes.Evl005, "buildings/windmill.json", string.Empty);
        diagnostic.Message.ShouldContain("techs/net_weaving.json#/effects/1");
        diagnostic.Message.ShouldContain("techs/net_weaving.json#/effects/2");
    }

    [Fact]
    public void Run_UnlockEffectInATradition_IsNotAnUnlockSource()
    {
        // ADR-0004: techs, civics and monuments list unlocks. A tradition that names a building does not unlock it.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("buildings/windmill.json", Windmill());
            content.Write("locale/en/windmill.json", """{ "building.windmill.name": "Windmill", "building.windmill.desc": "Grinds." }""");
            AddEffect(content, "traditions/common_hearth.json", """{ "unlockBuilding": "windmill" }""");
        });

        diagnostics.WithCode(LintCodes.Evl005).ShouldBeExactly(
            Expected.Error(LintCodes.Evl005, "buildings/windmill.json", string.Empty, "has no unlock source"));
    }

    [Fact]
    public void Run_DiagnosticsAreInIdOrder_NotTheOrderThingsWereDefined()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            string[] ids = ["zeta_mill", "alpha_mill", "mid_mill"];
            foreach (string id in ids)
            {
                content.Write($"buildings/{id}.json", Windmill().Replace("windmill", id, StringComparison.Ordinal));
                content.Write($"locale/en/{id}.json", $$"""{ "building.{{id}}.name": "Mill", "building.{{id}}.desc": "Grinds." }""");
            }
        });

        diagnostics.WithCode(LintCodes.Evl005).Select(static d => d.File).ShouldBe(
            ["buildings/alpha_mill.json", "buildings/mid_mill.json", "buildings/zeta_mill.json"]);
    }

    private static string Windmill() => """
        { "id": "windmill", "category": "production", "era": 1, "footprint": [2, 2], "buildDays": 1 }
        """;

    private static void AddTier(TempContent content, string json)
    {
        content.Write("tiers/townsfolk.json", json);
        content.Write("locale/en/extra.json", """{ "tier.townsfolk.name": "Townsfolk", "tier.townsfolk.desc": "Townspeople." }""");
    }

    private static void AddTradition(TempContent content, string json)
    {
        content.Write("traditions/kinship.json", json);
        content.Write("locale/en/extra.json", """{ "tradition.kinship.name": "Kinship", "tradition.kinship.desc": "Family first." }""");
    }

    private static void AddFestival(TempContent content, string json)
    {
        content.Write("festivals/harvest_fair.json", json);
        content.Write("locale/en/extra.json", """{ "festival.harvest_fair.name": "Harvest fair", "festival.harvest_fair.desc": "A fair." }""");
    }

    private static void AddRecipe(TempContent content, string json)
    {
        content.Write("recipes/mill_boards.json", json);
        content.Write("locale/en/extra.json", """{ "recipe.mill_boards.name": "Mill boards", "recipe.mill_boards.desc": "Saw boards." }""");
    }

    private static void AddMonument(TempContent content)
    {
        content.Write("buildings/standing_stones.json", MonumentBuildingJson.Replace("\"unique\": true", "\"unique\": true, \"start\": true", StringComparison.Ordinal));
        content.Write("monuments/standing_stones.json", """{ "id": "standing_stones", "building": "standing_stones", "stages": [ { "timber": 5 } ] }""");
        content.Write("locale/en/stones.json", """
            {
              "building.standing_stones.name": "Standing stones", "building.standing_stones.desc": "Old stones.",
              "monument.standing_stones.name": "Standing stones", "monument.standing_stones.desc": "A circle."
            }
            """);
    }

    /// <summary>Appends an effect to the file's <c>effects</c> array, creating the array when the file has none.</summary>
    private static void AddEffect(TempContent content, string file, string effect) =>
        content.Write(file, JsonEdit.Append(content.Read(file), "/effects", effect));
}
