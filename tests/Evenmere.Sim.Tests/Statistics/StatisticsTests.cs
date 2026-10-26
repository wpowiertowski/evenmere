using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.Systems.Statistics;
using Evenmere.Sim.State;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Statistics;

public sealed class StatisticsTests
{
    private static FakeSim CreateSim()
    {
        return FakeSim.Create();
    }

    private static GoodId Good(FakeSim sim, int index)
    {
        return GoodId.From(checked((ushort)index));
    }

    [Fact]
    public void Sampling_OneDay_RecordsStockProducedConsumedDemandResidentsAndJobs()
    {
        FakeSim sim = CreateSim();
        var system = new StatisticsSystem(sim.World.Statistics, sim.Content);

        GoodId fish = sim.Content.IdOf<GoodId>("fish");
        TierId settlers = sim.Content.IdOf<TierId>("settlers");

        sim.Queries.Storage.Set(fish, Amount.FromUnits(30));
        sim.Queries.Production.SetProducedYesterday(fish, Amount.FromUnits(8));
        sim.Queries.Needs.SetDemandYesterday(fish, Amount.FromUnits(12));
        sim.Queries.Needs.SetFulfilment(fish, Ratio.FromPerMille(500));
        sim.Queries.Population.SetResidents(settlers, 6);
        sim.Queries.Progression.SetCounter(CounterKey.Consumed(fish), 2000);
        sim.Queries.Logistics.AddJob(new TransportJobView(new EntityId(1), new EntityId(2), new EntityId(3), fish, Amount.FromUnits(2), TransportDirection.Pickup, TransportPhase.TravelOut, 0, 10));

        system.Run(sim.Context);

        Span<long> stock = stackalloc long[4];
        system.Queries.Series(StatKey.Stock(fish), stock).ShouldBe(1);
        stock[0].ShouldBe(30000);

        Span<long> produced = stackalloc long[4];
        system.Queries.Series(StatKey.Produced(fish), produced).ShouldBe(1);
        produced[0].ShouldBe(8000);

        Span<long> consumed = stackalloc long[4];
        system.Queries.Series(StatKey.Consumed(fish), consumed).ShouldBe(1);
        consumed[0].ShouldBe(2000);

        Span<long> demand = stackalloc long[4];
        system.Queries.Series(StatKey.Demand(fish), demand).ShouldBe(1);
        demand[0].ShouldBe(12000);

        Span<long> residents = stackalloc long[4];
        system.Queries.Series(StatKey.Residents(settlers), residents).ShouldBe(1);
        residents[0].ShouldBe(6);

        Span<long> jobs = stackalloc long[4];
        system.Queries.Series(StatKey.ActiveJobs, jobs).ShouldBe(1);
        jobs[0].ShouldBe(1);

        Span<long> knowledge = stackalloc long[4];
        system.Queries.Series(StatKey.KnowledgePerDay, knowledge).ShouldBe(1);
        knowledge[0].ShouldBe(0);

        Span<long> culture = stackalloc long[4];
        system.Queries.Series(StatKey.CulturePerDay, culture).ShouldBe(1);
        culture[0].ShouldBe(0);
    }

    [Fact]
    public void Consumed_SecondDay_SamplesTheDelta()
    {
        FakeSim sim = CreateSim();
        var system = new StatisticsSystem(sim.World.Statistics, sim.Content);
        GoodId fish = sim.Content.IdOf<GoodId>("fish");

        sim.Queries.Progression.SetCounter(CounterKey.Consumed(fish), 2000);
        system.Run(sim.Context);

        sim.Queries.Progression.SetCounter(CounterKey.Consumed(fish), 3500);
        system.Run(sim.Context);

        Span<long> consumed = stackalloc long[4];
        system.Queries.Series(StatKey.Consumed(fish), consumed).ShouldBe(2);
        consumed[0].ShouldBe(2000);
        consumed[1].ShouldBe(1500);
    }

    [Fact]
    public void RingBuffer_WrapsAt40Days_OldestFirstNewestLast()
    {
        FakeSim sim = CreateSim();
        var system = new StatisticsSystem(sim.World.Statistics, sim.Content);
        GoodId fish = sim.Content.IdOf<GoodId>("fish");

        for (int day = 0; day < 45; day++)
        {
            sim.Queries.Storage.Set(fish, Amount.FromMilli(day * 1000L));
            system.Run(sim.Context);
        }

        Span<long> series = stackalloc long[40];
        system.Queries.Series(StatKey.Stock(fish), series).ShouldBe(40);

        // Days 0-4 fell off; days 5-44 remain, oldest first.
        series[0].ShouldBe(5000);
        series[39].ShouldBe(44000);

        Span<long> shortSpan = stackalloc long[3];
        system.Queries.Series(StatKey.Stock(fish), shortSpan).ShouldBe(3);
        shortSpan[0].ShouldBe(42000);
        shortSpan[1].ShouldBe(43000);
        shortSpan[2].ShouldBe(44000);
    }

    [Fact]
    public void SeasonalRollup_AveragesEachSeasonOverTenDays()
    {
        FakeSim sim = CreateSim();
        var system = new StatisticsSystem(sim.World.Statistics, sim.Content);
        GoodId fish = sim.Content.IdOf<GoodId>("fish");

        for (int day = 0; day < 20; day++)
        {
            sim.Queries.Storage.Set(fish, Amount.FromMilli((day + 1) * 1000L));
            system.Run(sim.Context);
        }

        sim.World.Statistics.SeasonsSampled.ShouldBe(2);

        // Season 0: days 1..10 units average 5.5 units; season 1: days 11..20 average 15.5 units.
        sim.World.Statistics.Seasonal.Length.ShouldBe(sim.World.Statistics.SeriesCount * StatisticsState.SeasonalCapacity);
        int series = StatisticsState.SeriesIndex(StatKind.Stock, fish.Value, sim.World.Statistics.GoodCount, sim.World.Statistics.TierCount);
        series.ShouldBeGreaterThanOrEqualTo(0);
        long[] seasonal = sim.World.Statistics.Seasonal;
        seasonal[(series * StatisticsState.SeasonalCapacity) + 0].ShouldBe(5500);
        seasonal[(series * StatisticsState.SeasonalCapacity) + 1].ShouldBe(15500);
    }

    [Fact]
    public void SeasonalRollup_AveragesEveryKindOverSeason()
    {
        FakeSim sim = CreateSim();
        var system = new StatisticsSystem(sim.World.Statistics, sim.Content);
        GoodId fish = sim.Content.IdOf<GoodId>("fish");
        TierId settlers = sim.Content.IdOf<TierId>("settlers");

        for (int day = 0; day < 10; day++)
        {
            sim.Queries.Storage.Set(fish, Amount.FromMilli((day + 1) * 1000L));
            sim.Queries.Production.SetProducedYesterday(fish, Amount.FromMilli(2000));
            sim.Queries.Needs.SetDemandYesterday(fish, Amount.FromMilli(500));
            sim.Queries.Progression.SetCounter(CounterKey.Consumed(fish), (day + 1) * 300L);
            sim.Queries.Population.SetResidents(settlers, 4);
            sim.Queries.Research.PointsPerDay = Amount.FromMilli(700);
            sim.Queries.Culture.PointsPerDay = Amount.FromMilli(110);
            system.Run(sim.Context);
        }

        sim.World.Statistics.SeasonsSampled.ShouldBe(1);

        long[] seasonal = sim.World.Statistics.Seasonal;
        int goodCount = sim.World.Statistics.GoodCount;
        int tierCount = sim.World.Statistics.TierCount;

        // Every kind averages its ten days: stock of 1..10 units is 5.5 units, residents stay at 4.
        int stock = StatisticsState.SeriesIndex(StatKind.Stock, fish.Value, goodCount, tierCount);
        seasonal[(stock * StatisticsState.SeasonalCapacity) + 0].ShouldBe(5500);
        int residents = StatisticsState.SeriesIndex(StatKind.Residents, settlers.Value, goodCount, tierCount);
        seasonal[(residents * StatisticsState.SeasonalCapacity) + 0].ShouldBe(4);

        // Flows average like levels: 2 units produced, 0.3 consumed, 0.5 demanded, 0.7 knowledge, 0.11 culture.
        int produced = StatisticsState.SeriesIndex(StatKind.Produced, fish.Value, goodCount, tierCount);
        seasonal[(produced * StatisticsState.SeasonalCapacity) + 0].ShouldBe(2000);
        int consumed = StatisticsState.SeriesIndex(StatKind.Consumed, fish.Value, goodCount, tierCount);
        seasonal[(consumed * StatisticsState.SeasonalCapacity) + 0].ShouldBe(300);
        int demand = StatisticsState.SeriesIndex(StatKind.Demand, fish.Value, goodCount, tierCount);
        seasonal[(demand * StatisticsState.SeasonalCapacity) + 0].ShouldBe(500);
        int knowledge = StatisticsState.SeriesIndex(StatKind.KnowledgePerDay, 0, goodCount, tierCount);
        seasonal[(knowledge * StatisticsState.SeasonalCapacity) + 0].ShouldBe(700);
        int culture = StatisticsState.SeriesIndex(StatKind.CulturePerDay, 0, goodCount, tierCount);
        seasonal[(culture * StatisticsState.SeasonalCapacity) + 0].ShouldBe(110);
    }

    [Fact]
    public void SeasonalSeries_ReturnsOldestFirstWithSeasonAverage()
    {
        FakeSim sim = CreateSim();
        var system = new StatisticsSystem(sim.World.Statistics, sim.Content);
        GoodId fish = sim.Content.IdOf<GoodId>("fish");

        for (int day = 0; day < 30; day++)
        {
            sim.Queries.Production.SetProducedYesterday(fish, Amount.FromMilli((day + 1) * 100L));
            system.Run(sim.Context);
        }

        Span<long> series = stackalloc long[8];
        (int written, long seasons) = system.Queries.SeasonalSeries(StatKey.Produced(fish), series);
        written.ShouldBe(3);
        seasons.ShouldBe(3);

        // Each season averages its ten days: days 1..10 average 0.55 units, then 1.55 and 2.55.
        series[..written].ToArray().ShouldBe([550L, 1550L, 2550L]);

        Span<long> shortSpan = stackalloc long[2];
        (int shortWritten, long shortSeasons) = system.Queries.SeasonalSeries(StatKey.Produced(fish), shortSpan);
        shortWritten.ShouldBe(2);
        shortSeasons.ShouldBe(3);
        shortSpan[..shortWritten].ToArray().ShouldBe([1550L, 2550L]);
    }

    [Fact]
    public void SeasonalSeries_WrapsAtEightSeasons()
    {
        FakeSim sim = CreateSim();
        var system = new StatisticsSystem(sim.World.Statistics, sim.Content);
        GoodId fish = sim.Content.IdOf<GoodId>("fish");

        for (int day = 0; day < 100; day++)
        {
            sim.Queries.Storage.Set(fish, Amount.FromMilli((day + 1) * 1000L));
            system.Run(sim.Context);
        }

        Span<long> series = stackalloc long[8];
        (int written, long seasons) = system.Queries.SeasonalSeries(StatKey.Stock(fish), series);
        written.ShouldBe(8);
        seasons.ShouldBe(10);

        // Ten seasons sampled, so the ring keeps the newest eight: seasons 2..9, each averaging its ten days.
        series[..written].ToArray().ShouldBe([25500L, 35500L, 45500L, 55500L, 65500L, 75500L, 85500L, 95500L]);
    }

    [Fact]
    public void SeasonalSeries_EmptyBeforeFirstSeason()
    {
        FakeSim sim = CreateSim();
        var system = new StatisticsSystem(sim.World.Statistics, sim.Content);
        GoodId fish = sim.Content.IdOf<GoodId>("fish");

        for (int day = 0; day < 4; day++)
        {
            system.Run(sim.Context);
        }

        Span<long> series = stackalloc long[8];
        (int written, long seasons) = system.Queries.SeasonalSeries(StatKey.Stock(fish), series);
        written.ShouldBe(0);
        seasons.ShouldBe(0);
    }

    [Fact]
    public void Sampling_IsDeterministic()
    {
        FakeSim first = CreateSim();
        FakeSim second = CreateSim();
        var firstSystem = new StatisticsSystem(first.World.Statistics, first.Content);
        var secondSystem = new StatisticsSystem(second.World.Statistics, second.Content);

        GoodId fish = first.Content.IdOf<GoodId>("fish");
        for (int day = 0; day < 12; day++)
        {
            var stock = Amount.FromMilli(day * 777L);
            first.Queries.Storage.Set(first.Content.IdOf<GoodId>("fish"), stock);
            second.Queries.Storage.Set(second.Content.IdOf<GoodId>("fish"), stock);
            firstSystem.Run(first.Context);
            secondSystem.Run(second.Context);
        }

        Span<long> firstSeries = stackalloc long[40];
        Span<long> secondSeries = stackalloc long[40];
        int firstCount = firstSystem.Queries.Series(StatKey.Stock(fish), firstSeries);
        int secondCount = secondSystem.Queries.Series(StatKey.Stock(fish), secondSeries);

        firstCount.ShouldBe(secondCount);
        firstSeries[..firstCount].ToArray().ShouldBe(secondSeries[..secondCount].ToArray());
    }

    [Fact]
    public void SaveRoundTrip_PreservesHistory()
    {
        FakeSim sim = CreateSim();
        var system = new StatisticsSystem(sim.World.Statistics, sim.Content);
        GoodId fish = sim.Content.IdOf<GoodId>("fish");

        for (int day = 0; day < 25; day++)
        {
            sim.Queries.Storage.Set(fish, Amount.FromMilli(day * 1000L));
            sim.Queries.Production.SetProducedYesterday(fish, Amount.FromMilli(2000));
            system.Run(sim.Context);
        }

        byte[] saved = StateSerializer.Write(sim.World);
        WorldState restored = StateSerializer.Read(saved, WorldBuilder.Content);
        var restoredQueries = new StatisticsQueries(restored.Statistics);

        Span<long> before = stackalloc long[40];
        Span<long> after = stackalloc long[40];
        int beforeCount = system.Queries.Series(StatKey.Stock(fish), before);
        int afterCount = restoredQueries.Series(StatKey.Stock(fish), after);

        afterCount.ShouldBe(beforeCount);
        after[..afterCount].ToArray().ShouldBe(before[..beforeCount].ToArray());
        restored.Statistics.DaysSampled.ShouldBe(sim.World.Statistics.DaysSampled);
        restored.Statistics.SeasonsSampled.ShouldBe(sim.World.Statistics.SeasonsSampled);

        Span<long> seasonalBefore = stackalloc long[8];
        Span<long> seasonalAfter = stackalloc long[8];
        (int writtenBefore, long seasonsBefore) = system.Queries.SeasonalSeries(StatKey.Produced(fish), seasonalBefore);
        (int writtenAfter, long seasonsAfter) = restoredQueries.SeasonalSeries(StatKey.Produced(fish), seasonalAfter);
        writtenBefore.ShouldBe(2);
        writtenAfter.ShouldBe(writtenBefore);
        seasonsAfter.ShouldBe(seasonsBefore);
        seasonalAfter[..writtenAfter].ToArray().ShouldBe(seasonalBefore[..writtenBefore].ToArray());
    }
}
