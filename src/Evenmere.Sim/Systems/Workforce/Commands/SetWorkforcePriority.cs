using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Workforce.Commands;

/// <summary>
/// Marks a workplace as high priority, so the daily workforce allocation fills it before the others. Refused with
/// <see cref="RejectReason.UnknownTarget"/> or, for a building that needs no workers, <see cref="RejectReason.NotApplicable"/>.
/// Handled by M1-07.
/// </summary>
/// <param name="Building">The workplace's entity ID.</param>
/// <param name="High"><see langword="true"/> for high priority, <see langword="false"/> for normal.</param>
public sealed record SetWorkforcePriority(EntityId Building, bool High) : Command;
