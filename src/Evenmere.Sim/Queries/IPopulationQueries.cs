using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Queries;

/// <summary>Read-only queries over the houses, residents and tiers. Implemented by the population system (M1-10).</summary>
public interface IPopulationQueries
{
    /// <summary>Gets how many residents live in the houses of a tier.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The number of residents, as a whole count.</returns>
    int Residents(TierId tier);

    /// <summary>Gets how many residents live on the island in all tiers.</summary>
    int TotalResidents { get; }

    /// <summary>Describes one house.</summary>
    /// <param name="house">The house.</param>
    /// <param name="info">A copy of the house's state, when it exists.</param>
    /// <returns><see langword="true"/> if the building is a house.</returns>
    bool TryGetHouse(EntityId house, out HouseInfo info);
}
