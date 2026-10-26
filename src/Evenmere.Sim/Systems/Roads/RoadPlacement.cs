using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Map;

namespace Evenmere.Sim.Systems.Roads;

/// <summary>
/// The road half of <c>IPlacementQueries</c> (ADR-0011). The ghost preview calls <see cref="ValidateRoad"/> and the
/// <c>PlaceRoad</c> handler calls <see cref="Evaluate"/>, which are the same check, so the preview never promises what the
/// command then refuses. It reads the map and progression and never changes anything.
/// </summary>
/// <remarks>
/// Rules, checked per tile and reporting the first that fails, then per river run for bridges: the road type must be an
/// unlocked road type (for the whole path); then the tile must be on the map, on terrain whose access flag (if any)
/// research has opened, on terrain that is buildable and does not block roads, free of buildings, and, for a river tile,
/// crossed by a bridge type whose research is open. A bridge crossing must then be one straight run no longer than the
/// type's span. Ordinary roads can never be placed on a river tile, open or not.
/// </remarks>
internal sealed class RoadPlacement(RoadNetwork network, ContentDb content, Func<ISimQueries> queries) : IRoadPlacement
{
    /// <inheritdoc />
    public PlacementResult ValidateRoad(ReadOnlySpan<TileCoord> path, BuildingTypeId roadType)
    {
        if (path.IsEmpty)
        {
            return PlacementResult.Ok;
        }

        var issues = new PlacementIssue[path.Length];
        Evaluate(path, roadType, issues);
        return BuildResult(path, issues);
    }

    /// <summary>Checks every tile of a path and finds how much of it can be placed.</summary>
    /// <param name="path">The tiles in order.</param>
    /// <param name="roadType">The road type.</param>
    /// <param name="issues">Receives, for each tile, the first rule it breaks, or <see cref="PlacementIssue.None"/>; at least as long as the path.</param>
    /// <returns>The length of the valid prefix: the number of tiles before the first one with an issue.</returns>
    public int Evaluate(ReadOnlySpan<TileCoord> path, BuildingTypeId roadType, Span<PlacementIssue> issues)
    {
        if (path.IsEmpty)
        {
            return 0;
        }

        ISimQueries sim = queries();
        if (!network.IsRoadType(roadType) || !sim.Progression.IsUnlocked(roadType))
        {
            // A type that is not a road, or not yet unlocked, can pave nothing, so the whole path is refused.
            issues[..path.Length].Fill(PlacementIssue.NotUnlocked);
            return 0;
        }

        int? span = content.Get(roadType).BridgeSpan;
        for (int i = 0; i < path.Length; i++)
        {
            issues[i] = Check(sim, path[i], span is not null);
        }

        if (span is not null)
        {
            ApplyBridgeRuns(sim, path, issues, span.Value);
        }

        int prefix = -1;
        for (int i = 0; i < path.Length; i++)
        {
            if (prefix < 0 && issues[i] != PlacementIssue.None)
            {
                prefix = i;
            }
        }

        return prefix < 0 ? path.Length : prefix;
    }

    private PlacementIssue Check(ISimQueries sim, TileCoord tile, bool isBridge)
    {
        if (!tile.IsInside(sim.Map.Width, sim.Map.Height))
        {
            return PlacementIssue.OutOfBounds;
        }

        TileInfo info = sim.Map.GetTile(tile.X, tile.Y);
        if (info.Terrain.Value >= content.Terrains.Count)
        {
            return PlacementIssue.TerrainNotAllowed;
        }

        TerrainDef terrain = content.Get(info.Terrain);

        // Locked ground is reported as locked even when it would also be refused for other reasons, because research is
        // what changes the answer: deep water and marsh are locked first and only then found unfit for a road.
        if (terrain.Access is { } access && !sim.Progression.HasAccess(access))
        {
            return PlacementIssue.TerrainAccessLocked;
        }

        if (info.Has(TileFeatures.River))
        {
            // A river tile is crossed only by a bridge type whose width access is open. The span and the straightness of
            // the crossing are checked over the whole run afterwards, once every tile of the run has passed this check.
            if (TerrainAccessCheck.BridgeAccess(info.RiverWidth) is { } needed)
            {
                if (!isBridge)
                {
                    // Research never opens a river to a road without a span, so this is not a lock but a refusal.
                    return PlacementIssue.TerrainNotAllowed;
                }

                if (!sim.Progression.HasAccess(needed))
                {
                    return PlacementIssue.TerrainAccessLocked;
                }

                return sim.Map.BuildingAt(tile.X, tile.Y).IsNone ? PlacementIssue.None : PlacementIssue.Occupied;
            }

            // A river tile without a width (a map with no rivers layer) names no span to check against, so nothing crosses it.
            return PlacementIssue.TerrainNotAllowed;
        }

        if (!terrain.Buildable || terrain.BlocksRoads)
        {
            return PlacementIssue.TerrainNotAllowed;
        }

        return sim.Map.BuildingAt(tile.X, tile.Y).IsNone ? PlacementIssue.None : PlacementIssue.Occupied;
    }

    // Marks every river run a bridge crosses that is longer than its span, or that bends, with the run's issue. A run is a
    // maximal stretch of consecutive river tiles whose basic check passed, so a refused tile ends the run and is left alone.
    // A bridge is one structure: the whole run is refused, not just the tile where it overruns or turns.
    private static void ApplyBridgeRuns(ISimQueries sim, ReadOnlySpan<TileCoord> path, Span<PlacementIssue> issues, int span)
    {
        int runStart = -1;
        for (int i = 0; i <= path.Length; i++)
        {
            bool river = i < path.Length && issues[i] == PlacementIssue.None && IsSizedRiver(sim, path[i]);
            if (river && runStart < 0)
            {
                runStart = i;
            }

            if (!river && runStart >= 0)
            {
                if (i - runStart > span)
                {
                    issues[runStart..i].Fill(PlacementIssue.BridgeTooLong);
                }
                else if (!IsStraight(path[runStart..i]))
                {
                    issues[runStart..i].Fill(PlacementIssue.BridgeNotStraight);
                }

                runStart = -1;
            }
        }
    }

    // A river tile with a width class: the only kind a bridge span can be checked against.
    private static bool IsSizedRiver(ISimQueries sim, TileCoord tile)
    {
        if (!tile.IsInside(sim.Map.Width, sim.Map.Height))
        {
            return false;
        }

        TileInfo info = sim.Map.GetTile(tile.X, tile.Y);
        return info.Has(TileFeatures.River) && TerrainAccessCheck.BridgeAccess(info.RiverWidth) is not null;
    }

    // One tile cannot bend; a longer run holds its line when every step is the same unit move along one axis, which is how
    // a road is laid tile by tile. A diagonal step or a gap is not a straight run.
    private static bool IsStraight(ReadOnlySpan<TileCoord> run)
    {
        if (run.Length < 2)
        {
            return true;
        }

        int stepX = run[1].X - run[0].X;
        int stepY = run[1].Y - run[0].Y;
        if (Math.Abs(stepX) + Math.Abs(stepY) != 1)
        {
            return false;
        }

        for (int i = 2; i < run.Length; i++)
        {
            if (run[i].X - run[i - 1].X != stepX || run[i].Y - run[i - 1].Y != stepY)
            {
                return false;
            }
        }

        return true;
    }

    // One finding per issue, in the order each first occurs along the path, so the first error always holds the tile where the
    // valid prefix ends. Each finding's tiles are distinct and in ascending (Y, X) order.
    private static PlacementResult BuildResult(ReadOnlySpan<TileCoord> path, ReadOnlySpan<PlacementIssue> issues)
    {
        var order = new List<PlacementIssue>();
        for (int i = 0; i < path.Length; i++)
        {
            if (issues[i] != PlacementIssue.None && !order.Contains(issues[i]))
            {
                order.Add(issues[i]);
            }
        }

        if (order.Count == 0)
        {
            return PlacementResult.Ok;
        }

        ImmutableArray<PlacementFinding>.Builder errors = ImmutableArray.CreateBuilder<PlacementFinding>(order.Count);
        foreach (PlacementIssue issue in order)
        {
            var tiles = new List<TileCoord>();
            for (int i = 0; i < path.Length; i++)
            {
                if (issues[i] == issue)
                {
                    tiles.Add(path[i]);
                }
            }

            tiles.Sort(static (a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            for (int i = tiles.Count - 1; i > 0; i--)
            {
                if (tiles[i] == tiles[i - 1])
                {
                    tiles.RemoveAt(i);
                }
            }

            errors.Add(new PlacementFinding(issue, [.. tiles]));
        }

        return new PlacementResult(errors.MoveToImmutable(), []);
    }
}
