using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Happenings;

namespace Evenmere.Sim.Queries;

/// <summary>
/// Read-only queries over the gentle choice events. Filled by M2-12 (ADR-0017): the pending happening, if one waits
/// for a choice, and whether a happening is cooling down.
/// </summary>
public interface IHappeningsQueries
{
    /// <summary>Gets the happening waiting for a choice, if one is pending; at most one is pending at a time. (M2-12.)</summary>
    /// <returns>The pending happening with its raised tick, or <see langword="null"/> when none waits.</returns>
    PendingHappening? PendingHappening();

    /// <summary>Tests whether a happening is cooling down after a recent resolve. (M2-12.)</summary>
    /// <param name="happening">The happening.</param>
    /// <returns><see langword="true"/> while the cooldown lasts; <see langword="false"/> for an unknown happening.</returns>
    bool IsOnCooldown(HappeningId happening);
}
