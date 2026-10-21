using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Tools.Balance;

/// <summary>
/// The bot's road planner: a breadth-first search from the tiles beside a new building's footprint to the
/// existing road network, over free, buildable tiles. The winning path is validated through
/// <c>IPlacementQueries.ValidateRoad</c>. Everything runs in ascending, deterministic order with no randomness:
/// start tiles in (Y, X) order, neighbours north, west, east, south.
/// </summary>
public static class RoadPlanner
{
    private const int MaxAttempts = 4;

    /// <summary>
    /// Measures the shortest road-tile distance between two sets of tiles, for service coverage estimates.
    /// Only road tiles are entered; the count is the tiles stepped on after the starts.
    /// </summary>
    /// <param name="simulation">The simulation, read between ticks.</param>
    /// <param name="from">The tiles to start from; only those on roads are entered.</param>
    /// <param name="to">The tiles to reach.</param>
    /// <returns>The distance, or <see langword="null"/> when no route over roads exists.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static int? RoadDistance(ISimulation simulation, IEnumerable<TileCoord> from, IEnumerable<TileCoord> to)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        int mapWidth = simulation.Query.Map.Width;
        int mapHeight = simulation.Query.Map.Height;
        HashSet<int> targets = [];
        foreach (TileCoord tile in to)
        {
            if (tile.IsInside(mapWidth, mapHeight))
            {
                targets.Add(tile.ToIndex(mapWidth));
            }
        }

        HashSet<int> visited = [];
        Queue<(int Index, int Distance)> queue = new();
        foreach (TileCoord tile in from)
        {
            if (!tile.IsInside(mapWidth, mapHeight))
            {
                continue;
            }

            int index = tile.ToIndex(mapWidth);
            if (!visited.Add(index))
            {
                continue;
            }

            if (simulation.Query.Roads.RoadAt(tile.X, tile.Y) is null)
            {
                continue;
            }

            if (targets.Contains(index))
            {
                return 0;
            }

            queue.Enqueue((index, 0));
        }

        ReadOnlySpan<int> dx = [0, -1, 1, 0];
        ReadOnlySpan<int> dy = [-1, 0, 0, 1];
        while (queue.Count > 0)
        {
            (int index, int distance) = queue.Dequeue();
            int cx = index % mapWidth;
            int cy = index / mapWidth;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + dx[d];
                int ny = cy + dy[d];
                if (nx < 0 || ny < 0 || nx >= mapWidth || ny >= mapHeight)
                {
                    continue;
                }

                int next = ny * mapWidth + nx;
                if (!visited.Add(next) || simulation.Query.Roads.RoadAt(nx, ny) is null)
                {
                    continue;
                }

                if (targets.Contains(next))
                {
                    return distance + 1;
                }

                queue.Enqueue((next, distance + 1));
            }
        }

        return null;
    }

    /// <summary>
    /// Finds road tiles connecting a footprint to the road network.
    /// </summary>
    /// <param name="simulation">The simulation, read between ticks.</param>
    /// <param name="content">The loaded content, for terrain buildability.</param>
    /// <param name="roadType">The road type to lay.</param>
    /// <param name="x">The column of the footprint's top-left tile.</param>
    /// <param name="y">The row of the footprint's top-left tile.</param>
    /// <param name="width">The footprint width in tiles after rotation.</param>
    /// <param name="height">The footprint height in tiles after rotation.</param>
    /// <param name="anchorX">The column of the anchor rectangle's top-left tile, used only when no road exists yet.</param>
    /// <param name="anchorY">The row of the anchor rectangle's top-left tile.</param>
    /// <param name="anchorWidth">The anchor width.</param>
    /// <param name="anchorHeight">The anchor height.</param>
    /// <returns>The path from beside the footprint to the network (the final tile is already a road and is left out), or <see langword="null"/> when no valid path exists.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static TileCoord[]? FindConnector(
        ISimulation simulation,
        ContentDb content,
        BuildingTypeId roadType,
        int x,
        int y,
        int width,
        int height,
        int anchorX,
        int anchorY,
        int anchorWidth,
        int anchorHeight)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(content);

        int mapWidth = simulation.Query.Map.Width;
        int mapHeight = simulation.Query.Map.Height;
        var blocked = new HashSet<int>();
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            TileCoord[]? path = Search(simulation, content, x, y, width, height, anchorX, anchorY, anchorWidth, anchorHeight, mapWidth, mapHeight, blocked);
            if (path is null)
            {
                return null;
            }

            PlacementResult validation = simulation.Query.Placement.ValidateRoad(path, roadType);
            if (validation.CanPlace)
            {
                return path;
            }

            bool added = false;
            foreach (PlacementFinding finding in validation.Errors)
            {
                foreach (TileCoord tile in finding.Tiles)
                {
                    if (tile.IsInside(mapWidth, mapHeight))
                    {
                        added |= blocked.Add(tile.ToIndex(mapWidth));
                    }
                }
            }

            if (!added)
            {
                return null;
            }
        }

        return null;
    }

    private static TileCoord[]? Search(
        ISimulation simulation,
        ContentDb content,
        int x,
        int y,
        int width,
        int height,
        int anchorX,
        int anchorY,
        int anchorWidth,
        int anchorHeight,
        int mapWidth,
        int mapHeight,
        HashSet<int> blocked)
    {
        bool hasRoads = HasAnyRoad(simulation, mapWidth, mapHeight);
        var starts = new List<int>();
        // Orthogonally beside the footprint, in ascending (Y, X) order. The four corner tiles touch the footprint
        // only diagonally, so a road there would give no access: they are left out.
        for (int ty = y - 1; ty <= y + height; ty++)
        {
            for (int tx = x - 1; tx <= x + width; tx++)
            {
                bool inX = tx >= x && tx < x + width;
                bool inY = ty >= y && ty < y + height;
                bool beside = (tx == x - 1 || tx == x + width) ? inY : (ty == y - 1 || ty == y + height) && inX;
                if (!beside)
                {
                    continue;
                }

                var tile = new TileCoord(tx, ty);
                if (tile.IsInside(mapWidth, mapHeight) && IsWalkable(simulation, content, tile, x, y, width, height, blocked, mapWidth))
                {
                    starts.Add(tile.ToIndex(mapWidth));
                }
            }
        }

        if (starts.Count == 0)
        {
            return null;
        }

        // A road already beside the footprint gives access; the bot's roads always join the network, so reach follows.
        if (hasRoads)
        {
            foreach (int start in starts)
            {
                if (simulation.Query.Roads.RoadAt(start % mapWidth, start / mapWidth) is not null)
                {
                    return [];
                }
            }
        }

        int[] previous = new int[mapWidth * mapHeight];
        Array.Fill(previous, -2);
        var queue = new Queue<int>();
        foreach (int start in starts)
        {
            previous[start] = -1;
            queue.Enqueue(start);
        }

        // North, west, east, south: fixed order, so the search is deterministic.
        ReadOnlySpan<int> dx = [0, -1, 1, 0];
        ReadOnlySpan<int> dy = [-1, 0, 0, 1];
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int cx = current % mapWidth;
            int cy = current / mapWidth;
            var tile = new TileCoord(cx, cy);
            if (IsTarget(simulation, tile, hasRoads, anchorX, anchorY, anchorWidth, anchorHeight))
            {
                return BuildPath(previous, current, starts, mapWidth);
            }

            for (int d = 0; d < 4; d++)
            {
                var next = new TileCoord(cx + dx[d], cy + dy[d]);
                if (!next.IsInside(mapWidth, mapHeight))
                {
                    continue;
                }

                int index = next.ToIndex(mapWidth);
                if (previous[index] != -2)
                {
                    continue;
                }

                // Walking through existing roads is free; only new tiles must be walkable.
                if (!IsTarget(simulation, next, hasRoads, anchorX, anchorY, anchorWidth, anchorHeight)
                    && !IsWalkable(simulation, content, next, x, y, width, height, blocked, mapWidth))
                {
                    continue;
                }

                previous[index] = current;
                queue.Enqueue(index);
            }
        }

        return null;
    }

    private static bool IsTarget(ISimulation simulation, TileCoord tile, bool hasRoads, int anchorX, int anchorY, int anchorWidth, int anchorHeight)
    {
        if (hasRoads)
        {
            return simulation.Query.Roads.RoadAt(tile.X, tile.Y) is not null;
        }

        // No network yet: walk to the anchor's doorstep, so the first building grows roads from the hall.
        return tile.X >= anchorX - 1 && tile.X <= anchorX + anchorWidth && tile.Y >= anchorY - 1 && tile.Y <= anchorY + anchorHeight
            && !(tile.X >= anchorX && tile.X < anchorX + anchorWidth && tile.Y >= anchorY && tile.Y < anchorY + anchorHeight);
    }

    private static bool IsWalkable(
        ISimulation simulation,
        ContentDb content,
        TileCoord tile,
        int footprintX,
        int footprintY,
        int footprintWidth,
        int footprintHeight,
        HashSet<int> blocked,
        int mapWidth)
    {
        if (blocked.Contains(tile.ToIndex(mapWidth)))
        {
            return false;
        }

        // Never pave over the footprint the caller is about to build on.
        if (tile.X >= footprintX && tile.X < footprintX + footprintWidth && tile.Y >= footprintY && tile.Y < footprintY + footprintHeight)
        {
            return false;
        }

        if (simulation.Query.Map.BuildingAt(tile.X, tile.Y) != EntityId.None)
        {
            return false;
        }

        if (simulation.Query.Roads.RoadAt(tile.X, tile.Y) is not null)
        {
            return true;
        }

        TileInfo info = simulation.Query.Map.GetTile(tile.X, tile.Y);
        if (info.Has(TileFeatures.River))
        {
            return false;
        }

        TerrainDef terrain = content.Terrains[info.Terrain];
        return terrain.Buildable && !terrain.BlocksRoads;
    }

    private static bool HasAnyRoad(ISimulation simulation, int mapWidth, int mapHeight)
    {
        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                if (simulation.Query.Roads.RoadAt(x, y) is not null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static TileCoord[]? BuildPath(int[] previous, int target, List<int> starts, int mapWidth)
    {
        // The target is already a road (or the anchor's doorstep), so it is left out: the path is the new road tiles.
        // When the target is itself a start tile, the path is that one tile beside the footprint.
        if (previous[target] == -1)
        {
            return [new TileCoord(target % mapWidth, target / mapWidth)];
        }

        var reversed = new List<int>();
        int current = previous[target];
        while (current >= 0)
        {
            reversed.Add(current);
            current = previous[current];
        }

        if (reversed.Count == 0)
        {
            return null;
        }

        reversed.Reverse();
        var path = new TileCoord[reversed.Count];
        for (int i = 0; i < reversed.Count; i++)
        {
            path[i] = new TileCoord(reversed[i] % mapWidth, reversed[i] / mapWidth);
        }

        return path;
    }
}
