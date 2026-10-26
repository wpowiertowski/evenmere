using System.Collections.Immutable;

namespace Evenmere.Game.Ui.Stats;

/// <summary>Which span a statistics chart shows.</summary>
public enum StatsWindow
{
    /// <summary>The last 40 days, one point per day.</summary>
    Last40Days = 0,

    /// <summary>
    /// Up to two years, averaged into 10-day buckets (the sim's own season length), so long trends read at a
    /// glance. The sim keeps 40 daily samples in M2, so this view shows what history exists, up to four buckets.
    /// </summary>
    TwoYears = 1,
}

/// <summary>One plotted bucket: a run of consecutive daily samples, oldest first.</summary>
/// <param name="Start">The index of the first sample in the bucket.</param>
/// <param name="Count">How many samples the bucket holds.</param>
public readonly record struct StatsBucket(int Start, int Count);

/// <summary>
/// Pure windowing maths for the statistics graphs: how daily samples map onto the two window spans. It has no
/// Godot types, so the game tests cover it on its own.
/// </summary>
public static class StatsWindowShape
{
    /// <summary>How many days one two-year bucket averages, matching the sim's season length.</summary>
    public const int BucketDays = 10;

    /// <summary>
    /// Splits a sample run into plotted buckets, oldest first.
    /// </summary>
    /// <param name="sampleCount">How many daily samples exist, oldest first; not negative.</param>
    /// <param name="window">The span to show.</param>
    /// <returns>The buckets to plot; empty when there are no samples.</returns>
    public static ImmutableArray<StatsBucket> Buckets(int sampleCount, StatsWindow window)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleCount);
        if (sampleCount == 0)
        {
            return [];
        }

        if (window == StatsWindow.Last40Days)
        {
            ImmutableArray<StatsBucket>.Builder single = ImmutableArray.CreateBuilder<StatsBucket>(sampleCount);
            for (int i = 0; i < sampleCount; i++)
            {
                single.Add(new StatsBucket(i, 1));
            }

            return single.MoveToImmutable();
        }

        int buckets = (sampleCount + BucketDays - 1) / BucketDays;
        ImmutableArray<StatsBucket>.Builder builder = ImmutableArray.CreateBuilder<StatsBucket>(buckets);
        for (int b = 0; b < buckets; b++)
        {
            int start = b * BucketDays;
            builder.Add(new StatsBucket(start, Math.Min(BucketDays, sampleCount - start)));
        }

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Means a bucket in integer maths, rounding half away from zero, so a net-per-day line that oscillates
    /// around zero does not drift.
    /// </summary>
    /// <param name="samples">The daily samples, oldest first.</param>
    /// <param name="bucket">The bucket to mean.</param>
    /// <returns>The bucket's mean.</returns>
    public static long Mean(long[] samples, StatsBucket bucket)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegative(bucket.Start);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bucket.Count);
        if (bucket.Start + bucket.Count > samples.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(bucket), bucket, "The bucket runs past the samples.");
        }

        long sum = 0;
        for (int i = bucket.Start; i < bucket.Start + bucket.Count; i++)
        {
            sum = checked(sum + samples[i]);
        }

        if (sum >= 0)
        {
            return (sum + (bucket.Count / 2)) / bucket.Count;
        }

        return -((-sum + (bucket.Count / 2)) / bucket.Count);
    }
}
