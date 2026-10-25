using System.Collections.Immutable;
using Evenmere.Content.Lint;
using Evenmere.Content.Lint.Rules;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Lint;

/// <summary>
/// EVL010: a tier's basic goods and services are made and staffed by lower tiers. Comfort goods are exempt, so a
/// comfort good made by its own tier passes while a basic good made by its own tier fails (ART-01).
/// </summary>
public sealed class TierStaffingTests
{
    private const string Villagers = "tiers/villagers.json";

    [Fact]
    public void Run_RealContent_Passes()
    {
        ContentLoadResult result = ContentLoader.Load(Path.Combine(TestPaths.RepoRoot, "content"));

        ImmutableArray<ContentDiagnostic> diagnostics = ContentLinter.Run(result);

        diagnostics.WithCode(LintCodes.Evl010).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_MinimalPack_Passes()
    {
        ContentLoadResult result = ContentLoader.Load(TestPaths.MinimalPack);

        ImmutableArray<ContentDiagnostic> diagnostics = ContentLinter.Run(result);

        diagnostics.WithCode(LintCodes.Evl010).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_BasicGoodMadeByOwnTier_FailsNamingBuildingAndChain()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Replace("buildings/woodcutters_hut.json", "\"tier\": \"settlers\"", "\"tier\": \"villagers\"");
        });

        // Settlers need wood too, but the first tier is exempt: only the villagers' need is reported.
        diagnostics.WithCode(LintCodes.Evl010).ShouldBeExactly(
            Expected.Error(LintCodes.Evl010, Villagers, "/basicNeeds/goods/wood", "'woodcutters_hut' is staffed by 'villagers'"));
        ContentDiagnostic diagnostic = diagnostics.Single();
        diagnostic.Message.ShouldContain("which is not lower than 'villagers'");
        diagnostic.Message.ShouldContain("wood → cut_wood → woodcutters_hut");
        diagnostic.Message.ShouldContain("comfort goods are exempt");
    }

    [Fact]
    public void Run_TransitiveInputMadeByOwnTier_ReportsTheFullChain()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(BreadChain);

        diagnostics.WithCode(LintCodes.Evl010).ShouldBeExactly(
            Expected.Error(LintCodes.Evl010, Villagers, "/basicNeeds/goods/bread", "'windmill' is staffed by 'villagers'"));
        ContentDiagnostic diagnostic = diagnostics.Single();
        diagnostic.Message.ShouldContain("bread → bake_bread → bakery\nflour → grind_flour → windmill");
        diagnostic.Message.ShouldContain("comfort goods are exempt");
    }

    [Fact]
    public void Run_ComfortGoodMadeByOwnTier_Passes()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("goods/cider.json", """{ "id": "cider", "category": "consumer", "era": 1 }""");
            content.Write("goods/apples.json", """{ "id": "apples", "category": "raw", "era": 1 }""");
            content.Write("recipes/grow_apples.json", """{ "id": "grow_apples", "outputs": { "apples": 1 }, "cycleSeconds": 20 }""");
            content.Write("recipes/press_cider.json", """{ "id": "press_cider", "inputs": { "apples": 1 }, "outputs": { "cider": 2 }, "cycleSeconds": 30 }""");
            content.Write("buildings/orchard.json", Orchard("settlers"));
            content.Write("buildings/cider_press.json", CiderPress(tier: "villagers"));
            content.Write("locale/en/extra.json", CiderLocale());
            content.Replace(Villagers, "\"outputs\": { \"knowledge\": 10 },", "\"comfortNeeds\": { \"cider\": 2 },\n  \"outputs\": { \"knowledge\": 10 },");
        });

        // The cider press is staffed by villagers and makes cider, a villager comfort good: comfort chains are exempt.
        diagnostics.WithCode(LintCodes.Evl010).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_ServiceProvidedByOwnTier_FailsAtTheServicesArray()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Replace(
                "buildings/founders_hall.json",
                "\"outputs\": { \"knowledge\": 2, \"culture\": 1 },",
                "\"workforce\": { \"tier\": \"villagers\", \"count\": 2 },\n  \"outputs\": { \"knowledge\": 2, \"culture\": 1 },");
        });

        diagnostics.WithCode(LintCodes.Evl010).ShouldBeExactly(
            Expected.Error(LintCodes.Evl010, Villagers, "/basicNeeds/services/0", "'founders_hall' providing it is staffed by 'villagers'"));
        diagnostics.Single().Message.ShouldContain("needs the 'market' service");
    }

    [Fact]
    public void Run_SettlersNeedStaffedByHigherTier_IsExempt()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Replace("buildings/fishing_hut.json", "\"tier\": \"settlers\"", "\"tier\": \"villagers\"");
        });

        // The first tier has nothing lower than it, so its staffing is never checked; only the villagers' need fails.
        diagnostics.WithCode(LintCodes.Evl010).ShouldBeExactly(
            Expected.Error(LintCodes.Evl010, Villagers, "/basicNeeds/goods/fish", "'fishing_hut' is staffed by 'villagers'"));
    }

    [Theory]
    [InlineData(4, 0, 4)]
    [InlineData(4, -25, 3)]
    [InlineData(10, 20, 12)]
    [InlineData(3, -33, 2)]
    [InlineData(1, 50, 2)]
    [InlineData(1, -90, 1)]
    [InlineData(4, -100, 1)]
    [InlineData(3, -200, 1)]
    public void EffectiveCrew_Rounding_MatchesAdr0015(int count, int percent, int expected)
    {
        TierStaffingRule.EffectiveCrew(count, percent).ShouldBe(expected);
    }

    private static void BreadChain(TempContent content)
    {
        content.Write("goods/bread.json", """{ "id": "bread", "category": "consumer", "era": 1 }""");
        content.Write("goods/flour.json", """{ "id": "flour", "category": "intermediate", "era": 1 }""");
        content.Write("recipes/bake_bread.json", """{ "id": "bake_bread", "inputs": { "flour": 1 }, "outputs": { "bread": 1 }, "cycleSeconds": 30 }""");
        content.Write("recipes/grind_flour.json", """{ "id": "grind_flour", "inputs": { "wood": 1 }, "outputs": { "flour": 1 }, "cycleSeconds": 20 }""");
        content.Write("buildings/bakery.json", Bakery());
        content.Write("buildings/windmill.json", Windmill());
        content.Write("locale/en/extra.json", BreadLocale());
        content.Replace(Villagers, "\"goods\": { \"fish\": 4, \"wood\": 4 }", "\"goods\": { \"fish\": 4, \"wood\": 4, \"bread\": 2 }");
    }

    private static string Bakery() => """
        {
          "id": "bakery",
          "category": "production",
          "era": 1,
          "footprint": [2, 2],
          "placement": {
            "terrain": ["plains", "forest"]
          },
          "cost": { "timber": 3 },
          "buildDays": 1,
          "workforce": { "tier": "settlers", "count": 4 },
          "recipe": "bake_bread",
          "start": true,
          "tags": ["processing", "food"]
        }
        """;

    private static string Windmill() => """
        {
          "id": "windmill",
          "category": "production",
          "era": 1,
          "footprint": [2, 2],
          "placement": {
            "terrain": ["plains", "forest"]
          },
          "cost": { "timber": 3 },
          "buildDays": 1,
          "workforce": { "tier": "villagers", "count": 3 },
          "recipe": "grind_flour",
          "start": true,
          "tags": ["processing"]
        }
        """;

    private static string BreadLocale() => """
        {
          "good.bread.name": "Bread",
          "good.bread.desc": "A loaf for every table.",
          "good.flour.name": "Flour",
          "good.flour.desc": "Milled grain.",
          "recipe.bake_bread.name": "Bake bread",
          "recipe.bake_bread.desc": "Turn flour into loaves.",
          "recipe.grind_flour.name": "Grind flour",
          "recipe.grind_flour.desc": "Turn wood into flour.",
          "building.bakery.name": "Bakery",
          "building.bakery.desc": "Bakes bread.",
          "building.windmill.name": "Windmill",
          "building.windmill.desc": "Grinds flour."
        }
        """;

    private static string Orchard(string tier) => $$"""
        {
          "id": "orchard",
          "category": "production",
          "era": 1,
          "footprint": [4, 4],
          "placement": {
            "terrain": ["plains"]
          },
          "cost": { "timber": 4 },
          "buildDays": 2,
          "workforce": { "tier": "{{tier}}", "count": 3 },
          "recipe": "grow_apples",
          "start": true,
          "tags": ["farm", "gatherer"]
        }
        """;

    private static string CiderPress(string tier) => $$"""
        {
          "id": "cider_press",
          "category": "production",
          "era": 1,
          "footprint": [2, 2],
          "placement": {
            "terrain": ["plains", "forest"]
          },
          "cost": { "timber": 4 },
          "buildDays": 2,
          "workforce": { "tier": "{{tier}}", "count": 2 },
          "recipe": "press_cider",
          "start": true,
          "tags": ["processing"]
        }
        """;

    private static string CiderLocale() => """
        {
          "good.cider.name": "Cider",
          "good.cider.desc": "Pressed apples.",
          "good.apples.name": "Apples",
          "good.apples.desc": "Orchard fruit.",
          "recipe.grow_apples.name": "Grow apples",
          "recipe.grow_apples.desc": "Pick orchard fruit.",
          "recipe.press_cider.name": "Press cider",
          "recipe.press_cider.desc": "Turn apples into cider.",
          "building.orchard.name": "Orchard",
          "building.orchard.desc": "Grows apples.",
          "building.cider_press.name": "Cider press",
          "building.cider_press.desc": "Presses cider."
        }
        """;
}
