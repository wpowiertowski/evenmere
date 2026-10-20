using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Statistics;

/// <summary>Implements <see cref="IStatisticsQueries"/>. M1-00 declares the methods and M1-12 implements them; until then each throws <see cref="NotImplementedException"/>.</summary>
internal sealed class StatisticsQueries : IStatisticsQueries
{
    /// <inheritdoc />
    public int Series(StatKey key, Span<long> destination) => throw new NotImplementedException("M1-12");
}
