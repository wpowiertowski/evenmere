using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Population.Commands;

/// <summary>
/// Locks or unlocks a house against advancing to the next tier. A locked house still grows and can still decline.
/// Refused with <see cref="RejectReason.UnknownTarget"/> or, for a building that is not a house, <see cref="RejectReason.NotApplicable"/>.
/// Handled by M1-10.
/// </summary>
/// <param name="House">The house's entity ID.</param>
/// <param name="Locked"><see langword="true"/> to lock, <see langword="false"/> to unlock.</param>
public sealed record SetHouseUpgradeLock(EntityId House, bool Locked) : Command;
