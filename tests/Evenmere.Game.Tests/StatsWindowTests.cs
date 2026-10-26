using System.Collections.Immutable;
using Evenmere.Game.Ui.Stats;

namespace Evenmere.Game.Tests;

/// <summary>M2-22: the statistics windows slice daily samples into daily points or 10-day buckets.</summary>
public sealed class StatsWindowTests
{
    [Fact]
    public void Buckets_DailyWindow_PointsPerDay()
    {
        ImmutableArray<StatsBucket> buckets = StatsWindowShape.Buckets(5, StatsWindow.Last40Days);
        buckets.Length.ShouldBe(5);
        for (int i = 0; i < buckets.Length; i++)
        {
            buckets[i].ShouldBe(new StatsBucket(i, 1));
        }
    }

    [Fact]
    public void Buckets_Empty_StaysEmpty()
    {
        StatsWindowShape.Buckets(0, StatsWindow.Last40Days).IsEmpty.ShouldBeTrue();
        StatsWindowShape.Buckets(0, StatsWindow.TwoYears).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Buckets_TwoYearWindow_GroupsTens()
    {
        ImmutableArray<StatsBucket> buckets = StatsWindowShape.Buckets(25, StatsWindow.TwoYears);
        buckets.Length.ShouldBe(3);
        buckets[0].ShouldBe(new StatsBucket(0, 10));
        buckets[1].ShouldBe(new StatsBucket(10, 10));
        buckets[2].ShouldBe(new StatsBucket(20, 5));
    }

    [Fact]
    public void Buckets_TwoYearWindow_ShortRunIsOneBucket()
    {
        ImmutableArray<StatsBucket> buckets = StatsWindowShape.Buckets(7, StatsWindow.TwoYears);
        buckets.Length.ShouldBe(1);
        buckets[0].ShouldBe(new StatsBucket(0, 7));
    }

    [Fact]
    public void Buckets_RejectsNegativeCount()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => StatsWindowShape.Buckets(-1, StatsWindow.Last40Days));
    }

    [Fact]
    public void Mean_AveragesHalfAwayFromZero()
    {
        StatsWindowShape.Mean([1000, 2000, 3000], new StatsBucket(0, 3)).ShouldBe(2000L);
        StatsWindowShape.Mean([1, 2], new StatsBucket(0, 2)).ShouldBe(2L);
        StatsWindowShape.Mean([-1, -2], new StatsBucket(0, 2)).ShouldBe(-2L);
        StatsWindowShape.Mean([-1000, 1000], new StatsBucket(0, 2)).ShouldBe(0L);
    }

    [Fact]
    public void Mean_HonoursStart()
    {
        StatsWindowShape.Mean([9, 10, 20, 30], new StatsBucket(2, 2)).ShouldBe(25L);
    }

    [Fact]
    public void Mean_RejectsOutOfRangeBucket()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => StatsWindowShape.Mean([1, 2], new StatsBucket(1, 2)));
    }
}
