using System.Collections.Immutable;
using Evenmere.Content.Lint;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Lint;

/// <summary>EVL008 beyond the committed fixtures: the era a gateway belongs to, civic gateways and longer chains of eras.</summary>
public sealed class EraRuleTests
{
    private const string Township = "eras/township.json";

    [Fact]
    public void Run_GatewayThatBelongsToTheLaterEra_IsAnError()
    {
        // Town Charter (the tech gateway into era 2) is itself an era 2 tech here, so completing it cannot start era 2.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
            content.Replace("techs/town_charter.json", "\"era\": 1", "\"era\": 2"));

        diagnostics.ShouldBeExactly(
            Expected.Error(LintCodes.Evl008, Township, "/requires/tech", "belongs to era 2"));
        diagnostics[0].Message.ShouldContain("must belong to the previous era, era 1");
    }

    [Fact]
    public void Run_CivicGatewayThatIsNotFlagged_IsAnErrorAtTheCivic()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
            content.Replace("civics/village_moot.json", "  },\n  \"gateway\": true\n}", "  }\n}"));

        diagnostics.ShouldBeExactly(
            Expected.Error(LintCodes.Evl008, Township, "/requires/civic", "civic 'village_moot', which is not a gateway"));
    }

    [Fact]
    public void Run_GatewayThatIsNeitherFlaggedNorInTheRightEra_ReportsBothProblems()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Replace("techs/town_charter.json", "  },\n  \"gateway\": true\n}", "  }\n}");
            content.Replace("techs/town_charter.json", "\"era\": 1", "\"era\": 2");
        });

        // Both at one spot; they sort by message, and "belongs" comes before "is not".
        diagnostics.ShouldBeExactly(
            Expected.Error(LintCodes.Evl008, Township, "/requires/tech", "belongs to era 2"),
            Expected.Error(LintCodes.Evl008, Township, "/requires/tech", "not a gateway"));
    }

    [Fact]
    public void Run_CivicThatRequiresALaterEraCivic_IsAnErrorAtTheRequirement()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("civics/guild_rules.json", """{ "id": "guild_rules", "era": 1, "cost": 100, "requires": ["village_moot", "craft_pact"] }""");
            content.Write("civics/craft_pact.json", """{ "id": "craft_pact", "era": 2, "cost": 200, "requires": ["village_moot"] }""");
            content.Write("locale/en/extra.json", """
                {
                  "civic.guild_rules.name": "Guild rules", "civic.guild_rules.desc": "Rules.",
                  "civic.craft_pact.name": "Craft pact", "civic.craft_pact.desc": "A pact."
                }
                """);
        });

        diagnostics.ShouldBeExactly(
            Expected.Error(LintCodes.Evl008, "civics/guild_rules.json", "/requires/1", "civic 'guild_rules' (era 1) requires 'craft_pact', which belongs to the later era 2"));
    }

    [Fact]
    public void Run_NodeThatRequiresAnEarlierOrSameEraNode_IsFine()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("techs/brick_laying.json", """{ "id": "brick_laying", "era": 2, "cost": 200, "requires": ["town_charter", "net_weaving"] }""");
            content.Write("techs/glazing.json", """{ "id": "glazing", "era": 2, "cost": 250, "requires": ["brick_laying"] }""");
            content.Write("locale/en/extra.json", """
                {
                  "tech.brick_laying.name": "Brick-laying", "tech.brick_laying.desc": "Bricks.",
                  "tech.glazing.name": "Glazing", "tech.glazing.desc": "Glass."
                }
                """);
        });

        diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_FirstEraThatRequiresGateways_IsAnError_AndDoesNotAlsoLeaveThemUnreferenced()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
            content.Write("eras/founding.json", """{ "id": "founding", "order": 1, "requires": { "tech": "net_weaving", "civic": "village_moot" } }"""));

        diagnostics.ShouldBeExactly(
            Expected.Error(LintCodes.Evl008, "eras/founding.json", "/requires", "first era"));
    }

    [Fact]
    public void Run_TwoErasThatNameTheSameGateway_FailsForTheLaterOne()
    {
        // era 3 names era 1's gateways again, but must name era 2's.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
            content.Write("eras/enlightenment.json", """
                { "id": "enlightenment", "order": 3, "requires": { "tech": "town_charter", "civic": "village_moot" } }
                """));
        diagnostics = [.. diagnostics.Where(static d => d.Code == LintCodes.Evl008)];

        diagnostics.ShouldBeExactly(
            Expected.Error(LintCodes.Evl008, "eras/enlightenment.json", "/requires/civic", "belongs to era 1; the gateway into era 'enlightenment' must belong to the previous era, era 2"),
            Expected.Error(LintCodes.Evl008, "eras/enlightenment.json", "/requires/tech", "belongs to era 1; the gateway into era 'enlightenment' must belong to the previous era, era 2"));
    }

    [Fact]
    public void Run_GatewayOfTheLastEra_IsUnreferencedBecauseNoEraFollows()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            AddThirdEra(content);
            content.Write("techs/steam_power.json", """{ "id": "steam_power", "era": 3, "cost": 900, "requires": ["natural_philosophy"], "gateway": true }""");
            content.Write("locale/en/steam.json", """{ "tech.steam_power.name": "Steam power", "tech.steam_power.desc": "Steam." }""");
        });

        diagnostics.ShouldBeExactly(
            Expected.Error(LintCodes.Evl008, "techs/steam_power.json", "/gateway", "no era requires it"));
    }

    [Fact]
    public void Run_ErasWithAGapInTheirNumbers_AreComparedByOrder()
    {
        // Eras are paired by order, not by being numbered one apart; the tech and civic eras name the order they belong to.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("eras/township.json", """{ "id": "township", "order": 5, "requires": { "tech": "town_charter", "civic": "village_moot" } }""");
        });

        diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_ThreeErasInChain_PairEachWithTheOneBefore()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(AddThirdEra);

        diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    private static void AddThirdEra(TempContent content)
    {
        content.Write("eras/enlightenment.json", """
            { "id": "enlightenment", "order": 3, "requires": { "tech": "natural_philosophy", "civic": "humanism" } }
            """);
        content.Write("techs/natural_philosophy.json", """{ "id": "natural_philosophy", "era": 2, "cost": 400, "requires": ["town_charter"], "gateway": true }""");
        content.Write("civics/humanism.json", """{ "id": "humanism", "era": 2, "cost": 400, "requires": ["village_moot"], "gateway": true }""");
        content.Write("locale/en/third_era.json", """
            {
              "era.enlightenment.name": "Enlightenment", "era.enlightenment.desc": "Reason.",
              "tech.natural_philosophy.name": "Natural philosophy", "tech.natural_philosophy.desc": "Thought.",
              "civic.humanism.name": "Humanism", "civic.humanism.desc": "People."
            }
            """);
    }
}
