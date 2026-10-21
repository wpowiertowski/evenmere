using Evenmere.Core.Ids;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Roads.Commands;
using Evenmere.Tools.Balance;

namespace Evenmere.Tools.Tests.Balance;

/// <summary>Plans connector roads and measures road distances on the starter island.</summary>
public sealed class RoadPlannerTests
{
    [Fact]
    public void FindConnector_MarketNearHall_ReachesHallDoorstep()
    {
        var sim = Simulation.CreateNew(BotWorld.Content, new NewGameOptions { Seed = 1, Map = BotWorld.Map });
        Evenmere.Content.ContentDb content = BotWorld.Content;
        BuildingTypeId dirt = content.Buildings.IdOf("dirt_road");
        BuildingTypeId market = content.Buildings.IdOf("market");
        BuildingInfo hall = FindBuilding(sim, "founders_hall");

        PlacementCandidate? candidate = PlacementSearch.Find(sim, market, hall.Origin.X, hall.Origin.Y, 3, 3);
        candidate.ShouldNotBeNull();
        TileCoord[]? road = RoadPlanner.FindConnector(
            sim, content, dirt, candidate.X, candidate.Y, 3, 3,
            hall.Origin.X, hall.Origin.Y, hall.Width, hall.Height);

        road.ShouldNotBeNull();
        road.Length.ShouldBeGreaterThan(0);
        PlacementResult validation = sim.Query.Placement.ValidateRoad(road, dirt);
        validation.CanPlace.ShouldBeTrue();
    }

    [Fact]
    public void FindConnector_AdjacentRoad_ReturnsEmpty()
    {
        var sim = Simulation.CreateNew(BotWorld.Content, new NewGameOptions { Seed = 1, Map = BotWorld.Map });
        Evenmere.Content.ContentDb content = BotWorld.Content;
        BuildingTypeId dirt = content.Buildings.IdOf("dirt_road");
        BuildingTypeId market = content.Buildings.IdOf("market");
        BuildingInfo hall = FindBuilding(sim, "founders_hall");

        // Lay a road, then ask for a connector to a footprint beside it: nothing is left to lay.
        TileCoord[] laid = [new TileCoord(hall.Origin.X + hall.Width, hall.Origin.Y + 1) ];
        sim.Enqueue(new PlaceRoad(laid, dirt));
        sim.Enqueue(new PlaceBuilding(market, hall.Origin.X + hall.Width + 1, hall.Origin.Y, Rotation.Deg0));
        sim.Tick();
        sim.DrainEvents();

        TileCoord[]? road = RoadPlanner.FindConnector(
            sim, content, dirt, hall.Origin.X + hall.Width + 1, hall.Origin.Y, 3, 3,
            hall.Origin.X, hall.Origin.Y, hall.Width, hall.Height);

        road.ShouldNotBeNull();
        road.Length.ShouldBe(0);
    }

    [Fact]
    public void RoadDistance_ConnectedRoads_MeasuresTiles()
    {
        var sim = Simulation.CreateNew(BotWorld.Content, new NewGameOptions { Seed = 1, Map = BotWorld.Map });
        Evenmere.Content.ContentDb content = BotWorld.Content;
        BuildingTypeId dirt = content.Buildings.IdOf("dirt_road");
        BuildingInfo hall = FindBuilding(sim, "founders_hall");
        TileCoord[] path =
        [
            new TileCoord(hall.Origin.X + hall.Width, hall.Origin.Y),
            new TileCoord(hall.Origin.X + hall.Width + 1, hall.Origin.Y),
            new TileCoord(hall.Origin.X + hall.Width + 2, hall.Origin.Y),
        ];
        sim.Enqueue(new PlaceRoad(path, dirt));
        sim.Tick();
        sim.DrainEvents();

        int? distance = RoadPlanner.RoadDistance(sim, [path[0]], [path[2]]);

        distance.ShouldBe(2);
    }

    [Fact]
    public void RoadDistance_DisconnectedRoads_ReturnsNull()
    {
        var sim = Simulation.CreateNew(BotWorld.Content, new NewGameOptions { Seed = 1, Map = BotWorld.Map });
        Evenmere.Content.ContentDb content = BotWorld.Content;
        BuildingTypeId dirt = content.Buildings.IdOf("dirt_road");
        BuildingInfo hall = FindBuilding(sim, "founders_hall");
        TileCoord[] first = [ new TileCoord(hall.Origin.X + hall.Width, hall.Origin.Y) ];
        TileCoord[] second = [ new TileCoord(hall.Origin.X - 3, hall.Origin.Y) ];
        sim.Enqueue(new PlaceRoad(first, dirt));
        sim.Enqueue(new PlaceRoad(second, dirt));
        sim.Tick();
        sim.DrainEvents();

        RoadPlanner.RoadDistance(sim, first, second).ShouldBeNull();
    }

    private static BuildingInfo FindBuilding(Simulation sim, string name)
    {
        foreach (BuildingInfo building in sim.Query.Map.Buildings)
        {
            if (BotWorld.Content.Buildings.Names[building.Type.Value] == name)
            {
                return building;
            }
        }

        throw new InvalidOperationException($"no building named {name}");
    }
}
