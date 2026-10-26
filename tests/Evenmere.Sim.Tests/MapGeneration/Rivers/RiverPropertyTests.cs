using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Map.Generation;

namespace Evenmere.Sim.Tests.MapGeneration.Rivers;

/// <summary>The M2-02a round-4 river contract, over seeds 1–50 at all three island sizes: 2–5 main rivers
/// of the size-scaled length, each ending at the sea and starting in the hills or higher; river tiles
/// making up 0.4–4% of the land; elevation never rising along a downstream path; no straight run longer
/// than 12 steps; no parallel channels within 3 tiles on different systems; both width classes present.</summary>
public sealed class RiverPropertyTests
{
    private static readonly int[] _dx = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] _dy = [-1, -1, 0, 1, 1, 1, 0, -1];

    /// <summary>Gets every (seed, size) pair: seeds 1–50 at 256, 320 and 384.</summary>
    /// <returns>The pairs.</returns>
    public static TheoryData<int, int> SeedSizePairs()
    {
        var data = new TheoryData<int, int>();
        for (int seed = 1; seed <= 50; seed++)
        {
            data.Add(seed, 256);
            data.Add(seed, 320);
            data.Add(seed, 384);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateTerrain_HasTwoToFiveMainRiversEndingAtWater(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateTerrain(seed, size, TerrainFixture.Content);
        TerrainId river = TerrainFixture.Content.IdOf<TerrainId>("river");
        TerrainId sea = TerrainFixture.Content.IdOf<TerrainId>("sea");
        TerrainId reef = TerrainFixture.Content.IdOf<TerrainId>("reef");
        int mainLength = (size * 8) / 100;

        int[] component = LabelComponents(map, river, size);
        int components = 0;
        foreach (int c in component)
        {
            if (c > components)
            {
                components = c;
            }
        }

        int[] sizes = new int[components + 1];
        bool[] mouth = new bool[components + 1];
        int[] highest = new int[components + 1];
        for (int i = 0; i < map.TileCount; i++)
        {
            if (component[i] == 0)
            {
                continue;
            }

            sizes[component[i]]++;
            if (map.Elevation[i] > highest[component[i]])
            {
                highest[component[i]] = map.Elevation[i];
            }

            if (!mouth[component[i]] && NextToWater(map, sea, reef, size, i))
            {
                mouth[component[i]] = true;
            }
        }

        int big = 0;
        for (int c = 1; c <= components; c++)
        {
            if (sizes[c] >= mainLength)
            {
                big++;
                mouth[c].ShouldBeTrue($"river {c} (seed {seed}) of {sizes[c]} tiles ends inland");
                highest[c].ShouldBeGreaterThanOrEqualTo(BiomePainter.HillsElevation, $"river {c} (seed {seed}) starts below the hills");
            }
        }

        big.ShouldBeGreaterThanOrEqualTo(2, $"seed {seed} at size {size} grows fewer than two main rivers");
        big.ShouldBeLessThanOrEqualTo(5, $"seed {seed} at size {size} grows more than five main rivers");
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateTerrain_RiverShare_StaysInRange(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateTerrain(seed, size, TerrainFixture.Content);
        TerrainId river = TerrainFixture.Content.IdOf<TerrainId>("river");
        TerrainId sea = TerrainFixture.Content.IdOf<TerrainId>("sea");
        TerrainId reef = TerrainFixture.Content.IdOf<TerrainId>("reef");

        int rivers = 0;
        int land = 0;
        for (int i = 0; i < map.TileCount; i++)
        {
            if (map.Terrain[i].Equals(sea) || map.Terrain[i].Equals(reef))
            {
                continue;
            }

            land++;
            if (map.Terrain[i].Equals(river))
            {
                rivers++;
            }
        }

        int share = (1000 * rivers) / land;
        share.ShouldBeGreaterThanOrEqualTo(4, $"seed {seed} at size {size} has only {share}‰ rivers");
        share.ShouldBeLessThanOrEqualTo(40, $"seed {seed} at size {size} has {share}‰ rivers");
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateTerrain_RiverPath_NeverRises(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateTerrain(seed, size, TerrainFixture.Content);
        TerrainId river = TerrainFixture.Content.IdOf<TerrainId>("river");
        TerrainId sea = TerrainFixture.Content.IdOf<TerrainId>("sea");
        TerrainId reef = TerrainFixture.Content.IdOf<TerrainId>("reef");

        // Every river tile must reach a shore mouth through river tiles that never rise: one reverse
        // breadth-first search from all mouths visits exactly the tiles with such a path.
        bool[] reached = new bool[map.TileCount];
        var queue = new Queue<int>();
        for (int i = 0; i < map.TileCount; i++)
        {
            if (map.Terrain[i].Equals(river) && NextToWater(map, sea, reef, size, i))
            {
                reached[i] = true;
                queue.Enqueue(i);
            }
        }

        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            int x = at % size;
            int y = at / size;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + _dx[d];
                int ny = y + _dy[d];
                if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                {
                    continue;
                }

                int n = (ny * size) + nx;
                if (map.Terrain[n].Equals(river) && !reached[n] && map.Elevation[n] >= map.Elevation[at])
                {
                    reached[n] = true;
                    queue.Enqueue(n);
                }
            }
        }

        for (int i = 0; i < map.TileCount; i++)
        {
            if (map.Terrain[i].Equals(river))
            {
                reached[i].ShouldBeTrue($"river tile {i} (seed {seed}) cannot reach the sea without rising");
            }
        }
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateTerrain_Rivers_HaveNoLongStraightRun(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateTerrain(seed, size, TerrainFixture.Content);
        TerrainId river = TerrainFixture.Content.IdOf<TerrainId>("river");

        // A straight run of 13 steps is 14 collinear river tiles. Rays of that length must always
        // leave the river, whatever direction they take.
        for (int i = 0; i < map.TileCount; i++)
        {
            if (!map.Terrain[i].Equals(river))
            {
                continue;
            }

            int x = i % size;
            int y = i / size;
            for (int d = 0; d < 8; d++)
            {
                int run = 0;
                int nx = x;
                int ny = y;
                for (int s = 0; s < 13; s++)
                {
                    nx += _dx[d];
                    ny += _dy[d];
                    if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                    {
                        break;
                    }

                    if (!map.Terrain[(ny * size) + nx].Equals(river))
                    {
                        break;
                    }

                    run++;
                }

                run.ShouldBeLessThan(13, $"river tile {i} (seed {seed}) starts a straight run of {run} steps");
            }
        }
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateTerrain_Rivers_HaveNoParallelChannels(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateTerrain(seed, size, TerrainFixture.Content);
        TerrainId river = TerrainFixture.Content.IdOf<TerrainId>("river");

        int[] component = LabelComponents(map, river, size);
        for (int i = 0; i < map.TileCount; i++)
        {
            if (!map.Terrain[i].Equals(river))
            {
                continue;
            }

            int x = i % size;
            int y = i / size;
            for (int dy = -3; dy <= 3; dy++)
            {
                for (int dx = -3; dx <= 3; dx++)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                    {
                        continue;
                    }

                    int n = (ny * size) + nx;
                    if (n != i && map.Terrain[n].Equals(river) && component[n] != component[i])
                    {
                        Assert.Fail($"river tiles {i} and {n} (seed {seed}) run parallel on different systems");
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateTerrain_RiverWidth_MatchesRiverTerrain(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateTerrain(seed, size, TerrainFixture.Content);
        TerrainId river = TerrainFixture.Content.IdOf<TerrainId>("river");

        int narrow = 0;
        int wide = 0;
        for (int i = 0; i < map.TileCount; i++)
        {
            if (map.Terrain[i].Equals(river))
            {
                map.RiverWidth[i].ShouldBeOneOf((byte)1, (byte)2);
                if (map.RiverWidth[i] == 1)
                {
                    narrow++;
                }
                else
                {
                    wide++;
                }
            }
            else
            {
                map.RiverWidth[i].ShouldBe((byte)0, $"tile {i} (seed {seed}) carries a width class without river terrain");
            }
        }

        narrow.ShouldBeGreaterThan(0, $"seed {seed} at size {size} has no narrow river");
        wide.ShouldBeGreaterThan(0, $"seed {seed} at size {size} has no wide river");
    }

    // Connected river networks in 8-connectivity, labelled in ascending tile order: 0 means not river.
    // Joined tributaries share a system by construction.
    private static int[] LabelComponents(GeneratedHeightmap map, TerrainId river, int size)
    {
        int[] component = new int[map.TileCount];
        int components = 0;
        var stack = new Stack<int>();
        for (int i = 0; i < map.TileCount; i++)
        {
            if (!map.Terrain[i].Equals(river) || component[i] != 0)
            {
                continue;
            }

            components++;
            component[i] = components;
            stack.Push(i);
            while (stack.Count > 0)
            {
                int at = stack.Pop();
                int x = at % size;
                int y = at / size;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + _dx[d];
                    int ny = y + _dy[d];
                    if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                    {
                        continue;
                    }

                    int n = (ny * size) + nx;
                    if (map.Terrain[n].Equals(river) && component[n] == 0)
                    {
                        component[n] = components;
                        stack.Push(n);
                    }
                }
            }
        }

        return component;
    }

    private static bool NextToWater(GeneratedHeightmap map, TerrainId sea, TerrainId reef, int size, int tile)
    {
        int x = tile % size;
        int y = tile / size;
        for (int d = 0; d < 8; d++)
        {
            int nx = x + _dx[d];
            int ny = y + _dy[d];
            if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
            {
                continue;
            }

            TerrainId neighbour = map.Terrain[(ny * size) + nx];
            if (neighbour.Equals(sea) || neighbour.Equals(reef))
            {
                return true;
            }
        }

        return false;
    }
}
