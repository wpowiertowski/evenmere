using System.Collections.Immutable;
using Evenmere.Content.Lint;
using Evenmere.Content.Loading;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Lint;

/// <summary>How <see cref="ContentLinter"/> combines the loader, the schemas, the locale files and the rules.</summary>
public sealed class LinterPipelineTests
{
    [Fact]
    public void Run_MinimalPack_IsCleanOnBothOverloads()
    {
        ContentLoadResult result = ContentLoader.Load(TestPaths.MinimalPack);

        ContentLinter.Run(result).ShouldBeEmpty();
        ContentLinter.Run(result.Db!, result.Raw).ShouldBeEmpty();
    }

    [Fact]
    public void Run_NoDatabase_ReportsTheLoadersDiagnosticsAndNoRuleDiagnostics()
    {
        // The pack has a loader error (an unknown property) AND rule problems (no sawpit name, a good nobody uses).
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Replace("buildings/sawpit.json", "\"buildDays\": 1,", "\"buildDays\": 1,\n  \"colour\": \"red\",");
            content.Write("goods/ghost.json", """{ "id": "ghost", "category": "raw", "era": 1 }""");
        });

        diagnostics.ShouldBeExactly(Expected.Error(DiagnosticCodes.Evl001, "buildings/sawpit.json", "/colour", "unknown property 'colour'"));
    }

    [Fact]
    public void Run_NoDatabase_StillReportsLocaleFileProblemsAndSchemaFindings()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Replace("buildings/sawpit.json", "\"recipe\": \"saw_timber\"", "\"recipe\": \"saw_planks\"");
            content.Write("locale/en/tail.json", """{ "good.fish.name": "Fish again" }""");

            // The loader accepts a terrain listed twice; only the schema objects. Loading failed elsewhere, but this file is not
            // one the loader rejected, so it is still checked.
            content.Write("buildings/camp.json", """
                { "id": "camp", "category": "infrastructure", "era": 1, "footprint": [1, 1], "buildDays": 0, "placement": { "terrain": ["plains", "plains"] } }
                """);
        });

        diagnostics.ShouldContain(d => d.Code == DiagnosticCodes.Evl002 && d.File == "buildings/sawpit.json");
        diagnostics.ShouldContain(d => d.Code == DiagnosticCodes.Evl001 && d.File == "locale/en/tail.json" && d.JsonPointer == "/good.fish.name");
        diagnostics.ShouldContain(d => d.Code == DiagnosticCodes.Evl001 && d.File == "buildings/camp.json" && d.JsonPointer == "/placement/terrain");
        diagnostics.Where(static d => d.Code is LintCodes.Evl003 or LintCodes.Evl004 or LintCodes.Evl005 or LintCodes.Evl006 or LintCodes.Evl007 or LintCodes.Evl008)
            .ShouldBeEmpty("rules need the database, and loading failed");
    }

    [Fact]
    public void Run_SpotThatTheLoaderAndTheSchemaBothReport_AppearsOnceWithTheLoadersMessage()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("buildings/sawpit.json", "\"timber\": 3", "\"timber\": 3.1234");
        ContentLoadResult result = content.Load();

        ImmutableArray<ContentDiagnostic> diagnostics = ContentLinter.Run(result);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].ShouldBe(result.Diagnostics.Single());
    }

    [Fact]
    public void Run_LoaderWarnings_AreKept()
    {
        using var content = TempContent.FromMinimal();
        content.Write("notes/readme.json", "{}");
        ContentLoadResult result = content.Load();

        ImmutableArray<ContentDiagnostic> diagnostics = ContentLinter.Run(result);

        result.Db.ShouldNotBeNull();
        diagnostics.ShouldBeExactly(Expected.Warning(DiagnosticCodes.Evl001, "notes/readme.json", string.Empty, "not a content directory"));
    }

    [Fact]
    public void Run_DatabaseAndRawOverload_LeavesOutTheLoadersOwnDiagnostics()
    {
        using var content = TempContent.FromMinimal();
        content.Write("notes/readme.json", "{}");
        content.Replace("locale/en/goods.json", "  \"good.fish.desc\": \"Fresh fish from the shore.\",\n", string.Empty);
        ContentLoadResult result = content.Load();

        ImmutableArray<ContentDiagnostic> viaResult = ContentLinter.Run(result);
        ImmutableArray<ContentDiagnostic> viaParts = ContentLinter.Run(result.Db!, result.Raw);

        // Sorted by code first: the loader's EVL001 warning, then the EVL007 warning.
        viaResult.Select(static d => d.File).ShouldBe(["notes/readme.json", "goods/fish.json"]);
        viaParts.Select(static d => d.File).ShouldBe(["goods/fish.json"]);
    }

    [Fact]
    public void Run_OverloadsAgreeOnEveryFixtureThatLoads()
    {
        foreach (string name in LintFixtures.Names)
        {
            ContentLoadResult result = ContentLoader.Load(LintFixtures.PathOf(name));
            if (result.Db is null)
            {
                continue;
            }

            ContentLinter.Run(result.Db, result.Raw).ShouldBe(ContentLinter.Run(result), name);
        }
    }

    [Fact]
    public void Run_Output_IsSortedAndFreeOfDuplicates()
    {
        // A pack with problems in every rule, so the sort has real work to do.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("goods/ghost.json", """{ "id": "ghost", "category": "raw", "era": 1 }""");
            content.Write("techs/ring_a.json", """{ "id": "ring_a", "era": 1, "cost": 1, "requires": ["ring_b"] }""");
            content.Write("techs/ring_b.json", """{ "id": "ring_b", "era": 1, "cost": 1, "requires": ["ring_a"] }""");
            content.Write("buildings/windmill.json", """{ "id": "windmill", "category": "production", "era": 1, "footprint": [2, 2], "buildDays": 1 }""");
            content.Replace("locale/en/goods.json", "  \"good.fish.name\": \"Fish\",\n", string.Empty);
        });

        diagnostics.Length.ShouldBeGreaterThan(5);
        for (int i = 1; i < diagnostics.Length; i++)
        {
            ContentDiagnostic.Compare(diagnostics[i - 1], diagnostics[i]).ShouldBeLessThan(0, DiagnosticAssertions.Dump(diagnostics));
        }

        diagnostics.Select(static d => d.Code).Distinct().ShouldBe([LintCodes.Evl003, LintCodes.Evl004, LintCodes.Evl005, LintCodes.Evl007]);
    }

    [Fact]
    public void Run_SameContentTwice_GivesTheSameDiagnosticsInTheSameOrder()
    {
        foreach (string name in LintFixtures.Names)
        {
            string first = DiagnosticAssertions.Dump(LintFixtures.Lint(name));
            string second = DiagnosticAssertions.Dump(LintFixtures.Lint(name));

            second.ShouldBe(first, name);
        }
    }

    [Fact]
    public void Run_ContentCopiedToAnotherLocationWithCrlfLineEndings_GivesTheSameDiagnostics()
    {
        string expected = DiagnosticAssertions.Dump(LintFixtures.Lint("EVL006-good-too-late"));
        using var copy = TempContent.Empty();
        foreach (string file in Directory.EnumerateFiles(LintFixtures.PathOf("EVL006-good-too-late"), "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(LintFixtures.PathOf("EVL006-good-too-late"), file).Replace(Path.DirectorySeparatorChar, '/');
            copy.Write(relative, File.ReadAllText(file));
        }

        copy.ConvertToCrlf();

        DiagnosticAssertions.Dump(ContentLinter.Run(copy.Load())).ShouldBe(expected);
    }

    [Fact]
    public void ValidateSchemas_IsSortedAndOnlyEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("buildings/sawpit.json", "\"buildDays\": 1,", "\"buildDays\": 1,\n  \"zzz\": 1,\n  \"aaa\": 2,");
        content.Replace("tiers/villagers.json", "\"order\": 2", "\"order\": 0");

        ImmutableArray<ContentDiagnostic> schema = ContentLinter.ValidateSchemas(content.Load().Raw);

        schema.Length.ShouldBe(3, DiagnosticAssertions.Dump(schema));
        schema.ShouldAllBe(static d => d.Code == DiagnosticCodes.Evl001);
        schema.Select(static d => d.File + "#" + d.JsonPointer).ShouldBe(["buildings/sawpit.json#/aaa", "buildings/sawpit.json#/zzz", "tiers/villagers.json#/order"]);
    }

    [Fact]
    public void Run_NullArguments_Throw()
    {
        Should.Throw<ArgumentNullException>(() => ContentLinter.Run(null!));
        Should.Throw<ArgumentNullException>(() => ContentLinter.Run(null!, null!));
        Should.Throw<ArgumentNullException>(() => ContentLinter.ValidateSchemas(null!));
    }

    [Fact]
    public void Run_RulesFile_IsValidatedLikeAnyOtherFile()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("rules/core.json", "\"cartTicksPerTile\": 2", "\"cartTicksPerTile\": 0");

        ImmutableArray<ContentDiagnostic> diagnostics = ContentLinter.Run(content.Load());

        diagnostics.ShouldBeExactly(Expected.Error(DiagnosticCodes.Evl001, "rules/core.json", "/cartTicksPerTile", "out of range"));
    }

    [Fact]
    public void ValidateSchemas_MinimalPack_ChecksAFileOfEveryTypeItHoldsAndFindsNothing()
    {
        ContentLoadResult result = ContentLoader.Load(TestPaths.MinimalPack);

        // 11 item types plus the rules file; great works and monuments are covered by the kitchen sink.
        result.Raw.Items.Select(static i => i.Type).Distinct().Count().ShouldBe(12);
        ContentLinter.ValidateSchemas(result.Raw).ShouldBeEmpty();
    }
}
