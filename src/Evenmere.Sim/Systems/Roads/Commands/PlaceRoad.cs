using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Roads.Commands;

/// <summary>
/// Lays a road along a path of tiles. Roads are free and are never construction sites. Roads places the valid prefix of
/// the path (see <c>IPlacementQueries.ValidateRoad</c>), and a road laid over a road of another type upgrades it. Refused
/// with <see cref="RejectReason.PlacementInvalid"/> if not even the first tile is valid. Handled by M1-05.
/// </summary>
/// <remarks>The array is part of the command, so the sender must not change it after enqueueing the command. Two commands with equal paths are equal.</remarks>
/// <param name="Path">The tiles in order, each next to the one before.</param>
/// <param name="RoadType">The road type (a building type with a road cost).</param>
public sealed record PlaceRoad(TileCoord[] Path, BuildingTypeId RoadType) : Command
{
    /// <inheritdoc />
    public bool Equals(PlaceRoad? other) =>
        other is not null && RoadType == other.RoadType && ValueArrays.Equal(Path, other.Path);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(RoadType, ValueArrays.Hash(Path));
}
