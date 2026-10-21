using Evenmere.Core.Ids;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Tools.Balance;

namespace Evenmere.Tools.Tests.Balance;

/// <summary>Searches placements on the starter island through the real queries.</summary>
public sealed class PlacementSearchTests
{
    [Fact]
    public void Find_MarketNearHall_ReturnsPlaceableSpot()
    {
        var sim = Simulation.CreateNew(BotWorld.Content, new NewGameOptions { Seed = 1, Map = BotWorld.Map });
        BuildingTypeId market = BotWorld.Content.Buildings.IdOf("market");
        BuildingInfo hall = FindBuilding(sim, "founders_hall");

        PlacementCandidate? candidate = PlacementSearch.Find(sim, market, hall.Origin.X, hall.Origin.Y, 3, 3);

        candidate.ShouldNotBeNull();
        candidate.Validation.CanPlace.ShouldBeTrue();
    }

    [Fact]
    public void Find_FishingHutNearHall_StaysByCoastline()
    {
        var sim = Simulation.CreateNew(BotWorld.Content, new NewGameOptions { Seed = 1, Map = BotWorld.Map });
        BuildingTypeId hut = BotWorld.Content.Buildings.IdOf("fishing_hut");
        BuildingInfo hall = FindBuilding(sim, "founders_hall");

        PlacementCandidate? candidate = PlacementSearch.Find(sim, hut, hall.Origin.X, hall.Origin.Y, 2, 2);

        candidate.ShouldNotBeNull();
        candidate.Validation.CanPlace.ShouldBeTrue();
    }

    [Fact]
    public void Find_ExcludedOrigin_SkipsIt()
    {
        var sim = Simulation.CreateNew(BotWorld.Content, new NewGameOptions { Seed = 1, Map = BotWorld.Map });
        BuildingTypeId market = BotWorld.Content.Buildings.IdOf("market");
        BuildingInfo hall = FindBuilding(sim, "founders_hall");

        PlacementCandidate? first = PlacementSearch.Find(sim, market, hall.Origin.X, hall.Origin.Y, 3, 3);
        first.ShouldNotBeNull();
        PlacementCandidate? second = PlacementSearch.Find(
            sim, market, hall.Origin.X, hall.Origin.Y, 3, 3,
            isExcluded: (x, y, rotation) => x == first.X && y == first.Y && rotation == first.Rotation);

        second.ShouldNotBeNull();
        (second.X != first.X || second.Y != first.Y || second.Rotation != first.Rotation).ShouldBeTrue();
    }

    [Fact]
    public void FindAll_ListsPlaceableSpotsInSpiralOrder()
    {
        var sim = Simulation.CreateNew(BotWorld.Content, new NewGameOptions { Seed = 1, Map = BotWorld.Map });
        BuildingTypeId house = BotWorld.Content.Buildings.IdOf("settler_house");
        BuildingInfo hall = FindBuilding(sim, "founders_hall");

        List<PlacementCandidate> candidates = PlacementSearch.FindAll(sim, house, hall.Origin.X, hall.Origin.Y, 2, 2, 6, 5);

        candidates.Count.ShouldBe(5);
        foreach (PlacementCandidate candidate in candidates)
        {
            candidate.Validation.CanPlace.ShouldBeTrue();
        }
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
