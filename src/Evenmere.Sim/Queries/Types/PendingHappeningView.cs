using Evenmere.Core.Ids;

namespace Evenmere.Sim.Queries.Types;

/// <summary>
/// The pending happening as the view sees it: the happening waiting for a choice and the tick it was raised on.
/// It mirrors <see cref="Systems.Happenings.PendingHappening"/> without touching the happenings state, so view
/// code (which must never reference state types, ADR-0006) can read it through <see cref="IHappeningsQueries"/>.
/// (M2-27.)
/// </summary>
/// <param name="Happening">The happening waiting for a choice.</param>
/// <param name="RaisedTick">The tick the daily step raised it on.</param>
public readonly record struct PendingHappeningView(HappeningId Happening, long RaisedTick);
