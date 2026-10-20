using System.Collections.Immutable;
using System.Text.Json;
using Evenmere.Content.Definitions;
using Evenmere.Core.Time;

namespace Evenmere.Content.Tests.Lint;

/// <summary>
/// The code owns every closed vocabulary (<see cref="ContentNames"/>) and the schemas repeat them for editors and for EVL001.
/// These tests compare the two, so adding a stat, a category or an effect kind in code fails here until the schema follows.
/// </summary>
public sealed class SchemaSyncTests
{
    /// <summary>Each enum definition in <c>common.schema.json</c> and the code's list it must equal, in declaration order.</summary>
    private static readonly (string Definition, ImmutableArray<string> Names)[] _enumDefinitions =
    [
        ("goodCategory", ContentNames.NamesOf<GoodCategory>()),
        ("buildingCategory", ContentNames.NamesOf<BuildingCategory>()),
        ("serviceKind", ContentNames.NamesOf<ServiceKind>()),
        ("depositKind", ContentNames.NamesOf<DepositKind>()),
        ("cropKind", ContentNames.NamesOf<CropKind>()),
        ("terrainAccess", ContentNames.NamesOf<TerrainAccess>()),
        ("traditionSlotType", ContentNames.NamesOf<TraditionSlotType>()),
        ("stat", ContentNames.NamesOf<Stat>()),
        ("discoveryKind", ContentNames.NamesOf<DiscoveryKind>()),
        ("discoveryTarget", ContentNames.DiscoveryTargetKinds),
        ("areaYieldKind", ContentNames.NamesOf<AreaYieldKind>()),
        ("greatWorkSource", ContentNames.NamesOf<GreatWorkSource>()),
        ("season", ContentNames.NamesOf<Season>()),
    ];

    public static TheoryData<string> EnumDefinitionNames => [.. _enumDefinitions.Select(static e => e.Definition)];

    [Theory]
    [MemberData(nameof(EnumDefinitionNames))]
    public void Schema_EnumEqualsTheCodesVocabulary(string definition)
    {
        ImmutableArray<string> expected = _enumDefinitions.Single(e => e.Definition == definition).Names;

        ImmutableArray<string> actual = SchemaFiles.Strings(SchemaFiles.Definition(definition).GetProperty("enum"));

        actual.ShouldBe(expected, $"$defs/{definition} in common.schema.json has drifted from ContentNames/the enum in code.");
    }

    [Fact]
    public void Schema_FeaturesAreTheCodesFeaturesWithGreatWorksWrittenAsATypedString()
    {
        ImmutableArray<string> plain = SchemaFiles.Strings(SchemaFiles.Definition("featureKind").GetProperty("enum"));
        ImmutableArray<string> expected = ContentNames.NamesOf<FeatureKind>();

        // great_works is the one feature with an argument: great_works:<type>, matched by its own definition.
        ImmutableArray<string> withGreatWorks = [.. plain, ContentNames.NameOf(FeatureKind.GreatWorks)];
        withGreatWorks.ShouldBe(expected);
        string pattern = SchemaFiles.Definition("greatWorksFeature").GetProperty("pattern").GetString()!;
        pattern.ShouldStartWith("^" + ContentNames.NameOf(FeatureKind.GreatWorks) + ":");
    }

    [Fact]
    public void Schema_EffectKeysAreTheCodesEffectKinds()
    {
        ImmutableArray<string> actual = SchemaFiles.Keys(SchemaFiles.Definition("effect").GetProperty("properties"));

        actual.ShouldBe(ContentNames.EffectKinds);
    }

    [Fact]
    public void Schema_ConditionKeysAreTheCodesConditionKinds()
    {
        ImmutableArray<string> actual = SchemaFiles.Keys(SchemaFiles.Definition("condition").GetProperty("properties"));

        actual.ShouldBe(ContentNames.ConditionKinds);
    }

    [Fact]
    public void Schema_ModifierTargetKeysAreTheCodesTargetKinds()
    {
        ImmutableArray<string> actual = SchemaFiles.Keys(SchemaFiles.Definition("modifierTarget").GetProperty("properties"));

        actual.ShouldBe(ContentNames.ModifierTargetKinds);
    }

    [Fact]
    public void Schema_SiteConditionKeysAreTheCodesSiteConditionKinds()
    {
        ImmutableArray<string> actual = SchemaFiles.Keys(SchemaFiles.Definition("siteCondition").GetProperty("properties"));

        actual.ShouldBe(ContentNames.SiteConditionKinds);
    }

    [Fact]
    public void Schema_GrantPointsKeysAreTheKindsOfPoint()
    {
        JsonElement grantPoints = SchemaFiles.Definition("effect").GetProperty("properties").GetProperty("grantPoints");

        SchemaFiles.Keys(grantPoints.GetProperty("properties")).ShouldBe(ContentNames.NamesOf<PointKind>());
    }

    [Fact]
    public void Schema_SeasonsInTheRulesAreTheSeasons()
    {
        JsonElement rules = SchemaFiles.Read(SchemaFiles.FileOf(ContentType.Rules));
        JsonElement curve = rules.GetProperty("properties").GetProperty("seasons").GetProperty("patternProperties").EnumerateObject().Single().Value;

        SchemaFiles.Keys(curve.GetProperty("properties")).ShouldBe(ContentNames.NamesOf<Season>());
        SchemaFiles.Strings(curve.GetProperty("required")).ShouldBe(ContentNames.NamesOf<Season>());
    }

    [Fact]
    public void Schema_ConstantsThatNameAnEnumMemberStillNameOne()
    {
        // greatworks.schema.json and buildings.schema.json branch on a member name with "const".
        JsonElement source = SchemaFiles.Read(SchemaFiles.FileOf(ContentType.GreatWorks)).GetProperty("if").GetProperty("properties").GetProperty("source");
        ContentNames.NamesOf<GreatWorkSource>().ShouldContain(source.GetProperty("const").GetString()!);

        JsonElement kind = SchemaFiles.Read(SchemaFiles.FileOf(ContentType.Buildings))
            .GetProperty("properties").GetProperty("placement").GetProperty("properties").GetProperty("areaYield")
            .GetProperty("if").GetProperty("properties").GetProperty("kind");
        ContentNames.NamesOf<AreaYieldKind>().ShouldContain(kind.GetProperty("const").GetString()!);
    }

    [Fact]
    public void Schema_NoEnumHidesOutsideTheCheckedDefinitions()
    {
        // Every "enum" in any schema must be one of the definitions compared above, so a new vocabulary cannot be added
        // to a schema without a sync test for it.
        string[] allowed = [.. _enumDefinitions.Select(static e => "/$defs/" + e.Definition + "/enum"), "/$defs/featureKind/enum"];
        var found = new List<string>();
        foreach (string fileName in SchemaFiles.Names)
        {
            foreach ((string path, JsonElement node) in SchemaFiles.Walk(SchemaFiles.Read(fileName)))
            {
                if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty("enum", out _))
                {
                    found.Add(fileName + path + "/enum");
                }
            }
        }

        found.Order(StringComparer.Ordinal).ShouldBe([.. allowed.Select(static a => SchemaFiles.Common + a).Order(StringComparer.Ordinal)]);
    }
}
