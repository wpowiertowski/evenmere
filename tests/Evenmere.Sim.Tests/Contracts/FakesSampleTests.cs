using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Advisors.Commands;
using Evenmere.Sim.Systems.Production;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Contracts;

/// <summary>
/// Shows how a system task unit-tests its system in isolation: build a <see cref="FakeSim"/>, arrange the world through its
/// fakes, run the system against <see cref="FakeSim.Context"/>, and assert on the events and on the fakes. The system under
/// test here is a toy defined below, so these tests also prove the fakes behave like the real services where it matters.
/// </summary>
public sealed class FakesSampleTests
{
    private static BuildingTypeId Hut => WorldBuilder.Content.IdOf<BuildingTypeId>("woodcutters_hut");

    private static BuildingTypeId House => WorldBuilder.Content.IdOf<BuildingTypeId>("settler_house");

    private static GoodId Timber => WorldBuilder.Content.IdOf<GoodId>("timber");

    [Fact]
    public void SampleSystem_BuildingOutsideReach_StallsAndReportsItOnce()
    {
        // Arrange: two finished buildings, only one of them inside a storehouse's reach, and one site that is ignored.
        var fake = FakeSim.Create(tick: 120);
        fake.Queries.Map.AddBuilding(new EntityId(1), Hut, 0, 0, 2, 2);
        fake.Queries.Map.AddBuilding(new EntityId(2), Hut, 4, 0, 2, 2);
        fake.Queries.Map.AddBuilding(new EntityId(3), Hut, 8, 0, 2, 2, BuildingState.Site);
        fake.Queries.Roads.SetReach(new EntityId(9), new EntityId(1), 3000);
        var system = new ReachWatcher();

        // Act: run on two consecutive days.
        system.Run(fake.Context);
        fake.SetTick(240);
        system.Run(fake.Context);

        // Assert: only building 2 stalled, and the second day reported nothing new.
        ProductionStalled stalled = fake.DrainEvents().OfType<ProductionStalled>().ShouldHaveSingleItem();
        stalled.Building.ShouldBe(new EntityId(2));
        stalled.Reason.ShouldBe(StallReason.NoStorehouseReach);
        stalled.Tick.ShouldBe(120);
    }

    [Fact]
    public void SampleSystem_ReachGainedLater_ResumesProduction()
    {
        var fake = FakeSim.Create(tick: 120);
        fake.Queries.Map.AddBuilding(new EntityId(1), Hut, 0, 0, 2, 2);
        var system = new ReachWatcher();
        system.Run(fake.Context);

        fake.Queries.Roads.SetReach(new EntityId(9), new EntityId(1), 2000);
        fake.SetTick(240);
        system.Run(fake.Context);

        SimEvent[] events = [.. fake.DrainEvents()];
        events[0].ShouldBeOfType<ProductionStalled>();
        ProductionResumed resumed = events[1].ShouldBeOfType<ProductionResumed>();
        resumed.Building.ShouldBe(new EntityId(1));
        resumed.Tick.ShouldBe(240);
    }

    [Fact]
    public void FakeRegistry_Add_ChoosesTheFootprintFromContentAndRotation()
    {
        var fake = FakeSim.Create();
        BuildingTypeId storehouse = WorldBuilder.Content.IdOf<BuildingTypeId>("storehouse");

        EntityId id = fake.Services.BuildingRegistry.Add(storehouse, 3, 4, Rotation.Deg90, BuildingState.Site);

        fake.Queries.Map.TryGetBuilding(id, out BuildingInfo info).ShouldBeTrue();
        Footprint defined = WorldBuilder.Content.Get(storehouse).Footprint;
        (info.Width, info.Height).ShouldBe((defined.Height, defined.Width));
        info.State.ShouldBe(BuildingState.Site);
        fake.Queries.Map.BuildingAt(4, 5).ShouldBe(id);
        fake.Queries.Map.BuildingAt(3 + info.Width, 4).ShouldBe(EntityId.None);
    }

    [Fact]
    public void FakeRegistry_Overlap_IsRefusedLikeTheRealOne()
    {
        var fake = FakeSim.Create();
        fake.Services.BuildingRegistry.Add(House, 5, 5, Rotation.Deg0, BuildingState.Active);

        Should.Throw<ArgumentException>(() => fake.Services.BuildingRegistry.Add(House, 6, 6, Rotation.Deg0, BuildingState.Site));
        Should.Throw<ArgumentException>(() => fake.Services.BuildingRegistry.Add(House, 31, 31, Rotation.Deg0, BuildingState.Site));
    }

    [Fact]
    public void FakeRegistry_Lifecycle_NotifiesListenersInSubscriptionOrder()
    {
        var fake = FakeSim.Create();
        var log = new List<string>();
        fake.Services.BuildingLifecycle.Subscribe(new LoggingListener("first", log));
        fake.Services.BuildingLifecycle.Subscribe(new LoggingListener("second", log));
        FakeBuildingRegistry registry = fake.Services.BuildingRegistry;

        EntityId id = registry.Add(House, 0, 0, Rotation.Deg0, BuildingState.Site);
        registry.SetState(id, BuildingState.Active);
        registry.SetState(id, BuildingState.Paused);
        registry.SetState(id, BuildingState.Active);
        registry.SetType(id, WorldBuilder.Content.IdOf<BuildingTypeId>("storehouse"));
        registry.Remove(id).ShouldBeTrue();
        registry.Remove(id).ShouldBeFalse();

        // A site raises nothing until it activates; pausing and resuming raise nothing; a removal reaches both listeners.
        log.ShouldBe(
        [
            $"first activated {id.Value}", $"second activated {id.Value}",
            $"first type-changed {id.Value}", $"second type-changed {id.Value}",
            $"first removed {id.Value}", $"second removed {id.Value}",
        ]);
    }

    [Fact]
    public void FakeServices_CountersLedgerAndSupply_AreReadableThroughTheQueries()
    {
        var fake = FakeSim.Create();
        var tier = new TierId(0);

        fake.Services.Counters.Add(CounterKey.Produced(Timber), 3000);
        fake.Services.Counters.Add(CounterKey.Produced(Timber), 2000);
        fake.Services.PointsLedger.Credit(PointKind.Knowledge, Amount.FromUnits(6));
        fake.Services.PointsLedger.Credit(PointKind.Knowledge, Amount.FromUnits(4));
        fake.Services.WorkforceSupply.SetSupply(tier, 40);

        fake.Queries.Progression.Counter(CounterKey.Produced(Timber)).ShouldBe(5000);
        fake.Services.PointsLedger.TakeToday(PointKind.Knowledge).ShouldBe(Amount.FromUnits(10));
        fake.Services.PointsLedger.TakeToday(PointKind.Knowledge).ShouldBe(Amount.Zero);
        fake.Queries.Workforce.Supply(tier).ShouldBe(40);
    }

    [Fact]
    public void FakeStorage_IsTheSamePoolForQueriesAndService()
    {
        var fake = FakeSim.Create();

        fake.Context.Storage.Add(Timber, Amount.FromUnits(5));

        fake.Queries.Storage.Stock(Timber).ShouldBe(Amount.FromUnits(5));
        fake.Context.Storage.TryTake(Timber, Amount.FromUnits(6)).ShouldBeFalse();
        fake.Context.Storage.TryTake(Timber, Amount.FromUnits(5)).ShouldBeTrue();
        fake.Queries.Storage.Stock(Timber).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void FakeProductionBuffers_ListRequests_AreSortedByTickThenBuilding()
    {
        var buffers = new FakeProductionBuffers();
        buffers.AddRequest(new TransportRequest(new EntityId(5), TransportDirection.Pickup, Timber, Amount.FromUnits(2), 10));
        buffers.AddRequest(new TransportRequest(new EntityId(2), TransportDirection.Delivery, Timber, Amount.FromUnits(2), 10));
        buffers.AddRequest(new TransportRequest(new EntityId(9), TransportDirection.Pickup, Timber, Amount.FromUnits(2), 4));
        var listed = new List<TransportRequest>();

        buffers.ListRequests(listed);

        listed.Select(static r => r.Building.Value).ShouldBe([9, 2, 5]);
    }

    [Fact]
    public void FakeSim_CommandHandler_RunsAgainstTheFakeContext()
    {
        // A command handler is tested the same way as a system: call Validate and Apply with the fake context.
        var fake = FakeSim.Create(tick: 7);
        var handler = new NotImplementedCommandHandler<DismissAdvice>("M1-15");

        CommandResult result = handler.Validate(fake.Context, new DismissAdvice(1));

        result.IsRejected.ShouldBeTrue();
        fake.Context.Tick.ShouldBe(7);
    }

    /// <summary>A toy system: it reads the map and roads through their queries and reports a building's reach changing, once per change.</summary>
    private sealed class ReachWatcher
    {
        private readonly HashSet<EntityId> _stalled = [];

        public void Run(SimContext ctx)
        {
            foreach (BuildingInfo building in ctx.Queries.Map.ActiveBuildings)
            {
                bool inReach = ctx.Queries.Roads.IsInStorehouseReach(building.Id);
                if (!inReach && _stalled.Add(building.Id))
                {
                    ctx.Events.Emit(new ProductionStalled(ctx.Tick, building.Id, StallReason.NoStorehouseReach));
                }
                else if (inReach && _stalled.Remove(building.Id))
                {
                    ctx.Events.Emit(new ProductionResumed(ctx.Tick, building.Id));
                }
            }
        }
    }

    private sealed class LoggingListener(string name, List<string> log) : IBuildingLifecycleListener
    {
        public void OnActivated(SimContext ctx, BuildingInfo building) => log.Add($"{name} activated {building.Id.Value}");

        public void OnRemoved(SimContext ctx, BuildingInfo building) => log.Add($"{name} removed {building.Id.Value}");

        public void OnTypeChanged(SimContext ctx, BuildingInfo building, BuildingTypeId previousType) =>
            log.Add($"{name} type-changed {building.Id.Value}");
    }
}
