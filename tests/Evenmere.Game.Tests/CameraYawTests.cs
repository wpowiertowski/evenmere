using Evenmere.Game.Rendering.Camera;

namespace Evenmere.Game.Tests;

/// <summary>M1-32: the yaw arithmetic keeps the 30° isometric-style offset through targets, wraps and settles.</summary>
public sealed class CameraYawTests
{
    private const float OffsetDegrees = 30f;
    private const float Tolerance = 1e-5f;

    [Fact]
    public void TargetYaw_StepsZeroToThree_AddsQuarterTurnsToOffset()
    {
        float offset = CameraMath.YawOffsetRadians(OffsetDegrees);
        for (int steps = 0; steps < 4; steps++)
        {
            CameraMath.TargetYawRadians(steps, OffsetDegrees).ShouldBe(offset + (steps * CameraMath.QuarterTurn), Tolerance);
        }
    }

    [Fact]
    public void TargetYaw_StepZero_IsThirtyDegrees()
    {
        CameraMath.TargetYawRadians(0, OffsetDegrees).ShouldBe(MathF.PI / 6f, Tolerance);
    }

    [Fact]
    public void Quadrant_NegativeSteps_Wraps()
    {
        CameraMath.QuadrantForSteps(-1).ShouldBe(3);
        CameraMath.QuadrantForSteps(-4).ShouldBe(0);
        CameraMath.QuadrantForSteps(-5).ShouldBe(3);
        CameraMath.QuadrantForSteps(5).ShouldBe(1);
    }

    [Fact]
    public void TargetYaw_NegativeStep_MatchesSettledQuadrantModuloFullTurn()
    {
        float target = CameraMath.TargetYawRadians(-1, OffsetDegrees);
        float settled = CameraMath.SettledYawRadians(CameraMath.QuadrantForSteps(-1), OffsetDegrees);
        (settled - target).ShouldBe(2f * MathF.PI, Tolerance);
    }

    [Fact]
    public void SettledYaw_KeepsOffsetForEveryQuadrant()
    {
        float offset = CameraMath.YawOffsetRadians(OffsetDegrees);
        for (int quadrant = 0; quadrant < 4; quadrant++)
        {
            CameraMath.SettledYawRadians(quadrant, OffsetDegrees).ShouldBe(offset + (quadrant * CameraMath.QuarterTurn), Tolerance);
        }
    }

    [Fact]
    public void RotateByYaw_PanUpAtThirtyDegrees_MovesAwayFromCamera()
    {
        float yaw = CameraMath.TargetYawRadians(0, OffsetDegrees);
        (float x, float z) = CameraMath.RotateByYaw(0f, -1f, yaw);

        // The camera sits at (sin 30°, cos 30°) relative to the focus, so screen-up
        // (away from the camera) is the opposite ground direction.
        x.ShouldBe(-0.5f, Tolerance);
        z.ShouldBe(-MathF.Sqrt(3f) / 2f, Tolerance);
    }

    [Fact]
    public void RotateByYaw_PanRightAtThirtyDegrees_MovesAlongCameraRight()
    {
        float yaw = CameraMath.TargetYawRadians(0, OffsetDegrees);
        (float x, float z) = CameraMath.RotateByYaw(1f, 0f, yaw);

        x.ShouldBe(MathF.Sqrt(3f) / 2f, Tolerance);
        z.ShouldBe(-0.5f, Tolerance);
    }
}
