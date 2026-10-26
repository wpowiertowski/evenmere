using Evenmere.Game.Rendering.Camera;

namespace Evenmere.Game.Tests;

/// <summary>M2-29: the orthographic size grows linearly with the window height, so one tile covers the same number of
/// logical points at any window size and a bigger window shows more island — the same rule as the perspective field
/// of view (<see cref="CameraFovTests"/>), without the distortion clamp.</summary>
public sealed class CameraOrthoTests
{
    private const float Tight = 0.001f;
    private const float Loose = 0.05f;

    [Fact]
    public void OrthographicSize_ReferenceHeight_MatchesPerspectiveSpan()
    {
        // At the 900-point reference height the ortho span equals the perspective span at the focus point:
        // 2 x zoom x tan(22.5 degrees).
        float expected = 2f * 70f * MathF.Tan(22.5f * (MathF.PI / 180f));
        CameraMath.OrthographicSize(70f, 900f).ShouldBe(expected, Tight);
    }

    [Fact]
    public void OrthographicSize_DoublingHeight_DoublesSpan()
    {
        // The card's rule: doubling the window height in points doubles the visible span at the same zoom.
        float half = CameraMath.OrthographicSize(70f, 800f);
        CameraMath.OrthographicSize(70f, 1600f).ShouldBe(2f * half, Tight);
    }

    [Fact]
    public void OrthographicSize_ScalesWithZoom()
    {
        float near = CameraMath.OrthographicSize(8f, 900f);
        CameraMath.OrthographicSize(16f, 900f).ShouldBe(2f * near, Tight);
    }

    [Fact]
    public void OrthographicSize_TallerWindow_IsMore()
    {
        float at800 = CameraMath.OrthographicSize(70f, 800f);
        float at900 = CameraMath.OrthographicSize(70f, 900f);
        at800.ShouldBeLessThan(at900);
        at900.ShouldBe(58f, Loose);
        at800.ShouldBe(51.55f, Loose);
    }

    [Fact]
    public void OrthographicSize_Unclamped_GrowsPastPerspectiveClamp()
    {
        // Perspective clamps at 75 degrees; the ortho size keeps growing linearly instead.
        float at1600 = CameraMath.OrthographicSize(70f, 1600f);
        float linear = CameraMath.OrthographicSize(70f, 900f) * (1600f / 900f);
        at1600.ShouldBe(linear, Tight);
        at1600.ShouldBeGreaterThan(100f);
    }

    [Fact]
    public void OrthographicSize_RisesMonotonically()
    {
        float previous = float.NegativeInfinity;
        for (int h = 600; h <= 2400; h += 100)
        {
            float size = CameraMath.OrthographicSize(70f, h);
            size.ShouldBeGreaterThan(previous);
            previous = size;
        }
    }

    [Fact]
    public void OrthographicSize_NonPositive_IsZero()
    {
        CameraMath.OrthographicSize(0f, 900f).ShouldBe(0f, Tight);
        CameraMath.OrthographicSize(-5f, 900f).ShouldBe(0f, Tight);
        CameraMath.OrthographicSize(70f, 0f).ShouldBe(0f, Tight);
        CameraMath.OrthographicSize(70f, -100f).ShouldBe(0f, Tight);
    }
}
