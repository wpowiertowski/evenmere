using System.Collections.Immutable;
using Evenmere.Content.Lint;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Lint;

/// <summary>EVL004 beyond the committed fixtures: each way a good can be produced or consumed.</summary>
public sealed class GoodFlowRuleTests
{
    /// <summary>Each consumer of a good the rule recognises: a name for the test, and an edit that makes <c>extra</c> a consumer.</summary>
    public static TheoryData<string> ConsumerKinds =>
    [
        "recipe input",
        "tier basic need",
        "tier comfort need",
        "tier upgrade cost",
        "building cost",
        "building fuel",
        "monument stage cost",
        "festival cost",
    ];

    [Theory]
    [MemberData(nameof(ConsumerKinds))]
    public void Run_GoodWithAProducer_IsConsumedByEachKindOfConsumer(string kind)
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            AddExtraProducedByFishers(content);
            MakeExtraConsumed(content, kind);
        });

        diagnostics.WithCode(LintCodes.Evl004).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_GoodOnlyUsedAsAConditionOrAGrant_HasNoConsumer()
    {
        // Counting a good in an inspiration, or granting it, is not consuming it.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            AddExtraProducedByFishers(content);
            content.Replace(
                "techs/net_weaving.json",
                "\"count\": { \"building\": \"fishing_hut\", \"atLeast\": 5 }",
                "\"produced\": { \"good\": \"extra\", \"atLeast\": 5 }");
            content.Replace("techs/town_charter.json", "{ \"unlockFeature\": \"expeditions\" }", "{ \"unlockFeature\": \"expeditions\" },\n    { \"grantGoods\": { \"extra\": 2 } }");
        });

        diagnostics.WithCode(LintCodes.Evl004).ShouldBeExactly(
            Expected.Error(LintCodes.Evl004, "goods/extra.json", string.Empty, "no consumer"));
    }

    [Fact]
    public void Run_GoodNobodyMakes_IsAConsumerOnlyProblem()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("goods/extra.json", """{ "id": "extra", "category": "raw", "era": 1 }""");
            content.Write("locale/en/extra.json", """{ "good.extra.name": "Extra", "good.extra.desc": "Spare." }""");
            MakeExtraConsumed(content, "building cost");
        });

        diagnostics.WithCode(LintCodes.Evl004).ShouldBeExactly(
            Expected.Error(LintCodes.Evl004, "goods/extra.json", string.Empty, "no producer"));
    }

    [Fact]
    public void Run_GoodProducedByARecipeNoBuildingRuns_StillCountsAsProduced()
    {
        // The rule looks at recipes, as the card says; whether a building runs the recipe is EVL005's and EVL006's business.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("goods/extra.json", """{ "id": "extra", "category": "raw", "era": 1 }""");
            content.Write("recipes/make_extra.json", """{ "id": "make_extra", "outputs": { "extra": 1 }, "cycleSeconds": 10, "start": true }""");
            content.Write("locale/en/extra.json", """
                { "good.extra.name": "Extra", "good.extra.desc": "Spare.", "recipe.make_extra.name": "Make extra", "recipe.make_extra.desc": "Make it." }
                """);
            MakeExtraConsumed(content, "building cost");
        });

        diagnostics.WithCode(LintCodes.Evl004).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    private static void AddExtraProducedByFishers(TempContent content)
    {
        content.Write("goods/extra.json", """{ "id": "extra", "category": "raw", "era": 1 }""");
        content.Write("locale/en/extra.json", """{ "good.extra.name": "Extra", "good.extra.desc": "Spare." }""");
        content.Replace("recipes/catch_fish.json", "\"outputs\": { \"fish\": 1 }", "\"outputs\": { \"fish\": 1, \"extra\": 1 }");
    }

    private static void MakeExtraConsumed(TempContent content, string kind)
    {
        switch (kind)
        {
            case "recipe input":
                content.Replace("recipes/saw_timber.json", "\"inputs\": { \"wood\": 1 }", "\"inputs\": { \"wood\": 1, \"extra\": 1 }");
                break;
            case "tier basic need":
                content.Replace("tiers/settlers.json", "\"goods\": { \"fish\": 4, \"wood\": 3 }", "\"goods\": { \"fish\": 4, \"wood\": 3, \"extra\": 1 }");
                break;
            case "tier comfort need":
                content.Replace("tiers/settlers.json", "\"start\": true", "\"comfortNeeds\": { \"extra\": 1 },\n  \"start\": true");
                break;
            case "tier upgrade cost":
                content.Replace("tiers/villagers.json", "\"upgradeCost\": { \"timber\": 2 }", "\"upgradeCost\": { \"timber\": 2, \"extra\": 1 }");
                break;
            case "building cost":
                content.Replace("buildings/sawpit.json", "\"cost\": { \"timber\": 3 }", "\"cost\": { \"timber\": 3, \"extra\": 1 }");
                break;
            case "building fuel":
                content.Replace("buildings/sawpit.json", "\"buildDays\": 1,", "\"buildDays\": 1,\n  \"fuel\": { \"good\": \"extra\", \"perDay\": 1 },");
                break;
            case "monument stage cost":
                content.Write("buildings/standing_stones.json", """
                    { "id": "standing_stones", "category": "monument", "era": 1, "footprint": [3, 3], "buildDays": 12, "unique": true, "start": true }
                    """);
                content.Write("monuments/standing_stones.json", """{ "id": "standing_stones", "building": "standing_stones", "stages": [ { "extra": 5 } ] }""");
                content.Write("locale/en/stones.json", """
                    {
                      "building.standing_stones.name": "Standing stones", "building.standing_stones.desc": "Old stones.",
                      "monument.standing_stones.name": "Standing stones", "monument.standing_stones.desc": "A circle."
                    }
                    """);
                break;
            case "festival cost":
                content.Write("festivals/harvest_fair.json", """{ "id": "harvest_fair", "cost": { "extra": 2 }, "days": 3, "culturePerDay": 2, "start": true }""");
                content.Write("locale/en/fair.json", """{ "festival.harvest_fair.name": "Harvest fair", "festival.harvest_fair.desc": "A fair." }""");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown consumer kind.");
        }
    }
}
