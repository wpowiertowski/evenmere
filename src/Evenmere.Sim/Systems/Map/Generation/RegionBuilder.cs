using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Rng;

namespace Evenmere.Sim.Systems.Map.Generation;

/// <summary>
/// Partitions a generated island into regions for fog and exploration (all integers, per ADR-0003). Region seeds are
/// spread by farthest-point sampling over the land, then every tile is claimed by a multi-source Dijkstra run where
/// stepping onto a river tile costs extra, so region borders follow rivers where the geometry allows. The same seed
/// always paints the same regions: seed order breaks every tie, and the heap key is a total order over
/// (distance, seed order, tile).
/// </summary>
internal static class RegionBuilder
{
    /// <summary>The fewest regions a generated island holds.</summary>
    public const int MinRegions = 40;

    /// <summary>The most regions a generated island holds.</summary>
    public const int MaxRegions = 80;

    /// <summary>The extra Dijkstra cost of stepping onto a river tile, which pulls borders onto rivers.</summary>
    public const int RiverCrossPenalty = 6;

    /// <summary>Stream separation for the region count and first-seed dice: it must differ from every other generator stream.</summary>
    public const int RegionStream = unchecked((int)0x3D5A9B77);

    // Neighbour offsets in a fixed order (N, NE, E, SE, S, SW, W, NW), so relaxations are identical everywhere.
    private static readonly int[] _dx = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] _dy = [-1, -1, 0, 1, 1, 1, 0, -1];

    /// <summary>
    /// Builds the region layer of a heightmap in place: every tile, sea included, belongs to exactly one of 40–80 regions.
    /// Rivers and deposits are already placed; fertility is painted after this and never reads regions.
    /// </summary>
    /// <param name="map">The heightmap to partition; terrain and river widths are read, regions are written.</param>
    /// <param name="seed">The generator seed.</param>
    /// <param name="content">The loaded content, which must define the sea and reef terrains.</param>
    /// <returns>The number of regions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">The content does not define a terrain this builder needs.</exception>
    internal static int Build(GeneratedHeightmap map, int seed, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(content);

        TerrainId sea = content.IdOf<TerrainId>("sea");
        TerrainId reef = content.IdOf<TerrainId>("reef");

        int size = map.Size;
        int tiles = map.TileCount;

        int[] land = CollectLand(map, sea, reef);
        var rng = new Pcg32(unchecked((ulong)(uint)seed), unchecked((ulong)(uint)RegionStream));
        int count = Math.Min(MinRegions + rng.NextInt((MaxRegions - MinRegions) + 1), land.Length);

        int[] seeds = PickSeeds(land, count, size, rng);
        ClaimTiles(map, seeds, count, size, tiles);
        return count;
    }

    // Every tile that may hold a region seed: anything that is not open water and not a river, in ascending order.
    // Open water and rivers are claimed from these seeds but never seed a region of their own.
    private static int[] CollectLand(GeneratedHeightmap map, TerrainId sea, TerrainId reef)
    {
        int[] land = new int[map.TileCount];
        int count = 0;
        for (int i = 0; i < map.TileCount; i++)
        {
            if (!map.Terrain[i].Equals(sea) && !map.Terrain[i].Equals(reef) && map.RiverWidth[i] == 0)
            {
                land[count++] = i;
            }
        }

        Array.Resize(ref land, count);
        return land;
    }

    // Spreads seeds by farthest-point sampling: the first is a Pcg32 pick, and each next one is the land tile farthest
    // from the seeds so far (squared Euclidean distance, ties to the lowest tile). The best-distance array is updated
    // incrementally, so the whole pass costs O(count × land).
    private static int[] PickSeeds(int[] land, int count, int size, Pcg32 rng)
    {
        int[] seeds = new int[count];
        int[] best = new int[land.Length];
        seeds[0] = land[rng.NextInt(land.Length)];
        for (int l = 0; l < land.Length; l++)
        {
            best[l] = DistanceSquared(land[l], seeds[0], size);
        }

        for (int s = 1; s < count; s++)
        {
            int pick = 0;
            for (int l = 1; l < land.Length; l++)
            {
                if (best[l] > best[pick])
                {
                    pick = l;
                }
            }

            seeds[s] = land[pick];
            for (int l = 0; l < land.Length; l++)
            {
                int d = DistanceSquared(land[l], seeds[s], size);
                if (d < best[l])
                {
                    best[l] = d;
                }
            }
        }

        return seeds;
    }

    // The squared Euclidean distance between two tiles on a size-wide map, in square tiles.
    private static int DistanceSquared(int a, int b, int size)
    {
        int dx = (a % size) - (b % size);
        int dy = (a / size) - (b / size);
        return (dx * dx) + (dy * dy);
    }

    // Claims every tile for its nearest seed: a multi-source Dijkstra where stepping onto a river tile costs extra,
    // so borders prefer rivers. The heap key is a total order over (distance, seed order, tile), and a strictly lower
    // distance always wins, so the outcome is identical on every CPU.
    private static void ClaimTiles(GeneratedHeightmap map, int[] seeds, int count, int size, int tiles)
    {
        int[] dist = new int[tiles];
        Array.Fill(dist, int.MaxValue);
        long[] heap = new long[tiles];
        int heapCount = 0;
        for (int s = 0; s < count; s++)
        {
            dist[seeds[s]] = 0;
            map.Region[seeds[s]] = (ushort)s;
            heap[heapCount++] = Pack(0, s, seeds[s]);
            SiftUp(heap, heapCount - 1);
        }

        while (heapCount > 0)
        {
            long top = heap[0];
            heap[0] = heap[--heapCount];
            SiftDown(heap, heapCount, 0);
            int tile = (int)(top & 0xFFFFFFL);
            int distance = (int)(top >> 32);
            if (distance != dist[tile])
            {
                continue;
            }

            int region = map.Region[tile];
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

                int n = (ny * size) + nx;
                int step = 1 + (map.RiverWidth[n] != 0 ? RiverCrossPenalty : 0);
                int nd = distance + step;
                if (nd < dist[n])
                {
                    dist[n] = nd;
                    map.Region[n] = (ushort)region;
                    if (heapCount == heap.Length)
                    {
                        Array.Resize(ref heap, heap.Length * 2);
                    }

                    heap[heapCount++] = Pack(nd, region, n);
                    SiftUp(heap, heapCount - 1);
                }
            }
        }
    }

    private static long Pack(int distance, int order, int tile) =>
        (((long)distance) << 32) | (((long)order) << 24) | (uint)tile;

    private static void SiftUp(long[] heap, int i)
    {
        long item = heap[i];
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (heap[parent] <= item)
            {
                break;
            }

            heap[i] = heap[parent];
            i = parent;
        }

        heap[i] = item;
    }

    private static void SiftDown(long[] heap, int count, int i)
    {
        long item = heap[i];
        while (true)
        {
            int left = (i * 2) + 1;
            int right = left + 1;
            int smallest = i;
            if (left < count && heap[left] < heap[smallest])
            {
                smallest = left;
            }

            if (right < count && heap[right] < heap[smallest])
            {
                smallest = right;
            }

            if (smallest == i)
            {
                break;
            }

            heap[i] = heap[smallest];
            i = smallest;
        }

        heap[i] = item;
    }
}
