using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Roads;

/// <summary>
/// An independent reference for the distance fields, written to be obviously correct rather than fast: no priority queue and
/// no early exit. Costs are relaxed over the whole grid until nothing improves (Bellman-Ford style), and for layouts made of
/// one road type there is a plain breadth-first search. The tests compare the real fields and lookups against these.
/// </summary>
internal sealed class BruteForce(int width, int height, Func<int, int, int> costAt)
{
    public const int Far = int.MaxValue;

    /// <summary>Gets the cost of entering a tile; 0 when it has no road.</summary>
    public int CostAt(int x, int y) => costAt(x, y);

    /// <summary>
    /// The least cost from the seeds to every tile, entering a tile costing that tile's road cost and the seeds costing 0,
    /// by repeated relaxation. Tiles that cannot be reached, and tiles without a road, hold <see cref="Far"/>.
    /// </summary>
    public int[] Relaxed(IEnumerable<int> seeds)
    {
        int[] dist = new int[width * height];
        Array.Fill(dist, Far);
        foreach (int seed in seeds)
        {
            dist[seed] = 0;
        }

        bool improved = true;
        while (improved)
        {
            improved = false;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int step = costAt(x, y);
                    if (step == 0)
                    {
                        continue;
                    }

                    foreach ((int nx, int ny) in Neighbours(x, y))
                    {
                        int from = dist[(ny * width) + nx];
                        if (from != Far && from + step < dist[(y * width) + x])
                        {
                            dist[(y * width) + x] = from + step;
                            improved = true;
                        }
                    }
                }
            }
        }

        return dist;
    }

    /// <summary>Breadth-first search from the seeds over road tiles, for layouts of one road type with the given cost per tile.</summary>
    public int[] BreadthFirst(IEnumerable<int> seeds, int costPerTile)
    {
        int[] dist = new int[width * height];
        Array.Fill(dist, Far);
        var queue = new Queue<int>();
        foreach (int seed in seeds)
        {
            dist[seed] = 0;
            queue.Enqueue(seed);
        }

        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            foreach ((int nx, int ny) in Neighbours(index % width, index / width))
            {
                int next = (ny * width) + nx;
                if (costAt(nx, ny) > 0 && dist[next] == Far)
                {
                    dist[next] = dist[index] + costPerTile;
                    queue.Enqueue(next);
                }
            }
        }

        return dist;
    }

    /// <summary>The road tiles orthogonally adjacent to a footprint.</summary>
    public List<int> Access(BuildingInfo b)
    {
        var tiles = new List<int>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool inside = b.Covers(x, y);
                bool beside = !inside && (b.Covers(x - 1, y) || b.Covers(x + 1, y) || b.Covers(x, y - 1) || b.Covers(x, y + 1));
                if (beside && costAt(x, y) > 0)
                {
                    tiles.Add((y * width) + x);
                }
            }
        }

        return tiles;
    }

    /// <summary>The fields as the real system should hold them: every tile at or under the bound, with its cost.</summary>
    public static (int[] Tiles, int[] Costs) Within(int[] dist, int bound)
    {
        var tiles = new List<int>();
        var costs = new List<int>();
        for (int i = 0; i < dist.Length; i++)
        {
            if (dist[i] != Far && dist[i] <= bound)
            {
                tiles.Add(i);
                costs.Add(dist[i]);
            }
        }

        return ([.. tiles], [.. costs]);
    }

    private IEnumerable<(int X, int Y)> Neighbours(int x, int y)
    {
        if (x > 0)
        {
            yield return (x - 1, y);
        }

        if (x < width - 1)
        {
            yield return (x + 1, y);
        }

        if (y > 0)
        {
            yield return (x, y - 1);
        }

        if (y < height - 1)
        {
            yield return (x, y + 1);
        }
    }

    /// <summary>Gets the storehouse reach of a building type in cost units, or 0.</summary>
    public static int StorageBound(Evenmere.Content.ContentDb content, BuildingTypeId type) =>
        content.Get(type).Storage is { } storage ? storage.Reach * 1000 : 0;

    /// <summary>Gets the service coverage of a building type in cost units, or 0.</summary>
    public static int ServiceBound(Evenmere.Content.ContentDb content, BuildingTypeId type) =>
        content.Get(type).Service is { } service ? service.Coverage * 1000 : 0;

    /// <summary>Gets the service a building type provides, if any.</summary>
    public static ServiceKind? ServiceOf(Evenmere.Content.ContentDb content, BuildingTypeId type) => content.Get(type).Service?.Kind;
}
