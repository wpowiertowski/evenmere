using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Queries;

/// <summary>Implements <see cref="IExplainQueries"/>. A stand-in that throws until M1-12 replaces it.</summary>
internal sealed class ExplainQueries : IExplainQueries
{
    /// <inheritdoc />
    public Explanation ExplainGood(GoodId good) => throw new NotImplementedException("M1-12");

    /// <inheritdoc />
    public Explanation ExplainBuilding(EntityId building) => throw new NotImplementedException("M1-12");

    /// <inheritdoc />
    public Explanation ExplainHouse(EntityId house) => throw new NotImplementedException("M1-12");
}
