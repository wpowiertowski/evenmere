using Evenmere.Game.Ui.Common;

namespace Evenmere.Game.Tests;

/// <summary>M2-31: the UI keeps its 1600 × 900 design size in logical points at any window size.</summary>
public sealed class UiScaleTests
{
    private const float Tolerance = 0.0001f;

    [Fact]
    public void ContentScale_DesignSizeAtScale1_IsOne()
    {
        UiScale.ContentScale(1600f, 900f, 1f, 1f).ShouldBe(1f, Tolerance);
    }

    [Fact]
    public void ContentScale_DesignSizeAtScale2_IsTwo()
    {
        UiScale.ContentScale(1600f, 900f, 2f, 1f).ShouldBe(2f, Tolerance);
    }

    [Fact]
    public void ContentScale_DesignSize_SamePointsSizeAtBothScales()
    {
        // The content scale counts physical pixels, so dividing back out the HiDPI scale — the size in points the
        // player sees — is the design size at both scales.
        (UiScale.ContentScale(1600f, 900f, 1f, 1f) / 1f).ShouldBe(1f, Tolerance);
        (UiScale.ContentScale(1600f, 900f, 2f, 1f) / 2f).ShouldBe(1f, Tolerance);
    }

    [Fact]
    public void ContentScale_MaximizedWindow_KeepsOneNotStretch()
    {
        // About 1821 × 1024 points: the old canvas_items stretch grew everything to ~1.14; the policy keeps 1.0.
        UiScale.ContentScale(1821f, 1024f, 1f, 1f).ShouldBe(1f, Tolerance);
        UiScale.ContentScale(1821f, 1024f, 2f, 1f).ShouldBe(2f, Tolerance);
    }

    [Fact]
    public void ContentScale_MinimumWindow_IsPointEight()
    {
        UiScale.ContentScale(1280f, 800f, 1f, 1f).ShouldBe(0.8f, Tolerance);
    }

    [Fact]
    public void ContentScale_UiScaleSetting_Multiplies()
    {
        UiScale.ContentScale(1600f, 900f, 1f, 1.5f).ShouldBe(1.5f, Tolerance);
        UiScale.ContentScale(1600f, 900f, 2f, 0.75f).ShouldBe(1.5f, Tolerance);
    }

    [Fact]
    public void PointsFromPixels_RetinaHalvesThePixels()
    {
        UiScale.PointsFromPixels(3200f, 2f).ShouldBe(1600f, Tolerance);
        UiScale.PointsFromPixels(1800f, 2f).ShouldBe(900f, Tolerance);
        UiScale.PointsFromPixels(2560f, 2f).ShouldBe(1280f, Tolerance);
        UiScale.PointsFromPixels(1600f, 2f).ShouldBe(800f, Tolerance);
    }

    [Fact]
    public void PointsFromPixels_Scale1OrInvalid_KeepsPixels()
    {
        UiScale.PointsFromPixels(1600f, 1f).ShouldBe(1600f, Tolerance);
        UiScale.PointsFromPixels(1600f, 0f).ShouldBe(1600f, Tolerance);
        UiScale.PointsFromPixels(1600f, -2f).ShouldBe(1600f, Tolerance);
    }

    [Fact]
    public void ContentScale_PixelTerms_RetinaDesignSize_IsTwo()
    {
        // 3200 x 1800 px at scale 2 is the 1600 x 900-point design size: same points size, twice the pixels.
        float w = UiScale.PointsFromPixels(3200f, 2f);
        float h = UiScale.PointsFromPixels(1800f, 2f);
        UiScale.ContentScale(w, h, 2f, 1f).ShouldBe(2f, Tolerance);
    }

    [Fact]
    public void ContentScale_PixelTerms_RetinaMinimum_IsOnePointSix()
    {
        // 2560 x 1600 px at scale 2 is the 1280 x 800-point minimum window: 0.8 window factor times scale 2.
        float w = UiScale.PointsFromPixels(2560f, 2f);
        float h = UiScale.PointsFromPixels(1600f, 2f);
        UiScale.ContentScale(w, h, 2f, 1f).ShouldBe(1.6f, Tolerance);
    }

    [Fact]
    public void ContentScale_PixelTerms_LargeWindowAtScale1_IsOne()
    {
        // 3642 x 2049 px at scale 1 is past the design size in points too: the window factor clamps to 1.0.
        float w = UiScale.PointsFromPixels(3642f, 1f);
        float h = UiScale.PointsFromPixels(2049f, 1f);
        UiScale.ContentScale(w, h, 1f, 1f).ShouldBe(1f, Tolerance);
    }

    [Fact]
    public void WindowFactor_BelowDesignSize_ShrinksToMinimum()
    {
        UiScale.WindowFactor(1280f, 800f).ShouldBe(0.8f, Tolerance);
        UiScale.WindowFactor(800f, 600f).ShouldBe(0.8f, Tolerance);
    }

    [Fact]
    public void ContentScale_PixelTerms_DesignSizeAtScale1_IsOne()
    {
        float w = UiScale.PointsFromPixels(1600f, 1f);
        float h = UiScale.PointsFromPixels(900f, 1f);
        UiScale.ContentScale(w, h, 1f, 1f).ShouldBe(1f, Tolerance);
    }

    [Fact]
    public void WindowFactor_AboveDesignSize_StaysAtOne()
    {
        UiScale.WindowFactor(1821f, 1024f).ShouldBe(1f, Tolerance);
        UiScale.WindowFactor(2560f, 1600f).ShouldBe(1f, Tolerance);
    }
}
