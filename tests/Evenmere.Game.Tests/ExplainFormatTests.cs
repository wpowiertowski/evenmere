using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Explain;

namespace Evenmere.Game.Tests;

public sealed class ExplainFormatTests
{
    [Fact]
    public void FormatPercent_WholeAndFractional()
    {
        ExplainFormat.FormatPercent(Ratio.FromPerMille(1000)).ShouldBe("100%");
        ExplainFormat.FormatPercent(Ratio.FromPerMille(125)).ShouldBe("12.5%");
    }

    [Fact]
    public void FormatCost_WholeTiles()
    {
        ExplainFormat.FormatCost(3000).ShouldBe("3 tiles");
    }

    [Fact]
    public void FormatReach_HereWhenNoCost()
    {
        var strings = new UiStrings(TestLocales.Table());
        string text = ExplainFormat.FormatReach(0, 20, strings);
        text.ShouldContain("20");
    }

    [Fact]
    public void FormatReach_DetailRoundsUpToTiles()
    {
        var strings = new UiStrings(TestLocales.Table());
        string text = ExplainFormat.FormatReach(1500, 20, strings);
        text.ShouldContain("2");
        text.ShouldContain("20");
    }

    [Fact]
    public void NumberFormat_UnitsDropsFractions()
    {
        NumberFormat.Units(Amount.FromUnits(12) + Amount.FromMilli(900)).ShouldBe("12");
    }
}
