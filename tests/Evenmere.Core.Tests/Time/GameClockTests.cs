using Evenmere.Core.Ids;
using Evenmere.Core.Time;

namespace Evenmere.Core.Tests.Time;

public sealed class GameClockTests
{
    [Fact]
    public void Constants_TimeModel_MatchesAdr0003()
    {
        GameTime.TicksPerDay.ShouldBe(120);
        GameTime.DaysPerSeason.ShouldBe(10);
        GameTime.SeasonsPerYear.ShouldBe(4);
        GameTime.DaysPerYear.ShouldBe(40);
        GameTime.TicksPerSeason.ShouldBe(1_200);
        GameTime.TicksPerYear.ShouldBe(4_800);
    }

    [Fact]
    public void Default_IsFirstTickOfSpringInYearZero()
    {
        GameClock clock = default;

        clock.Tick.ShouldBe(0);
        clock.Day.ShouldBe(0);
        clock.TickOfDay.ShouldBe(0);
        clock.DayOfSeason.ShouldBe(0);
        clock.Season.ShouldBe(Season.Spring);
        clock.Year.ShouldBe(0);
        clock.IsDayStart.ShouldBeTrue();
        clock.IsSeasonStart.ShouldBeTrue();
    }

    [Fact]
    public void Day_Tick119To120_StartsANewDay()
    {
        GameClock last = new(119);
        GameClock next = new(120);

        last.Day.ShouldBe(0);
        last.TickOfDay.ShouldBe(119);
        last.IsDayStart.ShouldBeFalse();
        next.Day.ShouldBe(1);
        next.TickOfDay.ShouldBe(0);
        next.IsDayStart.ShouldBeTrue();
        next.IsSeasonStart.ShouldBeFalse();
    }

    [Fact]
    public void Season_Day9To10_StartsANewSeason()
    {
        GameClock lastOfDay9 = new((10 * GameTime.TicksPerDay) - 1);
        GameClock firstOfDay10 = new(10 * GameTime.TicksPerDay);

        lastOfDay9.Day.ShouldBe(9);
        lastOfDay9.Season.ShouldBe(Season.Spring);
        lastOfDay9.IsSeasonStart.ShouldBeFalse();
        firstOfDay10.Day.ShouldBe(10);
        firstOfDay10.Season.ShouldBe(Season.Summer);
        firstOfDay10.DayOfSeason.ShouldBe(0);
        firstOfDay10.IsSeasonStart.ShouldBeTrue();
    }

    [Fact]
    public void Year_Day40_IsYearOneSpring()
    {
        GameClock day39 = new((40 * GameTime.TicksPerDay) - 1);
        GameClock day40 = new(40 * GameTime.TicksPerDay);

        day39.Year.ShouldBe(0);
        day39.Season.ShouldBe(Season.Winter);
        day39.Day.ShouldBe(39);
        day40.Day.ShouldBe(40);
        day40.Year.ShouldBe(1);
        day40.Season.ShouldBe(Season.Spring);
        day40.IsSeasonStart.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0L, Season.Spring)]
    [InlineData(1_199L, Season.Spring)]
    [InlineData(1_200L, Season.Summer)]
    [InlineData(2_399L, Season.Summer)]
    [InlineData(2_400L, Season.Autumn)]
    [InlineData(3_599L, Season.Autumn)]
    [InlineData(3_600L, Season.Winter)]
    [InlineData(4_799L, Season.Winter)]
    [InlineData(4_800L, Season.Spring)]
    [InlineData(6_000L, Season.Summer)]
    [InlineData(48_000L, Season.Spring)]
    public void Season_Tick_CyclesThroughTheYear(long tick, Season expected)
    {
        new GameClock(tick).Season.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0L, 0)]
    [InlineData(1L, 0)]
    [InlineData(119L, 0)]
    [InlineData(120L, 1)]
    [InlineData(1_199L, 9)]
    [InlineData(1_200L, 0)]
    [InlineData(1_320L, 1)]
    public void DayOfSeason_Tick_CountsFromZeroEachSeason(long tick, int expected)
    {
        new GameClock(tick).DayOfSeason.ShouldBe(expected);
    }

    [Fact]
    public void IsSeasonStart_OverAYear_IsTrueExactlyFourTimes()
    {
        List<long> starts = [];
        for (long tick = 0; tick < GameTime.TicksPerYear; tick++)
        {
            if (new GameClock(tick).IsSeasonStart)
            {
                starts.Add(tick);
            }
        }

        starts.ShouldBe([0L, 1_200L, 2_400L, 3_600L]);
    }

    [Fact]
    public void IsDayStart_OverASeason_IsTrueOncePerDay()
    {
        int dayStarts = 0;
        for (long tick = 0; tick < GameTime.TicksPerSeason; tick++)
        {
            if (new GameClock(tick).IsDayStart)
            {
                dayStarts++;
            }
        }

        dayStarts.ShouldBe(GameTime.DaysPerSeason);
    }

    [Fact]
    public void Advance_OneTick_IncrementsTickAndCrossesBoundaries()
    {
        GameClock clock = new(118);

        clock = clock.Advance();
        clock.Tick.ShouldBe(119);
        clock.IsDayStart.ShouldBeFalse();

        clock = clock.Advance();
        clock.Tick.ShouldBe(120);
        clock.IsDayStart.ShouldBeTrue();
    }

    [Fact]
    public void Advance_LargestTick_ThrowsOverflowException()
    {
        GameClock clock = new(long.MaxValue);

        Should.Throw<OverflowException>(() => clock.Advance());
    }

    [Fact]
    public void Constructor_NegativeTick_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new GameClock(-1));
    }

    [Fact]
    public void Calendar_LargeTick_DoesNotOverflowIntFields()
    {
        GameClock clock = new(long.MaxValue);

        clock.TickOfDay.ShouldBeInRange(0, GameTime.TicksPerDay - 1);
        clock.DayOfSeason.ShouldBeInRange(0, GameTime.DaysPerSeason - 1);
        clock.Year.ShouldBe(long.MaxValue / GameTime.TicksPerYear);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(119, 119)]
    [InlineData(120, 0)]
    [InlineData(121, 1)]
    [InlineData(1_000_000, 40)]
    [InlineData(int.MaxValue, 7)]
    public void BucketOf_EntityId_IsIdModulo120(int id, int expectedBucket)
    {
        GameClock.BucketOf(new EntityId(id)).ShouldBe(expectedBucket);
    }

    [Fact]
    public void IsInBucket_OverADay_MatchesEachEntityExactlyOnce()
    {
        EntityId[] ids = [new(1), new(2), new(120), new(121), new(5_000), new(65_432)];

        foreach (EntityId id in ids)
        {
            int hits = 0;
            for (long tick = 0; tick < GameTime.TicksPerDay; tick++)
            {
                GameClock clock = new(tick);
                if (clock.IsInBucket(id))
                {
                    hits++;
                    clock.TickOfDay.ShouldBe(GameClock.BucketOf(id));
                }
            }

            hits.ShouldBe(1);
        }
    }

    [Fact]
    public void IsInBucket_SameBucketOnLaterDays_StaysTrue()
    {
        EntityId id = new(250);

        new GameClock(10).IsInBucket(id).ShouldBeTrue();
        new GameClock(130).IsInBucket(id).ShouldBeTrue();
        new GameClock(131).IsInBucket(id).ShouldBeFalse();
    }

    [Fact]
    public void Equality_SameTick_IsEqual()
    {
        new GameClock(5).ShouldBe(new GameClock(5));
        (new GameClock(5) == new GameClock(5)).ShouldBeTrue();
        (new GameClock(5) != new GameClock(6)).ShouldBeTrue();
        new GameClock(5).GetHashCode().ShouldBe(new GameClock(5).GetHashCode());
        new GameClock(5).Equals((object)new GameClock(5)).ShouldBeTrue();
        new GameClock(5).Equals("not a clock").ShouldBeFalse();
    }

    [Fact]
    public void ToString_Clock_DescribesTheCalendar()
    {
        new GameClock(4_800 + 1_200 + 120 + 7).ToString().ShouldBe("GameClock(tick 6127: year 1, Summer, day 1, tick 7)");
    }
}
