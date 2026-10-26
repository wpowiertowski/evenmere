using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Culture;
using Evenmere.Sim.Systems.Culture.Festivals;
using Evenmere.Sim.Systems.Culture.Festivals.Commands;

namespace Evenmere.Sim.Tests.Culture.Festivals;

/// <summary>Every <c>HoldFestival</c> validation rule: locked, wrong season, one running, held this year, goods short.</summary>
public sealed class HoldFestivalTests
{
    private static FestivalsHarness Ready()
    {
        var h = FestivalsHarness.Create();
        h.Unlock("test_harvest", "test_winter");
        h.Stock("fish", 100).Stock("wood", 100);
        h.AddGround(2, 2);
        h.Events();
        return h;
    }

    private static EntityId GroundOf(FestivalsHarness h)
    {
        foreach (BuildingInfo building in h.Sim.Queries.Map.ActiveBuildings)
        {
            if (building.Type == h.Building("test_festival_ground"))
            {
                return building.Id;
            }
        }

        throw new InvalidOperationException("The harness placed no festival ground.");
    }

    [Fact]
    public void Hold_ReadyFestival_TakesGoodsStartsRunAndEmitsStarted()
    {
        FestivalsHarness h = Ready();
        EntityId ground = GroundOf(h);

        h.Send(new HoldFestival(ground, h.Festival("test_harvest")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<FestivalStarted>()
            .ShouldSatisfyAllConditions(
                e => e.Festival.ShouldBe(h.Festival("test_harvest")),
                e => e.Ground.ShouldBe(ground));
        h.State.Active.ShouldBe(new FestivalRun(h.Festival("test_harvest"), ground, 5));
        h.State.FestivalRunning.ShouldBeTrue();
        // Discounted by nothing: 20 fish and 10 wood leave the pool.
        h.Sim.Services.Storage.Get(h.Good("fish")).ShouldBe(Amount.FromUnits(80));
        h.Sim.Services.Storage.Get(h.Good("wood")).ShouldBe(Amount.FromUnits(90));
        // The type is booked for the year and counted once.
        h.Sim.Queries.Progression.Counter(CounterKey.FestivalsHeld).ShouldBe(1);
        h.Queries.FestivalsHeldInYear(h.Festival("test_harvest"), 0).ShouldBe(1);
        h.Queries.FestivalsHeldInYear(h.Festival("test_harvest"), 1).ShouldBe(0);
        h.Queries.IsFestivalRunning().ShouldBeTrue();
        h.Queries.ActiveFestival().ShouldBe(new FestivalRun(h.Festival("test_harvest"), ground, 5));
    }

    [Fact]
    public void Hold_LockedFestival_IsRejected()
    {
        var h = FestivalsHarness.Create();
        h.Stock("fish", 100).Stock("wood", 100);
        EntityId ground = h.AddGround(2, 2);
        h.Events();

        h.Send(new HoldFestival(ground, h.Festival("test_harvest")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.FestivalNotUnlocked);
        h.State.Active.ShouldBeNull();
        h.State.FestivalRunning.ShouldBeFalse();
    }

    [Fact]
    public void Hold_WrongSeason_IsRejected()
    {
        FestivalsHarness h = Ready();
        EntityId ground = GroundOf(h);

        // Spring (tick 0) is not winter.
        h.Send(new HoldFestival(ground, h.Festival("test_winter")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.WrongSeason);
        h.State.Active.ShouldBeNull();
    }

    [Fact]
    public void Hold_SecondFestivalWhileOneRuns_IsRejected()
    {
        FestivalsHarness h = Ready();
        EntityId ground = GroundOf(h);
        h.Send(new HoldFestival(ground, h.Festival("test_harvest")));
        h.Events();

        h.Send(new HoldFestival(ground, h.Festival("test_harvest")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.FestivalAlreadyRunning);
    }

    [Fact]
    public void Hold_SameTypeTwiceInOneYear_IsRejected()
    {
        FestivalsHarness h = Ready();
        EntityId ground = GroundOf(h);
        h.Send(new HoldFestival(ground, h.Festival("test_harvest")));
        // The five-day run ends on the sixth step, still in year 0 (a year is 40 days).
        for (int day = 0; day < 6; day++)
        {
            h.Run();
        }

        h.Events();
        h.Send(new HoldFestival(ground, h.Festival("test_harvest")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.FestivalHeldThisYear);
    }

    [Fact]
    public void Hold_NextYear_AllowsTheTypeAgain()
    {
        FestivalsHarness h = Ready();
        EntityId ground = GroundOf(h);
        h.Send(new HoldFestival(ground, h.Festival("test_harvest")));
        for (int day = 0; day < 6; day++)
        {
            h.Run();
        }

        // Year 1 starts on day 40; restock first, then the same type may be held again.
        h.Stock("fish", 100).Stock("wood", 100);
        h.SetTick(40 * 120);
        h.Events();
        h.Send(new HoldFestival(ground, h.Festival("test_harvest")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<FestivalStarted>();
        h.Sim.Queries.Progression.Counter(CounterKey.FestivalsHeld).ShouldBe(2);
    }

    [Fact]
    public void Hold_GoodsShort_IsRejected()
    {
        var h = FestivalsHarness.Create();
        h.Unlock("test_harvest");
        h.Stock("fish", 5).Stock("wood", 100);
        EntityId ground = h.AddGround(2, 2);
        h.Events();

        h.Send(new HoldFestival(ground, h.Festival("test_harvest")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.FestivalGoodsShort);
        h.State.Active.ShouldBeNull();
        h.Sim.Services.Storage.Get(h.Good("fish")).ShouldBe(Amount.FromUnits(5));
    }

    [Fact]
    public void Hold_UnknownGround_IsRejected()
    {
        FestivalsHarness h = Ready();

        h.Send(new HoldFestival(new EntityId(999), h.Festival("test_harvest")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.UnknownTarget);
        h.State.Active.ShouldBeNull();
    }

    [Fact]
    public void Hold_GroundOfWrongType_IsRejected()
    {
        FestivalsHarness h = Ready();
        // The minimal pack's storehouse is finished but carries no festival_ground tag.
        EntityId store = h.Sim.Services.BuildingRegistry.Add(h.Building("storehouse"), 6, 6, Rotation.Deg0, BuildingState.Active);
        h.Events();

        h.Send(new HoldFestival(store, h.Festival("test_harvest")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.Invalid);
        h.State.Active.ShouldBeNull();
    }

    [Fact]
    public void Hold_UnfinishedGround_IsRejected()
    {
        FestivalsHarness h = Ready();
        EntityId site = h.AddGround(6, 6, state: BuildingState.Site);
        h.Events();

        h.Send(new HoldFestival(site, h.Festival("test_harvest")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe(RejectReason.Invalid);
        h.State.Active.ShouldBeNull();
    }

    [Fact]
    public void Hold_DiscountedCost_TakesLess()
    {
        FestivalsHarness h = Ready();
        EntityId ground = GroundOf(h);
        // The Harvest Festival tradition: festival goods cost 25% less.
        h.Sim.Queries.Progression.AddModifier(
            Stat.FestivalCost,
            new ModifierContribution(EffectSource.ForTradition(new TraditionId(1)), -25, true));
        h.Events();

        h.Send(new HoldFestival(ground, h.Festival("test_harvest")));

        h.Events().ShouldHaveSingleItem().ShouldBeOfType<FestivalStarted>();
        // 20 fish and 10 wood at 75%: 15 fish and 7.5 wood (amounts round down).
        h.Sim.Services.Storage.Get(h.Good("fish")).ShouldBe(Amount.FromUnits(85));
        h.Sim.Services.Storage.Get(h.Good("wood")).ShouldBe(Amount.FromMilli(92500));
    }
}
