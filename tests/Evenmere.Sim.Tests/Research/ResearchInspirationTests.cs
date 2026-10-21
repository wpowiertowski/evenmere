using Evenmere.Content.Conditions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Systems.Research;
using Evenmere.Sim.Systems.Research.Commands;

namespace Evenmere.Sim.Tests.Research;

public sealed class ResearchInspirationTests
{
    [Fact]
    public void Inspiration_OnDayThree_CompletesTheTechOnDaySix()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        h.Send(new SetResearchTarget(theory));
        h.Events();

        // Days 1 and 2 accrue without the inspiration; it triggers on day 3, cutting the 60-knowledge cost to 36.
        h.RunInspiredDay(theory, inspired: false, knowledge: 6, day: 1);
        h.Queries.Progress(theory).ShouldBe(Amount.FromUnits(6));
        h.Queries.IsInspired(theory).ShouldBeFalse();

        h.RunInspiredDay(theory, inspired: false, knowledge: 6, day: 2);
        h.Queries.Progress(theory).ShouldBe(Amount.FromUnits(12));

        h.RunInspiredDay(theory, inspired: true, knowledge: 6, day: 3);
        h.Queries.IsInspired(theory).ShouldBeTrue();
        h.Queries.Cost(theory).ShouldBe(Amount.FromUnits(36));
        h.Queries.Progress(theory).ShouldBe(Amount.FromUnits(18));
        h.Events().ShouldBe([InspirationTriggered.ForTech(h.Sim.Context.Tick, theory)]);

        h.RunInspiredDay(theory, inspired: true, knowledge: 6, day: 4);
        h.Queries.Progress(theory).ShouldBe(Amount.FromUnits(24));
        h.Queries.IsDone(theory).ShouldBeFalse();

        h.RunInspiredDay(theory, inspired: true, knowledge: 6, day: 5);
        h.Queries.Progress(theory).ShouldBe(Amount.FromUnits(30));

        h.RunInspiredDay(theory, inspired: true, knowledge: 6, day: 6);
        h.Queries.IsDone(theory).ShouldBeTrue();
        h.Events().ShouldBe([new TechResearched(h.Sim.Context.Tick, theory)]);
    }

    [Fact]
    public void Inspiration_PastProgress_FloorsTheCostAtProgress()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        h.Send(new SetResearchTarget(theory));

        // 50 of 60 is past the discounted 36, so the inspiration completes the tech with no new knowledge.
        h.CreditKnowledge(50);
        h.Run();
        h.Queries.IsDone(theory).ShouldBeFalse();
        h.Events();

        h.SetInspiration(theory, inspired: true);
        h.Run();

        h.Queries.IsDone(theory).ShouldBeTrue();
        h.Queries.Cost(theory).ShouldBe(Amount.FromUnits(50));
        h.Events().ShouldBe(
        [
            InspirationTriggered.ForTech(h.Sim.Context.Tick, theory),
            new TechResearched(h.Sim.Context.Tick, theory),
        ]);
    }

    [Fact]
    public void Inspiration_TriggersOnce()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        h.Send(new SetResearchTarget(theory));
        h.SetInspiration(theory, inspired: true);
        h.Events();

        h.Run();
        h.Run();
        h.Run();

        h.Events().OfType<InspirationTriggered>().ShouldHaveSingleItem();
        h.Queries.IsInspired(theory).ShouldBeTrue();
    }

    [Fact]
    public void Inspiration_DoneTech_NeverTriggers()
    {
        var h = ResearchHarness.Create();
        TechId theory = h.Tech("test_theory");
        h.Send(new SetResearchTarget(theory));
        h.CreditKnowledge(60);
        h.Run();
        h.Queries.IsDone(theory).ShouldBeTrue();
        h.SetInspiration(theory, inspired: true);
        h.Events();

        h.Run();

        h.Events().ShouldBeEmpty();
        h.Queries.IsInspired(theory).ShouldBeFalse();
    }
}

/// <summary>Day-by-day helpers for the inspiration tests.</summary>
internal static class ResearchInspirationHelpers
{
    /// <summary>Runs one day with the inspiration condition forced on or off and a fixed knowledge income.</summary>
    /// <param name="h">The harness.</param>
    /// <param name="theory">The tech whose inspiration is steered.</param>
    /// <param name="inspired">Whether the condition holds today.</param>
    /// <param name="knowledge">The whole units of knowledge credited before the run.</param>
    /// <param name="day">The day number, used for the clock so event ticks read as days.</param>
    public static void RunInspiredDay(this ResearchHarness h, TechId theory, bool inspired, long knowledge, int day)
    {
        h.SetInspiration(theory, inspired);
        h.CreditKnowledge(knowledge);
        h.Sim.SetTick(day);
        h.Run();
    }

    /// <summary>Steers the inspiration condition of one tech: it holds exactly when the flag is set.</summary>
    /// <param name="h">The harness.</param>
    /// <param name="theory">The tech.</param>
    /// <param name="inspired">Whether the condition holds.</param>
    public static void SetInspiration(this ResearchHarness h, TechId theory, bool inspired)
    {
        Condition? when = h.Content.Get(theory).Inspiration;
        h.Sim.Queries.Progression.ConditionEvaluator = condition => inspired && ReferenceEquals(condition, when);
    }
}
