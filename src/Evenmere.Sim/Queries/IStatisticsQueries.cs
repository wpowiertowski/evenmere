using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Queries;

/// <summary>Read-only queries over the rolling history for graphs. Implemented by the statistics system (M1-12), which samples once a day.</summary>
public interface IStatisticsQueries
{
    /// <summary>
    /// Copies the most recent daily samples of a series. The system keeps 40 days of samples, so a longer span receives at
    /// most 40 values.
    /// </summary>
    /// <param name="key">The series.</param>
    /// <param name="destination">
    /// Receives the samples, oldest first and newest last, filling from the start. Goods series are in milli-units, tier
    /// and job series are whole counts, as <see cref="StatKind"/> says.
    /// </param>
    /// <returns>The number of samples written, which is the smaller of the span length and the days sampled so far.</returns>
    int Series(StatKey key, Span<long> destination);
}
