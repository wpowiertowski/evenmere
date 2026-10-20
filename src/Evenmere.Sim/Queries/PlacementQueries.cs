using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Queries;

/// <summary>
/// Implements <see cref="IPlacementQueries"/> by forwarding each query to the system that owns its rules: buildings to
/// Construction (M1-06) and roads to Roads (M1-05). Both are throwing stand-ins until those tasks land.
/// </summary>
internal sealed class PlacementQueries(IBuildingPlacement buildings, IRoadPlacement roads) : IPlacementQueries
{
    /// <inheritdoc />
    public PlacementResult Validate(BuildingTypeId type, int x, int y, Rotation rotation) => buildings.Validate(type, x, y, rotation);

    /// <inheritdoc />
    public PlacementResult ValidateRoad(ReadOnlySpan<TileCoord> path, BuildingTypeId roadType) => roads.ValidateRoad(path, roadType);
}
