using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Population;

/// <summary>Implements <see cref="IPopulationQueries"/>. M1-00 declares the methods and M1-10 implements them; until then each throws <see cref="NotImplementedException"/>.</summary>
internal sealed class PopulationQueries : IPopulationQueries
{
    /// <inheritdoc />
    public int TotalResidents => throw new NotImplementedException("M1-10");

    /// <inheritdoc />
    public int Residents(TierId tier) => throw new NotImplementedException("M1-10");

    /// <inheritdoc />
    public bool TryGetHouse(EntityId house, out HouseInfo info) => throw new NotImplementedException("M1-10");
}
