using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Roads;

/// <summary>Storehouse reach, the nearest storehouse and service coverage, and how they follow changes to roads and buildings.</summary>
public sealed class ReachAndCoverageTests
{
    // A storehouse (2 x 2, reach 20) at (2, 2) whose door is (4, 2), the first tile of a dirt road along y = 2.
    // A tile (x, 2) is (x - 4) tiles of dirt from the door, so it costs (x - 4) * 1000.
    private static (RoadsHarness H, EntityId Store) ReachWorld()
    {
        var h = new RoadsHarness(size: 48);
        EntityId store = h.Add("storehouse", 2, 2);
        h.DirtLine((4, 2), (40, 2));
        return (h, store);
    }

    // --- Reach ---

    [Fact]
    public void Reach_HouseOnTheRoad_ReportsTheStorehouseAndTheRoadCost()
    {
        (RoadsHarness h, EntityId store) = ReachWorld();
        EntityId hut = h.Add("hut", 10, 3);

        h.Queries.IsInStorehouseReach(hut).ShouldBeTrue();
        h.Queries.TryNearestStorehouse(hut, out EntityId nearest, out int cost).ShouldBeTrue();
        nearest.ShouldBe(store);
        cost.ShouldBe(6000);
        h.Queries.TryGetStorehouseCost(store, hut, out int specific).ShouldBeTrue();
        specific.ShouldBe(6000);
    }

    [Fact]
    public void Reach_ExactlyAtTheReach_IsInsideAndOneTileMoreIsOutside()
    {
        (RoadsHarness h, _) = ReachWorld();
        EntityId atEdge = h.Add("hut", 24, 3);
        EntityId beyond = h.Add("hut", 25, 3);

        h.Queries.IsInStorehouseReach(atEdge).ShouldBeTrue();
        h.Queries.TryNearestStorehouse(atEdge, out _, out int cost).ShouldBeTrue();
        cost.ShouldBe(20000);
        h.Queries.IsInStorehouseReach(beyond).ShouldBeFalse();
        h.Queries.TryNearestStorehouse(beyond, out EntityId none, out _).ShouldBeFalse();
        none.ShouldBe(EntityId.None);
    }

    [Fact]
    public void Reach_AStorehouse_IsInItsOwnReachAtCostZero()
    {
        var h = new RoadsHarness();
        EntityId store = h.Add("storehouse", 5, 5);

        h.Queries.IsInStorehouseReach(store).ShouldBeTrue("even with no road at all");
        h.Queries.TryNearestStorehouse(store, out EntityId nearest, out int cost).ShouldBeTrue();
        (nearest, cost).ShouldBe((store, 0));
        h.Queries.TryGetStorehouseCost(store, store, out int own).ShouldBeTrue();
        own.ShouldBe(0);
    }

    [Fact]
    public void Reach_BuildingWithNoRoadAccess_IsNotInReach()
    {
        (RoadsHarness h, _) = ReachWorld();
        EntityId hut = h.Add("hut", 10, 6);

        h.Queries.IsInStorehouseReach(hut).ShouldBeFalse();
    }

    [Fact]
    public void Reach_BrokenRoad_CutsTheBuildingOffAndMendingItBringsItBack()
    {
        (RoadsHarness h, _) = ReachWorld();
        EntityId hut = h.Add("hut", 10, 3);
        h.Queries.IsInStorehouseReach(hut).ShouldBeTrue();

        h.RemoveRoads((8, 2));
        h.Queries.IsInStorehouseReach(hut).ShouldBeFalse();

        h.PlaceDirt((8, 2));
        h.Queries.IsInStorehouseReach(hut).ShouldBeTrue();
    }

    [Fact]
    public void Reach_UpgradingTheRoadToCheaperPaving_LowersTheCost()
    {
        (RoadsHarness h, EntityId store) = ReachWorld();
        EntityId hut = h.Add("hut", 10, 3);
        h.Unlock(RoadsHarness.Paved);

        h.Line(RoadsHarness.Paved, (5, 2), (10, 2));

        h.Queries.TryGetStorehouseCost(store, hut, out int cost).ShouldBeTrue();
        cost.ShouldBe(6 * 650);
    }

    [Fact]
    public void Reach_BuildingWithSeveralAccessTiles_UsesTheCheapest()
    {
        (RoadsHarness h, _) = ReachWorld();

        // The shrine (10..11, 3..4) touches the road at (10, 2) and (11, 2), which cost 6000 and 7000.
        EntityId shrine = h.Add("shrine", 10, 3);

        h.Queries.TryNearestStorehouse(shrine, out _, out int cost).ShouldBeTrue();
        cost.ShouldBe(6000);
    }

    [Fact]
    public void Reach_TwoStorehouses_TheNearerOneWinsAndEachHasItsOwnCost()
    {
        (RoadsHarness h, EntityId west) = ReachWorld();
        EntityId east = h.Add("storehouse", 30, 3);
        EntityId hut = h.Add("hut", 28, 3);

        h.Queries.TryNearestStorehouse(hut, out EntityId nearest, out int cost).ShouldBeTrue();
        nearest.ShouldBe(east);
        cost.ShouldBe(2000);
        h.Queries.TryGetStorehouseCost(west, hut, out _).ShouldBeFalse("24 tiles from the west storehouse, whose reach is 20");
        h.Queries.TryGetStorehouseCost(east, hut, out int viaEast).ShouldBeTrue();
        viaEast.ShouldBe(2000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reach_TwoStorehousesAtTheSameCost_TheLowestIdWinsWhicheverSideItIsOn(bool eastIsBuiltFirst)
    {
        var h = new RoadsHarness(size: 48);
        h.DirtLine((4, 2), (40, 2));
        EntityId first;
        EntityId second;
        if (eastIsBuiltFirst)
        {
            first = h.Add("storehouse", 30, 3);
            second = h.Add("storehouse", 2, 2);
        }
        else
        {
            first = h.Add("storehouse", 2, 2);
            second = h.Add("storehouse", 30, 3);
        }

        // West door (4, 2) and east doors (30, 2) and (31, 2) are both 13 tiles from (17, 2).
        EntityId hut = h.Add("hut", 17, 3);

        h.Queries.TryNearestStorehouse(hut, out EntityId nearest, out int cost).ShouldBeTrue();
        cost.ShouldBe(13000);
        nearest.ShouldBe(first);
        first.Value.ShouldBeLessThan(second.Value);
    }

    [Fact]
    public void Reach_ShortReachStorehouse_DoesNotReachWhatALongOneDoes()
    {
        (RoadsHarness h, EntityId store) = ReachWorld();
        EntityId depot = h.Add("depot", 8, 1);
        EntityId hut = h.Add("hut", 20, 3);

        h.Queries.TryGetStorehouseCost(store, hut, out _).ShouldBeTrue();
        h.Queries.TryGetStorehouseCost(depot, hut, out _).ShouldBeFalse("the depot's reach is 5 tiles");
    }

    [Fact]
    public void Reach_RemovingTheStorehouse_EndsItAndAnotherOneRestoresIt()
    {
        (RoadsHarness h, EntityId store) = ReachWorld();
        EntityId hut = h.Add("hut", 10, 3);
        h.Queries.IsInStorehouseReach(hut).ShouldBeTrue();

        h.Remove(store);
        h.Queries.IsInStorehouseReach(hut).ShouldBeFalse();
        h.Queries.TryGetStorehouseCost(store, hut, out _).ShouldBeFalse();

        EntityId replacement = h.Add("depot", 8, 1);
        h.Queries.TryNearestStorehouse(hut, out EntityId nearest, out _).ShouldBeTrue();
        nearest.ShouldBe(replacement);
    }

    [Fact]
    public void Reach_ASiteIsAnsweredLikeAnyBuilding()
    {
        (RoadsHarness h, _) = ReachWorld();
        EntityId site = h.Add("hut", 10, 3, BuildingState.Site);

        h.Queries.IsInStorehouseReach(site).ShouldBeTrue();
    }

    [Fact]
    public void Reach_ASiteStorehouse_ReachesNothingUntilItIsActive()
    {
        var h = new RoadsHarness();
        EntityId site = h.Add("depot", 5, 5, BuildingState.Site);
        h.DirtLine((5, 6), (8, 6));
        EntityId hut = h.Add("hut", 7, 7);

        h.Queries.IsInStorehouseReach(hut).ShouldBeFalse();

        h.Sim.Services.BuildingRegistry.SetState(site, BuildingState.Active);

        h.Queries.IsInStorehouseReach(hut).ShouldBeTrue();
    }

    [Fact]
    public void Reach_APausedStorehouse_StillReaches()
    {
        (RoadsHarness h, EntityId store) = ReachWorld();
        EntityId hut = h.Add("hut", 10, 3);

        h.Sim.Services.BuildingRegistry.SetState(store, BuildingState.Paused);

        h.Queries.IsInStorehouseReach(hut).ShouldBeTrue();
    }

    [Fact]
    public void Reach_StorehouseChangingType_StopsReaching()
    {
        (RoadsHarness h, EntityId store) = ReachWorld();
        EntityId hut = h.Add("hut", 10, 3);

        h.Sim.Services.BuildingRegistry.SetType(store, RoadContent.Building("settler_house"));

        h.Queries.IsInStorehouseReach(hut).ShouldBeFalse();
        h.Network.FieldCount.ShouldBe(0);
    }

    [Fact]
    public void Reach_UnknownOrMissingBuildings_AreNeverInReach()
    {
        (RoadsHarness h, EntityId store) = ReachWorld();

        h.Queries.IsInStorehouseReach(new EntityId(900)).ShouldBeFalse();
        h.Queries.IsInStorehouseReach(EntityId.None).ShouldBeFalse();
        h.Queries.TryGetStorehouseCost(new EntityId(900), store, out _).ShouldBeFalse();
        h.Queries.TryGetStorehouseCost(store, new EntityId(900), out _).ShouldBeFalse();
        h.Queries.TryGetStorehouseCost(EntityId.None, EntityId.None, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("hut", 10, 3, true)]
    [InlineData("hut", 24, 3, true)]
    [InlineData("hut", 25, 3, false)]
    [InlineData("hut", 10, 6, false)]
    [InlineData("hut", 44, 3, false)]
    [InlineData("shrine", 10, 3, true)]
    [InlineData("shrine", 23, 3, true)]
    [InlineData("shrine", 25, 3, false)]
    [InlineData("shrine", 10, 5, false)]
    public void Reach_UnbuiltFootprint_AgreesWithABuildingThenPlacedOnIt(string type, int x, int y, bool expected)
    {
        (RoadsHarness h, _) = ReachWorld();
        Footprint size = RoadContent.Db.Get(RoadContent.Building(type)).Footprint;

        bool beforeBuilding = h.Queries.IsFootprintInStorehouseReach(x, y, size.Width, size.Height);
        EntityId building = h.Add(type, x, y);

        beforeBuilding.ShouldBe(expected);
        h.Queries.IsInStorehouseReach(building).ShouldBe(beforeBuilding);
    }

    [Fact]
    public void Reach_UnbuiltFootprint_FollowsRoadsAndStorehouses()
    {
        (RoadsHarness h, EntityId store) = ReachWorld();
        h.Queries.IsFootprintInStorehouseReach(10, 3, 1, 1).ShouldBeTrue();

        h.RemoveRoads((8, 2));
        h.Queries.IsFootprintInStorehouseReach(10, 3, 1, 1).ShouldBeFalse();

        h.PlaceDirt((8, 2));
        h.Queries.IsFootprintInStorehouseReach(10, 3, 1, 1).ShouldBeTrue();

        h.Remove(store);
        h.Queries.IsFootprintInStorehouseReach(10, 3, 1, 1).ShouldBeFalse();
    }

    [Fact]
    public void Reach_UnbuiltFootprint_NeedsARoadTileBesideItEvenOnAStorehousesOwnSpot()
    {
        // The storehouse's own footprint is "in reach" only through the road beside it, like any other: there is no self rule for a ghost.
        var h = new RoadsHarness();

        h.Queries.IsFootprintInStorehouseReach(5, 5, 2, 2).ShouldBeFalse("no storehouse and no road yet");
        h.Add("storehouse", 5, 5);
        h.Queries.IsFootprintInStorehouseReach(5, 5, 2, 2).ShouldBeFalse("a storehouse with no road beside it reaches no road tile");
        h.DirtLine((7, 5), (7, 8));
        h.Queries.IsFootprintInStorehouseReach(5, 5, 2, 2).ShouldBeTrue("its door is now a road tile in its own field");
        h.Queries.IsFootprintInStorehouseReach(9, 6, 1, 1).ShouldBeFalse("no road touches that footprint");
        h.Queries.IsFootprintInStorehouseReach(8, 6, 1, 1).ShouldBeTrue();
    }

    [Fact]
    public void Reach_UnbuiltFootprint_EmptyOrPartlyOffTheMap_IsHandled()
    {
        (RoadsHarness h, _) = ReachWorld();

        h.Queries.IsFootprintInStorehouseReach(10, 3, 0, 1).ShouldBeFalse();
        h.Queries.IsFootprintInStorehouseReach(10, 3, 1, -2).ShouldBeFalse();
        h.Queries.IsFootprintInStorehouseReach(-1, -1, 2, 2).ShouldBeFalse("no road touches the part of the map it covers");
        h.Queries.IsFootprintInStorehouseReach(9, 3, 100, 100).ShouldBeTrue("it still touches the road along its top edge");
    }

    // --- Coverage ---

    // A storehouse (0..1, 8..9) with door (0..1, 10) on a road along y = 10. Two market stalls (coverage 4) sit above the
    // road at x = 6 and x = 12, and huts below it at x = 3, 9, 15 and 20. A stall covers the road within four tiles of its door.
    private static (RoadsHarness H, EntityId StallA, EntityId StallB, EntityId[] Huts) MarketWorld()
    {
        var h = new RoadsHarness(size: 48);
        h.Add("storehouse", 0, 8);
        h.DirtLine((0, 10), (40, 10));
        EntityId a = h.Add("stall", 6, 9);
        EntityId b = h.Add("stall", 12, 9);
        EntityId[] huts = [h.Add("hut", 3, 11), h.Add("hut", 9, 11), h.Add("hut", 15, 11), h.Add("hut", 20, 11)];
        return (h, a, b, huts);
    }

    [Fact]
    public void Coverage_HousesWithinAMarketsRange_AreCovered()
    {
        (RoadsHarness h, _, _, EntityId[] huts) = MarketWorld();

        huts.Select(hut => h.Queries.IsCovered(hut, ServiceKind.Market)).ShouldBe([true, true, true, false]);
    }

    [Fact]
    public void Coverage_TwoOverlappingMarkets_CoverTheUnionAndDropOutOneByOne()
    {
        (RoadsHarness h, EntityId a, EntityId b, EntityId[] huts) = MarketWorld();

        // The first hut is only in stall A's range, the second in both, the third only in stall B's.
        h.Remove(a);
        huts.Select(hut => h.Queries.IsCovered(hut, ServiceKind.Market)).ShouldBe([false, true, true, false]);

        h.Remove(b);
        huts.Select(hut => h.Queries.IsCovered(hut, ServiceKind.Market)).ShouldBe([false, false, false, false]);
    }

    [Fact]
    public void Coverage_RemovingTheSecondMarket_KeepsTheHouseBetweenThemCovered()
    {
        (RoadsHarness h, EntityId a, EntityId b, EntityId[] huts) = MarketWorld();

        h.Remove(b);

        huts.Select(hut => h.Queries.IsCovered(hut, ServiceKind.Market)).ShouldBe([true, true, false, false]);
        h.Queries.IsCovered(huts[1], ServiceKind.Market).ShouldBeTrue("covered by stall A alone");
        h.Remove(a);
        h.Queries.IsCovered(huts[1], ServiceKind.Market).ShouldBeFalse();
    }

    [Fact]
    public void Coverage_ExactlyAtTheCoverage_IsCoveredAndOneTileFurtherIsNot()
    {
        (RoadsHarness h, _, _, _) = MarketWorld();
        EntityId atEdge = h.Add("hut", 2, 11);
        EntityId beyond = h.Add("hut", 1, 11);

        // Stall A's door is (6, 10): four tiles of road to (2, 10), five to (1, 10).
        h.Queries.IsCovered(atEdge, ServiceKind.Market).ShouldBeTrue();
        h.Queries.IsCovered(beyond, ServiceKind.Market).ShouldBeFalse();
    }

    [Fact]
    public void Coverage_ACutRoad_UncoversAndMendingRecovers()
    {
        (RoadsHarness h, _, _, EntityId[] huts) = MarketWorld();
        h.Queries.IsCovered(huts[0], ServiceKind.Market).ShouldBeTrue();

        h.RemoveRoads((5, 10));
        h.Queries.IsCovered(huts[0], ServiceKind.Market).ShouldBeFalse();

        h.PlaceDirt((5, 10));
        h.Queries.IsCovered(huts[0], ServiceKind.Market).ShouldBeTrue();
    }

    [Fact]
    public void Coverage_MarketOutOfEveryStorehousesReach_CoversNobodyUntilOneReachesIt()
    {
        var h = new RoadsHarness(size: 48);
        h.Add("depot", 0, 9);
        h.DirtLine((0, 10), (40, 10));
        EntityId stall = h.Add("stall", 20, 9);
        EntityId hut = h.Add("hut", 21, 11);

        // The hut is next to the stall, but the stall is 20 tiles from the only depot, whose reach is 5.
        h.Queries.IsInStorehouseReach(stall).ShouldBeFalse();
        h.Queries.IsCovered(hut, ServiceKind.Market).ShouldBeFalse();

        h.Add("depot", 22, 9);

        h.Queries.IsInStorehouseReach(stall).ShouldBeTrue();
        h.Queries.IsCovered(hut, ServiceKind.Market).ShouldBeTrue();
    }

    [Fact]
    public void Coverage_RemovingTheStorehouseThatSuppliesAMarket_UncoversItsHouses()
    {
        var h = new RoadsHarness(size: 48);
        EntityId depot = h.Add("depot", 0, 9);
        h.DirtLine((0, 10), (10, 10));
        h.Add("stall", 3, 9);
        EntityId hut = h.Add("hut", 4, 11);
        h.Queries.IsCovered(hut, ServiceKind.Market).ShouldBeTrue();

        h.Remove(depot);

        h.Queries.IsCovered(hut, ServiceKind.Market).ShouldBeFalse();
    }

    [Fact]
    public void Coverage_ServicesOtherThanMarkets_NeedNoStorehouse()
    {
        var h = new RoadsHarness(size: 48);
        h.DirtLine((0, 10), (40, 10));
        h.Add("shrine", 20, 8);
        EntityId hut = h.Add("hut", 22, 11);

        h.Queries.IsCovered(hut, ServiceKind.Shrine).ShouldBeTrue();
        h.Queries.IsCovered(hut, ServiceKind.Market).ShouldBeFalse("a shrine does not make a market");
        h.Queries.IsCovered(hut, ServiceKind.Tavern).ShouldBeFalse();
    }

    [Fact]
    public void Coverage_ShrineCoverageEnds_AtItsOwnRange()
    {
        var h = new RoadsHarness(size: 48);
        h.DirtLine((0, 10), (40, 10));
        h.Add("shrine", 5, 8);

        // The shrine's doors are (5, 10) and (6, 10); its coverage is 14 tiles, so (20, 10) is 14 from (6, 10) and (21, 10) is 15.
        EntityId inside = h.Add("hut", 20, 11);
        EntityId outside = h.Add("hut", 21, 11);

        h.Queries.IsCovered(inside, ServiceKind.Shrine).ShouldBeTrue();
        h.Queries.IsCovered(outside, ServiceKind.Shrine).ShouldBeFalse();
    }

    [Fact]
    public void Coverage_FoundersHall_IsAStorehouseAndAMarketWithOneBoundForEach()
    {
        var h = new RoadsHarness(size: 48);
        EntityId hall = h.Add("founders_hall", 0, 6);
        h.DirtLine((0, 10), (40, 10));
        EntityId near = h.Add("hut", 10, 11);
        EntityId middle = h.Add("hut", 20, 11);
        EntityId far = h.Add("hut", 30, 11);

        // The hall's doors are (0..3, 10). Market coverage is 16 and storehouse reach 24, both counted from the nearest door, (3, 10).
        h.Queries.IsCovered(near, ServiceKind.Market).ShouldBeTrue();
        h.Queries.IsCovered(middle, ServiceKind.Market).ShouldBeFalse("17 tiles from the door");
        h.Queries.IsInStorehouseReach(middle).ShouldBeTrue();
        h.Queries.TryNearestStorehouse(middle, out EntityId nearest, out int cost).ShouldBeTrue();
        (nearest, cost).ShouldBe((hall, 17000));
        h.Queries.IsInStorehouseReach(far).ShouldBeFalse("27 tiles from the door");
    }

    [Fact]
    public void Coverage_ServiceBuildingChangingType_ChangesWhatItCovers()
    {
        (RoadsHarness h, EntityId a, _, EntityId[] huts) = MarketWorld();
        h.Queries.IsCovered(huts[0], ServiceKind.Market).ShouldBeTrue();

        h.Sim.Services.BuildingRegistry.SetType(a, RoadContent.Building("hut"));

        h.Queries.IsCovered(huts[0], ServiceKind.Market).ShouldBeFalse();
    }

    [Fact]
    public void Coverage_UnknownBuilding_IsNotCovered()
    {
        (RoadsHarness h, _, _, _) = MarketWorld();

        h.Queries.IsCovered(new EntityId(900), ServiceKind.Market).ShouldBeFalse();
        h.Queries.IsCovered(EntityId.None, ServiceKind.Market).ShouldBeFalse();
        h.Queries.IsCovered(new EntityId(1), (ServiceKind)99).ShouldBeFalse();
    }

    // --- Lifecycle ---

    [Fact]
    public void Lifecycle_BuildingsPlacedBeforeTheSystemWasListening_AreFoundOnFirstUse()
    {
        // A loaded game: the map already holds its buildings and nobody replays their activation.
        var h = new RoadsHarness(size: 48, subscribe: false);
        BuildingInfo store = h.Sim.Queries.Map.AddBuilding(new EntityId(1), RoadContent.Building("storehouse"), 2, 2, 2, 2);
        BuildingInfo hut = h.Sim.Queries.Map.AddBuilding(new EntityId(2), RoadContent.Building("hut"), 10, 3);
        h.DirtLine((4, 2), (20, 2));

        h.Queries.TryNearestStorehouse(hut.Id, out EntityId nearest, out int cost).ShouldBeTrue();

        (nearest, cost).ShouldBe((store.Id, 6000));
    }

    [Fact]
    public void Lifecycle_ASiteDemolishedBeforeItWasEverActive_IsHarmless()
    {
        var h = new RoadsHarness();
        EntityId site = h.Add("depot", 5, 5, BuildingState.Site);

        h.Remove(site);

        h.Network.FieldCount.ShouldBe(0);
    }
}
