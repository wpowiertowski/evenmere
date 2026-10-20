using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Construction.Commands;

/// <summary>
/// Places a building: Construction checks the same rules as <c>IPlacementQueries.Validate</c> and, if there is no error,
/// registers a construction site that then draws its materials from the storage pool. Refused with
/// <see cref="RejectReason.PlacementInvalid"/> when the check finds an error. Roads are not placed with this command; use
/// <c>PlaceRoad</c>. Handled by M1-06.
/// </summary>
/// <param name="Type">The building type.</param>
/// <param name="X">The column of the footprint's top-left tile after rotation.</param>
/// <param name="Y">The row of the footprint's top-left tile after rotation.</param>
/// <param name="Rotation">The orientation; a quarter or three-quarter turn swaps the footprint's width and height.</param>
public sealed record PlaceBuilding(BuildingTypeId Type, int X, int Y, Rotation Rotation) : Command;
