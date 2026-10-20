using System.Collections.Immutable;
using Evenmere.Content.Lint;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Lint;

/// <summary>EVL007 beyond the committed fixtures: the key prefix of every content type, bad values, and several roots.</summary>
public sealed class LocaleRuleTests
{
    /// <summary>Gets (locale file, key prefix, item file) for an item of each type that the minimal pack defines.</summary>
    public static TheoryData<string, string, string> ExistingItems => new()
    {
        { "goods", "good.fish", "goods/fish.json" },
        { "buildings", "building.sawpit", "buildings/sawpit.json" },
        { "recipes", "recipe.saw_timber", "recipes/saw_timber.json" },
        { "tiers", "tier.villagers", "tiers/villagers.json" },
        { "techs", "tech.net_weaving", "techs/net_weaving.json" },
        { "civics", "civic.village_moot", "civics/village_moot.json" },
        { "traditions", "tradition.common_hearth", "traditions/common_hearth.json" },
        { "eras", "era.township", "eras/township.json" },
        { "terrains", "terrain.river", "terrains/river.json" },
    };

    /// <summary>Gets (item file, item JSON, key prefix) for the types the minimal pack does not define.</summary>
    public static TheoryData<string, string, string> NewItems => new()
    {
        { "festivals/harvest_fair.json", """{ "id": "harvest_fair", "cost": { "timber": 1 }, "days": 2, "culturePerDay": 1, "start": true }""", "festival.harvest_fair" },
        { "greatworks/writing.json", """{ "id": "writing", "source": "discovery", "cultureByEra": [1] }""", "greatwork.writing" },
        { "monuments/sawpit_monument.json", """{ "id": "sawpit_monument", "building": "sawpit", "stages": [ { "timber": 1 } ] }""", "monument.sawpit_monument" },
        {
            "happenings/gift.json",
            """{ "id": "gift", "trigger": { "season": "spring" }, "chancePerMille": 5, "cooldownDays": 5, "options": [ { } ] }""",
            "happening.gift"
        },
    };

    [Theory]
    [MemberData(nameof(ExistingItems))]
    public void Run_ItemWithoutADisplayName_IsAnErrorNamingTheTypeAndId(string localeFile, string prefix, string itemFile)
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            string path = $"locale/en/{localeFile}.json";
            content.Write(path, JsonEdit.Remove(content.Read(path), "/" + prefix + ".name"));
        });

        diagnostics.ShouldBeExactly(Expected.Error(LintCodes.Evl007, itemFile, string.Empty, $"\"{prefix}.name\""));
    }

    [Theory]
    [MemberData(nameof(ExistingItems))]
    public void Run_ItemWithoutADescription_IsOnlyAWarning(string localeFile, string prefix, string itemFile)
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            string path = $"locale/en/{localeFile}.json";
            content.Write(path, JsonEdit.Remove(content.Read(path), "/" + prefix + ".desc"));
        });

        diagnostics.ShouldBeExactly(Expected.Warning(LintCodes.Evl007, itemFile, string.Empty, $"\"{prefix}.desc\""));
    }

    [Theory]
    [MemberData(nameof(NewItems))]
    public void Run_NewItemWithNoText_NeedsANameAndShouldHaveADescription(string file, string json, string prefix)
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content => content.Write(file, json));

        diagnostics.WithCode(LintCodes.Evl007).ShouldBeExactly(
            Expected.Warning(LintCodes.Evl007, file, string.Empty, $"\"{prefix}.desc\""),
            Expected.Error(LintCodes.Evl007, file, string.Empty, $"\"{prefix}.name\""));
    }

    [Theory]
    [MemberData(nameof(NewItems))]
    public void Run_NewItemWithText_IsClean(string file, string json, string prefix)
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write(file, json);
            content.Write("locale/en/new_item.json", $$"""{ "{{prefix}}.name": "Name", "{{prefix}}.desc": "Description." }""");
        });

        diagnostics.WithCode(LintCodes.Evl007).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Theory]
    [InlineData("\"\"", "is empty")]
    [InlineData("\"   \"", "is empty")]
    [InlineData("[\"Sawpit\", \"Sawmill\"]", "list of names")]
    public void Run_NameThatIsEmptyOrAList_IsAnError(string value, string complaint)
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
            content.Replace("locale/en/buildings.json", "\"building.sawpit.name\": \"Sawpit\"", $"\"building.sawpit.name\": {value}"));

        diagnostics.ShouldBeExactly(Expected.Error(LintCodes.Evl007, "buildings/sawpit.json", string.Empty, complaint));
    }

    [Fact]
    public void Run_KeyInAnyLocaleFile_Counts()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            string path = "locale/en/buildings.json";
            content.Write(path, JsonEdit.Remove(JsonEdit.Remove(content.Read(path), "/building.sawpit.name"), "/building.sawpit.desc"));
            content.Write("locale/en/anything_you_like.json", """{ "building.sawpit.name": "Sawpit", "building.sawpit.desc": "Saws." }""");
        });

        diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_OtherLanguages_AreNotChecked()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
            content.Write("locale/de/buildings.json", """{ "building.sawpit.name": "Saegewerk" }"""));

        diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_ItemFromALaterRoot_NeedsItsTextInAnyRoot()
    {
        using var pack = TempContent.Empty();
        pack.Write("goods/salt.json", """{ "id": "salt", "category": "raw", "era": 1 }""");
        using var packWithText = TempContent.Empty();
        packWithText.Write("goods/salt.json", """{ "id": "salt", "category": "raw", "era": 1 }""");
        packWithText.Write("locale/en/salt.json", """{ "good.salt.name": "Salt", "good.salt.desc": "Cures fish." }""");

        ImmutableArray<ContentDiagnostic> without = ContentLinter.Run(ContentLoader.Load([TestPaths.MinimalPack, pack.Root]));
        ImmutableArray<ContentDiagnostic> with = ContentLinter.Run(ContentLoader.Load([TestPaths.MinimalPack, packWithText.Root]));

        without.WithCode(LintCodes.Evl007).ShouldBeExactly(
            Expected.Warning(LintCodes.Evl007, "goods/salt.json", string.Empty, "good.salt.desc"),
            Expected.Error(LintCodes.Evl007, "goods/salt.json", string.Empty, "good.salt.name"));
        with.WithCode(LintCodes.Evl007).ShouldBeEmpty(DiagnosticAssertions.Dump(with));
    }

    [Fact]
    public void Run_TextOverriddenByALaterRoot_StillCounts()
    {
        using var pack = TempContent.Empty();
        pack.Write("locale/en/override.json", """{ "building.sawpit.name": "Sawmill", "building.sawpit.desc": "A better sawpit." }""");

        ImmutableArray<ContentDiagnostic> diagnostics = ContentLinter.Run(ContentLoader.Load([TestPaths.MinimalPack, pack.Root]));

        diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }
}
