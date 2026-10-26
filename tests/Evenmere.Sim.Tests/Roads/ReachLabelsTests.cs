using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Core.Rng;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Map.Generation;
using Evenmere.Sim.Tests.MapGeneration.Guarantee;

namespace Evenmere.Sim.Tests.Roads;

/// <summary>
/// The per-epoch reach labels agree with the field scan everywhere: on the starter island and several generated
/// islands, with random road and storehouse layouts from a seeded <see cref="Pcg32"/>, every building's boolean
/// <see cref="Roads.RoadNetwork.IsInStorehouseReach"/> answer must equal the old all-fields scan
/// (<see cref="Roads.RoadNetwork.TryNearestStorehouse"/>, which keeps the exact current semantics). The layouts are
/// rebuilt from the same seed for a determinism check, then mutated through the command and lifecycle paths so the
/// labels rebuild on roads-epoch bumps, and every building is compared again after each mutation.
/// </summary>
public sealed class ReachLabelsTests
{
    [Theory]
    [InlineData(-1, 128)]
    [InlineData(7, 256)]
    [InlineData(8, 256)]
    [InlineData(9, 256)]
    public void ReachLabels_MatchFieldScan_OnEveryBuilding(int seed, int size)
    {
        ContentDb content = GeneratedIslandFixture.Content;
        MapDef island = seed < 0
            ? MapLoader.Load([GeneratedIslandFixture.ContentRoot], "starter", content).GetMapOrThrow()
            : IslandGenerator.Generate(seed, size, content);
        island.Width.ShouldBe(size);
        island.Height.ShouldBe(size);

        bool[] first = ArrangeAndCompare(content, island, (ulong)(seed + 1000));
        bool[] second = ArrangeAndCompare(content, island, (ulong)(seed + 1000));
        second.ShouldBe(first, "the same seed must arrange the same labels");
    }

    private static bool[] ArrangeAndCompare(ContentDb content, MapDef island, ulong stream)
    {
        var rng = new Pcg32((ulong)island.Width * 1000003UL + (ulong)island.TileCount, stream);
        var h = new RoadsHarness(size: island.Width, content);
        h.Sim.Queries.Map.Width = island.Width;
        h.Sim.Queries.Map.Height = island.Height;
        h.Sim.Queries.Map.DefaultTile = new TileInfo(
            FirstBuildableTerrain(content), 0, SlopeClass.Flat, TileFeatures.None, RiverWidthClass.None, 0, null, 0);

        BuildingTypeId storehouse = content.Buildings.IdOf("storehouse");
        BuildingTypeId market = content.Buildings.IdOf("market");
        BuildingTypeId house = content.Buildings.IdOf("settler_house");
        BuildingTypeId dirt = content.Buildings.IdOf("dirt_road");
        BuildingTypeId cobbled = content.Buildings.IdOf("cobbled_road");
        BuildingTypeId bridge = content.Buildings.IdOf("timber_bridge");
        h.Unlock(dirt, cobbled, bridge);

        bool[] buildable = new bool[island.TileCount];
        var land = new List<int>();
        for (int i = 0; i < buildable.Length; i++)
        {
            buildable[i] = content.Get(island.Terrain[i]).Buildable;
            if (buildable[i])
            {
                land.Add(i);
            }
        }

        land.Count.ShouldBeGreaterThan(0, "the island must have buildable land");

        bool[] occupied = new bool[island.TileCount];
        var buildings = new List<EntityId>();
        var stores = new List<EntityId>();
        PaintRoads(h, rng, island, [dirt, cobbled, bridge]);

        for (int i = 0; i < 10; i++)
        {
            stores.Add(Place(content, h, rng, island, land, buildable, occupied, storehouse));
        }

        for (int i = 0; i < 6; i++)
        {
            buildings.Add(Place(content, h, rng, island, land, buildable, occupied, market));
        }

        for (int i = 0; i < 120; i++)
        {
            buildings.Add(Place(content, h, rng, island, land, buildable, occupied, house));
        }

        buildings.AddRange(stores);

        bool[] answers = CompareAll(h, buildings);
        answers.ShouldContain(true, "storehouses are in their own reach");
        answers.ShouldContain(false, "houses far from any road are not");

        // Mutations through the lifecycle and command paths bump the roads epoch; the labels must rebuild and agree again.
        // First, targeted roads through the command path: a dirt row along the top edge of three storehouses.
        foreach (EntityId store in stores.GetRange(1, 3))
        {
            if (h.Sim.Queries.Map.TryGetBuilding(store, out BuildingInfo info) && info.Origin.Y > 0)
            {
                var row = new (int X, int Y)[info.Width];
                for (int dx = 0; dx < info.Width; dx++)
                {
                    row[dx] = (info.Origin.X + dx, info.Origin.Y - 1);
                }

                h.Place(dirt, row);
            }
        }

        CompareAll(h, buildings);

        h.Remove(stores[0]);
        buildings.Remove(stores[0]);
        CompareAll(h, buildings);

        var from = new TileCoord(rng.NextInt(island.Width), rng.NextInt(island.Height));
        var to = new TileCoord(rng.NextInt(island.Width), from.Y);
        TileCoord[] line = RoadsHarness.Straight((from.X, from.Y), (to.X, to.Y));
        var tiles = new (int X, int Y)[line.Length];
        for (int i = 0; i < line.Length; i++)
        {
            tiles[i] = (line[i].X, line[i].Y);
        }

        h.Place(dirt, tiles);
        bool[] afterRoads = CompareAll(h, buildings);

        EntityId late = Place(content, h, rng, island, land, buildable, occupied, storehouse);
        buildings.Add(late);
        CompareAll(h, buildings);

        return afterRoads;
    }

    // Every building's label answer must equal the field-scan answer.
    private static bool[] CompareAll(RoadsHarness h, List<EntityId> buildings)
    {
        bool[] answers = new bool[buildings.Count];
        for (int i = 0; i < buildings.Count; i++)
        {
            bool labels = h.Network.IsInStorehouseReach(buildings[i]);
            bool scan = h.Network.TryNearestStorehouse(buildings[i], out _, out _);
            labels.ShouldBe(scan, $"building {buildings[i]} disagrees between labels and the field scan");
            answers[i] = labels;
        }

        return answers;
    }

    private static TerrainId FirstBuildableTerrain(ContentDb content)
    {
        for (int i = 0; i < content.Terrains.Count; i++)
        {
            var terrain = TerrainId.From(checked((ushort)i));
            if (content.Get(terrain).Buildable)
            {
                return terrain;
            }
        }

        throw new InvalidOperationException("The content has no buildable terrain.");
    }

    // Places a building on a random buildable, unoccupied tile that fits its footprint, sampling land so large
    // footprints find room without thousands of tries.
    private static EntityId Place(
        ContentDb content,
        RoadsHarness h,
        Pcg32 rng,
        MapDef island,
        List<int> land,
        bool[] buildable,
        bool[] occupied,
        BuildingTypeId type)
    {
        Footprint footprint = content.Get(type).Footprint;
        for (int attempt = 0; attempt < 2000; attempt++)
        {
            int anchor = land[rng.NextInt(land.Count)];
            int x = (anchor % island.Width) + rng.NextInt(-footprint.Width + 1, 1);
            int y = (anchor / island.Width) + rng.NextInt(-footprint.Height + 1, 1);
            if (x < 0 || y < 0 || x + footprint.Width > island.Width || y + footprint.Height > island.Height)
            {
                continue;
            }

            bool fits = true;
            for (int dy = 0; dy < footprint.Height && fits; dy++)
            {
                for (int dx = 0; dx < footprint.Width && fits; dx++)
                {
                    int tile = ((y + dy) * island.Width) + x + dx;
                    fits = buildable[tile] && !occupied[tile];
                }
            }

            if (!fits)
            {
                continue;
            }

            for (int dy = 0; dy < footprint.Height; dy++)
            {
                for (int dx = 0; dx < footprint.Width; dx++)
                {
                    occupied[((y + dy) * island.Width) + x + dx] = true;
                }
            }

            return h.Sim.Services.BuildingRegistry.Add(type, x, y, Rotation.Deg0, BuildingState.Active);
        }

        throw new InvalidOperationException($"No room for a {type} after 200 tries.");
    }

    // Writes a random road layer straight into the state: long global walks and scatter, with heterogeneous road
    // types so step costs vary. This must run before any building exists: the first field build then sees the
    // finished layer, exactly as a loaded game does. Later mutations go through the command and lifecycle paths.
    private static void PaintRoads(
        RoadsHarness h,
        Pcg32 rng,
        MapDef island,
        BuildingTypeId[] roads)
    {
        ushort[] layer = new ushort[island.TileCount];
        void Paint(int x, int y)
        {
            BuildingTypeId road = roads[rng.NextInt(roads.Length)];
            layer[(y * island.Width) + x] = (ushort)(road.Value + 1);
        }

        for (int i = 0; i < 30; i++)
        {
            Walk(rng, island, rng.NextInt(island.Width), rng.NextInt(island.Height), 70, Paint);
        }

        for (int i = 0; i < 500; i++)
        {
            Paint(rng.NextInt(island.Width), rng.NextInt(island.Height));
        }

        h.Sim.World.Roads.Width = island.Width;
        h.Sim.World.Roads.Height = island.Height;
        h.Sim.World.Roads.Layer = layer;
        h.Sim.World.Roads.TilesByRoadType = [];
    }

    private static void Walk(Pcg32 rng, MapDef island, int x, int y, int length, Action<int, int> paint)
    {
        for (int i = 0; i < length; i++)
        {
            x = Math.Clamp(x + rng.NextInt(-1, 2), 0, island.Width - 1);
            y = Math.Clamp(y + rng.NextInt(-1, 2), 0, island.Height - 1);
            paint(x, y);
        }
    }
}
