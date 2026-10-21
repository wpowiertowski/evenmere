using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Roads;

/// <summary>View paths: A* over road tiles for the cart replay. Presentation only, but deterministic all the same.</summary>
public sealed class ViewPathTests
{
    private static TileCoord T(int x, int y) => new(x, y);

    [Fact]
    public void ViewPath_BetweenTwoBuildings_RunsFromAnAccessTileOfOneToAnAccessTileOfTheOther()
    {
        var h = new RoadsHarness(size: 48);
        EntityId store = h.Add("storehouse", 2, 2);
        EntityId hut = h.Add("hut", 10, 3);
        h.DirtLine((4, 2), (12, 2));
        var path = new List<TileCoord> { T(-5, -5) };

        h.Queries.TryFindViewPath(store, hut, path).ShouldBeTrue();

        path.ShouldBe(Enumerable.Range(4, 7).Select(x => T(x, 2)).ToList());
    }

    [Fact]
    public void ViewPath_EveryStepIsOntoAnAdjacentRoadTile()
    {
        var h = new RoadsHarness();
        EntityId store = h.Add("storehouse", 2, 2);
        EntityId hut = h.Add("hut", 15, 15);
        h.DirtLine((4, 2), (14, 2));
        h.DirtLine((14, 3), (14, 14));
        h.DirtLine((14, 15), (14, 15));
        var path = new List<TileCoord>();

        h.Queries.TryFindViewPath(store, hut, path).ShouldBeTrue();

        path.First().ShouldBe(T(4, 2));
        path.Last().ShouldBe(T(14, 15));
        foreach (TileCoord tile in path)
        {
            h.Queries.RoadAt(tile.X, tile.Y).ShouldNotBeNull();
        }

        foreach ((TileCoord from, TileCoord to) in path.Zip(path.Skip(1)))
        {
            (Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y)).ShouldBe(1);
        }
    }

    [Fact]
    public void ViewPath_PrefersTheCheaperRouteOverTheShorterOne()
    {
        var h = new RoadsHarness();
        h.Unlock(RoadsHarness.Paved);

        // Straight along y = 5 is 9 dirt tiles and the end. Round by y = 7 is 14 tiles, all paved: 14 * 650 beats 9 * 1000 + 650.
        h.DirtLine((2, 5), (12, 5));
        h.Line(RoadsHarness.Paved, (2, 5), (2, 7));
        h.Line(RoadsHarness.Paved, (2, 7), (12, 7));
        h.Line(RoadsHarness.Paved, (12, 7), (12, 5));
        var path = new List<TileCoord>();

        h.Queries.TryFindViewPath(T(2, 5), T(12, 5), path).ShouldBeTrue();

        path.Count.ShouldBe(15);
        path.ShouldContain(T(7, 7));
        path.ShouldNotContain(T(7, 5));
    }

    [Fact]
    public void ViewPath_BetweenRoadsThatDoNotMeet_FindsNothingAndLeavesAnEmptyPath()
    {
        var h = new RoadsHarness();
        EntityId a = h.Add("hut", 2, 3);
        EntityId b = h.Add("hut", 12, 3);
        h.DirtLine((2, 2), (5, 2));
        h.DirtLine((10, 2), (12, 2));
        var path = new List<TileCoord> { T(1, 1), T(2, 2) };

        h.Queries.TryFindViewPath(a, b, path).ShouldBeFalse();

        path.ShouldBeEmpty();
        h.Queries.TryFindViewPath(T(2, 2), T(12, 2), path).ShouldBeFalse();
        path.ShouldBeEmpty();
    }

    [Fact]
    public void ViewPath_ABuildingWithNoRoadAccessOrAnUnknownOne_FindsNothing()
    {
        var h = new RoadsHarness();
        EntityId a = h.Add("hut", 2, 3);
        EntityId lonely = h.Add("hut", 20, 20);
        h.DirtLine((2, 2), (5, 2));
        var path = new List<TileCoord> { T(1, 1) };

        h.Queries.TryFindViewPath(a, lonely, path).ShouldBeFalse();
        h.Queries.TryFindViewPath(lonely, a, path).ShouldBeFalse();
        h.Queries.TryFindViewPath(a, new EntityId(99), path).ShouldBeFalse();
        h.Queries.TryFindViewPath(EntityId.None, a, path).ShouldBeFalse();
        path.ShouldBeEmpty();
    }

    [Fact]
    public void ViewPath_FromABuildingToItself_IsItsFirstAccessTile()
    {
        var h = new RoadsHarness();
        EntityId hut = h.Add("hut", 5, 5);
        h.PlaceDirt((6, 5), (5, 6));
        var path = new List<TileCoord>();

        h.Queries.TryFindViewPath(hut, hut, path).ShouldBeTrue();

        path.ShouldBe([T(6, 5)]);
    }

    [Fact]
    public void ViewPath_TileOverload_NeedsRoadsAtBothEnds()
    {
        var h = new RoadsHarness();
        h.DirtLine((2, 2), (6, 2));
        var path = new List<TileCoord>();

        h.Queries.TryFindViewPath(T(2, 2), T(6, 2), path).ShouldBeTrue();
        path.Count.ShouldBe(5);
        h.Queries.TryFindViewPath(T(2, 2), T(7, 2), path).ShouldBeFalse();
        h.Queries.TryFindViewPath(T(2, 3), T(6, 2), path).ShouldBeFalse();
        h.Queries.TryFindViewPath(T(-1, 2), T(6, 2), path).ShouldBeFalse();
        h.Queries.TryFindViewPath(T(2, 2), T(60, 2), path).ShouldBeFalse();
        path.ShouldBeEmpty();
        h.Queries.TryFindViewPath(T(4, 2), T(4, 2), path).ShouldBeTrue();
        path.ShouldBe([T(4, 2)]);
    }

    [Fact]
    public void ViewPath_BeforeAnyRoadExists_FindsNothing()
    {
        var h = new RoadsHarness();
        EntityId a = h.Add("hut", 2, 3);
        var path = new List<TileCoord>();

        h.Queries.TryFindViewPath(a, a, path).ShouldBeFalse();
        h.Queries.TryFindViewPath(T(1, 1), T(2, 2), path).ShouldBeFalse();
    }

    [Fact]
    public void ViewPath_OnAnOpenGridOfEqualRoutes_IsTheSameEveryTimeAndInEveryWorld()
    {
        List<TileCoord> Run(RoadsHarness h)
        {
            var path = new List<TileCoord>();
            h.Queries.TryFindViewPath(T(0, 0), T(6, 6), path).ShouldBeTrue();
            return path;
        }

        RoadsHarness MakeGrid()
        {
            var h = new RoadsHarness();
            for (int y = 0; y <= 6; y++)
            {
                h.DirtLine((0, y), (6, y));
            }

            return h;
        }

        RoadsHarness first = MakeGrid();
        RoadsHarness second = MakeGrid();

        List<TileCoord> a = Run(first);

        a.Count.ShouldBe(13, "a shortest route over a 7 x 7 grid has 13 tiles");
        Run(first).ShouldBe(a);
        Run(second).ShouldBe(a);
    }

    [Fact]
    public void ViewPath_DoesNotChangeTheState()
    {
        var h = new RoadsHarness();
        EntityId a = h.Add("hut", 2, 3);
        EntityId b = h.Add("hut", 8, 3);
        h.DirtLine((2, 2), (8, 2));
        ulong before = h.StateHash();
        int builds = h.Network.BuildCount;

        h.Queries.TryFindViewPath(a, b, []);
        h.Queries.TryFindViewPath(T(2, 2), T(8, 2), []);

        h.StateHash().ShouldBe(before);
        h.Network.BuildCount.ShouldBe(builds);
    }
}
