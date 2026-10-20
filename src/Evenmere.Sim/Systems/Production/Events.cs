using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Production;

/// <summary>A producer stopped producing, or its stall reason changed to another one. Emitted only when the reason changes.</summary>
/// <param name="Tick">The tick during which the reason changed.</param>
/// <param name="Building">The producer's entity ID.</param>
/// <param name="Reason">The new stall reason; never <see cref="StallReason.None"/>.</param>
public sealed record ProductionStalled(long Tick, EntityId Building, StallReason Reason) : SimEvent(Tick);

/// <summary>A stalled producer resumed producing. Emitted only when the stall reason becomes <see cref="StallReason.None"/>.</summary>
/// <param name="Tick">The tick during which the producer resumed.</param>
/// <param name="Building">The producer's entity ID.</param>
public sealed record ProductionResumed(long Tick, EntityId Building) : SimEvent(Tick);
