using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Systems.Roads;

/// <summary>
/// The road half of <c>IPlacementQueries</c> (ADR-0011). The ghost preview calls <see cref="ValidateRoad"/> and the
/// <c>PlaceRoad</c> handler calls <see cref="Evaluate"/>, which are the same check, so the preview never promises what the
/// command then refuses. It reads the map and progression and never changes anything.
/// </summary>
/// <remarks>
/// Rules, checked per tile and reporting the first that fails: the road type must be an unlocked road type (for the whole
/// path); then the tile must be on the map, on terrain whose access flag (if any) research has opened, on terrain that is
/// buildable and does not block roads, not a river, and free of buildings.
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

        int prefix = -1;
        for (int i = 0; i < path.Length; i++)
        {
            issues[i] = Check(sim, path[i]);
            if (prefix < 0 && issues[i] != PlacementIssue.None)
            {
                prefix = i;
            }
        }

        return prefix < 0 ? path.Length : prefix;
    }

    private PlacementIssue Check(ISimQueries sim, TileCoord tile)
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

        // TODO(M2-04): a bridge road type may cross river tiles up to its bridgeSpan; until then no road type may.
        if (!terrain.Buildable || terrain.BlocksRoads || info.Has(TileFeatures.River))
        {
            return PlacementIssue.TerrainNotAllowed;
        }

        return sim.Map.BuildingAt(tile.X, tile.Y).IsNone ? PlacementIssue.None : PlacementIssue.Occupied;
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
