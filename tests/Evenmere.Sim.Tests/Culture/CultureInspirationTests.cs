using Evenmere.Content.Conditions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Systems.Culture;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Systems.Research;

namespace Evenmere.Sim.Tests.Culture;

public sealed class CultureInspirationTests
{
    [Fact]
    public void Inspiration_OnDayThree_CompletesTheCivicOnDaySix()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        h.Send(new SetCivicTarget(custom));
        h.Events();

        // Days 1 and 2 accrue without the inspiration; it triggers on day 3, cutting the 40-culture cost to 24.
        h.RunInspiredDay(custom, inspired: false, culture: 4, day: 1);
        h.Queries.Progress(custom).ShouldBe(Amount.FromUnits(4));

        h.RunInspiredDay(custom, inspired: false, culture: 4, day: 2);
        h.Queries.Progress(custom).ShouldBe(Amount.FromUnits(8));

        h.RunInspiredDay(custom, inspired: true, culture: 4, day: 3);
        h.Queries.IsInspired(custom).ShouldBeTrue();
        h.Queries.Cost(custom).ShouldBe(Amount.FromUnits(24));
        h.Queries.Progress(custom).ShouldBe(Amount.FromUnits(12));
        h.Events().ShouldBe([InspirationTriggered.ForCivic(h.Sim.Context.Tick, custom)]);

        h.RunInspiredDay(custom, inspired: true, culture: 4, day: 4);
        h.Queries.Progress(custom).ShouldBe(Amount.FromUnits(16));

        h.RunInspiredDay(custom, inspired: true, culture: 4, day: 5);
        h.Queries.Progress(custom).ShouldBe(Amount.FromUnits(20));

        h.RunInspiredDay(custom, inspired: true, culture: 4, day: 6);
        h.Queries.IsDone(custom).ShouldBeTrue();
        h.Events().ShouldBe([new CivicAdopted(h.Sim.Context.Tick, custom)]);
    }

    [Fact]
    public void Inspiration_TriggersOnce()
    {
        var h = CultureHarness.Create();
        CivicId custom = h.Civic("test_custom");
        h.Send(new SetCivicTarget(custom));
        h.SetInspiration(custom, inspired: true);
        h.Events();

        h.Run();
        h.Run();

        h.Events().OfType<InspirationTriggered>().ShouldHaveSingleItem();
    }
}

/// <summary>Day-by-day helpers for the civic inspiration tests.</summary>
internal static class CultureInspirationHelpers
{
    /// <summary>Runs one day with the inspiration condition forced on or off and a fixed culture income.</summary>
    /// <param name="h">The harness.</param>
    /// <param name="custom">The civic whose inspiration is steered.</param>
    /// <param name="inspired">Whether the condition holds today.</param>
    /// <param name="culture">The whole units of culture credited before the run.</param>
    /// <param name="day">The day number, used for the clock so event ticks read as days.</param>
    public static void RunInspiredDay(this CultureHarness h, CivicId custom, bool inspired, long culture, int day)
    {
        h.SetInspiration(custom, inspired);
        h.CreditCulture(culture);
        h.Sim.SetTick(day);
        h.Run();
    }

    /// <summary>Steers the inspiration condition of one civic: it holds exactly when the flag is set.</summary>
    /// <param name="h">The harness.</param>
    /// <param name="custom">The civic.</param>
    /// <param name="inspired">Whether the condition holds.</param>
    public static void SetInspiration(this CultureHarness h, CivicId custom, bool inspired)
    {
        Condition? when = h.Content.Get(custom).Inspiration;
        h.Sim.Queries.Progression.ConditionEvaluator = condition => inspired && ReferenceEquals(condition, when);
    }
}
