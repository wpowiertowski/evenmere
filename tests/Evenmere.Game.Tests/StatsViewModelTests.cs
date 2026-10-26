using System.Collections.Immutable;
using Evenmere.Core.Ids;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Stats;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Game.Tests;

/// <summary>M2-22: the statistics view-model turns series into chart lines, windows and a debug summary.</summary>
public sealed class StatsViewModelTests
{
    private static readonly TierId _settlers = TierId.From(0);
    private static readonly TierId _villagers = TierId.From(1);
    private static readonly GoodId _bread = GoodId.From(0);
    private static readonly GoodId _timber = GoodId.From(1);

    [Fact]
    public void OnRefresh_PlotsResidentsPerTier()
    {
        var data = new FakeStatsData(
            new Dictionary<StatKey, long[]>
            {
                [StatKey.Residents(_settlers)] = [10, 12],
                [StatKey.Residents(_villagers)] = [0, 3],
            },
            tick: 240);

        StatsViewModel model = Refresh(data);
        model.HasData.ShouldBeTrue();
        model.PopulationLines.Length.ShouldBe(2);
        model.PopulationLines[0].Values.ShouldBe([10L, 12L]);
        model.PopulationLines[1].Values.ShouldBe([0L, 3L]);
        model.PopulationLines[0].IsMilli.ShouldBeFalse();
        model.PopulationLines[0].ColorIndex.ShouldBe(StatsPalette.PopulationTier(0));
    }

    [Fact]
    public void OnRefresh_NetsProducedMinusConsumed()
    {
        var data = new FakeStatsData(
            new Dictionary<StatKey, long[]>
            {
                [StatKey.Produced(_bread)] = [5000, 8000],
                [StatKey.Consumed(_bread)] = [3000, 9000],
                [StatKey.Stock(_bread)] = [12000, 11000],
            },
            tick: 240);

        StatsViewModel model = Refresh(data);
        model.StockLine.Values.ShouldBe([12000L, 11000L]);
        model.NetLine.Values.ShouldBe([2000L, -1000L]);
        model.NetLine.LastRaw.ShouldBe(-1000L);
    }

    [Fact]
    public void OnRefresh_DebugSummaryMatchesRawSeries()
    {
        var data = new FakeStatsData(
            new Dictionary<StatKey, long[]>
            {
                [StatKey.Stock(_bread)] = [12500],
                [StatKey.Produced(_bread)] = [2000],
                [StatKey.Consumed(_bread)] = [1500],
                [StatKey.KnowledgePerDay] = [4500],
                [StatKey.CulturePerDay] = [3200],
            },
            tick: 120);

        StatsViewModel model = Refresh(data);
        model.DebugSummary.ShouldContain("stock n=1 last=12");
        model.DebugSummary.ShouldContain("net n=1 last=0.5/d");
        model.DebugSummary.ShouldContain("knowledge n=1 last=4.5/d");
        model.DebugSummary.ShouldContain("culture n=1 last=3.2/d");
    }

    [Fact]
    public void SetWindow_TwoYears_BucketsTens()
    {
        long[] days = new long[25];
        for (int i = 0; i < days.Length; i++)
        {
            days[i] = 1000L * (i + 1);
        }

        var data = new FakeStatsData(
            new Dictionary<StatKey, long[]> { [StatKey.KnowledgePerDay] = days },
            tick: 25 * 120);

        StatsViewModel model = Refresh(data);
        model.KnowledgeLine.Values.Length.ShouldBe(25);

        model.SetWindow(StatsWindow.TwoYears);
        model.Advance(1.0);
        model.KnowledgeLine.Values.Length.ShouldBe(3);
        model.DayStep.ShouldBe(StatsWindowShape.BucketDays);
    }

    [Fact]
    public void SelectGood_SwitchesTheGoodsCharts()
    {
        var data = new FakeStatsData(
            new Dictionary<StatKey, long[]>
            {
                [StatKey.Stock(_bread)] = [1000],
                [StatKey.Stock(_timber)] = [9000],
                [StatKey.Produced(_bread)] = [100],
                [StatKey.Produced(_timber)] = [200],
                [StatKey.Consumed(_bread)] = [50],
                [StatKey.Consumed(_timber)] = [50],
            },
            tick: 120);

        StatsViewModel model = Refresh(data);
        model.SelectedGood.ShouldBe(_bread);
        model.StockLine.Values.ShouldBe([1000L]);

        model.SelectGood(_timber);
        model.Advance(1.0);
        model.SelectedGood.ShouldBe(_timber);
        model.StockLine.Values.ShouldBe([9000L]);

        model.SelectGood(GoodId.From(9));
        model.Advance(1.0);
        model.SelectedGood.ShouldBe(_timber);
    }

    [Fact]
    public void OnRefresh_NotRunning_ShowsEmpty()
    {
        var data = new FakeStatsData(new Dictionary<StatKey, long[]>(), tick: 0) { Running = false };
        StatsViewModel model = Refresh(data);
        model.HasData.ShouldBeFalse();
        model.PopulationLines.IsEmpty.ShouldBeTrue();
    }

    private static StatsViewModel Refresh(FakeStatsData data)
    {
        var model = new StatsViewModel(data, new UiStrings(TestLocales.Table()));
        model.Advance(1.0);
        return model;
    }

    private sealed class FakeStatsData(Dictionary<StatKey, long[]> series, long tick) : IStatsData
    {
        public bool Running { get; set; } = true;

        public bool IsRunning => Running;

        public long CurrentTick => tick;

        public ImmutableArray<(TierId Id, string Name)> Tiers { get; } =
        [
            (_settlers, "Settlers"),
            (_villagers, "Villagers"),
        ];

        public ImmutableArray<(GoodId Id, string Name)> Goods { get; } =
        [
            (_bread, "Bread"),
            (_timber, "Timber"),
        ];

        public int ReadSeries(StatKey key, Span<long> destination)
        {
            if (!series.TryGetValue(key, out long[]? values))
            {
                return 0;
            }

            int n = Math.Min(values.Length, destination.Length);
            for (int i = 0; i < n; i++)
            {
                destination[i] = values[i];
            }

            return n;
        }
    }
}
