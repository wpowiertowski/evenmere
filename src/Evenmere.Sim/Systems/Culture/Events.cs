using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Culture;

/// <summary>A civic was adopted and its effects applied. (A civic's inspiration is reported by <c>InspirationTriggered</c>, which lives with the research events.)</summary>
/// <param name="Tick">The tick during which the civic was adopted.</param>
/// <param name="Civic">The civic.</param>
public sealed record CivicAdopted(long Tick, CivicId Civic) : SimEvent(Tick);

/// <summary>A festival was held: its goods were taken, its effects applied, and its run began. (M2-08.)</summary>
/// <param name="Tick">The tick during which the festival was held.</param>
/// <param name="Festival">The festival.</param>
/// <param name="Ground">The festival ground it runs at.</param>
public sealed record FestivalStarted(long Tick, FestivalId Festival, EntityId Ground) : SimEvent(Tick);

/// <summary>A festival ran its last day: its effects were removed and the running flag cleared. (M2-08.)</summary>
/// <param name="Tick">The tick during which the festival ended.</param>
/// <param name="Festival">The festival.</param>
/// <param name="Ground">The festival ground it ran at.</param>
public sealed record FestivalEnded(long Tick, FestivalId Festival, EntityId Ground) : SimEvent(Tick);
