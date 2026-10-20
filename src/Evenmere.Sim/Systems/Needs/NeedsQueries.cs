using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Needs;

/// <summary>Implements <see cref="INeedsQueries"/>. M1-00 declares the methods and M1-10 implements them; until then each throws <see cref="NotImplementedException"/>.</summary>
internal sealed class NeedsQueries : INeedsQueries
{
    /// <inheritdoc />
    public Ratio Fulfilment(GoodId good) => throw new NotImplementedException("M1-10");

    /// <inheritdoc />
    public Amount DemandYesterday(GoodId good) => throw new NotImplementedException("M1-10");

    /// <inheritdoc />
    public bool TryGetHouseNeeds(EntityId house, out HouseNeedsInfo needs) => throw new NotImplementedException("M1-10");
}
