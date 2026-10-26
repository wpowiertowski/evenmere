using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Map.Generation;

namespace Evenmere.Sim.Tests.MapGeneration.Rivers;

/// <summary>The lake rule: enclosed seawater pockets stay water, except pockets of four tiles or fewer,
/// which are filled with neighbouring land. Reefs stay on edge-connected sea only.</summary>
public sealed class LakeTests
{
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
    public void GenerateTerrain_HasNoSmallEnclosedPockets(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateTerrain(seed, size, TerrainFixture.Content);
        TerrainId sea = TerrainFixture.Content.IdOf<TerrainId>("sea");
        TerrainId reef = TerrainFixture.Content.IdOf<TerrainId>("reef");

        // Every sea component either reaches the open sea through water (sea and reef alike) or is bigger
        // than a filled pocket. Reef bars can pinch a pocket off the border without disconnecting it.
        bool[] connected = ConnectedWater(map, sea, reef, size);
        int[] component = new int[map.TileCount];
        int[] members = new int[map.TileCount];
        int[] stack = new int[map.TileCount];
        int components = 0;
        for (int i = 0; i < map.TileCount; i++)
        {
            if (!map.Terrain[i].Equals(sea) || component[i] != 0)
            {
                continue;
            }

            components++;
            int count = 0;
            bool enclosed = false;
            stack[0] = i;
            int top = 1;
            component[i] = components;
            while (top > 0)
            {
                int at = stack[--top];
                members[count++] = at;
                if (!connected[at])
                {
                    enclosed = true;
                }

                int x = at % size;
                int y = at / size;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + _riverDx[d];
                    int ny = y + _riverDy[d];
                    if ((uint)nx < (uint)size && (uint)ny < (uint)size)
                    {
                        int n = (ny * size) + nx;
                        if (map.Terrain[n].Equals(sea) && component[n] == 0)
                        {
                            component[n] = components;
                            stack[top++] = n;
                        }
                    }
                }
            }

            if (enclosed)
            {
                count.ShouldBeGreaterThan(RiverTracer.MaxFillPocketTiles, $"sea pocket of {count} tiles (seed {seed}) survives inland");
            }
        }
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateTerrain_ZeroElevation_StaysWater(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateTerrain(seed, size, TerrainFixture.Content);
        TerrainId sea = TerrainFixture.Content.IdOf<TerrainId>("sea");
        TerrainId reef = TerrainFixture.Content.IdOf<TerrainId>("reef");

        // The heightmap stage never paints elevation 0 off the sea, and filled pockets take a
        // neighbouring land elevation, so elevation 0 is always water.
        for (int i = 0; i < map.TileCount; i++)
        {
            if (map.Elevation[i] == 0)
            {
                (map.Terrain[i].Equals(sea) || map.Terrain[i].Equals(reef)).ShouldBeTrue($"tile {i} (seed {seed}) sits at elevation 0 without being water");
            }
        }
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateTerrain_Reefs_StayOnOpenSea(int seed, int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateTerrain(seed, size, TerrainFixture.Content);
        TerrainId sea = TerrainFixture.Content.IdOf<TerrainId>("sea");
        TerrainId reef = TerrainFixture.Content.IdOf<TerrainId>("reef");

        bool[] connected = ConnectedWater(map, sea, reef, size);
        for (int i = 0; i < map.TileCount; i++)
        {
            if (map.Terrain[i].Equals(reef))
            {
                connected[i].ShouldBeTrue($"reef tile {i} (seed {seed}) lies in an enclosed pocket");
            }
        }
    }

    [Fact]
    public void GenerateTerrain_LargePockets_StaySea()
    {
        // Seed 49 at 320 holds a large inland sea: it must survive as water, not land.
        GeneratedHeightmap map = IslandGenerator.GenerateTerrain(49, 320, TerrainFixture.Content);
        TerrainId sea = TerrainFixture.Content.IdOf<TerrainId>("sea");
        TerrainId reef = TerrainFixture.Content.IdOf<TerrainId>("reef");

        bool[] connected = ConnectedWater(map, sea, reef, size: 320);
        int enclosed = 0;
        for (int i = 0; i < map.TileCount; i++)
        {
            if (map.Terrain[i].Equals(sea) && !connected[i])
            {
                enclosed++;
            }
        }

        enclosed.ShouldBeGreaterThan(0, "seed 49 at 320 holds no inland sea");
    }

    private static readonly int[] _riverDx = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] _riverDy = [-1, -1, 0, 1, 1, 1, 0, -1];

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
                int nx = x + _riverDx[d];
                int ny = y + _riverDy[d];
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
