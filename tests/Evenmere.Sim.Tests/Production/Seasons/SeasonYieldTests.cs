using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Production.Seasons;

public sealed class SeasonYieldTests
{
    // A 5x5 block of grain-fertile plains under the farm at (10, 10), so its area yield is full.
    private static void MakeGrainFields(ProductionHarness h)
    {
        uint grain = 1u << (int)CropKind.Grain;
        for (int y = 10; y < 15; y++)
        {
            for (int x = 10; x < 15; x++)
            {
                h.Map.SetTile(x, y, h.Tile("plains", grain));
            }
        }
    }

    private static EntityId AddReadyGrainFarm(ProductionHarness h)
    {
        EntityId farm = h.AddReady("grain_farm", 10, 10);
        h.SetFill(farm, 1000);
        return farm;
    }

    // Takes the whole output buffer, as a cart would over the day, and counts what came out.
    private static Amount Drain(ProductionHarness h, EntityId farm)
    {
        GoodId grain = h.Good("grain");
        Amount buffered = Amount.Zero;
        foreach (GoodAmount held in h.Info(farm).OutputBuffer)
        {
            if (held.Good == grain)
            {
                buffered = held.Amount;
            }
        }

        if (buffered > Amount.Zero)
        {
            h.System.Buffers.TryTakeOutput(farm, grain, buffered).ShouldBeTrue();
        }

        return buffered;
    }

    [Theory]
    [InlineData(Season.Spring, 1000)]
    [InlineData(Season.Summer, 1200)]
    [InlineData(Season.Autumn, 1100)]
    [InlineData(Season.Winter, 700)]
    public void GrainFarm_Productivity_MatchesTheSeasonCurve(Season season, int expected)
    {
        var h = ProductionHarness.Create();
        MakeGrainFields(h);
        EntityId farm = AddReadyGrainFarm(h);

        h.Sim.SetTick((long)season * GameTime.TicksPerSeason);
        h.RunTicks(1);

        h.Info(farm).Productivity.PerMille.ShouldBe(expected);
    }

    [Fact]
    public void GrainFarm_YearlyOutput_EqualsTheMeanOfTheCurveTimesTheBase()
    {
        var h = ProductionHarness.Create();
        MakeGrainFields(h);
        EntityId farm = AddReadyGrainFarm(h);

        // A 40 s cycle is 160,000 progress. Spring banks 1,200,000 (7 grain, 80,000 over), summer adds
        // 1,440,000 (9 grain), autumn 1,320,000 (8 grain) and winter 840,000 (6 grain): 30 for the year,
        // the base 30 cycles at full speed, because the curve means exactly 1000.
        int[] expected = [7, 9, 8, 6];
        Amount year = Amount.Zero;
        for (int s = 0; s < expected.Length; s++)
        {
            Amount season = Amount.Zero;
            for (int d = 0; d < GameTime.DaysPerSeason; d++)
            {
                h.RunTicks(GameTime.TicksPerDay);
                season += Drain(h, farm);
            }

            season.ShouldBe(Amount.FromUnits(expected[s]), $"season {s}");
            year += season;
        }

        year.ShouldBe(Amount.FromUnits(30));
    }
}
