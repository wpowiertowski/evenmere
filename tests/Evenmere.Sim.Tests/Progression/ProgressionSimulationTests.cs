using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Progression;

namespace Evenmere.Sim.Tests.Progression;

/// <summary>The progression and modifier systems inside the real roster and pipeline, with every other system as it is today.</summary>
public sealed class ProgressionSimulationTests
{
    private static Simulation NewSimulation() => Simulation.CreateNew(ProgressionPack.Content, new NewGameOptions { Seed = 7 });

    private static ProgressionSystem ProgressionOf(Simulation sim) =>
        (ProgressionSystem)sim.Pipeline.Entries.Single(static e => e.Id == SystemId.Progression).System;

    private static void ApplyTech(Simulation sim, string name)
    {
        TechId id = ProgressionPack.Content.IdOf<TechId>(name);
        sim.Context.ProgressionService.ApplyEffects(EffectSource.ForTech(id), ProgressionPack.Content.Get(id).Effects);
    }

    [Fact]
    public void Roster_ProgressionServices_AreTheRealOnes()
    {
        Simulation sim = NewSimulation();
        GoodId grain = ProgressionPack.Content.IdOf<GoodId>("grain");

        sim.Context.Counters.Add(CounterKey.Produced(grain), 1200);
        sim.Context.PointsLedger.Credit(PointKind.Knowledge, Amount.FromUnits(3));

        sim.Query.Progression.Counter(CounterKey.Produced(grain)).ShouldBe(1200);
        sim.Context.PointsLedger.TakeToday(PointKind.Knowledge).ShouldBe(Amount.FromUnits(3));
        sim.Context.PointsLedger.TakeToday(PointKind.Knowledge).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Roster_ApplyEffects_UnlocksThroughTheQueriesTheOtherSystemsUse()
    {
        Simulation sim = NewSimulation();
        BuildingTypeId bakery = ProgressionPack.Content.IdOf<BuildingTypeId>("bakery");
        sim.Query.Progression.IsUnlocked(bakery).ShouldBeFalse();

        ApplyTech(sim, "milling");

        sim.Query.Progression.IsUnlocked(bakery).ShouldBeTrue();
    }

    [Fact]
    public void Roster_GrantGoods_ArriveInTheRealPool()
    {
        Simulation sim = NewSimulation();
        GoodId timber = ProgressionPack.Content.IdOf<GoodId>("timber");

        ApplyTech(sim, "kitchen_tech");

        sim.Query.Storage.Stock(timber).ShouldBe(Amount.FromUnits(10));
    }

    [Fact]
    public void Roster_ApplyStartUnlocks_UnlocksTheStartItemsOnANewGame()
    {
        Simulation sim = NewSimulation();
        RequiredIds required = ProgressionPack.Content.Required;
        sim.Query.Progression.IsUnlocked(required.DirtRoad).ShouldBeFalse();
        ulong before = sim.ComputeStateHash();

        ProgressionOf(sim).ApplyStartUnlocks(sim.Context);

        sim.Query.Progression.IsUnlocked(required.DirtRoad).ShouldBeTrue();
        sim.Query.Progression.IsUnlocked(required.Settlers).ShouldBeTrue();
        sim.Query.Progression.CurrentEra.ShouldBe(ProgressionPack.Content.ErasByOrder[0]);
        sim.ComputeStateHash().ShouldNotBe(before);
    }

    [Fact]
    public void Tick_AfterUnlocks_KeepsEverythingAndRunsTheDailyStep()
    {
        Simulation sim = NewSimulation();
        ProgressionOf(sim).ApplyStartUnlocks(sim.Context);
        ApplyTech(sim, "milling");
        ulong unlocked = sim.ComputeStateHash();

        for (int i = 0; i < GameTime.TicksPerDay * 3; i++)
        {
            sim.Tick();
        }

        // Only the clock moves: the progression slice has nothing to do daily in M1.
        sim.Query.Progression.IsUnlocked(ProgressionPack.Content.IdOf<BuildingTypeId>("windmill")).ShouldBeTrue();
        sim.Query.Progression.CurrentEra.ShouldBe(ProgressionPack.Content.ErasByOrder[0]);
        sim.ComputeStateHash().ShouldNotBe(unlocked);
    }

    [Fact]
    public void SaveAndRestore_ModifiersAndUnlocks_AnswerTheSameInEverySeason()
    {
        Simulation original = NewSimulation();
        ProgressionOf(original).ApplyStartUnlocks(original.Context);
        ApplyTech(original, "milling");
        ApplyTech(original, "kitchen_tech");
        original.Context.Counters.Add(CounterKey.Built(ProgressionPack.Content.IdOf<BuildingTypeId>("bakery")), 2);
        byte[] saved = original.SerializeState();

        var restored = Simulation.Restore(ProgressionPack.Content, saved);

        restored.SerializeState().ShouldBe(saved);
        restored.ComputeStateHash().ShouldBe(original.ComputeStateHash());

        var farm = ModifierContext.ForBuildingType(ProgressionPack.Content.IdOf<BuildingTypeId>("grain_farm"));
        for (int season = 0; season < 4; season++)
        {
            restored.Query.Progression.BonusPercent(Stat.Productivity, farm).ShouldBe(original.Query.Progression.BonusPercent(Stat.Productivity, farm));
            restored.Query.Progression.BonusPercent(Stat.Productivity, farm).ShouldBe(season == (int)Season.Autumn ? 20 : 0, $"season {season}");

            for (int i = 0; i < GameTime.TicksPerSeason; i++)
            {
                original.Tick();
                restored.Tick();
            }

            restored.ComputeStateHash().ShouldBe(original.ComputeStateHash());
        }

        restored.Query.Progression.Counter(CounterKey.Built(ProgressionPack.Content.IdOf<BuildingTypeId>("bakery"))).ShouldBe(2);
        restored.Query.Progression.IsUnlocked(ProgressionPack.Content.IdOf<BuildingTypeId>("windmill")).ShouldBeTrue();
    }

    [Fact]
    public void ModifierQueries_RealSimulationWithSeasonModifier_FollowTheTickingClock()
    {
        Simulation sim = NewSimulation();
        ApplyTech(sim, "kitchen_tech");
        var farm = ModifierContext.ForBuildingType(ProgressionPack.Content.IdOf<BuildingTypeId>("grain_farm"));
        sim.Query.Progression.BonusPercent(Stat.Productivity, farm).ShouldBe(0);

        for (int i = 0; i < (2 * GameTime.TicksPerSeason) - 1; i++)
        {
            sim.Tick();
        }

        // The last tick of summer is still summer; one more tick is the first of autumn.
        sim.Query.Progression.BonusPercent(Stat.Productivity, farm).ShouldBe(0);
        sim.Tick();
        sim.Query.Progression.BonusPercent(Stat.Productivity, farm).ShouldBe(20);
    }
}
