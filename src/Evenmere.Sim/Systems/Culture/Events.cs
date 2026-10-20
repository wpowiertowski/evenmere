using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Culture;

/// <summary>A civic was adopted and its effects applied. (A civic's inspiration is reported by <c>InspirationTriggered</c>, which lives with the research events.)</summary>
/// <param name="Tick">The tick during which the civic was adopted.</param>
/// <param name="Civic">The civic.</param>
public sealed record CivicAdopted(long Tick, CivicId Civic) : SimEvent(Tick);
