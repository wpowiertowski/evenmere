using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Time;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Production.Seasons;
using Evenmere.Sim.Tests.Progression;

namespace Evenmere.Sim.Tests.Production.Seasons;

public sealed class SeasonBoundaryTests
{
    [Fact]
    public void Simulation_EmitsSeasonChanged_OnTheFirstTickOfEachSeason()
    {
        var sim = Simulation.CreateNew(ProductionHarness.SliceContent, new NewGameOptions { Seed = 42 });

        for (int i = 0; i < GameTime.TicksPerSeason - 1; i++)
        {
            sim.Tick();
        }

        sim.DrainEvents().OfType<SeasonChanged>().ShouldBeEmpty();

        sim.Tick();
        sim.DrainEvents().OfType<SeasonChanged>().ShouldBe([new SeasonChanged(GameTime.TicksPerSeason, Season.Summer)]);

        for (int i = 0; i < GameTime.TicksPerSeason; i++)
        {
            sim.Tick();
        }

        sim.DrainEvents().OfType<SeasonChanged>().ShouldBe([new SeasonChanged(2 * GameTime.TicksPerSeason, Season.Autumn)]);
    }

    [Fact]
    public void AutumnOnlyModifier_FlipsExactlyAtTheSeasonBoundary()
    {
        var h = ProgressionHarness.Create();
        TraditionId card = h.Content.IdOf<TraditionId>("harvest_festival");
        h.Apply(EffectSource.ForTradition(card), [.. h.Content.Get(card).Effects]);
        var farm = ModifierContext.ForBuildingType(h.Building("grain_farm"));

        // The last tick of summer: the harvest rite sleeps.
        h.Fake.SetTick(2 * GameTime.TicksPerSeason - 1);
        h.Queries.BonusPercent(Stat.Productivity, farm).ShouldBe(0);

        // The first tick of autumn: it wakes, with no other tick in between.
        h.Fake.SetTick(2 * GameTime.TicksPerSeason);
        h.Queries.BonusPercent(Stat.Productivity, farm).ShouldBe(20);
    }
}
