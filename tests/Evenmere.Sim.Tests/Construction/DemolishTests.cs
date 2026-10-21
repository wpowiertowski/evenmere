using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction;
using Evenmere.Sim.Systems.Construction.Commands;

namespace Evenmere.Sim.Tests.Construction;

/// <summary>The exact refunds, the protected hall, and what a demolition tells the rest of the simulation.</summary>
public sealed class DemolishTests
{
    private static void Demolish(ConstructionWorld world, EntityId building)
    {
        world.Enqueue(new Demolish(building));
        world.Tick();
    }

    [Fact]
    public void Demolish_SiteMidBuild_RefundsEverythingItDrew()
    {
        // After 30 ticks of work two steps are done and the third is drawn: 0.9 timber and 0.3 wood.
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        world.Stock("wood", "5");
        EntityId id = world.Place("test_lodge", 5, 5);
        world.GrantReach(id);
        world.Tick(30);
        world.Stocked("timber").ShouldBe(Amount.Parse("9.1"));
        world.Stocked("wood").ShouldBe(Amount.Parse("4.7"));
        world.Sim.DrainEvents();

        Demolish(world, id);

        world.Stocked("timber").ShouldBe(Amount.FromUnits(10));
        world.Stocked("wood").ShouldBe(Amount.FromUnits(5));
        world.Sim.Queries.Map.TryGetBuilding(id, out _).ShouldBeFalse();
        world.State.Sites.Count.ShouldBe(0);
        world.Sim.DrainEvents().OfType<BuildingDemolished>().ShouldHaveSingleItem()
            .ShouldBe(new BuildingDemolished(32, id, ConstructionWorld.Type("test_lodge"), new TileCoord(5, 5)));
    }

    [Fact]
    public void Demolish_SiteBeforeItDrewAnything_RefundsNothingAndStillRemovesIt()
    {
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        EntityId id = world.Place("sawpit", 5, 5);

        Demolish(world, id);

        world.Stocked("timber").ShouldBe(Amount.FromUnits(10));
        world.Sim.Queries.Map.BuildingAt(5, 5).IsNone.ShouldBeTrue();
    }

    [Fact]
    public void Demolish_ActiveBuilding_RefundsHalfTheBillRoundedDownPerGood()
    {
        // 3.001 timber halves to 1.5005, which rounds down to 1.500; 1 wood halves to 0.5.
        var world = ConstructionWorld.Create();
        EntityId id = world.Build("test_lodge", 5, 5);

        Demolish(world, id);

        world.Stocked("timber").ShouldBe(Amount.Parse("1.5"));
        world.Stocked("wood").ShouldBe(Amount.Parse("0.5"));
    }

    [Fact]
    public void Demolish_PausedBuilding_RefundsHalfToo()
    {
        var world = ConstructionWorld.Create();
        EntityId id = world.Build("sawpit", 5, 5, BuildingState.Paused);

        Demolish(world, id);

        world.Stocked("timber").ShouldBe(Amount.Parse("1.5"));
    }

    [Fact]
    public void Demolish_BuildingThatCostsNothing_RefundsNothing()
    {
        var world = ConstructionWorld.Create();
        EntityId id = world.Build("dirt_road", 5, 5);

        Demolish(world, id);

        world.Sim.Queries.Map.TryGetBuilding(id, out _).ShouldBeFalse();
        world.Stocked("timber").ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Demolish_FoundersHall_IsRefusedAsProtected()
    {
        var world = ConstructionWorld.Create();
        EntityId hall = world.Build("founders_hall", 10, 10);

        Demolish(world, hall);

        world.Rejection().ShouldNotBeNull().Code.ShouldBe(RejectReason.Protected);
        world.Info(hall).State.ShouldBe(BuildingState.Active);
    }

    [Fact]
    public void Demolish_UniqueBuildingThatIsNotAvailableFromTheStart_IsAllowed()
    {
        var world = ConstructionWorld.Create();
        EntityId shrine = world.Build("test_unique_shrine", 10, 10);

        Demolish(world, shrine);

        world.Sim.Queries.Map.TryGetBuilding(shrine, out _).ShouldBeFalse();
    }

    [Fact]
    public void Demolish_StartBuildingThatIsNotUnique_IsAllowed()
    {
        var world = ConstructionWorld.Create();
        EntityId storehouse = world.Build("storehouse", 10, 10);

        Demolish(world, storehouse);

        world.Sim.Queries.Map.TryGetBuilding(storehouse, out _).ShouldBeFalse();
    }

    [Fact]
    public void Demolish_UnknownBuilding_IsRefusedAsUnknownTarget()
    {
        var world = ConstructionWorld.Create();

        Demolish(world, new EntityId(42));

        world.Rejection().ShouldNotBeNull().Code.ShouldBe(RejectReason.UnknownTarget);
    }

    [Fact]
    public void Demolish_Ruin_ClearsItWithoutARefund()
    {
        var world = ConstructionWorld.Create();
        EntityId ruin = world.Build("sawpit", 5, 5, BuildingState.Ruin);

        Demolish(world, ruin);

        world.Sim.Queries.Map.TryGetBuilding(ruin, out _).ShouldBeFalse();
        world.Stocked("timber").ShouldBe(Amount.Zero);
        world.Sim.DrainEvents().OfType<BuildingDemolished>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Demolish_TellsTheLifecycleListenersTheBuildingIsGone()
    {
        var world = ConstructionWorld.Create();
        var removed = new List<BuildingInfo>();
        world.Sim.Services.BuildingLifecycle.Subscribe(new RecordingListener([], removed, []));
        EntityId id = world.Build("sawpit", 5, 5);
        BuildingInfo before = world.Info(id);

        Demolish(world, id);

        removed.ShouldHaveSingleItem().ShouldBe(before);
    }

    [Fact]
    public void Demolish_BuildingWithAnUpgradeUnderWay_RefundsTheUpgradeInFullAndTheBuildingByHalf()
    {
        // The market cost 6 timber (half back: 3). The upgrade drew two steps of 0.4 by tick 14.
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        EntityId market = world.Build("market", 5, 5);
        world.GrantReach(market);
        world.Enqueue(new UpgradeBuilding(market, ConstructionWorld.Type("test_market_square")));
        world.Tick(14);
        world.Stocked("timber").ShouldBe(Amount.Parse("9.2"));

        Demolish(world, market);

        world.Stocked("timber").ShouldBe(Amount.FromUnits(13));
        world.State.Sites.Count.ShouldBe(0);
    }
}
