using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Map.Generation;

namespace Evenmere.Sim.Tests.MapGeneration.Rivers;

/// <summary>The M2-02a deposit contract: every deposit sits on its required terrain, at most one deposit
/// per tile, and every deposit kind (plus offshore reefs) occurs on every tested island.</summary>
public sealed class DepositTerrainTests
{
    private static readonly int[] _dx = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] _dy = [-1, -1, 0, 1, 1, 1, 0, -1];

    /// <summary>Gets every (seed, size) pair: seeds 1–50 at 320, plus seeds 1–5 at 256 and 384.</summary>
    /// <returns>The pairs.</returns>
    public static TheoryData<int, int> SeedSizePairs()
    {
        var data = new TheoryData<int, int>();
        for (int seed = 1; seed <= 50; seed++)
        {
            data.Add(seed, 320);
        }

        for (int seed = 1; seed <= 5; seed++)
        {
            data.Add(seed, 256);
            data.Add(seed, 384);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateTerrain_Deposits_SitOnRequiredTerrain(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateTerrain(seed, size, TerrainFixture.Content);
        TerrainId hills = TerrainFixture.Content.IdOf<TerrainId>("hills");
        TerrainId highlands = TerrainFixture.Content.IdOf<TerrainId>("highlands");
        TerrainId river = TerrainFixture.Content.IdOf<TerrainId>("river");
        TerrainId sea = TerrainFixture.Content.IdOf<TerrainId>("sea");
        TerrainId reef = TerrainFixture.Content.IdOf<TerrainId>("reef");
        TerrainId plains = TerrainFixture.Content.IdOf<TerrainId>("plains");
        TerrainId forest = TerrainFixture.Content.IdOf<TerrainId>("forest");
        TerrainId beach = TerrainFixture.Content.IdOf<TerrainId>("beach");

        for (int i = 0; i < map.TileCount; i++)
        {
            if (map.Deposit[i] == 0)
            {
                continue;
            }

            map.Deposit[i].ShouldBeLessThanOrEqualTo((byte)5, $"tile {i} (seed {seed}) holds no known deposit");
            var kind = (DepositKind)(map.Deposit[i] - 1);
            switch (kind)
            {
                case DepositKind.Stone:
                case DepositKind.Iron:
                    map.Terrain[i].Equals(hills).ShouldBeTrue($"tile {i} (seed {seed}) holds {kind} off the hills");
                    break;
                case DepositKind.Clay:
                    IsBank(map, river, plains, forest, beach, hills, size, i).ShouldBeTrue($"tile {i} (seed {seed}) holds clay away from a riverbank");
                    break;
                case DepositKind.Coal:
                case DepositKind.Copper:
                    map.Terrain[i].Equals(highlands).ShouldBeTrue($"tile {i} (seed {seed}) holds {kind} outside the highlands");
                    break;
                default:
                    Assert.Fail($"tile {i} (seed {seed}) holds an unexpected deposit {(int)kind}");
                    break;
            }

            map.Terrain[i].Equals(river).ShouldBeFalse($"tile {i} (seed {seed}) holds {kind} on a river tile");
            map.Terrain[i].Equals(sea).ShouldBeFalse($"tile {i} (seed {seed}) holds {kind} at sea");
            map.Terrain[i].Equals(reef).ShouldBeFalse($"tile {i} (seed {seed}) holds {kind} on a reef");
        }
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateTerrain_EveryDepositKind_Occurs(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateTerrain(seed, size, TerrainFixture.Content);

        int[] counts = new int[5];
        foreach (byte deposit in map.Deposit)
        {
            if (deposit != 0)
            {
                counts[deposit - 1]++;
            }
        }

        foreach (DepositKind kind in Enum.GetValues<DepositKind>())
        {
            counts[(int)kind].ShouldBeGreaterThan(0, $"seed {seed} at size {size} places no {kind}");
        }
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateTerrain_Reefs_SitOffshore(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateTerrain(seed, size, TerrainFixture.Content);
        TerrainId sea = TerrainFixture.Content.IdOf<TerrainId>("sea");
        TerrainId reef = TerrainFixture.Content.IdOf<TerrainId>("reef");

        bool[] connected = ConnectedWater(map, sea, reef, size);
        int reefs = 0;
        for (int i = 0; i < map.TileCount; i++)
        {
            if (!map.Terrain[i].Equals(reef))
            {
                continue;
            }

            reefs++;
            connected[i].ShouldBeTrue($"reef tile {i} (seed {seed}) is cut off from the open sea");
            TouchesLand(map, sea, reef, size, i).ShouldBeTrue($"reef tile {i} (seed {seed}) sits in the open sea, away from land");
        }

        reefs.ShouldBeGreaterThan(0, $"seed {seed} at size {size} grows no reefs");
    }

    // A reef bar grows from land-adjacent sea, so every reef tile reaches land through reef tiles:
    // the flood fill walks the reef patch the tile belongs to and looks for a land neighbour.
    private static bool TouchesLand(GeneratedHeightmap map, TerrainId sea, TerrainId reef, int size, int tile)
    {
        bool[] seen = new bool[map.TileCount];
        var stack = new Stack<int>();
        seen[tile] = true;
        stack.Push(tile);
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

                TerrainId neighbour = map.Terrain[(ny * size) + nx];
                if (!neighbour.Equals(sea) && !neighbour.Equals(reef))
                {
                    return true;
                }

                int n = (ny * size) + nx;
                if (neighbour.Equals(reef) && !seen[n])
                {
                    seen[n] = true;
                    stack.Push(n);
                }
            }
        }

        return false;
    }

    // A clay tile is a riverbank tile: buildable land with a river tile among its eight neighbours.
    private static bool IsBank(
        GeneratedHeightmap map,
        TerrainId river,
        TerrainId plains,
        TerrainId forest,
        TerrainId beach,
        TerrainId hills,
        int size,
        int tile)
    {
        TerrainId terrain = map.Terrain[tile];
        if (!terrain.Equals(plains) && !terrain.Equals(forest) && !terrain.Equals(beach) && !terrain.Equals(hills))
        {
            return false;
        }

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

            if (map.Terrain[(ny * size) + nx].Equals(river))
            {
                return true;
            }
        }

        return false;
    }

    private static bool[] ConnectedWater(GeneratedHeightmap map, TerrainId sea, TerrainId reef, int size)
    {
        bool[] connected = new bool[map.TileCount];
        var stack = new Stack<int>();
        for (int x = 0; x < size; x++)
        {
            foreach (int y in new[] { 0, size - 1 })
            {
                PushWater(map, sea, reef, connected, stack, x, y);
            }
        }

        for (int y = 1; y < size - 1; y++)
        {
            foreach (int x in new[] { 0, size - 1 })
            {
                PushWater(map, sea, reef, connected, stack, x, y);
            }
        }

        while (stack.Count > 0)
        {
            int at = stack.Pop();
            int x = at % size;
            int y = at / size;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + _dx[d];
                int ny = y + _dy[d];
                if ((uint)nx < (uint)size && (uint)ny < (uint)size)
                {
                    PushWater(map, sea, reef, connected, stack, nx, ny);
                }
            }
        }

        return connected;
    }

    private static void PushWater(GeneratedHeightmap map, TerrainId sea, TerrainId reef, bool[] connected, Stack<int> stack, int x, int y)
    {
        int i = (y * map.Size) + x;
        if (!connected[i] && (map.Terrain[i].Equals(sea) || map.Terrain[i].Equals(reef)))
        {
            connected[i] = true;
            stack.Push(i);
        }
    }
}
