using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Statistics;

/// <summary>Implements <see cref="IStatisticsQueries"/> over the statistics state. Every answer is a copy.</summary>
/// <param name="state">The statistics slice of the world state.</param>
internal sealed class StatisticsQueries(StatisticsState state) : IStatisticsQueries
{
    /// <inheritdoc />
    /// <remarks>
    /// The newest samples that fit are copied oldest first. When fewer days were sampled than the span holds, only those
    /// are written; when more days were sampled than the 40-day ring holds, only the newest 40 can be returned.
    /// </remarks>
    public int Series(StatKey key, Span<long> destination)
    {
        int goodCount = state.GoodCount;
        int tierCount = state.TierCount;
        int series = StatisticsState.SeriesIndex(key.Kind, key.Subject, goodCount, tierCount);
        if (series < 0 || state.Daily.Length != state.SeriesCount * StatisticsState.DailyCapacity)
        {
            return 0;
        }

        long sampled = state.DaysSampled;
        long keep = Math.Min(sampled, StatisticsState.DailyCapacity);
        int count = (int)Math.Min(destination.Length, keep);
        if (count <= 0)
        {
            return 0;
        }

        long firstDay = sampled - count;
        int offset = series * StatisticsState.DailyCapacity;
        for (int i = 0; i < count; i++)
        {
            long day = firstDay + i;
            int slot = (int)(day % StatisticsState.DailyCapacity);
            destination[i] = state.Daily[offset + slot];
        }

        return count;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The newest seasons that fit are copied oldest first. When fewer seasons were sampled than the span holds, only
    /// those are written; when more seasons were sampled than the 8-season ring holds, only the newest 8 can be
    /// returned. The total seasons sampled rides along so callers can label each sample with its season and year.
    /// </remarks>
    public (int Written, long Seasons) SeasonalSeries(StatKey key, Span<long> destination)
    {
        int goodCount = state.GoodCount;
        int tierCount = state.TierCount;
        int series = StatisticsState.SeriesIndex(key.Kind, key.Subject, goodCount, tierCount);
        long sampled = state.SeasonsSampled;
        if (series < 0 || state.Seasonal.Length != state.SeriesCount * StatisticsState.SeasonalCapacity)
        {
            return (0, sampled);
        }

        long keep = Math.Min(sampled, StatisticsState.SeasonalCapacity);
        int count = (int)Math.Min(destination.Length, keep);
        if (count <= 0)
        {
            return (0, sampled);
        }

        long firstSeason = sampled - count;
        int offset = series * StatisticsState.SeasonalCapacity;
        for (int i = 0; i < count; i++)
        {
            long season = firstSeason + i;
            int slot = (int)(season % StatisticsState.SeasonalCapacity);
            destination[i] = state.Seasonal[offset + slot];
        }

        return (count, sampled);
    }
}
