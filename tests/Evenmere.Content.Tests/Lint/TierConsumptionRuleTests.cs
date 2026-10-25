using System.Collections.Immutable;
using Evenmere.Content.Lint;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Lint;

/// <summary>EVL011: a tier with an advanceFrom tier must need strictly more basic goods per resident than that tier.</summary>
public sealed class TierConsumptionRuleTests
{
    private const string Villagers = "tiers/villagers.json";

    [Fact]
    public void Run_RealContent_Passes()
    {
        ContentLoadResult result = ContentLoader.Load(Path.Combine(TestPaths.RepoRoot, "content"));

        ImmutableArray<ContentDiagnostic> diagnostics = ContentLinter.Run(result);

        diagnostics.WithCode(LintCodes.Evl011).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_MinimalPack_Passes()
    {
        ContentLoadResult result = ContentLoader.Load(TestPaths.MinimalPack);

        ImmutableArray<ContentDiagnostic> diagnostics = ContentLinter.Run(result);

        diagnostics.WithCode(LintCodes.Evl011).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_HigherTierEatsLess_FailsNamingBothTiersAndTotals()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Replace(Villagers, "\"goods\": { \"fish\": 4, \"wood\": 4 }", "\"goods\": { \"fish\": 3 }");
        });

        diagnostics.ShouldBeExactly(
            Expected.Error(LintCodes.Evl011, Villagers, "/basicNeeds/goods", "tier 'villagers' needs 3"));
        ContentDiagnostic diagnostic = diagnostics.Single();
        diagnostic.Message.ShouldContain("settlers");
        diagnostic.Message.ShouldContain("7");
    }

    [Fact]
    public void Run_HigherTierEatsTheSame_Fails()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Replace(Villagers, "\"goods\": { \"fish\": 4, \"wood\": 4 }", "\"goods\": { \"fish\": 4, \"wood\": 3 }");
        });

        diagnostics.WithCode(LintCodes.Evl011).ShouldHaveSingleMatch(LintCodes.Evl011, Villagers, "/basicNeeds/goods");
    }

    [Fact]
    public void Run_LowestTier_HasNoAdvanceFromAndPasses()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Replace("tiers/settlers.json", "\"goods\": { \"fish\": 4, \"wood\": 3 }", "\"goods\": { \"fish\": 1 }");
        });

        // Settlers has no advanceFrom; only villagers is checked and it still eats more than 1.
        diagnostics.WithCode(LintCodes.Evl011).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }
}
