using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Construction.Commands;

/// <summary>
/// Demolishes a building or a construction site and refunds part of its materials to the storage pool. Refused with
/// <see cref="RejectReason.UnknownTarget"/> if the building does not exist and with <see cref="RejectReason.Protected"/>
/// for the Founders' Hall. Handled by M1-06.
/// </summary>
/// <param name="Building">The building's entity ID.</param>
public sealed record Demolish(EntityId Building) : Command;
