using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Queries;

/// <summary>
/// Read-only queries over the consumption and service coverage of houses (progression.md, How needs work). Implemented
/// by the needs system (M1-10), which computes the fair shares once at the start of each day.
/// </summary>
public interface INeedsQueries
{
    /// <summary>
    /// Gets the fair share of a good: the fraction of its demand that the island can supply today, which every house
    /// consumes at. It is <c>min(1, (stock + producedYesterday) ÷ demand)</c>, computed at the start of the day.
    /// </summary>
    /// <param name="good">The good.</param>
    /// <returns>The fulfilment in per-mille (1000 = fully supplied); 1000 for a good nobody needs.</returns>
    Ratio Fulfilment(GoodId good);

    /// <summary>Gets how much of a good the houses wanted on the previous whole day, which is the sum over houses of residents × rate.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The demand in milli-units per day; 0 during the first day.</returns>
    Amount DemandYesterday(GoodId good);

    /// <summary>Describes the needs of one house.</summary>
    /// <param name="house">The house.</param>
    /// <param name="needs">A copy of the house's needs, when it exists.</param>
    /// <returns><see langword="true"/> if the building is a house.</returns>
    bool TryGetHouseNeeds(EntityId house, out HouseNeedsInfo needs);
}
