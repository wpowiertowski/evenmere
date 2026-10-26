using Evenmere.Game.Ui.Stats;

namespace Evenmere.Game.Tests;

/// <summary>M2-33: the statistics chart wraps its empty note inside the plot, so longer calm messages never clip.</summary>
public sealed class LineChartWrapTests
{
    private static float Measure(string word) => word.Length * 10f;

    [Fact]
    public void Wrap_FitsOnOneLine_StaysWhole()
    {
        LineChart.WrapEmptyText("No data yet.", 300f, Measure).ShouldBe(["No data yet."]);
    }

    [Fact]
    public void Wrap_TooWide_BreaksAtSpaces()
    {
        LineChart.WrapEmptyText("The long view needs two full seasons first.", 250f, Measure)
            .ShouldBe(["The long view needs two", "full seasons first."]);
    }

    [Fact]
    public void Wrap_ReassemblesWithoutLoss()
    {
        const string text = "The long view needs two full seasons first.";
        string joined = string.Join(" ", LineChart.WrapEmptyText(text, 150f, Measure));
        joined.ShouldBe(text);
    }
}
