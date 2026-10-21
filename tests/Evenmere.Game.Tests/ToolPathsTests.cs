using Evenmere.Game.Ui.Tools;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Game.Tests;

public sealed class ToolPathsTests
{
    [Fact]
    public void LPath_HorizontalFirst()
    {
        Span<TileCoord> path = stackalloc TileCoord[10];
        int count = ToolPaths.BuildLPath(new TileCoord(0, 0), new TileCoord(2, 1), false, path);
        count.ShouldBe(4);
        path[0].ShouldBe(new TileCoord(0, 0));
        path[1].ShouldBe(new TileCoord(1, 0));
        path[2].ShouldBe(new TileCoord(2, 0));
        path[3].ShouldBe(new TileCoord(2, 1));
    }

    [Fact]
    public void LPath_ElbowFlipRunsVerticalFirst()
    {
        Span<TileCoord> path = stackalloc TileCoord[10];
        int count = ToolPaths.BuildLPath(new TileCoord(0, 0), new TileCoord(2, 1), true, path);
        count.ShouldBe(4);
        path[0].ShouldBe(new TileCoord(0, 0));
        path[1].ShouldBe(new TileCoord(0, 1));
        path[2].ShouldBe(new TileCoord(1, 1));
        path[3].ShouldBe(new TileCoord(2, 1));
    }

    [Fact]
    public void LPath_ClickWithoutDrag_IsOneTile()
    {
        Span<TileCoord> path = stackalloc TileCoord[4];
        int count = ToolPaths.BuildLPath(new TileCoord(5, 5), new TileCoord(5, 5), false, path);
        count.ShouldBe(1);
        path[0].ShouldBe(new TileCoord(5, 5));
    }

    [Fact]
    public void NormalizeRect_PutsMinFirst()
    {
        ToolPaths.NormalizeRect(new TileCoord(5, 1), new TileCoord(2, 4), out TileCoord min, out TileCoord max);
        min.ShouldBe(new TileCoord(2, 1));
        max.ShouldBe(new TileCoord(5, 4));
    }

    [Fact]
    public void FootprintOverlaps_DetectsHitAndMiss()
    {
        ToolPaths.FootprintOverlaps(new TileCoord(0, 0), 2, 2, new TileCoord(1, 1), new TileCoord(3, 3)).ShouldBeTrue();
        ToolPaths.FootprintOverlaps(new TileCoord(0, 0), 2, 2, new TileCoord(5, 5), new TileCoord(6, 6)).ShouldBeFalse();
    }
}
