using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Construction;

/// <summary>
/// Answers "would a storehouse reach a building placed here?" for the placement warning. <see cref="IRoadsQueries"/> can
/// answer that only for a building that exists (it keys on an entity ID), and a ghost has none, so this walks the road
/// layer once with the rules of ADR-0005 and the M1-05 card: a storehouse reaches the road tiles whose cost from one of its
/// access tiles is at most <c>reach × 1000</c>, where a tile costs its road type's <c>roadCost</c>. The walk starts from the
/// new building's access tiles and runs outward, which is the same total for roads of one type and differs at the edge
/// only where road types are mixed.
/// </summary>
/// <remarks>
/// This duplicates, in a bounded and throwaway form, what the roads system's distance fields hold. When Roads exposes a
/// footprint-based reach query, replace <see cref="Covers"/> with it (proposal in the M1-06 report). The result feeds a
/// warning only; the real reach check that gates a site each tick is <see cref="IRoadsQueries.IsInStorehouseReach"/>.
/// </remarks>
internal static class StorehouseReach
{
    private const int DefaultRoadCost = 1000;
    private const int CostPerReachTile = 1000;

    /// <summary>Tests whether some finished storehouse reaches any of the given road tiles.</summary>
    /// <param name="content">The content, for storehouse specs and road costs.</param>
    /// <param name="map">The map queries.</param>
    /// <param name="roads">The roads queries.</param>
    /// <param name="startTiles">The road tiles that touch the new building's footprint (its access tiles).</param>
    /// <returns><see langword="true"/> if one of the tiles is within the reach of a storehouse.</returns>
    public static bool Covers(ContentDb content, IMapQueries map, IRoadsQueries roads, IReadOnlyList<TileCoord> startTiles)
    {
        // The budget each tile has as a storehouse access tile; the largest wins where storehouses share a tile.
        var budgets = new SortedDictionary<int, int>();
        int largest = 0;
        foreach (BuildingInfo building in map.ActiveBuildings)
        {
            StorageSpec? storage = content.Get(building.Type).Storage;
            if (storage is null)
            {
                continue;
            }

            int budget = storage.Reach * CostPerReachTile;
            largest = Math.Max(largest, budget);
            foreach (TileCoord access in AccessTiles(roads, building))
            {
                int index = access.ToIndex(map.Width);
                budgets[index] = budgets.TryGetValue(index, out int known) ? Math.Max(known, budget) : budget;
            }
        }

        if (budgets.Count == 0)
        {
            return false;
        }

        // Dijkstra over road tiles. The answer is a yes or no, so the order of equal-cost pops cannot change it.
        var best = new SortedDictionary<int, int>();
        var queue = new PriorityQueue<int, int>();
        foreach (TileCoord start in startTiles)
        {
            int index = start.ToIndex(map.Width);
            best[index] = 0;
            queue.Enqueue(index, 0);
        }

        while (queue.TryDequeue(out int tile, out int cost))
        {
            if (cost > best[tile])
            {
                continue;
            }

            if (budgets.TryGetValue(tile, out int allowed) && cost <= allowed)
            {
                return true;
            }

            var at = TileCoord.FromIndex(tile, map.Width);
            Visit(at.Offset(1, 0));
            Visit(at.Offset(-1, 0));
            Visit(at.Offset(0, 1));
            Visit(at.Offset(0, -1));

            void Visit(TileCoord next)
            {
                if (!next.IsInside(map.Width, map.Height) || roads.RoadAt(next.X, next.Y) is not { } roadType)
                {
                    return;
                }

                int nextCost = cost + (content.Get(roadType).RoadCost ?? DefaultRoadCost);
                int nextIndex = next.ToIndex(map.Width);
                if (nextCost <= largest && (!best.TryGetValue(nextIndex, out int old) || nextCost < old))
                {
                    best[nextIndex] = nextCost;
                    queue.Enqueue(nextIndex, nextCost);
                }
            }
        }

        return false;
    }

    // A building's access tiles: road tiles along the edges of its footprint, corners excluded.
    private static List<TileCoord> AccessTiles(IRoadsQueries roads, BuildingInfo building)
    {
        var found = new List<TileCoord>();
        int x = building.Origin.X;
        int y = building.Origin.Y;
        for (int ty = y - 1; ty <= y + building.Height; ty++)
        {
            for (int tx = x - 1; tx <= x + building.Width; tx++)
            {
                bool outsideColumns = tx < x || tx >= x + building.Width;
                bool outsideRows = ty < y || ty >= y + building.Height;
                if (outsideColumns != outsideRows && roads.RoadAt(tx, ty) is not null)
                {
                    found.Add(new TileCoord(tx, ty));
                }
            }
        }

        return found;
    }
}
