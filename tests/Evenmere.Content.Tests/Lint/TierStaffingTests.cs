using System.Collections.Immutable;
using Evenmere.Content.Lint;
using Evenmere.Content.Lint.Rules;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Lint;

/// <summary>
/// EVL010: for every tier's basic good and service, at least one production chain or provider staffed entirely by
/// lower tiers stays available. Upgrades may be staffed by any tier, including the tier they serve, as long as that
/// fallback stays; comfort goods are exempt, so a comfort good made by its own tier passes while a basic good with no
/// lower-tier chain fails.
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
    public void Run_BasicGoodWithNoLowerTierFallback_FailsNamingBuildingAndChain()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Replace("buildings/woodcutters_hut.json", "\"tier\": \"settlers\"", "\"tier\": \"villagers\"");
        });

        // Settlers need wood too, but the first tier is exempt: only the villagers' need is reported.
        diagnostics.WithCode(LintCodes.Evl010).ShouldBeExactly(
            Expected.Error(LintCodes.Evl010, Villagers, "/basicNeeds/goods/wood", "woodcutters_hut (staffed by 'villagers'"));
        ContentDiagnostic diagnostic = diagnostics.Single();
        diagnostic.Message.ShouldContain("has no lower-tier fallback for basic good 'wood'");
        diagnostic.Message.ShouldContain("wood → cut_wood → woodcutters_hut");
        diagnostic.Message.ShouldContain("Upgrades may be staffed by any tier");
    }

    [Fact]
    public void Run_UpgradeStaffedByOwnTier_PassesWhenFallbackRemains()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("goods/bread.json", """{ "id": "bread", "category": "consumer", "era": 1 }""");
            content.Write("goods/flour.json", """{ "id": "flour", "category": "intermediate", "era": 1 }""");
            content.Write("recipes/bake_bread.json", """{ "id": "bake_bread", "inputs": { "flour": 1 }, "outputs": { "bread": 1 }, "cycleSeconds": 30 }""");
            content.Write("recipes/grind_flour.json", """{ "id": "grind_flour", "inputs": { "wood": 1 }, "outputs": { "flour": 1 }, "cycleSeconds": 20 }""");
            content.Write("buildings/bakery.json", Bakery());
            content.Write("buildings/windmill.json", Windmill("settlers"));

            // A villagers-staffed upgrade of the bakery: allowed, because the settler chain stays available.
            content.Write("buildings/bulk_bakery.json", BulkBakery());
            content.Write("locale/en/extra.json", BreadLocale());
            content.Replace(Villagers, "\"goods\": { \"fish\": 4, \"wood\": 4 }", "\"goods\": { \"fish\": 4, \"wood\": 4, \"bread\": 2 }");
        });

        // The upgrade is staffed by villagers and makes villagers' basic bread, yet the settler fallback stays.
        diagnostics.WithCode(LintCodes.Evl010).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_TransitiveInputMadeByOwnTier_ReportsTheFullChain()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(BreadChain);

        diagnostics.WithCode(LintCodes.Evl010).ShouldBeExactly(
            Expected.Error(LintCodes.Evl010, Villagers, "/basicNeeds/goods/bread", "windmill (staffed by 'villagers'"));
        ContentDiagnostic diagnostic = diagnostics.Single();
        diagnostic.Message.ShouldContain("bread → bake_bread → bakery");
        diagnostic.Message.ShouldContain("flour → grind_flour → windmill");
        diagnostic.Message.ShouldContain("Upgrades may be staffed by any tier");
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
    public void Run_ServiceWithNoLowerTierFallback_FailsAtTheServicesArray()
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
        diagnostics.Single().Message.ShouldContain("for the 'market' service as a basic need");
    }

    [Fact]
    public void Run_ServiceFallbackKeptAvailable_Passes()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Replace(
                "buildings/founders_hall.json",
                "\"outputs\": { \"knowledge\": 2, \"culture\": 1 },",
                "\"workforce\": { \"tier\": \"villagers\", \"count\": 2 },\n  \"outputs\": { \"knowledge\": 2, \"culture\": 1 },");

            // A settler-staffed market hall keeps the market fallback standing beside the upgraded hall.
            content.Write("buildings/market_hall.json", MarketHall());
            content.Write("locale/en/extra.json", MarketLocale());
        });

        diagnostics.WithCode(LintCodes.Evl010).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_LowerTierChainWithoutUnlockSource_HasNoAvailableFallback()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Replace("buildings/woodcutters_hut.json", "\"start\": true,", string.Empty);
        });

        // The settler wood chain still exists, but nothing unlocks it, so it is not an available fallback.
        // EVL005 reports the missing source; EVL010 reports the tier need it leaves without a fallback.
        diagnostics.WithCode(LintCodes.Evl005).ShouldHaveSingleItem();
        diagnostics.WithCode(LintCodes.Evl010).ShouldBeExactly(
            Expected.Error(LintCodes.Evl010, Villagers, "/basicNeeds/goods/wood", "unavailable"));
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
            Expected.Error(LintCodes.Evl010, Villagers, "/basicNeeds/goods/fish", "fishing_hut (staffed by 'villagers'"));
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
        content.Write("buildings/windmill.json", Windmill("villagers"));
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

    private static string Windmill(string tier) => $$"""
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
          "workforce": { "tier": "{{tier}}", "count": 3 },
          "recipe": "grind_flour",
          "start": true,
          "tags": ["processing"]
        }
        """;

    private static string BulkBakery() => """
        {
          "id": "bulk_bakery",
          "category": "production",
          "era": 1,
          "footprint": [2, 2],
          "placement": {
            "terrain": ["plains", "forest"]
          },
          "cost": { "timber": 3 },
          "buildDays": 1,
          "workforce": { "tier": "villagers", "count": 3 },
          "recipe": "bake_bread",
          "upgradeOf": "bakery",
          "start": true,
          "tags": ["processing", "food"]
        }
        """;

    private static string MarketHall() => """
        {
          "id": "market_hall",
          "category": "service",
          "era": 1,
          "footprint": [2, 2],
          "placement": {
            "terrain": ["plains", "forest"]
          },
          "cost": { "timber": 3 },
          "buildDays": 1,
          "workforce": { "tier": "settlers", "count": 2 },
          "service": { "kind": "market", "coverage": 10 },
          "upgradeOf": "founders_hall",
          "start": true,
          "tags": ["hall"]
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
          "building.windmill.desc": "Grinds flour.",
          "building.bulk_bakery.name": "Bulk bakery",
          "building.bulk_bakery.desc": "An upgrade that still leaves the old bakery standing."
        }
        """;

    private static string MarketLocale() => """
        {
          "building.market_hall.name": "Market hall",
          "building.market_hall.desc": "A settler-staffed market that keeps the fallback standing."
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
          "recipe.grow_apples.name": "Pick orchard fruit",
          "recipe.grow_apples.desc": "Grow orchard fruit.",
          "recipe.press_cider.name": "Press cider",
          "recipe.press_cider.desc": "Turn apples into cider.",
          "building.orchard.name": "Orchard",
          "building.orchard.desc": "Grows apples.",
          "building.cider_press.name": "Cider press",
          "building.cider_press.desc": "Presses cider."
        }
        """;
}
