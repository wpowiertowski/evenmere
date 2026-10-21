using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Time;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Statistics;

/// <summary>
/// The statistics system: rolling history for graphs. Its cadence is <see cref="Cadence.Daily"/>, so the pipeline runs it
/// once a day at day start, after every other daily system, and each run samples one day.
/// </summary>
/// <remarks>
/// <para>
/// Each run samples the island as the other daily systems left it: the stock from Storage, the produced and demand
/// yesterdays from Production and Needs, the consumed delta from the Consumed counter, the residents per tier from
/// Population, the knowledge and culture per day from Research and Culture, and the active transport jobs from Logistics.
/// </para>
/// <para>Seasonal rollups average each series over the 10 days of a season once the season completes, keeping 8 seasons.</para>
/// </remarks>
internal sealed class StatisticsSystem : ISimSystem
{
    private readonly StatisticsState _state;
    private readonly ContentDb _content;

    /// <summary>Initializes a new instance of the <see cref="StatisticsSystem"/> class.</summary>
    /// <param name="state">The statistics slice of the world state; a state read from a save continues where it left off.</param>
    /// <param name="content">The loaded content, which sizes the history.</param>
    public StatisticsSystem(StatisticsState state, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);

        _state = state;
        _content = content;
        _state.Ensure(content.Goods.Count, content.Tiers.Count);
        Queries = new StatisticsQueries(_state);
    }

    /// <inheritdoc />
    public string Name => SystemNames.Statistics;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IStatisticsQueries Queries { get; }

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // Statistics has no commands: it only samples what the other systems report.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        _state.Ensure(_content.Goods.Count, _content.Tiers.Count);

        int goodCount = _state.GoodCount;
        int tierCount = _state.TierCount;
        int seriesCount = _state.SeriesCount;
        if (seriesCount == 3 && goodCount == 0 && tierCount == 0)
        {
            // No goods and no tiers (empty content): still count the day so the clock and the history agree.
            _state.DaysSampled++;
            return;
        }

        int slot = (int)(_state.DaysSampled % StatisticsState.DailyCapacity);
        long[] daily = _state.Daily;

        for (int g = 0; g < goodCount; g++)
        {
            var good = GoodId.From(checked((ushort)g));
            long stock = ctx.Queries.Storage.Stock(good).Milli;
            long produced = ctx.Queries.Production.ProducedYesterday(good).Milli;
            long demand = ctx.Queries.Needs.DemandYesterday(good).Milli;

            long cumulative = ctx.Queries.Progression.Counter(CounterKey.Consumed(good));
            long consumed = cumulative - _state.LastConsumed[g];
            if (consumed < 0)
            {
                consumed = 0;
            }

            _state.LastConsumed[g] = cumulative;

            daily[(g * StatisticsState.DailyCapacity) + slot] = stock;
            daily[((goodCount + g) * StatisticsState.DailyCapacity) + slot] = produced;
            daily[(((goodCount * 2) + g) * StatisticsState.DailyCapacity) + slot] = consumed;
            daily[(((goodCount * 3) + g) * StatisticsState.DailyCapacity) + slot] = demand;
        }

        for (int t = 0; t < tierCount; t++)
        {
            var tier = TierId.From(checked((ushort)t));
            int residents = ctx.Queries.Population.Residents(tier);
            daily[(((goodCount * 4) + t) * StatisticsState.DailyCapacity) + slot] = residents;
        }

        daily[(((goodCount * 4) + tierCount) * StatisticsState.DailyCapacity) + slot] = ctx.Queries.Research.PointsPerDay.Milli;
        daily[((((goodCount * 4) + tierCount) + 1) * StatisticsState.DailyCapacity) + slot] = ctx.Queries.Culture.PointsPerDay.Milli;

        long jobs = ctx.Queries.Logistics.ActiveJobs().Length;

        daily[((((goodCount * 4) + tierCount) + 2) * StatisticsState.DailyCapacity) + slot] = jobs;

        _state.DaysSampled++;

        if (_state.DaysSampled % GameTime.DaysPerSeason == 0)
        {
            RollSeason();
        }
    }

    private void RollSeason()
    {
        int seriesCount = _state.SeriesCount;
        int seasonSlot = (int)(_state.SeasonsSampled % StatisticsState.SeasonalCapacity);
        long firstDay = _state.DaysSampled - GameTime.DaysPerSeason;

        for (int s = 0; s < seriesCount; s++)
        {
            long sum = 0;
            int dailyOffset = s * StatisticsState.DailyCapacity;
            for (int d = 0; d < GameTime.DaysPerSeason; d++)
            {
                int slot = (int)((firstDay + d) % StatisticsState.DailyCapacity);
                sum += _state.Daily[dailyOffset + slot];
            }

            _state.Seasonal[(s * StatisticsState.SeasonalCapacity) + seasonSlot] = sum / GameTime.DaysPerSeason;
        }

        _state.SeasonsSampled++;
    }
}
