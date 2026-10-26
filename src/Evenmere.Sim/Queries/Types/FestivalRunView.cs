using Evenmere.Core.Ids;

namespace Evenmere.Sim.Queries.Types;

/// <summary>
/// The running festival as the view sees it: the festival, the ground it runs at and the daily credits left.
/// It mirrors <see cref="Systems.Culture.Festivals.FestivalRun"/> without touching the culture state, so view
/// code (which must never reference state types, ADR-0006) can read it through <see cref="ICultureQueries"/>.
/// (M2-27.)
/// </summary>
/// <param name="Festival">The festival being held.</param>
/// <param name="Ground">The festival ground it runs at.</param>
/// <param name="DaysLeft">How many more daily culture credits the festival pays out.</param>
public readonly record struct FestivalRunView(FestivalId Festival, EntityId Ground, int DaysLeft);
