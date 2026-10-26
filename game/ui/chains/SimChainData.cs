using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Game.Ui.Chains;

/// <summary>
/// The live numbers behind <see cref="ChainViewModel"/>, read from the simulation's query interfaces (M2-21 reads
/// existing queries and adds no sim surface). Producer counts and average productivity come from the finished
/// buildings behind each good; produced and consumed per day come from the statistics daily samples, with the
/// production query's yesterday totals as the first-day fallback; the bottleneck comes from the explain queries.
/// </summary>
public sealed class SimChainData : IChainData
{
    private const int SampleWindow = 8;

    private readonly ContentDb _content;
    private readonly IMapQueries _map;
    private readonly IProductionQueries _production;
    private readonly IStatisticsQueries _statistics;
    private readonly IExplainQueries _explain;
    private readonly long[] _samples = new long[SampleWindow];

    /// <summary>Initializes a new instance of the <see cref="SimChainData"/> class.</summary>
    /// <param name="content">The content database, for matching producers to goods through their recipes.</param>
    /// <param name="map">The finished buildings, in ascending ID order.</param>
    /// <param name="production">The producers and their yesterday totals.</param>
    /// <param name="statistics">The daily samples behind the per-day rates.</param>
    /// <param name="explain">The shortage chains behind the bottleneck highlight.</param>
    public SimChainData(
        ContentDb content,
        IMapQueries map,
        IProductionQueries production,
        IStatisticsQueries statistics,
        IExplainQueries explain)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _production = production ?? throw new ArgumentNullException(nameof(production));
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        _explain = explain ?? throw new ArgumentNullException(nameof(explain));
    }

    /// <inheritdoc />
    public IReadOnlyList<ChainProducer> ProducersOf(GoodId good)
    {
        var producers = new List<ChainProducer>();
        foreach (BuildingInfo building in _map.ActiveBuildings)
        {
            if (!_production.TryGet(building.Id, out ProductionInfo info))
            {
                continue;
            }

            if (info.Recipe.Value >= _content.Recipes.Count)
            {
                continue;
            }

            bool produces = false;
            foreach (GoodAmount output in _content.Get(info.Recipe).Outputs)
            {
                if (output.Good == good)
                {
                    produces = true;
                    break;
                }
            }

            if (produces)
            {
                producers.Add(new ChainProducer(building.Id, building.Type, info.Productivity.PerMille, info.Stall));
            }
        }

        return producers;
    }

    /// <inheritdoc />
    public long ProducedPerDayMilli(GoodId good)
    {
        int count = _statistics.Series(StatKey.Produced(good), _samples);
        return count > 0 ? _samples[count - 1] : _production.ProducedYesterday(good).Milli;
    }

    /// <inheritdoc />
    public long ConsumedPerDayMilli(GoodId good)
    {
        int count = _statistics.Series(StatKey.Consumed(good), _samples);
        return count > 0 ? _samples[count - 1] : 0;
    }

    /// <inheritdoc />
    public Explanation Explain(GoodId good) => _explain.ExplainGood(good);
}
