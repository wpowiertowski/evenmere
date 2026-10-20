using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Population;

/// <summary>A house advanced to the next tier.</summary>
/// <param name="Tick">The tick during which the house advanced.</param>
/// <param name="House">The house's entity ID.</param>
/// <param name="FromTier">The tier it had.</param>
/// <param name="ToTier">The tier it has now.</param>
public sealed record HouseTierAdvanced(long Tick, EntityId House, TierId FromTier, TierId ToTier) : SimEvent(Tick);

/// <summary>A house stepped down one tier after its grace period of unmet basic needs ran out.</summary>
/// <param name="Tick">The tick during which the house declined.</param>
/// <param name="House">The house's entity ID.</param>
/// <param name="FromTier">The tier it had.</param>
/// <param name="ToTier">The tier it has now, one below <paramref name="FromTier"/>.</param>
public sealed record HouseTierDeclined(long Tick, EntityId House, TierId FromTier, TierId ToTier) : SimEvent(Tick);

/// <summary>Residents moved into a house. Emitted at most once per house per day, with the day's total.</summary>
/// <param name="Tick">The tick during which the residents moved in.</param>
/// <param name="House">The house's entity ID.</param>
/// <param name="Count">How many residents arrived, as a whole count; at least 1.</param>
public sealed record ResidentsMovedIn(long Tick, EntityId House, int Count) : SimEvent(Tick);
