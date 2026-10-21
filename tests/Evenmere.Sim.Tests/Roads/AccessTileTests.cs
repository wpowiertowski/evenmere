using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Roads;

/// <summary>A building's access tiles: the road tiles orthogonally adjacent to its footprint, in row-major order.</summary>
public sealed class AccessTileTests
{
    private static List<TileCoord> AccessOf(RoadsHarness h, EntityId building)
    {
        var tiles = new List<TileCoord> { new(-9, -9) };
        h.Network.TryGetAccessTiles(building, tiles).ShouldBeTrue();
        return tiles;
    }

    [Fact]
    public void AccessTiles_AreTheAdjacentRoadTilesInRowMajorOrder()
    {
        var h = new RoadsHarness();
        EntityId shrine = h.Add("shrine", 5, 5);

        // Around the 2 × 2 footprint at (5..6, 5..6): top, left, right and bottom edges, plus the four diagonal corners and a far tile.
        h.PlaceDirt((6, 4), (5, 4), (4, 5), (7, 6), (6, 7), (5, 7), (4, 4), (7, 4), (4, 7), (7, 7), (10, 10));

        AccessOf(h, shrine).ShouldBe(
        [
            new TileCoord(5, 4), new TileCoord(6, 4),
            new TileCoord(4, 5),
            new TileCoord(7, 6),
            new TileCoord(5, 7), new TileCoord(6, 7),
        ]);
    }

    [Fact]
    public void AccessTiles_DoNotDependOnTheOrderTheRoadsWereLaid()
    {
        var forward = new RoadsHarness();
        var backward = new RoadsHarness();
        EntityId a = forward.Add("shrine", 5, 5);
        EntityId b = backward.Add("shrine", 5, 5);

        forward.PlaceDirt((5, 4), (4, 5), (7, 6), (6, 7));
        backward.PlaceDirt((6, 7), (7, 6), (4, 5), (5, 4));

        AccessOf(backward, b).ShouldBe(AccessOf(forward, a));
    }

    [Fact]
    public void AccessTiles_AtTheMapCorner_AreClippedToTheMap()
    {
        var h = new RoadsHarness();
        EntityId shrine = h.Add("shrine", 0, 0);

        h.PlaceDirt((2, 0), (2, 1), (0, 2), (1, 2), (2, 2));

        AccessOf(h, shrine).ShouldBe([new TileCoord(2, 0), new TileCoord(2, 1), new TileCoord(0, 2), new TileCoord(1, 2)]);
    }

    [Fact]
    public void AccessTiles_AtTheFarMapEdge_AreClippedToTheMap()
    {
        var h = new RoadsHarness();
        EntityId shrine = h.Add("shrine", 30, 30);

        h.PlaceDirt((29, 30), (30, 29), (29, 29));

        AccessOf(h, shrine).ShouldBe([new TileCoord(30, 29), new TileCoord(29, 30)]);
    }

    [Fact]
    public void AccessTiles_OfABuildingWithNoRoadNextToIt_AreEmpty()
    {
        var h = new RoadsHarness();
        EntityId hut = h.Add("hut", 5, 5);
        h.PlaceDirt((4, 4), (6, 6), (8, 5));

        AccessOf(h, hut).ShouldBeEmpty();
    }

    [Fact]
    public void AccessTiles_OfAnUnknownBuilding_ReportsMissing()
    {
        var h = new RoadsHarness();

        h.Network.TryGetAccessTiles(new EntityId(77), []).ShouldBeFalse();
        h.Network.TryGetAccessTiles(EntityId.None, []).ShouldBeFalse();
    }

    [Fact]
    public void HasRoadAccess_RoadTouchingAnEdge_IsTrue()
    {
        var h = new RoadsHarness();
        EntityId hut = h.Add("hut", 5, 5);

        h.Queries.HasRoadAccess(hut).ShouldBeFalse();
        h.PlaceDirt((5, 6));
        h.Queries.HasRoadAccess(hut).ShouldBeTrue();
    }

    [Fact]
    public void HasRoadAccess_RoadOnlyAtACorner_IsFalse()
    {
        var h = new RoadsHarness();
        EntityId hut = h.Add("hut", 5, 5);

        h.PlaceDirt((4, 4), (6, 6), (4, 6), (6, 4));

        h.Queries.HasRoadAccess(hut).ShouldBeFalse();
    }

    [Fact]
    public void HasRoadAccess_ASiteHasItToo()
    {
        var h = new RoadsHarness();
        EntityId site = h.Add("storehouse", 5, 5, BuildingState.Site);

        h.DirtLine((7, 5), (7, 8));

        h.Queries.HasRoadAccess(site).ShouldBeTrue();
    }

    [Fact]
    public void HasRoadAccess_UnknownBuildingOrNoRoadsAtAll_IsFalse()
    {
        var h = new RoadsHarness();
        EntityId hut = h.Add("hut", 5, 5);

        h.Queries.HasRoadAccess(hut).ShouldBeFalse("no road has ever been laid, so the layer does not even exist");
        h.Queries.HasRoadAccess(new EntityId(500)).ShouldBeFalse();
        h.Queries.HasRoadAccess(EntityId.None).ShouldBeFalse();
    }

    [Fact]
    public void HasRoadAccess_AfterTheRoadIsRemoved_IsFalseAgain()
    {
        var h = new RoadsHarness();
        EntityId hut = h.Add("hut", 5, 5);
        h.PlaceDirt((5, 6));

        h.RemoveRoads((5, 6));

        h.Queries.HasRoadAccess(hut).ShouldBeFalse();
    }
}
