using System.Collections.Immutable;
using System.Text;
using Evenmere.Content.Locale;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests;

public sealed class LocaleTests
{
    [Fact]
    public void Load_MergesEveryFileOfTheLanguage()
    {
        using var content = TempContent.Empty();
        content.Write("locale/en/a.json", """{ "good.wood.name": "Wood" }""");
        content.Write("locale/en/b.json", """{ "good.fish.name": "Fish", "good.fish.desc": "Fresh." }""");
        content.Write("locale/de/a.json", """{ "good.wood.name": "Holz" }""");

        LocaleLoadResult result = LocaleTable.Load(content.Root, "en");

        result.Diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(result.Diagnostics));
        result.Table.Language.ShouldBe("en");
        result.Table.Count.ShouldBe(3);
        result.Table.Keys.ShouldBe(["good.fish.desc", "good.fish.name", "good.wood.name"]);
        result.Table.Get("good.wood.name").ShouldBe("Wood");
        result.Table.Get("good.fish.desc").ShouldBe("Fresh.");
        LocaleTable.Load(content.Root, "de").Table.Get("good.wood.name").ShouldBe("Holz");
    }

    [Fact]
    public void Get_MissingKey_ReturnsTheKeyItself()
    {
        LocaleTable table = LocaleTable.Load(TestPaths.MinimalPack, "en").Table;

        table.Get("good.nothing.name").ShouldBe("good.nothing.name");
        table.TryGet("good.nothing.name", out string text).ShouldBeFalse();
        text.ShouldBeEmpty();
        table.ContainsKey("good.nothing.name").ShouldBeFalse();
        table.ContainsKey("good.wood.name").ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("../en")]
    [InlineData("en/x")]
    [InlineData("e n")]
    public void Load_LanguageThatIsNotACode_Throws(string language)
    {
        Should.Throw<ArgumentException>(() => LocaleTable.Load(TestPaths.MinimalPack, language));
    }

    [Fact]
    public void Load_MissingLanguageFolder_GivesAnEmptyTableWithoutDiagnostics()
    {
        LocaleLoadResult result = LocaleTable.Load(TestPaths.MinimalPack, "fr");

        result.Diagnostics.ShouldBeEmpty();
        result.Table.Count.ShouldBe(0);
    }

    [Fact]
    public void Load_DuplicateKeyAcrossFiles_IsEvl001OnTheSecondFile()
    {
        using var content = TempContent.Empty();
        content.Write("locale/en/a.json", """{ "good.wood.name": "Wood" }""");
        content.Write("locale/en/b.json", """{ "good.wood.name": "Logs" }""");

        LocaleLoadResult result = LocaleTable.Load(content.Root, "en");

        result.HasErrors.ShouldBeTrue();
        ContentDiagnostic diagnostic = result.Diagnostics.ShouldHaveSingleMatch("EVL001", "locale/en/b.json", "/good.wood.name");
        diagnostic.Message.ShouldContain("locale/en/a.json");
        result.Table.Get("good.wood.name").ShouldBe("Wood", "The first definition is kept.");
    }

    [Fact]
    public void Load_DuplicateKeyInOneFile_IsEvl001()
    {
        using var content = TempContent.Empty();
        content.Write("locale/en/a.json", """{ "good.wood.name": "Wood", "good.wood.name": "Logs" }""");

        LocaleLoadResult result = LocaleTable.Load(content.Root, "en");

        result.HasErrors.ShouldBeTrue();
        result.Diagnostics.Length.ShouldBe(1);
        result.Diagnostics[0].File.ShouldBe("locale/en/a.json");
    }

    [Fact]
    public void Load_LaterRootOverridesAnEarlierKeyWithoutADiagnostic()
    {
        using var baseGame = TempContent.Empty();
        using var pack = TempContent.Empty();
        baseGame.Write("locale/en/a.json", """{ "good.wood.name": "Wood", "good.fish.name": "Fish" }""");
        pack.Write("locale/en/pack.json", """{ "good.wood.name": "Logs" }""");

        LocaleLoadResult result = LocaleTable.Load([baseGame.Root, pack.Root], "en");

        result.Diagnostics.ShouldBeEmpty();
        result.Table.Get("good.wood.name").ShouldBe("Logs");
        result.Table.Get("good.fish.name").ShouldBe("Fish");
    }

    [Fact]
    public void Load_NameTables_AreListsRatherThanStrings()
    {
        using var content = TempContent.Empty();
        content.Write("locale/en/names.json", """
            {
              "names.greatwork.writing": ["The Ballad of Seven Coves", "The Long Winter"],
              "names.greatwork.art": ["A Single Name"],
              "good.wood.name": "Wood"
            }
            """);

        LocaleTable table = LocaleTable.Load(content.Root, "en").Table;

        table.TryGetList("names.greatwork.writing", out ImmutableArray<string> names).ShouldBeTrue();
        names.ShouldBe(["The Ballad of Seven Coves", "The Long Winter"]);
        table.TryGetList("names.greatwork.art", out ImmutableArray<string> single).ShouldBeTrue();
        single.ShouldBe(["A Single Name"]);
        table.TryGetList("good.wood.name", out _).ShouldBeFalse();
        table.TryGet("names.greatwork.writing", out _).ShouldBeFalse();
        table.ContainsKey("names.greatwork.writing").ShouldBeTrue();
    }

    [Fact]
    public void Load_ByteOrderMark_IsRejected()
    {
        using var content = TempContent.Empty();
        content.WriteBytes("locale/en/a.json", [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("""{ "a": "b" }""")]);

        LocaleLoadResult result = LocaleTable.Load(content.Root, "en");

        result.Diagnostics.ShouldHaveSingleMatch("EVL001", "locale/en/a.json", string.Empty);
        result.Table.Count.ShouldBe(0);
    }

    [Fact]
    public void Load_InvalidUtf8_IsEvl001()
    {
        using var content = TempContent.Empty();
        content.WriteBytes("locale/en/a.json", [.. Encoding.UTF8.GetBytes("{ \"a\": \""), 0xFF, 0xFE, .. Encoding.UTF8.GetBytes("\" }")]);

        LocaleTable.Load(content.Root, "en").Diagnostics.ShouldHaveSingleMatch("EVL001", "locale/en/a.json", string.Empty);
    }

    [Theory]
    [InlineData("{ \"a\": 5 }", "/a")]
    [InlineData("{ \"a\": null }", "/a")]
    [InlineData("{ \"a\": { \"b\": \"c\" } }", "/a")]
    [InlineData("{ \"a\": [\"ok\", 5] }", "/a/1")]
    [InlineData("{ \"\": \"empty key\" }", "/")]
    public void Load_ValueThatIsNotTextOrAListOfText_IsEvl001(string json, string jsonPointer)
    {
        using var content = TempContent.Empty();
        content.Write("locale/en/a.json", json);

        LocaleTable.Load(content.Root, "en").Diagnostics.ShouldHaveSingleMatch("EVL001", "locale/en/a.json", jsonPointer);
    }

    [Theory]
    [InlineData("[\"a\"]")]
    [InlineData("{ \"a\": \"b\", }")]
    [InlineData("not json")]
    public void Load_FileThatIsNotAJsonObject_IsEvl001(string json)
    {
        using var content = TempContent.Empty();
        content.Write("locale/en/a.json", json);

        LocaleTable.Load(content.Root, "en").Diagnostics.ShouldHaveSingleMatch("EVL001", "locale/en/a.json", string.Empty);
    }

    [Fact]
    public void Load_FilesThatAreNotJson_AreIgnored()
    {
        using var content = TempContent.Empty();
        content.Write("locale/en/a.json", """{ "a": "b" }""");
        content.Write("locale/en/notes.txt", "free text");
        content.Write("locale/en/.hidden.json", "not json");

        LocaleLoadResult result = LocaleTable.Load(content.Root, "en");

        result.Diagnostics.ShouldBeEmpty();
        result.Table.Count.ShouldBe(1);
    }
}
