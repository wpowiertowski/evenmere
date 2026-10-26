using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;

namespace Evenmere.Sim.Tests.Roads.Bridges;

/// <summary>Bridge tiles in the distance fields: each tile costs its own type, checked against brute force.</summary>
public sealed class BridgeFieldTests
{
    private const int Size = 32;

    private static int Index(int x, int y) => (y * Size) + x;

    private static RoadsHarness Harness() => new(Size, BridgeContent.Db);

    private static TileInfo River(RiverWidthClass width) =>
        new(BridgeContent.Terrain("plains"), 0, SlopeClass.Flat, TileFeatures.River, width, 0, null, 0);

    private static void PaintRiver(RoadsHarness h, int fromX, int toX)
    {
        for (int y = 0; y < Size; y++)
        {
            for (int x = fromX; x <= toX; x++)
            {
                h.Sim.Queries.Map.SetTile(x, y, River(RiverWidthClass.Narrow));
            }
        }
    }

    private static EntityId AddStorehouse(RoadsHarness h) =>
        h.Sim.Services.BuildingRegistry.Add(BridgeContent.Building("storehouse"), 2, 2, Rotation.Deg0, BuildingState.Active);

    // The island both tests build on: a 3-wide narrow river with a timber bridge at y = 4 and dirt roads on both banks.
    private static RoadsHarness Crossing()
    {
        RoadsHarness h = Harness();
        h.Unlock(BridgeContent.Timber);
        h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
        PaintRiver(h, 10, 12);
        AddStorehouse(h);

        h.Place(BridgeContent.Dirt, (2, 4), (3, 4), (4, 4), (5, 4), (6, 4), (7, 4), (8, 4), (9, 4));
        h.Place(BridgeContent.Timber, (10, 4), (11, 4), (12, 4));
        h.Place(BridgeContent.Dirt, (13, 4), (14, 4), (15, 4), (16, 4), (17, 4), (18, 4), (19, 4), (20, 4));
        return h;
    }

    private static Dictionary<int, int> Entries(RoadsHarness h, EntityId source)
    {
        h.Network.TryGetFieldEntries(source, out int[] tiles, out int[] costs).ShouldBeTrue();
        return tiles.Zip(costs).ToDictionary(static p => p.First, static p => p.Second);
    }

    private static (int[] Tiles, int[] Costs) ExpectBruteForce(RoadsHarness h, BuildingInfo source)
    {
        Content.ContentDb content = BridgeContent.Db;
        ushort[] layer = h.Sim.World.Roads.Layer;
        int CostAt(int x, int y)
        {
            ushort v = layer.Length == 0 ? (ushort)0 : layer[Index(x, y)];
            return v == 0 ? 0 : content.Buildings.Items[v - 1].RoadCost ?? 0;
        }

        var reference = new BruteForce(Size, Size, CostAt);
        int[] dist = reference.Relaxed(reference.Access(source));
        int bound = Math.Max(BruteForce.StorageBound(content, source.Type), BruteForce.ServiceBound(content, source.Type));
        return BruteForce.Within(dist, bound);
    }

    [Fact]
    public void Field_AcrossABridge_ChargesEachTileItsOwnCost()
    {
        RoadsHarness h = Crossing();
        EntityId storehouse = h.Sim.Queries.Map.AllBuildings.Single().Id;

        Dictionary<int, int> field = Entries(h, storehouse);

        // The storehouse doors are the dirt tiles at (2, 4) and (3, 4); every dirt step adds 1000, every bridge step 1500.
        field[Index(2, 4)].ShouldBe(0);
        field[Index(9, 4)].ShouldBe(6000);
        field[Index(10, 4)].ShouldBe(7500);
        field[Index(11, 4)].ShouldBe(9000);
        field[Index(12, 4)].ShouldBe(10500);
        field[Index(13, 4)].ShouldBe(11500);
    }

    [Fact]
    public void Field_AcrossABridge_EqualsBruteForce()
    {
        RoadsHarness h = Crossing();
        BuildingInfo storehouse = h.Sim.Queries.Map.AllBuildings.Single();

        (int[] expectedTiles, int[] expectedCosts) = ExpectBruteForce(h, storehouse);

        h.Network.TryGetFieldEntries(storehouse.Id, out int[] actualTiles, out int[] actualCosts).ShouldBeTrue();
        actualTiles.ShouldBe(expectedTiles);
        actualCosts.ShouldBe(expectedCosts);
    }

    [Fact]
    public void Fields_WithBridgesAndRandomBanks_MatchBruteForce()
    {
        int checkedTiles = 0;
        for (int seed = 0; seed < 30; seed++)
        {
            var random = new Random(seed);
            RoadsHarness h = Crossing();

            // Sprinkle dirt on the banks (never on the river, never on the storehouse): the comparison below replays
            // the same layout through the reference implementation.
            var sprinkle = new List<TileCoord>();
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    bool river = x is >= 10 and <= 12;
                    bool footprint = x is 2 or 3 && y is 2 or 3;
                    if (!river && !footprint && random.NextDouble() < 0.15)
                    {
                        sprinkle.Add(new TileCoord(x, y));
                    }
                }
            }

            if (sprinkle.Count > 0)
            {
                h.Place(BridgeContent.Dirt, [.. sprinkle.Select(static t => (t.X, t.Y))]);
            }

            BuildingInfo storehouse = h.Sim.Queries.Map.AllBuildings.Single();
            (int[] expectedTiles, int[] expectedCosts) = ExpectBruteForce(h, storehouse);
            h.Network.TryGetFieldEntries(storehouse.Id, out int[] actualTiles, out int[] actualCosts).ShouldBeTrue();
            actualTiles.ShouldBe(expectedTiles, $"seed {seed}");
            actualCosts.ShouldBe(expectedCosts, $"seed {seed}");
            checkedTiles += actualTiles.Length;
        }

        checkedTiles.ShouldBeGreaterThan(500);
    }

    [Fact]
    public void Bridge_BuiltInDifferentCommandOrders_HasTheSameHash()
    {
        static RoadsHarness Built(bool bridgeFirst)
        {
            RoadsHarness h = Harness();
            h.Unlock(BridgeContent.Timber);
            h.Sim.Queries.Progression.Open(TerrainAccess.NarrowRivers);
            PaintRiver(h, 10, 12);
            AddStorehouse(h);

            (int X, int Y)[] west = [(2, 4), (3, 4), (4, 4), (5, 4), (6, 4), (7, 4), (8, 4), (9, 4)];
            (int X, int Y)[] bridge = [(10, 4), (11, 4), (12, 4)];
            (int X, int Y)[] east = [(13, 4), (14, 4), (15, 4)];
            if (bridgeFirst)
            {
                h.Place(BridgeContent.Timber, bridge);
                h.Place(BridgeContent.Dirt, east);
                h.Place(BridgeContent.Dirt, west);
            }
            else
            {
                h.Place(BridgeContent.Dirt, west);
                h.Place(BridgeContent.Dirt, east);
                h.Place(BridgeContent.Timber, bridge);
            }

            return h;
        }

        RoadsHarness a = Built(bridgeFirst: false);
        RoadsHarness b = Built(bridgeFirst: true);

        b.StateHash().ShouldBe(a.StateHash());

        // And a save/load round trip keeps the hash: the layer holds the bridge types, the fields rebuild from it.
        WorldState restored = StateSerializer.Read(StateSerializer.Write(a.Sim.World), a.Sim.Content);
        StateSerializer.ComputeHash(restored).ShouldBe(a.StateHash());
    }
}
