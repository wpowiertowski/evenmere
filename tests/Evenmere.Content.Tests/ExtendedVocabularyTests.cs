using System.Collections.Immutable;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Content.Tests.Support;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Content.Tests;

/// <summary>
/// The vocabulary additions made after the first review: five modifier stats, the discovery target, the
/// revealCoastline and requestFestival effects, the stock condition, and the building-local siteBonus and aura.
/// </summary>
public sealed class ExtendedVocabularyTests
{
    private const string TechFile = "techs/net_weaving.json";
    private const string SawpitFile = "buildings/sawpit.json";

    private static TempContent TechWith(string? effects = null, string? when = null, bool withFestival = false)
    {
        var content = TempContent.FromMinimal();
        if (withFestival)
        {
            content.Write("festivals/harvest_fair.json", """
                { "id": "harvest_fair", "cost": { "fish": 10 }, "days": 5, "culturePerDay": 5 }
                """);
        }

        string effectsPart = effects is null ? string.Empty : $", \"effects\": {effects}";
        string inspirationPart = when is null ? string.Empty : $", \"inspiration\": {{ \"when\": {when} }}";
        content.Write(TechFile, $$"""{ "id": "net_weaving", "era": 1, "cost": 50{{effectsPart}}{{inspirationPart}} }""");
        return content;
    }

    private static TempContent SawpitWith(string extraFields)
    {
        var content = TempContent.FromMinimal();
        content.Write(SawpitFile, $$"""
            { "id": "sawpit", "category": "production", "era": 1, "footprint": [2, 2], "buildDays": 1, {{extraFields}} }
            """);
        return content;
    }

    // --- modifier stats

    [Theory]
    [InlineData("expeditionSpeed", Stat.ExpeditionSpeed)]
    [InlineData("comfortBonus", Stat.ComfortBonus)]
    [InlineData("festivalCost", Stat.FestivalCost)]
    [InlineData("festivalDuration", Stat.FestivalDuration)]
    [InlineData("settlerArrival", Stat.SettlerArrival)]
    public void Modifier_NewStat_IsReadWithItsExactSpelling(string name, Stat expected)
    {
        using TempContent content = TechWith(effects: $$"""[ { "modifier": { "stat": "{{name}}", "percent": 50 } } ]""");

        ContentDb db = content.Load().ShouldLoadCleanly();

        db.Get(db.IdOf<TechId>("net_weaving")).Effects.ShouldBe([new ModifierEffect(expected, new IslandTarget(), 50, null)]);
    }

    [Theory]
    [InlineData("ExpeditionSpeed")]
    [InlineData("expedition_speed")]
    [InlineData("settlerarrival")]
    public void Modifier_NewStatWithTheWrongSpelling_IsEvl001(string name)
    {
        using TempContent content = TechWith(effects: $$"""[ { "modifier": { "stat": "{{name}}", "percent": 50 } } ]""");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", TechFile, "/effects/0/modifier/stat");

        diagnostic.Message.ShouldContain("expeditionSpeed");
    }

    // --- discovery target

    [Theory]
    [InlineData("natural_wonder", DiscoveryKind.NaturalWonder)]
    [InlineData("ruin", DiscoveryKind.Ruin)]
    [InlineData("shipwreck", DiscoveryKind.Shipwreck)]
    public void Modifier_DiscoveryTarget_IsRead(string kind, DiscoveryKind expected)
    {
        using TempContent content = TechWith(effects: $$"""[ { "modifier": { "stat": "output.culture", "target": { "discovery": "{{kind}}" }, "percent": 50 } } ]""");

        ContentDb db = content.Load().ShouldLoadCleanly();

        db.Get(db.IdOf<TechId>("net_weaving")).Effects.ShouldBe(
            [new ModifierEffect(Stat.OutputCulture, new DiscoveryTarget(expected), 50, null)]);
    }

    [Theory]
    [InlineData("\"meteor\"")]
    [InlineData("\"iron_deposit\"")]
    [InlineData("\"Natural_Wonder\"")]
    [InlineData("5")]
    [InlineData("null")]
    public void Modifier_UnknownDiscoveryKind_IsEvl001(string kind)
    {
        using TempContent content = TechWith(effects: $$"""[ { "modifier": { "stat": "output.culture", "target": { "discovery": {{kind}} }, "percent": 50 } } ]""");

        content.Load().ShouldFailWith("EVL001", TechFile, "/effects/0/modifier/target/discovery");
    }

    [Fact]
    public void Modifier_UnknownDiscoveryKind_ListsTheThreeValidOnes()
    {
        using TempContent content = TechWith(effects: """[ { "modifier": { "stat": "output.culture", "target": { "discovery": "meteor" }, "percent": 50 } } ]""");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", TechFile, "/effects/0/modifier/target/discovery");

        diagnostic.Message.ShouldContain("natural_wonder, ruin, shipwreck");
    }

    // --- revealCoastline

    [Fact]
    public void RevealCoastline_True_IsRead()
    {
        using TempContent content = TechWith(effects: """[ { "revealCoastline": true } ]""");

        ContentDb db = content.Load().ShouldLoadCleanly();

        db.Get(db.IdOf<TechId>("net_weaving")).Effects.ShouldBe([new RevealCoastlineEffect()]);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("\"true\"")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[true]")]
    public void RevealCoastline_AnythingButTheLiteralTrue_IsEvl001(string value)
    {
        using TempContent content = TechWith(effects: $$"""[ { "revealCoastline": {{value}} } ]""");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", TechFile, "/effects/0/revealCoastline");

        diagnostic.Message.ShouldContain("exactly true");
    }

    // --- requestFestival

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(100)]
    public void RequestFestival_DiscountInRange_IsRead(int discount)
    {
        using TempContent content = TechWith(
            effects: $$"""[ { "requestFestival": { "festival": "harvest_fair", "discountPercent": {{discount}} } } ]""",
            withFestival: true);

        ContentDb db = content.Load().ShouldLoadCleanly();

        db.Get(db.IdOf<TechId>("net_weaving")).Effects.ShouldBe([new RequestFestivalEffect(db.IdOf<FestivalId>("harvest_fair"), discount)]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("-5")]
    [InlineData("25.5")]
    [InlineData("\"25\"")]
    public void RequestFestival_DiscountOutOfRangeOrNotAnInteger_IsEvl001(string discount)
    {
        using TempContent content = TechWith(
            effects: $$"""[ { "requestFestival": { "festival": "harvest_fair", "discountPercent": {{discount}} } } ]""",
            withFestival: true);

        content.Load().ShouldFailWith("EVL001", TechFile, "/effects/0/requestFestival/discountPercent");
    }

    [Fact]
    public void RequestFestival_UnknownFestival_IsEvl002AtTheFestival()
    {
        using TempContent content = TechWith(
            effects: """[ { "requestFestival": { "festival": "harvest_fare", "discountPercent": 25 } } ]""",
            withFestival: true);

        content.Load().ShouldFailWith("EVL002", TechFile, "/effects/0/requestFestival/festival");
    }

    [Theory]
    [InlineData("""{ "discountPercent": 25 }""", "/effects/0/requestFestival")]
    [InlineData("""{ "festival": "harvest_fair" }""", "/effects/0/requestFestival")]
    [InlineData("""{ "festival": "harvest_fair", "discountPercent": 25, "extra": 1 }""", "/effects/0/requestFestival/extra")]
    [InlineData("\"harvest_fair\"", "/effects/0/requestFestival")]
    [InlineData("25", "/effects/0/requestFestival")]
    public void RequestFestival_MalformedObject_IsEvl001(string value, string jsonPointer)
    {
        using TempContent content = TechWith(effects: $$"""[ { "requestFestival": {{value}} } ]""", withFestival: true);

        content.Load().ShouldFailWith("EVL001", TechFile, jsonPointer);
    }

    // --- stock

    [Fact]
    public void Stock_IsReadAsAGoodAndAQuantity()
    {
        using TempContent content = TechWith(when: """{ "stock": { "good": "timber", "atLeast": 40.5 } }""");

        ContentDb db = content.Load().ShouldLoadCleanly();

        db.Get(db.IdOf<TechId>("net_weaving")).Inspiration.ShouldBe(new StockCondition(db.IdOf<GoodId>("timber"), Amount.FromMilli(40500)));
    }

    [Theory]
    [InlineData("""{ "stock": { "good": "timber" } }""", "/inspiration/when/stock")]
    [InlineData("""{ "stock": { "atLeast": 40 } }""", "/inspiration/when/stock")]
    [InlineData("""{ "stock": { "good": "timber", "atLeast": 0 } }""", "/inspiration/when/stock/atLeast")]
    [InlineData("""{ "stock": { "good": "timber", "atLeast": 1.2345 } }""", "/inspiration/when/stock/atLeast")]
    [InlineData("""{ "stock": { "good": "timber", "atLeast": "40" } }""", "/inspiration/when/stock/atLeast")]
    [InlineData("""{ "stock": { "good": "timber", "atLeast": 40, "extra": 1 } }""", "/inspiration/when/stock/extra")]
    [InlineData("""{ "stock": "timber" }""", "/inspiration/when/stock")]
    public void Stock_Malformed_IsEvl001(string when, string jsonPointer)
    {
        using TempContent content = TechWith(when: when);

        content.Load().ShouldFailWith("EVL001", TechFile, jsonPointer);
    }

    [Fact]
    public void Stock_UnknownGood_IsEvl002AtTheGood()
    {
        using TempContent content = TechWith(when: """{ "stock": { "good": "timbre", "atLeast": 40 } }""");

        content.Load().ShouldFailWith("EVL002", TechFile, "/inspiration/when/stock/good");
    }

    // --- siteBonus

    [Fact]
    public void SiteBonus_TerrainAndNear_AreRead()
    {
        using TempContent content = SawpitWith("""
            "siteBonus": [
              { "when": { "terrain": ["forest", "plains"] }, "stat": "productivity", "percent": 15 },
              { "when": { "near": { "building": "fishing_hut", "radius": 4 } }, "stat": "output.knowledge", "percent": -10 },
              { "when": { "near": { "tag": "gatherer", "radius": 2 } }, "stat": "productivity", "percent": 5 }
            ]
            """);

        ContentDb db = content.Load().ShouldLoadCleanly();
        ImmutableArray<SiteBonusSpec> bonuses = db.Get(db.IdOf<BuildingTypeId>("sawpit")).SiteBonuses;

        bonuses.Length.ShouldBe(3);
        bonuses[0].When.ShouldBeOfType<TerrainSite>().Terrain.ShouldBe([db.IdOf<TerrainId>("forest"), db.IdOf<TerrainId>("plains")]);
        bonuses[0].Stat.ShouldBe(Stat.Productivity);
        bonuses[0].Percent.ShouldBe(15);
        bonuses[1].When.ShouldBe(new NearSite(new BuildingTypeSelector(db.IdOf<BuildingTypeId>("fishing_hut")), 4));
        bonuses[1].Stat.ShouldBe(Stat.OutputKnowledge);
        bonuses[1].Percent.ShouldBe(-10);
        bonuses[2].When.ShouldBe(new NearSite(new BuildingTagSelector("gatherer"), 2));
    }

    [Fact]
    public void SiteBonus_Absent_IsEmpty()
    {
        ContentDb db = ContentLoader.Load(TestPaths.MinimalPack).ShouldLoadCleanly();

        db.Get(db.IdOf<BuildingTypeId>("sawpit")).SiteBonuses.ShouldBeEmpty();
        db.Get(db.IdOf<BuildingTypeId>("sawpit")).Aura.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{ "when": { "terrain": ["forest"], "near": { "building": "fishing_hut", "radius": 4 } }, "stat": "productivity", "percent": 5 }""", "/siteBonus/0/when")]
    [InlineData("""{ "when": {}, "stat": "productivity", "percent": 5 }""", "/siteBonus/0/when")]
    [InlineData("""{ "stat": "productivity", "percent": 5 }""", "/siteBonus/0")]
    [InlineData("""{ "when": "forest", "stat": "productivity", "percent": 5 }""", "/siteBonus/0/when")]
    [InlineData("""{ "when": { "terrain": [] }, "stat": "productivity", "percent": 5 }""", "/siteBonus/0/when/terrain")]
    [InlineData("""{ "when": { "terrain": "forest" }, "stat": "productivity", "percent": 5 }""", "/siteBonus/0/when/terrain")]
    [InlineData("""{ "when": { "near": { "building": "fishing_hut", "tag": "gatherer", "radius": 4 } }, "stat": "productivity", "percent": 5 }""", "/siteBonus/0/when/near")]
    [InlineData("""{ "when": { "near": { "radius": 4 } }, "stat": "productivity", "percent": 5 }""", "/siteBonus/0/when/near")]
    [InlineData("""{ "when": { "near": { "tag": "gatherer" } }, "stat": "productivity", "percent": 5 }""", "/siteBonus/0/when/near")]
    [InlineData("""{ "when": { "near": { "tag": "gatherer", "radius": 0 } }, "stat": "productivity", "percent": 5 }""", "/siteBonus/0/when/near/radius")]
    [InlineData("""{ "when": { "near": { "tag": "gatherer", "radius": 2.5 } }, "stat": "productivity", "percent": 5 }""", "/siteBonus/0/when/near/radius")]
    [InlineData("""{ "when": { "near": { "category": "production", "radius": 4 } }, "stat": "productivity", "percent": 5 }""", "/siteBonus/0/when/near")]
    [InlineData("""{ "when": { "near": { "tag": "Not A Tag", "radius": 4 } }, "stat": "productivity", "percent": 5 }""", "/siteBonus/0/when/near/tag")]
    [InlineData("""{ "when": { "terrain": ["forest"], "colour": "red" }, "stat": "productivity", "percent": 5 }""", "/siteBonus/0/when/colour")]
    [InlineData("""{ "when": { "terrain": ["forest"] }, "stat": "luck", "percent": 5 }""", "/siteBonus/0/stat")]
    [InlineData("""{ "when": { "terrain": ["forest"] }, "percent": 5 }""", "/siteBonus/0")]
    [InlineData("""{ "when": { "terrain": ["forest"] }, "stat": "productivity" }""", "/siteBonus/0")]
    [InlineData("""{ "when": { "terrain": ["forest"] }, "stat": "productivity", "percent": 5.5 }""", "/siteBonus/0/percent")]
    [InlineData("""{ "when": { "terrain": ["forest"] }, "stat": "productivity", "percent": 5, "colour": "red" }""", "/siteBonus/0/colour")]
    public void SiteBonus_Malformed_IsEvl001AtTheOffendingValue(string entry, string jsonPointer)
    {
        using TempContent content = SawpitWith($$"""
            "siteBonus": [ {{entry}} ]
            """);

        content.Load().ShouldFailWith("EVL001", SawpitFile, jsonPointer);
    }

    [Fact]
    public void SiteBonus_NotAnArray_IsEvl001()
    {
        using TempContent content = SawpitWith("""
            "siteBonus": { "when": { "terrain": ["forest"] }, "stat": "productivity", "percent": 5 }
            """);

        content.Load().ShouldFailWith("EVL001", SawpitFile, "/siteBonus");
    }

    [Theory]
    [InlineData("""{ "when": { "terrain": ["forest", "tundra"] }, "stat": "productivity", "percent": 5 }""", "/siteBonus/0/when/terrain/1")]
    [InlineData("""{ "when": { "near": { "building": "bakery", "radius": 4 } }, "stat": "productivity", "percent": 5 }""", "/siteBonus/0/when/near/building")]
    public void SiteBonus_UnknownReference_IsEvl002(string entry, string jsonPointer)
    {
        using TempContent content = SawpitWith($$"""
            "siteBonus": [ {{entry}} ]
            """);

        content.Load().ShouldFailWith("EVL002", SawpitFile, jsonPointer);
    }

    [Fact]
    public void SiteBonus_SeveralBadEntries_AreEachReported()
    {
        using TempContent content = SawpitWith("""
            "siteBonus": [
              { "when": { "terrain": ["forest"], "near": { "tag": "x", "radius": 1 } }, "stat": "productivity", "percent": 5 },
              { "when": { "terrain": ["forest"] }, "stat": "luck", "percent": 5 }
            ]
            """);

        ContentLoadResult result = content.Load();

        result.Db.ShouldBeNull();
        result.Diagnostics.ShouldHaveSingleMatch("EVL001", SawpitFile, "/siteBonus/0/when");
        result.Diagnostics.ShouldHaveSingleMatch("EVL001", SawpitFile, "/siteBonus/1/stat");
    }

    // --- aura

    [Theory]
    [InlineData("""{ "building": "fishing_hut" }""")]
    [InlineData("""{ "tag": "house" }""")]
    [InlineData("""{ "category": "residence" }""")]
    [InlineData("""{ "tier": "settlers" }""")]
    [InlineData("""{ "good": "fish" }""")]
    [InlineData("""{ "discovery": "ruin" }""")]
    public void Aura_AnyModifierTargetShape_IsRead(string target)
    {
        using TempContent content = SawpitWith($$"""
            "aura": { "stat": "capacity", "percent": 10, "target": {{target}}, "coverage": 16 }
            """);

        ContentDb db = content.Load().ShouldLoadCleanly();

        AuraSpec aura = db.Get(db.IdOf<BuildingTypeId>("sawpit")).Aura.ShouldNotBeNull();
        aura.Stat.ShouldBe(Stat.Capacity);
        aura.Percent.ShouldBe(10);
        aura.Coverage.ShouldBe(16);
        aura.Target.ShouldNotBeOfType<IslandTarget>();
    }

    [Theory]
    [InlineData("""{ "stat": "capacity", "percent": 10, "coverage": 16 }""", "/aura")]
    [InlineData("""{ "stat": "capacity", "percent": 10, "target": { "category": "residence" } }""", "/aura")]
    [InlineData("""{ "stat": "capacity", "percent": 10, "target": { "category": "residence" }, "coverage": 0 }""", "/aura/coverage")]
    [InlineData("""{ "stat": "capacity", "percent": 10, "target": { "category": "residence" }, "coverage": 1.5 }""", "/aura/coverage")]
    [InlineData("""{ "stat": "capacity", "percent": 10, "target": {}, "coverage": 16 }""", "/aura/target")]
    [InlineData("""{ "stat": "capacity", "percent": 10, "target": null, "coverage": 16 }""", "/aura/target")]
    [InlineData("""{ "stat": "capacity", "percent": 10, "target": "residence", "coverage": 16 }""", "/aura/target")]
    [InlineData("""{ "stat": "capacity", "percent": 10, "target": { "category": "residence", "tier": "settlers" }, "coverage": 16 }""", "/aura/target")]
    [InlineData("""{ "stat": "capacity", "percent": 10, "target": { "colour": "red" }, "coverage": 16 }""", "/aura/target/colour")]
    [InlineData("""{ "stat": "capacity", "percent": 10, "target": { "category": "castle" }, "coverage": 16 }""", "/aura/target/category")]
    [InlineData("""{ "stat": "capacity", "percent": 10, "target": { "discovery": "meteor" }, "coverage": 16 }""", "/aura/target/discovery")]
    [InlineData("""{ "stat": "luck", "percent": 10, "target": { "category": "residence" }, "coverage": 16 }""", "/aura/stat")]
    [InlineData("""{ "percent": 10, "target": { "category": "residence" }, "coverage": 16 }""", "/aura")]
    [InlineData("""{ "stat": "capacity", "target": { "category": "residence" }, "coverage": 16 }""", "/aura")]
    [InlineData("""{ "stat": "capacity", "percent": 10, "target": { "category": "residence" }, "coverage": 16, "colour": "red" }""", "/aura/colour")]
    public void Aura_Malformed_IsEvl001AtTheOffendingValue(string aura, string jsonPointer)
    {
        using TempContent content = SawpitWith($$"""
            "aura": {{aura}}
            """);

        content.Load().ShouldFailWith("EVL001", SawpitFile, jsonPointer);
    }

    [Fact]
    public void Aura_NotAnObject_IsEvl001()
    {
        using TempContent content = SawpitWith("""
            "aura": [ { "stat": "capacity" } ]
            """);

        content.Load().ShouldFailWith("EVL001", SawpitFile, "/aura");
    }

    [Fact]
    public void Aura_UnknownTargetReference_IsEvl002()
    {
        using TempContent content = SawpitWith("""
            "aura": { "stat": "capacity", "percent": 10, "target": { "tier": "serfs" }, "coverage": 16 }
            """);

        content.Load().ShouldFailWith("EVL002", SawpitFile, "/aura/target/tier");
    }

    // --- ContentNames exposure

    [Fact]
    public void ContentNames_ListEveryKeyTheLoaderAccepts()
    {
        ContentNames.EffectKinds.ShouldContain("revealCoastline");
        ContentNames.EffectKinds.ShouldContain("requestFestival");
        ContentNames.ConditionKinds.ShouldContain("stock");
        ContentNames.ModifierTargetKinds.ShouldContain("discovery");
        ContentNames.SiteConditionKinds.ShouldBe(["terrain", "near"]);
        ContentNames.NamesOf<Stat>().ShouldContain("expeditionSpeed");
        ContentNames.NamesOf<Stat>().ShouldContain("comfortBonus");
        ContentNames.NamesOf<Stat>().ShouldContain("festivalCost");
        ContentNames.NamesOf<Stat>().ShouldContain("festivalDuration");
        ContentNames.NamesOf<Stat>().ShouldContain("settlerArrival");
    }

    [Fact]
    public void ContentNames_EffectKinds_AreAllAcceptedAndNothingElseIs()
    {
        // The kitchen-sink tech uses every effect kind, so a kind the loader dropped would fail here.
        using TempContent content = KitchenSink.Create();
        ContentDb db = content.Load().ShouldLoadCleanly();

        Type[] used = [.. db.Get(db.IdOf<TechId>("kitchen_tech")).Effects.Select(static e => e.GetType()).Distinct()];

        used.Length.ShouldBe(ContentNames.EffectKinds.Length, "Every effect kind appears in the kitchen-sink tech.");
    }
}
