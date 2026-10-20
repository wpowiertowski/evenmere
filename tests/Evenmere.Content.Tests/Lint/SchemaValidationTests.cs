using System.Collections.Immutable;
using Evenmere.Content.Lint;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Lint;

/// <summary>
/// EVL001 by schema. Each case takes a content file that is valid for both the loader and the schema, breaks one value,
/// and checks that the loader and the schema each point at the same spot, so the two never disagree about where a
/// problem is (and a spot both report is shown once, with the loader's message).
/// </summary>
public sealed class SchemaValidationTests
{
    private const string Remove = "<remove>";

    /// <summary>One valid file per content type, using every field, with names that the minimal pack defines.</summary>
    private static readonly SortedDictionary<string, (string File, string Json)> _bases = new(StringComparer.Ordinal)
    {
        ["goods"] = ("goods/test_good.json", """{ "id": "test_good", "category": "raw", "era": 1, "tags": ["a", "b"] }"""),
        ["buildings"] = ("buildings/test_hut.json", """
            {
              "id": "test_hut", "category": "production", "era": 1, "footprint": [2, 2],
              "placement": {
                "terrain": ["plains", "forest"], "roadAccess": true, "coastline": false, "riverside": false,
                "fertility": { "crop": "grain", "minPercent": 10 },
                "areaYield": { "kind": "footprint_fertility" }
              },
              "cost": { "timber": 1, "coin": 2.5 }, "buildDays": 1,
              "workforce": { "tier": "settlers", "count": 2 }, "recipe": "cut_wood",
              "storage": { "reach": 5, "carts": 1 }, "service": { "kind": "market", "coverage": 5 },
              "outputs": { "knowledge": 1, "culture": 0, "coin": 0.5 },
              "slots": [ { "type": "any", "count": 1 } ],
              "upgradeOf": "storehouse", "fuel": { "good": "wood", "perDay": 1 }, "upkeepCoin": 1,
              "attractiveness": 1, "lodging": 1, "roadCost": 1000, "bridgeSpan": 2,
              "siteBonus": [ { "when": { "terrain": ["plains"] }, "stat": "productivity", "percent": 10 } ],
              "aura": { "stat": "capacity", "percent": 5, "target": { "tag": "house" }, "coverage": 3 },
              "unique": false, "start": true, "tags": ["a"]
            }
            """),
        ["recipes"] = ("recipes/test_recipe.json", """
            { "id": "test_recipe", "inputs": { "wood": 1 }, "outputs": { "timber": 2 }, "cycleSeconds": 10, "start": true }
            """),
        ["tiers"] = ("tiers/test_tier.json", """
            {
              "id": "test_tier", "order": 9, "era": 1, "capacity": 5, "advanceFrom": "villagers",
              "upgradeCost": { "timber": 1, "coin": 2 },
              "basicNeeds": { "goods": { "fish": 1 }, "services": ["market"] },
              "comfortNeeds": { "wood": 1 }, "outputs": { "knowledge": 1, "coin": 1 }, "start": true
            }
            """),
        ["techs"] = ("techs/test_tech.json", """
            {
              "id": "test_tech", "era": 1, "cost": 10, "requires": ["net_weaving"],
              "effects": [ { "unlockBuilding": "sawpit" } ],
              "inspiration": { "when": { "season": "autumn" } }, "gateway": false,
              "repeatable": { "costGrowthPercent": 10 }
            }
            """),
        ["civics"] = ("civics/test_civic.json", """
            { "id": "test_civic", "era": 1, "cost": 10, "requires": ["village_moot"], "effects": [ { "traditionSlot": "wild" } ] }
            """),
        ["traditions"] = ("traditions/test_tradition.json", """
            {
              "id": "test_tradition", "slot": "craft", "tags": ["x"], "style": "test_style", "start": true,
              "effects": [ { "modifier": { "stat": "productivity", "percent": 5 } } ]
            }
            """),
        ["festivals"] = ("festivals/test_festival.json", """
            {
              "id": "test_festival", "seasons": ["spring", "summer"], "cost": { "timber": 1 }, "days": 3, "culturePerDay": 1.5,
              "seasonBonus": { "season": "autumn", "percent": 50 },
              "effects": [ { "modifier": { "stat": "visitors", "percent": 10 } } ], "start": true
            }
            """),
        ["greatworks"] = ("greatworks/test_work.json", """
            { "id": "test_work", "source": "creativity", "baseCreativity": 10, "growthPercent": 20, "cultureByEra": [1, 2], "attractiveness": 1 }
            """),
        ["monuments"] = ("monuments/test_monument.json", """
            {
              "id": "test_monument", "building": "sawpit",
              "stages": [ { "timber": 1 }, { "wood": 2, "coin": 3 } ], "effects": [ { "grantPoints": { "culture": 5 } } ]
            }
            """),
        ["happenings"] = ("happenings/test_event.json", """
            {
              "id": "test_event", "trigger": { "season": "spring" }, "chancePerMille": 10, "cooldownDays": 5,
              "options": [ { "effects": [ { "grantPoints": { "knowledge": 1 } } ], "durationDays": 2 } ], "defaultOption": 0
            }
            """),
        ["eras"] = ("eras/test_era.json", """{ "id": "test_era", "order": 5, "requires": { "tech": "net_weaving", "civic": "village_moot" } }"""),
        ["terrains"] = ("terrains/test_terrain.json", """{ "id": "test_terrain", "buildable": true, "blocksRoads": false, "access": "highlands" }"""),
        ["rules"] = ("rules/core.json", """
            {
              "cartTicksPerTile": 2, "outputBuffer": 4, "cartLoad": 2, "declineGraceDays": 10, "advanceStreakDays": 3,
              "inspirationDiscountPerMille": 400, "marketGating": true,
              "seasons": { "seasonal": { "spring": 1000, "summer": 1200, "autumn": 1100, "winter": 700 } }
            }
            """),
    };

    /// <summary>(content type, pointer to change, new value or Remove, pointer both the loader and the schema should report).</summary>
    private static readonly (string Type, string Change, string Value, string Pointer)[] _defects =
    [
        // goods
        ("goods", "/category", "\"bogus\"", "/category"),
        ("goods", "/era", "0", "/era"),
        ("goods", "/id", "\"coin\"", "/id"),
        ("goods", "/id", "\"Test_Good\"", "/id"),
        ("goods", "/tags/0", "\"Bad Tag\"", "/tags/0"),
        ("goods", "/colour", "\"red\"", "/colour"),

        // buildings: required fields, ranges and enums
        ("buildings", "/footprint", Remove, string.Empty),
        ("buildings", "/footprint/0", "0", "/footprint/0"),
        ("buildings", "/footprint", "[2]", "/footprint"),
        ("buildings", "/footprint", "[2, 2, 2]", "/footprint"),
        ("buildings", "/category", "\"bogus\"", "/category"),
        ("buildings", "/era", "0", "/era"),
        ("buildings", "/era", "65", "/era"),
        ("buildings", "/era", "1.5", "/era"),
        ("buildings", "/era", "\"1\"", "/era"),
        ("buildings", "/buildDays", "-1", "/buildDays"),
        ("buildings", "/buildDays", Remove, string.Empty),
        ("buildings", "/colour", "\"red\"", "/colour"),
        ("buildings", "/id", "\"Test_Hut\"", "/id"),
        ("buildings", "/recipe", "5", "/recipe"),
        ("buildings", "/unique", "\"yes\"", "/unique"),
        ("buildings", "/tags/0", "\"Bad Tag\"", "/tags/0"),
        ("buildings", "/attractiveness", "2000", "/attractiveness"),
        ("buildings", "/lodging", "-1", "/lodging"),
        ("buildings", "/roadCost", "0", "/roadCost"),
        ("buildings", "/bridgeSpan", "65", "/bridgeSpan"),
        ("buildings", "/upkeepCoin", "-1", "/upkeepCoin"),

        // buildings: quantities and costs
        ("buildings", "/cost/timber", "1.2345", "/cost/timber"),
        ("buildings", "/cost/timber", "0", "/cost/timber"),
        ("buildings", "/cost/timber", "-1", "/cost/timber"),
        ("buildings", "/cost/timber", "\"1\"", "/cost/timber"),
        ("buildings", "/cost/Timber", "1", "/cost/Timber"),
        ("buildings", "/cost/a~1b", "1", "/cost/a~1b"),
        ("buildings", "/outputs/knowledge", "-1", "/outputs/knowledge"),
        ("buildings", "/outputs/bogus", "1", "/outputs/bogus"),
        ("buildings", "/fuel/perDay", "0", "/fuel/perDay"),

        // buildings: nested objects
        ("buildings", "/placement/colour", "1", "/placement/colour"),
        ("buildings", "/placement/terrain/0", "\"Plains\"", "/placement/terrain/0"),
        ("buildings", "/placement/deposit", "\"gold\"", "/placement/deposit"),
        ("buildings", "/placement/fertility/minPercent", "0", "/placement/fertility/minPercent"),
        ("buildings", "/placement/fertility/crop", "\"bogus\"", "/placement/fertility/crop"),
        ("buildings", "/placement/areaYield/kind", "\"diagonal\"", "/placement/areaYield/kind"),
        ("buildings", "/placement/areaYield", """{ "kind": "radius", "terrain": ["plains"], "radius": 3 }""", "/placement/areaYield"),
        ("buildings", "/placement/areaYield", """{ "kind": "footprint_fertility", "radius": 3 }""", "/placement/areaYield/radius"),
        ("buildings", "/workforce/count", "0", "/workforce/count"),
        ("buildings", "/workforce/tier", Remove, "/workforce"),
        ("buildings", "/storage/reach", "0", "/storage/reach"),
        ("buildings", "/service/kind", "\"bogus\"", "/service/kind"),
        ("buildings", "/service/coverage", "1001", "/service/coverage"),
        ("buildings", "/slots/0/count", Remove, "/slots/0"),

        // buildings: siteBonus and aura
        ("buildings", "/siteBonus/0/stat", "\"bogus\"", "/siteBonus/0/stat"),
        ("buildings", "/siteBonus/0/percent", "20000", "/siteBonus/0/percent"),
        ("buildings", "/siteBonus/0/when", Remove, "/siteBonus/0"),
        ("buildings", "/siteBonus/0/when", """{ "terrain": ["plains"], "near": { "tag": "a", "radius": 2 } }""", "/siteBonus/0/when"),
        ("buildings", "/siteBonus/0/when", """{ "near": { "radius": 2 } }""", "/siteBonus/0/when/near"),
        ("buildings", "/siteBonus/0/when", """{ "near": { "tag": "a" } }""", "/siteBonus/0/when/near"),
        ("buildings", "/siteBonus/0/when", """{ "terrain": [] }""", "/siteBonus/0/when/terrain"),
        ("buildings", "/aura/target", Remove, "/aura"),
        ("buildings", "/aura/target", """{ "bogus": "x" }""", "/aura/target/bogus"),
        ("buildings", "/aura/coverage", "0", "/aura/coverage"),

        // techs: costs, requirements and gateway
        ("techs", "/cost", "1.5", "/cost"),
        ("techs", "/cost", "0", "/cost"),
        ("techs", "/cost", "\"10\"", "/cost"),
        ("techs", "/cost", "2147483648", "/cost"),
        ("techs", "/cost", Remove, string.Empty),
        ("techs", "/requires", "\"net_weaving\"", "/requires"),
        ("techs", "/requires/0", "\"Bad\"", "/requires/0"),
        ("techs", "/gateway", "\"yes\"", "/gateway"),
        ("techs", "/repeatable", "{ \"costGrowthPercent\": 0 }", "/repeatable/costGrowthPercent"),
        ("techs", "/repeatable", "{}", "/repeatable"),

        // effects
        ("techs", "/effects/0", "\"unlockBuilding\"", "/effects/0"),
        ("techs", "/effects/0", "{}", "/effects/0"),
        ("techs", "/effects/0", """{ "unlockBuildin": "x" }""", "/effects/0/unlockBuildin"),
        ("techs", "/effects/0", """{ "unlockBuilding": "sawpit", "unlockTier": "settlers" }""", "/effects/0"),
        ("techs", "/effects/0", """{ "unlockBuilding": 5 }""", "/effects/0/unlockBuilding"),
        ("techs", "/effects/0", """{ "unlockTerrainAccess": "bogus" }""", "/effects/0/unlockTerrainAccess"),
        ("techs", "/effects/0", """{ "unlockFeature": "fire" }""", "/effects/0/unlockFeature"),
        ("techs", "/effects/0", """{ "unlockFeature": "great_works" }""", "/effects/0/unlockFeature"),
        ("techs", "/effects/0", """{ "traditionSlot": "bogus" }""", "/effects/0/traditionSlot"),
        ("techs", "/effects/0", """{ "revealCoastline": false }""", "/effects/0/revealCoastline"),
        ("techs", "/effects/0", """{ "grantGoods": {} }""", "/effects/0/grantGoods"),
        ("techs", "/effects/0", """{ "grantPoints": {} }""", "/effects/0/grantPoints"),
        ("techs", "/effects/0", """{ "grantPoints": { "fame": 1 } }""", "/effects/0/grantPoints/fame"),
        ("techs", "/effects/0", """{ "requestFestival": { "festival": "harvest", "discountPercent": 0 } }""", "/effects/0/requestFestival/discountPercent"),
        ("techs", "/effects/0", """{ "modifier": { "stat": "bogus", "percent": 5 } }""", "/effects/0/modifier/stat"),
        ("techs", "/effects/0", """{ "modifier": { "stat": "productivity", "percent": 99999 } }""", "/effects/0/modifier/percent"),
        ("techs", "/effects/0", """{ "modifier": { "stat": "productivity" } }""", "/effects/0/modifier"),
        ("techs", "/effects/0", """{ "modifier": { "stat": "productivity", "percent": 5, "target": { "colour": "x" } } }""", "/effects/0/modifier/target/colour"),
        ("techs", "/effects/0", """{ "modifier": { "stat": "productivity", "percent": 5, "target": { "building": "sawpit", "tag": "x" } } }""", "/effects/0/modifier/target"),
        ("techs", "/effects/0", """{ "modifier": { "stat": "productivity", "percent": 5, "target": { "discovery": "stone_deposit" } } }""", "/effects/0/modifier/target/discovery"),

        // conditions
        ("techs", "/inspiration", "{}", "/inspiration"),
        ("techs", "/inspiration/when", """{ "season": "fall" }""", "/inspiration/when/season"),
        ("techs", "/inspiration/when", """{ "season": "spring", "era": "township" }""", "/inspiration/when"),
        ("techs", "/inspiration/when", """{ "count": { "atLeast": 0 } }""", "/inspiration/when/count/atLeast"),
        ("techs", "/inspiration/when", """{ "count": { "building": "sawpit" } }""", "/inspiration/when/count"),
        ("techs", "/inspiration/when", """{ "count": { "atLeast": 1 } }""", "/inspiration/when/count"),
        ("techs", "/inspiration/when", """{ "all": [] }""", "/inspiration/when/all"),
        ("techs", "/inspiration/when", """{ "all": {} }""", "/inspiration/when/all"),
        ("techs", "/inspiration/when", """{ "not": [] }""", "/inspiration/when/not"),
        ("techs", "/inspiration/when", """{ "any": [ { "season": "spring" }, { "bogus": 1 } ] }""", "/inspiration/when/any/1/bogus"),
        ("techs", "/inspiration/when", """{ "discovered": { "atLeast": 1 } }""", "/inspiration/when/discovered"),
        ("techs", "/inspiration/when", """{ "discovered": { "kind": "bogus", "atLeast": 1 } }""", "/inspiration/when/discovered/kind"),
        ("techs", "/inspiration/when", """{ "produced": { "good": "timber", "atLeast": 0 } }""", "/inspiration/when/produced/atLeast"),
        ("techs", "/inspiration/when", """{ "population": { "atLeast": 0 } }""", "/inspiration/when/population/atLeast"),
        ("techs", "/inspiration/when", """{ "greatWorks": { "displayed": 1, "atLeast": 1 } }""", "/inspiration/when/greatWorks/displayed"),
        ("techs", "/inspiration/when", """{ "researched": 5 }""", "/inspiration/when/researched"),

        // other types
        ("civics", "/era", "0", "/era"),
        ("tiers", "/capacity", "0", "/capacity"),
        ("tiers", "/order", "100", "/order"),
        ("tiers", "/era", Remove, string.Empty),
        ("tiers", "/basicNeeds/colour", "1", "/basicNeeds/colour"),
        ("tiers", "/basicNeeds/services/0", "\"bogus\"", "/basicNeeds/services/0"),
        ("tiers", "/comfortNeeds/wood", "-1", "/comfortNeeds/wood"),
        ("tiers", "/upgradeCost/timber", "1.0001", "/upgradeCost/timber"),
        ("tiers", "/outputs/culture", "-2", "/outputs/culture"),
        ("recipes", "/outputs", "{}", "/outputs"),
        ("recipes", "/outputs", Remove, string.Empty),
        ("recipes", "/cycleSeconds", "0", "/cycleSeconds"),
        ("recipes", "/inputs/timber", "0", "/inputs/timber"),
        ("recipes", "/start", "1", "/start"),
        ("traditions", "/slot", "\"bogus\"", "/slot"),
        ("traditions", "/slot", Remove, string.Empty),
        ("traditions", "/style", "\"Bad Style\"", "/style"),
        ("festivals", "/cost", "{}", "/cost"),
        ("festivals", "/cost", Remove, string.Empty),
        ("festivals", "/days", "0", "/days"),
        ("festivals", "/culturePerDay", "-1", "/culturePerDay"),
        ("festivals", "/seasons/0", "\"fall\"", "/seasons/0"),
        ("festivals", "/seasonBonus/percent", "-200", "/seasonBonus/percent"),
        ("greatworks", "/baseCreativity", Remove, string.Empty),
        ("greatworks", "/growthPercent", "2000", "/growthPercent"),
        ("greatworks", "/cultureByEra", "[]", "/cultureByEra"),
        ("greatworks", "/source", "\"discovery\"", "/baseCreativity"),
        ("monuments", "/stages", "[]", "/stages"),
        ("monuments", "/stages", Remove, string.Empty),
        ("monuments", "/stages/0", "{}", "/stages/0"),
        ("monuments", "/building", Remove, string.Empty),
        ("happenings", "/trigger", Remove, string.Empty),
        ("happenings", "/chancePerMille", "0", "/chancePerMille"),
        ("happenings", "/options", "[]", "/options"),
        ("happenings", "/options/0/durationDays", "-1", "/options/0/durationDays"),
        ("happenings", "/options/0/colour", "1", "/options/0/colour"),
        ("eras", "/order", "0", "/order"),
        ("eras", "/requires/civic", Remove, "/requires"),
        ("eras", "/requires/extra", "1", "/requires/extra"),
        ("terrains", "/access", "\"bogus\"", "/access"),
        ("terrains", "/buildable", "\"yes\"", "/buildable"),
        ("terrains", "/buildable", Remove, string.Empty),
        ("rules", "/cartTicksPerTile", "0", "/cartTicksPerTile"),
        ("rules", "/outputBuffer", "0", "/outputBuffer"),
        ("rules", "/marketGating", Remove, string.Empty),
        ("rules", "/colour", "1", "/colour"),
        ("rules", "/seasons/seasonal/winter", Remove, "/seasons/seasonal"),
        ("rules", "/seasons/seasonal/winter", "-1", "/seasons/seasonal/winter"),
        ("rules", "/seasons/seasonal/monsoon", "5", "/seasons/seasonal/monsoon"),
    ];

    public static TheoryData<string> ContentTypeNames => [.. _bases.Keys];

    public static TheoryData<string, string, string, string> Defects
    {
        get
        {
            var data = new TheoryData<string, string, string, string>();
            foreach ((string type, string change, string value, string pointer) in _defects)
            {
                data.Add(type, change, value, pointer);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ContentTypeNames))]
    public void Base_ValidForTheLoaderAndForItsSchema(string type)
    {
        (string file, string json) = _bases[type];
        using var content = TempContent.FromMinimal();
        content.Write(file, json);

        ContentLoadResult result = content.Load();
        ImmutableArray<ContentDiagnostic> schema = ContentLinter.ValidateSchemas(result.Raw);

        result.Diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(result.Diagnostics));
        schema.ShouldBeEmpty(DiagnosticAssertions.Dump(schema));
    }

    [Fact]
    public void Base_CoversEveryContentType()
    {
        string[] expected = [.. ContentTypes.All.Select(static t => t.DirectoryName()).Order(StringComparer.Ordinal)];

        _bases.Keys.ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(Defects))]
    public void Defect_LoaderAndSchemaReportTheSameSpot(string type, string change, string value, string spot)
    {
        (string file, string json) = _bases[type];
        string broken = value == Remove ? JsonEdit.Remove(json, change) : JsonEdit.Set(json, change, value);
        using var content = TempContent.FromMinimal();
        content.Write(file, broken);

        ContentLoadResult result = content.Load();
        ImmutableArray<ContentDiagnostic> schema = ContentLinter.ValidateSchemas(result.Raw);

        result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.File == file && d.JsonPointer == spot)
            .ShouldBeTrue($"The loader should report {file}#{spot}.\n{DiagnosticAssertions.Dump(result.Diagnostics)}");
        schema.Any(d => d.Code == DiagnosticCodes.Evl001 && d.File == file && d.JsonPointer == spot)
            .ShouldBeTrue($"The schema should report {file}#{spot}.\n{DiagnosticAssertions.Dump(schema)}");
    }

    [Fact]
    public void Defects_EveryContentTypeHasAtLeastOneCase()
    {
        string[] covered = [.. _defects.Select(static d => d.Type).Distinct().Order(StringComparer.Ordinal)];

        covered.ShouldBe([.. _bases.Keys]);
    }

    [Fact]
    public void Run_DefectBothReport_IsShownOnceWithTheLoadersMessage()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("buildings/sawpit.json", "\"buildDays\": 1,", "\"buildDays\": 1,\n  \"colour\": \"red\",");

        ContentLoadResult result = content.Load();
        ImmutableArray<ContentDiagnostic> linted = ContentLinter.Run(result);

        ContentDiagnostic schemaOnly = ContentLinter.ValidateSchemas(result.Raw).ShouldHaveOne(DiagnosticCodes.Evl001, "buildings/sawpit.json", "/colour");
        ContentDiagnostic merged = linted.ShouldHaveOne(DiagnosticCodes.Evl001, "buildings/sawpit.json", "/colour");
        schemaOnly.Message.ShouldNotBe(merged.Message);
        merged.Message.ShouldBe(result.Diagnostics.Single().Message);
        linted.Length.ShouldBe(1);
    }

    [Fact]
    public void Run_ExtraPropertyOnEveryType_FailsEvl001AtThatProperty()
    {
        foreach ((string type, (string file, string json)) in _bases)
        {
            using var content = TempContent.FromMinimal();
            content.Write(file, JsonEdit.Set(json, "/colour", "\"red\""));

            ImmutableArray<ContentDiagnostic> linted = ContentLinter.Run(content.Load());

            linted.ShouldHaveOne(DiagnosticCodes.Evl001, file, "/colour").Severity.ShouldBe(DiagnosticSeverity.Error, type);
        }
    }

    [Fact]
    public void Run_BridgeSpanWithoutRoadCost_IsReportedOnceAtTheProperty_ThoughTheSchemaAloneWouldPointAtTheObject()
    {
        using var content = TempContent.FromMinimal();
        content.Write("buildings/test_hut.json", """{ "id": "test_hut", "category": "infrastructure", "era": 1, "footprint": [1, 1], "buildDays": 0, "bridgeSpan": 3 }""");

        ContentLoadResult result = content.Load();

        result.Diagnostics.ShouldHaveOne(DiagnosticCodes.Evl001, "buildings/test_hut.json", "/bridgeSpan");
        ContentLinter.ValidateSchemas(result.Raw).ShouldHaveOne(DiagnosticCodes.Evl001, "buildings/test_hut.json", string.Empty);
        ContentLinter.Run(result).ShouldBe(result.Diagnostics);
    }

    [Fact]
    public void Run_DuplicateTags_AreReportedOnceAtTheSecondTag_ThoughTheSchemaAloneWouldPointAtTheArray()
    {
        using var content = TempContent.FromMinimal();
        content.Write("goods/test_good.json", """{ "id": "test_good", "category": "raw", "era": 1, "tags": ["a", "a"] }""");

        ContentLoadResult result = content.Load();

        result.Diagnostics.ShouldHaveOne(DiagnosticCodes.Evl001, "goods/test_good.json", "/tags/1");
        ContentLinter.ValidateSchemas(result.Raw).ShouldHaveOne(DiagnosticCodes.Evl001, "goods/test_good.json", "/tags");
        ContentLinter.Run(result).ShouldBe(result.Diagnostics);
    }

    [Fact]
    public void Run_CountWithTwoSelectors_IsReportedOnceAtTheCount_ThoughTheSchemaAloneWouldPointAtTheSecondSelector()
    {
        using var content = TempContent.FromMinimal();
        content.Write("techs/test_tech.json", """
            { "id": "test_tech", "era": 1, "cost": 10, "inspiration": { "when": { "count": { "building": "sawpit", "tag": "x", "atLeast": 1 } } } }
            """);

        ContentLoadResult result = content.Load();

        result.Diagnostics.ShouldHaveOne(DiagnosticCodes.Evl001, "techs/test_tech.json", "/inspiration/when/count");
        ContentLinter.ValidateSchemas(result.Raw).ShouldHaveOne(DiagnosticCodes.Evl001, "techs/test_tech.json", "/inspiration/when/count/tag");
        ContentLinter.Run(result).ShouldBe(result.Diagnostics);
    }

    [Fact]
    public void Run_FileTheLoaderRejected_IsNotCheckedAgainstItsSchema_ButOtherFilesAre()
    {
        // sawpit fails in the loader (an unknown property); the schema then adds nothing for it. test_hut passes the loader
        // yet repeats a terrain, which only the schema notices, and that is still reported.
        using var content = TempContent.FromMinimal();
        content.Replace("buildings/sawpit.json", "\"buildDays\": 1,", "\"buildDays\": 1,\n  \"colour\": \"red\",\n  \"zzz\": 1,");
        content.Write("buildings/test_hut.json", JsonEdit.Set(_bases["buildings"].Json, "/placement/terrain", """["plains", "plains"]"""));

        ContentLoadResult result = content.Load();
        ImmutableArray<ContentDiagnostic> linted = ContentLinter.Run(result);

        ContentLinter.ValidateSchemas(result.Raw).Count(static d => d.File == "buildings/sawpit.json").ShouldBe(2);
        linted.Where(static d => d.File == "buildings/sawpit.json").ShouldBe(result.Diagnostics.Where(static d => d.File == "buildings/sawpit.json"));
        linted.ShouldHaveOne(DiagnosticCodes.Evl001, "buildings/test_hut.json", "/placement/terrain");
    }

    [Theory]
    [InlineData("/placement/terrain", "[\"plains\", \"plains\"]", "/placement/terrain")]
    [InlineData("/placement/areaYield", "{ \"kind\": \"radius\", \"terrain\": [\"plains\", \"plains\"], \"radius\": 3, \"fullAt\": 5 }", "/placement/areaYield/terrain")]
    [InlineData("/siteBonus/0/when", "{ \"terrain\": [\"hills\", \"hills\"] }", "/siteBonus/0/when/terrain")]
    public void Run_RepeatedTerrain_LoadsButTheSchemaStillRejectsIt(string change, string value, string spot)
    {
        // The one place the schema is stricter than the loader: a terrain listed twice is harmless to the loader and almost
        // certainly a slip, so a file the loader accepts still gets this schema diagnostic.
        using var content = TempContent.FromMinimal();
        content.Write("terrains/hills.json", """{ "id": "hills", "buildable": true }""");
        content.Write("locale/en/hills.json", """{ "terrain.hills.name": "Hills", "terrain.hills.desc": "Rising ground." }""");
        content.Write("buildings/test_hut.json", JsonEdit.Set(_bases["buildings"].Json, change, value));

        ContentLoadResult result = content.Load();

        result.Diagnostics.ShouldBeEmpty(DiagnosticAssertions.Dump(result.Diagnostics));
        result.Db.ShouldNotBeNull();
        ImmutableArray<ContentDiagnostic> linted = ContentLinter.Run(result);
        linted.WithCode(DiagnosticCodes.Evl001).ShouldBeExactly(Expected.Error(DiagnosticCodes.Evl001, "buildings/test_hut.json", spot, "[uniqueItems]"));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1.5")]
    [InlineData("0.001")]
    [InlineData("12.250")]
    [InlineData("1000000")]
    [InlineData("0.1")]
    [InlineData("2.0")]
    public void Quantity_ValidDecimal_PassesBoth(string quantity)
    {
        ExpectQuantity(quantity, flagged: false);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("-1")]
    [InlineData("1.2345")]
    [InlineData("0.0001")]
    [InlineData("0.0005")]
    [InlineData("1.0001")]
    [InlineData("99999.9999")]
    [InlineData("\"1\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[1]")]
    public void Quantity_Invalid_FailsBoth(string quantity)
    {
        ExpectQuantity(quantity, flagged: true);
    }

    [Theory]
    [InlineData("1e1")]
    [InlineData("1E0")]
    [InlineData("5e-1")]
    public void Quantity_ExponentForm_IsRejectedOnlyByTheLoader(string quantity)
    {
        // A JSON number written as 1e1 is a valid multiple of 0.001 as far as a schema can tell; only the loader reads the text.
        using var content = TempContent.FromMinimal();
        content.Replace("buildings/sawpit.json", "\"timber\": 3", $"\"timber\": {quantity}");

        ContentLoadResult result = content.Load();

        result.Diagnostics.ShouldHaveOne(DiagnosticCodes.Evl001, "buildings/sawpit.json", "/cost/timber").Message.ShouldContain("exponent");
        ContentLinter.ValidateSchemas(result.Raw).ShouldBeEmpty();
        ContentLinter.Run(result).Length.ShouldBe(1);
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("1e-400")]
    [InlineData("123456789012345678901234567890.123456789")]
    [InlineData("79228162514264337593543950336")]
    [InlineData("0.0000000000000000000000000001")]
    public void Quantity_ExtremeNumber_DoesNotCrashTheLinter(string quantity)
    {
        using var content = TempContent.FromMinimal();
        content.Replace("buildings/sawpit.json", "\"timber\": 3", $"\"timber\": {quantity}");

        ImmutableArray<ContentDiagnostic> linted = ContentLinter.Run(content.Load());

        linted.ShouldContain(d => d.Code == DiagnosticCodes.Evl001 && d.File == "buildings/sawpit.json" && d.JsonPointer == "/cost/timber");
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("a1", true)]
    [InlineData("wood", true)]
    [InlineData("a_b", true)]
    [InlineData("a_1", true)]
    [InlineData("a1_b2_c3", true)]
    [InlineData("a__b", false)]
    [InlineData("_a", false)]
    [InlineData("a_", false)]
    [InlineData("A", false)]
    [InlineData("aB", false)]
    [InlineData("1a", false)]
    [InlineData("a-b", false)]
    [InlineData("a.b", false)]
    [InlineData("a b", false)]
    public void Id_SchemaAndLoaderUseTheSameGrammar(string id, bool valid)
    {
        // The loader checks the file name; the schema checks the "id" value. The same ID must pass or fail both.
        using var fileName = TempContent.FromMinimal();
        fileName.Write($"goods/{id}.json", $$"""{ "id": "{{id}}", "category": "raw", "era": 1 }""");
        bool loaderAccepts = !fileName.Load().Diagnostics.Any(d => d.Message.Contains("is not a valid ID", StringComparison.Ordinal));

        using var value = TempContent.FromMinimal();
        value.Write("goods/wood.json", $$"""{ "id": "{{id}}", "category": "raw", "era": 1 }""");
        bool schemaAccepts = ContentLinter.ValidateSchemas(value.Load().Raw).All(d => d.JsonPointer != "/id");

        loaderAccepts.ShouldBe(valid, "loader");
        schemaAccepts.ShouldBe(valid, "schema");
    }

    [Fact]
    public void Id_LongerThan64Characters_IsRejectedByBoth()
    {
        string id = "a" + new string('b', 64);
        using var content = TempContent.FromMinimal();
        content.Write("goods/wood.json", $$"""{ "id": "{{id}}", "category": "raw", "era": 1 }""");

        ContentLoadResult result = content.Load();

        result.Diagnostics.ShouldHaveOne(DiagnosticCodes.Evl001, "goods/wood.json", "/id");
        ContentLinter.ValidateSchemas(result.Raw).ShouldHaveOne(DiagnosticCodes.Evl001, "goods/wood.json", "/id");
    }

    [Theory]
    [InlineData("a b", "a b")]
    [InlineData("a/b", "a~1b")]
    [InlineData("a~b", "a~0b")]
    [InlineData("a~1b", "a~01b")]
    [InlineData("a%20b", "a%20b")]
    [InlineData("caf\\u00e9", "café")]
    [InlineData("a\\\"b", "a\"b")]
    [InlineData("a\\\\b", "a\\b")]
    [InlineData("a#b", "a#b")]
    [InlineData("", "")]
    public void Pointer_PropertyNameWithSpecialCharacters_IsEscapedTheSameWayByTheLoaderAndTheSchema(string jsonKey, string expectedToken)
    {
        // jsonKey is the key as written in the JSON file (with escapes); expectedToken is RFC 6901's spelling of it.
        using var content = TempContent.FromMinimal();
        content.Replace("buildings/sawpit.json", "\"timber\": 3", $"\"timber\": 3, \"{jsonKey}\": 1");
        string spot = "/cost/" + expectedToken;

        ContentLoadResult result = content.Load();

        result.Diagnostics.ShouldHaveOne(DiagnosticCodes.Evl002, "buildings/sawpit.json", spot);
        ContentLinter.ValidateSchemas(result.Raw).ShouldHaveOne(DiagnosticCodes.Evl001, "buildings/sawpit.json", spot);
    }

    [Fact]
    public void Validate_ItemsThatFailedToParse_AreSkippedByTheSchemaCheck()
    {
        using var content = TempContent.FromMinimal();
        content.Write("goods/broken.json", "{ \"id\": \"broken\", ");

        ContentLoadResult result = content.Load();

        ContentLinter.ValidateSchemas(result.Raw).ShouldBeEmpty();
        ContentLinter.Run(result).ShouldHaveOne(DiagnosticCodes.Evl001, "goods/broken.json", string.Empty);
    }

    private static void ExpectQuantity(string quantity, bool flagged)
    {
        using var content = TempContent.FromMinimal();
        content.Replace("buildings/sawpit.json", "\"timber\": 3", $"\"timber\": {quantity}");

        ContentLoadResult result = content.Load();
        bool loaderFlags = result.Diagnostics.Any(d => d.File == "buildings/sawpit.json" && d.JsonPointer == "/cost/timber");
        bool schemaFlags = ContentLinter.ValidateSchemas(result.Raw).Any(d => d.File == "buildings/sawpit.json" && d.JsonPointer == "/cost/timber");

        loaderFlags.ShouldBe(flagged, "loader: " + DiagnosticAssertions.Dump(result.Diagnostics));
        schemaFlags.ShouldBe(flagged, "schema");
    }
}
