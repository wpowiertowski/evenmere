using System.Diagnostics;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Roads;
using Evenmere.Sim.Systems.Roads.Commands;

namespace Evenmere.Sim.Tests.Roads;

/// <summary>The distance fields: costs on hand-drawn layouts, a property test against brute force, and what a change rebuilds.</summary>
public sealed class DistanceFieldTests(ITestOutputHelper output)
{
    private const int Width = 32;

    private static int Index(int x, int y) => (y * Width) + x;

    private static (int Tile, int Cost)[] Entries(RoadNetwork network, EntityId source)
    {
        network.TryGetFieldEntries(source, out int[] tiles, out int[] costs).ShouldBeTrue();
        return [.. tiles.Zip(costs)];
    }

    // --- Costs on hand-drawn layouts ---

    [Fact]
    public void Field_StraightDirtRoad_GrowsByOneTileOfCostAndStopsAtTheReach()
    {
        var h = new RoadsHarness();
        EntityId depot = h.Add("depot", 1, 1);

        // The depot's door is the road tile below it, at (1, 2). The road runs on to x = 12, but the depot's reach is 5 tiles.
        h.DirtLine((1, 2), (12, 2));

        Entries(h.Network, depot).ShouldBe(Enumerable.Range(0, 6).Select(i => (Index(1 + i, 2), i * 1000)).ToArray());
    }

    [Fact]
    public void Field_EnteringATile_CostsThatTilesRoadType()
    {
        var h = new RoadsHarness();
        h.Unlock(RoadsHarness.Cobbled, RoadsHarness.Paved);
        EntityId depot = h.Add("depot", 1, 1);

        h.Place(RoadsHarness.Paved, (1, 2));
        h.Place(RoadsHarness.Cobbled, (2, 2));
        h.Place(RoadsHarness.Paved, (3, 2));
        h.Place(RoadsHarness.Dirt, (4, 2));

        // The door tile is where the search starts, so it costs 0 whatever it is made of; each further tile costs what it costs to enter.
        Entries(h.Network, depot).ShouldBe([(Index(1, 2), 0), (Index(2, 2), 800), (Index(3, 2), 1450), (Index(4, 2), 2450)]);
    }

    [Fact]
    public void Field_ReachBoundIsInclusive()
    {
        var h = new RoadsHarness();
        EntityId depot = h.Add("depot", 1, 1);
        h.DirtLine((1, 2), (8, 2));

        int[] costs = Entries(h.Network, depot).Select(static e => e.Cost).ToArray();

        costs.ShouldContain(5000);
        costs.ShouldNotContain(6000);
    }

    [Fact]
    public void Field_AroundACorner_AddsEachStep()
    {
        var h = new RoadsHarness();
        EntityId depot = h.Add("depot", 1, 1);
        h.DirtLine((1, 2), (3, 2));
        h.DirtLine((3, 3), (3, 5));

        Dictionary<int, int> field = Entries(h.Network, depot).ToDictionary(static e => e.Tile, static e => e.Cost);

        field[Index(3, 2)].ShouldBe(2000);
        field[Index(3, 5)].ShouldBe(5000);
        field.Count.ShouldBe(6);
    }

    [Fact]
    public void Field_ARoadNotConnectedToTheDoor_IsLeftOut()
    {
        var h = new RoadsHarness();
        EntityId depot = h.Add("depot", 1, 1);
        h.DirtLine((1, 2), (3, 2));
        h.DirtLine((5, 2), (7, 2));

        Entries(h.Network, depot).Select(static e => e.Tile).ShouldBe([Index(1, 2), Index(2, 2), Index(3, 2)]);
    }

    [Fact]
    public void Field_EveryAccessTileStartsAtZero()
    {
        var h = new RoadsHarness();
        EntityId shrine = h.Add("shrine", 5, 5);

        // Two doors on different sides of the shrine, not joined to each other: each is a start of the search.
        h.Place(RoadsHarness.Dirt, (5, 4), (4, 6));

        Dictionary<int, int> field = Entries(h.Network, shrine).ToDictionary(static e => e.Tile, static e => e.Cost);

        field[Index(5, 4)].ShouldBe(0);
        field[Index(4, 6)].ShouldBe(0);
    }

    [Fact]
    public void Field_ABuildingWithBothStorageAndService_IsSearchedToTheLargerBound()
    {
        var h = new RoadsHarness(size: 48);
        EntityId hall = h.Add("founders_hall", 0, 0);

        h.DirtLine((0, 4), (40, 4));

        // Reach 24 and coverage 16: the field holds the larger, and queries compare with their own bound.
        Entries(h.Network, hall).Max(static e => e.Cost).ShouldBe(24000);
    }

    [Fact]
    public void Field_ABuildingThatIsNeitherStorehouseNorService_HasNone()
    {
        var h = new RoadsHarness();
        h.Add("hut", 1, 1);
        h.Add("settler_house", 6, 1);
        h.DirtLine((1, 2), (8, 2));

        h.Network.FieldCount.ShouldBe(0);
        h.Network.TryGetFieldEntries(new EntityId(1), out _, out _).ShouldBeFalse();
    }

    // --- The property test: fields against brute force ---

    [Fact]
    public void Fields_OnRandom32By32Layouts_MatchBruteForce()
    {
        long reloadTicks = 0;
        int fieldsChecked = 0;
        int tilesChecked = 0;

        for (int seed = 0; seed < 120; seed++)
        {
            var random = new Random(seed);
            bool dirtOnly = seed % 3 == 0;
            RoadsHarness h = RandomWorld(random, seed % 2 == 0, dirtOnly);

            // Fields as maintained by the lifecycle and the commands, as the city was built.
            (int fields, int tiles) = AssertMatchesBruteForce(h, h.Roads, dirtOnly);
            fieldsChecked += fields;
            tilesChecked += tiles;

            // The same fields built from nothing, as after a load: one full rebuild from the layer and the registry.
            RoadsSystem reloaded = h.Reload();
            long started = Stopwatch.GetTimestamp();
            reloaded.Network.FieldCount.ShouldBeGreaterThanOrEqualTo(0);
            reloadTicks += Stopwatch.GetTimestamp() - started;
            AssertSameAnswers(h, h.Roads, reloaded);

            // Then change the city: remove a quarter of the roads, upgrade some, demolish a source, and check both again.
            ChangeRandomly(random, h, dirtOnly);
            AssertMatchesBruteForce(h, h.Roads, dirtOnly);
            AssertSameAnswers(h, h.Roads, h.Reload());
        }

        double millisPerReload = reloadTicks * 1000.0 / Stopwatch.Frequency / 120;
        output.WriteLine($"{fieldsChecked} fields and {tilesChecked} field tiles checked against brute force on 120 random 32 x 32 layouts; a full rebuild of every field took {millisPerReload:F3} ms on average.");
        fieldsChecked.ShouldBeGreaterThan(400);
    }

    [Fact]
    public void Fields_AfterPlacingTheSameRoadsInAnotherOrder_AreTheSame()
    {
        var random = new Random(7);
        RoadsHarness a = RandomWorld(random, buildingsFirst: true, dirtOnly: false);
        var b = new RoadsHarness();
        b.Unlock(RoadsHarness.Cobbled, RoadsHarness.Paved);

        // Rebuild the same city in a second world, buildings last and roads in reverse, then compare every answer.
        foreach (BuildingInfo building in a.Sim.Queries.Map.AllBuildings)
        {
            b.Sim.Queries.Map.AddBuilding(building);
        }

        ushort[] layer = a.Sim.World.Roads.Layer;
        for (int i = layer.Length - 1; i >= 0; i--)
        {
            if (layer[i] != 0)
            {
                b.Place(new BuildingTypeId((ushort)(layer[i] - 1)), (i % Width, i / Width));
            }
        }

        b.Sim.World.Roads.Layer.ShouldBe(layer);
        foreach (BuildingInfo building in a.Sim.Queries.Map.AllBuildings)
        {
            b.Sim.Services.BuildingLifecycle.RaiseActivated(building);
        }

        AssertSameAnswers(a, a.Roads, b.Roads);
    }

    // --- What a change rebuilds ---

    // Two depots (reach 5) far apart, each with a road running east from its door.
    private static (RoadsHarness H, EntityId A, EntityId B) TwoDepots()
    {
        var h = new RoadsHarness(size: 96);
        h.Unlock(RoadsHarness.Cobbled);
        EntityId a = h.Add("depot", 5, 5);
        EntityId b = h.Add("depot", 80, 80);
        h.DirtLine((5, 6), (14, 6));
        h.DirtLine((80, 81), (89, 81));
        return (h, a, b);
    }

    [Fact]
    public void Invalidation_RoadFarFromEveryField_RebuildsNothing()
    {
        (RoadsHarness h, EntityId a, EntityId b) = TwoDepots();
        int builds = h.Network.BuildCount;
        int revisionA = h.Network.RevisionOf(a);
        int revisionB = h.Network.RevisionOf(b);

        h.DirtLine((40, 40), (50, 40));
        h.RemoveRoads((45, 40));
        h.Place(RoadsHarness.Cobbled, (41, 40));

        h.Network.BuildCount.ShouldBe(builds);
        h.Network.RevisionOf(a).ShouldBe(revisionA);
        h.Network.RevisionOf(b).ShouldBe(revisionB);
    }

    [Fact]
    public void Invalidation_RoadInsideOneField_RebuildsThatFieldOnly()
    {
        (RoadsHarness h, EntityId a, EntityId b) = TwoDepots();
        int revisionA = h.Network.RevisionOf(a);
        int revisionB = h.Network.RevisionOf(b);
        int builds = h.Network.BuildCount;

        h.Place(RoadsHarness.Cobbled, (7, 6));

        h.Network.RevisionOf(a).ShouldNotBe(revisionA);
        h.Network.RevisionOf(b).ShouldBe(revisionB);
        h.Network.BuildCount.ShouldBe(builds + 1);
        Entries(h.Network, a).Single(e => e.Tile == (6 * 96) + 8).Cost.ShouldBe(2800, "1000 + 800 + 1000 up to x = 8");
    }

    [Fact]
    public void Invalidation_RoadBesideTheEdgeOfAField_RebuildsItWithoutChangingIt()
    {
        (RoadsHarness h, EntityId a, _) = TwoDepots();
        (int Tile, int Cost)[] before = Entries(h.Network, a);
        int revision = h.Network.RevisionOf(a);

        // (10, 6) is the last tile within reach and (11, 6) lies just beyond it: a cheaper tile there could extend the field.
        h.Place(RoadsHarness.Cobbled, (11, 6));

        h.Network.RevisionOf(a).ShouldNotBe(revision);
        Entries(h.Network, a).ShouldBe(before);
    }

    [Fact]
    public void Invalidation_RoadTwoTilesBeyondTheEdge_RebuildsNothing()
    {
        (RoadsHarness h, _, _) = TwoDepots();
        int builds = h.Network.BuildCount;

        h.Place(RoadsHarness.Cobbled, (13, 6));
        h.RemoveRoads((14, 6));

        h.Network.BuildCount.ShouldBe(builds);
    }

    [Fact]
    public void Invalidation_RemovingARoadInsideAField_ShrinksItAndLeavesTheOtherAlone()
    {
        (RoadsHarness h, EntityId a, EntityId b) = TwoDepots();
        int revisionB = h.Network.RevisionOf(b);

        h.RemoveRoads((8, 6));

        Entries(h.Network, a).Select(static e => e.Cost).ShouldBe([0, 1000, 2000]);
        h.Network.RevisionOf(b).ShouldBe(revisionB);
    }

    [Fact]
    public void Invalidation_FirstRoadBesideASourceWithNone_BuildsItsFieldOnly()
    {
        (RoadsHarness h, EntityId a, EntityId b) = TwoDepots();
        EntityId lonely = h.Add("depot", 50, 5);
        Entries(h.Network, lonely).ShouldBeEmpty();
        int revisionA = h.Network.RevisionOf(a);
        int revisionB = h.Network.RevisionOf(b);
        int revisionLonely = h.Network.RevisionOf(lonely);

        h.DirtLine((50, 6), (52, 6));

        h.Network.RevisionOf(lonely).ShouldNotBe(revisionLonely);
        Entries(h.Network, lonely).Select(static e => e.Cost).ShouldBe([0, 1000, 2000]);
        h.Network.RevisionOf(a).ShouldBe(revisionA);
        h.Network.RevisionOf(b).ShouldBe(revisionB);
    }

    [Fact]
    public void Invalidation_AddingASource_BuildsOnlyItsOwnField()
    {
        (RoadsHarness h, EntityId a, EntityId b) = TwoDepots();
        int builds = h.Network.BuildCount;
        int revisionA = h.Network.RevisionOf(a);

        h.Add("hut", 30, 30);
        h.Network.BuildCount.ShouldBe(builds);

        EntityId added = h.Add("depot", 60, 60);

        h.Network.BuildCount.ShouldBe(builds + 1);
        h.Network.RevisionOf(added).ShouldBeGreaterThan(0);
        h.Network.RevisionOf(a).ShouldBe(revisionA);
        h.Network.RevisionOf(b).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Invalidation_RemovingASource_DropsItsFieldAndLeavesTheOthers()
    {
        (RoadsHarness h, EntityId a, EntityId b) = TwoDepots();
        int builds = h.Network.BuildCount;
        int revisionA = h.Network.RevisionOf(a);

        h.Remove(b);

        h.Network.RevisionOf(b).ShouldBe(-1);
        h.Network.FieldCount.ShouldBe(1);
        h.Network.RevisionOf(a).ShouldBe(revisionA);
        h.Network.BuildCount.ShouldBe(builds);
    }

    [Fact]
    public void Invalidation_ABigDragCrossingOneFieldOnly_RebuildsOnlyThatField()
    {
        (RoadsHarness h, EntityId a, EntityId b) = TwoDepots();
        int revisionB = h.Network.RevisionOf(b);

        // A column down from the depot's road at x = 4 joins the end of the road at (5, 6), so it extends the field by five tiles.
        h.Send(new PlaceRoad(RoadsHarness.Straight((4, 20), (4, 6)), RoadsHarness.Dirt));

        h.Network.RevisionOf(b).ShouldBe(revisionB);
        Entries(h.Network, a).Length.ShouldBe(11);
    }

    // --- Helpers for the property test ---

    private static RoadsHarness RandomWorld(Random random, bool buildingsFirst, bool dirtOnly)
    {
        var h = new RoadsHarness();
        h.Unlock(RoadsHarness.Cobbled, RoadsHarness.Paved);
        bool[] taken = new bool[Width * Width];
        var plans = new List<(string Type, int X, int Y)>();
        string[] sourceTypes = ["depot", "storehouse", "stall", "market", "shrine", "founders_hall"];
        for (int i = 0; i < 18; i++)
        {
            string type = i < 5 + random.Next(3) ? sourceTypes[random.Next(sourceTypes.Length)] : "hut";
            Footprint footprint = RoadContent.Db.Get(RoadContent.Building(type)).Footprint;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int x = random.Next(Width - footprint.Width + 1);
                int y = random.Next(Width - footprint.Height + 1);
                if (Free(taken, x, y, footprint))
                {
                    plans.Add((type, x, y));
                    break;
                }
            }
        }

        void AddBuildings()
        {
            foreach ((string type, int x, int y) in plans)
            {
                h.Add(type, x, y);
            }
        }

        void LayRoads()
        {
            double density = 0.3 + (random.NextDouble() * 0.4);
            var byType = new List<TileCoord>[3] { [], [], [] };
            foreach ((string type, int x, int y) in plans)
            {
                Footprint f = RoadContent.Db.Get(RoadContent.Building(type)).Footprint;
                for (int dy = 0; dy < f.Height; dy++)
                {
                    for (int dx = 0; dx < f.Width; dx++)
                    {
                        taken[Index(x + dx, y + dy)] = true;
                    }
                }
            }

            for (int i = 0; i < taken.Length; i++)
            {
                if (!taken[i] && random.NextDouble() < density)
                {
                    byType[dirtOnly ? 0 : random.Next(3)].Add(new TileCoord(i % Width, i / Width));
                }
            }

            BuildingTypeId[] types = [RoadsHarness.Dirt, RoadsHarness.Cobbled, RoadsHarness.Paved];
            for (int t = 0; t < 3; t++)
            {
                if (byType[t].Count > 0)
                {
                    h.Send(new PlaceRoad([.. byType[t]], types[t]));
                }
            }
        }

        // Footprints must be known to the road layout either way, so mark them before laying roads.
        if (buildingsFirst)
        {
            AddBuildings();
            LayRoads();
        }
        else
        {
            LayRoads();
            AddBuildings();
        }

        return h;
    }

    private static bool Free(bool[] taken, int x, int y, Footprint footprint)
    {
        // A one-tile gap is not required: neighbouring buildings are fine. Only overlap is refused, against what is already planned.
        for (int dy = 0; dy < footprint.Height; dy++)
        {
            for (int dx = 0; dx < footprint.Width; dx++)
            {
                if (taken[Index(x + dx, y + dy)])
                {
                    return false;
                }
            }
        }

        for (int dy = 0; dy < footprint.Height; dy++)
        {
            for (int dx = 0; dx < footprint.Width; dx++)
            {
                taken[Index(x + dx, y + dy)] = true;
            }
        }

        return true;
    }

    private static void ChangeRandomly(Random random, RoadsHarness h, bool dirtOnly)
    {
        ushort[] layer = h.Sim.World.Roads.Layer;
        var roads = new List<(int X, int Y)>();
        for (int i = 0; i < layer.Length; i++)
        {
            if (layer[i] != 0)
            {
                roads.Add((i % Width, i / Width));
            }
        }

        h.RemoveRoads([.. roads.Where(_ => random.Next(4) == 0)]);
        if (!dirtOnly)
        {
            h.Place(RoadsHarness.Paved, [.. roads.Where(_ => random.Next(5) == 0)]);
        }

        BuildingInfo[] sources = [.. h.Sim.Queries.Map.AllBuildings.Where(b => RoadContent.Db.Get(b.Type).Storage is not null || RoadContent.Db.Get(b.Type).Service is not null)];
        if (sources.Length > 1)
        {
            h.Remove(sources[random.Next(sources.Length)].Id);
        }
    }

    private static (int Fields, int Tiles) AssertMatchesBruteForce(RoadsHarness h, RoadsSystem system, bool dirtOnly)
    {
        Content.ContentDb content = RoadContent.Db;
        ushort[] layer = h.Sim.World.Roads.Layer;
        int CostAt(int x, int y)
        {
            ushort v = layer.Length == 0 ? (ushort)0 : layer[Index(x, y)];
            return v == 0 ? 0 : content.Buildings.Items[v - 1].RoadCost ?? 0;
        }

        var reference = new BruteForce(Width, Width, CostAt);
        var model = new Dictionary<EntityId, (BuildingInfo Building, int[] Dist)>();
        int fields = 0;
        int tiles = 0;
        foreach (BuildingInfo b in h.Sim.Queries.Map.AllBuildings)
        {
            int storage = BruteForce.StorageBound(content, b.Type);
            int service = BruteForce.ServiceBound(content, b.Type);
            if (storage == 0 && service == 0)
            {
                system.Network.TryGetFieldEntries(b.Id, out _, out _).ShouldBeFalse();
                continue;
            }

            List<int> access = layer.Length == 0 ? [] : reference.Access(b);
            int[] dist = reference.Relaxed(access);
            if (dirtOnly)
            {
                reference.BreadthFirst(access, 1000).ShouldBe(dist, "breadth-first search and relaxation disagree on a dirt-only layout");
            }

            (int[] expectedTiles, int[] expectedCosts) = BruteForce.Within(dist, Math.Max(storage, service));
            system.Network.TryGetFieldEntries(b.Id, out int[] actualTiles, out int[] actualCosts).ShouldBeTrue();
            actualTiles.ShouldBe(expectedTiles, $"field tiles of {b.Id}");
            actualCosts.ShouldBe(expectedCosts, $"field costs of {b.Id}");
            model[b.Id] = (b, dist);
            fields++;
            tiles += actualTiles.Length;
        }

        // The derived lookups, for every building: storehouse reach, nearest storehouse and service coverage.
        foreach (BuildingInfo b in h.Sim.Queries.Map.AllBuildings)
        {
            List<int> access = layer.Length == 0 ? [] : reference.Access(b);
            int Cost(EntityId source, int bound)
            {
                if (source == b.Id)
                {
                    return 0;
                }

                int best = BruteForce.Far;
                foreach (int tile in access)
                {
                    best = Math.Min(best, model[source].Dist[tile]);
                }

                return best <= bound ? best : BruteForce.Far;
            }

            EntityId nearest = EntityId.None;
            int nearestCost = BruteForce.Far;
            foreach ((EntityId source, (BuildingInfo sb, _)) in model.OrderBy(static p => p.Key))
            {
                int bound = BruteForce.StorageBound(content, sb.Type);
                if (bound == 0)
                {
                    continue;
                }

                int cost = Cost(source, bound);
                system.Queries.TryGetStorehouseCost(source, b.Id, out int actualCost).ShouldBe(cost != BruteForce.Far, $"{source} -> {b.Id}");
                if (cost != BruteForce.Far)
                {
                    actualCost.ShouldBe(cost);
                }

                if (cost < nearestCost)
                {
                    nearest = source;
                    nearestCost = cost;
                }
            }

            system.Queries.IsInStorehouseReach(b.Id).ShouldBe(!nearest.IsNone, $"reach of {b.Id}");
            system.Queries.TryNearestStorehouse(b.Id, out EntityId actualNearest, out int actualNearestCost).ShouldBe(!nearest.IsNone);
            if (!nearest.IsNone)
            {
                actualNearest.ShouldBe(nearest);
                actualNearestCost.ShouldBe(nearestCost);
            }

            foreach (ServiceKind kind in Enum.GetValues<ServiceKind>())
            {
                bool covered = model.Any(p =>
                {
                    ServiceKind? provided = BruteForce.ServiceOf(content, p.Value.Building.Type);
                    int bound = BruteForce.ServiceBound(content, p.Value.Building.Type);
                    if (provided != kind || bound == 0 || Cost(p.Key, bound) == BruteForce.Far)
                    {
                        return false;
                    }

                    return kind != ServiceKind.Market || MarketIsInReach(p.Value.Building);
                });
                system.Queries.IsCovered(b.Id, kind).ShouldBe(covered, $"{kind} coverage of {b.Id}");
            }
        }

        return (fields, tiles);

        bool MarketIsInReach(BuildingInfo market)
        {
            List<int> marketAccess = layer.Length == 0 ? [] : reference.Access(market);
            foreach ((EntityId source, (BuildingInfo sb, int[] dist)) in model)
            {
                int bound = BruteForce.StorageBound(content, sb.Type);
                if (bound == 0)
                {
                    continue;
                }

                if (source == market.Id || marketAccess.Any(tile => dist[tile] <= bound))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Two systems over the same world must answer every question alike: that is incremental maintenance against a rebuild.
    internal static void AssertSameAnswers(RoadsHarness h, RoadsSystem expected, RoadsSystem actual)
    {
        expected.Network.FieldCount.ShouldBe(actual.Network.FieldCount);
        foreach (BuildingInfo b in h.Sim.Queries.Map.AllBuildings)
        {
            bool has = expected.Network.TryGetFieldEntries(b.Id, out int[] tiles, out int[] costs);
            actual.Network.TryGetFieldEntries(b.Id, out int[] otherTiles, out int[] otherCosts).ShouldBe(has);
            otherTiles.ShouldBe(tiles);
            otherCosts.ShouldBe(costs);

            actual.Queries.HasRoadAccess(b.Id).ShouldBe(expected.Queries.HasRoadAccess(b.Id));
            actual.Queries.TryNearestStorehouse(b.Id, out EntityId store, out int cost).ShouldBe(expected.Queries.TryNearestStorehouse(b.Id, out EntityId wantedStore, out int wantedCost));
            store.ShouldBe(wantedStore);
            cost.ShouldBe(wantedCost);
            foreach (ServiceKind kind in Enum.GetValues<ServiceKind>())
            {
                actual.Queries.IsCovered(b.Id, kind).ShouldBe(expected.Queries.IsCovered(b.Id, kind));
            }
        }
    }
}
