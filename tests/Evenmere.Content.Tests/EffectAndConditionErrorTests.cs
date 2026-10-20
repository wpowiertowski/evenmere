using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests;

/// <summary>An unknown or malformed effect or condition is a diagnostic with a precise pointer, never an exception.</summary>
public sealed class EffectAndConditionErrorTests
{
    private const string TechFile = "techs/net_weaving.json";

    private static TempContent TechWith(string? effects = null, string? when = null)
    {
        var content = TempContent.FromMinimal();
        string effectsPart = effects is null ? string.Empty : $", \"effects\": {effects}";
        string inspirationPart = when is null ? string.Empty : $", \"inspiration\": {{ \"when\": {when} }}";
        content.Write(TechFile, $$"""{ "id": "net_weaving", "era": 1, "cost": 50{{effectsPart}}{{inspirationPart}} }""");
        return content;
    }

    [Fact]
    public void Load_UnknownEffectKey_IsEvl001AtTheKey()
    {
        using TempContent content = TechWith(effects: """[ { "frobnicate": "x" } ]""");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", TechFile, "/effects/0/frobnicate");

        diagnostic.Message.ShouldContain("unknown effect 'frobnicate'");
        diagnostic.Message.ShouldContain("unlockBuilding");
    }

    [Fact]
    public void Load_UnknownConditionKey_IsEvl001AtTheKey()
    {
        using TempContent content = TechWith(when: """{ "flurb": { "atLeast": 1 } }""");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", TechFile, "/inspiration/when/flurb");

        diagnostic.Message.ShouldContain("unknown condition 'flurb'");
    }

    [Fact]
    public void Load_UnknownConditionNestedInCombinators_IsReportedWithTheFullPointer()
    {
        using TempContent content = TechWith(when: """{ "all": [ { "not": { "any": [ { "season": "autumn" }, { "flurb": 1 } ] } } ] }""");

        content.Load().ShouldFailWith("EVL001", TechFile, "/inspiration/when/all/0/not/any/1/flurb");
    }

    [Fact]
    public void Load_EffectWithTwoKeys_IsEvl001()
    {
        using TempContent content = TechWith(effects: """[ { "unlockBuilding": "sawpit", "unlockTier": "villagers" } ]""");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", TechFile, "/effects/0");

        diagnostic.Message.ShouldContain("exactly one key");
    }

    [Theory]
    [InlineData("""[ {} ]""")]
    [InlineData("""[ "unlockBuilding" ]""")]
    [InlineData("""[ [ "unlockBuilding", "sawpit" ] ]""")]
    [InlineData("""[ null ]""")]
    public void Load_EffectThatIsNotASingleKeyObject_IsEvl001(string effects)
    {
        using TempContent content = TechWith(effects: effects);

        content.Load().ShouldFailWith("EVL001", TechFile, "/effects/0");
    }

    [Fact]
    public void Load_EffectsThatAreNotAnArray_IsEvl001()
    {
        using TempContent content = TechWith(effects: """{ "unlockBuilding": "sawpit" }""");

        content.Load().ShouldFailWith("EVL001", TechFile, "/effects");
    }

    [Fact]
    public void Load_ConditionWithASiblingAtLeast_IsRejected()
    {
        // tech-tree.md's authoring notes show this shape, but architecture.md puts atLeast inside the condition's object.
        using TempContent content = TechWith(when: """{ "count": { "building": "fishing_hut" }, "atLeast": 5 }""");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", TechFile, "/inspiration/when");

        diagnostic.Message.ShouldContain("exactly one key");
        diagnostic.Message.ShouldContain("count, atLeast");
    }

    [Fact]
    public void Load_UnknownStat_ListsTheStats()
    {
        using TempContent content = TechWith(effects: """[ { "modifier": { "stat": "luck", "percent": 5 } } ]""");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", TechFile, "/effects/0/modifier/stat");

        diagnostic.Message.ShouldContain("productivity");
        diagnostic.Message.ShouldContain("output.knowledge");
    }

    [Fact]
    public void Load_UnknownModifierTargetKind_IsEvl001AtTheKey()
    {
        using TempContent content = TechWith(effects: """[ { "modifier": { "stat": "productivity", "target": { "colour": "red" }, "percent": 5 } } ]""");

        content.Load().ShouldFailWith("EVL001", TechFile, "/effects/0/modifier/target/colour");
    }

    [Fact]
    public void Load_ModifierTargetWithTwoSelectors_IsEvl001()
    {
        using TempContent content = TechWith(effects: """[ { "modifier": { "stat": "productivity", "target": { "tag": "farm", "category": "production" }, "percent": 5 } } ]""");

        content.Load().ShouldFailWith("EVL001", TechFile, "/effects/0/modifier/target");
    }

    [Theory]
    [InlineData("12.5")]
    [InlineData("\"20\"")]
    [InlineData("100000")]
    public void Load_ModifierPercentThatIsNotASaneInteger_IsEvl001(string percent)
    {
        using TempContent content = TechWith(effects: $$"""[ { "modifier": { "stat": "productivity", "percent": {{percent}} } } ]""");

        content.Load().ShouldFailWith("EVL001", TechFile, "/effects/0/modifier/percent");
    }

    [Fact]
    public void Load_ModifierWithoutPercent_IsEvl001()
    {
        using TempContent content = TechWith(effects: """[ { "modifier": { "stat": "productivity" } } ]""");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", TechFile, "/effects/0/modifier");

        diagnostic.Message.ShouldContain("percent");
    }

    [Fact]
    public void Load_ModifierConditionWithAnUnknownKey_IsReportedAtTheCondition()
    {
        using TempContent content = TechWith(effects: """[ { "modifier": { "stat": "productivity", "percent": 5, "when": { "weather": "rain" } } } ]""");

        content.Load().ShouldFailWith("EVL001", TechFile, "/effects/0/modifier/when/weather");
    }

    [Theory]
    [InlineData("\"teleport\"")]
    [InlineData("\"great_works\"")]
    [InlineData("5")]
    public void Load_BadFeature_IsEvl001(string feature)
    {
        using TempContent content = TechWith(effects: $$"""[ { "unlockFeature": {{feature}} } ]""");

        content.Load().ShouldFailWith("EVL001", TechFile, "/effects/0/unlockFeature");
    }

    [Fact]
    public void Load_GreatWorksFeatureForAnUnknownType_IsEvl002()
    {
        using TempContent content = TechWith(effects: """[ { "unlockFeature": "great_works:bogus" } ]""");

        content.Load().ShouldFailWith("EVL002", TechFile, "/effects/0/unlockFeature");
    }

    [Theory]
    [InlineData("""[ { "unlockBuilding": 5 } ]""", "/effects/0/unlockBuilding")]
    [InlineData("""[ { "unlockTerrainAccess": "swamps" } ]""", "/effects/0/unlockTerrainAccess")]
    [InlineData("""[ { "traditionSlot": "arcane" } ]""", "/effects/0/traditionSlot")]
    [InlineData("""[ { "grantGoods": {} } ]""", "/effects/0/grantGoods")]
    [InlineData("""[ { "grantGoods": [] } ]""", "/effects/0/grantGoods")]
    [InlineData("""[ { "grantPoints": {} } ]""", "/effects/0/grantPoints")]
    [InlineData("""[ { "grantPoints": { "mana": 5 } } ]""", "/effects/0/grantPoints/mana")]
    [InlineData("""[ { "grantPoints": { "knowledge": 0 } } ]""", "/effects/0/grantPoints/knowledge")]
    public void Load_MalformedEffectValue_IsEvl001AtTheValue(string effects, string jsonPointer)
    {
        using TempContent content = TechWith(effects: effects);

        content.Load().ShouldFailWith("EVL001", TechFile, jsonPointer);
    }

    [Theory]
    [InlineData("""{ "count": { "building": "fishing_hut", "tag": "fishing", "atLeast": 5 } }""", "/inspiration/when/count")]
    [InlineData("""{ "count": { "atLeast": 5 } }""", "/inspiration/when/count")]
    [InlineData("""{ "count": { "building": "fishing_hut" } }""", "/inspiration/when/count")]
    [InlineData("""{ "count": { "building": "fishing_hut", "atLeast": 0 } }""", "/inspiration/when/count/atLeast")]
    [InlineData("""{ "discovered": { "atLeast": 1 } }""", "/inspiration/when/discovered")]
    [InlineData("""{ "discovered": { "kind": "ruin", "region": "plains", "atLeast": 1 } }""", "/inspiration/when/discovered")]
    [InlineData("""{ "all": [] }""", "/inspiration/when/all")]
    [InlineData("""{ "any": {} }""", "/inspiration/when/any")]
    [InlineData("""{ "season": "monsoon" }""", "/inspiration/when/season")]
    [InlineData("""{ "population": { "tier": "villagers", "atLeast": 1.5 } }""", "/inspiration/when/population/atLeast")]
    [InlineData("""{ "produced": { "good": "fish" } }""", "/inspiration/when/produced")]
    [InlineData("""{ "produced": { "good": "fish", "atLeast": 1.2345 } }""", "/inspiration/when/produced/atLeast")]
    [InlineData("""{ "researched": 5 }""", "/inspiration/when/researched")]
    public void Load_MalformedCondition_IsEvl001AtTheOffendingValue(string when, string jsonPointer)
    {
        using TempContent content = TechWith(when: when);

        content.Load().ShouldFailWith("EVL001", TechFile, jsonPointer);
    }

    [Theory]
    [InlineData("""{ "count": { "building": "fishing_hut", "atLeast": 5, "bogus": 1 } }""", "/inspiration/when/count/bogus")]
    [InlineData("""{ "population": { "atLeast": 5, "bogus": 1 } }""", "/inspiration/when/population/bogus")]
    [InlineData("""{ "festivalsHeld": { "atLeast": 2, "bogus": true } }""", "/inspiration/when/festivalsHeld/bogus")]
    public void Load_UnknownPropertyInsideAConditionObject_IsEvl001(string when, string jsonPointer)
    {
        using TempContent content = TechWith(when: when);

        content.Load().ShouldFailWith("EVL001", TechFile, jsonPointer);
    }

    [Fact]
    public void Load_UnknownPropertyInsideAModifier_IsEvl001()
    {
        using TempContent content = TechWith(effects: """[ { "modifier": { "stat": "productivity", "percent": 5, "colour": "red" } } ]""");

        content.Load().ShouldFailWith("EVL001", TechFile, "/effects/0/modifier/colour");
    }

    [Fact]
    public void Load_GreatWorkSourcedByDiscoveryWithCreativityFields_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Write("greatworks/relic.json", """
            { "id": "relic", "source": "discovery", "baseCreativity": 100, "cultureByEra": [2] }
            """);

        content.Load().ShouldFailWith("EVL001", "greatworks/relic.json", "/baseCreativity");
    }

    [Fact]
    public void Load_ConditionsNestedPastTheDepthLimit_IsEvl001NotAnException()
    {
        string nested = string.Concat(Enumerable.Repeat("{ \"not\": ", 100)) + "{ \"season\": \"autumn\" }" + new string('}', 100);
        using TempContent content = TechWith(when: nested);

        content.Load().ShouldFailWith("EVL001", TechFile, string.Empty);
    }

    [Fact]
    public void Load_InspirationWithoutWhen_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Write(TechFile, """{ "id": "net_weaving", "era": 1, "cost": 50, "inspiration": {} }""");

        content.Load().ShouldFailWith("EVL001", TechFile, "/inspiration");
    }

    [Fact]
    public void Load_RepeatableWithoutGrowth_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Write(TechFile, """{ "id": "net_weaving", "era": 1, "cost": 50, "repeatable": {} }""");

        content.Load().ShouldFailWith("EVL001", TechFile, "/repeatable");
    }
}
