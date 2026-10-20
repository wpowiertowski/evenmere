using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Construction;

/// <summary>A building was placed and registered as a construction site (emitted by <c>PlaceBuilding</c>). The view shows scaffolding.</summary>
/// <param name="Tick">The tick during which the building was placed.</param>
/// <param name="Building">The new site's entity ID.</param>
/// <param name="Type">The building type.</param>
/// <param name="Origin">The footprint's top-left tile after rotation.</param>
/// <param name="Rotation">The orientation.</param>
public sealed record BuildingPlaced(long Tick, EntityId Building, BuildingTypeId Type, TileCoord Origin, Rotation Rotation) : SimEvent(Tick);

/// <summary>A construction site finished and became an active building.</summary>
/// <param name="Tick">The tick during which the site finished.</param>
/// <param name="Building">The building's entity ID.</param>
/// <param name="Type">The building type.</param>
public sealed record ConstructionCompleted(long Tick, EntityId Building, BuildingTypeId Type) : SimEvent(Tick);

/// <summary>A building or site was demolished and is gone from the registry.</summary>
/// <param name="Tick">The tick during which the building was demolished.</param>
/// <param name="Building">The entity ID the building had.</param>
/// <param name="Type">The building type it had.</param>
/// <param name="Origin">The footprint's top-left tile after rotation.</param>
public sealed record BuildingDemolished(long Tick, EntityId Building, BuildingTypeId Type, TileCoord Origin) : SimEvent(Tick);

/// <summary>An in-place upgrade finished and the building changed type. House tier changes are reported as tier events instead.</summary>
/// <param name="Tick">The tick during which the upgrade finished.</param>
/// <param name="Building">The building's entity ID, which does not change.</param>
/// <param name="FromType">The type the building had.</param>
/// <param name="ToType">The type the building has now.</param>
public sealed record BuildingUpgraded(long Tick, EntityId Building, BuildingTypeId FromType, BuildingTypeId ToType) : SimEvent(Tick);
