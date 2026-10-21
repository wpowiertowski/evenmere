using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Integration;

/// <summary>A new game on the starter island: the hall, the stock, the map, the unlocks and the untouched targets.</summary>
public sealed class NewGameTests
{
    [Fact]
    public void NewGame_PlacesTheFoundersHallActiveAtTheStartSite()
    {
        Simulation sim = SiteFinder.NewGame();

        List<BuildingInfo> active = ActiveBuildings(sim);
        active.Count.ShouldBe(1);
        BuildingInfo hall = active[0];
        BuildingTypeId hallType = SiteFinder.Content.Required.FoundersHall;

        hall.Type.ShouldBe(hallType);
        hall.State.ShouldBe(BuildingState.Active);
        hall.Origin.ShouldBe(new TileCoord(SiteFinder.Map.StartSite.X, SiteFinder.Map.StartSite.Y));
        hall.Rotation.ShouldBe((Rotation)SiteFinder.Map.StartSite.Rotation);
    }

    [Fact]
    public void NewGame_AddsTheStartStockToThePool()
    {
        Simulation sim = SiteFinder.NewGame();

        foreach (GoodAmount line in SiteFinder.Map.StartStock)
        {
            sim.Query.Storage.Stock(line.Good).ShouldBe(line.Amount, SiteFinder.Content.NameOf(line.Good));
        }
    }

    [Fact]
    public void NewGame_LoadsTheMapDimensions()
    {
        Simulation sim = SiteFinder.NewGame();

        sim.Query.Map.Width.ShouldBe(SiteFinder.Map.Width);
        sim.Query.Map.Height.ShouldBe(SiteFinder.Map.Height);
    }

    [Fact]
    public void NewGame_AppliesTheStartUnlocks()
    {
        Simulation sim = SiteFinder.NewGame();

        sim.Query.Progression.IsUnlocked(SiteFinder.Content.Required.FoundersHall).ShouldBeTrue();
        sim.Query.Progression.IsUnlocked(SiteFinder.Content.Required.Settlers).ShouldBeTrue();
        sim.Query.Progression.IsUnlocked(SiteFinder.Content.Required.DirtRoad).ShouldBeTrue();
    }

    [Fact]
    public void NewGame_LeavesResearchAndCivicTargetsAsNone()
    {
        Simulation sim = SiteFinder.NewGame();

        sim.Query.Research.Target.ShouldBeNull();
        sim.Query.Research.Queue.ShouldBeEmpty();
        sim.Query.Culture.Target.ShouldBeNull();
        sim.Query.Culture.Queue.ShouldBeEmpty();
    }

    [Fact]
    public void NewGame_NullMap_KeepsTheEmptyWorld()
    {
        var sim = Simulation.CreateNew(SiteFinder.Content, new NewGameOptions { Seed = 7 });

        sim.Query.Map.Width.ShouldBe(0);
        sim.Query.Map.Height.ShouldBe(0);
        ActiveBuildings(sim).ShouldBeEmpty();
        sim.Query.Storage.Stock(SiteFinder.Content.IdOf<GoodId>("timber")).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void NewGame_SameSeed_GivesTheSameHash()
    {
        SiteFinder.NewGame(42).ComputeStateHash().ShouldBe(SiteFinder.NewGame(42).ComputeStateHash());
    }

    [Fact]
    public void NewGame_DifferentSeeds_GiveDifferentHashes()
    {
        SiteFinder.NewGame(1).ComputeStateHash().ShouldNotBe(SiteFinder.NewGame(2).ComputeStateHash());
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
