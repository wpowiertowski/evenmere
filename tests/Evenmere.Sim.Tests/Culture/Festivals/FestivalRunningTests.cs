using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Culture;
using Evenmere.Sim.Systems.Culture.Festivals;
using Evenmere.Sim.Systems.Culture.Festivals.Commands;

namespace Evenmere.Sim.Tests.Culture.Festivals;

/// <summary>The festival run: daily culture (exact, including the autumn Harvest Fair bonus), the countdown, the end
/// with effect removal, the discounted goods, the modified duration and the mid-run save round trip.</summary>
public sealed class FestivalRunningTests
{
    private static FestivalsHarness Running()
    {
        var h = FestivalsHarness.Create();
        h.Unlock("test_harvest");
        h.Stock("fish", 100).Stock("wood", 100);
        EntityId ground = h.AddGround(2, 2);
        h.Events();
        h.Send(new HoldFestival(ground, h.Festival("test_harvest")));
        return h;
    }

    [Fact]
    public void Run_EachDay_CreditsCultureAndCountsDown()
    {
        FestivalsHarness h = Running();
        h.Events();

        h.Run();

        // Spring: the flat 5 culture, credited to the ledger for civics to spend the same day.
        h.Queries.PointsPerDay.ShouldBe(Amount.FromUnits(5));
        h.State.Active.ShouldBe(new FestivalRun(h.Festival("test_harvest"), h.State.Active!.Value.Ground, 4));
        h.Queries.IsFestivalRunning().ShouldBeTrue();
        h.Events().ShouldBeEmpty();
    }

    [Fact]
    public void Run_Autumn_CreditsTheHarvestBonusExactly()
    {
        // Day 20 is the first day of autumn (10 days a season).
        var h = FestivalsHarness.Create(tick: 20 * 120);
        h.Sim.World.Clock.Season.ShouldBe(Season.Autumn);
        h.Unlock("test_harvest");
        h.Stock("fish", 100).Stock("wood", 100);
        EntityId ground = h.AddGround(2, 2);
        h.Events();
        h.Send(new HoldFestival(ground, h.Festival("test_harvest")));
        h.Run();

        // The autumn ×1.5 on 5 culture a day: exactly 7.5.
        h.Queries.PointsPerDay.ShouldBe(Amount.FromMilli(7500));
    }

    [Fact]
    public void Run_LastDay_EndsFestivalRemovesEffectsAndEmitsEnded()
    {
        FestivalsHarness h = Running();
        EntityId ground = h.State.Active!.Value.Ground;
        FestivalId festival = h.Festival("test_harvest");
        h.Events();

        Amount total = Amount.Zero;
        for (int day = 0; day < 5; day++)
        {
            h.Run();
            total += h.Queries.PointsPerDay;
        }

        // Five days at 5 culture: 25 in all. The run is spent but still active through its last day.
        total.ShouldBe(Amount.FromUnits(25));
        h.State.Active.ShouldBe(new FestivalRun(festival, ground, 0));
        h.Queries.IsFestivalRunning().ShouldBeTrue();
        h.Events().ShouldBeEmpty();

        // The next step ends it: the effects leave the modifier index with the source.
        h.Run();
        h.State.Active.ShouldBeNull();
        h.State.FestivalRunning.ShouldBeFalse();
        h.Queries.IsFestivalRunning().ShouldBeFalse();
        h.Queries.ActiveFestival().ShouldBeNull();
        h.Events().ShouldHaveSingleItem().ShouldBeOfType<FestivalEnded>()
            .ShouldSatisfyAllConditions(
                e => e.Festival.ShouldBe(festival),
                e => e.Ground.ShouldBe(ground));
        // The festival's effects leave the modifier index with the source.
        h.Sim.Services.ProgressionService.RemovedSources.ShouldContain(EffectSource.ForFestival(festival));
    }

    [Fact]
    public void Run_AppliesEffectsWithFestivalSource()
    {
        FestivalsHarness h = Running();

        h.Sim.Services.ProgressionService.Calls.ShouldHaveSingleItem()
            .ShouldSatisfyAllConditions(
                c => c.Source.ShouldBe(EffectSource.ForFestival(h.Festival("test_harvest"))),
                c => c.Effects.ShouldBe(h.Content.Get(h.Festival("test_harvest")).Effects));
    }

    [Fact]
    public void Hold_FestivalDurationBonus_RunsLonger()
    {
        var h = FestivalsHarness.Create();
        h.Unlock("test_harvest");
        h.Stock("fish", 100).Stock("wood", 100);
        EntityId ground = h.AddGround(2, 2);
        // The Festival Calendar tradition: festivals last 50% longer.
        h.Sim.Queries.Progression.AddModifier(
            Stat.FestivalDuration,
            new ModifierContribution(EffectSource.ForTradition(TraditionId.From(1)), 50, true));
        h.Events();
        h.Send(new HoldFestival(ground, h.Festival("test_harvest")));

        // Five days at ×1.5 is 7.5, which rounds half up to 8 days.
        h.State.Active.ShouldBe(new FestivalRun(h.Festival("test_harvest"), ground, 8));
    }

    [Fact]
    public void FestivalState_SaveRoundTripMidRun_ContinuesDeterministically()
    {
        FestivalsHarness h = Running();
        h.Run();
        h.Run();
        ulong before = h.Hash();

        var resumed = FestivalsHarness.Reload(h);
        resumed.State.Active.ShouldBe(h.State.Active);
        resumed.Hash().ShouldBe(before);

        resumed.Run();
        h.Run();
        resumed.Hash().ShouldBe(h.Hash());
        resumed.State.Active!.Value.DaysLeft.ShouldBe(2);
    }
}
