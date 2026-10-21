using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction;
using Evenmere.Sim.Systems.Construction.Commands;

namespace Evenmere.Sim.Tests.Construction;

/// <summary>In-place upgrades: the old building keeps working until the new type takes over.</summary>
public sealed class UpgradeTests
{
    private static CommandRejected Upgrade(ConstructionWorld world, EntityId building, string toType)
    {
        world.Enqueue(new UpgradeBuilding(building, ConstructionWorld.Type(toType)));
        world.Tick();
        return world.Rejection().ShouldNotBeNull();
    }

    [Fact]
    public void Upgrade_KeepsTheOldBuildingOperatingUntilTheNewTypeTakesOver()
    {
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        var activated = new List<BuildingInfo>();
        var typeChanged = new List<(BuildingInfo Building, BuildingTypeId Previous)>();
        world.Sim.Services.BuildingLifecycle.Subscribe(new RecordingListener(activated, [], typeChanged));
        EntityId market = world.Build("market", 5, 5);
        world.GrantReach(market);
        activated.Clear();
        world.Enqueue(new UpgradeBuilding(market, ConstructionWorld.Type("test_market_square")));
        world.Tick();

        // 120 ticks of work follow the placement tick; at every one of them the building is still an active market.
        SiteInfo started = world.Site(market);
        for (int tick = 0; tick < 119; tick++)
        {
            world.Tick();
            BuildingInfo during = world.Info(market);
            (during.State, during.Type, during.Stage).ShouldBe((BuildingState.Active, ConstructionWorld.Type("market"), 0));
        }

        SiteInfo almost = world.Site(market);
        typeChanged.ShouldBeEmpty();
        world.Tick();

        (started.Stage, started.Progress.PerMille).ShouldBe((0, 0));
        almost.Progress.PerMille.ShouldBe(991);
        BuildingInfo after = world.Info(market);
        (after.State, after.Type).ShouldBe((BuildingState.Active, ConstructionWorld.Type("test_market_square")));
        world.System.Queries.TryGetSite(market, out _).ShouldBeFalse();
        typeChanged.ShouldHaveSingleItem().ShouldBe((after, ConstructionWorld.Type("market")));
        activated.ShouldBeEmpty("an upgrade is not an activation");
        world.Stocked("timber").ShouldBe(Amount.FromUnits(6), "the upgrade drew the new type's bill of 4 timber");
        world.Sim.DrainEvents().OfType<BuildingUpgraded>().ShouldHaveSingleItem()
            .ShouldBe(new BuildingUpgraded(121, market, ConstructionWorld.Type("market"), ConstructionWorld.Type("test_market_square")));
    }

    [Fact]
    public void Upgrade_PausedBuilding_StaysPausedThroughTheUpgrade()
    {
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        EntityId market = world.Build("market", 5, 5);
        world.Sim.Services.BuildingRegistry.SetState(market, BuildingState.Paused);
        world.GrantReach(market);
        world.Enqueue(new UpgradeBuilding(market, ConstructionWorld.Type("test_market_square")));

        world.Tick(121);

        BuildingInfo after = world.Info(market);
        (after.State, after.Type).ShouldBe((BuildingState.Paused, ConstructionWorld.Type("test_market_square")));
    }

    [Fact]
    public void Upgrade_OutsideStorehouseReach_WaitsAndTheBuildingKeepsItsType()
    {
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        EntityId market = world.Build("market", 5, 5);
        world.Enqueue(new UpgradeBuilding(market, ConstructionWorld.Type("test_market_square")));

        world.Tick(30);

        world.Site(market).Stall.ShouldBe(StallReason.NoStorehouseReach);
        world.Info(market).Type.ShouldBe(ConstructionWorld.Type("market"));
        world.Stocked("timber").ShouldBe(Amount.FromUnits(10));
    }

    [Fact]
    public void Upgrade_MissingMaterial_StallsTheUpgradeNotTheBuilding()
    {
        var world = ConstructionWorld.Create();
        EntityId market = world.Build("market", 5, 5);
        world.GrantReach(market);
        world.Enqueue(new UpgradeBuilding(market, ConstructionWorld.Type("test_market_square")));

        world.Tick(5);

        SiteInfo site = world.Site(market);
        (site.Stall, site.WaitingFor).ShouldBe((StallReason.MissingMaterial, ConstructionWorld.Good("timber")));
        world.Info(market).State.ShouldBe(BuildingState.Active);
    }

    [Fact]
    public void Upgrade_TypeThatIsNotAnUpgradeOfTheBuilding_IsRefusedAsNotApplicable()
    {
        var world = ConstructionWorld.Create();
        EntityId market = world.Build("market", 5, 5);
        EntityId sawpit = world.Build("sawpit", 10, 10);

        Upgrade(world, market, "storehouse").Code.ShouldBe(RejectReason.NotApplicable);
        Upgrade(world, sawpit, "test_market_square").Code.ShouldBe(RejectReason.NotApplicable);
        world.State.Sites.Count.ShouldBe(0);
    }

    [Fact]
    public void Upgrade_House_IsRefusedBecausePopulationChangesHouseTiers()
    {
        var world = ConstructionWorld.Create();
        EntityId house = world.Build("settler_house", 5, 5);

        CommandRejected rejection = Upgrade(world, house, "test_big_house");

        rejection.Code.ShouldBe(RejectReason.NotApplicable);
        rejection.Detail.ShouldContain("Population");
    }

    [Fact]
    public void Upgrade_TypeWithAnotherFootprint_IsRefusedBecauseTheTypeChangesInPlace()
    {
        var world = ConstructionWorld.Create();
        EntityId market = world.Build("market", 5, 5);

        Upgrade(world, market, "test_big_market").Code.ShouldBe(RejectReason.NotApplicable);
    }

    [Fact]
    public void Upgrade_SiteThatIsNotFinished_IsRefusedAsNotApplicable()
    {
        var world = ConstructionWorld.Create();
        EntityId site = world.Build("market", 5, 5, BuildingState.Site);

        Upgrade(world, site, "test_market_square").Code.ShouldBe(RejectReason.NotApplicable);
    }

    [Fact]
    public void Upgrade_AlreadyUnderWay_IsRefusedAsInvalid()
    {
        var world = ConstructionWorld.Create();
        EntityId market = world.Build("market", 5, 5);
        world.Enqueue(new UpgradeBuilding(market, ConstructionWorld.Type("test_market_square")));
        world.Tick();
        world.Sim.DrainEvents();

        Upgrade(world, market, "test_market_square").Code.ShouldBe(RejectReason.Invalid);
    }

    [Fact]
    public void Upgrade_UnknownBuildingOrType_IsRefused()
    {
        var world = ConstructionWorld.Create();
        EntityId market = world.Build("market", 5, 5);

        Upgrade(world, new EntityId(99), "test_market_square").Code.ShouldBe(RejectReason.UnknownTarget);
        world.Enqueue(new UpgradeBuilding(market, new BuildingTypeId(9999)));
        world.Tick();
        world.Rejection().ShouldNotBeNull().Code.ShouldBe(RejectReason.Invalid);
    }

    [Fact]
    public void Upgrade_TypeNotUnlocked_IsRefusedAsAPlacementError()
    {
        var world = ConstructionWorld.Create(unlockAll: false);
        world.Sim.Queries.Progression.Unlock(ConstructionWorld.Type("market"));
        EntityId market = world.Build("market", 5, 5);

        CommandRejected rejection = Upgrade(world, market, "test_market_square");

        rejection.Code.ShouldBe(RejectReason.PlacementInvalid);
        rejection.Detail.ShouldContain("NotUnlocked");
    }
}
