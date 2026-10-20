using System.Text;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests;

/// <summary>Structural and shape errors are EVL001 diagnostics, never exceptions.</summary>
public sealed class StructureTests
{
    [Fact]
    public void Load_InvalidJson_IsEvl001WithLineAndColumn()
    {
        using var content = TempContent.FromMinimal();
        content.Write("goods/fish.json", "{\n  \"id\": \"fish\",\n  \"category\": \"consumer\",\n  \"era\": ,\n}");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", "goods/fish.json", string.Empty);

        diagnostic.Message.ShouldContain("line 4");
    }

    [Fact]
    public void Load_DuplicatePropertyName_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Write("goods/fish.json", """{ "id": "fish", "category": "consumer", "era": 1, "era": 2 }""");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", "goods/fish.json", string.Empty);

        diagnostic.Message.ShouldContain("era");
    }

    [Theory]
    [InlineData("{ \"id\": \"fish\", \"category\": \"consumer\", \"era\": 1, }")]
    [InlineData("{ // a comment\n \"id\": \"fish\", \"category\": \"consumer\", \"era\": 1 }")]
    public void Load_TrailingCommaOrComment_IsEvl001(string json)
    {
        using var content = TempContent.FromMinimal();
        content.Write("goods/fish.json", json);

        content.Load().ShouldFailWith("EVL001", "goods/fish.json", string.Empty);
    }

    [Fact]
    public void Load_Utf8ByteOrderMark_IsRejectedNotStripped()
    {
        using var content = TempContent.FromMinimal();
        byte[] json = Encoding.UTF8.GetBytes("""{ "id": "fish", "category": "consumer", "era": 1 }""");
        content.WriteBytes("goods/fish.json", [0xEF, 0xBB, 0xBF, .. json]);

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", "goods/fish.json", string.Empty);

        diagnostic.Message.ShouldContain("byte order mark");
    }

    [Fact]
    public void Load_InvalidUtf8_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.WriteBytes("goods/fish.json", [.. Encoding.UTF8.GetBytes("{ \"id\": \"fish\", \"x\": \""), 0xFF, 0xFE, .. Encoding.UTF8.GetBytes("\" }")]);

        content.Load().ShouldFailWith("EVL001", "goods/fish.json", string.Empty);
    }

    [Fact]
    public void Load_RootThatIsNotAnObject_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Write("goods/fish.json", "[1, 2]");

        content.Load().ShouldFailWith("EVL001", "goods/fish.json", string.Empty);
    }

    [Fact]
    public void Load_EmptyFile_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Write("goods/fish.json", string.Empty);

        content.Load().ShouldFailWith("EVL001", "goods/fish.json", string.Empty);
    }

    [Fact]
    public void Load_UnknownProperty_IsEvl001AtThatProperty()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("goods/fish.json", "\"era\": 1", "\"era\": 1, \"colour\": \"blue\"");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", "goods/fish.json", "/colour");

        diagnostic.Message.ShouldContain("colour");
    }

    [Fact]
    public void Load_UnknownNestedProperty_IsEvl001WithTheFullPointer()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("buildings/storehouse.json", "\"reach\": 20", "\"reach\": 20, \"speed\": 3");

        content.Load().ShouldFailWith("EVL001", "buildings/storehouse.json", "/storage/speed");
    }

    [Fact]
    public void Load_DisplayTextInAContentFile_IsRejectedAsAnUnknownProperty()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("goods/fish.json", "\"era\": 1", "\"era\": 1, \"name\": \"Fish\"");

        content.Load().ShouldFailWith("EVL001", "goods/fish.json", "/name");
    }

    [Fact]
    public void Load_MissingRequiredProperty_IsEvl001OnTheObject()
    {
        using var content = TempContent.FromMinimal();
        content.Write("goods/fish.json", """{ "id": "fish", "era": 1 }""");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", "goods/fish.json", string.Empty);

        diagnostic.Message.ShouldContain("category");
    }

    [Fact]
    public void Load_MissingRequiredNestedProperty_IsEvl001OnTheNestedObject()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("buildings/storehouse.json", "\"carts\": 2", "\"cart\": 2");

        ContentLoadResult result = content.Load();

        result.Db.ShouldBeNull();
        result.Diagnostics.ShouldHaveSingleMatch("EVL001", "buildings/storehouse.json", "/storage");
        result.Diagnostics.ShouldHaveSingleMatch("EVL001", "buildings/storehouse.json", "/storage/cart");
    }

    [Fact]
    public void Load_WrongValueType_IsEvl001AtTheValue()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("goods/fish.json", "\"era\": 1", "\"era\": \"one\"");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", "goods/fish.json", "/era");

        diagnostic.Message.ShouldContain("integer");
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    public void Load_EraThatIsNotAPositiveInteger_IsEvl001(string era)
    {
        using var content = TempContent.FromMinimal();
        content.Replace("goods/fish.json", "\"era\": 1", $"\"era\": {era}");

        content.Load().ShouldFailWith("EVL001", "goods/fish.json", "/era");
    }

    [Fact]
    public void Load_InvalidEnumValue_ListsTheAllowedNames()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("goods/fish.json", "\"consumer\"", "\"snack\"");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", "goods/fish.json", "/category");

        diagnostic.Message.ShouldContain("snack");
        diagnostic.Message.ShouldContain("raw, intermediate, construction, consumer");
    }

    [Fact]
    public void Load_IdThatDiffersFromTheFileName_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("goods/fish.json", "\"id\": \"fish\"", "\"id\": \"trout\"");

        content.Load().ShouldFailWith("EVL001", "goods/fish.json", "/id");
    }

    [Fact]
    public void Load_MissingId_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Write("goods/fish.json", """{ "category": "consumer", "era": 1 }""");

        content.Load().ShouldFailWith("EVL001", "goods/fish.json", string.Empty);
    }

    [Theory]
    [InlineData("Zebra")]
    [InlineData("zebra-hook")]
    [InlineData("zebra__hook")]
    [InlineData("_zebra")]
    [InlineData("zebra_")]
    [InlineData("1zebra")]
    public void Load_FileNameThatIsNotSnakeCase_IsEvl001(string stem)
    {
        using var content = TempContent.FromMinimal();
        content.Write($"goods/{stem}.json", $$"""{ "id": "{{stem}}", "category": "raw", "era": 1 }""");

        ContentLoadResult result = content.Load();

        result.Db.ShouldBeNull();
        result.Diagnostics.ShouldHaveSingleMatch("EVL001", $"goods/{stem}.json", string.Empty);
    }

    [Fact]
    public void Load_GoodNamedCoin_IsRejectedBecauseCoinIsReservedInCosts()
    {
        using var content = TempContent.FromMinimal();
        content.Write("goods/coin.json", """{ "id": "coin", "category": "raw", "era": 1 }""");

        content.Load().ShouldFailWith("EVL001", "goods/coin.json", "/id");
    }

    [Fact]
    public void Load_BrokenFilesAreEachReportedAndTheRestStillValidated()
    {
        using var content = TempContent.FromMinimal();
        content.Write("goods/fish.json", "{");
        content.Replace("buildings/sawpit.json", "\"recipe\": \"saw_timber\"", "\"recipe\": \"nope\"");
        content.Replace("tiers/villagers.json", "\"capacity\": 10", "\"capacity\": 0");

        ContentLoadResult result = content.Load();

        result.Db.ShouldBeNull();
        result.Diagnostics.Length.ShouldBe(3, DiagnosticAssertions.Dump(result.Diagnostics));
        result.Diagnostics.ShouldHaveSingleMatch("EVL001", "goods/fish.json", string.Empty);
        result.Diagnostics.ShouldHaveSingleMatch("EVL002", "buildings/sawpit.json", "/recipe");
        result.Diagnostics.ShouldHaveSingleMatch("EVL001", "tiers/villagers.json", "/capacity");
    }

    [Fact]
    public void Load_DuplicateEraOrder_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("eras/township.json", "\"order\": 2", "\"order\": 1");

        ContentLoadResult result = content.Load();

        result.Db.ShouldBeNull();
        ContentDiagnostic diagnostic = result.Diagnostics.ShouldHaveSingleMatch("EVL001", "eras/township.json", "/order");
        diagnostic.Message.ShouldContain("founding");
    }

    [Fact]
    public void Load_DuplicateTierOrder_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("tiers/villagers.json", "\"order\": 2", "\"order\": 1");

        content.Load().ShouldFailWith("EVL001", "tiers/villagers.json", "/order");
    }

    [Fact]
    public void Load_FootprintThatIsNotTwoIntegers_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("buildings/sawpit.json", "[2, 2]", "[2, 2, 2]");

        content.Load().ShouldFailWith("EVL001", "buildings/sawpit.json", "/footprint");
    }

    [Fact]
    public void Load_ZeroFootprintSide_IsEvl001AtThatElement()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("buildings/sawpit.json", "[2, 2]", "[2, 0]");

        content.Load().ShouldFailWith("EVL001", "buildings/sawpit.json", "/footprint/1");
    }

    [Fact]
    public void Load_FootprintYieldWithoutFertility_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Write("buildings/sawpit.json", """
            {
              "id": "sawpit", "category": "production", "era": 1, "footprint": [2, 2], "buildDays": 1,
              "placement": { "areaYield": { "kind": "footprint_fertility" } }
            }
            """);

        content.Load().ShouldFailWith("EVL001", "buildings/sawpit.json", "/placement/areaYield");
    }

    [Fact]
    public void Load_RadiusYieldWithoutTerrain_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Write("buildings/sawpit.json", """
            {
              "id": "sawpit", "category": "production", "era": 1, "footprint": [2, 2], "buildDays": 1,
              "placement": { "areaYield": { "kind": "radius", "terrain": [], "radius": 4, "fullAt": 24 } }
            }
            """);

        content.Load().ShouldFailWith("EVL001", "buildings/sawpit.json", "/placement/areaYield");
    }

    [Fact]
    public void Load_BridgeWithoutRoadCost_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Write("buildings/sawpit.json", """
            { "id": "sawpit", "category": "infrastructure", "era": 1, "footprint": [1, 1], "buildDays": 0, "bridgeSpan": 3 }
            """);

        content.Load().ShouldFailWith("EVL001", "buildings/sawpit.json", "/bridgeSpan");
    }

    [Fact]
    public void Load_BuildingThatUpgradesItself_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Write("buildings/sawpit.json", """
            { "id": "sawpit", "category": "production", "era": 1, "footprint": [2, 2], "buildDays": 1, "upgradeOf": "sawpit" }
            """);

        content.Load().ShouldFailWith("EVL001", "buildings/sawpit.json", "/upgradeOf");
    }

    [Fact]
    public void Load_TechRequiringItself_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("techs/town_charter.json", "[\"net_weaving\"]", "[\"town_charter\"]");

        content.Load().ShouldFailWith("EVL001", "techs/town_charter.json", "/requires");
    }

    [Fact]
    public void Load_CartLoadLargerThanTheOutputBuffer_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("rules/core.json", "\"cartLoad\": 2", "\"cartLoad\": 5");

        content.Load().ShouldFailWith("EVL001", "rules/core.json", "/cartLoad");
    }

    [Fact]
    public void Load_MissingRulesFile_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Delete("rules/core.json");

        content.Load().ShouldFailWith("EVL001", "rules/core.json", string.Empty);
    }

    [Fact]
    public void Load_RulesMissingATunable_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("rules/core.json", "\"marketGating\": true,", string.Empty);

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", "rules/core.json", string.Empty);

        diagnostic.Message.ShouldContain("marketGating");
    }

    [Fact]
    public void Load_RulesWithRoadCosts_AreRejectedBecauseRoadsAreBuildings()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("rules/core.json", "\"marketGating\": true,", "\"marketGating\": true, \"roads\": { \"dirt\": 1000 },");

        content.Load().ShouldFailWith("EVL001", "rules/core.json", "/roads");
    }

    [Fact]
    public void Load_MissingContentRoot_IsEvl001()
    {
        string missing = Path.Combine(Path.GetTempPath(), "evenmere-content-tests", "does-not-exist-" + Guid.NewGuid().ToString("N"));

        ContentLoadResult result = ContentLoader.Load(missing);

        result.Db.ShouldBeNull();
        result.Diagnostics.ShouldContain(d => d.Code == "EVL001" && d.Message.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_NoRoots_IsEvl001()
    {
        ContentLoadResult result = ContentLoader.Load(Array.Empty<string>());

        result.Db.ShouldBeNull();
        result.Diagnostics.Length.ShouldBe(1);
        result.Diagnostics[0].Code.ShouldBe("EVL001");
    }

    [Fact]
    public void Load_UnknownContentDirectory_IsAWarningAndStillLoads()
    {
        using var content = TempContent.FromMinimal();
        content.Write("building/typo.json", "{}");

        ContentLoadResult result = content.Load();

        result.Db.ShouldNotBeNull();
        result.HasErrors.ShouldBeFalse();
        ContentDiagnostic warning = result.Diagnostics.ShouldHaveSingleMatch("EVL001", "building/typo.json", string.Empty);
        warning.Severity.ShouldBe(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Load_FileInASubdirectoryOfAContentDirectory_IsAWarningAndIgnored()
    {
        using var content = TempContent.FromMinimal();
        content.Write("goods/extra/stone.json", """{ "id": "stone", "category": "raw", "era": 1 }""");

        ContentLoadResult result = content.Load();

        ContentDb db = result.Db.ShouldNotBeNull();
        db.Goods.TryGet("stone", out _).ShouldBeFalse();
        result.Diagnostics.ShouldHaveSingleMatch("EVL001", "goods/extra/stone.json", string.Empty).Severity.ShouldBe(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Load_OtherRulesFile_IsAWarningAndIgnored()
    {
        using var content = TempContent.FromMinimal();
        content.Write("rules/extra.json", "{}");

        ContentLoadResult result = content.Load();

        result.Db.ShouldNotBeNull();
        result.Diagnostics.ShouldHaveSingleMatch("EVL001", "rules/extra.json", string.Empty).Severity.ShouldBe(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Load_FilesThatAreNotContent_AreIgnoredWithoutDiagnostics()
    {
        using var content = TempContent.FromMinimal();
        content.Write("README.md", "# Content");
        content.Write("schemas/good.schema.json", "{}");
        content.Write("maps/starter/map.json", "{}");
        content.Write("goods/notes.txt", "not json");
        content.Write("goods/.DS_Store", "binary");

        content.Load().ShouldLoadCleanly();
    }

    [Fact]
    public void Load_DiagnosticsAreSortedByCodeFilePointerAndMessage()
    {
        using var content = TempContent.FromMinimal();
        content.Write("recipes/saw_timber.json", """
            { "id": "saw_timber", "inputs": { "zeta": 1, "alpha": 1 }, "outputs": { "timber": 1 }, "cycleSeconds": 20 }
            """);
        content.Replace("goods/fish.json", "\"era\": 1", "\"era\": \"x\"");

        ContentLoadResult result = content.Load();

        result.Diagnostics.Select(static d => (d.Code, d.File, d.JsonPointer)).ShouldBe(
        [
            ("EVL001", "goods/fish.json", "/era"),
            ("EVL002", "recipes/saw_timber.json", "/inputs/alpha"),
            ("EVL002", "recipes/saw_timber.json", "/inputs/zeta"),
        ]);
    }

    [Fact]
    public void Load_KeyWithSlashOrTilde_IsEscapedInThePointer()
    {
        using var content = TempContent.FromMinimal();
        content.Write("recipes/saw_timber.json", """
            { "id": "saw_timber", "inputs": { "a/b": 1, "c~d": 1 }, "outputs": { "timber": 1 }, "cycleSeconds": 20 }
            """);

        ContentLoadResult result = content.Load();

        result.Diagnostics.ShouldHaveSingleMatch("EVL002", "recipes/saw_timber.json", "/inputs/a~1b");
        result.Diagnostics.ShouldHaveSingleMatch("EVL002", "recipes/saw_timber.json", "/inputs/c~0d");
    }

    [Fact]
    public void GetDbOrThrow_WhenLoadingFailed_ThrowsWithTheDiagnostics()
    {
        using var content = TempContent.FromMinimal();
        content.Delete("rules/core.json");

        ContentLoadException exception = Should.Throw<ContentLoadException>(() => content.Load().GetDbOrThrow());

        exception.Diagnostics.ShouldNotBeEmpty();
        exception.Message.ShouldContain("rules/core.json");
    }

    [Fact]
    public void GetDbOrThrow_WhenLoadingSucceeded_ReturnsTheDatabase()
    {
        ContentLoader.Load(TestPaths.MinimalPack).GetDbOrThrow().Goods.Count.ShouldBe(3);
    }

    [Fact]
    public void Load_RawContent_IsAvailableEvenWhenLoadingFailed()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("buildings/sawpit.json", "\"recipe\": \"saw_timber\"", "\"recipe\": \"nope\"");

        ContentLoadResult result = content.Load();

        result.Db.ShouldBeNull();
        result.Raw.Items.Length.ShouldBe(25 + 1);
        RawItemSnapshot(result, ContentType.Buildings, "sawpit").Json.ShouldNotBeNull();
        result.Raw.ContentHash.ShouldNotBe(0UL);
    }

    private static Loading.RawItem RawItemSnapshot(ContentLoadResult result, ContentType type, string id) =>
        result.Raw.OfType(type).Single(item => item.Id == id);
}
