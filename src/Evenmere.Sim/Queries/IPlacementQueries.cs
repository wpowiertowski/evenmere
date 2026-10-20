using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Queries;

/// <summary>
/// Read-only placement queries ("can I build this here?"), which draw on the map, roads and progression. The command
/// handlers use the same code, so the ghost preview and the command always agree (architecture.md, Construction).
/// Implemented by the construction system (M1-06) for buildings and the roads system (M1-05) for roads.
/// </summary>
public interface IPlacementQueries
{
    /// <summary>Checks whether a building could be placed.</summary>
    /// <param name="type">The building type.</param>
    /// <param name="x">The column of the footprint's top-left tile after rotation.</param>
    /// <param name="y">The row of the footprint's top-left tile after rotation.</param>
    /// <param name="rotation">The orientation; a quarter turn swaps the footprint's width and height.</param>
    /// <returns>The errors (placement is refused) and warnings (placement is allowed) with their affected tiles.</returns>
    PlacementResult Validate(BuildingTypeId type, int x, int y, Rotation rotation);

    /// <summary>
    /// Checks a run of road tiles. The command places only the valid prefix of the path, so the tiles of the first error
    /// are where the valid part ends.
    /// </summary>
    /// <param name="path">The tiles in order, such as the L-shaped drag the player is previewing.</param>
    /// <param name="roadType">The road type (a building type with a road cost).</param>
    /// <returns>The errors and warnings with their affected tiles.</returns>
    PlacementResult ValidateRoad(ReadOnlySpan<TileCoord> path, BuildingTypeId roadType);
}
