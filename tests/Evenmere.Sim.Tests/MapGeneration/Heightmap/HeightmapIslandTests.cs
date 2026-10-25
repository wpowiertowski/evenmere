using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Map.Generation;

namespace Evenmere.Sim.Tests.MapGeneration.Heightmap;

/// <summary>The M2-01 island contract, over seeds 1–50 at every size: a guaranteed sea margin, one main
/// island holding at least 85% of the land, varied land shares, ragged coasts and usually off-centre
/// summits (rounds 2 and 3).</summary>
public sealed class HeightmapIslandTests
{
    private static readonly int[] _sizes = [256, 320, 384];

    private static readonly int[] _seeds = [.. Enumerable.Range(1, 50)];

    /// <summary>Gets every (seed, size) pair: seeds 1–50 at each supported size.</summary>
    /// <returns>The pairs.</returns>
    public static TheoryData<int, int> SeedSizePairs()
    {
        var data = new TheoryData<int, int>();
        for (int seed = 1; seed <= 50; seed++)
        {
            foreach (int size in _sizes)
            {
                data.Add(seed, size);
            }
        }

        return data;
    }

    /// <summary>Gets every supported size.</summary>
    /// <returns>The sizes.</returns>
    public static TheoryData<int> Sizes()
    {
        var data = new TheoryData<int>();
        foreach (int size in _sizes)
        {
            data.Add(size);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateHeightmap_Margin_IsAlwaysSea(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateHeightmap(seed, size, HeightmapFixture.Content);
        TerrainId sea = HeightmapFixture.Content.IdOf<TerrainId>("sea");
        int margin = Math.Max(12, (size * 6) / 100);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int edge = Math.Min(Math.Min(x, y), Math.Min(size - 1 - x, size - 1 - y));
                if (edge < margin)
                {
                    int i = (y * size) + x;
                    map.Terrain[i].ShouldBe(sea, $"tile ({x}, {y}) of seed {seed}");
                    map.Elevation[i].ShouldBe((byte)0, $"tile ({x}, {y}) of seed {seed}");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateHeightmap_MainIsland_HoldsMostLand(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateHeightmap(seed, size, HeightmapFixture.Content);
        TerrainId sea = HeightmapFixture.Content.IdOf<TerrainId>("sea");

        bool[] land = new bool[map.TileCount];
        int landCount = 0;
        for (int i = 0; i < map.TileCount; i++)
        {
            if (map.Terrain[i] != sea)
            {
                land[i] = true;
                landCount++;
            }
        }

        landCount.ShouldBeGreaterThan(0, $"seed {seed} has no land");

        bool[] seen = new bool[map.TileCount];
        var frontier = new Stack<int>();
        int largest = 0;
        for (int i = 0; i < map.TileCount; i++)
        {
            if (!land[i] || seen[i])
            {
                continue;
            }

            int count = 0;
            frontier.Push(i);
            seen[i] = true;
            while (frontier.Count > 0)
            {
                int tile = frontier.Pop();
                count++;
                int x = tile % size;
                int y = tile / size;
                PushIfLand(tile - 1, x > 0);
                PushIfLand(tile + 1, x < size - 1);
                PushIfLand(tile - size, y > 0);
                PushIfLand(tile + size, y < size - 1);
            }

            if (count > largest)
            {
                largest = count;
            }

            void PushIfLand(int tile, bool inside)
            {
                if (inside && land[tile] && !seen[tile])
                {
                    seen[tile] = true;
                    frontier.Push(tile);
                }
            }
        }

        ((double)largest / landCount).ShouldBeGreaterThanOrEqualTo(0.85, $"seed {seed} is fragmented");
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void GenerateHeightmap_LandShare_VariesWithinBand(int size)
    {
        double minShare = 100.0;
        double maxShare = 0.0;
        foreach (int seed in _seeds)
        {
            GeneratedHeightmap map = IslandGenerator.GenerateHeightmap(seed, size, HeightmapFixture.Content);
            double share = 100.0 * LandTiles(map) / map.TileCount;
            minShare = Math.Min(minShare, share);
            maxShare = Math.Max(maxShare, share);
            share.ShouldBeInRange(30.0, 60.0, $"seed {seed} at size {size} leaves the 30–60% band");
        }

        (maxShare - minShare).ShouldBeGreaterThanOrEqualTo(10.0, $"size {size} islands vary too little");
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateHeightmap_Coastline_IsIrregular(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateHeightmap(seed, size, HeightmapFixture.Content);
        TerrainId sea = HeightmapFixture.Content.IdOf<TerrainId>("sea");

        int landCount = 0;
        int coastCount = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = (y * size) + x;
                if (map.Terrain[i] == sea)
                {
                    continue;
                }

                landCount++;
                if ((x > 0 && map.Terrain[i - 1] == sea) || (x < size - 1 && map.Terrain[i + 1] == sea)
                    || (y > 0 && map.Terrain[i - size] == sea) || (y < size - 1 && map.Terrain[i + size] == sea))
                {
                    coastCount++;
                }
            }
        }

        double circlePerimeter = 2.0 * Math.Sqrt(Math.PI * landCount);
        ((double)coastCount / circlePerimeter).ShouldBeGreaterThanOrEqualTo(1.35, $"seed {seed} is too round");
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void GenerateHeightmap_Summit_IsUsuallyOffCentre(int size)
    {
        int offCentre = 0;
        foreach (int seed in _seeds)
        {
            GeneratedHeightmap map = IslandGenerator.GenerateHeightmap(seed, size, HeightmapFixture.Content);

            int[] histogram = new int[16];
            foreach (byte elevation in map.Elevation)
            {
                histogram[elevation]++;
            }

            int need = (int)(map.TileCount * 0.05);
            int count = 0;
            int level = MaxElevationTop;
            for (int elevation = MaxElevationTop; elevation >= 0; elevation--)
            {
                count += histogram[elevation];
                if (count >= need)
                {
                    level = elevation;
                    break;
                }
            }

            long sumX = 0;
            long sumY = 0;
            int summitTiles = 0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (map.Elevation[(y * size) + x] >= level)
                    {
                        sumX += x;
                        sumY += y;
                        summitTiles++;
                    }
                }
            }

            double centre = (size - 1) / 2.0;
            double distance = Math.Sqrt(
                ((sumX / (double)summitTiles) - centre) * ((sumX / (double)summitTiles) - centre)
                + ((sumY / (double)summitTiles) - centre) * ((sumY / (double)summitTiles) - centre));
            if (distance >= 0.12 * size)
            {
                offCentre++;
            }
        }

        offCentre.ShouldBeGreaterThanOrEqualTo(20, $"size {size} keeps its summits central");
    }

    private const int MaxElevationTop = 15;

    private static int LandTiles(GeneratedHeightmap map)
    {
        TerrainId sea = HeightmapFixture.Content.IdOf<TerrainId>("sea");
        int landCount = 0;
        foreach (TerrainId terrain in map.Terrain)
        {
            if (terrain != sea)
            {
                landCount++;
            }
        }

        return landCount;
    }
}
