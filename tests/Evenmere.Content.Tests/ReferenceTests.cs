using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests;

/// <summary>Unresolved references are EVL002 and name the file and the JSON pointer of the offending value.</summary>
public sealed class ReferenceTests
{
    [Fact]
    public void Load_UnknownRecipe_ReportsEvl002AtTheRecipePointer()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("buildings/sawpit.json", "\"recipe\": \"saw_timber\"", "\"recipe\": \"saw_timbre\"");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL002", "buildings/sawpit.json", "/recipe");

        diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
        diagnostic.Message.ShouldContain("saw_timbre");
    }

    [Fact]
    public void Load_UnknownGoodInACost_PointsAtTheMapKey()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("buildings/fishing_hut.json", "\"timber\": 3", "\"timbre\": 3");

        content.Load().ShouldFailWith("EVL002", "buildings/fishing_hut.json", "/cost/timbre");
    }

    [Fact]
    public void Load_UnknownPrerequisite_PointsAtTheArrayIndex()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("techs/town_charter.json", "[\"net_weaving\"]", "[\"net_weaving\", \"net_weavin\"]");

        content.Load().ShouldFailWith("EVL002", "techs/town_charter.json", "/requires/1");
    }

    [Fact]
    public void Load_UnknownUnlockTarget_PointsAtTheEffect()
    {
        using var content = TempContent.FromMinimal();
        content.Replace(
            "civics/village_moot.json",
            "{ \"unlockTradition\": \"common_hearth\" }",
            "{ \"unlockTradition\": \"common_hearth\" },\n    { \"unlockBuilding\": \"bakery\" }");

        content.Load().ShouldFailWith("EVL002", "civics/village_moot.json", "/effects/2/unlockBuilding");
    }

    [Fact]
    public void Load_UnknownGoodInANestedCondition_PointsAllTheWayDown()
    {
        using var content = TempContent.FromMinimal();
        content.Write("techs/net_weaving.json", """
            {
              "id": "net_weaving", "era": 1, "cost": 50,
              "inspiration": { "when": { "all": [
                { "researched": "net_weaving" },
                { "produced": { "good": "grain", "atLeast": 30 } }
              ] } }
            }
            """);

        content.Load().ShouldFailWith("EVL002", "techs/net_weaving.json", "/inspiration/when/all/1/produced/good");
    }

    [Fact]
    public void Load_UnknownTierInAModifierTarget_PointsAtTheTarget()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("traditions/common_hearth.json", "\"tier\": \"settlers\"", "\"tier\": \"settlerz\"");

        content.Load().ShouldFailWith("EVL002", "traditions/common_hearth.json", "/effects/0/modifier/target/tier");
    }

    [Fact]
    public void Load_UnknownTierReferences_ArePointedAtIndividually()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("tiers/villagers.json", "\"advanceFrom\": \"settlers\"", "\"advanceFrom\": \"serfs\"");
        content.Replace("tiers/villagers.json", "\"fish\": 3", "\"bread\": 3");

        ContentLoadResult result = content.Load();

        result.ShouldFailWith("EVL002", "tiers/villagers.json", "/advanceFrom");
        result.Diagnostics.ShouldHaveSingleMatch("EVL002", "tiers/villagers.json", "/basicNeeds/goods/bread");
    }

    [Fact]
    public void Load_UnknownEraNumber_IsEvl002OnTheEraField()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("buildings/sawpit.json", "\"era\": 1", "\"era\": 5");

        ContentDiagnostic diagnostic = content.Load().ShouldFailWith("EVL002", "buildings/sawpit.json", "/era");

        diagnostic.Message.ShouldContain("5");
    }

    [Fact]
    public void Load_EraRequiringUnknownTech_PointsAtTheRequirement()
    {
        using var content = TempContent.FromMinimal();
        content.Replace("eras/township.json", "\"tech\": \"town_charter\"", "\"tech\": \"town_charta\"");

        content.Load().ShouldFailWith("EVL002", "eras/township.json", "/requires/tech");
    }

    [Fact]
    public void Load_SeveralBadReferencesInOneFile_AreAllReported()
    {
        using var content = TempContent.FromMinimal();
        content.Write("recipes/saw_timber.json", """
            { "id": "saw_timber", "inputs": { "log": 1, "twig": 2 }, "outputs": { "plank": 1 }, "cycleSeconds": 20 }
            """);

        ContentLoadResult result = content.Load();

        result.Db.ShouldBeNull();
        result.Diagnostics.ShouldHaveSingleMatch("EVL002", "recipes/saw_timber.json", "/inputs/log");
        result.Diagnostics.ShouldHaveSingleMatch("EVL002", "recipes/saw_timber.json", "/inputs/twig");
        result.Diagnostics.ShouldHaveSingleMatch("EVL002", "recipes/saw_timber.json", "/outputs/plank");
    }

    [Fact]
    public void Load_ReferenceToAnItemThatFailedToParse_IsNotAlsoReportedUnresolved()
    {
        using var content = TempContent.FromMinimal();
        content.Write("goods/wood.json", "{ not json");

        ContentLoadResult result = content.Load();

        result.Db.ShouldBeNull();
        result.Diagnostics.ShouldAllBe(d => d.Code == "EVL001", DiagnosticAssertions.Dump(result.Diagnostics));
        result.Diagnostics.Length.ShouldBe(1);
    }

    [Theory]
    [InlineData("founders_hall", "buildings/founders_hall.json")]
    [InlineData("settler_house", "buildings/settler_house.json")]
    [InlineData("dirt_road", "buildings/dirt_road.json")]
    [InlineData("storehouse", "buildings/storehouse.json")]
    public void Load_MissingRequiredBuilding_ReportsEvl002NamingTheExpectedFile(string name, string file)
    {
        using var content = TempContent.FromMinimal();
        content.Delete(file);

        ContentLoadResult result = content.Load();

        result.Db.ShouldBeNull();
        ContentDiagnostic diagnostic = result.Diagnostics.ShouldHaveSingleMatch("EVL002", file, string.Empty);
        diagnostic.Message.ShouldContain(name);
    }

    [Fact]
    public void Load_MissingRequiredSettlersTier_ReportsEvl002()
    {
        using var content = TempContent.FromMinimal();
        content.Delete("tiers/settlers.json");

        ContentLoadResult result = content.Load();

        result.Db.ShouldBeNull();
        result.Diagnostics.ShouldHaveSingleMatch("EVL002", "tiers/settlers.json", string.Empty);
    }
}
