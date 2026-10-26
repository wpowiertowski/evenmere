using Evenmere.Game.Rendering.Camera;

namespace Evenmere.Game.Tests;

/// <summary>M2-29: the yaw offset blends from the street-level angle to the island-view angle with zoom, along the
/// same smooth step the pitch uses, so zooming out gradually turns the view toward the reference's angle.</summary>
public sealed class CameraYawBlendTests
{
    private const float MinZoom = 8f;
    private const float MaxZoom = 220f;
    private const float InitialZoom = 70f;
    private const float Tolerance = 1e-4f;

    [Fact]
    public void YawBlend_AtMinZoom_IsThirtyDegrees()
    {
        CameraMath.BlendedYawOffsetDegrees(MinZoom, MinZoom, MaxZoom).ShouldBe(30f, Tolerance);
    }

    [Fact]
    public void YawBlend_AtMaxZoom_IsFortyFiveDegrees()
    {
        CameraMath.BlendedYawOffsetDegrees(MaxZoom, MinZoom, MaxZoom).ShouldBe(45f, Tolerance);
    }

    [Fact]
    public void YawBlend_AtInitialZoom_SitsNearFortyOneDegrees()
    {
        float yaw = CameraMath.BlendedYawOffsetDegrees(InitialZoom, MinZoom, MaxZoom);

        // Log-space fraction ln(70/8)/ln(220/8) ≈ 0.654, eased by the smooth step to ≈ 0.724.
        yaw.ShouldBe(40.9f, 0.1f);
    }

    [Fact]
    public void YawBlend_BelowMinZoom_ClampsToThirtyDegrees()
    {
        CameraMath.BlendedYawOffsetDegrees(MinZoom / 2f, MinZoom, MaxZoom).ShouldBe(30f, Tolerance);
    }

    [Fact]
    public void YawBlend_AboveMaxZoom_ClampsToFortyFiveDegrees()
    {
        CameraMath.BlendedYawOffsetDegrees(MaxZoom * 2f, MinZoom, MaxZoom).ShouldBe(45f, Tolerance);
    }

    [Fact]
    public void YawBlend_BetweenEnds_RisesMonotonically()
    {
        float previous = float.NegativeInfinity;
        for (int i = 0; i <= 20; i++)
        {
            float zoom = MinZoom * MathF.Pow(MaxZoom / MinZoom, i / 20f);
            float yaw = CameraMath.BlendedYawOffsetDegrees(zoom, MinZoom, MaxZoom);
            yaw.ShouldBeGreaterThan(previous);
            previous = yaw;
        }
    }
}
