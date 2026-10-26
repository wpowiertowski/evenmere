using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Map.Generation;

/// <summary>
/// Places mineral deposits and offshore reefs (all integers, per ADR-0003): stone and iron on hills, coal
/// and copper in the highlands, clay on riverbanks, and reefs in edge-connected sea next to land. One tile
/// holds at most one deposit. The same seed always places the same deposits.
/// </summary>
internal static class DepositPlacer
{
    /// <summary>The share of hills tiles that hold iron, in per-mille of a tile hash.</summary>
    public const int IronPerMille = 40;

    /// <summary>The share of hills tiles that hold stone, in per-mille of a tile hash, after iron.</summary>
    public const int StonePerMille = 100;

    /// <summary>The share of highlands tiles that hold coal, in per-mille of a tile hash.</summary>
    public const int CoalPerMille = 50;

    /// <summary>The share of highlands tiles that hold copper, in per-mille of a tile hash, after coal.</summary>
    public const int CopperPerMille = 50;

    /// <summary>The share of eligible riverbank tiles that hold clay, in per-mille of a tile hash.</summary>
    public const int ClayPerMille = 650;

    /// <summary>A reef patch sample at or above this turns an offshore tile into reef.</summary>
    public const int ReefThreshold = 38500;

    /// <summary>The share of strong patch tiles that become reef, in per-mille of a tile hash.</summary>
    public const int ReefPerMille = 200;

    /// <summary>The number of noise octaves in the reef patch field.</summary>
    public const int ReefOctaves = 3;

    /// <summary>Stream separation for the deposit dice.</summary>
    public const int DepositStreamXor = unchecked((int)0x1C3A5B7D);

    /// <summary>Stream separation for the reef patch field.</summary>
    public const int ReefStreamXor = unchecked((int)0x6D9E4A2F);

    // Neighbour offsets in a fixed order (N, NE, E, SE, S, SW, W, NW), so bank walks are identical everywhere.
    private static readonly int[] _dx = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] _dy = [-1, -1, 0, 1, 1, 1, 0, -1];

    /// <summary>
    /// Places deposits and reefs onto a heightmap in place: the deposit byte becomes the
    /// <see cref="DepositKind"/> value plus one, and offshore patch tiles become reef terrain.
    /// </summary>
    /// <param name="map">The heightmap to paint; rivers must already be traced. Tiny pockets are filled after this.</param>
    /// <param name="seed">The generator seed.</param>
    /// <param name="content">The loaded content, which must define the hill, highland, sea and reef terrains.</param>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">The content does not define a terrain this placer needs.</exception>
    internal static void Place(GeneratedHeightmap map, int seed, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(content);

        TerrainId hills = content.IdOf<TerrainId>("hills");
        TerrainId highlands = content.IdOf<TerrainId>("highlands");
        TerrainId sea = content.IdOf<TerrainId>("sea");
        TerrainId reef = content.IdOf<TerrainId>("reef");
        TerrainId river = content.IdOf<TerrainId>("river");
        TerrainId plains = content.IdOf<TerrainId>("plains");
        TerrainId forest = content.IdOf<TerrainId>("forest");
        TerrainId beach = content.IdOf<TerrainId>("beach");

        PaintReefs(map, seed, sea, reef);
        PlaceHillDeposits(map, seed, hills, highlands);
        PlaceClay(map, seed, river, plains, forest, beach, hills);
    }

    // Reefs grow on edge-connected sea tiles next to land where a noise patch is strong: patchy offshore
    // bars on the real coast, never in enclosed pockets (which are still sea at this stage but unconnected).
    private static void PaintReefs(GeneratedHeightmap map, int seed, TerrainId sea, TerrainId reef)
    {
        int size = map.Size;
        bool[] edgeSea = EdgeSea(map, sea);
        int stream = seed ^ ReefStreamXor;
        for (int y = 0; y < size; y++)
        {
            int nyQ16 = (int)(((long)y * IntegerNoise.Unit) / size);
            for (int x = 0; x < size; x++)
            {
                int i = (y * size) + x;
                if (!map.Terrain[i].Equals(sea) || !edgeSea[i] || !NextToLand(map, sea, x, y))
                {
                    continue;
                }

                int nxQ16 = (int)(((long)x * IntegerNoise.Unit) / size);
                if (IntegerNoise.Fbm(nxQ16, nyQ16, stream, ReefOctaves) >= ReefThreshold
                    && (IntegerNoise.Hash(x, y, stream) % 1000) < ReefPerMille)
                {
                    map.Terrain[i] = reef;
                }
            }
        }
    }

    private static bool NextToLand(GeneratedHeightmap map, TerrainId sea, int x, int y)
    {
        int size = map.Size;
        for (int d = 0; d < 8; d++)
        {
            int nx = x + _dx[d];
            int ny = y + _dy[d];
            if ((uint)nx < (uint)size && (uint)ny < (uint)size && !map.Terrain[(ny * size) + nx].Equals(sea))
            {
                return true;
            }
        }

        return false;
    }

    // Stone and iron share the hills, coal and copper the highlands: one hash roll per tile partitioned by
    // the per-mille shares, so a tile never holds two deposits.
    private static void PlaceHillDeposits(GeneratedHeightmap map, int seed, TerrainId hills, TerrainId highlands)
    {
        int tiles = map.TileCount;
        for (int i = 0; i < tiles; i++)
        {
            int x = i % map.Size;
            int y = i / map.Size;
            int roll = (int)(IntegerNoise.Hash(x, y, seed ^ DepositStreamXor) % 1000);
            if (map.Terrain[i].Equals(hills))
            {
                if (roll < IronPerMille)
                {
                    map.Deposit[i] = (byte)((int)DepositKind.Iron + 1);
                }
                else if (roll < IronPerMille + StonePerMille)
                {
                    map.Deposit[i] = (byte)((int)DepositKind.Stone + 1);
                }
            }
            else if (map.Terrain[i].Equals(highlands))
            {
                if (roll < CoalPerMille)
                {
                    map.Deposit[i] = (byte)((int)DepositKind.Coal + 1);
                }
                else if (roll < CoalPerMille + CopperPerMille)
                {
                    map.Deposit[i] = (byte)((int)DepositKind.Copper + 1);
                }
            }
        }
    }

    // Clay sits on riverbanks: buildable land tiles next to a river tile. The walk runs over river tiles in
    // ascending order with a fixed neighbour order, so the same seed paints the same banks.
    private static void PlaceClay(
        GeneratedHeightmap map,
        int seed,
        TerrainId river,
        TerrainId plains,
        TerrainId forest,
        TerrainId beach,
        TerrainId hills)
    {
        int size = map.Size;
        int tiles = map.TileCount;
        for (int i = 0; i < tiles; i++)
        {
            if (!map.Terrain[i].Equals(river))
            {
                continue;
            }

            int x = i % size;
            int y = i / size;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + _dx[d];
                int ny = y + _dy[d];
                if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                {
                    continue;
                }

                int n = (ny * size) + nx;
                if (map.Deposit[n] != 0 || !IsBank(map.Terrain[n], plains, forest, beach, hills))
                {
                    continue;
                }

                if ((IntegerNoise.Hash(nx, ny, seed ^ DepositStreamXor) % 1000) < ClayPerMille)
                {
                    map.Deposit[n] = (byte)((int)DepositKind.Clay + 1);
                }
            }
        }
    }

    private static bool IsBank(TerrainId terrain, TerrainId plains, TerrainId forest, TerrainId beach, TerrainId hills)
    {
        return terrain.Equals(plains) || terrain.Equals(forest) || terrain.Equals(beach) || terrain.Equals(hills);
    }

    // Edge-connected sea in 8-connectivity, the same rule the river tracer uses for shores: the sea margin
    // guarantees border water to start the fill from.
    private static bool[] EdgeSea(GeneratedHeightmap map, TerrainId sea)
    {
        int size = map.Size;
        int tiles = map.TileCount;
        bool[] connected = new bool[tiles];
        int[] stack = new int[tiles];
        int top = 0;
        for (int x = 0; x < size; x++)
        {
            top = PushSea(map, sea, connected, stack, top, x, 0);
            top = PushSea(map, sea, connected, stack, top, x, size - 1);
        }

        for (int y = 1; y < size - 1; y++)
        {
            top = PushSea(map, sea, connected, stack, top, 0, y);
            top = PushSea(map, sea, connected, stack, top, size - 1, y);
        }

        while (top > 0)
        {
            int at = stack[--top];
            int x = at % size;
            int y = at / size;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + _dx[d];
                int ny = y + _dy[d];
                if ((uint)nx < (uint)size && (uint)ny < (uint)size)
                {
                    top = PushSea(map, sea, connected, stack, top, nx, ny);
                }
            }
        }

        return connected;
    }

    private static int PushSea(GeneratedHeightmap map, TerrainId sea, bool[] connected, int[] stack, int top, int x, int y)
    {
        int i = (y * map.Size) + x;
        if (!connected[i] && map.Terrain[i].Equals(sea))
        {
            connected[i] = true;
            stack[top++] = i;
        }

        return top;
    }
}
