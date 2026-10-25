using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Time;
using SimSeasonCurve = Evenmere.Sim.Systems.Production.Seasons.SeasonCurve;

namespace Evenmere.Sim.Tests.Production.Seasons;

public sealed class SeasonTableTests
{
    [Fact]
    public void SeasonFactorTable_MatchesFactorFor_ForEveryBuildingAndSeason()
    {
        var h = ProductionHarness.Create();
        ContentDb content = ProductionHarness.SliceContent;

        for (int type = 0; type < content.Buildings.Count; type++)
        {
            var id = BuildingTypeId.From((ushort)type);
            BuildingDef def = content.Get(id);
            foreach (Season season in Enum.GetValues<Season>())
            {
                h.System.SeasonFactorFor(id, season)
                    .ShouldBe(SimSeasonCurve.FactorFor(content.Rules, def, season), $"{content.NameOf(id)} in {season}");
            }
        }
    }
}
