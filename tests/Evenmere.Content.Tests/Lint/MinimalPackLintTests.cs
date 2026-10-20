using System.Collections.Immutable;
using Evenmere.Content.Lint;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Lint;

public sealed class MinimalPackLintTests
{
    [Fact]
    public void Run_MinimalPack_HasNoDiagnostics()
    {
        ContentLoadResult result = ContentLoader.Load(TestPaths.MinimalPack);

        ImmutableArray<ContentDiagnostic> diagnostics = ContentLinter.Run(result);

        diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_KitchenSink_SchemasAcceptEverythingTheLoaderAccepts()
    {
        using TempContent content = KitchenSink.Create();
        ContentLoadResult result = content.Load();
        result.ShouldLoadCleanly();

        ImmutableArray<ContentDiagnostic> schema = ContentLinter.ValidateSchemas(result.Raw);

        schema.ShouldBeEmpty(DiagnosticAssertions.Dump(schema));
    }
}
