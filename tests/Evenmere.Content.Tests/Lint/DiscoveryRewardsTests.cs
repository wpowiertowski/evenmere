using System.Collections.Immutable;
using Evenmere.Content.Lint;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Lint;

/// <summary>
/// The M2-06 discovery rewards in <c>content/rules/core.json</c>: what ruins, shipwrecks and natural wonders grant
/// when an expedition reveals them. The shape is schema-checked (EVL001) and the shipwreck goods must resolve
/// (EVL002), so `evenmere lint` catches both a typo and an unknown good.
/// </summary>
public sealed class DiscoveryRewardsTests
{
    [Fact]
    public void Run_RealContent_Passes()
    {
        ContentLoadResult result = ContentLoader.Load(Path.Combine(TestPaths.RepoRoot, "content"));

        ImmutableArray<ContentDiagnostic> diagnostics = ContentLinter.Run(result);

        diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_MinimalPack_Passes()
    {
        ContentLoadResult result = ContentLoader.Load(TestPaths.MinimalPack);

        ImmutableArray<ContentDiagnostic> diagnostics = ContentLinter.Run(result);

        diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_ShipwreckGoodNobodyDefines_FailsResolvingTheGood()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Replace(
                "rules/core.json",
                "\"marketGating\": true",
                "\"marketGating\": true, \"discoveries\": { \"shipwreck\": { \"goods\": { \"no_such_good\": 8 } } }");
        });

        diagnostics.WithCode(DiagnosticCodes.Evl002).ShouldBeExactly(
            Expected.Error(DiagnosticCodes.Evl002, "rules/core.json", "/discoveries/shipwreck/goods/no_such_good", "no good named 'no_such_good'"));
    }

    [Fact]
    public void Run_UnknownDiscoveryKind_FailsAsAnUnknownProperty()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Replace(
                "rules/core.json",
                "\"marketGating\": true",
                "\"marketGating\": true, \"discoveries\": { \"dragon_hoard\": {} }");
        });

        diagnostics.WithCode(DiagnosticCodes.Evl001).ShouldBeExactly(
            Expected.Error(DiagnosticCodes.Evl001, "rules/core.json", "/discoveries/dragon_hoard", "unknown property 'dragon_hoard'"));
    }
}
