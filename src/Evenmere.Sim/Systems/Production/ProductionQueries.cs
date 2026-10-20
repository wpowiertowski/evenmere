using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Production;

/// <summary>Implements <see cref="IProductionQueries"/>. M1-00 declares the methods and M1-08 implements them; until then each throws <see cref="NotImplementedException"/>.</summary>
internal sealed class ProductionQueries : IProductionQueries
{
    /// <inheritdoc />
    public bool TryGet(EntityId building, out ProductionInfo info) => throw new NotImplementedException("M1-08");

    /// <inheritdoc />
    public Amount ProducedYesterday(GoodId good) => throw new NotImplementedException("M1-08");
}
