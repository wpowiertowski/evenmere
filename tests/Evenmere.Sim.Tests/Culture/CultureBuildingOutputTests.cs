using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Culture.Commands;

namespace Evenmere.Sim.Tests.Culture;

public sealed class CultureBuildingOutputTests
{
    [Fact]
    public void BuildingOutputs_FoundersHallAndShrine_CreditCultureADay()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        h.Send(new SetCivicTarget(custom));
        h.Add("founders_hall", 0, 0);

        h.Run();

        // The hall outputs 1 culture alongside its 2 knowledge.
        h.Queries.Progress(custom).ShouldBe(Amount.FromUnits(1));
        h.Queries.PointsPerDay.ShouldBe(Amount.FromUnits(1));
    }

    [Fact]
    public void BuildingOutputs_ScalesWithFill()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        h.Send(new SetCivicTarget(custom));
        EntityId hall = h.Add("test_hall", 0, 0);

        // The hall outputs 4 culture; a half staff fills it halfway.
        h.Sim.Queries.Workforce.SetFill(hall, Ratio.FromPerMille(500));
        h.Run();

        h.Queries.Progress(custom).ShouldBe(Amount.FromUnits(2));
    }

    [Fact]
    public void BuildingOutputs_Bonus_IncreasesTheCredit()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        h.Send(new SetCivicTarget(custom));
        EntityId hall = h.Add("test_hall", 0, 0);
        h.Sim.Queries.Workforce.SetFill(hall, Ratio.One);
        h.Sim.Queries.Progression.AddModifier(
            Stat.OutputCulture,
            new ModifierContribution(EffectSource.ForCivic(CivicId.From(0)), 50, true));

        h.Run();

        // 4 culture at +50% is 6.
        h.Queries.Progress(custom).ShouldBe(Amount.FromUnits(6));
    }
}
