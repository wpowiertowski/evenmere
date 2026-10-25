using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Time;
using SimSeasonCurve = Evenmere.Sim.Systems.Production.Seasons.SeasonCurve;

namespace Evenmere.Sim.Tests.Production.Seasons;

public sealed class SeasonCurveTests
{
    [Theory]
    [InlineData(Season.Spring, 1000)]
    [InlineData(Season.Summer, 1200)]
    [InlineData(Season.Autumn, 1100)]
    [InlineData(Season.Winter, 700)]
    public void FactorFor_SeasonalTag_ReturnsTheCurveValue(Season season, int expected)
    {
        SimSeasonCurve.FactorFor(ProductionHarness.SliceContent.Rules, "seasonal", season).ShouldBe(expected);
    }

    [Fact]
    public void FactorFor_UnknownTag_ReturnsFullSpeed()
    {
        foreach (Season season in Enum.GetValues<Season>())
        {
            SimSeasonCurve.FactorFor(ProductionHarness.SliceContent.Rules, "processing", season).ShouldBe(1000, season.ToString());
        }
    }

    [Fact]
    public void FactorFor_GrainFarm_UsesItsSeasonalTag()
    {
        ContentDb content = ProductionHarness.SliceContent;
        BuildingDef farm = content.Get(content.IdOf<BuildingTypeId>("grain_farm"));

        SimSeasonCurve.FactorFor(content.Rules, farm, Season.Summer).ShouldBe(1200);
        SimSeasonCurve.FactorFor(content.Rules, farm, Season.Winter).ShouldBe(700);
    }

    [Fact]
    public void FactorFor_WindmillWithoutACurvedTag_ReturnsFullSpeed()
    {
        ContentDb content = ProductionHarness.SliceContent;
        BuildingDef mill = content.Get(content.IdOf<BuildingTypeId>("windmill"));

        foreach (Season season in Enum.GetValues<Season>())
        {
            SimSeasonCurve.FactorFor(content.Rules, mill, season).ShouldBe(1000, season.ToString());
        }
    }
}
