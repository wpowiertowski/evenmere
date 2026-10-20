using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Content.Tests.Support;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Content.Tests;

/// <summary>Quantities are read from the raw JSON number text and parsed with Amount.Parse; they never pass through double.</summary>
public sealed class QuantityTests
{
    private const string CostFile = "buildings/fishing_hut.json";

    [Theory]
    [InlineData("1.2345")]
    [InlineData("0.0001")]
    [InlineData("12.3456789")]
    public void Load_MoreThanThreeDecimalsInACost_IsEvl001AtTheQuantity(string quantity)
    {
        using var content = TempContent.FromMinimal();
        content.Replace(CostFile, "\"timber\": 3", $"\"timber\": {quantity}");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", CostFile, "/cost/timber");

        diagnostic.Message.ShouldContain(quantity);
        diagnostic.Message.ShouldContain("decimal");
    }

    [Theory]
    [InlineData("1e2")]
    [InlineData("1E2")]
    [InlineData("3.5e0")]
    [InlineData("1e-3")]
    public void Load_ExponentFormInACost_IsEvl001(string quantity)
    {
        using var content = TempContent.FromMinimal();
        content.Replace(CostFile, "\"timber\": 3", $"\"timber\": {quantity}");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", CostFile, "/cost/timber");

        diagnostic.Message.ShouldContain("exponent");
    }

    [Fact]
    public void Load_QuantityWrittenAsAString_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Replace(CostFile, "\"timber\": 3", "\"timber\": \"1.2345\"");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL001", CostFile, "/cost/timber");

        diagnostic.Message.ShouldContain("string");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.000")]
    public void Load_ZeroOrNegativeCostQuantity_IsEvl001(string quantity)
    {
        using var content = TempContent.FromMinimal();
        content.Replace(CostFile, "\"timber\": 3", $"\"timber\": {quantity}");

        content.Load().ShouldFailWith("EVL001", CostFile, "/cost/timber");
    }

    [Fact]
    public void Load_QuantityTooLargeForAnAmount_IsEvl001()
    {
        using var content = TempContent.FromMinimal();
        content.Replace(CostFile, "\"timber\": 3", "\"timber\": 99999999999999999999");

        content.Load().ShouldFailWith("EVL001", CostFile, "/cost/timber");
    }

    [Theory]
    [InlineData("1", 1000)]
    [InlineData("1.0", 1000)]
    [InlineData("1.5", 1500)]
    [InlineData("1.25", 1250)]
    [InlineData("1.250", 1250)]
    [InlineData("0.001", 1)]
    [InlineData("0.1", 100)]
    [InlineData("12.345", 12345)]
    public void Load_QuantityWithUpToThreeDecimals_IsExactInMilliUnits(string quantity, long expectedMilli)
    {
        using var content = TempContent.FromMinimal();
        content.Replace("recipes/cut_wood.json", "\"wood\": 1", $"\"wood\": {quantity}");

        ContentDb db = content.Load().ShouldLoadCleanly();

        RecipeDef recipe = db.Get(db.IdOf<RecipeId>("cut_wood"));
        recipe.Outputs[0].Amount.Milli.ShouldBe(expectedMilli);
    }

    [Fact]
    public void Load_QuantityBeyondDoublePrecision_IsParsedExactly()
    {
        // 2^53 milli-units is 9007199254740.992; a double cannot hold the next digit, a decimal-text parse can.
        using var content = TempContent.FromMinimal();
        content.Replace("recipes/cut_wood.json", "\"wood\": 1", "\"wood\": 9007199254740.993");

        ContentDb db = content.Load().ShouldLoadCleanly();

        db.Get(db.IdOf<RecipeId>("cut_wood")).Outputs[0].Amount.Milli.ShouldBe(9_007_199_254_740_993L);
    }

    [Fact]
    public void Load_QuantitiesInConditionsAndEffects_AreExactToo()
    {
        using var content = TempContent.FromMinimal();
        content.Write("techs/net_weaving.json", """
            {
              "id": "net_weaving", "era": 1, "cost": 50,
              "effects": [ { "grantGoods": { "fish": 0.001 } }, { "grantPoints": { "coin": 1.005 } } ],
              "inspiration": { "when": { "produced": { "good": "fish", "atLeast": 30.125 } } }
            }
            """);

        ContentDb db = content.Load().ShouldLoadCleanly();
        TechDef tech = db.Get(db.IdOf<TechId>("net_weaving"));

        GrantGoodsEffect goods = tech.Effects[0].ShouldBeOfType<GrantGoodsEffect>();
        goods.Goods[0].Amount.Milli.ShouldBe(1);
        GrantPointsEffect points = tech.Effects[1].ShouldBeOfType<GrantPointsEffect>();
        points.Points[0].Amount.Milli.ShouldBe(1005);
        ProducedCondition produced = tech.Inspiration.ShouldBeOfType<ProducedCondition>();
        produced.AtLeast.Milli.ShouldBe(30125);
    }

    [Fact]
    public void Load_BadQuantitiesInSeveralPlaces_AreEachReportedWithTheirOwnPointer()
    {
        using var content = TempContent.FromMinimal();
        content.Write("recipes/saw_timber.json", """
            { "id": "saw_timber", "inputs": { "wood": 1.2345 }, "outputs": { "timber": 2.5e1 }, "cycleSeconds": 20 }
            """);

        ContentLoadResult result = content.Load();

        result.Db.ShouldBeNull();
        result.Diagnostics.ShouldHaveSingleMatch("EVL001", "recipes/saw_timber.json", "/inputs/wood");
        result.Diagnostics.ShouldHaveSingleMatch("EVL001", "recipes/saw_timber.json", "/outputs/timber");
    }

    [Theory]
    [InlineData("160.5")]
    [InlineData("1.6e2")]
    [InlineData("\"160\"")]
    [InlineData("0")]
    public void Load_NonIntegerOrNonPositiveNodeCost_IsEvl001(string cost)
    {
        using var content = TempContent.FromMinimal();
        content.Replace("techs/town_charter.json", "\"cost\": 160", $"\"cost\": {cost}");

        content.Load().ShouldFailWith("EVL001", "techs/town_charter.json", "/cost");
    }

    [Fact]
    public void Load_NodeCost_IsStoredAsWholeUnitsOfAmount()
    {
        ContentDb db = ContentLoader.Load(TestPaths.MinimalPack).ShouldLoadCleanly();

        db.Get(db.IdOf<TechId>("town_charter")).Cost.ShouldBe(Amount.FromUnits(160));
    }
}
