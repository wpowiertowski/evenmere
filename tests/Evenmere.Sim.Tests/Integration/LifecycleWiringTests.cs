using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Roads.Commands;

namespace Evenmere.Sim.Tests.Integration;

/// <summary>
/// The roster subscribes every lifecycle listener in pipeline order (Roads, Workforce, Production, Logistics, Population),
/// and the pipeline works end to end on a new game: the hall exercises Roads directly because it is a storehouse, the hall's
/// carts serve the island pool, and houses, producers and markets placed after it prove the same wiring carries every later
/// building (Production creates its record, Population its house, Roads its reach and coverage).
/// </summary>
public sealed class LifecycleWiringTests
{
    [Fact]
    public void NewGame_RoadsHearsTheHall_AsAStorehouseInItsOwnReach()
    {
        Simulation sim = SiteFinder.NewGame();
        EntityId hall = ActiveBuildings(sim)[0].Id;

        sim.Query.Roads.IsInStorehouseReach(hall).ShouldBeTrue();
        sim.Query.Roads.TryNearestStorehouse(hall, out EntityId storehouse, out int cost).ShouldBeTrue();
        storehouse.ShouldBe(hall);
        cost.ShouldBe(0);
    }

    [Fact]
    public void NewGame_LogisticsSeesTheHallCarts()
    {
        Simulation sim = SiteFinder.NewGame();
        EntityId hall = ActiveBuildings(sim)[0].Id;

        sim.Query.Logistics.FreeCarts(hall).ShouldBe(3);
    }

    [Fact]
    public void NewGame_PlacedHouseIsHeardByPopulation()
    {
        Simulation sim = SiteFinder.NewGame();
        EntityId house = PlaceAndFinish(sim, "settler_house", 26, 96);

        sim.Query.Population.TryGetHouse(house, out HouseInfo info).ShouldBeTrue();
        info.Tier.ShouldBe(SiteFinder.Content.Required.Settlers);
        info.Residents.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void NewGame_PlacedProducerIsHeardByProduction()
    {
        Simulation sim = SiteFinder.NewGame();
        EntityId hut = PlaceAndFinish(sim, "woodcutters_hut", 38, 96);

        sim.Query.Production.TryGet(hut, out ProductionInfo info).ShouldBeTrue();
        info.Building.ShouldBe(hut);
    }

    [Fact]
    public void NewGame_PlacedMarketCoversTheHouse()
    {
        Simulation sim = SiteFinder.NewGame();
        EntityId house = PlaceAndFinish(sim, "settler_house", 26, 96);
        PlaceAndFinish(sim, "market", 34, 96);

        sim.Query.Roads.IsCovered(house, ServiceKind.Market).ShouldBeTrue();
    }

    // Lays a dirt road past the site, places the building beside it and ticks until the site completes. The road puts the
    // site in the hall's reach, so construction advances; the coordinates below are valid on the starter island.
    private static EntityId PlaceAndFinish(Simulation sim, string type, int x, int y)
    {
        BuildingTypeId road = SiteFinder.Content.Required.DirtRoad;
        List<TileCoord> path = [];
        for (int rx = 24; rx <= 40; rx++)
        {
            path.Add(new TileCoord(rx, 95));
        }

        sim.Enqueue(new PlaceRoad([.. path], road));
        sim.Tick();

        EntityId previous = EntityId.None;
        foreach (BuildingInfo building in sim.Query.Map.Buildings)
        {
            if (previous.IsNone || building.Id.Value > previous.Value)
            {
                previous = building.Id;
            }
        }

        sim.Enqueue(new PlaceBuilding(SiteFinder.Content.IdOf<BuildingTypeId>(type), x, y, Rotation.Deg0));
        sim.Tick();

        // Entity IDs are monotonic and logistics jobs take IDs of their own, so the new building is the first building
        // past the previous maximum to reach Active, not necessarily the very next ID.
        for (int i = 0; i < 600; i++)
        {
            EntityId ready = EntityId.None;
            foreach (BuildingInfo building in sim.Query.Map.Buildings)
            {
                if (building.Id.Value > previous.Value && building.State == BuildingState.Active
                    && (ready.IsNone || building.Id.Value < ready.Value))
                {
                    ready = building.Id;
                }
            }

            if (!ready.IsNone)
            {
                return ready;
            }

            sim.Tick();
        }

        Assert.Fail($"building {type} at ({x}, {y}) never completed");
        return EntityId.None;
    }

    private static List<BuildingInfo> ActiveBuildings(Simulation sim)
    {
        List<BuildingInfo> active = [];
        foreach (BuildingInfo building in sim.Query.Map.ActiveBuildings)
        {
            active.Add(building);
        }

        return active;
    }
}
