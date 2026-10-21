using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Roads;

// View paths: A* over road tiles. Presentation only; no system calls these and nothing in the simulation depends on the result.
internal sealed partial class RoadNetwork
{
    // For each tile the search reached, the tile it was reached from. Only tiles in _touched are meaningful.
    private int[] _parent = [];

    /// <summary>Finds a route over road tiles between two buildings, from an access tile of one to an access tile of the other.</summary>
    /// <param name="source">The building the route starts at.</param>
    /// <param name="destination">The building the route ends at.</param>
    /// <param name="path">Cleared, then filled with the road tiles of the cheapest route, inclusive.</param>
    /// <returns><see langword="true"/> if a route exists.</returns>
    public bool TryFindViewPath(EntityId source, EntityId destination, List<TileCoord> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        path.Clear();
        if (_state.Layer.Length == 0
            || !TryGetBuilding(source, out BuildingInfo from)
            || !TryGetBuilding(destination, out BuildingInfo to))
        {
            return false;
        }

        Span<int> starts = RingBuffer(from, stackalloc int[StackRing]);
        Span<int> goals = RingBuffer(to, stackalloc int[StackRing]);
        int startCount = CollectAccess(from, starts);
        int goalCount = CollectAccess(to, goals);
        return startCount > 0 && goalCount > 0 && FindPath(starts[..startCount], goals[..goalCount], path);
    }

    /// <summary>Finds a route over road tiles between two road tiles.</summary>
    /// <param name="source">The road tile the route starts at.</param>
    /// <param name="destination">The road tile the route ends at.</param>
    /// <param name="path">Cleared, then filled with the road tiles of the cheapest route, inclusive.</param>
    /// <returns><see langword="true"/> if both tiles are roads and a route exists.</returns>
    public bool TryFindViewPath(TileCoord source, TileCoord destination, List<TileCoord> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        path.Clear();
        if (!source.IsInside(_state.Width, _state.Height) || !destination.IsInside(_state.Width, _state.Height))
        {
            return false;
        }

        int start = source.ToIndex(_state.Width);
        int goal = destination.ToIndex(_state.Width);
        if (CostAt(start) == 0 || CostAt(goal) == 0)
        {
            return false;
        }

        return FindPath([start], [goal], path);
    }

    // A* from any of the starts to any of the goals, entering a tile at the cost of its road. The heuristic is the Manhattan
    // distance to the nearest goal times the cheapest road cost in the content, which never overestimates, so the first goal
    // popped is on a cheapest route. Ties in the queue break by tile index and neighbours are tried in a fixed order, so the
    // same layout always gives the same route.
    private bool FindPath(ReadOnlySpan<int> starts, ReadOnlySpan<int> goals, List<TileCoord> path)
    {
        int width = _state.Width;
        int tiles = _state.Layer.Length;
        EnsureScratch();
        if (_parent.Length != tiles)
        {
            _parent = new int[tiles];
        }

        _touchedCount = 0;
        _heapCount = 0;
        foreach (int start in starts)
        {
            _dist[start] = 0;
            _parent[start] = -1;
            _touched[_touchedCount++] = start;
            HeapPush(((long)Estimate(start, goals) << 32) | (uint)start);
        }

        int found = -1;
        while (_heapCount > 0)
        {
            long key = HeapPop();
            int index = (int)(key & uint.MaxValue);
            int spent = _dist[index];
            if ((int)(key >> 32) != spent + Estimate(index, goals))
            {
                // A cheaper route to this tile was found after this entry was queued.
                continue;
            }

            if (goals.Contains(index))
            {
                found = index;
                break;
            }

            int x = index % width;
            if (index >= width)
            {
                Step(index, index - width, spent, goals);
            }

            if (x > 0)
            {
                Step(index, index - 1, spent, goals);
            }

            if (x < width - 1)
            {
                Step(index, index + 1, spent, goals);
            }

            if (index + width < tiles)
            {
                Step(index, index + width, spent, goals);
            }
        }

        if (found >= 0)
        {
            for (int tile = found; tile >= 0; tile = _parent[tile])
            {
                path.Add(TileCoord.FromIndex(tile, width));
            }

            path.Reverse();
        }

        ResetScratch();
        return found >= 0;
    }

    private void Step(int from, int tile, int spent, ReadOnlySpan<int> goals)
    {
        int step = CostAt(tile);
        if (step == 0)
        {
            return;
        }

        int next = spent + step;
        int known = _dist[tile];
        if (next >= known)
        {
            return;
        }

        if (known == Unreached)
        {
            _touched[_touchedCount++] = tile;
        }

        _dist[tile] = next;
        _parent[tile] = from;
        HeapPush(((long)(next + Estimate(tile, goals)) << 32) | (uint)tile);
    }

    private int Estimate(int tile, ReadOnlySpan<int> goals)
    {
        int width = _state.Width;
        int x = tile % width;
        int y = tile / width;
        int nearest = int.MaxValue;
        foreach (int goal in goals)
        {
            nearest = Math.Min(nearest, Math.Abs((goal % width) - x) + Math.Abs((goal / width) - y));
        }

        return nearest * _minRoadCost;
    }
}
