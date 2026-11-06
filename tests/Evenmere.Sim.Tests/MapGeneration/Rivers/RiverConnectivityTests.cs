using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Map.Generation;

namespace Evenmere.Sim.Tests.MapGeneration.Rivers;

/// <summary>The M2-39 river connectivity contract, over seeds 1–50 at all three island sizes: every river
/// network is 4-connected, so a dirt road can never slip between two corner-touching river tiles without a
/// bridge. Every non-mouth river tile steps down or level onto an orthogonal river neighbour (toward its
/// mouth), and no two river tiles touch diagonally without a shared orthogonal river neighbour.</summary>
public sealed class RiverConnectivityTests
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
    public void GenerateTerrain_RiverTile_StepsOntoOrthogonalRiverDownstream(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateTerrain(seed, size, TerrainFixture.Content);
        TerrainId river = TerrainFixture.Content.IdOf<TerrainId>("river");
        TerrainId sea = TerrainFixture.Content.IdOf<TerrainId>("sea");
        TerrainId reef = TerrainFixture.Content.IdOf<TerrainId>("reef");

        for (int i = 0; i < map.TileCount; i++)
        {
            if (!map.Terrain[i].Equals(river) || NextToWater(map, sea, reef, size, i))
            {
                continue;
            }

            int x = i % size;
            int y = i / size;
            bool stepsDown = false;
            for (int d = 0; d < 8; d += 2)
            {
                int nx = x + _dx[d];
                int ny = y + _dy[d];
                if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                {
                    continue;
                }

                int n = (ny * size) + nx;
                if (map.Terrain[n].Equals(river) && map.Elevation[n] <= map.Elevation[i])
                {
                    stepsDown = true;
                    break;
                }
            }

            stepsDown.ShouldBeTrue($"river tile {i} (seed {seed}) has no orthogonal river neighbour toward its mouth");
        }
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateTerrain_Rivers_HaveNoUnbridgedDiagonalTouch(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateTerrain(seed, size, TerrainFixture.Content);
        TerrainId river = TerrainFixture.Content.IdOf<TerrainId>("river");

        for (int i = 0; i < map.TileCount; i++)
        {
            if (!map.Terrain[i].Equals(river))
            {
                continue;
            }

            int x = i % size;
            int y = i / size;
            for (int d = 1; d < 8; d += 2)
            {
                int nx = x + _dx[d];
                int ny = y + _dy[d];
                if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                {
                    continue;
                }

                int n = (ny * size) + nx;
                if (!map.Terrain[n].Equals(river))
                {
                    continue;
                }

                bool shared = map.Terrain[(y * size) + nx].Equals(river)
                    || map.Terrain[(ny * size) + x].Equals(river);
                shared.ShouldBeTrue($"river tiles {i} and {n} (seed {seed}) touch diagonally with no shared orthogonal river neighbour");
            }
        }
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
