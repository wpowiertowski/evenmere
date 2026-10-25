using Evenmere.Game.Rendering.Camera;
using Evenmere.Game.Ui.Common;

namespace Evenmere.Game.Tests;

/// <summary>M2-31: the vertical field of view grows with the window height, so one tile covers the same number of
/// logical points at any window size and a bigger window shows more island.</summary>
public sealed class CameraFovTests
{
    private const float Tight = 0.001f;
    private const float Loose = 0.2f;

    [Fact]
    public void VerticalFov_ReferenceHeight_IsFortyFive()
    {
        CameraMath.VerticalFovDegrees(900f).ShouldBe(45f, Tight);
    }

    [Fact]
    public void VerticalFov_TallerWindow_IsMore()
    {
        CameraMath.VerticalFovDegrees(1024f).ShouldBe(50.5f, Loose);
        CameraMath.VerticalFovDegrees(1600f).ShouldBe(72.7f, Loose);
    }

    [Fact]
    public void VerticalFov_ShorterWindow_IsLess()
    {
        float fov = CameraMath.VerticalFovDegrees(800f);
        fov.ShouldBeLessThan(45f);
        fov.ShouldBe(40.5f, Loose);
    }

    [Fact]
    public void VerticalFov_PixelTerms_RetinaDesignHeight_IsFortyFive()
    {
        // 1800 px at scale 2 is the 900-point reference height: the reference 45-degree view.
        CameraMath.VerticalFovDegrees(UiScale.PointsFromPixels(1800f, 2f)).ShouldBe(45f, Tight);
    }

    [Fact]
    public void VerticalFov_PixelTerms_RetinaMinimumHeight_IsLess()
    {
        // 1600 px at scale 2 is the 800-point minimum height.
        CameraMath.VerticalFovDegrees(UiScale.PointsFromPixels(1600f, 2f)).ShouldBe(40.5f, Loose);
    }

    [Fact]
    public void VerticalFov_PixelTerms_DesignHeightAtScale1_IsFortyFive()
    {
        CameraMath.VerticalFovDegrees(UiScale.PointsFromPixels(900f, 1f)).ShouldBe(45f, Tight);
    }

    [Fact]
    public void VerticalFov_ClampsHold()
    {
        CameraMath.VerticalFovDegrees(100f).ShouldBe(30f, Tight);
        CameraMath.VerticalFovDegrees(4000f).ShouldBe(75f, Tight);
    }

    [Fact]
    public void VerticalFov_BetweenClamps_RisesMonotonically()
    {
        float previous = float.NegativeInfinity;
        for (int h = 600; h <= 1600; h += 50)
        {
            float fov = CameraMath.VerticalFovDegrees(h);
            fov.ShouldBeGreaterThan(previous);
            previous = fov;
        }
    }
}
