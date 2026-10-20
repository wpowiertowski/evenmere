using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Services;

/// <summary>
/// The building half of <c>IPlacementQueries</c>. Construction (M1-06) implements it and uses the same code in its
/// <c>PlaceBuilding</c> handler, so the ghost preview and the command never disagree. <c>PlacementQueries</c> forwards to it.
/// </summary>
internal interface IBuildingPlacement
{
    /// <summary>Checks whether a building could be placed.</summary>
    /// <param name="type">The building type.</param>
    /// <param name="x">The column of the footprint's top-left tile after rotation.</param>
    /// <param name="y">The row of the footprint's top-left tile after rotation.</param>
    /// <param name="rotation">The orientation.</param>
    /// <returns>The errors and warnings with their affected tiles.</returns>
    PlacementResult Validate(BuildingTypeId type, int x, int y, Rotation rotation);
}

/// <summary>
/// The road half of <c>IPlacementQueries</c>. Roads (M1-05) implements it and uses the same code in its
/// <c>PlaceRoad</c> handler. <c>PlacementQueries</c> forwards to it.
/// </summary>
internal interface IRoadPlacement
{
    /// <summary>Checks a run of road tiles; the command places only the valid prefix.</summary>
    /// <param name="path">The tiles in order.</param>
    /// <param name="roadType">The road type.</param>
    /// <returns>The errors and warnings with their affected tiles.</returns>
    PlacementResult ValidateRoad(ReadOnlySpan<TileCoord> path, BuildingTypeId roadType);
}
