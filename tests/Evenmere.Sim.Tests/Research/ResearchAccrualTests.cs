using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Systems.Research;
using Evenmere.Sim.Systems.Research.Commands;

namespace Evenmere.Sim.Tests.Research;

public sealed class ResearchAccrualTests
{
    [Fact]
    public void Accrue_SixtyKnowledgeAtSixADay_CompletesOnDayTen()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        h.Send(new SetResearchTarget(theory));
        h.Events();

        for (int day = 1; day <= 9; day++)
        {
            h.CreditKnowledge(6);
            h.Run();

            h.Queries.IsDone(theory).ShouldBeFalse($"day {day}");
            h.Queries.Progress(theory).ShouldBe(Amount.FromUnits(6 * day), $"day {day}");
        }

        h.CreditKnowledge(6);
        h.Run();

        h.Queries.IsDone(theory).ShouldBeTrue();
        h.Queries.Progress(theory).ShouldBe(Amount.FromUnits(60));
        h.Queries.Target.ShouldBeNull();
        h.Queries.PointsPerDay.ShouldBe(Amount.FromUnits(6));
        h.Events().ShouldBe([new TechResearched(h.Sim.Context.Tick, theory)]);
    }

    [Fact]
    public void Accrue_WithNoTarget_BanksPointsForTheNextTarget()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");

        // A day's knowledge with nothing targeted is banked, not lost.
        h.CreditKnowledge(6);
        h.Run();

        h.Queries.Progress(theory).ShouldBe(Amount.Zero);
        h.State.Banked.ShouldBe(Amount.FromUnits(6));
        h.Queries.PointsPerDay.ShouldBe(Amount.FromUnits(6));

        // The banked points flow to the next target set, before that day's own points.
        h.Send(new SetResearchTarget(theory));
        h.Run();

        h.Queries.Progress(theory).ShouldBe(Amount.FromUnits(6));
        h.State.Banked.ShouldBe(Amount.Zero);
        h.Queries.PointsPerDay.ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Accrue_Overflow_CarriesIntoTheQueuedTech()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        TechId practice = h.Tech("test_practice");
        h.Send(new SetResearchTarget(theory));
        h.Send(new SetResearchQueue([practice]));
        h.Events();

        h.CreditKnowledge(70);
        h.Run();

        h.Queries.IsDone(theory).ShouldBeTrue();
        h.Queries.Progress(practice).ShouldBe(Amount.FromUnits(10));
        h.Queries.Target.ShouldBe(practice);
        h.Queries.Queue.ShouldBeEmpty();
        h.Events().ShouldBe([new TechResearched(h.Sim.Context.Tick, theory)]);
    }

    [Fact]
    public void Accrue_Overflow_RepeatsUntilItRunsOut()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        TechId practice = h.Tech("test_practice");
        h.Send(new SetResearchTarget(theory));
        h.Send(new SetResearchQueue([practice]));
        h.Events();

        h.CreditKnowledge(100);
        h.Run();

        h.Queries.IsDone(theory).ShouldBeTrue();
        h.Queries.IsDone(practice).ShouldBeTrue();
        h.Queries.Target.ShouldBeNull();
        h.Queries.Queue.ShouldBeEmpty();
        h.Events().ShouldBe(
        [
            new TechResearched(h.Sim.Context.Tick, theory),
            new TechResearched(h.Sim.Context.Tick, practice),
        ]);
    }

    [Fact]
    public void Accrue_PointsPerDay_ReportsTheLastDaysTotal()
    {
        var h = ResearchHarness.Create();
        h.Send(new SetResearchTarget(h.Tech("test_theory")));

        h.CreditKnowledge(6);
        h.Run();
        h.Queries.PointsPerDay.ShouldBe(Amount.FromUnits(6));

        h.CreditKnowledge(2);
        h.Run();
        h.Queries.PointsPerDay.ShouldBe(Amount.FromUnits(2));
    }
}
