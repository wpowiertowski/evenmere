using Evenmere.Game.Ui.Stats;

namespace Evenmere.Game.Tests;

/// <summary>M2-22: the chart geometry maps values to pixels and hover positions to points.</summary>
public sealed class ChartGeometryTests
{
    private const float Tight = 0.001f;

    [Fact]
    public void Bounds_PadsByATenth()
    {
        (long min, long max) = ChartGeometry.Bounds([10, 20, 30]);
        min.ShouldBe(8L);
        max.ShouldBe(32L);
    }

    [Fact]
    public void Bounds_FlatRunWidens()
    {
        (long min, long max) = ChartGeometry.Bounds([5, 5]);
        min.ShouldBeLessThan(5L);
        max.ShouldBeGreaterThan(5L);
    }

    [Fact]
    public void Bounds_FlatCounts_SpanOneUnitEachWay()
    {
        ChartGeometry.Bounds([12, 12]).ShouldBe((11L, 13L));
    }

    [Fact]
    public void Bounds_FlatMilli_SpansOneDisplayUnitEachWay()
    {
        ChartGeometry.Bounds([2000, 2000], displayUnit: 1000).ShouldBe((1000L, 3000L));
    }

    [Fact]
    public void Bounds_FlatZeroMilli_FloorsAtZero()
    {
        ChartGeometry.Bounds([0, 0], floorZero: true, displayUnit: 1000).ShouldBe((0L, 1000L));
    }

    [Fact]
    public void Bounds_FloorZero_ClampsNegativePad()
    {
        (long min, long max) = ChartGeometry.Bounds([0, 0], floorZero: true);
        min.ShouldBe(0L);
        max.ShouldBeGreaterThan(0L);
    }

    [Fact]
    public void Bounds_WithoutFloor_KeepsNegativePad()
    {
        (long min, long _) = ChartGeometry.Bounds([-5, 5]);
        min.ShouldBeLessThan(0L);
    }

    [Fact]
    public void Bounds_EmptyIsUnitRange()
    {
        ChartGeometry.Bounds([]).ShouldBe((0L, 1L));
    }

    [Fact]
    public void Map_CornersLandOnThePlot()
    {
        // The axis ends map to the plot edges, whatever the data range is.
        (float x0, float yTop) = ChartGeometry.Map(0, 32, 3, 8, 32, 100f, 50f);
        (float x2, float yBottom) = ChartGeometry.Map(2, 8, 3, 8, 32, 100f, 50f);
        x0.ShouldBe(0f, Tight);
        yTop.ShouldBe(0f, Tight);
        x2.ShouldBe(100f, Tight);
        yBottom.ShouldBe(50f, Tight);
    }

    [Fact]
    public void Map_SinglePointCentres()
    {
        (float x, _) = ChartGeometry.Map(0, 5, 1, 4, 6, 100f, 50f);
        x.ShouldBe(50f, Tight);
    }

    [Fact]
    public void HoverIndex_SnapsToNearest()
    {
        ChartGeometry.HoverIndex(0f, 100f, 11).ShouldBe(0);
        ChartGeometry.HoverIndex(100f, 100f, 11).ShouldBe(10);
        ChartGeometry.HoverIndex(54f, 100f, 11).ShouldBe(5);
        ChartGeometry.HoverIndex(-20f, 100f, 11).ShouldBe(0);
        ChartGeometry.HoverIndex(200f, 100f, 11).ShouldBe(10);
    }

    [Fact]
    public void HoverIndex_EmptyIsNone()
    {
        ChartGeometry.HoverIndex(50f, 100f, 0).ShouldBe(-1);
    }

    [Fact]
    public void LegendRowOf_EmptyStaysEmpty()
    {
        ChartGeometry.LegendRowOf([], 200f).ShouldBeEmpty();
    }

    [Fact]
    public void LegendRowOf_FittingEntriesShareARow()
    {
        ChartGeometry.LegendRowOf([80f, 90f, 50f], 250f).ShouldBe([0, 0, 0]);
    }

    [Fact]
    public void LegendRowOf_WrapsPastTheWidth()
    {
        ChartGeometry.LegendRowOf([100f, 100f, 100f], 250f).ShouldBe([0, 0, 1]);
    }

    [Fact]
    public void LegendRowOf_OversizedEntryKeepsItsOwnRow()
    {
        ChartGeometry.LegendRowOf([300f, 50f], 250f).ShouldBe([0, 1]);
    }

    [Fact]
    public void LegendRowOf_RejectsBadInput()
    {
        Should.Throw<ArgumentNullException>(() => ChartGeometry.LegendRowOf(null!, 200f));
        Should.Throw<ArgumentOutOfRangeException>(() => ChartGeometry.LegendRowOf([10f], 0f));
        Should.Throw<ArgumentOutOfRangeException>(() => ChartGeometry.LegendRowOf([-1f], 200f));
    }
}
