using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Production.Commands;

/// <summary>
/// Pauses or resumes a producer. A paused building produces nothing and stalls with <c>StallReason.Paused</c>, but still
/// counts as built. Refused with <see cref="RejectReason.UnknownTarget"/> or, for a building that is not a finished
/// producer, <see cref="RejectReason.NotApplicable"/>. Handled by M1-08.
/// </summary>
/// <param name="Building">The producer's entity ID.</param>
/// <param name="Paused"><see langword="true"/> to pause, <see langword="false"/> to resume.</param>
public sealed record SetBuildingPaused(EntityId Building, bool Paused) : Command;
