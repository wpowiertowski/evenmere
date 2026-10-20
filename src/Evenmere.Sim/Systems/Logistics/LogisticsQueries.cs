using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Logistics;

/// <summary>Implements <see cref="ILogisticsQueries"/>. M1-00 declares the methods and M1-09 implements them; until then each throws <see cref="NotImplementedException"/>.</summary>
internal sealed class LogisticsQueries : ILogisticsQueries
{
    /// <inheritdoc />
    public ReadOnlySpan<TransportJobView> ActiveJobs() => throw new NotImplementedException("M1-09");

    /// <inheritdoc />
    public int FreeCarts(EntityId storehouse) => throw new NotImplementedException("M1-09");
}
