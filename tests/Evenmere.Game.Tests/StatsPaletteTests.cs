using Evenmere.Game.Ui.Stats;

namespace Evenmere.Game.Tests;

/// <summary>M2-22: the statistics line colours are the colour-blind-safe Okabe-Ito palette in a fixed order.</summary>
public sealed class StatsPaletteTests
{
    [Fact]
    public void Hex_HoldsEightDistinctEntries()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < StatsPalette.Count; i++)
        {
            string hex = StatsPalette.Hex(i);
            hex.Length.ShouldBe(6);
            seen.Add(hex).ShouldBeTrue($"duplicate palette entry {hex}");
        }
    }

    [Fact]
    public void Hex_RolesPinTheDocumentedColours()
    {
        StatsPalette.Hex(StatsPalette.Stock).ShouldBe("0072B2");
        StatsPalette.Hex(StatsPalette.Net).ShouldBe("D55E00");
        StatsPalette.Hex(StatsPalette.Knowledge).ShouldBe("009E73");
        StatsPalette.Hex(StatsPalette.Culture).ShouldBe("CC79A7");
    }

    [Fact]
    public void Hex_WrapsAround()
    {
        StatsPalette.Hex(StatsPalette.Count).ShouldBe(StatsPalette.Hex(0));
    }

    [Fact]
    public void PopulationTier_CyclesWithoutYellowUntilNeeded()
    {
        for (int order = 0; order < StatsPalette.Count - 1; order++)
        {
            StatsPalette.PopulationTier(order).ShouldBe(order);
        }

        StatsPalette.Hex(StatsPalette.Count - 1).ShouldBe("F0E442");
        StatsPalette.PopulationTier(StatsPalette.Count - 1).ShouldBe(StatsPalette.Count - 1);
        StatsPalette.PopulationTier(StatsPalette.Count).ShouldBe(0);
    }

    [Fact]
    public void PopulationTier_RejectsNegativeOrder()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => StatsPalette.PopulationTier(-1));
    }
}
