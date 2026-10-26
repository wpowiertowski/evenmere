using Evenmere.Core.Ids;
using MessagePack;

namespace Evenmere.Sim.Systems.Culture.Festivals;

/// <summary>
/// A running festival, as the save holds it: the festival, the ground it runs at and the daily runs left. The
/// culture system's daily step credits the day's culture and counts down <see cref="DaysLeft"/>; the run ends at
/// the step after it reaches zero, so a festival of N days pays N credits.
/// </summary>
/// <param name="Festival">The festival being held.</param>
/// <param name="Ground">The festival ground it runs at.</param>
/// <param name="DaysLeft">How many more daily culture credits the festival pays out.</param>
[MessagePackObject]
public readonly record struct FestivalRun(
    [property: Key(0)] FestivalId Festival,
    [property: Key(1)] EntityId Ground,
    [property: Key(2)] int DaysLeft);
