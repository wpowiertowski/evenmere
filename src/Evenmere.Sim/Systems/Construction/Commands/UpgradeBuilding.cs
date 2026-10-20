using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Construction.Commands;

/// <summary>
/// Starts an in-place upgrade of a building to the type that names it in <c>upgradeOf</c>. The old building keeps running
/// until the upgrade completes. House tiers are changed by Population as residents advance, not by this command. Refused
/// with <see cref="RejectReason.UnknownTarget"/>, <see cref="RejectReason.NotApplicable"/> (the type is not an upgrade of
/// the building's type) or <see cref="RejectReason.PlacementInvalid"/> (not unlocked). Handled by M1-06.
/// </summary>
/// <param name="Building">The building's entity ID.</param>
/// <param name="ToType">The building type to upgrade to.</param>
public sealed record UpgradeBuilding(EntityId Building, BuildingTypeId ToType) : Command;
