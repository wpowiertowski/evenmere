using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.Chains;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Game.Tests;

/// <summary>The simulation adapter reads the newest daily sample for the per-day rates, falls back to the
/// production query's yesterday totals on the first day, and passes the explain tree through untouched.</summary>
public sealed class SimChainDataTests
{
    private static ContentDb Db => TestContent.Db;

    [Fact]
    public void ProducedPerDay_UsesNewestSample()
    {
        GoodId bread = Db.Goods.IdOf("bread");
        FakeStatistics statistics = new FakeStatistics().WithSamples(StatKey.Produced(bread), [3_000, 12_500]);
        var data = new SimChainData(Db, new FakeMaps(), new FakeProductions(), statistics, new FakeExplains());

        data.ProducedPerDayMilli(bread).ShouldBe(12_500);
    }

    [Fact]
    public void ProducedPerDay_NoSamples_FallsBackToYesterday()
    {
        GoodId bread = Db.Goods.IdOf("bread");
        FakeProductions production = new FakeProductions().WithYesterday(bread, Amount.FromMilli(9_000));
        var data = new SimChainData(Db, new FakeMaps(), production, new FakeStatistics(), new FakeExplains());

        data.ProducedPerDayMilli(bread).ShouldBe(9_000);
    }

    [Fact]
    public void ConsumedPerDay_UsesNewestSample()
    {
        GoodId bread = Db.Goods.IdOf("bread");
        FakeStatistics statistics = new FakeStatistics().WithSamples(StatKey.Consumed(bread), [1_000, 8_000]);
        var data = new SimChainData(Db, new FakeMaps(), new FakeProductions(), statistics, new FakeExplains());

        data.ConsumedPerDayMilli(bread).ShouldBe(8_000);
    }

    [Fact]
    public void ConsumedPerDay_NoSamples_IsZero()
    {
        var data = new SimChainData(Db, new FakeMaps(), new FakeProductions(), new FakeStatistics(), new FakeExplains());

        data.ConsumedPerDayMilli(Db.Goods.IdOf("bread")).ShouldBe(0);
    }

    [Fact]
    public void ProducersOf_EmptyMap_IsEmpty()
    {
        var data = new SimChainData(Db, new FakeMaps(), new FakeProductions(), new FakeStatistics(), new FakeExplains());

        data.ProducersOf(Db.Goods.IdOf("bread")).ShouldBeEmpty();
    }

    [Fact]
    public void Explain_PassesThrough()
    {
        GoodId bread = Db.Goods.IdOf("bread");
        var explanation = new Explanation(ExplanationLine.Leaf("explain.good.supply", ExplainArg.Of(bread)));
        var data = new SimChainData(Db, new FakeMaps(), new FakeProductions(), new FakeStatistics(), new FakeExplains(explanation));

        data.Explain(bread).ShouldBe(explanation);
    }

    private sealed class FakeMaps : IMapQueries
    {
        public int Width => 1;

        public int Height => 1;

        public TileInfo GetTile(int x, int y) => default;

        public EntityId BuildingAt(int x, int y) => EntityId.None;

        public bool TryGetBuilding(EntityId id, out BuildingInfo building)
        {
            building = default;
            return false;
        }

        public BuildingSequence Buildings => default;

        public BuildingSequence ActiveBuildings => default;

        public int DrainDirtyChunks(Span<ChunkCoord> destination) => 0;
    }

    private sealed class FakeProductions : IProductionQueries
    {
        private readonly Dictionary<GoodId, Amount> _yesterday = new();

        public FakeProductions WithYesterday(GoodId good, Amount amount)
        {
            _yesterday[good] = amount;
            return this;
        }

        public bool TryGet(EntityId building, out ProductionInfo info)
        {
            info = null!;
            return false;
        }

        public Amount ProducedYesterday(GoodId good) => _yesterday.GetValueOrDefault(good);
    }

    private sealed class FakeStatistics : IStatisticsQueries
    {
        private readonly Dictionary<StatKey, long[]> _series = new();

        public FakeStatistics WithSamples(StatKey key, long[] samples)
        {
            _series[key] = samples;
            return this;
        }

        public int Series(StatKey key, Span<long> destination)
        {
            if (!_series.TryGetValue(key, out long[]? samples))
            {
                return 0;
            }

            int count = Math.Min(samples.Length, destination.Length);
            for (int i = 0; i < count; i++)
            {
                destination[i] = samples[i];
            }

            return count;
        }
    }

    private sealed class FakeExplains : IExplainQueries
    {
        private readonly Explanation _explanation;

        public FakeExplains()
            : this(new Explanation(ExplanationLine.Leaf("explain.good.unknown", ExplainArg.Of(0))))
        {
        }

        public FakeExplains(Explanation explanation) => _explanation = explanation;

        public Explanation ExplainGood(GoodId good) => _explanation;

        public Explanation ExplainBuilding(EntityId building) => _explanation;

        public Explanation ExplainHouse(EntityId house) => _explanation;
    }
}
