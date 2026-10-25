using System.Collections.Immutable;
using Evenmere.Content.Lint;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Lint;

/// <summary>EVL006 beyond the committed fixtures: services, recipe cycles, sources and the era of a tier.</summary>
public sealed class TierNeedsRuleTests
{
    private const string Villagers = "tiers/villagers.json";

    [Fact]
    public void Run_ServiceNoBuildingProvides_IsReportedAtItsPositionInTheFile()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
            content.Replace(Villagers, "\"services\": [\"market\"]", "\"services\": [\"theatre\", \"market\", \"school\"]"));

        diagnostics.WithCode(LintCodes.Evl006).ShouldBeExactly(
            Expected.Error(LintCodes.Evl006, Villagers, "/basicNeeds/services/0", "'theatre' service, but no building unlocked by era 1 provides it, and no building provides it at all"),
            Expected.Error(LintCodes.Evl006, Villagers, "/basicNeeds/services/2", "'school' service"));
    }

    [Fact]
    public void Run_ServiceAStartBuildingProvides_IsMet()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("buildings/tavern.json", Tavern(start: true));
            content.Write("locale/en/tavern.json", """{ "building.tavern.name": "Tavern", "building.tavern.desc": "Drinks." }""");
            content.Replace(Villagers, "\"services\": [\"market\"]", "\"services\": [\"tavern\", \"market\"]");
        });

        diagnostics.WithCode(LintCodes.Evl006).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_ServiceAnUnlockedBuildingProvidesFromTheTiersEraOn_IsMet()
    {
        // The tavern is unlocked by town_charter (era 1), so a tier that starts in era 1 may need taverns.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("buildings/tavern.json", Tavern(start: false));
            content.Write("locale/en/tavern.json", """{ "building.tavern.name": "Tavern", "building.tavern.desc": "Drinks." }""");
            content.Replace("techs/town_charter.json", "{ \"unlockFeature\": \"expeditions\" }", "{ \"unlockFeature\": \"expeditions\" },\n    { \"unlockBuilding\": \"tavern\" }");
            content.Replace(Villagers, "\"services\": [\"market\"]", "\"services\": [\"tavern\", \"market\"]");
        });

        diagnostics.WithCode(LintCodes.Evl006).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_BreadAndFlourThatNeedEachOther_AreNeverProducible()
    {
        // Both buildings are available from the start, but neither recipe can ever run: a fixed point, not a walk, is needed.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("goods/bread.json", """{ "id": "bread", "category": "consumer", "era": 1 }""");
            content.Write("goods/flour.json", """{ "id": "flour", "category": "intermediate", "era": 1 }""");
            content.Write("recipes/bake_bread.json", """{ "id": "bake_bread", "inputs": { "flour": 1 }, "outputs": { "bread": 1 }, "cycleSeconds": 10 }""");
            content.Write("recipes/grind_flour.json", """{ "id": "grind_flour", "inputs": { "bread": 1 }, "outputs": { "flour": 1 }, "cycleSeconds": 10 }""");
            content.Write("buildings/bakery.json", Producer("bakery", "bake_bread"));
            content.Write("buildings/mill.json", Producer("mill", "grind_flour"));
            content.Write("locale/en/chain.json", """
                {
                  "good.bread.name": "Bread", "good.bread.desc": "Loaves.", "good.flour.name": "Flour", "good.flour.desc": "Ground grain.",
                  "recipe.bake_bread.name": "Bake", "recipe.bake_bread.desc": "Bake.", "recipe.grind_flour.name": "Grind", "recipe.grind_flour.desc": "Grind.",
                  "building.bakery.name": "Bakery", "building.bakery.desc": "Bakes.", "building.mill.name": "Mill", "building.mill.desc": "Grinds."
                }
                """);
            content.Replace(Villagers, "\"goods\": { \"fish\": 4, \"wood\": 4 }", "\"goods\": { \"fish\": 4, \"wood\": 4, \"bread\": 1 }");
        });

        diagnostics.WithCode(LintCodes.Evl006).ShouldBeExactly(
            Expected.Error(LintCodes.Evl006, Villagers, "/basicNeeds/goods/bread", "no chain of buildings makes it at all"));
    }

    [Fact]
    public void Run_TierNothingUnlocks_IsLeftToEvl005()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("goods/gems.json", """{ "id": "gems", "category": "consumer", "era": 1 }""");
            content.Write("tiers/townsfolk.json", """
                { "id": "townsfolk", "order": 3, "era": 1, "capacity": 15, "advanceFrom": "villagers", "basicNeeds": { "goods": { "gems": 1 } } }
                """);
            content.Write("locale/en/extra.json", """
                { "tier.townsfolk.name": "Townsfolk", "tier.townsfolk.desc": "Town.", "good.gems.name": "Gems", "good.gems.desc": "Shiny." }
                """);
        });

        diagnostics.WithCode(LintCodes.Evl006).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
        diagnostics.ShouldHaveOne(LintCodes.Evl005, "tiers/townsfolk.json", string.Empty);
    }

    [Fact]
    public void Run_TierNeeds_AreHeldToItsDeclaredEra_NotTheEraOfItsSource()
    {
        // Town Charter (era 1) unlocks Townsfolk, which declares era 2, so the era 2 school is available to it.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            TownsfolkNeedingASchool(content, declaredEra: 2, unlockedBy: "town_charter");
            content.Write("techs/brick_laying.json", """
                { "id": "brick_laying", "era": 2, "cost": 100, "requires": ["town_charter"], "effects": [ { "unlockBuilding": "school" } ] }
                """);
        });

        diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_TierThatDeclaresItsSourcesEra_IsHeldToThatEra()
    {
        // The same tier declaring era 1 has no school yet.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            TownsfolkNeedingASchool(content, declaredEra: 1, unlockedBy: "town_charter");
            content.Write("techs/brick_laying.json", """
                { "id": "brick_laying", "era": 2, "cost": 100, "requires": ["town_charter"], "effects": [ { "unlockBuilding": "school" } ] }
                """);
        });

        diagnostics.ShouldBeExactly(
            Expected.Error(LintCodes.Evl006, "tiers/townsfolk.json", "/basicNeeds/services/0", "tier 'townsfolk' (era 1) needs the 'school' service"));
    }

    [Fact]
    public void Run_TierDeclaringAnEarlierEraThanItsSource_IsReportedAtItsEra_AndItsNeedsAreStillChecked()
    {
        // Brick-laying is era 2. The tier says era 1, so it is inconsistent, and its school (also from brick-laying) is not there yet.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            TownsfolkNeedingASchool(content, declaredEra: 1, unlockedBy: "brick_laying");
            content.Write("techs/brick_laying.json", """
                {
                  "id": "brick_laying", "era": 2, "cost": 100, "requires": ["town_charter"],
                  "effects": [ { "unlockBuilding": "school" }, { "unlockTier": "townsfolk" } ]
                }
                """);
        });

        diagnostics.ShouldBeExactly(
            Expected.Error(LintCodes.Evl006, "tiers/townsfolk.json", "/basicNeeds/services/0", "tier 'townsfolk' (era 1) needs the 'school' service"),
            Expected.Error(LintCodes.Evl006, "tiers/townsfolk.json", "/era", "declares era 1, but it is unlocked in era 2 by tech 'brick_laying' (techs/brick_laying.json#/effects/1)"));
    }

    [Fact]
    public void Run_TierWithSeveralSources_IsComparedWithItsEarliestSource()
    {
        // A start tier that an era 2 tech also unlocks (EVL005 reports the two sources) may still declare era 1.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("tiers/townsfolk.json", """
                { "id": "townsfolk", "order": 3, "era": 1, "capacity": 15, "advanceFrom": "villagers",
                  "basicNeeds": { "goods": { "fish": 9 } }, "start": true }
                """);
            content.Write("techs/brick_laying.json", """
                { "id": "brick_laying", "era": 2, "cost": 100, "requires": ["town_charter"], "effects": [ { "unlockTier": "townsfolk" } ] }
                """);
            content.Write("locale/en/extra.json", """
                {
                  "tier.townsfolk.name": "Townsfolk", "tier.townsfolk.desc": "Town.",
                  "tech.brick_laying.name": "Brick-laying", "tech.brick_laying.desc": "Bricks."
                }
                """);
        });

        diagnostics.WithCode(LintCodes.Evl006).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
        diagnostics.WithCode(LintCodes.Evl005).Length.ShouldBe(1);
    }

    [Fact]
    public void Run_TierDeclaringALaterEraThanItsSource_HasNoUpperBound()
    {
        // The rule only stops a tier from being earlier than its source; a tier two eras later is accepted for now.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("eras/enlightenment.json", """
                { "id": "enlightenment", "order": 3, "requires": { "tech": "natural_philosophy", "civic": "humanism" } }
                """);
            content.Write("techs/natural_philosophy.json", """{ "id": "natural_philosophy", "era": 2, "cost": 400, "requires": ["town_charter"], "gateway": true }""");
            content.Write("civics/humanism.json", """{ "id": "humanism", "era": 2, "cost": 400, "requires": ["village_moot"], "gateway": true }""");
            content.Write("tiers/townsfolk.json", """
                { "id": "townsfolk", "order": 3, "era": 3, "capacity": 15, "advanceFrom": "villagers",
                  "basicNeeds": { "goods": { "fish": 9 } }, "start": true }
                """);
            content.Write("locale/en/extra.json", """
                {
                  "era.enlightenment.name": "Enlightenment", "era.enlightenment.desc": "Reason.",
                  "tech.natural_philosophy.name": "Natural philosophy", "tech.natural_philosophy.desc": "Thought.",
                  "civic.humanism.name": "Humanism", "civic.humanism.desc": "People.",
                  "tier.townsfolk.name": "Townsfolk", "tier.townsfolk.desc": "Town."
                }
                """);
        });

        diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    /// <summary>Adds a Townsfolk tier that needs the school service, a school building, and the effect that unlocks the tier.</summary>
    private static void TownsfolkNeedingASchool(TempContent content, int declaredEra, string unlockedBy)
    {
        content.Write("tiers/townsfolk.json", $$"""
            {
              "id": "townsfolk", "order": 3, "era": {{declaredEra}}, "capacity": 15, "advanceFrom": "villagers",
              "basicNeeds": { "goods": { "fish": 9 }, "services": ["school"] }
            }
            """);
        content.Write("buildings/school.json", School());
        content.Write("locale/en/extra.json", """
            {
              "tier.townsfolk.name": "Townsfolk", "tier.townsfolk.desc": "Town.", "building.school.name": "School", "building.school.desc": "Learn.",
              "tech.brick_laying.name": "Brick-laying", "tech.brick_laying.desc": "Bricks."
            }
            """);
        if (unlockedBy == "town_charter")
        {
            content.Replace("techs/town_charter.json", "{ \"unlockFeature\": \"expeditions\" }", "{ \"unlockFeature\": \"expeditions\" },\n    { \"unlockTier\": \"townsfolk\" }");
        }
    }

    [Fact]
    public void Run_BuildingUnlockedByAMonument_IsAsEarlyAsTheMonumentsBuilding()
    {
        // A monument has no era; the building it raises is era 2, so the school it unlocks is not available to an era 1 tier.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("buildings/standing_stones.json", """
                { "id": "standing_stones", "category": "monument", "era": 2, "footprint": [3, 3], "buildDays": 12, "unique": true, "start": true }
                """);
            content.Write("monuments/standing_stones.json", """
                { "id": "standing_stones", "building": "standing_stones", "stages": [ { "timber": 5 } ], "effects": [ { "unlockBuilding": "school" } ] }
                """);
            content.Write("buildings/school.json", School());
            content.Write("locale/en/extra.json", """
                {
                  "building.school.name": "School", "building.school.desc": "Learn.",
                  "building.standing_stones.name": "Standing stones", "building.standing_stones.desc": "Old.",
                  "monument.standing_stones.name": "Standing stones", "monument.standing_stones.desc": "A circle."
                }
                """);
            content.Replace(Villagers, "\"services\": [\"market\"]", "\"services\": [\"market\", \"school\"]");
        });

        diagnostics.WithCode(LintCodes.Evl006).ShouldBeExactly(
            Expected.Error(LintCodes.Evl006, Villagers, "/basicNeeds/services/1", "no building unlocked by era 1 provides it (one is unlocked in era 2)"));
    }

    private static string Tavern(bool start) => $$"""
        {
          "id": "tavern", "category": "service", "era": 1, "footprint": [2, 2], "buildDays": 2,
          "service": { "kind": "tavern", "coverage": 12 }{{(start ? ", \"start\": true" : string.Empty)}}
        }
        """;

    private static string School() => """
        { "id": "school", "category": "service", "era": 2, "footprint": [2, 2], "buildDays": 2, "service": { "kind": "school", "coverage": 12 } }
        """;

    private static string Producer(string id, string recipe) => $$"""
        { "id": "{{id}}", "category": "production", "era": 1, "footprint": [2, 2], "buildDays": 1, "recipe": "{{recipe}}", "start": true }
        """;
}
