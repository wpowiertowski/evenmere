using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Research.Commands;

namespace Evenmere.Sim.Tests.Research;

public sealed class ResearchBuildingOutputTests
{
    [Fact]
    public void BuildingOutputs_FoundersHall_CreditsTwoKnowledgeADay()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        h.Send(new SetResearchTarget(theory));
        h.Add("founders_hall", 0, 0);
        h.Events();

        h.Run();

        h.Queries.Progress(theory).ShouldBe(Amount.FromUnits(2));
        h.Queries.PointsPerDay.ShouldBe(Amount.FromUnits(2));
    }

    [Fact]
    public void BuildingOutputs_ScalesWithFill()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        h.Send(new SetResearchTarget(theory));
        EntityId hall = h.Add("test_hall", 0, 0);

        // The hall outputs 6 knowledge; a half staff fills it halfway.
        h.Sim.Queries.Workforce.SetFill(hall, Ratio.FromPerMille(500));
        h.Run();

        h.Queries.Progress(theory).ShouldBe(Amount.FromUnits(3));
    }

    [Fact]
    public void BuildingOutputs_EmptyHall_CreditsNothing()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        h.Send(new SetResearchTarget(theory));
        EntityId hall = h.Add("test_hall", 0, 0);

        h.Sim.Queries.Workforce.SetFill(hall, Ratio.Zero);
        h.Run();

        h.Queries.Progress(theory).ShouldBe(Amount.Zero);
        h.Queries.PointsPerDay.ShouldBe(Amount.Zero);
    }

    [Fact]
    public void BuildingOutputs_PausedAndUnfinishedBuildings_CreditNothing()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        h.Send(new SetResearchTarget(theory));
        h.Add("test_hall", 0, 0, BuildingState.Paused);
        h.Add("test_hall", 4, 0, BuildingState.Site);

        h.Run();

        h.Queries.Progress(theory).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void BuildingOutputs_Bonus_IncreasesTheCredit()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        h.Send(new SetResearchTarget(theory));
        EntityId hall = h.Add("test_hall", 0, 0);
        h.Sim.Queries.Workforce.SetFill(hall, Ratio.One);
        h.Sim.Queries.Progression.AddModifier(
            Stat.OutputKnowledge,
            new ModifierContribution(EffectSource.ForTech(TechId.From(0)), 25, true));

        h.Run();

        // 6 knowledge at +25% is 7.5.
        h.Queries.Progress(theory).ShouldBe(Amount.Parse("7.5"));
    }
}
