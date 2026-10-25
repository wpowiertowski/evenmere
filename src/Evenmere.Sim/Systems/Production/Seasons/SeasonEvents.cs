using Evenmere.Core.Time;

namespace Evenmere.Sim.Systems.Production.Seasons;

/// <summary>The calendar crossed into a new season. Emitted once, on the first tick of the new season.</summary>
/// <param name="Tick">The first tick of the new season.</param>
/// <param name="Season">The season that began.</param>
public sealed record SeasonChanged(long Tick, Season Season) : SimEvent(Tick);
