using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Construction;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Tests.Architecture;

namespace Evenmere.Sim.Tests.Construction;

/// <summary>Construction's saved state: the architecture guards, a save round trip mid-construction, and the pre-built hall.</summary>
public sealed class ConstructionStateTests
{
    [Fact]
    public void ConstructionState_PassesTheArchitectureGuards()
    {
        StateTypeGraph graph = StateTypeWalker.Walk(typeof(ConstructionState));

        graph.Contains(typeof(SiteRecord)).ShouldBeTrue();
        ForbiddenFieldTypeRules.Check(graph).ShouldBeEmpty();
        MessagePackKeyRules.Check(graph).ShouldBeEmpty();
    }

    [Fact]
    public void ConstructionState_SaveRoundTripMidConstruction_ResumesExactlyWhereItLeftOff()
    {
        // A stalled site (one timber is three of ten steps), an advancing upgrade and a site waiting for reach.
        var original = ConstructionWorld.Create();
        original.Stock("timber", "1");
        EntityId lodge = original.Place("sawpit", 5, 5);
        original.GrantReach(lodge);
        EntityId market = original.Build("market", 10, 10);
        original.Enqueue(new UpgradeBuilding(market, ConstructionWorld.Type("test_market_square")));
        EntityId lonely = original.Place("sawpit", 20, 20);
        original.Tick(40);
        original.Site(lodge).Stall.ShouldBe(StallReason.MissingMaterial);
        original.Site(lonely).Stall.ShouldBe(StallReason.NoStorehouseReach);

        byte[] bytes = StateSerializer.Write(original.Sim.World);
        WorldState restored = StateSerializer.Read(bytes, ConstructionWorld.Content);

        StateSerializer.ComputeHash(restored).ShouldBe(StateSerializer.ComputeHash(original.Sim.World));
        restored.Construction.Sites.Count.ShouldBe(3);

        // Rebuild the same world around the restored state and run both on: they must stay in lockstep.
        var resumed = ConstructionWorld.Create(restored.Construction);
        resumed.Sim.SetTick(original.Now);
        resumed.Build("sawpit", 5, 5, BuildingState.Site);
        resumed.Build("market", 10, 10);
        resumed.Build("sawpit", 20, 20, BuildingState.Site);
        resumed.Sim.Services.BuildingRegistry.SetStage(lodge, original.Info(lodge).Stage);
        resumed.Sim.Services.BuildingRegistry.SetStage(lonely, original.Info(lonely).Stage);
        resumed.GrantReach(lodge);
        resumed.Sim.Services.Storage.Set(ConstructionWorld.Good("timber"), original.Stocked("timber"));
        original.Sim.DrainEvents();

        for (int tick = 0; tick < 200; tick++)
        {
            if (tick == 20)
            {
                original.Stock("timber", "20");
                resumed.Stock("timber", "20");
                original.GrantReach(market);
                resumed.GrantReach(market);
            }

            original.Tick();
            resumed.Tick();
            foreach (EntityId id in new[] { lodge, market, lonely })
            {
                original.System.Queries.TryGetSite(id, out SiteInfo expected).ShouldBe(resumed.System.Queries.TryGetSite(id, out SiteInfo actual));
                actual.ShouldBe(expected, $"{id} at tick {original.Now}");
            }
        }

        StateSerializer.ComputeHash(resumed.Sim.World).ShouldBe(StateSerializer.ComputeHash(original.Sim.World));
        original.Info(lodge).State.ShouldBe(BuildingState.Active);
        original.Info(market).Type.ShouldBe(ConstructionWorld.Type("test_market_square"));
        resumed.Sim.DrainEvents().OfType<ConstructionCompleted>().Select(static e => (e.Tick, e.Building))
            .ShouldBe(original.Sim.DrainEvents().OfType<ConstructionCompleted>().Select(static e => (e.Tick, e.Building)));
    }

    [Fact]
    public void PlacePreBuilt_FoundersHall_IsActiveAtOnceWithNoSiteAndNoEvent()
    {
        var world = ConstructionWorld.Create();
        var activated = new List<BuildingInfo>();
        world.Sim.Services.BuildingLifecycle.Subscribe(new RecordingListener(activated, [], []));

        EntityId hall = ConstructionSystem.PlacePreBuilt(world.Sim.Context, ConstructionWorld.Type("founders_hall"), 10, 10, Rotation.Deg90);

        BuildingInfo info = world.Info(hall);
        (info.State, info.Type, info.Origin, info.Rotation, info.Stage).ShouldBe(
            (BuildingState.Active, ConstructionWorld.Type("founders_hall"), new TileCoord(10, 10), Rotation.Deg90, 0));
        activated.ShouldHaveSingleItem().Id.ShouldBe(hall);
        world.State.Sites.Count.ShouldBe(0);
        world.Sim.DrainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void PlacePreBuilt_RoadTypeOrBlockedFootprint_Throws()
    {
        var world = ConstructionWorld.Create();
        ConstructionSystem.PlacePreBuilt(world.Sim.Context, ConstructionWorld.Type("founders_hall"), 10, 10, Rotation.Deg0);

        Should.Throw<ArgumentException>(() =>
            ConstructionSystem.PlacePreBuilt(world.Sim.Context, ConstructionWorld.Type("dirt_road"), 1, 1, Rotation.Deg0));
        Should.Throw<ArgumentException>(() =>
            ConstructionSystem.PlacePreBuilt(world.Sim.Context, ConstructionWorld.Type("founders_hall"), 12, 12, Rotation.Deg0));
    }
}
