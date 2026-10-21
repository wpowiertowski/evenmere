using Evenmere.Sim.Persistence;
using MessagePack;

namespace Evenmere.Sim.Systems.Statistics;

/// <summary>
/// The statistics system's slice of the world state: a 40-day ring buffer per <see cref="Queries.Types.StatKey"/> and
/// seasonal averages for 2 years (8 seasons). Every member has an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
/// <remarks>
/// <para>
/// Series are stored flat: <c>Daily[series * DailyCapacity + slot]</c> where <c>slot = day % DailyCapacity</c> and
/// <c>day</c> is the zero-based sample number (<see cref="DaysSampled"/> counts samples). The series order is: for each
/// good in ID order stock, produced, consumed and demand (four blocks of <see cref="GoodCount"/>), then residents per
/// tier in ID order (<see cref="TierCount"/>), then knowledge, culture and active jobs. Seasonal averages use the same
/// series order with <see cref="SeasonalCapacity"/> slots per series.
/// </para>
/// <para>All history values are milli-units for goods series and whole counts for tier and job series, as <see cref="Queries.Types.StatKind"/> says.</para>
/// </remarks>
[MessagePackObject]
public sealed class StatisticsState
{
    /// <summary>Gets the number of daily samples kept per series.</summary>
    public const int DailyCapacity = 40;

    /// <summary>Gets the number of seasonal averages kept per series (2 years of 4 seasons).</summary>
    public const int SeasonalCapacity = 8;

    /// <summary>Gets or sets how many goods the history arrays are sized for.</summary>
    [Key(0)]
    public int GoodCount { get; set; }

    /// <summary>Gets or sets how many tiers the history arrays are sized for.</summary>
    [Key(1)]
    public int TierCount { get; set; }

    /// <summary>Gets or sets how many daily samples have been taken in all.</summary>
    [Key(2)]
    public long DaysSampled { get; set; }

    /// <summary>Gets or sets the daily samples, flat by series then ring slot (see the class remarks).</summary>
    [Key(3)]
    [ContentIndexed(ContentKind.Good, ContentKind.Tier)]
    public long[] Daily { get; set; } = [];

    /// <summary>Gets or sets the seasonal averages, flat by series then ring slot (see the class remarks).</summary>
    [Key(4)]
    [ContentIndexed(ContentKind.Good, ContentKind.Tier)]
    public long[] Seasonal { get; set; } = [];

    /// <summary>Gets or sets how many seasons have been averaged in all.</summary>
    [Key(5)]
    public long SeasonsSampled { get; set; }

    /// <summary>Gets or sets the last-seen cumulative consumption per good, in milli-units, so each day samples the delta.</summary>
    [Key(6)]
    [ContentIndexed(ContentKind.Good)]
    public long[] LastConsumed { get; set; } = [];

    /// <summary>Gets how many series the state holds for its sized goods and tiers.</summary>
    /// <returns>The series count: four per good, one per tier, and three singles.</returns>
    [IgnoreMember]
    public int SeriesCount => (GoodCount * 4) + TierCount + 3;

    /// <summary>
    /// Sizes the history arrays to the loaded content, keeping the counters. A new game starts empty and gets zeroed
    /// arrays; a state whose counts already match is left alone. When the counts differ (different content), the history
    /// is cleared because the old series order no longer applies, but the sample counts are kept.
    /// </summary>
    /// <param name="goodCount">The number of goods in the content.</param>
    /// <param name="tierCount">The number of tiers in the content.</param>
    internal void Ensure(int goodCount, int tierCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(goodCount);
        ArgumentOutOfRangeException.ThrowIfNegative(tierCount);

        if (GoodCount == goodCount && TierCount == tierCount
            && Daily.Length == (((goodCount * 4) + tierCount + 3) * DailyCapacity)
            && Seasonal.Length == (((goodCount * 4) + tierCount + 3) * SeasonalCapacity)
            && LastConsumed.Length == goodCount)
        {
            return;
        }

        int series = (goodCount * 4) + tierCount + 3;
        long[] previousConsumed = LastConsumed;
        Daily = new long[series * DailyCapacity];
        Seasonal = new long[series * SeasonalCapacity];
        LastConsumed = new long[goodCount];
        if (previousConsumed.Length > 0 && goodCount > 0)
        {
            Array.Copy(previousConsumed, LastConsumed, Math.Min(previousConsumed.Length, LastConsumed.Length));
        }

        GoodCount = goodCount;
        TierCount = tierCount;
    }

    /// <summary>
    /// Gets the series index of a <see cref="Queries.Types.StatKey"/> for sized counts, or -1 when the subject is out of range.
    /// </summary>
    /// <param name="kind">The series kind.</param>
    /// <param name="subject">The raw subject ID (a good or tier value, or 0 for the singles).</param>
    /// <param name="goodCount">The sized goods.</param>
    /// <param name="tierCount">The sized tiers.</param>
    /// <returns>The series index, or -1 for an unknown subject.</returns>
    internal static int SeriesIndex(Queries.Types.StatKind kind, int subject, int goodCount, int tierCount) => kind switch
    {
        Queries.Types.StatKind.Stock when subject >= 0 && subject < goodCount => subject,
        Queries.Types.StatKind.Produced when subject >= 0 && subject < goodCount => goodCount + subject,
        Queries.Types.StatKind.Consumed when subject >= 0 && subject < goodCount => (goodCount * 2) + subject,
        Queries.Types.StatKind.Demand when subject >= 0 && subject < goodCount => (goodCount * 3) + subject,
        Queries.Types.StatKind.Residents when subject >= 0 && subject < tierCount => (goodCount * 4) + subject,
        Queries.Types.StatKind.KnowledgePerDay when subject == 0 => (goodCount * 4) + tierCount,
        Queries.Types.StatKind.CulturePerDay when subject == 0 => ((goodCount * 4) + tierCount) + 1,
        Queries.Types.StatKind.ActiveJobs when subject == 0 => ((goodCount * 4) + tierCount) + 2,
        _ => -1,
    };
}
